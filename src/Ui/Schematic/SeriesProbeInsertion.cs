namespace CircuitRF.Ui.Schematic;

/// <summary>
/// The wire an <see cref="SymbolKind.IProbe"/> shorts out the moment it is placed, and the rule
/// for when circuitRF may clear it.
///
/// <para><b>Why this exists.</b> An IProbe is a 0 V series ammeter: it measures the current
/// through itself, so it is only useful placed IN a wire. The natural gesture is to drop it onto
/// the wire whose current is wanted — at which point both of its pins land on that wire and the
/// probe is shorted by the very run it was meant to break into, reading nothing. Every user then
/// performs the same second gesture: delete the stretch of wire between the two pins. This finds
/// that stretch so the placement can do it for them.</para>
///
/// <para><b>The result must be electrically identical to the hand edit</b>, and that is the whole
/// constraint on <see cref="FindShortedSpan"/>. The cut is allowed only when the two pins sit on
/// ONE straight segment of ONE wire and the open stretch between them carries nothing else — no
/// junction dot, no other wire touching or crossing it, no other component's pin, no net label
/// anchored inside it. Anything in there is a connection to a third thing, and removing the copper
/// under it would re-partition the net rather than break it in series. In that case this returns
/// null and the placement behaves exactly as it always did; the user still has the manual route.</para>
///
/// <para><b>Placement only.</b> The caller is <c>SchematicViewModel.CommitPlacement</c>, the single
/// commit path for the click-arm and drag-and-drop placements — so a later drag of a placed probe
/// reaches none of this, and a paste or a type-change does not either. There is deliberately no
/// "has this already run" flag on the component: the one entry point IS the once.</para>
///
/// <para>Framework-free (no Avalonia), like <see cref="WireGeometry"/> — headless-testable.</para>
/// </summary>
public static class SeriesProbeInsertion
{
    /// <summary>
    /// The stretch of wire to remove: which wire, which of its segments, and the two cut points
    /// in that segment's own order — <paramref name="First"/> is the one nearer the segment's
    /// start vertex, so the piece before the cut ends at First and the piece after starts at Second.
    /// </summary>
    public readonly record struct ShortedSpan(
        EditableWire         Wire,
        int                  SegmentIndex,
        (double X, double Y) First,
        (double X, double Y) Second);

    /// <summary>
    /// The span <paramref name="probe"/> would short out, or null when there is none or when
    /// removing it would change the circuit. <paramref name="probe"/> is the component about to be
    /// placed and is NOT expected to be in <paramref name="model"/> yet — which is also what keeps
    /// its own pins out of the "something else is in the way" scan.
    /// </summary>
    public static ShortedSpan? FindShortedSpan(SchematicEditModel model, EditableComponent probe)
    {
        const double tol = SchematicEditModel.ConnectTolerance;

        var defs = model.PortDefsOf(probe);
        if (defs.Count != 2) return null;
        var a = model.PortWorldOf(probe, defs[0]);
        var b = model.PortWorldOf(probe, defs[1]);
        if (SchematicGeometry.CoincidentPoints(a.X, a.Y, b.X, b.Y, tol)) return null;

        // Both pins on ONE straight segment. Two pins on two different segments means the run
        // between them turns a corner, which is not the shape this affordance is for; two pins on
        // two different wires means the short (if there is one) is not one wire's to give up.
        ShortedSpan? found = null;
        foreach (var w in model.Wires)
        {
            var pts = w.Points;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var (px, py) = pts[i];
                var (qx, qy) = pts[i + 1];
                if (!SchematicGeometry.PointOnSegment(a.X, a.Y, px, py, qx, qy, tol)) continue;
                if (!SchematicGeometry.PointOnSegment(b.X, b.Y, px, py, qx, qy, tol)) continue;

                // A second segment covering the same span means cutting one of them leaves the
                // other still shorting the probe. Ambiguous — leave the sheet alone.
                if (found is not null) return null;

                bool aFirst = Dist2(px, py, a.X, a.Y) <= Dist2(px, py, b.X, b.Y);
                found = new ShortedSpan(w, i, aFirst ? a : b, aFirst ? b : a);
            }
        }

