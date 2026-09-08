using System.Numerics;
using RfCore.Data;
using RfCore.Stability;

namespace CircuitRF.Core.Expressions;

/// <summary>
/// The multi-probe derived metrics of the WSProbe reference document — probe pairs (§5), network
/// bifurcation (§6), Ohtomo's global loop gains (§7), the reduced admittance matrix and the
/// probe-based NDF (§8), and the stability envelope (§9) — registered as built-ins with the
/// document's own names (brief-wsprobe-3; overview D-5, D-6, D-8).
///
/// <para><b>Nothing here computes anything.</b> Every value comes from
/// <c>src/RfCore/Stability/</c> — <see cref="WspPair"/>, <see cref="WspBifurcation"/>,
/// <see cref="WspOhtomo"/>, <see cref="WspGlobal"/>, <see cref="WspEnvelope"/>. This file reads a
/// <c>{…, freq, row, col}</c> cube one <c>2N×2N</c> block at a time, calls the scalar core, and
/// reassembles the answer with the leading axes untouched, exactly as <c>Evaluator.Wsp.cs</c> does
/// for the single-probe functions.</para>
///
/// <para><b>Two argument spellings the single-probe file did not need.</b> A <i>probe list</i> is
/// an integer (one probe), or a quoted string of comma-separated idx numbers or probe labels —
/// <c>"1,3"</c>, <c>"GATE,DRAIN"</c> — and defaults to every probe in idx order; labels resolve
/// through the <c>__WspProbes</c> metadata of the analysis that produced the cube. A <i>Γ grid</i>
/// is a single reflection coefficient, a cube of them, or the common case as a quoted string
/// <c>"|Γ|:count"</c> — <c>"0.8:24"</c> is 24 points on the <c>|Γ| = 0.8</c> circle from
/// <c>θ = 0°</c> in equal steps.</para>
///
/// <para>Reference throughout: T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i>
/// (2023).</para>
/// </summary>
public sealed partial class Evaluator
{
    internal static bool IsWspGlobalBuiltin(string name) => name switch
    {
        "wsp_yparam2" or "wsp_yparam2_residual" or "wsp_block_calc" or
        "wsp_block_breakout" or "wsp_fb_breakout" or "wsp_block_design" or "wsp_fb_design" or
        "wsp_bifurcate" or "wsp_YA" or "wsp_YF" or "wsp_ZA" or "wsp_ZF" or
        "wsp_loopgain_ohtomo" or "wsp_unstable_freq_loopgain" or
        "wsp_ymatrix" or "wsp_ndf" or
        "wsp_terminate" or "wsp_loadpull" or "wsp_loadpull_unstable" => true,
        _ => false,
    };

    private Value EvalWspGlobalCall(CallExpr cl, Scope scope)
    {
        try
        {
            switch (cl.Name)
            {
                case "wsp_yparam2":          return EvalYParam2(cl, scope);
                case "wsp_yparam2_residual": return EvalYParam2Residual(cl, scope);
                case "wsp_block_calc":       return EvalBlockCalc(cl, scope);
                case "wsp_block_breakout":   return EvalBreakout(cl, scope, feedback: false);
                case "wsp_fb_breakout":      return EvalBreakout(cl, scope, feedback: true);
                case "wsp_block_design":     return EvalDesign(cl, scope, feedback: false);
                case "wsp_fb_design":        return EvalDesign(cl, scope, feedback: true);
                case "wsp_bifurcate":        return EvalBifurcate(cl, scope);
                case "wsp_YA": case "wsp_YF": case "wsp_ZA": case "wsp_ZF":
                                             return EvalBifurcateAlias(cl, scope);
                case "wsp_loopgain_ohtomo":  return EvalOhtomo(cl, scope);
                case "wsp_unstable_freq_loopgain": return EvalUnstableLoopGain(cl, scope);
                case "wsp_ymatrix":          return EvalYmatrix(cl, scope);
                case "wsp_ndf":              return EvalNdf(cl, scope);
                case "wsp_terminate":        return EvalTerminate(cl, scope);
                case "wsp_loadpull":         return EvalLoadpull(cl, scope, unstable: false);
                case "wsp_loadpull_unstable": return EvalLoadpull(cl, scope, unstable: true);
                default: throw new UnknownFunctionException(cl.Name);
            }
        }
        catch (ArgumentException ex)          { throw new ExpressionException($"{cl.Name}: {ex.Message}"); }
        catch (InvalidOperationException ex)  { throw new ExpressionException($"{cl.Name}: {ex.Message}"); }
    }

    // ══ A wsp argument, with the analysis that produced it ═══════════════════

