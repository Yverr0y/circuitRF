using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CircuitRF.Design.Revision;
using CircuitRF.Diagnostics;
using CircuitRF.Ui.Messages;
using CircuitRF.Ui.Theming;

namespace CircuitRF.Ui.Revision;

/// <summary>
/// <b>The window's half of RC-5</b> — the three boundaries, the restore-point list, and going back
/// (<c>docs/design/revision-control.md</c> §5.3, §5.7a, §5.8; R-rc5-4 … R-rc5-22).
///
/// <para><b>It owns no revision-control logic.</b> Every decision is made below the firewall by
/// <see cref="WorkspaceArming"/>, <see cref="WorkspaceCheckpoints"/>, <see cref="RestorePoints"/> and
/// <see cref="WorkspaceRestore"/> — the same functions <c>circuitrf history</c> calls, which is what
/// stops the two spellings drifting. What is here is what only a window has: the preference, the
/// Messages sink, and the knowledge that circuitRF wrote a file during this session.</para>
///
/// <para><b>Automatic boundaries post nothing on success</b> (R-rc5-11). They would drown the panel
/// and defeat the readability §5.3 exists for; they appear in the restore-point list, which is where
/// someone looking for one looks. <b>Failure is the exception</b> (R-rc5-10) — §1.4 forbids a designer
/// believing they are protected when they are not, and one failing quietly is the purest form of that
/// failure. The other exception is the very first arming (R-rc5-4b), which is not a restore point at
/// all: it is the creation of the thing restore points go into.</para>
/// </summary>
public sealed class WorkspaceHistoryService
{
    private readonly IMessageSink _messages;

    /// <summary>
    /// R-rc5-4a. Whether circuitRF itself has written a file into the workspace folder this session.
    ///
    /// <para><b>This is what makes a colleague's glance create nothing.</b> "Would this boundary
    /// record something" has to be answerable BEFORE a repository exists, because before one exists
    /// there is nothing to diff against — and the answer must not come from the filesystem, which
    /// would report a file manager touching the folder as though circuitRF had edited a design. The
    /// edit session already knows; this is where it says so.</para>
    /// </summary>
    public bool CircuitRfWroteAFileThisSession { get; private set; }

    /// <summary>Set by every path that writes into the open workspace.</summary>
    public void NoteWorkspaceWrite() => CircuitRfWroteAFileThisSession = true;

    /// <summary>
    /// R-rc6-4a. Whether a boundary in this session actually WROTE an entry.
    ///
    /// <para><b>This is what stops a colleague's glance running housekeeping</b> (§12 Q24). RC-5's
    /// arming guard keeps that glance from creating a repository; on its own it did not keep it from
    /// running a retention sweep, under the READER'S preference, over the OWNER'S restore points — a
    /// per-user setting acting on a shared artifact, which is §4.4's identity mistake in a third file.
    /// Not "was a boundary attempted" and not "is there a repository": a session that only looked
    /// leaves the repository exactly as it found it, down to the bytes.</para>
    /// </summary>
    public bool RecordedSomethingThisSession { get; private set; }

    /// <summary>A new workspace is a new session's worth of that knowledge.</summary>
    public void ResetForWorkspace()
    {
        CircuitRfWroteAFileThisSession = false;
        RecordedSomethingThisSession   = false;
        _housekeeping.ResetForWorkspace();
        _reportedOnOpen                = false;
    }

    /// <summary>R-rc6-4a's once-per-session pass. Held here because a session is what a window is.</summary>
    private readonly SessionHousekeeping _housekeeping = new();

    /// <summary>R-rc6-9's first cadence fires once per workspace, not once per boundary.</summary>
    private bool _reportedOnOpen;

    public WorkspaceHistoryService(IMessageSink messages) => _messages = messages;

    // ── The three boundaries ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// R-rc5-4's second boundary — <b>the explicit save-point, which is a request and therefore always
    /// arms</b> (R-rc5-4a) and is always kept (R-rc5-1f): the user's judgement about what matters
    /// beats any heuristic, and thinning it would discard exactly that judgement.
    /// </summary>
    /// <param name="label">The one optional line the dialog asked for. R-rc5-6b's reasoning applies
    /// here too — the intent is the whole value of the entry — and an entry with none shows
    /// <i>save-point</i> with its time, never a bare time (R-rc5-5a).</param>
    public bool TakeSavePoint(string? workspaceRoot, string? label)
        => Reach(workspaceRoot, CheckpointOrigin.SavePoint, label, attended: true);

