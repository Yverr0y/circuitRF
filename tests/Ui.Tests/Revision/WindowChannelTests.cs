using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CircuitRF.Design.Revision;
using CircuitRF.Design.Workspace;
using Xunit;
using static CircuitRF.Ui.Tests.Revision.RestorePointsTests;

namespace CircuitRF.Ui.Tests.Revision;

/// <summary>
/// RC-5 gate 19d — <b>the channel carries both facts, and the advisory lock file carries neither</b>
/// (<c>brief-revision-control-5-checkpoints.md</c> §8; R-rc5-7a, R-rc5-7c).
///
/// <para>Driven over the REAL endpoint rather than through a substituted delegate, because the thing
/// under test is the wire: the Windows pipe had to become duplex and the Unix socket had to learn to
/// half-close, and a test that stubbed the transport would have passed before either change.</para>
///
/// <para><b>The server half is <c>src/Ui/Program.cs</c>'s, reproduced here in miniature</b> — a test
/// cannot start the application, and the parser it uses is the shared one, so the two cannot disagree
/// about the wire even though they are two loops.</para>
/// </summary>
[Collection(AppDataRootCollection.Name)]
public class WindowChannelTests
{
    /// <summary>
    /// R-rc5-7a. A window holding unsaved changes refuses the batch — <b>before anything is
    /// modified</b>, which is the whole of the promise.
    /// </summary>
    [GitFact]
    public void ADirtyWindowRefusesTheBatchOverTheRealChannelAndTheLockFileIsUntouched()
    {
        using var ws = Armed();
        ws.Write("cells/a/thing.csch", "untouched");

        // The advisory lock, as a running window would have left it. Its bytes are asserted because
        // §5.3c rejects putting a dirty flag in it: that would be a write to a shared folder on every
        // edit, and reading it back would be a timer — §5.3's rejected idle trigger under a third name.
        string lockPath = Path.Combine(ws.Root, WorkspaceLock.FileName);
        File.WriteAllText(lockPath, "{\"user\":\"someone\",\"host\":\"somewhere\"}");
        byte[] lockBefore = File.ReadAllBytes(lockPath);
        var    fileBefore = File.GetLastWriteTimeUtc(ws.File_("cells/a/thing.csch"));

        using var window = FakeWindow.Listening(unsaved: true);
        if (window.Unavailable) return;   // another circuitRF holds the endpoint; nothing to measure

        var refused = new BatchSession
        {
            KeepHistoryPreference   = true,
            WindowHasUnsavedChanges = root => WindowChannel.AsksWindowForUnsavedChanges(root) == true,
        }.Open(ws.Root, "widen the output match");

        Assert.False(refused.Ok);
        Assert.Equal("revision.batch.refused.unsaved", refused.Refusal!.Id);

        // Nothing was modified, and the question did not travel through the lock file.
        Assert.Equal(lockBefore, File.ReadAllBytes(lockPath));
        Assert.Equal(fileBefore, File.GetLastWriteTimeUtc(ws.File_("cells/a/thing.csch")));
        Assert.Empty(CheckpointReferences.List(ws.Git()));
    }

    /// <summary>
    /// R-rc5-7a, R-rc5-7b. With the window clean the batch opens, and <b>closing it tells the window
    /// which documents to reload</b> — without which the window goes on showing the old content over
    /// an undo stack describing edits the file no longer contains, and its next save discards
    /// everything the batch did.
    /// </summary>
    [GitFact]
    public void ACleanWindowLetsTheBatchOpenAndTheCloseNamesTheDocumentsItChanged()
    {
        using var ws = Armed();
        ws.Write("cells/a/thing.csch", "before");
        ws.Write("cells/b/other.csch", "untouched");

        string lockPath = Path.Combine(ws.Root, WorkspaceLock.FileName);
        File.WriteAllText(lockPath, "{\"user\":\"someone\",\"host\":\"somewhere\"}");
        byte[] lockBefore = File.ReadAllBytes(lockPath);

        using var window = FakeWindow.Listening(unsaved: false);
        if (window.Unavailable) return;

        var session = new BatchSession
        {
            KeepHistoryPreference   = true,
            WindowHasUnsavedChanges = root => WindowChannel.AsksWindowForUnsavedChanges(root) == true,
        };

        Assert.True(session.Open(ws.Root, "widen the output match").Ok);
        Assert.Single(CheckpointReferences.List(ws.Git()));

        ws.Write("cells/a/thing.csch", "after");

        var closed = session.Close();
        WindowChannel.TellWindowWhatChanged(ws.Root, closed.Modified);

        Assert.Contains("cells/a/thing.csch", window.Told);
        Assert.DoesNotContain("cells/b/other.csch", window.Told);
        Assert.Equal(ws.Root, window.ToldAbout);

        Assert.Equal(lockBefore, File.ReadAllBytes(lockPath));
    }

    /// <summary>
    /// <b>No window listening is an ANSWER, not a failure</b> (R-rc5-7c). Refusing every batch on a
    /// machine with no window open would refuse exactly the headless case §1.2 is written for.
    /// </summary>
    [Fact]
    public void WithNothingListeningTheQuestionAnswersNullAndTheBatchIsNotRefusedForIt()
    {
        var previous = WindowChannel.Timeout;
        WindowChannel.Timeout = TimeSpan.FromMilliseconds(250);
        try
        {
            // Null, not false: the two are different states and only one of them is "no window".
            Assert.Null(WindowChannel.AsksWindowForUnsavedChanges(Path.GetTempPath()));
        }
        finally { WindowChannel.Timeout = previous; }
    }

