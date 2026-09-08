// ================================================================
//  WspGlobalTests.cs — brief-wsprobe-3's scalar-core gates, on SYNTHETIC wsp matrices built from
//  known subnetworks.
//
//  A wsp matrix is a function of the network the probes cut through. For N probes all oriented the
//  same way, that network is a G-side N-port Y_G and an L-side N-port Y_L hanging off the same N
//  nodes, and the whole matrix follows from Kirchhoff (Fig. 16):
//
//      series stimulus at i (vS = e_i):  vG = −(Y_G + Y_L)⁻¹·Y_L·e_i,   iS = Y_L·(vG + e_i)
//      shunt  stimulus at i (iP = e_i):  vG =  (Y_G + Y_L)⁻¹·e_i,       iS = Y_L·vG
//
//  so every identity of §3–§6 can be checked against the matrices the fixture was BUILT from, with
//  no engine, no netlist, and — because Y_G is random and non-reciprocal — no place for a lost
//  transpose or a swapped side to hide. The probe-pair fixture (§2) is the same idea on the four
//  nodes of Fig. 40. The end-to-end gates on real circuits are in Engine.Tests.
//
//  Reference: T. A. Winslow, General Circuit Analysis Using The WSProbe (2023).
// ================================================================

using System;
using System.Linq;
using System.Numerics;
using RfCore.Stability;
using Xunit;

namespace RfCore.Tests.Stability;

public class WspGlobalTests
{
    private static Complex Rnd(Random r) => new(r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1);

    /// <summary>A random non-reciprocal N-port, diagonally weighted so it is comfortably invertible.</summary>
    private static Complex[,] RandomY(Random r, int n, double offDiag = 0.4)
    {
        var y = new Complex[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                y[i, j] = i == j ? new Complex(1.0 + r.NextDouble(), r.NextDouble() - 0.5) : offDiag * Rnd(r);
        return y;
    }

    /// <summary>The wsp matrix of N same-oriented probes between a G-side N-port and an L-side N-port.</summary>
    private static Complex[,] SyntheticWsp(Complex[,] yG, Complex[,] yL)
    {
        int n = yG.GetLength(0);
        var sum = new WspMatrix.Lu(WspMatrix.Add(yG, yL));
        var w = new Complex[2 * n, 2 * n];
        var e = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            Array.Clear(e); e[i] = Complex.One;

            // series: vG = −(YG+YL)⁻¹·YL·e_i ; iS = YL·(vG + e_i)
            var yle = Mul(yL, e);
            var vG  = sum.Solve(yle).Select(v => -v).ToArray();
            var vL  = (Complex[])vG.Clone(); vL[i] += Complex.One;
            var iS  = Mul(yL, vL);
            for (int j = 0; j < n; j++) { w[2 * i, 2 * j] = iS[j]; w[2 * i, 2 * j + 1] = vG[j]; }

            // shunt: vG = (YG+YL)⁻¹·e_i ; iS = YL·vG
            vG = sum.Solve(e);
            iS = Mul(yL, vG);
            for (int j = 0; j < n; j++) { w[2 * i + 1, 2 * j] = iS[j]; w[2 * i + 1, 2 * j + 1] = vG[j]; }
        }
        return w;
    }

