// ================================================================
//  CliStructuredOutputTests.cs — brief-automation-1-structured-output.md §6.
//
//  §6.1 IS THE TEST THAT MATTERS, and it is first in the file for that reason: the human output of
//  every verb, byte for byte, against bytes committed beside this file. R-aut1-1 made the console
//  table and the --json document share one selection step (LoadpullResultSummary); this is the test
//  that proves the sharing did not move a number. Everything below it checks the new flag; this one
//  checks that the OLD behaviour is exactly where it was.
//
//  THE GOLDENS CARRY PLACEHOLDERS, not paths. A netlist's resolved SnP reference and an explicit -o
//  are printed as absolute paths, and a repository must not carry anybody's home directory. So the
//  repo root and the test's temp directory are replaced with <ROOT> and <TMP> before comparing —
//  two substitutions, applied to both sides, and nothing else is normalised. A change to any other
//  character fails the test.
//
//  NOT tagged Benchmark, measured rather than assumed: the whole file's verbs run in about two
//  seconds together (lpp is the slowest at ~0.8 s). It belongs in the gate everybody runs.
// ================================================================

using System.Diagnostics;
using System.Text.Json;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests.Cli;

public sealed class CliStructuredOutputTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _tmp = Path.Combine(
        Path.GetTempPath(), "crf-clijson-" + Guid.NewGuid().ToString("N")[..12]);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    // ── §6.1 the one that matters ────────────────────────────────────────────

    [Theory]
    [InlineData("sparam", "sparam|testdata/Hero1/hero1.cnl|-o|<TMP>/hero1.s4p")]
    [InlineData("dc",     "dc|testdata/Hero2/hero2.cnl")]
    [InlineData("hb",     "hb|testdata/Hero2/hero2.cnl")]
    [InlineData("lp",     "lp|testdata/Hero3/hero3.cnl")]
    [InlineData("lpp",    "lpp|testdata/Hero3B/hero3B_at_compression.cnl|--out-grid|<TMP>/found.gam")]
    [InlineData("elab",   "elab|testdata/Hero1/hero1.cnl")]
    public void HumanOutput_IsByteIdenticalToTheCommittedGolden(string name, string argLine)
    {
        Directory.CreateDirectory(_tmp);
        var (_, stdout, stderr) = RunCli(Args(argLine));
        output.WriteLine(stderr);

        string golden = File.ReadAllText(GoldenPath(name));
        Assert.Equal(golden, Normalize(stdout));
    }

    /// <summary>
    /// <c>--all</c> does not change the loadpull SUMMARY, it adds cubes after it. Asserted as a
    /// property rather than as a second 620 kB golden file: dumping every cube of a 20-point grid at
    /// 32 drive steps is 3,600 lines, and committing that to gate a prefix would be paying a great
    /// deal for very little.
    /// </summary>
    [Fact]
    public void All_AddsCubesAfterTheLoadpullSummaryAndChangesNoneOfIt()
    {
        var (_, plain, _) = RunCli("lp", "testdata/Hero3/hero3.cnl");
        var (_, all,   _) = RunCli("lp", "testdata/Hero3/hero3.cnl", "--all");

        Assert.StartsWith(plain, all, StringComparison.Ordinal);
        Assert.True(all.Length > plain.Length, "--all printed no cubes");
    }

    // ── §6.2.1 every verb, every outcome, always a document ──────────────────

    [Theory]
    [InlineData("sparam|testdata/Hero1/hero1.cnl|-o|<TMP>/hero1.s4p", 0, "ok")]
    [InlineData("dc|testdata/Hero2/hero2.cnl",                        0, "ok")]
    [InlineData("hb|testdata/Hero2/hero2.cnl",                        0, "ok")]
    [InlineData("lp|testdata/Hero3/hero3.cnl",                        0, "ok")]
    [InlineData("lpp|testdata/Hero3B/hero3B_at_compression.cnl|--out-grid|<TMP>/found.gam", 0, "ok")]
    [InlineData("elab|testdata/Hero1/hero1.cnl",                      0, "ok")]
    [InlineData("hb|no/such/file.cnl",                                1, "failed")]
    [InlineData("hb|testdata/Hero1/hero1.cnl",                        1, "failed")]   // no HB chain
    [InlineData("convert|nope.dxf|-o|<TMP>/x.gds",                    1, "failed")]
    // The five verbs that produce no DataSet. `convert` above is only its REFUSAL, and a verb whose
    // document is only ever tested on the failing path is a verb whose successful document is not
    // tested at all — which is the half of "equal treatment" easiest to leave out.
    [InlineData("convert|testdata/pcb-samples/nets.kicad_pcb|-o|<TMP>/nets.gds", 0, "ok")]
    [InlineData("check|testdata/Hero1/hero1.cnl",                     0, "ok")]
    [InlineData("explain|testdata/Hero1/hero1.cnl",                   0, "ok")]
    [InlineData("read|testdata/Hero1/potentially_unstable_amp.s2p",   0, "ok")]
    [InlineData("read|testdata/Hero1/hero1.cnl",                      0, "ok")]
    public void Json_AlwaysParses_AndStatusAgreesWithTheExitCode(string argLine, int expectedExit, string status)
    {
        Directory.CreateDirectory(_tmp);
        var (exit, stdout, stderr) = RunCli([.. Args(argLine), "--json"]);
        output.WriteLine(stderr);

        var doc = JsonDocument.Parse(stdout).RootElement;

        Assert.Equal(expectedExit, exit);
        Assert.Equal(exit, doc.GetProperty("exitCode").GetInt32());
        Assert.Equal(status, doc.GetProperty("status").GetString());
        Assert.Equal(argLine.Split('|')[0], doc.GetProperty("circuitrf").GetProperty("verb").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.GetProperty("circuitrf").GetProperty("version").GetString()));
    }

    /// <summary>
    /// A run that RAN and did not converge is the third state, and it is not the same as a failure —
    /// <c>cli.md</c> §7's exit code 2, whose test is deliberately per-verb (R-aut-8). Driven with an
    /// iteration cap low enough that the solve cannot finish.
    /// </summary>
    [Fact]
    public void Json_ReportsNotConverged_Separately()
    {
        var (exit, stdout, stderr) = RunCli("hb", "testdata/Hero2/hero2.cnl", "--max-iter", "1", "--json");
        output.WriteLine(stderr);

        var doc = JsonDocument.Parse(stdout).RootElement;
        Assert.Equal(2, exit);
        Assert.Equal(2, doc.GetProperty("exitCode").GetInt32());
        Assert.Equal("not-converged", doc.GetProperty("status").GetString());
    }

    // ── §6.2.2 stdout holds nothing but the document ─────────────────────────

    /// <summary>
    /// Asserted by parsing the WHOLE of stdout and checking nothing follows the document, not by
    /// looking for a <c>{</c>. A stray <c>[circuitRF]</c> note or a "Wrote …" line before or after
    /// the JSON is exactly the failure this test exists to catch, and either would still leave a
    /// brace in the stream.
    /// </summary>
    [Theory]
    [InlineData("sparam|testdata/Hero1/hero1.cnl|-o|<TMP>/hero1.s4p")]
    [InlineData("hb|testdata/Hero2/hero2.cnl")]
    [InlineData("lp|testdata/Hero3/hero3.cnl")]
    [InlineData("elab|testdata/Hero1/hero1.cnl")]
    [InlineData("hb|no/such/file.cnl")]
    public void Json_StdoutHoldsTheDocumentAndNothingElse(string argLine)
    {
        Directory.CreateDirectory(_tmp);
        var (_, stdout, _) = RunCli([.. Args(argLine), "--json"]);

        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(stdout), new JsonReaderOptions());
        using (JsonDocument.ParseValue(ref reader)) { }
        Assert.False(reader.Read(), "something followed the document on stdout");

        // And nothing precedes it either: the very first non-whitespace character is the brace.
        Assert.Equal('{', stdout.TrimStart()[0]);
    }

    /// <summary>stderr is untouched by the flag — R-aut0-3, the whole change must be invisible to a
    /// script that watches stderr.</summary>
    [Fact]
    public void Json_LeavesStderrExactlyAsItWas()
    {
        var (_, _, without) = RunCli("lp", "testdata/Hero3/hero3.cnl");
        var (_, _, with)    = RunCli("lp", "testdata/Hero3/hero3.cnl", "--json");
        Assert.Equal(without, with);
    }

    // ── §6.2.3 --only / --group narrow `result` and nothing else ─────────────

    [Fact]
    public void OnlyAndGroup_NarrowResultAndNothingElse()
    {
        Directory.CreateDirectory(_tmp);
        string[] baseArgs = ["sparam", "testdata/Hero1/hero1.cnl", "-o", Path.Combine(_tmp, "hero1.s4p"), "--json"];

        var full     = JsonDocument.Parse(RunCli(baseArgs).StdOut).RootElement;
        var narrowed = JsonDocument.Parse(RunCli([.. baseArgs, "--only", "S"]).StdOut).RootElement;

        Assert.Equal(Raw(full, "status"),      Raw(narrowed, "status"));
        Assert.Equal(Raw(full, "exitCode"),    Raw(narrowed, "exitCode"));
        Assert.Equal(Raw(full, "outputs"),     Raw(narrowed, "outputs"));
        Assert.Equal(Raw(full, "diagnostics"), Raw(narrowed, "diagnostics"));

        var cubes = narrowed.GetProperty("result").GetProperty("groups").GetProperty("")
                            .EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["S"], cubes);
        Assert.True(
            full.GetProperty("result").GetProperty("groups").GetProperty("").EnumerateObject().Count() > 1,
            "the unnarrowed document should carry more than the one cube");
    }

    /// <summary>An unknown name is skipped silently on both sides, matching
    /// <c>DataSetSubset.SelectGroups</c>' existing behaviour rather than inventing a second rule.</summary>
    [Fact]
    public void UnknownNames_AreSkippedSilently()
    {
        Directory.CreateDirectory(_tmp);
        var (exit, stdout, _) = RunCli(
            "sparam", "testdata/Hero1/hero1.cnl", "-o", Path.Combine(_tmp, "hero1.s4p"),
            "--json", "--only", "NoSuchCube", "--group", "NoSuchGroup");

        Assert.Equal(0, exit);
        var doc = JsonDocument.Parse(stdout).RootElement;
        Assert.Equal("ok", doc.GetProperty("status").GetString());
        Assert.Empty(doc.GetProperty("result").GetProperty("groups").EnumerateObject());
    }

    /// <summary>R-aut1-5 — a loadpull's default document is the summary; <c>--all</c> adds the cubes.</summary>
    [Fact]
    public void Loadpull_DefaultsToTheSummary_AndAllAddsTheCubes()
    {
        var summaryOnly = JsonDocument.Parse(RunCli("lp", "testdata/Hero3/hero3.cnl", "--json").StdOut).RootElement;
        var withCubes   = JsonDocument.Parse(RunCli("lp", "testdata/Hero3/hero3.cnl", "--json", "--all").StdOut).RootElement;

        Assert.False(summaryOnly.GetProperty("result").TryGetProperty("groups", out _));
        Assert.True(withCubes.GetProperty("result").TryGetProperty("groups", out _));

        Assert.Equal(
            Raw(summaryOnly.GetProperty("result"), "summary"),
            Raw(withCubes.GetProperty("result"),   "summary"));
    }

    /// <summary>
    /// The summary the document carries and the table the terminal prints are the SAME selection
    /// (R-aut1-1) — so every row's printed figures must be the document's raw value times the scale
    /// the document states. This is the assertion that would fail if the two paths ever drifted.
    /// </summary>
    [Fact]
    public void LoadpullSummary_ReproducesThePrintedTableExactly()
    {
        string table = RunCli("lp", "testdata/Hero3/hero3.cnl").StdOut;
        var doc = JsonDocument.Parse(RunCli("lp", "testdata/Hero3/hero3.cnl", "--json").StdOut).RootElement;
        var grid = doc.GetProperty("result").GetProperty("summary").GetProperty("grid");

        var scale = grid.GetProperty("columns").EnumerateArray()
            .ToDictionary(c => c.GetProperty("column").GetString()!, c => c.GetProperty("consoleScale").GetDouble());

        foreach (var row in grid.GetProperty("rows").EnumerateArray())
        {
            int index = row.GetProperty("index").GetInt32();
            var fom   = row.GetProperty("fom");

            // The console's own row for this point — the columns after the "stop" word.
            string line = table.Split('\n').Single(l => l.TrimStart().StartsWith($"{index}  ", StringComparison.Ordinal));

            foreach (string column in (string[])["Pavl", "Pout", "Gt", "Efficiency", "PAE"])
            {
                double raw = fom.GetProperty(column).GetDouble();
                string printed = (raw * scale[column]).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                Assert.Contains(printed, line);
            }
        }
    }

    // ── §6.2.4 a complex cube survives, both parts ───────────────────────────

    [Fact]
    public void ComplexCube_RoundTripsWithBothParts_AgainstTheVerbsOwnExport()
    {
        Directory.CreateDirectory(_tmp);
        string npy = Path.Combine(_tmp, "hero2.npy");

        var (exit, stdout, stderr) = RunCli("hb", "testdata/Hero2/hero2.cnl", "-o", npy, "--json");
        output.WriteLine(stderr);
        Assert.Equal(0, exit);

        var groups = JsonDocument.Parse(stdout).RootElement.GetProperty("result").GetProperty("groups");
        var v = groups.GetProperty("").GetProperty("V");
        Assert.Equal("complex", v.GetProperty("kind").GetString());

        var fromJson = v.GetProperty("values").EnumerateArray()
            .Select(z => new System.Numerics.Complex(z[0].GetDouble(), z[1].GetDouble())).ToArray();

        var back = RfCore.Export.DataSetImporter.Import(npy).DataSet;
        var cube = back.Cubes["V"];

        Assert.Equal(cube.ComplexValues.Length, fromJson.Length);
        for (int i = 0; i < fromJson.Length; i++)
        {
            Assert.Equal(cube.ComplexValues[i].Real,      fromJson[i].Real);
            Assert.Equal(cube.ComplexValues[i].Imaginary, fromJson[i].Imaginary);
        }
        // Not a cube of real numbers dressed up as pairs — the imaginary part has to actually be there.
        Assert.Contains(fromJson, z => z.Imaginary != 0.0);
    }

    // ── §6.2.5 culture invariance ────────────────────────────────────────────

    /// <summary>
    /// A comma-decimal culture must not reach the document. <c>cli.md</c> §7A's rule is that every
    /// machine-readable surface is invariant, and a JSON number written as <c>2,5</c> is not merely
    /// mis-formatted — it is a syntax error a caller cannot parse.
    /// </summary>
    [Fact]
    public void Json_IsByteIdenticalUnderACommaDecimalCulture()
    {
        var invariant = Launch(culture: null,    "lp", "testdata/Hero3/hero3.cnl", "--json").StdOut;
        var german    = Launch(culture: "de_DE", "lp", "testdata/Hero3/hero3.cnl", "--json").StdOut;
        Assert.Equal(invariant, german);
    }

    // ── §6.2.6 diagnostic ids are stable and unique ──────────────────────────

    /// <summary>
    /// Every id this brief introduces, read out of <c>CliDiagnostics.cs</c> itself, asserted against
    /// the committed list. R-aut1-8: an id is a permanent contract, so this test exists to make an
    /// accidental change show up here rather than as a silently broken caller. Add an id and this
    /// test tells you to record it; RENAME one and it tells you that you have made a new diagnostic.
    ///
    /// <para>Read from source rather than by reflection because <c>CircuitRF.Cli</c> is referenced by
    /// this assembly with <c>ReferenceOutputAssembly="false"</c> — it is launched as a process, not
    /// linked — so its types are not available to bind against here.</para>
    /// </summary>
    [Fact]
    public void DiagnosticIds_AreTheCommittedSet_UniqueAndCaseDistinct()
    {
        string[] ids = DeclaredCliDiagnosticIds();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (string id in ids)
            Assert.Matches(@"^[a-z][a-z0-9]*(\.[a-z0-9]+(-[a-z0-9]+)*)+$", id);

        Assert.Equal<IEnumerable<string>>(ExpectedIds, [.. ids.OrderBy(x => x, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// The ids a caller can actually provoke from a command line, asserted end to end through the
    /// real document rather than through the source — the source list above says what exists, this
    /// says what arrives.
    /// </summary>
    [Theory]
    [InlineData("cli.input.not-found",                  "hb|no/such/file.cnl")]
    [InlineData("cli.args.input-required",              "hb")]
    [InlineData("cli.args.set-malformed",               "hb|testdata/Hero2/hero2.cnl|--set|nonsense")]
    [InlineData("cli.args.pin-malformed",               "lp|testdata/Hero3/hero3.cnl|--pin|nonsense")]
    [InlineData("cli.args.grid-not-for-pursuit",        "lpp|testdata/Hero3B/hero3B_at_compression.cnl|--grid|g.gam")]
    [InlineData("cli.args.out-grid-not-for-loadpull",   "lp|testdata/Hero3/hero3.cnl|--out-grid|g.gam")]
    // The five run verbs refuse an unrecognised option rather than dropping it — and, for the four
    // that find their input by "the first token that does not start with a dash", rather than taking
    // its VALUE as the input path (docs/design/cli.md §3.3). Both spellings are covered: the loop
    // form, and dc's own scan.
    [InlineData("cli.args.unknown-option",              "lp|testdata/Hero3/hero3.cnl|--maxmix|3")]
    [InlineData("cli.args.unknown-option",              "sparam|testdata/Hero1/hero1.cnl|-a|AC1")]
    [InlineData("cli.args.unknown-option",              "dc|testdata/Hero1/hero1.cnl|--set|Vg=1")]
    [InlineData("cli.args.multiple-inputs",             "dc|testdata/Hero1/hero1.cnl|second.cnl")]
    [InlineData("cli.analysis.not-selected",            "hb|testdata/Hero1/hero1.cnl")]
    [InlineData("convert.input.not-found",              "convert|nope.dxf|-o|out.gds")]
    [InlineData("convert.args.unknown-format",          "convert|x.dxf|--from|wat")]
    [InlineData("convert.args.dxf-version",             "convert|x.dxf|--dxf-version|AC9999")]
    [InlineData("convert.args.drill-units",             "convert|x.dxf|--drill-units|furlong")]
    [InlineData("convert.args.drill-zeros",             "convert|x.dxf|--drill-zeros|sideways")]
    [InlineData("convert.args.drill-format",            "convert|x.dxf|--drill-format|2")]
    public void RefusalsArriveWithTheirId(string id, string argLine)
    {
        var (_, stdout, _) = RunCli([.. Args(argLine), "--json"]);
        var ids = JsonDocument.Parse(stdout).RootElement.GetProperty("diagnostics")
            .EnumerateArray().Select(d => d.GetProperty("id").GetString()).ToArray();
        Assert.Contains(id, ids);
    }

    /// <summary>Every diagnostic carries its English sentence — <c>cli.md</c> §7A holds inside the
    /// JSON exactly as it holds on stderr — and the sentence is the one stderr got.</summary>
    [Fact]
    public void EveryDiagnostic_CarriesTheSentenceStderrPrinted()
    {
        var (_, stdout, stderr) = RunCli("hb", "no/such/file.cnl", "--json");
        var d = JsonDocument.Parse(stdout).RootElement.GetProperty("diagnostics")[0];

        Assert.Equal("File not found: no/such/file.cnl", d.GetProperty("message").GetString());
        Assert.Equal("error", d.GetProperty("severity").GetString());
        Assert.Equal("no/such/file.cnl", d.GetProperty("arguments").GetProperty("path").GetString());
        Assert.Contains("File not found: no/such/file.cnl", stderr);
    }

    // ── outputs ──────────────────────────────────────────────────────────────

    [Fact]
    public void Outputs_NameEveryFileTheRunWrote()
    {
        Directory.CreateDirectory(_tmp);
        string snp = Path.Combine(_tmp, "hero1.s4p");
        var (_, stdout, _) = RunCli("sparam", "testdata/Hero1/hero1.cnl", "-o", snp, "--json");

        var outputs = JsonDocument.Parse(stdout).RootElement.GetProperty("outputs").EnumerateArray()
            .Select(o => (o.GetProperty("kind").GetString(), o.GetProperty("path").GetString())).ToArray();

        Assert.Equal([("touchstone", snp)], outputs);
        Assert.True(File.Exists(snp));
    }

    /// <summary>The chain that ACTUALLY ran, so a caller that asked for an inner analysis and got its
    /// wrapper can see it (cli.md §4).</summary>
    [Fact]
    public void Input_RecordsTheChainThatRan()
    {
        var doc = JsonDocument.Parse(RunCli("lp", "testdata/Hero3/hero3.cnl", "--json").StdOut).RootElement;
        Assert.Equal("testdata/Hero3/hero3.cnl", doc.GetProperty("input").GetProperty("path").GetString());
        Assert.Equal("LP1", doc.GetProperty("input").GetProperty("analysis").GetString());
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    /// <summary>The ids declared in <c>CliDiagnostics.cs</c>: the first string literal of each
    /// factory, which the file's own layout puts alone on a line ending in a comma.</summary>
    private static string[] DeclaredCliDiagnosticIds()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Cli", "CliDiagnostics.cs"));
        // Anchored on what ALWAYS follows an id — the severity — rather than on line layout, which
        // the single-line factories do not share with the multi-line ones.
        return [.. System.Text.RegularExpressions.Regex
            .Matches(src, "\"([a-z][a-z0-9.\\-]*)\",\\s*DiagnosticSeverity\\.")
            .Select(m => m.Groups[1].Value)];
    }

    private static readonly string[] ExpectedIds =
    [
        // RC-5's batch, on `serve` (revision-control.md §5.3a, §5.3b). These are the only ids in this
        // set that belong to a tool rather than to a verb: a batch holds session state and there is
        // no command line that could carry it.
        "batch.closed",
        "batch.improvise-nothing",
        "batch.rule",
        "batch.state",
        "batch.was-not-open",
        "check.analysis.none",
        "check.args.multiple-paths",
        "check.args.path-required",
        "check.args.unknown-option",
        "check.args.unknown-severity",
        "check.cell.no-primary",
        "check.cell.primary-missing",
        "check.drc.note",
        "check.elaboration.failed",
        "check.elaboration.note",
        "check.elaboration.warning",
        "check.em.unresolved",
        "check.file.unreadable",
        "check.name.invalid",
        "check.path.interchange",
        "check.path.not-found",
        "check.path.unknown-kind",
        "check.ref.kit-not-loaded",
        "check.ref.not-found",
        "check.ref.primary-missing",
        "check.ref.redirected",
        "check.resolver.note",
        "check.schematic.conflict",
        "check.tech.problem",
        "check.technology.none",
        // Touchstone file health (2026-09-06). Data rather than a design, which is why passivity,
        // reciprocity and causality are warnings carrying a measured number rather than errors:
        // nothing in an `.sNp` says whether the part is meant to be passive or reciprocal.
        "check.touchstone.bad-z0",
        "check.touchstone.causality-skipped",
        "check.touchstone.frequency-order",
        "check.touchstone.not-causal",
        "check.touchstone.not-passive",
        "check.touchstone.not-reciprocal",
        "check.touchstone.port-mismatch",
        "check.touchstone.summary",
        "check.touchstone.z0-not-50",
        "check.view.defect",
        "check.wasm.rule-invalid",
        "check.workspace.ref-unresolved",
        "cli.analysis.not-selected",
        "cli.args.grid-not-for-pursuit",
        "cli.args.input-required",
        "cli.args.multiple-inputs",
        "cli.args.out-grid-not-for-loadpull",
        "cli.args.pin-malformed",
        "cli.args.set-malformed",
        "cli.args.unknown-option",
        "cli.em.setup-unreadable",
        "cli.input.not-found",
        "cli.measurement.failed",
        "cli.run.failed",
        "cli.verb.none",
        "cli.verb.unknown",
        "cli.worker.output",
        "cli.workspace.not-found",
        "convert.args.drill-format",
        "convert.args.drill-units",
        "convert.args.drill-zeros",
        "convert.args.dxf-version",
        "convert.args.list-cells-not-applicable",
        "convert.args.multiple-inputs",
        "convert.args.output-required",
        "convert.args.unknown-format",
        "convert.args.unknown-option",
        "convert.args.usage",
        "convert.board.refused",
        "convert.cell.ambiguous",
        "convert.cell.listed",
        "convert.cell.no-layout",
        "convert.cell.not-found",
        "convert.clay-to-clay",
        "convert.drill.format-unstated",
        "convert.failed",
        "convert.gdsii.coordinate-overflow",
        "convert.gerber.cross-technology-mapping",
        "convert.gerber.hierarchy-ceiling",
        "convert.gerber.refused",
        "convert.import.cancelled",
        "convert.import.no-cell",
        "convert.input.not-found",
        "convert.layout.unreadable",
        "convert.note",
        "convert.source.unrecognised",
        "convert.target.unrecognised",
        "convert.target.unsupported",
        "convert.technology.unreadable",
        "elab.note",
        "elab.warning",
        "em.run.error",
        "em.run.note",
        "em.run.warning",
        "em.setup.warning",
        "explain.analysis.none",
        "explain.analysis.not-found",
        "explain.args.multiple-paths",
        "explain.args.one-question",
        "explain.args.path-required",
        "explain.args.unknown-option",
        "explain.expr.failed",
        "explain.file.unreadable",
        "explain.option.not-applicable",
        "explain.path.not-found",
        "explain.path.unknown-kind",
        "explain.ref.not-found",
        "explain.ref.primary-missing",
        // RC-3's one headless spelling. R-rc0-19 fixes it as a VERB with nouns, so RC-5's `list` and
        // `restore` and RC-7's `commit` add ids here rather than a second verb.
        "history.args.noun-required",
        "history.args.unknown-noun",
        "history.git.unavailable",
        "history.input.not-a-workspace",
        "history.large-files.unanswered",
        "history.list.empty",
        "history.repository.absent",
        "history.restore.no-such-point",
        "history.restore.point-required",
        // RC-7's own two. `versions` is a DIFFERENT list from `list` (R-rc7-9) — the narrative rather
        // than the safety net — so its empty state and its not-found are separate ids.
        "history.versions.empty",
        "history.versions.no-such-version",
        "import.args.into-required",
        "import.args.multiple-sources",
        "import.args.noun-required",
        "import.args.source-required",
        "import.args.unknown-noun",
        "import.args.unknown-option",
        "import.layers.no-technology",
        "import.layers.not-installed",
        "import.no-technology",
        "import.note",
        "import.nothing-created",
        "import.part.ambiguous",
        "import.part.not-found",
        "import.refused",
        "import.source.not-found",
        "import.variant.not-found",
        "lp.export.no-surface",
        "new.args.cell-args-required",
        "new.args.cell-extra-argument",
        "new.args.directory-required",
        "new.args.multiple-directories",
        "new.args.noun-required",
        "new.args.unknown-noun",
        "new.args.unknown-option",
        "new.cell.exists",
        "new.name.invalid",
        "new.parent.not-found",
        "new.refused",
        "new.tech.unknown",
        "new.views.unknown",
        "read.args.multiple-paths",
        "read.args.path-required",
        "read.args.unknown-option",
        "read.file.interchange",
        "read.file.unreadable",
        "read.file.unsupported",
        "read.path.is-a-folder",
        "read.path.not-found",
        // brief-automation-6-reference-and-components.md — the reference surface.
        "reference.args.item-not-for-topic",
        "reference.args.item-without-topic",
        "reference.args.too-many",
        "reference.args.unknown-option",
        "reference.component.unknown",
        "reference.resource.unknown",
        "reference.topic.unknown",
        "serve.args.json-not-applicable",
        "serve.args.not-for-mode",
        "serve.args.required",
        "serve.args.root-required",
        "serve.args.unknown-option",
        "serve.args.unknown-value",
        "serve.args.wrong-type",
        "serve.path.outside-root",
        "serve.root.not-found",
        "serve.tool.cancelled",
        "serve.tool.failed",
        "serve.tool.unknown",
    ];

    private string[] Args(string argLine)
        => [.. argLine.Split('|').Select(a => a.Replace("<TMP>", _tmp))];

    private string Normalize(string s)
        => s.Replace(RepoRoot(), "<ROOT>", StringComparison.Ordinal)
            .Replace(_tmp, "<TMP>", StringComparison.Ordinal);

    private static string GoldenPath(string name)
        => Path.Combine(RepoRoot(), "tests", "Ui.Tests", "Cli", "golden", $"{name}.stdout.txt");

    private (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
        => Launch(culture: null, args);

    /// <summary>
    /// Runs the BUILT <c>CircuitRF.Cli.dll</c> from the repository root, never
    /// <c>dotnet run --project src/Cli</c> — a nested <c>dotnet run</c> starts an MSBuild inside a
    /// <c>dotnet test</c> that already holds this repository's build locks and does not finish. This
    /// is <c>EmCliVerbTests</c>' pattern verbatim, down to the <c>CliDir</c> assembly metadata; see
    /// its own remarks for the full account.
    ///
    /// <para><b>Both pipes are drained concurrently</b>, for the reason recorded there: reading
    /// stdout to the end and only then reading stderr deadlocks the moment the child fills stderr's
    /// buffer, and <c>lp</c> is chatty enough on stderr to reach it.</para>
    /// </summary>
    private (int ExitCode, string StdOut, string StdErr) Launch(string? culture, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory       = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        psi.ArgumentList.Add(CliDll());
        foreach (string a in args) psi.ArgumentList.Add(a);

        // A culture is set on the CHILD, because a CultureInfo assignment in this process would not
        // reach a separate one — and a separate one is the only place the invariance rule can be
        // tested, since it is the CLI's own formatting that is under test.
        if (culture is not null)
        {
            psi.Environment["DOTNET_SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY"] = "false";
            psi.Environment["LANG"]   = culture + ".UTF-8";
            psi.Environment["LC_ALL"] = culture + ".UTF-8";
        }

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
                typeof(CliStructuredOutputTests).Assembly)
            .First(a => a.Key == "CliDir").Value!;
        string path = Path.GetFullPath(Path.Combine(cliDir, "CircuitRF.Cli.dll"));
        Assert.True(File.Exists(path),
            $"the CLI was not built beside these tests: {path} — the ReferenceOutputAssembly=\"false\" " +
            "project reference in CircuitRF.Ui.Tests.csproj is what guarantees it, so check that first");
        return path;
    }

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "circuitRF.slnx")))
            dir = Path.GetDirectoryName(dir) ?? "";
        return dir;
    }

    private static string Raw(JsonElement e, string property) => e.GetProperty(property).GetRawText();
}
