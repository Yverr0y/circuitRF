using System.Linq;
using Avalonia.Input;
using CircuitRF.Render;
using CircuitRF.Ui.Layout;
using CircuitRF.Ui.Theming;

namespace CircuitRF.Ui.Tests.Layout;

/// <summary>
/// <b>A port commits to a conductor at a GESTURE, and is pinned to it at rest.</b> Two owner
/// requirements (2026-09-09) that pull in opposite directions, and the tests that hold both:
///
/// <list type="number">
/// <item>A placed port must NEVER move on its own — so its marker may not depend on which layers
/// happen to be switched on.</item>
/// <item>Dragging a port must not attract it to layers whose visibility is switched off — so a drag
/// may not see metal that is not on screen.</item>
/// </list>
///
/// <para>Resolving the conductor against VISIBLE artwork every time breaks (1); resolving it against
/// ALL artwork breaks (2). Both were shipped in that order and each broke the other. The rule that
/// holds both is that they are questions about different MOMENTS —
/// <see cref="LayoutPortDirection.LookupFor(LayoutView, Technology?, string, long)"/> states it
/// once — and <see cref="LabelShape.PortLayer"/> is what carries the commitment between them.</para>
///
/// <para>A third report is here too: a port's orientation did not update while it was being dragged,
/// only once the mouse was released.</para>
/// </summary>
public class LayoutPortStableUnderLayerVisibilityTests
{
    private static LayerDef Layer(int layer, int zOrder, bool visible = true, bool selectable = true) => new()
    {
        Key = new LayerKey(layer, 0),
        Name = $"L{layer}",
        Color = new Rgba(100, 100, 100),
        ZOrder = zOrder,
        Visible = visible,
        Selectable = selectable,
    };

    private static Technology Tech(bool innerVisible) =>
        new() { Layers = [Layer(1, 0), Layer(2, 100, visible: innerVisible)] };

    /// <summary>A trace on layer 1 (low ZOrder), crossing a much larger pour on layer 2 (high
    /// ZOrder). That is the reporting board's own shape — a signal trace over an inner-layer
    /// pour — and it is the arrangement in which BOTH defects appear.</summary>
    private static LayoutView TraceOverPour()
    {
        var view = new LayoutView();
        // The trace: x 0..100_000, y 40_000..60_000 — a 20k-wide low-x end at y = 50_000.
        view.Shapes.Add(new RectShape { Layer = new LayerKey(1, 0), X1 = 0, Y1 = 40_000, X2 = 100_000, Y2 = 60_000 });
        // The pour: bigger in every direction, so every quantity a port reads differs from the trace's.
        view.Shapes.Add(new RectShape { Layer = new LayerKey(2, 0), X1 = -50_000, Y1 = -50_000, X2 = 150_000, Y2 = 150_000 });
        return view;
    }

    private static LabelShape PortAtTraceEnd() => new()
    {
        Layer = new LayerKey(1, 0), X = 2_000, Y = 50_000, Text = "P1", Height = 5_000,
        IsPort = true, PortLayer = new LayerKey(1, 0),   // committed, as the Port tool stamps it
    };

    // ── (1) A committed port does not move ────────────────────────────────────────────────────

    [Fact]
    public void CommittedPort_IsIdentical_WhetherTheOtherLayerIsShownOrHidden()
    {
        var view = TraceOverPour();
        var port = PortAtTraceEnd();

        var before = LayoutPortDirection.Resolve(LayoutPortDirection.LookupFor(view, Tech(false), ""), port);
        var after  = LayoutPortDirection.Resolve(LayoutPortDirection.LookupFor(view, Tech(true),  ""), port);

        Assert.NotNull(before);
        Assert.Equal(before, after);

        // ...and it is the TRACE it measures, in both states. Stability alone would be satisfied by
        // resolving to the pour every time, which is the wrong conductor.
        Assert.Equal(0, before!.Value.PlaneX);        // the trace's low-x end
        Assert.Equal(50_000, before!.Value.PlaneY);
        Assert.Equal(20_000, before!.Value.WidthDbu); // the trace's width, not the pour's 200_000
    }

    [Fact]
    public void CommittedPort_SurvivesEvenItsOwnLayerBeingHidden()
    {
        // The strong form of (1): hiding the layer a port is committed to must not move it either.
        // Only the marker's VISIBILITY is a view question; its geometry is not.
        var view = TraceOverPour();
        var port = PortAtTraceEnd();

        var shown  = new Technology { Layers = [Layer(1, 0), Layer(2, 100)] };
        var hidden = new Technology { Layers = [Layer(1, 0, visible: false), Layer(2, 100)] };

        Assert.Equal(LayoutPortDirection.Resolve(LayoutPortDirection.LookupFor(view, shown,  ""), port),
                     LayoutPortDirection.Resolve(LayoutPortDirection.LookupFor(view, hidden, ""), port));
    }

    // ── (2) A gesture is never attracted to metal it cannot see ───────────────────────────────

    [Fact]
    public void AGesture_FindsNothing_WhereOnlyAHiddenLayerCarriesMetal()
    {
        var view = TraceOverPour();
        // (120_000, 120_000) is inside the pour and well clear of the trace.
        var gated = LayoutPortDirection.LookupFor(view, Tech(false), "");

        Assert.Null(gated(120_000, 120_000, onLayer: null));                       // pour hidden
        Assert.Equal(2, LayoutPortDirection.LookupFor(view, Tech(true), "")
                                           (120_000, 120_000, onLayer: null)!.Value.Shape!.Layer.Layer);
    }

