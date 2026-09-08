// ================================================================
//  WspReadouts.cs  —  what a WSProbe trace SAYS beside itself
//
//  WSP-4 R-wsp4-7. Three readings the reference document takes off a
//  polar plot by eye, made countable and put on the card:
//
//   • Kurokawa's start-up signature on a driving-point locus — the
//     frequencies at which 1/H0 or 1/Y0 crosses the negative real axis
//     CLOCKWISE (Eq. 107/108). "none" is a real answer and is printed.
//   • The encirclement count of any polar locus (App. E.3) — the
//     reading NDF needs and the one F invites (p. 34).
//   • A margin trace's minimum and where it is, together with the
//     Kurokawa frequencies of the MATCHING driving-point function:
//     SM_Y0 pairs with 1/Y0, SM_H0 with 1/H0 (WSP-9 §2.1f). The margin
//     is the distance and the search is the detector, and reading one
//     without the other is how a −40 dB margin at a frequency nothing
//     crosses gets mistaken for an oscillation.
//
//  Every number is a library call. The COMPANION curve is obtained by
//  resolving a copy of the trace with its metric swapped — so the
//  companion is sliced, swept and family-reduced exactly as the trace
//  the reader is looking at, rather than by a second slice
//  implementation that would agree on an unswept run and drift on a
//  swept one.
// ================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using RfCore;
using RfCore.Data;
using RfCore.Stability;

namespace CircuitRF.Render.DataDisplay;

/// <summary>One line of text for the card, and the frequencies "Mark crossings" would place a
/// marker at (empty when the reading has none).</summary>
/// <param name="Text">The sentence the card shows.</param>
/// <param name="Crossings">Hz, ascending.</param>
public readonly record struct WspReadout(string Text, IReadOnlyList<double> Crossings);

public static class WspReadouts
{
    /// <summary>
    /// The readout for <paramref name="t"/> against <paramref name="ds"/>, or null when the trace is
    /// not a WSProbe trace or has not resolved.
    /// </summary>
    public static WspReadout? For(Trace t, DataSet? ds, PlotType plotType)
    {
        if (!t.IsWspTrace || ds is null) return null;
        var spec = t.Wsp!;
        if (WspMetrics.Info(spec.Metric) is not { } info) return null;

        var x = t.CubeXValues;
        if (x is null || x.Count < 2) return null;

        // ── a margin trace: its minimum, and the detector that goes with it ──
        if (info.Group == WspMetricGroup.Margin)
        {
            var v = t.CubeReal;
            if (v is null || v.Count == 0) return null;
            int at = ArgMin(v);
            if (at < 0) return null;

            string head = $"minimum {Db(v[at])} at {Ghz(x[at])}";
            var companion = WspMetrics.MarginCompanion(spec.Metric);
            if (companion == WspMetric.None) return new WspReadout(head, []);

            var (cx, cv) = Companion(t, ds, companion);
            if (cv is null || cx is null) return new WspReadout(head, []);
            var hits = WspKurokawa.UnstableFrequencies(cv, cx);
            string inv = companion == WspMetric.Y0 ? "1/Y0" : "1/H0";
            return new WspReadout(
                $"{head} · Kurokawa on {inv}: {FreqList(hits)}", hits);
        }

        // ── a driving-point locus: Kurokawa's own signature ──────────────────
        if (spec.Metric is WspMetric.H0 or WspMetric.Y0 or WspMetric.InvH0 or WspMetric.InvY0)
        {
            // UnstableFrequencies takes T (H0 or Y0) and inverts it itself, so a 1/H0 trace hands
            // over H0 rather than what it draws — the inverse of the inverse is not free of NaN at a
            // degenerate node, and the library's own guard is the one to keep.
            var basis = spec.Metric is WspMetric.H0 or WspMetric.InvH0 ? WspMetric.H0 : WspMetric.Y0;
            IReadOnlyList<Complex>? cv;
            IReadOnlyList<double>?  cx;
            if (spec.Metric == basis) { cv = t.CubeComplex; cx = x; }
            else                      (cx, cv) = Companion(t, ds, basis);
            if (cv is null || cx is null) return null;

            var hits = WspKurokawa.UnstableFrequencies(cv, cx);
            string text = $"Kurokawa start-up: {FreqList(hits)}";
            if (plotType == PlotType.Polar && t.CubeComplex is { Count: > 0 } locus)
                text += $" · {Encirclements(locus)}";
            return new WspReadout(text, hits);
        }

        // ── a loop gain: the same search about the critical point +1 ─────────
        if (info.Group is WspMetricGroup.LoopGain or WspMetricGroup.Pair or WspMetricGroup.ProbeSet)
        {
            var g = t.CubeComplex;
            if (g is null || g.Count < 2) return null;
            var hits = WspOhtomo.UnstableFrequenciesLoopGain(g, x);
            string text = $"Loop-gain crossings (critical point +1): {FreqList(hits)}";
            if (plotType == PlotType.Polar) text += $" · {Encirclements(g)}";
            return new WspReadout(text, hits);
        }

        // ── anything else on a polar plot: the encirclement count alone ──────
        if (plotType == PlotType.Polar && t.CubeComplex is { Count: > 0 } any)
            return new WspReadout(Encirclements(any), []);

        return null;
    }

