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
    public sealed record ResultPayload(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        LoadpullSummaryJson? Summary,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, CubeJson>>? Groups);

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
