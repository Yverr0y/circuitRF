using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CircuitRF.Engine.Mom;
using CircuitRF.Ui.Layout;

namespace CircuitRF.Ui.Diagnostics.Fixtures;

/// <summary>
/// <b>Staircase against conformal, on one piece of artwork, side by side</b> — the figure the MoM
/// chapter's "Conformal boundary cells" section opens on.
///
/// <para>The two panels are the SAME Klopfenstein taper meshed by the SAME mesher at the SAME
/// settings, with one field different: <see cref="PlanarBoundaryCells"/>. That is what makes it a
/// comparison rather than two pictures — anything else that differed between them would be a second
/// explanation for what the reader sees.</para>
///
/// <para><b>The mesh is deliberately coarser than the default, and edge refinement is off.</b> Both
/// are about legibility and the caption says so. At the shipping settings the cells at a conductor
/// rim are a few per cent of the conductor's width, so the boundary treatment — the one thing this
/// figure is of — is drawn at a size a reader cannot see; and the graded edge rows are a second
/// striking pattern along exactly the edge they are meant to be looking at. Neither changes what
/// staircase and conformal DO, which is decided per boundary cell.</para>
/// </summary>
public static class DocConformalMeshFixtures
{
    /// <summary>
    /// The mesh frequency, and it is the low end of the band rather than the top.
    ///
    /// <para>The cell size is the smaller of the wavelength cap and the conductor-width cap, so a
    /// high frequency makes the cells small — and small cells are exactly what hides a staircase.
    /// 3 GHz on this stack puts the pitch where the taper's flank crosses about one cell every two
    /// or three columns, which is the regime in which a staircase looks like a staircase.</para>
    /// </summary>
    private const double MeshFrequencyHz = 3e9;

    /// <summary>One panel per boundary-cell mode; the frame each is drawn in.</summary>
    private const int PanelW = 430, PanelH = 330;

    /// <summary>
    /// <b>The figure.</b> Two panels, each headed with the mode it is drawn in, in the reading order
    /// the section's own prose lists them: staircase (the default) on the left, conformal on the
    /// right.
    /// </summary>
    public static FigureScene StaircaseVersusConformal()
    {
        var (left,  applyL) = Panel("Staircase — the default", PlanarBoundaryCells.Staircase);
        var (right, applyR) = Panel("Conformal", PlanarBoundaryCells.Conformal);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 18,
            Children = { left, right },
        };

        return new FigureScene(row) { AfterLayout = _ => { applyL(); applyR(); } };
    }

    /// <summary>
    /// One panel: a heading, and the taper meshed in <paramref name="mode"/> underneath it.
    ///
    /// <para><b>The mesh is built by the real mesher from the real extracted problem</b>, never
    /// fabricated. The whole claim of the figure is that one of these two follows the drawn outline
    /// and the other does not, and a drawn-by-hand grid would agree with itself and with nothing
    /// else.</para>
    /// </summary>
    private static (Control Control, Action Apply) Panel(string heading, PlanarBoundaryCells mode)
    {
        var view = DocLayoutFixtures.KlopfTaper();
        var vm   = DocLayoutFixtures.EditorVm(view);
        var tech = StarterTechnologies.Pcb2Layer();

        var planar = PlanarExtractor.Extract(
            view.Shapes, tech, LayoutUnits.DefaultDbuPerMicron, MeshFrequencyHz);
        if (!planar.Ok)
            throw new InvalidOperationException(
                $"The {mode} panel could not extract the taper, so there is nothing to mesh: "
              + planar.Refusal);

        // EVERY field is stated rather than taken from PlanarMeshSettings.Default, because the two
        // panels have to differ in exactly one of them and a default that moved would break that
        // silently rather than loudly.
        var report = SurfaceMesher.Mesh(planar.Problem!, new PlanarMeshSettings(
            Auto: false, CellsPerWavelength: 10, EdgeMesh: false, EdgeCells: 0,
            BoundaryCells: mode, MeshFrequencyHz: MeshFrequencyHz,
            MinCellsAcrossConductor: 3, CurrentModel: PlanarCurrentModel.None));

        if (report.Mesh.Cells.Count == 0)
            throw new InvalidOperationException(
                $"The {mode} panel meshed to nothing, so it would show bare metal under a caption "
              + "describing a mesh.");

        vm.PlanarMeshReport = report;
        vm.ShowPlanarMesh   = true;

        var (canvas, apply) = DocLayoutFixtures.FramedCanvas(
            new LayoutDocument("Klopfenstein taper", vm), PanelW, PanelH,
            marginX: 0.06, marginY: 0.06);

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text = heading,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                canvas,
            },
        };

        return (stack, apply);
    }
}
