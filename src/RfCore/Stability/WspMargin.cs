using System;
using System.Numerics;

namespace RfCore.Stability;

/// <summary>
/// The four bounded proxies and the two stability margins of one probe at one frequency
/// (<c>M-rY</c>, <c>M-iY</c>, <c>M-rH</c>, <c>M-iH</c>, <c>M-Eq. 9</c>, <c>M-Eq. 10</c>).
/// <see cref="Sm"/> is the pair's minimum — the single number
/// <c>wsp_stability_margin</c> answers with.
/// </summary>
/// <param name="rY">The real-part proxy of the <b>impedance</b> pair (M-rY), from <c>ZG</c>, <c>ZL</c>.</param>
/// <param name="iY">The imaginary-part proxy of the impedance pair (M-iY).</param>
/// <param name="SmY0">
/// <c>SM_Y0 = ½(rY + iY)</c> (M-Eq. 9) — the margin on the <b>series</b> driving-point function
/// <c>1/Y0 = ZG + ZL</c>.
/// </param>
/// <param name="rH">The real-part proxy of the <b>admittance</b> pair (M-rH), from <c>YG</c>, <c>YL</c>.</param>
/// <param name="iH">The imaginary-part proxy of the admittance pair (M-iH).</param>
/// <param name="SmH0">
/// <c>SM_H0 = ½(rH + iH)</c> (M-Eq. 10) — the margin on the <b>shunt</b> driving-point function
/// <c>1/H0 = YG + YL</c>.
/// </param>
public readonly record struct WspMargins(
    double rY, double iY, double SmY0,
    double rH, double iH, double SmH0)
{
    /// <summary>
    /// <c>min(SM_Y0, SM_H0)</c> — the margin of the node, both stimuli together.
    ///
    /// <para>Both are required ([M] §IV): a series-resonant instability is seen by the series
    /// stimulus (<c>Y0</c>, hence <c>SM_Y0</c>) and a parallel-resonant one by the shunt stimulus
    /// (<c>H0</c>, hence <c>SM_H0</c>), and "in rare exceptional circuits" one of them fails to
    /// detect. That is §4.10's pole masking in margin form, and it is visible on the simplest
    /// circuit there is: on the split resonator of brief-wsprobe-9 §3 the two margins collapse at
    /// <b>different frequencies</b>.</para>
    ///
    /// <para>NaN if either is NaN (<see cref="Math.Min(double, double)"/>), which is what a
    /// degenerate node must give — never a 0 that reads as an instability.</para>
    /// </summary>
    public double Sm => Math.Min(SmY0, SmH0);
}

