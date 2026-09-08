// ================================================================
//  ResultDocument.cs — the ONE structured shape every verb's `--json` emits.
//
//  brief-automation-1-structured-output.md R-aut1-3: one schema across every verb, so a caller that
//  can read one verb's output can find its way around another's. R-aut1-9 puts it here rather than
//  in src/Cli, because a protocol adapter must be able to emit the identical document without
//  reaching into a console program (docs/design/automation-architecture.md R-aut-13).
//
//  WHAT THIS FILE DOES NOT DO: decide anything. Every number in it came from a DataCube or from
//  LoadpullResultSummary, unrounded and unscaled. R-aut1-2 — the human column widths, the dB and
//  percent presentation, the row truncation, are all terminal concerns and none of them is encoded
//  here.
// ================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using CircuitRF.Diagnostics;
using RfCore.Data;
using RfCore.Loadpull;

namespace RfCore.Export
{
    /// <summary>How the run ended, in the three states a caller has to tell apart.</summary>
    public static class ResultStatus
    {
        /// <summary>Ran, and produced something usable.</summary>
        public const string Ok = "ok";
        /// <summary>Ran, but did not converge — <c>cli.md</c> §7's exit code 2, whose test is
        /// deliberately per-verb (R-aut-8).</summary>
        public const string NotConverged = "not-converged";
        /// <summary>Could not run, refused, or was stopped.</summary>
        public const string Failed = "failed";

        /// <summary>The mapping the CLI's own exit codes already imply, in one place.</summary>
        public static string FromExitCode(int exitCode) => exitCode switch
        {
            0 => Ok,
            2 => NotConverged,
            _ => Failed,
        };
    }

    /// <param name="Version">circuitRF's own version, so a document found on disk says which build
    /// produced it.</param>
    /// <param name="Verb">The verb as typed.</param>
    public sealed record ResultHeader(string Version, string Verb);

    /// <param name="Path">The input file, as given.</param>
    /// <param name="Analysis">
    /// The chain that ACTUALLY ran after <c>SelectTop</c>'s promotion (<c>cli.md</c> §4) — not what
    /// was requested. A caller that asked for an inner analysis and got its wrapper must be able to
    /// see that from the document alone, because the difference is a whole sweep axis.
    /// </param>
    public sealed record ResultInput(string? Path, string? Analysis);

    /// <param name="Kind">What the file is — <c>touchstone</c>, <c>npy</c>, <c>mat</c>, <c>tsv</c>,
    /// <c>spl</c>, <c>lpcwave</c>, <c>gamma-grid</c>, <c>layout</c>.</param>
    public sealed record ResultOutput(string Kind, string Path);

    /// <param name="Name">The axis name, which is what the grid axis is located BY — never its
    /// position, since a sweep prepends one axis per nesting level.</param>
    public sealed record AxisJson(string Name, string Unit, int Length, double[] Values, string[]? Labels);

    /// <summary>
    /// One cube. <paramref name="Kind"/> is the <see cref="DataKind"/>, and it decides what
    /// <paramref name="Values"/> holds: bare numbers for a real cube, <c>[re, im]</c> pairs for a
    /// complex one. There is no third encoding and the imaginary part is never dropped.
    /// </summary>
    public sealed record CubeJson(string Kind, IReadOnlyList<AxisJson> Axes, object Values);

    /// <param name="Groups">
    /// The <see cref="DataSet"/> as it stands: groups, then named cubes. The default group's key is
    /// the empty string, which is the group's own name — the console renders it as "(default)", and
    /// that is a console decision.
    /// </param>
    /// <param name="Summary">
    /// A loadpull's or pursuit's one-row-per-grid-point projection, when the result is one. Present
    /// for the same reason the console prints it rather than the cubes: it is the useful projection,
    /// not a terminal compromise (R-aut1-5).
    /// </param>
    /// <param name="Check">
    /// What <c>check</c> looked at and what it found, in counts. The findings themselves are
    /// <c>diagnostics</c> — this is the tally, so a caller can tell "checked nothing" from
    /// "checked everything and it was clean" without counting an array
    /// (brief-automation-4-check-and-explain.md R-aut4-10).
    /// </param>
    /// <param name="Explain">
    /// What <c>explain</c> resolved, and the WALK it performed to get there. The walk is not
    /// decoration: two of these start from different files and can legitimately land on different
    /// workspaces (<c>cli.md</c> §8.1), and that is exactly the thing a caller cannot otherwise see.
    /// </param>
    /// <param name="Document">
    /// What <c>read</c> handed back when the path named one of circuitRF's own documents rather than
    /// a result file. See <see cref="DocumentJson"/>.
    /// </param>
    /// <param name="Reference">
    /// What <c>reference</c> was asked: the topic list, one topic's text, or the component
    /// catalogue. It is about no document at all, which is why it is its own payload and not a mode
    /// of <c>explain</c> — every <c>explain</c> answer is anchored to a path
    /// (brief-automation-6-reference-and-components.md §5).
    /// </param>
    public sealed record ResultPayload(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        LoadpullSummaryJson? Summary,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, CubeJson>>? Groups,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CheckReportJson? Check = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ExplainReportJson? Explain = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        DocumentJson? Document = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ReferenceReportJson? Reference = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        HistoryReportJson? History = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        RenderReportJson? Render = null);

