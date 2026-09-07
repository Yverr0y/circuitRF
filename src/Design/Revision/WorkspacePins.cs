using CircuitRF.Design.Workspace;
using CircuitRF.Diagnostics;

namespace CircuitRF.Design.Revision;

/// <summary>What a pinned reference is doing right now.</summary>
public enum PinStatus
{
    /// <summary>No pin recorded — the reference follows whatever the other workspace contains. The
    /// default, and what every <c>.cws</c> written before the field existed says.</summary>
    Unpinned,

    /// <summary>Pinned, honourable, and naming the newest version the other workspace has.</summary>
    Current,

    /// <summary>Pinned, honourable, and the other workspace has moved on (R-rc9-12). <b>Nothing here
    /// has changed and nothing will until the designer says so.</b></summary>
    NewerAvailable,

    /// <summary>Pinned to something that cannot be reached (R-rc9-16) — the workspace is not there, or
    /// that version is not in its history any more. <b>Reported, never fallen back from.</b></summary>
    CannotBeHonoured,

    /// <summary>The other workspace keeps no history, so there is nothing to pin to and nothing to
    /// compare against. Not an error: most workspaces are like this.</summary>
    NoHistoryThere,
}

/// <summary>One referenced workspace's pin, as it stands.</summary>
/// <param name="Alias">The name <c>ws://alias/…</c> uses.</param>
/// <param name="Pin">The recorded identity, or null when unpinned.</param>
/// <param name="OwnerRoot">Where the referenced workspace is, or null when it cannot be found.</param>
/// <param name="Status">What that adds up to.</param>
/// <param name="Newest">The newest identity the other workspace has, when it has a history.</param>
/// <param name="Diagnostic">The sentence for this state, or null when there is nothing to say.</param>
public sealed record PinState(
    string      Alias,
    string?     Pin,
    string?     OwnerRoot,
    PinStatus   Status,
    string?     Newest,
    Diagnostic? Diagnostic)
{
    /// <summary>True when a designer has something to act on — the state R-rc9-12 says is visible and
    /// per-reference, with an explicit action and no prompt.</summary>
    public bool HasNewerVersion => Status == PinStatus.NewerAvailable;
}

/// <summary>
/// RC-9 §3 and §4: <b>the pin</b> — which version of a referenced workspace this design was built and
/// verified against (<c>revision-control.md</c> §7, §7A.4; R-rc9-8 … R-rc9-17).
///
/// <para><b>Under version control an unversioned reference is a hazard.</b> <i>"This design uses the
/// amplifier cell from that workspace"</i> resolves to whatever that workspace contains today, which is
/// not reproducible and not what was simulated. A pin makes it reproducible in the strong sense, from a
/// commit identity rather than from "the files as they were" — <b>the only feature in this series that
/// changes what a simulation result means</b>.</para>
///
/// <para><b>What it buys is decoupling</b> (R-rc9-11 … R-rc9-14). The librarian publishes whenever they
/// like and nothing in any designer's workspace changes. <i>"A newer version is available"</i> is a
/// visible, per-reference state with an explicit action — not an ambient event and not a prompt. Taking
/// it is a change to the local <c>.cws</c>, so it lands in the local history with a date and an author,
/// which is what answers <i>"when did my design start using the new library?"</i> — usually the
/// question being asked when something stopped working. And a checkpoint captures the pin along with
/// everything else, <b>so restoring to last Tuesday restores which library version last Tuesday's
/// design resolved against.</b> Without the pin a restore is only partial and does not say so.</para>
///
/// <para><b>It is on the alias</b> (R-rc9-9): one referenced workspace is one repository with one
/// commit identity. Pinning per cell would let one design reference two mutually inconsistent versions
/// of one library — a state nobody wants and nothing detects.</para>
///
/// <para><b>What it is not</b> (R-rc9-17). Not a package manager: no version ranges, no resolution, no
/// transitive constraint solving. <b>A pin is an exact identity and moving it is a human
/// decision.</b></para>
///
/// <para><b>Nothing here reaches a network.</b> Comparing against the newest version reads the
/// referenced workspace's repository as it is on this machine; bringing something newer INTO that
/// workspace is that workspace's own fetch, made by whoever owns it (R-rc9-6).</para>
/// </summary>
public static class WorkspacePins
{
    /// <summary>
    /// The pin recorded for one alias, or null. Reads the <c>.cws</c> directly rather than through a
    /// cache: this is asked when a state is being changed or reported, not per render.
    /// </summary>
    public static string? PinFor(string workspaceRoot, string alias)
        => EntryFor(Read(workspaceRoot), alias)?.Pin is { Length: > 0 } pin ? pin : null;

