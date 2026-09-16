// R-pcal7-4 — a row that is not a passive network is named on the FILE's own face.
//
// PCAL2's finding one case further on. A `.sNp` on disk carries no notes, and the run's NOT PASSIVE
// sentence is the last of thirty; on the shipped MMIC spiral the four rows below ~0.8 GHz read
// 110 nH against a real 2.8 nH with sigma_max up to 1.09, and a user opening that file in the Data
// Display six months later sees a smooth, plausible curve with nothing on it to say so.
//
// Gated against `ValidityCaveats` directly rather than through a run: the predicate is the whole of
// the change, and a full-wave sweep that happens to be non-passive costs minutes to reproduce and
// would be gating the sweep rather than the sentence.

using System.Numerics;
using CircuitRF.Design.Layout.Em;
using CircuitRF.Engine.Mom;
using NumFlat;
using Xunit;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests.Em;

public sealed class NonPassiveCaveatTests(ITestOutputHelper output)
{
    private static PlanarSolveResult Result(params PlanarPassivityExcess[] nonPassive) =>
        new()
        {
            Points           = [],
            CoreFillCount    = 0,
            UnknownCount     = 0,
            StandardCount    = 0,
            CoreBuildMs      = 0,
            Notes            = [],
            NonPassivePoints = nonPassive,
        };

    [Fact]
    public void APassiveSweepDeclaresNothing()
        => Assert.Empty(EmSnpProvenance.ValidityCaveats(Result()));

    /// <summary>
    /// <b>The frequencies, not just the count.</b> "4 of 50 rows" tells a reader that some of their
    /// plot is wrong and not which of it — and the action it implies, raising the sweep's lower
    /// edge, cannot be taken from a count.
    /// </summary>
    [Fact]
    public void ANonPassiveSweepNamesTheBandAndTheWorstValue()
    {
        var caveats = EmSnpProvenance.ValidityCaveats(Result(
            new PlanarPassivityExcess(160e6, 1.0919),
            new PlanarPassivityExcess(320e6, 1.0384),
            new PlanarPassivityExcess(480e6, 1.0152),
            new PlanarPassivityExcess(640e6, 1.0046)));

        string caveat = Assert.Single(caveats);
        output.WriteLine(caveat);

        Assert.Contains("NOT A PASSIVE NETWORK", caveat, StringComparison.Ordinal);
        Assert.Contains("4 of these rows", caveat, StringComparison.Ordinal);
        Assert.Contains("160 MHz", caveat, StringComparison.Ordinal);
        Assert.Contains("640 MHz", caveat, StringComparison.Ordinal);
        Assert.Contains("1.0919", caveat, StringComparison.Ordinal);
        Assert.Contains("lower edge", caveat, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>ASCII only, and that is not a style rule.</b> Touchstone is written in an encoding this
    /// writer transliterates to: the first version of this line read "worst ?_max(S) = 1.0919" and
    /// "divides by a???" on the file. A caveat the file cannot spell is one nobody can act on.
    /// </summary>
    [Fact]
    public void TheCaveatSurvivesTheTouchstonesOwnEncoding()
    {
        string caveat = Assert.Single(EmSnpProvenance.ValidityCaveats(
            Result(new PlanarPassivityExcess(1.6e8, 1.09))));

        foreach (char c in caveat)
            Assert.True(c < 128, $"the caveat carries U+{(int)c:X4} ('{c}'), which the .sNp writes as '?'.");
    }

    /// <summary>A clearance breach and a non-passive sweep are two different facts and the file
    /// carries both — the clearance one first, because it is the cause where there is one.</summary>
    [Fact]
    public void BothDeclarationsAreCarried()
    {
        var mesh = SurfaceMesher.Mesh(
            PlanarLineForCaveat(), new PlanarMeshSettings(Auto: false, CellsPerWavelength: 10,
                                                          EdgeMesh: false)).Mesh;
        var ports = PlanarPorts.ResolveAll(mesh,
        [
            new PlanarPort(1, new EmPoint(0,      5e-6), PlanarPortSide.MinX, 50.0) { LayerIndex = 0 },
            new PlanarPort(2, new EmPoint(400e-6, 5e-6), PlanarPortSide.MaxX, 50.0) { LayerIndex = 0 },
        ]);

        var r = new PlanarSolveResult
        {
            Points = [], CoreFillCount = 0, UnknownCount = 0, StandardCount = 0, CoreBuildMs = 0,
            Notes = [],
            FeedClearances = [new PlanarFeedClearance(ports[0].Number, PlanarNeighbourClass.Driven,
                                                      50e-6, 500e-6, 100e-6, 300e-6)],
            NonPassivePoints = [new PlanarPassivityExcess(160e6, 1.09)],
        };

        var caveats = EmSnpProvenance.ValidityCaveats(r);
        Assert.Equal(2, caveats.Count);
        Assert.Contains("OUTSIDE", caveats[0], StringComparison.Ordinal);
        Assert.Contains("NOT A PASSIVE NETWORK", caveats[1], StringComparison.Ordinal);
    }

    private static PlanarProblem PlanarLineForCaveat() =>
        new([new PlanarConductorLayer("Metal1",
                [new PlanarPolygon([new EmPoint(0, 0), new EmPoint(400e-6, 0),
                                    new EmPoint(400e-6, 10e-6), new EmPoint(0, 10e-6)])],
                4.1e7, 3e-6)],
            GroundedSlab.GaAsStarter, 2e9);
}
