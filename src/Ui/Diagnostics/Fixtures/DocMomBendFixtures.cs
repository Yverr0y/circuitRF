using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CircuitRF.Core.Design;
using CircuitRF.Design.Layout.Em;
using CircuitRF.Engine.Mom;
using CircuitRF.Render.DataDisplay;
using CircuitRF.Ui.DataDisplay.ViewModels;
using CircuitRF.Ui.Layout;
using CircuitRF.Ui.Layout.Em;
using CircuitRF.Ui.Views.DataDisplay;
using CircuitRF.Ui.Views.Layout;

namespace CircuitRF.Ui.Diagnostics.Fixtures;

/// <summary>
/// The figures of the MoM chapter's worked example — <b>a 50 Ω microstrip line with an unmitred
/// right-angle bend on the PCB starter technology</b>.
///
/// <para><b>The artwork is built here, and that is the opposite of the rule
/// <see cref="DocAntennaFixtures"/> states, on purpose.</b> The antenna page is written ABOUT a
/// committed design and quotes its numbers, so a second copy under <c>src/Ui</c> would drift away
/// from the thing the page is about. This page is an INSTRUCTION: it tells a reader to draw two
/// rectangles at four stated coordinates, and the figures have to be of exactly those coordinates.
/// Building them from the same constants the prose quotes is what keeps the two in step — there is
/// no file for them to be a copy OF.</para>
///
/// <para>Everything down to <see cref="MeshSettings"/> costs milliseconds and is regenerated
/// ordinarily. The three RESULT figures need the structure SOLVED, so they are <c>Static</c> in
/// <see cref="FigureCatalog"/> — committed pictures an ordinary regeneration leaves alone, redrawn
/// by <c>--rebuild-static</c>, exactly as AN-01's and the antenna's are.</para>
/// </summary>
public static class DocMomBendFixtures
{
    private const int Dbu = LayoutUnits.DefaultDbuPerMicron;

    /// <summary>A length in mil, which is the unit the worked example is written in — the PCB
    /// starter technology's own display unit, so the numbers on the page are the numbers a reader
    /// types.</summary>
    private static long Mil(decimal v) => LayoutUnits.ToDbu(v, LayoutUnit.Mil, Dbu);

    // ── The dimensions the PAGE quotes. Nothing here may drift from the prose. ─────────────────
    //
    //   horizontal arm:   (0, 0)     to (400, 114)
    //   vertical arm:     (286, 0)   to (400, 400)
    //
    // 114 mil is 2.9 mm, which is 50 Ω on 1.6 mm FR-4. The two arms overlap in the 114 mil corner
    // square, which is what makes them ONE conductor and what makes this a bend rather than two
    // lines. 286 = 400 − 114 is therefore each arm's length up to the bend's reference plane, and it
    // is the length the circuit-model comparison gives its two MLINs.

    private const decimal WidthMil = 114m, ArmMil = 400m, RunMil = ArmMil - WidthMil;

    /// <summary>
    /// The port label height.
    ///
    /// <para><b>Sized against the ARROWHEAD, not against the frame.</b> A port's name is centred on
    /// the same anchor its direction arrow points at, and the renderer knocks the name's own
    /// footprint out of the marker so the name reads on top. On a 114 mil port the arrow's barb is
    /// 0.22 of the width at most — 25 mil — so at 28 the numeral was bigger than the head it sits
    /// in, and the knock-out took most of the head with it. 22 mil is a little over one barb, which
    /// leaves the head readable as an arrow and the numeral readable as a numeral.</para>
    ///
    /// <para><b>This is NOT what made the marker invisible in the first place</b>, and it is worth
    /// saying so: the knock-out was expressed as an <c>SKClipOperation.Difference</c>, which inverts
    /// on the way into an SVG, so the whole glyph was clipped down to the inside of the numeral. See
    /// <c>src/Render/RESOLVED.md</c>. The size is a proportion judgement on top of that fix, not the
    /// fix.</para>
    /// </summary>
    private static long PortLabelHeight => Mil(22);

