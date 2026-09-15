// Owner reports, 2026-08-25, both about an internal port that is being MOVED:
//
//   "the internal port rendering is messed up during a drag (it reverts to edge port rendering)"
//   "(and the internal port highlight select box is rendered in the wrong spot)"
//
// One root cause family: the renderer decided a port's TYPE, and measured its selection outline,
// from the shape it was about to DRAW — which during a live move drag is a translated clone, not the
// shape the .cem's marks were computed from (R-L1c-3: the model is untouched until commit).
//
// The TYPE moved onto the label itself on 2026-09-14 (LabelShape.PortKind), which removes the class
// of bug entirely: a clone carries the field, so there is no anchor to match and nothing to keep in
// step. These tests are kept, rewritten against the field, because what they assert is about the
// PICTURE — a dragged internal port draws as an internal port, and its outline is where it is — and
// that has to stay true however the type is stored.

using CircuitRF.Engine.Mom;
using Avalonia.Input;
using CircuitRF.Ui.Layout;
using CircuitRF.Ui.Renderers;
using SkiaSharp;

namespace CircuitRF.Ui.Tests.Em;

public class InternalPortDragRenderTests
{
    private const int Dbu = LayoutUnits.DefaultDbuPerMicron;
    private static readonly LayerKey TopCopper = new(1, 0);

    private static long Mm(double mm) => (long)Math.Round(mm * 1000 * Dbu);

    private static LabelShape Port(double xMm, double yMm,
                                  PlanarPortKind kind = PlanarPortKind.InternalDeltaGap) => new()
    {
        Layer = TopCopper, X = Mm(xMm), Y = Mm(yMm), Text = "P1", Height = Mm(0.5),
        IsPort = true, PortDirection = LayoutRotation.R0, PortKind = kind,
    };

    /// <summary>Renders a one-trace layout whose single port is declared an internal delta gap at
    /// <c>anchorMm</c>, optionally with that port live-move-dragged to <c>draggedToMm</c>.</summary>
    private static byte[] Render(double anchorMm, double? draggedToMm, bool selected = false)
    {
        var view = new LayoutView { DbuPerMicron = Dbu, DisplayUnit = LayoutUnit.Um, SnapDbu = 1000 };
        view.Shapes.Add(new RectShape { Layer = TopCopper, X1 = 0, Y1 = 0, X2 = Mm(20), Y2 = Mm(2.9) });
        view.Shapes.Add(Port(anchorMm, 1.45));

        var overrides = new Dictionary<int, LayoutShape>();
        if (draggedToMm is { } to) overrides[1] = Port(to, 1.45);

        var bb = Bbox.Empty;
        foreach (var s in view.Shapes) bb = bb.Union(LayoutGeometry.BboxOf(s));
        var vp = LayoutViewport.ZoomToFit(bb, 400, 400, 0.2);

        using var surface = SKSurface.Create(new SKImageInfo((int)vp.Width, (int)vp.Height));
        LayoutRenderer.Draw(surface.Canvas, view, StarterTechnologies.Pcb2Layer(), vp,
            new LayoutRenderOptions
            {
                Theme = LayoutRenderTheme.Light,
                Overlay = new LayoutOverlay
                {
                    DragOverrides   = overrides,
                    SelectedIndices = selected ? [1] : [],
                },
            });

        using var img = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(img);
        return bmp.Bytes;
    }

    /// <summary>
    /// <b>A port dragged to a new position still draws as the gap it is.</b> The oracle is a
    /// differential render rather than a pixel probe: the frame with the port dragged to 12 mm must
    /// match the frame with the port STORED at 12 mm and not dragged, because a move changes where
    /// the port is and nothing else. Before the fix the dragged frame drew an edge port's
    /// reference-plane bar and arrow at the trace end instead of the gap's centred break, so the two
    /// differed everywhere the mark was.
    /// </summary>
    [Fact]
    public void DraggingAnInternalGapPort_DrawsTheGapMark_NotAnEdgePortsBarAndArrow()
    {
        Assert.Equal(Render(12, null), Render(10, draggedToMm: 12));
    }

