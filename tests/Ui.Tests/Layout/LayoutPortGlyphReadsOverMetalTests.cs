using System;
using CircuitRF.Render;
using CircuitRF.Ui.Theming;
using SkiaSharp;

namespace CircuitRF.Ui.Tests.Layout;

/// <summary>
/// <b>Every part of a port's glyph has to render wherever it falls — including on top of the
/// conductor it annotates.</b> Owner report, 2026-09-09: only one of a port's two end segments
/// appeared, and both are needed to read the port's width.
///
/// <para><b>Both were being drawn.</b> A differential render of the reported shape is what settles
/// it, and the numbers are in the assertions below: the serif hanging out over background changed
/// pixels, the one over metal changed <b>zero</b>. A port whose plane ends inside metal — a notch, a
/// tee, a pad on a pour — could only ever show the end that happened to stick out.</para>
///
/// <para><b>The cause was paint ORDER, not contrast.</b> It was first read as the marker being too
/// close to the fill it crossed, and answered with a background-coloured halo under every glyph;
/// that halo then showed up in its own right, as a pale line alongside the reference-plane bar in a
/// colour belonging to no part of the port (owner report, same day). What was actually happening is
/// that <c>DrawLayer</c> batches a layer's fills and outlines into one path each and paints them
/// after every shape in the layer has been visited, so a port drawn inline in that loop went
/// UNDERNEATH its own layer's artwork. Ports are now collected out of that loop and drawn above every
/// layer and every instance (<c>LayoutRenderer.DrawPortGlyphs</c>), the halo is gone, and this test
/// holds the outcome the report asked for rather than the mechanism first reached for.</para>
///
/// <para>A pixel probe cannot tell a glyph from the artwork under it, so the oracle here is the same
/// frame rendered WITHOUT the port: whatever differs is the glyph, and nothing else is.</para>
/// </summary>
public class LayoutPortGlyphReadsOverMetalTests
{
    private const int Dbu = 1000;

    private static Technology Tech() => new()
    {
        Layers = [new LayerDef { Key = new LayerKey(1, 0), Name = "Top", Color = new Rgba(200, 120, 40), ZOrder = 0 }],
    };

    /// <summary>A notch: metal everywhere except x &gt;= 4,000 with y &lt; 6,000. The wall is
    /// x = 4,000, y 0..6,000 — so a port on it has one end (y = 0) hanging out over background and
    /// the other (y = 6,000) buried in the metal that carries on to the right.</summary>
    private static PolygonShape Notch() => new()
    {
        Layer = new LayerKey(1, 0),
        Xy = [0, 0, 4_000, 0, 4_000, 6_000, 10_000, 6_000, 10_000, 10_000, 0, 10_000],
    };

    private static LabelShape WallPort() => new()
    {
        Layer = new LayerKey(1, 0), X = 4_000, Y = 3_000, Text = "P1", Height = 400,
        IsPort = true, PortDirection = LayoutRotation.R180, PortLayer = new LayerKey(1, 0),
    };

    private static readonly LayoutViewport Vp =
        LayoutViewport.ZoomToFit(new Bbox(-1_000, -1_000, 11_000, 11_000), 600, 600, 0.05);

    private static byte[] Render(bool withPort)
    {
        var v = new LayoutView { DbuPerMicron = Dbu, DisplayUnit = LayoutUnit.Um };
        v.Shapes.Add(Notch());
        if (withPort) v.Shapes.Add(WallPort());

        using var surface = SKSurface.Create(new SKImageInfo((int)Vp.Width, (int)Vp.Height));
        LayoutRenderer.Draw(surface.Canvas, v, Tech(), Vp,
            new LayoutRenderOptions { Theme = LayoutRenderTheme.Light });
        using var img = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(img);
        return bmp.Bytes;
    }

    /// <summary>Pixels within 6 device px of a world point that the port's presence changes.</summary>
    private static int GlyphPixelsNear(byte[] with, byte[] without, long wx, long wy)
    {
        int cx = (int)Math.Round(Vp.WorldToScreenX(wx));
        int cy = (int)Math.Round(Vp.WorldToScreenY(wy));
        int changed = 0;
        for (int y = cy - 6; y <= cy + 6; y++)
        for (int x = cx - 6; x <= cx + 6; x++)
        {
            if (x < 0 || y < 0 || x >= (int)Vp.Width || y >= (int)Vp.Height) continue;
            int i = (y * (int)Vp.Width + x) * 4;
            for (int c = 0; c < 3; c++)
                if (Math.Abs(with[i + c] - without[i + c]) > 8) { changed++; break; }
        }
        return changed;
    }

    [Fact]
    public void BothEndsOfThePlaneBar_AreVisible_IncludingTheOneInsideTheMetal()
    {
        var hint = LayoutPortDirection.Resolve(
            LayoutPortDirection.LookupFor(NotchView(), Tech(), ""), WallPort())!.Value;

        // The wall, and only the wall — the port's width is the edge it stands on.
        Assert.Equal(6_000, hint.WidthDbu);
        Assert.Equal(4_000, hint.PlaneX);
        Assert.Equal(3_000, hint.PlaneY);

        var with = Render(true);
        var without = Render(false);

        long half = hint.WidthDbu / 2;
        int lower = GlyphPixelsNear(with, without, hint.PlaneX, hint.PlaneY - half); // over background
        int upper = GlyphPixelsNear(with, without, hint.PlaneX, hint.PlaneY + half); // buried in metal

        // The one that always worked, and the one that measured EXACTLY ZERO before the halo.
        Assert.True(lower > 0, $"lower end drew nothing ({lower} px)");
        Assert.True(upper > 0, $"upper end drew nothing ({upper} px) — the glyph is invisible over its own layer's fill");

        // Not merely non-zero: the buried end must read about as strongly as the exposed one, or
        // "visible" is a technicality rather than something a user can aim at.
        Assert.True(upper >= lower / 2, $"upper end is far fainter than the lower ({upper} vs {lower} px)");
    }

    private static LayoutView NotchView()
    {
        var v = new LayoutView { DbuPerMicron = Dbu, DisplayUnit = LayoutUnit.Um };
        v.Shapes.Add(Notch());
        return v;
    }
}
