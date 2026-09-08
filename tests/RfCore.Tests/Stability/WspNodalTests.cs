// ================================================================
//  WspNodalTests.cs — brief-wsprobe-2's scalar-core gates (R-wsp2-14 (a)–(i)).
//
//  Every function of the reference document's single-probe library is a pure function of one
//  probe's 2×2 block of `wsp`. That makes the block itself the fixture: a random reduced two-port
//  [Y] is inverted back into the four transfer functions A, B, C, D the probe would have measured
//  (Fig. 16), and every identity the document states must then close on it. No engine, no netlist,
//  no solve — the end-to-end gates live in Engine.Tests.
//
//  Reference throughout: T. A. Winslow, General Circuit Analysis Using The WSProbe (2023).
// ================================================================

using System;
using System.Linq;
using System.Numerics;
using NumFlat;
using RfCore;
using RfCore.Stability;
using Xunit;

namespace RfCore.Tests.Stability;

public class WspNodalTests
{
    // ── The inverse of the reduction: [Y] → the probe's quad (Fig. 16) ───────
    //
    //  Eq. 44 reads [Y] = (1/C)·[[δ, −β], [−γ, α]] with α = |P|, β = |P| + A, γ = |P| − D and
    //  δ = |P| + A − D + 1. Reading it backwards:
    //
    //      C = H0 = 1/(y11 + y12 + y21 + y22)     (which is Eq. 93's 1/H0 = YG + YL)
    //      A = −C·(y12 + y22)                     (= −C·YL', the β − α line)
    //      D =  C·(y21 + y22)                     (= α − γ)
    //      B = (|P| + A·D)/C = |Y|·C              (which is Eq. 69's 1/Y0 = ZG + ZL)
    //
    //  so a random [Y] gives a quad that YParam maps straight back to it. That round trip is the
    //  first assertion of (a) and the reason every later identity here is a real test of the
    //  library rather than of the fixture.
    private static WspProbeQuad QuadFromY(Complex y11, Complex y12, Complex y21, Complex y22)
    {
        var c = Complex.One / (y11 + y12 + y21 + y22);
        var a = -c * (y12 + y22);
        var d =  c * (y21 + y22);
        var b = (y11 * y22 - y12 * y21) * c;
        return new WspProbeQuad(A: a, B: b, C: c, D: d);
    }

    private static Complex Rnd(Random r) => new(r.NextDouble() * 2 - 1, r.NextDouble() * 2 - 1);

    /// <summary>A random two-port; <paramref name="feedback"/> scales y12/y21 relative to the
    /// diagonal, so 0 is the zero-feedback case and 30 is heavy feedback.</summary>
    private static WspTwoPortY RandomY(Random r, double feedback = 1.0)
        => new(Rnd(r), feedback * Rnd(r), feedback * Rnd(r), Rnd(r));

    private static void Close(Complex expected, Complex actual, double tol, string what)
    {
        double scale = Math.Max(Math.Max(expected.Magnitude, actual.Magnitude), 1.0);
        double err   = (actual - expected).Magnitude / scale;
        Assert.True(err <= tol, $"{what}: expected {expected}, got {actual} (relative error {err:G3} > {tol:G1})");
    }

    private static void Rel(double expected, double actual, double tol, string what)
    {
        double err = Math.Abs(actual - expected) / Math.Max(Math.Abs(expected), 1e-300);
        Assert.True(err <= tol, $"{what}: expected {expected:G17}, got {actual:G17} (relative error {err:G3})");
    }

    // ══ (a) — the identities, on random two-ports ════════════════════════════

