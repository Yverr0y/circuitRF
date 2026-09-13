// PCAL3 — the calibration standard contains the passive neighbour the feed actually has.
// `docs/sonnet-briefs/brief-portcal-3-passive-neighbour.md`.
//
// The geometry is the series' own coupled pair with the neighbour's ports DELETED, which is the one
// PCAL1 measured at 18.0 dB out in S₁₁ at 1 GHz. What is gated here is the DECISION and the
// CONSTRUCTION — which conductor joins the profile, what is declined and by what name, and that the
// standard is still built from the DUT's own gridlines rather than re-meshed. The accuracy
// measurement itself lives in `src/Engine/Mom/RESOLVED.md`: it needs the cross-section oracle at
// seven frequencies and costs minutes, which is exactly what the standing rule about measuring with
// a harness rather than the Benchmark tier is about.

using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using Xunit;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

public sealed class PlanarPassiveNeighbourTests(ITestOutputHelper output)
{
    private static readonly GroundedSlab Slab = new(0.9e-3, new EmMaterial(4.4, 0.02));
    private const double W = 254e-6, Len = 3.83e-3, F = 5e9;

    /// <summary>The pair, the neighbour <paramref name="heights"/> × h away and running
    /// <paramref name="neighbourLen"/> of the line's length; a port at each end of the FIRST
    /// conductor only unless <paramref name="driven"/>.</summary>
    private static (PlanarProblem Problem, PlanarMesh Mesh, IReadOnlyList<PlanarPortResolution> Ports)
        Pair(double heights, bool driven = false, double neighbourLen = Len)
    {
        double gap = heights * Slab.HeightM;
        var problem = PlanarLineFixtures.Problem(Slab, F,
            PlanarLineFixtures.Rect(0, 0,       Len,          W),
            PlanarLineFixtures.Rect(0, W + gap, neighbourLen, W + gap + W));
        var mesh = SurfaceMesher.Mesh(problem, PlanarLineFixtures.Coarse).Mesh;

        var ports = new List<PlanarPort>
        {
            new(1, new EmPoint(0,   0.5 * W), PlanarPortSide.MinX, 50.0),
            new(2, new EmPoint(Len, 0.5 * W), PlanarPortSide.MaxX, 50.0),
        };
        if (driven)
        {
            ports.Add(new PlanarPort(3, new EmPoint(0, W + gap + 0.5 * W), PlanarPortSide.MinX, 50.0));
            ports.Add(new PlanarPort(4, new EmPoint(neighbourLen, W + gap + 0.5 * W),
                                     PlanarPortSide.MaxX, 50.0));
        }
        return (problem, mesh, PlanarPorts.ResolveAll(mesh, ports));
    }

