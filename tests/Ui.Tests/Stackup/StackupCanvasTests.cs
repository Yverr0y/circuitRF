using System;
using System.IO;
using System.Linq;
using CircuitRF.Design.Layout;
using CircuitRF.Render;
using CircuitRF.Ui.Controls;
using CircuitRF.Ui.Layout;
using Xunit;

// The namespace is NOT `CircuitRF.Ui.Tests.Stackup`, though the folder is — see the note at the top
// of StackupSceneTests.cs: a namespace segment named `Stackup` shadows the `Stackup` TYPE for every
// file under `CircuitRF.Ui.Tests`.
namespace CircuitRF.Ui.Tests.StackupRender;

/// <summary>
/// brief-stackup-render-2-canvas-and-tab.md §1's gate — the canvas itself.
///
/// <h3>What is real here and what stands in for what</h3>
/// <para>A <see cref="StackupCanvas"/> CONSTRUCTS and MEASURES with no application host: it is a
/// plain <c>Control</c> that computes its desired size from a <see cref="StackupScene"/>, and both
/// of those are framework-free arithmetic. So R-stk2-1, R-stk2-3 and R-stk2-4 are tested against the
/// real control, through two internal seams (<c>MeasureForWidth</c>, <c>SceneCache</c>) that exist
/// because there is no headless RENDER in this repo to read a picture back from.</para>
///
/// <para>What is NOT reachable is anything needing a visual tree — attach, detach, the theme
/// subscription and the arrange pass. Those are asserted in
/// <see cref="TechEditorStackupTabLayoutTests"/> as source scans, which is the same fallback every
/// other .axaml-shaped fix in this project uses.</para>
/// </summary>
public class StackupCanvasTests
{
    private const double PaneWidth = 900;

    /// <summary>R-stk2-9 names it: the shipped four-layer board.</summary>
    private const string FourLayerId = "pcb-4layer_FR-4_62mil_1oz";

    private const string TenLayerId = "mmic-GaAs_2LM_100um";

    private static Technology Shipped(string id) => ShippedTechnologies.Load(id);

    private static TechEditorViewModel Editor(string id) =>
        new(Path.Combine(Path.GetTempPath(), "stackup-canvas-tests.ctech"), Shipped(id));

    // ── R-stk2-1 — the canvas measures to the scene's intrinsic height ────────────────────────────

    [Fact]
    public void MeasuresToTheScenesIntrinsicHeight()
    {
        var canvas = new StackupCanvas { ViewModel = Editor(FourLayerId) };

        var size = canvas.MeasureForWidth(PaneWidth);

        Assert.Equal(PaneWidth, size.Width, 3);
        Assert.Equal(canvas.SceneCache.Current!.Height, size.Height, 3);
        Assert.True(size.Height > 0, "a four-layer board must measure to something");
    }

    [Fact]
    public void ATechnologyWithMoreLayersMeasuresTaller()
    {
        var four = new StackupCanvas { ViewModel = Editor(FourLayerId) }.MeasureForWidth(PaneWidth);
        var ten  = new StackupCanvas { ViewModel = Editor(TenLayerId)  }.MeasureForWidth(PaneWidth);

        Assert.True(ten.Height > four.Height,
            $"ten stackup entries measured {ten.Height}, four measured {four.Height}");
    }

    /// <summary>
    /// "Computed ONCE, read TWICE" is a COUNTER, not a timing — the structural property is that a
    /// second reader of the same layout pass gets the scene the first one built, not a second scene
    /// that happens to look like it (R-stk1-1: the hit-test in brief 3 reads what the renderer drew).
    /// </summary>
    [Fact]
    public void OneSceneIsBuiltPerLayoutPass_NotOnePerRead()
    {
        var canvas = new StackupCanvas { ViewModel = Editor(FourLayerId) };

        canvas.MeasureForWidth(PaneWidth);
        var built = canvas.SceneCache.Builds;
        var scene = canvas.SceneCache.Current;

        // Re-measuring at the same width, and every later read of the scene, costs nothing.
        canvas.MeasureForWidth(PaneWidth);
        canvas.MeasureForWidth(PaneWidth);
        Assert.Equal(built, canvas.SceneCache.Builds);
        Assert.Same(scene, canvas.SceneCache.Current);

        // A different width IS a different layout, and does rebuild.
        canvas.MeasureForWidth(PaneWidth - 120);
        Assert.Equal(built + 1, canvas.SceneCache.Builds);
    }

    // ── R-stk2-3 — one signal, every edit path ───────────────────────────────────────────────────

    [Fact]
    public void CommittingAThicknessRaisesStackupChangedExactlyOnce_AndSoDoAnUndoAndARedo()
    {
        var vm = Editor(FourLayerId);
        int raised = 0;
        vm.StackupChanged += () => raised++;

        var row = vm.StackupLayers.First(r => r.Layer.Kind == StackupKind.Dielectric);
        row.StagedThicknessText = "1.2";
        row.CommitThickness();
        Assert.Equal(1, raised);

        vm.UndoRedo.Undo();
        Assert.Equal(2, raised);

        vm.UndoRedo.Redo();
        Assert.Equal(3, raised);
    }

    /// <summary>Every OTHER edit path is the same one wire — an Add button here, and by construction
    /// a combo, a card's other fields, and everything briefs 4-6 add.</summary>
    [Fact]
    public void AddingALayerRaisesIt_Too()
    {
        var vm = Editor(FourLayerId);
        int raised = 0;
        vm.StackupChanged += () => raised++;

        vm.AddConductorLayerCommand.Execute(null);

        Assert.Equal(1, raised);
    }

