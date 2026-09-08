// ================================================================
//  DataDisplayDiagnostics.cs  —  the one thing the resolution code
//  reports UPWARDS, as a seam rather than a reference
//
//  RND-4. `TraceResolve` records a failed trace resolve on the crash
//  trail (`CrashReporter.Note`), which is src/Ui's and stays there — a
//  session file, a rolling log, a window that offers to send it. The
//  resolve itself is below the firewall now, so the report is a delegate
//  the application fills in.
//
//  UNSET IS A CORRECT STATE, not a degraded one. Every failure this
//  reports ALSO ends as "<invalid>" on the trace and an empty point
//  list, which is what the user and the caller actually see; the note is
//  the diagnostic that turned a bare field IndexOutOfRangeException into
//  a named cube and slice (see TraceResolve's own comment). A CLI render
//  and a unit test simply have nowhere to put it.
// ================================================================

namespace CircuitRF.Render.DataDisplay;

public static class DataDisplayDiagnostics
{
    /// <summary>
    /// Where a failed trace resolve is reported. Installed by src/Ui
    /// (<c>DataDisplayDiagnosticsInstaller</c>); null everywhere else.
    /// </summary>
    public static Action<string>? NoteSink { get; set; }

    internal static void Note(string message) => NoteSink?.Invoke(message);
}
