using System.Numerics;
using RfCore.Data;
using RfCore.Stability;

namespace CircuitRF.Core.Expressions;

/// <summary>
/// The single-probe derived metrics of the WSProbe reference document, registered as built-ins with
/// the document's own names and argument order — so a designer who has read it can type
/// <c>wsp_yparam(SP1.wsp, SP1.idx("GATE"))</c> and get Eq. 44
/// (brief-wsprobe-2-nodal-functions.md; overview D-5, D-6).
///
/// <para><b>Nothing here computes anything.</b> Every value comes from
/// <c>src/RfCore/Stability/</c> — <see cref="WspReduction"/>, <see cref="WspNodal"/>,
/// <see cref="WspKurokawa"/>, <see cref="WspTransfer"/>, <see cref="ImmittanceModels"/>,
/// <see cref="GainDefinitions"/>, <see cref="WspRenorm"/> — whose scalar cores are the tested unit.
/// This file is the cube glue: it reads a <c>{…, freq, row, col}</c> cube one 2N×2N block at a
/// time, calls the scalar core, and reassembles the answer with the leading axes (any parametric
/// sweep included) untouched. That is why a run's <c>ZG:GATE</c> cube and a trace card's
/// <c>wsp_ZG</c> of the same probe are bit-identical: one implementation, two callers.</para>
///
/// <para>Reference throughout: T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i>
/// (2023).</para>
/// </summary>
public sealed partial class Evaluator
{
    /// <summary>Every name this file registers, so <see cref="EvalCall"/> can route to it and the
    /// docs can be checked against the code rather than against a memory of it.</summary>
    internal static bool IsWspBuiltin(string name) => name switch
    {
        "wsp_yparam" or "wsp_zparam" or "wsp_H0" or "wsp_Y0" or "wsp_ZG" or "wsp_ZL" or
        "wsp_YG" or "wsp_YL" or "wsp_zop" or "wsp_yop" or "wsp_loopgain" or "wsp_nodal_gamma" or
        "wsp_nZ" or "wsp_nY" or "wsp_stability_margin" or
        "wsp_unstable_freq_kurokawa" or "encirculations" or "enc" or "_dB" or
        "wsp_zsrc" or "wsp_zprc" or "wsp_impedance" or "wsp_gain" or "GainDEFs" or
        "wsp_rc_renorm_s" or "wsp_zo_renorm_s" or
        "z_to_pr" or "z_to_pc" or "z_to_pl" or "z_to_sr" or "z_to_sc" or "z_to_sl" or
        "y_to_pr" or "y_to_pc" or "y_to_pl" or "y_to_sr" or "y_to_sc" or "y_to_sl" => true,
        _ => false,
    };

