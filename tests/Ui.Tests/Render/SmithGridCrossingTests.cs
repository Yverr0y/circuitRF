// ================================================================
//  SmithGridCrossingTests.cs — "the Smith Chart axes grid has gaps where the grid lines
//  overlap. Looks almost like an XOR." (owner, 2026-09-14)
//
//  It was exactly an XOR. The constant-R and constant-X family is accumulated into ONE path
//  and drawn ONCE, because N draws composite N times and the grid then goes dark wherever it
//  is busiest. That path used to hold each arc's STROKED OUTLINE (SKPaint.GetFillPath),
//  filled with a winding rule — and the arcs that carry a mask were trimmed with
//  SKPath.Op(…, Intersect), which returns pathops' own contour orientation. That orientation
//  is not the one AddCircle plus the stroker produce, and it is not even stable from one call
//  to the next, so under a winding fill two pieces of opposite sign CANCELLED where they
//  overlapped: a white rhombus the size of the two stroke widths, punched out of every such
//  crossing.
//
//  The family is now the arcs' CENTRELINES, stroked in one draw, with the masks applied as an
//  angular trim. A stroke has no such failure mode — Skia strokes the whole path in one pass
//  and orients its own output consistently.
//
//  The first test pins the Skia premise; the second is the picture.
// ================================================================

using System;
using System.Collections.Generic;
using CircuitRF.Render.DataDisplay;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests.Render;

[Collection(CircuitRF.Ui.Tests.SkiaFontsTypefaceCollection.Name)]
public sealed class SmithGridCrossingTests(ITestOutputHelper output)
{
    // ── The Skia premise the defect rested on ────────────────────────────────

    [Fact]
    public void APathOpResultCancelsAnOrdinaryRibbonItOverlaps_AStrokedCentrelineCannot()
    {
        using var stroke = new SKPaint
        {
            IsStroke = true, StrokeWidth = 8, IsAntialias = false, Color = SKColors.Black,
        };

        static SKPath Ribbon(SKPaint p, float cx, float cy, float r)
        {
            using var circle = new SKPath();
            circle.AddCircle(cx, cy, r);
            var ribbon = new SKPath();
            p.GetFillPath(circle, ribbon);
            return ribbon;
        }

        // A mask that takes nothing away — all that matters is that the piece has been through
        // pathops, exactly as a trimmed grid arc has.
        using var inert = new SKPath { FillType = SKPathFillType.EvenOdd };
        inert.AddRect(SKRect.Create(0, 0, 200, 200));
        inert.AddCircle(4, 4, 2);

        using var plain   = Ribbon(stroke, 70, 100, 45);
        using var trimmed = Ribbon(stroke, 130, 100, 45).Op(inert, SKPathOp.Intersect)!;

        // The two crossings of those circles are at x = 100, y = 100 ∓ sqrt(45² − 30²) ≈ 100 ∓ 33.5.
        const int CrossX = 100, CrossY = 66;

        using var outlines = new SKPath { FillType = SKPathFillType.Winding };
        outlines.AddPath(plain);
        outlines.AddPath(trimmed);

        using var centrelines = new SKPath();
        centrelines.AddCircle(70, 100, 45);
        centrelines.AddCircle(130, 100, 45);

        using var fill = new SKPaint { Color = SKColors.Black, IsAntialias = false };

        byte Tone(Action<SKCanvas> draw)
        {
            using var bmp = new SKBitmap(200, 200);
            using (var canvas = new SKCanvas(bmp)) { canvas.Clear(SKColors.White); draw(canvas); }
            return bmp.GetPixel(CrossX, CrossY).Red;
        }

        byte asOutlines   = Tone(c => c.DrawPath(outlines, fill));
        byte asCentreline = Tone(c => c.DrawPath(centrelines, stroke));

        Assert.Equal(255, asOutlines);     // the hole: a pathops piece cancelling an ordinary one
        Assert.Equal(0, asCentreline);     // one stroked draw has nothing to cancel
    }

    // ── The picture ──────────────────────────────────────────────────────────

    private const int Size = 520;

    /// <summary>Γ = (z − 1)/(z + 1) for z = r + jx — where a constant-R circle and a
    /// constant-X arc meet is simply the point they share.</summary>
    private static (double Re, double Im) Gamma(double r, double x)
    {
        double den = (r + 1.0) * (r + 1.0) + x * x;
        return ((r * r - 1.0 + x * x) / den, 2.0 * x / den);
    }

