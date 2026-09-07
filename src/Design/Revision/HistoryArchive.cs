namespace CircuitRF.Design.Revision;

/// <summary>
/// What including a workspace's history in an archive would actually add — <b>computed, never
/// generalised</b> (<c>docs/design/revision-control.md</c> §9A.3; RC-8 R-rc8-7).
/// </summary>
/// <param name="Bytes">
/// How much larger the archive becomes: the repository directory as it stands, measured AFTER packing
/// (R-rc8-9). Unpacked it can be ten times this, which is exactly the figure that would be dishonest.
/// </param>
/// <param name="Versions">Versions on the line of work — what a designer kept on purpose.</param>
/// <param name="RestorePoints">
/// Restore points carried, including the ones retention thinned. <b>This is the number that makes the
/// archive different from every other way a workspace leaves a machine</b> (R-rc8-5a).
/// </param>
/// <param name="DeletedPaths">
/// Files that are in the history and are not in the workspace now, workspace-relative, sorted.
/// <b>The item that does the real work</b> (R-rc8-8): someone about to leak a file will almost always
/// recognise it by name, and nobody recognises "the repository contains historical objects".
/// </param>
public sealed record HistoryArchiveSummary(
    long                  Bytes,
    int                   Versions,
    int                   RestorePoints,
    IReadOnlyList<string> DeletedPaths)
{
    /// <summary>Nothing to add — a workspace whose repository could not be read.</summary>
    public static readonly HistoryArchiveSummary Empty = new(0, 0, 0, []);

    /// <summary>
    /// The sentences the dialog shows when the box is ticked.
    ///
    /// <para><b>It must not read as a warning against its own feature</b> (R-rc8-3). Including a
    /// history is genuinely the better handover and is not discouraged; what is stated is what the
    /// recipient gets, factually, with the one fact a sender cannot see from the file tree on the end
    /// of it.</para>
    /// </summary>
    /// <param name="namesShown">
    /// How many deleted files are named before the rest become a count. <b>Never a count alone</b>
    /// (R-rc8-8) — the names are the whole mechanism.
    /// </param>
    public IReadOnlyList<string> Describe(int namesShown = 5)
    {
        List<string> lines =
        [
            $"Adds {FormatSize(Bytes)} — {Plural(Versions, "version")} and "
          + $"{Plural(RestorePoints, "restore point")}, with every earlier version of every file kept.",

            // R-rc8-5a. The one journey that carries restore points, because it copies the folder they
            // live in. Said here because a designer comparing this with a copy would otherwise assume
            // the two are the same.
            "Restore points travel only this way — a copy of a workspace starts a history of its own.",
        ];

        if (DeletedPaths.Count > 0)
        {
            var named = DeletedPaths.Take(namesShown).ToList();
            string list = string.Join(", ", named);
            if (DeletedPaths.Count > named.Count)
                list += $", and {DeletedPaths.Count - named.Count} more";

            lines.Add(DeletedPaths.Count == 1
                ? $"1 file is in the history and not in the workspace now: {list}."
                : $"{DeletedPaths.Count} files are in the history and not in the workspace now: {list}.");
        }

        return lines;
    }

    private static string Plural(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    /// <summary>Human-readable size, spelled the way the archive dialog's own total is.</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 0) return "…";
        if (bytes < 1024) return $"{bytes} B";
        string[] units = ["KB", "MB", "GB", "TB"];
        double v = bytes / 1024.0;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return v >= 100 ? $"{v:0} {units[u]}" : $"{v:0.0} {units[u]}";
    }
}

/// <summary>
/// <b>What an archive would carry if the sender chose to include the history</b>
/// (<c>docs/design/revision-control.md</c> §9A; RC-8 R-rc8-7, R-rc8-7a, R-rc8-8, R-rc8-9).
///
/// <para><b>The enumeration walks every reference the archive will carry, and that is more than the
/// branch.</b> An archive copies the repository DIRECTORY, so it takes RC-5's per-checkpoint
/// references with it (§5.2a) — and a checkpoint is <c>commit -a</c>-shaped by design (R-rc5-7): it
/// captures files the designer never deliberately committed and may never have opened. <b>A file that
/// lived only inside checkpoints is therefore in the archive and absent from a branch-only
/// enumeration</b>, which is exactly the population this warning exists for, since a deliberately
/// committed file is one the designer already knows about.</para>
///
/// <para><b>And it walks the thinned ones too</b> (§5.6a, R-rc8-9). Retention thins and never prunes,
/// and nothing reclaims unless a person asks — so a thinned checkpoint's objects are still in the
/// directory the archive copies. A live-references-only enumeration would produce a warning that is
/// <i>nearly</i> true, and §9A.4 is the paragraph explaining why that is worse than none: the
/// near-truth is what people act on.</para>
///
/// <para><b>Two processes, whatever the length of the history.</b> One <c>rev-list --objects</c> over
/// every tip at once, and one <c>cat-file --batch-check</c> over only the paths that are missing from
/// disk — which is a short list on every workspace where the answer is interesting. Naming a
/// per-checkpoint <c>ls-tree</c> instead would be one process per restore point, and the warning has
/// to arrive before the user clicks through rather than after.</para>
/// </summary>
public static class HistoryArchive
{
    /// <summary>The repository directory, relative to the workspace root.</summary>
    public const string RepositoryFolderName = ".git";

