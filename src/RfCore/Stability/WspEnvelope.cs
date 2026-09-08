using System;
using System.Collections.Generic;
using System.Numerics;

namespace RfCore.Stability;

/// <summary>The stability envelope over a grid of source and load reflection coefficients:
/// <c>H0'</c> and <c>Y0'</c> at one probe, indexed <c>[gS, gL, freq]</c>.</summary>
public sealed class WspLoadpullResult
{
    /// <summary>The source grid; a single NaN when the source side was not pulled.</summary>
    public required Complex[] GammaS { get; init; }
    /// <summary>The load grid; a single NaN when the load side was not pulled.</summary>
    public required Complex[] GammaL { get; init; }
    public required double[] Freqs { get; init; }
    /// <summary><c>H0'</c> at the probe, <c>[gS, gL, freq]</c> (Ω).</summary>
    public required Complex[,,] H0 { get; init; }
    /// <summary><c>Y0'</c> at the probe, <c>[gS, gL, freq]</c> (S).</summary>
    public required Complex[,,] Y0 { get; init; }
}

/// <summary>The terminations at which an internal node shows Kurokawa's start-up signature: per grid
/// point, the frequencies found on <c>1/H0'</c> and on <c>1/Y0'</c>.</summary>
public sealed class WspLoadpullUnstableResult
{
    public required Complex[] GammaS { get; init; }
    public required Complex[] GammaL { get; init; }
    /// <summary>Frequencies from the search on <c>1/H0'</c>, <c>[gS][gL]</c>, ascending, possibly empty.</summary>
    public required double[][][] FromH0 { get; init; }
    /// <summary>Frequencies from the search on <c>1/Y0'</c>, <c>[gS][gL]</c>.</summary>
    public required double[][][] FromY0 { get; init; }

    /// <summary>The number of unstable frequencies at a grid point, both immittances together.</summary>
    public int Count(int s, int l) => FromH0[s][l].Length + FromY0[s][l].Length;
}

/// <summary>The stability margin over a grid of source and load reflection coefficients — the
/// 2024 margin swept over the 2025 envelope, with no re-simulation ([E] Fig. 6–9).</summary>
public sealed class WspLoadpullMarginResult
{
    /// <summary>The source grid; a single NaN when the source side was not pulled.</summary>
    public required Complex[] GammaS { get; init; }
    /// <summary>The load grid; a single NaN when the load side was not pulled.</summary>
    public required Complex[] GammaL { get; init; }
    public required double[] Freqs { get; init; }

    /// <summary><c>SM_Y0</c> of the re-terminated circuit at the suspect probe, <c>[gS, gL, freq]</c>.</summary>
    public required double[,,] SmY0 { get; init; }
    /// <summary><c>SM_H0</c> of the re-terminated circuit at the suspect probe, <c>[gS, gL, freq]</c>.</summary>
    public required double[,,] SmH0 { get; init; }
    /// <summary><c>min(SM_Y0, SM_H0)</c> at each frequency, <c>[gS, gL, freq]</c>.</summary>
    public required double[,,] Sm { get; init; }

    /// <summary><b><c>SMenv</c></b> — the minimum over frequency of <see cref="Sm"/>, one number
    /// per termination. This is what [E] Fig. 6–9 plot against phase.</summary>
    public required double[,] SmEnv { get; init; }
    /// <summary>The frequency at which <see cref="SmEnv"/> is attained, Hz.</summary>
    public required double[,] SmEnvHz { get; init; }
    /// <summary>The minimum of <see cref="SmY0"/> over frequency, and where it sits.</summary>
    public required double[,] SmY0Min { get; init; }
    /// <inheritdoc cref="SmY0Min"/>
    public required double[,] SmY0MinHz { get; init; }
    /// <summary>The minimum of <see cref="SmH0"/> over frequency, and where it sits.</summary>
    public required double[,] SmH0Min { get; init; }
    /// <inheritdoc cref="SmH0Min"/>
    public required double[,] SmH0MinHz { get; init; }
}

/// <summary>The normalized determinant function of the re-terminated circuit over a Γ grid.</summary>
public sealed class WspLoadpullNdfResult
{
    public required Complex[] GammaS { get; init; }
    public required Complex[] GammaL { get; init; }
    public required double[] Freqs { get; init; }
    /// <summary><c>NDF</c> of the re-terminated circuit, <c>[gS, gL, freq]</c>.</summary>
    public required Complex[,,] Ndf { get; init; }
    /// <summary>The net clockwise encirclement count of the origin, rounded, <c>[gS, gL]</c>.
    /// Non-zero is the NDF's own verdict of instability.</summary>
    public required double[,] Encirclements { get; init; }
}

