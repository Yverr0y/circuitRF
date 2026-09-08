using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CircuitRF.Design.Revision;
using CircuitRF.Ui.Messages;
using CircuitRF.Ui.Revision;
using CircuitRF.Ui.ViewModels.Dock;
using Xunit;

namespace CircuitRF.Ui.Tests.Revision;

/// <summary>
/// <b>What Pull Changes actually makes visible</b> (RC-9 R-rc9-6) — and the gap these gates were
/// written to close.
///
/// <para><b>A fetch updates the remote-tracking reference and moves nothing else.</b> Every surface in
/// this feature read <c>HEAD</c>, so the versions a Pull brought down were on the machine, complete,
/// and invisible: the panel showed nothing new and there was nothing to go back to. Three things in
/// the tree asserted otherwise — the sharing partial's own header, <c>versions.md</c>, and the
/// <c>RefreshVersionHistoryPanel()</c> call sitting immediately after the fetch, refreshing a list
/// that could not have changed.</para>
///
/// <para><b>Measured against two real repositories and a real clone</b>, not against a mock: the whole
/// question is what git's own default refspec does with a fetch, and a fixture that answered it
/// ourselves would answer whatever we believed.</para>
///
/// <para>In <see cref="AppDataRootCollection"/> for the reason every gate here is: the identity path
/// redirects the per-user state directory, and that redirection is process-wide.</para>
/// </summary>
[Collection(AppDataRootCollection.Name)]
public class IncomingVersionsTests
{
    /// <summary>
    /// <b>The whole defect, and the whole fix, in one gate.</b> A version kept on the other copy and
    /// fetched is invisible to <see cref="HistoryBrowser.Versions"/> — which is correct, it is not on
    /// this workspace's own line of work — and is listed, marked, by
    /// <see cref="HistoryBrowser.Incoming"/>.
    /// </summary>
    [GitFact]
    public void AVersionKeptOnTheOtherCopyIsListedAfterAPull_AndIsNotConfusedWithOurOwn()
    {
        using var state = new AppDataRootScope();
        using var far   = Librarian();

        string near = CloneOf(far);
        try
        {
            var mine = GitCommand.For(near) ?? throw new InvalidOperationException("no git");

            // Nothing has happened over there yet, so there is nothing incoming. This half matters as
            // much as the other: a panel that always claimed something was waiting would be noise.
            Assert.Empty(HistoryBrowser.Incoming(mine));
            Assert.Single(HistoryBrowser.Versions(mine));

            // The librarian keeps a second version…
            far.Write("Amp/thing.csch", "widened the stub");
            Assert.True(WorkspaceCommit.Commit(far.Git(), "Widened the output stub").Ok);

            // …and nothing changes here until this workspace asks. R-rc9-6: nothing is automatic.
            Assert.Empty(HistoryBrowser.Incoming(mine));

            Assert.True(WorkspaceRemotes.Fetch(mine).Ok);

            var incoming = HistoryBrowser.Incoming(mine);
            var one      = Assert.Single(incoming);
            Assert.Equal("Widened the output stub", one.Title);
            Assert.True(one.OnTheOtherCopy);

            // And it is NOT part of this workspace's own history: HEAD did not move, and the files on
            // disk are still ours. That distinction is what the mark on the row exists to carry.
            Assert.Single(HistoryBrowser.Versions(mine));
            Assert.DoesNotContain(HistoryBrowser.Versions(mine), v => v.OnTheOtherCopy);
            Assert.Equal("the first state", File.ReadAllText(Path.Combine(near, "Amp", "thing.csch")));
        }
        finally { Delete(near); }
    }

    /// <summary>
    /// <b>The panel gets the incoming rows first, marked, with a count.</b> Ordering is not cosmetic
    /// here: an incoming version is newer than everything present almost by definition, and a row
    /// stranded mid-list under a mark nobody would look for there is no better than not showing it.
    /// </summary>
    [GitFact]
    public void ThePanelPutsIncomingVersionsOnTopAndSaysHowMany()
    {
        using var state = new AppDataRootScope();
        using var far   = Librarian();

        string near = CloneOf(far);
        try
        {
            far.Write("Amp/thing.csch", "widened the stub");
            Assert.True(WorkspaceCommit.Commit(far.Git(), "Widened the output stub").Ok);

            var service = new WorkspaceHistoryService(new Sink());
            Assert.True(WorkspaceRemotes.Fetch(GitCommand.For(near)!).Ok);

            var rows = service.VersionRows(near);
            Assert.Equal(2, rows.Count);
            Assert.True(rows[0].Version!.OnTheOtherCopy);
            Assert.False(rows[1].Version!.OnTheOtherCopy);
            Assert.Equal(1, service.IncomingCount(near));

            // RE-POINTED at RC-10's merged panel. The default filter shows versions, so the two rows
            // asserted below are the same two, in the same order, under the same marks.
            var tool = new HistoryTool();
            tool.SetRows(service.Entries(near, HistoryFilter.Default), hasWorkspace: true);

            Assert.True(tool.HasIncoming);
            Assert.Equal(1, tool.IncomingCount);
            Assert.Contains("not here yet", tool.IncomingText, StringComparison.Ordinal);
            Assert.True(tool.Rows[0].OnTheOtherCopy);
            Assert.NotEqual("", tool.Rows[0].OtherCopyNote);

            // Our own version carries no mark and no note — the pair is what makes either readable.
            var ours = tool.Rows.First(r => r.IsVersion && !r.OnTheOtherCopy);
            Assert.Equal("", ours.OtherCopyNote);
        }
        finally { Delete(near); }
    }

