using System;
using System.Globalization;
using System.Numerics;
using CircuitRF.Render.DataDisplay;
using CircuitRF.Ui.DataDisplay;
using CircuitRF.Ui.DataDisplay.ViewModels;
using RfCore;
using Xunit;

namespace CircuitRF.Ui.Tests.DataDisplay;

/// <summary>
/// Owner report, 2026-09-08, two halves of one complaint: a Polar plot could not be scaled BELOW the
/// unit circle — not by autoscale, and not by typing limits into the Axes Limits flyout.
///
/// <para><b>A Smith chart's grid IS the unit disc</b>, so its floor at unity is correct and stays.
/// A Polar plot carries whatever the trace is — an impedance in ohms, an admittance in siemens, a
/// loop gain — and the floor made every small one a dot on the origin, indistinguishable from a
/// trace that is not being drawn. That is exactly how the WSProbe chapter's <c>1/H0</c> locus
/// (tens of millisiemens) read beside <c>1/Y0</c> (tens of ohms).</para>
///
/// <para><b>The flyout's own failure was separate and sharper.</b> Its fields apply on every
/// keystroke and the two ends of one axis are read together, so refreshing BOTH boxes after an edit
/// wrote the resulting square back over the end the user had not typed yet — Min went in, Max was
/// re-written to the old outer limit, the square came back the size it already was, and Min was
/// overwritten with it. The window could never be made smaller, with no error anywhere.</para>
/// </summary>
public class PolarScaleTests
{
    /// <summary>One trace whose locus is well inside the unit circle: |z| ~ 0.05.</summary>
    private static Trace SmallLocus()
    {
        var t = new Trace(new SNP([1e9, 2e9], 2), MatrixType.S, 0, 0, DependentVarFormat.Complex)
        {
            CubeName = "SP1.wsp",
            Slice    = [new AxisSlice("freq", AxisRole.KeepAsX, 0)],
        };
        t.SetCubeData(
            [1e9, 2e9],
            [new Complex(0.05, -0.02), new Complex(0.06, 0.03)],
            null, "freq", "Hz", PlotType.Polar, FreqUnit.GHz);
        return t;
    }

    // ── Autoscale ────────────────────────────────────────────────────────────

    [Fact]
    public void PolarAutoscale_FramesALocusSmallerThanUnity()
    {
        var plot = new Plot(PlotType.Polar, FreqUnit.GHz);
        plot.Traces.Add(SmallLocus());

        plot.Autoscale(force: true);

        var w = plot.Axes.Window;
        Assert.Equal(w.Width, w.Height, 6);              // still square, still centred on the origin
        Assert.Equal(0.0, w.X + w.Width / 2, 6);
        Assert.True(w.Width < 2.0,
            $"Polar autoscale must be able to go inside the unit circle; window width was {w.Width}.");
        Assert.True(w.Width >= 0.12,                     // the locus itself spans ~0.06 either side
            $"…but it must still contain the locus; window width was {w.Width}.");
    }

    [Fact]
    public void SmithAutoscale_StillNeverGoesInsideTheUnitCircle()
    {
        var plot = new Plot(PlotType.Smith, FreqUnit.GHz);
        plot.Traces.Add(SmallLocus());

        plot.Autoscale(force: true);

        var w = plot.Axes.Window;
        Assert.Equal(2.0, w.Width,  6);
        Assert.Equal(2.0, w.Height, 6);
    }

    // ── The Axes Limits flyout ───────────────────────────────────────────────

    [Theory]
    [InlineData(PlotType.Polar)]
    [InlineData(PlotType.Smith)]
    public void ManualLimits_CanShrinkTheWindowBelowUnity(PlotType type)
    {
        var plot = new Plot(type, FreqUnit.GHz);
        var vm   = new AxesLimitsViewModel(plot, () => { });
        vm.XAutoscale = false;

        vm.XMinText = "-0.1";
        vm.XMaxText = "0.1";

        var w = plot.Axes.Window;
        Assert.Equal(0.2, w.Width,  6);
        Assert.Equal(0.2, w.Height, 6);                  // Y adapts to keep it square
        Assert.Equal(-0.1, w.X, 6);
        Assert.Equal(-0.1, w.Y, 6);

        // The coupled axis is what refreshes; the edited one keeps what the user typed.
        Assert.Equal(-0.1, double.Parse(vm.YMinText, CultureInfo.InvariantCulture), 6);
        Assert.Equal( 0.1, double.Parse(vm.YMaxText, CultureInfo.InvariantCulture), 6);
        Assert.Equal("-0.1", vm.XMinText);
        Assert.Equal("0.1",  vm.XMaxText);
    }

    [Fact]
    public void ManualLimits_AYEditShrinksItTooAndCouplesX()
    {
        var plot = new Plot(PlotType.Polar, FreqUnit.GHz);
        var vm   = new AxesLimitsViewModel(plot, () => { });
        vm.YAutoscale = false;

        vm.YMinText = "-0.25";
        vm.YMaxText = "0.25";

        var w = plot.Axes.Window;
        Assert.Equal(0.5, w.Width,  6);
        Assert.Equal(0.5, w.Height, 6);
        Assert.Equal(-0.25, double.Parse(vm.XMinText, CultureInfo.InvariantCulture), 6);
    }
}
