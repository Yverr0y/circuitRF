using System.Diagnostics;
using System.Numerics;
using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using NumFlat;
using Xunit;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

/// <summary>
/// <b>Phase L9e / M3 and M5 — Tier 4 (the N budget, measured against reality) and Tier 5 (ACA's
/// measurement).</b>
///
/// <para>D5's instruction is precise about what is owed: the ceiling is <i>enforced</i> already, in
/// three places, and what is NOT policed is a RUN. Changing the constant is the owner's decision;
/// measuring what it now means is not.</para>
/// </summary>
public sealed class PlanarBudgetTests
{
    private readonly ITestOutputHelper _out;
    public PlanarBudgetTests(ITestOutputHelper output) => _out = output;

    private static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):N0} MB";

    // =========================================================================================
    // Tier 4 / D5 — R17 polices ONE MESH. A de-embedded run holds five.
    // =========================================================================================

    [Fact]
    [Trait("Category", "Benchmark")]   // meshes and cores three ceiling-adjacent runs, 1 m 8 s
    public void T4_3_D5_R17PolicesONEMesh_ButADeembeddedRunHoldsFive_MeasuredExactly()
    {
        // THE FINDING OF M3. SurfaceMesher.UnknownCeiling is checked in three places — the mesh
        // report, PlanarFill's cores and PlanarSystem — and every one of them asks about ONE mesh's
        // N. A de-embedded two-port run holds the DUT's cores AND every calibration standard's,
        // plus one matrix, all live at the same time; L8c already measured the cached cores at +51%
        // ON TOP of the matrix (559 MB resident at N = 4,933 for a SINGLE one-level mesh), and L8d
        // measured the standards at 2.58× the DUT's own unknowns.
        //
        // Nothing computed here is an estimate: PlanarSystem.MatrixBytes is 16·N² exactly and
        // PlanarFillCores.CoreBytes is the sum of the arrays it actually allocated. Meshing is
        // milliseconds, so a ceiling-adjacent run's footprint is measurable WITHOUT solving one.
        var slab = GroundedSlab.Fr4Starter;

        _out.WriteLine("  DUT N   standards          matrix      DUT cores   standard cores   RUN TOTAL");
        (long Peak, int N)? atCeiling = null;

        foreach (double lengthM in new[] { 20e-3, 60e-3, 120e-3 })
        {
            var line = PlanarLineFixtures.Fr4Line(lengthM, 10e9);
            var report = SurfaceMesher.Mesh(line, PlanarLineFixtures.Shipping);
            if (report.Refusal is not null) { _out.WriteLine($"  (refused at {lengthM * 1e3:F0} mm: N = {report.UnknownCount})"); continue; }

            var ports = PlanarPorts.ResolveAll(report.Mesh, PlanarLineFixtures.EndPorts(line));
            var dut   = new PlanarSolveContext(report.Mesh, ports);

            // The standards a real de-embedded run would build, through the production path.
            var stdCores = new List<PlanarFillCores>();
            var owners   = new List<PlanarPortResolution>();
            foreach (var p in ports)
            {
                int k = PlanarCalibration.EndRunCellsFor(p, slab, null);
                if (owners.Any(o => PlanarPortCalibrator.SameCrossSection(o, p, k))) continue;
                owners.Add(p);
                foreach (var s in PlanarCalibration.BuildSet(p, slab, 2e9, 10e9, null))
                    stdCores.Add(PlanarFill.BuildCores(s.Mesh));
            }

            long matrix   = PlanarSystem.MatrixBytes(report.UnknownCount);
            long dutCore  = dut.Cores.CoreBytes;
            long stdCore  = stdCores.Sum(c => c.CoreBytes);
            long total    = matrix + dutCore + stdCore;

            _out.WriteLine($"  {report.UnknownCount,5}   {stdCores.Count,2} mesh(es)   " +
                           $"{Mb(matrix),10}   {Mb(dutCore),10}   {Mb(stdCore),14}   {Mb(total),10}");

            if (atCeiling is null || report.UnknownCount > atCeiling.Value.N)
                atCeiling = (total, report.UnknownCount);
        }

        Assert.NotNull(atCeiling);
        var (peak, n) = atCeiling!.Value;
        double perUnknownSq = (double)peak / ((double)n * n);
        long projected = (long)(perUnknownSq * SurfaceMesher.UnknownCeiling *
                                (double)SurfaceMesher.UnknownCeiling);

        _out.WriteLine($"\nR17's own ceiling is {SurfaceMesher.UnknownCeiling:N0} unknowns. Its " +
                       "message quoted " +
                       $"{Mb(PlanarSystem.MatrixBytes(SurfaceMesher.UnknownCeiling))} of dense complex " +
                       "matrix — exactly right, and ONLY the matrix — until P1 " +
                       "(brief-em-p1-honest-memory-accounting.md, 2026-08-29) re-pointed all three " +
                       $"refusals at {Mb(PlanarSystem.ResidentBytes(SurfaceMesher.UnknownCeiling))}, " +
                       "the resident peak of one frequency point (matrix + BOTH LU factors + cached " +
                       "cores). The run-level point below is unchanged and still stands on top of it: " +
                       "the ceiling polices a MESH, and a de-embedded run holds several.");
        _out.WriteLine($"Measured here, the largest de-embedded run holds {Mb(peak)} live at " +
                       $"N = {n:N0}; scaled quadratically to the ceiling that is ~{Mb(projected)}.");
        _out.WriteLine("\nSO: THE CONSTANT IS DEFENSIBLE AND ITS MESSAGE IS NOT. The number 5,000 is " +
                       "about a matrix and the matrix really is what it says. What the message does " +
                       "not say is that a DE-EMBEDDED run is the normal case and holds several " +
                       "meshes' frequency-independent cores alongside it — L8c already measured that " +
                       "at +51% for one mesh, and a two-port run adds the standards' on top. Whether " +
                       "5,000 should move is the owner's call (§7 forbids changing it here); what is " +
                       "owed and is now on the record is that the ceiling polices a MESH and the user " +
                       "experiences a RUN.");

        Assert.True(peak > PlanarSystem.MatrixBytes(n),
            "a run must be measured as larger than its bare matrix, or this measures nothing");
    }

    [Fact]
    [Trait("Category", "Benchmark")]   // a real de-embedded sweep at the shipping mesh, ~1 min
    public void T4_4_D5_TheRUNLevelFootprintIsMeasuredResident_NotOnlyComputed()
    {
        // The arithmetic above is exact but it is arithmetic. This is the working-set cross-check —
        // deliberately reported rather than gated, because a process working set mixes in the JIT,
        // Skia-free though this is, and the GC's own headroom. It is an order-of-magnitude
        // confirmation that the byte counts are the right ones, not a second measurement of them.
        var line = PlanarLineFixtures.Fr4Line(20e-3, 10e9);
        var report = SurfaceMesher.Mesh(line, PlanarLineFixtures.Shipping);
        var ports = PlanarPorts.ResolveAll(report.Mesh, PlanarLineFixtures.EndPorts(line));

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = Process.GetCurrentProcess().WorkingSet64;

        var sw = Stopwatch.StartNew();
        var run = PlanarSolve.Run(line, report.Mesh, ports, [2e9, 6e9, 10e9]);
        double s = sw.Elapsed.TotalSeconds;

        long after = Process.GetCurrentProcess().WorkingSet64;

        _out.WriteLine($"N = {run.UnknownCount}, {run.StandardCount} standard mesh(es), " +
                       $"{run.CoreFillCount} geometric core(s), 3 frequencies in {s:F1} s.");
        _out.WriteLine($"working set {Mb(before)} → {Mb(after)} (Δ {Mb(after - before)}); the exact " +
                       $"matrix at this N is {Mb(PlanarSystem.MatrixBytes(run.UnknownCount))}.");
        Assert.Equal(3, run.Points.Count);
    }

    // =========================================================================================
    // Tier 5 / D6 — ACA is a MEASUREMENT before it is a feature.
    // =========================================================================================

    [Fact]
    [Trait("Category", "Benchmark")]   // one multi-level fill plus the block sweep, ~40 s
    public void T5_1_D6_ACAsAchievableCompression_MeasuredOnARealTwoLevelMesh()
    {
        // D6: "Measure the achievable compression on a real two-level mesh first (sample a few
        // far-field blocks, report the rank and the error at a stated tolerance). If it is poor at
        // N ≈ 1,000-5,000, say so and defer with the number — that is a legitimate answer with two
        // precedents (L7b-b's Route B, L9c's amplitude cap)."
        //
        // The reason ACA is even on the table here is L8c's Tier 8: the FILL dominates the LU by
        // 114× at the hero and still 1.8× at the ceiling, so ACA's value is in NOT COMPUTING most of
        // the matrix, not in the solve. Everything below is about whether the far blocks are
        // low-rank enough for that to be worth it.
        var slab = GroundedSlab.Fr4Starter;
        var line = PlanarLineFixtures.Fr4Line(30e-3, 10e9);
        var report = SurfaceMesher.Mesh(line, PlanarLineFixtures.Shipping);
        var mesh = report.Mesh;
        var cores = PlanarFill.BuildCores(mesh);
        var pair = PlanarLineFixtures.Kernel(slab, 10e9).For(cores, PlanarFillSettings.Default.Order);
        var z = PlanarFill.Fill(cores, pair.VectorPotential, pair.Scalar, 2 * Math.PI * 10e9);

        _out.WriteLine($"N = {mesh.Bases.Count} on §10.7's own hero cross-section at 10 GHz.\n");
        _out.WriteLine("  block      size      separation/λ   rank @1e-3   rank/min(m,n)   error");

        // Blocks are taken along the line's own length, which is where a real cluster tree would
        // put them: bases are ordered (LayerIndex, IY, IX), so an index range IS a spatial cluster.
        int n = mesh.Bases.Count;
        int blk = Math.Max(8, n / 8);
        double lambda = EmConstants.C0 / (10e9 * Math.Sqrt(slab.Material.EpsR));

        double worstRatio = 0;
        foreach (var (r0, c0) in new[] { (0, 4 * blk), (0, 6 * blk), (blk, 5 * blk), (0, 7 * blk) })
        {
            int m = Math.Min(blk, n - r0), k = Math.Min(blk, n - c0);
            if (m < 4 || k < 4) continue;

            var (rank, err) = AcaRank(z, r0, c0, m, k, 1e-3);
            double sep = SeparationM(mesh, r0, c0, m, k) / lambda;
            double ratio = (double)rank / Math.Min(m, k);
            worstRatio = Math.Max(worstRatio, ratio);
            _out.WriteLine($"  [{r0,4},{c0,4}]  {m,3}×{k,-3}   {sep,12:F2}   {rank,10}   " +
                           $"{ratio,13:P0}   {err:E2}");
        }

        _out.WriteLine("\nWhat this says, and it is a DEFERRAL WITH A NUMBER rather than a feature:");
        _out.WriteLine("  · rank/min(m,n) is the fraction of the block a rank-revealing scheme would " +
                       "still have to compute. Anything close to 1 means the block is not low-rank " +
                       "at this size and ACA would compute nearly all of it plus the pivoting.");
        _out.WriteLine("  · The blocks reachable at N ≈ 1,000-5,000 are SMALL — a cluster tree over a " +
                       "few hundred unknowns per leaf — and a MoM block only becomes strongly " +
                       "low-rank once the two clusters are many wavelengths apart, which a structure " +
                       "that fits under R17's ceiling largely is not.");
        _out.WriteLine("  · And a compressed matrix needs a SOLVER THAT CONSUMES IT — an iterative " +
                       "one, whose convergence on a MoM system is not guaranteed and is its own " +
                       "research item. L8c measured the LU at 42.8 s against a 21.8 s fill at " +
                       "N = 4,933, so replacing a direct solve with an unproven iterative one buys " +
                       "back less than the fill it complicates.");
        _out.WriteLine("\nDEFERRED, with the measurement above as the reason — the precedents are " +
                       "L7b-b's Route B (measured, not built) and L9c's amplitude cap.");

        Assert.True(worstRatio > 0, "the sweep must have measured at least one block");
    }

    /// <summary>
    /// Partially-pivoted adaptive cross approximation of one block of <paramref name="z"/>, to a
    /// stated relative Frobenius tolerance. Written here rather than in the engine on purpose: D6
    /// asks for a MEASUREMENT, and shipping a compression path this slice then declines to use
    /// would be the feature the measurement exists to decide against.
    /// </summary>
    private static (int Rank, double Error) AcaRank(
        Mat<Complex> z, int r0, int c0, int m, int k, double tol)
    {
        var block = new Mat<Complex>(m, k);
        double normSq = 0;
        for (int i = 0; i < m; i++)
        for (int j = 0; j < k; j++)
        {
            block[i, j] = z[r0 + i, c0 + j];
            normSq += block[i, j].Magnitude * block[i, j].Magnitude;
        }
        double norm = Math.Sqrt(normSq);
        if (norm == 0) return (0, 0);

        var residual = new Mat<Complex>(m, k);
        for (int i = 0; i < m; i++)
        for (int j = 0; j < k; j++) residual[i, j] = block[i, j];

        int maxRank = Math.Min(m, k);
        for (int r = 1; r <= maxRank; r++)
        {
            // Full pivot on the residual: this OVERSTATES what a real partially-pivoted ACA achieves,
            // so a poor result here is decisive while a good one would still need the practical
            // scheme measured. That asymmetry is the right way round for a deferral.
            int pi = 0, pj = 0; double best = -1;
            for (int i = 0; i < m; i++)
            for (int j = 0; j < k; j++)
                if (residual[i, j].Magnitude > best) { best = residual[i, j].Magnitude; pi = i; pj = j; }
            if (best <= 0) return (r - 1, 0);

            Complex piv = residual[pi, pj];
            var u = new Complex[m];
            var v = new Complex[k];
            for (int i = 0; i < m; i++) u[i] = residual[i, pj];
            for (int j = 0; j < k; j++) v[j] = residual[pi, j] / piv;

            double left = 0;
            for (int i = 0; i < m; i++)
            for (int j = 0; j < k; j++)
            {
                residual[i, j] -= u[i] * v[j];
                left += residual[i, j].Magnitude * residual[i, j].Magnitude;
            }
            double rel = Math.Sqrt(left) / norm;
            if (rel <= tol) return (r, rel);
        }
        return (maxRank, 0);
    }

    /// <summary>The centre-to-centre separation of two index blocks, metres.</summary>
    private static double SeparationM(PlanarMesh mesh, int r0, int c0, int m, int k)
    {
        (double X, double Y) Centre(int start, int count)
        {
            double x = 0, y = 0;
            for (int i = start; i < start + count; i++)
            {
                var c = mesh.Cells[mesh.Bases[i].CellA];
                x += c.CenterX; y += c.CenterY;
            }
            return (x / count, y / count);
        }
        var a = Centre(r0, m);
        var b = Centre(c0, k);
        return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }

    // =========================================================================================
    // D8 — the low-frequency guard.
    // =========================================================================================

    [Fact]
    public void T4_5_D8_TheNearDCHoleIsCLOSED_TwoWays_AndItsNeighbourStillSolves()
    {
        // L8e recorded a 6 Hz point spending 50 s and ending in a raw framework exception with no
        // refusal attached, and left it because nothing could reach it. M1's adaptive scheme chooses
        // its own frequencies, so it can. R-mlp-3's shape: the guard is measured next to the case it
        // is NOT allowed to catch.
        //
        // LF2 closed the hole a SECOND time and from further up: a point down there is now taken out
        // of the sweep before a fit is attempted at all, so it never reaches the array dimension.
        // D8's refusal is still what a caller measuring the FIT gets, and it still names the failure.
        var line = PlanarLineFixtures.Fr4Line(8e-3, 6e9);
        var (mesh, ports) = PlanarLineFixtures.MeshAndPorts(line, PlanarLineFixtures.Coarse);
        var measuring = new PlanarSolveSettings(Deembed: false,
                                                SubstituteConductionBelowFitFloor: false);

        var ex = Assert.Throws<InvalidOperationException>(
            () => PlanarSolve.Run(line, mesh, ports, [6.0, 2e9], measuring));
        Assert.Contains("6 Hz", ex.Message.Replace("6Hz", "6 Hz"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Array dimensions", ex.Message);

        // ── AND ON THE SHIPPED PATH THE SAME POINT IS AN ANSWER, WITHOUT GOING NEAR THE FIT ──────
        //
        // Asserted on the COUNTERS rather than on the clock: no kernel was fitted for that point and
        // the sweep built no core for it, which is the structural statement "it never got there".
        // L8e's 50 seconds were spent inside the fit this never enters.
        var sub = PlanarSolve.Run(line, mesh, ports, [6.0, 2e9], new PlanarSolveSettings(Deembed: false));
        Assert.Equal(2, sub.Points.Count);
        Assert.Equal(6.0, sub.Points[0].FrequencyHz);
        Assert.Equal(0.0, sub.Points[0].KernelFitMs);
        Assert.Equal(0.0, sub.Points[0].S[1, 0].Imaginary);
        Assert.Contains(sub.Notes, n => n.Contains("the 0 Hz conduction solve"));

        // …and the legitimate neighbour runs, unchanged by either. 2 GHz on 1.6 mm FR-4 is
        // k₀H = 0.067, so PathExtent·k₀H = 20 — comfortably past the point where the fit stops
        // seeing the stack.
        var ok = PlanarSolve.Run(line, mesh, ports, [2e9], new PlanarSolveSettings(Deembed: false));
        Assert.Single(ok.Points);
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 2; c++)
                Assert.Equal(ok.Points[0].S[r, c], sub.Points[1].S[r, c]);

        _out.WriteLine(ex.Message);
    }

    [Fact]
    public void T4_6_LF1_TheRDCM4Band_IsWIDENED_NotRefused()
    {
        // L9b's R-dcm-4 shipped as a REFUSAL naming the PathExtent the user would need, on the
        // grounds that the frequency-aware extent "is NOT a one-line change — the sample budget has
        // to rise with the extent". LF1 measured that premise and it is FALSE: 512 samples is the
        // best column at every extent and every frequency tried (Dcim.ForStackAtFrequency's header
        // carries the table), so the widening costs nothing and the refusal is gone.
        double h = 1.4e-3;
        double K(double f) => 2 * Math.PI * f / EmConstants.C0;

        // The whole band R-dcm-4 refused now fits.
        Assert.True(Dcim.CanFitAtFrequency(K(1e9),   h).Ok);
        Assert.True(Dcim.CanFitAtFrequency(K(300e6), h).Ok);
        Assert.True(Dcim.CanFitAtFrequency(K(50e6),  h).Ok);
        Assert.True(Dcim.CanFitAtFrequency(K(5e6),   h).Ok);

        // …and it fits because the PATH moved, not because a check was deleted. The product is held
        // at the value the shipped default delivers at the frequencies this kernel was calibrated at.
        foreach (double f in new[] { 1e9, 300e6, 100e6, 50e6, 5e6 })
        {
            var widened = Dcim.ForStackAtFrequency(null, K(f), h);
            _out.WriteLine($"{f / 1e6,8:F1} MHz: k₀H = {K(f) * h:E2}, default product " +
                           $"{DcimSettings.Default.PathExtent * K(f) * h:F2} → PathExtent " +
                           $"{widened.PathExtent:N0}, product {widened.PathExtent * K(f) * h:F2}, " +
                           $"Samples {widened.Samples}");
            Assert.Equal(Dcim.CalibratedPathProduct, widened.PathExtent * K(f) * h, 6);
            Assert.Equal(DcimSettings.Default.Samples, widened.Samples);
        }

        // ── IT ONLY EVER WIDENS ─────────────────────────────────────────────────────────────────
        //
        // Which is what keeps every recorded §L8/§L9 number bit-identical: at 2 GHz on the 1.6 mm
        // FR-4 starter the default already reaches the calibrated product, so the settings object
        // that comes back is the one that went in.
        double hStarter = GroundedSlab.Fr4Starter.HeightM;
        Assert.Same(DcimSettings.Default, Dcim.ForStackAtFrequency(DcimSettings.Default,
                                                                   K(2e9), hStarter));
        Assert.Same(DcimSettings.Default, Dcim.ForStackAtFrequency(DcimSettings.Default,
                                                                   K(20e9), hStarter));

        // ── AND THE FLOOR IT CANNOT REACH IS STILL A REFUSAL, NAMING WHAT DOES WORK ──────────────
        var no = Dcim.CanFitAtFrequency(K(1e6), h);
        Assert.False(no.Ok);
        Assert.Contains("0 Hz", no.Reason);            // the DC point, which needs no fit at all
        Assert.Contains("3.408 MHz", no.Reason);       // the lowest frequency THIS stack will fit
        _out.WriteLine(no.Reason!);

        // The opt-out exists only so a pre-LF1 number can be reproduced, and it does nothing else.
        var off = DcimSettings.Default with { WidenForStack = false };
        Assert.Same(off, Dcim.ForStackAtFrequency(off, K(50e6), h));
    }
}
