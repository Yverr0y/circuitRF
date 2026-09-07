using CircuitRF.Diagnostics;

namespace CircuitRF.Design.Revision;

/// <summary>What a sweep would do, before it does any of it.</summary>
/// <param name="Thin">The entries this pass would drop, oldest first.</param>
/// <param name="Refused">
/// True when <see cref="RetentionPolicy.MaxFraction"/> was exceeded. <b>Nothing at all is dropped in
/// that case</b> — not a bounded prefix of the list (R-rc6-3).
/// </param>
/// <param name="Unkept">How many entries the fraction was computed over — the kept ones count toward
/// nothing (R-rc6-5a).</param>
/// <param name="Allowance">The most this pass was permitted to drop.</param>
/// <param name="Wanted">
/// How many entries the pass would have dropped had it been permitted to — <b>which on a refusal is
/// the only number that makes the report mean anything</b>.
///
/// <para>It is not <c>Thin.Count</c>: a refusal thins nothing, so on the one path where this number
/// is worth saying, <c>Thin</c> is empty. Rule 3's whole purpose is to convert a clock fault into a
/// sentence a designer can act on — "circuitRF was about to tidy away 38 of your 40 restore points" —
/// and a sentence built from <c>Thin.Count</c> says "about to tidy away 0", which reads as a defect in
/// circuitRF rather than as a wrong clock.</para>
/// </param>
public sealed record SweepPlan(
    IReadOnlyList<RestorePoint> Thin,
    bool                        Refused,
    int                         Unkept,
    int                         Allowance,
    int                         Wanted = 0)
{
    /// <summary>Nothing to do, and no fault — the ordinary answer.</summary>
    public static readonly SweepPlan Nothing = new([], false, 0, 0);
}

/// <summary>What a sweep actually did.</summary>
/// <param name="Thinned">The entries whose references were dropped and journalled.</param>
/// <param name="Plan">What was intended, including a refusal.</param>
/// <param name="Diagnostics">What to report. <b>A successful sweep says nothing</b> (R-rc6-9's
/// subtraction); a refusal says one thing.</param>
public sealed record SweepResult(
    IReadOnlyList<ThinnedState> Thinned,
    SweepPlan                   Plan,
    IReadOnlyList<Diagnostic>   Diagnostics)
{
    public static readonly SweepResult DidNothing = new([], SweepPlan.Nothing, []);
}

/// <summary>
/// <b>Retention</b> — the five rules that make a wrong clock harmless
/// (<c>docs/design/revision-control.md</c> §5.6; RC-6 R-rc6-1 … R-rc6-5a).
///
/// <para><b>A wall clock is user-writable state, and that is the whole design constraint.</b> A
/// machine whose clock jumps a century forward makes every restore point look expired on the next
/// sweep; one set backwards makes nothing expire and stamps new entries as older than the ones they
/// follow. A dead CMOS battery, a laptop resuming against a re-synced time server, a timezone change
/// and a dual-boot machine disagreeing about whether the hardware clock is UTC all produce the same
/// class of fault, and all of them are ordinary. <b>A retention policy that can be triggered into mass
/// deletion by a clock change is unacceptable</b>, so:</para>
///
/// <list type="number">
///   <item><description><b>A count floor age can never override</b> (R-rc6-1). The newest N unkept
///   entries survive whatever any timestamp says, so a clock jump costs the user nothing at
///   all.</description></item>
///   <item><description><b>Ordering comes from RC-5's monotonic sequence, never from the commit
///   timestamp</b> (R-rc6-2). The wall clock supplies the label a human reads; it never decides what is
///   oldest. This is also what fixes the backwards-clock case, where timestamp order and actual order
///   disagree.</description></item>
///   <item><description><b>A pass is bounded, and an over-large one is refused whole</b>
///   (R-rc6-3) — which converts a clock fault from silent data loss into one message saying something
///   is wrong with the clock.</description></item>
///   <item><description><b>Thinning is not pruning</b> (R-rc6-4). Dropping a reference leaves the
///   objects in the repository — §4.5's <c>gc.pruneExpire = never</c> forbids any pack from removing
///   them — and every drop is journalled, so the way back is one reference update rather than
///   <c>git fsck --unreachable</c>.</description></item>
///   <item><description><b>Nothing outside <c>refs/crf/restore/</c> is touched, ever</b> (R-rc6-5).
///   Human-written commits and the designer's own branch are out of scope for retention entirely; they
///   are small, they are the designer's own record, and no automatic process gets to delete them. That
///   is not a check here — it is the fact that this class only ever names a reference
///   <see cref="CheckpointReferences.List"/> returned.</description></item>
/// </list>
///
/// <para><b>Kept entries are out of scope too</b> (R-rc6-5a): a save-point, the pair that brackets an
/// off period, and any entry the designer marked <i>keep</i>. A sweep removes none of them AND counts
/// them toward nothing — neither toward the floor, which would otherwise be consumed by a run of
/// save-points, nor toward the fraction.</para>
///
/// <para><b>Nothing here decides WHEN a sweep runs.</b> That is
/// <see cref="SessionHousekeeping"/>'s, and it matters: rule 3's bound guarantees nothing until the
/// passes are counted, since a sweep on every checkpoint at one quarter empties the set inside a dozen
/// of them.</para>
/// </summary>
public static class RetentionSweep
{
    /// <summary>
    /// What a sweep would drop, and whether it is permitted to.
    ///
    /// <para><paramref name="now"/> is the only place a clock enters, and it enters as an ARGUMENT so
    /// a gate can move it a century without touching the machine's own.</para>
    /// </summary>
    public static SweepPlan Plan(
        IReadOnlyList<RestorePoint> points, RetentionPolicy policy, DateTimeOffset now)
    {
        // R-rc6-2. Newest first BY SEQUENCE. Sorting on TakenUtc here is the whole bug this rule
        // exists to prevent: under a backwards clock the two orders disagree, and the timestamp order
        // would nominate the newest entries as the oldest.
        var unkept = points.Where(p => !p.Kept && !p.Thinned)
                           .OrderByDescending(p => p.Sequence)
                           .ToList();

        // R-rc6-1, applied FIRST. Whatever any timestamp says.
        int floor = Math.Max(0, policy.MinimumRestorePoints);
        if (unkept.Count <= floor) return new SweepPlan([], false, unkept.Count, Allowance(unkept.Count, policy));

        var cutoff     = policy.CutoffFrom(now);
        var candidates = unkept.Skip(floor).Where(p => p.TakenUtc < cutoff)
                               .OrderBy(p => p.Sequence)
                               .ToList();

        int allowance = Allowance(unkept.Count, policy);

        // R-rc6-3. Refused WHOLE. A bounded prefix would destroy real work under a clock fault and
        // still leave the fault undiagnosed, which is the worst of the three available outcomes.
        return candidates.Count > allowance
            ? new SweepPlan([], true, unkept.Count, allowance, candidates.Count)
            : new SweepPlan(candidates, false, unkept.Count, allowance, candidates.Count);
    }

