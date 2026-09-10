// M0 — the graded marcher, gated STRUCTURALLY on the mesh.
//
// brief-em-transmission-line-mesh.md §0d found that BuildGridLines' grading fan silently became a
// uniform run at the finest cell size whenever it APPROACHED an attractor, and nothing downstream
// could see it: the mesh was valid, the tiling exact, the solve smooth. It showed up only as cell
// counts that would not fall when the pitch was coarsened.
//
// So every gate here is a property OF THE MESH, and none of them solves anything. That is the
// brief's own testing rule and it is not a shortcut: SurfaceMesher.Mesh on a hand-built
// PlanarProblem is milliseconds, a de-embedded point is tens of seconds, and none of the properties
// the marcher is about need a Green's function to state. The one question that genuinely needed
// solved s-parameters — is the new mesh MORE right, not merely cheaper — was answered once in a
// scratch harness and its table is recorded in RESOLVED.md, not run here.
//
// Every bound below was MEASURED across 96 (fixture × cells/λ × cells-across) combinations before
// it was written down, and each one is violated by the pre-M0 marcher on the same fixtures — a gate
// nothing can fail is not a gate. The pre-M0 numbers are quoted at each assertion.

using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;

namespace CircuitRF.Engine.Tests.Mom;

public class MeshGradingTests
{
    /// <summary>cells/λ = 20 at 10 GHz, edge mesh on — §0d's own measurement conditions.</summary>
    private static PlanarMeshSettings At(int cellsPerWavelength = 20, int across = 4) =>
        new(Auto: false, CellsPerWavelength: cellsPerWavelength, MinCellsAcrossConductor: across);

    private static PlanarProblem Trace(double widthM, double lengthM, double fHz = 10e9) =>
        PlanarLineFixtures.Line(GroundedSlab.Fr4Starter, widthM, lengthM, fHz);

    private static double[] Steps(IReadOnlyList<double> lines)
    {
        var s = new double[lines.Count - 1];
        for (int i = 1; i < lines.Count; i++) s[i - 1] = lines[i] - lines[i - 1];
        return s;
    }

    /// <summary>The longest run of consecutive cells at (within 3% of) the FINEST cell in the axis.</summary>
    private static int LongestFinestRun(IReadOnlyList<double> lines)
    {
        var s = Steps(lines);
        double min = s.Min();
        int best = 0, run = 0;
        foreach (double v in s) { if (v <= min * 1.03) { run++; best = Math.Max(best, run); } else run = 0; }
        return best;
    }