    /// <summary>
    /// One of circuitRF's own documents, read back verbatim.
    ///
    /// <para><b>Verbatim is the whole point.</b> The formats are the interface
    /// (<c>automation-architecture.md</c> §4) — a client authors a design by WRITING one of these
    /// files — so what it needs back is the file, not a re-serialization of a model parsed out of
    /// it. A round trip through a reader and a writer would hand back something that differs from
    /// what is on disk wherever the reader is lossy, and the difference would be invisible.</para>
    ///
    /// <para>A result file does not come back this way: it becomes <see cref="Groups"/>, through the
    /// same readers the GUI's own source library uses, because a <c>.npy</c> or a Touchstone answers
    /// a question about numbers rather than about text.</para>
    /// </summary>
    /// <param name="Kind">What the path was taken to be, spelled as <c>check</c> spells it.</param>
    /// <param name="Text">The file's content, exactly as it is on disk.</param>
    public sealed record DocumentJson(string Path, string Kind, string Text);

    // ── check and explain, on the wire (R-aut4-10) ───────────────────────────

    /// <param name="Kind">
    /// What the path was taken to BE — <c>workspace</c>, <c>cell</c>, <c>schematic</c>,
    /// <c>symbol</c>, <c>layout</c>, <c>technology</c>, <c>em-setup</c>, <c>netlist</c>,
    /// <c>assembly-rules</c>, <c>folder</c>. Inferred from the path exactly as <c>convert</c> infers
    /// a format (R-aut4-11): by extension, and for a directory by what it contains.
    /// </param>
    public sealed record CheckedDocumentJson(string Path, string Kind, int Errors, int Warnings);

    /// <param name="Severity">
    /// The threshold the exit code was decided at — <c>warning</c> or <c>error</c>. Carried because
    /// a document holding warnings and <c>exitCode: 0</c> is only readable next to the threshold
    /// that made it so (R-aut4-5).
    /// </param>
    public sealed record CheckReportJson(
        string                             Root,
        string                             Severity,
        int                                DocumentsChecked,
        int                                Errors,
        int                                Warnings,
        int                                Notes,
        IReadOnlyList<CheckedDocumentJson> Documents);

    /// <summary>
    /// One step of a resolution walk: what was being resolved, what it started from, what it landed
    /// on, and by which rule. <paramref name="Resolved"/> is null when the step found nothing —
    /// which is an answer, not an omission (R-aut4-8).
    /// </summary>
    public sealed record ResolutionStepJson(
        string  Step,
        string? From,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Resolved,
        string  How);

    /// <param name="Scale">
    /// What the stated unit's coefficients were multiplied by to reach base SI. Reported ALONGSIDE
    /// <paramref name="StatedUnit"/> and the base-SI numbers, because reading a unit's mark without
    /// its scale has already produced a sweep that ran at 2 Hz and looked entirely normal (R-aut4-9).
    /// </param>
    /// <param name="Step">
    /// The step SIZE in base SI for a step-size sweep; null for a point-count or explicit-list sweep,
    /// where there is no step to state and a computed one would be an invention.
    /// </param>
    public sealed record ExplainSweepJson(
        string  Variable,
        int     Points,
        double  Start,
        double  Stop,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        double? Step,
        string  BaseUnit,
        string  StatedUnit,
        double  Scale,
        string  Kind);

    /// <param name="Chain">The chain from this root inward, outermost first.</param>
    /// <param name="Dispatched">
    /// True for the one chain <c>SelectTop</c> would run. False on every other, INCLUDING a runnable
    /// one that simply is not first — which is what makes the ambiguity visible without a run.
    /// </param>
    /// <param name="DispatchedBy">
    /// WHICH verb would run it — <c>hb</c>, <c>lp</c> or <c>lpp</c>. Chain selection is per KIND, so
    /// one netlist can declare an HB chain and a loadpull chain and each verb dispatches its own;
    /// a single "dispatched" flag with no verb beside it would read as a claim that only one runs.
    /// </param>
    /// <param name="PromotedFrom">
    /// The inner analysis whose name would have been promoted to this chain. Non-null only when the
    /// caller named one, and the whole reason this option exists (<c>cli.md</c> §4).
    /// </param>
    public sealed record ExplainAnalysisJson(
        string                Name,
        string                Kind,
        bool                  Enabled,
        bool                  Runnable,
        bool                  IsRoot,
        IReadOnlyList<string> Chain,
        bool                  Dispatched,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string?               DispatchedBy,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string?               PromotedFrom,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ExplainSweepJson?     Sweep);

