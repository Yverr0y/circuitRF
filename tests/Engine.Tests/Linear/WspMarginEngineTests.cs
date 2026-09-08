// ================================================================
//  WspMarginEngineTests.cs — brief-wsprobe-9's end-to-end gates on real circuits.
//
//    (c) the closed form of §3 on the SPLIT series resonator, both R1 values, 2001 points — plus
//        the FLAT case, which is WSP-1's own resonator and shows what the definition does when one
//        side of the probe is purely resistive;
//    (d) ONE implementation: the run's SM_Y0:<label> cube is bit-identical to the built-in, to
//        wsp_sm_z of the run's own ZG/ZL cubes, and to WspMargin.Of on the block;
//    (e) Kurokawa consistency: where the search reports a start-up, rY is exactly 0 and the margin
//        is at the grid's own resolution of zero — and on the STABLE fixture the search reports
//        nothing while the margin still notches, which is [E]'s "more informative" claim on the
//        simplest circuit there is;
//    (f) the [E] reduction as an independent oracle for WSP-3's rank-1 wsp_terminate, on a
//        non-reciprocal three-probe amplifier — and the two things it pins: T-16 (E-Eq. 11 as
//        printed is wrong) and D-9 (the transpose is required);
//    (g) the margin envelope on WSP-3's terminated resonator, against the closed form of the arc;
//    (h) wsp_loadpull_ndf against a re-run with the Terms changed;
//    (k) D-13: nothing here changes an existing number, and MarginThreshold=none is silent.
//
//  The scalar core's own gates (a) and (b) are in tests/RfCore.Tests/Stability/WspMarginTests.cs;
//  nothing here re-derives them.
//
//  References: T. A. Winslow, General Circuit Analysis Using The WSProbe (2023); [M] = "A Novel
//  Stability Margin for Transfer Functions", EuMIC 2024; [E] = "Stability Envelope Using Nodal
//  Transfer Functions", EuMIC 2025.
// ================================================================

using System.Numerics;
using CircuitRF.Core.Design;
using CircuitRF.Core.Elaboration;
using CircuitRF.Core.Expressions;
using CircuitRF.Core.Netlist;
using RfCore.Data;
using RfCore.Stability;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Linear;

public sealed class WspMarginEngineTests(ITestOutputHelper output)
{
    // ── Fixture access ──────────────────────────────────────────────────────

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

    private static (DataSet Ds, double[] Freqs, ElaboratedNetlist Nl, Library Lib, TestBench Tb)
        RunText(string cnlText, string baseDir, AnalysisSettings? settings = null)
    {
        var (lib, tb) = new CnlReader().Read(cnlText, "tb", baseDir);
        var nl    = new Elaborator(lib) { BaseDirectory = baseDir }.Elaborate(tb);
        var spa   = tb.Analyses.OfType<SParameterAnalysis>().First();
        var freqs = spa.Expand(nl.ResolvedGlobals);
        settings ??= AnalysisSettings.Default.WithMarginThreshold(
            Analysis.ParseMarginThresholdDb(spa.MarginThresholdExpr));
        return (SParameterEngine.Run(nl, freqs, settings), freqs, nl, lib, tb);
    }

    private static (DataSet Ds, double[] Freqs, ElaboratedNetlist Nl, Library Lib, TestBench Tb)
        RunFixture(string name, AnalysisSettings? settings = null)
        => RunText(File.ReadAllText(Fixture(name)), Path.GetDirectoryName(Fixture(name))!, settings);

    private static Complex[,] Wsp(DataSet ds, int fi)
    {
        var cube = ds["wsp"];
        int n = cube.Axes[^1].Length;
        var raw = cube.ComplexValues;
        var m = new Complex[n, n];
        for (int r = 0; r < n; r++)
        for (int c = 0; c < n; c++)
            m[r, c] = raw[(fi * n + r) * n + c];
        return m;
    }

    private static Complex[][,] AllWsp(DataSet ds, int nf)
        => [.. Enumerable.Range(0, nf).Select(fi => Wsp(ds, fi))];

    private static Value Measure(string expr, DataSet sp, ElaboratedNetlist nl)
    {
        var tb = new TestBench("tb");
        tb.Measurements.Add(new Measurement("x", expr, null));
        var outp = new DataSet();
        var errs = new MeasurementEvaluator(tb, nl,
            new Dictionary<string, DataSet>(StringComparer.OrdinalIgnoreCase) { ["SP1"] = sp })
            .EvaluateInto(outp);
        Assert.True(errs.Count == 0, errs.Count == 0 ? "" : errs[0]);
        return new Value(outp["x"]);
    }

    private static double Db(double linear) => 20.0 * Math.Log10(linear);

    // ══ (c) — the closed form of §3 ══════════════════════════════════════════

