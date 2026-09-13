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
    /// <b>The literals below were measured on the tree BEFORE this brief and are asserted as EXACT
    /// equality, which is what makes them a gate rather than a tolerance.</b> §LF1 §5(b) declined
    /// this work on the grounds that it "changes de-embedded answers for every grouped port", and
    /// confining every change to runs that currently STOP is what removes the objection — so the
    /// thing that has to be proved is that a grouped sweep which calibrates today is untouched.
    /// </summary>
    [Fact]
    public void AGroupedSweepThatCalibratesTodayIsBitIdentical()
    {
        var (problem, mesh, ports) = Board();
        var r = PlanarSolve.Run(problem, mesh, ports, [1e9, 3e9, 5e9, 7e9]);

        // Recorded at HEAD, before any PCAL6 change, from this same fixture and sweep.
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
        foreach (var (pt, i, re, im) in pre)
        {
            Assert.Equal(re, r.Points[pt].S[i, 0].Real);          // EXACT — no tolerance
            Assert.Equal(im, r.Points[pt].S[i, 0].Imaginary);
        }
        var s = r.Points[0].S;

        Assert.DoesNotContain(r.Notes, n => n.Contains("SHORT standard", StringComparison.Ordinal));
        output.WriteLine($"1 GHz S11 = {s[0, 0]}, S21 = {s[1, 0]}");
    }

    /// <summary>
    /// <b>The recovery is OFF by default, and that is the mechanism of the bit-identity above rather
    /// than a consequence of it.</b> With <see cref="PlanarCalibrationSettings.GroupShortLineDegrees"/>
    /// at its shipped 0 a group's standard set is the one a plain port's rule builds, to the bit;
    /// with the retry's value it is not.
    /// </summary>
    [Fact]
    public void TheShortStandardIsUntouchedUntilTheRecoveryAsksForIt()
    {
        var (port, _) = GroupedPort();
        double shipped = PlanarCalibration.BuildSet(port, Slab, 200e6, 1e9)[0].LengthM;
        var (target, _) = PlanarCalibration.SuggestLengths(Slab, 200e6, 1e9);
        Assert.Equal(PlanarCalibration.BuildLine(port, target,
                         PlanarCalibration.EndRunCellsFor(port, Slab)).LengthM, shipped);

        double grown = PlanarCalibration.BuildSet(port, Slab, 200e6, 1e9,
            PlanarCalibrationSettings.Default with
            { GroupShortLineDegrees = PlanarCalibrationSettings.UsableLoDegrees })[0].LengthM;

        output.WriteLine($"short standard {shipped * 1e3:F2} mm shipped, {grown * 1e3:F2} mm recovered");
        Assert.True(grown > 10 * shipped, "the recovery did not lengthen the short standard");

        // And nothing moves on a port that is not in a group, which is every run that passes today.
        var (_, mesh, ports) = Board();
        Assert.Null(ports[0].Group);
        Assert.Equal(PlanarCalibration.BuildSet(ports[0], Slab, 200e6, 1e9)[0].LengthM,
                     PlanarCalibration.BuildSet(ports[0], Slab, 200e6, 1e9,
                         PlanarCalibrationSettings.Default with
                         { GroupShortLineDegrees = PlanarCalibrationSettings.UsableLoDegrees })[0].LengthM);
        Assert.NotNull(mesh);
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
    /// </summary>
    [Fact]
    public void TheSameFrequencyIsRefusedInOneSweepAndAcceptedInAnother()
    {
        var (problem, mesh, ports) = Board();

        // 100 MHz - 1 GHz: separations 171.0 and 54.1 mm. 200 MHz reads 1.63° and publishes.
        var ok = PlanarSolve.Run(problem, mesh, ports, [100e6, 200e6, 1e9]);
        Assert.Contains(ok.Notes, n => n.Contains("200 MHz", StringComparison.Ordinal)
                                    && n.Contains("separation 1.63°", StringComparison.Ordinal));

        // 200 MHz - 1 GHz: separations 101.7 and 45.5 mm. The SAME 200 MHz reads 0.405° and stops.
        var ex = Assert.Throws<PlanarGroupModesRefusedException>(
            () => PlanarSolve.Run(problem, mesh, ports, [200e6, 1e9]));
        output.WriteLine(ex.Message);

        Assert.Equal(200e6, ex.FrequencyHz);
        Assert.True(ex.MeasuredDegrees < 0.5);

        // ── And this is the half that says the refusal is not about the metal ──────────────────
        //
        // The group's own electrostatics puts the same two modes nearly SEVEN TIMES further apart
        // than the measurement did. A genuinely degenerate pair reads small on both.
        Assert.True(ex.QuasiStaticDegrees > 2.5,
                    $"quasi-static separation {ex.QuasiStaticDegrees:F3}° — expected the modes to be " +
                    "comfortably separable, which is what makes this a measurement failure");
        Assert.True(ex.QuasiStaticDegrees / ex.MeasuredDegrees > 5);
    }

    /// <summary>
    /// <b>M3 — and through <see cref="PlanarKernel.Solve"/>, where the settings live, the refused
    /// sweep runs.</b> §LF3's sentence one wall further along: a run that already knows the answer
    /// should not be asking a person to type it, especially when the sentence is about a calibration
    /// standard, which is not a thing the user drew.
    /// </summary>
    [Fact]
    public void TheRefusedSweepRecoversOnALongerShortStandard_AndSaysSo()
    {
        var r = new PlanarKernel().Solve(Problem(), PlanarLineFixtures.Coarse, Ports(),
                                         [200e6, 1e9]);

        string note = Assert.Single(r.Notes, n => n.Contains("SHORT standard", StringComparison.Ordinal));
        output.WriteLine(note);
        Assert.Contains("0.405°", note, StringComparison.Ordinal);       // what it measured
        Assert.Contains("2.839°", note, StringComparison.Ordinal);       // what the electrostatics says
        Assert.Contains("3.83 mm", note, StringComparison.Ordinal);      // the standard that could not
        Assert.Contains("20° electrical", note, StringComparison.Ordinal);

        // The point it refused on now reads within 1 % of the quasi-static truth rather than 0.3× it.
        string modal = Assert.Single(r.Notes, n => n.Contains("MODAL CALIBRATION", StringComparison.Ordinal));
        Assert.Contains("Worst mode separation 2.86° at 200 MHz", modal, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>R-pcal6-6 — a group whose modes are genuinely degenerate is still refused, and the retry
    /// never runs.</b> The two cases read identically on the measured number alone; what separates
    /// them is that the quasi-static separation does not move when the short standard grows. So a
    /// refusal raised where BOTH numbers are under the floor carries the plain type, which
    /// <see cref="PlanarKernel.Solve"/> does not catch.
    /// </summary>
    [Fact]
    public void GenuinelyDegenerateModesAreNotRetried()
    {
        var (problem, mesh, ports) = Board();
        var strict = PlanarCalibrationSettings.Default with { ModeSeparationFloorDegrees = 1e4 };

        var ex = Assert.Throws<PlanarFeedClearanceRefusedException>(
            () => PlanarSolve.Run(problem, mesh, ports, [1e9, 3e9, 5e9, 7e9],
                                  new PlanarSolveSettings(Calibration: strict)));
        Assert.IsNotType<PlanarGroupModesRefusedException>(ex);
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
}
