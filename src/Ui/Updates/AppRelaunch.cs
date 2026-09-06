using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CircuitRF.Ui.Updates;

/// <summary>
/// How an applied update starts the version it has just installed — on macOS, through Launch
/// Services rather than <c>execv</c>.
///
/// <para><b>The bug this exists to remove (owner report, 2026-09-04).</b> After an automatic update
/// on macOS, every workspace under <c>~/Documents</c> refused to open with the protected-folder
/// diagnostic, while a workspace elsewhere opened normally. Quitting circuitRF and launching it again
/// fixed it completely, with nothing else changed — no setting touched, no permission granted. The
/// kernel's own record names the cause exactly:</para>
///
/// <code>
/// System Policy: circuitRF(61022) deny(1) file-read-data …/Documents/&lt;workspace&gt;
/// System Policy: circuitRF(61022) deny(1) file-read-data …/Documents/&lt;workspace&gt;/.cws
/// System Policy: circuitRF(61022) deny(1) file-write-unlink …/Documents/&lt;workspace&gt;/.cws
/// </code>
///
/// <para><b>Why the grant stops applying, and only for that one session.</b> macOS resolves a
/// protected-folder grant against the RESPONSIBLE process — the application identity the system
/// established when the process was launched. An <c>execv</c> keeps the process id and the process
/// clock (which is what <c>CrashReporter.IsOwnExecPredecessor</c> relies on), and it therefore keeps
/// that launch-time attribution too. The update has meanwhile exchanged the bundle underneath it, so
/// the identity the attribution points at is no longer the application on disk, and the System Policy
/// check has nothing that satisfies the stored grant. It does not prompt, because from TCC's point of
/// view there is no unanswered question — it simply denies. The next ordinary launch is spawned by
/// launchd with a fresh attribution and everything works, which is exactly the "quit and relaunch and
/// it is fine" the report describes.</para>
///
/// <para><b>The fix is to hand over the way the user would.</b> <c>open -n -a &lt;bundle&gt;</c> asks
/// Launch Services to start the application, so launchd spawns it and the new process is attributed
/// to the bundle that is actually installed. It costs one short-lived child process on the one launch
/// per update that applies a swap, and it is the only mechanism that produces the same process a
/// double-click would.</para>
///
/// <para><b>It is macOS-only and it is a preference, never a requirement.</b> Linux has no TCC and
/// keeps <c>execv</c>; Windows has no <c>execv</c> and already starts a successor. And when Launch
/// Services cannot be reached for any reason the caller falls straight back to <c>execv</c>: an
/// update that leaves the user with a working application and a stale privacy attribution is bad, and
/// an update that leaves them with no application at all is very much worse.</para>
/// </summary>
public static class AppRelaunch
{
    /// <summary>
    /// How long to wait for <c>open</c> to hand the request to Launch Services. It normally returns in
    /// well under a second; the wait exists to read its exit code, which is the only way to tell a
    /// refused launch from an accepted one. A timeout is treated as ACCEPTED — <c>open</c> is a child
    /// of this process, not its parent, so exiting does not cancel it, and the failure direction that
    /// matters is never starting the successor at all.
    /// </summary>
    private const int LaunchServicesTimeoutMs = 10_000;

    /// <summary>
    /// The seam a test drives, since a test host must not actually launch the application. Receives
    /// the resolved <c>.app</c> bundle and the arguments, and returns whether the launch was accepted.
    /// Null (the default) runs the real <c>open</c>.
    /// </summary>
    internal static Func<string, IReadOnlyList<string>, bool>? Launcher { get; set; }

