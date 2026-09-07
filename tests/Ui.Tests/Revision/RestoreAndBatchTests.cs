using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CircuitRF.Design.Revision;
using CircuitRF.Ui.Archive;
using Xunit;
using static CircuitRF.Ui.Tests.Revision.RestorePointsTests;

namespace CircuitRF.Ui.Tests.Revision;

/// <summary>
/// RC-5's restore, batch, archive and headless gates —
/// <c>docs/sonnet-briefs/brief-revision-control-5-checkpoints.md</c> §8, gates 2, 6, 11, 12, 15,
/// 17a, 17b, 17c, 17d, 18, 19, 20, 22, 23.
/// </summary>
[Collection(AppDataRootCollection.Name)]
public class RestoreAndBatchTests
{
    // ── Gate 15: a restore records the state it is replacing, first ──────────────────────────────

    /// <summary>
    /// R-rc5-12a. <b>The state being replaced may never have been recorded</b> — by §5.3's design it
    /// is whatever the designer has done since the last boundary, which can be a whole afternoon. A
    /// restore that discarded it would be an automatic operation destroying history, and it is what
    /// makes the operation symmetric rather than a second cliff.
    /// </summary>
    [GitFact]
    public void ARestoreRecordsWhatItIsAboutToReplace()
    {
        using var ws = Armed();
        var git = ws.Git();

        ws.Write("cells/a/thing.csch", "tuesday");
        var tuesday = Take(ws, "as it was on Tuesday");

        // A whole afternoon's work that no boundary has seen.
        ws.Write("cells/a/thing.csch", "thursday afternoon");
        ws.Write("cells/e/new-cell.csch", "also thursday");

        var result = WorkspaceRestore.Restore(git, tuesday);
        Assert.True(result.Ok);
        Assert.NotNull(result.PreRestore);

        Assert.Equal("tuesday", File.ReadAllText(ws.File_("cells/a/thing.csch")));

        // And the afternoon is itself restorable — the difference between a safety net and a second
        // cliff.
        var back = RestorePoints.List(git).Single(p => p.Sequence == result.PreRestore!.Sequence);
        Assert.True(WorkspaceRestore.Restore(git, back).Ok);
        Assert.Equal("thursday afternoon", File.ReadAllText(ws.File_("cells/a/thing.csch")));
        Assert.True(File.Exists(ws.File_("cells/e/new-cell.csch")));
    }

    // ── Gate 17a: what a restore takes away, and what it never touches ──────────────────────────

    /// <summary>
    /// R-rc5-12c. <b>A workspace holding last Tuesday's files plus Thursday's new cell matches
    /// neither state</b> — so what came after goes. <b>Results are not touched</b>: a restore that
    /// swept them away would destroy hours of simulation to bring back the design that produced them.
    /// </summary>
    [GitFact]
    public void ARestoreRemovesWhatCameAfterAndLeavesWhatWasNeverIn()
    {
        using var ws = Armed();
        var git = ws.Git();

        ws.Write("cells/a/thing.csch", "tuesday");
        var tuesday = Take(ws, "as it was on Tuesday");

        ws.Write("cells/e/new-cell.csch", "created after");
        ws.Write("results/sweep.npy", "hours of simulation");
        byte[] resultBytes = File.ReadAllBytes(ws.File_("results/sweep.npy"));

        Assert.True(WorkspaceRestore.Restore(git, tuesday).Ok);

        Assert.False(File.Exists(ws.File_("cells/e/new-cell.csch")));
        Assert.False(Directory.Exists(Path.Combine(ws.Root, "cells", "e")));

        Assert.True(File.Exists(ws.File_("results/sweep.npy")));
        Assert.Equal(resultBytes, File.ReadAllBytes(ws.File_("results/sweep.npy")));
    }

    // ── Gate 17b: the recording flag and the policy files survive ───────────────────────────────

