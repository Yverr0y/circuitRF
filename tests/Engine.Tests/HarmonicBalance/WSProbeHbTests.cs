// ================================================================
//  WSProbeHbTests.cs — brief-wsprobe-5's gates (R-wsp5-9 (a)–(i)).
//
//  WSP-5 adds ONE engine capability: the conversion-matrix small-signal solve around a converged
//  harmonic-balance operating point, and the WSProbe's `wsp` computed with it over a swept probe
//  (tickle) frequency `ssfreq`. Every derived metric of WSP-2/3/9 then applies to that cube
//  unchanged, which is why nothing here re-tests them.
//
//  The two gates that carry the weight are (b) and (d), and they are independent of each other:
//    (b) folds J_ss at ω_ss = 0 onto HbNewton.BuildJ's own real-split matrix — the engine's Jacobian
//        IS this analysis at zero probe frequency, so a two-sided coefficient, a jω_k factor or a
//        Y_NN placement that is wrong shows up as a mismatch in one comparison.
//    (d) drives the SAME operating point with a −90 dBm second tone at the probe's terminals and
//        reads circuitRF's own two-tone HB response at the (0,1) mixing product. That path shares no
//        linearisation with the conversion matrix at all, so it is the independent oracle.
// ================================================================

using System.Numerics;
using CircuitRF.Core.Design;
using CircuitRF.Core.Elaboration;
using CircuitRF.Core.Netlist;
using CircuitRF.Engine.HarmonicBalance;
using RfCore.Data;
using RfCore.Stability;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.HarmonicBalance;

public sealed class WSProbeHbTests(ITestOutputHelper output)
{
    // ── Fixtures ────────────────────────────────────────────────────────────

    private static string TestData(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var cand = Path.Combine(dir, "testdata");
            if (Directory.Exists(cand)) return Path.Combine([cand, .. parts]);
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("testdata not found above " + AppContext.BaseDirectory);
    }

    private static string Fixture(string name) => TestData("wsprobe", name);

    /// <summary>
    /// A fixture with zero or more <c>key = value</c> global lines rewritten and zero or more extra
    /// keys appended to its <c>analysis</c> line, written to a temp file and read back through the
    /// ordinary reader — so every test drives the same code path a `.cnl` on disk does.
    /// </summary>
    private static (Library Lib, TestBench Tb, ElaboratedNetlist Nl) Load(
        string name,
        (string Var, string Value)[]? globals = null,
        string analysisExtra = "",
        string[]? extraLines = null)
    {
        var lines = File.ReadAllLines(Fixture(name)).ToList();
        foreach (var (v, val) in globals ?? [])
        {
            int at = lines.FindIndex(l => l.TrimStart().StartsWith(v + " ", StringComparison.Ordinal)
                                       || l.TrimStart().StartsWith(v + "=", StringComparison.Ordinal));
            Assert.True(at >= 0, $"fixture {name} has no global '{v}' to override");
            lines[at] = $"{v} = {val}";
        }
        if (analysisExtra.Length > 0)
        {
            int at = lines.FindIndex(l => l.TrimStart().StartsWith("analysis ", StringComparison.Ordinal));
            Assert.True(at >= 0, $"fixture {name} has no analysis line");
            lines[at] = lines[at] + " " + analysisExtra;
        }
        if (extraLines is not null)
        {
            int at = lines.FindIndex(l => l.TrimStart().StartsWith("analysis ", StringComparison.Ordinal));
            lines.InsertRange(at < 0 ? lines.Count : at, extraLines);
        }

        var tmp = Path.Combine(Path.GetTempPath(), $"wsp5_{Guid.NewGuid():N}.cnl");
        File.WriteAllLines(tmp, lines);
        try
        {
            var (lib, tb) = CnlReader.ReadFile(tmp);
            return (lib, tb, new Elaborator(lib).Elaborate(tb));
        }
        finally { File.Delete(tmp); }
    }

    private static (DataSet Ds, HbEngine Engine, HbAnalysisParams P, ElaboratedNetlist Nl) RunHb(
        string name,
        (string Var, string Value)[]? globals = null,
        string analysisExtra = "",
        string[]? extraLines = null,
        AnalysisSettings? settings = null,
        int? replaceMaxHarm = null)
    {
        var (lib, tb, nl) = Load(name, globals,
            replaceMaxHarm is { } mh ? $"MaxHarm={mh} " + analysisExtra : analysisExtra,
            extraLines);
        var hba = tb.Analyses.OfType<HarmonicBalanceAnalysis>().Single();
        var p   = HbEngine.Resolve(hba, nl.ResolvedGlobals, nl.GlobalsWithExplicitUnit);
        var eng = new HbEngine(nl, tb, settings ?? new AnalysisSettings());
        var res = eng.Run(p);
        return (res.DataSet, eng, p, nl);
    }

    /// <summary>The document's <c>wsp(r, c)</c> at probe-frequency index <paramref name="fi"/> —
    /// r, c 1-based, exactly as the document writes them.</summary>
    private static Complex Wsp(DataSet ds, int fi, int r, int c) => (Complex)ds["wsp"][fi, r - 1, c - 1];

    private const string SsSweep = "SSStart=0.35 SSStop=3.15 SSNpts=8 SSUnit=GHz";

    // ══ (a) — untouched without SS* ══════════════════════════════════════════

    /// <summary>
    /// R-wsp5-9(a). The same probed netlist with and without <c>SS*</c> keys: <c>V</c>, <c>I</c> and
    /// <c>INl</c> byte-identical. The small-signal solve happens strictly AFTER the Newton solve and
    /// must not perturb it — not through a re-extraction, not through a cached factorisation it
    /// invalidated, not through a source it left stamped at a different value.
    ///
    /// <para><b>Read as "the same netlist", not "probed vs unprobed"</b>: a WSProbe SPLITS a node, so
    /// a probed netlist genuinely has more nodes than an unprobed one and its <c>V</c> cube has more
    /// rows. What the probe must not change is the VOLTAGE — checked separately in
    /// <see cref="ProbeIsTransparentToTheNonlinearSolve"/>, which is R-wsp1-1's transparency claim
    /// restated for harmonic balance.</para>
    /// </summary>
    [Fact]
    public void WithoutSsKeys_TheNonlinearSolveIsByteIdentical()
    {
        var bare = RunHb("hb_pumped_two_port.cnl");
        var swept = RunHb("hb_pumped_two_port.cnl", analysisExtra: SsSweep);

        Assert.False(bare.Ds.Contains("wsp"));
        Assert.True(swept.Ds.Contains("wsp"));

        foreach (var cube in new[] { "V", "I", "INl" })
            AssertCubesIdentical(bare.Ds[cube], swept.Ds[cube], cube);

        static void AssertCubesIdentical(DataCube a, DataCube b, string what)
        {
            Assert.Equal(a.Axes.Count, b.Axes.Count);
            for (int d = 0; d < a.Axes.Count; d++)
                Assert.Equal(a.Axes[d].Values.Length, b.Axes[d].Values.Length);
            var av = a.ComplexValues; var bv = b.ComplexValues;
            Assert.Equal(av.Length, bv.Length);
            for (int i = 0; i < av.Length; i++)
            {
                Assert.True(av[i].Real == bv[i].Real && av[i].Imaginary == bv[i].Imaginary,
                    $"{what}[{i}] differs: {av[i]} vs {bv[i]} — the small-signal solve perturbed " +
                    "the nonlinear solve.");
            }
        }
    }