    /// <summary>A wsp cube taken apart: its leading axes, its <c>2N</c>, its raw blocks, and — when
    /// the cube is the analysis' own object — the DataSet it came from, for probe labels and the
    /// termination metadata.</summary>
    private sealed class WspArg(DataCube cube, Axis[] leading, int n, Complex[] raw, DataSet? owner)
    {
        public DataCube Cube    { get; } = cube;
        public Axis[]   Leading { get; } = leading;
        public int      N       { get; } = n;
        public Complex[] Raw    { get; } = raw;
        public DataSet? Owner   { get; } = owner;
        public int Probes => N / 2;
        public int Blocks => Raw.Length / (N * N);
        public Complex[,] Block(int b) => BlockAt(Raw, b, N);

        public string[]? Labels
            => Owner is { } o && o.Contains("__WspProbes") ? o["__WspProbes"].Axes[0].Labels : null;

        /// <summary>The document's label of probe <paramref name="idx"/>, or its number.</summary>
        public string Label(int idx)
        {
            var labels = Labels;
            if (labels is null) return idx.ToString();
            var idxVals = Owner!["__WspProbes"].RealValues;
            for (int k = 0; k < idxVals.Length; k++)
                if ((int)Math.Round(idxVals[k]) == idx) return labels[k];
            return idx.ToString();
        }

        /// <summary>The declared Z of the Term at the named terminal of probe <paramref name="idx"/>,
        /// or null when the run recorded none (or the owner is unknown).</summary>
        public Complex? TermZ(int idx, WspSide side)
        {
            if (Owner is not { } o || !o.Contains("__WspTermZ")) return null;
            var t = o["__WspTermZ"];
            var idxVals = o["__WspProbes"].RealValues;
            for (int k = 0; k < idxVals.Length; k++)
                if ((int)Math.Round(idxVals[k]) == idx)
                {
                    var z = (Complex)t[k, side == WspSide.G ? 0 : 1];
                    return Complex.IsNaN(z) ? null : z;
                }
            return null;
        }
    }

    private WspArg WspArgAt(CallExpr cl, Scope scope, int i)
    {
        var cube = CubeArg(cl, scope, i, "a wsp matrix cube ({…, freq, row, col})");
        var (leading, n, raw) = MatrixCube(cube, cl.Name, "a wsp matrix");
        if ((n & 1) != 0)
            throw new ExpressionException($"{cl.Name}: a wsp matrix is 2N × 2N; this cube is {n} × {n}.");
        DataSet? owner = null;
        _ctx?.TryFindWspOwner(cube, out owner);
        return new WspArg(cube, leading, n, raw, owner);
    }

    /// <summary>A probe list: an integer, or a quoted string of comma-separated idx numbers or
    /// labels; null (every probe, idx order) when the argument is absent.</summary>
    private int[]? ProbeListArg(CallExpr cl, Scope scope, int i, WspArg w)
    {
        if (cl.Args.Length <= i) return null;
        var v = EvalExpr(cl.Args[i], scope);
        if (v.Kind == ValueKind.Real) return [IdxArg(cl, scope, i, w.Probes)];
        if (v.Kind != ValueKind.String)
            throw new TypeErrorException(
                $"{cl.Name}: argument {i + 1} is a probe list — an integer idx, or a quoted string of " +
                "comma-separated idx numbers or probe labels (\"1,3\", \"GATE,DRAIN\"); omit it for every probe.");
        string spec = v.AsString().Trim();
        if (spec.Length == 0 || spec.Equals("all", StringComparison.OrdinalIgnoreCase)) return null;

        var labels = w.Labels;
        var outp = new List<int>();
        foreach (string tokRaw in spec.Split(','))
        {
            string tok = tokRaw.Trim();
            if (int.TryParse(tok, out int idx)) { outp.Add(idx); continue; }
            int k = labels is null ? -1 : Array.FindIndex(labels, l => l.Equals(tok, StringComparison.Ordinal));
            if (k < 0)
                throw new ExpressionException(
                    $"{cl.Name}: no WSProbe named '{tok}'" +
                    (labels is null
                        ? " — probe labels resolve only on an analysis' own wsp cube (SP1.wsp); use idx numbers here."
                        : $". Probes present: [{string.Join(", ", labels)}]"));
            outp.Add((int)Math.Round(w.Owner!["__WspProbes"].RealValues[k]));
        }
        foreach (int idx in outp)
            if (idx < 1 || idx > w.Probes)
                throw new ExpressionException(
                    $"{cl.Name}: probe index {idx} is outside 1..{w.Probes} (the wsp matrix is 2N × 2N for N probes).");
        return [.. outp];
    }

    // ══ Output shapes ════════════════════════════════════════════════════════