    /// <param name="Kind">The <c>ValueKind</c> — <c>real</c>, <c>complex</c>, <c>bool</c>, or
    /// whatever else the engine produced. Reported rather than coerced: a Bool forced to a number is
    /// a different answer.</param>
    /// <param name="Text">The engine's own rendering, so a caller sees what an expression that is
    /// not a number at all evaluated to.</param>
    public sealed record ExplainExpressionJson(
        string    Expression,
        string    Kind,
        string    Text,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        double?   Real,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        double[]? Complex,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        bool?     Boolean);

    /// <param name="State">
    /// <c>resolved</c>, <c>not-found</c>, or <c>primary-missing</c> — the three states
    /// <c>CellSymbolResolver</c> already keeps distinct, forwarded rather than collapsed.
    /// </param>
    /// <param name="OutsideWorkspace">
    /// True when the reference resolves to somewhere outside the referring document's own workspace.
    /// Null when there is no workspace to be outside of.
    /// </param>
    public sealed record ExplainReferenceJson(
        string  Ref,
        string  From,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ResolvedPath,
        string  State,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        bool?   OutsideWorkspace,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Redirect);

    public sealed record ExplainReportJson(
        string                              Path,
        string                              Kind,
        IReadOnlyList<ResolutionStepJson>   Walks,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ExplainAnalysisJson>? Analyses,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ExplainExpressionJson?              Expression,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ExplainReferenceJson?               Reference);

    // ── reference, on the wire (brief-automation-6-reference-and-components.md) ──

    /// <param name="Topic">The name a caller asks for, which is also the resource URI's last
    /// segment.</param>
    /// <param name="Bytes">
    /// How large the topic's text is, in UTF-8 bytes, AS SERVED — after the front matter and the
    /// documentation generator's placeholders are removed, not the authored file's size.
    ///
    /// <para>Carried because reading is the expensive direction and a client pays for every byte
    /// (<c>automation-architecture.md</c> R-aut-10). A list that hides the cost makes the cheap
    /// topics and the expensive ones look alike, and the spread here is roughly fifteen-fold
    /// (R-aut6-3).</para>
    /// </param>
    /// <param name="Text">The topic itself. Absent in a LISTING, present when one topic was
    /// asked for — so a list costs a line per topic rather than the whole library.</param>
    public sealed record ReferenceTopicJson(
        string  Topic,
        string  Title,
        string  Summary,
        int     Bytes,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Text = null);

    /// <param name="Expression">The default as an EXPRESSION — the string a freshly-placed component
    /// carries, not an evaluated number.</param>
    /// <param name="Meaning">What the parameter is for, where the registry knows. Absent where it
    /// does not: an invented meaning is worse than none (R-aut6-8).</param>
    public sealed record ReferenceParameterJson(
        string  Name,
        string  Expression,
        string  Unit,
        string  Dimension,
        bool    ShowOnSchematic,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Meaning);

    /// <summary>
    /// How many nets an instance line writes, and — where that is not fixed — what decides it.
    ///
    /// <para><b><paramref name="Count"/> and <paramref name="DeterminedBy"/> are mutually
    /// exclusive, and BOTH may be absent.</b> A number printed where the truth is "it depends" is
    /// the failure class <c>sweep-unit-scale-and-mark</c> records — plausible, specific and wrong,
    /// with nothing reporting it (R-aut6-9).</para>
    /// </summary>
    /// <param name="Names">The terminals, in the order a <c>.cnl</c> line writes their nets.</param>
    /// <param name="ListedAt">The port count <paramref name="Names"/> was listed at, when the count
    /// is parameter-determined — so an example cannot be read as an answer.</param>
    /// <param name="OrderNote">What the terminal ORDER means: whether the two ends may be swapped,
    /// and what tells them apart when they may not. Absent where nobody has stated it. Most
    /// two-terminal parts have no terminal NAMES to give, so this is the only thing
    /// <paramref name="Names"/> could not already say — without it the terminal listing for an R, an
    /// L or a C restates its own index and nothing else.</param>
    public sealed record ReferencePortsJson(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int?                  Count,
        IReadOnlyList<string> Names,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string?               DeterminedBy,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int?                  ListedAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string?               OrderNote = null);

    /// <param name="Kind">The palette entry's own name — what the editor and the <c>.csch</c> call
    /// it, which is not always what the <c>.cnl</c> calls it.</param>
    /// <param name="SearchTerms">How a person goes looking for this part, so a client can find "the
    /// thing that does X" without reading the whole catalogue.</param>
    public sealed record ReferenceSymbolJson(
        string                                  Kind,
        string                                  DisplayName,
        string                                  Category,
        IReadOnlyList<string>                   SearchTerms,
        ReferencePortsJson                      Ports,
        IReadOnlyList<ReferenceParameterJson>   Parameters);

