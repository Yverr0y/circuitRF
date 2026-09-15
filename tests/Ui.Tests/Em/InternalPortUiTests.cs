// The internal port, Ui side — the THIRD port type reaches the EM Setup panel.
//
// The engine half (which bases it drives, its fixed polarity, and the two solves that say it is a
// internal port rather than a series one) is tests/Engine.Tests/Mom/InternalPortTests.cs. What lives
// here is everything the engine cannot see: the `.cem` round trip, the provenance hash, the panel
// row, the mark the layout draws, and — the load-bearing ones — that an internal port declared in a
// setup actually reaches the extractor and that the extractor refuses one with no via under it.

using System.Numerics;
using CircuitRF.Engine.Mom;
using CircuitRF.Ui.Converters;
using CircuitRF.Ui.Layout;
using CircuitRF.Ui.Layout.Em;
using CircuitRF.Ui.Renderers;
using SkiaSharp;

namespace CircuitRF.Ui.Tests.Em;

public class InternalPortUiTests
{
    private const int Dbu = LayoutUnits.DefaultDbuPerMicron;
    private static readonly LayerKey TopCopper = new(1, 0);

    private static long Mm(double mm) => (long)Math.Round(mm * 1000 * Dbu);

    private static RectShape Line() =>
        new() { Layer = TopCopper, X1 = 0, Y1 = 0, X2 = Mm(20), Y2 = Mm(2.9) };

    private static LabelShape Port(string text, double xMm, double yMm, LayoutRotation? dir = null) =>
        new()
        {
            Layer = TopCopper, X = Mm(xMm), Y = Mm(yMm), Text = text, Height = Mm(0.5),
            IsPort = true, PortDirection = dir,
        };

    /// <summary>A backside via at (xMm, 1.45), on the starter PCB technology's own drill layer —
    /// the artwork an internal port stands on.</summary>
    private static ViaShape Via(double xMm)
    {
        var tech  = StarterTechnologies.Pcb2Layer();
        var drill = tech.Layers.First(l => l.Name.Contains("Drill", StringComparison.OrdinalIgnoreCase));
        return new ViaShape
        {
            Layer = drill.Key,
            X = Mm(xMm), Y = Mm(1.45), PadSize = Mm(0.7), DrillSize = Mm(0.36),
        };
    }

