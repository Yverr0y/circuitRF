// Owner report, 2026-08-09: "When dragging the port, the geometry snap distance appears to be
// asymmetric. In the direction of the arrow pointer it will snap farther than the opposite direction.
// Make them both the same (and the farther distance is working good right now for UX)."
//
// A label's hit box is built from its text, and text runs ONE WAY from a baseline-left origin — so the
// anchor sat on the box's own corner. Measured on a 2-character port of height H, the box reached
// 1.24·H in the text direction (+x at R0, which is also where the arrow points) and ZERO the other
// way. The port could be grabbed from a long way ahead of it and only within the click tolerance
// behind it.
//
// That asymmetry is CORRECT for an annotation and wrong for a port: a port is a marker, and what the
// user aims at is the plane bar and arrow drawn about the conductor end, not the text.
//
// ── SUPERSEDED IN PART, 2026-08-25 ──────────────────────────────────────────────────────────────
//
// "The farther distance is working good right now for UX" was a parenthetical about which of the two
// former reaches to keep, and it was implemented as `half = Max(w, h)` — but w and h are FULL
// extents, so making the region symmetric also DOUBLED it in every direction (four times the area).
// The owner reported the consequence: "the hitbox for the ports now seems too big — I am always
// selecting ports almost everywhere I click in the layout." On three two-character labels at a
// 1.016 mm height that is a 2.52 mm square each, over a 3.5 x 2.2 mm structure.
//
// The reach is now HALF the larger extent, so the square circumscribes the glyph rather than using
// the glyph as its radius. **SYMMETRY — which is what the 2026-08-09 report actually asked for — is
// unchanged and is still asserted below**; only the absolute size moved, and the click tolerance the
// caller adds on top is unchanged.

using Avalonia.Input;
using CircuitRF.Ui.Layout;

namespace CircuitRF.Ui.Tests.Layout;

public class LayoutPortPickSymmetryTests
{
    private const int Dbu = LayoutUnits.DefaultDbuPerMicron;
    private static readonly LayerKey TopCopper = new(1, 0);

    private const long H = 1_000 * Dbu;          // 1 mm text
    private const long Anchor = 6_000 * Dbu;     // well inside the conductor, clear of its own corners

    private static LayoutView Fixture(LayoutRotation rot, out LayoutView view)
    {
        view = new LayoutView { DbuPerMicron = Dbu, SnapDbu = 0 };
        view.Shapes.Add(new RectShape { Layer = TopCopper, X1 = 0, Y1 = 0, X2 = 20_000 * Dbu, Y2 = 2_900 * Dbu });
        view.Shapes.Add(new LabelShape
        {
            Layer = TopCopper, X = Anchor, Y = 1_450 * Dbu, Text = "P1", Height = H,
            IsPort = true, Rotation = rot, PortDirection = LayoutRotation.R0,
        });
        return view;
    }

