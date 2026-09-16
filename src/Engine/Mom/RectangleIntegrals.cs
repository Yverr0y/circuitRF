// L8c — the closed-form INNER integrals over a rectangular cell, for an observation point lying in
// the SAME plane.
//
// ══════════════════════════════════════════════════════════════════════════════════════════════
// WHY THIS FILE EXISTS, AND WHY IT IS THE WHOLE PAYOFF OF THE RECTANGULAR MESH
// ══════════════════════════════════════════════════════════════════════════════════════════════
//
// §10.2 names item 4 — the singular self- and near-term integrals — as the second place a schedule
// goes to die. The classic difficulty is that BOTH integrals of the 4-D Galerkin double integral are
// done numerically: the inner one is singular, the outer one then sees a function whose derivative
// blows up, and every "nearly touching cells" special case in a triangle code exists to cope with
// that. On a RECTANGULAR mesh with source and observer in one plane the inner integral is available
// in closed form, which removes the difficulty at the source rather than managing it. What is left is
// an outer integral over a continuous function with a kink — a quadrature-ORDER question, not a
// special case (R-fil-5 measures it; PlanarFillTests' Tier 6 reports the convergence).
//
// ══════════════════════════════════════════════════════════════════════════════════════════════
// DERIVATION — all six, from antiderivatives, not transcribed (the D4 rule L8a followed)
// ══════════════════════════════════════════════════════════════════════════════════════════════
//
// Put the observation point at the origin and let the rectangle be [x1,x2] x [y1,y2] in the local
// (u, v) frame, r = sqrt(u^2 + v^2). Every integrand below is either EVEN in u or ODD in u, and even
// in v, so a single CORNER primitive Q(a,b) = ∫₀^a ∫₀^b f du dv with a, b ≥ 0 generates the general
// rectangle by a signed four-corner sum (see the parity note on Corner, below). Working from a corner
// primitive rather than from a raw antiderivative is what makes the interior / edge / corner /
// far-outside cases ONE case: a naive corner-summed antiderivative divides by zero exactly where the
// observation point lands on a gridline, which on a Manhattan layout is not a rare event.
//
//  (1) ∫∫ dS/R.        F(u,v) = u·ln(v+r) + v·ln(u+r) has ∂²F/∂u∂v = 1/r:
//                        ∂F/∂v = u(1+v/r)/(v+r) + ln(u+r) + v²/(r(u+r)) = 1 + ln(u+r)   [the two
//                        rational pieces sum to (u²+ur+v²)/(r(u+r)) = 1], and ∂/∂u of that is 1/r.
//                      Q(a,b) = F(a,b) − F(0,b) − F(a,0) + F(0,0) = a·asinh(b/a) + b·asinh(a/b) —
//                      cancellation-free, since both asinh arguments are ≥ 0.
//                      HAND CHECK: the centre of a unit square is 4·Q(½,½) = 4·asinh(1) = 3.5254943…
//
//  (2) ∫∫ u dS/R.      G(u,v) = (v/2)·r + (u²/2)·ln(v+r) has ∂G/∂v = r/2 + v²/(2r) = r, hence
//                      ∂²G/∂u∂v = u/r.  Q(a,b) = (b/2)(r−b) + (a²/2)·asinh(b/a), with r−b written as
//                      a²/(r+b) because r−b cancels to nothing when a ≪ b.
//
//  (3) ∫∫ ln r dS.     ∫∫ ln(u²+v²) du dv = uv·ln(u²+v²) − 3uv + u²·atan(v/u) + v²·atan(u/v)
//                      (differentiate twice; the rational parts cancel to exactly 2, which the −3uv
//                      is there to absorb). Halved for ln r rather than ln r².
//                      Q(a,b) = (ab/2)ln(a²+b²) − (3/2)ab + (a²/2)atan(b/a) + (b²/2)atan(a/b).
//
//  (4) ∫∫ u·ln r dS.   ∫ u ln r du = ¼[(u²+v²)ln(u²+v²) − (u²+v²)] (substitute w = u²+v²), and
//                      integrating that over v gives
//                        W = ¼[(u²v + v³/3)ln(u²+v²) + (4/3)u³atan(v/u) − (7/3)u²v − (5/9)v³].
//                      Q(a,b) = (a²b/2)ln r + (b³/6)ln(r/b) + (a³/3)atan(b/a) − (7/12)a²b.
//
//  (5) ∫∫ r dS.        Easiest in polar over the corner wedge: ∫∫ r dA = ∫ ρ_max³/3 dθ, split at
//                      θ₀ = atan(b/a), using ∫sec³ = ½[sec·tan + ln|sec+tan|].
//                      Q(a,b) = abr/3 + (a³/6)asinh(b/a) + (b³/6)asinh(a/b).
//                      HAND CHECK: Q(1,1) = 0.76520 is the mean distance from the corner of a unit
//                      square, a number that can be looked up independently.
//
//  (6) ∫∫ u·r dS.      ∫ u·r du = ⅓(u²+v²)^{3/2}; integrating over v with the standard
//                      ∫(a²+v²)^{3/2}dv gives X = ⅓[(v/4)r³ + (3u²v/8)r + (3u⁴/8)ln(v+r)].
//                      Q(a,b) = (b/12)(r³−b³) + (a²b/8)r + (a⁴/8)asinh(b/a), again with r−b written
//                      as a²/(r+b) inside the factorised r³−b³.
//
// R-fil-4 asks for THREE of these to be checked against adaptive quadrature to 1e-12. All six are —
// see RectangleIntegralTests (Tier 1). (1)/(3)/(5) are the pulse-weighted forms the SCALAR block and
// D4's per-cell potential matrix need; (2)/(4)/(6) are the first moments a rooftop's LINEAR weight
// needs for the VECTOR block. (5) and (6) are used only when the extraction order reaches 2 — see
// SingularExtraction.
//
// Nothing here knows about frequency, a Green's function, or a mesh. These are pure geometry, which
// is exactly why D6's frequency-independent core is expressible at all.

