using System;
using System.Numerics;

namespace RfCore.Stability;

/// <summary>The eight single-probe loop gains of the reference document (§4.6, §4.8).</summary>
/// <remarks>
/// T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023), Eq. 92, 97, 99–104.
/// Every one of them is <b>incomplete</b> — the document says so of all of them (§4.4, p. 50: "not
/// fundamental circuit quantities and are therefore not rigorous stability measurements"). They are
/// diagnostic. The primary stability metric is the driving-point pair <c>1/H0</c>, <c>1/Y0</c> and
/// Kurokawa's start-up signature (<see cref="WspKurokawa"/>).
/// </remarks>
public enum WspLoopGainKind
{
    /// <summary><c>"BI"</c> — the bilateral (Tian) loop gain, Eq. 92.</summary>
    Bi,
    /// <summary><c>"UNI"</c> (alias <c>"FOR"</c>) — the forward synthetic-circulator loop gain, Eq. 99.
    /// The author "had hoped to" call it FOR (p. 68), so both spellings are accepted.</summary>
    Uni,
    /// <summary><c>"REV"</c> — the reverse synthetic-circulator loop gain, Eq. 97.</summary>
    Rev,
    /// <summary><c>"HST"</c> — Hurst, Eq. 100, implemented with the printed minus sign (T-6).</summary>
    Hst,
    /// <summary><c>"MB"</c> — Middlebrook forward, Eq. 101.</summary>
    Mb,
    /// <summary><c>"MBR"</c> — Middlebrook reverse, Eq. 102.</summary>
    Mbr,
    /// <summary><c>"GFT"</c> — general feedback theorem, forward, Eq. 103.</summary>
    Gft,
    /// <summary><c>"GFTR"</c> — general feedback theorem, reverse, Eq. 104.</summary>
    Gftr,
}

/// <summary>
/// The single-probe nodal quantities of the reference document's function library — the
/// bidirectional immittances (App. E.14), the open-port immittances (App. E.16), all eight loop
/// gains (§4.6, §4.8), the nodal conjugate reflection coefficient (App. E.7), circuitRF's own
/// normalised driving-point loci, and the slot reserved for the published stability margin.
///
/// <para>Pure functions over <see cref="Complex"/> and the two reduced two-ports of
/// <see cref="WspReduction"/>; nothing here touches a cube, a netlist or a solve (overview D-2).
/// The bidirectional immittances themselves live in <see cref="WspReduction"/>, because the engine
/// computes a run's default <c>ZG:</c>/<c>ZL:</c> cubes with them; the wrappers here exist so that
/// every name the document gives is spelled once, in one place, and resolves to that one
/// implementation.</para>
///
/// <para>Reference throughout: T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i>
/// (2023).</para>
/// </summary>
public static class WspNodal
{
    private static readonly Complex NaN = new(double.NaN, double.NaN);

    // ── Driving-point and bidirectional immittances (E.14, E.15) ─────────────

    /// <summary>The driving-point impedance <c>H0 = vP/iP = wsp(2·idx, 2·idx)</c> (Eq. 50, 132).</summary>
    public static Complex H0(in WspProbeQuad q) => WspReduction.H0(q);

    /// <summary>The driving-point admittance <c>Y0 = iS/vS = wsp(2·idx−1, 2·idx−1)</c> (Eq. 52, 134).</summary>
    public static Complex Y0(in WspProbeQuad q) => WspReduction.Y0(q);

    /// <summary>
    /// The bidirectional generator-side impedance <c>ZG = z11 − z12 = −(1/Y0)·(vP/vS)</c>
    /// (Eq. 67; E.14 <c>wsp_ZG = ZP(1,1) − ZP(1,2)</c>).
    ///
    /// <para><b><c>ZG</c> is not <c>1/YG</c>.</b> <c>ZG</c> and <c>ZL</c> are the impedances the two
    /// sides present under <b>series-voltage</b> stimulation; <c>YG</c> and <c>YL</c> are the
    /// admittances they present under <b>shunt-current</b> stimulation. They agree only when there
    /// is no feedback across the probe, <c>y12 = y21 = 0</c> (Eq. 79–82; §4.5, p. 56–57). Reading
    /// one as the reciprocal of the other is the single most common misreading of the probe. Only
    /// the sums are fundamental: <c>ZG + ZL = 1/Y0</c> (Eq. 69) and <c>YG + YL = 1/H0</c>
    /// (Eq. 77).</para>
    /// </summary>
    public static Complex ZG(in WspProbeQuad q) => WspReduction.ZG(q);

