using CircuitRF.Diagnostics;

namespace CircuitRF.Design.Revision;

/// <summary>
/// One entry in RC-6's thinning journal: a state that was recorded, later thinned, and whose objects
/// are still in the repository because <b>nothing automatic ever reclaims them</b>.
/// </summary>
/// <param name="CommitId">The commit the thinned reference used to point at.</param>
/// <param name="ThinnedUtc">
/// <b>When it was THINNED, which is not when it was made</b> — and that difference is the whole reason
/// the journal exists. git's own expiry is by OBJECT AGE, so a two-year-old restore point thinned
/// yesterday would be destroyed by "thinned more than a month ago" without this.
/// </param>
/// <param name="Reference">
/// The reference the sweep dropped. <b>What makes restoring a thinned entry ONE reference update</b>
/// (RC-6 R-rc6-4) rather than an escape-hatch expedition through <c>git fsck --unreachable</c>.
/// Null on an entry written before this field existed, which lists but cannot be restored in place —
/// <see cref="CheckpointReferences.NameFor"/> rebuilds it from <paramref name="Sequence"/> instead.
/// </param>
/// <param name="Sequence">R-rc5-8's ordering, so a thinned entry sits where it belongs in the list.</param>
/// <param name="Label">The line the list shows. Carried so listing a thinned entry costs no object read.</param>
public sealed record ThinnedState(
    string         CommitId,
    DateTimeOffset ThinnedUtc,
    string?        Reference = null,
    long           Sequence  = 0,
    string?        Label     = null);

/// <summary>What a reclaim did.</summary>
/// <param name="Reclaimed">The journal entries acted on — RC-6 removes exactly these.</param>
/// <param name="Protected">The entries held back because they were newer than the age.</param>
public sealed record ReclaimResult(
    IReadOnlyList<ThinnedState> Reclaimed,
    IReadOnlyList<ThinnedState> Protected,
    Diagnostic?                 Diagnostic = null);

/// <summary>
/// <b>Reclaiming space is explicit, and nothing in this brief calls it</b> (<c>revision-control.md</c>
/// §5.6a, §12 Q20, R-rc3-16a).
///
/// <para><b>Why it is a separate operation at all.</b> rev 3 wrote <c>gc.pruneExpire = never</c> into
/// §4.5 and left "until ordinary packing eventually reclaims them" in the retention rules. Both could
/// not be true. §1.4 decided it: <b>no automatic operation may destroy history, and reclaiming
/// unreachable objects is destruction</b> — of nothing a designer can see, but of the only copy of a
/// thinned state. So nothing automatic reclaims, ever, and the repository grows by whatever thinning
/// freed. For design documents that is kilobytes per checkpoint and does not matter; for a large import
/// somebody included and later regretted, it is the whole import, for as long as they leave it.</para>
///
/// <para><b>The protection step is not optional.</b> git's expiry is by object age — when a state was
/// MADE — so without giving each newer journal entry a temporary reference first, "reclaim what was
/// thinned more than a month ago" would destroy a state thinned yesterday merely because it was
/// recorded two years ago.</para>
///
/// <para>RC-4 exposes this as the Settings tab's one confirmed destructive action, carrying the
/// consequence sentence plainly: after it, those states cannot be brought back by anyone. RC-6 owns the
/// journal it reads. It destroys ONLY what has already been thinned — every restore point and every
/// commit survives it unchanged — so it is neither the "delete all history" §5.7 refuses nor the
/// history rewriting §8.3 refuses.</para>
/// </summary>
public static class GitReclaim
{
    /// <summary>The reference namespace the temporary protections live in, for the duration only.</summary>
    public const string ProtectionRefPrefix = "refs/crf/reclaim-protect/";

    /// <summary>
    /// Reclaims the objects of every journal entry thinned longer ago than
    /// <paramref name="thinnedLongerAgoThan"/>.
    ///
    /// <para><b>With no age given, nothing is reclaimed</b> — deliberately. This is the one destructive
    /// control in the whole design, and a missing argument must never mean "all of it".</para>
    /// </summary>
    public static ReclaimResult Reclaim(
        GitCommand                  git,
        IReadOnlyList<ThinnedState> journal,
        TimeSpan?                   thinnedLongerAgoThan,
        DateTimeOffset?             now = null,
        CancellationToken           ct  = default)
    {
        if (thinnedLongerAgoThan is not { } age) return new ReclaimResult([], journal);

        var cutoff    = (now ?? DateTimeOffset.UtcNow) - age;
        var reclaim   = journal.Where(e => e.ThinnedUtc <= cutoff).ToList();
        var protect   = journal.Where(e => e.ThinnedUtc >  cutoff).ToList();

        if (reclaim.Count == 0) return new ReclaimResult([], protect);

        List<string> protectionRefs = [];
        try
        {
            for (int i = 0; i < protect.Count; i++)
            {
                string reference = ProtectionRefPrefix + i.ToString("D6");
                var made = git.Run(["update-ref", reference, protect[i].CommitId]);
                if (!made.Ok)
                    // A protection that could not be taken means the next step would destroy something
                    // the user asked to keep, so nothing is destroyed at all.
                    return new ReclaimResult([], journal,
                        GitFailures.Translate(made, "reclaiming space", git.WorkspaceRoot));
                protectionRefs.Add(reference);
            }

            // `gc` with an IMMEDIATE expiry on the command line, which overrides the repository's own
            // `gc.pruneExpire = never` for this one invocation and for nothing else.
            //
            // A bare `git prune` is NOT enough and the reason is easy to miss: it removes only LOOSE
            // objects, and anything the packing schedule has already been through is in a pack. A
            // thinned state that survived one pack would then be permanently un-reclaimable, so the
            // control that exists to answer "where did the disk go" would answer it with "nowhere".
            //
            // This is the ONE place in circuitRF that prunes, it runs only when a person asked, and
            // GitSubstrateTests holds both halves of that shut.
            var pruned = git.Run(["gc", "--quiet", "--prune=now"],
                                 new GitRunOptions(Timeout: GitCommand.PackTimeout), ct);

            if (pruned.Cancelled)
                return new ReclaimResult([], journal, GitFailures.TimedOut("reclaiming space"));
            if (!pruned.Ok)
                return new ReclaimResult([], journal,
                    GitFailures.Translate(pruned, "reclaiming space", git.WorkspaceRoot));

            // The journal shrinks HERE and nowhere else (§5.6a). The objects behind these entries are
            // gone, so leaving them listed would offer a designer a way back to a state that no longer
            // exists — which is the one thing a list they trust must not do. Removed AFTER the prune,
            // so a failure between the two leaves an entry pointing at nothing rather than a state
            // pointing at nobody.
            ThinningJournal.Remove(git.WorkspaceRoot, reclaim.Select(e => e.CommitId));

            return new ReclaimResult(reclaim, protect);
        }
        finally
        {
            foreach (string reference in protectionRefs)
                git.Run(["update-ref", "-d", reference]);
        }
    }
}