    /// <summary>
    /// R-rc5-12c. <b>A restore is the user's decision about CONTENT, never about RECORDING.</b> The
    /// workspace file carries the off flag, so a restore across an off period would otherwise switch
    /// recording on or off silently; <c>.gitignore</c> carries §8.2's answers, so a restore to before
    /// one was given would silently start including the file it excluded.
    /// </summary>
    [GitFact]
    public void ARestorePreservesTheRecordingFlagAndThePolicyFiles()
    {
        using var ws = Armed();
        var git = ws.Git();

        ws.Write("cells/a/thing.csch", "before");
        var before = Take(ws, "before either answer was given");

        // Both decisions taken AFTER the entry being restored to.
        WorkspaceRevisionSetting.Write(WorkspaceRevisionSetting.CwsPathFor(ws.Root), false);
        LargeFileGuard.AppendIgnorePattern(ws.Root, "*.gds");
        ws.Write("cells/a/thing.csch", "after");

        Assert.True(WorkspaceRestore.Restore(git, before).Ok);

        // The design came back...
        Assert.Equal("before", File.ReadAllText(ws.File_("cells/a/thing.csch")));

        // ...and the two decisions did not.
        Assert.False(WorkspaceRevisionSetting.Read(WorkspaceRevisionSetting.CwsPathFor(ws.Root)));
        Assert.Contains("*.gds", File.ReadAllText(Path.Combine(ws.Root, ".gitignore")));
    }

    // ── Gate 17c: an interrupted restore is reported ────────────────────────────────────────────

    /// <summary>
    /// R-rc5-12c. What a crash mid-restore leaves is §1.3's failure exactly: a workspace that opens,
    /// is well-formed, and is half of two states. <b>The report names BOTH ends</b>, because one that
    /// named only the destination would leave a designer who has changed their mind with nothing to
    /// go back to.
    /// </summary>
    [GitFact]
    public void AnInterruptedRestoreIsFoundOnTheNextOpenAndCanBeFinished()
    {
        using var ws = Armed();
        var git = ws.Git();

        ws.Write("cells/a/one.csch", "tuesday-a");
        ws.Write("cells/b/two.csch", "tuesday-b");
        ws.Write("cells/c/three.csch", "tuesday-c");
        var tuesday = Take(ws, "as it was on Tuesday");

        ws.Write("cells/a/one.csch",   "thursday-a");
        ws.Write("cells/b/two.csch",   "thursday-b");
        ws.Write("cells/c/three.csch", "thursday-c");
        Take(ws, "thursday");

        // Interrupted through the seam, after the first file and before the rest — where a dropped
        // connection over a share would cut it.
        WorkspaceRestore.FilesPerWrite     = 1;
        WorkspaceRestore.AfterFilesWritten = _ => throw new IOException("the share went away");
        try
        {
            Assert.Throws<IOException>(() => WorkspaceRestore.Restore(git, tuesday));
        }
        finally
        {
            WorkspaceRestore.FilesPerWrite     = 0;
            WorkspaceRestore.AfterFilesWritten = null;
        }

        // The marker is there, and it names both ends.
        var inFlight = RestoreMarker.Read(ws.Root);
        Assert.NotNull(inFlight);
        Assert.Equal("as it was on Tuesday", inFlight!.Target.Label);
        Assert.Equal("thursday", inFlight.Fallback.Label);

        // Finishing produces the target state, and clears the marker.
        var finished = WorkspaceRestore.Finish(git, inFlight);
        Assert.True(finished.Ok);
        Assert.Null(RestoreMarker.Read(ws.Root));

        Assert.Equal("tuesday-a", File.ReadAllText(ws.File_("cells/a/one.csch")));
        Assert.Equal("tuesday-b", File.ReadAllText(ws.File_("cells/b/two.csch")));
        Assert.Equal("tuesday-c", File.ReadAllText(ws.File_("cells/c/three.csch")));
    }

    // ── Gate 17d: a restore never moves HEAD, and never branches ────────────────────────────────

