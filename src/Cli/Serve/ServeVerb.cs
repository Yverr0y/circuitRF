using System.Text;

namespace CircuitRF.Cli.Serve;

/// <summary>
/// <c>circuitrf serve --root &lt;dir&gt;</c> — the protocol adapter, as a VERB on this binary.
///
/// <para><b>R-aut5-1: a verb, not a second executable.</b> Packaging is the constraint, not style.
/// What ships is named after the APPLICATION rather than the assembly, and that name is a literal in
/// five packaging files because .NET names the published host after the assembly and
/// <c>CrfRenameApphost</c> renames it after publish only. A second executable means a second rename,
/// a second set of those literals, and a second thing a platform script can silently omit — and
/// 1.0.0-beta.2 already shipped 7 of its 15 artifacts that way, with no error, because a missing
/// payload stops an update without saying so.</para>
///
/// <para><b>R-aut5-2: this verb is exempt from <c>cli.md</c> §3.1's channel split, and only this
/// verb.</b> stdout carries the protocol framing, so nothing else may be written to it — ever. The
/// guarantee is structural rather than a rule every printer has to remember: the real stdout is
/// taken here, <see cref="Console.Out"/> is replaced with a sink before a single capability runs,
/// and each verb's document is written to a string. Every engine progress line, <c>[circuitRF]</c>
/// note and worker log that the run verbs deliberately send to stderr keeps going there, where a
/// client's own logging picks it up.</para>
///
/// <para><b>What the server will not do</b> (R-aut5-8): it resolves every path under
/// <see cref="PathRoot"/>; it launches no shell and no process a client named — the device-worker
/// and PCell paths still launch their own, and nothing new becomes launchable because a client asked;
/// and there is no tool that deletes or overwrites, because there is no user at the other end to
/// confirm with.</para>
///
/// <para><b><c>--kits</c> is the operator's, not the client's.</b> It is one of the flags every verb
/// takes and is stripped before dispatch, so <c>circuitrf serve --root &lt;dir&gt; --kits
/// &lt;dir&gt;</c> registers the resolver for the whole server and every <c>run</c> resolves an
/// externally-supplied device model with it. It is deliberately not a tool argument: a kit folder is
/// installed software rather than design data, it lives outside the root by nature, and letting a
/// client name one would be the server pointing at an arbitrary directory on its say-so.</para>
/// </summary>
internal static class ServeVerb
{
    public static int Run(string[] args)
    {
        string? root = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length:
                    root = args[++i];
                    break;
                default:
                    return JsonRun.Fail(CliDiagnostics.ServeUnknownOption(args[i]));
            }
        }

        if (root is null)
        {
            int code = JsonRun.Fail(CliDiagnostics.ServeRootRequired());
            Console.Error.WriteLine("Usage: circuitrf serve --root <dir> [--kits <dir>]");
            return code;
        }

        var confinement = PathRoot.Open(root, out var refusal);
        if (confinement is null) return JsonRun.Fail(refusal!);

        // --json is one of the flags taken before dispatch, so `serve` never sees it in its own
        // arguments — but it has already captured the real stdout, and this verb needs that stream
        // for the framing. Two writers on one stream is exactly R-aut5-2's failure, so it is refused.
        //
        // Refused HERE and not earlier, deliberately: every argument and root refusal above still
        // emits a document under --json, the way every other verb's does (R-aut-7). What cannot be
        // answered with a document is the case where the server would actually start, because from
        // that point stdout belongs to the protocol.
        if (JsonRun.Enabled) return JsonRun.Fail(CliDiagnostics.ServeJsonNotApplicable());

        // Taken BEFORE anything is redirected, and held for the protocol alone. Opened as a stream
        // rather than kept as Console.Out, so replacing Console.Out cannot take it away.
        var stdout = Console.OpenStandardOutput();
        var stdin  = Console.OpenStandardInput();

        // From here on nothing this process prints to Console.Out reaches a caller. Every verb also
        // redirects it for itself under --json; this is the belt for the paths that run before or
        // outside one.
        Console.SetOut(TextWriter.Null);

        Console.Error.WriteLine($"[circuitRF] serve: root {confinement.Root}");

        using var rpc = new JsonRpc(stdin, stdout);
        return new McpServer(rpc, confinement).Serve();
    }
}
