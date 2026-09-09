using CircuitRF.Diagnostics;

namespace CircuitRF.Design.Revision;

/// <summary>
/// <b>Every sentence RC-7 puts in front of a designer</b>
/// (<c>docs/design/revision-control.md</c> §5.5, §6.1, §6.3, §8.3;
/// R-rc7-4, R-rc7-7, R-rc7-13, R-rc7-19, R-rc7-21).
///
/// <para><b>Diagnostics, not strings</b> (R-rc3-6), for <see cref="RestorePointMessages"/>'s reasons:
/// the Messages panel and the CLI render the same wording from one source, and the ids are what dedup
/// and filtering key on.</para>
///
/// <para><b>The vocabulary rule, and the one place it is qualified</b> (R-rc7-3, R-rc7-4, R-rc0-6).
/// No branch, checkout, <c>HEAD</c>, detached, stash or merge appears in any of this. The single
/// exception in the whole feature is <see cref="VersionRecorded"/>, which names the identity of the
/// thing the designer has just deliberately created — and withholding that would be coyness rather
/// than clarity, because it is what they, or someone helping them, type into a git command when
/// circuitRF's own window cannot answer the question. <b>Nothing git-shaped appears unbidden; what an
/// explicit action produces may be named precisely.</b> The exemption is asserted by name in gate 3
/// so it cannot spread.</para>
///
/// <para><b>No failure is invented here</b> (R-rc7-19, R-rc7-20). Every git failure this feature can
/// hit is one of <see cref="GitFailures"/>' rows or is carried verbatim by
/// <see cref="GitFailures.Unrecognised"/>. A translation living only in the UI is a translation
/// <c>src/Cli</c> does not have, so what is here is what a git failure is not: refusals circuitRF
/// itself makes, and reports of what it did.</para>
/// </summary>
public static class HistoryMessages
{
    /// <summary>
    /// What the operation is called, in circuitRF's words, for
    /// <see cref="GitFailures.Translate"/>'s <c>what</c>. Never git's.
    /// </summary>
    public const string KeepingAVersion = "keeping this version";

    /// <summary>What comparing two versions is called, for the same reason.</summary>
    public const string ComparingVersions = "comparing two versions";

    /// <summary>What resolving a clash is called, for the same reason.</summary>
    public const string ChoosingAVersion = "choosing which version of a document to keep";

    // ── The commit itself (R-rc7-7) ───────────────────────────────────────────────────────────────

    /// <summary>
    /// R-rc7-7. <b>One entry, on an explicit commit only, and it names the commit's identity.</b>
    ///
    /// <para>The identifier is what makes §4.1's escape hatch usable: it is what the designer, or
    /// somebody helping them, types into a git command when circuitRF's own window cannot answer the
    /// question. <b>This is the one appearance R-rc7-4 permits in the whole feature</b> — the person
    /// reading it pressed the button that made the thing being named.</para>
    ///
    /// <para><b>Automatic checkpoints post nothing</b> (R-rc5-11, R-rc7-8). They are in the
    /// restore-point list, which is where someone looking for one looks; one entry per boundary would
    /// drown the panel and take this one down with it.</para>
    /// </summary>
    public static Diagnostic VersionRecorded(string title, string commitId) => Diagnostic.Create(
        "revision.version.recorded",
        DiagnosticSeverity.Info,
        "Kept this version of the workspace as '{title}'. Its identity is {id}, which is what to give "
      + "anyone helping you look at it outside circuitRF.",
        ("title", (object?)title), ("id", commitId));

    /// <summary>
    /// Nothing changed since the version already on the line of work. <b>Info, not a failure</b> — the
    /// state is already recorded, and a second entry saying the same thing would be one a designer
    /// could not tell from the one above it.
    /// </summary>
    public static Diagnostic NothingChangedSinceLastVersion() => new(
        "revision.version.nothing-changed",
        DiagnosticSeverity.Info,
        "Nothing has changed since the last version you kept, so no new one was kept.");

    // ── Refusals circuitRF makes itself (R-rc7-2) ─────────────────────────────────────────────────

    /// <summary>
    /// R-rc7-2's second half. <b>Visible and refusing, never hidden.</b> Held is a designer who may
    /// believe they are protected; a hidden control is indistinguishable from a feature that was never
    /// built. The remedy is deliberately not restated — it was said once, on open (R-rc6-9).
    /// </summary>
    public static Diagnostic CannotKeepAVersionHeld() => new(
        "revision.version.refused.held",
        DiagnosticSeverity.Error,
        "This workspace's history is not circuitRF's to write to, so no version was kept.");

