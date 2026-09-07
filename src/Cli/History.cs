using CircuitRF.Design.Revision;
using CircuitRF.Design.Workspace;
using CircuitRF.Diagnostics;

namespace CircuitRF.Cli;

/// <summary>
/// <c>circuitrf history checkpoint &lt;workspace&gt;</c> — records the workspace as it stands.
///
/// <para><b>Why this exists in RC-3 at all, when RC-5 owns checkpoints.</b> The governing motive
/// (<c>revision-control.md</c> §1.2) is a floor under an AI-authored edit, and §1.2's agent is
/// <b>out of process</b>: the safety net is worth nothing if it can only be reached from the GUI. So
/// the one thing RC-3 must prove is that a process with no <c>src/Ui</c> in it can take a checkpoint
/// AND be attributed to the identity the Settings tab wrote (R-rc3-1c, gate 22) — on the fresh
/// Windows machine §4.4 describes, git's own resolution names nobody, and rev 4's design refused the
/// checkpoint for exactly the population it exists to protect.</para>
///
/// <para><b>It is the spelling R-rc0-19 already fixed</b> — one verb, <c>history</c>, with nouns.
/// RC-5 adds <c>list</c> and <c>restore</c> and RC-7 adds <c>commit</c>; each calls the
/// <c>src/Design</c> function the GUI's own command calls, exactly as <c>new</c> and <c>import</c>
/// do. This file contains no revision-control logic of its own: argument parsing, refusals, and
/// reporting what the paths are.</para>
///
/// <para><b>No repository is created by accident</b> (R-rc3-8). RC-4's settings and RC-5's first
/// checkpoint decide when one appears; here it is an explicit flag, because a repository turning up
/// in a folder because somebody ran a command once is the surprise §0 forbids.</para>
/// </summary>
internal static class History
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            JsonRun.Report(CliDiagnostics.HistoryNounRequired());
            return Usage();
        }

        string noun = args[0].ToLowerInvariant();
        JsonRun.Verb = "history " + noun;

        if (noun != "checkpoint")
        {
            JsonRun.Report(CliDiagnostics.HistoryUnknownNoun(noun));
            return Usage();
        }

        return Checkpoint(args[1..]);
    }

    private static int Checkpoint(string[] args)
    {
        string? workspace = null;
        string? reference = null;
        string  message   = "circuitRF checkpoint";
        bool    create    = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ref"     when i + 1 < args.Length: reference = args[++i]; break;
                case "--message" when i + 1 < args.Length: message   = args[++i]; break;
                case "--create-repository":               create    = true;      break;
                default:
                    if (args[i].StartsWith('-'))
                        return JsonRun.Fail(CliDiagnostics.RunUnknownOption("history checkpoint", args[i]));
                    if (workspace is not null)
                        return JsonRun.Fail(CliDiagnostics.RunUnknownOption("history checkpoint", args[i]));
                    workspace = args[i];
                    break;
            }
        }

        if (workspace is null)
        {
            JsonRun.Report(CliDiagnostics.InputRequired("history checkpoint", "workspace folder"));
            return Usage();
        }

        string root = Path.GetFullPath(workspace);
        if (!File.Exists(Path.Combine(root, WorkspacePersistence.FileName)))
            return JsonRun.Fail(CliDiagnostics.HistoryNotAWorkspace(root));

        // R-rc3-3's silence, expressed as a refusal rather than as nothing at all: a caller who TYPED
        // this command asked, so the absence is reported. Nothing automatic ever reaches this path.
        if (GitCommand.For(root) is not { } git)
            return JsonRun.Fail(CliDiagnostics.HistoryNoGit());

        if (create && !git.IsRepositoryRoot())
        {
            var made = GitRepository.Create(git, RevisionManagement.Created);
            if (!made.Ok) return JsonRun.Fail(made.Diagnostic!);
        }

        if (!git.IsRepositoryRoot())
            return JsonRun.Fail(CliDiagnostics.HistoryNoRepository(root));

        // R-rc3-11a: a workspace that predates this feature gets circuitRF's policy files the moment
        // it has a repository, not only when it is created.
        WorkspacePolicyFiles.Ensure(root);

        reference ??= "refs/crf/checkpoints/cli";

        var result = GitCheckpoint.Record(git, reference, message.TrimEnd() + "\n",
                                          GitCheckpoint.TreeOf(git, reference));

        foreach (string excluded in result.ExcludedRepositories)
            JsonRun.Report(GitFailures.NestedRepositoryExcluded(excluded));

        if (result.Diagnostic is { Severity: DiagnosticSeverity.Error } failure)
            return JsonRun.Fail(failure);

        if (!result.Recorded)
        {
            JsonRun.Report(result.Diagnostic ?? GitFailures.NothingToRecord());
            Console.WriteLine("nothing changed");
            return 0;
        }

        // The commit id IS the result, on stdout, the way every authoring verb's paths are.
        Console.WriteLine(result.CommitId);
        return 0;
    }

    private static int Usage()
    {
        Console.Error.WriteLine(
            "usage: circuitrf history checkpoint <workspace> [--ref <name>] [--message <text>] "
          + "[--create-repository]");
        return 2;
    }
}