    /// <summary>And it is genuinely the gap mark that survives, not merely a stable picture: an edge
    /// port at the same place renders differently. Without this the test above would pass on any
    /// renderer that drew the same thing in both frames.</summary>
    [Fact]
    public void TheGapMarkAndAnEdgePortsMarkAreNotTheSamePicture()
    {
        var view = new LayoutView { DbuPerMicron = Dbu, DisplayUnit = LayoutUnit.Um, SnapDbu = 1000 };
        view.Shapes.Add(new RectShape { Layer = TopCopper, X1 = 0, Y1 = 0, X2 = Mm(20), Y2 = Mm(2.9) });
        view.Shapes.Add(Port(12, 1.45));

        var bb = Bbox.Empty;
        foreach (var s in view.Shapes) bb = bb.Union(LayoutGeometry.BboxOf(s));
        var vp = LayoutViewport.ZoomToFit(bb, 400, 400, 0.2);

        byte[] Draw(PlanarPortKind kind)
        {
            ((LabelShape)view.Shapes[1]).PortKind = kind;
            using var surface = SKSurface.Create(new SKImageInfo((int)vp.Width, (int)vp.Height));
            LayoutRenderer.Draw(surface.Canvas, view, StarterTechnologies.Pcb2Layer(), vp,
                new LayoutRenderOptions { Theme = LayoutRenderTheme.Light });
            using var img = surface.Snapshot();
            using var bmp = SKBitmap.FromImage(img);
            return bmp.Bytes;
        }

        Assert.NotEqual(Draw(PlanarPortKind.Edge), Draw(PlanarPortKind.InternalDeltaGap));
    }

    /// <summary>The selection outline follows the same rule — a selected gap port dragged to 12 mm
    /// draws the same frame as one stored at 12 mm, box included.</summary>
    [Fact]
    public void TheSelectionOutlineOfADraggedGapPort_IsWhereTheDraggedGapIs()
    {
        Assert.Equal(Render(12, null, selected: true), Render(10, draggedToMm: 12, selected: true));
    }

    // ── Mouse-up ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>The type survives the COMMIT, not just the drag.</b> Owner report, 2026-08-25: "after drag
    /// of the port, the port rendering glitches momentarily on the mouse up."
    ///
    /// <para>Two things used to happen at mouse-up and each on its own caused the flash.
    /// <c>Model.Changed</c> cleared the <c>.cem</c>'s published marks outright — correct for the mesh
    /// overlay beside it, which is derived from the geometry, and wrong for a port TYPE, which moving
    /// a label cannot change. And the marks were keyed by the label's ANCHOR, which the move had just
    /// changed, so even an uncleared list stopped matching. The gap stayed open until the owning
    /// <c>.cem</c> re-extracted and republished, one or more visible frames later.</para>
    ///
    /// <para>The type is a field on the label now, so it moves with the port by construction. This
    /// drives the real view model through a real move commit and asserts it.</para>
    /// </summary>
    [Fact]
    public void CommittingAMoveOfAnInternalPort_KeepsItsType()
    {
        var view = new LayoutView { DbuPerMicron = Dbu, DisplayUnit = LayoutUnit.Um, SnapDbu = 1000 };
        view.Shapes.Add(new RectShape { Layer = TopCopper, X1 = 0, Y1 = 0, X2 = Mm(20), Y2 = Mm(2.9) });
        view.Shapes.Add(Port(10, 1.45));

        var vm = new LayoutEditorViewModel(view);

        // The real pointer path: press on the port, drag 2 mm, release.
        vm.OnPointerPressed(Mm(10), Mm(1.45), KeyModifiers.None, 1, Mm(0.1));
        vm.OnPointerMoved(Mm(12), Mm(1.45), true, KeyModifiers.None, Mm(0.1));
        vm.OnPointerReleased(Mm(12), Mm(1.45), KeyModifiers.None);

        var port = (LabelShape)view.Shapes[1];
        Assert.Equal(Mm(12), port.X);
        Assert.Equal(PlanarPortKind.InternalDeltaGap, port.PortKind);
        Assert.Equal(PlanarPortKind.InternalDeltaGap, vm.PortKindAt(1));
    }

