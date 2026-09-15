// ================================================================
//  VectorExportClipAndLayerTests.cs — two owner-reported figure bugs, 2026-09-13,
//  which turned out to be ONE class of defect: a Skia construct that is correct on a
//  raster canvas and is not representable in SVG.
//
//   1. "the smith chart rendering does not show the axis grid lines" — every Smith
//      chart EXPORTED to SVG carried its outline, its real axis and its numbers over a
//      blank disc. AxesRenderer drew the constant-R and constant-X arcs inside a
//      SaveLayer (so an overlap composites once rather than darkening), and Skia's SVG
//      device DROPS a layer's contents entirely.
//
//   2. "I can't see the port arrowheads ... nor the port side lines" — every port
//      glyph exported to SVG was clipped down to the inside of its own NAME. The
//      renderer knocks the name's footprint out of the marker with
//      SKClipOperation.Difference; SVG has no difference operator, so the device writes
//      the raw path and the clip is re-read as an INTERSECT — exactly inverted.
//
//  The first two tests here pin the Skia behaviour itself, because both fixes are
//  worthless if the premise is wrong and neither premise is documented anywhere. The
//  rest pin the fixes.
// ================================================================

using System.Linq;
using System.Text;
using CircuitRF.Ui.DataDisplay;
using CircuitRF.Ui.Layout;
using CircuitRF.Ui.Renderers;
using SkiaSharp;
using Xunit;

namespace CircuitRF.Ui.Tests;

public sealed class VectorExportClipAndLayerTests
{
    private static string Svg(System.Action<SKCanvas> draw)
    {
        using var stream = new SKDynamicMemoryWStream();
        using (var canvas = SKSvgCanvas.Create(SKRect.Create(0, 0, 200, 200), stream)) draw(canvas);
        using var data = stream.DetachAsData();
        return Encoding.UTF8.GetString(data.ToArray());
    }

    // ── The two Skia facts both fixes rest on ────────────────────────────────

    [Fact]
    public void TheSvgDeviceDropsEverythingDrawnInsideASaveLayer()
    {
        string withLayer = Svg(c =>
        {
            using var p  = new SKPaint { IsStroke = true, StrokeWidth = 2, Color = SKColors.Red };
            using var lp = new SKPaint { Color = SKColors.Black.WithAlpha(128) };
            c.SaveLayer(lp);
            c.DrawCircle(50, 50, 30, p);
            c.Restore();
        });

        string without = Svg(c =>
        {
            using var p = new SKPaint { IsStroke = true, StrokeWidth = 2, Color = SKColors.Red };
            c.DrawCircle(50, 50, 30, p);
        });

        Assert.DoesNotContain("<ellipse", withLayer);
        Assert.Contains("<ellipse", without);
    }

    [Fact]
    public void TheSvgDeviceWritesADifferenceClipAsAnIntersectOne()
    {
        using var hole = new SKPath();
        hole.AddRect(SKRect.Create(80, 80, 40, 40));

        string difference = Svg(c =>
        {
            using var p = new SKPaint { IsStroke = true, StrokeWidth = 4, Color = SKColors.Red };
            c.Save();
            c.ClipPath(hole, SKClipOperation.Difference, antialias: true);
            c.DrawLine(10, 100, 190, 100, p);
            c.Restore();
        });

        // The clip is emitted as the raw hole with NO rule that would invert it, so a viewer
        // draws the line only INSIDE the hole. That is the whole bug.
        Assert.Contains("<clipPath", difference);
        Assert.DoesNotContain("evenodd", difference);

        // The replacement — an enclosing rectangle and the hole, even-odd, intersected — says the
        // same thing in a form SVG has.
        using var exclusion = new SKPath { FillType = SKPathFillType.EvenOdd };
        exclusion.AddRect(SKRect.Create(0, 0, 200, 200));
        exclusion.AddPath(hole);

        string evenOdd = Svg(c =>
        {
            using var p = new SKPaint { IsStroke = true, StrokeWidth = 4, Color = SKColors.Red };
            c.Save();
            c.ClipPath(exclusion, SKClipOperation.Intersect, antialias: true);
            c.DrawLine(10, 100, 190, 100, p);
            c.Restore();
        });

        Assert.Contains("clip-rule=\"evenodd\"", evenOdd);
    }

    [Fact]
    public void OneDrawPathFillsItsOwnOverlapOnceWhereSeparateDrawsComposite()
    {
        // The property the SaveLayer existed for, obtained without one. Two crossing circles at
        // 50 % black: as ONE path they read a single stroke's tone at the crossing; as two draws
        // they read the composite of two.
        using var bmp = new SKBitmap(200, 260);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.White);
            using var stroke = new SKPaint
            {
                IsStroke = true, StrokeWidth = 6, IsAntialias = false,
                Color = new SKColor(0, 0, 0, 128),
            };

            using var path = new SKPath();
            path.AddCircle(80, 100, 50);
            path.AddCircle(120, 100, 50);
            c.DrawPath(path, stroke);            // one draw, at the top

