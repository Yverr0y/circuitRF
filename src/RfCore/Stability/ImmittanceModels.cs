using System;
using System.Numerics;

namespace RfCore.Stability;

/// <summary>
/// The small immittance models and immittance-to-element conversions of the reference document's
/// appendices E.4 and E.11 — what a bidirectional impedance or admittance looks like as an R-C
/// series or parallel pair at one frequency.
///
/// <para>T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023), App. E.4 and
/// E.11.</para>
///
/// <para><b>Deliberate deviation, units only.</b> The document's <c>y_to_pc</c> returns picofarads
/// (a <c>1e12</c> inside the function) and its <c>y_to_pl</c> nanohenries (<c>1e9</c>). circuitRF
/// returns <b>farads and henries</b> — every trace and every derived metric here is base SI, and a
/// scale factor hidden inside a function is exactly the class of defect that once produced a run at
/// 2 Hz that looked entirely normal. The names are the document's, unchanged.</para>
///
/// <para>E.4's <c>freq + 1e-9</c> guard against <c>f = 0</c> is not reproduced either:
/// <see cref="Zsrc"/> at <c>f = 0</c> is NaN — a series capacitor at DC is an open, and saying so
/// is more useful than a number 1e9 times too large.</para>
/// </summary>
public static class ImmittanceModels
{
    private static readonly Complex NaN = new(double.NaN, double.NaN);

    private static double Omega(double freqHz) => 2.0 * Math.PI * freqHz;

    // ── E.4: the two two-element models ─────────────────────────────────────

    /// <summary>A series R-C impedance, <c>Z = R + 1/(jωC)</c> (E.4). NaN at <c>ω·C = 0</c>.</summary>
    public static Complex Zsrc(double r, double c, double freqHz)
    {
        double wc = Omega(freqHz) * c;
        return wc == 0.0 ? NaN : r + Complex.One / new Complex(0.0, wc);
    }

    /// <summary>A parallel R-C impedance, <c>Z = 1/(1/R + jωC)</c> (E.4). NaN when the admittance
    /// vanishes.</summary>
    public static Complex Zprc(double r, double c, double freqHz)
    {
        if (r == 0.0) return Complex.Zero;
        var y = new Complex(1.0 / r, Omega(freqHz) * c);
        return y == Complex.Zero ? NaN : Complex.One / y;
    }

    // ── E.11: admittance to element ─────────────────────────────────────────

    /// <summary>The parallel resistance of an admittance, <c>R = 1/Re(Y)</c> (E.11). Ohms.</summary>
    public static double YToPr(Complex y) => 1.0 / y.Real;

    /// <summary>The parallel capacitance of an admittance, <c>C = Im(Y)/ω</c> (E.11).
    /// <b>Farads</b>, not the document's picofarads.</summary>
    public static double YToPc(Complex y, double freqHz) => y.Imaginary / Omega(freqHz);

    /// <summary>The parallel inductance of an admittance, <c>L = −1/(Im(Y)·ω)</c> (E.11).
    /// <b>Henries</b>, not the document's nanohenries.</summary>
    public static double YToPl(Complex y, double freqHz) => -1.0 / (y.Imaginary * Omega(freqHz));

    /// <summary>The series resistance of an admittance, <c>R = Re(1/Y)</c> (E.11). Ohms.</summary>
    public static double YToSr(Complex y) => (Complex.One / y).Real;

    /// <summary>The series capacitance of an admittance, <c>C = −1/(Im(1/Y)·ω)</c> (E.11).
    /// <b>Farads.</b> Meaningful only where <c>Im(1/Y) &lt; 0</c> (a capacitive reactance).</summary>
    public static double YToSc(Complex y, double freqHz)
        => -1.0 / ((Complex.One / y).Imaginary * Omega(freqHz));

    /// <summary>The series inductance of an admittance, <c>L = Im(1/Y)/ω</c> (E.11).
    /// <b>Henries.</b></summary>
    public static double YToSl(Complex y, double freqHz) => (Complex.One / y).Imaginary / Omega(freqHz);

    // ── E.11 duals: impedance to element ────────────────────────────────────
    //
    // The document ships these as built-ins of its own; they are the E.11 functions of 1/Z, which
    // is the whole of their definition and the reason they are one line each here.

    /// <summary>The parallel resistance of an impedance — <c>y_to_pr(1/Z)</c> (E.11 dual). Ohms.</summary>
    public static double ZToPr(Complex z) => YToPr(Complex.One / z);

    /// <summary>The parallel capacitance of an impedance — <c>y_to_pc(1/Z, f)</c>. <b>Farads.</b></summary>
    public static double ZToPc(Complex z, double freqHz) => YToPc(Complex.One / z, freqHz);

    /// <summary>The parallel inductance of an impedance — <c>y_to_pl(1/Z, f)</c>. <b>Henries.</b></summary>
    public static double ZToPl(Complex z, double freqHz) => YToPl(Complex.One / z, freqHz);

    /// <summary>The series resistance of an impedance — <c>y_to_sr(1/Z)</c>, i.e. <c>Re(Z)</c>. Ohms.</summary>
    public static double ZToSr(Complex z) => YToSr(Complex.One / z);

    /// <summary>The series capacitance of an impedance — <c>y_to_sc(1/Z, f)</c>. <b>Farads.</b></summary>
    public static double ZToSc(Complex z, double freqHz) => YToSc(Complex.One / z, freqHz);

    /// <summary>The series inductance of an impedance — <c>y_to_sl(1/Z, f)</c>. <b>Henries.</b></summary>
    public static double ZToSl(Complex z, double freqHz) => YToSl(Complex.One / z, freqHz);
}
