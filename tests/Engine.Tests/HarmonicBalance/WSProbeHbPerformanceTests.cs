// ================================================================
//  WSProbeHbPerformanceTests.cs — brief-wsprobe-8's gates (R-wsp8-9 (a)–(g)).
//
//  WSP-8 changes no answer. It removes work: WSP-5's straightforward path factors the linear
//  partition 2·K_ss + 2 times per probe frequency PER OPERATING POINT, and every one of those
//  factorisations is of a matrix that does not depend on the drive. So the sparse work moves into a
//  cache keyed by frequency, the per-operating-point work becomes dense-only, and the tickle grid
//  parallelises.
//
//  Everything here is therefore a COUNTER or an EQUALITY — owner rule, and overview D-14: no test in
//  this repository asserts a wall clock. The measured timings live in `src/Engine/RESOLVED.md`,
//  taken with a scratch harness in a Release build.
//
//  The gate that carries the weight is (b): the same wsp, to 1e-12 relative, from
//  HbSmallSignal.SolveProbesStraightforward — which is WSP-5's implementation, kept for exactly
//  this comparison. WSP-5's own independent oracle (the two-tone HB run at the probe's terminals)
//  runs against the fast path and still passes, in WSProbeHbTests.
// ================================================================

using System.Numerics;
using CircuitRF.Core.Design;
using CircuitRF.Core.Elaboration;
using CircuitRF.Core.Netlist;
using CircuitRF.Engine.HarmonicBalance;
using RfCore.Data;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.HarmonicBalance;

public sealed class WSProbeHbPerformanceTests(ITestOutputHelper output)
{
    // ── Fixtures ────────────────────────────────────────────────────────────

    private static string TestData(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var cand = Path.Combine(dir, "testdata");
            if (Directory.Exists(cand)) return Path.Combine([cand, .. parts]);
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("testdata not found above " + AppContext.BaseDirectory);
    }

    /// <summary>The WSP-5 fixture, with extra keys on its analysis line and optional extra lines —
    /// written to a temp file and read back through the ordinary reader, so every test drives the
    /// code path a `.cnl` on disk does.</summary>
    private static (Library Lib, TestBench Tb, ElaboratedNetlist Nl) Load(
        string analysisExtra = "", string[]? extraLines = null,
        (string Var, string Value)[]? globals = null,
        (string StartsWith, string Replacement)[]? rewrite = null)
    {
        var lines = File.ReadAllLines(TestData("wsprobe", "hb_pumped_two_port.cnl")).ToList();
        foreach (var (prefix, replacement) in rewrite ?? [])
        {
            int at = lines.FindIndex(l => l.TrimStart().StartsWith(prefix, StringComparison.Ordinal));
            Assert.True(at >= 0, $"fixture has no line starting '{prefix}'");
            lines[at] = replacement;
        }
        foreach (var (v, val) in globals ?? [])
        {
            int at = lines.FindIndex(l => l.TrimStart().StartsWith(v + " ", StringComparison.Ordinal)
                                       || l.TrimStart().StartsWith(v + "=", StringComparison.Ordinal));
            Assert.True(at >= 0, $"fixture has no global '{v}'");
            lines[at] = $"{v} = {val}";
        }

        int ai = lines.FindIndex(l => l.TrimStart().StartsWith("analysis ", StringComparison.Ordinal));
        if (analysisExtra.Length > 0) lines[ai] += " " + analysisExtra;
        if (extraLines is not null) lines.InsertRange(ai, extraLines);

        var tmp = Path.Combine(Path.GetTempPath(), $"wsp8_{Guid.NewGuid():N}.cnl");
        File.WriteAllLines(tmp, lines);
        try
        {
            var (lib, tb) = CnlReader.ReadFile(tmp);
            return (lib, tb, new Elaborator(lib).Elaborate(tb));
        }
        finally { File.Delete(tmp); }
    }

