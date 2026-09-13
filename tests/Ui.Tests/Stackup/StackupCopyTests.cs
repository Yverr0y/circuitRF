using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Input;
using CircuitRF.Ui.Layout;
using CircuitRF.Ui.Theming;
using CircuitRF.Ui.Views.Layout;
using SkiaSharp;
using Xunit;

// The namespace is NOT `CircuitRF.Ui.Tests.Stackup`, though the folder is — see StackupSceneTests.cs
// for the shadowing that rule exists to avoid.
namespace CircuitRF.Ui.Tests.StackupRender;

/// <summary>
/// brief-stackup-render-7-copy.md's gate.
///
/// <para>A clipboard WRITE is not assertable headlessly — there is no clipboard in this process and
/// <c>PlotExporter.SetClipboardDataAsync</c> is the shared path this brief deliberately does not
/// re-implement. So every test here asserts everything up TO the write, which is where all of this
/// brief's decisions actually are: which scene, at what size, in which colours, with what overlay.</para>
/// </summary>
public class StackupCopyTests
{
    private const float PageW = 792f;   // PlotExporter's own Letter landscape, restated so a test
    private const float PageH = 612f;   // that measures the page does not read it from the subject.

    private static float UsableW => PageW * (1f - 2f * StackupGraphicExport.MarginFraction);
    private static float UsableH => PageH * (1f - 2f * StackupGraphicExport.MarginFraction);

    public static TheoryData<string> ShippedIds()
    {
        var data = new TheoryData<string>();
        foreach (var entry in ShippedTechnologies.All) data.Add(entry.Id);
        return data;
    }

    private static Technology Shipped(string id) => ShippedTechnologies.Load(id);

    // ── R-stk7-2 — the whole stackup, never the scrolled window ───────────────────────────────────

    /// <summary>
    /// <b>The crop test, and the one that matters.</b> A copy of what happens to be visible is a
    /// picture missing layers, and a picture missing layers is not obviously wrong to anyone reading
    /// it later — which is what makes the failure worth a test rather than an eye.
    ///
    /// <para>The fixture is taller than the pane it is drawn in by a factor of several, so at the
    /// control's own height most of these names are scrolled away; every one of them is still in the
    /// copied picture.</para>
    /// </summary>
    [Fact]
    public void EveryBandNameIsCopied_IncludingTheOnesThePaneScrollsAway()
    {
        var tech  = Tall(14);
        var scene = StackupGraphicExport.PageScene(tech);

        // Non-vacuity: this stack genuinely does not fit the drawing pane, so a screenshot of the
        // control would have cropped it.
        Assert.True(scene.Height > TechEditorMetrics.StackupDrawingMinHeight * 3,
            $"fixture is not tall enough to be a crop test: {scene.Height} px");

        string svg = StackupGraphicExport.BuildSvgString(tech, StackupRenderTheme.Light);

        foreach (var layer in tech.Stackup.Layers)
            Assert.Contains(layer.Name, svg, StringComparison.Ordinal);
    }

    /// <summary>The same, on every technology circuitRF ships — so a new one joins this gate the day
    /// it lands rather than the day someone remembers to add it.</summary>
    [Theory]
    [MemberData(nameof(ShippedIds))]
    public void EveryBandNameIsCopied_OnEveryShippedTechnology(string id)
    {
        var tech = Shipped(id);
        string svg = StackupGraphicExport.BuildSvgString(tech, StackupRenderTheme.Light);

        foreach (var layer in tech.Stackup.Layers)
            Assert.Contains(layer.Name, svg, StringComparison.Ordinal);
    }

