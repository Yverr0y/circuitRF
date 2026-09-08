using System;
using System.Collections.Generic;
using System.Numerics;

namespace RfCore.Stability;

/// <summary>
/// Kurokawa's start-up signature on a driving-point function, and the encirclement count of a
/// locus — the two things the reference document reads off a polar plot by eye, made countable.
///
/// <para>T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023), §4.9–4.10,
/// Eq. 105–108, App. E.3 and E.12.</para>
/// </summary>
public static class WspKurokawa
{
    /// <summary>
    /// The frequencies at which <c>g(ω) = 1/T(ω)</c> shows Kurokawa's three start-up conditions
    /// (Eq. 107 for <c>T = H0</c>, Eq. 108 for <c>T = Y0</c>; E.12's restatement):
    ///
    /// <code>
    ///   Re(g) ≤ 0        Im(g) = 0        ∂Im(g)/∂ω &gt; 0
    /// </code>
    ///
    /// <para>Concretely, for consecutive samples <c>k, k+1</c> with
    /// <c>Im(g_k) &lt; 0 ≤ Im(g_{k+1})</c> — the sign change that is a <b>clockwise</b> crossing of
    /// the negative real axis, decreasing argument through <c>π</c>, from the lower half-plane to
    /// the upper — the zero of <c>Im(g)</c> is interpolated linearly in the sweep variable,
    /// <c>Re(g)</c> is interpolated at that point, and the frequency is reported when that
    /// <c>Re(g) ≤ 0</c>. A sample sitting exactly on <c>Im(g) = 0</c> is the crossing point itself.
    /// <b>The step direction is the whole content of the test:</b> a counter-clockwise crossing
    /// (<c>Im</c> falling from positive to negative) is the steady-state side of a <i>stable</i>
    /// resonance and is not reported.</para>
    ///
    /// <para><b>Both <c>H0</c> and <c>Y0</c> must be checked</b> (§4.9–4.10). A zero can mask the
    /// pole in one of them but never in both: the series resonator shows the signature in
    /// <c>1/Y0</c> only, the parallel resonator in <c>1/H0</c> only.</para>
    ///
    /// <para><b>Sampling caveat</b>, the same one the group-delay page carries for phase
    /// unwrapping: a sweep coarser than the resonance can step over a crossing entirely. This
    /// reports nothing rather than guessing — an empty result means "no crossing was sampled", not
    /// "the circuit is stable".</para>
    /// </summary>
    /// <param name="t">The driving-point function <c>H0</c> or <c>Y0</c>, sample by sample.</param>
    /// <param name="x">The sweep variable at each sample (Hz), ascending.</param>
    /// <returns>The crossing frequencies, ascending, in the units of <paramref name="x"/>. Empty
    /// when none was sampled — an empty list, never a zero (the document returns "a zero in case
    /// there are no frequencies", E.12; an empty list is the honest spelling).</returns>
    public static double[] UnstableFrequencies(IReadOnlyList<Complex> t, IReadOnlyList<double> x)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(x);
        int n = Math.Min(t.Count, x.Count);
        if (n < 2) return [];

        var g = new Complex[n];
        for (int k = 0; k < n; k++)
            g[k] = t[k] == Complex.Zero ? new Complex(double.NaN, double.NaN) : Complex.One / t[k];

        var hits = new List<double>();

        // A first sample sitting exactly on the axis has no preceding pair to catch it; it is a
        // clockwise crossing only if Im is rising out of it.
        if (g[0].Imaginary == 0.0 && g[1].Imaginary > 0.0 && g[0].Real <= 0.0)
            hits.Add(x[0]);

        for (int k = 0; k + 1 < n; k++)
        {
            double i0 = g[k].Imaginary, i1 = g[k + 1].Imaginary;
            if (double.IsNaN(i0) || double.IsNaN(i1)) continue;
            if (!(i0 < 0.0 && i1 >= 0.0)) continue;          // clockwise crossings only

            double span = i1 - i0;
            double frac = span == 0.0 ? 0.0 : -i0 / span;    // where Im(g) = 0 between the samples
            double xc   = x[k] + frac * (x[k + 1] - x[k]);
            double re   = g[k].Real + frac * (g[k + 1].Real - g[k].Real);
            if (re <= 0.0) hits.Add(xc);
        }

        hits.Sort();
        return [.. hits];
    }

    /// <summary>
    /// The running encirclement count of a locus, <c>enc = −unwrap(phase(SP))/360</c> (E.3, phase in
    /// degrees) — one value per sample, so a trace shows where the turns happen. The <b>net</b>
    /// count is the last value rounded to the nearest integer.
    ///
    /// <para>The document's minus makes a <b>clockwise</b> encirclement count positive, which is
    /// the sign the NDF and the Nyquist argument want (§8: the NDF "only encirculate the origin in
    /// a clockwise direction"). The unwrap is the same one the group-delay metric uses: a ±360°
    /// offset accumulated whenever the step between adjacent samples exceeds half a turn, which
    /// assumes the sweep resolves the locus to better than half a turn per step.</para>
    /// </summary>
    public static double[] Encirclements(IReadOnlyList<Complex> locus)
    {
        ArgumentNullException.ThrowIfNull(locus);
        int n = locus.Count;
        var enc = new double[n];
        if (n == 0) return enc;

        const double Deg = 180.0 / Math.PI;
        double prev = locus[0].Phase, offset = 0.0;
        enc[0] = -(prev * Deg) / 360.0;
        for (int k = 1; k < n; k++)
        {
            double p = locus[k].Phase;
            double d = p - prev;
            if (d > Math.PI) offset -= 2.0 * Math.PI;
            else if (d < -Math.PI) offset += 2.0 * Math.PI;
            prev   = p;
            enc[k] = -((p + offset) * Deg) / 360.0;
        }
        return enc;
    }
}
