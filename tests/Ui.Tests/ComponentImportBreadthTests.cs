using System.Text.RegularExpressions;
using CircuitRF.Core.Pdk;
using CircuitRF.Design.Layout.Interchange;
using CircuitRF.Ui.Layout;
using CircuitRF.Ui.Schematic;

namespace CircuitRF.Ui.Tests;

/// <summary>
/// Phase PL2's acceptance gates (docs/sonnet-briefs/brief-PL2-component-library-breadth.md §7).
///
/// <para>Every fixture under <c>testdata/component-samples/pl2/</c> is SYNTHETIC (R-PL2-19), authored
/// to each grammar with invented part and terminal names throughout. One part — <c>GIZMO4</c>, five
/// terminals, one of them the string-named <c>TPAD</c> — is expressed in all five grammars, which is
/// what makes the cross-format gates (10 and 11) mean anything.</para>
///
/// <para><b>The part is deliberately awkward in three ways</b>, each of which a naive reader gets
/// wrong while producing something that looks right: the symbol's drawing order is not the pad order
/// (so an ordinal join mis-wires it), the land pattern is asymmetric in Y (so a spurious Y flip is
/// visible), and one pad identifier is not a number (so parsing it as an integer drops it).</para>
///
/// <para>Counters only, never wall clock (gate 14).</para>
/// </summary>
// In CellStatGlobalsCollection: these tests call ComponentImport.Import, and CellFolder routes every
// filesystem call it makes through CellStat's PROCESS-GLOBAL counter. They assert nothing about that
// counter — but SharedLibraryConcurrencyTests asserts EXACT counts, and any class making counted calls
// beside it turns those assertions red with a statement about the scheduler rather than about the
// code. See CellStatGlobalsCollection's own note: adding classes to this assembly is exactly what has
// made this surface before, and it surfaced again when the preview tests were added.
[Collection(CellStatGlobalsCollection.Name)]
public class ComponentImportBreadthTests
{
    private const int Dbu = LayoutUnits.DefaultDbuPerMicron;

