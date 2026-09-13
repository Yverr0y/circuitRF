using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using CircuitRF.Design.Layout;
using CircuitRF.Render;
using CircuitRF.Ui.Controls;
using CircuitRF.Ui.Layout;
using SkiaSharp;
using Xunit;

// The namespace is NOT `CircuitRF.Ui.Tests.Stackup`, though the folder is — see the note at the top
// of StackupSceneTests.cs: a namespace segment named `Stackup` shadows the `Stackup` TYPE for every
// file under `CircuitRF.Ui.Tests`.
namespace CircuitRF.Ui.Tests.StackupRender;

/// <summary>
/// brief-stackup-render-3-selection.md's gate — click a band, land on its fields.
///
/// <h3>What stands in for a pointer and for a rendered picture</h3>
/// <para>There is no input device in this project and no app host, so a click is
/// <see cref="StackupCanvas.PressAt"/> and a hover is <c>MoveAt</c> — the same kind of internal seam
/// <c>MeasureForWidth</c> and <c>SceneCache</c> already are. What they exercise is the real control
/// over the real scene; only the event plumbing is bypassed.</para>
///
/// <para><see cref="ScrollIntoView"/> itself needs a realised, arranged <c>ListBox</c>. What is
/// asserted instead is the DECISION — which row was asked for, and that it is in
/// <c>FilteredStackupLayers</c> when it is asked for. A test that arranged a <c>ListBox</c> to read
/// back a scroll offset would be measuring the layout engine.</para>
/// </summary>
public class StackupSelectionTests
{
    private const double PaneWidth = 900;
    private const string FourLayerId = "pcb-4layer_FR-4_62mil_1oz";

    private static TechEditorViewModel Editor(string id = FourLayerId) =>
        new(Path.Combine(Path.GetTempPath(), "stackup-selection-tests.ctech"),
            ShippedTechnologies.Load(id));

    private static StackupCanvas Canvas(TechEditorViewModel vm)
    {
        var canvas = new StackupCanvas { ViewModel = vm };
        canvas.MeasureForWidth(PaneWidth);   // the scene the click will be hit-tested against
        return canvas;
    }

    // ── R-stk3-1 — the selection survives a committed edit ───────────────────────────────────────

    /// <summary>
    /// <b>The test that pins the whole design decision.</b> <c>ApplySnapshot</c> assigns
    /// <c>Working</c> a freshly deserialized technology and rebuilds every row VM, so every object
    /// identity in the stackup is destroyed on every committed edit, undo and redo. A selection held
    /// as a <c>StackupLayer</c> or a row-VM reference fails this outright; a name survives it.
    /// </summary>
    [Fact]
    public void TheSelectionSurvivesACommittedEditOnADifferentEntry()
    {
        var vm = Editor();
        var conductor = vm.StackupLayers.First(r => r.Layer.Kind == StackupKind.Conductor);
        var other     = vm.StackupLayers.First(r => r.Layer.Kind == StackupKind.Dielectric);

        vm.SelectedStackupLayerName = conductor.Layer.Name;
        var name = conductor.Layer.Name;

        other.StagedThicknessText = "1.234";
        other.CommitThickness();

        Assert.Equal(name, vm.SelectedStackupLayerName);

        // …and it still RESOLVES: the row VM it points at is one of the ones that exist now, not the
        // dead one selection was made against.
        Assert.NotNull(vm.SelectedStackupLayerRow);
        Assert.Equal(name, vm.SelectedStackupLayerRow!.Layer.Name);
        Assert.Contains(vm.SelectedStackupLayerRow, vm.StackupLayers);
        Assert.DoesNotContain(conductor, vm.StackupLayers);
        Assert.True(vm.SelectedStackupLayerRow.IsSelected);
    }

    [Fact]
    public void AnUndoAndARedoBothKeepIt()
    {
        var vm = Editor();
        var target = vm.StackupLayers.First(r => r.Layer.Kind == StackupKind.Conductor);
        var name = target.Layer.Name;
        vm.SelectedStackupLayerName = name;

        vm.AddConductorLayerCommand.Execute(null);
        Assert.Equal(name, vm.SelectedStackupLayerRow?.Layer.Name);

        vm.UndoRedo.Undo();
        Assert.Equal(name, vm.SelectedStackupLayerRow?.Layer.Name);

        vm.UndoRedo.Redo();
        Assert.Equal(name, vm.SelectedStackupLayerRow?.Layer.Name);
    }

