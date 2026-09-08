using System.Numerics;
using CircuitRF.Core.Devices;
using CircuitRF.Core.Elaboration;
using RfCore.Data;
using RfCore.Stability;

namespace CircuitRF.Engine;

/// <summary>
/// The WSProbe result cubes, written in ONE place for every analysis that produces a <c>wsp</c>
/// matrix — the S-parameter engine over a <c>freq</c> axis (brief-wsprobe-1 R-wsp1-10) and the
/// harmonic-balance engine over an <c>ssfreq</c> axis (brief-wsprobe-5 R-wsp5-6).
///
/// <para><b>Why it is shared rather than transcribed.</b> Overview D-2 says the eight per-probe
/// defaults are computed "through the same library functions the Data Display calls", so a run's
/// <c>ZG:GATE</c> cube and a trace card's <c>wsp_ZG</c> are bit-identical by construction. The
/// second half of that promise is that the two ANALYSES agree with each other: a cube name, a unit,
/// a NaN policy or a diagnostic wording that existed twice would drift, and the drift would show up
/// as an HB result the Data Display draws differently from the S-parameter result of the same
/// design. The only difference between the two callers is the axis and the units on it.</para>
/// </summary>
internal static class WspCubePacker
{
    /// <summary>
    /// One probe as the packer needs it: the document's label and <c>idx</c>, plus the declared
    /// <c>Z</c> of the termination sitting directly at each terminal (null where there is none) —
    /// what <c>wsp_terminate</c>'s precondition compares <c>ZG</c>/<c>ZL</c> against
    /// (brief-wsprobe-3 §6.1).
    /// </summary>
    internal readonly record struct Probe(string Label, int Idx, Complex? TermZG, Complex? TermZL);

