using CircuitRF.Diagnostics;

namespace CircuitRF.Design.Revision;

/// <summary>
/// Git's diagnostics, translated into circuitRF's vocabulary — a <b>bounded</b> set of recognised
/// failures, each mapped to one sentence (<c>revision-control.md</c> §4.2, R-rc3-4).
///
/// <para><b>What arm's length costs, and who pays it.</b> Git's messages are written for people who
/// already understand git; shelling out means inheriting them. An RF designer who has never used git
/// must never be shown git's wording for a condition circuitRF created — so every failure this
/// project can foresee has a row here, and <b>anything unrecognised is carried verbatim</b>
/// (<see cref="Unrecognised"/>, R-rc3-5), never swallowed and never replaced by a generic sentence.
/// That is the same posture the run services already take with an engine error.</para>
///
/// <para><b>They are <see cref="Diagnostic"/>s, not sentences</b> (R-rc3-6). The CLI and the Messages
/// panel render the same failure from one source, the ids are what dedup and filtering key on, and a
/// translated failure is never flattened into English on the way out.</para>
///
/// <para><b>How each one is recognised.</b> Where a structural signal exists it is used, because a
/// substring match on English breaks the day someone rewords it: identity is checked BEFORE the
/// invocation, "nothing to commit" is a tree comparison, a stale lock is a FILE that exists, an
/// embedded repository is a directory walk, and "git is not at that path" is a process that never
/// started. Two rows have no structural signal at all — <c>safe.directory</c> and the credential
/// refusal — and for those the match is on a config key and on git's own fixed phrase, <b>which is
/// exactly why <see cref="GitEnvironment"/> pins the locale to <c>C</c></b>: unpinned, the token these
/// look for appears only on an English machine, and the failure shows up on somebody else's.</para>
/// </summary>
public static class GitFailures
{
    // ── The table (R-rc3-4) ───────────────────────────────────────────────────────────────────────

    /// <summary>Nobody to commit as. §4.4: the sentence says what must be filled in, and where.</summary>
    public static Diagnostic NoIdentity() => new(
        "revision.identity.missing",
        DiagnosticSeverity.Error,
        "circuitRF does not know who to record this change as, so nothing was recorded. "
      + "Fill in your name and email in Settings ▸ Revision Control.");

    /// <summary>Not a failure — the state is already recorded. Info, and it says so.</summary>
    public static Diagnostic NothingToRecord() => new(
        "revision.nothing-to-record",
        DiagnosticSeverity.Info,
        "Nothing has changed since the last restore point, so none was taken.");

    /// <summary>
    /// A lock file left behind by a process that died. The sentence says WHAT TO REMOVE and that it is
    /// safe to — which is the whole content of the message, and the part git's own wording buries.
    /// </summary>
    public static Diagnostic StaleLock(string lockPath) => Diagnostic.Create(
        "revision.lock.left-behind",
        DiagnosticSeverity.Error,
        "A previous operation did not finish and left the file '{lockPath}' behind, which is stopping "
      + "this one. Nothing is using it. Deleting that one file is safe and lets this proceed.",
        ("lockPath", lockPath));

    /// <summary>
    /// A directory inside the workspace is itself a repository. <b>The sentence says a nested
    /// repository was found and excluded, and nothing about gitlinks</b> (RC-6 R-rc6-7): handing such a
    /// directory to <c>git add</c> records a pointer to that repository's current commit, with a
    /// warning nobody reads.
    /// </summary>
    public static Diagnostic NestedRepositoryExcluded(string relativePath) => Diagnostic.Create(
        "revision.nested-repository.excluded",
        DiagnosticSeverity.Warning,
        "'{path}' keeps a history of its own, so circuitRF left it out of this workspace's history. "
      + "Its own contents are unaffected.",
        ("path", relativePath));