/// <summary>
/// The stability envelope — the reference document's §9 (Eq. 187–191) in a general form its 3×3
/// special case falls out of: a termination change is a <b>rank-1 update of <c>wsp</c></b>.
///
/// <para>T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023), §9;
/// brief-wsprobe-3 §6. The 2025 envelope paper (T. A. Winslow, "Stability Envelope Using Nodal
/// Transfer Functions", EuMIC 2025) names both driving-point immittances, which is why both are
/// carried.</para>
///
/// <para><b>The mechanism.</b> Adding a shunt admittance <c>ΔY</c> at a node is equivalent to
/// injecting <c>−ΔY·v'</c> there, and every response to an injection at a probe node is already in
/// <c>wsp</c>. With <c>h = wsp(2S, 2S) = H0_S</c>, for every row <c>r</c> (any stimulus) and column
/// <c>c</c> (any response):</para>
/// <code>
///   G node:  wsp'(r, c) = wsp(r, c) − wsp(r, 2S) · ΔY · wsp(2S, c) / (1 + ΔY·h)          (Sherman–Morrison)
///   L node:  wsp'(r, c) = wsp(r, c) − [wsp(r, 2S) + δ(r, 2S−1)] · ΔY · [wsp(2S, c) − δ(c, 2S−1)] / (1 + ΔY·h)
/// </code>
/// <para>The L-node form is the same physics with two bookkeeping corrections the brief's G-node
/// statement does not need: under the probe's OWN series stimulus (row <c>2S−1</c>) the L-node
/// voltage is <c>vP + vS</c>, one more than <c>wsp(2S−1, 2S)</c>; and a current injected at the L
/// node does not flow through the probe, so the probe's own branch-current response (column
/// <c>2S−1</c>) is one less than the G-node injection's. Every other response is identical, because
/// the two terminals are one node. Applied once for the source probe and once, on the result, for
/// the load probe, this yields the COMPLETE <c>wsp</c> of the re-terminated circuit — every
/// <c>H0'</c>, <c>Y0'</c>, <c>ZG'</c>, <c>ZL'</c>, loop gain and Ohtomo gain — with no cofactor
/// bookkeeping and no assumption about which node is "suspect". Eq. 191's <c>H03'</c> is the
/// <c>(2·3, 2·3)</c> entry of the result.</para>
/// </summary>
public static class WspEnvelope
{
    /// <summary>The diagnostic key <see cref="RequireAtTermination"/> refuses with.</summary>
    public const string NotAtTerminationKey = "wsprobe.envelope-probe-not-at-termination";

    /// <summary>The relative tolerance on <c>|ZG − Z_term|</c> (or <c>ZL</c>) the precondition allows.</summary>
    public const double TerminationTolerance = 1e-6;

    // ── The rank-1 update ────────────────────────────────────────────────────

    /// <summary>A shunt admittance <paramref name="deltaY"/> added at the <paramref name="side"/>
    /// node of probe <paramref name="idx"/>: the complete <c>wsp</c> of the modified network.</summary>
    public static Complex[,] ShuntUpdate(Complex[,] wsp, int idx, WspSide side, Complex deltaY)
    {
        int n = wsp.GetLength(0);
        int probes = WspMatrix.ProbeCount(wsp);
        if (idx < 1 || idx > probes)
            throw new ArgumentException($"probe index {idx} is outside 1..{probes}.", nameof(idx));
        if (deltaY == Complex.Zero) return (Complex[,])wsp.Clone();

        int cv = 2 * idx - 1;   // 0-based column/row of the document's 2S (the node voltage / shunt stimulus)
        int ci = 2 * idx - 2;   // 0-based column/row of the document's 2S−1 (the branch current / series stimulus)
        var h    = wsp[cv, cv];
        var denom = Complex.One + deltaY * h;
        bool lSide = side == WspSide.L;

        var outp = new Complex[n, n];
        for (int r = 0; r < n; r++)
        {
            var v = wsp[r, cv];
            if (lSide && r == ci) v += Complex.One;                 // vL = vP + vS under its own series stimulus
            var factor = v * deltaY / denom;
            for (int c = 0; c < n; c++)
            {
                var resp = wsp[cv, c];
                if (lSide && c == ci) resp -= Complex.One;          // an L-node injection bypasses the probe
                outp[r, c] = wsp[r, c] - factor * resp;
            }
        }
        return outp;
    }

