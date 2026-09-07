using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;

namespace CircuitRF.Design.Revision;

/// <summary>
/// <b>The channel a headless process reaches a running window over</b>
/// (<c>docs/design/revision-control.md</c> §5.3c, §12 Q28; R-rc5-7a, R-rc5-7c).
///
/// <para><b>It is the one the application already has</b> — <c>src/Ui/Program.cs</c>'s
/// single-instance forwarding, a named pipe on Windows and a Unix-domain socket on Linux, brought up
/// on macOS as well for this purpose since there the OS carries the double-click and nothing else had
/// needed it. Two messages travel on it: <c>serve</c> asks the window whether it holds unsaved
/// changes to a workspace, and tells it which documents a batch modified when the batch closes.</para>
///
/// <para><b>Why not the obvious alternatives</b>, both of which were specified and rejected:</para>
/// <list type="bullet">
///   <item><description><b>Not the on-focus rescan.</b> It is the wrong mechanism twice over — it
///   rebuilds the project tree rather than the documents, and a designer watching an agent work never
///   leaves the window, so it may not fire at all.</description></item>
///   <item><description><b>Not the advisory lock file.</b> A dirty flag rewritten there is a write to
///   a shared folder on every edit, and reading it back is a timer — which is §5.3's rejected idle
///   trigger under a third name.</description></item>
///   <item><description><b>Not a filesystem watcher.</b>
///   <c>workspace-and-project-tree.md</c> §9 defers one deliberately and this does not un-defer
///   it.</description></item>
/// </list>
///
/// <para><b>No window listening means no window to protect</b>, which is an ANSWER and not a failure:
/// refusing every batch on a machine with no window open would refuse exactly the headless case §1.2
/// is written for.</para>
///
/// <para><b>The client half is here, below the firewall, because <c>src/Cli</c> needs it and cannot
/// reference <c>src/Ui</c></b> — which is the same reason the git type is here. The SERVER half is in
/// <c>src/Ui/Program.cs</c>, where the single instance lives, and the two agree by the constants in
/// this file.</para>
/// </summary>
public static class WindowChannel
{
    /// <summary>The endpoint's name, shared by both halves. <b>A protocol identity</b> — changing it
    /// makes a new build unable to reach an old one, which is exactly what the version suffix is for
    /// if that ever has to happen deliberately.</summary>
    public const string EndpointName = "circuitRF_workspace_v1";

    /// <summary>The prefix that distinguishes a REQUEST from the plain file paths the channel has
    /// always carried. A line without it is a path to open, exactly as before.</summary>
    public const string Prefix = "crf1 ";

    /// <summary>Asks whether the window holds unsaved changes to a workspace (R-rc5-7a).</summary>
    public const string AskUnsaved = "unsaved?";

    /// <summary>Tells the window which documents a batch modified (R-rc5-7b).</summary>
    public const string TellModified = "modified";

    /// <summary>The affirmative answer to <see cref="AskUnsaved"/>. Anything else is "no".</summary>
    public const string Yes = "yes";

    /// <summary>The negative answer, and what an unreachable window is treated as.</summary>
    public const string No = "no";

    /// <summary>How long a request waits. <b>Short on purpose</b>: the window is on the same machine
    /// and either answers immediately or is not there, and a batch that hung waiting for one would be
    /// worse than a batch that proceeded.</summary>
    public static TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Whether a running window holds unsaved changes to <paramref name="workspaceRoot"/>.
    ///
    /// <para><b>Null means nobody answered</b> — no window, or one that could not be reached — which
    /// the caller must treat as "no window to protect" rather than as the worst case.</para>
    /// </summary>
    public static bool? AsksWindowForUnsavedChanges(string workspaceRoot)
        => Exchange(Prefix + AskUnsaved + " " + workspaceRoot) is { } answer
            ? answer.Trim().Equals(Yes, StringComparison.OrdinalIgnoreCase)
            : null;

    /// <summary>
    /// Tells the window which documents a batch changed, so it can reload them and discard their undo
    /// stacks (R-rc5-7b). <b>Fire and forget from the caller's point of view</b>: whether a window was
    /// listening changes nothing the batch can do about it.
    /// </summary>
    public static void TellWindowWhatChanged(string workspaceRoot, IReadOnlyList<string> relativePaths)
    {
        var line = new StringBuilder(Prefix).Append(TellModified).Append(' ').Append(workspaceRoot);
        foreach (string p in relativePaths) line.Append('\t').Append(p);
        Exchange(line.ToString());
    }

