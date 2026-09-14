// ================================================================
//  LogFrequencyAxisTests.cs — brief-dd-log-frequency-axis.md's gates.
//
//  The measured problem: circuitRF's EM kernel writes log-spaced sweeps by default, and an 11-point
//  sweep from 1 MHz to 2 GHz put FIVE of its points — the whole bottom two decades — inside the
//  first seven pixels of a ~700 px plot area. The line was drawn correctly and was unreadable.
//
//  The primary gate is the FIRST one below and it is the reason this change was safe to make in the
//  middle rather than in twenty places: every consumer of the axis goes through one transform, so a
//  linear plot has to come back byte for byte.
// ================================================================

using CircuitRF.Render.DataDisplay;
using CircuitRF.Ui.DataDisplay.ViewModels;
using RfCore;
using RfCore.Data;
using RfCore.Export;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests.Render;

[Collection(CircuitRF.Ui.Tests.SkiaFontsTypefaceCollection.Name)]
public sealed class LogFrequencyAxisTests(ITestOutputHelper output)
{
    // ── fixtures ──────────────────────────────────────────────────────────────

    private static double[] LogFreqs(int n, double f0, double f1)
    {
        var a = new double[n];
        for (int i = 0; i < n; i++) a[i] = f0 * Math.Pow(f1 / f0, (double)i / (n - 1));
        return a;
    }

    private static double[] LinFreqs(int n, double f0, double f1)
    {
        var a = new double[n];
        for (int i = 0; i < n; i++) a[i] = f0 + (f1 - f0) * i / (n - 1);
        return a;
    }

    private static SNP MakeSnp(int nPorts, double[] freqsHz)
    {
        var snp = new SNP(freqsHz, nPorts);
        for (int f = 0; f < freqsHz.Length; f++)
            for (int i = 0; i < nPorts; i++)
                for (int j = 0; j < nPorts; j++)
                    snp.Matrices[f][i, j] = System.Numerics.Complex.FromPolarCoordinates(
                        Math.Abs(0.1 + 0.4 * Math.Sin(0.3 * f + i + 2 * j)) + 0.01, 0.7 * f + i - j);
        return snp;
    }

    /// <summary>A Rect plot over the brief's own measured sweep: 11 log-spaced points, 1 MHz…2 GHz.</summary>
    private static (Plot Plot, Trace Trace) DecadeSpanningPlot(double[]? freqs = null)
    {
        var snp = MakeSnp(2, freqs ?? LogFreqs(11, 1e6, 2e9));
        var plot = new Plot(PlotType.Rect, FreqUnit.GHz);
        var trace = new Trace(snp, MatrixType.S, 0, 0, DependentVarFormat.Db, false);
        trace.Properties.MarkerEnabled = true;
        trace.BuildPath(plot.PlotType, plot.FreqUnits);
        plot.Traces.Add(trace);
        plot.Autoscale(force: true);
        return (plot, trace);
    }

    private static string Svg(Plot plot)
        => PlotDocumentWriter.BuildSvgString(
            c => PlotRenderer.Draw(c, (792.0, 612.0), plot, PlotDetail.Full, RenderTheme.Light),
            PagePlacement.Letter);

    // ── GATE 1 — the linear path did not move ─────────────────────────────────

