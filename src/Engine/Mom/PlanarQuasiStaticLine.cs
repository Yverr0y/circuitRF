// QSC — γ AND Z_c FOR A CALIBRATION STANDARD, FROM ITS OWN ELECTROSTATICS
//
// ══════════════════════════════════════════════════════════════════════════════════════════════
// WHY THIS EXISTS, AND WHAT IT IS NOT
// ══════════════════════════════════════════════════════════════════════════════════════════════
//
// D5 MEASURES γ from two full-wave line standards, and that is the right instrument at the top of a
// band: it captures dispersion, the port's own discontinuity and higher-order effects with no
// quasi-TEM assumption. What it costs is 1/f_lo, because the two lines' length difference has to sit
// inside TRL's usable interval βΔℓ ∈ [20°, 160°]. On a 3.8 mm microstrip swept from 100 MHz that
// bought a 161.5 mm standard — 42× the DUT, 79,055 unknowns against a 12,000 ceiling — and the run
// refused. `RESOLVED.md` §RAW1 §6.
//
// **Supplying γ does NOT by itself remove the long standards, and that is the trap this file's brief
// was written to close.** γ is ALREADY an input to `PlanarDeembed.SolveErrorBox`; the error box still
// needs TWO lines, because one line gives two complex equations (M₁₁, M₂₁) for three complex unknowns
// (a₁₁, a₂₁², a₂₂) and no reciprocity or symmetry argument closes that gap.
//
// **What a known γ buys is that Δℓ no longer has to be ELECTRICALLY long.** The [20°, 160°] interval
// protects the γ EXTRACTION, where `Acosh`'s branch structure makes βΔℓ = nπ a genuine singularity
// and where (per `PlanarCalibration.Gamma`'s own header) α is two orders below β and its extracted
// sign is noise. The error box's own conditioning degrades as 1/Δℓ — ordinary conditioning, not a
// denominator zero — so Δℓ can be sized from the SUBSTRATE and the mesh instead of from the
// wavelength, and both standards become small and frequency-independent.
//
// ══════════════════════════════════════════════════════════════════════════════════════════════
// R-qsc-2 — THE STANDARD'S OWN ELECTROSTATICS, NOT `RlgcExtractor`, AND IT IS NOT A PREFERENCE
// ══════════════════════════════════════════════════════════════════════════════════════════════
//
// The two candidate suppliers are kernel A's per-unit-length cross-section solve
// (`RlgcExtractor.Extract`) and the standard's own electrostatics. They are not the same quantity:
// the first describes the DUT's DRAWN cross-section, the second describes the STANDARD — the object
// the error box is actually solved from. Four reasons the second wins, and the first is structural:
//
//   • **`PlanarDeembed`'s own D7 rule already says so**: "Kernel A is the ORACLE for Z_c, never an
//     input. Reading Z_c or C_pul off QuasiStaticKernel and feeding it into B would make the phase
//     table's own 'A and B agree on a uniform line' gate a tautology and would import A's
//     discretisation error into B's answer." A γ taken from A is the same sentence one quantity over.
//   • **De-embedding is ALREADY part quasi-static**: `PlanarDeembed.CapacitancePerMetre` runs an
//     electrostatic solve on the two standards and supplies C_pul for Z_c = γ/(jωC_pul). This adds
//     ONE further solve of the same kind — the same geometry with the dielectric removed — and reads
//     L = μ₀ε₀/C₀ off it. Nothing new is introduced; an existing route is extended by its other half.
//   • **Kernel A cannot represent what a standard can.** A coplanar standard's C_pul is a MODAL
//     capacitance (RP-2c), a widened one carries a FLOATING conductor (PCAL3), and a group's is a
//     MATRIX (PCAL4/D7'). All three ride on `PlanarStandard`'s own `ModePotential`/`ModeWeight`/
//     `FloatingPotential` and are honoured here for free, because this is the same solve. Kernel A
//     would need a cross-section it does not have, and a WIDTH that is not defined for a feed that
//     is not a plain rectangle.
//   • **It is self-consistent in the discretisation.** C and C₀ come off the SAME mesh, so
//     ε_eff = C/C₀ and Z_c = √(L/C) share their discretisation error and it cancels to first order in
//     the ratio — which is exactly the argument D7 already makes for DIFFERENCING two standards
//     rather than solving one.
//
// ══════════════════════════════════════════════════════════════════════════════════════════════
// THE ARITHMETIC
// ══════════════════════════════════════════════════════════════════════════════════════════════
//
//     C   = C′ − jC″   from the standards' differencing with the real dielectric (complex ε*)
//     C₀  =            the same differencing with every dielectric replaced by vacuum
//     L   = μ₀ε₀/C₀    the quasi-TEM identity — `RlgcExtractor`'s own [L] = μ₀ε₀[C₀]⁻¹, N = 1
//     γ   = jω√(LC)    so α = ω√(LC′)·tanδ_eff/2 and β = ω√(LC′) fall out together
//     Z_c = γ/(jωC′)   D7's OWN spelling, unchanged — only γ's source changed
//
// **The metal is a PERFECT CONDUCTOR here, exactly as it is in the full-wave fill by default**
// (CL1: `PlanarFillSettings.ConductorLoss` is off), so γ carries the DIELECTRIC's attenuation and no
// conductor term. A conductor term taken from Wheeler would be a claim kernel B's own matrix does not
// make, and the two would disagree across the crossover in α by whatever the sheet model is worth.
//
// **C stays COMPLEX all the way to γ and that is not decoration.** `PlanarKernelTerms.StaticScalar`
// is built on `GroundedSlab.EpsComplex`, so the charge already carries tanδ; D7 drops it with a
// `.Real` because Z_c takes a double. Dropping it here would publish α = 0 on a lossy board — a
// plausible, wrong number, and one that would read as a regression against the measured path beside
// it in the same `.npy`.

