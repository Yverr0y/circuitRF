using System;
using System.Numerics;
using NumFlat;

namespace RfCore.Stability;

/// <summary>The two two-ports a probe pair brackets: the inner block <c>[Y]</c> and the feedback
/// block <c>[Yf]</c> (Fig. 40). True matrices — each unknown pair of Eq. 137 is a ROW, so no
/// transpose is involved.</summary>
public readonly record struct WspPairY(WspTwoPortY Inner, WspTwoPortY Feedback);

/// <summary>
/// Everything the reference document derives from a <b>pair</b> of probes: the inner and feedback
/// two-ports (§5.1, Eq. 137–139), the sixteen outputs of <c>wsp_block_calc</c> (§5.2–5.4,
/// Eq. 142–151) and the four appendix helpers (E.5, E.6, E.8, E.9).
///
/// <para>T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023); brief-wsprobe-3
/// §2. Pure functions over one <c>2N×2N</c> <c>wsp</c> block; the cube-facing wrappers live in the
/// expression engine (overview D-6).</para>
///
/// <para><b>Orientation (Fig. 40, GEN → LOAD).</b> The inner block sits between probe 1's <b>L</b>
/// terminal and probe 2's <b>G</b> terminal; the feedback block is everything else, between
/// probe 1's <b>G</b> terminal and probe 2's <b>L</b> terminal. Port 1 of <c>[Y]</c> is probe 1's
/// L side, port 2 is probe 2's G side; port 1 of <c>[Yf]</c> is probe 1's G side, port 2 is
/// probe 2's L side.</para>
/// </summary>
public static class WspPair
{
    // ── §5.1: the two-ports from the series stimuli (Eq. 137–139) ───────────

    /// <summary>
    /// <c>wsp_yparam2(wsp, idx1, idx2)</c> — the inner and feedback two-ports from the two SERIES
    /// stimuli (Eq. 137–139). Re-derived from Kirchhoff on Fig. 40:
    ///
    /// <code>
    ///   inner   : V_a = vP1 + vS1,  V_b = vP2,        I_a = +iS1,  I_b = −iS2
    ///   feedback: V_c = vP1,        V_d = vP2 + vS2,  I_c = −iS1,  I_d = +iS2
    ///
    ///   A = [[ VV11 + 1, VV12 ], [ VV21, VV22 ]]        B = [[ VV11, VV12 ], [ VV21, VV22 + 1 ]]
    ///   A·[y11; y12]   = [ VI11;  VI21 ]                 A·[y21; y22]   = [ −VI12; −VI22 ]
    ///   B·[yf11; yf12] = [ −VI11; −VI21 ]                B·[yf21; yf22] = [  VI12;  VI22 ]
    /// </code>
    ///
    /// <para>The <c>+1</c> lands on <c>A(1,1)</c> and on <c>B(2,2)</c>, not symmetrically — that
    /// asymmetry IS the orientation (probe 1's series source is inside the inner block's port-1
    /// voltage, probe 2's inside the feedback block's port-2 voltage). It is not a typo. Each
    /// unknown pair is a ROW of the two-port, so the true matrices come out with no transpose;
    /// <c>y21</c> of a non-reciprocal inner block sits at (2,1).</para>
    /// </summary>
    public static WspPairY YParam2(Complex[,] wsp, int idx1, int idx2)
    {
        var b = WspMatrix.Blocks(wsp, [idx1, idx2]);
        var vv = b.VV; var vi = b.VI;

        var (a11, a12, a21, a22) = (vv[0, 0] + Complex.One, vv[0, 1], vv[1, 0], vv[1, 1]);
        var (b11, b12, b21, b22) = (vv[0, 0], vv[0, 1], vv[1, 0], vv[1, 1] + Complex.One);

        var (y11, y12)   = Solve2(a11, a12, a21, a22,  vi[0, 0],  vi[1, 0]);
        var (y21, y22)   = Solve2(a11, a12, a21, a22, -vi[0, 1], -vi[1, 1]);
        var (yf11, yf12) = Solve2(b11, b12, b21, b22, -vi[0, 0], -vi[1, 0]);
        var (yf21, yf22) = Solve2(b11, b12, b21, b22,  vi[0, 1],  vi[1, 1]);

        return new WspPairY(new WspTwoPortY(y11, y12, y21, y22), new WspTwoPortY(yf11, yf12, yf21, yf22));
    }