    private static PlanarPortResolution? Widen(
        PlanarMesh mesh, IReadOnlyList<PlanarPortResolution> ports, out string? declined,
        double passiveHeights = 2.0)
        => PlanarPorts.TryWidenForNeighbours(
            mesh, ports[0], ports,
            PlanarCalibration.EndRunCellsFor(ports[0], Slab),
            passiveHeights * Slab.HeightM, PlanarConductors.Of(mesh), out declined);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-pcal3-1 — the neighbour joins the profile, and the run stops being a refusal
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Gate 1's decision half.</b> At the separation the series opened on — 246 µm, 0.27 substrate
    /// heights — a portless neighbour breaches the passive threshold and is therefore a refusal since
    /// PCAL2. It is taken into the profile instead, and the SAME predicate then reports the feed
    /// clear, because everything inside a port's profile is reproduced in its standard.
    /// </summary>
    [Fact]
    public void APassiveNeighbourJoinsTheProfile_AndTheRunThatWasRefusedNowRuns()
    {
        var (problem, mesh, ports) = Pair(0.27);

        // Before: the shipped predicate refuses it.
        var before = PlanarPorts.MeasureFeedClearance(
            mesh, ports[0], ports, 3.0 * Slab.HeightM, 5.0 * Slab.HeightM, 2.0 * Slab.HeightM,
            Slab.HeightM);
        Assert.NotNull(before);
        Assert.True(before!.Breached);
        Assert.Equal(PlanarNeighbourClass.Passive, before.Neighbour);
        output.WriteLine(before.Margin());

        var wider = Widen(mesh, ports, out string? declined);
        Assert.Null(declined);
        Assert.NotNull(wider);

        var nb = wider!.Neighbourhood!;
        output.WriteLine(nb.Describe());
        Assert.Equal(1, nb.NeighbourCount);
        Assert.Equal(243e-6, nb.NearestM, 9);      // 0.27 h on this slab

        // The port's own conductor is still its own run inside the wider profile.
        Assert.Equal(ports[0].TransverseLines[0],  nb.Lines[nb.OwnLo],     12);
        Assert.Equal(ports[0].TransverseLines[^1], nb.Lines[nb.OwnHi + 1], 12);
        Assert.True(nb.SpanHiM > ports[0].TransverseLines[^1], "the profile did not get wider");

        // There is a VOID between the two conductors — no basis may cross the gap.
        Assert.Contains(false, nb.IsMetal);

        // After: the same predicate, asked of the widened port, says the feed is clear.
        var after = PlanarPorts.MeasureFeedClearance(
            mesh, wider, ports, 3.0 * Slab.HeightM, 5.0 * Slab.HeightM, 2.0 * Slab.HeightM,
            Slab.HeightM);
        Assert.NotNull(after);
        Assert.False(after!.Breached);
        output.WriteLine(after.Margin());

        // And the whole run no longer throws — which is the shipped behaviour this phase is about.
        var r = PlanarSolve.Run(problem, mesh, ports, [F]);
        Assert.Contains(r.Notes, n => n.Contains("neighbouring conductor(s) beside the feed",
                                                 StringComparison.Ordinal));
        Assert.All(r.FeedClearances, c => Assert.False(c.Breached));
    }