    private Value EvalWspCall(CallExpr cl, Scope scope)
    {
        switch (cl.Name)
        {
            // ── The two reductions (Eq. 44, 48) ─────────────────────────────
            case "wsp_yparam":
                return TwoPortFromWsp(cl, scope, q =>
                {
                    var y = WspReduction.YParam(q);
                    return (y.Y11, y.Y12, y.Y21, y.Y22);
                }, unit: "S");

            case "wsp_zparam":
                return TwoPortFromWsp(cl, scope, q =>
                {
                    var z = WspReduction.ZParam(q);
                    return (z.Z11, z.Z12, z.Z21, z.Z22);
                }, unit: "Ohm");

            // ── Driving-point and bidirectional immittances (E.14, E.15) ────
            case "wsp_H0":  return ScalarFromWsp(cl, scope, q => WspNodal.H0(q), "Ohm");
            case "wsp_Y0":  return ScalarFromWsp(cl, scope, q => WspNodal.Y0(q), "S");
            case "wsp_ZG":  return ScalarFromWsp(cl, scope, q => WspNodal.ZG(q), "Ohm");
            case "wsp_ZL":  return ScalarFromWsp(cl, scope, q => WspNodal.ZL(q), "Ohm");
            case "wsp_YG":  return ScalarFromWsp(cl, scope, q => WspNodal.YG(WspReduction.YParam(q)), "S");
            case "wsp_YL":  return ScalarFromWsp(cl, scope, q => WspNodal.YL(WspReduction.YParam(q)), "S");

            // ── Open-port immittances (E.16) ────────────────────────────────
            case "wsp_zop": return ScalarFromWsp(cl, scope, q => WspNodal.Zop(WspReduction.YParam(q)), "Ohm");
            case "wsp_yop": return ScalarFromWsp(cl, scope, q => WspNodal.Yop(WspReduction.ZParam(q)), "S");

            // ── The nodal conjugate reflection coefficient (E.7) ────────────
            case "wsp_nodal_gamma": return ScalarFromWsp(cl, scope, q => WspNodal.NodalGamma(q), "");

            // ── circuitRF's normalised driving-point loci (overview D-12) ───
            case "wsp_nZ": return ScalarFromWsp(cl, scope, q => WspNodal.NormalizedLocusSeries(q), "");
            case "wsp_nY": return ScalarFromWsp(cl, scope,
                                q => WspNodal.NormalizedLocusShunt(WspReduction.YParam(q)), "");

            case "wsp_stability_margin":
                throw new ExpressionException(
                    $"wsp_stability_margin: {WspNodal.MarginNotTranscribedKey}. " +
                    WspNodal.MarginNotTranscribedMessage +
                    " circuitRF's own unitless driving-point loci wsp_nZ and wsp_nY are available " +
                    "in the meantime; they are not the published margin.");

            case "wsp_loopgain":              return EvalWspLoopGain(cl, scope);
            case "wsp_unstable_freq_kurokawa": return EvalWspKurokawa(cl, scope);
            case "encirculations":
            case "enc":                       return EvalEncirclements(cl, scope);
            case "_dB":                       return EvalUnderscoreDb(cl, scope);
            case "wsp_impedance":             return EvalWspImpedance(cl, scope);
            case "wsp_gain":                  return EvalWspGain(cl, scope);
            case "GainDEFs":                  return EvalGainDefs(cl, scope);
            case "wsp_zo_renorm_s":           return EvalZoRenorm(cl, scope);
            case "wsp_rc_renorm_s":           return EvalRcRenorm(cl, scope);

            // ── Immittance models (E.4, E.11) — base SI, farads and henries ─
            case "wsp_zsrc": return TwoRealsAndFreq(cl, scope, ImmittanceModels.Zsrc, "Ohm");
            case "wsp_zprc": return TwoRealsAndFreq(cl, scope, ImmittanceModels.Zprc, "Ohm");

            case "y_to_pr": return ImmittanceElement(cl, scope, (v, _) => ImmittanceModels.YToPr(v), needsFreq: false, "Ohm");
            case "y_to_sr": return ImmittanceElement(cl, scope, (v, _) => ImmittanceModels.YToSr(v), needsFreq: false, "Ohm");
            case "y_to_pc": return ImmittanceElement(cl, scope, ImmittanceModels.YToPc, needsFreq: true, "F");
            case "y_to_sc": return ImmittanceElement(cl, scope, ImmittanceModels.YToSc, needsFreq: true, "F");
            case "y_to_pl": return ImmittanceElement(cl, scope, ImmittanceModels.YToPl, needsFreq: true, "H");
            case "y_to_sl": return ImmittanceElement(cl, scope, ImmittanceModels.YToSl, needsFreq: true, "H");

            case "z_to_pr": return ImmittanceElement(cl, scope, (v, _) => ImmittanceModels.ZToPr(v), needsFreq: false, "Ohm");
            case "z_to_sr": return ImmittanceElement(cl, scope, (v, _) => ImmittanceModels.ZToSr(v), needsFreq: false, "Ohm");
            case "z_to_pc": return ImmittanceElement(cl, scope, ImmittanceModels.ZToPc, needsFreq: true, "F");
            case "z_to_sc": return ImmittanceElement(cl, scope, ImmittanceModels.ZToSc, needsFreq: true, "F");
            case "z_to_pl": return ImmittanceElement(cl, scope, ImmittanceModels.ZToPl, needsFreq: true, "H");
            case "z_to_sl": return ImmittanceElement(cl, scope, ImmittanceModels.ZToSl, needsFreq: true, "H");

            default: throw new UnknownFunctionException(cl.Name);
        }
    }

    // ══ Argument readers ═════════════════════════════════════════════════════