    /// <summary>
    /// <b>The artwork itself: two overlapping rectangles on Top Copper.</b> No ports — this is the
    /// figure of step 1, where there are none yet.
    /// </summary>
    private static LayoutView Bend()
    {
        var tech = StarterTechnologies.Pcb2Layer();
        var top  = DocLayoutFixtures.Layer(tech, "Top Copper");

        var view = new LayoutView
        {
            DbuPerMicron = Dbu,
            DisplayUnit  = tech.DefaultDisplayUnit,   // mil, which is what the page's numbers are in
            SnapDbu      = tech.DefaultSnapDbu,
        };

        // Two RECTANGLES rather than one traced polygon, because that is what the page tells the
        // reader to draw: the corner square is the overlap, and the union is one conductor either
        // way. Nothing downstream depends on the vertex list.
        view.Shapes.Add(new RectShape { Layer = top, X1 = 0,                Y1 = 0, X2 = Mil(ArmMil), Y2 = Mil(WidthMil) });
        view.Shapes.Add(new RectShape { Layer = top, X1 = Mil(RunMil),      Y1 = 0, X2 = Mil(ArmMil), Y2 = Mil(ArmMil)   });
        return view;
    }

    /// <summary>
    /// The same artwork with the two edge ports on it: one on the horizontal arm's left end face,
    /// one on the vertical arm's top end face.
    ///
    /// <para>Both directions point INWARD — current flows into the structure through both ends of a
    /// two-port — and both labels sit at the CENTRE of their own end face, which is the placement
    /// that resolves without a guess. At a corner the label is equally close to two edges and the
    /// run refuses by name rather than reversing the direction of current silently.</para>
    /// </summary>
    private static LayoutView BendWithPorts()
    {
        var tech = StarterTechnologies.Pcb2Layer();
        var top  = DocLayoutFixtures.Layer(tech, "Top Copper");
        var view = Bend();
        long h   = PortLabelHeight;

        // Port 1: the low-x end face of the horizontal arm, current flowing +x.
        view.Shapes.Add(DocLayoutFixtures.PortLabel(
            top, "1", 0, Mil(WidthMil / 2m), LayoutRotation.R0, h));
        // Port 2: the high-y end face of the vertical arm, current flowing −y (R270 → MaxY).
        view.Shapes.Add(DocLayoutFixtures.PortLabel(
            top, "2", Mil(RunMil + WidthMil / 2m), Mil(ArmMil), LayoutRotation.R270, h));
        return view;
    }

    // ── The frame every layout figure of this example uses ─────────────────────────────────────
    //
    // Square artwork, so the two margins are equal and the FRAME is the thing that has to be
    // taller than it is wide: DocLayoutFixtures.Framed is reliable only while the fit is bound by
    // WIDTH (height/worldH > width/worldW), and at 560x620 on a 528 mil world it is, with room to
    // spare. See the note in src/Ui/Diagnostics/RESOLVED.md for what happens when it is not.

    private const int FrameW = 560, FrameH = 620;
    private const double Margin = 0.16;

    /// <summary><b>Step 1, drawn.</b> The two rectangles and nothing else.</summary>
    public static FigureScene Layout()
        => DocLayoutFixtures.Framed(
            new LayoutDocument("Bend", DocLayoutFixtures.EditorVm(Bend())),
            FrameW, FrameH, Margin, Margin);

    /// <summary><b>Step 3, drawn.</b> The same artwork carrying its two edge ports.</summary>
    public static FigureScene Ports()
        => DocLayoutFixtures.Framed(
            new LayoutDocument("Bend", DocLayoutFixtures.EditorVm(BendWithPorts())),
            FrameW, FrameH, Margin, Margin);

    /// <summary>
    /// <b>Step 4, drawn: the surface mesh the defaults produce, over the metal it is a mesh of.</b>
    ///
    /// <para>Built by the REAL mesher from the REAL extracted problem, at the top of the sweep,
    /// which is the frequency the cell-size cap is sized at when the mesh frequency is left blank.
    /// A fabricated grid would agree with itself and with nothing else — and the whole claim the
    /// figure makes is that the edge mesh puts several graded cells across each 114 mil edge while
    /// the interior stays at the wavelength pitch.</para>
    /// </summary>
    public static FigureScene Mesh()
    {
        var view = BendWithPorts();
        var vm   = DocLayoutFixtures.EditorVm(view);
        var tech = StarterTechnologies.Pcb2Layer();

        var planar = PlanarExtractor.Extract(view.Shapes, tech, Dbu, TopOfSweepHz);
        if (!planar.Ok)
            throw new InvalidOperationException(
                "The worked example's mesh figure could not extract its own artwork, so there is "
              + "nothing to mesh: " + planar.Refusal);

        var report = SurfaceMesher.Mesh(planar.Problem!, PlanarMeshSettings.Default);
        if (report.Mesh.Cells.Count == 0)
            throw new InvalidOperationException(
                "The worked example's mesh figure meshed to nothing, so it would show bare metal "
              + "under a caption describing a mesh.");

        vm.PlanarMeshReport = report;
        vm.ShowPlanarMesh   = true;

        return DocLayoutFixtures.Framed(
            new LayoutDocument("Bend", vm), FrameW, FrameH, Margin, Margin);
    }

