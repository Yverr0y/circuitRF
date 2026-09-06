using CircuitRF.Ui.Commands.Schematic;
using CircuitRF.Ui.Schematic;
using CircuitRF.Ui.ViewModels;

namespace CircuitRF.Ui.Tests;

/// <summary>
/// An IProbe placed onto a wire clears the stretch of wire between its own two pins, so the probe
/// is in series with the run instead of shorted by it — but only at PLACEMENT, and only when the
/// cut provably changes no circuit. See <see cref="SeriesProbeInsertion"/>.
///
/// <para>Grid snap is off in these fixtures on purpose: it lets a junction be put at the MIDDLE of
/// the span between the two pins, which the shipped 100-unit connection grid has no coordinate for
/// (the probe's pins are exactly one pitch apart). The rule under test is geometric, not grid-bound.</para>
/// </summary>
public class IProbePlacementClearsWireTests
{
    // R0 IProbe pins sit at (X, Y+100) and (X+100, Y+100) — so this places the two pins at
    // (px, py) and (px+100, py).
    private const double PinDrop = 100.0;

    private static (SchematicEditModel Model, SchematicViewModel Vm) MakeVm()
    {
        var model = new SchematicEditModel { GridSnap = false };
        return (model, new SchematicViewModel(model));
    }

    private static EditableWire AddWire(SchematicEditModel m, params (double X, double Y)[] pts)
    {
        var w = new EditableWire();
        w.Points.AddRange(pts);
        m.Wires.Add(w);
        return w;
    }

    private static void PlaceProbe(SchematicViewModel vm, double pinX, double pinY,
                                   SymbolRotation rot = SymbolRotation.R0)
        => vm.CommitPlacement(SymbolKind.IProbe, 2, rot, pinX, pinY - PinDrop);

    private static (double X, double Y)[] Pts(EditableWire w) => w.Points.ToArray();

    // ── The affordance ────────────────────────────────────────────────────────