    private static string Dir(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine([dir, "testdata", "component-samples", "pl2", .. parts]);
            if (Directory.Exists(candidate) || File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException($"Fixture not found: {string.Join('/', parts)}");
    }

    private static ComponentPart Read(ComponentFormatFamily family, params string[] folder)
    {
        var scan = ComponentFolderScan.Scan(Dir(folder));
        var candidate = scan.Candidates.FirstOrDefault(c => c.Family == family);
        Assert.NotNull(candidate);

        var read = ComponentRead.Read(candidate, Dbu);
        Assert.Null(read.Refusal);
        Assert.NotNull(read.Part);
        return read.Part;
    }

    /// <summary>The joined terminal table — the invariant both views share (PL1 R-PL1-8).</summary>
    private static IReadOnlyList<ComponentTerminal> Terminals(ComponentPart part)
        => ComponentTerminals.Build(part, part.Footprints.FirstOrDefault()?.PadNames ?? []).Terminals;

    private static string MapOf(ComponentPart part)
        => string.Join(" ", Terminals(part).Select(t => $"{t.PadName}={t.PinName}"));

    /// <summary>What every format must agree on, and what an ordinal join gets wrong.</summary>
    private const string ExpectedMap = "1=ALPHA 2=BETA 3=GAMMA 4=DELTA TPAD=THERMAL";

    // ── Gate 2: one entry point ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// §5: PL2 adds no UI. Every format arrives through PL1's own <c>Import ▸ Component…</c> — its
    /// folder scan, its ranked chooser, its <c>ComponentRead</c>, its cell-folder output — and the way
    /// that is enforced is that the classifier is the ONLY thing widened.
    /// </summary>
    [Theory]
    [InlineData(ComponentFormatFamily.Records, "records")]
    [InlineData(ComponentFormatFamily.Hkp, "hkp")]
    [InlineData(ComponentFormatFamily.Plx, "plx")]
    [InlineData(ComponentFormatFamily.Cxf, "cxf")]
    [InlineData(ComponentFormatFamily.Script, "scr")]
    public void Gate2_EveryFormatAppearsInPl1sOwnRankedChooser(ComponentFormatFamily family, string folder)
    {
        var scan = ComponentFolderScan.Scan(Dir(folder));

        var candidate = Assert.Single(scan.Candidates, c => c.Family == family);
        Assert.Equal(ComponentCompleteness.SymbolFootprintAndMap, candidate.Completeness);
        Assert.NotEmpty(candidate.FormatSummary);
    }

    // ── Gate 3: PL1's invariants, per format ──────────────────────────────────────────────────────

    /// <summary>
    /// The pin↔pad map, read through each grammar's own spelling of it.
    ///
    /// <para><b>This is the gate that catches R-PL2-12 and its <c>.hkp</c> twin.</b> The symbol's
    /// drawing order is ALPHA, DELTA, BETA, GAMMA, THERMAL while the pad order is 1, 2, 3, 4, TPAD, so
    /// a reader that joins the two by position produces
    /// <c>1=ALPHA 2=DELTA 3=BETA 4=GAMMA TPAD=THERMAL</c> — fully populated, correctly shaped and
    /// wrongly wired. Nothing but this assertion tells the two apart.</para>
    /// </summary>
    [Theory]
    [InlineData(ComponentFormatFamily.Records, "records")]
    [InlineData(ComponentFormatFamily.Hkp, "hkp")]
    [InlineData(ComponentFormatFamily.Plx, "plx")]
    [InlineData(ComponentFormatFamily.Cxf, "cxf")]
    [InlineData(ComponentFormatFamily.Script, "scr")]
    public void Gate3a_ThePinPadMapSurvivesEveryGrammar(ComponentFormatFamily family, string folder)
        => Assert.Equal(ExpectedMap, MapOf(Read(family, folder)));

    /// <summary>
    /// R-PL1-9: a pad identifier is a STRING. <c>TPAD</c> is not a number, and a reader that parses
    /// pad identifiers as integers drops that terminal entirely — the part then imports with four
    /// terminals and no thermal connection, which looks like a complete part.
    /// </summary>
    [Theory]
    [InlineData(ComponentFormatFamily.Records, "records")]
    [InlineData(ComponentFormatFamily.Hkp, "hkp")]
    [InlineData(ComponentFormatFamily.Plx, "plx")]
    [InlineData(ComponentFormatFamily.Cxf, "cxf")]
    [InlineData(ComponentFormatFamily.Script, "scr")]
    public void Gate3b_AStringPadIdentifierIsATerminalLikeAnyOther(ComponentFormatFamily family, string folder)
    {
        var terminals = Terminals(Read(family, folder));

        var thermal = Assert.Single(terminals, t => t.PadName == "TPAD");
        Assert.Equal("THERMAL", thermal.PinName);

        // Numerals sort numerically and non-numeric identifiers come last (R-PL1-8), so the string
        // pad is terminal 5 rather than sorting between "1" and "2" as text.
        Assert.Equal(5, thermal.PortIndex);
    }

    /// <summary>
    /// <b>The footprint half does NOT flip Y, which is the opposite of PL1's rule</b>
    /// (ComponentArtwork's header). Every format in this phase is already +y up, while the board
    /// format PL1 reuses is +y down and negates.
    ///
    /// <para>The fixture's pads sit at +30 and +10 mil and NOWHERE below the axis, so a spurious flip
    /// moves every one of them — which a land pattern symmetric about its X axis could never show.</para>
    /// </summary>
    [Theory]
    [InlineData(ComponentFormatFamily.Records, "records")]
    [InlineData(ComponentFormatFamily.Hkp, "hkp")]
    [InlineData(ComponentFormatFamily.Plx, "plx")]
    [InlineData(ComponentFormatFamily.Cxf, "cxf")]
    [InlineData(ComponentFormatFamily.Script, "scr")]
    public void Gate3c_TheFootprintKeepsItsHandedness(ComponentFormatFamily family, string folder)
    {
        var cell = Read(family, folder).Footprints[0].Cell;

        var pad1 = Assert.Single(cell.Pins, p => p.Pin.Name == "1");
        Assert.Equal(-2032000, pad1.Pin.X);          // -80 mil
        Assert.Equal(+762000, pad1.Pin.Y);           // +30 mil, NOT -762000

        var pad4 = Assert.Single(cell.Pins, p => p.Pin.Name == "4");
        Assert.Equal(+2032000, pad4.Pin.X);
        Assert.Equal(+762000, pad4.Pin.Y);
    }

    /// <summary>
    /// The SYMBOL half is +y up too, and stays that way — <c>ComponentImport.FlipY</c> performs the
    /// <c>.csym</c> flip downstream (PL1 §3). A reader that flips here double-flips the symbol and it
    /// renders upside down beside a correct footprint.
    /// </summary>
    [Theory]
    [InlineData(ComponentFormatFamily.Records, "records")]
    [InlineData(ComponentFormatFamily.Hkp, "hkp")]
    [InlineData(ComponentFormatFamily.Plx, "plx")]
    [InlineData(ComponentFormatFamily.Cxf, "cxf")]
    [InlineData(ComponentFormatFamily.Script, "scr")]
    public void Gate3d_TheSymbolKeepsItsHandedness(ComponentFormatFamily family, string folder)
    {
        var symbol = Read(family, folder).Symbol;
        Assert.NotNull(symbol);

        // The body runs from +200 down to -800, so the drawing is asymmetric in Y and a flip shows.
        var second = symbol.Pins[1];
        Assert.Equal(0, second.XMil);
        Assert.Equal(-100, second.YMil);

        var fourth = symbol.Pins[3];
        Assert.Equal(1800, fourth.XMil);
        Assert.Equal(-300, fourth.YMil);
    }

    // ── The library's stated reference-designator prefix, in every grammar ────────────────────────

    /// <summary>
    /// Every one of these grammars states the letter its parts are numbered from, and each spells it
    /// differently — a <c>PREFIX</c> field, a <c>Prefix</c> attribute, a <c>RefPrefix</c> line, an
    /// unlabelled column of the part-type record. They all land on the one metadata key, which
    /// <c>ComponentImport</c> carries onto the cell as a read-only <c>Reference</c> parameter and the
    /// schematic reads to name a placed instance <c>U1</c> rather than <c>X1</c>.
    ///
    /// <para>Two of these read nothing at all before this test existed, on files that stated it
    /// plainly — the same part imported from two folders of one download got two different names.</para>
    /// </summary>
    [Theory]
    [InlineData(ComponentFormatFamily.Records, "records")]
    [InlineData(ComponentFormatFamily.Hkp, "hkp")]
    [InlineData(ComponentFormatFamily.Plx, "plx")]
    [InlineData(ComponentFormatFamily.Cxf, "cxf")]
    [InlineData(ComponentFormatFamily.Script, "scr")]
    public void EveryGrammarReadsTheSameStatedPrefix(ComponentFormatFamily family, string folder)
    {
        var part = Read(family, folder);
        Assert.True(part.Metadata.TryGetValue("Reference", out string? prefix),
            $"{family} read no Reference, though its fixture states one.");
        Assert.Equal("U", prefix);
    }

    // ── Gate 4: count-driven records (R-PL2-4) ────────────────────────────────────────────────────

    /// <summary>
    /// A declared vertex count the file cannot honour is a REFUSAL naming the entity — never a resync.
    /// A reader that scans for keywords instead of consuming by count reads straight past this and
    /// mis-associates every piece after it.
    /// </summary>
    [Fact]
    public void Gate4a_ADeclaredCountTheFileCannotHonourIsRefusedByEntity()
    {
        var result = ComponentRecordsReader.Read(
            File.ReadAllText(Dir("records", "PARTLIB.p")),
            File.ReadAllText(Dir("records-overrun", "PARTLIB.d")),
            File.ReadAllText(Dir("records", "PARTLIB.c")),
            Dbu);

        Assert.Null(result.Part);
        Assert.NotNull(result.Refusal);
        Assert.Contains("GIZMO4_LAND", result.Refusal);
        Assert.Contains("40 vertices", result.Refusal);
    }

    /// <summary>And the same decal, merely long, imports correctly — the refusal is about the COUNT
    /// disagreeing with the file, not about size.</summary>
    [Fact]
    public void Gate4b_ADecalThatIsMerelyLongImportsCorrectly()
    {
        var part = Read(ComponentFormatFamily.Records, "records");

        Assert.Equal(2, part.Footprints.Count);
        Assert.Equal(5, part.Footprints[0].PadNames.Count);
        Assert.Equal(ExpectedMap, MapOf(part));
    }

    // ── Gate 5: two grammars, one extension (R-PL2-6) ─────────────────────────────────────────────

    /// <summary>Both <c>.hkp</c> grammars import from the same folder, classified by CONTENT.</summary>
    [Fact]
    public void Gate5a_BothHkpGrammarsAreRecognisedInOneFolder()
    {
        var kinds = Directory.GetFiles(Dir("hkp"))
            .ToDictionary(f => Path.GetFileName(f), ComponentClassifier.Classify);

        Assert.Equal(ComponentFileKind.HkpParts, kinds["_Parts.hkp"]);
        Assert.Equal(ComponentFileKind.HkpCells, kinds["_Cells.hkp"]);
        Assert.Equal(ComponentFileKind.HkpPadstacks, kinds["_Pads.hkp"]);
        Assert.Equal(ComponentFileKind.HkpSymbols, kinds["_Symbols.hkp"]);
    }

    /// <summary>
    /// <b>Swapping the two files' names changes nothing.</b> The names are not part of any
    /// specification and have been observed to differ, so the dispatch is on the first non-comment
    /// character and never on the file name.
    /// </summary>
    [Fact]
    public void Gate5b_SwappingTheTwoGrammarsFileNamesChangesNothing()
    {
        string parts = File.ReadAllText(Dir("hkp", "_Parts.hkp"));
        string symbols = File.ReadAllText(Dir("hkp", "_Symbols.hkp"));

        // Classified from the bytes, with the names deliberately the wrong way round.
        Assert.Equal(HkpGrammar.Dotted, ComponentHkpReader.Grammar(parts));
        Assert.Equal(HkpGrammar.Starred, ComponentHkpReader.Grammar(symbols));

        var swapped = Directory.CreateTempSubdirectory("pl2-hkp-swap-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(swapped, "_Symbols.hkp"), parts);
            File.WriteAllText(Path.Combine(swapped, "_Parts.hkp"), symbols);
            File.Copy(Dir("hkp", "_Cells.hkp"), Path.Combine(swapped, "_Cells.hkp"));
            File.Copy(Dir("hkp", "_Pads.hkp"), Path.Combine(swapped, "_Pads.hkp"));

            var scan = ComponentFolderScan.Scan(swapped);
            var candidate = Assert.Single(scan.Candidates, c => c.Family == ComponentFormatFamily.Hkp);
            var read = ComponentRead.Read(candidate, Dbu);

            Assert.Null(read.Refusal);
            Assert.Equal(ExpectedMap, MapOf(read.Part!));
        }
        finally { Directory.Delete(swapped, recursive: true); }
    }

    // ── Gate 6: encrypted twins invisible (R-PL2-7) ───────────────────────────────────────────────

    /// <summary>
    /// A folder holding all eight <c>.hkp</c> files reports FOUR formats, not eight — and the four
    /// encrypted twins appear nowhere in the skipped summary either. Reporting both halves doubles the
    /// chooser's noise for no information, because the plaintext original sits right there.
    /// </summary>
    [Fact]
    public void Gate6_EncryptedTwinsAreInvisibleRatherThanUnreadable()
    {
        Assert.Equal(8, Directory.GetFiles(Dir("hkp"), "*.hkp").Length);

        var scan = ComponentFolderScan.Scan(Dir("hkp"));
        var candidate = Assert.Single(scan.Candidates, c => c.Family == ComponentFormatFamily.Hkp);

        Assert.Equal(4, candidate.Files.Count);
        Assert.All(candidate.Files, f => Assert.DoesNotContain("Encrypted", f.Name));

        // And not as "4 binary formats" in the skipped summary, which is the failure this rule names.
        Assert.DoesNotContain(scan.SkippedSummary, s => s.Contains("binary"));
    }

    // ── Gate 7: padstack dedupe (R-PL2-8) ─────────────────────────────────────────────────────────

    /// <summary>
    /// A cell fixture repeating one padstack nine times yields ONE. A repeat is not a redefinition
    /// conflict, and nine identical padstacks are not nine padstacks.
    /// </summary>
    [Fact]
    public void Gate7_ARepeatedPadstackDefinitionIsDeduplicated()
    {
        string pads = File.ReadAllText(Dir("hkp", "_Pads.hkp"));
        Assert.Equal(9, Regex.Matches(pads, @"^\.PAD ""RECTA""", RegexOptions.Multiline).Count);

        var part = Read(ComponentFormatFamily.Hkp, "hkp");

        var message = Assert.Single(part.Messages, m => m.Contains("repeats"));
        Assert.Contains("2 distinct pad(s) were kept", message);

        // The geometry survived the dedupe: every land is the one 60 x 10 definition.
        var cell = part.Footprints[0].Cell;
        var pad1 = Assert.Single(cell.Shapes, s => s.Shape.Pin == "1");
        var rect = Assert.IsType<RectShape>(pad1.Shape);
        Assert.Equal(ComponentFootprintBuilder.Mils(60, Dbu), rect.X2 - rect.X1);
        Assert.Equal(ComponentFootprintBuilder.Mils(10, Dbu), rect.Y2 - rect.Y1);
    }

    // ── Gate 8: all variants (R-PL2-9) ────────────────────────────────────────────────────────────

    /// <summary>
    /// A three-<c>PACKAGE_CELL</c> file yields ONE cell with THREE layout views. Every other format
    /// here states its density variants in separate files or separate blocks; this one puts all three
    /// in one file, and a reader that returns the first and stops loses two thirds of it with no error.
    /// </summary>
    [Fact]
    public void Gate8_AllDensityVariantsInOneCellFileBecomeSiblingViews()
    {
        var part = Read(ComponentFormatFamily.Hkp, "hkp");

        Assert.Equal(3, part.Footprints.Count);

        // The nominal pattern is first, so it becomes the primary view (R-PL1-25).
        Assert.Equal("", part.Footprints[0].Variant);
        Assert.Equal(["-L", "-M"], part.Footprints.Skip(1).Select(f => f.Variant).Order());

        // They are three views of ONE pattern: same name, same pads, different geometry.
        Assert.All(part.Footprints, f => Assert.Equal("GIZMO4_LAND", f.Name));
        Assert.All(part.Footprints, f => Assert.Equal(5, f.PadNames.Count));
        Assert.Equal(3, part.Footprints.Select(f => f.Cell.Pins[0].Pin.X).Distinct().Count());
    }

    // ── Gate 9: the second indirection (R-PL2-12) ─────────────────────────────────────────────────

    /// <summary>
    /// A <c>.PLX</c> whose <c>symPinNum</c> disagrees with its pad numbering imports <b>wired by the
    /// map</b>. Symbol pin 2 is pad 4, not pad 2.
    /// </summary>
    [Fact]
    public void Gate9a_SymPinNumIsFollowedRatherThanTheOrdinal()
    {
        var part = Read(ComponentFormatFamily.Plx, "plx");
        Assert.NotNull(part.Symbol);

        // The drawing's second pin sits at (0,-100) and belongs to pad 4 — an ordinal join would
        // hand it pad 2, and everything downstream would look entirely reasonable.
        var second = part.Symbol.Pins[1];
        Assert.Equal(-100, second.YMil);
        Assert.Equal("DELTA", second.Name);
        Assert.Equal("4", second.PadName);

        var fourth = part.Symbol.Pins[3];
        Assert.Equal("GAMMA", fourth.Name);
        Assert.Equal("3", fourth.PadName);

        Assert.Equal(ExpectedMap, MapOf(part));
    }

    /// <summary>
    /// And a fixture whose <c>compPin</c> and <c>padPinMap</c> contradict each other is REFUSED —
    /// the format states this map twice, and choosing one silently is exactly how this class of bug
    /// ships.
    /// </summary>
    [Fact]
    public void Gate9b_ContradictoryPadPinMapIsRefusedRatherThanPreferred()
    {
        var result = ComponentPlxReader.Read(File.ReadAllText(Dir("plx-contradictory", "Library.PLX")), Dbu);

        Assert.Null(result.Part);
        Assert.NotNull(result.Refusal);
        Assert.Contains("contradicts itself", result.Refusal);
        Assert.Contains("pad 2", result.Refusal);
    }

    // ── Gate 10: one reader, two extensions (R-PL2-10) ────────────────────────────────────────────

    /// <summary>
    /// The same content under both banners produces identical cells. The two extensions are one
    /// dialect and differ only in the first line, which is why they are one reader.
    /// </summary>
    [Fact]
    public void Gate10_BothBannersProduceTheSameCell()
    {
        var plx = ComponentPlxReader.Read(File.ReadAllText(Dir("plx", "Library.PLX")), Dbu);
        var dsl = ComponentPlxReader.Read(File.ReadAllText(Dir("dsl", "Library.DSL")), Dbu);

        Assert.Null(plx.Refusal);
        Assert.Null(dsl.Refusal);
        Assert.Equal(Describe(plx.Part!), Describe(dsl.Part!));
    }

    /// <summary>Everything a cell is made of, flattened to text, so two reads compare exactly rather
    /// than field by field.</summary>
    private static string Describe(ComponentPart part)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(part.Name);
        foreach (var pin in part.Symbol?.Pins ?? [])
            sb.AppendLine($"pin {pin.Name} {pin.PadName} {pin.XMil} {pin.YMil}");
        foreach (var footprint in part.Footprints)
        {
            sb.AppendLine($"fp {footprint.Name} {footprint.Variant}");
            foreach (var pin in footprint.Cell.Pins)
                sb.AppendLine($"  pad {pin.Pin.Name} {pin.Pin.X} {pin.Pin.Y} {pin.LayerName}");
            foreach (var shape in footprint.Cell.Shapes)
                sb.AppendLine($"  shape {shape.Shape.GetType().Name} {shape.LayerName} {shape.Shape.Pin}");
        }
        return sb.ToString();
    }

