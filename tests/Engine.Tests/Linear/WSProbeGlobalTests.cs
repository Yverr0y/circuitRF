// ================================================================
//  WSProbeGlobalTests.cs — brief-wsprobe-3's end-to-end gates (R-wsp3-9 (a)–(h)) on real circuits.
//
//    (a) the probe-pair reduction on a constructed two-block circuit, and the residual that says
//        when two probes do NOT bracket a two-port;
//    (b) bifurcation sides and transposes on a non-reciprocal three-probe fixture;
//    (c) wsp_ymatrix against the engine's own S → Y at Terms placed on the same nodes, and against
//        the closed form Y_G + Y_L;
//    (d) Ohtomo: the telescoping identity, the reversed-list invariance, N = 1 on the document's
//        series resonator with the loop-gain search;
//    (e) the envelope against brute force — a rank-1 update of the 50 Ω run versus a re-run with the
//        Terms changed — then Eq. 191's cofactor form, then the refusal;
//    (f) wsp_loadpull_unstable on the series resonator against the closed form of Eq. 109;
//    (g) wsp_block_calc self-consistency on a real block;
//    (h) the design helpers against wsp_zo_renorm_s.
//
//  The scalar cores are gated on synthetic wsp matrices in tests/RfCore.Tests/Stability/
//  WspGlobalTests.cs; nothing here re-derives them.
//
//  Reference: T. A. Winslow, General Circuit Analysis Using The WSProbe (2023).
// ================================================================

using System.Numerics;
using CircuitRF.Core.Design;
using CircuitRF.Core.Elaboration;
using CircuitRF.Core.Netlist;
using NumFlat;
using RfCore;
using RfCore.Data;
using RfCore.Stability;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Linear;

public sealed class WSProbeGlobalTests(ITestOutputHelper output)
{
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

    private static (DataSet Ds, double[] Freqs) RunText(string cnlText, string baseDir)
    {
        var (ds, freqs, _) = RunTextWithWarnings(cnlText, baseDir);
        return (ds, freqs);
    }

    private static (DataSet Ds, double[] Freqs, IReadOnlyList<string> Warnings) RunTextWithWarnings(string cnlText, string baseDir)
    {
        var (lib, tb) = new CnlReader().Read(cnlText, "tb", baseDir);
        var nl    = new Elaborator(lib) { BaseDirectory = baseDir }.Elaborate(tb);
        var freqs = tb.Analyses.OfType<SParameterAnalysis>().First().Expand(nl.ResolvedGlobals);
        var ds    = SParameterEngine.Run(nl, freqs);
        return (ds, freqs, nl.Warnings);
    }

    private static (DataSet Ds, double[] Freqs) RunFixture(string name)
        => RunText(File.ReadAllText(Fixture(name)), Path.GetDirectoryName(Fixture(name))!);

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

    private static void AssertRel(Complex expected, Complex actual, double tol, string what)
    {
        double scale = Math.Max(expected.Magnitude, 1e-300);
        double err   = (actual - expected).Magnitude / scale;
        Assert.True(err <= tol, $"{what}: expected {expected}, got {actual} (relative error {err:G3} > {tol:G1})");
    }

    /// <summary>For dimensionless quantities that can be exactly zero (a loop gain with no reverse
    /// transfer, an S12 of a unilateral block): relative to <c>max(|expected|, |actual|, 1)</c>.</summary>
    private static void AssertClose(Complex expected, Complex actual, double tol, string what)
    {
        double scale = Math.Max(Math.Max(expected.Magnitude, actual.Magnitude), 1.0);
        double err   = (actual - expected).Magnitude / scale;
        Assert.True(err <= tol, $"{what}: expected {expected}, got {actual} (error {err:G3} > {tol:G1})");
    }

    private static void AssertMatrix(Complex[,] expected, Complex[,] actual, double tol, string what)
    {
        int n = expected.GetLength(0), m = expected.GetLength(1);
        double scale = 1e-300;
        for (int i = 0; i < n; i++) for (int j = 0; j < m; j++) scale = Math.Max(scale, expected[i, j].Magnitude);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
            {
                double err = (actual[i, j] - expected[i, j]).Magnitude / scale;
                Assert.True(err <= tol, $"{what} [{i + 1},{j + 1}]: expected {expected[i, j]}, got {actual[i, j]} (rel {err:G3} > {tol:G1})");
            }
    }

    private static Complex Jw(double f) => new(0.0, 2 * Math.PI * f);

    // ── the hand-written matrices of the fixtures ────────────────────────────

    /// <summary>two_block.cnl's inner and feedback two-ports.</summary>
    private static (WspTwoPortY Y, WspTwoPortY Yf) TwoBlockHand(double f)
    {
        var s  = Jw(f);
        var y  = new WspTwoPortY(1.0 / 200 + s * 0.4e-12, 0.0, 0.03, 1.0 / 300 + s * 0.25e-12);
        var yS = Complex.One / (400.0 + Complex.One / (s * 0.2e-12));
        var yf = new WspTwoPortY(1.0 / 50 + yS, -yS, -yS, 1.0 / 75 + yS);
        return (y, yf);
    }

