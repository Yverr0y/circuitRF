using System.Globalization;

namespace CircuitRF.Design.Revision;

/// <summary>
/// Where restore points live, and how they are ordered
/// (<c>docs/design/revision-control.md</c> §5.2a; R-rc5-1, R-rc5-1a, R-rc5-8).
///
/// <para><b>One reference per restore point, in circuitRF's own namespace, outside the branch the
/// designer browses.</b> The shape is not a preference — it is what makes RC-6's thinning possible.
/// Retention drops individual entries out of the MIDDLE of a series while keeping the newest N, and a
/// single reference walking a chain cannot express that at all: a chain can only be truncated from the
/// oldest end, and truncation leaves everything before the cut still reachable, so it thins nothing.
/// With a reference each, dropping one is a single reference delete — the pointer goes, the objects
/// stay, which is exactly "thins, never prunes".</para>
///
/// <para><b>They are local and do not travel over a network</b> (R-rc5-1a). A clone does not copy them
/// — git's default fetch takes branches and tags and nothing under a private namespace — and a push
/// does not carry them. That is correct rather than a defect: the safety net is a property of one
/// machine and one designer's sessions, ordered by a sequence that means nothing on anybody else's
/// machine and thinned by that machine's retention preference. The consequence is that the three ways
/// a workspace leaves a machine disagree, which §10B.1 states in a row for each.</para>
///
/// <para><b>The sequence is derived from the references that exist, never from a counter file</b>
/// (R-rc5-1e), so it survives a crash, a clone and a reference somebody deleted by hand. Retention
/// keeps the newest N unconditionally, so the largest sequence in the namespace is never the one
/// thinned and a number is never handed out twice.</para>
/// </summary>
public static class CheckpointReferences
{
    /// <summary>
    /// The namespace, with its trailing slash. <b>Not under <c>refs/heads/</c> and not under
    /// <c>refs/tags/</c></b> — the first would put every restore point in the history a designer
    /// browses, and the second would push them to everyone who shares the workspace.
    /// </summary>
    public const string Namespace = "refs/crf/restore/";

    /// <summary>
    /// How many digits a sequence is padded to. Purely so a plain listing reads in order for a human
    /// looking at the escape hatch; <see cref="Parse"/> accepts any width and nothing sorts on the
    /// string.
    /// </summary>
    private const int SequenceDigits = 6;

    /// <summary>The reference name for one sequence number.</summary>
    public static string NameFor(long sequence)
        => Namespace + sequence.ToString("D" + SequenceDigits, CultureInfo.InvariantCulture);

    /// <summary>The sequence encoded in a reference name, or null when it is not one of ours.</summary>
    public static long? Parse(string reference)
    {
        if (!reference.StartsWith(Namespace, StringComparison.Ordinal)) return null;
        string tail = reference[Namespace.Length..];
        return long.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out long n) ? n : null;
    }

    /// <summary>
    /// Every restore-point reference in the workspace with the object it points at, <b>newest
    /// first</b>. A READ — it takes no optional locks, so listing never waits on a writer (§4.6).
    /// </summary>
    public static IReadOnlyList<(string Reference, string CommitId, long Sequence)> List(GitCommand git)
    {
        var r = git.Run(["for-each-ref", "--format=%(refname) %(objectname)", Namespace],
                        new GitRunOptions(ReadOnly: true));
        if (!r.Ok) return [];

        List<(string, string, long)> found = [];
        foreach (string line in r.StdOut.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            int space = line.IndexOf(' ');
            if (space <= 0) continue;

            string reference = line[..space].Trim();
            string commitId  = line[(space + 1)..].Trim();
            if (Parse(reference) is not { } sequence || commitId.Length == 0) continue;

            found.Add((reference, commitId, sequence));
        }

        found.Sort((a, b) => b.Item3.CompareTo(a.Item3));
        return found;
    }

    /// <summary>
    /// The number the next restore point takes. <b>One past the largest that exists</b>, and one when
    /// none does — never a count, because a count reuses a number the moment retention drops one.
    /// </summary>
    public static long NextSequence(GitCommand git)
    {
        long highest = 0;
        foreach (var (_, _, sequence) in List(git))
            if (sequence > highest) highest = sequence;
        return highest + 1;
    }

    /// <summary>The newest restore point's reference and object, or null when there are none.</summary>
    public static (string Reference, string CommitId, long Sequence)? Newest(GitCommand git)
    {
        var all = List(git);
        return all.Count > 0 ? all[0] : null;
    }
}