    private DataCube CubeArg(CallExpr cl, Scope scope, int i, string what)
    {
        var v = EvalExpr(cl.Args[i], scope);
        if (v.Kind != ValueKind.Cube)
            throw new TypeErrorException(
                $"{cl.Name}: argument {i + 1} must be {what}, not a single number. " +
                "A wsp matrix comes from an analysis (SP1.wsp); a two-port comes from " +
                "wsp_yparam(...) or an S cube.");
        return v.AsCube();
    }

    private int IdxArg(CallExpr cl, Scope scope, int i, int probeCount)
    {
        var v = EvalExpr(cl.Args[i], scope);
        if (v.Kind != ValueKind.Real)
            throw new TypeErrorException(
                $"{cl.Name}: argument {i + 1} must be a probe index — SP1.idx(\"GATE\") or an " +
                "integer literal (the document's 1-based idx, Eq. 50–52).");
        int idx = (int)Math.Round(v.AsReal());
        if (idx < 1 || idx > probeCount)
            throw new ExpressionException(
                $"{cl.Name}: probe index {idx} is outside 1..{probeCount} " +
                "(the wsp matrix is 2N × 2N for N probes). SP1.idx(\"<label>\") gives the index of a " +
                "named probe; an unknown label lists the probes present.");
        return idx;
    }

    private static void Arity(CallExpr cl, int min, int max)
    {
        if (cl.Args.Length < min || cl.Args.Length > max)
            throw new ArityException(cl.Name, min, cl.Args.Length);
    }

    // ══ Block machinery over a matrix-shaped cube ════════════════════════════
    //
    //  Every cube here has its MATRIX axes last — {…, freq, row, col} for a wsp cube, {…, freq,
    //  i, j} for a two-port. The leading axes are whatever the run produced (freq alone, or a
    //  parametric sweep in front of it) and are carried through untouched, which is the whole of
    //  what "and any prepended sweep axes" means in R-wsp2-1.

    /// <summary>The leading axes, the square matrix size and the raw blocks of a matrix-shaped
    /// cube, or an error naming what was wrong with its shape.</summary>
    private static (Axis[] Leading, int N, Complex[] Raw) MatrixCube(DataCube c, string fn, string what)
    {
        if (c.Rank < 2)
            throw new ExpressionException(
                $"{fn}: {what} must have its two matrix axes last; this cube has rank {c.Rank}.");
        int n = c.Axes[^1].Length;
        if (c.Axes[^2].Length != n)
            throw new ExpressionException(
                $"{fn}: {what} must be square; its last two axes are " +
                $"{c.Axes[^2].Length} × {n}.");
        return ([.. c.Axes.Take(c.Rank - 2)], n, c.ComplexValues);
    }

    private static Complex[,] BlockAt(Complex[] raw, int block, int n)
    {
        var m = new Complex[n, n];
        int b = block * n * n;
        for (int r = 0; r < n; r++)
        for (int col = 0; col < n; col++)
            m[r, col] = raw[b + r * n + col];
        return m;
    }

    /// <summary>Wrap a per-block result as a cube over the leading axes — or, when there are none,
    /// as the plain scalar it is.</summary>
    private static Value LeadingCube(Axis[] leading, Complex[] vals, string unit)
    {
        if (leading.Length == 0) return new Value(vals[0]);
        return new Value(new DataCube(leading, vals) { Unit = unit });
    }

    private static Value LeadingCube(Axis[] leading, double[] vals, string unit)
    {
        if (leading.Length == 0) return new Value(vals[0]);
        return new Value(new DataCube(leading, vals) { Unit = unit });
    }

    /// <summary><c>f(wsp, idx)</c> → one complex value per frequency (and per sweep point).</summary>
    private Value ScalarFromWsp(CallExpr cl, Scope scope, Func<WspProbeQuad, Complex> f, string unit)
    {
        Arity(cl, 2, 2);
        var cube = CubeArg(cl, scope, 0, "a wsp matrix cube ({…, freq, row, col})");
        var (leading, n, raw) = MatrixCube(cube, cl.Name, "a wsp matrix");
        int idx = IdxArg(cl, scope, 1, n / 2);

        int blocks = raw.Length / (n * n);
        var outv = new Complex[blocks];
        for (int b = 0; b < blocks; b++)
            outv[b] = f(WspProbeQuad.Of(BlockAt(raw, b, n), idx));
        return LeadingCube(leading, outv, unit);
    }

