using System.Numerics;
using System.Runtime.ExceptionServices;
using CircuitRF.Core;
using CircuitRF.Core.Design;
using CircuitRF.Core.Devices;
using CircuitRF.Core.Devices.External;
using CircuitRF.Core.Elaboration;
using CSparse.Complex.Factorization;
using NumFlat;
using RfCore;
using RfCore.Data;
using RfCore.Stability;

namespace CircuitRF.Engine;

/// <summary>
/// Runs an S-parameter sweep over a frequency grid (linear-engine §2, §6, §9).
///
/// Two code paths, chosen once per run based on port Z0 values:
///
///   WAVE PATH (allPortsResistive — Re(Z0) > 0 for every port, the common case):
///     Each port stamps a conductance 1/Z0 between its nodes (no branch unknown).
///     Excitation: current injection 2√(Re Z0)/Z0 at the driven port.
///     S read directly from port voltages via the power-wave (Kurokawa) formula.
///     No Y→S inversion step. Parallel ports / port-across-short topologies are
///     non-singular by construction. Regularization is a genuine last resort.
///
///   LEGACY PATH (any port has Re(Z0) ≤ 0, e.g. reactive reference impedance):
///     Each port stamps an ideal 0 V branch. Unit-voltage excitation at each port.
///     Y-matrix extracted from branch currents; S = RFNetwork.YToS(Y, z0).
///     Keeps the legacy singular-matrix behavior for genuinely ill-posed circuits.
///     HB/DC are never affected (they already treat Port/Term as inert).
///
/// Regularization tri-state (<see cref="RegularizationMode"/>):
///   IfNecessary: first attempt with no regularization; if singular, retry with both.
///   Always:      apply regularization before first factorization.
///   Never:       no regularization; throw <see cref="SingularMatrixException"/> on singular.
/// </summary>
public static class SParameterEngine
{
    /// <summary>gmin conductance added from every node to ground (§5).</summary>
    public const double DefaultGmin = 1e-12;

    /// <param name="control">
    /// Optional cancellation + progress. Checked (and, when it carries a progress sink, ticked) once
    /// per FREQUENCY POINT — never inside a factorization or a port back-substitution. Null keeps the
    /// pre-cancellation behaviour exactly.
    /// </param>
    public static DataSet Run(
        ElaboratedNetlist netlist,
        double[]          freqsHz,
        AnalysisSettings? settings = null,
        RunControl?       control  = null)
    {
        settings ??= AnalysisSettings.Default;

        return RunWithStats(netlist, freqsHz, settings, control).Data;
    }

    /// <summary>
    /// The work a run did, as COUNTS: how many factorisations and how many back-substitutions.
    /// Exposed for the WSProbe gate (brief-wsprobe-1 R-wsp1-14(l)): "one factorisation per
    /// frequency, and <c>N_ports + 2·N_probes</c> solves against it" is a structural property, and a
    /// count holds it where a timing would only measure the machine (owner rule, 2026-08-23).
    /// </summary>
    public sealed record RunStats(int Factorizations, int BackSubstitutions, int PatternBuilds);

    /// <summary>The serial sweep, with its <see cref="RunStats"/>. What <see cref="Run(ElaboratedNetlist, double[], AnalysisSettings?, RunControl?)"/> calls.</summary>
    public static (DataSet Data, RunStats Stats) RunWithStats(
        ElaboratedNetlist netlist,
        double[]          freqsHz,
        AnalysisSettings? settings = null,
        RunControl?       control  = null)
    {
        settings ??= AnalysisSettings.Default;

        var prep      = Prepare(netlist, freqsHz, settings);
        var sMatrices = new Mat<Complex>[freqsHz.Length];
        var wsp       = prep.Probes.Length > 0 ? new Complex[freqsHz.Length][,] : null;
        RunRange(prep, freqsHz, settings, 0, freqsHz.Length, sMatrices, wsp, control, abort: null);
        var stats = new RunStats(
            prep.Mna.Factorizations + (prep.MnaTerminated?.Factorizations ?? 0),
            prep.BackSubstitutions,
            prep.Mna.PatternBuilds);
        return (BuildDataSet(netlist, freqsHz, sMatrices, prep.Z0PerPort, prep.Probes, wsp, settings), stats);
    }

    // ── Frequency-parallel overload (SP-P3) ───────────────────────────────────

    /// <summary>
    /// The same sweep, with contiguous chunks of the frequency grid solved at once on separately
    /// elaborated copies of <paramref name="tb"/>.
    ///
    /// <para><b>The copies are the whole thread-safety story.</b> Nine models write a branch index
    /// during <c>Stamp</c>, <see cref="SnpModel"/> loads its file lazily, several microstrip models
    /// accumulate warnings, and an SDD carries resolved control-branch indices — all state on the
    /// MODEL, all written at every frequency. Every one of those writes the same value on every
    /// thread (the topology does not depend on ω), so the race is benign and it is still a race.
    /// Rather than make <c>Stamp</c> re-entrant across every model in the repository, each worker
    /// gets a netlist nothing else touches; elaboration costs 57 µs for Hero 1 and 1.7 ms for a
    /// 200-node ladder, against a sweep long enough to be worth splitting at all.</para>
    ///
    /// <para><b>The result is bit-identical to the serial path at every degree.</b> Each point's
    /// arithmetic is unchanged and each chunk writes only its own slice of the output array, so
    /// nothing is merged and nothing is reordered. Two things need care and get it: the run-time
    /// warnings each copy accumulates are folded back into <paramref name="netlist"/> in chunk
    /// order, and a failing point throws the exception the serial path would have thrown — the one
    /// from the LOWEST frequency index that failed, not whichever thread lost the race.</para>
    ///
    /// <para><paramref name="netlist"/> is the caller's own, already elaborated: it runs the first
    /// chunk, keeps the merged diagnostics, and is never disposed here. Passing it rather than
    /// re-elaborating it is what lets a caller go on reading <c>Warnings</c> as it always has.</para>
    /// </summary>
    /// <param name="maxDegreeOfParallelism">0 = consult <see cref="AnalysisSettings.MaxParallelism"/>
    /// (itself 0 = automatic); 1 pins the serial path; &gt;1 caps the worker count.</param>
    public static DataSet Run(
        ElaboratedNetlist netlist,
        Library           lib,
        TestBench         tb,
        string?           baseDirectory,
        double[]          freqsHz,
        AnalysisSettings? settings = null,
        RunControl?       control  = null,
        int               maxDegreeOfParallelism = 0)
    {
        settings ??= AnalysisSettings.Default;

        int requested = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : settings.MaxParallelism;
        int degree    = PlanDegree(netlist, freqsHz.Length, requested);
        if (degree <= 1) return Run(netlist, freqsHz, settings, control);

        int freqCount = freqsHz.Length;
        var sMatrices = new Mat<Complex>[freqCount];
        var preps     = new Prepared[degree];
        var extras    = new ElaboratedNetlist?[degree - 1];
        // The wsp matrices, one per frequency, written by index exactly as the S matrices are — so
        // a parallel run's wsp is bit-identical to the serial one for the same reason its S is.
        var wsp       = netlist.WspProbes.Count > 0 ? new Complex[freqCount][,] : null;

        try
        {
            // Elaborated SERIALLY, before any worker starts: the Elaborator reads the TestBench's
            // global-variable list, which a parametric sweep is mutating around this very call.
            preps[0] = Prepare(netlist, freqsHz, settings);
            for (int i = 1; i < degree; i++)
            {
                var copy = new Elaborator(lib) { BaseDirectory = baseDirectory }.Elaborate(tb);
                extras[i - 1] = copy;
                preps[i]      = Prepare(copy, freqsHz, settings);
            }

            // A chunk that fails records WHERE and stops; the others notice and stop too, so a
            // cancelled or singular run does not go on solving points nobody will read.
            var faults  = new Exception?[degree];
            int faulted = 0;
            Func<bool> abort = () => Volatile.Read(ref faulted) != 0;

            Parallel.For(0, degree, new ParallelOptions { MaxDegreeOfParallelism = degree }, c =>
            {
                var (lo, hi) = ChunkRange(freqCount, degree, c);
                try
                {
                    RunRange(preps[c], freqsHz, settings, lo, hi, sMatrices, wsp, control, abort);
                }
                catch (Exception ex)
                {
                    faults[c] = ex;
                    Volatile.Write(ref faulted, 1);
                }
            });

            // Chunks are contiguous and in order, so the lowest faulting CHUNK holds the lowest
            // faulting frequency — which is the point the serial loop would have died on.
            foreach (var fault in faults)
                if (fault is not null)
                    ExceptionDispatchInfo.Capture(fault).Throw();

            // Chunk order, first occurrence winning: the reported list is what a serial run reports.
            for (int i = 0; i < extras.Length; i++)
                if (extras[i] is { } copy) netlist.MergeDiagnosticsFrom(copy);

            return BuildDataSet(netlist, freqsHz, sMatrices, preps[0]!.Z0PerPort, preps[0]!.Probes, wsp, settings);
        }
        finally
        {
            foreach (var copy in extras) copy?.Dispose();
        }
    }

