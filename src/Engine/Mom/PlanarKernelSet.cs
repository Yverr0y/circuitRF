// L9c / M5 — the Green's function of a MULTI-LEVEL problem is not one function, it is one per height
// PAIRING, and this is where that is organised.
//
// L8d's PlanarKernelPair holds exactly two PlanarKernelTerms because L8's D2 puts every source and
// every observer on one plane: the kernel is mesh-independent and height-independent, so one fit per
// component per frequency serves the DUT and every calibration standard. **Neither half of that
// survives more than one level**, and the two halves fail differently:
//
//   • MESH-independence SURVIVES. A pairing is (z, z′), not (cell, cell), so a fit is still shared
//     across the DUT and its standards. L8d's caching decision is unchanged.
//   • HEIGHT-independence does NOT. L9b's D5 shift covers a pair in the top half-space with no refit,
//     and L9c's M3 measured that the interior pairings are exact shifts too — but of FOUR families in
//     the source region's own k_zm, which is a different fit, not the same one shifted. So a pairing
//     that is not high–high needs Dcim.FitAtHeights.
//
// D7 projected "four kernel components × three height pairings ≈ 12 fits per frequency at L9b's
// measured ~0.1 s each ≈ 1.2 s". This fits LAZILY and counts, so the projection is checked rather
// than assumed: a two-level structure with one via has three heights of interest (two levels and the
// via's midpoint), six unordered pairings, and asks for far fewer than 4 × 6 because most components
// are never wanted at most pairings — G_A^zz only between two vias, the mixed one only between a via
// and a level.

using System.Numerics;

namespace CircuitRF.Engine.Mom;

/// <summary>
/// The z of each conductor level, and the two quantities a via basis needs from a pair of them.
///
/// <para><b>The via's z-integral is RESOLVED, not replaced</b> — <c>ViaZIntegral</c>, and see its
/// header for the split (the two asymptotes' static parts in closed form, everything else on a Gauss
/// rule in z). L9c evaluated the kernel once at the midpoint of the two feet and multiplied by ℓ;
/// L9e measured that the via's own inductance came out high by ≈ 0.673·(ℓ/w) and shipped a geometric
/// bound. <b>That bound is retired</b>: the same sweep re-run against the fill is flat to 0.13% over
/// ℓ/w ∈ [0.01, 5] and a 16× range of w (<c>ViaPhysicsTests.T3_1</c>).</para>
///
/// <para><b><see cref="MaxElectricalLength"/> stays, and it is now about a different thing.</b> It is
/// no longer the quadrature — it is the BASIS. L9c's via basis carries one z-rooftop per inter-level
/// gap, so its current is UNIFORM along the whole via; that is an electrical assumption with no
/// quadrature anywhere in it, and no z rule removes it. Splitting the via across intermediate levels
/// is what gives it a current profile, and that remains the remedy the refusal names.</para>
/// </summary>
/// <param name="GroundZ">
/// <b>Where the ground plane is — the stack's own bottom termination interface</b>, which
/// <c>LayerStack</c>'s stated convention puts at z = 0. A GROUND-ATTACHMENT basis spans
/// <c>GroundZ … Of(LayerIndex)</c>, so this is the one number that lets a half basis have a z extent
/// at all; every other basis ignores it.
/// </param>
public sealed record PlanarLevels(IReadOnlyList<double> Z, double GroundZ = 0.0)
{
    public static PlanarLevels From(PlanarProblem problem)
    {
        var z = new double[problem.Layers.Count];
        for (int i = 0; i < z.Length; i++) z[i] = problem.LevelZ(i);
        var stack = problem.EffectiveStack;
        return new PlanarLevels(z, stack.InterfaceZ.Count > 0 ? stack.InterfaceZ[0] : 0.0);
    }

    /// <summary>The height a HORIZONTAL basis on this level sits at.</summary>
    public double Of(int layerIndex) => Z[layerIndex];

    /// <summary>The midpoint of a VERTICAL basis's span. Kept because the kernel's own asymptote is
    /// asked for at a representative height (its coefficients do not depend on the heights at all);
    /// the ENTRY is no longer evaluated there — see the type's own note.</summary>
    public double MidOf(int lower) => 0.5 * (Z[lower] + Z[lower + 1]);

    /// <summary>ℓ — the via's length, which multiplies its z-integral.</summary>
    public double LengthOf(int lower) => Z[lower + 1] - Z[lower];

    /// <summary>The z extent a GROUND-ATTACHMENT basis on <paramref name="layerIndex"/> occupies —
    /// the plane to the metal. Its length is what multiplies that basis's own z-integral.</summary>
    public double AttachmentLengthOf(int layerIndex) => Z[layerIndex] - GroundZ;

    /// <summary>
    /// R-mom-17: the electrical length above which a via's current can no longer be taken as UNIFORM
    /// along it — a property of the BASIS (one z-rooftop per span), never of any quadrature.
    ///
    /// <para><b>0.05 was inherited from L9c and is ~20× tighter than what the effect measures.</b>
    /// M1's own R-gv-1 measurement, on an ATTACHED via — the only kind that exists in a real
    /// structure, and the kind a backside via is at BOTH ends — subdivided the same via into n
    /// segments and compared the reaction vᵀZ⁻¹v:</para>
    ///
    /// <list type="table">
    ///   <item><term>k·ℓ = 0.01</term><description>n = 1 → 8 moves the answer 0.062%</description></item>
    ///   <item><term>k·ℓ = 0.23</term><description>0.077% — §0.2 item 3's own number, 4.5× over the bound</description></item>
    ///   <item><term>k·ℓ = 0.50</term><description>0.172%</description></item>
    ///   <item><term>k·ℓ = 1.00</term><description>0.141%, and the current is 2.0% non-uniform</description></item>
    /// </list>
    ///
    /// <para>A FLOATING rod does move — 10.2% at n = 8, 28.5% non-uniform — but that movement is
    /// <b>98% static</b> (identical at k·ℓ = 0.01 and 0.23), i.e. it is the floating end condition,
    /// not electrical length. A via in a circuit is terminated at both ends and has no such freedom.
    /// <b>0.30 is therefore what this bound is set to</b>: past every measured point where the
    /// uniform-current basis is worth under 0.1%, and comfortably inside where the CHAIN that would
    /// fix it is affordable (M1 measured 14.2% of a de-embedded point at n = 8, growing ~4× per
    /// doubling — so it is not).</para>
    ///
    /// <para><b>Widening it unlocks nothing on its own</b>, and the refusal says so:
    /// <see cref="Dcim.ValidatedRhoOverLambdaAtHeights"/> = 0.1 on G_A^zz already restricts every
    /// via-bearing run to electrically small structures, and 1.0 is as far as that limit let M1
    /// measure on its own fixture. It is untouched.</para>
    /// </summary>
    public const double MaxElectricalLength = 0.30;