    /// <summary>Recording is switched off for this workspace, so nothing is written — and the remedy
    /// is one setting away.</summary>
    public static Diagnostic CannotKeepAVersionOff() => new(
        "revision.version.refused.off",
        DiagnosticSeverity.Error,
        "This workspace does not keep a history, so no version was kept. Turn it on in "
      + "Settings ▸ Revision Control and try again.");

    /// <summary>
    /// There is no history to keep a version in yet. Reported only where the caller ASKED — R-rc3-3's
    /// silence still governs every automatic path, and R-rc7-2's first half hides the affordance
    /// entirely on a machine with no git.
    /// </summary>
    public static Diagnostic NoHistoryToKeepAVersionIn(string workspaceName) => Diagnostic.Create(
        "revision.version.refused.no-history",
        DiagnosticSeverity.Error,
        "circuitRF is not keeping a history of '{workspace}' yet, so there is nowhere to keep a "
      + "version.",
        ("workspace", workspaceName));

    // ── The browser (R-rc7-9, R-rc7-10, R-rc7-11) ─────────────────────────────────────────────────

    /// <summary>
    /// R-rc7-9. <b>The two lists are not merged, and the browser says which one it is.</b>
    ///
    /// <para>Conflating the narrative with the safety net produces a log no human will read, which
    /// then makes the safety net useless too because nobody looks at it. They may sit side by side —
    /// and do — and this is the line that stops the empty one reading as a broken one.</para>
    /// </summary>
    public const string NothingKeptYet =
        "You have not kept a version of this workspace yet. Keeping one records the whole workspace "
      + "under a title you write, so you can find it again and send it out.";

    /// <summary>
    /// The same empty panel with <b>no workspace open at all</b>, which is a different fact and needs a
    /// different sentence.
    ///
    /// <para><see cref="NothingKeptYet"/> says <i>this workspace</i>, and with nothing open there is no
    /// <i>this workspace</i> for it to be about — so the panel would be making a confident claim about
    /// a workspace the designer had just closed. That is the same defect as an indicator left running
    /// after a close, one panel over.</para>
    /// </summary>
    public const string NoWorkspaceOpen =
        "No workspace is open. Versions are kept per workspace; open one to see its versions.";

    /// <summary>
    /// R-rc7-10. <b>An off period renders as a gap, with its reason.</b>
    ///
    /// <para>Rendering it as an ordinary interval between two versions is §1.4's false-belief failure
    /// in its purest form: the designer reads a quiet fortnight and concludes nothing happened, when
    /// what actually happened is that nothing was recorded.</para>
    /// </summary>
    public static string GapBetween(DateTimeOffset from, DateTimeOffset? to)
        => to is { } ended
            ? $"Recording was off from {from.ToLocalTime():d MMM yyyy} to {ended.ToLocalTime():d MMM yyyy}. "
            + "Anything done in between is not in this history."
            : $"Recording has been off since {from.ToLocalTime():d MMM yyyy}. "
            + "Nothing done since then is in this history.";

    /// <summary>
    /// R-rc7-11. What comparison answers, said where a designer might expect more of it than it gives:
    /// which documents differ, not which lines within one do.
    /// </summary>
    public const string ComparisonIsByDocument =
        "This lists the documents that differ between the two versions. It does not compare what is "
      + "inside one — open both and look, or go back to one of them.";

    /// <summary>Nothing differs. A real answer to the question, and one worth saying plainly rather
    /// than showing as an empty list.</summary>
    public const string NothingDiffers = "These two versions hold exactly the same files.";

    // ── Clashes: whole-file, pick a side (R-rc7-12, R-rc7-13) ─────────────────────────────────────

    /// <summary>
    /// R-rc7-12/R-rc7-13. <b>Why circuitRF will not merge them, said in terms of geometry and
    /// connectivity rather than effort</b> — because it is a statement about what a merged file WOULD
    /// BE, not about how hard merging is.
    ///
    /// <para>A three-way text merge of a polygon's vertex list can produce geometry that is invalid,
    /// or valid and wrong, while remaining perfectly well-formed and opening without complaint. The
    /// same is true of a schematic's connectivity. <b>A merged design that is silently wrong is worse
    /// than a clash</b>, because the clash is at least visible.</para>
    /// </summary>
    public const string WhyThereIsNoMerging =
        "circuitRF does not combine two versions of a design document into one. Interleaving two sets "
      + "of edits to a shape or a set of connections can produce a document that opens perfectly well "
      + "and is wrong — which nobody would see. Choose the version you want and keep it whole.";