    /// <summary>
    /// The split series resonator of §3, both <c>R1</c> values, against the closed form:
    ///
    /// <code>
    ///   ZG = RS + jωL1                 ZL = R1 − j/(ωC1)          f0 = 1.5915 GHz
    ///   rY = ½(1 + R1/RS) when RS + R1 &gt; 0, else 0 — a CONSTANT
    ///   iY = ½(1 − 1/(ω²L1C1)) above f0, ½(1 − ω²L1C1) below
    /// </code>
    ///
    /// <para>Three things to read off. The <c>SM_Y0</c> notch sits at <c>f0</c> and its depth is set
    /// by <c>rY</c>, the negative-resistance ratio. <c>SM_H0</c>'s minimum is at a <b>different</b>
    /// frequency — where <c>Re(YG + YL)</c> changes sign, although the impedance sum is positive
    /// everywhere for <c>−5 Ω</c> — which is §4.10's pole masking made visible on a circuit with one
    /// loop in it. And the stable <c>−5 Ω</c> case already sits below [M]'s own −15 dB rule at
    /// <c>f0</c>: a node one negative-resistance step from oscillating HAS little margin.</para>
    /// </summary>
    [Theory]
    [InlineData("margin_split_resonator.cnl",       -5.0,  0.25, 0.12509, 1.59125, 0.10175, 1.73375)]
    [InlineData("margin_split_resonator_neg20.cnl", -20.0, 0.0,  9.406e-5, 1.59125, 0.15852, 1.86000)]
    public void C_TheSplitResonator_MatchesTheClosedForm_AndTheTwoMarginsCollapseAtDifferentFrequencies(
        string fixture, double r1, double rYExpected,
        double smYMin, double smYMinGhz, double smHMin, double smHMinGhz)
    {
        const double RS = 10.0, L1 = 1e-9, C1 = 10e-12;
        var (ds, freqs, _, _, _) = RunFixture(fixture);
        Assert.Equal(2001, freqs.Length);

        var smY = ds["SM_Y0:P"].RealValues;
        var smH = ds["SM_H0:P"].RealValues;
        double worst = 0.0;
        int firstNegConductanceSum = -1;

        for (int fi = 0; fi < freqs.Length; fi++)
        {
            double w = 2 * Math.PI * freqs[fi];
            var zg = new Complex(RS, w * L1);
            var zl = new Complex(r1, -1.0 / (w * C1));
            var yg = Complex.One / zg;
            var yl = Complex.One / zl;

            var (rY, iY, smYc) = WspMargin.FromZ(zg, zl);
            var (_,  _,  smHc) = WspMargin.FromY(yg, yl);

            // rY is a constant, and it is the negative-resistance ratio.
            Assert.Equal(rYExpected, rY, 12);

            // iY in the closed form of §3: the two reactances have opposite signs, so the ratio is
            // negative and passes through −1 at f0.
            double x = w * L1, y = -1.0 / (w * C1);
            double iYc = Math.Abs(x) >= Math.Abs(y) ? 0.5 * (1 + y / x) : 0.5 * (1 + x / y);
            Assert.Equal(iYc, iY, 12);

            worst = Math.Max(worst, Math.Max(Math.Abs(smY[fi] - smYc), Math.Abs(smH[fi] - smHc)));

            if (firstNegConductanceSum < 0 && (yg + yl).Real <= 0.0) firstNegConductanceSum = fi;

            // At −20 Ω the real-part sum is negative EVERYWHERE, so rY ≡ 0 and SM_Y0 ≡ ½·iY exactly.
            if (r1 <= -20.0)
            {
                // rY ≡ 0 and SM_Y0 ≡ ½·iY EXACTLY — asserted on the engine's own pair, since the
                // closed form's ZG/ZL differ from the solved ones in the last bit or two and the
                // claim here is about the arithmetic, not about the solve.
                var m = WspMargin.Of(WspProbeQuad.Of(Wsp(ds, fi), 1));
                Assert.Equal(0.0, rY, 15);
                Assert.Equal(0.0, m.rY, 15);
                Assert.Equal(0.5 * m.iY, smY[fi]);
                Assert.True((zg + zl).Real < 0.0, "Re(ZG + ZL) is negative everywhere at −20 Ω");
            }
        }
        output.WriteLine($"(c) {fixture}: worst |engine − closed form| over 2001 points = {worst:G3}");
        Assert.True(worst <= 1e-12, $"engine vs closed form differ by {worst:G3}");

        // ── The minima and where they sit ────────────────────────────────────
        int yi = ArgMin(smY), hi = ArgMin(smH);
        output.WriteLine(
            $"(c) {fixture}: SM_Y0 min {smY[yi]:G5} ({Db(smY[yi]):F2} dB) at {freqs[yi] / 1e9:G6} GHz; " +
            $"SM_H0 min {smH[hi]:G5} ({Db(smH[hi]):F2} dB) at {freqs[hi] / 1e9:G6} GHz");
        Assert.Equal(smYMin, smY[yi], 4 + (int)Math.Max(0, -Math.Floor(Math.Log10(smYMin))));
        Assert.Equal(smHMin, smH[hi], 5);
        double step = freqs[1] - freqs[0];
        Assert.True(Math.Abs(freqs[yi] - smYMinGhz * 1e9) <= step, $"SM_Y0 min at {freqs[yi] / 1e9:G8} GHz");
        Assert.True(Math.Abs(freqs[hi] - smHMinGhz * 1e9) <= step, $"SM_H0 min at {freqs[hi] / 1e9:G8} GHz");

        // The SM_Y0 notch sits at f0; SM_H0's minimum does NOT, and it coincides with the first
        // frequency at which the CONDUCTANCE sum turns negative. That is the pole masking.
        double f0 = 1.0 / (2 * Math.PI * Math.Sqrt(L1 * C1));
        Assert.True(Math.Abs(freqs[yi] - f0) <= step, $"the SM_Y0 notch is not at f0 ({freqs[yi] / 1e9:G8} vs {f0 / 1e9:G8} GHz)");
        Assert.True(Math.Abs(freqs[hi] - f0) > 100 * step, "SM_H0's minimum coincided with f0 — the two margins would not be independent");
        Assert.True(firstNegConductanceSum >= 0, "Re(YG + YL) never turned negative");
        output.WriteLine($"(c) {fixture}: Re(YG + YL) ≤ 0 from {freqs[firstNegConductanceSum] / 1e9:G6} GHz");
        Assert.True(Math.Abs(freqs[hi] - freqs[firstNegConductanceSum]) <= step,
            $"SM_H0's minimum ({freqs[hi] / 1e9:G8} GHz) is not where Re(YG + YL) changes sign " +
            $"({freqs[firstNegConductanceSum] / 1e9:G8} GHz)");
    }

    /// <summary>
    /// The FLAT case — and the sentence §3 opens with, held rather than assumed. WSP-1's own
    /// resonator puts the probe at the Term, so <c>ZL</c> is purely real, <c>Im ZL = 0</c>, and
    /// <c>iY</c> is 0.5 at every frequency by the both-parts-zero convention (§2.1c): the margin is
    /// <b>flat at 0.375</b> for <c>R1 = −5 Ω</c> and shows no resonance at all.
    ///
    /// <para>That is the definition, not a defect — the proxy normalises one reactance by the
    /// OTHER, and a resistive side has none to offer. A probe against a purely resistive termination
    /// reads the resonance on the other side's margin only, which is what <c>SM_H0</c> does here.</para>
    /// </summary>
    [Fact]
    public void C_ProbeAtAPurelyResistiveTermination_ReadsAFlatSeriesMargin()
    {
        var (ds, freqs, _, _, _) = RunFixture("series_resonator_term.cnl");
        var smY = ds["SM_Y0:P"].RealValues;
        var smH = ds["SM_H0:P"].RealValues;

        foreach (double v in smY) Assert.Equal(0.375, v, 15);

        // SM_H0 is not flat: the admittance pair has a reactance on both sides.
        int hi = ArgMin(smH);
        output.WriteLine($"(c) flat case: SM_Y0 ≡ 0.375 ({Db(0.375):F2} dB) at all {freqs.Length} points; " +
                         $"SM_H0 min {smH[hi]:G5} ({Db(smH[hi]):F2} dB) at {freqs[hi] / 1e9:G6} GHz");
        Assert.True(smH.Max() - smH.Min() > 0.1, "SM_H0 was flat too — the fixture proves nothing");
    }

    // ══ (d) — one implementation ═════════════════════════════════════════════

