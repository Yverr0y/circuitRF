using CircuitRF.Diagnostics;

namespace CircuitRF.Design.Revision;

/// <summary>Which of §12 Q4's four situations a workspace is in.</summary>
public enum RepositoryPlacement
{
    /// <summary>No repository at or above the workspace. The ordinary state before arming.</summary>
    None,

    /// <summary>A repository circuitRF manages, at the workspace root. Normal operation.</summary>
    Managed,

    /// <summary>
    /// A repository the <b>user</b> created at the workspace root, with no answer recorded yet.
    /// <b>Hold, and ASK</b> (R-rc6-7a) — their intent is unambiguous and the root is exactly right, so
    /// taking it over unasked is presumptuous and refusing to offer is unhelpful.
    /// </summary>
    UserRepositoryAtRoot,

    /// <summary>
    /// The repository root is an <b>ancestor</b> of the workspace. <b>Hold, and no adoption offered</b>
    /// — the serious case, and there is nothing to offer, because circuitRF may not write into somebody
    /// else's repository root at all.
    /// </summary>
    Ancestor,

    /// <summary>The user was asked about their own repository at the workspace root and answered
    /// <i>don't keep history for this workspace</i>. The hold state, by choice.</summary>
    Declined,
}

/// <summary>What the detection found.</summary>
/// <param name="Placement">Which of the four rows this is.</param>
/// <param name="RepositoryRoot">
/// The repository's own root, when there is one. <b>Named in the ancestor report</b>, because
/// "somewhere above you" is not something a designer can act on.
/// </param>
/// <param name="Answer">The recorded answer to §12 Q4, when one has been given.</param>
/// <param name="Nested">
/// Workspace-relative paths of repositories NESTED inside the workspace. <b>These hold nothing</b>:
/// the subtree is excluded from every checkpoint by pathspec and reported once, and the workspace's
/// own history is otherwise normal (§7A.5).
/// </param>
public sealed record RepositorySituation(
    RepositoryPlacement   Placement,
    string?               RepositoryRoot,
    RevisionManagement?   Answer,
    IReadOnlyList<string> Nested)
{
    /// <summary>Whether circuitRF may record here at all (R-rc6-6). Nested repositories do not
    /// stop it; the other three non-normal rows do.</summary>
    public bool MayRecord => Placement is RepositoryPlacement.None or RepositoryPlacement.Managed;

    /// <summary>Whether this is a hold — <b>which is not the same as absent</b> (R-rc6-8). Absent is
    /// harmless and is hidden; held is a designer who may believe they are protected.</summary>
    public bool IsHeld => Placement is RepositoryPlacement.UserRepositoryAtRoot
                                    or RepositoryPlacement.Ancestor
                                    or RepositoryPlacement.Declined;

    /// <summary>Whether the user still has R-rc6-7a's question to answer.</summary>
    public bool NeedsAnAnswer => Placement == RepositoryPlacement.UserRepositoryAtRoot;

    /// <summary>Nothing found and nothing to say — what a machine with no git answers.</summary>
    public static readonly RepositorySituation Nothing =
        new(RepositoryPlacement.None, null, null, []);
}

