// CL7 — THE GROUND PLANE'S OWN RETURN CURRENT, SO THE QUASI-STATIC γ CARRIES A GROUND TERM
//
// ══════════════════════════════════════════════════════════════════════════════════════════════
// WHY THIS EXISTS
// ══════════════════════════════════════════════════════════════════════════════════════════════
//
// CL3 gave the quasi-static port calibration's γ a CONDUCTOR term, read off the standards' own
// electrostatic charge:
//
//     R = Σ_levels Re(Z_s(ω, σ, t)) · [ ΔS_level · Δℓ / |ΔT|² ] ,   S = Σ_cells |q|²/A
//
// That sum is over MESHED levels. CL7 makes the ground plane a real conductor, and the plane is not
// a meshed level: it is laterally infinite, unmeshed, and has no ΔS of its own. Left alone, a GaAs
// line's published S would carry a ground term at every frequency while the γ its error box is
// solved against carried one at none of them — `RESOLVED.md` §CL3 §0's problem, one surface over,
// and on the shipped MMIC technology the quasi-static path is the WHOLE band (crossover 26.07 GHz).
//
// **The plane has no mesh, but the same solve determines its return current completely.** The
// strip's charge vector q — the one D7's C_pul differencing already computes — is the strip's own
// current up to the TEM factor v_p that cancels in the ratio, and a horizontal current at height h
// over a ground plane has ONE image. Re(Z_s) is CL6's floor (`Termination.SurfaceImpedanceAt` →
// `PlanarSurfaceImpedance.Plane`), not a fourth spelling of a surface impedance. So the whole of
// CL3 §1's argument transfers: this is the SAME model the fill loaded, read off the SAME solve and
// the SAME mesh, which is what makes the two agree across the crossover by construction rather than
// by calibration.
//
// ══════════════════════════════════════════════════════════════════════════════════════════════
// THE KERNEL, AND THE ONE THING THAT MAKES IT THE RIGHT ONE
// ══════════════════════════════════════════════════════════════════════════════════════════════
//
// The loss on the plane is ½Re(Z_s)∫|K_g|²dA, so what is needed is the plane's CURRENT density, not
// its charge density. **In an inhomogeneous line those are two different distributions, and using
// the wrong one over-reads the ground term by 1.56× on FR-4 and 1.87× on GaAs** — measured on the
// real standards, not argued, which is why it is written out here rather than left as a choice (see
// the rejected candidate below).
//
// The MAGNETOSTATIC problem is the one that sets the return current, and it does not see the
// dielectric: µᵣ is 1 everywhere, so a horizontal current filament I at height h over a PEC plane
// has exactly ONE negative image at −h, for every εᵣ. That is `StaticGreens.VectorPotential`'s own
// sentence, one quantity over. In the spectral domain the plane's induced current density is
//
//     K̂_g(k) = −I · e^{−kh}                                (and K̂_g(0) = −I, which is the return)
//
// and Parseval turns ∫|K_g|²dA into a pair sum over the SAME cells with a CLOSED-FORM kernel:
//
//     ∫ K_i K̄_j dA = q_i q̄_j · K(d_ij) ,     K(d) = h / (π (4h² + d²)^{3/2})
//
// using ∫₀^∞ e^{−ak}J₀(kd) k dk = a/(a²+d²)^{3/2} with a = 2h — the interaction of a source with the
// other source's image, at the image separation √(d² + 4h²). Two oracles fall out and both are
// asserted rather than believed (`GroundReachesAUserTests`): ∫K(d) d²ρ = 1, which is the statement
// that the plane returns the whole current; and a uniform FILAMENT integrates to R = Re(Z_s)/(2πh),
// the textbook closed form, which is also a strict UPPER bound on any distributed strip — spreading
// the source can only spread the return.
//
// ══════════════════════════════════════════════════════════════════════════════════════════════
// THE REJECTED CANDIDATE, REJECTED BY NUMBER AS WELL AS BY ARGUMENT
// ══════════════════════════════════════════════════════════════════════════════════════════════
//
// The plane's ELECTROSTATIC induced charge is a different and equally computable distribution:
//
//     σ̂_g(k) = −ε*/(sinh kh + ε* cosh kh) = −A₀ Σ_{n≥0} Γⁿ e^{−(2n+1)kh} ,
//     A₀ = 2ε*/(1+ε*) ,  Γ = (1−ε*)/(1+ε*)
//
// — an image ladder at the same ratio |Γ| that `StaticGreens.ScalarPotential` sums (0.630 on FR-4,
// 0.856 on GaAs). It is MORE CONCENTRATED than the return current, by exactly A₀ for a filament:
// **1.630 on FR-4 and 1.856 on GaAs**, measured at d = 0 and reproduced on the standards. Using it
// puts the supplier at **1.53× kernel A's** ground term on FR-4 at 2 GHz, where the magnetostatic
// one sits at 0.99×. <see cref="ElectrostaticImageCoefficients"/> is kept so
// that comparison stays reproducible, on `PlanarFillSettings.PerfectConductor`'s own rule: a
// measurement whose rejected alternative cannot be recomputed is not a measurement.
//
// **This is the cheapest statement of why a ground term is not a strip term.** On the STRIP, CL3
// reads ∫|K|² off the charge and it tracks the fill to 0.96-1.15×, because the strip's current and
// charge are supported on the same narrow metal and share its edge singularity. On the PLANE nothing
// confines either one and the spreading is entirely medium-determined — so the electric and magnetic
// problems part company, and which one is asked matters at the tens of per cent.
//
// ══════════════════════════════════════════════════════════════════════════════════════════════
// WHAT THIS DOES NOT COVER, SAID HERE RATHER THAN DISCOVERED
// ══════════════════════════════════════════════════════════════════════════════════════════════
//
// **One image, so ONE ground plane at ONE height.** That is exactly D2's geometry and exactly what a
// calibration standard is (`PlanarSolve._standardLevels` — a standard is always a single-level
// uniform line), so the one-slab electrostatic route has everything it needs. MIM-4's interior route
// puts the standard at a level inside a stratified stack, where the magnetostatic problem is still
// ε-free but the SOURCE height is not the slab's and a µᵣ ≠ 1 layer would add a ladder of its own. On
// that route the quasi-static γ carries NO ground term and
// `PlanarQuasiStaticLine.GroundTermSupplied` says so, so a caller can report the residue instead of
// assuming it away. `RESOLVED.md` §CL7 carries its measured size.

