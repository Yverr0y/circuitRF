// PCAL2 — a breach of a calibrated port's feed clearance is a REFUSAL, and the threshold is two
// numbers rather than one. `docs/sonnet-briefs/brief-portcal-2-refuse-not-warn.md`.
//
// The geometry here is the series' own: two parallel microstrips on 0.9 mm FR-4, ports at the ends,
// separation swept. PCAL1 measured every threshold this file asserts against the exact
// cross-section oracle (`src/Engine/Mom/RESOLVED.md`, "PCAL1"); what is gated here is the DECISION
// the solver takes from that measurement, not the measurement itself — those live in the findings
// and cost minutes apiece.

using System.Numerics;
using CircuitRF.Engine.Mom;
using CircuitRF.Engine.Tests.Mom.Support;
using Xunit;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Mom;

public sealed class PlanarFeedClearanceTests
{
    private readonly ITestOutputHelper _out;
    public PlanarFeedClearanceTests(ITestOutputHelper o) => _out = o;

    private static readonly GroundedSlab Slab = new(0.9e-3, new EmMaterial(4.4, 0.02));
    private const double W = 254e-6, Len = 3.83e-3, F = 5e9;

    /// <summary>The coupled pair, with the second conductor <paramref name="heights"/> × h away edge
    /// to edge, and a port at each end of it only when <paramref name="driven"/>.</summary>
    private static (PlanarProblem Problem, PlanarMesh Mesh, IReadOnlyList<PlanarPortResolution> Ports)
        Pair(double heights, bool driven)
    {
        double gap = heights * Slab.HeightM;
        var problem = PlanarLineFixtures.Problem(Slab, F,
            PlanarLineFixtures.Rect(0, 0,         Len, W),
            PlanarLineFixtures.Rect(0, W + gap,   Len, W + gap + W));
        var mesh = SurfaceMesher.Mesh(problem, PlanarLineFixtures.Coarse).Mesh;

        var ports = new List<PlanarPort>
        {
            new(1, new EmPoint(0,   0.5 * W), PlanarPortSide.MinX, 50.0),
            new(2, new EmPoint(Len, 0.5 * W), PlanarPortSide.MaxX, 50.0),
        };
        if (driven)
        {
            ports.Add(new PlanarPort(3, new EmPoint(0,   W + gap + 0.5 * W), PlanarPortSide.MinX, 50.0));
            ports.Add(new PlanarPort(4, new EmPoint(Len, W + gap + 0.5 * W), PlanarPortSide.MaxX, 50.0));
        }
        return (problem, mesh, PlanarPorts.ResolveAll(mesh, ports));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-pcal2-4 — ONE GEOMETRY, TWO ANSWERS, BECAUSE THE NEIGHBOUR'S CLASS IS PART OF THE QUESTION
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Gate 2b — the same metal at the same separation passes with a passive neighbour and is
    /// refused with a driven one.</b> Without this the two thresholds are untested AS two: a single
    /// number would make both cases agree, and whichever number it was would be wrong about one of
    /// them (PCAL1 measured the requirement at ≈ 2 h passive against ≈ 4-5.5 h driven).
    ///
    /// <para>2.5 substrate heights is deliberately between the two shipped thresholds and clear of
    /// both — above PCAL1's measured passive requirement of ≈ 2.1 h on this stackup, and well under
    /// the 5 a driven neighbour needs.</para>
    /// </summary>
    [Fact]
    public void TheSameSeparationPassesPassiveAndIsRefusedDriven()
    {
        var (pprob, pm, pp) = Pair(2.5, driven: false);
        var passive  = PlanarSolve.Run(pprob, pm, pp, [F]);   // must not throw
        _out.WriteLine("passive, 2.5 h: ran, " + passive.UnknownCount + " unknowns");

        var margin = passive.FeedClearances[0];
        Assert.Equal(PlanarNeighbourClass.Passive, margin.Neighbour);
        Assert.False(margin.Breached);
        Assert.Equal(2.0, margin.RequiredHeights, 6);
        Assert.Equal(2.5, margin.Heights, 3);
        _out.WriteLine("  " + margin.Margin());

        var (dprob, dm, dp) = Pair(2.5, driven: true);
        var ex = Assert.Throws<PlanarFeedClearanceRefusedException>(
            () => PlanarSolve.Run(dprob, dm, dp, [F]));
        _out.WriteLine("driven, 2.5 h: " + ex.Message);

        Assert.All(ex.Breaches, b => Assert.Equal(PlanarNeighbourClass.Driven, b.Neighbour));
        Assert.Equal(5.0, ex.Breaches[0].RequiredHeights, 6);
        Assert.Contains("port 1", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// R-pcal2-1 — the refusal names the port and the distance, and it says both of the two things a
    /// user can actually do about it. A refusal whose remedy is unreachable is the defect R-pcal2-3
    /// exists to fix, so the remedies are asserted rather than left to prose.
    /// </summary>
    [Fact]
    public void TheRefusalNamesThePortTheDistanceAndBothWaysOut()
    {
        var (problem, mesh, ports) = Pair(0.27, driven: true);   // the series' own 246 µm
        var ex = Assert.Throws<PlanarFeedClearanceRefusedException>(
            () => PlanarSolve.Run(problem, mesh, ports, [F]));
        _out.WriteLine(ex.Message);

        Assert.Equal(4, ex.Breaches.Count);
        Assert.Contains("port 1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("243 µm", ex.Message, StringComparison.Ordinal);      // 0.27 h on this slab
        Assert.Contains("de-embedding OFF", ex.Message, StringComparison.Ordinal);
        Assert.Contains("outside the calibration's validity", ex.Message, StringComparison.Ordinal);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-pcal2-2 / R-pcal2-3 — the two ways past it
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TheOverrideRuns_AndTheBREACHIsStillOnTheResultForTheFileWriterToFind()
    {
        var (problem, mesh, ports) = Pair(0.27, driven: true);
        var r = PlanarSolve.Run(problem, mesh, ports, [F],
            new PlanarSolveSettings(DeembedOutsideCalibrationValidity: true));

        Assert.Equal(4, r.FeedClearances.Count);
        Assert.All(r.FeedClearances, c => Assert.True(c.Breached));

        // It is not silent: the run says so, in the same words the .snp's provenance line will.
        string note = Assert.Single(r.Notes, n => n.Contains("OUTSIDE", StringComparison.Ordinal));
        _out.WriteLine(note);
    }

    /// <summary>
    /// R-pcal2-3 — with de-embedding off there is no calibration standard, nothing is being replaced
    /// by an isolated line, and a neighbour is simply part of the structure. So the check does not
    /// apply rather than being suppressed, and the run carries no clearance report at all.
    /// </summary>
    [Fact]
    public void WithDeembeddingOffTheQuestionDoesNotArise()
    {
        var (problem, mesh, ports) = Pair(0.27, driven: true);
        var r = PlanarSolve.Run(problem, mesh, ports, [F],
            new PlanarSolveSettings(Deembed: false));

        Assert.Empty(r.FeedClearances);
        Assert.Contains(r.Notes, n => n.Contains("De-embedding is OFF", StringComparison.Ordinal));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // R-pcal2-5 — the margin, on every de-embedded port, breached or not
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EveryDeembeddedPortReportsItsMarginInSubstrateHeights()
    {
        var (problem, mesh, ports) = Pair(8.0, driven: true);
        var r = PlanarSolve.Run(problem, mesh, ports, [F]);

        Assert.Equal(4, r.FeedClearances.Count);
        foreach (var c in r.FeedClearances)
        {
            _out.WriteLine(c.Margin());
            Assert.False(c.Breached);
            Assert.Equal(8.0, c.Heights, 3);
            Assert.Contains("substrate heights", c.Margin(), StringComparison.Ordinal);
            Assert.Contains(c.Margin(), r.Notes);
        }
    }

    /// <summary>An isolated line has no neighbour at all, and "none" is an answer rather than a
    /// blank — the note says so instead of leaving the reader to infer it from silence.</summary>
    [Fact]
    public void AnIsolatedLineSaysSoRatherThanSayingNothing()
    {
        var problem = PlanarLineFixtures.Fr4Line(12e-3, 10e9);
        var (mesh, ports) = PlanarLineFixtures.MeshAndPorts(problem);
        var r = PlanarSolve.Run(problem, mesh, ports, [10e9]);

        var c = r.FeedClearances[0];
        Assert.Equal(PlanarNeighbourClass.None, c.Neighbour);
        Assert.False(c.Breached);
        _out.WriteLine(c.Margin());
        Assert.Contains("no other conductor", c.Margin(), StringComparison.Ordinal);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // What is NOT a neighbour — the two ways this check could refuse a design that is fine
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>A flare on the port's OWN net is not a neighbour</b>, and this is the case that would make
    /// the refusal refuse every taper in the repository. R-fed-1 grows a collinear uniform lead and
    /// peels it exactly; PCAL1 measured a port sitting on a pad that way passive at every frequency
    /// with this check silent. The clearance threshold is larger than the lead R-fed-1 grows, so
    /// without the own-net rule the DUT's own flare falls inside the scanned region by construction.
    /// </summary>
    [Fact]
    public void TheStructuresOwnFlareIsNotANeighbour()
    {
        var slab  = GroundedSlab.Fr4Starter;
        var taper = PlanarLineFixtures.Taper(slab, 2.9e-3, 8e-3, 20e-3, 10e9, segments: 24);
        var ports = PlanarLineFixtures.EndPorts(taper);
        var grown = PlanarFeedExtension.Extend(taper, ports).Problem;
        var mesh  = SurfaceMesher.Mesh(grown, PlanarLineFixtures.Coarse).Mesh;

        foreach (var p in PlanarPorts.ResolveAll(mesh, ports))
        {
            var c = PlanarPorts.MeasureFeedClearance(
                mesh, p, PlanarPorts.ResolveAll(mesh, ports),
                endRunM: 3 * slab.HeightM,
                drivenRequiredM: 5 * slab.HeightM, passiveRequiredM: 2 * slab.HeightM,
                slabHeightM: slab.HeightM);
            _out.WriteLine(c!.Margin());
            Assert.Equal(PlanarNeighbourClass.None, c.Neighbour);
        }
    }

    /// <summary>
    /// <b>A cell no rooftop pairs with carries no current, so it is not a conductor.</b> Measured
    /// rather than reasoned about: the conformal mesh of the Klopfenstein taper the ceiling tests
    /// run on leaves 32 single cut cells with no basis — slivers of the taper's own metal that
    /// R-cut-4 declines to drive — and counting them as one-cell conductors refused that taper at
    /// 0.13 substrate heights from its own artwork.
    /// </summary>
    [Fact]
    public void ACellWithNoBasisIsNotAConductor()
    {
        var problem = PlanarLineFixtures.Fr4Line(12e-3, 10e9);
        var mesh = SurfaceMesher.Mesh(problem, PlanarLineFixtures.Coarse).Mesh;
        var conn = PlanarConductors.Of(mesh);

        int carrying = 0;
        for (int i = 0; i < mesh.Cells.Count; i++) if (conn.CarriesCurrent(i)) carrying++;
        Assert.Equal(mesh.Cells.Count, carrying);            // a Manhattan line drives all of it

        // …and a cell index past the end is neither current-carrying nor a crash.
        Assert.False(conn.CarriesCurrent(mesh.Cells.Count));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // The pre-PCAL2 one-threshold overload still means what it meant
    // ══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TheOneThresholdOverloadStillWarnsWithTheMeasuredDistance()
    {
        var (_, mesh, ports) = Pair(0.27, driven: false);
        string? warn = PlanarPorts.CheckFeedClearance(mesh, ports[0], 3 * Slab.HeightM);
        Assert.NotNull(warn);
        Assert.Contains("Port 1", warn, StringComparison.Ordinal);
        _out.WriteLine(warn!);

        var (_, clear, clearPorts) = Pair(8.0, driven: false);
        Assert.Null(PlanarPorts.CheckFeedClearance(clear, clearPorts[0], 3 * Slab.HeightM));
    }
}