    /// <param name="Type">The token a <c>.cnl</c> writes — the thing a caller cannot guess and is
    /// blocked without.</param>
    /// <param name="Simulatable">False for a token the palette draws and the engine cannot build.</param>
    /// <param name="Placeable">False for a token a <c>.cnl</c> may write that nothing draws.</param>
    /// <param name="Note">Why the two disagree, when they do. The mismatch is part of the answer and
    /// is never filtered out (R-aut6-10).</param>
    public sealed record ReferenceComponentJson(
        string                               Type,
        bool                                 Simulatable,
        bool                                 Placeable,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string?                              Note,
        ReferencePortsJson                   Ports,
        IReadOnlyList<ReferenceSymbolJson>   Symbols);

    /// <summary>
    /// What <c>reference</c> answered. Exactly one of the three is present.
    /// </summary>
    /// <param name="Topics">The topic list, with each topic's served size.</param>
    /// <param name="Topic">One topic, with its text.</param>
    /// <param name="Components">The generated catalogue, or the one primitive that was asked for.</param>
    /// <summary>
    /// What <c>history</c> answered — the restore-point list, or what one boundary or one restore did
    /// (<c>docs/design/revision-control.md</c> §5.3d, RC-5 R-rc5-23).
    ///
    /// <para><b>No git vocabulary reaches this document</b> (R-rc0-6). What travels is what a designer
    /// would be shown: a time, a label, how it came about, and whether anything was left out of it. The
    /// identity of the underlying object is deliberately absent — this is the same surface the panel
    /// renders, and RC-7's explicit commit is the only place an identifier is ever named.</para>
    /// </summary>
    /// <param name="Points">The list, newest first, when the caller asked for one.</param>
    /// <param name="Recorded">Whether a boundary wrote anything. False when nothing had changed.</param>
    /// <param name="Point">The entry a boundary produced, or the one a restore went to.</param>
    /// <param name="FilesWritten">How many files a restore brought back.</param>
    /// <param name="FilesRemoved">How many a restore took away.</param>
    /// <param name="Versions">
    /// RC-7's narrative — the versions a designer kept deliberately, newest first. <b>A different list
    /// from <paramref name="Points"/> and never merged with it</b> (R-rc7-9): one is the sparse,
    /// human-written history that gets shared, the other the dense, machine-written safety net that
    /// does not leave the machine.
    /// </param>
    /// <param name="Version">The version an explicit commit produced.</param>
    /// <param name="Changes">What differs between two versions, at the granularity of documents
    /// (R-rc7-11). Naming a changed document is the answer; what changed inside one is not.</param>
    /// <param name="Copy">
    /// Where a copy of a workspace landed, and whether it is one (RC-9 R-rc9-1). <b>Named
    /// <c>Copy</c> rather than <c>Clone</c> for a language reason, not a vocabulary one</b> — a record
    /// may not declare a member called <c>Clone</c> — and it happens to match what the user-facing
    /// wording calls it.
    /// </param>
    /// <param name="Pins">
    /// RC-9's pins — which version of each referenced workspace this design is built against
    /// (R-rc9-8, R-rc9-9). <b>One entry per ALIAS, never per cell</b>: one referenced workspace is one
    /// repository with one commit identity, and a per-cell shape would let a build machine reproduce a
    /// design against two mutually inconsistent versions of one library.
    /// </param>
    /// <param name="Exchange">What a fetch or a send did (R-rc9-6).</param>
    public sealed record HistoryReportJson(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<RestorePointJson>? Points = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        bool?                            Recorded = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        RestorePointJson?                Point = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int?                             FilesWritten = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int?                             FilesRemoved = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<VersionJson>?      Versions = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        VersionJson?                     Version = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<DocumentChangeJson>? Changes = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        CloneJson?                         Copy = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<PinJson>?            Pins = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ExchangeJson?                      Exchange = null);

    /// <summary>Where a copy of a workspace landed (RC-9 R-rc9-1).</summary>
    /// <param name="Destination">The folder that was created.</param>
    /// <param name="Workspace">Its <c>.cws</c>, or absent when what arrived is not a workspace.</param>
    /// <param name="RestorePoints">
    /// Always <c>false</c>, and present rather than implied (R-rc9-5a, §5.2a). <b>A clone carries the
    /// narrative and not the safety net</b>, because git's default fetch takes branches and tags and
    /// nothing under a private namespace. The three ways a workspace leaves a machine disagree
    /// deliberately, and a caller who is not told will assume the strongest of the three.
    /// </param>
    public sealed record CloneJson(
        string  Destination,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Workspace,
        bool    RestorePoints);

