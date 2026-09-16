// LFP — the two things that need a solve: the reported spiral's bad rows actually leaving by the
// guard's doors, and the entry test R-lfp-3 has to pass before anyone builds it.
//
// Category=Benchmark, by this repository's own rule: each of these solves a full-wave point and its
// calibration standards. The DECISIONS — which term sets the floor, which threshold it crosses,
// what band edge the remedy sentence can offer — are arithmetic and live in
// `LowFrequencyPortWallTests` beside this at 21 ms; this is what says those decisions land on the
// part the wall was reported on.

using System.Numerics;
using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using NumFlat;
using RfCore;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

[Trait("Category", "Benchmark")]
public sealed class LowFrequencyPortWallPhysicsTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _out = output;

    private static readonly PlanarMeshSettings CoilMesh =
        new(Auto: false, CellsPerWavelength: 20, EdgeMesh: false, MinCellsAcrossConductor: 2);

    private static PlanarPort[] CoilPorts() =>
    [
        new(1, new EmPoint(-110e-6, -125e-6), PlanarPortSide.MinY, 50.0) { LayerIndex = 0 },
        new(2, new EmPoint(-155e-6,  -46e-6), PlanarPortSide.MinX, 50.0) { LayerIndex = 0 },
    ];

    private static PlanarSolveResult Solve(PlanarProblem problem, PlanarPort[] ports,
                                           double[] freqs, PlanarMeshSettings mesh,
                                           PlanarSolveSettings? settings = null)
    {
        var (grown, leads, _) = PlanarFeedExtension.Extend(problem, ports, settings?.Calibration);
        var report = SurfaceMesher.Mesh(grown, mesh, leads: leads);
        var resolved = PlanarPorts.ResolveAll(report.Mesh, ports);
        return PlanarSolve.Run(grown, report.Mesh, resolved, freqs, settings, leads: leads);
    }

    private static double Nh(Mat<Complex> s, double fHz)
    {
        var y = RFNetwork.SToY(s, [new Complex(50, 0), new Complex(50, 0)]);
        return (-1.0 / y[1, 0]).Imaginary / (2 * Math.PI * fHz) * 1e9;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // M4 — the guard fires on the rows it exists for, and on no others
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The shipped <c>examples/PDK PCells</c> spiral: the row that is 39× wrong leaves the
    /// sweep, the three beside it are named and published, and the row the run is right on is
    /// untouched.</b>
    ///
    /// <para>Before this brief all four read UNDER the 0.05 budget — 2.0e-2 at 160 MHz where the
    /// realised error is 0.76 — so PEEL's per-point guard, which exists for exactly this, stayed
    /// silent. The floor's second term is what changes that and nothing else does: the s-parameters
    /// are the ones the sweep solved, and <see cref="TheWellConditionedRowIsUnchanged"/> is what
    /// says so.</para>
    /// </summary>
    [Fact]
    public void TheSpiralsBadRowsLeaveBySuchDoorAsTheGuardHas()
    {
        double[] fs = [0.16e9, 0.32e9, 0.64e9, 2.08e9];
        var r = Solve(MmicCoilFixture.Coil(0.32e9), CoilPorts(), fs, CoilMesh);

        foreach (var pt in r.Points)
            _out.WriteLine($"{pt.FrequencyHz / 1e9:F2} GHz: {Nh(pt.S, pt.FrequencyHz):F3} nH, " +
                           $"sigma_max {RFNetwork.Passivity(pt.S):F5}, " +
                           $"floor {pt.Calibrations.Max(c => c.Box.DeembedErrorFloor):0.000e+00}");

        // 160 MHz is over the 0.25 refusal threshold and is left out of the published sweep.
        Assert.DoesNotContain(r.Points, pt => Math.Abs(pt.FrequencyHz - 0.16e9) < 1);
        Assert.Equal(3, r.Points.Count);

        var note = Assert.Single(r.Notes, n => n.Contains("DeembedErrorFloor"));
        _out.WriteLine(note);
        Assert.Contains("160 MHz", note, StringComparison.Ordinal);
        Assert.Contains("were dropped", note, StringComparison.Ordinal);

        // The band edge, under the DUT-bound term's own 1/f² law. The first row of this sweep whose
        // sigma_max falls under 1 is 800 MHz, so an edge in the 600s is the measurement agreeing
        // with itself; the 1/f law would have named 2.4 GHz off the same 160 MHz row.
        Assert.Matches(@"above about 6\d\d(\.\d+)? MHz", note);

        // 320 and 640 MHz are published and named; 2.08 GHz is neither dropped nor flagged.
        foreach (var pt in r.Points)
        {
            double floor = pt.Calibrations.Max(c => c.Box.DeembedErrorFloor);
            Assert.True(floor < PlanarSolve.PeelErrorRefusalDS);
            Assert.Equal(pt.FrequencyHz < 1e9, floor >= PlanarSolve.PeelErrorBudgetDS);
        }
    }

    /// <summary>
    /// <b>PCAL7's gate, unmoved.</b> 2.08 GHz is the row this whole area's acceptance rests on —
    /// 3.480158 nH, positive resistance, passive — and the brief's own "what must NOT change" is
    /// that a well-conditioned point come back bit-identical. The floor is a diagnostic read off
    /// the sweep after the fact; it cannot reach an s-parameter, and this is what says it did not.
    /// </summary>
    [Fact]
    public void TheWellConditionedRowIsUnchanged()
    {
        var r = Solve(MmicCoilFixture.Coil(0.32e9), CoilPorts(), [2.08e9], CoilMesh);
        var pt = Assert.Single(r.Points);
        var zs = -1.0 / RFNetwork.SToY(pt.S, [new Complex(50, 0), new Complex(50, 0)])[1, 0];

        _out.WriteLine($"2.08 GHz: Zs = {zs.Real:F4} + j{zs.Imaginary:F4}, {Nh(pt.S, 2.08e9):F6} nH");
        Assert.Equal(3.480158, Nh(pt.S, 2.08e9), 5);
        Assert.True(zs.Real > 0);
        Assert.True(RFNetwork.Passivity(pt.S) <= 1.0);
        Assert.DoesNotContain(r.Notes, n => n.Contains("DeembedErrorFloor"));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // M5 — R-lfp-3's own entry test, which the brief says comes before any of the rest of it
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private const double U = 1e-6;

    private static PlanarPolygon Rect(double x1, double y1, double x2, double y2) =>
        PlanarLineFixtures.Rect(x1 * U, y1 * U, x2 * U, y2 * U);

    private static readonly LayerStack GaAs2Lm = new(
        Termination.Pec,
        [
            new MediumLayer(100e-6, new EmMaterial(12.90, 0.0006)),
            new MediumLayer(  3e-6, new EmMaterial( 1.00, 0.0000)),
        ],
        Termination.Air);

    /// <summary>
    /// The same 700 µm × 10 µm line, driven two ways: by the EDGE delta gap every measured
    /// structure here has, and by the ground-referenced port whose − terminal is the plane — which
    /// is <see cref="PlanarDcSolve"/>'s conduction terminal, one dimension over.
    /// </summary>
    private static (PlanarProblem, PlanarPort[]) Line(bool edge)
    {
        var m1 = new PlanarConductorLayer("Metal1", [Rect(0, 0, 700, 10)], 4.1e7, 3e-6, 100e-6);
        if (edge)
            return (new PlanarProblem([m1], GroundedSlab.GaAsStarter, 1e12, MediumStack: GaAs2Lm),
                    [new PlanarPort(1, new EmPoint(0, 5 * U), PlanarPortSide.MinX, 50.0) { LayerIndex = 0 },
                     new PlanarPort(2, new EmPoint(700 * U, 5 * U), PlanarPortSide.MaxX, 50.0) { LayerIndex = 0 }]);

        PlanarVia[] vias =
        [
            new(PlanarVia.GroundTerminal, 0, [Rect(0, 0, 10, 10)], 4.1e7),
            new(PlanarVia.GroundTerminal, 0, [Rect(690, 0, 700, 10)], 4.1e7),
        ];
        return (new PlanarProblem([m1], GroundedSlab.GaAsStarter, 1e12,
                                  MediumStack: GaAs2Lm, Vias: vias),
                [new PlanarPort(1, new EmPoint(5 * U, 5 * U), PlanarPortSide.MinX, 50.0)
                     { LayerIndex = 0, Kind = PlanarPortKind.Internal },
                 new PlanarPort(2, new EmPoint(695 * U, 5 * U), PlanarPortSide.MinX, 50.0)
                     { LayerIndex = 0, Kind = PlanarPortKind.Internal }]);
    }

    /// <summary>
    /// <b>R-lfp-3's defining property, measured on the port kind this kernel ALREADY has that is the
    /// right shape.</b> The brief's own cheapest test of any candidate: solve one uniform line at
    /// two frequencies a decade apart and read how much of the structure survives to the RAW
    /// measurement, with no calibration anywhere in the path.
    ///
    /// <para><b>The edge delta gap reads 700 µm of metal as −121.6 dB of insertion loss at 100 MHz
    /// and rises at 6.02 dB per octave</b>, which is a series capacitance and nothing else
    /// (<c>docs/design/mom-engine.md</c> §10.13(a), reproduced here on the GaAs stack). Every
    /// published number down there is the seventh decimal place of that, which is the whole of the
    /// wall this brief is about.</para>
    ///
    /// <para><b>A port referenced to the ground plane reads the same metal as 0.994 through, flat
    /// across the decade to two decimal places of a dB.</b> No error box, no a₂₁, no 1/|a₂₁|² and
    /// nothing to de-embed. So the wall is a property of the DELTA GAP and not of the method of
    /// moments, of the layered Green's function, of the calibration or of this stack — and the fill,
    /// the excitation and the solve already handle a ground-referenced port correctly at the bottom
    /// of a band.</para>
    ///
    /// <para><b>It is NOT a drop-in and this test does not say it is.</b> A via to the plane under
    /// each end is a different circuit from a series-fed line, so nobody measuring a spiral's two
    /// terminals can substitute one. What R-lfp-3 needs is this terminal PAIR at a conductor's end
    /// FACE with the return through the medium; what this measures is that the pair is the half
    /// that matters.</para>
    /// </summary>
    [Fact]
    public void ADeltaGapLosesTheStructureAsOmegaFalls_AndAGroundReferencedPortDoesNot()
    {
        double[] fs = [0.1e9, 1.0e9];
        var mesh = new PlanarMeshSettings(Auto: false, CellsPerWavelength: 20, EdgeMesh: false,
                                          MinCellsAcrossConductor: 2, MeshFrequencyHz: 1e12);
        var st = new PlanarSolveSettings(Deembed: false);

        double DbPerOctave(bool edge, out double lowMag)
        {
            var (problem, ports) = Line(edge);
            var r = Solve(problem, ports, fs, mesh, st);
            double lo = r.Points[0].RawS[1, 0].Magnitude, hi = r.Points[1].RawS[1, 0].Magnitude;
            lowMag = lo;
            _out.WriteLine($"{(edge ? "edge delta gap" : "ground-referenced"),-18} N = {r.UnknownCount}: " +
                           $"raw |S21| {lo:0.0000e+00} at 100 MHz, {hi:0.0000e+00} at 1 GHz");
            return 20 * Math.Log10(hi / lo) / Math.Log2(fs[1] / fs[0]);
        }

        double edgeSlope = DbPerOctave(true, out double edgeLow);
        double gndSlope  = DbPerOctave(false, out double gndLow);

        // The delta gap: a 6 dB/octave series capacitance, and a piece of copper reading as an open.
        Assert.Equal(6.02, edgeSlope, 0.05);
        Assert.True(edgeLow < 1e-5, $"a delta-gap-fed line must read as an open at 100 MHz; got {edgeLow:0.0e+00}");

        // The ground-referenced port: the structure, flat.
        Assert.Equal(0.0, gndSlope, 0.02);
        Assert.InRange(gndLow, 0.95, 1.0);
    }
}