    /// <summary>An <c>N×N</c> network per block as <c>{…, i, j}</c>, the port axes valued by probe idx
    /// and labelled by probe label — so <c>Y[:, 2, 1]</c> reads by PORT NUMBER exactly as it does on
    /// an <c>S</c> cube.</summary>
    private static Value NetworkCube(WspArg w, int[] probes, Complex[] vals, string unit)
    {
        var idxVals = probes.Select(p => (double)p).ToArray();
        var labels  = probes.Select(w.Label).ToArray();
        var ai = new Axis("i", idxVals, "port", labels);
        var aj = new Axis("j", (double[])idxVals.Clone(), "port", (string[])labels.Clone());
        return new Value(new DataCube([.. w.Leading, ai, aj], vals) { Unit = unit });
    }

    private static Value TwoPortCube(Axis[] leading, Complex[] vals, string unit)
        => new(new DataCube([.. leading, PortAxis("i"), PortAxis("j")], vals) { Unit = unit });

    private static Value LabelledCube(Axis[] leading, string axisName, string[] labels, Complex[] vals, string unit)
    {
        var ax = new Axis(axisName, [.. Enumerable.Range(1, labels.Length).Select(k => (double)k)], "", labels);
        return new Value(new DataCube([.. leading, ax], vals) { Unit = unit });
    }

    private Complex Z0Arg(CallExpr cl, Scope scope, int i)
        => cl.Args.Length > i ? EvalExpr(cl.Args[i], scope).ToComplex() : new Complex(50.0, 0.0);

    // ══ §5: the probe pair ═══════════════════════════════════════════════════

    /// <summary><c>wsp_yparam2(wsp, idx1, idx2 [, "inner"|"feedback"])</c> — the document's
    /// <c>YP(1..8)</c> (Eq. 137–139) as <c>{…, freq, k}</c> labelled <c>y11 … yf22</c>; with the
    /// fourth argument, that one block as a 2-port cube <c>{…, freq, i, j}</c>.</summary>
    private Value EvalYParam2(CallExpr cl, Scope scope)
    {
        Arity(cl, 3, 4);
        var w  = WspArgAt(cl, scope, 0);
        int i1 = IdxArg(cl, scope, 1, w.Probes), i2 = IdxArg(cl, scope, 2, w.Probes);
        if (i1 == i2) throw new ExpressionException($"{cl.Name}: the two probes must differ (both are {i1}).");

        string? which = null;
        if (cl.Args.Length == 4)
        {
            var v = EvalExpr(cl.Args[3], scope);
            which = (v.Kind == ValueKind.String ? v.AsString() : v.ToString()!).Trim().ToLowerInvariant();
            if (which is not ("inner" or "feedback"))
                throw new ExpressionException(
                    $"{cl.Name}: the fourth argument selects one block as a 2-port — \"inner\" ([Y], between " +
                    $"probe 1's L and probe 2's G) or \"feedback\" ([Yf], everything else) — not \"{which}\".");
        }

        int blocks = w.Blocks;
        if (which is null)
        {
            var outv = new Complex[blocks * 8];
            for (int b = 0; b < blocks; b++)
            {
                var p = WspPair.YParam2(w.Block(b), i1, i2);
                outv[b * 8 + 0] = p.Inner.Y11;    outv[b * 8 + 1] = p.Inner.Y12;
                outv[b * 8 + 2] = p.Inner.Y21;    outv[b * 8 + 3] = p.Inner.Y22;
                outv[b * 8 + 4] = p.Feedback.Y11; outv[b * 8 + 5] = p.Feedback.Y12;
                outv[b * 8 + 6] = p.Feedback.Y21; outv[b * 8 + 7] = p.Feedback.Y22;
            }
            return LabelledCube(w.Leading, "k",
                ["y11", "y12", "y21", "y22", "yf11", "yf12", "yf21", "yf22"], outv, "S");
        }
        else
        {
            var outv = new Complex[blocks * 4];
            for (int b = 0; b < blocks; b++)
            {
                var p = WspPair.YParam2(w.Block(b), i1, i2);
                var y = which == "inner" ? p.Inner : p.Feedback;
                outv[b * 4 + 0] = y.Y11; outv[b * 4 + 1] = y.Y12;
                outv[b * 4 + 2] = y.Y21; outv[b * 4 + 3] = y.Y22;
            }
            return TwoPortCube(w.Leading, outv, "S");
        }
    }

    /// <summary><c>wsp_yparam2_residual(wsp, idx1, idx2)</c> — <c>max|Y_series − Y_shunt|</c>,
    /// relative; round-off when the pair brackets a two-port, large when a third connection joins
    /// the region between them to the rest of the network.</summary>
    private Value EvalYParam2Residual(CallExpr cl, Scope scope)
    {
        Arity(cl, 3, 3);
        var w  = WspArgAt(cl, scope, 0);
        int i1 = IdxArg(cl, scope, 1, w.Probes), i2 = IdxArg(cl, scope, 2, w.Probes);
        if (i1 == i2) throw new ExpressionException($"{cl.Name}: the two probes must differ (both are {i1}).");
        var outv = new double[w.Blocks];
        for (int b = 0; b < w.Blocks; b++) outv[b] = WspPair.YParam2Residual(w.Block(b), i1, i2);
        return LeadingCube(w.Leading, outv, "");
    }

