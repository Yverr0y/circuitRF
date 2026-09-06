// ================================================================
//  PassiveReadoutTraceTests.cs — |Z|, ESR, X, C_eff, L_eff and Q as Data Display derived traces.
//
//  These are the numbers a capacitor, inductor or bead is specified and bought in, and the reason
//  they exist as traces is the question "does the model the vendor shipped agree with the file the
//  vendor shipped" — which nobody answers by reading S11 in dB.
//
//  Two things are gated here that the RfCore tests cannot reach:
//
//    * the FIXTURE travels with the trace, is persisted, and is forced to the only possible reading
//      on a 1-port source. Nothing in a Touchstone file records how the part was measured, and the
//      wrong reading is smooth and plausible (PassiveMetricsTests pins the closed form of that
//      error), so where the choice lives and whether it survives a save is the UI's half of it.
//    * the plotted path, the Table read and the marker read come from ONE array. The three had to
//      be able to disagree before the memoisation existed, and a metric added later is exactly the
//      thing that would reintroduce it.
// ================================================================

using System;
using System.Linq;
using System.Numerics;
using NumFlat;
using CircuitRF.Ui.DataDisplay;
using CircuitRF.Ui.DataDisplay.ViewModels;
using RfCore;
using RfCore.Data;
using Xunit;

namespace CircuitRF.Ui.Tests;

public sealed class PassiveReadoutTraceTests
{
    private const double Z0 = 50.0, Esr = 5e-3, Esl = 0.5e-9, Cap = 100e-9;

    private static double SrfHz => 1.0 / (2.0 * Math.PI * Math.Sqrt(Esl * Cap));

    private static Complex Z(double f)
    {
        double w = 2.0 * Math.PI * f;
        return new Complex(Esr, w * Esl - 1.0 / (w * Cap));
    }

    private static double[] Sweep(int n = 601, double lo = 1e6, double hi = 1e9) =>
        [.. Enumerable.Range(0, n).Select(i => lo * Math.Pow(hi / lo, i / (double)(n - 1)))];

    /// <summary>A shunt-through MLCC — the shape of a vendor decoupling-capacitor file.</summary>
    private static SNP Mlcc(int n = 601)
    {
        var f = Sweep(n);
        var mats = f.Select(fi =>
        {
            var z   = Z(fi);
            var s21 = 2.0 * z / (2.0 * z + Z0);
            var s11 = -Z0 / (2.0 * z + Z0);
            var m = new Mat<Complex>(2, 2);
            m[0, 0] = s11; m[0, 1] = s21; m[1, 0] = s21; m[1, 1] = s11;
            return m;
        }).ToArray();
        return new SNP(f, mats, MatrixType.S, MatrixFormat.RI, new Complex(Z0, 0));
    }

    /// <summary>The same part as a 1-port reflection measurement.</summary>
    private static SNP MlccOnePort(int n = 601)
    {
        var f = Sweep(n);
        var mats = f.Select(fi =>
        {
            var m = new Mat<Complex>(1, 1);
            m[0, 0] = (Z(fi) - Z0) / (Z(fi) + Z0);
            return m;
        }).ToArray();
        return new SNP(f, mats, MatrixType.S, MatrixFormat.RI, new Complex(Z0, 0));
    }

    private static Trace T(SNP snp, DerivedParameters d,
                           PassiveExtraction fixture = PassiveExtraction.ShuntThrough)
        => new(snp, MatrixType.S, 0, 0, DependentVarFormat.Mag)
        {
            Derived = d, InputPort = 1, OutputPort = Math.Min(2, Math.Max(1, snp.Ports)),
            PassiveExtraction = fixture,
        };

    private static readonly DerivedParameters[] All =
    [
        DerivedParameters.MagZ, DerivedParameters.Esr, DerivedParameters.Reactance,
        DerivedParameters.Ceff, DerivedParameters.Leff, DerivedParameters.QFactor,
    ];

    // ── kind and gating ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheyAreScalarsVersusFrequency_NotCircleLoci()
    {
        foreach (var d in All)
        {
            Assert.True(d.IsPassiveMetric(),      $"{d} should be a passive metric");
            Assert.True(d.IsScalarVsFrequency(),  $"{d} should be a scalar vs frequency");
            Assert.False(d.IsCircleLocus(),       $"{d} is not a Γ-plane locus");
            Assert.False(d.IsSweepDerivative(),   $"{d} is a point function of Z, not a derivative");
        }
    }

