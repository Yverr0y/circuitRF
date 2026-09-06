using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CircuitRF.Diagnostics;
using CircuitRF.Engine;

namespace CircuitRF.Cli.Serve;

/// <summary>
/// The protocol adapter: it advertises circuitRF's capabilities to an external client and invokes
/// them on request. It owns no logic (R-aut-1) — it translates a request into an argument vector,
/// hands that to <see cref="CliEntry.Run"/>, and hands the document back.
///
/// <para><b>It calls the verb; it does not re-implement it.</b> That is what makes R-aut-13 —
/// nothing reachable here that is not reachable from the command line, and vice versa — a property
/// of the code rather than a rule to remember. The parity gate compares two documents that came out
/// of one function.</para>
///
/// <para><b>One call at a time, but the reader never blocks.</b> The verbs use process-wide state
/// (<see cref="JsonRun"/>, <see cref="Console.Out"/>), so two capability calls cannot be in flight
/// together; they are queued onto one worker. The reader loop keeps reading regardless, which is the
/// whole reason for the split: <c>notifications/cancelled</c> and <c>ping</c> have to be answerable
/// while a full-wave run is going, and a run that cannot be cancelled is one a client times out on
/// and retries, doubling the cost of the run it gave up on.</para>
///
/// <para><b>Destructive operations are refusals, not confirmations</b> (R-aut5-8) — and they are
/// refusals by omission rather than by a filter: there is no tool that deletes, and no capability
/// below writes outside the paths it chooses itself. A client that wants a file gone deletes it
/// itself.</para>
/// </summary>
internal sealed class McpServer
{
    /// <summary>The protocol revisions this server knows how to speak. A client asking for one of
    /// them is answered in it; anything else is answered in the newest, which is what the protocol
    /// says to do rather than failing the handshake.</summary>
    private static readonly string[] KnownProtocolVersions = ["2025-06-18", "2025-03-26", "2024-11-05"];

    private readonly JsonRpc  _rpc;
    private readonly PathRoot _root;

    private readonly BlockingCollection<Action> _work = new(new ConcurrentQueue<Action>());
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inFlight = new();

    public McpServer(JsonRpc rpc, PathRoot root) { _rpc = rpc; _root = root; }

    /// <summary>Reads until the client disconnects. Returns the process exit code.</summary>
    public int Serve()
    {
        var worker = new Thread(WorkerLoop) { IsBackground = true, Name = "circuitrf-serve" };
        worker.Start();

        try
        {
            while (true)
            {
                var message = _rpc.Read(out string? malformed);
                if (message is null)
                {
                    // A line that is not JSON is the client's error and is reported as one; end of
                    // stream is the client disconnecting, which is not an error at all.
                    if (malformed is not null) _rpc.Error(null, JsonRpc.ParseError, "Not a JSON-RPC message.");
                    break;
                }
                Handle(message);
            }
        }
        finally
        {
            // A client that disconnects mid-run: stop the run rather than leaving the process
            // holding a solve nobody is waiting for.
            foreach (var cts in _inFlight.Values) { try { cts.Cancel(); } catch { /* already done */ } }
            _work.CompleteAdding();
            worker.Join(TimeSpan.FromSeconds(30));
        }

        return 0;
    }

    // ── dispatch ─────────────────────────────────────────────────────────────

    private void Handle(JsonObject message)
    {
        string? method = message["method"]?.GetValue<JsonElement>().ValueKind == JsonValueKind.String
            ? message["method"]!.GetValue<string>()
            : null;
        JsonNode? id = message["id"];

        if (method is null)
        {
            // A response, not a request. This server issues no requests of its own, so there is
            // nothing it can be an answer to; ignoring it is what the protocol asks for.
            return;
        }

        switch (method)
        {
            case "initialize":
                _rpc.Result(id, Initialize(message["params"] as JsonObject));
                return;

            case "notifications/initialized" or "notifications/roots/list_changed":
                return;

            case "ping":
                _rpc.Result(id, new JsonObject());
                return;

            case "tools/list":
                _rpc.Result(id, new JsonObject { ["tools"] = ToolCatalog.Advertise() });
                return;

            case "tools/call":
                Enqueue(id, message["params"] as JsonObject);
                return;

            case "notifications/cancelled":
                Cancel(message["params"] as JsonObject);
                return;

            default:
                if (id is not null) _rpc.Error(id, JsonRpc.MethodNotFound, $"No method '{method}'.");
                return;
        }
    }

    private JsonObject Initialize(JsonObject? parameters)
    {
        string? asked = parameters?["protocolVersion"]?.GetValue<JsonElement>().ValueKind == JsonValueKind.String
            ? parameters["protocolVersion"]!.GetValue<string>()
            : null;

        return new JsonObject
        {
            ["protocolVersion"] = asked is not null && KnownProtocolVersions.Contains(asked)
                ? asked
                : KnownProtocolVersions[0],
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"]   = new JsonObject { ["name"] = "circuitrf", ["version"] = Version() },
            // Terse, English, invariant (R-aut5-7). It says the three things a client cannot learn
            // from a tool schema.
            ["instructions"] =
                "circuitRF is driven by writing its documents and then running, checking or " +
                "explaining them; the file formats are the interface and there are no per-primitive " +
                "edit tools. Every path resolves under this server's root, and a path outside it is " +
                "refused. Nothing here deletes or overwrites an existing workspace.",
        };
    }

