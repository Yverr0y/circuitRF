using System.Text.Json;

namespace CircuitRF.Design.Revision;

/// <summary>
/// The journal of what retention thinned — <b>the way back after a sweep</b>
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
        => JsonSerializer.Serialize(new JournalLine(
               entry.CommitId, entry.ThinnedUtc, entry.Reference, entry.Sequence, entry.Label));

    // ── Writing (RC-6) ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends one dropped reference to the journal, creating <c>.git/circuitrf/</c> if this is the
    /// first.
    ///
    /// <para><b>Appended, never rewritten</b> (§5.6 rule 4), so a sweep records what it dropped without
    /// reading and re-serialising everything before it — and so a process killed mid-sweep leaves a
    /// journal that is short rather than one that is corrupt.</para>
    ///
    /// <para><b>Called AFTER the reference is gone, never before.</b> A journal entry for a reference
    /// that still resolves would list the same state twice — once live and once thinned — which is the
    /// one thing a list a designer trusts must not do. A failure to append therefore loses the way back
    /// to a state, which is why it returns false rather than throwing: the sweep reports it and stops
    /// rather than carrying on dropping references it can no longer record.</para>
    /// </summary>
    public static bool Append(string workspaceRoot, ThinnedState entry)
    {
        string path = PathFor(workspaceRoot);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, Format(entry) + "\n");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Removes the entries a reclaim acted on — <b>the one operation that makes this file shrink</b>
    /// (§5.6a). Everything not named survives, in order.
    ///
    /// <para>A rewrite, and the only one: after a reclaim the objects behind those entries are gone, so
    /// leaving them listed would offer a designer a way back to a state that no longer exists. Written
    /// to a temporary file and moved into place, so a crash mid-rewrite leaves the old journal rather
    /// than half of a new one.</para>
    /// </summary>
    public static bool Remove(string workspaceRoot, IEnumerable<string> commitIds)
    {
        var drop = new HashSet<string>(commitIds, StringComparer.OrdinalIgnoreCase);
        if (drop.Count == 0) return true;

        string path = PathFor(workspaceRoot);
        try
        {
            if (!File.Exists(path)) return true;

            var keep = Read(workspaceRoot).Where(e => !drop.Contains(e.CommitId)).ToList();

            string temporary = path + ".new";
            File.WriteAllText(temporary, string.Concat(keep.Select(e => Format(e) + "\n")));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static ThinnedState? TryParse(string line)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<JournalLine>(line);
            return parsed is { Commit: { Length: > 0 } id }
                ? new ThinnedState(id, parsed.Thinned, parsed.Reference, parsed.Sequence, parsed.Label)
                : null;
        }
        catch (JsonException)
        {
            // One bad line is one lost entry, not a lost journal.
            return null;
        }
    }

    private sealed record JournalLine(
        [property: System.Text.Json.Serialization.JsonPropertyName("commit")]  string         Commit,
        [property: System.Text.Json.Serialization.JsonPropertyName("thinned")] DateTimeOffset Thinned,
        [property: System.Text.Json.Serialization.JsonPropertyName("ref")]     string?        Reference = null,
        [property: System.Text.Json.Serialization.JsonPropertyName("seq")]     long           Sequence  = 0,
        [property: System.Text.Json.Serialization.JsonPropertyName("label")]   string?        Label     = null);
}
