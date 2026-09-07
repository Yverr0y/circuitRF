using CircuitRF.Diagnostics;

namespace CircuitRF.Design.Revision;

/// <summary>What arming a workspace produced.</summary>
/// <param name="Armed">Whether a boundary may now record. False means it records nothing, silently
/// unless <paramref name="Refusal"/> says otherwise.</param>
/// <param name="Git">The driver bound to this workspace, when there is one.</param>
/// <param name="RepositoryJustCreated">True on the ONE call that created it — what makes R-rc5-4b's
/// announcement fire once.</param>
/// <param name="Announcement">R-rc5-4b's single Messages entry, on that one call only.</param>
/// <param name="Refusal">Why nothing will be recorded, when that is worth saying — identity, chiefly.</param>
public sealed record ArmingResult(
    bool        Armed,
    GitCommand? Git,
    bool        RepositoryJustCreated,
    Diagnostic? Announcement,
    Diagnostic? Refusal);

/// <summary>
/// <b>When a workspace gains a history — which is at the first boundary that would record something,
/// never at open</b> (<c>docs/design/revision-control.md</c> §5.7a, §12 Q13, §12 Q24; R-rc5-4a,
/// R-rc5-4b, R-rc5-9).
///
/// <para><b>Opening a workspace to look at it creates nothing.</b> The case that makes this matter is
/// the one §7A and §4.7 both assume — a workspace on a share, belonging to somebody else — where
/// creating a repository because a colleague glanced at the folder is R-rc0-5's ambush pointed at a
/// directory instead of a history. It is also the whole of R-rc3-8's objection, answered without
/// defaulting the feature off.</para>
///
/// <para><b>"Would record something" is defined WITHOUT a repository</b> (§5.7a, §12 Q24), because
/// before one exists there is nothing to diff against. An unarmed workspace arms on close only if
/// circuitRF itself wrote a file into it during the session — which the edit session already knows,
/// with no question put to the disk — and always on a save-point or a batch, both of which are
/// requests. An ARMED workspace then applies R-rc5-5a's tree test, which is the ordinary case. Under
/// this rule the colleague's glance creates nothing, runs nothing and writes nothing.</para>
///
/// <para><b>Identity is a prerequisite, not an error to translate</b> (§4.4, R-rc5-9). A commit with
/// no author refuses outright, and on a fresh Windows machine <c>user.name</c> and <c>user.email</c>
/// are unset — so the first restore point circuitRF ever took would fail SILENTLY, since it is not
/// user-initiated and has no dialog to fail into. Checked here, before anything is created, and the
/// refusal names the Settings tab rather than a git command. <b>No synthetic committer is ever
/// invented</b>: a fabricated author would be indistinguishable from a real person of that name, and
/// it would break a search by author for the actual designer. The origin lives in the message
/// (§5.5), which is where the distinction belongs.</para>
/// </summary>
public static class WorkspaceArming
{
    /// <summary>
    /// R-rc5-4a. Whether this boundary is one that would record something in a workspace that has
    /// never recorded anything.
    /// </summary>
    /// <param name="circuitRfWroteAFileThisSession">
    /// Whether circuitRF itself wrote into the workspace folder during this session. The edit-session
    /// registry knows this; <b>the disk is never asked</b>, because "has anything changed" is not
    /// answerable before a repository exists and asking the filesystem would answer a different
    /// question — a colleague's file manager touching a folder is not circuitRF writing a design.
    /// </param>
    public static bool WouldRecordSomething(CheckpointOrigin origin, bool circuitRfWroteAFileThisSession)
        => origin switch
        {
            // Both are requests. A save-point IS the user asking; a batch's whole purpose is the floor
            // under something that is about to change, and refusing to arm for it would withhold the
            // floor at exactly the moment §1.2 asks for one.
            CheckpointOrigin.SavePoint or CheckpointOrigin.BeforeBatch => true,

            // Neither is. A close after a session that only looked, and a restore in a workspace
            // circuitRF has not written to, record nothing and create nothing.
            _ => circuitRfWroteAFileThisSession,
        };

