// ================================================================
//  PdkPCellExampleTests.cs — the "PDK PCells" example workspace.
//
//  Every assertion here is a bug this example actually shipped with (2026-09-15), and every one of
//  them was SILENT: the artwork drew, the workspace opened, and nothing anywhere said what was
//  wrong.
//
//   1. A PARAMETER WITH NO DECLARED DEFAULT IS NOT PLACED. The generator falls back to its own
//      internal number and draws correctly, so the cell looks finished — while the instance carries
//      no parameters at all, the Properties Inspector has nothing to edit, and every drag handle is
//      rejected with "declares a drag handle for 'L', which is not one of its parameters."
//
//   2. THE DEFAULT SIGNAL LAYER IS THE TOPMOST CONDUCTOR, and on this GaAs stackup that is the AIR
//      BRIDGE — 3 um up with nothing under it. A coil drawn there is suspended in air, and the
//      microstrip testbench beside it extracted h = 102.75 um through air, nitride and GaAs rather
//      than the 100 um of substrate it is supposed to demonstrate.
//
//   3. A COMMITTED PCELL INSTANCE HAS TO REBUILD ON SOMEBODY ELSE'S MACHINE. The generated cell
//      folder is content-addressed and git-ignored, so the .clay in the repository names a folder
//      that does not exist until a generator runs. If the name it rebuilds to differs, every
//      instance is repointed and the committed file is rewritten on first open.
//
//   4. A KIT THAT SHIPS ONLY ARTWORK HAD NO SCHEMATIC SIDE AT ALL. Update Schematic from Layout on
//      the spiral created a schematic and placed NOTHING in it, reporting that the kit "is not
//      loaded" — it was; what it had no part for was the cell. The kit now ships a .csym per
//      generator and circuitRF mounts a part around it, carrying the generator's own declared
//      parameters. See PCellKitSchematicParts.
// ================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CircuitRF.Core;
using CircuitRF.Design.Cells;
using CircuitRF.Design.Layout;
using CircuitRF.Design.Layout.PCells;
using CircuitRF.Design.Schematic;
using CircuitRF.Ui.Layout;
using CircuitRF.Ui.Schematic;
using CircuitRF.Ui.Layout.PCells;
using CircuitRF.Ui.Layout.PCells.Wire;
using CircuitRF.Ui.Tests.Layout.PCells;
using Xunit;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests.Examples;

[Collection(PCellResolverCollection.Name)]
public sealed class PdkPCellExampleTests(ITestOutputHelper output) : IDisposable
{
    private readonly List<string> _scratch = [];

    public void Dispose()
    {
        PCellRegistry.ClearResolvers();
        foreach (var d in _scratch) try { Directory.Delete(d, true); } catch { /* best effort */ }
    }

    private const string Mlin   = "KIT_MLIN";
    private const string Spiral = "KIT_SPIRAL";

    /// <summary>The example technology's resolution, and every length below is in its DBU.</summary>
    private const int Dbu = 1000;

