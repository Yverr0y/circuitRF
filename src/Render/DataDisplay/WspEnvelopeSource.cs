// ================================================================
//  WspEnvelopeSource.cs  —  the stability envelope of one run, as an
//  ordinary DataCube
//
//  WSP-4 R-wsp4-9. The Envelope sub-card's four quantities: the two
//  pulled driving-point loci (1/H0', 1/Y0'), the unstable-frequency
//  count per termination, the margin envelope SMenv, and the NDF's own
//  encirclement count over the same grid.
//
//  Everything here is `WspEnvelope.Loadpull`, `LoadpullUnstable`,
//  `LoadpullMargin` and `LoadpullNdf` and nothing else — the brief's own
//  list. What this file adds is the GRID: a termination is `rho * e^jtheta`,
//  and the card edits a ladder of rho per side plus one theta step, so the
//  cube comes back over four grid axes rather than the one flat Gamma axis
//  the expression engine's `{…, gS, gL, …}` uses.
//
//  That reshape is the whole difference, and it is deliberate. [E] Fig. 6-9
//  plot SMenv AGAINST PHASE, one curve per rho; with a flat index axis the
//  x axis would be an ordinal and the ladder would be unreadable. The
//  SAMPLES are the library's, in the library's own grid order, which is
//  what R-wsp4-14(a) is about.
// ================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using RfCore.Data;
using RfCore.Stability;

namespace CircuitRF.Render.DataDisplay;

public static partial class WspSource
{
    /// <summary>The metadata cube the engine writes beside <c>wsp</c>: the declared <c>Z</c> of the
    /// <c>Term</c> directly at each probe terminal, <c>{probe, side}</c>, NaN where there is
    /// none.</summary>
    public const string TermZCubeName = "__WspTermZ";

    /// <summary>The passivated companion of a run's <c>wsp</c> (WSP-6). Not emitted yet; a
    /// <c>NDFenc</c> trace names a second analysis on the card until it is.</summary>
    public const string PassiveCubeName = "wsp_passive";

    /// <summary>
    /// The four grid axes every envelope cube carries, in this order and ALWAYS all four — a side
    /// that is off contributes two length-1 axes rather than disappearing.
    ///
    /// <para>A constant rank is what lets a trace keep its slice when the card's ladders change:
    /// the slice is matched by axis NAME, so turning the load side on would otherwise change the
    /// cube's rank underneath a slice authored against the old one.</para>
    /// </summary>
    public static readonly string[] EnvelopeAxisNames = ["rhoS", "thetaS", "rhoL", "thetaL"];

    /// <summary>One side's Γ grid, its two axes, and whether it is actually being pulled.</summary>
    private readonly record struct GammaSide(bool Pulled, Complex[] Grid, Axis Rho, Axis Theta)
    {
        public int NRho   => Rho.Length;
        public int NTheta => Theta.Length;
    }

    /// <summary>
    /// A side's grid from the card's ladder and θ step. <b>An empty ladder, or the single rung
    /// <c>0</c>, is the side's OFF state</b> (R-wsp4-9's "an off state = 0"): |Γ| = 0 with 24 angles
    /// would otherwise be twenty-four copies of one point, which is a grid that costs 24× and says
    /// nothing. A ladder that CONTAINS a zero rung beside others keeps it — there it is the matched
    /// termination, and it is the rung the others are read against.
    /// </summary>
    private static GammaSide SideGrid(bool probeNamed, IReadOnlyList<double> mags, double thetaStepDeg)
    {
        bool off = !probeNamed || mags.Count == 0 || (mags.Count == 1 && mags[0] == 0.0);
        if (off)
            return new GammaSide(
                false, [new Complex(double.NaN, double.NaN)],
                new Axis("", [0.0], "", ["off"]), new Axis("", [0.0], "", ["off"]));

        int nT = WspMetrics.ThetaCount(thetaStepDeg);
        var thetas = new double[nT];
        for (int k = 0; k < nT; k++) thetas[k] = 360.0 * k / nT;

        var grid = new Complex[mags.Count * nT];
        for (int m = 0; m < mags.Count; m++)
            for (int k = 0; k < nT; k++)
                grid[m * nT + k] = Complex.FromPolarCoordinates(mags[m], thetas[k] * Math.PI / 180.0);

        var rhoLabels = mags.Select(m => m.ToString("0.####", CultureInfo.InvariantCulture)).ToArray();
        return new GammaSide(
            true, grid,
            new Axis("", [.. mags], "", rhoLabels),
            new Axis("", thetas, "deg"));
    }