    /// <summary>
    /// <c>wsp_terminate(wsp, idxS, YS, idxL, YL [, YSo, YLo])</c> at one frequency — the source
    /// termination at probe <paramref name="idxS"/>'s G node changed from <paramref name="ySo"/> to
    /// <paramref name="yS"/>, then the load termination at probe <paramref name="idxL"/>'s L node
    /// from <paramref name="yLo"/> to <paramref name="yL"/>. An index of 0 leaves that side alone.
    /// The caller supplies the starting admittances (the defaults are <c>1/ZG</c> and <c>1/ZL</c>
    /// of those probes — <see cref="StartingAdmittance"/>) and has checked the precondition
    /// (<see cref="RequireAtTermination"/>).
    /// </summary>
    public static Complex[,] Terminate(
        Complex[,] wsp, int idxS, Complex yS, Complex ySo, int idxL, Complex yL, Complex yLo)
    {
        var w = wsp;
        if (idxS > 0) w = ShuntUpdate(w, idxS, WspSide.G, yS - ySo);
        if (idxL > 0) w = ShuntUpdate(w, idxL, WspSide.L, yL - yLo);
        return ReferenceEquals(w, wsp) ? (Complex[,])wsp.Clone() : w;
    }

    /// <summary>The starting termination as the probe sees it: <c>1/ZG</c> for the G side,
    /// <c>1/ZL</c> for the L side (§9: "either known or determined using the bidirectional
    /// impedance calculations provided directly from the source and load WSProbes").</summary>
    public static Complex StartingAdmittance(Complex[,] wsp, int idx, WspSide side)
    {
        var q = WspProbeQuad.Of(wsp, idx);
        return Complex.One / (side == WspSide.G ? WspReduction.ZG(q) : WspReduction.ZL(q));
    }

    /// <summary>
    /// <b>Precondition, checked:</b> the probe must sit directly at its termination with the named
    /// side facing it, so that the termination is a pure shunt at that node and the probe's
    /// bidirectional impedance on that side equals the <c>Term</c>'s declared <c>Z</c> —
    /// <c>|ZG − Z_term| ≤ 1e-6·|Z_term|</c> at every frequency (the engine records each probe's
    /// neighbouring <c>Term</c>, if any, in <c>__WspTermZ</c>). A probe with feedback across it
    /// (§9, p. 119: "feedback across the WSProbe terminals can make the calculation of the source
    /// and load looking bidirectional impedances take on values that strongly depend on the
    /// feedback") cannot isolate a termination and fails this check, which is the intended outcome.
    /// </summary>
    /// <param name="wspPerFreq">The <c>wsp</c> matrix at each frequency.</param>
    /// <param name="zTerm">The declared <c>Z</c> of the <c>Term</c> on that side, or null when
    /// there is none.</param>
    /// <exception cref="ArgumentException">With <see cref="NotAtTerminationKey"/>, naming the probe.</exception>
    public static void RequireAtTermination(
        IReadOnlyList<Complex[,]> wspPerFreq, int idx, WspSide side, Complex? zTerm, string label,
        IReadOnlyList<double>? freqsHz = null)
    {
        string which = side == WspSide.G ? "G" : "L";
        string imm   = side == WspSide.G ? "ZG" : "ZL";
        if (zTerm is null || Complex.IsNaN(zTerm.Value))
            throw new ArgumentException(
                $"{NotAtTerminationKey}: WSProbe '{label}' has no Term on its {which} node. The " +
                $"{(side == WspSide.G ? "source" : "load")} probe must sit directly at its termination with " +
                $"{which} facing it, so that the termination is a pure shunt at that node and {imm} equals " +
                "the Term's declared Z (§9).", nameof(idx));

        var zt = zTerm.Value;
        for (int fi = 0; fi < wspPerFreq.Count; fi++)
        {
            var q = WspProbeQuad.Of(wspPerFreq[fi], idx);
            var z = side == WspSide.G ? WspReduction.ZG(q) : WspReduction.ZL(q);
            if ((z - zt).Magnitude <= TerminationTolerance * zt.Magnitude) continue;
            string at = freqsHz is not null && fi < freqsHz.Count ? $" at {freqsHz[fi] / 1e9:G6} GHz" : "";
            throw new ArgumentException(
                $"{NotAtTerminationKey}: WSProbe '{label}' is not directly at its termination — {imm} = {z}" +
                $"{at} but its {which}-side Term declares Z = {zt}. Either something else is connected to " +
                $"that node, or feedback across the probe makes the bidirectional impedance depend on the rest " +
                "of the network (§9, p. 119); either way the termination is not a pure shunt there and " +
                "cannot be swapped out.", nameof(idx));
        }
    }