    /// <summary>three_probe.cnl's G-side (active, non-reciprocal) and L-side (passive) 3-ports.</summary>
    private static (Complex[,] YG, Complex[,] YL) ThreeProbeHand(double f)
    {
        var s = Jw(f);
        var yG = new Complex[3, 3];
        yG[0, 0] = 1.0 / 150 + s * 0.3e-12;
        yG[1, 1] = 1.0 / 250 + s * 0.5e-12;
        yG[2, 2] = 1.0 / 100 + s * 0.2e-12;
        yG[1, 0] = 0.02; yG[2, 1] = 0.015; yG[0, 2] = 0.004;

        var yL = new Complex[3, 3];
        Complex g12 = 1.0 / 300, y23 = s * 0.3e-12, y13 = Complex.One / (s * 3e-9);
        yL[0, 0] = 1.0 / 60 + g12 + y13;
        yL[1, 1] = 1.0 / 80 + g12 + y23;
        yL[2, 2] = 1.0 / 45 + y23 + y13;
        yL[0, 1] = yL[1, 0] = -g12;
        yL[1, 2] = yL[2, 1] = -y23;
        yL[0, 2] = yL[2, 0] = -y13;
        return (yG, yL);
    }

    // ══ (a) — the probe pair on a constructed two-block circuit ══════════════

    /// <summary>
    /// <c>wsp_yparam2</c> returns <c>[Y]</c> and <c>[Yf]</c> equal to the hand-written matrices to
    /// 1e-10, with <c>y21 = +gm</c> at (2,1) and 0 at (1,2) — a transpose would move it — and the
    /// residual is round-off. Then a third connection between the inner region and the outside
    /// (a resistor from inner node a to outer node d) makes the residual large. NOT a resistor from
    /// an inner node to GROUND, which the brief suggested: ground is not a coupling path, a shunt
    /// at an inner node is simply part of the inner block's y11, and both stimulus sets agree on
    /// it — that variant is asserted SMALL here, because the distinction is the finding.
    /// </summary>
    [Fact]
    public void A_ProbePair_MatchesTheHandWrittenBlocks_AndTheResidualSaysWhenTheyDoNotBracketATwoPort()
    {
        var (ds, freqs) = RunFixture("two_block.cnl");
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var (y, yf) = TwoBlockHand(freqs[fi]);
            var w = Wsp(ds, fi);
            var p = WspPair.YParam2(w, 1, 2);
            string at = $"f = {freqs[fi] / 1e9:G4} GHz";
            AssertRel(y.Y11, p.Inner.Y11, 1e-10, $"y11 {at}");
            Assert.True(p.Inner.Y12.Magnitude < 1e-10 * y.Y21.Magnitude, $"y12 = {p.Inner.Y12} should be 0 {at}");
            AssertRel(y.Y21, p.Inner.Y21, 1e-10, $"y21 = +gm at (2,1) {at}");
            AssertRel(y.Y22, p.Inner.Y22, 1e-10, $"y22 {at}");
            AssertRel(yf.Y11, p.Feedback.Y11, 1e-10, $"yf11 {at}");
            AssertRel(yf.Y12, p.Feedback.Y12, 1e-10, $"yf12 {at}");
            AssertRel(yf.Y21, p.Feedback.Y21, 1e-10, $"yf21 {at}");
            AssertRel(yf.Y22, p.Feedback.Y22, 1e-10, $"yf22 {at}");

            double res = WspPair.YParam2Residual(w, 1, 2);
            Assert.True(res < 1e-10, $"residual {res:G3} {at}");
        }

        string dir  = Path.GetDirectoryName(Fixture("two_block.cnl"))!;
        string text = File.ReadAllText(Fixture("two_block.cnl"));

        // A bypass from the inner region to the outside: the residual is large.
        var (bypass, _) = RunText(text + "\nR:Rx a d R=300 Ohm\n", dir);
        double large = WspPair.YParam2Residual(Wsp(bypass, 0), 1, 2);
        output.WriteLine($"residual with a–d bypass: {large:G4}");
        Assert.True(large > 1e-3, $"residual {large:G3} should be large with a connection around the probes");

