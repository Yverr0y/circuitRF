// brief-em-mim-3-thin-layer-gate.md and brief-em-mim-8-cross-level-quadrature.md — two meshed
// conductor levels 0.05-0.5 um apart is a regime nothing had measured, LayerStack.CanRepresent
// accepts any positive thickness, and nothing refused the structure either.
//
// The three ladders are in src/Engine/Mom/HISTORY.md and the verdicts in RESOLVED.md. What is here
// gates the tier that proved fragile — the CROSS-LEVEL block of the multi-level fill, whose error
// against forced-high quadrature grew four decades between cell/separation 1 and 20 while the
// SAME-level block barely moved.
//
// MIM-8 FIXED IT, AND THAT IS WHY EVERY GATE BELOW IS NOW A PAIR. The peak a cross-level entry
// carries is a FITTED IMAGE whose depth is of the order of the film thickness; MIM-8 subtracts its
// static part and integrates that in closed form (ShallowImageCore). PlanarFillSettings'
// ShallowImageCells = 0 is the pre-MIM-8 arithmetic, reproduced BIT FOR BIT — T1 pins both, and the
// off-row literals are MIM-3's own, unchanged. A gate that only pinned the new numbers would leave
// nothing to say what was fixed.
//
// WHY THIS SHAPE. Reciprocity holds to 1e-19 and passivity to 1e-5 the whole way up the ladder, so
// no self-consistency check can see this: it is L8c's converged-looking-but-wrong mode, one tier
// down in z. Only a comparison against a better quadrature can, and that comparison is expensive
// (19 s and 74 s in a Debug test run), so it carries Category=Benchmark and the ROUTINE gate is a
// fixed-input matrix-entry comparison against literals — P3/P4's own pattern. The one routine gate
// that is NOT a matrix entry is T8, the capacitance, because a magnitude with the wrong sign is
// what this cost a user and a digest cannot say so.
//
// WHERE THE LITERALS CAME FROM. Printed by this file's own fixtures on the tree that landed each
// brief, against PlanarFillSettings.Default. They assert nothing about accuracy — that is HISTORY's
// job and T7's — but they hold the cross-level path still, which is the thing whose silent movement
// would invalidate the verdict.

using System.Numerics;
using System.Security.Cryptography;
using CircuitRF.Engine.Mom;
using NumFlat;
using Xunit;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