    // ── Γ ↔ Z ────────────────────────────────────────────────────────────────

    /// <summary><c>Z = Z0·(1 + Γ)/(1 − Γ)</c>.</summary>
    public static Complex GammaToZ(Complex gamma, Complex z0) => z0 * (Complex.One + gamma) / (Complex.One - gamma);

    /// <summary><c>Γ = (Z − Z0)/(Z + Z0)</c>.</summary>
    public static Complex ZToGamma(Complex z, Complex z0) => (z - z0) / (z + z0);

    /// <summary>The common grid: <c>|Γ|·e^{jθ}</c> with <c>θ = 0, 360°/count, …</c>.</summary>
    public static Complex[] CircleGrid(double magnitude, int count, double startDeg = 0.0)
    {
        if (count < 1) throw new ArgumentException("a circle needs at least one point.", nameof(count));
        if (magnitude < 0.0 || magnitude >= 1.0)
            throw new ArgumentException($"|Γ| must be in [0, 1); {magnitude} is not.", nameof(magnitude));
        var g = new Complex[count];
        for (int k = 0; k < count; k++)
            g[k] = Complex.FromPolarCoordinates(magnitude, (startDeg + 360.0 * k / count) * Math.PI / 180.0);
        return g;
    }

    // ── §6.2: the envelope ───────────────────────────────────────────────────

    /// <summary>
    /// <c>wsp_loadpull(wsp, idxS, idxL, idx, gammaS, gammaL [, Z0 = 50])</c> — for each pair of the
    /// two grids: <c>ZS = Z0·(1 + ΓS)/(1 − ΓS)</c>, <c>YS = 1/ZS</c>, likewise the load;
    /// <see cref="Terminate"/>; read <c>H0'</c> and <c>Y0'</c> at probe <paramref name="idx"/>.
    /// Both immittances, because §4.10's pole masking applies under mismatch as much as at nominal.
    /// An index of 0 leaves that side unpulled (its grid is then a single NaN).
    /// </summary>
    /// <param name="ySo">The starting source admittance per frequency (null → <c>1/ZG</c> of probe <paramref name="idxS"/>).</param>
    /// <param name="yLo">The starting load admittance per frequency (null → <c>1/ZL</c> of probe <paramref name="idxL"/>).</param>
    public static WspLoadpullResult Loadpull(
        IReadOnlyList<Complex[,]> wspPerFreq, double[] freqsHz,
        int idxS, int idxL, int idx, Complex[] gammaS, Complex[] gammaL, Complex z0,
        Complex[]? ySo = null, Complex[]? yLo = null)
    {
        ArgumentNullException.ThrowIfNull(wspPerFreq);
        int nf = wspPerFreq.Count;
        if (freqsHz.Length != nf)
            throw new ArgumentException($"{freqsHz.Length} frequencies for {nf} wsp matrices.", nameof(freqsHz));
        if (idxS <= 0) gammaS = [new Complex(double.NaN, double.NaN)];
        if (idxL <= 0) gammaL = [new Complex(double.NaN, double.NaN)];
        if (gammaS.Length == 0 || gammaL.Length == 0)
            throw new ArgumentException("a Γ grid is empty.");

        int ns = gammaS.Length, nl = gammaL.Length;
        var h0 = new Complex[ns, nl, nf];
        var y0 = new Complex[ns, nl, nf];
        int rv = 2 * idx - 1, ri = 2 * idx - 2;

        OverGrid(wspPerFreq, idxS, idxL, gammaS, gammaL, z0, ySo, yLo, (s, l, fi, wl) =>
        {
            h0[s, l, fi] = wl[rv, rv];
            y0[s, l, fi] = wl[ri, ri];
        });

        return new WspLoadpullResult
        {
            GammaS = (Complex[])gammaS.Clone(), GammaL = (Complex[])gammaL.Clone(),
            Freqs = (double[])freqsHz.Clone(), H0 = h0, Y0 = y0,
        };
    }