    // ── Gate 11: exact nanometre units (R-PL2-13) ─────────────────────────────────────────────────

    /// <summary>
    /// A <c>.cxf</c> pad and the same pad from a mil-stated format land on the IDENTICAL DBU
    /// coordinate, negative case included — asserted against another format rather than against this
    /// reader's own arithmetic.
    /// </summary>
    [Fact]
    public void Gate11_NanometresAndMilsLandOnTheSameDbu()
    {
        var fromNanometres = Read(ComponentFormatFamily.Cxf, "cxf").Footprints[0].Cell;
        var fromMils = Read(ComponentFormatFamily.Hkp, "hkp").Footprints[0].Cell;

        foreach (string pad in (string[])["1", "2", "3", "4", "TPAD"])
        {
            var a = Assert.Single(fromNanometres.Pins, p => p.Pin.Name == pad);
            var b = Assert.Single(fromMils.Pins, p => p.Pin.Name == pad);
            Assert.Equal(b.Pin.X, a.Pin.X);
            Assert.Equal(b.Pin.Y, a.Pin.Y);
        }

        // The negative side specifically: a cast would truncate toward zero and be wrong only here.
        Assert.Equal(-2032000, Assert.Single(fromNanometres.Pins, p => p.Pin.Name == "1").Pin.X);
    }

