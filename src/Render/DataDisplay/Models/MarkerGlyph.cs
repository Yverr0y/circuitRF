// ================================================================
//  MarkerGlyph.cs  —  the SHAPE of a per-point trace symbol
//
//  One table of vertices, two consumers: the Skia trace renderer that
//  draws a plot, and the trace card's shape picker in src/Ui, which
//  parses the same outline into an Avalonia Geometry. A picker drawing
//  its own approximation of the glyph is a picker that can disagree
//  with the plot, and the disagreement is only visible after the file
//  has been saved with the wrong choice in it.
// ================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SkiaSharp;

namespace CircuitRF.Render.DataDisplay
{
    /// <summary>
    /// Geometry for every <see cref="MarkerType"/>.
    ///
    /// <para>Each glyph is ONE closed polygon in units of the marker radius, y DOWN (screen
    /// sense), centred on the origin — except <see cref="MarkerType.Circle"/>, which no polygon
    /// approximates well and which the renderer draws as an oval. Keeping every other glyph to a
    /// single closed path is what lets them all take the existing fill + dark outline treatment
    /// unchanged; a glyph built from several overlapping pieces would draw its own internal seams.</para>
    ///
    /// <para><b>Self-intersection is deliberate</b> for Bowtie and Hourglass: under the non-zero
    /// winding rule each lobe still fills, and the stroke draws the crossing that makes the shape
    /// readable at 3 px.</para>
    /// </summary>
    public static class MarkerGlyph
    {
        // ---- Visual weight ---------------------------------------------------
        //
        // A square of half-width r covers 4r², a circle πr², a diamond 2r². Drawn at one radius
        // the set reads as a set of DIFFERENT SIZES rather than different shapes, so the thinner
        // outlines are grown to roughly match. Circle and Square are pinned at 1.0 — they are the
        // two shapes that already existed, and a `.cdd` saved before this must draw identically.
        private static double Weight(MarkerType t) => t switch
        {
            MarkerType.Circle        => 1.00,
            MarkerType.Square        => 1.00,
            MarkerType.Diamond       => 1.30,
            MarkerType.TriangleUp    => 1.25,
            MarkerType.TriangleDown  => 1.25,
            MarkerType.TriangleLeft  => 1.25,
            MarkerType.TriangleRight => 1.25,
            MarkerType.Plus          => 1.10,
            MarkerType.Cross         => 1.10,
            MarkerType.Star          => 1.30,
            MarkerType.Hexagon       => 1.10,
            MarkerType.Pentagon      => 1.15,
            MarkerType.Bowtie        => 1.00,
            MarkerType.Hourglass     => 1.00,
            _                        => 1.00
        };

        /// <summary>True when the glyph is an ellipse rather than a polygon.</summary>
        public static bool IsEllipse(MarkerType t) => t == MarkerType.Circle;

        /// <summary>
        /// The glyph outline, in units of the marker radius. Empty for <see cref="IsEllipse"/>.
        /// </summary>
        public static IReadOnlyList<(double X, double Y)> Outline(MarkerType t)
        {
            double w = Weight(t);
            var    v = RawOutline(t);
            var    o = new (double X, double Y)[v.Length];
            for (int i = 0; i < v.Length; i++) o[i] = (v[i].X * w, v[i].Y * w);
            return o;
        }

        // Circumradius-1 outlines, before the weight above is applied.
        private static (double X, double Y)[] RawOutline(MarkerType t)
        {
            const double S = 0.8660254037844386;  // sin 60° — the triangles' half-base
            const double P = 0.36;                // Plus/Cross arm half-width

            return t switch
            {
                MarkerType.Circle        => Array.Empty<(double, double)>(),

                MarkerType.Square        => new[] { (-1.0, -1.0), (1.0, -1.0), (1.0, 1.0), (-1.0, 1.0) },
                MarkerType.Diamond       => new[] { (0.0, -1.0), (1.0, 0.0), (0.0, 1.0), (-1.0, 0.0) },

                MarkerType.TriangleUp    => new[] { (0.0, -1.0), (S, 0.5), (-S, 0.5) },
                MarkerType.TriangleDown  => new[] { (0.0, 1.0), (-S, -0.5), (S, -0.5) },
                MarkerType.TriangleLeft  => new[] { (-1.0, 0.0), (0.5, -S), (0.5, S) },
                MarkerType.TriangleRight => new[] { (1.0, 0.0), (-0.5, S), (-0.5, -S) },

                MarkerType.Plus          => new[]
                {
                    (-P, -1.0), (P, -1.0), (P, -P), (1.0, -P),
                    (1.0,  P), (P,  P), (P,  1.0), (-P, 1.0),
                    (-P,  P), (-1.0, P), (-1.0, -P), (-P, -P)
                },
                MarkerType.Cross         => Rotate(RawOutline(MarkerType.Plus), Math.PI / 4),

                MarkerType.Star          => Radial(5, 1.0, 0.382),
                MarkerType.Pentagon      => Radial(5, 1.0, null),
                MarkerType.Hexagon       => Radial(6, 1.0, null),

                // Self-intersecting on purpose — see the class remarks.
                MarkerType.Bowtie        => new[] { (-1.0, -1.0), (1.0, 1.0), (1.0, -1.0), (-1.0, 1.0) },
                MarkerType.Hourglass     => new[] { (-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0) },

                _                        => Array.Empty<(double, double)>()
            };
        }