    /// <summary>
    /// Arms the workspace for this boundary, creating the repository if this is the first one.
    ///
    /// <para><b>Nothing is created when the answer is no</b>, and nothing is reported either: absence
    /// is silent (R-rc3-3), and a designer who does not want a history should not learn one exists
    /// because they opened a folder.</para>
    /// </summary>
    /// <param name="workspaceRoot">The workspace folder. A scratch workspace has none and never
    /// reaches here (R-rc5-20).</param>
    /// <param name="keepHistoryPreference">The per-user preference, defaulting to
    /// <see cref="RevisionArming.KeepHistoryDefault"/>.</param>
    /// <param name="workspaceSetting">What the <c>.cws</c> recorded, or null. Outranks the preference.</param>
    public static ArmingResult Arm(
        string           workspaceRoot,
        CheckpointOrigin origin,
        bool             keepHistoryPreference,
        bool?            workspaceSetting,
        bool             circuitRfWroteAFileThisSession)
    {
        if (!RevisionArming.IsArmed(keepHistoryPreference, workspaceSetting))
            return new ArmingResult(false, null, false, null, null);

        if (!WouldRecordSomething(origin, circuitRfWroteAFileThisSession))
            return new ArmingResult(false, null, false, null, null);

        // R-rc3-3's silence. No git on this machine means the feature is not there at all — every
        // affordance hidden, nothing disabled, and nothing said.
        if (GitCommand.For(workspaceRoot) is not { } git)
            return new ArmingResult(false, null, false, null, null);

        // RC-6 R-rc6-6, BEFORE anything is created, and this is the check that has to come first.
        //
        // Without it, `IsRepositoryRoot()` answers false for a workspace INSIDE somebody else's
        // repository, so the code below would run `git init` at the workspace root and plant a nested
        // repository in their tree; and for a repository the USER created at the root it answers true
        // while the marker is absent, so the code below would rewrite their configuration and write
        // circuitRF's policy files without ever asking. Both are the ambush §7A.1 exists to prevent,
        // and both are silent. A HOLD is a refusal that SAYS SO (R-rc6-8): absent is harmless and is
        // hidden, held is a designer who may believe they are protected.
        var situation = EnclosingRepository.Detect(git);
        if (!situation.MayRecord)
            return new ArmingResult(false, git, false, null, HoldRefusalFor(situation));

        // §4.4 next, still BEFORE anything is created: a repository made for a machine that cannot
        // name a committer is a folder that will never hold anything.
        git.Identity ??= RevisionIdentity.Resolve(git);
        if (git.Identity is null)
            return new ArmingResult(false, git, false, null, GitFailures.NoIdentity());

        bool existed = situation.Placement == RepositoryPlacement.Managed;

        if (!existed)
        {
            var made = GitRepository.Create(git, RevisionManagement.Created);
            if (!made.Ok) return new ArmingResult(false, git, false, null, made.Diagnostic);
        }
        else
        {
            // R-rc3-11a: a workspace that predates this feature gets circuitRF's policy files the
            // moment it has a repository, not only when it was created.
            WorkspacePolicyFiles.Ensure(workspaceRoot);
        }

        var announcement = existed
            ? null
            : RestorePointMessages.HistoryStarted(Path.GetFileName(workspaceRoot.TrimEnd(
                  Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

        return new ArmingResult(true, git, !existed, announcement, null);
    }

    /// <summary>
    /// What a held workspace says when a boundary reaches it.
    ///
    /// <para><b>The workspace-root row answers with a refusal here and nothing more</b>, because it is
    /// a QUESTION the window asks (R-rc6-7a) and this path has no dialog. What matters is that it
    /// refuses rather than adopting the user's repository by default — the answer is theirs.</para>
    /// </summary>
    private static Diagnostic? HoldRefusalFor(RepositorySituation situation) => situation.Placement switch
    {
        RepositoryPlacement.Ancestor
            => HoldMessages.HeldByAncestorOnOpen(situation.RepositoryRoot ?? ""),
        RepositoryPlacement.UserRepositoryAtRoot or RepositoryPlacement.Declined
            => HoldMessages.HeldRefusal(),
        _   => null,
    };
}
