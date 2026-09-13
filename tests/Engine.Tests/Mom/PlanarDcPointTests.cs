// LF1 — THE 0 Hz POINT.
//
// The acceptance question is not "is the number close", it is "does the file say the right thing
// about CONNECTION" — a series MIM cap is an open at DC and a trace is a short, and a bias
// calculation reads that row. So the gates here are a closed-form sheet resistance on one side and
// an exact identity matrix on the other, with nothing in between that a tolerance could hide.

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
    // THE FULL-WAVE KERNEL STILL REFUSES THE NEIGHBOURHOOD OF DC — AND NAMES THIS
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TheRefusalBelowTheFitsFloorNamesTheDcPointAsTheThingThatDoesWork()
    {
        var problem = PlanarLineFixtures.Fr4Line(20e-3, 6e9);
        var (mesh, ports) = PlanarLineFixtures.MeshAndPorts(problem, PlanarLineFixtures.Coarse);

        // 100 kHz on 1.6 mm FR-4 is k₀H = 3.4e-6 — below the widened fit's own floor, and the point
        // of the sentence is that the remedy it names is reachable rather than hypothetical.
        var ex = Assert.Throws<InvalidOperationException>(
            () => PlanarSolve.Run(problem, mesh, ports, [1e5, 2e9], new PlanarSolveSettings(Deembed: false)));
        _out.WriteLine(ex.Message);
        Assert.Contains("0 Hz", ex.Message);

        // …and it IS reachable: the same sweep with the lower edge AT zero runs.
        var ok = PlanarSolve.Run(problem, mesh, ports, [0, 2e9], new PlanarSolveSettings(Deembed: false));
        Assert.Equal(2, ok.Points.Count);
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
