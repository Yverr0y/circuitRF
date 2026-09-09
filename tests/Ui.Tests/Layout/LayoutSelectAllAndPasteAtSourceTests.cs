// Two owner reports, 2026-09-09, both about the Layout editor:
//
//   1. Ctrl/Cmd+A selected geometry on EVERY layer, including layers whose .ctech entry has Select
//      switched off. Every other selection gesture in this editor honours that flag — a click
//      through LayoutHitTest.HitStack, a marquee through ComputeMarqueeSelection's "gate 8" — so
//      Select All was the one way to end up holding something the user had locked against
//      selection, and Ctrl+A followed by Delete removed it.
//
//   2. During a paste placement, there was no way to say "put it back exactly where it came from".
//      That is the gesture that makes copy/paste BETWEEN .clay documents useful: the fragment has
//      to land on the coordinates it was copied from, which no amount of careful mousing achieves.
//      Enter/Return now does it.

using Avalonia.Input;
using CircuitRF.Ui.Layout;

namespace CircuitRF.Ui.Tests.Layout;

public class LayoutSelectAllAndPasteAtSourceTests
{
    private static readonly LayerKey Selectable   = new(1, 0);
    private static readonly LayerKey NotSelectable = new(2, 0);
    private static readonly LayerKey Hidden        = new(3, 0);

    private static LayoutView FreshModel() => new()
    {
        DbuPerMicron = 1000,
        DisplayUnit  = LayoutUnit.Um,
        SnapDbu      = 1000,
    };

    /// <summary>A three-layer technology: one ordinary, one visible-but-locked, one hidden — the two
    /// halves of the gate every other selection gesture already applies.</summary>
    private static Technology ThreeLayerTech() => new()
    {
        Name = "Select-flag test",
        Layers =
        {
            new LayerDef { Key = Selectable,    Name = "Top",    Visible = true,  Selectable = true  },
            new LayerDef { Key = NotSelectable, Name = "Locked", Visible = true,  Selectable = false },
            new LayerDef { Key = Hidden,        Name = "Off",    Visible = false, Selectable = true  },
        },
    };

    private static LayoutEditorViewModel EditorWithOneShapePerLayer(out LayoutView model)
    {
        model = FreshModel();
        model.Shapes.Add(new RectShape { Layer = Selectable,    X1 = 0,      Y1 = 0, X2 = 1_000,  Y2 = 1_000 });
        model.Shapes.Add(new RectShape { Layer = NotSelectable, X1 = 10_000, Y1 = 0, X2 = 11_000, Y2 = 1_000 });
        model.Shapes.Add(new RectShape { Layer = Hidden,        X1 = 20_000, Y1 = 0, X2 = 21_000, Y2 = 1_000 });

        var vm = new LayoutEditorViewModel(model);
        vm.ApplyTechResolution(new TechResolution(ThreeLayerTech(), "/ws/tech/t.ctech",
                                                  TechResolutionSource.WorkspaceDefault, []));
        return vm;
    }

    // ── Report 1: Select All honours the layer's Select flag ──────────────────────────────────────

    [Fact]
    public void SelectAll_SkipsShapesOnNonSelectableLayers()
    {
        var vm = EditorWithOneShapePerLayer(out var model);

        vm.SelectAllCommand.Execute(null);

        int only = Assert.Single(vm.SelectedIndices);
        Assert.Equal(Selectable, model.Shapes[only].Layer);
    }

    [Fact]
    public void SelectAll_SkipsShapesOnHiddenLayers()
    {
        var vm = EditorWithOneShapePerLayer(out var model);

        vm.SelectAllCommand.Execute(null);

        Assert.DoesNotContain(vm.SelectedIndices, i => model.Shapes[i].Layer == Hidden);
    }

    /// <summary>The point of the fix stated as the user's own sequence: Ctrl+A then Delete must leave
    /// the locked and hidden artwork exactly where it was.</summary>
    [Fact]
    public void SelectAllThenDelete_LeavesLockedAndHiddenGeometryAlone()
    {
        var vm = EditorWithOneShapePerLayer(out var model);

        vm.SelectAllCommand.Execute(null);
        vm.DeleteSelectedGeometry();

        Assert.Equal(2, model.Shapes.Count);
        Assert.Contains(model.Shapes, s => s.Layer == NotSelectable);
        Assert.Contains(model.Shapes, s => s.Layer == Hidden);
    }

    /// <summary>An unknown layer resolves through FallbackPalette and is selectable, so a document
    /// with no technology at all still selects everything — the case the fix must not regress.</summary>
    [Fact]
    public void SelectAll_WithNoTechnology_StillSelectsEverything()
    {
        var model = FreshModel();
        model.Shapes.Add(new RectShape { Layer = Selectable,    X1 = 0,      Y1 = 0, X2 = 1_000,  Y2 = 1_000 });
        model.Shapes.Add(new RectShape { Layer = NotSelectable, X1 = 10_000, Y1 = 0, X2 = 11_000, Y2 = 1_000 });
        var vm = new LayoutEditorViewModel(model);

        vm.SelectAllCommand.Execute(null);

        Assert.Equal(2, vm.SelectedIndices.Count);
    }

