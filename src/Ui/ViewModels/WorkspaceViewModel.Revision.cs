using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using CircuitRF.Design.Revision;
using CircuitRF.Ui.Messages;
using CircuitRF.Ui.Revision;
using CircuitRF.Ui.Views.Dialogs;

namespace CircuitRF.Ui.ViewModels;

/// <summary>
/// <b>The window half of RC-5, RC-6 and RC-7</b> — the affordances, the two automatic boundaries,
/// going back, and the explicit commit (<c>docs/design/revision-control.md</c> §5.2, §5.3, §5.8;
/// R-rc5-4c, R-rc5-12, R-rc5-21, R-rc5-22, R-rc7-1, R-rc7-17).
///
/// <para><b>Two commands and two panels, never one of each</b> (R-rc7-9). Keep This State… writes a
/// restore point; Keep This Version… writes a version the designer titled. They look similar and are
/// not: one is a safety-net entry nobody else ever sees, the other is what gets shared. Merging the
/// commands would merge the histories, which §5 exists to prevent.</para>
///
/// <para><b>Kept in its own file because none of it is about editing a design.</b> Everything that
/// decides anything lives below the firewall in <see cref="WorkspaceHistoryService"/> and the
/// <c>src/Design</c> types under it — the same functions <c>circuitrf history</c> calls, which is
/// what stops a headless run and a window disagreeing about what a boundary does.</para>
/// </summary>
public partial class WorkspaceViewModel
{
    private WorkspaceHistoryService? _history;

    /// <summary>
    /// The service, built on first use so a workspace nobody ever asks about costs nothing.
    /// </summary>
    private WorkspaceHistoryService History
    {
        get
        {
            if (_history is null)
            {
                _history = new WorkspaceHistoryService(Messages);
                _history.Changed += RefreshRestorePointsPanel;
            }
            return _history;
        }
    }

    /// <summary>The workspace folder, or null when there is no workspace open.</summary>
    private string? WorkspaceRootDir
        => CurrentWorkspacePath is { } cws ? Path.GetDirectoryName(cws) : null;

    /// <summary>
    /// R-rc5-4a. Called by every path that writes a file into the open workspace.
    ///
    /// <para><b>This is what keeps a colleague's glance from creating a repository on a share.</b>
    /// An unarmed workspace arms on close only if circuitRF itself wrote something during the
    /// session, and that fact has to come from the edit session rather than from the disk — a file
    /// manager touching the folder is not circuitRF editing a design.</para>
    /// </summary>
    public void NoteWorkspaceWrite() => History.NoteWorkspaceWrite();

    // ── The explicit save-point (R-rc5-4c, §5.3's second boundary) ────────────────────────────────

    /// <summary>
    /// File ▸ <b>Keep This State…</b>
    ///
    /// <para><b>Beside the save commands, because that is where a user looking for it will be</b>
    /// (R-rc5-4c) — §5.3 describes it as something the user asks for. It is not a save: it keeps the
    /// state the workspace is in so the designer can come back to it.</para>
    ///
    /// <para><b>It is the interactive moment §8.2's guard is asked at</b> (R-rc5-15a). A close is at
    /// the moment the designer asked to leave and a batch is headless, so both of those leave an
    /// unexpectedly large file out and record that they did; this is where the question finally gets
    /// put to somebody.</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCloseWorkspace))]
    private async Task KeepThisState(Window? owner)
    {
        if (WorkspaceRootDir is not { } root) return;

        var dialog = new KeepThisStateDialog();
        dialog.Present(LargeFilesAwaitingAnAnswer(root), IsFirstRecording(root));

        var choice = owner is null
            ? new KeepThisStateChoice(null, [], [])
            : await dialog.ShowDialog<KeepThisStateChoice?>(owner);

        if (choice is null) return;

        // The pattern goes in FIRST, so this recording already honours it and the next boundary does
        // not ask again. Appended, never rewritten (R-rc3-11b) — the file belongs to the workspace.
        foreach (string pattern in choice.NeverInclude)
            LargeFileGuard.AppendIgnorePattern(root, pattern);

        History.TakeSavePoint(root, choice.Label);
        RefreshRestorePointsPanel();
    }

    /// <summary>
    /// What the guard has to ask about at this moment: over the threshold, not already kept, and not
    /// already excluded. An empty answer is the ordinary case and leaves the dialog one field.
    /// </summary>
    private IReadOnlyList<LargeFile> LargeFilesAwaitingAnAnswer(string root)
    {
        if (GitCommand.For(root) is not { } git || !git.IsRepositoryRoot()) return [];
        return LargeFileGuard.Find(git, RestorePoints.Newest(git)?.TreeId);
    }

