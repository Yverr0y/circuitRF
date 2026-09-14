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
/// text fallback; the PDF, SVG and PNG writers are <c>PlotDocumentWriter</c>'s, shared with
/// <c>circuitrf render</c>. What this file adds is the four decisions that are the stackup's own —
/// which scene, at what size, on what PAGE (see <see cref="PageFor"/>: this drawing is the only copy
/// in the application that does not compose onto <c>PagePlacement.Letter</c>), in which colours.
/// <see cref="WBondGraphicExport"/> is the model it follows.</para>
///
/// <para>The picture is drawn by <see cref="StackupRenderer.Draw"/>, the same call the tab paints
/// each frame with, from a <see cref="StackupScene"/> built the same way — so a copied cross-section
/// cannot disagree with the one on screen about where a band is or how thick it reads.</para>
/// </summary>
internal static class StackupGraphicExport
{
    /// <summary>
    /// <b>The page is the PICTURE, plus this much on every side.</b>
    ///
    /// <para>Owner, 2026-09-13: pasted into a presentation, the cross-section arrived as a small
    /// drawing inside a much larger empty box. It was composed onto a letter-landscape page and
    /// centred there, exactly as an exported plot is — and a plot fills that page, while a stackup is
    /// a tall narrow drawing that never can. The pasted object's bounding box is the PAGE, so every
    /// blank inch of it was something the user then had to crop by hand.</para>
    ///
    /// <para>So the page is sized to the scene instead and the drawing fills it edge to edge. The
    /// padding is small and absolute rather than a fraction of the page: a fraction of a page that is
    /// itself the picture is a margin that grows with the stack, which is the same bug one step
    /// along.</para>
    /// </summary>
    internal const float PagePad = 8f;

    /// <summary>The page one scene is drawn on: its own extent, padded. <c>Margin</c> is
    /// <see cref="PagePad"/> so <see cref="FitScale"/> reads the usable area off the placement rather
    /// than re-deriving it.</summary>
    internal static PagePlacement PageFor(StackupScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return new(Math.Max(scene.Width,  1f) + 2f * PagePad,
                   Math.Max(scene.Height, 1f) + 2f * PagePad,
                   PagePad);
    }

    /// <summary>2x the page, matching <c>PlotExporter</c>'s own bitmap scale.</summary>
    internal const float BitmapScale = 2.0f;

    /// <summary>
    /// The widest page this copy will lay out on, when widening to keep the label column on one line
    /// (see <see cref="PageScene"/>).
    ///
    /// <para>A refusal to grow without bound rather than a layout choice: it is there so a technology
    /// with an extravagantly long layer name cannot produce a page nothing can open. A stackup that
    /// needs more than this gets this, and wraps — which is what every copy did before.</para>
    /// </summary>
    internal const float MaxPageWidth = 4f * PlotExporter.PageW;

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
    ///
    /// <para><b>And WIDE ENOUGH that no spec wraps</b> (owner, 2026-09-13). The pane has a width the
    /// user chose and the drawing makes the best of it — wrapping a dielectric's nine-piece spec onto
    /// a second line is the right answer there. A copied picture has no such constraint: it is going
    /// into a document, it is vector, and a page is only as wide as it is asked to be. So the page
    /// starts at <c>PlotExporter.PageW</c> and grows until the label column holds its widest group on
    /// one line. The arithmetic is <c>StackupScene.WidthThatFitsLabels</c>'s, not this file's —
    /// R-stk1-1: how a total width divides into two columns is the scene's business, and a second
    /// copy of it here would answer about a picture nobody drew.</para>
    /// </summary>
    internal static StackupScene PageScene(Technology? tech)
    {
        var t = tech ?? new Technology();
        return StackupScene.Build(t, StackupScene.WidthThatFitsLabels(t, PlotExporter.PageW, MaxPageWidth));
    }

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
    /// The UNIFORM scale that fits <paramref name="scene"/> into <paramref name="page"/>'s usable
    /// area — exactly 1 for <see cref="PageFor"/>'s own page, which is the point of it.
    ///
    /// <para>Uniform only: a stackup stretched on one axis misrepresents every thickness in it, which
    /// is the one thing this picture exists to show. It is still computed rather than assumed,
    /// because a caller composing this drawing onto a page of its own (a fixed sheet, a figure) has
    /// to get the same framing this does.</para>
    /// </summary>
    internal static float FitScale(StackupScene scene, PagePlacement page)
    {
        ArgumentNullException.ThrowIfNull(scene);

        float w = Math.Max(scene.Width,  1f);
        float h = Math.Max(scene.Height, 1f);

        return Math.Min(Math.Max(page.UsableWidth,  1f) / w,
                        Math.Max(page.UsableHeight, 1f) / h);
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
        SKCanvas canvas, StackupScene scene, StackupRenderTheme theme, PagePlacement page,
        bool transparentBackground = false)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(theme);

        float scale = FitScale(scene, page);

        int saved = canvas.Save();
        canvas.Translate((page.Width  - scene.Width  * scale) * 0.5f,
                         (page.Height - scene.Height * scale) * 0.5f);
        canvas.Scale(scale);
        StackupRenderer.Draw(canvas, scene, theme, overlay: null, transparentBackground);
        canvas.RestoreToCount(saved);
    }

    /// <summary>The page composition for one technology, and the page it is composed on — both, so a
    /// caller cannot hand one writer the composition and another writer a different page size.</summary>
    internal static (Action<SKCanvas> Compose, PagePlacement Page) Composition(
        Technology? tech, StackupRenderTheme theme, bool transparentBackground = false)
    {
        var scene = PageScene(tech);
        var page  = PageFor(scene);
        return (canvas => Render(canvas, scene, theme, page, transparentBackground), page);
    }

    internal static byte[] BuildPdfBytes(
        Technology? tech, StackupRenderTheme theme, bool transparentBackground = false)
    {
        var (compose, page) = Composition(tech, theme, transparentBackground);
        return PlotDocumentWriter.BuildPdfBytes(compose, page);
    }

    internal static string BuildSvgString(
        Technology? tech, StackupRenderTheme theme, bool transparentBackground = false)
    {
        var (compose, page) = Composition(tech, theme, transparentBackground);
        return PlotDocumentWriter.BuildSvgString(compose, page);
    }

    /// <summary>
    /// Renders the cross-section and places PDF, SVG and a 2x bitmap on the clipboard together.
    /// </summary>
    internal static async Task CopyToClipboardAsync(Control anchor, Technology? tech)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        var (theme, transparent) = ResolvePolicy();
        var (compose, page) = Composition(tech, theme, transparent);

        byte[] pdf   = PlotDocumentWriter.BuildPdfBytes(compose, page);
        string svg   = PlotDocumentWriter.BuildSvgString(compose, page);
        var  bitmap  = BuildBitmap(compose, page);

        // The page dimensions travel with the bytes: the Windows bypass tells the receiving
        // application how big the picture is, and handing it PlotExporter's letter landscape would
        // put this drawing back inside the empty box PageFor exists to remove.
        await PlotExporter.SetClipboardDataAsync(
            anchor, pdf, svg, json: string.Empty, bitmap, page.Width, page.Height);
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
    private static Avalonia.Media.Imaging.Bitmap? BuildBitmap(Action<SKCanvas> compose, PagePlacement page)
    {
        try
        {
            byte[]? png = PlotDocumentWriter.BuildPngBytes(compose, page, BitmapScale);
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