using System.Numerics;

namespace CircuitRF.Engine.Mom;

/// <summary>
/// The six closed-form integrals of <c>1/R</c>, <c>ln r</c> and <c>r</c> — each with and without a
/// linear weight — over an axis-aligned rectangle, for an observation point <b>in the same
/// plane</b>. See the file header for the derivations and the two hand-checkable values.
///
/// <para>Every entry point takes the rectangle in a frame whose ORIGIN IS THE OBSERVATION POINT.
/// Interior, edge, corner and far-outside observation points are one case, not four.</para>
/// </summary>
public static class RectangleIntegrals
{
    // ── The corner primitives: the observation point is the origin, the rectangle is [0,a]x[0,b] ──

    /// <summary>∫₀^a ∫₀^b du dv / r — the zeroth moment. <c>a·asinh(b/a) + b·asinh(a/b)</c>.</summary>
    public static double Corner0(double a, double b)
    {
        if (!(a > 0) || !(b > 0)) return 0.0;
        return a * Math.Asinh(b / a) + b * Math.Asinh(a / b);
    }

    /// <summary>
    /// <b>The via z-integral's own corner primitive:</b> <c>∫₀^a ∫₀^b du dv / √(u²+v²+c²)</c>, i.e.
    /// the same zeroth moment with the observation point lifted OUT of the rectangle's plane by
    /// <paramref name="c"/>.
    ///
    /// <para>Derived rather than transcribed, and it reduces to <see cref="Corner0"/> exactly at
    /// c = 0 — which is the check <c>RectangleIntegralTests</c> runs first. The antiderivative is
    /// <c>u·asinh(v/√(u²+c²)) + v·asinh(u/√(v²+c²)) − c·atan(uv/(c·√(u²+v²+c²)))</c>; the third term
    /// is the one that does not survive setting c = 0 in the planar form and is exactly what makes
    /// a vertical current's self term finite rather than logarithmic.</para>
    /// </summary>
    public static double Corner0AtOffset(double a, double b, double c)
    {
        if (!(a > 0) || !(b > 0)) return 0.0;
        double ac = Math.Abs(c);
        if (ac == 0) return Corner0(a, b);
        double r = Math.Sqrt(a * a + b * b + ac * ac);
        return a * Math.Asinh(b / Hypot(a, ac))
             + b * Math.Asinh(a / Hypot(b, ac))
             - ac * Math.Atan(a * b / (ac * r));
    }