    [Fact]
    public void AGesture_PrefersTheSmallestConductor_NotTheTopmost()
    {
        // ZOrder-descending — a CLICK's ordering — would return the pour (ZOrder 100) over the trace
        // (ZOrder 0) even with both visible, which is how a port on a trace came to measure a plane.
        var info = LayoutPortDirection.LookupFor(TraceOverPour(), Tech(true), "")(2_000, 50_000, onLayer: null);
        Assert.Equal(1, info!.Value.Shape!.Layer.Layer);
    }

    /// <summary>The renderer and the editor build the full lookup; the clipboard and
    /// <c>DocumentExtents</c> build the shapes-only one. A port that measured a different conductor
    /// depending on which was asked would export at a size it never drew at.</summary>
    [Fact]
    public void FullLookup_AgreesWithTheShapesOnlyLookup()
    {
        var view = TraceOverPour();
        var port = PortAtTraceEnd();

        Assert.Equal(LayoutPortDirection.Resolve(view.Shapes, port),
                     LayoutPortDirection.Resolve(LayoutPortDirection.LookupFor(view, Tech(true), ""), port));
    }

    // ── (3) The arrow turns DURING the drag, not on release ───────────────────────────────────

    /// <summary>A short, WIDE pad: a port near its left edge faces +x̂ (R0), and one near its bottom
    /// edge faces +ŷ (R90). Dragging from one face to the other is the gesture the owner reported —
    /// "you have to release the mouse before the arrow turns".</summary>
    private static (LayoutEditorViewModel Vm, LayoutView Model, int PortIndex) PadWithAPortOnIt()
    {
        var model = new LayoutView { DbuPerMicron = 1000, DisplayUnit = LayoutUnit.Um, SnapDbu = 0 };
        model.Shapes.Add(new RectShape { Layer = new LayerKey(1, 0), X1 = 0, Y1 = 0, X2 = 100_000, Y2 = 100_000 });
        model.Shapes.Add(new LabelShape
        {
            Layer = new LayerKey(1, 0), X = 1_000, Y = 50_000, Text = "P1", Height = 5_000,
            IsPort = true, PortDirection = LayoutRotation.R0, PortLayer = new LayerKey(1, 0),
        });
        var vm = new LayoutEditorViewModel(model)
        {
            ActiveTool = LayoutEditorViewModel.Tool.Select,
            Technology = Tech(true),
        };
        return (vm, model, 1);
    }

    [Fact]
    public void DraggingAPort_TurnsTheArrowLive_BeforeTheMouseIsReleased()
    {
        var (vm, model, portIndex) = PadWithAPortOnIt();
        vm.OnPointerPressed(1_000, 50_000, KeyModifiers.None, 1, 40);
        Assert.Contains(portIndex, vm.SelectedIndices);

        // Still DOWN — drag to the pad's bottom face, which names a different side.
        vm.OnPointerMoved(50_000, 1_000, true, KeyModifiers.None, 40);

        var ghost = Assert.IsType<LabelShape>(vm.Overlay.DragOverrides[portIndex]);
        Assert.Equal(LayoutRotation.R90, ghost.PortDirection);
        Assert.Equal(LayoutRotation.R0, ((LabelShape)model.Shapes[portIndex]).PortDirection); // model untouched

        // ...and what was previewed is what gets written.
        vm.OnPointerReleased(50_000, 1_000, KeyModifiers.None);
        Assert.Equal(LayoutRotation.R90, ((LabelShape)model.Shapes[portIndex]).PortDirection);
    }

    [Fact]
    public void ADragOntoAnotherLayersMetal_ReCommitsThePortToThatLayer()
    {
        var (vm, model, portIndex) = PadWithAPortOnIt();
        // A smaller pad on layer 2, clear of the first.
        model.Shapes.Add(new RectShape { Layer = new LayerKey(2, 0), X1 = 200_000, Y1 = 0, X2 = 240_000, Y2 = 40_000 });
        model.NotifyChanged();

        vm.OnPointerPressed(1_000, 50_000, KeyModifiers.None, 1, 40);
        vm.OnPointerMoved(201_000, 20_000, true, KeyModifiers.None, 40);
        vm.OnPointerReleased(201_000, 20_000, KeyModifiers.None);

        var port = (LabelShape)model.Shapes[portIndex];
        Assert.Equal(new LayerKey(2, 0), port.PortLayer);

        // Undo restores the commitment along with the move — it rides the same entry.
        vm.UndoCommand.Execute(null);
        Assert.Equal(new LayerKey(1, 0), ((LabelShape)model.Shapes[portIndex]).PortLayer);
    }

    [Fact]
    public void PortLayer_IsOmittedFromTheFile_WhenNull()
    {
        var view = new LayoutView();
        view.Shapes.Add(new LabelShape { X = 0, Y = 0, Text = "P1", Height = 1000, IsPort = true });
        Assert.DoesNotContain("PortLayer", LayoutPersistence.Serialize(view));

        view.Shapes.Add(new LabelShape
        {
            X = 0, Y = 0, Text = "P2", Height = 1000, IsPort = true, PortLayer = new LayerKey(7, 3),
        });
        var round = LayoutPersistence.Deserialize(LayoutPersistence.Serialize(view));
        Assert.Null(((LabelShape)round.Shapes[0]).PortLayer);
        Assert.Equal(new LayerKey(7, 3), ((LabelShape)round.Shapes[1]).PortLayer);
    }
}
