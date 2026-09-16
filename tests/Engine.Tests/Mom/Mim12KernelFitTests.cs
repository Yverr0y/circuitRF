// brief-em-mim-12-full-wave-thin-film-readback.md step 0, and brief-em-mim-12a-transmitted-image-peel.md
// — WHERE THE CAPACITOR'S DIGITS WERE LOST, AND WHAT PUTTING THEM BACK TOOK.
//
// MIM-12's step 0 asked one question: does the SOLVE lose digits it has (conditioning, which
// iterative refinement recovers), or did the matrix ENTRIES never carry the small quantity (which no
// solve can repair)? It is the second, and the missing digits were not double-precision rounding at
// 1e-16 — they were the DCIM fit's own error at 1e-2. Fourteen decades apart, and the two have
// entirely different remedies.
//
// THE ARITHMETIC, WHICH IS WHAT MADE IT A FINDING RATHER THAN AN OBSERVATION. A plate pair's
// capacitance is what is left after the same-level and cross-level potential coefficients nearly
// cancel: on the shipped 0.2 µm film under a 15 µm cell the difference is about d/cell = 1/75 of
// either. The two pairings were two INDEPENDENT Prony fits, so the capacitance inherited cell/d
// times whatever relative error they carried over the cell's own ρ range:
//
//   f        cross-level kernel error, UNPEELED     C/(eps0 epsR A/d), as a run fitted it
//   10 GHz   8.3e-5  ->  8.3e-5 * 75 =  0.6%         1.00
//    1 GHz   2.7e-2  ->  2.7e-2 * 75 =  200%        -0.54
//
// and the true kernel does not move with frequency at all — 4.13190E+005 at both 1 and 10 GHz, five
// figures — so EVERY bit of that frequency dependence was fit error (T1).
//
// MIM-12a IS THE FIX, AND IT IS A DERIVATION RATHER THAN A TUNING. The thin region is a Fabry-Perot
// cavity, its multiple reflections are a closed-form image series, and the sampled path never reached
// them: it decays like e^{-k_rho d} with d = 0.2 um, i.e. structure out to k_rho = 5e6, against a
// path that stops at 1.9e5. LayeredSpectralGreens.ThinRegionImagesAtHeights peels that series out of
// the samples BEFORE the fit runs, and ShallowImageCore integrates it back in closed form. The fitted
// cross-level kernel then reads 1.4e-6 at 1 GHz instead of 2.7e-2, and the ladder below reads 1.006
// at 1, 2, 3 and 10 GHz on two meshes (T1, T2).
//
// WHAT THIS REFUTES, AND IT IS THE BRIEF'S OWN DIAGNOSIS. MIM-12 concluded "it is a dynamic-range
// failure", from cond(Z) = 1.4e9 and a loss term 11 decades below the matrix, and proposed iterative
// refinement, a closed-form cell-pair difference, and a loop-star formulation. Those numbers are real
// and reproduce exactly (T3) — and they were not the cause: the ELECTROSTATIC instrument, whose own
// matrix is conditioned in the hundreds, was ALREADY sign-inverted at 1 GHz and is now RIGHT at the
// very same conditioning (T3). A solve that loses nine digits is not what broke an answer that was
// broken before the solve, and is not what fixed it either.
//
// AND THE LOW-FREQUENCY WIDENING IS NOT THE VILLAIN (T4). Dcim.ForStackAtFrequency does exactly what
// it was built to do on an ordinary one-level GaAs run — 4.6e-1 -> 4.3e-4 at 1 GHz — and removing it
// would break every such run. What it cannot do is carry a 0.2 µm film AND a 106 µm stack in one
// order budget: that is a 530:1 range of scales in one fit. MIM-12a does not widen the path; it takes
// the small scale out of the fit's job entirely.

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
    // T1 — the TRUE kernel does not move with frequency; the UNPEELED fit moved by decades
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void T1_EveryBitOfTheFrequencyDependenceWasFitError_AndThePeelRemovesIt()
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

        var setLo = new PlanarKernelSet(lo, PlanarExtractionOrder.Constant, 0.0, AsARunFits(1e9, MimStack(Film)));
        var setHi = new PlanarKernelSet(hi, PlanarExtractionOrder.Constant, 0.0, AsARunFits(10e9, MimStack(Film)));

        var fitLo = setLo.Get(GreensKernel.ScalarPotential, Zlow, Zlow + Film).Evaluate(rho);
        var fitHi = setHi.Get(GreensKernel.ScalarPotential, Zlow, Zlow + Film).Evaluate(rho);

        double errLo = (fitLo - trueLo.Value).Magnitude / trueLo.Value.Magnitude;
        double errHi = (fitHi - trueHi.Value).Magnitude / trueHi.Value.Magnitude;

        _out.WriteLine($"cross-level G_q at rho = {rho * 1e6:G3} um, exact: " +
                       $"{trueLo.Value.Real:E6} at 1 GHz, {trueHi.Value.Real:E6} at 10 GHz " +
                       $"({truth:E1} apart). UNPEELED fit: {errLo:E2} wrong at 1 GHz, {errHi:E2} at 10 GHz.");

        // The physics is frequency-independent to five figures at these dimensions.
        Assert.True(truth < 1e-4, $"the exact kernel moved {truth:E2} over a decade of frequency.");

        // The UNPEELED fit is not, and the two ends are two decades apart. THIS is the quantity the
        // capacitance used to divide by d/cell — and PlanarKernelSet.Get is still that fit, because
        // it is the view a caller asks for when it has no mesh and therefore no threshold.
        Assert.True(errHi < 1e-3, $"the 10 GHz unpeeled fit is {errHi:E2} out (measured 8.3e-5).");
        Assert.True(errLo > 1e-2, $"the 1 GHz unpeeled fit is {errLo:E2} out (measured 2.65e-2).");

        // MIM-12a — and this is what a RUN now asks for: the same pairing with the thin region's own
        // image series peeled out of the fit's samples and carried in closed form. The comparison is
        // against the same oracle, with the peeled images added back, so it grades the WHOLE kernel
        // and not the remainder the fit was left with.
        double PeeledError(PlanarKernelSet set, Complex exact, double shallowM)
        {
            var split = set.GetMinusShallowImages(GreensKernel.ScalarPotential, Zlow, Zlow + Film, shallowM);
            Complex got = split.Terms.Evaluate(rho);
            foreach (var im in split.Removed)
                got += im.Amplitude / (4.0 * Math.PI * Complex.Sqrt(rho * rho + im.Depth * im.Depth));
            return (got - exact).Magnitude / exact.Magnitude;
        }

        // The shipped mesh's own threshold: ShallowImageCells (0.5) times the 15 µm cell.
        const double shallow = 0.5 * 15e-6;
        double peelLo = PeeledError(setLo, trueLo.Value, shallow);
        double peelHi = PeeledError(setHi, trueHi.Value, shallow);
        _out.WriteLine($"PEELED (the view a run fills from): {peelLo:E2} at 1 GHz, {peelHi:E2} at 10 GHz.");

        // Four decades at 1 GHz, and the frequency dependence with it. The band is deliberately loose
        // — what is being asserted is the DECADES, not a particular last digit of a Prony fit.
        Assert.True(peelLo < 1e-5, $"the peeled 1 GHz kernel is {peelLo:E2} out (measured 3.6e-9).");
        Assert.True(peelHi < 1e-5, $"the peeled 10 GHz kernel is {peelHi:E2} out (measured 1.9e-7).");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T2 — MIM-8's own gate, re-run at the frequency and the fit a RUN uses
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    // MIM-8's instrument exactly — PlanarFill.ScalarPotentialMatrix, one plate at 1 V and the other
    // at 0 V, no port in it — on the shipped 60 µm capacitor at 4 and 6 cells across. The ONLY things
    // that move between these rows are the frequency the kernel is fitted at and the mesh, and the
    // exact kernel does not move with either (T1).
    //
    // MIM-12a RE-POINTED EVERY ROW, and the "was" column is the measurement that made the case for
    // it: −0.5449 / 1.3413 / 1.5953 / 0.9995 at 4 cells across, i.e. an answer whose SIGN followed the
    // frequency the kernel happened to be fitted at. It is now one number to four decimals at every
    // frequency and on both meshes, and the residual 0.6 % is the mesh's own discretisation — MIM-8
    // measured 1.0032 for the same capacitor at the same mesh with no low-frequency widening at all.
    [InlineData( 1.0, 4, 1.0057, -0.5449)]
    [InlineData( 2.0, 4, 1.0058,  1.3413)]
    [InlineData( 3.0, 4, 1.0057,  1.5953)]
    [InlineData(10.0, 4, 1.0058,  0.9995)]
    [InlineData( 1.0, 6, 1.0057,  double.NaN)]
    [InlineData(10.0, 6, 1.0058,  double.NaN)]
    public void T2_TheElectrostaticPlateCapacitanceIsNowFrequencyAndMeshIndependent(
        double fGHz, int across, double expected, double wasBeforeThePeel)
    {
        double ratio = PlateCapacitanceRatio(fGHz * 1e9, across);
        _out.WriteLine($"f = {fGHz:G4} GHz, {across} cells across: C/(eps0 epsR A/d) = {ratio:F4}" +
                       (double.IsNaN(wasBeforeThePeel) ? "" : $" (was {wasBeforeThePeel:F4})"));
        Assert.Equal(expected, ratio, 3);

        // The claim, as one assertion rather than six literals: the band a MMIC is designed in now
        // agrees with 10 GHz, and both are the capacitance. MIM-12a's own gate is 5%.
        Assert.True(Math.Abs(ratio - 1.0) < 0.05, $"{ratio:F4} at {fGHz:G3} GHz, {across} cells across");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T3 — it is NOT the conditioning, and this is the assertion that retires the brief's item 1
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void T3_TheConditioningDidNotBreakTheAnswerAndDidNotFixIt()
    {
        // Iterative refinement recovers digits a FACTORISATION lost. It can only help where the
        // factorisation is where they went. The electrostatic instrument's own matrix is the
        // potential-coefficient matrix over 32 cells and its condition number is in the hundreds —
        // and at that very conditioning its answer was sign-inverted before MIM-12a and is right
        // after it. Nothing about the matrix's conditioning moved; only the KERNEL did.
        //
        // That is the sharpest available form of the claim, and it is sharper than MIM-12's own:
        // MIM-12 could only say there was nothing for refinement to recover, because the answer was
        // already wrong before the solve. This says the same system, at the same conditioning, now
        // produces the right answer — so refinement was never what stood between them.
        var (ratio, cond)     = PlateCapacitanceAndConditioning(1e9, across: 4);
        var (unpeeled, cond0) = PlateCapacitanceAndConditioning(
            1e9, across: 4, PlanarFillSettings.Default with { ShallowImageCells = 0.0 });
        _out.WriteLine($"1 GHz, N = 32 cells: cond(P) = {cond:E3}, C/(eps0 epsR A/d) = {ratio:F4}; " +
                       $"with the treatment off, cond(P) = {cond0:E3} and C = {unpeeled:F4}");

        Assert.InRange(ratio, 0.95, 1.05);
        Assert.True(unpeeled < 0,
            $"the untreated arithmetic is expected to report a negative capacitance here; got {unpeeled:F4}.");

        // NEITHER system is ill-conditioned — both are five decades inside the 1.4e9 the brief
        // diagnosed from — and one of them is right while the other has the wrong sign. That is the
        // whole argument: conditioning does not separate these two answers.
        //
        // They are not conditioned the SAME, and the direction is worth recording rather than
        // asserting away: the treated matrix is about 10x WORSE conditioned, because the cross-level
        // entries now carry the near-cancelling structure that is the capacitor. The answer improved
        // by two orders while the conditioning got worse, which is the opposite of what a
        // dynamic-range diagnosis predicts.
        Assert.True(cond  < 1e4, $"cond(P) = {cond:E2} — the treated system is not ill-conditioned.");
        Assert.True(cond0 < 1e4, $"cond(P) = {cond0:E2} — the untreated system is not ill-conditioned.");
        Assert.True(cond > cond0, $"expected the treated matrix to be the worse conditioned of the two.");

        // And the full-wave numbers MIM-12's brief quotes are of the right ORDER — they were correct
        // measurements of something that is not the cause, and they are pinned here so that stays on
        // the record. MIM-12a moved them, in the direction the paragraph above describes: the brief
        // measured cond(Z) = 1.443e9 and min/max eig(Re Z) = 1.407e-11 on this fixture; with the
        // cross-level entries carrying the film's real structure they read ~1.96e9 and ~9.8e-12. The
        // matrix got HARDER while the answer got right, which is the finding, not a regression.
        var (condZ, lossFloor, n) = FullWaveConditioning(0.5e9, across: 6);
        _out.WriteLine($"0.5 GHz, N = {n}: cond(Z) = {condZ:E3}, min/max eig(Re Z) = {lossFloor:E3} " +
                       "(MIM-12 read 1.443e9 and 1.407e-11 before the peel)");
        Assert.Equal(120, n);
        Assert.InRange(condZ, 1.8e9, 2.1e9);
        Assert.InRange(lossFloor, 8e-12, 1.2e-11);
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
    public void T5b_TheDirectKernelNowAgreesWithTheFittedOne()
    {
        // WHAT MIM-12 MEASURED HERE, AND WHY THE ROW HAD TO MOVE. With the fit replaced by direct
        // Sommerfeld integration on a radial table, MIM-12 read 0.75 at 1, 2 and 3 GHz and 1.015 at
        // 10 GHz — frequency- and mesh-independent, and 24 % low. The residual was diagnosed rather
        // than tolerated: the images the table subtracted were the FIT's, so what was left still ran
        // 2.3e4 -> 87 over rho = 0.02 … 0.93 um and a linear table at the mesh's own spacing could
        // not carry it.
        //
        // MIM-12a removes exactly that: the subtracted images are now the thin region's own EXACT
        // series, so the tabulated remainder is the small smooth thing a table can carry. The two
        // paths — one fitted, one integrated directly — now agree to under 2 %, which is the
        // strongest statement available that the peel is COMPLETE rather than merely better. The
        // brief predicted this arm would start working and said that if it did not, the peel was
        // incomplete.
        var direct = PlanarFillSettings.Default with { DirectScalarKernel = true, ScalarTableSamples = 256 };
        foreach (double fGHz in new[] { 1.0, 3.0, 10.0 })
        {
            double fitted = PlateCapacitanceRatio(fGHz * 1e9, 4, PlanarFillSettings.Default);
            double got    = PlateCapacitanceRatio(fGHz * 1e9, 4, direct);
            _out.WriteLine($"f = {fGHz:G4} GHz: C/(eps0 epsR A/d) = {fitted,8:F4} fitted, {got,8:F4} direct " +
                           $"(MIM-12 read 0.75 direct below 5 GHz)");
            Assert.InRange(got, 0.98, 1.05);
            Assert.True(Math.Abs(got - fitted) < 0.02,
                $"fitted {fitted:F4} and direct {got:F4} disagree by more than 2% — the peel is incomplete.");
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

    private static (double Ratio, double Cond) PlateCapacitanceAndConditioning(
        double fHz, int across, PlanarFillSettings? fill = null)
    {
        var r = Solve(fHz, across, fill, conditioning: true);
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