    private static (DataSet Ds, HbEngine Eng, HbAnalysisParams P, ElaboratedNetlist Nl) RunOne(
        string analysisExtra, AnalysisSettings? settings = null,
        HbSmallSignalCache? cache = null, string[]? extraLines = null,
        (string StartsWith, string Replacement)[]? rewrite = null)
    {
        var (lib, tb, nl) = Load(analysisExtra, extraLines, rewrite: rewrite);
        var hba = tb.Analyses.OfType<HarmonicBalanceAnalysis>().Single();
        var p   = HbEngine.Resolve(hba, nl.ResolvedGlobals, nl.GlobalsWithExplicitUnit);
        var eng = new HbEngine(nl, tb, settings ?? new AnalysisSettings(), cache, lib, null);
        return (eng.Run(p).DataSet, eng, p, nl);
    }

    /// <summary>A drive sweep, run exactly as <c>ParametricSweepEngine</c> runs one — through the
    /// engine itself, with a cache the test can then interrogate.</summary>
    private static (DataSet Ds, HbSmallSignalCache Cache) RunDriveSweep(
        double[] pumpVolts, string analysisExtra, AnalysisSettings? settings = null,
        HbSmallSignalCache? cache = null, string[]? extraLines = null)
        => RunSweep("Vpump", pumpVolts, analysisExtra, settings, cache, extraLines);

    private static (DataSet Ds, HbSmallSignalCache Cache) RunSweep(
        string varName, double[] values, string analysisExtra, AnalysisSettings? settings = null,
        HbSmallSignalCache? cache = null, string[]? extraLines = null,
        (string StartsWith, string Replacement)[]? rewrite = null)
    {
        settings ??= new AnalysisSettings();
        var sweepLine =
            $"analysis SW1 type=parametric_sweep Var={varName} Values={string.Join(",", values)} Inner=HB1";
        var (lib, tb, _) = Load(analysisExtra, extraLines: [.. extraLines ?? [], sweepLine], rewrite: rewrite);
        var sweep = tb.Analyses.OfType<ParametricSweepAnalysis>().Single();
        cache ??= new HbSmallSignalCache(settings);
        var ds = ParametricSweepEngine.Run(sweep, lib, tb, settings, null, null, cache);
        return (ds, cache);
    }

    private const string Grid = "SSStart=0.45 SSStop=3.25 SSNpts=5 SSUnit=GHz SSMaxHarm=3";
    private const int    NInt = 2;      // the SDD's two ports are the interface
    private const int    NProbe = 2;    // GATE and DRAIN

    // ══ (a) — zero sparse work after the first operating point ═══════════════

    /// <summary>
    /// R-wsp8-9(a), and the whole point of the brief. Over a drive sweep, the linear partition is
    /// factored <b>once per distinct sideband frequency for the entire sweep</b> — not once per
    /// sideband per probe frequency per operating point. Points 2…P add nothing at all.
    ///
    /// <para>The first point pays exactly <c>distinct(ω)</c> factorisations and
    /// <c>distinct(ω)·N_int + M·2N</c> transposed solves: <c>N_int</c> interface rows of
    /// <c>M⁻¹</c> at every frequency, and <c>2N</c> reading rows at each of the <c>M</c> probe
    /// frequencies. Nothing else is sparse.</para>
    /// </summary>
    [Fact]
    public void NoSparseWorkSurvivesTheFirstOperatingPoint()
    {
        var single = RunOne(Grid);
        var c0 = single.Eng.LastSmallSignalCounters!;
        int distinct = c0.DistinctSidebandFrequencies;
        const int m = 5;

        Assert.Equal(distinct, c0.SparseFactorizations);
        Assert.Equal(distinct * NInt + m * 2 * NProbe, c0.TransposedSolves);
        Assert.Equal(0, c0.SparseSolves);

        // Now the same probe sweep at five drive levels, sharing one cache.
        var (_, cache) = RunDriveSweep([0.30, 0.375, 0.45, 0.525, 0.60], Grid);

        output.WriteLine($"one point: {c0.SparseFactorizations} factorisations, " +
                         $"{c0.TransposedSolves} transposed solves; five points: " +
                         $"{cache.Factorizations} / {cache.TransposedSolves}, " +
                         $"{cache.Stamps} stamps, {cache.Hits} cache hits");

        Assert.Equal(distinct, cache.Factorizations);
        Assert.Equal(distinct * NInt + m * 2 * NProbe, cache.TransposedSolves);

        // The one thing every later operating point still pays: a stamp and a bit-for-bit
        // comparison per frequency, which is what makes the reuse verified rather than assumed.
        Assert.True(cache.Stamps > cache.Factorizations,
            "the certificate comparison is not free and the counter should say so");
    }