    /// <summary>
    /// The grid walk every envelope function shares: for each frequency and each pair of the two Γ
    /// grids, the <b>complete</b> <c>wsp</c> of the re-terminated circuit, handed to
    /// <paramref name="visit"/> as <c>(s, l, fi, wsp')</c>.
    ///
    /// <para>Ordered frequency-outer so the source-side update is performed once per
    /// <c>(freq, ΓS)</c> rather than once per grid point — <c>ShuntUpdate</c> is <c>O((2N)²)</c> and
    /// the load loop is the inner one. The matrix handed to <paramref name="visit"/> is not cloned
    /// and must not be retained.</para>
    /// </summary>
    private static void OverGrid(
        IReadOnlyList<Complex[,]> wspPerFreq, int idxS, int idxL,
        Complex[] gammaS, Complex[] gammaL, Complex z0,
        Complex[]? ySo, Complex[]? yLo, Action<int, int, int, Complex[,]> visit)
    {
        int nf = wspPerFreq.Count, ns = gammaS.Length, nl = gammaL.Length;
        for (int fi = 0; fi < nf; fi++)
        {
            var w   = wspPerFreq[fi];
            var so  = idxS > 0 ? (ySo?[fi] ?? StartingAdmittance(w, idxS, WspSide.G)) : Complex.Zero;
            var lo  = idxL > 0 ? (yLo?[fi] ?? StartingAdmittance(w, idxL, WspSide.L)) : Complex.Zero;
            for (int s = 0; s < ns; s++)
            {
                var ys = idxS > 0 ? Complex.One / GammaToZ(gammaS[s], z0) : Complex.Zero;
                var ws = idxS > 0 ? ShuntUpdate(w, idxS, WspSide.G, ys - so) : w;
                for (int l = 0; l < nl; l++)
                {
                    var yl = idxL > 0 ? Complex.One / GammaToZ(gammaL[l], z0) : Complex.Zero;
                    var wl = idxL > 0 ? ShuntUpdate(ws, idxL, WspSide.L, yl - lo) : ws;
                    visit(s, l, fi, wl);
                }
            }
        }
    }

    /// <summary>
    /// <c>wsp_loadpull_unstable(...)</c> — <see cref="WspKurokawa.UnstableFrequencies"/> on
    /// <c>1/H0'</c> and on <c>1/Y0'</c> at every grid point: the terminations a circuit can be
    /// presented with before the internal node shows Kurokawa's signature. This is the "stability
    /// envelope"; the envelope over <c>|Γ|</c> at fixed <c>θ</c> steps is what the Data Display
    /// draws. Both searches are run and both lists are returned, because a zero can mask the pole in
    /// one of them but never in both (§4.10).
    /// </summary>
    public static WspLoadpullUnstableResult LoadpullUnstable(WspLoadpullResult env)
    {
        ArgumentNullException.ThrowIfNull(env);
        int ns = env.GammaS.Length, nl = env.GammaL.Length, nf = env.Freqs.Length;
        var fromH = new double[ns][][];
        var fromY = new double[ns][][];
        var h = new Complex[nf]; var y = new Complex[nf];
        for (int s = 0; s < ns; s++)
        {
            fromH[s] = new double[nl][];
            fromY[s] = new double[nl][];
            for (int l = 0; l < nl; l++)
            {
                for (int fi = 0; fi < nf; fi++) { h[fi] = env.H0[s, l, fi]; y[fi] = env.Y0[s, l, fi]; }
                fromH[s][l] = WspKurokawa.UnstableFrequencies(h, env.Freqs);
                fromY[s][l] = WspKurokawa.UnstableFrequencies(y, env.Freqs);
            }
        }
        return new WspLoadpullUnstableResult
        {
            GammaS = env.GammaS, GammaL = env.GammaL, FromH0 = fromH, FromY0 = fromY,
        };
    }

    // ── WSP-9 §5: the margin and the NDF over the same envelope ──────────────


