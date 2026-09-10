// M0 — the mesh-frequency parameter, engine side.
//
// The mesh has always been sized at the sweep's own top frequency. M0 lets a user size it lower,
// which is a pure cost reduction with a stated accuracy cost — and it is the ONLY milestone in
// brief-em-sweep-performance.md that can change an answer, which is why its own gate is an accuracy
// MEASUREMENT (below, Category=Benchmark) rather than a pass/fail.
//
// The routine tests here are the cheap structural ones: null reproduces today's behaviour exactly,
// the value survives Auto, and the report says what the mesh was actually sized at rather than
// claiming the sweep's top. That last one is the whole of R-emp-2 — a report that names the wrong
// frequency is precisely the class of silently wrong statement this area keeps finding.

using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;

namespace CircuitRF.Engine.Tests.Mom;

public class MeshFrequencyTests
{
    private static PlanarProblem Fr4Hero(double fHz = 20e9) =>
        PlanarLineFixtures.Problem(GroundedSlab.Fr4Starter, fHz,
            PlanarLineFixtures.Rect(0, 0, 20e-3, PlanarLineFixtures.Fr4HeroWidthM));

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Null is today's behaviour, bit for bit
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Unset_MeshesExactlyAsBefore_SizedAtTheSweepTop()
    {
        var p = Fr4Hero(20e9);

        var baseline = SurfaceMesher.Mesh(p, PlanarMeshSettings.Default);
        var explicitTop = SurfaceMesher.Mesh(
            p, PlanarMeshSettings.Default with { MeshFrequencyHz = 20e9 });

        // Not "about the same" — the same. Setting the control TO the sweep's top must be a no-op,
        // or every measured number in this directory's CLAUDE.md would have to be re-taken.
        Assert.Equal(baseline.UnknownCount, explicitTop.UnknownCount);
        Assert.Equal(baseline.CellCount, explicitTop.CellCount);
        Assert.Equal(baseline.GuidedWavelengthM, explicitTop.GuidedWavelengthM);
        Assert.Equal(baseline.MaxCellSizeM, explicitTop.MaxCellSizeM);
        Assert.Equal(20e9, baseline.FrequencyHz);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // It actually sizes the mesh — and the saving is AXIAL ONLY (R-emp-1's own caveat)
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HalvingIt_CoarsensTheMesh_ButNotQuadratically()
    {
        var p = Fr4Hero(20e9);

        var full = SurfaceMesher.Mesh(p, PlanarMeshSettings.Default);
        var half = SurfaceMesher.Mesh(p, PlanarMeshSettings.Default with { MeshFrequencyHz = 10e9 });

        Assert.True(half.UnknownCount < full.UnknownCount,
            $"halving the mesh frequency must lower N: {full.UnknownCount} -> {half.UnknownCount}");

        // λ_g doubles, so the λ-driven cap doubles too. The TRANSVERSE pitch is set by
        // MinCellsAcrossConductor on this geometry and does NOT respond — which is exactly why the
        // brief forbids describing the saving as quadratic. Assert the bound rather than a ratio.
        Assert.Equal(2.0, half.GuidedWavelengthM / full.GuidedWavelengthM, 6);
        Assert.True(half.UnknownCount > full.UnknownCount / 4,
            $"N must not fall as the square: {full.UnknownCount} -> {half.UnknownCount}");
    }

    [Fact]
    public void OnANarrowConductor_LoweringIt_CanRAISETheUnknownCount()
    {
        // THE FINDING, and it is the opposite of what the control is for. The saving is not merely
        // sub-quadratic — on some geometry it is NEGATIVE.
        //
        // The outermost edge cell is EdgeFractionOfReference × the conductor WIDTH (R-msh-5); the
        // bulk cell is λ_g/N. Coarsening the λ cap widens the gap the graded fan has to bridge
        // between the two, and past some point the fan costs more cells than the bulk saves. On the
        // 72 µm GaAs line the axial pitch also stops responding entirely — MinCellsAcrossConductor
        // caps it at a quarter of the conductor's own run — so the fan's growth is all that is left.
        //
        // Measured, 2 mm × 72 µm on 100 µm GaAs, sweep top 20 GHz: N = 773 / 705 / 2,014 at mesh
        // frequencies of 20 / 10 / 5 GHz. This is why M0's own accuracy benchmark reports N per row
        // rather than assuming it fell, and why the panel must show the unknown count rather than
        // letting a user assume a lower mesh frequency is always cheaper.
        var p = PlanarLineFixtures.GaAsLine(2e-3, 20e9);

        int atTop  = SurfaceMesher.Mesh(p, PlanarMeshSettings.Default).UnknownCount;
        int atHalf = SurfaceMesher.Mesh(p, PlanarMeshSettings.Default with { MeshFrequencyHz = 10e9 })
                                  .UnknownCount;
        int atQtr  = SurfaceMesher.Mesh(p, PlanarMeshSettings.Default with { MeshFrequencyHz = 5e9 })
                                  .UnknownCount;

        Assert.True(atHalf < atTop, $"halving still saves here: {atTop} -> {atHalf}");
        Assert.True(atQtr > atTop,
            $"quartering must be measured to COST here, not saved: {atTop} -> {atQtr}");
    }

    [Fact]
    public void ItIsCellsPerWavelength_REPARAMETERISED_OnlyTheProductMatters()
    {
        // THE STRUCTURAL FACT, and it is not in the brief anywhere.
        //
        // The cap is  cellSize = λ_g(f_mesh) / N = c / (f_mesh · √ε · N),  so it depends on the
        // PRODUCT f_mesh × CellsPerWavelength and on nothing else. M0 therefore produces no mesh
        // CellsPerWavelength could not already produce — halving the mesh frequency and halving
        // cells/λ are the same act.
        //
        // What M0 adds is the parameterisation a user can reason about, and the report that goes
        // with it: cells/λ alone cannot say WHERE in the band the resolution was spent, because λ
        // itself moves across the band. "Sized at 5 GHz, so λ_g/5 at the 20 GHz top" can.
        //
        // Asserted as bit-identical GRIDS, not merely equal N — two different meshes can share an
        // unknown count.
        var p = Fr4Hero(20e9);

        var a = SurfaceMesher.Mesh(p, PlanarMeshSettings.Default with
        {
            Auto = false, CellsPerWavelength = 5, MeshFrequencyHz = null,   // λ_g(20 GHz)/5
        });
        var b = SurfaceMesher.Mesh(p, PlanarMeshSettings.Default with
        {
            Auto = false, CellsPerWavelength = 20, MeshFrequencyHz = 5e9,   // λ_g(5 GHz)/20
        });

        Assert.Equal(a.MaxCellSizeM, b.MaxCellSizeM);
        Assert.Equal(a.UnknownCount, b.UnknownCount);
        Assert.Equal(a.CellCount,    b.CellCount);
        Assert.Equal(a.Mesh.GridX,   b.Mesh.GridX);
        Assert.Equal(a.Mesh.GridY,   b.Mesh.GridY);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-emp-5 — it survives Resolved's Auto collapse
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ItSurvivesAuto_LikeBoundaryCells_AndForTheSameReason()
    {
        // Auto decides cells/λ and edge cells — a RESOLUTION. Which frequency that resolution is
        // applied at is a different question, and Auto has no opinion about it. A fixture that sets
        // this and leaves Auto on must not silently mesh at the sweep's top instead.
        var s = new PlanarMeshSettings(Auto: true, MeshFrequencyHz: 5e9);
        Assert.Equal(5e9, s.Resolved.MeshFrequencyHz);

        var p = Fr4Hero(20e9);
        Assert.Equal(SurfaceMesher.Mesh(p, s).UnknownCount,
                     SurfaceMesher.Mesh(p, new PlanarMeshSettings(Auto: false, MeshFrequencyHz: 5e9))
                                  .UnknownCount);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 4 — R-emp-2: the report and BOTH notes
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TheReport_NamesTheMeshFrequency_NotTheSweepTop()
    {
        var r = SurfaceMesher.Mesh(Fr4Hero(20e9),
                                   PlanarMeshSettings.Default with { MeshFrequencyHz = 10e9 });

        Assert.Equal(10e9, r.FrequencyHz);

        // …and the λ_g note quotes the same number rather than the sweep's own top.
        string lambdaNote = Assert.Single(r.Notes, n => n.Contains("Cell size capped", StringComparison.Ordinal));
        Assert.Contains("the frequency the mesh is sized at", lambdaNote, StringComparison.Ordinal);
        Assert.DoesNotContain("highest frequency of the sweep", lambdaNote, StringComparison.Ordinal);

        // …and with the control unset it still reads as the sweep's own top, unchanged.
        var unset = SurfaceMesher.Mesh(Fr4Hero(20e9), PlanarMeshSettings.Default);
        Assert.Contains("highest frequency of the sweep",
                        Assert.Single(unset.Notes, n => n.Contains("Cell size capped", StringComparison.Ordinal)),
                        StringComparison.Ordinal);
    }

    [Fact]
    public void TheUnderResolutionNote_FiresBelowTheSweepTop_AndNotAtOrAboveIt()
    {
        var p = Fr4Hero(20e9);

        var below = SurfaceMesher.Mesh(p, PlanarMeshSettings.Default with { MeshFrequencyHz = 10e9 });
        string note = Assert.Single(below.Notes, n => n.Contains("was sized at", StringComparison.Ordinal));

        // The trade is stated in EFFECTIVE cells/λ at the sweep's top — a physical quantity — not in
        // hertz. Default is 20 cells/λ; at half the mesh frequency the sweep's top sees 10.
        Assert.Contains("λ_g/10", note, StringComparison.Ordinal);
        Assert.Contains("λ_g/20", note, StringComparison.Ordinal);

        foreach (var s in new[]
                 {
                     PlanarMeshSettings.Default,                                     // unset
                     PlanarMeshSettings.Default with { MeshFrequencyHz = 20e9 },     // exactly the top
                     PlanarMeshSettings.Default with { MeshFrequencyHz = 40e9 },     // above it
                 })
        {
            var r = SurfaceMesher.Mesh(p, s);
            Assert.DoesNotContain(r.Notes, n => n.Contains("was sized at", StringComparison.Ordinal));
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-emp-3 — MaxFrequencyHz still means THE SWEEP'S TOP, and M0 cannot widen a physics refusal
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void NoRefusalCanEverSeeTheMeshFrequency_BecauseNoneOfThemTakesMeshSettings()
    {
        // A via basis is one z-rooftop per gap, so its electrical bound is about the SWEEP's top
        // frequency and has nothing to do with the mesh. If a refusal could read the mesh frequency,
        // a user could silently widen a PHYSICS limit by turning down a PERFORMANCE knob.
        //
        // The guarantee is structural rather than numeric: CanSolve is handed a PlanarProblem and
        // nothing else, and MeshFrequencyHz lives on PlanarMeshSettings, which it never sees. Assert
        // that shape directly — a numeric test could only ever sample one geometry, while this
        // cannot be defeated by any future refusal added inside CanSolve.
        var canSolve = typeof(PlanarKernel).GetMethod(nameof(PlanarKernel.CanSolve));
        Assert.NotNull(canSolve);
        Assert.DoesNotContain(canSolve!.GetParameters(),
                              pi => pi.ParameterType == typeof(PlanarMeshSettings));

        // And the one place a mesh setting DOES reach a verdict — the R17 unknown budget — is a
        // budget, not a physics limit: lowering the mesh frequency may only ever relax it, which is
        // the entire point of the control.
        var p = Fr4Hero(20e9);
        Assert.True(SurfaceMesher.Mesh(p, PlanarMeshSettings.Default with { MeshFrequencyHz = 10e9 })
                                 .UnknownCount
                    <= SurfaceMesher.Mesh(p, PlanarMeshSettings.Default).UnknownCount);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Owner report, 2026-09-09 — a cap that did not bind must not be reported as one
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void WhereTheCapDoesNotBind_TheNoteSaysSo_AndNamesTheKnobsAsInert()
    {
        // The reported shape: a 360 µm run on 1.6 mm FR-4, meshed 4 cells across at 90 µm, against a
        // λ_g/5 cap of 2.86 mm at 10 GHz. The cap is ~32× coarser, so it never enters Math.Min and the
        // mesh frequency is inert — which is the user-visible symptom.
        var p = PlanarLineFixtures.Line(GroundedSlab.Fr4Starter, 360e-6, 3e-3, 10e9);
        var s = new PlanarMeshSettings(Auto: false, CellsPerWavelength: 5);

        var low  = SurfaceMesher.Mesh(p, s with { MeshFrequencyHz = 1e9 });
        var high = SurfaceMesher.Mesh(p, s with { MeshFrequencyHz = 10e9 });
        Assert.Equal(high.UnknownCount, low.UnknownCount);   // the complaint, reproduced

        // So the note must not claim a cap, and must name what actually set the pitch.
        Assert.DoesNotContain(low.Notes, n => n.Contains("Cell size capped", StringComparison.Ordinal));
        string note = Assert.Single(low.Notes, n => n.Contains("did NOT set the cell size", StringComparison.Ordinal));
        Assert.Contains("narrowest conductor run", note, StringComparison.Ordinal);
        Assert.Contains("WILL NOT CHANGE THIS MESH", note, StringComparison.Ordinal);

        // …and it names the control that IS live, because "your artwork is the problem" is accurate
        // and useless. On the reported file the edge mesh is 42% of the unknowns.
        Assert.Contains("turn the edge mesh off", note, StringComparison.Ordinal);
        Assert.DoesNotContain("turn the edge mesh off",
                              Assert.Single(SurfaceMesher.Mesh(p, s with { MeshFrequencyHz = 1e9, EdgeMesh = false })
                                                         .Notes,
                                            n => n.Contains("did NOT set the cell size", StringComparison.Ordinal)),
                              StringComparison.Ordinal);

        // …and where the cap DOES bind the wording is untouched: same geometry, same settings, at a
        // mesh frequency high enough that λ_g/5 is the smaller of the two.
        var bound = SurfaceMesher.Mesh(p, s with { MeshFrequencyHz = 4e12 });
        Assert.Contains(bound.Notes, n => n.Contains("Cell size capped", StringComparison.Ordinal));
        Assert.DoesNotContain(bound.Notes, n => n.Contains("did NOT set the cell size", StringComparison.Ordinal));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Cells across the narrowest conductor — a CONTROL since 2026-09-09, and 1 is reachable
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CellsAcrossTheConductor_SetsTheTransversePitch_AndIsNeverClampedOrRefused()
    {
        // 360 µm of metal, well inside the λ_g cap, so this setting alone decides the pitch.
        var p = PlanarLineFixtures.Line(GroundedSlab.Fr4Starter, 360e-6, 3e-3, 10e9);
        var s = new PlanarMeshSettings(Auto: false, CellsPerWavelength: 5);

        var four = SurfaceMesher.Mesh(p, s with { MinCellsAcrossConductor = 4 });
        var one  = SurfaceMesher.Mesh(p, s with { MinCellsAcrossConductor = 1 });

        // What the setting controls is the PITCH, and that is what is asserted. It is deliberately
        // NOT asserted that the cell count falls — it does not always, which is the whole reason the
        // note below exists: with the edge mesh on, this fixture goes 10 grid lines to 11 when the
        // pitch is coarsened 4×, because the edge fan is sized from the conductor width and now has
        // further to bridge. Asserting monotonicity here would pin a behaviour the mesher does not
        // have (and the first version of this test did exactly that, and failed).
        // Auto:false already, or `EdgeMesh = false` would be collapsed away by Resolved.
        var bare  = s with { EdgeMesh = false };
        var bare4 = SurfaceMesher.Mesh(p, bare with { MinCellsAcrossConductor = 4 });
        var bare1 = SurfaceMesher.Mesh(p, bare with { MinCellsAcrossConductor = 1 });
        Assert.True(bare1.Mesh.GridY.Count < bare4.Mesh.GridY.Count,
            $"with no edge fan, 1 across must coarsen the transverse grid: " +
            $"{bare4.Mesh.GridY.Count} -> {bare1.Mesh.GridY.Count}");

        // Owner instruction: mesh density is the user's. A bad mesh is a NOTE, never a refusal and
        // never a clamp — so the report still solves and still reports the number that was asked for.
        Assert.NotEqual(PlanarBudgetVerdict.Refused, one.Verdict);
        Assert.Contains(one.Notes, n => n.Contains("At 1 cell across", StringComparison.Ordinal));
        Assert.Contains(one.Notes, n => n.Contains("Cells across the narrowest conductor is set to 1",
                                                   StringComparison.Ordinal));

        // …and the default says nothing at all, so an untouched setup's report is unchanged.
        Assert.DoesNotContain(four.Notes, n => n.Contains("Cells across the narrowest conductor is set to",
                                                          StringComparison.Ordinal));

        // Below 1 is clamped by Resolved rather than throwing — it is not a value the UI can produce.
        Assert.Equal(1, (s with { MinCellsAcrossConductor = 0 }).Resolved.MinCellsAcrossConductor);

        // It survives Auto: a number the user typed must not be discarded by a checkbox.
        Assert.Equal(2, (new PlanarMeshSettings(Auto: true, MinCellsAcrossConductor: 2))
                        .Resolved.MinCellsAcrossConductor);
    }
}
