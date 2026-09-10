using CircuitRF.Render;
using CircuitRF.Ui.Theming;

namespace CircuitRF.Ui.Tests.Layout;

/// <summary>
/// <b>A port's reference plane is measured on the metal it is STANDING on, not on its conductor's
/// bounding box.</b> Owner report, 2026-09-09: a port dropped on the big rectangle at one end of a
/// top-copper polygon kept drawing itself over the trace at the other end.
///
/// <para><b>Nothing was snapping.</b> Every part of an edge port's plane came from the conductor's
/// BOUNDING BOX — <see cref="LayoutPortDirection.PlaneOf"/> returns a box edge, and
/// <c>SpanAt</c>'s default cut is taken at that box edge. For a straight run of metal the box IS the
/// conductor and both are exact, which is the case they were written for. A real imported polygon is
/// not one feature: the reported board's Top Copper is a SINGLE polygon carrying a blob at
/// x≈103.5 mm, a narrow 0.60 mm trace at x≈105.4, and a 1.80 mm rectangle from x≈106.0 to 109.63.
/// Its box spans all three and describes none of them. A port facing R90 anywhere on that polygon
/// had its plane placed at the BOX's bottom edge — a y that only the left blob reaches — so the
/// marker was drawn over the left blob no matter where on the metal the port was dropped. The one
/// port that always looked right (P1) is the one sitting exactly on the box's own MaxX.</para>
/// </summary>
public class LayoutPortOnMultiFeaturePolygonTests
{
    private static Technology Tech() => new()
    {
        Layers = [new LayerDef { Key = new LayerKey(1, 0), Name = "Top", Color = new Rgba(1, 2, 3), ZOrder = 0 }],
    };

    /// <summary>The reporting board's own shape, to scale and in DBU: ONE polygon whose left end is a
    /// tall blob, whose middle is a narrow trace, and whose right end is a big rectangle. The bounding
    /// box is 0..10,000 x 0..2,000 and matches no part of it.</summary>
    private static LayoutView ThreeFeaturePolygon()
    {
        var view = new LayoutView();
        view.Shapes.Add(new PolygonShape
        {
            Layer = new LayerKey(1, 0),
            Xy =
            [
                    0,    0,   1_000,    0,        // left blob, y 0..1_400
                1_000,  600,   6_000,  600,        // narrow trace, y 600..800
                6_000,    0,  10_000,    0,        // right rectangle, y 0..2_000
               10_000, 2_000,   6_000, 2_000,
                6_000,  800,   1_000,  800,
                1_000, 1_400,       0, 1_400,
            ],
        });
        return view;
    }

    private static LabelShape PortAt(long x, long y) => new()
    {
        Layer = new LayerKey(1, 0), X = x, Y = y, Text = "P", Height = 100,
        IsPort = true, PortLayer = new LayerKey(1, 0),
    };

    [Fact]
    public void APortOnTheRightRectangle_MeasuresTheRightRectangle_NotTheLeftBlob()
    {
        var view = ThreeFeaturePolygon();
        var lookup = LayoutPortDirection.LookupFor(view, Tech(), "");

        // Just inside the right rectangle's own bottom edge, 8,000 DBU away from the left blob.
        var hint = LayoutPortDirection.Resolve(lookup, PortAt(8_000, 100))!.Value;

        Assert.Equal(LayoutRotation.R90, hint.Direction);   // faces the edge it is nearest
        Assert.Equal(0, hint.PlaneY);                       // the rectangle's own bottom edge
        Assert.Equal(8_000, hint.PlaneX);                   // ...under the port, not at the box's midpoint
        Assert.Equal(4_000, hint.WidthDbu);                 // the rectangle's 6,000..10,000 run
    }

    [Fact]
    public void APortOnTheRightEndFace_MeasuresThatFace()
    {
        var view = ThreeFeaturePolygon();
        var hint = LayoutPortDirection.Resolve(
            LayoutPortDirection.LookupFor(view, Tech(), ""), PortAt(10_000, 1_000))!.Value;

        Assert.Equal(LayoutRotation.R180, hint.Direction);
        Assert.Equal(10_000, hint.PlaneX);
        Assert.Equal(2_000, hint.WidthDbu);   // the rectangle's full height, not the box's
    }

    [Fact]
    public void APortOnTheNarrowTrace_StandsOnTheTracesOwnEdge_NotTheBoxs()
    {
        var view = ThreeFeaturePolygon();
        var hint = LayoutPortDirection.Resolve(
            LayoutPortDirection.LookupFor(view, Tech(), ""), PortAt(3_000, 620))!.Value;

        Assert.Equal(LayoutRotation.R90, hint.Direction);

        // <b>y = 600, the TRACE's own lower edge — the box would have said y = 0</b>, which is 600
        // DBU below the metal this port is standing on and is reached only by the blob and the
        // rectangle at either end. That is the whole defect: the plane was placed on a face that
        // belongs to a different feature of the same polygon.
        Assert.Equal(600, hint.PlaneY);

        // <b>5,000 — the trace's own lower EDGE, x 1,000..6,000.</b> Three different answers were on
        // offer here and only one of them is the edge the port is standing on:
        //   the box's arithmetic gave 1,000 (the blob's run at y = 0, a piece of metal 2,000 DBU
        //     away, on a face 600 DBU below this one);
        //   a scanline just inside y = 600 gives 10,000, because the blob (y 0..1,400) and the
        //     rectangle (y 0..2,000) both span y = 600 and the metal there is CONTIGUOUS end to end;
        //   the boundary lying ON y = 600 is 1,000..6,000, and that is the trace's edge.
        Assert.Equal(5_000, hint.WidthDbu);
        Assert.Equal(3_500, hint.PlaneX);   // that edge's own centre
    }

