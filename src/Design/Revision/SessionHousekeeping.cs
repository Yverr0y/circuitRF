using CircuitRF.Diagnostics;

namespace CircuitRF.Design.Revision;

/// <summary>What the close-time housekeeping did.</summary>
/// <param name="Swept">The retention pass, or null when none ran.</param>
/// <param name="Packed">What packing decided.</param>
/// <param name="Diagnostics">Everything worth reporting. <b>Normally empty</b> — housekeeping that
/// went as intended says nothing at all.</param>
public sealed record HousekeepingResult(
    SweepResult?              Swept,
    PackOutcome               Packed,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    /// <summary>Nothing ran, and nothing should have.</summary>
    public static readonly HousekeepingResult Skipped = new(null, PackOutcome.NotNeeded, []);

    /// <summary>True when this call did any work at all — what gate 10a' asserts against.</summary>
    public bool Ran => Swept is not null || Packed == PackOutcome.Packed;
}

/// <summary>
/// <b>When retention runs, which is the rule that makes the bound a guarantee</b>
/// (<c>docs/design/revision-control.md</c> §5.6, §12 Q16, §12 Q24; RC-6 R-rc6-4a).
///
/// <para><b>At most once per session, on workspace close, after the close checkpoint and in the same
/// window as packing.</b> R-rc6-3's bound guarantees nothing until the passes are counted: a sweep on
/// every checkpoint at a quarter empties the set inside a dozen checkpoints, while a sweep once per
/// session cannot. <b>Three alternatives each fail on a stated principle</b> — <i>on open</i> puts work
/// in front of the thing the designer asked for, <i>on a timer</i> is §5.3's rejected idle trigger
/// under a third name, and <i>on every checkpoint</i> is the case that makes the bound vacuous. Close
/// is the boundary §5.3 already identifies as reliably present in every session, it is behind the user
/// rather than in front of them, and it composes with packing — which stores what thinning made
/// unreachable compactly, and never reclaims it.</para>
///
/// <para><b>And a session that recorded nothing sweeps nothing and packs nothing</b> (§12 Q24). RC-5's
/// arming guard keeps a colleague's glance from creating a repository on a share; on its own it did not
/// keep that glance from running a sweep, under the READER'S retention preference, over the OWNER'S
/// restore points — a per-user setting acting on a shared artifact, which is §4.4's identity mistake in
/// a third file. A close that took no checkpoint does no housekeeping, and the assertion is on the
/// repository directory's bytes.</para>
///
/// <para><b>Two writers with different retention preferences on one shared workspace still apply
/// whichever closed last</b>, bounded by R-rc6-1's floor. The architecture records that as open and
/// this brief reports on it rather than settling it silently — see
/// <c>src/Design/RESOLVED.md</c>.</para>
/// </summary>
public sealed class SessionHousekeeping
{
    /// <summary>Whether this session has already run its one pass.</summary>
    public bool HasRunThisSession { get; private set; }

    /// <summary>A new workspace is a new session's worth of that.</summary>
    public void ResetForWorkspace() => HasRunThisSession = false;

    /// <summary>
    /// The one pass, at close.
    /// </summary>
    /// <param name="git">Bound to the workspace, whose repository circuitRF manages.</param>
    /// <param name="recordedSomethingThisSession">
    /// <b>Whether the close boundary actually wrote an entry</b> (§12 Q24). Not "was a checkpoint
    /// attempted" and not "is there a repository" — a session that only looked leaves the repository
    /// exactly as it found it.
    /// </param>
    /// <param name="policy">RC-4's retention preferences, or <see cref="RetentionPolicy.Default"/>.</param>
    /// <param name="packThresholdBytes">RC-4's packing threshold.</param>
    /// <param name="now">The clock, as an argument, so a gate can move it without moving the machine's.</param>
    public HousekeepingResult OnClose(
        GitCommand        git,
        bool              recordedSomethingThisSession,
        RetentionPolicy?  policy             = null,
        long              packThresholdBytes = GitPacking.DefaultThresholdBytes,
        DateTimeOffset?   now                = null,
        CancellationToken ct                 = default)
    {
        if (HasRunThisSession || !recordedSomethingThisSession) return HousekeepingResult.Skipped;

        // R-rc6-6, checked again here rather than trusted from the caller: housekeeping writes to the
        // repository, and a held one is not circuitRF's to write to whatever brought us here.
        if (!EnclosingRepository.Detect(git).MayRecord) return HousekeepingResult.Skipped;

        HasRunThisSession = true;

        var swept = RetentionSweep.Run(git, policy ?? RetentionPolicy.Default, now);

        // Packing SECOND, and that is the composition §5.6 describes: thinning is what makes objects
        // unreachable, and packing is what stores them compactly until §5.6a's explicit reclaim — which
        // may never come, because nothing automatic reclaims. GitPacking passes no --prune and the
        // repository's own `gc.pruneExpire = never` is what makes that a guarantee rather than an
        // omission.
        var (packed, packFailure) = GitPacking.Pack(git, packThresholdBytes, ct);

        List<Diagnostic> notes = [.. swept.Diagnostics];
        if (packFailure is not null) notes.Add(packFailure);

        return new HousekeepingResult(swept, packed, notes);
    }
}