    /// <summary>
    /// Gate (d), overview D-2. The run's own margin cube, the built-in over the run's <c>wsp</c>
    /// cube, <c>wsp_sm_z</c> over the run's own <c>ZG</c>/<c>ZL</c> cubes, and
    /// <see cref="WspMargin.Of"/> on the block are <b>bit-identical</b> — not close, equal — because
    /// there is one implementation and four callers. The accessor resolves, and a parametric sweep
    /// stacks the margin cubes as it stacks <c>H0</c>.
    /// </summary>
    [Fact]
    public void D_TheRunCube_TheBuiltIn_ThePairPrimitive_AndTheLibraryAgreeBitForBit()
    {
        var (ds, freqs, nl, lib, tb) = RunFixture("derived_metrics.cnl");

        foreach (string label in new[] { "P1", "P2" })
        {
            var run = ds[$"SM_Y0:{label}"].RealValues;
            Assert.Equal(freqs.Length, run.Length);

            var builtin = Measure($"wsp_SM_Y0(SP1.wsp, SP1.idx(\"{label}\"))", ds, nl).AsCube().RealValues;
            var pair    = Measure($"wsp_sm_z(SP1.ZG(\"{label}\"), SP1.ZL(\"{label}\"))", ds, nl).AsCube().RealValues;
            var acc     = Measure($"SP1.SM_Y0(\"{label}\")", ds, nl).AsCube().RealValues;
            Assert.Equal(run, builtin);
            Assert.Equal(run, pair);
            Assert.Equal(run, acc);

            var runH  = ds[$"SM_H0:{label}"].RealValues;
            var bH    = Measure($"wsp_SM_H0(SP1.wsp, SP1.idx(\"{label}\"))", ds, nl).AsCube().RealValues;
            var pairH = Measure($"wsp_sm_y(wsp_YG(SP1.wsp, SP1.idx(\"{label}\")), " +
                                $"wsp_YL(SP1.wsp, SP1.idx(\"{label}\")))", ds, nl).AsCube().RealValues;
            var accH  = Measure($"SP1.SM_H0(\"{label}\")", ds, nl).AsCube().RealValues;
            Assert.Equal(runH, bH);
            Assert.Equal(runH, pairH);
            Assert.Equal(runH, accH);

            // wsp_stability_margin is the elementwise minimum of the two (D-12, now answering).
            var sm = Measure($"wsp_stability_margin(SP1.wsp, SP1.idx(\"{label}\"))", ds, nl).AsCube().RealValues;
            for (int fi = 0; fi < freqs.Length; fi++)
                Assert.Equal(Math.Min(run[fi], runH[fi]), sm[fi]);

            // …and the library called directly on the block.
            int idx = label == "P1" ? 1 : 2;
            for (int fi = 0; fi < freqs.Length; fi++)
            {
                var m = WspMargin.Of(WspProbeQuad.Of(Wsp(ds, fi), idx));
                Assert.Equal(m.SmY0, run[fi]);
                Assert.Equal(m.SmH0, runH[fi]);
            }

            // The four proxies come off the same call.
            var rY = Measure($"wsp_rY(SP1.wsp, {idx})", ds, nl).AsCube().RealValues;
            var iY = Measure($"wsp_iY(SP1.wsp, {idx})", ds, nl).AsCube().RealValues;
            var rH = Measure($"wsp_rH(SP1.wsp, {idx})", ds, nl).AsCube().RealValues;
            var iH = Measure($"wsp_iH(SP1.wsp, {idx})", ds, nl).AsCube().RealValues;
            for (int fi = 0; fi < freqs.Length; fi++)
            {
                Assert.Equal(0.5 * (rY[fi] + iY[fi]), run[fi],  15);
                Assert.Equal(0.5 * (rH[fi] + iH[fi]), runH[fi], 15);
            }
        }

        // ── A parametric sweep stacks them, exactly as it stacks H0 ──────────
        var sw = new ParametricSweepAnalysis("SW", "RGv", [150.0, 250.0], "SP1");
        var swept = ParametricSweepEngine.Run(sw, lib, tb);
        Assert.Equal(["RGv", "freq"], swept["SM_Y0:P1"].Axes.Select(a => a.Name).ToArray());
        Assert.Equal([2, freqs.Length], swept["SM_Y0:P1"].Axes.Select(a => a.Length).ToArray());
        Assert.Equal(["RGv", "freq"], swept["SM_H0:P1"].Axes.Select(a => a.Name).ToArray());
    }

    // ══ (e) — Kurokawa consistency ═══════════════════════════════════════════

    /// <summary>
    /// Gate (e), and §2.1f in one test. The Kurokawa search is the <b>detector</b>; the margin is the
    /// <b>distance</b>. Where the search reports a start-up on <c>1/Y0</c>, <c>rY</c> is exactly 0 at
    /// both neighbouring samples and the margin at the nearer one is at the grid's own resolution of
    /// zero. On the STABLE version of the same circuit the search reports nothing at all — and the
    /// margin still notches, 18 dB down, which is [E]'s "more informative" claim demonstrated on the
    /// simplest circuit there is.
    /// </summary>
    [Fact]
    public void E_WhereKurokawaReportsAStartUp_rYIsZeroAndTheMarginIsAtTheGridsResolutionOfZero()
    {
        var (unstable, freqs, _, _, _) = RunFixture("margin_split_resonator_neg20.cnl");
        var hits = WspKurokawa.UnstableFrequencies(unstable["Y0:P"].ComplexValues, freqs);
        double f = Assert.Single(hits);
        output.WriteLine($"(e) Kurokawa on 1/Y0 at −20 Ω: {f / 1e9:G8} GHz");
        Assert.True(Math.Abs(f - 1.59155e9) < 2e6, $"the crossing is not at f0: {f / 1e9:G8} GHz");

        int k = 0;
        while (k + 1 < freqs.Length && freqs[k + 1] < f) k++;
        var rY = new double[2];
        for (int j = 0; j < 2; j++)
            rY[j] = WspMargin.Of(WspProbeQuad.Of(Wsp(unstable, k + j), 1)).rY;
        Assert.Equal(0.0, rY[0], 15);
        Assert.Equal(0.0, rY[1], 15);

        var smY = unstable["SM_Y0:P"].RealValues;
        int nearer = Math.Abs(freqs[k] - f) <= Math.Abs(freqs[k + 1] - f) ? k : k + 1;
        output.WriteLine($"(e) SM_Y0 at the nearer sample ({freqs[nearer] / 1e9:G8} GHz): " +
                         $"{smY[nearer]:G4} ({Db(smY[nearer]):F1} dB)");
        Assert.True(smY[nearer] < 1e-3, $"SM_Y0 = {smY[nearer]:G4} at the crossing");

        // The stable fixture: no crossing on either driving-point function, and a deep notch anyway.
        var (stable, sf, _, _, _) = RunFixture("margin_split_resonator.cnl");
        Assert.Empty(WspKurokawa.UnstableFrequencies(stable["Y0:P"].ComplexValues, sf));
        Assert.Empty(WspKurokawa.UnstableFrequencies(stable["H0:P"].ComplexValues, sf));
        var sSmY = stable["SM_Y0:P"].RealValues;
        int si = ArgMin(sSmY);
        output.WriteLine($"(e) stable fixture: Kurokawa reports nothing; SM_Y0 still notches to " +
                         $"{Db(sSmY[si]):F2} dB at {sf[si] / 1e9:G6} GHz");
        Assert.True(Db(sSmY[si]) < -15.0, "the stable fixture's margin did not notch below −15 dB");
    }

    // ══ (f) — the [E] reduction as an independent oracle ═════════════════════

