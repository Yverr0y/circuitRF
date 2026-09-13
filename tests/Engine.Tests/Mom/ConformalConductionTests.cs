// A CUT CELL IS NOT ITS RECTANGLE, and two things downstream were reading the rectangle (2026-09-12).
//
// Both were found on one real board — a coupled pair whose arms mitre at 45° into a pad — and both
// are silent: neither produces a wrong number anyone can see, they produce a REFUSAL that names
// metal which is not there, and a published OPEN CIRCUIT that is passive at every frequency.
//
//  1. `PlanarPorts.MeasureFeedClearance` measured every cell by its GRID rectangle. At a shallow
//     oblique rim a cut cell's rectangle overhangs the metal by most of a cell, so a neighbour that
//     is hundreds of µm away reads as EXACTLY ZERO once the port's own profile edge falls under that
//     overhang. Same file, same geometry: "port 3's feed clearance is 0 substrate heights, 0 µm to
//     the nearest other conductor" with `Conformal`, "port 3's feed is clear" with `Staircase`.
//
//  2. Nothing asked whether the meshed structure still CONDUCTS where the artwork does. A rooftop
//     exists only where both cells' share of their common edge is swept by metal (R-cut-4's
//     `Anchored` test, all-or-nothing over a support's strips), so a coarse conformal mesh can
//     decline every rooftop across a mitre and cut the conductor in two — and every observable a
//     solve publishes stays healthy. `PlanarConductors.FindSeveredConductors` is what now catches it.
//
// The fixture is the real board's arm, with its rounded mitre corner squared off: what matters is a
// 45° rim on a mesh coarse enough that one cut cell spans a useful fraction of the conductor, which
// is what `Coarse` plus the edge mesh off produces.

using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using Xunit;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

public sealed class ConformalConductionTests(ITestOutputHelper output)
{
    private static readonly GroundedSlab Slab = new(0.9e-3, new EmMaterial(4.4, 0.02));
    private const double F = 1e9;
    private const double U = 1e-6;                     // the artwork below is quoted in µm

    private static PlanarMeshSettings Mesh(PlanarBoundaryCells cells, bool edge = false)
        => new(Auto: false, CellsPerWavelength: 5, EdgeMesh: edge, EdgeCells: 2,
               BoundaryCells: cells, MinCellsAcrossConductor: 2,
               CurrentModel: PlanarCurrentModel.TransmissionLine);

    /// <summary>
    /// One arm of the board this was found on: 254 µm of straight line, a 45° mitre down, and a
    /// 558.8 µm square pad. <paramref name="dy"/> lifts the whole arm, so two of them make the
    /// coupled pair.
    /// </summary>
    private static PlanarPolygon Arm(double dy)
    {
        EmPoint P(double x, double y) => new(x * U, (y + dy) * U);
        return new PlanarPolygon(
        [
            P(0,       718.82), P(2832.10, 718.82), P(3144.52, 406.40),   // top edge into the mitre
            P(3271.52, 406.40), P(3271.52, 558.80),                       // up the pad's left flank
            P(3830.32, 558.80), P(3830.32,   0.00),                       // round the pad
            P(3271.52,   0.00), P(3271.52, 152.40),
            P(3144.52, 152.40), P(2779.49, 464.82), P(0,      464.82),    // back along the bottom
        ]);
    }

    /// <summary>The pair, with the second arm far enough away that it is not a calibration question
    /// — this file is about the MESH, and a clearance refusal here would be about something else.
    /// The ports sit at the outer end of each arm's straight run and on each pad's far face.</summary>
    private static (PlanarProblem Problem, PlanarPort[] Ports) Pair(double armGapUm)
    {
        double dy = 718.82 + armGapUm;
        var problem = new PlanarProblem(
            [new PlanarConductorLayer("Metal", [Arm(0), Arm(dy)], 5.8e7, 35e-6)], Slab, F);

        PlanarPort Lo(int n, double y) => new(n, new EmPoint(0, y * U), PlanarPortSide.MinX, 50.0);
        PlanarPort Hi(int n, double y) => new(n, new EmPoint(3830.32 * U, y * U), PlanarPortSide.MaxX, 50.0);

        return (problem, [Lo(1, 591.82), Hi(2, 279.40), Lo(3, 591.82 + dy), Hi(4, 279.40 + dy)]);
    }

