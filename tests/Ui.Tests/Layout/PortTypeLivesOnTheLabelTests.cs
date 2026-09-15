// Owner report, 2026-09-14: placing a port in the middle of a 50 Ω line trace with geometry snapping
// turned OFF drew an internal port under the cursor, but the click produced something that looked
// like an edge port somewhere else. A click has to land what the ghost showed.
//
// NOTHING WAS SNAPPING. Measured on the reporting file, with geometry snap off, the committed label
// landed on exactly the ghost's anchor, to the DBU. What moved was the PICTURE, one frame later.
//
// The port's TYPE lived in the `.cem` (`EmSetup.PortKinds`), and the method that read it answered
// `Edge` for any slot it had never been told about — which is every port in a setup whose list is
// empty, the normal case and what every `.cem` written before port types existed carries. So the
// open EM setup re-extracted after the click and republished the brand-new port as an edge port; the
// renderer then drew the reference-plane bar at `PortHint.PlaneX`, the conductor's END FACE, 1.9 mm
// away at the far end of the trace. The run agreed with the wrong half.
//
// Silence is not an assertion. The type is `LabelShape.PortKind` now, stamped by the tool at
// placement exactly as `PortDirection` already was, and read through the one call everything makes.

using Avalonia.Input;
using CircuitRF.Engine.Mom;
using CircuitRF.Ui.Layout;

namespace CircuitRF.Ui.Tests.Layout;

public class PortTypeLivesOnTheLabelTests
{
    private const int Dbu = LayoutUnits.DefaultDbuPerMicron;
    private static readonly LayerKey TopCopper = new(1, 0);

    private static long Mm(double mm) => (long)Math.Round(mm * 1000 * Dbu);
    private const long Tol = 300 * Dbu;

    /// <summary>A 20 x 2.9 mm run of metal — the shape of the reporting file's 50 Ω trace.</summary>
    private static LayoutEditorViewModel Fixture(bool geometrySnap = false)
    {
        var view = new LayoutView { DbuPerMicron = Dbu, DisplayUnit = LayoutUnit.Um, SnapDbu = 100 * Dbu };
        view.Shapes.Add(new RectShape { Layer = TopCopper, X1 = 0, Y1 = 0, X2 = Mm(20), Y2 = Mm(2.9) });
        return new LayoutEditorViewModel(view)
        {
            ActiveTool = LayoutEditorViewModel.Tool.Port,
            GeometrySnapEnabled = geometrySnap,
        };
    }

    private static LabelShape? Ghost(LayoutEditorViewModel vm) =>
        vm.Overlay.InProgressPrimitive as LabelShape;

    private static LabelShape Placed(LayoutEditorViewModel vm) =>
        vm.Model.Shapes.OfType<LabelShape>().Single(l => l.IsPort);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // The report itself
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClickingInTheMiddleOfATrace_PlacesTheInternalPortTheGhostDrew()
    {
        var vm = Fixture();

        // Mid-trace, nowhere near either end face — an interior point by any reading.
        vm.OnPointerMoved(Mm(10), Mm(1.45), leftDown: false, KeyModifiers.None, 0, 0, Tol);

        var ghost = Ghost(vm);
        Assert.NotNull(ghost);
        Assert.Equal(PlanarPortKind.Internal, ghost!.PortKind);

        vm.OnPointerPressed(Mm(10), Mm(1.45), KeyModifiers.None, 1, 0, 0, Tol);

        var placed = Placed(vm);

        // The anchor was never the problem, and this pins that it stays that way.
        Assert.Equal(ghost.X, placed.X);
        Assert.Equal(ghost.Y, placed.Y);

        // …and the TYPE, which was. Stated on the label, so nothing can revise it a frame later.
        Assert.Equal(PlanarPortKind.Internal, placed.PortKind);
        Assert.Equal(PlanarPortKind.Internal, vm.PortKindAt(vm.Model.Shapes.IndexOf(placed)));
    }