/// <summary>
/// <b>circuitRF commits to exactly one repository: the one whose root IS the open workspace</b>
/// (<c>docs/design/revision-control.md</c> §7A.1, §12 Q4; RC-6 R-rc6-6, R-rc6-7).
///
/// <para>Not a repository above it, not a referenced workspace, not one nested inside. <b>The reason
/// is the same in all three directions</b>: an automatic recording in a repository the user also uses
/// for something else will sweep up work that was not circuitRF's to record. A checkpoint is
/// <c>commit -a</c>-shaped by nature — it must capture everything, because the whole point is
/// capturing files the user did not open. In someone else's repository that is not a safety net, it is
/// an ambush.</para>
///
/// <para><b>Three of the four rows are one <c>rev-parse --show-toplevel</c>; the fourth is a walk.</b>
/// <c>rev-parse</c> goes UP and never down, so a repository nested inside the workspace is invisible to
/// it. That walk matters mechanically as well as by principle: handing a directory that contains a
/// <c>.git</c> to <c>git add</c> records it as an EMBEDDED REPOSITORY — a pointer to that repository's
/// current commit — which is exactly §7A.5's "committed as something by the enclosing workspace", with
/// a warning nobody is reading.</para>
///
/// <para><b>The distinction between rows 1 and 2 cannot be made from the filesystem</b> (R-rc0-15). A
/// <c>.git</c> directory alone does not say who created it, and a user may perfectly well have written
/// <c>.gitignore</c> and <c>.gitattributes</c> themselves — so the answer comes from RC-3's management
/// marker in the repository's own config, which also records WHICH answer §12 Q4 got and is therefore
/// what makes the question asked once rather than on every open.</para>
/// </summary>
public static class EnclosingRepository
{
    /// <summary>
    /// Which situation <paramref name="workspaceRoot"/> is in.
    ///
    /// <para><b>Answers <see cref="RepositorySituation.Nothing"/> when there is no usable git</b>,
    /// because absence is silent (R-rc3-3) and a machine with no git has no hold to report — the
    /// feature is simply not there.</para>
    /// </summary>
    public static RepositorySituation Detect(string? workspaceRoot)
    {
        if (workspaceRoot is not { Length: > 0 } || !Directory.Exists(workspaceRoot))
            return RepositorySituation.Nothing;

        if (GitCommand.For(workspaceRoot) is not { } git) return RepositorySituation.Nothing;
        return Detect(git);
    }

    /// <inheritdoc cref="Detect(string?)"/>
    public static RepositorySituation Detect(GitCommand git)
    {
        // The fourth row, always: it is a directory walk over the workspace, and RC-5's checkpoint
        // enumerates those files anyway. It never changes the placement — a nested repository is
        // excluded and reported, not a reason to hold.
        var nested = NestedRepositories.Find(git.WorkspaceRoot);

        // ONE rev-parse decides rows one to three. `--show-prefix` rather than a path comparison, for
        // the reason GitCommand.IsRepositoryRoot's own header gives: git prints its symlink-resolved
        // view and .NET's GetFullPath does not resolve links, so a string compare answers wrongly
        // wherever a symlink sits above the workspace — which is every macOS /var and every network
        // home directory.
        if (git.IsRepositoryRoot())
        {
            var answer = GitRepository.ReadMarker(git);

            return answer switch
            {
                // No marker: the user made this, and has not been asked yet.
                null => new RepositorySituation(
                    RepositoryPlacement.UserRepositoryAtRoot, git.WorkspaceRoot, null, nested),

                // They were asked and said no. Held, by choice, and never asked again.
                RevisionManagement.Declined => new RepositorySituation(
                    RepositoryPlacement.Declined, git.WorkspaceRoot, answer, nested),

                // Created, Adopted or KeptUserSettings: circuitRF records here.
                _ => new RepositorySituation(
                    RepositoryPlacement.Managed, git.WorkspaceRoot, answer, nested),
            };
        }

        // Not the root. Either the workspace is inside somebody's repository, or it is in none.
        return git.TopLevel() is { } above
            ? new RepositorySituation(RepositoryPlacement.Ancestor, above, null, nested)
            : new RepositorySituation(RepositoryPlacement.None, null, null, nested);
    }

    /// <summary>
    /// R-rc6-9's first cadence — the one message a held workspace posts when it opens, saying what is
    /// not being kept, why, and <b>how to remedy it</b>. Null where there is nothing to say.
    ///
    /// <para><b>The workspace-root row is deliberately absent from this list</b>: it is a QUESTION
    /// (R-rc6-7a), not a report, and a message about it would be answered by a dialog the user is
    /// already looking at.</para>
    /// </summary>
    public static Diagnostic? OpenReportFor(RepositorySituation situation) => situation.Placement switch
    {
        RepositoryPlacement.Ancestor => HoldMessages.HeldByAncestorOnOpen(situation.RepositoryRoot ?? ""),
        RepositoryPlacement.Declined => HoldMessages.HeldByChoiceOnOpen(),
        _                            => null,
    };
}
