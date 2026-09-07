using CircuitRF.Design.Revision;
using CircuitRF.Design.Workspace;
using CircuitRF.Diagnostics;
using RfCore.Export;

namespace CircuitRF.Cli;

/// <summary>
/// <c>circuitrf history &lt;checkpoint|list|restore|commit|versions&gt;</c> — <b>every history
/// operation has a headless spelling</b> (<c>docs/design/revision-control.md</c> §5.3d, R-rc5-23,
/// RC-7 R-rc7-22).
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
/// <see cref="WorkspaceRestore.Restore"/>, <see cref="WorkspaceCommit.Commit"/> — so the two cannot
/// diverge, and a source scan proves the view model kept no second copy.</para>
///
/// <para><b><c>commit</c> is the narrative half, and it refuses in the same four ways the window
/// does</b> (R-rc7-22): held, off, no history here, and nothing to keep. Those are circuitRF's own
/// refusals rather than git's — every git failure that reaches a caller is one of RC-3's translated
/// rows or is carried verbatim (R-rc7-19), and a sentence invented here would be a sentence the
/// window does not have.</para>
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
            "commit"     => Commit(args[1..]),
            "versions"   => Versions(args[1..]),
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

    // ── history commit (RC-7 R-rc7-22) ────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>The explicit commit — the narrative half</b> (§5.2, R-rc7-1, R-rc7-22).
    ///
    /// <para><b>It calls the same <c>src/Design</c> function the Commit action calls</b> and holds no
    /// logic of its own, which is what gate 12 asserts by comparing the two byte for byte.</para>
    ///
    /// <para><b>The title is an argument, and a blank one is allowed</b> — the window's dialog lets a
    /// designer press the button with the field empty, and a verb that refused where the dialog does
    /// not would be a different operation wearing the same name.</para>
    /// </summary>
    private static int Commit(string[] args)
    {
        string?      workspace = null;
        string?      title     = null;
        List<string> leaveOut  = [];

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--title" when i + 1 < args.Length:      title = args[++i]; break;
                // The spelling anyone who has used a version-control tool will reach for first.
                case "--message" when i + 1 < args.Length:    title = args[++i]; break;
                case "-m" when i + 1 < args.Length:           title = args[++i]; break;
                case "--leave-out" when i + 1 < args.Length:  leaveOut.Add(args[++i]); break;
                default:
                    if (args[i].StartsWith('-'))
                        return JsonRun.Fail(CliDiagnostics.RunUnknownOption("history commit", args[i]));
                    if (workspace is not null)
                        return JsonRun.Fail(CliDiagnostics.RunUnknownOption("history commit", args[i]));
                    workspace = args[i];
                    break;
            }
        }

        if (Bind(workspace, "history commit", create: false, out string root, out var git, out int failure) is false)
            return failure;

        // R-rc7-2's second half, and the refusal the window makes at the same moment. HELD is checked
        // before anything is read: a repository circuitRF does not manage is not circuitRF's to write
        // to, and R-rc6-6 does not bend because somebody typed a command.
        if (!EnclosingRepository.Detect(git!).MayRecord)
            return JsonRun.Fail(HistoryMessages.CannotKeepAVersionHeld());

        // OFF is the workspace's own setting, which outranks the per-user preference — and headless
        // there is no per-user preference to fall back to but the documented default.
        var setting = WorkspaceRevisionSetting.Read(WorkspaceRevisionSetting.CwsPathFor(root));
        if (!RevisionArming.IsArmed(RevisionArming.KeepHistoryDefault, setting))
            return JsonRun.Fail(HistoryMessages.CannotKeepAVersionOff());

        var result = WorkspaceCommit.Commit(git!, title, leaveOut);
        foreach (var d in result.Diagnostics) JsonRun.Report(d);

        JsonRun.History = new HistoryReportJson(
            Recorded: result.Ok,
            Version:  result.Version is { } v ? ToJson(v) : null);

        if (result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            return JsonRun.Finish(1);

        Console.WriteLine(result.Ok
            ? $"kept version: {result.Version!.Title}"
            : "nothing changed");

        // R-rc7-21. §8.3's escape hatch, in plain language, beside the action a user would look for it
        // from — which headless is the verb that keeps a version. Said once, on a successful commit,
        // and never as an offer to do it.
        if (result.Ok) Console.WriteLine(HistoryMessages.RewritingIsYoursToDo);

        return JsonRun.Finish(0);
    }

    // ── history versions (RC-7 R-rc7-9) ───────────────────────────────────────────────────────────

    /// <summary>
    /// The versions a designer kept, newest first — <b>the narrative, which is a different list from
    /// <c>history list</c>'s safety net and is never merged with it</b> (R-rc7-9).
    ///
    /// <para>An off period appears as its own row with its reason (R-rc7-10), because rendering it as
    /// an ordinary interval between two versions is the false-belief failure in its purest form.</para>
    /// </summary>
    private static int Versions(string[] args)
    {
        string? workspace = null;
        string? compare   = null;
        int     limit     = 0;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--limit" when i + 1 < args.Length && int.TryParse(args[i + 1], out int n):
                    limit = n; i++; break;
                // R-rc7-11. What changed between this version and the one before it, at the
                // granularity of documents — which is the design layer's question.
                case "--changes" when i + 1 < args.Length:
                    compare = args[++i]; break;
                default:
                    if (args[i].StartsWith('-'))
                        return JsonRun.Fail(CliDiagnostics.RunUnknownOption("history versions", args[i]));
                    if (workspace is not null)
                        return JsonRun.Fail(CliDiagnostics.RunUnknownOption("history versions", args[i]));
                    workspace = args[i];
                    break;
            }
        }

        if (Bind(workspace, "history versions", create: false, out string root, out var git, out int failure) is false)
            return failure;

        var versions = HistoryBrowser.Versions(git!, limit);

        if (compare is { Length: > 0 } wanted)
        {
            var version = versions.FirstOrDefault(
                              v => v.CommitId.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
                       ?? HistoryBrowser.Versions(git!).FirstOrDefault(
                              v => v.CommitId.StartsWith(wanted, StringComparison.OrdinalIgnoreCase));

            if (version is null) return JsonRun.Fail(CliDiagnostics.HistoryNoSuchVersion(wanted));
            return ReportChanges(git!, version);
        }

        var rows = HistoryBrowser.Rows(versions, RestorePoints.ListIncludingThinned(git!));

        JsonRun.History = new HistoryReportJson(Versions: [.. versions.Select(ToJson)]);

        if (rows.Count == 0)
        {
            JsonRun.Report(CliDiagnostics.HistoryNoVersionsYet(root));
            return JsonRun.Finish(0);
        }

        foreach (var row in rows)
            Console.WriteLine(row.IsGap ? "        " + HistoryMessages.GapBetween(row.Gap!.FromUtc, row.Gap.ToUtc)
                                        : Describe(row.Version!));

        return JsonRun.Finish(0);
    }

    /// <summary>What one version changed against the one before it. An initial version has none to
    /// compare against and says so rather than printing an empty list.</summary>
    private static int ReportChanges(GitCommand git, HistoryVersion version)
    {
        var parent = git.Run(["rev-parse", "--verify", "--quiet", version.CommitId + "^"],
                             new GitRunOptions(ReadOnly: true));

        var changes = parent.Ok && parent.Line.Length > 0
                    ? HistoryBrowser.Compare(git, parent.Line, version.CommitId)
                    : [];

        JsonRun.History = new HistoryReportJson(
            Versions: [ToJson(version)],
            Changes:  [.. changes.Select(c => new DocumentChangeJson(c.RelativePath,
                                                                     c.Kind.ToString().ToLowerInvariant(),
                                                                     c.PreviousPath))]);

        Console.WriteLine(HistoryMessages.ComparisonIsByDocument);
        if (changes.Count == 0)
        {
            Console.WriteLine(HistoryMessages.NothingDiffers);
            return JsonRun.Finish(0);
        }

        foreach (var c in changes)
            Console.WriteLine($"  {c.Kind.ToString().ToLowerInvariant(),-8}  {c.RelativePath}"
                            + (c.PreviousPath is { } was ? $"  (was {was})" : ""));

        return JsonRun.Finish(0);
    }

    /// <summary>One line per version: when, what it was called, and — when it applies — what it was
    /// brought back from, which is the line R-rc7-6 exists for.</summary>
    private static string Describe(HistoryVersion v)
        => $"{v.WhenUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {v.Title}"
         + (v.RestoredFrom is { } from ? $"  [brought back from '{from.Label}']" : "");

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
    private static VersionJson ToJson(HistoryVersion v) => new(
        v.CommitId,
        v.WhenUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        v.Title,
        v.Who,
        v.RestoredFrom?.Label);

    /// <inheritdoc cref="ToJson(HistoryVersion)"/>
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
          + "       circuitrf history restore <workspace> --point <number>\n"
          + "       circuitrf history commit <workspace> [--title <text>] [--leave-out <path>]\n"
          + "       circuitrf history versions <workspace> [--limit <n>] [--changes <version>]");
        return 2;
    }
}