    /// <summary>R-PL2-14: an unmapped <c>FORM</c> is reported by number with a count and its pad is
    /// skipped — never guessed into a rectangle on the grounds that most pads are rectangles.</summary>
    [Fact]
    public void Gate11b_AnUnmappedPadFormIsReportedByNumberRatherThanGuessed()
    {
        var part = Read(ComponentFormatFamily.Cxf, "cxf");

        var message = Assert.Single(part.Messages, m => m.Contains("FORM="));
        Assert.Contains("FORM=9", message);
        Assert.DoesNotContain(part.Footprints[0].PadNames, p => p == "NOFORM");
    }

    // ── Gate 12: script state (R-PL2-15) ──────────────────────────────────────────────────────────

    /// <summary>
    /// A <c>.scr</c> fixture with interleaved layer changes puts each shape on its own layer.
    /// Collapsing the state machine — reading the <c>Wire</c> lines and ignoring the <c>Layer</c>
    /// lines between them — puts every shape on one layer, which this fails.
    /// </summary>
    [Fact]
    public void Gate12_InterleavedLayerCommandsAreReplayedRatherThanCollapsed()
    {
        var cell = Read(ComponentFormatFamily.Script, "scr").Footprints[0].Cell;

        var strokes = cell.Shapes.Where(s => s.Shape is PathShape).ToList();
        Assert.Equal(4, strokes.Count);

        // Two layers, alternating — the fixture interleaves them deliberately.
        Assert.Equal(2, strokes.Select(s => s.LayerName).Distinct().Count());
        Assert.Equal(2, strokes.Count(s => s.LayerName == "F.SilkS"));
        Assert.Equal(2, strokes.Count(s => s.LayerName == "F.Fab"));
    }