    /// <summary>
    /// R-rc5-12c, R-rc5-13. <b>Restore-then-keep-editing is linear.</b> rev 4 said git requires a
    /// second line of work here and specified a silently-created one; it does not — that is true only
    /// of a checkout, and this never checks anything out.
    /// </summary>
    [GitFact]
    public void ARestoreLeavesHeadAndTheDesignersOwnHistoryExactlyWhereTheyWere()
    {
        using var ws = Armed();
        var git = ws.Git();

        // A Stage 3 shape: the designer has their own commits on their own branch.
        ws.Write("cells/a/thing.csch", "one");
        ws.Raw("add", ".");
        ws.Raw("commit", "-m", "the designer's own");

        string head   = ws.Raw("rev-parse", "HEAD").Out.Trim();
        string branch = ws.Raw("rev-parse", "--abbrev-ref", "HEAD").Out.Trim();
        string heads  = ws.Raw("for-each-ref", "refs/heads/").Out;

        var first = Take(ws, "before");
        ws.Write("cells/a/thing.csch", "two");
        Take(ws, "after");

        Assert.True(WorkspaceRestore.Restore(git, first).Ok);

        // And a further boundary after going back records the restored content as the next step.
        ws.Write("cells/a/thing.csch", "three");
        Assert.True(WorkspaceCheckpoints.Take(git, CheckpointOrigin.SavePoint, "kept going").Recorded);

        Assert.Equal(head,   ws.Raw("rev-parse", "HEAD").Out.Trim());
        Assert.Equal(branch, ws.Raw("rev-parse", "--abbrev-ref", "HEAD").Out.Trim());
        Assert.Equal(heads,  ws.Raw("for-each-ref", "refs/heads/").Out);

        // Nothing detached anything either.
        Assert.NotEqual("HEAD", branch);
    }

    // ── Gate 18 / 19: the batch protocol and its four refusals ──────────────────────────────────

    /// <summary>
    /// R-rc5-6c, R-rc5-6d. <b>A batch opened twice is one batch and one entry</b> — two entries around
    /// one logical action is the unreadable log §5.3 rejects, and the designer's question has exactly
    /// one answer. <b>A batch never closed still leaves a usable restore point</b>: the safety net must
    /// have no failure mode that depends on an agent behaving well.
    /// </summary>
    [GitFact]
    public void ABatchOpenedTwiceIsOneBatchAndOneEntryAndNeverClosingLosesNothing()
    {
        using var ws = Armed();
        var git = ws.Git();

        ws.Write("cells/a/thing.csch", "before the agent");

        var session = new BatchSession { KeepHistoryPreference = true };

        var first = session.Open(ws.Root, "widen the output match");
        Assert.True(first.Ok);
        Assert.False(first.AlreadyOpen);
        Assert.NotNull(first.Point);

        var second = session.Open(ws.Root, "and something else");
        Assert.True(second.Ok);
        Assert.True(second.AlreadyOpen);
        Assert.Null(second.Point);

        Assert.Single(CheckpointReferences.List(git));

        // The agent dies here. Nothing closes the batch — and the restore point is exactly where it
        // should be.
        var point = RestorePoints.List(git).Single();
        Assert.Equal(CheckpointOrigin.BeforeBatch, point.Origin);
        Assert.Equal("before: widen the output match", point.Label);

        ws.Write("cells/a/thing.csch", "what the agent did");
        Assert.True(WorkspaceRestore.Restore(git, point).Ok);
        Assert.Equal("before the agent", File.ReadAllText(ws.File_("cells/a/thing.csch")));
    }

    /// <summary>
    /// R-rc5-6f, R-rc5-20. <b>Off, held and nowhere-to-record resolve the same way at the same
    /// moment</b> — before anything is modified, because a floor announced after the fall is not a
    /// floor. <b>File mtimes are asserted, not just the refusal</b>: "nothing was modified" is the
    /// promise, and a refusal that had already written something would still read as a refusal.
    /// </summary>
    [GitFact]
    public void ABatchAgainstAnOffHeldOrUnsavedWorkspaceIsRefusedWithNothingModified()
    {
        using var ws = Armed();

        ws.Write("cells/a/thing.csch", "untouched");
        string file = ws.File_("cells/a/thing.csch");
        var before  = File.GetLastWriteTimeUtc(file);

        // ── off ──
        WorkspaceRevisionSetting.Write(WorkspaceRevisionSetting.CwsPathFor(ws.Root), false);
        var off = new BatchSession { KeepHistoryPreference = true }.Open(ws.Root, "anything");
        Assert.False(off.Ok);
        Assert.Equal("revision.batch.refused.off", off.Refusal!.Id);
        WorkspaceRevisionSetting.Write(WorkspaceRevisionSetting.CwsPathFor(ws.Root), null);

        // ── held: a repository circuitRF does not manage ──
        ws.Raw("config", "--unset", "circuitrf.managed");
        var held = new BatchSession { KeepHistoryPreference = true }.Open(ws.Root, "anything");
        Assert.False(held.Ok);
        Assert.Equal("revision.batch.refused.held", held.Refusal!.Id);
        ws.Raw("config", "circuitrf.managed", "true");

        // ── nowhere to record: a workspace with no folder yet ──
        var scratch = new BatchSession { KeepHistoryPreference = true }.Open(null, "anything");
        Assert.False(scratch.Ok);
        Assert.Equal("revision.batch.refused.scratch", scratch.Refusal!.Id);
        // R-rc5-20: the remedy is OFFERED, not merely named.
        Assert.Contains("Save the workspace", scratch.Refusal.Render(), StringComparison.Ordinal);

        // ── a window holding unsaved changes ──
        var dirty = new BatchSession
        {
            KeepHistoryPreference   = true,
            WindowHasUnsavedChanges = _ => true,
        }.Open(ws.Root, "anything");

        Assert.False(dirty.Ok);
        Assert.Equal("revision.batch.refused.unsaved", dirty.Refusal!.Id);

        // Nothing was modified by any of the four.
        Assert.Equal(before, File.GetLastWriteTimeUtc(file));
        Assert.Equal("untouched", File.ReadAllText(file));
    }