    /// <summary>
    /// Gate (f), R-wsp9-7. [E]'s route to the mismatched driving-point quantities, built here from
    /// the <c>wsp</c> entries and reduced by hand, against WSP-3's rank-1 <c>wsp_terminate</c>.
    ///
    /// <para><b>The construction</b> (E-Eq. 1–4). With the suspect probe's branch OPEN, the circuit
    /// is a 4-port: port 1 the source probe's node, port 2 the load probe's node, ports 3 and 4 the
    /// suspect probe's G and L terminals. Four stimuli the <c>wsp</c> matrix already carries drive
    /// it — a shunt current at each of the three probe nodes, and the suspect probe's own series
    /// voltage — and for each one <c>wsp</c> gives every port voltage and every port current, the
    /// suspect probe's branch current included. Stacking those as <c>V_m</c> and <c>I_m</c> (rows =
    /// stimuli, the <c>wsp</c> convention) gives <c>I_mᵀ = Y·V_mᵀ</c>, hence
    /// <c>Y = (V_m⁻¹ I_m)ᵀ</c> — E-Eq. 4's transpose, which is overview D-9 arriving from the other
    /// side. The two bookkeeping corrections are the ones WSP-3 §6 already needed: under the probe's
    /// OWN series stimulus the L-terminal voltage is <c>vP + vS</c>, and the branch current leaves
    /// port 3 and enters port 4.</para>
    ///
    /// <para>Then E-Eq. 5 swaps the terminations on the diagonal (<c>Y11 − YSo + YS</c>, exactly as
    /// Eq. 191 does), E-Eq. 6–8 reduce the 4×4 onto ports 3 and 4 — the two terminated ports carry
    /// no external current, so the reduction is the Schur complement, and the register confirms
    /// [E]'s <c>d_ij/D</c> cofactor spelling equals it — and E-Eq. 9–12 read the six quantities off
    /// the resulting 2×2 <c>R</c>, which is the reduced ADMITTANCE two-port at the suspect probe.</para>
    ///
    /// <para>Nothing here calls <c>WspEnvelope</c>, <c>WspReduction</c> or <c>WspGlobal</c>: it is a
    /// second derivation from the <c>wsp</c> entries alone, which is what makes it an oracle.</para>
    /// </summary>
    [Fact]
    public void F_TheEReduction_IsAnIndependentOracleForTheRankOneUpdate_AndPinsT16AndD9()
    {
        var (nominal, freqs, _, _, _) = RunFixture("two_stage_terms.cnl");
        const int S = 1, L = 2, P = 3;                     // source, load, suspect (two_stage_terms.cnl)

        var gS = Complex.FromPolarCoordinates(0.5, 60.0 * Math.PI / 180.0);
        var gL = Complex.FromPolarCoordinates(0.3, -120.0 * Math.PI / 180.0);
        var zs = WspEnvelope.GammaToZ(gS, 50.0);
        var zl = WspEnvelope.GammaToZ(gL, 50.0);

        double worst = 0.0, worstPrinted = 0.0, worstTranspose = 0.0;
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var w = Wsp(nominal, fi);

            // ── E-Eq. 1–4: the core 4-port ───────────────────────────────────
            // Rows are the four stimuli; columns the four ports.
            int[] node = [2 * S - 1, 2 * L - 1, 2 * P - 1];          // 0-based wsp column of each probe's 2·idx
            int   ser  = 2 * P - 2;                                   // 0-based row/col of the suspect's 2·idx − 1
            int[] stim = [2 * S - 1, 2 * L - 1, ser, 2 * P - 1];      // iP at S, iP at L, vS at P, iP at P

            var vm = new Complex[4, 4];
            var im = new Complex[4, 4];
            for (int r = 0; r < 4; r++)
            {
                int row = stim[r];
                bool ownSeries = row == ser;

                vm[r, 0] = w[row, node[0]];
                vm[r, 1] = w[row, node[1]];
                vm[r, 2] = w[row, node[2]];
                vm[r, 3] = w[row, node[2]] + (ownSeries ? Complex.One : Complex.Zero);   // vL = vP + vS

                var iS = w[row, ser];                                  // the suspect probe's branch current
                im[r, 0] = r == 0 ? Complex.One : Complex.Zero;        // the injection at port 1
                im[r, 1] = r == 1 ? Complex.One : Complex.Zero;
                im[r, 2] = (r == 3 ? Complex.One : Complex.Zero) - iS; // injected here, minus what leaves through the probe
                im[r, 3] = iS;                                         // …and enters at the L terminal
            }

            var y = Transpose(MatMul(Invert(vm), im));                 // E-Eq. 4

            // ── E-Eq. 5: the terminations swapped on the diagonal ────────────
            var so = Complex.One / WspReductionZG(w, S);
            var lo = Complex.One / WspReductionZL(w, L);
            y[0, 0] += Complex.One / zs - so;
            y[1, 1] += Complex.One / zl - lo;

            // ── E-Eq. 6–8: reduce onto the suspect probe's two terminals ─────
            var a = new Complex[2, 2] { { y[0, 0], y[0, 1] }, { y[1, 0], y[1, 1] } };
            var b = new Complex[2, 2] { { y[0, 2], y[0, 3] }, { y[1, 2], y[1, 3] } };
            var c = new Complex[2, 2] { { y[2, 0], y[2, 1] }, { y[3, 0], y[3, 1] } };
            var d = new Complex[2, 2] { { y[2, 2], y[2, 3] }, { y[3, 2], y[3, 3] } };
            var r2 = Sub(d, MatMul(c, MatMul(Invert(a), b)));          // R
            var detR = r2[0, 0] * r2[1, 1] - r2[0, 1] * r2[1, 0];

            // ── E-Eq. 9–12, with T-16's correction on Eq. 11 ─────────────────
            var sum   = r2[0, 0] + r2[0, 1] + r2[1, 0] + r2[1, 1];
            var h0E   = Complex.One / sum;                              // E-Eq. 9
            var y0E   = detR / sum;                                     // E-Eq. 10
            var zgE   = (r2[0, 1] + r2[1, 1]) / detR;                   // E-Eq. 11 CORRECTED (T-16)
            var zlE   = (r2[1, 0] + r2[0, 0]) / detR;
            var ygE   = r2[0, 0] + r2[0, 1];                            // E-Eq. 12
            var ylE   = r2[1, 1] + r2[1, 0];
            var zgPrinted = (r2[0, 0] + r2[0, 1]) / detR;               // E-Eq. 11 AS PRINTED

            // ── The rank-1 route, WSP-3 §6 ───────────────────────────────────
            var t  = WspEnvelope.Terminate(w, S, Complex.One / zs, so, L, Complex.One / zl, lo);
            var q  = WspProbeQuad.Of(t, P);
            var yp = WspReduction.YParam(q);

            foreach (var (expected, got, what) in new (Complex, Complex, string)[]
            {
                (WspReduction.H0(q), h0E, "H0'"), (WspReduction.Y0(q), y0E, "Y0'"),
                (WspReduction.ZG(q), zgE, "ZG'"), (WspReduction.ZL(q), zlE, "ZL'"),
                (WspReduction.YG(yp), ygE, "YG'"), (WspReduction.YL(yp), ylE, "YL'"),
            })
            {
                double err = (got - expected).Magnitude / Math.Max(expected.Magnitude, 1e-300);
                worst = Math.Max(worst, err);
                Assert.True(err <= 1e-10,
                    $"{what} at {freqs[fi] / 1e9:G4} GHz: [E] route {got} vs rank-1 {expected} (rel {err:G3})");
            }

            // T-16: the PRINTED E-Eq. 11 disagrees, and not slightly.
            worstPrinted = Math.Max(worstPrinted,
                (zgPrinted - WspReduction.ZG(q)).Magnitude / WspReduction.ZG(q).Magnitude);

            // D-9: without the transpose, the core matrix is a different matrix.
            var untransposed = MatMul(Invert(vm), im);
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    worstTranspose = Math.Max(worstTranspose,
                        (untransposed[i, j] - Transpose(untransposed)[i, j]).Magnitude);
        }

