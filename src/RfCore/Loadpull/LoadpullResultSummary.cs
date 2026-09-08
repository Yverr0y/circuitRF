// ================================================================
//  LoadpullResultSummary.cs — the SELECTION a loadpull result's summary is made of,
//  returned as values rather than printed.
//
//  brief-automation-1-structured-output.md R-aut1-1: "The JSON is a projection of the same values
//  the human printer reads — never a second computation." The CLI's PrintLoadpullGrid and
//  PrintPursuitOptima each made several load-bearing decisions on the way to a column of text —
//  which drive step to report, which of two cube spellings to read, whether efficiency arrives as a
//  fraction or a percent — and those decisions are the summary. Once they exist twice they diverge,
//  and the first person to change one has no way to know about the other.
//
//  So they live here, once, and return values. The console printer formats them; the --json document
//  serializes them. Neither re-derives anything.
//
//  WHY RfCore AND NOT src/Cli: R-aut1-9 — a protocol adapter must be able to emit the identical
//  document without reaching into the CLI. This reads nothing but DataCube, which is RfCore's own
//  type, so there is nothing to move down: it was already below the wall in everything but location.
// ================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using RfCore.Data;

namespace RfCore.Loadpull
{
    /// <summary>
    /// One figure-of-merit column of the grid summary: what it is called, which cube it was actually
    /// read out of, and the factor the CONSOLE multiplies by on the way to the screen.
    ///
    /// <para><b>The scale is carried, not applied.</b> R-aut1-2: a structured document emits the raw
    /// engine value and names its cube, because "70" and "0.7" are the same measurement in two
    /// presentations and only one of them is the number the engine computed. The terminal's column
    /// header says <c>DE%</c>, so the terminal is where the ×100 belongs.</para>
    /// </summary>
    /// <param name="Column">The summary's own name for the column — stable regardless of which
    /// spelling the run happened to publish.</param>
    /// <param name="SourceCube">The cube the values came from, or null when the run published
    /// neither spelling (every value in the column is then NaN).</param>
    /// <param name="Scale">What the console multiplies the raw value by.</param>
    /// <param name="Unit">
    /// What the RAW values in this column are in (AUT-9 R-aut9-3) — the unit of what is reported,
    /// not of what the console prints. The efficiency column is the reason it exists: it reads a
    /// percentage out of an enriched run's <c>Efficiency</c> and a fraction out of a pursuit's
    /// unenriched <c>DE</c>, and until this was carried the only way to tell the two apart was to
    /// notice which cube name <see cref="SourceCube"/> happened to name.
    /// </param>
    /// <param name="ConsoleUnit">What the terminal's column header says, after
    /// <paramref name="Scale"/>.</param>
    public sealed record LoadpullFomColumn(
        string Column, string? SourceCube, double Scale, string Unit, string ConsoleUnit);

    /// <summary>
    /// One Γ grid point: where it was, how it stopped, and its figures of merit at the drive step it
    /// stopped at. Values are RAW — unscaled, and NaN wherever the console prints an em dash.
    /// </summary>
    /// <param name="Outer">Index into the sweep points wrapping the grid; 0 for an unswept run.</param>
    /// <param name="Index">The point's index within the Γ grid.</param>
    /// <param name="StopCode">The engine's own wire encoding — see <see cref="LoadpullResultSummary.StopName"/>.</param>
    /// <param name="DriveIndex">
    /// The drive step the figures of merit were read at: the LAST converged, non-tickle step.
    /// <c>-1</c> when the point has none, which is what makes every FOM NaN.
    /// </param>
    /// <param name="Fom">Raw values, parallel to <see cref="LoadpullGridSummary.Columns"/>.</param>
    public sealed record LoadpullGridRow(
        long     Outer,
        int      Index,
        double   StopCode,
        Complex? GammaLoad,
        Complex? ZLoad,
        int      DriveIndex,
        double[] Fom);