    /// <summary>
    /// Eq. 49, 60, 69, 77, 93, 94, 95 and every relation of §2.1–2.2, on 1,000 random two-ports at
    /// three feedback strengths. The reduction round trip (Eq. 44 of the quad built from [Y]) is
    /// asserted first, because if it failed everything after it would be measuring the fixture.
    /// </summary>
    [Theory]
    [InlineData(1.0)]     // ordinary feedback
    [InlineData(30.0)]    // |y12·y21| ≫ |y11·y22|
    [InlineData(0.05)]    // weak
    public void A_Identities_HoldOnRandomTwoPorts(double feedback)
    {
        var r = new Random(20260908 + (int)(feedback * 100));
        for (int trial = 0; trial < 1000; trial++)
        {
            var y0In = RandomY(r, feedback);
            var q    = QuadFromY(y0In.Y11, y0In.Y12, y0In.Y21, y0In.Y22);

            var y = WspReduction.YParam(q);
            var z = WspReduction.ZParam(q);

            // The round trip: Eq. 44 of the quad built from [Y] is [Y] again.
            Close(y0In.Y11, y.Y11, 1e-12, "y11 round trip");
            Close(y0In.Y12, y.Y12, 1e-12, "y12 round trip");
            Close(y0In.Y21, y.Y21, 1e-12, "y21 round trip");
            Close(y0In.Y22, y.Y22, 1e-12, "y22 round trip");

            // Eq. 49 — [Y] = [Z]⁻¹.
            var det = z.Z11 * z.Z22 - z.Z12 * z.Z21;
            Close(y.Y11,  z.Z22 / det, 1e-11, "Eq. 49 y11");
            Close(y.Y12, -z.Z12 / det, 1e-11, "Eq. 49 y12");
            Close(y.Y21, -z.Z21 / det, 1e-11, "Eq. 49 y21");
            Close(y.Y22,  z.Z11 / det, 1e-11, "Eq. 49 y22");

            // Eq. 60 — the two spellings of the bilateral loop gain agree.
            var lg = WspReduction.LoopGain(y);
            Close(lg, WspReduction.LoopGain(z), 1e-11, "Eq. 60 LG from [Z]");
            Close(lg, WspNodal.LoopGain(y, WspLoopGainKind.Bi, 50.0), 1e-15, "wsp_loopgain BI == LG");

            Complex zg = WspNodal.ZG(q), zl = WspNodal.ZL(q);
            Complex yg = WspNodal.YG(y), yl = WspNodal.YL(y);
            Complex h0 = WspNodal.H0(q), yy0 = WspNodal.Y0(q);

            // §2.1 — the Z and Y spellings of the bidirectional immittances (Eq. 65–68, 76).
            Close(z.Z11 - z.Z12, zg, 1e-11, "Eq. 67 ZG = z11 − z12");
            Close(z.Z22 - z.Z21, zl, 1e-11, "Eq. 68 ZL = z22 − z21");
            Close(WspReduction.ZGFromY(y), zg, 1e-10, "Eq. 65 ZG from |Y|");
            Close(WspReduction.ZLFromY(y), zl, 1e-10, "Eq. 66 ZL from |Y|");
            Close(y.Y11 + y.Y12, yg, 1e-15, "Eq. 76 YG = y11 + y12");
            Close(y.Y22 + y.Y21, yl, 1e-15, "Eq. 76 YL = y22 + y21");
            Close((Complex.One - q.D) / h0, yg, 1e-11, "Eq. 76 YG = (1/H0)(1 − iS/iP)");
            Close(q.D / h0,                 yl, 1e-11, "Eq. 76 YL = (1/H0)(iS/iP)");

            // Eq. 69 / 77 — only the sums are fundamental.
            Close(Complex.One / yy0, zg + zl, 1e-11, "Eq. 69 ZG + ZL = 1/Y0");
            Close(Complex.One / h0,  yg + yl, 1e-11, "Eq. 77 YG + YL = 1/H0");

            // Eq. 89/90, 93, 94, 95 — the open-port immittances and what they tie together.
            Complex zop = WspNodal.Zop(y), yop = WspNodal.Yop(z);
            Close(Complex.One / (y.Y11 + y.Y22), zop, 1e-15, "Eq. 89 Zop");
            Close(Complex.One / (z.Z11 + z.Z22), yop, 1e-15, "Eq. 90 Yop");
            Close(Complex.One / h0,  (Complex.One - lg) / zop, 1e-10, "Eq. 93 1/H0 = (1−LG)/Zop");
            Close(Complex.One / yy0, (Complex.One - lg) / yop, 1e-10, "Eq. 93 1/Y0 = (1−LG)/Yop");
            Close(zop / h0, yop / yy0, 1e-10, "Eq. 94 Zop/H0 = Yop/Y0");
            var detY = y.Y11 * y.Y22 - y.Y12 * y.Y21;
            Close(yg + yl, detY * (zg + zl), 1e-10, "Eq. 95 YG + YL = |Y|(ZG + ZL)");
        }
    }