    /// <summary>
    /// R-rc5-4's third boundary — <b>the one that reliably exists in every session</b>, including the
    /// ones where the designer never thought about history. Switchable (RC-4).
    ///
    /// <para><b>The workspace file is written BEFORE this runs</b> (R-rc5-21), or the entry keeps a
    /// workspace file one save out of date. That is ordering, it costs nothing, and it is invisible
    /// when wrong: the restored workspace simply comes up with slightly stale configuration and
    /// nobody connects it to the close. The caller does the write; this asserts nothing about it,
    /// because there is nothing here that could.</para>
    /// </summary>
    public bool TakeCloseCheckpoint(string? workspaceRoot)
    {
        var prefs = AppPreferencesIo.Load();
        if (prefs.RevisionCheckpointOnClose == false) return false;

        // R-rc5-15a. Nobody is at a close — it is the moment the designer asked to leave — so an
        // unexpectedly large new file is left out rather than asked about, and the entry says so.
        return Reach(workspaceRoot, CheckpointOrigin.WorkspaceClosed, label: null, attended: false);
    }

    /// <summary>
    /// The common path. <b>Everything that could stop a boundary is answered here, in one place</b>,
    /// so the three callers cannot each answer it differently.
    /// </summary>
    private bool Reach(string? workspaceRoot, CheckpointOrigin origin, string? label, bool attended)
    {
        if (workspaceRoot is not { Length: > 0 }) return false;

        var prefs   = AppPreferencesIo.Load();
        var setting = WorkspaceRevisionSetting.Read(WorkspaceRevisionSetting.CwsPathFor(workspaceRoot));

        var armed = WorkspaceArming.Arm(
            workspaceRoot, origin,
            prefs.RevisionKeepHistory ?? RevisionArming.KeepHistoryDefault,
            setting,
            CircuitRfWroteAFileThisSession);

        // R-rc5-4b, and it is the ONE exception to the silence above.
        if (armed.Announcement is { } announcement) _messages.PostDiagnostic(announcement);

        if (!armed.Armed || armed.Git is null)
        {
            // Absence is silent; a refusal is not (R-rc5-10). The only thing that reaches here with a
            // refusal is identity, which is precisely the failure §4.4 says would otherwise be
            // invisible on a fresh machine.
            if (armed.Refusal is { } why)
            {
                _lastBoundaryFailed = true;
                _messages.PostDiagnostic(RestorePointMessages.CouldNotTake(
                    WorkspaceCheckpoints.Describe(origin), why.Render()));
            }
            return false;
        }

        var outcome = WorkspaceCheckpoints.Take(armed.Git, origin, label, attended);

        foreach (var d in outcome.Diagnostics)
        {
            switch (d.Severity)
            {
                case DiagnosticSeverity.Error:
                    // R-rc6-10's third reason. A failed boundary means nothing since then is in the
                    // history, which is the same thing to a designer as off or held — so it lights the
                    // same indicator rather than a third one nobody would notice.
                    _lastBoundaryFailed = true;
                    _messages.PostDiagnostic(RestorePointMessages.CouldNotTake(
                        WorkspaceCheckpoints.Describe(origin), d.Render()));
                    break;

                // A warning is a nested repository left out, or a large file left out at an unattended
                // boundary. Both change what the entry CONTAINS, so both are said.
                case DiagnosticSeverity.Warning:
                    _messages.PostDiagnostic(d);
                    break;

                // Info here is "nothing had changed" (R-rc5-5a), which an automatic boundary drops and
                // a save-point reports, because the person who pressed it is owed an answer.
                case DiagnosticSeverity.Info when attended:
                    _messages.PostDiagnostic(d);
                    break;
            }
        }

        if (outcome.Recorded) RecordedSomethingThisSession = true;

        Changed?.Invoke();
        return outcome.Recorded;
    }

    /// <summary>Raised whenever the list may have changed, so the panel refreshes without polling.</summary>
    public event Action? Changed;

    // ── The list ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The restore points, newest first, or an empty list. <b>Never throws and never blocks on a
    /// writer</b> — every read below the firewall takes no optional locks, which is what lets a panel
    /// refresh while a boundary is being taken.
    /// </summary>
    public IReadOnlyList<RestorePoint> List(string? workspaceRoot)
        => Bind(workspaceRoot) is { } git ? RestorePoints.List(git) : [];

