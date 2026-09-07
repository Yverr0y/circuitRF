using System.Globalization;
using System.Text;
using CircuitRF.Diagnostics;

namespace CircuitRF.Design.Revision;

/// <summary>
/// One entry in the restore-point list, as a designer sees it.
///
/// <para><b>Nothing here is git vocabulary</b> (R-rc5-2, R-rc0-6). <see cref="Reference"/> and
/// <see cref="CommitId"/> exist so the operations below can act on the entry; they are addresses, not
/// something the list renders.</para>
/// </summary>
/// <param name="Reference">Where it lives. Never shown.</param>
/// <param name="CommitId">The object it is. Never shown.</param>
/// <param name="TreeId">The state it holds — what a restore writes and what the tree test compares.</param>
/// <param name="Sequence">R-rc5-8's ordering. The clock supplies the label; this decides what is oldest.</param>
/// <param name="TakenUtc">When, for the label a human reads and for nothing else.</param>
/// <param name="Origin">How it came about (§5.5).</param>
/// <param name="Label">The line the list shows, beside the time.</param>
/// <param name="Intent">
/// The words the user or the agent supplied, before any shaping. <b>Not the same as
/// <see cref="Label"/></b>: a batch's label is "before: <i>intent</i>", so anything that REBUILDS a
/// message has to start from this or it doubles the prefix — which is silent, because the result is
/// still a well-formed entry with a slightly odd line in it.
/// </param>
/// <param name="Kept">Retention may never thin it (R-rc5-1f).</param>
/// <param name="LeftOut">Paths left out at an unattended boundary — the entry is INCOMPLETE and says
/// so, with the names (R-rc5-15a).</param>
public sealed record RestorePoint(
    string                Reference,
    string                CommitId,
    string                TreeId,
    long                  Sequence,
    DateTimeOffset        TakenUtc,
    CheckpointOrigin      Origin,
    string                Label,
    string?               Intent,
    bool                  Kept,
    IReadOnlyList<string> LeftOut)
{
    /// <summary>True when a file was left out of this entry and the question has not been asked yet.</summary>
    public bool IsIncomplete => LeftOut.Count > 0;

    /// <summary>
    /// <b>Retention dropped this entry's reference, and it is listed anyway</b> (RC-6 R-rc6-4).
    ///
    /// <para>Thinning frees the objects; it does not destroy them — §4.5's <c>gc.pruneExpire = never</c>
    /// forbids any pack from removing an unreachable object, and nothing reclaims unless a person asks
    /// (§5.6a). So a thinned state is still there and still restorable, and hiding it from the list
    /// would make rule 4's promise invisible: the designer would see a state disappear and have no way
    /// to learn it had not gone. Marked rather than merged, because "still offered" and "kept for now"
    /// are different promises.</para>
    ///
    /// <para>Not a positional member: every construction of this record predates it and means false.</para>
    /// </summary>
    public bool Thinned { get; init; }
}

/// <summary>
/// <b>Reading the restore-point list, and the two things a designer can do to one entry that are not
/// a restore</b> (<c>docs/design/revision-control.md</c> §5.1, §5.6 rule 6; R-rc5-2, R-rc5-1f).
///
/// <para><b>Everything is read in two processes, whatever the list's length.</b> One
/// <c>for-each-ref</c> for the reference-to-object mapping and one batched object read for the rest.
/// The obvious shape — one invocation per entry — is a third of a second on a list of fifty, paid
/// every time a panel refreshes, and this is a panel that refreshes on every boundary.</para>
/// </summary>
public static class RestorePoints
{
    /// <summary>
    /// Every restore point in the workspace, <b>newest first</b>. An empty list is the ordinary state
    /// of a workspace nothing has recorded yet, and never an error.
    /// </summary>
    public static IReadOnlyList<RestorePoint> List(GitCommand git)
    {
        var refs = CheckpointReferences.List(git);
        if (refs.Count == 0) return [];

        var bodies = ReadObjects(git, refs.Select(r => r.CommitId));

        List<RestorePoint> points = [];
        foreach (var (reference, commitId, sequence) in refs)
        {
            if (!bodies.TryGetValue(commitId, out string? raw)) continue;

            var (treeId, takenUtc, message) = SplitCommitObject(raw);
            var meta = CheckpointMessage.Read(message);

            points.Add(new RestorePoint(
                reference, commitId, treeId,
                meta.Sequence ?? sequence,
                takenUtc,
                meta.Origin,
                meta.Subject.Length > 0 ? meta.Subject
                                        : CheckpointMessage.SubjectFor(meta.Origin, meta.Intent),
                meta.Intent,
                meta.Kept,
                meta.LeftOut));
        }

        points.Sort((a, b) => b.Sequence.CompareTo(a.Sequence));
        return points;
    }