    /// <summary>The other side moved (RC-9).</summary>
    public static Diagnostic NonFastForward(string remote) => Diagnostic.Create(
        "revision.push.other-side-moved",
        DiagnosticSeverity.Error,
        "'{remote}' has changes that are not here yet, so nothing was sent. Bring them in first, then "
      + "send again.",
        ("remote", remote));

    /// <summary>Something is at the configured path and it is not a git. Names the path that was
    /// tried, because "somewhere you picked" is not something a caller can act on.</summary>
    public static Diagnostic NotGit(string path, string what) => Diagnostic.Create(
        "revision.git.not-usable",
        DiagnosticSeverity.Error,
        "circuitRF could not use '{path}' as git: {what}. Correct the path in "
      + "Settings ▸ Revision Control, or clear it to use the one on the system path.",
        ("path", path), ("what", what));

    /// <summary>
    /// <b>The entry most likely to be left out and most likely to be hit</b> (§4.7). Git 2.35.2 and
    /// later refuse to operate on a repository owned by another user, and that is the ORDINARY state
    /// of a workspace on a network share and of an archive someone else extracted. RF workspaces live
    /// on shares.
    ///
    /// <para><b>On a git at or above the floor this is never reached</b>, because every invocation
    /// names the root as <c>safe.directory</c> (<see cref="GitEnvironment.GlobalArguments"/>). The row
    /// exists for a git that predates the option — and the sentence says a newer git would not ask,
    /// because that is the remedy, and the alternative remedy is a global config write in git
    /// vocabulary that §4.4 rules out.</para>
    /// </summary>
    public static Diagnostic ForeignOwnership(string workspaceRoot) => Diagnostic.Create(
        "revision.ownership.foreign",
        DiagnosticSeverity.Error,
        "'{root}' belongs to a different account on this machine, and the installed git refuses to "
      + "work in a folder it does not own. A newer git would not ask. Until then circuitRF cannot keep "
      + "a history for this workspace.",
        ("root", workspaceRoot));

    /// <summary>
    /// R-rc3-3a: a git below the floor is ABSENT, not broken, so this never reaches a designer through
    /// an automatic path. It exists for RC-4's Detect, where the user asked.
    /// </summary>
    public static Diagnostic TooOld(string path, Version found, Version required) => Diagnostic.Create(
        "revision.git.too-old",
        DiagnosticSeverity.Info,
        "The git at '{path}' is version {found}. circuitRF needs {required} or newer, so it is not "
      + "using it.",
        ("path", path), ("found", found.ToString()), ("required", required.ToString()));

    /// <summary>
    /// What the remote wanted — <b>never a hang</b> (R-rc3-1a). circuitRF holds no credential and asks
    /// for none (§9.1); it uses whatever git is already configured to use and refuses rather than asks.
    /// </summary>
    public static Diagnostic CredentialRequired(string remote) => Diagnostic.Create(
        "revision.credentials.required",
        DiagnosticSeverity.Error,
        "'{remote}' asked for a sign-in that this machine's git could not supply, so nothing was sent "
      + "or fetched. circuitRF uses whatever sign-in git is already set up with and stores none of its "
      + "own.",
        ("remote", remote));

    /// <summary>A call that hit its bound and was stopped. Named separately from a git error because
    /// nothing went wrong with the repository and the operation can simply be tried again.</summary>
    public static Diagnostic TimedOut(string what) => Diagnostic.Create(
        "revision.timed-out",
        DiagnosticSeverity.Error,
        "'{what}' was taking too long and was stopped. The workspace is unchanged.",
        ("what", what));

    /// <summary>
    /// <b>Everything else, verbatim</b> (R-rc3-5). The raw output is always available and never
    /// foregrounded; it is not replaced by a generic sentence, because a generic sentence is how a
    /// failure nobody has seen before becomes a failure nobody can diagnose.
    /// </summary>
    public static Diagnostic Unrecognised(string what, string rawOutput) => Diagnostic.Create(
        "revision.git.unrecognised",
        DiagnosticSeverity.Error,
        "'{what}' did not succeed. git said: {raw}",
        ("what", what), ("raw", rawOutput.Trim()));

