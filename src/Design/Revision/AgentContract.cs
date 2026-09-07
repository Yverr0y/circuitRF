namespace CircuitRF.Design.Revision;

/// <summary>Whether this workspace has a floor under it, in the four states the agent surface
/// reports (<c>docs/design/revision-control.md</c> §5.3b, R-rc5-6g).</summary>
public enum RevisionAvailability
{
    /// <summary>circuitRF keeps a history here and a batch may open.</summary>
    On,

    /// <summary>The designer turned it off for this workspace. A legitimate choice, and a batch is
    /// refused rather than run without a floor.</summary>
    Off,

    /// <summary>There is a repository here that circuitRF does not manage (RC-6's held state). Not
    /// circuitRF's to write into.</summary>
    Held,

    /// <summary>No git on this machine, or nowhere to record into — a workspace with no folder yet.</summary>
    Unavailable,
}

/// <summary>
/// <b>What <c>serve</c> advertises about a workspace, and the rules it publishes next to it</b>
/// (<c>docs/design/revision-control.md</c> §5.3b; R-rc5-6g, R-rc5-6h, R-rc5-6i).
///
/// <para><b>The state is advertised, not inferred.</b> An agent left to deduce this from the
/// filesystem will deduce it wrongly — a <c>.git</c> directory alone does not distinguish a
/// repository circuitRF created from one the user created, and RC-6 makes those opposite
/// situations.</para>
///
/// <para><b>The rules are published by the surface, next to the state.</b> A capable agent has a
/// shell and a git of its own; it can read this repository and write to it whether or not anyone
/// intended that. Publishing the rules means an agent learns them from the server rather than from
/// having read an architecture document it was never given.</para>
///
/// <para><b>Rules 3 and 9 are the load-bearing pair</b> (R-rc5-6i). Everything else prevents damage;
/// those two prevent the HELPFUL failure — an agent that, finding no checkpoint mechanism available,
/// makes its own arrangements and reports success. That produces either entries in a history the
/// designer did not author or a private backup nothing knows about, and in both cases the designer is
/// told they are protected by a mechanism they cannot go back through. <b>Which is why every refusal
/// in <see cref="BatchSession"/> names what to do instead:</b> an agent given a vague refusal will
/// improvise, which is exactly what rule 9 forbids and what good tool design should make
/// unnecessary.</para>
/// </summary>
public static class AgentContract
{
    /// <summary>
    /// §5.3b's canonical ten, <b>reproduced and not paraphrased</b>. A rule an agent cannot read is a
    /// rule that does not exist, so these travel with the state on every advertisement.
    /// </summary>
    public static readonly IReadOnlyList<string> Rules =
    [
        "Open a batch before your first modification, and state your intent in one line. A batch "
      + "opened afterwards protects nothing.",

        "Read git freely; write only through the batch. log, show, diff, status and rev-parse are the "
      + "fastest way to answer what changed. Every write is circuitRF's.",

        "Do not commit — not to the designer's branch, which is their own record, and not to "
      + "circuitRF's checkpoint namespace, which carries metadata and an ordering sequence you cannot "
      + "supply.",

        "Do not rewrite, and do not reclaim. No amend, rebase, reset, filter or forced push; no gc, "
      + "prune or reflog expire. circuitRF's configuration exists so a mistaken deletion stays "
      + "recoverable until the designer chooses otherwise, and one housekeeping command undoes that "
      + "for everyone.",

        "Do not change what the working tree contains. No checkout, branch, switch, stash or restore — "
      + "the files are open documents in a running application.",

        "Do not touch configuration or .git itself.",

        "Do not reach the network. Fetch, push and clone are explicit user actions.",

        "Do not force a file past .gitignore.",

        "If the batch cannot be opened — off, held, no git, or an unsaved workspace — stop and say so. "
      + "Do not substitute a commit of your own, a folder copy, or any other improvised backup.",

        "Never report a checkpoint you did not take.",
    ];

    /// <summary>
    /// What state a workspace is in, answered once so nothing has to guess (R-rc5-6g).
    /// </summary>
    /// <param name="workspaceRoot">The workspace folder, or null for one with no folder yet.</param>
    public static RevisionAvailability StateOf(
        string? workspaceRoot, bool keepHistoryPreference, bool? workspaceSetting)
    {
        // §5.9. A repository is a directory inside a workspace folder; a workspace with no folder has
        // nowhere to record into, and the posture is ABSENT rather than disabled.
        if (workspaceRoot is not { Length: > 0 } || !Directory.Exists(workspaceRoot))
            return RevisionAvailability.Unavailable;

        if (GitCommand.For(workspaceRoot) is not { } git) return RevisionAvailability.Unavailable;

        // Held is decided BEFORE off, and the order matters: a repository circuitRF does not manage
        // is not circuitRF's to write into whatever any preference says, and reporting it as "off"
        // would invite an agent to suggest turning something on that would not help.
        //
        // Through RC-6's own detection, not a `.git` test of its own (R-rc6-6). Two of the four rows
        // are invisible to a test at the workspace root: a repository an ANCESTOR owns leaves
        // `IsRepositoryRoot()` false, which rev 5's spelling reported as ON — so an agent would have
        // been told it had a floor under it, in the one situation where a checkpoint must never be
        // taken at all. And a workspace whose owner ANSWERED "don't keep history" carries a marker,
        // which `IsManagedByCircuitRf` reports as managed.
        if (!EnclosingRepository.Detect(git).MayRecord) return RevisionAvailability.Held;

        return RevisionArming.IsArmed(keepHistoryPreference, workspaceSetting)
            ? RevisionAvailability.On
            : RevisionAvailability.Off;
    }

    /// <summary>The state's own word, for the surface's output. A wire spelling — do not rename.</summary>
    public static string Spell(RevisionAvailability state) => state switch
    {
        RevisionAvailability.On          => "on",
        RevisionAvailability.Off         => "off",
        RevisionAvailability.Held        => "held",
        _                                => "unavailable",
    };
}