    /// <summary>A loadpull grid, summarised one row per Γ point.</summary>
    public sealed record LoadpullGridSummary(
        int                                GridPoints,
        long                               OuterPoints,
        IReadOnlyList<Axis>                OuterAxes,
        int                                PinSteps,
        int                                Compressed,
        int                                MaxDrive,
        int                                NotConverged,
        IReadOnlyList<LoadpullFomColumn>   Columns,
        IReadOnlyList<LoadpullGridRow>     Rows);

    /// <summary>
    /// One of a pursuit's two answers. <paramref name="Value"/> is raw,
    /// <paramref name="ValueUnit"/> says what THAT number is in, and
    /// <paramref name="ValueScale"/> with <paramref name="ConsoleUnit"/> are the console's ×100 and
    /// its <c>%</c> — same rule as <see cref="LoadpullFomColumn"/>, and for the same reason.
    ///
    /// <para><b>Those used to be one field, and it was the wrong one</b> (AUT-9 R-aut9-3): the unit
    /// reported beside MXE's value was <c>%</c> while the value itself was a fraction, so a client
    /// that believed the label reported 0.7% for a 70.87% amplifier. The unit now describes the
    /// number it sits beside.</para>
    ///
    /// <para>A NON-converged optimum still carries its <c>Z</c>: that is the last termination the
    /// search looked at, and it is worth reporting. Its <paramref name="Value"/> is not — the engine
    /// publishes 0 there, and a zero beside "did not converge" reads as a measurement rather than as
    /// an absent number, which is why the console prints an em dash and a caller should read
    /// <paramref name="Converged"/> before <paramref name="Value"/>.</para>
    /// </summary>
    public sealed record PursuitOptimum(
        string  Tag,
        bool    Converged,
        string  ValueCube,
        double  Value,
        string  ValueUnit,
        double  ValueScale,
        string  ConsoleUnit,
        double  ZRe,
        double  ZIm,
        bool    HasZsource,
        double  ZsourceRe,
        double  ZsourceIm);

    /// <summary>What a pursuit found, and how much of the space it had to look at to find it.</summary>
    public sealed record LoadpullPursuitSummary(
        PursuitOptimum Mxp,
        PursuitOptimum Mxe,
        double         Queried,
        double         Unscorable,
        double         Recommended);

    /// <summary>
    /// Turns a loadpull or pursuit <see cref="DataSet"/> group into the values its summary is made
    /// of. Every method returns null on a group that carries no such result, which is the same test
    /// the console printer applies before printing anything.
    /// </summary>
    public static class LoadpullResultSummary
    {
        /// <summary>
        /// The engine's own StopCode encoding (LoadpullEngine.BuildLoadpullDataSet), mirrored here
        /// rather than shared as an enum: it is a wire value in a published DataCube, and a shared
        /// enum could be renumbered under a <c>.npy</c> written by an older build.
        /// </summary>
        public static string StopName(double code) => code switch
        {
            1 => "compressed",
            2 => "no converge",
            3 => "no seed",
            _ => "max drive",
        };

        /// <summary>True when this group holds a Γ-grid loadpull surface.</summary>
        public static bool HasGrid(IReadOnlyDictionary<string, DataCube> cubes)
            => cubes.ContainsKey("StopCode");

        /// <summary>True when this group holds a pursuit's optima.</summary>
        public static bool HasPursuit(IReadOnlyDictionary<string, DataCube> cubes)
            => cubes.ContainsKey("MXP_Converged") || cubes.ContainsKey("MXE_Converged");

