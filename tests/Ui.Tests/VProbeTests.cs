using System;
using System.IO;
using System.Linq;
using CircuitRF.Core.Design;
using CircuitRF.Core.Elaboration;
using CircuitRF.Core.Netlist;
using CircuitRF.Design.Schematic;
using CircuitRF.Design.Symbol;
using CircuitRF.Ui.Schematic;
using Xunit;

namespace CircuitRF.Ui.Tests;

/// <summary>
/// The voltage probe: a one-terminal net LABEL that reports and stamps nothing.
///
/// <para>Three rules are what these tests are for, and each has a way of failing quietly. A probe on
/// an unnamed net BECOMES that net's name (rather than adding a second row for one point in the
/// circuit); a probe on a labelled net leaves the label standing and aliases it (rather than
/// silently renaming a net the user named); and a name that would mean two things REFUSES the run
/// (rather than merging two nets into one, which is a probe changing the circuit).</para>
/// </summary>
public sealed class VProbeTests
{
    // ── fixtures ──────────────────────────────────────────────────────────────

    private static EditableWire Wire(params (double X, double Y)[] pts)
    {
        var w = new EditableWire();
        w.Points.AddRange(pts);
        return w;
    }

    /// <summary>A VProbe whose single pin lands at (pinX, pinY) — the pin sits at (-100, +100)
    /// from the body origin, so the body goes up and to the right of the point it reads.</summary>
    private static EditableComponent Probe(string name, double pinX, double pinY)
        => new() { InstanceName = name, Symbol = SymbolKind.VProbe, X = pinX + 100, Y = pinY - 100 };

    private static EditableComponent Resistor(string name, double x, double y)
        => new() { InstanceName = name, Symbol = SymbolKind.Resistor, X = x, Y = y };

    /// <summary>Two resistors in series on one vertical run: R1 (0,-200)..(0,200) and R2 below it,
    /// with the midpoint net at (0, 400) — the wire a probe is dropped on.</summary>
    private static SchematicEditModel Divider()
    {
        var m = new SchematicEditModel();
        m.Components.Add(Resistor("R1", 0, 200));    // pins (0,0) and (0,400)
        m.Components.Add(Resistor("R2", 0, 800));    // pins (0,600) and (0,1000)
        m.Wires.Add(Wire((0, 400), (0, 600)));       // the midpoint run
        return m;
    }

    private static Instance? ProbeInstance(TestBench tb, string name)
        => tb.Instances.FirstOrDefault(i => i.InstanceName == name);

