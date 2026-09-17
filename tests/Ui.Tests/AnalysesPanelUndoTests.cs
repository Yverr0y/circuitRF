using System.Collections.Generic;
using System.Linq;
using CircuitRF.Core.Design;
using CircuitRF.Ui.Commands;
using CircuitRF.Ui.Schematic;
using CircuitRF.Ui.ViewModels;
using Xunit;

namespace CircuitRF.Ui.Tests;

/// <summary>
/// "Undo/Redo does not work for deleting an analysis" (owner, 2026-09-16).
///
/// <para>The edit was always on a real undo stack — it was the SHELL that could not find that stack.
/// The Analyses panel is a tool, so it is never the document dock's active dockable; it retains its
/// schematic when focus moves elsewhere; and it edits the BASE session even while the tab is pushed
/// into a sub-cell. The shell's Undo follows the active DOCUMENT, so it had nothing to do with the
/// edit the user had just made. The panel now reports the session every edit lands on, which is what
/// the shell retargets Undo to — these tests state that half, since the other half is one call.</para>
/// </summary>
public sealed class AnalysesPanelUndoTests
{
    private static (AnalysesListViewModel List, SchematicViewModel Sch, SchematicEditModel Model)
        Panel(params Analysis[] analyses)
    {
        var model = new SchematicEditModel();
        foreach (var a in analyses) model.Analyses.Add(a);
        var sch  = new SchematicViewModel(model, messageSink: null);
        var list = new AnalysesListViewModel();
        list.SetActiveSchematic(sch);
        return (list, sch, model);
    }

    /// <summary>
    /// The headline, stated end to end through the shell's own contract: delete a card, take the
    /// session the panel reported, and Undo through <see cref="IEditHistoryDocument"/> — the exact
    /// interface the Undo command calls — brings the analysis back. Redo removes it again.
    /// </summary>
    [Fact]
    public void DeletingACard_ReportsASessionWhoseUndoBringsItBack()
    {
        var (list, _, model) = Panel(new DcAnalysis("DC1"));

        IEditHistoryDocument? target = null;
        list.EditCommitted += vm => target = new SessionUndoTarget(vm.UndoRedo);

        list.DeleteRowCommand.Execute(list.Rows[0]);
        Assert.Empty(model.Analyses);

        Assert.NotNull(target);
        Assert.True(target!.CanUndoLast);
        Assert.Contains("DC1", target.UndoLastDescription);

        target.UndoLast();
        Assert.Equal("DC1", Assert.Single(model.Analyses).Name);
        Assert.Single(list.Rows);           // the panel follows the undo

        target.RedoLast();
        Assert.Empty(model.Analyses);
        Assert.Empty(list.Rows);
    }

    /// <summary>
    /// Not just Delete: EVERY mutation the panel makes reports its session, because every one of them
    /// is undoable and the shell cannot find any of their stacks on its own. A route added later that
    /// calls the schematic directly would be silently un-undoable from the panel — which is exactly
    /// the bug this fixes, so it is worth stating over the whole surface rather than over one command.
    /// </summary>
    [Fact]
    public void EveryPanelEdit_ReportsTheSessionItLandedOn()
    {
        var (list, sch, _) = Panel(new DcAnalysis("DC1"), new DcAnalysis("DC2"));

        var reported = new List<SchematicViewModel>();
        list.EditCommitted += reported.Add;

        list.SelectedRow = list.Rows[0];
        list.DuplicateCommand.Execute(null);        // toolbar Duplicate
        list.Rows[0].Enabled = false;               // the row's own checkbox
        list.SelectedRow = list.Rows[^1];
        list.RemoveCommand.Execute(null);           // toolbar Remove (the header delete button)
        list.DeleteRowCommand.Execute(list.Rows[0]); // card menu ▸ Delete
        list.CommitResultsFileName("baseline");     // the results-file field

        Assert.Equal(5, reported.Count);
        Assert.All(reported, vm => Assert.Same(sch, vm));
    }

    /// <summary>
    /// The card menu's Remove and Run items both name the card they act on. A sweep is named by the
    /// analysis it ultimately wraps plus its own variable — never by its generated <c>Name</c>, which
    /// appears nowhere on screen; the nested case proves the walk goes all the way down rather than
    /// one level.
    /// </summary>
    [Fact]
    public void TheCardMenuItems_NameTheCard_AndASweepByItsBaseAndVariable()
    {
        var (list, _, _) = Panel(
            new DcAnalysis("DC1"),
            new ParametricSweepAnalysis("DC1_sweep_Vgs", "Vgs", [0, 1, 2], "DC1"),
            new ParametricSweepAnalysis("DC1_sweep_Vgs_sweep_Vds", "Vds", [0, 5], "DC1_sweep_Vgs"));

        Assert.Equal("Remove DC1",           list.Rows[0].RemoveLabel);
        Assert.Equal("Remove DC1 Vgs Sweep", list.Rows[1].RemoveLabel);
        Assert.Equal("Remove DC1 Vds Sweep", list.Rows[2].RemoveLabel);

        Assert.Equal("Run DC1",           list.Rows[0].RunLabel);
        Assert.Equal("Run DC1 Vgs Sweep", list.Rows[1].RunLabel);
        Assert.Equal("Run DC1 Vds Sweep", list.Rows[2].RunLabel);
    }

    /// <summary>
    /// The session reported is the one the panel is BOUND to, which is not necessarily the one the
    /// shell would have guessed: the panel retains its schematic, so an edit made while some other
    /// document is in front still belongs to this session's stack.
    /// </summary>
    [Fact]
    public void TheReportedSession_IsThePanelsOwn_NotWhateverIsInFront()
    {
        var (list, sch, _) = Panel(new DcAnalysis("DC1"));
        var other = new SchematicViewModel(new SchematicEditModel(), messageSink: null);

        SchematicViewModel? reported = null;
        list.EditCommitted += vm => reported = vm;

        list.DeleteRowCommand.Execute(list.Rows[0]);

        Assert.Same(sch, reported);
        Assert.False(other.UndoRedo.CanUndo);   // nothing landed on the document that was in front
    }
}