    /// <summary>
    /// The zero-feedback case, Eq. 81/82 and 89/90: with <c>y12 = y21 = 0</c> the two stimulations
    /// agree (<c>ZG = 1/YG</c>), the open-port immittances collapse onto the driving-point ones, and
    /// every loop gain but the two circulator forms is exactly zero.
    /// </summary>
    [Fact]
    public void A_ZeroFeedback_ZgIsOneOverYg_AndEveryNonCirculatorLoopGainIsZero()
    {
        var r = new Random(99);
        for (int trial = 0; trial < 200; trial++)
        {
            var yin = new WspTwoPortY(Rnd(r), Complex.Zero, Complex.Zero, Rnd(r));
            var q   = QuadFromY(yin.Y11, yin.Y12, yin.Y21, yin.Y22);
            var y   = WspReduction.YParam(q);

            Close(Complex.One / WspNodal.YG(y), WspNodal.ZG(q), 1e-10, "Eq. 81 ZG = 1/YG");
            Close(Complex.One / WspNodal.YL(y), WspNodal.ZL(q), 1e-10, "Eq. 82 ZL = 1/YL");
            Close(Complex.One / y.Y11, WspNodal.ZG(q), 1e-10, "Eq. 89 ZG = 1/y11");
            Close(Complex.One / y.Y22, WspNodal.ZL(q), 1e-10, "Eq. 89 ZL = 1/y22");

            foreach (var kind in new[] { WspLoopGainKind.Bi,  WspLoopGainKind.Hst, WspLoopGainKind.Mb,
                                         WspLoopGainKind.Mbr, WspLoopGainKind.Gft, WspLoopGainKind.Gftr })
                Assert.True(WspNodal.LoopGain(y, kind, 50.0).Magnitude < 1e-12,
                    $"{kind} must be 0 with no feedback, got {WspNodal.LoopGain(y, kind, 50.0)}");
        }
    }

    // ══ (b) — the circulator forms against their S-parameter equivalents ═════

    /// <summary>
    /// Eq. 99 against Eq. 98 and Eq. 97 against Eq. 96, on random two-ports at several references.
    /// The <c>ȳ</c> forms are what ship; the <c>S</c> forms — the reflection coefficient a real
    /// circulator's third port would see — are the oracle.
    /// </summary>
    [Theory]
    [InlineData(50.0)]
    [InlineData(12.5)]
    [InlineData(377.0)]
    public void B_CirculatorLoopGains_MatchTheirSParameterForms(double z0)
    {
        var r = new Random(4242 + (int)z0);
        for (int trial = 0; trial < 500; trial++)
        {
            // Scale the admittances to the reference so ȳ stays O(1) and (1 − S12) is not tiny.
            var yin = RandomY(r);
            var y   = new WspTwoPortY(yin.Y11 / z0, yin.Y12 / z0, yin.Y21 / z0, yin.Y22 / z0);

            Close(WspNodal.LoopGainFwdFromS(y, z0), WspNodal.LoopGain(y, WspLoopGainKind.Uni, z0),
                  1e-12, "Eq. 99 vs Eq. 98 (LGF)");
            Close(WspNodal.LoopGainRevFromS(y, z0), WspNodal.LoopGain(y, WspLoopGainKind.Rev, z0),
                  1e-12, "Eq. 97 vs Eq. 96 (LGR)");
            Close(WspNodal.LoopGain(y, WspLoopGainKind.Uni, z0),
                  WspNodal.LoopGain(y, WspLoopGainKind.Uni, z0), 0.0, "FOR alias");
        }
    }

    /// <summary>
    /// With <c>y12 = y21 = 0</c> both circulator loop gains collapse to <c>S11·S22 = ΓG·ΓL</c> —
    /// Jackson's stability index (§3.7, App. B), the product of the two reflections a wave sees on
    /// one round trip through the node.
    /// </summary>
    [Fact]
    public void B_ZeroFeedback_BothCirculatorFormsAreTheReflectionProduct()
    {
        const double z0 = 50.0;
        var r = new Random(7);
        for (int trial = 0; trial < 200; trial++)
        {
            var y  = new WspTwoPortY(Rnd(r) / z0, Complex.Zero, Complex.Zero, Rnd(r) / z0);
            var g1 = (Complex.One - z0 * y.Y11) / (Complex.One + z0 * y.Y11);
            var g2 = (Complex.One - z0 * y.Y22) / (Complex.One + z0 * y.Y22);

            Close(g1 * g2, WspNodal.LoopGain(y, WspLoopGainKind.Rev, z0), 1e-12, "LGR = ΓG·ΓL");
            Close(g1 * g2, WspNodal.LoopGain(y, WspLoopGainKind.Uni, z0), 1e-12, "LGF = ΓG·ΓL");
        }
    }

