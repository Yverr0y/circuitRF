using System.IO.Compression;
using CircuitRF.Design.Workspace;
using System.Security.Cryptography;
using System.Text;

namespace CircuitRF.Design.Revision;

/// <summary>
/// <b>What a pinned reference actually resolves to</b> — the referenced workspace's content as it was
/// at one commit, laid out on disk so every existing resolver reads it exactly as it reads an ordinary
/// workspace (RC-9 §3, §4; R-rc9-8, R-rc9-15, R-rc9-16).
///
/// <para><b>Why materialising is the only honest answer.</b> A pin that merely CHECKED that the other
/// workspace happens to be sitting on the right commit would be a warning, not a pin: the moment the
/// librarian checks out anything else, the design silently resolves against content it was never
/// verified against — which is the exact failure §7A.4 is written to prevent, arriving through the
/// feature meant to prevent it. R-rc9-15 states the consequence in the designer's own words: with a
/// pinned reference, editing a cell in the library and coming back <b>deliberately does not</b> show
/// the new cell. That sentence is only true if the pinned bytes are what is read.</para>
///
/// <para><b>It is still not a copy</b> (R-rc9-16). Nothing is copied into either workspace: the cache
/// lives in the per-user state directory, is keyed by the library's location and the commit, is
/// rebuildable from the library's own repository at any time, and is versioned by nobody. A designer
/// who needs the CONTENT to travel with the design has the workspace archive, which is a different
/// tool for a different problem. What is recorded in the <c>.cws</c> remains an identity.</para>
///
/// <para><b>Nothing is written into the referenced workspace</b> (R-rc0-5, §7A.1). <c>git archive</c>
/// and <c>cat-file</c> are reads. A <c>git worktree add</c> would have been the shorter route and is
/// not taken: it writes administrative files into a repository that is not the open workspace's own,
/// which is the one rule every brief from RC-3 onward is written against.</para>
///
/// <para><b>Through a zip, not a tar pipe.</b> <c>git archive --format=zip</c> is written to a file and
/// expanded by <c>System.IO.Compression</c>, which is in the framework. The alternatives both fail on a
/// real design: piping <c>cat-file --batch</c> through this project's own subprocess wrapper collects
/// standard output as TEXT, so a bitmap referenced by a <c>.clay</c> or a <c>.csym</c> — the very case
/// the archive work found broken and R-rc9-3 asks to be re-checked — would arrive corrupted and open
/// without complaint; and <c>--format=tar</c> needs an extractor this repository does not ship on
/// Windows.</para>
/// </summary>
public static class PinnedContent
{
    /// <summary>The per-user directory the cache lives in. Not in either workspace, not versioned, and
    /// safe to delete at any time — the next resolution rebuilds it.</summary>
    public static string CacheRoot => UserStateDirectory.SubDir("pinned");

    /// <summary>
    /// Written last, after everything has been expanded. Its presence is what distinguishes a complete
    /// cache entry from one whose extraction was interrupted — <b>a half-expanded workspace that
    /// resolved would be worse than none</b>, because it would resolve some cells and not others with
    /// nothing saying why.
    /// </summary>
    private const string CompleteMarker = ".crf-pin-complete";

    /// <summary>
    /// Whether <paramref name="commitId"/> is a commit that exists in <paramref name="libraryGit"/>'s
    /// repository. A pin whose identity is not there cannot be honoured, and that is
    /// <see cref="SharingMessages.BecauseThatVersionIsNotInItsHistory"/> rather than a silent fall-back.
    /// </summary>
    public static bool CommitExists(GitCommand libraryGit, string commitId)
    {
        if (string.IsNullOrWhiteSpace(commitId)) return false;
        var r = libraryGit.Run(["cat-file", "-e", commitId + "^{commit}"], new GitRunOptions(ReadOnly: true));
        return r.Ok;
    }

    /// <summary>
    /// The full commit identity of the newest version on the referenced workspace's own line of work,
    /// or null when it keeps no history at all.
    /// </summary>
    public static string? NewestCommit(GitCommand libraryGit)
    {
        var r = libraryGit.Run(["rev-parse", "--verify", "--quiet", "HEAD"],
                               new GitRunOptions(ReadOnly: true));
        return r.Ok && r.Line.Length >= 7 ? r.Line : null;
    }

    /// <summary>
    /// The directory holding <paramref name="commitId"/>'s content, expanding it first if it is not
    /// already cached. Null with <paramref name="why"/> set when the pin cannot be honoured.
    ///
    /// <para><b>The common path is one directory-exists check.</b> Resolution asks this once per alias
    /// per cache generation (<c>ExternalCellRef</c>'s alias table is memoised on the same terms as the
    /// workspace walk-up), so an already-expanded pin costs a stat and nothing else. Only the first
    /// resolution after a pin changes runs git.</para>
    /// </summary>
    public static string? Materialise(
        GitCommand libraryGit, string commitId, out string? why)
    {
        why = null;

        string key = Key(libraryGit.WorkspaceRoot, commitId);
        lock (_memoGate)
            if (_memo.TryGetValue(key, out string? cached)) return cached;

        string? answer = Resolve(libraryGit, commitId, out why);

        lock (_memoGate) _memo[key] = answer;
        return answer;
    }

