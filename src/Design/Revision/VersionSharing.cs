namespace CircuitRF.Design.Revision;

/// <summary>How much this workspace can say about what has left the machine.</summary>
public enum SharingReach
{
    /// <summary>There is no other copy at all — the ordinary state of a workspace whose history
    /// circuitRF started. <b>Nothing has been shared</b>, and that is a fact rather than a guess.</summary>
    NoOtherCopy,

    /// <summary>There is another copy and this machine knows where it is up to. The set is exact.</summary>
    Known,

    /// <summary>
    /// There is another copy and this machine has never heard from it — a remote configured and never
    /// fetched, or a reference read that failed. <b>Everything is treated as shared</b>, because a
    /// correction refused is recoverable and an erasure believed is not.
    /// </summary>
    CannotBeComputed,
}

/// <summary>
/// What has left this machine, as one answer computed once.
/// </summary>
/// <param name="Reach">Which of the three situations produced it.</param>
/// <param name="Identities">The versions reachable from the other copy. Empty in the two states where
/// the set is not the thing that decides.</param>
public sealed record SharedVersions(SharingReach Reach, IReadOnlySet<string> Identities)
{
    /// <summary>A workspace with no other copy, which is most of them.</summary>
    public static readonly SharedVersions Local =
        new(SharingReach.NoOtherCopy, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>
    /// <b>Whether this version has a second reader</b> — the hinge of <c>revision-control.md</c> §5.11.
    ///
    /// <para><see cref="SharingReach.CannotBeComputed"/> answers <b>yes</b> for every version, and that
    /// is the whole point of the state existing: §9A.1's rule says a default that can be wrong in two
    /// directions points away from the irreversible one, and here the irreversible direction is
    /// correcting a title somebody else is already holding.</para>
    /// </summary>
    public bool Contains(string commitId)
        => Reach switch
        {
            SharingReach.NoOtherCopy      => false,
            SharingReach.CannotBeComputed => true,
            _                             => commitId.Length > 0 && Identities.Contains(commitId),
        };
}

/// <summary>
/// <b>"Shared" is computed, never assumed</b> (<c>docs/design/revision-control.md</c> §5.11;
/// RC-11 R-rc11-2).
///
/// <para><b>It is a named function with its own tests rather than a condition inlined at three call
/// sites</b>, because it is the one predicate the whole of §5.11 turns on: a title nobody else has
/// seen is its author's to correct, and one that has left the machine is not. Three call sites each
/// deciding it for themselves is three chances for one of them to decide it the permissive way.</para>
///
/// <para><b>A version is shared when it is reachable from a remote-tracking reference</b>, and
/// anything else is local. That is the exact question — not "does a remote exist", which is true of
/// every clone the moment it is made and false of the versions kept in it since.</para>
///
/// <para><b>Where it cannot be computed the answer is SHARED</b> (<see cref="SharingReach"/>). A
/// remote configured but never fetched is the ordinary shape of a workspace somebody added a remote
/// to this morning, and this machine genuinely does not know what is on the other end. §9A.1's rule
/// decides the direction: <b>a correction refused is recoverable and an erasure believed is not</b>,
/// and case (c)'s annotation is available for any version at all, so the refusal costs a designer a
/// keystroke rather than an outcome.</para>
///
/// <para><b>Nothing here reaches a network.</b> It reads the reference a fetch already put on this
/// machine, so the answer does not change while a designer is looking at it, and the context menu that
/// asks does not pause on a slow link.</para>
///
/// <para><b>Cost: one process on a workspace with a remote and two on one without.</b> The set is
/// computed ONCE per list read and handed to every row —
/// <see cref="HistoryList.Build"/>'s <c>shared</c> parameter — never per row. A per-row
/// <c>merge-base --is-ancestor</c> would be one subprocess per entry on a panel that refreshes at
/// every boundary, which is the shape <see cref="RestorePoints"/>' own header measured at a third of a
/// second on a list of fifty.</para>
/// </summary>
public static class VersionSharing
{
    /// <summary>
    /// The identities that have left this machine, computed once.
    /// </summary>
    public static SharedVersions Compute(GitCommand git)
    {
        // No remote at all: nothing has been shared, and no further question is worth asking. This is
        // the common case and it costs one process.
        if (WorkspaceRemotes.OtherCopy(git) is null) return SharedVersions.Local;

        // A remote exists but this machine has never heard from it. R-rc11-2's safe direction.
        if (WorkspaceRemotes.IncomingRef(git) is not { } reference)
            return new SharedVersions(SharingReach.CannotBeComputed,
                                      new HashSet<string>(StringComparer.Ordinal));

        var listed = git.Run(["rev-list", reference], new GitRunOptions(ReadOnly: true));
        if (!listed.Ok)
            return new SharedVersions(SharingReach.CannotBeComputed,
                                      new HashSet<string>(StringComparer.Ordinal));

        return new SharedVersions(
            SharingReach.Known,
            new HashSet<string>(
                listed.StdOut.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                             .Select(s => s.Trim())
                             .Where(s => s.Length > 0),
                StringComparer.Ordinal));
    }

    /// <summary>
    /// Whether one version has a second reader. <b>For a single question only</b> — anything listing
    /// more than one entry calls <see cref="Compute"/> once and asks the answer, because this starts a
    /// process every time it is called.
    /// </summary>
    public static bool IsShared(GitCommand git, string commitId) => Compute(git).Contains(commitId);
}
