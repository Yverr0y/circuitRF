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
        ("title", (object?)title), ("id", Short(commitId)));

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
    /// </summary>
    public const string RewritingIsYoursToDo =
        "Something already kept stays in the history for good — a version you kept is a record, and "
      + "circuitRF will not alter one after the fact. If a file is in there that must not be (a large "
      + "one you regret, or something that should never have left your machine), the git command line "
      + "can rewrite a history to remove it. circuitRF deliberately offers no button for that: "
      + "rewriting replaces every entry, so every copy anyone else has taken of this workspace stops "
      + "matching and cannot be brought back into line. It is worth doing when it is worth that.";

    /// <summary>The whole identity, shortened to what a person can retype without transcribing it
    /// wrongly. Long enough to be unambiguous in any workspace a designer will ever have.</summary>
    private static string Short(string commitId)
        => commitId.Length > 12 ? commitId[..12] : commitId;
}