    /// <summary>
    /// <b>MIM-3 / MIM-8 — how large a CELL may be against the SEPARATION between two conductor
    /// levels before the cross-level block stops being the answer it looks like. A NOTE, never a
    /// refusal.</b>
    ///
    /// <para><b>It was 5, and MIM-8 measured it at 200.</b> The constant is a measurement's name, so
    /// it moved because the measurement moved and not the other way round: the peak a cross-level
    /// entry carries is a FITTED IMAGE whose depth is of the order of the film thickness, MIM-8
    /// subtracts its static part and integrates that in closed form
    /// (<see cref="ShallowImageCore"/>, <c>PlanarFillSettings.ShallowImageCells</c>), and the two
    /// ladders MIM-3 drew 5 from were re-run on MIM-3's own fixtures.</para>
    ///
    /// <para>The kernel was never the problem and still is not: at height pairs straddling a
    /// 0.05-3 µm capacitor dielectric, <see cref="Dcim.FitAtHeights"/> against direct Sommerfeld
    /// integration is <b>flat in the separation</b> — worst 4.2e-3 of the free-space kernel at
    /// 0.05 µm and 6.4e-3 at 3 µm.</para>
    ///
    /// <para><b>Ladder 1 — the cross-level matrix block</b> against forced-high quadrature, scaled by
    /// the block's own largest entry, on two coincident plates four cells to a side at d = 0.2 µm.
    /// Before MIM-8, then after:</para>
    ///
    /// <list type="table">
    ///   <item><term>cell/sep 5</term><description>3.4e-3 → 2.4e-11</description></item>
    ///   <item><term>cell/sep 20</term><description>1.1e-1 → 7.9e-8</description></item>
    ///   <item><term>cell/sep 50</term><description>3.2e-1 → 2.1e-6</description></item>
    ///   <item><term>cell/sep 200</term><description>7.4e-1 → 1.7e-4</description></item>
    ///   <item><term>cell/sep 500</term><description>9.6e-1 → 2.9e-3</description></item>
    /// </list>
    ///
    /// <para>MIM-3 drew its bound where this reached 4.1e-3. It now reaches 4.1e-3 at about 500.</para>
    ///
    /// <para><b>Ladder 2 — what it costs in an answer</b>: the capacitance of a plate pair read off
    /// the ω → 0 potential-coefficient matrix the fill itself builds, against ε₀εᵣA/d, <b>with the
    /// separation held at the shipped process value of 0.2 µm and the PLATE grown</b> — so
    /// cell/separation is the only thing that moves. Before, then after:</para>
    ///
    /// <list type="table">
    ///   <item><term>cell/sep 5</term><description>1.101 → 1.096 (a 4 µm plate: the excess is fringing, and real)</description></item>
    ///   <item><term>cell/sep 12.5</term><description>1.352 → 1.041</description></item>
    ///   <item><term>cell/sep 75</term><description>−0.046 → 1.003</description></item>
    ///   <item><term>cell/sep 150</term><description>−0.011 → 0.994</description></item>
    ///   <item><term>cell/sep 300</term><description>−0.003 → 0.996</description></item>
    ///   <item><term>cell/sep 600</term><description>−0.001 → 0.962</description></item>
    /// </list>
    ///
    /// <para><b>And the sharpest form of it: ONE structure, FOUR meshes.</b> A 60 × 60 µm plate pair
    /// at 0.2 µm, meshed at 1.87 / 3.75 / 7.5 / 15 µm — cell/separation 9.4 through 75 — reads
    /// 1.086 / 4.470 / −0.261 / −0.046 before and <b>1.003 at every one of them</b> after. The
    /// answer stopped depending on the mesh, which is what the constant is about.</para>
    ///
    /// <para><b>BOTH LADDERS WERE MEASURED AT 10 GHz. MIM-12 found that this was a condition rather
    /// than a detail; MIM-12a made it a detail again.</b> The fixtures here fit their kernel at the
    /// problem's own <c>MaxFrequencyHz</c> of 10 GHz and never apply
    /// <see cref="Dcim.ForStackAtFrequency"/>, which is what a RUN applies. On the very same 60 µm
    /// capacitor the same 1 V / 0 V instrument read <c>C/(ε₀εᵣA/d)</c> = 0.9995 at 10 GHz and
    /// <b>1.60 / 1.34 / −0.54 at 3 / 2 / 1 GHz</b> — the shipped MMIC band, with the sign inverted at
    /// the bottom of it. <b>Since MIM-12a it reads 1.006 at 1, 2, 3 and 10 GHz alike, on two
    /// meshes</b>, because the thin region's own image series is peeled out of the kernel fit and
    /// carried in closed form instead of being extrapolated. The 1 % IS now carried down the band.
    /// <c>Mim12KernelFitTests.T2</c> holds that, and its old literals are on the record beside the
    /// new ones.</para>
    ///
    /// <para><b>200 is where both ladders are measured and both hold</b>, MIM-3's own rule for
    /// drawing it. Past that they part company and the evidence gets confounded: ladder 2's 600 and
    /// 1200 rungs need a 480 µm and a 960 µm plate, which is no longer an electrically small
    /// structure for a static instrument to be asking about, and ladder 1's own forced-high
    /// reference has only 1.6-2.0× of headroom from cell/sep 50 out (51× at 5, 3.4× at 20) — so
    /// those rungs support "small, and no longer a function of the ratio" and not their own third
    /// significant figure. The shipped MMIC technology's 60 µm capacitor meshes at cell/separation
    /// 75, well inside.</para>
    ///
    /// <para><b>A separate limit, named so it is not mistaken for this one.</b> Shrinking d instead
    /// of growing the plate also raises cell/separation, and there the extracted capacitance does
    /// depart — 1.014 / 0.973 / 0.948 / 0.819 at d = 0.05 / 0.025 / 0.0125 / 0.005 µm. That is NOT a
    /// mesh condition: at a fixed d = 0.0125 µm it reads 0.948 at every pitch from 2.5 µm down to
    /// 0.31 µm, cell/separation 200 through 25. It tracks the SEPARATION, it starts below the
    /// 0.05 µm MIM-3's kernel tier was measured over, and it is a kernel-tier question MIM-8 did not
    /// open — the oracle MIM-3 used is itself validated only to ρ/λ ≥ 1e-3 and cannot be asked at
    /// ρ ~ 10 nm. The shipped 0.2 µm dielectric is four times above that floor.</para>
    /// </summary>
    public const double ValidatedCellOverSeparation = 200.0;

