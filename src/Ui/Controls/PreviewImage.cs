using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CircuitRF.Ui.Controls;

/// <summary>
/// Draws one already-rendered bitmap across the whole of its own bounds — <b>an explicit source rect
/// to an explicit destination rect, and nothing else</b>.
///
/// <para><b>Why this exists instead of an <see cref="Image"/>.</b> An Image decides where the bitmap
/// lands from three things at once: the bitmap's own <see cref="Bitmap.Size"/> (which is its pixel
/// size divided by whatever DPI it was stamped with), the <see cref="Stretch"/> mode, and the size the
/// layout gives the control. Any one of those being off by a factor produces a picture that is
/// simultaneously blurry (upscaled), letterboxed (so it reads as off-centre) and — when the factor
/// goes the other way — clipped. All three were reported together for the import preview, which is
/// the signature of exactly this negotiation going wrong rather than three separate faults (owner,
/// 2026-09-05, after two fixes that each corrected the transform and changed nothing).</para>
///
/// <para>Here there is no negotiation. The destination is <c>Bounds</c>, so the drawing always fills
/// the control exactly: it cannot be letterboxed and it cannot overflow. The source is the whole
/// bitmap, addressed in its own pixels, so DPI never enters into it. Sharpness then depends on ONE
/// number — how many pixels the caller rendered — which is the only thing that should have controlled
/// it in the first place.</para>
///
/// <para>The caller renders at the control's logical size times its supersample factor, so the blit
/// is 1:1 against physical pixels on a 2x display and a clean downsample on a 1x one. The bitmap is
/// rendered at this control's own aspect ratio, so filling it introduces no distortion; the one
/// moment the two disagree is the instant between a resize and its re-raster.</para>
/// </summary>
public sealed class PreviewImage : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<PreviewImage, Bitmap?>(nameof(Source));

    /// <summary>
    /// Rounds the drawing's own corners, background and all.
    ///
    /// <para>The raster paints the editor's background across the whole surface, so without this the
    /// pane is a hard-cornered rectangle sitting among controls that are not — the buttons in the same
    /// dialog are 3 (<c>CircuitRfStyles.axaml</c>, <c>Button.seg-btn</c>), and matching them is the
    /// point. Applied as a CLIP rather than by rounding the raster: the renderers draw a rectangle
    /// because that is what a viewport is, and the shape it is presented in is this control's
    /// business.</para>
    /// </summary>
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<PreviewImage, CornerRadius>(nameof(CornerRadius), new CornerRadius(3));

    /// <inheritdoc cref="CornerRadiusProperty"/>
    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    static PreviewImage()
    {
        AffectsRender<PreviewImage>(SourceProperty, CornerRadiusProperty);
    }

    public override void Render(DrawingContext context)
    {
        if (Source is not { } bitmap) return;
        if (Bounds.Width < 1 || Bounds.Height < 1) return;

        var pixels = bitmap.PixelSize;
        if (pixels.Width < 1 || pixels.Height < 1) return;

        var dest = new Rect(Bounds.Size);

        // Source addressed in the bitmap's OWN Size units — which is what DrawImage's srcRect means,
        // and the one place the DPI stamp still shows through. Taking the whole of it either way.
        using (context.PushClip(new RoundedRect(dest, CornerRadius)))
            context.DrawImage(bitmap, new Rect(bitmap.Size), dest);
    }
}