    /// <summary>The bidirectional load-side impedance <c>ZL = z22 − z21 = (1/Y0)·(1 + vP/vS)</c>
    /// (Eq. 68; E.14 <c>wsp_ZL = ZP(2,2) − ZP(2,1)</c>). Not <c>1/YL</c> — see
    /// <see cref="ZG(in WspProbeQuad)"/>.</summary>
    public static Complex ZL(in WspProbeQuad q) => WspReduction.ZL(q);

    /// <summary>
    /// The bidirectional generator-side admittance <c>YG = y11 + y12 = (1/H0)·(1 − iS/iP)</c>
    /// (Eq. 76; E.14 <c>wsp_YG = YP(1,1) + YP(1,2)</c>).
    ///
    /// <para>The document notes that <c>YG</c>/<c>YL</c> are "NOT outputs of the WSProbes" — that
    /// describes its own implementation, not the quantity. Here they are first-class functions and
    /// Data Display items; an independent public re-implementation of the 2018 probe found them
    /// useful enough to add.</para>
    /// </summary>
    public static Complex YG(in WspTwoPortY y) => WspReduction.YG(y);

    /// <summary>The bidirectional load-side admittance <c>YL = y22 + y21 = (1/H0)·(iS/iP)</c>
    /// (Eq. 76; E.14 <c>wsp_YL = YP(2,2) + YP(2,1)</c>).</summary>
    public static Complex YL(in WspTwoPortY y) => WspReduction.YL(y);

    // ── Open-port immittances (§4.7, E.16) ──────────────────────────────────

    /// <summary>
    /// Ochoa's open-loop port impedance <c>Zop = 1/(y11 + y22) = H0|_{y12 = y21 = 0}</c> (Eq. 89).
    ///
    /// <para>The part of <c>1/H0</c> that survives when the bilateral feedback across the probe is
    /// removed (§4.7), and therefore the part that carries an instability <i>between</i> two
    /// otherwise-unconnected blocks — Kurokawa's own case, where every loop gain reads zero
    /// (p. 62–63). <c>1/H0 = (1 − LG)/Zop</c> (Eq. 93).</para>
    ///
    /// <para>The document's E.16 adds <c>1e-15</c> to the denominator; circuitRF does not
    /// (overview D-7) — an exactly vanishing <c>y11 + y22</c> returns NaN.</para>
    /// </summary>
    public static Complex Zop(in WspTwoPortY y)
    {
        var d = y.Y11 + y.Y22;
        return d == Complex.Zero ? NaN : Complex.One / d;
    }

    /// <summary>
    /// Ochoa's open-loop port admittance <c>Yop = 1/(z11 + z22) = Y0|_{z12 = z21 = 0}</c> (Eq. 90).
    /// <c>1/Y0 = (1 − LG)/Yop</c> (Eq. 93). No epsilon is added to the denominator.
    /// </summary>
    public static Complex Yop(in WspTwoPortZ z)
    {
        var d = z.Z11 + z.Z22;
        return d == Complex.Zero ? NaN : Complex.One / d;
    }

    // ── Loop gains (§4.6, §4.8) ─────────────────────────────────────────────

