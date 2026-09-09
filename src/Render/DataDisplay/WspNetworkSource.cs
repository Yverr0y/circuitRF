// ================================================================
//  WspNetworkSource.cs  —  the probe-pair blocks and the probe-set
//  admittance matrix, as ordinary NETWORK cubes
//
//  WSP-4 R-wsp4-6 (second half) on R-wsp4-8's terms. The pair picker
//  offers wsp_block_calc's {9..16} as scalar traces; its {1..4} and
//  {5..8} are not scalars — they are the S-parameters of a TWO-PORT,
//  and so are the in-situ renormalised forms `wsp_block_design` and
//  `wsp_fb_design`. A probe SET's `wsp_ymatrix` is likewise an N-port.
//
//  So they arrive the way the reduced two-port does: as a virtual
//  network GROUP. That is the whole capability — once a block is an
//  ordinary network group, mu, mu', K, |Delta|, MAG/MSG, both stability
//  circles, the element picker and every export apply to it with no
//  code at all, and the document's "break the block out and terminate
//  it with your target loadlines" (p. 88) is a thing you can look at.
// ================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using RfCore.Data;
using RfCore.Stability;

namespace CircuitRF.Render.DataDisplay;

/// <summary>Which two-port of a probe PAIR a virtual network group holds (Fig. 40).</summary>
public enum WspBlockKind
{
    /// <summary><c>wsp_block_breakout</c> — the inner block's S at Z0, <c>{1..4}</c> (Eq. 142).</summary>
    Inner,
    /// <summary><c>wsp_fb_breakout</c> — the feedback block's S at Z0, <c>{5..8}</c> (Eq. 143).</summary>
    Feedback,
    /// <summary><c>wsp_block_design</c> — the inner block renormalised in situ to probe 1's ZG and
    /// probe 2's ZL as parallel RC pairs (E.8).</summary>
    InnerDesign,
    /// <summary><c>wsp_fb_design</c> — the feedback block renormalised to probe 1's ZL and probe 2's
    /// ZG (E.9; the roles flip because the block's ports face the other way).</summary>
    FeedbackDesign,
}

public static partial class WspSource
{
    /// <summary>Every block a probe pair offers, in the card's own order.</summary>
    public static readonly IReadOnlyList<WspBlockKind> BlockKinds =
        [WspBlockKind.Inner, WspBlockKind.Feedback, WspBlockKind.InnerDesign, WspBlockKind.FeedbackDesign];

    /// <summary>The name the source tree shows a block under.</summary>
    public static string BlockKindName(WspBlockKind kind) => kind switch
    {
        WspBlockKind.Inner          => "inner block",
        WspBlockKind.Feedback       => "feedback block",
        WspBlockKind.InnerDesign    => "inner block (design)",
        _                           => "feedback block (design)",
    };

    /// <summary>The measure-line call the block comes from — what the group's own description says,
    /// so a reader can carry it into a <c>measure</c> line unchanged.</summary>
    public static string BlockFunction(WspBlockKind kind) => kind switch
    {
        WspBlockKind.Inner          => "wsp_block_breakout",
        WspBlockKind.Feedback       => "wsp_fb_breakout",
        WspBlockKind.InnerDesign    => "wsp_block_design",
        _                           => "wsp_fb_design",
    };