    /// <summary>
    /// R-rc5-7b. <b>The modified set is MEASURED, not declared</b> — an agent that forgot to mention a
    /// file it wrote cannot leave that file's window showing the old content.
    /// </summary>
    [GitFact]
    public void ABatchCloseReportsWhatItActuallyChanged()
    {
        using var ws = Armed();

        ws.Write("cells/a/thing.csch", "before");
        ws.Write("cells/b/other.csch", "untouched");

        var session = new BatchSession { KeepHistoryPreference = true };
        Assert.True(session.Open(ws.Root, "widen the output match").Ok);

        ws.Write("cells/a/thing.csch", "after");
        ws.Write("cells/c/created.csch", "brand new");

        var closed = session.Close();
        Assert.True(closed.WasOpen);
        Assert.Contains("cells/a/thing.csch", closed.Modified);
        Assert.Contains("cells/c/created.csch", closed.Modified);
        Assert.DoesNotContain("cells/b/other.csch", closed.Modified);

        // R-rc5-6d: closing one that is not open is not an error.
        Assert.False(session.Close().WasOpen);
    }

    /// <summary>
    /// R-rc5-11. A skipped batch is reported <b>once per session</b>, however many are skipped:
    /// enough to be unmissable, not so often that the panel becomes noise.
    /// </summary>
    [Fact]
    public void ASkippedBatchIsReportedOncePerSession()
    {
        var session = new BatchSession();
        Assert.True(session.ShouldReportSkip());
        Assert.False(session.ShouldReportSkip());
        Assert.False(session.ShouldReportSkip());
    }

    // ── Gate 20: serve advertises the state and publishes the rules ─────────────────────────────

    /// <summary>
    /// R-rc5-6g, R-rc5-6h. <b>A rule an agent cannot read is a rule that does not exist</b>, so the
    /// ten travel with the state rather than living only in a document. And the state is reported for
    /// each of the four, because an agent left to deduce it from the filesystem will deduce it wrongly.
    /// </summary>
    [GitFact]
    public void TheStateIsReportedForAllFourAndTheTenRulesAreInTheSurfacesOwnOutput()
    {
        Assert.Equal(10, AgentContract.Rules.Count);

        // The load-bearing pair, present in the words §5.3b uses.
        Assert.Contains(AgentContract.Rules, r => r.Contains("Do not commit", StringComparison.Ordinal));
        Assert.Contains(AgentContract.Rules, r => r.Contains("stop and say so", StringComparison.Ordinal));
        Assert.Contains(AgentContract.Rules, r => r.Contains("improvised backup", StringComparison.Ordinal));
        Assert.Contains(AgentContract.Rules, r => r.Contains("Never report a checkpoint you did not take",
                                                            StringComparison.Ordinal));

        using var ws = Armed();

        // on
        Assert.Equal(RevisionAvailability.On, AgentContract.StateOf(ws.Root, true, null));

        // off — the per-workspace flag outranks the preference
        Assert.Equal(RevisionAvailability.Off, AgentContract.StateOf(ws.Root, true, false));

        // held — a repository circuitRF does not manage
        ws.Raw("config", "--unset", "circuitrf.managed");
        Assert.Equal(RevisionAvailability.Held, AgentContract.StateOf(ws.Root, true, null));
        ws.Raw("config", "circuitrf.managed", "true");

        // unavailable — nowhere to record into
        Assert.Equal(RevisionAvailability.Unavailable, AgentContract.StateOf(null, true, null));

        // The surface publishes them: the tool's own description points at the batch, and the server
        // emits one entry per rule.
        string surface = StripComments(ReadSource("src/Cli/Serve/HistoryBatch.cs"));
        Assert.Contains("AgentContract.Rules", surface, StringComparison.Ordinal);
        Assert.Contains("BatchRule", surface, StringComparison.Ordinal);
    }

