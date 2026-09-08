// ================================================================
//  WspMarginTests.cs — brief-wsprobe-9's scalar-core gates (a) and (b).
//
//  (a) is a TRANSCRIPTION gate: the four displays of [M] §II are written out a second time here,
//  independently, in the paper's own three-branch order with the sum test moved first (§2.1a), and
//  compared against WspMargin on 10^5 random pairs plus every boundary the definition has.
//
//  (b) is a PROPERTY gate: positive-real scaling invariance, the two exact values (SM = 1 only at
//  ZL = ZG with Re > 0; a conjugate match reads exactly 0.5), and the −12 dB floor — which is the
//  one interpretive rule the user docs carry, because its contrapositive says that a margin below
//  −12 dB CERTIFIES negative resistance on one side of the node.
//
//  Reference: T. A. Winslow, "A Novel Stability Margin for Transfer Functions", Proc. 19th EuMIC,
//  Paris, 2024, pp. 291–294 — cited as [M], its equations as M-Eq. n and its four unnumbered
//  displays as M-rY, M-iY, M-rH, M-iH.
// ================================================================

using System;
using System.Linq;
using System.Numerics;
using RfCore.Stability;
using Xunit;
using Xunit.Abstractions;

namespace RfCore.Tests.Stability;

public class WspMarginTests(ITestOutputHelper output)
{
    // ══ The independent second transcription ═════════════════════════════════
    //
    //  Written from [M] §II as printed, branch for branch, with nothing shared with the shipped
    //  implementation — no call into WspMargin, no helper of its own. The ONLY deliberate change is
    //  the order of the rY branches, which is §2.1a's decision (T-18) and is what the gate below
    //  demonstrates is not cosmetic.

    /// <summary>(M-rY)/(M-rH) as [M] prints them, with the third case tested first (§2.1a).</summary>
    private static double PaperReal(double g, double l)
    {
        if (g + l <= 0.0) return 0.0;                              // [M]'s third case, moved first
        if (Math.Abs(g) >= Math.Abs(l)) return 0.5 * (1.0 + l / g);
        return 0.5 * (1.0 + g / l);
    }

    /// <summary>(M-iY)/(M-iH) as [M] prints them — two branches, no sum test.</summary>
    private static double PaperImag(double g, double l)
    {
        if (Math.Abs(g) >= Math.Abs(l)) return 0.5 * (1.0 + l / g);
        return 0.5 * (1.0 + g / l);
    }

    private static double PaperSm(Complex g, Complex l)
        => 0.5 * (PaperReal(g.Real, l.Real) + PaperImag(g.Imaginary, l.Imaginary));

    // ══ (a) — the transcription ══════════════════════════════════════════════