/// <summary>
/// The published stability margin — T. A. Winslow, "A Novel Stability Margin for Transfer
/// Functions", <i>Proc. 19th EuMIC</i>, Paris, Sept. 2024, pp. 291–294
/// (DOI 10.23919/EuMIC61603.2024.10732614), cited here as <c>[M]</c>.
///
/// <para><b>What it is for.</b> <c>H0</c> and <c>Y0</c> are the rigorous nodal stability functions
/// (their denominators carry the network determinant), but they have <b>units</b>, and their
/// absolute trajectory in the complex plane is set by the node's impedance level: two FETs of very
/// different periphery tuned to the same Rollett <c>K</c> have driving-point loci that differ by
/// orders of magnitude, so <c>1/H0</c> on a polar chart gives a binary answer — a Kurokawa crossing
/// or not — and no sense of <i>how close</i>. The way out is that both are sums of bidirectional
/// immittances, <c>1/H0 = YG + YL</c> (M-Eq. 1) and <c>1/Y0 = ZG + ZL</c> (M-Eq. 2), and Kurokawa's
/// condition on each (M-Eq. 7, 8) is a statement about the <b>relative</b> size of the two halves:
/// the real parts cancelling, the imaginary parts cancelling. Normalising each half against the
/// other gives four bounded, unitless proxies, and their mean is a margin that means the same thing
/// at every node of every circuit.</para>
///
/// <para><b>The definitions</b> ([M] §II), with <c>ZG</c>, <c>ZL</c> the bidirectional impedances
/// (Eq. 65–68 of the 2023 document; M-Eq. 3, 4) and <c>YG</c>, <c>YL</c> the bidirectional
/// admittances (Eq. 72–78; M-Eq. 5, 6 — <b>the Y-forms</b>, see T-17):</para>
/// <code>
///   rY = 0                       if  Re ZG + Re ZL ≤ 0            (M-rY, third case — tested FIRST)
///      = ½ (1 + Re ZL / Re ZG)   if |Re ZG| ≥ |Re ZL|
///      = ½ (1 + Re ZG / Re ZL)   otherwise
///
///   iY = ½ (1 + Im ZL / Im ZG)   if |Im ZG| ≥ |Im ZL|             (M-iY)
///      = ½ (1 + Im ZG / Im ZL)   otherwise
///
///   rH, iH:  the same two functions over Re YG, Re YL and Im YG, Im YL      (M-rH, M-iH)
///
///   SM_Y0 = ½ (rY + iY)                                            (M-Eq. 9)
///   SM_H0 = ½ (rH + iH)                                            (M-Eq. 10)
/// </code>
///
/// <para><b>Conventions [M] leaves open</b> (typo register T-18; brief-wsprobe-9 §2.1), each held by
/// a gate:</para>
/// <list type="bullet">
///   <item><b>(a) The <c>≤ 0</c> case takes precedence.</b> [M] lists it third. With
///   <c>Re ZG = 5</c>, <c>Re ZL = −10</c> the magnitude branch alone gives <c>0.25</c>; the sum is
///   <c>−5</c>, Kurokawa's real-part condition (M-Eq. 8) holds, and the margin <b>must</b> be 0
///   there. <see cref="ProxyReal"/> tests the sum first.</item>
///   <item><b>(b) Equal magnitudes.</b> Both magnitude branches give the same value (the ratio is
///   <c>±1</c>), so <c>≥</c> on the first branch changes nothing.</item>
///   <item><b>(c) Both parts zero</b> — the larger-magnitude term is exactly 0 — is <b>0.5</b>. The
///   paper does not say; the function is genuinely discontinuous at the origin (the limit is 1
///   along <c>Im ZL = Im ZG → 0</c>, 0 along <c>Im ZL = −Im ZG → 0</c>, 0.5 along either axis), so
///   any value is a convention. 0.5 is what one purely resistive side gives
///   (<c>Im ZG = 0</c>, <c>Im ZL ≠ 0</c> ⇒ <c>½(1 + 0)</c>) and is continuous with it.</item>
///   <item><b>(d) NaN propagates.</b> A degenerate node (<c>wsprobe.degenerate-node</c>) has NaN
///   immittances and gets a NaN margin, never a 0 that reads as an instability.</item>
///   <item><b>(e) dB is <c>20·log10</c></b> (overview D-16). The margin is a unitless ratio bounded
///   by 1 and circuitRF applies <c>20·log10</c> to every unitless magnitude, so the ordinary
///   <c>dB(...)</c> is the one to use; no <c>_dB</c> variant is added. [M] §IV's rule of thumb —
///   investigate "any sudden decrease (below −15 dB)" — is <c>0.178</c> under this convention.</item>
///   <item><b>(f) Kurokawa's third condition is not in the margin.</b> <c>∂Im/∂ω &gt; 0</c>
///   (M-Eq. 7, 8) is what separates a start-up from a benign crossing; the margin measures distance
///   to the first two only. <see cref="WspKurokawa"/> stays the <i>detector</i>, this is the
///   <i>distance</i>, and every place one is reported the other is beside it.</item>
/// </list>
///
/// <para><b>Properties the definitions guarantee</b> (brief-wsprobe-9 §2.2, all gated):</para>
/// <list type="bullet">
///   <item><c>0 ≤ rY, iY, rH, iH ≤ 1</c>, hence <c>0 ≤ SM ≤ 1</c>.</item>
///   <item><b>Unitless in the sense that matters:</b> scaling <c>ZG</c> and <c>ZL</c> by the same
///   positive real leaves <c>SM_Y0</c> unchanged. That is the cross-node comparability [M] §I
///   wants.</item>
///   <item><c>SM_Y0 = 1</c> iff <c>ZL = ZG</c> with <c>Re &gt; 0</c>. <b>A conjugate match is not
///   the top of the scale:</b> <c>ZL = conj(ZG)</c> gives <c>rY = 1</c>, <c>iY = 0</c>,
///   <c>SM_Y0 = 0.5 = −6.02 dB</c>. A designer who expects a matched node to read 0 dB will read
///   −6 dB as a problem, so the docs say so.</item>
///   <item><b>The −12 dB floor.</b> If <c>Re ZG &gt; 0</c> and <c>Re ZL &gt; 0</c> then the ratio is
///   in <c>(0, 1]</c>, <c>rY ∈ (0.5, 1]</c>, and <c>SM_Y0 ≥ 0.25</c> (−12.04 dB). Contrapositive,
///   and the interpretive rule the docs carry: <b>a margin below −12 dB certifies that one side of
///   the node presents negative resistance at that frequency.</b></item>
///   <item><c>SM_Y0 = 0</c> iff <c>Re(ZG + ZL) ≤ 0</c> and <c>Im ZL = −Im ZG</c> — the two static
///   Kurokawa conditions on <c>1/Y0</c> — <b>with one exception, and it is convention (c), not an
///   accident:</b> when <c>Im ZG</c> and <c>Im ZL</c> are BOTH exactly zero the reactances cancel
///   trivially, so Kurokawa's second condition holds, yet <c>iY</c> is the conventional 0.5 and
///   <c>SM_Y0</c> is 0.25 rather than 0. That is the case a probe facing a purely resistive
///   termination sits in at every frequency, and it is why the resonator fixture of
///   brief-wsprobe-9 §3 splits its reactance ACROSS the probe: with <c>Im ZL ≡ 0</c> the imaginary
///   proxy is 0.5 everywhere and the margin on that stimulus is flat at −12.04 dB. The margin
///   still reports the node correctly — it just reports it on the OTHER stimulus.</item>
///   <item><c>SM_Y0</c> and <c>SM_H0</c> are <b>not</b> functions of each other, even with zero
///   feedback (<c>YG = 1/ZG</c>, <c>YL = 1/ZL</c> there, and the ratio of reciprocals is not the
///   reciprocal of the ratio).</item>
/// </list>
///
/// <para><b>No epsilon is ever added to a denominator</b> (overview D-7): <see cref="Proxy"/> tests
/// the larger magnitude for exact zero and returns 0.5.</para>
/// </summary>
public static class WspMargin
{
    /// <summary>
    /// One bounded proxy over one pair of parts — the magnitude-branch form of (M-iY)/(M-iH), and
    /// the second and third branches of (M-rY)/(M-rH):
    /// <c>½(1 + l/g)</c> when <c>|g| ≥ |l|</c>, <c>½(1 + g/l)</c> otherwise.
    ///
    /// <para>Always in <c>[0, 1]</c>: the ratio taken is the smaller part over the larger, so it is
    /// in <c>[−1, 1]</c>. Both parts exactly zero returns <b>0.5</b> (§2.1c). NaN in, NaN out
    /// (§2.1d).</para>
    /// </summary>
    /// <param name="g">The generator-side part — <c>Re ZG</c>, <c>Im ZG</c>, <c>Re YG</c> or <c>Im YG</c>.</param>
    /// <param name="l">The load-side part, of the same kind.</param>
    public static double Proxy(double g, double l)
    {
        if (double.IsNaN(g) || double.IsNaN(l)) return double.NaN;
        if (Math.Abs(g) >= Math.Abs(l))
            return g == 0.0 ? 0.5 : 0.5 * (1.0 + l / g);   // |g| ≥ |l| and g = 0 ⇒ l = 0 too (§2.1c)
        return 0.5 * (1.0 + g / l);                        // |g| < |l| ⇒ l ≠ 0
    }