            c.DrawCircle(80,  200, 50, stroke);  // two draws, lower down
            c.DrawCircle(120, 200, 50, stroke);
        }

        // x = 100 is the vertical through both crossings.
        byte onePath  = bmp.GetPixel(100, 54).Red;    // crossing of the single-path pair
        byte twoDraws = bmp.GetPixel(100, 154).Red;   // crossing of the two-draw pair

        Assert.InRange(onePath, 120, 135);            // one stroke's tone
        Assert.InRange(twoDraws, 55, 70);             // two composited
    }

    // ── The Smith grid, fixed ────────────────────────────────────────────────

    private static (double W, double H) Canvas => (520.0, 520.0);

    private static Plot SmithPlot()
    {
        var plot = new Plot(PlotType.Smith, FreqUnit.GHz);
        plot.Axes.Window = new PlotRect(-1.0, -1.0, 2.0, 2.0);
        return plot;
    }

    [Fact]
    public void AnExportedSmithChartCarriesItsGridArcs()
    {
        string svg = Svg(c => PlotRenderer.Draw(c, Canvas, SmithPlot(), PlotDetail.Full,
                                                RenderTheme.Light, watermarkOpacity: 0f));

        // The outline is one <ellipse> and was never missing. The ARCS are the whole constant-R and
        // constant-X family, accumulated into ONE path and STROKED once — so the gate is that the
        // export carries a stroked path with a subpath per arc, which is what a SaveLayer took
        // away. (It was a FILLED path of stroked outlines until 2026-09-14; that form cancelled
        // itself where two arcs crossed — see SmithGridCrossingTests.)
        var family = System.Text.RegularExpressions.Regex.Matches(svg, "<path[^>]*\\bstroke=\"#[^\"]+\"[^>]*\\bd=\"([^\"]*)\"")
            .Select(m => m.Groups[1].Value)
            .OrderByDescending(d => d.Count(ch => ch == 'M'))
            .FirstOrDefault();

        Assert.NotNull(family);
        Assert.True(family!.Count(ch => ch == 'M') >= 20,
            "The exported Smith chart's largest stroked path has only "
          + family.Count(ch => ch == 'M') + " subpaths in it. The grid is ~27 arcs, several of them "
          + "cut into more than one span, so far fewer than that means the family did not reach the "
          + "file — which is what a SaveLayer did to it.");
    }

    // ── The port glyph, fixed ────────────────────────────────────────────────

    private static LayoutView PortedLine()
    {
        var view = new LayoutView { DbuPerMicron = 1000, DisplayUnit = LayoutUnit.Um, SnapDbu = 1000 };
        view.Shapes.Add(new RectShape { Layer = Metal, X1 = 0, Y1 = 0, X2 = 200_000, Y2 = 60_000 });
        view.Shapes.Add(new LabelShape
        {
            Layer = Metal, X = 0, Y = 30_000, Text = "1", Height = 12_000,
            IsPort = true, PortDirection = LayoutRotation.R0,
        });
        return view;
    }

    private static readonly LayerKey Metal = new(1, 0);

    private static Technology PortTech() => new()
    {
        Name = "Test", DefaultDisplayUnit = LayoutUnit.Um, DefaultSnapDbu = 1000,
        Layers =
        [
            new LayerDef
            {
                Key = Metal, Name = "Metal",
                Color = new CircuitRF.Design.Theming.Rgba(200, 122, 62),
                FillOpacity = 0.35, ZOrder = 0, Visible = true, Selectable = true,
            },
        ],
    };

    [Fact]
    public void AnExportedPortGlyphIsNotClippedToItsOwnName()
    {
        var vp = new LayoutViewport(-60_000, -40_000, 520.0 / 320_000, 520, 260);

        string svg = Svg(c => LayoutRenderer.Draw(
            c, PortedLine(), PortTech(), vp,
            new LayoutRenderOptions { Theme = LayoutRenderTheme.Light, ShowGrid = false }));

        // The marker pass knocks the port's NAME out of itself. Expressed as
        // SKClipOperation.Difference that inverts on the way out — the glyph becomes the only
        // place the marker may be drawn — and the only way to tell from the file is the rule on
        // the clip: an exclusion carries clip-rule="evenodd", a plain intersect carries nothing.
        Assert.Contains("clip-rule=\"evenodd\"", svg);

        // And the marker itself has to be in the file, outside the numeral's own few pixels. The
        // reference-plane bar spans the port's whole 60 um width, which at this viewport is far
        // longer than the 12 um numeral is tall.
        var marker = System.Text.RegularExpressions.Regex.Matches(svg, "<path[^>]*\\bfill=\"none\"[^>]*\\bd=\"([^\"]*)\"")
            .Select(m => m.Groups[1].Value)
            .Where(d => d.Count(ch => ch == 'M') >= 4)   // bar + two serifs + arrow shaft
            .ToList();

        Assert.NotEmpty(marker);
    }
}
