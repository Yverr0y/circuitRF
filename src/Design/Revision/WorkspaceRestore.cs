using CircuitRF.Diagnostics;

namespace CircuitRF.Design.Revision;

/// <summary>What a restore did.</summary>
/// <param name="Ok">Whether the workspace reached the state that was asked for.</param>
/// <param name="FilesWritten">How many files were brought back.</param>
/// <param name="FilesRemoved">How many added since were taken away.</param>
/// <param name="PreRestore">R-rc5-12a's entry for the state that was replaced — always taken, and
/// what makes the operation symmetric rather than a second cliff.</param>
/// <param name="Diagnostics">Everything worth reporting, failures included.</param>
public sealed record RestoreResult(
    bool                      Ok,
    int                       FilesWritten,
    int                       FilesRemoved,
    RestorePoint?             PreRestore,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// <b>Going back to an earlier state</b> (<c>docs/design/revision-control.md</c> §5.8, §6.3;
/// R-rc5-12 … R-rc5-14).
///
/// <para><b>It writes the state into the working tree and nothing is ever checked out</b>
/// (R-rc5-12c). The designer's own branch, if one exists yet, stays where it was, and
/// <c>HEAD</c> never moves. That is also what makes R-rc5-13's linear continuation possible: rev 4 of
/// the architecture said git requires a second line of work here and specified one, and it does not —
/// a second line is required only if the restore is a checkout. The next restore point, or in Stage 3
/// the next commit, simply records the restored content as the next step, which is what the designer
/// meant by "I went back to Tuesday" and the only shape that survives a shared history, where a second
/// line of work would be a merge nobody can perform.</para>
///
/// <para><b>Five rules, each silent when missed</b> (R-rc5-12c), and they are the whole of why this is
/// a type rather than two git calls:</para>
/// <list type="number">
///   <item><description>The state being replaced is recorded FIRST, always. By §5.3's design it is
///   whatever the designer has done since the last boundary, which can be a whole afternoon; a restore
///   that discarded it would be an automatic operation destroying history.</description></item>
///   <item><description>A file created after the entry is taken away — a workspace holding last
///   Tuesday's files plus Thursday's new cell matches neither state. <b>Only a file the pre-restore
///   entry actually captured</b>, which is what makes the removal safe and is why a file left out
///   under R-rc5-15a is never touched.</description></item>
///   <item><description>Ignored files are not touched. Results are not in a restore point, and a
///   restore that swept them away would destroy hours of simulation to bring back the design that
///   produced them.</description></item>
///   <item><description>The revision-control flag and the policy files are preserved. The workspace
///   file carries RC-6's off flag, so a restore across an off period would silently switch recording
///   on or off — a restore is the user's decision about CONTENT, never about RECORDING. And
///   <c>.gitignore</c> carries §8.2's answers, so a restore to before one was given would silently
///   start including the file it excluded.</description></item>
///   <item><description>An interrupted restore is detected on the next open
///   (<see cref="RestoreMarker"/>).</description></item>
/// </list>
///
/// <para><b>Unsaved documents and their undo stacks are the caller's half</b> (R-rc5-12b) and cannot
/// be done here: this project draws nothing and holds no window. A restore performed on top of dirty
/// documents produces a workspace matching neither state, and an undo afterwards re-applies the last
/// few minutes of the REPLACED state onto the RESTORED file — a document that existed at no moment
/// ever: well-formed, openable, and wrong.</para>
/// </summary>
public static class WorkspaceRestore
{
    /// <summary>
    /// How many files are written per invocation, or 0 for all of them at once — <b>which is the
    /// shipping value</b>, because one invocation is what makes a restore fast.
    ///
    /// <para>The seam exists so gate 17c can interrupt a restore where a dropped connection would:
    /// after some files and before the rest, with the marker on disk. A test sets this to 1 and throws
    /// from <see cref="AfterFilesWritten"/>. Nothing in the product sets it.</para>
    /// </summary>
    public static int FilesPerWrite { get; set; }

    /// <summary>Called with the running count after each batch of files reaches disk. The seam above.</summary>
    public static Action<int>? AfterFilesWritten { get; set; }

    /// <summary>
    /// Puts the workspace back to <paramref name="target"/>.
    /// </summary>
    /// <param name="preserveRevisionFlag">
    /// Whether to carry the workspace's own recording setting across (R-rc5-12c, rule 4). Always true
    /// in the product; a parameter so a test can prove the rule is doing something.
    /// </param>
    public static RestoreResult Restore(GitCommand git, RestorePoint target, bool preserveRevisionFlag = true)
    {
        List<Diagnostic> notes = [];

        // ── 1. The state being replaced, first and unconditionally (R-rc5-12a) ────────────────────
        var before = WorkspaceCheckpoints.Take(git, CheckpointOrigin.BeforeRestore, label: null,
                                               attended: false);
        foreach (var d in before.Diagnostics)
            if (d.Severity == DiagnosticSeverity.Error) notes.Add(d);

        if (notes.Count > 0)
            return new RestoreResult(false, 0, 0, null, notes);

        // The tree the workspace is in RIGHT NOW — recorded a moment ago, or identical to the newest
        // entry because nothing had changed. Either way it is the set of files a removal may act on.
        string? currentTree = before.TreeId;
        var     fallback    = before.Point ?? RestorePoints.Newest(git);

        // ── 2. What survives the write (R-rc5-12c, rule 4) ────────────────────────────────────────
        bool?  flag       = preserveRevisionFlag
                          ? WorkspaceRevisionSetting.Read(WorkspaceRevisionSetting.CwsPathFor(git.WorkspaceRoot))
                          : null;
        var    policy     = ReadPolicyFiles(git.WorkspaceRoot);

        // ── 3. The marker, before the first file (R-rc5-12c, rule 5) ──────────────────────────────
        var inFlight = new RestoreInFlight(
            new RestoreEnd(target.Reference, target.CommitId, target.Label),
            new RestoreEnd(fallback?.Reference ?? "", fallback?.CommitId ?? "", fallback?.Label ?? ""),
            DateTimeOffset.UtcNow);

        if (!RestoreMarker.Write(git.WorkspaceRoot, inFlight))
            return new RestoreResult(false, 0, 0, before.Point, [
                RestorePointMessages.CouldNotTake(
                    "going back to an earlier state",
                    "circuitRF could not write inside this workspace's history folder, and it will not "
                  + "start something it could not tell you had been interrupted.")]);

        string index = GitCheckpoint.PrivateIndexPath(git.WorkspaceRoot) + "-restore";

        try
        {
            try { File.Delete(index); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

            var options = new GitRunOptions(IndexFile: index);

            // ── 4. Write the state (R-rc5-12c, rule 1: nothing is checked out) ────────────────────
            var loaded = git.Run(["read-tree", target.TreeId], options);
            if (!loaded.Ok)
                return Fail(GitFailures.Translate(loaded, "going back to an earlier state", git.WorkspaceRoot));

            var targetPaths = PathsIn(git, target.TreeId);
            int written     = WriteFiles(git, options, targetPaths);
            if (written < 0)
                return Fail(GitFailures.Unrecognised("going back to an earlier state",
                                                     "the files could not be written"));

            // ── 5. Take away what came after — and ONLY what the pre-restore entry holds ──────────
            int removed = 0;
            if (currentTree is { Length: > 0 })
            {
                foreach (string path in PathsIn(git, currentTree))
                {
                    if (targetPaths.Contains(path)) continue;
                    if (IsPreserved(path)) continue;

                    try
                    {
                        string full = Path.Combine(git.WorkspaceRoot,
                                                   path.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(full)) { File.Delete(full); removed++; }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }

                RemoveEmptyDirectories(git.WorkspaceRoot);
            }

            // ── 6. Put back the two things a restore leaves as it found them ──────────────────────
            WritePolicyFiles(git.WorkspaceRoot, policy);
            if (preserveRevisionFlag)
                WorkspaceRevisionSetting.Write(WorkspaceRevisionSetting.CwsPathFor(git.WorkspaceRoot), flag);

            // ── 7. The marker goes after the last file ────────────────────────────────────────────
            RestoreMarker.Clear(git.WorkspaceRoot);

            notes.Add(RestorePointMessages.Restored(target.Label, written, removed));
            return new RestoreResult(true, written, removed, before.Point, notes);
        }
        finally
        {
            try { File.Delete(index); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        RestoreResult Fail(Diagnostic d) => new(false, 0, 0, before.Point, [d]);
    }

    /// <summary>
    /// R-rc5-12c, rule 5. Carries an interrupted restore through to the state it was heading for —
    /// which is one of the two actions the report offers, the other being <see cref="Restore"/> to the
    /// entry the marker names as the fallback.
    /// </summary>
    public static RestoreResult Finish(GitCommand git, RestoreInFlight inFlight)
    {
        var point = RestorePoints.List(git).FirstOrDefault(
            p => string.Equals(p.CommitId, inFlight.Target.CommitId, StringComparison.Ordinal));

        if (point is null)
            return new RestoreResult(false, 0, 0, null, [
                GitFailures.Unrecognised("finishing an interrupted change",
                                         "the state it was going to is no longer in this workspace's history")]);

        return Restore(git, point);
    }

    // ── The pieces ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the loaded state out. <c>checkout-index</c> is the plumbing that turns an index into
    /// files and it is the ONLY thing in this brief whose name contains that word — nothing switches,
    /// branches, stashes or resets, which gate 24 scans for.
    /// </summary>
    private static int WriteFiles(GitCommand git, GitRunOptions options, IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0) return 0;

        int perCall = FilesPerWrite;
        if (perCall <= 0)
        {
            var all = git.Run(["checkout-index", "-a", "-f"], options);
            if (!all.Ok) return -1;
            AfterFilesWritten?.Invoke(paths.Count);
            return paths.Count;
        }

        int done = 0;
        foreach (var chunk in paths.Chunk(perCall))
        {
            List<string> arguments = ["checkout-index", "-f", "--"];
            arguments.AddRange(chunk);

            var r = git.Run(arguments, options);
            if (!r.Ok) return -1;

            done += chunk.Length;
            AfterFilesWritten?.Invoke(done);
        }

        return done;
    }

    /// <summary>
    /// The two files a restore leaves exactly as it found them, plus the workspace file — which is
    /// restored like any other document and then has its one recording field put back.
    /// </summary>
    private static bool IsPreserved(string relativePath)
        => relativePath is WorkspacePolicyFiles.GitIgnoreName or WorkspacePolicyFiles.GitAttributesName;

    private static (string? Ignore, string? Attributes) ReadPolicyFiles(string root)
        => (TryRead(Path.Combine(root, WorkspacePolicyFiles.GitIgnoreName)),
            TryRead(Path.Combine(root, WorkspacePolicyFiles.GitAttributesName)));

    private static void WritePolicyFiles(string root, (string? Ignore, string? Attributes) policy)
    {
        TryWrite(Path.Combine(root, WorkspacePolicyFiles.GitIgnoreName),     policy.Ignore);
        TryWrite(Path.Combine(root, WorkspacePolicyFiles.GitAttributesName), policy.Attributes);
    }

    private static string? TryRead(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void TryWrite(string path, string? text)
    {
        if (text is null) return;
        try { File.WriteAllText(path, text); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static HashSet<string> PathsIn(GitCommand git, string treeId)
    {
        HashSet<string> paths = new(StringComparer.Ordinal);
        var r = git.Run(["ls-tree", "-r", "--name-only", "-z", treeId], new GitRunOptions(ReadOnly: true));
        if (!r.Ok) return paths;

        foreach (string entry in r.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            if (entry.Trim() is { Length: > 0 } p) paths.Add(p);
        return paths;
    }

    /// <summary>
    /// Drops directories a removal emptied. A folder left behind after its last file went is not a
    /// design state either — and a cell folder with nothing in it is one circuitRF's own project tree
    /// would show as a cell.
    /// </summary>
    private static void RemoveEmptyDirectories(string root)
    {
        void Descend(string dir, int depth)
        {
            if (depth > 64) return;

            string[] children;
            try { children = Directory.GetDirectories(dir); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }

            foreach (string child in children)
            {
                if (Path.GetFileName(child) is ".git") continue;
                Descend(child, depth + 1);

                try
                {
                    if (Directory.GetFileSystemEntries(child).Length == 0) Directory.Delete(child);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }

        Descend(Path.GetFullPath(root), 0);
    }
}