    /// <summary>
    /// One referenced workspace's pin — <b>the identity a build machine reproduces a signed-off result
    /// from</b>, which is the reason RC-9 has a headless spelling at all (R-rc9-20).
    /// </summary>
    /// <param name="Alias">The name <c>ws://alias/…</c> uses.</param>
    /// <param name="Pin">The identity this design is built against, or absent when unpinned.</param>
    /// <param name="Status">
    /// <c>unpinned</c>, <c>current</c>, <c>newer-available</c>, <c>cannot-be-honoured</c> or
    /// <c>no-history-there</c>. <b><c>cannot-be-honoured</c> is a failure and never a fall-back</b>
    /// (R-rc9-16): the cells behind that alias do not resolve, and reporting the newest version instead
    /// would be the silent wrong answer the pin exists to prevent.
    /// </param>
    /// <param name="Newest">The newest identity that workspace has, when it keeps a history.</param>
    public sealed record PinJson(
        string  Alias,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Pin,
        string  Status,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Newest);

    /// <summary>What a fetch or a send did (RC-9 R-rc9-6).</summary>
    /// <param name="Remote">What the other copy is called.</param>
    /// <param name="Changed">Whether anything actually moved.</param>
    public sealed record ExchangeJson(string Remote, bool Changed);

    /// <summary>
    /// One version a designer kept (RC-7 R-rc7-1, R-rc7-5).
    ///
    /// <para><b>This is the one place in the whole feature where an object identity travels</b>, and
    /// R-rc7-4 is why: the caller asked for this commit, and the identifier is what makes §4.1's
    /// escape hatch usable. The restore-point list beside it carries none, deliberately.</para>
    /// </summary>
    /// <param name="Id">Its identity — what to give anyone helping outside circuitRF.</param>
    /// <param name="Kept">When, in ISO-8601 UTC.</param>
    /// <param name="Title">The line the designer wrote.</param>
    /// <param name="Who">Who kept it.</param>
    /// <param name="RestoredFrom">
    /// What the workspace had been brought back from when this was kept, or absent. <b>Present on
    /// exactly the versions that need it</b> (R-rc7-6): without it, two consecutive versions where the
    /// second reverts the first read as a change of mind with no record of the moment.
    /// </param>
    public sealed record VersionJson(
        string  Id,
        string  Kept,
        string  Title,
        string  Who,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? RestoredFrom = null);

    /// <summary>One document that differs between two versions.</summary>
    /// <param name="Path">Where it is in the workspace.</param>
    /// <param name="Change"><c>added</c>, <c>changed</c>, <c>removed</c> or <c>renamed</c>.</param>
    /// <param name="Was">Where it used to be, on a rename.</param>
    public sealed record DocumentChangeJson(
        string  Path,
        string  Change,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Was = null);

    /// <summary>One entry, as the list and the panel both show it.</summary>
    /// <param name="Sequence">circuitRF's own monotonic ordering. <b>Not the clock</b> — a wall clock
    /// is user-writable state, and it supplies the label a human reads and nothing else.</param>
    /// <param name="Taken">When, in ISO-8601 UTC, for that label.</param>
    /// <param name="Origin">How it came about: <c>save-point</c>, <c>workspace-closed</c>,
    /// <c>before-batch</c>, <c>before-restore</c>, <c>recording-off</c> or <c>recording-on</c>.</param>
    /// <param name="Label">The line a designer reads.</param>
    /// <param name="Kept">Whether retention may never thin it.</param>
    /// <param name="LeftOut">Paths left out at a boundary nobody was at. A non-empty list means the
    /// entry is INCOMPLETE and says so.</param>
    /// <param name="Thinned">
    /// Whether retention has tidied this one away. <b>It is still listed and still restorable</b> —
    /// thinning drops the pointer and leaves the state, and nothing reclaims it unless a person asks.
    /// Omitted when false, which is every entry written before RC-6.
    /// </param>
    public sealed record RestorePointJson(
        long                  Sequence,
        string                Taken,
        string                Origin,
        string                Label,
        bool                  Kept,
        IReadOnlyList<string> LeftOut,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        bool                  Thinned = false);

    public sealed record ReferenceReportJson(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ReferenceTopicJson>?     Topics,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ReferenceTopicJson?                    Topic,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<ReferenceComponentJson>? Components);

    // ── the loadpull summary, on the wire ────────────────────────────────────

    /// <param name="ConsoleScale">What the TERMINAL multiplies <c>value</c> by. Carried so a reader
    /// can reproduce the printed table exactly; <c>value</c> itself is the engine's own number.</param>
    public sealed record PursuitOptimumJson(
        string    Tag,
        bool      Converged,
        string    ValueCube,
        double    Value,
        double    ConsoleScale,
        string    Unit,
        double[]  ZLoad,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        double[]? ZSource);

    public sealed record PursuitOptimaJson(
        PursuitOptimumJson Mxp,
        PursuitOptimumJson Mxe,
        double             Queried,
        double             Unscorable,
        double             Recommended);