    /// <summary>
    /// Gate (a). 10^5 random pairs against the transcription above, plus every boundary: equal
    /// magnitudes of both signs, one part zero, both parts zero, the §2.1a precedence example, and
    /// NaN. Every value in <c>[0, 1]</c>.
    ///
    /// <para>The random pairs are drawn over a wide dynamic range and are allowed to be negative on
    /// either side, because the interesting branches of this function are exactly the ones a passive
    /// circuit never reaches.</para>
    /// </summary>
    [Fact]
    public void A_ProxiesMatchAnIndependentTranscriptionOfThePaper_AndAreBoundedByZeroAndOne()
    {
        var rnd = new Random(90901);
        double worst = 0.0;

        for (int k = 0; k < 100_000; k++)
        {
            // Sign and decade both random: ±1e-3 … ±1e3, so both magnitude branches and both signs
            // of the sum are exercised heavily.
            double G() => (rnd.Next(2) == 0 ? -1.0 : 1.0) * Math.Pow(10.0, rnd.NextDouble() * 6 - 3);
            var zg = new Complex(G(), G());
            var zl = new Complex(G(), G());

            var (rY, iY, sm) = WspMargin.FromZ(zg, zl);
            double pr = PaperReal(zg.Real, zl.Real);
            double pi = PaperImag(zg.Imaginary, zl.Imaginary);

            Assert.Equal(pr, rY, 15);
            Assert.Equal(pi, iY, 15);
            worst = Math.Max(worst, Math.Abs(sm - PaperSm(zg, zl)));

            foreach (var (v, what) in new[] { (rY, "rY"), (iY, "iY"), (sm, "SM_Y0") })
                Assert.True(v >= 0.0 && v <= 1.0, $"{what} = {v:G17} is outside [0, 1] for ZG = {zg}, ZL = {zl}");

            // FromY is the same two functions over the admittance pair (M-rH, M-iH).
            var (rH, iH, smh) = WspMargin.FromY(zg, zl);
            Assert.Equal(rY, rH, 15);
            Assert.Equal(iY, iH, 15);
            Assert.Equal(sm, smh, 15);
        }
        output.WriteLine($"(a) worst |SM − transcription| over 100,000 random pairs: {worst:G3}");
        Assert.True(worst <= 1e-15, $"worst SM disagreement {worst:G3}");

        // ── Boundaries ───────────────────────────────────────────────────────

        // §2.1b: equal magnitudes agree on both branches, so `≥` is not a choice.
        Assert.Equal(1.0,  WspMargin.Proxy( 3.0,  3.0), 15);
        Assert.Equal(0.0,  WspMargin.Proxy( 3.0, -3.0), 15);
        Assert.Equal(0.0,  WspMargin.Proxy(-3.0,  3.0), 15);
        Assert.Equal(1.0,  WspMargin.Proxy(-3.0, -3.0), 15);

        // §2.1c: one part zero is 0.5 — the value one purely resistive side gives — and both parts
        // exactly zero takes that same value by convention, since the limit does not exist there.
        Assert.Equal(0.5, WspMargin.Proxy(0.0,  7.0), 15);
        Assert.Equal(0.5, WspMargin.Proxy(7.0,  0.0), 15);
        Assert.Equal(0.5, WspMargin.Proxy(0.0,  0.0), 15);

        // §2.1a, the precedence example, and the reason the sum test cannot be left third: the
        // magnitude branch ALONE would answer 0.25 where Kurokawa's real-part condition holds.
        Assert.Equal(0.0,  WspMargin.ProxyReal( 5.0, -10.0), 15);
        Assert.Equal(0.25, WspMargin.Proxy    ( 5.0, -10.0), 15);   // what the magnitude branch alone gives
        Assert.Equal(0.25, WspMargin.ProxyReal(10.0,  -5.0), 15);   // sum > 0, so the magnitude branch stands
        Assert.Equal(0.0,  WspMargin.ProxyReal(-1.0,   1.0), 15);   // sum exactly 0 is the ≤ case

        // §2.1d: NaN in, NaN out — never a 0 that reads as an instability.
        Assert.True(double.IsNaN(WspMargin.Proxy(double.NaN, 1.0)));
        Assert.True(double.IsNaN(WspMargin.Proxy(1.0, double.NaN)));
        Assert.True(double.IsNaN(WspMargin.ProxyReal(double.NaN, 1.0)));
        Assert.True(double.IsNaN(WspMargin.ProxyReal(1.0, double.NaN)));
        var nan = new Complex(double.NaN, double.NaN);
        Assert.True(double.IsNaN(WspMargin.FromZ(nan, Complex.One).sm));
        Assert.True(double.IsNaN(WspMargin.FromY(Complex.One, nan).sm));
        Assert.True(double.IsNaN(new WspMargins(0.5, 0.5, 0.5, double.NaN, 0.5, double.NaN).Sm));
    }

    // ══ (b) — the properties of §2.2 ═════════════════════════════════════════

