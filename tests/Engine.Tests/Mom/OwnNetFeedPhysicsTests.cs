// R-pcal7 — the de-embedded numbers the decisions in `OwnNetFeedNeighbourhoodTests` were taken for.
//
// Category=Benchmark, by this repository's own rule: each of these solves a DUT and its calibration
// standards at 2 GHz and costs 3 s to 100 s on its own (measured, per fixture, in
// `src/Engine/Mom/RESOLVED.md` §PCAL7-OWNNET). The CHEAP half — which metal is a neighbour, how long
// a lead is, which run is refused — is in the routine tier beside them and is what would catch a
// regression first; this is what says those decisions produce an inductor.

using System.Numerics;
using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using NumFlat;
using RfCore;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

[Trait("Category", "Benchmark")]
public sealed class OwnNetFeedPhysicsTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _out = output;

    private const double F = 2e9;

    private static PlanarSolveResult Solve(PlanarProblem problem, PlanarPort[] ports,
                                           PlanarMeshSettings? mesh = null,
                                           PlanarSolveSettings? settings = null)
    {
        var (grown, leads, _) = PlanarFeedExtension.Extend(problem, ports, settings?.Calibration);
        var report = SurfaceMesher.Mesh(grown, mesh ?? PlanarOwnNetFixtures.Mesh, leads: leads);
        var resolved = PlanarPorts.ResolveAll(report.Mesh, ports);
        return PlanarSolve.Run(grown, report.Mesh, resolved, [F], settings, leads: leads);
    }

    /// <summary>The series impedance a two-port reads as, <c>Zs = −1/Y₂₁</c>, and the inductance that
    /// implies. The quantity the whole report is written in.</summary>
    private static (Complex Zs, double Lnh) Series(Mat<Complex> s, double fHz)
    {
        var y = RFNetwork.SToY(s, [new Complex(50, 0), new Complex(50, 0)]);
        var zs = -1.0 / y[1, 0];
        return (zs, zs.Imaginary / (2 * Math.PI * fHz) * 1e9);
    }

    private void Report(string name, PlanarSolveResult r)
    {
        var (zs, l) = Series(r.Points[0].S, F);
        _out.WriteLine($"{name}: Zs = {zs.Real:F4}{(zs.Imaginary < 0 ? "" : "+")}j{zs.Imaginary:F4} Ω, " +
                       $"L = {l:F4} nH, σ_max = {RFNetwork.Passivity(r.Points[0].S):F6}, " +
                       $"N = {r.UnknownCount}");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-pcal4-1's invariant: a feed with nothing inside its clearance reaches none of this
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Bit-identical to twelve significant figures.</b> These three were measured against the same
    /// fixtures at the commit before R-pcal7 and every digit below is that measurement, not a
    /// tolerance chosen to pass: `Straight` 1.391026245606 nH, `ViaHop` 1.419003477165 nH, `Bend`
    /// 1.528748572685 nH. A change in the neighbour predicate that reached a clear feed would move
    /// them in the fourth digit at best.
    /// </summary>
    [Theory]
    [InlineData("Straight", 1.391026245606)]
    [InlineData("ViaHop",   1.419003477165)]
    [InlineData("Bend",     1.528748572685)]
    public void AClearFeedsAnswerIsUnchanged(string name, double expectedNh)
    {
        var (problem, ports) = PlanarOwnNetFixtures.Fixture(name, F);
        var r = Solve(problem, ports);
        Report(name, r);

        var (zs, l) = Series(r.Points[0].S, F);
        Assert.Equal(expectedNh, l, 9);
        Assert.True(zs.Real > 0, $"{name} must read a positive resistance; got {zs.Real:F4} Ω");
        Assert.True(RFNetwork.Passivity(r.Points[0].S) <= 1.0);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-pcal7-3 — the own-net calibration group produces an inductor
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>`Coupled` — 135.6 nH and non-passive before, 1.16 nH and passive after.</b> Two 800 µm
    /// arms 50 µm apart shorted at the far end; a two-wire estimate of the same section is
    /// ≈ 1.0–1.3 nH. The 135 nH was not a degraded answer: |S₂₁| was 0.0586, which is an open
    /// circuit, and σ_max was 1.0037, which is not a network.
    /// </summary>
    [Fact]
    public void ACoilBroughtOutSideBySide_ReadsAsAnInductor()
    {
        var (problem, ports) = PlanarOwnNetFixtures.Fixture("Coupled", F);
        var r = Solve(problem, ports);
        Report("Coupled", r);

        var (zs, l) = Series(r.Points[0].S, F);
        Assert.InRange(l, 1.0, 1.4);
        Assert.True(zs.Real > 0, $"a passive coil cannot have a negative resistance; got {zs.Real:F4} Ω");
        Assert.True(RFNetwork.Passivity(r.Points[0].S) <= 1.0);
        Assert.True(r.Points[0].S[1, 0].Magnitude > 0.9,
                    "the two terminals of one inductor are not an open circuit");
        Assert.Contains(r.Notes, n => n.Contains("CALIBRATION GROUP"));
    }

    /// <summary>
    /// <b>`UnevenPads` — 100.9 nH and non-passive before, 1.18 nH and passive after.</b> The same
    /// structure with its two feeds on pads of different lengths, which is what makes R-fed-1 grow
    /// leads that differ and what <c>CommonPeelLength</c> would otherwise refuse the group over.
    /// </summary>
    [Fact]
    public void AGroupWhoseMembersNeededDifferentLeads_StillCalibrates()
    {
        var (problem, ports) = PlanarOwnNetFixtures.Fixture("UnevenPads", F);
        var r = Solve(problem, ports);
        Report("UnevenPads", r);

        var (zs, l) = Series(r.Points[0].S, F);
        Assert.InRange(l, 1.0, 1.4);
        Assert.True(zs.Real > 0);
        Assert.True(RFNetwork.Passivity(r.Points[0].S) <= 1.0);
        Assert.Contains(r.Notes, n => n.Contains("CALIBRATION GROUP"));
        Assert.DoesNotContain(r.Notes, n => n.Contains("not the same length"));
    }

    /// <summary>
    /// <b>`Splay` is the file whose published answer this work CHANGES, and it passed before with no
    /// flag on it</b> — 3.2519 nH against 1.5265 nH now, both passive, both plausible. Which of the
    /// two is right is not decided by this test; it is decided by
    /// <see cref="ACoupledSectionMustCascadeWithItself"/>, and this records the number that came out
    /// so a future change to it is visible rather than silent.
    /// </summary>
    [Fact]
    public void SplayIsRecalibratedAsAGroup()
    {
        var (problem, ports) = PlanarOwnNetFixtures.Fixture("Splay", F);
        var r = Solve(problem, ports);
        Report("Splay", r);

        var (_, l) = Series(r.Points[0].S, F);
        Assert.Equal(1.52652, l, 4);
        Assert.True(RFNetwork.Passivity(r.Points[0].S) <= 1.0);
        Assert.Contains(r.Notes, n => n.Contains("CALIBRATION GROUP"));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // M1's ORACLE — and the error IS the answer
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>A de-embedded uniform coupled section of length ℓ, cascaded with itself, must equal the
    /// same section de-embedded at length 2ℓ.</b> Nothing external is modelled: the identity is a
    /// property of a uniform line and of an exact de-embedding, so the residual between T(2ℓ) and
    /// T(ℓ)² is the instrument's own error and the two calibration paths can be ranked by it.
    ///
    /// <para><b>This is what settled `Splay`.</b> Its two feeds sit 350 µm apart, which is inside the
    /// driven clearance, so with own-net metal visible a group forms and the published inductance
    /// moves by 2×. Both answers are passive and both are plausible; the control says which is the
    /// instrument. Measured over 18 / 50 / 350 µm and two lengths: grouped 1.2e-2 … 7.7e-2 against
    /// per-port 2.1e-1 … 4.4e+0, better at every separation and every length, and passive at every
    /// point where the per-port path is not.</para>
    /// </summary>
    [Theory]
    [InlineData(50.0)]
    [InlineData(350.0)]
    public void ACoupledSectionMustCascadeWithItself(double separationUm)
    {
        const double L = 800;

        double Residual(bool grouped)
        {
            var st = new PlanarSolveSettings(
                Calibration: PlanarCalibrationSettings.Default with { IncludeDrivenGroups = grouped },
                DeembedOutsideCalibrationValidity: !grouped);

            var a = PlanarOwnNetFixtures.CoupledPair(F, L, separationUm);
            var b = PlanarOwnNetFixtures.CoupledPair(F, 2 * L, separationUm);
            var ta = Cascade(Solve(a.Problem, a.Ports, settings: st).Points[0].S);
            var tb = Cascade(Solve(b.Problem, b.Ports, settings: st).Points[0].S);
            return Frobenius(tb - ta * ta) / Frobenius(tb);
        }

        double grouped = Residual(true);
        double perPort = Residual(false);
        _out.WriteLine($"{separationUm} µm: grouped {grouped:0.00e+00}, per-port {perPort:0.00e+00} " +
                       $"— {perPort / grouped:F1}× better grouped");

        Assert.True(grouped < perPort,
            $"the grouped path must be the better instrument; got {grouped:0.00e+00} against " +
            $"{perPort:0.00e+00} at {separationUm} µm. If this ever inverts, R-pcal7-3 is wrong.");
    }

    /// <summary>Block wave-cascade matrix of a 4-port whose left pair is (1,2) and right pair (3,4),
    /// so that a section of length 2ℓ is the square of one of length ℓ.</summary>
    private static Mat<Complex> Cascade(Mat<Complex> s)
    {
        Mat<Complex> Sub(int r0, int c0)
        {
            var m = new Mat<Complex>(2, 2);
            for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++) m[i, j] = s[r0 + i, c0 + j];
            return m;
        }

        var inv = Sub(2, 0).Inverse();
        var t11 = Sub(0, 2) - Sub(0, 0) * inv * Sub(2, 2);
        var t12 = Sub(0, 0) * inv;
        var t21 = -(inv * Sub(2, 2));

        var t = new Mat<Complex>(4, 4);
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
            {
                t[i, j] = t11[i, j];     t[i, j + 2]     = t12[i, j];
                t[i + 2, j] = t21[i, j]; t[i + 2, j + 2] = inv[i, j];
            }
        return t;
    }

    private static double Frobenius(Mat<Complex> m)
    {
        double a = 0;
        for (int i = 0; i < m.RowCount; i++)
            for (int j = 0; j < m.ColCount; j++) a += m[i, j].Magnitude * m[i, j].Magnitude;
        return Math.Sqrt(a);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // M5 — the part it was reported on
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The reported MMIC spiral, on its own artwork, reads as an inductor.</b> 33.5 nH with a
    /// −13.8 Ω resistance and σ_max = 1.0126 before; 3.5 nH, positive and passive, after. A
    /// modified-Wheeler estimate for 3 turns of 10 µm on an 8 µm pitch with a 120 µm opening is
    /// 2.48 nH, and the coil converges to 3.58 nH at the next mesh up — so the remaining gap is the
    /// estimate and the mesh, not the calibration.
    /// </summary>
    [Fact]
    public void TheReportedSpiral_ReadsAsAnInductor()
    {
        var problem = MmicCoilFixture.Coil(F);
        PlanarPort[] ports =
        [
            new(1, new EmPoint(-110e-6, -125e-6), PlanarPortSide.MinY, 50.0) { LayerIndex = 0 },
            new(2, new EmPoint(-155e-6,  -46e-6), PlanarPortSide.MinX, 50.0) { LayerIndex = 0 },
        ];
        var mesh = new PlanarMeshSettings(Auto: false, CellsPerWavelength: 20, EdgeMesh: false,
                                          MinCellsAcrossConductor: 2);

        var r = Solve(problem, ports, mesh);
        Report("MMIC spiral", r);

        var (zs, l) = Series(r.Points[0].S, F);
        Assert.InRange(l, 3.3, 3.8);
        Assert.True(zs.Real > 0, $"a passive coil cannot have a negative resistance; got {zs.Real:F4} Ω");
        Assert.True(RFNetwork.Passivity(r.Points[0].S) <= 1.0);
        Assert.Empty(r.NonPassivePoints);
        Assert.All(r.FeedClearances, c => Assert.False(c.Breached));
    }
}
