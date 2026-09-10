// RP-2a — THE MEASUREMENT R-rp2-3 ASKS FOR, MADE BEFORE ANY SOLVER WORK.
//
// A conductor-referenced port drives a delta gap between two pieces of METAL. A delta gap is defined
// across a SHARED CELL EDGE (PlanarExcitation's header: the reaction is v exactly because the basis
// is normalised to unit current across that edge). So the whole of RP-2 rests on one measurable
// question: on the ordinary coplanar cross-section — a signal strip and a ground strip separated by
// a slot — does this mesher produce a basis whose two cells lie on OPPOSITE sides of the slot?
//
// These tests do not assert a desired answer. They MEASURE, and the numbers are reported.

using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

public class CoplanarSlotMeshTests(ITestOutputHelper output)
{
    // A CPW cross-section on the FR-4 starter: centre strip W, slots S either side, wide grounds.
    private static PlanarProblem Cpw(double w, double s, double gw, double lengthM, double fHz)
        => PlanarLineFixtures.Problem(
            GroundedSlab.Fr4Starter, fHz,
            PlanarLineFixtures.Rect(0, -0.5 * w, lengthM, 0.5 * w),
            PlanarLineFixtures.Rect(0, 0.5 * w + s, lengthM, 0.5 * w + s + gw),
            PlanarLineFixtures.Rect(0, -(0.5 * w + s + gw), lengthM, -(0.5 * w + s)));

    /// <summary>Every basis whose two cells sit on opposite sides of one of the two slots.</summary>
    private static int BasesSpanningSlot(PlanarMesh mesh, double w, double s)
    {
        double sLo1 = 0.5 * w, sHi1 = 0.5 * w + s;
        double sLo2 = -(0.5 * w + s), sHi2 = -0.5 * w;
        int count = 0;
        foreach (var b in mesh.Bases)
        {
            var a = mesh.Cells[b.CellA];
            var c = mesh.Cells[b.CellB];
            // "Opposite sides of a slot": one cell entirely below the slot's low edge, the other
            // entirely above its high edge (in y), for either slot.
            if (Crosses(a.YMin, a.YMax, c.YMin, c.YMax, sLo1, sHi1) ||
                Crosses(a.YMin, a.YMax, c.YMin, c.YMax, sLo2, sHi2))
                count++;
        }
        return count;

        static bool Crosses(double aLo, double aHi, double bLo, double bHi, double lo, double hi)
            => (aHi <= lo + 1e-15 && bLo >= hi - 1e-15) || (bHi <= lo + 1e-15 && aLo >= hi - 1e-15);
    }

    /// <summary>Cells whose grid rectangle lies wholly inside a slot — i.e. metal-free columns.</summary>
    private static int CellsInsideSlot(PlanarMesh mesh, double w, double s)
    {
        double sLo1 = 0.5 * w, sHi1 = 0.5 * w + s;
        double sLo2 = -(0.5 * w + s), sHi2 = -0.5 * w;
        int n = 0;
        foreach (var c in mesh.Cells)
            if ((c.YMin >= sLo1 - 1e-15 && c.YMax <= sHi1 + 1e-15) ||
                (c.YMin >= sLo2 - 1e-15 && c.YMax <= sHi2 + 1e-15)) n++;
        return n;
    }

    /// <summary>How many grid ROWS fall strictly inside a slot — the geometric reason for the answer.</summary>
    private static int GridRowsInsideSlot(PlanarMesh mesh, double w, double s)
    {
        double sLo = 0.5 * w, sHi = 0.5 * w + s;
        int n = 0;
        for (int i = 0; i + 1 < mesh.GridY.Count; i++)
            if (mesh.GridY[i] >= sLo - 1e-15 && mesh.GridY[i + 1] <= sHi + 1e-15) n++;
        return n;
    }

    /// <summary>
    /// <b>R-rp2-3, measured — and the answer is NO, structurally rather than by omission.</b>
    ///
    /// <para>A CPW of realistic proportions, meshed at the shipping settings and at the coarse ones,
    /// across slot widths from generous down to well under the bulk cell size. No basis spans a slot
    /// at any of them, and the second assertion is the REASON: a polygon edge is a hard gridline
    /// (<c>CollectBoundaryLines</c>), so both edges of a slot are gridlines and a slot of nonzero
    /// width always contains at least one whole grid row. A cell exists only where the row centre is
    /// inside metal, so that row is empty, and the two conductors are never grid-adjacent. Refining
    /// the mesh adds rows to the slot; it never removes the last one.</para>
    ///
    /// <para><b>This test asserts the state of the world, not a wish.</b> If a later phase makes a
    /// port span a slot, it will do it by adding an excitation over two SEPARATE cuts — one in each
    /// conductor — and this test stays true. A test that starts failing here means a basis is
    /// shorting two conductors across a slot, which is a much worse thing than a missing feature.</para>
    /// </summary>
    [Theory]
    [InlineData(2.9e-3, 1.0e-3)]
    [InlineData(2.9e-3, 0.3e-3)]
    [InlineData(2.9e-3, 0.1e-3)]
    [InlineData(1.0e-3, 0.05e-3)]
    public void DoesAnyBasisSpanTheSlot(double w, double s)
    {
        const double fHz = 5e9, len = 10e-3, gw = 3e-3;
        foreach (var (name, settings) in new[]
                 {
                     ("coarse",   PlanarLineFixtures.Coarse),
                     ("shipping", PlanarLineFixtures.Shipping),
                 })
        {
            var rep = SurfaceMesher.Mesh(Cpw(w, s, gw, len, fHz), settings);
            var mesh = rep.Mesh;
            int span = BasesSpanningSlot(mesh, w, s);
            output.WriteLine(
                $"W={w * 1e3:0.###}mm S={s * 1e3:0.###}mm [{name}] verdict={rep.Verdict} " +
                $"cells={rep.CellCount} N={rep.UnknownCount} " +
                $"minEdge={rep.MinCellEdgeM * 1e6:0.##}um maxEdge={rep.MaxCellEdgeM * 1e6:0.##}um " +
                $"gridRowsInSlot={GridRowsInsideSlot(mesh, w, s)} " +
                $"cellsInsideSlot={CellsInsideSlot(mesh, w, s)} " +
                $"basesSpanningSlot={span}");

            Assert.Equal(0, span);
            // The structural reason, asserted so the zero above cannot be read as an accident of
            // this particular geometry: the slot always owns at least one metal-free grid row.
            Assert.True(GridRowsInsideSlot(mesh, w, s) >= 1);
            Assert.Equal(0, CellsInsideSlot(mesh, w, s));
        }
    }
}
