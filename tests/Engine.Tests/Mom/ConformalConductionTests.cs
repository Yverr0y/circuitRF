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
    /// <summary>
    /// <b>LF2 — and the sweep asks BEFORE it splits 0 Hz and the sub-floor points off.</b>
    ///
    /// <para>The check used to sit below that split, so a sweep with no fitted point in it returned
    /// early and never asked — and that is the worst place to skip it. The conduction solve answers
    /// a disconnected port with EXACTLY zero by design (LF1 §4), so a severed mesh publishes a
    /// clean, exact open circuit with nothing anywhere to say so. All three sweep shapes are asked
    /// here because all three take different paths out of <c>PlanarSolve.Run</c>.</para>
    /// </summary>
    [Theory]
    [InlineData("AC only")]
    [InlineData("DC only")]
    [InlineData("below the fit floor only")]
    [InlineData("DC and AC")]
    public void ASeveredConductorRefusesWHATEVERShapeTheSweepIs(string shape)
    {
        var (problem, ports) = Pair(812.8);
        var mesh = SurfaceMesher.Mesh(problem, Mesh(PlanarBoundaryCells.Conformal)).Mesh;
        var resolved = PlanarPorts.ResolveAll(mesh, ports);
        Assert.NotEmpty(PlanarConductors.FindSeveredConductors(problem, mesh, resolved));

        double[] freqs = shape switch
        {
            "AC only"                  => [F],
            "DC only"                  => [0.0],
            "below the fit floor only" => [1e5, 1e6],
            _                          => [0.0, F],
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => PlanarSolve.Run(problem, mesh, resolved, freqs,
                                  new PlanarSolveSettings(Deembed: false)));
        output.WriteLine($"{shape}: {ex.Message[..96]}…");
        Assert.Contains("SEVERED", ex.Message);
    }

    /// <summary>
    /// <b>LF3 — the KERNEL re-meshes rather than handing the user a choice of two remedies.</b>
    ///
    /// <para>Owner report: told "turn the edge mesh on, or set Boundary cells to Staircase", a user
    /// picked the edge mesh — the expensive one. On the board this came from that is 4x the DUT's
    /// unknowns AND 4x every calibration standard, because a standard reproduces the DUT's own
    /// gridlines; staircasing the same artwork cost 2%. But staircase does NOT always clear it —
    /// on this fixture only resolving the rim does — so both are tried in cost order.</para>
    ///
    /// <para>The gate is fixture-independent on purpose: it computes what each remedy would cost and
    /// asserts the run landed on the CHEAPEST one that actually restores conduction. That holds on
    /// a board where staircase is enough and on one where it is not, which is the property, rather
    /// than naming a remedy this one fixture happens to need.</para>
    /// </summary>
    [Fact]
    public void AMeshThatSeversAConductorRemeshesItselfOnTheCHEAPESTRemedyThatWorks()
    {
        var (problem, ports) = Pair(812.8);

        // The premise, asked of the geometry the KERNEL meshes — the ground-path- and feed-extended
        // problem, not the raw one. On this fixture that distinction matters: raw, the edge mesh
        // alone clears it; extended, neither remedy alone does and the pair of them does.
        var (g0, _, _)  = PlanarGroundPath.Extend(problem, ports);
        var (ext, _, _) = PlanarFeedExtension.Extend(g0, ports, null);

        int CostIfItWorks(PlanarMeshSettings ms)
        {
            // Through the KERNEL's own mesh wrapper — it passes PlanarEdgeReference.LocalConductorWidth,
            // and a bare SurfaceMesher.Mesh here silently measures a different mesh.
            var rep = new PlanarKernel().Mesh(ext, ms, ports: ports);
            if (!rep.CanSolve) return int.MaxValue;
            var rp = PlanarPorts.ResolveAll(rep.Mesh, ports);
            return PlanarConductors.FindSeveredConductors(ext, rep.Mesh, rp).Count == 0
                ? rep.Mesh.Bases.Count : int.MaxValue;
        }

        Assert.Equal(int.MaxValue, CostIfItWorks(Mesh(PlanarBoundaryCells.Conformal)));

        // Exactly the three the kernel will try: the refusal's two remedies and both together.
        int best = int.MaxValue;
        foreach (var ms in (PlanarMeshSettings[])[
            Mesh(PlanarBoundaryCells.Staircase),
            Mesh(PlanarBoundaryCells.Conformal, edge: true),
            Mesh(PlanarBoundaryCells.Staircase, edge: true)])
            best = Math.Min(best, CostIfItWorks(ms));

        output.WriteLine($"cheapest remedy that restores conduction: N = {best:N0}");
        Assert.True(best < int.MaxValue, "no remedy works — the fixture moved");

        var result = new PlanarKernel().Solve(
            problem, Mesh(PlanarBoundaryCells.Conformal), ports, [F],
            new PlanarSolveSettings(Deembed: false));

        foreach (var n in result.Notes) output.WriteLine("  " + n);
        var note = Assert.Single(result.Notes, n => n.Contains("The mesh severed"));

        // It has to say WHICH conductor, BETWEEN WHICH PORTS, and what it cost — a user who is not
        // told the count will go looking for it.
        Assert.Contains("Metal", note);
        Assert.Contains("port 1", note);
        Assert.Contains($"N = {best:N0}", note);

        // The CHEAPEST one that works, not the first one tried.
        Assert.Equal(best, result.MeshReport.Mesh.Bases.Count);
        Assert.Empty(PlanarConductors.FindSeveredConductors(ext, result.MeshReport.Mesh, result.Ports));
        // NOT asserted on |S₂₁| being "large". With de-embedding off the raw answer is dominated by
        // the delta-gap port rather than by the structure at every frequency (mom-engine.md §10.13a),
        // so a magnitude gate here would be measuring the excitation. The structural statement — the
        // meshed conductor conducts, and the run landed on the cheapest mesh that makes it so — is
        // the claim; the companion test below pins the NUMBERS against that mesh asked for directly.
    }

    [Fact]
    public void ARunThatWasNEVERSeveredSaysNothing_AndTheRecoveredOneMatchesItExactly()
    {
        var (problem, ports) = Pair(812.8);
        var st = new PlanarSolveSettings(Deembed: false);

        // The remedy this fixture needs, asked for explicitly — no recovery, no note.
        var direct = new PlanarKernel().Solve(
            problem, Mesh(PlanarBoundaryCells.Staircase, edge: true), ports, [F], st);
        Assert.DoesNotContain(direct.Notes, n => n.Contains("The mesh severed"));

        // …and the conformal run that recovered onto it lands on the same mesh and the same numbers,
        // bit for bit, because it IS that mesh rather than something like it.
        var recovered = new PlanarKernel().Solve(
            problem, Mesh(PlanarBoundaryCells.Conformal), ports, [F], st);
        Assert.Contains(recovered.Notes, n => n.Contains("The mesh severed"));
        Assert.Equal(direct.MeshReport.Mesh.Bases.Count, recovered.MeshReport.Mesh.Bases.Count);
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                Assert.Equal(direct.Solve.Points[0].S[r, c], recovered.Solve.Points[0].S[r, c]);
    }

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
