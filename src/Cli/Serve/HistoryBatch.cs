using System.Text.Json.Nodes;
using CircuitRF.Design.Revision;
using CircuitRF.Design.Workspace;
using CircuitRF.Diagnostics;
using RfCore.Export;

namespace CircuitRF.Cli.Serve;

/// <summary>
/// <b>The batch's open and close, and the state advertisement that goes with them</b>
/// (<c>docs/design/revision-control.md</c> §5.3a, §5.3b, §5.3d; R-rc5-6a … R-rc5-6i).
///
/// <para><b>Why these two are here and the other three nouns are CLI verbs.</b> A batch is a SESSION:
/// it is opened, it stays open while an agent works, and it is closed. A process that exits after one
/// command cannot hold that, so <c>history checkpoint</c> can be a verb and <c>batch open</c> cannot.
/// This server is the only surface in circuitRF that can, which is exactly what §5.3d says.</para>
///
/// <para><b>It writes no revision-control logic of its own.</b> <see cref="BatchSession"/> below the
/// firewall decides everything; this is argument parsing, the state document, and the rules.</para>
///
/// <para><b>The rules travel with the state, on every call</b> (R-rc5-6h). A capable agent has a shell
/// and a git of its own and can write to this repository whether or not anyone intended that;
/// publishing the rules means it learns them from the server rather than from an architecture document
/// it was never given. <b>A rule an agent cannot read is a rule that does not exist.</b></para>
/// </summary>
internal sealed class HistoryBatch
{
    /// <summary>The tool name. One tool with three actions, for R-aut5-4's reason: a client that
    /// discovers tools up front carries every description for the whole session.</summary>
    public const string ToolName = "batch";

    private readonly BatchSession _session = new();
    private readonly PathRoot     _root;

    public HistoryBatch(PathRoot root)
    {
        _root = root;

        // R-rc5-7a, over R-rc5-7c's channel. The window is ASKED, and a null answer — nobody
        // listening — is "no window to protect" rather than the worst case: treating it as dirty
        // would refuse every batch on a machine with no window open, which is exactly the headless
        // case §1.2 is written for.
        _session.WindowHasUnsavedChanges = root2 => WindowChannel.AsksWindowForUnsavedChanges(root2) == true;
    }