    /// <summary>The document's kind strings, with <c>FOR</c> as the accepted alias of <c>UNI</c>
    /// (p. 68), and an unknown one listing them all rather than falling back.</summary>
    [Fact]
    public void B_KindStrings_AreTheDocumentsWithForAsAnAlias()
    {
        Assert.Equal(WspLoopGainKind.Uni, WspNodal.ParseKind("FOR"));
        Assert.Equal(WspLoopGainKind.Uni, WspNodal.ParseKind("uni"));
        Assert.Equal(WspLoopGainKind.Gftr, WspNodal.ParseKind(" gftr "));
        var ex = Assert.Throws<ArgumentException>(() => WspNodal.ParseKind("LOOP"));
        Assert.Contains("GFTR", ex.Message);
        Assert.Contains("FOR is an accepted alias", ex.Message);
    }

    // ══ (e) — encirclements ══════════════════════════════════════════════════

    /// <summary>
    /// <c>enc</c> on the synthetic loci <c>e^{−jkθ}</c>, <c>θ</c> over <c>[0, 2π)</c>: the running
    /// count ends at <c>k</c>, clockwise positive (E.3's minus sign). A locus that touches the
    /// origin's neighbourhood without turning ends at 0.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(-2)]
    public void E_Encirclements_CountClockwiseTurns(int k)
    {
        const int n = 2000;
        var locus = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            double th = 2.0 * Math.PI * i / n;
            locus[i] = Complex.FromPolarCoordinates(1.0, -k * th);
        }