    /// <summary>Minimum frequency points a worker must be given before splitting is worth the
    /// elaboration and the thread start. Hero 1 costs ~5 µs a point, so 64 points is ~0.3 ms —
    /// comparable to one elaboration of a circuit big enough to care.</summary>
    public const int MinPointsPerWorker = 64;

    /// <summary>
    /// How many workers this netlist and grid will actually be run with. Pure and public to the
    /// tests, because "did it take the serial path?" is otherwise only answerable by timing.
    /// </summary>
    /// <param name="maxDegree">0 = automatic, 1 = pinned serial, &gt;1 = a cap.</param>
    public static int PlanDegree(ElaboratedNetlist netlist, int freqCount, int maxDegree)
    {
        if (maxDegree == 1)              return 1;
        if (!CanRunInParallel(netlist))  return 1;

        int cap = maxDegree > 1 ? maxDegree : Environment.ProcessorCount;
        return Math.Max(1, Math.Min(cap, freqCount / MinPointsPerWorker));
    }

    /// <summary>
    /// Whether this netlist may be elaborated more than once for one run.
    ///
    /// <para><b>An external device may not.</b> Its instance is a slot in a WORKER PROCESS, one per
    /// kit rather than one per thread, so T copies would ask that process for T times the instances
    /// and then serialize on its channel anyway — paying the cost of parallelism for none of it.</para>
    ///
    /// <para><b>A control-referencing SDD may not, in this revision.</b> Nothing about it is unsafe:
    /// <c>ResolveSParamControlBranches</c> simply runs per netlist and its test surface is small, so
    /// it stays on the path it has always run on until this one has some use behind it.</para>
    /// </summary>
    private static bool CanRunInParallel(ElaboratedNetlist netlist)
    {
        foreach (var ec in netlist.Components)
        {
            if (ec.Model is ExternalDeviceModel) return false;
            if (ec.Model is SddModel sdd && sdd.ControlRefs.Length > 0) return false;
        }
        return true;
    }

    /// <summary>Half-open range of frequency indices chunk <paramref name="chunk"/> owns. The first
    /// <c>count % degree</c> chunks carry one extra point, so the split is contiguous and covers the
    /// grid exactly whatever the remainder.</summary>
    private static (int Lo, int Hi) ChunkRange(int count, int degree, int chunk)
    {
        int size = count / degree, rem = count % degree;
        int lo   = chunk * size + Math.Min(chunk, rem);
        int hi   = lo + size + (chunk < rem ? 1 : 0);
        return (lo, hi);
    }

    private static DataSet BuildDataSet(
        ElaboratedNetlist netlist,
        double[]          freqsHz,
        Mat<Complex>[]   sMatrices,
        Complex[]         z0PerPort,
        WspProbeSite[]    probes,
        Complex[][,]?     wsp,
        AnalysisSettings  settings)
    {
        DataSet ds;
        if (z0PerPort.Length > 0)
        {
            var refZ0 = z0PerPort[0];
            var snp   = new SNP(freqsHz, sMatrices, MatrixType.S, MatrixFormat.RI, refZ0);
            ds = DataSetBuilder.FromSnp(snp);                    // S cube + uniform Z0 placeholder
            ds.Add("Z0", DataSetBuilder.BuildZ0Cube(z0PerPort)); // overwrite with per-port truth
        }
        else
        {
            // R-wsp1-6: a port-less run is legal when a probe is present — the document's own
            // fixtures (Fig. 31, 34) have none. No S, no Z0; the wsp cubes go into an otherwise
            // empty DataSet, and nothing downstream may assume S exists.
            ds = new DataSet();
        }

        if (wsp is not null) AddWspCubes(ds, netlist, freqsHz, probes, wsp, settings);
        return ds;
    }

    /// <summary>
    /// The <c>wsp</c> matrix, the eight per-probe defaults and the metadata cubes — written by
    /// <see cref="WspCubePacker"/>, which the harmonic-balance engine's <c>ssfreq</c> sweep calls
    /// too (brief-wsprobe-5 R-wsp5-6), so the two analyses cannot disagree about a cube name, a
    /// unit, the NaN policy or a diagnostic's wording.
    /// </summary>
    private static void AddWspCubes(
        DataSet ds, ElaboratedNetlist netlist, double[] freqsHz, WspProbeSite[] probes, Complex[][,] wsp,
        AnalysisSettings settings)
        => WspCubePacker.Add(
            ds, netlist,
            new Axis("freq", (double[])freqsHz.Clone(), "Hz"), freqsHz,
            probes.Select(p => new WspCubePacker.Probe(p.Label, p.Idx, p.TermZG, p.TermZL)).ToArray(),
            wsp, settings.WspMarginThresholdDb, axisWhat: "");