    /// <summary>
    /// The <c>wsp</c> matrix and everything derived from it, as cubes on <paramref name="axis"/>:
    /// <c>wsp</c> over <c>{axis, row, col}</c> with 1-based integer <c>row</c>/<c>col</c> values so
    /// the document's <c>wsp(r, c)</c> is <c>wsp[i, r, c]</c> with no index arithmetic between;
    /// <c>H0:&lt;label&gt;</c> … <c>F:&lt;label&gt;</c> through <see cref="WspReduction"/> and
    /// <c>SM_Y0</c>/<c>SM_H0</c> through <see cref="WspMargin"/>; and the three <c>__</c> metadata
    /// cubes a sweep passes through unstacked.
    /// </summary>
    /// <param name="axisValues">The axis values in Hz, for the diagnostics' own wording.</param>
    /// <param name="axisWhat">How a diagnostic names a point of this axis — "GHz" for both callers,
    /// but the SENTENCE differs: an S-parameter run says "at 2 GHz", a small-signal sweep says "at a
    /// probe frequency of 2 GHz".</param>
    internal static void Add(
        DataSet ds, ElaboratedNetlist netlist, Axis axis, double[] axisValues,
        IReadOnlyList<Probe> probes, Complex[][,] wsp, double? marginThresholdDb,
        string axisWhat)
    {
        int nf = axisValues.Length, m = probes.Count, size = 2 * m;

        var rc = new double[size];
        for (int k = 0; k < size; k++) rc[k] = k + 1;
        var rowAxis = new Axis("row", rc);
        var colAxis = new Axis("col", (double[])rc.Clone());

        var data = new Complex[nf * size * size];
        for (int fi = 0; fi < nf; fi++)
        {
            var w = wsp[fi];
            for (int r = 0; r < size; r++)
            for (int c = 0; c < size; c++)
                data[(fi * size + r) * size + c] = w[r, c];
        }
        ds.Add("wsp", new DataCube([axis, rowAxis, colAxis], data));

        for (int pi = 0; pi < m; pi++)
        {
            var probe = probes[pi];
            var h0 = new Complex[nf]; var y0 = new Complex[nf];
            var zg = new Complex[nf]; var zl = new Complex[nf];
            var lg = new Complex[nf]; var f  = new Complex[nf];
            // R-wsp9-1: the two margins beside the six defaults, through WspMargin.Of on the SAME
            // quad — one implementation, so SM_Y0:<label> and wsp_SM_Y0(SP1.wsp, idx) are
            // bit-identical (overview D-2).
            var smY = new double[nf]; var smH = new double[nf];
            int firstDegenerate = -1;
            for (int fi = 0; fi < nf; fi++)
            {
                var q = WspProbeQuad.Of(wsp[fi], probe.Idx);
                var d = WspReduction.Defaults(q);
                h0[fi] = d.H0; y0[fi] = d.Y0; zg[fi] = d.ZG; zl[fi] = d.ZL; lg[fi] = d.LG; f[fi] = d.F;
                var mg = WspMargin.Of(q);
                smY[fi] = mg.SmY0; smH[fi] = mg.SmH0;
                if (d.Degenerate && firstDegenerate < 0) firstDegenerate = fi;
            }
            if (firstDegenerate >= 0)
                netlist.AddWarningOnce($"wsprobe.degenerate-node:{probe.Label}",
                    $"WSProbe '{probe.Label}': the reduced two-port is undefined at " +
                    $"{axisWhat}{axisValues[firstDegenerate] / 1e9:G6} GHz (H0 = 0 is an exact short " +
                    $"from the G node to ground; Y0 = 0 is an exact open in the probe branch). The " +
                    $"affected outputs are NaN there; no epsilon was added to a denominator.");

            ds.Add($"H0:{probe.Label}", new DataCube([axis], h0) { Unit = "Ohm" });
            ds.Add($"Y0:{probe.Label}", new DataCube([axis], y0) { Unit = "S" });
            ds.Add($"ZG:{probe.Label}", new DataCube([axis], zg) { Unit = "Ohm" });
            ds.Add($"ZL:{probe.Label}", new DataCube([axis], zl) { Unit = "Ohm" });
            ds.Add($"LG:{probe.Label}", new DataCube([axis], lg));
            ds.Add($"F:{probe.Label}",  new DataCube([axis], f));
            ds.Add($"SM_Y0:{probe.Label}", new DataCube([axis], smY));
            ds.Add($"SM_H0:{probe.Label}", new DataCube([axis], smH));

            ReportMarginThreshold(netlist, axisValues, probe.Label, smY, smH, marginThresholdDb, axisWhat);
        }

        var pIdx   = new double[m];
        var labels = new string[m];
        for (int pi = 0; pi < m; pi++) { pIdx[pi] = pi; labels[pi] = probes[pi].Label; }
        var idxVals = new double[m];
        for (int pi = 0; pi < m; pi++) idxVals[pi] = probes[pi].Idx;
        ds.Add("__WspProbes", new DataCube([new Axis("probe", pIdx, "", labels)], idxVals));

        // __WspMarginThreshold: the run's own MarginThreshold, in dB, so the Data Display can draw
        // the line the run actually judged against rather than the published default (WSP-4
        // R-wsp4-7). NaN when the knob is `none`. Metadata like __WspProbes — a sweep passes it
        // through unstacked, and the trace picker skips every `__` cube.
        ds.Add("__WspMarginThreshold",
            new DataCube([new Axis("one", [0.0])], new[] { marginThresholdDb ?? double.NaN }));

        // __WspTermZ {probe, side}: the declared Z of the termination directly at each probe
        // terminal, NaN where there is none. Metadata like __WspProbes.
        var nan   = new Complex(double.NaN, double.NaN);
        var termZ = new Complex[2 * m];
        for (int pi = 0; pi < m; pi++)
        {
            termZ[2 * pi]     = probes[pi].TermZG ?? nan;
            termZ[2 * pi + 1] = probes[pi].TermZL ?? nan;
        }
        ds.Add("__WspTermZ", new DataCube(
            [new Axis("probe", (double[])pIdx.Clone(), "", (string[])labels.Clone()),
             new Axis("side", [0.0, 1.0], "", ["G", "L"])],
            termZ) { Unit = "Ohm" });
    }