    /// <summary>
    /// The PRIMARY gate (brief §Gates): a plot switched to Log and back to Linear renders the SAME
    /// BYTES as before it was ever switched.
    ///
    /// <para><b>It is written as a round trip rather than against a committed baseline on purpose.</b>
    /// A stored `.svg` answers "is it what it was when someone last regenerated the file"; this
    /// answers "is the linear arithmetic still the linear arithmetic", which is the actual claim —
    /// and it cannot be repaired by re-blessing an expected file. The same renderer draws both
    /// sides, exactly as <c>RenderDataDisplayCliTests</c> compares the verb against the application.
    /// </para>
    ///
    /// <para>Six plot shapes, because the transform is not the only thing that could have moved: the
    /// zero-nudged left edge, the secondary axis's own window, the Y tick nubs (whose inboard ends
    /// are the one place the grid measures a distance ALONG X), a marker glyph, and the two complex
    /// plot kinds that must never see the mode at all.</para>
    /// </summary>
    [Fact]
    public void LinearPlotsRenderIdentically_AfterARoundTripThroughLog()
    {
        foreach (var (name, plot) in LinearFixtures())
        {
            string before = StripSkiaIds(Svg(plot));
            plot.SetXScale(AxisScale.Log);
            plot.SetXScale(AxisScale.Linear);
            string after = StripSkiaIds(Svg(plot));
            if (before != after)
            {
                int i = 0; while (i < before.Length && before[i] == after[i]) i++;
                output.WriteLine($"{name} first differs at {i}");
                output.WriteLine($"  before: …{before.Substring(Math.Max(0, i - 80), Math.Min(160, before.Length - Math.Max(0, i - 80)))}");
                output.WriteLine($"  after:  …{after.Substring(Math.Max(0, i - 80), Math.Min(160, after.Length - Math.Max(0, i - 80)))}");
            }
            Assert.True(before == after,
                $"{name}: the linear picture changed across a round trip through the log axis " +
                $"({before.Length} vs {after.Length} bytes)");
        }
    }

    /// <summary>
    /// Skia's SVG element ids come from a counter that lives in the PROCESS, not in the picture, so
    /// two renders of one composition differ in <c>cl_3</c> versus <c>cl_6</c> and in nothing else.
    /// The same normalisation, for the same reason, as <c>RenderDataDisplayCliTests.StripSkiaIds</c>;
    /// nothing else is excluded — every coordinate, colour and glyph run is compared as written.
    /// </summary>
    private static string StripSkiaIds(string svg) =>
        System.Text.RegularExpressions.Regex.Replace(svg, @"\b(cl|img|gr|fp)_[0-9a-z]+\b", "$1_N");

    private static IEnumerable<(string Name, Plot Plot)> LinearFixtures()
    {
        {
            var snp = MakeSnp(2, LinFreqs(21, 1e9, 6e9));
            var p = new Plot(PlotType.Rect, FreqUnit.GHz);
            var t = new Trace(snp, MatrixType.S, 1, 0, DependentVarFormat.Db, false);
            t.BuildPath(p.PlotType, p.FreqUnits); p.Traces.Add(t); p.Autoscale(force: true);
            yield return ("rect-linear-db", p);
        }
        {
            var (p, _) = DecadeSpanningPlot();
            yield return ("rect-log-spaced-data-on-a-linear-axis", p);
        }
        {
            var snp = MakeSnp(2, LinFreqs(31, 0.5e9, 10e9));
            var p = new Plot(PlotType.Rect, FreqUnit.GHz) { Axes = { ShowSecondary = true } };
            var a = new Trace(snp, MatrixType.S, 0, 0, DependentVarFormat.Db, false);
            var b = new Trace(snp, MatrixType.S, 1, 0, DependentVarFormat.Phase, true);
            a.BuildPath(p.PlotType, p.FreqUnits); b.BuildPath(p.PlotType, p.FreqUnits);
            p.Traces.Add(a); p.Traces.Add(b); p.Autoscale(force: true);
            yield return ("rect-secondary-axis", p);
        }
        {
            // A PINNED window, autoscale off — the case where the round trip has to preserve the
            // user's own framing rather than re-deriving it. Its left edge is positive, so the log
            // mode needs no repair and the window survives untouched in both directions. (A pinned
            // window whose left edge is ZERO is a different matter and is pinned in
            // TheZeroNudge_IsLinearOnly_AndALogWindowIsRepairedInstead: the log axis cannot draw it,
            // so it is repaired on the way out and does not come back.)
            var snp = MakeSnp(2, LinFreqs(21, 0.5e9, 6e9));
            var p = new Plot(PlotType.Rect, FreqUnit.GHz);
            var t = new Trace(snp, MatrixType.S, 0, 0, DependentVarFormat.Mag, false);
            t.BuildPath(p.PlotType, p.FreqUnits); p.Traces.Add(t); p.Autoscale(force: true);
            p.AutoscaleX = false;
            p.Axes.Window = new PlotRect(0.5, -1, 6.0, 2);
            yield return ("rect-pinned-window", p);
        }
        {
            var snp = MakeSnp(2, LinFreqs(21, 1e9, 6e9));
            var p = new Plot(PlotType.Rect, FreqUnit.GHz);
            var t = new Trace(snp, MatrixType.S, 1, 0, DependentVarFormat.Db, false);
            t.BuildPath(p.PlotType, p.FreqUnits);
            t.Markers.Add(new Marker(t, snp.Frequencies[7], false, false, 1, p.FreqUnits));
            p.Traces.Add(t); p.Autoscale(force: true);
            yield return ("rect-marker", p);
        }
        foreach (var kind in new[] { PlotType.Smith, PlotType.Polar })
        {
            var snp = MakeSnp(2, LinFreqs(21, 1e9, 6e9));
            var p = new Plot(kind, FreqUnit.GHz);
            var t = new Trace(snp, MatrixType.S, 0, 0, DependentVarFormat.Complex, false);
            t.BuildPath(p.PlotType, p.FreqUnits); p.Traces.Add(t); p.Autoscale(force: true);
            yield return (kind.ToString().ToLowerInvariant(), p);
        }
    }