    private static Complex[] Mul(Complex[,] a, Complex[] x)
    {
        int n = a.GetLength(0), m = a.GetLength(1);
        var y = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            Complex s = Complex.Zero;
            for (int j = 0; j < m; j++) s += a[i, j] * x[j];
            y[i] = s;
        }
        return y;
    }

    private static void Close(Complex[,] expected, Complex[,] actual, double tol, string what)
    {
        int n = expected.GetLength(0), m = expected.GetLength(1);
        Assert.Equal(n, actual.GetLength(0));
        Assert.Equal(m, actual.GetLength(1));
        double scale = 0;
        for (int i = 0; i < n; i++) for (int j = 0; j < m; j++) scale = Math.Max(scale, expected[i, j].Magnitude);
        scale = Math.Max(scale, 1e-300);
        for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
            {
                double err = (actual[i, j] - expected[i, j]).Magnitude / scale;
                Assert.True(err <= tol, $"{what} [{i + 1},{j + 1}]: expected {expected[i, j]}, got {actual[i, j]} (rel {err:G3} > {tol:G1})");
            }
    }

    private static void Close(Complex expected, Complex actual, double tol, string what)
    {
        double scale = Math.Max(Math.Max(expected.Magnitude, actual.Magnitude), 1e-300);
        double err = (actual - expected).Magnitude / scale;
        Assert.True(err <= tol, $"{what}: expected {expected}, got {actual} (rel {err:G3} > {tol:G1})");
    }

    // ══ The matrix helpers themselves ════════════════════════════════════════

    /// <summary>The LU behind everything: inverse, solve, and the log-form determinant against a
    /// cofactor expansion — including a matrix whose first pivot is zero, so the permutation
    /// parity in <c>ArgDet</c> is exercised rather than assumed.</summary>
    [Fact]
    public void Lu_InverseSolveAndLogDeterminant_AgreeWithDirectArithmetic()
    {
        var r = new Random(3);
        for (int trial = 0; trial < 50; trial++)
        {
            int n = 2 + trial % 6;
            var a = RandomY(r, n, 0.9);
            if (trial % 3 == 0) a[0, 0] = Complex.Zero;   // force a row swap at the first step

            var inv = WspMatrix.Inverse(a);
            Close(WspMatrix.Identity(n), WspMatrix.Multiply(a, inv), 1e-12, "A·A⁻¹");

            // det by cofactor expansion along the first row (exact for these sizes).
            var det = Cofactor(a);
            var lu  = new WspMatrix.Lu(a);
            Close(det, lu.Determinant, 1e-11, "log-form determinant vs cofactor expansion");
            Assert.Equal(Math.Log(det.Magnitude), lu.LogAbsDet, 9);
        }
    }

    private static Complex Cofactor(Complex[,] a)
    {
        int n = a.GetLength(0);
        if (n == 1) return a[0, 0];
        Complex d = Complex.Zero;
        for (int c = 0; c < n; c++)
        {
            var m = new Complex[n - 1, n - 1];
            for (int i = 1; i < n; i++)
            {
                int cc = 0;
                for (int j = 0; j < n; j++)
                {
                    if (j == c) continue;
                    m[i - 1, cc++] = a[i, j];
                }
            }
            d += (c % 2 == 0 ? 1 : -1) * a[0, c] * Cofactor(m);
        }
        return d;
    }

    // ══ §3 — bifurcation sides and transposes (R-wsp3-9(b), synthetic half) ══

    /// <summary>
    /// On a random NON-reciprocal pair of subnetworks, <c>wsp_bifurcate</c> returns exactly the
    /// matrices the wsp was built from — the true <c>Y_G</c> and <c>Y_L</c>, not their
    /// transposes — and the Z form of each side is the inverse of its Y form (R-wsp3-2). The
    /// document's four names resolve to the sides the table in §3 states.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void Bifurcation_RecoversTheTrueMatricesOfBothSides_AndTheTwoFormsAgree(int n)
    {
        var r = new Random(100 + n);
        for (int trial = 0; trial < 100; trial++)
        {
            var yG = RandomY(r, n); var yL = RandomY(r, n);
            var w  = SyntheticWsp(yG, yL);

            var yg = WspBifurcation.Bifurcate(w, WspForm.Y, WspSide.G);
            var yl = WspBifurcation.Bifurcate(w, WspForm.Y, WspSide.L);
            var zg = WspBifurcation.Bifurcate(w, WspForm.Z, WspSide.G);
            var zl = WspBifurcation.Bifurcate(w, WspForm.Z, WspSide.L);

            Close(yG, yg, 1e-10, "Y_G (true, not transposed)");
            Close(yL, yl, 1e-10, "Y_L (true, not transposed)");
            Close(WspMatrix.Inverse(yG), zg, 1e-10, "Z_G = Y_G⁻¹");
            Close(WspMatrix.Inverse(yL), zl, 1e-10, "Z_L = Y_L⁻¹");
            Close(yg, WspMatrix.Inverse(zg), 1e-9, "R-wsp3-2: Y form G == inverse(Z form G)");
            Close(yl, WspMatrix.Inverse(zl), 1e-9, "R-wsp3-2: Y form L == inverse(Z form L)");

            // The document's names, with the document's sides (T-9): YA/ZF are G, YF/ZA are L.
            Close(yg, WspBifurcation.YA(w), 0.0, "wsp_YA is the G side");
            Close(yl, WspBifurcation.YF(w), 0.0, "wsp_YF is the L side");
            Close(zl, WspBifurcation.ZA(w), 0.0, "wsp_ZA is the L side");
            Close(zg, WspBifurcation.ZF(w), 0.0, "wsp_ZF is the G side");

            // A transpose would be caught: Y_G is non-reciprocal by construction.
            if (n > 1)
                Assert.True((yG[1, 0] - yG[0, 1]).Magnitude > 1e-3, "fixture must be non-reciprocal");
        }
    }

    /// <summary>A probe SUBSET, in any order, is the corresponding sub-blocks — and the one-probe
    /// case reduces to the single-probe library: <c>Y_G = YG</c>, <c>Y_L = YL</c>, <c>Z_G = ZG</c>,
    /// <c>Z_L = ZL</c> (Eq. 67/68/76).</summary>
    [Fact]
    public void Bifurcation_OverASubset_AndTheOneProbeCaseIsTheSingleProbeLibrary()
    {
        var r = new Random(7);
        // A block-diagonal pair of subnetworks (probes {1,3} decoupled from {2}) so a subset is a
        // genuine bifurcation of its own; the sub-blocks of the full Y_G are then the answer.
        var yG = new Complex[3, 3]; var yL = new Complex[3, 3];
        var a = RandomY(r, 2); var b = RandomY(r, 2);
        int[] map = [0, 2];
        for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++) { yG[map[i], map[j]] = a[i, j]; yL[map[i], map[j]] = b[i, j]; }
        yG[1, 1] = new Complex(2.0, 0.5); yL[1, 1] = new Complex(1.5, -0.25);
        var w = SyntheticWsp(yG, yL);

        var sub = WspBifurcation.Bifurcate(w, WspForm.Y, WspSide.G, [3, 1]);
        Close(new[,] { { a[1, 1], a[1, 0] }, { a[0, 1], a[0, 0] } }, sub, 1e-10, "subset {3,1} of Y_G, in list order");

        var one = WspBifurcation.Bifurcate(w, WspForm.Y, WspSide.G, [2]);
        var q   = WspProbeQuad.Of(w, 2);
        var y2  = WspReduction.YParam(q);
        Close(WspReduction.YG(y2), one[0, 0], 1e-10, "one probe: Y_G = YG (Eq. 76)");
        Close(WspReduction.YL(y2), WspBifurcation.Bifurcate(w, WspForm.Y, WspSide.L, [2])[0, 0], 1e-10, "Y_L = YL");
        Close(WspReduction.ZG(q),  WspBifurcation.Bifurcate(w, WspForm.Z, WspSide.G, [2])[0, 0], 1e-10, "Z_G = ZG (Eq. 67)");
        Close(WspReduction.ZL(q),  WspBifurcation.Bifurcate(w, WspForm.Z, WspSide.L, [2])[0, 0], 1e-10, "Z_L = ZL (Eq. 68)");

        Assert.Contains("listed twice", Assert.Throws<ArgumentException>(() => WspMatrix.Blocks(w, [1, 1])).Message);
        Assert.Contains("outside 1..3", Assert.Throws<ArgumentException>(() => WspMatrix.Blocks(w, [4])).Message);
    }

    // ══ §5 — the reduced matrices and NDF ════════════════════════════════════

    /// <summary><c>wsp_ymatrix</c> is <c>Y_G + Y_L</c> — both sides in parallel at the same nodes
    /// (Eq. 184/185, with the IVᵀ transpose exercised by a non-reciprocal fixture) — and the NDF
    /// of Eq. 186 is the determinant ratio of the two reduced matrices, in log form.</summary>
    [Fact]
    public void Ymatrix_IsBothSidesInParallel_AndNdfIsTheDeterminantRatio()
    {
        var r = new Random(11);
        for (int trial = 0; trial < 50; trial++)
        {
            int n = 1 + trial % 4;
            var yG = RandomY(r, n); var yL = RandomY(r, n);
            var w  = SyntheticWsp(yG, yL);
            Close(WspMatrix.Add(yG, yL), WspGlobal.Ymatrix(w), 1e-10, "Y = Y_G + Y_L");

            // A "passivated" run: the G side's off-diagonals (its controlled sources) removed.
            var yGp = (Complex[,])yG.Clone();
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) if (i != j) yGp[i, j] = Complex.Zero;
            var wp = SyntheticWsp(yGp, yL);

            var expected = WspMatrix.Determinant(WspMatrix.Add(yG, yL)) / WspMatrix.Determinant(WspMatrix.Add(yGp, yL));
            Close(expected, WspGlobal.Ndf(w, wp), 1e-9, "NDF = det(Z_p)/det(Z) = det(Y)/det(Y_p)");
        }
        // Identical runs: NDF ≡ 1.
        {
            var yG = RandomY(r, 3); var yL = RandomY(r, 3);
            var w  = SyntheticWsp(yG, yL);
            Close(Complex.One, WspGlobal.Ndf(w, w), 1e-12, "NDF of a run against itself");
        }
    }

    // ══ §4 — Ohtomo (R-wsp3-9(d), synthetic half) ════════════════════════════

    /// <summary>The telescoping identity <c>Π(G_i − 1) = det(M)</c> on random <c>M</c>, and the
    /// corrected Eq. 180: <c>G_N = 1 + M_NN</c>, <c>G_1 = 1 + det(M)/det(M[2..N, 2..N])</c>.</summary>
    [Fact]
    public void Ohtomo_TelescopesToTheDeterminant_AndFollowsTheCorrectedEq180()
    {
        var r = new Random(21);
        for (int trial = 0; trial < 100; trial++)
        {
            int n = 1 + trial % 5;
            var m = RandomY(r, n, 0.8);
            var g = WspOhtomo.LoopGainsFromM(m);
            Assert.Equal(n, g.Length);

            Complex prod = Complex.One;
            foreach (var gi in g) prod *= gi - Complex.One;
            Close(WspMatrix.Determinant(m), prod, 1e-9, "Π(G_i − 1) = det(M)");

            Close(Complex.One + m[n - 1, n - 1], g[n - 1], 1e-12, "G_N = 1 + M_NN");
            if (n > 1)
                Close(Complex.One + WspMatrix.Determinant(m) / WspMatrix.Determinant(WspMatrix.TrailingPrincipal(m, 1)),
                      g[0], 1e-9, "G_1 = 1 + det(M)/det(M[2..N,2..N])");
        }
    }

    /// <summary><c>N = 1</c>: <c>G_1 = SP·SA = ΓP·ΓA</c>, Jackson's stability index, with the
    /// reflection of each side taken from the TRUE one-port each side is.</summary>
    [Fact]
    public void Ohtomo_OneProbe_IsTheReflectionProduct()
    {
        var r = new Random(5);
        var z0 = new Complex(50, 0);
        for (int trial = 0; trial < 100; trial++)
        {
            var yG = new[,] { { Rnd(r) / 50.0 } }; var yL = new[,] { { Rnd(r) / 50.0 } };
            var w  = SyntheticWsp(yG, yL);
            var gA = (Complex.One - z0 * yG[0, 0]) / (Complex.One + z0 * yG[0, 0]);
            var gP = (Complex.One - z0 * yL[0, 0]) / (Complex.One + z0 * yL[0, 0]);
            var g  = WspOhtomo.LoopGains(w, null, WspSide.G, z0);
            Close(gP * gA, g[0], 1e-10, "G_1 = ΓP·ΓA");
            // Swapping which side is "active" swaps the factors and changes nothing for N = 1.
            Close(gP * gA, WspOhtomo.LoopGains(w, null, WspSide.L, z0)[0], 1e-10, "active side irrelevant at N = 1");
        }
    }

    /// <summary>The loop-gain search: a clockwise crossing of the positive real axis at
    /// <c>|G| ≥ 1</c> is reported; the same crossing counter-clockwise, or at <c>|G| &lt; 1</c>, is
    /// not. All three conditions of p. 110 are required.</summary>
    [Fact]
    public void LoopGainSearch_ReportsOnlyClockwiseCrossingsBeyondPlusOne()
    {
        var x = Enumerable.Range(0, 21).Select(i => (double)i).ToArray();
        // G = 2 + j(10 − ω): Im falls through 0 at ω = 10 (clockwise), Re = 2 ≥ 1 → reported.
        var cw  = x.Select(w => new Complex(2.0, 10.0 - w)).ToArray();
        var ccw = x.Select(w => new Complex(2.0, w - 10.0)).ToArray();
        var small = x.Select(w => new Complex(0.5, 10.0 - w)).ToArray();

        var hits = WspOhtomo.UnstableFrequenciesLoopGain(cw, x);
        Assert.Single(hits);
        Assert.Equal(10.0, hits[0], 9);
        Assert.Empty(WspOhtomo.UnstableFrequenciesLoopGain(ccw, x));
        Assert.Empty(WspOhtomo.UnstableFrequenciesLoopGain(small, x));
    }

    // ══ §2 — the probe pair (R-wsp3-9(a)/(g), synthetic half) ═══════════════

    /// <summary>The wsp of two probes in the GEN → LOAD orientation of Fig. 40 around an inner
    /// two-port <c>[Y]</c> and a feedback two-port <c>[Yf]</c>, by nodal analysis on the four nodes
    /// c (P1 G), a (P1 L), b (P2 G), d (P2 L) with <c>va = vc + vS1</c>, <c>vd = vb + vS2</c>.</summary>
    private static Complex[,] SyntheticPairWsp(WspTwoPortY y, WspTwoPortY f)
    {
        // Unknowns [vc, vb, iS1, iS2]:
        //   KCL c: Yf11 vc + Yf12 vd + iS1 = iP1   →  Yf11 vc + Yf12 vb + iS1 = iP1 − Yf12 vS2
        //   KCL d: Yf21 vc + Yf22 vd − iS2 = 0     →  Yf21 vc + Yf22 vb − iS2 = −Yf22 vS2
        //   KCL a: Y11 va + Y12 vb − iS1 = 0       →  Y11 vc + Y12 vb − iS1 = −Y11 vS1
        //   KCL b: Y21 va + Y22 vb + iS2 = iP2     →  Y21 vc + Y22 vb + iS2 = iP2 − Y21 vS1
        var a = new[,]
        {
            { f.Y11, f.Y12,  Complex.One,  Complex.Zero },
            { f.Y21, f.Y22,  Complex.Zero, -Complex.One },
            { y.Y11, y.Y12, -Complex.One,  Complex.Zero },
            { y.Y21, y.Y22,  Complex.Zero,  Complex.One },
        };
        var lu = new WspMatrix.Lu(a);
        Complex[][] rhs =
        [
            [Complex.Zero, Complex.Zero, -y.Y11, -y.Y21],    // series at probe 1
            [Complex.One,  Complex.Zero, Complex.Zero, Complex.Zero],   // shunt at probe 1
            [-f.Y12, -f.Y22, Complex.Zero, Complex.Zero],    // series at probe 2
            [Complex.Zero, Complex.Zero, Complex.Zero, Complex.One],    // shunt at probe 2
        ];
        var w = new Complex[4, 4];
        for (int row = 0; row < 4; row++)
        {
            var s = lu.Solve(rhs[row]);
            // columns: [iS1, vP1 = vc, iS2, vP2 = vb]
            w[row, 0] = s[2]; w[row, 1] = s[0]; w[row, 2] = s[3]; w[row, 3] = s[1];
        }
        return w;
    }

    /// <summary>
    /// <c>wsp_yparam2</c> returns the inner and feedback two-ports the wsp was built from, true
    /// matrices with <c>y21</c> at (2,1); both stimulus sets agree, so the residual is round-off;
    /// and <c>wsp_block_calc</c>'s identities close: <c>1 − {9} == {13}</c> (two derivations of
    /// the synthetic-FET loop gain), <c>1 − {10} == {14}</c>, <c>{1..4}</c> is <c>S</c> of
    /// <c>[Y]</c>, and <c>LGM == LGH</c> in the no-reverse-transfer limit (R-wsp3-9(g)).
    /// </summary>
    [Fact]
    public void ProbePair_RecoversBothTwoPorts_AndTheBlockCalcIdentitiesClose()
    {
        var r = new Random(31);
        var z0 = new Complex(50, 0);
        for (int trial = 0; trial < 200; trial++)
        {
            var y = new WspTwoPortY(Rnd(r) / 50.0 + 0.03, Rnd(r) / 200.0, Rnd(r) / 20.0, Rnd(r) / 50.0 + 0.03);
            var f = new WspTwoPortY(Rnd(r) / 50.0 + 0.03, Rnd(r) / 200.0, Rnd(r) / 200.0, Rnd(r) / 50.0 + 0.03);
            var w = SyntheticPairWsp(y, f);

            var pair = WspPair.YParam2(w, 1, 2);
            Close(y.Y11, pair.Inner.Y11, 1e-10, "y11"); Close(y.Y12, pair.Inner.Y12, 1e-10, "y12");
            Close(y.Y21, pair.Inner.Y21, 1e-10, "y21 at (2,1)"); Close(y.Y22, pair.Inner.Y22, 1e-10, "y22");
            Close(f.Y11, pair.Feedback.Y11, 1e-10, "yf11"); Close(f.Y12, pair.Feedback.Y12, 1e-10, "yf12");
            Close(f.Y21, pair.Feedback.Y21, 1e-10, "yf21"); Close(f.Y22, pair.Feedback.Y22, 1e-10, "yf22");

            Assert.True(WspPair.YParam2Residual(w, 1, 2) < 1e-9,
                $"residual {WspPair.YParam2Residual(w, 1, 2):G3} on an exact two-block network");

            var bc = WspPair.BlockCalc(w, 1, 2, z0);
            Assert.Equal(16, bc.Length);
            Close(Complex.One - bc[8],  bc[12], 1e-10, "1 − {9} == {13} (Eq. 159 vs Eq. 148)");
            Close(Complex.One - bc[9],  bc[13], 1e-10, "1 − {10} == {14} (determinant form vs Eq. 149)");
            Close(Complex.One - bc[10], bc[14], 1e-13, "1 − {11} == {15}");
            Close(Complex.One - bc[11], bc[15], 1e-13, "1 − {12} == {16}");

            var s = WspMatrix.ScatteringOfY(new[,] { { y.Y11, y.Y12 }, { y.Y21, y.Y22 } }, z0);
            Close(s[0, 0], bc[0], 1e-10, "{1} = s11"); Close(s[0, 1], bc[1], 1e-10, "{2} = s12");
            Close(s[1, 0], bc[2], 1e-10, "{3} = s21"); Close(s[1, 1], bc[3], 1e-10, "{4} = s22");
            var bb = WspPair.BlockBreakout(w, 1, 2, z0);
            Close(s, bb, 1e-10, "wsp_block_breakout = {1..4}");
            var sf = WspMatrix.ScatteringOfY(new[,] { { f.Y11, f.Y12 }, { f.Y21, f.Y22 } }, z0);
            Close(sf, WspPair.FbBreakout(w, 1, 2, z0), 1e-10, "wsp_fb_breakout = {5..8}");

            // No reverse transfer (y12 = yf21 = 0): LGM == LGH.
            var y0 = new WspTwoPortY(y.Y11, Complex.Zero, y.Y21, y.Y22);
            var f0 = new WspTwoPortY(f.Y11, f.Y12, Complex.Zero, f.Y22);
            var bc0 = WspPair.BlockCalc(new WspPairY(y0, f0), z0);
            Close(bc0[14], bc0[15], 1e-12, "LGM == LGH when y12 = yf21 = 0");
        }
    }

    /// <summary>A third connection between the region the two probes enclose and the rest of the
    /// network makes the two stimulus sets disagree — the residual is the diagnostic. Built by
    /// adding a fifth unknown: a conductance from inner node a to outer node d.</summary>
    [Fact]
    public void ProbePair_Residual_IsLargeWhenTheRegionHasAThirdConnection()
    {
        var y = new WspTwoPortY(0.02, 0.0, 0.05, 0.01);
        var f = new WspTwoPortY(0.03, -0.004, -0.004, 0.02);
        // The same nodal system as SyntheticPairWsp, with g from a to d: KCL a gains +g(va − vd),
        // KCL d gains +g(vd − va). va = vc + vS1, vd = vb + vS2.
        const double g = 0.01;
        var a = new[,]
        {
            { f.Y11,     f.Y12,      Complex.One,  Complex.Zero },
            { f.Y21 - g, f.Y22 + g,  Complex.Zero, -Complex.One },
            { y.Y11 + g, y.Y12 - g, -Complex.One,  Complex.Zero },
            { y.Y21,     y.Y22,      Complex.Zero,  Complex.One },
        };
        var lu = new WspMatrix.Lu(a);
        Complex[][] rhs =
        [
            [Complex.Zero, g, -y.Y11 - g, -y.Y21],
            [Complex.One,  Complex.Zero, Complex.Zero, Complex.Zero],
            [-f.Y12, -f.Y22 - g, g, Complex.Zero],
            [Complex.Zero, Complex.Zero, Complex.Zero, Complex.One],
        ];
        var w = new Complex[4, 4];
        for (int row = 0; row < 4; row++)
        {
            var s = lu.Solve(rhs[row]);
            w[row, 0] = s[2]; w[row, 1] = s[0]; w[row, 2] = s[3]; w[row, 3] = s[1];
        }
        double res = WspPair.YParam2Residual(w, 1, 2);
        Assert.True(res > 1e-3, $"residual {res:G3} should be large with a bypass around the probes");
    }

    // ══ §6 — the rank-1 update (R-wsp3-9(e), synthetic half) ═════════════════

    /// <summary>
    /// <c>ShuntUpdate</c> at the G node equals rebuilding the wsp with <c>ΔY</c> added to the G-side
    /// matrix; at the L node, with <c>ΔY</c> added to the L side — which is the test of the two
    /// δ-corrections the L-node form carries (the probe's own series row sees <c>vP + vS</c>, an
    /// injection at L bypasses the probe branch). A wrong correction shows up ONLY in the updated
    /// probe's own row and column, which is why every entry is compared.
    /// </summary>
    [Theory]
    [InlineData(WspSide.G)]
    [InlineData(WspSide.L)]
    public void ShuntUpdate_EqualsRebuildingTheNetworkWithTheElementAdded(WspSide side)
    {
        var r = new Random(41 + (int)side);
        for (int trial = 0; trial < 100; trial++)
        {
            int n = 1 + trial % 4;
            int k = trial % n;
            var yG = RandomY(r, n); var yL = RandomY(r, n);
            var w  = SyntheticWsp(yG, yL);
            var dY = Rnd(r);

            var yG2 = (Complex[,])yG.Clone(); var yL2 = (Complex[,])yL.Clone();
            if (side == WspSide.G) yG2[k, k] += dY; else yL2[k, k] += dY;
            var expected = SyntheticWsp(yG2, yL2);

            Close(expected, WspEnvelope.ShuntUpdate(w, k + 1, side, dY), 1e-9, $"rank-1 update at probe {k + 1} {side}");
        }
    }

    /// <summary><c>Terminate</c> is the two updates in sequence, and the load-pull reads
    /// <c>H0'</c>/<c>Y0'</c> off the updated matrix; an unpulled side is a single NaN on its axis.</summary>
    [Fact]
    public void Terminate_AndLoadpull_AreTheSequencedUpdates()
    {
        var r = new Random(53);
        var yG = RandomY(r, 3); var yL = RandomY(r, 3);
        var w  = SyntheticWsp(yG, yL);
        var z0 = new Complex(50, 0);

        var yS = Complex.One / WspEnvelope.GammaToZ(Complex.FromPolarCoordinates(0.5, Math.PI / 3), z0);
        var yLd = Complex.One / WspEnvelope.GammaToZ(Complex.FromPolarCoordinates(0.3, -2 * Math.PI / 3), z0);
        var so = WspEnvelope.StartingAdmittance(w, 1, WspSide.G);
        var lo = WspEnvelope.StartingAdmittance(w, 2, WspSide.L);

        var t = WspEnvelope.Terminate(w, 1, yS, so, 2, yLd, lo);
        var step = WspEnvelope.ShuntUpdate(WspEnvelope.ShuntUpdate(w, 1, WspSide.G, yS - so), 2, WspSide.L, yLd - lo);
        Close(step, t, 0.0, "Terminate = two ShuntUpdates");

        var env = WspEnvelope.Loadpull([w], [1e9], 1, 2, 3,
            [Complex.FromPolarCoordinates(0.5, Math.PI / 3)], [Complex.FromPolarCoordinates(0.3, -2 * Math.PI / 3)], z0);
        Close(t[5, 5], env.H0[0, 0, 0], 0.0, "H0' at probe 3");
        Close(t[4, 4], env.Y0[0, 0, 0], 0.0, "Y0' at probe 3");

        var half = WspEnvelope.Loadpull([w], [1e9], 0, 2, 3, [], [Complex.FromPolarCoordinates(0.3, -2 * Math.PI / 3)], z0);
        Assert.Single(half.GammaS);
        Assert.True(Complex.IsNaN(half.GammaS[0]));
        Close(WspEnvelope.ShuntUpdate(w, 2, WspSide.L, yLd - lo)[5, 5], half.H0[0, 0, 0], 0.0, "source unpulled");

        // Γ ↔ Z round trip and the circle grid.
        var g = Complex.FromPolarCoordinates(0.8, 1.1);
        Close(g, WspEnvelope.ZToGamma(WspEnvelope.GammaToZ(g, z0), z0), 1e-14, "Γ round trip");
        var circle = WspEnvelope.CircleGrid(0.8, 8);
        Assert.Equal(8, circle.Length);
        Assert.All(circle, c => Assert.Equal(0.8, c.Magnitude, 12));
        Assert.Equal(Math.PI / 2, circle[2].Phase, 12);
    }

    /// <summary>The precondition names the probe and the side, and passes when the bidirectional
    /// impedance is the declared termination.</summary>
    [Fact]
    public void RequireAtTermination_RefusesByName_AndAcceptsAnExactMatch()
    {
        var r = new Random(61);
        // G side of probe 1 is a bare termination: yG diagonal, decoupled.
        var yG = new Complex[2, 2]; yG[0, 0] = 1.0 / 50.0; yG[1, 1] = new Complex(0.01, 0.002);
        var yL = RandomY(r, 2);
        var w  = SyntheticWsp(yG, yL);

        WspEnvelope.RequireAtTermination([w], 1, WspSide.G, new Complex(50, 0), "PS");   // no throw

        var ex = Assert.Throws<ArgumentException>(() => WspEnvelope.RequireAtTermination([w], 1, WspSide.G, new Complex(75, 0), "PS"));
        Assert.Contains(WspEnvelope.NotAtTerminationKey, ex.Message);
        Assert.Contains("'PS'", ex.Message);

        var none = Assert.Throws<ArgumentException>(() => WspEnvelope.RequireAtTermination([w], 2, WspSide.L, null, "PL"));
        Assert.Contains("no Term on its L node", none.Message);
    }
}