    /// <summary>
    /// The probe is a 0 V short (R-wsp1-1): every node it splits carries the SAME spectrum on both
    /// sides, and the nodes the unprobed netlist shares with it carry the same spectrum as well.
    /// </summary>
    [Fact]
    public void ProbeIsTransparentToTheNonlinearSolve()
    {
        var probed   = RunHb("hb_pumped_two_port.cnl");
        var unprobed = RunHb("hb_pumped_two_port_unprobed.cnl");

        // The GATE probe splits the inductor's far end from device port 1; the DRAIN probe splits
        // device port 2 from the output network. Both pairs must be one node electrically.
        // The floor is ABSOLUTE and set by the largest harmonic of the node: one of each pair is the
        // Newton solution at an interface node and the other comes out of the linear back-solve — a
        // separate triangular solve on the same factorisation — so a 4th harmonic three decades
        // below the fundamental is resolved to the fundamental's precision, not to its own.
        foreach (var (a, b) in new[] { ("n_gl", "n_p1"), ("n_p2", "n_dl") })
        {
            double scale = Peak(probed.Ds, a);
            for (int k = 0; k <= 4; k++)
            {
                var va = NodeSpec(probed.Ds, a, k);
                var vb = NodeSpec(probed.Ds, b, k);
                Assert.True((va - vb).Magnitude <= 1e-9 * scale,
                    $"probe short violated at {a}/{b}, harmonic {k}: {va} vs {vb} " +
                    $"(peak {scale:E3})");
            }
        }

        // And the nodes both netlists name carry the same spectrum.
        foreach (var node in new[] { "n_p1", "n_p2", "n_s", "n_gg", "n_dr", "n_out" })
        {
            double scale = Peak(unprobed.Ds, node);
            for (int k = 0; k <= 4; k++)
            {
                var vp = NodeSpec(probed.Ds, node, k);
                var vu = NodeSpec(unprobed.Ds, node, k);
                Assert.True((vp - vu).Magnitude <= 1e-8 * scale,
                    $"{node} harmonic {k}: probed {vp} vs unprobed {vu} (peak {scale:E3})");
            }
        }

        static double Peak(DataSet ds, string node)
            => Enumerable.Range(0, 5).Max(k => NodeSpec(ds, node, k).Magnitude);
    }

    private static Complex NodeSpec(DataSet ds, string node, int harmonic)
    {
        var cube = ds["V"];
        int axis = cube.Axes.ToList().FindIndex(a => a.Name == "node");
        var labels = cube.Axes[axis].Labels!;
        int row = Array.IndexOf(labels, node);
        Assert.True(row >= 0, $"no node '{node}' in the V cube");
        return (Complex)cube[row, harmonic];
    }

    // ══ (b) — the HB Jacobian is the ω_ss = 0 case ═══════════════════════════

    /// <summary>
    /// R-wsp5-9(b), and the load-bearing gate of this brief. At the converged operating point build
    /// <c>J_ss</c> at <c>ω_ss = 0</c> with <c>K_ss = K</c>, fold its <c>±k</c> blocks back onto the
    /// one-sided real-split form, and assert it equals <see cref="HbNewton.BuildJ"/>'s matrix.
    ///
    /// <para>This pins, in ONE comparison: R-wsp5-2's two-sided coefficient rule (a missing factor
    /// of two on every AC bin, or a missing conjugate on the negative differences), the
    /// <c>j·ω_k</c> factor on the charge block (a sign, or <c>ω_ss</c> in place of <c>ω_k</c>),
    /// and <c>Y_NN</c>'s placement on the sideband diagonal. Any one of them wrong is invisible at
    /// low drive and produces a plausible wrong answer at high drive, which is exactly the class of
    /// bug this repository's conventions sections exist to prevent.</para>
    /// </summary>
    [Fact]
    public void ConversionMatrixAtZeroProbeFrequency_IsTheHbJacobian()
    {
        var (_, eng, p, _) = RunHb("hb_pumped_two_port.cnl");
        var lp = eng.LastLinearizedPoint;
        Assert.NotNull(lp);

        int n = lp.InterfaceCount, k = lp.MaxHarmonic;

        // The engine's own Jacobian at that iterate.
        var expected = HbNewton.BuildJ(lp.YNN, lp.G, lp.C, n, k, lp.Omega0,
                                       higherBuckets: lp.Buckets);

        // The conversion matrix at ω_ss = 0, folded back.
        var sidebands = new HbSmallSignal.ToneSidebands(lp.G, lp.C, lp.Buckets, k, lp.Omega0);
        var jss = HbSmallSignal.BuildJss(sidebands, n, 0.0, omega =>
            Math.Abs(omega) < 1e-12
                ? lp.YNN[0]
                : Conj(lp.YNN[(int)Math.Round(Math.Abs(omega) / lp.Omega0)], omega < 0));
        var folded = HbSmallSignal.FoldToRealSplit(jss, n, k);

        int dof = 2 * n * (k + 1);
        Assert.Equal(dof * dof, folded.Length);
        Assert.Equal(dof * dof, expected.Length);

        double scale = expected.Max(Math.Abs);
        double worst = 0; int worstAt = -1;
        for (int i = 0; i < expected.Length; i++)
        {
            double e = Math.Abs(folded[i] - expected[i]) / Math.Max(scale * 1e-12, Math.Abs(expected[i]));
            if (Math.Abs(folded[i] - expected[i]) <= 1e-12 * scale) continue;
            if (e > worst) { worst = e; worstAt = i; }
        }
        output.WriteLine($"dof = {dof}, |J|_max = {scale:E3}, worst relative deviation = {worst:E3}" +
                         (worstAt < 0 ? "" : $" at [{worstAt / dof},{worstAt % dof}]"));
        Assert.True(worst <= 1e-12, worstAt < 0 ? "unreachable" :
            $"the folded conversion matrix differs from BuildJ by {worst:E3} relative at " +
            $"[{worstAt / dof},{worstAt % dof}] (folded {folded[worstAt]:E17} vs " +
            $"BuildJ {expected[worstAt]:E17}).");

        static Complex[,] Conj(Complex[,] y, bool doIt)
        {
            if (!doIt) return y;
            int a = y.GetLength(0), b = y.GetLength(1);
            var c = new Complex[a, b];
            for (int i = 0; i < a; i++) for (int j = 0; j < b; j++) c[i, j] = Complex.Conjugate(y[i, j]);
            return c;
        }
    }

    // ══ (c) — low drive tends to the linear wsp ══════════════════════════════

