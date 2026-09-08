// ================================================================
//  WSProbeMessagesTests.cs — WSP-4 R-wsp4-13.
//
//  The run diagnostics WSP-1/2/3/9 emit reach the Messages pane through the channel every other run
//  diagnostic uses, and there is nothing new to build for that. What is worth holding shut is the
//  SEVERITY, because the margin one is the odd member: a low margin is somewhere to look, not a
//  failure, so it is an Info NOTE and must not turn a successful run's status amber.
// ================================================================

using CircuitRF.Engine;
using CircuitRF.Ui.Schematic;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests.DataDisplay;

public sealed class WSProbeMessagesTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "crf-wsp4-msg-" + Guid.NewGuid().ToString("N")[..12]);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { /* best effort */ } }

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "circuitRF.slnx")))
            dir = Path.GetDirectoryName(dir) ?? "";
        return dir.Length > 0 ? dir : AppContext.BaseDirectory;
    }

    private RunResult RunFixture(string name)
    {
        Directory.CreateDirectory(_dir);
        string cnl = Path.Combine(_dir, name);
        File.Copy(Path.Combine(RepoRoot(), "testdata", "wsprobe", name), cnl, overwrite: true);

        var plan = SchematicRunService.Prepare(cnl, _dir);
        Assert.True(plan.Status == RunStatus.Success, plan.StatusMessage);
        return SchematicRunService.Execute(plan, new RunControl());
    }

    /// <summary>
    /// WSP-9's split resonator is STABLE and fires the margin diagnostic by design — a node one
    /// negative-resistance step from oscillating genuinely has little margin. It must arrive as a
    /// NOTE (which the Messages pane renders at Info) and leave the run's own status Success, so the
    /// window does not report a healthy run as a troubled one.
    /// </summary>
    [Fact]
    public void TheMarginDiagnostic_IsANoteAndLeavesTheRunSuccessful()
    {
        var result = RunFixture("margin_split_resonator.cnl");
        foreach (string n in result.Notes)    output.WriteLine("note:    " + n);
        foreach (string w in result.Warnings) output.WriteLine("warning: " + w);

        Assert.Equal(RunStatus.Success, result.Status);
        Assert.Contains(result.Notes, n => n.Contains("stability margin below"));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("stability margin below"));
    }

    /// <summary>The note names the probe, both margins with their frequencies, and the −12 dB rule —
    /// which is what makes it something to act on rather than a flag.</summary>
    [Fact]
    public void TheMarginNote_NamesTheProbeAndBothMargins()
    {
        var note = Assert.Single(RunFixture("margin_split_resonator.cnl").Notes,
                                 n => n.Contains("stability margin below"));
        output.WriteLine(note);

        Assert.Contains("WSProbe 'P'", note);
        Assert.Contains("SM_Y0", note);
        Assert.Contains("SM_H0", note);
        Assert.Contains("−12 dB", note);        // a typographic minus, as the paper prints it
    }

    /// <summary>A probed run that is comfortably above the threshold says nothing at all — the
    /// diagnostic is a pointer, and one that fires on every run points nowhere.</summary>
    [Fact]
    public void AComfortableRun_SaysNothing()
    {
        var result = RunFixture("cascade.cnl");
        Assert.Equal(RunStatus.Success, result.Status);
        Assert.DoesNotContain(result.Notes,    n => n.Contains("stability margin below"));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("wsprobe."));
    }
}
