// ================================================================
//  TouchstoneHealthTests.cs — is this S-parameter file sound?
//
//  Two kinds of test here and they answer different questions.
//
//  The structural ones (port count, frequency order, Z0, passivity, reciprocity) are ordinary:
//  build a file with a known defect, assert it is found and located.
//
//  The CAUSALITY ones are the reason this file is long. The pre-cursor measurement has a floor set
//  by band truncation and a second, larger floor set by whether the sweep resolves its own
//  response, and the threshold constant is chosen from MEASUREMENT rather than theory. So the
//  reference table is a test: causal responses of several shapes must land well below the
//  threshold, genuinely acausal ones well above, and the under-resolved case must be shown to
//  exist — because it is the reason the finding names two possible causes instead of accusing the
//  file of acausality. If someone later tightens the threshold, the causal rows fail and say why.
// ================================================================

using System;
using System.Linq;
using System.Numerics;
using NumFlat;
using RfCore;
using RfCore.Data;
using Xunit;
using Xunit.Abstractions;

namespace RfCore.Tests;

public class TouchstoneHealthTests(ITestOutputHelper output)
{
    private const double Z0 = 50.0;

    private static SNP Make(double[] f, Func<double, Mat<Complex>> m, double z0 = Z0) =>
        new(f, [.. f.Select(m)], MatrixType.S, MatrixFormat.RI, new Complex(z0, 0));

    private static Mat<Complex> M2(Complex s11, Complex s12, Complex s21, Complex s22)
    {
        var m = new Mat<Complex>(2, 2);
        m[0, 0] = s11; m[0, 1] = s12; m[1, 0] = s21; m[1, 1] = s22;
        return m;
    }

    /// <summary>A shunt DUT across a through line — the fixture every passive reference below uses.</summary>
    private static Mat<Complex> Shunt(Complex z) =>
        M2(-Z0 / (2 * z + Z0), 2 * z / (2 * z + Z0), 2 * z / (2 * z + Z0), -Z0 / (2 * z + Z0));

    private static double[] Uniform(int n, double df) => [.. Enumerable.Range(1, n).Select(k => k * df)];

    // ── structure ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnAmplifierIsReportedAsHavingGainAndAsNonReciprocal()
    {
        var f = Uniform(50, 1e8);
        var h = TouchstoneHealth.Analyze(Make(f, _ => M2(0.1, 0.01, 3.16, 0.2)));

        Assert.True(h.WorstSigmaMax > 1.0 + TouchstoneHealth.PassivityTolerance);
        Assert.True(h.WorstReciprocityError > TouchstoneHealth.ReciprocityTolerance);
        // Located, not just detected — the frequency is what sends someone to a plot.
        Assert.Contains(h.WorstSigmaMaxFrequencyHz, f);
        Assert.Equal(Math.Abs(3.16 - 0.01), h.WorstReciprocityError, 9);
    }

    [Fact]
    public void APassiveReciprocalPartIsClean()
    {
        var f = Uniform(200, 5e6);
        var h = TouchstoneHealth.Analyze(Make(f, fi =>
        {
            double w = 2 * Math.PI * fi;
            return Shunt(new Complex(5e-3, w * 0.5e-9 - 1.0 / (w * 100e-9)));
        }));

        Assert.True(h.WorstSigmaMax <= 1.0 + TouchstoneHealth.PassivityTolerance,
            $"σmax was {h.WorstSigmaMax}");
        Assert.True(h.WorstReciprocityError <= TouchstoneHealth.ReciprocityTolerance);
        Assert.Equal(-1, h.FirstNonIncreasingIndex);
        Assert.True(h.GridUniform);
    }

    [Fact]
    public void ARepeatedFrequencyIsFoundAndItsIndexReported()
    {
        double[] f = [1e9, 2e9, 2e9, 3e9];
        var h = TouchstoneHealth.Analyze(Make(f, _ => M2(0.1, 0.5, 0.5, 0.1)));
        Assert.Equal(2, h.FirstNonIncreasingIndex);
        Assert.False(h.GridUniform);
    }

