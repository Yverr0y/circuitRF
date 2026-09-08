using System;
using System.Collections.Generic;
using System.Numerics;

namespace RfCore.Stability;

/// <summary>
/// Ohtomo's global loop gains over a set of probes — the reference document's §7 (Eq. 177–180) — and
/// the oscillation search on a loop gain (p. 110).
///
/// <para>T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023), §7;
/// brief-wsprobe-3 §4.</para>
///
/// <para><b>Prerequisite the function cannot check</b> (§7, p. 108): Ohtomo's method assumes each
/// subnetwork is stable on its own (no right-half-plane poles). The document's recommendation is one
/// side purely active — devices with no terminations that could form a loop — and the other purely
/// passive. The probes must all be oriented the same way (<see cref="WspBifurcation"/>).</para>
/// </summary>
public static class WspOhtomo
{
    /// <summary>
    /// <c>M = SP·SA − I</c> (Eq. 178), from the two bifurcated subnetworks as scattering matrices at
    /// <paramref name="z0"/>: <c>S_X = (I − Z0·Y_X)(I + Z0·Y_X)⁻¹</c> with <c>Y_X</c> the TRUE
    /// matrix of each side (<see cref="WspBifurcation.Bifurcate"/>). <c>SA</c> is the
    /// <paramref name="active"/> side, <c>SP</c> the other.
    /// </summary>
    public static Complex[,] M(Complex[,] wsp, int[]? probes, WspSide active, Complex z0)
    {
        var passive = active == WspSide.G ? WspSide.L : WspSide.G;
        var sa = WspMatrix.ScatteringOfY(WspBifurcation.Bifurcate(wsp, WspForm.Y, active,  probes), z0);
        var sp = WspMatrix.ScatteringOfY(WspBifurcation.Bifurcate(wsp, WspForm.Y, passive, probes), z0);
        return WspMatrix.Subtract(WspMatrix.Multiply(sp, sa), WspMatrix.Identity(sa.GetLength(0)));
    }

    /// <summary>
    /// <c>wsp_loopgain_ohtomo(wsp, probes [, active = "G", Z0 = 50])</c> — the <c>N</c> sequential
    /// loop gains (Eq. 179, with Eq. 180 corrected — typo register T-10):
    ///
    /// <code>
    ///   G_i = 1 + |M_{N−i+1}| / |M_{N−i}|,   i = 1 … N,   M_0 ≡ 1
    ///   M_{N−i+1} = the trailing principal submatrix of M on rows and columns i … N
    /// </code>
    ///
    /// <para>so <c>G_N = 1 + M_{N,N}</c>, <c>G_{N−1} = 1 + det(M[N−1..N, N−1..N]) / M_{N,N}</c>, …,
    /// <c>G_1 = 1 + det(M)/det(M[2..N, 2..N])</c>. Sanity: <c>N = 1</c> gives
    /// <c>G_1 = SP·SA = ΓP·ΓA</c>, Jackson's stability index.</para>
    ///
    /// <para><b>Why the bookkeeping cannot be wrong if one identity holds:</b> telescoping,
    /// <c>Π_{i=1..N} (G_i − 1) = det(M)</c>, and <c>det(M) = det(SP·SA − I)</c> is the Nyquist
    /// determinant of the closed loop of travelling waves, so the sum of the encirclements of
    /// <c>+1</c> by the <c>G_i</c> equals the encirclements of the origin by <c>det(M)</c>. That is
    /// what makes the method global. The choice of <paramref name="active"/> side and the ORDER of the
    /// probe list change the individual <c>G_i</c> — they are the sequential loop gains with the
    /// earlier loops closed — but not that sum.</para>
    /// </summary>
    /// <returns><c>G_1 … G_N</c>, one per probe in list order.</returns>
    public static Complex[] LoopGains(Complex[,] wsp, int[]? probes, WspSide active, Complex z0)
        => LoopGainsFromM(M(wsp, probes, active, z0));

    /// <summary>Eq. 179/180 on a given <c>M</c>.</summary>
    public static Complex[] LoopGainsFromM(Complex[,] m)
    {
        int n = m.GetLength(0);
        var g = new Complex[n];
        // det of the trailing principal submatrix starting at row/col k (0-based); det(empty) = 1.
        var det = new Complex[n + 1];
        det[n] = Complex.One;
        for (int k = n - 1; k >= 0; k--)
            det[k] = WspMatrix.Determinant(WspMatrix.TrailingPrincipal(m, k));
        for (int i = 0; i < n; i++)
            g[i] = Complex.One + det[i] / det[i + 1];
        return g;
    }

    /// <summary>
    /// <c>wsp_unstable_freq_loopgain(G)</c> — the frequencies at which a loop gain shows the
    /// oscillation signature (p. 110): <c>|G| ≥ 1</c> with <c>∠G = 0</c> crossed <b>clockwise</b> —
    /// the positive real axis, decreasing argument, so <c>Im(G)</c> going from positive to negative.
    /// The counterpart of <see cref="WspKurokawa.UnstableFrequencies"/> with the critical point
    /// <c>+1</c> instead of the origin's negative axis: the same linear interpolation between
    /// consecutive samples, the same "empty means no crossing was sampled" caveat, the same use for
    /// <c>LG</c>, the circulator loop gains and Ohtomo's <c>G_i</c> alike.
    /// </summary>
    /// <param name="g">The loop gain, sample by sample (NOT its reciprocal).</param>
    /// <param name="x">The sweep variable at each sample (Hz), ascending.</param>
    public static double[] UnstableFrequenciesLoopGain(IReadOnlyList<Complex> g, IReadOnlyList<double> x)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(x);
        int n = Math.Min(g.Count, x.Count);
        if (n < 2) return [];

        var hits = new List<double>();

        if (g[0].Imaginary == 0.0 && g[1].Imaginary < 0.0 && g[0].Real >= 1.0)
            hits.Add(x[0]);

        for (int k = 0; k + 1 < n; k++)
        {
            double i0 = g[k].Imaginary, i1 = g[k + 1].Imaginary;
            if (double.IsNaN(i0) || double.IsNaN(i1)) continue;
            if (!(i0 > 0.0 && i1 <= 0.0)) continue;          // clockwise crossings only

            double span = i1 - i0;
            double frac = span == 0.0 ? 0.0 : -i0 / span;
            double xc   = x[k] + frac * (x[k + 1] - x[k]);
            double re   = g[k].Real + frac * (g[k + 1].Real - g[k].Real);
            if (re >= 1.0) hits.Add(xc);
        }

        hits.Sort();
        return [.. hits];
    }
}