        var enc = WspKurokawa.Encirclements(locus);
        Assert.Equal(n, enc.Length);
        Assert.Equal(0.0, enc[0], 12);
        Assert.Equal(k, (int)Math.Round(enc[^1]));
    }

    /// <summary>A locus that swings across the plane but never encircles the origin ends at 0.</summary>
    [Fact]
    public void E_NonEncirclingLocus_EndsAtZero()
    {
        const int n = 1000;
        var locus = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            double th = 2.0 * Math.PI * i / n;
            locus[i]  = new Complex(2.0, 0.0) + Complex.FromPolarCoordinates(1.0, -th);  // circle of radius 1 about 2
        }
        Assert.Equal(0, (int)Math.Round(WspKurokawa.Encirclements(locus)[^1]));
    }

    // ══ (d, i) — the Kurokawa search's step direction ════════════════════════

    /// <summary>
    /// The direction rule, on a synthetic locus with no circuit behind it: <c>g(ω)</c> crossing the
    /// negative real axis from the lower half-plane to the upper (a clockwise crossing) is reported;
    /// the same crossing traversed the other way is not, because that is the steady-state side of a
    /// stable resonance.
    /// </summary>
    [Fact]
    public void D_KurokawaSearch_ReportsOnlyClockwiseCrossings()
    {
        // g(ω) = (ω − 10) · j − 1  →  Re = −1 everywhere, Im rising through 0 at ω = 10.
        var x  = Enumerable.Range(0, 21).Select(i => (double)i).ToArray();
        var cw = x.Select(w => Complex.One / new Complex(-1.0, w - 10.0)).ToArray();   // T = 1/g
        var ccw = x.Select(w => Complex.One / new Complex(-1.0, 10.0 - w)).ToArray();

        var hitsCw = WspKurokawa.UnstableFrequencies(cw, x);
        Assert.Single(hitsCw);
        Assert.Equal(10.0, hitsCw[0], 9);

        Assert.Empty(WspKurokawa.UnstableFrequencies(ccw, x));
    }

    /// <summary>A clockwise crossing on the POSITIVE real axis fails <c>Re(g) ≤ 0</c> and is not
    /// reported: all three of Eq. 107's conditions are required, not two.</summary>
    [Fact]
    public void D_KurokawaSearch_RequiresANegativeRealPart()
    {
        var x = Enumerable.Range(0, 21).Select(i => (double)i).ToArray();
        var t = x.Select(w => Complex.One / new Complex(+1.0, w - 10.0)).ToArray();
        Assert.Empty(WspKurokawa.UnstableFrequencies(t, x));
    }

    /// <summary>The normalised driving-point loci divide by a positive real, so they carry the same
    /// start-up signature as the raw ones and are bounded by 1 (R-wsp2-14(i), overview D-12).</summary>
    [Fact]
    public void I_NormalizedLoci_AreBoundedAndReportTheSameKurokawaFrequencies()
    {
        // A series resonance in 1/Y0: 1/Y0 = ZG + ZL sweeps upward through the negative real axis.
        var x  = Enumerable.Range(0, 201).Select(i => 5.0 + i * 0.05).ToArray();
        var zg = new Complex[x.Length];
        var zl = new Complex[x.Length];
        var y0 = new Complex[x.Length];
        var nz = new Complex[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            zg[i] = new Complex(-2.0, 0.3 * (x[i] - 10.0));    // the active side
            zl[i] = new Complex(1.0, 0.2 * (x[i] - 10.0));
            y0[i] = Complex.One / (zg[i] + zl[i]);
            nz[i] = WspNodal.NormalizedLocusSeries(zg[i], zl[i]);
            Assert.True(nz[i].Magnitude <= 1.0 + 1e-12, $"|nZ| = {nz[i].Magnitude} > 1 at x = {x[i]}");
        }

        var fromY0 = WspKurokawa.UnstableFrequencies(y0, x);
        var fromNz = WspKurokawa.UnstableFrequencies(nz.Select(v => Complex.One / v).ToArray(), x);
        Assert.NotEmpty(fromY0);
        Assert.Equal(fromY0.Length, fromNz.Length);
        for (int i = 0; i < fromY0.Length; i++)
            Assert.Equal(fromY0[i], fromNz[i], 9);
    }

    /// <summary>The reserved margin is registered and refuses, citing the paper (overview D-12).</summary>
    [Fact]
    public void I_StabilityMargin_IsRegisteredAndRefuses()
    {
        var q  = QuadFromY(new Complex(0.02, 0), new Complex(0, -1e-3), new Complex(0.05, 0), new Complex(0.01, 0));
        var ex = Assert.Throws<NotSupportedException>(() => WspNodal.StabilityMargin(q));
        Assert.Contains("wsprobe.margin-not-transcribed", ex.Message);
        Assert.Contains("EuMIC", ex.Message);
        Assert.Contains("10.23919/EuMIC61603.2024.10732614", ex.Message);
    }

    // ══ (g) — the immittance models ══════════════════════════════════════════

    /// <summary>
    /// E.11's round trips: the parallel pair rebuilt by <c>wsp_zprc</c> and the series pair by
    /// <c>wsp_zsrc</c> return the original immittance exactly.
    /// </summary>
    [Fact]
    public void G_ImmittanceModels_RoundTrip()
    {
        var r = new Random(31415);
        for (int trial = 0; trial < 500; trial++)
        {
            double f = 1e6 + r.NextDouble() * 1e10;
            var y = new Complex(1e-3 + r.NextDouble() * 0.1, (r.NextDouble() * 2 - 1) * 0.1);

            Close(Complex.One / y,
                  ImmittanceModels.Zprc(ImmittanceModels.YToPr(y), ImmittanceModels.YToPc(y, f), f),
                  1e-12, "wsp_zprc(y_to_pr, y_to_pc) = 1/Y");

            if ((Complex.One / y).Imaginary < 0)
                Close(Complex.One / y,
                      ImmittanceModels.Zsrc(ImmittanceModels.YToSr(y), ImmittanceModels.YToSc(y, f), f),
                      1e-12, "wsp_zsrc(y_to_sr, y_to_sc) = 1/Y");
        }
    }

    /// <summary>
    /// The unit deviation, asserted against the numbers rather than the document's scaled ones: a
    /// 100 pF parallel capacitance reads 1e-10 F and a 2 nH series inductance 2e-9 H — farads and
    /// henries, never picofarads and nanohenries.
    /// </summary>
    [Fact]
    public void G_ImmittanceModels_ReturnBaseSiFaradsAndHenries()
    {
        const double f = 1e9, w = 2 * Math.PI * f;
        const double c = 100e-12, l = 2e-9, rp = 250.0;

        var yPar = new Complex(1.0 / rp, w * c);
        Assert.Equal(rp, ImmittanceModels.YToPr(yPar), 9);
        Rel(c, ImmittanceModels.YToPc(yPar, f), 1e-12, "y_to_pc is 100 pF expressed in FARADS");

        var ySer = Complex.One / new Complex(3.0, w * l);
        Assert.Equal(3.0, ImmittanceModels.YToSr(ySer), 9);
        Rel(l, ImmittanceModels.YToSl(ySer, f), 1e-12, "y_to_sl is 2 nH expressed in HENRIES");

        // A pure parallel inductor: L = −1/(Im(Y)·ω).
        var yInd = Complex.One / new Complex(0.0, w * l);
        Rel(l, ImmittanceModels.YToPl(yInd, f), 1e-12, "y_to_pl in henries");

        // The Z-side duals are the Y functions of 1/Z, so they agree by construction.
        Rel(ImmittanceModels.YToPc(yPar, f), ImmittanceModels.ZToPc(Complex.One / yPar, f), 1e-12, "z_to_pc dual");
        Rel(ImmittanceModels.YToSl(ySer, f), ImmittanceModels.ZToSl(Complex.One / ySer, f), 1e-12, "z_to_sl dual");
        Assert.Equal(3.0, ImmittanceModels.ZToSr(Complex.One / ySer), 9);
    }

    /// <summary>E.4's <c>freq + 1e-9</c> guard is not reproduced: a series capacitance at DC is an
    /// open, and NaN says so (overview D-7).</summary>
    [Fact]
    public void G_ZsrcAtDc_IsNaN_NotAGuardedLargeNumber()
    {
        Assert.True(Complex.IsNaN(ImmittanceModels.Zsrc(10.0, 1e-12, 0.0)));
    }

    // ══ (h) — renormalisation ════════════════════════════════════════════════

    /// <summary>
    /// <c>wsp_zo_renorm_s</c> equals the transcribed E.10 matrix expression
    /// <c>S' = F·(I − conj(Zr)·Y)·(I + Zr·Y)⁻¹·F⁻¹</c>. The shipped path calls
    /// <see cref="RFNetwork.SToS(Mat{Complex}, Complex[], Complex[])"/>; the transcription lives
    /// here, as the oracle, and nowhere else.
    /// </summary>
    [Fact]
    public void H_ZoRenorm_EqualsTheTranscribedE10Expression()
    {
        var r = new Random(1618);
        for (int trial = 0; trial < 300; trial++)
        {
            var s  = RandomS(r);
            var zg = new Complex(5.0 + r.NextDouble() * 90.0, (r.NextDouble() * 2 - 1) * 40.0);
            var zl = new Complex(5.0 + r.NextDouble() * 90.0, (r.NextDouble() * 2 - 1) * 40.0);

            var shipped   = WspRenorm.ZoRenormS(zg, s, zl);
            var transcribed = E10Transcription(s, zg, zl);
            for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                Close(transcribed[i, j], shipped[i, j], 1e-11, $"S'({i + 1},{j + 1})");
        }
    }

    /// <summary>With both capacitances zero, <c>wsp_rc_renorm_s</c> is <c>wsp_zo_renorm_s</c> to
    /// real references — the second function's own degenerate case, and the cheapest check that its
    /// two steps are wired in the right order.</summary>
    [Fact]
    public void H_RcRenormWithNoCapacitance_IsZoRenormToRealReferences()
    {
        var r = new Random(2718);
        for (int trial = 0; trial < 200; trial++)
        {
            var s  = RandomS(r);
            double rg = 5.0 + r.NextDouble() * 90.0, rl = 5.0 + r.NextDouble() * 90.0;

            var a = WspRenorm.RcRenormS(rg, 0.0, s, rl, 0.0, 2.4e9);
            var b = WspRenorm.ZoRenormS(rg, s, rl);
            for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                Close(b[i, j], a[i, j], 1e-11, $"S'({i + 1},{j + 1})");
        }
    }

    /// <summary>A negative reference resistance is refused, not absorbed by an
    /// <c>abs()</c> as the document's own code does.</summary>
    [Fact]
    public void H_NegativeReference_IsRefused()
    {
        var s = RandomS(new Random(5));
        var e1 = Assert.Throws<ArgumentException>(() => WspRenorm.ZoRenormS(new Complex(-10, 5), s, 50.0));
        Assert.Contains(WspRenorm.NegativeReferenceKey, e1.Message);
        var e2 = Assert.Throws<ArgumentException>(() => WspRenorm.RcRenormS(-10, 1e-12, s, 50, 0, 1e9));
        Assert.Contains(WspRenorm.NegativeReferenceKey, e2.Message);
    }

    /// <summary>The parallel capacitances really are absorbed: at 50 Ω references the renormalised
    /// network is the original with <c>jωC</c> across each port, so its Y differs from the original
    /// Y by exactly that diagonal.</summary>
    [Fact]
    public void H_RcRenorm_AbsorbsTheParallelCapacitances()
    {
        const double f = 3e9, w = 2 * Math.PI * f, cg = 0.4e-12, cl = 1.1e-12;
        var s = RandomS(new Random(11));

        var y0 = RFNetwork.SToY(s, new Complex(50, 0));
        var y1 = RFNetwork.SToY(WspRenorm.RcRenormS(50, cg, s, 50, cl, f), new Complex(50, 0));

        Close(new Complex(0, w * cg), y1[0, 0] - y0[0, 0], 1e-11, "port 1 shunt C absorbed");
        Close(new Complex(0, w * cl), y1[1, 1] - y0[1, 1], 1e-11, "port 2 shunt C absorbed");
        Close(y0[0, 1], y1[0, 1], 1e-11, "y12 untouched");
        Close(y0[1, 0], y1[1, 0], 1e-11, "y21 untouched");
    }

    /// <summary>App. E.10, transcribed as printed — the oracle for
    /// <see cref="H_ZoRenorm_EqualsTheTranscribedE10Expression"/> and nothing else.</summary>
    private static Mat<Complex> E10Transcription(Mat<Complex> s, Complex zg, Complex zl)
    {
        var y = RFNetwork.SToY(s, new Complex(WspRenorm.DocumentSourceReferenceOhm, 0));

        // F = diag(1/(2√Re Z)),  Zr = diag(ZG, ZL)
        var f0 = 1.0 / (2.0 * Math.Sqrt(zg.Real));
        var f1 = 1.0 / (2.0 * Math.Sqrt(zl.Real));

        // A = I − conj(Zr)·Y ; B = I + Zr·Y   (row i scaled by the i-th diagonal entry)
        var a = new Mat<Complex>(2, 2);
        var b = new Mat<Complex>(2, 2);
        Complex[] zr = [zg, zl];
        for (int i = 0; i < 2; i++)
        for (int j = 0; j < 2; j++)
        {
            var id = i == j ? Complex.One : Complex.Zero;
            a[i, j] = id - Complex.Conjugate(zr[i]) * y[i, j];
            b[i, j] = id + zr[i] * y[i, j];
        }

        // S' = F·A·B⁻¹·F⁻¹
        var detB = b[0, 0] * b[1, 1] - b[0, 1] * b[1, 0];
        var bi   = new Mat<Complex>(2, 2);
        bi[0, 0] =  b[1, 1] / detB; bi[0, 1] = -b[0, 1] / detB;
        bi[1, 0] = -b[1, 0] / detB; bi[1, 1] =  b[0, 0] / detB;

        var ab = new Mat<Complex>(2, 2);
        for (int i = 0; i < 2; i++)
        for (int j = 0; j < 2; j++)
            ab[i, j] = a[i, 0] * bi[0, j] + a[i, 1] * bi[1, j];

        double[] fd = [f0, f1];
        var outM = new Mat<Complex>(2, 2);
        for (int i = 0; i < 2; i++)
        for (int j = 0; j < 2; j++)
            outM[i, j] = fd[i] * ab[i, j] / fd[j];
        return outM;
    }

    private static Mat<Complex> RandomS(Random r)
    {
        var m = new Mat<Complex>(2, 2);
        for (int i = 0; i < 2; i++)
        for (int j = 0; j < 2; j++)
            m[i, j] = Complex.FromPolarCoordinates(r.NextDouble() * 0.8, (r.NextDouble() * 2 - 1) * Math.PI);
        return m;
    }

    // ══ (f, partial) — wsp_gain and wsp_impedance read the right elements ════

    /// <summary>
    /// The two remote functions read the elements App. C and App. D's code reads. The fixture is a
    /// <c>wsp</c> whose every element is a distinct marker, so a transposed or off-by-one index is
    /// a different number rather than a plausible one — and T-14's mistake (the drain index in both
    /// factors) would make <c>wsp_gain</c> identically 0 dB.
    /// </summary>
    [Fact]
    public void F_RemoteFunctions_ReadTheAppendixCAndDElements()
    {
        const int n = 3;                       // three probes → 6×6
        var wsp = new Complex[2 * n, 2 * n];
        for (int r = 0; r < 2 * n; r++)
        for (int c = 0; c < 2 * n; c++)
            wsp[r, c] = new Complex(1 + r * 10 + c, 1 + r - c);

        // wsp_impedance(wsp, 1, 3) series  = wsp(1, 6)/wsp(1, 5)
        Close(wsp[0, 5] / wsp[0, 4], WspTransfer.Impedance(wsp, 1, 3), 1e-15, "App. C series form");
        // Eq. 37's shunt form = wsp(2, 6)/wsp(2, 5)
        Close(wsp[1, 5] / wsp[1, 4], WspTransfer.Impedance(wsp, 1, 3, WspStimulus.Shunt), 1e-15, "Eq. 37 shunt form");

        // wsp_gain(wsp, S=1, G=2, D=3) = 10 log10 |Re(wsp(1,6)·conj(wsp(1,5))) / Re(wsp(1,4)·conj(wsp(1,3)))|
        double num = (wsp[0, 5] * Complex.Conjugate(wsp[0, 4])).Real;
        double den = (wsp[0, 3] * Complex.Conjugate(wsp[0, 2])).Real;
        Assert.Equal(10.0 * Math.Log10(Math.Abs(num / den)), WspTransfer.GainDb(wsp, 1, 2, 3), 12);

        // T-14: had both factors used the drain index, the ratio would be 1 and the gain 0 dB.
        Assert.True(Math.Abs(WspTransfer.GainDb(wsp, 1, 2, 3)) > 1.0,
            "wsp_gain must not be 0 dB here — that is what Eq. 199/201 as printed would give.");
    }

    // ══ (f) — the gain definitions ═══════════════════════════════════════════

    /// <summary>
    /// The three gains agree with each other where they must: with the source conjugately matched
    /// to <c>Γin</c>, <c>GT = GP</c>; with the load conjugately matched to <c>Γout</c>,
    /// <c>GT = GA</c>; and with both ports matched (<c>ΓS = ΓL = 0</c>) all three are
    /// <c>|S21|²</c>. <c>Gmax</c> comes from the repository's one MAG/MSG.
    /// </summary>
    [Fact]
    public void F_GainDefinitions_AgreeAtTheirMatchedLimits()
    {
        var r = new Random(161803);
        for (int trial = 0; trial < 300; trial++)
        {
            var s = RandomS(r);
            Complex s11 = s[0, 0], s12 = s[0, 1], s21 = s[1, 0], s22 = s[1, 1];

            // With both Γs zero GT is |S21|^2 exactly; GP and GA are not, because each still
            // divides by the mismatch its own definition leaves un-terminated (Γin = S11, Γout = S22).
            var flat = GainDefinitions.Compute(Complex.Zero, s11, s12, s21, s22, Complex.Zero);
            double s21sq = s21.Magnitude * s21.Magnitude;
            Assert.Equal(10.0 * Math.Log10(s21sq), flat.GtDb, 9);
            Assert.Equal(10.0 * Math.Log10(s21sq / (1 - s11.Magnitude * s11.Magnitude)), flat.GpDb, 9);
            Assert.Equal(10.0 * Math.Log10(s21sq / (1 - s22.Magnitude * s22.Magnitude)), flat.GaDb, 9);
            Assert.Equal(RFNetwork.MaxGain(s), flat.GmaxDb, 9);

            // GT = GP when ΓS = conj(Γin).
            var gamL = Complex.FromPolarCoordinates(r.NextDouble() * 0.7, (r.NextDouble() * 2 - 1) * Math.PI);
            var gin  = GainDefinitions.GammaIn(s11, s12, s21, s22, gamL);
            var m1   = GainDefinitions.Compute(Complex.Conjugate(gin), s11, s12, s21, s22, gamL);
            Assert.Equal(m1.GpDb, m1.GtDb, 8);

            // GT = GA when ΓL = conj(Γout).
            var gamS = Complex.FromPolarCoordinates(r.NextDouble() * 0.7, (r.NextDouble() * 2 - 1) * Math.PI);
            var gout = GainDefinitions.GammaOut(s11, s12, s21, s22, gamS);
            var m2   = GainDefinitions.Compute(gamS, s11, s12, s21, s22, Complex.Conjugate(gout));
            Assert.Equal(m2.GaDb, m2.GtDb, 8);
        }
    }

    /// <summary><c>_dB</c> is a POWER dB — 10·log10|M| (E.2), not 20.</summary>
    [Fact]
    public void F_Db_IsTenLogTen()
    {
        Assert.Equal(10.0, GainDefinitions.Db(new Complex(10.0, 0.0)), 12);
        Assert.Equal(0.0,  GainDefinitions.Db(new Complex(0.0, -1.0)), 12);
    }

    // ══ (§2.4) — the nodal conjugate reflection coefficient ══════════════════

    /// <summary>E.7's <c>Γ = (ZG − conj(ZL))/(ZG + conj(ZL))</c>: zero exactly when the two sides
    /// of the probe are conjugate matches, and 1 when one side is an open.</summary>
    [Fact]
    public void NodalGamma_IsZeroAtAConjugateMatch()
    {
        var zg = new Complex(35.0, 22.0);
        Assert.True(WspNodal.NodalGamma(zg, Complex.Conjugate(zg)).Magnitude < 1e-15);
        Assert.Equal(1.0, WspNodal.NodalGamma(new Complex(1e300, 0), new Complex(50, 0)).Magnitude, 9);
    }
}
