using CircuitRF.Ui.Schematic;
using CircuitRF.Ui.ViewModels;

namespace CircuitRF.Ui.Tests.Schematic;

/// <summary>
/// WSP-4 gate (d) — the WSProbe places into a wire exactly as an <c>IProbe</c> does, and its pin
/// ORDER is the G/L contract whatever the drawing does.
///
/// <para><b>Why the second half matters more than it looks.</b> The glyph carries the letters
/// <c>G</c> and <c>L</c>, and a rotated or mirrored instance moves them on screen. Extraction must
/// follow the PINS — <c>WSProbe:&lt;label&gt; nG nL</c>, G first — because every G/L-labelled output
/// of the run (ZG, ZL, YG, YL, and both margins) swaps when they swap. That is a plausible wrong
/// answer, not an error: the numbers stay the right size and describe the other side of the node.
/// </para>
///
/// <para>Grid snap is off for the same reason <see cref="IProbePlacementClearsWireTests"/> turns it
/// off: the pins are exactly one 100-unit pitch apart, so a junction at the MIDDLE of the span has
/// no coordinate on the shipped connection grid.</para>
/// </summary>
public class WSProbePlacementTests
{
    // R0 WSProbe pins sit at (X, Y) and (X+100, Y) — a straight horizontal through, so the origin
    // IS the G pin (owner, 2026-09-08: the IProbe's dropped pins bent the wire at both ends).
    private const double PinDrop = 0.0;

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

    private static void Place(SchematicViewModel vm, double pinX, double pinY,
                              SymbolRotation rot = SymbolRotation.R0)
        => vm.CommitPlacement(SymbolKind.WSProbe, 2, rot, pinX, pinY - PinDrop);

    private static (double X, double Y)[] Pts(EditableWire w) => w.Points.ToArray();

    // ══ the cut ══════════════════════════════════════════════════════════════

    [Fact]
    public void PlacedOnAStraightWire_CutsIt()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 0), (500, 0));

        Place(vm, 200, 0);

        Assert.Single(model.Components);
        Assert.Equal(2, model.Wires.Count);
        Assert.Equal([(0.0, 0.0), (200.0, 0.0)],   Pts(model.Wires[0]));
        Assert.Equal([(300.0, 0.0), (500.0, 0.0)], Pts(model.Wires[1]));
    }

    [Fact]
    public void PlacedOverAJunction_LeavesTheWireAlone()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 0), (500, 0));
        model.Dots.Add(new EditableDot { X = 250, Y = 0 });

        Place(vm, 200, 0);

        Assert.Single(model.Components);
        Assert.Single(model.Wires);
        Assert.Equal([(0.0, 0.0), (500.0, 0.0)], Pts(model.Wires[0]));
    }

    [Fact]
    public void TheCutAndThePlacement_AreOneUndoEntry()
    {
        var (model, vm) = MakeVm();
        AddWire(model, (0, 0), (500, 0));

        Place(vm, 200, 0);
        vm.UndoRedo.Undo();

        Assert.Empty(model.Components);
        Assert.Single(model.Wires);
        Assert.Equal([(0.0, 0.0), (500.0, 0.0)], Pts(model.Wires[0]));
    }

    // ══ the pin order, under every rotation and both flips ═══════════════════

    public static TheoryData<SymbolRotation, bool, bool> Orientations()
    {
        var d = new TheoryData<SymbolRotation, bool, bool>();
        foreach (var r in new[] { SymbolRotation.R0, SymbolRotation.R90,
                                  SymbolRotation.R180, SymbolRotation.R270 })
        foreach (bool mirror in new[] { false, true })
        foreach (bool groundTheGSide in new[] { true, false })
            d.Add(r, mirror, groundTheGSide);
        return d;
    }

    /// <summary>
    /// A ground is placed on ONE of the probe's two pins — at whatever world point that pin has
    /// landed on after the rotation and the mirror — and the extracted line must carry <c>"0"</c> in
    /// the matching POSITION: first for G, second for L.
    ///
    /// <para>That is the whole contract, and it is asserted this way rather than by reading the
    /// drawing because the drawing is what moves. A rotation that carried the pin order with it
    /// would still produce a netlist that elaborates, solves and reports ZG where ZL belongs.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Orientations))]
    public void ExtractionEmitsGThenL_WhateverTheDrawingDoes(
        SymbolRotation rot, bool mirrored, bool groundTheGSide)
    {
        var model = new SchematicEditModel { GridSnap = false };

        var probe = new EditableComponent
        {
            Symbol       = SymbolKind.WSProbe,
            InstanceName = "WSP1",
            X = 0, Y = 0,
            Rotation = rot,
            MirrorX  = mirrored,
        };
        model.Components.Add(probe);

        var defs = SymbolPortDefs.For(SymbolKind.WSProbe);
        Assert.Equal(["G", "L"], defs.Select(d => d.Name).ToArray());

        var pins = model.PortDefsOf(probe);
        Assert.Equal(2, pins.Count);
        var at = model.PortWorldOf(probe, pins[groundTheGSide ? 0 : 1]);

        // Ground's single pin is at its own origin, so placing it here lands it exactly on the
        // probe pin and the two share a net — which the extractor names "0".
        model.Components.Add(new EditableComponent
        {
            Symbol       = SymbolKind.Ground,
            InstanceName = "GND1",
            X = at.X, Y = at.Y,
        });

        var tb   = NetExtractor.Extract(model).TestBench;
        var inst = Assert.Single(tb.Instances, i => i.Reference == "WSProbe");

        Assert.Equal("WSP1", inst.InstanceName);
        Assert.Equal(2, inst.NetBindings.Count);
        Assert.Equal("0", inst.NetBindings[groundTheGSide ? 0 : 1]);
        Assert.NotEqual("0", inst.NetBindings[groundTheGSide ? 1 : 0]);
    }

    /// <summary>The two things the recipe's own registry entry states, asserted rather than
    /// assumed — the engine reference is what <c>ComponentModelFactory</c> answers to, and the
    /// instance prefix is what the document calls the probe's "Label".</summary>
    [Fact]
    public void RegistryEntry_IsTheOneTheEngineAndTheDocumentExpect()
    {
        Assert.Equal("WSProbe", ComponentTypeRegistry.EngineReference(SymbolKind.WSProbe));
        Assert.Equal("WSProbe", ComponentTypeRegistry.DisplayName(SymbolKind.WSProbe));
        Assert.Equal("WSP",     ComponentTypeRegistry.InstancePrefix(SymbolKind.WSProbe));
        Assert.True(ComponentTypeRegistry.TryParseCode("WSP", out var kind, out _));
        Assert.Equal(SymbolKind.WSProbe, kind);
    }

    /// <summary>The glyph exists and draws the two terminal letters — they ARE the orientation, so a
    /// symbol without them is ambiguous in exactly the way that produces a silently wrong
    /// answer.</summary>
    [Fact]
    public void Glyph_DrawsTheGAndLLetters()
    {
        var sym = BuiltInSymbols.Primitives(SymbolKind.WSProbe);
        var text = sym.Primitives.OfType<TextPrimitive>().Select(t => t.Content).ToList();
        Assert.Contains("G", text);
        Assert.Contains("L", text);
        Assert.Equal(2, sym.Pins.Count);
    }
}
