using System;
using System.Numerics;
using NumFlat;

namespace RfCore.Stability;

/// <summary>
/// The four power gains of the reference document's App. E.1, all in dB, for one two-port at one
/// frequency with one source and one load reflection coefficient.
/// </summary>
/// <param name="GtDb">Transducer power gain, Eq. 207.</param>
/// <param name="GpDb">Operating (power) gain, Eq. 205.</param>
/// <param name="GaDb">Available power gain, Eq. 206.</param>
/// <param name="GmaxDb">MAG when the two-port is unconditionally stable, MSG when it is not, Eq. 208.</param>
public readonly record struct GainDefs(double GtDb, double GpDb, double GaDb, double GmaxDb);

/// <summary>
/// The gain definitions of the reference document's App. E.1 and the <c>_dB</c> of E.2.
///
/// <para>T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023), Eq. 203–208 and
/// App. E.1–E.2. The formulas are textbook; what makes them belong to the probe is where the two
/// reflection coefficients come from — E.1's own recommendation is the probe's <c>ZG</c> and
/// <c>ZL</c> converted to <c>Γ</c> against the network's reference, which is a gain the node
/// actually sees rather than one against a nominal 50 Ω.</para>
/// </summary>
public static class GainDefinitions
{
    /// <summary>The document's <c>_dB(M) = 10·log10|M|</c> (E.2) — a POWER dB, so 10 and not 20.</summary>
    public static double Db(Complex m) => 10.0 * Math.Log10(m.Magnitude);

    /// <inheritdoc cref="Db(Complex)"/>
    public static double Db(double m) => 10.0 * Math.Log10(Math.Abs(m));

    /// <summary>The input reflection coefficient with the load terminated in <paramref name="gamL"/>,
    /// <c>Γin = S11 + S12·S21·ΓL/(1 − S22·ΓL)</c> (Eq. 203).</summary>
    public static Complex GammaIn(Complex s11, Complex s12, Complex s21, Complex s22, Complex gamL)
        => s11 + s12 * s21 * gamL / (Complex.One - s22 * gamL);

    /// <summary>The output reflection coefficient with the source terminated in <paramref name="gamS"/>,
    /// <c>Γout = S22 + S12·S21·ΓS/(1 − S11·ΓS)</c> (Eq. 204).</summary>
    public static Complex GammaOut(Complex s11, Complex s12, Complex s21, Complex s22, Complex gamS)
        => s22 + s12 * s21 * gamS / (Complex.One - s11 * gamS);

    /// <summary>
    /// <c>GT</c>, <c>GP</c>, <c>GA</c> and <c>Gmax</c> in dB (Eq. 205–208):
    ///
    /// <code>
    ///   GP   = |S21|²·(1 − |ΓL|²) / [ (1 − |Γin|²)  · |1 − S22·ΓL|² ]      (Eq. 205)
    ///   GA   = |S21|²·(1 − |ΓS|²) / [ (1 − |Γout|²) · |1 − S11·ΓS|² ]      (Eq. 206)
    ///   GT   = |S21|²·(1 − |ΓS|²)·(1 − |ΓL|²) / [ |1 − Γin·ΓS|² · |1 − S22·ΓL|² ]   (Eq. 207)
    ///   Gmax = MAG (K ≥ 1) or MSG (K &lt; 1)                                 (Eq. 208)
    /// </code>
    ///
    /// <para><c>Gmax</c> calls <see cref="RFNetwork.MaxGainLinear(Mat{Complex})"/> — the one
    /// implementation of MAG/MSG in the repository, the same one the Data Display's Max Gain trace
    /// and the derived-metrics page document. It depends on the two-port alone, not on
    /// <paramref name="gamS"/> or <paramref name="gamL"/>.</para>
    /// </summary>
    /// <param name="gamS">The source reflection coefficient (E.1: the probe's <c>ZG</c> as Γ).</param>
    /// <param name="s11">The two-port, at a uniform real reference.</param>
    /// <param name="gamL">The load reflection coefficient (E.1: the probe's <c>ZL</c> as Γ).</param>
    public static GainDefs Compute(
        Complex gamS, Complex s11, Complex s12, Complex s21, Complex s22, Complex gamL)
    {
        var gin  = GammaIn(s11, s12, s21, s22, gamL);
        var gout = GammaOut(s11, s12, s21, s22, gamS);

        double s21sq = s21.Magnitude * s21.Magnitude;
        double ms    = gamS.Magnitude * gamS.Magnitude;
        double ml    = gamL.Magnitude * gamL.Magnitude;

        double gp = s21sq * (1.0 - ml)
                  / ((1.0 - gin.Magnitude * gin.Magnitude) * Sq(Complex.One - s22 * gamL));
        double ga = s21sq * (1.0 - ms)
                  / ((1.0 - gout.Magnitude * gout.Magnitude) * Sq(Complex.One - s11 * gamS));
        double gt = s21sq * (1.0 - ms) * (1.0 - ml)
                  / (Sq(Complex.One - gin * gamS) * Sq(Complex.One - s22 * gamL));

        var m = new Mat<Complex>(2, 2);
        m[0, 0] = s11; m[0, 1] = s12;
        m[1, 0] = s21; m[1, 1] = s22;
        double gmax = RFNetwork.MaxGainLinear(m);

        return new GainDefs(Db(gt), Db(gp), Db(ga), Db(gmax));
    }

    /// <summary>Γ of an impedance against a reference — E.1's recommended way of turning the
    /// probe's <c>ZG</c>/<c>ZL</c> into the <c>ΓS</c>/<c>ΓL</c> the gains take.</summary>
    public static Complex GammaOf(Complex z, Complex z0) => (z - z0) / (z + z0);

    private static double Sq(Complex z) => z.Magnitude * z.Magnitude;
}
