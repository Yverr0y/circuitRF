using System.Text.Json;

namespace CircuitRF.Design.Revision;

/// <summary>
/// The journal of what retention thinned — <b>read here, written by RC-6</b>
/// (<c>docs/design/revision-control.md</c> §5.6 rule 4, §5.6a).
///
/// <para><b>Why a journal exists at all.</b> Retention thins and never prunes: dropping a checkpoint
/// reference leaves its objects in the repository, which §4.5's <c>gc.pruneExpire = never</c> forbids
/// any pack from removing. rev 3 named the REFLOG as the way back from a mistaken sweep and rev 5
/// corrected it — a deleted reference takes its reflog with it, and references outside
/// <c>refs/heads/</c> carry none in any case. What finds an unreachable commit is
/// <c>git fsck --unreachable</c>, which is an escape hatch only for somebody who already knows git. So
/// circuitRF keeps its own record instead, and that record is what makes rule 4 a promise to a designer
/// rather than to a git user.</para>
///
/// <para><b>And it is the only thing that makes "thinned more than N ago" answerable.</b> git's own
/// expiry is by OBJECT AGE — when a state was made, not when it was thinned — so without this a
/// two-year-old restore point thinned yesterday would be destroyed by a one-month reclaim.</para>
///
/// <para><b>RC-4 defines the FORMAT because RC-4 is the first thing that needs to read it</b>, and a
/// count is what its reclaim confirmation has to show. RC-6 owns the writer and owns the thinning
/// policy; if it needs another field, it adds one — <see cref="Read"/> ignores what it does not
/// recognise, and a line it cannot parse at all is skipped rather than failing the read. That
/// tolerance is not laziness: this is consulted on the path that offers a destructive action, and a
/// reader that threw would turn one malformed line into a control that cannot be used at all.</para>
///
/// <para><b>Line-per-entry, appended, never rewritten.</b> §5.6 says "appended under
/// <c>.git/circuitrf/</c>", and appending is what lets a sweep record what it dropped without reading
/// and re-serialising everything before it. The one operation that removes entries is a reclaim, which
/// removes exactly the ones it acted on — that is RC-6's, and it is a rewrite because it is the only
/// time the file shrinks.</para>
/// </summary>
public static class ThinningJournal
{
    /// <summary>circuitRF's own directory inside the repository, relative to <c>.git</c>.</summary>
    public const string DirectoryName = "circuitrf";

    /// <summary>The journal file's name inside it.</summary>
    public const string FileName = "thinned.jsonl";

    /// <summary>Where the journal lives for a workspace whose repository root is given.</summary>
    public static string PathFor(string workspaceRoot)
        => Path.Combine(workspaceRoot, ".git", DirectoryName, FileName);

    /// <summary>
    /// Every entry, oldest first. <b>An absent file is an empty journal</b> — the state every workspace
    /// is in until retention has thinned something, and not an error.
    /// </summary>
    public static IReadOnlyList<ThinnedState> Read(string workspaceRoot)
    {
        string path = PathFor(workspaceRoot);
        List<ThinnedState> entries = [];

        try
        {
            if (!File.Exists(path)) return entries;

            foreach (string line in File.ReadLines(path))
            {
                if (line.Trim().Length == 0) continue;
                if (TryParse(line) is { } entry) entries.Add(entry);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return entries;
        }

        return entries;
    }

    /// <summary>How many entries were thinned longer ago than <paramref name="age"/> — what RC-4's
    /// confirmation counts, and exactly the set <c>GitReclaim.Reclaim</c> would act on.</summary>
    public static int CountOlderThan(
        IReadOnlyList<ThinnedState> journal, TimeSpan age, DateTimeOffset? now = null)
    {
        var cutoff = (now ?? DateTimeOffset.UtcNow) - age;
        return journal.Count(e => e.ThinnedUtc <= cutoff);
    }

    /// <summary>One entry's line. Public so RC-6's writer and this reader cannot disagree.</summary>
    public static string Format(ThinnedState entry)
        => JsonSerializer.Serialize(new JournalLine(entry.CommitId, entry.ThinnedUtc));

    private static ThinnedState? TryParse(string line)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<JournalLine>(line);
            return parsed is { Commit: { Length: > 0 } id } ? new ThinnedState(id, parsed.Thinned) : null;
        }
        catch (JsonException)
        {
            // One bad line is one lost entry, not a lost journal.
            return null;
        }
    }

    private sealed record JournalLine(
        [property: System.Text.Json.Serialization.JsonPropertyName("commit")]  string         Commit,
        [property: System.Text.Json.Serialization.JsonPropertyName("thinned")] DateTimeOffset Thinned);
}
