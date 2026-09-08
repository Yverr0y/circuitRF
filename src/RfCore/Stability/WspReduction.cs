using System.Numerics;

namespace RfCore.Stability;

/// <summary>
/// The four transfer functions one WSProbe measures, normalised — the probe's own 2×2 block of the
/// <c>wsp</c> matrix, in the letters of the reference document's Appendix E.13.
///
/// <para>T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023), Eq. 33 with
/// <c>i = j</c>. For probe <c>idx</c> (1-based), with <c>r = 2·idx − 1</c> and <c>s = 2·idx</c>:</para>
/// <code>
///   A = vP/vS = wsp(r, s)          shunt voltage response to the series voltage      (Eq. 135)
///   B = iS/vS = wsp(r, r) = Y0     series current response to the series voltage     (Eq. 52, 133)
///   C = vP/iP = wsp(s, s) = H0     shunt voltage response to the shunt current        (Eq. 50, 131)
///   D = iS/iP = wsp(s, r)          series current response to the shunt current      (Eq. 136)
/// </code>
/// </summary>
public readonly record struct WspProbeQuad(Complex A, Complex B, Complex C, Complex D)
{
    /// <summary>The probe's own block read out of a <c>wsp</c> matrix. <paramref name="wsp"/> is
    /// 0-based storage of the document's 1-based matrix, so <c>wsp(r, c)</c> is
    /// <c>wsp[r − 1, c − 1]</c>; <paramref name="idx"/> is the document's 1-based probe index.</summary>
    public static WspProbeQuad Of(Complex[,] wsp, int idx)
    {
        int r = 2 * idx - 2, s = 2 * idx - 1;   // 0-based rows/cols of the document's 2i−1 and 2i
        return new WspProbeQuad(A: wsp[r, s], B: wsp[r, r], C: wsp[s, s], D: wsp[s, r]);
    }
}

/// <summary>The reduced two-port at the probe, admittance form (Eq. 44). Port 1 is the G-side
/// terminal, port 2 the L-side terminal (overview §3).</summary>
public readonly record struct WspTwoPortY(Complex Y11, Complex Y12, Complex Y21, Complex Y22);

/// <summary>The reduced two-port at the probe, impedance form (Eq. 48). Same port assignment.</summary>
public readonly record struct WspTwoPortZ(Complex Z11, Complex Z12, Complex Z21, Complex Z22);

/// <summary>
/// The six default outputs of one WSProbe (the document's Fig. 13), plus whether the reduction was
/// undefined at this point. <see cref="Degenerate"/> is true when <c>H0 = 0</c> (an exact short from
/// the G node to ground makes <c>[Y]</c> undefined, so <c>LG</c> and <c>F</c> are NaN) or
/// <c>Y0 = 0</c> (an exact open in the probe branch makes <c>[Z]</c> undefined, so <c>ZG</c> and
/// <c>ZL</c> are NaN). No epsilon is ever added to a denominator (overview D-7).
/// </summary>
public readonly record struct WspDefaults(
    Complex H0, Complex Y0, Complex ZG, Complex ZL, Complex LG, Complex F, bool Degenerate);

/// <summary>
/// The single-probe reduction of the <c>wsp</c> matrix — pure functions over <see cref="Complex"/>
/// values, no cubes, no engine types. This is the library the engine computes a run's default
/// per-probe cubes with AND the library every derived metric is built on (overview D-2, D-6), so a
/// run's <c>ZG:GATE</c> and a trace card's <c>wsp_ZG</c> of the same probe are one implementation.
///
/// <para>Every function cites T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i>
/// (2023) by equation. Two of the document's printed equations are wrong and are NOT implemented
/// (overview §5): Eq. 40's <c>y12</c>/<c>y21</c> carry the <c>|P|</c> term with the wrong sign
/// (T-1 — use Eq. 44), and Eq. 47's <c>z22</c> has a wrong first factor (T-2 — use Eq. 48).</para>
/// </summary>
public static class WspReduction
{
    private static readonly Complex NaN = new(double.NaN, double.NaN);

    /// <summary>The determinant of the probe's transfer matrix, <c>|P| = B·C − A·D</c> (Eq. 42).</summary>
    public static Complex DetP(in WspProbeQuad q) => q.B * q.C - q.A * q.D;

    /// <summary>The four cofactor combinations of Eq. 43:
    /// <c>α = |P|</c>, <c>β = |P| + A</c>, <c>γ = |P| − D</c>, <c>δ = |P| + A − D + 1</c>.</summary>
    public static (Complex Alpha, Complex Beta, Complex Gamma, Complex Delta) Greek(in WspProbeQuad q)
    {
        var p = DetP(q);
        return (p, p + q.A, p - q.D, p + q.A - q.D + Complex.One);
    }

    /// <summary>
    /// The reduced admittance two-port, <c>[Y] = (1/H0)·[[δ, −β], [−γ, α]]</c> (Eq. 44; the
    /// document's <c>wsp_yparam</c>). NaN throughout when <c>H0 = C = 0</c>.
    /// </summary>
    public static WspTwoPortY YParam(in WspProbeQuad q)
    {
        if (q.C == Complex.Zero) return new WspTwoPortY(NaN, NaN, NaN, NaN);
        var (a, b, g, d) = Greek(q);
        return new WspTwoPortY(Y11: d / q.C, Y12: -b / q.C, Y21: -g / q.C, Y22: a / q.C);
    }

