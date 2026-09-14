// Owner bug report, 2026-09-14: "an EM sim of this file showed about -80 dB S(2,1). This is a
// short transmission line. This is unacceptable."
//
// It was a short transmission line, and the answer was -107 dB. The `.cem` carried
// `"Deembed": false`, and with de-embedding off the planar kernel published the RAW delta-gap
// solve. That is not the structure's response with a launch included — at an EDGE port the cut sits
// one cell INSIDE the drawn metal, so the source's outer terminal is an isolated sliver and the
// port drives nothing but that sliver's fringing capacitance:
//
//   * a plain 3.83 mm x 254 um microstrip published S11 = S22 = +1 and S21 = -115 dB;
//   * Y11 came out as jwC with C = 13.9 fF at a 254 um port and 31.8 fF at a 558.8 um one — a ratio
//     of 2.29 against the port-width ratio of 2.20, which is the sliver and nothing else;
//   * DOUBLING the line to 7.66 mm moved S11 in the FOURTH DECIMAL. The raw answer carries no
//     information about the structure at all.
//
// Four things had to change, and this file holds three of them (the fourth, dropping the `.cem`
// field and warning about a legacy one, is `Ui.Tests/Em/PortClearanceRefusalTests.cs`):
//
//   R1  No refusal in the Mom engine may recommend reading the raw solve. The mesh-ceiling refusal
//       did, in prose, and that recommendation is how this file came to be written that way.
//   R2  The ceiling refusal must name the remedy that actually BINDS — the lower band edge.
//   R3  `CalibrationUsable` must not read 1 on a run that calibrated nothing.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using Xunit;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

