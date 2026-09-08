// ================================================================
//  WspSource.cs  —  a WSProbe metric of a run, as an ordinary DataCube
//
//  WSP-4. The whole of the value production for a WSProbe trace, and
//  deliberately the whole of it in ONE place: everything downstream —
//  the slice, the family/slider mechanism, the markers, the Table, the
//  export, `.cdd` persistence, the `plot` verb — then handles a probe
//  metric with no change at all, because what it is handed is a cube
//  over the run's own leading axes and nothing else.
//
//  That is also what makes R-wsp4-10 free rather than a feature: a
//  swept `wsp` is {Pin, freq, row, col}, so the metric of it is a cube
//  over {Pin, freq}, and the family marker `~` on Pin draws one curve
//  per sweep point through the mechanism that already exists.
//
//  Every number comes from src/RfCore/Stability/. See WspTrace.cs.
// ================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using RfCore.Data;
using RfCore.Stability;

namespace CircuitRF.Render.DataDisplay;

/// <summary>
/// Which probes a metric is taken at, and the two options some of them read. Value-typed on the
/// trace, round-tripped through <c>.cdd</c> (R-wsp4-11).
/// </summary>
public sealed class WspTraceSpec
{
    /// <summary>The probe the metric is OF — the document's "Label", as it appears in
    /// <c>__WspProbes</c>. Every metric needs this one.</summary>
    public string Probe { get; set; } = "";

    /// <summary>The second probe of a pair (<c>with=</c>), for the <c>wsp_block_calc</c> metrics.
    /// Empty for every other metric.</summary>
    public string With { get; set; } = "";

    /// <summary>The ordered probe SET, for Ohtomo. Empty for every other metric.</summary>
    public List<string> Set { get; set; } = [];

    public WspMetric Metric { get; set; } = WspMetric.None;

    /// <summary>The reference the circulator, pair and Ohtomo metrics normalise by
    /// (<see cref="WspMetrics.UsesZ0"/>). 0 means "the source group's own port-1 Re(Z0), else
    /// 50 Ohm" — resolved at evaluation, so a display authored against one run reads the next run's
    /// reference rather than a number frozen when it was saved.</summary>
    public Complex Z0 { get; set; } = Complex.Zero;

    /// <summary>Which side of every probe Ohtomo treats as the ACTIVE subnetwork (overview D-8:
    /// sides are explicit in every multi-probe function, because the document is not consistent
    /// about them).</summary>
    public WspSide ActiveSide { get; set; } = WspSide.G;

    /// <summary>Which of Ohtomo's <c>G_i</c> to draw, 1-based (Eq. 179). Ignored by every other
    /// metric.</summary>
    public int SetIndex { get; set; } = 1;

    // ── The Envelope sub-card (R-wsp4-9) ─────────────────────────────────────
    //
    //  Three probes, not one: the SOURCE probe whose G-side termination is pulled, the LOAD probe
    //  whose L-side termination is pulled, and `Probe` — the SUSPECT node the pulled circuit is
    //  read at. All three are on the same wsp matrix, because a termination change is a rank-1
    //  update of it (WspEnvelope) rather than a second simulation.

    /// <summary>The probe whose <b>G</b>-side termination the source ladder replaces. Empty leaves
    /// the source side unpulled.</summary>
    public string SourceProbe { get; set; } = "";

    /// <summary>The probe whose <b>L</b>-side termination the load ladder replaces. Empty leaves
    /// the load side unpulled.</summary>
    public string LoadProbe { get; set; } = "";

    /// <summary>The source ladder: one <c>|\u0393S|</c> per rung, each swept over the full turn at
    /// <see cref="ThetaStepDeg"/>. EMPTY is the side's "off" state ([E]'s own ladder is
    /// <c>0.9, 0.875, 0.874</c> — the point of a ladder is to read the \u03c1 at which
    /// encirclements first appear off ONE card).</summary>
    public List<double> GammaSMags { get; set; } = [];

    /// <inheritdoc cref="GammaSMags"/>
    public List<double> GammaLMags { get; set; } = [];

    /// <summary>The angular step of both grids, in degrees.</summary>
    public double ThetaStepDeg { get; set; } = 15.0;