    /// <summary>
    /// <b>Step 4's settings, in the panel that owns them</b> — a CROP of the real EM Setup editor to
    /// its Surface mesh group, for the reason <see cref="FigureCrop"/> exists: the group is eight
    /// rows near the bottom of a panel two thousand pixels tall, and a reader told to "leave the
    /// mesh on its defaults" needs to see which eight numbers that means.
    ///
    /// <para>The rectangle comes from the live visual tree by the group's NAME
    /// (<c>SurfaceMeshGroup</c> in <c>EmSetupEditorView.axaml</c>), never from a pixel offset — a
    /// measured offset is right until the panel gains a row, and then the figure is of the wrong
    /// part of it with nothing to say so.</para>
    ///
    /// <para>The mesh is BUILT before the capture, so the group carries its own summary line
    /// underneath the settings — which is the mesh report step 4 tells the reader to read.</para>
    /// </summary>
    public static FigureScene MeshSettings()
    {
        var (setup, source, cem) = Problem();
        var vm = new EmSetupEditorViewModel(cem, setup) { ResolveLayout = _ => source };
        vm.BuildActiveMesh(null);

        if (!vm.IsPlanarAnalysis)
            throw new InvalidOperationException(
                "The worked example's mesh-settings figure needs the full-wave planar kernel, and "
              + "the registry chose " + (vm.SelectedKernelName is { Length: > 0 } n ? n : "another one")
              + " for a right-angle bend. The Surface mesh group is not even visible on the other "
              + "kernel, so the figure would be of whatever is in its place.");

        var panel = new EmSetupEditorView { DataContext = new EmSetupDocument("Bend", vm, cem) };

        var crop = FigureCrop.Around(
            panel, 600, 2400,
            c => c.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "SurfaceMeshGroup"),
            pad: 8,
            describeWhatIsMissing:
                "This figure crops to the EM Setup panel's Surface mesh group, by the name "
              + "SurfaceMeshGroup in EmSetupEditorView.axaml. If that Border is renamed or removed, "
              + "this fixture must follow it rather than guess at an offset.");

        return new FigureScene(crop.Content) { AfterLayout = _ => crop.Apply() };
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    //  The RESULT
    //
    //  Everything above is a picture of the DESIGN. What follows needs it SOLVED — a de-embedded
    //  full-wave sweep, which is minutes rather than milliseconds, which is why the three figures
    //  below are Static.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The band the page's own step 5 asks for, and the point count with it.</summary>
    private const double BottomOfSweepGHz = 1.0, TopOfSweepGHz = 10.0;
    private const int    SweepPoints      = 101;
    private const double TopOfSweepHz     = TopOfSweepGHz * 1e9;