    // ── R-stk3-2 — what a click selects ─────────────────────────────────────────────────────────

    [Fact]
    public void AClickInsideABandSelectsThatBand()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = canvas.SceneCache.Current!;

        foreach (var band in scene.Bands)
        {
            vm.SelectedStackupLayerName = null;
            Assert.True(canvas.PressAt(PointInBand(scene, band)));
            Assert.Equal(band.Name, vm.SelectedStackupLayerName);
        }
    }

    [Fact]
    public void AClickOnTheBackgroundClearsIt()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = canvas.SceneCache.Current!;

        canvas.PressAt(PointInBand(scene, scene.Bands[0]));
        Assert.NotNull(vm.SelectedStackupLayerName);

        // 0,0 is the pane's own top-left corner: outside the gutter, above the first note.
        Assert.Null(scene.HitTest(0, 0));
        canvas.PressAt(new Avalonia.Point(0, 0));

        Assert.Null(vm.SelectedStackupLayerName);
        Assert.Null(vm.SelectedStackupLayerRow);
        Assert.All(vm.StackupLayers, r => Assert.False(r.IsSelected));
    }

    /// <summary>A barrel is drawn ACROSS the bands it spans, so the same point is inside both. The
    /// scene's hit order is the draw order back to front and <c>HitTest</c> walks it backwards, which
    /// is what makes the picture and the pointer agree (R-stk1-1).</summary>
    [Fact]
    public void AClickOnAViaBarrelSelectsTheVia_NotTheConductorBehindIt()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = canvas.SceneCache.Current!;
        Assert.NotEmpty(scene.Barrels);

        foreach (var barrel in scene.Barrels)
        {
            var p = new Avalonia.Point(barrel.Rect.MidX, barrel.Rect.MidY);

            // The point really is inside a band too, or this proves nothing about precedence.
            Assert.Contains(scene.Bands, b => b.Rect.Contains((float)p.X, (float)p.Y));

            vm.SelectedStackupLayerName = null;
            canvas.PressAt(p);
            Assert.Equal(barrel.Name, vm.SelectedStackupLayerName);
        }
    }

    /// <summary>A label hit selects the layer the label belongs to — brief 4 makes a DOUBLE click on
    /// a value open an editor; a single click still just selects.</summary>
    [Fact]
    public void AClickOnALabelSelectsItsOwnLayer()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = canvas.SceneCache.Current!;

        var label = scene.Labels.First(l => l.LayerName.Length > 0 && l.Field != StackupField.None);
        canvas.PressAt(new Avalonia.Point(label.Rect.MidX, label.Rect.MidY));

        Assert.Equal(label.LayerName, vm.SelectedStackupLayerName);
    }

    [Fact]
    public void ACanvasWithNoViewModelSelectsNothingAndDoesNotThrow()
    {
        var canvas = new StackupCanvas();
        canvas.MeasureForWidth(PaneWidth);
        Assert.False(canvas.PressAt(new Avalonia.Point(100, 100)));
    }

    // ── R-stk3-3 — the outline ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// OUTSIDE the band's own edge, with a gap — so it never hides the ground-reference heavy edge,
    /// which brief 1 §1 makes the one distinguishing mark on the whole picture.
    /// </summary>
    [Fact]
    public void TheOutlineRectIsStrictlyOutsideTheBandRect()
    {
        var scene = StackupScene.Build(ShippedTechnologies.Load(FourLayerId), (float)PaneWidth);

        foreach (var band in scene.Bands)
        {
            var outline = StackupRenderer.OutlineRectFor(band.Rect);
            Assert.True(outline.Contains(band.Rect), $"{band.Name}: {outline} must contain {band.Rect}");
            Assert.True(outline.Left   < band.Rect.Left);
            Assert.True(outline.Top    < band.Rect.Top);
            Assert.True(outline.Right  > band.Rect.Right);
            Assert.True(outline.Bottom > band.Rect.Bottom);
        }

        // The GAP is what the rule is actually about: a stroke is centred on its rect, so the
        // outline's inner face has to clear the heaviest edge the picture draws.
        Assert.True(StackupRenderer.SelectionGap - StackupRenderer.SelectionWidth / 2
                    > StackupRenderer.GroundEdgeWidth / 2,
            "the selection outline must not touch the ground reference's heavy edge");
    }

    /// <summary>For a via, around the BARREL — not around the bands it crosses.</summary>
    [Fact]
    public void AViasOutlineFramesItsBarrel()
    {
        var scene = StackupScene.Build(ShippedTechnologies.Load(FourLayerId), (float)PaneWidth);
        var barrel = scene.Barrels[0];

        var rect = scene.RectOf(barrel.Name);
        Assert.NotNull(rect);
        Assert.Equal(barrel.Rect, rect!.Value);
    }

    [Fact]
    public void TheSelectedLayerReachesTheRenderer()
    {
        var scene = StackupScene.Build(ShippedTechnologies.Load(FourLayerId), (float)PaneWidth);

        var none  = Svg(scene, StackupOverlay.Empty);
        var first = Svg(scene, new StackupOverlay { SelectedLayer = scene.Bands[0].Name });
        var last  = Svg(scene, new StackupOverlay { SelectedLayer = scene.Bands[^1].Name });

        Assert.NotEqual(none, first);
        Assert.NotEqual(first, last);

        // A name that is in no stackup draws nothing — RectOf returns null and the renderer skips it.
        Assert.Equal(none, Svg(scene, new StackupOverlay { SelectedLayer = "no such layer" }));
    }

    // ── R-stk3-4 — hover ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HoverTracksThePointerAndClearsOnExit()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = canvas.SceneCache.Current!;

        canvas.MoveAt(PointInBand(scene, scene.Bands[1]));
        Assert.Equal(scene.Bands[1].Name, canvas.HoverLayer);
        Assert.Equal(scene.Bands[1].Name, canvas.CurrentOverlay.HoverLayer);

        canvas.MoveAt(new Avalonia.Point(0, 0));
        Assert.Null(canvas.HoverLayer);
    }

    /// <summary>Hover is the CANVAS's and the selection is the VIEW MODEL's: hovering must not
    /// select, or a pointer crossing the drawing would rewrite the card list under the user.</summary>
    [Fact]
    public void HoveringSelectsNothing()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = canvas.SceneCache.Current!;

        canvas.MoveAt(PointInBand(scene, scene.Bands[0]));

        Assert.Null(vm.SelectedStackupLayerName);
    }

    [Fact]
    public void AHoveredViaRevealsItsGrippers_AndAHoverOutlineIsDrawn()
    {
        var scene = StackupScene.Build(ShippedTechnologies.Load(FourLayerId), (float)PaneWidth);

        var none    = Svg(scene, StackupOverlay.Empty);
        var hovered = Svg(scene, new StackupOverlay { HoverLayer = scene.Barrels[0].Name });
        Assert.NotEqual(none, hovered);

        // A band that is BOTH hovered and selected shows the selected outline and no second one —
        // the two are the same rect, so drawing both would only thicken it.
        var band = scene.Bands[0].Name;
        Assert.Equal(
            Svg(scene, new StackupOverlay { SelectedLayer = band }),
            Svg(scene, new StackupOverlay { SelectedLayer = band, HoverLayer = band }));
    }

    // ── R-stk3-5 — the filter cannot hide the thing the user just clicked ───────────────────────

    [Fact]
    public void AClickOnABandTheFilterExcludesClearsTheFilter()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = canvas.SceneCache.Current!;
        var band = scene.Bands[^1];

        // A filter that matches something, and not the band about to be clicked.
        vm.StackupFilter = "Prepreg";
        Assert.DoesNotContain(vm.FilteredStackupLayers, r => r.Layer.Name == band.Name);

        canvas.PressAt(PointInBand(scene, band));

        Assert.Equal("", vm.StackupFilter);
        Assert.Equal(band.Name, vm.SelectedStackupLayerName);

        // The DECISION ScrollIntoView is given: the row exists, and it is in the projection the list
        // is bound to.
        Assert.NotNull(vm.SelectedStackupLayerRow);
        Assert.Contains(vm.SelectedStackupLayerRow!, vm.FilteredStackupLayers);
    }

    /// <summary>A filter that already shows the clicked band is left exactly as the user typed it —
    /// the clear is a remedy for a click that would otherwise appear to do nothing, not a policy.</summary>
    [Fact]
    public void AFilterThatWouldNotHideItIsLeftAlone()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = canvas.SceneCache.Current!;

        var band = scene.Bands.First(b => b.Name.Contains("Copper", StringComparison.Ordinal));
        vm.StackupFilter = "Copper";

        canvas.PressAt(PointInBand(scene, band));

        Assert.Equal("Copper", vm.StackupFilter);
        Assert.Contains(vm.SelectedStackupLayerRow!, vm.FilteredStackupLayers);
    }

    // ── R-stk3-6 — the shaded card ──────────────────────────────────────────────────────────────

    [Fact]
    public void ExactlyOneRowIsMarkedSelected()
    {
        var vm = Editor();
        var target = vm.StackupLayers[2];

        vm.SelectedStackupLayerName = target.Layer.Name;

        Assert.Single(vm.StackupLayers, r => r.IsSelected);
        Assert.True(target.IsSelected);
        Assert.All(vm.StackupLayers.Where(r => r != target), r => Assert.False(r.IsSelected));

        vm.SelectedStackupLayerName = null;
        Assert.All(vm.StackupLayers, r => Assert.False(r.IsSelected));
    }

    [Fact]
    public void TheCardBindsItsBackgroundToIsSelected()
    {
        var axaml = RepoFile(Path.Combine("src", "Ui", "Views", "Layout", "TechEditorView.axaml"));

        Assert.Contains("Classes.selected=\"{Binding IsSelected}\"", axaml);
        Assert.Contains("Selector=\"Border.stackupcard.selected\"", axaml);
        Assert.Contains("<Setter Property=\"Background\" Value=\"{DynamicResource CrfStackupSelectedCardBrush}\"/>",
                        axaml);

        // SHADE, not outline, and low-alpha so the card's own text and its needsvalue marking stay
        // legible on top of it.
        var resources = RepoFile(Path.Combine("src", "Ui", "Styles", "CircuitRfResources.axaml"));
        int key = resources.IndexOf("x:Key=\"CrfStackupSelectedCardBrush\"", StringComparison.Ordinal);
        Assert.True(key >= 0, "the selected-card brush must be an application-scope resource");

        var color = resources[key..resources.IndexOf("/>", key, StringComparison.Ordinal)];
        var alpha = Convert.ToInt32(color[(color.IndexOf("Color=\"#", StringComparison.Ordinal) + 8)..][..2], 16);
        Assert.InRange(alpha, 1, 80);
    }

    // ── R-stk3-7 — one selection, two surfaces, no re-entrancy ──────────────────────────────────

    [Fact]
    public void SelectingACardSelectsItsBand()
    {
        var vm = Editor();
        var row = vm.StackupLayers[3];

        vm.SelectedStackupLayerRow = row;

        Assert.Equal(row.Layer.Name, vm.SelectedStackupLayerName);
        Assert.True(row.IsSelected);
    }

    [Fact]
    public void SettingTheNameSelectsTheCard()
    {
        var vm = Editor();
        var row = vm.StackupLayers[3];

        vm.SelectedStackupLayerName = row.Layer.Name;

        Assert.Same(row, vm.SelectedStackupLayerRow);
    }

    /// <summary>Each surface raises its own change exactly once per selection — a second raise is
    /// what a re-entrant round trip looks like from the outside.</summary>
    [Fact]
    public void NeitherDirectionReEnters()
    {
        var vm = Editor();
        var counts = new Dictionary<string, int>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is { } n) counts[n] = counts.GetValueOrDefault(n) + 1;
        };

        vm.SelectedStackupLayerName = vm.StackupLayers[1].Layer.Name;
        Assert.Equal(1, counts.GetValueOrDefault(nameof(TechEditorViewModel.SelectedStackupLayerName)));
        Assert.Equal(1, counts.GetValueOrDefault(nameof(TechEditorViewModel.SelectedStackupLayerRow)));

        counts.Clear();
        vm.SelectedStackupLayerRow = vm.StackupLayers[4];
        Assert.Equal(1, counts.GetValueOrDefault(nameof(TechEditorViewModel.SelectedStackupLayerName)));
        Assert.Equal(1, counts.GetValueOrDefault(nameof(TechEditorViewModel.SelectedStackupLayerRow)));
    }

    /// <summary>
    /// The ListBox drops its own <c>SelectedItem</c> — and pushes the null back through the two-way
    /// binding — whenever the collection it is bound to is emptied. That happens on every keystroke
    /// in the filter box and on every committed edit, so taking the write-back at face value would
    /// clear the selection constantly, silently, and for no reason the user could see.
    /// </summary>
    [Fact]
    public void AFilterKeystrokeDoesNotClearTheSelection()
    {
        var vm = Editor();
        var name = vm.StackupLayers[2].Layer.Name;
        vm.SelectedStackupLayerName = name;

        // What a bound ListBox does as ApplyStackupFilter clears and refills the collection.
        vm.FilteredStackupLayers.CollectionChanged += (_, _) => vm.SelectedStackupLayerRow = null;

        vm.StackupFilter = "Copper";

        Assert.Equal(name, vm.SelectedStackupLayerName);
    }

    [Fact]
    public void TheListBoxSelectionIsBoundTwoWay()
    {
        var axaml = RepoFile(Path.Combine("src", "Ui", "Views", "Layout", "TechEditorView.axaml"));
        int list = axaml.IndexOf("x:Name=\"StackupList\"", StringComparison.Ordinal);
        Assert.True(list >= 0);

        var element = axaml[list..axaml.IndexOf('>', list)];
        Assert.Contains("SelectedItem=\"{Binding ViewModel.SelectedStackupLayerRow, Mode=TwoWay}\"", element);
    }

    // ── R-stk3-8 — a rename keeps the selection ─────────────────────────────────────────────────

    [Fact]
    public void RenamingTheSelectedEntryKeepsItSelected()
    {
        var vm = Editor();
        var row = vm.StackupLayers.First(r => r.Layer.Kind == StackupKind.Conductor);
        vm.SelectedStackupLayerName = row.Layer.Name;

        row.StagedName = "Renamed Metal";
        row.CommitName();

        Assert.Equal("Renamed Metal", vm.SelectedStackupLayerName);
        Assert.NotNull(vm.SelectedStackupLayerRow);
        Assert.Equal("Renamed Metal", vm.SelectedStackupLayerRow!.Layer.Name);
        Assert.True(vm.SelectedStackupLayerRow.IsSelected);
    }

    /// <summary>Renaming a DIFFERENT entry must not steal the selection.</summary>
    [Fact]
    public void RenamingAnUnselectedEntryLeavesTheSelectionWhereItWas()
    {
        var vm = Editor();
        var selected = vm.StackupLayers[0];
        var other    = vm.StackupLayers[1];
        vm.SelectedStackupLayerName = selected.Layer.Name;
        var name = selected.Layer.Name;

        other.StagedName = "Something Else";
        other.CommitName();

        Assert.Equal(name, vm.SelectedStackupLayerName);
        Assert.Equal(name, vm.SelectedStackupLayerRow?.Layer.Name);
    }

    // ── R-stk3-9 — Esc ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ClearingTheSelectionClearsTheOutlineTheCardAndTheListSelectionTogether()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = canvas.SceneCache.Current!;
        canvas.PressAt(PointInBand(scene, scene.Bands[0]));
        Assert.NotNull(canvas.CurrentOverlay.SelectedLayer);

        vm.ClearStackupSelection();

        Assert.Null(vm.SelectedStackupLayerName);
        Assert.Null(vm.SelectedStackupLayerRow);
        Assert.Null(canvas.CurrentOverlay.SelectedLayer);
        Assert.All(vm.StackupLayers, r => Assert.False(r.IsSelected));
    }

    /// <summary>
    /// <c>Esc</c> does NOT put back a filter the selection cleared. Undoing that on <c>Esc</c> would
    /// make <c>Esc</c> a second undo, which it is not.
    /// </summary>
    [Fact]
    public void EscDoesNotRestoreAFilterTheSelectionCleared()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = canvas.SceneCache.Current!;

        vm.StackupFilter = "Prepreg";
        canvas.PressAt(PointInBand(scene, scene.Bands[^1]));
        Assert.Equal("", vm.StackupFilter);

        vm.ClearStackupSelection();

        Assert.Equal("", vm.StackupFilter);
    }

    /// <summary>
    /// The keystroke itself is a TUNNELLING handler on the view, not a key handler on the drawing:
    /// the canvas is deliberately not focusable (R-stk2-10 — a focusable control inside the drawing's
    /// ScrollViewer re-points Page Up/Down at the drawing), and the ask is "pressing Esc will
    /// unselect", not "pressing Esc while the drawing happens to have focus". So it has to reach the
    /// card list too, which one tunnelling handler on the view does and a canvas handler cannot.
    /// </summary>
    [Fact]
    public void EscIsWiredAsATunnellingHandlerOnTheView_AndTheCanvasStillTakesNoFocus()
    {
        var code = RepoFile(Path.Combine("src", "Ui", "Views", "Layout", "TechEditorView.axaml.cs"));

        Assert.Contains("AddHandler(KeyDownEvent, OnEscapeKeyDown, RoutingStrategies.Tunnel);", code);

        int handler = code.IndexOf("private void OnEscapeKeyDown", StringComparison.Ordinal);
        Assert.True(handler >= 0);
        var body = code[handler..code.IndexOf("\n    }", handler, StringComparison.Ordinal)];
        Assert.Contains("Key.Escape", body);
        Assert.Contains("ClearStackupSelection()", body);

        Assert.False(new StackupCanvas().Focusable);
    }

    // ── The canvas hands the renderer what the view model says ──────────────────────────────────

    [Fact]
    public void TheCanvasOverlayFollowsTheViewModel_WithNoCopyOfItsOwn()
    {
        var vm = Editor();
        var canvas = Canvas(vm);

        vm.SelectedStackupLayerName = vm.StackupLayers[2].Layer.Name;
        Assert.Equal(vm.StackupLayers[2].Layer.Name, canvas.CurrentOverlay.SelectedLayer);

        // Re-pointed at a different editor, the canvas shows THAT editor's selection — it holds none.
        var second = Editor("mmic-GaAs_2LM_100um");
        canvas.ViewModel = second;
        Assert.Null(canvas.CurrentOverlay.SelectedLayer);

        second.SelectedStackupLayerName = second.StackupLayers[0].Layer.Name;
        Assert.Equal(second.StackupLayers[0].Layer.Name, canvas.CurrentOverlay.SelectedLayer);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A point inside <paramref name="band"/> that the scene agrees belongs to it — scanned rather
    /// than computed, because a barrel crosses the band at its own x and a label sits on it, and both
    /// are supposed to win there.
    /// </summary>
    private static Avalonia.Point PointInBand(StackupScene scene, StackupBand band)
    {
        float y = band.Rect.MidY;
        for (float x = band.Rect.Left + 2; x < band.Rect.Right - 2; x += 3)
            if (scene.HitTest(x, y) is { } hit && hit.LayerName == band.Name &&
                hit.Kind == StackupHitKind.Band)
                return new Avalonia.Point(x, y);

        throw new InvalidOperationException($"no point of band {band.Name} hit-tests to it");
    }

    private static string Svg(StackupScene scene, StackupOverlay overlay) =>
        PlotDocumentWriter.BuildSvgString(
            canvas => StackupRenderer.Draw(canvas, scene, StackupRenderTheme.Light, overlay),
            new PagePlacement(scene.Width, scene.Height, 0f));

    private static string RepoFile(string rel)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "circuitrf.slnx")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!, rel));
    }
}