    /// <summary>What a clash is, and what the choice is. Two named versions, and no invitation to
    /// reconcile them by hand.</summary>
    public static Diagnostic DocumentsNeedAChoice(int count) => Diagnostic.Create(
        "revision.clash.choose",
        DiagnosticSeverity.Warning,
        "{count} document(s) here were changed in two places at once. Choose which version of each to "
      + "keep — circuitRF keeps the one you pick, whole.",
        ("count", (object?)count));

    /// <summary>What was kept, once a side was chosen. Names which one, because "resolved" says
    /// nothing about which of two designs is now on disk.</summary>
    public static Diagnostic ChoiceKept(string relativePath, string whose) => Diagnostic.Create(
        "revision.clash.kept",
        DiagnosticSeverity.Info,
        "'{path}' is now {whose} version, whole. The other one is untouched in the history.",
        ("path", (object?)relativePath), ("whose", whose));

    // ── §8.3, in plain language (R-rc7-21) ────────────────────────────────────────────────────────

    /// <summary>
    /// <b>§8.3's escape hatch, stated plainly, once, beside the action a user would look for it
    /// from</b> (R-rc7-21, R-rc0-7).
    ///
    /// <para>Rewriting a history is the only real remedy for a large file already recorded. circuitRF
    /// must not offer a button for it — it invalidates every existing copy anyone has taken, and no
    /// affordance can make that safe. But a user who genuinely needs it should be <b>told so plainly
    /// rather than left to conclude the application has no answer</b>, which is how somebody ends up
    /// deleting the history and losing everything else with it.</para>
    ///
    /// <para>One sentence for each of the three things they need: that it is possible, that it is not
    /// circuitRF's to do, and why — which is the part that decides whether they should.</para>
    ///
    /// <para><b>Re-pointed by RC-11, not deleted</b> (R-rc11-15, §12 Q33). It used to open by saying
    /// circuitRF <i>will not alter</i> a kept version, full stop — which after §5.11 is untrue of its
    /// title and remains true of its content. §8.3's subject is a FILE in the history that must not be
    /// there, and that is what this keeps; what a person wrote about a version is §5.11's, and this no
    /// longer claims it. The two do not conflict, and the sentence that used to imply they were one
    /// rule is the one sentence here that changed.</para>
    /// </summary>
    public const string RewritingIsYoursToDo =
        "The FILES in a version stay in the history for good — circuitRF never alters what was "
      + "recorded. (What you wrote about it is a different matter: see below.) If a file is in there "
      + "that must not be (a large one you regret, or something that should never have left your "
      + "machine), the git command line can rewrite a history to remove it. circuitRF deliberately "
      + "offers no button for that: rewriting replaces every entry, so every copy anyone else has "
      + "taken of this workspace stops matching and cannot be brought back into line. It is worth "
      + "doing when it is worth that.";

    /// <summary>
    /// The other half of R-rc11-15, said beside it: what a designer CAN do, so the paragraph above is
    /// read as the narrow rule it is rather than as a blanket one.
    /// </summary>
    public const string TitlesAreYoursToCorrect =
        "A title is not a file. The line you wrote on a version is yours to correct until you have "
      + "shared it, and a restore point's label is yours to correct at any time — right-click the "
      + "entry in the History panel. Once a version has gone to another copy, you can add a "
      + "correction to it instead, which travels with it.";


    // ── RC-10: the merged panel (§5.10) ───────────────────────────────────────────────────────────

    /// <summary>
    /// R-rc10-1. <b>The panel's name, in one place.</b> Not <i>Versions</i>: that word has a precise
    /// meaning in §5.2 — titled, permanent, travels with a copy — and it is the word that distinguishes
    /// the two kinds of row. A panel in which everything is a version has no word left for the
    /// distinction, and a designer who learns these are all versions and then finds half of them did
    /// not arrive with a copy (§5.2a) has been told something false.
    /// </summary>
    public const string PanelTitle = "History";

