// ================================================================
//  WsProbePlotVerbTests.cs — WSP-4 gate (e), and the refusals beside it.
//
//  `plot --trace cube=…wsp,probe=…,metric=…` writes the SAME `.cdd` the GUI writes and hands it to
//  the SAME composer, so the gate is byte identity against `render --data` on the document the verb
//  itself produced. A second probe path here would have drifted invisibly: a picture that is
//  plausible is indistinguishable from one that is right.
//
//  Run as a PROCESS, like every other CLI gate in this folder.
// ================================================================

using System.Diagnostics;
using System.Text.Json;
using CircuitRF.Render.DataDisplay;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests.Cli;

public sealed class WsProbePlotVerbTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "crf-wsp4-plot-" + Guid.NewGuid().ToString("N")[..12]);

    public void Dispose() { try { Directory.Delete(_root, true); } catch { /* best effort */ } }

    private string Dir(string name)
    {
        string d = Path.Combine(_root, name);
        Directory.CreateDirectory(d);
        return d;
    }

    private static string Fixture(string name)
        => Path.Combine(RepoRoot(), "testdata", "wsprobe", name);

    /// <summary>A probed run, as a result file — the input every test here starts from.</summary>
    private string ProbedRun(string dir, string fixture = "margin_split_resonator.cnl")
    {
        string npy = Path.Combine(dir, "run.npy");
        var run = RunCli("sparam", Fixture(fixture), "-o", npy);
        Assert.True(run.ExitCode == 0, run.StdErr + run.StdOut);
        return npy;
    }

    // ══ (e) — byte identity with the document it wrote ═══════════════════════

    [Theory]
    [InlineData("svg")]
    [InlineData("pdf")]
    [InlineData("png")]
    public void AProbePlot_IsTheSameBytesAsRenderingTheDisplayItBuilt(string format)
    {
        string dir = Dir("bytes-" + format);
        string npy = ProbedRun(dir);

        // The SAME file name in two directories: a PDF's Title metadata is taken from the output
        // path, so two NAMES would differ in that one field and nowhere else.
        string viaPlot   = Path.Combine(Dir("bytes-" + format + "-a"), "one." + format);
        string viaRender = Path.Combine(Dir("bytes-" + format + "-b"), "one." + format);
        string cdd       = Path.Combine(dir, "one.cdd");

        var plot = RunCli("plot", npy, "-o", viaPlot, "--write-cdd", cdd,
                          "--trace", "cube=wsp,probe=P,metric=SM_Y0,y=db20",
                          "--trace", "cube=wsp,probe=P,metric=SM_H0,y=db20",
                          "--title", "split resonator");
        Assert.True(plot.ExitCode == 0, plot.StdErr + plot.StdOut);

        var render = RunCli("render", cdd, "--data", npy, "-o", viaRender);
        Assert.True(render.ExitCode == 0, render.StdErr + render.StdOut);

        AssertSameBytes(viaPlot, viaRender);
    }

    /// <summary>The document the verb writes carries the probe fields the GUI's own writer emits —
    /// it is the same class, so this is what makes the byte-identity above meaningful rather than
    /// two empty pictures agreeing.</summary>
    [Fact]
    public void TheDocumentItWrites_CarriesTheProbeSpec()
    {
        string dir = Dir("doc");
        string cdd = Path.Combine(dir, "one.cdd");

        Assert.Equal(0, RunCli("plot", ProbedRun(dir), "-o", Path.Combine(dir, "p.svg"),
                               "--write-cdd", cdd, "--type", "polar",
                               "--trace", "cube=wsp,probe=P,metric=invY0").ExitCode);

        var config = JsonSerializer.Deserialize<DataDisplayConfig>(File.ReadAllText(cdd))!;
        var trace  = config.Tabs[0].Plots[0].Traces[0];

        Assert.Equal("wsp", trace.CubeName);
        Assert.NotNull(trace.WsProbe);
        Assert.Equal("P", trace.WsProbe!.Probe);
        Assert.Equal(WspMetric.InvY0, trace.WsProbe.Metric);
        // A polar trace draws the complex value itself — no scalar reduction.
        Assert.Equal(CubeTransform.None, trace.CubeTransform);
        // The slice is authored against the METRIC's axes, not the raw matrix's {freq, row, col}.
        Assert.Equal(["freq"], trace.CubeSlice.Select(a => a.AxisName).ToArray());
    }

    /// <summary>
    /// The two margin reference lines reach the picture — the only feature this brief adds to the
    /// DRAWING that a byte comparison against the same document would not notice.
    ///
    /// <para>Asserted on stroke opacity rather than on <c>stroke-dasharray</c>: the dash is emitted
    /// as segments (see <c>PlotRenderer.DrawWspMarginReferenceLines</c> for why), so what tells the
    /// two lines apart in the file is the alpha each carries — 70/255 for the −12 dB floor and
    /// 140/255 for the run's own threshold, neither of which any other element in the picture
    /// uses.</para>
    /// </summary>
    [Fact]
    public void AMarginPlot_DrawsTheThresholdAndTheMinusTwelveFloor()
    {
        const string floorAlpha     = "stroke-opacity=\"0.27450982\"";   //  70/255 — the −12 dB floor
        const string thresholdAlpha = "stroke-opacity=\"0.54901963\"";   // 140/255 — MarginThreshold

        string dir = Dir("lines");
        string svg = Path.Combine(dir, "margin.svg");
        Assert.Equal(0, RunCli("plot", ProbedRun(dir), "-o", svg,
                               "--trace", "cube=wsp,probe=P,metric=SM_Y0,y=db20").ExitCode);

        string text = File.ReadAllText(svg);
        Assert.Contains(floorAlpha,     text);
        Assert.Contains(thresholdAlpha, text);

        // And the same picture WITHOUT a margin trace has neither — so the assertion above is about
        // the margin rather than about something the renderer always draws.
        string plain = Path.Combine(dir, "plain.svg");
        Assert.Equal(0, RunCli("plot", ProbedRun(Dir("lines-b")), "-o", plain,
                               "--trace", "cube=wsp,probe=P,metric=H0,y=mag").ExitCode);
        string plainText = File.ReadAllText(plain);
        Assert.DoesNotContain(floorAlpha,     plainText);
        Assert.DoesNotContain(thresholdAlpha, plainText);
    }

    // ══ the refusals ═════════════════════════════════════════════════════════

    /// <summary>A probe this run does not have is named, with the run's own list beside it — the
    /// library's own sentence, forwarded. Not an empty picture.</summary>
    [Fact]
    public void AProbeTheRunDoesNotHave_IsRefusedByNameWithTheRunsOwnList()
    {
        string dir = Dir("bad-probe");
        var run = RunCli("plot", ProbedRun(dir), "-o", Path.Combine(dir, "x.svg"),
                         "--trace", "cube=wsp,probe=NOPE,metric=SM_Y0");
        output.WriteLine(run.StdErr);
        Assert.Equal(1, run.ExitCode);
        Assert.Contains("NOPE", run.StdErr);
        Assert.Contains("It has: P", run.StdErr);
        Assert.False(File.Exists(Path.Combine(dir, "x.svg")));
    }

    [Fact]
    public void AProbeWithNoMetric_IsRefusedAndNamesTheFlagThatAnswersIt()
    {
        string dir = Dir("no-metric");
        var run = RunCli("plot", ProbedRun(dir), "-o", Path.Combine(dir, "x.svg"),
                         "--trace", "cube=wsp,probe=P");
        Assert.Equal(1, run.ExitCode);
        Assert.Contains("metric=", run.StdErr);
    }

    [Fact]
    public void AnUnknownMetric_IsRefusedAndListsTheRealOnes()
    {
        string dir = Dir("bad-metric");
        var run = RunCli("plot", ProbedRun(dir), "-o", Path.Combine(dir, "x.svg"),
                         "--trace", "cube=wsp,probe=P,metric=nonsense");
        Assert.Equal(1, run.ExitCode);
        Assert.Contains("SM_Y0", run.StdErr);
        Assert.Contains("1/H0",  run.StdErr);
    }

    /// <summary>A probe field on a cube that is not a wsp matrix. Refused rather than dropped: the
    /// picture would be of the cube named, which is not what was asked for.</summary>
    [Fact]
    public void AProbeFieldOnSomeOtherCube_IsRefusedByKind()
    {
        string dir = Dir("wrong-cube");
        var run = RunCli("plot", ProbedRun(dir), "-o", Path.Combine(dir, "x.svg"),
                         "--trace", "cube=SM_Y0:P,probe=P,metric=SM_Y0");
        Assert.Equal(1, run.ExitCode);
        Assert.Contains("not a wsp matrix", run.StdErr);
    }

    /// <summary>
    /// <c>LGF</c> (the forward synthetic-circulator loop gain of one probe, Eq. 99) and <c>LGf</c>
    /// (the feedback-as-synthetic-FET loop gain of a probe PAIR, Eq. 149) are two quantities that
    /// differ only in case. Neither may resolve to the other, and the case-insensitive spelling
    /// resolves to neither — a wrong answer here is a plausible curve, not an error.
    /// </summary>
    [Fact]
    public void CaseIsLoadBearingInTheMetricNames()
    {
        Assert.True(WspMetrics.TryParse("LGF", out var upper));
        Assert.Equal(WspMetric.LGF, upper);

        Assert.True(WspMetrics.TryParse("LGf", out var lower));
        Assert.Equal(WspMetric.LGf, lower);

        Assert.False(WspMetrics.TryParse("lgf", out _));
        Assert.False(WspMetrics.TryParse("LGH", out _) && WspMetrics.TryParse("lgh", out _));

        // The two spellings a command line cannot type, and the placeholders WSP-9 retired.
        Assert.True(WspMetrics.TryParse("invY0", out var inv) && inv == WspMetric.InvY0);
        Assert.True(WspMetrics.TryParse("1/Y0",  out var slash) && slash == WspMetric.InvY0);
        Assert.True(WspMetrics.TryParse("nY",    out var old) && old == WspMetric.InvY0);
    }

    // ── process plumbing ─────────────────────────────────────────────────────

    private static void AssertSameBytes(string a, string b)
    {
        var left  = File.ReadAllBytes(a);
        var right = File.ReadAllBytes(b);
        Assert.True(left.AsSpan().SequenceEqual(right), $"{a} and {b} differ");
    }

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
            .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(
                typeof(WsProbePlotVerbTests).Assembly)
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