    /// <summary>
    /// R-rc5-1f's <b>keep</b> action, on any entry — §10B.3's "make it permanent", which exists here
    /// in Stage 2 before there is a Commit to turn anything into.
    /// </summary>
    public bool Keep(string? workspaceRoot, RestorePoint point)
    {
        if (Bind(workspaceRoot) is not { } git) return false;

        var outcome = RestorePoints.MarkKept(git, point);
        if (outcome.Diagnostic is { } d) _messages.PostDiagnostic(d);

        Changed?.Invoke();
        return outcome.Ok;
    }

    // ── Going back ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts the workspace back to one entry.
    ///
    /// <para><b>The caller must have offered up unsaved work first and must reload the open documents
    /// afterwards</b> (R-rc5-12b). Both belong to the window, and neither can be done from here;
    /// <c>WorkspaceViewModel</c>'s own restore command is the one caller and does both.</para>
    /// </summary>
    public RestoreResult? Restore(string? workspaceRoot, RestorePoint point)
    {
        if (Bind(workspaceRoot) is not { } git) return null;

        var result = WorkspaceRestore.Restore(git, point);
        foreach (var d in result.Diagnostics) _messages.PostDiagnostic(d);

        if (result.Ok)
        {
            _messages.Info(RestorePointMessages.RestoreCoversThisWorkspaceOnly);
            _messages.Info(RestorePointMessages.RestoreLeavesResultsAlone);
        }

        Changed?.Invoke();
        return result;
    }

    /// <summary>
    /// R-rc5-12c's last rule, on workspace open. <b>An interrupted restore is DETECTED, not
    /// simulated</b> — what a crash mid-restore leaves is §1.3's failure exactly: a workspace that
    /// opens, is well-formed, and is half of two states.
    /// </summary>
    public RestoreInFlight? ReportInterruptedRestore(string? workspaceRoot)
    {
        if (workspaceRoot is not { Length: > 0 }) return null;
        if (RestoreMarker.Read(workspaceRoot) is not { } inFlight) return null;

        _messages.PostDiagnostic(RestorePointMessages.RestoreWasInterrupted(
            inFlight.Target.Label, inFlight.Fallback.Label));

        return inFlight;
    }

    /// <summary>Carries an interrupted restore through to where it was going.</summary>
    public RestoreResult? FinishInterruptedRestore(string? workspaceRoot, RestoreInFlight inFlight)
    {
        if (Bind(workspaceRoot) is not { } git) return null;

        var result = WorkspaceRestore.Finish(git, inFlight);
        foreach (var d in result.Diagnostics) _messages.PostDiagnostic(d);

        Changed?.Invoke();
        return result;
    }

    // ── RC-6: the hold, off/on, retention (§5.6, §5.7, §12 Q4) ────────────────────────────────────

    /// <summary>Which of §12 Q4's four situations this workspace is in (R-rc6-7).</summary>
    public static RepositorySituation Situation(string? workspaceRoot)
        => EnclosingRepository.Detect(workspaceRoot);

    /// <summary>
    /// <b>What the persistent indicator says</b> (R-rc6-10) — one indicator, three reasons, and the
    /// reason is in its text. Held, off and a failure all mean the same thing to a designer:
    /// <i>nothing is being recorded right now.</i>
    ///
    /// <para><b>A machine with no git answers <see cref="RecordingState.On"/> and shows nothing</b>,
    /// which is not a lie by omission but R-rc3-3's silence: absence is harmless, and a designer who
    /// never had this feature must not be given a badge about it.</para>
    /// </summary>
    public RecordingState State(string? workspaceRoot)
    {
        if (workspaceRoot is not { Length: > 0 } || !Directory.Exists(workspaceRoot)) return RecordingState.On;
        if (GitCommand.For(workspaceRoot) is null) return RecordingState.On;

        if (Situation(workspaceRoot).IsHeld) return RecordingState.Held;

        var prefs   = AppPreferencesIo.Load();
        var setting = WorkspaceRevisionSetting.Read(WorkspaceRevisionSetting.CwsPathFor(workspaceRoot));

        if (!RevisionArming.IsArmed(prefs.RevisionKeepHistory ?? RevisionArming.KeepHistoryDefault, setting))
            return RecordingState.Off;

        return _lastBoundaryFailed ? RecordingState.Failed : RecordingState.On;
    }

