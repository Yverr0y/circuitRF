using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using CircuitRF.Design.Layout;
using CircuitRF.Ui.Controls;
using CircuitRF.Ui.Layout;
using CircuitRF.Ui.Views.Layout;
using Xunit;

// The namespace is NOT `CircuitRF.Ui.Tests.Stackup`, though the folder is — see StackupSceneTests.cs
// for the shadowing that rule exists to avoid.
namespace CircuitRF.Ui.Tests.StackupRender;

/// <summary>
/// <c>Delete</c> removes the selected stackup entry (owner, 2026-09-13).
///
/// <para><b>The deletion itself is not new and is not re-tested here</b> —
/// <c>StackupContextMenuTests</c> already pins <c>RemoveStackupLayer</c>'s behaviour (one undo entry,
/// a spanned conductor's via left alone for the validator to report). What this gate is about is
/// that the keystroke reaches THAT function rather than growing a second deletion beside it, and
/// that it is claimed in exactly the cases it should be: the last test here is the one that would
/// fail if somebody re-implemented the removal.</para>
///
/// <para>There is no input device in this project and no app host, so the keystroke is
/// <see cref="TechEditorView.DeleteKeystrokeTakes"/> and the action is
/// <c>TechEditorViewModel.DeleteSelectedStackupLayer</c> — the same kind of seam
/// <c>StackupCanvas.PressAt</c> and <c>CopyKeystrokeTakes</c> already are. Only the event plumbing
/// is bypassed.</para>
/// </summary>
public class StackupDeleteKeyTests
{
    private const double PaneWidth = 900;
    private const string FourLayerId = "pcb-4layer_FR-4_62mil_1oz";

    /// <summary>The Stackup tab's index in <c>SectionTabs</c>, restated rather than read from the
    /// subject.</summary>
    private const int StackupTab = 1;

    private static TechEditorViewModel Editor() =>
        new(Path.Combine(Path.GetTempPath(), "stackup-delete-key-tests.ctech"),
            ShippedTechnologies.Load(FourLayerId));

    private static StackupCanvas Canvas(TechEditorViewModel vm)
    {
        var canvas = new StackupCanvas { ViewModel = vm };
        canvas.MeasureForWidth(PaneWidth);
        return canvas;
    }

    private static string NameOf(TechEditorViewModel vm, StackupKind kind) =>
        vm.Working.Stackup.Layers.First(l => l.Kind == kind).Name;

    // ── The keystroke ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Both spellings.</b> The key a Mac keyboard labels "delete" is <c>Back</c>; <c>Delete</c> is
    /// the forward-delete above it on a full keyboard. Taking only one of them would leave the
    /// feature doing nothing on one of the two platforms that ship this application — which is why
    /// every other editor here (schematic, symbol, layout, harmonicaRF, wBond) takes both.
    /// </summary>
    [Theory]
    [InlineData(Key.Delete)]
    [InlineData(Key.Back)]
    public void BothDeleteKeysAreTaken(Key key)
        => Assert.True(TechEditorView.DeleteKeystrokeTakes(key, KeyModifiers.None, StackupTab, source: null));

    /// <summary>The gesture is the bare key. A deletion with no confirmation dialog is not one to
    /// take on a near miss, and each of these means something else somewhere else.</summary>
    [Theory]
    [InlineData(Key.Delete, KeyModifiers.Control)]
    [InlineData(Key.Delete, KeyModifiers.Meta)]
    [InlineData(Key.Delete, KeyModifiers.Shift)]
    [InlineData(Key.Delete, KeyModifiers.Alt)]
    [InlineData(Key.Back,   KeyModifiers.Control)]
    [InlineData(Key.Escape, KeyModifiers.None)]
    [InlineData(Key.D,      KeyModifiers.None)]
    public void NoOtherGestureIsClaimed(Key key, KeyModifiers modifiers)
        => Assert.False(TechEditorView.DeleteKeystrokeTakes(key, modifiers, StackupTab, source: null));

    /// <summary>The other three tabs have their own rows and their own selection; a Delete there
    /// would remove something the user is not looking at.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(-1)]
    public void TheKeystrokeIsNotTakenOnAnyOtherTab(int tabIndex)
        => Assert.False(TechEditorView.DeleteKeystrokeTakes(Key.Delete, KeyModifiers.None, tabIndex, source: null));