    /// <summary>
    /// The real-part proxy (M-rY)/(M-rH): <b>0 when the sum is non-positive</b>, otherwise
    /// <see cref="Proxy"/>.
    ///
    /// <para>The sum test comes FIRST although [M] prints it third (§2.1a, T-18). With
    /// <c>g = 5</c>, <c>l = −10</c> the magnitude branch alone gives 0.25, while the sum is −5 and
    /// Kurokawa's real-part condition holds — the margin must be 0 there.</para>
    /// </summary>
    public static double ProxyReal(double g, double l)
    {
        if (double.IsNaN(g) || double.IsNaN(l)) return double.NaN;
        if (g + l <= 0.0) return 0.0;
        return Proxy(g, l);
    }

    /// <summary>
    /// <c>rY</c>, <c>iY</c> and <c>SM_Y0 = ½(rY + iY)</c> (M-Eq. 9) from the bidirectional
    /// <b>impedance</b> pair — the margin on <c>1/Y0 = ZG + ZL</c> (M-Eq. 2), which is the stimulus
    /// that sees a <i>series</i>-resonant instability.
    /// </summary>
    public static (double rY, double iY, double sm) FromZ(Complex zg, Complex zl)
    {
        double r = ProxyReal(zg.Real,      zl.Real);
        double i = Proxy    (zg.Imaginary, zl.Imaginary);
        return (r, i, 0.5 * (r + i));
    }