    /// <summary>The passivated run <c>NDFenc</c> is taken against — a cube spec in the SAME source
    /// (a second analysis of the same netlist with its devices passivated). Empty reads the
    /// <c>wsp_passive</c> cube beside this group's own <c>wsp</c>, which is what WSP-6 emits.</summary>
    public string PassiveSource { get; set; } = "";

    public WspTraceSpec Clone() => new()
    {
        Probe        = Probe,
        With         = With,
        Set          = [.. Set],
        Metric       = Metric,
        Z0           = Z0,
        ActiveSide   = ActiveSide,
        SetIndex     = SetIndex,
        SourceProbe  = SourceProbe,
        LoadProbe    = LoadProbe,
        GammaSMags   = [.. GammaSMags],
        GammaLMags   = [.. GammaLMags],
        ThetaStepDeg = ThetaStepDeg,
        PassiveSource = PassiveSource,
    };

    public bool IsActive => Metric != WspMetric.None;
}

/// <summary>
/// The <c>wsp</c> cube of one analysis group, its probe table, and the metric evaluation over them.
/// </summary>
public static partial class WspSource
{
    /// <summary>The metadata cube the S-parameter engine writes beside <c>wsp</c>: one entry per
    /// probe, the axis LABEL its document "Label" and the VALUE its 1-based <c>idx</c>
    /// (overview D-3, so the index is never guessed).</summary>
    public const string ProbesCubeName = "__WspProbes";

    /// <summary>The matrix cube's bare name.</summary>
    public const string WspCubeName = "wsp";

    /// <summary>Every group of <paramref name="ds"/> that carries both a <c>wsp</c> cube and its
    /// probe table — the groups a WSProbe section appears for (R-wsp4-5).</summary>
    public static IEnumerable<string> GroupsWithProbes(DataSet? ds)
    {
        if (ds is null) yield break;
        foreach (var group in ds.Groups)
        {
            var cubes = ds.CubesIn(group);
            if (cubes.ContainsKey(WspCubeName) && cubes.ContainsKey(ProbesCubeName))
                yield return group;
        }
    }

    /// <summary>The qualified name of a group's <c>wsp</c> cube — what a WSProbe trace carries as
    /// its <c>CubeName</c>, so every "is this cube still here" check downstream is the ordinary
    /// one.</summary>
    public static string WspCubeSpec(string group)
        => group == DataSet.DefaultGroup ? WspCubeName : $"{group}.{WspCubeName}";

    /// <summary>The group a qualified <c>…wsp</c> spec names.</summary>
    public static string GroupOf(string cubeSpec)
    {
        int dot = cubeSpec.LastIndexOf('.');
        return dot < 0 ? DataSet.DefaultGroup : cubeSpec[..dot];
    }

    /// <summary>
    /// The probes of a group, in <c>idx</c> order — label and the document's 1-based index. Empty
    /// when the group carries no probe table.
    /// </summary>
    public static IReadOnlyList<(string Label, int Idx)> Probes(DataSet? ds, string group)
    {
        if (ds is null) return [];
        var cubes = ds.CubesIn(group);
        if (!cubes.TryGetValue(ProbesCubeName, out var table)) return [];
        if (table.Rank != 1) return [];

        var labels = table.Axes[0].Labels;
        var idx    = table.DataKind == DataKind.Real
            ? table.RealValues
            : [.. table.ComplexValues.Select(c => c.Real)];

        var list = new List<(string, int)>(idx.Length);
        for (int k = 0; k < idx.Length; k++)
        {
            string label = labels is not null && k < labels.Length && labels[k].Length > 0
                ? labels[k]
                : $"#{k + 1}";
            list.Add((label, (int)Math.Round(idx[k])));
        }
        list.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return list;
    }

    /// <summary>
    /// The axes a metric cube of this group WILL have — the <c>wsp</c> cube's leading axes, which is
    /// everything but its two matrix axes. Used to build a trace's default slice without evaluating
    /// anything, so the picker costs nothing to open.
    /// </summary>
    public static Axis[]? LeadingAxes(DataSet? ds, string cubeSpec)
    {
        if (ds is null || !ds.Contains(cubeSpec)) return null;
        var cube = ds[cubeSpec];
        return cube.Rank < 3 ? null : [.. cube.Axes.Take(cube.Rank - 2)];
    }

