using System;
using System.Numerics;

namespace RfCore.Stability;

/// <summary>Which side of a probe a subnetwork sits on. For probe <c>i</c> the <b>G</b> side is the
/// network at node <c>vP_i</c> (port voltage <c>vP_i</c>, current into it <c>−iS_i</c> under a
/// series stimulus and <c>δ_ii·iP − iS_i</c> under a shunt one); the <b>L</b> side is the network
/// at <c>vP_i + vS_i</c> (current into it <c>+iS_i</c>).</summary>
public enum WspSide
{
    G,
    L,
}

/// <summary>Which pair of stimuli a bifurcation is computed from: the series stimuli give an
/// admittance matrix (Eq. 161–168), the shunt stimuli an impedance matrix (Eq. 169–176).</summary>
public enum WspForm
{
    Y,
    Z,
}

/// <summary>
/// Network bifurcation — the reference document's §6 (Eq. 161–176): with <c>N</c> probes all
/// oriented the same way, the network splits into the subnetwork on the G sides and the subnetwork
/// on the L sides, and each can be recovered as an <c>N</c>-port from <c>wsp</c>.
///
/// <para>T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023), §6;
/// brief-wsprobe-3 §3.</para>
///
/// <para><b>The trap this class exists to defuse (overview T-9, T-15, D-8, D-9).</b> The document's
/// matrix equations are written for a symmetric world; for a non-reciprocal network its §6 code
/// returns the <b>transpose</b> of the subnetwork's Y or Z, and its Y-form and Z-form put the
/// "active" network on <b>opposite</b> sides of the probes (<c>wsp_YA</c> is the G side,
/// <c>wsp_ZA</c> the L side). Every use the document makes of those matrices — determinants,
/// principal minors, a diagonal cofactor — is transpose-invariant, so its results are right; a
/// designer reading <c>y21</c> of a block is not. <see cref="Bifurcate"/> returns the TRUE matrix
/// of a NAMED side, and the document's four names are aliases with the document's own sides.</para>
///
/// <para>Derivation, from the Kirchhoff bookkeeping of <see cref="WspSide"/>, stacking the stimuli
/// as columns (<c>VV</c>, <c>VI</c>, <c>IV</c>, <c>II</c> are <see cref="WspBlocks"/>):</para>
/// <code>
///   Y-form (one series stimulus per probe):
///      Y_G · VVᵀ       = −VIᵀ     ⇒  Y_G = −( VV⁻¹ · VI )ᵀ           (true form of Eq. 168)
///      Y_L · (VV + I)ᵀ =  VIᵀ     ⇒  Y_L =  ( (VV + I)⁻¹ · VI )ᵀ     (true form of Eq. 167)
///   Z-form (one shunt stimulus per probe):
///      Z_L · IIᵀ       =  IVᵀ     ⇒  Z_L =  ( II⁻¹ · IV )ᵀ           (true form of Eq. 175)
///      Z_G · (I − II)ᵀ =  IVᵀ     ⇒  Z_G =  ( (I − II)⁻¹ · IV )ᵀ     (true form of Eq. 176; T-12/T-13)
/// </code>
/// <para>The document's §6 functions compute the bracketed products WITHOUT the transpose.</para>
///
/// <para><b>Prerequisite the function cannot check:</b> a subnetwork is well defined only when
/// every listed probe has it on the same side — the document's "all oriented in the same direction
/// (this is a requirement of the math)" (§6, p. 100). Two probes whose named sides meet each other
/// give a singular system or a matrix that describes nothing.</para>
///
/// <para>The one-probe case reduces to the single-probe library: <c>Y_G</c> (1×1) is <c>YG</c>,
/// <c>Y_L = YL</c>, <c>Z_L = ZL</c>, <c>Z_G = ZG</c>.</para>
/// </summary>
public static class WspBifurcation
{
    /// <summary>
    /// <c>wsp_bifurcate(wsp, form, side [, probes])</c> — the TRUE <c>N×N</c> matrix of the named
    /// side, in the given form, with rows and columns in the order of <paramref name="probes"/>
    /// (the whole set, in idx order, when null).
    /// </summary>
    public static Complex[,] Bifurcate(Complex[,] wsp, WspForm form, WspSide side, int[]? probes = null)
    {
        var b = WspMatrix.Blocks(wsp, probes);
        int n = b.Count;
        var eye = WspMatrix.Identity(n);
        return (form, side) switch
        {
            (WspForm.Y, WspSide.G) => WspMatrix.Transpose(WspMatrix.Scale(-Complex.One, WspMatrix.Solve(b.VV, b.VI))),
            (WspForm.Y, WspSide.L) => WspMatrix.Transpose(WspMatrix.Solve(WspMatrix.Add(b.VV, eye), b.VI)),
            (WspForm.Z, WspSide.L) => WspMatrix.Transpose(WspMatrix.Solve(b.II, b.IV)),
            (WspForm.Z, WspSide.G) => WspMatrix.Transpose(WspMatrix.Solve(WspMatrix.Subtract(eye, b.II), b.IV)),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };
    }

