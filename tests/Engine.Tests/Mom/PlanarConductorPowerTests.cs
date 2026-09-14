using System.Numerics;
using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using NumFlat;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

/// <summary>
/// <b>CL2 — <c>P_conductor</c> stops being an identical zero.</b> R-cl2-1 … R-cl2-4.
///
/// <para>Three of the four gates here are about the ARITHMETIC of a budget that sums to
/// <c>P_accepted</c> by construction and therefore cannot gate itself: the lossless case (where
/// every absorbing term must vanish), a second independent route to the conductor term (which is
/// what catches a factor of two), and the SIGN of the residual on a substrate where the conductor
/// term dominates it thirtyfold (which is what catches the term being added beside the residual
/// instead of taken out of it). The fourth measures the consequence the whole brief exists for.</para>
/// </summary>
public class PlanarConductorPowerTests(ITestOutputHelper output)
{
    private const double CopperSigma = 5.8e7, GoldSigma = 4.1e7;

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-cl2-1 — the lossless balance still closes, with the conductor term SWITCHED ON
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>ANT-5's own gate, re-run with the fill's conductor-loss term live and the metal declared a
    /// PERFECT CONDUCTOR.</b> With tanδ = 0, σ = ∞ and a PEC floor there is no absorption anywhere,
    /// so <c>P_dielectric</c> and <c>P_conductor</c> must BOTH come out zero and the three
    /// independent routes — ½Re(Y_jj) from the factorisation, ∫U dΩ from the far field, and the pole
    /// residues — must still close on one number.
    ///
    /// <para><b>Two substrates of very different thickness, for ANT-5's own reason</b>: a balance
    /// that closes in one regime may be closing on a cancellation. The 8 mm case books a third of
    /// its accepted power into the surface wave and the 1.6 mm case a fifth.</para>
    ///
    /// <para>The conductor zero here is <b>exact</b>, not small:
    /// <see cref="PlanarSurfaceImpedance.Sheet"/> returns <c>Complex.Zero</c> for a perfect
    /// conductor, so the quadratic form is a sum of terms every one of which is skipped. σ = +∞ is
    /// deliberately the spelling used — it is the one CL1 found produces a NaN if the three
    /// spellings of PEC are not asked in one place.</para>
    /// </summary>
    [Theory]
    [InlineData(1.6e-3, 4.4, 2.9e-3)]     // the shipped FR-4 starter cross-section, loss removed
    [InlineData(8.0e-3, 2.2, 20e-3)]      // five times thicker, low permittivity
    public void R_cl2_1_TheLosslessBalanceCloses_WithTheConductorTermOn(double h, double epsR,
                                                                        double widthM)
    {
        const double fHz = 5e9;
        var slab = new GroundedSlab(h, new EmMaterial(epsR, 0));
        double lambda = EmConstants.C0 / (fHz * Math.Sqrt(0.5 * (epsR + 1)));

        // σ = +∞ and t = 35 µm: a real thickness with an unreal conductivity, which is how kernel A
        // spells a perfect ground and therefore how an A-vs-B comparison hands this a PEC.
        var problem = Problem(slab, fHz, double.PositiveInfinity, 35e-6,
                              PlanarLineFixtures.Rect(0, -0.5 * widthM, 0.5 * lambda, 0.5 * widthM));
        var (mesh, ports) = PlanarLineFixtures.MeshAndPorts(problem, PlanarLineFixtures.Coarse);

        var loss = PlanarConductorLoss.For(problem);
        var st   = PlanarFillSettings.Default with { ConductorLoss = loss };
        var sol  = new PlanarSolveContext(mesh, ports, st)
                       .SolveAt(PlanarLineFixtures.Kernel(slab, fHz), fHz);

        var pattern = PlanarFarField.Compute(problem, mesh, sol.Currents[0], ports[0].Number, fHz,
                                             PlanarFarFieldGrid.Hemisphere(1, 2));
        var budget = PlanarPowerBudget.For(
            problem, mesh, sol.Currents[0], pattern, sol.Y[0, 0],
            new PlanarConductorLossInputs(loss, PlanarGram.Build(mesh), null));

        double residual = budget.DielectricAndGroundW / budget.AcceptedW;
        double swShare  = budget.SurfaceWaveW / budget.AcceptedW;
        output.WriteLine($"h = {h * 1e3:F2} mm, εᵣ = {epsR}, N = {mesh.Bases.Count}");
        output.WriteLine(budget.Caption);
        output.WriteLine($"dielectric residual = {residual:E3} of accepted; surface wave = {swShare:P2}");

        Assert.True(budget.ConductorModelled, "the fill's term was on, so the budget must say so");
        Assert.Equal(0.0, budget.ConductorW);            // exact, not approximate — see the summary
        Assert.Equal(0.0, budget.Conductor!.SheetW);
        Assert.Equal(0.0, budget.Conductor!.BarrelW);

        Assert.True(budget.AcceptedW > 0, "a driven passive structure accepts power");
        Assert.True(budget.RadiatedW > 0, "an open planar structure radiates");
        Assert.True(budget.SurfaceWaveW > 0, "a grounded slab guides a TM₀ mode at any thickness");
        Assert.InRange(swShare, 0.05, 0.95);
        Assert.True(Math.Abs(residual) < 1e-3,
            $"a LOSSLESS substrate with PEC metal absorbs nothing, so the dielectric residual must " +
            $"vanish; it is {residual:E3} of the accepted power");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-cl2-2 — two routes to the conductor term agree. THE GATE THAT CATCHES A FACTOR OF TWO.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The shipped quadratic form against a direct numerical <c>∫Re(Z_s)|J|²dS</c> over
    /// <see cref="PlanarCurrentDensity"/>'s own map.</b> The direct route is a test-only reference
    /// and does not ship — the whole point of the shipped route is that there is exactly ONE of it.
    ///
    /// <para><b>The two are not the same quadrature and the difference is known in closed form</b>,
    /// which is what makes the tolerance a measurement rather than a guess. The map is the current
    /// at each cell's CENTRE, so the direct sum is a midpoint rule; over one cell shared by rooftops
    /// carrying I_m and I_n the exact integral exceeds it by exactly
    /// <c>Δ·|I_m − I_n|²/(12·L)</c>. That vanishes where the current is flat and grows where it
    /// crowds, so the midpoint route reads LOW, by second order in the current's own variation — and
    /// a factor of two is three decades outside anything that can produce.</para>
    /// </summary>
    [Theory]
    [InlineData("fr4-line")]
    [InlineData("fr4-patch")]
    [InlineData("gaas-line")]
    public void R_cl2_2_TwoRoutesToTheConductorTermAgree(string fixtureName)
    {
        var (problem, mesh, ports, fHz) = Fixture(fixtureName);
        var loss = PlanarConductorLoss.For(problem);
        var gram = PlanarGram.Build(mesh);
        var sol  = new PlanarSolveContext(mesh, ports,
                       PlanarFillSettings.Default with { ConductorLoss = loss })
                       .SolveAt(PlanarLineFixtures.Kernel(problem.Slab, fHz), fHz);

        var shipped = new PlanarConductorLossInputs(loss, gram, null)
                          .PowerOf(mesh, sol.Currents[0], fHz);
        double direct = DirectSheetPowerW(problem, mesh, loss, sol.Currents[0], ports[0].Number, fHz);

        double rel = (shipped.SheetW - direct) / shipped.SheetW;
        output.WriteLine($"{fixtureName}: N = {mesh.Bases.Count}, {mesh.Cells.Count} cells, " +
                         $"f = {fHz / 1e9:F2} GHz");
        output.WriteLine($"  quadratic form xᴴGx : {shipped.SheetW:E6} W");
        output.WriteLine($"  direct ∫Re(Z_s)|J|² : {direct:E6} W");
        output.WriteLine($"  (exact − midpoint)/exact = {rel:P3}");

        Assert.True(shipped.SheetW > 0, "a lossy conductor carrying current dissipates power");
        Assert.Equal(0.0, shipped.BarrelW);          // single level, no vertical basis
        // The midpoint rule reads LOW, always — the closed form above is a sum of squares. A route
        // that read HIGH would mean the quadratic form had lost an off-diagonal, not that the
        // quadrature had drifted, so the sign is asserted separately from the magnitude.
        Assert.True(rel > 0,
            $"the midpoint route must under-read the exact integral; it read {rel:P3}");
        Assert.True(rel < 0.10,
            $"the two routes are {rel:P3} apart, which is far more than a midpoint rule's own " +
            $"second-order deficit — the likely cause is a missing factor of two on the Gram's " +
            $"stored upper triangle");
    }

    /// <summary>
    /// <b>The same agreement on a mesh carrying VIAS</b>, where the barrel arm is live — the one
    /// place the two arms could double-count each other, since a vertical basis has no Gram row at
    /// all and its dissipation is the elementary ½Re(Z)|I|².
    /// </summary>
    [Fact]
    public void R_cl2_2b_TheBarrelArmIsSeparateAndAgreesWithItsOwnDirectSum()
    {
        var (problem, mesh, levels, ports, fHz) = ViaFixture();
        var loss = PlanarConductorLoss.For(problem);
        var gram = PlanarGram.Build(mesh);
        var sol  = new PlanarSolveContext(mesh, ports,
                       PlanarFillSettings.Default with { ConductorLoss = loss }, levels)
                       .SolveAt(PlanarLineFixtures.Kernel(problem.Slab, fHz), fHz);

        var got = new PlanarConductorLossInputs(loss, gram, levels).PowerOf(mesh, sol.Currents[0], fHz);

        // The direct barrel sum, written out rather than called: ½Re(Z)|I|² over every vertical
        // basis. It is the SAME arithmetic, which is why this half is an identity and the sheet
        // half is a quadrature comparison — there is nothing to approximate about a scalar.
        double omega = 2.0 * Math.PI * fHz, barrel = 0;
        int nz = 0;
        for (int i = 0; i < mesh.Bases.Count; i++)
        {
            var b = mesh.Bases[i];
            if (b.Direction != PlanarBasisDirection.Z) continue;
            nz++;
            var x = sol.Currents[0][i];
            barrel += 0.5 * loss.BarrelAt(mesh, levels, b, omega).Real * (x * Complex.Conjugate(x)).Real;
        }

        output.WriteLine($"N = {mesh.Bases.Count} ({nz} vertical), sheet {got.SheetW:E6} W, " +
                         $"barrels {got.BarrelW:E6} W, total {got.TotalW:E6} W");
        Assert.True(nz > 0, "the via fixture must carry vertical bases or this gate is vacuous");
        Assert.True(got.SheetW > 0);
        Assert.True(got.BarrelW > 0, "a gold via barrel at 30 GHz is not lossless");
        Assert.Equal(barrel, got.BarrelW, 1e-15);
        Assert.Equal(got.SheetW + got.BarrelW, got.TotalW, 1e-15);
    }

    /// <summary>
    /// <b>The Gram's stored UPPER TRIANGLE is counted twice off the diagonal and once on it</b> —
    /// stated against a hand-built current vector rather than a solve, so it fails on the arithmetic
    /// alone. Drive one basis at 1 A and the answer is ½Re(Z_s)·G[i,i]; drive two neighbours and the
    /// cross term appears with its factor of two.
    /// </summary>
    [Fact]
    public void R_cl2_2c_TheOffDiagonalIsCountedTwice_AgainstAHandBuiltCurrent()
    {
        var (problem, mesh, _, fHz) = Fixture("fr4-line");
        var loss = PlanarConductorLoss.For(problem);
        var gram = PlanarGram.Build(mesh);
        var c    = new PlanarConductorLossInputs(loss, gram, null);
        double omega = 2.0 * Math.PI * fHz;
        double rs = loss.SheetTable(mesh, omega)[0].Real;

        // A pair that actually shares a cell — the first stored off-diagonal.
        int i = 0, j = -1;
        while (j < 0 && i < gram.UnknownCount)
        {
            for (int k = gram.RowPtr[i]; k < gram.RowPtr[i + 1]; k++)
                if (gram.ColIdx[k] != i) { j = gram.ColIdx[k]; break; }
            if (j < 0) i++;
        }
        Assert.True(j >= 0, "the Gram must hold an off-diagonal or this gate is vacuous");

        var one = new Complex[mesh.Bases.Count];
        one[i] = Complex.One;
        double solo = c.PowerOf(mesh, one.ToVector(), fHz).SheetW;
        Assert.Equal(0.5 * rs * gram.At(i, i), solo, 1e-15 * solo);

        var two = new Complex[mesh.Bases.Count];
        two[i] = two[j] = Complex.One;
        double pair = c.PowerOf(mesh, two.ToVector(), fHz).SheetW;
        double want = 0.5 * rs * (gram.At(i, i) + gram.At(j, j) + 2.0 * gram.At(i, j));

        output.WriteLine($"bases {i} and {j}: G_ii = {gram.At(i, i):E6}, G_jj = {gram.At(j, j):E6}, " +
                         $"G_ij = {gram.At(i, j):E6}, Re Z_s = {rs:E6} Ω/sq");
        output.WriteLine($"  one basis driven : {solo:E9} W");
        output.WriteLine($"  both driven      : {pair:E9} W (want {want:E9} W)");
        // Halving the cross term would land 1 − G_ij/(G_ii+G_jj+2G_ij) below this — a per-cent-scale
        // move on a real mesh, which is exactly why this is asserted here and not left to R-cl2-2.
        // The tolerance is RELATIVE and a few ulp wide: the two spellings sum the same three terms
        // in different orders, which is worth 3 ulp here and is not what this gate is about.
        Assert.Equal(want, pair, 1e-15 * want);

        // And the IMAGINARY part of the quadratic form cancels identically: rotating the whole
        // current vector by a phase cannot change a dissipated power.
        var rot = new Complex[mesh.Bases.Count];
        rot[i] = Complex.ImaginaryOne; rot[j] = Complex.ImaginaryOne;
        Assert.Equal(pair, c.PowerOf(mesh, rot.ToVector(), fHz).SheetW, 1e-15 * pair);
    }

    /// <summary>
    /// <b>And the gap between the two routes is the MIDPOINT RULE's, to twelve digits.</b> R-cl2-2
    /// gates at a band; this says what the band is MADE of, and it is what turns "the two agree to
    /// the quadrature's accuracy" from a hope into a statement.
    ///
    /// <para>On one cell carrying rooftops I_m and I_n, with Δ the extent along the flow and L the
    /// transverse extent, the exact integral is <c>(Δ/3L)(|I_m|²+|I_n|²+Re(I_m*I_n))</c> and the
    /// midpoint rule is <c>(Δ/4L)|I_m+I_n|²</c>; the difference is exactly
    /// <c>Δ·|I_m − I_n|²/(12L)</c> — a sum of squares, which is why the reference route can only
    /// under-read, and which vanishes where the current is flat. Summing that closed form over the
    /// mesh reproduces the observed gap to round-off, so <b>the two routes differ by the reference
    /// route's quadrature and by NOTHING else</b>. A dropped off-diagonal, a missing ½ or a wrong
    /// Z_s would not be expressible in it.</para>
    ///
    /// <para><b>It also explains why the band is per cent rather than per mille, and why refining
    /// the mesh barely helps</b> — measured on this fixture at cells/λ 10 / 20 / 40 / 80 (16 / 20 /
    /// 36 / 72 cells): 8.91 / 7.07 / 4.45 / 3.16 %, i.e. falling like √(cells) rather than like
    /// cells². The deficit is dominated by |I_m − I_n|² at the strip's own RIM, where the transverse
    /// current has a 1/√d edge behaviour a uniform mesh never resolves, so the jump between
    /// neighbouring coefficients does not shrink with refinement the way it does mid-strip. That is
    /// a property of the test-only reference route, not of the shipped one.</para>
    /// </summary>
    [Theory]
    [InlineData("fr4-line")]
    [InlineData("fr4-patch")]
    [InlineData("gaas-line")]
    public void R_cl2_2d_TheGapBetweenTheTwoRoutesIsTheMidpointRuleInClosedForm(string fixtureName)
    {
        var (problem, mesh, ports, fHz) = Fixture(fixtureName);
        var loss = PlanarConductorLoss.For(problem);
        var sol  = new PlanarSolveContext(mesh, ports,
                       PlanarFillSettings.Default with { ConductorLoss = loss })
                       .SolveAt(PlanarLineFixtures.Kernel(problem.Slab, fHz), fHz);

        double exact  = new PlanarConductorLossInputs(loss, PlanarGram.Build(mesh), null)
                            .PowerOf(mesh, sol.Currents[0], fHz).SheetW;
        double approx = DirectSheetPowerW(problem, mesh, loss, sol.Currents[0], ports[0].Number, fHz);
        double predicted = MidpointDeficitW(mesh, loss, sol.Currents[0], fHz);

        double observed = exact - approx;
        output.WriteLine($"{fixtureName}: exact {exact:E9} W, midpoint {approx:E9} W");
        output.WriteLine($"  observed gap  {observed:E9} W ({observed / exact:P3} of the term)");
        output.WriteLine($"  Σ Δ|ΔI|²/12L  {predicted:E9} W  → rel {(predicted - observed) / observed:E3}");

        Assert.True(observed > 0);
        Assert.Equal(observed, predicted, 1e-10 * observed);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-cl2-3 — the residual has the right SIGN where the conductor term dominates
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>THE SHARPEST FORM OF THE GATE, and it is not the one the brief named: tanδ = 0 with REAL
    /// metal.</b> Then the conductor term is the only absorber in the model, so the dielectric
    /// residual must come out at zero — and it is the residual of THREE independent routes, ½Re(Y_jj)
    /// from the factorisation, ∫U dΩ plus the pole residues from the spectral kernel, and this
    /// brief's quadratic form. That makes it a check on the conductor term's own MAGNITUDE, not only
    /// on the sign of the arithmetic around it: the term carries 87-99.7% of the accepted power
    /// here, so an error of any appreciable factor in it lands in the residual at that scale.
    ///
    /// <para><b>The failure this names</b> is CL2's one unsafe mistake — adding <c>P_conductor</c>
    /// to the budget without taking it out of the dielectric residual. That leaves a budget which
    /// still sums to <c>P_accepted</c> by construction and therefore still LOOKS right, while
    /// <c>P_dielectric</c> silently carries a negative copy of the conductor term. Here that is a
    /// residual of −87% to −99.7% of the accepted power rather than a fraction of a per cent.</para>
    ///
    /// <para><b>The residual's SIGN is noise at tanδ = 0 and the tolerance is two-sided for that
    /// reason</b> — it is the matrix fill's own accuracy, which RESOLVED.md §ANT-5 records as ~1e-2
    /// relative on 100 µm GaAs. Measured here at 1.7e-4 / 6.8e-5 / 2.4e-4, i.e. comfortably inside
    /// it; the gate is set at the documented floor rather than at what was measured.</para>
    /// </summary>
    [Theory]
    [InlineData("gaas-line-10")]
    [InlineData("gaas-line-30")]
    [InlineData("gaas-patch-60")]
    public void R_cl2_3a_WithALosslessDielectric_TheConductorTermIsTheWholeResidual(string which)
    {
        var (budget, n) = MmicBudget(which, tanD: 0.0);
        double residual = budget.DielectricAndGroundW / budget.AcceptedW;

        output.WriteLine($"{which}, tanδ = 0, N = {n}");
        output.WriteLine(budget.Caption);
        output.WriteLine($"  dielectric residual = {residual:E3} of accepted; conductor carries " +
                         $"{budget.ConductorW / budget.AcceptedW:P2}");

        Assert.True(budget.ConductorW > 0, "real gold at these frequencies is not a perfect conductor");
        Assert.True(budget.ConductorW / budget.AcceptedW > 0.05,
            "this gate is only sharp while the conductor term is a real share of the budget");
        Assert.True(Math.Abs(residual) < 1e-2,
            $"with tanδ = 0 the metal is the only absorber, so the dielectric residual must vanish; " +
            $"it is {residual:E3} of the accepted power against a conductor term of " +
            $"{budget.ConductorW / budget.AcceptedW:P2}. A residual near MINUS the conductor share " +
            $"is the milestone-2 failure: the term added beside the residual rather than taken out " +
            $"of it, which still sums to the accepted power and still looks right.");
    }

    /// <summary>
    /// <b>The MMIC starter with its dielectric loss switched back on: the conductor term is the
    /// LARGER of the two absorbers and the residual stays positive.</b>
    ///
    /// <para><b>The brief's own premise for this gate — "the conductor term dominates the dielectric
    /// term by ~30×" — is not what a power budget measures, and the ratio is REPORTED rather than
    /// asserted at a target.</b> The series overview's 30× is a ratio of per-unit-length
    /// ATTENUATIONS on a matched line, which weighs the two mechanisms as ∫R_s|J|² against
    /// ∫G|V|² with |V| and |I| locked together by Z₀. This budget drives one port at 1 V into a
    /// structure whose far port is a transparent zero-volt gap, so the voltage and current profiles
    /// are a standing wave and the same two mechanisms are weighted quite differently. Measured
    /// across the MMIC fixtures: <b>2.53× at best</b>, not 30×. The gate that does not depend on
    /// that ratio is <see cref="R_cl2_3a_WithALosslessDielectric_TheConductorTermIsTheWholeResidual"/>.</para>
    /// </summary>
    [Fact]
    public void R_cl2_3b_OnTheMmicStarter_TheConductorTermIsTheLargerAbsorber()
    {
        // tanδ = 6e-4 is the series overview's own MMIC starter figure; GroundedSlab.GaAsStarter
        // carries 2e-3, and the difference moves this ratio by 3.3×, so it is spelled out here.
        var (budget, n) = MmicBudget("gaas-line-30", tanD: 6e-4);
        double ratio = budget.ConductorW / budget.DielectricAndGroundW;

        output.WriteLine($"MMIC starter line, tanδ = 6e-4, N = {n}");
        output.WriteLine(budget.Caption);
        output.WriteLine($"  conductor / dielectric = {ratio:F2}× " +
                         $"(the overview's 30× is an α ratio on a MATCHED line, not this)");

        Assert.True(budget.DielectricAndGroundW > 0,
            $"the dielectric residual came out NEGATIVE ({SurfaceMesher.Eng(budget.DielectricAndGroundW)}W " +
            $"against a conductor term of {SurfaceMesher.Eng(budget.ConductorW)}W) — the " +
            $"milestone-2 failure named in R_cl2_3a");
        Assert.True(ratio > 1.0,
            $"on a low-tanδ MMIC substrate the metal is the larger absorber; it read {ratio:F2}×");

        // The budget closes as an identity whichever way round it is, so this asserts the
        // bookkeeping, not the physics — it is what says no term was dropped on the way through.
        double sum = budget.RadiatedW + budget.SurfaceWaveW + budget.DielectricAndGroundW + budget.ConductorW;
        Assert.Equal(budget.AcceptedW, sum, 1e-12 * budget.AcceptedW);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-cl2-4 — the efficiency moved the predicted way
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The same patch with and without the fill's conductor-loss term.</b> Radiation efficiency
    /// must be LOWER with it, by roughly the conductor share — the accepted power has one more place
    /// to go. Both numbers are reported; only the direction is asserted, because the size of the
    /// drop is the physics this brief is measuring rather than a target it is hitting.
    ///
    /// <para><b>The fixture's quasi-static crossover is reported beside the numbers</b> (brief §0).
    /// Nothing in the budget reads the port calibration — <c>P_conductor</c> is a quadratic form in
    /// the solved current on the DUT's own mesh and <c>P_accepted</c> is ½Re(Y_jj) of the RAW
    /// self-admittance — but <c>P_accepted</c> does come through the port, so a later reader needs
    /// to be able to tell the two effects apart.</para>
    /// </summary>
    [Theory]
    [InlineData("fr4")]
    [InlineData("gaas")]
    public void R_cl2_4_TheRadiationEfficiencyFalls_WhenTheMetalStopsBeingPerfect(string starter)
    {
        bool fr4 = starter == "fr4";
        var slab  = fr4 ? GroundedSlab.Fr4Starter : GroundedSlab.GaAsStarter;
        double sg = fr4 ? CopperSigma : GoldSigma;
        double t  = fr4 ? 35e-6 : 3e-6;
        double f  = fr4 ? 2.4e9 : 60e9;

        var (with,    n) = PatchBudget(slab, sg,                      t, f);
        var (without, _) = PatchBudget(slab, double.PositiveInfinity,  t, f);

        // brief §0's crossover: min(0.03c/(h√(εᵣ−1)), 0.02c/h). Reported, not used.
        double h = slab.HeightM, epsR = slab.Material.EpsR;
        double crossover = Math.Min(0.03 * EmConstants.C0 / (h * Math.Sqrt(epsR - 1.0)),
                                    0.02 * EmConstants.C0 / h);

        output.WriteLine($"{starter} patch at {f / 1e9:F1} GHz, N = {n}, σ = {sg:E3} S/m, " +
                         $"t = {t * 1e6:F0} µm");
        output.WriteLine($"  quasi-static port-calibration crossover for this stack: " +
                         $"{crossover / 1e9:F3} GHz — this point is " +
                         $"{(f < crossover ? "BELOW" : "ABOVE")} it");
        output.WriteLine($"  PEC metal    : η_rad = {without.RadiationEfficiency:P3}, " +
                         $"conductor {SurfaceMesher.Eng(without.ConductorW)}W");
        output.WriteLine($"  with Z_s     : η_rad = {with.RadiationEfficiency:P3}, " +
                         $"conductor {SurfaceMesher.Eng(with.ConductorW)}W " +
                         $"({100.0 * with.ConductorW / with.AcceptedW:F2} % of accepted)");
        output.WriteLine($"  Δη_rad = {100.0 * (with.RadiationEfficiency - without.RadiationEfficiency):F3} pp");
        output.WriteLine(with.Caption);

        Assert.True(with.RadiationEfficiency < without.RadiationEfficiency,
            $"loading the operator with a surface impedance can only take power out of the " +
            $"radiated share: η went {without.RadiationEfficiency:P3} → {with.RadiationEfficiency:P3}");
        Assert.Equal(0.0, without.ConductorW);
        Assert.False(without.ConductorModelled);

        // The two notes must describe the state each run is actually in — the milestone-3 rule.
        Assert.Contains("NOT MODELLED", without.Caption);
        Assert.DoesNotContain("NOT MODELLED", with.Caption);
        Assert.Contains("DID model conductor loss", with.ConductorBoundClause);
        Assert.Contains("PERFECT CONDUCTOR", without.ConductorBoundClause);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Support
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private static PlanarProblem Problem(GroundedSlab slab, double fHz, double sigmaSm,
                                         double thicknessM, params PlanarPolygon[] polys) =>
        new([new PlanarConductorLayer("Metal", polys, sigmaSm, thicknessM)], slab, fHz);

    /// <summary>A half-guided-wavelength patch on the stated stackup, edge-fed at x = 0 — ANT-5's
    /// own fixture shape, with the metal's σ and t made an argument.</summary>
    private (PlanarPowerBudget Budget, int N) PatchBudget(GroundedSlab slab, double sigmaSm,
                                                          double thicknessM, double fHz)
    {
        double lambda0 = EmConstants.C0 / fHz;
        double L = 0.49 * lambda0 / Math.Sqrt(slab.Material.EpsR), W = 1.25 * L;

        var problem = Problem(slab, fHz, sigmaSm, thicknessM,
                              PlanarLineFixtures.Rect(0, -0.5 * W, L, 0.5 * W));
        var mesh = SurfaceMesher.Mesh(problem,
            new PlanarMeshSettings(Auto: false, CellsPerWavelength: 20, EdgeMesh: false)).Mesh;
        var ports = PlanarPorts.ResolveAll(mesh,
            [new PlanarPort(1, new EmPoint(0, 0), PlanarPortSide.MinX, 50.0)]);

        var loss = PlanarConductorLoss.For(problem);
        var sol  = new PlanarSolveContext(mesh, ports,
                       PlanarFillSettings.Default with { ConductorLoss = loss })
                       .SolveAt(PlanarLineFixtures.Kernel(slab, fHz), fHz);

        var pattern = PlanarFarField.Compute(problem, mesh, sol.Currents[0], 1, fHz,
                                             PlanarFarFieldGrid.Hemisphere(1, 2));

        // A PEC stackup is still handed the inputs — that is the state where the term is live and
        // the metal is perfect, which is R-cl2-1's case. R-cl2-4 wants the OTHER zero, so it asks
        // for the term to be absent entirely, which is what `ConductorModelled` distinguishes.
        var inputs = PlanarSurfaceImpedanceIsPerfect(sigmaSm, thicknessM)
                         ? null
                         : new PlanarConductorLossInputs(loss, PlanarGram.Build(mesh), null);

        return (PlanarPowerBudget.For(problem, mesh, sol.Currents[0], pattern, sol.Y[0, 0], inputs),
                mesh.Bases.Count);
    }

    /// <summary>
    /// One of the three MMIC fixtures at a stated dielectric loss tangent: 3 µm gold on 100 µm GaAs,
    /// either a re-bisected 50 Ω line (the series overview's own 70.72 µm width) or a half-guided-
    /// wavelength patch. The loss tangent is an ARGUMENT because the whole point of R-cl2-3a is to
    /// take it to zero.
    /// </summary>
    private (PlanarPowerBudget Budget, int N) MmicBudget(string which, double tanD)
    {
        double f = which.EndsWith("-10") ? 10e9 : which.EndsWith("-30") ? 30e9 : 60e9;
        var slab = new GroundedSlab(GroundedSlab.GaAsStarter.HeightM,
                                    new EmMaterial(GroundedSlab.GaAsStarter.Material.EpsR, tanD));

        PlanarPolygon poly;
        bool patch = which.Contains("patch");
        if (patch)
        {
            double lambda0 = EmConstants.C0 / f;
            double L = 0.49 * lambda0 / Math.Sqrt(slab.Material.EpsR);
            poly = PlanarLineFixtures.Rect(0, -0.625 * L, L, 0.625 * L);
        }
        else poly = PlanarLineFixtures.Rect(0, -35.36e-6, 1200e-6, 35.36e-6);

        var problem = Problem(slab, f, GoldSigma, 3e-6, poly);
        var mesh = SurfaceMesher.Mesh(problem,
            new PlanarMeshSettings(Auto: false, CellsPerWavelength: 20, EdgeMesh: false)).Mesh;
        var ports = patch
            ? PlanarPorts.ResolveAll(mesh,
                  [new PlanarPort(1, new EmPoint(0, 0), PlanarPortSide.MinX, 50.0)])
            : PlanarPorts.ResolveAll(mesh, PlanarLineFixtures.EndPorts(problem));

        var loss = PlanarConductorLoss.For(problem);
        var sol  = new PlanarSolveContext(mesh, ports,
                       PlanarFillSettings.Default with { ConductorLoss = loss })
                       .SolveAt(PlanarLineFixtures.Kernel(slab, f), f);
        var pattern = PlanarFarField.Compute(problem, mesh, sol.Currents[0], ports[0].Number, f,
                                             PlanarFarFieldGrid.Hemisphere(1, 2));

        return (PlanarPowerBudget.For(problem, mesh, sol.Currents[0], pattern, sol.Y[0, 0],
                    new PlanarConductorLossInputs(loss, PlanarGram.Build(mesh), null)),
                mesh.Bases.Count);
    }

    private static bool PlanarSurfaceImpedanceIsPerfect(double sigmaSm, double thicknessM)
        => !(sigmaSm > 0) || double.IsPositiveInfinity(sigmaSm) || !(thicknessM > 0);

    private static (PlanarProblem, PlanarMesh, IReadOnlyList<PlanarPortResolution>, double)
        Fixture(string name)
    {
        switch (name)
        {
            case "fr4-line":
            {
                var p = Problem(GroundedSlab.Fr4Starter, 10e9, CopperSigma, 35e-6,
                                PlanarLineFixtures.Rect(0, -1.45e-3, 8e-3, 1.45e-3));
                var (m, q) = PlanarLineFixtures.MeshAndPorts(p, PlanarLineFixtures.Coarse);
                return (p, m, q, 10e9);
            }
            case "gaas-line":
            {
                var p = Problem(GroundedSlab.GaAsStarter, 30e9, GoldSigma, 3e-6,
                                PlanarLineFixtures.Rect(0, -36e-6, 600e-6, 36e-6));
                var (m, q) = PlanarLineFixtures.MeshAndPorts(p, PlanarLineFixtures.Coarse);
                return (p, m, q, 30e9);
            }
            case "fr4-patch":
            {
                const double f = 2.4e9;
                var slab = GroundedSlab.Fr4Starter;
                double lambda0 = EmConstants.C0 / f;
                double L = 0.49 * lambda0 / Math.Sqrt(slab.Material.EpsR), W = 1.25 * L;
                var p = Problem(slab, f, CopperSigma, 35e-6,
                                PlanarLineFixtures.Rect(0, -0.5 * W, L, 0.5 * W));
                var m = SurfaceMesher.Mesh(p,
                    new PlanarMeshSettings(Auto: false, CellsPerWavelength: 20, EdgeMesh: false)).Mesh;
                var q = PlanarPorts.ResolveAll(m,
                    [new PlanarPort(1, new EmPoint(0, 0), PlanarPortSide.MinX, 50.0)]);
                return (p, m, q, f);
            }
            default: throw new ArgumentOutOfRangeException(nameof(name), name, "unknown fixture");
        }
    }

    /// <summary>Two gold levels joined by a via — CL1's own via fixture, with ports so it solves.</summary>
    private static (PlanarProblem, PlanarMesh, PlanarLevels, IReadOnlyList<PlanarPortResolution>,
                    double) ViaFixture()
    {
        const double fHz = 30e9;
        var lower = new PlanarConductorLayer("M1",
            [PlanarLineFixtures.Rect(0, -50e-6, 400e-6, 50e-6)], GoldSigma, 1e-6, 0.0);
        var upper = new PlanarConductorLayer("M2",
            [PlanarLineFixtures.Rect(300e-6, -50e-6, 700e-6, 50e-6)], GoldSigma, 3e-6, 3e-6);
        var via = new PlanarVia(0, 1, [PlanarLineFixtures.Rect(330e-6, -30e-6, 390e-6, 30e-6)],
                                GoldSigma);
        var problem = new PlanarProblem([lower, upper], GroundedSlab.GaAsStarter, fHz,
                                        Vias: [via]);

        var mesh = SurfaceMesher.Mesh(problem, PlanarLineFixtures.Coarse).Mesh;
        var ports = PlanarPorts.ResolveAll(mesh,
        [
            new PlanarPort(1, new EmPoint(0,      0), PlanarPortSide.MinX, 50.0, 0),
            new PlanarPort(2, new EmPoint(700e-6, 0), PlanarPortSide.MaxX, 50.0, 1),
        ]);
        return (problem, mesh, PlanarLevels.From(problem), ports, fHz);
    }

    /// <summary>
    /// <b>The closed-form difference between the exact integral and the midpoint rule</b>, summed
    /// over the mesh: <c>½ Σ_cells Re(Z_s)·Δ·|I_m − I_n|²/(12·L)</c> per flow direction, with the
    /// two covering rooftops' coefficients (a missing one counting as zero, which is the outermost
    /// cell and is where the term is largest). Derived in
    /// <see cref="R_cl2_2d_TheGapBetweenTheTwoRoutesIsTheMidpointRuleInClosedForm"/>'s own summary.
    ///
    /// <para>Manhattan cells only, which is every cell of every fixture here — on a CUT cell the
    /// map's mean transverse extent is not the rectangle's and the closed form above is not the
    /// right one.</para>
    /// </summary>
    private static double MidpointDeficitW(PlanarMesh mesh, PlanarConductorLoss loss,
                                           Vec<Complex> currents, double fHz)
    {
        var zs = loss.SheetTable(mesh, 2.0 * Math.PI * fHz);
        var jx = new Complex[mesh.Cells.Count];
        var jy = new Complex[mesh.Cells.Count];
        var nx = new int[mesh.Cells.Count];
        var ny = new int[mesh.Cells.Count];

        // |I_m − I_n|² needs the two coefficients SEPARATELY, so this accumulates the difference by
        // flipping the sign on the second visit — the rooftops covering one cell come in an order
        // the mesh fixes, and only their difference is wanted.
        for (int b = 0; b < mesh.Bases.Count; b++)
        {
            var bs = mesh.Bases[b];
            if (bs.Direction == PlanarBasisDirection.Z) continue;
            foreach (int c in new[] { bs.CellA, bs.CellB })
            {
                if ((uint)c >= (uint)mesh.Cells.Count) continue;
                if (bs.Direction == PlanarBasisDirection.X)
                { jx[c] += nx[c] == 0 ? currents[b] : -currents[b]; nx[c]++; }
                else
                { jy[c] += ny[c] == 0 ? currents[b] : -currents[b]; ny[c]++; }
            }
        }

        double sum = 0;
        for (int c = 0; c < mesh.Cells.Count; c++)
        {
            var cell = mesh.Cells[c];
            if (cell.Region is not null) continue;
            double rs = zs[Math.Clamp(cell.LayerIndex, 0, zs.Length - 1)].Real;
            if (rs == 0 || !(cell.Width > 0) || !(cell.Height > 0)) continue;
            sum += rs * (cell.Width  / (12.0 * cell.Height) * (jx[c] * Complex.Conjugate(jx[c])).Real
                       + cell.Height / (12.0 * cell.Width)  * (jy[c] * Complex.Conjugate(jy[c])).Real);
        }
        return 0.5 * sum;
    }

    /// <summary>
    /// <b>The test-only reference route: ½∫Re(Z_s)|J|²dS as a direct sum over the CELL map.</b> It
    /// shares nothing with the shipped quadratic form but the mesh and the solved coefficients — it
    /// reads |J| out of <see cref="PlanarCurrentDensity"/>'s own reduction, which is a different
    /// statement of the current (a per-cell density in A/m, formed from the rooftop ramp's value at
    /// the cell centre) from the Gram's integral of the basis against itself.
    /// </summary>
    private static double DirectSheetPowerW(PlanarProblem problem, PlanarMesh mesh,
                                            PlanarConductorLoss loss, Vec<Complex> currents,
                                            int portNumber, double fHz)
    {
        var map = PlanarCurrentDensity.Compute(mesh, currents, portNumber, fHz);
        var zs  = loss.SheetTable(mesh, 2.0 * Math.PI * fHz);

        double p = 0;
        for (int c = 0; c < mesh.Cells.Count; c++)
        {
            var cell = mesh.Cells[c];
            double rs = zs[Math.Clamp(cell.LayerIndex, 0, zs.Length - 1)].Real;
            p += rs * map.Magnitude[c] * map.Magnitude[c] * cell.Area;
        }
        return 0.5 * p;
    }
}