    /// <summary>
    /// <b>MIM-9 / R-emsev-4 — how large a CELL may be against the SEPARATION between two conductor
    /// levels before the FULL-WAVE two-port stops being an answer. A REFUSAL, and a different
    /// quantity from <see cref="ValidatedCellOverSeparation"/> above.</b>
    ///
    /// <para><b>The two constants do not measure the same thing and that is the whole point of
    /// there being two.</b> 200 is the range the cross-level FILL and the ELECTROSTATIC plate
    /// capacitance are measured over — <c>PlanarFill.ScalarPotentialMatrix</c> driven by a 1 V / 0 V
    /// instrument with no port in it. 40 is the range a DE-EMBEDDED s-parameter is measured over,
    /// which is what a user actually reads, and it is five times tighter. Quoting the first at
    /// someone reading the second is the defect MIM-9 exists to fix: a run whose published capacitor
    /// had the wrong SIGN was being told its capacitance was within 1 %.</para>
    ///
    /// <para><b>The ladder, measured 2026-09-16</b> (MIM-12), <b>on the kernel as it was BEFORE
    /// MIM-12a repaired it, and not re-run since</b> — see the MIM-12a paragraph below. One
    /// 60 × 60 µm plate pair, one set of feeds, one set of ports, the straddling cell pinned at
    /// 40 µm on every rung, and only the film thickness moving — so cell/separation is the only
    /// axis. <c>C</c> is the series arm read back from the de-embedded two-port as <c>−1/Y₂₁</c>,
    /// against ε₀εᵣA/d:</para>
    ///
    /// <list type="table">
    ///   <item><term>cell/sep 20 (d = 2 µm)</term><description>σ_max 0.998, C = 1.12 / 1.13 / 1.15 at 1 / 2 / 3 GHz — correct, the excess is plate fringing</description></item>
    ///   <item><term>cell/sep 40 (d = 1 µm)</term><description>σ_max 0.998, C = 1.27 / 1.05 / 1.13</description></item>
    ///   <item><term>cell/sep 80 (d = 0.5 µm)</term><description>σ_max 1.04-1.09, C = −0.93 / −1.02 / −1.10 — magnitude right, SIGN INVERTED</description></item>
    ///   <item><term>cell/sep 200 (d = 0.2 µm)</term><description>σ_max 1.04-1.73, C = −0.84 / −1.52 / +5.90 — incoherent</description></item>
    /// </list>
    ///
    /// <para><b>It is a REFUSAL rather than a note because of what the answer looks like past it.</b>
    /// R-prt-13's "report the number, not the verdict" habit is right where the failure is visible:
    /// a note is enough when the reader can see something is wrong. Here the run produces a
    /// complete, smooth, reciprocal, plausible two-port whose capacitor is an inductor, and no mesh
    /// control reaches it — 2, 4 and 8 cells across give 1.7322, 1.7439, 1.7491, i.e. a CONVERGED
    /// wrong answer moving slightly the wrong way. This is <c>PlanarLevels.CanRepresentVias</c>'
    /// own argument, in its own words: approximating it would give a plausible wrong inductance
    /// rather than an obvious failure, which is why it is refused.</para>
    ///
    /// <para><b>THE CAUSE WAS THE KERNEL FIT, NOT THE CONDITIONING — measured 2026-09-16, and the
    /// sentence that stood here said otherwise.</b> It read "the cause is a dynamic-range failure",
    /// from cond(Z) = 1.4e9 and a loss term 11 decades below the matrix on the 60 µm pair at
    /// 0.5 GHz. Both numbers are real and reproduce to their order, and neither is the cause: the
    /// ELECTROSTATIC instrument — <c>ScalarPotentialMatrix</c> over 32 cells, conditioned in the
    /// HUNDREDS — was ALREADY sign-inverted at 1 GHz. A factorisation that loses nine digits cannot
    /// be what breaks an answer that is broken before the factorisation.</para>
    ///
    /// <para>What broke it is that a plate pair's capacitance is a <c>d/cell</c> difference of two
    /// INDEPENDENT DCIM fits, so it inherited <c>cell/d</c> times whatever relative error they
    /// carried. The exact kernel does not move with frequency at all here (5.656193e3 at 1 GHz
    /// against 5.655920e3 at 10 GHz, direct Sommerfeld), while the FIT carried 8.3e-5 at 10 GHz and
    /// 2.7e-2 at 1 GHz — and 2.7e-2 × 75 is 200%, which is the sign inversion.</para>
    ///
    /// <para><b>MIM-12a FIXED THAT KERNEL AND THIS NUMBER DID NOT MOVE, which is a deliberate state
    /// and not an oversight.</b> The thin region's own image series is now peeled out of the fit's
    /// samples and carried in closed form, so the cross-level kernel reads 1.4e-6 at 1 GHz instead
    /// of 2.7e-2 and the electrostatic ladder reads 1.006 at every frequency in the band. <b>The
    /// DE-EMBEDDED ladder below was NOT re-run</b> — MIM-12 measured it with a harness that is not
    /// in the tree, and a fresh two-port fixture built to replace it reads its series capacitance
    /// 6-10× below the electrostatic mutual capacitance of the SAME mesh at cell/separation 1-4,
    /// i.e. in a regime this floor already admits and where no thin film is involved at all. Until
    /// that is settled the ladder cannot grade anything, so the floor stays where MIM-12 put it and
    /// is now CONSERVATIVE: the kernel defect its rungs measured is gone, and how far the two-port
    /// reaches on the repaired kernel is unmeasured rather than known. Leaving the refusal standing
    /// is MIM-9's own instruction — a fix that REMOVES it instead of moving it is how the next
    /// regime becomes silent. See <c>RESOLVED.md</c> §MIM-12 and §MIM-12a.</para>
    /// </summary>
    public const double FullWaveCellOverSeparation = 40.0;