    /// <summary>
    /// The document's kind string for a loop gain, exactly as it spells it, with <c>"FOR"</c>
    /// accepted as an alias of <c>"UNI"</c> (p. 68). Case-insensitive; anything else throws with
    /// the legal spellings listed.
    /// </summary>
    public static WspLoopGainKind ParseKind(string kind) => kind?.Trim().ToUpperInvariant() switch
    {
        "BI"   => WspLoopGainKind.Bi,
        "UNI"  => WspLoopGainKind.Uni,
        "FOR"  => WspLoopGainKind.Uni,
        "REV"  => WspLoopGainKind.Rev,
        "HST"  => WspLoopGainKind.Hst,
        "MB"   => WspLoopGainKind.Mb,
        "MBR"  => WspLoopGainKind.Mbr,
        "GFT"  => WspLoopGainKind.Gft,
        "GFTR" => WspLoopGainKind.Gftr,
        _ => throw new ArgumentException(
            $"wsp_loopgain: unknown kind '{kind}'. Kinds are BI (bilateral/Tian), UNI (forward " +
            "circulator; FOR is an accepted alias), REV (reverse circulator), HST (Hurst), " +
            "MB / MBR (Middlebrook forward/reverse), GFT / GFTR (general feedback theorem " +
            "forward/reverse).", nameof(kind)),
    };