    /// <summary>R-rc6-10's third reason. Set by the one place that reports a failed boundary.</summary>
    private bool _lastBoundaryFailed;

    /// <summary>
    /// R-rc6-9's <b>first cadence</b>: one message on workspace open, saying what is not being kept,
    /// why, and how to remedy it — <b>once per workspace</b>, never per boundary.
    ///
    /// <para>Returns the situation so the caller can put R-rc6-7a's question, which is the one row
    /// this does not report: the workspace-root case is a QUESTION, and a message about it would be
    /// answered by a dialog the user is already looking at.</para>
    /// </summary>
    public RepositorySituation ReportStateOnOpen(string? workspaceRoot)
    {
        var situation = Situation(workspaceRoot);
        if (_reportedOnOpen || workspaceRoot is not { Length: > 0 }) return situation;
        _reportedOnOpen = true;

        if (EnclosingRepository.OpenReportFor(situation) is { } held)
        {
            _messages.PostDiagnostic(held);
            return situation;
        }

        // R-rc6-14c. The flag lives in the .cws, so it TRAVELS: a clone or an archive of a workspace
        // that was switched off arrives switched off, and the recipient's own preference does not
        // override it. Reported at their first boundary rather than silently doing nothing — and only
        // when the workspace itself recorded the answer, because "the preference is off everywhere" is
        // a state the user set on this machine and does not need to be told about per workspace.
        if (WorkspaceRevisionSetting.Read(WorkspaceRevisionSetting.CwsPathFor(workspaceRoot)) == false)
            _messages.PostDiagnostic(HoldMessages.ArrivedSwitchedOff(
                RevisionSwitch.WorkspaceName(workspaceRoot)));

        return situation;
    }

    /// <summary>
    /// R-rc6-9's <b>second cadence</b>: a refusal on an attempt, saying why <b>and not restating the
    /// remedy</b> — the user has been told once, and repeating it on every attempt is how a message
    /// becomes noise.
    ///
    /// <para>Returns true when it refused, so the caller stops. <b>The affordance stays visible</b>
    /// (R-rc6-8): a hidden button is indistinguishable from a feature that was never there, and the
    /// failure this whole feature guards against is a designer who believes they are protected and is
    /// not.</para>
    /// </summary>
    public bool RefusedBecauseHeld(string? workspaceRoot)
    {
        if (!Situation(workspaceRoot).IsHeld) return false;
        _messages.PostDiagnostic(HoldMessages.HeldRefusal());
        return true;
    }

    /// <summary>
    /// R-rc6-7a. Records one answer to <i>"this workspace already keeps a history of its own"</i>, and
    /// the marker it writes is what makes the question asked <b>once</b> rather than on every open.
    /// </summary>
    public bool AnswerAdoption(string? workspaceRoot, AdoptionAnswer answer)
    {
        if (workspaceRoot is not { Length: > 0 } || GitCommand.For(workspaceRoot) is not { } git)
            return false;

        git.Identity ??= RevisionIdentity.Resolve(git);

        var outcome = RepositoryAdoption.Apply(git, answer);
        if (outcome.Diagnostic is { } d) _messages.PostDiagnostic(d);

        Changed?.Invoke();
        return outcome.Ok;
    }

    /// <summary>
    /// R-rc6-11/R-rc6-14a. Stops recording for this workspace — <b>writing the setting, then one final
    /// entry that records the change, and only then stopping.</b> Deletes nothing.
    /// </summary>
    public RevisionSwitchResult TurnOff(string? workspaceRoot) => Flip(workspaceRoot, on: false);

    /// <summary>R-rc6-11's symmetry. Resumes the existing history and records the resumption, which is
    /// the far end of R-rc6-13's gap.</summary>
    public RevisionSwitchResult TurnOn(string? workspaceRoot) => Flip(workspaceRoot, on: true);

    private RevisionSwitchResult Flip(string? workspaceRoot, bool on)
    {
        if (workspaceRoot is not { Length: > 0 }) return new RevisionSwitchResult(false, null, []);

        bool preference = AppPreferencesIo.Load().RevisionKeepHistory ?? RevisionArming.KeepHistoryDefault;

        var result = on ? RevisionSwitch.TurnOn(workspaceRoot, preference)
                        : RevisionSwitch.TurnOff(workspaceRoot, preference);

        foreach (var d in result.Diagnostics) _messages.PostDiagnostic(d);
        if (result.Transition is not null) RecordedSomethingThisSession = true;

        Changed?.Invoke();
        return result;
    }