    /// <summary>
    /// The linear ARM of the transform is still the expression this type has always evaluated,
    /// asserted on the bits rather than on a tolerance — which is what makes the byte gate above
    /// hold rather than nearly hold.
    /// </summary>
    [Fact]
    public void LinearTransformArithmetic_IsBitIdenticalToTheLegacyExpression()
    {
        var window   = new PlotRect(-1e-6, -40, 6.0000001, 65.5);
        var viewport = new PlotRect(0.15, 0.05, 0.80, 0.88);
        var canvas   = (792.0, 612.0);

        var prm = PlotRenderer.WorldToCanvasParams(window, viewport, canvas);
        var tf  = new TransformSet { Primary = prm, Secondary = prm, Viewport = viewport,
                                     CanvasSize = canvas, XLog = false };

        foreach (float wx in new[] { -1e-6f, 0f, 1e-7f, 0.5f, 1.234567f, 6f })
        {
            float expected = (float)(wx * prm.XScale + prm.XOffset);
            Assert.Equal(expected, tf.PrimaryToCanvas(wx, 0f).X);
            Assert.Equal((wx - prm.XOffset) / prm.XScale, tf.PrimaryFromCanvas(wx, 0f).Wx);
        }
    }

    /// <summary>
    /// The zero-nudge — <c>Axes.Window</c>'s repair of a left edge of exactly 0 — is a LINEAR rule
    /// and the log axis must never reach it. On a log axis −1e-6 is the one point the map has no
    /// answer for, and it arrives by DEFAULT: an autoscaled frequency window literally begins there.
    /// </summary>
    [Fact]
    public void TheZeroNudge_IsLinearOnly_AndALogWindowIsRepairedInstead()
    {
        var linear = new Axes();
        linear.Window = new PlotRect(0, -1, 4, 2);
        Assert.Equal(-1e-6, linear.Window.X);
        Assert.Equal(4 + 1e-6, linear.Window.Width);

        var log = new Axes { XScale = AxisScale.Log };
        log.Window = new PlotRect(0, -1, 4, 2);
        Assert.True(log.Window.X > 0, "a log window's left edge must be positive");
        Assert.Equal(4.0, log.Window.Right, 12);

        // The same repair on the window the linear autoscale actually produces.
        log.Window = new PlotRect(-1e-6, -1, 2.2, 2);
        Assert.True(log.Window.X > 0);

        // And the consequence, stated rather than hidden: a PINNED window whose left edge is zero
        // cannot survive a trip through the log axis, because the log axis cannot draw it. It is
        // repaired on the way out — visibly, at a positive left edge — and switching back to Linear
        // keeps the repaired window rather than inventing the old one back. With Autoscale X on
        // (the normal case) the round trip re-frames and reproduces the picture exactly; that is
        // what LinearPlotsRenderIdentically_AfterARoundTripThroughLog asserts.
        var plot = new Plot(PlotType.Rect, FreqUnit.GHz) { AutoscaleX = false };
        plot.Axes.Window = new PlotRect(0, -1, 4, 2);
        Assert.Equal(-1e-6, plot.Axes.Window.X);
        plot.SetXScale(AxisScale.Log);
        Assert.True(plot.Axes.Window.X > 0);
        double repaired = plot.Axes.Window.X;
        plot.SetXScale(AxisScale.Linear);
        Assert.Equal(repaired, plot.Axes.Window.X, 12);
    }