    /// <summary>
    /// The refusal, and it is now earned on ONE quantity rather than two.
    ///
    /// <para><b>L9e's geometric bound (<c>MaxLengthOverWidth = 0.5</c>) is RETIRED.</b> It existed
    /// because the midpoint rule froze <c>1/R</c> over the via's length, making the via's inductance
    /// high by ≈ 0.673·(ℓ/w) with no frequency in the condition at all. The z-integral is now
    /// resolved and the same measurement reads flat to 0.13% over ℓ/w ∈ [0.01, 5] and a 16× range of
    /// footprint width, so there is nothing left for a geometric bound to refuse. Retiring it does
    /// NOT widen what this kernel can answer:
    /// <see cref="Dcim.ValidatedRhoOverLambdaAtHeights"/> = 0.1 on G_A^zz already restricts every
    /// via-bearing run to electrically small structures, and that limit is untouched.</para>
    ///
    /// <para>What remains is electrical and real: a via basis is ONE z-rooftop per inter-level gap, so
    /// the current it carries is uniform over the whole length. That is exact for a short via and
    /// wrong for a resonant one however well the kernel is integrated.</para>
    /// </summary>
    /// <param name="kMax">Wavenumber at the top of the sweep, in the fastest-slowing medium.</param>
    /// <param name="hasGroundAttachment">Whether any via runs to the ground plane, whose span is
    /// <see cref="GroundZ"/> → the metal rather than one inter-level gap.</param>
    /// <param name="fHiHz">
    /// The sweep top <paramref name="kMax"/> was taken at, when the caller knows it — so a refusal can
    /// name the frequency that WOULD pass rather than only the direction to move in. Zero omits that
    /// sentence, which is what a unit test constructing a bare wavenumber wants.
    /// </param>
    public EmSuitability CanRepresentVias(double kMax, bool hasGroundAttachment = false, double fHiHz = 0)
    {
        for (int i = 0; i + 1 < Z.Count; i++)
        {
            var v = CheckOne(kMax, LengthOf(i), $"The via between levels {i} and {i + 1}", fHiHz);
            if (!v.Ok) return v;
        }

        if (hasGroundAttachment)
            for (int i = 0; i < Z.Count; i++)
            {
                var v = CheckOne(kMax, AttachmentLengthOf(i),
                                 $"The ground via from the plane up to level {i}", fHiHz);
                if (!v.Ok) return v;
            }

        return EmSuitability.Yes;
    }

    private static EmSuitability CheckOne(double kMax, double ell, string subject, double fHiHz = 0)
    {
        double kl = kMax * ell;
        if (kl <= MaxElectricalLength) return EmSuitability.Yes;

        return EmSuitability.No(
            $"{subject} is {ell:G4} m long, i.e. k·ℓ = {kl:G4} at the top of the sweep, above this " +
            $"kernel's floor of {MaxElectricalLength}. A vertical basis here is a SINGLE z-rooftop " +
            $"spanning the whole run, so the current it carries is UNIFORM along it — a limit on the " +
            $"BASIS, not on the quadrature: the z-integral of the Green's function is resolved " +
            $"(ViaZIntegral), and no amount of integrating it better gives the via a current profile " +
            $"it has no degree of freedom for. The bound is set at 0.30 from a MEASUREMENT rather " +
            $"than from O((kℓ)²): subdividing an ATTACHED via moved the answer 0.077% at k·ℓ = 0.23 " +
            $"and 0.141% at k·ℓ = 1.0, while the subdivision itself costs ~14% of a de-embedded " +
            $"point (see PlanarLevels.MaxElectricalLength). Lower the sweep's top{TopThatPasses(kl, fHiHz)}, " +
            $"or — for a via between two meshed levels — split it across intermediate levels, which " +
            $"gives it an n-step profile at that cost.");
    }

    /// <summary>
    /// <b>The sweep top this via WOULD pass at, as a phrase to hang on the refusal.</b>
    ///
    /// <para>k is proportional to frequency, so the bound is a plain ratio and the answer is exact
    /// rather than a search. A refusal that says only "lower the sweep's top" leaves the reader to
    /// find that number by bisecting whole EM runs, and nothing else on screen says how far over the
    /// line the geometry is — 4% over reads exactly like 400% over.</para>
    /// </summary>
    private static string TopThatPasses(double kl, double fHiHz)
    {
        if (!(fHiHz > 0) || !(kl > 0)) return "";

        double f = fHiHz * MaxElectricalLength / kl;
        return f >= 1e9 ? $" (to {f / 1e9:G4} GHz or below, from {fHiHz / 1e9:G4} GHz)"
             : f >= 1e6 ? $" (to {f / 1e6:G4} MHz or below, from {fHiHz / 1e6:G4} MHz)"
                        : $" (to {f:G4} Hz or below)";
    }
}