    /// <summary>
    /// The metric named by <paramref name="spec"/>, as a cube over the run's leading axes
    /// (<c>{freq}</c>, or <c>{Pin, freq}</c> under a parametric sweep). False with a sentence when
    /// the probe is not in this run, the shape is not a <c>wsp</c> matrix, or the metric needs a
    /// second probe or a set that is not there.
    /// </summary>
    public static bool TryEvaluate(
        DataSet? ds, string? cubeSpec, WspTraceSpec spec,
        out DataCube? result, out string error)
    {
        result = null; error = "";

        if (ds is null || cubeSpec is null || !ds.Contains(cubeSpec))
        { error = "This source carries no wsp matrix — place a WSProbe and re-run."; return false; }
        if (!spec.IsActive) { error = "No WSProbe metric is selected."; return false; }

        var cube = ds[cubeSpec];
        if (cube.Rank < 3 || cube.Axes[^1].Length != cube.Axes[^2].Length)
        { error = $"'{cubeSpec}' is not a wsp matrix ({{…, freq, row, col}}, square)."; return false; }

        // An envelope metric is not a metric of ONE probe over the run's own axes: it is a metric of
        // the RE-TERMINATED circuit over a Γ grid, and it therefore has grid axes of its own. It gets
        // its own evaluation for that reason and for no other — the samples still come from
        // src/RfCore/Stability/ and from nowhere else.
        if (WspMetrics.NeedsEnvelope(spec.Metric))
            return TryEvaluateEnvelope(ds, cubeSpec, spec, cube, out result, out error);

        int size = cube.Axes[^1].Length;
        int nProbes = size / 2;
        var leading = cube.Axes.Take(cube.Rank - 2).ToArray();
        var raw = cube.ComplexValues;
        int blocks = raw.Length / (size * size);

        string group = GroupOf(cubeSpec);
        var probes = Probes(ds, group);

        if (!TryIdx(probes, nProbes, spec.Probe, out int idx, out error)) return false;

        Complex z0 = spec.Z0 == Complex.Zero ? DefaultZ0(ds, group) : spec.Z0;

        int idx2 = 0;
        int[]? set = null;
        var info = WspMetrics.Info(spec.Metric);
        if (info is null) { error = "No WSProbe metric is selected."; return false; }

        if (info.Value.Group == WspMetricGroup.Pair)
        {
            if (!TryIdx(probes, nProbes, spec.With, out idx2, out error))
            { error = $"{info.Value.Name} is a probe-PAIR quantity: choose a second probe. {error}"; return false; }
            if (idx2 == idx)
            { error = $"{info.Value.Name} needs TWO different probes; '{spec.Probe}' is named twice."; return false; }
        }
        else if (info.Value.Group == WspMetricGroup.ProbeSet)
        {
            var members = new List<int>();
            foreach (string label in spec.Set)
            {
                if (!TryIdx(probes, nProbes, label, out int m, out error)) return false;
                if (!members.Contains(m)) members.Add(m);
            }
            if (members.Count == 0)
            { error = "Ohtomo's global loop gain is taken over a probe SET: choose at least one probe."; return false; }
            set = [.. members];
        }

        // ── the samples, one library call per leading-axis point ─────────────
        var cz = info.Value.IsReal ? null : new Complex[blocks];
        var rv = info.Value.IsReal ? new double[blocks] : null;

        for (int b = 0; b < blocks; b++)
        {
            var w = BlockAt(raw, b, size);
            if (info.Value.IsReal) rv![b] = Real(w, idx, spec.Metric);
            else                   cz![b] = Value(w, idx, idx2, set, z0, spec, spec.Metric);
        }

        result = cz is not null
            ? new DataCube(leading, cz) { Unit = info.Value.Unit }
            : new DataCube(leading, rv!) { Unit = info.Value.Unit };
        return true;
    }

