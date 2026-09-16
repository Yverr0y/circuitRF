// brief-em-mim-12a-transmitted-image-peel.md — THE THIN REGION'S OWN REFLECTIONS, AS A CLOSED FORM.
//
// MIM-12 step 0 established that a MIM capacitor's digits are lost in the DCIM fit and named the
// structural cause: LayeredSpectralGreens.AsymptoticAtHeights returns zero coefficients for a
// cross-REGION pairing by design, so a thin cross-level pairing's entire near field rested on fitted
// images with no closed form underneath it — while the same-level pairing beside it had an exact
// 1/rho. Two decompositions of different exactness, subtracted, with cell/d = 75 multiplying the
// difference.
//
// THE MECHANISM, IN ONE SENTENCE. A thin region is a Fabry-Perot cavity; its multiple reflections are
// a geometric series of images at depths that are multiples of the film thickness; the DCIM sampling
// path reaches k_rho = 1.9e5 while that structure lives out at 1/d = 5e6, so the fit EXTRAPOLATED all
// of it. ThinRegionImagesAtHeights derives the series from the same cascade AsymptoticAtHeights
// already takes, Dcim.FitAtHeights peels it out of the samples before Prony runs, and
// ShallowImageCore integrates it back over a cell pair in closed form — MIM-8's own machinery, with
// EXACT images in it instead of fitted ones.
//
// WHAT IS GATED HERE. T1-T3 are the derivation against direct Sommerfeld integration, which is the
// only acceptance instrument in this area (DcimModel.FitResidual is blind to the whole defect — it
// grades the exponentials against the samples, and it is the samples that were of the wrong
// function). T4-T6 are the scope: what is declined, by name, and what must not move.
//
// TWO CORRECTIONS TO THE BRIEF ARE ON THE RECORD IN T2 AND T3, both measured rather than argued.

using System.Numerics;
using CircuitRF.Engine.Mom;
using Xunit;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

