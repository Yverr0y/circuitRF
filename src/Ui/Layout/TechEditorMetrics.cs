using Avalonia.Controls;

namespace CircuitRF.Ui.Layout;

/// <summary>
/// The Stackup tab's sizes, in one place, because the view and its tests must agree about them and a
/// number typed into the .axaml agrees with nothing.
///
/// <para><b>Two kinds of number live here and they are not equally binding.</b></para>
///
/// <para><b>Pinned.</b> <see cref="DrawingLayerRowHeight"/>, <see cref="DrawingLayerPickerMaxHeight"/>
/// and the two pane minimums are read by <c>TechEditorView.axaml</c> through <c>x:Static</c>, so what
/// is written here is what the control does. R-stk2-8 asks for a picker whose height admits
/// <i>exactly</i> four rows — which is only true if the row height is OURS rather than whatever the
/// theme's CheckBox happens to measure, so the picker's item style pins it from
/// <see cref="DrawingLayerRowHeight"/> and the ListBox's MaxHeight is four of them by arithmetic
/// rather than by a typed 88.</para>
///
/// <para><b>Nominal.</b> <see cref="ConductorCardHeight"/> is a sum over the conductor card's own
/// template — its four field rows, the spacing between them, the border chrome, and the
/// drawing-layer block — and the field rows are sized by the theme, not by us. It is an estimate,
/// and it is allowed to be: its only job is R-stk2-6's OPENING split, which is a starting position
/// a user drags off in a second. <b>It tracks the card template</b> — add a row to the conductor
/// card and <see cref="ConductorCardFieldRows"/> is what changes, not a magic 280 somewhere that
/// stops matching and never says so.</para>
/// </summary>
public static class TechEditorMetrics
{
    // ── The conductor card's drawing-layer picker (R-stk2-8) ──────────────────────────────────────

    /// <summary>One picker row. PINNED by the picker's own item style — see the class remarks for
    /// why the theme's own CheckBox height would not do.</summary>
    public const double DrawingLayerRowHeight = 22;

    /// <summary>Three across, which is what turns four rows into twelve visible layers.</summary>
    public const int DrawingLayerPickerColumns = 3;

    /// <summary>Four down. The owner's number: "a height to only support 4 elements".</summary>
    public const int DrawingLayerPickerRows = 4;

    /// <summary>Exactly <see cref="DrawingLayerPickerRows"/> rows, by arithmetic.</summary>
    public const double DrawingLayerPickerMaxHeight = DrawingLayerRowHeight * DrawingLayerPickerRows;

    /// <summary>What the single-column picker used before this brief: a typed 140, which admitted
    /// four rows of the theme's ~35 px CheckBox. Recorded so R-stk2-9's "the card got shorter" is an
    /// assertion about two stated numbers rather than about a memory of the old file.</summary>
    public const double DrawingLayerPickerMaxHeightBefore = 140;

    // ── The conductor card (R-stk2-9) ─────────────────────────────────────────────────────────────

    /// <summary>One labelled field row — a <c>Classes="cell"</c> TextBox or a ComboBox beside its
    /// label. Nominal: the theme sizes these.</summary>
    public const double CardFieldRowHeight = 26;

    /// <summary>The card StackPanel's own <c>Spacing</c>.</summary>
    public const double CardRowSpacing = 4;

    /// <summary>The card Border's <c>Margin="0,3"</c> + <c>Padding="8,6"</c> + 1 px edge, both
    /// edges: 2 × (3 + 6 + 1).</summary>
    public const double CardChrome = 20;

    /// <summary>The rows a CONDUCTOR card shows: name, thickness, σ, "Metal thickness goes to".
    /// The drawing-layer block below them is counted separately because it is not a field row.</summary>
    public const int ConductorCardFieldRows = 4;

    /// <summary>The drawing-layer block's own <c>Spacing</c>.</summary>
    public const double DrawingLayerBlockSpacing = 3;

    /// <summary>The "Metal 1, Metal 2 …" line above the filter box.</summary>
    public const double DrawingLayerSummaryHeight = 20;

    /// <summary>The "Filter layers…" box, which R-stk2-8 keeps.</summary>
    public const double DrawingLayerFilterHeight = 26;

    /// <summary>Summary + filter + picker, with the block's two gaps.</summary>
    public const double DrawingLayerBlockHeight =
        DrawingLayerSummaryHeight + DrawingLayerFilterHeight + DrawingLayerPickerMaxHeight
        + 2 * DrawingLayerBlockSpacing;

    /// <summary>One conductor card, nominally — see the class remarks. Five children in the card's
    /// StackPanel means four gaps.</summary>
    public const double ConductorCardHeight =
        CardChrome
        + ConductorCardFieldRows * CardFieldRowHeight
        + ConductorCardFieldRows * CardRowSpacing
        + DrawingLayerBlockHeight;

    /// <summary>R-stk2-9's stated ceiling. It sits between the card as it is now and the card as it
    /// was with the taller picker, so it fails if the picker grows back.</summary>
    public const double ConductorCardHeightCeiling = 320;

    // ── The tab's two panes (R-stk2-6) ────────────────────────────────────────────────────────────

    /// <summary>The card list opens at roughly one conductor card — the owner's "perhaps only as
    /// tall as what a conductor entry is currently using". A GridLength because that is what a
    /// <c>RowDefinition.Height</c> takes; the drawing takes the rest of the tab.</summary>
    public static GridLength StackupCardPaneOpeningHeight { get; } = new(ConductorCardHeight);

    /// <summary>Neither pane can be annihilated by the splitter. Enough to keep the filter box and
    /// the top of one card reachable.</summary>
    public const double StackupCardPaneMinHeight = 96;

    /// <summary>Enough drawing to be worth having: the top boundary note, a band or two, and the
    /// bottom note.</summary>
    public const double StackupDrawingMinHeight = 120;
}