    /// <summary><c>f(wsp, idx)</c> → a 2×2 network per frequency, shaped exactly like the <c>S</c>
    /// cube (axes <c>i</c>, <c>j</c>, values 1 and 2, unit "port") so that every network-parameter
    /// path already in the repository accepts it.</summary>
    private Value TwoPortFromWsp(
        CallExpr cl, Scope scope, Func<WspProbeQuad, (Complex, Complex, Complex, Complex)> f, string unit)
    {
        Arity(cl, 2, 2);
        var cube = CubeArg(cl, scope, 0, "a wsp matrix cube ({…, freq, row, col})");
        var (leading, n, raw) = MatrixCube(cube, cl.Name, "a wsp matrix");
        int idx = IdxArg(cl, scope, 1, n / 2);

        int blocks = raw.Length / (n * n);
        var outv = new Complex[blocks * 4];
        for (int b = 0; b < blocks; b++)
        {
            var (m11, m12, m21, m22) = f(WspProbeQuad.Of(BlockAt(raw, b, n), idx));
            outv[b * 4 + 0] = m11; outv[b * 4 + 1] = m12;
            outv[b * 4 + 2] = m21; outv[b * 4 + 3] = m22;
        }
        return new Value(new DataCube([.. leading, PortAxis("i"), PortAxis("j")], outv) { Unit = unit });
    }

    private static Axis PortAxis(string name) => new(name, [1.0, 2.0], "port");

    /// <summary>A two-port cube's blocks as <see cref="WspTwoPortY"/>, with the shape check the
    /// caller's own name in it.</summary>
    private (Axis[] Leading, WspTwoPortY[] Blocks) TwoPortArg(CallExpr cl, Scope scope, int i)
    {
        var cube = CubeArg(cl, scope, i, "a 2-port cube ({…, freq, i, j})");
        var (leading, n, raw) = MatrixCube(cube, cl.Name, "a 2-port");
        if (n != 2)
            throw new ExpressionException(
                $"{cl.Name}: expects a 2-PORT ({{…, freq, i, j}} with i, j in {{1, 2}}); this cube is " +
                $"{n} × {n}. wsp_yparam(wsp, idx) reduces a wsp matrix to one.");
        int blocks = raw.Length / 4;
        var outv = new WspTwoPortY[blocks];
        for (int b = 0; b < blocks; b++)
            outv[b] = new WspTwoPortY(raw[b * 4], raw[b * 4 + 1], raw[b * 4 + 2], raw[b * 4 + 3]);
        return (leading, outv);
    }

    // ══ The functions that are not a plain map over one wsp cube ═════════════

    /// <summary><c>wsp_loopgain(Y, kind [, Z0])</c> — one of the eight loop gains (Eq. 92,
    /// 97, 99–104), from the reduced two-port, which is the document's own calling pattern
    /// (p. 68). <c>Z0</c> defaults to 50 Ω and is used only by the two circulator kinds.</summary>
    private Value EvalWspLoopGain(CallExpr cl, Scope scope)
    {
        Arity(cl, 2, 3);
        var (leading, blocks) = TwoPortArg(cl, scope, 0);

        var kindVal = EvalExpr(cl.Args[1], scope);
        if (kindVal.Kind != ValueKind.String)
            throw new TypeErrorException(
                $"{cl.Name}: argument 2 is the kind, a quoted string — \"BI\", \"UNI\" (or \"FOR\"), " +
                "\"REV\", \"HST\", \"MB\", \"MBR\", \"GFT\", \"GFTR\".");
        WspLoopGainKind kind;
        try { kind = WspNodal.ParseKind(kindVal.AsString()); }
        catch (ArgumentException ex) { throw new ExpressionException(ex.Message); }

        var z0 = cl.Args.Length == 3 ? EvalExpr(cl.Args[2], scope).ToComplex() : new Complex(50.0, 0.0);

        var outv = new Complex[blocks.Length];
        for (int b = 0; b < blocks.Length; b++) outv[b] = WspNodal.LoopGain(blocks[b], kind, z0);
        return LeadingCube(leading, outv, "");
    }

