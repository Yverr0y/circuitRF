using CircuitRF.Ui.Controls;
using Xunit;

namespace CircuitRF.Ui.Tests.Layout;

/// <summary>
/// <see cref="ShrinkThenOverflowPanel"/>'s two rules, asserted directly.
///
/// <para>Owner report, 2026-09-09: at a small enough width the .cem header's Mesh/Simulate cluster
/// renders on top of the Output file box, and the buttons should leave the window instead. The panel
/// exists because neither built-in panel can do both halves — a <c>Grid</c> shrinks the right thing
/// and positions the buttons at their column's edge (on top of a content block that could not shrink
/// that far); a <c>DockPanel</c> positions them correctly and squeezes the buttons away first.</para>
///
/// <para><b>Measure/Arrange itself needs a platform this test project does not have</b> (no font
/// manager, so nothing containing text can be measured — the same reason
/// <c>DockWindowBehaviourTests</c> pins mechanisms rather than geometry). Both rules are therefore
/// pure functions of three numbers, which is what is checked here; the panel wired into the real
/// header was measured separately, headless, and the figures are in <c>src/Ui/RESOLVED.md</c>.</para>
/// </summary>
public sealed class ShrinkThenOverflowPanelTests
{
    private const double Floor = 282;      // the .cem header identity block's own declared floor
    private const double Buttons = 290;    // a plausible Mesh/Simulate cluster

    /// <summary>Room for both: the content is offered everything the buttons do not need, so it
    /// ellipsizes before they are touched. This is the half a DockPanel gets wrong.</summary>
    [Fact]
    public void TheContentIsOfferedWhatTheButtonsDoNotNeed()
    {
        Assert.Equal(710, ShrinkThenOverflowPanel.ContentConstraint(1000, Buttons, Floor));
    }

    /// <summary>Below the content's floor the offer stops falling. Offering less only buys a
    /// DesiredSize clamped to the offer, which is exactly the reading that hides how wide the content
    /// really is.</summary>
    [Theory]
    [InlineData(400)]   // available - buttons = 110, under the floor
    [InlineData(300)]
    [InlineData(0)]
    public void TheOfferNeverFallsBelowTheContentsFloor(double available)
    {
        Assert.Equal(Floor, ShrinkThenOverflowPanel.ContentConstraint(available, Buttons, Floor));
    }

    /// <summary>An infinite offer passes straight through — a panel measuring for its own desired
    /// size must not be handed a finite number invented here.</summary>
    [Fact]
    public void AnInfiniteOfferIsPassedThrough()
    {
        Assert.Equal(double.PositiveInfinity,
            ShrinkThenOverflowPanel.ContentConstraint(double.PositiveInfinity, Buttons, Floor));
    }

    /// <summary>With room to spare the buttons hug the right edge, exactly as they did when this was
    /// a Grid — the fix may not cost the arrangement it was reported against.</summary>
    [Fact]
    public void WithRoomToSpareTheButtonsHugTheRightEdge()
    {
        Assert.Equal(710, ShrinkThenOverflowPanel.TrailingLeft(1000, contentWidth: 500,
                                                               trailingWidth: Buttons, contentFloor: Floor));
    }

    /// <summary>
    /// Once the content is at its floor the buttons start after it — PAST the right edge, where the
    /// window clips them — rather than on top of it. This is the reported bug, in one number: a Grid
    /// would put them at <c>finalWidth - trailingWidth</c>, which is 110 here, i.e. 172 px inside the
    /// content.
    /// </summary>
    [Theory]
    [InlineData(400)]
    [InlineData(300)]
    [InlineData(200)]
    public void OnceTheContentIsAtItsFloorTheButtonsGoPastTheRightEdge(double finalWidth)
    {
        double left = ShrinkThenOverflowPanel.TrailingLeft(finalWidth, contentWidth: Floor,
                                                           trailingWidth: Buttons, contentFloor: Floor);

        Assert.Equal(Floor, left);                       // immediately after the content, never inside it
        Assert.True(left + Buttons > finalWidth,
            $"the buttons fit inside {finalWidth} px, so this case is not the overflow one");
    }

    /// <summary>The floor wins even if a caller passes a content width below it — the two are read
    /// from different places (a measured DesiredSize and a declared MinWidth) and only one of them
    /// can be trusted not to have been clamped.</summary>
    [Fact]
    public void TheFloorWinsOverAClampedContentWidth()
    {
        Assert.Equal(Floor, ShrinkThenOverflowPanel.TrailingLeft(400, contentWidth: 110,
                                                                 trailingWidth: Buttons, contentFloor: Floor));
    }

    /// <summary>No floor declared: the panel behaves as a plain right-aligning row, and a content
    /// block wider than the space still pushes the buttons out rather than under itself.</summary>
    [Fact]
    public void WithNoFloorItStillNeverPlacesTheButtonsInsideTheContent()
    {
        Assert.Equal(500, ShrinkThenOverflowPanel.TrailingLeft(600, contentWidth: 500,
                                                               trailingWidth: Buttons, contentFloor: 0));
    }
}
