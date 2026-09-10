using CircuitRF.Diagnostics;

namespace CircuitRF.Ui.Tests.Localization;

/// <summary>
/// Both diagnostic classes below read <see cref="FileAccessDiagnostics.AppBundleReplacedThisSession"/>,
/// which is process-wide by design — it is one fact established once per launch. Sharing a collection
/// is what stops the class that SETS it from running beside the class that asserts the ordinary
/// message; without it the Downloads case failed while the after-update case was mid-flight.
/// </summary>
[CollectionDefinition("FileAccessDiagnostics")]
public sealed class FileAccessDiagnosticsCollection { }

/// <summary>
/// The macOS protected-folder diagnostic (owner report, 2026-08-27).
///
/// <para>Every workspace under <c>~/Documents</c> failed to open or save with "Access to the path …
/// is denied", while the identical operation under <c>~/Desktop</c> worked. The file permissions
/// were normal throughout — macOS gates <c>~/Documents</c> behind a per-folder privacy grant, and
/// .NET surfaces that with the same sentence it uses for a real permissions problem. The raw message
/// actively misleads: it sends the reader to check permissions that are already correct.</para>
/// </summary>
[Collection("FileAccessDiagnostics")]
public sealed class FileAccessDiagnosticsTests
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>The shape of the path from the report: a workspace folder under ~/Documents.</summary>
    [Fact]
    public void TheReportedWorkspacePath_IsRecognisedAsAProtectedFolder()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var d = FileAccessDiagnostics.TryDescribe(
            Path.Combine(Home, "Documents", "MyWorkspace", ".cws"),
            new UnauthorizedAccessException("Access to the path is denied."));

        Assert.NotNull(d);
        Assert.StartsWith("file.access.macos-protected-folder", d!.Id, StringComparison.Ordinal);

        string text = d.Render();
        // The exact System Settings row label, so the instruction matches what is on screen.
        Assert.Contains("\"Documents Folder\"", text, StringComparison.Ordinal);
        Assert.Contains("Privacy & Security > Files and Folders", text, StringComparison.Ordinal);
        // The point of the whole diagnostic: say the permissions are NOT the problem.
        Assert.Contains("not a file permission", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The subtle half, and the reason the original report took so long to resolve: for a build
    /// launched from a shell, macOS attributes file access to the TERMINAL. A user searching the
    /// privacy list for "circuitRF" finds nothing and reasonably concludes the setting is
    /// irrelevant. The test suite runs from exactly such a host, so this is the live case here.
    /// </summary>
    [Fact]
    public void ForATerminalLaunchedBuild_ItNamesTheTerminalRatherThanCircuitRf()
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (AppContext.BaseDirectory.Replace('\\', '/').Contains(".app/Contents/", StringComparison.Ordinal))
            return;   // a bundled run grants circuitRF directly; not the case under test

        string text = FileAccessDiagnostics.TryDescribe(
            Path.Combine(Home, "Documents", "w", ".cws"),
            new UnauthorizedAccessException("denied"))!.Render();

        Assert.Contains("terminal", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("circuitRF will not be listed", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Documents", "Documents Folder")]
    [InlineData("Desktop",   "Desktop Folder")]
    [InlineData("Downloads", "Downloads Folder")]
    public void EachProtectedFolder_IsNamedAsSystemSettingsNamesIt(string folder, string label)
    {
        if (!OperatingSystem.IsMacOS()) return;

        string text = FileAccessDiagnostics.TryDescribe(
            Path.Combine(Home, folder, "ws", ".cws"),
            new UnauthorizedAccessException("denied"))!.Render();

        Assert.Contains($"\"{label}\"", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The bundled and terminal cases get DIFFERENT ids, because they are different remedies — one
    /// points at circuitRF in the settings list, the other says circuitRF will not be there at all.
    /// Separate ids let support filter them apart, and stop a future edit from collapsing them back
    /// into one message with a swapped noun, which is what the first version of this did.
    /// </summary>
    [Fact]
    public void TheBundledAndTerminalCases_AreDistinctDiagnostics()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var d = FileAccessDiagnostics.TryDescribe(
            Path.Combine(Home, "Documents", "ws", ".cws"),
            new UnauthorizedAccessException("denied"))!;

        bool bundled = AppContext.BaseDirectory.Replace('\\', '/')
                                 .Contains(".app/Contents/", StringComparison.Ordinal);

        Assert.Equal(bundled
            ? "file.access.macos-protected-folder"
            : "file.access.macos-protected-folder.terminal-launch", d.Id);
    }

    /// <summary>
    /// Read by someone whose work has just failed to open, so it has to be scannable. The first
    /// version was a single 60-word sentence in which the "which app to grant" clause collided with
    /// the "then restart it" clause and left "it" with no clear referent.
    /// </summary>
    [Fact]
    public void TheMessageIsBrokenIntoShortSentences()
    {
        if (!OperatingSystem.IsMacOS()) return;

        string text = FileAccessDiagnostics.TryDescribe(
            Path.Combine(Home, "Documents", "ws", ".cws"),
            new UnauthorizedAccessException("denied"))!.Render();

        var sentences = text.Split(". ", StringSplitOptions.RemoveEmptyEntries);
        Assert.True(sentences.Length >= 5, $"Expected several short sentences, got {sentences.Length}.");
        foreach (var s in sentences)
            Assert.True(s.Split(' ').Length <= 34, $"Sentence too long to scan ({s.Split(' ').Length} words): {s}");
    }

    /// <summary>
    /// A save that is refused must not tell the user their workspace could not be OPENED. The
    /// 2026-09-04 report reached exactly that: the kernel logged <c>deny(1) file-write-unlink
    /// …/.cws</c> — a write — while the message on screen said "blocking circuitRF from opening".
    /// The remedy is identical either way, so the verb is the only thing that was wrong.
    /// </summary>
    [Fact]
    public void ARefusedSave_SaysSavingRatherThanOpening()
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (!AppContext.BaseDirectory.Replace('\\', '/').Contains(".app/Contents/", StringComparison.Ordinal))
            return;   // the terminal-launch message names no app and carries no verb

        string path = Path.Combine(Home, "Documents", "ws", ".cws");
        var ex = new UnauthorizedAccessException("denied");

        string saving  = FileAccessDiagnostics.TryDescribe(path, ex, FileAccessOperation.Saving)!.Render();
        string opening = FileAccessDiagnostics.TryDescribe(path, ex, FileAccessOperation.Opening)!.Render();

        Assert.Contains("from saving to", saving,  StringComparison.Ordinal);
        Assert.Contains("from opening",   opening, StringComparison.Ordinal);
        Assert.DoesNotContain("from opening", saving, StringComparison.Ordinal);
    }

    /// <summary>
    /// The default is Opening, so every existing caller keeps the sentence it had. A verb that
    /// changed under callers that never asked for one would be a regression dressed as a fix.
    /// </summary>
    [Fact]
    public void TheDefaultOperation_IsOpening()
    {
        var ex   = new UnauthorizedAccessException("denied");
        string p = Path.Combine(Home, "Documents", "ws", ".cws");

        Assert.Equal(FileAccessDiagnostics.TryDescribe(p, ex)!.Render(),
                     FileAccessDiagnostics.TryDescribe(p, ex, FileAccessOperation.Opening)!.Render());
    }

    /// <summary>
    /// Containment is by path SEGMENT, not by prefix. <c>~/Documents2</c> is not inside
    /// <c>~/Documents</c>, and describing it as a protected folder would send the user to toggle a
    /// setting that changes nothing.
    /// </summary>
    [Fact]
    public void ASiblingFolderWithAProtectedPrefix_IsNotMistakenForOne()
    {
        var d = FileAccessDiagnostics.TryDescribe(
            Path.Combine(Home, "Documents2", "ws", ".cws"),
            new UnauthorizedAccessException("denied"));

        Assert.Equal("file.access.denied", d!.Id);
    }

    /// <summary>An unprotected location still gets a diagnostic, just not the privacy one — the
    /// explanation has to match the cause or it is worse than the raw message.</summary>
    [Fact]
    public void AnOrdinaryPermissionsFailure_GetsThePlainExplanation()
    {
        var d = FileAccessDiagnostics.TryDescribe(
            Path.Combine(Home, "code", "ws", ".cws"),
            new UnauthorizedAccessException("denied"));

        Assert.Equal("file.access.denied", d!.Id);
        Assert.DoesNotContain("System Settings", d.Render(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Anything that is not an access failure returns null so the caller reports as it always has.
    /// A corrupt <c>.cws</c> must keep saying it is corrupt — mislabelling it a privacy problem
    /// would be a straight regression on the message that was already right.
    /// </summary>
    [Theory]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(InvalidDataException))]
    public void ANonAccessFailure_IsLeftAlone(Type exceptionType)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType)!;
        Assert.Null(FileAccessDiagnostics.TryDescribe(Path.Combine(Home, "Documents", "w.cws"), ex));
    }

    /// <summary>Renders cleanly — no leftover placeholder reaching the user as debris.</summary>
    [Fact]
    public void TheDiagnosticRendersWithNoUnsubstitutedPlaceholders()
    {
        var d = FileAccessDiagnostics.TryDescribe(
            Path.Combine(Home, "Documents", "w", ".cws"),
            new UnauthorizedAccessException("denied"));

        Assert.DoesNotContain("{", d!.Render(), StringComparison.Ordinal);
    }
}

/// <summary>
/// The refusal that follows an update the application applied to ITSELF (owner report, 2026-09-10).
///
/// <para>A workspace on the Desktop and one under Documents were both refused, seconds after an
/// automatic update, by a session whose launch-time identity named the bundle the update had just
/// moved aside. The message it produced was the ordinary one: open System Settings, find circuitRF,
/// switch on "Desktop Folder". There was nothing there to switch on that would have helped — the
/// permission was intact and the session was not. These pin the branch that says so.</para>
/// </summary>
[Collection("FileAccessDiagnostics")]
public sealed class FileAccessDiagnosticsAfterUpdateTests : IDisposable
{
    public void Dispose() => FileAccessDiagnostics.AppBundleReplacedThisSession = false;

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string Describe(string relative)
        => FileAccessDiagnostics.TryDescribe(
               Path.Combine(Home, relative, "My_EM_Simulation_Example", ".cws"),
               new UnauthorizedAccessException("Access to the path is denied."))!.Render();

    /// <summary>Both folders from the report, because the session loses ALL of them at once — the
    /// grant is per folder but the identity that fails to match is one.</summary>
    [Theory]
    [InlineData("Desktop")]
    [InlineData("Documents")]
    public void AfterAnInPlaceUpdate_TheRemedyIsARelaunchAndNotASetting(string folder)
    {
        if (!OperatingSystem.IsMacOS()) return;

        FileAccessDiagnostics.AppBundleReplacedThisSession = true;
        string text = Describe(folder);

        Assert.Contains("updated itself while it was running", text, StringComparison.Ordinal);
        Assert.Contains("Quit circuitRF and open it again", text, StringComparison.Ordinal);

        // The whole point. The previous message's instruction was to go and change a setting that
        // was already correct, which is what the report was spent on.
        Assert.DoesNotContain("switch on", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Full Disk Access", text, StringComparison.Ordinal);
        Assert.Contains("Do not change anything in Privacy & Security", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Off by default and it stays off, or every ordinary privacy refusal starts telling people to
    /// relaunch after an update that never happened.
    /// </summary>
    [Fact]
    public void WithNoUpdateApplied_TheOrdinaryDiagnosticIsUnchanged()
    {
        if (!OperatingSystem.IsMacOS()) return;

        Assert.False(FileAccessDiagnostics.AppBundleReplacedThisSession);
        Assert.DoesNotContain("updated itself", Describe("Documents"), StringComparison.Ordinal);
    }
}
