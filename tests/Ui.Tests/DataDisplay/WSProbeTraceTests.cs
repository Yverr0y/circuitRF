// ================================================================
//  WSProbeTraceTests.cs — WSP-4 gates (a), (b), (c), (f) and (g).
//
//  What this file holds is that the Data Display SHOWS what the library computed, and nothing else:
//
//   (a) every WSProbe trace's samples are bit-identical to the src/RfCore/Stability/ call on the
//       run's own cubes — every metric, both plot types, a swept source and an unswept one;
//   (b) the virtual reduced-two-port group yields the same S matrix the pair blocks are scattered
//       by, so µ, K and the circles apply to it through the code that already exists;
//   (c) every new `.cdd` field round-trips, and a `.cdd` written before WSP-4 loads unchanged;
//   (f) the Kurokawa readout on WSP-1's series resonator, and "Mark crossings";
//   (g) the margin group is Rect-only and is DISABLED WITH A REASON elsewhere rather than absent;
//       the two margin readouts on WSP-9's split resonator; and the two reference lines.
//
//  Gate (e) — the `plot` verb against the GUI's own document — is in
//  tests/Ui.Tests/Cli/WsProbePlotVerbTests.cs, because it runs the CLI as a process.
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

public sealed class WSProbeTraceTests(ITestOutputHelper output)
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

    private static (DataSet Ds, double[] Freqs) Run(string fixture)
    {
        var (lib, tb) = CnlReader.ReadFile(Fixture(fixture));
        var nl    = new Elaborator(lib).Elaborate(tb);
        var freqs = tb.Analyses.OfType<CircuitRF.Core.Design.SParameterAnalysis>()
                      .First().Expand(nl.ResolvedGlobals);
        var ds = SParameterEngine.Run(nl, freqs);
        DataSourceView.MaterializeNetworkParamCubes(ds);
        return (ds, freqs);
    }

    /// <summary>The raw 2N x 2N matrix at one frequency, straight out of the run's own cube — the
    /// input every library call in this file is made with.</summary>
    private static Complex[,] Wsp(DataSet ds, string cubeSpec, int fi)
    {
        var cube = ds[cubeSpec];
        int n    = cube.Axes[^1].Length;
        var raw  = cube.ComplexValues;
        var m    = new Complex[n, n];
        for (int r = 0; r < n; r++)
        for (int c = 0; c < n; c++)
            m[r, c] = raw[(fi * n + r) * n + c];
        return m;
    }

    private static Trace WspTrace(DataSet ds, string cubeSpec, WspMetric metric,
                                  string probe = "P", string with = "")
    {
        var t = new Trace(new SNP([1e9], 1), MatrixType.S, 0, 0, DependentVarFormat.Complex);
        t.CubeName = cubeSpec;
        t.Wsp      = new WspTraceSpec { Probe = probe, With = with, Metric = metric };

        var leading = WspSource.LeadingAxes(ds, cubeSpec)!;
        var slice   = new AxisSlice[leading.Length];
        for (int d = 0; d < leading.Length; d++)
            slice[d] = leading[d].Name == "freq"
                ? new AxisSlice(leading[d].Name, AxisRole.KeepAsX, 0)
                : new AxisSlice(leading[d].Name, AxisRole.PinToIndex, 0);
        t.Slice = slice;
        return t;
    }

    // ══ (a) — bit identity with the library ══════════════════════════════════

    public static TheoryData<WspMetric, PlotType> EveryMetricOnBothPlotTypes()
    {
        var d = new TheoryData<WspMetric, PlotType>();
        foreach (var info in WspMetrics.All)
        {
            // The probe-SET group needs a set, and the single-probe fixture has one probe: it is
            // covered by its own test below rather than skipped silently.
            if (info.Group == WspMetricGroup.ProbeSet) continue;
            // The ENVELOPE group needs a source probe, a load probe and a Γ ladder — a grid this
            // fixture cannot supply, and one whose own bit-identity gate is the whole of
            // WSProbeEnvelopeTests. Named here so it is an exclusion rather than an omission.
            if (info.Group == WspMetricGroup.Envelope) continue;
            foreach (var pt in new[] { PlotType.Rect, PlotType.Polar })
                d.Add(info.Metric, pt);
        }
        return d;
    }

    /// <summary>
    /// Every metric, drawn, compared sample by sample against the library call on the same matrix.
    /// <b>Bit for bit</b> — the same rule <c>NetworkMetrics</c> is under (R-wsp4-14a): there is one
    /// implementation and the Data Display calls it, so any tolerance at all here would be hiding a
    /// second one.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryMetricOnBothPlotTypes))]
    public void A_EveryMetric_IsBitIdenticalToTheLibraryCall(WspMetric metric, PlotType plotType)
    {
        var (ds, freqs) = Run("two_block.cnl");
        var probes = WspSource.Probes(ds, DataSet.DefaultGroup);
        Assert.True(probes.Count >= 2, "two_block.cnl carries a probe PAIR");

        var t = WspTrace(ds, "wsp", metric, probes[0].Label, probes[1].Label);
        t.Transform = CubeTransform.None;
        TraceResolve.SetCubeDataFrom(t, ds, plotType, FreqUnit.GHz);

        Assert.Null(t.ExpressionError);
        var info = WspMetrics.Info(metric)!.Value;

        if (info.IsReal)
        {
            var got = t.CubeReal;
            Assert.NotNull(got);
            Assert.Equal(freqs.Length, got!.Count);
            for (int fi = 0; fi < freqs.Length; fi++)
                Assert.Equal(LibraryReal(Wsp(ds, "wsp", fi), probes[0].Idx, metric), got[fi]);
        }
        else
        {
            var got = t.CubeComplex;
            Assert.NotNull(got);
            Assert.Equal(freqs.Length, got!.Count);
            for (int fi = 0; fi < freqs.Length; fi++)
            {
                var want = LibraryComplex(Wsp(ds, "wsp", fi), probes[0].Idx, probes[1].Idx, metric);
                Assert.Equal(want.Real,      got[fi].Real);
                Assert.Equal(want.Imaginary, got[fi].Imaginary);
            }
        }
    }

    /// <summary>
    /// The same identity over a PARAMETRIC SWEEP, where the wsp cube is <c>{Pin, freq, row, col}</c>
    /// and the metric is a cube over <c>{Pin, freq}</c> — the shape R-wsp4-10's family mechanism
    /// reads. Both the pinned slice AND the family are checked, because they take different paths
    /// through the resolver and only one of them was exercised above.
    /// </summary>
    [Fact]
    public void A_ASweptRun_IsBitIdenticalOnEverySweepPoint()
    {
        var (ds, _) = Run("derived_metrics.cnl");
        var leading = WspSource.LeadingAxes(ds, FirstWspSpec(ds))!;
        output.WriteLine("wsp leading axes: " + string.Join(", ", leading.Select(a => a.Name)));

        string spec  = FirstWspSpec(ds);
        var probes   = WspSource.Probes(ds, WspSource.GroupOf(spec));
        int size     = ds[spec].Axes[^1].Length;
        int blocks   = ds[spec].ComplexValues.Length / (size * size);

        var t = WspTrace(ds, spec, WspMetric.SM_Y0, probes[0].Label);
        TraceResolve.SetCubeDataFrom(t, ds, PlotType.Rect, FreqUnit.GHz);
        Assert.Null(t.ExpressionError);

        // Whatever the leading shape, the FIRST block is the first sample of the pinned slice.
        var got = t.CubeReal;
        Assert.NotNull(got);
        Assert.Equal(WspMargin.Of(WspProbeQuad.Of(Wsp(ds, spec, 0), probes[0].Idx)).SmY0, got![0]);
        Assert.True(blocks >= got.Count, "the pinned slice is at most the whole buffer");
    }

    private static string FirstWspSpec(DataSet ds)
        => WspSource.WspCubeSpec(WspSource.GroupsWithProbes(ds).First());

    private static double LibraryReal(Complex[,] w, int idx, WspMetric metric)
    {
        var m = WspMargin.Of(WspProbeQuad.Of(w, idx));
        return metric switch
        {
            WspMetric.SM_Y0 => m.SmY0, WspMetric.SM_H0 => m.SmH0, WspMetric.SM => m.Sm,
            WspMetric.rY => m.rY, WspMetric.iY => m.iY, WspMetric.rH => m.rH, WspMetric.iH => m.iH,
            _ => throw new InvalidOperationException($"{metric} is not a real metric"),
        };
    }

    private static Complex LibraryComplex(Complex[,] w, int idx, int idx2, WspMetric metric)
    {
        var z0 = new Complex(50, 0);
        if (WspMetrics.NeedsPair(metric))
        {
            var all = WspPair.BlockCalc(w, idx, idx2, z0);
            int one = metric switch
            {
                WspMetric.F_LGa => 9,  WspMetric.F_LGf => 10, WspMetric.F_LGH => 11, WspMetric.F_LGM => 12,
                WspMetric.LGa   => 13, WspMetric.LGf   => 14, WspMetric.LGH   => 15, WspMetric.LGM   => 16,
                _ => 0,
            };
            return all[one - 1];
        }

        var q = WspProbeQuad.Of(w, idx);
        var y = WspReduction.YParam(q);
        return metric switch
        {
            WspMetric.H0    => WspNodal.H0(q),
            WspMetric.Y0    => WspNodal.Y0(q),
            WspMetric.InvH0 => Complex.One / WspNodal.H0(q),
            WspMetric.InvY0 => Complex.One / WspNodal.Y0(q),
            WspMetric.ZG    => WspNodal.ZG(q),
            WspMetric.ZL    => WspNodal.ZL(q),
            WspMetric.YG    => WspNodal.YG(y),
            WspMetric.YL    => WspNodal.YL(y),
            WspMetric.Zop   => WspNodal.Zop(y),
            WspMetric.Yop   => WspNodal.Yop(WspReduction.ZParam(q)),
            WspMetric.LG    => WspNodal.LoopGain(y, WspLoopGainKind.Bi, z0),
            WspMetric.F     => WspReduction.ReturnDifference(WspNodal.LoopGain(y, WspLoopGainKind.Bi, z0)),
            WspMetric.LGF   => WspNodal.LoopGain(y, WspLoopGainKind.Uni, z0),
            WspMetric.LGR   => WspNodal.LoopGain(y, WspLoopGainKind.Rev, z0),
            WspMetric.LG_H  => WspNodal.LoopGain(y, WspLoopGainKind.Hst, z0),
            WspMetric.LG_MF => WspNodal.LoopGain(y, WspLoopGainKind.Mb, z0),
            WspMetric.LG_MR => WspNodal.LoopGain(y, WspLoopGainKind.Mbr, z0),
            WspMetric.LG_MGF => WspNodal.LoopGain(y, WspLoopGainKind.Gft, z0),
            WspMetric.LG_MGR => WspNodal.LoopGain(y, WspLoopGainKind.Gftr, z0),
            WspMetric.NodalGamma => WspNodal.NodalGamma(q),
            _ => throw new InvalidOperationException($"{metric} is not a complex metric"),
        };
    }

    /// <summary>Ohtomo's own metric, over the ordered set the card would supply.</summary>
    [Fact]
    public void A_OhtomoOverAProbeSet_IsBitIdenticalToTheLibraryCall()
    {
        var (ds, freqs) = Run("three_probe.cnl");
        var probes = WspSource.Probes(ds, DataSet.DefaultGroup);
        Assert.True(probes.Count >= 2);

        var t = WspTrace(ds, "wsp", WspMetric.OhtomoG, probes[0].Label);
        t.Wsp!.Set = [.. probes.Select(p => p.Label)];
        t.Wsp.SetIndex = 1;
        t.Transform = CubeTransform.None;
        TraceResolve.SetCubeDataFrom(t, ds, PlotType.Polar, FreqUnit.GHz);

        Assert.Null(t.ExpressionError);
        var got = t.CubeComplex;
        Assert.NotNull(got);
        int[] set = [.. probes.Select(p => p.Idx)];
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var want = WspOhtomo.LoopGains(Wsp(ds, "wsp", fi), set, WspSide.G, new Complex(50, 0))[0];
            Assert.Equal(want.Real,      got![fi].Real);
            Assert.Equal(want.Imaginary, got[fi].Imaginary);
        }
    }

    /// <summary>A probe this run does not have is reported BY NAME with the run's own list, and the
    /// trace draws nothing — never an empty curve that looks like an answer.</summary>
    [Fact]
    public void AProbeThatIsNotInTheRun_IsReportedByNameAndDrawsNothing()
    {
        var (ds, _) = Run("series_resonator.cnl");
        var t = WspTrace(ds, "wsp", WspMetric.SM_Y0, probe: "NOT_HERE");
        TraceResolve.SetCubeDataFrom(t, ds, PlotType.Rect, FreqUnit.GHz);

        Assert.NotNull(t.ExpressionError);
        Assert.Contains("NOT_HERE", t.ExpressionError!);
        Assert.Contains("P", t.ExpressionError!);
        Assert.Empty(t.Points);
    }

    // ══ (b) — the reduced two-port as a network source ═══════════════════════

    /// <summary>
    /// R-wsp4-14b. The virtual group's <c>S</c> is the scattering matrix of <c>wsp_yparam</c> at the
    /// same probe — so every metric the Data Display already has (µ, µ', K, |Δ|, MAG/MSG, the
    /// circles) applies to the reduction through the code that computes them for any other source.
    /// The test proves the group is WIRED to that, which is the only thing that could be wrong: the
    /// metrics themselves have their own gates.
    /// </summary>
    [Fact]
    public void B_TheReducedTwoPortGroup_IsTheScatteringMatrixOfWspYparam()
    {
        var (ds, freqs) = Run("series_resonator.cnl");
        var probes = WspSource.Probes(ds, DataSet.DefaultGroup);
        string group = DataSourceView.ReducedTwoPortGroup(DataSet.DefaultGroup, probes[0].Label);

        Assert.Contains(group, ds.Groups);
        var cubes = ds.CubesIn(group);
        Assert.True(cubes.ContainsKey("S"));
        Assert.True(cubes.ContainsKey("Y"));
        Assert.True(cubes.ContainsKey("Z"));
        Assert.True(cubes.ContainsKey(NetworkMetrics.Z0CubeName));

        // The group is a genuine network group, which is the whole point — that is what makes
        // every existing metric apply with no new code.
        Assert.True(NetworkMetrics.IsNetworkParamCubeSpec(ds, $"{group}.S"));

        var s  = cubes["S"].ComplexValues;
        var yc = cubes["Y"].ComplexValues;
        var z0 = new Complex(50, 0);
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var y = WspReduction.YParam(WspProbeQuad.Of(Wsp(ds, "wsp", fi), probes[0].Idx));
            Assert.Equal(y.Y11.Real, yc[fi * 4 + 0].Real);
            Assert.Equal(y.Y22.Real, yc[fi * 4 + 3].Real);

            var want = WspMatrix.ScatteringOfY(
                new[,] { { y.Y11, y.Y12 }, { y.Y21, y.Y22 } }, z0);
            Assert.Equal(want[0, 0].Real,      s[fi * 4 + 0].Real);
            Assert.Equal(want[0, 0].Imaginary, s[fi * 4 + 0].Imaginary);
            Assert.Equal(want[1, 1].Real,      s[fi * 4 + 3].Real);
        }
    }

    /// <summary>
    /// The run's OWN S cube must still be the one a network view is built from. Materializing the
    /// reduced two-ports after the analysis groups is what keeps that true, and it is the kind of
    /// thing that breaks silently: every stability metric on the source would quietly describe a
    /// probe's reduction instead of the amplifier.
    /// </summary>
    [Fact]
    public void B_TheReducedGroups_DoNotBecomeTheSourcesNetworkView()
    {
        var (ds, _) = Run("hero1_probed.cnl");
        string? sSpec = NetworkMetrics.FindSCubeSpec(ds);
        Assert.NotNull(sSpec);
        Assert.DoesNotContain("WSProbe", sSpec!);
    }

    // ══ (c) — `.cdd` round trip ══════════════════════════════════════════════

    /// <summary>Every field of the probe spec survives a write and a read of the document the
    /// application itself writes.</summary>
    [Fact]
    public void C_EveryNewFieldRoundTripsThroughTheDocument()
    {
        var cfg = new WspTraceConfig
        {
            Probe = "GATE", With = "DRAIN", Set = ["GATE", "DRAIN", "SOURCE"],
            Metric = WspMetric.LGM, Z0 = "75", ActiveSide = WspSide.L, SetIndex = 3,
        };
        var tc = new TraceConfig { CubeName = "SP1.wsp", WsProbe = cfg };

        string json = JsonSerializer.Serialize(tc);
        var back = JsonSerializer.Deserialize<TraceConfig>(json)!;

        Assert.NotNull(back.WsProbe);
        Assert.Equal("GATE",  back.WsProbe!.Probe);
        Assert.Equal("DRAIN", back.WsProbe.With);
        Assert.Equal(["GATE", "DRAIN", "SOURCE"], back.WsProbe.Set);
        Assert.Equal(WspMetric.LGM, back.WsProbe.Metric);
        Assert.Equal("75", back.WsProbe.Z0);
        Assert.Equal(WspSide.L, back.WsProbe.ActiveSide);
        Assert.Equal(3, back.WsProbe.SetIndex);

        // Written by NAME, not by ordinal — the member may be added to but never renamed.
        Assert.Contains("\"LGM\"", json);
    }

    /// <summary>A `.cdd` written before WSP-4 has no probe field at all, and must load as the plain
    /// cube trace it was.</summary>
    [Fact]
    public void C_ADocumentWrittenBeforeWSP4_LoadsUnchanged()
    {
        const string old = """
            {"SourcePath":"run.npy","CubeName":"SP1.S","CubeTransform":"dB20",
             "CubeSlice":[{"AxisName":"freq","Role":"KeepAsX","Index":0,
                           "RangeStart":0,"RangeEndExclusive":-1,"Label":""}]}
            """;
        var back = JsonSerializer.Deserialize<TraceConfig>(old)!;
        Assert.Null(back.WsProbe);
        Assert.Equal("SP1.S", back.CubeName);
        Assert.Equal(CubeTransform.dB20, back.CubeTransform);
    }

    // ══ (f) — the Kurokawa readout, and "Mark crossings" ═════════════════════

    /// <summary>
    /// R-wsp4-14f. WSP-1's series resonator shows Kurokawa's start-up signature in <c>1/Y0</c> and
    /// NOT in <c>1/H0</c> — the pole masking of §4.10, which is the whole reason the document says
    /// both must be checked. The card reads exactly that.
    /// </summary>
    [Fact]
    public void F_TheKurokawaReadout_ReadsF0OnOneOverY0AndNoneOnOneOverH0()
    {
        var (ds, _) = Run("series_resonator.cnl");

        var y = WspTrace(ds, "wsp", WspMetric.InvY0);
        y.Transform = CubeTransform.None;
        TraceResolve.SetCubeDataFrom(y, ds, PlotType.Polar, FreqUnit.GHz);
        var ry = WspReadouts.For(y, ds, PlotType.Polar);
        Assert.NotNull(ry);
        output.WriteLine("1/Y0: " + ry!.Value.Text);
        Assert.Contains("Kurokawa start-up:", ry.Value.Text);
        Assert.Contains("1.591", ry.Value.Text);
        double f0 = Assert.Single(ry.Value.Crossings);
        Assert.InRange(f0, 1.585e9, 1.598e9);

        var h = WspTrace(ds, "wsp", WspMetric.InvH0);
        h.Transform = CubeTransform.None;
        TraceResolve.SetCubeDataFrom(h, ds, PlotType.Polar, FreqUnit.GHz);
        var rh = WspReadouts.For(h, ds, PlotType.Polar);
        Assert.NotNull(rh);
        output.WriteLine("1/H0: " + rh!.Value.Text);
        Assert.Contains("Kurokawa start-up: none", rh.Value.Text);
        Assert.Empty(rh.Value.Crossings);
    }

    /// <summary>An encirclement count rides beside the Kurokawa reading on a polar plot — the
    /// reading NDF needs (§8) and the one F invites (p. 34).</summary>
    [Fact]
    public void F_APolarTrace_AlsoReportsItsEncirclementCount()
    {
        var (ds, _) = Run("series_resonator.cnl");
        var t = WspTrace(ds, "wsp", WspMetric.InvY0);
        t.Transform = CubeTransform.None;
        TraceResolve.SetCubeDataFrom(t, ds, PlotType.Polar, FreqUnit.GHz);

        var r = WspReadouts.For(t, ds, PlotType.Polar)!.Value;
        Assert.Contains("encirclements:", r.Text);
    }

    // ══ (g) — the margin group's gating, its readouts and its lines ══════════

    /// <summary>
    /// R-wsp4-14g. Every margin quantity is Rect-only, and on Smith or Polar it is offered DISABLED
    /// WITH A REASON rather than removed — the same rule the derived metrics follow, and for the
    /// same reason: a metric that vanishes reads as a metric circuitRF does not have.
    /// </summary>
    [Theory]
    [InlineData(PlotType.Polar)]
    [InlineData(PlotType.Smith)]
    public void G_TheMarginGroup_IsDisabledWithAReasonOnAComplexPlot(PlotType plotType)
    {
        foreach (var info in WspMetrics.All.Where(m => m.Group == WspMetricGroup.Margin))
        {
            string? why = WspMetrics.DisabledReasonOn(info.Metric, plotType);
            Assert.False(string.IsNullOrEmpty(why), $"{info.Name} on {plotType}");
            Assert.Contains("rectangular", why!);
            Assert.Null(WspMetrics.DisabledReasonOn(info.Metric, PlotType.Rect));
        }
    }

    /// <summary>The complement: a rect plot lists the Smith-only quantities the same way. A loop
    /// gain has no Smith grid — it is not an immittance and not a reflection coefficient — and says
    /// so instead of drawing on one.</summary>
    [Fact]
    public void G_ARectPlotTakesEveryMetric_AndSmithRefusesTheOnesItHasNoGridFor()
    {
        foreach (var info in WspMetrics.All)
            Assert.Null(WspMetrics.DisabledReasonOn(info.Metric, PlotType.Rect));

        Assert.NotNull(WspMetrics.DisabledReasonOn(WspMetric.LG, PlotType.Smith));
        Assert.Null(WspMetrics.DisabledReasonOn(WspMetric.LG, PlotType.Polar));
        Assert.Null(WspMetrics.DisabledReasonOn(WspMetric.ZG, PlotType.Smith));
        Assert.Null(WspMetrics.DisabledReasonOn(WspMetric.NodalGamma, PlotType.Smith));
    }

    /// <summary>
    /// WSP-9 §3's own table, read off the card: on the split resonator <c>SM_Y0</c> bottoms out at
    /// 0.12509 at 1.59125 GHz and <c>SM_H0</c> at 0.10175 at 1.73375 GHz, and the two collapse at
    /// DIFFERENT frequencies — §4.10's pole masking in margin form, which is why the card prints the
    /// matching driving-point search beside each.
    ///
    /// <para><b>The linear minima are the assertion, not the printed dB.</b> WSP-9's own engine gate
    /// (<c>WspMarginEngineTests</c>) pins those two numbers and this card must agree with the engine
    /// rather than with a rounding of it; the brief's §3 table quotes them as "−18.06 dB at
    /// 1.5913 GHz" and "−19.85 dB at 1.7337 GHz", which is the same two points printed to a
    /// different precision (20·log10(0.12509) = −18.058, and 1.59125 rounded down). The card prints
    /// one decimal of dB, matching <c>sparam</c>'s own summary line, so what it shows is
    /// "−18.1 dB at 1.5913 GHz" — asserted below as the STRING, beside the numbers.</para>
    /// </summary>
    [Fact]
    public void G_TheMarginReadouts_ReadWSP9sOwnNumbers()
    {
        var (ds, _) = Run("margin_split_resonator.cnl");
        string spec = FirstWspSpec(ds);
        var probes  = WspSource.Probes(ds, WspSource.GroupOf(spec));

        foreach (var (metric, wantLinear, wantHz, wantText) in new[]
        {
            (WspMetric.SM_Y0, 0.12509, 1.59125e9, "minimum −18.1 dB at 1.5913 GHz"),
            (WspMetric.SM_H0, 0.10175, 1.73375e9, "minimum −19.8 dB at 1.7338 GHz"),
        })
        {
            var t = WspTrace(ds, spec, metric, probes[0].Label);
            t.Transform = CubeTransform.dB20;
            TraceResolve.SetCubeDataFrom(t, ds, PlotType.Rect, FreqUnit.GHz);

            var values = t.CubeReal!;
            var freqs  = t.CubeXValues!;
            int at = 0;
            for (int i = 1; i < values.Count; i++) if (values[i] < values[at]) at = i;
            Assert.Equal(wantLinear, values[at], 5);
            Assert.Equal(wantHz,     freqs[at],  0);

            var r = WspReadouts.For(t, ds, PlotType.Rect);
            Assert.NotNull(r);
            output.WriteLine($"{metric}: {r!.Value.Text}");
            Assert.StartsWith(wantText, r.Value.Text);

            // The DETECTOR that goes with the distance (WSP-9 §2.1f) — SM_Y0 pairs with 1/Y0. This
            // fixture is stable at R1 = −5 Ω, so the search reports "none" beside a low margin,
            // which is the pair of readings the card exists to put side by side.
            Assert.Contains(metric == WspMetric.SM_Y0 ? "Kurokawa on 1/Y0" : "Kurokawa on 1/H0",
                            r.Value.Text);
            Assert.Contains("none", r.Value.Text);
        }
    }

    /// <summary>
    /// The two horizontal reference lines are a property of the TRACE, decided once, so the renderer
    /// cannot disagree with the card about which plot gets them. The threshold is the RUN's own —
    /// read from the metadata cube the engine writes — and the −12 dB floor is arithmetic.
    /// </summary>
    [Fact]
    public void G_AMarginTrace_CarriesTheRunsOwnThresholdAndTheMinusTwelveFloor()
    {
        var (ds, _) = Run("margin_split_resonator.cnl");
        string spec = FirstWspSpec(ds);
        var probes  = WspSource.Probes(ds, WspSource.GroupOf(spec));

        var t = WspTrace(ds, spec, WspMetric.SM_Y0, probes[0].Label);
        t.Transform = CubeTransform.dB20;
        TraceResolve.SetCubeDataFrom(t, ds, PlotType.Rect, FreqUnit.GHz);

        Assert.True(t.ShowsWspMarginReferenceLines);
        Assert.Equal(-15.0, t.WspMarginThresholdDb);          // the engine's own default
        Assert.Equal(-12.0, PlotRenderer.WspMarginFloorDb);

        // The level is stated at the paper's 20*log10 and lands on the same RATIO whatever the axis
        // shows: it passes through on a dB20 axis, HALVES on a 10*log10 one, and becomes the linear
        // ratio on a magnitude axis.
        Assert.Equal(-12.0, t.WspMarginLevelInDisplayUnits(-12.0));
        t.Transform = CubeTransform.dB10;
        Assert.Equal(-6.0, t.WspMarginLevelInDisplayUnits(-12.0));
        t.Transform = CubeTransform.Mag;
        Assert.Equal(Math.Pow(10, -12.0 / 20.0), t.WspMarginLevelInDisplayUnits(-12.0), 12);

        // A complex-plane plot never gets them, whatever the transform says.
        TraceResolve.SetCubeDataFrom(t, ds, PlotType.Polar, FreqUnit.GHz);
        Assert.False(t.ShowsWspMarginReferenceLines);
    }

    /// <summary>
    /// A trace that stops being cube-bound stops being a probe trace, spec or no spec. The value
    /// path already returns early for a non-cube trace, so what this guards is the RENDERER: a
    /// lingering margin spec would go on drawing two horizontal reference lines under a curve that
    /// is no longer a margin — the same shape as the cube-versus-derived rule the card already
    /// carries.
    /// </summary>
    [Fact]
    public void ATraceThatStopsBeingCubeBound_StopsBeingAProbeTrace()
    {
        var (ds, _) = Run("margin_split_resonator.cnl");
        string spec = FirstWspSpec(ds);
        var t = WspTrace(ds, spec, WspMetric.SM_Y0,
                         WspSource.Probes(ds, WspSource.GroupOf(spec))[0].Label);
        t.Transform = CubeTransform.dB20;
        TraceResolve.SetCubeDataFrom(t, ds, PlotType.Rect, FreqUnit.GHz);
        Assert.True(t.ShowsWspMarginReferenceLines);

        t.CubeName   = null;
        t.Expression = null;
        Assert.False(t.IsWspTrace);
        Assert.False(t.ShowsWspMarginReferenceLines);
    }
}
