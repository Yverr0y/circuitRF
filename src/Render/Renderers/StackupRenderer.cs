using System;
using System.Collections.Generic;
using SkiaSharp;

namespace CircuitRF.Render;

/// <summary>
/// Transient interaction state for the stackup cross-section — the frame's chrome, which the editor
/// fills in and the renderer draws. No Skia and no Avalonia types: what is selected is a
/// <c>StackupLayer.Name</c>, which is the key everything in this drawing resolves by.
/// </summary>
public sealed record StackupOverlay
{
    public static readonly StackupOverlay Empty = new();

    /// <summary>The layer under the pointer, or null.</summary>
    public string? HoverLayer { get; init; }

    /// <summary>The selected layer, or null (brief 3).</summary>
    public string? SelectedLayer { get; init; }

    /// <summary>Where a drag would land, in scene coordinates, or null (brief 5).</summary>
    public (float Left, float Top, float Right, float Bottom)? DragGhost { get; init; }

    /// <summary>
    /// R-stk5-3's insertion line: the scene y of the band boundary a reorder drag would drop
    /// between, or null.
    ///
    /// <para>A y and not a rect, because the line spans the band column and the scene already knows
    /// how wide that is (<see cref="StackupScene.BandColumn"/>). The GHOST says what is moving and
    /// this says where it lands; a drawing with only one of the two leaves the user guessing which
    /// gap the drop takes.</para>
    /// </summary>
    public float? DragInsertY { get; init; }
}

/// <summary>
/// <b>Paints a <see cref="StackupScene"/>. Computes nothing.</b>
///
/// <para>Every rectangle it draws came out of the scene, which is the whole of R-stk1-1: the control
/// that hit-tests reads the same rects, so the picture and the pointer cannot disagree. If something
/// here needs a position that is not already on the scene, the fix is to put it on the scene — not to
/// work it out again here.</para>
///
/// <para>The draw order is the scene's hit order, back to front: bands, barrels, labels, then the
/// selection outline and the grippers on top.</para>
/// </summary>
public static class StackupRenderer
{
    /// <summary>Stroke width of an ordinary band edge.</summary>
    public const float BandEdgeWidth = 1f;

    /// <summary>The ground-designated conductor is the one band with a heavy edge. Every other
    /// distinction in this picture is a label.</summary>
    public const float GroundEdgeWidth = 2.5f;

    public const float SelectionWidth = 2f;

    /// <summary>R-stk5-3's insertion line. Heavier than the selection outline because it is the one
    /// mark on the drawing that says what a release will DO, and it is drawn over bands whose own
    /// edges are already a pixel wide.</summary>
    public const float InsertionWidth = 3f;

    /// <summary>The hover outline is the SAME outline, lighter — R-stk3-4 asks for a lighter mark,
    /// not a differently shaped one, so a band the pointer is over reads as "this is what a click
    /// would take" rather than as a second kind of state.</summary>
    public const float HoverWidth = 1.5f;

    /// <summary>How much of <see cref="StackupRenderTheme.Selection"/> the hover outline keeps.</summary>
    public const byte HoverAlpha = 90;

    /// <summary>
    /// How far OUTSIDE a band's own edge the selection outline sits (R-stk3-3).
    ///
    /// <para>It has to clear the heaviest edge the picture draws, which is the ground reference's
    /// <see cref="GroundEdgeWidth"/> — that mark is the one distinguishing feature of the whole
    /// drawing (brief 1 §1) and a selection outline laid on top of it would hide exactly the thing
    /// the user selected the band to look at. A stroke is centred on its rect, so the ground edge
    /// reaches <c>GroundEdgeWidth / 2</c> outward and the selection's own inner face reaches
    /// <c>SelectionGap - SelectionWidth / 2</c>; this value keeps the second clear of the first.</para>
    /// </summary>
    public const float SelectionGap = 3f;

    /// <summary>Where the outline around <paramref name="rect"/> goes — the one place the gap is
    /// applied, so the selected outline, the hovered outline and the tests cannot separately decide
    /// what "outside the band" means.</summary>
    public static SKRect OutlineRectFor(SKRect rect) => SKRect.Inflate(rect, SelectionGap, SelectionGap);