    /// <summary>Every alias that carries a pin, alias → identity. What
    /// <c>ExternalCellRef</c>'s alias table consults, and the only part of this type on the resolution
    /// path.</summary>
    public static IReadOnlyDictionary<string, string> PinnedAliases(string workspaceRoot)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Read(workspaceRoot)?.ReferencedWorkspaces ?? [])
            if (!string.IsNullOrWhiteSpace(entry.Alias) && entry.Pin is { Length: > 0 } pin)
                map[entry.Alias] = pin;
        return map;
    }

    // ── What the designer is shown (R-rc9-12) ─────────────────────────────────────────────────────

    /// <summary>
    /// Every referenced workspace's pin state, in the order the <c>.cws</c> lists them.
    ///
    /// <para><b>Deliberately not on the resolution path.</b> Resolving a pinned reference expands its
    /// content and asks nothing about what is newer; working out whether something newer exists runs
    /// git once per pinned alias and belongs to an explicit moment — opening the workspace, or pressing
    /// Refresh — rather than to every render. Unpinned aliases cost nothing at all.</para>
    /// </summary>
    public static IReadOnlyList<PinState> Survey(string workspaceRoot)
    {
        List<PinState> states = [];
        foreach (var entry in Read(workspaceRoot)?.ReferencedWorkspaces ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.Alias)) continue;
            states.Add(StateOf(workspaceRoot, entry.Alias));
        }
        return states;
    }

    /// <summary>One alias's pin state.</summary>
    public static PinState StateOf(string workspaceRoot, string alias)
    {
        var entry = EntryFor(Read(workspaceRoot), alias);
        string? pin = entry?.Pin is { Length: > 0 } p ? p : null;

        // The alias table already knows where an alias points, and it is the one rule for that. For a
        // PINNED alias it answers with the expanded copy, so the real workspace is asked for directly —
        // "where does this reference point" and "where does it resolve to" are different questions the
        // moment a pin exists, and only this type may confuse them.
        string? owner = OwnerRootOf(workspaceRoot, entry);

        if (pin is null)
            return new PinState(alias, null, owner, PinStatus.Unpinned, null, null);

        if (owner is null)
            return new PinState(alias, pin, null, PinStatus.CannotBeHonoured, null,
                SharingMessages.PinCannotBeHonoured(alias, pin, SharingMessages.BecauseTheWorkspaceIsNotThere));

        if (GitCommand.For(owner) is not { } git || !git.IsRepositoryRoot())
            return new PinState(alias, pin, owner, PinStatus.CannotBeHonoured, null,
                SharingMessages.PinCannotBeHonoured(alias, pin, SharingMessages.BecauseThatVersionIsNotInItsHistory));

        if (!PinnedContent.CommitExists(git, pin))
        {
            // The memo below resolution is deliberately longer-lived than the alias table (see its own
            // remarks), so THIS is the path that drops a stale entry: a library whose history was
            // rewritten must stop resolving, and the survey is what notices.
            PinnedContent.InvalidateCache(owner, pin);
            return new PinState(alias, pin, owner, PinStatus.CannotBeHonoured, null,
                SharingMessages.PinCannotBeHonoured(alias, pin, SharingMessages.BecauseThatVersionIsNotInItsHistory));
        }

        string? newest = PinnedContent.NewestCommit(git);
        bool    moved  = newest is { Length: > 0 } && !SameCommit(newest, pin);

        return moved
            ? new PinState(alias, pin, owner, PinStatus.NewerAvailable, newest,
                           SharingMessages.NewerVersionAvailable(alias))
            : new PinState(alias, pin, owner, PinStatus.Current, newest, null);
    }

    // ── Changing one (R-rc9-8, R-rc9-13) ──────────────────────────────────────────────────────────

    /// <summary>
    /// Fixes this design to the referenced workspace's newest version, or — with
    /// <paramref name="commitId"/> given — to a version the caller already chose.
    ///
    /// <para><b>The write is to this workspace's own <c>.cws</c> and to nothing else</b> (R-rc9-13,
    /// R-rc0-5). That is what puts the decision in the local history with a date and an author, and it
    /// is the whole of what "moving a pin" does: nothing is copied, nothing is fetched, and the
    /// referenced workspace is not touched.</para>
    /// </summary>
    public static PinChange Pin(string workspaceRoot, string alias, string? commitId = null)
    {
        var cws = Read(workspaceRoot);
        if (EntryFor(cws, alias) is not { } entry)
            return PinChange.Refused(NoSuchAlias(alias));

        string? owner = OwnerRootOf(workspaceRoot, entry);
        if (owner is null)
            return PinChange.Refused(SharingMessages.PinCannotBeHonoured(
                alias, commitId ?? "", SharingMessages.BecauseTheWorkspaceIsNotThere));

        string? wanted = commitId;
        if (wanted is null)
        {
            if (GitCommand.For(owner) is not { } git || !git.IsRepositoryRoot()
                || PinnedContent.NewestCommit(git) is not { } newest)
                return PinChange.Refused(SharingMessages.CannotPinWithoutAHistory(alias));
            wanted = newest;
        }

        if (entry.Pin is { Length: > 0 } already && SameCommit(already, wanted))
            return new PinChange(true, false, wanted, SharingMessages.PinAlreadyCurrent(alias));

        bool moving  = entry.Pin is { Length: > 0 };
        entry.Pin    = wanted;

        if (!Write(workspaceRoot, cws!, out var failure)) return PinChange.Refused(failure!);

        return new PinChange(true, true, wanted,
            moving ? SharingMessages.TookNewerVersion(alias, wanted)
                   : SharingMessages.Pinned(alias, wanted));
    }

    /// <summary>R-rc9-12's explicit action: move to the newest version the referenced workspace has.
    /// Identical to <see cref="Pin(string, string, string?)"/> with no identity — named separately
    /// because it is a different gesture with a different label.</summary>
    public static PinChange TakeNewerVersion(string workspaceRoot, string alias)
        => Pin(workspaceRoot, alias);

    /// <summary>Back to following the referenced workspace as its owner changes it.</summary>
    public static PinChange Unpin(string workspaceRoot, string alias)
    {
        var cws = Read(workspaceRoot);
        if (EntryFor(cws, alias) is not { } entry)
            return PinChange.Refused(NoSuchAlias(alias));

        if (entry.Pin is not { Length: > 0 })
            return new PinChange(true, false, null, SharingMessages.Unpinned(alias));

        entry.Pin = null;
        if (!Write(workspaceRoot, cws!, out var failure)) return PinChange.Refused(failure!);

        return new PinChange(true, true, null, SharingMessages.Unpinned(alias));
    }

    // ── Resolution's one entry point (R-rc9-15, R-rc9-16) ─────────────────────────────────────────

    /// <summary>
    /// Where a pinned alias resolves to: the expanded copy of that version, or <b>null</b> when the pin
    /// cannot be honoured.
    ///
    /// <para><b>Null, never the current content.</b> Falling back to whatever the other workspace holds
    /// now would defeat the entire feature — the design would be built against something it was never
    /// verified against, silently. An unresolvable alias is a state circuitRF already draws and
    /// reports (a cell that places through it draws as Not Found), so the failure is visible by
    /// construction; <paramref name="why"/> is what turns it into a sentence.</para>
    /// </summary>
    public static string? ResolvePinned(string ownerRoot, string commitId, out string? why)
    {
        why = null;

        // Before anything that starts a process. Measured: five pinned libraries cost ~100 ms of
        // subprocess starts to re-verify, and the alias table above this is dropped on ordinary
        // editing events — so without this the answer would be recomputed every time somebody saved a
        // symbol. See PinnedContent's own remarks for why the memo may outlive that table.
        if (PinnedContent.TryMemoised(ownerRoot, commitId, out string? memoised))
        {
            if (memoised is null) why = SharingMessages.BecauseThatVersionIsNotInItsHistory;
            return memoised;
        }

        if (GitCommand.For(ownerRoot) is not { } git)
        {
            // No git on this machine at all. The pin is not dishonoured by that — there is simply no
            // way to read it — and R-rc3-3's silence does not extend to a design that says it needs a
            // particular version, so it is reported rather than ignored.
            why = SharingMessages.BecauseThatVersionIsNotInItsHistory;
            return null;
        }

        if (!git.IsRepositoryRoot())
        {
            why = SharingMessages.BecauseThatVersionIsNotInItsHistory;
            return null;
        }

        return PinnedContent.Materialise(git, commitId, out why);
    }

    // ── Reading and writing the .cws ──────────────────────────────────────────────────────────────

    private static CwsFile? Read(string workspaceRoot)
    {
        try { return WorkspacePersistence.LoadFromFile(CwsPathOf(workspaceRoot)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
                                   or System.Text.Json.JsonException)
        { return null; }
    }

    private static bool Write(string workspaceRoot, CwsFile cws, out Diagnostic? failure)
    {
        failure = null;
        try
        {
            // SL2's one write choke point, exactly as RC-2's editability flag uses it. A read-only
            // workspace answers false here rather than throwing.
            if (WorkspacePersistence.SaveToFileAtomic(CwsPathOf(workspaceRoot), cws)) { Invalidate(); return true; }
            failure = GitFailures.Unrecognised(
                "recording which version of that workspace this design uses",
                $"'{workspaceRoot}' is read-only on this machine.");
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            failure = GitFailures.Unrecognised(
                "recording which version of that workspace this design uses", e.Message);
            return false;
        }
    }

    private static string CwsPathOf(string workspaceRoot)
        => Path.Combine(workspaceRoot, WorkspacePersistence.FileName);

    private static CwsWorkspaceRef? EntryFor(CwsFile? cws, string alias)
        => (cws?.ReferencedWorkspaces ?? []).FirstOrDefault(
               r => string.Equals(r.Alias, alias, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Where the reference POINTS — the referenced workspace itself, not the expanded copy a pin
    /// resolves to. <c>ExternalCellRef</c>'s alias table cannot answer this once a pin exists, because
    /// by then it answers the other question.
    /// </summary>
    private static string? OwnerRootOf(string workspaceRoot, CwsWorkspaceRef? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Path)) return null;
        if (PathTokens.ExpandOrNull(entry.Path) is not { } expanded) return null;

        try
        {
            string native = expanded.Replace('/', Path.DirectorySeparatorChar);
            string abs = Path.IsPathRooted(native)
                ? native
                : Path.GetFullPath(Path.Combine(workspaceRoot, native));

            if (File.Exists(abs)) return WorkspaceRootFinder.Normalize(Path.GetDirectoryName(abs));
            if (Directory.Exists(abs) && File.Exists(Path.Combine(abs, WorkspacePersistence.FileName)))
                return WorkspaceRootFinder.Normalize(abs);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
                                   or PathTooLongException or NotSupportedException)
        { return null; }
    }

    /// <summary>Two identities naming one commit. Git accepts an abbreviation, so this compares on the
    /// shorter of the two rather than for equality — a pin a designer shortened by hand still names
    /// what it names.</summary>
    private static bool SameCommit(string a, string b)
    {
        int n = Math.Min(a.Length, b.Length);
        return n >= 7 && string.Equals(a[..n], b[..n], StringComparison.OrdinalIgnoreCase);
    }

    private static Diagnostic NoSuchAlias(string alias) => Diagnostic.Create(
        "revision.pin.no-such-reference",
        DiagnosticSeverity.Error,
        "No workspace reference named \"{alias}\" is recorded here.",
        ("alias", alias));

    /// <summary>Drops the resolvers that read the pin. The same moment RC-2's editability flag drops
    /// them, and for the same reason: a <c>.cws</c> rewritten changes where every alias resolves.</summary>
    private static void Invalidate()
    {
        ExternalCellRef.InvalidateCache();
        ReferencedWorkspacePolicy.InvalidateCache();
        // The expanded-content memo keys on (library, version) rather than on the alias, so a pin that
        // MOVED leaves a perfectly valid entry for the version it moved away from. Dropped whole here
        // rather than reasoned about: this runs on a deliberate gesture, not per render.
        PinnedContent.InvalidateCache();
    }
}

/// <summary>What changing a pin did.</summary>
/// <param name="Ok">Whether the operation was allowed and completed.</param>
/// <param name="Written">
/// Whether the <c>.cws</c> was actually rewritten. False on "it already says that", which is a success
/// with nothing to record — and the distinction matters, because a write is what lands in the local
/// history (R-rc9-13).
/// </param>
/// <param name="Pin">The identity now recorded, or null when the reference is unpinned.</param>
/// <param name="Diagnostic">The sentence for what happened.</param>
public sealed record PinChange(bool Ok, bool Written, string? Pin, Diagnostic Diagnostic)
{
    public static PinChange Refused(Diagnostic d) => new(false, false, null, d);
}
