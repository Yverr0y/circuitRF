// ================================================================
//  PassiveMetricsTests.cs — the two-terminal readouts a passive part is bought on.
//
//  The oracle throughout is a CLOSED FORM, never another circuitRF path: a series R-L-C is built
//  analytically, turned into S-parameters by the textbook fixture relation, and the readouts are
//  asked to give the R, the L, the C and the resonance back. A test that compared PassiveMetrics
//  against, say, the Z-parameter conversion would be two of our own routines agreeing with each
//  other, which proves nothing about either.
//
//  The headline gate is FixtureMatters: the SAME file read under the wrong fixture returns a
//  smooth, plausible, wrong curve. That is the failure this whole feature exists to prevent, and it
//  is worth a test that states the size of the error rather than one that merely checks arithmetic.
// ================================================================

using System;
using System.Linq;
using System.Numerics;
using NumFlat;
using RfCore;
using RfCore.Data;
using Xunit;

namespace RfCore.Tests;

public class PassiveMetricsTests
{
    private const double Z0    = 50.0;
    private const double Esr   = 5e-3;      // 5 mΩ  — a real MLCC's floor
    private const double Esl   = 0.5e-9;    // 0.5 nH — the mounting/package inductance
    private const double Cap   = 100e-9;    // 100 nF

    /// <summary>Analytic SRF of the series R-L-C: 1/(2π√(LC)) ≈ 22.5079 MHz for the values above.</summary>
    private static double SrfHz => 1.0 / (2.0 * Math.PI * Math.Sqrt(Esl * Cap));

    private static Complex Zrlc(double f) =>
        new(Esr, 2.0 * Math.PI * f * Esl - 1.0 / (2.0 * Math.PI * f * Cap));

    /// <summary>A log sweep spanning three decades around the resonance.</summary>
    private static double[] Sweep(int n = 601, double lo = 1e6, double hi = 1e9) =>
        [.. Enumerable.Range(0, n).Select(i => lo * Math.Pow(hi / lo, i / (double)(n - 1)))];

    /// <summary>The DUT as a shunt element bridging two Z0 lines: S21 = 2Z/(2Z+Z0).</summary>
    private static Mat<Complex>[] ShuntFixture(double[] f, Func<double, Complex> z) =>
        [.. f.Select(fi =>
        {
            var Z   = z(fi);
            var s21 = 2.0 * Z / (2.0 * Z + Z0);
            var s11 = -Z0 / (2.0 * Z + Z0);
            var m = new Mat<Complex>(2, 2);
            m[0, 0] = s11; m[0, 1] = s21; m[1, 0] = s21; m[1, 1] = s11;
            return m;
        })];

    /// <summary>The DUT in series with the through line: S21 = 2·Z0/(Z+2·Z0).</summary>
    private static Mat<Complex>[] SeriesFixture(double[] f, Func<double, Complex> z) =>
        [.. f.Select(fi =>
        {
            var Z   = z(fi);
            var s21 = 2.0 * Z0 / (Z + 2.0 * Z0);
            var s11 = Z / (Z + 2.0 * Z0);
            var m = new Mat<Complex>(2, 2);
            m[0, 0] = s11; m[0, 1] = s21; m[1, 0] = s21; m[1, 1] = s11;
            return m;
        })];

    /// <summary>The DUT as the only thing at a 1-port: S11 = (Z−Z0)/(Z+Z0).</summary>
    private static Mat<Complex>[] OnePortFixture(double[] f, Func<double, Complex> z) =>
        [.. f.Select(fi =>
        {
            var Z = z(fi);
            var m = new Mat<Complex>(1, 1);
            m[0, 0] = (Z - Z0) / (Z + Z0);
            return m;
        })];

    private static Complex[] Refs(int n, double r = Z0) => [.. Enumerable.Repeat(new Complex(r, 0), n)];

    // ── the impedance itself comes back exactly, under all three fixtures ───────────────────────