    /// <summary>The largest ratio between two ADJACENT cells, either way round.</summary>
    private static double WorstAdjacentRatio(IReadOnlyList<double> lines)
    {
        var s = Steps(lines);
        double worst = 1.0;
        for (int i = 1; i < s.Length; i++)
            worst = Math.Max(worst, Math.Max(s[i] / s[i - 1], s[i - 1] / s[i]));
        return worst;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 1 — no collapsed run. THE defect, stated as a testable property.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(200e-6, 10e-3)]
    [InlineData(100e-6, 50e-3)]
    public void TheGradedFan_NeverCollapsesIntoAUniformRunAtTheFinestCellSize(double w, double len)
    {
        // Pre-M0 on these two, x axis: 2 and 110 consecutive cells at c0 (and 117 / 106 at
        // cells/λ = 10, where the wider bulk cell gives the crawl further to cover). A fan that has
        // stopped grading is invisible in every other observable the mesher reports, which is why
        // this is asserted on the STEPS and not on the cell count.
        //
        // 4 is generous against the 1-2 the marcher actually produces: two attractors can sit within
        // one edge cell of each other on genuinely narrow metal, and that is a real c0 pair rather
        // than a collapse.
        var report = SurfaceMesher.Mesh(Trace(w, len), At());

        Assert.True(LongestFinestRun(report.Mesh.GridX) <= 4,
            $"x: {LongestFinestRun(report.Mesh.GridX)} consecutive cells at the finest size — the " +
            "grading fan has collapsed into a uniform crawl (brief-em-transmission-line-mesh.md §0d).");
        Assert.True(LongestFinestRun(report.Mesh.GridY) <= 4,
            $"y: {LongestFinestRun(report.Mesh.GridY)} consecutive cells at the finest size.");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 2 — the fan grades in BOTH directions
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(200e-6, 10e-3, 10)]
    [InlineData(200e-6, 10e-3, 20)]
    [InlineData(200e-6, 10e-3, 40)]
    [InlineData(100e-6, 50e-3, 10)]
    [InlineData(100e-6, 50e-3, 20)]
    [InlineData(100e-6, 50e-3, 40)]
    [InlineData(2.9e-3, 20e-3, 20)]
    public void EveryAdjacentCellPair_StaysInsideTheDerivedGrowthRatio_ApproachingAnEdgeAsWellAsLeavingOne(
        double w, double len, int cellsPerWavelength)
    {
        // The near end of an interval always graded correctly, because the size field GROWS away
        // from an attractor and the half-step look-ahead therefore never bound. Only the APPROACH
        // failed — so a gate written on the first cells of the interval would have passed
        // throughout, and this one is deliberately written on every adjacent pair instead.
        //
        // The bound is GrowthRatioFor's own ceiling, which is what the size field promises: no two
        // neighbouring cells may differ by more than MaxGrowthRatio. Measured worst case with the
        // M0 marcher across all seven rows here: exactly 3.000 (the ratio is derived and clamped, so
        // it lands ON the ceiling rather than under it). Pre-M0 the same rows read 476, 238, 102,
        // 238, 115, 60 and 3.1 — the last of which shows the defect reaches the FR-4 hero too.
        var report = SurfaceMesher.Mesh(Trace(w, len), At(cellsPerWavelength));

        const double tol = 1.0 + 1e-9;
        Assert.True(WorstAdjacentRatio(report.Mesh.GridX) <= SurfaceMesher.MaxGrowthRatio * tol,
            $"x: adjacent cells differ by {WorstAdjacentRatio(report.Mesh.GridX):G4}×, past the " +
            $"{SurfaceMesher.MaxGrowthRatio:G3}× the size field promises.");
        Assert.True(WorstAdjacentRatio(report.Mesh.GridY) <= SurfaceMesher.MaxGrowthRatio * tol,
            $"y: adjacent cells differ by {WorstAdjacentRatio(report.Mesh.GridY):G4}×.");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 3 — monotonicity over the cells-across ladder. This is what closes §0a.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void LoweringCellsAcrossTheConductor_NeverRaisesTheCellCount()
    {
        // MinCellsAcrossConductor shipped as a control on 2026-09-09 with a NOTE saying its cell
        // count might not fall, because it did not: 8/6/4/3/2/1 across gave 220/200/180/160/180/200
        // on this fixture, so 1 cost as much as 6. That was never a property of the control — it was
        // the collapsed run getting longer as the bulk pitch coarsened. With the M0 marcher the same
        // ladder reads 264/220/198/176/154/154.
        //
        // Asserted as non-INCREASING rather than strictly decreasing: the transverse grid is a whole
        // number of cells, so two adjacent rungs can legitimately land on the same one (2 and 1 do).
        var p = Trace(200e-6, 10e-3);

        int previous = int.MaxValue;
        foreach (int across in new[] { 8, 6, 4, 3, 2, 1 })
        {
            var report = SurfaceMesher.Mesh(p, At(across: across));
            Assert.True(report.CellCount <= previous,
                $"{across} cell(s) across cost {report.CellCount} cells, more than the previous " +
                $"rung's {previous} — coarsening the transverse pitch must not make the mesh bigger.");
            previous = report.CellCount;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 4 — translation invariance. L8b's own knife edge, and a marcher change is exactly the
    // class of edit that reintroduces it.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(200e-6, 10e-3)]
    [InlineData(100e-6, 50e-3)]
    public void MovingTheArtwork3Point7mm_LeavesTheMeshUnchanged(double w, double len)
    {
        // The grading ratio is DERIVED rather than fixed precisely so the size field stays
        // continuous, because a discontinuous one put the marcher's own accumulation point on a
        // knife edge and moving the same rectangle 3.7 mm changed the mesh by 33% (see Mesh's
        // growth-ratio derivation). The M0 step has the same continuity requirement — its two
        // branches meet at d = c0 — so it is asserted the same way.
        const double d = 3.7e-3;
        var at0   = SurfaceMesher.Mesh(Trace(w, len), At());
        var moved = SurfaceMesher.Mesh(
            PlanarLineFixtures.Problem(GroundedSlab.Fr4Starter, 10e9,
                PlanarLineFixtures.Rect(d, d - 0.5 * w, d + len, d + 0.5 * w)),
            At());

        Assert.Equal(at0.CellCount, moved.CellCount);
        Assert.Equal(at0.UnknownCount, moved.UnknownCount);
        Assert.Equal(at0.Mesh.GridX.Count, moved.Mesh.GridX.Count);
        Assert.Equal(at0.Mesh.GridY.Count, moved.Mesh.GridY.Count);

        // Coordinates too, not just counts: the same gridline pattern shifted by exactly d. The
        // tolerance is the absolute round-off of adding 3.7 mm to a 3 µm cell, nothing looser.
        for (int i = 0; i < at0.Mesh.GridX.Count; i++)
            Assert.Equal(at0.Mesh.GridX[i], moved.Mesh.GridX[i] - d, 15);
        for (int i = 0; i < at0.Mesh.GridY.Count; i++)
            Assert.Equal(at0.Mesh.GridY[i], moved.Mesh.GridY[i] - d, 15);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 5 — the cheap safety net: with no attractors the marcher is never reached at all
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(100e-6, 50e-3, 280, 486)]
    [InlineData(200e-6, 10e-3,  56,  94)]
    [InlineData(2.9e-3, 20e-3, 140, 247)]
    public void WithTheEdgeMeshOff_ManhattanArtworkIsBitIdenticalToThePreM0Mesh(
        double w, double len, int cells, int unknowns)
    {
        // No edge mesh ⇒ no attractors ⇒ BuildGridLines takes its ungraded branch and subdivides
        // each interval uniformly, so nothing the marcher does can reach this mesh. The counts below
        // were measured on the pre-M0 tree and are pinned here rather than merely asserted to be
        // "the same as something", so this stays a net if the ungraded branch is ever touched.
        //
        // Auto is off already — EdgeMesh = false on a Default settings record is INERT, because
        // Resolved collapses Auto to the default edge mesh. That trap cost a measurement in §0a.
        var report = SurfaceMesher.Mesh(Trace(w, len), At() with { EdgeMesh = false });

        Assert.Equal(cells, report.CellCount);
        Assert.Equal(unknowns, report.UnknownCount);
    }
}