    [Fact]
    public void TheArrowIsClampedByTheLOCALFeature_NotByTheWholePolygon()
    {
        // LengthDbu is what ArrowGeometry clamps the arrow with. Measured from the port's own face,
        // a port on the right rectangle's bottom edge has 2,000 DBU of metal ahead of it — not the
        // polygon's whole 2,000-tall box... and, facing R180 from the right end, 10,000 rather than
        // the box's own width used blindly.
        var view = ThreeFeaturePolygon();
        var lookup = LayoutPortDirection.LookupFor(view, Tech(), "");

        Assert.Equal(2_000, LayoutPortDirection.Resolve(lookup, PortAt(8_000, 100))!.Value.LengthDbu);
        Assert.Equal(10_000, LayoutPortDirection.Resolve(lookup, PortAt(10_000, 1_000))!.Value.LengthDbu);
    }

    [Fact]
    public void AStatedDirection_AlsoGetsTheLocalFace()
    {
        // A user who rotated the port has overruled the inference — but not the geometry. The plane
        // still belongs on the metal under the port.
        var view = ThreeFeaturePolygon();
        var port = PortAt(8_000, 1_000);
        port.PortDirection = LayoutRotation.R270;   // faces the top edge

        var hint = LayoutPortDirection.Resolve(LayoutPortDirection.LookupFor(view, Tech(), ""), port)!.Value;
        Assert.Equal(LayoutRotation.R270, hint.Direction);
        Assert.Equal(2_000, hint.PlaneY);     // the rectangle's own top edge
        Assert.Equal(4_000, hint.WidthDbu);
    }

    /// <summary>A NOTCH: metal continues past the face for part of its transverse extent. This is
    /// the shape a connector cutout makes, and it is where measuring the metal behind the face and
    /// measuring the FACE stop agreeing.</summary>
    private static LayoutView NotchedPolygon()
    {
        var view = new LayoutView();
        view.Shapes.Add(new PolygonShape
        {
            Layer = new LayerKey(1, 0),
            Xy =
            [
                    0,     0,   1_000,     0,      // the wall: x = 1,000, y 0..600
                1_000,   600,  10_000,   600,      // ...above which the metal carries on RIGHT
               10_000, 1_000,       0, 1_000,
            ],
        });
        return view;
    }

    [Fact]
    public void OnANotchWall_TheBarIsTheWALL_NotTheMetalBehindIt()
    {
        // A scanline one part-in-a-thousand inside x = 1,000 stays in metal from y = 0 all the way
        // to y = 1,000, because above the wall the conductor turns and keeps going. The WALL is
        // y 0..600. Measuring the metal reported 1,000 — 67% too long, and centred 200 DBU above the
        // port, overlapping artwork the port does not touch.
        var hint = LayoutPortDirection.Resolve(
            LayoutPortDirection.LookupFor(NotchedPolygon(), Tech(), ""), PortAt(1_000, 300))!.Value;

        Assert.Equal(LayoutRotation.R180, hint.Direction);
        Assert.Equal(1_000, hint.PlaneX);
        Assert.Equal(600, hint.WidthDbu);
        Assert.Equal(300, hint.PlaneY);   // the wall's centre — which is where the port was put
    }

    [Fact]
    public void TwoFingersEndingOnTheSameFace_AreTwoEdges_AndThePortGetsItsOwn()
    {
        // A scanline cannot separate these: it crosses both fingers and keeps the run nearest the
        // port. The boundary chains can, and do.
        var view = new LayoutView();
        view.Shapes.Add(new PolygonShape
        {
            Layer = new LayerKey(1, 0),
            Xy =
            [
                    0,     0,   1_000,     0,   1_000,   200,       0,   200,   // lower finger
                    0,   800,   1_000,   800,   1_000, 1_000,       0, 1_000,   // upper finger
            ],
        });

        var hint = LayoutPortDirection.Resolve(
            LayoutPortDirection.LookupFor(view, Tech(), ""), PortAt(1_000, 100))!.Value;
        Assert.Equal(200, hint.WidthDbu);   // the LOWER finger's face, not both plus the gap
        Assert.Equal(100, hint.PlaneY);
    }

    [Fact]
    public void AStraightRunOfMetal_IsUnchanged_TheBoxIsTheConductorThere()
    {
        // The case the bounding-box arithmetic was written for still answers identically.
        var view = new LayoutView();
        view.Shapes.Add(new RectShape { Layer = new LayerKey(1, 0), X1 = 0, Y1 = 0, X2 = 10_000, Y2 = 2_000 });
        var hint = LayoutPortDirection.Resolve(
            LayoutPortDirection.LookupFor(view, Tech(), ""), PortAt(0, 1_000))!.Value;

        Assert.Equal(LayoutRotation.R0, hint.Direction);
        Assert.Equal(0, hint.PlaneX);
        Assert.Equal(1_000, hint.PlaneY);
        Assert.Equal(2_000, hint.WidthDbu);
        Assert.Equal(10_000, hint.LengthDbu);
    }
}