    // ── tool calls ───────────────────────────────────────────────────────────

    private void Enqueue(JsonNode? id, JsonObject? parameters)
    {
        string? tool = parameters?["name"]?.GetValue<JsonElement>().ValueKind == JsonValueKind.String
            ? parameters["name"]!.GetValue<string>()
            : null;

        if (tool is null)
        {
            _rpc.Error(id, JsonRpc.InvalidParams, "tools/call needs a tool name.");
            return;
        }

        var    arguments     = parameters?["arguments"] as JsonObject;
        var    progressToken = parameters?["_meta"]?["progressToken"];
        string key           = KeyOf(id);
        var    cts           = new CancellationTokenSource();
        _inFlight[key] = cts;

        _work.Add(() =>
        {
            try     { Invoke(id, tool, arguments, progressToken, cts.Token); }
            finally { _inFlight.TryRemove(key, out _); cts.Dispose(); }
        });
    }

    private void Invoke(JsonNode? id, string tool, JsonObject? arguments, JsonNode? progressToken,
                        CancellationToken ct)
    {
        // Every collector, cleared. Without this a document would carry the previous call's
        // diagnostics and outputs — a stale success a caller cannot tell from a real one.
        JsonRun.Reset();

        var document = new StringWriter();
        JsonRun.Sink = document;

        int exitCode;
        try
        {
            var argv = ToolCatalog.ToArgv(tool, arguments, _root, out var refusal);
            if (argv is null)
            {
                exitCode = Refuse(tool, refusal!, document);
            }
            else
            {
                using var _ = RunHost.Install(ct, progressToken is null ? null : p => Report(progressToken, p));
                exitCode = CliEntry.Run(argv);
            }
        }
        catch (OperationCanceledException)
        {
            // 130, which is the code `em` already returns for a run stopped at a work boundary
            // (cli.md §7). The adapter picks no new number.
            exitCode = Refuse(tool, CliDiagnostics.ServeCancelled(tool), document, 130);
        }
        catch (Exception ex)
        {
            // A server survives a verb that throws and reports it. The alternative is a connection
            // that simply ends, which tells the client nothing at all.
            exitCode = Refuse(tool, CliDiagnostics.ServeToolFailed(tool, ex.Message), document);
        }
        finally
        {
            JsonRun.Sink = null;
            JsonRun.Reset();
        }

        // The document, unchanged (R-aut5-5). The text block is the protocol's own envelope, not a
        // reshaping of the payload: these bytes are the CLI's `--json` bytes.
        _rpc.Result(id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = document.ToString().TrimEnd('\n'),
            }),
            // The CLI's own 0-or-not split (cli.md §7), forwarded. The document carries the truth —
            // including the deliberate difference between "did not converge" and "could not run".
            ["isError"] = exitCode != 0,
        });
    }

    /// <summary>
    /// Emits a refusal the way a verb would: the sentence on stderr, the diagnostic in the document.
    /// The adapter's own refusals are the same shape as everyone else's (R-aut-7).
    /// </summary>
    private static int Refuse(string tool, Diagnostic d, StringWriter document, int exitCode = 1)
    {
        JsonRun.Verb = tool;
        JsonRun.TakeFlags(["--json"]);
        JsonRun.Report(d);
        return JsonRun.Finish(exitCode);
    }

    private void Report(JsonNode progressToken, RunProgress p)
    {
        var parameters = new JsonObject
        {
            ["progressToken"] = progressToken.DeepClone(),
            ["progress"]      = p.Completed,
            ["message"]       = p.Stage,
        };
        // Total 0 means INDETERMINATE, which RunProgress states explicitly. Sending a zero would
        // read as a finished run rather than an unknown one, so the field is left out instead.
        if (p.Total > 0) parameters["total"] = p.Total;

        _rpc.Notify("notifications/progress", parameters);
    }

    private void Cancel(JsonObject? parameters)
    {
        if (parameters?["requestId"] is not { } requestId) return;
        if (_inFlight.TryGetValue(KeyOf(requestId), out var cts))
            try { cts.Cancel(); } catch { /* already finished */ }
    }

    /// <summary>A JSON-RPC id is a string OR a number, and the two spellings must not collide — the
    /// id <c>1</c> and the id <c>"1"</c> are different requests.</summary>
    private static string KeyOf(JsonNode? id) =>
        id is null ? "" : id.GetValue<JsonElement>().ValueKind + ":" + id.ToJsonString();

    private void WorkerLoop()
    {
        foreach (var item in _work.GetConsumingEnumerable())
        {
            try { item(); }
            catch (Exception ex)
            {
                // Nothing here should throw — Invoke catches — so this is the last resort, and it
                // reports rather than taking the server down with it.
                Console.Error.WriteLine($"serve: {ex.Message}");
            }
        }
    }

    private static string Version()
    {
        var asm = typeof(McpServer).Assembly;
        string? v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? asm.GetName().Version?.ToString();
        if (string.IsNullOrWhiteSpace(v)) return "unknown";
        int plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }
}