    // ── Per-netlist setup ─────────────────────────────────────────────────────

    /// <summary>
    /// Everything one netlist needs before its first frequency: the ports, the singularity namers,
    /// its own <see cref="MnaSystem"/>, the DC operating point the nonlinear devices linearize at,
    /// and the SDD control-branch resolution. Invariant across ω, so it is done once per netlist —
    /// which for a parallel run means once per worker, on state nothing else can see.
    /// </summary>
    private sealed class Prepared
    {
        internal ElaboratedNetlist  Netlist           = null!;
        internal List<PortEntry>    Ports             = null!;
        internal Complex[]          Z0PerPort         = null!;
        internal bool               AllPortsResistive;
        internal MnaSystem          Mna               = null!;
        internal Func<int, string>  NodeNamer         = null!;
        internal Func<int, string>  BranchNamer       = null!;
        internal bool               CanRetry;
        internal double[]?          DcNodeVoltages;

        /// <summary>The WSProbes, in idx order. Empty for every unprobed run, and the emptiness is
        /// what keeps such a run byte-identical (R-wsp1-12).</summary>
        internal WspProbeSite[]     Probes            = [];

        /// <summary>
        /// The legacy path's TERMINATED assembly, built only when a probe is present there. The
        /// document is explicit that the network the probes see is the terminated one (§4.2) — on
        /// the wave path that is the assembly already on the stack, but a legacy port is a 0 V
        /// driven branch, which is not a termination. Its own <see cref="MnaSystem"/> so the main
        /// assembly's pattern cache is untouched.
        /// </summary>
        internal MnaSystem?         MnaTerminated;

        /// <summary>Back-substitutions performed so far, for <see cref="RunStats"/>.</summary>
        internal int                BackSubstitutions;
    }

    /// <summary>One WSProbe as the engine addresses it: the component, the document's label and
    /// idx, its two nodes (G first), and the declared <c>Z</c> of a top-level <c>Term</c>/<c>Port</c>
    /// sitting directly between each of those nodes and ground, if there is one — what
    /// <c>wsp_terminate</c>'s precondition compares the probe's <c>ZG</c>/<c>ZL</c> against
    /// (brief-wsprobe-3 §6.1). The branch index is read off the model at solve time, because the
    /// wave and legacy assemblies number branches differently.</summary>
    internal readonly record struct WspProbeSite(
        int ComponentIndex, string Label, int Idx, int GNode, int LNode, Complex? TermZG, Complex? TermZL);

    private static Prepared Prepare(
        ElaboratedNetlist netlist, double[] freqsHz, AnalysisSettings settings)
    {
        // ── Identify ports + build branch-label map ───────────────────────────
        int nonGroundNodes = netlist.Nodes.Count - 1;
        var (ports, branchLabels) = CollectPortsAndBranchLabels(netlist, nonGroundNodes, freqsHz);

        // R-wsp1-2/R-wsp1-6: the probes, in the idx order the elaborator assigned. A port-less run is
        // legal when there is at least one — the document's own fixtures have no ports.
        // The Term (if any) shunting each probe terminal to ground is recorded with the probe, so
        // the envelope's precondition — "the probe sits directly at its termination" — can be
        // checked against the declared Z rather than guessed from ZG (R-wsp3 §6.1).
        Complex? TermAt(int node) => WspCubePacker.TermZAt(netlist, node);
        var probes = netlist.WspProbes
            .Select(w => new WspProbeSite(
                w.ComponentIndex, w.Label, w.Idx,
                netlist.Components[w.ComponentIndex].Nodes[0],
                netlist.Components[w.ComponentIndex].Nodes[1],
                TermAt(netlist.Components[w.ComponentIndex].Nodes[0]),
                TermAt(netlist.Components[w.ComponentIndex].Nodes[1])))
            .ToArray();

        if (ports.Count == 0 && probes.Length == 0)
            throw new InvalidOperationException(
                "S-parameter analysis requires at least one Port, Term, or P1Tone component at the testbench top level. " +
                "Place Term or P1Tone components (Num=1, Z=50 Ohm) directly in the testbench, not inside sub-cells.");
        int N = ports.Count;

        var z0PerPort = ports.Select(p => p.Z0).ToArray();

        // Choose path once: wave path when every port Z0 has a positive real part.
        bool allPortsResistive = z0PerPort.All(z => z.Real > 1e-12);

        // R-wsp1-4: on the legacy path the probe solves need every port terminated in its Z0, and a
        // port with Z0 = 0 exactly cannot be — never a large-conductance stand-in.
        if (probes.Length > 0 && !allPortsResistive)
            foreach (var port in ports)
                if (port.Z0 == Complex.Zero)
                    throw new InvalidOperationException(
                        $"wsprobe.port-short: port {port.PortNum} has Z0 = 0 and cannot be terminated " +
                        $"for the WSProbe solves (the probes see the network with every port terminated " +
                        $"in its Z0). Give the port a non-zero reference impedance.");

        // ── Build diagnostic namers (used when factorization fails) ───────────
        var nodeTouchers = new Dictionary<int, List<string>>(netlist.Nodes.Count);
        foreach (var ec in netlist.Components)
            foreach (var n in ec.Nodes)
            {
                if (n == 0) continue;
                if (!nodeTouchers.TryGetValue(n, out var lst))
                    nodeTouchers[n] = lst = [];
                lst.Add($"{ec.ComponentType}:{ec.InstancePath}");
            }

        Func<int, string> nodeNamer = matIdx =>
        {
            int nodeIdx = matIdx + 1;
            string name = nodeIdx < netlist.Nodes.Count
                ? netlist.Nodes.NameOf(nodeIdx)
                : $"node#{nodeIdx}";
            if (nodeTouchers.TryGetValue(nodeIdx, out var t) && t.Count > 0)
            {
                const int maxShow = 8;
                var shown = t.Count <= maxShow
                    ? t
                    : [.. t.Take(maxShow), $"...+{t.Count - maxShow} more"];
                name += $" (touched by: {string.Join(", ", shown)})";
            }
            return name;
        };

        Func<int, string> branchNamer = idx =>
            branchLabels.TryGetValue(idx, out var lbl) ? lbl : $"branch#{idx - nonGroundNodes}";

        bool canRetry =
            settings.ConductanceRegularization == RegularizationMode.IfNecessary ||
            settings.InductanceRegularization  == RegularizationMode.IfNecessary;

        // ── Nonlinear devices → solve the DC operating point once and linearize there (design §3.2) ──
        // RULE: purely-linear S-parameter runs never touch the DC engine (zero behavior change).
        // Per NETLIST, which for a parallel run means once per worker: NonlinearDcEngine is
        // deterministic for a given netlist, so every copy reaches the same operating point and the
        // chunks linearize about the same bias (Engine.Tests pins this rather than assuming it).
        double[]? dcNodeVoltages = null;
        bool hasNonlinear = netlist.Components.Any(c => c.Model.Kind == ModelKind.Nonlinear);
        if (hasNonlinear)
        {
            NonlinearDcEngine.DcResult? dc = null;
            try { dc = NonlinearDcEngine.Run(netlist, settings); }
            catch (NonlinearDcNotConvergedException) { dc = null; }  // DcBiasStepping=Never path throws

            if (dc is { Converged: true })
            {
                dcNodeVoltages = dc.NodeVoltages;
                const double ZeroBiasTol = 1e-9;
                if (dc.NodeVoltages.All(v => Math.Abs(v) < ZeroBiasTol))
                    netlist.AddWarningOnce("sparam-zero-bias",
                        "No DC bias present; nonlinear components linearized at the 0 V operating point.");
            }
            else
            {
                // Non-convergence (degenerate) → warn + fall back to zero-bias linearization (design §3.5).
                string detail = dc is null ? "(no result)" : $"residual {dc.FinalResidual:G3} after {dc.Iterations} iters";
                netlist.AddWarningOnce("sparam-dc-nonconverged",
                    $"DC operating-point solve did not converge ({detail}); nonlinear components linearized at " +
                    "0 V. S-parameters may be inaccurate.");
                dcNodeVoltages = null;  // null ⇒ BuildBias yields 0 V
                ResetSddControlBias(netlist);  // consistent 0 V seed for the control sensitivities
            }
        }

        // Resolve each control-using SDD's referenced branch index against THIS (S-param) assembly.
        // Must happen before the frequency loop — the SDD reads ControlBranchIndices when it stamps.
        ResolveSParamControlBranches(netlist, freqsHz, allPortsResistive, ports, N, dcNodeVoltages, settings);

        return new Prepared
        {
            Netlist           = netlist,
            Ports             = ports,
            Z0PerPort         = z0PerPort,
            AllPortsResistive = allPortsResistive,
            Mna               = new MnaSystem(nonGroundNodes),
            NodeNamer         = nodeNamer,
            BranchNamer       = branchNamer,
            CanRetry          = canRetry,
            DcNodeVoltages    = dcNodeVoltages,
            Probes            = probes,
            MnaTerminated     = probes.Length > 0 && !allPortsResistive ? new MnaSystem(nonGroundNodes) : null,
        };
    }