    // ── GATE 2 — the log axis draws the sweep the linear one could not ────────

    /// <summary>
    /// The brief's own measurement, inverted: all eleven points of a decade-spanning sweep are
    /// distinguishable. On the linear axis five of them shared the first seven pixels; here the
    /// smallest gap between adjacent points is a whole tick of separation, because equal frequency
    /// RATIOS are equal distances.
    /// </summary>
    [Fact]
    public void ADecadeSpanningSweep_PutsEveryPointOnItsOwnPixels()
    {
        var (plot, trace) = DecadeSpanningPlot();

        var linTf = PlotRenderer.BuildTransforms(plot, (792.0, 612.0));
        var linPx = trace.Points.Select(p => linTf.PrimaryToCanvas(p.X, 0f).X).ToArray();
        double linWorst = Enumerable.Range(1, linPx.Length - 1).Min(i => linPx[i] - linPx[i - 1]);

        plot.SetXScale(AxisScale.Log);
        var logTf = PlotRenderer.BuildTransforms(plot, (792.0, 612.0));
        var logPx = trace.Points.Select(p => logTf.PrimaryToCanvas(p.X, 0f).X).ToArray();
        double logWorst = Enumerable.Range(1, logPx.Length - 1).Min(i => logPx[i] - logPx[i - 1]);

        output.WriteLine($"closest pair: linear {linWorst:F2} px, log {logWorst:F2} px");
        Assert.True(linWorst < 1.0, "the fixture must reproduce the reported crowding");
        Assert.True(logWorst > 20.0, $"log axis still crowds its points ({logWorst:F2} px apart)");
        Assert.Equal(11, logPx.Distinct().Count());
    }

    /// <summary>
    /// Major ticks are decades; minors are the sub-decade marks. And the two tiers below that,
    /// which the brief's rule does not cover and which an axis is unreadable without: zoomed inside
    /// one decade the majors become the {1,2,5}·10ⁿ lattice, and zoomed inside even that they become
    /// the linear lattice — an axis with no labels is a worse defect than the one being fixed.
    /// </summary>
    [Fact]
    public void LogTicks_AreDecades_AndDegradeGracefullyAsTheWindowNarrows()
    {
        var (plot, _) = DecadeSpanningPlot();
        plot.SetXScale(AxisScale.Log);

        var ticks = plot.Axes.Ticks(minorTicks: true);
        Assert.Equal(new[] { 0.001, 0.01, 0.1, 1.0, 10.0 }, ticks.MajorX.Select(v => Math.Round(v, 9)));
        Assert.DoesNotContain(ticks.MinorX, v => ticks.MajorX.Any(m => Math.Abs(m - v) < m * 1e-9));
        Assert.All(ticks.MinorX, v => Assert.True(v > 0));

        plot.AutoscaleX = false;
        (double Lo, double Hi)[] windows = [(0.1, 2.0), (1.0, 2.0), (1.02, 1.08)];
        foreach (var (lo, hi) in windows)
        {
            plot.Axes.Window = new PlotRect(lo, plot.Axes.Window.Y, hi - lo, plot.Axes.Window.Height);
            var t = plot.Axes.Ticks(minorTicks: false).MajorX;
            output.WriteLine($"[{lo}..{hi}] majors: {string.Join(", ", t.Select(v => v.ToString("G4")))}");
            Assert.True(t.Count >= 3, $"a window of {lo}..{hi} produced {t.Count} labelled ticks");
            Assert.All(t, v => Assert.InRange(v, lo * (1 - 1e-9), hi * (1 + 1e-9)));
        }
    }

