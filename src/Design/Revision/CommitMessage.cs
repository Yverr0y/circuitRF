using System.Globalization;
using System.Text;

namespace CircuitRF.Design.Revision;

/// <summary>
/// <b>What an explicit commit says</b> (<c>docs/design/revision-control.md</c> §5.5's first row, §6.3;
/// RC-7 R-rc7-5, R-rc7-6).
///
/// <para><b>A commit message records how the commit came about</b>, because six weeks later
/// <i>"which of these did I mean?"</i> is the only question anyone asks of a history. Three things go
/// in: the title the designer typed, a line saying this was an explicit commit they asked for, and —
/// when the working tree was restored since the previous one — <b>which state it was restored
/// from</b>.</para>
///
/// <para><b>The restored-from line is not decoration.</b> Two consecutive commits where the second
/// silently reverts the first is a history that reads as a change of mind with no record of the
/// moment. That is the one thing the narrative can lie about by omission, and the line is what stops
/// it. It is written from the state a restore leaves behind
/// (<see cref="RestoreProvenance"/>), never inferred from the content.</para>
///
/// <para><b>Trailers, in git's own trailer shape</b>, for the reason
/// <see cref="CheckpointMessage"/>'s are: the escape hatch of §4.1 can read them with
/// <c>git log</c>, unrecognised keys are ignored on the way in, and there is no side file to keep in
/// step. The key prefix is shared with <see cref="CheckpointMessage"/> so one reader recognises
/// circuitRF's trailers wherever it finds them.</para>
///
/// <para><b>The vocabulary rule and its one qualification</b> (R-rc7-4, R-rc0-6). Nothing git-shaped
/// appears in any of this text — no branch, no checkout, no <c>HEAD</c>, no merge. What an explicit
/// action produces may be named precisely, and that qualification is spent in exactly one place:
/// <see cref="HistoryMessages.VersionRecorded"/>'s report of the identity. Not here.</para>
/// </summary>
public static class CommitMessage
{
    /// <summary>Shared with <see cref="CheckpointMessage"/>, so one reader recognises both.</summary>
    public const string KeyPrefix = CheckpointMessage.KeyPrefix;

    /// <summary>How this commit came about. Present on every commit circuitRF writes.</summary>
    public const string OriginKey = KeyPrefix + "Origin";

    /// <summary>The wire spelling of §5.5's first row. A file format — do not rename one; add one.</summary>
    public const string ExplicitCommitOrigin = "commit";

    /// <summary>What the working tree was restored from, when it was. Absent otherwise, and its
    /// absence is asserted by gate 1 — a line on every commit would say nothing.</summary>
    public const string RestoredFromKey = KeyPrefix + "Restored-From";

    /// <summary>When that state was kept, in ISO-8601 UTC. <b>Carried because the label alone can
    /// repeat</b>: two entries taken before two restores are both labelled the same way, and the time
    /// is what tells them apart.</summary>
    public const string RestoredAtKey = KeyPrefix + "Restored-From-Time";

    /// <summary>
    /// What a commit is listed as when the designer typed no title.
    ///
    /// <para><b>Never a bare time</b>, for <see cref="CheckpointMessage.UnnamedSavePoint"/>'s reason:
    /// a time is what every entry already has. A commit with no title is a rarer thing than a
    /// save-point with none — this one the designer opened a dialog for — but it must still be
    /// listable.</para>
    /// </summary>
    public const string UntitledVersion = "a version you kept";

    /// <summary>
    /// R-rc7-5's line. <b>Said in circuitRF's words, not git's</b>, and it is what distinguishes this
    /// commit from anything else that might be found on the same line of work — an import, a script,
    /// somebody's own command line.
    /// </summary>
    public const string ExplicitLine = "You asked circuitRF to keep this version.";

    /// <summary>
    /// Builds the whole message: the title, the line saying it was asked for, the restored-from line
    /// when there is one, then the trailer block.
    /// </summary>
    /// <param name="title">What the designer typed. Blank yields <see cref="UntitledVersion"/>.</param>
    /// <param name="restoredFrom">
    /// The state the working tree was restored from since the previous commit, or null. <b>The last
    /// restore, not the first</b>: restore to A, then to B, then commit, and the content is B's —
    /// naming A would be a sentence that is simply untrue.
    /// </param>
    public static string Build(string? title, RestoredFrom? restoredFrom = null)
    {
        var text = new StringBuilder();

        text.Append(Subject(title)).Append('\n').Append('\n');
        text.Append(ExplicitLine).Append('\n');

        if (restoredFrom is { } from)
            text.Append(RestoredLine(from)).Append('\n');

        text.Append('\n');
        text.Append(OriginKey).Append(": ").Append(ExplicitCommitOrigin).Append('\n');

        if (restoredFrom is { } trailer)
        {
            text.Append(RestoredFromKey).Append(": ")
                .Append(OneLine(trailer.Label)).Append('\n');
            text.Append(RestoredAtKey).Append(": ")
                .Append(trailer.TakenUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ",
                                                             CultureInfo.InvariantCulture))
                .Append('\n');
        }