    /// <summary>
    /// <b>MIM-8 — <see cref="Corner0AtOffset"/> at a COMPLEX offset:</b>
    /// <c>∫₀^a ∫₀^b du dv / √(u²+v²+c²)</c>, with every square root taken on the branch with
    /// POSITIVE REAL PART — the decaying branch <c>DcimModel.EvaluateAtHeights</c> already picks for
    /// a fitted image's <c>R = √(ρ² + b²)</c>.
    ///
    /// <para><b>Why a complex offset is needed at all.</b> A thin film puts a DCIM image at a depth
    /// of the order of the film thickness, and an image depth is complex in general — that is what
    /// the method fits. The peak that image carries is what the fill's Gauss rule cannot see
    /// (§MIM-8), so the integral of its static part has to be available in closed form for the
    /// depth the fit actually produced, not for a real approximation of it.</para>
    ///
    /// <para><b>It is the analytic continuation of the real form, not a second derivation.</b> The
    /// antiderivative is the same; the only question is branch selection, and both pieces are
    /// arranged so that the arguments stay where the principal branch is the continuous one:</para>
    /// <list type="bullet">
    ///   <item>the two <c>asinh</c> arguments are <c>a/√(b²+c²)</c> and <c>b/√(a²+c²)</c>, and a
    ///     positive real over a positive-real-part root has positive real part — the half-plane the
    ///     principal <c>asinh</c>'s cuts (the imaginary axis outside ±i) do not enter.</item>
    ///   <item>the third term is <c>−c·atan(ab/(cR))</c>, whose argument CAN approach the cuts at
    ///     ±i. <c>atan(z) + atan(1/z) = π/2</c> holds wherever the argument stays off the cuts, so
    ///     the evaluation takes whichever of <c>z</c> and <c>1/z</c> has modulus ≤ 1 — which is also
    ///     the one with no cancellation in it, since <c>atan</c> of a small argument is that
    ///     argument.</item>
    /// </list>
    ///
    /// <para>At a real <paramref name="c"/> it reproduces <see cref="Corner0AtOffset"/>, and it is
    /// measured against adaptive quadrature over a fan of complex offsets in
    /// <c>RectangleIntegralTests</c> — including nearly-imaginary ones, where the integrand has a
    /// near-pole ON the real-ρ path and it is the QUADRATURE that fails rather than this.</para>
    /// </summary>
    public static Complex Corner0AtComplexOffset(double a, double b, Complex c)
    {
        if (!(a > 0) || !(b > 0)) return Complex.Zero;
        if (c.Imaginary == 0) return Corner0AtOffset(a, b, c.Real);

        Complex c2 = c * c;
        Complex ra = PositiveRoot(a * a + c2);
        Complex rb = PositiveRoot(b * b + c2);
        Complex r  = PositiveRoot(a * a + b * b + c2);

        Complex z = a * b / (c * r);
        Complex third = z.Magnitude <= 1.0
            ? -c * Atan(z)
            : c * (Atan(1.0 / z) - Math.PI / 2.0);

        return a * Asinh(b / ra) + b * Asinh(a / rb) + third;
    }

    /// <summary>∫∫ dS′/√(u²+v²+c²) over <c>[x1,x2] x [y1,y2]</c> at a COMPLEX offset — the signed
    /// four-corner sum of <see cref="Corner0AtComplexOffset"/>, on the same parity rule the real
    /// forms use (the integrand is even in both u and v).</summary>
    public static Complex InverseAtComplexOffset(double x1, double x2, double y1, double y2, Complex c)
        => SC(x2, y2, c) - SC(x1, y2, c) - SC(x2, y1, c) + SC(x1, y1, c);

    private static Complex SC(double x, double y, Complex c)
        => x == 0 || y == 0
            ? Complex.Zero
            : Math.Sign(x) * Math.Sign(y) * Corner0AtComplexOffset(Math.Abs(x), Math.Abs(y), c);

    /// <summary>√z on the branch with positive real part — the decaying one.</summary>
    private static Complex PositiveRoot(Complex z)
    {
        var r = Complex.Sqrt(z);
        return r.Real < 0 ? -r : r;
    }

    private static Complex Asinh(Complex z) => Complex.Log(z + Complex.Sqrt(z * z + 1.0));

    private static Complex Atan(Complex z) =>
        0.5 * Complex.ImaginaryOne * (Complex.Log(1.0 - Complex.ImaginaryOne * z)
                                    - Complex.Log(1.0 + Complex.ImaginaryOne * z));

    /// <summary>∫₀^a ∫₀^b u du dv / r — the first moment along the FIRST argument's axis.</summary>
    public static double Corner0Moment(double a, double b)
    {
        if (!(a > 0) || !(b > 0)) return 0.0;
        double r = Hypot(a, b);
        // r − b cancels to nothing when a ≪ b; a²/(r+b) is the same number without the subtraction.
        return 0.5 * b * (a * a / (r + b)) + 0.5 * a * a * Math.Asinh(b / a);
    }

    /// <summary>∫₀^a ∫₀^b ln r du dv.</summary>
    public static double CornerLog(double a, double b)
    {
        if (!(a > 0) || !(b > 0)) return 0.0;
        return a * b * LogRadius(a, b)
             - 1.5 * a * b
             + 0.5 * a * a * Math.Atan(b / a)
             + 0.5 * b * b * Math.Atan(a / b);
    }