    // ══ (b) — identity with WSP-5's straightforward path ═════════════════════

    /// <summary>
    /// R-wsp8-9(b). <c>wsp</c> from the shipped path equals <c>wsp</c> from
    /// <see cref="HbSmallSignal.SolveProbesStraightforward"/> — WSP-5's implementation, which
    /// factors the linear partition at every sideband and back-solves it four times per injection —
    /// to 1e-12 relative at every <c>(ssfreq, row, col)</c>.
    ///
    /// <para>The two paths share the conversion matrix and the dense solve and share nothing else:
    /// the straightforward one takes forward solves of <c>M</c>, this one takes rows of <c>M⁻¹</c>
    /// from HERMITIAN transposed solves and contracts them. A missing conjugation, a swapped
    /// injection sign or a transposed <c>W</c> would all show up here and nowhere else, because
    /// each of them preserves magnitudes.</para>
    /// </summary>
    [Fact]
    public void TheFastPathEqualsTheStraightforwardOne()
    {
        var (ds, eng, p, nl) = RunOne(Grid);
        var lp = eng.LastLinearizedPoint;
        Assert.NotNull(lp);

        var sb = new HbSmallSignal.ToneSidebands(lp.G, lp.C, lp.Buckets, p.SsOrder, lp.Omega0);
        var probes = nl.WspProbes
            .Select(w => new HbSmallSignal.ProbeSite(
                w.ComponentIndex, w.Label, w.Idx,
                nl.Components[w.ComponentIndex].Nodes[0], nl.Components[w.ComponentIndex].Nodes[1]))
            .ToArray();

        var counters = new HbSmallSignal.Counters();
        var reference = HbSmallSignal.SolveProbesStraightforward(
            nl, new HbLinearExtractor(nl, new AnalysisSettings()), sb, probes, p.SsGrid,
            lp.YNN[0], counters, "the fundamental");

        output.WriteLine($"reference path: {counters.LinearPartitions} partitions, " +
                         $"{counters.SparseSolves} sparse solves for {p.SsGrid.Length} points");
        Assert.True(counters.SparseSolves > 0, "the oracle must actually do the sparse work");

        double worst = 0;
        for (int fi = 0; fi < p.SsGrid.Length; fi++)
        for (int r = 0; r < 2 * NProbe; r++)
        for (int cc = 0; cc < 2 * NProbe; cc++)
        {
            var a = (Complex)ds["wsp"][fi, r, cc];
            var b = reference[fi][r, cc];
            double scale = Math.Max(Complex.Abs(b), 1e-30);
            worst = Math.Max(worst, Complex.Abs(a - b) / scale);
        }
        output.WriteLine($"worst relative deviation from the straightforward path: {worst:E3}");
        Assert.True(worst < 1e-12, $"worst relative deviation {worst:E3}");
    }

    // ══ (c) — pattern reuse ═════════════════════════════════════════════════

    /// <summary>
    /// R-wsp8-9(c). The small-signal sweep stamps every one of its frequencies into ONE
    /// <c>MnaSystem</c>, so the sparsity pattern and the AMD ordering are built once for the whole
    /// drive sweep rather than once per frequency. The extractor's own per-omega cache is right for
    /// the handful of harmonics an HB solve visits and wrong for thousands of one-shot frequencies.
    ///
    /// <para><b>The brief's own arithmetic for this gate counts SIGNED sidebands and the
    /// factorisation count is lower</b> — <c>Y(−ω) = conj(Y(ω))</c> is a theorem, so one entry
    /// serves <c>+ω</c> and <c>−ω</c> both and the folded count is what gets factored. Both numbers
    /// are reported; this asserts the one that is actually the work.</para>
    /// </summary>
    [Fact]
    public void ThePatternIsBuiltOncePerWorkerForTheWholeSweep()
    {
        var (_, cache) = RunDriveSweep([0.35, 0.45, 0.55], Grid);
        Assert.Equal(1, cache.PatternBuilds);

        var single = RunOne(Grid);
        var c = single.Eng.LastSmallSignalCounters!;
        Assert.Equal(c.DistinctSidebandFrequencies, cache.Factorizations);
        Assert.True(c.DistinctSidebandFrequencies <= c.SignedSidebandFrequencies);
        output.WriteLine($"{c.TotalSidebandVisits} visits → {c.SignedSidebandFrequencies} signed → " +
                         $"{c.DistinctSidebandFrequencies} folded; pattern builds {cache.PatternBuilds}");
    }