public sealed class RawSolveAndCalibrationRemedyTests(ITestOutputHelper output)
{
    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "circuitRF.slnx")))
            dir = Path.GetDirectoryName(dir) ?? "";
        return dir;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R1 — no refusal recommends the raw solve, and the source scan is what holds it
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The recommendation is the defect, so it is the recommendation that is gated.</b>
    ///
    /// <para>Prose in a refusal is not decoration — it is the only instruction a user gets at the
    /// moment they are stuck, and four separate refusals in <c>PlanarSolve</c> ended with "turn
    /// de-embedding off and read the raw solve". A user who followed it got an open circuit and a
    /// Touchstone file with no error in it. Scanned over the source rather than asserted on one
    /// message, because the phrasing varied at all four sites and a fifth would be written the same
    /// way.</para>
    /// </summary>
    [Fact]
    public void NoRefusalInTheMomEngineRecommendsReadingTheRawSolve()
    {
        string mom = Path.Combine(RepoRoot(), "src", "Engine", "Mom");
        var offenders = new List<string>();

        foreach (string f in Directory.EnumerateFiles(mom, "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                           StringComparison.Ordinal)) continue;

            string[] lines = File.ReadAllLines(f);
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) ||
                    t.StartsWith("///", StringComparison.Ordinal)) continue;   // commentary may explain it

                if (t.Contains("turn de-embedding off", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("de-embedding off and read", StringComparison.OrdinalIgnoreCase))
                    offenders.Add($"{Path.GetFileName(f)}:{i + 1}  {t}");
            }
        }

        foreach (string o in offenders) output.WriteLine(o);
        Assert.Empty(offenders);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R2 — the ceiling refusal names the LOWER BAND EDGE, which is what actually binds
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The standard's size tracks the bottom of the sweep, not the mesh and not the port width.</b>
    ///
    /// <para>This is the arithmetic the refusal's new sentence rests on, asserted directly so the
    /// sentence cannot become false while still being printed. A separation is aimed at
    /// <see cref="PlanarCalibrationSettings.TargetElectricalDegrees"/> at each sub-band's geometric
    /// mean, so the length scales as 1/f_lo, and the separation COUNT steps with the band ratio.</para>
    ///
    /// <para>The measured case: 100 MHz-6 GHz on a 3.8 mm DUT wanted 3 separations and a 161.5 mm
    /// longest standard — 42x the DUT — at 79,055 unknowns against a 12,000 ceiling.</para>
    /// </summary>
    [Fact]
    public void TheLongestStandardIsSetByTheLowerBandEdge_NotByTheMesh()
    {
        var slab = PlanarLineFixtures.Fr4Line(3.8e-3, 6e9).Slab;

        double at100M = PlanarCalibration.LongestStandardLengthM(slab, 100e6, 6e9);
        double at1G   = PlanarCalibration.LongestStandardLengthM(slab, 1e9,   6e9);
        double at2G   = PlanarCalibration.LongestStandardLengthM(slab, 2e9,   6e9);

        output.WriteLine($"longest standard: {at100M * 1e3:0.#} mm @100 MHz, " +
                         $"{at1G * 1e3:0.#} mm @1 GHz, {at2G * 1e3:0.#} mm @2 GHz");

        // Monotone in the lower edge, and STEEPLY so — that is the whole point of naming it.
        Assert.True(at100M > at1G && at1G > at2G);
        Assert.True(at100M / at1G > 5.0,
                    $"a 10x lower band edge should cost far more than 5x in standard length, got " +
                    $"{at100M / at1G:0.##}x");

        // And the separation COUNT steps rather than sliding, which is why StartFrequencyThatFits
        // scans upward instead of bisecting.
        Assert.Equal(3, PlanarCalibration.SuggestDeltas(slab, 100e6, 6e9).Length);
        Assert.Equal(2, PlanarCalibration.SuggestDeltas(slab, 1e9,   6e9).Length);
        Assert.Single(PlanarCalibration.SuggestDeltas(slab, 2e9,   6e9));
    }

    /// <summary>
    /// The frequency the refusal offers is one that actually FITS, and it is offered on the 1-2-5
    /// ladder so it is a number someone would type. Rounded UP for the same reason: a number rounded
    /// down past the estimate hands back the refusal it was supposed to clear.
    /// </summary>
    [Fact]
    public void StartFrequencyThatFits_ReturnsABandEdgeWhoseStandardIsUnderTheCeiling()
    {
        var slab = PlanarLineFixtures.Fr4Line(3.8e-3, 6e9).Slab;

        // The measured case: 79,055 unknowns on the 161.5 mm standard, against the accelerated
        // ceiling. Scaling is by LENGTH RATIO, so the measured pair is the calibration.
        double lMax = PlanarCalibration.LongestStandardLengthM(slab, 100e6, 6e9);
        double? fit = PlanarCalibration.StartFrequencyThatFits(
            slab, 100e6, 6e9, null, ceiling: 12_000, measuredN: 79_055, measuredLengthM: lMax);

        Assert.NotNull(fit);
        double tidy = PlanarCalibration.RoundUpToTidyFrequency(fit!.Value);
        output.WriteLine($"fits at {fit.Value / 1e6:0.#} MHz, reported as {tidy / 1e6:0.#} MHz");

        // It fits at the frequency it names — the predicate the sentence asserts.
        double predicted = 79_055 * (PlanarCalibration.LongestStandardLengthM(slab, tidy, 6e9) / lMax);
        Assert.True(predicted <= 12_000,
                    $"the named band edge must fit; predicted {predicted:N0} against 12,000");

        // …and it is not a vacuous answer that just returns the top of the band.
        Assert.True(tidy < 6e9);

        // A ceiling nothing can meet inside the band returns null rather than a frequency that
        // would refuse again.
        Assert.Null(PlanarCalibration.StartFrequencyThatFits(
            slab, 100e6, 6e9, null, ceiling: 10, measuredN: 79_055, measuredLengthM: lMax));
    }

    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(1.3, 2.0)]
    [InlineData(2.0, 2.0)]
    [InlineData(2.1, 5.0)]
    [InlineData(5.0, 5.0)]
    [InlineData(7.7, 10.0)]
    public void RoundUpToTidyFrequency_LandsOnThe125Ladder(double inGhz, double wantGhz)
        => Assert.Equal(wantGhz * 1e9, PlanarCalibration.RoundUpToTidyFrequency(inGhz * 1e9), 3);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R3 — CalibrationUsable must not say "usable" about a run that calibrated nothing
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The flag a reader checks before trusting the file said 1 for a run in which every other
    /// calibration diagnostic beside it was NaN.</b>
    ///
    /// <para>That is how the reported bug survived inspection: Gamma, Zc, Eeff, Cpul, AttenDbPerM
    /// and CalElectricalDeg were NaN at all 11 frequencies, and <c>CalibrationUsable</c> read 1.0 at
    /// all 11. It asked only whether the FREQUENCY was above zero; it now also asks whether anything
    /// was actually calibrated, which is the question the cube's name implies.</para>
    ///
    /// <para>Both directions are asserted in one run pair, because a fix that returns NaN
    /// unconditionally would pass the half of this test that matters most and break every
    /// de-embedded run silently.</para>
    /// </summary>
    [Fact]
    public void CalibrationUsable_IsNaNWhenNothingWasCalibrated_AndOneWhenSomethingWas()
    {
        var problem = PlanarLineFixtures.Fr4Line(6e-3, 5e9);
        var ports   = PlanarLineFixtures.EndPorts(problem);
        double[] freqs = [5e9];

        var raw = new PlanarKernel().Solve(
            problem, PlanarMeshSettings.Default, ports, freqs,
            PlanarSolveSettings.Default with { Deembed = false });

        var usableRaw = raw.Data[$"{PlanarKernel.DiagnosticsGroup}.CalibrationUsable"];
        var gammaRaw  = raw.Data[$"{PlanarKernel.DiagnosticsGroup}.Gamma"];

        output.WriteLine($"de-embedding off: CalibrationUsable = {usableRaw.RealValues[0]}, " +
                         $"Gamma = {gammaRaw.ComplexValues[0]}");

        // The invariant, stated as the relationship rather than as two separate facts: the flag is
        // NaN exactly where the numbers it is a verdict ON are NaN.
        Assert.True(double.IsNaN(gammaRaw.ComplexValues[0].Real), "precondition: nothing was calibrated");
        Assert.True(double.IsNaN(usableRaw.RealValues[0]),
                    "a run that calibrated nothing must not report its calibration as usable");

        var deembedded = new PlanarKernel().Solve(
            problem, PlanarMeshSettings.Default, ports, freqs, PlanarSolveSettings.Default);

        var usableDe = deembedded.Data[$"{PlanarKernel.DiagnosticsGroup}.CalibrationUsable"];
        var gammaDe  = deembedded.Data[$"{PlanarKernel.DiagnosticsGroup}.Gamma"];

        output.WriteLine($"de-embedded: CalibrationUsable = {usableDe.RealValues[0]}, " +
                         $"Gamma = {gammaDe.ComplexValues[0]}");

        Assert.False(double.IsNaN(gammaDe.ComplexValues[0].Real));
        Assert.Equal(1.0, usableDe.RealValues[0]);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // The finding itself — the raw solve reads an edge port as an OPEN, so it is never an answer
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The measurement that removed the setting, kept so nobody re-adds it.</b>
    ///
    /// <para>Not "the raw answer is less accurate" — the raw answer is an open circuit. S11 is +1 to
    /// within the port's own fringing capacitance, and S21 is in the −100 dB region on a line whose
    /// de-embedded loss is a tenth of a dB. Asserted on a fixture rather than on the reported file so
    /// it costs one small solve, and asserted as a CONTRAST so it cannot be satisfied by a kernel
    /// that has stopped working altogether.</para>
    /// </summary>
    [Fact]
    public void TheRawSolveReadsAnEdgePortAsAnOpen_WhichIsWhyItIsNeverPublished()
    {
        var problem = PlanarLineFixtures.Fr4Line(6e-3, 5e9);
        var ports   = PlanarLineFixtures.EndPorts(problem);
        double[] freqs = [5e9];

        var raw = new PlanarKernel().Solve(
            problem, PlanarMeshSettings.Default, ports, freqs,
            PlanarSolveSettings.Default with { Deembed = false });
        var de = new PlanarKernel().Solve(
            problem, PlanarMeshSettings.Default, ports, freqs, PlanarSolveSettings.Default);

        var sRaw = raw.Solve.Points[0].S;
        var sDe  = de.Solve.Points[0].S;

        double rawS21Db = 20 * Math.Log10(sRaw[1, 0].Magnitude);
        double deS21Db  = 20 * Math.Log10(sDe[1, 0].Magnitude);
        output.WriteLine($"raw: S11 = {sRaw[0, 0]}, S21 = {rawS21Db:0.0} dB");
        output.WriteLine($"de-embedded: S11 = {sDe[0, 0]}, S21 = {deS21Db:0.0} dB");

        // TOTAL REFLECTION is the invariant, not a particular phase. On the reported file — 3.8 mm
        // at 100 MHz, where the sliver's jwC is negligible — S11 came back at +1 to five decimals.
        // Here the line is longer and the frequency 50x higher, so the same open has rotated in
        // phase; what does NOT change is that all of the power comes back.
        Assert.True(sRaw[0, 0].Magnitude > 0.97,
                    $"raw S11 should be a near-total reflection, was |{sRaw[0, 0]}| = " +
                    $"{sRaw[0, 0].Magnitude:0.####}");
        Assert.True(sDe[0, 0].Magnitude < 0.5,
                    $"the de-embedded port should be roughly matched, was {sDe[0, 0].Magnitude:0.###}");

        // …and essentially nothing gets through it, on a line that is almost lossless.
        Assert.True(rawS21Db < -30, $"raw S21 should be far down, was {rawS21Db:0.0} dB");
        Assert.True(deS21Db  > -3,  $"de-embedded S21 should be near 0 dB, was {deS21Db:0.0} dB");

        // The contrast is the finding: the raw path is not a few dB worse, it is a different answer.
        Assert.True(deS21Db - rawS21Db > 25,
                    $"raw and de-embedded should be worlds apart, got {deS21Db - rawS21Db:0.0} dB");
    }
}
