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
/// One row of the version history: <b>either a version the designer kept, or a stretch in which
/// circuitRF recorded nothing</b> (RC-7 R-rc7-10).
///
/// <para><b>The gap row is not decoration.</b> Rendering an off period as an ordinary interval between
/// two versions is §1.4's false-belief failure in its purest form — the designer reads a quiet
/// fortnight and concludes nothing happened, when what actually happened is that nothing was
/// recorded.</para>
/// </summary>
public sealed class VersionRow
{
    public VersionRow(HistoryVersion version) => Version = version;

    public VersionRow(RevisionGap gap)
    {
        Gap      = gap;
        GapText  = HistoryMessages.GapBetween(gap.FromUtc, gap.ToUtc);
    }

    public HistoryVersion? Version { get; }
    public RevisionGap?    Gap     { get; }

    public bool IsGap     => Gap is not null;
    public bool IsVersion => Version is not null;

    /// <summary>What the gap row says — the dates and the reason, because the reason is the whole
    /// content of the row.</summary>
    public string GapText { get; } = "";

    public string When => Version?.WhenUtc.ToLocalTime().ToString("HH:mm") ?? "";

    /// <summary>The day, for a list that spans more than today.</summary>
    public string Day => Version?.WhenUtc.ToLocalTime().ToString("ddd d MMM") ?? "";

    public string Title => Version?.Title ?? "";

    /// <summary>Who kept it. Blank on a single-designer workspace, where it says nothing.</summary>
    public string Who => Version?.Who ?? "";

    /// <summary>
    /// R-rc7-6. <b>What this version was brought back from, when it was.</b>
    ///
    /// <para>Without it, two consecutive versions where the second reverts the first read as a change
    /// of mind with no record of the moment — which is the one thing the narrative can lie about by
    /// omission.</para>
    /// </summary>
    public string RestoredFrom
        => Version?.RestoredFrom is { } from
            ? $"brought back from '{from.Label}', {from.TakenUtc.ToLocalTime():d MMM HH:mm}"
            : "";

    public bool WasRestored => RestoredFrom.Length > 0;

    /// <summary>
    /// R-rc9-6. <b>This version is on the copy this workspace came from and is not here yet.</b>
    ///
    /// <para>Marked rather than merged into the list, for the reason a thinned restore point is marked
    /// rather than hidden: <i>here</i> and <i>available to take</i> are different promises, and a row
    /// that read the same for both would tell a designer their workspace contains something it does
    /// not. Going back to one writes its files over this workspace — after keeping what is here now,
    /// like every other restore.</para>
    /// </summary>
    public bool OnTheOtherCopy => Version is { OnTheOtherCopy: true };

    /// <summary>The line that says so, on the row itself.</summary>
    public string OtherCopyNote
        => OnTheOtherCopy ? "on the copy this came from — not here yet" : "";

    /// <summary>An incoming version reads dimmer than one of this workspace's own — present and
    /// offered, but not part of this history until it is taken.</summary>
    public double RowOpacity => OnTheOtherCopy ? 0.75 : 1.0;

    /// <summary>
    /// R-rc7-4. <b>The one identifier this feature ever shows a designer</b>, and only because they
    /// pressed the button that made it. It is what they, or someone helping them, gives to a git
    /// command when circuitRF's own window cannot answer the question.
    /// </summary>
    public string Identity
        => Version is { CommitId.Length: > 12 } v ? v.CommitId[..12] : Version?.CommitId ?? "";
}

/// <summary>One document that differs between two versions (R-rc7-11).</summary>
public sealed class VersionChangeRow
{
    public VersionChangeRow(DocumentChange change) => Change = change;

    public DocumentChange Change { get; }

    public string Path => Change.RelativePath;

    /// <summary>What happened to it, in a word a designer reads rather than a status letter.</summary>
    public string Kind => Change.Kind switch
    {
        DocumentChangeKind.Added   => "added",
        DocumentChangeKind.Removed => "removed",
        DocumentChangeKind.Renamed => "moved",
        _                          => "changed",
    };

    public string Detail => Change.PreviousPath is { } was ? "was " + was : "";
    public bool   HasDetail => Detail.Length > 0;
}

/// <summary>
/// <b>The history browser — the narrative half</b> (<c>docs/design/revision-control.md</c> §5.2,
/// §5.7; RC-7 R-rc7-9, R-rc7-10, R-rc7-11).
///
/// <para><b>A separate panel from the restore points, and that is the requirement</b> (R-rc7-9). The
/// two may sit side by side and must not interleave: this is the sparse, deliberate history a designer
/// wrote down on purpose and shares; that one is the dense, automatic, local safety net. §5's whole
/// argument is that conflating them produces a log no human will read — which then makes the safety
/// net useless too, because nobody looks at it.</para>
///
/// <para><b>The panel holds no history of its own</b>, exactly as the restore-point panel does not: a
/// stale list would offer a designer a way back to a version that is no longer the one they are
/// looking at.</para>
/// </summary>
public partial class VersionHistoryTool : Tool
{
    /// <summary>The rows, newest first — versions and gaps together, in one ordering.</summary>
    public ObservableCollection<VersionRow> Rows { get; } = [];