    /// <summary>The script restates its whole map once per land pattern it edits; that is one map,
    /// not three.</summary>
    [Fact]
    public void Gate12b_ARestatedMapIsOneMapRatherThanSeveral()
    {
        var part = Read(ComponentFormatFamily.Script, "scr");

        Assert.Equal(5, part.ConnectTable.Count);
        Assert.Equal(5, Terminals(part).Count);
    }

    // ── Gate 13: an unknown command refuses (R-PL2-17) ────────────────────────────────────────────

    /// <summary>
    /// A <c>.scr</c> carrying one unmodelled command is refused BY NAME and creates nothing.
    ///
    /// <para>This is the one place in PL1/PL2 where "report and continue" is wrong: an unknown command
    /// in a data format costs one skipped entity, but an unknown command in a script may have changed
    /// state that silently corrupts everything after it.</para>
    /// </summary>
    [Fact]
    public void Gate13_AnUnmodelledCommandRefusesByNameAndCreatesNothing()
    {
        var result = ComponentScrReader.Read(File.ReadAllText(Dir("scr-unknown", "Library.scr")), Dbu);

        Assert.Null(result.Part);
        Assert.NotNull(result.Refusal);
        Assert.Contains("Sculpt", result.Refusal);
        Assert.Contains("Nothing was imported", result.Refusal);
    }

