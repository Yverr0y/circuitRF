namespace CircuitRF.Design.Workspace;

/// <summary>How a workspace may write through one of its <c>ws://</c> references (RC-2, §7A.2).</summary>
public enum ReferenceAccess
{
    /// <summary>The path is not inside any workspace this one references — it is this workspace's own
    /// content, a library, or a loose file. Nothing here applies to it.</summary>
    NotReferenced,

    /// <summary>The path belongs to a referenced workspace whose reference is read-only — the
    /// default, including on every <c>.cws</c> written before
    /// <see cref="CwsWorkspaceRef.Editable"/> existed.</summary>
    ReadOnly,

    /// <summary>The path belongs to a referenced workspace someone explicitly made editable. It is
    /// marked wherever it appears (R-rc2-5), because two designers doing this in one library is the
    /// concurrent-edit problem §6.2 describes and circuitRF is not solving it.</summary>
    Editable,
}

/// <summary>
/// RC-2 (brief-revision-control-2-read-only-references.md, <c>docs/design/revision-control.md</c>
/// §7A.2): <b>should</b> circuitRF write into this file, given which workspace is asking?
///
/// <para><b>This is a different question from <see cref="WorkspaceWritability"/>'s and the two
/// combine as an OR.</b> That type answers "CAN circuitRF write here?" — discovered by attempting a
/// write, a fact about the filesystem, the same answer for everybody. This one answers "SHOULD
/// circuitRF write here, from the workspace that is open?" — a policy carried on the reference, true
/// even when the filesystem would allow the write, and false again from the window that OWNS the
/// content. A document is read-only if either says so.</para>
///
/// <para><b>It is deliberately asked with the REFERENCING workspace as an argument rather than
/// memoised globally per root.</b> The same cell folder is read-only through workspace A's window
/// and writable through B's own — that is the whole of §7A.3: the edit is not forbidden, it is
/// routed to the workspace that owns it. A global "this root is read-only" flag (which is what
/// <c>WorkspaceWritability.OpenReadOnlyThisSession</c> is, correctly, for a different question)
/// would refuse the owner's own save.</para>
///
/// <para>Framework-free and in <c>src/Design</c> for the ordinary reason: the reference table it
/// reads is a <c>.cws</c> structure and <c>src/Cli</c> resolves these references too. <b>Nothing
/// here reaches for git</b>, and none of it depends on a repository existing — §7A.2 is a defect in
/// the workspace model that revision control merely found.</para>
/// </summary>
public static class ReferencedWorkspacePolicy
{
    /// <summary>
    /// The access <paramref name="path"/> has from the workspace rooted at
    /// <paramref name="referencingWorkspaceRoot"/>, plus the owning workspace's root and the alias it
    /// is referenced under.
    ///
    /// <para>A path inside the referencing workspace itself, a loose file, or a path under a
    /// referenced workspace that cannot be resolved all answer
    /// <see cref="ReferenceAccess.NotReferenced"/> — this type states a policy about content another
    /// workspace owns, and says nothing about anything else.</para>
    ///
    /// <para><b>When two aliases name the same workspace and disagree, EDITABLE wins.</b> Editable is
    /// the state somebody explicitly chose; refusing on the strength of the other entry would make a
    /// deliberate choice depend on alphabetical order.</para>
    /// </summary>
    public static ReferenceAccess AccessFor(
        string? referencingWorkspaceRoot, string? path, out string? ownerRoot, out string? alias)
    {
        ownerRoot = null;
        alias     = null;
        if (string.IsNullOrWhiteSpace(referencingWorkspaceRoot) || string.IsNullOrWhiteSpace(path))
            return ReferenceAccess.NotReferenced;

        string target;
        try   { target = WorkspaceRootFinder.Normalize(path); }
        catch { return ReferenceAccess.NotReferenced; }
        if (target.Length == 0) return ReferenceAccess.NotReferenced;

        var found = ReferenceAccess.NotReferenced;
        foreach (var (entryAlias, root, editable) in EntriesFor(referencingWorkspaceRoot))
        {
            if (root is null || !IsAtOrUnder(target, root)) continue;

            // Editable wins over read-only, and the FIRST answer wins over a later equal one, so the
            // alias reported is stable for a given .cws rather than "whichever was last".
            if (found == ReferenceAccess.NotReferenced || (editable && found == ReferenceAccess.ReadOnly))
            {
                found     = editable ? ReferenceAccess.Editable : ReferenceAccess.ReadOnly;
                ownerRoot = root;
                alias     = entryAlias;
            }
        }

        if (found == ReferenceAccess.NotReferenced) { ownerRoot = null; alias = null; }
        return found;
    }