    /// <summary>A commit that turns out to change nothing pushes no undo entry and must therefore
    /// announce nothing — otherwise every keystroke that reverts itself repaints the drawing.</summary>
    [Fact]
    public void ANoOpCommitRaisesNothing()
    {
        var vm = Editor(FourLayerId);
        var row = vm.StackupLayers.First(r => r.Layer.Kind == StackupKind.Dielectric);
        row.StagedThicknessText = row.StagedThicknessText;

        int raised = 0;
        vm.StackupChanged += () => raised++;
        row.CommitThickness();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void TheCanvasRebuildsItsScene_AndTheNewHeightReflectsTheEdit()
    {
        var vm = Editor(FourLayerId);
        var canvas = new StackupCanvas { ViewModel = vm };

        canvas.MeasureForWidth(PaneWidth);
        var before = canvas.SceneCache.Current;
        Assert.NotNull(before);

        // A dielectric grown by 50x: past the point where the height map can stay proportional, so
        // the drawing's total height is certain to move.
        var row = vm.StackupLayers.First(r => r.Layer.Kind == StackupKind.Dielectric);
        var grown = row.Layer.ThicknessDbu * 50;
        row.StagedThicknessText = LayoutUnits.Format(grown, vm.Working.DefaultDisplayUnit, LayoutUnits.DefaultDbuPerMicron);
        row.CommitThickness();

        // The cue alone drops the scene — nothing waits for a paint.
        Assert.Null(canvas.SceneCache.Current);

        var after = canvas.MeasureForWidth(PaneWidth);
        Assert.NotSame(before, canvas.SceneCache.Current);
        Assert.NotEqual(before!.Height, after.Height, 1);
        Assert.Equal(canvas.SceneCache.Current!.Height, after.Height, 3);
    }

    /// <summary>R-stk2-2's rule, on the view-model half: a handler left on an editor this canvas no
    /// longer shows repaints a technology it does not draw and keeps that editor alive.</summary>
    [Fact]
    public void RepointingTheViewModelDropsTheOldOnesSubscription()
    {
        var first  = Editor(FourLayerId);
        var second = Editor(TenLayerId);
        var canvas = new StackupCanvas { ViewModel = first };

        canvas.ViewModel = second;
        canvas.MeasureForWidth(PaneWidth);
        Assert.NotNull(canvas.SceneCache.Current);

        // An edit on the ABANDONED editor must not reach this canvas at all.
        first.AddConductorLayerCommand.Execute(null);
        Assert.NotNull(canvas.SceneCache.Current);

        // …while an edit on the one it now shows does.
        second.AddConductorLayerCommand.Execute(null);
        Assert.Null(canvas.SceneCache.Current);
    }

    // ── R-stk2-4 — a null and an empty technology ────────────────────────────────────────────────

    [Fact]
    public void ANullViewModelMeasuresNonZeroAndDrawsNoBands()
    {
        var canvas = new StackupCanvas();

        var size = canvas.MeasureForWidth(PaneWidth);

        Assert.True(size.Height > 0, "a zero-height measure would collapse the splitter's top pane");
        Assert.Empty(canvas.SceneCache.Current!.Bands);
    }

    [Fact]
    public void AnEmptyTechnologyMeasuresNonZeroAndDrawsNoBands()
    {
        var vm = new TechEditorViewModel(
            Path.Combine(Path.GetTempPath(), "empty.ctech"), new Technology());
        var canvas = new StackupCanvas { ViewModel = vm };

        var size = canvas.MeasureForWidth(PaneWidth);

        Assert.True(size.Height > 0);
        Assert.Empty(canvas.SceneCache.Current!.Bands);
    }

    /// <summary>The one width that has to be survivable rather than merely handled: a measure before
    /// the pane has any. The canvas lays out at its stated fallback rather than at zero, because a
    /// zero-width scene is the "too narrow" scene and would be what the first frame drew.</summary>
    [Fact]
    public void AnUnconstrainedWidthLaysOutAtTheStatedFallback()
    {
        var canvas = new StackupCanvas { ViewModel = Editor(FourLayerId) };

        var size = canvas.MeasureForWidth(double.PositiveInfinity);

        Assert.Equal(StackupCanvas.FallbackWidth, size.Width, 3);
        Assert.NotEmpty(canvas.SceneCache.Current!.Bands);
    }

    // ── R-stk2-10's half that is a runtime fact ──────────────────────────────────────────────────

    /// <summary>
    /// Page Up/Down reach <c>TechEditorView.OnScrollKeyDown</c>, which scrolls the ScrollViewer the
    /// FOCUSED control sits inside and falls back to the visible tab's row list. A focusable canvas
    /// inside the drawing's own ScrollViewer would therefore have re-pointed those keys at the
    /// drawing the first time anything clicked it. It takes no focus, so it cannot.
    /// </summary>
    [Fact]
    public void TheCanvasTakesNoFocus_SoItCannotClaimPageUpAndPageDown()
    {
        Assert.False(new StackupCanvas().Focusable);
    }

    // ── The theme resolves without a visual tree ─────────────────────────────────────────────────

    [Fact]
    public void ThemeDefaultsToTheFallbackPalette_BeforeAnyAttach()
    {
        var canvas = new StackupCanvas();
        Assert.Equal(StackupRenderTheme.Fallback.Background, canvas.StackupTheme.Background);
    }
}
