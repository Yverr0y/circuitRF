// The LOCAL edge reference length — PlanarEdgeReference.LocalConductorWidth.
//
// Owner report, 2026-09-09, after four rounds on the same complaint: on a connector cutout carrying
// a 360 um feature beside 3.6 mm metal, the edge fans WERE the mesh. 513 cells, of which 442 went
// away when the edge mesh was switched off, because the edge cell is 3% of the NARROWEST conductor
// anywhere in the layout and that one cell is then placed at EVERY conductor edge. The owner put it
// exactly: the narrow trace's edge mesh continues into the wide trace even though it is not needed
// for accuracy there.
//
// LocalConductorWidth gives each attractor its own c0 from the metal AT that edge. The grading rate
// g is untouched, so the size field h(x) = min_i [c0_i + g*|x - a_i|] differs only in its floors,
// and every c0_i is floored at the global c0 — which is what makes the whole change a pointwise
// RELAXATION of the old field and gives the monotonicity gate below its teeth.
//
// Nothing here solves anything: every property is a property of the mesh. Same rule as
// MeshGradingTests, and for the same reason.

using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

public class LocalEdgeReferenceTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _out = output;

    private static PlanarMeshSettings At(int cellsPerWavelength = 20, int across = 4) =>
        new(Auto: false, CellsPerWavelength: cellsPerWavelength, MinCellsAcrossConductor: across);

    /// <summary>
    /// The owner's shape, reduced to its essential: a NARROW line butted onto a WIDE one. The narrow
    /// half sets the global narrowest conductor, so under the old rule its 3%-of-360-um edge cell is
    /// what every edge of the wide half is meshed at too.
    /// </summary>
    private static PlanarProblem NarrowMeetsWide(
        double narrowW = 360e-6, double wideW = 3.6e-3,
        double narrowLen = 2.4e-3, double wideLen = 3.6e-3, double fHz = 10e9) =>
        PlanarLineFixtures.Problem(GroundedSlab.Fr4Starter, fHz,
            PlanarLineFixtures.Rect(0, -0.5 * narrowW, narrowLen, 0.5 * narrowW),
            PlanarLineFixtures.Rect(narrowLen, -0.5 * wideW, narrowLen + wideLen, 0.5 * wideW));

    private static IEnumerable<PlanarProblem> Fixtures()
    {
        yield return NarrowMeetsWide();
        yield return NarrowMeetsWide(100e-6, 5e-3, 1e-3, 5e-3);
        yield return PlanarLineFixtures.Line(GroundedSlab.Fr4Starter, 200e-6, 10e-3, 10e9);
        yield return PlanarLineFixtures.Line(GroundedSlab.Fr4Starter, 2.9e-3, 20e-3, 10e9);
        yield return PlanarLineFixtures.Taper(GroundedSlab.Fr4Starter, 2.9e-3, 200e-6, 10e-3, 10e9);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 1 — THE INVARIANT: it may only coarsen. This is what makes the mode safe to default on.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TheLocalReference_NeverProducesAFinerCell_NorMoreOfThem_OnAnyFixture()
    {
        // Every c0_i is floored at the global c0, so h_local(x) >= h_global(x) POINTWISE. A finer
        // cell anywhere, or a higher count, means a floor was skipped — and a local width is a
        // SAMPLED quantity, so without this a sampling artefact on some unseen artwork could refine
        // the mesh of a user who reached for the setting to make it smaller.
        foreach (var problem in Fixtures())
            foreach (int cpw in new[] { 5, 20 })
                foreach (int across in new[] { 1, 4, 8 })
                {
                    var s = At(cpw, across);
                    var global = SurfaceMesher.Mesh(problem, s, PlanarEdgeReference.ConductorWidth);
                    var local  = SurfaceMesher.Mesh(problem, s, PlanarEdgeReference.LocalConductorWidth);

                    Assert.True(local.CellCount <= global.CellCount,
                        $"cells/λ={cpw} across={across}: local {local.CellCount} > global {global.CellCount}");

                    // The COUNT is the invariant. The finest CELL is not, and the difference is
                    // worth stating rather than tightening away: a pointwise coarser field does not
                    // give pointwise coarser cells, because PartitionGraded rescales each interval
                    // to land exactly on its endpoints (xs[i] * length / last). A coarser field can
                    // therefore leave a slightly shorter remainder cell against a hard gridline.
                    // Measured worst case across this sweep: 4% below the global mesh's finest cell.
                    Assert.True(local.MinCellEdgeM >= global.MinCellEdgeM * 0.9,
                        $"cells/λ={cpw} across={across}: local finest cell {local.MinCellEdgeM:E3} " +
                        $"is more than 10% below global {global.MinCellEdgeM:E3} — that is past the " +
                        "rescale remainder and means a c0 floor was skipped");
                }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 2 — it actually DOES something on the shape it was written for
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void OnANarrowTraceMeetingAWideOne_TheWideHalfsEdgeCellsAreNoLongerSizedByTheNarrowHalf()
    {
        var problem = NarrowMeetsWide();
        var s = At();

        var global = SurfaceMesher.Mesh(problem, s, PlanarEdgeReference.ConductorWidth);
        var local  = SurfaceMesher.Mesh(problem, s, PlanarEdgeReference.LocalConductorWidth);

        _out.WriteLine($"global: {global.CellCount} cells, {global.Mesh.GridX.Count}x{global.Mesh.GridY.Count} grid");
        _out.WriteLine($"local : {local.CellCount} cells, {local.Mesh.GridX.Count}x{local.Mesh.GridY.Count} grid");

        // A gate nothing can fail is not a gate: assert a real reduction, not merely "<=".
        Assert.True(local.CellCount < global.CellCount,
            $"the local reference changed nothing on the fixture it exists for: {local.CellCount} vs {global.CellCount}");

        // And the saving is in the GRID, not in cells that happened to miss the metal — which is the
        // difference between shortening a fan and merely rejecting cells later.
        Assert.True(local.Mesh.GridY.Count < global.Mesh.GridY.Count,
            "the fan across the wide half's long edges did not shorten");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 3 — the two older references are untouched, to the bit
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TheGlobalReferences_AreBitIdenticalToWhatTheyAlwaysWere()
    {
        // Every recorded number in HISTORY.md was taken on ConductorWidth or CellSize, so both must
        // survive the per-attractor rework unchanged. The pinned counts are the pre-change tree's.
        var line = PlanarLineFixtures.Line(GroundedSlab.Fr4Starter, 200e-6, 10e-3, 10e9);
        foreach (var kind in new[] { PlanarEdgeReference.ConductorWidth, PlanarEdgeReference.CellSize })
        {
            var r = SurfaceMesher.Mesh(line, At(), kind);
            _out.WriteLine($"{kind}: {r.CellCount} cells, c0 = {r.EdgeReferenceLengthM * 0.03:E3} m");
        }

        // 198 is M0's own post-fix ladder at 4 cells across (264/220/198/176/154/154 over 8..1) —
        // PlanarMeshSettings.MinCellsAcrossConductor records it. The PRE-M0 number was 180; quoting
        // that one is the mistake this line is written out to stop being made twice.
        Assert.Equal(198, SurfaceMesher.Mesh(line, At(), PlanarEdgeReference.ConductorWidth).CellCount);
    }

    [Fact]
    public void WithTheEdgeMeshOff_EveryReferenceGivesTheSameMesh()
    {
        // No attractors, no fan, nothing for a per-edge c0 to be. If these ever differ, the mode is
        // reaching the ungraded branch, which it has no business doing.
        var s = At() with { EdgeMesh = false };
        foreach (var problem in Fixtures())
        {
            var global = SurfaceMesher.Mesh(problem, s, PlanarEdgeReference.ConductorWidth);
            var local  = SurfaceMesher.Mesh(problem, s, PlanarEdgeReference.LocalConductorWidth);
            Assert.Equal(global.CellCount, local.CellCount);
            Assert.Equal(global.Mesh.GridX, local.Mesh.GridX);
            Assert.Equal(global.Mesh.GridY, local.Mesh.GridY);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 4 — a uniform line has ONE width, so there is nothing local to find
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(200e-6, 10e-3)]
    [InlineData(2.9e-3, 20e-3)]
    public void OnAUniformLine_TheLocalReferenceChangesNothing(double w, double len)
    {
        // The narrowest conductor IS the metal at every edge here, so c0_i = c0 for all i and the
        // field is the same field. This is the test that catches the local measurement drifting off
        // the geometry — a width measured wrong shows up here as a mesh that moved when it could
        // not have.
        var line = PlanarLineFixtures.Line(GroundedSlab.Fr4Starter, w, len, 10e9);
        var global = SurfaceMesher.Mesh(line, At(), PlanarEdgeReference.ConductorWidth);
        var local  = SurfaceMesher.Mesh(line, At(), PlanarEdgeReference.LocalConductorWidth);

        Assert.Equal(global.CellCount, local.CellCount);
        Assert.Equal(global.Mesh.GridX, local.Mesh.GridX);
        Assert.Equal(global.Mesh.GridY, local.Mesh.GridY);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 5 — translation invariance survives, because the field is still Lipschitz at one rate
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MovingTheArtwork3Point7mm_LeavesTheLocalMeshUnchanged()
    {
        const double d = 3.7e-3;
        var at0 = SurfaceMesher.Mesh(NarrowMeetsWide(), At(), PlanarEdgeReference.LocalConductorWidth);
        var moved = SurfaceMesher.Mesh(
            PlanarLineFixtures.Problem(GroundedSlab.Fr4Starter, 10e9,
                PlanarLineFixtures.Rect(d, d - 180e-6, d + 2.4e-3, d + 180e-6),
                PlanarLineFixtures.Rect(d + 2.4e-3, d - 1.8e-3, d + 6e-3, d + 1.8e-3)),
            At(), PlanarEdgeReference.LocalConductorWidth);

        Assert.Equal(at0.CellCount, moved.CellCount);
        Assert.Equal(at0.Mesh.GridX.Count, moved.Mesh.GridX.Count);
        Assert.Equal(at0.Mesh.GridY.Count, moved.Mesh.GridY.Count);
        for (int i = 0; i < at0.Mesh.GridX.Count; i++)
            Assert.Equal(at0.Mesh.GridX[i], moved.Mesh.GridX[i] - d, 15);
        for (int i = 0; i < at0.Mesh.GridY.Count; i++)
            Assert.Equal(at0.Mesh.GridY[i], moved.Mesh.GridY[i] - d, 15);
    }
}