        /// <summary>
        /// One row per Γ grid point, each read at its LAST converged, non-tickle drive step — which
        /// is the compression point when the point compressed, and the highest drive it managed
        /// otherwise. Reading a fixed drive index instead would report whatever rung happened to sit
        /// there, mixing compressed and uncompressed points in one column.
        ///
        /// <para>Returns null when the group is not a grid, or when it is one whose cubes do not
        /// line up — the console printer's own early returns, kept identical so that a result the
        /// table declines to print is a result the document declines to summarise.</para>
        /// </summary>
        public static LoadpullGridSummary? SummarizeGrid(IReadOnlyDictionary<string, DataCube> cubes)
        {
            if (!cubes.TryGetValue("StopCode", out var stop)) return null;
            if (stop.DataKind != DataKind.Real) return null;

            // A SWEPT loadpull prepends one axis per nesting level, so the grid axis is not
            // necessarily the first — and the whole result is not one grid. Found by NAME rather
            // than by position: taking the last axis would silently read a two-frequency run as one
            // grid of twice the size, whose rows are labelled with the wrong terminations for half
            // of it.
            int gridDim = -1;
            for (int d = 0; d < stop.Axes.Count; d++)
                if (stop.Axes[d].Name == "gridPoint") { gridDim = d; break; }
            if (gridDim < 0) gridDim = stop.Axes.Count - 1;
            if (gridDim < 0) return null;

            int  nGrid = stop.Axes[gridDim].Length;
            long outer = 1;
            for (int d = 0; d < gridDim; d++) outer *= stop.Axes[d].Length;
            var outerAxes = stop.Axes.Take(gridDim).ToList();

            var codes = stop.RealValues;

            var conv = Real(cubes, "Converged");
            if (conv is null) return null;
            int nPin = conv.Axes.Count > 0 ? conv.Axes[^1].Length : 1;
            if (nPin <= 0 || conv.RealValues.Length < outer * nGrid * nPin) return null;

            bool haveGamma = cubes.TryGetValue("GammaLoad", out var gammaCube)
                             && gammaCube.DataKind == DataKind.Complex;
            bool haveZ     = cubes.TryGetValue("ZLoad", out var zCube)
                             && zCube.DataKind == DataKind.Complex;

            var tickle = Real(cubes, "IsTickle");

            // BOTH spellings, because both reach here: a plain loadpull is Enriched (Pout_dBm /
            // Gt_dB / Efficiency-in-%) while a PURSUIT's follow-on grid is not, and carries the
            // engine's raw names with DE and PAE still as fractions. Reading only one set produces a
            // table of em dashes for the other — which looks like a run that produced no figures of
            // merit rather than like a naming mismatch.
            bool   enriched = cubes.ContainsKey("Efficiency");
            double effScale = enriched ? 1.0 : 100.0;
            // The unit of the RAW value, which is the whole of what the two spellings differ by:
            // Enrich has already multiplied by 100 and publishes a percentage, while the engine's
            // own DE and PAE are fractions (R-aut9-3).
            string effUnit  = enriched ? "%" : "1";

            var columns = new[]
            {
                Column(cubes, "Pavl",       1.0,      "dBm",   "dBm", "PavlDbm"),
                Column(cubes, "Pout",       1.0,      "dBm",   "dBm", "Pout_dBm",   "Pout"),
                Column(cubes, "Gt",         1.0,      "dB",    "dB",  "Gt_dB",      "Gt"),
                Column(cubes, "Efficiency", effScale, effUnit, "%",   "Efficiency", "DE"),
                Column(cubes, "PAE",        effScale, effUnit, "%",   "PAE"),
            };
            var sources = columns.Select(c => c.SourceCube is null ? null : Real(cubes, c.SourceCube)).ToArray();

            int compressed = codes.Count(c => c == 1), notConv = codes.Count(c => c is 2 or 3);
            int maxDrive   = codes.Length - compressed - notConv;

            var rows = new List<LoadpullGridRow>(checked((int)(outer * nGrid)));
            for (long o = 0; o < outer; o++)
                for (int g = 0; g < nGrid; g++)
                {
                    long flat    = o * nGrid + g;   // index into the [outer x grid] cubes
                    long fomBase = flat * nPin;     // index into the [outer x grid x pin] cubes

                    // The last converged, non-tickle drive step for this point.
                    int at = -1;
                    for (int k = nPin - 1; k >= 0; k--)
                    {
                        long idx = fomBase + k;
                        if (conv.RealValues[idx] == 0.0) continue;
                        if (tickle is not null && tickle.RealValues[idx] != 0.0) continue;
                        at = k;
                        break;
                    }

                    var fom = new double[sources.Length];
                    for (int c = 0; c < sources.Length; c++) fom[c] = Value(sources[c], fomBase, at);

                    rows.Add(new LoadpullGridRow(
                        o, g, codes[flat],
                        haveGamma && flat < gammaCube!.ComplexValues.Length ? gammaCube.ComplexValues[flat] : null,
                        haveZ     && flat < zCube!.ComplexValues.Length     ? zCube.ComplexValues[flat]     : null,
                        at, fom));
                }

            return new LoadpullGridSummary(
                nGrid, outer, outerAxes, nPin, compressed, maxDrive, notConv, columns, rows);
        }