/// <summary>
/// Every kernel component at every height pairing a mesh asks for, fitted ONCE PER FREQUENCY and
/// lazily. See the file header for what survives from L8d's <see cref="PlanarKernelPair"/> and what
/// does not.
/// </summary>
public sealed class PlanarKernelSet
{
    /// <summary>
    /// <b>L9d/M1 — the FIT cache is shared by every per-mesh view, and that is the load-bearing
    /// half of L8d's caching decision rather than an optimisation.</b>
    ///
    /// <para>L8d's own rule is "fit once per frequency, share across the DUT and every standard",
    /// and a de-embedded solve touches THREE meshes at every frequency. Before L9d, <c>For</c>
    /// returned a fresh set whose cache was a COPY of whatever had been fitted so far — which was
    /// harmless while only one mesh ever asked (L9c had no solve), and turns 9 fits per frequency
    /// into 9 per MESH the moment a calibrator does. So the <see cref="DcimModel"/>s live here, once
    /// per (component, height pairing), and every view derives its own
    /// <see cref="PlanarKernelTerms"/> from them — which is the cheap half
    /// (<c>FromDcimAtHeights</c> re-decomposes an already-fitted model; <c>FitAtHeights</c> is the
    /// ~0.1 s one).</para>
    ///
    /// <para>The dictionary is a pure LOOKUP built by lazy insertion and never iterated, so R-mlp-5's
    /// determinism is unaffected — there is no hash order anywhere on this path.</para>
    /// </summary>
    private sealed class FitCache
    {
        public readonly Dictionary<(GreensKernel, double, double), DcimModel> Models = new();
        // M2's direct tables live here for exactly the reason the fits do (L9d's own finding): a
        // de-embedded solve builds one PlanarKernelSet VIEW per mesh, and a per-view cache would
        // rebuild them per mesh with no answer anywhere looking wrong. It matters MORE here — a
        // miss is seconds of Sommerfeld integration rather than ~90 ms of fit.
        public readonly Dictionary<((GreensKernel, double, double), double, int), PlanarKernelTerms> Direct = new();
        /// <summary>One gate per direct-table key, so exactly ONE thread pays for a table and the
        /// rest wait for it. Building outside the shared lock (which is right — a table is seconds
        /// and must not block every other fit) otherwise lets N threads each build the SAME table.</summary>
        public readonly Dictionary<((GreensKernel, double, double), double, int), object> DirectGates = new();
        /// <summary>MIM-12a — the same pairing FITTED WITH the transmitted chain peeled out, keyed by
        /// the threshold that decided the peel. A separate dictionary rather than a wider key on
        /// <see cref="Models"/>, so an unpeeled lookup cannot accidentally find a peeled fit.</summary>
        public readonly Dictionary<(GreensKernel, double, double, double), DcimModel> Peeled = new();
        public readonly object Gate = new();
        public int Count;
    }

    private readonly LayeredSpectralGreens _greens;
    private readonly DcimSettings          _dcim;
    private readonly PlanarExtractionOrder _order;
    private readonly double                _rhoFloor;
    private readonly FitCache              _fits;
    private readonly Dictionary<(GreensKernel, double, double), PlanarKernelTerms> _terms = new();
    private readonly Dictionary<(GreensKernel, double, double), PlanarKernelTerms> _reduced = new();
    private readonly Dictionary<(GreensKernel, double, double, double), ShallowImageSplit> _shallow = new();
    /// <summary>MIM-12a — the SAME pairing with nothing subtracted, taken from whichever model
    /// <see cref="GetMinusShallowImages"/> used. Keyed by the threshold for the same reason that one
    /// is: the threshold decides whether the fit was peeled, and a peeled fit is a different (and
    /// more accurate) representation of the same kernel.</summary>
    private readonly Dictionary<(GreensKernel, double, double, double), PlanarKernelTerms> _whole = new();
    /// <summary>MIM-12 — the same split with the fit replaced by direct Sommerfeld integration.
    /// Keyed by the table's extent and sample count as well, because those are what it IS.</summary>
    private readonly Dictionary<(GreensKernel, double, double, double, double, int), ShallowImageSplit>
        _directShallow = new();

    public LayerStack Stack       => _greens.Stack;
    public double     FrequencyHz => _greens.FrequencyHz;

    /// <summary>
    /// <b>D7's counter, and it is the R-mom-11 pattern.</b> "Four components × three pairings ≈ 12
    /// fits per frequency" is a projection; this is what was actually asked for. A test asserts it,
    /// so a future change that starts refitting per CELL PAIR instead of per PAIRING fails loudly
    /// rather than costing an hour a sweep.
    ///
    /// <para>It counts fits across every view produced by <see cref="For"/>, because that is the
    /// quantity L8d's decision is about — the DUT and its standards share one number, not three.</para>
    /// </summary>
    public int FitCount { get { lock (_fits.Gate) return _fits.Count; } }

    public PlanarKernelSet(LayeredSpectralGreens greens,
                           PlanarExtractionOrder order = PlanarExtractionOrder.Constant,
                           double rhoFloor = 0.0, DcimSettings? dcim = null)
        : this(greens, order, rhoFloor, dcim ?? DcimSettings.Default, new FitCache()) { }

    private PlanarKernelSet(LayeredSpectralGreens greens, PlanarExtractionOrder order,
                            double rhoFloor, DcimSettings dcim, FitCache fits)
    {
        _greens = greens;
        _order = order;
        _rhoFloor = rhoFloor;
        _dcim = dcim;
        _fits = fits;
    }

    /// <summary>The same set re-floored for one mesh's smallest cell — L8d's <c>For</c>, unchanged in
    /// spirit: the per-mesh part of the terms is only the ρ floor. The FIT cache is shared with the
    /// set this was made from, so a second mesh at the same frequency refits nothing.</summary>
    public PlanarKernelSet For(PlanarFillCores cores)
        => new(_greens, _order, cores.RhoFloorM, _dcim, _fits);