    [Fact]
    public void PlacedOnHorizontalWire_RemovesOnlyTheSpanBetweenThePins()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 0), (500, 0));

        PlaceProbe(vm, 200, 0);

        Assert.Single(model.Components);
        Assert.Equal(2, model.Wires.Count);
        Assert.Equal([(0.0, 0.0), (200.0, 0.0)],   Pts(model.Wires[0]));
        Assert.Equal([(300.0, 0.0), (500.0, 0.0)], Pts(model.Wires[1]));
    }

    [Fact]
    public void PlacedOnVerticalWire_RemovesOnlyTheSpanBetweenThePins()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 0), (0, 500));

        // R90 puts both pins on x = X-100, at y = Y and y = Y+100.
        vm.CommitPlacement(SymbolKind.IProbe, 2, SymbolRotation.R90, 100, 200);

        Assert.Equal(2, model.Wires.Count);
        Assert.Equal([(0.0, 0.0), (0.0, 200.0)],   Pts(model.Wires[0]));
        Assert.Equal([(0.0, 300.0), (0.0, 500.0)], Pts(model.Wires[1]));
    }

    [Fact]
    public void TheCutAndThePlacement_AreOneUndoEntry()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 0), (500, 0));

        PlaceProbe(vm, 200, 0);
        vm.UndoRedo.Undo();

        Assert.Empty(model.Components);
        Assert.Single(model.Wires);
        Assert.Equal([(0.0, 0.0), (500.0, 0.0)], Pts(model.Wires[0]));

        vm.UndoRedo.Redo();
        Assert.Single(model.Components);
        Assert.Equal(2, model.Wires.Count);
    }

    [Fact]
    public void APinLandingOnTheWiresOwnEnd_LeavesNoStubBehind()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (200, 0), (500, 0));

        PlaceProbe(vm, 200, 0);

        // Nothing survives on the left: whatever held that wire end now meets the probe pin there.
        Assert.Single(model.Wires);
        Assert.Equal([(300.0, 0.0), (500.0, 0.0)], Pts(model.Wires[0]));
    }

    [Fact]
    public void AWireExactlySpannedByTheProbe_IsRemovedEntirely()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (200, 0), (300, 0));

        PlaceProbe(vm, 200, 0);

        Assert.Empty(model.Wires);
        Assert.Single(model.Components);
    }

    [Fact]
    public void ACornerOutsideTheSpan_Survives()
    {
        var (model, vm) = MakeVm();
        // L-shaped run: down, then right. The probe goes on the horizontal leg.
        AddWire(model, (0, -400), (0, 0), (500, 0));

        PlaceProbe(vm, 200, 0);

        Assert.Equal(2, model.Wires.Count);
        Assert.Equal([(0.0, -400.0), (0.0, 0.0), (200.0, 0.0)], Pts(model.Wires[0]));
        Assert.Equal([(300.0, 0.0), (500.0, 0.0)],              Pts(model.Wires[1]));
    }

    /// <summary>
    /// The same thing on the SHIPPED grid rather than the free-coordinate fixtures above — the
    /// probe's pins are exactly one connection pitch apart, so this is the gesture a user actually
    /// makes. Both pins read connected afterwards: the run is broken in series, not left dangling.
    /// </summary>
    [Fact]
    public void OnTheShippedGrid_TheProbeLandsInSeriesAndConnectedBothSides()
    {
        var model = new SchematicEditModel();          // grid snap on, 100-unit connection grid
        var vm    = new SchematicViewModel(model);
        AddWire(model, (0, 0), (500, 0));

        vm.CommitPlacement(SymbolKind.IProbe, 2, SymbolRotation.R0, 200, 0 - PinDrop);

        Assert.Equal(2, model.Wires.Count);
        Assert.Equal([(0.0, 0.0), (200.0, 0.0)],   Pts(model.Wires[0]));
        Assert.Equal([(300.0, 0.0), (500.0, 0.0)], Pts(model.Wires[1]));

        var probe = model.BuildRenderModel().Model.Components[0];
        Assert.Equal(PortConnectionState.Connected, probe.Ports[0].State);
        Assert.Equal(PortConnectionState.Connected, probe.Ports[1].State);
    }

    // ── The refusals: anything in the span means the cut would change the circuit ──

    [Fact]
    public void ATJunctionInsideTheSpan_LeavesTheWireAlone()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 0), (500, 0));
        AddWire(model, (250, 0), (250, 400));       // stub tapping the middle of the span

        PlaceProbe(vm, 200, 0);

        Assert.Single(model.Components);
        Assert.Equal(2, model.Wires.Count);
        Assert.Equal([(0.0, 0.0), (500.0, 0.0)], Pts(model.Wires[0]));
    }

    [Fact]
    public void ACrossingInsideTheSpan_LeavesTheWireAlone()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 0), (500, 0));
        AddWire(model, (250, -400), (250, 400));    // passes straight through the span

        PlaceProbe(vm, 200, 0);

        Assert.Equal(2, model.Wires.Count);
        Assert.Equal([(0.0, 0.0), (500.0, 0.0)], Pts(model.Wires[0]));
    }

    [Fact]
    public void AJunctionDotInsideTheSpan_LeavesTheWireAlone()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 0), (500, 0));
        model.Dots.Add(new EditableDot { X = 250, Y = 0 });

        PlaceProbe(vm, 200, 0);

        Assert.Single(model.Wires);
        Assert.Equal([(0.0, 0.0), (500.0, 0.0)], Pts(model.Wires[0]));
    }

    [Fact]
    public void AnotherComponentsPinInsideTheSpan_LeavesTheWireAlone()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 0), (500, 0));
        // Ground's single pin sits at its own origin.
        vm.CommitPlacement(SymbolKind.Ground, 1, SymbolRotation.R0, 250, 0);

        PlaceProbe(vm, 200, 0);

        Assert.Single(model.Wires);
        Assert.Equal([(0.0, 0.0), (500.0, 0.0)], Pts(model.Wires[0]));
    }

    [Fact]
    public void ASecondWireLyingAlongTheSpan_LeavesBothAlone()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 0), (500, 0));
        AddWire(model, (150, 0), (250, 0));   // duplicate run overlapping half the span

        PlaceProbe(vm, 200, 0);

        Assert.Equal(2, model.Wires.Count);
        Assert.Equal([(0.0, 0.0), (500.0, 0.0)],   Pts(model.Wires[0]));
        Assert.Equal([(150.0, 0.0), (250.0, 0.0)], Pts(model.Wires[1]));
    }

    [Fact]
    public void TwoWiresCoveringTheWholeSpan_AreAmbiguousAndLeftAlone()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 0), (500, 0));
        AddWire(model, (100, 0), (400, 0));

        PlaceProbe(vm, 200, 0);

        Assert.Equal(2, model.Wires.Count);
        Assert.Equal([(0.0, 0.0), (500.0, 0.0)],   Pts(model.Wires[0]));
        Assert.Equal([(100.0, 0.0), (400.0, 0.0)], Pts(model.Wires[1]));
    }

    [Fact]
    public void AWireEndingExactlyAtAProbePin_IsNotAJunctionInTheSpan()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 0), (500, 0));
        AddWire(model, (300, 0), (300, 400));   // taps the run AT the second pin, not between

        PlaceProbe(vm, 200, 0);

        // The tap stays connected to the probe's own pin, so the cut is still safe.
        Assert.Equal(3, model.Wires.Count);
        Assert.Equal([(0.0, 0.0), (200.0, 0.0)],   Pts(model.Wires[0]));
        Assert.Equal([(300.0, 0.0), (500.0, 0.0)], Pts(model.Wires[1]));
        Assert.Equal([(300.0, 0.0), (300.0, 400.0)], Pts(model.Wires[2]));
    }

    [Fact]
    public void PinsOnTwoDifferentWires_LeaveBothAlone()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 0),   (200, 0));
        AddWire(model, (300, 0), (500, 0));

        PlaceProbe(vm, 200, 0);

        Assert.Equal(2, model.Wires.Count);
        Assert.Equal([(0.0, 0.0), (200.0, 0.0)],   Pts(model.Wires[0]));
        Assert.Equal([(300.0, 0.0), (500.0, 0.0)], Pts(model.Wires[1]));
    }

    [Fact]
    public void PlacedOnEmptyCanvas_ChangesNothingButAddsTheProbe()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 900), (500, 900));

        PlaceProbe(vm, 200, 0);

        Assert.Single(model.Components);
        Assert.Single(model.Wires);
        Assert.Equal([(0.0, 900.0), (500.0, 900.0)], Pts(model.Wires[0]));
    }

    // ── Scope: placement only, IProbe only ────────────────────────────────────

    [Fact]
    public void AnotherComponentPlacedAcrossAWire_NeverCutsIt()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 0), (500, 0));

        // A resistor is two-terminal and shorts out just the same — and is still not the affordance.
        vm.CommitPlacement(SymbolKind.Resistor, 2, SymbolRotation.R90, 250, 0);

        Assert.Single(model.Wires);
        Assert.Equal([(0.0, 0.0), (500.0, 0.0)], Pts(model.Wires[0]));
    }

    [Fact]
    public void DraggingAPlacedProbeOntoAWire_NeverCutsIt()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 900), (500, 900));      // far from where the probe is placed
        PlaceProbe(vm, 200, 0);
        var probe = Assert.Single(model.Components);

        // Move it so its pins land on (200,900) and (300,900) — the same geometry that WOULD have
        // been cut at placement.
        vm.Execute(new MoveCommand(model,
            [new ComponentMoveSnapshot(probe, probe.X, probe.Y, 200, 900 - PinDrop)],
            [], []));

        Assert.Equal((200.0, 900.0), probe.GetPortWorldCoord(0));
        Assert.Single(model.Wires);
        Assert.Equal([(0.0, 900.0), (500.0, 900.0)], Pts(model.Wires[0]));
    }
}