    /// <summary>
    /// The setup, the layout source and the <c>.cem</c> path the panel figure is captioned with.
    ///
    /// <para><b>Everything is left on its default</b> — the analysis on Auto (a bend is not a
    /// uniform cross-section, so the registry picks the full-wave kernel and says so), the mesh on
    /// <c>PlanarMeshSettings.Default</c>, adaptive sampling on, de-embedding on, both ports at
    /// 50 Ω. That is what the page tells the reader to do, so it is what the figures have to be
    /// of.</para>
    ///
    /// <para>The path is a scratch one and no file is written at it: it names the setup for the
    /// panel's title bar and gives <c>EmGeometry.Flatten</c> a base to resolve against, and this
    /// artwork references nothing.</para>
    /// </summary>
    private static (EmSetup Setup, EmLayoutSource Source, string CemPath) Problem()
    {
        string dir = Path.Combine(Path.GetTempPath(), "circuitrf-docgen-" + Environment.ProcessId, "mom-bend");
        Directory.CreateDirectory(dir);

        var setup = new EmSetup
        {
            Name      = "Bend",
            LayoutRef = "Bend.clay",
            Frequency = new FrequencySpec(
                BottomOfSweepGHz.ToString(System.Globalization.CultureInfo.InvariantCulture),
                TopOfSweepGHz.ToString(System.Globalization.CultureInfo.InvariantCulture),
                SweepPoints, SweepKind.Linear, "GHz", "GHz"),
        };

        var source = new EmLayoutSource(
            Path.Combine(dir, "Bend.clay"), BendWithPorts(), StarterTechnologies.Pcb2Layer(), Dbu);

        return (setup, source, Path.Combine(dir, "Bend.cem"));
    }

    /// <summary>
    /// <b>The bend, solved.</b> Returns the result's file name, which is the data-source library's
    /// own logical id.
    ///
    /// <para>Run once per process, into the documentation's own results directory. It is the same
    /// call the Simulate button and the <c>em</c> verb make, on the same setup the panel figure
    /// above is a picture of, so the curves on the page are the curves that setup produces.</para>
    /// </summary>
    public static string EmResult() => EmRun.Value;

    private static readonly Lazy<string> EmRun = new(() =>
    {
        var (setup, source, _) = Problem();

        Directory.CreateDirectory(DocRunData.ResultsRoot);
        var result = EmRunService.Run(setup, source, DocRunData.ResultsRoot);

        if (result.Status != EmRunStatus.Ok || result.NpyPath is null)
            throw new InvalidOperationException(
                "The worked example's bend did not solve, so its result figures would be empty "
              + "frames: " + (result.Error ?? result.Status.ToString()));

        // The NPY, not the .sNp beside it: the data-source picker enumerates results/*.npy, so that
        // is the id it offers and the name it shows in the toolbar combo.
        return Path.GetFileName(result.NpyPath);
    });

    /// <summary>
    /// <b>The same bend as a circuit model</b> — MLIN, MBEND, MLIN — over the same band, for the
    /// comparison step 7 tells the reader to make.
    ///
    /// <para><b>Every substrate number is left unstated on purpose.</b> The microstrip family's own
    /// defaults are 1.6 mm of ε<sub>r</sub> 4.4 FR-4 with tanδ 0.02 and 35 µm of copper — the PCB
    /// starter technology's stackup, exactly — so a netlist that restated them could disagree with
    /// the stackup the EM run used and nothing would say so. What IS stated is the geometry, in the
    /// page's own unit: 114 mil wide, and 286 mil of line either side of the bend, which is where
    /// the bend's own reference planes are (400 − 114, the corner square's edges).</para>
    ///
    /// <para>The bend is UNMITRED, which is <c>MBEND</c>'s default and is what the artwork draws —
    /// the page leaves the corner square in deliberately, because an unmitred bend is the thing
    /// being measured.</para>
    /// </summary>
    public static string CircuitResult() => DocRunData.RunNetlistText("Bend_circuit", $"""
        ; The MoM chapter's worked example, as a CIRCUIT model: two microstrip lines and the
        ; junction between them. Generated for the user documentation.
        Port:P1 in  0   Num=1 Z=50 Ohm
        MLIN:T1  in  b    W={WidthMil} mil L={RunMil} mil
        MBEND:B1 b   c    W={WidthMil} mil
        MLIN:T2  c   out  W={WidthMil} mil L={RunMil} mil
        Port:P2 out 0   Num=2 Z=50 Ohm
        analysis SP1 type=sparam start={BottomOfSweepGHz} stop={TopOfSweepGHz} npts={SweepPoints} Unit=GHz
        """);