    /// <summary>The tool's schema, hand-written because it is the one tool that does not become a
    /// command line — <see cref="ToolCatalog"/> generates from the table that BUILDS one, and there
    /// is no argv here to build.</summary>
    public static JsonObject Advertise() => new()
    {
        ["name"] = ToolName,
        ["description"] =
            "The safety net under a change you are about to make. Open a batch BEFORE your first "
          + "modification and say what you intend in one line: circuitRF keeps a restore point of the "
          + "workspace as it is now, so the designer can undo everything you do in one action. Close "
          + "it when you are done. 'state' reports whether a history is being kept here and returns "
          + "the rules you are expected to follow.",
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["action"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("open", "close", "state"),
                    ["description"] = "open before your first modification; close when done; state to ask.",
                },
                ["path"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The workspace folder.",
                },
                ["intent"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] =
                        "With 'open': one line saying what you are about to change, in your own words. "
                      + "It is the label the designer reads in their restore-point list.",
                },
            },
            ["required"] = new JsonArray("action", "path"),
        },
    };

    /// <summary>
    /// Runs one call and returns the document, exactly shaped like every other verb's
    /// (<c>cli.md</c> §7A): the sentence as a diagnostic, the answer in the payload.
    /// </summary>
    public string Invoke(JsonObject? arguments, out int exitCode)
    {
        JsonRun.Reset();
        var document = new StringWriter();
        JsonRun.Sink = document;
        JsonRun.Verb = ToolName;
        JsonRun.TakeFlags(["--json"]);

        try     { exitCode = Dispatch(arguments); }
        finally { JsonRun.Sink = null; }

        string text = document.ToString().TrimEnd('\n');
        JsonRun.Reset();
        return text;
    }

    private int Dispatch(JsonObject? arguments)
    {
        string? action = Text(arguments, "action");
        string? given  = Text(arguments, "path");

        if (action is null || given is null)
            return JsonRun.Finish(JsonRun.Fail(CliDiagnostics.ServeToolFailed(
                ToolName, "both 'action' and 'path' are required.")));

        string? root = _root.Resolve(given, out var refusal);
        if (root is null) return JsonRun.Finish(JsonRun.Fail(refusal!));

        JsonRun.InputPath = root;

        // A workspace folder, or nothing to record into. §5.9's posture is ABSENT rather than
        // disabled, and the refusal offers the remedy the save lifecycle already has (R-rc5-20).
        bool isWorkspace = Directory.Exists(root)
                        && File.Exists(Path.Combine(root, WorkspacePersistence.FileName));

        return action switch
        {
            "open"  => Open(isWorkspace ? root : null, Text(arguments, "intent")),
            "close" => Close(root),
            "state" => State(isWorkspace ? root : null),
            _       => JsonRun.Finish(JsonRun.Fail(CliDiagnostics.ServeToolFailed(
                           ToolName, $"there is no action '{action}'. Known: open, close, state."))),
        };
    }

    /// <summary>
    /// R-rc5-6a. <b>The restore point is taken when the batch opens, before the first
    /// modification.</b> A refusal here means nothing was modified — which is the whole of the
    /// promise, and is why every one of them arrives at this moment rather than later.
    /// </summary>
    private int Open(string? root, string? intent)
    {
        var opened = _session.Open(root, intent);

        if (!opened.Ok)
        {
            JsonRun.Report(opened.Refusal!);
            Advise(root, stopped: true);
            return JsonRun.Finish(1);
        }

        if (opened.Point is { } point)
            JsonRun.History = new HistoryReportJson(
                Recorded: true,
                Point:    new RestorePointJson(
                              point.Sequence,
                              point.TakenUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                              CheckpointMessage.Spell(point.Origin),
                              point.Label, point.Kept, point.LeftOut));
        else
            // R-rc5-5a and R-rc5-6c both land here, and they are the same answer: the state is
            // already in the history, so the floor is under the agent either way.
            JsonRun.History = new HistoryReportJson(Recorded: false);

        Advise(root, stopped: false);
        return JsonRun.Finish(0);
    }

    /// <summary>
    /// R-rc5-6d. <b>Closing a batch that is not open is not an error.</b> An agent that died and was
    /// restarted must be able to say so without being told it did something wrong, and the restore
    /// point it needed was taken before any of that.
    /// </summary>
    private int Close(string root)
    {
        var closed = _session.Close();

        // R-rc5-7b. The window is told which documents the batch changed, so it reloads them and
        // discards their undo stacks — without this it goes on showing the old content over a stack
        // describing edits the file no longer contains, and its next save discards everything the
        // batch did. No window listening means no window to protect, which is why nothing here waits
        // on an answer.
        if (closed.WasOpen && closed.Modified.Count > 0)
            WindowChannel.TellWindowWhatChanged(root, closed.Modified);

        JsonRun.Report(closed.WasOpen
            ? CliDiagnostics.BatchClosed(closed.Modified.Count)
            : CliDiagnostics.BatchWasNotOpen());

        foreach (string path in closed.Modified) JsonRun.AddOutput("document", Path.Combine(root, path));

        Advise(root, stopped: false);
        return JsonRun.Finish(0);
    }

    /// <summary>R-rc5-6g. The state, reported rather than left to be inferred from the filesystem —
    /// which an agent would infer wrongly, because a <c>.git</c> directory alone does not distinguish
    /// a repository circuitRF created from one the user created.</summary>
    private int State(string? root)
    {
        Advise(root, stopped: false);
        return JsonRun.Finish(0);
    }

    /// <summary>
    /// The state and the rules, on every answer this tool gives.
    ///
    /// <para><b>They are diagnostics rather than a bespoke payload</b> so they render identically
    /// wherever this document is read, and so a client that only reads sentences still gets them.</para>
    /// </summary>
    private void Advise(string? root, bool stopped)
    {
        var setting = root is { Length: > 0 }
            ? WorkspaceRevisionSetting.Read(WorkspaceRevisionSetting.CwsPathFor(root))
            : null;

        var state = AgentContract.StateOf(root, _session.KeepHistoryPreference, setting);

        JsonRun.Note(CliDiagnostics.BatchState(
            AgentContract.Spell(state),
            _session.OpenWorkspace is not null,
            _session.OpenIntent,
            _session.OpenWorkspace ?? root ?? ""));

        for (int i = 0; i < AgentContract.Rules.Count; i++)
            JsonRun.Note(CliDiagnostics.BatchRule(i + 1, AgentContract.Rules[i]));

        if (stopped) JsonRun.Note(CliDiagnostics.BatchImproviseNothing());
    }

    private static string? Text(JsonObject? arguments, string key)
        => arguments?[key] is { } node
        && node.GetValue<System.Text.Json.JsonElement>().ValueKind == System.Text.Json.JsonValueKind.String
            ? node.GetValue<string>()
            : null;
}