    /// <inheritdoc cref="AccessFor(string?, string?, out string?, out string?)"/>
    public static ReferenceAccess AccessFor(string? referencingWorkspaceRoot, string? path)
        => AccessFor(referencingWorkspaceRoot, path, out _, out _);

    /// <summary>
    /// The workspace that OWNS <paramref name="path"/> when it is reached through a READ-ONLY
    /// reference from <paramref name="referencingWorkspaceRoot"/> — the root the refusal names and
    /// the window the edit is routed to (R-rc2-6/-7). Null in every other case.
    /// </summary>
    public static string? ReadOnlyOwnerRootFor(string? referencingWorkspaceRoot, string? path)
        => AccessFor(referencingWorkspaceRoot, path, out string? owner, out _) == ReferenceAccess.ReadOnly
            ? owner
            : null;

    /// <summary>True when this reference lets the referencing window edit through it. Absent, or an
    /// alias that is not recorded at all, is read-only (R-rc2-4).</summary>
    public static bool IsReferenceEditable(string? referencingWorkspaceRoot, string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias)) return false;
        foreach (var (entryAlias, _, editable) in EntriesFor(referencingWorkspaceRoot))
            if (string.Equals(entryAlias, alias, StringComparison.OrdinalIgnoreCase) && editable)
                return true;
        return false;
    }

    /// <summary>
    /// Writes <see cref="CwsWorkspaceRef.Editable"/> for one alias — the per-reference override
    /// R-rc2-4 promises is one click away, and the ONE place it is written.
    ///
    /// <para>Returns false with a sentence when the <c>.cws</c> cannot be read, cannot be written
    /// (SL2's choke point answers that), or names no such alias. Setting the value it already has is
    /// success with nothing written.</para>
    /// </summary>
    public static bool SetReferenceEditable(
        string workspaceRoot, string alias, bool editable, out string? error)
    {
        error = null;
        string cwsPath = Path.Combine(workspaceRoot, ".cws");

        CwsFile cws;
        try   { cws = WorkspacePersistence.LoadFromFile(cwsPath); }
        catch (Exception ex) { error = $"Could not read this workspace's .cws: {ex.Message}"; return false; }

        var entry = (cws.ReferencedWorkspaces ?? []).FirstOrDefault(
            r => string.Equals(r.Alias, alias, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            error = $"No workspace reference named \"{alias}\" is recorded here.";
            return false;
        }

        // RC-9 R-rc9-8. A pinned reference resolves to a rebuildable copy of one recorded version, so
        // there is nothing there an edit could usefully write to — see the rule in Read() below. It is
        // refused HERE rather than silently ignored, because a toggle that appears to work and does
        // nothing is exactly the class of failure this whole area is written against.
        if (editable && entry.Pin is { Length: > 0 })
        {
            error = $"This design uses a fixed version of \"{alias}\", so its cells cannot be edited " +
                    "from here — what you would be editing is circuitRF's copy of that version, not " +
                    "the workspace itself. Stop using a fixed version first, or open that workspace " +
                    "and edit it there.";
            return false;
        }

        if (entry.Editable == editable) return true;
        entry.Editable = editable;

        try
        {
            if (!WorkspacePersistence.SaveToFileAtomic(cwsPath, cws))
            {
                error = $"'{Path.GetFileName(Path.GetDirectoryName(cwsPath))}' is read-only on this " +
                        "machine, so the change could not be recorded in its .cws.";
                return false;
            }
        }
        catch (Exception ex) { error = $"Could not write this workspace's .cws: {ex.Message}"; return false; }

        InvalidateCache();
        return true;
    }

    // ── The table, memoised ───────────────────────────────────────────────────
    //
    // Asked on every edit gesture and on every menu-state refresh, so it is memoised on the same
    // terms as the alias table beside it (ExternalCellRef) and dropped at the same moment. It is a
    // SECOND table rather than a widening of that one because the two are read by different layers:
    // ExternalCellRef's is how a ws:// reference RESOLVES, and resolution deliberately does not read
    // this flag — a read-only reference addresses exactly what an editable one does.

    private static readonly Dictionary<string, IReadOnlyList<(string Alias, string? Root, bool Editable)>> _memo =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock _memoGate = new();

    private static IReadOnlyList<(string Alias, string? Root, bool Editable)> EntriesFor(string? workspaceRoot)
    {
        string key = WorkspaceRootFinder.Normalize(workspaceRoot);

        lock (_memoGate)
            if (_memo.TryGetValue(key, out var memo)) return memo;

        var entries = Read(key);

        lock (_memoGate) _memo[key] = entries;
        return entries;
    }

    private static IReadOnlyList<(string Alias, string? Root, bool Editable)> Read(string workspaceRoot)
    {
        if (string.IsNullOrEmpty(workspaceRoot)) return [];

        CwsFile cws;
        try   { cws = WorkspacePersistence.LoadFromFile(Path.Combine(workspaceRoot, ".cws")); }
        catch { return []; }   // no .cws, or one this build cannot read — no references, not an error

        var list = new List<(string, string?, bool)>();
        foreach (var entry in cws.ReferencedWorkspaces ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.Alias)) continue;

            // RC-9 R-rc9-8: A PINNED REFERENCE IS READ-ONLY WHATEVER THE EDITABILITY FLAG SAYS, and
            // this is a correctness rule rather than a policy preference. A pinned alias resolves to an
            // expanded copy of one recorded version (PinnedContent) — a rebuildable cache in the
            // per-user state directory, not the library. An edit through it would write into that
            // cache: it would appear to work, it would not reach the library, it would not be in
            // anybody's history, and it would vanish the next time the cache was rebuilt. That is
            // every failure §7A.2 exists to prevent, with an extra one on the end.
            bool editable = entry.Editable && entry.Pin is not { Length: > 0 };

            // Resolved through the alias table rather than by re-implementing the walk: one rule for
            // where a reference points, whatever is being asked about it.
            list.Add((entry.Alias, ExternalCellRef.WorkspaceRootForAlias(workspaceRoot, entry.Alias), editable));
        }
        return list;
    }

    /// <summary>Forgets the memoised tables. Called by
    /// <see cref="WorkspaceRootFinder.InvalidateCache"/>, alongside the alias table this one is read
    /// beside — a <c>.cws</c> being rewritten changes both answers at once.</summary>
    public static void InvalidateCache()
    {
        lock (_memoGate) _memo.Clear();
    }

    /// <summary>
    /// The prefix rule <c>WorkspaceWritability</c> already uses: a path IS the root, or sits under it
    /// behind a separator. Comparing without the separator check would make <c>…/stdlib-old</c> read
    /// as content of <c>…/stdlib</c>.
    /// </summary>
    private static bool IsAtOrUnder(string normalizedPath, string normalizedRoot)
    {
        if (normalizedRoot.Length == 0) return false;
        if (normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)) return true;
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            && normalizedPath.Length > normalizedRoot.Length
            && (normalizedPath[normalizedRoot.Length] == Path.DirectorySeparatorChar
                || normalizedPath[normalizedRoot.Length] == Path.AltDirectorySeparatorChar);
    }
}
