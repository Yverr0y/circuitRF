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
        CheckpointOrigin.RecordingOff    => "recording turned off",
        CheckpointOrigin.RecordingOn     => "recording turned back on",
        _                                => "",
    };

    /// <summary>Retention will never thin this one (R-rc5-1f).</summary>
    public bool Kept => Point.Kept;

    /// <summary>
    /// R-rc6-4. <b>Retention tidied this one away, and it is still offered.</b>
    ///
    /// <para>Marked rather than hidden: thinning frees the state's storage without destroying it, and a
    /// row that silently vanished would be indistinguishable to a designer from one that had been
    /// deleted. "Still offered" and "kept for now" are different promises, so they read differently.</para>
    /// </summary>
    public bool Thinned => Point.Thinned;

    /// <summary>The word the row shows on the right — one of them at a time, because an entry the
    /// designer marked keep is never one retention thinned.</summary>
    public string Mark => Point.Thinned ? "tidied away" : Point.Kept ? "kept" : "";

    /// <summary>Whether that word is shown at all.</summary>
    public bool HasMark => Mark.Length > 0;

    /// <summary>A thinned row reads dimmer than a live one — <b>present but no longer being kept
    /// for you</b>, which is exactly what thinning means and is hard to say in a word.</summary>
    public double RowOpacity => Point.Thinned ? 0.55 : 1.0;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(CanKeepPermanently))]
    private RestorePointRow? _selected;

    /// <summary>Whether the two actions that act on a row have a row to act on.</summary>
    /// <remarks>
    /// This is not the greying R-rc6-8 forbids. That rule is about a state — held, off, failing —
    /// where a greyed control says <i>not available to you</i> and nothing about why, so the buttons
    /// stay live and refuse out loud instead. "Nothing is selected" needs no sentence: the list is
    /// right there and the remedy is to click a row.
    /// </remarks>
    public bool HasSelection => Selected is not null;

    /// <summary>
    /// Whether <b>keep permanently</b> has anything to do. An entry already marked keep is skipped by
    /// the operation itself, so a live button on one would be a button that does nothing.
    /// </summary>
    public bool CanKeepPermanently => Selected is { Kept: false };

    /// <summary>True when there is a workspace with a history to show. Otherwise the panel says why
    /// it is empty rather than looking broken.</summary>
    [ObservableProperty] private bool _hasWorkspace;

    /// <summary>
    /// R-rc6-8. <b>Held must not look like absent.</b>
    ///
    /// <para>RC-3 hides every affordance when git is missing, because absent is harmless. Held is a
    /// designer who may believe they are protected — so the actions stay VISIBLE and REFUSE, saying
    /// why. A hidden button is indistinguishable from a feature that was never there, and the failure
    /// this whole feature guards against is a designer who believes they are protected and is not.
    /// This is the exact inverse of RC-3's absence gate, and the pair is the point.</para>
    ///
    /// <para><b>Not <c>IsEnabled=false</c> either.</b> A greyed button says "not available to you" and
    /// nothing about why; a button that refuses out loud says what the state is, which is the whole
    /// content of R-rc6-9's second cadence.</para>
    /// </summary>
    [ObservableProperty] private string _recordingState = "";

    /// <summary>Whether the line saying why is shown. Bound rather than derived from a null check so
    /// the panel has one source of truth.</summary>
    public bool IsRecordingBlocked => RecordingState.Length > 0;

    partial void OnRecordingStateChanged(string value) => OnPropertyChanged(nameof(IsRecordingBlocked));

    /// <summary>Invoked when the designer asks to go back to the selected entry. Wired by
    /// <c>WorkspaceViewModel</c>, which is the only thing that can offer up unsaved work first and
    /// reload the open documents afterwards (R-rc5-12b).</summary>
    public Action<RestorePoint>? RestoreRequested { get; set; }

    /// <summary>Invoked for §10B.3's "make it permanent" (R-rc5-1f).</summary>
    public Action<RestorePoint>? KeepRequested { get; set; }

    /// <summary>R-rc6-4's way back: brings a thinned entry back into the live list, which is one
    /// reference update from the journal.</summary>
    public Action<RestorePoint>? BringBackRequested { get; set; }

    /// <summary>Invoked for the explicit save-point, which is also a File-menu action — the panel
    /// carries it too because this is where a designer is when they decide one is worth keeping.</summary>
    public Action? SavePointRequested { get; set; }

    public RestorePointsTool()
    {
        Id    = DockPanelIds.RestorePoints;
        Title = "Restore Points";
    }

    /// <summary>Replaces the list. Called on every boundary and on every workspace switch.</summary>
    public void SetPoints(IReadOnlyList<RestorePoint> points, bool hasWorkspace, string recordingState = "")
    {
        RecordingState = recordingState;

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

    /// <summary>R-rc6-4. Only ever meaningful on a thinned row, and harmless on any other.</summary>
    public void BringBack() { if (Selected is { Thinned: true } row) BringBackRequested?.Invoke(row.Point); }
}