    /// <summary>Solves the half-open frequency range [lo, hi) into <paramref name="sMatrices"/>,
    /// which it writes BY INDEX and therefore shares with no other range.</summary>
    private static void RunRange(
        Prepared         p,
        double[]         freqsHz,
        AnalysisSettings settings,
        int              lo,
        int              hi,
        Mat<Complex>[]  sMatrices,
        Complex[][,]?    wspOut,
        RunControl?      control,
        Func<bool>?      abort)
    {
        if (p.AllPortsResistive)
            RunWavePath(p, freqsHz, settings, lo, hi, sMatrices, wspOut, control, abort);
        else
            RunLegacyPath(p, freqsHz, settings, lo, hi, sMatrices, wspOut, control, abort);
    }

    // ── Wave path (Re(Z0) > 0 for every port) ─────────────────────────────────

    private static void RunWavePath(
        Prepared         p,
        double[]         freqsHz,
        AnalysisSettings settings,
        int              lo,
        int              hi,
        Mat<Complex>[]  sMatrices,
        Complex[][,]?    wspOut,
        RunControl?      control,
        Func<bool>?      abort)
    {
        var netlist        = p.Netlist;
        var ports          = p.Ports;
        int N              = ports.Count;
        var mna            = p.Mna;
        var nodeNamer      = p.NodeNamer;
        var branchNamer    = p.BranchNamer;
        bool canRetry      = p.CanRetry;
        var dcNodeVoltages = p.DcNodeVoltages;

        int nonGroundNodes = mna.NodeCount;
        var xBuf = Array.Empty<Complex>();
        var bBuf = Array.Empty<Complex>();

        for (int fi = lo; fi < hi; fi++)
        {
            // Another chunk has already failed; nothing will read the rest of this one.
            if (abort is not null && abort()) return;

            // One frequency is this loop's work unit and its cancellation boundary alike.
            control?.Tick();

            double omega = 2.0 * Math.PI * freqsHz[fi];

            // Stamp network (ports contribute conductances, not 0 V branches).
            StampAll(mna, netlist, omega, skipPorts: true, dcNodeVoltages: dcNodeVoltages);
            StampPortConductances(mna, ports, N);
            ApplyRegularization(mna, netlist, nonGroundNodes, settings, applyIfNecessary: false);

            SparseLU lu;
            try
            {
                lu = mna.Factorize(nodeNamer: nodeNamer, branchNamer: branchNamer);
            }
            catch (SingularMatrixException ex) when (canRetry)
            {
                // Genuine floating-node case; retry with regularization.
                netlist.AddWarningOnce("sparam-regularization",
                    $"S-parameter matrix singular — regularization (gmin) applied. Likely floating node(s):\n" +
                    $"{ex.Message}\n" +
                    $"(conductance={settings.ConductanceRegularization != RegularizationMode.Never} " +
                    $"inductance={settings.InductanceRegularization != RegularizationMode.Never})");

                StampAll(mna, netlist, omega, skipPorts: true, dcNodeVoltages: dcNodeVoltages);
                StampPortConductances(mna, ports, N);
                ApplyRegularization(mna, netlist, nonGroundNodes, settings, applyIfNecessary: true);
                lu = mna.Factorize(nodeNamer: nodeNamer, branchNamer: branchNamer);
            }

            // N = 0 (a port-less probe run, R-wsp1-6): nothing to extract, and the entry stays default.
            var sMatrix = N > 0 ? new Mat<Complex>(N, N) : default;
            if (xBuf.Length != mna.Size) { xBuf = new Complex[mna.Size]; bBuf = new Complex[mna.Size]; }

            for (int j = 0; j < N; j++)
            {
                // Incident wave (unit a_j = 1): Norton current I_j = 2√(Re Z0_j) / Z0_j.
                double  sqrtReZ0j = Math.Sqrt(ports[j].Z0.Real);
                Complex iInj      = 2.0 * sqrtReZ0j / ports[j].Z0;

                // RHS: current injection at the driven port nodes. One buffer, cleared per port —
                // the wave path's RHS is zero everywhere but the two driven rows.
                var b = bBuf;
                Array.Clear(b);
                if (ports[j].Node0 > 0) b[ports[j].Node0 - 1] = iInj;
                if (ports[j].Node1 > 0) b[ports[j].Node1 - 1] = -iInj;

                lu.Solve(b, xBuf);
                p.BackSubstitutions++;

                // Extract S column j via Kurokawa power-wave formula.
                // I_k = I_inj(k==j) − V_k/Z0_k  (port current: injection minus conductance draw)
                // b_k = (V_k − conj(Z0_k)·I_k) / (2√(Re Z0_k))
                // S[k,j] = b_k  (since a_j = 1)
                for (int k = 0; k < N; k++)
                {
                    double  sqrtReZ0k = Math.Sqrt(ports[k].Z0.Real);
                    Complex vk        = GetPortVoltage(xBuf, ports[k]);
                    Complex ik        = (k == j ? iInj : Complex.Zero) - vk / ports[k].Z0;
                    sMatrix[k, j]     = (vk - Complex.Conjugate(ports[k].Z0) * ik) / (2.0 * sqrtReZ0k);
                }
            }

            sMatrices[fi] = sMatrix;

            // R-wsp1-3/R-wsp1-4: the probe injections, against the SAME factorisation — on this path
            // the assembly on the stack is already the terminated network (every port stamps its
            // 1/Z0, sources are off, nonlinear devices are linearised at the operating point).
            if (wspOut is not null) wspOut[fi] = SolveProbes(p, lu, xBuf, bBuf);
        }
    }

