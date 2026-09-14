using System.Numerics;
using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

/// <summary>
/// <b>CL5 — the lossless-stack refusal belongs to the INTEGRATOR, not to the fit.</b>
/// <c>docs/sonnet-briefs/brief-conductor-loss-5-lossless-predicate.md</c>; findings in
/// <c>RESOLVED.md</c> §CL5.
///
/// <para><see cref="SommerfeldIntegral.CanIntegrateLayered"/> refuses a guided stack with no loss in
/// it, because a surface-wave pole sitting exactly on the real k_ρ axis is fatal to a contour that
/// walks that axis. That refusal reached <see cref="Dcim.FitAtHeights"/> only because it borrowed
/// <see cref="SommerfeldIntegral.CanIntegrateInterior"/> wholesale — and the fit integrates nothing:
/// it samples in the source region's own k_zm and subtracts every pole in closed form first. Two
/// separable defects, and both are measured here rather than argued:</para>
/// <list type="number">
/// <item>the predicate was BORROWED — <see cref="R_cl5_2_TheEpsilonLadder"/> and
/// <see cref="R_cl5_3_ATwoLevelDesignOnAnIdealSubstrateSolves"/>;</item>
/// <item>the predicate was BLIND TO THE TERMINATION — a CL4 conducting floor dissipates and moves
/// the poles off the axis, which is exactly the condition the refusal exists to guarantee, and
/// <c>Layers.Any(tanδ &gt; 0)</c> could not see it: <see cref="R_cl5_5_ACl4FloorIsAdmitted_AndAPerfectOneIsStillRefused"/>.</item>
/// </list>
///
/// <para><b>R-cl5-1 was measured against HEAD rather than asserted here, and the number is in
/// §CL5.</b> A dump of <see cref="Dcim.FitAtHeights"/>, <see cref="Dcim.Fit"/> and
/// <see cref="SommerfeldIntegral.EvaluateLayered"/> over all six <see cref="LayerStacks"/> fixtures
/// × 2/10/20 GHz × three kernels × three height pairings — 234 configurations, <b>2,070 doubles at
/// full round-trip precision</b> — came back byte for byte identical from a worktree at HEAD, with
/// no refusal on either side. What is asserted below instead is the thing a future edit could
/// break: that the split predicate still agrees with the union wherever the stack dissipates.</para>
/// </summary>
public sealed class LosslessStackPredicateTests(ITestOutputHelper output)
{
    private const double CopperSigma = 5.8e7, GoldSigma = 4.1e7;

    /// <summary>The same stack with every layer's tanδ replaced — the ε-limit ORACLE construction.
    /// It is never applied to a run: nothing in the product may displace a pole to make a contour
    /// work, which is a wrong answer with a plausible shape.</summary>
    private static LayerStack AtTanD(LayerStack s, double tanD) =>
        new(s.Bottom,
            s.Layers.Select(l => new MediumLayer(l.ThicknessM, new EmMaterial(l.Material.EpsR, tanD))).ToArray(),
            s.Top);

