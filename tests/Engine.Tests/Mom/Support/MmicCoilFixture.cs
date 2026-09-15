// EFAN — the part the edge-fan complaint was actually made about, as a fixture.
//
// A 3-turn square spiral inductor: 10 um turns, 8 um spacing, 120 um inner opening, on Metal1 over
// 100 um of GaAs, with its inner terminal escaping by a via post, a span on Metal2 and a second post
// onto a landing pad. 270 x 250 um of envelope. It is the shipped PCell example's own artwork at its
// own default parameters, so it carries no user path, no workspace name and no vendor reference; the
// coordinates below are its generator's own output, in micrometres.
//
// IT IS HERE BECAUSE NO LINE REPRODUCES THE SHAPE OF THE PROBLEM. Every fixture in
// PlanarLineFixtures is one or two pieces of metal with two or four axis-parallel rims; a coil is 28
// rims packed at 18 um pitch across 250 um of tensor grid, which is what makes the edge fan cost
// 3.6x the unknowns at the default and 45x at the coarsest. A gate on the fan's cost written over a
// line measures nothing.

using CircuitRF.Engine.Mom;

namespace CircuitRF.Engine.Tests.Mom.Support;

public static class MmicCoilFixture
{
    private const double U = 1e-6;

    /// <summary>The merged winding plus its outer lead — one rectilinear outline, 39 vertices.</summary>
    private static readonly double[] CoilXy =
    [
        -105,-105, -105,115, 105,115, 105,-77, -87,-77, -87,97, 87,97, 87,-59, -69,-59, -69,79,
         69,79, 69,-41, -61,-41, -61,-51, 79,-51, 79,79, 79,89, 69,89, -69,89, -79,89, -79,-69,
        -69,-69, 97,-69, 97,97, 97,107, 87,107, -87,107, -97,107, -97,-87, -87,-87, 115,-87,
        115,115, 115,125, 105,125, -105,125, -115,125, -115,-95, -115,-125, -105,-125,
    ];

    /// <summary>The landing pad the inner escape comes back down onto — a separate piece of metal.</summary>
    private static readonly double[] PadXy = [-125,-41, -155,-41, -155,-51, -125,-51];

    private static PlanarPolygon Poly(double[] xyUm)
    {
        var pts = new List<EmPoint>(xyUm.Length / 2);
        for (int i = 0; i < xyUm.Length; i += 2) pts.Add(new EmPoint(xyUm[i] * U, xyUm[i + 1] * U));
        return new PlanarPolygon(pts);
    }

    private static PlanarPolygon Rect(double x1, double y1, double x2, double y2) =>
        PlanarLineFixtures.Rect(x1 * U, y1 * U, x2 * U, y2 * U);

    /// <summary>
    /// The coil as a two-level planar problem: Metal1 on the GaAs surface, Metal2 3 um above it, and
    /// the two via posts between them.
    /// </summary>
    /// <param name="withVias">
    /// <b>False reproduces the numbers recorded in <c>RESOLVED.md</c> §"Why the edge mesh is
    /// unaffordable on a MMIC spiral"</b>, which were measured without them — the two posts add
    /// exactly 162 vertical unknowns at the default mesh and nothing else. The default is the real
    /// part.
    /// </param>
    public static PlanarProblem Coil(double fHz = 20e9, bool withVias = true)
    {
        // GaAs 100 um on the backside plane, then the 3 um of mostly-air the MIM and Metal2 stack
        // occupies — Metal1 sits at the GaAs surface and Metal2 at the top of it.
        var stack = new LayerStack(
            Termination.Pec,
            [
                new MediumLayer(100e-6, new EmMaterial(12.90, 0.0006)),
                new MediumLayer(  3e-6, new EmMaterial( 1.00, 0.0000)),
            ],
            Termination.Air);

        var metal1 = new PlanarConductorLayer("Metal1", [Poly(CoilXy), Poly(PadXy)], 4.1e7, 3e-6, 100e-6);
        var metal2 = new PlanarConductorLayer("Metal2", [Rect(-135, -51, -51, -41)], 4.1e7, 3e-6, 103e-6);

        PlanarVia[] vias =
        [
            new(0, 1, [Rect(-61,  -51,  -51, -41)], 4.1e7),
            new(0, 1, [Rect(-135, -51, -125, -41)], 4.1e7),
        ];

        return new PlanarProblem([metal1, metal2], GroundedSlab.GaAsStarter, fHz,
                                 MediumStack: stack, Vias: withVias ? vias : null);
    }
}