    private static (PlanarMesh Mesh, IReadOnlyList<PlanarPortResolution> Ports)
        Resolve(PlanarProblem problem, PlanarPort[] ports, PlanarMeshSettings settings)
    {
        var mesh = SurfaceMesher.Mesh(problem, settings).Mesh;
        return (mesh, PlanarPorts.ResolveAll(mesh, ports));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 1 — the clearance question is asked of the METAL
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The two arms are 812.8 µm apart — 0.9 substrate heights, a real and reportable separation.
    /// Staircased, the check says so. Conformally cut, it used to say ZERO, because a mitre cell's
    /// rectangle reaches across the gap even though its metal does not. The two spellings of the
    /// same artwork must agree about a distance that does not depend on either.
    /// </summary>
    [Fact]
    public void ACutCellsRectangleIsNotItsMetal_AndTheClearanceMeasuresTheMetal()
    {
        const double gapUm = 812.8;

        double Clearance(PlanarBoundaryCells cells)
        {
            var (problem, ports) = Pair(gapUm);
            var (mesh, resolved) = Resolve(problem, ports, Mesh(cells));

            // Port 3 is the one the overhang reached: it is the UPPER arm's low-x port, and the
            // lower arm's mitre dips below its profile exactly inside the calibration's end run.
            var port = resolved.Single(p => p.Number == 3);
            var c = PlanarPorts.MeasureFeedClearance(
                mesh, port, resolved,
                endRunM: 3.0 * Slab.HeightM,
                drivenRequiredM: 5.0 * Slab.HeightM, passiveRequiredM: 2.0 * Slab.HeightM,
                slabHeightM: Slab.HeightM);
            Assert.NotNull(c);
            return c!.NearestM;
        }

        double staircase = Clearance(PlanarBoundaryCells.Staircase);
        double conformal = Clearance(PlanarBoundaryCells.Conformal);

        output.WriteLine($"staircase {staircase * 1e6:F1} µm, conformal {conformal * 1e6:F1} µm, " +
                         $"drawn {gapUm:F1} µm");

        // Neither may read zero, and the two may not disagree by more than a cell of the transverse
        // mesh — the same artwork is the same distance apart whichever way its rim was cut.
        Assert.True(conformal > 0, "the conformal mesh reported metal at zero distance");
        Assert.True(staircase > 0, "the staircased mesh reported metal at zero distance");
        Assert.Equal(staircase * 1e6, conformal * 1e6, 0);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 2 — a conductor the mesh has cut in two
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// On this mesh the mitre's cut cells carry no rooftop across the bend, so ports 1 and 2 — drawn
    /// on ONE polygon — end up in different conduction islands. That is the whole finding: the mesh
    /// is well formed, the solve would converge, and the answer would be a passive open circuit.
    /// </summary>
    [Fact]
    public void AMitreThatCarriesNoRooftopSeversItsConductor_AndIsFound()
    {
        var (problem, ports) = Pair(812.8);
        var (mesh, resolved) = Resolve(problem, ports, Mesh(PlanarBoundaryCells.Conformal));

        var severed = PlanarConductors.FindSeveredConductors(problem, mesh, resolved);
        foreach (var s in severed)
            output.WriteLine($"layer {s.LayerIndex} polygon {s.PolygonIndex}: " +
                             string.Join(" | ", s.Islands.Select(g => string.Join(",", g))));

        Assert.NotEmpty(severed);

        // Every entry names one drawn polygon and at least two islands; the ports in one island are
        // the ones that can still reach each other.
        Assert.All(severed, s =>
        {
            Assert.True(s.Islands.Count >= 2);
            Assert.All(s.Islands, g => Assert.NotEmpty(g));
        });

        // Both arms are the same shape, so both are severed, and each names the pair drawn on it.
        var found = severed.SelectMany(s => s.Islands).Select(g => g[0]).OrderBy(n => n).ToList();
        Assert.Equal([1, 2, 3, 4], found);
    }

    /// <summary>
    /// <b>The remedy the refusal names has to BIND</b> — the rule this directory has been burned by
    /// three times, and the reason the refusal does not say "raise Cells per wavelength". Resolving
    /// the rim is what restores conduction, under both boundary models.
    /// </summary>
    [Theory]
    [InlineData(PlanarBoundaryCells.Staircase)]
    [InlineData(PlanarBoundaryCells.Conformal)]
    public void TheEdgeMeshRestoresConduction_UnderEitherBoundaryModel(PlanarBoundaryCells cells)
    {
        var (problem, ports) = Pair(812.8);
        var (mesh, resolved) = Resolve(problem, ports, Mesh(cells, edge: true));

        var severed = PlanarConductors.FindSeveredConductors(problem, mesh, resolved);
        foreach (var s in severed)
            output.WriteLine($"STILL SEVERED: polygon {s.PolygonIndex} " +
                             string.Join(" | ", s.Islands.Select(g => string.Join(",", g))));

        Assert.Empty(severed);
    }

    /// <summary>
    /// <b>And the remedy the refusal REFUSES to name is inert, which is why it does not name it.</b>
    /// The mitre's pitch is set by the metal's own width and the detail floor, not by λ, so the mesh
    /// here is identical at cells/λ 5, 10, 20 and 40 — severed at all four. Measured rather than
    /// assumed, because "raise Cells per wavelength" is the sentence anyone would write.
    /// </summary>
    [Fact]
    public void CellsPerWavelengthIsInertOnThisMesh_WhichIsWhyTheRefusalDoesNotOfferIt()
    {
        var counts = new List<(int Cpw, int Bases, int Severed)>();
        foreach (int cpw in new[] { 5, 10, 20, 40 })
        {
            var (problem, ports) = Pair(812.8);
            var settings = Mesh(PlanarBoundaryCells.Conformal) with { CellsPerWavelength = cpw };
            var (mesh, resolved) = Resolve(problem, ports, settings);
            counts.Add((cpw, mesh.Bases.Count,
                        PlanarConductors.FindSeveredConductors(problem, mesh, resolved).Count));
        }

        foreach (var c in counts) output.WriteLine($"cells/λ {c.Cpw}: N = {c.Bases}, severed {c.Severed}");

        Assert.All(counts, c => Assert.Equal(counts[0].Bases, c.Bases));   // the knob moved nothing
        Assert.All(counts, c => Assert.True(c.Severed > 0));               // …and nothing was fixed
    }

    /// <summary>
    /// The detector may not fire on the ordinary conformal mesh, and a chamfered line is where that
    /// is decided: it meshes with cut cells that carry no basis at all — slivers of its OWN metal
    /// that R-cut-4 declines to drive — and those are separate one-cell "conductors" in the basis
    /// graph on every such run (<c>PlanarConductors.CarriesCurrent</c>'s own note). Asking the
    /// question of the PORTS rather than of the whole basis graph is what keeps this off them, and a
    /// false refusal here would be worse than the bug: it would stop runs that are correct today.
    ///
    /// <para>The ENDS are straight and full-height on purpose, so the cut cells are in the middle of
    /// the conductor where a rooftop really does have to cross them.</para>
    /// </summary>
    [Fact]
    public void AChamferedLinesUndrivenSliversAreNotASeveredConductor()
    {
        EmPoint P(double x, double y) => new(x * U, y * U);
        var problem = new PlanarProblem(
            [new PlanarConductorLayer("Metal",
                [new PlanarPolygon([P(0, 0), P(12000, 0), P(12000, 2900), P(8000, 2900),
                                    P(6000, 1800), P(4000, 2900), P(0, 2900)])],
                5.8e7, 35e-6)],
            Slab, 10e9);
        PlanarPort[] ports =
        [
            new(1, P(0, 1450), PlanarPortSide.MinX, 50.0),
            new(2, P(12000, 1450), PlanarPortSide.MaxX, 50.0),
        ];

        var (mesh, resolved) = Resolve(
            problem, ports,
            new PlanarMeshSettings(Auto: false, CellsPerWavelength: 20, EdgeMesh: true, EdgeCells: 3,
                                   BoundaryCells: PlanarBoundaryCells.Conformal));

        int cut = mesh.Cells.Count(c => c.IsCut);
        var conn = PlanarConductors.Of(mesh);
        int dead = Enumerable.Range(0, mesh.Cells.Count).Count(i => !conn.CarriesCurrent(i));
        output.WriteLine($"{mesh.Cells.Count} cells, {cut} cut, {dead} carrying no current");

        Assert.True(cut > 0, "this fixture is only meaningful if cells were actually cut");
        Assert.Empty(PlanarConductors.FindSeveredConductors(problem, mesh, resolved));
    }
}