    // ── Recognition ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Turns a failed <see cref="GitResult"/> into the right row, or into <see cref="Unrecognised"/>.
    ///
    /// <para><paramref name="what"/> names the operation in circuitRF's words ("taking a restore
    /// point"), never git's.</para>
    /// </summary>
    public static Diagnostic Translate(GitResult result, string what, string workspaceRoot)
    {
        if (result.Ok) return NothingToRecord();     // callers should not ask, but never throw here

        // Structural first, and in cost order.
        if (!result.Started) return NotGit(workspaceRoot, result.StdErr.Trim());
        if (result.TimedOut) return TimedOut(what);

        string text = result.StdErr + "\n" + result.StdOut;

        // A lock file that EXISTS is the signal; the message merely tells us which one to look for.
        if (FindLockPath(text, workspaceRoot) is { } lockPath) return StaleLock(lockPath);

        // Not prose: `safe.directory` is a configuration key, so it is stable under any wording git
        // ever gives this. The locale is pinned to C regardless (GitEnvironment).
        if (text.Contains("safe.directory", StringComparison.Ordinal) ||
            text.Contains("dubious ownership", StringComparison.OrdinalIgnoreCase))
            return ForeignOwnership(workspaceRoot);

        if (text.Contains("terminal prompts disabled", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("could not read Username", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("could not read Password", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase))
            return CredentialRequired(DescribeRemote(text));

        // `git push --porcelain` emits a leading '!' line with a machine-readable reason, which is why
        // RC-9 must always ask for it.
        if (text.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("fetch first", StringComparison.OrdinalIgnoreCase))
            return NonFastForward(DescribeRemote(text));

        if (text.Contains("Please tell me who you are", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("unable to auto-detect email address", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("empty ident name", StringComparison.OrdinalIgnoreCase))
            return NoIdentity();

        return Unrecognised(what, text);
    }

    /// <summary>
    /// The lock file git is complaining about, but only if it is <b>actually there</b> — the existence
    /// of the file is the recognisable signature, not the sentence around it.
    ///
    /// <para><b>Two guards, and neither is a path prefix.</b> A prefix comparison against the workspace
    /// root is the obvious rule and it is wrong: git prints its own symlink-resolved view of a path
    /// (<c>/private/var/…</c> where .NET says <c>/var/…</c>, and likewise through any symlinked home or
    /// share), so the check would silently stop recognising the row on exactly the machines this
    /// product runs on. Instead the token must end in <c>.lock</c>, must name a file that EXISTS, and
    /// must sit inside a directory named <c>.git</c> — which is what stops a path in a message being a
    /// reason to go looking anywhere else on the disk.</para>
    /// </summary>
    internal static string? FindLockPath(string text, string workspaceRoot)
    {
        string root = Path.GetFullPath(workspaceRoot);

        foreach (string token in text.Split(['\n', '\r', ' ', '\t', '\'', '"'],
                                            StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = token.TrimEnd(':', '.', ',');
            if (!trimmed.EndsWith(".lock", StringComparison.Ordinal)) continue;

            string candidate;
            try { candidate = Path.GetFullPath(Path.Combine(root, trimmed)); }
            catch (Exception e) when (e is ArgumentException or PathTooLongException or NotSupportedException)
            { continue; }

            if (!IsInsideAGitDirectory(candidate)) continue;
            try { if (File.Exists(candidate)) return candidate; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        return null;
    }

    private static bool IsInsideAGitDirectory(string path)
    {
        foreach (string segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            if (string.Equals(segment, ".git", StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>The remote in git's output, or a neutral word. Never fabricates a URL.</summary>
    private static string DescribeRemote(string text)
    {
        foreach (string token in text.Split(['\n', '\r', ' ', '\t', '\''],
                                            StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) ||
                token.Contains('@') && token.Contains(':'))
                return token.TrimEnd('/', ':', ',', '.');
        }
        return "the other copy";
    }
}
