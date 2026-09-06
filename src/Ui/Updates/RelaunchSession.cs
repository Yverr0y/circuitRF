using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CircuitRF.Ui.Updates;

/// <summary>
/// The workspaces a deliberate relaunch should reopen, handed from the session that is quitting to
/// the one that replaces it.
///
/// <para><b>A FILE, not command-line arguments, and that is the whole design decision.</b> The
/// obvious route — start the successor with the paths in its argv — cannot survive this particular
/// journey on any platform, for three unrelated reasons:</para>
/// <list type="bullet">
///   <item><description><b>macOS ignores argv for files.</b> <c>App.OnFrameworkInitializationCompleted</c>
///   takes startup paths from <c>desktop.Args</c> on Windows and Linux only; on macOS the desktop
///   opens documents by Apple Event instead, so anything after <c>open --args</c> is never read as a
///   file to open.</description></item>
///   <item><description><b>The update hand-over re-launches a THIRD process.</b> A relaunch that
///   applies a staged update starts a successor which, in <c>Main</c>, flips the pointer or exchanges
///   the bundle and then hands over again (<see cref="UpdateStartup"/>). The paths would have to
///   survive two launches and, on macOS, an <c>open --args</c> in between.</description></item>
///   <item><description><b>Apple Events would race the swap.</b> Handing the files to Launch Services
///   instead delivers them to the process that is about to hand over, and the one that finally shows
///   a window never hears about them.</description></item>
/// </list>
///
/// <para>A file sidesteps all three: whichever process eventually gets as far as showing a window
/// reads it. It is <b>consumed once</b> — <see cref="Take"/> deletes it before returning — so a
/// crash midway through reopening does not turn into a relaunch that reopens the same workspaces on
/// every subsequent launch for ever.</para>
///
/// <para><b>It is written only by an explicit relaunch</b>, never by an ordinary quit. This is not a
/// session-restore feature and must not become one by accident: a user who closes circuitRF with
/// six workspaces open and launches it again gets their configured launch action, exactly as
/// before.</para>
/// </summary>
public static class RelaunchSession
{
    /// <summary>
    /// Beside the update state rather than in it. <see cref="UpdateState"/> is the durable record of
    /// what has been downloaded, staged and rolled back, read on paths that must keep working when
    /// this file is absent, malformed or from a future version — and a relaunch hand-off is none of
    /// those things. It is a scrap of paper, and it lives in its own envelope.
    /// </summary>
    public static string FilePath => Path.Combine(UpdatePaths.Root, "relaunch-session.json");

    private sealed record Payload(string[] Workspaces, string WrittenUtc);

    /// <summary>
    /// Records the workspaces to reopen. An empty list still WRITES — a relaunch from a window with
    /// no workspace open is a real case, and the empty file is what tells the next launch that this
    /// was a relaunch and its ordinary launch action should therefore not run.
    /// </summary>
    public static void Write(IEnumerable<string> workspacePaths)
    {
        try
        {
            string[] paths = workspacePaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => { try { return Path.GetFullPath(p); } catch { return p; } })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Directory.CreateDirectory(UpdatePaths.Root);
            AtomicFile.WriteAllTextAtomic(
                FilePath,
                JsonSerializer.Serialize(new Payload(paths, DateTime.UtcNow.ToString("O"))));
        }
        catch (Exception)
        {
            // A relaunch that reopens nothing is a small disappointment; a relaunch that does not
            // happen because the hand-off note could not be written is a broken button.
        }
    }

    /// <summary>
    /// Removes the note without reading it. Called when the relaunch is CANCELLED — a user who
    /// answered "cancel" at a save prompt is still working in this session, and a note left behind
    /// would reopen their workspaces on top of whatever they do next time they launch.
    /// </summary>
    public static void Clear()
    {
        try { File.Delete(FilePath); } catch { /* nothing to clear, or nothing we can do */ }
    }

    /// <summary>
    /// The workspaces this launch should reopen, or null when this launch is not a relaunch. Deletes
    /// the note first, so it is answered exactly once however the rest of the launch goes.
    ///
    /// <para>An empty (but present) note answers an empty array, which is NOT the same as null: it
    /// still means "this was a relaunch, do not run the launch action".</para>
    ///
    /// <para>Paths that no longer exist are dropped here rather than at the point of opening, so a
    /// workspace deleted or unmounted between the two launches produces one fewer window and no
    /// error dialog — the user asked to carry on where they left off, not to be told what has
    /// changed on disk since.</para>
    /// </summary>
    public static string[]? Take()
    {
        string path = FilePath;
        string text;

        try
        {
            if (!File.Exists(path)) return null;
            text = File.ReadAllText(path);
        }
        catch (Exception) { return null; }
        finally
        {
            try { File.Delete(path); } catch { /* best effort — a stale note is worse than none */ }
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(text);
            if (payload is null) return Array.Empty<string>();
            return payload.Workspaces?.Where(File.Exists).ToArray() ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            // Present but unreadable still means a relaunch happened. Opening nothing is the honest
            // outcome; refusing to treat it as a relaunch would run the launch action over the top.
            return Array.Empty<string>();
        }
    }
}
