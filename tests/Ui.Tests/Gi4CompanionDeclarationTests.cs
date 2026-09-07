// Gate for docs/sonnet-briefs/brief-gi4-companion-declaration-files.md.
//
// A folder's own declarations: the plain-text parameter file stating the coordinate format the whole
// output job was written with, and the tool listing carrying the plating column a drill file most
// often omits. Both were classified "no Gerber or drill content in its head" and skipped, in the same
// run that reported, in words, that the digit format had been INFERRED.
//
// Fixtures are hand-authored, following L4e/L4f/L4g/GI1's precedent: worth less than a real set as a
// dialect test, costs nothing to redistribute, and names no tool, product or toolchain — which is
// also R-gi4-1's own rule, since the recognition is by KEYWORD and never by a filename convention.
//
// COUNTERS AND CONTENT ONLY. No wall-clock assertion anywhere in this file.

using System.Diagnostics;
using System.IO.Compression;

using CircuitRF.Design.Layout.Interchange;

namespace CircuitRF.Ui.Tests;

public class Gi4CompanionDeclarationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("gi4-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private const int Dbu = 1000;

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Two flashes, at 1 mm and 3 mm — the extent every drill fixture below lands inside, so
    /// the artwork cross-check never fires and never masks what is being measured.</summary>
    private const string Artwork =
        "%FSLAX46Y46*%\n%MOMM*%\n%ADD10C,0.400*%\nD10*\nX1000000Y1000000D03*\nX3000000Y3000000D03*\nM02*\n";

    /// <summary>A drill file that declares NOTHING about its format: no INCH/METRIC line, no
    /// ;FILE_FORMAT, no LZ/TZ word, and too few coordinate words for the width inference. The common
    /// case, and the one a declaration is for.</summary>
    private const string SilentDrill = "M48\nT1C0.300\n%\nG90\nG05\nT1\nX12000Y12000\nM30\n";

    /// <summary>The same file, stating its own format — 3:3, which the declaration below contradicts.</summary>
    private const string SelfDescribingDrill =
        "M48\n;FILE_FORMAT=3:3\nT1C0.300\n%\nG90\nG05\nT1\nX12000Y12000\nM30\n";

    /// <summary>Two tools, both used, and no statement of plating anywhere — the file a tool listing
    /// exists to complete.</summary>
    private const string TwoToolDrill =
        "M48\nMETRIC,LZ,000.000\nT1C0.300000\nT2C3.000000\n%\nG90\nG05\n" +
        "T1\nX001200Y001200\nT2\nX002000Y002000\nM30\n";

    /// <summary>R-gi4-1's <c>KEYWORD  VALUE</c> form: uppercase, hyphenated, one per line. The
    /// suppression flags name the zeros that are SUPPRESSED — the Gerber sense (R-gi4-4).</summary>
    private static string Parameters(
        string dataType = "DRILL", string units = "METRIC", int integers = 3, int decimals = 4,
        string leading = "NO", string trailing = "NO", string? scale = null) =>
        $"DATA-TYPE                 {dataType}\n" +
        $"UNITS                     {units}\n" +
        $"INTEGER-PLACES            {integers}\n" +
        $"DECIMAL-PLACES            {decimals}\n" +
        $"SUPPRESS-LEADING-ZEROS    {leading}\n" +
        $"SUPPRESS-TRAILING-ZEROS   {trailing}\n" +
        (scale is null ? "" : $"COORDINATE-SCALE          {scale}\n");

    private static string ToolListing(params (int Tool, string Diameter, string Plating)[] rows) =>
        "Tool   Size       Plating\n" +
        string.Concat(rows.Select(r => $"T{r.Tool:00}    {r.Diameter}   {r.Plating}\n"));

    private string Folder(string name)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(string dir, string fileName, string content) =>
        File.WriteAllText(Path.Combine(dir, fileName), content);

    private static IReadOnlyList<string> FilesIn(string dir) =>
        [.. Directory.EnumerateFiles(dir).OrderBy(p => p, StringComparer.Ordinal)];

    private GerberImport.ImportResult Import(
        string sourceDir, string name, GerberImport.OfferArchive? offerArchive = null, string? parent = null) =>
        GerberImport.Import(FilesIn(sourceDir), parent ?? _root, name, null, Dbu, null, null, null, offerArchive);

    private static Technology TechOf(GerberImport.ImportResult r) =>
        TechPersistence.LoadFromFile(r.TechPath!);

    private static GerberDeclaration Declaration(string text) =>
        GerberDeclarationFile.ReadParameters("/set/params.txt", text)!;

    private static ExcellonReadResult ReadDrill(string text, GerberDeclaration? declaration = null) =>
        ExcellonReader.Read(text, Dbu, null, declaration);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 1 — two keywords minimum
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AGenuineDeclarationIsAFourthFileKind_AndOneKeywordIsNot()
    {
        // The whole file, over text already in hand — ClassifyContent's own test shape, so nothing
        // here needs a temporary directory to assert what a byte stream is.
        var real = GerberFileClassifier.ClassifyContent("params", Parameters());
        Assert.Equal(GerberFileKind.Declaration, real.Kind);
        Assert.Equal(GerberDeclarationForm.Parameters, real.Form);

        // ONE recognized keyword is a coincidence in somebody's unrelated settings file, and the same
        // caution is already written into LooksLikeJobFile.
        var lonely = GerberFileClassifier.ClassifyContent("settings", "UNITS   METRIC\n");
        Assert.Equal(GerberFileKind.Other, lonely.Kind);

        // And a file of unrelated prose stays where it was.
        var prose = GerberFileClassifier.ClassifyContent(
            "readme", "This board was fabricated in two panels.\nSee the notes for the finish.\n");
        Assert.Equal(GerberFileKind.Other, prose.Kind);
    }

    [Fact]
    public void AToolListingIsTheSameKind_SeparatelyIdentified()
    {
        var listing = GerberFileClassifier.ClassifyContent(
            "tools", ToolListing((1, "0.300000", "PLATED"), (2, "3.000000", "NON-PLATED")));

        Assert.Equal(GerberFileKind.Declaration, listing.Kind);
        Assert.Equal(GerberDeclarationForm.ToolListing, listing.Form);

        // A single row is a sentence, not a table.
        var oneRow = GerberFileClassifier.ClassifyContent("tools", ToolListing((1, "0.300000", "PLATED")));
        Assert.Equal(GerberFileKind.Other, oneRow.Kind);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 2 — extension-blind, which is the classifier's own doctrine and the reason it exists
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClassificationIsByContent_NotByExtension()
    {
        // A declaration wearing a drill extension is still a declaration...
        Assert.Equal(GerberFileKind.Declaration,
                     GerberFileClassifier.ClassifyContent("board.drl", Parameters()).Kind);
        Assert.Equal(GerberFileKind.Declaration,
                     GerberFileClassifier.ClassifyContent("board.xln",
                         ToolListing((1, "0.300000", "PLATED"), (2, "3.000000", "NPTH"))).Kind);

        // ...and a drill file wearing whatever a declaration is conventionally called is still drill
        // data. This half is why the declaration test runs LAST in the cascade.
        Assert.Equal(GerberFileKind.Drill,
                     GerberFileClassifier.ClassifyContent("board.rep", SilentDrill).Kind);
        Assert.Equal(GerberFileKind.Drill,
                     GerberFileClassifier.ClassifyContent("board.txt", TwoToolDrill).Kind);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 3 — the inversion (R-gi4-4), all three conventions in one test
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ADeclarationsSuppressionFlagsAreTheGerberSense_TheOppositeOfExcellonsLzWord()
    {
        // (a) The declaration: "suppress leading zeros = YES" names the zeros SUPPRESSED.
        var declared = ExcellonFormat.Resolve(
            new DrillFormatDeclarations(), null,
            Declaration(Parameters(integers: 3, decimals: 3, leading: "YES", trailing: "NO")));
        Assert.Equal(GerberZeroOmission.Leading, declared.ZeroOmission);
        Assert.Equal(DrillFormatEvidence.Declaration, declared.ZeroOmissionEvidence);

        // (b) Gerber's %FSL names the same thing: leading zeros OMITTED. Read through the real
        // artwork reader, so this is the format's own behaviour and not a restatement of it — a
        // 2-digit word under 3:3 lands at 0.012 mm, which is only true of leading omission.
        var gerber = GerberReader.Read(
            new StringReader("%FSLAX33Y33*%\n%MOMM*%\n%ADD10C,0.100*%\nD10*\nX12Y12D03*\nM02*\n"), Dbu);
        var flash = Assert.IsType<CircleShape>(gerber.Shapes.Single().Shape);
        Assert.Equal(12_000, flash.Cx);                   // 0.012 mm = 12 µm, at 1000 DBU/µm
        Assert.Equal(12_000, flash.Cy);

        // (c) Excellon's LZ names the zeros KEPT, so it is the OPPOSITE of both of the above.
        var excellon = ReadDrill("M48\nMETRIC,LZ,000.000\nT1C0.300000\n%\nG90\nG05\nT1\nX12Y12\nM30\n");
        Assert.Equal(GerberZeroOmission.Trailing, excellon.Format.ZeroOmission);

        // The coordinate values that make the difference visible: ONE word, three digits of decimals,
        // read under the two conventions the three sources produce. These are the format's own
        // integers (the mantissa, before the unit is applied), so 12 is 0.012 mm and 120000 is 120 mm
        // — four orders of magnitude apart, from identical text.
        var asDeclared = new GerberCoordinateFormat(
            GerberUnit.Millimetres, 3, 3, declared.ZeroOmission, GerberNotation.Absolute, Dbu);
        var asExcellon = new GerberCoordinateFormat(
            GerberUnit.Millimetres, 3, 3, excellon.Format.ZeroOmission, GerberNotation.Absolute, Dbu);
        Assert.Equal(12, asDeclared.ParseCoordinateWord("12"));          // 0.012 mm — leading omitted
        Assert.Equal(120_000, asExcellon.ParseCoordinateWord("12"));     // 120 mm   — trailing omitted
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 4 — a declaration outranks every inference
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ADeclarationBeatsTheInference_AndIsReportedAsDeclaredRatherThanInferred()
    {
        var alone = ReadDrill(SilentDrill);
        Assert.Equal(DrillFormatEvidence.Defaulted, alone.Format.DigitsEvidence);
        Assert.Equal(12_000_000, alone.Hits.Single().X);              // 12 mm, at the 3:3 default

        var declared = ReadDrill(SilentDrill, Declaration(Parameters()));
        Assert.Equal(DrillFormatEvidence.Declaration, declared.Format.DigitsEvidence);
        Assert.Equal(DrillFormatEvidence.Declaration, declared.Format.UnitEvidence);
        Assert.Equal(3, declared.Format.IntegerDigits);
        Assert.Equal(4, declared.Format.DecimalDigits);
        Assert.Equal(1_200_000, declared.Hits.Single().X);            // 1.2 mm, at the declared 3:4

        // R-gi4-8: named, with what it settled — and never described as an inference.
        string evidence = string.Join(" ", declared.Format.Evidence);
        Assert.Contains("params.txt", evidence);
        Assert.DoesNotContain("INFERRED", evidence);
        Assert.False(declared.Format.RequiredAGuess);

        // Both flags off is a real, stated third answer, and the headline says so rather than naming a
        // convention the evidence says was never chosen (GI1 R-gi1-1).
        Assert.False(declared.Format.ZeroSuppressionApplies);
        Assert.Contains("neither zero convention applies", declared.Format.ToString());
    }

    [Fact]
    public void TheImportNamesTheDeclarationItRead()
    {
        string dir = Folder("declared");
        Write(dir, "top.gbr", Artwork);
        Write(dir, "board.drl", SilentDrill);
        Write(dir, "params.txt", Parameters());

        var result = Import(dir, "declared");

        string all = string.Join("\n", result.Messages);
        Assert.Contains("params.txt", all);
        Assert.DoesNotContain("Skipped params.txt", all);

        // R-gi4-9: the skipped list keeps naming what is genuinely skipped, and it got shorter.
        Assert.DoesNotContain("params.txt", result.SkippedFiles);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 5 — the file beats the declaration, and the disagreement is reported
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AFileThatSpeaksForItselfOutranksTheDeclaration_AndBothAreNamed()
    {
        var read = ReadDrill(SelfDescribingDrill, Declaration(Parameters()));

        Assert.Equal(DrillFormatEvidence.FormatComment, read.Format.DigitsEvidence);
        Assert.Equal(3, read.Format.DecimalDigits);
        Assert.Equal(12_000_000, read.Hits.Single().X);               // the FILE's 3:3, not the 3:4

        string evidence = string.Join(" ", read.Format.Evidence);
        Assert.Contains("3:3", evidence);
        Assert.Contains("3:4", evidence);
        Assert.Contains("DISAGREES", evidence);
        Assert.Contains("params.txt", evidence);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 6 — a scale factor is never silently ignored
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ANonUnityScaleIsRefused_ByName_AndNothingFromThatFileIsUsed()
    {
        var scaled = Declaration(Parameters(scale: "2.5"));
        Assert.NotNull(scaled.Refusal);
        Assert.Contains("2.5", scaled.Refusal);
        Assert.False(scaled.StatesAnything);

        // Refused means refused: the drill file falls back to what it can work out for itself.
        var read = ReadDrill(SilentDrill, scaled);
        Assert.Equal(DrillFormatEvidence.Defaulted, read.Format.DigitsEvidence);
        Assert.Equal(12_000_000, read.Hits.Single().X);

        // A scale of exactly 1 is not a refusal — it is the file confirming it did nothing.
        Assert.Null(Declaration(Parameters(scale: "1.0")).Refusal);

        string dir = Folder("scaled");
        Write(dir, "top.gbr", Artwork);
        Write(dir, "board.drl", SilentDrill);
        Write(dir, "params.txt", Parameters(scale: "2.5"));
        string all = string.Join("\n", Import(dir, "scaled").Messages);
        Assert.Contains("coordinate scale of 2.5", all);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 7 — plating from a listing, and only plating
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ATooListingSuppliesPlatingForExactlyTheToolsItNames()
    {
        var read = ReadDrill(TwoToolDrill);
        Assert.All(read.Hits, h => Assert.Null(h.Plated));

        var listing = GerberDeclarationFile.ReadToolListing(
            "/set/tools.txt", ToolListing((1, "0.300000", "PLATED"), (2, "3.000000", "NON-PLATED")))!;
        var plated = ExcellonReader.ApplyToolListing(read, listing, Dbu, out var notes);

        Assert.True(plated.Tools.Single(t => t.Number == 1).Plated);
        Assert.False(plated.Tools.Single(t => t.Number == 2).Plated);
        Assert.True(plated.Hits.Single(h => h.Tool == 1).Plated);
        Assert.False(plated.Hits.Single(h => h.Tool == 2).Plated);

        // Mixed, so there is no single answer for the LAYER — said out loud rather than picked.
        Assert.Null(plated.Plated);
        Assert.Contains(notes, n => n.Contains("some of this file's tools plated and others not", StringComparison.Ordinal));
    }

    [Fact]
    public void AListingThatMarksEveryToolNonPlated_MintsAViaEntryThatIsNotPlated()
    {
        string dir = Folder("nonplated");
        Write(dir, "top.gbr", Artwork);
        Write(dir, "holes.drl", TwoToolDrill);
        Write(dir, "holes-tools.txt", ToolListing((1, "0.300000", "NON-PLATED"), (2, "3.000000", "NPTH")));

        var result = Import(dir, "nonplated");
        var via = TechOf(result).Stackup.Layers.Single(l => l.Kind == StackupKind.Via);

        // GI1's field is where the answer lands, and the entry is no longer a conductor: a
        // millimetre-scale non-plated hole modelled as a plated barrel shorts every layer it passes.
        Assert.False(via.Plated);
        Assert.Null(via.Fill);
        Assert.Null(via.WallThicknessDbu);
        Assert.Contains("tool listing", string.Join("\n", result.Messages));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 8 — geometry is not taken from a listing
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AListingThatDisagreesAboutADiameterIsReported_AndTheDrillFileIsKept()
    {
        var read = ReadDrill(TwoToolDrill);
        long fromFile = read.Tools.Single(t => t.Number == 1).DiameterDbu;

        var listing = GerberDeclarationFile.ReadToolListing(
            "/set/tools.txt", ToolListing((1, "0.500000", "NON-PLATED"), (2, "3.000000", "PLATED")))!;
        var applied = ExcellonReader.ApplyToolListing(read, listing, Dbu, out var notes);

        Assert.Equal(fromFile, applied.Tools.Single(t => t.Number == 1).DiameterDbu);
        Assert.Equal("0.300000", applied.Tools.Single(t => t.Number == 1).DiameterText);

        // ...and the row that disagreed about the size did not get to state that tool's plating.
        Assert.Null(applied.Tools.Single(t => t.Number == 1).Plated);
        Assert.True(applied.Tools.Single(t => t.Number == 2).Plated);

        string said = string.Join("\n", notes);
        Assert.Contains("0.500000", said);
        Assert.Contains("0.300000", said);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 9 — scoping: neither kind's declaration reaches across the boundary
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AnArtworkDeclarationDoesNotAlterDrillReading()
    {
        string dir = Folder("artwork-scoped");
        Write(dir, "top.gbr", Artwork);
        Write(dir, "board.drl", SilentDrill);
        Write(dir, "params.txt", Parameters(dataType: "GERBER", units: "INCH", integers: 2, decimals: 5));

        string all = string.Join("\n", Import(dir, "artwork-scoped").Messages);

        // Read at the drill file's own default, exactly as it would be with no companion at all.
        Assert.Contains("mm 3:3", all);
        Assert.DoesNotContain("inch 2:5", all);

        // R-gi4-8: recognised and NOT used says why.
        Assert.Contains("It was not used", all);
        Assert.Contains("authoritative about itself", all);
    }

    [Fact]
    public void ADrillDeclarationDoesNotAlterArtworkReading()
    {
        string plain = Folder("artwork-only");
        Write(plain, "top.gbr", Artwork);

        string withDeclaration = Folder("artwork-plus-declaration");
        Write(withDeclaration, "top.gbr", Artwork);
        Write(withDeclaration, "params.txt", Parameters());

        var a = Import(plain, "same", parent: Folder("p1"));
        var b = Import(withDeclaration, "same", parent: Folder("p2"));

        Assert.Equal(ClayBytes(a), ClayBytes(b));
        Assert.Contains("no drill file in its folder was read", string.Join("\n", b.Messages));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 10 — the archive is an OFFER, never an action
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AFolderHoldingOnlyAnArchiveOffersToLookInside_AndOpensNothingUnasked()
    {
        string dir = ArchiveOnlyFolder("archive-declined");
        var before = TemporaryUnpackFolders();

        // No callback at all — the CLI's case before its flag, and every existing test's.
        var untouched = Import(dir, "archive-declined");
        Assert.True(untouched.Cancelled);
        Assert.Null(untouched.CellDir);
        Assert.Contains("board.zip", untouched.ArchiveCandidates.Select(Path.GetFileName));
        Assert.Contains("archive(s) sit beside it", string.Join("\n", untouched.Messages));

        // A callback that says no is the same outcome, and the folder is untouched either way.
        bool asked = false;
        var declined = Import(dir, "archive-declined-2", _ => { asked = true; return false; });
        Assert.True(asked);
        Assert.True(declined.Cancelled);

        Assert.Single(FilesIn(dir));                                   // nothing was unpacked into it
        Assert.Equal(before, TemporaryUnpackFolders());                // and nothing left behind
    }

    [Fact]
    public void SayingYesImportsFromTheArchiveAndLeavesNothingBehind()
    {
        string dir = ArchiveOnlyFolder("archive-accepted");
        var before = TemporaryUnpackFolders();

        var result = Import(dir, "archive-accepted", _ => true);

        Assert.False(result.Cancelled);
        Assert.NotNull(result.CellDir);
        Assert.NotEmpty(result.Layers);
        Assert.Contains("Looking inside board.zip", string.Join("\n", result.Messages));

        Assert.Single(FilesIn(dir));
        Assert.Equal(before, TemporaryUnpackFolders());
    }

    [Fact]
    public void AFolderThatAlreadyYieldsArtworkNeverTouchesTheArchiveBesideIt()
    {
        // R-gi4-11: reading both is how two versions of one board get silently merged.
        string dir = ArchiveOnlyFolder("archive-plus-artwork");
        Write(dir, "top.gbr", Artwork);

        bool asked = false;
        var result = Import(dir, "archive-plus-artwork", _ => { asked = true; return true; });

        Assert.False(asked);
        Assert.False(result.Cancelled);
        Assert.DoesNotContain("Looking inside", string.Join("\n", result.Messages));
        Assert.Contains("board.zip", result.SkippedFiles);
    }

    [Fact]
    public void HeadlessTheOfferIsARefusalNamingTheFlagThatAnswersIt()
    {
        string dir = ArchiveOnlyFolder("archive-cli");
        string target = Path.Combine(_root, "out-cli");

        var refused = RunCli("convert", dir, "-o", target, "--to", "clay",
                             "--accept-inferred-drill-format");
        Assert.NotEqual(0, refused.ExitCode);
        Assert.Contains("--open-archives", refused.StdErr);
        Assert.False(Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any());

        var opened = RunCli("convert", dir, "-o", Path.Combine(_root, "out-cli-2"), "--to", "clay",
                            "--accept-inferred-drill-format", "--open-archives");
        Assert.Equal(0, opened.ExitCode);
        Assert.Single(FilesIn(dir));
    }

    /// <summary>A folder whose only content is an archive holding a one-layer file set.</summary>
    private string ArchiveOnlyFolder(string name)
    {
        string staging = Folder(name + "-staging");
        Write(staging, "top.gbr", Artwork);
        Write(staging, "board.drl", SilentDrill);

        string dir = Folder(name);
        ZipFile.CreateFromDirectory(staging, Path.Combine(dir, "board.zip"));
        Directory.Delete(staging, recursive: true);
        return dir;
    }

    /// <summary>Every temporary unpack folder currently on disk. "Leave nothing behind" is a gate, so
    /// it is measured rather than assumed.</summary>
    private static IReadOnlyList<string> TemporaryUnpackFolders() =>
        [.. Directory.EnumerateDirectories(Path.GetTempPath(), "crf-gerber-zip-*").Order(StringComparer.Ordinal)];

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 11 — bit-identical when there is nothing to read
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ASetWithNoUsableDeclarationWritesExactlyTheSameDocuments()
    {
        // The comparison "against how it imports today" is made against the same set imported with
        // the whole declaration path INERT — a companion that is recognised and then refused, plus one
        // scoped to the other kind. If GI4 changed anything about a set it cannot speak for, these two
        // documents differ.
        string plain = Folder("plain");
        Write(plain, "top.gbr", Artwork);
        Write(plain, "board.drl", TwoToolDrill);

        string companioned = Folder("companioned");
        Write(companioned, "top.gbr", Artwork);
        Write(companioned, "board.drl", TwoToolDrill);
        Write(companioned, "scaled.txt", Parameters(scale: "4.0"));
        Write(companioned, "artwork.txt", Parameters(dataType: "GERBER", units: "INCH"));

        var a = Import(plain, "same", parent: Folder("q1"));
        var b = Import(companioned, "same", parent: Folder("q2"));

        Assert.Equal(ClayBytes(a), ClayBytes(b));
        Assert.Equal(File.ReadAllBytes(a.TechPath!), File.ReadAllBytes(b.TechPath!));
    }

    private static byte[] ClayBytes(GerberImport.ImportResult r) =>
        File.ReadAllBytes(Directory.GetFiles(r.CellDir!, "*.clay", SearchOption.AllDirectories).Single());

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Review follow-up — three ways a real folder went past the reader that was written for it
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>A drill file that writes every coordinate at its full field width, and states nothing
    /// else — the shape that makes a declaration's PARTIAL answer dangerous rather than merely
    /// incomplete.</summary>
    private const string FullWidthDrill =
        "M48\nMETRIC\nT1C0.600\n%\nG90\nT1\n" +
        "X0056999Y0318200\nX0064999Y0318200\nX0014999Y0231800\nX0027001Y0231800\nM30\n";

    /// <summary>
    /// <b>A declaration that settles the DIGITS and not the SUPPRESSION must not leave the file worse
    /// off than no declaration at all.</b>
    ///
    /// <para>The full-width rung used to be keyed on the digits having been settled BY the coordinate
    /// width. A declaration outranks that rung and supplies the digits itself, so it displaced the
    /// conclusion the coordinates still proved — and the suppression fell through to Defaulted, which
    /// is the value that raises a prompt and, headless, refuses the whole import. The file had
    /// settled itself; adding a companion that agreed with it broke it.</para>
    ///
    /// <para>The rung now keys on the FORMAT: the file's own words are at the resolved full width, so
    /// nothing is suppressed, whichever rung supplied the digit count.</para>
    /// </summary>
    [Fact]
    public void ADeclarationThatStatesOnlyTheDigits_LeavesTheFullWidthCoordinatesToSettleSuppression()
    {
        // No suppression line of any kind in the declaration — only the digit split and the unit.
        var digitsOnly = Declaration("UNITS  METRIC\nINTEGER-PLACES  3\nDECIMAL-PLACES  4\n");
        Assert.Null(digitsOnly.ZeroOmission);
        Assert.False(digitsOnly.ZeroSuppressionNone);

        var withDeclaration = ReadDrill(FullWidthDrill, digitsOnly);
        var without = ReadDrill(FullWidthDrill);

        // The declaration settled the digits…
        Assert.Equal(DrillFormatEvidence.Declaration, withDeclaration.Format.DigitsEvidence);
        Assert.Equal(3, withDeclaration.Format.IntegerDigits);
        Assert.Equal(4, withDeclaration.Format.DecimalDigits);

        // …and the coordinates still settle the suppression, exactly as they do on their own. NOT
        // Defaulted: that is the value that makes the import stop and ask about a file that left
        // nothing open.
        Assert.Equal(DrillFormatEvidence.CoordinateWidth, withDeclaration.Format.ZeroOmissionEvidence);
        Assert.Equal(without.Format.ZeroOmissionEvidence, withDeclaration.Format.ZeroOmissionEvidence);
        Assert.False(withDeclaration.Format.ZeroSuppressionApplies);
        Assert.DoesNotContain("suppress", withDeclaration.Format.ToString(), StringComparison.OrdinalIgnoreCase);

        // And the holes land in the same place either way, which is the point of the whole rung.
        Assert.Equal(without.Slots.Count, withDeclaration.Slots.Count);
    }

    /// <summary>
    /// <b>The suppression keywords are recognised CLIPPED as well as spelled out.</b> These tables are
    /// written to a fixed column width, so "LEADING" is routinely clipped to "LEAD". Missing the alias
    /// does not cost a keyword — it costs the one field the rung exists for, silently, while the file
    /// still counts as a recognised declaration on the strength of its other lines.
    /// </summary>
    [Theory]
    [InlineData("SUPPRESS-LEAD-ZEROES", "SUPPRESS-TRAIL-ZEROES")]
    [InlineData("SUPPRESS-LEADING-ZEROS", "SUPPRESS-TRAILING-ZEROS")]
    [InlineData("OMIT-LEAD-ZEROS", "OMIT-TRAIL-ZEROS")]
    public void TheClippedSuppressionKeywordsAreReadLikeTheSpelledOutOnes(string leading, string trailing)
    {
        // Both flags off is a STATED third answer — nothing is suppressed — not an absent one.
        var none = Declaration(
            $"UNITS  METRIC\nINTEGER-PLACES  3\nDECIMAL-PLACES  4\n{leading}  NO\n{trailing}  NO\n");
        Assert.True(none.ZeroSuppressionNone);
        Assert.Null(none.ZeroOmission);
        Assert.Contains("no zero suppression", none.Summary, StringComparison.Ordinal);

        // And the Gerber sense, NOT the Excellon inversion: the flag names the zeros SUPPRESSED.
        var lead = Declaration(
            $"UNITS  METRIC\nINTEGER-PLACES  3\nDECIMAL-PLACES  4\n{leading}  YES\n{trailing}  NO\n");
        Assert.Equal(GerberZeroOmission.Leading, lead.ZeroOmission);
        Assert.False(lead.ZeroSuppressionNone);
    }

    /// <summary>
    /// <b>An artwork parameter file that names its data through the OUTPUT DEVICE scopes itself, and
    /// stops colliding with the drill parameter file beside it.</b>
    ///
    /// <para>Two parameter files in one folder are compared, and two that disagree are both discarded
    /// — correct when they describe the same data, and wrong when they describe different kinds of it.
    /// An artwork file usually says which it is only by naming the device the job was written for, so
    /// without that alias it was Unstated, landed in the drill bucket, contradicted the drill file's
    /// own parameters and took both of them out of the run.</para>
    /// </summary>
    [Fact]
    public void AnArtworkParameterFileNamedByItsOutputDevice_DoesNotContradictTheDrillOne()
    {
        string dir = Folder("device-type");
        Write(dir, "top.gbr", Artwork);
        Write(dir, "board.drl", SilentDrill);
        Write(dir, "art_param.txt",
              "DEVICE-TYPE   GERBER_RS274X\nOUTPUT-UNITS  MM\nFORMAT  4.5\nSUPPRESS-LEAD-ZEROES  YES\n");
        Write(dir, "nc_param.txt", Parameters(dataType: "", units: "METRIC")
                                       .Replace("DATA-TYPE                 \n", ""));

        string all = string.Join("\n", Import(dir, "device-type").Messages);

        // The artwork file is recognised, scoped to the artwork, and set aside with the reason.
        Assert.Contains("art_param.txt", all, StringComparison.Ordinal);
        Assert.Contains("ARTWORK", all, StringComparison.Ordinal);

        // The two are NOT reported as contradicting each other…
        Assert.DoesNotContain("DIFFERENT coordinate formats", all, StringComparison.Ordinal);

        // …and the drill parameter file is the one that speaks for the drill data.
        var declaration = GerberCompanionFiles
            .Read([GerberFileClassifier.ClassifyContent(Path.Combine(dir, "nc_param.txt"),
                                                        File.ReadAllText(Path.Combine(dir, "nc_param.txt")))])
            .DrillDeclarationFor(Path.Combine(dir, "board.drl"));
        Assert.NotNull(declaration);
        Assert.Equal(3, declaration!.IntegerDigits);
        Assert.Equal(4, declaration.DecimalDigits);
    }

    /// <summary>"NC" is two letters and it is a WORD here, never a substring — it runs before the
    /// artwork markers, so a substring match scopes an artwork declaration at the drill data.</summary>
    [Fact]
    public void TheDrillMarkerNcIsAWordAndNotASubstring()
    {
        Assert.Equal(GerberDeclarationScope.Drill,
                     Declaration("DATA-TYPE  NC DRILL\nUNITS  METRIC\n").Scope);
        Assert.Equal(GerberDeclarationScope.Artwork,
                     Declaration("DATA-TYPE  ENCODED GERBER\nUNITS  METRIC\n").Scope);
    }

    // ── The CLI, as a process ─────────────────────────────────────────────────────────────────

    private (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory       = _root,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        psi.ArgumentList.Add(CliDll());
        foreach (string a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;
        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        proc.WaitForExit();
        return (proc.ExitCode, outTask.GetAwaiter().GetResult(), errTask.GetAwaiter().GetResult());
    }

    private static string CliDll()
    {
        string cliDir = System.Reflection.CustomAttributeExtensions
            .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(
                typeof(Gi4CompanionDeclarationTests).Assembly)
            .First(a => a.Key == "CliDir").Value!;
        return Path.GetFullPath(Path.Combine(cliDir, "CircuitRF.Cli.dll"));
    }
}