    /// <summary>
    /// The reduced impedance two-port, <c>[Z] = (1/Y0)·[[α, β], [γ, δ]]</c> (Eq. 48; the document's
    /// <c>wsp_zparam</c>). NaN throughout when <c>Y0 = B = 0</c>.
    /// </summary>
    public static WspTwoPortZ ZParam(in WspProbeQuad q)
    {
        if (q.B == Complex.Zero) return new WspTwoPortZ(NaN, NaN, NaN, NaN);
        var (a, b, g, d) = Greek(q);
        return new WspTwoPortZ(Z11: a / q.B, Z12: b / q.B, Z21: g / q.B, Z22: d / q.B);
    }

    /// <summary>The driving-point impedance <c>H0 = vP/iP = wsp(2i, 2i)</c> (Eq. 29, 50). The
    /// document regrets the letter H for an impedance (§4.1, p. 34) and keeps it; so does this.</summary>
    public static Complex H0(in WspProbeQuad q) => q.C;

    /// <summary>The driving-point admittance <c>Y0 = iS/vS = wsp(2i−1, 2i−1)</c> (Eq. 52).</summary>
    public static Complex Y0(in WspProbeQuad q) => q.B;

    /// <summary>The bilateral (Tian) loop gain <c>LG = −(y12 + y21)/(y11 + y22)</c> (Eq. 58/92).
    /// Diagnostic, not a rigorous stability measure — the document says so of every loop gain
    /// (§4.4, p. 50).</summary>
    public static Complex LoopGain(in WspTwoPortY y) => -(y.Y12 + y.Y21) / (y.Y11 + y.Y22);

    /// <summary>The same loop gain from the impedance form, <c>LG = (z12 + z21)/(z11 + z22)</c>
    /// (Eq. 60). Algebraically identical to <see cref="LoopGain(in WspTwoPortY)"/>; kept as the
    /// test oracle for that identity.</summary>
    public static Complex LoopGain(in WspTwoPortZ z) => (z.Z12 + z.Z21) / (z.Z11 + z.Z22);

    /// <summary>The return difference <c>F = 1 − LG</c> (Eq. 57; p. 33).</summary>
    public static Complex ReturnDifference(Complex loopGain) => Complex.One - loopGain;

    /// <summary>
    /// The bidirectional generator-side impedance, looking from the probe into port 1 (the G side):
    /// <c>ZG = z11 − z12 = −(1/Y0)·(vP/vS)</c> (Eq. 67). Implemented in the Z form, which has one
    /// fewer cancellation than the <c>|Y|</c> form of Eq. 65 and is exact algebraically. NaN when
    /// <c>Y0 = 0</c>.
    /// </summary>
    public static Complex ZG(in WspProbeQuad q) => q.B == Complex.Zero ? NaN : -q.A / q.B;

    /// <summary>
    /// The bidirectional load-side impedance, looking from the probe into port 2 (the L side):
    /// <c>ZL = z22 − z21 = (1/Y0)·(1 + vP/vS)</c> (Eq. 68). NaN when <c>Y0 = 0</c>.
    /// </summary>
    public static Complex ZL(in WspProbeQuad q) => q.B == Complex.Zero ? NaN : (Complex.One + q.A) / q.B;

    /// <summary><c>ZG</c> through the admittance form, <c>(y12 + y22)/(y11·y22 − y21·y12)</c>
    /// (Eq. 65). Test oracle for <see cref="ZG"/> only — never the shipped path.</summary>
    public static Complex ZGFromY(in WspTwoPortY y) => (y.Y12 + y.Y22) / (y.Y11 * y.Y22 - y.Y21 * y.Y12);

    /// <summary><c>ZL</c> through the admittance form, <c>(y21 + y11)/(y11·y22 − y21·y12)</c>
    /// (Eq. 66). Test oracle for <see cref="ZL"/> only.</summary>
    public static Complex ZLFromY(in WspTwoPortY y) => (y.Y21 + y.Y11) / (y.Y11 * y.Y22 - y.Y21 * y.Y12);

    /// <summary>The bidirectional generator-side admittance <c>YG = y11 + y12</c> (Eq. 76). Not
    /// <c>1/ZG</c> in general (Eq. 79) — equal only when <c>y12 = y21 = 0</c>.</summary>
    public static Complex YG(in WspTwoPortY y) => y.Y11 + y.Y12;

    /// <summary>The bidirectional load-side admittance <c>YL = y22 + y21</c> (Eq. 77).</summary>
    public static Complex YL(in WspTwoPortY y) => y.Y22 + y.Y21;

    /// <summary>
    /// The six default outputs of one probe at one frequency (the document's Fig. 13), from its
    /// quad, with the guard of R-wsp1-9: <c>C = 0</c> makes <c>[Y]</c> — and so <c>LG</c>, <c>F</c> —
    /// undefined; <c>B = 0</c> makes <c>[Z]</c> — and so <c>ZG</c>, <c>ZL</c> — undefined. The
    /// affected outputs are NaN and <see cref="WspDefaults.Degenerate"/> is set so the caller can
    /// warn once per probe.
    /// </summary>
    public static WspDefaults Defaults(in WspProbeQuad q)
    {
        bool cZero = q.C == Complex.Zero;
        bool bZero = q.B == Complex.Zero;

        Complex lg, f;
        if (cZero) { lg = NaN; f = NaN; }
        else
        {
            lg = LoopGain(YParam(q));
            f  = ReturnDifference(lg);
        }

        return new WspDefaults(
            H0: q.C, Y0: q.B,
            ZG: ZG(q), ZL: ZL(q),
            LG: lg, F: f,
            Degenerate: cZero || bZero);
    }
}