    /// <summary>
    /// Gate (b). The four properties the definition guarantees, each of which the user docs state
    /// and a designer will rely on.
    /// </summary>
    [Fact]
    public void B_ScalingInvariance_TheTwoExactValues_AndTheMinusTwelveDbFloor()
    {
        var rnd = new Random(90902);

        // ── Positive-real scaling invariance: the cross-node comparability [M] §I wants ──
        double worstScale = 0.0;
        for (int k = 0; k < 10_000; k++)
        {
            var zg = new Complex(rnd.NextDouble() * 200 - 100, rnd.NextDouble() * 200 - 100);
            var zl = new Complex(rnd.NextDouble() * 200 - 100, rnd.NextDouble() * 200 - 100);
            double s = Math.Pow(10.0, rnd.NextDouble() * 8 - 4);      // 1e-4 … 1e4
            worstScale = Math.Max(worstScale,
                Math.Abs(WspMargin.FromZ(zg, zl).sm - WspMargin.FromZ(s * zg, s * zl).sm));
        }
        output.WriteLine($"(b) worst |SM(Z) − SM(k·Z)| over 10,000 pairs × random k > 0: {worstScale:G3}");
        Assert.True(worstScale <= 1e-15, $"scaling invariance broken by {worstScale:G3}");

        // ── SM_Y0 = 1 iff ZL = ZG with Re > 0 ────────────────────────────────
        // The "if" needs Im ≠ 0: with both reactances exactly zero the both-parts-zero convention
        // gives iY = 0.5 and the pair reads 0.75, which is a consequence of §2.1c and not a defect.
        var z = new Complex(25.0, -8.0);
        Assert.Equal(1.0, WspMargin.FromZ(z, z).sm, 15);
        Assert.Equal(0.75, WspMargin.FromZ(new Complex(25.0, 0.0), new Complex(25.0, 0.0)).sm, 15);

        // The "only if", over random pairs: nothing but ZL = ZG reaches 1.
        for (int k = 0; k < 10_000; k++)
        {
            var zg = new Complex(rnd.NextDouble() * 100 - 50, rnd.NextDouble() * 100 - 50);
            var zl = new Complex(rnd.NextDouble() * 100 - 50, rnd.NextDouble() * 100 - 50);
            Assert.True(WspMargin.FromZ(zg, zl).sm < 1.0 - 1e-9,
                $"SM_Y0 reached 1 at ZG = {zg} ≠ ZL = {zl}");
        }

        // ── A conjugate match reads exactly 0.5 = −6.02 dB, NOT the top of the scale ──
        // The sentence the user docs must carry: a designer who expects a matched node to read
        // 0 dB will read −6 dB as a problem.
        for (int k = 0; k < 1_000; k++)
        {
            var zg = new Complex(rnd.NextDouble() * 100 + 1e-3, rnd.NextDouble() * 200 - 100);
            if (zg.Imaginary == 0.0) continue;
            var (rY, iY, sm) = WspMargin.FromZ(zg, Complex.Conjugate(zg));
            Assert.Equal(1.0, rY, 15);
            Assert.Equal(0.0, iY, 15);
            Assert.Equal(0.5, sm, 15);
        }
        Assert.Equal(-6.0206, 20.0 * Math.Log10(0.5), 4);

        // ── The −12 dB floor, and its contrapositive ─────────────────────────
        double lowest = double.MaxValue;
        for (int k = 0; k < 100_000; k++)
        {
            var zg = new Complex(rnd.NextDouble() * 1000 + 1e-6, rnd.NextDouble() * 2000 - 1000);
            var zl = new Complex(rnd.NextDouble() * 1000 + 1e-6, rnd.NextDouble() * 2000 - 1000);
            double sm = WspMargin.FromZ(zg, zl).sm;
            Assert.True(sm >= 0.25, $"SM_Y0 = {sm:G17} < 0.25 with Re ZG = {zg.Real:G6} > 0 and Re ZL = {zl.Real:G6} > 0");
            lowest = Math.Min(lowest, sm);
        }
        output.WriteLine($"(b) lowest SM_Y0 over 100,000 random pairs with both real parts positive: " +
                         $"{lowest:G6} ({20 * Math.Log10(lowest):F3} dB); the floor is 0.25 (−12.04 dB)");
        Assert.Equal(-12.0412, 20.0 * Math.Log10(0.25), 4);

        // Equality is APPROACHED and never reached, and a random draw will not find the corner it
        // sits in — both conditions have to hold at once: Re ZL/Re ZG → 0 (so rY → 0.5) with the
        // reactances exactly opposed (so iY = 0). Constructed rather than sampled, because "the
        // bound is tight" is a statement about the corner, not about the bulk.
        foreach (double eps in new[] { 1e-3, 1e-6, 1e-9, 1e-12 })
        {
            double sm = WspMargin.FromZ(new Complex(1.0, 40.0), new Complex(eps, -40.0)).sm;
            output.WriteLine($"    Re ZL/Re ZG = {eps:G1}, reactances opposed: SM_Y0 = {sm:G17}");
            Assert.True(sm > 0.25, $"SM_Y0 = {sm:G17} reached the floor");
            Assert.True(sm <= 0.25 + eps, $"SM_Y0 = {sm:G17} is not within {eps:G1} of the floor");
        }

        // The contrapositive is the interpretive rule: below the floor, one side is active.
        for (int k = 0; k < 100_000; k++)
        {
            var zg = new Complex(rnd.NextDouble() * 200 - 100, rnd.NextDouble() * 200 - 100);
            var zl = new Complex(rnd.NextDouble() * 200 - 100, rnd.NextDouble() * 200 - 100);
            if (WspMargin.FromZ(zg, zl).sm >= 0.25) continue;
            Assert.True(zg.Real <= 0.0 || zl.Real <= 0.0,
                $"SM_Y0 below the floor with both real parts positive: ZG = {zg}, ZL = {zl}");
        }

        // ── SM_Y0 = 0 iff the two static Kurokawa conditions on 1/Y0 hold ────
        Assert.Equal(0.0, WspMargin.FromZ(new Complex(-20.0, 30.0), new Complex(10.0, -30.0)).sm, 15);
        Assert.True(WspMargin.FromZ(new Complex(-20.0, 30.0), new Complex(10.0, -29.0)).sm > 0.0);
        Assert.True(WspMargin.FromZ(new Complex(-20.0, 30.0), new Complex(30.0, -30.0)).sm > 0.0);
    }

