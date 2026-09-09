using CircuitRF.Ui;
using CircuitRF.Ui.Messages;
using CircuitRF.Ui.Updates;

namespace CircuitRF.Ui.Tests.Updates;

/// <summary>
/// The Relaunch action in the Messages panel (owner request, 2026-09-06) — the button on the
/// "updated in the background" line that shuts this session down, starts the version that was
/// installed, and reopens the workspaces that were open.
///
/// <para><b>What it replaced.</b> docs/design/auto-update.md §10 refused a Relaunch button outright,
/// on the grounds that the application can be holding unsaved workspaces and a one-click relaunch
/// invites data loss to save a keystroke. That objection is answered rather than overruled: the button
/// runs the ordinary Quit, so every window is asked about its unsaved work first and any cancelled
/// prompt calls the whole thing off. These pin the parts of that which can be checked without a
/// display — the hand-off note, the successor's argument, and what the announcement actually
/// posts.</para>
/// </summary>
[Collection(AppDataRootCollection.Name)]
public sealed class RelaunchTests : IDisposable
{
    private readonly string _root;

    public RelaunchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "crf-relaunch-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        AppDataRoot.RedirectTo(_root);
    }

    public void Dispose()
    {
        RelaunchRequest.Handler = null;
        AppRelaunch.Launcher    = null;
        AppDataRoot.RedirectTo(null);
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private string MakeWorkspace(string name)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        string cws = Path.Combine(dir, name + ".cws");
        File.WriteAllText(cws, "{}");
        return cws;
    }

    /// <summary>A sink that keeps the action as well as the text, which the default interface
    /// implementation deliberately does not.</summary>
    private sealed class ActionSink : IMessageSink
    {
        public List<(MessageLevel Level, string Text, string? Label)> Posted { get; } = [];
        public Func<Task>? LastAction { get; private set; }

        public void Post(MessageLevel level, string text, string? filePath = null)
            => Posted.Add((level, text, null));

        public void PostAction(MessageLevel level, string text, string actionLabel, Func<Task> action)
        {
            Posted.Add((level, text, actionLabel));
            LastAction = action;
        }

        public void Clear() => Posted.Clear();
    }

    // ── the hand-off note ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The whole point of the feature: what was open comes back. Order is preserved because the
    /// FIRST workspace opens into the first window and the rest get windows of their own
    /// (App.OpenFiles), so it decides which one the user is looking at.
    /// </summary>
    [Fact]
    public void TheWorkspacesOpenAtRelaunch_AreWhatTheNextLaunchReopens()
    {
        string a = MakeWorkspace("alpha");
        string b = MakeWorkspace("beta");

        RelaunchSession.Write([a, b]);

        var taken = RelaunchSession.Take();
        Assert.NotNull(taken);
        Assert.Equal([a, b], taken);
    }

    /// <summary>
    /// <b>Consumed once.</b> A relaunch that crashes partway through reopening must not become a
    /// launch that reopens the same workspaces for ever — the note is deleted as it is read, before
    /// anything is done with it.
    /// </summary>
    [Fact]
    public void TheNoteIsAnsweredExactlyOnce()
    {
        RelaunchSession.Write([MakeWorkspace("alpha")]);

        Assert.Single(RelaunchSession.Take()!);
        Assert.Null(RelaunchSession.Take());
        Assert.False(File.Exists(RelaunchSession.FilePath));
    }

    /// <summary>An ordinary launch. Null, not empty — the difference decides whether the user's
    /// configured launch action runs.</summary>
    [Fact]
    public void AnOrdinaryLaunchHasNoNote() => Assert.Null(RelaunchSession.Take());

    /// <summary>
    /// A relaunch from a window with nothing open still records that it WAS a relaunch. Empty is not
    /// null: without the note the next launch would run the user's start-up action and open their
    /// default workspace, which is not what a restart-to-change-nothing should do.
    /// </summary>
    [Fact]
    public void ARelaunchWithNothingOpen_StillLeavesANote()
    {
        RelaunchSession.Write([]);

        string[]? taken = RelaunchSession.Take();

        Assert.NotNull(taken);
        Assert.Empty(taken);
    }

    /// <summary>
    /// The cancelled-prompt path. A user who answered "cancel" at a save dialog is still working in
    /// this session on this version; a note left behind would reopen these workspaces on top of
    /// whatever they do next time they launch.
    /// </summary>
    [Fact]
    public void CancellingTheRelaunchRemovesTheNote()
    {
        RelaunchSession.Write([MakeWorkspace("alpha")]);
        RelaunchSession.Clear();

        Assert.Null(RelaunchSession.Take());
    }

    /// <summary>
    /// A workspace deleted or unmounted between the two launches is dropped silently. The user asked
    /// to carry on where they left off, not to be told what has changed on disk since — and the
    /// alternative is an error dialog in front of a session they did not choose to start.
    /// </summary>
    [Fact]
    public void AWorkspaceThatIsNoLongerThere_IsDroppedRatherThanReported()
    {
        string gone = MakeWorkspace("gone");
        string kept = MakeWorkspace("kept");
        RelaunchSession.Write([gone, kept]);
        File.Delete(gone);

        var taken = RelaunchSession.Take();
        Assert.NotNull(taken);
        Assert.Equal([kept], taken);
    }

    /// <summary>
    /// Unreadable still means a relaunch happened. Opening nothing is the honest outcome; treating it
    /// as an ordinary launch would run the start-up action over the top of a restart the user asked
    /// for.
    /// </summary>
    [Fact]
    public void AnUnreadableNoteStillCountsAsARelaunch()
    {
        Directory.CreateDirectory(UpdatePaths.Root);
        File.WriteAllText(RelaunchSession.FilePath, "{ not json");

        string[]? taken = RelaunchSession.Take();

        Assert.NotNull(taken);
        Assert.Empty(taken);
        Assert.False(File.Exists(RelaunchSession.FilePath));
    }

    // ── the successor's wait argument ────────────────────────────────────────────────────────

    /// <summary>
    /// <b>The argument that stops the relaunch closing the application instead of restarting it.</b>
    /// Windows holds a Mutex and Linux a flock()ed file for the life of the process, so a successor
    /// that starts while its predecessor is still shutting down takes the "second instance" branch,
    /// forwards its arguments to the instance on its way out, and exits with no window.
    /// </summary>
    [Fact]
    public void TheWaitArgumentIsReadAndStrippedFromTheCommandLine()
    {
        string[] args = [AppRelaunch.WaitForPidArgument, "4321"];

        int? pid = AppRelaunch.TakeWaitForPid(ref args);

        Assert.Equal(4321, pid);
        Assert.Empty(args);
    }

    /// <summary>
    /// It must not disturb anything else on the command line — a relaunch and a file to open can
    /// arrive together on the platforms that pass files in argv.
    /// </summary>
    [Fact]
    public void EverythingElseOnTheCommandLineSurvives()
    {
        string[] args = ["/tmp/one.cws", AppRelaunch.WaitForPidArgument, "77", "/tmp/two.csch"];

        Assert.Equal(77, AppRelaunch.TakeWaitForPid(ref args));
        Assert.Equal(["/tmp/one.cws", "/tmp/two.csch"], args);
    }

    /// <summary>
    /// A malformed pair takes its value with it. Leaving a bare number behind would hand it to the
    /// startup file scan, where it is filtered by File.Exists — by luck rather than by design.
    /// </summary>
    [Fact]
    public void AMalformedWaitArgumentConsumesItsValueAnyway()
    {
        string[] args = [AppRelaunch.WaitForPidArgument, "not-a-pid", "/tmp/one.cws"];

        Assert.Null(AppRelaunch.TakeWaitForPid(ref args));
        Assert.Equal(["/tmp/one.cws"], args);
    }

    [Fact]
    public void AnOrdinaryCommandLineCarriesNoWait()
    {
        string[] args = ["/tmp/one.cws"];

        Assert.Null(AppRelaunch.TakeWaitForPid(ref args));
        Assert.Equal(["/tmp/one.cws"], args);
    }

    /// <summary>
    /// Every reason the predecessor cannot be found means the same thing — it is already gone — and
    /// none of them may block a launch. The one thing this wait protects is a single-instance guard,
    /// which a dead process cannot be holding.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]     // no such process
    public void WaitingOnAProcessThatIsNotThere_ReturnsAtOnce(int pid)
    {
        var start = DateTime.UtcNow;

        AppRelaunch.WaitForProcessExit(pid, timeoutMs: 30_000);

        Assert.True(DateTime.UtcNow - start < TimeSpan.FromSeconds(5));
    }

    /// <summary>Waiting on ourselves would never return, so it is refused rather than attempted.</summary>
    [Fact]
    public void WaitingOnOurOwnProcess_ReturnsAtOnce()
    {
        var start = DateTime.UtcNow;

        AppRelaunch.WaitForProcessExit(Environment.ProcessId, timeoutMs: 30_000);

        Assert.True(DateTime.UtcNow - start < TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// macOS starts the successor through Launch Services for the same reason the update hand-over
    /// does — an inherited launch-time attribution pointing at a bundle the update has replaced is
    /// denied ~/Documents with no prompt (see AppRelaunchTests). The wait argument goes with it.
    /// </summary>
    [Fact]
    public void OnMacOs_TheSuccessorIsAskedForByBundle_AndCarriesTheWait()
    {
        if (!OperatingSystem.IsMacOS()) return;

        string? bundle = null;
        IReadOnlyList<string>? args = null;
        AppRelaunch.Launcher = (b, a) => { bundle = b; args = a; return true; };

        bool started = AppRelaunch.StartSuccessor(
            "/Applications/circuitRF.app/Contents/MacOS/circuitRF");

        Assert.True(started);
        Assert.Equal("/Applications/circuitRF.app", bundle);
        Assert.Equal([AppRelaunch.WaitForPidArgument, Environment.ProcessId.ToString()], args);
    }

    [Fact]
    public void NoExecutable_IsNotAStart() => Assert.False(AppRelaunch.StartSuccessor(""));

    // ── what the Messages panel is actually given ────────────────────────────────────────────

    /// <summary>
    /// With a handler installed the announcement carries the button, and its caption says what it
    /// does.
    /// </summary>
    [Fact]
    public void TheAnnouncementOffersTheRelaunch()
    {
        RelaunchRequest.Handler = () => Task.CompletedTask;
        var sink = new ActionSink();

        UpdateService.PostAnnouncement(sink, "1.0.0-beta.11", "1.0.0-beta.12");

        var (level, text, label) = Assert.Single(sink.Posted);
        Assert.Equal(MessageLevel.Info, level);
        Assert.Equal($"Relaunch {UpdateApp.Name}", label);
        Assert.Contains("1.0.0-beta.11", text);
        Assert.Contains("1.0.0-beta.12", text);
        Assert.NotNull(sink.LastAction);
    }

    /// <summary>
    /// <b>The sentence stands on its own with no button.</b> harmonicaRF and wBond share this update
    /// machinery and install no handler; so does any headless sink. The line must therefore still
    /// tell the user what to do, which is why it says "Relaunch … to start using the version"
    /// whether or not there is something to press — the button removes a keystroke, it is not the
    /// instruction.
    /// </summary>
    [Fact]
    public void WithNoHandlerInstalled_TheSameSentenceIsPostedWithNoButton()
    {
        RelaunchRequest.Handler = null;
        var sink = new ActionSink();

        UpdateService.PostAnnouncement(sink, "1.0.0-beta.11", "1.0.0-beta.12");

        var (level, text, label) = Assert.Single(sink.Posted);
        Assert.Equal(MessageLevel.Info, level);
        Assert.Null(label);
        Assert.Contains($"Relaunch {UpdateApp.Name} to start using the version", text);
    }

    /// <summary>
    /// The wording does not change with the button — a user reading the log later, or copying it
    /// into a report, must see the same sentence either way.
    /// </summary>
    [Fact]
    public void TheSentenceIsTheSameWithAndWithoutTheButton()
    {
        var withButton = new ActionSink();
        RelaunchRequest.Handler = () => Task.CompletedTask;
        UpdateService.PostAnnouncement(withButton, "1.0.0", "1.1.0");

        var without = new ActionSink();
        RelaunchRequest.Handler = null;
        UpdateService.PostAnnouncement(without, "1.0.0", "1.1.0");

        Assert.Equal(withButton.Posted[0].Text, without.Posted[0].Text);
    }

    /// <summary>
    /// Owner request, 2026-09-09: the offer lands on the row the DOWNLOAD was drawn on, where the
    /// progress bar was — not on a row of its own underneath it. So when there is a live row, the
    /// announcement settles it and posts nothing at all to the sink.
    /// </summary>
    [Fact]
    public void WithALiveDownloadRow_TheAnnouncementSettlesThatRow_AndPostsNoSecondOne()
    {
        RelaunchRequest.Handler = () => Task.CompletedTask;
        var sink = new ActionSink();
        var row  = new RecordingProgressMessage();

        UpdateService.PostAnnouncement(sink, "1.0.0-beta.11", "1.0.0-beta.12", row);

        Assert.Empty(sink.Posted);
        var (level, text, label) = Assert.Single(row.CompletedWithAction);
        Assert.Equal(MessageLevel.Info, level);
        Assert.Equal($"Relaunch {UpdateApp.Name}", label);
        Assert.Contains("1.0.0-beta.12", text);
        Assert.NotNull(row.LastAction);

        // Settled through the WITH-ACTION path only: a plain Complete as well would mean the row
        // was written twice, and which write landed last would decide whether the button exists.
        Assert.Empty(row.Completed);
    }

    /// <summary>
    /// The same row, in a build with no handler installed — harmonicaRF, wBond. It settles into the
    /// sentence with no button, which is the point of the sentence standing on its own.
    /// </summary>
    [Fact]
    public void WithALiveRowAndNoHandler_TheRowSettlesIntoTheSentenceWithNoButton()
    {
        RelaunchRequest.Handler = null;
        var sink = new ActionSink();
        var row  = new RecordingProgressMessage();

        UpdateService.PostAnnouncement(sink, "1.0.0", "1.1.0", row);

        Assert.Empty(sink.Posted);
        Assert.Empty(row.CompletedWithAction);
        var (level, text) = Assert.Single(row.Completed);
        Assert.Equal(MessageLevel.Info, level);
        Assert.Contains($"Relaunch {UpdateApp.Name} to start using the version", text);
    }

    /// <summary>
    /// The wording does not change with the row either: the same sentence whether it settles a live
    /// row or is posted on its own, for the same reason it is the same with and without the button.
    /// </summary>
    [Fact]
    public void TheSentenceIsTheSameOnALiveRowAsOnItsOwn()
    {
        RelaunchRequest.Handler = () => Task.CompletedTask;

        var posted = new ActionSink();
        UpdateService.PostAnnouncement(posted, "1.0.0", "1.1.0");

        var row = new RecordingProgressMessage();
        UpdateService.PostAnnouncement(new ActionSink(), "1.0.0", "1.1.0", row);

        Assert.Equal(posted.Posted[0].Text, row.CompletedWithAction[0].Text);
    }

    /// <summary>
    /// A sink that has not heard of actions loses the button and keeps the message. This is the
    /// interface default, and it is what lets the message model gain an action without every
    /// existing sink changing.
    /// </summary>
    [Fact]
    public void ASinkWithNoActionSupport_StillGetsTheMessage()
    {
        RelaunchRequest.Handler = () => Task.CompletedTask;
        var plain = new PlainSink();

        UpdateService.PostAnnouncement(plain, "1.0.0", "1.1.0");

        Assert.Contains("updated from 1.0.0 to 1.1.0", Assert.Single(plain.Posted));
    }

    private sealed class PlainSink : IMessageSink
    {
        public List<string> Posted { get; } = [];
        public void Post(MessageLevel level, string text, string? filePath = null) => Posted.Add(text);
        public void Clear() => Posted.Clear();
    }

    // ── the message row itself ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The view's only visibility test. A caption with no callback, or a callback with no caption, is
    /// half a button and must not render as one.
    /// </summary>
    [Fact]
    public void ARowShowsAnActionButtonOnlyWhenItHasBothHalves()
    {
        Assert.True(new MessageEntry(MessageLevel.Info, "x", null, DateTime.Now,
                                     "Do it", () => Task.CompletedTask).HasAction);

        Assert.False(new MessageEntry(MessageLevel.Info, "x", null, DateTime.Now).HasAction);
        Assert.False(new MessageEntry(MessageLevel.Info, "x", null, DateTime.Now,
                                      "Do it", null).HasAction);
        Assert.False(new MessageEntry(MessageLevel.Info, "x", null, DateTime.Now,
                                      "", () => Task.CompletedTask).HasAction);
    }
}