    [Theory]
    [InlineData(PassiveExtraction.ShuntThrough)]
    [InlineData(PassiveExtraction.SeriesThrough)]
    [InlineData(PassiveExtraction.OnePort)]
    public void EachFixtureInvertsItsOwnRelationExactly(PassiveExtraction mode)
    {
        var f = Sweep();
        var mats = mode switch
        {
            PassiveExtraction.ShuntThrough  => ShuntFixture(f, Zrlc),
            PassiveExtraction.SeriesThrough => SeriesFixture(f, Zrlc),
            _                               => OnePortFixture(f, Zrlc),
        };
        int ports = mode == PassiveExtraction.OnePort ? 1 : 2;

        var z = PassiveMetrics.Impedance(mats, Refs(ports), mode, 1, ports);

        for (int i = 0; i < f.Length; i++)
        {
            var want = Zrlc(f[i]);
            // Relative, because |Z| runs from ~1.6 kΩ at the bottom of the sweep to 5 mΩ at
            // resonance — an absolute tolerance would be vacuous at one end and impossible at the
            // other.
            Assert.True((z[i] - want).Magnitude <= 1e-9 * want.Magnitude,
                $"{mode} at {f[i]:G4} Hz: got {z[i]}, want {want}");
        }
    }

    // ── the readouts give the component values back ────────────────────────────────────────────

    [Fact]
    public void EsrCeffLeffAndQRecoverTheComponentTheyWereBuiltFrom()
    {
        var f    = Sweep();
        var mats = ShuntFixture(f, Zrlc);
        var z0   = Refs(2);

        double[] esr  = PassiveMetrics.Evaluate(mats, z0, f, PassiveExtraction.ShuntThrough, PassiveMetric.Esr,  1, 2);
        double[] ceff = PassiveMetrics.Evaluate(mats, z0, f, PassiveExtraction.ShuntThrough, PassiveMetric.Ceff, 1, 2);
        double[] leff = PassiveMetrics.Evaluate(mats, z0, f, PassiveExtraction.ShuntThrough, PassiveMetric.Leff, 1, 2);
        double[] q    = PassiveMetrics.Evaluate(mats, z0, f, PassiveExtraction.ShuntThrough, PassiveMetric.Q,    1, 2);

        // ESR is the series resistance at EVERY frequency — it is not a resonance-only quantity.
        Assert.All(esr, r => Assert.Equal(Esr, r, 12));

        // A decade below resonance the part is a capacitor, and C_eff is its capacitance. The ESL
        // pulls it up by 1/(1 − ω²LC) — 1 % at a decade down — so the tolerance is the physics,
        // not slop.
        int lo = Array.FindIndex(f, x => x >= SrfHz / 10.0);
        Assert.InRange(ceff[lo], Cap * 0.99, Cap * 1.02);
        Assert.True(double.IsNaN(leff[lo]), "below resonance the part is capacitive: L_eff has no value");

        // A decade above, it is the mounting inductance and nothing else.
        int hi = Array.FindIndex(f, x => x >= SrfHz * 10.0);
        Assert.InRange(leff[hi], Esl * 0.98, Esl * 1.01);
        Assert.True(double.IsNaN(ceff[hi]), "above resonance the part is inductive: C_eff has no value");

        // Q = |X|/R, checked against the closed form at the same point rather than a magic number.
        var zHi = Zrlc(f[hi]);
        Assert.Equal(Math.Abs(zHi.Imaginary) / zHi.Real, q[hi], 8);
    }

    [Fact]
    public void SelfResonanceMatchesTheClosedForm()
    {
        var f = Sweep();
        double? srf = PassiveMetrics.SelfResonance(
            ShuntFixture(f, Zrlc), Refs(2), f, PassiveExtraction.ShuntThrough, 1, 2);

        Assert.NotNull(srf);
        // Interpolated between two log-spaced samples, so the residual is the grid's, not the
        // method's: 601 points over three decades is ~1.15 % per step.
        Assert.InRange(srf!.Value, SrfHz * 0.999, SrfHz * 1.001);
    }