    /// <summary>What the selected version changed, against the one before it (R-rc7-11).</summary>
    public ObservableCollection<VersionChangeRow> Changes { get; } = [];

    [ObservableProperty] private VersionRow? _selected;

    /// <summary>True when there is a workspace to show versions of. Otherwise the panel says why it
    /// is empty rather than looking broken.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private bool _hasWorkspace;

    /// <summary>
    /// R-rc7-9's line: what an empty list means. <b>A workspace with restore points and no versions is
    /// the ordinary state of one nobody has deliberately kept a version of</b>, which is most of them
    /// — so the empty panel explains the feature rather than reading as a failure.
    /// </summary>
    public string EmptyText => HasWorkspace ? HistoryMessages.NothingKeptYet
                                            : HistoryMessages.NoWorkspaceOpen;

    /// <summary>R-rc7-11's caveat, said where a designer might expect more of the list than it
    /// gives.</summary>
    public string ComparisonNote => HistoryMessages.ComparisonIsByDocument;

    /// <summary>
    /// R-rc7-2. <b>Held must not look like absent.</b> Non-empty when nothing is being recorded, and
    /// the buttons stay visible and refuse rather than disappearing — a hidden control is
    /// indistinguishable from a feature that was never built.
    /// </summary>
    [ObservableProperty] private string _recordingState = "";

    public bool IsRecordingBlocked => RecordingState.Length > 0;

    partial void OnRecordingStateChanged(string value) => OnPropertyChanged(nameof(IsRecordingBlocked));

    /// <summary>True when the selection is a version rather than a gap — a gap has nothing to go back
    /// to and nothing to compare.</summary>
    public bool HasVersionSelected => Selected is { IsVersion: true };

    partial void OnSelectedChanged(VersionRow? value)
    {
        OnPropertyChanged(nameof(HasVersionSelected));
        SelectionChanged?.Invoke(value?.Version);
    }

    /// <summary>Raised when the selected version changes, so the host can fill in
    /// <see cref="Changes"/> without this panel reaching below the firewall itself.</summary>
    public event Action<HistoryVersion?>? SelectionChanged;

    /// <summary>Invoked for the explicit commit (R-rc7-1). Wired by <c>WorkspaceViewModel</c>.</summary>
    public Action? KeepVersionRequested { get; set; }

    /// <summary>
    /// R-rc7-17. Invoked when the designer asks to go back to the selected version. <b>It goes through
    /// RC-5's restore</b> — the caller is the only thing that can offer up unsaved work first and
    /// reload the open documents afterwards.
    /// </summary>
    public Action<HistoryVersion>? GoBackRequested { get; set; }

    public VersionHistoryTool()
    {
        Id    = DockPanelIds.VersionHistory;
        Title = "Versions";
    }

    /// <summary>
    /// R-rc9-6. How many of the rows are versions a Pull brought in and this workspace does not have.
    /// <b>Said above the list as well as marked on each row</b>: the count is the answer to "did that
    /// Pull do anything", and counting marked rows is not something to ask of a reader.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIncoming))]
    [NotifyPropertyChangedFor(nameof(IncomingText))]
    private int _incomingCount;

    public bool HasIncoming => IncomingCount > 0;

    public string IncomingText => IncomingCount switch
    {
        0 => "",
        1 => "1 version on the copy this workspace came from is not here yet. "
           + "Select it to see what it changes, or go back to it to bring it in.",
        _ => $"{IncomingCount} versions on the copy this workspace came from are not here yet. "
           + "Select one to see what it changes, or go back to it to bring it in.",
    };

    /// <summary>Replaces the list. Called whenever a version is kept and on every workspace switch.</summary>
    public void SetRows(IReadOnlyList<HistoryRow> rows, bool hasWorkspace, string recordingState = "")
    {
        RecordingState = recordingState;

        string? keep = Selected?.Version?.CommitId;

        Rows.Clear();
        foreach (var row in rows)
            Rows.Add(row.IsGap ? new VersionRow(row.Gap!) : new VersionRow(row.Version!));

        HasWorkspace  = hasWorkspace;
        IncomingCount = Rows.Count(r => r.OnTheOtherCopy);
        Selected      = keep is { } id
                      ? Rows.FirstOrDefault(r => r.Version?.CommitId == id)
                      : null;
    }

    /// <summary>Replaces what the selected version changed.</summary>
    public void SetChanges(IReadOnlyList<DocumentChange> changes)
    {
        Changes.Clear();
        foreach (var c in changes) Changes.Add(new VersionChangeRow(c));
    }

    public void KeepVersion() => KeepVersionRequested?.Invoke();

    public void GoBack()
    {
        if (Selected?.Version is { } version) GoBackRequested?.Invoke(version);
    }
}