        // A shunt to ground at an inner node: still a two-port, still round-off.
        var (shunt, _) = RunText(text + "\nR:Rg a 0 R=300 Ohm\n", dir);
        double small = WspPair.YParam2Residual(Wsp(shunt, 0), 1, 2);
        output.WriteLine($"residual with a–ground shunt: {small:G4}");
        Assert.True(small < 1e-10, $"a shunt to ground at an inner node is part of the inner block; residual {small:G3}");
    }

    // ══ (b) — bifurcation sides and transposes ══════════════════════════════

    /// <summary>
    /// On three same-oriented probes with the VCCS network on the G side:
    /// <c>wsp_bifurcate("Y","G")</c> is the hand-written <c>Y_G</c> — <c>[2,1] = gm</c>, <c>[1,2] = 0</c>,
    /// so a transpose cannot hide — <c>("Y","L")</c> is <c>Y_L</c>, each Y form is the inverse of
    /// the same side's Z form (R-wsp3-2), and the document's aliases resolve to the sides of §3's table.
    /// </summary>
    [Fact]
    public void B_Bifurcation_ReturnsTheTrueMatrixOfEachSide_AndBothFormsAgree()
    {
        var (ds, freqs) = RunFixture("three_probe.cnl");
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var (yG, yL) = ThreeProbeHand(freqs[fi]);
            var w = Wsp(ds, fi);
            string at = $"f = {freqs[fi] / 1e9:G4} GHz";

            var g = WspBifurcation.Bifurcate(w, WspForm.Y, WspSide.G);
            var l = WspBifurcation.Bifurcate(w, WspForm.Y, WspSide.L);
            AssertMatrix(yG, g, 1e-10, $"Y_G {at}");
            AssertMatrix(yL, l, 1e-10, $"Y_L {at}");
            AssertRel(0.02, g[1, 0], 1e-10, $"Y_G[2,1] = gm {at}");
            Assert.True(g[0, 1].Magnitude < 1e-12, $"Y_G[1,2] = {g[0, 1]} should be 0 {at}");

            var zg = WspBifurcation.Bifurcate(w, WspForm.Z, WspSide.G);
            var zl = WspBifurcation.Bifurcate(w, WspForm.Z, WspSide.L);
            AssertMatrix(g, WspMatrix.Inverse(zg), 1e-10, $"Y_G == inverse(Z_G) {at}");
            AssertMatrix(l, WspMatrix.Inverse(zl), 1e-10, $"Y_L == inverse(Z_L) {at}");

            AssertMatrix(g,  WspBifurcation.YA(w), 0.0, "wsp_YA = G side");
            AssertMatrix(l,  WspBifurcation.YF(w), 0.0, "wsp_YF = L side");
            AssertMatrix(zl, WspBifurcation.ZA(w), 0.0, "wsp_ZA = L side");
            AssertMatrix(zg, WspBifurcation.ZF(w), 0.0, "wsp_ZF = G side");
        }
    }

    // ══ (c) — wsp_ymatrix against the engine ════════════════════════════════

    /// <summary>
    /// <c>wsp_ymatrix</c> equals the admittance matrix obtained by running an S-parameter analysis
    /// with Terms (Z = 1e9 Ω, effectively open) at the same three nodes, converting S → Y, to 1e-8
    /// — on the non-reciprocal fixture, so the IVᵀ transpose is exercised. No 1e-9 S is subtracted:
    /// S → Y at a reference impedance yields the network's own admittance, the reference being the
    /// generator's and not part of the network. It also equals the closed form Y_G + Y_L.
    /// </summary>
    [Fact]
    public void C_Ymatrix_EqualsTheEnginesOwnSToY_AtTermsOnTheSameNodes()
    {
        string dir  = Path.GetDirectoryName(Fixture("three_probe.cnl"))!;
        string text = File.ReadAllText(Fixture("three_probe.cnl"));
        var (probed, freqs, pw) = RunTextWithWarnings(text, dir);
        output.WriteLine($"probed run warnings: [{string.Join(" | ", pw)}]");

        const string zTerm = "1e6";
        string termed = string.Join('\n', text.Split('\n').Where(l => !l.TrimStart().StartsWith("WSProbe")))
            .Replace("R:RL1", $"Term:T1 n1 0 Num=1 Z={zTerm} Ohm\nTerm:T2 n2 0 Num=2 Z={zTerm} Ohm\nTerm:T3 n3 0 Num=3 Z={zTerm} Ohm\nR:RL1")
            .Replace(" m1 ", " n1 ").Replace(" m2 ", " n2 ").Replace(" m3 ", " n3 ");
        var (terms, tf, tw) = RunTextWithWarnings(termed, dir);
        output.WriteLine($"termed run warnings: [{string.Join(" | ", tw)}]");
        Assert.Equal(freqs.Length, tf.Length);

        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var y = WspGlobal.Ymatrix(Wsp(probed, fi));
            var (yG, yL) = ThreeProbeHand(freqs[fi]);
            AssertMatrix(WspMatrix.Add(yG, yL), y, 1e-9, $"Y = Y_G + Y_L at {freqs[fi] / 1e9:G4} GHz");

            var s = new Mat<Complex>(3, 3);
            for (int i = 1; i <= 3; i++)
                for (int j = 1; j <= 3; j++) s[i - 1, j - 1] = terms.S(i, j).ComplexValues[fi];
            var yEng = RFNetwork.SToY(s, new Complex(double.Parse(zTerm), 0));
            var yE = new Complex[3, 3];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) yE[i, j] = yEng[i, j];
            if (fi == 0)
                for (int i = 0; i < 3; i++)
                    output.WriteLine($"S→Y − ymatrix row {i + 1}: {string.Join("  ", Enumerable.Range(0, 3).Select(j => (yE[i, j] - y[i, j]).ToString("G4")))}");
            AssertMatrix(yE, y, 1e-8, $"wsp_ymatrix vs S → Y at {freqs[fi] / 1e9:G4} GHz");
            Assert.True((y[1, 0] - y[0, 1]).Magnitude > 1e-3, "the fixture must be non-reciprocal for this to test the transpose");
        }
    }

    // ══ (d) — Ohtomo ═════════════════════════════════════════════════════════

    /// <summary>
    /// On the three-probe fixture: <c>Π(G_i − 1) = det(M)</c> at every frequency; reversing the probe
    /// list changes the individual <c>G_i</c> but not the sum of their encirclements of <c>+1</c>,
    /// which equals the encirclements of the origin by <c>det(M)</c> (the running count's net change
    /// over the sweep, compared on a fine grid).
    /// </summary>
    [Fact]
    public void D_Ohtomo_TelescopesToDetM_AndTheEncirclementSumDoesNotDependOnTheOrder()
    {
        string dir  = Path.GetDirectoryName(Fixture("three_probe.cnl"))!;
        string text = File.ReadAllText(Fixture("three_probe.cnl")).Replace("npts=12", "npts=801");
        var (ds, freqs) = RunText(text, dir);
        var z0 = new Complex(50, 0);
        int nf = freqs.Length;

        int[] fwd = [1, 2, 3], rev = [3, 2, 1];
        var gF = new Complex[3][]; var gR = new Complex[3][]; var det = new Complex[nf];
        for (int i = 0; i < 3; i++) { gF[i] = new Complex[nf]; gR[i] = new Complex[nf]; }
        bool differ = false;
        for (int fi = 0; fi < nf; fi++)
        {
            var w = Wsp(ds, fi);
            var m = WspOhtomo.M(w, fwd, WspSide.G, z0);
            det[fi] = WspMatrix.Determinant(m);
            var a = WspOhtomo.LoopGains(w, fwd, WspSide.G, z0);
            var b = WspOhtomo.LoopGains(w, rev, WspSide.G, z0);
            Complex prod = Complex.One;
            for (int i = 0; i < 3; i++) { gF[i][fi] = a[i]; gR[i][fi] = b[i]; prod *= a[i] - Complex.One; }
            AssertRel(det[fi], prod, 1e-9, $"Π(G_i − 1) = det(M) at {freqs[fi] / 1e9:G4} GHz");
            // The reversed list telescopes to the same determinant (the trailing minors differ, the product does not).
            Complex prodR = Complex.One;
            for (int i = 0; i < 3; i++) prodR *= b[i] - Complex.One;
            AssertRel(det[fi], prodR, 1e-9, $"reversed: Π(G_i − 1) = det(M) at {freqs[fi] / 1e9:G4} GHz");
            if ((a[0] - b[0]).Magnitude > 1e-6 * a[0].Magnitude) differ = true;
        }
        Assert.True(differ, "reversing the probe list should change the individual G_i");

        static double Turns(Complex[] locus)
        {
            var e = WspKurokawa.Encirclements(locus);
            return e[^1] - e[0];
        }
        double sumF = 0, sumR = 0;
        for (int i = 0; i < 3; i++)
        {
            sumF += Turns([.. gF[i].Select(g => g - Complex.One)]);
            sumR += Turns([.. gR[i].Select(g => g - Complex.One)]);
        }
        double detTurns = Turns(det);
        output.WriteLine($"Σ enc(G_i − 1): forward {sumF:G6}, reversed {sumR:G6}; enc(det M) {detTurns:G6}");
        Assert.Equal(detTurns, sumF, 6);
        Assert.Equal(detTurns, sumR, 6);
    }

    /// <summary>
    /// <c>N = 1</c> on the document's series resonator with the loop split at the probe (G side =
    /// <c>R1 + L1 + C1</c>, L side = <c>RS</c>): <c>G_1 = ΓP·ΓA</c>, and
    /// <c>wsp_unstable_freq_loopgain(G_1)</c> reports 1.5915 GHz for <c>R1 = −20 Ω</c> and nothing
    /// for <c>R1 = +20 Ω</c>.
    /// </summary>
    [Fact]
    public void D_Ohtomo_OneProbe_OnTheSeriesResonator_ReportsF0OnlyWhenUnstable()
    {
        string dir  = Path.GetDirectoryName(Fixture("series_resonator.cnl"))!;
        string text = File.ReadAllText(Fixture("series_resonator.cnl"));
        double f0 = 1.0 / (2 * Math.PI * Math.Sqrt(1e-9 * 10e-12));
        var z0 = new Complex(50, 0);

        foreach (var (r1, unstable) in new (string, bool)[] { ("R=-20 Ohm", true), ("R=20 Ohm", false) })
        {
            var (ds, freqs) = RunText(text.Replace("R=-20 Ohm", r1), dir);
            var g = new Complex[freqs.Length];
            for (int fi = 0; fi < freqs.Length; fi++)
            {
                var w = Wsp(ds, fi);
                g[fi] = WspOhtomo.LoopGains(w, [1], WspSide.G, z0)[0];
                // ΓP·ΓA from the two sides' true one-ports.
                var yA = WspBifurcation.Bifurcate(w, WspForm.Y, WspSide.G)[0, 0];
                var yP = WspBifurcation.Bifurcate(w, WspForm.Y, WspSide.L)[0, 0];
                var expect = (Complex.One - z0 * yP) / (Complex.One + z0 * yP) * (Complex.One - z0 * yA) / (Complex.One + z0 * yA);
                AssertRel(expect, g[fi], 1e-10, $"G_1 = ΓP·ΓA at {freqs[fi] / 1e9:G4} GHz");
            }
            var hits = WspOhtomo.UnstableFrequenciesLoopGain(g, freqs);
            double step = freqs[1] - freqs[0];
            output.WriteLine($"{r1}: loop-gain crossings [{string.Join(", ", hits.Select(h => (h / 1e9).ToString("G6")))}] GHz");
            if (unstable)
            {
                double got = Assert.Single(hits);
                Assert.True(Math.Abs(got - f0) <= step / 2, $"expected {f0 / 1e9:G6} GHz, got {got / 1e9:G6}");
            }
            else Assert.Empty(hits);
        }
    }

    // ══ (e) — the envelope against brute force ═══════════════════════════════

    private static (Complex ZS, Complex ZL) PulledTerminations()
    {
        var z0 = new Complex(50, 0);
        var zs = WspEnvelope.GammaToZ(Complex.FromPolarCoordinates(0.5, 60 * Math.PI / 180), z0);
        var zl = WspEnvelope.GammaToZ(Complex.FromPolarCoordinates(0.3, -120 * Math.PI / 180), z0);
        return (zs, zl);
    }

    private static string ComplexParam(Complex z)
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"complex({z.Real:R},{z.Imaginary:R})");

    /// <summary>
    /// <c>ΓS = 0.5∠60°</c>, <c>ΓL = 0.3∠−120°</c>: <c>wsp_terminate</c> from the 50 Ω run agrees with
    /// a RE-RUN whose Terms declare <c>ZS</c>, <c>ZL</c>, to 1e-9 relative in every entry of wsp —
    /// including the load probe's own row and column, where the L-node form's two δ-corrections act.
    /// Then <c>H03'</c> from Eq. 191's cofactor form (T-11: the full modified 3×3, its (3,3)
    /// cofactor over its determinant) equals <c>wsp'(6, 6)</c> to 1e-10.
    /// </summary>
    [Fact]
    public void E_Terminate_MatchesARerunWithTheTermsChanged_AndEq191sCofactorForm()
    {
        string dir  = Path.GetDirectoryName(Fixture("two_stage_terms.cnl"))!;
        string text = File.ReadAllText(Fixture("two_stage_terms.cnl"));
        var (nominal, freqs) = RunFixture("two_stage_terms.cnl");
        var (zs, zl) = PulledTerminations();

        // The metadata the precondition reads: T1 at PS's G node, T2 at PL's L node, nothing at P3.
        var termZ = nominal["__WspTermZ"];
        Assert.Equal(["probe", "side"], termZ.Axes.Select(a => a.Name).ToArray());
        Assert.Equal(new Complex(50, 0), (Complex)termZ[0, 0]);
        Assert.True(Complex.IsNaN((Complex)termZ[0, 1]));
        Assert.True(Complex.IsNaN((Complex)termZ[1, 0]));
        Assert.Equal(new Complex(50, 0), (Complex)termZ[1, 1]);
        Assert.True(Complex.IsNaN((Complex)termZ[2, 0]) && Complex.IsNaN((Complex)termZ[2, 1]));

        string pulled = text
            .Replace("Term:T1     p1 0   Num=1 Z=50 Ohm", $"Term:T1     p1 0   Num=1 Z={ComplexParam(zs)}")
            .Replace("Term:T2     p2 0   Num=2 Z=50 Ohm", $"Term:T2     p2 0   Num=2 Z={ComplexParam(zl)}");
        Assert.NotEqual(text, pulled);
        var (rerun, rf) = RunText(pulled, dir);
        Assert.Equal(freqs.Length, rf.Length);

        var all = AllWsp(nominal, freqs.Length);
        WspEnvelope.RequireAtTermination(all, 1, WspSide.G, new Complex(50, 0), "PS", freqs);
        WspEnvelope.RequireAtTermination(all, 2, WspSide.L, new Complex(50, 0), "PL", freqs);

        double worst = 0;
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var w  = all[fi];
            var so = WspEnvelope.StartingAdmittance(w, 1, WspSide.G);
            var lo = WspEnvelope.StartingAdmittance(w, 2, WspSide.L);
            AssertRel(1.0 / 50, so, 1e-10, "YSo = 1/ZG(PS) = 1/50");
            AssertRel(1.0 / 50, lo, 1e-10, "YLo = 1/ZL(PL) = 1/50");

            var t = WspEnvelope.Terminate(w, 1, Complex.One / zs, so, 2, Complex.One / zl, lo);
            var b = Wsp(rerun, fi);
            double scale = 0;
            for (int r = 0; r < 6; r++) for (int c = 0; c < 6; c++) scale = Math.Max(scale, b[r, c].Magnitude);
            for (int r = 0; r < 6; r++)
                for (int c = 0; c < 6; c++)
                {
                    double err = (t[r, c] - b[r, c]).Magnitude / Math.Max(b[r, c].Magnitude, 1e-6 * scale);
                    worst = Math.Max(worst, err);
                    Assert.True(err <= 1e-9,
                        $"wsp'({r + 1},{c + 1}) at {freqs[fi] / 1e9:G4} GHz: rank-1 update {t[r, c]} vs re-run {b[r, c]} (rel {err:G3})");
                }

            // The re-run's own probes see the pulled terminations.
            AssertRel(zs, WspReduction.ZG(WspProbeQuad.Of(b, 1)), 1e-9, "re-run ZG(PS) = ZS");
            AssertRel(zl, WspReduction.ZL(WspProbeQuad.Of(b, 2)), 1e-9, "re-run ZL(PL) = ZL");

            // Eq. 191 (T-11): H03' = C33(Y') / det(Y'), Y' the reduced 3×3 with y11 → y11 − YSo + YS
            // and y22 → y22 − YLo + YL.
            var y = WspGlobal.Ymatrix(w);
            y[0, 0] += Complex.One / zs - so;
            y[1, 1] += Complex.One / zl - lo;
            var c33 = y[0, 0] * y[1, 1] - y[0, 1] * y[1, 0];
            var h03 = c33 / WspMatrix.Determinant(y);
            AssertRel(h03, t[5, 5], 1e-10, $"Eq. 191 H03' vs wsp'(6,6) at {freqs[fi] / 1e9:G4} GHz");
        }
        output.WriteLine($"worst relative entry error, rank-1 update vs re-run: {worst:G3}");
    }

    /// <summary>The source probe moved one series element away from its Term: the precondition
    /// refuses by name, because the termination is no longer a pure shunt at the probe's G node.</summary>
    [Fact]
    public void E_Terminate_RefusesAProbeThatIsNotDirectlyAtItsTermination()
    {
        string dir  = Path.GetDirectoryName(Fixture("two_stage_terms.cnl"))!;
        string text = File.ReadAllText(Fixture("two_stage_terms.cnl"));
        string moved = text
            .Replace("WSProbe:PS  p1 s1\nR:RS1   s1 g1  R=10 Ohm", "R:RS1   p1 s1  R=10 Ohm\nWSProbe:PS  s1 g1");
        Assert.NotEqual(text, moved);
        var (ds, freqs) = RunText(moved, dir);
        var all = AllWsp(ds, freqs.Length);

        // The engine records no Term at PS's G node any more …
        Assert.True(Complex.IsNaN((Complex)ds["__WspTermZ"][0, 0]));
        var ex = Assert.Throws<ArgumentException>(() =>
            WspEnvelope.RequireAtTermination(all, 1, WspSide.G, null, "PS", freqs));
        Assert.Contains(WspEnvelope.NotAtTerminationKey, ex.Message);
        Assert.Contains("'PS'", ex.Message);

        // … and even told the Term is 50 Ω, ZG is 60 Ω there, which is the second refusal.
        var ex2 = Assert.Throws<ArgumentException>(() =>
            WspEnvelope.RequireAtTermination(all, 1, WspSide.G, new Complex(50, 0), "PS", freqs));
        Assert.Contains(WspEnvelope.NotAtTerminationKey, ex2.Message);
        Assert.Contains("not directly at its termination", ex2.Message);
        output.WriteLine(ex2.Message);
    }

    // ══ (f) — wsp_loadpull_unstable against the closed form ══════════════════

    /// <summary>
    /// The series resonator with <c>RS</c> replaced by a Term at 10 Ω and <c>R1 = −5 Ω</c> (stable at
    /// 10 Ω): a load-pull over a constant-|Γ| circle finds exactly the arc of θ on which
    /// <c>Re ZS(θ) &lt; 5 Ω</c> unstable on the <c>1/Y0'</c> side, at the frequency where
    /// <c>X(ω) = −Im ZS(θ)</c> — Eq. 109 with <c>RS → ZS(θ)</c>. Two things the brief stated that
    /// the arithmetic overrides: at <c>|Γ| = 0.8</c> the whole circle is STABLE (min Re ZS = 5.56 Ω
    /// &gt; 5 Ω), so the sweep is at <c>|Γ| = 0.9</c>; and the reported frequency tracks <c>f0</c>
    /// only where <c>Im ZS ≈ 0</c> — elsewhere the load reactance detunes the resonator, and the
    /// closed form is what it tracks. The <c>1/H0'</c> side is reported, not asserted: the
    /// document's own §4.10 says the zero masks the pole there.
    /// </summary>
    [Fact]
    public void F_LoadpullUnstable_FindsTheArcOfTheCircleThatEq109SaysIsUnstable()
    {
        var (ds, freqs) = RunFixture("series_resonator_term.cnl");
        var all = AllWsp(ds, freqs.Length);
        const double L = 1e-9, C = 10e-12, R1 = -5.0;
        var z0 = new Complex(50, 0);
        double step = freqs[1] - freqs[0];

        WspEnvelope.RequireAtTermination(all, 1, WspSide.L, new Complex(10, 0), "P", freqs);

        var grid = WspEnvelope.CircleGrid(0.9, 36);
        var env  = WspEnvelope.Loadpull(all, freqs, 0, 1, 1, [], grid, z0);
        var uns  = WspEnvelope.LoadpullUnstable(env);

        int unstableCount = 0, stableCount = 0, skipped = 0;
        for (int l = 0; l < grid.Length; l++)
        {
            var zs = WspEnvelope.GammaToZ(grid[l], z0);
            double rs = zs.Real, xs = zs.Imaginary;
            // X(ω) = ωL − 1/(ωC) = −XS  →  ω = (−XS + √(XS² + 4L/C)) / (2L)
            double wc = (-xs + Math.Sqrt(xs * xs + 4 * L / C)) / (2 * L);
            double fc = wc / (2 * Math.PI);
            var fromY = uns.FromY0[0][l];
            var fromH = uns.FromH0[0][l];
            output.WriteLine($"θ = {grid[l].Phase * 180 / Math.PI,7:F1}°  ZS = {rs,7:F2} {(xs >= 0 ? "+" : "-")} j{Math.Abs(xs),7:F2}  " +
                             $"closed-form f = {fc / 1e9:F4} GHz  Y0 hits [{string.Join(", ", fromY.Select(f => (f / 1e9).ToString("F4")))}]  " +
                             $"H0 hits [{string.Join(", ", fromH.Select(f => (f / 1e9).ToString("F4")))}]");

            bool inSweep = fc > freqs[0] + step && fc < freqs[^1] - step;
            if (Math.Abs(rs - (-R1)) < 0.05) { skipped++; continue; }

            if (rs > -R1)
            {
                // Stable at this load: nothing on the 1/Y0' side, wherever the crossing would be.
                Assert.Empty(fromY);
                stableCount++;
            }
            else if (!inSweep) skipped++;
            else
            {
                double got = Assert.Single(fromY);
                Assert.True(Math.Abs(got - fc) <= step, $"θ = {grid[l].Phase * 180 / Math.PI:F1}°: expected {fc / 1e9:F4} GHz, got {got / 1e9:F4}");
                unstableCount++;
            }
        }
        output.WriteLine($"unstable θ: {unstableCount}, stable θ: {stableCount}, skipped (edge/out of sweep): {skipped}");
        Assert.True(unstableCount >= 3 && stableCount >= 3, "the circle should cross the envelope");
    }

    // ══ (g) — wsp_block_calc self-consistency on a real block ════════════════

    /// <summary><c>1 − {9} == {13}</c> … <c>1 − {12} == {16}</c> and <c>{1..4}</c> is <c>S</c> of
    /// <c>[Y]</c> at <c>Z0</c>, on the two-block circuit; the no-reverse-transfer limit is a real
    /// property of this fixture (<c>y12 = 0</c>), so <c>LGM == LGH</c> holds only once
    /// <c>yf21</c> is also zero — asserted on the synthetic side, and here the two are shown to differ.</summary>
    [Fact]
    public void G_BlockCalc_IsSelfConsistentOnARealBlock()
    {
        var (ds, freqs) = RunFixture("two_block.cnl");
        foreach (double z0r in (double[])[50.0, 75.0])
        {
            var z0 = new Complex(z0r, 0);
            for (int fi = 0; fi < freqs.Length; fi++)
            {
                var w  = Wsp(ds, fi);
                var bc = WspPair.BlockCalc(w, 1, 2, z0);
                var (y, _) = TwoBlockHand(freqs[fi]);
                string at = $"Z0 = {z0r}, f = {freqs[fi] / 1e9:G4} GHz";
                AssertClose(Complex.One - bc[8],  bc[12], 1e-10, $"1 − {{9}} == {{13}} {at}");
                AssertClose(Complex.One - bc[9],  bc[13], 1e-10, $"1 − {{10}} == {{14}} {at}");
                AssertClose(Complex.One - bc[10], bc[14], 1e-13, $"1 − {{11}} == {{15}} {at}");
                AssertClose(Complex.One - bc[11], bc[15], 1e-13, $"1 − {{12}} == {{16}} {at}");
                var s = WspMatrix.ScatteringOfY(new[,] { { y.Y11, y.Y12 }, { y.Y21, y.Y22 } }, z0);
                AssertClose(s[0, 0], bc[0], 1e-9, $"s11 {at}"); AssertClose(s[0, 1], bc[1], 1e-9, $"s12 {at}");
                AssertClose(s[1, 0], bc[2], 1e-9, $"s21 {at}"); AssertClose(s[1, 1], bc[3], 1e-9, $"s22 {at}");
                Assert.True((bc[14] - bc[15]).Magnitude > 1e-6 * bc[14].Magnitude,
                    $"LGH and LGM should differ while yf21 ≠ 0 ({at})");
            }
        }
    }

    // ══ (h) — the design helpers ═════════════════════════════════════════════

    /// <summary>
    /// <c>wsp_block_breakout</c> is <c>{1..4}</c>; and <c>wsp_block_design</c> with the probes' own
    /// parallel-RC models equals <c>wsp_zo_renorm_s</c> with <c>ZG₁</c>, <c>ZL₂</c> — up to a
    /// per-port phase the two definitions differ by. The RC form absorbs <c>jωC</c> into the
    /// network and renormalises to the real <c>R</c>; the power-wave form renormalises to the
    /// complex <c>Z = R ∥ 1/jωC</c>. Their waves differ by <c>e^{∓jφ}</c>, <c>φ = atan(ωCR)</c>, so
    /// <c>S_rc = D*·S_zo·D*</c> with <c>D = diag(e^{jφ})</c>: equal magnitudes, and equal to 1e-12
    /// once the phase is put back. The brief's "equals to 1e-12" is true of the magnitudes only.
    /// </summary>
    [Fact]
    public void H_DesignHelpers_MatchTheBreakout_AndZoRenormUpToTheKnownPortPhase()
    {
        var (ds, freqs) = RunFixture("two_block.cnl");
        var z0 = new Complex(50, 0);
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var w = Wsp(ds, fi);
            var bc = WspPair.BlockCalc(w, 1, 2, z0);
            var bb = WspPair.BlockBreakout(w, 1, 2, z0);
            AssertRel(bc[0], bb[0, 0], 0.0, "breakout = {1}"); AssertRel(bc[3], bb[1, 1], 0.0, "breakout = {4}");

            var zg1 = WspReduction.ZG(WspProbeQuad.Of(w, 1));
            var zl2 = WspReduction.ZL(WspProbeQuad.Of(w, 2));
            Assert.True(zg1.Real > 0 && zl2.Real > 0, "both sides are passive at this pair");

            var design = WspPair.BlockDesign(w, 1, 2, freqs[fi], z0);
            var sMat = new Mat<Complex>(2, 2);
            sMat[0, 0] = bb[0, 0]; sMat[0, 1] = bb[0, 1]; sMat[1, 0] = bb[1, 0]; sMat[1, 1] = bb[1, 1];
            var zo = WspRenorm.ZoRenormS(zg1, sMat, zl2, z0);

            double wv = 2 * Math.PI * freqs[fi];
            double phi1 = Math.Atan(wv * ImmittanceModels.ZToPc(zg1, freqs[fi]) * ImmittanceModels.ZToPr(zg1));
            double phi2 = Math.Atan(wv * ImmittanceModels.ZToPc(zl2, freqs[fi]) * ImmittanceModels.ZToPr(zl2));
            Complex[] d = [Complex.FromPolarCoordinates(1, -phi1), Complex.FromPolarCoordinates(1, -phi2)];
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                {
                    string at = $"S{i + 1}{j + 1} at {freqs[fi] / 1e9:G4} GHz";
                    Assert.Equal(zo[i, j].Magnitude, design[i, j].Magnitude, 12);
                    AssertClose(d[i] * zo[i, j] * d[j], design[i, j], 1e-12, $"wsp_block_design = D*·wsp_zo_renorm_s·D* {at}");
                }
        }
    }
}
