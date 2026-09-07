using System.Collections.Generic;

namespace CircuitRF.Design.Revision;

/// <summary>Who a commit is by. Author and committer are the same person here; git resolves the two
/// separately and supplying only one leaves the other to config or to a guess at
/// <c>user@hostname</c>.</summary>
/// <param name="Name">A display name.</param>
/// <param name="Email">An address. Git does not validate it and neither does this.</param>
/// <param name="Source">Where it came from, for the write-up and for RC-4's status line.</param>
public sealed record GitIdentity(string Name, string Email, string Source);

/// <summary>
/// The environment every circuitRF git subprocess starts with (<c>revision-control.md</c> §4.5,
/// R-rc3-1a). <b>Each entry prevents one specific silent failure</b>, and the reason belongs beside
/// the value, because a configuration written down nowhere is a configuration the next person removes
/// as clutter.
/// </summary>
public static class GitEnvironment
{
    /// <summary>
    /// Applies the invocation environment to a <c>ProcessStartInfo</c>'s variables.
    ///
    /// <para><b><c>GIT_TERMINAL_PROMPT=0</c></b> — without it an operation needing credentials (RC-9's
    /// fetch or push against a private repository) BLOCKS FOREVER on a prompt written to a terminal
    /// that does not exist. A hang is the worst failure mode available, because it is the only one
    /// with no message, no exit code and no end. It is set here, in the brief that starts the process,
    /// rather than in the brief that reaches a network: the environment belongs to the type that
    /// starts the process.</para>
    ///
    /// <para><b>The locale is pinned to <c>C</c></b> — <see cref="GitFailures"/> keys on exit codes and
    /// stable tokens, and where it must read git's own words (<c>safe.directory</c>'s "dubious
    /// ownership", the credential refusal), a localised git would not produce them. That failure
    /// appears only on someone else's machine. <c>LC_ALL</c> wins over everything on POSIX;
    /// <c>LANGUAGE</c> is cleared because gettext consults it ahead of <c>LC_ALL</c>.</para>
    ///
    /// <para><b>Nothing here is a credential and nothing supplies one</b> (§9.1). circuitRF adds no
    /// <c>GIT_ASKPASS</c> and no <c>SSH_ASKPASS</c> of its own, and it removes neither: a helper the
    /// user installed opening its own window is the user's configured setup doing its job, not a
    /// hang.</para>
    /// </summary>
    public static void Apply(IDictionary<string, string?> environment, GitIdentity? identity = null)
    {
        environment["GIT_TERMINAL_PROMPT"] = "0";

        environment["LC_ALL"]   = "C";
        environment["LANG"]     = "C";
        environment["LANGUAGE"] = "";

        // Belt and braces with the --no-pager flag: a pager started on a redirected stdout waits for a
        // terminal that is not there, which is the hang above by another route.
        environment["GIT_PAGER"] = "cat";
        environment["PAGER"]     = "cat";

        // AUTHOR AND COMMITTER, both, always — see the type's own remarks and §4.4. Nothing is written
        // to a git config file; where circuitRF's preference names nobody, these are simply absent and
        // git's ordinary resolution applies.
        if (identity is not null)
        {
            environment["GIT_AUTHOR_NAME"]     = identity.Name;
            environment["GIT_AUTHOR_EMAIL"]    = identity.Email;
            environment["GIT_COMMITTER_NAME"]  = identity.Name;
            environment["GIT_COMMITTER_EMAIL"] = identity.Email;
        }
    }

    /// <summary>
    /// The <c>-c key=value</c> arguments that precede every subcommand. These are the settings that are
    /// a property of the PERSON or of CIRCUITRF'S OWN BEHAVIOUR rather than of the repository, which is
    /// §4.5's dividing line — so they are supplied per invocation and written to no config file.
    /// </summary>
    /// <param name="workspaceRoot">
    /// The open workspace's root, named as <c>safe.directory</c> (§4.7, R-rc3-1a).
    ///
    /// <para>Git 2.35.2 and later refuse to operate on a repository owned by another user, and that is
    /// the ORDINARY state of a workspace on a network share and of an archive someone else extracted.
    /// The only remedy a designer could otherwise be handed is a global config write in git
    /// vocabulary, which §4.4 rules out. <b>The trade is stated rather than hidden:</b> the ownership
    /// check exists because a foreign-owned repository's configuration can name programs to run, and
    /// naming this root trusts a folder the user chose to open and whose contents circuitRF already
    /// loads — the trust the open already expressed. It is narrowed to the one root, <b>never
    /// <c>*</c></b>.</para>
    /// </param>
    public static IEnumerable<string> GlobalArguments(string workspaceRoot)
    {
        yield return "--no-pager";

        yield return "-c";
        yield return "safe.directory=" + workspaceRoot;

        // R-rc3-7a. RC-6 offers ADOPTION of a repository the user created, which may carry a
        // pre-commit hook written for their own workflow. Running someone's hook on a checkpoint they
        // did not initiate is an ambush; blocking on it is worse. `--no-verify` is NOT the mechanism —
        // it skips pre-commit and commit-msg only, and post-commit still fires. An empty hooksPath
        // covers every path, including the plumbing one where `git commit` never runs at all.
        yield return "-c";
        yield return "core.hooksPath=";

        // A designer who signs globally would otherwise have every automatic checkpoint block on a
        // passphrase prompt with no window to appear in. PER INVOCATION and never in the repository's
        // config: this is a statement about circuitRF's commits, not about the repository — that same
        // designer's own commits from a shell in the same folder must still sign, exactly as they
        // configured it.
        yield return "-c";
        yield return "commit.gpgsign=false";
        yield return "-c";
        yield return "tag.gpgsign=false";
    }
}
