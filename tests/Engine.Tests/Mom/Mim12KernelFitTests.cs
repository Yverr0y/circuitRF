// brief-em-mim-12-full-wave-thin-film-readback.md, step 0 — WHERE THE CAPACITOR'S DIGITS ARE LOST.
//
// The brief's step 0 asks one question and says to answer it before committing to any of its three
// remedies: does the SOLVE lose digits it has (conditioning, which iterative refinement recovers),
// or did the matrix ENTRIES never carry the small quantity (which no solve can repair)? It is the
// second, and the missing digits are not double-precision rounding at 1e-16 — they are the DCIM
// fit's own error at 1e-2. Fourteen decades apart, and the two have entirely different remedies.
//
// THE ARITHMETIC, WHICH IS WHAT MAKES THIS A FINDING RATHER THAN AN OBSERVATION. A plate pair's
// capacitance is what is left after the same-level and cross-level potential coefficients nearly
// cancel: on the shipped 0.2 µm film under a 15 µm cell the difference is about d/cell = 1/75 of
// either. The two pairings are two INDEPENDENT Prony fits, so the capacitance inherits cell/d times
// whatever relative error they carry over the cell's own ρ range. Measured on the 60 µm plate pair:
//
//   f        fitted cross-level kernel error      C/(eps0 epsR A/d)
//   10 GHz   8.3e-5  ->  8.3e-5 * 75 =  0.6%       1.00
//    1 GHz   2.7e-2  ->  2.7e-2 * 75 =  200%      -0.54     (T1, T2)
//
// and the true kernel does not move with frequency at all — 4.13190E+005 at both 1 and 10 GHz, five
// figures — so EVERY bit of the answer's frequency dependence is fit error (T1).
//
// WHAT THIS REFUTES, AND IT IS THE BRIEF'S OWN DIAGNOSIS. The brief concludes "it is a dynamic-range
// failure", from cond(Z) = 1.4e9 and a loss term 11 decades below the matrix, and proposes iterative
// refinement, a closed-form cell-pair difference, and a loop-star formulation. Those numbers are
// real and reproduce exactly (T4) — and they are not the cause: the ELECTROSTATIC instrument, whose
// own matrix is conditioned at 20-830, is ALREADY sign-inverted at 1 GHz (T3). A solve that loses
// nine digits cannot be what breaks an answer that is already broken before the solve.
//
// The brief's third exclusion — "3. Not the electrostatic fill. MIM-8 did what it says it did" — is
// true only at 10 GHz. MIM-8's gate is a good gate for what it measures; it simply never ran the
// arithmetic a RUN runs at the frequency a user runs it at (T2's own note).
//
// AND THE LOW-FREQUENCY WIDENING IS NOT THE VILLAIN (T5). Dcim.ForStackAtFrequency does exactly what
// it was built to do on an ordinary one-level GaAs run — 4.6e-1 -> 4.3e-4 at 1 GHz — and removing it
// would break every such run. What it cannot do is carry a 0.2 µm film AND a 106 µm stack in one
// order budget: that is a 530:1 range of scales in one fit, and the small end is the whole capacitor.

using System.Numerics;
using CircuitRF.Engine.Mom;
using NumFlat;
using Xunit;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