    /// <summary>
    /// <c>wsp_unstable_freq_kurokawa(T)</c> — the frequencies at which the driving-point function
    /// <c>T</c> (<c>H0</c> or <c>Y0</c>; the document says check both, §4.9–4.10) shows Kurokawa's
    /// start-up signature (Eq. 107/108, E.12). Returns a <c>{n}</c> cube of frequencies in Hz,
    /// possibly empty.
    ///
    /// <para>A swept <c>T</c> is refused with the document's own note (E.12: "does not work with
    /// multi-index swept data"), naming <c>at(...)</c> as the way to pin the sweep first — a list
    /// of frequencies has no natural shape once there is more than one sweep point, and inventing
    /// one would put a different number of answers on each row.</para>
    /// </summary>
    private Value EvalWspKurokawa(CallExpr cl, Scope scope)
    {
        Arity(cl, 1, 1);
        var cube = CubeArg(cl, scope, 0, "a driving-point cube ({freq}) — SP1.H0(\"GATE\") or wsp_Y0(...)");
        if (cube.Rank != 1)
            throw new ExpressionException(
                $"{cl.Name}: does not work with multi-index swept data (the reference document's own " +
                $"note, App. E.12). This cube has {cube.Rank} axes " +
                $"({string.Join(", ", cube.Axes.Select(a => a.Name))}); pin every axis but the " +
                "frequency first — at(<cube>, \"<sweep axis>\", <index>) — so the answer is one list " +
                "of frequencies rather than a different number of them per sweep point.");

        var freqs = cube.Axes[0].Values;
        var hits  = WspKurokawa.UnstableFrequencies(cube.ComplexValues, freqs);
        var axis  = new Axis("n", [.. Enumerable.Range(0, hits.Length).Select(i => (double)(i + 1))]);
        return new Value(new DataCube([axis], hits) { Unit = cube.Axes[0].Unit });
    }

    /// <summary><c>encirculations(SP)</c> / <c>enc(SP)</c> — the running clockwise encirclement
    /// count, <c>−unwrap(phase(SP))/360</c> (E.3). One value per sample; the net count is the last
    /// one rounded.</summary>
    private Value EvalEncirclements(CallExpr cl, Scope scope)
    {
        Arity(cl, 1, 1);
        var cube = CubeArg(cl, scope, 0, "a locus cube ({…, freq})");
        if (cube.Rank != 1)
            throw new ExpressionException(
                $"{cl.Name}: needs a single locus ({{freq}}); this cube has {cube.Rank} axes " +
                $"({string.Join(", ", cube.Axes.Select(a => a.Name))}). Pin the other axes with " +
                "at(...) first — an encirclement count is a property of one traversal, and unwrapping " +
                "across a sweep boundary would count a jump between two sweep points as a turn.");
        return new Value(new DataCube([.. cube.Axes], WspKurokawa.Encirclements(cube.ComplexValues)));
    }

    /// <summary><c>_dB(M) = 10·log10|M|</c> (E.2) — a POWER dB, which is why it is not
    /// <c>dB()</c>. Cube-aware like the rest of the dB family.</summary>
    private Value EvalUnderscoreDb(CallExpr cl, Scope scope)
    {
        Arity(cl, 1, 1);
        var v = EvalExpr(cl.Args[0], scope);
        if (v.Kind == ValueKind.Cube) return new Value(v.AsCube().DB10());
        double mag = v.Kind == ValueKind.Real ? Math.Abs(v.AsReal()) : v.AsComplex().Magnitude;
        return new Value(10.0 * Math.Log10(mag + 1e-300));
    }

    /// <summary><c>wsp_impedance(wsp, idx1, idx2 [, stimulus])</c> — the even-mode load-line
    /// impedance at probe <c>idx2</c> under a common stimulus at probe <c>idx1</c> (App. C,
    /// Eq. 196/197). <c>stimulus</c> is <c>"series"</c> (App. C's own code, the default) or
    /// <c>"shunt"</c> (Eq. 37's form).</summary>
    private Value EvalWspImpedance(CallExpr cl, Scope scope)
    {
        Arity(cl, 3, 4);
        var cube = CubeArg(cl, scope, 0, "a wsp matrix cube ({…, freq, row, col})");
        var (leading, n, raw) = MatrixCube(cube, cl.Name, "a wsp matrix");
        int i1 = IdxArg(cl, scope, 1, n / 2);
        int i2 = IdxArg(cl, scope, 2, n / 2);

        var stim = WspStimulus.Series;
        if (cl.Args.Length == 4)
        {
            var sv = EvalExpr(cl.Args[3], scope);
            string s = sv.Kind == ValueKind.String ? sv.AsString() : sv.ToString();
            stim = s.Trim().ToLowerInvariant() switch
            {
                "series" => WspStimulus.Series,
                "shunt"  => WspStimulus.Shunt,
                _ => throw new ExpressionException(
                    $"{cl.Name}: stimulus must be \"series\" (App. C, the default) or \"shunt\" " +
                    $"(Eq. 37), not \"{s}\". Both cancel the common stimulus and both are even-mode " +
                    "load lines."),
            };
        }

        int blocks = raw.Length / (n * n);
        var outv = new Complex[blocks];
        for (int b = 0; b < blocks; b++) outv[b] = WspTransfer.Impedance(BlockAt(raw, b, n), i1, i2, stim);
        return LeadingCube(leading, outv, "Ohm");
    }