        /// <summary>
        /// The pursuit's answer: the maximum-power and maximum-efficiency terminations it converged
        /// on, plus how much of the space it queried. Null when the group holds no pursuit.
        /// </summary>
        public static LoadpullPursuitSummary? SummarizePursuit(IReadOnlyDictionary<string, DataCube> cubes)
        {
            if (!HasPursuit(cubes)) return null;

            return new LoadpullPursuitSummary(
                // MXP_PoutDbm is already in dBm, so the console scales by 1 and the two units agree.
                // MXE_Eff is a FRACTION the console prints as a percentage, and saying so is
                // R-aut9-3's whole point.
                Optimum(cubes, "MXP", "MXP_PoutDbm", "dBm", 1.0,   "dBm"),
                Optimum(cubes, "MXE", "MXE_Eff",     "1",   100.0, "%"),
                Scalar(cubes, "CacheCount"),
                Scalar(cubes, "UnscorableCount"),
                Scalar(cubes, "RecommTermCount"));
        }

        // ── the selections, one place each ───────────────────────────────────

        private static PursuitOptimum Optimum(
            IReadOnlyDictionary<string, DataCube> cubes,
            string tag, string valueCube, string valueUnit, double valueScale, string consoleUnit)
            => new(
                tag,
                Scalar(cubes, $"{tag}_Converged") != 0.0,
                valueCube,
                Scalar(cubes, valueCube),
                valueUnit,
                valueScale,
                consoleUnit,
                Scalar(cubes, $"{tag}_ZRe"),
                Scalar(cubes, $"{tag}_ZIm"),
                Scalar(cubes, $"{tag}_HasZsource") != 0.0,
                Scalar(cubes, $"{tag}_ZsourceRe"),
                Scalar(cubes, $"{tag}_ZsourceIm"));

        /// <summary>The first element of a real scalar cube, NaN when there is no such cube.</summary>
        private static double Scalar(IReadOnlyDictionary<string, DataCube> cubes, string name)
            => cubes.TryGetValue(name, out var c) && c.DataKind == DataKind.Real && c.RealValues.Length > 0
                ? c.RealValues[0]
                : double.NaN;

        /// <summary>A real cube, or null — a complex cube under one of these names is not one.</summary>
        private static DataCube? Real(IReadOnlyDictionary<string, DataCube> cubes, string name)
            => cubes.TryGetValue(name, out var c) && c.DataKind == DataKind.Real ? c : null;

        /// <summary>The first spelling this run actually published, in preference order.</summary>
        private static LoadpullFomColumn Column(
            IReadOnlyDictionary<string, DataCube> cubes, string column, double scale,
            string unit, string consoleUnit, params string[] spellings)
        {
            foreach (string s in spellings)
                if (Real(cubes, s) is { } c)
                    // A cube that states its own unit outranks the caller's expectation of it — the
                    // one that does is the enriched PAE, whose NAME is unchanged by the scaling that
                    // turned it into a percentage.
                    return new LoadpullFomColumn(
                        column, s, scale, string.IsNullOrEmpty(c.Unit) ? unit : c.Unit, consoleUnit);
            return new LoadpullFomColumn(column, null, scale, unit, consoleUnit);
        }

        /// <summary>
        /// The raw value at a point's chosen drive step. NaN for an absent cube, an absent drive step
        /// and a short buffer alike — all three are the em dash the console prints, and none of them
        /// is a number.
        /// </summary>
        private static double Value(DataCube? cube, long fomBase, int at)
        {
            if (cube is null || at < 0) return double.NaN;
            long idx = fomBase + at;
            return idx >= cube.RealValues.Length ? double.NaN : cube.RealValues[idx];
        }
    }
}