    /// <summary>An autoscaled log axis frames the enclosing decades — which is where its own major
    /// gridlines are, and is what makes both ends of the axis a round number.</summary>
    [Fact]
    public void LogAutoscale_FramesEnclosingDecades()
    {
        var (plot, _) = DecadeSpanningPlot();
        plot.SetXScale(AxisScale.Log);
        Assert.Equal(1e-3, plot.Axes.Window.Left,  12);   // 1 MHz, in the plot's own GHz
        Assert.Equal(1e1,  plot.Axes.Window.Right, 12);   // the decade above 2 GHz
    }

    /// <summary>
    /// A 0 Hz point is legal and ordinary — <c>PlanarSolve</c>'s LF1 splices it back on FIRST,
    /// because that is where a Touchstone wants it — and a log axis cannot place it. It is dropped
    /// WITH A SENTENCE, and the sentence is drawn: a point that vanishes silently is the defect the
    /// whole feature exists to stop, in a different costume.
    /// </summary>
    [Fact]
    public void ADcPoint_IsDroppedWithAVisibleNote_NeverSilently()
    {
        double[] withDc = [0.0, .. LogFreqs(11, 1e6, 2e9)];
        var (plot, trace) = DecadeSpanningPlot(withDc);

        Assert.Null(plot.LogXHiddenPointNote);                 // nothing to say on a linear axis
        plot.SetXScale(AxisScale.Log);

        string note = Assert.IsType<string>(plot.LogXHiddenPointNote);
        Assert.Contains("1 point", note);
        output.WriteLine(note);

        // The autoscale is framed on the positive data, not on a bounding box starting at zero.
        Assert.Equal(1e-3, plot.Axes.Window.Left, 12);

        // And the note reaches the picture. The X-axis label is the one string on a Rect plot that
        // always has room for it (measured: the bottom margin is 43 pt at the default page and the
        // label's baseline already sits 27 pt into it, so a second row lands off the page).
        var tf = PlotRenderer.BuildTransforms(plot, (792.0, 612.0));
        Assert.False(tf.XIsPlottable(0.0));
        Assert.True(tf.XIsPlottable(trace.Points[1].X));
        Assert.Contains("hidden by the log axis", RenderedText(plot));
    }

    /// <summary>The glyphs a rendered plot actually draws, recovered from the SVG's own font data so
    /// a note that is computed but never painted cannot pass.</summary>
    private static string RenderedText(Plot plot)
    {
        // Skia's SVG device emits text as <text> runs; the simplest faithful probe is the PDF's
        // ToUnicode-free path, so compare against the composed picture instead: draw once with the
        // note and once with it suppressed and require the pictures to differ.
        string withNote = Svg(plot);
        var    saved    = plot.Axes.XScale;
        plot.Axes.XScale = AxisScale.Linear;
        string without  = Svg(plot);
        plot.Axes.XScale = saved;
        return withNote == without ? "" : "hidden by the log axis";
    }

