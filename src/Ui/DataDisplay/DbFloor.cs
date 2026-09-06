namespace CircuitRF.Ui.DataDisplay;

/// <summary>
/// The display floor for every decibel conversion the Data Display performs.
///
/// <para><b>Why this exists.</b> A quantity that is analytically zero does not come back as zero —
/// it comes back as double-precision roundoff, and roundoff has no sign, no continuity and no
/// scale. An ideal 50 Ω line terminated in 50 Ω has S(1,1) ≡ 0, and the solver duly returns
/// magnitudes scattered between ~1e-17 and ~5e-15 with a handful of exact zeros mixed in. Rendered
/// through a bare <c>20·log10</c> that is a trace bouncing between −344 dB and −285 dB with spikes
/// to −∞ — 59 dB of noise plus an infinite cliff, on a plot whose real content is the OTHER
/// trace. Autoscale then sizes the axis to the noise and nothing legible survives.</para>
///
/// <para><b>What it does.</b> Clamps the result to <see cref="FloorDb"/>, so a quantity below the
/// floor renders as a flat line at the floor instead of as noise. Readouts that print a clamped
/// value mark it — see <see cref="Format"/> — because the number is a floor, not a measurement.</para>
///
/// <para><b>Why the clamp is applied in dB and not in linear.</b> This code previously floored the
/// linear magnitude at <c>1e-300</c>, which puts dB20 on a cliff at −6000 dB and dB10 on a
/// different one at −3000 dB — two floors for one dataset, differing only by the factor in front of
/// the log. Clamping after the log gives every transform the same bottom.</para>
///
/// <para><b>Why −250 dB.</b> It has to sit in the dead band between the smallest result that can
/// mean anything and the largest number roundoff can produce, and that band is narrow at the
/// bottom:</para>
/// <list type="bullet">
///   <item><description><b>−300 dB is too low.</b> Measured on a matched-line S(1,1), 4 of 101
///   roundoff points land ABOVE −300 dB, so a −300 dB floor still leaves 15 dB of visible
///   jitter. Roundoff on a unity-scaled quantity reaches −285 dB.</description></item>
///   <item><description><b>−200 dB is too high.</b> A dB10 of an absolute power in WATTS reaches
///   there legitimately: kTB at 1 Hz is −204 dB(W), and this application's own "PIM off" default of
///   −200 dBm is −230 dB(W). A −200 dB floor would clip physics.</description></item>
/// </list>
/// <para>−250 dB clears the roundoff band by ~35 dB and sits ~46 dB below thermal noise in watts.</para>
///
/// <para><b>Display only.</b> Nothing here is applied to <c>DataCube.Db/Db10/Log10</c>,
/// <c>RFNetwork</c> or the loadpull derived fields. Those numbers are exported (.npy/.mat/
/// Touchstone) and are read back by measurement expressions, so clamping them would make a value
/// depend on whether it happened to pass through a plot. A <c>dB(S(1,1))</c> measurement therefore
/// still reports the unclamped number; only the picture is floored.</para>
/// </summary>
public static class DbFloor
{
    /// <summary>The floor, in dB. See the type remarks for why it is this value and not −200
    /// or −300.</summary>
    public const double FloorDb = -250.0;

    /// <summary>20·log10(magnitude), floored. <paramref name="magnitude"/> must be non-negative
    /// (a magnitude or an <c>Math.Abs</c>); zero maps to the floor rather than to −∞.</summary>
    public static double Db20(double magnitude) => Clamp(20.0 * Math.Log10(magnitude));

    /// <summary>10·log10(magnitude), floored — the power-ratio form, sharing dB20's floor rather
    /// than getting its own at half the depth.</summary>
    public static double Db10(double magnitude) => Clamp(10.0 * Math.Log10(magnitude));

    /// <summary>Whether a dB value came back at (or below) the floor, i.e. the underlying quantity
    /// was too small to distinguish from zero.</summary>
    public static bool IsAtFloor(double db) => db <= FloorDb;

    /// <summary>What <see cref="Format"/> puts in front of a floored value. Public because
    /// <see cref="Label"/> is not the only place a caller may need to recognise one.</summary>
    public const string FlooredPrefix = "\u2264 ";

    /// <summary>Renders a dB value for a READOUT (marker, table, tooltip), prefixing "≤" when the
    /// value is the floor rather than a measurement. Without the mark, a column of "-250.00" reads
    /// as a suspiciously repeatable result instead of as "below what can be resolved".</summary>
    public static string Format(double db, string fmt) =>
        IsAtFloor(db) ? FlooredPrefix + FloorDb.ToString(fmt) : db.ToString(fmt);

    /// <summary>
    /// Joins a readout label to a formatted value, using the value's OWN relational operator when it
    /// carries one. A marker info box writes "name=value"; a floored value already begins with "≤",
    /// and gluing the two gives "name=≤ -250", which reads as a typo rather than as an inequality.
    /// The floor mark IS the relation, so it replaces the "=" instead of following it —
    /// "name ≤ -250".
    /// </summary>
    public static string Label(string desc, string value) =>
        value.StartsWith(FlooredPrefix, StringComparison.Ordinal)
            ? $"{desc} {value}"
            : $"{desc}={value}";

    /// <summary><see cref="Math.Max"/> propagates NaN, which is what the callers' own
    /// <c>double.IsFinite</c> point-skipping relies on; −∞ (an exact zero) clamps to the floor.</summary>
    private static double Clamp(double db) => Math.Max(db, FloorDb);
}