    /// <summary>
    /// The <c>.app</c> bundle <paramref name="executable"/> is the main executable of, or null when it
    /// is not one — a versioned-pointer install, a <c>dotnet build</c> host, anything else.
    ///
    /// <para>Matched STRUCTURALLY, on the <c>&lt;name&gt;.app/Contents/MacOS/&lt;exe&gt;</c> shape that
    /// makes a bundle a bundle, rather than by looking for <c>.app</c> anywhere in the string. A
    /// directory called <c>foo.app</c> somewhere up a build tree is not a bundle root, and handing one
    /// to <c>open</c> produces a refusal the caller would then have to interpret.</para>
    /// </summary>
    public static string? BundleRootOf(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return null;

        try
        {
            string? macOsDir  = Path.GetDirectoryName(Path.GetFullPath(executable));
            string? contents  = Path.GetDirectoryName(macOsDir);
            string? bundle    = Path.GetDirectoryName(contents);

            if (macOsDir is null || contents is null || bundle is null) return null;

            return string.Equals(Path.GetFileName(macOsDir), "MacOS", StringComparison.Ordinal)
                && string.Equals(Path.GetFileName(contents), "Contents", StringComparison.Ordinal)
                && Path.GetFileName(bundle).EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                    ? bundle
                    : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Asks Launch Services to start the bundle <paramref name="executable"/> belongs to, and reports
    /// whether the request was accepted. False means the caller should fall back to <c>execv</c>.
    ///
    /// <para><c>-n</c> is not optional. This process is still alive when the request is made — it exits
    /// a moment later — and without <c>-n</c> Launch Services would see the application already running
    /// and simply activate this instance, which is then the one that exits. The result would be an
    /// update that quietly closes the application instead of restarting it.</para>
    /// </summary>
    public static bool TryRelaunchBundle(string executable, IReadOnlyList<string> args)
    {
        if (!OperatingSystem.IsMacOS()) return false;
        if (BundleRootOf(executable) is not { } bundle) return false;

        if (Launcher is { } seam)
        {
            try   { return seam(bundle, args); }
            catch { return false; }
        }

        return OpenNewInstance(bundle, args);
    }

    /// <summary>
    /// The argument that makes a successor wait for the session that started it. Read in
    /// <c>Program.Main</c> before anything else, and stripped there.
    /// </summary>
    public const string WaitForPidArgument = "--relaunch-wait";

    /// <summary>
    /// Starts a replacement for the RUNNING application and reports whether it began — the
    /// Relaunch action in the Messages panel, and nothing else.
    ///
    /// <para><b>This is not <see cref="UpdateStartup.HandOverTo"/>, and the difference is that this
    /// process is a live GUI.</b> The hand-over runs in <c>Main</c> before Avalonia, where
    /// <c>execv</c> is free: there is no window, nothing unsaved, and no one notices the process
    /// image change. Here there are windows that have just been asked to save, an exit sequence to
    /// run, and workers to end — so the successor is an ordinary independent process and this one
    /// leaves by the front door.</para>
    ///
    /// <para><b>The successor must not start until this process is gone, and that is a correctness
    /// requirement rather than a courtesy.</b> Windows and Linux both hold a single-instance guard
    /// for the lifetime of the process — a <c>Mutex</c> and a <c>flock</c>ed file respectively
    /// (<c>Program.Main</c>). A successor that starts while this one still holds it sees itself as a
    /// SECOND instance, forwards its arguments to the instance that is on its way out, and exits
    /// without showing a window. The user clicks Relaunch and circuitRF simply closes. So the
    /// successor is handed this process's id and waits for it, which also means this process can
    /// start it BEFORE exiting and does not need something else to do it afterwards.</para>
    ///
    /// <para><b>macOS goes through Launch Services for the same reason the update hand-over does</b> —
    /// see the class summary: a process that inherits a launch-time attribution pointing at a bundle
    /// the update has replaced is denied <c>~/Documents</c> with no prompt. It has no single-instance
    /// guard to wait for, but it is given the pid anyway: uniform, free, and correct if one is ever
    /// added.</para>
    /// </summary>
    /// <param name="executable">The application to start. Normally <c>Environment.ProcessPath</c> —
    /// the same executable this session was launched from, which on the versioned layout is what the
    /// stub would have started and is where <see cref="UpdateStartup"/> applies any staged update.</param>
    public static bool StartSuccessor(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return false;

        string[] args = [WaitForPidArgument, Environment.ProcessId.ToString()];

        // macOS first, and only macOS — TryRelaunchBundle returns false everywhere else, and returns
        // false here too when this is not a bundle (a `dotnet run` session, a versioned layout).
        if (TryRelaunchBundle(executable, args)) return true;

        try
        {
            var psi = new ProcessStartInfo(executable) { UseShellExecute = false };
            foreach (string a in args) psi.ArgumentList.Add(a);

            // Deliberately not redirecting anything, and deliberately not disposing the Process in a
            // way that would wait on it: the child must outlive this process, which is about to exit.
            using Process? p = Process.Start(psi);
            return p is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Blocks until process <paramref name="pid"/> has exited, or until
    /// <paramref name="timeoutMs"/> elapses. Called at the very top of <c>Main</c> in a successor
    /// started by <see cref="StartSuccessor"/>.
    ///
    /// <para><b>Every failure to find the process means the same thing here — it is already gone.</b>
    /// A pid that no longer exists, one the OS will not let us open, and a reused pid belonging to
    /// something else all resolve to "stop waiting", because the only thing this wait protects is a
    /// single-instance guard that a dead process cannot be holding.</para>
    ///
    /// <para><b>The timeout is what stops a wedged predecessor from taking the successor with it.</b>
    /// A user who clicked Relaunch is owed an application. If the old session is stuck — a save
    /// dialog on a disconnected volume, a hung worker — the successor eventually starts anyway and
    /// the worst case is the second-instance forwarding that would have happened without this wait:
    /// a relaunch that did not relaunch, rather than one that hangs for ever.</para>
    /// </summary>
    public static void WaitForProcessExit(int pid, int timeoutMs = 60_000)
    {
        if (pid <= 0 || pid == Environment.ProcessId) return;

        try
        {
            using Process p = Process.GetProcessById(pid);
            p.WaitForExit(timeoutMs);
        }
        catch (ArgumentException)     { /* not running — nothing to wait for */ }
        catch (InvalidOperationException) { /* exited between the lookup and the wait */ }
        catch (Exception)             { /* unreachable for any other reason: do not block a launch */ }
    }

    /// <summary>
    /// Reads and removes <see cref="WaitForPidArgument"/> from a command line, answering the pid it
    /// carried (or null). The argument must never reach anything downstream: <c>App</c> filters
    /// startup arguments with <c>File.Exists</c> so a flag would be ignored there by luck rather
    /// than by design, and Avalonia is handed the same array.
    /// </summary>
    public static int? TakeWaitForPid(ref string[] args)
    {
        int? pid = null;
        var kept = new List<string>(args.Length);

        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], WaitForPidArgument, StringComparison.Ordinal))
            {
                kept.Add(args[i]);
                continue;
            }

            // The value is consumed with the flag whether or not it parses, so a malformed pair
            // cannot leave a bare number behind to be mistaken for a file.
            if (i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int parsed)) pid = parsed;
                i++;
            }
        }

        args = kept.ToArray();
        return pid;
    }

    private static bool OpenNewInstance(string bundle, IReadOnlyList<string> args)
    {
        try
        {
            var psi = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add("-a");
            psi.ArgumentList.Add(bundle);

            // `--args` with nothing after it is accepted but pointless, and an empty argument list is
            // the overwhelmingly common case — a launch from the Dock or from Finder.
            if (args.Count > 0)
            {
                psi.ArgumentList.Add("--args");
                foreach (string a in args) psi.ArgumentList.Add(a);
            }

            using Process? p = Process.Start(psi);
            if (p is null) return false;

            return !p.WaitForExit(LaunchServicesTimeoutMs) || p.ExitCode == 0;
        }
        catch (Exception)
        {
            // No /usr/bin/open, no permission to spawn, a full process table — every one of them means
            // the same thing here: this route is unavailable, use the one that does not need it.
            return false;
        }
    }
}