    /// <summary>
    /// <b>S(1,1) on its own Smith chart</b>, 1–10 GHz.
    ///
    /// <para>It is here rather than folded into the rectangular figure below because it answers a
    /// different question: a magnitude plot says how big the mismatch is, and the chart says what
    /// KIND it is. An unmitred bend's excess is a shunt capacitance, so the locus runs clockwise
    /// into the lower half of the chart and away from the open — which is a thing to recognise, not
    /// a number to read.</para>
    /// </summary>
    public static FigureScene SmithS11()
    {
        var (vm, plot) = DocDataDisplayFixtures.PlotFor(EmResult(), PlotType.Smith);
        DocDataDisplayFixtures.PickSignal(plot.Inspector.Traces[0], "S(1,1)");
        return new FigureScene(
            new DataDisplayView { DataContext = DocDataDisplayFixtures.Document(vm, "Bend") })
        { AfterLayout = DocDataDisplayFixtures.CentredPlot(plot) };
    }

    /// <summary>
    /// <b>Return loss, insertion loss and both phases on one pair of axes</b> — |S(1,1)| and
    /// |S(2,1)| in dB on the left, ∠S(1,1) and ∠S(2,1) in degrees on the RIGHT.
    ///
    /// <para>The two phases are on the secondary axis because they have to be: a phase runs over
    /// ±180° and a return loss over tens of dB, and on one axis the dB curves flatten into the
    /// bottom of the frame. The right-hand axis is the trace card's own left/right toggle, so this
    /// is an arrangement a reader can reach rather than a figure-only drawing mode.</para>
    ///
    /// <para>The phase traces are DASHED, which is the only thing separating them from the
    /// magnitudes at a glance once the plot is in black and white or a reader is colour-blind.</para>
    /// </summary>
    public static FigureScene MagnitudeAndPhase()
    {
        var (vm, plot) = DocDataDisplayFixtures.PlotFor(
            EmResult(), PlotType.Rect, traces: 4, size: (700.0, 400.0));

        string[] signals = ["S(1,1)", "S(2,1)", "S(1,1)", "S(2,1)"];
        for (int i = 0; i < signals.Length; i++)
        {
            var card = DocDataDisplayFixtures.Card(plot, i);
            DocDataDisplayFixtures.PickSignal(card(), signals[i]);
            bool phase = i >= 2;
            DocDataDisplayFixtures.SetTransform(card, phase ? CubeTransform.Phase : CubeTransform.dB20);
            if (phase)
            {
                DocDataDisplayFixtures.UseRightAxis(card);
                DocDataDisplayFixtures.Dashed(card());
            }
        }

        return new FigureScene(
            new DataDisplayView { DataContext = DocDataDisplayFixtures.Document(vm, "Bend") })
        { AfterLayout = DocDataDisplayFixtures.CentredPlot(plot) };
    }

    /// <summary>
    /// <b>Step 7's comparison, drawn</b> — the EM run's S(1,1) against the MLIN/MBEND circuit
    /// model's, magnitude in dB on the left and phase in degrees on the right, over the same band.
    ///
    /// <para>Two data sources on one plot, picked per trace through the trace card's own Source
    /// combo — which is the gesture the page describes, and the reason the combo exists. The
    /// circuit-model traces are dashed, so which curve is which is readable without the legend.</para>
    /// </summary>
    public static FigureScene EmVersusCircuit()
    {
        string em = EmResult(), circuit = CircuitResult();

        var (vm, plot) = DocDataDisplayFixtures.PlotFor(
            em, PlotType.Rect, traces: 4, size: (700.0, 400.0), also: [circuit]);

        // (source, transform) per trace: EM magnitude, model magnitude, EM phase, model phase.
        (string Source, CubeTransform T)[] rows =
        [
            (em, CubeTransform.dB20), (circuit, CubeTransform.dB20),
            (em, CubeTransform.Phase), (circuit, CubeTransform.Phase),
        ];

        for (int i = 0; i < rows.Length; i++)
        {
            var card = DocDataDisplayFixtures.Card(plot, i);
            DocDataDisplayFixtures.PickSource(card, rows[i].Source);
            DocDataDisplayFixtures.PickSignal(card(), "S(1,1)");
            DocDataDisplayFixtures.SetTransform(card, rows[i].T);
            if (rows[i].T == CubeTransform.Phase) DocDataDisplayFixtures.UseRightAxis(card);
            if (rows[i].Source == circuit) DocDataDisplayFixtures.Dashed(card());
        }

        return new FigureScene(
            new DataDisplayView { DataContext = DocDataDisplayFixtures.Document(vm, "Bend") })
        { AfterLayout = DocDataDisplayFixtures.CentredPlot(plot) };
    }
}
