// ================================================================
//  PlotRect / PlotPoint against the Avalonia types they replaced.
//
//  RND-4 replaced the Data Display's only Avalonia dependency in its models and renderers — 17
//  `Rect`, 3 `Point` — with framework-free equivalents, so `CircuitRF.Render` could draw a `.cdd`
//  with no Avalonia on the path (brief-render-4-data-display.md R-rnd4-1).
//
//  The semantics had to be Avalonia's, not merely similar, and this is the assembly that can say so:
//  tests/Ui.Tests is the one project that references BOTH. Autoscale unions bounding boxes and the
//  resulting window is what every tick, label and plotted point is computed from, so a Union that
//  differs on an empty operand is a DIFFERENT PICTURE — and one that would be invisible until a
//  plot happened to start from `default`.
// ================================================================

using CircuitRF.Render.DataDisplay;
using Xunit;

namespace CircuitRF.Ui.Tests.Render;

public sealed class PlotGeometryParityTests
{
    public static TheoryData<double, double, double, double> Rects() => new()
    {
        {   0,   0,   0,   0 },   // the empty rect Union special-cases
        {   5,   7,   0,   0 },   // empty AWAY from the origin — the case a size-only test misses
        {  -1,  -1,   2,   2 },   // the unity Smith window
        {   0,   0,   2,   2 },
        {  10,  20,   5,   3 },
        { -50, -50, 150, 150 },   // Axes' own default window
        {   3,   4,  -2,  -6 },   // negative extents, which neither type normalises
    };

    [Theory]
    [MemberData(nameof(Rects))]
    public void EdgesAndCorners_MatchAvalonia(double x, double y, double w, double h)
    {
        var mine  = new PlotRect(x, y, w, h);
        var theirs = new Avalonia.Rect(x, y, w, h);

        Assert.Equal(theirs.Left,   mine.Left);
        Assert.Equal(theirs.Top,    mine.Top);
        Assert.Equal(theirs.Right,  mine.Right);
        Assert.Equal(theirs.Bottom, mine.Bottom);
        Assert.Equal(theirs.TopLeft.X,     mine.TopLeft.X);
        Assert.Equal(theirs.TopLeft.Y,     mine.TopLeft.Y);
        Assert.Equal(theirs.BottomRight.X, mine.BottomRight.X);
        Assert.Equal(theirs.BottomRight.Y, mine.BottomRight.Y);
        Assert.Equal(theirs.Center.X,      mine.Center.X);
        Assert.Equal(theirs.Center.Y,      mine.Center.Y);
    }

    /// <summary>
    /// Union over every ordered pair of the fixtures above — 49 cases, which is what makes the
    /// empty-operand behaviour (the one place the two could plausibly disagree) unavoidable rather
    /// than a case someone remembered to write.
    /// </summary>
    [Fact]
    public void Union_MatchesAvalonia_OverEveryOrderedPair()
    {
        var cases = Rects().Select(row => ((double)row[0], (double)row[1], (double)row[2], (double)row[3])).ToList();

        foreach (var a in cases)
        foreach (var b in cases)
        {
            var mine   = new PlotRect(a.Item1, a.Item2, a.Item3, a.Item4)
                        .Union(new PlotRect(b.Item1, b.Item2, b.Item3, b.Item4));
            var theirs = new Avalonia.Rect(a.Item1, a.Item2, a.Item3, a.Item4)
                        .Union(new Avalonia.Rect(b.Item1, b.Item2, b.Item3, b.Item4));

            Assert.Equal(theirs.X,      mine.X);
            Assert.Equal(theirs.Y,      mine.Y);
            Assert.Equal(theirs.Width,  mine.Width);
            Assert.Equal(theirs.Height, mine.Height);
        }
    }

    [Fact]
    public void Contains_MatchesAvalonia_OnAndAroundTheEdges()
    {
        var mine   = new PlotRect(10, 20, 5, 3);
        var theirs = new Avalonia.Rect(10, 20, 5, 3);

        double[] xs = [9.999, 10, 12, 15, 15.001];
        double[] ys = [19.999, 20, 21, 23, 23.001];

        foreach (double px in xs)
        foreach (double py in ys)
            Assert.Equal(theirs.Contains(new Avalonia.Point(px, py)),
                         mine.Contains(new PlotPoint(px, py)));
    }

    /// <summary>
    /// A `default` rect is what <c>Plot.Autoscale</c> starts its bounding box from, and its
    /// emptiness is what stops that start from dragging every window back to the origin.
    /// </summary>
    [Fact]
    public void DefaultUnionedWithARealRect_IsThatRect()
    {
        var box = new PlotRect(100, 200, 30, 40);
        Assert.Equal(box, default(PlotRect).Union(box));
        Assert.Equal(box, box.Union(default));
    }
}
