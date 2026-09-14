using System.Numerics;
using CircuitRF.Engine.Mom;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

/// <summary>
/// <b>CL6 — the ONE-SLAB kernel gets CL4's conducting floor, and keeps its own accuracy tier.</b>
/// <c>docs/sonnet-briefs/brief-conductor-loss-6-one-slab-floor.md</c>; findings in
/// <c>RESOLVED.md</c> §CL6.
///
/// <para>CL4 built a real ground plane and no run a user can make gets one, because a conducting
/// floor was expressible only on <c>LayerStack</c> and switching every single-dielectric run onto
/// the general kernel would re-base it from <c>Dcim.ValidatedRhoOverLambda</c>'s ≤6e-3 onto
/// <c>ValidatedRhoOverLambdaLayered</c>'s ≤1.6e-2. That trade is refused; the one-slab kernel gets
/// its own floor instead. <b>Nothing here widens a validated range</b>, and the brief's four
/// measurements are in its own order — each a TABLE first and an assertion second, because the
/// brief was allowed to refuse.</para>
///
/// <para><b>The PEC reduction is asked TWICE and the two halves answer different questions.</b>
/// <see cref="M1a_PecReduction_AgainstThePreCl6Arithmetic"/> compares the new coefficients against a
/// verbatim transcription of the pre-CL6 ones — that is what says the ζ = 0 arithmetic IS the
/// shipped arithmetic. <see cref="M1b_PerfectFloorSpellings_LandOnThePecBits"/> compares a PEC floor
/// against all three spellings of a perfect conductor through the fitted product — that is what says
/// the new path is TAKEN and reproduces the old bits, which is CL4 §1's own stronger claim. Neither
/// collapses a perfect floor onto the old code path to get there.</para>
/// </summary>
public sealed class OneSlabLossyFloorTests(ITestOutputHelper output)
{
    private const double CopperSigma = 5.8e7, GoldSigma = 4.1e7;

    private static IEnumerable<(string Name, GroundedSlab Pec, double Sigma, double T)> Starters()
    {
        yield return ("FR-4 1.6 mm, 35 µm Cu", GroundedSlab.Fr4Starter,  CopperSigma, 35e-6);
        yield return ("GaAs 100 µm, 3 µm Au",  GroundedSlab.GaAsStarter, GoldSigma,   3e-6);
    }

    private static GroundedSlab WithFloor(GroundedSlab s, Termination floor) => s with { Floor = floor };

    private static readonly double[] KRhoOverK0 =
        [0.0, 1e-8, 1e-5, 1e-3, 0.01, 0.1, 0.5, 0.9, 0.999, 1.001, 1.5, 2.0, 3.5, 10.0, 40.0, 300.0];

    private static double Rel(Complex expected, Complex actual, double floor = 1e-300) =>
        (expected - actual).Magnitude / Math.Max(expected.Magnitude, floor);