    /// <summary>Where the repository is for a workspace root. Existence is not implied.</summary>
    public static string RepositoryPathFor(string workspaceRoot)
        => Path.Combine(workspaceRoot, RepositoryFolderName);

    /// <summary>
    /// Whether this workspace has a repository of ITS OWN to offer (R-rc0-5, R-rc8-10).
    ///
    /// <para>A directory test rather than a git invocation, deliberately: this is asked while the
    /// archive dialog is being built and must cost nothing. It is also the exactly right question —
    /// a workspace sitting INSIDE somebody else's repository has no <c>.git</c> at its own root, and
    /// circuitRF would not archive that repository in any case.</para>
    /// </summary>
    public static bool Available(string? workspaceRoot)
        => workspaceRoot is { Length: > 0 } && Directory.Exists(RepositoryPathFor(workspaceRoot));

    /// <summary>
    /// <b>Pack, then measure</b> (R-rc8-9) — and in that order, because the size figure is dishonest
    /// otherwise.
    ///
    /// <para>This is <b>the one deliberate exception to R-rc3-14</b>, which otherwise keeps packing
    /// behind a workspace close so an unasked-for pause is never a defect. Here the user has asked for
    /// an archive; RC-3's overhang means an unpacked repository can be ten times its real size, and an
    /// archive is exactly where that surprises someone. R-rc3-1b's rule that packing YIELDS to another
    /// process holding the workspace still applies, and a yielded pack is not a failure — the figures
    /// are still computed, from a repository that is merely larger than it needs to be.</para>
    ///
    /// <para><b>Packing never reclaims</b> (§5.6a): every thinned restore point's objects are still
    /// there afterwards, which is why <see cref="Summarise"/> has to walk them and why they are inside
    /// the size.</para>
    /// </summary>
    public static (PackOutcome Outcome, HistoryArchiveSummary Summary) Prepare(
        GitCommand git, CancellationToken ct = default)
    {
        // Threshold zero: the trigger that decides whether packing is WORTH doing on a close is not the
        // question here. The user asked for an archive, so the repository is packed whatever its size.
        var (outcome, _) = GitPacking.Pack(git, thresholdBytes: 0, ct);
        return (outcome, Summarise(git));
    }

    /// <summary>
    /// Everything R-rc8-7 requires the dialog to state, read out of the repository as it stands.
    /// </summary>
    public static HistoryArchiveSummary Summarise(GitCommand git)
    {
        var tips = Tips(git, out int versions, out int restorePoints);

        return new HistoryArchiveSummary(
            RepositoryBytes(git.WorkspaceRoot),
            versions,
            restorePoints,
            DeletedPaths(git, tips));
    }

    /// <summary>
    /// Every commit the archive would carry a history from: the line of work, every live restore
    /// point, and every restore point retention thinned whose objects are still there.
    /// </summary>
    private static IReadOnlyList<string> Tips(GitCommand git, out int versions, out int restorePoints)
    {
        List<string> tips = [];
        versions = 0;

        var head = git.Run(["rev-parse", "--verify", "--quiet", "HEAD"], new GitRunOptions(ReadOnly: true));
        if (head.Ok && head.Line.Length > 0)
        {
            tips.Add(head.Line);
            var counted = git.Run(["rev-list", "--count", head.Line], new GitRunOptions(ReadOnly: true));
            if (counted.Ok && int.TryParse(counted.Line, out int n)) versions = n;
        }

        // Live and thinned alike. ListIncludingThinned already drops a journal entry whose object is
        // genuinely gone, so every id here resolves — which matters, because ONE unresolvable id makes
        // `rev-list` fail outright and the enumeration would silently become empty.
        var points = RestorePoints.ListIncludingThinned(git);
        restorePoints = points.Count;
        foreach (var point in points)
            if (point.CommitId is { Length: > 0 } id) tips.Add(id);

        return tips;
    }