    // ══ (d) — sideband coincidence, and the grid is untouched ═══════════════

    /// <summary>
    /// R-wsp8-9(d). When the tickle step divides the fundamental, the sidebands of one probe
    /// frequency ARE the sidebands of others and the sparse work collapses by that ratio; when it
    /// does not, nothing coincides. Either way <b>the grid is exactly the one the user asked for</b>
    /// — the AUT-8 rule: a frequency the user wrote is a frequency the engine uses. The saving is
    /// REPORTED so a designer can choose an aligned step, never taken by moving their points.
    /// </summary>
    [Fact]
    public void AnAlignedStepSharesSidebands_AndTheGridIsNeverMoved()
    {
        // f0 = 2 GHz. A 0.25 GHz step is f0/8; a 0.27 GHz step divides nothing.
        var aligned   = RunOne("SSStart=0.35 SSStop=2.35 SSNpts=9 SSUnit=GHz SSMaxHarm=3");
        var unaligned = RunOne("SSStart=0.35 SSStop=2.51 SSNpts=9 SSUnit=GHz SSMaxHarm=3");

        var a = aligned.Eng.LastSmallSignalCounters!;
        var u = unaligned.Eng.LastSmallSignalCounters!;
        output.WriteLine($"aligned:   {a.TotalSidebandVisits} visits → {a.SignedSidebandFrequencies} " +
                         $"signed → {a.DistinctSidebandFrequencies} factored");
        output.WriteLine($"unaligned: {u.TotalSidebandVisits} visits → {u.SignedSidebandFrequencies} " +
                         $"signed → {u.DistinctSidebandFrequencies} factored");

        Assert.Equal(a.TotalSidebandVisits, u.TotalSidebandVisits);
        Assert.True(a.SignedSidebandFrequencies < u.SignedSidebandFrequencies,
            "an aligned step must share sidebands between points");
        Assert.Equal(u.TotalSidebandVisits, u.SignedSidebandFrequencies);   // nothing coincides
        Assert.True(a.SparseFactorizations < u.SparseFactorizations);

        // The ssfreq axis is the requested grid exactly, in both cases.
        var want = Enumerable.Range(0, 9).Select(i => 0.35e9 + i * 0.25e9).ToArray();
        Assert.Equal(want, aligned.Ds["wsp"].Axes[0].Values.Select(v => Math.Round(v, 3)));

        Assert.Contains(aligned.Nl.Notes, n => n.Contains("the grid step is f0/8"));
        Assert.Contains(unaligned.Nl.Notes, n => n.Contains("does not divide f0"));
        Assert.Contains(aligned.Nl.Notes, n => n.Contains("The grid itself is untouched."));
    }

    // ══ (e) — invalidation ══════════════════════════════════════════════════