using System.Numerics;

namespace CircuitRF.Engine.Mom;

/// <summary>
/// <b>One calibration standard pair's quasi-TEM description</b> — the per-unit-length C (complex,
/// with the dielectric) and L (from the air-filled solve), and the γ and Z_c that follow. Built
/// ONCE per calibrator and reused at every frequency below the crossover, exactly as D7's own C_pul
/// is: the matrices are frequency-independent (R-mom-11's rule, one kernel over).
/// </summary>
/// <param name="CComplexPerMetre">C′ − jC″ per metre. <see cref="CPerMetre"/> is its real part and is
/// what D7's Z_c divides by.</param>
/// <param name="C0PerMetre">The same differencing with every dielectric replaced by vacuum — the ONLY
/// route to [L] that does not import a second kernel's answer.</param>
public sealed record PlanarQuasiStaticLine(Complex CComplexPerMetre, double C0PerMetre)
{
    /// <summary>C′ — F/m, the real part, which is what <see cref="PlanarDeembed.CharacteristicImpedance"/>
    /// takes and what the <c>Cpul</c> diagnostic cube publishes.</summary>
    public double CPerMetre => CComplexPerMetre.Real;

    /// <summary>L = μ₀ε₀/C₀ — H/m. The quasi-TEM identity, N = 1.</summary>
    public double LPerMetre => EmConstants.Mu0 * EmConstants.Eps0 / C0PerMetre;

    /// <summary>ε_eff = C′/C₀, the number M1's tables are read in.</summary>
    public double EffectivePermittivity => CComplexPerMetre.Real / C0PerMetre;

    /// <summary>
    /// γ = jω√(LC) at one frequency. β &gt; 0 and α ≥ 0 come out of the principal square root without
    /// a branch decision — which is the whole point of not extracting it: there is no acosh here, no
    /// 2π ambiguity, and nothing to continue from the previous frequency.
    /// </summary>
    public Complex GammaAt(double fHz)
    {
        double omega = 2.0 * Math.PI * fHz;
        return Complex.ImaginaryOne * omega * Complex.Sqrt(LPerMetre * CComplexPerMetre);
    }