    // ── Gate 15: naming (R-PL2-18) ────────────────────────────────────────────────────────────────

    /// <summary>
    /// R-PL2-18: every format is referred to by its EXTENSION and nothing else.
    ///
    /// <para><b>This is asserted structurally rather than with a list of forbidden names, because root
    /// <c>CLAUDE.md</c> forbids those names "not even as a glossery of names to filter out".</b> A
    /// scan that stores what it forbids is itself the leak — so the property checked is the one that
    /// makes a leak impossible: the banner constants these readers match on cannot spell a product
    /// name, because each begins with the separator that FOLLOWS one.</para>
    ///
    /// <para>Three of these formats open with <c>&lt;product&gt;-LIBRARY-PART-TYPES-V9</c> and the
    /// like. Matching the whole banner would put a commercial product's name in this repo as a string
    /// literal; matching from the separator onward is just as specific and carries nothing.</para>
    /// </summary>
    [Fact]
    public void Gate15a_BannerConstantsCannotSpellAProductName()
    {
        string[] constants =
        [
            ComponentRecordsReader.PartHeader,
            ComponentRecordsReader.DecalHeader,
            ComponentRecordsReader.SymbolHeader,
            ComponentPlxReader.PlxBanner,
            ComponentPlxReader.DslBanner,
        ];

        Assert.All(constants, c =>
        {
            Assert.NotEmpty(c);
            Assert.True(c[0] is '-' or '_',
                $"\"{c}\" must begin with the separator that follows the product word, so that no part " +
                "of a vendor's name can be spelled by this constant (R-PL2-18).");
        });
    }

    /// <summary>
    /// And the synthetic fixtures carry an INVENTED prefix in that same slot (R-PL2-19), so no fixture
    /// reproduces a real product's banner either. They still classify, which is the point: the reader
    /// never needed the product word.
    /// </summary>
    [Theory]
    [InlineData("records", "PARTLIB.p", ComponentFileKind.PartRecords)]
    [InlineData("records", "PARTLIB.d", ComponentFileKind.FootprintRecords)]
    [InlineData("records", "PARTLIB.c", ComponentFileKind.SymbolRecords)]
    [InlineData("plx", "Library.PLX", ComponentFileKind.PlxLibrary)]
    [InlineData("dsl", "Library.DSL", ComponentFileKind.PlxLibrary)]
    public void Gate15b_FixtureBannersAreInventedAndStillClassify(
        string folder, string file, ComponentFileKind expected)
    {
        string path = Dir(folder, file);
        string first = File.ReadLines(path).First().TrimStart('*');

        Assert.StartsWith("EXAMPLE", first, StringComparison.Ordinal);
        Assert.Equal(expected, ComponentClassifier.Classify(path));
    }

    /// <summary>
    /// Nothing this phase added names a format any way but by its extension: every mention of a format
    /// in these readers' own identifiers is one of the extensions <c>ReadableExtensions</c> lists.
    /// Comments stripped first — the <c>brief-harmonicarf-h8</c> lesson, that an unstripped scan passes
    /// on a comment it should have caught.
    /// </summary>
    [Fact]
    public void Gate15c_EveryFormatThisPhaseAddedIsNamedByItsExtension()
    {
        string[] added = [".p", ".d", ".c", ".hkp", ".plx", ".dsl", ".cxf", ".scr"];
        Assert.All(added, e => Assert.Contains(e, ComponentClassifier.ReadableExtensions));

        // The family names are shapes and extensions, never products.
        Assert.Equal(
            ["Pl1", "Records", "Hkp", "Plx", "Cxf", "Script"],
            Enum.GetNames<ComponentFormatFamily>());
    }


    // ── Gate 16: the pin's LEAD is drawn, in every grammar ────────────────────────────────────────