    /// <summary>
    /// A tall technology SCALES to fit the page — uniformly, and centred.
    ///
    /// <para>Measured off the composed raster rather than off the arithmetic that produced it: the
    /// question is where the ink actually landed, and a test that re-ran <c>FitScale</c> and compared
    /// it with itself would pass however the canvas was transformed. Uniform scale is the part that
    /// matters — a stackup stretched on one axis misrepresents every thickness in it.</para>
    /// </summary>
    [Fact]
    public void ATallStackupIsScaledToFitThePage_UniformlyAndCentred()
    {
        var tech  = Tall(14);
        var scene = StackupGraphicExport.PageScene(tech);

        // Non-vacuity: the HEIGHT is what binds here, not the page margin.
        Assert.True(scene.Height > UsableH, $"fixture fits the page unscaled: {scene.Height} px");

        var (minX, minY, maxX, maxY) = PaintedBounds(tech, StackupRenderTheme.Light);

        // Inside the page's usable area, on all four sides.
        float margin = StackupGraphicExport.MarginFraction;
        Assert.True(minX >= PageW * margin - 2, $"left edge at {minX}");
        Assert.True(minY >= PageH * margin - 2, $"top edge at {minY}");
        Assert.True(maxX <= PageW * (1 - margin) + 2, $"right edge at {maxX}");
        Assert.True(maxY <= PageH * (1 - margin) + 2, $"bottom edge at {maxY}");

        // And the same scale on both axes: the painted box has the scene's own aspect ratio.
        float scaleX = (maxX - minX) / scene.Width;
        float scaleY = (maxY - minY) / scene.Height;
        Assert.Equal(scaleX, scaleY, 2);

        // Centred: the margins left and right, top and bottom, are the same.
        Assert.Equal(minX, PageW - maxX, 0);
        Assert.Equal(minY, PageH - maxY, 0);
    }

    /// <summary>
    /// The page composition is a pure function of the TECHNOLOGY — never of the pane's width, which
    /// is the one number a screenshot or a reused on-screen scene would have carried into it.
    /// </summary>
    [Fact]
    public void ThePictureIsLaidOutAtThePageWidth_NotThePanes()
    {
        var tech = Shipped(ShippedTechnologies.All.First().Id);

        Assert.Equal(PageW, StackupGraphicExport.PageScene(tech).Width);
    }

    // ── R-stk7-3 — an export is artwork, not an editing surface ───────────────────────────────────

    /// <summary>
    /// <b>A selection changes nothing about the copied picture.</b>
    ///
    /// <para>Byte-identical, and NOT vacuously so: the second half of this test draws the same scene
    /// on the same page with a selection overlay and shows that it would have been visible. Without
    /// that half, a renderer that had stopped drawing selections at all would pass.</para>
    /// </summary>
    [Fact]
    public void ASelectionIsNotPartOfTheCopiedPicture()
    {
        var tech  = Shipped(ShippedTechnologies.All.First().Id);
        var scene = StackupGraphicExport.PageScene(tech);
        var theme = StackupRenderTheme.Light;

        var vm = new TechEditorViewModel(
            Path.Combine(Path.GetTempPath(), "stackup-copy.ctech"), tech);
        vm.SelectedStackupLayerName = scene.Bands[0].Name;
        string selected = StackupGraphicExport.BuildSvgString(vm.Working, theme);

        vm.ClearStackupSelection();
        string cleared = StackupGraphicExport.BuildSvgString(vm.Working, theme);

        Assert.Equal(cleared, selected, StringComparer.Ordinal);

        // The overlay the export refuses to draw is one that WOULD have shown.
        string withOverlay = PlotDocumentWriter.BuildSvgString(
            canvas => RenderWithOverlay(canvas, scene, theme,
                new StackupOverlay { SelectedLayer = scene.Bands[0].Name }),
            PagePlacement.Letter);

        Assert.NotEqual(cleared, withOverlay);
    }

    /// <summary>Hover, grippers and a drag's chrome are the same answer as the selection's — none of
    /// them is a parameter of this export at all, which is what makes them unreachable.</summary>
    [Fact]
    public void NoOverlayReachesTheCopiedPicture()
    {
        var tech  = Shipped(ShippedTechnologies.All.First().Id);
        var scene = StackupGraphicExport.PageScene(tech);
        var theme = StackupRenderTheme.Light;

        string plain = StackupGraphicExport.BuildSvgString(tech, theme);

        foreach (var overlay in new[]
        {
            new StackupOverlay { HoverLayer    = scene.Bands[0].Name },
            new StackupOverlay { DragInsertY   = scene.Bands[0].Rect.Bottom },
            new StackupOverlay { DragGhost     = (scene.BandColumn.Left, 10f, scene.BandColumn.Right, 40f) },
        })
        {
            string chromed = PlotDocumentWriter.BuildSvgString(
                canvas => RenderWithOverlay(canvas, scene, theme, overlay), PagePlacement.Letter);

            Assert.NotEqual(plain, chromed);
        }
    }

    // ── Both writers, everywhere ──────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(ShippedIds))]
    public void BothWritersProduceADocument_ForEveryShippedTechnology(string id)
    {
        var tech = Shipped(id);

        Assert.NotEmpty(StackupGraphicExport.BuildPdfBytes(tech, StackupRenderTheme.Light));
        Assert.Contains("<svg", StackupGraphicExport.BuildSvgString(tech, StackupRenderTheme.Light),
            StringComparison.Ordinal);
    }