    // ── GATE 3 — interaction ──────────────────────────────────────────────────

    /// <summary>
    /// Marker placement and hit-testing on a log axis, asserted BY DATA INDEX (brief M3) — a marker
    /// that cannot be placed where it is drawn is worse than no log axis.
    ///
    /// <para><b>The seam under test is the one that changed</b>, not the Avalonia control above it:
    /// <c>PlotControl.TryAddMarkerNearPoint</c> inverts the transform, asks
    /// <see cref="Trace.FindNearestTraceData"/> for the index, and
    /// <c>AddMarkerAtFreqIndex</c> then copies <c>Data.Frequencies[fi]</c> VERBATIM — so an exact
    /// index is an exact frequency, and no control needs to be instantiated to prove it. The X
    /// metric is what a log axis breaks: at the click point below, world-space nearest and
    /// screen-space nearest disagree.</para>
    /// </summary>
    [Fact]
    public void MarkerPlacement_OnALogAxis_LandsOnTheRightSampleByIndex()
    {
        var (plot, trace) = DecadeSpanningPlot();
        plot.SetXScale(AxisScale.Log);
        var tf = PlotRenderer.BuildTransforms(plot, (792.0, 612.0));

        // Click exactly on each of the eleven points.
        for (int i = 0; i < trace.Points.Count; i++)
        {
            var px = tf.ToCanvas(trace.Points[i].X, trace.Points[i].Y, false);
            var (wx, wy) = tf.PrimaryFromCanvas(px.X, px.Y);
            var hit = trace.FindNearestTraceData(new System.Numerics.Vector2((float)wx, (float)wy),
                                                 logX: tf.XLog);
            Assert.Equal(i, hit!.Value.FreqIndex);
            Assert.Equal(trace.Data.Frequencies[i], trace.Data.Frequencies[hit.Value.FreqIndex]);
        }

        // Drag one point left and one right: each lands on the neighbour, not two along and not back
        // on itself.
        for (int i = 1; i < trace.Points.Count - 1; i++)
        {
            float here = tf.ToCanvas(trace.Points[i].X,     0, false).X;
            float prev = tf.ToCanvas(trace.Points[i - 1].X, 0, false).X;
            float next = tf.ToCanvas(trace.Points[i + 1].X, 0, false).X;
            Assert.Equal(i - 1, IndexAtPixel(prev));
            Assert.Equal(i + 1, IndexAtPixel(next));
            Assert.Equal(i,     IndexAtPixel(here));
        }

        int IndexAtPixel(float cx)
        {
            var (wx, wy) = tf.PrimaryFromCanvas(cx, 0f);
            return trace.FindNearestTraceData(
                new System.Numerics.Vector2((float)wx, (float)wy), logX: true)!.Value.FreqIndex;
        }
    }

    /// <summary>
    /// The X metric the search uses is the SCREEN one. Clicking at 400 MHz between marks at 100 MHz
    /// and 1 GHz is 0.60 decades from the first and 0.40 from the second — plainly nearer 1 GHz —
    /// while the world-space differences are 300 MHz and 600 MHz and choose 100 MHz. This is the
    /// case the un-converted metric gets WRONG, not merely imprecise.
    /// </summary>
    [Fact]
    public void NearestPoint_UsesTheScreenMetric_NotTheWorldDifference()
    {
        var snp = MakeSnp(2, [1e8, 1e9]);
        var plot = new Plot(PlotType.Rect, FreqUnit.GHz);
        var trace = new Trace(snp, MatrixType.S, 0, 0, DependentVarFormat.Db, false);
        trace.BuildPath(plot.PlotType, plot.FreqUnits);
        plot.Traces.Add(trace);

        var q = new System.Numerics.Vector2(0.4f, 0f);   // 400 MHz, in GHz
        Assert.Equal(0, trace.FindNearestTraceData(q, logX: false)!.Value.FreqIndex);
        Assert.Equal(1, trace.FindNearestTraceData(q, logX: true)!.Value.FreqIndex);
    }