public sealed class MimThinLayerTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _out = output;

    private const double Zlow = 103e-6;   // the shipped MIM technology's lower plate
    private const double Ztop = 106e-6;   // its upper interconnect level

    /// <summary>GaAs 103 um | capacitor dielectric d | air to 106 um, on a ground plane. Only d
    /// moves, so a ladder over it holds the artwork, the mesh and the unknown count fixed.</summary>
    private static LayerStack MimStack(double d) => new(
        Termination.Pec,
        [
            new MediumLayer(Zlow, new EmMaterial(12.90, 0.0006)),
            new MediumLayer(d,    new EmMaterial( 6.80, 0.0010)),
            new MediumLayer(Ztop - Zlow - d, new EmMaterial(1.0, 1e-6)),
        ],
        Termination.Air);

    private static PlanarPolygon Square(double w) =>
        new([new EmPoint(0, 0), new EmPoint(w, 0), new EmPoint(w, w), new EmPoint(0, w)]);

    /// <summary>Two coincident square plates straddling the dielectric, always four cells to a
    /// side, so <paramref name="cellOverD"/> is the only thing that changes between rungs.</summary>
    private static PlanarProblem Plates(double d, double cellOverD)
    {
        double w = 4 * cellOverD * d;
        var stack = MimStack(d);
        return new PlanarProblem(
            [
                new PlanarConductorLayer("bottom plate", [Square(w)], 4.1e7, 0.25e-6, Zlow),
                new PlanarConductorLayer("top plate",    [Square(w)], 4.1e7, 0.25e-6, Zlow + d),
            ],
            GroundedSlab.GaAsStarter, 10e9, null, stack, null);
    }

    private static readonly PlanarMeshSettings Uniform =
        new(Auto: false, CellsPerWavelength: 20, EdgeMesh: false, EdgeCells: 3);

    /// <summary>
    /// A modest step above the shipped rule — enough to separate the two regimes by three decades.
    /// HISTORY's own ladder uses a heavier one and shows that stepping the reference AGAIN moves it
    /// by 1e-14, i.e. that the reference is converged rather than merely different.
    /// </summary>
    private static readonly PlanarFillSettings Reference = PlanarFillSettings.Default with
    {
        SelfPanels = 8, TouchPanels = 6,
        NearNodes = 16, MidNodes = 12, FarNodes = 8,
        NearRatio = 8.0, FarRatio = 32.0,
        RemainderNodesNear = 12, RemainderNodesMid = 8, RemainderNodesFar = 6,
        UseRadialTable = false,
    };

    private static Mat<Complex> Fill(PlanarMesh mesh, PlanarProblem p, PlanarFillSettings st)
    {
        var cores  = PlanarFill.BuildCores(mesh, st);
        var greens = new LayeredSpectralGreens(p.EffectiveStack, p.MaxFrequencyHz);
        var set    = new PlanarKernelSet(greens, st.Order).For(cores);
        return PlanarFill.FillMultiLevel(cores, set, PlanarLevels.From(p), 2 * Math.PI * p.MaxFrequencyHz);
    }

    /// <summary>Worst entry-wise difference over the chosen block, scaled by that block's OWN
    /// largest entry — a small block must not be graded on a large block's dynamic range.</summary>
    private static double Block(Mat<Complex> a, Mat<Complex> b, int[] layer, bool cross)
    {
        double worst = 0, scale = 0;
        for (int i = 0; i < a.RowCount; i++)
            for (int j = 0; j < a.ColCount; j++)
            {
                if (cross != (layer[i] != layer[j])) continue;
                scale = Math.Max(scale, b[i, j].Magnitude);
                worst = Math.Max(worst, (a[i, j] - b[i, j]).Magnitude);
            }
        return scale > 0 ? worst / scale : double.NaN;
    }

    private static string DigestCross(Mat<Complex> z, int[] lay)
    {
        var buf = new byte[16];
        using var sha = SHA256.Create();
        for (int i = 0; i < z.RowCount; i++)
            for (int j = 0; j < z.ColCount; j++)
            {
                if (lay[i] == lay[j]) continue;
                BitConverter.TryWriteBytes(buf.AsSpan(0, 8), z[i, j].Real);
                BitConverter.TryWriteBytes(buf.AsSpan(8, 8), z[i, j].Imaginary);
                sha.TransformBlock(buf, 0, 16, null, 0);
            }
        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T1 — the ROUTINE gate: the cross-level block, on a fixed input, against literals
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    // cell/separation, ShallowImageCells, digest of the cross-level block, and one representative
    // entry (row 0, last column) so a failure message says HOW it moved rather than only that a hash
    // changed.
    //
    // THE TWO ROWS AT cell/separation 1 ARE THE BIT-IDENTITY CLAIM: the shallowest image there sits
    // at 0.64 of a cell, outside the 0.5 the treatment acts within, so nothing is subtracted and the
    // digest is the same one. That is the shape of the claim for every pairing whose images the mesh
    // already resolves — not that the subtraction happens and cancels, but that it does not happen.
    //
    // THE ROWS AT 0 ARE MIM-3's OWN LITERALS, NOT RE-TAKEN. They are what the pre-MIM-8 tree
    // printed, and they still come out of this one — which is the strongest available statement
    // that ShallowImageCells = 0 is the old arithmetic rather than an approximation to it.
    [InlineData(1.0,  0.5, "917BBCA5D70341ADB227CADC723A5B6311103FBA48C2418398921DAA1B890D76",
                0.27535759464851667, -318.6400677788218)]
    [InlineData(1.0,  0.0, "917BBCA5D70341ADB227CADC723A5B6311103FBA48C2418398921DAA1B890D76",
                0.27535759464851667, -318.6400677788218)]
    // MIM-12a RE-TOOK THIS ROW, and nothing about the row above it moved. The treatment at
    // cell/separation 20 now subtracts the thin region's EXACT image series rather than the fit's
    // images, and the fit itself is asked only for what is left — so the cross-level block is a
    // different (and, measured against direct Sommerfeld integration, a 4-decade better) set of
    // numbers. The rows at cell/separation 1 and the rows at ShallowImageCells 0 are untouched,
    // which is the whole of the bit-identity claim: the treatment does not happen there.
    [InlineData(20.0, 0.5, "1F223750094BCDD5072B392CED51C8BCFA1E7097C58A4C7574361C3EBA8E8B1B",
                0.0077787711848579955, -13.456455402723147)]
    [InlineData(20.0, 0.0, "12CB6DECE52B3234793855736C205D671717EB9AD48789A8892CA94285549081",
                0.007459157175535663, -13.46907400956937)]
    public void T1_TheCrossLevelBlockIsHeldStill_OnAFixedInput(
        double cellOverD, double shallowCells, string digest, double lastReal, double lastImag)
    {
        var p    = Plates(0.2e-6, cellOverD);
        var mesh = SurfaceMesher.Mesh(p, Uniform).Mesh;
        var lay  = mesh.Bases.Select(b => b.LayerIndex).ToArray();
        var z    = Fill(mesh, p, PlanarFillSettings.Default with { ShallowImageCells = shallowCells });

        Assert.Equal(48, mesh.Bases.Count);
        Assert.Equal(32, mesh.Cells.Count);

        var probe = z[0, z.ColCount - 1];
        _out.WriteLine($"cell/separation {cellOverD:G3}, ShallowImageCells {shallowCells:G3}: " +
                       $"[0,{z.ColCount - 1}] = {probe.Real:R} {probe.Imaginary:R}, " +
                       $"digest {DigestCross(z, lay)}");
        Assert.Equal(lastReal, probe.Real,      12);
        Assert.Equal(lastImag, probe.Imaginary, 9);
        Assert.Equal(digest, DigestCross(z, lay));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T2-T5 — the note the verdict ships as
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void T2_TheNoteFiresOnAnUnresolvedGapAndStaysQuietOnAnOrdinaryOne()
    {
        // The shipped MIM technology's own geometry: a 10 um plate pair 0.2 um apart meshes at
        // 2.5 um, i.e. cell/separation = 12.5 — which since MIM-8 is INSIDE the measured range and
        // must NOT be flagged. So must the same artwork at ordinary interconnect spacing, 0.833. A
        // note that fires on every multi-level run is a note nobody reads, which is exactly what
        // CheckFeedClearance was before 2026-08-12.
        //
        // What is past the range now is a 5 nm film — cell/separation 500 — and it is deliberately
        // a geometry no shipped process states. That is the point: MIM-8's whole result is that the
        // structure a user actually draws came back inside.
        var past    = Note(0.005e-6, 10e-6);
        var shipped = Note(0.2e-6,  10e-6);
        var wide    = Note(3.0e-6,  10e-6);
        _out.WriteLine("PAST:    " + past);
        _out.WriteLine("SHIPPED: " + shipped);
        _out.WriteLine("WIDE:    " + wide);

        // MIM-9 moved the QUIET arm's own bound: "resolved by the mesh" is now said only inside the
        // full-wave floor, not inside the fill's own 200 — and since MIM-14 re-ran the de-embedded
        // ladder on the repaired kernel that floor IS 200, so the two bounds coincide today. 12.5
        // and 0.833 are inside it, so the two quiet rungs are unchanged; 500 is past both and still
        // shouts.
        Assert.Contains("CELL/SEPARATION = 500", past);
        Assert.Contains("cell/separation = 12.5", shipped);
        Assert.Contains("are resolved by the mesh", shipped);
        Assert.Contains("cell/separation = 0.833", wide);
        Assert.Contains("are resolved by the mesh", wide);
    }

    [Fact]
    public void T3_TheNoteNamesTheBindingQuantity_AndDoesNotRecommendTheInertKnobs()
    {
        // §3.5's recorded trap, and the reason EmCeilingRefusalTests exists: naming a remedy
        // without asking whether it BINDS. The cell size is
        // min(λ_g/CellsPerWavelength, width/MinCellsAcrossConductor) and only the first term
        // responds to the frequency knobs, so the note must name the pitch and must never simply
        // tell the user to lower Cells per wavelength.
        var thin = Note(0.005e-6, 10e-6);
        _out.WriteLine(thin);

        Assert.Contains("CELL PITCH", thin);
        Assert.Contains("only the first term responds to the frequency knobs", thin);
        Assert.Contains("neither frequency knob acts", thin);

        // And it must scope the damage: single-level results are untouched by this.
        Assert.Contains("single level is unaffected", thin);
    }

    [Fact]
    public void T3b_TheNoteDECIDESTheKnobQuestionArithmetically_RatherThanHedging()
    {
        // The two frequency knobs reach the cell size only through the λ_g/CellsPerWavelength cap,
        // so "do they act here" has an arithmetic answer: what would CellsPerWavelength have to be
        // for that cap to equal the pitch the mesh already has? On the shipped MIM geometry — a
        // 10 µm plate pair at 10 GHz over GaAs — λ_g is ~8.35 mm and the pitch is 2.5 µm, so the
        // answer is in the thousands and the note says so with the number in it.
        var thin = Note(0.005e-6, 10e-6);
        _out.WriteLine(thin);

        Assert.Contains("it would take Cells per wavelength ≥", thin);
        Assert.Contains("neither frequency knob acts here", thin);
        Assert.Contains("the metal's own width", thin);

        // The number itself: λ_g / 2.5 µm at 10 GHz in εᵣ 12.9. Quoted so the test fails if the
        // arithmetic drifts, not only if the sentence does.
        double lambdaG = 299792458.0 / (10e9 * Math.Sqrt(12.9));
        Assert.Contains($"≥ {lambdaG / 2.5e-6:N0} ", thin);
    }

    [Fact]
    public void T4_TheNoteIsAskedPerADJACENTLEVELPAIR_OverTheCellsOnThoseLevelsOnly()
    {
        // R-zz-1's discipline, carried over: the cross-level block is only ever evaluated between
        // cells on the two levels concerned, so a per-mesh question would grade a plate pair on
        // some unrelated conductor's cell. The fixture adds a wide line on the upper level, whose
        // own cells are large; the gate is that exactly one note comes out, about the one adjacent
        // pair that exists.
        double d = 3.0e-6;
        var p = new PlanarProblem(
            [
                new PlanarConductorLayer("plate", [Square(10e-6)], 4.1e7, 0.25e-6, Zlow),
                new PlanarConductorLayer("plate + a wide line",
                    [Square(10e-6),
                     new PlanarPolygon([new EmPoint(200e-6, 0), new EmPoint(400e-6, 0),
                                        new EmPoint(400e-6, 200e-6), new EmPoint(200e-6, 200e-6)])],
                    4.1e7, 0.25e-6, Zlow + d),
            ],
            GroundedSlab.GaAsStarter, 10e9, null, MimStack(d), null);

        var mesh  = SurfaceMesher.Mesh(p, Uniform).Mesh;
        var notes = PlanarSolve.LevelSeparationNotes(p, mesh, 10e9);
        _out.WriteLine(string.Join("\n", notes));

        Assert.Single(notes);
        Assert.Contains("levels 0 and 1", notes[0].Text);
    }

    [Fact]
    public void T5_ASingleLevelProblemGetsNoNoteAtAll()
    {
        // There is no cross-level block, so there is nothing to say — and saying it anyway is how a
        // note stops being read.
        var p = new PlanarProblem(
            [new PlanarConductorLayer("M", [Square(70e-6)], 4.1e7, 2e-6)],
            GroundedSlab.GaAsStarter, 10e9);
        var mesh = SurfaceMesher.Mesh(p, Uniform).Mesh;
        Assert.Empty(PlanarSolve.LevelSeparationNotes(p, mesh, 10e9));
    }

    [Fact]
    public void T6_TheSweepItselfCarriesTheNote()
    {
        // The note is worth nothing if the driver drops it. PlanarSolve.Run assembles it for any
        // multi-level problem — this is the wiring gate, and it is deliberately the cheap one
        // (VerticalRangeVerdict, not a solve).
        var p = Plates(0.2e-6, 500);
        var mesh = SurfaceMesher.Mesh(p, Uniform).Mesh;
        var (verdict, notes) = PlanarSolve.VerticalRangeVerdict(p, mesh, 10e9);

        // MIM-9: at cell/separation 500 this is now a REFUSAL as well as a warning, and the notes
        // come back WITH it — a refusal on its own would take away the sentence that explains the
        // scale it was measured on.
        Assert.False(verdict.Ok);
        Assert.Contains(notes, n => n.Text.Contains("CELL/SEPARATION"));

        // EM-SEV R-emsev-4: and it rides the sweep AS A WARNING. The capitals this sentence has
        // always carried were the author reaching for a class the type system did not have.
        Assert.All(notes.Where(n => n.Text.Contains("CELL/SEPARATION")),
                   n => Assert.True(n.IsWarning));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // M9 — brief-em-mim-9-thin-film-diagnostics.md. Three notes sent a user the wrong way: the
    // non-passivity sentence blamed the innocent de-embedding, the level-separation note reassured
    // on a quantity that was never in question, and the floor was a note where it should have been
    // a refusal. Every gate here is on WHAT THE RUN SAYS; no answer moves.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void M9_1_PastTheFullWaveFloorItRefuses_NamingThePairTheSeparationAndTheCellItMeasured()
    {
        // Item 3. R-emsev-4, deferred by MIM-8 on the grounds that past its bound the answer was
        // unmeasured rather than wrong. MIM-12 measured it — correct to cell/separation 40, the
        // SIGN inverted by 80, noise at 200 — and the refusal was earned on that and set at 40.
        //
        // MIM-14 RE-RAN THAT LADDER ON THE KERNEL MIM-12a REPAIRED AND THE FLOOR IS 200. The sign
        // inversion is gone: every rung out to 200 reads the series element within 10% of the
        // electrostatic mutual capacitance of the SAME mesh, and passive. So the rung this test
        // refuses at has to move with it — and what the refusal now reports past 200 is that
        // NOTHING is measured there, on either side, rather than a measured wrongness.
        var p = Plates(0.2e-6, 500);
        var mesh = SurfaceMesher.Mesh(p, Uniform).Mesh;
        var verdict = PlanarSolve.LevelSeparationVerdict(p, mesh);
        _out.WriteLine(verdict.Reason);

        Assert.False(verdict.Ok);
        Assert.Contains("levels 0 and 1", verdict.Reason);
        Assert.Contains("cell/separation = 500", verdict.Reason);
        Assert.Contains("are 200 nm apart", verdict.Reason);            // the separation
        Assert.Contains("straddling them is 100 µm", verdict.Reason);   // the cell
        Assert.Contains("past the 200 this solve is measured over", verdict.Reason);

        // And ONLY the remedies that act. The three the old sentence offered were each measured
        // inert on this very structure, so naming one here is the defect coming back.
        Assert.Contains("thicken the film", verdict.Reason);
        Assert.Contains("model the part it carries as a circuit element", verdict.Reason);
        Assert.DoesNotContain("raise Cells per wavelength", verdict.Reason);
        Assert.DoesNotContain("narrow the sweep to where", verdict.Reason);

        // Inside the floor nothing is refused — the bound has to let the many runs through that it
        // was never measured against. THE RUNG THAT MOVED IS THE POINT OF THIS LINE: 80 is where
        // MIM-12 read the sign inverted and where this engine used to stop, and it now runs.
        foreach (double inside in new[] { 20.0, 80.0, 100.0 })
            Assert.True(PlanarSolve.LevelSeparationVerdict(
                Plates(0.2e-6, inside),
                SurfaceMesher.Mesh(Plates(0.2e-6, inside), Uniform).Mesh).Ok,
                $"cell/separation {inside} is inside MIM-14's own ladder and must not be refused");
    }

    [Fact]
    public void M9_2_TheReassuringClauseAndTheFullWaveFloorCannotBothFire()
    {
        // Item 2, and it is the sharpest form of the defect: every clause of "the closest conductor
        // levels are resolved by the mesh … the extracted plate capacitance within 1% of ε₀εᵣA/d"
        // was TRUE at cell/separation 200, and a run whose published capacitor had the wrong sign
        // was reading it. MIM-8's validation is electrostatic; the user is reading a de-embedded
        // s-parameter. The two sentences may not co-exist.
        foreach (double ratio in new[] { 5.0, 20.0, 40.0, 80.0, 500.0, 1000.0 })
        {
            var p = Plates(0.2e-6, ratio);
            var mesh = SurfaceMesher.Mesh(p, Uniform).Mesh;
            string note = Assert.Single(PlanarSolve.LevelSeparationNotes(p, mesh, 10e9)).Text;
            bool refused = !PlanarSolve.LevelSeparationVerdict(p, mesh).Ok;
            _out.WriteLine($"{ratio,5}: refused={refused}  {note[..Math.Min(110, note.Length)]}");

            Assert.Equal(refused, !note.Contains("are resolved by the mesh"));

            // Past the floor the note has to state what was validated and what was not, in those
            // terms — the electrostatic/full-wave distinction IS the finding, and it is what the
            // reassuring version never said.
            if (refused)
            {
                Assert.Contains("full-wave", note, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("ELECTROSTATIC", note);
            }
        }
    }

    [Fact]
    public void M9_3_TheNonPassivityCauseFollowsTheCounters_NotAFixedGuessAtTheDeembedding()
    {
        // Item 1. The shipped sentence named the de-embedding as "the usual cause" on every
        // non-passive run. On the run that produced it, DeembedErrorFloor reads 1.6e-3 against an
        // excess of 0.73 — the peel's own estimate of its own error is three decades too small to
        // be the cause, and the engine already computes it.
        // MIM-14: the floor is 200, and 40 µm over 200 nm IS 200 — a rung ON the boundary rather
        // than past it, which would make this fixture's arm a floating-point coin toss. 100 µm is
        // unambiguously past it and measures the same thing.
        var thin = PlanarSolve.NonPassivityCause(
            excess: 0.73, peelErrorFloor: 1.6e-3, pair: new PlanarLevelPair(0, 0.2e-6, 100e-6));
        var peel = PlanarSolve.NonPassivityCause(excess: 0.09, peelErrorFloor: 0.12, pair: null);
        var open = PlanarSolve.NonPassivityCause(excess: 0.73, peelErrorFloor: 1.6e-3, pair: null);
        _out.WriteLine("THIN: " + thin);
        _out.WriteLine("PEEL: " + peel);
        _out.WriteLine("OPEN: " + open);

        // A conductor pair past the floor: named, with its separation, and with the two mesh knobs
        // measured inert rather than offered.
        Assert.Contains("levels 0 and 1", thin);
        Assert.Contains("cell/separation = 500", thin);
        Assert.Contains("does NOT act", thin);
        Assert.DoesNotContain("de-embedding rather than the fill", thin);

        // The case the old sentence WAS written for — the shipped spiral's bottom decade, where the
        // floor really does reach the size of the excess — keeps it. It must not be lost.
        Assert.Contains("de-embedding rather than the fill", peel);
        Assert.Contains("a21 squared", peel);

        // And where nothing accounts for it, it says so rather than naming a cause anyway.
        Assert.Contains("NOT the de-embedding", open);
        Assert.Contains("not identified", open);

        // ASCII throughout, because the same string is stamped into the Touchstone header and that
        // writer transliterates — a σ or an a₂₁ arrives there as "?".
        foreach (string t in new[] { thin, peel, open })
            Assert.DoesNotContain(t, c => c > '\u007f' && c != 'µ');
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T7 — the ACCURACY statement the verdicts rest on. Category=Benchmark: 19 s and 74 s in Debug
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    [Trait("Category", "Benchmark")]
    public void T7_TheCrossLevelBlockDegradedByDecades_AndTheTREATMENTFlattensIt()
    {
        // MIM-3's claim and MIM-8's, in one measurement, because the second is only meaningful
        // beside the first. Turned OFF, the cross-level block moves by decades with cell/separation
        // and the same-level block does not follow it; turned ON, neither does.
        //
        // IF THE "OFF" HALF GOES RED BECAUSE THE ERROR SHRANK, that is a fix and not a regression:
        // PlanarLevels.ValidatedCellOverSeparation, the note in PlanarSolve.LevelSeparationNotes
        // and both verdicts in RESOLVED.md rest on these numbers and must be re-measured rather
        // than these bounds relaxed.
        var offFine   = Measure(0.2e-6, 1.0,  0.0);
        var offCoarse = Measure(0.2e-6, 20.0, 0.0);
        var onFine    = Measure(0.2e-6, 1.0,  0.5);
        var onCoarse  = Measure(0.2e-6, 20.0, 0.5);

        // ── MIM-3, unchanged ──────────────────────────────────────────────────────────────────
        Assert.True(offFine.Cross < 1e-5,
            $"cross-level at cell/separation = 1: {offFine.Cross:E2} (HISTORY's ladder: 2.2e-7).");
        Assert.True(offCoarse.Cross > 1e-2,
            $"cross-level at cell/separation = 20: {offCoarse.Cross:E2} (HISTORY's ladder: 1.5e-1).");
        Assert.True(offCoarse.Cross > 1e3 * offFine.Cross,
            $"{offFine.Cross:E2} at 1 vs {offCoarse.Cross:E2} at 20.");

        // ── MIM-8: the coarse rung is now four decades better, and it is no longer the rung that
        //    decides the answer. The fine rung is untouched, because nothing there is shallow.
        Assert.True(onCoarse.Cross < 1e-5,
            $"cross-level at cell/separation = 20 with the treatment on: {onCoarse.Cross:E2} " +
            $"(MIM-8's ladder: 7.9e-8).");
        Assert.True(onCoarse.Cross < 1e-3 * offCoarse.Cross,
            $"the treatment bought less than three decades at cell/separation 20: " +
            $"{offCoarse.Cross:E2} -> {onCoarse.Cross:E2}.");
        Assert.Equal(offFine.Cross, onFine.Cross, 15);

        // ── and the SAME-level block comes with it. MIM-3 measured that block as flat and it is,
        //    over the range MIM-3 drew; its own Table 2 has it turning up at the coarse end, and
        //    that is why the treatment is asked of every scalar pairing rather than only the
        //    cross-level ones.
        Assert.True(onCoarse.Same < 0.1 * offCoarse.Same,
            $"same-level at cell/separation = 20: {offCoarse.Same:E2} -> {onCoarse.Same:E2} " +
            $"(MIM-8's ladder: 8.3e-3 -> 5.0e-6).");
    }

    private (double Same, double Cross, int N) Measure(double d, double cellOverD, double shallowCells)
    {
        var p    = Plates(d, cellOverD);
        var mesh = SurfaceMesher.Mesh(p, Uniform).Mesh;
        var lay  = mesh.Bases.Select(b => b.LayerIndex).ToArray();
        var lo   = Fill(mesh, p, PlanarFillSettings.Default with { ShallowImageCells = shallowCells });
        var hi   = Fill(mesh, p, Reference with { ShallowImageCells = shallowCells });
        var r    = (Block(lo, hi, lay, false), Block(lo, hi, lay, true), mesh.Bases.Count);
        _out.WriteLine($"d = {d * 1e6:G3} um, cell/separation = {cellOverD:G3}, " +
                       $"ShallowImageCells = {shallowCells:G3}, N = {r.Item3}: " +
                       $"same-level {r.Item1:E2}, cross-level {r.Item2:E2}");
        return r;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T8 — MIM-8's own gate: the PLATE CAPACITANCE
    // ══════════════════════════════════════════════════════════════════════════════════════════
    //
    // The matrix gates above are comparisons against a better quadrature, which is the right
    // instrument for "is the fill converged" and the wrong one for "is the answer right". This is
    // the second ladder, and it is the one the range is drawn on: a plate pair's capacitance
    // against ε₀εᵣA/d, which is an oracle rather than a refinement.
    //
    // It reads P — the ω → 0 potential-coefficient matrix — off PlanarFill's own multi-level
    // builder, so it is the arithmetic a solve uses and not a second copy of it, and it is the
    // instrument PlanarStaticLimitTests already uses: hold one plate at 1 V, the other at 0 V, and
    // report the charge that appears on the second.
    //
    // MIM-12 FOUND THE ONE THING IT IS NOT: THE KERNEL IS FITTED AT 10 GHz, AND A RUN DOES NOT FIT
    // IT THAT WAY. PlateCapacitanceRatio constructs its own PlanarKernelSet and so never calls
    // Dcim.ForStackAtFrequency, which PlanarFrequencyKernel.Fit — i.e. every run — does. On this
    // very capacitor, with this very instrument, the run's own fit reads 1.60 / 1.34 / −0.54 at
    // 3 / 2 / 1 GHz against the 1.003 below. Nothing here is wrong; it is a statement about 10 GHz,
    // and Mim12KernelFitTests.T2 is the same measurement down the band. The general shape of it is
    // worth keeping: a gate that constructs its own kernel can stop measuring what the application
    // does, and here the divergence is a whole sign. MIM-3 needed a whole de-embedded two-port to
    // ask this and had to truncate the stack to get a port at all (§MIM-3 finding 4: raw S cannot
    // carry a capacitance in this engine). Nothing here has a port in it.

    [Theory]
    // ONE STRUCTURE, THREE MESHES — the sharpest form of the statement, and the only capacitance
    // ladder that needs to be a test. A 60 x 60 µm plate pair at the shipped 0.2 µm: the README's
    // own "60 µm capacitor", 1.084 pF of it. The answer used to depend on the MESH, swinging from
    // 4.470 through −0.261 to −0.046 of the closed form; it now reads 1.003 at every one, which is
    // the property the constant is about. A negative capacitance is what the user saw as an open,
    // so the wrong SIGN is asserted here rather than in a test of its own.
    //
    // HISTORY §MIM-8 Table 4 carries the rest: the d-varying ladder in MIM-3's Table 3b shape, the
    // fourth mesh (32 cells across, 26 s, and the one pre-MIM-8 reading that does not LOOK broken at
    // 1.086), and the rungs past cell/separation 75. Cells across the plate, cell/separation,
    // before, after.
    [InlineData(16,  18.75,   4.470, 1.003)]
    [InlineData( 8,  37.5,   -0.261, 1.003)]
    [InlineData( 4,  75.0,   -0.046, 1.003)]
    public void T8_TheSameCapacitorOnThreeMeshes_NowGivesOneAnswer(
        int across, double cellOverSeparation, double before, double after)
        => OneCapacitorOneMesh(across, cellOverSeparation, before, after);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T9 — THE ACCELERATED PATH FILLS THE SAME SCALAR BLOCK
    // ══════════════════════════════════════════════════════════════════════════════════════════
    //
    // MIM-8 subtracts the shallow images from the pairing's terms and puts them back in closed form.
    // The DENSE fill does both halves in ScalarPotentialMatrix; the ACCELERATED one reads the same
    // pairing table through PlanarPulsePotential, for its near set AND for the dense via border, and
    // its grid table samples the kernel directly. A subtraction that reaches one and not the other
    // is not a quadrature question — it DELETES the image, and on this fixture that is 78% of the
    // block's largest entry, an error far larger than the one MIM-8 exists to remove.
    //
    // Nothing else would catch it: AimAccuracyTests' stacks resolve their own images, so the split
    // is empty there and the two paths agree trivially. The gate is the on-demand operator against
    // the dense matrix on a fixture where the split is NOT empty.
    [Fact]
    public void T9_TheOnDemandScalarBlockMatchesTheDenseOne_WhereImagesWereSubtracted()
    {
        var p     = Plates(0.2e-6, 12.5);
        var mesh  = SurfaceMesher.Mesh(p, Uniform, PlanarEdgeReference.LocalConductorWidth).Mesh;
        var st    = PlanarFillSettings.Default;
        var cores = PlanarFill.BuildCores(mesh, st);
        var set   = new PlanarKernelSet(new LayeredSpectralGreens(p.EffectiveStack, p.MaxFrequencyHz),
                                        st.Order).For(cores);
        var levels = PlanarLevels.From(p);

        var dense = PlanarFill.ScalarPotentialMatrix(cores, set, levels);
        var pr    = PlanarFill.MultiLevelPairings.Resolve(cores, set, levels, st);
        var entry = new PlanarEntryCores(cores);

        int layers = pr.TermsQ.GetLength(0);
        var pulse  = new PlanarPulsePotential?[layers, layers];
        bool anyShallow = false;
        for (int la = 0; la < layers; la++)
        for (int lb = 0; lb < layers; lb++)
            if (pr.TermsQ[la, lb] is { } t)
            {
                anyShallow |= pr.ShallowQ[la, lb].Count > 0;
                pulse[la, lb] = new PlanarPulsePotential(entry, t, pr.RemQ[la, lb],
                                                        pr.ShallowQ[la, lb], pr.TermsQFar[la, lb],
                                                        pr.RemQFar[la, lb]);
            }

        // The fixture must be IN the regime, or the comparison proves nothing.
        Assert.True(anyShallow, "no image was shallow here, so this fixture cannot see the defect");

        // MIM-12a — and in the regime as it is NOW: the subtracted list must hold the thin region's
        // own DERIVED series, not a fitted image that happens to be shallow. A branch whose off-state
        // is the identity is a branch no existing test exercises, and the images in that list changed
        // under this test without its arithmetic changing at all.
        var greens = new LayeredSpectralGreens(p.EffectiveStack, p.MaxFrequencyHz);
        double cell = 0;
        foreach (var c in mesh.Cells) cell = Math.Max(cell, Math.Max(c.Width, c.Height));
        var derived = greens.ThinRegionImagesAtHeights(
            GreensKernel.ScalarPotential, levels.Of(1), levels.Of(0),
            PlanarFillSettings.Default.ShallowImageCells * cell);
        Assert.Null(derived.NotApplicable);
        Assert.True(derived.Crosses);
        var crossRemoved = pr.ShallowQ[0, 1];
        _out.WriteLine($"cross-level removed list: {crossRemoved.Count} images, " +
                       $"{derived.Images.Count} of them the derived series (A = {derived.Amplitude.Real:F6}, " +
                       $"q = {derived.Ratio.Real:F6})");
        for (int i = 0; i < derived.Images.Count; i++)
            Assert.Equal(derived.Images[i].Depth.Real, crossRemoved[i].Depth.Real, 15);

        double worst = 0, scale = 0;
        int m = mesh.Cells.Count;
        for (int a = 0; a < m; a++)
        for (int b = 0; b < m; b++)
        {
            var got = pulse[mesh.Cells[a].LayerIndex, mesh.Cells[b].LayerIndex]!.At(a, b);
            scale = Math.Max(scale, dense[a, b].Magnitude);
            worst = Math.Max(worst, (got - dense[a, b]).Magnitude);
        }
        _out.WriteLine($"{m} cells: worst |on-demand − dense| / largest entry = {worst / scale:E3}");

        // P5's own tolerance for this comparison: the class representative's coordinates, not the
        // member's, so the last bits move. Measured at 6.3e-16 here.
        Assert.True(worst / scale < 1e-12, $"{worst / scale:E3}");
    }

    private void OneCapacitorOneMesh(int across, double cellOverSeparation, double before, double after)
    {
        var mesh = new PlanarMeshSettings(Auto: false, CellsPerWavelength: 20, EdgeMesh: false,
                                          EdgeCells: 3, MinCellsAcrossConductor: across);
        var (ratioOff, cell) = PlateCapacitanceRatio(0.2e-6, 0.0, 60e-6, mesh);
        var (ratioOn,  _)    = PlateCapacitanceRatio(0.2e-6, 0.5, 60e-6, mesh);
        _out.WriteLine($"{across} cells across a 60 µm plate: pitch {cell * 1e6:G3} µm, " +
                       $"cell/separation {cell / 0.2e-6:G4}, C/(ε₀εᵣA/d) = {ratioOff:F3} off, " +
                       $"{ratioOn:F3} on");

        Assert.Equal(cellOverSeparation, cell / 0.2e-6, 3);
        Assert.Equal(before, ratioOff, 2);
        Assert.Equal(after,  ratioOn,  2);
        Assert.True(Math.Abs(ratioOn - 1.0) <= 0.10,
            $"cell/separation {cell / 0.2e-6:G4}: C/(ε₀εᵣA/d) = {ratioOn:F3}, outside 10%.");
    }

    /// <summary>The mutual capacitance of the two plates over ε₀εᵣA/d, and the cell pitch it was
    /// measured at. PLATE AND PITCH FIXED, so the ladder's axis is the film thickness.</summary>
    private static (double Ratio, double Cell) PlateCapacitanceRatio(
        double d, double shallowCells, double w = 10e-6, PlanarMeshSettings? meshSettings = null)
    {
        const double epsR = 6.80;
        var p = new PlanarProblem(
            [
                new PlanarConductorLayer("bottom plate", [Square(w)], 4.1e7, 0.25e-6, Zlow),
                new PlanarConductorLayer("top plate",    [Square(w)], 4.1e7, 0.25e-6, Zlow + d),
            ],
            GroundedSlab.GaAsStarter, 10e9, null, MimStack(d), null);

        var st    = PlanarFillSettings.Default with { ShallowImageCells = shallowCells };
        var mesh  = SurfaceMesher.Mesh(p, meshSettings ?? Uniform).Mesh;
        var cores = PlanarFill.BuildCores(mesh, st);
        var set   = new PlanarKernelSet(new LayeredSpectralGreens(p.EffectiveStack, p.MaxFrequencyHz),
                                        st.Order).For(cores);
        var pm    = PlanarFill.ScalarPotentialMatrix(cores, set, PlanarLevels.From(p));

        // φ_a = (1/ε₀)·Σ_b P[a,b]·Q_b — bottom plate at 1 V, top at 0 V, and the mutual capacitance
        // is minus the charge that lands on the top one.
        int m = mesh.Cells.Count;
        var a   = new Mat<Complex>(m, m);
        var rhs = new Vec<Complex>(m);
        for (int i = 0; i < m; i++)
        {
            rhs[i] = mesh.Cells[i].LayerIndex == 0 ? Complex.One : Complex.Zero;
            for (int j = 0; j < m; j++) a[i, j] = pm[i, j] / EmConstants.Eps0;
        }
        var q = a.Lu().Solve(rhs);

        Complex onTop = Complex.Zero;
        for (int i = 0; i < m; i++) if (mesh.Cells[i].LayerIndex != 0) onTop += q[i];
        double closed = EmConstants.Eps0 * epsR * w * w / d;
        return (-onTop.Real / closed, mesh.Cells[0].Width);
    }

    private static string Note(double d, double plate)
    {
        var p = new PlanarProblem(
            [
                new PlanarConductorLayer("bottom plate", [Square(plate)], 4.1e7, 0.25e-6, Zlow),
                new PlanarConductorLayer("top plate",    [Square(plate)], 4.1e7, 0.25e-6, Zlow + d),
            ],
            GroundedSlab.GaAsStarter, 10e9, null, MimStack(d), null);
        var mesh = SurfaceMesher.Mesh(p, Uniform).Mesh;
        return Assert.Single(PlanarSolve.LevelSeparationNotes(p, mesh, 10e9)).Text;
    }
}