        /// <summary>n points on a circle starting at 12 o'clock; with <paramref name="inner"/>, a star.</summary>
        private static (double X, double Y)[] Radial(int n, double outer, double? inner)
        {
            int count = inner.HasValue ? n * 2 : n;
            var pts   = new (double X, double Y)[count];
            double step = Math.PI / n;            // half a sector
            for (int i = 0; i < count; i++)
            {
                double a = -Math.PI / 2 + i * (inner.HasValue ? step : 2 * step);
                double r = inner.HasValue && (i % 2 == 1) ? inner.Value : outer;
                pts[i] = (r * Math.Cos(a), r * Math.Sin(a));
            }
            return pts;
        }

        private static (double X, double Y)[] Rotate((double X, double Y)[] src, double radians)
        {
            double c = Math.Cos(radians), s = Math.Sin(radians);
            var    o = new (double X, double Y)[src.Length];
            for (int i = 0; i < src.Length; i++)
                o[i] = (src[i].X * c - src[i].Y * s, src[i].X * s + src[i].Y * c);
            return o;
        }

        // ---- Skia ------------------------------------------------------------

        /// <summary>
        /// The glyph as a Skia path centred on the ORIGIN at pixel radius <paramref name="r"/>.
        /// Build it once per trace and translate the canvas per point — the outline does not
        /// change between points, and re-building it for each of a few thousand samples shows.
        /// </summary>
        public static SKPath BuildPath(MarkerType t, float r)
        {
            var path = new SKPath { FillType = SKPathFillType.Winding };

            if (IsEllipse(t))
            {
                path.AddOval(new SKRect(-r, -r, r, r));
                return path;
            }

            var pts = Outline(t);
            if (pts.Count == 0) return path;

            path.MoveTo((float)(pts[0].X * r), (float)(pts[0].Y * r));
            for (int i = 1; i < pts.Count; i++)
                path.LineTo((float)(pts[i].X * r), (float)(pts[i].Y * r));
            path.Close();
            return path;
        }

        // ---- SVG path data ---------------------------------------------------

        /// <summary>
        /// The same outline as SVG path data, centred on the origin at radius <paramref name="r"/>
        /// — what the trace card's picker parses into an Avalonia <c>Geometry</c>, so the icon in
        /// the popup is the shape the plot draws and not a look-alike.
        /// </summary>
        public static string SvgPath(MarkerType t, double r = 1.0)
        {
            var ci = CultureInfo.InvariantCulture;

            if (IsEllipse(t))
                return string.Create(ci,
                    $"M {-r},0 A {r},{r} 0 1 0 {r},0 A {r},{r} 0 1 0 {-r},0 Z");

            var pts = Outline(t);
            if (pts.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < pts.Count; i++)
            {
                sb.Append(i == 0 ? "M " : " L ");
                sb.Append((pts[i].X * r).ToString("0.####", ci));
                sb.Append(',');
                sb.Append((pts[i].Y * r).ToString("0.####", ci));
            }
            sb.Append(" Z");
            return sb.ToString();
        }

        // ---- Naming ----------------------------------------------------------

        /// <summary>Label for the picker's tooltip. The enum name IS what a `.cdd` stores.</summary>
        public static string Description(MarkerType t) => t switch
        {
            MarkerType.TriangleUp    => "Triangle up",
            MarkerType.TriangleDown  => "Triangle down",
            MarkerType.TriangleLeft  => "Triangle left",
            MarkerType.TriangleRight => "Triangle right",
            _                        => t.ToString()
        };
    }
}