    /// <summary>
    /// <c>wsp_loadpull_margin(wsp, idxS, idxL, idx, gammaS, gammaL [, Z0 = 50])</c> — the stability
    /// margin of the <b>re-terminated</b> circuit at probe <paramref name="idx"/>, over the two Γ
    /// grids (brief-wsprobe-9 R-wsp9-5).
    ///
    /// <para>Same arguments and the same precondition as <see cref="Loadpull"/>. For each grid point
    /// the rank-1 update gives the complete <c>wsp'</c> of the mismatched circuit, and
    /// <see cref="WspMargin.Of"/> reads the suspect probe's block of it — the <c>Z^R_G</c>,
    /// <c>Z^R_L</c>, <c>Y^R_G</c>, <c>Y^R_L</c> of E-Eq. 11/12, <b>with T-16's correction</b> (E-Eq. 11
    /// as printed swaps the G and L numerators, the same swap as T-4; the corrected form is what
    /// <see cref="WspReduction.ZG"/>/<see cref="WspReduction.ZL"/> compute, and WSP-9 gate (f)
    /// asserts the printed one disagrees).</para>
    ///
    /// <para>[E]'s thesis is that this envelope "tracks NDF perfectly, yet is more informative": at
    /// its <c>ρ = 0.875</c> the NDF encircles at two phase points and the margin collapses there; at
    /// <c>ρ = 0.874</c> the NDF says stable — zero encirclements — and the margin still reads
    /// −40 dB, because the circuit is on the edge and the NDF is binary. Both halves of that
    /// comparison are post-processing here (<see cref="LoadpullNdf"/> is the other).</para>
    /// </summary>
    public static WspLoadpullMarginResult LoadpullMargin(
        IReadOnlyList<Complex[,]> wspPerFreq, double[] freqsHz,
        int idxS, int idxL, int idx, Complex[] gammaS, Complex[] gammaL, Complex z0,
        Complex[]? ySo = null, Complex[]? yLo = null)
    {
        ArgumentNullException.ThrowIfNull(wspPerFreq);
        int nf = wspPerFreq.Count;
        if (freqsHz.Length != nf)
            throw new ArgumentException($"{freqsHz.Length} frequencies for {nf} wsp matrices.", nameof(freqsHz));
        if (idxS <= 0) gammaS = [new Complex(double.NaN, double.NaN)];
        if (idxL <= 0) gammaL = [new Complex(double.NaN, double.NaN)];
        if (gammaS.Length == 0 || gammaL.Length == 0)
            throw new ArgumentException("a Γ grid is empty.");

        int ns = gammaS.Length, nl = gammaL.Length;
        var smY = new double[ns, nl, nf];
        var smH = new double[ns, nl, nf];
        var sm  = new double[ns, nl, nf];

        OverGrid(wspPerFreq, idxS, idxL, gammaS, gammaL, z0, ySo, yLo, (s, l, fi, wl) =>
        {
            var m = WspMargin.Of(WspProbeQuad.Of(wl, idx));
            smY[s, l, fi] = m.SmY0;
            smH[s, l, fi] = m.SmH0;
            sm [s, l, fi] = m.Sm;
        });

        var (envMin, envHz) = MinOverFreq(sm,  freqsHz);
        var (yMin,   yHz)   = MinOverFreq(smY, freqsHz);
        var (hMin,   hHz)   = MinOverFreq(smH, freqsHz);

        return new WspLoadpullMarginResult
        {
            GammaS = (Complex[])gammaS.Clone(), GammaL = (Complex[])gammaL.Clone(),
            Freqs = (double[])freqsHz.Clone(),
            SmY0 = smY, SmH0 = smH, Sm = sm,
            SmEnv = envMin, SmEnvHz = envHz,
            SmY0Min = yMin, SmY0MinHz = yHz,
            SmH0Min = hMin, SmH0MinHz = hHz,
        };
    }

    /// <summary>The minimum of a <c>[gS, gL, freq]</c> block over its frequency axis, and the
    /// frequency at which it is attained. A NaN sample is skipped rather than winning the
    /// comparison; an all-NaN trace gives NaN and NaN.</summary>
    private static (double[,] Min, double[,] Hz) MinOverFreq(double[,,] v, double[] freqsHz)
    {
        int ns = v.GetLength(0), nl = v.GetLength(1), nf = v.GetLength(2);
        var min = new double[ns, nl];
        var hz  = new double[ns, nl];
        for (int s = 0; s < ns; s++)
            for (int l = 0; l < nl; l++)
            {
                double best = double.NaN, at = double.NaN;
                for (int fi = 0; fi < nf; fi++)
                {
                    double x = v[s, l, fi];
                    if (double.IsNaN(x)) continue;
                    if (double.IsNaN(best) || x < best) { best = x; at = freqsHz[fi]; }
                }
                min[s, l] = best;
                hz[s, l]  = at;
            }
        return (min, hz);
    }


