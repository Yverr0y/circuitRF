// ================================================================
//  WSProbeEnvelopeTests.cs — WSP-4 R-wsp4-9 (the Envelope sub-card) and the
//  second half of R-wsp4-6 (the pair blocks and wsp_ymatrix as network sources).
//
//  The rule both halves are under is R-wsp4-14(a)'s: what the Data Display draws
//  is BIT-IDENTICAL to the src/RfCore/Stability/ call on the run's own matrices.
//  So every test here computes the same quantity twice — once through the card's
//  own path, once by calling WspEnvelope/WspPair/WspGlobal directly — and asserts
//  equality with no tolerance at all. A tolerance would be hiding a second
//  implementation, which is the failure the gate exists to make impossible rather
//  than unlikely.
//
//  What is NOT asserted by equality is the SHAPE: the card lays the library's one
//  flat Γ grid out over four axes (rhoS, thetaS, rhoL, thetaL), because [E]
//  Fig. 6-9 read the margin AGAINST PHASE and an ordinal x axis cannot be read
//  that way. The reshape is asserted separately, and the samples underneath it are
//  asserted to be the library's in the library's own order.
// ================================================================

using System.Numerics;
using System.Text.Json;
using CircuitRF.Core.Elaboration;
using CircuitRF.Core.Netlist;
using CircuitRF.Engine;
using CircuitRF.Render.DataDisplay;
using RfCore;
using RfCore.Data;
using RfCore.Stability;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests.DataDisplay;