    /// <summary><c>wsp_block_calc(wsp, idx1, idx2 [, Z0 = 50])</c> — the sixteen outputs of
    /// Eq. 142–151 as <c>{…, freq, k}</c>, <c>k = 1 … 16</c> labelled with the document's names.</summary>
    private Value EvalBlockCalc(CallExpr cl, Scope scope)
    {
        Arity(cl, 3, 4);
        var w  = WspArgAt(cl, scope, 0);
        int i1 = IdxArg(cl, scope, 1, w.Probes), i2 = IdxArg(cl, scope, 2, w.Probes);
        if (i1 == i2) throw new ExpressionException($"{cl.Name}: the two probes must differ (both are {i1}).");
        var z0 = Z0Arg(cl, scope, 3);
        var outv = new Complex[w.Blocks * 16];
        for (int b = 0; b < w.Blocks; b++)
        {
            var r = WspPair.BlockCalc(w.Block(b), i1, i2, z0);
            Array.Copy(r, 0, outv, b * 16, 16);
        }
        return LabelledCube(w.Leading, "k", WspPair.BlockCalcLabels, outv, "");
    }

    /// <summary><c>wsp_block_breakout</c> / <c>wsp_fb_breakout(wsp, idx1, idx2 [, Z0 = 50])</c> —
    /// the inner / feedback block's S-parameters as a 2-port cube (E.5, E.6).</summary>
    private Value EvalBreakout(CallExpr cl, Scope scope, bool feedback)
    {
        Arity(cl, 3, 4);
        var w  = WspArgAt(cl, scope, 0);
        int i1 = IdxArg(cl, scope, 1, w.Probes), i2 = IdxArg(cl, scope, 2, w.Probes);
        if (i1 == i2) throw new ExpressionException($"{cl.Name}: the two probes must differ (both are {i1}).");
        var z0 = Z0Arg(cl, scope, 3);
        var outv = new Complex[w.Blocks * 4];
        for (int b = 0; b < w.Blocks; b++)
        {
            var s = feedback ? WspPair.FbBreakout(w.Block(b), i1, i2, z0) : WspPair.BlockBreakout(w.Block(b), i1, i2, z0);
            outv[b * 4 + 0] = s[0, 0]; outv[b * 4 + 1] = s[0, 1];
            outv[b * 4 + 2] = s[1, 0]; outv[b * 4 + 3] = s[1, 1];
        }
        return TwoPortCube(w.Leading, outv, "");
    }

    /// <summary><c>wsp_block_design</c> / <c>wsp_fb_design(wsp, idx1, idx2 [, Z0 = 50 [, freq]])</c> —
    /// the block renormalised in situ to the probes' own bidirectional impedances as parallel RC
    /// pairs (E.8, E.9). The frequency the capacitances are evaluated at is the cube's own
    /// <c>freq</c> axis unless stated.</summary>
    private Value EvalDesign(CallExpr cl, Scope scope, bool feedback)
    {
        Arity(cl, 3, 5);
        var w  = WspArgAt(cl, scope, 0);
        int i1 = IdxArg(cl, scope, 1, w.Probes), i2 = IdxArg(cl, scope, 2, w.Probes);
        if (i1 == i2) throw new ExpressionException($"{cl.Name}: the two probes must differ (both are {i1}).");
        var z0 = Z0Arg(cl, scope, 3);
        var f  = FreqPerBlock(cl, scope, 4, w.Leading, w.Blocks);
        var outv = new Complex[w.Blocks * 4];
        for (int b = 0; b < w.Blocks; b++)
        {
            var s = feedback
                ? WspPair.FbDesign(w.Block(b), i1, i2, f[b], z0)
                : WspPair.BlockDesign(w.Block(b), i1, i2, f[b], z0);
            outv[b * 4 + 0] = s[0, 0]; outv[b * 4 + 1] = s[0, 1];
            outv[b * 4 + 2] = s[1, 0]; outv[b * 4 + 3] = s[1, 1];
        }
        return TwoPortCube(w.Leading, outv, "");
    }

    // ══ §6: bifurcation ══════════════════════════════════════════════════════

    /// <summary><c>wsp_bifurcate(wsp, form, side [, probes])</c> — the TRUE matrix of the named
    /// side (<c>"G"</c> or <c>"L"</c>) in the named form (<c>"Y"</c> or <c>"Z"</c>), as
    /// <c>{…, freq, i, j}</c> with <c>i</c>, <c>j</c> labelled by probe label in list order.</summary>
    private Value EvalBifurcate(CallExpr cl, Scope scope)
    {
        Arity(cl, 3, 4);
        var w = WspArgAt(cl, scope, 0);
        var form = WspBifurcation.ParseForm(StringArg(cl, scope, 1, "the form, \"Y\" or \"Z\""));
        var side = WspBifurcation.ParseSide(StringArg(cl, scope, 2, "the side, \"G\" or \"L\""));
        var probes = ProbeListArg(cl, scope, 3, w);
        return BifurcateCube(w, form, side, probes);
    }