    /// <summary>
    /// The most one pass may drop. <b>Rounded UP and never below one</b>, so a list that has only just
    /// passed the floor can still retire the single entry that crossed the age — a bound that rounded
    /// down to zero would stall retention permanently on a small workspace.
    /// </summary>
    private static int Allowance(int unkept, RetentionPolicy policy)
        => unkept <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(unkept * policy.MaxFraction));

    /// <summary>
    /// Runs the sweep: drops the references the plan names and journals each one.
    ///
    /// <para><b>The reference is dropped first and journalled second</b>, and the order is not
    /// arbitrary. Journalling first would list a state twice on a crash — once live and once thinned —
    /// which is the one thing a list a designer trusts must not do. Journalling second risks the
    /// opposite on a crash, an unreachable state with no record; that is recoverable through
    /// <c>git fsck</c> and the first is not recoverable at all, so this is the direction that fails
    /// safely. <b>A journal write that fails STOPS the sweep</b> rather than carrying on dropping
    /// references it can no longer record.</para>
    ///
    /// <para><b>The reference delete names the object it expects</b>, so a reference another process
    /// moved between the read and the delete is left alone by git rather than dropped blind.</para>
    /// </summary>
    public static SweepResult Run(
        GitCommand      git,
        RetentionPolicy policy,
        DateTimeOffset? now = null)
    {
        SweepsRun++;

        var points = RestorePoints.List(git);
        var plan   = Plan(points, policy, now ?? DateTimeOffset.UtcNow);

        if (plan.Refused)
            // Plan.WANTED, never Thin.Count: a refusal drops nothing, so the list is empty and the
            // message would have read "about to tidy away 0 of your 40", which names a defect in
            // circuitRF instead of a wrong clock — the one thing this report exists to say.
            return new SweepResult([], plan, [HoldMessages.RetentionSweepRefused(plan.Wanted, plan.Unkept)]);

        if (plan.Thin.Count == 0) return new SweepResult([], plan, []);

        List<ThinnedState> thinned = [];
        List<Diagnostic>   notes   = [];

        foreach (var point in plan.Thin)
        {
            var dropped = git.Run(["update-ref", "-d", point.Reference, point.CommitId]);
            if (!dropped.Ok)
            {
                notes.Add(GitFailures.Translate(dropped, "tidying up this workspace's history",
                                                git.WorkspaceRoot));
                break;
            }

            var entry = new ThinnedState(point.CommitId, now ?? DateTimeOffset.UtcNow,
                                         point.Reference, point.Sequence, point.Label);

            if (!ThinningJournal.Append(git.WorkspaceRoot, entry))
            {
                notes.Add(HoldMessages.ThinningJournalUnwritable());
                thinned.Add(entry);
                break;
            }

            thinned.Add(entry);
        }

        return new SweepResult(thinned, plan, notes);
    }

    /// <summary>
    /// How many sweeps this process has run. <b>A counter, because the property that matters is a CALL
    /// COUNT and this repository does not write wall-clock gates</b> (R-rc0-8): R-rc6-4a's rule is
    /// "at most once per session, on close", and asserting it means counting passes, not timing them.
    /// </summary>
    public static int SweepsRun { get; private set; }

    /// <summary>Resets the counter. A test seam and nothing else — no product path calls it.</summary>
    internal static void ResetSweepCounter() => SweepsRun = 0;
}
