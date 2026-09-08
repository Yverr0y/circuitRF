using CircuitRF.Diagnostics;

namespace CircuitRF.Design.Revision;

/// <summary>What one recording attempt produced.</summary>
/// <param name="CommitId">The commit written, or null when nothing was.</param>
/// <param name="TreeId">Its tree — what "nothing changed" is decided by.</param>
/// <param name="Recorded">False when the tree already matched and nothing was written.</param>
/// <param name="ExcludedRepositories">Nested repositories left out (§7A.5), workspace-relative.</param>
/// <param name="Diagnostic">The translated failure, or the Info that nothing had changed.</param>
public sealed record CheckpointResult(
    string?                 CommitId,
    string?                 TreeId,
    bool                    Recorded,
    IReadOnlyList<string>   ExcludedRepositories,
    Diagnostic?             Diagnostic);

/// <summary>
/// <b>The commit primitive.</b> Everything a checkpoint mechanically IS (<c>revision-control.md</c>
/// §5.2b, R-rc3-1b) and nothing about WHEN one is taken, what it is called, or how they are thinned —
/// RC-5 and RC-6 own those and build on this.
///
/// <para><b>It is built through a TEMPORARY INDEX, never the repository's shared one</b> (§4.6, §5.2b).
/// Add every file into a private index, write the tree, write the commit, update the reference.
/// <c>HEAD</c> and the designer's own branch never move, their staged state from a shell in the same
/// folder is untouched, and the one resource git's writers actually contend on is left to them. Two
/// circuitRF processes checkpointing at once then contend only on REFERENCE UPDATES, which git makes
/// atomic. <b>This is also what makes a linear restore possible</b>: a restore is a working-tree write,
/// and nothing ever checks anything out.</para>
///
/// <para><b>The commit has NO PARENT</b> (§5.2b, R-rc0-16). This is not a preference. §5.2a's whole
/// argument — that deleting one reference frees that checkpoint's objects — holds only if the next
/// checkpoint does not name this one as its parent. Chained, a deletion frees nothing, retention thins
/// nothing, and no gate that asserts SURVIVAL would notice.</para>
///
/// <para><b>It captures every file <c>.gitignore</c> does not exclude, INCLUDING files no checkpoint
/// has seen before.</b> The phrase "<c>commit -a</c>-shaped" elsewhere in the architecture names the
/// intent, not the command: a literal <c>commit -a</c> skips untracked files, and a new cell folder an
/// agent just created is exactly the file §1.2 exists to capture. Two exclusions apply beyond
/// <c>.gitignore</c> — a nested repository's subtree, and whatever the caller leaves out under
/// RC-5's large-file boundary.</para>
///
/// <para><b>Hooks never run.</b> With this plumbing path <c>git commit</c> is not even the command that
/// executes, and the invocation-level empty <c>core.hooksPath</c> covers every path regardless
/// (R-rc3-7a).</para>
/// </summary>
public static class GitCheckpoint
{
    /// <summary>
    /// The private index file. Inside <c>.git/</c> so it is never mistaken for a design file, never
    /// archived and never committed; per-PROCESS so two circuitRF processes do not collide on it, which
    /// is the whole point of not using the shared one.
    /// </summary>
    public static string PrivateIndexPath(string workspaceRoot)
        => Path.Combine(workspaceRoot, ".git",
                        $"crf-index-{Environment.ProcessId}-{Environment.CurrentManagedThreadId}");

