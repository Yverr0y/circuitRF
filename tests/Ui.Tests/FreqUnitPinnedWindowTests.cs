// ================================================================
//  FreqUnitPinnedWindowTests.cs
//
//  Owner-reported, 2026-09-14, against a real extraction: the per-point marker glyphs were missing,
//  and the only data visible sat at the bottom three frequencies of the sweep.
//
//  The point markers were not missing. The POINTS were off the picture. The display's frequency
//  unit had been changed from MHz to kHz with Autoscale X off, every trace's X moved by 1000× — and
//  the pinned window did not, so a plot framed 1…10000 MHz became 1…10000 kHz: the same numbers, a
//  thousandth of the span. Seven of the eleven points of a 1 MHz…2 GHz sweep went off the right
//  edge, silently. Measured on the reporter's own `.cdd`: 2 ellipses drawn on each rect plot (one
//  point), against 17 on the Smith plot beside it, whose axis is Γ and does not move.
//
//  A pinned window is a range of FREQUENCIES, not a range of numbers.
// ================================================================

using CircuitRF.Render.DataDisplay;
using RfCore;

namespace CircuitRF.Ui.Tests;

public sealed class FreqUnitPinnedWindowTests
{
    private static (Plot Plot, Trace Trace) Sweep(FreqUnit unit)
    {
        // The reporter's own sweep: 11 log-spaced points, 1 MHz to 2 GHz.
        var freqs = new double[11];
        for (int i = 0; i < freqs.Length; i++) freqs[i] = 1e6 * Math.Pow(2000.0, i / 10.0);

        var snp = new SNP(freqs, 2);
        for (int f = 0; f < freqs.Length; f++)
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                    snp.Matrices[f][i, j] = new System.Numerics.Complex(0.2 + 0.05 * f, -0.1);

        var plot = new Plot(PlotType.Rect, unit);
        var trace = new Trace(snp, MatrixType.S, 0, 0, DependentVarFormat.Db, false);
        trace.BuildPath(plot.PlotType, plot.FreqUnits);
        plot.Traces.Add(trace);
        plot.Autoscale(force: true);
        return (plot, trace);
    }

    /// <summary>
    /// The reported failure, end to end: a pinned window that framed every point still frames every
    /// point after the unit changes.
    /// </summary>
    [Fact]
    public void APinnedWindow_FollowsTheFrequencyUnit()
    {
        var (plot, trace) = Sweep(FreqUnit.MHz);
        plot.AutoscaleX = false;
        plot.Axes.Window = new PlotRect(1, -80, 9999, 85);      // 1 … 10 000 MHz
        Assert.Equal(11, PointsInWindow(plot, trace));

        plot.FreqUnits = FreqUnit.kHz;

        Assert.Equal(1e3,  plot.Axes.Window.Left,  6);          // 1 … 10 000 MHz, said in kHz
        Assert.Equal(1e7,  plot.Axes.Window.Right, 6);
        Assert.Equal(11, PointsInWindow(plot, trace));

        // And back, exactly — the ratio is the only thing applied.
        plot.FreqUnits = FreqUnit.MHz;
        Assert.Equal(1.0,   plot.Axes.Window.Left,  9);
        Assert.Equal(1e4,   plot.Axes.Window.Right, 9);
    }

    /// <summary>The secondary window shares the X range on a Rect plot, so it is rescaled with the
    /// primary or the two stop agreeing about where a frequency is.</summary>
    [Fact]
    public void TheSecondaryWindowIsRescaledToo()
    {
        var (plot, _) = Sweep(FreqUnit.MHz);
        plot.Axes.ShowSecondary = true;
        plot.AutoscaleX = false;
        plot.Axes.Window          = new PlotRect(1, -80, 9999, 85);
        plot.Axes.WindowSecondary = new PlotRect(1, -180, 9999, 360);

        plot.FreqUnits = FreqUnit.kHz;

        Assert.Equal(plot.Axes.Window.Left,  plot.Axes.WindowSecondary.Left,  6);
        Assert.Equal(plot.Axes.Window.Right, plot.Axes.WindowSecondary.Right, 6);
        Assert.Equal(plot.Axes.Window,          plot.Axes.WindowState);
        Assert.Equal(plot.Axes.WindowSecondary, plot.Axes.WindowSecondaryState);
    }

