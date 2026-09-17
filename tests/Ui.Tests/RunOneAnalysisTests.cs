using System;
using System.IO;
using System.Linq;
using CircuitRF.Core.Design;
using CircuitRF.Ui.Schematic;
using CircuitRF.Ui.ViewModels;
using Xunit;

namespace CircuitRF.Ui.Tests;

/// <summary>
/// The Analyses panel's card menu ▸ Run (named after the card) — one CHAIN, not the whole list. The narrowing
/// is a parameter on <see cref="SchematicRunService.Prepare"/> rather than a second run path, so these
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
    /// Naming ANY card of a chain runs the whole chain — every enabled sweep wrapping it included.
    /// Dispatching the card alone drops those axes and still returns a converged, complete-looking
    /// result, which is the mistake nobody can see: 12 points over Ra x Rb, not SW_INNER's 4 and not
    /// SP1's bare 2. This is the CLI's own rule for <c>-a</c>.
    /// </summary>
    [Theory]
    [InlineData("SP1")]        // the base analysis
    [InlineData("SW_INNER")]   // the sweep in the middle
    [InlineData("SW_OUTER")]   // the chain root, which was never in doubt
    public void RunOne_OnAnyCardOfAChain_RunsEverySweepWrappingIt(string card)
    {
        var plan = WithNetlist(NestedSweepCnl,
            path => SchematicRunService.Prepare(path, null, card));

        Assert.Equal(RunStatus.Success, plan.Status);
        var line = Assert.Single(plan.Lines);
        Assert.Contains("over Ra", line);
        Assert.Contains("over Rb", line);
        Assert.Equal(3 * 4, plan.TotalWorkUnits);   // exactly what the unnarrowed run plans
    }

    /// <summary>
    /// The promotion is REPORTED, because the run is about to do more than the card that was
    /// right-clicked says. A chain root, promoted from nothing, says nothing.
    /// </summary>
    [Fact]
    public void APromotedRun_SaysSo_AndAnUnpromotedOneDoesNot()
    {
        var promoted = WithNetlist(NestedSweepCnl,
            path => SchematicRunService.Prepare(path, null, "SP1"));
        var note = Assert.Single(promoted.Notes);
        Assert.Contains("SP1", note);
        Assert.Contains("SW_OUTER", note);

        var root = WithNetlist(NestedSweepCnl,
            path => SchematicRunService.Prepare(path, null, "SW_OUTER"));
        Assert.Empty(root.Notes);
    }

    /// <summary>
    /// The narrowing is still a narrowing: a second chain declared in the same schematic does not run.
    /// That is the whole of what this menu item does that the Run button does not.
    /// </summary>
    [Fact]
    public void RunOne_RunsOneChain_AndLeavesTheOtherChainsAlone()
    {
        const string twoChains = """
            Ra = 50
            R:R1  in mid  R=Ra Ohm
            R:R2  mid 0    R=50 Ohm
            Term:T1  in  0   Num=1 Z=50 Ohm
            Term:T2  mid 0   Num=2 Z=50 Ohm
            analysis SP1 type=sparam start=1 GHz stop=2 GHz step=1 GHz
            analysis SW1 type=parametric_sweep Var=Ra Values=10,20,30 Inner=SP1
            analysis SP2 type=sparam start=1 GHz stop=5 GHz step=1 GHz
            """;

        var narrowed = WithNetlist(twoChains, path => SchematicRunService.Prepare(path, null, "SP1"));
        Assert.Equal(RunStatus.Success, narrowed.Status);
        Assert.Single(narrowed.Lines);
        Assert.Equal(3, narrowed.TotalWorkUnits);             // SW1's 3 sweep points, and only those

        var all = WithNetlist(twoChains, path => SchematicRunService.Prepare(path));
        Assert.Equal(2, all.Lines.Count);
        Assert.Equal(3 + 5, all.TotalWorkUnits);             // SP2's 5 frequencies as well
    }

    /// <summary>
    /// A DISABLED outer sweep still collapses under promotion — the checkbox drops that axis and the
    /// run lands on the outermost sweep that is actually enabled, exactly as the Run button's would.
    /// </summary>
    [Fact]
    public void RunOne_PromotesPastADisabledOuterSweep_NotOntoIt()
    {
        var cnl = NestedSweepCnl.Replace(
            "analysis SW_OUTER type=parametric_sweep Var=Ra Values=10,20,30 Inner=SW_INNER",
            "analysis SW_OUTER type=parametric_sweep Var=Ra Values=10,20,30 Inner=SW_INNER enabled=false");

        var plan = WithNetlist(cnl, path => SchematicRunService.Prepare(path, null, "SP1"));

        Assert.Equal(RunStatus.Success, plan.Status);
        var line = Assert.Single(plan.Lines);
        Assert.Contains("over Rb", line);
        Assert.DoesNotContain("over Ra", line);
        Assert.Equal(4, plan.TotalWorkUnits);
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
