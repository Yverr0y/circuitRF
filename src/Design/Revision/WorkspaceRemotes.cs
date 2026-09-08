using CircuitRF.Diagnostics;

namespace CircuitRF.Design.Revision;

/// <summary>What a fetch or a send did.</summary>
/// <param name="Ok">Whether the exchange completed. False carries a translated failure.</param>
/// <param name="Remote">What the other copy is called, for the sentence. Empty when there is none.</param>
/// <param name="Changed">Whether anything actually moved.</param>
/// <param name="Diagnostics">Everything worth saying, in order.</param>
public sealed record ExchangeResult(
    bool Ok, string Remote, bool Changed, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// RC-9 §2: <b>fetch and send</b> (<c>revision-control.md</c> §9, §9.1; R-rc9-6, R-rc9-7, R-rc9-7a,
/// R-rc9-7b).
///
/// <para><b>Both are explicit user actions and nothing here is ever automatic</b> (R-rc9-6). Nothing
/// in this series contacts a network without being asked, and gate 10 source-scans the open and
/// checkpoint paths for a call into this type. An automatic fetch would silently change what a design
/// resolves against, which is precisely the failure §7A.4 is written to prevent — the librarian
/// publishes whenever they like and <b>nothing in any designer's workspace changes</b> until that
/// designer says so.</para>
///
/// <para><b>A fetch brings things in and applies nothing.</b> It does not touch a single file in the
/// working tree, so it can never surprise an open document, and there is no merge anywhere in this
/// series (§6.1, R-rc0-4): the five design formats are marked unmergeable and resolution is whole-file,
/// pick a side, which RC-7 owns. That is why "fetch" is the only half of the pair that is safe to
/// offer at all.</para>
///
/// <para><b>circuitRF holds no credential and asks for none</b> (§9.1). It supplies whatever git is
/// already configured to supply — the user's credential helper, their SSH agent, whatever their IT
/// department set up — and adds nothing: no prompt, no stored token, no keychain entry. What makes
/// that safe rather than merely small is <c>GIT_TERMINAL_PROMPT=0</c>
/// (<see cref="GitEnvironment"/>): an operation that would have asked <b>refuses</b>, with
/// <see cref="GitFailures.CredentialRequired"/> naming what the remote wanted. Without it the same
/// minimalism produces a subprocess blocked on an invisible prompt — no message, no exit code, and a
/// workspace close that never completes.</para>
///
/// <para><b>That variable reaches git's own prompts and nothing else</b> (§9.1, rev 5). An SSH key
/// with a passphrase and no agent prompts through <c>ssh</c>, which does not read it; with no terminal
/// <c>ssh</c> fails rather than hangs, which is the wanted outcome — and it has no terminal only
/// because <see cref="GitCommand"/> gives the subprocess none. A credential helper that opens its own
/// window, the ordinary Windows case, is the user's configured setup doing what they installed it to
/// do, and §9's posture is to let it. <b>What must never be added here is a terminal, an
/// <c>SSH_ASKPASS</c>, or a stored answer of circuitRF's own.</b></para>
///
/// <para><b>The bound is inactivity, not wall-clock</b> (R-rc3-1a): a slow exchange with a large
/// library is not a hang, and a fixed limit would report it as one.</para>
/// </summary>
public static class WorkspaceRemotes
{
    /// <summary>
    /// The other copy this workspace exchanges with, or null when there is none. <b>Whatever git
    /// records</b> — a clone's is <c>origin</c>, and a workspace whose history circuitRF started has
    /// no remote at all, which is the ordinary case.
    /// </summary>
    public static string? OtherCopy(GitCommand git)
    {
        var r = git.Run(["remote"], new GitRunOptions(ReadOnly: true));
        if (!r.Ok) return null;

        foreach (string line in r.StdOut.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            string name = line.Trim();
            if (name.Length > 0) return name;
        }
        return null;
    }

    /// <summary>Whether an exchange is possible at all — what the two menu items key their enabled
    /// state on, so a workspace with no other copy shows them greyed rather than failing when
    /// pressed.</summary>
    public static bool HasOtherCopy(GitCommand git) => OtherCopy(git) is not null;

    /// <summary>
    /// <b>Where a fetch put what it brought in</b> — the remote-tracking reference this workspace's own
    /// line of work corresponds to, or null when there is none.
    ///
    /// <para>This exists because a fetch updates <c>refs/remotes/…</c> and moves nothing else. Without
    /// it, everything that reads the history reads <c>HEAD</c> and a Pull is a download with no visible
    /// result — the versions are on the machine and the panel that exists to list them cannot see them
    /// (R-rc9-6's own promise that <i>what arrives shows up in the versions list</i>).</para>
    ///
    /// <para><b>Three ways to ask, in order of how much they know.</b> <c>@{upstream}</c> is what a
    /// clone configures and is the exact answer when it is there. A remote added by hand has no
    /// upstream configured, so the remote's own default branch is asked next, and the same-named branch
    /// last. Returning null rather than guessing at <c>refs/remotes/&lt;remote&gt;/*</c> is deliberate:
    /// circuitRF has no branch concept, so a repository with several remote branches is one this
    /// feature has no opinion about, and picking one would be inventing an answer.</para>
    /// </summary>
    public static string? IncomingRef(GitCommand git)
    {
        if (Resolves(git, "@{upstream}") is { } upstream) return upstream;
        if (OtherCopy(git) is not { } remote) return null;

        if (Resolves(git, $"refs/remotes/{remote}/HEAD") is { } head) return head;

        var branch = git.Run(["symbolic-ref", "--quiet", "--short", "HEAD"],
                             new GitRunOptions(ReadOnly: true));
        return branch.Ok && branch.Line.Length > 0
            ? Resolves(git, $"refs/remotes/{remote}/{branch.Line}")
            : null;
    }

    /// <summary>The full reference name, when it resolves to something. Null otherwise — including for
    /// the ordinary case of a workspace whose history circuitRF started and which has no remote.</summary>
    private static string? Resolves(GitCommand git, string reference)
    {
        var r = git.Run(["rev-parse", "--symbolic-full-name", "--verify", "--quiet", reference],
                        new GitRunOptions(ReadOnly: true));
        return r.Ok && r.Line.Length > 0 ? r.Line : null;
    }

    /// <summary>
    /// Brings in what is new on the other copy. <b>Explicit only</b> (R-rc9-6) — there is no caller
    /// for this on any automatic path and gate 10 asserts there is not.
    /// </summary>
    public static ExchangeResult Fetch(GitCommand git, CancellationToken ct = default)
    {
        if (OtherCopy(git) is not { } remote)
            return new ExchangeResult(false, "", false, [SharingMessages.NoOtherCopy()]);

        string before = Fingerprint(git);

        // --no-tags is NOT passed and neither is a refspec: git's own default is what leaves the
        // checkpoint namespace behind (R-rc9-5a), exactly as the clone's is. --prune is not passed
        // either — dropping a reference on the strength of a remote's absence is a destruction this
        // series does not make automatically (R-rc0-7).
        var r = git.Run(["fetch", "--", remote], new GitRunOptions(Network: true), ct);

        if (r.Cancelled)
            return new ExchangeResult(false, remote, false, [SharingMessages.CloneCancelled()]);

        if (!r.Ok)
            return new ExchangeResult(false, remote, false,
                [GitFailures.Translate(r, SharingMessages.BringingInChanges, git.WorkspaceRoot)]);

        BringInCorrections(git, remote, ct);

        bool changed = !string.Equals(before, Fingerprint(git), StringComparison.Ordinal);
        return new ExchangeResult(true, remote, changed,
            [changed ? SharingMessages.BroughtInChanges(remote)
                     : SharingMessages.NothingNewToBringIn(remote)]);
    }

    /// <summary>
    /// Sends the versions kept here to the other copy.
    ///
    /// <para><b>The restore points do not go</b> (R-rc9-5a): they live under a private namespace, no
    /// refspec here names it, and git's default push takes branches only. That is the same asymmetry a
    /// clone has, in the other direction.</para>
    ///
    /// <para><b><c>--porcelain</c> is always asked for</b> — it is what makes a non-fast-forward a
    /// machine-readable reason rather than a sentence to match on, and
    /// <see cref="GitFailures.NonFastForward"/> is the row it lands in (R-rc9-7). The remedy that
    /// failure offers is whole-file, pick a side (RC-7) — <b>never a merge</b>.</para>
    /// </summary>
    public static ExchangeResult Push(GitCommand git, CancellationToken ct = default)
    {
        if (OtherCopy(git) is not { } remote)
            return new ExchangeResult(false, "", false, [SharingMessages.NoOtherCopy()]);

        var r = git.Run(["push", "--porcelain", "--", remote], new GitRunOptions(Network: true), ct);

        if (r.Cancelled)
            return new ExchangeResult(false, remote, false, [SharingMessages.CloneCancelled()]);

        if (!r.Ok)
            return new ExchangeResult(false, remote, false,
                [GitFailures.Translate(r, SharingMessages.SendingChanges, git.WorkspaceRoot)]);

        SendCorrections(git, remote, ct);

        // --porcelain marks an up-to-date reference with '='; anything else moved.
        bool changed = r.StdOut
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Any(l => l.Length > 0 && l[0] is not ('=' or 'D') && !l.StartsWith("To ", StringComparison.Ordinal)
                                                              && !l.StartsWith("Done", StringComparison.Ordinal));

        return new ExchangeResult(true, remote, changed,
            [changed ? SharingMessages.Sent(remote) : SharingMessages.NothingToSend(remote)]);
    }

    // ── §5.11's corrections travel with the versions they annotate (RC-11 R-rc11-13) ──────────────

    /// <summary>
    /// Brings in the corrections the other copy holds, and <b>a failure here is not a failure of the
    /// fetch</b>.
    ///
    /// <para><b>Why it is a second invocation rather than a second refspec.</b> A command-line refspec
    /// REPLACES git's configured one rather than adding to it, so naming the notes reference in the
    /// fetch above would mean spelling the default out as well — and worse, <b>a non-wildcard refspec
    /// naming a reference the remote does not have is a fatal error</b>. Every workspace in which
    /// nobody has ever written a correction is exactly that case, which is nearly all of them, so the
    /// tidy-looking version would have broken Pull Changes for everybody in order to carry a string
    /// almost nobody has written.</para>
    ///
    /// <para>So: best effort, ignored when it fails, and it only runs after a fetch that already
    /// succeeded. Nothing about a correction is worth reporting a failed exchange for.</para>
    /// </summary>
    private static void BringInCorrections(GitCommand git, string remote, CancellationToken ct)
        => git.Run(["fetch", "--", remote, VersionCorrections.NotesRefspec],
                   new GitRunOptions(Network: true), ct);

    /// <summary>
    /// Sends the corrections alongside the versions they annotate (§9, R-rc11-13).
    ///
    /// <para><b>Only when there is one to send.</b> A push naming a source reference that does not
    /// exist locally is a refusal, and a workspace with no corrections in it is the ordinary state —
    /// so the reference is resolved first and the invocation skipped rather than made and forgiven.
    /// This is also what keeps a send on a workspace with no corrections at exactly the cost it had
    /// before RC-11.</para>
    /// </summary>
    private static void SendCorrections(GitCommand git, string remote, CancellationToken ct)
    {
        var exists = git.Run(["rev-parse", "--verify", "--quiet", VersionCorrections.NotesRef],
                             new GitRunOptions(ReadOnly: true));
        if (!exists.Ok || exists.Line.Length == 0) return;

        git.Run(["push", "--porcelain", "--", remote, VersionCorrections.NotesRefspec],
                new GitRunOptions(Network: true), ct);
    }

    /// <summary>
    /// What the remote-tracking references point at, as one string. Comparing it before and after is
    /// how "did anything arrive" is answered without parsing git's progress output, which is written
    /// for a person and is not stable.
    ///
    /// <para><b>The notes reference is in it</b> (RC-11): a fetch that brought in nothing but a
    /// colleague's correction did change something, and reporting "nothing new" for it would be the
    /// silent-download defect R-rc9-6 exists to prevent, arriving by a different route.</para>
    /// </summary>
    private static string Fingerprint(GitCommand git)
    {
        var r = git.Run(["for-each-ref", "--format=%(refname) %(objectname)",
                         "refs/remotes/", VersionCorrections.NotesRef],
                        new GitRunOptions(ReadOnly: true));
        return r.Ok ? r.StdOut.Trim() : "";
    }
}
