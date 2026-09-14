// PCAL6 — WHICH CALIBRATION SEPARATION A GROUPED PORT USES, and the refusal that choice can cause.
// `docs/sonnet-briefs/brief-portcal-6-separation-selection.md`; findings in
// `src/Engine/Mom/RESOLVED.md`, "PCAL6".
//
// The brief opened on a board where 200 MHz was refused in one sweep and accepted in another, and on
// the theory that the SELECTION rule was at fault. M1 refuted that: the measured separation is not
// |Δγ|·Δℓ at all below about half a gigahertz, because the SHORT standard — 3 h of substrate, which
// is 1.5° of line at 200 MHz — cannot separate the modes at any Δℓ. What is gated here is that
// finding, the recovery drawn from it, and the two properties the brief asked for on their own terms
// (one decision point, and a choice that does not depend on sweep history).
//
// PCAL7 then asked whether the per-frequency refusal is drawn on the wrong quantity and should be
// deleted in favour of the setup guard's; the answer is no, and the last two tests here are why.
// `docs/sonnet-briefs/brief-portcal-7-separation-gate.md`; RESOLVED.md, "PCAL7".
//
// The geometry is the series' own coupled pair on 0.9 mm FR-4, driven at both ends of both
// conductors — the case PCAL1 measured and PCAL4 turned into a calibration group.

using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using Xunit;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

public sealed class PlanarGroupSeparationTests(ITestOutputHelper output)
{
    private static readonly GroundedSlab Slab = new(0.9e-3, new EmMaterial(4.4, 0.02));
    private const double W = 254e-6, Len = 3.83e-3, MeshF = 5e9, Gap = 0.27;

    private static PlanarPort[] Ports()
    {
        double g = Gap * Slab.HeightM;
        return
        [
            new PlanarPort(1, new EmPoint(0,   0.5 * W), PlanarPortSide.MinX, 50.0),
            new PlanarPort(2, new EmPoint(Len, 0.5 * W), PlanarPortSide.MaxX, 50.0),
            new PlanarPort(3, new EmPoint(0,   W + g + 0.5 * W), PlanarPortSide.MinX, 50.0),
            new PlanarPort(4, new EmPoint(Len, W + g + 0.5 * W), PlanarPortSide.MaxX, 50.0),
        ];
    }

    private static PlanarProblem Problem()
    {
        double g = Gap * Slab.HeightM;
        return PlanarLineFixtures.Problem(Slab, MeshF,
            PlanarLineFixtures.Rect(0, 0, Len, W),
            PlanarLineFixtures.Rect(0, W + g, Len, W + g + W));
    }

    private static (PlanarProblem P, PlanarMesh M, IReadOnlyList<PlanarPortResolution> Ports) Board()
    {
        var problem = Problem();
        var mesh = SurfaceMesher.Mesh(problem, PlanarLineFixtures.Coarse).Mesh;
        return (problem, mesh, PlanarPorts.ResolveAll(mesh, Ports()));
    }

