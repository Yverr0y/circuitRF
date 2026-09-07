namespace CircuitRF.Design.Revision;

/// <summary>
/// A stretch in which circuitRF recorded nothing because recording was switched off.
/// </summary>
/// <param name="Start">The entry taken as recording stopped. Always present — a gap with no start is
/// not detectable and is exactly the quiet interval §5.7 forbids.</param>
/// <param name="End">
/// The entry taken as recording resumed, or <b>null while the gap is still open</b> — which is the
/// ordinary state of a workspace somebody switched off and has not switched back on.
/// </param>
public sealed record RevisionGap(RestorePoint Start, RestorePoint? End)
{
    /// <summary>True while recording is still off.</summary>
    public bool IsOpen => End is null;

    /// <summary>When it began, for the label a browser draws.</summary>
    public DateTimeOffset FromUtc => Start.TakenUtc;

    /// <summary>When it ended, or null while it is open.</summary>
    public DateTimeOffset? ToUtc => End?.TakenUtc;
}

/// <summary>
/// <b>Where the history is quiet because recording was off</b> (<c>revision-control.md</c> §5.7;
/// RC-6 R-rc6-13).
///
/// <para><b>A designer scanning the history later must be able to see that nothing was recorded
/// between two dates BECAUSE recording was off</b>, not because nothing happened. Rendering it as an
/// ordinary interval between two entries is §1.4's false-belief failure in its purest form.</para>
///
/// <para><b>RC-7 owns the browser; this brief owns the fact the gap is recorded</b> and the data a
/// browser reads to draw one. There is no separate record and no side file: the gap IS the pair of
/// transition entries <see cref="RevisionSwitch"/> writes, which is why both carry the kept mark — a
/// pair retention could thin is a gap retention could erase.</para>
///
/// <para><b>Ordered by RC-5's sequence, never by the clock</b> (R-rc6-2), for the reason retention is:
/// under a backwards clock the two orders disagree, and a gap derived from timestamps would pair the
/// wrong ends together.</para>
/// </summary>
public static class RevisionGaps
{
    /// <summary>
    /// Every gap in a restore-point list, oldest first.
    ///
    /// <para><b>An unmatched resumption is not a gap</b> and is skipped: it is what a workspace that
    /// arrived already switched off produces when its recipient turns recording on, and there is no
    /// stretch of missing history on this machine to draw.</para>
    /// </summary>
    public static IReadOnlyList<RevisionGap> Find(IReadOnlyList<RestorePoint> points)
    {
        var ordered = points.OrderBy(p => p.Sequence).ToList();

        List<RevisionGap> gaps  = [];
        RestorePoint?     open  = null;

        foreach (var point in ordered)
        {
            switch (point.Origin)
            {
                case CheckpointOrigin.RecordingOff:
                    // Two "off" entries with no resumption between them cannot both open a gap. The
                    // FIRST is the one that ends the recorded history, so it is the one kept.
                    open ??= point;
                    break;

                case CheckpointOrigin.RecordingOn when open is { } start:
                    gaps.Add(new RevisionGap(start, point));
                    open = null;
                    break;
            }
        }

        if (open is { } stillOff) gaps.Add(new RevisionGap(stillOff, null));
        return gaps;
    }

    /// <summary>Whether this entry is one end of a gap — what a list row needs in order to render
    /// itself differently from an ordinary state.</summary>
    public static bool IsGapEdge(RestorePoint point)
        => point.Origin is CheckpointOrigin.RecordingOff or CheckpointOrigin.RecordingOn;
}