    /// <summary>∫₀^a ∫₀^b u·ln r du dv.</summary>
    public static double CornerLogMoment(double a, double b)
    {
        if (!(a > 0) || !(b > 0)) return 0.0;
        double r = Hypot(a, b);
        // ln(r/b) = ½·ln(1 + (a/b)²) — written through Log1P because r/b rounds to 1 long before the
        // term stops mattering. At a/b = 1e-4 the direct form is wrong in the SIXTH significant
        // figure of the answer (measured: 4.3e-10 relative against an independent quadrature, versus
        // 1.7e-10 for this form, which is the reference's own limit). A 1 : 1e4 aspect ratio is not a
        // contrived case — it is an edge-graded cell on a microstrip line.
        return 0.5 * a * a * b * LogRadius(a, b)
             + (b * b * b / 12.0) * Log1P((a / b) * (a / b))
             + (a * a * a / 3.0) * Math.Atan(b / a)
             - (7.0 / 12.0) * a * a * b;
    }

    /// <summary>∫₀^a ∫₀^b r du dv. <c>Corner(1,1) = 0.765196…</c>, the mean corner distance of a
    /// unit square.</summary>
    public static double CornerRadius(double a, double b)
    {
        if (!(a > 0) || !(b > 0)) return 0.0;
        double r = Hypot(a, b);
        return a * b * r / 3.0
             + (a * a * a / 6.0) * Math.Asinh(b / a)
             + (b * b * b / 6.0) * Math.Asinh(a / b);
    }

    /// <summary>∫₀^a ∫₀^b u·r du dv.</summary>
    public static double CornerRadiusMoment(double a, double b)
    {
        if (!(a > 0) || !(b > 0)) return 0.0;
        double r    = Hypot(a, b);
        double rmb  = a * a / (r + b);                  // r − b, without the cancellation
        return (b / 12.0) * rmb * (r * r + r * b + b * b)
             + (a * a * b / 8.0) * r
             + (a * a * a * a / 8.0) * Math.Asinh(b / a);
    }

    // ── The general rectangle, by the signed corner rule ──────────────────────────────────────
    //
    // For f EVEN in both u and v, S(x,y) = sgn(x)·sgn(y)·Q(|x|,|y|) is ∫₀^x∫₀^y f, and the rectangle
    // integral is the usual S(x₂,y₂) − S(x₁,y₂) − S(x₂,y₁) + S(x₁,y₁). For f ODD in u and even in v,
    // ∫₀^x f du is EVEN in x (∫₀^x u du = x²/2), so the u-sign drops out: S(x,y) = sgn(y)·Q(|x|,|y|).
    // sgn(0) = 0 gives S = 0, which is right — a degenerate strip integrates to nothing.

    /// <summary>∫∫ dS' / R over <c>[x1,x2] x [y1,y2]</c>, observation point at the origin.</summary>
    public static double Inverse(double x1, double x2, double y1, double y2) =>
        Even(Corner0, x1, x2, y1, y2);

    /// <summary>∫∫ dS' / √(u²+v²+c²) over <c>[x1,x2] x [y1,y2]</c>, observation point at the origin
    /// of the plane and <paramref name="c"/> above it. See <see cref="Corner0AtOffset"/>.</summary>
    public static double InverseAtOffset(double x1, double x2, double y1, double y2, double c) =>
        Even((a, b) => Corner0AtOffset(a, b, c), x1, x2, y1, y2);

    /// <summary>∫∫ ln r dS' over the same rectangle.</summary>
    public static double Log(double x1, double x2, double y1, double y2) =>
        Even(CornerLog, x1, x2, y1, y2);

    /// <summary>∫∫ r dS' over the same rectangle.</summary>
    public static double Radius(double x1, double x2, double y1, double y2) =>
        Even(CornerRadius, x1, x2, y1, y2);

    /// <summary>∫∫ u dS' / R — the first moment along u, measured FROM THE OBSERVATION POINT.</summary>
    public static double InverseMomentU(double x1, double x2, double y1, double y2) =>
        OddInU(Corner0Moment, x1, x2, y1, y2);

    /// <summary>∫∫ v dS' / R — the first moment along v.</summary>
    public static double InverseMomentV(double x1, double x2, double y1, double y2) =>
        OddInU(Corner0Moment, y1, y2, x1, x2);