    /// <summary>
    /// The same two-ports from the two SHUNT stimuli — the independent second set of equations
    /// per block, with the same side bookkeeping (no series source, so every port voltage is a
    /// <c>vP</c>; the injected current enters the block whose port sits at the injected G node):
    ///
    /// <code>
    ///   inner   : V_a = vP1,  V_b = vP2,  I_a = +iS1,       I_b = δ_i2 − iS2
    ///   feedback: V_c = vP1,  V_d = vP2,  I_c = δ_i1 − iS1, I_d = +iS2
    ///
    ///   A' = [[ IV11, IV12 ], [ IV21, IV22 ]]  (= B')
    ///   A'·[y11; y12]   = [ II11; II21 ]             A'·[y21; y22]   = [ −II12; 1 − II22 ]
    ///   A'·[yf11; yf12] = [ 1 − II11; −II21 ]        A'·[yf21; yf22] = [ II12; II22 ]
    /// </code>
    ///
    /// <para>Not in the document. Its only use is <see cref="YParam2Residual"/>.</para>
    /// </summary>
    public static WspPairY YParam2Shunt(Complex[,] wsp, int idx1, int idx2)
    {
        var b = WspMatrix.Blocks(wsp, [idx1, idx2]);
        var iv = b.IV; var ii = b.II;
        var (a11, a12, a21, a22) = (iv[0, 0], iv[0, 1], iv[1, 0], iv[1, 1]);

        var (y11, y12)   = Solve2(a11, a12, a21, a22, ii[0, 0], ii[1, 0]);
        var (y21, y22)   = Solve2(a11, a12, a21, a22, -ii[0, 1], Complex.One - ii[1, 1]);
        var (yf11, yf12) = Solve2(a11, a12, a21, a22, Complex.One - ii[0, 0], -ii[1, 0]);
        var (yf21, yf22) = Solve2(a11, a12, a21, a22, ii[0, 1], ii[1, 1]);

        return new WspPairY(new WspTwoPortY(y11, y12, y21, y22), new WspTwoPortY(yf11, yf12, yf21, yf22));
    }

    /// <summary>
    /// <c>wsp_yparam2_residual(wsp, idx1, idx2)</c> — <c>max|Y_series − Y_shunt|</c> over the eight
    /// entries, relative to <c>max|Y_series|</c>. On an exact linear solve it is at round-off; a
    /// large value means the two probes do NOT bracket a two-port with everything else outside —
    /// there is a third connection between the region they enclose and the rest of the network.
    /// (A connection to GROUND inside the region is not that: ground is not a coupling path, and a
    /// shunt element at an inner node is simply part of the inner block's <c>y11</c> or <c>y22</c>.)
    ///
    /// <para>The document has no such check; it is cheap and it is the first thing a designer
    /// wants to know when a block's parameters look wrong.</para>
    /// </summary>
    public static double YParam2Residual(Complex[,] wsp, int idx1, int idx2)
    {
        var s = YParam2(wsp, idx1, idx2);
        var h = YParam2Shunt(wsp, idx1, idx2);
        double diff = 0.0, scale = 0.0;
        foreach (var (a, b) in new (Complex, Complex)[]
        {
            (s.Inner.Y11, h.Inner.Y11), (s.Inner.Y12, h.Inner.Y12), (s.Inner.Y21, h.Inner.Y21), (s.Inner.Y22, h.Inner.Y22),
            (s.Feedback.Y11, h.Feedback.Y11), (s.Feedback.Y12, h.Feedback.Y12),
            (s.Feedback.Y21, h.Feedback.Y21), (s.Feedback.Y22, h.Feedback.Y22),
        })
        {
            diff  = Math.Max(diff, (a - b).Magnitude);
            scale = Math.Max(scale, a.Magnitude);
        }
        return scale == 0.0 ? (diff == 0.0 ? 0.0 : double.PositiveInfinity) : diff / scale;
    }