    private static IEnumerable<(string Name, LayerStack Stack, double Sigma, double T)> Starters()
    {
        yield return ("FR-4 1.6 mm", LayerStacks.Fr4Slab,  CopperSigma, 35e-6);
        yield return ("GaAs 100 µm", LayerStacks.GaAsSlab, GoldSigma,   3e-6);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // R-cl5-4 — the refusal still refuses, and its REASON is about the contour
    // ═══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The direct integrator keeps its restriction, and the MESSAGE is asserted rather than only
    /// the verdict.</b> This refusal has been wrong in its attribution once — it named a path that
    /// does not share it, and that sentence is what carried it into <see cref="Dcim.FitAtHeights"/>
    /// and into two briefs' scoping. Asserting the reason is what stops that recurring.
    /// </summary>
    [Fact]
    public void R_cl5_4_TheDirectIntegratorStillRefusesALosslessGuidedStack_ByTheContour()
    {
        const double f = 10e9;
        var stack = AtTanD(LayerStacks.Fr4Slab, 0.0);
        var g = new LayeredSpectralGreens(stack, f);
        double h = stack.TopZ, rho = 1e-2 * EmConstants.C0 / f;

        var verdict = SommerfeldIntegral.CanIntegrateLayered(stack);
        output.WriteLine("CanIntegrateLayered(FR-4, tanδ = 0): " + (verdict.Ok ? "yes" : "NO"));
        output.WriteLine(verdict.Reason);
        Assert.False(verdict.Ok);

        // The reason is the CONTOUR's, and it names the paths that do NOT share it.
        Assert.Contains("real-k_ρ contour", verdict.Reason);
        Assert.Contains("principal value", verdict.Reason);
        Assert.Contains("CanFitAtHeightsStructurally", verdict.Reason);

        foreach (var (what, ex) in new (string, Exception)[]
        {
            ("EvaluateLayered",  Assert.Throws<ArgumentException>(() =>
                SommerfeldIntegral.EvaluateLayered(g, GreensKernel.ScalarPotential, rho, h, h))),
            ("EvaluateInterior", Assert.Throws<ArgumentException>(() =>
                SommerfeldIntegral.EvaluateInterior(g, GreensKernel.ScalarPotential, rho, h, h))),
        })
        {
            output.WriteLine($"{what}: refused — {ex.Message[..60]}…");
            Assert.Contains("real-k_ρ contour", ex.Message);
        }

        // …and the two structural refusals it also carries are unchanged and still fire, so this is
        // a NARROWING and not a deletion: a wall on top, and a source inside a wall.
        var walled = new LayerStack(Termination.Pec, stack.Layers, Termination.Pec);
        Assert.Contains("solid wall", SommerfeldIntegral.CanIntegrateLayered(walled).Reason!);
        var lossy = new LayeredSpectralGreens(LayerStacks.Fr4Slab, f);
        var inWall = SommerfeldIntegral.CanFitAtHeightsStructurally(lossy, h, -1e-6);
        output.WriteLine("CanFitAtHeightsStructurally with the source in the floor: " + inWall.Reason);
        Assert.False(inWall.Ok);
        Assert.Contains("solid wall", inWall.Reason!);
    }

    /// <summary>
    /// <b>R-cl5-1's durable half — the split predicate and the union still agree wherever the stack
    /// dissipates</b>, which is every stack that ran before this brief. They may differ ONLY on a
    /// lossless guided stack, and there the union must be the one that refuses.
    /// </summary>
    [Fact]
    public void R_cl5_1_TheSplitPredicateAgreesWithTheUnion_WhereverTheStackDissipates()
    {
        const double f = 10e9;
        var cases = LayerStacks.All().ToList();
        cases.Add(("FR-4, tanδ = 0", AtTanD(LayerStacks.Fr4Slab, 0.0)));
        cases.Add(("GaAs, tanδ = 0", AtTanD(LayerStacks.GaAsSlab, 0.0)));
        cases.Add(("FR-4, tanδ = 0, 35 µm Cu floor",
            new LayerStack(Termination.LossyGround(CopperSigma, 35e-6),
                           AtTanD(LayerStacks.Fr4Slab, 0.0).Layers, Termination.Air)));
        cases.Add(("Air over ground (no guided mode)", LayerStacks.AirOverGround()));

        output.WriteLine("                                      dissipates  guided  union  structural");
        foreach (var (name, stack) in cases)
        {
            var g = new LayeredSpectralGreens(stack, f);
            double z = stack.TopZ;
            bool union      = SommerfeldIntegral.CanIntegrateInterior(g, z, z).Ok;
            bool structural = SommerfeldIntegral.CanFitAtHeightsStructurally(g, z, z).Ok;
            bool guided     = stack.Layers.Any(l => l.Material.EpsR * l.Material.MuR
                                                  > stack.Top.Material.EpsR * stack.Top.Material.MuR + 1e-12);
            output.WriteLine($"  {name,-38}{stack.Dissipates,7}{guided,8}{union,7}{structural,12}");

            // The structural half never refuses something the union admits — it is a SUBSET.
            Assert.True(!union || structural, $"{name}: the narrowed predicate refuses more than the union");
            // And wherever the stack dissipates the two are the same question.
            if (stack.Dissipates) Assert.Equal(union, structural);
            // The only place they part company is the lossless guided stack.
            if (!union) Assert.True(guided && !stack.Dissipates, $"{name}: refused for some other reason");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // R-cl5-5 — a CL4 floor is admitted
    // ═══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The row that puts this brief in the conductor-loss series rather than in a tidy-up pile.</b>
    /// A 35 µm copper floor under a tanδ = 0 substrate moves the TM₀ pole three orders FURTHER off
    /// the real axis than a tanδ = 1e-12 substrate the old test admitted freely — and the old test
    /// still called it lossless, because a <see cref="Termination"/> is not a <see cref="MediumLayer"/>.
    /// A PERFECT spelling of the same floor is correctly still lossless.
    /// </summary>
    [Fact]
    public void R_cl5_5_ACl4FloorIsAdmitted_AndAPerfectOneIsStillRefused()
    {
        const double f = 10e9;
        output.WriteLine("                                              dissipates  admitted   pole |Im k_ρ|/Re k_ρ");
        foreach (var (name, pec, sigma, t) in Starters())
        {
            var lossless = AtTanD(pec, 0.0);
            foreach (var (tag, floor, expect) in new[]
            {
                ("PEC floor",                 Termination.Pec,                                false),
                ("real floor",                Termination.LossyGround(sigma, t),              true),
                ("σ = +∞ floor",              Termination.LossyGround(double.PositiveInfinity, t), false),
                ("t = 0 floor",               Termination.LossyGround(sigma, 0.0),            false),
            })
            {
                var stack = new LayerStack(floor, lossless.Layers, lossless.Top);
                var modes = SurfaceWavePoles.Find(stack, f).Modes;
                double off = modes.Count == 0 ? double.NaN
                           : Math.Abs(modes[0].KRho.Imaginary) / Math.Abs(modes[0].KRho.Real);
                bool ok = SommerfeldIntegral.CanIntegrateLayered(stack).Ok;
                output.WriteLine($"  {name}, tanδ = 0, {tag,-16}{stack.Dissipates,10}{(ok ? "yes" : "NO"),10}   {off:E3}");
                Assert.Equal(expect, stack.Dissipates);
                Assert.Equal(expect, ok);
                if (expect) Assert.True(off > 1e-7, "a real floor must actually move the pole off the axis");
                else        Assert.Equal(0.0, off);
            }
        }

        // The whole-stack test is the one place it is written, and it reads the SAME rule
        // PlanarSurfaceImpedance.Plane opens with — so "does not dissipate" and "Z_s is exactly
        // zero" cannot drift apart.
        foreach (var (sigma, t) in new[] { (CopperSigma, 35e-6), (0.0, 35e-6),
                                           (double.PositiveInfinity, 35e-6), (CopperSigma, 0.0) })
        {
            var term = Termination.LossyGround(sigma, t);
            Assert.Equal(term.SurfaceImpedanceAt(2 * Math.PI * f) != Complex.Zero, term.Dissipates);
        }

        // A PEC is a CONDUCTOR and does not DISSIPATE — the two predicates ask different questions
        // and CL4's four narrowed call sites depend on the first, not on this one.
        Assert.True(Termination.Pec.IsConductor);
        Assert.False(Termination.Pec.Dissipates);
        Assert.False(Termination.Pmc.Dissipates);
        // An open termination into a lossy half-space dissipates too.
        Assert.True(Termination.OpenTo(new EmMaterial(11.9, 0.005)).Dissipates);
        Assert.False(Termination.Air.Dissipates);
        Assert.True(LayerStacks.FilmOnSilicon.Dissipates);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // R-cl5-2 — the ε-ladder
    // ═══════════════════════════════════════════════════════════════════════════════════════

    private static readonly double[] Ladder = [1e-3, 1e-6, 1e-9, 1e-12, 1e-16, 0.0];

    /// <summary>The un-widened error ceiling inside <see cref="Dcim.ValidatedRhoOverLambdaLayered"/>
    /// — CL4 §3's own, scaled on the free-space kernel. <b>It is not the same number as the validated
    /// RANGE</b>, which is ρ/λ ≤ 1; CL5's brief quoted the two as one and they are separate
    /// measurements.</summary>
    private const double Ceiling = 1.6e-2;

    /// <summary>
    /// One rung of the ε-ladder: the fit at <paramref name="tanD"/>, against the direct integrator
    /// where that is admitted, and against the LOSSLESS fit, worst over <paramref name="rls"/>,
    /// scaled on the free-space kernel.
    /// </summary>
    private static (double Pole, bool Direct, double FitResidual, double VsDirect, double VsZero)
        Rung(LayerStack pec, double tanD, double f, GreensKernel kind, double[] rls, Complex[] zeroAt,
             bool useDirect)
    {
        var stack = AtTanD(pec, tanD);
        var g = new LayeredSpectralGreens(stack, f);
        var modes = SurfaceWavePoles.Find(stack, f).Modes;
        double off = modes.Count == 0 ? double.NaN
                   : Math.Abs(modes[0].KRho.Imaginary) / Math.Abs(modes[0].KRho.Real);
        bool direct = useDirect && SommerfeldIntegral.CanIntegrateLayered(stack).Ok;

        var m = Dcim.FitAtHeights(g, kind, pec.TopZ, pec.TopZ);
        double lam = EmConstants.C0 / f, worstDirect = 0, continuity = 0;
        for (int i = 0; i < rls.Length; i++)
        {
            double rho = rls[i] * lam, fs = 1.0 / (4 * Math.PI * rho);
            Complex got = m.EvaluateAtHeights(rho);
            continuity = Math.Max(continuity, (got - zeroAt[i]).Magnitude / fs);
            if (direct)
                worstDirect = Math.Max(worstDirect,
                    (got - SommerfeldIntegral.EvaluateLayered(g, kind, rho, pec.TopZ, pec.TopZ).Value)
                    .Magnitude / fs);
        }
        return (off, direct, m.FitResidual, worstDirect, continuity);
    }

    private Complex[] LosslessFitAt(LayerStack pec, double f, GreensKernel kind, double[] rls)
    {
        var m = Dcim.FitAtHeights(new LayeredSpectralGreens(AtTanD(pec, 0.0), f), kind, pec.TopZ, pec.TopZ);
        double lam = EmConstants.C0 / f;
        return rls.Select(rl => m.EvaluateAtHeights(rl * lam)).ToArray();
    }

    /// <summary>
    /// <b>The ladder's own verdict, applied identically by the routine and the tagged test.</b>
    ///
    /// <para>The claim is that the fit CONVERGES as the loss goes to zero rather than degrading, and
    /// the measurement makes the shape of that precise: the <c>vs-tanδ=0</c> column falls by three
    /// decades from tanδ = 1e-3 to 1e-6 and then FLOORS at a few times 1e-7, which is the fit's own
    /// reproducibility and not the physics — below tanδ ≈ 1e-6 the two kernels differ by less than
    /// two Prony fits of the same kernel do. So monotonicity is asserted only where the column is
    /// still signal, and below that the floor itself is bounded. A fit that degraded as the loss
    /// went away would break both halves at once.</para>
    /// </summary>
    private static void AssertLadderConverges(string what, IReadOnlyList<(double TanD, double VsZero)> rows)
    {
        double top = rows[0].VsZero;
        foreach (var (td, e) in rows)
        {
            Assert.True(e <= Ceiling, $"{what} tanδ = {td:E0}: {e:E3} from the lossless fit, past {Ceiling:G3}");
            if (td <= 1e-6)
                Assert.True(e < top / 100.0,
                    $"{what} tanδ = {td:E0}: {e:E3} is not two decades inside the tanδ = 1e-3 rung's " +
                    $"{top:E3} — the ladder is not converging");
            // …and the floor it converges ONTO is the fit's own reproducibility, four decades inside
            // the ceiling. Stated as a bound so a fit that started depending on the loss would fail.
            if (td <= 1e-9) Assert.True(e < 1e-5, $"{what} tanδ = {td:E0}: floor {e:E3} is not the fit's own noise");
        }
    }

    /// <summary>
    /// <b>The fitted kernel CONVERGES as the loss goes to zero; it does not degrade.</b> The same
    /// guided stack at tanδ = 1e-3 … 1e-16 and at 0, both starters, both kernels, over the whole of
    /// <see cref="Dcim.ValidatedRhoOverLambdaLayered"/> (ρ/λ ≤ 1; <b>1.6e-2 is the ERROR ceiling
    /// inside it</b>, and neither is widened here).
    ///
    /// <para>Two columns, and they answer different halves of the brief. <c>vs-direct</c> is the fit
    /// against <see cref="SommerfeldIntegral.EvaluateLayered"/> — an INDEPENDENT reference, and the
    /// one that means anything at the top of the ladder where the integrator is healthy.
    /// <c>vs-tanδ=0</c> is each rung against the lossless fit, which is the ladder's own continuity
    /// statement and is what carries the bottom of it.</para>
    ///
    /// <para><b>It also measures the direct integrator's own degradation, which is the evidence for
    /// the milestone-4 decision</b> (§CL5): the integrator is formally ADMITTED at tanδ = 1e-16 and
    /// is two decades worse there than at 1e-3, because the pole-on-the-contour problem is
    /// continuous and a tanδ &gt; 0 test is a binary proxy for it.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Benchmark")]
    public void R_cl5_2_TheEpsilonLadder()
    {
        double[] rls = [1e-4, 1e-3, 1e-2, 1e-1, 1.0];
        const double f = 10e9;

        foreach (var (name, pec, _, _) in Starters())
        {
            output.WriteLine($"=== {name}, {f / 1e9:F0} GHz — ρ/λ ≤ {Dcim.ValidatedRhoOverLambdaLayered}, " +
                             $"scaled on the free-space kernel, ceiling {Ceiling:G3} ===");
            foreach (var kind in new[] { GreensKernel.ScalarPotential, GreensKernel.VectorPotential })
            {
                var zeroAt = LosslessFitAt(pec, f, kind, rls);
                output.WriteLine($"  {kind}");
                output.WriteLine("      tanδ    pole |Im|/Re   direct   fit residual    vs-direct    vs-tanδ=0");
                var rows = new List<(double, double)>();
                foreach (double td in Ladder)
                {
                    var r = Rung(pec, td, f, kind, rls, zeroAt, useDirect: true);
                    output.WriteLine($"   {td,9:E0}  {r.Pole,12:E3}   {(r.Direct ? "yes" : "NO "),6}   {r.FitResidual,12:E3} " +
                                     $"{(r.Direct ? $"{r.VsDirect,12:E3}" : "           —")} {r.VsZero,12:E3}");
                    rows.Add((td, r.VsZero));

                    // The INDEPENDENT reference, where the integrator is healthy. Below tanδ = 1e-9
                    // it is reported and not asserted — the contour is running into the pole, which
                    // is the direct integrator's own problem and is measured in the column above.
                    if (r.Direct && td >= 1e-9)
                        Assert.True(r.VsDirect <= Ceiling,
                            $"{name} {kind} tanδ = {td:E0}: {r.VsDirect:E3} against the direct integrator");
                }
                AssertLadderConverges($"{name} {kind}", rows);
            }
            output.WriteLine("");
        }
    }

    /// <summary>
    /// <b>The ladder's routine-tier counterpart</b> — one starter, one kernel, the near field only,
    /// and no direct integration at all. The tagged sweep above measures how well the fit agrees with
    /// an independent reference; this is the half that catches the thing worth catching on every
    /// run, which is a fit that stops working as the loss goes away.
    /// </summary>
    [Fact]
    public void R_cl5_2b_TheLadderConverges_OnOneStackAndOneKernel()
    {
        const double f = 10e9;
        double[] rls = [1e-3, 1e-2];
        var pec = LayerStacks.Fr4Slab;
        var kind = GreensKernel.ScalarPotential;
        var zeroAt = LosslessFitAt(pec, f, kind, rls);

        output.WriteLine("      tanδ    pole |Im|/Re   fit residual    vs-tanδ=0");
        var rows = new List<(double, double)>();
        foreach (double td in Ladder)
        {
            var r = Rung(pec, td, f, kind, rls, zeroAt, useDirect: false);
            output.WriteLine($"   {td,9:E0}  {r.Pole,12:E3}   {r.FitResidual,12:E3} {r.VsZero,12:E3}");
            rows.Add((td, r.VsZero));
        }
        AssertLadderConverges("FR-4 ScalarPotential", rows);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // R-cl5-3 — the live defect closes, through PlanarSolve
    // ═══════════════════════════════════════════════════════════════════════════════════════

    private static PlanarPolygon Rect(double x0, double y0, double x1, double y1) =>
        new([new EmPoint(x0, y0), new EmPoint(x1, y0), new EmPoint(x1, y1), new EmPoint(x0, y1)]);

    /// <summary>A 120 µm line on M1 with a 3 µm-high overlay pad on M2 — an overlap capacitor, which
    /// is the ordinary reason a design has two levels at all. Every dielectric is at
    /// <paramref name="tanD"/>, so at zero this is the εᵣ &gt; 1, tanδ = 0 guided stack the old
    /// predicate refused by name.</summary>
    private static PlanarProblem OverlapCap(double tanD, double fHz, double lengthM = 120e-6,
                                            double wM = 40e-6)
    {
        var stack = new LayerStack(Termination.Pec,
            [new MediumLayer(100e-6, new EmMaterial(12.90, tanD)),
             new MediumLayer(  3e-6, new EmMaterial( 2.70, tanD))],
            Termination.Air);
        return new PlanarProblem(
            [new PlanarConductorLayer("M1", [Rect(0, 0, lengthM, wM)], GoldSigma, 2e-6, stack.InterfaceZ[1]),
             new PlanarConductorLayer("M2", [Rect(0.35 * lengthM, 0, 0.65 * lengthM, wM)], GoldSigma, 3e-6,
                                      stack.TopZ)],
            new GroundedSlab(100e-6, new EmMaterial(12.9, tanD)), fHz, null, stack);
    }

    /// <summary>
    /// <b>The gate that says this brief reached a user.</b> It is the only one driven through
    /// <see cref="PlanarSolve"/> rather than through the fit, and before CL5 the tanδ = 0 row did not
    /// produce a wrong number — it THREW, citing an integrator its run never reaches.
    ///
    /// <para>The tolerance is the one that case's own loss justifies, and it is stated as a RATIO
    /// rather than as a constant: the tanδ = 0 and tanδ = 1e-16 answers must agree far more closely
    /// than either differs from the real GaAs substrate at tanδ = 2e-3, which is the only scale on
    /// this problem that means anything.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Benchmark")]          // measured: 8.3 s — three de-embedded two-level solves
    public void R_cl5_3_ATwoLevelDesignOnAnIdealSubstrateSolves()
    {
        const double f = 10e9, len = 120e-6, w = 40e-6;
        var mesh = new PlanarMeshSettings(Auto: false, CellsPerWavelength: 20, EdgeMesh: false);
        var got = new List<(double TanD, Complex[] S)>();

        foreach (double td in new[] { 0.0, 1e-16, 2e-3 })
        {
            var problem = OverlapCap(td, f, len, w);
            var m = SurfaceMesher.Mesh(problem, mesh).Mesh;
            var ports = PlanarPorts.ResolveAll(m,
            [
                new PlanarPort(1, new EmPoint(0,   0.5 * w), PlanarPortSide.MinX, 50.0, 0),
                new PlanarPort(2, new EmPoint(len, 0.5 * w), PlanarPortSide.MaxX, 50.0, 0),
            ]);
            bool admitted = SommerfeldIntegral.CanIntegrateLayered(problem.EffectiveStack).Ok;
            var p = PlanarSolve.Run(problem, m, ports, [f]).Points[0];
            output.WriteLine($"tanδ = {td,-8:E0} N = {m.Bases.Count,4}  direct integrator {(admitted ? "admitted" : "REFUSED "),8}  " +
                             $"Z_c = {p.Calibrations[0].Zc:F4}  S11 = {p.S[0, 0]:F6}  S21 = {p.S[1, 0]:F6}");
            Assert.All(new[] { p.S[0, 0], p.S[0, 1], p.S[1, 0], p.S[1, 1] },
                       s => Assert.True(double.IsFinite(s.Magnitude)));
            got.Add((td, [p.S[0, 0], p.S[0, 1], p.S[1, 0], p.S[1, 1]]));
        }

        // The stack the run is actually on IS the one the old predicate refused — otherwise this
        // gate would be passing for the wrong reason.
        Assert.False(SommerfeldIntegral.CanIntegrateLayered(OverlapCap(0.0, f).EffectiveStack).Ok);

        double Worst(int a, int b) => got[a].S.Zip(got[b].S, (x, y) => (x - y).Magnitude).Max();
        double epsilon = Worst(0, 1), physical = Worst(0, 2);
        output.WriteLine($"worst |ΔS|, tanδ = 0 against 1e-16: {epsilon:E3}");
        output.WriteLine($"worst |ΔS|, tanδ = 0 against the real substrate's 2e-3: {physical:E3}" +
                         $"   — the ε agreement is {physical / epsilon:F0}× tighter");
        Assert.True(epsilon < 0.05 * physical,
            $"tanδ = 0 and tanδ = 1e-16 differ by {epsilon:E3}, which is not small against the " +
            $"{physical:E3} the substrate's own loss is worth");
        Assert.True(physical > 1e-4, "the loss comparison is vacuous — the substrate did nothing");
    }

    /// <summary>
    /// <b>R-cl5-3's routine-tier counterpart</b> — the CROSS-REGION fit itself, which is the exact
    /// call the de-embedded solve above used to die inside. A two-level design reaches
    /// <see cref="Dcim.FitAtHeights"/> through <see cref="PlanarKernelSet.Model"/> for every pairing
    /// that is not high-high, and on a tanδ = 0 guided stack that threw
    /// <see cref="ArgumentException"/> with the direct integrator's sentence.
    ///
    /// <para>The expensive half above measures that the ANSWER is right; this is the half that
    /// catches the refusal coming back, and it costs a few milliseconds. It also pins the direction:
    /// the same stack is still refused by <see cref="SommerfeldIntegral.CanIntegrateLayered"/>, so a
    /// future edit that "fixed" this by deleting that refusal fails
    /// <see cref="R_cl5_4_TheDirectIntegratorStillRefusesALosslessGuidedStack_ByTheContour"/> instead.</para>
    /// </summary>
    [Fact]
    public void R_cl5_3b_TheCrossRegionFitAnIdealTwoLevelStackNeeds_IsBuilt()
    {
        const double f = 10e9;
        var problem = OverlapCap(0.0, f);
        var stack = problem.EffectiveStack;
        Assert.False(SommerfeldIntegral.CanIntegrateLayered(stack).Ok,
                     "the direct integrator must still refuse this stack, or the gate is vacuous");

        double zLow = stack.InterfaceZ[1], zHigh = stack.TopZ;
        var set = new PlanarKernelSet(new LayeredSpectralGreens(stack, f));
        foreach (var kind in new[] { GreensKernel.ScalarPotential, GreensKernel.VectorPotential })
        foreach (var (tag, a, b) in new[] { ("same-region interior", zLow, zLow),
                                            ("cross-region",         zLow, zHigh) })
        {
            var m = set.Model(kind, a, b);
            output.WriteLine($"{kind,-16} {tag,-22} {m.Images.Count} images, {m.SurfaceWaves.Count} SW, " +
                             $"residual {m.FitResidual:E3}");
            Assert.True(m.IsAtHeights);
            Assert.True(double.IsFinite(m.FitResidual) && m.FitResidual < 1e-2);
        }
    }
}