    /// <summary>
    /// R-wsp8-9(e). A swept element IN the linear partition changes the matrix, and the entries
    /// computed from the old one are replaced — detected by comparing the matrix just stamped
    /// against the one the entry was computed from, bit for bit, with no cooperation from any
    /// caller. The recomputation costs exactly one more full set of factorisations, and the answer
    /// equals a fresh run at that value.
    ///
    /// <para>This is the mechanism a loadpull tuner override, a re-configured termination and a
    /// swept passive all reach; an "invalidate after you mutate" protocol would answer the same
    /// question and be silently wrong the first time someone forgot.</para>
    /// </summary>
    [Fact]
    public void ASweptLinearElementIsDetectedAndRecomputed()
    {
        // Rl is a plain resistor in the linear partition; pointing it at a swept global changes the
        // stamped matrix at every frequency, at every point of the sweep.
        (string, string)[] rewrite = [("R:Rl", "R:Rl          n_out 0    R=Rload")];
        string[] extra = ["Rload = 50"];

        var (ds, cache) = RunSweep("Rload", [50, 75], Grid, extraLines: extra, rewrite: rewrite);
        int distinct = RunOne(Grid, extraLines: extra, rewrite: rewrite)
                       .Eng.LastSmallSignalCounters!.DistinctSidebandFrequencies;

        output.WriteLine($"two linear-partition values cost {cache.Factorizations} factorisations " +
                         $"against {distinct} for one — the certificate caught the change");
        Assert.Equal(2 * distinct, cache.Factorizations);

        // …and the swept point equals a fresh run at that value: the recomputation is a
        // recomputation, not a partially-refreshed entry.
        //
        // NOT bit-identical, and the reason is not the cache. The sweep WARM-STARTS each Newton
        // solve from the previous point's converged spectrum (§11 continuation), so the operating
        // point the second run linearises about differs from a cold one within the HB tolerance —
        // here Tol = 1e-10, and the wsp entries agree to ~1e-10 relative. The linear partition is
        // reproduced exactly; the periodic steady state it is linearised around is reproduced to
        // the tolerance that was asked for.
        var fresh = RunOne(Grid, extraLines: ["Rload = 75"], rewrite: rewrite);
        double worst = 0;
        for (int fi = 0; fi < 5; fi++)
        for (int r = 0; r < 2 * NProbe; r++)
        for (int c = 0; c < 2 * NProbe; c++)
        {
            var want = (Complex)fresh.Ds["wsp"][fi, r, c];
            var got  = (Complex)ds["wsp"][1, fi, r, c];
            worst = Math.Max(worst, Complex.Abs(got - want) / Math.Max(Complex.Abs(want), 1e-30));
        }
        output.WriteLine($"swept point vs a fresh run at the same value: {worst:E3} relative");
        Assert.True(worst < 1e-7, $"worst relative deviation {worst:E3}");
    }

    // ══ (f) — the budget fallback ═══════════════════════════════════════════

    /// <summary>
    /// R-wsp8-9(f). <c>WspCacheBudgetMB = 1</c> on a sweep whose projected cache does not fit: the
    /// results are identical, the run says so ONCE with the sizes, and the counters show the
    /// fallback's per-operating-point work.
    ///
    /// <para>A run that silently got slower is a run nobody can explain, which is why this is a
    /// note rather than an internal decision.</para>
    /// </summary>
    [Fact]
    public void OverBudget_KeepsTheAnswerAndSaysWhatItGaveUp()
    {
        var full  = RunDriveSweep([0.35, 0.45], Grid);
        var tight = RunDriveSweep([0.35, 0.45], Grid,
                                  new AnalysisSettings { WspCacheBudgetMB = 0 });

        output.WriteLine($"full:  {full.Cache.Factorizations} factorisations, " +
                         $"{full.Cache.EntryCount} entries held");
        output.WriteLine($"tight: {tight.Cache.Factorizations} factorisations, " +
                         $"{tight.Cache.EntryCount} entries held");

        Assert.Equal(0, tight.Cache.EntryCount);
        Assert.True(tight.Cache.Factorizations > full.Cache.Factorizations,
            "a cache that keeps nothing must refactor at every operating point");

        var a = full.Ds["wsp"];
        var b = tight.Ds["wsp"];
        for (int pi = 0; pi < 2; pi++)
        for (int fi = 0; fi < 5; fi++)
        for (int r = 0; r < 2 * NProbe; r++)
        for (int c = 0; c < 2 * NProbe; c++)
            Assert.Equal((Complex)a[pi, fi, r, c], (Complex)b[pi, fi, r, c]);
    }