    /// <summary>An empty stackup copies a blank cross-section rather than throwing or dividing by
    /// zero — and so does a null technology, which is what a canvas with no document bound has.</summary>
    [Fact]
    public void BothWritersProduceADocument_ForAnEmptyStackupAndForNothingAtAll()
    {
        foreach (var tech in new Technology?[] { new Technology(), null })
        {
            Assert.NotEmpty(StackupGraphicExport.BuildPdfBytes(tech, StackupRenderTheme.Light));
            Assert.Contains("<svg", StackupGraphicExport.BuildSvgString(tech, StackupRenderTheme.Light),
                StringComparison.Ordinal);
        }
    }

    // ── R-stk7-4 — the keystroke ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>Ctrl</c>/<c>Cmd</c>+<c>C</c> on the Stackup tab copies the picture. Both spellings, because
    /// one of the two is the only one a Mac user ever types.
    /// </summary>
    [Theory]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Meta)]
    public void TheCopyKeystrokeIsTakenOnTheStackupTab(KeyModifiers modifiers)
        => Assert.True(TechEditorView.CopyKeystrokeTakes(Key.C, modifiers, StackupTab, source: null));

    /// <summary>
    /// <b>The other three tabs keep their plain text copy.</b> This editor has four of them and only
    /// one has a picture to copy; claiming the keystroke on the Layers, DRC or Interchange tab would
    /// break Ctrl+C there and give nothing back.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(-1)]
    public void TheCopyKeystrokeIsNotTakenOnAnyOtherTab(int tabIndex)
        => Assert.False(TechEditorView.CopyKeystrokeTakes(Key.C, KeyModifiers.Control, tabIndex, source: null));

    /// <summary>
    /// <b>It never steals the keystroke from a text field.</b> Avalonia's text box only marks Ctrl+C
    /// handled when it actually copied something, so a Ctrl+C typed into a field with nothing selected
    /// reaches this handler — and putting a picture on the clipboard because somebody's text selection
    /// was empty is not what they asked for. The inline value editor over the drawing is a
    /// <c>TextBox</c>, so this covers R-stk4's box as well as the cards' fields.
    /// </summary>
    [Fact]
    public void TheCopyKeystrokeIsNotTakenFromATextInput()
    {
        Assert.False(TechEditorView.CopyKeystrokeTakes(
            Key.C, KeyModifiers.Control, StackupTab, new Avalonia.Controls.TextBox()));
        Assert.False(TechEditorView.CopyKeystrokeTakes(
            Key.C, KeyModifiers.Control, StackupTab, new Avalonia.Controls.SelectableTextBlock()));
    }

    /// <summary>The gesture is the plain one. Ctrl+Shift+C and Ctrl+Alt+C mean other things in other
    /// applications and are not claimed here; an unmodified C is a letter.</summary>
    [Theory]
    [InlineData(Key.C, KeyModifiers.None)]
    [InlineData(Key.C, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(Key.C, KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData(Key.C, KeyModifiers.Meta    | KeyModifiers.Shift)]
    [InlineData(Key.X, KeyModifiers.Control)]
    [InlineData(Key.V, KeyModifiers.Control)]
    public void NoOtherGestureIsClaimed(Key key, KeyModifiers modifiers)
        => Assert.False(TechEditorView.CopyKeystrokeTakes(key, modifiers, StackupTab, source: null));

    /// <summary>
    /// <b>The handler must BUBBLE.</b> R-stk2-10 keeps the drawing non-focusable, so "while the
    /// drawing has focus" cannot be the discriminator the brief names — and a tunnelling handler here
    /// would take Ctrl+C away from every text box in the tab before the box ever saw it, which is the
    /// exact thing R-stk7-4 says not to do. Bubbling is what makes "only a keystroke nothing else
    /// claimed" true, and it is invisible from the handler's own body — hence a scan.
    /// </summary>
    [Fact]
    public void TheCopyHandlerIsRegisteredBubbling_UnlikeTheOtherTwo()
    {
        string code = RepoFile(Path.Combine("src", "Ui", "Views", "Layout", "TechEditorView.axaml.cs"));

        Assert.Contains("AddHandler(KeyDownEvent, OnCopyKeyDown);", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AddHandler(KeyDownEvent, OnCopyKeyDown, RoutingStrategies",
            code, StringComparison.Ordinal);

        // The other two tunnel on purpose, and this scan is only meaningful while that is still true.
        Assert.Contains("AddHandler(KeyDownEvent, OnEscapeKeyDown, RoutingStrategies.Tunnel);",
            code, StringComparison.Ordinal);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>The Stackup tab's index in the editor's <c>SectionTabs</c>.</summary>
    private const int StackupTab = 1;

    /// <summary>
    /// The composed SVG with Skia's own generated resource ids renumbered from zero.
    ///
    /// <para><b>Skia's SVG device numbers <c>clipPath</c> ids from a PROCESS-GLOBAL counter</b>, in
    /// hex — so two SVGs of the same picture, composed one after the other, carry <c>cl_9</c> and
    /// <c>cl_f</c> and are not byte-equal however deterministic the drawing is. It appears on the
    /// transparent-background path only, which is the only one that clips (one id per document; the
    /// opaque path generates none at all).</para>
    ///
    /// <para>Renumbering is what keeps a byte comparison meaningful rather than abandoning it: every
    /// coordinate, colour, order and string is still compared exactly, so the failure this gate
    /// exists for — a layout pass that read a dictionary in hash order — still fails it.</para>
    /// </summary>
    internal static string CanonicalSvg(string svg)
    {
        var seen = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);

        return System.Text.RegularExpressions.Regex.Replace(
            svg, @"(?<=id=""|url\(#)([A-Za-z]+_[0-9a-f]+)(?="")?", m =>
            {
                if (!seen.TryGetValue(m.Value, out var canonical))
                    seen[m.Value] = canonical = $"id{seen.Count}";
                return canonical;
            });
    }

    private static string RepoFile(string relativePath)
    {
        string dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "circuitrf.slnx")))
            dir = Path.GetDirectoryName(dir) ?? "";
        return File.ReadAllText(Path.Combine(dir, relativePath));
    }


    /// <summary>The export's own page composition with an overlay put back — the counterfactual the
    /// R-stk7-3 tests measure against, and the only place in this file that draws one.</summary>
    private static void RenderWithOverlay(
        SKCanvas canvas, StackupScene scene, StackupRenderTheme theme, StackupOverlay overlay)
    {
        float scale = StackupGraphicExport.FitScale(scene, PageW, PageH);
        canvas.Translate((PageW - scene.Width * scale) * 0.5f, (PageH - scene.Height * scale) * 0.5f);
        canvas.Scale(scale);
        StackupRenderer.Draw(canvas, scene, theme, overlay);
    }

    /// <summary>The bounding box of everything the composed page actually painted, in page points.
    /// The page starts transparent and the drawing carries its own ground, so "painted" is exactly
    /// "alpha > 0".</summary>
    internal static (float MinX, float MinY, float MaxX, float MaxY) PaintedBounds(
        Technology? tech, StackupRenderTheme theme)
    {
        using var bitmap = Raster(tech, theme);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).Alpha > 0)
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }

        Assert.True(minX != int.MaxValue, "the composed page painted nothing at all");
        return (minX, minY, maxX + 1, maxY + 1);
    }

    /// <summary>The composed page as a 1x raster, so a test can ask where the ink is.</summary>
    internal static SKBitmap Raster(
        Technology? tech, StackupRenderTheme theme, bool transparentBackground = false)
    {
        var bitmap = new SKBitmap((int)PageW, (int)PageH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            StackupGraphicExport.Render(canvas, StackupGraphicExport.PageScene(tech), theme,
                                        PageW, PageH, transparentBackground);
        }
        return bitmap;
    }

    /// <summary>A stackup with <paramref name="pairs"/> conductor/dielectric pairs — taller than the
    /// page, which is the case the whole of R-stk7-2 is about.</summary>
    internal static Technology Tall(int pairs)
    {
        var tech = new Technology { Name = "Tall", DefaultDisplayUnit = LayoutUnit.Um };
        for (int i = 0; i < pairs; i++)
        {
            tech.Stackup.Layers.Add(new StackupLayer
            {
                Kind = StackupKind.Conductor, Name = $"Metal {i + 1}",
                ThicknessDbu = 3_000, SigmaSm = 5.8e7,
            });
            tech.Stackup.Layers.Add(new StackupLayer
            {
                Kind = StackupKind.Dielectric, Name = $"Dielectric {i + 1}",
                ThicknessDbu = 20_000, Epsr = 4.4, TanD = 0.02,
            });
        }
        return tech;
    }
}
