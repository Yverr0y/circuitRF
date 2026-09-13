// ================================================================
//  PlotDocumentScope.cs  —  "is this frame going into a DOCUMENT, or
//  onto the screen?"
//
//  Almost nothing in this renderer needs to know. One thing does: the
//  3D pattern surface is tens of thousands of triangles, and the two
//  ways Skia can put those on a canvas are not interchangeable.
//
//    * A filled PATH per triangle is exact, antialiased and resolution
//      independent — and it is the only one of the two that a VECTOR
//      device records at all. SkSVGDevice and SkPDFDevice ignore
//      drawVertices outright: measured, not assumed — the same mesh
//      that rasterises correctly writes a 150-byte SVG with an empty
//      <svg> element and a one-page PDF with nothing on it. A surface
//      that exports as a blank square is exactly the plausible-and-
//      empty picture R-rnd4-4 refuses to draw.
//    * One drawVertices call for the whole mesh is ~9x faster (measured
//      at the 91 x 360 grid an EM far-field run produces: 48.6 ms of
//      paths against 5.6 ms of mesh, at retina scale), which is the
//      difference between 17 fps and a display that pans smoothly.
//
//  So the surface draws itself as a mesh for a live frame and as paths
//  for a document, and this is how it tells them apart. The scope is
//  entered by PlotDocumentWriter — the ONE place in the repo that
//  builds a plot document, PDF, SVG and PNG alike — rather than by a
//  parameter each caller passes, because a parameter can be forgotten
//  by the next writer and the symptom would be an empty export.
//
//  A DOCUMENT, not "a vector device": a PNG export is raster and could
//  take the mesh, but it is written once and read at whatever size the
//  reader likes, so it gets the exact path fill too. The mesh is for
//  the frame that has 16 ms to be in.
// ================================================================

using System;

namespace CircuitRF.Render.DataDisplay;

/// <summary>
/// Marks the calling thread as drawing into an exported document rather than onto the screen.
/// Reentrant (a multi-page PDF nests one page scope inside the document's), thread-local, and
/// restored in a <c>finally</c> so a throwing render cannot leave it set.
/// </summary>
public static class PlotDocumentScope
{
    [ThreadStatic] private static int _depth;
    [ThreadStatic] private static int _budgeted;

    /// <summary>True while this thread is drawing a document page.</summary>
    public static bool IsActive => _depth > 0;

    /// <summary>
    /// <b>True while the document being drawn is a FIGURE at a known printed size</b>, and a
    /// surface may therefore be tessellated to the renderer's facet budget instead of drawn whole.
    ///
    /// <para>An ordinary export is archival: the reader may open it at any size, so it gets every
    /// facet the grid has. A user-doc figure is placed at one width on one page, where a 1° facet of
    /// an EM far-field pattern is far under a pixel — and the difference is 11 MB of SVG against
    /// 750 KB for a picture nobody can tell apart (measured 2026-09-12). The two are the same claim
    /// about the data and a different claim about the page, which is why this is a second question
    /// and not a redefinition of <see cref="IsActive"/>.</para>
    /// </summary>
    public static bool SurfacesAreBudgeted => _budgeted > 0;

    /// <summary>Opens the scope; dispose to close it.</summary>
    /// <param name="budgetSurfaces">See <see cref="SurfacesAreBudgeted"/>. Default false — an export
    /// draws the grid it was given.</param>
    public static IDisposable Enter(bool budgetSurfaces = false)
    {
        _depth++;
        if (budgetSurfaces) _budgeted++;
        return budgetSurfaces ? BudgetedCloser.Instance : Closer.Instance;
    }

    private sealed class Closer : IDisposable
    {
        public static readonly Closer Instance = new();
        public void Dispose() => _depth--;
    }

    private sealed class BudgetedCloser : IDisposable
    {
        public static readonly BudgetedCloser Instance = new();
        public void Dispose() { _depth--; _budgeted--; }
    }
}