    /// <summary>
    /// The two injections per probe and the four readings per probe pair that fill the document's
    /// <c>wsp</c> matrix (T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023),
    /// §4.1, Eq. 31–36), against an already-factored terminated assembly: <c>2N</c>
    /// back-substitutions and no factorisation, no stamping.
    ///
    /// <para>For probe <c>i</c> (branch <c>br_i</c>, G-side node <c>nG_i</c>), overview §3's
    /// conventions:</para>
    /// <code>
    ///   series:  b = 0;  b[br_i]     = −1     unit vS with − at G, + at L  (the constraint row is
    ///                                         V(nG) − V(nL) = value, so −1 gives v_L − v_G = 1)
    ///   shunt:   b = 0;  b[nG_i − 1] = +1     unit iP INTO the G-side node
    /// </code>
    /// <para>and for every probe <c>j</c>, <c>iS_j = x[br_j]</c> (the branch current, which flows
    /// G → L by the engine's own first-node → second-node convention) and <c>vP_j = x[nG_j − 1]</c>
    /// (a grounded G node reads 0). Filled 1-based per Eq. 33 — rows are the STIMULUS probe,
    /// columns the RESPONSE probe:</para>
    /// <code>
    ///   wsp(2i−1, 2j−1) = iS_j  of the series solve   (Y0_ij, Eq. 133)
    ///   wsp(2i−1, 2j  ) = vP_j  of the series solve   (Eq. 135)
    ///   wsp(2i  , 2j−1) = iS_j  of the shunt  solve   (Eq. 136)
    ///   wsp(2i  , 2j  ) = vP_j  of the shunt  solve   (H0_ij, Eq. 131)
    /// </code>
    /// <para><b>The series-source sign is the one place a wrong guess survives every symmetric
    /// test:</b> with −1 a zero-feedback cascade gives <c>vP/vS = −ZG/(ZG + ZL)</c> (Eq. 67, 69);
    /// with +1 it gives the negative and ZG comes out negated. R-wsp1-14(a) holds it.</para>
    /// </summary>
    private static Complex[,] SolveProbes(Prepared p, SparseLU lu, Complex[] x, Complex[] b)
    {
        var probes = p.Probes;
        var comps  = p.Netlist.Components;
        int m      = probes.Length;

        // The branch index of THIS assembly, read off the model after the stamp — the wave and
        // legacy paths number branches differently, and a precomputed index would be the wrong one
        // on one of them.
        var br = new int[m];
        for (int i = 0; i < m; i++)
        {
            br[i] = ((SeriesProbeModelBase)comps[probes[i].ComponentIndex].Model).LastBranchIndex;
            if (br[i] < 0)
                throw new InvalidOperationException(
                    $"WSProbe '{probes[i].Label}' allocated no branch in the S-parameter assembly.");
        }

        var w = new Complex[2 * m, 2 * m];
        for (int i = 0; i < m; i++)
        {
            int rs = 2 * i;       // 0-based row of the document's 2i−1 (series stimulus)
            int rp = 2 * i + 1;   // 0-based row of the document's 2i   (shunt stimulus)

            Array.Clear(b);
            b[br[i]] = -Complex.One;
            lu.Solve(b, x);
            p.BackSubstitutions++;
            for (int j = 0; j < m; j++)
            {
                w[rs, 2 * j]     = x[br[j]];
                w[rs, 2 * j + 1] = probes[j].GNode > 0 ? x[probes[j].GNode - 1] : Complex.Zero;
            }

            Array.Clear(b);
            if (probes[i].GNode > 0) b[probes[i].GNode - 1] = Complex.One;
            lu.Solve(b, x);
            p.BackSubstitutions++;
            for (int j = 0; j < m; j++)
            {
                w[rp, 2 * j]     = x[br[j]];
                w[rp, 2 * j + 1] = probes[j].GNode > 0 ? x[probes[j].GNode - 1] : Complex.Zero;
            }
        }
        return w;
    }

    private static void StampPortConductances(MnaSystem mna, List<PortEntry> ports, int N)
    {
        for (int j = 0; j < N; j++)
            mna.AddAdmittance(ports[j].Node0, ports[j].Node1, Complex.One / ports[j].Z0);
    }

    private static Complex GetPortVoltage(Complex[] x, in PortEntry p)
    {
        Complex v0 = p.Node0 > 0 ? x[p.Node0 - 1] : Complex.Zero;
        Complex v1 = p.Node1 > 0 ? x[p.Node1 - 1] : Complex.Zero;
        return v0 - v1;
    }

    // ── Legacy path (any port has Re(Z0) ≤ 0) ─────────────────────────────────