    /// <summary>Instances carry no layer of their own — their artwork lives on the layers of the cell
    /// they place — so the gate must not reach them.</summary>
    [Fact]
    public void SelectAll_StillSelectsInstancesAndRulers()
    {
        var vm = EditorWithOneShapePerLayer(out var model);
        model.Instances.Add(new LayoutInstance { CellRef = "../nowhere", X = 0, Y = 0, Mag = 1.0 });
        model.Rulers.Add(new RulerAnnotation { X1 = 0, Y1 = 0, X2 = 5_000, Y2 = 0 });

        vm.SelectAllCommand.Execute(null);

        Assert.Single(vm.SelectedInstanceIndices);
        Assert.Single(vm.SelectedRulerIndices);
    }

    // ── Report 3: a marker grab must not pick up a locked layer ──────────────────────────────────
    //
    // Owner, 2026-09-09: "only 2 layers Selectable, select all, then click to drag, and some other
    // layer moves." Geometry snap gates its candidates on Visible ALONE and says so — "locked IS
    // snappable" — which is right for the TARGET role. But the marker GRAB reuses the same list to
    // decide what the press picks up, so a marker belonging to a locked shape replaced the selection
    // with that shape and dragged it.

    /// <summary>A large snap tolerance keeps this independent of the pixel constant — the point under
    /// test is which candidate the press is allowed to GRAB, not the tolerance value.</summary>
    private const long SnapTol = 3_000;

    private static LayoutEditorViewModel SnapEditor(LayoutView model)
    {
        var vm = new LayoutEditorViewModel(model) { ActiveTool = LayoutEditorViewModel.Tool.Select };
        vm.ApplyTechResolution(new TechResolution(ThreeLayerTech(), "/ws/tech/t.ctech",
                                                  TechResolutionSource.WorkspaceDefault, []));
        return vm;
    }

    [Fact]
    public void MarkerGrab_OnALockedLayersCorner_DoesNotSelectOrMoveIt()
    {
        var model = FreshModel();
        // The only thing anywhere near the press is a rect on the LOCKED layer, and the press lands
        // outside it — so only the marker grab could ever claim it.
        model.Shapes.Add(new RectShape { Layer = NotSelectable, X1 = 0, Y1 = 0, X2 = 50_000, Y2 = 50_000 });
        var vm = SnapEditor(model);

        vm.OnPointerPressed(-2_000, -2_000, KeyModifiers.None, 1, hitTolDbu: 40, zoomPxPerDbu: 0, snapTolDbu: SnapTol);
        vm.OnPointerMoved(20_000, 20_000, leftDown: true, KeyModifiers.None, 40, 0, SnapTol);
        vm.OnPointerReleased(20_000, 20_000, KeyModifiers.None);

        Assert.Empty(vm.SelectedIndices);
        var rect = Assert.IsType<RectShape>(Assert.Single(model.Shapes));
        Assert.Equal(0, rect.X1);
        Assert.Equal(0, rect.Y1);
    }

    /// <summary>The bug as the owner hit it: a Select All selection, then one press to drag it, and the
    /// locked shape's marker took over the whole gesture.</summary>
    [Fact]
    public void MarkerGrab_NearALockedShape_DoesNotHijackAnExistingSelection()
    {
        var model = FreshModel();
        model.Shapes.Add(new RectShape { Layer = Selectable,    X1 = 100_000, Y1 = 0, X2 = 150_000, Y2 = 50_000 });
        model.Shapes.Add(new RectShape { Layer = NotSelectable, X1 = 0,       Y1 = 0, X2 = 50_000,  Y2 = 50_000 });
        var vm = SnapEditor(model);

        vm.SelectAllCommand.Execute(null);
        Assert.Equal([0], vm.SelectedIndices);   // the selectable one only

        // Press just outside the LOCKED rect's corner — the gesture the owner described.
        vm.OnPointerPressed(-2_000, -2_000, KeyModifiers.None, 1, hitTolDbu: 40, zoomPxPerDbu: 0, snapTolDbu: SnapTol);

        // The locked shape is neither selected nor swapped in for what was already selected.
        Assert.DoesNotContain(1, vm.SelectedIndices);
    }

    /// <summary>The grab gate must not reach the TARGET role: snapping new artwork to a locked board
    /// outline is what locking a layer is for, and that is a different question from picking it up.</summary>
    [Fact]
    public void ALockedLayerIsStillASnapTarget()
    {
        var model = FreshModel();
        model.Shapes.Add(new RectShape { Layer = NotSelectable, X1 = 0, Y1 = 0, X2 = 50_000, Y2 = 50_000 });
        var vm = SnapEditor(model);

        var counters = new SnapQueryCounters();
        var candidates = LayoutSnapQuery.FindCandidates(
            model, vm.Technology, vm.InstanceBaseDir, -2_000, -2_000, SnapTol,
            includeIntersections: false, null, null, ref counters);

        Assert.NotEmpty(candidates);
    }

