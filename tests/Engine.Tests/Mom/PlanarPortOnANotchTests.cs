// An EDGE port's run is the END FACE, and the face it names is the one it is STANDING on.
//
// Owner report, 2026-09-09, on a connector-cutout board: a port placed on the wall of a NOTCH in the
// top-copper polygon resolved to a reference plane 5.7 mm away — the far edge of the same polygon,
// and the SAME plane the other port had already claimed. Both ports drove one edge. Nothing refused,
// nothing warned: a complete, plausible two-port answer for a structure nobody drew.
//
// Two independent defects, both invisible on a uniform feed and both exposed by the same shape:
//
//   1. The march inward read only the port's TRANSVERSE coordinate and stopped at the first metal it
//      met from the mesh's own edge. Correct while the port's row crosses one run of metal — every
//      uniform feed, every fixture in PlanarPortTests — and wrong the moment it crosses two.
//   2. The transverse run then walked every rooftop straddling the plane, which at a notch continues
//      well past the end of the wall, because above the wall the conductor turns and keeps going.
//      Driving those rooftops injects current into the MIDDLE of unbroken metal: a delta gap, not an
//      edge feed.
//
// Measured on the reporting board, in order: plane 109.6192 mm / width 1.800 mm (the wrong edge
// entirely) -> 103.9245 mm / 1.213 mm (right wall, 42% too wide) -> 103.9245 mm / 0.853 mm, which is
// the wall, and which is what the layout editor now draws for the same port.

using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

public class PlanarPortOnANotchTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _out = output;

    /// <summary>
    /// A notch, at cell resolution. 10 x 10 cells of 1 mm; metal everywhere EXCEPT the block
    /// x &gt;= 4 mm, y &lt; 6 mm — so along any row below y = 6 mm the metal stops at x = 4 mm and
    /// starts again nowhere, while along any row above it the metal runs the full 10 mm.
    ///
    /// <para>The notch's wall is x = 4 mm, y 0..6 mm. A port on it names that wall — 6 cells — and
    /// the 4 rows above it are metal at the same plane but do not END there.</para>
    /// </summary>
    private static PlanarMesh Notched()
    {
        var gx = new double[11];
        var gy = new double[11];
        for (int i = 0; i < 11; i++) { gx[i] = i * 1e-3; gy[i] = i * 1e-3; }
        return PlanarFillTests.Grid(gx, gy, (ix, iy) => !(ix >= 4 && iy < 6));
    }

    [Fact]
    public void APortOnTheNotchWall_DrivesTheWALL_NotTheFarEdgeAndNotTheMetalAboveIt()
    {
        var mesh = Notched();

        // On the wall (x = 4 mm), a third of the way up it. Side MaxX: current flows -x, into the
        // metal to the left, so the port names the high-x face of the run it is on.
        var p = PlanarPorts.Resolve(mesh, new PlanarPort(1, new EmPoint(4e-3, 2.5e-3),
                                                         PlanarPortSide.MaxX, 50.0));
        _out.WriteLine(p.Describe());

        // (1) The plane is the WALL's, one cell in from x = 4 mm — not the polygon's own far edge at
        //     x = 10 mm, which is where the old march from the mesh boundary landed.
        Assert.Equal(4e-3, p.OuterEdgeM, 12);
        Assert.Equal(3e-3, p.ReferencePlaneM, 12);

        // (2) Six cells of wall, and only six: the four rows above y = 6 mm carry metal at this very
        //     plane, and carry rooftops across it, but the conductor does not end there.
        Assert.Equal(6, p.BasisCount);
        Assert.Equal(6e-3, p.WidthM, 12);

        // Not folded into UndrivenMetalM — that field means "a conformal cell declined to pair here"
        // and says so in words. Metal that never ended at this face is not undriven; it is not this
        // port's cross-section.
        Assert.Equal(0.0, p.UndrivenMetalM, 12);
    }

    [Fact]
    public void APortOnTheFarEdge_IsUnaffected_AndTheTwoPortsAreDifferentEdges()
    {
        var mesh = Notched();

        var wall = PlanarPorts.Resolve(mesh, new PlanarPort(1, new EmPoint(4e-3, 2.5e-3),
                                                            PlanarPortSide.MaxX, 50.0));
        var far  = PlanarPorts.Resolve(mesh, new PlanarPort(2, new EmPoint(10e-3, 8.5e-3),
                                                            PlanarPortSide.MaxX, 50.0));

        Assert.Equal(10e-3, far.OuterEdgeM, 12);
        Assert.Equal(4e-3, far.WidthM, 12);   // the full-width rows, y 6..10 mm

        // The whole report in one line: they are not the same edge.
        Assert.NotEqual(wall.ReferencePlaneM, far.ReferencePlaneM);
        Assert.Empty(wall.BasisIndices.Intersect(far.BasisIndices));
    }

    [Fact]
    public void ALabelJUSTOffTheFaceItNames_StillLandsOnThatFace()
    {
        // The documented allowance — "its label may legitimately sit just off the end face it names"
        // — survives the change: nearest-run is what the old march-from-the-boundary did for a label
        // beyond the metal, and it is what this does.
        var mesh = Notched();
        var p = PlanarPorts.Resolve(mesh, new PlanarPort(1, new EmPoint(4.4e-3, 2.5e-3),
                                                         PlanarPortSide.MaxX, 50.0));
        Assert.Equal(4e-3, p.OuterEdgeM, 12);
        Assert.Equal(6e-3, p.WidthM, 12);
    }

    [Fact]
    public void AUniformFeed_ResolvesExactlyAsItAlwaysDid()
    {
        // The guard on all of the above: one run per row, nothing outside the face, so neither the
        // run choice nor the end-face test can bite. Full width, outermost row.
        var gx = new double[7];
        var gy = new double[4];
        for (int i = 0; i < 7; i++) gx[i] = i * 1e-3;
        for (int i = 0; i < 4; i++) gy[i] = i * 0.5e-3;
        var mesh = PlanarFillTests.Grid(gx, gy);

        var p = PlanarPorts.Resolve(mesh, new PlanarPort(1, new EmPoint(0, 0.75e-3),
                                                         PlanarPortSide.MinX, 50.0));
        Assert.Equal(3, p.BasisCount);
        Assert.Equal(1.5e-3, p.WidthM, 12);
        Assert.Equal(0.0, p.OuterEdgeM, 12);
        Assert.Equal(1e-3, p.ReferencePlaneM, 12);
    }
}