    /// <summary>∫∫ u·ln r dS'.</summary>
    public static double LogMomentU(double x1, double x2, double y1, double y2) =>
        OddInU(CornerLogMoment, x1, x2, y1, y2);

    /// <summary>∫∫ v·ln r dS'.</summary>
    public static double LogMomentV(double x1, double x2, double y1, double y2) =>
        OddInU(CornerLogMoment, y1, y2, x1, x2);

    /// <summary>∫∫ u·r dS'.</summary>
    public static double RadiusMomentU(double x1, double x2, double y1, double y2) =>
        OddInU(CornerRadiusMoment, x1, x2, y1, y2);

    /// <summary>∫∫ v·r dS'.</summary>
    public static double RadiusMomentV(double x1, double x2, double y1, double y2) =>
        OddInU(CornerRadiusMoment, y1, y2, x1, x2);

    /// <summary>∫∫ dS' — the area. Present so a caller assembling a weighted sum of all four
    /// extracted cores does not have to special-case the constant one.</summary>
    public static double Area(double x1, double x2, double y1, double y2) =>
        (x2 - x1) * (y2 - y1);

    /// <summary>∫∫ u dS' — the first moment of the area itself, about the observation point.</summary>
    public static double AreaMomentU(double x1, double x2, double y1, double y2) =>
        0.5 * (x2 * x2 - x1 * x1) * (y2 - y1);

    /// <summary>∫∫ v dS'.</summary>
    public static double AreaMomentV(double x1, double x2, double y1, double y2) =>
        0.5 * (y2 * y2 - y1 * y1) * (x2 - x1);

    // ── Plumbing ──────────────────────────────────────────────────────────────────────────────

    private static double Even(Func<double, double, double> q,
                               double x1, double x2, double y1, double y2)
        => S(q, x2, y2) - S(q, x1, y2) - S(q, x2, y1) + S(q, x1, y1);

    private static double S(Func<double, double, double> q, double x, double y)
        => Math.Sign(x) * Math.Sign(y) * q(Math.Abs(x), Math.Abs(y));

    private static double OddInU(Func<double, double, double> q,
                                 double x1, double x2, double y1, double y2)
        => So(q, x2, y2) - So(q, x1, y2) - So(q, x2, y1) + So(q, x1, y1);

    private static double So(Func<double, double, double> q, double x, double y)
        => Math.Sign(y) * q(Math.Abs(x), Math.Abs(y));

    /// <summary>√(a²+b²) without the overflow/underflow of the naive form.</summary>
    private static double Hypot(double a, double b) => double.Hypot(a, b);

    /// <summary>
    /// <c>ln √(a²+b²)</c> written as <c>ln(max) + ½·ln(1 + (min/max)²)</c>.
    ///
    /// <para><b>The naive <c>Math.Log(Math.Sqrt(a*a+b*b))</c> is not good enough here and the failure
    /// is invisible in the answer's leading digits.</b> When a ≪ b the radius rounds to b, so the log
    /// evaluates near 1 and every digit of the SMALL part of the answer is lost — measured at a/b =
    /// 1e-4 the term came out 20% wrong, which is 5e-9 of <see cref="CornerLogMoment"/>'s own value.
    /// <see cref="Log1P"/> keeps it exact, and a 1 : 1e4 aspect ratio is an ordinary edge-graded cell, not a
    /// contrived fixture.</para>
    /// </summary>
    private static double LogRadius(double a, double b)
    {
        double m = Math.Max(a, b), t = Math.Min(a, b) / m;
        return Math.Log(m) + 0.5 * Log1P(t * t);
    }

    /// <summary>
    /// <c>ln(1 + x)</c>, accurate for small x.
    ///
    /// <para><b>.NET's own <c>double.LogP1</c> is NOT a true log1p and this was measured, not
    /// assumed:</b> at x = 1e-8 it returns 9.99999989e-9 against the correct 9.99999995e-9 — a 6e-9
    /// relative error, i.e. it is simply <c>Log(1 + x)</c> with the addition's rounding left in. That
    /// error is invisible almost everywhere and shows up here as a 1e-9 relative error in
    /// <see cref="CornerLogMoment"/> on a high-aspect-ratio cell, which is exactly the case Tier 1's
    /// "sliver" fixture exists to catch. The identity below is exact because <c>u − 1</c> recovers the
    /// rounding error of <c>1 + x</c> exactly in binary floating point.</para>
    /// </summary>
    private static double Log1P(double x)
    {
        double u = 1.0 + x;
        if (u == 1.0) return x;                       // x is below half an ulp of 1
        return Math.Log(u) * (x / (u - 1.0));
    }
}
