// ================================================================
//  WsProbeCliTests.cs — brief-wsprobe-1's CLI half (R-wsp1-11, R-wsp1-12), the REAL CLI as a
//  separate process, as AuthoringCliVerbTests and MissingVerbsCliTests gate a verb.
//
//  The engine gates live in tests/Engine.Tests/Linear/WSProbeTests.cs; what this file holds is
//  that the verbs REPORT what the engine produced — a `measure` line reaches the probe cubes
//  through `circuitrf sparam` exactly as it does through MeasurementEvaluator, the --json document
//  carries the label ↔ idx pairs, a port-less run refuses a Touchstone by name, `check` warns on a
//  shorted probe, and `explain --analysis` lists the probes with both nets.
// ================================================================

using System.Diagnostics;
using System.Text.Json;
using CircuitRF.Core.Elaboration;
using CircuitRF.Core.Netlist;
using CircuitRF.Engine;
using RfCore.Data;
using RfCore.Export;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests.Cli;

public sealed class WsProbeCliTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "crf-wsp1-cli-" + Guid.NewGuid().ToString("N")[..12]);

    public void Dispose() { try { Directory.Delete(_root, true); } catch { /* best effort */ } }

    private static string Fixture(string name) => Path.Combine(RepoRoot(), "testdata", "wsprobe", name);

    private string Dir(string name)
    {
        string d = Path.Combine(_root, name);
        Directory.CreateDirectory(d);
        return d;
    }

    // ══ R-wsp1-11 / (n): the measure lines, through the verb ═══════════════════

    /// <summary>
    /// The `.npy` the verb writes carries the TestBench's measurements evaluated over the probe
    /// cubes, and they are the same values MeasurementEvaluator produces in process — the verb
    /// and the GUI share one evaluator, so a measurement of a probe output cannot answer
    /// differently headlessly.
    /// </summary>
    [Fact]
    public void Sparam_EvaluatesMeasureLinesOverTheProbeCubes_ExactlyAsTheEvaluatorDoes()
    {
        string outPath = Path.Combine(Dir("meas"), "probed.npy");
        var run = RunCli("sparam", Fixture("hero1_probed.cnl"), "-o", outPath);
        Assert.True(run.ExitCode == 0, run.StdErr + run.StdOut);
        Assert.Contains("WSProbe P1 idx=1", run.StdOut);
        Assert.Contains("WSProbe P2 idx=2", run.StdOut);

        var (fromFile, _) = DataSetImporter.Import(outPath);

        var (lib, tb) = CnlReader.ReadFile(Fixture("hero1_probed.cnl"));
        var nl    = new Elaborator(lib).Elaborate(tb);
        var freqs = tb.Analyses.OfType<CircuitRF.Core.Design.SParameterAnalysis>().First().Expand(nl.ResolvedGlobals);
        var ds    = SParameterEngine.Run(nl, freqs);
        var meas  = new DataSet();
        Assert.Empty(new MeasurementEvaluator(tb, nl,
            new Dictionary<string, DataSet>(StringComparer.OrdinalIgnoreCase) { ["SP1"] = ds }).EvaluateInto(meas));

        foreach (string name in (string[])["ZGg", "h"])
        {
            var a = fromFile["measurements." + name].ComplexValues;
            var b = meas[name].ComplexValues;
            Assert.Equal(b.Length, a.Length);
            for (int k = 0; k < a.Length; k++)
            {
                Assert.Equal(b[k].Real,      a[k].Real,      12);
                Assert.Equal(b[k].Imaginary, a[k].Imaginary, 12);
            }
        }
        Assert.Equal(2.0, fromFile["measurements.ix"].RealValues.Single());
        Assert.Equal(ds["wsp"].ComplexValues.Length, fromFile["wsp"].ComplexValues.Length);
    }

    // ══ R-wsp1-12(a): the --json document ═══════════════════════════════════════

    [Fact]
    public void Sparam_Json_CarriesTheProbesWithTheirIdx()
    {
        string outPath = Path.Combine(Dir("json"), "probed.npy");
        var run = RunCli("sparam", Fixture("hero1_probed.cnl"), "-o", outPath, "--json", "--result", "summary");
        Assert.True(run.ExitCode == 0, run.StdErr + run.StdOut);

        using var doc = JsonDocument.Parse(run.StdOut);
        var probes = doc.RootElement.GetProperty("result").GetProperty("wsprobes");
        Assert.Equal(2, probes.GetArrayLength());
        Assert.Equal("P1", probes[0].GetProperty("label").GetString());
        Assert.Equal(1,    probes[0].GetProperty("idx").GetInt32());
        Assert.Equal("P2", probes[1].GetProperty("label").GetString());
        Assert.Equal(2,    probes[1].GetProperty("idx").GetInt32());
    }

    /// <summary>An unprobed run's document has no <c>wsprobes</c> key at all — absent, not empty.</summary>
    [Fact]
    public void Sparam_Json_HasNoWsprobesKey_WhenThereIsNoProbe()
    {
        string outPath = Path.Combine(Dir("nojson"), "hero1.npy");
        var run = RunCli("sparam", Path.Combine(RepoRoot(), "testdata", "Hero1", "hero1.cnl"),
                         "--freq", "1e9:3e9:1e9", "-o", outPath, "--json", "--result", "summary");
        Assert.True(run.ExitCode == 0, run.StdErr + run.StdOut);
        using var doc = JsonDocument.Parse(run.StdOut);
        Assert.False(doc.RootElement.GetProperty("result").TryGetProperty("wsprobes", out _));
        Assert.DoesNotContain("WSProbe", run.StdOut);
    }

    // ══ R-wsp1-6 / R-wsp1-12(a): a port-less run and the Touchstone refusal ═════

    [Fact]
    public void Sparam_PortlessProbeRun_WritesTheCubes_AndRefusesATouchstoneByName()
    {
        string d = Dir("portless");
        var ok = RunCli("sparam", Fixture("series_resonator.cnl"), "-o", Path.Combine(d, "sr.npy"));
        Assert.True(ok.ExitCode == 0, ok.StdErr + ok.StdOut);
        Assert.Contains("S-parameters: none (no ports); WSProbe outputs: 1 probe(s)", ok.StdOut);
        var (ds, _) = DataSetImporter.Import(Path.Combine(d, "sr.npy"));
        Assert.True(ds.Contains("wsp"));
        Assert.True(ds.Contains("H0:P"));
        Assert.False(ds.Contains("S"));

        var refused = RunCli("sparam", Fixture("series_resonator.cnl"), "-o", Path.Combine(d, "sr.s1p"), "--json");
        Assert.Equal(1, refused.ExitCode);
        Assert.Contains("sparam.export.no-s-parameters", refused.StdOut);
        Assert.Contains(".npy", refused.StdErr + refused.StdOut);
        Assert.False(File.Exists(Path.Combine(d, "sr.s1p")));

        // The default output, with no -o, is a Touchstone too — and the same refusal.
        var bare = RunCli("sparam", Fixture("series_resonator.cnl"), "--json");
        Assert.Equal(1, bare.ExitCode);
        Assert.Contains("sparam.export.no-s-parameters", bare.StdOut);
    }

    // ══ R-wsp1-12(b): check and explain ═════════════════════════════════════════

    [Fact]
    public void Check_WarnsOnAProbeWithOneNetOnBothTerminals()
    {
        string path = Path.Combine(Dir("check"), "shorted.cnl");
        File.WriteAllText(path, """
            WSProbe:P  a a
            R:R1       a 0  R=50 Ohm
            Term:T1    a 0  Num=1 Z=50 Ohm
            analysis SP1 type=sparam start=1 stop=2 npts=2 Unit=GHz
            """);
        var run = RunCli("check", path, "--json");
        Assert.Equal(0, run.ExitCode);   // a warning: reported, exit 0
        Assert.Contains("wsprobe.shorted", run.StdOut);
        Assert.Contains("'P'", run.StdErr);

        File.WriteAllText(path, """
            WSProbe:P  a b
            R:R1       b 0  R=50 Ohm
            Term:T1    a 0  Num=1 Z=50 Ohm
            analysis SP1 type=sparam start=1 stop=2 npts=2 Unit=GHz
            """);
        var clean = RunCli("check", path, "--json");
        Assert.Equal(0, clean.ExitCode);
        Assert.DoesNotContain("wsprobe.shorted", clean.StdOut);
    }

    [Fact]
    public void Explain_Analysis_ListsTheProbesWithIdxAndBothNets()
    {
        var run = RunCli("explain", Fixture("hero1_probed.cnl"), "--analysis", "--json");
        Assert.True(run.ExitCode == 0, run.StdErr + run.StdOut);

        using var doc = JsonDocument.Parse(run.StdOut);
        var row = doc.RootElement.GetProperty("result").GetProperty("explain").GetProperty("analyses")[0];
        Assert.Equal("SP1", row.GetProperty("name").GetString());
        Assert.Equal(4, row.GetProperty("ports").GetInt32());
        var probes = row.GetProperty("wsProbes");
        Assert.Equal(2, probes.GetArrayLength());
        Assert.Equal("P1", probes[0].GetProperty("label").GetString());
        Assert.Equal(1,    probes[0].GetProperty("idx").GetInt32());
        Assert.Equal("a1", probes[0].GetProperty("g").GetString());
        Assert.Equal("x1", probes[0].GetProperty("l").GetString());
        Assert.Equal("x2", probes[1].GetProperty("g").GetString());
        Assert.Equal("a2", probes[1].GetProperty("l").GetString());

        var portless = RunCli("explain", Fixture("series_resonator.cnl"), "--analysis");
        Assert.True(portless.ExitCode == 0, portless.StdErr + portless.StdOut);
        Assert.Contains("S-parameters: none (no ports); WSProbe outputs: 1 probe(s)", portless.StdOut);
        Assert.Contains("WSProbe P idx=1  G=nG  L=nL", portless.StdOut);
        output.WriteLine(portless.StdOut);
    }

    // ── The CLI as a process ────────────────────────────────────────────────────

    private static (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
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

        using var proc = Process.Start(psi)!;
        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        proc.WaitForExit();
        return (proc.ExitCode, outTask.GetAwaiter().GetResult(), errTask.GetAwaiter().GetResult());
    }

    private static string CliDll()
    {
        string cliDir = System.Reflection.CustomAttributeExtensions
            .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(typeof(WsProbeCliTests).Assembly)
            .First(a => a.Key == "CliDir").Value!;
        string path = Path.GetFullPath(Path.Combine(cliDir, "CircuitRF.Cli.dll"));
        Assert.True(File.Exists(path), $"the CLI was not built beside these tests: {path}");
        return path;
    }

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "circuitRF.slnx")))
            dir = Path.GetDirectoryName(dir) ?? "";
        return dir.Length > 0 ? dir : AppContext.BaseDirectory;
    }
}
