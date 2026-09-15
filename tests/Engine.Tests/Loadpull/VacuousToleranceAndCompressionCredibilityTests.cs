using System.Numerics;
using CircuitRF.Core.Design;
using CircuitRF.Core.Elaboration;
using CircuitRF.Core.Netlist;
using CircuitRF.Engine.HarmonicBalance;
using CircuitRF.Engine.Loadpull;
using Xunit;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Loadpull;

/// <summary>
/// The two guards that answer one reported failure: a loadpull_pursuit whose follow-on loadpull
/// reported all 44 terminations as "compressed" over a surface that was ONE constant value, so the
/// Data Display drew no contours (a constant field has no level crossings). See
/// src/Engine/Loadpull/RESOLVED.md.
///
/// The mechanism, in order:
///   1. The follow-on presents the pursuit's own Zsource, whose real part is ~1.5 Ω. Drive into a
///      high-impedance input scales as sqrt(8·Pavl·Re(Zs)), so the whole circuit runs far below the
///      directive's ABSOLUTE tolerance (Tol, in amperes).
///   2. HbNewton tests ‖F‖ &lt; tol before its first update, so every warm-started rung returned the
///      PREVIOUS rung's spectrum, flagged converged, at iteration 1.
///   3. Pout therefore never moved, gain fell exactly 1 dB per dB of Pin, and the 3 dB compression
///      stop tripped three rungs in — at every termination alike.
///
/// Guard 1 (HbNewton.IsToleranceVacuous) refuses step 2. Guard 2
/// (DriveLadder.IsCompressionCredible) refuses step 3 on its own account, so a Pout floor arriving
/// by any other route is not read as compression either.
/// </summary>
public class VacuousToleranceAndCompressionCredibilityTests(ITestOutputHelper output)
{
    // ── Guard 2: a gain drop against a Pout that never moved is not compression ──────────

    [Fact]
    public void CompressionCredible_RequiresPoutToHaveRisenSinceTheGainPeak()
    {
        // The reported failure's exact shape: Pout identical at the gain peak and 3 dB "later".
        Assert.False(DriveLadder.IsCompressionCredible(-180.83, -180.83));

        // Ordinary compression: Pin rose 6 dB for 3 dB of compression, so Pout rose 3 dB.
        Assert.True(DriveLadder.IsCompressionCredible(40.64, 43.64));

        // A gain drop with Pout FALLING is even less credible than a flat one.
        Assert.False(DriveLadder.IsCompressionCredible(40.0, 39.0));

        // No gain peak recorded yet is not evidence against the stop.
        Assert.True(DriveLadder.IsCompressionCredible(double.NaN, -180.83));

        // The threshold separates "did not move" from "moved", not "moved enough": a rise of twice
        // it is already credible, and half of it is not.
        Assert.True(DriveLadder.IsCompressionCredible(
            10.0, 10.0 + 2 * DriveLadder.MinPoutRiseForCompressionDb));
        Assert.False(DriveLadder.IsCompressionCredible(
            10.0, 10.0 + 0.5 * DriveLadder.MinPoutRiseForCompressionDb));
    }

    // ── Guard 1: a tolerance the RF excitation cannot reach is refused, not answered ─────

    [Fact]
    public void ToleranceVacuous_WhenTolIsNotSmallerThanTheRfExcitation()
    {
        // iSrc[0] is DC and is deliberately NOT counted: a large bias current must not hide a dead
        // RF drive. Only k = 1…K carries the tone.
        Complex[][] iSrc =
        [
            [new Complex(0.5, 0.0)],          // k=0: half an amp of bias
            [new Complex(3e-7, 0.0)],         // k=1: the whole RF drive
        ];

        Assert.Equal(3e-7, HbNewton.RfExcitationScale(iSrc, 1), 12);

        Assert.True(HbNewton.IsToleranceVacuous(iSrc, 1, 1e-6, out double scale));
        Assert.Equal(3e-7, scale, 12);

        // Tol below the excitation still constrains the answer — nothing is refused there.
        Assert.False(HbNewton.IsToleranceVacuous(iSrc, 1, 1e-9, out _));

        // No RF excitation at all (an S-param-mode stamp, no tone) is not this fault.
        Complex[][] noDrive = [[new Complex(0.5, 0.0)], [Complex.Zero]];
        Assert.False(HbNewton.IsToleranceVacuous(noDrive, 1, 1e-6, out _));
    }

