// Owner report, 2026-09-09: "ports are being forced to the edge … both when I drag a port that was
// already placed and when I arm the port button and try to place a new port."
//
// The drag half was real and had nothing to do with the snap TOGGLE. R-snp-8's click-through grab
// consumes a press for the top snap candidate's OWNING shape, "even when the click itself misses that
// shape's own hit-test" — its headline behaviour. But a port label contributes no snap features of its
// own and is deliberately placed ON a conductor's features (an end face, a corner, an edge midpoint),
// because that is exactly what the Port tool snaps it to. So the marker under a press aimed at a port
// belongs to the METAL: the press was handed to the conductor, the drag moved the trace, and the port
// stayed where it was. On screen a port welded to the edge, springing back on every attempt.
//
// The press did not MISS anything, which is the case click-through exists for — it landed inside the
// port's own pick region. A port now outranks the grab, exactly as a ruler already did.

using Avalonia.Input;
using CircuitRF.Ui.Layout;

namespace CircuitRF.Ui.Tests.Layout;

public class LayoutPortDragNotTheConductorTests
{
    private const int Dbu = LayoutUnits.DefaultDbuPerMicron;
    private static readonly LayerKey TopCopper = new(1, 0);
    private static long Um(double um) => (long)Math.Round(um * Dbu);

    /// <summary>A 20 × 2.9 mm trace with a port at its low-x end face — the position the Port tool's
    /// own edge snap produces, and therefore the one every real port starts at.</summary>
    private static (LayoutEditorViewModel Vm, LayoutView View) Fixture(bool geometrySnap, double portY = 1_450)
    {
        var view = new LayoutView { DbuPerMicron = Dbu, DisplayUnit = LayoutUnit.Um, SnapDbu = 0 };
        view.Shapes.Add(new RectShape { Layer = TopCopper, X1 = 0, Y1 = 0, X2 = Um(20_000), Y2 = Um(2_900) });
        view.Shapes.Add(new LabelShape
        {
            Layer = TopCopper, X = 0, Y = Um(portY), Text = "P1", Height = Um(300),
            IsPort = true, PortDirection = LayoutRotation.R0,
        });
        return (new LayoutEditorViewModel(view) { GeometrySnapEnabled = geometrySnap }, view);
    }

    private static void Drag(LayoutEditorViewModel vm, long fromX, long fromY, long toX, long toY, long tol)
    {
        vm.OnPointerPressed(fromX, fromY, KeyModifiers.None, 1, tol, 0, tol);
        vm.OnPointerMoved((fromX + toX) / 2, (fromY + toY) / 2, leftDown: true, KeyModifiers.None, tol, 0, tol);
        vm.OnPointerMoved(toX, toY, leftDown: true, KeyModifiers.None, tol, 0, tol);
        vm.OnPointerReleased(toX, toY, KeyModifiers.None, tol);
    }

    /// <param name="geometrySnap">
    /// Both toggle states, because the report happened under both — and they land the port in
    /// different places, which is the owner's own rule of 2026-09-09: "when geometry snap is on, the
    /// port should be snapping to the edge for placement and for drags. when snap is off, then port
    /// can be placed anywhere". Snap ON therefore expects the low-y side; snap OFF expects the exact
    /// drop point. What is asserted identically for both — and is what this test is actually about —
    /// is that the thing that moved was the PORT.
    /// </param>
    [Theory]
    [InlineData(true,  0)]
    [InlineData(false, 400)]
    public void DraggingAPortMovesTHEPORT_NotTheConductorUnderIt(bool geometrySnap, double expectedYUm)
    {
        var (vm, view) = Fixture(geometrySnap);

        // From the end-face midpoint — a snap feature, which is the whole trap — to mid-span, well
        // clear of every feature so nothing can legitimately attract the drop.
        Drag(vm, 0, Um(1_450), Um(10_000), Um(400), tol: Um(250));

        var port = view.Shapes.OfType<LabelShape>().Single(l => l.IsPort);
        var rect = (RectShape)view.Shapes[0];

        Assert.Equal(Um(10_000), port.X);
        Assert.Equal(Um(expectedYUm), port.Y);

        // The other half of the report, and the part that made it look like the port was pinned: the
        // conductor must not have moved at all.
        Assert.Equal(0, rect.X1);
        Assert.Equal(0, rect.Y1);
    }

    [Fact]
    public void APortDroppedNearAConductorFeature_StillLandsExactlyOnIt()
    {
        // The non-vacuity guard: the fix must take the press away from the click-through grab WITHOUT
        // taking away the port's own geometry snap, which is the reason a port is dragged at all.
        // Dropped 40 × 30 µm off the high-x end face's midpoint, inside a 250 µm tolerance.
        var (vm, view) = Fixture(geometrySnap: true);

        Drag(vm, 0, Um(1_450), Um(20_000) - Um(40), Um(1_450) + Um(30), tol: Um(250));

        var port = view.Shapes.OfType<LabelShape>().Single(l => l.IsPort);
        Assert.Equal(Um(20_000), port.X);
        Assert.Equal(Um(1_450), port.Y);
    }

    [Fact]
    public void WithGeometrySnapOFF_ADroppedPortKeepsTheExactPointItWasDroppedOn()
    {
        // …and with the toggle off, the same drop near that feature is NOT attracted to it.
        var (vm, view) = Fixture(geometrySnap: false);

        Drag(vm, 0, Um(1_450), Um(20_000) - Um(40), Um(1_450) + Um(30), tol: Um(250));

        var port = view.Shapes.OfType<LabelShape>().Single(l => l.IsPort);
        Assert.Equal(Um(20_000) - Um(40), port.X);
        Assert.Equal(Um(1_450) + Um(30), port.Y);
    }

    [Fact]
    public void APressOnPLAINMetalStillReachesTheConductorThroughItsMarker()
    {
        // R-snp-8's headline behaviour is untouched where no port is involved: a press that misses the
        // rect's own hit-test but is inside the snap tolerance of its corner still grabs the rect.
        var view = new LayoutView { DbuPerMicron = Dbu, DisplayUnit = LayoutUnit.Um, SnapDbu = 0 };
        view.Shapes.Add(new RectShape { Layer = TopCopper, X1 = 0, Y1 = 0, X2 = Um(20_000), Y2 = Um(2_900) });
        var vm = new LayoutEditorViewModel(view) { GeometrySnapEnabled = true };

        // Outside the metal, 100 µm past the low-x/low-y corner, within a 250 µm tolerance of it.
        Drag(vm, -Um(100), -Um(100), Um(5_000), Um(5_000), tol: Um(250));

        var rect = (RectShape)view.Shapes[0];
        Assert.NotEqual(0, rect.X1);
    }
}
