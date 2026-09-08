using Avalonia.Input;
using CircuitRF.Ui.Layout;
using CircuitRF.Ui.ViewModels;
using Xunit;

namespace CircuitRF.Ui.Tests.Layout;

/// <summary>
/// Owner report, 2026-09-07: changing the layout editor's display unit with geometry ALREADY
/// selected left the Property Inspector showing the old unit's numbers.
///
/// <para>Every dimension row in the inspector is formatted through <c>_vm.DisplayUnit</c> and parsed
/// back through it on commit, but the panel only refreshed on the editor's <c>Overlay</c> and
/// <c>Technology</c> notifications — a unit change touched neither. The result was not merely stale
/// text: because a commit re-parses in the CURRENT unit, committing an untouched field afterwards
/// would have written 254 back as 254 <i>mil</i>, a factor of 25.4 out, with nothing on screen
/// suggesting anything had happened.</para>
///
/// <para>The fixture uses 254 µm on purpose — exactly 10 mil — so a conversion that runs is
/// unmistakable from one that does not, in both directions.</para>
/// </summary>
public class LayoutInspectorUnitChangeTests
{
    private static LayoutView FreshModel() => new()
    {
        DbuPerMicron = 1000,
        DisplayUnit  = LayoutUnit.Um,
        SnapDbu      = 1000,
    };

    private static (LayoutEditorViewModel Vm, LayoutShapePropertiesViewModel Props) Setup(LayoutView model)
    {
        var vm = new LayoutEditorViewModel(model) { ActiveTool = LayoutEditorViewModel.Tool.Select };
        var props = new LayoutShapePropertiesViewModel();
        props.SetContext(vm);
        return (vm, props);
    }

    private static void Click(LayoutEditorViewModel vm, double wx, double wy, long tolDbu = 40)
    {
        vm.OnPointerPressed(wx, wy, KeyModifiers.None, 1, tolDbu);
        vm.OnPointerReleased(wx, wy, KeyModifiers.None);
    }

    [Fact]
    public void RectFields_Reformat_WhenDisplayUnitChanges_WithTheShapeStillSelected()
    {
        var model = FreshModel();
        // 254 um x 508 um at (254, 0) um  =  10 mil x 20 mil at (10, 0) mil.
        model.Shapes.Add(new RectShape
        {
            Layer = new LayerKey(1, 0),
            X1 = 254_000, Y1 = 0, X2 = 508_000, Y2 = 508_000,
        });
        var (vm, props) = Setup(model);

        Click(vm, 400_000, 250_000);
        Assert.Equal("254", props.RectWidthText);
        Assert.Equal("508", props.RectHeightText);
        Assert.Equal("254", props.RectXText);
        Assert.Equal("0",   props.RectYText);

        vm.DisplayUnit = LayoutUnit.Mil;

        Assert.Equal("10", props.RectWidthText);
        Assert.Equal("20", props.RectHeightText);
        Assert.Equal("10", props.RectXText);
        Assert.Equal("0",  props.RectYText);

        // And back — the panel holds no unit state of its own, so this is symmetric.
        vm.DisplayUnit = LayoutUnit.Um;
        Assert.Equal("254", props.RectWidthText);
        Assert.Equal("508", props.RectHeightText);
    }

    [Fact]
    public void ACommitAfterAUnitChange_IsParsedInTheNewUnit_NotTheOldNumber()
    {
        // The consequence that made this more than a cosmetic bug: the field says 254 (um), the unit
        // becomes mil, and re-committing the field the user can see would have re-read that 254 as
        // mil — silently inflating a 254 um edge to 6.45 mm.
        var model = FreshModel();
        model.Shapes.Add(new RectShape { Layer = new LayerKey(1, 0), X1 = 0, Y1 = 0, X2 = 254_000, Y2 = 254_000 });
        var (vm, props) = Setup(model);

        Click(vm, 127_000, 127_000);
        vm.DisplayUnit = LayoutUnit.Mil;

        props.CommitRectWidthText(props.RectWidthText);   // commit exactly what is on screen

        var rect = (RectShape)model.Shapes[0];
        Assert.Equal(254_000, rect.X2 - rect.X1);         // unchanged: 10 mil IS 254 um
    }

    [Fact]
    public void VertexRows_Reformat_WhenDisplayUnitChanges()
    {
        var model = FreshModel();
        model.Shapes.Add(new PolygonShape
        {
            Layer = new LayerKey(1, 0),
            Xy = [0, 0, 254_000, 0, 254_000, 254_000],
        });
        var (vm, props) = Setup(model);

        Click(vm, 200_000, 50_000);
        Assert.True(props.ShowVertexList);
        // Row 0 is the ring header; row 2 is the polygon's SECOND vertex, at x = 254 um.
        var row = (VertexRowViewModel)props.VertexRows![2];
        Assert.Equal("254", row.XText);

        vm.DisplayUnit = LayoutUnit.Mil;
        Assert.Equal("10", ((VertexRowViewModel)props.VertexRows![2]).XText);
    }

    [Fact]
    public void AFieldTheUserIsEditing_IsLeftAlone_ExactlyAsOnAnyOtherRefresh()
    {
        // The focus guard is the ordinary SetTextIfNotFocused one: a unit change must not overwrite
        // a half-typed value out from under the caret, any more than a model change does.
        var model = FreshModel();
        model.Shapes.Add(new RectShape { Layer = new LayerKey(1, 0), X1 = 0, Y1 = 0, X2 = 254_000, Y2 = 254_000 });
        var (vm, props) = Setup(model);

        Click(vm, 127_000, 127_000);
        props.SetFocusedField("RectWidth");
        props.RectWidthText = "12";        // mid-edit

        vm.DisplayUnit = LayoutUnit.Mil;

        Assert.Equal("12", props.RectWidthText);   // untouched
        Assert.Equal("10", props.RectHeightText);  // every unfocused row still converted
    }
}