    private static int BitsMoved(Complex a, Complex b)
    {
        int n = 0;
        if (BitConverter.DoubleToInt64Bits(a.Real)      != BitConverter.DoubleToInt64Bits(b.Real))      n++;
        if (BitConverter.DoubleToInt64Bits(a.Imaginary) != BitConverter.DoubleToInt64Bits(b.Imaginary)) n++;
        return n;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // The pre-CL6 kernel, transcribed verbatim — the oracle R-cl6-1's first half compares against.
    //
    // It is written out here rather than reached for through the shipped class precisely because
    // the shipped class is the thing under test: a gate that asks the new code whether it agrees
    // with itself is vacuous. Every line below is the file as it stood before CL6, including the
    // association order of each product, because the claim is bit-identity and not closeness.
    // ═══════════════════════════════════════════════════════════════════════════════════════

    private sealed class PreCl6Kernel(GroundedSlab slab, double fHz)
    {
        public double  K0   { get; } = slab.FreeSpaceWavenumberAt(fHz);
        public Complex EpsR { get; } = slab.EpsComplex;
        private Complex K1 => K0 * Complex.Sqrt(EpsR);
        private readonly double _h = slab.HeightM;

        private static Complex ProperRoot(Complex squared)
        {
            var r = Complex.Sqrt(squared);
            return r.Imaginary > 0 ? -r : r;
        }

        private static Complex StableTan(Complex z) =>
            Math.Abs(z.Imaginary) > 30.0 ? new Complex(0, Math.Sign(z.Imaginary)) : Complex.Tan(z);

        public Complex Kz0(Complex kRho) => ProperRoot(K0 * (Complex)K0 - kRho * kRho);
        public Complex Kz1(Complex kRho) => ProperRoot(K1 * K1 - kRho * kRho);

        private Complex TanOverArgument(Complex kz1)
        {
            Complex u = kz1 * _h;
            if (u.Magnitude < 1e-4)
            {
                Complex u2 = u * u;
                return _h * (1.0 + u2 / 3.0 + 2.0 * u2 * u2 / 15.0);
            }
            return StableTan(u) / kz1;
        }

        private static bool Degenerate(Complex kz0, Complex kz1) =>
            kz0 == Complex.Zero && kz1 == Complex.Zero;

        public Complex Te(Complex kRho)
        {
            Complex kz0 = Kz0(kRho), kz1 = Kz1(kRho);
            if (Degenerate(kz0, kz1)) return -Complex.One;
            Complex a = Complex.ImaginaryOne * kz0 * TanOverArgument(kz1);
            return (a - 1.0) / (a + 1.0);
        }

        public Complex Tm(Complex kRho)
        {
            Complex kz0 = Kz0(kRho), kz1 = Kz1(kRho);
            if (Degenerate(kz0, kz1)) return -Complex.One;
            Complex a = Complex.ImaginaryOne * kz1 * kz1 * TanOverArgument(kz1);
            Complex b = EpsR * kz0;
            return (a - b) / (a + b);
        }

        public Complex Scalar(Complex kRho)
        {
            Complex kz0 = Kz0(kRho), kz1 = Kz1(kRho);
            if (Degenerate(kz0, kz1)) return -Complex.One;

            Complex j = Complex.ImaginaryOne;
            Complex s = TanOverArgument(kz1);
            Complex denTm = j * kz1 * kz1 * s + EpsR * kz0;
            Complex denTe = j * kz0 * s + 1.0;
            Complex gammaTm = (j * kz1 * kz1 * s - EpsR * kz0) / denTm;
            Complex diff = 2.0 * j * s * (EpsR - 1.0) / (denTm * denTe);
            return gammaTm - K0 * (Complex)K0 * diff;
        }

        public Complex Dispersion(SurfaceWavePolarization pol, Complex kRho)
        {
            Complex kz0 = Kz0(kRho), kz1 = Kz1(kRho), j = Complex.ImaginaryOne;
            Complex u = kz1 * _h;
            return pol == SurfaceWavePolarization.Tm
                ? j * kz1 * Complex.Sin(u) + EpsR * kz0 * Complex.Cos(u)
                : j * kz0 * Complex.Sin(u) + kz1 * Complex.Cos(u);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // R-cl6-1 — the PEC reduction, bit-identical
    // ═══════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void M1a_PecReduction_AgainstThePreCl6Arithmetic()
    {
        int moved = 0, total = 0;
        foreach (var (name, pec, _, _) in Starters())
        foreach (double f in new[] { 2e9, 10e9, 20e9 })
        {
            var now = new SpectralGreens(pec, f);
            var was = new PreCl6Kernel(pec, f);
            int here = 0, n = 0;
            foreach (double u in KRhoOverK0)
            {
                Complex kRho = u * now.K0;
                foreach (var (a, b) in new[]
                {
                    (was.Te(kRho),     now.ReflectionTe(kRho)),
                    (was.Tm(kRho),     now.ReflectionTm(kRho)),
                    (was.Scalar(kRho), now.ReflectionScalar(kRho)),
                })
                { here += BitsMoved(a, b); n += 2; }

                foreach (var pol in new[] { SurfaceWavePolarization.Tm, SurfaceWavePolarization.Te })
                { here += BitsMoved(was.Dispersion(pol, kRho), now.DispersionResidual(pol, kRho)); n += 2; }
            }
            output.WriteLine($"{name}, {f / 1e9:F0} GHz: Γ^h/Γ^e/Γ^q and both Ψ — {here}/{n} bits moved");
            moved += here; total += n;
        }
        output.WriteLine($"TOTAL {moved}/{total} bits moved against the pre-CL6 arithmetic");
        Assert.Equal(0, moved);
    }

    /// <summary>
    /// CL4 §1's own gate, one kernel over: a PEC floor against all three spellings of a perfect
    /// conductor, through the FITTED product rather than through the coefficients — so the poles,
    /// the asymptotic extraction and the image fit are all in the comparison.
    /// </summary>
    [Fact]
    public void M1b_PerfectFloorSpellings_LandOnThePecBits()
    {
        foreach (var (name, pec, _, t) in Starters())
        foreach (double f in new[] { 2e9, 10e9, 20e9 })
        {
            var fitted = new Dictionary<GreensKernel, DcimModel>();
            var gp = new SpectralGreens(pec, f);
            foreach (var kind in new[] { GreensKernel.ScalarPotential, GreensKernel.VectorPotential })
                fitted[kind] = Dcim.Fit(gp, kind);

            foreach (var (how, floor) in new[]
            {
                ("σ = +∞", Termination.LossyGround(double.PositiveInfinity, t)),
                ("t = 0",  Termination.LossyGround(CopperSigma, 0.0)),
                ("σ = 0",  Termination.LossyGround(0.0, t)),
            })
            {
                var gl = new SpectralGreens(WithFloor(pec, floor), f);
                int moved = 0, total = 0;
                foreach (var kind in new[] { GreensKernel.ScalarPotential, GreensKernel.VectorPotential })
                {
                    var m = Dcim.Fit(gl, kind);
                    foreach (double rl in new[] { 1e-3, 1e-2, 0.1, 0.5, 1.0 })
                    {
                        double rho = rl * EmConstants.C0 / f;
                        moved += BitsMoved(fitted[kind].Evaluate(rho), m.Evaluate(rho));
                        total += 2;
                    }
                }
                output.WriteLine($"{name}, {f / 1e9:F0} GHz, {how}: {moved}/{total} bits moved");
                Assert.Equal(0, moved);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // MEASUREMENT 2 / R-cl6-2 — does Γ^q's zero survive in ARITHMETIC?  D5's one-layer
    // reduction, re-run with the SAME floor on both sides.
    // ═══════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void M2_D5Reduction_AtEverySigma()
    {
        foreach (var (name, pec, sigma, t) in Starters())
        foreach (var (tag, slab) in new[]
        {
            ("PEC  ", pec),
            ("LOSSY", WithFloor(pec, Termination.LossyGround(sigma, t))),
        })
        {
            double worstMain = 0, worstAny = 0;
            string whereMain = "";
            foreach (double f in new[] { 1e9, 2e9, 10e9, 20e9, 40e9 })
            {
                var shipped = new SpectralGreens(slab, f);
                var general = new LayeredSpectralGreens(LayerStack.FromGroundedSlab(slab), f);
                foreach (double u in KRhoOverK0)
                {
                    Complex kRho = u * shipped.K0;
                    foreach (var (what, a, b) in new[]
                    {
                        ("Γ^h", shipped.ReflectionTe(kRho),
                                general.TopInterfaceFresnel(SurfaceWavePolarization.Te, kRho)),
                        ("Γ^e", shipped.ReflectionTm(kRho),
                                general.TopInterfaceFresnel(SurfaceWavePolarization.Tm, kRho)),
                        ("Γ^q", shipped.ReflectionScalar(kRho),
                                general.TopInterfaceReflection(GreensKernel.ScalarPotential, kRho)),
                    })
                    {
                        double rel = Rel(a, b, 1e-14);
                        worstAny = Math.Max(worstAny, rel);
                        if (u > 40) continue;
                        if (rel > worstMain) { worstMain = rel; whereMain = $"{what} at u={u}, {f / 1e9:G} GHz"; }
                    }
                }
            }
            output.WriteLine($"{name} {tag}: worst relative inside k_ρ ≤ 40k₀ {worstMain:E3} ({whereMain}); " +
                             $"worst anywhere (to 300 k₀) {worstAny:E3}");
            // D5's own two tolerances, un-widened: 1e-13 inside k_ρ ≤ 40k₀, 2e-11 past it, where
            // both kernels are computing the same exactly-Fresnel limit by two underflowing routes.
            Assert.True(worstMain < 1e-13, $"{name} {tag}: {worstMain:E3} inside k_ρ ≤ 40k₀ ({whereMain})");
            Assert.True(worstAny  < 2e-11, $"{name} {tag}: {worstAny:E3} anywhere");
        }
    }

    /// <summary>
    /// <b>The half of measurement 2 that is about Γ^q and nothing else — and the brief's one named
    /// risk.</b> <c>Γ^q = Γ^e − (k₀²/k_ρ²)(Γ^e − Γ^h)</c>, and the shipped kernel meets that
    /// <c>k_ρ²</c> with an ALGEBRAICALLY cancelled identity so nothing is divided by a small number.
    /// That identity is derived from the shorted-slab algebra and does not survive Z_s ≠ 0 as
    /// written; CL6 re-derives it (<c>P</c>, in this file's own header) rather than taking
    /// <c>LayeredSpectralGreens.ReflectionTaylor</c>'s contour route.
    ///
    /// <para><b>The gate is the VALUE, not the identity.</b> Γ^q is swept down through the decade
    /// the DCIM sampling path starts in and compared against the general kernel's INDEPENDENT
    /// answer — which reaches the same quantity by numerical Taylor coefficients off a small contour
    /// — and the naive route is computed beside it, so this test cannot quietly stop demonstrating
    /// why the factored form is needed. That is <c>LayeredSpectralGreens.ScalarKernelNaive</c>'s own
    /// pattern, one kernel over.</para>
    /// </summary>
    [Fact]
    public void M2b_TheScalarsDoubleZero_IsRealisedInFloatingPoint()
    {
        foreach (var (name, pec, sigma, t) in Starters())
        {
            var slab = WithFloor(pec, Termination.LossyGround(sigma, t));
            var g = new SpectralGreens(slab, 10e9);
            var general = new LayeredSpectralGreens(LayerStack.FromGroundedSlab(slab), 10e9);
            output.WriteLine($"=== {name}, 10 GHz, conducting floor ===");
            output.WriteLine("     k_rho/k0        rel vs general        naive route error");

            double worst = 0, worstNaive = 0;
            foreach (double u in new[] { 1e-1, 1e-2, 1e-3, 1e-4, 1e-5, 1e-6, 1e-7, 1e-8 })
            {
                Complex kRho = u * g.K0;
                Complex mine = g.ReflectionScalar(kRho);
                Complex theirs = general.TopInterfaceReflection(GreensKernel.ScalarPotential, kRho);
                Complex naive = g.ReflectionTm(kRho)
                              - (g.K0 * g.K0) / (kRho * kRho) * (g.ReflectionTm(kRho) - g.ReflectionTe(kRho));

                double rel = Rel(theirs, mine);
                double naiveErr = Rel(theirs, naive);
                worst = Math.Max(worst, rel);
                worstNaive = Math.Max(worstNaive, naiveErr);
                output.WriteLine($"   {u,10:E1}   {rel,18:E3}   {naiveErr,22:E3}");
            }

            Complex atZero = g.ReflectionScalar(Complex.Zero);
            Complex nearZero = g.ReflectionScalar(1e-9 * g.K0);
            output.WriteLine($"   Γ^q(0) = {atZero}, Γ^q(1e-9 k₀) = {nearZero}, rel {Rel(atZero, nearZero):E2}");
            output.WriteLine($"   worst factored {worst:E3}; worst NAIVE {worstNaive:E3}");

            Assert.True(worst < 1e-13, $"{name}: the factored Γ^q is {worst:E3} from the general kernel");
            Assert.True(double.IsFinite(atZero.Real) && double.IsFinite(atZero.Imaginary));
            Assert.True(Rel(atZero, nearZero) < 1e-12);
            // The naive route must still be RUINED down here, or this test has stopped saying
            // anything about why the cancellation was re-derived.
            Assert.True(worstNaive > 1e-6, $"{name}: the naive route no longer degrades ({worstNaive:E3})");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // MEASUREMENT 3 — does the DCIM fit stay inside the UN-WIDENED one-slab tier?
    // ═══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The whole point of the brief — the tier is what is being bought.</b> Spectral error
    /// against direct Sommerfeld integration of the SAME kernel, scaled on the free-space kernel
    /// (|ΔG|·4πρ — what a MoM fill experiences), over §5's own span ρ/λ ∈ [1e-4, 1], both kernels,
    /// both starters, 2/10/20 GHz, PEC floor and real floor. The ceiling is
    /// <see cref="Dcim.ValidatedRhoOverLambda"/>'s own ≤ 6e-3 and NOTHING is widened to meet it.
    ///
    /// <para><b>It is in the ROUTINE tier and that is a measurement, not a preference:</b> the whole
    /// sweep — 12 cases, 24 fits, 408 direct Sommerfeld evaluations — is <b>3.96 s</b>, under the
    /// repo's mechanical ~5 s <c>Category=Benchmark</c> threshold. CL4's own counterpart was 5.0 s
    /// on the general kernel and was tagged; this one is cheaper because the one-slab reflection
    /// coefficient is a closed form rather than a cascade. So there is no cheap counterpart beside
    /// it: the expensive half IS the gate that runs on every build, which is the outcome CL4 §10's
    /// "a gate nobody runs is not a gate" was settling for.</para>
    /// </summary>
    [Fact]
    public void M3_DcimFit_AgainstDirectSommerfeld()
    {
        foreach (var (name, pec, sigma, t) in Starters())
        foreach (double f in new[] { 2e9, 10e9, 20e9 })
        foreach (var (tag, slab) in new[]
        {
            ("PEC  ", pec),
            ("LOSSY", WithFloor(pec, Termination.LossyGround(sigma, t))),
        })
        {
            var g = new SpectralGreens(slab, f);
            double lam = EmConstants.C0 / f;
            output.WriteLine($"=== {name}, {f / 1e9:F0} GHz, {tag} ===");
            var models = new Dictionary<GreensKernel, DcimModel>
            {
                [GreensKernel.ScalarPotential] = Dcim.Fit(g, GreensKernel.ScalarPotential),
                [GreensKernel.VectorPotential] = Dcim.Fit(g, GreensKernel.VectorPotential),
            };
            foreach (var (kind, m) in models)
                output.WriteLine($"   {kind,-16} {m.Images.Count} images, {m.SurfaceWaves.Count} SW, " +
                                 $"fit residual {m.FitResidual:E2}, sum-rule {m.SumRuleResidual.Magnitude:E2}");

            output.WriteLine("     rho/lam     scaled G_q     scaled G_A       rel G_q       rel G_A");
            var worst = new Dictionary<GreensKernel, (double E, double At)>
                { [GreensKernel.ScalarPotential] = (0, 0), [GreensKernel.VectorPotential] = (0, 0) };
            for (double rl = 1e-4; rl <= 1.0001; rl *= Math.Pow(10, 0.25))
            {
                double rho = rl * lam, fs = 1.0 / (4 * Math.PI * rho);
                var cells = new List<string>(); var rels = new List<string>();
                foreach (var kind in new[] { GreensKernel.ScalarPotential, GreensKernel.VectorPotential })
                {
                    Complex exact = SommerfeldIntegral.Evaluate(g, kind, rho).Value;
                    double abs = (models[kind].Evaluate(rho) - exact).Magnitude;
                    double scaled = abs / fs;
                    cells.Add($"{scaled,13:E3}"); rels.Add($"{abs / exact.Magnitude,13:E3}");
                    if (Dcim.WithinValidatedRange(kind, rl).Ok && scaled > worst[kind].E) worst[kind] = (scaled, rl);
                }
                output.WriteLine($"   {rl,9:E2} {string.Join("", cells)} {string.Join("", rels)}");
            }
            foreach (var k in worst.Keys)
            {
                output.WriteLine($"   WORST IN RANGE {k,-16} {worst[k].E:E3} at rho/lam {worst[k].At:E2} " +
                                 $"(ceiling 6e-3)");
                Assert.True(worst[k].E <= 6e-3, $"{name} {f / 1e9:F0} GHz {tag} {k}: {worst[k].E:E3} > 6e-3");
            }
            output.WriteLine("");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // MEASUREMENT 4 — where do the one-slab poles go?
    // ═══════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void M4_SurfaceWavePoles_OnAConductingFloor()
    {
        output.WriteLine($"PoleLossCeiling = {PlanarSurfaceWaveLaunch.PoleLossCeiling:G3}");
        double worstAnywhere = 0;
        foreach (var (name, pec, sigma, t) in Starters())
        {
            output.WriteLine($"=== {name} ===");
            output.WriteLine("      f GHz   mode      PEC |Im|/Re      LOSSY |Im|/Re        delta");
            foreach (double f in new[] { 1e9, 2e9, 5e9, 10e9, 20e9, 40e9 })
            {
                var p = new SpectralGreens(pec, f).SurfaceWaveModes;
                var l = new SpectralGreens(WithFloor(pec, Termination.LossyGround(sigma, t)), f).SurfaceWaveModes;
                Assert.Equal(p.Count, l.Count);
                for (int i = 0; i < p.Count; i++)
                {
                    double a = Math.Abs(p[i].KRho.Imaginary) / Math.Abs(p[i].KRho.Real);
                    double b = Math.Abs(l[i].KRho.Imaginary) / Math.Abs(l[i].KRho.Real);
                    Assert.Equal(p[i].Name, l[i].Name);
                    worstAnywhere = Math.Max(worstAnywhere, b);
                    output.WriteLine($"   {f / 1e9,8:F0}   {l[i].Name,-6}  {a,14:E4}  {b,16:E4}  {b - a,12:E3}" +
                                     (b > PlanarSurfaceWaveLaunch.PoleLossCeiling ? "   *** PAST CEILING" : ""));
                }
            }
            output.WriteLine("");
        }
        output.WriteLine($"worst |Im k_ρ|/Re k_ρ anywhere with a real plane: {worstAnywhere:E4} " +
                         $"(ceiling {PlanarSurfaceWaveLaunch.PoleLossCeiling:G3})");
        Assert.True(worstAnywhere < PlanarSurfaceWaveLaunch.PoleLossCeiling);
    }

    /// <summary>
    /// <b>The delegation is measured, not assumed.</b> On a PEC floor the shipped closed-form
    /// bisection and CL4's chain-matrix search are two independent answers to the same question, and
    /// the lossy path swaps one for the other — so how far apart they are is the size of the seam
    /// this brief introduces. The PEC path keeps the shipped route precisely because they are not
    /// bit-identical and DCIM subtracts each pole in closed form.
    /// </summary>
    [Fact]
    public void M4b_TheTwoPoleSearches_AgreeOnAPecFloor()
    {
        double worst = 0;
        foreach (var (name, pec, _, _) in Starters())
        foreach (double f in new[] { 1e9, 10e9, 40e9 })
        {
            var mine = new SpectralGreens(pec, f).SurfaceWaveModes;
            var theirs = SurfaceWavePoles.Find(LayerStack.FromGroundedSlab(pec), f).Modes;
            Assert.Equal(mine.Count, theirs.Count);
            for (int i = 0; i < mine.Count; i++)
            {
                double rel = Rel(mine[i].KRho, theirs[i].KRho);
                worst = Math.Max(worst, rel);
                output.WriteLine($"{name}, {f / 1e9:F0} GHz, {mine[i].Name}: shipped {mine[i].KRho}, " +
                                 $"chain-matrix {theirs[i].KRho}, rel {rel:E3}");
            }
        }
        output.WriteLine($"worst disagreement between the two searches on a PEC floor: {worst:E3}");
        Assert.True(worst < 1e-9, $"the two pole searches disagree by {worst:E3} on a PEC floor");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // R-cl6-3 — the εᵣ = 1 image reduction survives
    // ═══════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void M5_EpsR1ImageReduction_Survives()
    {
        const double f = 10e9;
        foreach (var (tag, floor) in new[]
        {
            ("σ = +∞      ", Termination.LossyGround(double.PositiveInfinity, 35e-6)),
            ("35 µm copper", Termination.LossyGround(CopperSigma, 35e-6)),
        })
        {
            double worstRel = 0, worstScaled = 0;
            foreach (double hM in new[] { 0.8e-3, 1.6e-3, 3.2e-3 })
            {
                var slab = WithFloor(new GroundedSlab(hM, EmMaterial.Air), floor);
                var g = new SpectralGreens(slab, f);
                double lambda = 2 * Math.PI / g.K0;
                foreach (double over in new[] { 1e-3, 1e-2, 0.1, 0.5, 1.0, 3.0 })
                foreach (var kind in new[] { GreensKernel.VectorPotential, GreensKernel.ScalarPotential })
                {
                    double rho = over * lambda, z = hM;
                    Complex got = SommerfeldIntegral.Evaluate(g, kind, rho).Value;
                    Complex expected = SommerfeldIntegral.FreeSpace(g.K0, rho)
                                     - SommerfeldIntegral.FreeSpace(g.K0, Math.Sqrt(rho * rho + 4 * z * z));
                    worstRel    = Math.Max(worstRel, (expected - got).Magnitude / expected.Magnitude);
                    worstScaled = Math.Max(worstScaled, (expected - got).Magnitude * 4 * Math.PI * rho);
                }
            }
            output.WriteLine($"{tag}: worst relative {worstRel:E3}, worst scaled {worstScaled:E3} " +
                             "(free space + one NEGATIVE image)");
            // The perfect row is §3.1's strongest closed-form oracle and must stay exact; the lossy
            // row is the MEASURED SIZE of the imperfection a real plane is for, not a failure.
            if (floor.ConductivitySm is double.PositiveInfinity)
                Assert.True(worstRel < 1e-8, $"the εᵣ = 1 image reduction moved: {worstRel:E3}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    // R-cl6-4 / R-cl6-5 / R-cl6-6 — the three things that must NOT move
    // ═══════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void M6_StaticRoutesAreBitIdenticalAtEverySigma()
    {
        foreach (var (name, pec, sigma, t) in Starters())
        {
            var lossy = WithFloor(pec, Termination.LossyGround(sigma, t));
            int moved = 0, total = 0;

            foreach (double rl in new[] { 1e-3, 1e-2, 0.1, 1.0, 10.0 })
            {
                double rho = rl * pec.HeightM;
                moved += BitsMoved(StaticGreens.ScalarPotential(pec, rho), StaticGreens.ScalarPotential(lossy, rho));
                moved += BitsMoved(StaticGreens.VectorPotential(pec, rho), StaticGreens.VectorPotential(lossy, rho));
                total += 4;
            }

            // SingularExtraction's own ω → 0 decompositions, which are what the fill actually reads.
            var sp = PlanarKernelTerms.StaticScalar(pec);
            var sl = PlanarKernelTerms.StaticScalar(lossy);
            var vp = PlanarKernelTerms.StaticVector(pec);
            var vl = PlanarKernelTerms.StaticVector(lossy);
            moved += BitsMoved(sp.Inverse, sl.Inverse) + BitsMoved(sp.Constant, sl.Constant);
            moved += BitsMoved(vp.Inverse, vl.Inverse) + BitsMoved(vp.Constant, vl.Constant);
            total += 8;

            output.WriteLine($"{name}: static routes at a REAL σ — {moved}/{total} bits moved");
            Assert.Equal(0, moved);
        }
    }

    [Fact]
    public void M7_AsymptoticReflection_DoesNotMove()
    {
        foreach (var (name, pec, sigma, t) in Starters())
        foreach (double f in new[] { 2e9, 10e9, 20e9 })
        {
            var a = new SpectralGreens(pec, f);
            var b = new SpectralGreens(WithFloor(pec, Termination.LossyGround(sigma, t)), f);
            foreach (var kind in new[] { GreensKernel.VectorPotential, GreensKernel.ScalarPotential })
            {
                Assert.Equal(0, BitsMoved(a.AsymptoticReflection(kind), b.AsymptoticReflection(kind)));

                // And it is still the LIMIT, not merely an unchanged constant: the floor reaches the
                // top only through e^{−2k_ρh}, so Γ walks into it from the conducting stack too.
                Complex far = b.Reflection(kind, 4000.0 * b.K0);
                double gap = (far - b.AsymptoticReflection(kind)).Magnitude;
                output.WriteLine($"{name}, {f / 1e9:F0} GHz, {kind,-16} Γ(∞) = {b.AsymptoticReflection(kind)}, " +
                                 $"|Γ(4000k₀) − Γ(∞)| = {gap:E3}");
                Assert.True(gap < 1e-6, $"{kind} does not walk into its k_ρ → ∞ limit on a conducting floor");
            }
        }
    }

    [Fact]
    public void M8_TheKernelFloorIsUnchanged_AndANonGroundFloorIsRefusedByName()
    {
        var slab = GroundedSlab.Fr4Starter;
        double just = GroundedSlab.MinElectricalThickness * 1.01 * EmConstants.C0 / (2 * Math.PI * slab.HeightM);
        double under = GroundedSlab.MinElectricalThickness * 0.99 * EmConstants.C0 / (2 * Math.PI * slab.HeightM);

        foreach (var floor in new[] { Termination.Pec, Termination.LossyGround(CopperSigma, 35e-6) })
        {
            var s = WithFloor(slab, floor);
            Assert.True(SpectralGreens.CanSolveAt(s, just).Ok);
            var no = SpectralGreens.CanSolveAt(s, under);
            Assert.False(no.Ok);
            Assert.Contains("floor of", no.Reason ?? "");
            output.WriteLine($"{floor}: solves at k₀h = {GroundedSlab.MinElectricalThickness * 1.01:E2}, " +
                             $"refused at {GroundedSlab.MinElectricalThickness * 0.99:E2} — the floor did not move");
        }

        foreach (var notAGround in new[] { Termination.Pmc, Termination.Air, Termination.OpenTo(new EmMaterial(11.9)) })
        {
            var no = SpectralGreens.CanSolveAt(WithFloor(slab, notAGround), 10e9);
            Assert.False(no.Ok);
            string why = no.Reason ?? "";
            Assert.Contains("GROUND", why);
            Assert.Contains("LayeredSpectralGreens", why);
            output.WriteLine($"refused: {why[..Math.Min(90, why.Length)]}…");
        }

        var nan = SpectralGreens.CanSolveAt(WithFloor(slab, Termination.LossyGround(double.NaN, 35e-6)), 10e9);
        Assert.False(nan.Ok);
        Assert.Contains("NaN", nan.Reason ?? "");
    }

    /// <summary>
    /// D5's bridge keeps its meaning at every σ — the property the whole brief turns on.
    /// </summary>
    [Fact]
    public void M9_FromGroundedSlab_CarriesTheFloor()
    {
        var floor = Termination.LossyGround(CopperSigma, 35e-6);
        var stack = LayerStack.FromGroundedSlab(WithFloor(GroundedSlab.Fr4Starter, floor));
        Assert.Equal(TerminationKind.SurfaceImpedance, stack.Bottom.Kind);
        Assert.Equal(CopperSigma, stack.Bottom.ConductivitySm);
        Assert.Equal(35e-6, stack.Bottom.ThicknessM);
        Assert.True(stack.Dissipates);
        // And a slab built the ordinary way is still a PEC-floored one, byte for byte the old bridge.
        Assert.Equal(TerminationKind.Pec, LayerStack.FromGroundedSlab(GroundedSlab.Fr4Starter).Bottom.Kind);
    }
}