    /// <summary>
    /// <b>Every one of these formats states the stem from a pin's terminal in to the body, and every
    /// one states it differently</b> — as geometry of its own, as a length and a rotation on the pin,
    /// as a length WORD, or as a separate pin decal the terminal names. A reader that keeps only the
    /// terminal's point produces a symbol whose body has all its pins floating clear of it: not a
    /// refusal, not a warning, just a wrong drawing that looks deliberate.
    ///
    /// <para>So the gate is stated on the DRAWING and not on any format's own spelling: for each pin,
    /// a segment runs from that pin's point to the body edge beside it. GIZMO4's body is x = 300 to
    /// 1500 with pins at x = 0 and x = 1800, so the lead is 300 mils long in either direction and its
    /// far end is on the body.</para>
    /// </summary>
    [Theory]
    [InlineData(ComponentFormatFamily.Records, "records")]
    [InlineData(ComponentFormatFamily.Hkp, "hkp")]
    [InlineData(ComponentFormatFamily.Plx, "plx")]
    [InlineData(ComponentFormatFamily.Plx, "dsl")]
    [InlineData(ComponentFormatFamily.Cxf, "cxf")]
    [InlineData(ComponentFormatFamily.Script, "scr")]
    public void Gate16_EveryGrammarDrawsEachPinsLeadInToTheBody(ComponentFormatFamily family, string folder)
    {
        var drawing = Read(family, folder).Symbol;
        Assert.NotNull(drawing);
        Assert.Equal(5, drawing.Pins.Count);

        foreach (var pin in drawing.Pins)
        {
            double bodyEdge = pin.XMil < 900 ? 300 : 1500;
            Assert.True(
                drawing.Shapes.Any(sh => IsSegment(sh, pin.XMil, pin.YMil, bodyEdge, pin.YMil)),
                $"{folder}: pin \"{pin.Name}\" at ({pin.XMil},{pin.YMil}) has no lead drawn to the body " +
                $"edge at x={bodyEdge}. The file states one; nothing in the drawing carries it.");
        }
    }

    /// <summary>
    /// The one grammar whose lead is not on the pin at all: its terminal names a PIN DECAL defined
    /// elsewhere in the same file, and that decal's own geometry is the lead. Removing the definition
    /// leaves the pins where the file puts them, draws no lead, and SAYS so — a length is never
    /// invented, because the lead's length is what says where the body edge is.
    /// </summary>
    [Fact]
    public void Gate16b_ARecordsTerminalNamingAnUndefinedPinDecalIsReportedRatherThanGuessed()
    {
        string symbolText = File.ReadAllText(Dir("records", "PARTLIB.c"));
        int at = symbolText.IndexOf("PINDECAL 0", StringComparison.Ordinal);
        Assert.True(at > 0, "The fixture must define the pin decal for this test to remove it.");

        var result = ComponentRecordsReader.Read(
            File.ReadAllText(Dir("records", "PARTLIB.p")),
            File.ReadAllText(Dir("records", "PARTLIB.d")),
            symbolText[..at],
            Dbu);

        Assert.Null(result.Refusal);
        var drawing = result.Part!.Symbol!;

        Assert.Equal(5, drawing.Pins.Count);
        Assert.DoesNotContain(drawing.Shapes, sh => IsSegment(sh, 0, 0, 300, 0));
        Assert.Contains(result.Part.Messages, m => m.Contains("PINDECAL", StringComparison.Ordinal));
    }


    // ── Gate 17: the pin NAME's side, and the stroke widths (R-PL2-20, R-PL2-21) ─────────────────

    /// <summary>
    /// <b>Every grammar puts a pin's name on the BODY side of its terminal, and they must all agree
    /// about which side that is.</b> Two of them state the justification outright — one as a signed
    /// field, one as a <c>justify</c> word — and the rest fix it through the pin's own rotation. That
    /// makes this a real cross-check rather than a restatement: the derived answer is compared against
    /// the same part's stated answer, in the same assertion, over every grammar at once.
    ///
    /// <para>Getting it wrong is not subtle once seen and completely invisible until then: the whole
    /// right-hand column of names is drawn outward, away from the body, into empty space.</para>
    /// </summary>
    [Theory]
    [InlineData(ComponentFormatFamily.Records, "records")]
    [InlineData(ComponentFormatFamily.Hkp, "hkp")]
    [InlineData(ComponentFormatFamily.Plx, "plx")]
    [InlineData(ComponentFormatFamily.Plx, "dsl")]
    [InlineData(ComponentFormatFamily.Cxf, "cxf")]
    [InlineData(ComponentFormatFamily.Script, "scr")]
    public void Gate17_EveryGrammarPutsAPinsNameOnItsBodySide(ComponentFormatFamily family, string folder)
    {
        var drawing = Read(family, folder).Symbol;
        Assert.NotNull(drawing);

        // GIZMO4's body spans x = 300..1500 with pins at x = 0 and x = 1800.
        Assert.Equal(
            drawing.Pins.Select(p => p.XMil < 900 ? KitTextAlign.Left : KitTextAlign.Right),
            drawing.Pins.Select(p => p.NameAlign));
    }

