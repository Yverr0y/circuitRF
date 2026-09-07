using CircuitRF.Design.Revision;
using CircuitRF.Design.Workspace;
using CircuitRF.Diagnostics;
using RfCore.Export;

namespace CircuitRF.Cli;

/// <summary>
/// <c>circuitrf history &lt;checkpoint|list|restore&gt;</c> — <b>every history operation has a
/// headless spelling</b> (<c>docs/design/revision-control.md</c> §5.3d, R-rc5-23; RC-7 adds
/// <c>commit</c>).
///
/// <para><b>Why it exists at all.</b> The governing motive (§1.2) is a floor under an AI-authored
/// edit, and §1.2's agent is <b>out of process</b>: a safety net reachable only from a window is
/// worth nothing to it. rev 4 left every history operation on a window or an agent's tool call, which
/// breaks the rule <c>Authoring.cs</c> exists to hold — <b>an operation that lives only in a view
/// model is not a capability</b>, and <c>serve</c>'s tools are the CLI's verbs by construction.</para>
///
/// <para><b>This file contains no revision-control logic of its own</b>: argument parsing, refusals,
/// and reporting. Each noun calls the <c>src/Design</c> function the GUI's own command calls —
/// <see cref="WorkspaceCheckpoints.Take"/>, <see cref="RestorePoints.List"/>,
/// <see cref="WorkspaceRestore.Restore"/> — so the two cannot diverge, and a source scan proves the
/// view model kept no second copy.</para>
///
/// <para><b>The batch's open and close are deliberately NOT here</b> (§5.3d). They hold session
/// state, and a process that exits after one command cannot; they stay on <c>serve</c>, which is the
/// only surface that can. <c>history checkpoint --intent</c> IS what a batch's open takes, which is
/// why that flag exists on this verb.</para>
///
/// <para><b>No repository is created by accident</b> (R-rc3-8). RC-5's arming decides when one
/// appears; here it is an explicit flag, because a repository turning up in a folder because somebody
/// ran a command once is the surprise §0 forbids.</para>
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

        return noun switch
        {
            "checkpoint" => Checkpoint(args[1..]),
            "list"       => List(args[1..]),
            "restore"    => Restore(args[1..]),
            _            => UnknownNoun(noun),
        };
    }

    private static int UnknownNoun(string noun)
    {
        JsonRun.Report(CliDiagnostics.HistoryUnknownNoun(noun));
        return Usage();
    }

    // ── history checkpoint ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An explicit save-point (R-rc5-4's second boundary), and the same call a batch's open makes.
    ///
    /// <para><b>It is ATTENDED</b> — somebody typed it — so the large-file guard's question belongs
    /// to the caller and nothing is silently left out. <c>--leave-out</c> is how a caller that has
    /// already asked passes the answer.</para>
    /// </summary>
    private static int Checkpoint(string[] args)
    {
        string?      workspace = null;
        string?      intent    = null;
        bool         create       = false;
        bool         unattended   = false;
        bool         includeLarge = false;
        List<string> leaveOut     = [];

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--intent" when i + 1 < args.Length:     intent = args[++i]; break;
                // The RC-3 spelling, kept so a caller that learned it still works.
                case "--message" when i + 1 < args.Length:    intent = args[++i]; break;
                case "--leave-out" when i + 1 < args.Length:  leaveOut.Add(args[++i]); break;
                case "--create-repository":                   create = true; break;
                // R-rc5-15a's other side, for a caller that genuinely has nobody to ask — a scheduled
                // run. Named rather than inferred: "is there a person here" is not something a
                // process can work out about itself.
                case "--unattended":                          unattended = true; break;
                // R-rc5-15's "Include it", as the flag that answers the question below.
                case "--include-large":                       includeLarge = true; break;
                default:
                    if (args[i].StartsWith('-'))
                        return JsonRun.Fail(CliDiagnostics.RunUnknownOption("history checkpoint", args[i]));
                    if (workspace is not null)
                        return JsonRun.Fail(CliDiagnostics.RunUnknownOption("history checkpoint", args[i]));
                    workspace = args[i];
                    break;
            }
        }

        if (Bind(workspace, "history checkpoint", create, out string root, out var git, out int failure) is false)
            return failure;

        // R-rc5-15, and the rule `new` and `import` already hold: anything the GUI's dialog would have
        // ASKED is a refusal naming the flag that answers it, never a guess. §9A.1 decides the
        // direction — including a file is irreversible — so an unanswered one stops the recording
        // rather than being quietly kept.
        if (!unattended && !includeLarge)
        {
            var unanswered = LargeFileGuard
                .Find(git!, RestorePoints.Newest(git!)?.TreeId)
                .Where(f => !leaveOut.Contains(f.RelativePath, StringComparer.Ordinal))
                .ToList();

            if (unanswered.Count > 0)
                return JsonRun.Fail(CliDiagnostics.HistoryLargeFilesNeedAnAnswer(
                    string.Join(", ", unanswered.Select(f => f.RelativePath)), unanswered.Count));
        }

        var outcome = WorkspaceCheckpoints.Take(
            git!, CheckpointOrigin.SavePoint, intent, attended: !unattended, exclusions: leaveOut);

        foreach (var d in outcome.Diagnostics)
            JsonRun.Report(d);

        JsonRun.History = new HistoryReportJson(
            Recorded: outcome.Recorded,
            Point:    outcome.Point is { } p ? ToJson(p) : null);

        if (outcome.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return JsonRun.Finish(1);

        Console.WriteLine(outcome.Recorded
            ? $"kept: {outcome.Point!.Sequence}  {outcome.Point.Label}"
            : "nothing changed");

        return JsonRun.Finish(0);
    }

    // ── history list ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The restore points, newest first. <b>Times and intents</b> — the same surface the panel shows,
    /// and no object identity anywhere in it (R-rc5-2).
    /// </summary>
    private static int List(string[] args)
    {
        string? workspace = null;
        int     limit     = 0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--limit" when i + 1 < args.Length && int.TryParse(args[i + 1], out int n):
                    limit = n; i++; break;
                default:
                    if (args[i].StartsWith('-'))
                        return JsonRun.Fail(CliDiagnostics.RunUnknownOption("history list", args[i]));
                    if (workspace is not null)
                        return JsonRun.Fail(CliDiagnostics.RunUnknownOption("history list", args[i]));
                    workspace = args[i];
                    break;
            }
        }

        if (Bind(workspace, "history list", create: false, out string root, out var git, out int failure) is false)
            return failure;

        // RC-6 R-rc6-4: the entries retention TIDIED AWAY are listed too, marked. Thinning drops a
        // reference and leaves the objects, so those states are still there and still restorable — and
        // a list that omitted them would make "thins, never prunes" invisible to the one caller who
        // cannot open a panel to check.
        var points = RestorePoints.ListIncludingThinned(git!);
        if (limit > 0 && points.Count > limit) points = [.. points.Take(limit)];

        JsonRun.History = new HistoryReportJson(Points: [.. points.Select(ToJson)]);

        if (points.Count == 0)
        {
            JsonRun.Report(CliDiagnostics.HistoryNothingKeptYet(root));
            return JsonRun.Finish(0);
        }

        foreach (var p in points)
            Console.WriteLine(Describe(p));

        return JsonRun.Finish(0);
    }

    /// <summary>One line per entry: the ordering number, the time, the label, and the two things that
    /// change what the entry means — kept, and incomplete.</summary>
    private static string Describe(RestorePoint p)
    {
        string marks = (p.Thinned ? " [tidied away]" : p.Kept ? " [kept]" : "")
                     + (p.IsIncomplete ? $" [incomplete: {string.Join(", ", p.LeftOut)}]" : "");

        return $"{p.Sequence,6}  {p.TakenUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {p.Label}{marks}";
    }

    // ── history restore ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts the workspace back to one entry.
    ///
    /// <para><b>The state being replaced is recorded first, always</b> (R-rc5-12a) —
    /// <see cref="WorkspaceRestore"/> does that and there is no flag here to switch it off, because a
    /// restore that could discard an afternoon's work would be a second cliff rather than a safety
    /// net.</para>
    ///
    /// <para><b>Open documents are the GUI's half</b> (R-rc5-12b). Headless there is no window and no
    /// undo stack, so this leaves R-rc5-14's sentence on the record instead: what a restore covers,
    /// and what it deliberately does not.</para>
    /// </summary>
    private static int Restore(string[] args)
    {
        string? workspace = null;
        long?   sequence  = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--point" when i + 1 < args.Length && long.TryParse(args[i + 1], out long n):
                    sequence = n; i++; break;
                default:
                    if (args[i].StartsWith('-'))
                        return JsonRun.Fail(CliDiagnostics.RunUnknownOption("history restore", args[i]));
                    if (workspace is not null)
                        return JsonRun.Fail(CliDiagnostics.RunUnknownOption("history restore", args[i]));
                    workspace = args[i];
                    break;
            }
        }

        if (Bind(workspace, "history restore", create: false, out string root, out var git, out int failure) is false)
            return failure;

        if (sequence is not { } wanted)
        {
            JsonRun.Report(CliDiagnostics.HistoryRestoreNeedsAPoint());
            return Usage();
        }

        var point = RestorePoints.ListIncludingThinned(git!).FirstOrDefault(p => p.Sequence == wanted);
        if (point is null) return JsonRun.Fail(CliDiagnostics.HistoryNoSuchPoint(wanted));

        // R-rc6-4. A thinned entry is put back in the live list first — one reference update from the
        // journal — so that going back to it leaves a workspace whose history says where it came from.
        if (point.Thinned)
        {
            var back = RestorePoints.RestoreThinned(git!, point);
            if (!back.Ok) return JsonRun.Fail(back.Diagnostic!);
        }

        var result = WorkspaceRestore.Restore(git!, point);
        foreach (var d in result.Diagnostics) JsonRun.Report(d);

        JsonRun.History = new HistoryReportJson(
            Recorded:     result.Ok,
            Point:        ToJson(point),
            FilesWritten: result.FilesWritten,
            FilesRemoved: result.FilesRemoved);

        if (!result.Ok) return JsonRun.Finish(1);

        Console.WriteLine($"restored: {point.Sequence}  {point.Label}");
        Console.WriteLine(RestorePointMessages.RestoreCoversThisWorkspaceOnly);
        Console.WriteLine(RestorePointMessages.RestoreLeavesResultsAlone);
        return JsonRun.Finish(0);
    }

    // ── shared ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the workspace and binds a driver to it, or reports why not.
    ///
    /// <para><b>R-rc3-3's silence, expressed as a refusal rather than as nothing at all</b>: a caller
    /// who TYPED this command asked, so an absent git is reported. Nothing automatic reaches this
    /// path.</para>
    /// </summary>
    private static bool Bind(string? workspace, string verb, bool create,
                             out string root, out GitCommand? git, out int failure)
    {
        root    = "";
        git     = null;
        failure = 0;

        if (workspace is null)
        {
            JsonRun.Report(CliDiagnostics.InputRequired(verb, "workspace folder"));
            failure = Usage();
            return false;
        }

        root = Path.GetFullPath(workspace);
        JsonRun.InputPath = root;

        if (!File.Exists(Path.Combine(root, WorkspacePersistence.FileName)))
        {
            failure = JsonRun.Fail(CliDiagnostics.HistoryNotAWorkspace(root));
            return false;
        }

        if (GitCommand.For(root) is not { } bound)
        {
            failure = JsonRun.Fail(CliDiagnostics.HistoryNoGit());
            return false;
        }

        if (create && !bound.IsRepositoryRoot())
        {
            var made = GitRepository.Create(bound, RevisionManagement.Created);
            if (!made.Ok) { failure = JsonRun.Fail(made.Diagnostic!); return false; }
        }

        if (!bound.IsRepositoryRoot())
        {
            failure = JsonRun.Fail(CliDiagnostics.HistoryNoRepository(root));
            return false;
        }

        // R-rc3-11a: a workspace that predates this feature gets circuitRF's policy files the moment
        // it has a repository, not only when it was created.
        WorkspacePolicyFiles.Ensure(root);

        git = bound;
        return true;
    }

    /// <summary>The wire shape of one entry. <b>Built here and nowhere else</b>, so the terminal line
    /// and the document cannot describe different things.</summary>
    private static RestorePointJson ToJson(RestorePoint p) => new(
        p.Sequence,
        p.TakenUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        CheckpointMessage.Spell(p.Origin),
        p.Label,
        p.Kept,
        p.LeftOut,
        p.Thinned);

    private static int Usage()
    {
        Console.Error.WriteLine(
            "usage: circuitrf history checkpoint <workspace> [--intent <text>] [--leave-out <path>]\n"
          + "                                    [--include-large] [--unattended] [--create-repository]\n"
          + "       circuitrf history list <workspace> [--limit <n>]\n"
          + "       circuitrf history restore <workspace> --point <number>");
        return 2;
    }
}