    /// <summary>
    /// <c>rH</c>, <c>iH</c> and <c>SM_H0 = ½(rH + iH)</c> (M-Eq. 10) from the bidirectional
    /// <b>admittance</b> pair — the margin on <c>1/H0 = YG + YL</c> (M-Eq. 1), which is the stimulus
    /// that sees a <i>parallel</i>-resonant instability.
    ///
    /// <para>[M] Eq. 5/6 also print Z-forms of <c>YG</c>/<c>YL</c> and those are wrong as printed
    /// (T-17: the off-diagonals are swapped, invisible on a reciprocal network and O(1) on a
    /// non-reciprocal one). circuitRF computes <c>YG = y11 + y12</c>, <c>YL = y22 + y21</c>
    /// (<see cref="WspReduction.YG"/>), which are right.</para>
    /// </summary>
    public static (double rH, double iH, double sm) FromY(Complex yg, Complex yl)
    {
        double r = ProxyReal(yg.Real,      yl.Real);
        double i = Proxy    (yg.Imaginary, yl.Imaginary);
        return (r, i, 0.5 * (r + i));
    }

    /// <summary>
    /// Both margins of one probe at one frequency, from its own block of the <c>wsp</c> matrix —
    /// the <b>one implementation</b> the engine's <c>SM_Y0:&lt;label&gt;</c>/<c>SM_H0:&lt;label&gt;</c>
    /// cubes and the <c>wsp_SM_Y0</c>/<c>wsp_SM_H0</c> built-ins both call (overview D-2).
    ///
    /// <para>The immittances come from <see cref="WspReduction"/>, which is where the run's own
    /// <c>ZG:</c>/<c>ZL:</c> cubes come from, so a run's margin and a trace card's margin of the
    /// same probe are bit-identical by construction. A degenerate probe (<c>Y0 = 0</c> or
    /// <c>H0 = 0</c>) has NaN immittances on the affected side and therefore a NaN margin.</para>
    /// </summary>
    public static WspMargins Of(in WspProbeQuad q)
    {
        var (rY, iY, smY) = FromZ(WspReduction.ZG(q), WspReduction.ZL(q));
        var y = WspReduction.YParam(q);
        var (rH, iH, smH) = FromY(WspReduction.YG(y), WspReduction.YL(y));
        return new WspMargins(rY, iY, smY, rH, iH, smH);
    }
}