    /// <summary>The newest entry, or null. What the tree test (R-rc5-5a) compares against.</summary>
    public static RestorePoint? Newest(GitCommand git)
    {
        var all = List(git);
        return all.Count > 0 ? all[0] : null;
    }

    /// <summary>
    /// Marks an entry <b>kept</b>, so retention may never thin it (R-rc5-1f) — §10B.3's "make it
    /// permanent", offered in Stage 2 before there is anything else to turn it into.
    ///
    /// <para><b>A new object is written and the reference is moved to it; the old one is left
    /// unreachable.</b> The tree is the same tree, so the state a restore would produce is
    /// byte-identical, and the entry keeps its sequence and therefore its place in the list. The
    /// alternative — editing an object in place — does not exist, and a side file recording the mark
    /// would be the second thing to keep in step that <see cref="CheckpointMessage"/> exists to
    /// avoid.</para>
    /// </summary>
    public static RevisionOutcome MarkKept(GitCommand git, RestorePoint point)
    {
        if (point.Kept) return RevisionOutcome.Success;

        // From the INTENT, never from the label: a batch's label is "before: <intent>", so rebuilding
        // from the label would produce "before: before: …" — a well-formed entry with a slightly odd
        // line in it, which is exactly the kind of wrong nobody notices.
        string message = CheckpointMessage.Build(
            point.Origin, point.Intent, point.Sequence, kept: true, leftOut: point.LeftOut);

        var written = git.Run(["commit-tree", point.TreeId], new GitRunOptions(StandardInput: message));
        if (!written.Ok || written.Line.Length == 0)
            return RevisionOutcome.Failed(
                GitFailures.Translate(written, "marking a restore point to keep", git.WorkspaceRoot));

        var moved = git.Run(["update-ref", point.Reference, written.Line, point.CommitId]);
        return moved.Ok
            ? RevisionOutcome.Success
            : RevisionOutcome.Failed(GitFailures.Translate(moved, "marking a restore point to keep", git.WorkspaceRoot));
    }

    // ── Thinned entries (RC-6 R-rc6-4) ────────────────────────────────────────────────────────────

    /// <summary>
    /// Every restore point <b>including the ones retention thinned</b>, newest first, each thinned one
    /// marked (<see cref="RestorePoint.Thinned"/>).
    ///
    /// <para><b>This is what makes "thins, never prunes" a promise to a designer rather than to a git
    /// user.</b> A sweep that made states silently vanish from the list would be indistinguishable from
    /// one that destroyed them, and the escape hatch that finds an unreachable commit —
    /// <c>git fsck --unreachable</c> — is only an escape hatch for somebody who already knows git.</para>
    ///
    /// <para><b>The journal is the source, and an entry whose object is genuinely gone is dropped from
    /// the list.</b> That is the state a reclaim leaves behind if its journal rewrite did not complete,
    /// and offering a way back to it would be worse than not listing it at all.</para>
    /// </summary>
    public static IReadOnlyList<RestorePoint> ListIncludingThinned(GitCommand git)
    {
        var live    = List(git);
        var journal = ThinningJournal.Read(git.WorkspaceRoot);
        if (journal.Count == 0) return live;

        var liveCommits = new HashSet<string>(live.Select(p => p.CommitId), StringComparer.OrdinalIgnoreCase);

        List<RestorePoint> all = [.. live];
        foreach (var entry in journal)
        {
            // Restored already, or journalled twice: the live entry wins and there is only ever one row.
            if (liveCommits.Contains(entry.CommitId)) continue;
            if (Describe(git, entry) is { } point) all.Add(point);
        }

        all.Sort((a, b) => b.Sequence.CompareTo(a.Sequence));
        return all;
    }

    /// <summary>One thinned journal entry as a list row, or null when its object is no longer there.</summary>
    private static RestorePoint? Describe(GitCommand git, ThinnedState entry)
    {
        var bodies = ReadObjects(git, [entry.CommitId]);
        if (!bodies.TryGetValue(entry.CommitId, out string? raw)) return null;

        var (treeId, takenUtc, message) = SplitCommitObject(raw);
        if (treeId.Length == 0) return null;

        var meta = CheckpointMessage.Read(message);

        return new RestorePoint(
            entry.Reference ?? CheckpointReferences.NameFor(meta.Sequence ?? entry.Sequence),
            entry.CommitId, treeId,
            meta.Sequence ?? entry.Sequence,
            takenUtc,
            meta.Origin,
            meta.Subject.Length > 0 ? meta.Subject
                                    : entry.Label ?? CheckpointMessage.SubjectFor(meta.Origin, meta.Intent),
            meta.Intent,
            meta.Kept,
            meta.LeftOut) { Thinned = true };
    }