    /// <summary><c>wsp_gain(wsp, idxS, idxG, idxD)</c> — the power gain in dB between a gate probe
    /// and a drain probe under a common series stimulus (App. D; T-14: the code, not Eq. 199/201 as
    /// printed).</summary>
    private Value EvalWspGain(CallExpr cl, Scope scope)
    {
        Arity(cl, 4, 4);
        var cube = CubeArg(cl, scope, 0, "a wsp matrix cube ({…, freq, row, col})");
        var (leading, n, raw) = MatrixCube(cube, cl.Name, "a wsp matrix");
        int s = IdxArg(cl, scope, 1, n / 2);
        int g = IdxArg(cl, scope, 2, n / 2);
        int d = IdxArg(cl, scope, 3, n / 2);

        int blocks = raw.Length / (n * n);
        var outv = new double[blocks];
        for (int b = 0; b < blocks; b++) outv[b] = WspTransfer.GainDb(BlockAt(raw, b, n), s, g, d);
        return LeadingCube(leading, outv, "dB");
    }

    /// <summary>
    /// <c>GainDEFs(GamS, SM, GamL)</c> — the four power gains of E.1 (Eq. 205–208), in dB.
    ///
    /// <para>Returns a Real cube with a trailing labelled axis <c>gaindef</c> of length 4 —
    /// <c>GT_dB</c>, <c>GP_dB</c>, <c>GA_dB</c>, <c>Gmax_dB</c>, in that order — because four named
    /// numbers per frequency is a labelled axis in this result model and nothing else. Pick one
    /// with <c>at(GainDEFs(...), "gaindef", 0)</c>.</para>
    /// </summary>
    private Value EvalGainDefs(CallExpr cl, Scope scope)
    {
        Arity(cl, 3, 3);
        var gamS = EvalExpr(cl.Args[0], scope);
        var (leading, blocks) = TwoPortArgAsS(cl, scope, 1);
        var gamL = EvalExpr(cl.Args[2], scope);

        var gs = PerBlockComplex(gamS, blocks.Length, cl.Name, "GamS");
        var gl = PerBlockComplex(gamL, blocks.Length, cl.Name, "GamL");

        var outv = new double[blocks.Length * 4];
        for (int b = 0; b < blocks.Length; b++)
        {
            var s = blocks[b];
            var g = GainDefinitions.Compute(gs[b], s.Y11, s.Y12, s.Y21, s.Y22, gl[b]);
            outv[b * 4 + 0] = g.GtDb;   outv[b * 4 + 1] = g.GpDb;
            outv[b * 4 + 2] = g.GaDb;   outv[b * 4 + 3] = g.GmaxDb;
        }

        var axis = new Axis("gaindef", [0, 1, 2, 3], "", ["GT_dB", "GP_dB", "GA_dB", "Gmax_dB"]);
        return new Value(new DataCube([.. leading, axis], outv) { Unit = "dB" });
    }

    /// <summary>A 2-port cube read as S rather than Y — the same block reader, a different reading
    /// of the four numbers. Kept separate so an error message says "S".</summary>
    private (Axis[] Leading, WspTwoPortY[] Blocks) TwoPortArgAsS(CallExpr cl, Scope scope, int i)
        => TwoPortArg(cl, scope, i);