    /// <summary>
    /// And it survives the conversion into circuitRF's own symbol, which is where it has to be true —
    /// a reader that reads the field correctly and a builder that drops it look identical from the
    /// reader's side.
    /// </summary>
    [Theory]
    [InlineData(ComponentFormatFamily.Hkp, "hkp")]
    [InlineData(ComponentFormatFamily.Plx, "plx")]
    [InlineData(ComponentFormatFamily.Script, "scr")]
    public void Gate17b_TheNamesSideReachesTheBuiltSymbol(ComponentFormatFamily family, string folder)
    {
        var part = Read(family, folder);
        var built = ComponentImport.Build(part, null, Dbu, []);
        Assert.NotNull(built?.Symbol);

        // Compared pin for pin against the DRAWING's own answer, in declaration order — the build
        // scales and snaps the coordinates, so a side re-derived from the built LocalX would be
        // testing this test's arithmetic rather than the import's.
        Assert.Contains(built.Symbol.Pins, p => p.NameAlign == SymbolPinNameAlign.Right);
        Assert.Equal(
            part.Symbol!.Pins.Select(p => p.NameAlign switch
            {
                KitTextAlign.Right  => SymbolPinNameAlign.Right,
                KitTextAlign.Center => SymbolPinNameAlign.Center,
                KitTextAlign.Top    => SymbolPinNameAlign.Top,
                KitTextAlign.Bottom => SymbolPinNameAlign.Bottom,
                _ => SymbolPinNameAlign.Left,
            }),
            built.Symbol.Pins.Select(p => p.NameAlign));
    }

    /// <summary>
    /// The stated stroke WIDTHS reach the drawing, and the two grammars that state a heavier lead than
    /// body produce exactly that contrast.
    ///
    /// <para>Asserted as an ORDER and never as a number: circuitRF has three stroke tiers and these
    /// files state a continuous width, in a different unit and at a different absolute scale per
    /// format, so the only thing that crosses the gap is which line the author drew heavier.</para>
    /// </summary>
    [Theory]
    [InlineData(ComponentFormatFamily.Records, "records")]
    [InlineData(ComponentFormatFamily.Hkp, "hkp")]
    public void Gate17c_ALeadDrawnHeavierThanItsBodyStaysHeavier(ComponentFormatFamily family, string folder)
    {
        var drawing = Read(family, folder).Symbol;
        Assert.NotNull(drawing);

        var leads = drawing.Shapes.Where(s => TouchesAPin(s, drawing)).Select(s => s.Width).ToList();
        var body = drawing.Shapes.Where(s => !TouchesAPin(s, drawing)).Select(s => s.Width).ToList();

        Assert.NotEmpty(leads);
        Assert.NotEmpty(body);
        Assert.All(body, w => Assert.True(w > 0, $"{folder}: a body piece states no width."));
        Assert.True(leads.Min() > body.Max(),
            $"{folder}: the file draws its leads at {leads.Min()} and its body at {body.Max()}; the " +
            "contrast the author drew did not survive the read.");

        var built = ComponentImport.Build(Read(family, folder), null, Dbu, []);
        var tiers = built!.Symbol!.Primitives.Select(TierOf).Distinct().ToList();
        Assert.Contains(SymbolStrokeTier.Normal, tiers);
        Assert.Contains(SymbolStrokeTier.Thick, tiers);
    }

    /// <summary>
    /// A grammar that states ONE width throughout draws one tier. Not a lesser case of the above — it
    /// is the common one, and a mapping that reached for a second tier here would put a contrast on
    /// screen that the file does not contain.
    /// </summary>
    [Theory]
    [InlineData(ComponentFormatFamily.Plx, "plx")]
    [InlineData(ComponentFormatFamily.Cxf, "cxf")]
    [InlineData(ComponentFormatFamily.Script, "scr")]
    public void Gate17d_OneStatedWidthIsOneTier(ComponentFormatFamily family, string folder)
    {
        var built = ComponentImport.Build(Read(family, folder), null, Dbu, []);
        Assert.NotNull(built?.Symbol);
        Assert.Equal([SymbolStrokeTier.Normal], built.Symbol.Primitives.Select(TierOf).Distinct());
    }

    private static bool TouchesAPin(KitSymbolShape shape, ComponentSymbolDrawing drawing)
        => drawing.Pins.Any(p => IsSegment(shape, p.XMil, p.YMil, p.XMil < 900 ? 300 : 1500, p.YMil));

    private static SymbolStrokeTier TierOf(SymbolPrimitive p) => p switch
    {
        LinePrimitive x => x.StrokeTier,
        PolylinePrimitive x => x.StrokeTier,
        PolygonPrimitive x => x.StrokeTier,
        RectPrimitive x => x.StrokeTier,
        ArcPrimitive x => x.StrokeTier,
        _ => SymbolStrokeTier.Normal,
    };

    /// <summary>Whether a shape is the two-point segment between those ends, either way round. The
    /// grammars land on <see cref="KitSymbolLine"/> or on a two-vertex <see cref="KitSymbolPath"/>
    /// depending on how the file spells a line, and which one is not what this gate is about.</summary>
    private static bool IsSegment(KitSymbolShape shape, double x1, double y1, double x2, double y2)
    {
        var (ax, ay, bx, by) = shape switch
        {
            KitSymbolLine l => (l.X1, l.Y1, l.X2, l.Y2),
            KitSymbolPath { Xy.Count: 4 } p => (p.Xy[0], p.Xy[1], p.Xy[2], p.Xy[3]),
            _ => (double.NaN, double.NaN, double.NaN, double.NaN),
        };

        return (Near(ax, x1) && Near(ay, y1) && Near(bx, x2) && Near(by, y2))
            || (Near(ax, x2) && Near(ay, y2) && Near(bx, x1) && Near(by, y1));

        static bool Near(double a, double b) => Math.Abs(a - b) < 0.5;
    }
}