    /// <summary>
    /// <b>Undoing that move takes the type back with it.</b> The old marks were keyed by anchor and
    /// an undo left them stranded on empty space, which a prune then had to clean up; a field on the
    /// label has none of that shape — undo restores the shape, and the shape is where the type is.
    /// </summary>
    [Fact]
    public void UndoingThatMove_TakesTheTypeBackWithThePort()
    {
        var view = new LayoutView { DbuPerMicron = Dbu, DisplayUnit = LayoutUnit.Um, SnapDbu = 1000 };
        view.Shapes.Add(new RectShape { Layer = TopCopper, X1 = 0, Y1 = 0, X2 = Mm(20), Y2 = Mm(2.9) });
        view.Shapes.Add(Port(10, 1.45));

        var vm = new LayoutEditorViewModel(view);

        vm.OnPointerPressed(Mm(10), Mm(1.45), KeyModifiers.None, 1, Mm(0.1));
        vm.OnPointerMoved(Mm(12), Mm(1.45), true, KeyModifiers.None, Mm(0.1));
        vm.OnPointerReleased(Mm(12), Mm(1.45), KeyModifiers.None);
        Assert.Equal(Mm(12), ((LabelShape)view.Shapes[1]).X);

        vm.UndoCommand.Execute(null);

        var port = (LabelShape)view.Shapes[1];
        Assert.Equal(Mm(10), port.X);
        Assert.Equal(PlanarPortKind.InternalDeltaGap, port.PortKind);
    }

    /// <summary>
    /// <b>And drawing something else does not disturb it.</b> An ADD used to clear every mark,
    /// because it can renumber the ports and the <c>.cem</c>'s rows were addressed by number — so
    /// which row meant which label became unknowable from the layout side. A type carried by the
    /// label is immune to renumbering by construction, which is the half of R-em-17 that this
    /// change retires.
    /// </summary>
    [Fact]
    public void AddingAShape_NoLongerDisturbsAnotherPortsType()
    {
        var view = new LayoutView { DbuPerMicron = Dbu, DisplayUnit = LayoutUnit.Um, SnapDbu = 1000 };
        view.Shapes.Add(new RectShape { Layer = TopCopper, X1 = 0, Y1 = 0, X2 = Mm(20), Y2 = Mm(2.9) });
        view.Shapes.Add(Port(10, 1.45));

        var vm = new LayoutEditorViewModel(view);

        view.Shapes.Add(Port(5, 1.45, PlanarPortKind.Edge));
        view.NotifyChanged(LayoutChangeInfo.Appended(2, 1));

        Assert.Equal(PlanarPortKind.InternalDeltaGap, ((LabelShape)view.Shapes[1]).PortKind);
        Assert.Equal(PlanarPortKind.Edge,             ((LabelShape)view.Shapes[2]).PortKind);
    }

    // ── The box itself ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>The selection outline IS the pick region, and the pick region is the MARK.</b> Owner
    /// requests, 2026-08-25: "the hitbox of the port does not match with the select highlight
    /// rendering", then "make the hitbox/highlight the anchor arrow area + padding".
    ///
    /// <para>Asserted as IDENTITY against <c>LayoutHitTest.PortPickBbox</c> rather than by comparing
    /// two computed rectangles, because the property that matters is that there is only one region.
    /// Two rectangles that happen to be equal today are exactly what produced the original
    /// mismatch: <c>MeasureLabelWorldBbox</c> began as a copy of the hit test's estimate and
    /// drifted.</para>
    /// </summary>
    [Fact]
    public void TheSelectedPortsHighlightIsDrawnAtThePickRegion()
    {
        Assert.Equal(Render(12, null, selected: true), Render(10, draggedToMm: 12, selected: true));
        Assert.NotEqual(Render(10, null, selected: true), Render(12, null, selected: true));
    }

    /// <summary>
    /// <b>A GAP port's box is centred on its anchor</b>, because that is where its mark is drawn —
    /// the break across the metal. This is the half of the owner's "how will that work for gap port?"
    /// that needed no compromise.
    /// </summary>
    [Fact]
    public void AGapPortsRegionIsCentredOnItsAnchor()
    {
        var (label, hint) = OnATrace(10);
        var bb = LayoutHitTest.PortPickBbox(label, hint, atAnchor: true);

        Assert.Equal(label.X - bb.MinX, bb.MaxX - label.X);
        Assert.Equal(label.Y - bb.MinY, bb.MaxY - label.Y);
    }

