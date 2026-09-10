// ================================================================
//  PortSeededLevelsTests.cs — WHICH CONDUCTOR LEVELS A RUN SOLVES, and why the ports decide it.
//
//  User report, 2026-09-10, on a Gerber-imported 4-layer board: a patch antenna drawn on Top Copper
//  was told "Every port returns through 'Bottom Copper' … the highest one below the signal level at
//  335 µm", when the plane directly under the patch is Inner 1. Every word of that note was true and
//  the answer was wrong, because the SIGNAL LEVEL was not the one the user drew on.
//
//  The import had brought in the inner-layer annular pads of the connector's two through-holes: two
//  0.485 mm rings on Inner 2, which is a signal conductor carrying artwork and therefore — under the
//  old rule, "every signal conductor that carries artwork" — an analysis level. Being the LOWEST it
//  became `signal`, so R-em-4's "highest designated ground BELOW the signal" could no longer see
//  Inner 1 at all and fell through to Bottom Copper. The run would have solved a 1.67 mm slab in
//  place of a 0.3 mm one, with the real ground plane absorbed into the dielectric as substrate.
//
//  Ports are the one unambiguous statement of what is being fed, so they now decide ONE question:
//  whether the lowest level — the only one that sets the return plane — is part of the structure.
//  They deliberately do NOT decide the level set. Seeding the levels from the ports is the obvious
//  design and it is wrong: a MIM capacitor's bottom plate is coupled to its top plate by the
//  capacitor dielectric and by nothing else, so no port and no via reaches it, and the acceptance
//  fixtures in MimCapacitorTests lose half of every capacitor. A parasitic stacked patch and a
//  broadside pair are the same shape. These tests pin both halves — the trim, and its limits.
// ================================================================

using CircuitRF.Ui.Layout;
using CircuitRF.Ui.Layout.Em;

namespace CircuitRF.Ui.Tests.Em;

public class PortSeededLevelsTests
{
    private const int Dbu = LayoutUnits.DefaultDbuPerMicron;

    private static Technology Tech() => ShippedTechnologies.Load("pcb-4layer_FR-4_62mil_1oz");

    private static List<StackupLayer> Conductors(Technology t) =>
        [.. t.Stackup.Layers.Where(l => l.Kind == StackupKind.Conductor)];

    private static long Um(double v) => (long)Math.Round(v * Dbu);

    private static RectShape Patch(LayerKey layer) =>
        new() { Layer = layer, X1 = 0, Y1 = 0, X2 = Um(30000), Y2 = Um(30000) };

    /// <summary>The two annular pads the import left on the inner layer — incidental metal, on a
    /// signal conductor, nowhere near anything anyone is analysing.</summary>
    private static CircleShape Pad(LayerKey layer, double cx, double cy) =>
        new() { Layer = layer, Cx = Um(cx), Cy = Um(cy), R = Um(485) };

    private static LabelShape Port(LayerKey layer, double x, double y) =>
        new() { Layer = layer, X = Um(x), Y = Um(y), Text = "P1", Height = Um(600), IsPort = true };

    private static string? ReturnNote(PlanarExtractionResult r) =>
        r.Notes.FirstOrDefault(n => n.StartsWith("Every port returns through", StringComparison.Ordinal));

    // ── The report ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>The reported case and its own before/after in one test.</b> Identical artwork; the only
    /// difference is whether a port label is present. Without one the stray inner-layer pads decide
    /// the signal level and the return plane drops to the bottom of the board; with one the run is
    /// the microstrip patch over Inner 1 that was drawn.
    /// </summary>
    [Fact]
    public void StrayInnerLayerPads_NoLongerDecideTheSignalLevel_NorTheReturnPlane()
    {
        var tech   = Tech();
        var conds  = Conductors(tech);
        var top    = conds[0].DrawingLayers[0];      // Top Copper
        var inner2 = conds[2].DrawingLayers[0];      // Inner 2 — signal, carries the import's pads

        LayoutShape[] artwork = [Patch(top), Pad(inner2, 6000, 6000), Pad(inner2, 6000, 9000)];

        // Before: no port, so the artwork rule stands and the lowest level wins.
        var without = PlanarExtractor.Extract(artwork, tech, Dbu);
        Assert.True(without.Ok, without.Refusal);
        Assert.Equal(2, without.Problem!.Layers.Count);
        Assert.Contains($"'{conds[3].Name}'", ReturnNote(without)!, StringComparison.Ordinal);

        // After: one port on Top Copper is enough to say what is being fed.
        var with = PlanarExtractor.Extract([.. artwork, Port(top, 0, 15000)], tech, Dbu);
        Assert.True(with.Ok, with.Refusal);
        Assert.Single(with.Problem!.Layers);
        Assert.Contains($"'{conds[1].Name}'", ReturnNote(with)!, StringComparison.Ordinal);
    }