public sealed class WSProbeEnvelopeTests(ITestOutputHelper output)
{
    // ── fixtures ─────────────────────────────────────────────────────────────

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "circuitRF.slnx")))
            dir = Path.GetDirectoryName(dir) ?? "";
        return dir.Length > 0 ? dir : AppContext.BaseDirectory;
    }

    private static string Fixture(string name)
        => Path.Combine(RepoRoot(), "testdata", "wsprobe", name);

    private static (DataSet Ds, double[] Freqs) RunText(string text, string dir)
    {
        var (lib, tb) = new CnlReader().Read(text, "tb", dir);
        var nl    = new Elaborator(lib) { BaseDirectory = dir }.Elaborate(tb);
        var freqs = tb.Analyses.OfType<CircuitRF.Core.Design.SParameterAnalysis>()
                      .First().Expand(nl.ResolvedGlobals);
        var ds = SParameterEngine.Run(nl, freqs);
        DataSourceView.MaterializeNetworkParamCubes(ds);
        return (ds, freqs);
    }

    private static (DataSet Ds, double[] Freqs) Run(string fixture)
        => RunText(File.ReadAllText(Fixture(fixture)), Path.GetDirectoryName(Fixture(fixture))!);

    /// <summary>Every frequency's raw 2N × 2N matrix, straight out of the run's own cube — the
    /// input every library call in this file is made with.</summary>
    private static Complex[][,] AllWsp(DataSet ds, int nf, string spec = "wsp")
    {
        var cube = ds[spec];
        int n    = cube.Axes[^1].Length;
        var raw  = cube.ComplexValues;
        var all  = new Complex[nf][,];
        for (int fi = 0; fi < nf; fi++)
        {
            var m = new Complex[n, n];
            for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                m[r, c] = raw[(fi * n + r) * n + c];
            all[fi] = m;
        }
        return all;
    }

    /// <summary>The grid the card builds for one side, in the card's own order (ρ outer, θ inner) —
    /// which is the order the library is handed and therefore the order its results come back in.</summary>
    private static Complex[] CardGrid(double[] mags, double stepDeg)
    {
        int nT = WspMetrics.ThetaCount(stepDeg);
        var g = new Complex[mags.Length * nT];
        for (int m = 0; m < mags.Length; m++)
        for (int k = 0; k < nT; k++)
            g[m * nT + k] = Complex.FromPolarCoordinates(mags[m], 360.0 * k / nT * Math.PI / 180.0);
        return g;
    }

    private static readonly Complex[] Unpulled = [new(double.NaN, double.NaN)];

    private static WspTraceSpec Envelope(
        WspMetric metric, string suspect, string source = "", string load = "",
        double[]? gammaS = null, double[]? gammaL = null, double theta = 15.0)
        => new()
        {
            Metric       = metric,
            Probe        = suspect,
            SourceProbe  = source,
            LoadProbe    = load,
            GammaSMags   = [.. gammaS ?? []],
            GammaLMags   = [.. gammaL ?? []],
            ThetaStepDeg = theta,
            // Stated, not defaulted. The card's blank Z0 follows the SOURCE GROUP's own port-1
            // Re(Z0), and series_resonator_term.cnl's Term declares 10 Ω — so a test that left this
            // blank would be comparing a Γ grid mapped through 10 Ω against a library call made at
            // 50 Ω, and would read as an arithmetic disagreement rather than a reference one.
            Z0           = new Complex(50, 0),
        };

    // ══ R-wsp4-9 — the envelope, sample for sample ═══════════════════════════

    /// <summary>
    /// The two pulled driving-point loci. The card draws <c>1/H0'</c> and <c>1/Y0'</c>; the library
    /// returns <c>H0'</c> and <c>Y0'</c>, so the inversion is the only arithmetic the card does and
    /// it is asserted to be exactly that, at every one of the 24 × 251 samples.
    /// </summary>
    [Theory]
    [InlineData(WspMetric.EnvInvH0)]
    [InlineData(WspMetric.EnvInvY0)]
    public void Env_TheLoci_AreTheLibrarysLoadpullInverted(WspMetric metric)
    {
        var (ds, freqs) = Run("series_resonator_term.cnl");
        var all  = AllWsp(ds, freqs.Length);
        var mags = new[] { 0.9 };
        var grid = CardGrid(mags, 15.0);

        var spec = Envelope(metric, "P", load: "P", gammaL: mags);
        Assert.True(WspSource.TryEvaluate(ds, "wsp", spec, out var cube, out string err), err);

        var lib = WspEnvelope.Loadpull(all, freqs, 0, 1, 1, Unpulled, grid, 50.0);

        // {rhoS(1), thetaS(1), rhoL(1), thetaL(24), freq(251)}
        Assert.Equal(["rhoS", "thetaS", "rhoL", "thetaL", "freq"],
                     cube!.Axes.Select(a => a.Name).ToArray());
        Assert.Equal([1, 1, 1, grid.Length, freqs.Length],
                     cube.Axes.Select(a => a.Length).ToArray());

        var got = cube.ComplexValues;
        for (int l = 0; l < grid.Length; l++)
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var want = Complex.One / (metric == WspMetric.EnvInvH0 ? lib.H0[0, l, fi] : lib.Y0[0, l, fi]);
            Assert.Equal(want, got[l * freqs.Length + fi]);
        }
        output.WriteLine($"{metric}: {grid.Length} × {freqs.Length} samples, bit-identical");
    }

    /// <summary>
    /// R-wsp4-14(g)'s envelope half. <c>SMenv</c> equals <c>LoadpullMargin</c>'s own <c>SmEnv</c>
    /// exactly, and the arc the closed form predicts (Re Z_S ≤ 5 Ω, so R1 + Re Z_S ≤ 0 with
    /// R1 = −5 Ω) is where it collapses.
    ///
    /// <para><b>The depth is asserted at −30 dB, not −40, and that is a finding rather than a
    /// slack tolerance.</b> On this fixture's own 251-point sweep the shallowest REACTIVE arc point
    /// reads −39.3 dB — the notch is narrower than the 10 MHz step, so the sampled minimum misses
    /// the bottom (WspMarginEngineTests G records the same measurement, and refining the sweep takes
    /// it past −40 dB). The purely resistive pull at θ = 180° is exempt for its own reason: the
    /// reactance proxy takes its both-parts-zero convention value there and the margin floors at
    /// −12.04 dB.</para>
    /// </summary>
    [Fact]
    public void Env_SMenv_IsTheLibrarysMarginEnvelope_AndCollapsesOnTheArc()
    {
        var (ds, freqs) = Run("series_resonator_term.cnl");
        var all  = AllWsp(ds, freqs.Length);
        var mags = new[] { 0.9 };
        var grid = CardGrid(mags, 15.0);

        var spec = Envelope(WspMetric.SMenv, "P", load: "P", gammaL: mags);
        Assert.True(WspSource.TryEvaluate(ds, "wsp", spec, out var cube, out string err), err);

        var lib = WspEnvelope.LoadpullMargin(all, freqs, 0, 1, 1, Unpulled, grid, 50.0);

        Assert.Equal(["rhoS", "thetaS", "rhoL", "thetaL"], cube!.Axes.Select(a => a.Name).ToArray());
        Assert.Equal([1, 1, 1, grid.Length], cube.Axes.Select(a => a.Length).ToArray());

        // The θ axis carries DEGREES, not an ordinal — which is the whole point of the reshape.
        Assert.Equal("deg", cube.Axes[3].Unit);
        for (int l = 0; l < grid.Length; l++)
            Assert.Equal(360.0 * l / grid.Length, cube.Axes[3].Values[l], 12);

        var got = cube.RealValues;
        int onArc = 0, flagged = 0;
        for (int l = 0; l < grid.Length; l++)
        {
            Assert.Equal(lib.SmEnv[0, l], got[l]);                      // bit for bit

            var zs = WspEnvelope.GammaToZ(grid[l], 50.0);
            if (zs.Real > 5.0) continue;
            onArc++;
            double db = 20.0 * Math.Log10(got[l]);
            if (Math.Abs(zs.Imaginary) <= 1e-12 * zs.Magnitude) continue;   // the resistive pull
            flagged++;
            Assert.True(db < -30.0, $"SMenv = {db:F2} dB at θ = {360.0 * l / grid.Length:F0}°");
            output.WriteLine($"θ = {360.0 * l / grid.Length,5:F0}°  Re ZS = {zs.Real,7:F3}  SMenv {db,8:F2} dB");
        }
        Assert.True(onArc > 0 && flagged > 0, $"arc {onArc}, reactive {flagged}");
        Assert.True(onArc < grid.Length, "every termination is on the arc — the fixture proves nothing");
    }

    /// <summary>The stability map's count is <c>LoadpullUnstable.Count</c>, per termination.</summary>
    [Fact]
    public void Env_TheStabilityMap_IsTheLibrarysUnstableCount()
    {
        var (ds, freqs) = Run("series_resonator_term.cnl");
        var all  = AllWsp(ds, freqs.Length);
        var mags = new[] { 0.9 };
        var grid = CardGrid(mags, 15.0);

        var spec = Envelope(WspMetric.EnvUnstable, "P", load: "P", gammaL: mags);
        Assert.True(WspSource.TryEvaluate(ds, "wsp", spec, out var cube, out string err), err);

        var lib = WspEnvelope.LoadpullUnstable(
            WspEnvelope.Loadpull(all, freqs, 0, 1, 1, Unpulled, grid, 50.0));

        var got = cube!.RealValues;
        int nonZero = 0;
        for (int l = 0; l < grid.Length; l++)
        {
            Assert.Equal(lib.Count(0, l), got[l]);
            if (got[l] != 0.0) nonZero++;
        }
        Assert.True(nonZero > 0, "no termination on this grid is flagged — the fixture proves nothing");
        output.WriteLine($"{nonZero} of {grid.Length} terminations flagged");
    }

    /// <summary>
    /// <c>NDFenc</c> against a passivated run — the second half of [E]'s comparison. The passive run
    /// here is the same netlist with its two transconductances zeroed, filed beside the active one
    /// as <c>wsp_passive</c>, which is the cube the card reads when it names no second run.
    /// </summary>
    [Fact]
    public void Env_NDFenc_IsTheLibrarysEncirclementCount_AgainstAPassivatedRun()
    {
        string dir  = Path.GetDirectoryName(Fixture("two_stage_terms.cnl"))!;
        string text = File.ReadAllText(Fixture("two_stage_terms.cnl"));
        var (ds, freqs) = RunText(text, dir);

        string passivated = text.Replace("G=0.04", "G=0").Replace("G=0.05", "G=0");
        Assert.NotEqual(text, passivated);
        var (pds, pf) = RunText(passivated, dir);
        Assert.Equal(freqs.Length, pf.Length);
        ds.Add(WspSource.PassiveCubeName, pds["wsp"]);

        var mags = new[] { 0.6 };
        var grid = CardGrid(mags, 30.0);
        var spec = Envelope(WspMetric.NDFenc, "P3", source: "PS", gammaS: mags, theta: 30.0);
        Assert.True(WspSource.TryEvaluate(ds, "wsp", spec, out var cube, out string err), err);

        var lib = WspEnvelope.LoadpullNdf(
            AllWsp(ds, freqs.Length), AllWsp(pds, freqs.Length), freqs,
            1, 0, null, grid, Unpulled, 50.0);

        Assert.Equal([1, grid.Length, 1, 1], cube!.Axes.Select(a => a.Length).ToArray());
        var got = cube.RealValues;
        for (int s = 0; s < grid.Length; s++)
            Assert.Equal(lib.Encirclements[s, 0], got[s]);
        output.WriteLine($"NDFenc over {grid.Length} source terminations: "
                       + string.Join(", ", got.Select(v => v.ToString("0"))));

        // ── the vacuity guard ────────────────────────────────────────────────
        //
        //  This amplifier is stable over the whole grid, so every count is 0 — and 0 == 0 would pass
        //  against a card that computed nothing at all. What makes the comparison mean something is
        //  that the NDF underneath it is a real, varying curve of a genuinely passivated companion:
        //  the two runs' matrices differ, and the determinant ratio moves over the sweep.
        var act = AllWsp(ds, freqs.Length);
        var pas = AllWsp(pds, freqs.Length);
        Assert.NotEqual(act[0][0, 0], pas[0][0, 0]);

        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            double phase = lib.Ndf[0, 0, fi].Phase;
            lo = Math.Min(lo, phase);
            hi = Math.Max(hi, phase);
        }
        output.WriteLine($"NDF phase over the sweep at ΓS[0]: [{lo:F3}, {hi:F3}] rad");
        Assert.True(hi - lo > 1e-3, "the NDF is constant over the sweep — a zero count says nothing");
    }

    /// <summary>
    /// A trace naming no passivated run at all is a REFUSAL naming both routes to one, not an empty
    /// curve. The other four envelope quantities need no such thing and must still resolve — this
    /// is the one that reads a second run.
    /// </summary>
    [Fact]
    public void Env_NDFenc_WithNoPassiveRun_RefusesAndNamesBothRoutes()
    {
        var (ds, _) = Run("two_stage_terms.cnl");
        var spec = Envelope(WspMetric.NDFenc, "P3", source: "PS", gammaS: [0.6], theta: 90.0);

        Assert.False(WspSource.TryEvaluate(ds, "wsp", spec, out _, out string err));
        output.WriteLine(err);
        Assert.Contains("wsp_passive", err);
        Assert.Contains("passiv", err, StringComparison.OrdinalIgnoreCase);

        var ok = Envelope(WspMetric.SMenv, "P3", source: "PS", gammaS: [0.6], theta: 90.0);
        Assert.True(WspSource.TryEvaluate(ds, "wsp", ok, out _, out string smErr), smErr);
    }

    // ══ the refusals the card shows ══════════════════════════════════════════

    /// <summary>
    /// <c>wsprobe.envelope-probe-not-at-termination</c>, in the library's own words, on the card.
    /// P3 is the suspect node in the middle of the amplifier — it has no <c>Term</c> at all — so
    /// naming it as the SOURCE probe is exactly the mistake §9 refuses, and it must be refused
    /// rather than answered with a shunt update that is not the physics.
    /// </summary>
    [Fact]
    public void Env_APulledProbeNotAtItsTermination_IsRefusedWithTheLibrarysOwnText()
    {
        var (ds, _) = Run("two_stage_terms.cnl");
        var spec = Envelope(WspMetric.SMenv, "P3", source: "P3", gammaS: [0.5], theta: 90.0);

        Assert.False(WspSource.TryEvaluate(ds, "wsp", spec, out _, out string err));
        output.WriteLine(err);
        Assert.Contains(WspEnvelope.NotAtTerminationKey, err);
        Assert.Contains("P3", err);
    }

    /// <summary>Neither side pulled is a refusal that says so — and an EMPTY ladder is what turns a
    /// named side off, which is R-wsp4-9's "an off state = 0".</summary>
    [Fact]
    public void Env_NeitherSidePulled_IsRefusedRatherThanDrawnEmpty()
    {
        var (ds, _) = Run("series_resonator_term.cnl");

        // A probe is named, and its ladder is empty.
        var off = Envelope(WspMetric.SMenv, "P", load: "P");
        Assert.False(WspSource.TryEvaluate(ds, "wsp", off, out _, out string err));
        Assert.Contains("Neither side is pulled", err);

        // A single 0 rung means the same thing, rather than 24 copies of the matched point.
        var zero = Envelope(WspMetric.SMenv, "P", load: "P", gammaL: [0.0]);
        Assert.False(WspSource.TryEvaluate(ds, "wsp", zero, out _, out _));

        // A zero rung BESIDE others is kept — there it is the matched termination the rest are read
        // against, and 3 rungs × 24 angles is the grid the card asked for.
        var ladder = Envelope(WspMetric.SMenv, "P", load: "P", gammaL: [0.0, 0.5, 0.9]);
        Assert.True(WspSource.TryEvaluate(ds, "wsp", ladder, out var cube, out string lerr), lerr);
        Assert.Equal([1, 1, 3, 24], cube!.Axes.Select(a => a.Length).ToArray());
    }

    /// <summary>
    /// The rank is CONSTANT: all four grid axes are always present, an off side contributing two
    /// length-1 axes. A trace's slice is matched by axis name, so a rank that changed when a ladder
    /// was typed would leave the slice describing a cube that no longer exists.
    /// </summary>
    [Fact]
    public void Env_TheFourGridAxes_ArePresentWhicheverSideIsPulled()
    {
        var (ds, _) = Run("two_stage_terms.cnl");
        string[] want = ["rhoS", "thetaS", "rhoL", "thetaL"];

        foreach (var (src, load, gs, gl, shape) in new (string, string, double[]?, double[]?, int[])[]
        {
            ("PS", "",   [0.5],      null,       [1, 4, 1, 1]),
            ("",   "PL", null,       [0.5],      [1, 1, 1, 4]),
            ("PS", "PL", [0.5, 0.7], [0.5],      [2, 4, 1, 4]),
        })
        {
            var spec = Envelope(WspMetric.SMenv, "P3", src, load, gs, gl, theta: 90.0);
            Assert.True(WspSource.TryEvaluate(ds, "wsp", spec, out var cube, out string err), err);
            Assert.Equal(want, cube!.Axes.Select(a => a.Name).ToArray());
            Assert.Equal(shape, cube.Axes.Select(a => a.Length).ToArray());
        }
    }

    /// <summary>
    /// The envelope quantities are gated exactly as the margin is: the three REAL ones are
    /// rectangular-only and appear DISABLED WITH A REASON on Smith and Polar rather than vanishing;
    /// the two loci draw on both. And <c>SMenv</c> carries the margin's own reference lines, because
    /// it IS the margin.
    /// </summary>
    [Fact]
    public void Env_TheRealQuantities_AreRectOnly_AndSMenvKeepsTheMarginsReferenceLines()
    {
        foreach (var m in new[] { WspMetric.SMenv, WspMetric.EnvUnstable, WspMetric.NDFenc })
        {
            Assert.Null(WspMetrics.DisabledReasonOn(m, PlotType.Rect));
            Assert.Null(WspMetrics.DisabledReasonOn(m, PlotType.Table));
            foreach (var pt in new[] { PlotType.Polar, PlotType.Smith })
            {
                string? why = WspMetrics.DisabledReasonOn(m, pt);
                Assert.NotNull(why);
                Assert.Contains(WspMetrics.Name(m), why!);
            }
        }

        foreach (var m in new[] { WspMetric.EnvInvH0, WspMetric.EnvInvY0 })
        {
            Assert.Null(WspMetrics.DisabledReasonOn(m, PlotType.Polar));
            Assert.Null(WspMetrics.DisabledReasonOn(m, PlotType.Rect));
            Assert.NotNull(WspMetrics.DisabledReasonOn(m, PlotType.Smith));   // not an immittance
        }

        var (ds, _) = Run("series_resonator_term.cnl");
        var t = new Trace(new SNP([1e9], 1), MatrixType.S, 0, 0, DependentVarFormat.Complex)
        {
            CubeName = "wsp",
            Wsp      = Envelope(WspMetric.SMenv, "P", load: "P", gammaL: [0.9]),
            Transform = CubeTransform.dB20,
        };
        t.Slice =
        [
            new AxisSlice("rhoS", AxisRole.PinToIndex, 0), new AxisSlice("thetaS", AxisRole.PinToIndex, 0),
            new AxisSlice("rhoL", AxisRole.PinToIndex, 0), new AxisSlice("thetaL", AxisRole.KeepAsX, 0),
        ];
        TraceResolve.SetCubeDataFrom(t, ds, PlotType.Rect, FreqUnit.GHz);
        Assert.Null(t.ExpressionError);
        Assert.True(t.ShowsWspMarginReferenceLines);

        var r = WspReadouts.For(t, ds, PlotType.Rect);
        Assert.NotNull(r);
        output.WriteLine("SMenv readout: " + r!.Value.Text);
        Assert.Contains("SMenv minimum", r.Value.Text);
        Assert.Contains("θL =", r.Value.Text);      // the phase, in its own units — not a frequency
    }

    /// <summary>
    /// The spec box names the measure line that would produce the same numbers (overview D-5). It is
    /// a NAME for what the card's pickers author, not a shorthand the parser reads back — the same
    /// rule the single-probe metrics are under — so what is asserted is that it names the right
    /// library function with the right grid, in the grid spelling that function's own argument takes.
    /// </summary>
    [Fact]
    public void Env_TheSpecBox_NamesTheMeasureLineThatWouldProduceIt()
    {
        var spec = Envelope(WspMetric.SMenv, "P3", source: "PS", load: "PL",
                            gammaS: [0.9, 0.875], gammaL: [0.5], theta: 15.0);
        string text = WspMetrics.AccessorText(spec, "SP1.wsp");
        output.WriteLine(text);

        Assert.StartsWith("wsp_loadpull_margin_env(SP1.wsp,", text);
        Assert.Contains("SP1.idx(\"PS\")", text);
        Assert.Contains("SP1.idx(\"PL\")", text);
        Assert.Contains("SP1.idx(\"P3\")", text);
        Assert.Contains("[\"0.9:24\", \"0.875:24\"]", text);       // the ladder, one circle per rung
        Assert.Contains("\"0.5:24\"", text);                        // one rung needs no list

        // An unpulled side spells as the 0 index wsp_loadpull takes for it.
        string oneSided = WspMetrics.AccessorText(
            Envelope(WspMetric.EnvUnstable, "P", load: "P", gammaL: [0.9], theta: 90.0), "wsp");
        output.WriteLine(oneSided);
        Assert.Contains("wsp_loadpull_unstable(wsp, 0,", oneSided);
        Assert.Contains("\"0.9:4\"", oneSided);
    }

    // ══ R-wsp4-6, second half — the blocks and [Y] as network sources ════════

    /// <summary>
    /// The four two-ports a probe pair brackets, each equal to the library's own breakout or
    /// in-situ renormalised design, entry for entry.
    /// </summary>
    [Theory]
    [InlineData(WspBlockKind.Inner)]
    [InlineData(WspBlockKind.Feedback)]
    [InlineData(WspBlockKind.InnerDesign)]
    [InlineData(WspBlockKind.FeedbackDesign)]
    public void Pair_EachBlock_IsTheLibrarysOwnTwoPort(WspBlockKind kind)
    {
        var (ds, freqs) = Run("two_block.cnl");
        var probes = WspSource.Probes(ds, DataSet.DefaultGroup);
        Assert.True(probes.Count >= 2);
        var z0 = new Complex(50, 0);

        Assert.True(WspSource.TryPairBlock(
            ds, "wsp", probes[0].Label, probes[1].Label, kind, z0, out var cube, out string err), err);

        Assert.Equal(["freq", "i", "j"], cube!.Axes.Select(a => a.Name).ToArray());
        var all = AllWsp(ds, freqs.Length);
        var got = cube.ComplexValues;
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var want = kind switch
            {
                WspBlockKind.Inner       => WspPair.BlockBreakout(all[fi], 1, 2, z0),
                WspBlockKind.Feedback    => WspPair.FbBreakout(all[fi], 1, 2, z0),
                WspBlockKind.InnerDesign => WspPair.BlockDesign(all[fi], 1, 2, freqs[fi], z0),
                _                        => WspPair.FbDesign(all[fi], 1, 2, freqs[fi], z0),
            };
            for (int r = 0; r < 2; r++)
            for (int c = 0; c < 2; c++)
                Assert.Equal(want[r, c], got[fi * 4 + r * 2 + c]);
        }
    }

    /// <summary>
    /// <b>The design forms are handed each point's own frequency, and on the DEFAULT reference pair
    /// that makes no difference to the answer.</b> Measured here rather than assumed, because it is
    /// the opposite of what the code reads like.
    ///
    /// <para><c>wsp_block_design</c> turns each side's bidirectional impedance into a parallel RC
    /// pair at <c>freqHz</c> and then renormalises to that pair AT THE SAME <c>freqHz</c> — so the
    /// frequency cancels and the reference is the impedance it came from, whatever frequency was
    /// passed. It stops cancelling the moment the design use the document describes is taken up
    /// (p. 88: "any RC pair may be replaced by the user's own"), which is why the per-point
    /// frequency is what is passed. The consequence worth knowing is the other way round: a WRONG
    /// frequency here would be invisible on this path, so nothing downstream may take the frequency
    /// broadcast on trust.</para>
    /// </summary>
    [Fact]
    public void Pair_TheDesignForms_AreInvariantToTheFrequencyArgumentOnTheDefaultRcPair()
    {
        var (ds, freqs) = Run("two_block.cnl");
        var probes = WspSource.Probes(ds, DataSet.DefaultGroup);
        var z0 = new Complex(50, 0);
        var all = AllWsp(ds, freqs.Length);

        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var own    = WspPair.BlockDesign(all[fi], 1, 2, freqs[fi], z0);
            var frozen = WspPair.BlockDesign(all[fi], 1, 2, freqs[0],  z0);
            for (int r = 0; r < 2; r++)
            for (int c = 0; c < 2; c++)
                Assert.Equal(own[r, c], frozen[r, c]);
        }
        output.WriteLine("wsp_block_design is invariant to freqHz on the default RC pair, at all "
                       + $"{freqs.Length} points — the frequency cancels between ZToPc and RcRenormS.");

        // And the group the card materializes is that same block, so the two agree by construction.
        Assert.True(WspSource.TryPairBlock(ds, "wsp", probes[0].Label, probes[1].Label,
                                           WspBlockKind.InnerDesign, z0, out var cube, out string err), err);
        var got = cube!.ComplexValues;
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var want = WspPair.BlockDesign(all[fi], 1, 2, freqs[fi], z0);
            for (int r = 0; r < 2; r++)
            for (int c = 0; c < 2; c++)
                Assert.Equal(want[r, c], got[fi * 4 + r * 2 + c]);
        }
    }

    /// <summary>
    /// <c>wsp_ymatrix</c> over an ordered probe set, as a virtual NETWORK group: <c>Y</c> equals
    /// <see cref="WspGlobal.Ymatrix"/> entry for entry, and the group carries the <c>S</c>/<c>Z</c>
    /// /<c>Z0</c> that make every existing network metric apply to it with no further code.
    /// </summary>
    [Fact]
    public void Set_TheYMatrixGroup_IsWspYmatrix_AndIsAnOrdinaryNetworkGroup()
    {
        var (ds, freqs) = Run("three_probe.cnl");
        var labels = WspSource.Probes(ds, DataSet.DefaultGroup).Select(p => p.Label).ToList();
        Assert.Equal(3, labels.Count);

        // Materialized EAGERLY for the full set, in idx order — the one set that needs no picker.
        string group = DataSourceView.ProbeSetYGroup(DataSet.DefaultGroup, labels);
        Assert.Contains(group, ds.Groups);

        var cubes = ds.CubesIn(group);
        foreach (string name in new[] { "S", "Y", "Z", NetworkMetrics.Z0CubeName })
            Assert.True(cubes.ContainsKey(name), $"the group is missing '{name}'");

        var y   = cubes["Y"];
        var all = AllWsp(ds, freqs.Length);
        // The port axes read by the document's own idx, and are LABELLED by the probe label.
        Assert.Equal([1.0, 2.0, 3.0], y.Axes[^1].Values);
        Assert.Equal(labels, y.Axes[^1].Labels);

        var raw = y.ComplexValues;
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var want = WspGlobal.Ymatrix(all[fi], [1, 2, 3]);
            for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                Assert.Equal(want[r, c], raw[(fi * 3 + r) * 3 + c]);
        }

        // A SUBSET is a different matrix and a different group, and order is part of its identity.
        Assert.True(DataSourceView.EnsureWspProbeSetGroup(ds, DataSet.DefaultGroup, [labels[2], labels[0]]));
        string sub = DataSourceView.ProbeSetYGroup(DataSet.DefaultGroup, [labels[2], labels[0]]);
        Assert.Contains(sub, ds.Groups);
        Assert.NotEqual(group, sub);
        Assert.Equal(2, ds.CubesIn(sub)["Y"].Axes[^1].Length);
    }

    /// <summary>
    /// The pair blocks arrive ON DEMAND — the "with probe" picker is what enables them (R-wsp4-6),
    /// and materializing all N(N−1) ordered pairs eagerly would be thousands of cubes nobody asked
    /// for on a matrix with many probes. Idempotent, and both orientations are distinct groups
    /// because Fig. 40's orientation is part of what a block IS.
    /// </summary>
    [Fact]
    public void Pair_TheBlockGroups_ArriveOnDemand_AndTheOrientationIsPartOfTheIdentity()
    {
        var (ds, _) = Run("two_block.cnl");
        var labels = WspSource.Probes(ds, DataSet.DefaultGroup).Select(p => p.Label).ToList();

        string forward = DataSourceView.PairBlockGroup(
            DataSet.DefaultGroup, labels[0], labels[1], WspBlockKind.Inner);
        Assert.DoesNotContain(forward, ds.Groups);

        Assert.True(DataSourceView.EnsureWspPairBlockGroups(ds, DataSet.DefaultGroup, labels[0], labels[1]));
        foreach (var kind in WspSource.BlockKinds)
            Assert.Contains(DataSourceView.PairBlockGroup(DataSet.DefaultGroup, labels[0], labels[1], kind),
                            ds.Groups);

        int before = ds.Groups.Count;
        Assert.True(DataSourceView.EnsureWspPairBlockGroups(ds, DataSet.DefaultGroup, labels[0], labels[1]));
        Assert.Equal(before, ds.Groups.Count);                      // idempotent

        Assert.True(DataSourceView.EnsureWspPairBlockGroups(ds, DataSet.DefaultGroup, labels[1], labels[0]));
        Assert.Contains(DataSourceView.PairBlockGroup(
            DataSet.DefaultGroup, labels[1], labels[0], WspBlockKind.Inner), ds.Groups);

        // The reverse pair is a DIFFERENT two-port, not the same one written the other way round.
        var f = ds.CubesIn(forward)["S"].ComplexValues;
        var r = ds.CubesIn(DataSourceView.PairBlockGroup(
            DataSet.DefaultGroup, labels[1], labels[0], WspBlockKind.Inner))["S"].ComplexValues;
        Assert.NotEqual(f[0], r[0]);

        // A pair naming one probe twice is refused rather than producing a degenerate block.
        Assert.False(DataSourceView.EnsureWspPairBlockGroups(ds, DataSet.DefaultGroup, labels[0], labels[0]));
    }

    // ══ R-wsp4-11 — `.cdd` round trip of the new fields ══════════════════════

    /// <summary>Every Envelope field survives a write and a read of the document the application
    /// itself writes; a `.cdd` that predates them loads with the card's own "off".</summary>
    [Fact]
    public void Env_EveryNewFieldRoundTripsThroughTheDocument()
    {
        var cfg = new WspTraceConfig
        {
            Probe = "P3", Metric = WspMetric.NDFenc,
            SourceProbe = "PS", LoadProbe = "PL",
            GammaSMags = [0.9, 0.875, 0.874], GammaLMags = [0.5],
            ThetaStepDeg = 7.5, PassiveSource = "SP2.wsp",
        };
        string json = JsonSerializer.Serialize(new TraceConfig { CubeName = "SP1.wsp", WsProbe = cfg });
        var back = JsonSerializer.Deserialize<TraceConfig>(json)!.WsProbe!;

        Assert.Equal("PS", back.SourceProbe);
        Assert.Equal("PL", back.LoadProbe);
        Assert.Equal([0.9, 0.875, 0.874], back.GammaSMags);
        Assert.Equal([0.5], back.GammaLMags);
        Assert.Equal(7.5, back.ThetaStepDeg);
        Assert.Equal("SP2.wsp", back.PassiveSource);
        Assert.Contains("\"NDFenc\"", json);                        // by NAME, never by ordinal

        // A document written before the Envelope card carries none of these.
        const string old = """
            {"CubeName":"SP1.wsp","WsProbe":{"Probe":"P","Metric":"SM_Y0","Z0":"0"}}
            """;
        var older = JsonSerializer.Deserialize<TraceConfig>(old)!.WsProbe!;
        Assert.Equal("", older.SourceProbe);
        Assert.Equal("", older.LoadProbe);
        Assert.Empty(older.GammaSMags);
        Assert.Empty(older.GammaLMags);
        Assert.Equal(15.0, older.ThetaStepDeg);
        Assert.Equal("", older.PassiveSource);
    }
}
