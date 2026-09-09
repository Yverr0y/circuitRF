using System.IO;
using System.Runtime.CompilerServices;
using CircuitRF.Render;
using CircuitRF.Render.DataDisplay;
using SkiaSharp;
using Xunit;

namespace CircuitRF.Ui.Tests.DataDisplay;

/// <summary>
/// Owner report, 2026-09-08: a Y-axis title on a WSProbe plot —
/// <c>SP1 ▸ WSProbes WSP1, WSP2 ▸ [Y].S[:, 1, 1]</c> — drew a RECTANGLE where the separator should
/// be. Skia draws a code point the typeface lacks as NOTDEF, a hollow box, and substitutes nothing.
///
/// <para><b>Three different renderers draw a Data Display Y-axis label, and the report was about
/// the one that was missed twice.</b> <c>AxesRenderer.DrawTitleAndAxisLabels</c> draws the label in
/// the Skia margin, <c>AxisLabelControl</c> draws the per-trace STRIPS beside the plot, and
/// <c>PlotComposer</c> draws those same strips into an exported document. A WSProbe trace's label
/// lands in the strips. Fixing only the margin one fixed nothing the reporter could see, which is
/// why this pins all three together rather than the one that was reported.</para>
/// </summary>
public class AxisLabelGlyphFallbackTests
{
    private const string PlexPath   = "Assets/Fonts/IBM_Plex_Sans/static/IBMPlexSans-Regular.ttf";
    private const string DejaVuPath  = "Assets/Fonts/DejaVuSans.ttf";

    /// <summary>The characters a Data Display label is actually built from, checked against the
    /// faces this repo ships rather than assumed.
    ///
    /// <para>Loaded through <c>SkiaFonts.RealFace</c> and never through the public properties, for
    /// two separate reasons. The properties honour <c>SkiaFonts.TestOverrideTypeface</c>, which
    /// about ten test classes set for their own duration while xunit runs classes in PARALLEL — so
    /// a coverage assertion on one would report the suite's scheduling. And <c>RealPlexRegular</c>
    /// hands back the LAZY CACHED instance the renderers themselves draw with: disposing that
    /// (which a <c>using</c> here did, once) destroys the shared typeface for the whole process and
    /// crashes the test host on the next paint. <c>RealFace</c> reads the resource afresh each
    /// call, so what it returns is this test's to own.</para></summary>
    [Theory]
    [InlineData(0x25B8, "the group separator ▸ — this is the one the report saw")]
    [InlineData(0x2220, "∠, in a MA/DB complex readout")]
    [InlineData(0x2225, "∥, in a parallel-immittance label")]
    public void PlexLacksTheGlyph_AndDejaVuHasIt(int codePoint, string what)
    {
        using var plex   = SkiaFonts.RealFace(PlexPath);
        using var dejaVu = SkiaFonts.RealFace(DejaVuPath);

        Assert.True(plex.GetGlyph(codePoint) == 0,
            $"IBM Plex now covers U+{codePoint:X4} ({what}). That is not a failure — but the "
          + "fallback below is then carrying one code point fewer, so check the rest still need it.");
        Assert.True(dejaVu.GetGlyph(codePoint) != 0,
            $"DejaVu must cover U+{codePoint:X4} ({what}) — it is the fallback face.");
    }

    /// <summary>
    /// The arrow was NOT the problem, and saying so is the point: the report described the broken
    /// glyph as the "->" one, and it is the small triangle beside it in
    /// <c>WSProbe GATE→DRAIN ▸ block</c>. Substituting the arrow would have been a fix for a
    /// character that renders correctly.
    /// </summary>
    [Fact]
    public void PlexCoversTheProbePairArrow()
    {
        using var plex = SkiaFonts.RealFace(PlexPath);
        Assert.NotEqual(0, plex.GetGlyph(0x2192));
    }

    /// <summary>
    /// The fallback changes what is DRAWN, not merely what is measured — a run splitter that
    /// measured with DejaVu and still drew with Plex would leave the box on screen and pass every
    /// width assertion.
    /// </summary>
    [Fact]
    public void TheFallbackDrawsADifferentPictureThanPlexAlone()
    {
        const string label = "SP1 ▸ WSProbes WSP1, WSP2 ▸ [Y].S[:, 1, 1]";

        Assert.NotEqual(RenderOf(label, fallback: false), RenderOf(label, fallback: true));

        // ASCII must be untouched by the fallback, or the split is firing where it should not and
        // every ordinary label has silently changed face.
        const string ascii = "SP1.S[:, 1, 1] (dB)";
        Assert.Equal(RenderOf(ascii, fallback: false), RenderOf(ascii, fallback: true));
    }

    private static string RenderOf(string text, bool fallback)
    {
        using var plex   = SkiaFonts.RealFace(PlexPath);
        using var dejaVu = SkiaFonts.RealFace(DejaVuPath);
        using var fPlex  = new SKFont(plex,   20f);
        using var fDeja  = new SKFont(dejaVu, 20f);
        using var paint  = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        var info = new SKImageInfo(700, 40);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);

        if (fallback)
            RendererText.DrawLeftTextWithFallback(surface.Canvas, text, 4f, 28f, fPlex, fDeja, paint);
        else
            surface.Canvas.DrawText(text, 4f, 28f, SKTextAlign.Left, fPlex, paint);

        using var image = surface.Snapshot();
        using var data  = image.Encode(SKEncodedImageFormat.Png, 100);
        return System.Convert.ToBase64String(data.ToArray());
    }

    /// <summary>
    /// The two Y-label renderers that are not <c>AxesRenderer</c>. A source scan, because neither is
    /// reachable without a canvas — <c>AxisLabelControl</c>'s draw lives in a nested
    /// <c>ImmediateDrawingContext</c> custom operation, and <c>PlotComposer.Render</c> wants a page
    /// of placed plots. What has to hold is only that they measure and draw through the fallback at
    /// all; the picture assertion above is what says the fallback works.
    /// </summary>
    [Theory]
    [InlineData("src/Ui/DataDisplay/Controls/AxisLabelControl.cs")]
    [InlineData("src/Render/DataDisplay/PlotComposer.cs")]
    public void EveryYLabelRendererGoesThroughTheFallback(string relativePath)
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        Assert.Contains("RendererText.DrawLeftTextWithFallback", src, System.StringComparison.Ordinal);
        Assert.Contains("RendererText.MeasureTextWithFallback",  src, System.StringComparison.Ordinal);
        Assert.Contains("SkiaFonts.DejaVuRegular",               src, System.StringComparison.Ordinal);
    }

    private static string RepoRoot([CallerFilePath] string here = "")
    {
        var dir = Path.GetDirectoryName(here);
        while (dir is not null && !File.Exists(Path.Combine(dir, "circuitRF.slnx")))
            dir = Path.GetDirectoryName(dir);
        Assert.True(dir is not null, "Could not locate the repo root walking up from this test file.");
        return dir!;
    }
}