    /// <summary>
    /// <b>They have no <c>NetworkMetric</c> member, and the port-pair question is not theirs to
    /// answer.</b> Whether an ordered pair is needed depends on the FIXTURE — a through reading uses
    /// S21, a 1-port reading uses S11 — so it cannot be a property of the metric the way it is for
    /// μ or K. Group delay set the precedent for a derived quantity that routes past
    /// <c>ToNetworkMetric</c>; these are the second family to do it.
    /// </summary>
    [Fact]
    public void TheyRoutePastToNetworkMetric_AndDeferThePortPairToTheFixture()
    {
        foreach (var d in All)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => d.ToNetworkMetric());
            Assert.False(d.NeedsPortPair(), $"{d}'s port-pair need belongs to the fixture, not the metric");
        }

        Assert.True(PassiveMetrics.NeedsPortPair(PassiveExtraction.ShuntThrough));
        Assert.False(PassiveMetrics.NeedsPortPair(PassiveExtraction.OnePort));
    }

    [Theory]
    [InlineData(PlotType.Rect, true)]
    [InlineData(PlotType.Table, true)]
    [InlineData(PlotType.Smith, false)]
    [InlineData(PlotType.Polar, false)]
    public void TheyAreOfferedOnRectAndTableOnly(PlotType plot, bool expected)
    {
        foreach (var d in All)
        {
            var item = new TraceDataItem(null!, d, plot, omitFilePrefix: true);
            Assert.Equal(expected, item.IsEnabled);
            if (!expected)
                Assert.Contains("rectangular", item.DisabledReason!, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Each label carries its unit. An axis running from 1e−9 to 1e−3 with no unit on it is not
    /// something to make anyone read, and unlike group delay there is no single scale worth
    /// applying — the capacitors in one plot can span picofarads to millifarads.
    /// </summary>
    [Fact]
    public void EveryLabelNamesItsUnit()
    {
        Assert.Contains("Ω", DerivedParameters.MagZ.Description(),      StringComparison.Ordinal);
        Assert.Contains("Ω", DerivedParameters.Esr.Description(),       StringComparison.Ordinal);
        Assert.Contains("Ω", DerivedParameters.Reactance.Description(), StringComparison.Ordinal);
        Assert.Contains("F", DerivedParameters.Ceff.Description(),      StringComparison.Ordinal);
        Assert.Contains("H", DerivedParameters.Leff.Description(),      StringComparison.Ordinal);

        foreach (var d in All)
            Assert.Equal(d.Description(),
                         new TraceDataItem(null!, d, PlotType.Rect, omitFilePrefix: true).Label);
    }

    // ── the numbers ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ARectTracePlotsTheComponentItWasBuiltFrom()
    {
        var snp = Mlcc();

        var esr = T(snp, DerivedParameters.Esr);
        esr.BuildPath(PlotType.Rect, FreqUnit.GHz);
        Assert.NotEmpty(esr.Points);
        Assert.All(esr.Points, p => Assert.Equal(Esr, p.Y, 9));

        // A decade below resonance the part is its capacitance; a decade above, its mounting
        // inductance. Both come out of the SAME trace machinery, on the same file.
        var ceff = T(snp, DerivedParameters.Ceff);
        ceff.BuildPath(PlotType.Rect, FreqUnit.GHz);
        Assert.InRange(ceff.DataPointScalar(Nearest(snp, SrfHz / 10.0)), Cap * 0.99, Cap * 1.02);

        var leff = T(snp, DerivedParameters.Leff);
        leff.BuildPath(PlotType.Rect, FreqUnit.GHz);
        Assert.InRange(leff.DataPointScalar(Nearest(snp, SrfHz * 10.0)), Esl * 0.98, Esl * 1.01);
    }

    /// <summary>
    /// Where a metric has no value the point is DROPPED, not clamped or folded. C_eff above the
    /// self-resonance is the routine case: the part is inductive and its capacitance has no answer,
    /// so the trace ends rather than plotting a negative number on a log axis.
    /// </summary>
    [Fact]
    public void UndefinedPointsLeaveTheTrace_TheyDoNotBecomeNumbers()
    {
        var snp = Mlcc();
        var ceff = T(snp, DerivedParameters.Ceff);
        ceff.BuildPath(PlotType.Rect, FreqUnit.GHz);

        int capacitive = snp.Frequencies.Count(f => f < SrfHz);
        Assert.Equal(capacitive, ceff.Points.Count);
        Assert.All(ceff.Points, p => Assert.True(p.Y > 0f, "a plotted capacitance is positive"));

        Assert.True(double.IsNaN(ceff.DataPointScalar(Nearest(snp, SrfHz * 10.0))));
    }

    /// <summary>The Table/marker read and the plotted path come from one array.</summary>
    [Fact]
    public void TheScalarReadoutAgreesWithThePlottedPath()
    {
        var snp = Mlcc();
        var t = T(snp, DerivedParameters.MagZ);
        t.BuildPath(PlotType.Rect, FreqUnit.GHz);

        foreach (int i in new[] { 0, 100, 300 })
            Assert.Equal(Z(snp.Frequencies[i]).Magnitude, t.DataPointScalar(snp.Frequencies[i]), 9);
    }

    // ── the fixture ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Changing the fixture changes the answer, and the memoised array must follow it. The cache
    /// key is the thing under test: a switch that plotted the previous fixture's numbers would look
    /// exactly like a correct plot.
    /// </summary>
    [Fact]
    public void SwitchingTheFixtureChangesTheTrace_AndTheCacheFollows()
    {
        var snp = Mlcc();
        var t = T(snp, DerivedParameters.MagZ);
        t.BuildPath(PlotType.Rect, FreqUnit.GHz);

        double f  = snp.Frequencies[100];
        double shunt = t.DataPointScalar(f);
        Assert.Equal(Z(f).Magnitude, shunt, 9);

        t.PassiveExtraction = PassiveExtraction.OnePort;
        t.BuildPath(PlotType.Rect, FreqUnit.GHz);
        double onePort = t.DataPointScalar(f);

        // Z ∥ Z0 — the exact misread, which is what makes it plausible.
        var parallel = Z(f) * Z0 / (Z(f) + Z0);
        Assert.Equal(parallel.Magnitude, onePort, 9);
        Assert.NotEqual(shunt, onePort, 9);
    }

    /// <summary>
    /// A source with one port can only be read as a 1-port, whatever is stored. The stored
    /// preference SURVIVES, so pointing the same plot at a 2-port file later restores it — the
    /// alternative, correcting it at load time, silently loses a user's choice.
    /// </summary>
    [Fact]
    public void AOnePortSourceForcesTheOnePortReadingWithoutDiscardingThePreference()
    {
        var t = T(MlccOnePort(), DerivedParameters.Esr);
        Assert.Equal(PassiveExtraction.ShuntThrough, t.PassiveExtraction);
        Assert.Equal(PassiveExtraction.OnePort,      t.EffectivePassiveExtraction);

        t.BuildPath(PlotType.Rect, FreqUnit.GHz);
        Assert.NotEmpty(t.Points);
        Assert.All(t.Points, p => Assert.Equal(Esr, p.Y, 9));
    }

    /// <summary>
    /// The 1-port reading of a genuine 1-port file and the shunt-through reading of the same part's
    /// 2-port file give the SAME component — which is the point of choosing the fixture rather than
    /// a coincidence of two similar formulas.
    /// </summary>
    [Fact]
    public void TheRightFixtureOnEitherFileGivesTheSamePart()
    {
        var two = T(Mlcc(),         DerivedParameters.Esr);
        var one = T(MlccOnePort(),  DerivedParameters.Esr);
        two.BuildPath(PlotType.Rect, FreqUnit.GHz);
        one.BuildPath(PlotType.Rect, FreqUnit.GHz);

        Assert.Equal(two.Points.Count, one.Points.Count);
        for (int i = 0; i < two.Points.Count; i++)
            Assert.Equal(two.Points[i].Y, one.Points[i].Y, 6);
    }

    // ── reading the number ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>The bug this fixes: a Table of C effective read "0.000" in every cell.</b> The display's
    /// default number format is Fixed with three fraction digits, which is right for a decibel and
    /// destroys a capacitance — 100 pF is 1e-10 F. Selecting a passive readout now selects
    /// engineering notation with it, beside the Y-axis format that is chosen the same way.
    /// </summary>
    [Fact]
    public void APassiveReadoutSelectsEngineeringNotation_SoATableCanShowPicofarads()
    {
        var t = T(Mlcc(), DerivedParameters.Ceff);
        Assert.Equal(PrecisionFormat.S, t.FormatString);

        // What the Table renderer would print, through the renderer's own entry point.
        var snp = Mlcc();
        var ceff = T(snp, DerivedParameters.Ceff);
        ceff.BuildPath(PlotType.Table, FreqUnit.GHz);

        string cell = TableRenderer.FormatTraceCell(ceff, Nearest(snp, SrfHz / 10.0));
        Assert.EndsWith(" n", cell);                                   // 100 nF, not "0.000"
        Assert.DoesNotContain("0.000", cell, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every OTHER derived metric keeps the format it had. The engineering default is per-metric
    /// precisely so that no existing plot or table moves.
    /// </summary>
    [Theory]
    [InlineData(DerivedParameters.Mu)]
    [InlineData(DerivedParameters.MaxGain)]
    [InlineData(DerivedParameters.Passivity)]
    [InlineData(DerivedParameters.GroupDelay)]
    public void ANonPassiveMetricKeepsTheDefaultFormat(DerivedParameters d)
        => Assert.Equal(PrecisionFormat.F, T(Mlcc(), d).FormatString);

    /// <summary>
    /// A picofarad-scale part is the case the axis itself used to destroy: the near-zero snap was an
    /// absolute 1e-12, so every gridline of such an axis was labelled "0". Gated at the trace level
    /// here — the formatter's own gate is <c>EngineeringFormatTests</c>.
    /// </summary>
    [Fact]
    public void APicofaradTraceProducesTicksThatAreNotAllZero()
    {
        const double CSmall = 10e-12;
        var f = Sweep(401, 1e6, 1e8);
        var mats = f.Select(fi =>
        {
            double w = 2.0 * Math.PI * fi;
            var z   = new Complex(0.05, w * Esl - 1.0 / (w * CSmall));
            var s21 = 2.0 * z / (2.0 * z + Z0);
            var s11 = -Z0 / (2.0 * z + Z0);
            var m = new Mat<Complex>(2, 2);
            m[0, 0] = s11; m[0, 1] = s21; m[1, 0] = s21; m[1, 1] = s11;
            return m;
        }).ToArray();
        var snp = new SNP(f, mats, MatrixType.S, MatrixFormat.RI, new Complex(Z0, 0));

        var t = T(snp, DerivedParameters.Ceff);
        t.BuildPath(PlotType.Rect, FreqUnit.GHz);
        Assert.NotEmpty(t.Points);

        // The window the autoscale would frame, and the label its ticks would carry.
        double lo = t.Points.Min(p => p.Y), hi = t.Points.Max(p => p.Y);
        int group  = EngineeringFormat.GroupFor(EngineeringFormat.AxisMagnitude(lo, hi));
        double tick = Math.Abs(hi - lo) > 0 ? Math.Abs(hi - lo) / 5.0 : Math.Abs(hi) / 5.0;

        string label = EngineeringFormat.Tick(EngineeringFormat.SnapNearZero(hi, tick), group, 3);
        Assert.NotEqual("0", label);
        Assert.EndsWith("p", label);
    }

    /// <summary>
    /// <b>The autoscale half of the reported bug, and the larger half.</b> A 100 nF part's C_eff
    /// varies by about 2 % across its band — a height of 2e-9 farads — which the old degeneracy
    /// test (<c>height &lt; 1e-6</c>, absolute) classified as no extent at all. The window was then
    /// replaced with a fixed half-unit: an axis from −0.25 to +0.25 FARADS with the trace flat
    /// through the middle. No tick formatting could have rescued that.
    /// </summary>
    [Fact]
    public void AutoscaleFramesACapacitanceTraceAroundItsOwnValue()
    {
        var snp  = Mlcc();
        var plot = new Plot(PlotType.Rect, FreqUnit.GHz);
        var t    = T(snp, DerivedParameters.Ceff);
        plot.Traces.Add(t);
        t.BuildPath(PlotType.Rect, FreqUnit.GHz);
        plot.Autoscale();

        var w = plot.Axes.Window;

        // C_eff genuinely runs to infinity at the self-resonance — it divides by a reactance
        // passing through zero — so this trace really does span two decades and the window is
        // framed on all of it. That excursion is expected and is left visible.
        //
        // What is asserted is the bug: the window must be of the DATA's order, not the fixed
        // half-farad box the absolute degeneracy test used to substitute. Before the fix this was
        // 0.5 F wide with the whole trace inside one pixel row.
        Assert.True(w.Height < 1e-3, $"window height {w.Height} F is still an absolute-scale box");
        Assert.True(w.Y > -1e-3 && w.Y < Cap,
            $"window starts at {w.Y} F, which does not sit near a {Cap} F capacitance");
        Assert.True(w.Bottom > Cap, "the window must contain the capacitive plateau");
    }

    /// <summary>
    /// A genuinely FLAT trace of an ordinary quantity still frames exactly as it always did. The
    /// relative rule is scoped to values outside the range plain notation can write, so no existing
    /// plot moves.
    /// </summary>
    [Fact]
    public void AFlatOrdinaryTraceStillGetsTheHalfUnitWindow()
    {
        var f = Sweep(51);
        var mats = f.Select(_ =>
        {
            var m = new Mat<Complex>(2, 2);
            m[0, 0] = 0.5; m[0, 1] = 0.5; m[1, 0] = 0.5; m[1, 1] = 0.5;
            return m;
        }).ToArray();
        var snp = new SNP(f, mats, MatrixType.S, MatrixFormat.RI, new Complex(Z0, 0));

        var plot = new Plot(PlotType.Rect, FreqUnit.GHz);
        var t = new Trace(snp, MatrixType.S, 0, 0, DependentVarFormat.Mag);
        plot.Traces.Add(t);
        t.BuildPath(PlotType.Rect, FreqUnit.GHz);
        plot.Autoscale();

        // 0.5 of extent, plus the 10 % rectangular padding autoscale applies to every window.
        Assert.Equal(0.5 * 1.2, plot.Axes.Window.Height, 6);
    }

    // ── persistence ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The fixture survives a save. It is a per-TRACE choice about the physical measurement, not a
    /// view setting, so a `.cdd` reopened tomorrow that quietly reverted to the default would show
    /// a different part with no edit by the user — the one failure mode an explicit fixture exists
    /// to remove.
    /// </summary>
    [Theory]
    [InlineData(PassiveExtraction.ShuntThrough)]
    [InlineData(PassiveExtraction.SeriesThrough)]
    [InlineData(PassiveExtraction.OnePort)]
    public void TheFixtureSurvivesACddRoundTrip(PassiveExtraction fixture)
    {
        var trace = T(Mlcc(41), DerivedParameters.Ceff, fixture);

        var cfg  = DataDisplayViewModel.BuildTraceConfig(trace, configDir: "");
        Assert.Equal(fixture, cfg.PassiveExtraction);

        var opts = new System.Text.Json.JsonSerializerOptions();
        var back = System.Text.Json.JsonSerializer.Deserialize<TraceConfig>(
            System.Text.Json.JsonSerializer.Serialize(cfg, opts), opts)!;

        Assert.Equal(fixture, back.PassiveExtraction);
        Assert.Equal(DerivedParameters.Ceff, back.Derived);
    }

    /// <summary>
    /// A `.cdd` written before the fixture existed carries no field for it, and the reading it
    /// falls back to has to be the one a 2-port vendor part file wants — which is shunt-through,
    /// the only fixture with usable dynamic range on a part that is milliohms at resonance.
    /// </summary>
    [Fact]
    public void ACddWrittenBeforeTheFixtureExistedLoadsAsShuntThrough()
    {
        var back = System.Text.Json.JsonSerializer.Deserialize<TraceConfig>(
            """{"Derived":"Esr","InputPort":1,"OutputPort":2}""")!;
        Assert.Equal(PassiveExtraction.ShuntThrough, back.PassiveExtraction);
    }

    private static double Nearest(SNP snp, double target) =>
        snp.Frequencies.OrderBy(f => Math.Abs(f - target)).First();
}