    /// <summary>
    /// R-rc5-17a. Whether this is the FIRST recording into a workspace that already exists — which is
    /// a different operation from every later one, and is why the dialog summarises by pattern rather
    /// than naming hundreds of files.
    /// </summary>
    private static bool IsFirstRecording(string root)
        => GitCommand.For(root) is { } git
        && (!git.IsRepositoryRoot() || CheckpointReferences.List(git).Count == 0);

    // ── The close boundary (§5.3's third, R-rc5-21, R-rc5-22) ─────────────────────────────────────

    /// <summary>
    /// R-rc5-4's third boundary — <b>the one that reliably exists in every session</b>, including the
    /// ones where the designer never thought about history.
    ///
    /// <para><b>Called AFTER the workspace file has been written</b> (R-rc5-21), or the entry keeps a
    /// workspace file one save out of date. That is ordering, it costs nothing, and it is invisible
    /// when wrong: the restored workspace simply comes up with slightly stale configuration and
    /// nobody connects it to the close.</para>
    ///
    /// <para><b>It must not make quitting feel broken</b> (R-rc5-22). This is the one boundary that
    /// sits in front of the user, and it is a whole-workspace recording over what may be several
    /// multi-megabyte layouts. It is measured rather than asserted: the elapsed time goes to the
    /// trace listener where a build can read it, and the process does not exit until the recording has
    /// completed or failed — a failure still reports (R-rc5-10).</para>
    /// </summary>
    private void TakeCloseCheckpoint(string? cwsPath)
    {
        if (cwsPath is null) return;
        if (Path.GetDirectoryName(cwsPath) is not { Length: > 0 } root) return;

        var timer = Stopwatch.StartNew();
        History.TakeCloseCheckpoint(root);
        timer.Stop();

        LastCloseCheckpointMs = timer.Elapsed.TotalMilliseconds;
        Trace.WriteLine($"[circuitRF] close restore point: {timer.Elapsed.TotalMilliseconds:F0} ms");

        // RC-6 R-rc6-4a. The one housekeeping pass — a retention sweep and then packing — AFTER the
        // close entry and in the same window. It is here rather than at each of this method's three
        // callers because "at most once per session" is a property of the session, and three callers
        // agreeing about it is how it becomes true in two of them.
        //
        // A session that recorded nothing does neither (§12 Q24): a colleague's glance at a shared
        // workspace must not run a sweep under the reader's retention preference over the owner's
        // restore points.
        LastCloseHousekeeping = History.CloseHousekeeping(root);
    }

    /// <summary>What the close-time sweep and pack did, if anything. R-rc0-8's measurement, read by
    /// the write-up rather than asserted in a timing test.</summary>
    public HousekeepingResult? LastCloseHousekeeping { get; private set; }

    /// <summary>What the last close boundary cost, in milliseconds. R-rc0-8's measurement, read by
    /// the write-up rather than asserted in a timing test.</summary>
    public double LastCloseCheckpointMs { get; private set; }

    // ── The panel (R-rc5-4c) ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the restore-point list. <b>Called on every boundary and every workspace switch</b> —
    /// the panel holds no list of its own, because a stale one would offer a designer a way back to a
    /// state that is no longer there.
    /// </summary>
    private void RefreshRestorePointsPanel()
    {
        if (_factory.RestorePointsTool is not { } tool) return;

        tool.RestoreRequested    ??= point => _ = GoBackTo(point);
        tool.KeepRequested       ??= point => { History.Keep(WorkspaceRootDir, point); };
        tool.SavePointRequested  ??= () => _ = KeepThisState(Views.WorkspaceLocator.WindowFor(this));
        tool.BringBackRequested  ??= point => { History.BringBackThinned(WorkspaceRootDir, point); };

        // RC-6 R-rc6-4. The list carries the entries retention TIDIED AWAY as well as the live ones,
        // each marked. A state that silently vanished from the list is indistinguishable to a designer
        // from one that was destroyed — and this one has not been, and can be brought back.
        //
        // R-rc6-8/R-rc6-10: the buttons stay visible whatever the state, and this line is what makes a
        // hold or an off state legible instead of looking like a feature that was never built.
        var state = History.State(WorkspaceRootDir);
        tool.SetPoints(
            History.ListIncludingThinned(WorkspaceRootDir),
            WorkspaceRootDir is not null,
            state == RecordingState.On ? "" : HoldMessages.IndicatorDetailFor(state));

        RefreshRecordingIndicator();
    }