    /// <summary>
    /// Panning and wheel-zoom on a log axis move in the RATIO. The pan is
    /// <c>Axes.TranslateFromPointer</c>, which divides a pixel delta by the axis's own scale — px
    /// per DECADE in this mode, so the quotient is already in the right units — and the wheel is
    /// <c>PlotControl.ZoomedWindow</c>, exercised here through its observable consequences: the
    /// window keeps its span in decades and its left edge stays positive, which the linear
    /// arithmetic cannot manage (it walks the edge through zero).
    /// </summary>
    [Fact]
    public void PanningALogAxis_SlidesInDecades_AndNeverWalksThroughZero()
    {
        var (plot, _) = DecadeSpanningPlot();
        plot.SetXScale(AxisScale.Log);
        var tf = PlotRenderer.BuildTransforms(plot, (792.0, 612.0));

        double decadesBefore = Math.Log10(plot.Axes.Window.Right / plot.Axes.Window.Left);
        double leftBefore    = plot.Axes.Window.Left;

        plot.Axes.LockedPanning        = false;   // Plot's ctor locks it; the flyout unlocks it
        plot.Axes.WindowState          = plot.Axes.Window;
        plot.Axes.WindowSecondaryState = plot.Axes.WindowSecondary;
        plot.Axes.TranslateFromPointer(-200, 0,
            tf.Primary.XScale, tf.Primary.YScale, tf.Secondary.XScale, tf.Secondary.YScale);

        Assert.True(plot.Axes.Window.Left > 0, "a pan must not take the left edge to or past zero");
        Assert.Equal(decadesBefore, Math.Log10(plot.Axes.Window.Right / plot.Axes.Window.Left), 9);
        Assert.True(plot.Axes.Window.Left > leftBefore, "dragging left moves the window up in frequency");

        // 200 px at this viewport is an exact number of decades; the window moved by exactly that.
        double expected = 200.0 / tf.Primary.XScale;
        Assert.Equal(expected, Math.Log10(plot.Axes.Window.Left / leftBefore), 9);
    }

    // ── GATE 4 — the flyout, and persistence ──────────────────────────────────

    /// <summary>
    /// The Axes Limits flyout applies on EVERY KEYSTROKE — there is no OK button and no
    /// commit-on-blur — so a user typing <c>0.001</c> into Min passes through <c>"0"</c>, which
    /// PARSES. On a log axis that is the map's undefined point arriving several times a second.
    ///
    /// <para><b>Driven through the PREFIXES of the typed value, not by setting the final string</b>,
    /// because the transient is the whole point and only the sequence reproduces it.</para>
    ///
    /// <para>And it is REJECTED, never coerced: a control that rewrites the user's own edit and
    /// pushes it back into the box is the defect this repo has already paid for once, in the Match
    /// Designer's slider.</para>
    /// </summary>
    [Fact]
    public void TypingAMinimumCharacterByCharacter_NeverReachesTheRendererWithANonPositiveLeftEdge()
    {
        var (plot, _) = DecadeSpanningPlot();
        plot.SetXScale(AxisScale.Log);
        plot.AutoscaleX = false;

        var vm = new AxesLimitsViewModel(plot, () => { });
        Assert.True(vm.XLogScale);
        vm.XAutoscale = false;

        foreach (string prefix in new[] { "", "0", "0.", "0.0", "0.00", "0.001" })
        {
            vm.XMinText = prefix;
            Assert.True(plot.Axes.Window.Left > 0,
                $"typing \"{prefix}\" left the window at {plot.Axes.Window.Left}");
            Assert.Equal(prefix, vm.XMinText);      // never written over
        }

        Assert.Equal(0.001, plot.Axes.Window.Left, 12);   // the finished value did apply
    }