        output.WriteLine($"(f) worst relative disagreement, [E] reduction vs rank-1 update: {worst:G3}");
        output.WriteLine($"(f) T-16: E-Eq. 11 AS PRINTED is off by {worstPrinted:G3} relative — not a rounding difference");
        output.WriteLine($"(f) D-9: the un-transposed V_m⁻¹·I_m differs from the core Y by up to {worstTranspose:G3}");
        Assert.True(worstPrinted > 1e-3, $"E-Eq. 11 as printed agreed to {worstPrinted:G3} — T-16 would be wrong");
        Assert.True(worstTranspose > 1e-9, "the un-transposed matrix is symmetric — the fixture is reciprocal and proves nothing");
    }

    // ══ (g) — the margin envelope ════════════════════════════════════════════

    /// <summary>
    /// Gate (g). WSP-3's terminated resonator — a Term at 10 Ω on the probe's L side and
    /// <c>R1 = −5 Ω</c> on its G side, so it is STABLE at 10 Ω and the load-pull decides where it
    /// stops being so. Over <c>|Γ| = 0.9</c> in 24 phase steps:
    ///
    /// <list type="bullet">
    ///   <item><c>rY'</c> is exactly 0 on the arc where <c>Re ZS(θ) ≤ 5 Ω</c> — the closed form of
    ///   Eq. 109 with <c>RS → ZS(θ)</c> — and strictly positive everywhere else;</item>
    ///   <item><c>SMenv</c> is below −40 dB on that arc;</item>
    ///   <item>every termination <c>wsp_loadpull_unstable</c> flags is on it.</item>
    /// </list>
    ///
    /// <para>The last is the containment the brief asks for, and it is one-directional on purpose:
    /// the margin collapses wherever the real-part condition holds, while Kurokawa also needs the
    /// reactances to cancel at a SAMPLED frequency, so the flagged set is a subset.</para>
    /// </summary>
    [Fact]
    public void G_TheMarginEnvelope_CollapsesExactlyOnTheArcTheClosedFormPredicts()
    {
        var (ds, freqs, _, _, _) = RunFixture("series_resonator_term.cnl");
        var all  = AllWsp(ds, freqs.Length);
        var grid = WspEnvelope.CircleGrid(0.9, 24);

        var env = WspEnvelope.LoadpullMargin(all, freqs, 0, 1, 1, [new Complex(double.NaN, double.NaN)], grid, 50.0);
        var uns = WspEnvelope.LoadpullUnstable(
            WspEnvelope.Loadpull(all, freqs, 0, 1, 1, [new Complex(double.NaN, double.NaN)], grid, 50.0));

        var onArc = new bool[grid.Length];
        double worstOnArc = double.NegativeInfinity;
        for (int l = 0; l < grid.Length; l++)
        {
            var zs = WspEnvelope.GammaToZ(grid[l], 50.0);
            onArc[l] = zs.Real <= 5.0;                      // R1 + Re ZS ≤ 0 with R1 = −5

            // rY' on the arc is exactly 0 at every frequency, and strictly positive off it.
            double maxRy = 0.0, minRy = 1.0;
            for (int fi = 0; fi < freqs.Length; fi++)
            {
                var t = WspEnvelope.Terminate(all[fi], 0, Complex.Zero, Complex.Zero,
                                              1, Complex.One / zs, WspEnvelope.StartingAdmittance(all[fi], 1, WspSide.L));
                double ry = WspMargin.Of(WspProbeQuad.Of(t, 1)).rY;
                maxRy = Math.Max(maxRy, ry);
                minRy = Math.Min(minRy, ry);
            }

            double smDb = Db(env.SmEnv[0, l]);
            output.WriteLine(
                $"(g) θ = {grid[l].Phase * 180 / Math.PI,6:F1}°  ZS = {zs.Real,8:F3} {(zs.Imaginary < 0 ? "−" : "+")} " +
                $"j{Math.Abs(zs.Imaginary),7:F3}  rY' ∈ [{minRy:G3}, {maxRy:G3}]  SMenv {smDb,8:F2} dB  " +
                $"unstable {uns.Count(0, l)}");

            if (onArc[l])
            {
                Assert.Equal(0.0, maxRy, 15);

                // ONE grid point of the 24 is exempt, and it is a finding rather than a tolerance:
                // at θ = 180° the pulled load is PURELY RESISTIVE (Γ = −0.9 ⇒ ZS = 2.63 Ω, Im = 0),
                // so the reactance proxy takes its both-parts-zero convention value of 0.5 (§2.1c)
                // and the margin floors at 0.25 = −12.04 dB instead of collapsing. That is the flat
                // case of §3 arriving from the other direction — a probe facing a resistive side has
                // no reactance to normalise against — and it means the brief's "below −40 dB on the
                // arc" holds everywhere the pulled termination is reactive, which is 23 of 24.
                if (Math.Abs(zs.Imaginary) <= 1e-12 * zs.Magnitude)
                {
                    Assert.Equal(0.25, env.SmEnv[0, l], 12);
                    output.WriteLine("(g)   ^ purely resistive pull: the reactance proxy is 0.5 by " +
                                     "convention (§2.1c) and the margin floors at −12.04 dB");
                }
                else
                {
                    worstOnArc = Math.Max(worstOnArc, smDb);
                    Assert.True(smDb < -30.0, $"SMenv = {smDb:F2} dB on the arc at θ = {grid[l].Phase * 180 / Math.PI:F1}°");
                }
            }
            else
            {
                Assert.True(minRy > 0.0, $"rY' reached 0 off the arc at θ = {grid[l].Phase * 180 / Math.PI:F1}°");
            }
        }

        Assert.Contains(true,  onArc);
        Assert.Contains(false, onArc);

        // The depth on the arc is a GRID property, not a circuit property, and the brief's "below
        // −40 dB" is a claim about a sweep it did not name. On this fixture's own 251-point sweep
        // the shallowest reactive arc point reads −39.3 dB — the notch is narrower there than the
        // 10 MHz step, so the sampled minimum misses the bottom. Refining the sweep to 2001 points
        // takes the same point past −40 dB, which is what pins the cause as resolution rather than
        // arithmetic; it is recorded here rather than tuned away.
        output.WriteLine($"(g) shallowest SMenv on the reactive arc, 251-point sweep: {worstOnArc:F2} dB");
        string fine = File.ReadAllText(Fixture("series_resonator_term.cnl")).Replace("npts=251", "npts=2001");
        var (fineDs, fineF, _, _, _) = RunText(fine, Path.GetDirectoryName(Fixture("series_resonator_term.cnl"))!);
        var fineEnv = WspEnvelope.LoadpullMargin(
            AllWsp(fineDs, fineF.Length), fineF, 0, 1, 1, [new Complex(double.NaN, double.NaN)], grid, 50.0);
        double worstFine = double.NegativeInfinity;
        for (int l = 0; l < grid.Length; l++)
        {
            var zs = WspEnvelope.GammaToZ(grid[l], 50.0);
            if (!onArc[l] || Math.Abs(zs.Imaginary) <= 1e-12 * zs.Magnitude) continue;
            worstFine = Math.Max(worstFine, Db(fineEnv.SmEnv[0, l]));
        }
        output.WriteLine($"(g) shallowest SMenv on the reactive arc, 2001-point sweep: {worstFine:F2} dB");
        Assert.True(worstFine < -40.0,
            $"even at 2001 points the shallowest reactive arc point is {worstFine:F2} dB — not a resolution effect");
        for (int l = 0; l < grid.Length; l++)
            if (uns.Count(0, l) > 0)
                Assert.True(onArc[l], $"a flagged termination at θ = {grid[l].Phase * 180 / Math.PI:F1}° is off the arc");

        // The collapsed and the frequency-resolved answers are the same numbers.
        for (int l = 0; l < grid.Length; l++)
        {
            double best = double.MaxValue; int at = -1;
            for (int fi = 0; fi < freqs.Length; fi++)
                if (env.Sm[0, l, fi] < best) { best = env.Sm[0, l, fi]; at = fi; }
            Assert.Equal(best, env.SmEnv[0, l]);
            Assert.Equal(freqs[at], env.SmEnvHz[0, l]);
            for (int fi = 0; fi < freqs.Length; fi++)
                Assert.Equal(Math.Min(env.SmY0[0, l, fi], env.SmH0[0, l, fi]), env.Sm[0, l, fi]);
        }
    }

    // ══ (h) — the NDF over the same envelope ═════════════════════════════════

    /// <summary>
    /// Gate (h). <c>wsp_loadpull_ndf</c> against a re-run with the Terms changed, at four grid
    /// points. The passivated circuit is the same fixture with both transconductances set to zero —
    /// WSP-6's <c>NDFgm</c> pattern written into the netlist — and the SAME terminations are applied
    /// to both matrices, which is the whole content of the claim that the envelope NDF needs no
    /// re-simulation.
    /// </summary>
    [Fact]
    public void H_TheEnvelopeNdf_EqualsARerunWithTheTermsChanged()
    {
        string dir  = Path.GetDirectoryName(Fixture("two_stage_terms.cnl"))!;
        string text = File.ReadAllText(Fixture("two_stage_terms.cnl"));
        string passiveText = Passivate(text);
        Assert.NotEqual(text, passiveText);

        var (active,  freqs, _, _, _) = RunText(text,        dir);
        var (passive, pf,    _, _, _) = RunText(passiveText, dir);
        Assert.Equal(freqs.Length, pf.Length);

        var grid = new[]
        {
            Complex.FromPolarCoordinates(0.6,  30.0 * Math.PI / 180.0),
            Complex.FromPolarCoordinates(0.6, 150.0 * Math.PI / 180.0),
            Complex.FromPolarCoordinates(0.6, 210.0 * Math.PI / 180.0),
            Complex.FromPolarCoordinates(0.6, 330.0 * Math.PI / 180.0),
        };
        var noPull = new[] { new Complex(double.NaN, double.NaN) };

        var env = WspEnvelope.LoadpullNdf(
            AllWsp(active, freqs.Length), AllWsp(passive, freqs.Length), freqs,
            1, 0, null, grid, noPull, 50.0);

        double worst = 0.0;
        for (int s = 0; s < grid.Length; s++)
        {
            var zs = WspEnvelope.GammaToZ(grid[s], 50.0);
            string pulled  = text.Replace("Term:T1     p1 0   Num=1 Z=50 Ohm",
                                          $"Term:T1     p1 0   Num=1 Z={ComplexParam(zs)}");
            Assert.NotEqual(text, pulled);
            var (rerunA, rf, _, _, _) = RunText(pulled,            dir);
            var (rerunP, _,  _, _, _) = RunText(Passivate(pulled), dir);
            Assert.Equal(freqs.Length, rf.Length);

            var trace = new Complex[freqs.Length];
            for (int fi = 0; fi < freqs.Length; fi++)
            {
                trace[fi] = WspGlobal.Ndf(Wsp(rerunA, fi), Wsp(rerunP, fi));
                double err = (env.Ndf[s, 0, fi] - trace[fi]).Magnitude / Math.Max(trace[fi].Magnitude, 1e-300);
                worst = Math.Max(worst, err);
                Assert.True(err <= 1e-9,
                    $"NDFenv at θ = {grid[s].Phase * 180 / Math.PI:F0}°, {freqs[fi] / 1e9:G4} GHz: " +
                    $"{env.Ndf[s, 0, fi]} vs re-run {trace[fi]} (rel {err:G3})");
            }

            double encRerun = Math.Round(WspKurokawa.Encirclements(trace)[^1], MidpointRounding.AwayFromZero);
            Assert.Equal(encRerun, env.Encirclements[s, 0]);
            output.WriteLine($"(h) θ = {grid[s].Phase * 180 / Math.PI,5:F0}°  ZS = {zs}  " +
                             $"NDF encirclements {env.Encirclements[s, 0]}");
        }
        output.WriteLine($"(h) worst relative NDF error, rank-1 envelope vs re-run: {worst:G3}");
    }

    // ══ (i) — Ohtomo's Type-A amplifier ══════════════════════════════════════

    /// <summary>
    /// Gate (i). The two-device parallel amplifier of M. Ohtomo, <i>IEEE Trans. MTT</i> vol. 41
    /// no. 6/7, 1993 — the topology [E] sweeps — <b>redrawn with circuitRF's own element values</b>
    /// (overview D-15), probed S / G / L as [E] Fig. 1, with the balancing resistor <c>Rb</c> across
    /// the two gates.
    ///
    /// <para><b>Asserted.</b> At nominal 50 Ω with <c>Rb = 30 Ω</c> the Kurokawa search finds nothing
    /// at the suspect probe and the reduced NDF makes no encirclement. <c>Rb</c> is <b>load-bearing</b>:
    /// raising it to 100 Ω — weakening the odd-mode damping and changing nothing else — makes the
    /// search report a start-up at 6.09 GHz, the NDF encircle, and the margin at the suspect probe
    /// collapse there. That is Ohtomo's odd mode, and it is what the fixture exists to show.</para>
    ///
    /// <para><b>Measured and recorded, not asserted</b> — and it is the substantive finding of this
    /// gate. Over <c>ρ = 0.9</c> on either side in 10° steps, <b>no termination produces an
    /// encirclement and the margin barely moves</b>. That is not a defect in the envelope: the odd
    /// mode sees both ports as virtual grounds, so no source or load termination can reach it, which
    /// is Ohtomo's own thesis and the reason a Type-A amplifier needs <c>Rb</c> rather than a better
    /// match. <b>A source/load stability envelope cannot see an odd-mode instability</b>, and an
    /// instability [E]'s own circuit exposes to a VSWR sweep must therefore live in a port-coupled
    /// path — its 200–400 MHz band is a low-frequency bias-network mode, not this one.</para>
    ///
    /// <para><b>What this gate does NOT claim.</b> The brief asks for the encirclement set at
    /// <c>ρ = 0.9</c> to be non-empty. It is empty on this circuit, for the reason above. The line
    /// impedances and lengths of [E] Fig. 4 and the eleven element values of [E] Fig. 5 are in a
    /// document not held here, so the circuit is the same TOPOLOGY with different elements and its
    /// numbers were never going to be [E]'s; rather than tune a fixture until it produced the
    /// asserted shape, the ladder is measured and printed. The margin-vs-NDF comparison the brief
    /// wants is made instead on the <c>Rb</c> sweep, where both quantities move together.</para>
    /// </summary>
    [Fact]
    public void I_OhtomoTypeA_IsStableAtFiftyOhms_AndItsOddModeIsInvisibleToTheEnvelope()
    {
        string dir  = Path.GetDirectoryName(Fixture("ohtomo_type_a.cnl"))!;
        string text = File.ReadAllText(Fixture("ohtomo_type_a.cnl"));
        var (active,  freqs, _, _, _) = RunText(text, dir);
        var (passive, _,     _, _, _) = RunText(PassivateOhtomo(text), dir);
        const int S = 1, P = 2, L = 3;

        var wA = AllWsp(active,  freqs.Length);
        var wP = AllWsp(passive, freqs.Length);

        // ── Nominal 50 Ω, Rb = 30 Ω: nothing on either driving-point function, no encirclement ──
        Assert.Empty(WspKurokawa.UnstableFrequencies(active["Y0:PG"].ComplexValues, freqs));
        Assert.Empty(WspKurokawa.UnstableFrequencies(active["H0:PG"].ComplexValues, freqs));
        var ndf0 = new Complex[freqs.Length];
        for (int fi = 0; fi < freqs.Length; fi++) ndf0[fi] = WspGlobal.Ndf(wA[fi], wP[fi]);
        double enc0 = Round0(WspKurokawa.Encirclements(ndf0)[^1]);
        var smNom = active["SM_Y0:PG"].RealValues;
        int nomI = ArgMin(smNom);
        output.WriteLine($"(i) nominal 50 Ω, Rb = 30 Ω: NDF encirclements {enc0}; " +
                         $"SM_Y0(PG) min {Db(smNom[nomI]):F2} dB at {freqs[nomI] / 1e9:G5} GHz");
        Assert.Equal(0.0, enc0);

        // ── Rb is load-bearing: 100 Ω lets the odd mode start up ─────────────
        string weak = text.Replace("R:RB    g1 g2    R=30 Ohm", "R:RB    g1 g2    R=100 Ohm");
        Assert.NotEqual(text, weak);
        var (weakA, wf, _, _, _) = RunText(weak, dir);
        var (weakP, _,  _, _, _) = RunText(PassivateOhtomo(weak), dir);

        var oddHits = WspKurokawa.UnstableFrequencies(weakA["Y0:PG"].ComplexValues, wf);
        var ndfW = new Complex[wf.Length];
        for (int fi = 0; fi < wf.Length; fi++) ndfW[fi] = WspGlobal.Ndf(Wsp(weakA, fi), Wsp(weakP, fi));
        double encW = Round0(WspKurokawa.Encirclements(ndfW)[^1]);
        var smW = weakA["SM_Y0:PG"].RealValues;
        int wI = ArgMin(smW);
        output.WriteLine($"(i) Rb raised to 100 Ω: Kurokawa on 1/Y0(PG) reports " +
                         $"[{string.Join(", ", oddHits.Select(h => $"{h / 1e9:G6} GHz"))}]; " +
                         $"NDF encirclements {encW}; SM_Y0(PG) min {Db(smW[wI]):F2} dB at {wf[wI] / 1e9:G5} GHz");
        Assert.NotEmpty(oddHits);
        Assert.True(Db(smW[wI]) < Db(smNom[nomI]),
            "the margin did not deepen when the balancing resistor was weakened");

        // ── The reduced NDF over THIS probe set reads zero — and why ─────────
        // The document's own caveat (p. 112–113): the probe-based NDF is the REDUCED one over the
        // probed nodes, and it is complete only if the probe set covers every node that can hide a
        // pole. The odd mode is differential across g1 and g2, and only g1 carries a probe, so the
        // reduction cannot see it. That is demonstrated rather than asserted: adding a fourth probe
        // at gate 2 — nothing else changed — makes the same NDF encircle.
        Assert.Equal(0.0, encW);
        string bothGates = weak.Replace(
            "R:RG2   g2 gi2   R=0.5 Ohm",
            "WSProbe:PG2 g2 g2a\nR:RG2   g2a gi2   R=0.5 Ohm");
        Assert.NotEqual(weak, bothGates);
        var (bgA, bgF, _, _, _) = RunText(bothGates, dir);
        var (bgP, _,   _, _, _) = RunText(PassivateOhtomo(bothGates), dir);
        var ndfBg = new Complex[bgF.Length];
        for (int fi = 0; fi < bgF.Length; fi++) ndfBg[fi] = WspGlobal.Ndf(Wsp(bgA, fi), Wsp(bgP, fi));
        double encBg = Round0(WspKurokawa.Encirclements(ndfBg)[^1]);
        output.WriteLine($"(i) the same circuit with a probe on BOTH gates: NDF encirclements {encBg} " +
                         "— the reduced NDF is complete only over a probe set that covers every node " +
                         "that can hide a pole (the reference document, p. 112-113)");
        Assert.True(Math.Abs(encBg) >= 1.0,
            "a probe on both gates still did not make the reduced NDF see the odd mode");

        // The margin collapses AT the frequency the search reports, which is §2.1f's two halves —
        // the detector and the distance — on one circuit.
        int near = 0;
        for (int fi = 0; fi < wf.Length; fi++)
            if (Math.Abs(wf[fi] - oddHits[0]) < Math.Abs(wf[near] - oddHits[0])) near = fi;
        output.WriteLine($"(i) SM_Y0(PG) at the reported start-up ({wf[near] / 1e9:G6} GHz): {Db(smW[near]):F2} dB");
        Assert.True(Db(smW[near]) < -25.0, $"the margin reads {Db(smW[near]):F2} dB at the start-up frequency");

        // ── The envelope at ρ = 0.9: measured, and it finds nothing ──────────
        var grid   = WspEnvelope.CircleGrid(0.9, 24);
        var single = new[] { new Complex(double.NaN, double.NaN) };
        int encirclingTotal = 0;

        foreach (var (idxS, idxL, gS, gL, which) in new (int, int, Complex[], Complex[], string)[]
        {
            (S, 0, grid, single, "source"),
            (0, L, single, grid, "load"),
        })
        {
            var env = WspEnvelope.LoadpullMargin(wA, freqs, idxS, idxL, P, gS, gL, 50.0);
            var ndf = WspEnvelope.LoadpullNdf(wA, wP, freqs, idxS, idxL, null, gS, gL, 50.0);
            var uns = WspEnvelope.LoadpullUnstable(
                WspEnvelope.Loadpull(wA, freqs, idxS, idxL, P, gS, gL, 50.0));

            int ns = gS.Length, nl = gL.Length, encircling = 0, flagged = 0;
            double worst = double.PositiveInfinity, spread = 0.0;
            for (int s = 0; s < ns; s++)
                for (int l = 0; l < nl; l++)
                {
                    if (Math.Abs(ndf.Encirclements[s, l]) >= 1.0)
                    {
                        encircling++;
                        // Wherever the NDF DOES encircle, the margin must have collapsed — that is
                        // the half of [E]'s comparison this fixture can still hold.
                        Assert.True(Db(env.SmEnv[s, l]) < -30.0,
                            $"the NDF encircles at {which} point {s},{l} but SMenv reads {Db(env.SmEnv[s, l]):F2} dB");
                    }
                    if (uns.Count(s, l) > 0) flagged++;
                    worst  = Math.Min(worst, env.SmEnv[s, l]);
                    spread = Math.Max(spread, Math.Abs(Db(env.SmEnv[s, l]) - Db(env.SmEnv[0, 0])));
                }
            output.WriteLine($"(i) {which} ρ = 0.9: {encircling} of {ns * nl} points encircle, " +
                             $"{flagged} flagged by Kurokawa; worst SMenv {Db(worst):F2} dB, " +
                             $"spread {spread:F2} dB across the circle");
            encirclingTotal += encircling;
        }

        output.WriteLine(
            "(i) RECORDED: no ρ = 0.9 termination reaches this circuit's instability, on either side. " +
            "The mode Rb damps is the ODD mode, and the odd mode sees both ports as virtual grounds — " +
            "so a source/load stability envelope is structurally blind to it. An instability a VSWR " +
            "sweep can expose has to live in a port-coupled path.");
        Assert.Equal(0, encirclingTotal);

        // ── The ρ ladder: MEASURED, not asserted ─────────────────────────────
        output.WriteLine("(i) ρ ladder (source side), measured:");
        foreach (double rho in new[] { 0.85, 0.874, 0.875, 0.90, 0.99 })
        {
            var g   = WspEnvelope.CircleGrid(rho, 18);
            var env = WspEnvelope.LoadpullMargin(wA, freqs, S, 0, P, g, single, 50.0);
            var ndf = WspEnvelope.LoadpullNdf(wA, wP, freqs, S, 0, null, g, single, 50.0);
            int enc = 0; double best = double.MaxValue, bestHz = double.NaN;
            for (int s = 0; s < g.Length; s++)
            {
                if (Math.Abs(ndf.Encirclements[s, 0]) >= 1.0) enc++;
                if (env.SmEnv[s, 0] < best) { best = env.SmEnv[s, 0]; bestHz = env.SmEnvHz[s, 0]; }
            }
            output.WriteLine($"(i)   ρ = {rho:F3}: {enc,2} encircling point(s); worst SMenv " +
                             $"{Db(best):F2} dB at {bestHz / 1e9:G5} GHz");
        }
    }

    /// <summary><see cref="Math.Round(double, MidpointRounding)"/> hands back −0 for a small
    /// negative; a count of zero should print as one.</summary>
    private static double Round0(double v)
    {
        double r = Math.Round(v, MidpointRounding.AwayFromZero);
        return r == 0.0 ? 0.0 : r;
    }

    /// <summary>The Type-A fixture passivated: both transconductances scaled to zero — WSP-6's
    /// <c>NDFgm</c> pattern, written into the netlist text.</summary>
    private static string PassivateOhtomo(string cnl) => cnl.Replace("G=0.40", "G=0");

    // ══ (k) — D-13, and the threshold knob ═══════════════════════════════════

    /// <summary>
    /// Gate (k). A run without probes is byte-identical to what it always was; a probed run with
    /// <c>MarginThreshold=none</c> emits no margin note; the default emits it for the −5 Ω fixture
    /// and stays silent on a fixture whose margin never falls below −15 dB.
    /// </summary>
    [Fact]
    public void K_TheMarginNote_FiresOnlyBelowTheThreshold_AndAnUnprobedRunIsUnchanged()
    {
        // A run with no probe carries no margin cube and no note at all.
        string noProbe = File.ReadAllText(Fixture("margin_split_resonator.cnl"))
            .Replace("WSProbe:P   nG nL", "R:RSHORT    nG nL  R=1e-12 Ohm");
        var (plain, _, plainNl, _, _) = RunText(noProbe, Path.GetDirectoryName(Fixture("margin_split_resonator.cnl"))!);
        Assert.False(plain.Contains("wsp"));
        Assert.DoesNotContain(plainNl.Notes, n => n.Contains("stability margin"));

        // The default fires on the −5 Ω split resonator (its minimum is −18.1 dB).
        var (_, _, nl, _, _) = RunFixture("margin_split_resonator.cnl");
        string note = Assert.Single(nl.Notes, n => n.Contains("stability margin"));
        output.WriteLine("(k) " + note);
        Assert.Contains("WSProbe 'P'", note);
        Assert.Contains("SM_Y0", note);
        Assert.Contains("SM_H0", note);
        Assert.Contains("1.59125 GHz", note);
        Assert.Contains("negative resistance", note);

        // MarginThreshold=none is silent — and still produces the cubes.
        var (quietDs, _, quietNl, _, _) = RunFixture(
            "margin_split_resonator.cnl", AnalysisSettings.Default.WithMarginThreshold(null));
        Assert.DoesNotContain(quietNl.Notes, n => n.Contains("stability margin"));
        Assert.True(quietDs.Contains("SM_Y0:P"));

        // A fixture whose margin never falls that far stays silent at the default.
        var (_, _, calmNl, _, _) = RunFixture("derived_metrics.cnl");
        Assert.DoesNotContain(calmNl.Notes, n => n.Contains("stability margin"));

        // …and the directive is what sets it: MarginThreshold=none on the line reaches the engine.
        string quiet = File.ReadAllText(Fixture("margin_split_resonator.cnl"))
            .Replace("npts=2001 Unit=GHz", "npts=2001 Unit=GHz MarginThreshold=none");
        var (_, _, viaDirective, _, _) = RunText(quiet, Path.GetDirectoryName(Fixture("margin_split_resonator.cnl"))!);
        Assert.DoesNotContain(viaDirective.Notes, n => n.Contains("stability margin"));

        // A threshold the fixture clears turns it back on with the caller's own number in it.
        string strict = File.ReadAllText(Fixture("margin_split_resonator.cnl"))
            .Replace("npts=2001 Unit=GHz", "npts=2001 Unit=GHz MarginThreshold=-30");
        var (_, _, strictNl, _, _) = RunText(strict, Path.GetDirectoryName(Fixture("margin_split_resonator.cnl"))!);
        Assert.DoesNotContain(strictNl.Notes, n => n.Contains("stability margin"));
    }

    // ── Local helpers ───────────────────────────────────────────────────────

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

    /// <summary>WSP-6's <c>NDFgm</c> pattern written into the netlist text: every controlled source
    /// scaled to zero, which is what "passivated" means for this fixture.</summary>
    private static string Passivate(string cnl)
        => cnl.Replace("G=0.04", "G=0").Replace("G=0.05", "G=0");

    private static string ComplexParam(Complex z)
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"complex({z.Real:R},{z.Imaginary:R})");

    // The oracle of gate (f) reads ZG/ZL of the SOURCE and LOAD probes only, to find the starting
    // admittances — a quantity the rank-1 route also needs and which is not what the gate compares.
    private static Complex WspReductionZG(Complex[,] w, int idx) => WspReduction.ZG(WspProbeQuad.Of(w, idx));
    private static Complex WspReductionZL(Complex[,] w, int idx) => WspReduction.ZL(WspProbeQuad.Of(w, idx));

    private static Complex[,] Transpose(Complex[,] a)
    {
        int n = a.GetLength(0), m = a.GetLength(1);
        var t = new Complex[m, n];
        for (int i = 0; i < n; i++) for (int j = 0; j < m; j++) t[j, i] = a[i, j];
        return t;
    }

    private static Complex[,] MatMul(Complex[,] a, Complex[,] b)
    {
        int n = a.GetLength(0), k = a.GetLength(1), m = b.GetLength(1);
        var c = new Complex[n, m];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
            {
                var s = Complex.Zero;
                for (int q = 0; q < k; q++) s += a[i, q] * b[q, j];
                c[i, j] = s;
            }
        return c;
    }

    private static Complex[,] Sub(Complex[,] a, Complex[,] b)
    {
        int n = a.GetLength(0), m = a.GetLength(1);
        var c = new Complex[n, m];
        for (int i = 0; i < n; i++) for (int j = 0; j < m; j++) c[i, j] = a[i, j] - b[i, j];
        return c;
    }

    /// <summary>Gauss–Jordan with partial pivoting — written here rather than borrowed, because the
    /// oracle must not share an implementation with what it is checking.</summary>
    private static Complex[,] Invert(Complex[,] a)
    {
        int n = a.GetLength(0);
        var m = new Complex[n, 2 * n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) m[i, j] = a[i, j];
            m[i, n + i] = Complex.One;
        }
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (m[r, col].Magnitude > m[piv, col].Magnitude) piv = r;
            if (piv != col)
                for (int j = 0; j < 2 * n; j++) (m[col, j], m[piv, j]) = (m[piv, j], m[col, j]);

            var d = m[col, col];
            for (int j = 0; j < 2 * n; j++) m[col, j] /= d;
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                var f = m[r, col];
                if (f == Complex.Zero) continue;
                for (int j = 0; j < 2 * n; j++) m[r, j] -= f * m[col, j];
            }
        }
        var inv = new Complex[n, n];
        for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) inv[i, j] = m[i, n + j];
        return inv;
    }
}