public sealed class Mim12aThinRegionPeelTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _out = output;

    private const double Zlow    = 103e-6;    // the shipped MIM technology's lower plate
    private const double Ztop    = 106e-6;    // its upper interconnect level
    private const double EpsFilm = 6.80;
    private const double Film    = 0.2e-6;    // the shipped capacitor dielectric

    /// <summary>MIM-12's own stack, so every number here is comparable to §MIM-12's tables.</summary>
    private static LayerStack MimStack(double d) => new(
        Termination.Pec,
        [
            new MediumLayer(Zlow, new EmMaterial(12.90, 0.0006)),
            new MediumLayer(d,    new EmMaterial(EpsFilm, 0.0010)),
            new MediumLayer(Ztop - Zlow - d, new EmMaterial(1.0, 1e-6)),
        ],
        Termination.Air);

    /// <summary>MIM-8's own threshold on the shipped 60 µm capacitor at 4 cells across: half of the
    /// 15 µm cell. Everything below is "the mesh cannot resolve this".</summary>
    private const double Shallow = 0.5 * 15e-6;

    private static Complex Series(LayeredSpectralGreens.ThinRegionImageSeries s, double rho)
    {
        Complex v = Complex.Zero;
        foreach (var im in s.Images)
            v += im.Amplitude / (4.0 * Math.PI * Complex.Sqrt(rho * rho + im.Depth * im.Depth));
        return v;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T1 — the DERIVED coefficients are the cascade's, and the acceptance target is the hand formula
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void T1_TheCoefficientsComeOutOfTheCascadeAndHitTheQuasiStaticHandFormula()
    {
        // The brief's own target, computed by hand from a three-medium electrostatic solve:
        //   A = 2/(e1+e2) * 2*e2/(e2+e3) = 0.177014,  q = R12 R23 = -0.230249,  depths (2n+1) d.
        // What ships is NOT that formula — it is the k_rho -> infinity limit of the generalised
        // reflection cascade, taken per component through the same LocalFresnel the same-region
        // branch uses. The hand formula is the acceptance target precisely because it was derived
        // independently; agreeing with it to 1e-6 is the statement that the cascade route is right.
        var g = new LayeredSpectralGreens(MimStack(Film), 1e9);
        var s = g.ThinRegionImagesAtHeights(GreensKernel.ScalarPotential, Zlow + Film, Zlow, Shallow);

        Assert.Null(s.NotApplicable);
        Assert.True(s.Crosses);
        Assert.Equal(Film, s.ThicknessM, 15);

        _out.WriteLine($"A = {s.Amplitude.Real:F6} (target 0.177014), q = {s.Ratio.Real:F6} " +
                       $"(target -0.230249), {s.Images.Count} images");
        Assert.Equal( 0.177014, s.Amplitude.Real, 6);
        Assert.Equal(-0.230249, s.Ratio.Real,     6);

        // The imaginary parts are the stack's own loss tangents and are four decades down — they are
        // not zero and must not be asserted to be, because a lossy film is an ordinary case.
        Assert.InRange(Math.Abs(s.Amplitude.Imaginary / s.Amplitude.Real), 1e-6, 1e-2);

        // Depths and amplitudes: (2n+1) d and A qⁿ, which is what makes it a SERIES rather than a
        // second image. One term is not enough and the table in T2 is where that is measured.
        for (int n = 0; n < s.Images.Count; n++)
        {
            Assert.Equal((2 * n + 1) * Film, s.Images[n].Depth.Real, 15);
            Assert.Equal(0.0, s.Images[n].Depth.Imaginary, 15);
            Complex want = s.Amplitude * Complex.Pow(s.Ratio, n);
            Assert.Equal(want.Real, s.Images[n].Amplitude.Real, 12);
        }

        // The default tolerance stops at |q|ⁿ < 1e-4, i.e. q⁶ = 1.5e-4 — which is exactly where the
        // measured remainder in T2 stops moving.
        Assert.Equal(7, s.Images.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T2 — the assembled series against direct Sommerfeld integration, and what is LEFT
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(1e9)]
    [InlineData(10e9)]
    public void T2_ThePeeledRemainderIsAConstantOverFourDecadesOfRho(double fHz)
    {
        // Direct Sommerfeld integration throughout — FitResidual cannot see any of this.
        var g = new LayeredSpectralGreens(MimStack(Film), fHz);
        var s = g.ThinRegionImagesAtHeights(GreensKernel.ScalarPotential, Zlow + Film, Zlow, Shallow);

        double lo = double.MaxValue, hi = -double.MaxValue, worstFraction = 0;
        foreach (double rho in new[] { 1e-9, 5e-9, 2e-8, 1e-7, 0.2e-6, 1e-6, 5e-6, 15e-6 })
        {
            var exact = SommerfeldIntegral.EvaluateInterior(
                g, GreensKernel.ScalarPotential, rho, Zlow + Film, Zlow);
            Assert.True(exact.TailConverged, $"the oracle's own tail did not converge at rho = {rho:E2}");

            Complex rem = exact.Value - Series(s, rho);
            lo = Math.Min(lo, rem.Real);
            hi = Math.Max(hi, rem.Real);
            worstFraction = Math.Max(worstFraction, (rem / exact.Value).Magnitude);
            _out.WriteLine($"rho = {rho * 1e6,9:G4} um: exact {exact.Value.Real,12:E4}, " +
                           $"remainder {rem.Real,11:E5} ({(rem / exact.Value).Magnitude:E2} of it)");
        }

        double flatness = (hi - lo) / Math.Abs(0.5 * (hi + lo));
        _out.WriteLine($"{fHz / 1e9:G3} GHz: remainder flat to {flatness:P3} over rho in [1 nm, 15 um]");

        // THE GATE. Flat to 1% over four decades of rho — the brief's own, and it is what says the
        // remainder's structure is on the scale of the regions BEYOND the film (103 µm) rather than
        // of the film. That is the scale the DCIM sampling path does reach, which is the whole
        // argument for peeling rather than for widening the path.
        Assert.True(flatness < 0.01, $"the remainder varies by {flatness:P2} over rho.");

        // …and it is 1.1e-3 of the kernel at the small-rho end, which is the arithmetic that says the
        // fit's 1e-2 becomes 1.1e-5 of the kernel and cell/d = 75 turns that into 0.08% of C.
        Assert.InRange(worstFraction, 1e-3, 0.15);
    }

    [Fact]
    public void T2b_TheBriefsOwnAeffGateCannotBeMet_AndTheRemainderIsExactlyWhy()
    {
        // A BRIEF CORRECTION, MEASURED. The brief asks that "the assembled series reproduces A_eff ->
        // 0.164856 to 1e-4". It cannot, and its own text says why one paragraph earlier: the peeled
        // remainder is 1.1e-3 OF THE KERNEL. A_eff is the kernel times 4*pi*R, so the series alone
        // must miss A_eff by exactly that 1.1e-3 — the two gates are the same quantity and they
        // contradict each other. Measured: 0.165043 against 0.164856, i.e. 1.14e-3.
        //
        // Recorded as a test rather than as a note because the number is the POINT: reaching 1e-4
        // would mean the remainder was not there, and the remainder being there is what the fit is
        // still for.
        double rho = 0.005 * Film;
        foreach (double fHz in new[] { 1e9, 10e9 })
        {
            var g = new LayeredSpectralGreens(MimStack(Film), fHz);
            var s = g.ThinRegionImagesAtHeights(GreensKernel.ScalarPotential, Zlow + Film, Zlow, Shallow);
            var exact = SommerfeldIntegral.EvaluateInterior(
                g, GreensKernel.ScalarPotential, rho, Zlow + Film, Zlow).Value;

            double r = Math.Sqrt(rho * rho + Film * Film);
            double aEffExact  = (4.0 * Math.PI * r * exact).Real;
            double aEffSeries = (4.0 * Math.PI * r * Series(s, rho)).Real;
            double gap = Math.Abs(aEffSeries - aEffExact) / aEffExact;
            _out.WriteLine($"{fHz / 1e9:G3} GHz: A_eff exact {aEffExact:F6}, series {aEffSeries:F6}, " +
                           $"apart by {gap:E2}");

            Assert.Equal(0.164856, aEffExact, 5);
            Assert.InRange(gap, 5e-4, 5e-3);        // 1.14e-3 — the remainder, and nothing else
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T3 — the SAME-LEVEL pairing has the same defect, and the brief says it does not
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void T3_OnePlateAgainstItselfSeesTheSameCavity_WhichTheBriefTreatsAsAlreadyExact()
    {
        // THE SECOND BRIEF CORRECTION, and it is the one that decided how much had to be built. The
        // brief's premise is "one side of the difference has its leading term exact and the other has
        // none of it" — the same-level pairing's 2/(e1+e2) being what AsymptoticAtHeights already
        // returns. The LEADING term is exact. The cavity's round trips are not: a source on the floor
        // of the film sees R_top e^{-2 k_rho d} come back, and that is the same unresolved structure
        // one interface over.
        //
        // Measured with the crossing already peeled, the shipped capacitor's remaining error WAS the
        // same-level block — 2e-3 to 2e-2 over the whole cell range at 1 GHz — and cell/d = 75 turned
        // it into a ladder that read 0.843 instead of 1.006. So the face case is built too, and this
        // is the measurement that demanded it.
        var g = new LayeredSpectralGreens(MimStack(Film), 1e9);
        var face = g.ThinRegionImagesAtHeights(GreensKernel.ScalarPotential, Zlow, Zlow, Shallow);

        Assert.Null(face.NotApplicable);
        Assert.False(face.Crosses);

        // On a face the n = 0 term is AsymptoticAtHeights' own (its direct and its image both sit at
        // depth zero), so the series starts at 2d and its first amplitude is C0 (g + R_far) rather
        // than A. Depths 2, 4, 6 … times the film.
        for (int n = 0; n < face.Images.Count; n++)
            Assert.Equal((2 * n + 2) * Film, face.Images[n].Depth.Real, 15);

        // The two pairings share ONE cavity, which is the physical claim: same thickness, same
        // round-trip factor, different placement.
        var cross = g.ThinRegionImagesAtHeights(GreensKernel.ScalarPotential, Zlow + Film, Zlow, Shallow);
        Assert.Equal(cross.Ratio.Real, face.Ratio.Real, 12);
        Assert.Equal(cross.ThicknessM, face.ThicknessM, 15);

        _out.WriteLine($"face: first amplitude {face.Amplitude.Real:F6} at {face.Images[0].Depth.Real / Film:F0} d, " +
                       $"{face.Images.Count} images; shared q = {face.Ratio.Real:F6}");

        // And it is worth what it costs: the same-level kernel, peeled, against the oracle.
        var set = new PlanarKernelSet(g, PlanarExtractionOrder.Constant, 0.0,
                                      Dcim.ForStackAtFrequency(null, 2.0 * Math.PI * 1e9 / EmConstants.C0,
                                                               MimStack(Film).TopZ));
        double worstPeeled = 0, worstPlain = 0;
        foreach (double rho in new[] { 1e-6, 3.75e-6, 7.5e-6, 15e-6, 30e-6 })
        {
            var exact = SommerfeldIntegral.EvaluateInterior(
                g, GreensKernel.ScalarPotential, rho, Zlow, Zlow).Value;
            var split = set.GetMinusShallowImages(GreensKernel.ScalarPotential, Zlow, Zlow, Shallow);
            Complex got = split.Terms.Evaluate(rho);
            foreach (var im in split.Removed)
                got += im.Amplitude / (4.0 * Math.PI * Complex.Sqrt(rho * rho + im.Depth * im.Depth));
            worstPeeled = Math.Max(worstPeeled, (got - exact).Magnitude / exact.Magnitude);
            worstPlain  = Math.Max(worstPlain,
                (set.Get(GreensKernel.ScalarPotential, Zlow, Zlow).Evaluate(rho) - exact).Magnitude
                / exact.Magnitude);
        }
        _out.WriteLine($"same-level G_q at 1 GHz, worst over the cell's own rho range: " +
                       $"{worstPlain:E2} unpeeled, {worstPeeled:E2} peeled");

        Assert.True(worstPlain  > 1e-3, $"the unpeeled same-level kernel was measured 4.5e-3 to 6e-3 out; got {worstPlain:E2}");
        Assert.True(worstPeeled < 1e-4, $"the peeled same-level kernel is {worstPeeled:E2} out");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T4 — the SCOPE, as refusals that name themselves (R-mom-17)
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void T4_EveryPairingOutsideTheDerivationIsDeclinedByName()
    {
        var g = new LayeredSpectralGreens(MimStack(Film), 1e9);

        // A THICKNESS THE MESH RESOLVES. This is the bit-identity trigger and it is MIM-8's own test
        // — the cell against the separation, not "a film is present". A threshold of zero peels
        // nothing at all, which is what keeps PlanarFillSettings.ShallowImageCells = 0 the pre-MIM-8
        // arithmetic exactly.
        foreach (double threshold in new[] { 0.0, Film, Film * 0.5 })
        {
            var no = g.ThinRegionImagesAtHeights(GreensKernel.ScalarPotential, Zlow + Film, Zlow, threshold);
            Assert.Empty(no.Images);
            Assert.Contains("the mesh resolves", no.NotApplicable);
        }

        // THE TWO VERTICAL COMPONENTS. G_A^zz is built from both equivalent lines and the mixed
        // component's asymptote is a 1/k_rho² logarithm rather than an exponential, so neither is the
        // series derived here. Declined by name rather than approximated.
        foreach (var k in new[] { GreensKernel.VerticalVectorPotential, GreensKernel.MixedVectorPotential })
        {
            var no = g.ThinRegionImagesAtHeights(k, Zlow + Film, Zlow, Shallow);
            Assert.Empty(no.Images);
            Assert.Contains(k.ToString(), no.NotApplicable);
        }

        // A POINT IN THE INTERIOR OF A REGION — a via's own z-quadrature asks for exactly this, and
        // the chain between a cavity's two faces is not the chain a point halfway up one sees.
        var interior = g.ThinRegionImagesAtHeights(
            GreensKernel.ScalarPotential, Zlow + Film, Zlow + 0.5 * Film, Shallow);
        Assert.Empty(interior.Images);
        Assert.Contains("INTERIOR", interior.NotApplicable);

        // MORE THAN ONE REGION APART.
        var far = g.ThinRegionImagesAtHeights(GreensKernel.ScalarPotential, Ztop, Zlow, Shallow);
        Assert.Empty(far.Images);
        Assert.Contains("regions apart", far.NotApplicable);

        // A SEMI-INFINITE REGION — which is every ordinary single-level run, metal on the slab top
        // under an open half-space, and therefore the case that must cost nothing.
        var slab = new LayerStack(Termination.Pec,
            [new MediumLayer(100e-6, new EmMaterial(12.90, 0.0006))], Termination.Air);
        var one = new LayeredSpectralGreens(slab, 1e9);
        var nothing = one.ThinRegionImagesAtHeights(GreensKernel.ScalarPotential, 100e-6, 100e-6, 1e-3);
        Assert.Empty(nothing.Images);
        Assert.Contains("semi-infinite", nothing.NotApplicable);

        // TWO COUPLED CAVITIES. A face with a thin region on both sides is not the product of the two
        // series, and approximating it is precisely what this brief exists to stop.
        var thinBoth = new LayerStack(Termination.Pec,
            [
                new MediumLayer(100e-6, new EmMaterial(12.90, 0.0006)),
                new MediumLayer(0.2e-6, new EmMaterial(6.80, 0.001)),
                new MediumLayer(0.3e-6, new EmMaterial(7.50, 0.001)),
                new MediumLayer(3e-6,   new EmMaterial(1.00, 0.0)),
            ],
            Termination.Air);
        var two = new LayeredSpectralGreens(thinBoth, 1e9);
        // The interface height taken from the stack itself rather than written out: a point one ulp
        // below an interface is in the region BELOW it, which would make this a different question.
        double between = thinBoth.InterfaceZ[2];
        var coupled = two.ThinRegionImagesAtHeights(
            GreensKernel.ScalarPotential, between, between, Shallow);
        Assert.Empty(coupled.Images);
        Assert.Contains("coupled cavities", coupled.NotApplicable);
        _out.WriteLine("declined: " + coupled.NotApplicable);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T5 — the horizontal VECTOR kernel is derivable and is NOT needed, and that was measured
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void T5_TheVectorBlockHasNoGeometricSeries_SoItsFitWasNeverExtrapolating()
    {
        // The brief leaves the vector half "unmeasured here, not excluded" and says to measure it on
        // this fixture before deciding. Measured, and the answer is structural rather than a
        // tolerance: on a NON-MAGNETIC stack R^h = 0 at every interface, so the TE cavity's round-trip
        // factor is zero and the series is ONE exponential across the film and nothing at all on a
        // face. One exponential is exactly what a Prony fit represents — so there was never anything
        // to extrapolate, and MIM-8's decision not to build the vector block's closed-form cell-pair
        // integral stands.
        var g = new LayeredSpectralGreens(MimStack(Film), 1e9);
        var s = g.ThinRegionImagesAtHeights(GreensKernel.VectorPotential, Zlow + Film, Zlow, Shallow);

        Assert.Null(s.NotApplicable);
        Assert.Single(s.Images);                            // q = 0 ⇒ the series terminates at n = 0
        Assert.Equal(0.0, s.Ratio.Magnitude, 15);
        Assert.Equal(1.0, s.Amplitude.Real, 12);            // mu_c (1 + 0)(1 + 0) — free space across d

        // …and the fitted G_A^xx is five decades better than the fitted G_q was, on the same pairing
        // at the same frequency. THIS is the measurement, not the algebra.
        var set = new PlanarKernelSet(g, PlanarExtractionOrder.Constant, 0.0,
                                      Dcim.ForStackAtFrequency(null, 2.0 * Math.PI * 1e9 / EmConstants.C0,
                                                               MimStack(Film).TopZ));
        double worstA = 0, worstQ = 0;
        foreach (double rho in new[] { 1e-9, 0.2e-6, 1e-6, 5e-6, 15e-6 })
        {
            var ea = SommerfeldIntegral.EvaluateInterior(
                g, GreensKernel.VectorPotential, rho, Zlow + Film, Zlow).Value;
            var eq = SommerfeldIntegral.EvaluateInterior(
                g, GreensKernel.ScalarPotential, rho, Zlow + Film, Zlow).Value;
            worstA = Math.Max(worstA,
                (set.Get(GreensKernel.VectorPotential, Zlow, Zlow + Film).Evaluate(rho) - ea).Magnitude / ea.Magnitude);
            worstQ = Math.Max(worstQ,
                (set.Get(GreensKernel.ScalarPotential, Zlow, Zlow + Film).Evaluate(rho) - eq).Magnitude / eq.Magnitude);
        }
        _out.WriteLine($"cross-level fit error at 1 GHz: G_A^xx {worstA:E2}, G_q {worstQ:E2} " +
                       "— the defect is the geometric SERIES, not the crossing");

        Assert.True(worstA < 1e-3, $"G_A^xx is {worstA:E2} out (measured 4.7e-6)");
        Assert.True(worstQ > 1e-2, $"G_q is {worstQ:E2} out (measured 3.7e-1)");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T6 — the peeled model is still the WHOLE kernel, and the far view costs no second fit
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void T6_TheWholeKernelViewComesFromTheSameFit()
    {
        // ShallowImageSplit's two halves are a decomposition of ONE kernel and the fill tiers between
        // a treated near view and an untreated far one. Before MIM-12a the far view was Get() — the
        // same cached object, and therefore free. With a peel it is not, and taking it from Get()
        // would both pay for a second Dcim.FitAtHeights AND quietly hand the far pairs the less
        // accurate of two representations of one kernel. GetWhole is that view, from the same model.
        var g = new LayeredSpectralGreens(MimStack(Film), 1e9);
        var set = new PlanarKernelSet(g, PlanarExtractionOrder.Constant, 0.0,
                                      Dcim.ForStackAtFrequency(null, 2.0 * Math.PI * 1e9 / EmConstants.C0,
                                                               MimStack(Film).TopZ));

        var split = set.GetMinusShallowImages(GreensKernel.ScalarPotential, Zlow, Zlow + Film, Shallow);
        var whole = set.GetWhole(GreensKernel.ScalarPotential, Zlow, Zlow + Film, Shallow);
        Assert.NotEmpty(split.Removed);

        double worst = 0;
        foreach (double rho in new[] { 1e-6, 5e-6, 15e-6, 60e-6 })
        {
            Complex reassembled = split.Terms.Evaluate(rho);
            foreach (var im in split.Removed)
                reassembled += im.Amplitude / (4.0 * Math.PI * Complex.Sqrt(rho * rho + im.Depth * im.Depth));
            worst = Math.Max(worst, (reassembled - whole.Evaluate(rho)).Magnitude / whole.Evaluate(rho).Magnitude);
        }
        _out.WriteLine($"near view + removed images vs the whole-kernel view: worst {worst:E2}");
        Assert.True(worst < 1e-12, $"the two views are not the same kernel: {worst:E2}");

        // And it is ONE fit for the pairing, not two — D7's counter, which exists so a change that
        // starts refitting per view fails loudly rather than costing an hour a sweep.
        var fresh = new PlanarKernelSet(g, PlanarExtractionOrder.Constant, 0.0,
                                        Dcim.ForStackAtFrequency(null, 2.0 * Math.PI * 1e9 / EmConstants.C0,
                                                                 MimStack(Film).TopZ));
        fresh.GetMinusShallowImages(GreensKernel.ScalarPotential, Zlow, Zlow + Film, Shallow);
        fresh.GetWhole(GreensKernel.ScalarPotential, Zlow, Zlow + Film, Shallow);
        Assert.Equal(1, fresh.FitCount);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // T7 — M3: the ACCELERATED path's removed list is the new EXACT one
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void T7_TheAcceleratedPathCarriesTheExactImagesToo()
    {
        // MIM-8 built the tiering that PlanarAimBordered reads — the treated terms plus the closed
        // form for a near pair, the untreated view for a far one — and MimThinLayerTests.T9 gates the
        // on-demand operator against the dense matrix on a fixture where the split is not empty. What
        // MIM-12a changes is WHICH images are in that list, and a branch whose off-state is the
        // identity is a branch no existing test exercises. So this asserts the fixture is in the NEW
        // regime — the removed list holds the derived series, not a fitted image — before T9's own
        // comparison is worth anything.
        var g = new LayeredSpectralGreens(MimStack(Film), 10e9);
        var set = new PlanarKernelSet(g, PlanarExtractionOrder.Constant);
        var split = set.GetMinusShallowImages(GreensKernel.ScalarPotential, Zlow, Zlow + Film, Shallow);
        var exact = g.ThinRegionImagesAtHeights(GreensKernel.ScalarPotential, Zlow + Film, Zlow, Shallow);

        _out.WriteLine($"removed: {split.Removed.Count} images, of which {exact.Images.Count} are the " +
                       "derived series");
        Assert.True(split.Removed.Count >= exact.Images.Count);

        // The derived series is there, amplitude for amplitude and depth for depth — which is what
        // ShallowImageCore.CellPairMean will integrate over every near cell pair, on the dense path
        // and on the accelerated one alike.
        for (int n = 0; n < exact.Images.Count; n++)
        {
            Assert.Equal(exact.Images[n].Amplitude.Real, split.Removed[n].Amplitude.Real, 15);
            Assert.Equal(exact.Images[n].Depth.Real,     split.Removed[n].Depth.Real,     15);
        }
    }
}