        return found is { } span && SpanIsClear(model, span) ? span : null;
    }

    /// <summary>
    /// True when nothing but wire occupies the OPEN stretch between the two cut points. The two
    /// ends themselves are excluded on purpose: that is where the probe's own pins land, and a
    /// wire, pin or dot meeting the run exactly there stays connected to the probe afterwards.
    /// </summary>
    private static bool SpanIsClear(SchematicEditModel model, ShortedSpan span)
    {
        const double tol = SchematicEditModel.ConnectTolerance;
        var (ax, ay) = span.First;
        var (bx, by) = span.Second;

        bool Inside(double x, double y)
            => SchematicGeometry.PointOnSegmentInterior(x, y, ax, ay, bx, by, tol);

        // A user junction dot — by definition a 4-way crossing that is connected.
        foreach (var d in model.Dots)
            if (Inside(d.X, d.Y)) return false;

        // Another component's pin tapping the run mid-span.
        foreach (var c in model.Components)
            foreach (var def in model.PortDefsOf(c))
            {
                var (px, py) = model.PortWorldOf(c, def);
                if (Inside(px, py)) return false;
            }

        // A net label anchored inside the run: the name belongs to the copper about to go away.
        foreach (var l in model.NetLabels)
            if (Inside(l.X - l.OffsetX, l.Y - l.OffsetY)) return false;

        // Any OTHER wire meeting the run — a T-junction (its vertex lands on the run), a crossing
        // (its body passes through), or a collinear duplicate lying along it.
        foreach (var w in model.Wires)
        {
            if (ReferenceEquals(w, span.Wire)) continue;
            var pts = w.Points;
            if (pts.Count == 1 && Inside(pts[0].X, pts[0].Y)) return false;

            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p = pts[i];
                var q = pts[i + 1];
                if (Inside(p.X, p.Y) || Inside(q.X, q.Y)) return false;
                if (SchematicGeometry.SegmentsIntersectInterior(
                        ax, ay, bx, by, p.X, p.Y, q.X, q.Y, out _, out _)) return false;
                if (RunsAlongSpan(ax, ay, bx, by, p.X, p.Y, q.X, q.Y, tol)) return false;
            }
        }

        // The cut wire's own vertices cannot fall inside the span — both cut points lie on one
        // straight segment, whose interior has none by construction. Nothing to check.
        return true;
    }

    /// <summary>
    /// True when segment (p,q) lies along the same line as the span and overlaps more than a single
    /// point of it — a duplicate run of wire the cut would leave behind, still shorting the probe.
    /// Neither <see cref="SchematicGeometry.SegmentsIntersectInterior"/> (which rejects parallels)
    /// nor the vertex test above sees this one when the duplicate outreaches the span at both ends.
    /// </summary>
    private static bool RunsAlongSpan(
        double ax, double ay, double bx, double by,
        double px, double py, double qx, double qy, double tol)
    {
        if (Math.Abs(ay - by) <= tol && Math.Abs(py - qy) <= tol && Math.Abs(py - ay) <= tol)
            return Overlap(ax, bx, px, qx) > tol;
        if (Math.Abs(ax - bx) <= tol && Math.Abs(px - qx) <= tol && Math.Abs(px - ax) <= tol)
            return Overlap(ay, by, py, qy) > tol;
        return false;
    }

    private static double Overlap(double a1, double a2, double b1, double b2)
        => Math.Min(Math.Max(a1, a2), Math.Max(b1, b2)) - Math.Max(Math.Min(a1, a2), Math.Min(b1, b2));

    private static double Dist2(double ax, double ay, double bx, double by)
        => (ax - bx) * (ax - bx) + (ay - by) * (ay - by);
}
