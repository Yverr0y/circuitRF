using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using CircuitRF.Ui.DataDisplay;
using CircuitRF.Ui.Theming;
using CircuitRF.Ui.WBond;
using SkiaSharp;

namespace CircuitRF.Ui.Layout;

/// <summary>
/// R-stk7-1/2/3 — copies the stackup cross-section to the system clipboard as a PICTURE: PDF, SVG
/// and a 2x bitmap at once, so the receiving application picks whichever it understands.
///
/// <para><b>There is no clipboard code here, on purpose.</b> The owner's instruction for this brief
/// was that the pitfalls of copy-to-clipboard are already solved and must not be solved a second
/// time, and they are: the write is <see cref="PlotExporter.SetClipboardDataAsync"/>, which carries
/// the Windows bypass (the cross-platform path does not tell Windows the page dimensions) and the
/// text fallback; the page is <c>PagePlacement.Letter</c>, the one every other copy in this
/// application composes onto; the PDF, SVG and PNG writers are <c>PlotDocumentWriter</c>'s, shared
/// with <c>circuitrf render</c>. What this file adds is the three decisions that are the stackup's
/// own — which scene, at what size, in which colours — and nothing else.
/// <see cref="WBondGraphicExport"/> is the model it follows.</para>
///
/// <para>The picture is drawn by <see cref="StackupRenderer.Draw"/>, the same call the tab paints
/// each frame with, from a <see cref="StackupScene"/> built the same way — so a copied cross-section
/// cannot disagree with the one on screen about where a band is or how thick it reads.</para>
/// </summary>
internal static class StackupGraphicExport
{
    /// <summary>
    /// The page margin, which is <see cref="WBondGraphicExport.MarginFraction"/> itself and not a
    /// second copy of the number: two graphic copies from one application that framed their pages
    /// differently would be a difference nobody chose.
    /// </summary>
    internal const float MarginFraction = WBondGraphicExport.MarginFraction;

    /// <summary>2x the page, matching <c>PlotExporter</c>'s own bitmap scale.</summary>
    internal const float BitmapScale = 2.0f;

    /// <summary>
    /// <b>The whole stackup at the page's width — never the scrolled window (R-stk7-2).</b>
    ///
    /// <para>A copy of what happens to be visible is a crop, and a crop of a stackup is a picture
    /// missing layers, which is not obviously wrong to anyone reading it later. The scene is a pure
    /// function of <c>(Technology, width)</c>, so building a fresh one at the page width costs one
    /// layout pass and cannot be cropped by anything: no screenshot of the control, no reuse of the
    /// control's cached scene (which is laid out at the PANE's width), no
    /// <c>RenderTargetBitmap</c> over the visual tree.</para>
    ///
    /// <para>A null technology lays out an EMPTY one rather than returning null, exactly as
    /// <c>StackupSceneCache</c> does — an empty cross-section is a valid picture and a blank page is
    /// the honest thing to put on the clipboard for a technology with no stackup in it.</para>
    /// </summary>
    internal static StackupScene PageScene(Technology? tech)
        => StackupScene.Build(tech ?? new Technology(), PlotExporter.PageW);

    /// <summary>
    /// R-stk7-1 — <b>both</b> halves of the copy's appearance come from
    /// <see cref="ClipboardRenderPolicy"/>, and neither is ever a per-call parameter. A user who has
    /// set "always copy in light mode", or "copy on a transparent background", has set it for this
    /// drawing too.
    ///
    /// <para>Transparency costs this picture more than it costs the others, and the cost is paid in
    /// <see cref="StackupRenderer"/> rather than refused here: the cross-section uses its background
    /// colour as PAINT, filling a via's bore with it so the hole reads as a drill that removed
    /// material. With no ground to paint, the renderer cuts the bands away from the bore instead, so
    /// the hole shows the destination document through it. A bore simply left unpainted would show
    /// the dielectric behind the via, which is the thing that fill was added to stop.</para>
    /// </summary>
    internal static (StackupRenderTheme Theme, bool TransparentBackground) ResolvePolicy()
    {
        var (variant, transparent) = ClipboardRenderPolicy.Resolve();
        return (StackupRenderTheme.FromTheme(ThemeService.Active, variant), transparent);
    }