    /// <summary>The socket a Unix-family platform uses. Public so the server half names the same one.</summary>
    public static string UnixSocketPath()
        => Path.Combine(RuntimeDirectory(), $"{EndpointName}-{Environment.UserName}.sock");

    /// <summary>The instance lock beside it.</summary>
    public static string UnixLockPath()
        => Path.Combine(RuntimeDirectory(), $"{EndpointName}-{Environment.UserName}.lock");

    /// <summary>
    /// The runtime directory both halves agree on. <c>XDG_RUNTIME_DIR</c> where the session has one —
    /// per-user, cleaned up at logout, and short, which matters because a Unix socket path is capped
    /// near 104 bytes. Otherwise the temp directory, which is SHARED, so the user name is in the
    /// socket's own name there and two users on one machine do not collide.
    /// </summary>
    private static string RuntimeDirectory()
    {
        string? xdg = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return !string.IsNullOrEmpty(xdg) && Directory.Exists(xdg) ? xdg : Path.GetTempPath();
    }

    /// <summary>
    /// Sends one line and reads the one line back, or null when nobody was listening.
    ///
    /// <para><b>Every failure is null</b> — no endpoint, a refused connection, a timeout, a window
    /// that closed mid-exchange. There is nothing else this could usefully be, and an exception here
    /// would turn "no window is open" into a crashed batch.</para>
    /// </summary>
    private static string? Exchange(string request)
    {
        try
        {
            return OperatingSystem.IsWindows() ? OverPipe(request) : OverSocket(request);
        }
        catch (Exception e) when (e is IOException or SocketException or TimeoutException
                                    or UnauthorizedAccessException or ObjectDisposedException
                                    or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? OverPipe(string request)
    {
        using var client = new NamedPipeClientStream(".", EndpointName,
                                                     PipeDirection.InOut, PipeOptions.None);
        client.Connect((int)Timeout.TotalMilliseconds);

        using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        writer.WriteLine(request);

        using var reader = new StreamReader(client, new UTF8Encoding(false), false, 1024, leaveOpen: true);
        return reader.ReadLine();
    }

    private static string? OverSocket(string request)
    {
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        client.Connect(new UnixDomainSocketEndPoint(UnixSocketPath()));
        client.ReceiveTimeout = (int)Timeout.TotalMilliseconds;
        client.SendTimeout    = (int)Timeout.TotalMilliseconds;

        client.Send(Encoding.UTF8.GetBytes(request + "\n"));

        // Half-close so the server's own read loop sees end of input and answers. Shutting both ways,
        // as the file-forwarding path does, would close the channel before the reply could arrive.
        client.Shutdown(SocketShutdown.Send);

        var buffer = new byte[1024];
        int read   = client.Receive(buffer);
        return read <= 0 ? null : Encoding.UTF8.GetString(buffer, 0, read).Split('\n')[0];
    }

    /// <summary>
    /// The SERVER half's parser, here so both halves cannot disagree about the wire.
    /// </summary>
    /// <returns>Null when the line is not a request — which means it is a path to open, exactly as it
    /// always was.</returns>
    public static WindowRequest? Parse(string line)
    {
        if (!line.StartsWith(Prefix, StringComparison.Ordinal)) return null;

        string body  = line[Prefix.Length..];
        int    space = body.IndexOf(' ');
        if (space <= 0) return null;

        string verb = body[..space];
        string rest = body[(space + 1)..];

        string[] parts = rest.Split('\t');
        return new WindowRequest(verb, parts[0], [.. parts.Skip(1)]);
    }
}

/// <summary>One request off the channel.</summary>
/// <param name="Verb"><see cref="WindowChannel.AskUnsaved"/> or <see cref="WindowChannel.TellModified"/>.</param>
/// <param name="WorkspaceRoot">Which workspace it is about.</param>
/// <param name="Paths">Workspace-relative document paths, for <c>modified</c>.</param>
public sealed record WindowRequest(string Verb, string WorkspaceRoot, IReadOnlyList<string> Paths);