    // ── RC-7: keeping a version, and the browser (§5.2, §5.5, §6.3) ───────────────────────────────

    /// <summary>
    /// File ▸ <b>Keep This Version…</b> — R-rc7-1's explicit commit.
    ///
    /// <para><b>Beside Keep This State…, and the pair is deliberate.</b> They are different operations
    /// with different audiences: a save-point is a safety net entry nobody else ever sees, and a
    /// version is what a designer writes down on purpose and sends out. Merging the two commands would
    /// merge the two histories, which §5 exists to prevent.</para>
    ///
    /// <para><b>Two things the dialog says before the button is pressed.</b> Whether this version will
    /// record having been brought back from an earlier state (R-rc7-6) — discovering that in the list
    /// afterwards is how a history comes to read as a change of mind — and §8.3's sentence about
    /// rewriting (R-rc7-21), which is stated and never offered.</para>
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCloseWorkspace))]
    private async Task KeepThisVersion(Window? owner)
    {
        if (WorkspaceRootDir is not { } root) return;

        var dialog = new KeepThisVersionDialog();
        dialog.Present(RestoreProvenance.Read(root) is { } state
                           ? new RestoredFrom(state.Label, state.TakenUtc)
                           : null);

        var choice = owner is null
            ? new KeepThisVersionChoice(null)
            : await dialog.ShowDialog<KeepThisVersionChoice?>(owner);

        if (choice is null) return;

        History.KeepVersion(root, choice.Title);
        RefreshVersionHistoryPanel();
    }

    /// <summary>
    /// Rebuilds the version list. <b>Called whenever a version is kept and on every workspace
    /// switch</b> — the panel holds no list of its own, for the reason the restore-point panel holds
    /// none.
    /// </summary>
    private void RefreshVersionHistoryPanel()
    {
        if (_factory.VersionHistoryTool is not { } tool) return;

        tool.KeepVersionRequested ??= () => _ = KeepThisVersion(Views.WorkspaceLocator.WindowFor(this));
        tool.GoBackRequested      ??= version => _ = GoBackToVersion(version);

        if (!_versionSelectionWired)
        {
            _versionSelectionWired = true;
            tool.SelectionChanged += version =>
                tool.SetChanges(version is null ? [] : History.ChangesIn(WorkspaceRootDir, version));
        }

        var state = History.State(WorkspaceRootDir);
        tool.SetRows(
            History.VersionRows(WorkspaceRootDir),
            WorkspaceRootDir is not null,
            state == RecordingState.On ? "" : HoldMessages.IndicatorDetailFor(state));
    }

    /// <summary>Subscribed once. A handler added on every refresh would fire once per refresh, which
    /// is silent and gets worse the longer the session runs.</summary>
    private bool _versionSelectionWired;

    /// <summary>
    /// R-rc7-17. <b>Going back to a version is RC-5's restore with that version's tree as the
    /// source</b> — there is no second restore implementation, so everything R-rc5-12c guarantees
    /// applies unchanged, including the checkpoint of the state being replaced.
    ///
    /// <para>Both halves of R-rc5-12b belong here for the same reason they do for a restore point:
    /// unsaved work is offered up first, and the open documents are reloaded with their undo stacks
    /// discarded afterwards.</para>
    /// </summary>
    public async Task GoBackToVersion(HistoryVersion version)
    {
        var window = Views.WorkspaceLocator.WindowFor(this);
        if (WorkspaceRootDir is not { } root) return;

        if (window is not null && HasAnyDirtyWork(includeFloated: false)
            && !await PromptSaveBeforeClose(window, "going back to an earlier version", includeFloated: false))
            return;

        if (History.GoBackToVersion(root, version) is not { Ok: true }) return;

        await ReloadWorkspaceAfterFilesChangedUnderneath();
    }

    // ── Going back (§5.8) ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts the workspace back to one restore point.
    ///
    /// <para><b>Two halves, and the second is the one that is silent if missed</b> (R-rc5-12b):</para>
    /// <list type="bullet">
    ///   <item><description><b>Unsaved work is offered up first</b>, through the prompt that already
    ///   guards a close, an archive and a workspace copy — reused, not rewritten. A restore is built
    ///   from what is on disk; performed on top of dirty documents it produces a workspace matching
    ///   neither state.</description></item>
    ///   <item><description><b>Open documents are reloaded and their undo stacks discarded.</b> An
    ///   undo after a restore would re-apply the last few minutes of the REPLACED state onto the
    ///   RESTORED file, producing a document that existed at no moment ever: well-formed, openable,
    ///   and wrong. §1.3 names that as the failure this whole feature is written against, and it
    ///   would be this feature causing it.</description></item>
    /// </list>
    /// </summary>
    public async Task GoBackTo(RestorePoint point)
    {
        var window = Views.WorkspaceLocator.WindowFor(this);
        if (WorkspaceRootDir is not { } root) return;

        if (window is not null && HasAnyDirtyWork(includeFloated: false)
            && !await PromptSaveBeforeClose(window, "going back to an earlier state", includeFloated: false))
            return;

        if (History.Restore(root, point) is not { Ok: true }) return;

        await ReloadWorkspaceAfterFilesChangedUnderneath();
    }

    /// <summary>
    /// R-rc5-12b's second half, and R-rc5-7b's — <b>one implementation with two callers</b>, which is
    /// the requirement rather than a convenience: two reload paths that drift is the shape of defect
    /// §5.8 is written against.
    ///
    /// <para>Reopening the workspace is what discards the undo stacks, because the edit-session
    /// registry is cleared as part of the switch. Nothing here reaches into a stack to trim it — an
    /// undo stack describing a file that no longer contains what the stack describes has no correct
    /// contents, only an absence.</para>
    /// </summary>
    private async Task ReloadWorkspaceAfterFilesChangedUnderneath()
    {
        if (CurrentWorkspacePath is not { } cws) return;

        // Discarded rather than retired: by this point the designer has been prompted and answered,
        // so anything still dirty is deliberately gone. Retiring would refuse to drop it and the
        // reopened document would come back carrying edits to a file that no longer has them.
        _registry.Clear();
        _layoutRegistry.Clear();

        await SwitchToWorkspaceReporting(cws);
    }

    /// <summary>
    /// R-rc5-7b. What a batch's close asks the window to do, over R-rc5-7c's channel.
    ///
    /// <para><b>The same path a restore uses, and that is the requirement</b> rather than a
    /// convenience: an agent's edits through a batch are exactly the situation §5.8 works out in full
    /// for a restore — the window showing the old content over an undo stack describing edits the file
    /// no longer contains, and the window's next save discarding everything the batch did. Two reload
    /// implementations that drift is the shape of defect §5.8 is written against.</para>
    ///
    /// <para><b>Nothing was unsaved when the batch opened</b> (R-rc5-7a refused it otherwise), so the
    /// reload cannot lose anything — which is why that refusal comes first and why nothing is prompted
    /// for here.</para>
    /// </summary>
    public async Task ReloadAfterExternalChange(IReadOnlyList<string> relativePaths)
    {
        if (relativePaths.Count == 0) return;
        await ReloadWorkspaceAfterFilesChangedUnderneath();
    }

    // ── Opening a workspace (R-rc5-12c's last rule, R-rc5-4c) ─────────────────────────────────────

    /// <summary>
    /// What RC-5 does when a workspace opens: <b>nothing that creates anything</b> (R-rc5-4a — opening
    /// a workspace to look at it creates nothing), and two things that only read.
    /// </summary>
    private void OnWorkspaceOpenedForRevision()
    {
        History.ResetForWorkspace();

        // RC-6 R-rc6-9's first cadence, and R-rc6-14c's. One message saying what is NOT being kept and
        // why — for the ancestor row, the declined row, and a workspace whose own setting travelled
        // here switched off. It reads only.
        var situation = History.ReportStateOnOpen(WorkspaceRootDir);
        RefreshRecordingIndicator();

        // R-rc6-7a. The workspace-root row is the one that is a QUESTION rather than a report, and it
        // is asked ONCE — the answer goes in the repository's own marker, so a reopen does not ask
        // again. Deferred off the open path so a dialog never sits in front of the thing the designer
        // actually asked for.
        if (situation.NeedsAnAnswer) _ = AskAboutExistingHistory();

        // R-rc5-12c. A restore over thousands of files on a share can be cut off by a crash or a
        // dropped connection, and what it leaves is §1.3's failure exactly: a workspace that opens, is
        // well-formed, and is half of two states. Detected, never simulated.
        InterruptedRestore = History.ReportInterruptedRestore(WorkspaceRootDir);

        RefreshRestorePointsPanel();
    }

    /// <summary>The interrupted restore found on open, or null. Held so the two ways out — finish it,
    /// or go back to where it started — can act on it.</summary>
    public RestoreInFlight? InterruptedRestore { get; private set; }

    /// <summary>Carries an interrupted restore through to where it was going.</summary>
    public async Task FinishInterruptedRestore()
    {
        if (InterruptedRestore is not { } inFlight) return;

        History.FinishInterruptedRestore(WorkspaceRootDir, inFlight);
        InterruptedRestore = null;
        await ReloadWorkspaceAfterFilesChangedUnderneath();
    }

    // ── RC-6: the indicator, the question, and switching recording off (§5.6, §5.7, §12 Q4) ───────

    /// <summary>
    /// <b>The persistent, non-scrolling indicator</b> (R-rc6-9, R-rc6-10) — the measure most likely to
    /// actually prevent the false belief.
    ///
    /// <para>A scrolling log is read once and then trained against; the state is permanent for the
    /// session and is therefore displayed permanently, in the workspace window's own status strip.
    /// <b>One indicator, three reasons</b> — off, held, and a boundary that failed — because all three
    /// mean the same thing to a designer: nothing is being recorded right now. Three separate
    /// indicators would be three things to notice.</para>
    ///
    /// <para>Empty when recording is normal. A badge that is always there is a badge nobody reads, and
    /// on a machine with no git the feature does not exist at all (R-rc3-3).</para>
    /// </summary>
    public string RecordingIndicator
    {
        get => _recordingIndicator;
        private set { if (_recordingIndicator != value) { _recordingIndicator = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRecordingIndicator)); } }
    }
    private string _recordingIndicator = "";

    /// <summary>The sentence behind the indicator, which is where the reason lives.</summary>
    public string RecordingIndicatorDetail
    {
        get => _recordingIndicatorDetail;
        private set { if (_recordingIndicatorDetail != value) { _recordingIndicatorDetail = value; OnPropertyChanged(); } }
    }
    private string _recordingIndicatorDetail = "";

    /// <summary>Whether the strip shows anything at all.</summary>
    public bool HasRecordingIndicator => RecordingIndicator.Length > 0;

    /// <summary>Re-reads the recording state. Cheap, and called wherever it could have changed.</summary>
    public void RefreshRecordingIndicator()
    {
        var state = History.State(WorkspaceRootDir);
        RecordingIndicator       = HoldMessages.IndicatorFor(state);
        RecordingIndicatorDetail = HoldMessages.IndicatorDetailFor(state);
    }

    /// <summary>
    /// R-rc6-7a. <b>The workspace-root case is a QUESTION, not an offer.</b>
    ///
    /// <para>rev 3 offered adoption as a one-click action and never said what the user was choosing
    /// between. They are asked, told what keeping their own configuration costs — specifically, in
    /// recoverability terms rather than tidiness ones — and encouraged to adopt circuitRF's. Three
    /// answers, not two.</para>
    ///
    /// <para><b>Cancelling is not a fourth answer.</b> A closed dialog records nothing, so the question
    /// is put again next time the workspace opens — which is right: an unanswered question is not an
    /// answer, and the alternative is a workspace held forever because somebody pressed Escape.</para>
    /// </summary>
    public async Task AskAboutExistingHistory()
    {
        if (WorkspaceRootDir is not { } root) return;
        if (Views.WorkspaceLocator.WindowFor(this) is not { } owner) return;

        var answer = await new AdoptExistingHistoryDialog(Path.GetFileName(root)).ShowDialog<AdoptionAnswer?>(owner);
        if (answer is not { } chosen) return;

        History.AnswerAdoption(root, chosen);
        RefreshRecordingIndicator();
        RefreshRestorePointsPanel();
    }

    /// <summary>
    /// R-rc6-11. Stops recording for this workspace, or starts it again — <b>through the ordered
    /// transition</b>, never by writing the flag alone.
    ///
    /// <para>Reverse the order and the flag is set, circuitRF is already off, nothing is recorded, and
    /// the history simply stops with no entry saying why (R-rc6-14a). That is invisible from the flag,
    /// which is why the ordering lives below the firewall in one function rather than at each
    /// caller.</para>
    /// </summary>
    public void SetRecordingForThisWorkspace(bool on)
    {
        if (WorkspaceRootDir is null) return;

        if (on) History.TurnOn(WorkspaceRootDir);
        else    History.TurnOff(WorkspaceRootDir);

        RefreshRecordingIndicator();
        RefreshRestorePointsPanel();
    }
}