    /// <summary>Reads a `.cnl` from text and elaborates it — the same chain a run takes, minus the
    /// engine, so a warning can be read without solving anything.</summary>
    private static ElaboratedNetlist ElaborateText(string cnl)
    {
        string path = Path.Combine(Path.GetTempPath(), $"crf_vprobe_{Guid.NewGuid():N}.cnl");
        try
        {
            File.WriteAllText(path, cnl);
            var (lib, tb) = CnlReader.ReadFile(path);
            return new Elaborator(lib).Elaborate(tb);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ── naming ────────────────────────────────────────────────────────────────

    [Fact]
    public void OnAnUnnamedNet_TheProbesNameBecomesTheNetsOwnName()
    {
        var m = Divider();
        m.Components.Add(Probe("Vmid", 0, 400));

        var r = NetExtractor.Extract(m);

        // The net is called Vmid — and there is no auto-name for it beside that.
        Assert.Equal("Vmid", ProbeInstance(r.TestBench, "Vmid")!.NetBindings[0]);
        Assert.Contains("Vmid", r.TestBench.LabeledNets);
        Assert.Contains(r.TestBench.Instances,
            i => i.InstanceName == "R1" && i.NetBindings.Contains("Vmid"));

        var nl = new Elaborator(r.Library).Elaborate(r.TestBench);
        Assert.True(nl.Nodes.TryGetIndex("Vmid", out _));
        Assert.Empty(nl.Nodes.Aliases);                        // nothing left to alias
        Assert.Contains("Vmid", nl.Nodes.LabeledNames);
    }

    [Fact]
    public void OnALabelledNet_TheLabelStands_AndTheProbeIsAnAliasForTheSameNode()
    {
        var m = Divider();
        m.NetLabels.Add(new EditableNetLabel { X = 0, Y = 500, Name = "n_drain" });
        m.Components.Add(Probe("Vmid", 0, 400));

        var r = NetExtractor.Extract(m);

        // The user's own label is untouched, and the probe is emitted against it.
        Assert.Equal("n_drain", ProbeInstance(r.TestBench, "Vmid")!.NetBindings[0]);
        Assert.Contains("n_drain", r.TestBench.LabeledNets);

        var nl = new Elaborator(r.Library).Elaborate(r.TestBench);
        int node = nl.Nodes.IndexOf("n_drain");

        Assert.Equal([("Vmid", node)], nl.Nodes.Aliases);
        Assert.Equal(node, nl.Nodes.IndexOf("Vmid"));          // both names, one node
        Assert.Contains("Vmid",    nl.Nodes.LabeledNames);
        Assert.Contains("n_drain", nl.Nodes.LabeledNames);
    }

    [Fact]
    public void DroppedMidSpan_ItStillAttachesToTheRun_TheWayALabelDoes()
    {
        // The pin is nowhere near a wire VERTEX — it is halfway along the segment, which is the
        // gesture a probe is for and the one a device pin deliberately does not honour.
        var m = Divider();
        m.Components.Add(Probe("Vmid", 0, 500));

        var r = NetExtractor.Extract(m);

        Assert.Empty(r.Conflicts);
        Assert.Contains(r.TestBench.Instances,
            i => i.InstanceName == "R1" && i.NetBindings.Contains("Vmid"));
    }

    // ── the two refusals ──────────────────────────────────────────────────────

    [Fact]
    public void TwoProbesSharingAName_RefuseTheRun()
    {
        var m = Divider();
        m.Wires.Add(Wire((0, 0), (0, -200)));      // a second, separate run
        m.Components.Add(Probe("Vmid", 0, 400));
        m.Components.Add(Probe("Vmid", 0, 0));

        var r  = NetExtractor.Extract(m);
        var ex = Assert.Throws<InvalidOperationException>(
            () => new Elaborator(r.Library).Elaborate(r.TestBench));

        Assert.Contains("Vmid", ex.Message, StringComparison.Ordinal);
        Assert.Contains("two",  ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AProbeNamedAfterSomeOtherNet_RefusesTheRun_RatherThanMergingTheTwo()
    {
        var m = Divider();
        m.Wires.Add(Wire((0, 0), (0, -200)));
        m.NetLabels.Add(new EditableNetLabel { X = 0, Y = -100, Name = "n_top" });
        m.Components.Add(Probe("n_top", 0, 400));   // …but sitting on the MIDPOINT run

        var r = NetExtractor.Extract(m);

        // Extraction refuses to hand the name out twice: two roots under one name would become ONE
        // node at elaboration, which is a probe changing the circuit.
        Assert.Contains(r.Conflicts, c => c.Contains("n_top", StringComparison.Ordinal));
        Assert.NotEqual("n_top", ProbeInstance(r.TestBench, "n_top")!.NetBindings[0]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => new Elaborator(r.Library).Elaborate(r.TestBench));
        Assert.Contains("n_top", ex.Message, StringComparison.Ordinal);
    }

    // ── unconnected ───────────────────────────────────────────────────────────

    [Fact]
    public void TouchingNothing_ItIsReportedByName_AndEmitsNoInstance()
    {
        var m = Divider();
        m.Components.Add(Probe("Vfloat", 2000, 2000));

        var r = NetExtractor.Extract(m);

        Assert.Contains(r.Conflicts,
            c => c.Contains("VProbe Vfloat is not connected and will not report any results",
                            StringComparison.Ordinal));
        Assert.Null(ProbeInstance(r.TestBench, "Vfloat"));
        Assert.DoesNotContain("Vfloat", r.TestBench.LabeledNets);
    }

    [Fact]
    public void TwoProbesTouchingOnlyEachOther_AreBothReportedAsUnconnected()
    {
        var m = Divider();
        m.Components.Add(Probe("Va", 2000, 2000));
        m.Components.Add(Probe("Vb", 2000, 2000));

        var r = NetExtractor.Extract(m);

        Assert.Contains(r.Conflicts, c => c.Contains("VProbe Va is not connected", StringComparison.Ordinal));
        Assert.Contains(r.Conflicts, c => c.Contains("VProbe Vb is not connected", StringComparison.Ordinal));
    }

    // ── it cannot change the circuit ──────────────────────────────────────────

    [Fact]
    public void ThePresenceOfAProbe_ChangesNothingTheEngineSees()
    {
        var bare    = Divider();
        var probed  = Divider();
        probed.NetLabels.Add(new EditableNetLabel { X = 0, Y = 500, Name = "n_drain" });
        bare  .NetLabels.Add(new EditableNetLabel { X = 0, Y = 500, Name = "n_drain" });
        probed.Components.Add(Probe("Vmid", 0, 400));

        var a = NetExtractor.Extract(bare);
        var b = NetExtractor.Extract(probed);

        var nlA = new Elaborator(a.Library).Elaborate(a.TestBench);
        var nlB = new Elaborator(b.Library).Elaborate(b.TestBench);

        // Same node count — an alias carries no matrix row of its own — and the same components,
        // in the same order, on the same nodes.
        Assert.Equal(nlA.Nodes.Count, nlB.Nodes.Count);
        Assert.Equal(nlA.Components.Count, nlB.Components.Count);
        Assert.Equal(nlA.Components.Select(c => (c.InstancePath, string.Join(",", c.Nodes))),
                     nlB.Components.Select(c => (c.InstancePath, string.Join(",", c.Nodes))));
    }

    // ── the results, and measurements ─────────────────────────────────────────

    /// <summary>
    /// The end-to-end that matters: a `.cnl` carrying a VProbe line runs, the probed net appears in
    /// the V cube under the probe's own name, the picker's own filter cube lists it, and a
    /// measurement expression reads it by that name.
    /// </summary>
    [Fact]
    public void ADcRun_ReportsTheProbedNode_UnderTheProbesName_AndAMeasurementReadsIt()
    {
        // A 3:1 divider off a 12 V rail: the midpoint sits at 4 V.
        const string cnl = """
            Vdc:V1     rail 0     Vdc=12
            R:R1       rail mid   R=200 Ohm
            R:R2       mid  0     R=100 Ohm
            VProbe:Vmid mid
            labelednets mid
            measure Vprobed = DC1.V("Vmid")
            analysis DC1 type=dc
            """;

        string path = Path.Combine(Path.GetTempPath(), $"crf_vprobe_{Guid.NewGuid():N}.cnl");
        try
        {
            File.WriteAllText(path, cnl);
            var run = SchematicRunService.RunNetlist(path);
            Assert.Equal(RunStatus.Success, run.Status);

            var ds = run.GroupedResults!;
            var v  = ds["DC1.V"];
            var names = v.Axes[0].Labels!;

            // "mid" was a user label, so BOTH names are there, on the same voltage.
            Assert.Contains("mid",  names);
            Assert.Contains("Vmid", names);
            Assert.Equal(v.RealValues[Array.IndexOf(names, "mid")],
                         v.RealValues[Array.IndexOf(names, "Vmid")], 12);
            Assert.Equal(4.0, v.RealValues[Array.IndexOf(names, "Vmid")], 6);

            // The node picker's own filter lists it, exactly as it lists a label.
            Assert.Contains("Vmid", ds["DC1.__LabeledNodes"].Axes[0].Labels!);

            // And a measurement expression resolves the probe by name.
            Assert.Equal(4.0, ds["measurements.Vprobed"].RealValues[0], 6);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void AProbeNamingANetNothingElseReaches_ReportsItAndRunsAnyway()
    {
        const string cnl = """
            Vdc:V1     rail 0     Vdc=12
            R:R1       rail mid   R=200 Ohm
            R:R2       mid  0     R=100 Ohm
            VProbe:Vghost nowhere
            analysis DC1 type=dc
            """;

        var nl = ElaborateText(cnl);

        Assert.False(nl.Nodes.TryGetIndex("Vghost", out _));
        Assert.Contains(nl.Warnings,
            w => w.Contains("Vghost", StringComparison.Ordinal)
              && w.Contains("will not report any results", StringComparison.Ordinal));
    }

    [Fact]
    public void AProbeOnGround_ReportsIt_RatherThanPublishingAConstantZero()
    {
        const string cnl = """
            Vdc:V1     rail 0     Vdc=12
            R:R1       rail 0     R=200 Ohm
            VProbe:Vgnd 0
            analysis DC1 type=dc
            """;

        var nl = ElaborateText(cnl);

        Assert.False(nl.Nodes.TryGetIndex("Vgnd", out _));
        Assert.Contains(nl.Warnings, w => w.Contains("Vgnd", StringComparison.Ordinal)
                                       && w.Contains("ground", StringComparison.Ordinal));
    }

    // ── the symbol ────────────────────────────────────────────────────────────

    [Fact]
    public void ItHasExactlyOnePin_AtTheTipOfItsArrow()
    {
        var defs = SymbolPortDefs.For(SymbolKind.VProbe);
        Assert.Single(defs);
        Assert.Equal((-100f, 100f), (defs[0].LocalX, defs[0].LocalY));

        var sym = BuiltInSymbols.Primitives(SymbolKind.VProbe);
        Assert.Single(sym.Pins);

        // Two rings of different weight, a filled arrowhead, and the letter it reports.
        var circles = sym.Primitives.OfType<CirclePrimitive>().OrderByDescending(c => c.R).ToArray();
        Assert.Equal(2, circles.Length);
        Assert.True(circles[0].R > circles[1].R, "the outer ring must be the larger one");
        Assert.Equal(SymbolStrokeTier.Thick, circles[0].StrokeTier);
        Assert.Equal(SymbolStrokeTier.Thin,  circles[1].StrokeTier);
        Assert.Contains(sym.Primitives.OfType<TextPrimitive>(), t => t.Content == "V");
        Assert.Contains(sym.Primitives.OfType<PolygonPrimitive>(), p => p.Filled);

        // Smaller than the IProbe, which is what the glyph is meant to read as.
        Assert.True(circles[0].R * 2 < 80.0);

        // The dial sits HALF A GRID left of the origin, so the "V" is in the middle of a grid square
        // rather than on its corner — that is what stops it overlapping the next node's component
        // (owner, 2026-09-06). Concentric, and the letter is at the same centre.
        foreach (var c in circles) Assert.Equal((-50.0, 0.0), (c.Cx, c.Cy));
        var v = sym.Primitives.OfType<TextPrimitive>().Single(t => t.Content == "V");
        Assert.Equal((-50.0, 0.0), (v.AnchorX, v.AnchorY));
    }

    /// <summary>
    /// The arrow reads as ONE arrow: its tip is exactly the pin, its head is aimed along the line
    /// from the dial's centre, and its shaft starts on the outer ring rather than floating off it.
    ///
    /// <para>Measured rather than eyeballed, because the dial and the tip moved independently: the
    /// dial went half a grid left and the tip stayed where a user aims it, so the whole arrow had to
    /// be re-aimed. Keeping the old head angle while the shaft came in at the new one draws a bent
    /// arrow, and that is exactly the kind of thing nobody notices in a diff.</para>
    /// </summary>
    [Fact]
    public void ItsArrowIsStraight_TippedAtThePin_AndRootedOnTheOuterRing()
    {
        var sym  = BuiltInSymbols.Primitives(SymbolKind.VProbe);
        var ring = sym.Primitives.OfType<CirclePrimitive>().MaxBy(c => c.R)!;
        var head = sym.Primitives.OfType<PolygonPrimitive>().Single(p => p.Filled);
        var line = sym.Primitives.OfType<LinePrimitive>().Single();

        var pin = SymbolPortDefs.For(SymbolKind.VProbe)[0];
        var tip = head.Points[0];
        Assert.Equal((double)pin.LocalX, tip[0], 6);
        Assert.Equal((double)pin.LocalY, tip[1], 6);

        static double Dist(double ax, double ay, double bx, double by)
            => System.Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

        // The shaft's far end starts ON the ring — within half a unit of it, and never outside.
        double atRing = Dist(line.X1, line.Y1, ring.Cx, ring.Cy);
        Assert.InRange(atRing, ring.R - 1.0, ring.R);

        // …and both shaft ends are on the centre-to-tip line, which is what makes it straight.
        double full = Dist(ring.Cx, ring.Cy, tip[0], tip[1]);
        foreach (var (x, y) in new[] { (line.X1, line.Y1), (line.X2, line.Y2) })
            Assert.Equal(full,
                Dist(ring.Cx, ring.Cy, x, y) + Dist(x, y, tip[0], tip[1]), 0);
    }

    /// <summary>
    /// Both probes name the quantity they report, at the SAME size, and each letter sits inside its
    /// own display with air around it.
    ///
    /// <para>The two are read side by side on one schematic, so a letter a little bigger on one of
    /// them reads as a mistake rather than as a distinction (owner, 2026-09-06). The binding
    /// constraint is the ammeter window, not the dial: the window's clear height is the smaller of
    /// the two frames, so that is what this measures.</para>
    /// </summary>
    [Fact]
    public void BothProbesNameTheirQuantity_AtTheSameSize_WithAirAroundIt()
    {
        var v = BuiltInSymbols.Primitives(SymbolKind.VProbe)
                              .Primitives.OfType<TextPrimitive>().Single(t => t.Content == "V");
        var i = BuiltInSymbols.Primitives(SymbolKind.IProbe)
                              .Primitives.OfType<TextPrimitive>().Single(t => t.Content == "I");

        Assert.Equal(v.FontSize, i.FontSize);

        // The IProbe's ammeter window: the two bowed edges reach y = -19 (bottom) and y = -55 (top)
        // at their centres, and the "I" is anchored between them. A cap height of roughly 0.7em
        // must leave clear air on both sides — this is the measurement that says "shrink the pair"
        // if the window is ever redrawn smaller.
        const double windowTop = -55.0, windowBottom = -19.0;
        double cap = i.FontSize * 0.7;
        Assert.InRange(i.AnchorY, windowTop + cap / 2, windowBottom - cap / 2);
        Assert.True(windowBottom - windowTop - cap > 8.0,
            $"the 'I' fills the ammeter window with only {windowBottom - windowTop - cap:F1} units " +
            "of air; shrink BOTH probe letters together.");

        // Horizontally centred in the window (its two sides are symmetric about x = 50).
        Assert.Equal(50.0, i.AnchorX, 6);
    }

    // ── the catalogue ─────────────────────────────────────────────────────────

    [Fact]
    public void ItIsPlaceable_ReachesTheNetlist_AndIsNeverAnEngineComponent()
    {
        Assert.Equal("VProbe", ComponentTypeRegistry.EngineReference(SymbolKind.VProbe));
        Assert.Equal("VP",     ComponentTypeRegistry.InstancePrefix(SymbolKind.VProbe));
        Assert.False(CircuitRF.Core.Devices.ComponentModelFactory.IsPrimitive("VProbe"));

        Assert.True(ComponentTypeRegistry.TryParseCode("VP",     out var k1, out _) && k1 == SymbolKind.VProbe);
        Assert.True(ComponentTypeRegistry.TryParseCode("VProbe", out var k2, out _) && k2 == SymbolKind.VProbe);

        var entry = ComponentCatalog.All().Single(e => e.Type == "VProbe");
        Assert.False(entry.Simulatable);
        Assert.True(entry.Placeable);
        Assert.Contains("alias", entry.Note, StringComparison.OrdinalIgnoreCase);
    }
}