    /// <summary>The sentence itself, and that it is said once — with the projection that produced
    /// the decision, so the reader can size <c>WspCacheBudgetMB</c> rather than guess at it.</summary>
    [Fact]
    public void TheOverBudgetNoteNamesTheSizesAndTheSetting()
    {
        var (_, _, _, nl) = RunOne(Grid, new AnalysisSettings { WspCacheBudgetMB = 0 });
        var note = nl.Notes.Where(n => n.Contains("WspCacheBudgetMB")).ToList();
        Assert.Single(note);
        output.WriteLine(note[0]);
        Assert.Contains("MB", note[0]);
        Assert.Contains("The results are identical", note[0]);
    }

    // ══ (g) — parallel identity ═════════════════════════════════════════════

    /// <summary>
    /// R-wsp8-9(g). The tickle grid split four ways gives a <b>bit-identical</b> <c>wsp</c>: each
    /// point's arithmetic is unchanged and each worker writes only its own slice by index, so
    /// nothing is merged and nothing is reordered. Each worker brings its own elaborated netlist,
    /// its own extractor and its own cache slice — the copies are the whole thread-safety story,
    /// because a model writes state during <c>Stamp</c>.
    /// </summary>
    [Fact]
    public void ParallelIsBitIdenticalAndTheCountersSum()
    {
        const string wide = "SSStart=0.41 SSStop=3.29 SSNpts=145 SSUnit=GHz SSMaxHarm=2";
        var serial   = RunOne(wide, new AnalysisSettings { MaxParallelism = 1 });
        var parallel = RunOne(wide, new AnalysisSettings { MaxParallelism = 4 });

        Assert.Equal(1, serial.Eng.PlanSmallSignalDegree(145, Sidebands(serial)));
        Assert.Equal(4, parallel.Eng.PlanSmallSignalDegree(145, Sidebands(parallel)));

        var a = serial.Ds["wsp"];
        var b = parallel.Ds["wsp"];
        for (int fi = 0; fi < 145; fi++)
        for (int r = 0; r < 2 * NProbe; r++)
        for (int c = 0; c < 2 * NProbe; c++)
            Assert.Equal((Complex)a[fi, r, c], (Complex)b[fi, r, c]);

        // Bit-identity here is NOT free the way the S-parameter sweep's is. That sweep shares
        // nothing between points; this one deliberately shares sidebands, and a shared frequency is
        // reached by several spellings of ω_ss + k·ω0 that differ in their last bits. Whichever
        // spelling created the entry would otherwise decide the last bits of the answer — and
        // chunking changes which one that is, so the result would agree at one degree and not
        // another. HbSmallSignalCache.Canonical rounds the frequency before it is used, which is
        // what makes the entry a function of the value alone.

        var cs = serial.Eng.LastSmallSignalCounters!;
        var cp = parallel.Eng.LastSmallSignalCounters!;
        output.WriteLine($"serial   {cs.SparseFactorizations} LU / {cs.TransposedSolves} solves / " +
                         $"{cs.DenseFactorizations} dense LU");
        output.WriteLine($"parallel {cp.SparseFactorizations} LU / {cp.TransposedSolves} solves / " +
                         $"{cp.DenseFactorizations} dense LU");

        // The dense work is per point and cannot change; the sparse work is per DISTINCT frequency
        // and each worker owns its own chunk, so a shared sideband that straddles two chunks is
        // factored once in each — never fewer, never more than the serial count times the degree.
        Assert.Equal(cs.DenseFactorizations, cp.DenseFactorizations);
        Assert.Equal(cs.DenseSolves,         cp.DenseSolves);
        Assert.Equal(0,                      cp.SparseSolves);
        Assert.True(cp.SparseFactorizations >= cs.SparseFactorizations);
        Assert.True(cp.SparseFactorizations <= 4 * cs.SparseFactorizations);
    }

    private static HbSmallSignal.ISidebands Sidebands(
        (DataSet Ds, HbEngine Eng, HbAnalysisParams P, ElaboratedNetlist Nl) run)
    {
        var lp = run.Eng.LastLinearizedPoint!;
        return new HbSmallSignal.ToneSidebands(lp.G, lp.C, lp.Buckets, run.P.SsOrder, lp.Omega0);
    }
}
