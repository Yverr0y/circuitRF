using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace CircuitRF.Ui.Controls;

/// <summary>
/// Two children on one row: a CONTENT block that gives up width first, and a TRAILING block that
/// gives up none and runs off the right edge instead. The trailing block hugs the right edge while
/// there is room for it, and is never drawn over the content.
/// </summary>
/// <remarks>
/// <b>No built-in panel does this, and the two obvious candidates fail in opposite directions.</b>
/// The <c>.cem</c> header is the case that needed it: an identity block whose two file names
/// ellipsize, and a Mesh/Simulate cluster of fixed-size buttons.
///
/// <para>A <c>Grid</c> with <c>ColumnDefinitions="*,Auto"</c> gets the PRIORITY right — the star
/// column yields, the buttons keep their size — but it positions the buttons at their COLUMN's edge.
/// Once the star column has collapsed to zero, a content block with a hard minimum of its own
/// overflows its cell and the buttons are laid out on top of it. That is what was reported (owner,
/// 2026-09-09: the buttons render over the Output file box).</para>
///
/// <para>A <c>DockPanel</c> with the content docked <c>Left</c> positions the buttons after the
/// content's ACTUAL width, so they can never overlap it — but it inverts the priority: it offers the
/// left child everything, so the content keeps its natural width and the BUTTONS are squeezed away
/// first. Measured on the real header, at a 1000 px window: the name still rendered at its full
/// 672 px while the button cluster had been cut to 16 px of its 574. The name has to ellipsize
/// first; the buttons have to be the last thing to go.</para>
///
/// <para><b>Shrinking the fields instead was tried and does not answer it</b> (owner suggestion,
/// same day). The Output-file <c>TextBox</c> is the only field in that row with any give, and even
/// pinned at its 20 px <c>MinWidth</c> the row still has a ~162 px floor it cannot go below — the
/// "Output file:" label and the "…" picker are both fixed — so the buttons still land on top of the
/// row, just at a narrower window. It also costs the box its stable resting size: without the fixed
/// <c>Width</c> its width becomes content-driven, and a blank path (which is the common case, since
/// blank means "the default") renders as a 20 px stub. Both measured before this panel was kept.</para>
///
/// <para>So: measure the trailing block unconstrained, offer the content what is left, and place the
/// trailing block at whichever is further right — the panel's own right edge, or the content's. Each
/// rule is one line and each is named below.</para>
///
/// <para><b>The content's own floor has to be DECLARED, because Avalonia will not report it.</b>
/// <c>Layoutable</c> clamps <c>DesiredSize</c> to the size it was offered — measured directly, and it
/// clamps a <c>MinWidth</c> too (a Border with <c>MinWidth=300</c> offered 100 px reports 100). So a
/// block that cannot shrink past a fixed label and a fixed-width text box still reports whatever it
/// was offered, and a parent positioning anything after it lands on top of it. Nor can the floor be
/// discovered by re-measuring: for every offer between the floor and the natural width, this content
/// legitimately wants exactly what it is given, so a probing measure saturates all the way up to the
/// natural width and never reveals the knee. The content therefore states its own floor as
/// <see cref="Layoutable.MinWidth"/>, and this panel READS it — the one place a number lives, on the
/// block it describes. It is a floor, so it fails gracefully in both directions: set too low, a
/// little of the old overlap returns at extreme widths; set too high, the trailing block leaves the
/// window slightly early.</para>
///
/// <para>Exactly two visible children, content first. A third is a mistake rather than a case worth
/// defining, so it throws instead of being silently ignored.</para>
/// </remarks>
public sealed class ShrinkThenOverflowPanel : Panel
{
    /// <summary>
    /// What the content block is offered: everything except what the trailing block needs.
    ///
    /// <para>This is the half a <c>DockPanel</c> gets wrong. Offering the content the FULL width lets
    /// it keep its natural size and pushes the trailing block out first; offering it the remainder
    /// makes it ellipsize first, which is the order a user wants — a name still recognisable
    /// truncated, against buttons that are either there or not.</para>
    /// </summary>
    internal static double ContentConstraint(double available, double trailingWidth, double contentFloor)
    {
        if (double.IsInfinity(available)) return available;

        // Never offer less than the content's own floor: below it the offer only buys a clamped
        // DesiredSize that hides how wide the content really is.
        return Math.Max(contentFloor, Math.Max(0, available - trailingWidth));
    }

    /// <summary>
    /// Where the trailing block starts: hugging the right edge, or immediately after the content when
    /// the content could not shrink far enough to leave room.
    ///
    /// <para>This is the half a <c>Grid</c> gets wrong. <c>finalWidth - trailingWidth</c> alone is
    /// the right edge, and is what puts the buttons under the content once the content has a floor of
    /// its own; the <c>Math.Max</c> against <paramref name="contentWidth"/> turns that overlap into an
    /// overflow past the right edge, where the window clips it instead.</para>
    /// </summary>
    internal static double TrailingLeft(double finalWidth, double contentWidth, double trailingWidth,
                                       double contentFloor) =>
        Math.Max(Math.Max(contentWidth, contentFloor), finalWidth - trailingWidth);

    /// <summary>The content's declared floor, including the margin it is laid out with — the panel
    /// positions against outer edges, and <see cref="Layoutable.MinWidth"/> is an inner
    /// measurement.</summary>
    private static double ContentFloor(Control content)
    {
        double min = content.MinWidth;
        if (!double.IsFinite(min) || min <= 0) return 0;
        return min + content.Margin.Left + content.Margin.Right;
    }

    private (Control Content, Control Trailing) Pair()
    {
        var visible = new List<Control>();
        foreach (var child in Children)
            if (child is { IsVisible: true } c) visible.Add(c);

        if (visible.Count != 2)
            throw new InvalidOperationException(
                $"{nameof(ShrinkThenOverflowPanel)} takes exactly two visible children — the content "
                + $"block first, then the trailing block — and was given {visible.Count}.");

        return (visible[0], visible[1]);
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        var (content, trailing) = Pair();

        // Unconstrained: the trailing block's size is a fact this panel arranges around, never
        // something it negotiates.
        trailing.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        double trailingWidth = trailing.DesiredSize.Width;

        double floor = ContentFloor(content);
        content.Measure(new Size(ContentConstraint(availableSize.Width, trailingWidth, floor),
                                 availableSize.Height));

        // The panel asks for both side by side. When that is more than the parent has, the parent
        // arranges it at the smaller width and TrailingLeft decides who overflows.
        return new Size(Math.Max(content.DesiredSize.Width, floor) + trailingWidth,
                        Math.Max(content.DesiredSize.Height, trailing.DesiredSize.Height));
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var (content, trailing) = Pair();

        double floor = ContentFloor(content);
        double contentWidth = Math.Max(content.DesiredSize.Width, floor);
        double trailingWidth = trailing.DesiredSize.Width;

        content.Arrange(new Rect(0, 0, contentWidth, finalSize.Height));
        trailing.Arrange(new Rect(TrailingLeft(finalSize.Width, contentWidth, trailingWidth, floor), 0,
                                  trailingWidth, finalSize.Height));
        return finalSize;
    }
}