    public sealed record FomColumnJson(
        string  Column,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? SourceCube,
        double  ConsoleScale);

    /// <param name="DriveIndex">The drive step the FOMs were read at; <c>-1</c> when this point has
    /// no converged, non-tickle step at all, which is what makes every FOM NaN.</param>
    public sealed record GridRowJson(
        long                              Sweep,
        int                               Index,
        double                            StopCode,
        string                            Stop,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        double[]?                         GammaLoad,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        double[]?                         ZLoad,
        int                               DriveIndex,
        IReadOnlyDictionary<string, double> Fom);

    public sealed record GridSummaryJson(
        int                            GridPoints,
        long                           SweepPoints,
        int                            PinSteps,
        int                            Compressed,
        int                            MaxDrive,
        int                            NotConverged,
        IReadOnlyList<AxisJson>        SweepAxes,
        IReadOnlyList<FomColumnJson>   Columns,
        IReadOnlyList<GridRowJson>     Rows);

    public sealed record LoadpullSummaryJson(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string?             Group,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        PursuitOptimaJson?  Optima,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        GridSummaryJson?    Grid);

    // ── render, on the wire (brief-render-2-render-verb.md R-rnd2-11) ────────

    /// <summary>
    /// What <c>render</c> DECIDED, which is the half a caller cannot see by looking at the file it got
    /// back.
    ///
    /// <para><b>Every field here answers a question the picture itself cannot.</b> A caller that asked
    /// for <c>--fit</c> has no way to know what was fitted; one that asked for a window has no way to
    /// know whether it was letterboxed; one that misspelled a layer would otherwise see a picture
    /// missing that layer and be unable to tell it from a layer that is genuinely empty. The refusals
    /// close most of that (R-rnd2-7), and this closes the rest.</para>
    ///
    /// <para><b>There is no duration.</b> <see cref="Counters"/> is <c>LayoutRenderResult</c>'s own
    /// work count — deterministic and machine-independent by construction — which is what lets a gate
    /// assert about work done rather than about a shared runner's wall clock
    /// (<c>feedback-no-new-timing-benchmark-tests</c>).</para>
    /// </summary>
    /// <param name="Kind">What the path was taken to be, spelled as <c>check</c> spells it.</param>
    /// <param name="View">Which view of a cell folder was drawn, or absent for a file named directly.</param>
    /// <param name="Layers">Layout only. Absent for a schematic or a symbol, which have no layers —
    /// an empty list there would read as "this document defines none", which is a claim.</param>
    /// <param name="Detail">Layout only, for the same reason: the LOD tiers <c>--detail</c> governs
    /// are <c>LayoutRenderer</c>'s.</param>
    /// <param name="Bytes">The size of the file written. Reported beside
    /// <see cref="RenderCountersJson.VerticesEmitted"/> because those two together are the whole of
    /// what <c>--detail</c> trades (R-rnd2-6): an undecimated vector export of a real board carries
    /// ~7.6x the vertices of the on-screen one, and every one of them is in the file.</param>
    public sealed record RenderReportJson(
        string                          Path,
        string                          Kind,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string?                         View,
        string                          Format,
        RenderViewportJson              Viewport,
        RenderExtentsJson               Extents,
        RenderSizeJson                  Size,
        RenderThemeJson                 Theme,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyList<RenderLayerJson>? Layers,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        RenderDetailJson?               Detail,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        RenderCountersJson?             Counters,
        long                            Bytes);

    /// <param name="Mode"><c>fit</c>, <c>window</c> or <c>center</c> — which of the three the caller
    /// asked for. The three are refused TOGETHER rather than ordered (R-rnd2-3), so exactly one is
    /// ever in force and naming it costs nothing.</param>
    /// <param name="Letterboxed">
    /// True when the requested window's aspect differed from the output's and the extra was filled
    /// rather than cropped (R-rnd2-5). <b>The four coordinates are the window AFTER that</b> — what
    /// the picture actually shows. A caller that asked for a region and silently got less of it than
    /// it asked for has no way to notice; one that got more can see it here.
    /// </param>
    /// <param name="Unit">
    /// The BASE SI unit the four coordinates are in — <c>m</c> for a layout, <c>design-units</c> for a
    /// schematic or a symbol, whose coordinates are dimensionless and are reported as what they are
    /// rather than dressed up in metres (R-rnd0-5, R-rnd2-4).
    /// </param>
    /// <param name="Scale">
    /// What multiplies the DOCUMENT's own display unit to reach <paramref name="Unit"/> — 1e-6 for a
    /// layout drawn in micrometres, 2.54e-5 for one drawn in mils, 1 where the coordinates are
    /// dimensionless. Carried for the reason <c>explain --analysis</c> carries it: a mark read without
    /// its scale has already produced a run at 2 Hz that looked entirely normal.
    /// </param>
    /// <param name="Zoom">Output units per world unit — device pixels per DBU for a png, points per
    /// DBU for an svg or a pdf.</param>
    public sealed record RenderViewportJson(
        string Mode,
        double X0, double Y0, double X1, double Y1,
        string Unit,
        double Scale,
        double Zoom,
        bool   Letterboxed);