    /// <summary>One complex value per matrix block, from an argument that is either a scalar (used
    /// for every block) or a cube whose element count matches.</summary>
    private static Complex[] PerBlockComplex(Value v, int blocks, string fn, string what)
    {
        if (v.Kind != ValueKind.Cube)
        {
            var c = v.ToComplex();
            var a = new Complex[blocks];
            Array.Fill(a, c);
            return a;
        }
        var cube = v.AsCube();
        var vals = cube.DataKind == DataKind.Complex
            ? cube.ComplexValues
            : [.. cube.RealValues.Select(x => new Complex(x, 0))];
        if (vals.Length != blocks)
            throw new ExpressionException(
                $"{fn}: {what} has {vals.Length} values but the network has {blocks} — they must be " +
                "over the same sweep. Pin the axes that differ with at(...).");
        return vals;
    }

    /// <summary>One real value per matrix block, same rule.</summary>
    private static double[] PerBlockReal(Value v, int blocks, string fn, string what)
    {
        var c = PerBlockComplex(v, blocks, fn, what);
        var outv = new double[blocks];
        for (int i = 0; i < blocks; i++) outv[i] = c[i].Real;
        return outv;
    }

    /// <summary><c>wsp_zo_renorm_s(ZG, SP, ZL [, freq])</c> — the two-port renormalised to the
    /// probe's own bidirectional impedances (E.10), through the repository's one complex-reference
    /// renormalisation. <c>freq</c> is accepted because the document's signature carries it and is
    /// not used: a power-wave renormalisation to a stated reference has no frequency in it.</summary>
    private Value EvalZoRenorm(CallExpr cl, Scope scope)
    {
        Arity(cl, 3, 4);
        var zgV = EvalExpr(cl.Args[0], scope);
        var (leading, blocks) = TwoPortArgAsS(cl, scope, 1);
        var zlV = EvalExpr(cl.Args[2], scope);

        var zg = PerBlockComplex(zgV, blocks.Length, cl.Name, "ZG");
        var zl = PerBlockComplex(zlV, blocks.Length, cl.Name, "ZL");

        return RenormBlocks(cl, leading, blocks.Length,
            b => WspRenorm.ZoRenormS(zg[b], MatOf(blocks[b]), zl[b]));
    }

    /// <summary><c>wsp_rc_renorm_s(RG, CG, SP, RL, CL [, freq])</c> — the shunt capacitances
    /// absorbed into the network and the result renormalised to the real port resistances
    /// (E.10). Capacitances are FARADS.</summary>
    private Value EvalRcRenorm(CallExpr cl, Scope scope)
    {
        Arity(cl, 5, 6);
        var rgV = EvalExpr(cl.Args[0], scope);
        var cgV = EvalExpr(cl.Args[1], scope);
        var (leading, blocks) = TwoPortArgAsS(cl, scope, 2);
        var rlV = EvalExpr(cl.Args[3], scope);
        var clV = EvalExpr(cl.Args[4], scope);

        int n  = blocks.Length;
        var rg = PerBlockReal(rgV, n, cl.Name, "RG");
        var cg = PerBlockReal(cgV, n, cl.Name, "CG");
        var rl = PerBlockReal(rlV, n, cl.Name, "RL");
        var cc = PerBlockReal(clV, n, cl.Name, "CL");
        var f  = FreqPerBlock(cl, scope, 5, leading, n);

        return RenormBlocks(cl, leading, n,
            b => WspRenorm.RcRenormS(rg[b], cg[b], MatOf(blocks[b]), rl[b], cc[b], f[b]));
    }

    private Value RenormBlocks(CallExpr cl, Axis[] leading, int blocks, Func<int, NumFlat.Mat<Complex>> f)
    {
        var outv = new Complex[blocks * 4];
        for (int b = 0; b < blocks; b++)
        {
            NumFlat.Mat<Complex> m;
            try { m = f(b); }
            catch (ArgumentException ex) { throw new ExpressionException($"{cl.Name}: {ex.Message}"); }
            outv[b * 4 + 0] = m[0, 0]; outv[b * 4 + 1] = m[0, 1];
            outv[b * 4 + 2] = m[1, 0]; outv[b * 4 + 3] = m[1, 1];
        }
        return new Value(new DataCube([.. leading, PortAxis("i"), PortAxis("j")], outv));
    }

    private static NumFlat.Mat<Complex> MatOf(in WspTwoPortY b)
    {
        var m = new NumFlat.Mat<Complex>(2, 2);
        m[0, 0] = b.Y11; m[0, 1] = b.Y12;
        m[1, 0] = b.Y21; m[1, 1] = b.Y22;
        return m;
    }