        return text.ToString();
    }

    /// <summary>The title as it will be listed — one line, trimmed, never empty.</summary>
    public static string Subject(string? title)
        => OneLine(title) is { Length: > 0 } t ? t : UntitledVersion;

    /// <summary>
    /// R-rc7-6's sentence. <b>It names the state AND when that state was kept</b>, because the label
    /// on its own is not always unique: an entry taken before an earlier restore is labelled
    /// <i>before going back</i>, and a designer who has gone back twice would otherwise read the same
    /// six words twice with nothing to tell them apart.
    /// </summary>
    public static string RestoredLine(RestoredFrom from)
        => $"This version was brought back from '{OneLine(from.Label)}', "
         + $"kept on {from.TakenUtc.ToLocalTime():d MMMM yyyy} at {from.TakenUtc.ToLocalTime():HH:mm}.";

    /// <summary>
    /// Reads back what <see cref="Build"/> wrote. <b>Never throws and never refuses</b>, for
    /// <see cref="CheckpointMessage.Read"/>'s reason: a commit whose message this cannot understand is
    /// still a commit on the designer's own line of work — someone's own <c>git commit</c>, an import,
    /// a script — and hiding it from the browser would be a history that lies by omission about
    /// itself.
    /// </summary>
    public static CommitMetadata Read(string message)
    {
        string  title        = "";
        bool    explicitly   = false;
        string? restoredFrom = null;
        DateTimeOffset? restoredAt = null;

        var lines = (message ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        foreach (string line in lines)
        {
            if (title.Length == 0 && line.Trim().Length > 0 && !IsTrailer(line))
                title = line.Trim();

            if (!IsTrailer(line)) continue;

            int    colon = line.IndexOf(':');
            string key   = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();

            switch (key)
            {
                case OriginKey:
                    explicitly = value.Equals(ExplicitCommitOrigin, StringComparison.OrdinalIgnoreCase);
                    break;

                case RestoredFromKey when value.Length > 0:
                    restoredFrom = value;
                    break;

                case RestoredAtKey when DateTimeOffset.TryParse(
                        value, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when):
                    restoredAt = when;
                    break;
            }
        }

        return new CommitMetadata(
            title.Length > 0 ? title : UntitledVersion,
            explicitly,
            restoredFrom is { } label
                ? new RestoredFrom(label, restoredAt ?? DateTimeOffset.UnixEpoch)
                : null);
    }

    private static bool IsTrailer(string line)
        => line.StartsWith(KeyPrefix, StringComparison.Ordinal) && line.Contains(':');

    /// <summary>A trailer value and a subject are both single lines. A title with a newline in it
    /// would silently become a body, and the entry would list as untitled.</summary>
    private static string OneLine(string? text)
        => text?.ReplaceLineEndings(" ").Trim() ?? "";
}

/// <summary>
/// The state a restore brought the working tree back to, as the following commit names it.
/// </summary>
/// <param name="Label">What the designer would recognise it by — a restore point's own line.</param>
/// <param name="TakenUtc">When that state was kept. <b>What tells two identically-labelled entries
/// apart</b>, which is why it travels with the label rather than being looked up later: by the time
/// the commit is written, retention may have thinned the entry the label came from.</param>
public sealed record RestoredFrom(string Label, DateTimeOffset TakenUtc);

/// <summary>What <see cref="CommitMessage.Read"/> found.</summary>
/// <param name="Title">The line the browser shows.</param>
/// <param name="Explicit">Whether circuitRF wrote it as R-rc7-1's deliberate commit. False for
/// anything else that reached this line of work — someone's own command line, an import, a script —
/// which is listed anyway and is not pretended to be something it is not.</param>
/// <param name="RestoredFrom">What it was restored from, or null when nothing was.</param>
public sealed record CommitMetadata(string Title, bool Explicit, RestoredFrom? RestoredFrom);