    /// <summary>The document's <c>wsp_YA</c> — the <b>G-side</b> admittance matrix (Eq. 168, the
    /// Y-form's "active" network). Alias of <c>Bifurcate(wsp, Y, G)</c>. Note that the document's
    /// <c>A</c>/<c>F</c> labels do NOT agree between its two forms: <c>wsp_ZA</c> is the L side.</summary>
    public static Complex[,] YA(Complex[,] wsp, int[]? probes = null) => Bifurcate(wsp, WspForm.Y, WspSide.G, probes);

    /// <summary>The document's <c>wsp_YF</c> — the <b>L-side</b> admittance matrix (Eq. 167, the
    /// Y-form's "feedback" network). Alias of <c>Bifurcate(wsp, Y, L)</c>.</summary>
    public static Complex[,] YF(Complex[,] wsp, int[]? probes = null) => Bifurcate(wsp, WspForm.Y, WspSide.L, probes);

    /// <summary>The document's <c>wsp_ZA</c> — the <b>L-side</b> impedance matrix (Eq. 175, the
    /// Z-form's "active" network — the OTHER side from <c>wsp_YA</c>'s). Alias of
    /// <c>Bifurcate(wsp, Z, L)</c>. <c>wsp_YF = inverse(wsp_ZA)</c>.</summary>
    public static Complex[,] ZA(Complex[,] wsp, int[]? probes = null) => Bifurcate(wsp, WspForm.Z, WspSide.L, probes);

    /// <summary>The document's <c>wsp_ZF</c> — the <b>G-side</b> impedance matrix (Eq. 176, the
    /// Z-form's "feedback" network — the same side as <c>wsp_YA</c>). Alias of
    /// <c>Bifurcate(wsp, Z, G)</c>. <c>wsp_YA = inverse(wsp_ZF)</c>.</summary>
    public static Complex[,] ZF(Complex[,] wsp, int[]? probes = null) => Bifurcate(wsp, WspForm.Z, WspSide.G, probes);

    /// <summary>The side the document's four names refer to.</summary>
    public static (WspForm Form, WspSide Side) DocumentAlias(string name) => name switch
    {
        "wsp_YA" => (WspForm.Y, WspSide.G),
        "wsp_YF" => (WspForm.Y, WspSide.L),
        "wsp_ZA" => (WspForm.Z, WspSide.L),
        "wsp_ZF" => (WspForm.Z, WspSide.G),
        _ => throw new ArgumentException($"'{name}' is not one of wsp_YA, wsp_YF, wsp_ZA, wsp_ZF.", nameof(name)),
    };

    public static WspSide ParseSide(string side) => side?.Trim().ToUpperInvariant() switch
    {
        "G" => WspSide.G,
        "L" => WspSide.L,
        _ => throw new ArgumentException(
            $"side must be \"G\" (the generator side, at vP) or \"L\" (the load side, at vP + vS), not '{side}'.",
            nameof(side)),
    };

    public static WspForm ParseForm(string form) => form?.Trim().ToUpperInvariant() switch
    {
        "Y" => WspForm.Y,
        "Z" => WspForm.Z,
        _ => throw new ArgumentException(
            $"form must be \"Y\" (from the series stimuli, Eq. 161–168) or \"Z\" (from the shunt " +
            $"stimuli, Eq. 169–176), not '{form}'.", nameof(form)),
    };
}