    /// <summary>The whole document's extents, in the same base SI units
    /// <see cref="RenderViewportJson"/> reports — so a caller that windowed can see how much of the
    /// document it asked for, and one that fitted can see what was fitted.</summary>
    public sealed record RenderExtentsJson(
        double X0, double Y0, double X1, double Y1, string Unit, double Scale);

    /// <param name="UnitKind"><c>device-pixels</c> for a png, <c>points</c> for an svg or a pdf. The
    /// two are not interchangeable and a bare number would leave a caller to guess which it got.</param>
    /// <param name="Scale">The raster multiplier <c>--scale</c>/<c>--dpi</c> resolved to. Always 1 for
    /// a vector format, which has no pixels to multiply.</param>
    public sealed record RenderSizeJson(int Width, int Height, string UnitKind, double Scale);

    /// <param name="ResolvedFrom">
    /// WHICH step of <c>ThemeResolver</c>'s chain answered — <c>file</c>, <c>workspace</c>,
    /// <c>user</c>, <c>shipped</c>. The chain is four steps deep and its last step always succeeds, so
    /// without this a theme that quietly fell through to the built-in palette is indistinguishable
    /// from one that resolved (R-rnd2-9).
    /// </param>
    public sealed record RenderThemeJson(string Name, string Variant, string ResolvedFrom);

    /// <param name="Rendered">Whether the layer was drawn. False for one the technology marks
    /// invisible, and for one <c>--layers</c>/<c>--hide-layers</c> excluded.</param>
    /// <param name="Shapes">Shapes on that layer in the document, drawn or not — so an empty layer
    /// and an excluded one are two different answers.</param>
    public sealed record RenderLayerJson(string Name, bool Rendered, int Shapes);

    /// <param name="Mode"><c>full</c>, <c>screen</c>, or the pixel budget as written.</param>
    /// <param name="ToleranceDbu">
    /// The decimation tolerance the budget actually resolved to at this zoom, in DBU. Reported because
    /// <c>LayoutRenderDetail</c> buckets it DOWN to a power of two — so the effective tolerance is not
    /// the number the caller typed, and the difference is up to a factor of two. Absent where nothing
    /// decimates.
    /// </param>
    public sealed record RenderDetailJson(
        string Mode,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        long?  ToleranceDbu);

    /// <summary><c>LayoutRenderResult</c>'s own per-frame work counters, forwarded unchanged. Layout
    /// only — the schematic and symbol renderers keep none.</summary>
    public sealed record RenderCountersJson(
        int ShapesExamined,
        int ShapesDrawn,
        int VerticesEmitted,
        int InstancesExamined,
        int InstancesDrawn,
        int PathsConstructed,
        int DrawCalls,
        int LayersVisited);

    // ── the document ─────────────────────────────────────────────────────────

    /// <summary>
    /// One JSON document per invocation, on stdout, and nothing else on stdout. A FAILED run still
    /// emits one — the failure is the payload — so a caller never has to tell "no output" apart from
    /// "output I could not parse".
    /// </summary>
    public sealed record ResultDocument(
        ResultHeader                  Circuitrf,
        ResultInput                   Input,
        string                        Status,
        int                           ExitCode,
        IReadOnlyList<ResultOutput>   Outputs,
        IReadOnlyList<DiagnosticJson> Diagnostics,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        ResultPayload?                Result);

    /// <summary>
    /// Builds and writes a <see cref="ResultDocument"/>. Nothing here consults a culture, a console
    /// width or a language setting.
    /// </summary>
    public static class ResultDocumentWriter
    {
        /// <summary>
        /// The one set of options, matching what the persistence types already do — enum-as-string
        /// is moot here (every enum is projected to a string by hand, so the wire spelling is this
        /// file's decision rather than C#'s casing), nulls are omitted, and numbers are written at
        /// full round-trippable double precision by System.Text.Json's default.
        ///
        /// <para><b>NaN and infinity are written as the named literals</b>
        /// (<see cref="JsonNumberHandling.AllowNamedFloatingPointLiterals"/>, so <c>"NaN"</c>) and
        /// that is not optional: a loadpull grid genuinely contains NaN wherever a point never
        /// converged, JSON has no number for it, and dropping the key or substituting a zero would
        /// turn "no measurement" into a measurement.</para>
        /// </summary>
        public static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented          = true,
            PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling         = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters             = { new JsonStringEnumConverter() },
        };

        public static string Serialize(ResultDocument doc) => JsonSerializer.Serialize(doc, Options);