    /// <summary>
    /// The terms for one component at one height pairing. <b>Symmetric in the two heights</b>, and
    /// canonicalised so that (z, z′) and (z′, z) share one fit — which is legitimate here and is NOT
    /// the canonicalisation L9a's R-lyr-5 forbids: that one is about never computing the reverse
    /// chain, and reciprocity of the KERNEL is measured independently in
    /// <c>VerticalCurrentTests.T0_2</c>. This is about not fitting the same function twice.
    /// </summary>
    public PlanarKernelTerms Get(GreensKernel kernel, double zA, double zB)
    {
        var key = Key(kernel, zA, zB);
        lock (_terms)
            if (_terms.TryGetValue(key, out var hit)) return hit;

        var terms = PlanarKernelTerms.FromDcimAtHeights(Model(kernel, zA, zB), _order, _rhoFloor);
        lock (_terms) _terms[key] = terms;
        return terms;
    }

    /// <summary>
    /// <b>The via z-integral's view of the same fit: the decomposition with the two asymptotes' STATIC
    /// parts removed</b> (<see cref="PlanarKernelTerms.FromDcimAtHeightsMinusStaticAsymptotes"/>).
    /// Shares the fit — asking for both views of one height pair costs one
    /// <see cref="Dcim.FitAtHeights"/>, which is the whole point of L9d's shared cache.
    /// </summary>
    public PlanarKernelTerms GetMinusStaticAsymptotes(GreensKernel kernel, double zA, double zB)
    {
        var key = Key(kernel, zA, zB);
        lock (_reduced)
            if (_reduced.TryGetValue(key, out var hit)) return hit;

        var terms = PlanarKernelTerms.FromDcimAtHeightsMinusStaticAsymptotes(
            Model(kernel, zA, zB), _order, _rhoFloor);
        lock (_reduced) _reduced[key] = terms;
        return terms;
    }

    /// <summary>
    /// <b>MIM-8's view of the same fit: the decomposition with every image SHALLOWER than
    /// <paramref name="shallowDepthM"/> stripped of its static part, together with the images that
    /// were stripped</b>
    /// (<see cref="PlanarKernelTerms.FromDcimAtHeightsMinusShallowImages"/>).
    ///
    /// <para>Shares the fit with <see cref="Get"/> and <see cref="GetMinusStaticAsymptotes"/>, so a
    /// height pairing asked for in more than one view still costs one <see cref="Dcim.FitAtHeights"/>.
    /// The threshold is part of the cache key because it is derived from the MESH — two meshes over
    /// one stack can disagree about which images are shallow, and they are not the same
    /// decomposition.</para>
    /// </summary>
    public ShallowImageSplit GetMinusShallowImages(GreensKernel kernel, double zA, double zB,
                                                   double shallowDepthM)
    {
        var k = Key(kernel, zA, zB);
        var key = (k.Kernel, k.Lo, k.Hi, shallowDepthM);
        lock (_shallow)
            if (_shallow.TryGetValue(key, out var hit)) return hit;

        var split = PlanarKernelTerms.FromDcimAtHeightsMinusShallowImages(
            Model(kernel, zA, zB, shallowDepthM), shallowDepthM, _order, _rhoFloor);

        // Nothing shallow ⇒ hand back the SHARED ordinary terms rather than an equal copy, so a run
        // with no thin film in it reaches the fill with the very object it reached it with before.
        if (split.Removed.Count == 0) split = new ShallowImageSplit(Get(kernel, zA, zB), []);

        lock (_shallow) _shallow[key] = split;
        return split;
    }

    /// <summary>
    /// <b>MIM-12 — MIM-8's shallow-image split with the FIT replaced by direct Sommerfeld
    /// integration, for the scalar kernel of a level pairing the mesh cannot resolve.</b>
    ///
    /// <para>Exactly <see cref="GetDirectMinusStaticAsymptotes"/>'s construction, one decomposition
    /// over: keep every part that is exact — the extraction coefficients and the list of images
    /// MIM-8 strips and re-integrates in closed form — and replace only the part that is fitted.
    /// The assembled entry is <c>Extracted·(closed-form core) + Remainder·(quadrature)</c> with
    /// <c>Remainder = full − Extracted</c>, so with <c>full</c> the direct integral the SUM is the
    /// direct integral whatever the coefficients are.</para>
    ///
    /// <para><b>Why the scalar block of a thin pairing needs it, when nothing else does.</b> A
    /// plate pair's capacitance is what is left after the same-level and cross-level potential
    /// coefficients nearly cancel — on the shipped 0.2 µm film under a 15 µm cell the difference is
    /// about 2% of either. The two pairings are two INDEPENDENT Prony fits whose own relative
    /// accuracy was 1e-5 to 1e-2 and whose errors are uncorrelated, so the capacitance inherited
    /// 200× whatever they carried. Measured (60 × 60 µm plate pair, d = 0.2 µm, four meshes), the
    /// extracted <c>C/(ε₀εᵣA/d)</c> moved from 1.00 to −0.55 between two path extents that differ
    /// only in which one the low-frequency widening happens to pick.</para>
    ///
    /// <para><b>MIM-12a repaired the fit itself, and this path became a CROSS-CHECK on it rather
    /// than a remedy.</b> The fitted route now reads 1.006 where this one reads 1.015 — within 2 %,
    /// on a kernel the two reach by entirely different means. It matters that this arm improved as
    /// well: the images it subtracts come from the same split, so before MIM-12a they were the fit's
    /// own and the remainder it tabulated still carried the film's whole structure (2.3e4 → 87 over
    /// ρ = 0.02 … 0.93 µm, which no linear table at the mesh's spacing can hold). They are now the
    /// derived series, the remainder is small and smooth, and the 24 % shortfall is gone.</para>
    /// </summary>
    /// <param name="shallowDepthM">MIM-8's threshold, the mesh's own cell against the pairing.</param>
    /// <param name="rhoMaxM">The mesh's radial extent — the table must reach the widest pair.</param>
    /// <param name="samples">Direct integration points. Each is one
    /// <see cref="SommerfeldIntegral.EvaluateInterior"/>, which is why this is a setting.</param>
    public ShallowImageSplit GetDirectMinusShallowImages(
        GreensKernel kernel, double zA, double zB, double shallowDepthM,
        double rhoMaxM, int samples)
    {
        if (!(rhoMaxM > 0))
            throw new ArgumentOutOfRangeException(nameof(rhoMaxM), rhoMaxM,
                "The direct scalar table needs a positive radial extent.");
        if (samples < 8)
            throw new ArgumentOutOfRangeException(nameof(samples), samples,
                "The direct scalar table needs at least 8 samples.");

        var k   = Key(kernel, zA, zB);
        var key = (k.Kernel, k.Lo, k.Hi, shallowDepthM, rhoMaxM, samples);
        lock (_directShallow)
            if (_directShallow.TryGetValue(key, out var hit)) return hit;

        // The FITTED split is still what decides which images are shallow and what the extraction
        // coefficients are. Both are cheap, both are shared with every other view of this pairing,
        // and neither is the thing that was measured failing.
        var fitted  = GetMinusShallowImages(kernel, zA, zB, shallowDepthM);
        var removed = fitted.Removed;
        var terms0  = fitted.Terms;

        Complex Full(double rho)
        {
            Complex v = SommerfeldIntegral.EvaluateInterior(_greens, kernel, rho, k.Hi, k.Lo).Value;
            foreach (var im in removed)
            {
                Complex r = Complex.Sqrt(rho * rho + im.Depth * im.Depth);
                if (r.Real < 0) r = -r;
                v -= im.Amplitude / (4.0 * Math.PI * r);
            }
            return v;
        }

        // TABULATE THE REMAINDER, NOT THE KERNEL — GetDirectMinusStaticAsymptotes' own reason: the
        // kernel still diverges as 1/ρ and a linear table cannot carry that, worst exactly at the
        // self and touching pairs where most of the block's value is.
        var table = RadialRemainderTable.BuildFrom(
            rho => Full(rho) - terms0.Extracted(rho),
            rhoMaxM, rhoMaxM / Math.Max(samples - 4, 4), samples);

        var split = new ShallowImageSplit(
            new PlanarKernelTerms(rho => table.Evaluate(rho) + terms0.Extracted(rho),
                                  terms0.Inverse, terms0.Log, terms0.Constant, terms0.Linear,
                                  _order, _rhoFloor, terms0.SmallestImageDepth),
            removed);

        lock (_directShallow) _directShallow[key] = split;
        return split;
    }