    /// <summary>
    /// Puts a thinned entry back in the live list — <b>one reference update, which is the whole of
    /// R-rc6-4's promise</b>.
    ///
    /// <para>The journal entry is removed on success, because the state is no longer thinned; leaving it
    /// would list the same state twice and would let a later reclaim destroy something now live.</para>
    /// </summary>
    public static RevisionOutcome RestoreThinned(GitCommand git, RestorePoint point)
    {
        if (!point.Thinned) return RevisionOutcome.Success;

        var updated = git.Run(["update-ref", point.Reference, point.CommitId]);
        if (!updated.Ok)
            return RevisionOutcome.Failed(GitFailures.Translate(
                updated, "bringing back a restore point that had been tidied away", git.WorkspaceRoot));

        ThinningJournal.Remove(git.WorkspaceRoot, [point.CommitId]);
        return RevisionOutcome.Success;
    }

    // ── Reading objects in one process ────────────────────────────────────────────────────────────

    /// <summary>
    /// The raw bodies of a set of objects, keyed by identity. <c>cat-file --batch</c> is length-framed,
    /// so this parses on the announced size rather than looking for a delimiter that a commit message
    /// is perfectly entitled to contain.
    /// </summary>
    private static Dictionary<string, string> ReadObjects(GitCommand git, IEnumerable<string> ids)
    {
        Dictionary<string, string> bodies = new(StringComparer.Ordinal);

        string request = string.Join('\n', ids) + "\n";
        var r = git.Run(["cat-file", "--batch"], new GitRunOptions(StandardInput: request, ReadOnly: true));
        if (!r.Ok) return bodies;

        // The stream is bytes; git's own object text is UTF-8 and .NET has already decoded it, so the
        // announced size is in BYTES and the buffer is in CHARS. Anything non-ASCII in a message would
        // make the two disagree, so the sizes are used only to find the NEXT header line — the body is
        // taken as everything up to it.
        string   text  = r.StdOut.Replace("\r\n", "\n");
        string[] lines = text.Split('\n');

        string? currentId   = null;
        var     currentBody = new StringBuilder();

        foreach (string line in lines)
        {
            if (IsObjectHeader(line, out string? id))
            {
                if (currentId is not null) bodies[currentId] = currentBody.ToString();
                currentId = id;
                currentBody.Clear();
                continue;
            }

            if (currentId is not null) currentBody.Append(line).Append('\n');
        }

        if (currentId is not null) bodies[currentId] = currentBody.ToString();
        return bodies;
    }

    /// <summary><c>&lt;oid&gt; commit &lt;size&gt;</c> — the only line shaped like that in the stream,
    /// because a commit object's own header lines are <c>tree</c>, <c>author</c> and
    /// <c>committer</c>.</summary>
    private static bool IsObjectHeader(string line, out string? id)
    {
        id = null;
        string[] parts = line.Split(' ');
        if (parts.Length != 3 || parts[1] != "commit") return false;
        if (!long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out _)) return false;
        if (parts[0].Length < 7 || !parts[0].All(Uri.IsHexDigit)) return false;

        id = parts[0];
        return true;
    }

    /// <summary>A raw commit object's tree, its committer time, and its message.</summary>
    private static (string TreeId, DateTimeOffset TakenUtc, string Message) SplitCommitObject(string raw)
    {
        string tree = "";
        var    when = DateTimeOffset.UnixEpoch;
        int    i    = 0;

        string[] lines = raw.Split('\n');
        for (; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length == 0) { i++; break; }

            if (line.StartsWith("tree ", StringComparison.Ordinal))
                tree = line[5..].Trim();
            else if (line.StartsWith("committer ", StringComparison.Ordinal) && ParseIdentLine(line) is { } t)
                when = t;
        }

        return (tree, when, string.Join('\n', lines.Skip(i)));
    }

    /// <summary>The epoch seconds out of a <c>committer Name &lt;email&gt; 1700000000 +0000</c> line.</summary>
    private static DateTimeOffset? ParseIdentLine(string line)
    {
        string[] parts = line.TrimEnd().Split(' ');
        if (parts.Length < 2) return null;
        return long.TryParse(parts[^2], NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }
}