    /// <summary>The wire, parsed by the one function both halves use.</summary>
    [Fact]
    public void APlainPathIsStillAPathAndOnlyThePrefixIsARequest()
    {
        Assert.Null(WindowChannel.Parse("/some/workspace/cell.csch"));

        var asked = WindowChannel.Parse(WindowChannel.Prefix + WindowChannel.AskUnsaved + " /ws");
        Assert.NotNull(asked);
        Assert.Equal(WindowChannel.AskUnsaved, asked!.Verb);
        Assert.Equal("/ws", asked.WorkspaceRoot);
        Assert.Empty(asked.Paths);

        var told = WindowChannel.Parse(
            WindowChannel.Prefix + WindowChannel.TellModified + " /ws\tcells/a.csch\tcells/b.csch");
        Assert.NotNull(told);
        Assert.Equal(["cells/a.csch", "cells/b.csch"], told!.Paths);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    private static GitWorkspace Armed()
    {
        var ws = new GitWorkspace();
        File.WriteAllText(ws.GlobalConfig,
            "[user]\n\tname = A Designer\n\temail = designer@example.invalid\n");
        Assert.True(WorkspaceArming.Arm(ws.Root, CheckpointOrigin.SavePoint, true, null, true).Armed);
        return ws;
    }

    /// <summary>
    /// A running window, as far as the channel is concerned: it answers the unsaved question and
    /// records what a batch close told it.
    ///
    /// <para><b>It binds the real endpoint</b>, which another circuitRF on the same machine may
    /// already hold. That is reported as <see cref="Unavailable"/> and the gate returns rather than
    /// failing — taking somebody's running application's socket away would be a worse test than no
    /// test.</para>
    /// </summary>
    private sealed class FakeWindow : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly bool                    _unsaved;
        private          Socket?                 _listener;
        private          Task?                   _loop;

        public bool                  Unavailable { get; private set; }
        public string?               ToldAbout   { get; private set; }
        public IReadOnlyList<string> Told        { get; private set; } = [];

        private FakeWindow(bool unsaved) => _unsaved = unsaved;

        public static FakeWindow Listening(bool unsaved)
        {
            var window = new FakeWindow(unsaved);
            try
            {
                if (OperatingSystem.IsWindows()) window.StartPipe();
                else                            window.StartSocket();
            }
            catch (Exception e) when (e is IOException or SocketException or UnauthorizedAccessException)
            {
                window.Unavailable = true;
            }

            return window;
        }

        private void StartSocket()
        {
            string path = WindowChannel.UnixSocketPath();

            // Only removed when nothing answers on it — a live circuitRF's socket is not this test's
            // to take.
            if (File.Exists(path) && !Answers(path)) File.Delete(path);

            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(path));
            _listener.Listen(4);

            _loop = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    Socket conn;
                    try { conn = await _listener.AcceptAsync(_stop.Token); }
                    catch { break; }

                    using (conn)
                    {
                        var buffer = new byte[8192];
                        int read;
                        try { read = conn.Receive(buffer); } catch { continue; }
                        if (read <= 0) continue;

                        string line = Encoding.UTF8.GetString(buffer, 0, read).Split('\n')[0];
                        if (Answer(line) is { } reply)
                            try { conn.Send(Encoding.UTF8.GetBytes(reply + "\n")); } catch { }
                    }
                }
            });
        }

        private static bool Answers(string path)
        {
            try
            {
                using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                probe.Connect(new UnixDomainSocketEndPoint(path));
                return true;
            }
            catch { return false; }
        }

        private void StartPipe()
        {
            _loop = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(
                            WindowChannel.EndpointName, PipeDirection.InOut,
                            maxNumberOfServerInstances: 1,
                            transmissionMode: PipeTransmissionMode.Byte,
                            options: PipeOptions.Asynchronous);

                        await server.WaitForConnectionAsync(_stop.Token);

                        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                        string? line = await reader.ReadLineAsync(_stop.Token);
                        if (line is null) continue;

                        if (Answer(line) is { } reply)
                        {
                            using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true)
                                               { AutoFlush = true };
                            await writer.WriteLineAsync(reply);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch { /* one bad client is not the end of the loop */ }
                }
            });
        }

        /// <summary>What <c>src/Ui/Program.cs</c>'s own <c>Answer</c> does, over the shared parser.</summary>
        private string? Answer(string line)
        {
            if (WindowChannel.Parse(line) is not { } request) return null;

            if (request.Verb == WindowChannel.AskUnsaved)
                return _unsaved ? WindowChannel.Yes : WindowChannel.No;

            if (request.Verb == WindowChannel.TellModified)
            {
                ToldAbout = request.WorkspaceRoot;
                Told      = request.Paths;
                return "ok";
            }

            return WindowChannel.No;
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener?.Dispose(); } catch { }
            try { _loop?.Wait(TimeSpan.FromSeconds(5)); } catch { }

            if (!OperatingSystem.IsWindows())
                try { File.Delete(WindowChannel.UnixSocketPath()); } catch { }
        }
    }
}
