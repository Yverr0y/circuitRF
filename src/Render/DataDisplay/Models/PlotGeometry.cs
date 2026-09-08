// ================================================================
//  PlotGeometry.cs  —  the two value types the Data Display's models and
//  renderers used to borrow from Avalonia
//
//  RND-4 (brief-render-4-data-display.md R-rnd4-1/R-rnd4-2). Across
//  Models and Renderers the ENTIRE Avalonia surface was 17 `Rect`,
//  3 `Point` and a handful of `Media.Color` — value types with no
//  framework behind them, borrowed because the code happened to live
//  next to a window. `PlotRect` and `PlotPoint` are those two, said
//  here, so `CircuitRF.Render` can draw a `.cdd` with no Avalonia on
//  the path.
//
//  WHY NOT `Rect` AND `Point`. src/Ui consumes this namespace and is
//  full of `Avalonia.Rect`/`Avalonia.Point`; a type of the same name
//  reaching those files through a using directive turns every one of
//  them into CS0104. The prefix is what keeps the two apart at a
//  glance, and a plot rectangle is not a control rectangle.
//
//  THE SEMANTICS ARE AVALONIA'S, DELIBERATELY, not merely similar:
//  autoscale unions bounding boxes and the resulting window is what
//  every tick, label and traced point is computed from, so a Union that
//  differs on an empty operand is a DIFFERENT PICTURE.
//  `PlotGeometryParityTests` in tests/Ui.Tests — the one project that
//  can reference both — asserts the two agree case for case.
// ================================================================

namespace CircuitRF.Render.DataDisplay;

/// <summary>A point in plot space. Avalonia's <c>Point</c>, without Avalonia.</summary>
public readonly record struct PlotPoint(double X, double Y)
{
    public override string ToString() => $"{X}, {Y}";
}

/// <summary>
/// An axis-aligned rectangle in plot space — an axis window, a path bounding box, a
/// renderer viewport. Avalonia's <c>Rect</c>, without Avalonia.
/// </summary>
public readonly record struct PlotRect(double X, double Y, double Width, double Height)
{
    public PlotRect(PlotPoint topLeft, PlotPoint bottomRight)
        : this(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y) { }

    public double Left   => X;
    public double Top    => Y;
    public double Right  => X + Width;
    public double Bottom => Y + Height;

    public PlotPoint TopLeft     => new(X, Y);
    public PlotPoint BottomRight => new(Right, Bottom);
    public PlotPoint Center      => new(X + Width / 2, Y + Height / 2);

    /// <summary>Avalonia's own emptiness test: zero size, whatever the origin.</summary>
    private bool IsEmptyRect => Width == 0 && Height == 0;

    public bool Contains(PlotPoint p) =>
        p.X >= X && p.X <= X + Width && p.Y >= Y && p.Y <= Y + Height;

    public bool Contains(PlotRect r) => Contains(r.TopLeft) && Contains(r.BottomRight);

    /// <summary>
    /// The smallest rectangle containing both. An EMPTY operand is returned around rather than
    /// unioned — an autoscale that started from <c>default</c> would otherwise drag every window
    /// back to the origin.
    /// </summary>
    public PlotRect Union(PlotRect r)
    {
        if (IsEmptyRect) return r;
        if (r.IsEmptyRect) return this;
        double x1 = Math.Min(X, r.X);
        double x2 = Math.Max(Right, r.Right);
        double y1 = Math.Min(Y, r.Y);
        double y2 = Math.Max(Bottom, r.Bottom);
        return new PlotRect(new PlotPoint(x1, y1), new PlotPoint(x2, y2));
    }

    public override string ToString() => $"{X}, {Y}, {Width}, {Height}";
}