    private static void RunLegacyPath(
        Prepared         p,
        double[]         freqsHz,
        AnalysisSettings settings,
        int              lo,
        int              hi,
        Mat<Complex>[]  sMatrices,
        Complex[][,]?    wspOut,
        RunControl?      control,
        Func<bool>?      abort)
    {
        var netlist        = p.Netlist;
        var ports          = p.Ports;
        int N              = ports.Count;
        var z0PerPort      = p.Z0PerPort;
        var mna            = p.Mna;
        var nodeNamer      = p.NodeNamer;
        var branchNamer    = p.BranchNamer;
        bool canRetry      = p.CanRetry;
        var dcNodeVoltages = p.DcNodeVoltages;

        int nonGroundNodes = mna.NodeCount;
        var xBuf = Array.Empty<Complex>();
        var bBuf = Array.Empty<Complex>();

        for (int fi = lo; fi < hi; fi++)
        {
            // Another chunk has already failed; nothing will read the rest of this one.
            if (abort is not null && abort()) return;

            // One frequency is this loop's work unit and its cancellation boundary alike.
            control?.Tick();

            double hz    = freqsHz[fi];
            double omega = 2.0 * Math.PI * hz;

            // ── Stamp components + first regularization pass ──────────────────
            StampAll(mna, netlist, omega, dcNodeVoltages: dcNodeVoltages);
            ApplyRegularization(mna, netlist, nonGroundNodes, settings,
                applyIfNecessary: false); // first attempt omits IfNecessary regs

            // ── Factorize: attempt 1 ──────────────────────────────────────────
            SparseLU lu;
            bool regularized = false;
            try
            {
                lu = mna.Factorize(nodeNamer: nodeNamer, branchNamer: branchNamer);
            }
            catch (SingularMatrixException ex) when (canRetry)
            {
                // IfNecessary path: re-stamp and apply all non-Never regs, then retry.
                netlist.AddWarningOnce("sparam-regularization",
                    $"S-parameter matrix singular — regularization (gmin) applied. Likely floating node(s):\n" +
                    $"{ex.Message}\n" +
                    $"(conductance={settings.ConductanceRegularization != RegularizationMode.Never} " +
                    $"inductance={settings.InductanceRegularization != RegularizationMode.Never})");

                StampAll(mna, netlist, omega, dcNodeVoltages: dcNodeVoltages);
                ApplyRegularization(mna, netlist, nonGroundNodes, settings,
                    applyIfNecessary: true);
                lu = mna.Factorize(nodeNamer: nodeNamer, branchNamer: branchNamer);
                regularized = true;
            }

            // ── Extract port Y-matrix via unit-voltage excitation ─────────────
            var yMat = new Mat<Complex>(N, N);
            if (xBuf.Length != mna.Size) { xBuf = new Complex[mna.Size]; bBuf = new Complex[mna.Size]; }

            for (int j = 0; j < N; j++)
            {
                var b = bBuf;
                mna.FillRhsWithPortDrive(b, branchRow: ports[j].BranchIndex, driveValue: Complex.One);

                lu.Solve(b, xBuf);
                p.BackSubstitutions++;

                // Branch current flows FROM signal TO ref (AddBranchCurrent convention).
                // Port current (INTO the + terminal) = −branch_current.
                for (int k = 0; k < N; k++)
                    yMat[k, j] = -xBuf[ports[k].BranchIndex];
            }

            // ── Y → S via RfCore (power-wave, per-port complex Z0) ────────────
            sMatrices[fi] = RFNetwork.YToS(yMat, z0PerPort);

            // ── The probe solves, against the TERMINATED network (R-wsp1-4) ───
            // A legacy port is a 0 V driven branch, which is not a termination. The second
            // assembly is the same stamp sequence — so every model's branch index and every SDD's
            // resolved control branch are the ones this assembly has too — plus one diagonal entry
            // per port branch, −Z0, which turns the constraint V(n0) − V(n1) = 0 into
            // V(n0) − V(n1) − Z0·I = 0: the port terminated in its own Z0 with no drive. It is
            // factored on its own MnaSystem, so the main assembly's pattern cache is untouched, and
            // it takes the regularisation the main assembly needed at this frequency.
            if (wspOut is not null)
            {
                var mnaT = p.MnaTerminated!;
                StampAll(mnaT, netlist, omega, dcNodeVoltages: dcNodeVoltages);
                for (int j = 0; j < N; j++)
                    mnaT.AddBranchConstraint(ports[j].BranchIndex, ports[j].BranchIndex, -z0PerPort[j]);
                ApplyRegularization(mnaT, netlist, nonGroundNodes, settings, applyIfNecessary: regularized);
                var luT = mnaT.Factorize(nodeNamer: nodeNamer, branchNamer: branchNamer);
                if (xBuf.Length != mnaT.Size) { xBuf = new Complex[mnaT.Size]; bBuf = new Complex[mnaT.Size]; }
                wspOut[fi] = SolveProbes(p, luT, xBuf, bBuf);
            }
        }
    }

    // ── Assembly helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// <summary>Returns true for models that act as s-param ports (Port, Term, or P1Tone).</summary>
    private static bool IsSParamPort(ComponentModel m) => m is PortModel or TermModel or P1ToneModel;

    /// Stamp all components (two-phase: non-mutual first so InductorModel.LastBranchIndex
    /// is set before MutualInductanceModel reads it).
    /// Buried Term/Port/P1Tone components (dotted InstancePath = inside a sub-cell) are silently
    /// skipped — they are inert and never become driven ports (Layer 2 scoping rule).
    /// <paramref name="skipPorts"/>: when true (wave path), top-level Port/Term/P1Tone are also
    /// skipped — they contribute conductances directly in RunWavePath instead of 0 V branches.
    /// </summary>
    private static void StampAll(
        MnaSystem         mna,
        ElaboratedNetlist netlist,
        double            omega,
        bool              skipPorts      = false,
        double[]?         dcNodeVoltages = null)
    {
        mna.Reset();
        foreach (var ec in netlist.Components)
        {
            if (ec.Model is MutualInductanceModel) continue;

            // P1Tone: its internal __drv node (Nodes[2]) hosts the HB V-source junction and is
            // unused in S-param mode — nothing else stamps it, so it would be a floating MNA
            // unknown (zero row/col → singular). Tie it off in BOTH paths. The port itself is
            // realized at the external terminals: the wave path adds the port conductance in
            // RunWavePath (skipPorts); the legacy path stamps a 0 V port branch here (mirrors Term).
            // Buried (sub-cell) P1Tones are inert ports but still need their __drv tied off.
            if (ec.Model is P1ToneModel p1)
            {
                bool buried = ec.InstancePath.Contains('.');
                p1.StampSParamDriveTie(mna, ec);
                if (!buried && !skipPorts) p1.StampAsSParamPort(mna, ec);
                continue;
            }

            // Buried Term/Port: inert even in S-param analysis.
            if (IsSParamPort(ec.Model) && ec.InstancePath.Contains('.')) continue;
            // Wave path: top-level ports stamp their own conductances; skip the branch stamp.
            if (skipPorts && IsSParamPort(ec.Model)) continue;

            // Nonlinear devices: small-signal linearization at the DC operating point (design §3).
            // Never IsSParamPort, so they fall through the port skips above to here.
            if (ec.Model.Kind == ModelKind.Nonlinear)
            {
                ec.StampLinearized(mna, omega, BuildBias(ec, dcNodeVoltages));
                continue;
            }

            ec.Stamp(mna, omega);
            netlist.DrainModelWarnings(ec.Model);
        }
        foreach (var ec in netlist.Components)
            if (ec.Model is MutualInductanceModel)
            {
                ec.Stamp(mna, omega);
                netlist.DrainModelWarnings(ec.Model);
            }
    }

