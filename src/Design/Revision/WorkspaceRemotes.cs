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

        // --porcelain marks an up-to-date reference with '='; anything else moved.
        bool changed = r.StdOut
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Any(l => l.Length > 0 && l[0] is not ('=' or 'D') && !l.StartsWith("To ", StringComparison.Ordinal)
                                                              && !l.StartsWith("Done", StringComparison.Ordinal));

        return new ExchangeResult(true, remote, changed,
            [changed ? SharingMessages.Sent(remote) : SharingMessages.NothingToSend(remote)]);
    }

    /// <summary>
    /// What the remote-tracking references point at, as one string. Comparing it before and after is
    /// how "did anything arrive" is answered without parsing git's progress output, which is written
    /// for a person and is not stable.
    /// </summary>
    private static string Fingerprint(GitCommand git)
    {
        var r = git.Run(["for-each-ref", "--format=%(refname) %(objectname)", "refs/remotes/"],
                        new GitRunOptions(ReadOnly: true));
        return r.Ok ? r.StdOut.Trim() : "";
    }
}
