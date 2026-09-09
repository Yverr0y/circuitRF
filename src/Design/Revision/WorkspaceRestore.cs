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
    IReadOnlyList<Diagnostic> Diagnostics)
{
    /// <summary>
    /// <b>Exactly which files the restore touched</b>, workspace-relative — or <b>null when that could
    /// not be worked out</b>, which is a different answer from "none".
    ///
    /// <para>It exists because the caller's half of a restore (R-rc5-12b) is per DOCUMENT, and without
    /// this the only honest way to satisfy it was to reload everything. A restore that changed one
    /// schematic used to rebuild the whole window — every panel, every tab, every generated cell —
    /// because nothing told the window that the other four tabs were already correct.</para>
    ///
    /// <para><b>Null means "reload everything", never "reload nothing"</b>. A caller that read a
    /// failed diff as an empty set would leave every open document showing the state that was just
    /// replaced, which is §1.3's failure with no error attached to it.</para>
    ///
    /// <para>Not a positional member: every construction of this record predates it, and a failure
    /// result has nothing to say here.</para>
    /// </summary>
    public IReadOnlyList<RestoredPath>? ChangedPaths { get; init; }
}

/// <summary>What a restore did to one path.</summary>
public enum RestoredPathKind
{
    /// <summary>Its content was brought back — it differed, or it was not there at all.</summary>
    Written,

    /// <summary>It came after the state being restored, so it was taken away.</summary>
    Removed,
}