    /// <summary>
    /// <b>It never takes the key from a field being typed in.</b> This is not a duplicate of the
    /// routing rule: Avalonia's <c>TextBox</c> marks these keys handled only when they actually
    /// deleted a character, so <c>Back</c> with the caret at the start — or <c>Delete</c> at the end —
    /// reaches this handler. Without the check, a stackup entry would be deleted while somebody was
    /// editing a card's name. The inline value editor over the drawing is a <c>TextBox</c> too, so
    /// this covers brief 4's box as well as the cards' fields.
    /// </summary>
    [Fact]
    public void TheKeystrokeIsNotTakenFromATextInput()
    {
        Assert.False(TechEditorView.DeleteKeystrokeTakes(
            Key.Delete, KeyModifiers.None, StackupTab, new Avalonia.Controls.TextBox()));
        Assert.False(TechEditorView.DeleteKeystrokeTakes(
            Key.Back, KeyModifiers.None, StackupTab, new Avalonia.Controls.TextBox()));
        Assert.False(TechEditorView.DeleteKeystrokeTakes(
            Key.Delete, KeyModifiers.None, StackupTab, new Avalonia.Controls.SelectableTextBlock()));
    }

    // ── What it acts on ───────────────────────────────────────────────────────────────────────────

    /// <summary>Each kind, selected by CLICKING it on the drawing exactly as the owner's ask
    /// describes, then deleted — one entry gone, one undo entry, and the undo puts it back.</summary>
    [Theory]
    [InlineData(StackupKind.Conductor)]
    [InlineData(StackupKind.Dielectric)]
    [InlineData(StackupKind.Via)]
    public void DeletesWhateverWasSelectedOnTheDrawing(StackupKind kind)
    {
        var vm = Editor();
        var canvas = Canvas(vm);
        string name = NameOf(vm, kind);
        int before = vm.Working.Stackup.Layers.Count;
        Assert.False(vm.UndoRedo.CanUndo);      // a fresh editor: the entry below is this deletion's

        // The click, through the canvas's own seam — the selection this acts on is the one the
        // drawing makes, not one written straight into the view model by the test.
        canvas.PressAt(PointOn(canvas, name));
        Assert.Equal(name, vm.SelectedStackupLayerName);

        Assert.True(vm.DeleteSelectedStackupLayer());

        Assert.Equal(before - 1, vm.Working.Stackup.Layers.Count);
        Assert.DoesNotContain(vm.Working.Stackup.Layers, l => l.Name == name);

        Assert.True(vm.UndoRedo.CanUndo);
        vm.UndoCommand.Execute(null);
        Assert.False(vm.UndoRedo.CanUndo);
        Assert.Equal(before, vm.Working.Stackup.Layers.Count);
        Assert.Contains(vm.Working.Stackup.Layers, l => l.Name == name);
    }

    /// <summary>
    /// <b>Nothing selected is not handled.</b> The keystroke falls through unmarked rather than being
    /// swallowed by a view with nothing to delete — so a Delete typed with no selection still reaches
    /// whatever else might want it, and nothing is removed on a guess.
    /// </summary>
    [Fact]
    public void WithNothingSelectedItDeletesNothingAndSaysSo()
    {
        var vm = Editor();
        int before = vm.Working.Stackup.Layers.Count;

        Assert.Null(vm.SelectedStackupLayerName);
        Assert.False(vm.DeleteSelectedStackupLayer());

        Assert.Equal(before, vm.Working.Stackup.Layers.Count);
        Assert.False(vm.UndoRedo.CanUndo);
    }

    /// <summary>
    /// The selection is held BY NAME (R-stk3-1) and the deletion has just made that name name
    /// nothing. Left standing it would outline nothing on the drawing and shade no card while the
    /// editor still claimed something was selected — and a second Delete would do nothing with no
    /// visible reason. Cleared; and the second press reports that there was nothing to take.
    /// </summary>
    [Fact]
    public void DeletingClearsTheSelectionItJustEmptied()
    {
        var vm = Editor();
        vm.SelectedStackupLayerName = NameOf(vm, StackupKind.Conductor);
        Assert.NotNull(vm.SelectedStackupLayerRow);

        Assert.True(vm.DeleteSelectedStackupLayer());

        Assert.Null(vm.SelectedStackupLayerName);
        Assert.Null(vm.SelectedStackupLayerRow);
        Assert.DoesNotContain(vm.StackupLayers, r => r.IsSelected);

        Assert.False(vm.DeleteSelectedStackupLayer());
    }