    // ── Gate 11 / 12: the archive and the copy ──────────────────────────────────────────────────

    /// <summary>
    /// R-rc5-18. <b>Excluded in the brief that first puts one inside the folder the scanner
    /// walks.</b> Without it an archive would carry every earlier version of every file kept, and
    /// files no longer in the workspace at all — a designer who imported a customer's artwork,
    /// deleted it, and archived the workspace for a different customer would ship that artwork with
    /// nothing in the visible tree showing it. <b>That is disclosure, and it cannot be undone.</b>
    /// </summary>
    [Fact]
    public void TheHistoryIsExcludedFromAnArchive()
    {
        Assert.True(WorkspaceArchiveScanner.IsSkipped(".git/config"));
        Assert.True(WorkspaceArchiveScanner.IsSkipped(".git/objects/ab/cdef"));
        Assert.True(WorkspaceArchiveScanner.IsSkipped("cells/a/.git/HEAD"));
        Assert.True(WorkspaceArchiveScanner.IsSkippedFromArchive(".git/config"));

        // The policy files are NOT the history and travel with the workspace.
        Assert.False(WorkspaceArchiveScanner.IsSkipped(".gitignore"));
        Assert.False(WorkspaceArchiveScanner.IsSkipped(".gitattributes"));
    }

    /// <summary>
    /// R-rc5-19. The exclusion is shared, so a Save Workspace As copy stops carrying the history too —
    /// which is right for §9A.1's identical reason. <b>The silence was not right</b>: Save As is the
    /// only one of the three journeys with no dialog to read, so the copy's own report says it.
    ///
    /// <para>The absence was already true the moment the history joined the shared skip list; the
    /// SENTENCE is the part that is this brief's, which is why it is asserted rather than the absence
    /// alone.</para>
    /// </summary>
    [Fact]
    public void ACopyStartsItsOwnHistoryAndTheReportSaysSo()
    {
        // The one predicate a copy filters through.
        Assert.True(WorkspaceArchiveScanner.IsSkipped(".git/config"));

        string sentence = RestorePointMessages.CopyStartsItsOwnHistory;
        Assert.Contains("starts a history of its own", sentence, StringComparison.Ordinal);
        Assert.Contains("original keeps every restore point", sentence, StringComparison.Ordinal);

        // And the copy's report posts it.
        string source = StripComments(ReadSource("src/Ui/ViewModels/WorkspaceViewModel.cs"));
        Assert.Contains("CopyStartsItsOwnHistory", source, StringComparison.Ordinal);
    }

    // ── Gate 2: no git vocabulary ───────────────────────────────────────────────────────────────

    /// <summary>
    /// R-rc0-6, R-rc5-2. <b>Every user-visible string this brief adds</b>, scanned with comments
    /// stripped. A designer meets this feature as restore points and nothing else.
    /// </summary>
    [Fact]
    public void NoUserVisibleStringCarriesGitVocabulary()
    {
        string[] paths =
        [
            "src/Design/Revision/RestorePointMessages.cs",
            "src/Design/Revision/CheckpointMessage.cs",
            "src/Ui/ViewModels/Dock/RestorePointsTool.cs",
            "src/Ui/Views/Revision/RestorePointsToolView.axaml",
            "src/Ui/Views/Dialogs/KeepThisStateDialog.axaml",
        ];

        // The words the architecture forbids reaching a designer. "commit" is checked as a whole word
        // so "committed" and "committer" in a comment cannot pass or fail it by accident — the strings
        // themselves are what is being read.
        string[] forbidden = ["branch", "checkout", "HEAD", "commit", "detached", "stash", "repository"];

        foreach (string path in paths)
        {
            string source = StripComments(ReadSource(path));

            foreach (string word in forbidden)
                foreach (System.Text.RegularExpressions.Match literal in Regex.Matches(source, "\"([^\"\\\\]|\\\\.)*\""))
                    Assert.False(
                        Regex.IsMatch(literal.Value, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase),
                        $"{path} shows the word '{word}' to a designer: {literal.Value}");
        }
    }

