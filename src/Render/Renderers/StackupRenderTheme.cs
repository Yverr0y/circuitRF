using SkiaSharp;

namespace CircuitRF.Render;

/// <summary>
/// SKColor token bundle for the stackup cross-section's chrome — on
/// <see cref="LayoutRenderTheme"/>'s exact pattern, and for the same reason: a renderer's colours
/// are a PROJECTION of the active theme, never hardcoded (docs/design/color-themes.md, L2).
///
/// <para><b>The band and barrel fills are deliberately not here.</b> A conductor's fill is the colour
/// of the drawing layer it is bound to (R-stk1-5) — literal <c>Rgba</c> on <c>LayerDef</c>, carried
/// on <see cref="StackupBand.Fill"/> and converted per band by <see cref="StackupRenderer"/>, exactly
/// as <c>LayoutRenderer</c> does it. A role for them would be a second answer to a question the
/// technology has already answered.</para>
/// </summary>
public sealed class StackupRenderTheme
{
    public SKColor Background      { get; init; }
    public SKColor BandEdge        { get; init; }
    public SKColor DielectricFill  { get; init; }

    /// <summary>The ground reference's heavy edge, and the accent text that names it.</summary>
    public SKColor GroundAccent    { get; init; }

    /// <summary>Every label outside a band.</summary>
    public SKColor LabelInk        { get; init; }

    /// <summary>A band's own name, which sits ON the band. Fixed dark in BOTH variants — see
    /// <see cref="ColorRole.StackupOnBandInk"/> for the bug that rule exists to prevent.</summary>
    public SKColor OnBandInk       { get; init; }

    public SKColor Gripper         { get; init; }

    /// <summary>Brief 5's drag destination. Carries its own alpha.</summary>
    public SKColor DragGhost       { get; init; }

    /// <summary>A via the drawing could not place. <see cref="ColorRole.SystemWarning"/>, not a role
    /// of its own — this is exactly what that role is for.</summary>
    public SKColor Refusal         { get; init; }

    /// <summary>brief 3's selection outline. <see cref="ColorRole.LayoutSelection"/> deliberately, so
    /// a selected band and a selected shape in the layout editor are the same colour.</summary>
    public SKColor Selection       { get; init; }

    public static StackupRenderTheme FromTheme(ColorTheme theme, ColorVariant variant)
    {
        ArgumentNullException.ThrowIfNull(theme);

        SKColor SK(string role)
        {
            var c = theme.Resolve(role, variant);
            return new SKColor(c.R, c.G, c.B, c.A);
        }

        return new StackupRenderTheme
        {
            Background     = SK(ColorRole.StackupBackground),
            BandEdge       = SK(ColorRole.StackupBandEdge),
            DielectricFill = SK(ColorRole.StackupDielectricFill),
            GroundAccent   = SK(ColorRole.StackupGroundAccent),
            LabelInk       = SK(ColorRole.StackupLabelInk),
            OnBandInk      = SK(ColorRole.StackupOnBandInk),
            Gripper        = SK(ColorRole.StackupGripper),
            DragGhost      = SK(ColorRole.StackupDragGhost),
            Refusal        = SK(ColorRole.SystemWarning),
            Selection      = SK(ColorRole.LayoutSelection),
        };
    }

    /// <summary>A sensible default so a canvas can render before a theme is wired up — the BUILT-IN
    /// light palette, not a private copy of it, so "the fallback" and "the shipped theme" cannot
    /// drift apart the way <c>WBondRenderTheme</c>'s had.</summary>
    public static StackupRenderTheme Fallback { get; } =
        FromTheme(ColorTheme.BuiltIn, ColorVariant.Light);

    public static StackupRenderTheme Light { get; } = FromTheme(ColorTheme.BuiltIn, ColorVariant.Light);
    public static StackupRenderTheme Dark  { get; } = FromTheme(ColorTheme.BuiltIn, ColorVariant.Dark);
}