        // ── projections ──────────────────────────────────────────────────────

        public static AxisJson ToJson(Axis a) => new(a.Name, a.Unit, a.Length, a.Values, a.Labels);

        public static CubeJson ToJson(DataCube c)
        {
            var axes = c.Axes.Select(ToJson).ToArray();
            object values = c.DataKind == DataKind.Real
                ? c.RealValues
                : c.ComplexValues.Select(z => new[] { z.Real, z.Imaginary }).ToArray();
            return new CubeJson(c.DataKind == DataKind.Real ? "real" : "complex", axes, values);
        }

        /// <summary>
        /// Every group and every cube, narrowed by <paramref name="groups"/> and
        /// <paramref name="cubes"/> when either is given (R-aut1-4). An unknown name is skipped
        /// silently on BOTH sides, matching <see cref="DataSetSubset.SelectGroups"/>' existing
        /// behaviour rather than inventing a second rule for the cube half.
        ///
        /// <para>The <c>__</c>-prefixed internal cubes are kept. The console skips them because they
        /// carry label metadata the axes already show; a machine caller asked for the DataSet, and
        /// this is what the DataSet holds.</para>
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, CubeJson>> ToJson(
            DataSet ds, IReadOnlyCollection<string>? groups = null, IReadOnlyCollection<string>? cubes = null)
        {
            var outp = new Dictionary<string, IReadOnlyDictionary<string, CubeJson>>(StringComparer.Ordinal);
            foreach (string g in ds.Groups)
            {
                if (groups is { Count: > 0 } && !groups.Contains(g, StringComparer.Ordinal)) continue;

                var into = new Dictionary<string, CubeJson>(StringComparer.Ordinal);
                foreach (var (name, cube) in ds.CubesIn(g))
                {
                    if (cubes is { Count: > 0 } && !cubes.Contains(name, StringComparer.Ordinal)) continue;
                    into[name] = ToJson(cube);
                }
                // A group narrowed to nothing is omitted rather than emitted empty: an empty object
                // reads as "this group holds no cubes", which is a claim about the run.
                if (into.Count > 0 || (cubes is null or { Count: 0 })) outp[g] = into;
            }
            return outp;
        }

        public static PursuitOptimumJson ToJson(PursuitOptimum o) => new(
            o.Tag, o.Converged, o.ValueCube, o.Value, o.ValueScale, o.ValueUnit,
            [o.ZRe, o.ZIm],
            o.HasZsource ? [o.ZsourceRe, o.ZsourceIm] : null);

        public static GridSummaryJson ToJson(LoadpullGridSummary s)
        {
            var columns = s.Columns.Select(c => new FomColumnJson(c.Column, c.SourceCube, c.Scale)).ToArray();
            var rows = s.Rows.Select(r =>
            {
                var fom = new Dictionary<string, double>(s.Columns.Count, StringComparer.Ordinal);
                for (int i = 0; i < s.Columns.Count; i++) fom[s.Columns[i].Column] = r.Fom[i];
                return new GridRowJson(
                    r.Outer, r.Index, r.StopCode, LoadpullResultSummary.StopName(r.StopCode),
                    r.GammaLoad is { } g ? [g.Real, g.Imaginary] : null,
                    r.ZLoad     is { } z ? [z.Real, z.Imaginary] : null,
                    r.DriveIndex, fom);
            }).ToArray();

            return new GridSummaryJson(
                s.GridPoints, s.OuterPoints, s.PinSteps,
                s.Compressed, s.MaxDrive, s.NotConverged,
                s.OuterAxes.Select(ToJson).ToArray(), columns, rows);
        }

        /// <summary>
        /// The loadpull projection for whichever group carries the surface — searched for by name
        /// rather than assumed to be the default, because a swept run leaves the cubes in the
        /// sweep's own group. Null when this DataSet is not a loadpull at all.
        /// </summary>
        public static LoadpullSummaryJson? SummarizeLoadpull(DataSet ds)
        {
            foreach (string g in ds.Groups)
            {
                var cubes = ds.CubesIn(g);
                bool pursuit = LoadpullResultSummary.HasPursuit(cubes);
                bool grid    = LoadpullResultSummary.HasGrid(cubes);
                if (!pursuit && !grid) continue;

                var optima = pursuit
                    ? LoadpullResultSummary.SummarizePursuit(cubes) is { } p
                        ? new PursuitOptimaJson(ToJson(p.Mxp), ToJson(p.Mxe), p.Queried, p.Unscorable, p.Recommended)
                        : null
                    : null;
                var gridJson = grid && LoadpullResultSummary.SummarizeGrid(cubes) is { } gs ? ToJson(gs) : null;
                if (optima is null && gridJson is null) continue;

                return new LoadpullSummaryJson(g, optima, gridJson);
            }
            return null;
        }
    }
}