    /// <summary>
    /// R-wsp5-9(c). At a vanishing drive the periodic linearisation IS the DC-linearised one, so the
    /// HB <c>wsp(ssfreq)</c> must equal the S-parameter <c>wsp(freq)</c> of the same netlist — the
    /// two analyses answering the same question at the two ends of the drive sweep — and the
    /// margins with it. Then the SAME comparison at a real drive must FAIL, because an analysis that
    /// agreed with the linear one everywhere would not be seeing the drive at all.
    /// </summary>
    [Fact]
    public void LowDrive_MatchesTheSParameterWsp_AndHighDriveDoesNot()
    {
        // The S-parameter run of the same netlist: port-less but probed (R-wsp1-6), the nonlinear
        // device linearised at its DC operating point by the engine's own DC pre-pass.
        var (lib, tb, nlSp) = Load("hb_pumped_two_port.cnl",
            extraLines: ["analysis SP1 type=sparam start=0.35 stop=3.15 npts=8 Unit=GHz"]);
        var sp     = tb.Analyses.OfType<SParameterAnalysis>().Single();
        var spFreq = sp.Expand(nlSp.ResolvedGlobals, nlSp.GlobalsWithExplicitUnit);
        var dsSp   = SParameterEngine.Run(nlSp, spFreq);
        _ = lib;

        var low = RunHb("hb_pumped_two_port.cnl",
            globals: [("Vpump", "1e-6")], analysisExtra: SsSweep);
        var ssFreq = low.Ds["wsp"].Axes[0].Values;
        Assert.Equal(spFreq.Length, ssFreq.Length);
        for (int i = 0; i < spFreq.Length; i++) Assert.Equal(spFreq[i], ssFreq[i], 1e-6);

        double worst = 0;
        for (int fi = 0; fi < spFreq.Length; fi++)
        for (int r = 1; r <= 4; r++)
        for (int c = 1; c <= 4; c++)
        {
            var a = Wsp(dsSp, fi, r, c);
            var b = Wsp(low.Ds, fi, r, c);
            double rel = (a - b).Magnitude / Math.Max(1e-30, a.Magnitude);
            worst = Math.Max(worst, rel);
        }
        output.WriteLine($"low drive: worst relative wsp deviation from the S-parameter run = {worst:E3}");
        Assert.True(worst <= 1e-6, $"low-drive wsp differs from the linear wsp by {worst:E3} relative.");

        foreach (var probe in new[] { "GATE", "DRAIN" })
        foreach (var margin in new[] { "SM_Y0", "SM_H0" })
        {
            var a = dsSp[$"{margin}:{probe}"].RealValues;
            var b = low.Ds[$"{margin}:{probe}"].RealValues;
            for (int i = 0; i < a.Length; i++)
                Assert.True(Math.Abs(a[i] - b[i]) <= 1e-6,
                    $"{margin}:{probe}[{i}] — linear {a[i]:F9} vs low-drive HB {b[i]:F9}");
        }

        // ── And the analysis must SEE the drive ───────────────────────────────
        var driven = RunHb("hb_pumped_two_port.cnl", analysisExtra: SsSweep);
        double biggest = 0;
        for (int fi = 0; fi < spFreq.Length; fi++)
        for (int r = 1; r <= 4; r++)
        for (int c = 1; c <= 4; c++)
        {
            var a = Wsp(dsSp, fi, r, c);
            var b = Wsp(driven.Ds, fi, r, c);
            biggest = Math.Max(biggest, (a - b).Magnitude / Math.Max(1e-30, a.Magnitude));
        }
        output.WriteLine($"at the fixture's own drive: largest relative wsp deviation = {biggest:E3}");
        Assert.True(biggest > 1e-2,
            $"the driven wsp is within {biggest:E3} of the DC-linearised one — the small-signal " +
            "solve is not seeing the operating point.");
    }

    // ══ (d) — the independent oracle: two-tone HB ════════════════════════════

    /// <summary>
    /// R-wsp5-9(d), and the gate that proves the small-signal solve is the linearisation of the
    /// engine's own nonlinear solution through a code path that shares no linearisation with it.
    ///
    /// <para>The same operating point is driven with a second tone at <c>f2</c>, small enough
    /// (−90 dBm-scale) that its response is linear, injected exactly where the probe injects: a
    /// <c>V_1Tone</c> in series on the probe's G side is the series injection, an <c>I_1Tone</c> to
    /// ground at the probe's G node is the shunt one. circuitRF's two-tone HB then reports the
    /// response at the <c>(0, 1)</c> mixing product — which IS <c>f2</c> — at every probe, and
    /// dividing by the injected amplitude must reproduce the conversion-matrix <c>wsp</c> entries at
    /// <c>ω_ss = 2π·f2</c>.</para>
    ///
    /// <para><b>Where the series source goes is a derivation, not a choice, and only one placement
    /// works.</b> The probe's own branch source sits BETWEEN its two terminals, so <c>vP = v_G</c> is
    /// set by the generator branch alone: <c>iS = vS/(Z_G + Z_L)</c> and
    /// <c>vP/vS = −Z_G/(Z_G + Z_L)</c>. An external source on the probe's G side reproduces
    /// <c>iS</c> but gives <c>vP/vS = +Z_L/(Z_G + Z_L)</c> — a different number, because the
    /// measurement node then sits on the far side of the inserted EMF. Only a source on the
    /// <b>L side</b>, oriented with its FIRST net facing the load
    /// (<c>V(load) − V(nL) = A</c>), reproduces both: that is what
    /// <see cref="RunTwoToneOracle"/> splices in, and reversing its two nets negates every entry of
    /// the series rows. The shunt injection has no such freedom — <c>I_1Tone</c> into the G node is
    /// literally the probe's own <c>AddCurrentInjection(nG, +1)</c>.</para>
    ///
    /// <para><b>The tolerance is the ORACLE's accuracy, measured, and it is bounded from both
    /// sides.</b> The two-tone run resolves the tickle's response only down to an absolute floor of
    /// ≈7e-10 A on a mixing-product current (the APFT's least-squares conditioning against a
    /// ‖F‖ = 1e-14 residual), so a SMALLER tickle is worse; and the tickle must stay small against
    /// the pump, so a LARGER one is worse too. Measured over five decades, the series (voltage)
    /// injection is best near 3 mV — 2% of the 0.17 V pump at the probe — and the shunt (current)
    /// injection near 0.1 mA — 1% of the 10 mA pump current — each bottoming out at ≈1e-5 relative;
    /// the two optima differ by a factor of fifty precisely because the probe's <c>iS/iP</c> is
    /// fifty times its <c>iS/vS</c> here. <c>HbApftOversample = 3</c> halves the floor. 5e-5 is
    /// therefore a real bound on the comparison and not a slackened one.</para>
    /// </summary>
    [Theory]
    [InlineData("GATE",  1, "n_gl")]
    [InlineData("DRAIN", 2, "n_p2")]
    public void TwoToneResponse_ReproducesTheConversionMatrixWsp(string probe, int idx, string gNode)
    {
        const double f2      = 2.3e9;   // off every harmonic of f0 = 2 GHz, and off f0/2
        const int    order   = 8;       // both runs: harmonics and mixing products to 8
        const int    kSs     = 6;       // sidebands (k, 1) retained by MaxMixOrder = 8
        const double seriesV = 3e-3;    // volts, vs a ~0.17 V pump at the probe
        const double shuntA  = 1e-4;    // amperes, vs a ~10 mA pump current at the probe
        const double tol     = 5e-5;    // the ORACLE's own accuracy — see the comment below

        // ── The reference: the conversion-matrix wsp at ω_ss = 2π·f2 ──────────
        var single = RunHb("hb_pumped_two_port.cnl",
            analysisExtra: $"SSStart={f2} SSStop={f2} SSNpts=1 SSMaxHarm={kSs}",
            replaceMaxHarm: order);
        Assert.True(single.Ds.Contains("wsp"), "the single-tone run produced no wsp");

        // ── The oracle: the same operating point with a small second tone ─────
        var oracleSettings = new AnalysisSettings { HbApftOversample = 3.0 };
        var series = RunTwoToneOracle(probe, f2,
            [$"V_1Tone:Vinj  n_inj {LNodeOf(probe)}  Freq={f2}  V={seriesV}  Phase=0"],
            rewireGenerator: true, mixOrder: order, settings: oracleSettings);
        var shunt = RunTwoToneOracle(probe, f2,
            [$"I_1Tone:Iinj  {gNode} 0  Freq={f2}  I={shuntA}  Phase=0"],
            rewireGenerator: false, mixOrder: order, settings: oracleSettings);

        string[] probes = ["GATE", "DRAIN"];
        string[] gNodes = ["n_gl", "n_p2"];
        double worst = 0; string worstAt = "";
        for (int inj = 0; inj < 2; inj++)
        {
            var    ds  = inj == 0 ? series : shunt;
            double amp = inj == 0 ? seriesV : shuntA;
            int    row = 2 * idx - 1 + inj;                       // the document's 2i−1 then 2i

            // The oracle's floor is ADDITIVE, in amperes on a current and volts on a voltage, so a
            // small entry of a row is resolved no better in absolute terms than the row's largest
            // entry of the same kind. Scaling each comparison against that largest entry is the
            // faithful statement of the floor; scaling every entry against ITSELF would demand of a
            // 40-times-smaller cross term forty times the accuracy the oracle has anywhere.
            double maxIs = 0, maxVp = 0;
            for (int j = 0; j < probes.Length; j++)
            {
                maxIs = Math.Max(maxIs, Wsp(single.Ds, 0, row, 2 * (j + 1) - 1).Magnitude);
                maxVp = Math.Max(maxVp, Wsp(single.Ds, 0, row, 2 * (j + 1)).Magnitude);
            }

            for (int j = 0; j < probes.Length; j++)
            {
                Check($"wsp({row},{2 * (j + 1) - 1}) iS[{probes[j]}]",
                      Wsp(single.Ds, 0, row, 2 * (j + 1) - 1),
                      MixValue(ds, "I", "branch", probes[j], "(0,1)") / amp, maxIs);
                Check($"wsp({row},{2 * (j + 1)}) vP[{probes[j]}]",
                      Wsp(single.Ds, 0, row, 2 * (j + 1)),
                      MixValue(ds, "V", "node", gNodes[j], "(0,1)") / amp, maxVp);
            }
        }
        output.WriteLine($"{probe}: worst scaled deviation from the two-tone oracle = {worst:E3} ({worstAt})");
        Assert.True(worst <= tol, $"the two-tone oracle disagrees by {worst:E3} at {worstAt}.");

        void Check(string what, Complex conversion, Complex oracle, double rowScale)
        {
            double rel = (conversion - oracle).Magnitude
                       / Math.Max(1e-30, Math.Max(conversion.Magnitude, rowScale));
            if (rel > worst) { worst = rel; worstAt = what; }
            output.WriteLine($"  {what,-28} conversion {conversion,-44} oracle {oracle,-44} rel {rel:E2}");
        }
    }

