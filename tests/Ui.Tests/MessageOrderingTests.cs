using System;
using System.Collections.ObjectModel;
using System.Linq;
using CircuitRF.Ui.Messages;
using Xunit;

namespace CircuitRF.Ui.Tests;

/// <summary>
/// Owner request, 2026-09-13: "If a new message appears in the Message panel, make sure it scrolls to
/// the bottom. (And keep the simulation progress bar at the bottom of the messages list)."
///
/// <para>The scroll itself is the view's, and it is driven by <c>CollectionChanged</c> — which an
/// Insert raises exactly as an Add does. What is testable without a window is the ORDER, which is the
/// half that was actually wrong: a run posts notes while it runs, and appending them walked the live
/// row up the log.</para>
/// </summary>
public sealed class MessageOrderingTests
{
    private static readonly Action<Action> Inline = a => a();

    private static MessageEntry Plain(string text) =>
        new(MessageLevel.Info, text, null, DateTime.Now);

    private static (LiveProgressMessage Live, MessageEntry Entry) Live(string text)
    {
        var e = new MessageEntry(MessageLevel.Info, text, null, DateTime.Now)
        {
            ProgressIndeterminate = true,
            ProgressPercent       = 0,
            IsLiveProgress        = true,
        };
        return (new LiveProgressMessage(e, Inline), e);
    }

    [Fact]
    public void AMessagePostedDuringARunGoesABOVETheRunsOwnRow()
    {
        var log = new ObservableCollection<MessageEntry>();

        MessageOrdering.Insert(log, Plain("Opened 'board.clay'"));
        var (_, bar) = Live("Simulating 'board' — EM");
        log.Add(bar);                                   // BeginProgress appends; only Post inserts

        // …and now the run talks, which is what an EM sweep does for minutes at a time.
        MessageOrdering.Insert(log, Plain("note: port 1 resolved to 4 basis functions"));
        MessageOrdering.Insert(log, Plain("note: 0 Hz is solved as a conduction network"));

        Assert.Equal(
            ["Opened 'board.clay'",
             "note: port 1 resolved to 4 basis functions",
             "note: 0 Hz is solved as a conduction network",
             "Simulating 'board' — EM"],
            log.Select(m => m.Text).ToArray());

        // The bar is the LAST row, which is the row scroll-to-end brings into view.
        Assert.Same(bar, log[^1]);
    }

    [Fact]
    public void TwoLiveRowsOfOneRunStayTogetherAtTheBottom_InTheOrderTheyWereCreated()
    {
        var log = new ObservableCollection<MessageEntry>();
        var (_, sweep) = Live("Simulating 'board'");
        var (_, stage) = Live("10 GHz — solving the structure");
        log.Add(sweep);
        log.Add(stage);

        MessageOrdering.Insert(log, Plain("note: mesh N = 3,017"));

        Assert.Equal(["note: mesh N = 3,017", "Simulating 'board'", "10 GHz — solving the structure"],
                     log.Select(m => m.Text).ToArray());
    }

    [Fact]
    public void TheEmRunsOwnShape_StageRowSettledFirst_StillKeepsTheBarAtTheBOTTOM()
    {
        // Exactly what WorkspaceViewModel.RunEmSetup does: two live rows, the STAGE one settled the
        // moment the sweep ends, then the run's notes, then the sweep row's own summary. A rule that
        // only skipped a TRAILING block of live rows would break on the settled stage row and put
        // every note below the bar.
        var log = new ObservableCollection<MessageEntry>();
        MessageOrdering.Insert(log, Plain("Opened 'board.cws'"));

        var (sweep, sweepRow) = Live("EM 'board'");
        var (stage, stageRow) = Live("EM 'board' — starting");
        log.Add(sweepRow);
        log.Add(stageRow);

        stage.Complete(MessageLevel.Info, "EM 'board' — solve finished");
        MessageOrdering.Insert(log, Plain("note: port 1 resolved"));
        MessageOrdering.Insert(log, Plain("note: 0 Hz is solved as a conduction network"));
        sweep.Finish(MessageLevel.Info, "3 points", keepBar: false);

        Assert.Equal(
            ["Opened 'board.cws'",
             "note: port 1 resolved",
             "note: 0 Hz is solved as a conduction network",
             "EM 'board' - 3 points",
             "EM 'board' — solve finished"],
            log.Select(m => m.Text).ToArray());
    }

    [Fact]
    public void ONCETHERUNHASSETTLED_NothingIsInsertedAboveItAgain()
    {
        var log = new ObservableCollection<MessageEntry>();
        var (live, bar) = Live("Simulating 'board'");
        log.Add(bar);
        MessageOrdering.Insert(log, Plain("note: during the run"));

        // Finish, the way every long-running row in this application finishes.
        live.Finish(MessageLevel.Info, "done", keepBar: false);
        Assert.False(bar.IsLiveProgress);

        MessageOrdering.Insert(log, Plain("after the run"));

        Assert.Equal(["note: during the run", "Simulating 'board' - done", "after the run"],
                     log.Select(m => m.Text).ToArray());
    }

    [Fact]
    public void LivenessIsNOTWhetherABarIsDrawn()
    {
        // The two part company at both ends, which is why IsLiveProgress exists rather than a read of
        // HasProgress. `Finish(keepBar: true)` settles the operation and KEEPS the bar; a row that
        // keeps a full bar as its own "how much work that was" summary must not go on holding
        // messages above it for the rest of the session.
        var log = new ObservableCollection<MessageEntry>();
        var (live, bar) = Live("Downloading");
        log.Add(bar);

        live.Finish(MessageLevel.Info, "complete", keepBar: true);
        Assert.True(bar.HasProgress);
        Assert.False(bar.IsLiveProgress);

        MessageOrdering.Insert(log, Plain("next"));
        Assert.Same(bar, log[0]);
        Assert.Equal("next", log[^1].Text);
    }

    [Fact]
    public void CompleteWithActionSettlesItToo_SoTheUpdateAnnouncementDoesNotPINTheLog()
    {
        var log = new ObservableCollection<MessageEntry>();
        var (live, bar) = Live("Downloading circuitRF 1.0.0");
        log.Add(bar);

        live.CompleteWithAction(MessageLevel.Info, "circuitRF 1.0.0 is ready",
                                "Relaunch circuitRF", () => System.Threading.Tasks.Task.CompletedTask);
        Assert.False(bar.IsLiveProgress);

        MessageOrdering.Insert(log, Plain("after"));
        Assert.Equal("after", log[^1].Text);
    }

    [Fact]
    public void AnEmptyLogAndALogWithNoLiveRowBothJustAPPEND()
    {
        var log = new ObservableCollection<MessageEntry>();
        Assert.Equal(0, MessageOrdering.InsertIndexFor(log));

        MessageOrdering.Insert(log, Plain("one"));
        MessageOrdering.Insert(log, Plain("two"));
        Assert.Equal(["one", "two"], log.Select(m => m.Text).ToArray());
    }
}
