using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
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
/// brief-stackup-render-5-drag.md's gate — reorder a band, re-span a via, slide it sideways.
///
/// <h3>What stands in for a pointer</h3>
/// <para>There is no input device in this project and no application host, so a gesture is
/// <see cref="StackupCanvas.PressAt"/> / <c>MoveAt</c> / <c>ReleaseAt</c> — the same internal seam
/// brief 3's gate uses, and for the same reason. What they exercise is the real control over the real
/// scene, driving the real <c>StackupDragController</c>; only the event plumbing is bypassed.</para>
///
/// <h3>Every coordinate comes out of the scene</h3>
/// <para>Nothing here hardcodes a pixel. A band's rect, a barrel's rect, a gripper's hit rect and the
/// band column are all read back from the <see cref="StackupScene"/> the canvas is showing — which is
/// R-stk1-1 restated from the test side: a gate that re-derived where a band is would pass over a
/// picture nobody drew.</para>
/// </summary>
public class StackupDragTests
{
    private const double PaneWidth = 900;
    private const string FourLayerId = "pcb-4layer_FR-4_62mil_1oz";

    private static TechEditorViewModel Editor(Technology tech) =>
        new(Path.Combine(Path.GetTempPath(), "stackup-drag-tests.ctech"), tech);

    private static TechEditorViewModel Editor(string id = FourLayerId) =>
        Editor(ShippedTechnologies.Load(id));

    private static StackupCanvas Canvas(TechEditorViewModel vm)
    {
        var canvas = new StackupCanvas { ViewModel = vm };
        canvas.MeasureForWidth(PaneWidth);
        return canvas;
    }

    private static StackupScene SceneOf(StackupCanvas canvas) => canvas.SceneCache.Current!;

    /// <summary>A point inside <paramref name="band"/> that actually hit-tests to it — labels and
    /// barrels sit over the band column, so its geometric middle is not necessarily its own.</summary>
    private static Point PointInBand(StackupScene scene, StackupBand band)
    {
        float y = band.Rect.MidY;
        for (float x = band.Rect.Left + 2; x < band.Rect.Right - 2; x += 3)
            if (scene.HitTest(x, y) is { Kind: StackupHitKind.Band } hit && hit.LayerName == band.Name)
                return new Point(x, y);
        throw new InvalidOperationException($"no point of band {band.Name} hit-tests to it");
    }

    private static StackupBarrel Barrel(StackupScene scene, string name) =>
        scene.Barrels.First(b => b.Name == name);

    private static IReadOnlyList<string> Order(TechEditorViewModel vm) =>
        [.. vm.Working.Stackup.Layers.Select(l => l.Name)];

    private static IReadOnlyList<string> BandOrder(TechEditorViewModel vm) =>
        [.. vm.Working.Stackup.Layers.Where(l => l.Kind != StackupKind.Via).Select(l => l.Name)];