    private static string? Resolve(GitCommand libraryGit, string commitId, out string? why)
    {
        why = null;

        if (!CommitExists(libraryGit, commitId))
        {
            why = SharingMessages.BecauseThatVersionIsNotInItsHistory;
            return null;
        }

        string dir = DirectoryFor(libraryGit.WorkspaceRoot, commitId);

        try
        {
            if (File.Exists(Path.Combine(dir, CompleteMarker))) return dir;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

        return Expand(libraryGit, commitId, dir, out why);
    }

    // ── The in-process memo, and why it outlives the alias table ──────────────────────────────────
    //
    // MEASURED, not assumed (RC-9's completion write-up): verifying five pinned libraries costs about
    // 45 ms each — one git process per alias — and the alias table above this is dropped on ordinary
    // editing events, a symbol-editor save among them. Without a memo of its own, a design with a
    // handful of pinned libraries would pay a fifth of a second of subprocess starts every time
    // somebody saved a symbol, for an answer that had not changed.
    //
    // IT IS SAFE TO OUTLIVE THAT TABLE BECAUSE IT ANSWERS A DIFFERENT QUESTION. The alias table
    // answers "where does this alias point", which changes whenever a .cws is written. This answers
    // "is this exact, immutable commit present in that repository, and where is it expanded" — which
    // changes only when a pin is rewritten or somebody rewrites the library's history. The first is
    // dropped here explicitly; the second is what WorkspacePins.Survey re-checks on open and after
    // every pin change, and it drops the entry when it finds one that can no longer be honoured.

    private static readonly Dictionary<string, string?> _memo = new(StringComparer.Ordinal);
    private static readonly Lock _memoGate = new();

    private static string Key(string libraryRoot, string commitId)
        => WorkspaceRootFinder.Normalize(libraryRoot) + "\u0000" + commitId;

    /// <summary>
    /// The memoised answer for one pair, without touching git at all — <b>the first thing resolution
    /// asks</b>, because everything below it starts a subprocess, including the <c>--show-prefix</c>
    /// that establishes the library is a repository. True with a null <paramref name="directory"/> is a
    /// remembered refusal and is as much an answer as a remembered hit.
    /// </summary>
    public static bool TryMemoised(string libraryRoot, string commitId, out string? directory)
    {
        lock (_memoGate) return _memo.TryGetValue(Key(libraryRoot, commitId), out directory);
    }

    /// <summary>Drops the memoised answer for one (library, version) pair, or all of them.</summary>
    public static void InvalidateCache(string? libraryRoot = null, string? commitId = null)
    {
        lock (_memoGate)
        {
            if (libraryRoot is null || commitId is null) _memo.Clear();
            else _memo.Remove(Key(libraryRoot, commitId));
        }
    }

    /// <summary>
    /// Where one (library, version) pair is expanded to.
    ///
    /// <para><b>Keyed on the library's LOCATION as well as the commit</b>, because two workspaces on
    /// one machine can perfectly well hold the same commit of the same upstream library and there is
    /// no reason for one to write into the other's entry. The location is hashed rather than embedded:
    /// a real path can be longer than a Windows path allows once a cell folder's own depth is added on
    /// the end of it, and this directory is a cache nobody reads by name.</para>
    /// </summary>
    internal static string DirectoryFor(string libraryRoot, string commitId)
    {
        string key = WorkspaceRootFinder.Normalize(libraryRoot);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        string tag  = Convert.ToHexStringLower(hash)[..16];

        string shortId = commitId.Length > 16 ? commitId[..16] : commitId;
        return Path.Combine(CacheRoot, tag + "-" + shortId);
    }

    private static string? Expand(GitCommand libraryGit, string commitId, string dir, out string? why)
    {
        why = null;
        string staging = dir + ".crf-tmp-" + Guid.NewGuid().ToString("N")[..8];
        string zip     = staging + ".zip";

        try
        {
            Directory.CreateDirectory(CacheRoot);

            // A READ of the referenced workspace's repository, written to a file of circuitRF's own.
            // `--output` rather than a redirected stdout: the archive is binary and this project's
            // subprocess wrapper collects standard output as text.
            var made = libraryGit.Run(
                ["archive", "--format=zip", "--output", zip, commitId],
                new GitRunOptions(ReadOnly: true));

            if (!made.Ok)
            {
                why = SharingMessages.BecauseThatVersionIsNotInItsHistory;
                return null;
            }

            Directory.CreateDirectory(staging);
            ZipFile.ExtractToDirectory(zip, staging, overwriteFiles: true);

            File.WriteAllText(Path.Combine(staging, CompleteMarker), commitId + "\n");

            // The marker is inside the staged directory, so the move is what publishes a COMPLETE
            // entry — there is no window in which a half-expanded directory carries a complete mark.
            if (Directory.Exists(dir))
            {
                // Another process expanded the same pair while this one was working. Theirs is as good
                // as ours; drop ours rather than racing to replace a directory something may be
                // reading out of right now.
                return dir;
            }

            Directory.Move(staging, dir);
            return dir;
        }
        catch (IOException) when (Directory.Exists(dir))
        {
            // Same race, reported by the filesystem instead.
            return dir;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
                                   or NotSupportedException)
        {
            why = e.Message;
            return null;
        }
        finally
        {
            TryDelete(zip);
            TryDeleteDirectory(staging);
        }
    }

    /// <summary>
    /// Forgets every expanded copy of one library, or of everything when
    /// <paramref name="libraryRoot"/> is null. <b>Nothing calls this automatically</b> — it is the
    /// operation behind a "reclaim" gesture, and dropping a cache while a workspace is open would make
    /// its next resolution silently slow rather than wrong.
    /// </summary>
    public static void Forget(string? libraryRoot = null)
    {
        string prefix = libraryRoot is null
            ? ""
            : Convert.ToHexStringLower(
                  SHA256.HashData(Encoding.UTF8.GetBytes(WorkspaceRootFinder.Normalize(libraryRoot))))[..16];

        InvalidateCache();

        try
        {
            if (!Directory.Exists(CacheRoot)) return;
            foreach (string dir in Directory.GetDirectories(CacheRoot))
                if (prefix.Length == 0 || Path.GetFileName(dir).StartsWith(prefix, StringComparison.Ordinal))
                    TryDeleteDirectory(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