    /// <summary>
    /// <c>wsp_loadpull_ndf(wsp, wsp_passive, idxS, idxL, probes, gammaS, gammaL [, Z0 = 50])</c> —
    /// the NDF of the re-terminated circuit over the probe set, per grid point (R-wsp9-6).
    ///
    /// <para>The same terminations are applied to <b>both</b> matrices — the active run and the
    /// passivated one — and <see cref="WspGlobal.Ndf"/> is taken of the pair. This is exact: both
    /// are the exact <c>wsp</c> of the re-terminated network, so no approximation enters between the
    /// rank-1 update and the determinant ratio. [E] re-ran a full NDF sweep per grid point to draw
    /// its blue curves; circuitRF does not have to.</para>
    ///
    /// <para><b>The document's own caveat applies</b> (p. 112–113): this is the <i>reduced</i> NDF
    /// over the probed nodes, and it is complete only if the probe set covers every node that can
    /// hide a pole. A pole at an unprobed node is invisible to it.</para>
    ///
    /// <para>The starting admittances default to <c>1/ZG</c> and <c>1/ZL</c> of each matrix's own
    /// source and load probes. Those agree between the two runs because the precondition puts each
    /// of those probes directly at its <c>Term</c>, and a probe's bidirectional impedance on the
    /// side facing its termination looks into the termination itself — which passivating the active
    /// devices does not change.</para>
    /// </summary>
    public static WspLoadpullNdfResult LoadpullNdf(
        IReadOnlyList<Complex[,]> wspActive, IReadOnlyList<Complex[,]> wspPassive, double[] freqsHz,
        int idxS, int idxL, int[]? probes, Complex[] gammaS, Complex[] gammaL, Complex z0,
        Complex[]? ySo = null, Complex[]? yLo = null)
    {
        ArgumentNullException.ThrowIfNull(wspActive);
        ArgumentNullException.ThrowIfNull(wspPassive);
        int nf = wspActive.Count;
        if (wspPassive.Count != nf)
            throw new ArgumentException(
                $"the active and passive runs must cover the same sweep ({nf} vs {wspPassive.Count} points).",
                nameof(wspPassive));
        if (freqsHz.Length != nf)
            throw new ArgumentException($"{freqsHz.Length} frequencies for {nf} wsp matrices.", nameof(freqsHz));
        if (idxS <= 0) gammaS = [new Complex(double.NaN, double.NaN)];
        if (idxL <= 0) gammaL = [new Complex(double.NaN, double.NaN)];
        if (gammaS.Length == 0 || gammaL.Length == 0)
            throw new ArgumentException("a Γ grid is empty.");

        int ns = gammaS.Length, nl = gammaL.Length;
        var act = new Complex[ns, nl, nf][,];
        var ndf = new Complex[ns, nl, nf];

        // Both walks visit the grid in the same order, so the passive matrix of a point meets the
        // active one that was kept for it.
        OverGrid(wspActive,  idxS, idxL, gammaS, gammaL, z0, ySo, yLo,
                 (s, l, fi, wl) => act[s, l, fi] = (Complex[,])wl.Clone());
        OverGrid(wspPassive, idxS, idxL, gammaS, gammaL, z0, ySo, yLo,
                 (s, l, fi, wl) => ndf[s, l, fi] = WspGlobal.Ndf(act[s, l, fi], wl, probes));

        var enc = new double[ns, nl];
        var trace = new Complex[nf];
        for (int s = 0; s < ns; s++)
            for (int l = 0; l < nl; l++)
            {
                for (int fi = 0; fi < nf; fi++) trace[fi] = ndf[s, l, fi];
                var running = WspKurokawa.Encirclements(trace);
                double n = nf == 0 ? 0.0 : Math.Round(running[^1], MidpointRounding.AwayFromZero);
                enc[s, l] = n == 0.0 ? 0.0 : n;   // Math.Round hands back −0 for a small negative

            }

        return new WspLoadpullNdfResult
        {
            GammaS = (Complex[])gammaS.Clone(), GammaL = (Complex[])gammaL.Clone(),
            Freqs = (double[])freqsHz.Clone(), Ndf = ndf, Encirclements = enc,
        };
    }

}
