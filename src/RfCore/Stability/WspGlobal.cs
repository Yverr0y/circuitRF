using System;
using System.Numerics;

namespace RfCore.Stability;

/// <summary>
/// The reduced admittance matrix among the probe nodes and the probe-based normalized determinant
/// function — the reference document's §8 (Eq. 181–186).
///
/// <para>T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023), §8;
/// brief-wsprobe-3 §5.</para>
/// </summary>
public static class WspGlobal
{
    /// <summary>
    /// The impedance matrix of the whole network seen at the probe <b>G nodes</b> under shunt
    /// stimuli, with the probes closed (they are shorts):
    /// <c>Z[j, k] = V_j / I_k = wsp(2k, 2j) = IV[k, j]</c>, i.e. <c>Z = IVᵀ</c> (Eq. 184; the
    /// transpose is overview D-9 — <c>wsp</c> is stimulus-major, the document's matrices are
    /// response-major).
    ///
    /// <para>Unlike a bifurcation this contains <b>both</b> sides of every probe, because a shunt
    /// injection at a closed probe excites the whole node. It is therefore the matrix the NDF wants
    /// and the matrix the stability envelope modifies.</para>
    /// </summary>
    public static Complex[,] Zmatrix(Complex[,] wsp, int[]? probes = null)
        => WspMatrix.Transpose(WspMatrix.Blocks(wsp, probes).IV);

    /// <summary><c>wsp_ymatrix(wsp, probes)</c> — <c>Y = Z⁻¹</c> of <see cref="Zmatrix"/> (Eq. 185):
    /// the reduced admittance matrix of the network at the probe nodes.</summary>
    public static Complex[,] Ymatrix(Complex[,] wsp, int[]? probes = null)
        => WspMatrix.Inverse(Zmatrix(wsp, probes));

    /// <summary>
    /// <c>wsp_ndf(wsp_active, wsp_passive, probes) = det(Z_passive) / det(Z)</c> (Eq. 186) — the
    /// normalized determinant function from two <c>wsp</c> matrices at one frequency, one from an
    /// ordinary run and one from a passivated run (brief-wsprobe-6's analysis knob). The probe-based
    /// route to NDF and the cross-check for the native one.
    ///
    /// <para>Both determinants are taken in log form (<see cref="WspMatrix.Lu"/> — the sum of the
    /// logs of the LU pivots), so a 30-probe matrix at 1,000 frequencies neither overflows nor
    /// underflows; only the RATIO is ever exponentiated.</para>
    /// </summary>
    public static Complex Ndf(Complex[,] wspActive, Complex[,] wspPassive, int[]? probes = null)
    {
        var lz  = new WspMatrix.Lu(Zmatrix(wspActive,  probes));
        var lzp = new WspMatrix.Lu(Zmatrix(wspPassive, probes));
        if (lz.Singular) return new Complex(double.NaN, double.NaN);
        if (lzp.Singular) return Complex.Zero;
        return Complex.FromPolarCoordinates(Math.Exp(lzp.LogAbsDet - lz.LogAbsDet), lzp.ArgDet - lz.ArgDet);
    }
}