    /// <summary>Builds a device's bias PortVoltages from the DC node-voltage solution, using the same
    /// port→node-pair convention as NonlinearDcEngine (port p = Nodes[2p] − Nodes[2p+1]).
    /// Null dcNodeVoltages ⇒ all-zero bias (purely-linear run never reaches here; DC-fail fallback).</summary>
    private static PortVoltages BuildBias(ElaboratedComponent ec, double[]? dcNodeVoltages)
    {
        int P = ec.Model.PortCount;
        var v = new double[P];
        for (int p = 0; p < P; p++)
        {
            int np = ec.Nodes.Length > 2 * p     ? ec.Nodes[2 * p]     : 0;
            int nm = ec.Nodes.Length > 2 * p + 1 ? ec.Nodes[2 * p + 1] : 0;
            v[p] = NodeV(dcNodeVoltages, np) - NodeV(dcNodeVoltages, nm);
        }
        return new PortVoltages(v);
    }

    /// <summary>DC voltage of 1-based circuit node (0 = ground = 0 V).</summary>
    private static double NodeV(double[]? dc, int node1based)
        => (dc is null || node1based <= 0 || node1based - 1 >= dc.Length) ? 0.0 : dc[node1based - 1];

    // ── SDD control-current column: resolve referenced branches in the S-param matrix ──────────

    /// <summary>Zero every control-using SDD's bias seed (DC-nonconverged fallback → 0 V linearization).</summary>
    private static void ResetSddControlBias(ElaboratedNetlist netlist)
    {
        foreach (var ec in netlist.Components)
            if (ec.Model is SddModel sdd && sdd.ControlRefs.Length > 0)
                Array.Clear(sdd.ControlBias);
    }

    /// <summary>
    /// Resolve each control-using SDD's referenced branch index (C[n]) against the S-parameter
    /// assembly. Branch numbering differs from the DC/HB matrices (the wave path skips ports; legacy
    /// stamps 0 V port branches), so a throwaway pass replicating the chosen path's StampAll is run
    /// into a temp MNA — every referenced device's LastBranchIndex / PortBranchIndices then matches
    /// what the real per-frequency StampAll produces (topology-invariant across ω). The resolved
    /// indices are written into each SDD's ControlBranchIndices for the run. No-op when no SDD has
    /// control references (so purely-linear and control-free SDD runs are byte-identical).
    /// </summary>
    private static void ResolveSParamControlBranches(
        ElaboratedNetlist netlist, double[] freqsHz, bool wavePath,
        List<PortEntry> ports, int N, double[]? dcNodeVoltages, AnalysisSettings settings)
    {
        var sdds = netlist.Components
            .Where(ec => ec.Model is SddModel s && s.ControlRefs.Length > 0)
            .ToList();
        if (sdds.Count == 0) return;

        // Reset so the SDD's own control column is skipped during the throwaway resolution pass.
        foreach (var ec in sdds)
        {
            var s = (SddModel)ec.Model;
            for (int i = 0; i < s.ControlBranchIndices.Length; i++) s.ControlBranchIndices[i] = -1;
        }

        // Replicate the real solve assembly so referenced-device branch indices match the solve.
        double omega = freqsHz.Length > 0 ? 2.0 * Math.PI * freqsHz[0] : 1.0;
        var temp = new MnaSystem(netlist.Nodes.Count - 1);
        StampAll(temp, netlist, omega, skipPorts: wavePath, dcNodeVoltages: dcNodeVoltages);
        if (wavePath) StampPortConductances(temp, ports, N);

        foreach (var ec in sdds)
        {
            var sdd = (SddModel)ec.Model;
            for (int i = 0; i < sdd.ControlRefs.Length; i++)
            {
                var (n, refInst, port) = sdd.ControlRefs[i];
                // Sibling-first, exactly as the DC engine resolves it — the two must agree, or a
                // design's S-parameters are taken about a bias its own sensed current never reached.
                var target = NonlinearDcEngine.FindControlTarget(netlist, ec, refInst)
                    ?? throw new InvalidOperationException(
                        $"SDD '{sdd.Name}': C[{n}]={refInst} — no sibling component named '{refInst}' " +
                        $"found in the netlist.");
                sdd.ControlBranchIndices[i] = ResolveSParamBranchIndex(sdd.Name, n, port, target);
            }
        }
    }

    /// <summary>Map a referenced device (by kind + optional port) to its S-param-MNA branch index.</summary>
    private static int ResolveSParamBranchIndex(string sddName, int n, int port, ElaboratedComponent target)
    {
        int br = target.Model switch
        {
            VdcModel        vdc => vdc.LastBranchIndex,
            ToneSourceModel ton => ton.LastBranchIndex,
            // An ideal current source's current is an INPUT, not a solved unknown — it allocates no
            // branch to point at. Named explicitly because "the other tone source works" is exactly
            // the wrong inference to leave the user to draw from a generic list of allowed kinds.
            CurrentToneSourceModel => throw new InvalidOperationException(
                $"SDD '{sddName}': C[{n}]={target.InstancePath} is an ideal current source (I_1Tone/I_nTone): " +
                $"its current is an input, not a solved unknown, so it has no branch to reference. " +
                $"Put an IProbe in series with it and reference that instead."),
            // IProbe and WSProbe alike: a 0 V ammeter is a legitimate C[n]= reference.
            SeriesProbeModelBase probe => probe.LastBranchIndex,
            // The ideal voltage-gain source — see the DC engine's own list for why.
            VcvsModel      vcvs => vcvs.LastBranchIndex,
            // L, SRLC and PRLC all carry their inductor current on a branch of their own — the
            // IInductiveBranch contract — so all three are referenceable, by that contract rather
            // than by a list of type names that the next inductive model would silently miss.
            IInductiveBranch ind => ind.LastBranchIndex,
            SnpModel        snp => PortBranch(snp.PortBranchIndices, port),
            ZPortModel       zp => PortBranch(zp.PortBranchIndices, port),
            _ => throw new InvalidOperationException(
                $"SDD '{sddName}': C[{n}]={target.InstancePath} references a '{target.ComponentType}' " +
                $"which is not a referenceable device class (Vdc, VCVS, V_1Tone/V_nTone, IProbe, WSProbe, L, SRLC, PRLC, SnP, Z_Port).")
        };
        if (br < 0)
            throw new InvalidOperationException(
                $"SDD '{sddName}': C[{n}]={target.InstancePath} — referenced device allocated no branch " +
                $"in the S-parameter matrix.");
        return br;

        static int PortBranch(int[] indices, int port)
            => (port >= 1 && port <= indices.Length) ? indices[port - 1] : -1;
    }