    /// <summary>
    /// End to end on the shipped Hero 3B fixture, with no external model: a source impedance whose
    /// real part starves the input, driven at a tolerance meant for a milliamp-scale circuit. Before
    /// the guard this ladder stopped at the 4th rung claiming compression, 80 dB below PinMax, with
    /// Pout bit-identical at every rung. It must now refuse instead.
    /// </summary>
    [Fact]
    public void Ladder_RefusesVacuousTolerance_InsteadOfReportingFrozenPoutAsCompression()
    {
        var (lp, p) = Hero3B();

        // Re(Zs) = 1.5 Ω against a Tol of 1e-5 A: |Vs| = sqrt(8·Pavl·Re(Zs)) puts the whole RF
        // excitation four orders below the tolerance.
        p = p with { PinStartDbm = -50, PinStepDb = 1, PinMaxDbm = 0, Tol = 1e-5 };
        var ctx = lp.PrepareContext(p);
        ctx.SrcModel.SetHarmonicOverride(1, new Complex(1.5, 1188.0));

        var gpr = lp.RunOneTermination(p, ctx, new Complex(80, 10), 0, null);

        output.WriteLine($"Stop={gpr.StopReason}  steps={gpr.PinSteps.Count}");
        Assert.NotEqual("Compression", gpr.StopReason);
        Assert.Equal("NonConvergence", gpr.StopReason);

        // The refusal SAYS which two numbers are in the wrong order — the whole point of it, since
        // neither the tolerance nor the impedance looks wrong on its own.
        string? reason = gpr.PinSteps[^1].FailReason;
        output.WriteLine(reason ?? "(no reason)");
        Assert.NotNull(reason);
        Assert.Contains("not smaller than the RF excitation", reason);
    }

    /// <summary>
    /// The same fixture and the same starved source, at the fixture's own tolerance (1e-8 A, which
    /// the 2.9e-7 A excitation CAN reach), still runs — the guard must fire on the tolerance, not on
    /// the impedance.
    /// </summary>
    [Fact]
    public void Ladder_StillRunsWithTheSameStarvedSource_WhenTolIsSmallerThanTheExcitation()
    {
        var (lp, p) = Hero3B();
        p = p with { PinStartDbm = -50, PinStepDb = 1, PinMaxDbm = -40, Tol = 1e-8 };
        var ctx = lp.PrepareContext(p);
        ctx.SrcModel.SetHarmonicOverride(1, new Complex(1.5, 1188.0));

        var gpr = lp.RunOneTermination(p, ctx, new Complex(80, 10), 0, null);

        output.WriteLine($"Stop={gpr.StopReason}  converged={gpr.PinSteps.Count(s => s.Converged)}");
        Assert.Equal("PinMax", gpr.StopReason);
        Assert.All(gpr.PinSteps, s => Assert.True(s.Converged));

        // And it responds to drive — the property the frozen ladder did not have.
        var rungs = gpr.PinSteps.Where(s => !s.IsTickle).ToList();
        Assert.True(rungs[^1].PoutW > rungs[0].PoutW * 1.5,
            "Pout did not track drive; the ladder is frozen, not solved.");
    }

    private static (LoadpullEngine Lp, LoadpullAnalysisParams P) Hero3B()
    {
        var dir = AppContext.BaseDirectory;
        string? found = null;
        while (dir is not null)
        {
            var cand = Path.Combine(dir, "testdata", "Hero3B");
            if (Directory.Exists(cand)) { found = cand; break; }
            dir = Path.GetDirectoryName(dir);
        }
        if (found is null) throw new DirectoryNotFoundException("testdata/Hero3B not found");

        var (lib, tb) = CnlReader.ReadFile(Path.Combine(found, "hero3B_at_compression.cnl"));
        var netlist   = new Elaborator(lib).Elaborate(tb);
        var lpa       = tb.Analyses.OfType<LoadpullPursuitAnalysis>().First();
        var pp        = LoadpullPursuitEngine.Resolve(lpa, netlist.ResolvedGlobals,
                                                      netlist.GlobalsWithExplicitUnit);
        return (new LoadpullEngine(netlist, tb), pp.LpParams);
    }
}