    /// <summary>
    /// <b>R-pcal3-4 — nothing that passes today changes.</b> A feed with nothing beside it resolves to
    /// a null neighbourhood, so <see cref="PlanarCalibration.BuildLine"/> takes the path it always
    /// took and the standard is the one it always built. Asserted on the standard's own COORDINATES,
    /// because that is what D4 is a construction of.
    /// </summary>
    [Fact]
    public void AClearFeedIsUntouched_AndItsStandardIsTheOneItAlwaysWas()
    {
        var (_, mesh, ports) = Pair(6.0);
        Assert.Null(ports[0].Neighbourhood);

        var wider = Widen(mesh, ports, out string? declined);
        Assert.Null(wider);
        Assert.Null(declined);            // nothing to widen is not a decline

        int k = PlanarCalibration.EndRunCellsFor(ports[0], Slab);
        var std = PlanarCalibration.BuildLine(ports[0], 3.0 * Slab.HeightM, k);
        Assert.Equal(ports[0].TransverseLines.Count, std.Mesh.GridY.Count);
        for (int i = 0; i < ports[0].TransverseLines.Count; i++)
            Assert.Equal(ports[0].TransverseLines[i], std.Mesh.GridY[i], 15);
        Assert.Null(std.ModePotential);
        Assert.Null(std.FloatingPotential);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-pcal3-2 / gate 3 — a neighbour that cannot be extruded is DECLINED BY NAME
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A standard is a uniform extrusion, so a neighbour that ENDS inside the run the standard
    /// reproduces cannot be put in one. The decline names the conductor and the station, and the run
    /// then behaves exactly as PCAL2 specifies — it refuses, and the refusal is the one PCAL2 wrote.
    /// </summary>
    [Fact]
    public void ANeighbourThatEndsInsideTheStandardsRunIsDeclinedByName_AndPcal2Refuses()
    {
        // The neighbour runs 1.5 mm of the line's 3.83 mm, so it stops well inside the ~2.7 mm the
        // standard reproduces at port 1 — and stops nowhere near port 2's end of it.
        var (problem, mesh, ports) = Pair(0.27, neighbourLen: 1.5e-3);

        var wider = Widen(mesh, ports, out string? declined);
        Assert.Null(wider);
        Assert.NotNull(declined);
        output.WriteLine(declined!);
        Assert.Contains("not uniform", declined, StringComparison.Ordinal);
        Assert.Contains("Port 1's feed", declined, StringComparison.Ordinal);

        var ex = Assert.Throws<PlanarFeedClearanceRefusedException>(
            () => PlanarSolve.Run(problem, mesh, ports, [F]));
        output.WriteLine(ex.Message);
        Assert.Contains("port 1", ex.Message, StringComparison.Ordinal);
        Assert.All(ex.Breaches, b => Assert.Equal(PlanarNeighbourClass.Passive, b.Neighbour));
    }

    /// <summary>
    /// <b>The boundary with brief 4, asserted rather than left to prose.</b> The moment the neighbour
    /// carries a port of its own there are two modes at the reference plane against a per-port SCALAR
    /// error box, and reproducing the metal does not fix that — so it is declined, by name, and the
    /// refusal PCAL2 already writes stands.
    /// </summary>
    [Fact]
    public void ADrivenNeighbourIsNotWidened_AndSaysWhy()
    {
        var (problem, mesh, ports) = Pair(0.27, driven: true);

        var wider = PlanarPorts.TryWidenForNeighbours(
            mesh, ports[0], ports, PlanarCalibration.EndRunCellsFor(ports[0], Slab),
            2.0 * Slab.HeightM, PlanarConductors.Of(mesh), out string? declined);

        Assert.Null(wider);
        Assert.NotNull(declined);
        output.WriteLine(declined!);
        Assert.Contains("CARRIES A PORT", declined, StringComparison.Ordinal);

        // PCAL4 — the grouping is turned OFF here on purpose. This gate is PCAL2's: a DRIVEN
        // neighbour that the calibration cannot describe is a refusal. PCAL4 describes one KIND of
        // driven neighbour — two ports sharing a reference plane — with a modal error box, and this
        // synthetic pair is exactly that kind, so with the grouping on it runs. What is asserted
        // here is still what has to hold: everything PCAL4 declines, and everything it is switched
        // off for, still refuses rather than publishing.
        var ex = Assert.Throws<PlanarFeedClearanceRefusedException>(
            () => PlanarSolve.Run(problem, mesh, ports, [F], new PlanarSolveSettings(Calibration: PlanarCalibrationSettings.Default with { IncludeDrivenGroups = false })));
        Assert.All(ex.Breaches, b => Assert.Equal(PlanarNeighbourClass.Driven, b.Neighbour));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // Gate 4 — the standard is still a CONSTRUCTION from the DUT's own mesh (R-prt-5)
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>D4's rule is unchanged, one conductor wider.</b> Every transverse gridline of the widened
    /// standard is one of the DUT's own, at the same coordinate, and the longitudinal end run is the
    /// DUT's own cell run — an EQUALITY, not a tolerance, because the error box has to be the same
    /// object and "nearly the same cells" is not the same object.
    /// </summary>
    [Fact]
    public void TheWidenedStandardsCoordinatesAreTheDutsOwn()
    {
        var (_, mesh, ports) = Pair(0.27);
        var port = Widen(mesh, ports, out _)!;
        var nb   = port.Neighbourhood!;

        int k = PlanarCalibration.EndRunCellsFor(port, Slab);
        var std = PlanarCalibration.BuildLine(port, 3.0 * Slab.HeightM, k);

        // Transverse: the profile's lines, verbatim, and each of them is a gridline of the DUT.
        Assert.Equal(nb.Lines.Count, std.Mesh.GridY.Count);
        for (int i = 0; i < nb.Lines.Count; i++)
        {
            Assert.Equal(nb.Lines[i], std.Mesh.GridY[i], 15);
            Assert.Contains(mesh.GridY, g => g == nb.Lines[i]);
        }

        // Longitudinal: the first k cells are the port's own, verbatim, at both ends.
        for (int i = 0; i < k; i++)
        {
            Assert.Equal(port.LongitudinalRunM[i],
                         std.Mesh.GridX[i + 1] - std.Mesh.GridX[i], 15);
            Assert.Equal(port.LongitudinalRunM[i],
                         std.Mesh.GridX[^(i + 1)] - std.Mesh.GridX[^(i + 2)], 15);
        }

        // A cell exists only where the profile says metal, so the slot carries no basis at all.
        Assert.Equal(nb.IsMetal.Count(m => m) * (std.Mesh.GridX.Count - 1), std.Mesh.Cells.Count);
        output.WriteLine($"standard: N = {std.Mesh.Bases.Count}, cells = {std.Mesh.Cells.Count}, " +
                         $"profile {nb.Lines.Count - 1} interval(s), {nb.IsMetal.Count(m => m)} metal");
    }

    /// <summary>
    /// <b>D7's electrostatic problem on a widened standard: the port drives its OWN conductor, and the
    /// neighbour's potential is an UNKNOWN.</b> Putting the whole sheet at 1 V measures the two of
    /// them bonded together, and holding the neighbour at 0 V measures a ground pour — a different
    /// structure. Both are complete and plausible; on this fixture they are +29.9 % and +12.3 % away
    /// from the answer, which lands directly on Z_c.
    /// </summary>
    [Fact]
    public void TheNeighbourIsDrivenByNothingAndFloats()
    {
        var (_, mesh, ports) = Pair(0.27);
        var port = Widen(mesh, ports, out _)!;
        var nb   = port.Neighbourhood!;
        var std  = PlanarCalibration.BuildLine(port, 3.0 * Slab.HeightM,
                                               PlanarCalibration.EndRunCellsFor(port, Slab));

        Assert.NotNull(std.ModePotential);
        Assert.NotNull(std.ModeWeight);
        Assert.NotNull(std.FloatingPotential);

        for (int c = 0; c < std.Mesh.Cells.Count; c++)
        {
            bool own = nb.IsOwn(std.Mesh.Cells[c].IY);
            Assert.Equal(own ? 1.0 : 0.0, std.ModePotential![c]);
            Assert.Equal(own ? 1.0 : 0.0, std.ModeWeight![c]);
            Assert.Equal(own ? 0.0 : 1.0, std.FloatingPotential![c]);
        }

        // The two readings are far enough apart that choosing silently would matter.
        var terms = PlanarKernelTerms.StaticScalar(Slab);
        double grounded = PlanarDeembed.StaticCapacitance(
            std.Mesh, terms, null, null, Slab.HeightM, std.ModePotential, std.ModeWeight);
        double floating = PlanarDeembed.StaticCapacitance(
            std.Mesh, terms, null, null, Slab.HeightM, std.ModePotential, std.ModeWeight,
            std.FloatingPotential);
        output.WriteLine($"grounded {grounded:E4}, floating {floating:E4}, " +
                         $"{(floating / grounded - 1) * 100:F1}%");
        Assert.True(floating < grounded, "a floating neighbour must carry LESS of the line's charge");
        Assert.True(Math.Abs(floating / grounded - 1) > 0.02,
                    "the two boundary conditions are supposed to differ materially here");
    }

    /// <summary>
    /// Two ports share one calibration only if they share the WHOLE neighbourhood — the same sentence
    /// RP-2c states for a coplanar return, one conductor over. Handing port 2 a standard built for
    /// port 1's neighbour is a complete, plausible error box for a structure that is not there.
    /// </summary>
    [Fact]
    public void TwoPortsWithDifferentNeighbourhoodsDoNotShareAStandard()
    {
        var (_, mesh, ports) = Pair(0.27);
        int k = PlanarCalibration.EndRunCellsFor(ports[0], Slab);
        var conn = PlanarConductors.Of(mesh);

        var p1 = PlanarPorts.TryWidenForNeighbours(mesh, ports[0], ports, k, 2.0 * Slab.HeightM,
                                                   conn, out _)!;
        var p2 = PlanarPorts.TryWidenForNeighbours(mesh, ports[1], ports, k, 2.0 * Slab.HeightM,
                                                   conn, out _)!;

        // The two ends of this line ARE the same cross-section and the same neighbourhood.
        Assert.True(PlanarPortCalibrator.SameCrossSection(p1, p2, k));

        // A widened port and the same port unwidened are not.
        Assert.False(PlanarPortCalibrator.SameCrossSection(p1, ports[1], k));
    }
}
