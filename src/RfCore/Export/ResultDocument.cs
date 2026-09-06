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
        ReferenceReportJson? Reference = null);

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
    public sealed record ReferencePortsJson(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int?                  Count,
        IReadOnlyList<string> Names,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string?               DeterminedBy,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        int?                  ListedAt);

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