    [Fact]
    public void ClickingAtAConductorEnd_StillPlacesAnEdgePort()
    {
        // The complement: the inference is unchanged for the case it was built for, so nothing about
        // the ordinary path moved.
        var vm = Fixture();
        vm.OnPointerMoved(0, Mm(1.45), leftDown: false, KeyModifiers.None, 0, 0, Tol);

        Assert.Equal(PlanarPortKind.Edge, Ghost(vm)!.PortKind);

        vm.OnPointerPressed(0, Mm(1.45), KeyModifiers.None, 1, 0, 0, Tol);
        Assert.Equal(PlanarPortKind.Edge, Placed(vm).PortKind);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // The three editors of the one field
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SettingTheTypeIsOneUndoableLayoutEdit()
    {
        var vm = Fixture();
        vm.OnPointerPressed(Mm(10), Mm(1.45), KeyModifiers.None, 1, 0, 0, Tol);

        var placed = Placed(vm);
        int index = vm.Model.Shapes.IndexOf(placed);
        Assert.Equal(PlanarPortKind.Internal, placed.PortKind);

        vm.SetPortKind(index, PlanarPortKind.InternalDeltaGap);
        Assert.Equal(PlanarPortKind.InternalDeltaGap, placed.PortKind);

        vm.UndoCommand.Execute(null);
        Assert.Equal(PlanarPortKind.Internal, placed.PortKind);

        vm.RedoCommand.Execute(null);
        Assert.Equal(PlanarPortKind.InternalDeltaGap, placed.PortKind);
    }

    [Fact]
    public void SettingTheTypeItAlreadyHas_PushesNoUndoEntry()
    {
        var vm = Fixture();
        vm.OnPointerPressed(Mm(10), Mm(1.45), KeyModifiers.None, 1, 0, 0, Tol);
        int index = vm.Model.Shapes.IndexOf(Placed(vm));

        bool couldUndoBefore = vm.UndoRedo.CanUndo;
        int  depthBefore     = 0;
        while (vm.UndoRedo.CanUndo) { vm.UndoRedo.Undo(); depthBefore++; }
        while (vm.UndoRedo.CanRedo) vm.UndoRedo.Redo();

        vm.SetPortKind(index, PlanarPortKind.Internal);   // the type it already has

        int depthAfter = 0;
        while (vm.UndoRedo.CanUndo) { vm.UndoRedo.Undo(); depthAfter++; }
        Assert.Equal(depthBefore, depthAfter);
        Assert.True(couldUndoBefore);
    }

    [Fact]
    public void TheContextMenuFindsThePortByItsOwnMark_NotByTheLabelsAnchor()
    {
        // An EDGE port's mark is drawn at the conductor END, an arbitrary distance from the label —
        // so "which port did I right-click" has to go through the port's pick region, exactly as
        // selection does, or the menu appears for presses that do not select it and vice versa.
        var vm = Fixture();
        vm.OnPointerPressed(0, Mm(1.45), KeyModifiers.None, 1, 0, 0, Tol);
        var placed = Placed(vm);

        int? found = vm.FindPortForContextMenu(placed.X, placed.Y, 0);
        Assert.Equal(vm.Model.Shapes.IndexOf(placed), found);

        // Well clear of every port: no menu section rather than the nearest one.
        Assert.Null(vm.FindPortForContextMenu(Mm(10), Mm(40), 0));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // A drag across the artwork's own answer still retypes; a nudge does not
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DraggingAnInternalPortOntoTheEndFace_MakesItAnEdgePort()
    {
        // A property the owner named in 2026-09-09: dragging a port across the toggle changed what
        // it drew as. That used to fall out of re-inferring the type every frame; with the type
        // STORED it has to be re-seated deliberately, on the same trigger the DIRECTION uses — the
        // artwork's own answer under the port changed.
        var vm = Fixture();
        vm.OnPointerPressed(Mm(10), Mm(1.45), KeyModifiers.None, 1, 0, 0, Tol);
        var placed = Placed(vm);
        Assert.Equal(PlanarPortKind.Internal, placed.PortKind);

        vm.ActiveTool = LayoutEditorViewModel.Tool.Select;
        vm.OnPointerPressed(Mm(10), Mm(1.45), KeyModifiers.None, 1, Mm(0.1));
        vm.OnPointerMoved(0, Mm(1.45), leftDown: true, KeyModifiers.None, Mm(0.1));
        vm.OnPointerReleased(0, Mm(1.45), KeyModifiers.None);

        Assert.Equal(0, placed.X);
        Assert.Equal(PlanarPortKind.Edge, placed.PortKind);
    }

    [Fact]
    public void NudgingAnInternalPortWithinTheMetal_LeavesItsTypeAlone()
    {
        var vm = Fixture();
        vm.OnPointerPressed(Mm(10), Mm(1.45), KeyModifiers.None, 1, 0, 0, Tol);
        var placed = Placed(vm);

        // A user's deliberate choice, of the type the ARTWORK would not have inferred.
        vm.SetPortKind(vm.Model.Shapes.IndexOf(placed), PlanarPortKind.InternalDeltaGap);

        vm.ActiveTool = LayoutEditorViewModel.Tool.Select;
        vm.OnPointerPressed(Mm(10), Mm(1.45), KeyModifiers.None, 1, Mm(0.1));
        vm.OnPointerMoved(Mm(11), Mm(1.45), leftDown: true, KeyModifiers.None, Mm(0.1));
        vm.OnPointerReleased(Mm(11), Mm(1.45), KeyModifiers.None);

        Assert.Equal(Mm(11), placed.X);
        Assert.Equal(PlanarPortKind.InternalDeltaGap, placed.PortKind);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // The file
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AnUnstatedTypeCostsTheClayNoByte_AndAStatedOneRoundTrips()
    {
        var view = new LayoutView { DbuPerMicron = Dbu };
        view.Shapes.Add(new RectShape { Layer = TopCopper, X1 = 0, Y1 = 0, X2 = Mm(20), Y2 = Mm(2.9) });
        var port = new LabelShape
        {
            Layer = TopCopper, X = Mm(10), Y = Mm(1.45), Text = "P1", Height = Mm(0.5), IsPort = true,
        };
        view.Shapes.Add(port);

        // Additive in behaviour as well as in schema — every .clay written before this field carries
        // null here and re-serialises byte for byte.
        string before = LayoutPersistence.Serialize(view);
        Assert.DoesNotContain("PortKind", before, StringComparison.Ordinal);

        port.PortKind = PlanarPortKind.InternalDeltaGap;
        string after = LayoutPersistence.Serialize(view);
        Assert.Contains("InternalDeltaGap", after, StringComparison.Ordinal);

        var back = LayoutPersistence.Deserialize(after);
        Assert.Equal(PlanarPortKind.InternalDeltaGap,
                     back.Shapes.OfType<LabelShape>().Single().PortKind);
    }

    [Fact]
    public void ALegacyCemsPortKinds_ReachTheLabelsAndOverwriteNothingStated()
    {
        // The one door between where the type used to live and where it lives now.
        var view = new LayoutView { DbuPerMicron = Dbu };
        view.Shapes.Add(new RectShape { Layer = TopCopper, X1 = 0, Y1 = 0, X2 = Mm(20), Y2 = Mm(2.9) });
        for (int n = 1; n <= 3; n++)
            view.Shapes.Add(new LabelShape
            {
                Layer = TopCopper, X = Mm(5 * n), Y = Mm(1.45), Text = n.ToString(),
                Height = Mm(0.5), IsPort = true,
            });

        // P2 already states its own type; the setup's opinion of it must not win.
        var p2 = view.Shapes.OfType<LabelShape>().Single(l => l.Text == "2");
        p2.PortKind = PlanarPortKind.Internal;

        List<PlanarPortKind> legacy =
            [PlanarPortKind.Edge, PlanarPortKind.InternalDeltaGap, PlanarPortKind.InternalDeltaGap];

        int moved = EmPortKindMigration.ApplyInMemory(view.Shapes, legacy);

        Assert.Equal(1, moved);                                  // only P3 had nothing to say
        Assert.Null(view.Shapes.OfType<LabelShape>().Single(l => l.Text == "1").PortKind);
        Assert.Equal(PlanarPortKind.Internal, p2.PortKind);      // the drawing wins
        Assert.Equal(PlanarPortKind.InternalDeltaGap,
                     view.Shapes.OfType<LabelShape>().Single(l => l.Text == "3").PortKind);

        // Idempotent: running it again moves nothing.
        Assert.Equal(0, EmPortKindMigration.ApplyInMemory(view.Shapes, legacy));
    }
}