using System.Numerics;

namespace CircuitRF.Engine.Mom;

/// <summary>
/// <b>CL7 — ∫|K_g|² over a laterally infinite ground plane, from the strip current that returns
/// through it.</b> The ground's counterpart of <c>PlanarQuasiStaticLine.SecondMoment</c>, in the same
/// units (C²/m², the TEM factor v_p cancelling in the ratio) and read off the same charge vector, so
/// the two enter <c>R = Re(Z_s)·ΔS·Δℓ/|ΔT|²</c> the same way. See this file's header for the
/// derivation, for the candidate it rejects and for what it does not cover.
/// </summary>
public static class PlanarGroundReturn
{
    /// <summary>
    /// <c>K(d) = h/(π(4h²+d²)^{3/2})</c> — the return-current interaction of two source cells a
    /// lateral distance <paramref name="dM"/> apart, per unit of each one's current. Positive,
    /// symmetric, largest at d = 0, and independent of εᵣ, which is the whole finding.
    /// </summary>
    public static double PairKernel(double heightM, double dM)
    {
        double r = 4.0 * heightM * heightM + dM * dM;
        return heightM / (Math.PI * r * Math.Sqrt(r));
    }

    /// <summary>
    /// <b><c>S_ground = ∫|K_g|² dA = Σ_ij q_i q̄_j K(d_ij)</c></b> — the quantity
    /// <c>R = Re(Z_s)·ΔS·Δℓ/|ΔT|²</c> differences between the two standards.
    ///
    /// <para>The form is Hermitian and positive definite (it is Parseval's identity for a real
    /// integral), so the sum is real by construction and the imaginary part is dropped rather than
    /// carried. The pair loop is folded on the symmetry of K.</para>
    ///
    /// <para><b>Every cell is taken at the slab's top surface</b>, which is where a calibration
    /// standard's metal is — a standard is always a single-level uniform line
    /// (<c>PlanarSolve._standardLevels</c>), so there is no second height to carry and a mesh with
    /// one would be a standard this route never sees.</para>
    /// </summary>
    public static double SecondMoment(PlanarMesh mesh, Complex[] q, double heightM)
    {
        if (!(heightM > 0)) return 0;
        var cells = mesh.Cells;
        int n = Math.Min(q.Length, cells.Count);

        double self = PairKernel(heightM, 0.0), total = 0;
        for (int i = 0; i < n; i++)
        {
            var ci = cells[i];
            total += q[i].Magnitude * q[i].Magnitude * self;
            for (int j = i + 1; j < n; j++)
            {
                var cj = cells[j];
                double dx = ci.CentroidX - cj.CentroidX, dy = ci.CentroidY - cj.CentroidY;
                Complex cross = q[i] * Complex.Conjugate(q[j]);
                total += 2.0 * cross.Real * PairKernel(heightM, Math.Sqrt(dx * dx + dy * dy));
            }
        }
        return total;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // The rejected candidate, kept computable
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The image ladder's relative truncation, for the rejected electrostatic kernel.</summary>
    public const double ImageTolerance = 1e-14;

    /// <summary>Hard cap on that ladder, for a stack whose |Γ| is pathologically close to 1.</summary>
    public const int MaxImages = 20000;

    /// <summary>
    /// <b>The REJECTED candidate's coefficients — the plane's electrostatic induced CHARGE rather
    /// than its return current.</b> <c>c_{m−1}</c> scaled by the prefactor, so
    /// <see cref="ElectrostaticPairKernel"/> is one loop. Kept so that CL7's own decision stays
    /// reproducible; nothing in the shipped path calls it.
    /// </summary>
    public static double[] ElectrostaticImageCoefficients(Complex epsComplex, double heightM)
    {
        Complex gam = (1.0 - epsComplex) / (1.0 + epsComplex);
        Complex a0  = 2.0 * epsComplex / (1.0 + epsComplex);
        double  p   = a0.Magnitude * a0.Magnitude / (2.0 * Math.PI * 4.0 * heightM * heightM);
        double  gm  = gam.Magnitude;
        var c = new List<double>(64);

        // c_s = Γ·c_{s−1} + Γ̄^s, real by construction (a sum of conjugate pairs) — the imaginary part
        // is an exact zero and is dropped rather than accumulated. The ladder ALTERNATES in sign for
        // a real εᵣ (Γ < 0), so the loop ends on the tail BOUND |c_s| ≤ (s+1)|Γ|^s and not on the
        // term itself: a partial sum can pass through a small value with a large tail.
        Complex cs = Complex.One, gbar = Complex.Conjugate(gam), gbarPow = Complex.One;
        for (int s = 0; s < MaxImages; s++)
        {
            if (s > 0) { gbarPow *= gbar; cs = gam * cs + gbarPow; }
            c.Add(p * cs.Real);
            if (s > 0 && Math.Pow(gm, s) / ((s + 1) * Math.Max(1e-300, 1.0 - gm)) < ImageTolerance) break;
        }
        return [.. c];
    }

    /// <inheritdoc cref="ElectrostaticImageCoefficients"/>
    public static double ElectrostaticPairKernel(double[] coefficients, double heightM, double dM)
    {
        double u2 = dM * dM / (4.0 * heightM * heightM), sum = 0;
        for (int m = 1; m <= coefficients.Length; m++)
        {
            double r = m * m + u2;
            sum += coefficients[m - 1] * m / (r * Math.Sqrt(r));
        }
        return sum;
    }

    /// <inheritdoc cref="SecondMoment"/>
    public static double ElectrostaticSecondMoment(PlanarMesh mesh, Complex[] q,
                                                   Complex epsComplex, double heightM)
    {
        if (!(heightM > 0)) return 0;
        var c = ElectrostaticImageCoefficients(epsComplex, heightM);
        var cells = mesh.Cells;
        int n = Math.Min(q.Length, cells.Count);

        double self = ElectrostaticPairKernel(c, heightM, 0.0), total = 0;
        for (int i = 0; i < n; i++)
        {
            var ci = cells[i];
            total += q[i].Magnitude * q[i].Magnitude * self;
            for (int j = i + 1; j < n; j++)
            {
                var cj = cells[j];
                double dx = ci.CentroidX - cj.CentroidX, dy = ci.CentroidY - cj.CentroidY;
                Complex cross = q[i] * Complex.Conjugate(q[j]);
                total += 2.0 * cross.Real *
                         ElectrostaticPairKernel(c, heightM, Math.Sqrt(dx * dx + dy * dy));
            }
        }
        return total;
    }
}