    /// <summary>The ✕ on the card clears it too — one function serves all three gestures, so it could
    /// hardly not, and this is what says that is deliberate rather than incidental.</summary>
    [Fact]
    public void TheCardsOwnRemoveClearsTheSelectionAsWell()
    {
        var vm = Editor();
        var row = vm.StackupLayers.First(r => r.Layer.Kind == StackupKind.Dielectric);
        vm.SelectedStackupLayerName = row.Layer.Name;

        row.RemoveCommand.Execute(null);

        Assert.Null(vm.SelectedStackupLayerName);
    }

    /// <summary>A selection on a DIFFERENT entry is left exactly where it is — deleting the card's ✕
    /// row does not disturb what the user has selected elsewhere.</summary>
    [Fact]
    public void DeletingAnotherEntryLeavesTheSelectionAlone()
    {
        var vm = Editor();
        string kept = NameOf(vm, StackupKind.Conductor);
        vm.SelectedStackupLayerName = kept;

        var other = vm.StackupLayers.First(r => r.Layer.Kind == StackupKind.Dielectric);
        other.RemoveCommand.Execute(null);

        Assert.Equal(kept, vm.SelectedStackupLayerName);
        Assert.NotNull(vm.SelectedStackupLayerRow);    // still resolves, after a rebuild
    }

    // ── One deletion, more than one way in ────────────────────────────────────────────────────────

    /// <summary>
    /// <b>The test this file exists for.</b> The keystroke and the card's ✕ must be two ways into ONE
    /// function — a second deletion written for the keyboard would drift from the others silently,
    /// and the first thing to drift would be the undo entry. Two fresh editors, the same entry, the
    /// two gestures: identical documents afterwards.
    ///
    /// <para>The MENU is the third way in and is compared against this one in
    /// <c>StackupContextMenuTests</c> rather than here: constructing an Avalonia <c>MenuItem</c> from
    /// two test classes at once races that type's own static registration ("an item with the same key
    /// has already been added"), and xUnit runs classes in parallel. One class builds menus.</para>
    /// </summary>
    [Fact]
    public void TheKeystrokeAndTheCardAreOneDeletion()
    {
        string ViaKeystroke()
        {
            var vm = Editor();
            vm.SelectedStackupLayerName = NameOf(vm, StackupKind.Conductor);
            Assert.True(vm.DeleteSelectedStackupLayer());
            return TechPersistence.Serialize(vm.Working);
        }

        string ViaCard()
        {
            var vm = Editor();
            string name = NameOf(vm, StackupKind.Conductor);
            vm.StackupLayers.First(r => r.Layer.Name == name).RemoveCommand.Execute(null);
            return TechPersistence.Serialize(vm.Working);
        }

        Assert.Equal(ViaKeystroke(), ViaCard());
    }

    // ── Where a band is ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A point on the drawing that really does resolve to <paramref name="name"/> — the same helper
    /// <c>StackupContextMenuTests.PointOn</c> is, and not the centre of the rect for the reason
    /// stated there: a via barrel is drawn ACROSS the bands it spans and takes hit-test precedence,
    /// so the middle of a conductor on this board is a via.
    /// </summary>
    private static Point PointOn(StackupCanvas canvas, string name)
    {
        canvas.MeasureForWidth(PaneWidth);
        var scene = canvas.SceneCache.Current!;
        var rect  = scene.Bands.FirstOrDefault(b => b.Name == name)?.Rect
                 ?? scene.Barrels.First(b => b.Name == name).Rect;

        for (int i = 1; i < 64; i++)
        {
            var p = new Point(rect.Left + rect.Width * i / 64f, rect.MidY);
            if (scene.HitTest((float)p.X, (float)p.Y)?.LayerName == name) return p;
        }
        throw new InvalidOperationException($"No point on the drawing resolves to \"{name}\".");
    }
}