    /// <summary>
    /// <b>Going back to an incoming version brings its files in</b> — through RC-5's restore, with no
    /// second implementation and therefore with everything R-rc5-12c guarantees, including a restore
    /// point of the state it replaced. That is what makes the row an offer rather than a notification.
    /// </summary>
    [GitFact]
    public void GoingBackToAnIncomingVersionBringsItsFilesIn_AndKeepsWhatWasHere()
    {
        using var state = new AppDataRootScope();
        using var far   = Librarian();

        string near = CloneOf(far);
        try
        {
            far.Write("Amp/thing.csch", "widened the stub");
            Assert.True(WorkspaceCommit.Commit(far.Git(), "Widened the output stub").Ok);

            var mine    = GitCommand.For(near)!;
            var service = new WorkspaceHistoryService(new Sink());
            Assert.True(WorkspaceRemotes.Fetch(mine).Ok);

            int before = RestorePoints.List(mine).Count;

            var theirs = Assert.Single(HistoryBrowser.Incoming(mine));
            var result = service.GoBackToVersion(near, theirs);

            Assert.True(result is { Ok: true });
            Assert.Equal("widened the stub", File.ReadAllText(Path.Combine(near, "Amp", "thing.csch")));

            // R-rc5-12c: what was here is kept first, so the way back exists before the overwrite does.
            Assert.True(RestorePoints.List(mine).Count > before);
        }
        finally { Delete(near); }
    }

    /// <summary>
    /// <b>A workspace with no other copy has nothing incoming and asks git nothing about it.</b> That
    /// is most workspaces — one designer, one machine, a history circuitRF started — and the panel
    /// must be exactly as it was for them.
    /// </summary>
    [GitFact]
    public void AWorkspaceWithNoOtherCopyHasNothingIncoming()
    {
        using var state = new AppDataRootScope();
        using var ws    = Librarian();

        Assert.Null(WorkspaceRemotes.IncomingRef(ws.Git()));
        Assert.Empty(HistoryBrowser.Incoming(ws.Git()));

        var service = new WorkspaceHistoryService(new Sink());
        Assert.Equal(0, service.IncomingCount(ws.Root));
        Assert.All(service.VersionRows(ws.Root), r => Assert.False(r.Version?.OnTheOtherCopy ?? false));
    }

    // ── Fixtures ──────────────────────────────────────────────────────────────

    /// <summary>The workspace the other copy is made FROM: armed, with one version kept in it.</summary>
    private static GitWorkspace Librarian()
    {
        var ws = new GitWorkspace();
        File.WriteAllText(ws.GlobalConfig,
            "[user]\n\tname = A Designer\n\temail = designer@example.invalid\n");

        var armed = WorkspaceArming.Arm(ws.Root, CheckpointOrigin.SavePoint, true, null, true);
        Assert.True(armed.Armed);

        ws.Write("Amp/thing.csch", "the first state");
        Assert.True(WorkspaceCommit.Commit(ws.Git(), "The first version").Ok);
        return ws;
    }

    /// <summary>A clone of it, which is exactly what File ▸ Clone Workspace… produces.</summary>
    private static string CloneOf(GitWorkspace far)
    {
        string near = Path.Combine(Path.GetTempPath(), "crf-rc9near-" + Guid.NewGuid().ToString("N")[..12]);

        var installation = GitDiscovery.Find(out _)
            ?? throw new InvalidOperationException("no git");

        var cloned = WorkspaceClone.Clone(installation, far.Root, near);
        Assert.True(cloned.Ok, string.Join(" ", cloned.Diagnostics.Select(d => d.Render())));
        Assert.True(cloned.IsWorkspace);
        return near;
    }

    private static void Delete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private sealed class Sink : IMessageSink
    {
        public List<string> Texts { get; } = [];
        public void Post(MessageLevel level, string text, string? filePath = null) => Texts.Add(text);
        public void Clear() => Texts.Clear();
    }
}