    /// <summary>
    /// Apply conductance and/or inductance regularization to the assembled MNA.
    /// <paramref name="applyIfNecessary"/>: if true, treat IfNecessary as Active (retry pass);
    /// if false, only apply Always regs (first-attempt pass).
    /// Never-mode regs are never applied.
    /// </summary>
    private static void ApplyRegularization(
        MnaSystem         mna,
        ElaboratedNetlist netlist,
        int               nonGroundNodes,
        AnalysisSettings  settings,
        bool              applyIfNecessary)
    {
        bool applyCond = settings.ConductanceRegularization == RegularizationMode.Always
            || (applyIfNecessary && settings.ConductanceRegularization == RegularizationMode.IfNecessary);

        bool applyInd  = settings.InductanceRegularization == RegularizationMode.Always
            || (applyIfNecessary && settings.InductanceRegularization == RegularizationMode.IfNecessary);

        if (applyCond)
        {
            // Add gmin conductance from every non-ground node to ground (linear-engine §5).
            var g = new Complex(settings.Gmin, 0.0);
            for (int n = 1; n <= nonGroundNodes; n++)
                mna.AddAdmittance(n, 0, g);
        }

        if (applyInd)
        {
            // Add small series resistance to every inductor branch diagonal.
            // Cures rank-deficient coupled-inductance D-block (zero eigenvalue of inductance matrix).
            var rReg = new Complex(-settings.InductanceRegR, 0.0);
            foreach (var ec in netlist.Components)
                if (ec.Model is IInductiveBranch im && im.LastBranchIndex >= 0)
                    mna.AddBranchConstraint(im.LastBranchIndex, im.LastBranchIndex, rReg);
        }
    }

    // ── Port collection + branch label map ────────────────────────────────────

    /// <summary>
    /// Port entry. Node0/Node1 are used by the wave path (conductance + voltage readback).
    /// BranchIndex is used by the legacy path (unit-voltage drive + current extraction).
    /// </summary>
    private record struct PortEntry(int PortNum, Complex Z0, int BranchIndex, int Node0, int Node1);

    /// <summary>
    /// Preliminary stamp pass to capture port branch indices and build a
    /// branch-index→component-name map for singularity diagnostics.
    /// Two-phase: non-mutual first so LastBranchIndex is stable when mutuals stamp.
    ///
    /// <para><b>Stamped at the sweep's FIRST frequency, not at a placeholder ω.</b> What this pass
    /// reads off is topology, which is invariant across ω, so any ω would do for its own purpose —
    /// but the matrix is not the only thing a <c>Stamp</c> produces. A model also warns from in
    /// there, and a warning names the frequency it was given. This pass used to pass ω=1, i.e.
    /// 0.159 Hz, which is not a point in any sweep: a <c>TLIN</c> whose θ ∝ f then sees θ≈0, reports
    /// a resonance at a frequency the user never asked for, and — because that warning is latched
    /// once per instance — consumes the report a GENUINE resonance later in the sweep would have
    /// made. Every netlist containing an ideal line hit this, on every run. Using freqsHz[0] makes
    /// the pass indistinguishable from the first real frequency, so a warning raised here is one the
    /// per-frequency loop would raise anyway and <c>AddWarningOnce</c> dedups. This is the same
    /// (frequency, 1.0-fallback) form the sibling throwaway pass in
    /// <see cref="ResolveSParamControlBranches"/> already used.</para>
    /// </summary>
    private static (List<PortEntry> Ports, Dictionary<int, string> BranchLabels)
        CollectPortsAndBranchLabels(ElaboratedNetlist netlist, int nonGroundNodes, double[] freqsHz)
    {
        var tempMna      = new MnaSystem(nonGroundNodes);
        var branchLabels = new Dictionary<int, string>();
        double omega     = freqsHz.Length > 0 ? 2.0 * Math.PI * freqsHz[0] : 1.0;

        foreach (var ec in netlist.Components)
        {
            if (ec.Model is MutualInductanceModel) continue;
            // Buried s-param ports: skip in the preliminary stamp pass (Layer 2 scoping rule).
            if (IsSParamPort(ec.Model) && ec.InstancePath.Contains('.')) continue;
            int before = tempMna.BranchCount;
            // P1Tone: stamp as a 0 V branch (same as Term) so LastBranchIndex is captured for
            // the legacy path. Its own S-param Z-port stamp must NOT be called here.
            if (ec.Model is P1ToneModel p1)
                p1.StampAsSParamPort(tempMna, ec);
            else
                ec.Stamp(tempMna, omega);
            netlist.DrainModelWarnings(ec.Model);
            for (int b = before; b < tempMna.BranchCount; b++)
                branchLabels[tempMna.NodeCount + b] = $"{ec.ComponentType}:{ec.InstancePath}";
        }

        foreach (var ec in netlist.Components)
            if (ec.Model is MutualInductanceModel)
            {
                ec.Stamp(tempMna, omega);
                netlist.DrainModelWarnings(ec.Model);
            }

        var ports = new List<PortEntry>();
        foreach (var ec in netlist.Components)
        {
            // Only top-level s-param port components (no dot in path) become S-param ports.
            if (IsSParamPort(ec.Model) && ec.InstancePath.Contains('.')) continue;
            if (ec.Model is PortModel pm)
                ports.Add(new PortEntry(GetPortNum(ec), GetZ0(ec), pm.LastBranchIndex, ec.Nodes[0], ec.Nodes[1]));
            else if (ec.Model is TermModel tm)
                ports.Add(new PortEntry(GetPortNum(ec), GetZ0(ec), tm.LastBranchIndex, ec.Nodes[0], ec.Nodes[1]));
            else if (ec.Model is P1ToneModel p1)
                ports.Add(new PortEntry(GetPortNum(ec), GetZ0(ec), p1.LastBranchIndex, ec.Nodes[0], ec.Nodes[1]));
        }

        ports.Sort((a, b) => a.PortNum.CompareTo(b.PortNum));
        return (ports, branchLabels);
    }

    private static int GetPortNum(ElaboratedComponent ec)
    {
        if (!ec.Parameters.TryGetValue("Num", out var v))
            throw new InvalidOperationException(
                $"{ec.InstancePath}: Port/Term is missing the Num parameter.");
        return (int)v.AsReal();
    }

    private static Complex GetZ0(ElaboratedComponent ec)
    {
        if (!ec.Parameters.TryGetValue("Z", out var v))
            return new Complex(50, 0);
        return v.Kind == CircuitRF.Core.Expressions.ValueKind.Complex
            ? v.AsComplex()
            : new Complex(v.AsReal(), 0);
    }
}
