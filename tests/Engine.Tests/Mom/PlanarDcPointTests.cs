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
}