    /// <summary>
    /// <b>M2 (brief-gazz-accuracy-ceiling) — the SAME decomposition with the fit replaced by DIRECT
    /// Sommerfeld integration.</b>
    ///
    /// <para>The fit is what fails: M1 measured every reachable <see cref="DcimSettings"/> knob and
    /// the best configuration is still 71× outside the envelope the other three components meet at
    /// ρ/λ = 1 — and is 23× WORSE inside ρ/λ ≤ 0.1, where the kernel is used today. So this path
    /// keeps every part of the decomposition that is exact and replaces only the part that is
    /// fitted.</para>
    ///
    /// <para><b>What is kept, and why that makes the result exact rather than merely different.</b>
    /// The extraction coefficients and the removed static-asymptote pieces come from the cached fit
    /// — one fit, already paid for — but the assembled entry is
    /// <c>Extracted·(closed-form core) + Remainder·(quadrature)</c> with
    /// <c>Remainder = full − Extracted</c>, so the split only decides how much value sits on each
    /// side. With <c>full</c> the direct integral, the SUM is the direct integral whatever the
    /// coefficients are. That is the same extraction-order invariance L9c's own T4_3 measures.</para>
    ///
    /// <para><b>The cost is real and is the point of it being a SETTING.</b> One
    /// <see cref="SommerfeldIntegral.EvaluateInterior"/> point is 40–50 ms, against a whole
    /// <see cref="Dcim.FitAtHeights"/> at ~90 ms — so this is affordable only because the ẑẑ block
    /// consumes its kernel through a radial TABLE, and only at the sample count M2 measured rather
    /// than at the DCIM table's own mesh-derived spacing.</para>
    /// </summary>
    public PlanarKernelTerms GetDirectMinusStaticAsymptotes(
        GreensKernel kernel, double zA, double zB, double rhoMaxM, int samples)
    {
        var key = Key(kernel, zA, zB);
        if (!(rhoMaxM > 0))
            throw new ArgumentOutOfRangeException(nameof(rhoMaxM), rhoMaxM,
                "The direct ẑẑ table needs a positive radial extent.");
        if (samples < 8)
            throw new ArgumentOutOfRangeException(nameof(samples), samples,
                "The direct ẑẑ table needs at least 8 samples; M2 measured the assembled block " +
                "converging at 128 and still moving 2.2e-3 at 32.");

        var cacheKey = (key, rhoMaxM, samples);
        object gate;
        lock (_fits.Gate)
        {
            if (_fits.Direct.TryGetValue(cacheKey, out var hit)) return hit;
            if (!_fits.DirectGates.TryGetValue(cacheKey, out var g))
                _fits.DirectGates[cacheKey] = g = new object();
            gate = g;
        }

        // Serialise on the KEY, not on the shared cache: one thread pays for the table, the rest
        // wait for that one rather than each building an identical copy, and no fit anywhere else
        // is blocked meanwhile.
        lock (gate)
        {
        lock (_fits.Gate)
            if (_fits.Direct.TryGetValue(cacheKey, out var hit2)) return hit2;

        // The fitted view supplies the extraction coefficients and the list of static asymptote
        // pieces the via's closed-form z-integral has already accounted for. Both are cheap and
        // cached; neither is what M1 measured as failing.
        var fitted = GetMinusStaticAsymptotes(kernel, zA, zB);
        var model  = Model(kernel, zA, zB);
        var pieces = model.AsymptotePieces
                          .Where(p => p.Coefficient != Complex.Zero)
                          .Select(p => (p.Coefficient, p.Depth))
                          .ToArray();

        // The direct kernel, minus the static asymptote pieces the via's closed-form z-integral has
        // already taken.
        Complex Full(double rho)
        {
            Complex v = SommerfeldIntegral.EvaluateInterior(_greens, kernel, rho, key.Hi, key.Lo).Value;
            foreach (var (c, d) in pieces)
                v -= c / (4.0 * Math.PI * Math.Sqrt(rho * rho + d * d));
            return v;
        }

        // TABULATE THE REMAINDER, NOT THE KERNEL. The kernel still diverges as 1/ρ once the static
        // asymptotes are removed (the poles' ln ρ and the direct term's own 1/ρ are still in it), and
        // a linear table cannot carry either — it would be worst exactly at the self and touching
        // cell pairs, which is where most of the block's value is. Subtracting `Extracted` first is
        // what makes the tabulated function bounded, and it is the same thing L8c's own
        // RadialRemainderTable.Build does; only the evaluator differs.
        var table = RadialRemainderTable.BuildFrom(
            rho => Full(rho) - fitted.Extracted(rho),
            rhoMaxM, rhoMaxM / Math.Max(samples - 4, 4), samples);

        // Handing back `table + Extracted` as the FULL kernel makes Remainder() return the tabulated
        // bounded function exactly, so the fill sees the same shape it always does.
        var terms = new PlanarKernelTerms(
            rho => table.Evaluate(rho) + fitted.Extracted(rho),
            fitted.Inverse, fitted.Log, fitted.Constant, fitted.Linear,
            _order, _rhoFloor, fitted.SmallestImageDepth);

        lock (_fits.Gate) _fits.Direct[cacheKey] = terms;
        return terms;
        }
    }