    private static (Complex X1, Complex X2) Solve2(
        Complex a11, Complex a12, Complex a21, Complex a22, Complex r1, Complex r2)
    {
        var det = a11 * a22 - a12 * a21;
        if (det == Complex.Zero)
        {
            var nan = new Complex(double.NaN, double.NaN);
            return (nan, nan);
        }
        return ((r1 * a22 - a12 * r2) / det, (a11 * r2 - a21 * r1) / det);
    }

    // ── §5.2–5.4: wsp_block_calc (Eq. 142–151) ──────────────────────────────

    /// <summary>The document's names for the sixteen entries of <see cref="BlockCalc"/>, in its
    /// own order (<c>FET2(1) … FET2(16)</c>, p. 87, 91, 100).</summary>
    public static readonly string[] BlockCalcLabels =
    [
        "s11", "s12", "s21", "s22",
        "sf11", "sf12", "sf21", "sf22",
        "F_LGa", "F_LGf", "F_LGH", "F_LGM",
        "LGa", "LGf", "LGH", "LGM",
    ];

    /// <summary>
    /// <c>wsp_block_calc(wsp, idx1, idx2 [, Z0])</c> — the document's list of sixteen, flattened
    /// exactly as it indexes them (Eq. 142–151):
    ///
    /// <code>
    ///  {1..4}  s11 s12 s21 s22      of the inner block at Z0        (Eq. 142)  S = (I − Z0·Y)(I + Z0·Y)⁻¹
    ///  {5..8}  sf11 sf12 sf21 sf22  of the feedback block at Z0     (Eq. 143)
    ///  {9}     F = 1 − LGa  = FB = |Y + Yf| / |Yo + Yf|,  Yo = Y with y21 → y12     (Eq. 144, 159)
    ///  {10}    F = 1 − LGf  = |Y + Yf| / |Y + Yfo|,       Yfo = Yf with yf12 → yf21  (Eq. 145)
    ///  {11}    F = 1 − LGH                                                          (Eq. 146)
    ///  {12}    F = 1 − LGM                                                          (Eq. 147)
    ///  {13}    LGa = [(yf21+y21)(yf12+y12) − (yf21+y12)(yf12+y12)]
    ///                / [(yf11+y11)(yf22+y22) − (yf21+y12)(yf12+y12)]                (Eq. 148 = Eq. 160, LGB)
    ///  {14}    LGf = [(yf21+y21)(yf12+y12) − (yf21+y21)(yf21+y12)]
    ///                / [(yf11+y11)(yf22+y22) − (yf21+y21)(yf21+y12)]                (Eq. 149 as printed — T-7)
    ///  {15}    LGH = (yf21+y21)(yf12+y12) / [(yf11+y11)(yf22+y22)]                  (Eq. 150 = Eq. 141, Hurst)
    ///  {16}    LGM = yf12·y21 / [(y11+yf11)(y22+yf22) − y12·y21 − yf12·yf21]        (Eq. 151 = Eq. 155 RHS — T-8)
    /// </code>
    ///
    /// <para><c>{9}</c> and <c>{10}</c> are computed as the determinant ratios of §5.4 (the
    /// synthetic-FET return difference with the block's controlled source zeroed, Fig. 50/51) and
    /// <c>{13}</c>/<c>{14}</c> literally as Eq. 148/149, so <c>1 − {9} == {13}</c> is an identity
    /// between two derivations, held by test, rather than a definition.</para>
    ///
    /// <para><b>Winslow's own warning (p. 89):</b> the Hurst form "is most meaningful when the probe
    /// pair is closest to and capture a single dependent source — such as with a transistor. I don't
    /// recommend using a complex network between the probe pair."</para>
    ///
    /// <para><b>T-7:</b> Eq. 149 is not a transcription error — the feedback network "as a synthetic
    /// FET" is passivated by replacing <c>yf12</c> with <c>yf21</c> (its reverse transfer term is
    /// treated as the controlled source), which is why its second product reads
    /// <c>(yf21+y21)(yf21+y12)</c>. <b>T-8:</b> Eq. 152–155's sign chain is inconsistent as printed;
    /// the right-hand side of Eq. 155 equals Eq. 151, which is what is implemented.</para>
    /// </summary>
    public static Complex[] BlockCalc(Complex[,] wsp, int idx1, int idx2, Complex z0)
    {
        var pair = YParam2(wsp, idx1, idx2);
        return BlockCalc(pair, z0);
    }