    /// <summary>
    /// One of the eight single-probe loop gains, from the reduced two-port <c>[Y]</c> at the probe
    /// (the document's own calling pattern, p. 68). <paramref name="z0"/> is the reference the two
    /// circulator kinds normalise by, <c>ȳ = Z0·y</c>; the other six ignore it.
    ///
    /// <code>
    ///  BI    LG     = −(y12 + y21)/(y11 + y22)                                     (Eq. 92)
    ///  UNI   LGF    = [1 − ȳ22 − ȳ12(ȳ21 + 2) + ȳ11(ȳ22 − 1)]
    ///                 / [1 + ȳ22 − ȳ21(ȳ12 − 2) + ȳ11(ȳ22 + 1)]                    (Eq. 99)
    ///  REV   LGR    = [1 − ȳ22 − ȳ21(ȳ12 + 2) + ȳ11(ȳ22 − 1)]
    ///                 / [1 + ȳ22 − ȳ12(ȳ21 − 2) + ȳ11(ȳ22 + 1)]                    (Eq. 97)
    ///  HST   LG_H   = −y21·y12/(y11·y22)                                            (Eq. 100)
    ///  MB    LG_MF  = −(y21 − y12)/(y11 + y22 + 2·y12)                              (Eq. 101)
    ///  MBR   LG_MR  = −(y12 − y21)/(y11 + y22 + 2·y21)                              (Eq. 102)
    ///  GFT   LG_MGF = −y21/(y11 + y22 + y12)                                        (Eq. 103)
    ///  GFTR  LG_MGR = −y12/(y11 + y22 + y21)                                        (Eq. 104)
    /// </code>
    ///
    /// <para>The two circulator forms are the reflection coefficient the third port of an ideal
    /// circulator inserted at the probe would see; their S-parameter equivalents
    /// <c>LGR = S21 + S11·S22/(1 − S12)</c> and <c>LGF = S12 + S11·S22/(1 − S21)</c> (Eq. 96, 98,
    /// with <c>S = (I − ȳ)(I + ȳ)⁻¹</c>) were verified numerically against these to 2e-15 on random
    /// two-ports and are kept only as the test oracle.</para>
    ///
    /// <para><b>T-6:</b> Eq. 100 is printed with a leading minus while the two-block Hurst form
    /// (Eq. 141/150) has none. The minus follows the convention of Eq. 53 (<c>T ≡ −Vp/Vx</c>,
    /// <c>LG = −T</c>); it was not independently re-derived. Implemented as printed.</para>
    /// </summary>
    public static Complex LoopGain(in WspTwoPortY y, WspLoopGainKind kind, Complex z0)
    {
        switch (kind)
        {
            case WspLoopGainKind.Bi:
                return WspReduction.LoopGain(y);

            case WspLoopGainKind.Uni:
            {
                var (a, b, c, d) = Normalized(y, z0);
                return (Complex.One - d - b * (c + 2.0) + a * (d - Complex.One))
                     / (Complex.One + d - c * (b - 2.0) + a * (d + Complex.One));
            }

            case WspLoopGainKind.Rev:
            {
                var (a, b, c, d) = Normalized(y, z0);
                return (Complex.One - d - c * (b + 2.0) + a * (d - Complex.One))
                     / (Complex.One + d - b * (c - 2.0) + a * (d + Complex.One));
            }

            case WspLoopGainKind.Hst:
                return -(y.Y21 * y.Y12) / (y.Y11 * y.Y22);

            case WspLoopGainKind.Mb:
                return -(y.Y21 - y.Y12) / (y.Y11 + y.Y22 + 2.0 * y.Y12);

            case WspLoopGainKind.Mbr:
                return -(y.Y12 - y.Y21) / (y.Y11 + y.Y22 + 2.0 * y.Y21);

            case WspLoopGainKind.Gft:
                return -y.Y21 / (y.Y11 + y.Y22 + y.Y12);

            case WspLoopGainKind.Gftr:
                return -y.Y12 / (y.Y11 + y.Y22 + y.Y21);

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "unhandled loop-gain kind");
        }
    }

    /// <summary>The normalised admittances <c>ȳ = Z0·y</c> the two circulator forms are written in.</summary>
    private static (Complex Y11, Complex Y12, Complex Y21, Complex Y22) Normalized(in WspTwoPortY y, Complex z0)
        => (z0 * y.Y11, z0 * y.Y12, z0 * y.Y21, z0 * y.Y22);

    /// <summary>
    /// The test oracle for <see cref="WspLoopGainKind.Rev"/>: <c>LGR = S21 + S11·S22/(1 − S12)</c>
    /// (Eq. 96), with <c>S</c> the scattering matrix of <c>ȳ</c>. Never the shipped path.
    /// </summary>
    public static Complex LoopGainRevFromS(in WspTwoPortY y, Complex z0)
    {
        var s = ScatteringOfNormalized(y, z0);
        return s.S21 + s.S11 * s.S22 / (Complex.One - s.S12);
    }

    /// <summary>
    /// The test oracle for <see cref="WspLoopGainKind.Uni"/>: <c>LGF = S12 + S11·S22/(1 − S21)</c>
    /// (Eq. 98). Never the shipped path.
    /// </summary>
    public static Complex LoopGainFwdFromS(in WspTwoPortY y, Complex z0)
    {
        var s = ScatteringOfNormalized(y, z0);
        return s.S12 + s.S11 * s.S22 / (Complex.One - s.S21);
    }

    /// <summary><c>S = (I − ȳ)(I + ȳ)⁻¹</c> for the 2×2 normalised admittance — the reduced
    /// two-port seen at a uniform real reference. Test oracle only.</summary>
    private static (Complex S11, Complex S12, Complex S21, Complex S22)
        ScatteringOfNormalized(in WspTwoPortY y, Complex z0)
    {
        var (a, b, c, d) = Normalized(y, z0);
        // (I + ȳ)⁻¹
        var p11 = Complex.One + a; var p12 = b;
        var p21 = c;               var p22 = Complex.One + d;
        var det = p11 * p22 - p12 * p21;
        var i11 =  p22 / det; var i12 = -p12 / det;
        var i21 = -p21 / det; var i22 =  p11 / det;
        // (I − ȳ)
        var m11 = Complex.One - a; var m12 = -b;
        var m21 = -c;              var m22 = Complex.One - d;
        return (m11 * i11 + m12 * i21, m11 * i12 + m12 * i22,
                m21 * i11 + m22 * i21, m21 * i12 + m22 * i22);
    }

    // ── The nodal conjugate reflection coefficient (E.7) ────────────────────

    /// <summary>
    /// <c>Γ = (ZG − conj(ZL)) / (ZG + conj(ZL))</c> (E.7) — "a measure of how well the node is
    /// conjugately matched … plotting on a dB scale can give you a good sense of how well a node is
    /// gain matched". Zero when the two sides of the probe are conjugate matches of each other.
    /// </summary>
    public static Complex NodalGamma(Complex zg, Complex zl)
    {
        var cl  = Complex.Conjugate(zl);
        var den = zg + cl;
        return den == Complex.Zero ? NaN : (zg - cl) / den;
    }

    /// <inheritdoc cref="NodalGamma(Complex, Complex)"/>
    public static Complex NodalGamma(in WspProbeQuad q) => NodalGamma(ZG(q), ZL(q));

    // ── circuitRF's normalised driving-point loci (overview D-12) ───────────

    /// <summary>
    /// <b>circuitRF normalized driving-point locus (series)</b>:
    /// <c>nZ = (ZG + ZL)/(|ZG| + |ZL|) = (1/Y0)/(|ZG| + |ZL|)</c>. Unitless, <c>|nZ| ≤ 1</c>.
    ///
    /// <para><b>This is not the published margin.</b> It is circuitRF's own, cited to
    /// <c>docs/sonnet-briefs/brief-wsprobe-2-nodal-functions.md</c> §2.11 and to the abstract of
    /// T. A. Winslow, "A Novel Stability Margin for Transfer Functions", EuMIC 2024
    /// (DOI 10.23919/EuMIC61603.2024.10732614), whose definition is not public. What it offers is
    /// the ingredient that abstract names: a unitless, bounded proxy for the driving-point locus
    /// that can be compared across nodes. Kurokawa's three conditions are invariant under division
    /// by a positive real, so <see cref="WspKurokawa"/> reports exactly the same frequencies on
    /// <c>nZ</c> as on <c>Y0</c> — which is what makes it safe to offer beside the raw locus.</para>
    /// </summary>
    public static Complex NormalizedLocusSeries(Complex zg, Complex zl)
    {
        double d = zg.Magnitude + zl.Magnitude;
        return d == 0.0 ? NaN : (zg + zl) / d;
    }

    /// <inheritdoc cref="NormalizedLocusSeries(Complex, Complex)"/>
    public static Complex NormalizedLocusSeries(in WspProbeQuad q)
        => NormalizedLocusSeries(ZG(q), ZL(q));

    /// <summary>
    /// <b>circuitRF normalized driving-point locus (shunt)</b>:
    /// <c>nY = (YG + YL)/(|YG| + |YL|) = (1/H0)/(|YG| + |YL|)</c>. Unitless, <c>|nY| ≤ 1</c>.
    /// <b>Not the published margin</b> — see <see cref="NormalizedLocusSeries(Complex, Complex)"/>.
    /// Reports the same Kurokawa frequencies as <c>H0</c>.
    /// </summary>
    public static Complex NormalizedLocusShunt(Complex yg, Complex yl)
    {
        double d = yg.Magnitude + yl.Magnitude;
        return d == 0.0 ? NaN : (yg + yl) / d;
    }

    /// <inheritdoc cref="NormalizedLocusShunt(Complex, Complex)"/>
    public static Complex NormalizedLocusShunt(in WspTwoPortY y)
        => NormalizedLocusShunt(YG(y), YL(y));

    // ── The reserved margin (overview D-12) ────────────────────────────────

    /// <summary>The diagnostic key <see cref="StabilityMargin"/> refuses with.</summary>
    public const string MarginNotTranscribedKey = "wsprobe.margin-not-transcribed";

    /// <summary>The sentence <see cref="StabilityMargin"/> refuses with.</summary>
    public const string MarginNotTranscribedMessage =
        "The stability margin of T. A. Winslow, 'A Novel Stability Margin for Transfer Functions', " +
        "EuMIC 2024 (DOI 10.23919/EuMIC61603.2024.10732614) has not been transcribed into circuitRF; " +
        "its definition is not in the public abstract.";

    /// <summary>
    /// The published stability margin — <b>registered and refused</b>, never guessed (overview
    /// D-12). The name and the Data Display slot exist so that the day the paper is available the
    /// body replaces this refusal and nothing that referenced it has to be renamed; the normalised
    /// driving-point loci above are the inputs it will take.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public static Complex StabilityMargin(in WspProbeQuad q)
        => throw new NotSupportedException($"{MarginNotTranscribedKey}: {MarginNotTranscribedMessage}");
}
