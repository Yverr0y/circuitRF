using System.Globalization;

namespace CircuitRF.Design.Revision;

/// <summary>
/// <b>How a moment is written on a history row</b> (RC-10 R-rc10-12, R-rc10-13; §5.10).
///
/// <para><b>The missing year was a defect, not a sparseness.</b> The two panels rendered
/// <c>ddd d MMM</c> and <c>d MMM HH:mm</c>, so a version kept in December read in January as one kept
/// this week — the list's own date saying something false about the entry it labelled. The year
/// therefore appears whenever the entry is not from the current year, and is left off when it is,
/// because a column of identical years is a column nobody reads.</para>
///
/// <para><b>Relative labels for the recent entries are safe</b> (§5.10). §5.6 rule 2 already
/// establishes that the wall clock supplies the label and never the ordering, so <i>today</i> and
/// <i>yesterday</i> can be wrong about the label on a machine whose clock has jumped without being
/// able to reorder anything — which is the failure this whole feature is built to survive.</para>
///
/// <para><b>It lives below the firewall because it is a rule rather than a rendering.</b> The gate
/// asserts it on the strings, and the panel and anything else that draws a row read the one
/// function.</para>
/// </summary>
public static class HistoryDates
{
    /// <summary>
    /// The day, as a row shows it: <i>today</i>, <i>yesterday</i>, <c>Tue 3 Mar</c> inside the current
    /// year, and <c>3 Mar 2025</c> outside it.
    /// </summary>
    /// <param name="whenUtc">The entry's own moment.</param>
    /// <param name="nowUtc">What "the current year" is measured against. A parameter so the gate can
    /// assert both sides of the boundary without waiting for January.</param>
    public static string Day(DateTimeOffset whenUtc, DateTimeOffset nowUtc)
    {
        var when = whenUtc.ToLocalTime();
        var now  = nowUtc.ToLocalTime();

        if (when.Date == now.Date)                return "today";
        if (when.Date == now.Date.AddDays(-1))    return "yesterday";

        return when.Year == now.Year
            ? when.ToString("ddd d MMM", CultureInfo.CurrentCulture)
            : when.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// The time a row shows: <b>how long ago, for anything inside the last day</b> — <c>just now</c>,
    /// <c>7 minutes ago</c>, <c>3 hours ago</c> — and the clock time <c>14:32</c> beyond that (owner,
    /// 2026-09-07).
    ///
    /// <para>Within a day it is the elapsed time that a designer is actually reading for: the entry
    /// they want is the one from before the change they now regret, and <i>20 minutes ago</i> answers
    /// that where <i>14:32</i> makes them work out what the time was when they started. Past a day the
    /// arithmetic stops helping and the clock time is the plainer fact, with <see cref="Day"/> beside
    /// it carrying the date.</para>
    ///
    /// <para><b>The label only</b>, exactly as <see cref="Day"/> is: what is oldest is decided by
    /// circuitRF's own sequence, because a wall clock is user-writable state. An entry stamped in the
    /// FUTURE — a clock moved back, or a workspace off a machine set differently — therefore falls
    /// back to the clock time rather than reporting a negative age, since that is the one case where a
    /// relative phrase would be a statement rather than a rendering.</para>
    /// </summary>
    public static string Time(DateTimeOffset whenUtc, DateTimeOffset nowUtc)
    {
        var elapsed = nowUtc - whenUtc;

        if (elapsed < TimeSpan.Zero || elapsed >= TimeSpan.FromDays(1))
            return whenUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);

        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";

        if (elapsed < TimeSpan.FromHours(1))
        {
            int minutes = (int)elapsed.TotalMinutes;
            return minutes == 1 ? "1 minute ago" : $"{minutes} minutes ago";
        }

        int hours = (int)elapsed.TotalHours;
        return hours == 1 ? "1 hour ago" : $"{hours} hours ago";
    }

    /// <summary>
    /// A day and a time on one line — what the restored-from line and any other in-sentence reference
    /// to a moment uses. <b>Carries the year under the same rule</b>, because the sentence that names
    /// the state a version was brought back from is exactly where a wrong year misleads.
    /// </summary>
    public static string DayAndTime(DateTimeOffset whenUtc, DateTimeOffset nowUtc)
    {
        var when = whenUtc.ToLocalTime();

        return when.ToLocalTime().Year == nowUtc.ToLocalTime().Year
            ? when.ToString("d MMM HH:mm", CultureInfo.CurrentCulture)
            : when.ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// A moment inside a sentence, at the shortest spelling that stays unambiguous: the clock time on
    /// its own when it is today, and <see cref="DayAndTime"/> otherwise. Used where the sentence has
    /// already established which day it is talking about.
    /// </summary>
    public static string ClockOrDayAndTime(DateTimeOffset whenUtc, DateTimeOffset nowUtc)
        => whenUtc.ToLocalTime().Date == nowUtc.ToLocalTime().Date
         ? whenUtc.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture)
         : DayAndTime(whenUtc, nowUtc);

    /// <summary>
    /// The whole moment, with its zone — <b>what the expander carries and the row does not</b>
    /// (R-rc10-14). The row answers <i>which moment was this</i>; this answers <i>what exactly is
    /// this</i>, and the offset is the part that settles an argument about a workspace that has been
    /// on two machines.
    /// </summary>
    public static string Full(DateTimeOffset whenUtc)
        => whenUtc.ToLocalTime().ToString("dddd d MMMM yyyy, HH:mm:ss zzz", CultureInfo.CurrentCulture);
}