    // ══ Frequency: stated, or taken from the cube's own axis ═════════════════
    //
    //  "freq may be omitted in a measure line; the cube's own freq axis is used" (§2.8, the
    //  document's indep(R,1) default). Which axis that is has to be found by NAME rather than by
    //  position, because a parametric sweep prepends its own axes.

    private static int FreqAxisIndex(IReadOnlyList<Axis> axes)
    {
        for (int a = 0; a < axes.Count; a++)
            if (axes[a].Name is "freq" or "ssfreq") return a;
        return -1;
    }

    /// <summary>The frequency for each element of a cube laid out on <paramref name="axes"/> — the
    /// stated argument if there is one, else the cube's own <c>freq</c> (or <c>ssfreq</c>) axis
    /// broadcast along its position.</summary>
    private double[] FreqPerBlock(CallExpr cl, Scope scope, int argIndex, Axis[] axes, int blocks)
    {
        if (cl.Args.Length > argIndex)
        {
            var v = EvalExpr(cl.Args[argIndex], scope);
            return PerBlockReal(v, blocks, cl.Name, "freq");
        }

        int fa = FreqAxisIndex(axes);
        if (fa < 0)
            throw new ExpressionException(
                $"{cl.Name}: no frequency was given and this value has no freq axis to take one from " +
                $"(axes: {(axes.Length == 0 ? "none" : string.Join(", ", axes.Select(a => a.Name)))}). " +
                "Pass the frequency in Hz as the last argument.");

        // stride of the freq axis within the leading block layout
        int stride = 1;
        for (int a = fa + 1; a < axes.Length; a++) stride *= axes[a].Length;
        int len = axes[fa].Length;

        var vals = axes[fa].Values;
        var outv = new double[blocks];
        for (int b = 0; b < blocks; b++) outv[b] = vals[(b / stride) % len];
        return outv;
    }

    // ══ The immittance models (E.4, E.11) ════════════════════════════════════

    /// <summary><c>wsp_zsrc(R, C [, freq])</c> and <c>wsp_zprc(R, C [, freq])</c> — two reals and a
    /// frequency, element by element.</summary>
    private Value TwoRealsAndFreq(CallExpr cl, Scope scope, Func<double, double, double, Complex> f, string unit)
    {
        Arity(cl, 2, 3);
        var rV = EvalExpr(cl.Args[0], scope);
        var cV = EvalExpr(cl.Args[1], scope);

        var axes = ShapeOf(rV) ?? ShapeOf(cV) ?? [];
        int n = axes.Length == 0 ? 1 : axes.Aggregate(1, (acc, a) => acc * a.Length);

        var r = PerBlockReal(rV, n, cl.Name, "R");
        var c = PerBlockReal(cV, n, cl.Name, "C");
        var q = FreqPerBlock(cl, scope, 2, axes, n);

        var outv = new Complex[n];
        for (int i = 0; i < n; i++) outv[i] = f(r[i], c[i], q[i]);
        return LeadingCube(axes, outv, unit);
    }

    /// <summary><c>y_to_*</c> / <c>z_to_*</c> — one immittance in, one element value out, element
    /// by element over whatever shape the argument has. <b>Farads and henries</b>, never the
    /// document's picofarads and nanohenries (§2.8).</summary>
    private Value ImmittanceElement(
        CallExpr cl, Scope scope, Func<Complex, double, double> f, bool needsFreq, string unit)
    {
        Arity(cl, 1, needsFreq ? 2 : 1);
        var v    = EvalExpr(cl.Args[0], scope);
        var axes = ShapeOf(v) ?? [];
        int n    = axes.Length == 0 ? 1 : axes.Aggregate(1, (acc, a) => acc * a.Length);

        var vals = PerBlockComplex(v, n, cl.Name, "the immittance");
        var q    = needsFreq ? FreqPerBlock(cl, scope, 1, axes, n) : new double[n];

        var outv = new double[n];
        for (int i = 0; i < n; i++) outv[i] = f(vals[i], q[i]);
        return LeadingCube(axes, outv, unit);
    }

    private static Axis[]? ShapeOf(Value v)
        => v.Kind == ValueKind.Cube ? [.. v.AsCube().Axes] : null;
}