    /// <summary>
    /// One of the four two-ports a probe pair brackets, as an S cube over
    /// <c>{…leading…, i, j}</c> — the shape every network-parameter consumer already reads.
    ///
    /// <para><b>The two DESIGN forms need a frequency, and it is the cube's own.</b>
    /// <see cref="WspPair.BlockDesign"/> turns each side's bidirectional impedance into a parallel
    /// RC pair, and a capacitance is only a capacitance at a stated frequency
    /// (<c>ImmittanceModels.ZToPc</c>). Feeding it one frequency for the whole sweep — the shape a
    /// per-block loop invites — would renormalise every point to the RC that fits the first one, and
    /// the answer would look entirely ordinary.</para>
    /// </summary>
    public static bool TryPairBlock(
        DataSet? ds, string? cubeSpec, string label1, string label2, WspBlockKind kind, Complex z0,
        out DataCube? result, out string error)
    {
        result = null; error = "";
        if (!TryMatrix(ds, cubeSpec, out var cube, out int size, out var leading, out var raw, out int blocks, out error))
            return false;

        var probes = Probes(ds, GroupOf(cubeSpec!));
        if (!TryIdx(probes, size / 2, label1, out int idx1, out error)) return false;
        if (!TryIdx(probes, size / 2, label2, out int idx2, out error)) return false;
        if (idx1 == idx2)
        { error = $"A block is bracketed by TWO probes; '{label1}' is named twice."; return false; }

        bool design = kind is WspBlockKind.InnerDesign or WspBlockKind.FeedbackDesign;
        double[]? freqPerBlock = design ? FreqPerBlock(leading!, blocks) : null;
        if (design && freqPerBlock is null)
        {
            error = $"{BlockFunction(kind)} renormalises to a parallel RC pair, which is only defined "
                  + "at a stated frequency — and this cube has no freq axis "
                  + $"({string.Join(", ", cube!.Axes.Select(a => a.Name))}).";
            return false;
        }

        var vals = new Complex[blocks * 4];
        try
        {
            for (int b = 0; b < blocks; b++)
            {
                var w = BlockAt(raw!, b, size);
                var s = kind switch
                {
                    WspBlockKind.Inner       => WspPair.BlockBreakout(w, idx1, idx2, z0),
                    WspBlockKind.Feedback    => WspPair.FbBreakout(w, idx1, idx2, z0),
                    WspBlockKind.InnerDesign => WspPair.BlockDesign(w, idx1, idx2, freqPerBlock![b], z0),
                    _                        => WspPair.FbDesign(w, idx1, idx2, freqPerBlock![b], z0),
                };
                vals[b * 4 + 0] = s[0, 0]; vals[b * 4 + 1] = s[0, 1];
                vals[b * 4 + 2] = s[1, 0]; vals[b * 4 + 3] = s[1, 1];
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { error = ex.Message.Split(" (Parameter")[0]; return false; }

        result = new DataCube([.. leading!, new Axis("i", [1.0, 2.0]), new Axis("j", [1.0, 2.0])], vals);
        return true;
    }

    /// <summary>
    /// <c>wsp_ymatrix(wsp, probes)</c> — the reduced admittance matrix of the whole network at the
    /// probe nodes (Eq. 185), as a Y cube over <c>{…leading…, i, j}</c> with the port axes VALUED by
    /// probe idx and LABELLED by probe label, so <c>Y[:, 2, 1]</c> reads by the document's own index
    /// exactly as it does in a measure line.
    ///
    /// <para>Order is part of the answer here as it is for Ohtomo: the matrix is indexed in the
    /// set's own order, so re-ordering the set permutes every row and column.</para>
    /// </summary>
    public static bool TrySetYMatrix(
        DataSet? ds, string? cubeSpec, IReadOnlyList<string> labels,
        out DataCube? result, out string error)
    {
        result = null; error = "";
        if (!TryMatrix(ds, cubeSpec, out _, out int size, out var leading, out var raw, out int blocks, out error))
            return false;

        var probes = Probes(ds, GroupOf(cubeSpec!));
        var set = new List<int>();
        var names = new List<string>();
        foreach (string label in labels)
        {
            if (!TryIdx(probes, size / 2, label, out int m, out error)) return false;
            if (set.Contains(m)) continue;
            set.Add(m);
            names.Add(label);
        }
        if (set.Count == 0)
        { error = "wsp_ymatrix is taken over a probe SET: choose at least one probe."; return false; }

        int n = set.Count;
        var vals = new Complex[blocks * n * n];
        int singular = 0;
        try
        {
            for (int b = 0; b < blocks; b++)
            {
                Complex[,]? y = null;
                // A SINGULAR reduced Z at one frequency is an ordinary condition, not a failure of
                // the run: a probe whose G node is held by an ideal voltage source — the gate bias
                // node of the shipped FET test bench, say — has H0 = 0 exactly, which is the
                // degenerate node the library already reports as NaN rather than fudging (overview
                // D-7). It must arrive here as a GAP in the trace, and it must never leave this
                // method as an exception: this group is materialized EAGERLY for every run that
                // carries a probe, so an escape takes the whole Data Display with it.
                try { y = WspGlobal.Ymatrix(BlockAt(raw!, b, size), [.. set]); }
                catch (InvalidOperationException) { singular++; }

                for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    vals[(b * n + r) * n + c] = y is null
                        ? new Complex(double.NaN, double.NaN)
                        : y[r, c];
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { error = ex.Message.Split(" (Parameter")[0]; return false; }

        // Every point singular is not a gap, it is a quantity this probe set does not have — one
        // probe on a hard-driven node is the whole matrix. Reported as a refusal rather than
        // offered as an item that can only ever draw nothing.
        if (singular == blocks)
        {
            error = "wsp_ymatrix over " + string.Join(", ", names)
                  + " is singular at every point: the network's reduced impedance matrix at these "
                  + "probe nodes has no inverse. A probe whose G node is held by an ideal voltage "
                  + "source has H0 = 0 exactly, which is that case.";
            return false;
        }

        var ports = set.Select(i => (double)i).ToArray();
        result = new DataCube(
            [.. leading!,
             new Axis("i", ports, "", [.. names]),
             new Axis("j", (double[])ports.Clone(), "", [.. names])],
            vals) { Unit = "S" };
        return true;
    }

    /// <summary>The shared "is this actually a wsp matrix" gate, so one sentence answers it
    /// everywhere.</summary>
    private static bool TryMatrix(
        DataSet? ds, string? cubeSpec,
        out DataCube? cube, out int size, out Axis[]? leading, out Complex[]? raw, out int blocks,
        out string error)
    {
        cube = null; size = 0; leading = null; raw = null; blocks = 0; error = "";
        if (ds is null || cubeSpec is null || !ds.Contains(cubeSpec))
        { error = "This source carries no wsp matrix — place a WSProbe and re-run."; return false; }

        cube = ds[cubeSpec];
        if (cube.Rank < 3 || cube.Axes[^1].Length != cube.Axes[^2].Length)
        { error = $"'{cubeSpec}' is not a wsp matrix ({{…, freq, row, col}}, square)."; return false; }

        size    = cube.Axes[^1].Length;
        leading = [.. cube.Axes.Take(cube.Rank - 2)];
        raw     = cube.ComplexValues;
        blocks  = raw.Length / (size * size);
        return true;
    }

    /// <summary>The frequency of each leading-axis point, broadcast along the freq axis's own
    /// position. Null when there is no freq axis at all.</summary>
    private static double[]? FreqPerBlock(Axis[] leading, int blocks)
    {
        int fa = -1;
        for (int a = 0; a < leading.Length; a++)
            if (leading[a].Name is "freq" or "ssfreq") { fa = a; break; }
        if (fa < 0) return null;

        int inner = 1;
        for (int a = fa + 1; a < leading.Length; a++) inner *= leading[a].Length;
        var freqs = leading[fa].Values;
        var outp = new double[blocks];
        for (int b = 0; b < blocks; b++) outp[b] = freqs[(b / inner) % freqs.Length];
        return outp;
    }
}