    /// <summary>
    /// The same trace with one metric swapped, resolved through the ordinary path so its slice,
    /// sweep pin and family reduction are the trace's own.
    /// </summary>
    private static (IReadOnlyList<double>? X, IReadOnlyList<Complex>? V) Companion(
        Trace t, DataSet ds, WspMetric metric)
    {
        try
        {
            var tmp = new Trace(t, includeMarkers: false);
            tmp.Wsp = t.Wsp!.Clone();
            tmp.Wsp.Metric = metric;
            tmp.Transform  = CubeTransform.None;
            TraceResolve.SetCubeDataFrom(tmp, ds, PlotType.Polar, FreqUnit.GHz);
            return (tmp.CubeXValues, tmp.CubeComplex);
        }
        catch
        {
            // A readout is a convenience beside a curve that already drew. It never takes the
            // session with it — the same rule SetCubeDataFrom's own catch states.
            return (null, null);
        }
    }

    private static string Encirclements(IReadOnlyList<Complex> locus)
    {
        var enc = WspKurokawa.Encirclements(locus);
        if (enc.Length == 0) return "encirclements: —";
        double last = enc[^1];
        return $"encirclements: {Math.Round(last):0} (net {last.ToString("0.00", CultureInfo.InvariantCulture)})";
    }

    private static int ArgMin(IReadOnlyList<double> v)
    {
        int at = -1;
        double best = double.PositiveInfinity;
        for (int i = 0; i < v.Count; i++)
            if (double.IsFinite(v[i]) && v[i] < best) { best = v[i]; at = i; }
        return at;
    }

    /// <summary>The margin in dB, at the 20·log10 convention of overview D-16, with a typographic
    /// minus so the card reads as the paper prints it.</summary>
    private static string Db(double v)
        => v <= 0.0 ? "−inf dB"
         : Minus((20.0 * Math.Log10(v)).ToString("F1", CultureInfo.InvariantCulture)) + " dB";

    /// <summary>
    /// A frequency as the two papers print one: GHz to four decimals, trailing zeros dropped
    /// (1.59125 GHz reads 1.5913, which is [M]'s own spelling of it). Below 100 kHz the fixed form
    /// would round to zero, so a significant-digit form takes over rather than printing "0 GHz".
    /// </summary>
    private static string Ghz(double hz)
    {
        double g = hz / 1e9;
        return (Math.Abs(g) >= 1e-4
            ? g.ToString("0.####", CultureInfo.InvariantCulture)
            : g.ToString("G4", CultureInfo.InvariantCulture)) + " GHz";
    }

    private static string FreqList(IReadOnlyList<double> hits)
        => hits.Count == 0 ? "none" : string.Join(", ", hits.Select(Ghz));

    private static string Minus(string s) => s.StartsWith('-') ? "−" + s[1..] : s;
}