    /// <summary>
    /// Records the workspace as it stands under <paramref name="reference"/>.
    /// </summary>
    /// <param name="git">Bound to the workspace root. Its <see cref="GitCommand.Identity"/> is who the
    /// commit is by — author AND committer.</param>
    /// <param name="reference">The full reference name RC-5 chose, e.g. <c>refs/crf/restore/17</c>.
    /// Outside <c>refs/heads/</c> deliberately: nothing here creates a branch, ever (R-rc0-17).</param>
    /// <param name="message">The commit message, trailers and all. Piped on stdin, never assembled
    /// into a command line.</param>
    /// <param name="previousTreeId">The newest checkpoint's tree, or null. When the new tree equals it,
    /// <b>nothing is written</b> — which is a tree comparison rather than a substring match on git's
    /// "nothing to commit" (R-rc3-4).</param>
    /// <param name="extraExclusions">Workspace-relative paths the caller is leaving out — RC-5's
    /// large-file boundary. Recorded by the caller in the commit's own metadata.</param>
    /// <param name="alsoHeldTrees">
    /// Other trees the history ALREADY holds. When the new tree is one of them, nothing is written —
    /// for exactly <paramref name="previousTreeId"/>'s reason, over a set rather than one entry.
    ///
    /// <para>Null everywhere but the entry taken before a restore, where comparing against the newest
    /// alone is not enough: going back and forth between two states leaves each one recorded already,
    /// but never as the NEWEST, so every toggle wrote another copy of a state the history had
    /// (owner, 2026-09-07).</para>
    /// </param>
    public static CheckpointResult Record(
        GitCommand            git,
        string                reference,
        string                message,
        string?               previousTreeId   = null,
        IReadOnlyList<string>? extraExclusions = null,
        IReadOnlySet<string>?  alsoHeldTrees   = null)
    {
        // §4.4's first sentence, checked BEFORE the invocation rather than recognised from git's
        // English afterwards: if nobody can be named, the feature does not arm.
        git.Identity ??= RevisionIdentity.Resolve(git);
        if (git.Identity is null)
            return new CheckpointResult(null, null, false, [], GitFailures.NoIdentity());

        var nested = NestedRepositories.Find(git.WorkspaceRoot);
        string index = PrivateIndexPath(git.WorkspaceRoot);

        try
        {
            try { File.Delete(index); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

            var options = new GitRunOptions(IndexFile: index);

            List<string> add = ["add", "--all", "--"];
            add.Add(".");
            add.AddRange(NestedRepositories.ExcludePathspecs(nested));
            if (extraExclusions is { Count: > 0 })
                add.AddRange(extraExclusions.Select(p => $":(exclude){p}"));

            var added = git.Run(add, options);
            if (!added.Ok)
                return Fail(GitFailures.Translate(added, "taking a restore point", git.WorkspaceRoot), nested);

            var tree = git.Run(["write-tree"], options);
            if (!tree.Ok || tree.Line.Length == 0)
                return Fail(GitFailures.Translate(tree, "taking a restore point", git.WorkspaceRoot), nested);

            string treeId = tree.Line;

            // §5.3: a boundary whose tree is one the history already holds records nothing. Not a
            // failure — the state is already recorded. The tree is computed above either way, so
            // asking about a SET costs no more than asking about one entry.
            if ((previousTreeId is { Length: > 0 }
                 && string.Equals(previousTreeId, treeId, StringComparison.Ordinal))
                || alsoHeldTrees?.Contains(treeId) == true)
                return new CheckpointResult(null, treeId, false, nested, GitFailures.NothingToRecord());

            // PARENTLESS — no `-p`. See the type's own remarks; this is what makes thinning real.
            var commit = git.Run(["commit-tree", treeId],
                                 options with { StandardInput = message });
            if (!commit.Ok || commit.Line.Length == 0)
                return Fail(GitFailures.Translate(commit, "taking a restore point", git.WorkspaceRoot), nested);

            string commitId = commit.Line;

            var updated = git.Run(["update-ref", reference, commitId], options);
            if (!updated.Ok)
                return Fail(GitFailures.Translate(updated, "taking a restore point", git.WorkspaceRoot), nested);

            return new CheckpointResult(commitId, treeId, true, nested, null);
        }
        finally
        {
            try { File.Delete(index); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        CheckpointResult Fail(Diagnostic d, IReadOnlyList<string> excluded)
            => new(null, null, false, excluded, d);
    }

    /// <summary>
    /// The tree a reference points at, or null when it does not resolve. A READ — it must not wait on a
    /// writer, so it takes no optional locks (R-rc3-1b).
    /// </summary>
    public static string? TreeOf(GitCommand git, string reference)
    {
        var r = git.Run(["rev-parse", "--verify", "--quiet", reference + "^{tree}"],
                        new GitRunOptions(ReadOnly: true));
        return r.Ok && r.Line.Length > 0 ? r.Line : null;
    }
}