    [Fact]
    public void AnOutOfOrderFrequencyIsFoundAtTheFirstOffendingPoint()
    {
        double[] f = [1e9, 3e9, 2e9, 4e9];
        Assert.Equal(2, TouchstoneHealth.Analyze(Make(f, _ => M2(0.1, 0.5, 0.5, 0.1))).FirstNonIncreasingIndex);
    }

    [Fact]
    public void AOnePortHasNoReciprocityQuestionToAnswer()
    {
        var m = new Mat<Complex>(1, 1);
        m[0, 0] = 0.3;
        Assert.True(double.IsNaN(TouchstoneHealth.ReciprocityError(m)));
    }

    // ── causality: the reference table the threshold was set from ──────────────────────────────

    public static TheoryData<string, bool> CausalityReferences() => new()
    {
        // name                          expected to trip the threshold
        { "shunt C 100pF + 0.5nH ESL",   false },
        { "shunt L 10nH",                false },
        { "series C 1pF",                false },
        { "RC low-pass, 1 ns",           false },
        { "delay line, +2 ns",           false },
        { "plain through, S21 = 1",      false },
        { "ACAUSAL: delay run backwards", true },
        { "ACAUSAL: right-half-plane pole", true },
    };

    [Theory]
    [MemberData(nameof(CausalityReferences))]
    public void ThePrecursorMeasurementSeparatesCausalFromAcausal(string name, bool shouldTrip)
    {
        var f = Uniform(400, 25e6);                     // uniform, reaching DC in one step
        Func<double, Mat<Complex>> body = name switch
        {
            "shunt C 100pF + 0.5nH ESL" => fi => Shunt(Rlc(fi, 0.05, 0.5e-9, 100e-12)),
            "shunt L 10nH"              => fi => Shunt(new Complex(0.5, 2 * Math.PI * fi * 10e-9)),
            "series C 1pF"              => fi => Series(new Complex(0.1, -1.0 / (2 * Math.PI * fi * 1e-12))),
            "RC low-pass, 1 ns"         => fi => Thru(1.0 / (1.0 + new Complex(0, 2 * Math.PI * fi * 1e-9))),
            "delay line, +2 ns"         => fi => Thru(Complex.Exp(new Complex(0, -2 * Math.PI * fi * 2e-9))),
            "plain through, S21 = 1"    => _  => Thru(Complex.One),
            "ACAUSAL: delay run backwards"      => fi => Thru(Complex.Exp(new Complex(0, +2 * Math.PI * fi * 2e-9))),
            _ /* right-half-plane pole */       => fi => Thru(1.0 / (1.0 - new Complex(0, 2 * Math.PI * fi * 1e-9))),
        };

        var h = TouchstoneHealth.Analyze(Make(f, body));
        output.WriteLine($"{name,-36} precursor = {h.CausalityPrecursorRatio:P2}  ({h.Causality})");

        Assert.NotEqual(CausalityVerdict.NotEvaluated, h.Causality);
        Assert.Equal(shouldTrip ? CausalityVerdict.PrecursorEnergyHigh : CausalityVerdict.Consistent,
                     h.Causality);

        // Margin, not just the side of the line — the threshold is empirical, so the gate has to
        // fail loudly if a change narrows the separation rather than only when it crosses.
        if (shouldTrip)
            Assert.True(h.CausalityPrecursorRatio > 2.0 * TouchstoneHealth.CausalityPrecursorThreshold,
                $"{name}: only {h.CausalityPrecursorRatio:P2}, too close to the threshold to be a gate");
        else
            Assert.True(h.CausalityPrecursorRatio < 0.5 * TouchstoneHealth.CausalityPrecursorThreshold,
                $"{name}: {h.CausalityPrecursorRatio:P2}, too close to the threshold to be a gate");
    }

