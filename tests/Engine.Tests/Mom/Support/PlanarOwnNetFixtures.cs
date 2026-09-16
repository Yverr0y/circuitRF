// R-pcal7 — the reduced fixtures for a feed that runs beside its OWN net.
// `docs/sonnet-briefs/brief-em-pcal7-own-net-feed-neighbourhood.md` §M0.
//
// WHY THESE AND NOT PlanarLineFixtures'. Every fixture there is one or two pieces of metal whose
// neighbourhood is either empty or a SEPARATE conductor — which is PCAL1-PCAL4's subject and is
// already covered. What reached a user is the case those cannot express: metal that is the port's
// OWN net running parallel to its feed, which is what a spiral inductor is made of and what PCAL2's
// clearance check used to skip by name.
//
// All 10 um Metal1 on the GaAs 2LM starter medium, all two-port, and all solvable in seconds to a
// minute or two at `Mesh` below. The six of them span the whole decision:
//
//   Straight  ViaHop  Bend        nothing inside the clearance  -> nothing may change (R-pcal4-1)
//   Splay                         own-net feeds 350 um apart    -> PCAL4's group, on own-net metal
//   Coupled                       own-net feeds 50 um apart     -> the same, at the hard separation
//   Hair50                        the U-turn is INSIDE the run  -> a refusal, and it must stay one
//   UnevenPads                    one plane, two lead lengths   -> R-pcal7-3's peel equalisation
//
// The numbers each of them produces, before and after, are in `src/Engine/Mom/RESOLVED.md`
// §PCAL7-OWNNET. The MMIC coil the whole thing was reported on is `MmicCoilFixture`, which already
// existed for EFAN and is the generated artwork of the shipped `examples/PDK PCells` spiral.

using CircuitRF.Engine.Mom;

namespace CircuitRF.Engine.Tests.Mom.Support;

public static class PlanarOwnNetFixtures
{
    private const double U = 1e-6;

    /// <summary>The conductor width every fixture here is drawn at, in microns.</summary>
    public const double WidthUm = 10;

    /// <summary>The edge-to-edge separation the coupled fixtures use, in microns.</summary>
    public const double GapUm = 50;

    /// <summary>
    /// <b>The transmission-line mesh, one cell across the metal.</b> Not a corner cut: a calibration
    /// GROUP stays on the MEASURED separation ladder by design (<c>PlanarCalibration.SeparationPlan</c>
    /// declines the quasi-static shortcut for a group), so its standard is 60 degrees of line long —
    /// 9.78 mm at 2 GHz on this stack. Under the per-axis rule a 10 um conductor forces a 10 um cell
    /// in BOTH directions, which makes that standard 18,224 unknowns and refuses every grouped
    /// fixture here at the dense ceiling. Telling the mesher the metal is a LINE puts lambda along
    /// the current and the width across it, and the same standard is 818.
    /// </summary>
    public static readonly PlanarMeshSettings Mesh = new(
        Auto: false, CellsPerWavelength: 20, EdgeMesh: true, EdgeCells: 3,
        MinCellsAcrossConductor: 1, CurrentModel: PlanarCurrentModel.TransmissionLine);

    /// <summary>The 2LM starter medium — the same stack <see cref="MmicCoilFixture"/> uses, so a
    /// number measured on a reduced fixture and one measured on the coil are comparable.</summary>
    private static readonly LayerStack Stack = new(
        Termination.Pec,
        [
            new MediumLayer(100e-6, new EmMaterial(12.90, 0.0006)),
            new MediumLayer(  3e-6, new EmMaterial( 1.00, 0.0000)),
        ],
        Termination.Air);

    private static PlanarPolygon R(double x0, double y0, double x1, double y1) =>
        PlanarLineFixtures.Rect(x0 * U, y0 * U, x1 * U, y1 * U);

    private static PlanarPolygon Poly(params double[] xyUm)
    {
        var pts = new List<EmPoint>(xyUm.Length / 2);
        for (int i = 0; i < xyUm.Length; i += 2) pts.Add(new EmPoint(xyUm[i] * U, xyUm[i + 1] * U));
        return new PlanarPolygon(pts);
    }

    private static PlanarProblem Problem(double fHz, IEnumerable<PlanarPolygon> metal1,
                                         IEnumerable<PlanarPolygon>? metal2 = null,
                                         PlanarVia[]? vias = null)
    {
        var layers = new List<PlanarConductorLayer>
        {
            new("Metal1", [.. metal1], 4.1e7, 3e-6, 100e-6),
        };
        // Only where there IS a second level. An empty conductor layer changes no number (measured),
        // but it is artwork nobody drew and a fixture should not carry one.
        if (metal2 is not null)
            layers.Add(new PlanarConductorLayer("Metal2", [.. metal2], 4.1e7, 3e-6, 103e-6));

        return new PlanarProblem(layers, GroundedSlab.GaAsStarter, fHz, MediumStack: Stack, Vias: vias);
    }

    private static PlanarPort Port(int n, double xUm, double yUm, PlanarPortSide side) =>
        new(n, new EmPoint(xUm * U, yUm * U), side, 50.0) { LayerIndex = 0 };

    /// <summary>Every fixture this file can build, by the name the gates and RESOLVED.md use.</summary>
    public static readonly string[] Names =
        ["Straight", "ViaHop", "Bend", "Splay", "Coupled", "Hair50", "UnevenPads"];

