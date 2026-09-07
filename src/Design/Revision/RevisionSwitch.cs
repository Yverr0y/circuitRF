using CircuitRF.Diagnostics;

namespace CircuitRF.Design.Revision;

/// <summary>What switching recording on or off for a workspace produced.</summary>
/// <param name="Ok">Whether the setting was recorded. False means the <c>.cws</c> could not be written
/// — a read-only workspace, chiefly — and nothing changed.</param>
/// <param name="Transition">The entry that marks this end of the gap, when one was taken.</param>
/// <param name="Diagnostics">What to say. One line on success, because this is a request and the
/// person who made it is owed an answer.</param>
public sealed record RevisionSwitchResult(
    bool                      Ok,
    RestorePoint?             Transition,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// <b>Turning recording off for a workspace, and back on</b>
/// (<c>docs/design/revision-control.md</c> §5.7; RC-6 R-rc6-11 … R-rc6-14c).
///
/// <para><b>Off means circuitRF stops writing. It never deletes anything.</b> Three reasons, the first
/// sufficient on its own: an off switch that destroyed a history would be the single most damaging
/// control in the application — nobody expects a checkbox to be irreversible, and by the time they
/// discover it was, there is nothing to discover it with. Off and on must be symmetric, so turning it
/// back on resumes the existing history rather than starting from nothing. And the escape hatch
/// requires the repository stay an ordinary git repository: off is circuitRF declining to write, not a
/// change to what is in the folder.</para>
///
/// <para><b>The transition is ORDERED, and the ordering is the whole of the side effect</b>
/// (R-rc6-14a). Turning it off writes the <c>.cws</c>, then takes one final entry recording that
/// change, and only THEN stops writing. Reverse the two and the flag is set, circuitRF is already off,
/// nothing is recorded, and the history simply stops with no entry saying why — which is precisely what
/// §5.7 claims does not happen, and it is invisible from the flag alone.</para>
///
/// <para><b>The pair is what gives the gap two ends</b> (R-rc6-13), which is what a browser needs in
/// order to render an off period as a GAP rather than as a quiet interval in which nothing happened to
/// be worth keeping. Both carry the kept mark
/// (<see cref="WorkspaceCheckpoints.IsAlwaysKept"/>): a pair retention could thin is a gap retention
/// could erase.</para>
///
/// <para><b>A workspace with no history yet records no transition, and that is right.</b> Creating a
/// repository in order to record that the designer does not want one would be the surprise §5.7a's
/// arming guard exists to prevent, pointed at a directory. The flag is written and nothing else
/// happens; there is no gap because there was never anything either side of it.</para>
/// </summary>
public static class RevisionSwitch
{
    /// <summary>
    /// Stops recording for this workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace folder.</param>
    /// <param name="keepHistoryPreference">RC-4's per-user preference, which the flag outranks.</param>
    public static RevisionSwitchResult TurnOff(string workspaceRoot, bool keepHistoryPreference)
        => Switch(workspaceRoot, keepHistoryPreference, on: false);

    /// <summary>
    /// Starts recording for this workspace again. <b>The history from before is untouched and still
    /// ordered correctly</b>, because nothing was ever removed.
    /// </summary>
    public static RevisionSwitchResult TurnOn(string workspaceRoot, bool keepHistoryPreference)
        => Switch(workspaceRoot, keepHistoryPreference, on: true);

    private static RevisionSwitchResult Switch(string workspaceRoot, bool keepHistoryPreference, bool on)
    {
        string cws  = WorkspaceRevisionSetting.CwsPathFor(workspaceRoot);
        string name = WorkspaceName(workspaceRoot);

        bool? before = WorkspaceRevisionSetting.Read(cws);
        bool  wasOn  = RevisionArming.IsArmed(keepHistoryPreference, before);

        // §5.7's ORDER: the workspace file first, so the entry that records the change contains it.
        if (!WorkspaceRevisionSetting.Write(cws, on))
            return new RevisionSwitchResult(false, null, [SettingNotWritten(name)]);

        if (wasOn == on) return new RevisionSwitchResult(true, null, []);

        var note = on ? HoldMessages.RecordingSwitchedOn(name) : HoldMessages.RecordingSwitchedOff(name);

        // Only a workspace that already has a repository circuitRF may write to marks the transition.
        // Turning OFF must never create one, and turning ON creates one exactly as any other request
        // does — through the arming path, which is the only thing that creates a repository at all.
        Diagnostic? refusal = null;
        var git = on
            ? ArmForTransition(workspaceRoot, keepHistoryPreference, out refusal)
            : ExistingRepository(workspaceRoot);

        if (git is null)
            return new RevisionSwitchResult(true, null, refusal is { } why ? [note, why] : [note]);

        var taken = WorkspaceCheckpoints.Take(
            git,
            on ? CheckpointOrigin.RecordingOn : CheckpointOrigin.RecordingOff,
            label: null,
            attended: true,
            forceRecord: true);

        List<Diagnostic> said = [note];
        foreach (var d in taken.Diagnostics)
            if (d.Severity == DiagnosticSeverity.Error)
                said.Add(RestorePointMessages.CouldNotTake(
                    WorkspaceCheckpoints.Describe(on ? CheckpointOrigin.RecordingOn
                                                     : CheckpointOrigin.RecordingOff), d.Render()));

        return new RevisionSwitchResult(true, taken.Point, said);
    }

    /// <summary>
    /// A driver for a repository that already exists and that circuitRF may write to, or null.
    ///
    /// <para><b>Creates nothing</b> — that is the whole reason this is not <c>WorkspaceArming.Arm</c>.
    /// A held workspace answers null too, because the transition cannot be recorded into a repository
    /// that is not circuitRF's, and R-rc6-6 does not bend for a setting change.</para>
    /// </summary>
    private static GitCommand? ExistingRepository(string workspaceRoot)
    {
        if (GitCommand.For(workspaceRoot) is not { } git) return null;
        if (!EnclosingRepository.Detect(git).MayRecord) return null;
        if (!git.IsRepositoryRoot()) return null;
        if (CheckpointReferences.List(git).Count == 0) return null;

        git.Identity ??= RevisionIdentity.Resolve(git);
        return git.Identity is null ? null : git;
    }

    /// <summary>The arming path, used only when turning recording ON — where creating a repository is
    /// exactly what the designer just asked for.</summary>
    private static GitCommand? ArmForTransition(
        string workspaceRoot, bool keepHistoryPreference, out Diagnostic? refusal)
    {
        var armed = WorkspaceArming.Arm(
            workspaceRoot, CheckpointOrigin.RecordingOn, keepHistoryPreference,
            workspaceSetting: true, circuitRfWroteAFileThisSession: true);

        refusal = armed.Refusal;
        return armed is { Armed: true, Git: { } git } ? git : null;
    }

    private static Diagnostic SettingNotWritten(string workspaceName) => Diagnostic.Create(
        "revision.off.setting-not-written",
        DiagnosticSeverity.Error,
        "circuitRF could not record that setting in '{workspace}' — the workspace could not be written "
      + "to. Nothing has changed.",
        ("workspace", workspaceName));

    internal static string WorkspaceName(string workspaceRoot)
    {
        try
        {
            return Path.GetFileName(workspaceRoot.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } n
                ? n : workspaceRoot;
        }
        catch (ArgumentException) { return workspaceRoot; }
    }
}
