// ================================================================
//  WSProbeTests.cs — brief-wsprobe-1's gates (R-wsp1-14 (a)–(n)).
//
//  The WSProbe (T. A. Winslow, General Circuit Analysis Using The WSProbe, 2023) is a 0 V series
//  element the S-parameter engine injects through; its output is the `wsp` matrix (§4.1, Eq. 31–36)
//  and, from it, the six default per-probe outputs (Fig. 13). Every fixture here is analytic —
//  the document's own closed-form resonators (Eq. 109–128), the pure-feedback capacitor (Eq. 87),
//  the zero-feedback cascade (Eq. 67/76/89) — or an identity that must hold on any network with
//  feedback around the probe (Eq. 49, 60, 69, 76–77, 93–95). Nothing is timed; the one cost gate
//  is a COUNT (R-wsp1-14(l), overview D-14).
// ================================================================

using System.Numerics;
using CircuitRF.Core.Design;
using CircuitRF.Core.Elaboration;
using CircuitRF.Core.Netlist;
using CircuitRF.Engine;
using CircuitRF.Engine.HarmonicBalance;
using NumFlat;
using RfCore;
using RfCore.Data;
using RfCore.Stability;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Linear;

public sealed class WSProbeTests(ITestOutputHelper output)
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

    private static (Library Lib, TestBench Tb, ElaboratedNetlist Nl) Load(string cnlPath)
    {
        var (lib, tb) = CnlReader.ReadFile(cnlPath);
        return (lib, tb, new Elaborator(lib).Elaborate(tb));
    }

    private static double[] DeclaredFreqs(TestBench tb, ElaboratedNetlist nl)
        => tb.Analyses.OfType<SParameterAnalysis>().First().Expand(nl.ResolvedGlobals);

    private static (DataSet Ds, double[] Freqs, ElaboratedNetlist Nl) RunFixture(string name)
    {
        var (_, tb, nl) = Load(Fixture(name));
        var freqs = DeclaredFreqs(tb, nl);
        return (SParameterEngine.Run(nl, freqs), freqs, nl);
    }

    /// <summary>The document's <c>wsp(r, c)</c> at frequency index <paramref name="fi"/> — r, c
    /// 1-based, exactly as the document writes them.</summary>
    private static Complex Wsp(DataSet ds, int fi, int r, int c) => (Complex)ds["wsp"][fi, r - 1, c - 1];

    private static WspProbeQuad Quad(DataSet ds, int fi, int idx)
        => new(A: Wsp(ds, fi, 2 * idx - 1, 2 * idx),
               B: Wsp(ds, fi, 2 * idx - 1, 2 * idx - 1),
               C: Wsp(ds, fi, 2 * idx,     2 * idx),
               D: Wsp(ds, fi, 2 * idx,     2 * idx - 1));

    private static Complex Cube(DataSet ds, string name, int fi) => (Complex)ds[name][fi];

    private static void AssertRel(Complex expected, Complex actual, double relTol, string what)
    {
        double scale = Math.Max(expected.Magnitude, 1e-300);
        double err   = (actual - expected).Magnitude / scale;
        Assert.True(err <= relTol,
            $"{what}: expected {expected}, got {actual} (relative error {err:G3} > {relTol:G1})");
    }

    private static void AssertAbs(Complex expected, Complex actual, double absTol, string what)
    {
        double err = (actual - expected).Magnitude;
        Assert.True(err <= absTol,
            $"{what}: expected {expected}, got {actual} (|Δ| {err:G3} > {absTol:G1})");
    }

    private static Complex Par(Complex a, Complex b) => Complex.One / (Complex.One / a + Complex.One / b);

    // ══ (a) — the zero-feedback cascade, the sign anchor ═════════════════════

    /// <summary>
    /// Term1 (50 Ω) — L1 — [probe] — C2 ∥ R2 — ground, no path around the probe. All four wsp
    /// elements and the six outputs against the closed form, to 1e-12 relative at 50 frequencies.
    /// A wrong series-source sign fails vP/vS and ZG here and nowhere symmetric.
    /// </summary>
    [Fact]
    public void A_ZeroFeedbackCascade_MatchesClosedForm_AndPinsTheSeriesSourceSign()
    {
        var (ds, freqs, _) = RunFixture("cascade.cnl");
        Assert.Equal(50, freqs.Length);

        const double L1 = 2e-9, C2 = 1e-12, R2 = 100.0, Z0 = 50.0;
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var s  = new Complex(0, 2 * Math.PI * freqs[fi]);
            var zg = Z0 + s * L1;
            var zl = Complex.One / (s * C2 + 1.0 / R2);
            var y0 = Complex.One / (zg + zl);
            var h0 = Par(zg, zl);
            var a  = -zg / (zg + zl);                                   // vP/vS      (Eq. 67)
            var d  = (Complex.One / zl) / (Complex.One / zg + Complex.One / zl);   // iS/iP (Eq. 76)

            string at = $"f={freqs[fi] / 1e9:G4} GHz";
            AssertRel(y0, Wsp(ds, fi, 1, 1), 1e-12, $"wsp(1,1)=Y0 {at}");
            AssertRel(a,  Wsp(ds, fi, 1, 2), 1e-12, $"wsp(1,2)=vP/vS {at}");
            AssertRel(d,  Wsp(ds, fi, 2, 1), 1e-12, $"wsp(2,1)=iS/iP {at}");
            AssertRel(h0, Wsp(ds, fi, 2, 2), 1e-12, $"wsp(2,2)=H0 {at}");

            AssertRel(h0, Cube(ds, "H0:P", fi), 1e-12, $"H0 {at}");
            AssertRel(y0, Cube(ds, "Y0:P", fi), 1e-12, $"Y0 {at}");
            AssertRel(zg, Cube(ds, "ZG:P", fi), 1e-12, $"ZG {at}");
            AssertRel(zl, Cube(ds, "ZL:P", fi), 1e-12, $"ZL {at}");
            AssertAbs(Complex.Zero, Cube(ds, "LG:P", fi), 1e-12, $"LG {at}");
            AssertRel(Complex.One,  Cube(ds, "F:P",  fi), 1e-12, $"F {at}");

            // Eq. 89: with no feedback, ZG = 1/y11 and ZL = 1/y22, and y12 = y21 = 0.
            var y = WspReduction.YParam(Quad(ds, fi, 1));
            AssertAbs(Complex.Zero, y.Y12, 1e-12 / zg.Magnitude, $"y12 {at}");
            AssertAbs(Complex.Zero, y.Y21, 1e-12 / zl.Magnitude, $"y21 {at}");
            AssertRel(zg, Complex.One / y.Y11, 1e-11, $"1/y11 {at}");
            AssertRel(zl, Complex.One / y.Y22, 1e-11, $"1/y22 {at}");
        }

        // The DataSet layout of R-wsp1-10.
        var wsp = ds["wsp"];
        Assert.Equal(["freq", "row", "col"], wsp.Axes.Select(a => a.Name).ToArray());
        Assert.Equal([1.0, 2.0], wsp.Axes[1].Values);
        Assert.Equal([1.0, 2.0], wsp.Axes[2].Values);
        Assert.Equal("Hz", wsp.Axes[0].Unit);
        Assert.Equal("", wsp.Axes[1].Unit);
        var meta = ds["__WspProbes"];
        Assert.Equal(["P"], meta.Axes[0].Labels!);
        Assert.Equal([1.0], meta.RealValues);
        Assert.True(ds.Contains("S"), "a run with a port still carries S");
    }

    // ══ (b) — the series unstable resonator (Fig. 31, Eq. 109–118) ═══════════

    [Fact]
    public void B_SeriesUnstableResonator_ClosedForm_AndKurokawaSignatureOnOneOverY0Only()
    {
        var (ds, freqs, nl) = RunFixture("series_resonator.cnl");
        Assert.False(ds.Contains("S"), "R-wsp1-6: no ports ⇒ no S cube");
        Assert.False(ds.Contains("Z0"), "R-wsp1-6: no ports ⇒ no Z0 cube");
        Assert.Equal(1, Assert.Single(nl.WspProbes).Idx);

        const double L1 = 1e-9, C1 = 10e-12, RS = 10.0, R1 = -20.0;
        double f0 = 1.0 / (2 * Math.PI * Math.Sqrt(L1 * C1));
        Assert.InRange(f0, 1.59e9, 1.60e9);

        var invY0 = new Complex[freqs.Length];
        var invH0 = new Complex[freqs.Length];
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var s   = new Complex(0, 2 * Math.PI * freqs[fi]);
            var den = C1 * L1 * s * s + C1 * (R1 + RS) * s + 1.0;
            var y0  = s * C1 / den;                                              // Eq. 109
            var h0  = (RS * C1 * L1 * s * s + C1 * R1 * RS * s + RS) / den;      // Eq. 110
            string at = $"f={freqs[fi] / 1e9:G4} GHz";
            AssertRel(y0, Cube(ds, "Y0:P", fi), 1e-10, $"Y0 {at}");
            AssertRel(h0, Cube(ds, "H0:P", fi), 1e-10, $"H0 {at}");
            invY0[fi] = Complex.One / Cube(ds, "Y0:P", fi);
            invH0[fi] = Complex.One / Cube(ds, "H0:P", fi);
        }

        // Kurokawa (Eq. 105–108): 1/Y0 crosses the negative real axis clockwise at f0; 1/H0 never
        // crosses it at all (§4.10 p. 76–77 — the zero of H0 masks the pole).
        var cross = NegativeRealAxisCrossings(invY0, freqs);
        Assert.Single(cross);
        Assert.InRange(cross[0], f0 - (freqs[1] - freqs[0]), f0 + (freqs[1] - freqs[0]));
        Assert.Empty(NegativeRealAxisCrossings(invH0, freqs));
    }

    // ══ (c) — the parallel unstable resonator (Fig. 34, Eq. 119–128) ═════════

    [Fact]
    public void C_ParallelUnstableResonator_ClosedForm_AndKurokawaSignatureOnOneOverH0Only()
    {
        var (ds, freqs, _) = RunFixture("parallel_resonator.cnl");

        // R1 = −5 Ω, not the series fixture's −20 (see the fixture header and RESOLVED.md): a
        // parallel negative resistance starts up only when |R1| < RS.
        const double L1 = 1e-9, C1 = 10e-12, RS = 10.0, R1 = -5.0;
        double f0 = 1.0 / (2 * Math.PI * Math.Sqrt(L1 * C1));

        var invY0 = new Complex[freqs.Length];
        var invH0 = new Complex[freqs.Length];
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var s   = new Complex(0, 2 * Math.PI * freqs[fi]);
            var den = s * s * C1 * L1 * R1 * RS + s * L1 * (R1 + RS) + R1 * RS;
            var h0  = s * L1 * R1 * RS / den;                                    // Eq. 119
            var y0  = (R1 + s * L1 + s * s * R1 * L1 * C1) / den;                // Eq. 120
            string at = $"f={freqs[fi] / 1e9:G4} GHz";
            AssertRel(h0, Cube(ds, "H0:P", fi), 1e-10, $"H0 {at}");
            AssertRel(y0, Cube(ds, "Y0:P", fi), 1e-10, $"Y0 {at}");
            invY0[fi] = Complex.One / Cube(ds, "Y0:P", fi);
            invH0[fi] = Complex.One / Cube(ds, "H0:P", fi);
        }

        var cross = NegativeRealAxisCrossings(invH0, freqs);
        Assert.Single(cross);
        Assert.InRange(cross[0], f0 - (freqs[1] - freqs[0]), f0 + (freqs[1] - freqs[0]));
        Assert.Empty(NegativeRealAxisCrossings(invY0, freqs));
    }

    /// <summary>The frequencies at which a sampled locus crosses the NEGATIVE real axis
    /// CLOCKWISE — Re &lt; 0 on both sides and Im going from negative to positive with increasing ω
    /// (∂Im/∂ω &gt; 0, Eq. 108). The three conditions asserted directly on the samples; the search
    /// itself is WSP-2's.</summary>
    private static List<double> NegativeRealAxisCrossings(Complex[] locus, double[] freqs)
    {
        var found = new List<double>();
        for (int i = 1; i < locus.Length; i++)
        {
            bool signChange = locus[i - 1].Imaginary < 0 && locus[i].Imaginary >= 0;
            bool negativeRe = locus[i - 1].Real < 0 && locus[i].Real < 0;
            if (signChange && negativeRe) found.Add(0.5 * (freqs[i - 1] + freqs[i]));
        }
        return found;
    }

    // ══ (d) — pure feedback (Fig. 25, Eq. 85–87, symmetric case only) ════════

    [Fact]
    public void D_PureFeedbackCapacitor_SymmetricCase_GivesCGequalsCLequals2C()
    {
        var (ds, freqs, _) = RunFixture("feedback_cap.cnl");
        const double G = 1e-9, C = 1e-12;
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var jw = new Complex(0, 2 * Math.PI * freqs[fi]);
            var z  = Complex.One / (G + 2.0 * jw * C);                 // Eq. 87: CG = CL = 2C
            string at = $"f={freqs[fi] / 1e9:G4} GHz";
            AssertRel(z, Cube(ds, "ZG:P", fi), 1e-9, $"ZG {at}");
            AssertRel(z, Cube(ds, "ZL:P", fi), 1e-9, $"ZL {at}");
            AssertRel(Complex.One / Cube(ds, "Y0:P", fi),
                      Cube(ds, "ZG:P", fi) + Cube(ds, "ZL:P", fi), 1e-9, $"ZG + ZL = 1/Y0 {at}");
        }
    }

    // ══ (e) — identities on a network with feedback around the probe ═════════

    [Fact]
    public void E_Hero1WithTwoProbes_EveryIdentityOfSection4Holds_AndFeedbackIsVisible()
    {
        var (ds, freqs, nl) = RunFixture("hero1_probed.cnl");
        Assert.Equal(2, nl.WspProbes.Count);
        Assert.Equal(("P1", 1), (nl.WspProbes[0].Label, nl.WspProbes[0].Idx));
        Assert.Equal(("P2", 2), (nl.WspProbes[1].Label, nl.WspProbes[1].Idx));

        const double tol = 1e-11;
        double maxZgVsInvYg = 0;
        foreach (var probe in nl.WspProbes)
        {
            for (int fi = 0; fi < freqs.Length; fi++)
            {
                var q = Quad(ds, fi, probe.Idx);
                var y = WspReduction.YParam(q);
                var z = WspReduction.ZParam(q);
                string at = $"{probe.Label} f={freqs[fi] / 1e9:G4} GHz";

                // Eq. 49: [Y]·[Z] = I.
                AssertAbs(Complex.One,  y.Y11 * z.Z11 + y.Y12 * z.Z21, tol, $"(YZ)11 {at}");
                AssertAbs(Complex.Zero, y.Y11 * z.Z12 + y.Y12 * z.Z22, tol, $"(YZ)12 {at}");
                AssertAbs(Complex.Zero, y.Y21 * z.Z11 + y.Y22 * z.Z21, tol, $"(YZ)21 {at}");
                AssertAbs(Complex.One,  y.Y21 * z.Z12 + y.Y22 * z.Z22, tol, $"(YZ)22 {at}");

                var h0 = Cube(ds, $"H0:{probe.Label}", fi);
                var y0 = Cube(ds, $"Y0:{probe.Label}", fi);
                var zg = Cube(ds, $"ZG:{probe.Label}", fi);
                var zl = Cube(ds, $"ZL:{probe.Label}", fi);
                var lg = Cube(ds, $"LG:{probe.Label}", fi);
                var yg = WspReduction.YG(y);
                var yl = WspReduction.YL(y);

                AssertRel(Complex.One / y0, zg + zl, tol, $"ZG + ZL = 1/Y0 (Eq. 69) {at}");
                AssertRel(Complex.One / h0, yg + yl, tol, $"YG + YL = 1/H0 (Eq. 76–77) {at}");

                var zop = Complex.One / (y.Y11 + y.Y22);
                var yop = Complex.One / (z.Z11 + z.Z22);
                AssertRel(Complex.One / h0, (Complex.One - lg) / zop, tol, $"1/H0 = (1−LG)/Zop (Eq. 93) {at}");
                AssertRel(Complex.One / y0, (Complex.One - lg) / yop, tol, $"1/Y0 = (1−LG)/Yop (Eq. 93) {at}");
                AssertRel(zop / h0, yop / y0, tol, $"Zop/H0 = Yop/Y0 (Eq. 94) {at}");

                var detY = y.Y11 * y.Y22 - y.Y12 * y.Y21;
                AssertRel(yg + yl, detY * (zg + zl), tol, $"YG + YL = |Y|·(ZG + ZL) (Eq. 95) {at}");

                AssertRel(WspReduction.LoopGain(y), WspReduction.LoopGain(z), tol, $"LG from [Y] = LG from [Z] (Eq. 60) {at}");
                AssertRel(lg, WspReduction.LoopGain(y), tol, $"LG cube = LG from [Y] {at}");

                // The Y-form oracles of Eq. 65/66 agree with the shipped Z form.
                AssertRel(zg, WspReduction.ZGFromY(y), 1e-9, $"ZG Z-form vs Y-form {at}");
                AssertRel(zl, WspReduction.ZLFromY(y), 1e-9, $"ZL Z-form vs Y-form {at}");

                // Eq. 79: ZG ≠ 1/YG in general. Record the largest relative difference.
                double diff = (zg - Complex.One / yg).Magnitude / zg.Magnitude;
                if (diff > maxZgVsInvYg) maxZgVsInvYg = diff;
            }
        }
        output.WriteLine($"max |ZG − 1/YG| / |ZG| over both probes: {maxZgVsInvYg:G4}");
        Assert.True(maxZgVsInvYg > 1e-6,
            "ZG equalled 1/YG everywhere — the feedback around the probe (Eq. 79) is not visible, " +
            "which means the fixture has no feedback or the reduction lost it.");
    }

    // ══ (f) — non-perturbation ═══════════════════════════════════════════════

    [Fact]
    public void F_TwoProbesInHero1_LeaveItsSUntouched_AndTheGoldenGateStillHolds()
    {
        var (_, tbP, nlP) = Load(Fixture("hero1_probed.cnl"));
        var (_, _,   nlU) = Load(TestData("Hero1", "hero1.cnl"));
        var freqs = DeclaredFreqs(tbP, nlP);
        Assert.Equal(41, freqs.Length);

        var dsP = SParameterEngine.Run(nlP, freqs);
        var dsU = SParameterEngine.Run(nlU, freqs);

        var sP = dsP["S"].ComplexValues;
        var sU = dsU["S"].ComplexValues;
        Assert.Equal(sU.Length, sP.Length);
        double maxDiff = 0;
        for (int k = 0; k < sP.Length; k++) maxDiff = Math.Max(maxDiff, (sP[k] - sU[k]).Magnitude);
        output.WriteLine($"max |S_probed − S_unprobed| = {maxDiff:G3}");
        Assert.True(maxDiff < 1e-12, $"max |S_probed − S_unprobed| = {maxDiff:G3} ≥ 1e-12");

        // Hero1Tests' own gate, on the probed netlist.
        var refRaw = TouchstoneIO.ReadFile(TestData("Hero1", "hero1_golden_result.s4p"));
        var refSnp = RFNetwork.Interpolate(refRaw, freqs, InterpolationMethod.Linear,
                        InterpolationFormat.RealImag, MatrixType.S, OutOfRangePolicy.WarnClamp);
        double maxGolden = 0;
        for (int fi = 0; fi < freqs.Length; fi++)
        for (int r = 0; r < 4; r++)
        for (int c = 0; c < 4; c++)
            maxGolden = Math.Max(maxGolden, ((Complex)dsP["S"][fi, r, c] - refSnp.Matrices[fi][r, c]).Magnitude);
        Assert.True(maxGolden < 1e-6, $"golden gate on the probed netlist: {maxGolden:G3} ≥ 1e-6");
    }

    // ══ (g) — cross terms and orientation ════════════════════════════════════

    /// <summary>
    /// wsp(2, 4) — the voltage at P2 per unit current into P1 — equals wsp(4, 2) only on a
    /// reciprocal network. With Hero 1's non-reciprocal .s2p in the path they differ; with that
    /// file replaced by its reciprocal part (S + Sᵀ)/2 they agree. A stimulus-major/response-major
    /// mix-up passes the second half and fails the first.
    /// </summary>
    [Fact]
    public void G_CrossTerms_DifferOnTheNonReciprocalNetwork_AndAgreeOnItsReciprocalPart()
    {
        var (dsN, freqs, _) = RunFixture("hero1_probed.cnl");
        double maxRelDiff = 0;
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var a = Wsp(dsN, fi, 2, 4);
            var b = Wsp(dsN, fi, 4, 2);
            maxRelDiff = Math.Max(maxRelDiff, (a - b).Magnitude / Math.Max(a.Magnitude, b.Magnitude));
        }
        output.WriteLine($"non-reciprocal: max |wsp(2,4) − wsp(4,2)| / |wsp| = {maxRelDiff:G3}");
        Assert.True(maxRelDiff > 1e-6, "the cross terms agreed on a NON-reciprocal network");

        // The reciprocal part of the .s2p, written to a temp file the fixture is re-pointed at.
        string tmp = Path.Combine(Path.GetTempPath(), "crf-wsp1-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            var raw  = TouchstoneIO.ReadFile(TestData("Hero1", "potentially_unstable_amp.s2p"));
            var mats = new Mat<Complex>[raw.FrequencyCount];
            for (int fi = 0; fi < raw.FrequencyCount; fi++)
            {
                var m = raw.Matrices[fi];
                var r = new Mat<Complex>(2, 2);
                for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                    r[i, j] = 0.5 * (m[i, j] + m[j, i]);
                mats[fi] = r;
            }
            string recPath = Path.Combine(tmp, "reciprocal.s2p");
            TouchstoneIO.WriteFile(new SNP(raw.Frequencies, mats, MatrixType.S, MatrixFormat.RI, raw.Z0), recPath);

            string text = File.ReadAllText(Fixture("hero1_probed.cnl"))
                .Replace("File=\"../Hero1/potentially_unstable_amp.s2p\"",
                         $"File=\"{recPath.Replace('\\', '/')}\"");
            var (lib, tb) = new CnlReader().Read(text);
            var nl  = new Elaborator(lib).Elaborate(tb);
            var dsR = SParameterEngine.Run(nl, freqs);

            for (int fi = 0; fi < freqs.Length; fi++)
                AssertRel(Wsp(dsR, fi, 2, 4), Wsp(dsR, fi, 4, 2), 1e-10,
                    $"reciprocal wsp(2,4) vs wsp(4,2) at f={freqs[fi] / 1e9:G4} GHz");
        }
        finally { try { Directory.Delete(tmp, true); } catch { /* best effort */ } }
    }

    // ══ (h) — sweep stacking ═════════════════════════════════════════════════

    [Fact]
    public void H_UnderAParametricSweep_WspAndPerProbeCubesStack_AndTheMetadataPassesThrough()
    {
        var (lib, tb, nl) = Load(Fixture("hero1_probed.cnl"));
        var unswept = SParameterEngine.Run(nl, DeclaredFreqs(tb, nl));

        var sw = new ParametricSweepAnalysis("SW", "R3v", [200.0, 300.0], "SP1");
        var ds = ParametricSweepEngine.Run(sw, lib, tb);

        Assert.Equal(["R3v", "freq", "row", "col"], ds["wsp"].Axes.Select(a => a.Name).ToArray());
        Assert.Equal([2, 41, 4, 4], ds["wsp"].Axes.Select(a => a.Length).ToArray());
        Assert.Equal(["R3v", "freq"], ds["ZG:P1"].Axes.Select(a => a.Name).ToArray());
        Assert.Equal(["probe"], ds["__WspProbes"].Axes.Select(a => a.Name).ToArray());
        Assert.Equal(unswept["__WspProbes"].RealValues, ds["__WspProbes"].RealValues);
        Assert.Equal(unswept["__WspProbes"].Axes[0].Labels, ds["__WspProbes"].Axes[0].Labels);

        // The first sweep point IS the unswept run (R3v = 200 is the fixture's own value).
        Assert.Equal(unswept["wsp"].ComplexValues, ((DataCube)ds["wsp"][0, .., .., ..]).ComplexValues);
    }

    // ══ (i) — frequency-parallel identity ════════════════════════════════════

    [Fact]
    public void I_ParallelRun_GivesABitIdenticalWsp()
    {
        string path = Fixture("hero1_probed.cnl");
        var freqs = Enumerable.Range(0, 256).Select(i => 1e9 + i * (2e9 / 255)).ToArray();

        var (lib1, tb1) = CnlReader.ReadFile(path);
        var serial = SParameterEngine.Run(new Elaborator(lib1).Elaborate(tb1), freqs);

        var (lib2, tb2) = CnlReader.ReadFile(path);
        using var nl2 = new Elaborator(lib2).Elaborate(tb2);
        Assert.True(SParameterEngine.PlanDegree(nl2, freqs.Length, 4) > 1, "the parallel path was not taken");
        var parallel = SParameterEngine.Run(nl2, lib2, tb2, Path.GetDirectoryName(path), freqs,
                           settings: null, control: null, maxDegreeOfParallelism: 4);

        foreach (string name in (string[])["wsp", "H0:P1", "Y0:P1", "ZG:P1", "ZL:P1", "LG:P1", "F:P1", "ZG:P2"])
        {
            var a = serial[name].ComplexValues;
            var b = parallel[name].ComplexValues;
            Assert.Equal(a.Length, b.Length);
            for (int k = 0; k < a.Length; k++)
            {
                Assert.Equal(BitConverter.DoubleToInt64Bits(a[k].Real),      BitConverter.DoubleToInt64Bits(b[k].Real));
                Assert.Equal(BitConverter.DoubleToInt64Bits(a[k].Imaginary), BitConverter.DoubleToInt64Bits(b[k].Imaginary));
            }
        }
        Assert.Equal(serial["__WspProbes"].RealValues, parallel["__WspProbes"].RealValues);
    }

    // ══ (j) — a biased nonlinear device ══════════════════════════════════════

    /// <summary>NonlinearSParamTests' T3 pattern: an SDD whose conductance depends on bias, a
    /// probe at its port; wsp equals that of the hand-linearised circuit to 1e-9.</summary>
    [Fact]
    public void J_BiasedSdd_ProbeSeesTheLinearisedConductance()
    {
        // I = 0.02·v + 0.01·v²  ⇒  G(3 V) = 0.02 + 2·0.01·3 = 0.08 S = 1/12.5 Ω.
        const string sdd = """
            Port:P1   n1   0     Num=1  Z=50 Ohm
            L:Lchoke  n1   n_dc  L=1e9
            Vdc:Vbias n_dc 0     Vdc=3.0
            WSProbe:W n1   nd
            SDD:D1    nd   0     I[1]=0.02*_v1+0.01*_v1^2
            """;
        const string lin = """
            Port:P1   n1   0     Num=1  Z=50 Ohm
            L:Lchoke  n1   n_dc  L=1e9
            Vdc:Vbias n_dc 0     Vdc=3.0
            WSProbe:W n1   nd
            R:Rlin    nd   0     R=12.5 Ohm
            """;
        double[] freqs = [0.5e9, 1e9, 2e9, 5e9];
        var dsS = Run(sdd, freqs);
        var dsL = Run(lin, freqs);

        var a = dsS["wsp"].ComplexValues;
        var b = dsL["wsp"].ComplexValues;
        for (int k = 0; k < a.Length; k++)
            AssertRel(b[k], a[k], 1e-9, $"wsp element {k}");
        for (int fi = 0; fi < freqs.Length; fi++)
            AssertRel(new Complex(12.5, 0), Cube(dsS, "ZL:W", fi), 1e-9, $"ZL = 12.5 Ω at f={freqs[fi] / 1e9:G3} GHz");
    }

    private static DataSet Run(string cnl, double[] freqs)
    {
        var (lib, tb) = new CnlReader().Read(cnl);
        return SParameterEngine.Run(new Elaborator(lib).Elaborate(tb), freqs);
    }

    // ══ (k) — no-probe runs are untouched ════════════════════════════════════

    /// <summary>
    /// A netlist with no WSProbe produces exactly the cubes it always did — S and Z0, nothing
    /// else — does exactly the solves it always did (one back-substitution per port per
    /// frequency, one pattern build), and Hero 1's golden gate holds. The byte-for-byte comparison
    /// of the serialised .npy against the pre-change build was made once, by hand, and is recorded
    /// in src/Engine/RESOLVED.md; what a test can hold is the structure that makes it true.
    /// </summary>
    [Fact]
    public void K_NoProbe_ProducesOnlySAndZ0_AndDoesOnlyThePortSolves()
    {
        const string ladder = """
            Port:P1  n1 0  Num=1 Z=50 Ohm
            Port:P2  n3 0  Num=2 Z=50 Ohm
            L:L1     n1 n2 L=2.2 nH
            C:C1     n2 0  C=1.4 pF
            L:L2     n2 n3 L=3.3 nH
            C:C2     n3 0  C=0.9 pF
            R:R1     n3 0  R=180 Ohm
            """;
        double[] freqs = Enumerable.Range(0, 41).Select(i => (1.0 + i * 0.05) * 1e9).ToArray();

        foreach (var (name, nl, ports) in new (string, ElaboratedNetlist, int)[]
        {
            ("Hero 1", Load(TestData("Hero1", "hero1.cnl")).Nl, 4),
            ("ladder", Elab(ladder), 2),
        })
        {
            var (ds, stats) = SParameterEngine.RunWithStats(nl, freqs);
            Assert.Equal(["S", "Z0"], ds.Cubes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
            Assert.Equal(freqs.Length,         stats.Factorizations);
            Assert.Equal(freqs.Length * ports, stats.BackSubstitutions);
            Assert.Equal(1,                    stats.PatternBuilds);
            output.WriteLine($"{name}: {stats}");
        }
    }

    private static ElaboratedNetlist Elab(string cnl)
    {
        var (lib, tb) = new CnlReader().Read(cnl);
        return new Elaborator(lib).Elaborate(tb);
    }

    // ══ (l) — counters, not timings ══════════════════════════════════════════

    [Fact]
    public void L_TwoProbesAt100Frequencies_Cost100Factorizations_And100TimesPortsPlusFourSolves()
    {
        var (_, _, nl) = Load(Fixture("hero1_probed.cnl"));
        var freqs = Enumerable.Range(0, 100).Select(i => 1e9 + i * 2e7).ToArray();
        var (_, stats) = SParameterEngine.RunWithStats(nl, freqs);
        Assert.Equal(100,           stats.Factorizations);
        Assert.Equal(100 * (4 + 4), stats.BackSubstitutions);   // N_ports + 2·N_probes
        Assert.Equal(1,             stats.PatternBuilds);
    }

    // ══ (m) — transparency to DC and HB ══════════════════════════════════════

    /// <summary>
    /// A WSProbe in hero2's drain wire: the HB V and I cubes are bit-identical to the run with an
    /// IProbe of the same name in the same place, and the DC packer reports the same I:&lt;label&gt;.
    /// Against the unprobed run the drain voltage agrees to 1e-9 relative (the probe adds a node,
    /// so the matrices are not the same object and bit identity is not the claim there).
    /// </summary>
    [Fact]
    public void M_InHarmonicBalanceAndDc_AWSProbeReportsExactlyWhatAnIProbeDoes()
    {
        string hero2Dir = TestData("Hero2");
        string text     = File.ReadAllText(Path.Combine(hero2Dir, "hero2.cnl"));
        const string drainLine = "C:Cblock_d       n_drain n_zl   C=1 uF";
        Assert.Contains(drainLine, text);

        string wsText = text.Replace(drainLine, "WSProbe:PD n_drain n_dw\nC:Cblock_d n_dw n_zl C=1 uF");
        string ipText = text.Replace(drainLine, "IProbe:PD  n_drain n_dw\nC:Cblock_d n_dw n_zl C=1 uF");

        var (hbW, dcW) = RunHero2(wsText, hero2Dir);
        var (hbI, dcI) = RunHero2(ipText, hero2Dir);
        var (hbU, _)   = RunHero2(text,   hero2Dir);

        foreach (string cube in (string[])["V", "I"])
        {
            Assert.Equal(hbI[cube].Axes.Select(a => a.Name), hbW[cube].Axes.Select(a => a.Name));
            Assert.Equal(hbI[cube].Axes[0].Labels, hbW[cube].Axes[0].Labels);
            var a = hbW[cube].ComplexValues; var b = hbI[cube].ComplexValues;
            Assert.Equal(b.Length, a.Length);
            for (int k = 0; k < a.Length; k++)
            {
                Assert.Equal(BitConverter.DoubleToInt64Bits(b[k].Real),      BitConverter.DoubleToInt64Bits(a[k].Real));
                Assert.Equal(BitConverter.DoubleToInt64Bits(b[k].Imaginary), BitConverter.DoubleToInt64Bits(a[k].Imaginary));
            }
        }
        Assert.Contains("PD", hbW["I"].Axes[0].Labels!);
        Assert.Equal(hbI["__ProbeBranches"].Axes[0].Labels, hbW["__ProbeBranches"].Axes[0].Labels);

        // Against the unprobed run: the drain fundamental to 1e-9 relative.
        int dW = Array.IndexOf(hbW["V"].Axes[0].Labels!, "n_drain");
        int dU = Array.IndexOf(hbU["V"].Axes[0].Labels!, "n_drain");
        Assert.True(dW >= 0 && dU >= 0);
        AssertRel((Complex)hbU["V"][dU, 1], (Complex)hbW["V"][dW, 1], 1e-9, "V(n_drain, 1) probed vs unprobed");

        // DC: the packer's I cube carries the WSProbe under its label, equal to the IProbe's.
        Assert.True(dcW.Contains("I"));
        Assert.Equal(dcI["I"].Axes[0].Labels, dcW["I"].Axes[0].Labels);
        Assert.Equal(dcI["I"].RealValues,     dcW["I"].RealValues);
        Assert.Contains("PD", dcW["I"].Axes[0].Labels!);
    }

    private static (DataSet Hb, DataSet Dc) RunHero2(string cnlText, string baseDir)
    {
        var (lib, tb) = new CnlReader().Read(cnlText, "tb", baseDir);
        var nl  = new Elaborator(lib) { BaseDirectory = baseDir }.Elaborate(tb);
        var hba = tb.Analyses.OfType<HarmonicBalanceAnalysis>().First();
        var p   = HbEngine.Resolve(hba, nl.ResolvedGlobals, nl.GlobalsWithExplicitUnit);
        var hb  = new HbEngine(nl, tb).Run(p).DataSet;
        var dc  = DcResultPacker.Pack(NonlinearDcEngine.Run(nl, AnalysisSettings.Default), nl);
        return (hb, dc);
    }

    // ══ (n) — measurement accessors ══════════════════════════════════════════

    [Fact]
    public void N_MeasureLines_ReachTheProbeCubesThroughTheEvaluator()
    {
        var (_, tb, nl) = Load(Fixture("hero1_probed.cnl"));
        var freqs = DeclaredFreqs(tb, nl);
        var ds    = SParameterEngine.Run(nl, freqs);

        var measDs = new DataSet();
        var errors = new MeasurementEvaluator(tb, nl,
            new Dictionary<string, DataSet>(StringComparer.OrdinalIgnoreCase) { ["SP1"] = ds }).EvaluateInto(measDs);
        Assert.Empty(errors);

        Assert.Equal(ds["ZG:P1"].ComplexValues, measDs["ZGg"].ComplexValues);          // SP1.ZG("P1")
        Assert.Equal(ds["H0:P1"].ComplexValues, measDs["h"].ComplexValues);            // SP1.wsp(2, 2) = H0 of probe 1
        Assert.Equal(["freq"], measDs["h"].Axes.Select(a => a.Name).ToArray());
        Assert.Equal(2.0, measDs["ix"].RealValues.Single());                           // SP1.idx("P2")

        // An unknown label is an error that lists the probes present.
        var bad = new TestBench("tb");
        bad.Measurements.Add(new Measurement("x", "SP1.ZG(\"GATE\")", ""));
        var badErrors = new MeasurementEvaluator(bad, nl,
            new Dictionary<string, DataSet>(StringComparer.OrdinalIgnoreCase) { ["SP1"] = ds }).EvaluateInto(new DataSet());
        var msg = Assert.Single(badErrors);
        Assert.Contains("GATE", msg);
        Assert.Contains("P1", msg);
        Assert.Contains("P2", msg);
    }
}