    public static (PlanarProblem Problem, PlanarPort[] Ports) Fixture(string name, double fHz = 2e9)
    {
        const double W = WidthUm;
        switch (name)
        {
            // ── The three that must not move: nothing is inside either feed's clearance ─────────
            case "Straight":
                return (Problem(fHz, [R(0, 0, 700, W)]),
                        [Port(1, 0, 0.5 * W, PlanarPortSide.MinX),
                         Port(2, 700, 0.5 * W, PlanarPortSide.MaxX)]);

            case "ViaHop":
                // The same 700 um, with x in [300, 400] carried on Metal2 by two via posts — so the
                // multi-level and via paths are exercised by a fixture whose answer must not change.
                return (Problem(fHz, [R(0, 0, 310, W), R(390, 0, 700, W)],
                                [R(300, 0, 400, W)],
                                [new PlanarVia(0, 1, [R(300, 0, 310, W)], 4.1e7),
                                 new PlanarVia(0, 1, [R(390, 0, 400, W)], 4.1e7)]),
                        [Port(1, 0, 0.5 * W, PlanarPortSide.MinX),
                         Port(2, 700, 0.5 * W, PlanarPortSide.MaxX)]);

            case "Bend":
                // One 90-degree corner, 300 um each arm. Its corner is the port's OWN net and is
                // CONTIGUOUS with the feed, which is R-fed-1's case and must stay silent here.
                return (Problem(fHz, [Poly(0, 0, 300, 0, 300, 300, 290, 300, 290, W, 0, W)]),
                        [Port(1, 0, 0.5 * W, PlanarPortSide.MinX),
                         Port(2, 295, 300, PlanarPortSide.MaxY)]);

            // ── The three the brief is about ────────────────────────────────────────────────────
            case "Splay":
            {
                // The same two arms and the same 50 um gap as `Coupled`, with each feed carried
                // 400 um out to a station 340 um from the other before its port. Symmetric, so both
                // arms gain the same length; the coupling starts 400 um in, OUTSIDE the 300 um the
                // standard reproduces. It passed with no flag on it before this brief, and it is the
                // file whose published answer this work changes by 2x — see the coupled-line control
                // in RESOLVED.md §PCAL7-OWNNET, which is what settled which of the two is right.
                const double g = W + GapUm, off = 145;
                return (Problem(fHz,
                        [R(0, -off - W, 400, -off), R(400 - W, -off - W, 400, W), R(400 - W, 0, 800, W),
                         R(0, g + off, 400, g + off + W), R(400 - W, g, 400, g + off + W),
                         R(400 - W, g, 800, g + W),
                         R(800 - W, 0, 800, g + W)]),
                        [Port(1, 0, -off - 0.5 * W, PlanarPortSide.MinX),
                         Port(2, 0, g + off + 0.5 * W, PlanarPortSide.MinX)]);
            }

            case "Coupled":
            {
                // 800 um arms, 50 um apart the whole way, shorted at the far end — the other common
                // way to draw an inductor, and the one extension can never clear because the
                // obstruction grows with the lead.
                const double g = W + GapUm;
                return (Problem(fHz, [R(0, 0, 800, W), R(0, g, 800, g + W), R(800 - W, 0, 800, g + W)]),
                        [Port(1, 0, 0.5 * W, PlanarPortSide.MinX),
                         Port(2, 0, g + 0.5 * W, PlanarPortSide.MinX)]);
            }

            case "Hair50":
            {
                // 300 um arms: the U-turn is INSIDE the 300 um the standard reproduces, so no lead
                // and no group can describe this feed and the run must be refused.
                const double g = W + GapUm;
                return (Problem(fHz, [R(0, 0, 300, W), R(0, g, 300, g + W), R(300 - W, 0, 300, g + W)]),
                        [Port(1, 0, 0.5 * W, PlanarPortSide.MinX),
                         Port(2, 0, g + 0.5 * W, PlanarPortSide.MinX)]);
            }

            case "UnevenPads":
            {
                // Two feeds at ONE reference plane, 50 um apart, on pads of DIFFERENT lengths — so
                // R-fed-1's own cross-section shortfall differs between them and PCAL5's
                // CommonPeelLength refuses the group they are otherwise entitled to. R-pcal7-3 grows
                // the shorter lead to the longer one instead.
                return (Problem(fHz, [Pad(60, 0), Pad(40, 70), R(790, 5, 800, 85)]),
                        [Port(1, 0, 10, PlanarPortSide.MinX),
                         Port(2, 0, 80, PlanarPortSide.MinX)]);
            }
        }
        throw new ArgumentException($"No own-net fixture is called '{name}'.", nameof(name));
    }

    /// <summary>A 20 um pad of the stated length stepping down to a 10 um line — one outline, so the
    /// step is a change of CROSS-SECTION rather than a second piece of metal.</summary>
    private static PlanarPolygon Pad(double padLenUm, double y0) =>
        Poly(0, y0, padLenUm, y0, padLenUm, y0 + 5, 800, y0 + 5,
             800, y0 + 15, padLenUm, y0 + 15, padLenUm, y0 + 20, 0, y0 + 20);

    /// <summary>
    /// <b>M1's oracle geometry</b> — two coupled uniform lines with a port at all four ends, ordered
    /// (A-left, B-left, A-right, B-right) so a block wave-cascade matrix can be formed from it.
    /// </summary>
    public static (PlanarProblem Problem, PlanarPort[] Ports) CoupledPair(
        double fHz, double lengthUm, double separationUm)
    {
        const double W = WidthUm;
        double g = W + separationUm;
        return (Problem(fHz, [R(0, 0, lengthUm, W), R(0, g, lengthUm, g + W)]),
                [Port(1, 0,        0.5 * W,     PlanarPortSide.MinX),
                 Port(2, 0,        g + 0.5 * W, PlanarPortSide.MinX),
                 Port(3, lengthUm, 0.5 * W,     PlanarPortSide.MaxX),
                 Port(4, lengthUm, g + 0.5 * W, PlanarPortSide.MaxX)]);
    }
}