    /// <summary>The Smith disc as the render actually placed it: the centre and radius are
    /// measured off the outline rather than re-derived from the viewport arithmetic, so this
    /// says nothing about how the plot area is laid out.</summary>
    private static (double Cx, double Cy, double R) MeasureDisc(SKBitmap bmp)
    {
        bool Ink(int x, int y) => bmp.GetPixel(x, y).Red < 250;

        (int Lo, int Hi) SpanX(int y)
        {
            int lo = -1, hi = -1;
            for (int x = 0; x < Size; x++) if (Ink(x, y)) { if (lo < 0) lo = x; hi = x; }
            return (lo, hi);
        }

        (int Lo, int Hi) SpanY(int x)
        {
            int lo = -1, hi = -1;
            for (int y = 0; y < Size; y++) if (Ink(x, y)) { if (lo < 0) lo = y; hi = y; }
            return (lo, hi);
        }

        var rough = SpanX(Size / 2);
        var down  = SpanY((rough.Lo + rough.Hi) / 2);
        double cy = (down.Lo + down.Hi) / 2.0;

        var across = SpanX((int)Math.Round(cy));
        double cx  = (across.Lo + across.Hi) / 2.0;
        double r   = ((across.Hi - across.Lo) + (down.Hi - down.Lo)) / 4.0;
        return (cx, cy, r);
    }

    [Fact]
    public void NoCrossingOfTheSmithGridIsPunchedOut()
    {
        var plot = new Plot(PlotType.Smith, FreqUnit.GHz);
        plot.Axes.Window = new PlotRect(-1.0, -1.0, 2.0, 2.0);

        // A heavier grid than the 0.5 default, because the defect scales with the stroke: at a
        // hairline it was a one-pixel pinhole and here it is an unmistakable white rhombus.
        plot.Axes.GridThicknessFactor = 3.0;

        using var bmp = new SKBitmap(Size, Size);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.White);
            PlotRenderer.Draw(canvas, (Size, Size), plot, PlotDetail.Full,
                              RenderTheme.Light, watermarkOpacity: 0f);
        }

        var (cx, cy, r) = MeasureDisc(bmp);
        Assert.InRange(r, Size * 0.3, Size * 0.5);

        // Where a constant-R circle meets a constant-X arc, in Γ: z = r + jx ⇒ Γ = (z−1)/(z+1).
        // These are the crossings the mask tables reach, which are the ones that were punched.
        var crossings = new List<(double R, double X)>
        {
            (1.0, 1.0), (1.0, -1.0), (0.5, 0.5), (0.5, -0.5),
            (2.0, 1.0), (2.0, -1.0), (1.0, 0.5), (1.0, -0.5),
        };

        var punched = new List<string>();
        foreach (var (rr, xx) in crossings)
        {
            var (gRe, gIm) = Gamma(rr, xx);

            int px = (int)Math.Round(cx + gRe * r);
            int py = (int)Math.Round(cy - gIm * r);

            int white = 0;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    if (bmp.GetPixel(px + dx, py + dy).Red >= 250) white++;

            output.WriteLine($"z = {rr} + j{xx} -> Γ = ({gRe:F3}, {gIm:F3}) px ({px},{py}) white = {white}/25");
            if (white > 0) punched.Add($"z = {rr} + j{xx} at ({px},{py}): {white} of 25 px blank");
        }

        Assert.True(punched.Count == 0,
            "The Smith grid has a hole where its arcs cross:\n  " + string.Join("\n  ", punched)
          + "\nThat is the winding-cancellation defect: the family reaches the canvas as one path, "
          + "so two pieces of opposite contour orientation erase each other where they overlap.");
    }

    [Fact]
    public void TheWholeFamilyStillReachesTheCanvasInOneDraw()
    {
        // The property the one-path construction exists for, and the reason the arcs may not
        // simply be drawn one at a time: a crossing must read a SINGLE stroke's tone. Two
        // composites of the same 50 % grey would be visibly darker, which is the stippling this
        // renderer was carrying before 2026-09-08.
        var plot = new Plot(PlotType.Smith, FreqUnit.GHz);
        plot.Axes.Window = new PlotRect(-1.0, -1.0, 2.0, 2.0);
        plot.Axes.GridThicknessFactor = 3.0;

        using var bmp = new SKBitmap(Size, Size);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.White);
            PlotRenderer.Draw(canvas, (Size, Size), plot, PlotDetail.Full,
                              RenderTheme.Light, watermarkOpacity: 0f);
        }

        var (cx, cy, r) = MeasureDisc(bmp);

        // z = 1 + j1 is a crossing of the r = 1 circle with the x = 1 arc. z = 0.25 + j1 is on
        // that same arc where nothing else meets it — r = 0.25 carries no circle of its own.
        byte At(double rr, double xx)
        {
            var (gRe, gIm) = Gamma(rr, xx);
            return bmp.GetPixel((int)Math.Round(cx + gRe * r), (int)Math.Round(cy - gIm * r)).Red;
        }

        byte crossing = At(1.0, 1.0);
        byte plain    = At(0.25, 1.0);

        output.WriteLine($"crossing tone {crossing}, plain-arc tone {plain}");
        Assert.Equal(plain, crossing);
    }
}