public sealed class Mim12KernelFitTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _out = output;

    private const double Zlow    = 103e-6;   // the shipped MIM technology's lower plate
    private const double Ztop    = 106e-6;   // its upper interconnect level
    private const double EpsFilm = 6.80;
    private const double Film    = 0.2e-6;   // the shipped capacitor dielectric

    private static LayerStack MimStack(double d) => new(
        Termination.Pec,
        [
            new MediumLayer(Zlow, new EmMaterial(12.90, 0.0006)),
            new MediumLayer(d,    new EmMaterial(EpsFilm, 0.0010)),
            new MediumLayer(Ztop - Zlow - d, new EmMaterial(1.0, 1e-6)),
        ],
        Termination.Air);

    private static PlanarPolygon Square(double w) =>
        new([new EmPoint(0, 0), new EmPoint(w, 0), new EmPoint(w, w), new EmPoint(0, w)]);

    private static PlanarProblem Plates(double d, double w, double fHz) => new(
        [
            new PlanarConductorLayer("bottom plate", [Square(w)], 4.1e7, 0.25e-6, Zlow),
            new PlanarConductorLayer("top plate",    [Square(w)], 4.1e7, 0.25e-6, Zlow + d),
        ],
        GroundedSlab.GaAsStarter, fHz, null, MimStack(d), null);

    /// <summary>The DCIM settings a RUN uses at this frequency — <c>PlanarFrequencyKernel.Fit</c>'s
    /// own call, which is the one thing MIM-8's capacitance fixture does not make.</summary>
    private static DcimSettings AsARunFits(double fHz, LayerStack stack) =>
        Dcim.ForStackAtFrequency(null, 2.0 * Math.PI * fHz / EmConstants.C0, stack.TopZ);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T1 — the TRUE kernel does not move with frequency; the FITTED one moves by decades
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void T1_EveryBitOfTheFrequencyDependenceIsFitError()
    {
        // Direct Sommerfeld integration is the oracle (SommerfeldIntegral's own header: it shares
        // nothing with Dcim but SpectralGreens). It is asked here at two frequencies a decade apart
        // on a structure that is electrically nothing at either, so its answer MUST be the same — and
        // the tail is asserted converged, because EvaluateInterior's own note warns that a pair
        // separated by a thin spacer can exhaust MaxTailPanels and return a plausible partial sum.
        const double rho = 2e-6;
        var lo = new LayeredSpectralGreens(MimStack(Film), 1e9);
        var hi = new LayeredSpectralGreens(MimStack(Film), 10e9);

        var trueLo = SommerfeldIntegral.EvaluateInterior(lo, GreensKernel.ScalarPotential, rho, Zlow + Film, Zlow);
        var trueHi = SommerfeldIntegral.EvaluateInterior(hi, GreensKernel.ScalarPotential, rho, Zlow + Film, Zlow);
        Assert.True(trueLo.TailConverged && trueHi.TailConverged, "the oracle's own tail did not converge");

        double truth = (trueLo.Value - trueHi.Value).Magnitude / trueHi.Value.Magnitude;

        var fitLo = new PlanarKernelSet(lo, PlanarExtractionOrder.Constant, 0.0, AsARunFits(1e9, MimStack(Film)))
                    .Get(GreensKernel.ScalarPotential, Zlow, Zlow + Film).Evaluate(rho);
        var fitHi = new PlanarKernelSet(hi, PlanarExtractionOrder.Constant, 0.0, AsARunFits(10e9, MimStack(Film)))
                    .Get(GreensKernel.ScalarPotential, Zlow, Zlow + Film).Evaluate(rho);

        double errLo = (fitLo - trueLo.Value).Magnitude / trueLo.Value.Magnitude;
        double errHi = (fitHi - trueHi.Value).Magnitude / trueHi.Value.Magnitude;

        _out.WriteLine($"cross-level G_q at rho = {rho * 1e6:G3} um, exact: " +
                       $"{trueLo.Value.Real:E6} at 1 GHz, {trueHi.Value.Real:E6} at 10 GHz " +
                       $"({truth:E1} apart). Fitted: {errLo:E2} wrong at 1 GHz, {errHi:E2} at 10 GHz.");

        // The physics is frequency-independent to five figures at these dimensions.
        Assert.True(truth < 1e-4, $"the exact kernel moved {truth:E2} over a decade of frequency.");

        // The fit is not, and the two ends are two decades apart. THIS is the quantity the
        // capacitance divides by d/cell — see T2.
        Assert.True(errHi < 1e-3, $"the 10 GHz fit is {errHi:E2} out (measured 8.3e-5).");
        Assert.True(errLo > 1e-2, $"the 1 GHz fit is {errLo:E2} out (measured 2.65e-2).");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T2 — MIM-8's own gate, re-run at the frequency and the fit a RUN uses
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    // MIM-8's instrument exactly — PlanarFill.ScalarPotentialMatrix, one plate at 1 V and the other
    // at 0 V, no port in it — on the shipped 60 µm capacitor at 4 cells across, cell/separation 75.
    // The ONLY thing that moves between these rows is the frequency the kernel is fitted at, and the
    // exact kernel does not move with it at all (T1).
    //
    // MIM-8's own recorded 1.003 is the last row. Nothing about it was wrong; it is a statement
    // about 10 GHz, and the shipped MMIC band is the first three rows.
    [InlineData( 1.0, -0.5449)]
    [InlineData( 2.0,  1.3413)]
    [InlineData( 3.0,  1.5953)]
    [InlineData(10.0,  0.9995)]
    public void T2_TheElectrostaticPlateCapacitanceIsNotFrequencyIndependent(double fGHz, double expected)
    {
        double ratio = PlateCapacitanceRatio(fGHz * 1e9, across: 4);
        _out.WriteLine($"f = {fGHz:G4} GHz: C/(eps0 epsR A/d) = {ratio:F4}");
        Assert.Equal(expected, ratio, 3);

        // The claim, as one assertion rather than four literals: the band a MMIC is designed in does
        // not agree with 10 GHz, and one of the two is not a capacitance.
        if (fGHz < 5.0) Assert.True(Math.Abs(ratio - 1.0) > 0.25, $"{ratio:F4} at {fGHz:G3} GHz");
        else            Assert.True(Math.Abs(ratio - 1.0) < 0.05, $"{ratio:F4} at {fGHz:G3} GHz");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T3 — it is NOT the conditioning, and this is the assertion that retires the brief's item 1
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void T3_TheAnswerIsAlreadyDestroyedWhereNothingIsIllConditioned()
    {
        // Iterative refinement recovers digits a FACTORISATION lost. It can only help where the
        // factorisation is where they went. The electrostatic instrument's own matrix is the
        // potential-coefficient matrix over 32 cells, its condition number is in the hundreds, and
        // its answer is already sign-inverted — so there is nothing for refinement to recover and
        // the brief's item 1 is inapplicable, not merely unlikely.
        var (ratio, cond) = PlateCapacitanceAndConditioning(1e9, across: 4);
        _out.WriteLine($"1 GHz, N = 32 cells: cond(P) = {cond:E3}, C/(eps0 epsR A/d) = {ratio:F4}");

        Assert.True(cond < 1e4, $"cond(P) = {cond:E2} — the electrostatic system is not ill-conditioned.");
        Assert.True(ratio < 0, $"C/(eps0 epsR A/d) = {ratio:F4} — the sign is expected to be inverted here.");

        // And the full-wave numbers the brief quotes DO reproduce — they are correct measurements of
        // something that is not the cause. Pinned so that is on the record rather than only asserted.
        var (condZ, lossFloor, n) = FullWaveConditioning(0.5e9, across: 6);
        _out.WriteLine($"0.5 GHz, N = {n}: cond(Z) = {condZ:E3}, min/max eig(Re Z) = {lossFloor:E3} " +
                       "(the brief's 1.4e9 and 1.4e-11)");
        Assert.Equal(120, n);
        Assert.InRange(condZ, 1.3e9, 1.6e9);
        Assert.InRange(lossFloor, 1.2e-11, 1.6e-11);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T4 — the widening is RIGHT, and removing it is not the fix
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void T4_TheLowFrequencyWideningIsWhatMakesAnOrdinaryRunWork()
    {
        // The obvious reading of T1-T2 is "the low-frequency path widening broke the MIM run, turn it
        // off". It is the wrong reading and this is the measurement that says so: on an ordinary
        // one-level GaAs problem at 1 GHz the widening takes the scalar kernel from 46% wrong to
        // 4e-4, and every GaAs run below ~30 GHz is in that regime because the default PathExtent of
        // 300 reaches a product of only 0.63 on a 100 µm slab.
        //
        // What the widening cannot do is carry a 0.2 µm film and a 106 µm stack in ONE order budget.
        // That is where the defect lives, and it is a property of the STACK rather than of the knob.
        var slab  = GroundedSlab.GaAsStarter;
        var stack = new LayerStack(Termination.Pec,
            [new MediumLayer(slab.HeightM, new EmMaterial(12.90, 0.0006))], Termination.Air);
        var greens = new LayeredSpectralGreens(stack, 1e9);
        double z = slab.HeightM;

        double Worst(DcimSettings dcim)
        {
            var terms = new PlanarKernelSet(greens, PlanarExtractionOrder.Constant, 0.0, dcim)
                        .Get(GreensKernel.ScalarPotential, z, z);
            double worst = 0;
            foreach (double rho in new[] { 2e-6, 10e-6, 50e-6, 200e-6, 1000e-6 })
            {
                var exact = SommerfeldIntegral.EvaluateInterior(greens, GreensKernel.ScalarPotential, rho, z, z).Value;
                worst = Math.Max(worst, (terms.Evaluate(rho) - exact).Magnitude / exact.Magnitude);
            }
            return worst;
        }

        double plain   = Worst(DcimSettings.Default);
        double widened = Worst(AsARunFits(1e9, stack));
        _out.WriteLine($"one-level GaAs at 1 GHz: G_q worst relative error {plain:E2} unwidened, " +
                       $"{widened:E2} widened");

        Assert.True(plain > 0.1, $"{plain:E2} — the unwidened fit cannot see a 100 um slab at 1 GHz.");
        Assert.True(widened < 1e-3, $"{widened:E2} — the widening is what makes an ordinary run work.");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T5 — DirectScalarKernel: off is bit-identical, and on it is a MEASUREMENT and not yet a fix
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void T5_TheDirectScalarKernelIsOffByDefaultAndChangesNotOneEntry()
    {
        // The setting exists for the reason DirectVerticalKernel exists (R-zz-3): the fit is the
        // measured failure, so the fill keeps a path that replaces only the fitted part. This is the
        // half of the claim that has to hold whatever the other half measures — with it off, a mesh
        // reaches the fill with the arithmetic it always did.
        var p    = Plates(Film, 10e-6, 10e9);
        var mesh = SurfaceMesher.Mesh(p, Uniform).Mesh;
        var a = ScalarPotential(p, mesh, PlanarFillSettings.Default, 10e9);
        var b = ScalarPotential(p, mesh, PlanarFillSettings.Default with { DirectScalarKernel = false }, 10e9);

        int differ = 0;
        for (int i = 0; i < a.RowCount; i++)
            for (int j = 0; j < a.ColCount; j++)
                if (a[i, j].Real != b[i, j].Real || a[i, j].Imaginary != b[i, j].Imaginary) differ++;
        _out.WriteLine($"{a.RowCount}x{a.ColCount} scalar potential entries, {differ} differ");
        Assert.Equal(0, differ);
        Assert.False(PlanarFillSettings.Default.DirectScalarKernel);
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void T5b_TheDirectKernelRemovesTheFrequencyDependenceAndIsStill24PercentLow()
    {
        // WHAT IS AND IS NOT FIXED, measured rather than argued, because the residual is what the
        // next phase has to attack and reporting only the improvement would hide it.
        //
        // Replacing the fitted kernel with direct Sommerfeld integration on a radial table removes
        // the frequency dependence entirely — the answer becomes the same number at 1, 2 and 3 GHz,
        // and the same number on two meshes — and it lands 24% LOW instead of at 1.00. The residual
        // is diagnosed and is not a tolerance: after MIM-8's shallow images are subtracted, the
        // cross-level remainder still runs 2.3e4 -> 1.1e4 -> 2.6e3 -> 87 over rho = 0.02 ... 0.93 um,
        // because the images subtracted are the FIT's and at 1 GHz the fit does not have the film's
        // own structure in it. A linear table at the mesh's own 0.3 µm spacing cannot carry that.
        //
        // The structural remedy is the k_rho -> infinity asymptote of a CROSS-REGION pairing, which
        // LayeredSpectralGreens.AsymptoticAtHeights returns zero coefficients for by design (MIM-8's
        // own finding) — so a cross-level pairing's entire near field rests on fitted images with no
        // closed form underneath it. That is the brief's item 2 in its true location.
        var direct = PlanarFillSettings.Default with { DirectScalarKernel = true, ScalarTableSamples = 256 };
        foreach (double fGHz in new[] { 1.0, 3.0, 10.0 })
        {
            double fitted = PlateCapacitanceRatio(fGHz * 1e9, 4, PlanarFillSettings.Default);
            double got    = PlateCapacitanceRatio(fGHz * 1e9, 4, direct);
            _out.WriteLine($"f = {fGHz:G4} GHz: C/(eps0 epsR A/d) = {fitted,8:F4} fitted, {got,8:F4} direct");
            Assert.InRange(got, 0.70, 1.05);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════

    private static readonly PlanarMeshSettings Uniform =
        new(Auto: false, CellsPerWavelength: 20, EdgeMesh: false, EdgeCells: 3);

    private static Mat<Complex> ScalarPotential(PlanarProblem p, PlanarMesh mesh,
                                                PlanarFillSettings fill, double fHz)
    {
        var cores = PlanarFill.BuildCores(mesh, fill);
        var set   = new PlanarKernelSet(new LayeredSpectralGreens(p.EffectiveStack, fHz),
                                        fill.Order, 0.0, AsARunFits(fHz, p.EffectiveStack));
        return PlanarFill.ScalarPotentialMatrix(cores, set.For(cores), PlanarLevels.From(p));
    }

    private static double PlateCapacitanceRatio(double fHz, int across, PlanarFillSettings? fill = null)
        => Solve(fHz, across, fill).Ratio;

    private static (double Ratio, double Cond) PlateCapacitanceAndConditioning(double fHz, int across)
    {
        var r = Solve(fHz, across, null, conditioning: true);
        return (r.Ratio, r.Cond);
    }

    /// <summary>MIM-8's own 1 V / 0 V instrument, on the shipped 60 µm capacitor.</summary>
    private static (double Ratio, double Cond) Solve(double fHz, int across, PlanarFillSettings? fill,
                                                     bool conditioning = false)
    {
        const double w = 60e-6;
        fill ??= PlanarFillSettings.Default;
        var p = Plates(Film, w, fHz);
        var mesh = SurfaceMesher.Mesh(p, Uniform with { MinCellsAcrossConductor = across }).Mesh;
        var pm = ScalarPotential(p, mesh, fill, fHz);

        int m = mesh.Cells.Count;
        var a = new Mat<Complex>(m, m);
        var rhs = new Vec<Complex>(m);
        for (int i = 0; i < m; i++)
        {
            rhs[i] = mesh.Cells[i].LayerIndex == 0 ? Complex.One : Complex.Zero;
            for (int j = 0; j < m; j++) a[i, j] = pm[i, j] / EmConstants.Eps0;
        }

        double cond = double.NaN;
        if (conditioning)
        {
            var sv = a.Svd().S;
            double hi = 0, lo = double.MaxValue;
            for (int i = 0; i < sv.Count; i++) { hi = Math.Max(hi, sv[i]); lo = Math.Min(lo, sv[i]); }
            cond = hi / lo;
        }

        var q = a.Lu().Solve(rhs);
        Complex onTop = Complex.Zero;
        for (int i = 0; i < m; i++) if (mesh.Cells[i].LayerIndex != 0) onTop += q[i];
        return (-onTop.Real / (EmConstants.Eps0 * EpsFilm * w * w / Film), cond);
    }

    /// <summary>The brief's own full-wave measurement, reproduced so its numbers are on the record
    /// beside the finding that they are not the cause.</summary>
    private static (double CondZ, double LossFloor, int N) FullWaveConditioning(double fHz, int across)
    {
        var p = Plates(Film, 60e-6, fHz);
        var mesh = SurfaceMesher.Mesh(p, Uniform with { MinCellsAcrossConductor = across }).Mesh;
        var fill = PlanarFillSettings.Default;
        var cores = PlanarFill.BuildCores(mesh, fill);
        var set = new PlanarKernelSet(new LayeredSpectralGreens(p.EffectiveStack, fHz), fill.Order).For(cores);
        var z = PlanarFill.FillMultiLevel(cores, set, PlanarLevels.From(p), 2 * Math.PI * fHz);
        int n = z.RowCount;

        var sv = z.Svd().S;
        double hi = 0, lo = double.MaxValue;
        for (int i = 0; i < n; i++) { hi = Math.Max(hi, sv[i]); lo = Math.Min(lo, sv[i]); }

        var re = new Mat<double>(n, n);
        for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) re[i, j] = z[i, j].Real;
        var ev = re.Evd().D;
        double emin = ev[0], emax = ev[0];
        for (int i = 0; i < n; i++) { emin = Math.Min(emin, ev[i]); emax = Math.Max(emax, ev[i]); }

        return (hi / lo, emin / emax, n);
    }
}
