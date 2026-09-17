using System;
using System.IO;
using System.Linq;
using CircuitRF.Core.Design;
using CircuitRF.Ui.Schematic;
using CircuitRF.Ui.ViewModels;
using Xunit;

namespace CircuitRF.Ui.Tests;

/// <summary>
/// The Analyses panel's card menu ▸ "Run This Analysis" — one card, not the list. The narrowing is a
/// parameter on <see cref="SchematicRunService.Prepare"/> rather than a second run path, so these
/// tests state what the narrowed PLAN contains; everything after the plan (elaboration, the engines,
/// the results file) is the ordinary run and is already covered.
/// </summary>
public sealed class RunOneAnalysisTests
{
    // The same shape the panel shows as three cards: an S-param base wrapped by an inner sweep over
    // Rb, wrapped in turn by an outer sweep over Ra.
    private const string NestedSweepCnl = """
        Ra = 50
        Rb = 50
        R:R1  in mid  R=Ra Ohm
        R:R2  mid 0    R=Rb Ohm
        Term:T1  in  0   Num=1 Z=50 Ohm
        Term:T2  mid 0   Num=2 Z=50 Ohm
        analysis SP1 type=sparam start=1 GHz stop=2 GHz step=1 GHz
        analysis SW_INNER type=parametric_sweep Var=Rb Values=10,20,30,40 Inner=SP1
        analysis SW_OUTER type=parametric_sweep Var=Ra Values=10,20,30 Inner=SW_INNER
        """;

    private static T WithNetlist<T>(string cnl, Func<string, T> body)
    {
        var path = Path.Combine(Path.GetTempPath(), "crf-runone-" + Guid.NewGuid().ToString("N")[..8] + ".cnl");
        try
        {
            File.WriteAllText(path, cnl);
            return body(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>
    /// Naming the INNER sweep runs that sweep and everything it wraps, and nothing that wraps IT:
    /// 4 points over Rb, no Ra axis. Running the outer chain instead would be 12 points — which is
    /// exactly the mistake "only that parametric sweep gets run" forbids.
    /// </summary>
    [Fact]
    public void RunOne_OnAnInnerSweep_RunsThatSweepOnly_NotTheOuterOneWrappingIt()
    {
        var plan = WithNetlist(NestedSweepCnl,
            path => SchematicRunService.Prepare(path, null, "SW_INNER"));

        Assert.Equal(RunStatus.Success, plan.Status);
        var line = Assert.Single(plan.Lines);
        Assert.Contains("4 pt(s) over Rb", line);
        Assert.DoesNotContain("over Ra", line);
        Assert.Equal(4, plan.TotalWorkUnits);   // the sweep's own leaf points, as a full run counts them
    }

    /// <summary>Naming the BASE analysis runs it bare — no sweep axis at all.</summary>
    [Fact]
    public void RunOne_OnTheBaseAnalysis_RunsItWithNoSweepAxis()
    {
        var plan = WithNetlist(NestedSweepCnl,
            path => SchematicRunService.Prepare(path, null, "SP1"));

        Assert.Equal(RunStatus.Success, plan.Status);
        var line = Assert.Single(plan.Lines);
        Assert.Contains("S-param", line);
        Assert.DoesNotContain("Parametric sweep", line);
        Assert.Equal(2, plan.TotalWorkUnits);
    }

    /// <summary>
    /// A disabled card is refused BY NAME rather than run: the checkbox says disabled analyses are not
    /// run, and a menu item that overrode it would make the checkbox mean nothing.
    /// </summary>
    [Fact]
    public void RunOne_OnADisabledAnalysis_IsRefused_NotRun()
    {
        const string cnl = """
            R:R1  in 0  R=50 Ohm
            Term:T1  in  0   Num=1 Z=50 Ohm
            analysis SP1 type=sparam start=1 GHz stop=2 GHz step=1 GHz enabled=false
            """;

        var plan = WithNetlist(cnl, path => SchematicRunService.Prepare(path, null, "SP1"));

        Assert.Equal(RunStatus.NoAnalysis, plan.Status);
        Assert.Contains("disabled", plan.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(plan.Lines);
    }

    /// <summary>
    /// A name that no longer resolves is reported, never silently widened to a full run — the one
    /// failure mode that would quietly turn "run this one" into "run all of them".
    /// </summary>
    [Fact]
    public void RunOne_OnAnUnknownName_IsRefused_NotWidenedToTheWholeList()
    {
        var plan = WithNetlist(NestedSweepCnl,
            path => SchematicRunService.Prepare(path, null, "NotThere"));

        Assert.Equal(RunStatus.NoAnalysis, plan.Status);
        Assert.Empty(plan.Lines);
    }

    /// <summary>
    /// The run's own lines name the CARD, and a sweep card is named by the variable it sweeps — which
    /// is what the panel draws. Naming the generated analysis ("DC1_sweep_Vgs") would put a string on
    /// the progress row that appears nowhere on screen.
    /// </summary>
    [Fact]
    public void TheRunLabel_NamesTheCard_AsThePanelDrawsIt()
    {
        Assert.Equal("DC1", WorkspaceViewModel.CardLabel(new DcAnalysis("DC1")));
        Assert.Equal("sweep Vgs", WorkspaceViewModel.CardLabel(
            new ParametricSweepAnalysis("DC1_sweep_Vgs", "Vgs", [0, 1, 2], "DC1")));
    }

    /// <summary>Unnarrowed, the same netlist still plans the whole list — one chain, 12 leaf points.</summary>
    [Fact]
    public void Unnarrowed_Prepare_IsUnchanged()
    {
        var plan = WithNetlist(NestedSweepCnl, path => SchematicRunService.Prepare(path));

        Assert.Equal(RunStatus.Success, plan.Status);
        Assert.Equal(3 * 4, plan.TotalWorkUnits);
    }
}