    /// <summary>An ordinary layer still grabs by its marker — the behaviour the gate must not cost.</summary>
    [Fact]
    public void MarkerGrab_OnASelectableLayer_StillSelectsAndDrags()
    {
        var model = FreshModel();
        model.Shapes.Add(new RectShape { Layer = Selectable, X1 = 0, Y1 = 0, X2 = 50_000, Y2 = 50_000 });
        var vm = SnapEditor(model);

        vm.OnPointerPressed(-2_000, -2_000, KeyModifiers.None, 1, hitTolDbu: 40, zoomPxPerDbu: 0, snapTolDbu: SnapTol);

        Assert.Equal([0], vm.SelectedIndices);
    }

    // ── Report 2: Enter places the paste ghost at the source coordinates ──────────────────────────

    [Fact]
    public void PastePlacement_Enter_PlacesAtTheSourceCoordinates_EvenAfterTheGhostHasMoved()
    {
        var model = FreshModel();
        var vm = new LayoutEditorViewModel(model);

        // A fragment copied from somewhere else, still carrying its SOURCE coordinates; the anchor
        // is its own bbox corner, which is what LayoutFragment.Build records.
        var shape = new RectShape { Layer = Selectable, X1 = 40_000, Y1 = 70_000, X2 = 41_000, Y2 = 71_000 };
        vm.BeginPastePlacement([shape], anchorX: 40_000, anchorY: 70_000);

        // Drag the ghost well away first, so a passing Enter cannot be mistaken for "it never moved".
        vm.OnPointerMoved(123_000, 456_000, leftDown: false, KeyModifiers.None);
        Assert.True(vm.IsPastePlacementActive);

        vm.OnKeyDown(Key.Enter, KeyModifiers.None);

        Assert.False(vm.IsPastePlacementActive);
        var placed = Assert.IsType<RectShape>(Assert.Single(model.Shapes));
        Assert.Equal(40_000, placed.X1);
        Assert.Equal(70_000, placed.Y1);
        Assert.Equal(41_000, placed.X2);
        Assert.Equal(71_000, placed.Y2);
    }

    /// <summary>Return is the same key on the keyboards that spell it that way.</summary>
    [Fact]
    public void PastePlacement_Return_PlacesAtTheSourceCoordinates()
    {
        var model = FreshModel();
        var vm = new LayoutEditorViewModel(model);
        vm.BeginPastePlacement([new RectShape { Layer = Selectable, X1 = 5_000, Y1 = 5_000, X2 = 6_000, Y2 = 6_000 }],
                               anchorX: 5_000, anchorY: 5_000);
        vm.OnPointerMoved(80_000, 90_000, leftDown: false, KeyModifiers.None);

        vm.OnKeyDown(Key.Return, KeyModifiers.None);

        var placed = Assert.IsType<RectShape>(Assert.Single(model.Shapes));
        Assert.Equal(5_000, placed.X1);
        Assert.Equal(5_000, placed.Y1);
    }

    /// <summary>One undo entry, and it undoes the whole placement — the same contract a click-placed
    /// paste has, because Enter runs the identical commit.</summary>
    [Fact]
    public void PastePlacement_Enter_IsOneUndoEntry_AndSelectsWhatWasPlaced()
    {
        var model = FreshModel();
        var vm = new LayoutEditorViewModel(model);
        vm.BeginPastePlacement(
        [
            new RectShape { Layer = Selectable, X1 = 0,     Y1 = 0, X2 = 1_000, Y2 = 1_000 },
            new RectShape { Layer = Selectable, X1 = 2_000, Y1 = 0, X2 = 3_000, Y2 = 1_000 },
        ], anchorX: 0, anchorY: 0);
        vm.OnPointerMoved(50_000, 50_000, leftDown: false, KeyModifiers.None);

        vm.OnKeyDown(Key.Enter, KeyModifiers.None);

        Assert.Equal(2, model.Shapes.Count);
        Assert.Equal(2, vm.SelectedIndices.Count);

        vm.UndoCommand.Execute(null);
        Assert.Empty(model.Shapes);
        Assert.False(vm.UndoRedo.CanUndo);
    }

    /// <summary>Enter reaches this only while a ghost is armed — with nothing being placed it must
    /// not paste anything, since there is nothing to place.</summary>
    [Fact]
    public void Enter_WithNoPastePlacementArmed_ChangesNothing()
    {
        var model = FreshModel();
        var vm = new LayoutEditorViewModel(model) { ActiveTool = LayoutEditorViewModel.Tool.Select };

        vm.OnKeyDown(Key.Enter, KeyModifiers.None);

        Assert.Empty(model.Shapes);
        Assert.False(vm.UndoRedo.CanUndo);
    }
}