    /// <summary>
    /// The resonance rule is DIRECTIONAL. A parallel tank is inductive below its resonance and
    /// capacitive above, so its reactance crosses zero the other way — and that crossing is a |Z|
    /// MAXIMUM, the opposite of what anyone means by "self-resonance". An unsigned zero-crossing
    /// search would report it, so the sign is the thing under test.
    /// </summary>
    [Fact]
    public void AParallelResonanceIsNotReportedAsSelfResonance()
    {
        var f = Sweep(601, 1e6, 1e9);
        Complex Tank(double fi)
        {
            double w = 2.0 * Math.PI * fi;
            var y = 1.0 / new Complex(0.0, w * Esl) + new Complex(0.0, w * Cap) + 1.0 / 1e6;
            return 1.0 / y;
        }

        Assert.Null(PassiveMetrics.SelfResonance(
            ShuntFixture(f, Tank), Refs(2), f, PassiveExtraction.ShuntThrough, 1, 2));
    }

    [Fact]
    public void ASweepThatDoesNotContainTheResonanceReportsNothing_NotABandEdge()
    {
        // Stop two decades below the real SRF. A clamped answer here would be read as a measurement.
        var f = Sweep(101, 1e4, SrfHz / 100.0);
        Assert.Null(PassiveMetrics.SelfResonance(
            ShuntFixture(f, Zrlc), Refs(2), f, PassiveExtraction.ShuntThrough, 1, 2));
    }

    // ── the headline: the fixture is not inferable, and the wrong one is plausible ──────────────

    /// <summary>
    /// The same shunt-through file read as a 1-port reflection. It does not fail, it does not
    /// produce NaN, and it does not look wrong on a plot — it produces a different part.
    ///
    /// <para><b>The error has an exact closed form, and it is not where intuition puts it.</b> For
    /// a shunt DUT, S11 = −Z0/(2Z+Z0), and feeding that to the 1-port relation gives
    /// Z0·(1+S11)/(1−S11) = <b>Z ∥ Z0</b>. So the misread SATURATES at Z0: it is close to right
    /// wherever |Z| ≪ Z0 — which for a bulk decoupling capacitor is most of its useful band — and
    /// catastrophically wrong for anything whose impedance approaches or exceeds the reference. A
    /// 10 pF capacitor, an inductor, a bead: all of them come back as ≈ 50 Ω.</para>
    ///
    /// <para>The parallel form is pinned over the whole sweep, and the saturation is shown on a
    /// part that actually reaches it. The 100 nF part used everywhere else in this class does NOT,
    /// which is worth knowing: a test that reached for the obvious fixture would have shown a 1 %
    /// error and concluded the fixture does not matter much.</para>
    /// </summary>
    [Fact]
    public void FixtureMatters_AShuntFileReadAsAOnePortIsZParallelZ0AndSaturates()
    {
        var f    = Sweep();
        var mats = ShuntFixture(f, Zrlc);
        var z0   = Refs(2);

        var right = PassiveMetrics.Impedance(mats, z0, PassiveExtraction.ShuntThrough, 1, 2);
        var wrong = PassiveMetrics.Impedance(mats, z0, PassiveExtraction.OnePort,      1, 2);

        for (int i = 0; i < f.Length; i++)
        {
            var parallel = right[i] * Z0 / (right[i] + Z0);
            Assert.True((wrong[i] - parallel).Magnitude <= 1e-9 * parallel.Magnitude,
                $"at {f[i]:G4} Hz: misread {wrong[i]}, Z∥Z0 {parallel}");
        }

        // A 10 pF part: |X| is 1.6 kΩ at 10 MHz, well above the reference, so the misread clamps.
        const double CSmall = 10e-12;
        Complex Small(double fi)
        {
            double w = 2.0 * Math.PI * fi;
            return new Complex(0.05, w * Esl - 1.0 / (w * CSmall));
        }
        var fs    = Sweep(401, 1e6, 1e8);
        var small = ShuntFixture(fs, Small);

        var sRight = PassiveMetrics.Impedance(small, z0, PassiveExtraction.ShuntThrough, 1, 2);
        var sWrong = PassiveMetrics.Impedance(small, z0, PassiveExtraction.OnePort,      1, 2);

        int at = Array.FindIndex(fs, x => x >= 1e7);
        Assert.True(sRight[at].Magnitude > 20.0 * Z0,
            $"premise: |Z| should be well above Z0 here, got {sRight[at].Magnitude}");
        Assert.True(sWrong[at].Magnitude < 1.01 * Z0,
            $"the misread should saturate at Z0, got {sWrong[at].Magnitude}");

        // Which is the error that reaches the user: the capacitance is out by two orders.
        double cRight = PassiveMetrics.Evaluate(sRight, fs, PassiveMetric.Ceff)[at];
        double cWrong = PassiveMetrics.Evaluate(sWrong, fs, PassiveMetric.Ceff)[at];
        Assert.InRange(cRight, CSmall * 0.99, CSmall * 1.02);
        Assert.True(cWrong > 100.0 * CSmall, $"expected the misread capacitance to be far out; got {cWrong} F");

        // And it is finite and smooth over the whole sweep — no NaN, no discontinuity, nothing a
        // user would notice on a plot.
        Assert.All(wrong, v => Assert.True(double.IsFinite(v.Real) && double.IsFinite(v.Imaginary)));
    }