    /// <inheritdoc cref="BlockCalc(Complex[,], int, int, Complex)"/>
    public static Complex[] BlockCalc(in WspPairY pair, Complex z0)
    {
        var y = pair.Inner; var f = pair.Feedback;
        var s  = WspMatrix.ScatteringOfY(ToArray(y), z0);
        var sf = WspMatrix.ScatteringOfY(ToArray(f), z0);

        // Eq. 159: FB = |Y + Yf| / |Yo + Yf| with Yo = Y, y21 → y12 (the inner block's controlled
        // source y21 − y12 zeroed, Fig. 50/51).
        var detSum = Det2(y.Y11 + f.Y11, y.Y12 + f.Y12, y.Y21 + f.Y21, y.Y22 + f.Y22);
        var detYo  = Det2(y.Y11 + f.Y11, y.Y12 + f.Y12, y.Y12 + f.Y21, y.Y22 + f.Y22);
        var fb     = detSum / detYo;
        // The feedback network as the synthetic FET: Yfo = Yf with yf12 → yf21 (T-7).
        var detYfo = Det2(y.Y11 + f.Y11, y.Y12 + f.Y21, y.Y21 + f.Y21, y.Y22 + f.Y22);
        var fbf    = detSum / detYfo;

        var lga = ((f.Y21 + y.Y21) * (f.Y12 + y.Y12) - (f.Y21 + y.Y12) * (f.Y12 + y.Y12))
                / ((f.Y11 + y.Y11) * (f.Y22 + y.Y22) - (f.Y21 + y.Y12) * (f.Y12 + y.Y12));
        var lgf = ((f.Y21 + y.Y21) * (f.Y12 + y.Y12) - (f.Y21 + y.Y21) * (f.Y21 + y.Y12))
                / ((f.Y11 + y.Y11) * (f.Y22 + y.Y22) - (f.Y21 + y.Y21) * (f.Y21 + y.Y12));
        var lgh = (f.Y21 + y.Y21) * (f.Y12 + y.Y12) / ((f.Y11 + y.Y11) * (f.Y22 + y.Y22));
        var lgm = f.Y12 * y.Y21
                / ((y.Y11 + f.Y11) * (y.Y22 + f.Y22) - y.Y12 * y.Y21 - f.Y12 * f.Y21);

        return
        [
            s[0, 0],  s[0, 1],  s[1, 0],  s[1, 1],
            sf[0, 0], sf[0, 1], sf[1, 0], sf[1, 1],
            fb, fbf, Complex.One - lgh, Complex.One - lgm,
            lga, lgf, lgh, lgm,
        ];
    }

    private static Complex Det2(Complex a, Complex b, Complex c, Complex d) => a * d - b * c;

    private static Complex[,] ToArray(in WspTwoPortY y)
        => new[,] { { y.Y11, y.Y12 }, { y.Y21, y.Y22 } };

    // ── E.5, E.6: breakouts ──────────────────────────────────────────────────