    /// <summary>The net on the LOAD side of a probe — where (d)'s series source is spliced in.</summary>
    private static string LNodeOf(string probe) => probe == "GATE" ? "n_p1" : "n_dl";

    /// <summary>
    /// The two-tone run that serves as (d)'s oracle: the same fixture, a second tone declared, and
    /// the injection spliced in. <paramref name="rewireGenerator"/> renames the net the probe's
    /// G-side generator attaches to so the series source sits between them; the shunt injection needs
    /// no rewiring at all.
    /// </summary>
    private DataSet RunTwoToneOracle(string probe, double f2, string[] injection,
        bool rewireGenerator, int mixOrder = 8, AnalysisSettings? settings = null)
    {
        var lines = File.ReadAllLines(Fixture("hb_pumped_two_port.cnl")).ToList();

        if (rewireGenerator)
        {
            // The series source goes on the probe's LOAD side, oriented so that its first node is the
            // far (load) side and its second node is the probe's L node — see the gate's own comment
            // for why that orientation, and no other, reproduces BOTH iS and vP of the probe's own
            // branch injection.
            (string find, string with)[] edits = probe == "GATE"
                // GATE's L node is the device's port-1 net; the device moves to n_inj.
                ? [("SDD:D1  n_p1 0  n_p2 0  \\", "SDD:D1  n_inj 0  n_p2 0  \\")]
                // DRAIN's L node is n_dl; every load-side element on it moves to n_inj.
                : [("L:Ld          n_dl n_dr  L=2 nH",  "L:Ld          n_inj n_dr  L=2 nH"),
                   ("C:Cout        n_dl n_out C=10 pF", "C:Cout        n_inj n_out C=10 pF")];
            foreach (var (find, with) in edits)
            {
                int at = lines.FindIndex(l => l.Trim() == find.Trim());
                Assert.True(at >= 0, $"could not find '{find}' to rewire in the fixture");
                lines[at] = with;
            }
        }

        int an = lines.FindIndex(l => l.TrimStart().StartsWith("analysis ", StringComparison.Ordinal));
        lines.InsertRange(an, injection);
        an += injection.Length;
        lines[an] = "analysis HB1 type=hb NumFreqs=2 Tone[1]=f0 " +
                    $"Tone[2]={f2.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} " +
                    $"MaxMixOrder={mixOrder} MaxHarm={mixOrder} Tol=1e-14";

        var tmp = Path.Combine(Path.GetTempPath(), $"wsp5_2t_{Guid.NewGuid():N}.cnl");
        File.WriteAllLines(tmp, lines);
        try
        {
            var (lib, tb) = CnlReader.ReadFile(tmp);
            var nl  = new Elaborator(lib).Elaborate(tb);
            var hba = tb.Analyses.OfType<HarmonicBalanceAnalysis>().Single();
            var p   = HbEngine.Resolve(hba, nl.ResolvedGlobals, nl.GlobalsWithExplicitUnit);
            var res = new HbEngine(nl, tb, settings ?? new AnalysisSettings()).Run(p);
            Assert.True(res.Converged, "the two-tone oracle run did not converge");
            return res.DataSet;
        }
        finally { File.Delete(tmp); }
    }

    /// <summary>One mixing product of a two-tone cube, by its row label and its
    /// <c>mixIndex</c> label.</summary>
    private static Complex MixValue(DataSet ds, string cube, string rowAxis, string row, string mixLabel)
    {
        var c = ds[cube];
        var axes = c.Axes.ToList();
        int mixAxis = axes.FindIndex(a => a.Name == "mixIndex");
        Assert.True(mixAxis >= 0, $"{cube} has no mixIndex axis");
        int mi = Array.IndexOf(axes[mixAxis].Labels!, mixLabel);
        Assert.True(mi >= 0, $"{cube} has no mixing product {mixLabel}");

        int ra = axes.FindIndex(a => a.Name == rowAxis);
        Assert.True(ra >= 0, $"{cube} has no {rowAxis} axis");
        int ri = Array.IndexOf(axes[ra].Labels!, row);
        Assert.True(ri >= 0, $"{cube} has no {rowAxis} '{row}'");
        return (Complex)c[ri, mi];
    }

    // ══ (e) — a parametric instability ══════════════════════════════════════

