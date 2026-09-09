namespace CircuitRF.Design.Revision;

/// <summary>
/// <b>How an entry is named when nobody named it</b> (owner, 2026-09-08).
///
/// <para>Every automatic entry used to render under circuitRF's own wording for how it came about, so
/// a workspace closed forty times listed forty rows reading <i>workspace closed</i>. A time
/// distinguishes them, but a time is not something a designer can quote, point at, or hand to somebody
/// helping them — and the sentence that reports a restore had to quote a label, which meant quoting the
/// same generated phrase twice in one sentence about two different states.</para>
///
/// <para><b>The abbreviation is git's own, seven characters</b>, which is what <c>git log --oneline</c>
/// prints and therefore what anyone using §4.1's escape hatch is already reading. It is a rendering, not
/// an identity: the whole string stays on the expander and is what the copy action puts on the
/// clipboard, because a value shown short and copied long is the trust problem R-rc10-15 already had to
/// fix once.</para>
///
/// <para><b>This is not a widening of §0's vocabulary rule.</b> The rule governs what a designer who
/// never asked for version control is shown, and the identity of a state was already the single
/// exemption RC-7 made (<see cref="HistoryMessages.VersionRecorded"/>) — nothing git-shaped appears
/// unbidden; what an explicit action produces may be named precisely. A row in the History panel is
/// something the designer opened.</para>
/// </summary>
public static class HistoryIds
{
    /// <summary>How many characters of an identity a row carries. Git's own default abbreviation.</summary>
    public const int ShortLength = 7;

    /// <summary>
    /// The identity, abbreviated. <b>Never padded and never invented</b>: an entry with no identity —
    /// a gap row, or one read out of a message this could not parse — renders as nothing at all, which
    /// is what stops a placeholder being mistaken for a state somebody could go back to.
    /// </summary>
    public static string Short(string? commitId)
    {
        string id = commitId?.Trim() ?? "";
        return id.Length <= ShortLength ? id : id[..ShortLength];
    }
}