    /// <summary>
    /// <c>SM_Y0</c> and <c>SM_H0</c> are not functions of each other even with zero feedback, where
    /// <c>YG = 1/ZG</c> and <c>YL = 1/ZL</c> exactly — the ratio of reciprocals is not the reciprocal
    /// of the ratio (§2.2). The engine gate shows the two collapsing at different FREQUENCIES on one
    /// fixture; this is the same statement without a circuit.
    /// </summary>
    [Fact]
    public void B_TheTwoMarginsAreNotFunctionsOfEachOther_EvenWithNoFeedback()
    {
        var rnd = new Random(90903);
        int disagreed = 0;
        for (int k = 0; k < 10_000; k++)
        {
            var zg = new Complex(rnd.NextDouble() * 100 - 20, rnd.NextDouble() * 200 - 100);
            var zl = new Complex(rnd.NextDouble() * 100 - 20, rnd.NextDouble() * 200 - 100);
            double smY = WspMargin.FromZ(zg, zl).sm;
            double smH = WspMargin.FromY(Complex.One / zg, Complex.One / zl).sm;
            if (Math.Abs(smY - smH) > 1e-6) disagreed++;
        }
        output.WriteLine($"(b) SM_Y0 ≠ SM_H0 on {disagreed} of 10,000 zero-feedback pairs");
        Assert.True(disagreed > 9_000,
            $"only {disagreed} of 10,000 disagreed — the two margins would have to be one quantity.");
    }
}
