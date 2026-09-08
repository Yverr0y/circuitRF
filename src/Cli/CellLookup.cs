namespace CircuitRF.Cli;

/// <summary>
/// Which cell folders a workspace holds, and where the one called <c>&lt;name&gt;</c> is.
///
/// <para><b>By SHAPE, not by a manifest.</b> A cell folder is legal with no <c>.ccell</c> at all —
/// primacy is implicit when a view sub-folder holds exactly one file — so the test is
/// <see cref="DocumentKinds.LooksLikeCellFolder"/>, the same one <c>check</c>'s own walk uses. A scan
/// keyed on the manifest would miss a perfectly ordinary cell.</para>
///
/// <para><b>A nested workspace is somebody else's tree</b> and is not descended into, for the reason
/// <c>Check.WalkFolder</c> gives: two workspaces have different default technologies, and a cell
/// attributed to the wrong one is worse than a cell not found.</para>
///
/// <para><b>Ambiguity is reported, never resolved by taking the first.</b> Two cells of one name in
/// one tree is a real state — a library folder and a design folder both holding <c>Stage1</c> — and
/// picking one silently renders a picture of the wrong cell.</para>
///
/// <para>Separate from <c>Render</c> because it is the answer <c>explain --cells</c> gives (RND-3),
/// and a second copy of it there would be free to disagree with what <c>render</c> actually drew.</para>
/// </summary>
internal static class CellLookup
{
    /// <summary>How deep a scan goes below the workspace root. Deep enough for the folder-per-library
    /// arrangements circuitRF's own workspaces use, shallow enough that a workspace sitting above a
    /// large unrelated tree does not turn a name lookup into a full disk walk.</summary>
    private const int MaxDepth = 6;

    /// <summary>Every cell folder path under <paramref name="root"/>, in a stable order.</summary>
    public static IReadOnlyList<string> All(string root)
    {
        var found = new List<string>();
        Walk(root, 0, found);
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary>Every cell NAME under <paramref name="root"/> — what a refusal lists.</summary>
    public static IReadOnlyList<string> Names(string root)
        => [.. All(root).Select(p => Path.GetFileName(Path.TrimEndingDirectorySeparator(p)))
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>Every cell folder called <paramref name="name"/>. Zero, one or several — all three are
    /// answers the caller has to be told apart.</summary>
    public static IReadOnlyList<string> Find(string root, string name)
        => [.. All(root).Where(p => string.Equals(
                Path.GetFileName(Path.TrimEndingDirectorySeparator(p)), name, StringComparison.OrdinalIgnoreCase))];

    private static void Walk(string dir, int depth, List<string> found)
    {
        if (depth > MaxDepth) return;

        string[] subs;
        try { subs = Directory.GetDirectories(dir); }
        catch { return; }   // an unreadable folder holds no cells anyone can render

        foreach (var sub in subs)
        {
            if (File.Exists(Path.Combine(sub, DocumentKinds.CwsFileName))) continue;
            if (DocumentKinds.LooksLikeCellFolder(sub)) { found.Add(sub); continue; }
            Walk(sub, depth + 1, found);
        }
    }
}