    /// <summary>
    /// R-rc10-2. <b>What the mark on a version row means</b>, said in the tooltip that carries it.
    /// Three promises the unmarked rows do not make — and it is not an importance badge.
    /// </summary>
    public const string VersionMarkMeans =
        "A version: you gave it a title, nothing tidies it away, and it travels with a copy of this "
      + "workspace. The unmarked entries are this machine's own safety net and do none of those things.";

    /// <summary>
    /// R-rc10-5's empty list, and <b>the wording is the whole of it</b>.
    ///
    /// <para>A workspace whose only history is workspace-close entries opens on an empty list under the
    /// default filter — which is exactly the workspace §1 is written for, since its owner never thought
    /// about history at all. A line saying "nothing kept yet" would be false, so this one says what is
    /// there and where it is.</para>
    /// </summary>
    public static string OnlyAutomaticEntries(int count) => count == 1
        ? "One entry was kept automatically when this workspace was closed. Automatic entries are "
        + "hidden until you ask for them — turn on 'kept automatically' in the filter to see it."
        : $"{count} entries were kept automatically when this workspace was closed. Automatic entries "
        + "are hidden until you ask for them — turn on 'kept automatically' in the filter to see them.";

    /// <summary>
    /// R-rc10-5's empty list when the filter really is hiding everything a designer asked to see. Says
    /// which control is responsible, because an empty list with no explanation reads as a broken panel.
    /// </summary>
    public const string EverythingIsFilteredOut =
        "Nothing here matches what the filter is set to show. Widen it, or clear the search.";

    /// <summary>
    /// R-rc10-1's empty list on a workspace with no history at all — neither kind. It explains the
    /// feature rather than reading as a failure, because this is the ordinary state of a workspace
    /// nobody has recorded anything in yet.
    /// </summary>
    public const string NothingRecordedYet =
        "Nothing has been kept for this workspace yet. circuitRF keeps the whole workspace when you "
      + "close it, when you ask it to, and before an assistant changes anything — and you can keep a "
      + "titled version of your own at any time.";

    /// <summary>The same empty panel with no workspace open, which is a different fact.</summary>
    public const string NoWorkspaceOpenForHistory =
        "No workspace is open. A history belongs to one workspace; open one to see it.";

    /// <summary>
    /// R-rc10-10. <b>A search whose answer is incomplete says so, on a line of its own.</b>
    ///
    /// <para>RC-6's journal already knows the entries retention tidied away, so returning fewer results
    /// than exist would be a wrong answer rather than a short one — and the designer would have no way
    /// to tell the two apart. The same reasoning as R-rc9-6's incoming-versions line, which is already
    /// in this panel.</para>
    /// </summary>
    public static string ThinnedAlsoMatch(int count) => count == 1
        ? "One entry that was tidied away also matches. Turn on 'tidied away' in the filter to see it."
        : $"{count} entries that were tidied away also match. Turn on 'tidied away' in the filter to "
        + "see them.";

    /// <summary>
    /// R-rc10-18, §5.8, §12 Q35. <b>The way forward, offered where the way back was taken.</b>
    ///
    /// <para>This is the most reassuring control in the feature and <b>it creates nothing</b>: the entry
    /// it names already exists, and reaching it is an ordinary restore with an ordinary checkpoint of
    /// its own. Until the panels were merged it was findable only in the other one, so a designer who
    /// went back from the Versions panel was left looking at a window showing no evidence that the
    /// afternoon they had just replaced still existed — the reassurance was implemented, correct, and
    /// in the room the designer was not in.</para>
    /// </summary>
    public static string WayForward(
        string         nowAtId,
        DateTimeOffset nowAtUtc,
        string         keptAsId,
        DateTimeOffset keptAsUtc,
        DateTimeOffset nowUtc)
    {
        string here = HistoryIds.Short(nowAtId);
        string kept = HistoryIds.Short(keptAsId);

        return (here.Length > 0
                 ? $"Now at {here}, {HistoryDates.DayAndTime(nowAtUtc, nowUtc)}."
                 : $"Now at the state from {HistoryDates.DayAndTime(nowAtUtc, nowUtc)}.")
             + (kept.Length > 0
                 ? $" Your work up to {HistoryDates.ClockOrDayAndTime(keptAsUtc, nowUtc)} is kept as {kept}."
                 : "");
    }