    /// <summary>
    /// Clips the band pass so that nothing is painted inside a via's bore, and returns the save
    /// count to restore to. Used only on the transparent-background path.
    ///
    /// <para><b>Why a clip and not "just skip the bore fill".</b> The bands are painted BEFORE the
    /// barrels, so a bore that is simply left unpainted shows the dielectric the via passes through —
    /// which is the picture the owner reported as wrong on 2026-09-13 ("2 vertical lines with a
    /// gap"), not transparency. A hole has to be cut in what is behind it, and the only thing behind
    /// it is the bands.</para>
    ///
    /// <para><b>Why a clip and not <c>SKBlendMode.Clear</c>.</b> Clear erases what is already on the
    /// surface, which is right on a raster and is not recorded at all by Skia's SVG device and only
    /// approximated by its PDF one — and those two are the richest formats this drawing is copied in,
    /// so the failure would be invisible where it matters most. A clip is an ordinary primitive in
    /// both. Even-odd over one outer rect plus the bores is the spelling that needs no difference
    /// clip, which SVG does not express either.</para>
    ///
    /// <para>Only the BAND pass is clipped. A barrel's walls lie outside its own bore, and a label
    /// that happens to cross one is ink drawn on top — it is not what the hole is cut through.</para>
    /// </summary>
    private static int ClipBands(SKCanvas canvas, StackupScene scene)
    {
        int saved = canvas.Save();

        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        path.AddRect(new SKRect(0, 0, scene.Width, scene.Height));

        bool any = false;
        foreach (var barrel in scene.Barrels)
        {
            if (BoreOf(barrel) is not { } bore) continue;
            path.AddRect(bore);
            any = true;
        }

        if (any) canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);
        return saved;
    }

    /// <summary>
    /// The hole in a barrel — the part a drill removed — or null when the via has no hole.
    ///
    /// <para>For a plated barrel that is the space BETWEEN the two walls, not the whole rect: the
    /// walls are metal and the band behind them is what they are drawn against, so cutting them out
    /// too would leave a via whose walls are see-through wherever the technology gave that layer a
    /// colour with alpha in it. An unplated hole has no walls, so all of it is the hole.</para>
    /// </summary>
    private static SKRect? BoreOf(StackupBarrel barrel)
    {
        switch (barrel.Look)
        {
            case StackupViaLook.SolidFill:
                return null;                        // metal all the way through; nothing was drilled

            case StackupViaLook.PlatedBarrel:
                var bore = new SKRect(barrel.Rect.Left + barrel.WallPx, barrel.Rect.Top,
                                      barrel.Rect.Right - barrel.WallPx, barrel.Rect.Bottom);
                return bore.Width > 0 ? bore : null;   // walls that meet leave no bore to cut

            default:
                return barrel.Rect;                 // an unplated hole is the bore and nothing else
        }
    }

    /// <param name="transparentBackground">
    /// Paint no ground at all: the destination supplies it. The pane's own background rect is
    /// skipped, and — the part that is not obvious — <b>every via bore becomes a real hole</b>
    /// rather than a disc of the pane colour, so what shows through a drilled hole is whatever the
    /// picture was pasted onto. See <see cref="ClipBands"/> for why that needs a clip rather than
    /// simply not painting the bore.
    /// </param>
    public static void Draw(
        SKCanvas canvas, StackupScene scene, StackupRenderTheme theme, StackupOverlay? overlay = null,
        bool transparentBackground = false)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(theme);
        overlay ??= StackupOverlay.Empty;

        if (!transparentBackground)
            canvas.DrawRect(new SKRect(0, 0, scene.Width, scene.Height), new SKPaint { Color = theme.Background });
        if (scene.IsTooNarrow) { DrawLabels(canvas, scene, theme); return; }

        using var fill   = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill   };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

        // ── Bands ────────────────────────────────────────────────────────────────────────────────
        int bandsClip = transparentBackground ? ClipBands(canvas, scene) : canvas.Save();
        foreach (var band in scene.Bands)
        {
            fill.Color = band.Fill is { } rgba ? new SKColor(rgba.R, rgba.G, rgba.B, rgba.A) : theme.DielectricFill;
            canvas.DrawRect(band.Rect, fill);

            stroke.Color       = band.IsGroundReference ? theme.GroundAccent : theme.BandEdge;
            stroke.StrokeWidth = band.IsGroundReference ? GroundEdgeWidth : BandEdgeWidth;
            canvas.DrawRect(band.Rect, stroke);
        }
        canvas.RestoreToCount(bandsClip);

        // ── Barrels ──────────────────────────────────────────────────────────────────────────────
        stroke.StrokeWidth = BandEdgeWidth;
        foreach (var barrel in scene.Barrels)
        {
            var metal = barrel.Fill is { } rgba
                ? new SKColor(rgba.R, rgba.G, rgba.B, rgba.A)
                : new SKColor(StackupScene.MetalFallback.R, StackupScene.MetalFallback.G,
                              StackupScene.MetalFallback.B, StackupScene.MetalFallback.A);
            stroke.Color = theme.BandEdge;

            // THE HOLE IS DRAWN AS A VOID, not left transparent (owner, 2026-09-13: a plated barrel
            // "renders strangely… 2 vertical lines with a gap"). Letting the bands show through the
            // bore leaves two metal walls with dielectric between them, which reads as two separate
            // thin vias rather than as one barrel with a hole in it. A drill removes material, so the
            // bore is painted in the pane's own ground first and the walls go on top of it — the
            // convention every cross-section drawing of a plated through-hole uses — and one outline
            // around the whole barrel then binds the two walls into one object.
            //
            // With no ground to paint it in, the same convention is kept by REMOVING the bands from
            // the bore instead (ClipBands, above) — so the hole is a hole, and what shows through it
            // is whatever the picture was pasted onto rather than the dielectric behind the via.
            if (barrel.Look != StackupViaLook.SolidFill && !transparentBackground)
            {
                fill.Color = theme.Background;
                canvas.DrawRect(barrel.Rect, fill);
            }

            fill.Color = metal;
            switch (barrel.Look)
            {
                case StackupViaLook.SolidFill:
                    canvas.DrawRect(barrel.Rect, fill);
                    break;

                // A HOLLOW barrel: two metal walls with the bore between them. WallPx is the WALL,
                // not the hole radius — the confusion the model field's own doc comment warns about,
                // and one a drawing that got it backwards would make permanent.
                case StackupViaLook.PlatedBarrel:
                    canvas.DrawRect(
                        new SKRect(barrel.Rect.Left, barrel.Rect.Top,
                                   barrel.Rect.Left + barrel.WallPx, barrel.Rect.Bottom), fill);
                    canvas.DrawRect(
                        new SKRect(barrel.Rect.Right - barrel.WallPx, barrel.Rect.Top,
                                   barrel.Rect.Right, barrel.Rect.Bottom), fill);
                    break;

                // An unplated hole is not a conductor. It is the bore and nothing else.
                case StackupViaLook.UnplatedHole:
                default:
                    break;
            }
            canvas.DrawRect(barrel.Rect, stroke);
        }

        DrawLabels(canvas, scene, theme);

        // ── Hover, then selection, then grippers, on top ─────────────────────────────────────────
        //
        // Hover FIRST, so that a band which is both hovered and selected shows the selected outline:
        // the two are the same rect and the last one drawn is the one that is seen.
        if (overlay.HoverLayer is { Length: > 0 } hovered &&
            !string.Equals(hovered, overlay.SelectedLayer, StringComparison.Ordinal) &&
            scene.RectOf(hovered) is { } hoverRect)
        {
            stroke.Color       = theme.Selection.WithAlpha(HoverAlpha);
            stroke.StrokeWidth = HoverWidth;
            canvas.DrawRect(OutlineRectFor(hoverRect), stroke);
        }

        if (overlay.SelectedLayer is { Length: > 0 } selected && scene.RectOf(selected) is { } rect)
        {
            stroke.Color       = theme.Selection;
            stroke.StrokeWidth = SelectionWidth;
            canvas.DrawRect(OutlineRectFor(rect), stroke);
        }

        if (overlay.DragGhost is { } ghost)
        {
            fill.Color = theme.DragGhost;
            canvas.DrawRect(new SKRect(ghost.Left, ghost.Top, ghost.Right, ghost.Bottom), fill);
        }

        // The insertion line last of the drag chrome, so it stays readable over its own ghost. It is
        // the SELECTION colour rather than the ghost's: the ghost is a translucent copy of the thing
        // being moved and the line is a decision about where it goes, and the one thing that must
        // never happen is the two reading as one smear.
        if (overlay.DragInsertY is { } insertY && scene.BandColumn.Width > 0)
        {
            stroke.Color       = theme.Selection;
            stroke.StrokeWidth = InsertionWidth;
            canvas.DrawLine(scene.BandColumn.Left, insertY, scene.BandColumn.Right, insertY, stroke);
        }

        // R-stk1-5 / §5: grippers are SUBTLE — drawn only when the overlay says this via is hovered
        // or selected, never on every via at rest. The scene PLACES them regardless, because the
        // hit-test has to find one whether or not this frame drew it.
        fill.Color   = theme.Gripper;
        stroke.Color = theme.BandEdge;
        stroke.StrokeWidth = BandEdgeWidth;
        foreach (var barrel in scene.Barrels)
        {
            if (!string.Equals(barrel.Name, overlay.HoverLayer, StringComparison.Ordinal) &&
                !string.Equals(barrel.Name, overlay.SelectedLayer, StringComparison.Ordinal)) continue;

            DrawGrip(canvas, barrel.GripTop, fill, stroke);
            DrawGrip(canvas, barrel.GripBottom, fill, stroke);
        }
    }

    /// <summary>The gripper's HIT rect is larger than its glyph by
    /// <see cref="StackupScene.GripHitSlop"/> on each side (§5) — so the glyph is the hit rect
    /// deflated by exactly that, and the two can never be separately tuned into disagreement.</summary>
    private static void DrawGrip(SKCanvas canvas, SKRect hit, SKPaint fill, SKPaint stroke)
    {
        var glyph = SKRect.Inflate(hit, -StackupScene.GripHitSlop, -StackupScene.GripHitSlop);
        canvas.DrawRect(glyph, fill);
        canvas.DrawRect(glyph, stroke);
    }

    private static void DrawLabels(SKCanvas canvas, StackupScene scene, StackupRenderTheme theme)
    {
        var fonts = new Dictionary<StackupLabelStyle, SKFont>();
        try
        {
            using var ink = new SKPaint { IsAntialias = true };
            foreach (var label in scene.Labels)
            {
                if (!fonts.TryGetValue(label.Style, out var font))
                    fonts[label.Style] = font = FontFor(label.Style);

                ink.Color = label.Style switch
                {
                    // A band's name sits on metal whose colour is the technology's and is identical
                    // in both variants, so it takes fixed dark ink rather than the theme's
                    // foreground — which would vanish into copper in the dark variant.
                    StackupLabelStyle.BandName => theme.OnBandInk,
                    // …and the same name AFTER it moved off its band takes the theme's, because it
                    // is on the pane now and not on the metal. Same face, different ink.
                    StackupLabelStyle.ColumnName => theme.LabelInk,
                    StackupLabelStyle.Accent   => theme.GroundAccent,
                    StackupLabelStyle.Refusal  => theme.Refusal,
                    _                          => theme.LabelInk,
                };
                canvas.DrawText(label.Text, label.TextX, label.Baseline, SKTextAlign.Left, font, ink);
            }
        }
        finally
        {
            foreach (var font in fonts.Values) font.Dispose();
        }
    }

    /// <summary>
    /// The face and size one label style is drawn in.
    ///
    /// <para>The SIZE comes from <see cref="StackupScene.FontSizeFor"/> rather than from a second
    /// switch here, because brief 4's inline editor opens at the size the label under it was drawn at
    /// and a box sized from a stale copy of this table would be the wrong height with nothing saying
    /// so. The FACE stays here: the scene does not draw, and nothing outside this file needs it.</para>
    /// </summary>
    private static SKFont FontFor(StackupLabelStyle style) => new(
        style switch
        {
            StackupLabelStyle.BandName   => SkiaFonts.PlexSemiBold,
            StackupLabelStyle.ColumnName => SkiaFonts.PlexSemiBold,
            StackupLabelStyle.Note       => SkiaFonts.PlexSemiBold,
            _                            => SkiaFonts.PlexRegular,
        },
        StackupScene.FontSizeFor(style));
}