    /// <summary>
    /// R-wsp5-9(e). A pumped varactor divider — a tank at <c>f0/2</c> whose capacitance the pump
    /// modulates — is the textbook period-doubling case, and <b>the one instability a linear
    /// analysis cannot see at all</b>: it lives at <c>f0/2</c>, which no S-parameter sweep of the
    /// unpumped circuit knows anything about.
    ///
    /// <para>Below the pump threshold <c>wsp_unstable_freq_kurokawa(H0)</c> over
    /// <c>ssfreq ∈ [0.3 f0, 0.7 f0]</c> is empty; above it, it reports a frequency within a grid step
    /// of <c>f0/2</c>. The threshold is bracketed here, not asserted from a textbook formula, and it
    /// is confirmed independently in <see cref="TheParametricThresholdIsConfirmedByTheTwoToneGain"/>
    /// by the growth of the two-tone response — the same oracle path gate (d) uses.</para>
    ///
    /// <para><b>The grid deliberately steps OVER <c>f0/2</c>.</b> At exactly <c>f0/2</c> the sideband
    /// family is closed under negation and the analysis reports NaN by design
    /// (<see cref="ACommensurateProbeFrequency_IsNaNWithOneWarning"/>); the crossing is found by the
    /// samples either side of it, which is why the point count is chosen so no sample lands on it.
    /// A NaN sample is skipped by the crossing detector rather than breaking it.</para>
    /// </summary>
    [Theory]
    [InlineData(1.0, false)]
    [InlineData(3.0, false)]
    [InlineData(6.0, true)]
    [InlineData(8.0, true)]
    public void APumpedVaractorDivider_ShowsKurokawasSignatureAtHalfTheFundamental(
        double vPump, bool expectUnstable)
    {
        // 40 points over 0.6 .. 1.4 GHz: a step of 20.51 MHz, and 1.0 GHz is not one of them.
        var (ds, _, _, _) = RunHb("hb_varactor_divider.cnl",
            globals: [("Vpump", vPump.ToString("R", System.Globalization.CultureInfo.InvariantCulture))],
            analysisExtra: "SSStart=0.6 SSStop=1.4 SSNpts=40 SSUnit=GHz MarginThreshold=none");

        var ssFreq = ds["wsp"].Axes[0].Values;
        double step = ssFreq[1] - ssFreq[0];
        Assert.DoesNotContain(1e9, ssFreq);

        var h0  = ds["H0:TANK"].ComplexValues;
        var hits = WspKurokawa.UnstableFrequencies(h0, ssFreq);
        output.WriteLine($"Vpump = {vPump}: {hits.Length} Kurokawa crossing(s)" +
                         (hits.Length == 0 ? "" : " at " + string.Join(", ", hits.Select(f => $"{f / 1e9:F4} GHz"))));

        if (!expectUnstable)
        {
            Assert.Empty(hits);
            return;
        }

        Assert.NotEmpty(hits);
        double nearest = hits.MinBy(f => Math.Abs(f - 1e9));
        Assert.True(Math.Abs(nearest - 1e9) <= step,
            $"the crossing at {nearest / 1e9:F4} GHz is further than one grid step " +
            $"({step / 1e6:F2} MHz) from f0/2 = 1 GHz.");
    }

    /// <summary>
    /// (e)'s threshold, confirmed through the code path of (d) rather than a textbook number — and
    /// the observable is the one the brief names: the growth of the pump's own IMAGE of the tickle,
    /// the <c>(1, −1)</c> mixing product at <c>f0 − f_tickle</c>.
    ///
    /// <para><b>The direct response is the wrong observable and measurably so</b> (measured
    /// 171 → 182 → 181 → 166 Ω over the pump range): the tank's resonance moves as the pump's DC
    /// average capacitance shifts, which pulls the tickle off resonance and hides the parametric
    /// gain underneath. The image has no such competing effect — it exists ONLY because the pump
    /// converts, so it is the parametric interaction on its own.</para>
    ///
    /// <para>What that measures is the approach to a <b>degenerate</b> parametric oscillation, and
    /// the honest statement of it is the ratio: signal and idler grow equal at threshold. Measured,
    /// the image runs from 18% of the direct response at 1 V to 95% at 5 V — the pump range in which
    /// <see cref="APumpedVaractorDivider_ShowsKurokawasSignatureAtHalfTheFundamental"/> goes from no
    /// crossing at 3 V to a crossing at 6 V. Past threshold there is nothing for a converged HB run
    /// to report: the periodic solution the tickle is linearised about is no longer the one the
    /// circuit would settle into, which is exactly why the WSProbe's answer — computed at the
    /// nominal solution — is the useful one.</para>
    /// </summary>
    [Fact]
    public void TheParametricThresholdIsConfirmedByTheTwoToneGain()
    {
        const double fTickle = 0.98e9;   // inside the tank's bandwidth, off f0/2 and off f0
        const double amp     = 1e-4;

        var pts = new List<(double Vpump, double Direct, double Image)>();
        foreach (double vPump in new[] { 1.0, 2.0, 3.0, 4.0, 5.0 })
        {
            var lines = File.ReadAllLines(Fixture("hb_varactor_divider.cnl")).ToList();
            int at = lines.FindIndex(l => l.TrimStart().StartsWith("Vpump ", StringComparison.Ordinal));
            lines[at] = $"Vpump = {vPump.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}";
            int an = lines.FindIndex(l => l.TrimStart().StartsWith("analysis ", StringComparison.Ordinal));
            lines.Insert(an, $"I_1Tone:Iinj  n_t 0  Freq={fTickle}  I={amp}  Phase=0");
            lines[an + 1] = "analysis HB1 type=hb NumFreqs=2 Tone[1]=f0 " +
                            $"Tone[2]={fTickle.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} " +
                            "MaxMixOrder=6 MaxHarm=6 Tol=1e-13";

            var tmp = Path.Combine(Path.GetTempPath(), $"wsp5_div_{Guid.NewGuid():N}.cnl");
            File.WriteAllLines(tmp, lines);
            try
            {
                var (lib, tb) = CnlReader.ReadFile(tmp);
                var nl  = new Elaborator(lib).Elaborate(tb);
                var hba = tb.Analyses.OfType<HarmonicBalanceAnalysis>().Single();
                var p   = HbEngine.Resolve(hba, nl.ResolvedGlobals, nl.GlobalsWithExplicitUnit);
                var res = new HbEngine(nl, tb, new AnalysisSettings { HbApftOversample = 3.0 }).Run(p);
                Assert.True(res.Converged, $"the two-tone run at Vpump = {vPump} did not converge");
                double direct = MixValue(res.DataSet, "V", "node", "n_t", "(0,1)").Magnitude / amp;
                double image  = MixValue(res.DataSet, "V", "node", "n_t", "(1,-1)").Magnitude / amp;
                pts.Add((vPump, direct, image));
                output.WriteLine($"Vpump = {vPump,4}: signal {direct,9:F2} Ohm   " +
                                 $"image {image,9:F2} Ohm   image/signal {image / direct,6:F3}");
            }
            finally { File.Delete(tmp); }
        }

        for (int i = 1; i < pts.Count; i++)
            Assert.True(pts[i].Image > pts[i - 1].Image,
                $"the image response fell from {pts[i - 1].Image:F2} at Vpump = {pts[i - 1].Vpump} " +
                $"to {pts[i].Image:F2} at Vpump = {pts[i].Vpump}; the parametric conversion must " +
                "grow with the pump.");

        Assert.True(pts[0].Image / pts[0].Direct < 0.4,
            $"at Vpump = {pts[0].Vpump} the image is already {pts[0].Image / pts[0].Direct:F3} of the " +
            "signal; the fixture is not starting well below its threshold.");
        Assert.True(pts[^1].Image / pts[^1].Direct > 0.85,
            $"at Vpump = {pts[^1].Vpump} the image is only {pts[^1].Image / pts[^1].Direct:F3} of the " +
            "signal; the fixture never approaches the degenerate threshold the Kurokawa signature " +
            "reports between 3 and 6 V.");
    }