    /// <summary>Metal1 is the conductor that sits on the GaAs; Metal2 is the air bridge above it.</summary>
    private static readonly LayerKey Metal1 = new(1, 0);
    private static readonly LayerKey Metal2 = new(2, 0);
    private static readonly LayerKey Via    = new(3, 0);

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "circuitRF.slnx")))
            dir = Path.GetDirectoryName(dir) ?? "";
        return dir.Length > 0 ? dir : AppContext.BaseDirectory;
    }

    private static string ExampleRoot() => Path.Combine(RepoRoot(), "examples", "PDK PCells");

    private static Technology Tech()
        => TechPersistence.LoadFromFile(
               Path.Combine(ExampleRoot(), "tech", "mmic-GaAs_2LM_100um.ctech"));

    private PCellWorkerProvider StartKit(string workspaceRoot)
    {
        string kitDir = Path.Combine(workspaceRoot, "pcell-kit");
        var manifest = PCellGeneratorManifest.TryRead(kitDir, out _)
            ?? throw new InvalidOperationException($"No PCell manifest in '{kitDir}'.");

        // The package the application ships is on the kit's path in production; a test supplies it
        // the same way rather than relying on whatever happens to be importable here.
        return new PCellWorkerProvider(ProcessPCellWorkerTransport.Start(
            PythonRunner.Interpreter!, manifest.ResolveEntry(kitDir),
            [.. manifest.ResolvePythonPath(kitDir), PythonRunner.PackageRoot]));
    }

    // ══ 1. Every parameter is placeable, and every handle is draggable ═══════

    /// <summary>
    /// The whole parameter list arrives on a placed instance, and every drag handle names a
    /// parameter that is on it.
    ///
    /// <para>These are one fact, not two: <see cref="PCellHandleSolver.Validate"/> is asked about
    /// the instance's PARAMETERS, so a parameter missing for want of a default takes its handle
    /// down with it. Asserting the handles alone would pass on a cell whose defaults were declared
    /// and whose geometry was wrong; asserting the defaults alone would miss a handle naming a
    /// parameter that was renamed.</para>
    /// </summary>
    [PythonFact]
    public void EveryDeclaredParameterHasADefault_AndEveryHandleIsDraggable()
    {
        using var kit = StartKit(ExampleRoot());
        var tech = Tech();

        foreach (string id in kit.GeneratorIds)
        {
            var declared = kit.DeclaredParameters(id)!;
            var defaults = kit.DeclaredDefaults(id)!;
            Assert.NotEmpty(declared);

            foreach (var d in declared)
                Assert.True(defaults.ContainsKey(d.Name),
                    $"'{id}' declares '{d.Name}' with no default, so a placed instance will not " +
                    "carry it: nothing to edit, and any handle naming it is rejected.");

            Assert.True(kit.TryGetGenerator(id, out var generate));
            var result = generate(defaults, tech, PCellLayerSelection.Default);

            foreach (var handle in result.Handles ?? [])
                Assert.Equal(PCellHandleRejection.None,
                             PCellHandleSolver.Validate(handle, defaults));

            output.WriteLine($"{id}: {declared.Count} parameter(s), {(result.Handles?.Count ?? 0)} handle(s)");
        }
    }

    /// <summary>
    /// The layer choice is a DROPDOWN, not a free-text box — the generator declares the two values
    /// it accepts, which is the only thing that can make the Properties Inspector offer them.
    /// </summary>
    [PythonFact]
    public void TheMetalParameterOffersItsTwoLayersAsChoices()
    {
        using var kit = StartKit(ExampleRoot());

        foreach (string id in kit.GeneratorIds)
        {
            var metal = Assert.Single(kit.DeclaredParameters(id)!.Where(p => p.Name == "Metal"));
            Assert.Equal(["Metal1", "Metal2"], metal.Choices!.Select(c => c.AsText()));
            Assert.Equal("Metal1", metal.Default!.Value.AsText());
        }
    }

    // ══ 2. Nothing is drawn on the air bridge by default ════════════════════

    /// <summary>
    /// Both generators draw on Metal1 — the conductor that lies on the GaAs — and not on the
    /// technology's own default signal layer, which is the topmost conductor and therefore the air
    /// bridge. A coil suspended 3 um up in air over nothing is not a coil.
    /// </summary>
    [PythonFact]
    public void ThePlacedArtworkIsOnMetal1_NotOnTheAirBridge()
    {
        var tech = Tech();
        Assert.Equal(Metal2, SubstrateResolver.ResolveSignalLayerKey(tech, PCellLayerSelection.Default, out _));

        using var kit = StartKit(ExampleRoot());

        // The line is one rectangle and it is on the substrate metal, full stop.
        Assert.True(kit.TryGetGenerator(Mlin, out var line));
        var lineShapes = line(kit.DeclaredDefaults(Mlin)!, tech, PCellLayerSelection.Default).Shapes;
        Assert.All(lineShapes, s => Assert.Equal(Metal1, s.Layer));

        // The spiral is allowed geometry on Metal2 — that is its crossover — but the coil itself is
        // on the substrate metal, and the crossover is a single span rather than the bulk of the cell.
        Assert.True(kit.TryGetGenerator(Spiral, out var coil));
        var coilShapes = coil(kit.DeclaredDefaults(Spiral)!, tech, PCellLayerSelection.Default).Shapes;
        Assert.Contains(coilShapes, s => s.Layer == Metal1);
        Assert.Single(coilShapes.Where(s => s.Layer == Metal2));
    }

    /// <summary>
    /// The spiral's inner terminal escapes on the OTHER metal, through via posts — three distinct
    /// layers, and both pins back on the coil's own one. Without the crossover the inner end of the
    /// coil is a terminal nothing can reach; with it on the wrong layer it shorts every turn it
    /// passes over.
    /// </summary>
    [PythonFact]
    public void TheSpiralsInnerTerminalEscapesOverAnAirBridge()
    {
        using var kit = StartKit(ExampleRoot());
        Assert.True(kit.TryGetGenerator(Spiral, out var generate));

        var coilOnMetal1 = generate(kit.DeclaredDefaults(Spiral)!, Tech(), PCellLayerSelection.Default);
        AssertThreeLayerEscape(coilOnMetal1, coil: Metal1, bridge: Metal2);

        // The inverse arrangement: the coil moves up onto the bridge metal and the escape becomes an
        // underpass. Asserted because it is what makes `Metal` a real parameter rather than a label.
        var flipped = new Dictionary<string, PCellValue>(kit.DeclaredDefaults(Spiral)!)
        {
            ["Metal"] = PCellValue.Text("Metal2"),
        };
        AssertThreeLayerEscape(generate(flipped, Tech(), PCellLayerSelection.Default),
                               coil: Metal2, bridge: Metal1);
    }

    private static void AssertThreeLayerEscape(PCellResult result, LayerKey coil, LayerKey bridge)
    {
        var layers = result.Shapes.Select(sh => sh.Layer).ToHashSet();
        Assert.Contains(coil,   layers);
        Assert.Contains(bridge, layers);
        Assert.Contains(Via,    layers);

        // Both terminals on the coil's own metal, or the cell abuts two different things.
        Assert.Equal(2, result.Pins.Count);
        Assert.All(result.Pins, p => Assert.Equal(coil, p.Layer));

        // The crossover spans every turn it has to clear: it starts at pin 2 and reaches past the
        // innermost rail. A bridge that stops short is a broken terminal that still renders.
        var span = result.Shapes.OfType<RectShape>().Single(r => r.Layer == bridge);
        long innermost = result.Shapes.Where(sh => sh.Layer == coil).Max(RightmostX);
        Assert.True(Math.Min(span.X1, span.X2) <= result.Pins[1].X);
        Assert.True(Math.Max(span.X1, span.X2) < innermost);
    }

    private static long RightmostX(LayoutShape shape) => shape switch
    {
        RectShape r    => Math.Max(r.X1, r.X2),
        PolygonShape p => Enumerable.Range(0, p.Xy.Length / 2).Max(i => p.Xy[i * 2]),
        _              => long.MinValue,
    };

    /// <summary>
    /// <b>The coil is emitted as the REGIONS it forms, not as the rectangles it was drawn from.</b>
    ///
    /// <para>Owner, 2026-09-15: the spiral was built out of many segments and should render as one
    /// continuous piece of metal. Overlapping rectangles already LOOK solid, so this is easy to leave
    /// — but they are one picture and not one figure: a DRC width check measures each rectangle
    /// rather than the conductor, an EM extraction meshes internal edges that carry no current, and
    /// an export carries every seam into whatever reads it next.</para>
    ///
    /// <para>The union is performed by circuitRF over the boolean channel, so the generator's answer
    /// and the layout editor's own Boolean commands cannot disagree — which is also what this test
    /// exercises, since a `clip` call is a host round trip made in the middle of a `generate`.</para>
    /// </summary>
    [PythonFact]
    public void TheCoilIsOneContinuousPieceOfMetal_NotSixteenRectangles()
    {
        using var kit = StartKit(ExampleRoot());
        Assert.True(kit.TryGetGenerator(Spiral, out var generate));
        var result = generate(kit.DeclaredDefaults(Spiral)!, Tech(), PCellLayerSelection.Default);

        var onCoil = result.Shapes.Where(sh => sh.Layer == Metal1).ToList();
        Assert.All(onCoil, sh => Assert.IsType<PolygonShape>(sh));

        // Two regions, and the second is not an accident: the coil with its outer lead is one piece,
        // and the landing pad the escape drops back onto is deliberately separate — it is joined
        // through a via, not through metal, which is the whole reason the crossover exists.
        Assert.Equal(2, onCoil.Count);

        // A three-turn square spiral plus its lead: one closed outline, and far fewer vertices than
        // the 16 rectangles (64 corners) that drew it. Asserted as a bound rather than an exact
        // count — the point is that the seams are gone, not a particular vertex total.
        var coilRegion = onCoil.Cast<PolygonShape>().MaxBy(p => p.Xy.Length)!;
        Assert.True(coilRegion.Xy.Length / 2 < 64,
                    $"the coil came back as {coilRegion.Xy.Length / 2} vertices — it was not merged.");
        Assert.True(coilRegion.Holes is null or { Count: 0 },
                    "a spiral's turns open to the outside; a hole means the outline closed on itself.");
    }

    /// <summary>
    /// With NO technology at all the three layers are still three layers. A layout that resolves no
    /// technology still generates geometry (the PCell contract's own §2), and a generator that
    /// leaned on <c>tech.signal_layer</c> there would collapse coil, bridge and via onto one — which
    /// draws perfectly and shorts the cell.
    /// </summary>
    [PythonFact]
    public void TheLayersSurviveALayoutWithNoTechnology()
    {
        using var kit = StartKit(ExampleRoot());
        Assert.True(kit.TryGetGenerator(Spiral, out var generate));
        AssertThreeLayerEscape(
            generate(kit.DeclaredDefaults(Spiral)!, technology: null, PCellLayerSelection.Default),
            coil: Metal1, bridge: Metal2);
    }

    /// <summary>
    /// <b>The winding contains exactly the metal its turn count calls for — no dead-ended stub.</b>
    ///
    /// <para>OWNER REPORT: for any turn count there was a trace that ran off to the right, bent
    /// twice and stopped. It was the outermost lap's free end. The outer lead was hung off the
    /// MIDDLE of the outermost side, so metal continued from it in both directions — one way
    /// spiralling inward, the other running three-quarters of a lap to the winding's real end and
    /// dead-ending there. In the same metal as the coil, with nothing in the picture to tell them
    /// apart, and an inductor with a ¾-lap open stub on it is a different part.</para>
    ///
    /// <para>Checked as AREA against a closed form, because that is what a stub changes and what no
    /// amount of looking at the outline will tell you. A rectilinear path of constant width w and
    /// centre-line length L, mitred square, covers exactly <c>L·w + w²</c>; and the centre line of a
    /// square spiral is an arithmetic series — two sides of every length, each pair one pitch longer
    /// than the last — so for n whole turns it sums to <c>8na + 2pn(2n−1)</c>. Both are written out
    /// here rather than asked of the generator: an oracle that shares the code under test proves
    /// nothing. The old stub was some 30% of the winding, against the 1% tolerance below.</para>
    /// </summary>
    [PythonTheory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void TheWindingHoldsExactlyTheMetalItsTurnCountCallsFor(int turns)
    {
        using var kit = StartKit(ExampleRoot());
        Assert.True(kit.TryGetGenerator(Spiral, out var generate));

        var defaults = kit.DeclaredDefaults(Spiral)!;
        long w     = PCellUnits.MetresToDbu(defaults["Width"].AsReal(), Dbu);
        long pitch = w + PCellUnits.MetresToDbu(defaults["Space"].AsReal(), Dbu);
        long a     = PCellUnits.MetresToDbu(defaults["Inner"].AsReal(), Dbu) / 2 + w / 2;
        long lead  = 2 * w;

        var parameters = new Dictionary<string, PCellValue>(defaults) { ["Turns"] = PCellValue.Real(turns) };
        var coil = generate(parameters, Tech(), PCellLayerSelection.Default)
                   .Shapes.OfType<PolygonShape>().Where(g => g.Layer == Metal1)
                   .MaxBy(g => Math.Abs(SignedArea(g)))!;

        long centreLine = 8L * turns * a + 2L * pitch * turns * (2L * turns - 1) + lead;
        double expected = (double)centreLine * w + (double)w * w;
        double actual   = Math.Abs(SignedArea(coil));

        Assert.True(Math.Abs(actual - expected) / expected < 0.01,
            $"{turns} turns should cover {expected:N0} DBU² of metal; the winding covers {actual:N0}. " +
            "More than that is metal on a path nothing asked for.");
    }

    /// <summary>Shoelace. The polygon is closed implicitly, like every <c>.clay</c> ring.</summary>
    private static double SignedArea(PolygonShape polygon)
    {
        double sum = 0;
        int n = polygon.Xy.Length / 2;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            sum += (double)polygon.Xy[i * 2] * polygon.Xy[j * 2 + 1]
                 - (double)polygon.Xy[j * 2] * polygon.Xy[i * 2 + 1];
        }
        return sum / 2.0;
    }

    /// <summary>
    /// <b>A fractional turn count is a real turn count, and it moves the outer terminal.</b>
    ///
    /// <para>Owner: 3.5 turns should put the second terminal somewhere else. A square spiral changes
    /// heading every quarter lap, so each extra quarter brings the outer end out on the next side —
    /// and there is nothing to special-case, because the winding is walked rather than stacked out of
    /// whole rings. `Turns` is declared REAL for this; as an integer the parameter editor refuses
    /// "3.5" outright, which is the correct behaviour for a declared integer and the wrong
    /// declaration for a spiral.</para>
    /// </summary>
    [PythonFact]
    public void AQuarterTurnMovesTheOuterTerminalToTheNextSide()
    {
        using var kit = StartKit(ExampleRoot());
        Assert.True(kit.TryGetGenerator(Spiral, out var generate));
        var defaults = kit.DeclaredDefaults(Spiral)!;

        double OuterHeading(double turns)
        {
            var parameters = new Dictionary<string, PCellValue>(defaults) { ["Turns"] = PCellValue.Real(turns) };
            var result = generate(parameters, Tech(), PCellLayerSelection.Default);
            return result.Pins.Single(pin => pin.Name == "1").OutwardDirectionDeg;
        }

        // Four consecutive quarters, four different headings — and the fifth comes back round.
        var headings = new[] { 3.0, 3.25, 3.5, 3.75 }.Select(OuterHeading).ToList();
        Assert.Equal(4, headings.Distinct().Count());
        Assert.Equal(headings[0], OuterHeading(4.0));

        // The declaration is Real, or none of the above can be typed into the parameter editor:
        // an edit is parsed back into the kind the parameter already has, never coerced to another.
        Assert.Equal(PCellValueKind.Real, defaults["Turns"].Kind);
        Assert.Equal(PCellValueKind.Real,
                     kit.DeclaredParameters(Spiral)!.Single(d => d.Name == "Turns").Kind);
    }

    /// <summary>
    /// <b>Every grip has a floor, and the floor actually stops the drag.</b>
    ///
    /// <para>OWNER REPORT: the grip handles need a minimum or the geometry goes crazy. It does, and
    /// specifically: a width or an inner opening dragged through zero turns negative, every
    /// rectangle in the winding inverts, and what reaches the clipper is a self-intersecting mess
    /// that still renders. <see cref="PCellHandleSolver"/> clamps each proposal to the handle's own
    /// declared <c>Min</c>, so the bound has to be ON the handle — a guard inside the generator is
    /// never consulted by the solver's search, it only refuses the result afterwards.</para>
    ///
    /// <para>Driven through the real solver rather than by reading the declaration back: a bound
    /// that is declared and not honoured looks identical from the outside.</para>
    /// </summary>
    [PythonTheory]
    [InlineData(Mlin)]
    [InlineData(Spiral)]
    public void DraggingAGripPastZero_StopsAtTheProcessMinimum(string generatorId)
    {
        using var kit = StartKit(ExampleRoot());
        Assert.True(kit.TryGetGenerator(generatorId, out var generate));
        var tech = Tech();
        var defaults = kit.DeclaredDefaults(generatorId)!;

        PCellResult Generate(IReadOnlyDictionary<string, PCellValue> p)
            => generate(p, tech, PCellLayerSelection.Default);

        var handles = Generate(defaults).Handles!;
        Assert.NotEmpty(handles);

        for (int i = 0; i < handles.Count; i++)
        {
            var handle = handles[i];
            Assert.True(handle.Min is > 0,
                $"'{handle.Parameter}' has no declared floor — dragging it through zero inverts the cell.");

            // Aim the grip a long way past the anchor, which is the centre: the drag the report is
            // about. Where it lands is the generator's answer, and it must be at or above the floor.
            Assert.True(PCellHandleSolver.MeasureSensitivity(
                Generate, defaults, handle, i, out double valuePerProjection, out _));
            var solved = PCellHandleSolver.Solve(
                Generate, defaults, handle, i, targetProjection: -10_000_000, valuePerProjection);

            Assert.True(solved.Ok);
            double landed = solved.Value.AsReal();
            Assert.True(landed >= handle.Min!.Value - 1e-15,
                $"'{handle.Parameter}' was dragged to {landed}, under its declared floor of {handle.Min}.");
        }
    }

    /// <summary>
    /// And a value TYPED past the floor is refused, naming the parameter — because a handle's bound
    /// binds the gesture and nothing else. The Properties Inspector writes straight through to the
    /// generator, so the floor has to be stated in both places or it holds in only one of them.
    /// </summary>
    [PythonTheory]
    [InlineData(Mlin,   "W", "L")]
    [InlineData(Spiral, "Width", "Space", "Inner")]
    public void AValueTypedBelowTheProcessMinimum_IsRefusedRatherThanDrawn(
        string generatorId, params string[] lengths)
    {
        using var kit = StartKit(ExampleRoot());
        Assert.True(kit.TryGetGenerator(generatorId, out var generate));
        var defaults = kit.DeclaredDefaults(generatorId)!;

        foreach (string name in lengths)
        {
            var parameters = new Dictionary<string, PCellValue>(defaults)
            {
                [name] = PCellValue.Real(0.5e-6),   // half a micron, on a 4 µm process
            };
            var refusal = Assert.Throws<PCellWireException>(
                () => generate(parameters, Tech(), PCellLayerSelection.Default));
            Assert.Contains(name, refusal.Message, StringComparison.Ordinal);
        }

        // …and a negative one, which is the value that actually inverts the artwork.
        var inverted = new Dictionary<string, PCellValue>(defaults) { [lengths[0]] = PCellValue.Real(-50e-6) };
        Assert.Throws<PCellWireException>(() => generate(inverted, Tech(), PCellLayerSelection.Default));
    }

    // ══ 3. The committed instance rebuilds somewhere else ═══════════════════

    /// <summary>
    /// A copy of the workspace with its generated-cells cache deleted — which is every clone, since
    /// the folder is git-ignored — rebuilds the cell the committed <c>.clay</c> names, and the
    /// instance ends up pointing at real artwork.
    ///
    /// <para><b>The snapshot deliberately records NO technology identity.</b> That field is an
    /// ABSOLUTE PATH on the machine that placed the cell: committed, it would be somebody's home
    /// directory in a public repository and a path that resolves nowhere else, so every other
    /// machine would silently rebuild against no technology at all. The generators carry their own
    /// fallback for the layer names instead (see the test above), which makes the answer the same
    /// everywhere.</para>
    ///
    /// <para><b>Repointing is allowed here, and that is not a weakened assertion.</b> A generated
    /// cell's folder name hashes the generator's sources AND circuitRF's own shipped PCell Python
    /// package, so editing a comment in <c>tools/pcell-python</c> moves it. Repointing is precisely
    /// the designed response to that; demanding a stable name would turn an ordinary edit to the
    /// package into a failure of an example workspace's test. What must hold is that the rebuild
    /// HAPPENS and the instance resolves afterwards.</para>
    /// </summary>
    [PythonFact]
    public void TheCommittedSpiralRebuildsFromAnEmptyCache_WithoutRepointingAnything()
    {
        string copy = Path.Combine(Path.GetTempPath(), "crf-pdkex-" + Guid.NewGuid().ToString("N")[..12]);
        _scratch.Add(copy);
        CopyDirectory(ExampleRoot(), copy);
        try { Directory.Delete(Path.Combine(copy, GeneratedCellStore.ReservedFolderName), true); }
        catch (DirectoryNotFoundException) { /* already absent, which is the clone's own state */ }

        string clayPath = Path.Combine(copy, "SpiralInductor", "layout", "SpiralInductor.clay");
        string before = File.ReadAllText(clayPath);

        var view = LayoutPersistence.LoadFromFile(clayPath);
        var snapshot = Assert.Single(view.PCellSnapshots).Value;
        Assert.Equal(Spiral, snapshot.GeneratorId);
        Assert.Null(snapshot.TechIdentity);
        Assert.NotEmpty(snapshot.Parameters);
        Assert.Single(view.Instances);
        Assert.Empty(view.Shapes);

        PCellRegistry.ClearResolvers();
        using var resolver = new PCellWorkerResolver(
            copy,
            findInterpreter: (_, _) => new PythonInterpreter(PythonRunner.Interpreter!, [], "test", "supplied by the test"),
            report: output.WriteLine);
        PCellRegistry.AddResolver(resolver);

        GeneratedCellsLifecycle.RegenerateAll(copy, _ => null, output.WriteLine);

        // Whatever the rebuild named the cell, the instance now points at it and it is there.
        var rebuilt = LayoutPersistence.LoadFromFile(clayPath);
        string cellDir = RefPath.Resolve(
            Path.GetDirectoryName(clayPath)!, Assert.Single(rebuilt.Instances).CellRef);
        Assert.True(Directory.Exists(cellDir), $"'{cellDir}' was not rebuilt.");

        var cell = LayoutPersistence.LoadFromFile(
            Path.Combine(cellDir, "layout", Path.GetFileName(cellDir) + ".clay"));
        Assert.Equal(Spiral, cell.PCellOrigin!.GeneratorId);
        AssertThreeLayerLayout(cell, coil: Metal1, bridge: Metal2);

        // Untouched when nothing moved — the ordinary case, and what says the committed file is the
        // one this build produces rather than one that is silently rewritten on every open.
        if (File.ReadAllText(clayPath) != before)
            output.WriteLine("NOTE: the cell was repointed, so the committed .clay is stale against " +
                             "this build of tools/pcell-python. Re-author it to remove the churn.");
    }

    /// <summary>The generated cell on disk carries the same three layers the generator produced —
    /// asserted against the FILE, because that is what the layout actually draws.</summary>
    private static void AssertThreeLayerLayout(LayoutView cell, LayerKey coil, LayerKey bridge)
    {
        var layers = cell.Shapes.Where(sh => sh is not LabelShape).Select(sh => sh.Layer).ToHashSet();
        Assert.Contains(coil, layers);
        Assert.Contains(bridge, layers);
        Assert.Contains(Via, layers);
        Assert.Equal(2, cell.Pins.Count);
        Assert.All(cell.Pins, p => Assert.Equal(coil, p.Layer));
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from))
            File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.GetDirectories(from))
            CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
    }

    // ══ 4. The testbench beside it models the right substrate ═══════════════

    /// <summary>
    /// The microstrip testbench extracts the GaAs substrate, not the air above it: h = 100 um and
    /// er = 12.9, which is what `TL1`'s explicit <c>SignalLayer = Metal1</c> buys. Left to the
    /// default the resolver picks the topmost conductor — the air bridge — and quietly produces
    /// h = 102.75 um through a mixture of air, nitride and GaAs.
    /// </summary>
    [Fact]
    public void TheMicrostripTestbenchIsReferencedToTheGaAsSubstrate()
    {
        var tech = Tech();
        var overrides = CircuitRF.Design.Schematic.MicrostripSubstrateInjection.BuildOverrides(
            tech, out _, signalLayerNameOverride: "Metal1");

        double H  = double.Parse(overrides.Single(o => o.Name == "H").Expression,  System.Globalization.CultureInfo.InvariantCulture);
        double Er = double.Parse(overrides.Single(o => o.Name == "Er").Expression, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(100e-6, H, 12);
        Assert.Equal(12.9,   Er, 10);

        // …and the schematic actually says so. A correct resolver reached through no parameter is
        // not what the testbench runs.
        string csch = File.ReadAllText(
            Path.Combine(ExampleRoot(), "MicrostripLine", "schematic", "MicrostripLine.csch"));
        Assert.Contains("\"SignalLayer\"", csch, StringComparison.Ordinal);
        Assert.Contains("\"Metal1\"", csch, StringComparison.Ordinal);
    }

    // ══ 5. The kit has a schematic side, because it ships a symbol ═══════════

    /// <summary>
    /// The example ships one <c>.csym</c> per generator, beside its manifest, and that file's NAME
    /// is the whole declaration — nothing lists it. Both are readable, and both declare the two pins
    /// their generator declares, which is what decides the placed component's port count.
    /// </summary>
    [Theory]
    [InlineData(Mlin)]
    [InlineData(Spiral)]
    public void TheKitShipsASchematicSymbolForEveryGenerator(string generatorId)
    {
        string kitDir = Path.Combine(ExampleRoot(), "pcell-kit");

        string path = PCellKitSchematicParts.FindSymbolFile(kitDir, generatorId)
            ?? throw new Xunit.Sdk.XunitException(
                $"'{generatorId}' has no shipped symbol under '{kitDir}'.");

        var part = PCellKitSchematicParts.TryBuild("pcell-kit", generatorId, kitDir,
                                                   declaredParameters: null, out string? problem);
        Assert.Null(problem);
        Assert.NotNull(part);
        Assert.Equal(2, part!.Symbol.Pins.Count);
        Assert.Equal(2, part.Ccell.NumPorts);
        output.WriteLine($"{generatorId}: {path} — {part.Symbol.Primitives.Count} primitive(s)");
    }

    /// <summary>
    /// The published parameter interface is the GENERATOR's declaration, not the symbol's — the
    /// one-list rule the PDK authoring reference states. A length default arrives in SI metres and
    /// is written in the mm baseline a placement then rewrites to the workspace's own unit; a count
    /// is left exactly as declared, because circuitRF scales a length and never a count.
    /// </summary>
    [Fact]
    public void TheSchematicPartPublishesTheGeneratorsOwnParameters()
    {
        string workspace = ExampleRoot();
        using var provider = StartKit(workspace);
        var declared = provider.DeclaredParameters(Spiral)
            ?? throw new InvalidOperationException($"'{Spiral}' declared no parameters.");

        var part = PCellKitSchematicParts.TryBuild(
            "pcell-kit", Spiral, Path.Combine(workspace, "pcell-kit"), declared, out string? problem);
        Assert.Null(problem);
        Assert.NotNull(part);

        var names = part!.Ccell.Parameters.Select(p => p.Name).ToList();
        Assert.Equal(["Width", "Space", "Inner", "Turns", "Metal"], names);

        // 10 um, stated by the generator in SI metres, reaches the schematic as 0.01 mm — the same
        // physical width. Writing the SI number verbatim would put 1E-05 in a field with no unit.
        var width = part.Ccell.Parameters.Single(p => p.Name == "Width");
        Assert.Equal(UnitDimension.Length, width.Dimension);
        Assert.Equal("mm", width.Unit);
        Assert.Equal(0.01, double.Parse(width.DefaultExpression, System.Globalization.CultureInfo.InvariantCulture), 12);

        // A count is dimensionless. Scaled as a length it would come out a billion turns.
        var turns = part.Ccell.Parameters.Single(p => p.Name == "Turns");
        Assert.Equal(UnitDimension.None, turns.Dimension);
        Assert.Equal("", turns.Unit);
        Assert.Equal(3.0, double.Parse(turns.DefaultExpression, System.Globalization.CultureInfo.InvariantCulture), 12);

        // The dropdown the generator declares survives onto the schematic side as a closed set.
        var metal = part.Ccell.Parameters.Single(p => p.Name == "Metal");
        Assert.Equal(["Metal1", "Metal2"], metal.Choices);
        Assert.Equal("Metal1", metal.DefaultExpression);

        // Every one of them is ANNOTATED. A parametric cell is its parameters — a spiral is three
        // turns of 10 um metal — and a sheet that hides all five makes the reader click each part to
        // find out what the design is.
        Assert.All(part.Ccell.Parameters, p => Assert.True(p.ShowOnSchematic, $"'{p.Name}' is hidden"));
    }

    /// <summary>
    /// <b>The reported bug, end to end.</b> Open <c>SpiralInductor.clay</c>, run Update Schematic
    /// from Layout, and a component appears — with the coil's own parameters on it, in the
    /// technology's own unit.
    ///
    /// <para>Before the kit had a schematic side this placed NOTHING: the generator matched no part,
    /// so <c>PdkKitRegistry.Find</c> answered null and the run reported that the kit was not loaded.
    /// The schematic was created and left empty, which is exactly what was reported.</para>
    /// </summary>
    [Fact]
    public void UpdateSchematicFromLayout_PlacesTheCoil_WithItsParameters()
    {
        string copy = Path.Combine(Path.GetTempPath(), "crf-pcell-sch-" + Guid.NewGuid().ToString("N")[..8]);
        _scratch.Add(copy);
        CopyDirectory(ExampleRoot(), copy);
        try { Directory.Delete(Path.Combine(copy, GeneratedCellStore.ReservedFolderName), true); }
        catch (DirectoryNotFoundException) { /* already absent, which is a fresh clone's own state */ }

        PdkKitRegistry.ResetAllForTests();
        KitLayoutGenerators.ResetAllForTests();
        PCellRegistry.ClearResolvers();
        using var resolver = new PCellWorkerResolver(
            copy,
            findInterpreter: (_, _) => new PythonInterpreter(PythonRunner.Interpreter!, [], "test", "supplied by the test"),
            report: output.WriteLine);
        PCellRegistry.AddResolver(resolver);
        GeneratedCellsLifecycle.RegenerateAll(copy, _ => null, output.WriteLine);

        // What the workspace does on open: mount the schematic side of every parametric cell whose
        // kit ships a symbol, then publish the palette so the generator and the part are one tile.
        var kitNames = resolver.KitNameByGeneratorId;
        var kitDirs  = resolver.KitDirectoryByGeneratorId;
        var parts    = new List<PdkKitPart>();
        var tiles    = new List<PaletteItem>();
        foreach (var (gid, kitName) in kitNames)
        {
            var built = PCellKitSchematicParts.TryBuild(
                kitName, gid, kitDirs.GetValueOrDefault(gid), resolver.DeclaredParameters(gid), out _);
            if (built is null) continue;
            parts.Add(built);
            tiles.Add(PCellKitSchematicParts.PaletteItemFor(kitName, built));
        }
        Assert.Equal(2, parts.Count);

        string kit = kitNames[Spiral];
        PdkKitRegistry.SetPCellParts(copy, kit, parts);
        KitLayoutGenerators.Publish(copy, KitPaletteMerge.Compose(
            tiles, kitNames.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)));

        var layout = LayoutPersistence.LoadFromFile(
            Path.Combine(copy, "SpiralInductor", "layout", "SpiralInductor.clay"));
        string layoutDir = Path.Combine(copy, "SpiralInductor", "layout");

        var schematic = new SchematicEditModel
        {
            SchematicDirectory = Path.Combine(copy, "SpiralInductor", "schematic"),
        };
        Directory.CreateDirectory(schematic.SchematicDirectory);

        var result = LayoutToSchematicGenerator.Run(layout, schematic, layoutDir, Tech());
        foreach (var l in result.Lines) output.WriteLine($"{l.Severity}: {l.Text}");

        Assert.Equal(1, result.CreatedCount);
        Assert.NotNull(result.Command);
        result.Command!.Execute();

        var placed = Assert.Single(schematic.Components);
        Assert.Equal(PdkKitRegistry.RefFor(kit, Spiral), placed.CellRef);

        // The symbol the kit ships is what this instance renders as — two pins, not a blank box.
        var symbol = CellSymbolResolver.Resolve(placed.CellRef!, schematic.SchematicDirectory);
        Assert.Equal(CellSymbolState.Resolved, symbol.State);
        Assert.Equal(2, symbol.Symbol!.Pins.Count);

        // …and the parameters are the coil's own, in the technology's display unit. The .clay holds
        // Width = 1E-05 m; this workspace displays lengths in micrometres, so the schematic says 10.
        Assert.Equal(["Width", "Space", "Inner", "Turns", "Metal"],
                     placed.Parameters.Select(p => p.Name).ToList());
        var width = placed.Parameters.Single(p => p.Name == "Width");
        Assert.Equal("\u00b5m", width.Unit);   // the MMIC technology's own display unit
        Assert.Equal(10.0, double.Parse(width.Expression, System.Globalization.CultureInfo.InvariantCulture), 9);
        Assert.Equal("3", placed.Parameters.Single(p => p.Name == "Turns").Expression);
        Assert.Equal("Metal1", placed.Parameters.Single(p => p.Name == "Metal").Expression);
        Assert.All(placed.Parameters, p => Assert.True(p.ShowOnSchematic, $"'{p.Name}' is hidden"));
    }

    /// <summary>
    /// The two halves of a kit are replaced on different occasions and must not replace each other:
    /// a kit re-import rebuilds its own parts while its interpreters keep running, and a kit's
    /// scripts are re-read while its imported parts sit untouched. A part the kit ITSELF ships wins,
    /// because the kit has stated what that part is.
    /// </summary>
    [Fact]
    public void AnImportedPartWins_AndNeitherHalfDiscardsTheOther()
    {
        string root = Path.Combine(Path.GetTempPath(), "crf-pcell-mount-" + Guid.NewGuid().ToString("N")[..8]);
        _scratch.Add(root);
        Directory.CreateDirectory(root);

        PdkKitRegistry.ResetAllForTests();
        string kitDir = Path.Combine(ExampleRoot(), "pcell-kit");

        var spiral = PCellKitSchematicParts.TryBuild("k", Spiral, kitDir, null, out _)!;
        var mlin   = PCellKitSchematicParts.TryBuild("k", Mlin,   kitDir, null, out _)!;
        PdkKitRegistry.SetPCellParts(root, "k", [spiral, mlin]);
        Assert.True(PdkKitRegistry.HasKit(root, "k"));
        Assert.NotNull(PdkKitRegistry.Find(PdkKitRegistry.RefFor("k", Spiral), root));

        // The kit's own import lands, naming one of the same cells. Its part replaces the
        // synthesised one; the other synthesised part is untouched.
        var imported = new PdkKitPart(
            Spiral,
            new CircuitRF.Design.Symbol.Symbol([], [new CircuitRF.Design.Symbol.SymbolPin(0, 0, 0, "p")], 1),
            new CcellFile { NumPorts = 1 }, IconPath: null);
        PdkKitRegistry.SetKit(root, "k", [imported]);

        Assert.Equal(1, PdkKitRegistry.Find(PdkKitRegistry.RefFor("k", Spiral), root)!.Symbol.Pins.Count);
        Assert.Equal(2, PdkKitRegistry.Find(PdkKitRegistry.RefFor("k", Mlin),   root)!.Symbol.Pins.Count);

        // …and a later re-reading of the kit's scripts does not take the imported part back off.
        PdkKitRegistry.SetPCellParts(root, "k", [spiral, mlin]);
        Assert.Equal(1, PdkKitRegistry.Find(PdkKitRegistry.RefFor("k", Spiral), root)!.Symbol.Pins.Count);
        Assert.Equal(2, PdkKitRegistry.Find(PdkKitRegistry.RefFor("k", Mlin),   root)!.Symbol.Pins.Count);

        PdkKitRegistry.ResetAllForTests();
    }
}
