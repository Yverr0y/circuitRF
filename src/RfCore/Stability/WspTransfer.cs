using System;
using System.Numerics;

namespace RfCore.Stability;

/// <summary>Which of the probe's two auxiliary generators drives a remote transfer function.</summary>
public enum WspStimulus
{
    /// <summary>The series voltage <c>vS</c> — the odd row <c>2·idx−1</c>. App. C's own code, and
    /// the default.</summary>
    Series,
    /// <summary>The shunt current <c>iP</c> — the even row <c>2·idx</c>. Eq. 37's form.</summary>
    Shunt,
}

/// <summary>
/// The two remote (probe-pair) transfer functions of the reference document's appendices C and D:
/// the even-mode load-line impedance seen at one probe under a common stimulus at another, and the
/// power gain between two probes of the same device.
///
/// <para>T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023), §4.1 Eq. 37,
/// Eq. 196–201, App. C and D. These read <c>wsp</c> off-diagonal blocks but need no matrix algebra,
/// which is why they belong with the single-probe library rather than with the probe-pair
/// reduction.</para>
/// </summary>
public static class WspTransfer
{
    /// <summary>
    /// The even-mode load-line impedance at probe <paramref name="idxResponse"/> under a common
    /// stimulus applied at probe <paramref name="idxStimulus"/> (Eq. 196/197, App. C):
    ///
    /// <code>
    ///   series (App. C, default):  wsp(2·i−1, 2·j) / wsp(2·i−1, 2·j−1) = (V_j/vS_i) / (I_j/vS_i)
    ///   shunt  (Eq. 37):           wsp(2·i,   2·j) / wsp(2·i,   2·j−1) = (V_j/iP_i) / (I_j/iP_i)
    /// </code>
    ///
    /// <para>Both cancel the common stimulus and both are even-mode load lines; the document ships
    /// the series one as a function and prints the shunt one as Eq. 37. With
    /// <c>idxStimulus == idxResponse</c> this is the probe's own <c>ZL</c>-direction ratio.</para>
    ///
    /// <para>The value it answers is what a device at probe <c>j</c> actually looks into when every
    /// device of a symmetric combiner is driven in phase — the classic even-mode reduction, which
    /// is why one half of a two-way combiner with its common load doubled reproduces it exactly.</para>
    /// </summary>
    /// <param name="wsp">0-based storage of the document's 1-based matrix.</param>
    /// <param name="idxStimulus">The document's 1-based probe index the stimulus is applied at.</param>
    /// <param name="idxResponse">The document's 1-based probe index the response is read at.</param>
    /// <param name="stimulus">Which auxiliary generator drives it.</param>
    public static Complex Impedance(
        Complex[,] wsp, int idxStimulus, int idxResponse, WspStimulus stimulus = WspStimulus.Series)
    {
        int row = stimulus == WspStimulus.Series ? 2 * idxStimulus - 2 : 2 * idxStimulus - 1;
        var v   = wsp[row, 2 * idxResponse - 1];
        var i   = wsp[row, 2 * idxResponse - 2];
        return i == Complex.Zero ? new Complex(double.NaN, double.NaN) : v / i;
    }

    /// <summary>
    /// The power gain in dB between a gate probe and a drain probe of the same device, under a
    /// common series stimulus at probe <paramref name="idxStimulus"/> (App. D code; Eq. 198–201):
    ///
    /// <code>
    ///   GT = 10·log10 | Re( wsp(2S−1, 2D)·conj(wsp(2S−1, 2D−1)) )
    ///                 / Re( wsp(2S−1, 2G)·conj(wsp(2S−1, 2G−1)) ) |
    /// </code>
    ///
    /// <para>Each <c>Re(V·conj(I))</c> is the real power flowing at that probe, up to the common
    /// stimulus normalisation that cancels in the ratio.</para>
    ///
    /// <para><b>Typo register T-14:</b> Eq. 199 and 201 as printed use the <i>drain</i> index in
    /// both factors of the ratio. The App. D code is unambiguous — drain in the numerator, gate in
    /// the denominator — and Eq. 198/200 say the same. The code is what is implemented. The
    /// document's <c>log</c> is <c>log10</c>.</para>
    /// </summary>
    public static double GainDb(Complex[,] wsp, int idxStimulus, int idxGate, int idxDrain)
    {
        int row = 2 * idxStimulus - 2;
        double num = (wsp[row, 2 * idxDrain - 1] * Complex.Conjugate(wsp[row, 2 * idxDrain - 2])).Real;
        double den = (wsp[row, 2 * idxGate  - 1] * Complex.Conjugate(wsp[row, 2 * idxGate  - 2])).Real;
        return 10.0 * Math.Log10(Math.Abs(num / den));
    }
}