    /// <summary>
    /// R-rc6-15. <b>An AI edit requested while recording is off says so before anything is modified,
    /// and offers to turn it back on.</b>
    ///
    /// <para>Switching revision control off switches off the one checkpoint RC-4 makes non-switchable,
    /// because it is §1.2 — the reason the feature exists. That is a legitimate thing to choose and an
    /// illegitimate thing to stumble into, so the conflict is resolved out loud, at the moment it
    /// matters. <b>The remedy is OFFERED rather than named</b> (§7A.3), which the Messages panel can
    /// carry; the sentence still reads correctly with no button on the end, because a headless sink
    /// drops the action.</para>
    /// </summary>
    public void ReportAiEditRefusedBecauseOff(string? workspaceRoot)
        => _messages.PostAction(
               MessageLevel.Warning,
               HoldMessages.AiEditWhileOff().Render(),
               HoldMessages.TurnRecordingOnAction,
               () => { TurnOn(workspaceRoot); return System.Threading.Tasks.Task.CompletedTask; });

    /// <summary>
    /// R-rc6-4a. <b>The one housekeeping pass, at close, after the close checkpoint.</b> A sweep and
    /// then a pack, and neither runs in a session that recorded nothing.
    /// </summary>
    public HousekeepingResult CloseHousekeeping(string? workspaceRoot)
    {
        if (Bind(workspaceRoot) is not { } git) return HousekeepingResult.Skipped;

        var prefs = AppPreferencesIo.Load();

        var result = _housekeeping.OnClose(
            git,
            RecordedSomethingThisSession,
            new RetentionPolicy(
                prefs.RevisionRetentionDays        ?? RevisionPreferenceDefaults.RetentionDays,
                prefs.RevisionMinimumRestorePoints ?? RevisionPreferenceDefaults.MinimumRestorePoints),
            (long)(prefs.RevisionPackThresholdMb ?? RevisionPreferenceDefaults.PackThresholdMb)
                * 1024 * 1024);

        foreach (var d in result.Diagnostics) _messages.PostDiagnostic(d);
        return result;
    }

    /// <summary>
    /// R-rc6-4. The restore points <b>including the ones retention thinned</b>, each thinned one
    /// marked — because a state that silently vanished from the list is indistinguishable from one
    /// that was destroyed.
    /// </summary>
    public IReadOnlyList<RestorePoint> ListIncludingThinned(string? workspaceRoot)
        => Bind(workspaceRoot) is { } git ? RestorePoints.ListIncludingThinned(git) : [];

    /// <summary>R-rc6-4's way back: one reference update, from the journal.</summary>
    public bool BringBackThinned(string? workspaceRoot, RestorePoint point)
    {
        if (Bind(workspaceRoot) is not { } git) return false;

        var outcome = RestorePoints.RestoreThinned(git, point);
        if (outcome.Diagnostic is { } d) _messages.PostDiagnostic(d);

        Changed?.Invoke();
        return outcome.Ok;
    }

    /// <summary>R-rc6-13's data, for the browser RC-7 builds.</summary>
    public IReadOnlyList<RevisionGap> Gaps(string? workspaceRoot)
        => RevisionGaps.Find(ListIncludingThinned(workspaceRoot));

    // ── Shared ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A driver bound to the workspace, or null. <b>Absence is silent</b> (R-rc3-3): no git and no
    /// repository both answer null and say nothing, because a designer who does not want this feature
    /// should never learn it exists.
    /// </summary>
    private static GitCommand? Bind(string? workspaceRoot)
    {
        if (workspaceRoot is not { Length: > 0 } || !Directory.Exists(workspaceRoot)) return null;
        if (GitCommand.For(workspaceRoot) is not { } git) return null;
        if (!git.IsRepositoryRoot()) return null;

        git.Identity ??= RevisionIdentity.Resolve(git);
        return git;
    }

    /// <summary>
    /// Whether this workspace has a history at all — what the Save As report asks before adding
    /// R-rc5-19's sentence, because a workspace with nothing to lose must not be told it lost it.
    /// </summary>
    public static bool HasHistory(string? workspaceRoot)
        => Bind(workspaceRoot) is { } git && CheckpointReferences.List(git).Count > 0;
}