    /// <summary>
    /// The same rule for the ten rules and the batch's own refusals, which ARE addressed to an agent
    /// and therefore MAY use git's own words — §0's qualification, stated here so the exemption is a
    /// decision rather than an omission. <b>What may not appear there is a refusal with no remedy.</b>
    /// </summary>
    [Fact]
    public void TheAgentFacingRulesMayNameGitAndEveryRefusalNamesWhatToDoInstead()
    {
        Assert.Contains(AgentContract.Rules, r => r.Contains("git", StringComparison.OrdinalIgnoreCase));

        foreach (var refusal in new[]
                 {
                     RestorePointMessages.RecordingOff(),
                     RestorePointMessages.RecordingHeld(),
                     RestorePointMessages.ScratchHasNowhereToRecord(),
                     RestorePointMessages.WindowHasUnsavedChanges(),
                     RestorePointMessages.NoGitHere(),
                 })
        {
            string text = refusal.Render();
            Assert.Contains("Nothing was changed", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── Gate 6 / 23: the headless spelling is the same operation ────────────────────────────────

    /// <summary>
    /// R-rc5-23, gate 6. <b>The CLI's entry is byte-identical to the in-process one</b> for the same
    /// workspace, exempting only the timestamp the object carries by design — which is named here
    /// rather than left as a diff nobody explains.
    /// </summary>
    [GitFact]
    public void TheHeadlessCheckpointIsByteIdenticalToTheInProcessOne()
    {
        Assert.NotNull(CliDll);

        using var a = Armed();
        using var b = Armed();
        using var appData = new AppDataRootScope();

        foreach (var ws in new[] { a, b })
        {
            ws.Write("cells/a/thing.csch", "identical content");
            ws.Write("cells/b/other.csch", "and more of it");
        }

        var inProcess = WorkspaceCheckpoints.Take(a.Git(), CheckpointOrigin.SavePoint, "widen the output match");
        Assert.True(inProcess.Recorded);

        var cli = RunCli(b, ["history", "checkpoint", b.Root, "--intent", "widen the output match"]);
        Assert.Equal(0, cli.Code);

        var headless = RestorePoints.List(b.Git()).Single();

        // The state itself: identical trees, byte for byte.
        Assert.Equal(inProcess.Point!.TreeId, headless.TreeId);

        // And the message, with the timestamp exempted — the object carries one by design and two
        // recordings a moment apart legitimately differ in it.
        Assert.Equal(WithoutTimestamps(a.Raw("cat-file", "-p", inProcess.Point.CommitId).Out),
                     WithoutTimestamps(b.Raw("cat-file", "-p", headless.CommitId).Out));
    }

    /// <summary>
    /// R-rc5-23. <c>list</c> and <c>restore</c> answer what the in-process path answers, and
    /// <b>the view model kept no second copy of the operation</b> — an operation that lives only in a
    /// view model is not a capability, and a verb that re-implements one diverges from it silently.
    /// </summary>
    [GitFact]
    public void ListAndRestoreAnswerWhatTheInProcessPathDoesAndNothingWasReimplemented()
    {
        Assert.NotNull(CliDll);

        using var ws = Armed();
        using var appData = new AppDataRootScope();

        ws.Write("cells/a/thing.csch", "tuesday");
        WorkspaceCheckpoints.Take(ws.Git(), CheckpointOrigin.SavePoint, "as it was on Tuesday");
        ws.Write("cells/a/thing.csch", "thursday");
        WorkspaceCheckpoints.Take(ws.Git(), CheckpointOrigin.SavePoint, "thursday");

        var listed = RunCli(ws, ["history", "list", ws.Root]);
        Assert.Equal(0, listed.Code);
        Assert.Contains("as it was on Tuesday", listed.Out, StringComparison.Ordinal);
        Assert.Contains("thursday", listed.Out, StringComparison.Ordinal);

        var restored = RunCli(ws, ["history", "restore", ws.Root, "--point", "1"]);
        Assert.Equal(0, restored.Code);
        Assert.Equal("tuesday", File.ReadAllText(ws.File_("cells/a/thing.csch")));

        // The view model calls the same functions rather than carrying its own.
        string vm = StripComments(ReadSource("src/Ui/Revision/WorkspaceHistoryService.cs"))
                  + StripComments(ReadSource("src/Ui/ViewModels/WorkspaceViewModel.Revision.cs"));

        foreach (string call in new[] { "WorkspaceCheckpoints.Take", "RestorePoints.List", "WorkspaceRestore.Restore" })
            Assert.Contains(call, vm, StringComparison.Ordinal);

        // And it never assembles a message, a reference name or a sequence of its own — the three
        // things a second copy would have had to reinvent.
        foreach (string reinvented in new[] { "commit-tree", "update-ref", "refs/crf", "CircuitRF-Sequence" })
            Assert.DoesNotContain(reinvented, vm, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gate 22. A machine with no git <b>skips with a reason</b>, following <c>RfCore.Tests</c>'
    /// fixture idiom — a red suite on a missing prerequisite trains people to ignore red. Never fail;
    /// never vendor a git.
    /// </summary>
    [Fact]
    public void EveryGateThatNeedsAGitSkipsRatherThanFailingWithoutOne()
    {
        var methods = typeof(RestoreAndBatchTests).GetMethods()
            .Concat(typeof(RestorePointsTests).GetMethods())
            .Where(m => m.GetCustomAttributes(typeof(GitFactAttribute), false).Length > 0)
            .ToList();

        Assert.NotEmpty(methods);

        // The attribute is what carries the skip, so the property being tested is that the gates use
        // it rather than a plain Fact with a git call inside.
        foreach (var m in methods)
            Assert.Empty(m.GetCustomAttributes(typeof(FactAttribute), false)
                          .Where(a => a.GetType() == typeof(FactAttribute)));
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

    private static RestorePoint Take(GitWorkspace ws, string label)
    {
        var outcome = WorkspaceCheckpoints.Take(ws.Git(), CheckpointOrigin.SavePoint, label);
        Assert.True(outcome.Recorded);
        return outcome.Point!;
    }

    /// <summary>The built CLI, found by walking up — the idiom <c>EmCliVerbTests</c> uses, and for
    /// the same reason: <c>dotnet run</c> would rebuild inside a test.</summary>
    private static readonly string? CliDll = FindCli();

    private static string? FindCli()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir, "src", "Cli", "bin", "Debug", "net10.0", "CircuitRF.Cli.dll");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static (int Code, string Out, string Err) RunCli(GitWorkspace ws, string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory       = ws.Root,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        psi.ArgumentList.Add(CliDll!);
        foreach (string a in args) psi.ArgumentList.Add(a);

        // The same isolation the in-process half runs under, so the comparison is of the operation
        // and not of two different machines' configuration.
        foreach (string key in new[]
                 { "GIT_CONFIG_GLOBAL", "GIT_CONFIG_SYSTEM", "GIT_CONFIG_NOSYSTEM",
                   "HOME", "USERPROFILE", "XDG_CONFIG_HOME" })
            if (Environment.GetEnvironmentVariable(key) is { } value) psi.Environment[key] = value;

        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        string e = p.StandardError.ReadToEnd();
        p.WaitForExit(120_000);
        return (p.ExitCode, o, e);
    }

    /// <summary>
    /// The author and committer lines' timestamps off. <b>The exemption is named</b> (gate 6): two
    /// recordings taken a moment apart legitimately differ there, and nowhere else.
    /// </summary>
    private static string WithoutTimestamps(string commitObject)
        => Regex.Replace(commitObject, @"^(author|committer) (.*) \d+ [+-]\d{4}$", "$1 $2 <when>",
                         RegexOptions.Multiline);
}