    /// <summary>
    /// The case that stops this finding from being worded as "not causal". A ferrite bead whose
    /// impedance corner is at 95 MHz, sampled on a 25 MHz uniform grid to 10 GHz, is perfectly
    /// causal and measures like an acausal file; refining the step to 1 MHz takes the same network
    /// well under the threshold. Nothing available to the measurement separates the two, which is
    /// why the diagnostic names both readings.
    /// </summary>
    [Fact]
    public void AnUnderResolvedSweepMeasuresLikeAnAcausalOne_WhichIsWhyTheFindingNamesBothCauses()
    {
        static Mat<Complex> Bead(double fi)
        {
            var jwl = new Complex(0, 2 * Math.PI * fi * 1e-6);
            return Shunt(0.1 + jwl * 600.0 / (600.0 + jwl));
        }

        var coarse = TouchstoneHealth.Analyze(Make(Uniform(400,   25e6), Bead));
        var fine   = TouchstoneHealth.Analyze(Make(Uniform(10000, 1e6),  Bead));

        output.WriteLine($"bead, 25 MHz step: {coarse.CausalityPrecursorRatio:P2}");
        output.WriteLine($"bead,  1 MHz step: {fine.CausalityPrecursorRatio:P2}");

        Assert.Equal(CausalityVerdict.PrecursorEnergyHigh, coarse.Causality);
        Assert.Equal(CausalityVerdict.Consistent,          fine.Causality);
    }

    // ── causality: when it declines to run ─────────────────────────────────────────────────────

    [Fact]
    public void ALogSweepIsNotEvaluated_AndSaysSo()
    {
        var f = Enumerable.Range(0, 200).Select(i => 1e6 * Math.Pow(1000.0, i / 199.0)).ToArray();
        var h = TouchstoneHealth.Analyze(Make(f, fi => Shunt(Rlc(fi, 0.05, 0.5e-9, 100e-9))));

        Assert.Equal(CausalityVerdict.NotEvaluated, h.Causality);
        Assert.Contains("uniform", h.CausalitySkipReason);
        Assert.False(h.GridUniform);
    }

    [Fact]
    public void AUniformGridThatDoesNotReachDcInOneStepIsNotEvaluated()
    {
        // Starts at 1 GHz with a 25 MHz step: the 39 points between DC and the first sample are
        // missing, and inventing them would decide the answer.
        var f = Enumerable.Range(0, 200).Select(i => 1e9 + i * 25e6).ToArray();
        var h = TouchstoneHealth.Analyze(Make(f, fi => Shunt(Rlc(fi, 0.05, 0.5e-9, 1e-12))));

        Assert.Equal(CausalityVerdict.NotEvaluated, h.Causality);
        Assert.Contains("DC", h.CausalitySkipReason);
        Assert.True(h.GridUniform, "the grid IS uniform — it just does not reach DC");
    }

    [Fact]
    public void AGridStartingExactlyAtDcIsEvaluated()
    {
        var f = Enumerable.Range(0, 400).Select(k => k * 25e6).ToArray();
        var h = TouchstoneHealth.Analyze(Make(f, fi =>
            Thru(1.0 / (1.0 + new Complex(0, 2 * Math.PI * fi * 1e-9)))));

        Assert.Equal(CausalityVerdict.Consistent, h.Causality);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────

    private static Complex Rlc(double f, double r, double l, double c)
    {
        double w = 2 * Math.PI * f;
        return new Complex(r, w * l - 1.0 / (w * c));
    }

    private static Mat<Complex> Series(Complex z) =>
        M2(z / (z + 2 * Z0), 2 * Z0 / (z + 2 * Z0), 2 * Z0 / (z + 2 * Z0), z / (z + 2 * Z0));

    /// <summary>A matched two-port with a given transmission — reflectionless, so the only thing
    /// under test is the transmission's own time-domain behaviour.</summary>
    private static Mat<Complex> Thru(Complex s21) => M2(0, s21, s21, 0);
}
