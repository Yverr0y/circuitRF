using System;
using System.Numerics;
using NumFlat;

namespace RfCore.Stability;

/// <summary>
/// The two renormalisations of the reference document's App. E.10 — a two-port's S-parameters seen
/// against the bidirectional impedances the probe actually measured, rather than against a nominal
/// reference.
///
/// <para>T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023), App. E.10.</para>
/// </summary>
public static class WspRenorm
{
    /// <summary>The diagnostic key both functions refuse a non-positive reference resistance with.</summary>
    public const string NegativeReferenceKey = "wsprobe.renorm-negative-reference";

    /// <summary>The reference the document's E.10 renormalises <i>from</i>: its <c>stoy(SP, 50)</c>
    /// is written against a uniform real 50 Ω, and both functions here keep that as the default
    /// source reference rather than inventing one.</summary>
    public const double DocumentSourceReferenceOhm = 50.0;

    /// <summary>
    /// The two-port renormalised to the complex references <paramref name="zg"/> and
    /// <paramref name="zl"/> (E.10 <c>wsp_zo_renorm_s</c>) — the power-wave (Kurokawa)
    /// renormalisation, which is what E.10's matrix expression
    /// <c>S' = F·(I − conj(Zr)·Y)·(I + Zr·Y)⁻¹·F⁻¹</c> with <c>F = diag(1/(2√Re Z))</c> is.
    ///
    /// <para>circuitRF does not transcribe that expression: it calls
    /// <see cref="RFNetwork.SToS(Mat{Complex}, Complex[], Complex[])"/>, the one implementation of
    /// complex-reference renormalisation in the repository — the same one the Data Display's Z0
    /// override path uses. Equality with the transcribed E.10 form is a test, not an
    /// assumption.</para>
    ///
    /// <para><b>Refusal.</b> The document writes <c>abs(real(RG))</c> under its square root, which
    /// silently accepts a negative reference resistance. circuitRF refuses
    /// (<see cref="NegativeReferenceKey"/>): a reference impedance with <c>Re ≤ 0</c> is a wrong
    /// input, not a case — the power-wave definition divides by <c>√Re(Z0)</c>.</para>
    /// </summary>
    /// <param name="zg">The new port-1 reference (the probe's <c>ZG</c>).</param>
    /// <param name="s">The two-port at <paramref name="z0Src"/>.</param>
    /// <param name="zl">The new port-2 reference (the probe's <c>ZL</c>).</param>
    /// <param name="z0Src">The reference <paramref name="s"/> is given at; the document's 50 Ω.</param>
    public static Mat<Complex> ZoRenormS(
        Complex zg, Mat<Complex> s, Complex zl, Complex? z0Src = null)
    {
        RequirePositiveReal(zg, "ZG");
        RequirePositiveReal(zl, "ZL");
        var src = z0Src ?? new Complex(DocumentSourceReferenceOhm, 0.0);
        return RFNetwork.SToS(s, [src, src], [zg, zl]);
    }

    /// <summary>
    /// The two-port with a shunt capacitance absorbed at each port and then renormalised to the
    /// real port resistances (E.10 <c>wsp_rc_renorm_s</c>) — the two steps it is:
    ///
    /// <code>
    ///   Y  ←  stoy(SP, 50) + diag(jωCG, jωCL)          absorb the parallel capacitances
    ///   S' ←  renormalise ytos(Y, 50) from 50 Ω to diag(RG, RL)
    /// </code>
    ///
    /// <para>The point of it, in the document's own words, is that "the S-parameters are normalized
    /// to the real port resistance and not a complex impedance": the reactive part of each
    /// bidirectional immittance becomes part of the network, and what is left to normalise against
    /// is real.</para>
    ///
    /// <para>A non-positive <paramref name="rg"/> or <paramref name="rl"/> is refused
    /// (<see cref="NegativeReferenceKey"/>).</para>
    /// </summary>
    /// <param name="rg">Port-1 reference resistance, Ω.</param>
    /// <param name="cg">Port-1 shunt capacitance, F.</param>
    /// <param name="s">The two-port at <paramref name="z0Src"/>.</param>
    /// <param name="rl">Port-2 reference resistance, Ω.</param>
    /// <param name="cl">Port-2 shunt capacitance, F.</param>
    /// <param name="freqHz">The frequency the capacitances are evaluated at.</param>
    /// <param name="z0Src">The reference <paramref name="s"/> is given at; the document's 50 Ω.</param>
    public static Mat<Complex> RcRenormS(
        double rg, double cg, Mat<Complex> s, double rl, double cl, double freqHz,
        Complex? z0Src = null)
    {
        RequirePositiveReal(rg, "RG");
        RequirePositiveReal(rl, "RL");

        var src = z0Src ?? new Complex(DocumentSourceReferenceOhm, 0.0);
        double w = 2.0 * Math.PI * freqHz;

        var y = RFNetwork.SToY(s, src);
        y[0, 0] += new Complex(0.0, w * cg);
        y[1, 1] += new Complex(0.0, w * cl);

        return RFNetwork.SToS(RFNetwork.YToS(y, src), [src, src], [new Complex(rg, 0.0), new Complex(rl, 0.0)]);
    }

    private static void RequirePositiveReal(Complex z, string what)
    {
        if (z.Real > 0.0) return;
        throw new ArgumentException(
            $"{NegativeReferenceKey}: {what} has Re = {z.Real:G6} Ω. A reference impedance must have " +
            "Re > 0 — the power-wave definition divides by √Re(Z0). The reference document takes the " +
            "absolute value here and carries on; circuitRF does not, because a negative reference " +
            "resistance is a wrong input rather than a case.", what);
    }

    private static void RequirePositiveReal(double r, string what)
        => RequirePositiveReal(new Complex(r, 0.0), what);
}