    // ══ (f) — negative sidebands are conjugated ══════════════════════════════

    /// <summary>
    /// R-wsp5-9(f). The linear network is real in the time domain, so <c>Y(−ω) = conj(Y(ω))</c> is a
    /// theorem, and a model is never called at a negative frequency at all
    /// (<c>src/Engine/HarmonicBalance/CLAUDE.md</c>'s own contract — the Touchstone interpolators
    /// would not survive it). Asserted directly on <see cref="HbSmallSignal.YNNAt"/>, whose
    /// <c>Extract</c> calls are the only place a sideband frequency reaches the linear engine.
    /// </summary>
    [Fact]
    public void NegativeSidebands_TakeTheConjugateAndNeverCallAModelAtNegativeFrequency()
    {
        var (_, tb, nl) = Load("hb_pumped_two_port.cnl");
        var hba = tb.Analyses.OfType<HarmonicBalanceAnalysis>().Single();
        var p   = HbEngine.Resolve(hba, nl.ResolvedGlobals, nl.GlobalsWithExplicitUnit);
        var eng = new HbEngine(nl, tb, new AnalysisSettings());
        eng.Run(p);

        var ex  = new HbLinearExtractor(nl, new AnalysisSettings());
        var yDc = ex.ExtractDC().YNN;

        foreach (double f in new[] { 0.37e9, 1.63e9, 2.9e9 })
        {
            double w = 2 * Math.PI * f;
            var pos = HbSmallSignal.YNNAt(ex, +w, yDc);
            var neg = HbSmallSignal.YNNAt(ex, -w, yDc);
            for (int i = 0; i < pos.GetLength(0); i++)
            for (int j = 0; j < pos.GetLength(1); j++)
            {
                var want = Complex.Conjugate(pos[i, j]);
                Assert.True((neg[i, j] - want).Magnitude <= 1e-14 * Math.Max(1.0, want.Magnitude),
                    $"Y_NN(−{f / 1e9:G4} GHz)[{i},{j}] = {neg[i, j]}, expected conj of {pos[i, j]}");
            }
        }

        // Zero goes to the DC formulation — a real matrix, not the AC stamp at ω → 0.
        var atZero = HbSmallSignal.YNNAt(ex, 0.0, yDc);
        Assert.Same(yDc, atZero);
    }

    // ══ (g) — sweep stacking and accessors ══════════════════════════════════