    private static Axis Named(Axis a, string name) => new(name, a.Values, a.Unit, a.Labels);

    /// <summary>
    /// The envelope metric named by <paramref name="spec"/>, over <c>{…outer…, rhoS, thetaS, rhoL,
    /// thetaL[, freq]}</c>. False with a sentence for every refusal the card shows, including
    /// <c>wsprobe.envelope-probe-not-at-termination</c> in the library's own words.
    /// </summary>
    private static bool TryEvaluateEnvelope(
        DataSet ds, string cubeSpec, WspTraceSpec spec, DataCube cube,
        out DataCube? result, out string error)
    {
        result = null; error = "";

        int size    = cube.Axes[^1].Length;
        int nProbes = size / 2;
        var leading = cube.Axes.Take(cube.Rank - 2).ToArray();
        var raw     = cube.ComplexValues;
        int blocks  = raw.Length / (size * size);

        // The library walks a frequency LIST, so the frequency axis has to be the innermost of the
        // leading axes; anything else and the walk would stride across a sweep point.
        if (leading.Length == 0 || leading[^1].Name is not ("freq" or "ssfreq"))
        {
            error = "The stability envelope is read over a frequency sweep, and this cube's last "
                  + $"axis before row/col is '{(leading.Length == 0 ? "(none)" : leading[^1].Name)}' "
                  + $"({string.Join(", ", cube.Axes.Select(a => a.Name))}).";
            return false;
        }

        var freqAxis = leading[^1];
        var outer    = leading.Take(leading.Length - 1).ToArray();
        int nf       = freqAxis.Length;
        int nOuter   = blocks / nf;
        var freqs    = freqAxis.Values;

        string group  = GroupOf(cubeSpec);
        var    probes = Probes(ds, group);

        if (!TryIdx(probes, nProbes, spec.Probe, out int idx, out error))
        { error = $"The envelope is read AT a suspect probe. {error}"; return false; }

        int idxS = 0, idxL = 0;
        if (spec.SourceProbe.Length > 0 && !TryIdx(probes, nProbes, spec.SourceProbe, out idxS, out error))
        { error = $"The source probe: {error}"; return false; }
        if (spec.LoadProbe.Length > 0 && !TryIdx(probes, nProbes, spec.LoadProbe, out idxL, out error))
        { error = $"The load probe: {error}"; return false; }

        var sideS = SideGrid(idxS > 0, spec.GammaSMags, spec.ThetaStepDeg);
        var sideL = SideGrid(idxL > 0, spec.GammaLMags, spec.ThetaStepDeg);
        if (!sideS.Pulled) idxS = 0;
        if (!sideL.Pulled) idxL = 0;

        if (idxS == 0 && idxL == 0)
        {
            error = "Neither side is pulled. Name a source probe with a |ΓS| ladder, a load probe "
                  + "with a |ΓL| ladder, or both — an empty ladder (or a single 0) is that side's "
                  + "off state.";
            return false;
        }

        // The precondition, in the library's own words and by the same call the expression engine
        // makes (`wsprobe.envelope-probe-not-at-termination`): a probe that is not directly at its
        // Term cannot have that Term swapped out, and the card says so rather than drawing a curve
        // computed from a shunt update that is not the physics.
        var all = new Complex[blocks][,];
        for (int b = 0; b < blocks; b++) all[b] = BlockAt(raw, b, size);
        try
        {
            if (idxS > 0)
                WspEnvelope.RequireAtTermination(all, idxS, WspSide.G, TermZ(ds, group, idxS, WspSide.G), spec.SourceProbe);
            if (idxL > 0)
                WspEnvelope.RequireAtTermination(all, idxL, WspSide.L, TermZ(ds, group, idxL, WspSide.L), spec.LoadProbe);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { error = ex.Message.Split(" (Parameter")[0]; return false; }

        Complex z0 = spec.Z0 == Complex.Zero ? DefaultZ0(ds, group) : spec.Z0;

        var axes = new List<Axis>(outer)
        {
            Named(sideS.Rho,   EnvelopeAxisNames[0]),
            Named(sideS.Theta, EnvelopeAxisNames[1]),
            Named(sideL.Rho,   EnvelopeAxisNames[2]),
            Named(sideL.Theta, EnvelopeAxisNames[3]),
        };

        int nS = sideS.NRho * sideS.NTheta;
        int nL = sideL.NRho * sideL.NTheta;
        int nGrid = nOuter * nS * nL;

        try
        {
            switch (spec.Metric)
            {
                case WspMetric.EnvInvH0:
                case WspMetric.EnvInvY0:
                {
                    axes.Add(freqAxis);
                    var vals = new Complex[nGrid * nf];
                    bool onH0 = spec.Metric == WspMetric.EnvInvH0;
                    for (int o = 0; o < nOuter; o++)
                    {
                        var env = WspEnvelope.Loadpull(
                            PerFreq(all, o, nf), freqs, idxS, idxL, idx, sideS.Grid, sideL.Grid, z0);
                        for (int s = 0; s < nS; s++)
                        for (int l = 0; l < nL; l++)
                        for (int fi = 0; fi < nf; fi++)
                            vals[((o * nS + s) * nL + l) * nf + fi] =
                                Complex.One / (onH0 ? env.H0[s, l, fi] : env.Y0[s, l, fi]);
                    }
                    result = new DataCube([.. axes], vals) { Unit = WspMetrics.Info(spec.Metric)!.Value.Unit };
                    return true;
                }

                case WspMetric.EnvUnstable:
                {
                    var vals = new double[nGrid];
                    for (int o = 0; o < nOuter; o++)
                    {
                        var found = WspEnvelope.LoadpullUnstable(WspEnvelope.Loadpull(
                            PerFreq(all, o, nf), freqs, idxS, idxL, idx, sideS.Grid, sideL.Grid, z0));
                        for (int s = 0; s < nS; s++)
                        for (int l = 0; l < nL; l++)
                            vals[(o * nS + s) * nL + l] = found.Count(s, l);
                    }
                    result = new DataCube([.. axes], vals);
                    return true;
                }

                case WspMetric.SMenv:
                {
                    var vals = new double[nGrid];
                    for (int o = 0; o < nOuter; o++)
                    {
                        var m = WspEnvelope.LoadpullMargin(
                            PerFreq(all, o, nf), freqs, idxS, idxL, idx, sideS.Grid, sideL.Grid, z0);
                        for (int s = 0; s < nS; s++)
                        for (int l = 0; l < nL; l++)
                            vals[(o * nS + s) * nL + l] = m.SmEnv[s, l];
                    }
                    result = new DataCube([.. axes], vals);
                    return true;
                }

                default:
                {
                    if (!TryPassiveBlocks(ds, cubeSpec, spec, size, blocks, out var passive, out error))
                        return false;
                    var vals = new double[nGrid];
                    for (int o = 0; o < nOuter; o++)
                    {
                        var n = WspEnvelope.LoadpullNdf(
                            PerFreq(all, o, nf), PerFreq(passive!, o, nf), freqs,
                            idxS, idxL, null, sideS.Grid, sideL.Grid, z0);
                        for (int s = 0; s < nS; s++)
                        for (int l = 0; l < nL; l++)
                            vals[(o * nS + s) * nL + l] = n.Encirclements[s, l];
                    }
                    result = new DataCube([.. axes], vals);
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { error = ex.Message.Split(" (Parameter")[0]; return false; }
    }

    private static Complex[][,] PerFreq(Complex[][,] blocks, int outerIndex, int nf)
    {
        var per = new Complex[nf][,];
        for (int fi = 0; fi < nf; fi++) per[fi] = blocks[outerIndex * nf + fi];
        return per;
    }

    /// <summary>
    /// The passivated run's blocks for a <c>NDFenc</c> trace: the cube named on the card, else the
    /// <c>wsp_passive</c> beside this group's own <c>wsp</c>. It must cover the SAME sweep — the two
    /// walks visit the grid in the same order and a shorter one would silently pair the wrong
    /// points.
    /// </summary>
    private static bool TryPassiveBlocks(
        DataSet ds, string cubeSpec, WspTraceSpec spec, int size, int blocks,
        out Complex[][,]? result, out string error)
    {
        result = null; error = "";
        string wanted = spec.PassiveSource.Length > 0
            ? spec.PassiveSource
            : WspMetrics.PassiveCubeSpecOf(cubeSpec);

        if (!ds.Contains(wanted))
        {
            error = spec.PassiveSource.Length > 0
                ? $"This source has no cube '{wanted}'. NDFenc is the determinant ratio of the ACTIVE "
                + "run and a PASSIVATED one, so it needs both."
                : $"NDFenc needs a passivated run. This source carries no '{wanted}' cube — name a "
                + "second analysis of the same netlist, run with its devices passivated, in the "
                + "\"passive\" box.";
            return false;
        }

        var pc = ds[wanted];
        if (pc.Rank < 3 || pc.Axes[^1].Length != size || pc.Axes[^2].Length != size)
        { error = $"'{wanted}' is not a wsp matrix of the same {size} × {size} shape as this run's."; return false; }

        var praw = pc.ComplexValues;
        int pblocks = praw.Length / (size * size);
        if (pblocks != blocks)
        {
            error = $"'{wanted}' covers {pblocks} sweep points and this run covers {blocks}; the "
                  + "active and passivated runs must cover the same sweep.";
            return false;
        }

        var outp = new Complex[blocks][,];
        for (int b = 0; b < blocks; b++) outp[b] = BlockAt(praw, b, size);
        result = outp;
        return true;
    }

    /// <summary>The declared <c>Z</c> of the <c>Term</c> at a probe terminal, or null when the run
    /// recorded none — read from <c>__WspTermZ</c> exactly as a measure line reads it, so the
    /// precondition asks the same question in the window and headlessly.</summary>
    private static Complex? TermZ(DataSet ds, string group, int idx, WspSide side)
    {
        var cubes = ds.CubesIn(group);
        if (!cubes.TryGetValue(TermZCubeName, out var t)) return null;
        if (!cubes.TryGetValue(ProbesCubeName, out var table)) return null;
        if (t.Rank != 2 || t.Axes[^1].Length != 2) return null;

        var idxVals = table.DataKind == DataKind.Real
            ? table.RealValues
            : [.. table.ComplexValues.Select(c => c.Real)];
        // The SIDE is not a convenience: it is the G column for a source probe and the L column for
        // a load probe, because the shunt swap is only defined at the terminal facing the
        // termination (§9). Reading the other one would compare ZG against the load's declared Z and
        // refuse a probe that is exactly where it should be.
        for (int k = 0; k < idxVals.Length; k++)
            if ((int)Math.Round(idxVals[k]) == idx)
            {
                var z = t.ComplexValues[k * 2 + (side == WspSide.G ? 0 : 1)];
                return Complex.IsNaN(z) ? null : z;
            }
        return null;
    }
}