    /// <summary>The fitted model at one height pairing, from the shared cache — what a caller that
    /// needs the model's own structure (its asymptote depths, its radial derivative) asks for rather
    /// than re-fitting.</summary>
    public DcimModel Model(GreensKernel kernel, double zA, double zB)
    {
        var key = Key(kernel, zA, zB);
        lock (_fits.Gate)
        {
            if (!_fits.Models.TryGetValue(key, out var model))
            {
                model = Dcim.FitAtHeights(_greens, kernel, key.Hi, key.Lo, _dcim);
                _fits.Models[key] = model;
                _fits.Count++;
            }
            return model;
        }
    }

    /// <summary>
    /// <b>MIM-12a — the same model, with the cross-region transmitted chain peeled out of the
    /// samples before the fit ran</b>, for a pairing that crosses a region the mesh cannot resolve.
    ///
    /// <para><b>Identical to <see cref="Model(GreensKernel, double, double)"/> — the very same
    /// object — whenever no series applies</b>, which is every same-region pairing, every cross-region
    /// pairing across a thickness the mesh DOES resolve, and every component but the two horizontal
    /// ones. That identity is what keeps an ordinary run bit-for-bit unchanged, and it is why the
    /// threshold is a parameter rather than a stack property: the trigger is MIM-8's own cell-against-
    /// separation test, not "a film is present".</para>
    /// </summary>
    public DcimModel Model(GreensKernel kernel, double zA, double zB, double thinnerThanM)
    {
        var k = Key(kernel, zA, zB);
        var series = _greens.ThinRegionImagesAtHeights(kernel, k.Hi, k.Lo, thinnerThanM);
        if (series.Images.Count == 0) return Model(kernel, zA, zB);

        var key = (k.Kernel, k.Lo, k.Hi, thinnerThanM);
        lock (_fits.Gate)
        {
            if (!_fits.Peeled.TryGetValue(key, out var model))
            {
                model = Dcim.FitAtHeights(_greens, kernel, k.Hi, k.Lo, _dcim, series.Images);
                _fits.Peeled[key] = model;
                _fits.Count++;
            }
            return model;
        }
    }

    /// <summary>
    /// <b>MIM-12a — the WHOLE kernel of a pairing, from whichever fit
    /// <see cref="GetMinusShallowImages"/> used at the same threshold.</b> This is the view a cell
    /// pair whose own ρ range never reaches the peak takes, and the one an AIM grid table must be
    /// sampled from.
    ///
    /// <para><b>It exists so that the far view costs no second fit and is no less accurate than the
    /// near one.</b> Before MIM-12a the far view was <see cref="Get"/> — the unpeeled fit — which was
    /// the same object and therefore free. With a peel it is not the same object, and asking
    /// <see cref="Get"/> for it would both pay for a second <c>Dcim.FitAtHeights</c> and quietly hand
    /// the far pairs the less accurate of two representations of one kernel.</para>
    ///
    /// <para>With no peel it returns <see cref="Get"/>'s own cached object, so a run with no thin
    /// cross-region pairing reaches the fill with the very terms it always did.</para>
    /// </summary>
    public PlanarKernelTerms GetWhole(GreensKernel kernel, double zA, double zB, double thinnerThanM)
    {
        var model = Model(kernel, zA, zB, thinnerThanM);
        if (model.TransmittedImages.Count == 0) return Get(kernel, zA, zB);

        var k = Key(kernel, zA, zB);
        var key = (k.Kernel, k.Lo, k.Hi, thinnerThanM);
        lock (_whole)
            if (_whole.TryGetValue(key, out var hit)) return hit;

        var terms = PlanarKernelTerms.FromDcimAtHeights(model, _order, _rhoFloor);
        lock (_whole) _whole[key] = terms;
        return terms;
    }

    /// <summary>The k_ρ → ∞ asymptote of one component at one height pair. <b>Costs no fit</b> — it is
    /// a handful of Fresnel coefficients — and its two COEFFICIENTS do not depend on the heights at
    /// all, which is what makes the via's singular z-integral a closed form.</summary>
    public LayeredSpectralGreens.InteriorAsymptote Asymptote(GreensKernel kernel, double z, double zp)
        => _greens.AsymptoticAtHeights(kernel, z, zp);

    private static (GreensKernel Kernel, double Lo, double Hi) Key(GreensKernel k, double zA, double zB)
        => (k, Math.Min(zA, zB), Math.Max(zA, zB));

    /// <summary>R-via-4's refusal, asked once for a mesh rather than per entry: is the widest
    /// separation this mesh will ask about inside the interior fit's validated range?</summary>
    public EmSuitability WithinValidatedRange(double meshExtentM)
    {
        double lambda = EmConstants.C0 / FrequencyHz;
        return Dcim.WithinValidatedRangeAtHeights(GreensKernel.VerticalVectorPotential,
                                                  _greens, meshExtentM / lambda);
    }
}
