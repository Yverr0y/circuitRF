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

    public static void Draw(
        SKCanvas canvas, StackupScene scene, StackupRenderTheme theme, StackupOverlay? overlay = null)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(theme);
        overlay ??= StackupOverlay.Empty;

        canvas.DrawRect(new SKRect(0, 0, scene.Width, scene.Height), new SKPaint { Color = theme.Background });
        if (scene.IsTooNarrow) { DrawLabels(canvas, scene, theme); return; }

        using var fill   = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill   };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

        // ── Bands ────────────────────────────────────────────────────────────────────────────────
        foreach (var band in scene.Bands)
        {
            fill.Color = band.Fill is { } rgba ? new SKColor(rgba.R, rgba.G, rgba.B, rgba.A) : theme.DielectricFill;
            canvas.DrawRect(band.Rect, fill);

            stroke.Color       = band.IsGroundReference ? theme.GroundAccent : theme.BandEdge;
            stroke.StrokeWidth = band.IsGroundReference ? GroundEdgeWidth : BandEdgeWidth;
            canvas.DrawRect(band.Rect, stroke);
        }

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
            if (barrel.Look != StackupViaLook.SolidFill)
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

        // ── Selection, then grippers, on top ─────────────────────────────────────────────────────
        if (overlay.SelectedLayer is { Length: > 0 } selected && scene.RectOf(selected) is { } rect)
        {
            stroke.Color       = theme.Selection;
            stroke.StrokeWidth = SelectionWidth;
            canvas.DrawRect(rect, stroke);
        }

        if (overlay.DragGhost is { } ghost)
        {
            fill.Color = theme.DragGhost;
            canvas.DrawRect(new SKRect(ghost.Left, ghost.Top, ghost.Right, ghost.Bottom), fill);
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

    private static SKFont FontFor(StackupLabelStyle style) => style switch
    {
        StackupLabelStyle.BandName   => new SKFont(SkiaFonts.PlexSemiBold, StackupScene.BandNameSize),
        StackupLabelStyle.ColumnName => new SKFont(SkiaFonts.PlexSemiBold, StackupScene.BandNameSize),
        StackupLabelStyle.Note     => new SKFont(SkiaFonts.PlexSemiBold, StackupScene.NoteSize),
        _                          => new SKFont(SkiaFonts.PlexRegular,  StackupScene.SpecSize),
    };
}