    /// <summary>One grouped port resolution, for the pieces that need a calibrator and not a run.</summary>
    private static (PlanarPortResolution Port, int K) GroupedPort()
    {
        var (_, mesh, ports) = Board();
        int k = PlanarCalibration.EndRunCellsFor(ports[0], Slab);
        var g = PlanarPorts.TryFormCalibrationGroup(
            mesh, ports[0], ports, k, 5.0 * Slab.HeightM, 3, PlanarConductors.Of(mesh), out _);
        Assert.NotNull(g);
        return (ports[0] with { Group = g }, k);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-pcal6-1 — A RUN THAT SUCCEEDS TODAY IS BIT-IDENTICAL
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The literals below are asserted as EXACT equality, which is what makes them a gate rather
    /// than a tolerance.</b> §LF1 §5(b) declined this work on the grounds that it "changes
    /// de-embedded answers for every grouped port", and confining every change to runs that
    /// currently STOP is what removes the objection — so the thing that has to be proved is that a
    /// grouped sweep which calibrates today is untouched.
    ///
    /// <para><b>CL3 RE-BLESSED IT, AND THE PRE-CL3 LITERALS ARE STILL HERE AS THE OTHER HALF OF THE
    /// GATE.</b> <c>PlanarFillSettings.ConductorLoss</c> is on by default now, so this fixture's
    /// 35 µm copper is real metal and the de-embedded answer legitimately moved — by <b>4.8e-4 to
    /// 2.8e-3 relative</b> across the eight entries, which is the size of α_c on 1.6 mm-class FR-4
    /// at 1-7 GHz (the series overview's 8.8 % at 1 GHz, once). Both sets are asserted: the lossy
    /// one against the shipped default, the PEC one against
    /// <see cref="PlanarFillSettings.PerfectConductor"/>. <b>Asserting the oracle is the point</b> —
    /// a re-bless that only moved the numbers would not say whether the move came from the metal or
    /// from something else in the same commit, and every CL1/CL2 accuracy gate is a comparison
    /// against exactly this flag.</para>
    /// </summary>
    [Fact]
    public void AGroupedSweepThatCalibratesTodayIsBitIdentical()
    {
        var (problem, mesh, ports) = Board();
        var r   = PlanarSolve.Run(problem, mesh, ports, [1e9, 3e9, 5e9, 7e9]);
        var pec = PlanarSolve.Run(problem, mesh, ports, [1e9, 3e9, 5e9, 7e9],
                                  new PlanarSolveSettings(
                                      PlanarFillSettings.Default with { PerfectConductor = true }));

        // CL3 — the shipped default, real copper. Re-blessed from the PEC column beside it.
        (int Point, int I, double Re, double Im)[] lossy =
        [
            (0, 0,  0.06391205616018335,   0.18747501662593496),
            (0, 1,  0.9349078429968425,   -0.2207389474994221),
            (0, 2,  0.059816170917528576,  0.13940222659014842),
            (0, 3, -0.059250356141446664, -0.1277447743104419),
            (3, 0,  0.2508735945679807,    0.28492634386881016),
            (3, 1,  0.7145536009396143,   -0.5172432641453646),
            (3, 2,  0.16870041214140055,   0.1294002224362011),
            (3, 3, -0.1482320809491303,   -0.050820039971441286),
        ];

        // Recorded at HEAD, before any PCAL6 change, from this same fixture and sweep — and still
        // reproduced EXACTLY by the PEC oracle three briefs later.
        (int Point, int I, double Re, double Im)[] pre =
        [
            (0, 0,  0.06347998547490756,  0.18730399232764747),
            (0, 1,  0.93533999509545760, -0.22056785238784363),
            (0, 2,  0.05980324010508564,  0.13954934362027435),
            (0, 3, -0.05923747890684179, -0.12789193719963166),
            (3, 0,  0.24991421902212263,  0.28509027866550040),
            (3, 1,  0.71552607105961430, -0.51740058915066640),
            (3, 2,  0.16901297308619603,  0.12970768367527550),
            (3, 3, -0.14855289041339780, -0.05113086018271953),
        ];

        for (int k = 0; k < lossy.Length; k++)
        {
            var (pt, i, re, im) = lossy[k];
            Assert.Equal(re, r.Points[pt].S[i, 0].Real);              // EXACT — no tolerance
            Assert.Equal(im, r.Points[pt].S[i, 0].Imaginary);

            var (ppt, pi, pre_, pim) = pre[k];
            Assert.Equal(pre_, pec.Points[ppt].S[pi, 0].Real);        // EXACT — the PEC oracle
            Assert.Equal(pim,  pec.Points[ppt].S[pi, 0].Imaginary);

            double moved = (r.Points[pt].S[i, 0] - pec.Points[pt].S[i, 0]).Magnitude
                         / pec.Points[pt].S[i, 0].Magnitude;
            output.WriteLine($"point {pt} S[{i},0]  PEC {pec.Points[pt].S[i, 0]}  " +
                             $"lossy {r.Points[pt].S[i, 0]}  moved {moved:E2}");

            // Conductor loss moves this fixture and it moves it by a KNOWN amount. A move an order
            // either side of that is not a re-bless, it is something else in the same commit.
            Assert.InRange(moved, 1e-4, 1e-2);
        }

        Assert.DoesNotContain(r.Notes, n => n.Contains("SHORT standard", StringComparison.Ordinal));
        output.WriteLine($"1 GHz S11 = {r.Points[0].S[0, 0]}, S21 = {r.Points[0].S[1, 0]}");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // §8 gate 2 — THE DEFECT ITSELF, AS A FIXTURE RATHER THAN A BOARD
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>200 MHz is refused in one sweep and accepted in another, on the same metal, the same
    /// ports, the same group and the same floor.</b> That is the owner's §1 rows 1 and 4 reduced to
    /// a fixture, and it is what says the mechanism is understood: the two sweeps differ in their
    /// BAND, the band is what <see cref="PlanarCalibration.SuggestDeltas"/> sizes the separations
    /// from, and the separation is what the measurement is made over.
    ///
    /// <para><b>CL3 MOVED THE SECOND BAND, AND WHY IT HAD TO IS ITSELF A FINDING.</b> With the
    /// metal a real conductor the two modes are MORE separable, not less: the cascade's eigenvalues
    /// are e^{±γ_m Δℓ} and the even and odd modes carry different current distributions, so they
    /// differ in α as well as in β and |Δγ|·Δℓ grows. Measured on this fixture at 200 MHz — PEC
    /// against real copper — <b>0.405° → 0.56°</b> over a 200 MHz - 1 GHz band, which takes that
    /// band from under the 0.50° floor to over it. The phenomenon this test exists for is
    /// unchanged and is demonstrated one rung narrower: a 200 MHz - 400 MHz band reads 0.255° PEC
    /// and <b>0.405°</b> with real metal, and refuses on both.</para>
    ///
    /// <para><b>The band moved rather than the metal.</b> Pinning this with
    /// <see cref="PlanarFillSettings.PerfectConductor"/> was the other option and is refused on the
    /// brief's own terms: the PEC flag is an oracle for comparing two answers, and a gate that
    /// flipped it would be testing a refusal no user can reach.</para>
    /// </summary>
    [Fact]
    public void TheSameFrequencyIsRefusedInOneSweepAndAcceptedInAnother()
    {
        var (problem, mesh, ports) = Board();

        // 100 MHz - 1 GHz: separations 171.0 and 54.1 mm. 200 MHz reads 1.31° and publishes.
        var ok = PlanarSolve.Run(problem, mesh, ports, [100e6, 200e6, 1e9]);
        Assert.Contains(ok.Notes, n => n.Contains("200 MHz", StringComparison.Ordinal)
                                    && n.Contains("separation 1.31°", StringComparison.Ordinal));

        // 200 - 400 MHz: a shorter pair of separations again. The SAME 200 MHz reads 0.405° and stops.
        var ex = Assert.Throws<PlanarFeedClearanceRefusedException>(
            () => PlanarSolve.Run(problem, mesh, ports, [200e6, 400e6]));
        output.WriteLine(ex.Message);

        Assert.Contains("at 200 MHz", ex.Message, StringComparison.Ordinal);
        Assert.Contains("0.405°", ex.Message, StringComparison.Ordinal);

        // ── And this is the half that says the refusal is not about the metal ──────────────────
        //
        // The refusal reports the same quantity taken from the group's own electrostatics, which
        // puts the two modes SEVEN TIMES further apart than the measurement did. A genuinely
        // degenerate pair reads small on both, and the pair of numbers is the only thing that
        // separates the two cases — so the message carries both.
        Assert.Contains("electrostatics puts the same quantity at 3.008°", ex.Message,
                        StringComparison.Ordinal);
        Assert.Contains("short standard of 3.83 mm", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The mode-separation refusal quotes four measured sizes, and this is what holds them.</b>
    ///
    /// <para>It is a user-facing remedy carrying numbers, so something has to. The ones it used to
    /// print were taken at PCAL7 on a different board with PEC metal; CL3 both moved them and showed
    /// that a real conductor does not move a mode separation monotonically, so the sentence had come
    /// to read as "narrow the band and the separation rises" — which is not what the lever does.</para>
    ///
    /// <para>The CHEAP half of that re-measurement lives here (one coarse mesh, N = 48, two bands);
    /// the edge-mesh half costs a group calibration on N = 424 and is
    /// <see cref="TheEdgeMeshMovesTheSameSeparationTheOtherWay"/>, tagged.</para>
    /// </summary>
    [Fact]
    public void TheBandIsALever_AndTheRefusalQuotesTheMeasuredSizes()
    {
        var problem = Problem();
        var mesh    = SurfaceMesher.Mesh(problem, PlanarLineFixtures.Coarse).Mesh;
        var ports   = PlanarPorts.ResolveAll(mesh, Ports());
        output.WriteLine($"edge mesh OFF: N = {mesh.Bases.Count}");

        // 200 - 800 MHz publishes at 1.29°; 200 - 400 MHz refuses at 0.405°. Same metal, same ports.
        var wide = PlanarSolve.Run(problem, mesh, ports, [200e6, 800e6]);
        Assert.Contains(wide.Notes, n => n.Contains("Worst mode separation 1.29°", StringComparison.Ordinal));

        var ex = Assert.Throws<PlanarFeedClearanceRefusedException>(
            () => PlanarSolve.Run(problem, mesh, ports, [200e6, 400e6]));
        Assert.Contains("at 200 MHz", ex.Message, StringComparison.Ordinal);
        Assert.Contains("0.405°", ex.Message, StringComparison.Ordinal);
        output.WriteLine(ex.Message);

        // …and the message quotes exactly the four figures the two halves measured, so it cannot
        // drift from the fixture the way PCAL7's did.
        foreach (string n in new[] { "0.405°", "1.29°", "0.96°", "0.155°" })
            Assert.Contains(n, ex.Message, StringComparison.Ordinal);
        Assert.Contains("NOT A RULE", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>…and the edge mesh moves the same 200 MHz point the OTHER way, which is the half that
    /// makes the direction not a rule.</b> With it on, the 200 - 400 MHz band that refused at 0.405°
    /// publishes at 0.96°, and the 200 - 800 MHz band that published at 1.29° refuses at 0.155°.
    ///
    /// <para>Tagged because the edge-meshed fixture is N = 424 and each band is a full grouped
    /// de-embedded calibration — ~2.5 min together, well past the routine tier's ~5 s. The cheap half
    /// and the message's own four figures stay in the default gate
    /// (<see cref="TheBandIsALever_AndTheRefusalQuotesTheMeasuredSizes"/>).</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Benchmark")]   // N = 424 grouped calibration on two bands, ~2.5 min
    public void TheEdgeMeshMovesTheSameSeparationTheOtherWay()
    {
        var problem = Problem();
        var mesh    = SurfaceMesher.Mesh(problem, PlanarLineFixtures.Coarse with { EdgeMesh = true }).Mesh;
        var ports   = PlanarPorts.ResolveAll(mesh, Ports());
        output.WriteLine($"edge mesh ON: N = {mesh.Bases.Count}");

        var narrow = PlanarSolve.Run(problem, mesh, ports, [200e6, 400e6]);
        Assert.Contains(narrow.Notes, n => n.Contains("Worst mode separation 0.96°", StringComparison.Ordinal));

        var ex = Assert.Throws<PlanarFeedClearanceRefusedException>(
            () => PlanarSolve.Run(problem, mesh, ports, [200e6, 800e6]));
        Assert.Contains("at 200 MHz", ex.Message, StringComparison.Ordinal);
        Assert.Contains("0.155°", ex.Message, StringComparison.Ordinal);
        output.WriteLine(ex.Message);
    }

    /// <summary>
    /// <b>R-pcal6-6 — the floor does not move and a refusal stays a refusal.</b> Where BOTH
    /// numbers are under the floor the modes are genuinely degenerate, and the message says so by
    /// carrying the two of them; nothing recovers, because PCAL6/M3 measured that the one remedy
    /// available — a longer short standard — makes the published answer worse rather than better.
    /// </summary>
    [Fact]
    public void GenuinelyDegenerateModesStillRefuse()
    {
        var (problem, mesh, ports) = Board();
        var strict = PlanarCalibrationSettings.Default with { ModeSeparationFloorDegrees = 1e4 };

        var ex = Assert.Throws<PlanarFeedClearanceRefusedException>(
            () => PlanarSolve.Run(problem, mesh, ports, [1e9, 3e9, 5e9, 7e9],
                                  new PlanarSolveSettings(Calibration: strict)));
        output.WriteLine(ex.Message[..Math.Min(200, ex.Message.Length)]);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-pcal6-3 / R-pcal6-4 — ONE DECISION POINT, AND IT DOES NOT REMEMBER THE SWEEP
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>A grouped frequency picks the same separation whichever frequencies preceded it.</b>
    /// Before PCAL6 the choice was made on the previous solved point's β scaled by frequency, so the
    /// two sweeps below — same band, same candidates, same metal — could hand 7 GHz two different
    /// standards and publish two different S there with nothing to mark it.
    /// </summary>
    [Fact]
    public void TheSeparationAFrequencyUsesDoesNotDependOnTheSweep()
    {
        var (port, _) = GroupedPort();
        var kern = PlanarLineFixtures.Kernel(Slab, 7e9);

        int Walk(double[] sweep)
        {
            var cal = new PlanarPortCalibrator(port, Slab, 1e9, 7e9);
            foreach (double f in sweep)
                cal.ModalAt(() => PlanarLineFixtures.Kernel(Slab, f) is var kp ? PlanarFrequencyKernel.FromPair(kp) : null!, f);
            return cal.SelectedIndexAt(7e9);
        }

        Assert.Equal(Walk([1e9, 7e9]), Walk([1e9, 2e9, 4e9, 7e9]));
        Assert.NotNull(kern);

        // …and the question is the same question the SETUP guard asks, which is R-pcal6-3: before
        // PCAL6 the guard predicted β from the pre-solve estimate while the sweep predicted it from
        // the previous point, so the run could solve one standard and report about another.
        var fresh = new PlanarPortCalibrator(port, Slab, 1e9, 7e9);
        int before = fresh.SelectedIndexAt(7e9);
        fresh.ModalAt(() => PlanarFrequencyKernel.FromPair(PlanarLineFixtures.Kernel(Slab, 1e9)), 1e9);
        Assert.Equal(before, fresh.SelectedIndexAt(7e9));
        output.WriteLine($"7 GHz selects separation {before} whatever preceded it");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-pcal6-7 — THE SETUP GUARD, AND WHY THE BAND'S BOTTOM IS NOT THE WORST POINT
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The quasi-static separation is NOT monotone in frequency, so asking only at fLo can miss.</b>
    /// Δβ is exactly proportional to frequency, so if Δℓ were fixed the bottom of the band would
    /// provably be the worst point — but <see cref="PlanarCalibration.SuggestDeltas"/> hands out one
    /// separation per sub-band and the selection steps DOWN to a shorter one as the frequency rises,
    /// dropping the product by most of that step at every switch.
    /// </summary>
    [Fact]
    public void TheWorstQuasiStaticSeparationIsNotAlwaysAtTheBandsBottom()
    {
        var (port, _) = GroupedPort();
        var cal = new PlanarPortCalibrator(port, Slab, 100e6, 1e9);

        double atLo = cal.QuasiStaticModeSeparationDegrees(100e6);
        double worst = atLo, at = 100e6;
        for (double f = 100e6; f <= 1e9; f += 5e6)
        {
            double sep = cal.QuasiStaticModeSeparationDegrees(f);
            if (sep < worst) { worst = sep; at = f; }
        }
        output.WriteLine($"quasi-static separation: {atLo:F3}° at 100 MHz, worst {worst:F3}° at " +
                         $"{at / 1e6:F0} MHz — the selection steps from 171.0 mm to 54.1 mm there");

        Assert.True(at > 100e6, "the worst point was the band's bottom after all");
        Assert.True(worst < atLo);
    }

    /// <summary>The guard therefore asks at every requested frequency, and names the one it found.</summary>
    [Fact]
    public void TheSetupGuardAsksAtEveryFrequency()
    {
        var (port, _) = GroupedPort();
        var cal = new PlanarPortCalibrator(port, Slab, 100e6, 1e9);

        double atLo = cal.QuasiStaticModeSeparationDegrees(100e6);
        var floor = PlanarCalibrationSettings.Default with
        {
            ModeSeparationFloorDegrees = 0.5 * (atLo + cal.QuasiStaticModeSeparationDegrees(305e6)),
        };

        // fLo alone passes this floor…
        PlanarSolve.GuardModeSeparation(cal, port, [100e6], floor, v => $"{v * 1e6:F0} µm");

        // …and the sweep that contains the frequency just past the candidate switch does not.
        var ex = Assert.Throws<PlanarFeedClearanceRefusedException>(
            () => PlanarSolve.GuardModeSeparation(cal, port, [100e6, 305e6, 1e9], floor,
                                                  v => $"{v * 1e6:F0} µm"));
        output.WriteLine(ex.Message);
        Assert.Contains("305 MHz", ex.Message, StringComparison.Ordinal);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-pcal6-2 / §8 — THE LADDER, KEPT AS THE MEASUREMENT IT IS
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Modal separation measured at one frequency on a chosen short line and Δℓ, against
    /// the quasi-static answer for the same pair — the ratio M1 laddered.</summary>
    private (double Measured, double QuasiStatic) MeasureAt(
        PlanarPortResolution port, int k, double shortTargetM, double dlM, double fHz)
    {
        var shortStd = PlanarCalibration.BuildLine(port, shortTargetM, k);
        var longStd  = PlanarCalibration.BuildLine(port, shortStd.LengthM + dlM, k);
        double dl    = longStd.LengthM - shortStd.LengthM;

        var cal = new PlanarPortCalibrator(port, Slab, 100e6, 1e9, null, null,
                                           standards: [shortStd, longStd]);
        var gc  = cal.ModalAt(
            () => PlanarFrequencyKernel.FromPair(PlanarLineFixtures.Kernel(Slab, fHz)), fHz);
        var med = cal.GroupMedium();
        double qs = Math.Abs(med.Beta(fHz, 0) - med.Beta(fHz, 1)) * dl * 180.0 / Math.PI;

        output.WriteLine($"  ℓ1 {shortStd.LengthM * 1e3,6:F2} mm ({med.Beta(fHz, 0) * shortStd.LengthM * 180 / Math.PI,5:F2}° " +
                         $"electrical), Δℓ {dl * 1e3,6:F2} mm: measured {gc.Box.ModeSeparationDegrees,7:F3}° " +
                         $"against quasi-static {qs,6:F3}° — ratio {gc.Box.ModeSeparationDegrees / qs:F3}");
        return (gc.Box.ModeSeparationDegrees, qs);
    }

    /// <summary>
    /// <b>M1's first half, and it is what refutes the brief's own framing: the measured separation
    /// is not |Δγ|·Δℓ.</b> Δβ is a property of the cross-section, so if the extraction were sound
    /// the measured separation would be exactly proportional to Δℓ and the ratio below would be a
    /// constant. It is not: on this pair at 200 MHz it runs from <b>17× too large</b> at the short
    /// end to well under 1 at the long, which is why a "choose the candidate that clears the floor"
    /// rule would be choosing on noise. The full ladder — eleven rungs at five frequencies — is in
    /// <c>src/Engine/Mom/RESOLVED.md</c>, §PCAL6.
    /// </summary>
    [Fact]
    public void TheMeasuredSeparationIsNotProportionalToTheSeparation()
    {
        var (port, k) = GroupedPort();
        output.WriteLine("Δℓ ladder at 200 MHz, short line held at its shipped 3 h:");

        double hi = MeasureAt(port, k, 3.0 * Slab.HeightM, 10e-3, 200e6) is var a
                  ? a.Measured / a.QuasiStatic : 0;
        double lo = MeasureAt(port, k, 3.0 * Slab.HeightM, 70e-3, 200e6) is var b
                  ? b.Measured / b.QuasiStatic : 0;

        Assert.True(hi > 10, $"ratio {hi:F3} at Δℓ = 10 mm — expected the short rung to read many " +
                             "times the modes' actual distance");
        Assert.True(lo < 1.0, $"ratio {lo:F3} at Δℓ = 70 mm");
        Assert.True(hi / lo > 10, "the ratio was near enough constant for |Δγ|·Δℓ to be the story");
    }

    /// <summary>
    /// <b>M1's second half, and it is the finding the fix is built on: the SHORT standard is the
    /// variable.</b> At a fixed Δℓ and a fixed frequency, lengthening ℓ₁ walks the measured
    /// separation monotonically onto the quasi-static one and holds it there. 3 h of substrate is
    /// 1.5° of line at 200 MHz; <see cref="PlanarCalibrationSettings.UsableLoDegrees"/> of it is
    /// 50 mm, and the whole recovery is that one substitution.
    /// </summary>
    [Fact]
    public void LengtheningTheShortStandardWalksTheMeasurementOntoTheTruth()
    {
        var (port, k) = GroupedPort();
        output.WriteLine("short-line ladder at 200 MHz, Δℓ held at 45 mm:");

        double last = double.PositiveInfinity;
        foreach (double shortM in (double[])[3.0 * Slab.HeightM, 10e-3, 30e-3])
        {
            var (m, qs) = MeasureAt(port, k, shortM, 45e-3, 200e6);
            double err = Math.Abs(m / qs - 1.0);
            Assert.True(err < last, $"|ratio − 1| went {last:F3} → {err:F3}; the short standard is " +
                                    "supposed to be the variable");
            last = err;
        }
        Assert.True(last < 0.15, $"|ratio − 1| = {last:F3} at 30 mm — expected the plateau");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // PCAL7 — TWO GATES, TWO QUANTITIES, AND NEITHER IS A SECOND SPELLING OF THE OTHER
    // ══════════════════════════════════════════════════════════════════════════════════════════
    //
    // `docs/sonnet-briefs/brief-portcal-7-separation-gate.md` asked whether the per-frequency
    // refusal is drawn on the wrong quantity and should move onto the quasi-static one — which
    // would make it redundant with the setup guard and delete it. The measurement (RESOLVED.md,
    // §PCAL7, 93 de-embedded points scored against the kernel-A oracle) says no: each gate catches a
    // failure the other misses, and the answers behind those two failures are 13.6x and 7.5x their
    // own A-vs-B floor. What is gated HERE is the structural half of that — that the two questions
    // have different answers on one fixture, so neither gate can stand in for the other. The
    // accuracy half is a harness, per the standing rule.

    /// <summary>Both separations for a single-point band, which is what a run built on that band
    /// would measure — the measured one off the solved cascade, the quasi-static one off the group's
    /// own electrostatics.</summary>
    private (double Measured, double QuasiStatic) BothAt(PlanarPortResolution port, double fHz)
    {
        var cal = new PlanarPortCalibrator(port, Slab, fHz, fHz);
        var gc  = cal.ModalAt(
            () => PlanarFrequencyKernel.FromPair(PlanarLineFixtures.Kernel(Slab, fHz)), fHz);
        var pair = (gc.Box.ModeSeparationDegrees, cal.QuasiStaticModeSeparationDegrees(fHz));
        output.WriteLine($"{SurfaceMesherEng(fHz)}: measured {pair.Item1:F3}°, " +
                         $"electrostatics {pair.Item2:F3}°");
        return pair;

        static string SurfaceMesherEng(double f) => $"{f / 1e9:F0} GHz";
    }

    /// <summary>
    /// <b>The per-frequency gate refuses a group the setup gate has already passed.</b> At 1 GHz on
    /// this fixture the electrostatics puts the two modes further apart than the standards managed
    /// to measure them, so a floor placed between the two readings is cleared by the quasi-static
    /// question and failed by the measured one. That is not hypothetical caution: PCAL7 measured a
    /// pair 4.4 mm apart at 500 MHz whose electrostatics read 0.517° — over the shipped floor, so
    /// the setup guard passes it — and whose measured separation read 0.026°; with the floor lifted
    /// it published max |ΔS| 0.999 against an A-vs-B floor of 0.073.
    /// </summary>
    [Fact]
    public void ThePerFrequencyGateRefusesAGroupTheSetupGatePassed()
    {
        var (problem, mesh, ports) = Board();
        var (port, _) = GroupedPort();

        var (measured, quasiStatic) = BothAt(port, 1e9);
        Assert.True(measured < quasiStatic,
                    $"measured {measured:F3}° is not below the electrostatic {quasiStatic:F3}° — " +
                    "this test needs the reading the standards UNDER-report to place its floor");

        var floor = PlanarCalibrationSettings.Default with
        {
            ModeSeparationFloorDegrees = 0.5 * (measured + quasiStatic),
        };
        var ex = Assert.Throws<PlanarFeedClearanceRefusedException>(
            () => PlanarSolve.Run(problem, mesh, ports, [1e9],
                                  new PlanarSolveSettings(Calibration: floor)));
        output.WriteLine(ex.Message);

        // The SWEEP's refusal, not the setup guard's — the two are told apart by their first clause.
        Assert.Contains("are calibrated together as one group, and at", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("would be calibrated together", ex.Message, StringComparison.Ordinal);

        // R-pcal7-7 — and because the electrostatic figure is OVER the floor, the remedy it names is
        // the one that binds on this case: the band and the mesh, not the feeds.
        //
        // **It names the two levers and no longer names a DIRECTION.** It used to say "narrowing the
        // sweep", which was measured on a PEC board at PCAL7 and is not what the lever does — with
        // the edge mesh on, narrowing goes the other way on this very fixture
        // (TheBandAndTheEdgeMeshAreTheTwoLevers). The assertion follows the message.
        Assert.Contains("moving either band edge", ex.Message, StringComparison.Ordinal);
        Assert.Contains("NOT A RULE", ex.Message, StringComparison.Ordinal);
        Assert.Contains("standards' own MESH", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Your metal is not the problem", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>And the reverse, which is why the setup gate cannot be dropped either.</b> At 7 GHz the
    /// measured separation OVER-reads the electrostatic one on this fixture, so a floor between them
    /// is failed by the quasi-static question and cleared by the measured one — the run stops before
    /// a standard is solved rather than publishing. PCAL7's own instance of this is an equal-width
    /// triple at 200 MHz: electrostatics 0.28°, measured 0.53° (over the shipped floor), and with
    /// the floor lifted it published 1.11 in max |ΔS| against a floor of 0.148.
    /// </summary>
    [Fact]
    public void TheSetupGateRefusesAGroupThePerFrequencyGateWouldHavePassed()
    {
        var (problem, mesh, ports) = Board();
        var (port, _) = GroupedPort();

        var (measured, quasiStatic) = BothAt(port, 7e9);
        Assert.True(quasiStatic < measured,
                    $"electrostatic {quasiStatic:F3}° is not below the measured {measured:F3}°");

        var floor = PlanarCalibrationSettings.Default with
        {
            ModeSeparationFloorDegrees = 0.5 * (measured + quasiStatic),
        };
        var ex = Assert.Throws<PlanarFeedClearanceRefusedException>(
            () => PlanarSolve.Run(problem, mesh, ports, [7e9],
                                  new PlanarSolveSettings(Calibration: floor)));
        output.WriteLine(ex.Message);

        Assert.Contains("would be calibrated together as one group", ex.Message, StringComparison.Ordinal);
        // Its remedy is the one about the METAL, and it stays that way: this refusal is the case
        // where the cross-section itself cannot be told apart.
        Assert.Contains("Separate the feeds", ex.Message, StringComparison.Ordinal);
    }
}
