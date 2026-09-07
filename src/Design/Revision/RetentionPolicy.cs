namespace CircuitRF.Design.Revision;

/// <summary>
/// The three numbers a retention sweep is decided by (<c>docs/design/revision-control.md</c> §5.6;
/// RC-6 R-rc6-1, R-rc6-3).
///
/// <para><b>A record rather than three arguments, because two of the three are user preferences and
/// the third is not.</b> RC-4 owns the age and the count floor; the fraction is circuitRF's and has no
/// row on the tab — a bound the user could raise to 1.0 is not a bound.</para>
///
/// <para><b>The count floor is what makes a wrong clock harmless</b> (R-rc6-1). It is applied FIRST
/// and no age ever overrides it, so a machine whose clock jumps a century forward loses nothing at all
/// rather than everything. RC-4 refuses to let it be set below
/// <c>RevisionPreferenceDefaults.MinimumRestorePointsFloor</c> for exactly that reason.</para>
/// </summary>
/// <param name="RetentionDays">How old an unkept restore point must be before a sweep may thin it.</param>
/// <param name="MinimumRestorePoints">
/// How many of the newest UNKEPT entries survive whatever any timestamp says. Kept entries do not
/// consume a slot here (R-rc6-5a: a sweep counts them toward nothing), because a workspace whose
/// twenty newest entries were all save-points would otherwise have no floor left at all.
/// </param>
/// <param name="MaxFraction">
/// <b>The bound</b> (R-rc6-3): the largest share of the unkept entries one pass may remove. A pass
/// that wants more is refused whole and reported — never partially applied, because a partial deletion
/// under a clock fault destroys real work and still leaves the fault undiagnosed.
/// </param>
public sealed record RetentionPolicy(
    int    RetentionDays        = RetentionPolicy.DefaultRetentionDays,
    int    MinimumRestorePoints = RetentionPolicy.DefaultMinimumRestorePoints,
    double MaxFraction          = RetentionPolicy.DefaultMaxFraction)
{
    /// <summary>§10A's documented default, mirrored by <c>RevisionPreferenceDefaults.RetentionDays</c>
    /// above the firewall. Declared here too because <c>src/Cli</c> cannot reach that class and a
    /// headless sweep must use the same number the tab shows.</summary>
    public const int DefaultRetentionDays = 30;

    /// <inheritdoc cref="DefaultRetentionDays"/>
    public const int DefaultMinimumRestorePoints = 20;

    /// <summary>
    /// A quarter of the unkept entries, and the number is chosen against the failure rather than for
    /// tidiness.
    ///
    /// <para><b>What it has to catch:</b> a clock fault makes EVERY entry look expired at once, so any
    /// fraction below 1.0 turns that into a refusal. <b>What it must not catch:</b> the ordinary steady
    /// state, where a session's sweep retires the handful that crossed the age since the last one — far
    /// below a quarter on any list long enough to have reached the floor.</para>
    ///
    /// <para><b>The case in between is real and is not silently absorbed:</b> a designer who returns
    /// after a long absence has a list where everything past the floor is legitimately expired, which
    /// is indistinguishable from a clock jump using only the data. That pass is refused and reported —
    /// nothing is lost, and the report is a sentence about the clock that is, in that one case, wrong.
    /// The alternative is a policy that cannot tell the two apart and deletes in both, which §1.4
    /// forbids. Recorded in <c>src/Design/RESOLVED.md</c>.</para>
    /// </summary>
    public const double DefaultMaxFraction = 0.25;

    /// <summary>The policy a caller with no preferences to read uses — a headless sweep, or a test.</summary>
    public static RetentionPolicy Default { get; } = new();

    /// <summary>The age boundary, given the clock the caller is prepared to trust for a LABEL.</summary>
    public DateTimeOffset CutoffFrom(DateTimeOffset now) => now - TimeSpan.FromDays(RetentionDays);
}