    // ── reference impedance ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A file referenced to something other than 50 Ω gives the SAME impedance, because the
    /// extraction renormalises first. The closed forms are all derived for a real Z0; skipping the
    /// renormalisation would return a number rather than an error.
    /// </summary>
    [Fact]
    public void ANonFiftyOhmReferenceGivesTheSameImpedance()
    {
        var f = Sweep(201);

        Mat<Complex>[] At(double zref) =>
            [.. f.Select(fi =>
            {
                var Z   = Zrlc(fi);
                var s21 = 2.0 * Z / (2.0 * Z + zref);
                var s11 = -zref / (2.0 * Z + zref);
                var m = new Mat<Complex>(2, 2);
                m[0, 0] = s11; m[0, 1] = s21; m[1, 0] = s21; m[1, 1] = s11;
                return m;
            })];

        var a = PassiveMetrics.Impedance(At(50.0), Refs(2, 50.0), PassiveExtraction.ShuntThrough, 1, 2);
        var b = PassiveMetrics.Impedance(At(75.0), Refs(2, 75.0), PassiveExtraction.ShuntThrough, 1, 2);

        for (int i = 0; i < f.Length; i++)
            Assert.True((a[i] - b[i]).Magnitude <= 1e-9 * a[i].Magnitude,
                $"at {f[i]:G4} Hz: 50 Ω file gave {a[i]}, 75 Ω file gave {b[i]}");
    }

    [Fact]
    public void QIsNaNWhereTheResistanceIsNotPositive()
    {
        var f = new[] { 1e9 };
        var mats = ShuntFixture(f, _ => new Complex(0.0, 10.0));   // lossless
        Assert.True(double.IsNaN(
            PassiveMetrics.Evaluate(mats, Refs(2), f, PassiveExtraction.ShuntThrough, PassiveMetric.Q, 1, 2)[0]));
    }

    [Fact]
    public void NeedsPortPairIsAPropertyOfTheFixture_NotOfTheMetric()
    {
        Assert.True(PassiveMetrics.NeedsPortPair(PassiveExtraction.ShuntThrough));
        Assert.True(PassiveMetrics.NeedsPortPair(PassiveExtraction.SeriesThrough));
        Assert.False(PassiveMetrics.NeedsPortPair(PassiveExtraction.OnePort));
    }
}