    private static PlanarProblem Problem(params LayoutShape[] shapes)
    {
        var r = PlanarExtractor.Extract(shapes, StarterTechnologies.Pcb2Layer(), Dbu, 10e9);
        Assert.True(r.Ok, r.Refusal);
        return r.Problem!;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // The fixture itself — a via that does NOT reach the ground plane would make every test below
    // pass for the wrong reason
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TheStarterPcbTechnologysViaReachesTheGroundPlane()
    {
        var problem = Problem(Line(), Via(10));
        Assert.Contains(problem.ViaList, v => v.ToGround);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // The .cem round trip and the provenance hash
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AnInternalPortSurvivesTheCemAndTheClone_ByNAME()
    {
        var setup = new EmSetup
        {
            Name = "viaport", LayoutRef = "a.clay", AnalysisKind = EmAnalysisKind.Planar,
            PortKinds = [PlanarPortKind.Edge, PlanarPortKind.Edge, PlanarPortKind.Internal],
        };

        string json = EmSetupPersistence.Serialize(setup);
        Assert.Contains("Internal", json, StringComparison.Ordinal);

        var back = EmSetupPersistence.Deserialize(json);
        Assert.Equal(PlanarPortKind.Internal, back.PortKinds[2]);
        Assert.Equal(PlanarPortKind.Edge,     back.PortKinds[0]);
        Assert.Equal(json, EmSetupPersistence.Serialize(back));

        Assert.Equal(PlanarPortKind.Internal, setup.Clone().PortKinds[2]);
    }

    [Fact]
    public void AnInternalPortHashesDifferentlyFromAGapAndFromAnEdgePort()
    {
        var at = new EmPoint(0.010, 0.00145);

        PlanarPort[] edge  = [new(1, at, PlanarPortSide.MinX, 50.0)];
        PlanarPort[] gap   = [new(1, at, PlanarPortSide.MinX, 50.0, Kind: PlanarPortKind.InternalDeltaGap)];
        PlanarPort[] via = [new(1, at, PlanarPortSide.MinX, 50.0, Kind: PlanarPortKind.Internal)];

        // All three drive DIFFERENT unknowns at the same point — a rooftop row at the conductor end,
        // a rooftop row at an interior cut, and the attachment bases of the via underneath. An .snp
        // written under one is not current for either of the others.
        Assert.NotEqual(EmSnpProvenance.PortHash(edge),  EmSnpProvenance.PortHash(via));
        Assert.NotEqual(EmSnpProvenance.PortHash(gap),   EmSnpProvenance.PortHash(via));
    }

    [Fact]
    public void AnInternalPortBlocksTheUniformLineKernelToo_ByName()
    {
        // A line with a via on it is still a uniform cross-section, so the cross-section extractor
        // accepts it and Auto prefers that kernel — which has no via, no mesh and no way to say so.
        var setup = new EmSetup
        {
            Name = "x", LayoutRef = "a.clay", AnalysisKind = EmAnalysisKind.CrossSection,
            PortKinds = [PlanarPortKind.Edge, PlanarPortKind.Internal],
        };

        // The question is asked of the ARTWORK now, not of a list of stated types — so a port that
        // is an internal port because of where it is DRAWN is caught too, which is the port this
        // guard exists for. The setup's legacy list still reaches the labels through the one door.
        LayoutShape[] guarded = [Line(), Port("1", 0, 1.45), Port("2", 10, 1.45)];
        EmPortKindMigration.ApplyInMemory(guarded, setup.PortKinds);
        Assert.True(EmPortExtraction.AnyNonEdgePort(guarded));
        string why = EmRunService.InternalPortNeedsFullWave("uniform-line (quasi-static) kernel");
        Assert.Contains("internal port is the foot of a via", why);
        Assert.Contains("full-wave planar kernel", why);
    }

    /// <summary>States a port's type the way the drawing does — the type is a field on the LABEL
    /// (2026-09-14), so a test that wants a particular type says so on the port rather than handing
    /// the extractor a slot lookup of its own.</summary>
    private static void TypePort(IEnumerable<LayoutShape> shapes, string text, PlanarPortKind kind)
    {
        foreach (var l in shapes.OfType<LabelShape>())
            if (l.IsPort && l.Text == text) l.PortKind = kind;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Extraction — the type comes from the LABEL, and an internal port needs a via under it
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AInternalPortOnALine_ResolvesAndNeedsNoDirectionAtAll()
    {
        // The label carries NO PortDirection — a gap in the same spot is refused for that, and a
        // internal port must not be, because its polarity is fixed by which terminal is the ground
        // plane rather than by anything in the drawing.
        LayoutShape[] shapes = [Line(), Via(10), Port("1", 0, 1.45), Port("2", 20, 1.45), Port("3", 10, 1.45)];

        TypePort(shapes, "3", PlanarPortKind.Internal);
        var r = EmPortExtraction.Extract(shapes, Problem(shapes), Dbu, null, LayoutUnit.Um);

        Assert.True(r.Ok, r.Refusal);
        Assert.Equal(PlanarPortKind.Internal, r.Ports[2].Kind);
        Assert.Equal(PlanarBasisDirection.Z,       r.Ports[2].Direction);
        Assert.Contains(r.Notes, n => n.Contains("INTERNAL port"));
        Assert.Contains(r.Notes, n => n.Contains("direction the label points is not read"));
    }

    [Fact]
    public void AnInternalPortOnBAREMetal_JustWorks_AndSaysThePathWasBuiltForIt()
    {
        // THE ORDINARY CASE, and the one this port type exists for: put it on the metal, at the
        // point you want referenced to ground. There is no via there and the user should not have to
        // draw one — the run builds the path down to the plane, at the technology's own via size,
        // and reports that it did.
        LayoutShape[] shapes = [Line(), Port("1", 0, 1.45), Port("2", 20, 1.45), Port("3", 10, 1.45)];
        var problem = Problem(shapes);

        TypePort(shapes, "3", PlanarPortKind.Internal);
        var r = EmPortExtraction.Extract(
            shapes, problem, Dbu, null, LayoutUnit.Um,
            EmPortExtraction.DefaultGroundPathWidthM(StarterTechnologies.Pcb2Layer()));

        Assert.True(r.Ok, r.Refusal);
        Assert.Equal(PlanarPortKind.Internal, r.Ports[2].Kind);
        Assert.NotNull(r.Ports[2].GroundPathWidthM);
        Assert.Contains(r.Notes, n => n.Contains("no via under it"));

        // …and the engine really does build it: the problem the mesher sees carries a via to the
        // plane that the artwork never had, at the width the port asked for.
        var (grown, built, notes) = PlanarGroundPath.Extend(problem, r.Ports);
        Assert.NotSame(problem, grown);
        Assert.Empty(problem.ViaList);
        Assert.Equal(3, Assert.Single(built).PortNumber);
        Assert.Contains(grown.ViaList, v => v.ToGround);
        Assert.Contains(notes, n => n.Contains("was built for it"));
    }

    [Fact]
    public void ADRAWNViaWINS_AndNothingIsGrownForAPortStandingOnOne()
    {
        // The port drives the via that is there, at the size and shape it was drawn — the built path
        // only ever fills in where the artwork has none, and the problem reaches the mesher BY
        // REFERENCE when it has nothing to add.
        LayoutShape[] shapes = [Line(), Via(10), Port("1", 0, 1.45), Port("2", 20, 1.45), Port("3", 10, 1.45)];
        var problem = Problem(shapes);

        TypePort(shapes, "3", PlanarPortKind.Internal);
        var r = EmPortExtraction.Extract(
            shapes, problem, Dbu, null, LayoutUnit.Um,
            EmPortExtraction.DefaultGroundPathWidthM(StarterTechnologies.Pcb2Layer()));

        Assert.True(r.Ok, r.Refusal);
        Assert.Contains(r.Notes, n => n.Contains("stands on a via you drew"));

        var (grown, built, _) = PlanarGroundPath.Extend(problem, r.Ports);
        Assert.Same(problem, grown);
        Assert.Empty(built);
    }

    [Fact]
    public void ATechnologyWithNoDefaultViaSize_StillWorks_AndSaysWhatItAssumed()
    {
        // The stackup's own Via entries carry a fill, a wall and a span — never a diameter — so
        // "this technology declares no via" must not be a reason to refuse the port. The width falls
        // back to a quarter of the substrate height, which is a rule of thumb, and is reported as
        // one: the number is the user's to fix by drawing a via.
        LayoutShape[] shapes = [Line(), Port("1", 10, 1.45)];

        TypePort(shapes, "1", PlanarPortKind.Internal);
        var r = EmPortExtraction.Extract(shapes, Problem(shapes), Dbu, null, LayoutUnit.Um);

        Assert.True(r.Ok, r.Refusal);
        Assert.NotNull(r.Ports[0].GroundPathWidthM);
        Assert.Contains(r.Notes, n => n.Contains("declares no default via size")
                                   && n.Contains("quarter of the substrate height"));

        // A quarter of the 1.6 mm FR-4 starter's substrate, which is the right ORDER for a real via
        // rather than a number pulled out of the air.
        Assert.Equal(0.4e-3, r.Ports[0].GroundPathWidthM!.Value, 6);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // The panel
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "crf-viaport-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }

    private static LayoutView PortedLineWithVia()
    {
        var view = new LayoutView { DbuPerMicron = Dbu };
        view.Shapes.Add(Line());
        view.Shapes.Add(Via(10));
        view.Shapes.Add(Port("1", 0,  1.45));
        view.Shapes.Add(Port("2", 20, 1.45));
        view.Shapes.Add(Port("3", 10, 1.45));
        return view;
    }

    /// <summary>The panel plus the LIVE layout it edits. The port type is stated on the LABEL
    /// (2026-09-14), and <c>SetPortKind</c> is what <c>WorkspaceViewModel</c> supplies, minus the
    /// document plumbing.</summary>
    private static (EmSetupEditorViewModel Vm, LayoutView View) EditorAndLayout(string dir)
    {
        string path  = Path.Combine(dir, "panel.cem");
        var    setup = new EmSetup
        {
            Name = "panel", LayoutRef = "a.clay", AnalysisKind = EmAnalysisKind.Planar,
        };
        EmSetupPersistence.SaveToFile(path, setup);

        var view = PortedLineWithVia();
        TypePort(view.Shapes, "3", PlanarPortKind.Internal);

        var vm = new EmSetupEditorViewModel(path, setup)
        {
            ResolveLayout = _ => new EmLayoutSource(
                Path.Combine(dir, "a.clay"), view, StarterTechnologies.Pcb2Layer(), Dbu),
            SetPortKind = (label, kind) => { label.PortKind = kind; view.NotifyChanged(); },
        };
        vm.Refresh();
        return (vm, view);
    }

    private static EmSetupEditorViewModel Editor(string dir) => EditorAndLayout(dir).Vm;

    [Fact]
    public void ThePanelNamesTheInternalRowForWhatItIs_NotForAnEndOrADirection()
    {
        var vm = Editor(TempDir());

        Assert.Null(vm.PortRefusal);
        Assert.Equal(3, vm.PortRows.Count);
        Assert.Equal(PlanarPortKind.Internal, vm.PortRows[2].Kind);
        Assert.Equal("Port 3 — internal, to ground", vm.PortRows[2].Label);

        // The other two are unchanged, so the row label is about the type rather than the list.
        Assert.EndsWith("end", vm.PortRows[0].Label, StringComparison.Ordinal);

        // And the type is offered by name, from the enum — "Internal", not the
        // enum's own spelling.
        Assert.Contains(PlanarPortKind.Internal, EmPortZ0Row.KindChoices);
        Assert.Equal("Internal", PlanarPortKindNameConverter.Label(PlanarPortKind.Internal));
    }

    [Fact]
    public void ThePanelReadsThePortTypeOffTheLABEL_AndPublishesNothingToTheLayout()
    {
        // This replaces a test for the channel that no longer exists. The panel used to publish an
        // anchor-and-kind list the drawing was told to obey — built from the .cem's own port-kind
        // list, which answered "edge port" for every port it had never been told about. That is what
        // redrew a port placed in the middle of a trace as an edge port one frame after the click
        // (owner report, 2026-09-14). The type is on the label now: the panel READS it and WRITES it,
        // and the drawing needs telling nothing.
        var (vm, view) = EditorAndLayout(TempDir());

        Assert.Equal(PlanarPortKind.Internal, vm.PortRows[2].Kind);

        var p3 = view.Shapes.OfType<LabelShape>().Single(l => l.Text == "3");
        Assert.Equal(PlanarPortKind.Internal, p3.PortKind);

        // …and the row the panel shows IS the label's own answer, for every port, in both
        // directions — the owner's requirement that a type changed in the .cem window draws properly
        // in the layout includes changing one BACK to Edge.
        vm.PortRows[2].Kind = PlanarPortKind.Edge;
        Assert.Equal(PlanarPortKind.Edge, p3.PortKind);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // The mark
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private static SKSurface RenderWith(PlanarPortKind? kind)
    {
        var view = new LayoutView { DbuPerMicron = Dbu, DisplayUnit = LayoutUnit.Um, SnapDbu = 1000 };
        view.Shapes.Add(Line());
        view.Shapes.Add(Port("1", 10, 1.45, LayoutRotation.R0));

        var bb = Bbox.Empty;
        foreach (var s in view.Shapes) bb = bb.Union(LayoutGeometry.BboxOf(s));
        var vp = LayoutViewport.ZoomToFit(bb, 400, 400, 0.2);

        var surface = SKSurface.Create(new SKImageInfo((int)vp.Width, (int)vp.Height));
        // The type is on the LABEL, so a render of a given type is a render of a label that states
        // it. `null` is a port that states nothing, which infers from the artwork.
        ((LabelShape)view.Shapes[^1]).PortKind = kind;
        var opts = new LayoutRenderOptions { Theme = LayoutRenderTheme.Light };
        LayoutRenderer.Draw(surface.Canvas, view, StarterTechnologies.Pcb2Layer(), vp, opts);
        return surface;
    }

    private static byte[] Pixels(SKSurface s)
    {
        using var img = s.Snapshot();
        using var bmp = SKBitmap.FromImage(img);
        return bmp.Bytes;
    }

    [Fact]
    public void TheMarkIsDrawnAtTheMESHEDFootprintOnceThereIsAMesh()
    {
        // The same rule the gap's break follows: a glyph before there is anything to measure, the
        // real thing after. An internal port drives the ground-attachment cells under the label, so
        // the ring is drawn round exactly those — which is also how a user sees a footprint the mesh
        // resolved smaller than they drew.
        var view = new LayoutView { DbuPerMicron = Dbu, DisplayUnit = LayoutUnit.Um, SnapDbu = 1000 };
        view.Shapes.Add(Line());
        view.Shapes.Add(Via(10));
        view.Shapes.Add(Port("3", 10, 1.45));

        var extraction = PlanarExtractor.Extract(view.Shapes, StarterTechnologies.Pcb2Layer(), Dbu, 10e9);
        Assert.True(extraction.Ok, extraction.Refusal);
        var report = SurfaceMesher.Mesh(extraction.Problem!);

        var label = view.Shapes.OfType<LabelShape>().Single();
        var fp = LayoutRenderer.MeshViaFootprint(report, label, Dbu);

        Assert.NotNull(fp);
        // It is the VIA's own extent, not the label's and not the conductor's: the equal-area square
        // of a 0.36 mm drill is ~0.319 mm on a side, and its edges are hard gridlines.
        double side = 0.5 * ((fp!.Value.X1 - fp.Value.X0) + (fp.Value.Y1 - fp.Value.Y0));
        Assert.InRange(side, Mm(0.2), Mm(0.5));
        Assert.InRange(0.5 * (fp.Value.X0 + fp.Value.X1), Mm(9.8), Mm(10.2));

        // And with no mesh there is nothing to measure — the mark stays a glyph rather than
        // reporting a size it does not have.
        Assert.Null(LayoutRenderer.MeshViaFootprint(null, label, Dbu));
    }

    [Fact]
    public void AnInternalPortDrawsItsOwnMark_NotAnEdgePortsAndNotAGaps()
    {
        // A differential render, because the claim is only that the three marks are different from
        // each other — asserting particular pixels would be asserting the glyph's design, which is
        // free to change, rather than the property that a reader can tell them apart.
        // `Edge` EXPLICITLY, not null (2026-09-09). Null means "no EM setup has claimed this port",
        // which is no longer a synonym for "edge port": this fixture's label sits at 10 mm along a
        // 20 mm line, in the middle of the metal, and an unclaimed port there is now drawn where it
        // stands rather than at a conductor end it is 10 mm from. Saying Edge here is the .cem saying
        // Edge, which is what this test is about and what still produces an edge port's bar.
        using var edge  = RenderWith(PlanarPortKind.Edge);
        using var gap   = RenderWith(PlanarPortKind.InternalDeltaGap);
        using var via = RenderWith(PlanarPortKind.Internal);

        Assert.NotEqual(Pixels(edge),  Pixels(via));
        Assert.NotEqual(Pixels(gap),   Pixels(via));

        // And it is drawn at all — the same render with no port mark differs from itself only if
        // something was painted, so this also pins that the via-port branch is reached.
        Assert.NotEqual(Pixels(edge), Pixels(gap));
    }

    /// <summary>
    /// <b>The owner's report, at the pixel: a port no EM setup has claimed, standing in the middle of
    /// a piece of metal, is drawn as an internal port and NOT as an edge port.</b>
    ///
    /// <para>This is the pair that used to be identical — <c>RenderWith(null)</c> was the edge-port
    /// reference above — and the identity was the bug: with no <c>.cem</c> open there was no way to
    /// draw a port anywhere but at a conductor end.</para>
    /// </summary>
    [Fact]
    public void AnUnclaimedPortInTheMiddleOfMetal_DrawsAsAnInternalPort_NotAnEdgeOne()
    {
        using var unclaimed = RenderWith(null);
        using var edge      = RenderWith(PlanarPortKind.Edge);
        using var via       = RenderWith(PlanarPortKind.Internal);

        Assert.Equal(Pixels(via), Pixels(unclaimed));
        Assert.NotEqual(Pixels(edge), Pixels(unclaimed));
    }
}