    /// <summary>
    /// The paths that are in the history and are not files in the workspace now.
    ///
    /// <para><b><c>--root</c> is load-bearing, not tidiness.</b> A restore point is a PARENTLESS commit
    /// by design (§5.2b) — that is what makes retention's thinning real — and without <c>--root</c> git
    /// shows no diff for one at all. A file that lived only inside restore points would then be absent
    /// from the answer, which is precisely the population this warning exists for.</para>
    ///
    /// <para><b>And it is a path walk rather than an object walk, which is the correction that
    /// matters.</b> <c>rev-list --objects</c> is three times faster and is WRONG here: it prints each
    /// object once, so two deleted files with identical content collapse to one name. Measured on a
    /// 340-state fixture, seventeen deleted files were reported as one — a warning that names some of
    /// the files is exactly the nearly-true guarantee §9A.4 rejects, and it fails silently, in the
    /// direction of saying less.</para>
    ///
    /// <para>Read with <c>-z</c> for <see cref="HistoryBrowser.Compare"/>'s reason: without it a path
    /// holding a quote or a non-ASCII character is re-encoded on the way out and mis-split on the way
    /// in. <c>-m</c> covers the one thing circuitRF never creates and a designer's own shell might — a
    /// merge whose tree holds a path neither parent had.</para>
    /// </summary>
    private static IReadOnlyList<string> DeletedPaths(GitCommand git, IReadOnlyList<string> tips)
    {
        if (tips.Count == 0) return [];

        // --stdin, not a command line: a workspace with a few hundred restore points would otherwise
        // build an argument list long enough to be refused by the operating system, and it would be
        // refused only on the workspaces that have the most history to warn about.
        var walked = git.Run(["log", "--stdin", "--root", "-m", "--name-only", "-z", "--pretty=format:"],
                             new GitRunOptions(StandardInput: string.Join('\n', tips) + "\n",
                                               ReadOnly: true));
        if (!walked.Ok) return [];

        SortedSet<string> deleted = new(StringComparer.Ordinal);

        foreach (string field in walked.StdOut.Split('\0'))
        {
            string path = field.Trim('\n', '\r');
            if (path.Length == 0) continue;

            string onDisk = Path.Combine(git.WorkspaceRoot, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(onDisk)) deleted.Add(path);
        }

        return [.. deleted];
    }

    /// <summary>
    /// What the repository directory weighs — the bytes the archive would gain, measured the same way
    /// the writer will spend them so the two cannot disagree (gate 6).
    /// </summary>
    public static long RepositoryBytes(string workspaceRoot)
    {
        long total = 0;
        foreach (string file in RepositoryFiles(workspaceRoot))
        {
            try { total += new FileInfo(file).Length; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return total;
    }

    /// <summary>
    /// Every file in the repository directory that travels, absolute.
    ///
    /// <para><b>A <c>.lock</c> file does not.</b> git's lock files are the momentary state of a write
    /// happening right now, and one copied into an archive arrives as a repository the recipient's git
    /// refuses to write to — a failure with no cause anyone could find, in a folder they were told
    /// holds only copies of their own files. Nothing else is filtered: what makes an archive with
    /// history worth having is that it IS the repository.</para>
    /// </summary>
    public static IEnumerable<string> RepositoryFiles(string workspaceRoot)
    {
        string root = RepositoryPathFor(workspaceRoot);
        if (!Directory.Exists(root)) yield break;

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { yield break; }

        foreach (string file in files)
        {
            if (Path.GetFileName(file).EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) continue;
            yield return file;
        }
    }

    /// <summary>
    /// The repository's own EMPTY directories, relative to the workspace root and '/'-separated.
    ///
    /// <para><b>Leaving these out produces an archive that is not a repository at all, and the packing
    /// R-rc8-9 requires is what creates them.</b> git decides a directory is a repository by looking
    /// for <c>objects</c> and <c>refs</c> inside it; <c>git gc</c> packs every loose reference into
    /// <c>packed-refs</c> and leaves <c>refs/heads</c> and <c>refs/tags</c> empty behind it. A zip that
    /// stores only FILES therefore drops <c>refs/</c> entirely, and the extracted workspace answers
    /// <i>fatal: not a git repository</i> — verified, not assumed. So the empty directories are stored
    /// as directory entries, which is also what a file manager's own unzip reads.</para>
    /// </summary>
    public static IEnumerable<string> RepositoryEmptyDirectories(string workspaceRoot)
    {
        string root = RepositoryPathFor(workspaceRoot);
        if (!Directory.Exists(root)) yield break;

        IEnumerable<string> directories;
        try { directories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { yield break; }

        foreach (string directory in directories)
        {
            bool empty;
            try { empty = !Directory.EnumerateFileSystemEntries(directory).Any(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (!empty) continue;

            yield return Path.GetRelativePath(workspaceRoot, directory).Replace('\\', '/');
        }
    }
}