    /// <summary>
    /// <b>The same reading as <see cref="PlanarCalibration.Gamma"/> returns</b>, so the two paths
    /// hand the sweep one type and every diagnostic downstream reads one field.
    ///
    /// <para><b><see cref="PlanarCalibration.GammaResult.Usable"/> is TRUE here whatever βΔℓ comes
    /// out at, and that is the finding rather than a relaxation.</b> The [20°, 160°] interval is
    /// drawn around D5/D6's shared denominator zero at βΔℓ = nπ — it exists to protect the
    /// EXTRACTION of γ from `½·tr(M)`, which is not performed on this path at all. The error box's
    /// own conditioning is a different and far gentler thing, and it is gated separately on the a₂₂
    /// sign margin (<see cref="PlanarErrorBox.RejectedResidual"/>) rather than on an angle.
    /// Reporting the realised angle anyway is deliberate: it is the number that says how short the
    /// standards actually are.</para>
    ///
    /// <para><see cref="PlanarCalibration.GammaResult.Unwrapped"/> is 0 by construction — there is
    /// no principal branch to add 2π to.</para>
    /// </summary>
    public PlanarCalibration.GammaResult ResultAt(double fHz, double deltaLM)
    {
        Complex g = GammaAt(fHz);
        return new PlanarCalibration.GammaResult(
            g, g.Imaginary * deltaLM * 180.0 / Math.PI, 0, true);
    }

    /// <summary>
    /// <b>Extract the pair from the two EXTREME standards, by the same differencing D7 already
    /// does.</b> Two electrostatic solves with the dielectric and two without; the ones WITH it are
    /// the identical call <see cref="PlanarDeembed.CapacitancePerMetre"/> makes, so a run whose band
    /// never reaches the quasi-static path pays for none of this and a run that does pays for the
    /// air-filled half only.
    ///
    /// <para>Both standards' <see cref="PlanarStandard.ModePotential"/>,
    /// <see cref="PlanarStandard.ModeWeight"/> and <see cref="PlanarStandard.FloatingPotential"/> ride
    /// along untouched, so a coplanar or widened standard measures the capacitance its own PORT's mode
    /// sees rather than the sheet's total — RP-2c and PCAL3's rules, inherited rather than restated.</para>
    /// </summary>
    public static PlanarQuasiStaticLine Extract(
        PlanarStandard shortStd, PlanarStandard longStd, GroundedSlab slab,
        PlanarFillSettings? settings = null,
        PlanarFillCores? shortCores = null, PlanarFillCores? longCores = null)
    {
        Complex c  = PlanarDeembed.CapacitancePerMetreComplex(
            shortStd, longStd, slab, settings, shortCores, longCores);
        Complex c0 = PlanarDeembed.CapacitancePerMetreComplex(
            shortStd, longStd, slab, settings, shortCores, longCores, airFilled: true);

        return Checked(c, c0);
    }

    /// <summary><b>MIM-4's medium, the same way.</b> Only the electrostatic kernel changes; the
    /// differencing, the end-effect cancellation and every guard are the ones above.</summary>
    public static PlanarQuasiStaticLine Extract(
        PlanarStandard shortStd, PlanarStandard longStd,
        LayerStack stack, double levelZ, double referenceHeightM,
        PlanarFillSettings? settings = null,
        PlanarFillCores? shortCores = null, PlanarFillCores? longCores = null,
        InteriorStaticModel? model = null)
    {
        Complex c  = PlanarDeembed.CapacitancePerMetreComplex(
            shortStd, longStd, stack, levelZ, referenceHeightM, settings, shortCores, longCores, model);
        Complex c0 = PlanarDeembed.CapacitancePerMetreComplex(
            shortStd, longStd, stack, levelZ, referenceHeightM, settings, shortCores, longCores,
            model, airFilled: true);

        return Checked(c, c0);
    }

    /// <summary>
    /// The one place both routes are sanity-checked, because a non-positive C₀ is not a small error:
    /// L = μ₀ε₀/C₀ would come back negative or infinite and γ = jω√(LC) would then be a REAL number,
    /// i.e. a line that attenuates without propagating — complete, smooth, and wrong.
    /// </summary>
    private static PlanarQuasiStaticLine Checked(Complex c, Complex c0)
    {
        if (!(c0.Real > 0) || !(c.Real > 0))
            throw new InvalidOperationException(
                $"The quasi-static port calibration differenced the two standards' static " +
                $"capacitances and got C = {c.Real:G4} F/m with the dielectric and " +
                $"{c0.Real:G4} F/m without it; both have to be positive for L = μ₀ε₀/C₀ and " +
                "γ = jω√(LC) to describe a propagating line. The two standards differ only in the " +
                "bulk cells between their reference planes, so a non-positive difference means the " +
                "electrostatic solve did not converge on one of them rather than that the geometry " +
                "is unusual.");

        return new PlanarQuasiStaticLine(c, c0.Real);
    }
}