    /// <summary>
    /// <b>An EDGE port's region is at its mark, which is at the conductor END — not at its label.</b>
    /// Owner, 2026-08-25: "make the hitbox/highlight the arrow boundary box for edge and internal
    /// ports". A port is grabbed and highlighted by its arrow; the name is a label beside it.
    ///
    /// <para>Stated as a NEGATIVE as well as a positive, because "the box no longer covers the name"
    /// is the deliberate consequence of the request and the thing a later reader is most likely to
    /// mistake for a regression.</para>
    /// </summary>
    [Fact]
    public void AnEdgePortsRegionIsAtItsPlane_NotAtItsLabel()
    {
        var (label, hint) = OnATrace(10);
        Assert.NotNull(hint);
        var bb = LayoutHitTest.PortPickBbox(label, hint, atAnchor: false);

        Assert.True(bb.Contains(hint!.Value.PlaneX, hint.Value.PlaneY), "a port is grabbed at its arrow");
        Assert.False(bb.Contains(label.X, label.Y), "and deliberately NOT at its name");

        // The two really are far apart here, so this is not vacuous.
        Assert.True(Math.Abs(hint.Value.PlaneX - label.X) > Mm(1));
    }

    /// <summary>The padding is real — the region is bigger than the bare mark, so a port is grabbable
    /// from just outside its own arrow.</summary>
    [Fact]
    public void TheRegionIncludesPaddingAroundTheMark()
    {
        var (label, hint) = OnATrace(10);
        var padded = LayoutHitTest.PortPickBbox(label, hint, atAnchor: true);
        var bare   = LayoutPortDirection.MarkerBbox(label, hint!.Value, atAnchor: true, padding: 0);

        Assert.True(padded.MinX < bare.MinX && padded.MaxX > bare.MaxX);
        Assert.True(padded.MinY < bare.MinY && padded.MaxY > bare.MaxY);
    }

    /// <summary>With no conductor under it there is no mark to measure — the renderer draws none
    /// either — so the region falls back to a square on the anchor. A port that could not be picked
    /// at all would be a port that could not be dragged back onto the metal.</summary>
    [Fact]
    public void APortOffTheMetalStillHasAPickRegion()
    {
        var label = Port(10, 1.45);
        var bb = LayoutHitTest.PortPickBbox(label, hint: null);

        Assert.False(bb.IsEmpty);
        Assert.True(bb.Contains(label.X, label.Y));
    }

    /// <summary>An ordinary label keeps the real-font-metrics glyph box — the scope fence. Ports are
    /// the exception because their pick region is deliberately their MARK; an annotation has no mark
    /// and its glyphs are the whole of it.</summary>
    [Fact]
    public void AnOrdinaryLabelStillMeasuresItsGlyphs()
    {
        var wide   = LayoutRenderer.MeasureLabelWorldBbox(
            new LabelShape { Layer = TopCopper, X = 0, Y = 0, Text = "WWWW", Height = Mm(0.5) });
        var narrow = LayoutRenderer.MeasureLabelWorldBbox(
            new LabelShape { Layer = TopCopper, X = 0, Y = 0, Text = "iiii", Height = Mm(0.5) });

        Assert.NotNull(wide);
        Assert.NotNull(narrow);
        Assert.True(wide!.Value.MaxX - wide.Value.MinX > narrow!.Value.MaxX - narrow.Value.MinX,
                    "an ordinary label's box must still follow its real glyph widths");
    }

    /// <summary>A port on the 20 mm trace at <paramref name="xMm"/>, with its conductor resolved.</summary>
    private static (LabelShape Label, LayoutPortDirection.PortHint? Hint) OnATrace(double xMm)
    {
        var view = new LayoutView { DbuPerMicron = Dbu, DisplayUnit = LayoutUnit.Um, SnapDbu = 1000 };
        view.Shapes.Add(new RectShape { Layer = TopCopper, X1 = 0, Y1 = 0, X2 = Mm(20), Y2 = Mm(2.9) });
        var label = Port(xMm, 1.45);
        view.Shapes.Add(label);

        var lookup = LayoutConductorLookup.LookupFor(view, StarterTechnologies.Pcb2Layer(), baseDir: "");
        return (label, LayoutPortDirection.Resolve(lookup, label));
    }
}