    /// <summary>
    /// The reduced two-port <c>[Y]</c> at a probe (Eq. 44) as a cube over
    /// <c>{…leading…, i, j}</c> — the shape every network-parameter consumer already reads
    /// (R-wsp4-8). Converted to S by the ordinary <c>NetworkMetrics</c> path, so µ, µ', K, |Δ|,
    /// MAG/MSG and the stability circles apply to it unchanged.
    /// </summary>
    public static bool TryReducedTwoPort(
        DataSet? ds, string? cubeSpec, string probeLabel,
        out DataCube? result, out string error)
    {
        result = null; error = "";
        if (ds is null || cubeSpec is null || !ds.Contains(cubeSpec))
        { error = "This source carries no wsp matrix — place a WSProbe and re-run."; return false; }

        var cube = ds[cubeSpec];
        if (cube.Rank < 3 || cube.Axes[^1].Length != cube.Axes[^2].Length)
        { error = $"'{cubeSpec}' is not a wsp matrix ({{…, freq, row, col}}, square)."; return false; }

        int size = cube.Axes[^1].Length;
        var leading = cube.Axes.Take(cube.Rank - 2).ToArray();
        var raw = cube.ComplexValues;
        int blocks = raw.Length / (size * size);

        var probes = Probes(ds, GroupOf(cubeSpec));
        if (!TryIdx(probes, size / 2, probeLabel, out int idx, out error)) return false;

        var vals = new Complex[blocks * 4];
        for (int b = 0; b < blocks; b++)
        {
            var y = WspReduction.YParam(WspProbeQuad.Of(BlockAt(raw, b, size), idx));
            vals[b * 4 + 0] = y.Y11; vals[b * 4 + 1] = y.Y12;
            vals[b * 4 + 2] = y.Y21; vals[b * 4 + 3] = y.Y22;
        }

        var axes = new Axis[leading.Length + 2];
        Array.Copy(leading, axes, leading.Length);
        axes[^2] = new Axis("i", [1.0, 2.0]);
        axes[^1] = new Axis("j", [1.0, 2.0]);
        result = new DataCube(axes, vals) { Unit = "S" };
        return true;
    }

    /// <summary>
    /// The scattering matrix of a two-port admittance cube at <paramref name="z0"/>, block by
    /// block — <c>S = (I - Z0*Y)(I + Z0*Y)^-1</c> through <see cref="WspMatrix.ScatteringOfY"/>,
    /// which is the same conversion WSP-3's probe-pair blocks are scattered by (Eq. 142/143). One
    /// implementation, so a circle read off the reduced two-port and a pair block's own
    /// S-parameters agree by construction.
    /// </summary>
    public static DataCube ScatteringOf(DataCube yCube, Complex z0)
    {
        var raw   = yCube.ComplexValues;
        // The port count is READ from the cube rather than assumed to be 2: the same conversion
        // serves the reduced two-port and wsp_ymatrix's N x N reduction over a probe set (Eq. 185),
        // and a hard-coded 2 would have silently produced the first four entries of an N-port.
        int n     = yCube.Axes[^1].Length;
        int nMats = raw.Length / (n * n);
        var outv  = new Complex[raw.Length];
        for (int k = 0; k < nMats; k++)
        {
            var y = new Complex[n, n];
            for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                y[r, c] = raw[k * n * n + r * n + c];
            var s = WspMatrix.ScatteringOfY(y, z0);
            for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                outv[k * n * n + r * n + c] = s[r, c];
        }
        return new DataCube([.. yCube.Axes], outv);
    }

    // ── internals ────────────────────────────────────────────────────────────

    private static bool TryIdx(
        IReadOnlyList<(string Label, int Idx)> probes, int nProbes, string label,
        out int idx, out string error)
    {
        idx = 0; error = "";
        if (string.IsNullOrWhiteSpace(label))
        { error = "No probe is selected."; return false; }

        foreach (var (l, i) in probes)
            if (string.Equals(l, label, StringComparison.Ordinal)) { idx = i; return true; }
        // A case-insensitive second pass, for a label typed on a command line.
        foreach (var (l, i) in probes)
            if (string.Equals(l, label, StringComparison.OrdinalIgnoreCase)) { idx = i; return true; }

        error = probes.Count == 0
            ? $"This run has no probe named '{label}', and no probe table at all."
            : $"This run has no probe named '{label}'. It has: {string.Join(", ", probes.Select(p => p.Label))}.";
        _ = nProbes;
        return false;
    }

    /// <summary>The group's own port-1 reference, else 50 Ohm — R-wsp4-5's rule for the Z0 field's
    /// default. Real, because every quantity that reads it normalises by a real reference.</summary>
    private static Complex DefaultZ0(DataSet ds, string group)
    {
        var cubes = ds.CubesIn(group);
        if (cubes.TryGetValue(NetworkMetrics.Z0CubeName, out var z0) && z0.BufferLength > 0)
        {
            var v = z0.DataKind == DataKind.Complex ? z0.ComplexValues[0] : new Complex(z0.RealValues[0], 0);
            if (v.Real > 0) return new Complex(v.Real, 0);
        }
        return new Complex(50, 0);
    }

