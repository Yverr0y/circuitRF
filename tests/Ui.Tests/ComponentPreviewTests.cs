using CircuitRF.Design.Layout.Interchange;
using CircuitRF.Ui;
using CircuitRF.Ui.Layout;
using CircuitRF.Ui.Schematic;
using CircuitRF.Ui.Views.Dialogs;

namespace CircuitRF.Ui.Tests;

/// <summary>
/// The import chooser's preview (owner, 2026-09-05).
///
/// <para><b>The one property worth gating is IDENTITY</b>, not appearance: the preview must be the
/// import's own conversion, so a user who accepts what the pane shows gets exactly that. Every test
/// here compares the preview's views against what <see cref="ComponentImport.Import"/> actually wrote
/// to disk from the same files — a rendered pixel is not the subject, and a picture that merely
/// resembles the import is the failure this exists to catch.</para>
///
/// <para>Fixtures are the same synthetic ones PL1/PL2 use (R-PL1-32).</para>
/// </summary>
// In CellStatGlobalsCollection because these tests call ComponentImport.Import, and CellFolder routes
// every filesystem call it makes through CellStat's PROCESS-GLOBAL counter. They assert nothing about
// that counter themselves — but SharedLibraryConcurrencyTests asserts an EXACT count, and a class
// making counted calls in parallel with it turns that assertion red with a statement about the
// scheduler ("expected 40, actual 42"). See CellStatGlobalsCollection's own note: adding classes to
// this assembly is exactly what has made this surface before.
[Collection(CellStatGlobalsCollection.Name)]
public class ComponentPreviewTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("component-preview-test-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const int Dbu = LayoutUnits.DefaultDbuPerMicron;

    private static string Fixture(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine([dir, "testdata", "component-samples", .. parts]);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException($"Fixture not found: {string.Join('/', parts)}");
    }

    private static ComponentCandidate Candidate(params (string[] Path, ComponentFileKind Kind)[] files)
        => new(ComponentCompleteness.SymbolFootprintAndMap, "test", "test",
               [.. files.Select(f => new ComponentFile(Fixture(f.Path), f.Kind))]);

    private static ComponentCandidate Widget9() => Candidate(
        (["widget9", "WIDGET9.kicad_sym"], ComponentFileKind.SymbolSexpr),
        (["widget9", "WIDGET9.kicad_mod"], ComponentFileKind.FootprintSexpr));

    /// <summary>The three density levels of one land pattern (R-PL1-25) — the case the pane's variant
    /// chooser exists for.</summary>
    private static ComponentCandidate Density() => Candidate(
        (["density", "PATTERN.kicad_sym"], ComponentFileKind.SymbolSexpr),
        (["density", "PATTERN-L.kicad_mod"], ComponentFileKind.FootprintSexpr),
        (["density", "PATTERN-M.kicad_mod"], ComponentFileKind.FootprintSexpr),
        (["density", "PATTERN.kicad_mod"], ComponentFileKind.FootprintSexpr));

    private ComponentImport.ImportResult Import(ComponentCandidate candidate, Technology? tech = null)
    {
        var read = ComponentRead.Read(candidate, Dbu);
        Assert.Null(read.Refusal);
        return ComponentImport.Import(read.Part!, _dir, tech, Dbu);
    }

    /// <summary>A shape and a pin as a string, so a mismatch names the layer and the coordinate rather
    /// than reporting that two object graphs differ.</summary>
    private static IEnumerable<string> Signature(LayoutView view)
    {
        foreach (var shape in view.Shapes)
        {
            var bb = LayoutGeometry.BboxOf(shape);
            yield return $"{shape.GetType().Name}@{shape.Layer}:{bb.MinX},{bb.MinY},{bb.MaxX},{bb.MaxY}";
        }
        foreach (var pin in view.Pins)
            yield return $"pin {pin.Name}@{pin.Layer}:{pin.X},{pin.Y},{pin.WidthDbu},{pin.OutwardDeg}";
    }

    // ── The identity gate ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What the preview draws is what the import writes — the same land pattern, shape for shape and
    /// pin for pin, on the same layer keys.
    /// </summary>
    [Fact]
    public void ThePreviewedLandPattern_IsTheOneTheImportWrites()
    {
        var tech = new Technology { Name = "preview" };

        var preview = ComponentPreview.Build(Widget9(), tech, Dbu);
        Assert.Null(preview.Refusal);

        var result = Import(Widget9(), tech);
        Assert.False(result.Cancelled);

        var written = LayoutPersistence.LoadFromFile(Path.Combine(
            CellFolder.SubFolderPath(result.CellDir!, ViewType.Layout),
            Path.GetFileName(Path.TrimEndingDirectorySeparator(result.CellDir!)) + ".clay"));

        var previewed = Assert.Single(preview.Layouts).View;
        Assert.Equal(Signature(written), Signature(previewed));
    }

    /// <summary>And the same symbol: the same primitives, and the same pin names on the same port
    /// indices, which is the numbering both views share (R-PL1-8).</summary>
    [Fact]
    public void ThePreviewedSymbol_IsTheOneTheImportWrites()
    {
        var preview = ComponentPreview.Build(Widget9(), null, Dbu);
        var result = Import(Widget9());

        var written = SymbolPersistence.LoadFromFile(Path.Combine(
            CellFolder.SubFolderPath(result.CellDir!, ViewType.Symbol),
            Path.GetFileName(Path.TrimEndingDirectorySeparator(result.CellDir!)) + ".csym"));

        Assert.NotNull(preview.Symbol);
        Assert.Equal(written.Primitives.Count, preview.Symbol!.Primitives.Count);
        Assert.Equal(
            written.Pins.Select(p => $"{p.Name}#{p.PortIndex}@{p.LocalX},{p.LocalY}"),
            preview.Symbol.Pins.Select(p => $"{p.Name}#{p.PortIndex}@{p.LocalX},{p.LocalY}"));
    }

    /// <summary>Every density level is previewable, in the order the import writes them as sibling
    /// views — so the chooser's variant list and the imported cell's views agree.</summary>
    [Fact]
    public void EveryDensityVariant_IsPreviewedInTheOrderTheImportWritesThem()
    {
        var preview = ComponentPreview.Build(Density(), null, Dbu);
        Assert.Equal(3, preview.Layouts.Count);

        var result = Import(Density());
        string cellName = Path.GetFileName(Path.TrimEndingDirectorySeparator(result.CellDir!));
        string layoutDir = CellFolder.SubFolderPath(result.CellDir!, ViewType.Layout);

        foreach (var layout in preview.Layouts)
        {
            var written = LayoutPersistence.LoadFromFile(
                Path.Combine(layoutDir, cellName + layout.Variant + ".clay"));
            Assert.Equal(Signature(written), Signature(layout.View));
        }

        // The nominal pattern is first, because it is the one that becomes PrimaryLayout.
        Assert.Equal("", preview.Layouts[0].Variant);
    }

    // ── A preview has no side effects ───────────────────────────────────────────────────────────

    /// <summary>
    /// Building a preview creates nothing and changes nothing. The destination technology in
    /// particular is the LIVE object every open document renders through — a preview that appended to
    /// it would repaint the workspace behind the dialog with layers nobody has agreed to import.
    /// </summary>
    [Fact]
    public void BuildingAPreview_WritesNothing_AndDoesNotTouchTheDestinationTechnology()
    {
        var tech = new Technology { Name = "live" };
        int layersBefore = tech.Layers.Count;

        var preview = ComponentPreview.Build(Widget9(), tech, Dbu);

        Assert.Null(preview.Refusal);
        Assert.Equal(layersBefore, tech.Layers.Count);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir));

        // …and the layers it would add are carried on the preview's OWN technology, which is what lets
        // a pad be drawn in the colour it will actually have rather than in the unknown-layer fallback.
        Assert.NotNull(preview.Technology);
        Assert.NotSame(tech, preview.Technology);
        Assert.True(preview.Technology!.Layers.Count > layersBefore);
    }

    // ── What the pictures cannot say, the summary does ──────────────────────────────────────────

    /// <summary>
    /// A part whose second gate and second package variant are not imported looks complete in a
    /// picture of its first one. The summary names both — that is exactly the mistaken import this
    /// preview exists to stop.
    /// </summary>
    [Fact]
    public void TheSummary_NamesTheSectionsAndVariantsThatWouldNotBeImported()
    {
        var preview = ComponentPreview.Build(
            Candidate((["xml", "XLIB4.lbr"], ComponentFileKind.LibraryXml)), null, Dbu);

        Assert.Null(preview.Refusal);
        Assert.Contains("G$2", preview.Summary);
        Assert.Contains("would NOT be imported", preview.Summary);
    }

    /// <summary>The ordinary summary counts the terminals, the land patterns and the symbol.</summary>
    [Fact]
    public void TheSummary_CountsWhatTheCandidateHolds()
    {
        var preview = ComponentPreview.Build(Widget9(), null, Dbu);

        Assert.Contains("9 terminal(s)", preview.Summary);
        Assert.Contains("1 land pattern", preview.Summary);
        Assert.Contains("1 symbol", preview.Summary);
    }

    // ── What the ROW says, and what the header says ─────────────────────────────────────────────

    /// <summary>
    /// A row leads with the candidate's own name, and carries nothing else but the folder.
    ///
    /// <para>The completeness and the format list are the same handful of phrases the whole way down
    /// a list — as row text they crowd out the one field that differs between two rows, so they
    /// belong beside the drawings instead (owner, 2026-09-05). The FOLDER stays in the row precisely
    /// because two candidates legitimately share a base name; it is what separates them.</para>
    /// </summary>
    [Fact]
    public void ARowLeadsWithTheName_AndTheCompositionIsSeparate()
    {
        var candidate = Widget9();

        Assert.Equal("WIDGET9", candidate.DisplayName);
        Assert.StartsWith("WIDGET9", candidate.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(candidate.Composition, candidate.ToString(), StringComparison.Ordinal);
        Assert.Contains(candidate.Description, candidate.Composition, StringComparison.Ordinal);
        Assert.Contains(candidate.FormatSummary, candidate.Composition, StringComparison.Ordinal);
    }

    /// <summary>
    /// The name comes off the file, never off a read — a scan classifies files rather than parsing
    /// them, and reading every candidate in a folder to label a list would be the whole import run
    /// dozens of times before the user has chosen anything. It is the same fallback
    /// <see cref="ComponentRead"/> itself uses, so the two agree by construction.
    /// </summary>
    [Fact]
    public void TheRowName_IsTheFilesOwn_AndNeedsNoRead()
    {
        var scan = ComponentFolderScan.Scan(Path.GetDirectoryName(Fixture("widget9", "WIDGET9.kicad_sym"))!);

        Assert.NotEmpty(scan.Candidates);
        Assert.All(scan.Candidates, c => Assert.NotEmpty(c.DisplayName));
        Assert.Contains(scan.Candidates, c => c.DisplayName == "WIDGET9");
    }

    /// <summary>
    /// And once a read HAS produced the declared name, the row says both (owner, 2026-09-05).
    ///
    /// <para>Neither name replaces the other, and the row is the reason: a format written as a fixed
    /// triple of files names every one of its parts the same thing, so the file's name alone
    /// identifies nothing — while the declared name alone cannot separate the several rows that hold
    /// the same part in different formats, which is the choice the list exists to offer. A part whose
    /// two names agree says it once, because a name repeated with a separator between reads as two
    /// parts.</para>
    /// </summary>
    [Fact]
    public void ARowSaysBothTheDeclaredNameAndTheFilesOwn_OnceTheyDiffer()
    {
        var candidate = Widget9();

        var row = new CandidateRow(candidate);
        Assert.Equal(candidate.DisplayName, row.Title);          // nothing read yet

        row.DeclaredName = "PART-9";
        Assert.Contains("PART-9", row.Title, StringComparison.Ordinal);
        Assert.Contains(candidate.DisplayName, row.Title, StringComparison.Ordinal);

        row.DeclaredName = candidate.DisplayName;
        Assert.Equal(candidate.DisplayName, row.Title);
    }

    /// <summary>A row raises <c>Title</c> as well as <c>DeclaredName</c>: the bound property is
    /// <c>Title</c>, and a row that changed silently would keep the file's name on screen until
    /// something else happened to rebuild the list.</summary>
    [Fact]
    public void ARowRaisesTitle_WhenTheDeclaredNameArrives()
    {
        var row = new CandidateRow(Widget9());
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.DeclaredName = "PART-9";
        Assert.Contains(nameof(CandidateRow.Title), raised);

        raised.Clear();
        row.DeclaredName = "PART-9";                              // same value: nothing to say
        Assert.Empty(raised);
    }

    // ── Framing: the drawing fills the pane and sits in the middle of it ────────────────────────

    /// <summary>
    /// Centred, scaled to the pane, and not clipped — the three things three rounds of reports were
    /// about, asserted on the PIXELS because that is the only form of the question that can come back
    /// false. Re-deriving the transform said it was right every time it was wrong.
    ///
    /// <para>The tolerances are tight ON PURPOSE. The defect that survived two fixes was a ~6%
    /// off-centre shift, which a 12% slack waved straight through: the fit reserved the widest pad
    /// name at the right-hand edge, so a part whose longest name is not its rightmost pad grew an
    /// empty margin on one side. 2% catches that; the fit is exact to within a pixel.</para>
    /// </summary>
    private static void AssertFramed((int MinX, int MinY, int MaxX, int MaxY, int Count) ink, int w, int h)
    {
        Assert.True(ink.Count > 0, "the pane is empty");

        double slackX = Math.Max(w * 0.02, 3), slackY = Math.Max(h * 0.02, 3);
        Assert.InRange((ink.MinX + ink.MaxX) / 2.0, w / 2.0 - slackX, w / 2.0 + slackX);
        Assert.InRange((ink.MinY + ink.MaxY) / 2.0, h / 2.0 - slackY, h / 2.0 + slackY);

        // The fit leaves 10% clear per side, so whichever axis it is fitted to spans ~80%. Under 70%
        // means the box being fitted is bigger than what gets drawn in it.
        double spanX = (ink.MaxX - ink.MinX) / (double)w, spanY = (ink.MaxY - ink.MinY) / (double)h;
        Assert.True(Math.Max(spanX, spanY) > 0.70,
                    $"drawing spans only {spanX:P0} x {spanY:P0} of the pane");

        // Nothing touches an edge: the pad identifiers and port labels are drawn in pixel space,
        // past the geometry, so a fit computed from the geometry alone runs them off the side.
        Assert.True(ink.MinX >= 1 && ink.MinY >= 1 && ink.MaxX <= w - 2 && ink.MaxY <= h - 2,
                    $"ink {ink.MinX},{ink.MinY}..{ink.MaxX},{ink.MaxY} touches the edge of {w}x{h}");
    }

    /// <summary>The bounding box of every pixel that is not the background, in pixels.</summary>
    private static (int MinX, int MinY, int MaxX, int MaxY, int Count) InkBox(SkiaSharp.SKBitmap bmp)
    {
        var background = bmp.GetPixel(0, 0);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, count = 0;
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
        {
            var p = bmp.GetPixel(x, y);
            if (p == background) continue;
            count++;
            if (x < minX) minX = x; if (x > maxX) maxX = x;
            if (y < minY) minY = y; if (y > maxY) maxY = y;
        }
        return (minX, minY, maxX, maxY, count);
    }

    /// <summary>
    /// The drawing is scaled to the pane and centred in it — measured from the PIXELS, because that is
    /// the only thing that answers it. Every earlier attempt to reason about the framing from the
    /// transform alone was consistent with a picture that was neither (owner, 2026-09-05).
    /// </summary>
    [Theory]
    [InlineData(400, 240)]
    [InlineData(240, 400)]   // a pane taller than it is wide fits on the OTHER axis
    public void TheFootprintDrawing_FillsThePane_AndIsCentredInIt(int w, int h)
    {
        SkiaFonts.TestOverrideTypeface = SkiaSharp.SKTypeface.Default;
        try
        {
            var preview = ComponentPreview.Build(Widget9(), null, Dbu);
            using var bmp = ComponentPreviewRaster.RasterFootprint(
                preview.Layouts[0].View, preview.Technology, LayoutRenderTheme.Light, w, h, 1.0);

            Assert.NotNull(bmp);
            var ink = InkBox(bmp!);
            Assert.True(ink.Count > 0, "the pane is empty");

            AssertFramed(ink, w, h);
        }
        finally { SkiaFonts.TestOverrideTypeface = null; }
    }

    /// <summary>The same, for the symbol half — a different renderer and a different handedness.</summary>
    [Fact]
    public void TheSymbolDrawing_FillsThePane_AndIsCentredInIt()
    {
        SkiaFonts.TestOverrideTypeface = SkiaSharp.SKTypeface.Default;
        try
        {
            var preview = ComponentPreview.Build(Widget9(), null, Dbu);
            using var bmp = ComponentPreviewRaster.RasterSymbol(
                preview.Symbol, SchematicRenderTheme.Light, 400, 240, 1.0);

            Assert.NotNull(bmp);
            var ink = InkBox(bmp!);
            Assert.True(ink.Count > 0, "the pane is empty");

            AssertFramed(ink, 400, 240);
        }
        finally { SkiaFonts.TestOverrideTypeface = null; }
    }

    // ── The list's search and type filter ───────────────────────────────────────────────────────

    /// <summary>A library of two parts, each written out in two formats — the shape that makes both
    /// the search and the type filter worth having, and the one a real download has.</summary>
    private string TwoPartsTwoFormats()
    {
        string root = Path.Combine(_dir, "library");
        void Put(string relative, string[] fixture)
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(Fixture(fixture), path);
        }

        Put("partA/sexpr/PART.kicad_sym", ["widget9", "WIDGET9.kicad_sym"]);
        Put("partA/sexpr/patterns/PART.kicad_mod", ["widget9", "WIDGET9.kicad_mod"]);
        Put("partB/sexpr/PART.kicad_sym", ["widget9", "WIDGET9.kicad_sym"]);
        Put("partB/sexpr/patterns/PART.kicad_mod", ["widget9", "WIDGET9.kicad_mod"]);
        foreach (var name in new[] { "PARTLIB.p", "PARTLIB.d", "PARTLIB.c" })
        {
            Put($"partA/records/{name}", ["pl2", "records", name]);
            Put($"partB/records/{name}", ["pl2", "records", name]);
        }
        return root;
    }

    /// <summary>
    /// The two fields the search matches, and why it is both: the row carries a name AND a folder,
    /// and the folder is in the row precisely because names repeat across a library. A search that
    /// matched only the name could not find "the one under partB" — and in this library every row is
    /// called PART or PARTLIB, so the folder is the ONLY thing that separates half of them.
    /// </summary>
    [Fact]
    public void TheSearch_MatchesTheNameAndTheFolder_CaseInsensitively()
    {
        var scan = ComponentFolderScan.Scan(TwoPartsTwoFormats());
        Assert.NotEmpty(scan.Candidates);

        static bool Matches(ComponentCandidate c, string term)
            => c.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || c.Location.Contains(term, StringComparison.OrdinalIgnoreCase);

        var byFolder = scan.Candidates.Where(c => Matches(c, "PARTB")).ToList();
        Assert.NotEmpty(byFolder);
        Assert.All(byFolder, c => Assert.Contains("partB", c.Location, StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(scan.Candidates.Count, byFolder.Count);   // it really narrowed

        var byName = scan.Candidates.Where(c => Matches(c, "partlib")).ToList();
        Assert.NotEmpty(byName);
        Assert.All(byName, c => Assert.Equal("PARTLIB", c.DisplayName));

        // An empty term is "no filter", never "match nothing".
        Assert.Equal(scan.Candidates.Count, scan.Candidates.Count(c => Matches(c, "")));
    }

    /// <summary>
    /// The type filter's categories come from what the scan HOLDS, and a category holding every row
    /// is not offered at all — a checkbox that filters nothing is a promise the list is not keeping,
    /// and a flyout of ten toggles where two of them do anything is worse than one of two.
    /// </summary>
    [Fact]
    public void TheTypeFilter_OffersOnlyCategoriesTheScanActuallyHolds()
    {
        var scan = ComponentFolderScan.Scan(TwoPartsTwoFormats());

        var families = scan.Candidates.Select(c => c.Family).Distinct().ToList();
        Assert.True(families.Count > 1, "this library holds two formats and the filter should say so");
        Assert.All(families, f => Assert.Contains(scan.Candidates, c => c.Family == f));

        // Selecting one format really does narrow the list rather than reordering it.
        foreach (var f in families)
            Assert.NotEqual(scan.Candidates.Count, scan.Candidates.Count(c => c.Family == f));

        // A single-format folder has nothing to narrow BY format, which is the case the panel drops
        // rather than showing one permanently-ticked box.
        var oneFormat = ComponentFolderScan.Scan(FixtureDir("widget9"));
        Assert.Single(oneFormat.Candidates.Select(c => c.Family).Distinct());
    }

    /// <summary>
    /// A row's Reveal target is the file its READ begins at, not the group's folder — because
    /// FileReveal highlights the file it is given, and a candidate is routinely several files in one
    /// directory. A group whose symbol sits one level above its land patterns (the ordinary shape of
    /// a downloaded part) reveals the symbol's folder, which is the one the row is named after.
    /// </summary>
    [Fact]
    public void TheRevealTarget_IsTheFileTheRowIsNamedAfter()
    {
        var scan = ComponentFolderScan.Scan(TwoPartsTwoFormats());

        foreach (var c in scan.Candidates)
        {
            var target = c.SymbolFile ?? c.FootprintFiles.FirstOrDefault() ?? c.Files.FirstOrDefault();
            Assert.NotNull(target);
            Assert.Equal(c.DisplayName, Path.GetFileNameWithoutExtension(target!.Path));

            // And it resolves to a real folder, which is what Reveal actually opens.
            Assert.NotNull(FileReveal.NearestExistingDirectory(target.Path));
        }
    }

    private static string FixtureDir(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "testdata", "component-samples", name);
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException($"Fixture folder not found: {name}");
    }


    // ── A candidate that cannot be read ─────────────────────────────────────────────────────────

    /// <summary>
    /// An unreadable candidate yields a REFUSAL, not an exception and not an empty pane. The dialog
    /// shows the sentence where the drawings would be; a preview runs over files circuitRF did not
    /// write, chosen by pointing at a folder, so this is the ordinary case rather than the exotic one.
    /// </summary>
    [Fact]
    public void AnUnreadableCandidate_Refuses_RatherThanThrowing()
    {
        string junk = Path.Combine(_dir, "NOTAPART.kicad_sym");
        File.WriteAllText(junk, "this is not a symbol library");

        var preview = ComponentPreview.Build(
            new ComponentCandidate(ComponentCompleteness.SymbolOnly, "junk", "junk",
                                   [new ComponentFile(junk, ComponentFileKind.SymbolSexpr)]),
            null, Dbu);

        Assert.NotNull(preview.Refusal);
        Assert.Null(preview.Symbol);
        Assert.Empty(preview.Layouts);
    }
}