    /// <summary>
    /// What the button under that line says. <b>It names its destination</b> rather than a direction,
    /// which is the whole of what was wrong with "Come forward again" (owner, 2026-09-08): going back
    /// and coming forward are one operation performed twice, so a button labelled with a direction can
    /// be pressed for ever and never says where the last press took you. Two presses land on two
    /// identities and the label changes between them, which is the evidence that something happened —
    /// and from the third press on the two settle into a pair, because WorkspaceRestore stops recording
    /// a state the history already holds.
    /// </summary>
    public static string GoBackToId(string commitId)
        => HistoryIds.Short(commitId) is { Length: > 0 } id ? "Go back to " + id : "Go back to that state";

    // ── RC-11: correcting what you wrote (§5.11) ──────────────────────────────────────────────────

    /// <summary>What a git failure during a title correction was being attempted for.</summary>
    public const string CorrectingATitle = "correcting what you wrote about this version";

    /// <summary>The same, for a restore point's label.</summary>
    public const string RenamingARestorePoint = "renaming this entry";

    /// <summary>The same, for letting one go.</summary>
    public const string LettingARestorePointGo = "letting this entry go";

    /// <summary>A correction with nothing in it. Refused rather than applied: an entry whose title
    /// somebody cleared would list under circuitRF's own placeholder, which is not what anyone who
    /// opened this dialog meant.</summary>
    public static Diagnostic ACorrectionNeedsWords() => new(
        "revision.correction.empty",
        DiagnosticSeverity.Error,
        "Write the line it should say. Leaving it blank does not remove the entry — "
      + "use 'let this entry go' for that.");

    /// <summary>
    /// <b>The refusal case (c) exists for</b> (R-rc11-2, R-rc11-12) — and it offers the annotation by
    /// name, because a refusal that only says no leaves the designer with the problem they came with.
    ///
    /// <para>It covers the state where sharing <i>cannot be computed</i> as well as the one where it is
    /// known: a remote configured and never fetched answers "shared", because a correction refused is
    /// recoverable and an erasure believed is not.</para>
    /// </summary>
    public static Diagnostic TitleHasBeenShared(string title) => Diagnostic.Create(
        "revision.correction.shared",
        DiagnosticSeverity.Error,
        "'{title}' has already gone to another copy of this workspace, so its title is no longer "
      + "yours alone to change. Add a correction to it instead — the correction travels with it, and "
      + "shows in place of the original.",
        ("title", (object?)title));

    /// <summary>
    /// R-rc11-8's refusal. <b>It names why rather than saying no</b>, and offers case (c) — the tail
    /// correction is a follow-up that must not be assumed, and this sentence is what most designers
    /// who meet this feature at all will actually read.
    /// </summary>
    public static Diagnostic OnlyTheNewestTitleCorrects(string title) => Diagnostic.Create(
        "revision.correction.not-newest",
        DiagnosticSeverity.Error,
        "Only the newest version's title can be corrected in place — changing an older one would "
      + "rewrite every version kept after it. Add a correction to '{title}' instead: it travels with "
      + "the version and shows in place of the original.",
        ("title", (object?)title));

    /// <summary>The entry's own object could not be read. Rare, and it is a repository fault rather
    /// than anything the designer did.</summary>
    public static Diagnostic EntryCouldNotBeRead() => new(
        "revision.correction.unreadable",
        DiagnosticSeverity.Error,
        "That entry could not be read out of this workspace's history, so nothing was changed.");

    /// <summary>What a title correction did.</summary>
    public static Diagnostic TitleCorrected(string title) => Diagnostic.Create(
        "revision.correction.title",
        DiagnosticSeverity.Info,
        "That version is now called '{title}'. What it holds is untouched — the files, who kept it "
      + "and when are the ones already recorded.",
        ("title", (object?)title));

    /// <summary>What an annotation did.</summary>
    public static Diagnostic CorrectionAdded(string correction) => Diagnostic.Create(
        "revision.correction.added",
        DiagnosticSeverity.Info,
        "Correction added: '{text}'. It shows in place of the original and travels with the version.",
        ("text", (object?)correction));

    /// <summary>And what taking one back did. Not an erasure of anything: the original was never
    /// altered, so removing the correction simply puts the original back in front.</summary>
    public static Diagnostic CorrectionRemoved(string title) => Diagnostic.Create(
        "revision.correction.removed",
        DiagnosticSeverity.Info,
        "The correction on '{title}' is gone, and the original wording shows again.",
        ("title", (object?)title));

    /// <summary>A renamed entry.</summary>
    public static Diagnostic RestorePointRenamed(string label) => Diagnostic.Create(
        "revision.correction.renamed",
        DiagnosticSeverity.Info,
        "That entry is now called '{label}'. The state it holds is exactly the state it held.",
        ("label", (object?)label));

