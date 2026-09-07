namespace CircuitRF.Ui.Revision;

/// <summary>
/// The documented defaults and hard limits behind Settings ▸ Revision Control
/// (<c>docs/design/revision-control.md</c> §10A; RC-4 R-rc4-10, R-rc4-11).
///
/// <para><b>Named here rather than written as literals at the control and again at the reader.</b>
/// Every one of these preferences is nullable and absent-means-default (R-rc4-11), so the default is a
/// value two unrelated pieces of code have to agree about — and RC-4 gate 6b explicitly asks that a
/// test assert against the DOCUMENTED default rather than against a transcription of whatever the
/// writer happens to do. That is only possible if the document has a name in code.</para>
///
/// <para><c>RevisionArming.KeepHistoryDefault</c> is deliberately NOT re-declared here: it lives in
/// <c>src/Design</c> because the precedence function and <c>src/Cli</c> both need it, and a second copy
/// above the firewall is the drift this class exists to prevent.</para>
/// </summary>
public static class RevisionPreferenceDefaults
{
    /// <summary>
    /// How long automatic restore points are kept before retention thins them (§5.6).
    ///
    /// <para>§1.3 argues the recovery window is WEEKS, because the failure being guarded against may
    /// not be noticed for weeks. It is a ceiling on age only: <see cref="MinimumRestorePoints"/> keeps
    /// the newest N whatever any timestamp says, which is what makes a clock jump cost the user nothing
    /// (§5.6 rule 1).</para>
    /// </summary>
    public const int RetentionDays = 30;

    /// <summary>The shortest retention the field will accept, in days.</summary>
    public const int RetentionDaysFloor = 1;

    /// <summary>The longest, in days — ten years, which is a field bound and not a policy.</summary>
    public const int RetentionDaysCeiling = 3650;

    /// <summary>
    /// The count floor: how many of the newest restore points are kept unconditionally, whatever their
    /// timestamps say (§5.6 rule 1).
    /// </summary>
    public const int MinimumRestorePoints = 20;

    /// <summary>
    /// <b>The hard minimum the field cannot be set below — including by typing a value rather than
    /// using the stepper</b> (R-rc4-10, gate 7).
    ///
    /// <para>This is the number that makes §5.6's first rule real. A wall clock is user-writable state:
    /// a machine whose clock jumps a century forward makes every checkpoint "expired" on the next
    /// sweep, and a dead CMOS battery, a re-synced NTP server and a dual-boot machine disagreeing about
    /// UTC all produce the same fault. Keeping the newest ten unconditionally means a clock jump costs
    /// the user nothing at all — so a user is not permitted to set it to zero and give that guarantee
    /// away without knowing they have.</para>
    /// </summary>
    public const int MinimumRestorePointsFloor = 10;

    /// <summary>The largest the count floor may be set to. A field bound, not a policy.</summary>
    public const int MinimumRestorePointsCeiling = 1000;

    /// <summary>Take a restore point when the workspace closes (§5.3). On by default.</summary>
    public const bool CheckpointOnClose = true;

    /// <summary>
    /// The repository size at which circuitRF packs on its own (§2.4), in megabytes — the same trigger
    /// <c>GitPacking.DefaultThresholdBytes</c> carries, expressed in the unit the field shows.
    ///
    /// <para>circuitRF owns packing because git's own automatic trigger counts LOOSE OBJECTS, and
    /// RC-3's re-measurement found 45 commits with no <c>gc</c> leaving 148 MB against 5.7 MB packed at
    /// 138 loose objects — 2% of git's own 6,700 count, so git would never once have acted.</para>
    /// </summary>
    public const int PackThresholdMb = 64;

    /// <summary>The smallest pack threshold the field accepts, in megabytes.</summary>
    public const int PackThresholdMbFloor = 1;

    /// <summary>The largest, in megabytes.</summary>
    public const int PackThresholdMbCeiling = 100_000;

    /// <summary>
    /// The age the reclaim action defaults to: reclaim space from restore points thinned longer ago
    /// than this many days (§5.6a).
    ///
    /// <para><b>The field on its own does nothing.</b> Nothing reclaims on a schedule; this is the
    /// value the confirmed action is proposed with, and 90 days is deliberately far longer than
    /// <see cref="RetentionDays"/> so the default proposal cannot destroy something thinned in the same
    /// month it was thinned.</para>
    /// </summary>
    public const int ReclaimAgeDays = 90;

    /// <summary>The shortest reclaim age the field accepts, in days.</summary>
    public const int ReclaimAgeDaysFloor = 1;

    /// <summary>The longest, in days.</summary>
    public const int ReclaimAgeDaysCeiling = 3650;

    /// <summary>Clamps a value into [min, max]. What the typed-value path uses (gate 7).</summary>
    public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
}