    /// <summary><c>wsp_block_breakout(wsp, idx1, idx2)</c> — <c>{1..4}</c> of <see cref="BlockCalc"/>
    /// as a 2×2: the inner block's S-parameters at <paramref name="z0"/> (E.5).</summary>
    public static Complex[,] BlockBreakout(Complex[,] wsp, int idx1, int idx2, Complex z0)
        => WspMatrix.ScatteringOfY(ToArray(YParam2(wsp, idx1, idx2).Inner), z0);

    /// <summary><c>wsp_fb_breakout(wsp, idx1, idx2)</c> — <c>{5..8}</c>: the feedback block's
    /// S-parameters at <paramref name="z0"/> (E.6).</summary>
    public static Complex[,] FbBreakout(Complex[,] wsp, int idx1, int idx2, Complex z0)
        => WspMatrix.ScatteringOfY(ToArray(YParam2(wsp, idx1, idx2).Feedback), z0);

    // ── E.8, E.9: in-situ renormalised designs ───────────────────────────────

    /// <summary>
    /// <c>wsp_block_design(wsp, idx1, idx2)</c> — the inner block "as if it were broken out and
    /// isolated and terminated with your target loadlines" (p. 88):
    /// <c>wsp_rc_renorm_s(z_to_pr(ZG₁), z_to_pc(ZG₁), block S, z_to_pr(ZL₂), z_to_pc(ZL₂))</c> (E.8),
    /// where <c>ZG₁</c> is probe 1's <c>ZG</c> (looking away from the inner block into the source
    /// side) and <c>ZL₂</c> is probe 2's <c>ZL</c>. Any RC pair may be replaced by the user's own —
    /// <see cref="WspRenorm.RcRenormS"/> is public — which is the design use the document describes.
    ///
    /// <para>A side whose real part is not positive (an active <c>ZG</c>) is refused by
    /// <see cref="WspRenorm"/> (<c>wsprobe.renorm-negative-reference</c>) rather than absorbed.</para>
    /// </summary>
    public static Complex[,] BlockDesign(Complex[,] wsp, int idx1, int idx2, double freqHz, Complex z0)
    {
        var zg1 = WspReduction.ZG(WspProbeQuad.Of(wsp, idx1));
        var zl2 = WspReduction.ZL(WspProbeQuad.Of(wsp, idx2));
        return RcDesign(BlockBreakout(wsp, idx1, idx2, z0), zg1, zl2, freqHz, z0);
    }

    /// <summary><c>wsp_fb_design(wsp, idx1, idx2)</c> — the feedback block renormalised to probe 1's
    /// <c>ZL</c> and probe 2's <c>ZG</c> as parallel RC pairs (E.9; the roles flip because the
    /// feedback block's ports face the other way).</summary>
    public static Complex[,] FbDesign(Complex[,] wsp, int idx1, int idx2, double freqHz, Complex z0)
    {
        var zl1 = WspReduction.ZL(WspProbeQuad.Of(wsp, idx1));
        var zg2 = WspReduction.ZG(WspProbeQuad.Of(wsp, idx2));
        return RcDesign(FbBreakout(wsp, idx1, idx2, z0), zl1, zg2, freqHz, z0);
    }

    private static Complex[,] RcDesign(Complex[,] s, Complex zPort1, Complex zPort2, double freqHz, Complex z0)
    {
        var m = new Mat<Complex>(2, 2);
        m[0, 0] = s[0, 0]; m[0, 1] = s[0, 1];
        m[1, 0] = s[1, 0]; m[1, 1] = s[1, 1];
        var r = WspRenorm.RcRenormS(
            ImmittanceModels.ZToPr(zPort1), ImmittanceModels.ZToPc(zPort1, freqHz), m,
            ImmittanceModels.ZToPr(zPort2), ImmittanceModels.ZToPc(zPort2, freqHz), freqHz, z0);
        return new[,] { { r[0, 0], r[0, 1] }, { r[1, 0], r[1, 1] } };
    }
}