    /// <summary>
    /// R-wsp5-9(g). Under a drive sweep <c>ParametricSweepEngine</c> stacks the per-point cubes to
    /// <c>wsp[Vpump, ssfreq, row, col]</c> and each per-probe default to <c>[Vpump, ssfreq]</c>,
    /// exactly as it stacks <c>V</c> and <c>S</c> — the small-signal solve invents no stacking of its
    /// own. The <c>__</c> metadata cubes ride through unstacked.
    /// </summary>
    [Fact]
    public void ADriveSweepStacksTheWspCubes()
    {
        var (lib, tb, nl) = Load("hb_pumped_two_port.cnl",
            // 0.4, 1.3, 2.2, 3.1 GHz — no point is a half-multiple of f0 = 2 GHz, which would be
            // NaN by design (see ACommensurateProbeFrequency_IsNaNWithOneWarning).
            analysisExtra: "SSStart=0.4 SSStop=3.1 SSNpts=4 SSUnit=GHz",
            extraLines: ["analysis SW1 type=parametric_sweep Var=Vpump Start=0.1 Stop=0.5 Npts=3 Inner=HB1"]);
        var sweep = tb.Analyses.OfType<ParametricSweepAnalysis>().Single();
        _ = nl;
        var ds    = ParametricSweepEngine.Run(sweep, lib, tb, new AnalysisSettings());

        var wsp = ds["wsp"];
        Assert.Equal(4, wsp.Axes.Count);
        Assert.Equal("Vpump",  wsp.Axes[0].Name);
        Assert.Equal("ssfreq", wsp.Axes[1].Name);
        Assert.Equal("Hz",     wsp.Axes[1].Unit);
        Assert.Equal(3, wsp.Axes[0].Values.Length);
        Assert.Equal(4, wsp.Axes[1].Values.Length);
        Assert.Equal(4, wsp.Axes[2].Values.Length);

        foreach (var name in new[] { "H0:GATE", "Y0:GATE", "ZG:GATE", "ZL:GATE", "LG:GATE", "F:GATE",
                                     "SM_Y0:GATE", "SM_H0:GATE" })
        {
            var c = ds[name];
            Assert.Equal(2, c.Axes.Count);
            Assert.Equal("Vpump",  c.Axes[0].Name);
            Assert.Equal("ssfreq", c.Axes[1].Name);
        }

        // Metadata is passed through unstacked (src/Engine/CLAUDE.md, "__-prefixed cube names").
        Assert.Single(ds["__WspProbes"].Axes);
        Assert.Equal(2, ds["__WspProbes"].RealValues.Length);

        // H0 read through the library on the stacked cube — the accessors of WSP-1 R-wsp1-11 apply
        // to an HB wsp with no change, because every function maps over the LEADING axes.
        int probes = WspMatrix.ProbeCount(BlockAt(wsp, 0, 0));
        Assert.Equal(2, probes);
        for (int s = 0; s < 3; s++)
        for (int f = 0; f < 4; f++)
        {
            var q  = WspProbeQuad.Of(BlockAt(wsp, s, f), 1);
            var h0 = WspReduction.Defaults(q).H0;
            var cube = (Complex)ds["H0:GATE"][s, f];
            Assert.True((h0 - cube).Magnitude <= 1e-15 * Math.Max(1.0, cube.Magnitude),
                $"H0:GATE[{s},{f}] = {cube} but the library gives {h0} from the same wsp block — " +
                "the run's cube and a derived metric must be one implementation (overview D-2).");
        }

        static Complex[,] BlockAt(DataCube wsp, int s, int f)
        {
            int n = wsp.Axes[2].Values.Length;
            var b = new Complex[n, n];
            for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++) b[r, c] = (Complex)wsp[s, f, r, c];
            return b;
        }
    }

    // ══ (h) — a two-tone operating point ════════════════════════════════════

    /// <summary>
    /// R-wsp5-9(h). The lattice form of the solve (R-wsp5-7): a two-tone operating point carries a
    /// <c>wsp</c>, its low-drive limit is still the S-parameter one, and three tones are refused with
    /// the count of retained products the conversion matrix would need.
    /// </summary>
    [Fact]
    public void TwoToneOperatingPoint_CarriesAWsp_AndThreeTonesAreRefused()
    {
        const string twoTone = "analysis HB1 type=hb NumFreqs=2 Tone[1]=f0 Tone[2]=2.01e9 " +
                               "MaxMixOrder=3 MaxHarm=3 Tol=1e-12 " +
                               "SSStart=0.45 SSStop=3.15 SSNpts=4 SSUnit=GHz";

        // Low drive, so the answer is checkable against the linear wsp of the same netlist.
        var (lib, tb, nl) = LoadWithAnalysis("hb_pumped_two_port.cnl", twoTone,
            globals: [("Vpump", "1e-6")]);
        var hba = tb.Analyses.OfType<HarmonicBalanceAnalysis>().Single();
        var p   = HbEngine.Resolve(hba, nl.ResolvedGlobals, nl.GlobalsWithExplicitUnit);
        var res = new HbEngine(nl, tb, new AnalysisSettings()).Run(p);
        Assert.True(res.Converged, "the two-tone operating point did not converge");
        Assert.True(res.DataSet.Contains("wsp"), "a two-tone operating point produced no wsp");

        var ssFreq = res.DataSet["wsp"].Axes[0].Values;
        var (_, tbSp, nlSp) = Load("hb_pumped_two_port.cnl",
            globals: [("Vpump", "1e-6")],
            extraLines: ["analysis SP1 type=sparam start=0.45 stop=3.15 npts=4 Unit=GHz"]);
        var spFreq = tbSp.Analyses.OfType<SParameterAnalysis>().Single()
                         .Expand(nlSp.ResolvedGlobals, nlSp.GlobalsWithExplicitUnit);
        var dsSp = SParameterEngine.Run(nlSp, spFreq);
        for (int i = 0; i < spFreq.Length; i++) Assert.Equal(spFreq[i], ssFreq[i], 1e-6);

        double worst = 0;
        for (int fi = 0; fi < spFreq.Length; fi++)
        for (int r = 1; r <= 4; r++)
        for (int c = 1; c <= 4; c++)
        {
            var a = Wsp(dsSp, fi, r, c);
            var b = Wsp(res.DataSet, fi, r, c);
            worst = Math.Max(worst, (a - b).Magnitude / Math.Max(1e-30, a.Magnitude));
        }
        output.WriteLine($"two-tone low drive: worst relative wsp deviation = {worst:E3}");
        Assert.True(worst <= 1e-6, $"the two-tone low-drive wsp differs by {worst:E3} relative.");

        // ── Three tones: refused, with the size it would need ─────────────────
        var (_, tb3, nl3) = LoadWithAnalysis("hb_pumped_two_port.cnl",
            "analysis HB1 type=hb NumFreqs=3 Tone[1]=f0 Tone[2]=2.01e9 Tone[3]=2.02e9 " +
            "MaxMixOrder=3 MaxHarm=3 SSStart=0.45 SSStop=3.15 SSNpts=2 SSUnit=GHz");
        var hba3 = tb3.Analyses.OfType<HarmonicBalanceAnalysis>().Single();
        var p3   = HbEngine.Resolve(hba3, nl3.ResolvedGlobals, nl3.GlobalsWithExplicitUnit);
        var ex   = Assert.Throws<InvalidOperationException>(
            () => new HbEngine(nl3, tb3, new AnalysisSettings()).Run(p3));
        Assert.Contains("wsprobe.hb-tones-unsupported", ex.Message);
        Assert.Contains("3", ex.Message);
        output.WriteLine("three tones: " + ex.Message);
        _ = lib;
    }

    /// <summary>The fixture with its analysis line REPLACED rather than extended.</summary>
    private static (Library Lib, TestBench Tb, ElaboratedNetlist Nl) LoadWithAnalysis(
        string name, string analysisLine, (string Var, string Value)[]? globals = null)
    {
        var lines = File.ReadAllLines(Fixture(name)).ToList();
        foreach (var (v, val) in globals ?? [])
        {
            int at = lines.FindIndex(l => l.TrimStart().StartsWith(v + " ", StringComparison.Ordinal));
            Assert.True(at >= 0, $"fixture {name} has no global '{v}'");
            lines[at] = $"{v} = {val}";
        }
        int an = lines.FindIndex(l => l.TrimStart().StartsWith("analysis ", StringComparison.Ordinal));
        lines[an] = analysisLine;

        var tmp = Path.Combine(Path.GetTempPath(), $"wsp5_a_{Guid.NewGuid():N}.cnl");
        File.WriteAllLines(tmp, lines);
        try
        {
            var (lib, tb) = CnlReader.ReadFile(tmp);
            return (lib, tb, new Elaborator(lib).Elaborate(tb));
        }
        finally { File.Delete(tmp); }
    }

    // ══ (i) — counters, not timings ═════════════════════════════════════════

    /// <summary>
    /// R-wsp5-9(i), and overview D-14: the SHAPE of the work asserted as counts, never as wall
    /// clock.
    ///
    /// <para><b>These are brief-wsprobe-8's counts, not WSP-5's.</b> WSP-5's straightforward path
    /// performed <c>2·K_ss + 2</c> linear-partition FACTORISATIONS, <c>4N</c> sparse back-solves and
    /// <c>2N</c> dense solves per probe frequency PER OPERATING POINT; the shipped path performs no
    /// sparse factorisation and no sparse solve at all after the first operating point, because the
    /// linear partition does not depend on the drive. What is asserted here is that the requests are
    /// still the same shape — one probe block plus one <c>Y_NN</c> per sideband, one dense
    /// factorisation and <c>2N</c> dense solves per point — and that the sparse work underneath them
    /// collapsed onto the distinct frequencies. WSP-8's own gates
    /// (<c>WSProbeHbPerformanceTests</c>) hold the rest, including the equality with WSP-5's path.</para>
    /// </summary>
    [Fact]
    public void SmallSignalSolve_HasTheStructureItsCountersClaim()
    {
        const int nPoints = 5, kSs = 3, nProbes = 2;
        var (_, eng, _, _) = RunHb("hb_pumped_two_port.cnl",
            analysisExtra: $"SSStart=0.45 SSStop=3.25 SSNpts={nPoints} SSUnit=GHz SSMaxHarm={kSs}");

        var c = eng.LastSmallSignalCounters;
        Assert.NotNull(c);
        output.WriteLine($"per {nPoints} points: partitions {c.LinearPartitions}, dense LU " +
                         $"{c.DenseFactorizations}, dense solves {c.DenseSolves}, sparse solves " +
                         $"{c.SparseSolves}, sparse LU {c.SparseFactorizations}, transposed " +
                         $"{c.TransposedSolves}, stamps {c.Stamps}, hits {c.CacheHits}, " +
                         $"degenerate {c.DegeneratePoints}");

        Assert.Equal(0, c.DegeneratePoints);
        Assert.Equal(nPoints, c.DenseFactorizations);
        Assert.Equal(nPoints * 2 * nProbes, c.DenseSolves);
        // 2·K_ss + 1 sidebands and the probe block at ω_ss, plus one PREFILL pass over the probe
        // frequencies — which exists so a probe frequency that is another point's sideband is
        // factored once rather than twice (see HbSmallSignal.Precompute).
        Assert.Equal(nPoints * (2 * kSs + 3), c.LinearPartitions);

        // The whole of brief-wsprobe-8 in two lines: no forward sparse solve survives, and the
        // factorisations are one per DISTINCT sideband frequency rather than 2·K_ss + 2 per point.
        Assert.Equal(0, c.SparseSolves);
        Assert.Equal(c.DistinctSidebandFrequencies, c.SparseFactorizations);
        Assert.True(c.SparseFactorizations < nPoints * (2 * kSs + 2),
            $"{c.SparseFactorizations} factorisations for {nPoints} points at K_ss = {kSs}");

        // Each factorisation carries N_int interface rows; a probe frequency carries 2N more.
        const int nInt = 2;
        Assert.Equal(c.SparseFactorizations * nInt + nPoints * 2 * nProbes, c.TransposedSolves);
    }

    /// <summary>
    /// A probe frequency commensurate with the fundamental at order 2 — <c>2·f_ss</c> an exact
    /// multiple of <c>f0</c> — is NaN with one warning, not a plausible wrong number.
    ///
    /// <para>There the sideband family <c>ω_ss + kω0</c> and its negation are the SAME set of
    /// frequencies, so the responses at <c>+ω_ss</c> and <c>−ω_ss</c> are conjugates of each other
    /// rather than independent, and the formulation — one family, the stimulus at its own sideband —
    /// carries only half the stimulus. <b>This is why a parametric instability at <c>f0/2</c> is
    /// found by the points either side of it</b>, which is what the sweep is for.</para>
    /// </summary>
    [Fact]
    public void ACommensurateProbeFrequency_IsNaNWithOneWarning()
    {
        // f0 = 2 GHz, so 1 GHz (f0/2), 2 GHz (f0) and 3 GHz (3f0/2) are all degenerate; 1.4 GHz is not.
        var (ds, eng, _, nl) = RunHb("hb_pumped_two_port.cnl",
            analysisExtra: "SSStart=1.0 SSStop=3.0 SSNpts=5 SSUnit=GHz");

        var freqs = ds["wsp"].Axes[0].Values;
        Assert.Equal([1e9, 1.5e9, 2e9, 2.5e9, 3e9], freqs.Select(f => Math.Round(f, 3)));

        for (int fi = 0; fi < freqs.Length; fi++)
        {
            bool degenerate = Math.Abs(2 * freqs[fi] / 2e9 - Math.Round(2 * freqs[fi] / 2e9)) < 1e-9;
            var v = Wsp(ds, fi, 2, 2);
            Assert.Equal(degenerate, Complex.IsNaN(v));
        }

        Assert.Equal(3, eng.LastSmallSignalCounters!.DegeneratePoints);
        var warn = nl.Warnings.Where(w => w.Contains("commensurate")).ToList();
        Assert.Single(warn);
        Assert.Contains("either side of it", warn[0]);
        output.WriteLine(warn[0]);
    }

    // ══ The directive and its reports ═══════════════════════════════════════

    /// <summary>
    /// R-wsp5-1's "not an error" case and its <c>SSMaxHarm</c> report — the two things a probed HB
    /// run says about itself.
    /// </summary>
    [Fact]
    public void TheRunReportsWhatItDidAndDidNotDo()
    {
        var bare = RunHb("hb_pumped_two_port.cnl");
        Assert.Contains(bare.Nl.Notes, n => n.Contains("2 WSProbes present")
                                         && n.Contains("SSStart/SSStop"));

        var truncated = RunHb("hb_pumped_two_port.cnl",
            analysisExtra: "SSStart=1.4 SSStop=1.4 SSNpts=1 SSUnit=GHz SSMaxHarm=1");
        Assert.Contains(truncated.Nl.Notes, n => n.Contains("SSMaxHarm = 1") && n.Contains("4 harmonics"));

        var full = RunHb("hb_pumped_two_port.cnl",
            analysisExtra: "SSStart=1.4 SSStop=1.4 SSNpts=1 SSUnit=GHz");
        Assert.DoesNotContain(full.Nl.Notes, n => n.Contains("SSMaxHarm"));
        Assert.DoesNotContain(full.Nl.Notes, n => n.Contains("SSStart/SSStop"));
    }

    /// <summary>
    /// The <c>SS*</c> keys round-trip through <c>.cnl</c>, and an HB line without them writes and
    /// reads exactly as it did before WSP-5 — the file half of R-wsp5-9(a).
    /// </summary>
    [Fact]
    public void SsKeysRoundTripThroughCnl()
    {
        var hba = new HarmonicBalanceAnalysis("HB1")
        {
            ToneExpr      = "2e9",
            SsStartExpr   = "0.1",
            SsStopExpr    = "10",
            SsNptsExpr    = "991",
            SsUnit        = "GHz",
            SsLogExpr     = "true",
            SsMaxHarmExpr = "3",
        };
        var tb = new TestBench("T");
        tb.Analyses.Add(hba);
        var text = CnlWriter.Write(tb, new Library("L"));
        output.WriteLine(text.Split('\n').First(l => l.Contains("type=hb")));

        var tmp = Path.Combine(Path.GetTempPath(), $"wsp5_rt_{Guid.NewGuid():N}.cnl");
        File.WriteAllText(tmp, text);
        try
        {
            var (_, back) = CnlReader.ReadFile(tmp);
            var got = back.Analyses.OfType<HarmonicBalanceAnalysis>().Single();
            Assert.Equal("0.1", got.SsStartExpr);
            Assert.Equal("10",  got.SsStopExpr);
            Assert.Equal("991", got.SsNptsExpr);
            Assert.Equal("GHz", got.SsUnit);
            Assert.True(got.HasSsSweep);
            Assert.Equal("3", got.SsMaxHarmExpr);
            Assert.Equal(SweepKind.Log, got.SsSweep()!.Kind);

            // SSUnit applies to start and stop alike (the AUT-8 lesson): 0.1 .. 10 GHz.
            var grid = got.SsSweep()!.Expand(new Dictionary<string, CircuitRF.Core.Expressions.Value>());
            Assert.Equal(991, grid.Length);
            Assert.Equal(0.1e9, grid[0], 1.0);
            Assert.Equal(10e9,  grid[^1], 1.0);
        }
        finally { File.Delete(tmp); }

        // An HB line with no SS* keys writes none of them.
        var plainTb = new TestBench("T");
        plainTb.Analyses.Add(new HarmonicBalanceAnalysis("HB1") { ToneExpr = "2e9" });
        string plain = CnlWriter.Write(plainTb, new Library("L"));
        Assert.DoesNotContain("SS", plain.Split('\n').First(l => l.Contains("type=hb")));
    }

    /// <summary>
    /// brief-wsprobe-5 §6: a probed loadpull is not refused and is not silently probe-less either —
    /// it says once that the small-signal sweep belongs to the <c>hb</c> analysis, and points at the
    /// envelope, which re-terminates ONE <c>wsp</c> for no further HB solves.
    /// </summary>
    [Fact]
    public void AProbedLoadpullSaysWhereTheSmallSignalSweepLives()
    {
        var (_, tbLp, nl) = Load("hb_pumped_two_port.cnl");
        // The note is raised by the loadpull engine's own context preparation; drive it directly
        // rather than authoring a whole loadpull fixture, since the note is all that is under test.
        var lp = new CircuitRF.Engine.Loadpull.LoadpullEngine(nl, tbLp);
        Assert.Throws<InvalidOperationException>(() => lp.PrepareContext(
            new CircuitRF.Engine.Loadpull.LoadpullAnalysisParams(
                ToneHz: 2e9, MaxHarmonic: 4, FFTOverSample: 1, Tol: 1e-8,
                DriveStepping: DcBiasSteppingMode.IfNecessary, GuardHarmonic: 0, MaxIter: 100,
                LoadTunerName: "", SourceTunerName: "", SweepLoad: true, TuneHarm: 1,
                Grid: null!, Compression: 3, UseGt: true,
                PinStartDbm: -20, PinStepDb: 1, PinMaxDbm: 0, TickleDbm: null)));
        Assert.Contains(nl.Notes, n => n.Contains("wsp_terminate")
                                    && n.Contains("hb analysis only"));
    }
}
