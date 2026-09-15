// LF1 — THE 0 Hz POINT.
//
// The acceptance question is not "is the number close", it is "does the file say the right thing
// about CONNECTION" — a series MIM cap is an open at DC and a trace is a short, and a bias
// calculation reads that row. So the gates here are a closed-form sheet resistance on one side and
// an exact identity matrix on the other, with nothing in between that a tolerance could hide.

using System.Linq;
using System.Numerics;
using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using NumFlat;
using Xunit;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

public sealed class PlanarDcPointTests
{
    private readonly ITestOutputHelper _out;
    public PlanarDcPointTests(ITestOutputHelper output) => _out = output;

    private const double SigmaCu = 5.8e7;
    private const double ThickCu = 35e-6;

    private static double SheetOhms => 1.0 / (SigmaCu * ThickCu);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // A CONTINUOUS TRACE IS A RESISTOR, AND THE RESISTOR IS Rs·L/W
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ATraceIsItsOwnSheetResistanceAtDC_AndThePortsAreCONNECTED()
    {
        const double w = 2.9e-3, len = 20e-3;
        var problem = PlanarLineFixtures.Line(GroundedSlab.Fr4Starter, w, len, 6e9);
        var (mesh, ports) = PlanarLineFixtures.MeshAndPorts(problem, PlanarLineFixtures.Coarse);

        var dc = PlanarDcSolve.Solve(problem, mesh, ports);

        // Y of a two-terminal resistor: Y₁₂ = −1/R, and the matrix is symmetric because a resistor
        // network is.
        double g = -dc.Y[0, 1].Real;
        double r = 1.0 / g;
        double closedForm = SheetOhms * len / w;

        _out.WriteLine($"Rs = {SheetOhms * 1e3:F4} mΩ/sq, L/W = {len / w:F3} squares");
        _out.WriteLine($"closed form (full drawn length) = {closedForm * 1e3:F4} mΩ");
        _out.WriteLine($"solved (port cell to port cell) = {r * 1e3:F4} mΩ");
        _out.WriteLine($"S21 = {dc.S[1, 0]}, S11 = {dc.S[0, 0]}");
        foreach (var n in dc.Notes) _out.WriteLine("  " + n);

        // The network's terminals are the ports' own OUTERMOST cells, so it measures a little less
        // than the drawn length — one outer half-cell at each end. On this mesh that is ~11 %, and
        // the gate is written on what the model actually claims rather than on the drawn figure.
        Assert.InRange(r, 0.8 * closedForm, closedForm);

        // Reciprocity. The conductance matrix is symmetric by construction, but Y's two entries
        // come from two separate sparse solves, so this is the solver's own tolerance rather than
        // bit-identity — and it is asked RELATIVE, because g here is thousands of siemens.
        Assert.InRange(Math.Abs(dc.Y[0, 1].Real - dc.Y[1, 0].Real) / g, 0, 1e-10);

        // Y₁₁ = −Y₁₂ for a floating two-terminal network: there is no third path for current, which
        // is exactly what "this trace does not touch the ground plane" means at DC.
        Assert.InRange(Math.Abs(dc.Y[0, 0].Real - g) / g, 0, 1e-10);

        // …and the published row says THROUGH, which is the thing a bias calculation reads.
        Assert.True(dc.S[1, 0].Magnitude > 0.9999, $"S21 = {dc.S[1, 0].Magnitude}");
        Assert.True(dc.S[0, 0].Magnitude < 1e-4, $"S11 = {dc.S[0, 0].Magnitude}");
        Assert.Equal(0.0, dc.S[1, 0].Imaginary, 12);      // real by construction
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // A SERIES GAP IS AN OPEN, AND THE ANSWER IS THE IDENTITY EXACTLY
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ASeriesGapIsAnOPENAtDC_AndTheMatrixIsExactlyTheIdentity()
    {
        // Two collinear traces with a gap between them — the DC behaviour of every series capacitor,
        // MIM or otherwise. This is the case the whole file exists for: the full-wave sweep says
        // "small coupling" at its bottom frequency, and a bias calculation needs "no path at all".
        const double w = 2.9e-3;
        var problem = PlanarLineFixtures.Problem(
            GroundedSlab.Fr4Starter, 6e9,
            PlanarLineFixtures.Rect(0,     -0.5 * w,  9e-3, 0.5 * w),
            PlanarLineFixtures.Rect(11e-3, -0.5 * w, 20e-3, 0.5 * w));

        var ports = new[]
        {
            new PlanarPort(1, new EmPoint(0,     0), PlanarPortSide.MinX, 50.0),
            new PlanarPort(2, new EmPoint(20e-3, 0), PlanarPortSide.MaxX, 50.0),
        };
        var report   = SurfaceMesher.Mesh(problem, PlanarLineFixtures.Coarse);
        var resolved = PlanarPorts.ResolveAll(report.Mesh, ports);

        var dc = PlanarDcSolve.Solve(problem, report.Mesh, resolved);

        _out.WriteLine($"Y = [{dc.Y[0, 0]} {dc.Y[0, 1]}; {dc.Y[1, 0]} {dc.Y[1, 1]}]");
        _out.WriteLine($"S = [{dc.S[0, 0]} {dc.S[0, 1]}; {dc.S[1, 0]} {dc.S[1, 1]}]");
        foreach (var n in dc.Notes) _out.WriteLine("  " + n);

        // EXACTLY zero, not small: the two islands share no branch, so no current is computed, not a
        // tiny one. That is the difference between an answer and a tolerance.
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                Assert.Equal(0.0, dc.Y[i, j].Magnitude);

        Assert.Equal(1.0, dc.S[0, 0].Real, 14);
        Assert.Equal(1.0, dc.S[1, 1].Real, 14);
        Assert.Equal(0.0, dc.S[1, 0].Magnitude, 14);
        Assert.Contains("DC open", string.Join(" ", dc.Notes));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // A NARROWER TRACE IS A LARGER RESISTOR, IN PROPORTION
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(2.9e-3)]
    [InlineData(1.45e-3)]
    [InlineData(0.725e-3)]
    public void TheDcResistanceScalesWithSQUARES_NotWithLength(double w)
    {
        const double len = 20e-3;
        var problem = PlanarLineFixtures.Line(GroundedSlab.Fr4Starter, w, len, 6e9);
        var (mesh, ports) = PlanarLineFixtures.MeshAndPorts(problem, PlanarLineFixtures.Coarse);

        var dc = PlanarDcSolve.Solve(problem, mesh, ports);
        double r = 1.0 / -dc.Y[0, 1].Real;

        // The measured length is the port-cell separation, which the mesh fixes; what varies here is
        // only the width, so R·w is the quantity that must stay put.
        _out.WriteLine($"w = {w * 1e3:F3} mm → R = {r * 1e3:F4} mΩ, R·w/Rs = {r * w / SheetOhms:F4} m");
        Assert.InRange(r * w / SheetOhms, 0.8 * len, len);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // THE SWEEP SPLICE
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ASweepThatASKSForZeroGetsIt_First_AndTheACPointsAreUnchangedByIt()
    {
        var problem = PlanarLineFixtures.Fr4Line(20e-3, 6e9);
        var (mesh, ports) = PlanarLineFixtures.MeshAndPorts(problem, PlanarLineFixtures.Coarse);
        var st = new PlanarSolveSettings(Deembed: false);

        var without = PlanarSolve.Run(problem, mesh, ports, [2e9, 5e9], st);
        var with    = PlanarSolve.Run(problem, mesh, ports, [0, 2e9, 5e9], st);

        Assert.Equal(2, without.Points.Count);
        Assert.Equal(3, with.Points.Count);
        Assert.Equal(0.0, with.Points[0].FrequencyHz);

        // ── ADDING THE DC POINT CHANGES NO OTHER POINT, BIT FOR BIT ──────────────────────────────
        //
        // 0 Hz is taken out of the array before anything downstream reads it, so the AC sweep is the
        // same array it always was. Asserted as exact equality rather than a tolerance, because that
        // is a claim about the code path.
        for (int i = 0; i < without.Points.Count; i++)
        {
            var a = without.Points[i].S;
            var b = with.Points[i + 1].S;
            Assert.Equal(without.Points[i].FrequencyHz, with.Points[i + 1].FrequencyHz);
            for (int r = 0; r < a.RowCount; r++)
                for (int c = 0; c < a.ColCount; c++)
                    Assert.Equal(a[r, c], b[r, c]);
        }

        // The DC row is REAL, which is what makes it a resistance rather than a limit someone took.
        var dcS = with.Points[0].S;
        for (int r = 0; r < dcS.RowCount; r++)
            for (int c = 0; c < dcS.ColCount; c++)
                Assert.Equal(0.0, dcS[r, c].Imaginary);

        // RawS IS S at DC — there is no port discontinuity to remove, because a discontinuity is a
        // reactance — and no calibration is claimed.
        Assert.Empty(with.Points[0].Calibrations);
        Assert.Equal(with.Points[0].S[1, 0], with.Points[0].RawS[1, 0]);

        foreach (var n in with.Notes) _out.WriteLine("  " + n);
        Assert.Contains(with.Notes, n => n.Contains("conduction network"));
    }

    [Fact]
    public void ASweepOfNOTHINGBUTZeroIsAnAnswer_NotARefusal()
    {
        var problem = PlanarLineFixtures.Fr4Line(20e-3, 6e9);
        var (mesh, ports) = PlanarLineFixtures.MeshAndPorts(problem, PlanarLineFixtures.Coarse);

        var run = PlanarSolve.Run(problem, mesh, ports, [0.0], new PlanarSolveSettings(Deembed: true));

        Assert.Single(run.Points);
        Assert.Equal(0.0, run.Points[0].FrequencyHz);
        Assert.True(run.Points[0].S[1, 0].Magnitude > 0.9999);

        // Nothing was fitted, nothing was calibrated, and the run says so rather than reporting a
        // standard count for standards it never built.
        Assert.Equal(0, run.StandardCount);
        Assert.Equal(0, run.CoreFillCount);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // LF2 — A POINT BELOW THE FIT'S FLOOR CARRIES THE CONDUCTION ANSWER
    //
    // The refusal L9e/D8 put here is not gone, it is behind a flag (SubstituteConductionBelowFitFloor)
    // — because a caller MEASURING the fit wants it and a caller USING the fit wants a run that
    // finishes. What makes the substitution defensible rather than a fudge is that the thing it
    // omits (reactance) shrinks as the frequency falls while the thing the fit gets wrong grows, so
    // the two error curves cross somewhere and the floor is a defensible place to put the crossing.
    // docs/design/mom-engine.md §10.13 is the measurement.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TheRefusalBelowTheFitsFloorIsSTILLTHERE_AndStillNamesTheDcPoint()
    {
        var problem = PlanarLineFixtures.Fr4Line(20e-3, 6e9);
        var (mesh, ports) = PlanarLineFixtures.MeshAndPorts(problem, PlanarLineFixtures.Coarse);
        var measuring = new PlanarSolveSettings(Deembed: false,
                                                SubstituteConductionBelowFitFloor: false);

        // 100 kHz on 1.6 mm FR-4 is k₀H = 3.4e-6 — below the widened fit's own floor, and the point
        // of the sentence is that the remedy it names is reachable rather than hypothetical.
        var ex = Assert.Throws<InvalidOperationException>(
            () => PlanarSolve.Run(problem, mesh, ports, [1e5, 2e9], measuring));
        _out.WriteLine(ex.Message);
        Assert.Contains("0 Hz", ex.Message);

        // …and it IS reachable: the same sweep with the lower edge AT zero runs.
        var ok = PlanarSolve.Run(problem, mesh, ports, [0, 2e9], measuring);
        Assert.Equal(2, ok.Points.Count);
    }

    [Fact]
    public void APointBelowTheFloorIsTheCONDUCTIONRow_AndTheACPointsAreUnchangedByIt()
    {
        var problem = PlanarLineFixtures.Fr4Line(20e-3, 6e9);
        var (mesh, ports) = PlanarLineFixtures.MeshAndPorts(problem, PlanarLineFixtures.Coarse);
        var st = new PlanarSolveSettings(Deembed: false);

        var without = PlanarSolve.Run(problem, mesh, ports, [2e9, 5e9], st);
        var with    = PlanarSolve.Run(problem, mesh, ports, [0, 1e5, 1e6, 2e9, 5e9], st);

        Assert.Equal(5, with.Points.Count);
        Assert.Equal([0.0, 1e5, 1e6, 2e9, 5e9], with.Points.Select(p => p.FrequencyHz));

        // ── THE SUB-FLOOR ROWS ARE THE 0 Hz ROW, AT THE USER'S OWN FREQUENCIES ───────────────────
        //
        // Bit-identical rather than close: one conduction solve serves all of them, so two rows
        // that differed at all would mean it had been solved twice.
        foreach (int i in (int[])[1, 2])
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 2; c++)
                    Assert.Equal(with.Points[0].S[r, c], with.Points[i].S[r, c]);

        // Real, uncalibrated, and RawS IS S — a point that was not calibrated says so rather than
        // carrying an identity box that pretends it was.
        foreach (int i in (int[])[1, 2])
        {
            Assert.Empty(with.Points[i].Calibrations);
            Assert.Equal(with.Points[i].S[1, 0], with.Points[i].RawS[1, 0]);
            Assert.Equal(0.0, with.Points[i].S[1, 0].Imaginary);
        }

        // ── AND THE POINTS THAT WERE FITTED ARE BIT-IDENTICAL TO A SWEEP THAT NEVER SAW THESE ────
        //
        // The split happens beside LF1's own, above everything else, so nothing downstream has a
        // sub-floor frequency to branch on. Exact equality, because that is a claim about the code
        // path and not about a tolerance.
        for (int i = 0; i < without.Points.Count; i++)
        {
            Assert.Equal(without.Points[i].FrequencyHz, with.Points[i + 3].FrequencyHz);
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 2; c++)
                    Assert.Equal(without.Points[i].S[r, c], with.Points[i + 3].S[r, c]);
        }

        foreach (var n in with.Notes) _out.WriteLine("  " + n);
        var note = Assert.Single(with.Notes, n => n.Contains("the field solver has no valid range"));

        // The note has to carry three things and no more: the boundary, which points moved, and
        // that the reactance is not in them. It is ONE sentence on purpose.
        //
        // §PEEL (2026-09-14) re-pointed the phrase this is found by, on the owner's instruction that
        // these notes use plain terms: it said "the full-wave fit has no valid range", and "fit" is
        // this engine's word for the DCIM Green's-function fit, not a word the person reading the
        // run has. Nothing else about the sentence moved — the assertions below are unchanged,
        // including the one that keeps it to a single sentence.
        Assert.Contains("2.982 MHz", note);          // Dcim.LowestFittableFrequency on 1.6 mm
        Assert.Contains("no reactance", note);
        Assert.Contains("mesh", note);               // a coarse mesh reads the resistance low
        Assert.DoesNotContain(". ", note);           // one sentence, because a sentence gets read
    }

    [Fact]
    public void ASweepENTIRELYBelowTheFloorIsAnAnswer_WithNoZeroHzInIt()
    {
        var problem = PlanarLineFixtures.Fr4Line(20e-3, 6e9);
        var (mesh, ports) = PlanarLineFixtures.MeshAndPorts(problem, PlanarLineFixtures.Coarse);

        var run = PlanarSolve.Run(problem, mesh, ports, [1e5, 1e6, 2e6],
                                  new PlanarSolveSettings(Deembed: true));

        Assert.Equal(3, run.Points.Count);
        Assert.DoesNotContain(run.Points, p => p.FrequencyHz == 0.0);
        Assert.True(run.Points[0].S[1, 0].Magnitude > 0.9999);

        // Nothing was fitted and nothing was calibrated, and the run reports that rather than a
        // standard count for standards it never built — the same claim LF1's DC-only sweep makes.
        Assert.Equal(0, run.StandardCount);
        Assert.Equal(0, run.CoreFillCount);
        Assert.Contains(run.Notes, n => n.Contains("the field solver has no valid range"));
    }

    [Fact]
    public void TheSubstitutionAndTheRefusalAskTheSAMEQuestion()
    {
        // Two spellings of "is this point below the floor" is two answers waiting to disagree at the
        // boundary, which is the one place it would matter. Dcim.IsBelowFitFloor is the only one.
        const double h = 1.6e-3;
        double fFloor  = Dcim.LowestFittableFrequency(h);
        _out.WriteLine($"floor on {h * 1e3:F1} mm = {fFloor / 1e6:F4} MHz");

        foreach (double f in (double[])[0.5 * fFloor, 0.999 * fFloor, fFloor, 1.001 * fFloor, 2 * fFloor])
        {
            double k0 = 2.0 * Math.PI * f / EmConstants.C0;
            bool below = Dcim.IsBelowFitFloor(k0, h);
            bool fits  = Dcim.CanFitAtFrequency(k0, h).Ok;
            _out.WriteLine($"  {f / 1e6,10:F4} MHz  below={below,-5} fits={fits}");
            Assert.NotEqual(below, fits);
        }

        // And the sweep partitions on exactly that: a point at the floor is FITTED, not substituted.
        var problem = PlanarLineFixtures.Fr4Line(20e-3, 6e9);
        var (mesh, ports) = PlanarLineFixtures.MeshAndPorts(problem, PlanarLineFixtures.Coarse);
        var run = PlanarSolve.Run(problem, mesh, ports, [fFloor, 2e9],
                                  new PlanarSolveSettings(Deembed: false));
        Assert.DoesNotContain(run.Notes, n => n.Contains("the field solver has no valid range"));
        Assert.NotEqual(0.0, run.Points[0].S[1, 0].Imaginary);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // A LAYER WITH NO CONDUCTIVITY IS A PEC, AND SAYS SO
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AConductorLevelDeclaringNoConductivityReadsAsAShort_WithItsNote()
    {
        const double w = 2.9e-3, len = 20e-3;
        var problem = new PlanarProblem(
            [new PlanarConductorLayer("Metal", [PlanarLineFixtures.Rect(0, -0.5 * w, len, 0.5 * w)],
                                      SigmaSm: 0, ThicknessM: 0)],
            GroundedSlab.Fr4Starter, 6e9);
        var (mesh, ports) = PlanarLineFixtures.MeshAndPorts(problem, PlanarLineFixtures.Coarse);

        var dc = PlanarDcSolve.Solve(problem, mesh, ports);
        _out.WriteLine($"S21 = {dc.S[1, 0]}");
        foreach (var n in dc.Notes) _out.WriteLine("  " + n);

        Assert.True(dc.S[1, 0].Magnitude > 1 - 1e-7, $"|S21| = {dc.S[1, 0].Magnitude:R}");
        Assert.Contains(dc.Notes, n => n.Contains("perfect conductor"));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // A PORT WHOSE REFERENCE IS NOT THE PLANE — THE INTERNAL DELTA GAP
    //
    // Owner report, 2026-09-14: a 50 Ω line simulated twice, once as a 2-port with edge ports and
    // once as a 3-port with a gap port in the middle, disagreed completely below the DCIM fit floor
    // (where LF2 substitutes this solve) and agreed to six digits above it. The gap file published
    // S13 = 1 and S22 = -1 — port 2 shorted to ground and isolated from everything.
    //
    // These gate the SHAPE of the answer, not a tolerance: a cut in a floating trace makes three
    // ports meet at one series node, and that has a closed form with no mesh in it.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The 3-port that a cut in an otherwise continuous line has to be. With the metal
    /// floating over the plane the only thing joining the three ports is the trace itself, so ONE
    /// current flows and Y is rank 1: I₁ = I₃, I₂ = −I₃. That makes S the 1/3-2/3 split of three
    /// matched sources meeting at a series node — the same numbers the full-wave solve publishes at
    /// the bottom of its own band, which is what makes the two halves of a sweep meet.</summary>
    [Fact]
    public void AGapPortIsASERIESPortAtDC_NotTwoTerminalsPinnedToGround()
    {
        const double w = 2.9e-3, len = 20e-3;
        var problem = PlanarLineFixtures.Line(GroundedSlab.Fr4Starter, w, len, 6e9);
        var mesh    = SurfaceMesher.Mesh(problem, PlanarLineFixtures.Coarse).Mesh;
        var (x0, y0, x1, y1) = problem.Bounds();
        double yc = 0.5 * (y0 + y1);

        var ports = PlanarPorts.ResolveAll(mesh,
        [
            new PlanarPort(1, new EmPoint(x0, yc), PlanarPortSide.MinX, 50.0),
            new PlanarPort(2, new EmPoint(x1, yc), PlanarPortSide.MaxX, 50.0),
            new PlanarPort(3, new EmPoint(0.5 * (x0 + x1), yc), PlanarPortSide.MinX, 50.0,
                           Kind: PlanarPortKind.InternalDeltaGap),
        ]);
        var dc = PlanarDcSolve.Solve(problem, mesh, ports);

        for (int i = 0; i < 3; i++)
            _out.WriteLine($"  S{i + 1}* = {dc.S[i, 0].Real:F6}  {dc.S[i, 1].Real:F6}  {dc.S[i, 2].Real:F6}");
        foreach (var n in dc.Notes) _out.WriteLine("  " + n);

        // Every diagonal 1/3, every off-diagonal 2/3 in magnitude. The signs are the port-3 polarity
        // and are asserted by their PRODUCT, which is what the polarity cannot change.
        for (int i = 0; i < 3; i++)
            Assert.Equal(1.0 / 3.0, dc.S[i, i].Real, 4);
        foreach (var (i, j) in ((int, int)[])[(0, 1), (0, 2), (1, 2)])
        {
            Assert.Equal(2.0 / 3.0, dc.S[i, j].Magnitude, 4);
            // Reciprocal — but the two entries come from two separate sparse solves, so this is the
            // solver's own tolerance rather than bit-identity, exactly as the trace test above says.
            Assert.Equal(0.0, (dc.S[i, j] - dc.S[j, i]).Magnitude, 9);
        }
        // Around the loop the three off-diagonals multiply to a NEGATIVE number whichever lip is +,
        // which is the statement that port 3 sits in series with the other two rather than shunting
        // them. It is what came out wrong: the old answer had S12 = S23 = 0.
        Assert.True((dc.S[0, 1] * dc.S[0, 2] * dc.S[1, 2]).Real < 0,
                    $"S12·S13·S23 = {dc.S[0, 1] * dc.S[0, 2] * dc.S[1, 2]}");

        // Y is rank 1: one current, so every 2×2 minor vanishes.
        for (int i = 0; i < 3; i++)
            for (int j = i + 1; j < 3; j++)
                Assert.Equal(0.0,
                    (dc.Y[i, i] * dc.Y[j, j] - dc.Y[i, j] * dc.Y[j, i]).Magnitude /
                    (dc.Y[i, i] * dc.Y[j, j]).Magnitude, 6);
    }

    /// <summary>The owner's actual question: short the gap port out and you must get the line back.
    /// This is the acceptance test — it is how anyone uses a gap port for a series component, and it
    /// is the comparison that failed.</summary>
    [Fact]
    public void ShortingTheGapPortGivesBackThePlainTwoPortLine()
    {
        const double w = 2.9e-3, len = 20e-3;
        var problem = PlanarLineFixtures.Line(GroundedSlab.Fr4Starter, w, len, 6e9);
        var mesh    = SurfaceMesher.Mesh(problem, PlanarLineFixtures.Coarse).Mesh;
        var (x0, y0, x1, y1) = problem.Bounds();
        double yc = 0.5 * (y0 + y1);

        PlanarPort P1() => new(1, new EmPoint(x0, yc), PlanarPortSide.MinX, 50.0);
        PlanarPort P2() => new(2, new EmPoint(x1, yc), PlanarPortSide.MaxX, 50.0);

        var two   = PlanarDcSolve.Solve(problem, mesh, PlanarPorts.ResolveAll(mesh, [P1(), P2()]));
        var three = PlanarDcSolve.Solve(problem, mesh, PlanarPorts.ResolveAll(mesh,
        [
            P1(), P2(),
            new PlanarPort(3, new EmPoint(0.5 * (x0 + x1), yc), PlanarPortSide.MinX, 50.0,
                           Kind: PlanarPortKind.InternalDeltaGap),
        ]));

        // Terminate port 3 in a short (Γ = −1) and reduce to two ports.
        var S = three.S;
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
            {
                var reduced = S[i, j] - S[i, 2] * S[2, j] / (Complex.One + S[2, 2]);
                _out.WriteLine($"  short-P3 S{i + 1}{j + 1} = {reduced.Real:F8}   " +
                               $"2-port = {two.S[i, j].Real:F8}");
                // Not bit-identical and cannot be: the cut removes the gap's own rooftop from the
                // conduction path, so the shorted line is one rooftop SHORTER than the uncut one.
                // On this mesh that is ~13 % of a milliohm on a 3 mΩ trace — far below anything S
                // can show at 50 Ω, which is why the gate is written on S rather than on R.
                Assert.Equal(two.S[i, j].Real, reduced.Real, 5);
            }
    }

    /// <summary>The regression in one line: what the old Dirichlet excitation published. A gap port
    /// held at an absolute potential welds the far lip to ground, which severs the line — S₂₂ = −1,
    /// port 2 alone, and a perfect through between the other two. None of it may come back.</summary>
    [Fact]
    public void AGapDoesNotWELDTheFarLipToGround()
    {
        const double w = 2.9e-3, len = 20e-3;
        var problem = PlanarLineFixtures.Line(GroundedSlab.Fr4Starter, w, len, 6e9);
        var mesh    = SurfaceMesher.Mesh(problem, PlanarLineFixtures.Coarse).Mesh;
        var (x0, y0, x1, y1) = problem.Bounds();
        double yc = 0.5 * (y0 + y1);

        var dc = PlanarDcSolve.Solve(problem, mesh, PlanarPorts.ResolveAll(mesh,
        [
            new PlanarPort(1, new EmPoint(x0, yc), PlanarPortSide.MinX, 50.0),
            new PlanarPort(2, new EmPoint(x1, yc), PlanarPortSide.MaxX, 50.0),
            new PlanarPort(3, new EmPoint(0.5 * (x0 + x1), yc), PlanarPortSide.MinX, 50.0,
                           Kind: PlanarPortKind.InternalDeltaGap),
        ]));

        Assert.True(dc.S[1, 1].Real > 0, $"S22 = {dc.S[1, 1]} — port 2 is shorted to the plane");
        Assert.NotEqual(0.0, dc.S[0, 1].Magnitude);     // port 2 reaches port 1 …
        Assert.NotEqual(0.0, dc.S[1, 2].Magnitude);     // … and port 3
        Assert.True(dc.S[0, 2].Magnitude < 0.9,
                    $"|S13| = {dc.S[0, 2].Magnitude} — ports 1 and 3 are not the same terminal pair");

        // And the metal is still FLOATING: nothing reaches the plane, so lifting the WHOLE structure
        // by a volt relative to it must draw no current anywhere. That is +1 V at each edge port and
        // 0 at the gap, whose two lips rise together — so Y·(1,1,0) = 0. It is the property the weld
        // destroyed, and the one that says port 2 is not tied to ground.
        for (int i = 0; i < 3; i++)
        {
            var lift = dc.Y[i, 0] + dc.Y[i, 1];
            Assert.Equal(0.0, lift.Magnitude / dc.Y[i, i].Magnitude, 6);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // THE OTHER TWO KINDS — one that was wrong the same way, one that never was
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>An edge port with a RETURN CONDUCTOR is floating too</b>, and it was wrong in the same
    /// place — quietly, which is why it outlived the gap port's version of the bug. Its − terminal is
    /// the return strip, so holding both terminals at an absolute potential put the return at ground
    /// and left it carrying no current: the published resistance was the SIGNAL conductor's alone,
    /// with the return's contribution simply missing. On a symmetric coplanar pair that is a clean
    /// factor of two and looks like nothing at all in S, since both answers are 0 dB on copper.
    ///
    /// <para>The gate is the series law, which needs no closed form and no mesh: a loop is
    /// R_signal + R_return, so narrowing the return to a third of the signal must TRIPLE its share
    /// and widening it to three times must cut it to a third. Under the old code all three widths
    /// published the same number.</para>
    /// </summary>
    [Fact]
    public void AConductorReferencedPortCarriesTheReturnConductorsResistanceToo()
    {
        double same = CoplanarLoopOhms(0.6e-3, 0.6e-3);
        double thin = CoplanarLoopOhms(0.6e-3, 0.2e-3);
        double wide = CoplanarLoopOhms(0.6e-3, 1.8e-3);

        _out.WriteLine($"  return = signal   {same * 1e3:F4} mΩ");
        _out.WriteLine($"  return = signal/3 {thin * 1e3:F4} mΩ   ×{thin / same:F4}");
        _out.WriteLine($"  return = signal·3 {wide * 1e3:F4} mΩ   ×{wide / same:F4}");

        // R + 3R over R + R, and R + R/3 over R + R.
        Assert.Equal(2.0,       thin / same, 3);
        Assert.Equal(2.0 / 3.0, wide / same, 3);
    }

    /// <summary>The coplanar loop resistance, read straight off Y's off-diagonal.</summary>
    private static double CoplanarLoopOhms(double wSignal, double wReturn)
    {
        const double slot = 0.2e-3, len = 10e-3;
        var problem = PlanarLineFixtures.Problem(
            new GroundedSlab(1e-3, new EmMaterial(1.0, 0.0)), 6e9,
            PlanarLineFixtures.Rect(0,  0.5 * slot,             len,  0.5 * slot + wSignal),
            PlanarLineFixtures.Rect(0, -(0.5 * slot + wReturn), len, -0.5 * slot));
        var mesh = SurfaceMesher.Mesh(problem,
            new PlanarMeshSettings(Auto: false, CellsPerWavelength: 40, EdgeMesh: false,
                                   MinCellsAcrossConductor: 3)).Mesh;

        double yp = 0.5 * slot + 0.5 * wSignal, yn = -(0.5 * slot + 0.5 * wReturn);
        var ports = PlanarPorts.ResolveAll(mesh,
        [
            new PlanarPort(1, new EmPoint(0, yp), PlanarPortSide.MinX, 50.0,
                           Reference: PlanarPortReference.CoplanarGround,
                           NegativeLocation: new EmPoint(0, yn)),
            new PlanarPort(2, new EmPoint(len, yp), PlanarPortSide.MaxX, 50.0,
                           Reference: PlanarPortReference.CoplanarGround,
                           NegativeLocation: new EmPoint(len, yn)),
        ]);
        return 1.0 / -PlanarDcSolve.Solve(problem, mesh, ports).Y[0, 1].Real;
    }

    /// <summary>
    /// <b>A via-to-plane port's − terminal IS the ground node, so it was never affected</b> — and
    /// this pins that, because the source formulation reduces to the old Dirichlet condition for it
    /// and a change that broke the reduction would be invisible otherwise.
    ///
    /// <para>It is also the instructive contrast with the gap port: this one SHUNTS the line to the
    /// plane, so the three ports meet at a common node and S is the −1/3, +2/3 of a shunt junction.
    /// The gap port puts the third port in SERIES, and the same three lines give +1/3, ∓2/3.</para>
    /// </summary>
    [Fact]
    public void AViaToPlanePortIsAShuntJunction_AndIsUnchangedByTheSourceFormulation()
    {
        const double via = 1.2e-3;
        var line = PlanarLineFixtures.Fr4Line(8e-3, 2e9);
        var (x0, y0, x1, y1) = line.Bounds();
        double xc = 0.5 * (x0 + x1), yc = 0.5 * (y0 + y1);
        var problem = line with
        {
            Vias = [new PlanarVia(PlanarVia.GroundTerminal, 0,
                                  [PlanarLineFixtures.Rect(xc - 0.5 * via, yc - 0.5 * via,
                                                           xc + 0.5 * via, yc + 0.5 * via)], 5.8e7)],
        };
        var mesh = SurfaceMesher.Mesh(problem, PlanarLineFixtures.Coarse).Mesh;
        var dc   = PlanarDcSolve.Solve(problem, mesh, PlanarPorts.ResolveAll(mesh,
        [
            new PlanarPort(1, new EmPoint(x0, yc), PlanarPortSide.MinX, 50.0),
            new PlanarPort(2, new EmPoint(x1, yc), PlanarPortSide.MaxX, 50.0),
            new PlanarPort(3, new EmPoint(xc, yc), PlanarPortSide.MinX, 50.0,
                           Kind: PlanarPortKind.Internal),
        ]));

        for (int i = 0; i < 3; i++)
            _out.WriteLine($"  S{i + 1}* = {dc.S[i, 0].Real,10:F6} {dc.S[i, 1].Real,10:F6} " +
                           $"{dc.S[i, 2].Real,10:F6}");

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(-1.0 / 3.0, dc.S[i, i].Real, 4);
            for (int j = 0; j < 3; j++)
                if (i != j) Assert.Equal(2.0 / 3.0, dc.S[i, j].Real, 4);
        }
    }
}