    /// <summary>The largest offset from the anchor, along <paramref name="ux"/>/<paramref name="uy"/>,
    /// at which the port (shape index 1) is still the topmost hit. Bisected rather than stepped, so
    /// the answer is exact to a DBU regardless of how large the reach turns out to be.</summary>
    private static long PickReach(LayoutView view, int ux, int uy)
    {
        long lo = 0, hi = 8 * H;
        while (lo < hi)
        {
            long mid = lo + (hi - lo + 1) / 2;
            var hits = LayoutHitTest.HitStack(view, null, Anchor + ux * mid, 1_450 * Dbu + uy * mid, 0);
            if (hits.Count > 0 && hits[0] == 1) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    [Fact]
    public void APortsPickRegionIsSymmetric_AtHalfTheLargerExtent()
    {
        Fixture(LayoutRotation.R0, out var view);

        long plusX  = PickReach(view, +1, 0);
        long minusX = PickReach(view, -1, 0);
        long plusY  = PickReach(view, 0, +1);
        long minusY = PickReach(view, 0, -1);

        Assert.Equal(plusX, minusX);
        Assert.Equal(plusX, plusY);
        Assert.Equal(plusX, minusY);

        // The symmetric reach is HALF the larger of the two extents — the text extent is
        // 2 chars x H x 0.62 = 1.24 H, so the reach is 0.62 H and the square is 1.24 H across, i.e.
        // exactly the glyph's own footprint. See this file's header for why this is half what it was.
        Assert.Equal((long)Math.Round(2 * H * 0.62) / 2, plusX);
    }

    [Theory]
    [InlineData(LayoutRotation.R90)]
    [InlineData(LayoutRotation.R180)]
    [InlineData(LayoutRotation.R270)]
    public void SymmetryHoldsAtEveryTextRotation(LayoutRotation rot)
    {
        // The old box's asymmetry pointed a different way per rotation, so a fix that only handled R0
        // would leave three quarters of the bug in place.
        Fixture(rot, out var view);

        long plusX  = PickReach(view, +1, 0);
        long minusX = PickReach(view, -1, 0);
        long plusY  = PickReach(view, 0, +1);
        long minusY = PickReach(view, 0, -1);

        Assert.Equal(plusX, minusX);
        Assert.Equal(plusX, plusY);
        Assert.Equal(plusX, minusY);
    }

    [Fact]
    public void AnOrdinaryLabelKeepsItsTextShapedBox_BecauseTextGenuinelyRunsOneWay()
    {
        // The non-vacuity control, and the scope fence: this change is about PORTS. An annotation's
        // one-sided box is right — its glyphs really are all on one side of the baseline origin — and
        // making every label symmetric would grow every annotation's pick region for no reason.
        var view = new LayoutView { DbuPerMicron = Dbu, SnapDbu = 0 };
        view.Shapes.Add(new RectShape { Layer = TopCopper, X1 = 0, Y1 = 0, X2 = 20_000 * Dbu, Y2 = 2_900 * Dbu });
        view.Shapes.Add(new LabelShape
        {
            Layer = TopCopper, X = Anchor, Y = 1_450 * Dbu, Text = "P1", Height = H, IsPort = false,
        });

        Assert.True(PickReach(view, +1, 0) > PickReach(view, -1, 0),
            "an ordinary label's box must still follow its text");
    }

    /// <summary>
    /// <b>SUPERSEDED AGAIN, 2026-08-25.</b> A port's pick region is now the MARK it draws (owner:
    /// "make the hitbox/highlight the arrow boundary box for edge and internal ports and make it the
    /// gap boundary rendering for the gap port"), and an EDGE port's mark is at the conductor END —
    /// so the region is not about the anchor at all, and cannot be symmetric about it.
    ///
    /// <para>The reaches asserted equal by the tests above are the FALLBACK square, which is what a
    /// caller with no conductor lookup still gets and is still symmetric. What the 2026-08-09 report
    /// was really about — a port being grabbable from behind rather than only from in front — is now
    /// answered differently: you grab a port at its arrow, from any side of it.</para>
    ///
    /// <para><b>NARROWED, 2026-09-09 — it is an EDGE port you grab at its arrow.</b> The fixture
    /// above puts its label at x = 6 mm, "well inside the conductor, clear of its own corners", and
    /// went on to assert that pressing there did NOT select it while pressing 14 mm away at the
    /// metal's end did. That is the owner's report — "port placement always snaps to edge of geometry
    /// … SAME DAMN BUG WITH DRAGGING TOO" — written down as an expectation: a port standing in the
    /// middle of a rectangle was drawn, highlighted and picked at an edge it was nowhere near, so
    /// dragging it appeared to do nothing. A port with no EM setup claiming it is now drawn where the
    /// evidence puts it (<c>LayoutPortDirection.PortHint.Interior</c>), and picked there too. The
    /// original claim is kept below for the port it was always about — one at a conductor end.</para>
    /// </summary>
    [Fact]
    public void AnEdgePortIsGrabbedAtItsArrow_FromEitherSideOfIt()
    {
        // R0 means current flows +x, so the port names the LOW-x end and its plane is at x = 0 — and
        // the label is put there too, which is what makes it an edge port rather than a mark stranded
        // mid-conductor.
        foreach (long dx in new long[] { -H / 8, 0, H / 8 })
        {
            EndFixture(out var v);
            var vm = new LayoutEditorViewModel(v) { ActiveTool = LayoutEditorViewModel.Tool.Select };
            vm.ApplyTechResolution(new TechResolution(
                StarterTechnologies.Pcb2Layer(), null, TechResolutionSource.WorkspaceDefault, []));

            vm.OnPointerPressed(dx, 1_450 * Dbu, KeyModifiers.None, 1, 0);
            Assert.Contains(1, vm.SelectedIndices);
        }
    }

    /// <summary>…and not from the far end of the metal it stands on, which is the other half of the
    /// same claim: an edge port's region is at ITS end, not at every end.</summary>
    [Fact]
    public void AnEdgePortIsNotGrabbedAtTheFarEndOfItsConductor()
    {
        EndFixture(out var view);
        var vm = new LayoutEditorViewModel(view) { ActiveTool = LayoutEditorViewModel.Tool.Select };
        vm.ApplyTechResolution(new TechResolution(
            StarterTechnologies.Pcb2Layer(), null, TechResolutionSource.WorkspaceDefault, []));

        vm.OnPointerPressed(20_000 * Dbu, 1_450 * Dbu, KeyModifiers.None, 1, 0);

        Assert.DoesNotContain(1, vm.SelectedIndices);
    }

    /// <summary>
    /// <b>The owner's report, as a test: a port in the middle of a piece of metal is grabbed
    /// THERE.</b>
    ///
    /// <para>Not at the conductor's end, which is where its mark used to be drawn and therefore where
    /// its pick region used to sit — 14 mm from the thing the user was aiming at. Pressing on the port
    /// selected the RECTANGLE instead, so the drag moved the artwork and left the port behind: on
    /// screen, a port welded to the edge.</para>
    /// </summary>
    [Fact]
    public void APortInTheMiddleOfMetalIsGrabbedWhereItIs_NotAtTheConductorEnd()
    {
        Fixture(LayoutRotation.R0, out var view);
        var vm = new LayoutEditorViewModel(view) { ActiveTool = LayoutEditorViewModel.Tool.Select };
        vm.ApplyTechResolution(new TechResolution(
            StarterTechnologies.Pcb2Layer(), null, TechResolutionSource.WorkspaceDefault, []));

        vm.OnPointerPressed(Anchor, 1_450 * Dbu, KeyModifiers.None, 1, 0);
        Assert.Contains(1, vm.SelectedIndices);

        // …and NOT at the end it used to be dragged to, which is bare metal as far as this port is
        // concerned. Shape 0 is the rectangle: pressing there is a press on the rectangle.
        var far = new LayoutEditorViewModel(view) { ActiveTool = LayoutEditorViewModel.Tool.Select };
        far.ApplyTechResolution(new TechResolution(
            StarterTechnologies.Pcb2Layer(), null, TechResolutionSource.WorkspaceDefault, []));
        far.OnPointerPressed(0, 1_450 * Dbu, KeyModifiers.None, 1, 0);
        Assert.DoesNotContain(1, far.SelectedIndices);
    }

    /// <summary>The same trace, with the port's label ON the low-x end it names — an ordinary edge
    /// port, which is what the two tests above are about.</summary>
    private static void EndFixture(out LayoutView view)
    {
        view = new LayoutView { DbuPerMicron = Dbu, SnapDbu = 0 };
        view.Shapes.Add(new RectShape { Layer = TopCopper, X1 = 0, Y1 = 0, X2 = 20_000 * Dbu, Y2 = 2_900 * Dbu });
        view.Shapes.Add(new LabelShape
        {
            Layer = TopCopper, X = 0, Y = 1_450 * Dbu, Text = "P1", Height = H,
            IsPort = true, PortDirection = LayoutRotation.R0,
        });
    }
}
