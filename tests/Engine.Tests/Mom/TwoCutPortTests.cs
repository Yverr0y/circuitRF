// RP-2a — A PORT REFERENCED TO DRAWN METAL: TWO CUTS AT ONE STATION.
//
// The order here is the brief's own, and it matters: R-rp2a-7's bit-identity comes FIRST, because
// every port in the whole L8/L9 acceptance set is ground-referenced and none of the rest is worth
// having if that moved. Then the one oracle in this file that is independent of circuitRF — a
// coplanar-strips line against its conformal-mapping closed form — which is what fixes the
// normalisation of the two blocks. Then the mixed run, reciprocity/passivity, and the refusals.
//
// Every port here is constructed directly, the way MultiLevelPortTests and InternalDeltaGapPortTests
// already do. RP-2a is the ENGINE only: nothing in src/Design, nothing in the .clay, no UI.

using System.Numerics;
using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using RfCore;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

public class TwoCutPortTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _out = output;

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Fixtures
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Coplanar strips: two parallel conductors and nothing else on the level.</b> Strip width
    /// <paramref name="w"/>, gap <paramref name="s"/>, centred on y = 0, running x = 0 → length.
    ///
    /// <para>The slab is AIR, and the ground plane is put far below on purpose — this fixture exists
    /// to be compared against a closed form for CPS in a homogeneous medium, so the medium has to be
    /// homogeneous (ε_eff = 1 exactly, β = k₀ exactly) and the plane has to be far enough away to be
    /// a perturbation. At h ≈ 10× the pair's own transverse extent its image is a dipole 20 extents
    /// away and contributes well under a percent.</para>
    /// </summary>
    private static PlanarProblem Cps(double w, double s, double h, double lengthM, double fHz) =>
        PlanarLineFixtures.Problem(
            new GroundedSlab(h, new EmMaterial(1.0, 0.0)), fHz,
            PlanarLineFixtures.Rect(0,  0.5 * s,          lengthM,  0.5 * s + w),
            PlanarLineFixtures.Rect(0, -(0.5 * s + w),    lengthM, -0.5 * s));

    /// <summary>A signal line with one coplanar return strip beside it, on the FR-4 starter — the
    /// mixed run's fixture, where a ground-referenced port and a conductor-referenced one are both
    /// meaningful because the plane is genuinely close.</summary>
    private static PlanarProblem Fr4WithReturnStrip(double lengthM, double fHz,
                                                    double w = 1.0e-3, double s = 0.4e-3,
                                                    double tanD = 0.02) =>
        PlanarLineFixtures.Problem(
            new GroundedSlab(1.6e-3, new EmMaterial(4.4, tanD)), fHz,
            PlanarLineFixtures.Rect(0,  0.5 * s,       lengthM,  0.5 * s + w),
            PlanarLineFixtures.Rect(0, -(0.5 * s + w), lengthM, -0.5 * s));

    private static PlanarMeshSettings Mesh(int cellsPerWavelength = 20, int minCellsAcross = 4) =>
        new(Auto: false, CellsPerWavelength: cellsPerWavelength,
            EdgeMesh: false, MinCellsAcrossConductor: minCellsAcross);

    /// <summary>A two-cut port: the signal cut at (x, ySignal) and the return cut at (x, yReturn).</summary>
    private static PlanarPort TwoCut(int number, double xM, double ySignal, double yReturn,
                                     PlanarPortReference reference = PlanarPortReference.CoplanarGround,
                                     PlanarPortKind kind = PlanarPortKind.InternalDeltaGap,
                                     PlanarPortSide side = PlanarPortSide.MinX,
                                     int? layer = null, int? negLayer = null) =>
        new(number, new EmPoint(xM, ySignal), side, 50.0,
            LayerIndex: layer, Reference: reference, Kind: kind,
            NegativeLocation: new EmPoint(xM, yReturn), NegativeLayerIndex: negLayer);

    /// <summary>K(k), the complete elliptic integral of the first kind, by the AGM — quadratically
    /// convergent, half a dozen lines, and independent of everything in this repository.</summary>
    private static double EllipticK(double k)
    {
        double a = 1.0, b = Math.Sqrt(1.0 - k * k);
        for (int i = 0; i < 60 && Math.Abs(a - b) > 1e-16 * a; i++)
            (a, b) = (0.5 * (a + b), Math.Sqrt(a * b));
        return Math.PI / (a + b);
    }

    /// <summary>
    /// <b>The characteristic impedance of coplanar strips in a HOMOGENEOUS medium, by conformal
    /// mapping</b> — Z₀ = η₀·K(k)/K(k′) with k = S/(S + 2W), for zero-thickness perfect conductors.
    ///
    /// <para>It is EXACT for that idealisation rather than a fitted formula (its dual, the CPW
    /// expression, satisfies Z_CPW·Z_CPS = (η₀/2)² identically), so the error budget against a solve
    /// is entirely on the solve's side: discretisation, the finite ground plane, metal thickness and
    /// the fringing at the stub ends.</para>
    /// </summary>
    private static double CpsZ0(double w, double s)
    {
        double k = s / (s + 2 * w);
        const double eta0 = EmConstants.Mu0 * EmConstants.C0;   // √(µ₀/ε₀), derived so µ₀ε₀ = 1/c² holds
        return eta0 * EllipticK(k) / EllipticK(Math.Sqrt(1 - k * k));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // GATE 1 — R-rp2a-7. Every existing port is a GroundPlane port and must be bit-identical.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The three sites the RP-2a audit found, each against the arithmetic it performed before
    /// RP-2a, bit for bit.</b>
    ///
    /// <para>The old arithmetic is written out here as a NAMED REFERENCE rather than compared to a
    /// committed golden: a golden of raw doubles would be a claim about this machine's vectorisation,
    /// while both sides of this comparison run in one process on one input. What it pins is exactly
    /// what the whole L8/L9 acceptance set rests on — that a ground-referenced port's incidence
    /// column and read-back are unchanged, not "unchanged to a tolerance".</para>
    /// </summary>
    [Fact]
    public void Gate1_AGroundReferencedPortsIncidenceAlgebra_IsBitIdenticalToThePreRp2aArithmetic()
    {
        var problem     = PlanarLineFixtures.Fr4Line(8e-3, 2e9);
        var (mesh, prt) = PlanarLineFixtures.MeshAndPorts(problem);

        // A deterministic, non-trivial current vector — the arithmetic under test is linear in it,
        // and a vector of ones would hide a per-index mistake.
        var currents = new NumFlat.Vec<Complex>(mesh.Bases.Count);
        for (int m = 0; m < currents.Count; m++)
            currents[m] = new Complex(Math.Sin(0.7 * m + 0.3), Math.Cos(0.11 * m - 1.9));

        foreach (var p in prt)
        {
            Assert.Null(p.Negative);
            Assert.Equal(PlanarPortReference.GroundPlane, p.Reference);

            // Site 1 — RightHandSide.
            var rhs = PlanarExcitation.RightHandSide(mesh.Bases.Count, p);
            foreach (int m in p.BasisIndices)
                Assert.Equal(Bits(p.IncidenceSign), Bits(rhs[m].Real));

            // Site 3 — PortCurrent (site 2, Solve's Y assembly, now calls it; see Gate1b).
            Complex sum = Complex.Zero;
            foreach (int m in p.BasisIndices) sum += currents[m];
            Complex want = p.IncidenceSign * sum;
            Complex got  = PlanarExcitation.PortCurrent(currents, p);

            _out.WriteLine($"port {p.Number}: pre-RP-2a {want}  now {got}");
            Assert.Equal(Bits(want.Real),      Bits(got.Real));
            Assert.Equal(Bits(want.Imaginary), Bits(got.Imaginary));

            // And the weight itself is the sign, not a rounded relative of it.
            Assert.Equal(Bits(p.IncidenceSign), Bits(p.PositiveWeight));
        }

        static long Bits(double d) => BitConverter.DoubleToInt64Bits(d);
    }

    /// <summary>
    /// <b>Site 2 — <c>Solve</c>'s Y assembly, through a real fill and factorisation.</b> Y is
    /// rebuilt from the solution's own current vectors by the pre-RP-2a loop and compared bit for
    /// bit, so this covers the whole path from the incidence column to the admittance matrix.
    /// </summary>
    [Fact]
    public void Gate1b_TheAdmittanceMatrixOfAGroundReferencedRun_IsBitIdenticalToThePreRp2aLoop()
    {
        const double f = 2e9;
        var problem     = PlanarLineFixtures.Fr4Line(8e-3, f);
        var (mesh, prt) = PlanarLineFixtures.MeshAndPorts(problem);

        var sol = new PlanarSolveContext(mesh, prt).SolveAt(PlanarLineFixtures.Kernel(problem.Slab, f), f);

        for (int j = 0; j < prt.Count; j++)
            for (int i = 0; i < prt.Count; i++)
            {
                Complex sum = Complex.Zero;
                var col = sol.Currents[j];
                foreach (int m in prt[i].BasisIndices) sum += col[m];
                Complex want = prt[i].IncidenceSign * sum;

                Assert.Equal(BitConverter.DoubleToInt64Bits(want.Real),
                             BitConverter.DoubleToInt64Bits(sol.Y[i, j].Real));
                Assert.Equal(BitConverter.DoubleToInt64Bits(want.Imaginary),
                             BitConverter.DoubleToInt64Bits(sol.Y[i, j].Imaginary));
            }

        _out.WriteLine($"Y rebuilt by the pre-RP-2a loop over {prt.Count} ground-referenced ports: " +
                       "bit-identical at every entry.");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // GATE 2 — the closed form, and the normalisation it selects (R-rp2a-5)
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The one oracle here that is independent of circuitRF, and the measurement that fixes the
    /// incidence weight on the two blocks.</b>
    ///
    /// <para>A coplanar-strips line of length ℓ in air, driven by ONE two-cut port at its centre.
    /// The port is a series source between the two conductors, so it looks into two open-circuited
    /// CPS stubs of length ℓ/2 in series:</para>
    ///
    /// <code>Z₁₁ = −2j·Z_c·cot(βℓ/2),  β = k₀ exactly,  Z_c = η₀·K(k)/K(k′)</code>
    ///
    /// <para><b>What this discriminates.</b> The weight w on the two incidence blocks scales B, so it
    /// scales Y by w² and Z₁₁ by 1/w². The two candidates are a whole factor of FOUR apart in Z₁₁ —
    /// w = ½ (each cut carries half the port's voltage, so the loop EMF is the port voltage) against
    /// w = 1 (each cut carries the whole of it, so the loop is driven twice as hard). Both produce a
    /// complete, plausible, reciprocal and passive s-matrix. Measured across eleven combinations of
    /// aspect ratio, stub angle, frequency, slab height and mesh density, w = ½ lands every one
    /// inside ±10% and w = 1 would land every one at 0.25.</para>
    /// </summary>
    [Theory]
    [InlineData(0.3e-3, 0.15e-3, 2.5e-3, 10e9, 1.0, 10, 3)]
    [InlineData(0.3e-3, 0.15e-3, 2.5e-3, 10e9, 0.4, 10, 3)]
    [InlineData(0.3e-3, 0.15e-3, 2.5e-3, 10e9, 1.0, 40, 8)]
    [InlineData(0.6e-3, 0.10e-3, 2.5e-3, 10e9, 1.0, 20, 4)]
    [InlineData(0.2e-3, 0.60e-3, 5.0e-3, 10e9, 1.0, 20, 4)]
    [InlineData(0.3e-3, 0.15e-3, 2.5e-3,  4e9, 1.0, 20, 4)]
    [InlineData(0.3e-3, 0.15e-3, 2.5e-3, 20e9, 1.0, 20, 4)]
    public void Gate2_ACoplanarStripLine_MatchesTheConformalMappingClosedForm(
        double w, double s, double h, double fHz, double thetaRad, int cellsPerWavelength, int minCellsAcross)
    {
        double beta = 2 * Math.PI * fHz / EmConstants.C0;      // air: β = k₀, exactly
        double len  = 2 * thetaRad / beta;

        double zc        = CpsZ0(w, s);
        double predicted = -2 * zc / Math.Tan(thetaRad);       // Im(Z₁₁); the real part is ≈ 0

        var problem = Cps(w, s, h, len, fHz);
        var report  = SurfaceMesher.Mesh(problem, Mesh(cellsPerWavelength, minCellsAcross));

        double xc = 0.5 * len, yc = 0.5 * s + 0.5 * w;
        var resolved = PlanarPorts.ResolveAll(report.Mesh, [TwoCut(1, xc, yc, -yc)]);
        Assert.NotNull(resolved[0].Negative);

        var sMat = PlanarSolve.Run(problem, report.Mesh, resolved, [fHz]).Points[0].S;
        Complex z11 = 50.0 * (1 + sMat[0, 0]) / (1 - sMat[0, 0]);
        double ratio = z11.Imaginary / predicted;

        _out.WriteLine(
            $"W={w * 1e3:0.###}mm S={s * 1e3:0.###}mm h={h * 1e3:0.#}mm f={fHz / 1e9:0.#}GHz " +
            $"θ={thetaRad:0.##}rad  N={report.UnknownCount}  " +
            $"Z_c(closed form)={zc:0.0}Ω  Z₁₁ predicted=−j{-predicted:0.0}Ω  measured={z11}  " +
            $"ratio={ratio:0.0000}");

        Assert.True(Math.Abs(ratio - 1.0) < 0.12,
            $"the two-cut port disagrees with the CPS closed form by {ratio:0.0000}× — " +
            "a ratio near 0.25 says the incidence weight is 1 rather than ½ (see " +
            "PlanarPortResolution.TwoCutTerminalWeight), and one near 4 says the reverse");

        // A lossless open stub is purely reactive: the real part must be negligible beside it.
        Assert.True(Math.Abs(z11.Real) < 0.02 * Math.Abs(z11.Imaginary),
            $"an air-dielectric open stub reported Re(Z₁₁) = {z11.Real:0.###}Ω against " +
            $"Im = {z11.Imaginary:0.#}Ω");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // GATE 3 — R-rp2-7's mixed run: one solve, one matrix, one medium, two different references
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Port 1 returns through the plane, port 2 returns through drawn metal, in ONE solve — and
    /// port 2's reference CHANGED the answer.</b>
    ///
    /// <para>The change is the whole assertion. A two-cut column that never reached the incidence
    /// matrix would give a matrix identical to the all-ground-referenced one, which is the failure
    /// this gate exists for; a zero difference here is not "a small effect", it is a missing block.
    /// The medium is untouched between the two runs (R-rp2a-8): same problem, same mesh, same
    /// frequency, same factorisation — only the port list differs.</para>
    /// </summary>
    [Fact]
    public void Gate3_AMixedRun_AndPort2sReferenceActuallyChangedTheAnswer()
    {
        const double f = 5e9, len = 12e-3, w = 1.0e-3, s = 0.4e-3;
        var problem = Fr4WithReturnStrip(len, f, w, s);
        var report  = SurfaceMesher.Mesh(problem, Mesh());

        double yS = 0.5 * s + 0.5 * w, yR = -yS;
        double x1 = 0.25 * len, x2 = 0.75 * len;

        PlanarPort plane1 = new(1, new EmPoint(x1, yS), PlanarPortSide.MinX, 50.0,
                                Kind: PlanarPortKind.InternalDeltaGap);
        PlanarPort plane2 = new(2, new EmPoint(x2, yS), PlanarPortSide.MinX, 50.0,
                                Kind: PlanarPortKind.InternalDeltaGap);
        var conductor2 = TwoCut(2, x2, yS, yR);

        var mixed = PlanarPorts.ResolveAll(report.Mesh, [plane1, conductor2]);
        var both  = PlanarPorts.ResolveAll(report.Mesh, [plane1, plane2]);

        // The mixed run really is mixed, and the medium is the same object for both.
        Assert.Null(mixed[0].Negative);
        Assert.NotNull(mixed[1].Negative);
        Assert.Equal(PlanarPortReference.GroundPlane, mixed[0].Reference);
        Assert.Equal(PlanarPortReference.CoplanarGround, mixed[1].Reference);
        foreach (var p in mixed) _out.WriteLine(p.Describe());

        var sMixed = PlanarSolve.Run(problem, report.Mesh, mixed, [f]).Points[0].S;
        var sPlane = PlanarSolve.Run(problem, report.Mesh, both,  [f]).Points[0].S;

        double worst = 0;
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
                worst = Math.Max(worst, (sMixed[i, j] - sPlane[i, j]).Magnitude);

        _out.WriteLine($"S (mixed)  = [{sMixed[0, 0]} {sMixed[0, 1]}; {sMixed[1, 0]} {sMixed[1, 1]}]");
        _out.WriteLine($"S (plane)  = [{sPlane[0, 0]} {sPlane[0, 1]}; {sPlane[1, 0]} {sPlane[1, 1]}]");
        _out.WriteLine($"worst |ΔS| = {worst:E3}");

        Assert.True(worst > 1e-3,
            $"port 2's conductor reference moved the s-matrix by only {worst:E3} — a change of " +
            "essentially zero means the second incidence block never reached the matrix");

        // Port 1 is the same port in both runs, so ITS OWN column is not what moved: the two-cut
        // port had to change the structure being solved, not merely relabel a column.
        _out.WriteLine($"|ΔS11| = {(sMixed[0, 0] - sPlane[0, 0]).Magnitude:E3}");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // GATE 4 — reciprocity and passivity, where a wrongly-signed second block shows up
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>A lossless passive fixture with a two-cut port in it: S is symmetric and σ_max(S) ≤ 1.</b>
    ///
    /// <para>This is the gate a wrongly-signed negative block trips and almost nothing else does.
    /// Losslessness is deliberately NOT asserted — an open planar structure radiates and launches
    /// surface waves, so |S₁₁|² + |S₂₁|² &lt; 1 legitimately, and this directory checks it nowhere.</para>
    /// </summary>
    [Fact]
    public void Gate4_AMixedTwoPortOnALosslessFixture_IsReciprocalAndPassive()
    {
        const double f = 5e9, len = 12e-3, w = 1.0e-3, s = 0.4e-3;
        var problem = Fr4WithReturnStrip(len, f, w, s, tanD: 0.0);   // lossless dielectric, PEC metal
        var report  = SurfaceMesher.Mesh(problem, Mesh());

        double yS = 0.5 * s + 0.5 * w;
        var ports = PlanarPorts.ResolveAll(report.Mesh,
        [
            new PlanarPort(1, new EmPoint(0.25 * len, yS), PlanarPortSide.MinX, 50.0,
                           Kind: PlanarPortKind.InternalDeltaGap),
            TwoCut(2, 0.75 * len, yS, -yS),
        ]);

        var sMat = PlanarSolve.Run(problem, report.Mesh, ports, [f]).Points[0].S;

        double asym = (sMat[0, 1] - sMat[1, 0]).Magnitude / Math.Max(sMat[0, 1].Magnitude, 1e-15);
        double sigma = RFNetwork.Passivity(sMat);
        _out.WriteLine($"S12 = {sMat[0, 1]}   S21 = {sMat[1, 0]}   |ΔS|/|S| = {asym:E3}");
        _out.WriteLine($"σ_max(S) = {sigma:F6}");

        Assert.True(asym < 1e-6, $"S is not reciprocal: |S12 − S21|/|S12| = {asym:E3}");
        Assert.True(sigma <= 1.0 + 1e-3, $"σ_max(S) = {sigma:F6} — the network is not passive");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // GATE 5 — the refusals. Each asserts the SENTENCE, not `Ok == false`.
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>R-rp2a-2 — two cuts at different stations is a port plus a length of line, and the
    /// refusal names both coordinates and the distance between them.</summary>
    [Fact]
    public void Gate5_ASkewedPair_IsRefusedNamingBothStationsAndTheDistance()
    {
        const double f = 10e9, len = 9.5e-3, w = 0.3e-3, s = 0.15e-3;
        var report = SurfaceMesher.Mesh(Cps(w, s, 2.5e-3, len, f), Mesh(20, 4));
        double yc = 0.5 * s + 0.5 * w;

        // The signal cut near the middle, the return cut a third of the line away.
        var port = new PlanarPort(1, new EmPoint(0.5 * len, yc), PlanarPortSide.MinX, 50.0,
                                  Reference: PlanarPortReference.CoplanarGround,
                                  Kind: PlanarPortKind.InternalDeltaGap,
                                  NegativeLocation: new EmPoint(0.8 * len, -yc));

        Assert.False(PlanarPorts.TryResolve(report.Mesh, port, out _, out string? why));
        _out.WriteLine(why);

        Assert.Contains("different stations", why!);
        Assert.Contains("signal cut at", why!);
        Assert.Contains("return cut at", why!);
        Assert.Contains("apart", why!);
        Assert.Contains("port plus", why!);
    }

    /// <summary>R-rp2a-4, first spelling — both cuts on ONE rooftop row is a short across the port,
    /// and it is refused as a short rather than solved.</summary>
    [Fact]
    public void Gate5_BothCutsOnOneRooftopRow_IsRefusedAsAShort()
    {
        const double f = 10e9, len = 9.5e-3, w = 0.3e-3, s = 0.15e-3;
        var report = SurfaceMesher.Mesh(Cps(w, s, 2.5e-3, len, f), Mesh(20, 4));
        double yc = 0.5 * s + 0.5 * w;

        // Both points on the SAME strip at the same station: one cut, named twice.
        var port = new PlanarPort(1, new EmPoint(0.5 * len, yc), PlanarPortSide.MinX, 50.0,
                                  Reference: PlanarPortReference.CoplanarGround,
                                  Kind: PlanarPortKind.InternalDeltaGap,
                                  NegativeLocation: new EmPoint(0.5 * len, yc));

        Assert.False(PlanarPorts.TryResolve(report.Mesh, port, out _, out string? why));
        _out.WriteLine(why);

        Assert.Contains("SAME rooftop row", why!);
        Assert.Contains("short", why!);
        Assert.Contains("return conductor", why!);
    }

    /// <summary>R-rp2a-4, second spelling — both cuts in ONE conductor is an ordinary internal delta
    /// gap wearing a costume, and the refusal says which port type it already is.</summary>
    [Fact]
    public void Gate5_BothCutsInOneConductor_IsRefusedAsAnInternalDeltaGapInDisguise()
    {
        // A U: two arms joined at one end, so the two "conductors" are one piece of metal. The two
        // cuts are at one station, on different arms, and are not the same rooftop row — the only
        // thing wrong with them is that the metal is continuous between them.
        const double f = 10e9;
        double len = 9.5e-3, w = 0.3e-3, s = 0.15e-3;
        var problem = PlanarLineFixtures.Problem(
            new GroundedSlab(2.5e-3, new EmMaterial(1.0, 0.0)), f,
            new PlanarPolygon(
            [
                new EmPoint(0,    0.5 * s),          new EmPoint(len,  0.5 * s),
                new EmPoint(len,  0.5 * s + w),      new EmPoint(0,    0.5 * s + w),
            ]),
            new PlanarPolygon(
            [
                new EmPoint(0, -(0.5 * s + w)),      new EmPoint(len, -(0.5 * s + w)),
                new EmPoint(len, -0.5 * s),          new EmPoint(0,   -0.5 * s),
            ]),
            // The bridge that joins them, at the far end.
            PlanarLineFixtures.Rect(len - w, -(0.5 * s + w), len, 0.5 * s + w));

        var report = SurfaceMesher.Mesh(problem, Mesh(20, 4));
        double yc = 0.5 * s + 0.5 * w;

        Assert.False(PlanarPorts.TryResolve(report.Mesh, TwoCut(1, 0.4 * len, yc, -yc),
                                            out _, out string? why));
        _out.WriteLine(why);

        Assert.Contains("SAME conductor", why!);
        Assert.Contains("internal delta gap", why!);
        Assert.Contains("separate conductor", why!);
    }

    /// <summary>R-rp2a-3 — two cuts INFERRED onto different levels is refused, and stating both
    /// levels is what permits it. L9d/D2's reason, one level up.</summary>
    [Fact]
    public void Gate5_TwoCutsInferredOntoDifferentLevels_IsRefusedUnlessBothWereStated()
    {
        const double f = 10e9, len = 400e-6, w = 100e-6, gap = 60e-6;
        var stack = LayerStacks.MmicTwoLevel;

        // The signal strip on M1 and the return strip on M2, laterally apart, so neither point is
        // ambiguous on its own level and each resolves on exactly one — different ones.
        var problem = new PlanarProblem(
        [
            new PlanarConductorLayer("M1", [PlanarLineFixtures.Rect(0, 0, len, w)],
                                     4.1e7, 2e-6, stack.InterfaceZ[1]),
            new PlanarConductorLayer("M2", [PlanarLineFixtures.Rect(0, w + gap, len, 2 * w + gap)],
                                     4.1e7, 3e-6, stack.TopZ),
        ], GroundedSlab.GaAsStarter, f, null, stack);

        var report = SurfaceMesher.Mesh(problem, Mesh(20, 4));
        double yS = 0.5 * w, yR = 1.5 * w + gap;

        Assert.False(PlanarPorts.TryResolve(report.Mesh, TwoCut(1, 0.5 * len, yS, yR),
                                            out _, out string? why));
        _out.WriteLine(why);

        Assert.Contains("INFERRED onto different levels", why!);
        Assert.Contains("signal cut", why!);
        Assert.Contains("return cut", why!);
        Assert.Contains("state the level of both", why!);

        // Saying both levels is what makes it legal — the refusal is about the SILENCE, not about
        // the geometry.
        Assert.True(PlanarPorts.TryResolve(
            report.Mesh, TwoCut(1, 0.5 * len, yS, yR, layer: 0, negLayer: 1),
            out var stated, out string? why2), why2);
        Assert.Equal(0, stated!.LayerIndex);
        Assert.Equal(1, stated.Negative!.LayerIndex);
        _out.WriteLine(stated.Describe());
    }

    /// <summary>A conductor-referenced port with no return point named at all: refused, naming what
    /// is missing rather than searching for the nearest conductor.</summary>
    [Fact]
    public void Gate5_AConductorReferenceWithNoReturnPoint_IsRefusedNamingWhatIsMissing()
    {
        var mesh = SurfaceMesher.Mesh(Cps(0.3e-3, 0.15e-3, 2.5e-3, 9.5e-3, 10e9), Mesh(20, 4)).Mesh;
        var port = new PlanarPort(1, new EmPoint(4.75e-3, 0.225e-3), PlanarPortSide.MinX, 50.0,
                                  Reference: PlanarPortReference.SecondConductor,
                                  Kind: PlanarPortKind.InternalDeltaGap);

        Assert.False(PlanarPorts.TryResolve(mesh, port, out _, out string? why));
        _out.WriteLine(why);
        Assert.Contains("does not say WHERE", why!);
        Assert.Contains("point on the return metal", why!);
    }

    /// <summary>A return point that is not on any metal: the inner refusal, wrapped so it says WHICH
    /// terminal failed. Without that a user reading "Port 1 is not on any conductor" has two places
    /// to look and no way to tell which.</summary>
    [Fact]
    public void Gate5_AReturnPointOffTheMetal_IsRefusedSayingItIsTheRETURNTerminal()
    {
        const double f = 10e9, len = 9.5e-3, w = 0.3e-3, s = 0.15e-3;
        var report = SurfaceMesher.Mesh(Cps(w, s, 2.5e-3, len, f), Mesh(20, 4));
        double yc = 0.5 * s + 0.5 * w;

        // In the slot, where there is no metal on either strip.
        var port = new PlanarPort(1, new EmPoint(0.5 * len, yc), PlanarPortSide.MinX, 50.0,
                                  Reference: PlanarPortReference.CoplanarGround,
                                  Kind: PlanarPortKind.InternalDeltaGap,
                                  NegativeLocation: new EmPoint(0.5 * len, 0));

        Assert.False(PlanarPorts.TryResolve(report.Mesh, port, out _, out string? why));
        _out.WriteLine(why);
        Assert.Contains("RETURN terminal", why!);
        Assert.Contains("did not resolve", why!);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // GATE 6 — R-rp2a-11's refusal, and what RP-2c did to it
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>RP-2a refused a conductor-referenced EDGE port by name, because its error box is a coplanar
    /// line and <c>PlanarCalibration</c> built uniform single conductors over the plane. RP-2c built
    /// that standard, so the refusal is gone and this is what stands in its place.</b>
    ///
    /// <para>The test is kept rather than deleted: R-rp2a-11's requirement was that the refusal name
    /// the arrival place rather than a phase number, and the counterpart of that requirement, once
    /// the capability has arrived, is that the port RESOLVES — with the cross-section its standard is
    /// built from. The de-embedding itself is gated in <c>CoplanarDeembedTests</c>; what belongs here
    /// is that the kernel no longer turns the port away.</para>
    /// </summary>
    [Theory]
    [InlineData(PlanarPortReference.CoplanarGround)]
    [InlineData(PlanarPortReference.SecondConductor)]
    public void Gate6_AConductorReferencedEdgePort_ResolvesSinceRp2cBuiltItsStandard(
        PlanarPortReference reference)
    {
        const double f = 10e9, len = 9.5e-3, w = 0.3e-3, s = 0.15e-3;
        var report = SurfaceMesher.Mesh(Cps(w, s, 2.5e-3, len, f), Mesh(20, 4));
        double yc = 0.5 * s + 0.5 * w;

        var port = TwoCut(1, 0, yc, -yc, reference, kind: PlanarPortKind.Edge);
        Assert.True(PlanarPorts.TryResolve(report.Mesh, port, out var res, out string? why),
                    $"a conductor-referenced EDGE port was refused: {why}");
        _out.WriteLine(res!.Describe());

        Assert.NotNull(res.Negative);
        Assert.True(res.IsDeembeddable);

        // The cross-section is the whole of what RP-2c added to the resolution: two conductors and
        // the slot between them, which is what the coplanar standard is extruded from.
        var xs = res.CrossSection;
        Assert.NotNull(xs);
        Assert.Equal(2, xs.ConductorCount);
        Assert.True(xs.SlotM > 0);
        _out.WriteLine($"cross-section: {xs.ConductorCount} conductors over " +
                       $"{(xs.SpanHiM - xs.SpanLoM) * 1e6:F1} µm, slot {xs.SlotM * 1e6:F1} µm");
    }

    /// <summary>An internal (via-to-plane) port takes no reference at all — its negative terminal IS
    /// the plane by construction, so there is nothing for a second cut to be.</summary>
    [Fact]
    public void Gate6_AnInternalViaPortWithAConductorReference_IsRefusedByWhatThePortIS()
    {
        var mesh = SurfaceMesher.Mesh(Cps(0.3e-3, 0.15e-3, 2.5e-3, 9.5e-3, 10e9), Mesh(20, 4)).Mesh;
        var port = TwoCut(1, 4.75e-3, 0.225e-3, -0.225e-3, kind: PlanarPortKind.Internal);

        Assert.False(PlanarPorts.TryResolve(mesh, port, out _, out string? why));
        _out.WriteLine(why);
        Assert.Contains("ground plane by construction", why!);
        Assert.Contains("takes no reference", why!);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // The record, and what the port SAYS about itself
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// R-rp2a-10 — the resolution GREW: a ground-referenced port is the record it always was, with a
    /// null companion, and a two-cut port carries the negative terminal's counterparts field for
    /// field. R-rp2a-12 — the two enum members are one object here and differ only in what the note
    /// says.
    /// </summary>
    [Fact]
    public void TheResolutionGrowsRatherThanForking_AndTheTwoReferenceKindsAreOneMechanism()
    {
        const double f = 10e9, len = 9.5e-3, w = 0.3e-3, s = 0.15e-3;
        var report = SurfaceMesher.Mesh(Cps(w, s, 2.5e-3, len, f), Mesh(20, 4));
        double yc = 0.5 * s + 0.5 * w, xc = 0.5 * len;

        var coplanar = PlanarPorts.Resolve(report.Mesh, TwoCut(1, xc, yc, -yc));
        var second   = PlanarPorts.Resolve(report.Mesh,
                            TwoCut(1, xc, yc, -yc, PlanarPortReference.SecondConductor));

        // One mechanism: same cuts, same bases, same station, same weights.
        Assert.Equal(coplanar.BasisIndices, second.BasisIndices);
        Assert.Equal(coplanar.Negative!.BasisIndices, second.Negative!.BasisIndices);
        Assert.Equal(coplanar.ReferencePlaneM, second.ReferencePlaneM, 15);
        Assert.Equal(coplanar.Negative.ReferencePlaneM, second.Negative.ReferencePlaneM, 15);

        // Two meanings: the note reads differently, which is why both members exist.
        Assert.Contains("coplanar ground conductor", coplanar.Describe());
        Assert.Contains("second signal conductor",   second.Describe());
        Assert.Contains("NOT the ground plane",      coplanar.Describe());

        // The negative terminal is a real cross-section, not a placeholder.
        Assert.Equal(coplanar.BasisCount, coplanar.Negative.BasisCount);
        Assert.Equal(coplanar.WidthM, coplanar.Negative.WidthM, 12);
        Assert.Equal(coplanar.Negative.BasisCount + 1, coplanar.Negative.TransverseLines.Count);

        // The weights: half the port's voltage on each cut, opposite signs, and the port keeps ONE
        // polarity — the negative terminal carries no sign of its own to drift.
        Assert.Equal(+0.5, coplanar.PositiveWeight, 15);
        Assert.Equal(-0.5, coplanar.NegativeWeight, 15);
        Assert.True(coplanar.IsConductorReferenced);
        Assert.False(coplanar.IsDeembeddable);          // it is an interior cut; nothing to remove

        _out.WriteLine(coplanar.Describe());
    }

    /// <summary>
    /// R-rp2a-6 — the SAME B on both sides, over two blocks. The incidence column has both signed
    /// blocks and nothing else, and the current read back is the signed sum over BOTH index sets.
    /// A code that impresses over two and reads back over one gives a plausible, wrong Y that is
    /// symmetric anyway, so reciprocity does not catch it — this does.
    /// </summary>
    [Fact]
    public void TheSameIncidenceColumnIsUsedForTheDriveAndForTheReadBack_OverBothBlocks()
    {
        const double f = 10e9, len = 9.5e-3, w = 0.3e-3, s = 0.15e-3;
        var report = SurfaceMesher.Mesh(Cps(w, s, 2.5e-3, len, f), Mesh(20, 4));
        double yc = 0.5 * s + 0.5 * w;
        var port = PlanarPorts.Resolve(report.Mesh, TwoCut(1, 0.5 * len, yc, -yc));

        int n = report.Mesh.Bases.Count;
        var rhs = PlanarExcitation.RightHandSide(n, port);

        var positive = new HashSet<int>(port.BasisIndices);
        var negative = new HashSet<int>(port.Negative!.BasisIndices);
        Assert.Empty(positive.Intersect(negative));

        int nonZero = 0;
        for (int m = 0; m < n; m++)
        {
            if (positive.Contains(m))      { Assert.Equal(+0.5, rhs[m].Real, 15); nonZero++; }
            else if (negative.Contains(m)) { Assert.Equal(-0.5, rhs[m].Real, 15); nonZero++; }
            else                             Assert.Equal(Complex.Zero, rhs[m]);
        }
        Assert.Equal(positive.Count + negative.Count, nonZero);

        // The read-back is the same column: a current vector that is +1 on the positive block and
        // −1 on the negative one is the loop current, and Bᵀ of it is the sum of both halves.
        var currents = new NumFlat.Vec<Complex>(n);
        foreach (int m in positive) currents[m] = Complex.One;
        foreach (int m in negative) currents[m] = -Complex.One;
        Complex i = PlanarExcitation.PortCurrent(currents, port);

        _out.WriteLine($"{positive.Count} positive + {negative.Count} negative bases; " +
                       $"Bᵀ·I on the loop current = {i}");
        Assert.Equal(0.5 * (positive.Count + negative.Count), i.Real, 12);
    }
}