    /// <summary>
    /// A stackup whose via entries lie BETWEEN the bands, which no shipped technology does — both
    /// shipped ones list their vias after every band. R-stk5-1's second rule ("a band swaps with the
    /// next non-via band, stepping over any via entries lying between them in the list") is
    /// unexercised by a stackup where nothing lies between them, so the fixture that tests it has to
    /// interleave them on purpose.
    /// </summary>
    private static Technology Interleaved()
    {
        var top = new LayerKey(1, 0);
        var mid = new LayerKey(2, 0);
        var bot = new LayerKey(3, 0);
        return new Technology
        {
            Name = "Interleaved",
            Layers =
            [
                new LayerDef { Key = top, Name = "TopCu", Color = new Rgba(0xC8, 0x7A, 0x3E) },
                new LayerDef { Key = mid, Name = "MidCu", Color = new Rgba(0xA0, 0x60, 0x30) },
                new LayerDef { Key = bot, Name = "BotCu", Color = new Rgba(0x8A, 0x50, 0x28) },
            ],
            Stackup = new Stackup
            {
                Layers =
                [
                    new StackupLayer { Kind = StackupKind.Conductor,  Name = "Top",   ThicknessDbu =    35_000, SigmaSm = 5.8e7, DrawingLayers = [top] },
                    new StackupLayer { Kind = StackupKind.Via,        Name = "ViaA",  SpanFromLayer = "Top",   SpanToLayer = "Mid" },
                    new StackupLayer { Kind = StackupKind.Dielectric, Name = "Pre",   ThicknessDbu =   200_000, Epsr = 4.4 },
                    new StackupLayer { Kind = StackupKind.Conductor,  Name = "Mid",   ThicknessDbu =    35_000, SigmaSm = 5.8e7, DrawingLayers = [mid] },
                    new StackupLayer { Kind = StackupKind.Via,        Name = "ViaB",  SpanFromLayer = "Top",   SpanToLayer = "Bot" },
                    new StackupLayer { Kind = StackupKind.Dielectric, Name = "Core",  ThicknessDbu = 1_200_000, Epsr = 4.4 },
                    new StackupLayer { Kind = StackupKind.Conductor,  Name = "Bot",   ThicknessDbu =    35_000, SigmaSm = 5.8e7, DrawingLayers = [bot], IsGroundReference = true },
                ],
            },
        };
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  R-stk5-1 — the drag means exactly what the arrows mean
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The rule the whole brief rests on.</b> The drag must produce the same sequence of moves a
    /// user would get by clicking the card's ↑/↓ — including over a stack whose via entries lie
    /// between the bands, where "step over the via and leave its index alone" is the part that is
    /// easy to get wrong and was decided for a reason: a via's list position is not z.
    /// </summary>
    [Fact]
    public void AReorderDragOverAStackWithInterleavedVias_LandsWhereTheArrowsWould()
    {
        var dragged = Editor(Interleaved());
        var clicked = Editor(Interleaved());

        var canvas = Canvas(dragged);
        var scene = SceneOf(canvas);

        // "Top" (band 0) to the bottom of the five-band z order, in one gesture.
        var band = scene.Bands[0];
        var from = PointInBand(scene, band);
        var to = new Point(from.X, scene.Bands[^1].Rect.Bottom - 1);

        canvas.PressAt(from);
        canvas.MoveAt(to);
        Assert.True(canvas.ReleaseAt(to));

        // …and the same thing, four clicks of ↓ on the card. The row VM is re-looked-up each time
        // because every committed edit rebuilds every one of them (R-stk3-1).
        for (int i = 0; i < 4; i++)
            clicked.StackupLayers.First(r => r.Layer.Name == "Top").MoveDownCommand.Execute(null);

        Assert.Equal(Order(clicked), Order(dragged));
        Assert.Equal(["Pre", "Mid", "Core", "Bot", "Top"], BandOrder(dragged));

        // The via entries never moved — their indices carry no meaning, and leaving them alone is
        // what "the z order is the order of the non-via entries" means in the list.
        Assert.Equal(1, Order(dragged).ToList().IndexOf("ViaA"));
        Assert.Equal(4, Order(dragged).ToList().IndexOf("ViaB"));
    }

    /// <summary>R-stk5-1's first rule, from the drag side: a via is never reordered by any gesture on
    /// it. A barrel drag is a legitimate edit — it re-spans — but the ORDER of the stackup list must
    /// come back untouched.</summary>
    [Fact]
    public void ADragOnAViaNeverReordersAnything()
    {
        var vm = Editor(Interleaved());
        var canvas = Canvas(vm);
        var scene = SceneOf(canvas);
        var before = Order(vm);

        var barrel = Barrel(scene, "ViaA");
        var p0 = new Point(barrel.Rect.MidX, barrel.Rect.MidY);

        canvas.PressAt(p0);
        canvas.MoveAt(new Point(p0.X, p0.Y + 400));
        canvas.ReleaseAt(new Point(p0.X, p0.Y + 400));

        Assert.Equal(before, Order(vm));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  R-stk5-2 — one drag, one undo entry
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Four bands crossed, ONE entry. Four <c>MoveStackupLayer</c> calls would push four, and one
    /// Ctrl-Z would put the band back one place — which reads as a broken undo, and is the whole
    /// reason <c>MoveStackupLayerTo</c> exists.
    /// </summary>
    [Fact]
    public void DraggingABandAcrossFourOthers_PushesExactlyOneUndoEntry_AndOneUndoRestoresIt()
    {
        var vm = Editor();                       // 7 bands, 2 vias
        var canvas = Canvas(vm);
        var scene = SceneOf(canvas);
        var before = BandOrder(vm);
        Assert.False(vm.UndoRedo.CanUndo);       // a fresh editor: every entry below is this drag's

        var band = scene.Bands[0];
        var from = PointInBand(scene, band);
        var to = new Point(from.X, PointInBand(scene, scene.Bands[4]).Y);

        canvas.PressAt(from);
        canvas.MoveAt(to);
        Assert.True(canvas.ReleaseAt(to));

        Assert.NotEqual(before, BandOrder(vm));

        // ONE entry: after a single Undo the order is back AND there is nothing left to undo. Four
        // MoveStackupLayer calls would leave three.
        vm.UndoCommand.Execute(null);
        Assert.Equal(before, BandOrder(vm));
        Assert.False(vm.UndoRedo.CanUndo);
    }

    /// <summary>
    /// <c>MoveStackupLayer(row, ±1)</c> is now written in terms of <c>MoveStackupLayerTo</c>, so the
    /// two cannot disagree by construction — which is worth pinning anyway, over every single-step
    /// case on every shipped technology, because "expressed in terms of" is a claim about a rewrite
    /// that a later edit could quietly undo.
    /// </summary>
    [Fact]
    public void TheSingleStepMoveAndTheMoveTo_AgreeForEverySingleStepCaseOnEveryShippedTechnology()
    {
        foreach (var entry in ShippedTechnologies.All)
        {
            var probe = Editor(ShippedTechnologies.Load(entry.Id));
            int bandCount = probe.Working.Stackup.Layers.Count(l => l.Kind != StackupKind.Via);

            for (int b = 0; b < bandCount; b++)
                foreach (int dir in new[] { -1, 1 })
                {
                    var stepped = Editor(ShippedTechnologies.Load(entry.Id));
                    var jumped = Editor(ShippedTechnologies.Load(entry.Id));

                    var sRow = stepped.StackupLayers.Where(r => !r.IsVia).ElementAt(b);
                    var jRow = jumped.StackupLayers.Where(r => !r.IsVia).ElementAt(b);

                    if (dir < 0) sRow.MoveUpCommand.Execute(null); else sRow.MoveDownCommand.Execute(null);
                    jumped.MoveStackupLayerTo(jRow, b + dir);

                    Assert.Equal(Order(stepped), Order(jumped));
                }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  R-stk5-3 — the threshold, Esc, and the release that lands outside
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Without a movement threshold every click nudges the stack, because a press and a
    /// release at the "same" place differ by a pixel or two on any real device.</summary>
    [Fact]
    public void APressAndReleaseBelowTheThreshold_SelectsAndDoesNotReorder()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = SceneOf(canvas);
        var before = BandOrder(vm);
        Assert.False(vm.UndoRedo.CanUndo);       // a fresh editor: every entry below is this drag's

        var band = scene.Bands[1];
        var p = PointInBand(scene, band);
        var nudge = new Point(p.X + StackupDragController.Threshold - 1, p.Y);

        canvas.PressAt(p);
        canvas.MoveAt(nudge);
        Assert.Equal(StackupDragKind.None, canvas.DragKind);
        Assert.False(canvas.ReleaseAt(nudge));

        Assert.Equal(before, BandOrder(vm));
        Assert.False(vm.UndoRedo.CanUndo);
        Assert.Equal(band.Name, vm.SelectedStackupLayerName);
    }

    [Fact]
    public void EscMidDrag_LeavesTheModelUntouchedAndPushesNothing()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = SceneOf(canvas);
        var before = BandOrder(vm);
        Assert.False(vm.UndoRedo.CanUndo);       // a fresh editor: every entry below is this drag's

        var from = PointInBand(scene, scene.Bands[0]);
        var to = new Point(from.X, PointInBand(scene, scene.Bands[4]).Y);

        canvas.PressAt(from);
        canvas.MoveAt(to);
        Assert.Equal(StackupDragKind.Reorder, canvas.DragKind);

        Assert.True(canvas.CancelDrag());
        Assert.Equal(StackupDragKind.None, canvas.DragKind);

        // …and the release that follows the key writes nothing either.
        Assert.False(canvas.ReleaseAt(to));
        Assert.Equal(before, BandOrder(vm));
        Assert.False(vm.UndoRedo.CanUndo);

        // Esc with no gesture live declines, which is what lets the keystroke fall through to
        // brief 3's clear-selection.
        Assert.False(canvas.CancelDrag());
    }

    [Fact]
    public void ReleasingOutsideTheCanvas_Cancels()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = SceneOf(canvas);
        var before = BandOrder(vm);
        Assert.False(vm.UndoRedo.CanUndo);       // a fresh editor: every entry below is this drag's

        var from = PointInBand(scene, scene.Bands[0]);
        canvas.PressAt(from);
        canvas.MoveAt(new Point(from.X, from.Y + 200));
        Assert.True(canvas.DragKind == StackupDragKind.Reorder);

        Assert.False(canvas.ReleaseAt(new Point(from.X, scene.Height + 50)));
        Assert.Equal(before, BandOrder(vm));
        Assert.False(vm.UndoRedo.CanUndo);
    }

    /// <summary>R-stk5-3's chrome: a ghost of the thing being moved AND the line it would drop on.
    /// The insertion line snaps to a non-via band boundary — the same rule the reorder itself
    /// obeys.</summary>
    [Fact]
    public void WhileDragging_TheOverlayCarriesAGhostAndAnInsertionLineOnABandBoundary()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = SceneOf(canvas);

        var from = PointInBand(scene, scene.Bands[0]);
        canvas.PressAt(from);
        canvas.MoveAt(new Point(from.X, PointInBand(scene, scene.Bands[3]).Y));

        var overlay = canvas.CurrentOverlay;
        Assert.NotNull(overlay.DragGhost);
        var insertY = Assert.NotNull(overlay.DragInsertY);

        var boundaries = new List<float> { scene.Bands[0].Rect.Top };
        boundaries.AddRange(scene.Bands.Select(b => b.Rect.Bottom));
        Assert.Contains(insertY, boundaries);

        // …and nothing is drawn once the gesture is over.
        canvas.CancelDrag();
        Assert.Null(canvas.CurrentOverlay.DragGhost);
        Assert.Null(canvas.CurrentOverlay.DragInsertY);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  R-stk5-4 — the barrel moves as a unit
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Both ends step together and the number of conductors spanned is CONSTANT. A barrel that
    /// silently changed length when it hit the top copper would be a via the user did not ask for, so
    /// the span STOPS at the ends of the stack rather than clamping one end and stretching.
    /// </summary>
    [Fact]
    public void ABarrelDragPreservesTheNumberOfConductorsSpanned_AndStopsRatherThanStretching()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = SceneOf(canvas);
        var conductors = scene.Bands.Where(b => b.Kind == StackupKind.Conductor).Select(b => b.Name).ToList();

        // "Ground Via (L1-L2)" spans the first two conductors of the four. The span is captured as
        // STRINGS rather than as the entry: a commit mutates the entry in place before it replaces
        // Working, so a reference held across one compares a value with itself.
        var via = vm.Working.Stackup.Layers.First(l => l.Kind == StackupKind.Via && l.Name.StartsWith("Ground Via", StringComparison.Ordinal));
        string name = via.Name, from0 = via.SpanFromLayer!, to0 = via.SpanToLayer!;
        int spanned = Math.Abs(conductors.IndexOf(to0) - conductors.IndexOf(from0));
        Assert.True(spanned > 0);

        var barrel = Barrel(scene, name);
        var p0 = new Point(barrel.Rect.MidX, barrel.Rect.MidY);

        // Downward one conductor.
        canvas.PressAt(p0);
        var down = new Point(p0.X, p0.Y + (scene.Bands[2].Rect.Top - scene.Bands[0].Rect.Top));
        canvas.MoveAt(down);
        Assert.Equal(StackupDragKind.ViaSpan, canvas.DragKind);
        Assert.True(canvas.ReleaseAt(down));

        var moved = vm.Working.Stackup.Layers.First(l => l.Name == name);
        Assert.Equal(spanned, Math.Abs(conductors.IndexOf(moved.SpanToLayer!) - conductors.IndexOf(moved.SpanFromLayer!)));
        Assert.NotEqual(from0, moved.SpanFromLayer);
        Assert.NotEqual(to0, moved.SpanToLayer);

        // Now drive it far past the bottom: it stops, it does not stretch. The RELEASE lands inside
        // the drawing — a release outside cancels (R-stk5-3), which would make this prove nothing.
        var canvas2 = Canvas(vm);
        var scene2 = SceneOf(canvas2);
        var barrel2 = Barrel(scene2, name);
        var q0 = new Point(barrel2.Rect.MidX, barrel2.Rect.MidY);
        canvas2.PressAt(q0);
        canvas2.MoveAt(new Point(q0.X, q0.Y + 10_000));
        Assert.True(canvas2.ReleaseAt(new Point(q0.X, scene2.Height - 1)));

        var end = vm.Working.Stackup.Layers.First(l => l.Name == name);
        Assert.Equal(spanned, Math.Abs(conductors.IndexOf(end.SpanToLayer!) - conductors.IndexOf(end.SpanFromLayer!)));
        Assert.Equal(conductors[^1], end.SpanToLayer);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  R-stk5-5 — a gripper moves one end
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A gripper snaps to CONDUCTORS only — a via terminating on a dielectric is not expressible in
    /// the model and never should become so — and the two ends may neither cross nor coincide, since
    /// a via spanning one conductor connects nothing. Both are clamps rather than refusals at
    /// release: a gesture that ends in a message is one the user has to undo by reading.
    /// </summary>
    [Fact]
    public void AGripperDragSnapsToConductorsOnly_AndTheEndsCanNeitherCrossNorCoincide()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = SceneOf(canvas);
        var conductors = scene.Bands.Where(b => b.Kind == StackupKind.Conductor).ToList();
        var dielectrics = scene.Bands.Where(b => b.Kind == StackupKind.Dielectric).ToList();

        var through = vm.Working.Stackup.Layers.First(l => l.Kind == StackupKind.Via && l.SpanToLayer == conductors[^1].Name);
        var barrel = Barrel(scene, through.Name);

        // Aim the TOP gripper squarely at a dielectric: it lands on a conductor regardless.
        var grip = new Point(barrel.GripTop.MidX, barrel.GripTop.MidY);
        var ontoDielectric = new Point(grip.X, dielectrics[1].Rect.MidY);

        canvas.PressAt(grip);
        canvas.MoveAt(ontoDielectric);
        Assert.Equal(StackupDragKind.ViaGripTop, canvas.DragKind);
        Assert.True(canvas.ReleaseAt(ontoDielectric));

        var after = vm.Working.Stackup.Layers.First(l => l.Name == through.Name);
        Assert.Contains(after.SpanFromLayer, conductors.Select(c => c.Name));
        Assert.Contains(after.SpanToLayer, conductors.Select(c => c.Name));
        Assert.NotEqual(after.SpanFromLayer, after.SpanToLayer);

        // …and driving the top gripper past the bottom end clamps at the adjacent conductor, which is
        // the minimum meaningful span. It never crosses and it never collapses.
        var canvas2 = Canvas(vm);
        var scene2 = SceneOf(canvas2);
        var barrel2 = Barrel(scene2, through.Name);
        var grip2 = new Point(barrel2.GripTop.MidX, barrel2.GripTop.MidY);
        var wayBelow = new Point(grip2.X, scene2.Height + 5_000);

        canvas2.PressAt(grip2);
        canvas2.MoveAt(wayBelow);
        canvas2.ReleaseAt(new Point(grip2.X, scene2.Height - 1));

        var clamped = vm.Working.Stackup.Layers.First(l => l.Name == through.Name);
        var names = conductors.Select(c => c.Name).ToList();
        int a = names.IndexOf(clamped.SpanFromLayer!), b = names.IndexOf(clamped.SpanToLayer!);
        Assert.True(a >= 0 && b >= 0);
        Assert.Equal(1, Math.Abs(a - b));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  R-stk5-6 — one undo entry, and the same bytes the combo boxes would have written
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The drag is not a second way to say what the card's two <c>Spans</c> combo boxes say. One
    /// entry, and the technology that results is byte-identical to the one two combo-box commits
    /// produce.
    /// </summary>
    [Fact]
    public void AViaDragIsOneUndoEntry_AndTheResultMatchesTheComboBoxesByte()
    {
        var dragged = Editor();
        var typed = Editor();

        var canvas = Canvas(dragged);
        var scene = SceneOf(canvas);
        var conductors = scene.Bands.Where(b => b.Kind == StackupKind.Conductor).Select(b => b.Name).ToList();

        var via = dragged.Working.Stackup.Layers.First(l => l.Kind == StackupKind.Via && l.Name.StartsWith("Ground Via", StringComparison.Ordinal));
        var barrel = Barrel(scene, via.Name);
        Assert.False(dragged.UndoRedo.CanUndo);

        var p0 = new Point(barrel.Rect.MidX, barrel.Rect.MidY);
        var down = new Point(p0.X, p0.Y + (scene.Bands[2].Rect.Top - scene.Bands[0].Rect.Top));
        canvas.PressAt(p0);
        canvas.MoveAt(down);
        Assert.True(canvas.ReleaseAt(down));

        // ONE entry for the whole gesture: a single Undo puts the span back and leaves nothing.
        var restore = TechPersistence.Serialize(dragged.Working);
        dragged.UndoCommand.Execute(null);
        Assert.False(dragged.UndoRedo.CanUndo);
        dragged.RedoCommand.Execute(null);
        Assert.Equal(restore, TechPersistence.Serialize(dragged.Working));

        var moved = dragged.Working.Stackup.Layers.First(l => l.Name == via.Name);
        var row = typed.StackupLayers.First(r => r.Layer.Name == via.Name);
        row.SelectedSpanFrom = moved.SpanFromLayer!;
        typed.StackupLayers.First(r => r.Layer.Name == via.Name).SelectedSpanTo = moved.SpanToLayer!;

        Assert.Equal(TechPersistence.Serialize(typed.Working), TechPersistence.Serialize(dragged.Working));
        Assert.Contains(moved.SpanFromLayer, conductors);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  R-stk5-10 — the lateral lane, and the dominant-axis lock
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AHorizontalBarrelDragWritesTheLane_AndTheBarrelMovesWithIt()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = SceneOf(canvas);

        var via = vm.Working.Stackup.Layers.First(l => l.Kind == StackupKind.Via);
        Assert.Null(via.DrawLaneFraction);

        var barrel = Barrel(scene, via.Name);
        var p0 = new Point(barrel.Rect.MidX, barrel.Rect.MidY);
        var left = new Point(p0.X - 120, p0.Y + 2);

        canvas.PressAt(p0);
        canvas.MoveAt(left);
        Assert.Equal(StackupDragKind.ViaLane, canvas.DragKind);
        Assert.True(canvas.ReleaseAt(left));

        var after = vm.Working.Stackup.Layers.First(l => l.Name == via.Name);
        double lane = Assert.NotNull(after.DrawLaneFraction);
        Assert.InRange(lane, 0d, 1d);

        // The default spread is on the RIGHT of the band column, so a drag to the left must have
        // reduced the fraction — and the picture must agree with the number.
        var after2 = SceneOf(Canvas(vm));
        var moved = Barrel(after2, via.Name);
        Assert.True(moved.Rect.MidX < barrel.Rect.MidX);
        Assert.Equal(after2.BandColumn.Left + (float)lane * after2.BandColumn.Width, moved.Rect.MidX, 0.5);

        // The span is untouched: a lateral drag is cosmetic and nothing else.
        Assert.Equal(via.SpanFromLayer, after.SpanFromLayer);
        Assert.Equal(via.SpanToLayer, after.SpanToLayer);
    }

    /// <summary>Snapping is the default because "nice looking stackups" means ALIGNED ones, and a
    /// free-drag-only control makes alignment a matter of eyesight. <c>Alt</c> turns it off.</summary>
    [Fact]
    public void TheLaneSnapsByDefault_AndAltPositionsFreely()
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        var scene = SceneOf(canvas);
        var via = vm.Working.Stackup.Layers.First(l => l.Kind == StackupKind.Via);
        var barrel = Barrel(scene, via.Name);

        var p0 = new Point(barrel.Rect.MidX, barrel.Rect.MidY);
        // A deliberately awkward offset, so a snapped answer and a free one cannot coincide.
        var target = new Point(p0.X - 97.5, p0.Y);

        canvas.PressAt(p0);
        canvas.MoveAt(target);
        double snapped = Assert.NotNull(canvas.Drag.PreviewLane);
        canvas.CancelDrag();

        canvas.PressAt(p0);
        canvas.MoveAt(target, freeLane: true);
        double free = Assert.NotNull(canvas.Drag.PreviewLane);
        canvas.CancelDrag();

        Assert.Equal(snapped * StackupDragController.LaneSnapDivisions,
                     Math.Round(snapped * StackupDragController.LaneSnapDivisions), 6);
        Assert.NotEqual(snapped, free, 6);
    }