    /// <summary><c>wsp_YA</c>, <c>wsp_YF</c>, <c>wsp_ZA</c>, <c>wsp_ZF(wsp [, probes])</c> — the
    /// document's four names, with the document's sides: <c>YA</c> = Y of the G side, <c>YF</c> = Y
    /// of the L side, <c>ZA</c> = Z of the <b>L</b> side, <c>ZF</c> = Z of the <b>G</b> side. The
    /// document's <c>A</c>/<c>F</c> labels do not agree between its two forms (overview T-9); these
    /// aliases keep its labels and say which side each one is.</summary>
    private Value EvalBifurcateAlias(CallExpr cl, Scope scope)
    {
        Arity(cl, 1, 2);
        var w = WspArgAt(cl, scope, 0);
        var (form, side) = WspBifurcation.DocumentAlias(cl.Name);
        var probes = ProbeListArg(cl, scope, 1, w);
        return BifurcateCube(w, form, side, probes);
    }

    private static Value BifurcateCube(WspArg w, WspForm form, WspSide side, int[]? probes)
    {
        probes ??= WspMatrix.AllProbes(w.Block(0));
        int n = probes.Length;
        var outv = new Complex[w.Blocks * n * n];
        for (int b = 0; b < w.Blocks; b++)
        {
            var m = WspBifurcation.Bifurcate(w.Block(b), form, side, probes);
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++) outv[(b * n + r) * n + c] = m[r, c];
        }
        return NetworkCube(w, probes, outv, form == WspForm.Y ? "S" : "Ohm");
    }

    private string StringArg(CallExpr cl, Scope scope, int i, string what)
    {
        var v = EvalExpr(cl.Args[i], scope);
        if (v.Kind != ValueKind.String)
            throw new TypeErrorException($"{cl.Name}: argument {i + 1} is {what}, a quoted string.");
        return v.AsString();
    }

    // ══ §7: Ohtomo ═══════════════════════════════════════════════════════════

    /// <summary><c>wsp_loopgain_ohtomo(wsp, probes [, active = "G", Z0 = 50])</c> — the sequential
    /// loop gains <c>G_1 … G_N</c> (Eq. 177–180, T-10) as <c>{…, freq, node}</c>, <c>node</c>
    /// labelled by probe label in list order. Assumes each subnetwork is stable on its own (§7,
    /// p. 108); nothing here can check that.</summary>
    private Value EvalOhtomo(CallExpr cl, Scope scope)
    {
        Arity(cl, 2, 4);
        var w = WspArgAt(cl, scope, 0);
        var probes = ProbeListArg(cl, scope, 1, w) ?? WspMatrix.AllProbes(w.Block(0));
        var active = cl.Args.Length > 2 ? WspBifurcation.ParseSide(StringArg(cl, scope, 2, "the active side, \"G\" or \"L\"")) : WspSide.G;
        var z0 = Z0Arg(cl, scope, 3);
        int n = probes.Length;
        var outv = new Complex[w.Blocks * n];
        for (int b = 0; b < w.Blocks; b++)
        {
            var g = WspOhtomo.LoopGains(w.Block(b), probes, active, z0);
            Array.Copy(g, 0, outv, b * n, n);
        }
        var ax = new Axis("node", probes.Select(p => (double)p).ToArray(), "", probes.Select(w.Label).ToArray());
        return new Value(new DataCube([.. w.Leading, ax], outv));
    }

    /// <summary><c>wsp_unstable_freq_loopgain(G)</c> — the frequencies at which a loop gain crosses
    /// the positive real axis clockwise with <c>|G| ≥ 1</c> (p. 110). Same rules as the Kurokawa
    /// search: a <c>{n}</c> cube, possibly empty; swept data refused.</summary>
    private Value EvalUnstableLoopGain(CallExpr cl, Scope scope)
    {
        Arity(cl, 1, 1);
        var cube = CubeArg(cl, scope, 0, "a loop-gain cube ({freq}) — SP1.LG(\"GATE\") or one node of wsp_loopgain_ohtomo(...)");
        if (cube.Rank != 1)
            throw new ExpressionException(
                $"{cl.Name}: needs a single loop gain over frequency ({{freq}}); this cube has {cube.Rank} axes " +
                $"({string.Join(", ", cube.Axes.Select(a => a.Name))}). Pin every axis but the frequency first — " +
                "at(<cube>, \"<axis>\", <index>) — so the answer is one list of frequencies.");
        var freqs = cube.Axes[0].Values;
        var vals  = cube.DataKind == DataKind.Complex ? cube.ComplexValues : [.. cube.RealValues.Select(x => new Complex(x, 0))];
        var hits  = WspOhtomo.UnstableFrequenciesLoopGain(vals, freqs);
        var axis  = new Axis("n", [.. Enumerable.Range(0, hits.Length).Select(i => (double)(i + 1))]);
        return new Value(new DataCube([axis], hits) { Unit = cube.Axes[0].Unit });
    }

    // ══ §8: the reduced matrices ═════════════════════════════════════════════

    /// <summary><c>wsp_ymatrix(wsp [, probes])</c> — the admittance matrix of the whole network at
    /// the probe nodes, <c>Y = (IVᵀ)⁻¹</c> (Eq. 184–185), as <c>{…, freq, i, j}</c>.</summary>
    private Value EvalYmatrix(CallExpr cl, Scope scope)
    {
        Arity(cl, 1, 2);
        var w = WspArgAt(cl, scope, 0);
        var probes = ProbeListArg(cl, scope, 1, w) ?? WspMatrix.AllProbes(w.Block(0));
        int n = probes.Length;
        var outv = new Complex[w.Blocks * n * n];
        for (int b = 0; b < w.Blocks; b++)
        {
            var m = WspGlobal.Ymatrix(w.Block(b), probes);
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++) outv[(b * n + r) * n + c] = m[r, c];
        }
        return NetworkCube(w, probes, outv, "S");
    }

    /// <summary><c>wsp_ndf(wsp_active, wsp_passive [, probes])</c> — <c>det(Z_passive)/det(Z)</c>
    /// (Eq. 186) over frequency, from one ordinary and one passivated run of the same probed
    /// circuit. The determinants are taken in log form.</summary>
    private Value EvalNdf(CallExpr cl, Scope scope)
    {
        Arity(cl, 2, 3);
        var a = WspArgAt(cl, scope, 0);
        var p = WspArgAt(cl, scope, 1);
        if (a.N != p.N || a.Blocks != p.Blocks)
            throw new ExpressionException(
                $"{cl.Name}: the two wsp cubes must come from the same probed circuit over the same sweep " +
                $"({a.N}×{a.N} × {a.Blocks} blocks vs {p.N}×{p.N} × {p.Blocks}).");
        var probes = ProbeListArg(cl, scope, 2, a);
        var outv = new Complex[a.Blocks];
        for (int b = 0; b < a.Blocks; b++) outv[b] = WspGlobal.Ndf(a.Block(b), p.Block(b), probes);
        return LeadingCube(a.Leading, outv, "");
    }

    // ══ §9: the envelope ═════════════════════════════════════════════════════

    /// <summary>The refusal every envelope function meets when the wsp cube is not the analysis'
    /// own object, because then the termination metadata cannot be found.</summary>
    private static ExpressionException NoOwner(CallExpr cl)
        => new($"{cl.Name}: cannot verify that the probes sit at their terminations — the wsp argument is " +
               "not an analysis' own wsp cube (SP1.wsp), so its __WspTermZ metadata is not reachable. Pass " +
               "the analysis' cube directly, not a sliced or derived one.");

    /// <summary>The precondition of §6.1 on one probe, over every block of the cube.</summary>
    private static void RequireTermination(CallExpr cl, WspArg w, int idx, WspSide side)
    {
        if (idx <= 0) return;
        if (w.Owner is null) throw NoOwner(cl);
        var blocks = new Complex[w.Blocks][,];
        for (int b = 0; b < w.Blocks; b++) blocks[b] = w.Block(b);
        WspEnvelope.RequireAtTermination(blocks, idx, side, w.TermZ(idx, side), w.Label(idx));
    }

    /// <summary><c>wsp_terminate(wsp, idxS, YS, idxL, YL [, YSo, YLo])</c> — the complete wsp of the
    /// circuit with the source termination at probe <c>idxS</c> (G facing it) changed to <c>YS</c>
    /// and the load termination at probe <c>idxL</c> (L facing it) changed to <c>YL</c>, by rank-1
    /// updates (§6.1). <c>YSo</c>/<c>YLo</c> default to <c>1/ZG</c> and <c>1/ZL</c> of those probes.
    /// An index of 0 leaves that side alone. Refuses when a probe is not directly at its Term.</summary>
    private Value EvalTerminate(CallExpr cl, Scope scope)
    {
        Arity(cl, 5, 7);
        var w    = WspArgAt(cl, scope, 0);
        int idxS = IdxOrZero(cl, scope, 1, w.Probes);
        var ys   = PerBlockComplex(EvalExpr(cl.Args[2], scope), w.Blocks, cl.Name, "YS");
        int idxL = IdxOrZero(cl, scope, 3, w.Probes);
        var yl   = PerBlockComplex(EvalExpr(cl.Args[4], scope), w.Blocks, cl.Name, "YL");
        var yso  = cl.Args.Length > 5 ? PerBlockComplex(EvalExpr(cl.Args[5], scope), w.Blocks, cl.Name, "YSo") : null;
        var ylo  = cl.Args.Length > 6 ? PerBlockComplex(EvalExpr(cl.Args[6], scope), w.Blocks, cl.Name, "YLo") : null;
        if (idxS == 0 && idxL == 0)
            throw new ExpressionException($"{cl.Name}: neither side is pulled (both probe indices are 0).");

        RequireTermination(cl, w, idxS, WspSide.G);
        RequireTermination(cl, w, idxL, WspSide.L);

        int n = w.N;
        var outv = new Complex[w.Raw.Length];
        for (int b = 0; b < w.Blocks; b++)
        {
            var blk = w.Block(b);
            var so  = idxS > 0 ? (yso?[b] ?? WspEnvelope.StartingAdmittance(blk, idxS, WspSide.G)) : Complex.Zero;
            var lo  = idxL > 0 ? (ylo?[b] ?? WspEnvelope.StartingAdmittance(blk, idxL, WspSide.L)) : Complex.Zero;
            var t   = WspEnvelope.Terminate(blk, idxS, ys[b], so, idxL, yl[b], lo);
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++) outv[(b * n + r) * n + c] = t[r, c];
        }
        return new Value(new DataCube([.. w.Cube.Axes], outv) { Unit = w.Cube.Unit });
    }

    private int IdxOrZero(CallExpr cl, Scope scope, int i, int probeCount)
    {
        var v = EvalExpr(cl.Args[i], scope);
        if (v.Kind == ValueKind.Real && (int)Math.Round(v.AsReal()) == 0) return 0;
        return IdxArg(cl, scope, i, probeCount);
    }

    /// <summary>A Γ grid: one reflection coefficient, a cube of them, or <c>"|Γ|:count"</c> for a
    /// constant-|Γ| circle from θ = 0° (optionally <c>"|Γ|:count@start°"</c>).</summary>
    private Complex[] GammaGridArg(CallExpr cl, Scope scope, int i, string what)
    {
        var v = EvalExpr(cl.Args[i], scope);
        switch (v.Kind)
        {
            case ValueKind.Real:
            case ValueKind.Complex:
                return [v.ToComplex()];
            case ValueKind.Cube:
            {
                var c = v.AsCube();
                return c.DataKind == DataKind.Complex ? c.ComplexValues : [.. c.RealValues.Select(x => new Complex(x, 0))];
            }
            case ValueKind.String:
            {
                string spec = v.AsString().Trim();
                string body = spec, start = "0";
                int at = spec.IndexOf('@');
                if (at >= 0) { body = spec[..at]; start = spec[(at + 1)..].TrimEnd('°', ' '); }
                var parts = body.Split(':');
                if (parts.Length == 2
                    && double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out double mag)
                    && int.TryParse(parts[1].Trim(), out int count)
                    && double.TryParse(start, System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out double startDeg))
                    return WspEnvelope.CircleGrid(mag, count, startDeg);
                throw new ExpressionException(
                    $"{cl.Name}: {what} as a string is a constant-|Γ| circle, \"|Γ|:count\" (e.g. \"0.8:24\" — 24 " +
                    $"points on |Γ| = 0.8 from θ = 0°, or \"0.8:24@15\" to start at 15°), not \"{spec}\". A single " +
                    "Γ or a cube of them is also accepted.");
            }
            default:
                throw new TypeErrorException($"{cl.Name}: {what} must be a reflection coefficient, a cube of them, or \"|Γ|:count\".");
        }
    }

    private static Axis GammaAxis(string name, Complex[] grid, bool pulled)
    {
        if (!pulled) return new Axis(name, [0.0], "", ["unpulled"]);
        var labels = grid.Select(g => string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{g.Magnitude:0.###}@{g.Phase * 180.0 / Math.PI:0.#}")).ToArray();
        return new Axis(name, [.. Enumerable.Range(0, grid.Length).Select(k => (double)k)], "", labels);
    }

    /// <summary>
    /// <c>wsp_loadpull(wsp, idxS, idxL, idx, gammaS, gammaL [, Z0 = 50])</c> — <c>H0'</c> and
    /// <c>Y0'</c> at probe <c>idx</c> over the two Γ grids, as <c>{…, gS, gL, freq, env}</c> with
    /// <c>env</c> labelled <c>H0env</c>, <c>Y0env</c> (§6.2). The grid axes carry each Γ as a label
    /// (<c>0.8@60</c>) so the Data Display can lay them on a polar chart per θ.
    /// <c>wsp_loadpull_unstable(...)</c>, same arguments, runs the Kurokawa search on both at every
    /// grid point and returns a Real <c>{…, gS, gL, item}</c>: <c>unstable</c> (the count over both
    /// immittances), <c>unstable_H0</c>, <c>unstable_Y0</c>, then the frequencies found,
    /// <c>f1 … fK</c>, NaN-padded to the longest list (§6.3).
    /// </summary>
    private Value EvalLoadpull(CallExpr cl, Scope scope, bool unstable)
    {
        Arity(cl, 6, 7);
        var w    = WspArgAt(cl, scope, 0);
        int idxS = IdxOrZero(cl, scope, 1, w.Probes);
        int idxL = IdxOrZero(cl, scope, 2, w.Probes);
        int idx  = IdxArg(cl, scope, 3, w.Probes);
        var gS   = idxS > 0 ? GammaGridArg(cl, scope, 4, "gammaS") : [new Complex(double.NaN, double.NaN)];
        var gL   = idxL > 0 ? GammaGridArg(cl, scope, 5, "gammaL") : [new Complex(double.NaN, double.NaN)];
        var z0   = Z0Arg(cl, scope, 6);
        if (idxS == 0 && idxL == 0)
            throw new ExpressionException($"{cl.Name}: neither side is pulled (both probe indices are 0).");

        int fa = FreqAxisIndex(w.Leading);
        if (fa != w.Leading.Length - 1)
            throw new ExpressionException(
                $"{cl.Name}: the wsp cube's frequency axis must be the last axis before row/col; this cube's " +
                $"axes are ({string.Join(", ", w.Cube.Axes.Select(a => a.Name))}). Pin the other axes with at(...).");
        int nf = w.Leading[fa].Length;
        var freqs = w.Leading[fa].Values;
        var outer = w.Leading.Take(fa).ToArray();
        int nOuter = w.Blocks / nf;

        RequireTermination(cl, w, idxS, WspSide.G);
        RequireTermination(cl, w, idxL, WspSide.L);

        int ns = gS.Length, nl = gL.Length;
        var axS = GammaAxis("gS", gS, idxS > 0);
        var axL = GammaAxis("gL", gL, idxL > 0);

        var results = new WspLoadpullResult[nOuter];
        for (int o = 0; o < nOuter; o++)
        {
            var per = new Complex[nf][,];
            for (int fi = 0; fi < nf; fi++) per[fi] = w.Block(o * nf + fi);
            results[o] = WspEnvelope.Loadpull(per, freqs, idxS, idxL, idx, gS, gL, z0);
        }

        if (!unstable)
        {
            var outv = new Complex[nOuter * ns * nl * nf * 2];
            for (int o = 0; o < nOuter; o++)
                for (int s = 0; s < ns; s++)
                    for (int l = 0; l < nl; l++)
                        for (int fi = 0; fi < nf; fi++)
                        {
                            int k = (((o * ns + s) * nl + l) * nf + fi) * 2;
                            outv[k]     = results[o].H0[s, l, fi];
                            outv[k + 1] = results[o].Y0[s, l, fi];
                        }
            var freqAxis = w.Leading[fa];
            var env = new Axis("env", [0.0, 1.0], "", ["H0env", "Y0env"]);
            return new Value(new DataCube([.. outer, axS, axL, freqAxis, env], outv));
        }
        else
        {
            var found = results.Select(WspEnvelope.LoadpullUnstable).ToArray();
            int kmax = 0;
            foreach (var f in found)
                for (int s = 0; s < ns; s++)
                    for (int l = 0; l < nl; l++) kmax = Math.Max(kmax, f.Count(s, l));
            int items = 3 + kmax;
            var outv = new double[nOuter * ns * nl * items];
            for (int o = 0; o < nOuter; o++)
                for (int s = 0; s < ns; s++)
                    for (int l = 0; l < nl; l++)
                    {
                        int k = ((o * ns + s) * nl + l) * items;
                        var fh = found[o].FromH0[s][l]; var fy = found[o].FromY0[s][l];
                        outv[k]     = fh.Length + fy.Length;
                        outv[k + 1] = fh.Length;
                        outv[k + 2] = fy.Length;
                        var all = fh.Concat(fy).OrderBy(x => x).ToArray();
                        for (int q = 0; q < kmax; q++) outv[k + 3 + q] = q < all.Length ? all[q] : double.NaN;
                    }
            var labels = new List<string> { "unstable", "unstable_H0", "unstable_Y0" };
            for (int q = 1; q <= kmax; q++) labels.Add($"f{q}");
            var item = new Axis("item", [.. Enumerable.Range(0, items).Select(q => (double)q)], "", [.. labels]);
            return new Value(new DataCube([.. outer, axS, axL, item], outv));
        }
    }
}
