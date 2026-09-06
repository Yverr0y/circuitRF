// Which edge of a generated symbol each pin sits on.

namespace CircuitRF.Design.Symbol;

/// <summary>
/// The one rule circuitRF's own symbol GENERATORS use to decide which way a pin's name runs.
///
/// <para>A symbol read from a component library states this — the file either carries the
/// justification outright or fixes it through the pin's rotation. A symbol circuitRF draws itself has
/// no such statement, so it is derived from geometry, and it has to be derived the SAME way
/// everywhere: a cell's auto-generated box, an imported part whose file supplied no artwork, and a
/// generated block glyph all put their names inward, and a second copy of the rule would eventually
/// put one of them outward.</para>
/// </summary>
public static class SymbolPinSides
{
    /// <summary>
    /// The edge <paramref name="x"/>,<paramref name="y"/> sits on, relative to the centre of the pin
    /// cloud it belongs to.
    ///
    /// <para><b>The dominant axis wins</b>, so a pin at a corner is assigned to the edge it is
    /// furthest along rather than to both. A pin AT the centre — a one-pin symbol, or a pin dropped on
    /// the origin — is <see cref="SymbolPinNameAlign.Center"/>: there is no side for it to be on, and
    /// picking one arbitrarily would be a guess that reads as a bug the first time it is wrong.</para>
    /// </summary>
    public static SymbolPinNameAlign SideOf(double x, double y, double centerX, double centerY)
    {
        double dx = x - centerX, dy = y - centerY;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return SymbolPinNameAlign.Center;

        // Y is DOWN in local coordinates, so the pin with the larger y is the one on the BOTTOM edge.
        return Math.Abs(dx) >= Math.Abs(dy)
            ? (dx < 0 ? SymbolPinNameAlign.Left : SymbolPinNameAlign.Right)
            : (dy < 0 ? SymbolPinNameAlign.Top  : SymbolPinNameAlign.Bottom);
    }

    /// <summary>
    /// <see cref="SideOf(double,double,double,double)"/> against the centre of <paramref name="pins"/>'
    /// own bounding box — the centre of the artwork, not the mean, so one crowded edge does not drag
    /// the reference point onto itself and flip the sparse edge's names outward.
    /// </summary>
    public static SymbolPinNameAlign SideOf(IReadOnlyList<SymbolPin> pins, int index)
    {
        if (pins.Count == 0) return SymbolPinNameAlign.Center;
        double minX = pins.Min(p => p.LocalX), maxX = pins.Max(p => p.LocalX);
        double minY = pins.Min(p => p.LocalY), maxY = pins.Max(p => p.LocalY);
        return SideOf(pins[index].LocalX, pins[index].LocalY, (minX + maxX) / 2, (minY + maxY) / 2);
    }

    /// <summary>Assigns every pin in <paramref name="pins"/> its side, in place.</summary>
    public static void Assign(IReadOnlyList<SymbolPin> pins)
    {
        if (pins.Count == 0) return;
        double minX = pins.Min(p => p.LocalX), maxX = pins.Max(p => p.LocalX);
        double minY = pins.Min(p => p.LocalY), maxY = pins.Max(p => p.LocalY);
        double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        foreach (var pin in pins) pin.NameAlign = SideOf(pin.LocalX, pin.LocalY, cx, cy);
    }
}
