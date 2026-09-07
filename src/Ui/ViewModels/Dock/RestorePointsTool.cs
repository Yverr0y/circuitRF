using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using CircuitRF.Design.Revision;
using CircuitRF.Ui.Docking;

namespace CircuitRF.Ui.ViewModels.Dock;

/// <summary>
/// One row of the restore-point list, as a designer reads it.
///
/// <para><b>Times and intents, and nothing else</b> (R-rc5-2, R-rc0-6). There is no identifier, no
/// reference name and no vocabulary from the machinery underneath — including for the entry taken
/// just before going back, which is a restore point like any other and is not "the state before you
/// went back", a sentence with a shape borrowed from somewhere this list does not go.</para>
/// </summary>
public sealed class RestorePointRow
{
    public RestorePointRow(RestorePoint point) => Point = point;

    public RestorePoint Point { get; }

    /// <summary>The time a human reads. <b>The label only</b> — what is oldest is decided by
    /// circuitRF's own sequence, because a wall clock is user-writable state.</summary>
    public string When => Point.TakenUtc.ToLocalTime().ToString("HH:mm");

    /// <summary>The day, for a list that spans more than today.</summary>
    public string Day => Point.TakenUtc.ToLocalTime().ToString("ddd d MMM");

    public string Label => Point.Label;

    /// <summary>
    /// <b>How it came about, in one word</b> (§5.5). This is the distinction the list exists to draw:
    /// a designer scanning it has to be able to tell "I decided this was worth keeping" from
    /// "circuitRF kept this because I shut the lid" without opening either. The automatic one is not
    /// lesser — it is frequently the one that saves them — but it means something different, and a
    /// list that rendered them identically would be lying by omission.
    /// </summary>
    public string Origin => Point.Origin switch
    {
        CheckpointOrigin.SavePoint       => "you asked",
        CheckpointOrigin.WorkspaceClosed => "on closing",
        CheckpointOrigin.BeforeBatch     => "before an assistant",
        CheckpointOrigin.BeforeRestore   => "before going back",
        _                                => "",
    };

    /// <summary>Retention will never thin this one (R-rc5-1f).</summary>
    public bool Kept => Point.Kept;

    /// <summary>R-rc5-15a. Something was left out because nobody was there to be asked, and the row
    /// says so with the names rather than looking like a complete one.</summary>
    public bool IsIncomplete => Point.IsIncomplete;

    public string LeftOutSummary
        => Point.IsIncomplete ? "Left out: " + string.Join(", ", Point.LeftOut) : "";
}

/// <summary>
/// <b>Where restore points are presented</b> (<c>docs/design/revision-control.md</c> §5.1; R-rc5-2,
/// R-rc5-4c, R-rc5-12).
///
/// <para><b>The panel holds no history of its own.</b> The list is read from the workspace on demand
/// and refreshed when a boundary says it changed, exactly as the DRC panel holds no result of its
/// own — a stale list of restore points is worse than none, because it would offer a designer a way
/// back to a state that is no longer there.</para>
/// </summary>
public partial class RestorePointsTool : Tool
{
    /// <summary>What the list currently holds, newest first.</summary>
    public ObservableCollection<RestorePointRow> Points { get; } = [];

    [ObservableProperty] private RestorePointRow? _selected;

    /// <summary>True when there is a workspace with a history to show. Otherwise the panel says why
    /// it is empty rather than looking broken.</summary>
    [ObservableProperty] private bool _hasWorkspace;

    /// <summary>Invoked when the designer asks to go back to the selected entry. Wired by
    /// <c>WorkspaceViewModel</c>, which is the only thing that can offer up unsaved work first and
    /// reload the open documents afterwards (R-rc5-12b).</summary>
    public Action<RestorePoint>? RestoreRequested { get; set; }

    /// <summary>Invoked for §10B.3's "make it permanent" (R-rc5-1f).</summary>
    public Action<RestorePoint>? KeepRequested { get; set; }

    /// <summary>Invoked for the explicit save-point, which is also a File-menu action — the panel
    /// carries it too because this is where a designer is when they decide one is worth keeping.</summary>
    public Action? SavePointRequested { get; set; }

    public RestorePointsTool()
    {
        Id    = DockPanelIds.RestorePoints;
        Title = "Restore Points";
    }

    /// <summary>Replaces the list. Called on every boundary and on every workspace switch.</summary>
    public void SetPoints(IReadOnlyList<RestorePoint> points, bool hasWorkspace)
    {
        var wanted = points.Select(p => p.Sequence).ToList();
        var keep   = Selected?.Point.Sequence;

        Points.Clear();
        foreach (var p in points) Points.Add(new RestorePointRow(p));

        HasWorkspace = hasWorkspace;
        Selected     = keep is { } s ? Points.FirstOrDefault(r => r.Point.Sequence == s) : null;
    }

    public void Restore()   { if (Selected is { } row) RestoreRequested?.Invoke(row.Point); }
    public void Keep()      { if (Selected is { } row) KeepRequested?.Invoke(row.Point); }
    public void SavePoint() => SavePointRequested?.Invoke();
}
