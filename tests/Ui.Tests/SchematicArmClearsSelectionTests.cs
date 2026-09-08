using Avalonia.Input;
using CircuitRF.Core.Design;
using CircuitRF.Ui.Schematic;
using CircuitRF.Ui.ViewModels;
using Xunit;

namespace CircuitRF.Ui.Tests;

/// <summary>
/// Owner report, 2026-09-07: arming Pin / Term / GND from the schematic toolbar left whatever was
/// selected still selected, so R rotated the SELECTION instead of the armed ghost.
///
/// <para>Not a keybinding bug. <c>RotateSelection</c>/<c>MirrorSelection</c> already fall back to the
/// armed placement's own rotation — but only when nothing is selected, which is the correct rule and
/// the one the toolbar was breaking. The palette arms through <c>PlacementService</c>, whose R has a
/// path of its own, so the fault was visible only on the toolbar buttons and their P / T / Shift+G
/// shortcuts.</para>
///
/// <para>Delete is the sharper reason this had to change: with a selection still live under an armed
/// placement, the key that would have discarded the ghost deleted real components instead.</para>
/// </summary>
public class SchematicArmClearsSelectionTests
{
    /// <summary>Places one resistor by the ordinary armed-placement gesture and leaves it selected —
    /// which is what CommitPlacement does, and so is the exact state the report starts from.</summary>
    private static (SchematicEditModel Model, SchematicViewModel Vm, string Id) WithOnePlacedPart()
    {
        var model = new SchematicEditModel();
        var vm    = new SchematicViewModel(model);

        vm.BeginPlacement(SymbolKind.Resistor);
        vm.OnPointerMoved(700, 300, leftDown: false);
        vm.OnPointerPressed(700, 300, KeyModifiers.None);

        Assert.Single(model.Components);
        return (model, vm, model.Components[0].Id);
    }

    [Fact]
    public void ArmingFromTheToolbar_ClearsTheSelection()
    {
        var (_, vm, id) = WithOnePlacedPart();
        vm.Selection.SetAll([id]);
        Assert.NotEmpty(vm.Selection.Ids);

        vm.BeginPlacement(SymbolKind.Pin);

        Assert.Empty(vm.Selection.Ids);
        Assert.Equal(SchematicViewModel.Tool.Place, vm.ActiveTool);
        Assert.Equal(SymbolKind.Pin, vm.PlacementSymbol);
    }

    [Theory]
    [InlineData(SymbolKind.Pin)]
    [InlineData(SymbolKind.Term)]
    [InlineData(SymbolKind.Ground)]
    public void R_RotatesTheArmedGhost_NotTheStillSelectedPart(SymbolKind armed)
    {
        var (model, vm, id) = WithOnePlacedPart();
        vm.Selection.SetAll([id]);
        var rotationBefore = model.Components[0].Rotation;
        var undoBefore     = vm.UndoRedo.UndoDescription;

        vm.BeginPlacement(armed);
        Assert.Equal(SymbolRotation.R0, vm.CurrentPlacementRotation);

        Assert.True(vm.OnKeyDown(Key.R, KeyModifiers.None));

        Assert.Equal(SymbolRotation.R90, vm.CurrentPlacementRotation);
        Assert.Equal(rotationBefore, model.Components[0].Rotation);
        // Rotating a ghost is not an edit: nothing new on the undo stack.
        Assert.Equal(undoBefore, vm.UndoRedo.UndoDescription);
    }

    [Fact]
    public void Delete_WithAPlacementArmed_NoLongerReachesTheOldSelection()
    {
        var (model, vm, id) = WithOnePlacedPart();
        vm.Selection.SetAll([id]);

        vm.BeginPlacement(SymbolKind.Ground);
        Assert.True(vm.OnKeyDown(Key.Delete, KeyModifiers.None));

        Assert.Single(model.Components);
    }
}