    /// <summary>The slab is the quantity that was silently wrong, so it is asserted as a NUMBER
    /// rather than inferred from the plane's name: 8 mil of prepreg, not the whole 62 mil board.
    /// A name assertion alone would still pass if the medium were built from the wrong band.</summary>
    [Fact]
    public void TheSlabIsTheHeightAboveTheNearestPlane_NotTheWholeBoard()
    {
        var tech  = Tech();
        var conds = Conductors(tech);
        var top   = conds[0].DrawingLayers[0];

        var r = PlanarExtractor.Extract(
            [Patch(top), Pad(conds[2].DrawingLayers[0], 6000, 6000), Port(top, 0, 15000)], tech, Dbu);

        Assert.True(r.Ok, r.Refusal);

        // Prepreg (top) — the one dielectric between Top Copper and Inner 1.
        double prepregM = tech.Stackup.Layers
            .First(l => l.Kind == StackupKind.Dielectric).ThicknessDbu / (LayoutUnits.DefaultDbuPerMicron * 1e6);
        Assert.Equal(prepregM, r.Problem!.Slab.HeightM, 12);
    }

    /// <summary>The level that was left out is NAMED, together with the plane its absence bought and
    /// the way to put it back. A level vanishing from a full-wave solve with no sentence attached is
    /// the whole failure this change is about — reporting it silently would only move the surprise.</summary>
    [Fact]
    public void TheLevelLeftOut_IsNamedWithThePlaneItsAbsenceBought()
    {
        var tech  = Tech();
        var conds = Conductors(tech);
        var top   = conds[0].DrawingLayers[0];

        var r = PlanarExtractor.Extract(
            [Patch(top), Pad(conds[2].DrawingLayers[0], 6000, 6000), Port(top, 0, 15000)], tech, Dbu);

        var note = r.Notes.FirstOrDefault(n => n.Contains("no port reaches it", StringComparison.Ordinal));
        Assert.NotNull(note);
        Assert.Contains($"'{conds[2].Name}'", note, StringComparison.Ordinal);   // Inner 2, left out
        Assert.Contains($"'{conds[1].Name}'", note, StringComparison.Ordinal);   // Inner 1, the plane kept
        Assert.Contains("analysis levels", note, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The limit of the rule, and the fixture that found it.</b> A level no port reaches is left
    /// alone unless dropping it actually RESCUES the return plane. A MIM capacitor's bottom plate is
    /// unreachable by construction — the capacitor dielectric is the only thing joining it to the
    /// plate above — so unreachability alone must never condemn a level. Here Inner 2 is unreachable
    /// and lowest, but the only designated ground below EITHER candidate is Bottom Copper, so
    /// nothing is gained by dropping it and it stays.
    /// </summary>
    [Fact]
    public void AnUnreachableLevel_ThatDoesNotMoveTheReturnPlane_IsKept()
    {
        var tech  = Tech();
        var conds = Conductors(tech);
        conds[1].IsGroundReference = false;          // only Bottom Copper is designated now

        var r = PlanarExtractor.Extract(
            [Patch(conds[0].DrawingLayers[0]), Pad(conds[2].DrawingLayers[0], 6000, 6000),
             Port(conds[0].DrawingLayers[0], 0, 15000)],
            tech, Dbu);

        Assert.True(r.Ok, r.Refusal);
        Assert.Equal(2, r.Problem!.Layers.Count);
        Assert.DoesNotContain(r.Notes, n => n.Contains("no port reaches it", StringComparison.Ordinal));
    }

    // ── What the seed must NOT break ──────────────────────────────────────────────────────────

    /// <summary>A DRAWN via carries reachability to the level it lands on, which is what keeps a
    /// real lower conductor — a spiral underpass, a via-fed stacked structure — from being trimmed
    /// as incidental. Geometrically identical to the reported case; the via is the only
    /// difference.</summary>
    [Fact]
    public void ADrawnVia_DefendsTheLevelItLandsOn()
    {
        var tech  = Tech();
        var conds = Conductors(tech);
        var top   = conds[0].DrawingLayers[0];
        var inner2 = conds[2].DrawingLayers[0];

        // Re-point the through-hole entry at Inner 2; the starter's own spans reach only planes.
        var pth = tech.Stackup.Layers.First(l => l.Kind == StackupKind.Via && l.SpanToLayer == conds[3].Name);
        pth.SpanToLayer = conds[2].Name;

        var via = new ViaShape
        {
            Layer = pth.DrawingLayers[0],
            X = Um(6000), Y = Um(6000), PadSize = Um(600), DrillSize = Um(300),
        };

        var r = PlanarExtractor.Extract(
            [Patch(top), Pad(inner2, 6000, 6000), via, Port(top, 0, 15000)], tech, Dbu);

        Assert.True(r.Ok, r.Refusal);
        Assert.Equal(2, r.Problem!.Layers.Count);
        Assert.Contains($"'{conds[3].Name}'", ReturnNote(r)!, StringComparison.Ordinal);
        Assert.DoesNotContain(r.Notes, n => n.Contains("no port reaches it", StringComparison.Ordinal));
    }

    /// <summary>An explicit level list is the user's answer and outranks the ports — including when
    /// it names a level no port reaches, which is exactly what the field is for.</summary>
    [Fact]
    public void AnExplicitLevelList_OutranksThePorts()
    {
        var tech  = Tech();
        var conds = Conductors(tech);
        var top   = conds[0].DrawingLayers[0];

        var r = PlanarExtractor.Extract(
            [Patch(top), Patch(conds[2].DrawingLayers[0]), Port(top, 0, 15000)],
            tech, Dbu,
            settings: EmExtractionSettings.Default with
            {
                AnalysisLevelNames = [conds[0].Name, conds[2].Name],
            });

        Assert.True(r.Ok, r.Refusal);
        Assert.Equal(2, r.Problem!.Layers.Count);
    }

    /// <summary>A port this file cannot resolve to a conductor must not silently narrow anything —
    /// it falls back to the artwork rule and SAYS the ports were unreadable, which is a different
    /// sentence from "the old rule applied".</summary>
    [Fact]
    public void APortOnNoConductor_FallsBackAndSaysSo()
    {
        var tech  = Tech();
        var conds = Conductors(tech);
        var top   = conds[0].DrawingLayers[0];

        var r = PlanarExtractor.Extract(
            [Patch(top), Patch(conds[2].DrawingLayers[0]), Port(new LayerKey(200, 0), 0, 15000)],
            tech, Dbu);

        Assert.True(r.Ok, r.Refusal);
        Assert.Equal(2, r.Problem!.Layers.Count);
        Assert.Contains(r.Notes, n =>
            n.Contains("none of them resolves to a signal conductor layer", StringComparison.Ordinal));
    }

    // ── The plane that is skipped over ────────────────────────────────────────────────────────

    /// <summary>
    /// <b>A ground-designated plane BETWEEN the levels and the return is not merely unused — it
    /// leaves the physics.</b> BuildMediumStack absorbs any conductor that is not a level into a
    /// neighbouring dielectric, so Inner 1's copper is modelled as FR-4 and the run reports an
    /// ordinary-looking result for a structure referenced four times further away than the real
    /// plane. Reachable now only by asking for it explicitly — and still worth a sentence, because
    /// nothing else in the run mentions that plane at all.
    /// </summary>
    [Fact]
    public void AGroundPlaneBetweenTheLevelsAndTheReturn_IsNamedAndItsFateStated()
    {
        var tech  = Tech();
        var conds = Conductors(tech);

        var r = PlanarExtractor.Extract(
            [Patch(conds[0].DrawingLayers[0]), Patch(conds[2].DrawingLayers[0])],
            tech, Dbu,
            settings: EmExtractionSettings.Default with
            {
                AnalysisLevelNames = [conds[2].Name, conds[0].Name],
            });

        Assert.True(r.Ok, r.Refusal);

        var warn = r.Notes.FirstOrDefault(n => n.StartsWith("WARNING:", StringComparison.Ordinal));
        Assert.NotNull(warn);
        Assert.Contains($"'{conds[1].Name}'", warn, StringComparison.Ordinal);
        Assert.Contains("absorbed into the surrounding dielectric", warn, StringComparison.Ordinal);
    }

    /// <summary>…and it must NOT fire on the ordinary microstrip case, where the chosen plane is
    /// directly beneath the level and nothing was passed over. A warning that fires on every run is
    /// a warning nobody reads.</summary>
    [Fact]
    public void NoPlaneIsSkipped_NoWarning()
    {
        var tech  = Tech();
        var conds = Conductors(tech);
        var top   = conds[0].DrawingLayers[0];

        var r = PlanarExtractor.Extract([Patch(top), Port(top, 0, 15000)], tech, Dbu);

        Assert.True(r.Ok, r.Refusal);
        Assert.DoesNotContain(r.Notes, n => n.StartsWith("WARNING:", StringComparison.Ordinal));
    }
}