    /// <summary>
    /// The UNIFORM scale that fits <paramref name="scene"/> into the page's usable area.
    ///
    /// <para>Uniform only: a stackup stretched on one axis misrepresents every thickness in it, which
    /// is the one thing this picture exists to show. The scene is laid out at the page's full width,
    /// so the width term alone is <c>1 - 2 * MarginFraction</c>; the height term binds instead when
    /// the stack is tall enough that its intrinsic height overflows the page, which is the case a
    /// screenshot of the scrolled pane would have silently cropped.</para>
    /// </summary>
    internal static float FitScale(StackupScene scene, float pageW, float pageH)
    {
        ArgumentNullException.ThrowIfNull(scene);

        float w = Math.Max(scene.Width,  1f);
        float h = Math.Max(scene.Height, 1f);

        return Math.Min(pageW * (1f - 2f * MarginFraction) / w,
                        pageH * (1f - 2f * MarginFraction) / h);
    }

    /// <summary>
    /// Composes the cross-section onto a page-sized canvas, centred.
    ///
    /// <para>R-stk7-3: <c>overlay: null</c>. No selection outline, no hover mark, no grippers, no
    /// insertion line, no drag ghost — an export is artwork, not an editing surface, and what is
    /// copied is the design rather than the state of somebody's pointer. Everything else on the
    /// drawing STAYS: the boundary-condition notes, the band names, the specs and the ground
    /// reference's heavy edge are content, and brief 1 (R-stk1-4) already keeps the drawing free of
    /// annotation, so there is nothing else to strip.</para>
    /// </summary>
    internal static void Render(
        SKCanvas canvas, StackupScene scene, StackupRenderTheme theme, float pageW, float pageH,
        bool transparentBackground = false)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(theme);

        float scale = FitScale(scene, pageW, pageH);

        int saved = canvas.Save();
        canvas.Translate((pageW - scene.Width * scale) * 0.5f, (pageH - scene.Height * scale) * 0.5f);
        canvas.Scale(scale);
        StackupRenderer.Draw(canvas, scene, theme, overlay: null, transparentBackground);
        canvas.RestoreToCount(saved);
    }

    /// <summary>The page composition for one technology, as the callback every writer below takes.</summary>
    internal static Action<SKCanvas> Composer(
        Technology? tech, StackupRenderTheme theme, bool transparentBackground = false)
    {
        var scene = PageScene(tech);
        return canvas => Render(
            canvas, scene, theme, PlotExporter.PageW, PlotExporter.PageH, transparentBackground);
    }

    internal static byte[] BuildPdfBytes(
        Technology? tech, StackupRenderTheme theme, bool transparentBackground = false)
        => PlotExporter.BuildPdfBytes(Composer(tech, theme, transparentBackground));

    internal static string BuildSvgString(
        Technology? tech, StackupRenderTheme theme, bool transparentBackground = false)
        => PlotExporter.BuildSvgString(Composer(tech, theme, transparentBackground));

    /// <summary>
    /// Renders the cross-section and places PDF, SVG and a 2x bitmap on the clipboard together.
    /// </summary>
    internal static async Task CopyToClipboardAsync(Control anchor, Technology? tech)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        var (theme, transparent) = ResolvePolicy();
        var compose = Composer(tech, theme, transparent);

        byte[] pdf   = PlotExporter.BuildPdfBytes(compose);
        string svg   = PlotExporter.BuildSvgString(compose);
        var  bitmap  = BuildBitmap(compose);

        await PlotExporter.SetClipboardDataAsync(anchor, pdf, svg, json: string.Empty, bitmap);
    }

    /// <summary>
    /// A 2x raster for applications that recognise <c>DataFormat.Bitmap</c> but not the
    /// application-scoped PDF format.
    ///
    /// <para><b>Best effort, and the <c>catch</c> is not sloppiness</b> — it is
    /// <see cref="WBondGraphicExport"/>'s own rule: a raster failure must never cost the vector
    /// formats, which are the richer two of the three.</para>
    ///
    /// <para>The raster itself is <c>PlotDocumentWriter</c>'s, not a fourth copy of "scale a canvas
    /// and encode a PNG" — which also means it enters <c>PlotDocumentScope</c>, so this bitmap is
    /// built to the same standard as the PDF and the SVG beside it.</para>
    /// </summary>
    private static Avalonia.Media.Imaging.Bitmap? BuildBitmap(Action<SKCanvas> compose)
    {
        try
        {
            byte[]? png = PlotDocumentWriter.BuildPngBytes(compose, PagePlacement.Letter, BitmapScale);
            if (png is null) return null;

            using var ms = new MemoryStream(png);
            return new Avalonia.Media.Imaging.Bitmap(ms);
        }
        catch
        {
            return null;   // best effort — never let a raster failure cost the vector formats
        }
    }
}