    private static Complex[,] BlockAt(Complex[] raw, int block, int n)
    {
        var m = new Complex[n, n];
        int b = block * n * n;
        for (int r = 0; r < n; r++)
        for (int c = 0; c < n; c++)
            m[r, c] = raw[b + r * n + c];
        return m;
    }

    private static double Real(Complex[,] w, int idx, WspMetric metric)
    {
        var m = WspMargin.Of(WspProbeQuad.Of(w, idx));
        return metric switch
        {
            WspMetric.SM_Y0 => m.SmY0,
            WspMetric.SM_H0 => m.SmH0,
            WspMetric.SM    => m.Sm,
            WspMetric.rY    => m.rY,
            WspMetric.iY    => m.iY,
            WspMetric.rH    => m.rH,
            WspMetric.iH    => m.iH,
            _ => double.NaN,
        };
    }

    private static Complex Value(
        Complex[,] w, int idx, int idx2, int[]? set, Complex z0, WspTraceSpec spec, WspMetric metric)
    {
        // The probe-pair family is the whole of wsp_block_calc's second half, indexed exactly as the
        // document indexes it (Eq. 144–151) — {9..16}, 1-based, so {13} is LGa.
        if (WspMetrics.NeedsPair(metric))
        {
            var all = WspPair.BlockCalc(w, idx, idx2, z0);
            int one = metric switch
            {
                WspMetric.F_LGa => 9,  WspMetric.F_LGf => 10, WspMetric.F_LGH => 11, WspMetric.F_LGM => 12,
                WspMetric.LGa   => 13, WspMetric.LGf   => 14, WspMetric.LGH   => 15, WspMetric.LGM   => 16,
                _ => 0,
            };
            return all[one - 1];
        }

        if (metric == WspMetric.OhtomoG)
        {
            var g = WspOhtomo.LoopGains(w, set, spec.ActiveSide, z0);
            int k = Math.Clamp(spec.SetIndex, 1, g.Length) - 1;
            return g[k];
        }

        var q = WspProbeQuad.Of(w, idx);
        var y = WspReduction.YParam(q);

        return metric switch
        {
            WspMetric.H0    => WspNodal.H0(q),
            WspMetric.Y0    => WspNodal.Y0(q),
            WspMetric.InvH0 => Complex.One / WspNodal.H0(q),
            WspMetric.InvY0 => Complex.One / WspNodal.Y0(q),

            WspMetric.ZG  => WspNodal.ZG(q),
            WspMetric.ZL  => WspNodal.ZL(q),
            WspMetric.YG  => WspNodal.YG(y),
            WspMetric.YL  => WspNodal.YL(y),
            WspMetric.Zop => WspNodal.Zop(y),
            WspMetric.Yop => WspNodal.Yop(WspReduction.ZParam(q)),

            WspMetric.LG     => WspNodal.LoopGain(y, WspLoopGainKind.Bi,   z0),
            WspMetric.F      => WspReduction.ReturnDifference(WspNodal.LoopGain(y, WspLoopGainKind.Bi, z0)),
            WspMetric.LGF    => WspNodal.LoopGain(y, WspLoopGainKind.Uni,  z0),
            WspMetric.LGR    => WspNodal.LoopGain(y, WspLoopGainKind.Rev,  z0),
            WspMetric.LG_H   => WspNodal.LoopGain(y, WspLoopGainKind.Hst,  z0),
            WspMetric.LG_MF  => WspNodal.LoopGain(y, WspLoopGainKind.Mb,   z0),
            WspMetric.LG_MR  => WspNodal.LoopGain(y, WspLoopGainKind.Mbr,  z0),
            WspMetric.LG_MGF => WspNodal.LoopGain(y, WspLoopGainKind.Gft,  z0),
            WspMetric.LG_MGR => WspNodal.LoopGain(y, WspLoopGainKind.Gftr, z0),

            WspMetric.NodalGamma => WspNodal.NodalGamma(q),

            _ => new Complex(double.NaN, double.NaN),
        };
    }
}