    /// <summary>
    /// The declared <c>Z</c> of a top-level termination shunting <paramref name="node"/> to ground —
    /// a <c>Port</c>, a <c>Term</c> or a <c>P1Tone</c>, the same three the S-parameter engine counts
    /// as ports, and buried instances skipped for the same Layer-2 scoping reason. Null when there
    /// is none.
    ///
    /// <para><b>A Tuner is deliberately not one of them.</b> Its impedance is set per harmonic BAND
    /// against the run's own tone ruler, so it has no single declared <c>Z</c> that <c>ZG</c> could
    /// be required to equal at every point of a small-signal sweep — which is exactly the
    /// precondition <c>wsp_terminate</c> checks. Reporting a Tuner's tone-band value here would let
    /// the envelope re-terminate a shunt whose real value is a different number at every
    /// <c>ssfreq</c>.</para>
    /// </summary>
    internal static Complex? TermZAt(ElaboratedNetlist netlist, int node)
    {
        if (node == 0) return null;
        foreach (var ec in netlist.Components)
        {
            if (ec.Model is not (PortModel or TermModel or P1ToneModel)) continue;
            if (ec.InstancePath.Contains('.')) continue;          // buried: inert (Layer 2 scoping)
            if (ec.Nodes.Length < 2) continue;
            bool at = (ec.Nodes[0] == node && ec.Nodes[1] == 0)
                   || (ec.Nodes[1] == node && ec.Nodes[0] == 0);
            if (!at) continue;
            if (!ec.Parameters.TryGetValue("Z", out var v)) return new Complex(50, 0);
            return v.Kind == CircuitRF.Core.Expressions.ValueKind.Complex
                ? v.AsComplex()
                : new Complex(v.AsReal(), 0);
        }
        return null;
    }

    /// <summary>
    /// R-wsp9-3 — one <b>Info</b> note per probe whose <c>min(SM_Y0, SM_H0)</c> falls below
    /// <paramref name="thresholdDb"/> over the sweep, keyed
    /// <c>wsprobe.margin-below-threshold:&lt;label&gt;</c>. Silent when the threshold is null
    /// (<c>MarginThreshold=none</c>) or nothing crossed it.
    ///
    /// <para><b>A note, not a warning.</b> The −5 Ω split resonator of brief-wsprobe-9 §3 is stable
    /// and fires this by design: a node one negative-resistance step from oscillating genuinely has
    /// little margin. It says "look here". A NaN margin (a degenerate node, overview D-7) never
    /// fires it — that probe already has its own diagnostic.</para>
    /// </summary>
    private static void ReportMarginThreshold(
        ElaboratedNetlist netlist, double[] axisValues, string label,
        double[] smY, double[] smH, double? thresholdDb, string axisWhat)
    {
        if (thresholdDb is not { } dbLimit) return;

        double limit = Math.Pow(10.0, dbLimit / 20.0);     // 20·log10 (overview D-16)
        int    yi = ArgMin(smY), hi = ArgMin(smH);
        if (yi < 0 && hi < 0) return;

        double ym = yi < 0 ? double.PositiveInfinity : smY[yi];
        double hm = hi < 0 ? double.PositiveInfinity : smH[hi];
        if (Math.Min(ym, hm) >= limit) return;

        netlist.AddNoteOnce($"wsprobe.margin-below-threshold:{label}",
            $"WSProbe '{label}': stability margin below {Signed(dbLimit)} dB ({limit:G3}): " +
            $"SM_Y0 = {Db(ym)} at {Ghz(axisValues, yi)}, SM_H0 = {Db(hm)} at {Ghz(axisValues, hi)}. " +
            "A margin below −12 dB means one side of the node presents negative resistance there. " +
            "Winslow (EuMIC 2024) §IV: find the root cause of any sudden decrease.");

        static string Db(double v)
            => double.IsPositiveInfinity(v) ? "n/a"
             : v <= 0.0                     ? "−inf dB"
             : $"{Minus($"{20.0 * Math.Log10(v):F1}")} dB";

        // A typographic minus, so the threshold and the margins read as the paper prints them.
        static string Signed(double v) => Minus($"{v:G4}");

        static string Minus(string s) => s.StartsWith('-') ? "−" + s[1..] : s;

        string Ghz(double[] f, int i) => i < 0 ? "n/a" : $"{axisWhat}{f[i] / 1e9:G6} GHz";
    }

    /// <summary>The index of the smallest non-NaN entry, or −1 when every entry is NaN.</summary>
    private static int ArgMin(double[] v)
    {
        int best = -1;
        for (int k = 0; k < v.Length; k++)
        {
            if (double.IsNaN(v[k])) continue;
            if (best < 0 || v[k] < v[best]) best = k;
        }
        return best;
    }
}
