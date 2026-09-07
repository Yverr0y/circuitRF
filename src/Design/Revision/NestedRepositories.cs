namespace CircuitRF.Design.Revision;

/// <summary>
/// Repositories NESTED inside the workspace (<c>revision-control.md</c> §12 Q4/Q30, §7A.5).
///
/// <para><b><c>rev-parse</c> walks UP, never down, so this needs a detection of its own.</b> The three
/// enclosing cases — circuitRF's own repository, the user's own at the root, an ancestor's — are one
/// <c>rev-parse --show-toplevel</c>. The fourth is a walk of the workspace tree for directories named
/// <c>.git</c>, done at the moment a checkpoint enumerates files anyway, so it costs nothing extra.</para>
///
/// <para><b>It matters mechanically as well as by principle.</b> Handing a directory that contains a
/// <c>.git</c> to <c>git add</c> records it as an EMBEDDED REPOSITORY — a pointer to that repository's
/// current commit — which is precisely §7A.5's "committed as something by the enclosing workspace",
/// with a warning nobody is reading. The subtree is excluded from every checkpoint by pathspec and
/// named once (<see cref="GitFailures.NestedRepositoryExcluded"/>).</para>
/// </summary>
public static class NestedRepositories
{
    /// <summary>
    /// Workspace-relative paths (forward slashes) of every directory inside <paramref name="root"/>
    /// that is itself a repository. The root's own <c>.git</c> is not one of them.
    ///
    /// <para>A <c>.git</c> that is a FILE counts too: that is how a worktree and a submodule check-out
    /// point at their real repository, and <c>git add</c> treats both the same way.</para>
    /// </summary>
    public static IReadOnlyList<string> Find(string root)
    {
        string full = Path.GetFullPath(root);
        List<string> found = [];
        Walk(full, full, found, depth: 0);
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>The <c>:(exclude)</c> pathspecs that keep those subtrees out of an <c>add</c>. Pathspec
    /// magic is git 1.9, well below R-rc3-3a's floor.</summary>
    public static IReadOnlyList<string> ExcludePathspecs(IReadOnlyList<string> relativePaths)
        => [.. relativePaths.Select(p => $":(exclude,glob){p}/**"),
            .. relativePaths.Select(p => $":(exclude){p}")];

    private static void Walk(string root, string dir, List<string> found, int depth)
    {
        if (depth > 64) return;   // a symlink cycle is not a reason to hang a checkpoint

        string[] children;
        try { children = Directory.GetDirectories(dir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }

        foreach (string child in children)
        {
            string name = Path.GetFileName(child);

            if (string.Equals(name, ".git", StringComparison.Ordinal))
            {
                if (!PathsEqual(dir, root)) found.Add(Relative(root, dir));
                continue;   // never descend into a repository's own object store
            }

            if (string.Equals(name, WorkspacePolicyFiles.GeneratedCellsFolder, StringComparison.OrdinalIgnoreCase))
                continue;

            Walk(root, child, found, depth + 1);
        }

        // A worktree or submodule check-out: `.git` is a FILE naming the real one.
        try
        {
            if (!PathsEqual(dir, root) && File.Exists(Path.Combine(dir, ".git")))
            {
                string rel = Relative(root, dir);
                if (!found.Contains(rel, StringComparer.Ordinal)) found.Add(rel);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(
               a.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
               b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
               OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static string Relative(string root, string dir)
        => Path.GetRelativePath(root, dir).Replace(Path.DirectorySeparatorChar, '/');
}