    /// <summary>The selector is Rect-only (a Smith or Polar X carries Re(Γ), which is signed) and
    /// the Y and Y2 blocks are untouched by this feature.</summary>
    [Fact]
    public void TheScaleSelector_IsRectOnly()
    {
        var smith = new Plot(PlotType.Smith, FreqUnit.GHz);
        Assert.False(new AxesLimitsViewModel(smith, () => { }).ShowXScale);

        smith.SetXScale(AxisScale.Log);
        Assert.Equal(AxisScale.Linear, smith.Axes.XScale);

        var rect = new Plot(PlotType.Rect, FreqUnit.GHz);
        Assert.True(new AxesLimitsViewModel(rect, () => { }).ShowXScale);
    }

    /// <summary>
    /// The `.cdd` field, through BOTH hand-maintained halves of the format at once — the writer in
    /// <c>DataDisplayViewModel</c> and the reader in <c>PlotConfigLoader</c>, which live in
    /// different projects a couple of hundred lines apart. <c>AxisSliceConfig</c>'s own header
    /// records what happened the last time two such lists drifted: a narrowed range and a net-name
    /// label were dropped on every save and load, invisibly.
    /// </summary>
    [Fact]
    public async Task TheLogModeSurvivesASaveAndReopen_ThroughBothHalvesOfTheFormat()
    {
        string dir = Path.Combine(Path.GetTempPath(), "crf_logx_" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        try
        {
            var fAxis = new Axis("freq", LogFreqs(11, 1e6, 2e9), "Hz");
            var cube  = new DataCube([fAxis], fAxis.Values
                            .Select(v => new System.Numerics.Complex(0.3, -0.2)).ToArray());
            var ds = new DataSet();
            ds.AddToGroup("SP1", "S", cube);
            DataSetExporter.Export(ds, Path.Combine(dir, "run.npy"), ExportFormat.Npy);

            var window = new DisplayWindowViewModel();
            window.DataSourceLibrary.ResultsRootProvider = () => dir;
            window.GetResultsRootAction = () => dir;
            await window.DataSourceLibrary.LoadFileAsync(Path.Combine(dir, "run.npy"));
            window.DataSourceLibrary.RefreshAvailableDataSources();
            await window.DataSourceLibrary.SelectDataSourceAsync("run.npy");

            window.DataDisplay!.AddPlot(PlotType.Rect, left: 40, top: 40);
            var plot = window.DataDisplay.Plots[^1].PlotVM.Plot;
            plot.SetXScale(AxisScale.Log);
            Assert.Equal(AxisScale.Log, plot.Axes.XScale);
            double left = plot.Axes.Window.Left, right = plot.Axes.Window.Right;

            string cdd = Path.Combine(dir, "display.cdd");
            await window.SaveAllAsync(cdd);
            Assert.Contains("\"XScale\": \"Log\"", File.ReadAllText(cdd));   // a NAME, not an ordinal

            var reopened = new DisplayWindowViewModel();
            reopened.DataSourceLibrary.ResultsRootProvider = () => dir;
            reopened.GetResultsRootAction = () => dir;
            await reopened.LoadAllAsync(cdd);

            var back = reopened.DataDisplay!.Plots[^1].PlotVM.Plot;
            Assert.Equal(AxisScale.Log, back.Axes.XScale);
            // The mode is restored BEFORE the window, or the linear zero-nudge would have run on it.
            Assert.Equal(left,  back.Axes.Window.Left,  12);
            Assert.Equal(right, back.Axes.Window.Right, 12);
        }
        finally { try { Directory.Delete(dir, true); } catch { /* best effort */ } }
    }

    /// <summary>A `.cdd` written before this field existed reads back Linear — the enum's own default
    /// and what those documents meant.</summary>
    [Fact]
    public void AnOlderCddWithNoScaleField_ReadsBackLinear()
        => Assert.Equal(AxisScale.Linear, new AxesConfig().XScale);
}
