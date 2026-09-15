// EFAN — the edge cell is capped against the density the user asked for.
//
// Owner report, 2026-09-15: the default mesh on the shipped MMIC coil has far too many cells, the
// edge mesh has to be switched off to get the count down, and turning Cells across a conductor down
// barely helps. The third complaint is the one this phase is about and it was a real defect:
// `c0 = EdgeFractionOfReference * width` is a fraction of the METAL while the bulk pitch is that
// same metal DIVIDED BY MinCellsAcrossConductor, so the climb the graded fan has to make is
// 1/(0.03 * MinCellsAcross) — 8.3 at the default and 33.3 at 1 across. The user's own density
// control made the fan LONGER the coarser they asked for.
//
// `PlanarMeshSettings.MaxEdgeRefinement` caps that climb. Nothing here solves anything: every
// property is a property of the mesh, same rule as MeshGradingTests and LocalEdgeReferenceTests and
// for the same reason. The ACCURACY half is a measurement rather than a test — the sweep against
// kernel A's own cross-section extraction is in RESOLVED.md §EFAN and at the constant itself.

using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

public class EdgeRefinementFloorTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _out = output;

    /// <summary>The field as it was before the floor existed — what "no finer, and no more of them"
    /// is measured against.</summary>
    private const double NoFloor = double.PositiveInfinity;

    private static PlanarMeshSettings At(int across, int cellsPerWavelength = 20) =>
        new(Auto: false, CellsPerWavelength: cellsPerWavelength, MinCellsAcrossConductor: across);

    private static IEnumerable<(string Name, PlanarProblem Problem)> Fixtures()
    {
        yield return ("FR-4 line 200 um x 10 mm",
                      PlanarLineFixtures.Line(GroundedSlab.Fr4Starter, 200e-6, 10e-3, 10e9));
        yield return ("FR-4 hero 2.9 x 20 mm",
                      PlanarLineFixtures.Fr4Line(20e-3, 10e9));
        yield return ("GaAs hero 72 um x 2 mm",
                      PlanarLineFixtures.GaAsLine(2e-3, 20e9));
        yield return ("FR-4 taper 2.9 mm -> 200 um",
                      PlanarLineFixtures.Taper(GroundedSlab.Fr4Starter, 2.9e-3, 200e-6, 10e-3, 10e9));
        yield return ("MMIC coil, 3 turns",  MmicCoilFixture.Coil());
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 1 — THE INVARIANT: it may only coarsen. This is the whole reason the floor is safe.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The floor raises c0 and leaves the grading rate at the one derived from the UNFLOORED c0, so
    /// <c>h(x) = min_i[c0_i + g_i·|x − a_i|]</c> is pointwise ≥ what it was: the marcher takes steps
    /// that are never shorter, from the same start, so it reaches the end of every interval in no
    /// more of them. <b>Under the per-axis rule — which is the shipped default — that carries all
    /// the way to the cell and unknown counts and this gate is EXACT</b>, measured over every
    /// fixture, three values of Cells per wavelength and every Cells across from 1 to 8.
    ///
    /// <para>Re-deriving the rate against the raised c0 breaks exactly this and is the edit this
    /// test exists to catch: a shallower ramp reaches the bulk further out, so the field would be
    /// FINER than today's over the band between the two fans' ends.</para>
    ///
    /// <para><b>UNDER A PITCH FIELD IT IS NOT EXACT, AND THAT IS A PROPERTY OF THE ENFORCEMENT PASS
    /// RATHER THAN OF THE FLOOR.</b> <c>BuildGridLines</c> finishes by splitting any cell longer
    /// than the cap, and with a field it reads that cap at the cell's own MIDPOINT — the only place
    /// a single number can honestly describe a cell. Midpoint sampling of a varying field is not
    /// monotone in the partition: a coarser cell whose midpoint lands in a FINER part of the field
    /// is split into more pieces than the two finer cells it replaced were, because those two had
    /// their midpoints in coarser parts and were let through. The coarser partition is the one being
    /// corrected more, not the one being meshed worse. <b>Measured worst case over the same sweep:
    /// +1 gridline, +3.3% cells and +3.4% unknowns, on the FR-4 taper with the sheet mesh at
    /// cells/λ = 5 and 3 across; the transmission-line mesh's worst is +1.2% unknowns with the
    /// gridline count still exactly monotone.</b> Everything else on that sweep falls, by up to
    /// 2.2×. The detail floor — the other one-way relaxation of this same field — IS exactly
    /// monotone under a field, because it coarsens the CAP as well as the fan, so this residue is
    /// specific to a change that moves the partition while leaving the cap where it was.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Benchmark")]   // 29 s measured: a pitch FIELD is built per mesh, and there
                                       // are 720 of them. The EXACT half of this invariant — the
                                       // per-axis rule, which is what every shipped run takes — is
                                       // the test below, it costs half a second, and it stays in the
                                       // routine gate. This one measures the field path's residue.
    public void TheFloor_NeverProducesAFinerCell_NorMoreOfThem_OnAnyFixture()
    {
        // The margin the field path's own enforcement pass costs — see the note above. It is NOT a
        // tuning knob: the per-axis rule is asserted exactly, three lines down.
        const double FieldPathMargin = 1.05;

        // The per-axis rule is swept by the EXACT gate below; this one is the two pitch-FIELD modes,
        // which is where the residue lives and where the cost is.
        foreach (var (name, problem) in Fixtures())
            foreach (var model in new[] { PlanarCurrentModel.Sheet, PlanarCurrentModel.TransmissionLine })
                foreach (int cpw in new[] { 5, 20, 40 })
                    for (int across = 1; across <= 8; across++)
                    {
                        var s = At(across, cpw) with { CurrentModel = model };
                        var before = SurfaceMesher.Mesh(problem, s, PlanarEdgeReference.LocalConductorWidth,
                                                        edgeRefinementCap: NoFloor);
                        var after  = SurfaceMesher.Mesh(problem, s, PlanarEdgeReference.LocalConductorWidth);

                        const double margin = FieldPathMargin;
                        string at = $"{name} {model} cells/λ={cpw} across={across}";

                        Assert.True(after.CellCount <= before.CellCount * margin,
                            $"{at}: floored {after.CellCount} cells > unfloored {before.CellCount}");
                        Assert.True(after.UnknownCount <= before.UnknownCount * margin,
                            $"{at}: floored {after.UnknownCount} unknowns > unfloored {before.UnknownCount}");

                        // Same 10% allowance, for the same reason, as LocalEdgeReferenceTests' own:
                        // PartitionGraded rescales each interval onto its endpoints, so a pointwise
                        // COARSER field does not give pointwise coarser CELLS — it can leave a
                        // slightly shorter remainder cell against a hard gridline. Anything past
                        // that is a skipped floor, not a rescale remainder.
                        Assert.True(after.MinCellEdgeM >= before.MinCellEdgeM * 0.9,
                            $"{at}: floored finest cell {after.MinCellEdgeM:E3} is more than 10% " +
                            $"below unfloored {before.MinCellEdgeM:E3}");
                    }
    }

    /// <summary>
    /// <b>Under the per-axis rule the invariant is EXACT, and that is the half worth asserting on
    /// its own</b> — it is what every shipped run takes, and a margin large enough to cover the
    /// field path would hide a real regression here. Separated from the sweep above so a failure
    /// says which of the two properties broke.
    /// </summary>
    [Fact]
    public void UnderThePerAxisRule_TheFlooredCountIsBoundedAboveExactly()
    {
        foreach (var (name, problem) in Fixtures())
            foreach (int cpw in new[] { 5, 20, 40 })
                for (int across = 1; across <= 8; across++)
                {
                    var s = At(across, cpw);
                    var before = SurfaceMesher.Mesh(problem, s, PlanarEdgeReference.LocalConductorWidth,
                                                    edgeRefinementCap: NoFloor);
                    var after  = SurfaceMesher.Mesh(problem, s, PlanarEdgeReference.LocalConductorWidth);
                    Assert.True(after.CellCount <= before.CellCount,
                        $"{name} cells/λ={cpw} across={across}: {before.CellCount} -> {after.CellCount} cells");
                    Assert.True(after.UnknownCount <= before.UnknownCount,
                        $"{name} cells/λ={cpw} across={across}: {before.UnknownCount} -> {after.UnknownCount} unknowns");
                    Assert.True(after.Mesh.GridX.Count <= before.Mesh.GridX.Count
                             && after.Mesh.GridY.Count <= before.Mesh.GridY.Count,
                        $"{name} cells/λ={cpw} across={across}: the gridline count rose");
                }
    }

    /// <summary>
    /// <b>At the shipped default the floor does nothing, to the bit.</b> It binds only where
    /// <c>1/(MinCellsAcross · MaxEdgeRefinement)</c> exceeds
    /// <see cref="PlanarMeshSettings.EdgeFractionOfReference"/>, which at 10 and 3% is 3 cells
    /// across and below — so every measured number in this directory's <c>HISTORY.md</c>, all taken
    /// at 4, is untouched. A change that moves this is a change to the whole recorded acceptance
    /// set and has to be argued for rather than noticed later.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void AtTheShippedDefaultAndAbove_TheFloorChangesNothingBitForBit(int across)
    {
        foreach (var (name, problem) in Fixtures())
        {
            var s = At(across);
            var before = SurfaceMesher.Mesh(problem, s, PlanarEdgeReference.LocalConductorWidth,
                                            edgeRefinementCap: NoFloor);
            var after  = SurfaceMesher.Mesh(problem, s, PlanarEdgeReference.LocalConductorWidth);

            Assert.Equal(before.UnknownCount, after.UnknownCount);
            Assert.Equal(before.Mesh.GridX, after.Mesh.GridX);
            Assert.Equal(before.Mesh.GridY, after.Mesh.GridY);
            _out.WriteLine($"{name} across={across}: {after.UnknownCount} unknowns, unchanged");
        }
    }

    /// <summary>
    /// Translation invariance is the property the continuous size field was chosen for, and a floor
    /// that read anything positional would lose it silently. Same 3.7 mm, same reason, as
    /// <c>MeshGradingTests</c> and <c>LocalEdgeReferenceTests</c>.
    /// </summary>
    [Fact]
    public void MovingTheArtwork3Point7mm_LeavesTheFlooredMeshUnchanged()
    {
        const double d = 3.7e-3;
        var at0 = PlanarLineFixtures.Line(GroundedSlab.Fr4Starter, 200e-6, 10e-3, 10e9);
        var moved = PlanarLineFixtures.Problem(GroundedSlab.Fr4Starter, 10e9,
            PlanarLineFixtures.Rect(d, d - 100e-6, d + 10e-3, d + 100e-6));

        foreach (int across in new[] { 1, 2, 3 })
        {
            var a = SurfaceMesher.Mesh(at0,   At(across), PlanarEdgeReference.LocalConductorWidth);
            var b = SurfaceMesher.Mesh(moved, At(across), PlanarEdgeReference.LocalConductorWidth);
            Assert.Equal(a.CellCount, b.CellCount);
            Assert.Equal(a.Mesh.GridX.Count, b.Mesh.GridX.Count);
            for (int i = 0; i < a.Mesh.GridX.Count; i++)
                Assert.Equal(a.Mesh.GridX[i], b.Mesh.GridX[i] - d, 15);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 2 — the control it exists for actually works now
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The defect, as a number.</b> On the reported coil, taking Cells across a conductor from 4
    /// to 1 bought <b>2.2×</b> with the edge mesh on and <b>28×</b> with it off — a control 13×
    /// weaker than it reads, with nothing anywhere saying so. After the floor the edge-meshed run
    /// buys <b>4.5×</b>, and the gap between the two is 6.3× rather than 12.7×.
    ///
    /// <para><b>The residual gap is structural and is not a target.</b> What is left is the fan's
    /// LENGTH IN CELLS — two or three gridlines per attractor, times 28 rims, across the whole
    /// tensor grid — and that does not scale with the bulk pitch at all. No value of
    /// <see cref="PlanarMeshSettings.MaxEdgeRefinement"/> closes it, because it is a count and this
    /// constant governs a size. <c>EdgeCells</c> is the control over the count, and it is a FLOOR on
    /// the fan's length rather than a cap on it.</para>
    /// </summary>
    [Fact]
    public void LoweringCellsAcross_NowActsOnAnEdgeMeshedMeshAndNotOnlyOnABareOne()
    {
        var coil = MmicCoilFixture.Coil();

        int OnAt(int across, double cap) =>
            SurfaceMesher.Mesh(coil, At(across), PlanarEdgeReference.LocalConductorWidth,
                               edgeRefinementCap: cap).UnknownCount;
        int OffAt(int across) =>
            SurfaceMesher.Mesh(coil, At(across) with { EdgeMesh = false },
                               PlanarEdgeReference.LocalConductorWidth).UnknownCount;

        double beforeGain = (double)OnAt(4, NoFloor) / OnAt(1, NoFloor);
        double afterGain  = (double)OnAt(4, double.NaN) / OnAt(1, double.NaN);
        double offGain    = (double)OffAt(4) / OffAt(1);

        _out.WriteLine($"Cells across 4 -> 1 on the coil: edge mesh ON {beforeGain:F1}x before, " +
                       $"{afterGain:F1}x after; edge mesh OFF {offGain:F1}x");

        // The control is genuinely stronger than it was, not merely different.
        Assert.True(afterGain > beforeGain * 1.5,
            $"the floor barely strengthened Cells across: {beforeGain:F2}x -> {afterGain:F2}x");

        // …and the stated factor between the edge-meshed control and the bare one. It was 12.7x.
        Assert.True(offGain / afterGain < 8.0,
            $"Cells across is still {offGain / afterGain:F1}x weaker with the edge mesh on than " +
            "without it — the floor did not act, or the fan's cell COUNT has grown");
    }

    /// <summary>
    /// The one-way property said the other way round, on the part it matters on: at every setting
    /// below the default the floored mesh is strictly smaller, and at the default it is identical.
    /// A gate nothing can fail is not a gate.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OnTheReportedCoil_TheFloorRemovesRealUnknowns(int across)
    {
        var coil = MmicCoilFixture.Coil();
        var before = SurfaceMesher.Mesh(coil, At(across), PlanarEdgeReference.LocalConductorWidth,
                                        edgeRefinementCap: NoFloor);
        var after  = SurfaceMesher.Mesh(coil, At(across), PlanarEdgeReference.LocalConductorWidth);

        _out.WriteLine($"across={across}: {before.UnknownCount} -> {after.UnknownCount} unknowns " +
                       $"({(double)before.UnknownCount / after.UnknownCount:F2}x)");
        Assert.True(after.UnknownCount < before.UnknownCount * 0.9,
            $"across={across}: {before.UnknownCount} -> {after.UnknownCount} is not a real saving");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 3 — the user is told, because the edge cell is the one size they cannot see or set
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void WhenTheFloorBinds_TheNotesNameTheControlThatSetIt_AndQuoteTheFractionRealised()
    {
        var r = SurfaceMesher.Mesh(MmicCoilFixture.Coil(), At(2),
                                   PlanarEdgeReference.LocalConductorWidth);

        string held = Assert.Single(r.Notes, n => n.Contains("The edge cell is held at", StringComparison.Ordinal));
        _out.WriteLine(held);
        Assert.Contains("Cells across a conductor is 2", held, StringComparison.Ordinal);
        Assert.Contains("5%", held, StringComparison.Ordinal);   // 1/(2 x 10), not the 3% asked for

        // …and the finest-cell note quotes the fraction that was REALISED rather than the literal
        // 3%. A coarsened edge cell reported beside a "3%" is the same class of statement as a cap
        // that did not bind being reported as one.
        string finest = Assert.Single(r.Notes, n => n.StartsWith("Edge mesh on:", StringComparison.Ordinal));
        _out.WriteLine(finest);
        Assert.Contains("5%", finest, StringComparison.Ordinal);
        Assert.DoesNotContain("(3%", finest, StringComparison.Ordinal);
    }

    [Fact]
    public void WhenTheFloorDoesNotBind_ThereIsNoSuchNote_AndTheFractionIsStillThreePercent()
    {
        var r = SurfaceMesher.Mesh(MmicCoilFixture.Coil(), At(4),
                                   PlanarEdgeReference.LocalConductorWidth);

        Assert.DoesNotContain(r.Notes, n => n.Contains("The edge cell is held at", StringComparison.Ordinal));
        string finest = Assert.Single(r.Notes, n => n.StartsWith("Edge mesh on:", StringComparison.Ordinal));
        Assert.Contains("3%", finest, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>M3 — the refusal offers the control it has been describing all along.</b> It already said
    /// "meshing it 4 cells across forces a 2.5 µm pitch", which reads as a property of the mesher; it
    /// is a number the user typed and it is the largest lever on a part like this. Memory
    /// <c>em-refusal-must-name-a-binding-remedy</c>: a remedy list that omits the binding constraint
    /// sends the user round the inert ones, and this one named four remedies of which two were the
    /// wavelength knobs it had just said in capitals were dead.
    /// </summary>
    [Fact]
    public void TheRefusalNamesCellsAcrossAsASetting_AndSaysHowManyTheFanActuallyPuts()
    {
        var r = SurfaceMesher.Mesh(MmicCoilFixture.Coil(), At(4),
                                   PlanarEdgeReference.LocalConductorWidth);
        Assert.Equal(PlanarBudgetVerdict.Refused, r.Verdict);
        string refusal = r.Refusal!;
        _out.WriteLine(refusal);

        Assert.Contains("Cells across a conductor", refusal, StringComparison.Ordinal);
        Assert.Contains("is a SETTING", refusal, StringComparison.Ordinal);
        Assert.Contains("lower Cells across a conductor", refusal, StringComparison.Ordinal);

        // The second number, which only the mesher knows: the fan puts MORE cells across the metal
        // than were asked for, so the number the user typed is not the number they got. It comes off
        // the mesh's own R-msh-4 metric — no second mesh, no estimate.
        Assert.Contains($"{r.CellsAcrossNarrowestConductor} cells across that metal", refusal,
                        StringComparison.Ordinal);
        Assert.True(r.CellsAcrossNarrowestConductor > 4);
    }

    /// <summary>
    /// The same sentence on a mesh that SOLVES. The refusal fires only past the ceiling, so without
    /// this a user whose mesh is merely expensive is still told which two knobs are dead and never
    /// which one is alive — which is the defect BuildRefusal was already rewritten for once, in a
    /// note instead of a refusal.
    /// </summary>
    [Fact]
    public void TheSolvingMeshsOwnNote_NamesTheSameControl()
    {
        // A line whose metal is narrower than a λ cell in BOTH axes — so the λ knobs are genuinely
        // inert and this is the branch that says so — and nowhere near the ceiling. A 2 mm line is
        // NOT that fixture: λ_g/20 is 209 µm against its own 500 µm along pitch, so the cap binds
        // along it and the note takes the other branch entirely.
        var r = SurfaceMesher.Mesh(PlanarLineFixtures.GaAsLine(200e-6, 20e9), At(4),
                                   PlanarEdgeReference.LocalConductorWidth);
        Assert.NotEqual(PlanarBudgetVerdict.Refused, r.Verdict);

        string note = Assert.Single(r.Notes, n => n.Contains("do NOT set this mesh", StringComparison.Ordinal));
        _out.WriteLine(note);
        Assert.Contains("lower Cells across a conductor", note, StringComparison.Ordinal);
    }

    [Fact]
    public void AtOneCellAcross_TheNoteDoesNotOfferToLowerItFurther()
    {
        // There is nowhere below 1, and a remedy that cannot be taken is the failure this whole
        // thread is about, one control over.
        var r = SurfaceMesher.Mesh(PlanarLineFixtures.GaAsLine(200e-6, 20e9), At(1),
                                   PlanarEdgeReference.LocalConductorWidth);
        foreach (var n in r.Notes)
            Assert.DoesNotContain("lower Cells across a conductor", n, StringComparison.Ordinal);
    }
}