    /// <summary>An AUTOSCALED axis is reframed by the autoscale the setter already runs and must not
    /// be rescaled as well — doing both would apply the ratio twice.</summary>
    [Fact]
    public void AnAutoscaledAxisIsLeftToTheAutoscale()
    {
        var (plot, trace) = Sweep(FreqUnit.MHz);
        Assert.True(plot.AutoscaleX);

        plot.FreqUnits = FreqUnit.kHz;

        Assert.Equal(11, PointsInWindow(plot, trace));
        Assert.True(plot.Axes.Window.Right >= 2e6,    // 2 GHz in kHz
                    $"the autoscaled window stops at {plot.Axes.Window.Right} kHz");
    }

    /// <summary>
    /// The log axis takes the same treatment, and it is the one that shows the rescale is a RATIO:
    /// the window keeps its span in decades, and its enclosing-decade framing stays exact.
    /// </summary>
    [Fact]
    public void ALogWindowKeepsItsDecades()
    {
        var (plot, trace) = Sweep(FreqUnit.MHz);
        plot.SetXScale(AxisScale.Log);
        Assert.Equal(1.0, plot.Axes.Window.Left,  9);
        Assert.Equal(1e4, plot.Axes.Window.Right, 9);

        plot.AutoscaleX = false;
        double decades = Math.Log10(plot.Axes.Window.Right / plot.Axes.Window.Left);

        plot.FreqUnits = FreqUnit.kHz;

        Assert.Equal(decades, Math.Log10(plot.Axes.Window.Right / plot.Axes.Window.Left), 9);
        Assert.Equal(1e3, plot.Axes.Window.Left,  6);
        Assert.Equal(1e7, plot.Axes.Window.Right, 6);
        Assert.Equal(11, PointsInWindow(plot, trace));
    }

    /// <summary>
    /// A cube swept in something that is NOT a frequency does not move when the frequency unit
    /// changes, so its window must not either. <see cref="Trace.XIsFrequency"/> is written as the
    /// disjunction of the two tests the path builders apply, for exactly this reason.
    /// </summary>
    [Fact]
    public void ANonFrequencyXAxisIsNotRescaled()
    {
        var plot = new Plot(PlotType.Rect, FreqUnit.MHz);
        var snp  = new SNP([1e9], 2);
        var t    = new Trace(snp, MatrixType.S, 0, 0, DependentVarFormat.Db, false);
        t.CubeName = "Pout";           // what makes BuildPath take the CUBE path
        t.SetCubeData([0, 5, 10, 15, 20], null, [0.1, 0.5, 1.0, 1.4, 1.5],
                      "Pin", "dBm", PlotType.Rect, FreqUnit.MHz);
        plot.Traces.Add(t);
        Assert.False(t.XIsFrequency);
        Assert.False(plot.XAxisTracksFreqUnit);

        plot.AutoscaleX = false;
        plot.Axes.Window = new PlotRect(0, 0, 20, 2);
        double left = plot.Axes.Window.Left, right = plot.Axes.Window.Right;

        plot.FreqUnits = FreqUnit.kHz;

        // X only: the setter's own Autoscale still reframes Y, which is autoscaled and unrelated.
        Assert.Equal(left,  plot.Axes.Window.Left,  12);
        Assert.Equal(right, plot.Axes.Window.Right, 12);
    }

    private static int PointsInWindow(Plot plot, Trace trace)
    {
        var w = plot.Axes.Window;
        return trace.Points.Count(p => p.X >= w.Left && p.X <= w.Right);
    }
}