/// <summary>One path a restore touched, workspace-relative with forward slashes (git's own spelling).</summary>
/// <param name="RelativePath">Where it is in the workspace.</param>
/// <param name="Kind">What the restore did to it.</param>
public sealed record RestoredPath(string RelativePath, RestoredPathKind Kind);

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
        //
        // "Unconditionally" is about the PROMISE, not about writing a duplicate. R-rc5-12a exists so
        // the state being replaced is recoverable; a state the history already holds is already
        // recoverable, and recording it again buys nothing and costs an entry.
        //
        // Owner, 2026-09-07: going back and forth between two states "a bunch of times" grew an entry
        // every single toggle. Comparing against the NEWEST entry alone could never catch it — on a
        // ping-pong the tree being replaced is always the one BEFORE the newest, so the test looked at
        // the wrong entry every time and always said "changed". Compared against the whole live list it
        // matches from the third operation on, which is the first one that is genuinely a repeat.
        //
        // THINNED entries are deliberately not in the set. One is still restorable, so skipping on a
        // match would be defensible — but the way forward below would then name a row the designer
        // cannot see in the list, and an entry that is hidden is not a way back anyone can take.
        var live      = RestorePoints.List(git);
        var heldTrees = new HashSet<string>(live.Select(p => p.TreeId), StringComparer.Ordinal);

        var before = WorkspaceCheckpoints.Take(git, CheckpointOrigin.BeforeRestore, label: null,
                                               attended: false, alsoHeldTrees: heldTrees);
        foreach (var d in before.Diagnostics)
            if (d.Severity == DiagnosticSeverity.Error) notes.Add(d);

        if (notes.Count > 0)
            return new RestoreResult(false, 0, 0, null, notes);

        // The tree the workspace is in RIGHT NOW — recorded a moment ago, or identical to an entry the
        // history already had. Either way it is the set of files a removal may act on.
        string? currentTree = before.TreeId;

        // THE ENTRY HOLDING THE STATE BEING REPLACED: the one just taken, or — when nothing was taken
        // because the state was already held — the entry that holds it. This is what the way forward
        // names, so it has to be a real entry either way. Pointing at the ORIGINAL rather than at a
        // fresh duplicate is also the better answer: it is the row the designer already knows.
        var replaced = before.Point
                    ?? (currentTree is { Length: > 0 }
                        ? live.FirstOrDefault(p => string.Equals(p.TreeId, currentTree, StringComparison.Ordinal))
                        : null);

        var fallback = replaced ?? RestorePoints.Newest(git);

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
            return new RestoreResult(false, 0, 0, replaced, [
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
            //
            // A failure BEFORE the first file is written clears the marker again; a failure after it
            // leaves it. That distinction is the marker's whole meaning — it says "this workspace may
            // be half of two states", and a workspace nothing was written into is not. Leaving it on
            // the early failures would greet the designer on the next open with a report naming two
            // states and two ways out of a situation they are not in, which trains them to dismiss the
            // one report that must never be dismissed.
            var loaded = git.Run(["read-tree", target.TreeId], options);
            if (!loaded.Ok)
                return FailBeforeAnyWrite(
                    GitFailures.Translate(loaded, "going back to an earlier state", git.WorkspaceRoot));

            var targetPaths = PathsIn(git, target.TreeId);
            if (targetPaths.Count == 0)
                return FailBeforeAnyWrite(GitFailures.Unrecognised(
                    "going back to an earlier state",
                    "that state holds no files, so there is nothing to bring back"));

            // ── 4a. WHAT DIFFERS, before a single file is written ─────────────────────────────────
            //
            // Git already knows, and it is almost never many. The pre-restore checkpoint above holds
            // the tree the workspace is in RIGHT NOW, so one read-only diff against the target answers
            // "which paths, and how" in milliseconds — and the alternative, `checkout-index -a`,
            // rewrites every file in the workspace whether it differs or not. That is not only slow:
            // it restamps the modification time of every untouched file, which is what turned a
            // one-schematic restore into a whole-window rebuild upstream.
            //
            // NULL is a third answer and it is not "nothing differs". A diff that could not be
            // produced falls back to writing the whole tree, exactly as before — a restore that is
            // slow is a nuisance, a restore that half-writes is §1.3's failure.
            var changed = ChangedBetween(git, currentTree, target.TreeId);

            int written = changed is null
                        ? WriteEverything(git, options, targetPaths)
                        : WriteFiles(git, options, [.. changed.Where(c => c.Kind == RestoredPathKind.Written)
                                                             .Select(c => c.RelativePath)]);
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

            // RC-7 R-rc7-6. What the tree was brought back FROM, for the next commit to name.
            //
            // It cannot be derived afterwards: from the content alone, a commit that went back to
            // Tuesday is indistinguishable from one that undid three days of work by hand, and those
            // are different decisions. A failure to write it is not a reason to fail the restore —
            // the workspace holds what was asked for, and all that is lost is one line in a message
            // that may never be written.
            // The identity goes with it so a later correction to this entry's title can find this copy
            // and replace it — see RestoreProvenance.Retitle, and the leak it exists to close.
            RestoreProvenance.Write(git.WorkspaceRoot,
                                    new RestoredState(target.Label, target.TakenUtc, target.CommitId));

            notes.Add(RestorePointMessages.Restored(target.Label, written, removed));
            return new RestoreResult(true, written, removed, replaced, notes)
                   { ChangedPaths = changed };
        }
        finally
        {
            try { File.Delete(index); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        RestoreResult Fail(Diagnostic d) => new(false, 0, 0, replaced, [d]);

        // The same failure, from a point where the working tree is untouched — so the marker goes with
        // it. See the comment at step 4.
        RestoreResult FailBeforeAnyWrite(Diagnostic d)
        {
            RestoreMarker.Clear(git.WorkspaceRoot);
            return Fail(d);
        }
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
    /// <b>Which paths differ between the state the workspace is in and the one being restored</b>, or
    /// null when that question could not be answered.
    ///
    /// <para>One read-only <c>diff-tree</c>, through <see cref="HistoryBrowser.TryCompare"/> — the
    /// parser that already exists, rather than a second one that would drift from it. <b>Renames are
    /// deliberately NOT detected</b>: to a reader "moved" is one fact, but to a caller acting on the
    /// paths a rename is a removal AND a write, and collapsing the pair hides one of the two paths
    /// that has to be touched.</para>
    ///
    /// <para>The two policy files are left out on both sides. They are put back verbatim a few steps
    /// later (rule 4), so a restore never changes them and a caller told otherwise would reload a
    /// document for a file whose content it just preserved.</para>
    /// </summary>
    private static IReadOnlyList<RestoredPath>? ChangedBetween(
        GitCommand git, string? currentTree, string targetTree)
    {
        if (currentTree is not { Length: > 0 }) return null;

        var diff = HistoryBrowser.TryCompare(git, currentTree, targetTree, findRenames: false);
        if (diff is null) return null;

        List<RestoredPath> changed = [];
        foreach (var c in diff)
        {
            // findRenames is off, so this cannot arrive — but reading one as a single write would
            // silently leave the old path on disk, which is the one outcome rule 2 exists to prevent.
            if (c.Kind == DocumentChangeKind.Renamed && c.PreviousPath is { Length: > 0 } was
                && !IsPreserved(was))
                changed.Add(new RestoredPath(was, RestoredPathKind.Removed));

            if (IsPreserved(c.RelativePath)) continue;

            changed.Add(new RestoredPath(
                c.RelativePath,
                c.Kind == DocumentChangeKind.Removed ? RestoredPathKind.Removed : RestoredPathKind.Written));
        }

        return changed;
    }

    /// <summary>
    /// How many paths one <c>checkout-index</c> invocation is asked for when the caller has not set
    /// <see cref="FilesPerWrite"/>. Named paths are what makes a restore write only what changed, and
    /// a command line has a length bound — so the set is chunked rather than passed whole. Large
    /// enough that the ordinary restore of a handful of files is still one process.
    /// </summary>
    private const int PathsPerWrite = 500;

    /// <summary>
    /// Writes the loaded state out. <c>checkout-index</c> is the plumbing that turns an index into
    /// files and it is the ONLY thing in this brief whose name contains that word — nothing switches,
    /// branches, stashes or resets, which gate 24 scans for.
    ///
    /// <para><b>Named paths, not <c>-a</c>.</b> <c>-a</c> writes every file in the workspace whether
    /// it differs or not, which is both the wait and — because it restamps every file's modification
    /// time — the reason a one-file restore looked to everything downstream like a workspace that had
    /// changed entirely. <see cref="WriteEverything"/> is still there for the case where the diff
    /// could not be produced.</para>
    /// </summary>
    private static int WriteFiles(GitCommand git, GitRunOptions options, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return 0;

        int perCall = FilesPerWrite > 0 ? FilesPerWrite : PathsPerWrite;

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
    /// The fallback: the whole tree, as every restore did before the diff existed. Reached only when
    /// <see cref="ChangedBetween"/> could not answer — a restore that is slow is a nuisance, and a
    /// restore that wrote half a state would be the defect the whole feature is written against.
    /// </summary>
    private static int WriteEverything(GitCommand git, GitRunOptions options,
                                       IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0) return 0;

        if (FilesPerWrite > 0) return WriteFiles(git, options, [.. paths]);

        var all = git.Run(["checkout-index", "-a", "-f"], options);
        if (!all.Ok) return -1;
        AfterFilesWritten?.Invoke(paths.Count);
        return paths.Count;
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
