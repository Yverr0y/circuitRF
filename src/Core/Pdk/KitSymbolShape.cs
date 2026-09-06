namespace CircuitRF.Core.Pdk;

/// <summary>
/// One drawn element of a kit's symbol, in the FILE's own coordinates and units.
///
/// <para><b>Why a neutral shape vocabulary rather than circuitRF's own primitives.</b> The reader
/// lives in the core and must stay framework-free; <c>SymbolPrimitive</c> lives in the UI project
/// alongside the renderer. So the reader reports what the file DREW, and the UI turns that into
/// primitives — the same split <see cref="KitSymbolPin"/> already follows for terminals.</para>
///
/// <para>Coordinates are exactly as the file states them: no scale is applied here, because the
/// scale is chosen from the whole drawing at once and that is the consumer's job.</para>
/// </summary>
public abstract record KitSymbolShape
{
    /// <summary>
    /// The stroke width the file states for this element, in the same unit as this shape's
    /// COORDINATES, or 0 when the file states none (several formats spell "the default" as a zero).
    ///
    /// <para><b>Why the unit is not pinned down here.</b> circuitRF's symbol model has three stroke
    /// TIERS, not a width, so what survives the import is the ORDER of the widths within one symbol
    /// and never their absolute size. Requiring every reader to convert to a common unit would be
    /// arithmetic that no consumer can observe. See <c>KitTemplateSymbol.StrokeTiers</c> for the
    /// mapping, which is the only thing that reads this.</para>
    /// </summary>
    public double Width { get; init; }
}

/// <summary>
/// Which EDGE of a symbol's body a pin sits on, and so which way its name runs from it — inward.
///
/// <para><see cref="Top"/> and <see cref="Bottom"/> are the vertical lead's two cases; a name on one
/// of those runs ALONG the lead, turned a quarter turn, because a horizontal name on an edge whose
/// pins are one grid apart overlaps its neighbours.</para>
///
/// <para>Framework-free like the shapes, and for the same reason: the readers live in the core and
/// <c>SymbolPinNameAlign</c> lives beside the renderer in <c>src/Ui</c>.</para>
/// </summary>
public enum KitTextAlign { Left, Center, Right, Top, Bottom }

/// <summary>A straight segment.</summary>
public sealed record KitSymbolLine(double X1, double Y1, double X2, double Y2) : KitSymbolShape;

/// <summary>
/// A rectangle given by two opposite corners. Only rectangles that do NOT declare a terminal reach
/// here — one that does is a pin, and is reported as such instead.
/// </summary>
public sealed record KitSymbolRectangle(double X1, double Y1, double X2, double Y2, bool Filled)
    : KitSymbolShape;

/// <summary>
/// A run of points, closed or open. <paramref name="Xy"/> is a flat list of x,y pairs and never
/// repeats the first point at the end — a closed run says so with <paramref name="Closed"/>.
/// </summary>
public sealed record KitSymbolPath(IReadOnlyList<double> Xy, bool Closed, bool Filled) : KitSymbolShape;

/// <summary>
/// A circular arc.
///
/// <para><b>The angles are the file's own and are NOT circuitRF's.</b> This format measures them
/// counter-clockwise ON SCREEN from the +x axis; circuitRF's own arc primitive measures clockwise.
/// The conversion is the consumer's, and it is a sign flip on both fields — doing it here would
/// bury a rendering convention inside a format reader.</para>
/// </summary>
public sealed record KitSymbolArc(double Cx, double Cy, double Radius, double StartDeg, double SweepDeg)
    : KitSymbolShape;
