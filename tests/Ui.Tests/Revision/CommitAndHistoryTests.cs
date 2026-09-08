using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CircuitRF.Design.Revision;
using CircuitRF.Diagnostics;
using CircuitRF.Ui.Messages;
using CircuitRF.Ui.Revision;
using CircuitRF.Ui.ViewModels.Dock;
using Xunit;

namespace CircuitRF.Ui.Tests.Revision;

/// <summary>
/// RC-7's gates — <c>docs/sonnet-briefs/brief-revision-control-7-commit-and-history.md</c> §7.
///
/// <para><b>Every fixture path here is the SHAPE of a path, never a real one.</b> A workspace name
/// from a real machine must not reach this repository.</para>
///
/// <para>In <see cref="AppDataRootCollection"/> for the reason RC-3's, RC-5's and RC-6's own gates
/// are: the identity path redirects the per-user state directory, and the git-environment isolation
/// these tests install is process-wide, so two collections that both touch a process global still
/// clobber each other.</para>
/// </summary>
[Collection(AppDataRootCollection.Name)]
public class CommitAndHistoryTests
{
    // ══ 1. What a commit says (R-rc7-5, R-rc7-6) ══════════════════════════════════════════════════

    /// <summary>
    /// <b>An explicit commit says it was explicit, and a commit after a restore says what it was
    /// restored from — and one with no restore since the previous version carries no such line.</b>
    ///
    /// <para>The second half is the one that makes the first mean anything. A line on every version
    /// would say nothing; the line is load-bearing precisely because it appears only where the
    /// content came from somewhere other than the designer's own hands. Without it, two consecutive
    /// versions where the second reverts the first read as a change of mind with no record of the
    /// moment — the one thing the narrative can lie about by omission.</para>
    /// </summary>
    [GitFact]
    public void ACommitSaysItWasExplicitAndNamesWhatItWasRestoredFrom()
    {
        using var ws = Armed();
        var git = ws.Git();

        ws.Write("cells/a/thing.csch", "v1");
        var first = WorkspaceCheckpoints.Take(git, CheckpointOrigin.SavePoint, "the working match");
        Assert.True(first.Recorded);

        ws.Write("cells/a/thing.csch", "v2");
        var kept = WorkspaceCommit.Commit(git, "Output match retuned");
        Assert.True(kept.Ok);

        // No restore has happened, so there is no line — and its ABSENCE is what makes the presence
        // below informative.
        string plain = MessageOf(ws, kept.Version!.CommitId);
        Assert.Contains(CommitMessage.ExplicitLine, plain, StringComparison.Ordinal);
        Assert.DoesNotContain(CommitMessage.RestoredFromKey, plain, StringComparison.Ordinal);
        Assert.Null(kept.Version.RestoredFrom);

        // Go back, then keep a version of what came back.
        var back = WorkspaceRestore.Restore(git, first.Point!);
        Assert.True(back.Ok);

        var after = WorkspaceCommit.Commit(git, "Back to the working match");
        Assert.True(after.Ok);

        string message = MessageOf(ws, after.Version!.CommitId);
        Assert.Contains(CommitMessage.ExplicitLine, message, StringComparison.Ordinal);
        Assert.Contains("the working match", message, StringComparison.Ordinal);
        Assert.Contains(CommitMessage.RestoredFromKey + ": the working match", message,
                        StringComparison.Ordinal);

        Assert.Equal("the working match", after.Version.RestoredFrom?.Label);

        // And the line does not repeat forever. The next version is the designer's own work again.
        ws.Write("cells/a/thing.csch", "v3");
        var next = WorkspaceCommit.Commit(git, "Widened the stub");
        Assert.True(next.Ok);
        Assert.Null(next.Version!.RestoredFrom);
        Assert.DoesNotContain(CommitMessage.RestoredFromKey, MessageOf(ws, next.Version.CommitId),
                              StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The last restore wins.</b> Restore to A, then to B, then commit: the content is B's, so
    /// naming A would be a sentence that is simply untrue — and it is the kind of untrue nobody would
    /// catch, because the entry is otherwise perfectly well formed.
    /// </summary>
    [GitFact]
    public void TwoRestoresInARowNameTheOneTheContentCameFrom()
    {
        using var ws = Armed();
        var git = ws.Git();

        ws.Write("cells/a/thing.csch", "one");
        var a = WorkspaceCheckpoints.Take(git, CheckpointOrigin.SavePoint, "state A");

        ws.Write("cells/a/thing.csch", "two");
        var b = WorkspaceCheckpoints.Take(git, CheckpointOrigin.SavePoint, "state B");

        ws.Write("cells/a/thing.csch", "three");

        Assert.True(WorkspaceRestore.Restore(git, a.Point!).Ok);
        Assert.True(WorkspaceRestore.Restore(git, b.Point!).Ok);

        var kept = WorkspaceCommit.Commit(git, "whichever this is");
        Assert.True(kept.Ok);

        Assert.Equal("state B", kept.Version!.RestoredFrom?.Label);
        Assert.Equal("two", File.ReadAllText(ws.File_("cells/a/thing.csch")));
    }

    // ══ 2. The identity is reported once, on an explicit commit only (R-rc7-7, R-rc7-8) ═══════════

    /// <summary>
    /// <b>One entry, naming the commit, on an explicit commit — and automatic checkpoints post
    /// nothing.</b>
    ///
    /// <para>The pair is the requirement rather than either half. A checkpoint that reported would
    /// drown the panel and take this entry down with it; a commit that did not would withhold the
    /// identifier that makes the escape hatch usable from the one person entitled to it.</para>
    /// </summary>
    [GitFact]
    public void TheIdentityIsNamedOnceAndOnlyOnAnExplicitCommit()
    {
        using var scope = Identity(out var ws);
        using (ws)
        {
            var sink    = new RecordingSink();
            var service = new WorkspaceHistoryService(sink);
            service.NoteWorkspaceWrite();

            ws.Write("cells/a/thing.csch", "v1");

            // Two automatic boundaries first. R-rc5-11: neither says anything on success.
            Assert.True(service.TakeSavePoint(ws.Root, "a save-point"));
            ws.Write("cells/a/thing.csch", "v2");
            Assert.True(service.TakeCloseCheckpoint(ws.Root));

            Assert.DoesNotContain(sink.Texts, t => t.Contains("identity", StringComparison.OrdinalIgnoreCase));

            sink.Clear();
            ws.Write("cells/a/thing.csch", "v3");

            var kept = service.KeepVersion(ws.Root, "Output match retuned");
            Assert.True(kept.Ok);

            // Exactly one entry names it, and the identifier in that entry is this commit's.
            var named = sink.Texts.Where(t => t.Contains("Its identity is", StringComparison.Ordinal)).ToList();
            Assert.Single(named);
            // The WHOLE identity — one spelling everywhere it is shown, said, or copied.
            Assert.Contains(kept.Version!.CommitId, named[0], StringComparison.Ordinal);
        }
    }

    // ══ 3. No git vocabulary, except the one appearance R-rc7-4 permits ═══════════════════════════

    /// <summary>
    /// <b>Every user-visible string in this feature is scanned, and the one exemption is asserted by
    /// name so it cannot spread.</b>
    ///
    /// <para>R-rc7-3: the designer is never shown a commit graph, a branch name, a detached
    /// <c>HEAD</c> or a merge conflict — they are shown their own design at an earlier moment. Git is
    /// what makes that cheap and durable; it is not what the feature is about. R-rc7-4's
    /// qualification governs only what an <b>explicit</b> action produces, and it is spent on
    /// <see cref="HistoryMessages.VersionRecorded"/>'s identifier.</para>
    ///
    /// <para><b>The scan reads STRING LITERALS, not source.</b> A file that invokes
    /// <c>rev-parse HEAD</c> is not showing a designer anything; a file whose sentence says "HEAD" is.
    /// Scanning the whole source would force the two apart into separate files for no reason and would
    /// pass by accident the day somebody moved a sentence.</para>
    /// </summary>
    [Fact]
    public void NoGitVocabularyReachesADesigner()
    {
        string[] forbidden = ["branch", "checkout", "HEAD", "detached", "stash", "merge", "rebase"];

        foreach (string path in UserVisibleSources())
        {
            string source = RestorePointsTests.StripComments(RestorePointsTests.ReadSource(path));

            foreach (string text in Literals(source, path))
                foreach (string word in forbidden)
                    Assert.False(
                        Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase),
                        $"{path} shows a designer the word '{word}': {text}");
        }

        // The one permitted appearance, asserted BY NAME. Without this the scan above would pass just
        // as well on a feature that named nothing at all, which is the opposite failure.
        string reported = HistoryMessages.VersionRecorded("a title", "0123456789abcdef0123").Render();
        Assert.Contains("Its identity is 0123456789ab", reported, StringComparison.Ordinal);
        Assert.DoesNotContain("commit", reported, StringComparison.OrdinalIgnoreCase);
    }

    // ══ 4. Hidden without git, visible and refusing when held (R-rc7-2) ═══════════════════════════

    /// <summary>
    /// <b>Both halves, because the pair is deliberate.</b>
    ///
    /// <para>Absence is silent: a designer with no git never learns this feature exists, and every
    /// affordance is simply not there. A HOLD is loud: the affordance stays visible and refuses,
    /// saying what the state is — because a hidden control is indistinguishable from a feature that
    /// was never built, and the failure this whole feature guards against is a designer who believes
    /// they are protected and is not.</para>
    /// </summary>
    [GitFact]
    public void HiddenWithoutGitAndVisibleAndRefusingWhenHeld()
    {
        // ── Held: a repository the USER made at the workspace root, with no answer recorded ────────
        using var ws = new GitWorkspace();
        using var scope = Identity(ws);

        Assert.Equal(0, ws.Raw("init", "--quiet", ws.Root).Code);

        var sink    = new RecordingSink();
        var service = new WorkspaceHistoryService(sink);
        service.NoteWorkspaceWrite();
        ws.Write("cells/a/thing.csch", "v1");

        var refused = service.KeepVersion(ws.Root, "should not be kept");
        Assert.False(refused.Ok);
        Assert.Contains(sink.Texts, t => t.Contains("not circuitRF's to write to", StringComparison.Ordinal));

        // Nothing was written into their repository.
        Assert.Equal("", ws.Raw("rev-parse", "--verify", "--quiet", "HEAD").Out.Trim());

        // The panel keeps its actions and says why — R-rc6-8's shape. RE-POINTED at RC-10's merged
        // panel rather than relaxed: what is asserted is unchanged.
        var tool = new HistoryTool();
        tool.SetRows(new HistoryList.Result([], 0), hasWorkspace: true, recordingState: "History held");
        Assert.True(tool.IsRecordingBlocked);
        Assert.Equal("History held", tool.RecordingState);

        // ── Absent: the whole surface answers empty and says nothing at all ───────────────────────
        var quiet = new RecordingSink();
        var noWorkspace = new WorkspaceHistoryService(quiet);
        Assert.Empty(noWorkspace.Versions(null));
        Assert.Empty(noWorkspace.VersionRows(null));
        Assert.Empty(quiet.Texts);
    }

    // ══ 5. The two lists are not merged (R-rc7-9) ═════════════════════════════════════════════════

    /// <summary>
    /// <b>A checkpoint never appears in the version list, and a version never appears in the
    /// restore-point list.</b>
    ///
    /// <para>The separation is mechanical rather than a rendering choice: checkpoints live on
    /// references in circuitRF's own namespace, versions on the line of work, and neither reader walks
    /// the other.</para>
    ///
    /// <para><b>NARROWED by RC-10, not withdrawn</b> (§5.10 rule 6, R-rc10-4). rev 6 merged the two
    /// PANELS; it may not merge the two STORES, and the moment it does §5.2a's travel table stops
    /// being true and §5.6's retention has a human-written commit in its scope. So this gate is now
    /// about storage alone, and the presentation half — one list in which the two kinds stay visibly
    /// distinct under one mark — is <c>OneHistoryPanelTests</c>' second gate.</para>
    /// </summary>
    [GitFact]
    public void CheckpointsAndVersionsAreSeparateLists()
    {
        using var ws = Armed();
        var git = ws.Git();

        ws.Write("cells/a/thing.csch", "v1");
        Assert.True(WorkspaceCheckpoints.Take(git, CheckpointOrigin.SavePoint, "a save-point").Recorded);

        ws.Write("cells/a/thing.csch", "v2");
        var kept = WorkspaceCommit.Commit(git, "a version");
        Assert.True(kept.Ok);

        ws.Write("cells/a/thing.csch", "v3");
        Assert.True(WorkspaceCheckpoints.Take(git, CheckpointOrigin.WorkspaceClosed, null).Recorded);

        var points   = RestorePoints.ListIncludingThinned(git);
        var versions = HistoryBrowser.Versions(git);

        Assert.Equal(2, points.Count);
        Assert.Single(versions);

        Assert.Equal("a version", versions[0].Title);
        Assert.DoesNotContain(points, p => p.Label == "a version");
        Assert.DoesNotContain(points, p => p.CommitId == kept.Version!.CommitId);

        var versionIds = versions.Select(v => v.CommitId).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(points, p => versionIds.Contains(p.CommitId));
    }

    // ══ 6. An off period renders as a gap, with its reason (R-rc7-10) ═════════════════════════════

    /// <summary>
    /// <b>The stretch while recording was off is a row of its own, carrying its dates and its
    /// reason.</b>
    ///
    /// <para>Rendering it as an ordinary interval between two versions is §1.4's false-belief failure
    /// in its purest form: the designer reads a quiet fortnight and concludes nothing happened, when
    /// what actually happened is that nothing was recorded.</para>
    /// </summary>
    [GitFact]
    public void AnOffPeriodRendersAsAGapWithItsReason()
    {
        using var ws = Armed();
        var git = ws.Git();

        ws.Write("cells/a/thing.csch", "v1");
        Assert.True(WorkspaceCommit.Commit(git, "before the quiet stretch").Ok);

        Assert.True(RevisionSwitch.TurnOff(ws.Root, keepHistoryPreference: true).Ok);
        ws.Write("cells/a/thing.csch", "changed while nothing was watching");
        Assert.True(RevisionSwitch.TurnOn(ws.Root, keepHistoryPreference: true).Ok);

        ws.Write("cells/a/thing.csch", "v2");
        Assert.True(WorkspaceCommit.Commit(git, "after it").Ok);

        var rows = HistoryBrowser.Rows(HistoryBrowser.Versions(git),
                                       RestorePoints.ListIncludingThinned(git));

        var gaps = rows.Where(r => r.IsGap).ToList();
        Assert.Single(gaps);

        // Two ends, which is what makes it a gap rather than an open-ended silence.
        Assert.NotNull(gaps[0].Gap!.End);

        string text = HistoryMessages.GapBetween(gaps[0].Gap!.FromUtc, gaps[0].Gap!.ToUtc);
        Assert.Contains("Recording was off", text, StringComparison.Ordinal);
        Assert.Contains("not in this history", text, StringComparison.Ordinal);

        // And the panel row carries that sentence, so it cannot render as a blank line. RC-10
        // R-rc10-11: the gap row survives the merge, and it is now the only row in the list that is
        // neither kind of entry.
        var tool = new HistoryTool();
        tool.SetRows(HistoryList.Read(git, HistoryFilter.Default), hasWorkspace: true);
        Assert.Contains(tool.Rows, r => r.IsGap && r.GapText.Contains("Recording was off", StringComparison.Ordinal));
    }

    // ══ 7. Whole-file, pick a side — and no path writes a merged file (R-rc7-12, R-rc7-13) ════════

    /// <summary>
    /// <b>A clash offers two named versions, and choosing one writes that side's bytes exactly.</b>
    ///
    /// <para>A three-way text merge of a polygon's vertex list can produce geometry that is invalid,
    /// or valid and wrong, while remaining perfectly well-formed JSON that opens without complaint.
    /// A merged design that is silently wrong is worse than a clash, because the clash is at least
    /// visible — so the file that comes out of a choice is byte-for-byte one of the two that went in,
    /// which is what this asserts rather than merely that a choice exists.</para>
    /// </summary>
    [GitFact]
    public void AClashOffersTwoNamedVersionsAndKeepsOneWhole()
    {
        using var ws = Armed();
        var git = ws.Git();

        ws.Write("cells/a/thing.csch", "shared\n");
        Assert.True(WorkspaceCommit.Commit(git, "common ground").Ok);
        string basis = ws.Raw("rev-parse", "HEAD").Out.Trim();

        // A second line of work, made with raw git because circuitRF itself never creates one — which
        // is the point: this is a clash arriving from somewhere else, not one circuitRF caused.
        Assert.Equal(0, ws.Raw("checkout", "-q", "-b", "incoming", basis).Code);
        ws.Write("cells/a/thing.csch", "the other copy's version\n");
        Assert.Equal(0, ws.Raw("add", "-A").Code);
        Assert.Equal(0, ws.Raw("-c", "user.name=Someone Else",
                               "-c", "user.email=else@example.invalid",
                               "commit", "-q", "-m", "theirs").Code);

        Assert.Equal(0, ws.Raw("checkout", "-q", "-").Code);
        ws.Write("cells/a/thing.csch", "my version\n");
        Assert.True(WorkspaceCommit.Commit(git, "mine").Ok);

        // The merge is refused per-file by .gitattributes' -merge, which is exactly the state RC-7
        // renders. Its exit code is non-zero and that is the expected outcome.
        ws.Raw("merge", "--no-commit", "incoming");

        var clashes = DocumentClashes.Find(git);
        Assert.Single(clashes);
        Assert.Equal("cells/a/thing.csch", clashes[0].RelativePath);

        // Two NAMED versions: who wrote each, and when.
        Assert.NotEqual("", clashes[0].Mine.Who);
        Assert.NotEqual("", clashes[0].Mine.Title);
        Assert.NotEqual(clashes[0].Mine.BlobId, clashes[0].Theirs.BlobId);

        Assert.True(DocumentClashes.Keep(git, clashes[0], ClashSide.Theirs).Ok);

        // Whole. Not interleaved, not annotated, not a marker anywhere in it.
        Assert.Equal("the other copy's version\n", File.ReadAllText(ws.File_("cells/a/thing.csch")));
        Assert.Empty(DocumentClashes.Find(git));

        // And the other choice is the other file, exactly.
        ws.Raw("merge", "--abort");
    }

    /// <summary>
    /// <b>No code path writes a merged file for any of the five unmergeable types</b> (gate 7).
    ///
    /// <para>The assertion is structural rather than behavioural: there is no third content anywhere
    /// in the feature and nothing that could produce one. A behavioural test can only show that one
    /// path did not merge on one input.</para>
    /// </summary>
    [Fact]
    public void NothingWritesAMergedFile()
    {
        foreach (string path in EveryRevisionSource())
        {
            string source = RestorePointsTests.StripComments(RestorePointsTests.ReadSource(path));

            foreach (string forbidden in new[]
                     { "\"merge\"", "\"merge-file\"", "merge-file", "\"mergetool\"", "<<<<<<<" })
                Assert.False(source.Contains(forbidden, StringComparison.Ordinal),
                             $"{path} could produce a merged design document ({forbidden})");
        }

        // The five types are still marked unmergeable, which is what makes git refuse rather than
        // produce one — the half of this that lives outside our own code.
        string attributes = WorkspacePolicyFiles.GitAttributesBlock();
        foreach (string extension in WorkspacePolicyFiles.UnmergeableExtensions)
            Assert.Contains($"*{extension} -merge", attributes, StringComparison.Ordinal);
    }

    // ══ 8. Restore-then-edit is linear and creates nothing (R-rc7-16) ═════════════════════════════

    /// <summary>
    /// <b>Restore, edit, keep a version: one new version on one line of work, and nothing created.</b>
    ///
    /// <para>rev 4 of the architecture said git requires a second line of work here and had this brief
    /// build a silently-created variant. It does not: a second line is required only if the restore is
    /// a checkout, and it never is. This asserts what that means concretely — the set of references
    /// the designer's own work lives on is exactly the same set afterwards, one entry longer on the
    /// line that was already there.</para>
    /// </summary>
    [GitFact]
    public void RestoreThenEditIsLinearAndCreatesNothing()
    {
        using var ws = Armed();
        var git = ws.Git();

        ws.Write("cells/a/thing.csch", "v1");
        var point = WorkspaceCheckpoints.Take(git, CheckpointOrigin.SavePoint, "the working match");

        ws.Write("cells/a/thing.csch", "v2");
        Assert.True(WorkspaceCommit.Commit(git, "first version").Ok);

        var before   = Heads(ws);
        string wasOn = ws.Raw("symbolic-ref", "HEAD").Out.Trim();
        Assert.Single(before);

        Assert.True(WorkspaceRestore.Restore(git, point.Point!).Ok);
        ws.Write("cells/a/thing.csch", "v1 plus one more edit");

        var kept = WorkspaceCommit.Commit(git, "carried on from there");
        Assert.True(kept.Ok);

        var after = Heads(ws);

        // The same references, by name. Nothing was created, and nothing was left behind.
        Assert.Equal(before.Keys.OrderBy(k => k, StringComparer.Ordinal),
                     after.Keys.OrderBy(k => k, StringComparer.Ordinal));

        // HEAD never left the line it was on.
        Assert.Equal(wasOn, ws.Raw("symbolic-ref", "HEAD").Out.Trim());

        // Exactly one new entry, and the one before it is the one that was there.
        var versions = HistoryBrowser.Versions(git);
        Assert.Equal(2, versions.Count);
        Assert.Equal("carried on from there", versions[0].Title);
        Assert.Equal("first version",         versions[1].Title);
    }

    /// <summary>
    /// <b>Nothing in this brief's paths checks out, switches, branches, stashes or resets</b>
    /// (gate 8) — RC-5 gate 24's scan, over RC-7's own files.
    ///
    /// <para>The words branch, checkout and <c>HEAD</c> have no scenario in which to appear, so the
    /// commands that would create one are absent. <c>update-ref HEAD</c> is the one thing that names
    /// <c>HEAD</c> at all, and it MOVES the line that is already there rather than creating or
    /// switching one — which is why nothing here needs to know what that line is called.</para>
    /// </summary>
    [Fact]
    public void NoRc7PathChecksOutSwitchesBranchesStashesOrResets()
    {
        foreach (string path in EveryRevisionSource())
        {
            string source = RestorePointsTests.StripComments(RestorePointsTests.ReadSource(path));

            foreach (string forbidden in new[]
                     { "\"checkout\"", "\"switch\"", "\"branch\"", "\"stash\"", "\"reset\"",
                       "\"cherry-pick\"", "\"symbolic-ref\"" })
                Assert.False(source.Contains(forbidden, StringComparison.Ordinal),
                             $"{path} invokes git {forbidden}");

            Assert.DoesNotContain("refs/heads", source, StringComparison.Ordinal);
        }

        // The one permitted spelling is present, so the scan is not passing by absence.
        Assert.Contains("\"update-ref\", \"HEAD\"",
                        RestorePointsTests.ReadSource("src/Design/Revision/WorkspaceCommit.cs"),
                        StringComparison.Ordinal);
    }

    // ══ 8a. Going back to a version uses RC-5's restore (R-rc7-17) ════════════════════════════════

    /// <summary>
    /// <b>Restoring to a version reaches RC-5's restore, with every one of its guarantees intact.</b>
    ///
    /// <para>The same assertions RC-5's own gates make — the pre-restore entry exists, ignored files
    /// survived, a file added since is gone — reached from this entry point rather than from the
    /// restore-point list. There is no second restore implementation, and this is what says so in
    /// behaviour rather than by inspection.</para>
    /// </summary>
    [GitFact]
    public void GoingBackToAVersionIsTheSameRestore()
    {
        using var scope = Identity(out var ws);
        using (ws)
        {
            var service = new WorkspaceHistoryService(new RecordingSink());
            service.NoteWorkspaceWrite();

            ws.Write("cells/a/thing.csch", "v1");
            Assert.True(service.KeepVersion(ws.Root, "the one that worked").Ok);

            ws.Write("cells/a/thing.csch", "v2");
            ws.Write("cells/a/added-since.csch", "added after");
            ws.Write("results/sweep.npy", "hours of simulation");     // ignored, and must survive
            Assert.True(service.KeepVersion(ws.Root, "the one that did not").Ok);

            int pointsBefore = service.ListIncludingThinned(ws.Root).Count;

            var target = service.Versions(ws.Root).Single(v => v.Title == "the one that worked");
            var result = service.GoBackToVersion(ws.Root, target);

            Assert.NotNull(result);
            Assert.True(result!.Ok);

            // RC-5 gate 15: the state being replaced was kept FIRST, always.
            Assert.NotNull(result.PreRestore);
            Assert.Equal(CheckpointOrigin.BeforeRestore, result.PreRestore!.Origin);
            Assert.True(service.ListIncludingThinned(ws.Root).Count > pointsBefore);

            // RC-5 gate 17a: the content is back, what came after is gone, ignored files are untouched.
            Assert.Equal("v1", File.ReadAllText(ws.File_("cells/a/thing.csch")));
            Assert.False(File.Exists(ws.File_("cells/a/added-since.csch")));
            Assert.Equal("hours of simulation", File.ReadAllText(ws.File_("results/sweep.npy")));
        }
    }

    // ══ 10. A restore from the browser still checkpoints first (R-rc7-16a) ════════════════════════

    /// <summary>
    /// <b>The pre-restore state is still reachable after restore-then-edit-then-commit.</b>
    ///
    /// <para>A commit made after a restore does not make the pre-restore checkpoint redundant, and
    /// this is the case that shows why: it is what a designer who chose the WRONG version to go back
    /// to gets back from, and by then they have committed on top of the mistake.</para>
    /// </summary>
    [GitFact]
    public void ThePreRestoreStateSurvivesACommitOnTopOfIt()
    {
        using var scope = Identity(out var ws);
        using (ws)
        {
            var service = new WorkspaceHistoryService(new RecordingSink());
            service.NoteWorkspaceWrite();

            ws.Write("cells/a/thing.csch", "the good one");
            Assert.True(service.KeepVersion(ws.Root, "version one").Ok);

            ws.Write("cells/a/thing.csch", "an afternoon of work");

            var target = service.Versions(ws.Root).Single();
            Assert.True(service.GoBackToVersion(ws.Root, target) is { Ok: true });

            ws.Write("cells/a/thing.csch", "and then some more");
            Assert.True(service.KeepVersion(ws.Root, "version two, on top of the mistake").Ok);

            // The afternoon is still there, and still restorable.
            var rescue = service.ListIncludingThinned(ws.Root)
                                .First(p => p.Origin == CheckpointOrigin.BeforeRestore);

            Assert.True(service.Restore(ws.Root, rescue) is { Ok: true });
            Assert.Equal("an afternoon of work", File.ReadAllText(ws.File_("cells/a/thing.csch")));
        }
    }

    // ══ 9. Every failure came from RC-3's table (R-rc7-19, R-rc7-20) ══════════════════════════════

    /// <summary>
    /// <b>No sentence in this feature translates a git failure locally.</b>
    ///
    /// <para>A translation living only in the UI is a translation <c>src/Cli</c> does not have. So
    /// every failure that reaches a designer is one of <see cref="GitFailures"/>' rows or is carried
    /// verbatim by <see cref="GitFailures.Unrecognised"/> — and what RC-7 writes itself is what a git
    /// failure is not: circuitRF's own refusals, and reports of what it did.</para>
    /// </summary>
    [Fact]
    public void EveryGitFailureIsTranslatedByRc3sTable()
    {
        foreach (string path in (string[])
                 ["src/Design/Revision/WorkspaceCommit.cs",
                  "src/Design/Revision/HistoryBrowser.cs",
                  "src/Design/Revision/DocumentClash.cs",
                  "src/Ui/Revision/WorkspaceHistoryService.cs",
                  "src/Cli/History.cs"])
        {
            string source = RestorePointsTests.StripComments(RestorePointsTests.ReadSource(path));

            // Every failure of a git call goes through Translate or Unrecognised. Nothing reads git's
            // own output and writes a sentence about it.
            foreach (var match in Regex.Matches(source, @"\.StdErr\b").Cast<System.Text.RegularExpressions.Match>())
                Assert.Fail($"{path} reads git's own error text at offset {match.Index}");
        }

        // The refusals RC-7 writes itself are circuitRF's, not git's — each one names a state
        // circuitRF decided, and none of them describes a git failure.
        foreach (var d in (Diagnostic[])
                 [HistoryMessages.CannotKeepAVersionHeld(),
                  HistoryMessages.CannotKeepAVersionOff(),
                  HistoryMessages.NoHistoryToKeepAVersionIn("a workspace"),
                  HistoryMessages.NothingChangedSinceLastVersion()])
        {
            Assert.StartsWith("revision.version.", d.Id, StringComparison.Ordinal);
            Assert.DoesNotContain("git said", d.Render(), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// <b>A real git failure comes out of RC-3's table with its own id</b>, rather than as a sentence
    /// this feature made up — which is the half of gate 9 a source scan cannot show.
    /// </summary>
    [GitFact]
    public void ARealFailureCarriesRc3sIdentifier()
    {
        using var ws = Armed();
        var git = ws.Git();
        ws.Write("cells/a/thing.csch", "v1");

        // §4.4's state: circuitRF cannot name a committer. Reproduced by emptying every source of one,
        // which is the ordinary state of a fresh Windows machine. `--unset-all` exits 5 where the key
        // was not set, which is a success here and not a failure to assert against.
        File.WriteAllText(ws.GlobalConfig, "");
        ws.Raw("config", "--local", "--unset-all", "user.name");
        ws.Raw("config", "--local", "--unset-all", "user.email");

        var bare = new GitCommand(git.Installation, ws.Root) { Identity = null };
        var result = WorkspaceCommit.Commit(bare, "should not be kept");

        Assert.False(result.Ok);
        Assert.Contains(result.Diagnostics, d => d.Id == GitFailures.NoIdentity().Id);
    }

    // ══ 11. §8.3's sentence exists and offers no button (R-rc7-21) ════════════════════════════════

    /// <summary>
    /// <b>The one place §8.3 is stated in plain language, and it is stated rather than offered.</b>
    ///
    /// <para>History rewriting is the only real remedy for something already recorded, circuitRF must
    /// not offer a button for it, and a user who genuinely needs it should be told so plainly rather
    /// than left to conclude the application has no answer — which is how somebody ends up deleting
    /// the whole history and losing everything else with it.</para>
    /// </summary>
    [Fact]
    public void TheEscapeHatchIsStatedAndNotOffered()
    {
        string said = HistoryMessages.RewritingIsYoursToDo;

        Assert.Contains("git command line", said, StringComparison.Ordinal);
        Assert.Contains("offers no button", said, StringComparison.Ordinal);
        Assert.Contains("every copy anyone else has taken", said, StringComparison.Ordinal);

        // It sits beside the action a user would look for it from.
        Assert.Contains("RewritingIsYoursToDo",
                        RestorePointsTests.ReadSource("src/Ui/Views/Dialogs/KeepThisVersionDialog.axaml.cs"),
                        StringComparison.Ordinal);
        Assert.Contains("RewritingIsYoursToDo",
                        RestorePointsTests.ReadSource("src/Cli/History.cs"),
                        StringComparison.Ordinal);

        // And there is no code path behind it. Not a disabled button, not a hidden flag: nothing.
        foreach (string path in EveryRevisionSource())
        {
            string source = RestorePointsTests.StripComments(RestorePointsTests.ReadSource(path));

            foreach (string forbidden in new[]
                     { "\"rebase\"", "\"--amend\"", "\"filter-branch\"", "\"filter-repo\"",
                       "\"replace\"", "\"reflog\"" })
                Assert.False(source.Contains(forbidden, StringComparison.Ordinal),
                             $"{path} can rewrite a history ({forbidden})");
        }
    }

    // ══ 12. `history commit` is the Commit action (R-rc7-22) ══════════════════════════════════════

    /// <summary>
    /// <b>The verb and the window's own command produce the same commit</b> — the same tree and the
    /// same message, byte for byte, with only the wall-clock stamp differing.
    ///
    /// <para>The timestamp is the one legitimately-varying field and it is named here rather than
    /// glossed over: the two runs happen at different moments and git records when. Everything a
    /// designer will ever read is compared exactly.</para>
    /// </summary>
    [GitFact]
    public void HistoryCommitIsTheCommitAction()
    {
        using var scope = Identity(out var viaWindow);
        using (viaWindow)
        {
            var service = new WorkspaceHistoryService(new RecordingSink());
            service.NoteWorkspaceWrite();
            viaWindow.Write("cells/a/thing.csch", "identical content\n");

            var kept = service.KeepVersion(viaWindow.Root, "Output match retuned");
            Assert.True(kept.Ok);

            string windowTree    = TreeOf(viaWindow, kept.Version!.CommitId);
            string windowMessage = MessageOf(viaWindow, kept.Version.CommitId);

            // The same workspace content, through the process.
            using var viaCli = Armed();
            viaCli.Write("cells/a/thing.csch", "identical content\n");

            var run = RunCli(viaCli, "history", "commit", viaCli.Root, "--title", "Output match retuned");
            Assert.Equal(0, run.ExitCode);

            string cliCommit  = viaCli.Raw("rev-parse", "HEAD").Out.Trim();
            string cliTree    = TreeOf(viaCli, cliCommit);
            string cliMessage = MessageOf(viaCli, cliCommit);

            Assert.Equal(windowTree,    cliTree);
            Assert.Equal(windowMessage, cliMessage);
        }
    }

    /// <summary>
    /// <b>The view model kept no second copy</b> — the standard <c>AuthoringCliVerbTests</c> scan, over
    /// RC-7's own paths. An operation re-implemented in a view model diverges from the verb silently.
    /// </summary>
    [Fact]
    public void NeitherSpellingHoldsARevisionControlLogicOfItsOwn()
    {
        foreach (string path in (string[])
                 ["src/Ui/ViewModels/WorkspaceViewModel.Revision.cs",
                  "src/Ui/Revision/WorkspaceHistoryService.cs",
                  "src/Ui/ViewModels/Dock/HistoryTool.cs",
                  "src/Cli/History.cs"])
        {
            string source = RestorePointsTests.StripComments(RestorePointsTests.ReadSource(path));

            // Neither builds a commit message, and neither writes a commit. Both call the one function
            // in src/Design that does.
            Assert.DoesNotContain("commit-tree", source, StringComparison.Ordinal);
            Assert.DoesNotContain("write-tree",  source, StringComparison.Ordinal);
            Assert.DoesNotContain("CommitMessage.Build", source, StringComparison.Ordinal);
        }

        Assert.Contains("WorkspaceCommit.Commit",
                        RestorePointsTests.ReadSource("src/Ui/Revision/WorkspaceHistoryService.cs"),
                        StringComparison.Ordinal);
        Assert.Contains("WorkspaceCommit.Commit",
                        RestorePointsTests.ReadSource("src/Cli/History.cs"),
                        StringComparison.Ordinal);
    }

    // ══ Comparison is by document (R-rc7-11) ══════════════════════════════════════════════════════

    /// <summary>
    /// <b>Comparison names the documents that differ.</b> That is the design layer's question; what
    /// changed inside one is the text layer's, and is deliberately not this brief's scope.
    /// </summary>
    [GitFact]
    public void ComparisonNamesTheDocumentsThatDiffer()
    {
        using var ws = Armed();
        var git = ws.Git();

        ws.Write("cells/a/thing.csch", "v1");
        ws.Write("cells/b/other.clay", "unchanged throughout");
        var first = WorkspaceCommit.Commit(git, "first");
        Assert.True(first.Ok);

        ws.Write("cells/a/thing.csch", "v2");
        ws.Write("cells/c/new.csym", "brand new");
        File.Delete(ws.File_("cells/b/other.clay"));

        var second = WorkspaceCommit.Commit(git, "second");
        Assert.True(second.Ok);

        var changes = HistoryBrowser.Compare(git, first.Version!.CommitId, second.Version!.CommitId);

        Assert.Equal(3, changes.Count);
        Assert.Contains(changes, c => c.RelativePath == "cells/a/thing.csch"
                                   && c.Kind == DocumentChangeKind.Changed);
        Assert.Contains(changes, c => c.RelativePath == "cells/c/new.csym"
                                   && c.Kind == DocumentChangeKind.Added);
        Assert.Contains(changes, c => c.RelativePath == "cells/b/other.clay"
                                   && c.Kind == DocumentChangeKind.Removed);
    }

    /// <summary>Nothing changed means no new version, decided by comparing trees rather than by
    /// matching git's English.</summary>
    [GitFact]
    public void NothingChangedKeepsNoNewVersion()
    {
        using var ws = Armed();
        var git = ws.Git();

        ws.Write("cells/a/thing.csch", "v1");
        Assert.True(WorkspaceCommit.Commit(git, "the only one").Ok);

        var again = WorkspaceCommit.Commit(git, "a second title for the same content");
        Assert.False(again.Ok);
        Assert.Contains(again.Diagnostics, d => d.Id == "revision.version.nothing-changed");
        Assert.Single(HistoryBrowser.Versions(git));
    }

    // ══ Helpers ═══════════════════════════════════════════════════════════════════════════════════

    /// <summary>A workspace with an identity, a repository circuitRF manages, and its policy
    /// files.</summary>
    private static GitWorkspace Armed()
    {
        var ws = new GitWorkspace();
        File.WriteAllText(ws.GlobalConfig,
            "[user]\n\tname = A Designer\n\temail = designer@example.invalid\n");

        var armed = WorkspaceArming.Arm(ws.Root, CheckpointOrigin.SavePoint, true, null, true);
        Assert.True(armed.Armed);
        return ws;
    }

    private static IDisposable Identity(GitWorkspace ws)
    {
        File.WriteAllText(ws.GlobalConfig,
            "[user]\n\tname = A Designer\n\temail = designer@example.invalid\n");
        return new AppDataRootScope();
    }

    /// <summary>An unarmed workspace plus a redirected per-user state directory — for the tests that
    /// drive <see cref="WorkspaceHistoryService"/>, which reads the preference.</summary>
    private static IDisposable Identity(out GitWorkspace ws)
    {
        ws = new GitWorkspace();
        return Identity(ws);
    }

    /// <summary>The raw message of one commit, for the assertions that read what it says.</summary>
    private static string MessageOf(GitWorkspace ws, string commitId)
        => ws.Raw("--no-pager", "log", "-1", "--format=%B", commitId).Out;

    private static string TreeOf(GitWorkspace ws, string commitId)
        => ws.Raw("rev-parse", commitId + "^{tree}").Out.Trim();

    /// <summary>Every reference the designer's own line of work lives on, by name.</summary>
    private static Dictionary<string, string> Heads(GitWorkspace ws)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in ws.Raw("for-each-ref", "--format=%(refname) %(objectname)", "refs/heads/")
                                  .Out.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int space = line.IndexOf(' ');
            if (space > 0) found[line[..space]] = line[(space + 1)..].Trim();
        }
        return found;
    }

    /// <summary>
    /// Every source file RC-7's absence gates read. <b>Named rather than globbed</b>, so a file added
    /// to the feature and not to this list is a visible omission rather than a silent one.
    /// </summary>
    private static IEnumerable<string> EveryRevisionSource()
    {
        foreach (string name in (string[])
                 ["WorkspaceCommit", "CommitMessage", "HistoryBrowser", "HistoryMessages",
                  "DocumentClash", "RestoreProvenance", "WorkspaceRestore"])
            yield return $"src/Design/Revision/{name}.cs";

        yield return "src/Ui/Revision/WorkspaceHistoryService.cs";
        yield return "src/Ui/ViewModels/WorkspaceViewModel.Revision.cs";
        yield return "src/Ui/ViewModels/Dock/HistoryTool.cs";
        yield return "src/Cli/History.cs";
    }

    /// <summary>
    /// Every source that puts words in front of a designer. <b>Not the same list as
    /// <see cref="EveryRevisionSource"/></b>: a file that invokes <c>rev-parse HEAD</c> shows nobody
    /// anything, and forcing the two apart to satisfy one scan would be arranging the code around a
    /// test.
    /// </summary>
    private static IEnumerable<string> UserVisibleSources()
    {
        yield return "src/Design/Revision/HistoryMessages.cs";
        yield return "src/Design/Revision/CommitMessage.cs";
        yield return "src/Ui/ViewModels/Dock/HistoryTool.cs";
        yield return "src/Ui/Views/Revision/HistoryToolView.axaml";
        yield return "src/Ui/Views/Dialogs/KeepThisVersionDialog.axaml";
        yield return "src/Ui/Views/Dialogs/KeepThisVersionDialog.axaml.cs";
    }

    /// <summary>
    /// The string literals in a source file — C# <c>"…"</c> and, for a <c>.axaml</c>, the attribute
    /// values and element text a designer actually reads.
    /// </summary>
    private static IEnumerable<string> Literals(string source, string path)
    {
        if (path.EndsWith(".axaml", StringComparison.Ordinal))
        {
            // Attribute values, minus the ones that are addresses rather than words: a binding path,
            // a type name, a class name, a namespace.
            foreach (System.Text.RegularExpressions.Match m in Regex.Matches(source, @"(\w[\w.]*)\s*=\s*""([^""]*)"""))
            {
                string name = m.Groups[1].Value;
                if (name is "x:Class" or "x:DataType" or "x:Name" or "Command" or "CommandParameter"
                         or "ItemsSource" or "SelectedItem" or "DataType" or "Kind"
                    || name.StartsWith("xmlns", StringComparison.Ordinal))
                    continue;

                // A binding is an address, not a sentence.
                if (m.Groups[2].Value.StartsWith("{", StringComparison.Ordinal)) continue;
                yield return m.Groups[2].Value;
            }
            yield break;
        }

        foreach (System.Text.RegularExpressions.Match m in Regex.Matches(source, @"""((?:[^""\\\n]|\\.)*)"""))
            yield return m.Groups[1].Value;
    }

    /// <summary>
    /// Runs the built CLI against a workspace, with that workspace's isolated git environment.
    ///
    /// <para><b>The BUILT <c>CircuitRF.Cli.dll</c>, never <c>dotnet run --project</c></b> — a nested
    /// <c>dotnet run</c> starts an MSBuild inside a <c>dotnet test</c> that already holds this
    /// repository's build locks and does not finish (<c>EmCliVerbTests.RunCli</c> records why).</para>
    /// </summary>
    private static (int ExitCode, string StdOut, string StdErr) RunCli(
        GitWorkspace ws, params string[] args)
    {
        string cliDir = System.Reflection.CustomAttributeExtensions
            .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(
                typeof(CommitAndHistoryTests).Assembly)
            .First(a => a.Key == "CliDir").Value!;

        string dll = Path.GetFullPath(Path.Combine(cliDir, "CircuitRF.Cli.dll"));
        Assert.True(File.Exists(dll), $"the CLI was not built beside these tests: {dll}");

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory       = ws.Root,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        psi.ArgumentList.Add(dll);
        foreach (string a in args) psi.ArgumentList.Add(a);

        // The same isolation the in-process half runs under, so the two are comparable: the developer's
        // own git identity must not reach either of them.
        psi.Environment["GIT_CONFIG_GLOBAL"] = ws.GlobalConfig;
        psi.Environment["GIT_CONFIG_SYSTEM"] = Path.Combine(ws.HomeDir, "no-such-system-config");
        psi.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        psi.Environment["HOME"]                = ws.HomeDir;
        psi.Environment["USERPROFILE"]         = ws.HomeDir;
        psi.Environment["XDG_CONFIG_HOME"]     = ws.HomeDir;

        using var proc = Process.Start(psi)!;
        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        proc.WaitForExit();
        return (proc.ExitCode, outTask.GetAwaiter().GetResult(), errTask.GetAwaiter().GetResult());
    }

    /// <summary>A sink that keeps what it was told, so a CADENCE can be asserted rather than a
    /// state.</summary>
    private sealed class RecordingSink : IMessageSink
    {
        public List<string>       Texts  { get; } = [];
        public List<MessageLevel> Levels { get; } = [];

        public void Post(MessageLevel level, string text, string? filePath = null)
        {
            Levels.Add(level);
            Texts.Add(text);
        }

        public void Clear() { Texts.Clear(); Levels.Clear(); }
    }
}