    /// <summary>
    /// <b>The dominant axis is read once, at the threshold, and never revisited.</b> A barrel that
    /// re-spanned itself because the user's hand drifted upward while sliding it sideways is the
    /// defect this rule exists to prevent — so a mostly-horizontal drag never changes the span, and a
    /// mostly-vertical one never changes the lane, however the rest of the gesture wanders.
    /// </summary>
    [Fact]
    public void TheDominantAxisIsLockedAtTheThreshold_AndLaterDriftDoesNotChangeIt()
    {
        // Mostly horizontal, then a long vertical wander: the span must not move.
        var h = Editor();
        var hCanvas = Canvas(h);
        var hScene = SceneOf(hCanvas);
        var hVia = h.Working.Stackup.Layers.First(l => l.Kind == StackupKind.Via);
        string hName = hVia.Name, hFrom = hVia.SpanFromLayer!, hTo = hVia.SpanToLayer!;
        var hBarrel = Barrel(hScene, hName);
        var hp = new Point(hBarrel.Rect.MidX, hBarrel.Rect.MidY);
        var hEnd = new Point(hp.X - 60, hScene.Height - 1);

        hCanvas.PressAt(hp);
        hCanvas.MoveAt(new Point(hp.X - 40, hp.Y + 1));   // axis decided: horizontal
        hCanvas.MoveAt(hEnd);                             // …and then all the way down the drawing
        Assert.Equal(StackupDragKind.ViaLane, hCanvas.DragKind);
        Assert.True(hCanvas.ReleaseAt(hEnd));

        var hAfter = h.Working.Stackup.Layers.First(l => l.Name == hName);
        Assert.Equal(hFrom, hAfter.SpanFromLayer);
        Assert.Equal(hTo, hAfter.SpanToLayer);
        Assert.NotNull(hAfter.DrawLaneFraction);

        // Mostly vertical, then a long horizontal wander: the lane must not move.
        var v = Editor();
        var vCanvas = Canvas(v);
        var vScene = SceneOf(vCanvas);
        var vVia = v.Working.Stackup.Layers.First(l => l.Kind == StackupKind.Via && l.Name.StartsWith("Ground Via", StringComparison.Ordinal));
        string vName = vVia.Name, vFrom = vVia.SpanFromLayer!;
        var vBarrel = Barrel(vScene, vName);
        var vp = new Point(vBarrel.Rect.MidX, vBarrel.Rect.MidY);
        var vEnd = new Point(vScene.BandColumn.Left + 2, vp.Y + 60);

        vCanvas.PressAt(vp);
        vCanvas.MoveAt(new Point(vp.X + 1, vp.Y + 40));   // axis decided: vertical
        vCanvas.MoveAt(vEnd);                             // …and then all the way across the column
        Assert.Equal(StackupDragKind.ViaSpan, vCanvas.DragKind);
        Assert.True(vCanvas.ReleaseAt(vEnd));

        var vAfter = v.Working.Stackup.Layers.First(l => l.Name == vName);
        Assert.Null(vAfter.DrawLaneFraction);
        Assert.NotEqual(vFrom, vAfter.SpanFromLayer);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    //  R-stk5-9 — the cosmetic-stays-cosmetic gate
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The important test in this brief.</b> <c>DrawLaneFraction</c> is one careless read away
    /// from becoming geometry, and the failure mode if it ever is would be an answer that changes
    /// when someone tidies up a picture. A comment does not prevent that; this does.
    ///
    /// <para>The comparison is a structural signature and an element-for-element walk, not record
    /// equality — <c>PlanarProblem</c> is a record over ARRAYS and its own <c>Equals</c> is reference
    /// equality on the level list, so it would pass on two entirely different problems (the same trap
    /// <c>src/Design/RESOLVED.md</c> records for the ground-override gate).</para>
    /// </summary>
    [Fact]
    public void ATechnologyDifferingOnlyInTheLaneFraction_ExtractsIdentically()
    {
        foreach (var entry in ShippedTechnologies.All)
        {
            var plain = ShippedTechnologies.Load(entry.Id);
            var laned = ShippedTechnologies.Load(entry.Id);

            double f = 0.05;
            bool any = false;
            foreach (var via in laned.Stackup.Layers.Where(l => l.Kind == StackupKind.Via))
            {
                via.DrawLaneFraction = f;
                f = f + 0.17 > 1.0 ? 0.05 : f + 0.17;
                any = true;
            }
            if (!any) continue;

            var shapes = ProbeShapes(plain);
            var a = PlanarExtractor.Extract(shapes, plain, LayoutUnits.DefaultDbuPerMicron, 10e9);
            var b = PlanarExtractor.Extract(shapes, laned, LayoutUnits.DefaultDbuPerMicron, 10e9);

            Assert.Equal(a.Ok, b.Ok);
            Assert.Equal(a.Refusal, b.Refusal);
            Assert.Equal(a.Notes, b.Notes);
            if (!a.Ok) continue;

            Assert.Equal(EmSnpProvenance.GeometryHash(a.Problem!), EmSnpProvenance.GeometryHash(b.Problem!));

            // …and element for element, because a hash is a summary and this is the one field whose
            // leaking into the answer would be invisible in any summary that did not happen to
            // include it.
            var (pa, pb) = (a.Problem!, b.Problem!);
            Assert.Equal(pa.Layers.Count, pb.Layers.Count);
            for (int i = 0; i < pa.Layers.Count; i++)
            {
                Assert.Equal(pa.Layers[i].ZM, pb.Layers[i].ZM);
                Assert.Equal(pa.Layers[i].ThicknessM, pb.Layers[i].ThicknessM);
                Assert.Equal(pa.Layers[i].SigmaSm, pb.Layers[i].SigmaSm);
                Assert.Equal(pa.Layers[i].Polygons.Count, pb.Layers[i].Polygons.Count);
            }
            Assert.Equal(pa.ViaList.Count, pb.ViaList.Count);
            for (int i = 0; i < pa.ViaList.Count; i++)
            {
                Assert.Equal(pa.ViaList[i].LowerLayerIndex, pb.ViaList[i].LowerLayerIndex);
                Assert.Equal(pa.ViaList[i].UpperLayerIndex, pb.ViaList[i].UpperLayerIndex);
                Assert.Equal(pa.ViaList[i].SigmaSm, pb.ViaList[i].SigmaSm);
            }
            Assert.Equal(pa.Slab.HeightM, pb.Slab.HeightM);
            Assert.Equal(pa.EffectiveStack.Layers.Count, pb.EffectiveStack.Layers.Count);
        }
    }

    /// <summary>A strip of metal on every conductor drawing layer the technology has, so the
    /// extraction has something to do. What it IS does not matter — only that it is the same on both
    /// sides of the comparison.</summary>
    private static IReadOnlyList<LayoutShape> ProbeShapes(Technology tech)
    {
        var shapes = new List<LayoutShape>();
        long x = 0;
        foreach (var conductor in tech.Stackup.Layers.Where(l => l.Kind == StackupKind.Conductor))
            foreach (var key in conductor.DrawingLayers)
            {
                shapes.Add(new RectShape
                {
                    Layer = key,
                    X1 = x,
                    Y1 = 0,
                    X2 = x + 200L * LayoutUnits.DefaultDbuPerMicron,
                    Y2 = 50L * LayoutUnits.DefaultDbuPerMicron,
                });
                x += 400L * LayoutUnits.DefaultDbuPerMicron;
            }
        return shapes;
    }
}