    /// <summary>An entry retention has already tidied away has no reference to rename. The remedy is
    /// on the same menu and is one action.</summary>
    public static Diagnostic TidiedAwayCannotBeRenamed() => new(
        "revision.correction.tidied-away",
        DiagnosticSeverity.Error,
        "That entry has been tidied away. Bring it back first, then rename it.");

    /// <summary>
    /// R-rc11-5. <b>Letting an entry go is the same thing tidying up does</b>, and the sentence says
    /// so — because a designer who thought they had destroyed a state, and then found it listed, would
    /// trust the list less rather than more.
    /// </summary>
    public static Diagnostic RestorePointLetGo(string label) => Diagnostic.Create(
        "revision.correction.let-go",
        DiagnosticSeverity.Info,
        "'{label}' has been tidied away, exactly as tidying up would have done it. It is still "
      + "listed under 'tidied away' and you can bring it back; nothing is freed until you ask "
      + "circuitRF to reclaim space.",
        ("label", (object?)label));

    // ── R-rc11-14: the sentence that may not be softened ──────────────────────────────────────────

    /// <summary>
    /// <b>The one sentence in this feature that may not be softened</b> (R-rc11-14, §5.11 case (c)).
    ///
    /// <para>A correction is an annotation. The original wording stays in the file and can still be
    /// read by anyone holding the workspace. <b>A designer whose problem is embarrassment specifically
    /// needs to know that</b>, and a UI that hid it would cause the exact harm they came to it to
    /// avoid — which is §1.4's false belief, in the one place where the belief is about other people
    /// rather than about their own data. A delete offered here would be §5.3b rule 10's reassuring
    /// sentence about a protection that does not exist, told to the designer this time instead of by
    /// an agent.</para>
    ///
    /// <para>Held by this brief's gate 7, in the shape RC-7 gate 11 uses on
    /// <see cref="RewritingIsYoursToDo"/>.</para>
    /// </summary>
    public const string ACorrectionDoesNotErase =
        "This adds a correction; it does not change what was written. The original wording stays in "
      + "the history and can still be read by anyone holding a copy of this workspace, including the "
      + "copies already sent. What the correction buys is that your wording is what they see first.";

    /// <summary>The same fact for the entry a designer CAN correct outright, so the two dialogs read
    /// as one feature with a line drawn through it rather than as two unrelated controls.</summary>
    public const string ThisOneHasNotLeftTheMachine =
        "This has not left this machine, so correcting it changes nothing for anybody else. What the "
      + "version holds — the files, who kept it and when — is untouched either way.";

    // ── R-rc11-16: the review before a history leaves the machine ─────────────────────────────────

    /// <summary>
    /// The heading over §5.11's review, for each of the three journeys (R-rc11-16, §12 Q36).
    ///
    /// <para><b>It is worth more than every correction mechanism</b> (R-rc11-17): the expensive case is
    /// not a careless word, it is a customer's name or a part number in a title going to a different
    /// customer, and nobody can recall from memory what forty titles say. §9A.3 already establishes
    /// that a computation the user cannot perform belongs in front of the operation.</para>
    /// </summary>
    public static string TitlesLeaving(int count, string journey) => count switch
    {
        0 => $"No titles are {journey}.",
        1 => $"One title is {journey}. Read it before you go on.",
        _ => $"{count} titles are {journey}. Read them before you go on.",
    };

    /// <summary>
    /// R-rc11-19. <b>It is not a confirmation prompt with a checkbox</b>, and the line under the list
    /// says so — the useful response to reading a bad title is fixing it, not abandoning the send.
    /// </summary>
    public const string TitlesLeavingWhatToDo =
        "A title is what the other side reads first. If one of these says something it should not, "
      + "close this and correct it from the History panel — the newest one can be retitled outright, "
      + "and any of them can take a correction.";

    /// <summary>The three journeys, spelled once so the same sentence serves all three. <b>Noun
    /// phrases with no verb of their own</b>, because the count supplies it — "one title <i>is</i>",
    /// "four titles <i>are</i>" — and a journey carrying its own verb reads wrong at one of the
    /// two.</summary>
    public const string JourneySend    = "about to be sent to the other copy";
    public const string JourneyCopy    = "about to be copied";
    public const string JourneyArchive = "in the history this archive would carry";

}
