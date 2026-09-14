// PCAL7 — "the mode-separation refusal is drawn on the wrong quantity", and it is not.
// `docs/sonnet-briefs/brief-portcal-7-separation-gate.md`; findings in
// `src/Engine/Mom/RESOLVED.md`, §PCAL7. It began life as PCAL6's GroupSeparationRefusalTests and
// changed name with the answer: what is gated here is no longer only THAT this shape refuses, but
// that the refusal names a remedy which actually works on it.
//
// The decision, the two gates' structural independence and the unit-level checks are
// `Engine.Tests/Mom/PlanarGroupSeparationTests.cs`. What lives here is the one thing none of those
// can see: the owner's own file shape — a four-port grouped board, a DECADE band, and 0 Hz in it —
// resolved exactly as the Simulate button resolves it and run through EmRunService, so the setup
// guard, the per-frequency refusal, LF1's DC split and LF3's two recoveries are all exercised
// together on one run.

using CircuitRF.Design.Layout.Em;
using CircuitRF.Ui.Layout.Em;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests.Em;

public sealed class GroupSeparationRemedyTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _results = Path.Combine(
        Path.GetTempPath(), "crf-pcal7-" + Guid.NewGuid().ToString("N")[..12], "results");

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_results)!, true); } catch { /* best effort */ }
    }

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "circuitRF.slnx")))
            dir = Path.GetDirectoryName(dir) ?? "";
        return dir;
    }

    /// <summary>The fixture and the owner's own sweep shape, with the mesh left to the caller —
    /// which is the variable PCAL7 found mattered most on this run.</summary>
    private (EmSetup Setup, EmLayoutSource Source) OwnersRun(bool edgeMesh)
    {
        string portcal = Path.Combine(RepoRoot(), "testdata", "portcal");
        string cem     = Path.Combine(portcal, "coupled-pair", "em", "coupled-pair.cem");

        var setup = EmSetupPersistence.LoadFromFile(cem);
        var r = EmSetupResolver.Resolve(cem, setup.LayoutRef, Path.Combine(portcal, ".cws"),
                                        new TechnologyCache());
        Assert.NotNull(r.Source);

        var s = setup.Clone();
        // 0, 100, 200 … 1000 MHz — the owner's own sweep shape.
        s.Frequency = new CircuitRF.Core.Design.FrequencySpec(
            "0", "1", 11, CircuitRF.Core.Design.SweepKind.Linear, "GHz", "GHz");
        s.PlanarMesh = s.PlanarMesh with { CellsPerWavelength = 2, EdgeMesh = edgeMesh };
        return (s, r.Source!);
    }

    /// <summary>
    /// <b>At a mesh with no edge refinement this shape REFUSES at 200 MHz, and the refusal now names
    /// the remedy that actually moves it.</b>
    ///
    /// <para>PCAL7 scored this exact run against the cross-section oracle with the floor lifted, and
    /// the refusal is not over-caution: at this mesh the de-embedded answer is max |ΔS| <b>0.994</b>
    /// — against an A-vs-B agreement floor, measured on the same geometry with the conductors 9 mm
    /// apart, of <b>0.995</b>. Nothing at this discretisation is measurable, which is why the
    /// measured separation collapses while the group's own electrostatics still reads <b>4.701°</b>:
    /// it is the STANDARDS that could not resolve the modes.</para>
    ///
    /// <para><b>And the mesh is the lever.</b> The same sweep with the edge mesh on publishes — so
    /// the remedy the message names is one that works, which is R-pcal7-7. The old spelling
    /// ("separate the feeds") does not apply here and is now reserved for the case it is about: a
    /// cross-section whose modes are genuinely degenerate, which the SETUP guard refuses before a
    /// standard is solved.</para>
    ///
    /// <para><b>CL3 MOVED THE MEASURED FIGURES AND THIS COMMENT NOW SAYS WHICH ARE WHICH.</b> The
    /// conductor-loss series made the metal real, and a mode separation is |Δγ|·Δℓ with γ complex,
    /// so it moves — and not monotonically (`RESOLVED.md` §CL3 §5). Re-measured here: this run's
    /// separation is <b>0.096°</b>, where PCAL7 recorded 0.19° on PEC metal. <b>The remaining PCAL7
    /// numbers above — the two |ΔS| figures, and the 2.94° the edge-meshed run used to read — are
    /// PEC-metal measurements that have NOT been re-taken</b>, because doing so means running the
    /// owner's board and that is <see cref="TheSameSweepWithTheEdgeMeshOn_Publishes"/>, which is
    /// tagged. They are kept as the PCAL7 record rather than quietly restated as current. Nothing
    /// user-facing quotes them: the refusal's own sizes come off the unit fixture and are gated by
    /// <c>PlanarGroupSeparationTests.TheBandIsALever_AndTheRefusalQuotesTheMeasuredSizes</c>.</para>
    /// </summary>
    [Fact]
    public void TheOwnersSweepShape_AtAMeshTooCoarseToResolveTheModes_RefusesAndNamesTheMeshAndTheBand()
    {
        var (s, source) = OwnersRun(edgeMesh: false);
        var run = EmRunService.Run(s, source, _results);
        output.WriteLine(run.Error ?? "(no error)");

        Assert.Equal(EmRunStatus.Refused, run.Status);
        Assert.NotNull(run.Error);

        // The pair of numbers, which is what says which kind of refusal this is: what was measured,
        // what the cross-section says the same quantity is, and the standard it was measured on.
        Assert.Contains("not separable", run.Error!, StringComparison.Ordinal);
        Assert.Contains("electrostatics puts the same quantity at", run.Error!, StringComparison.Ordinal);
        Assert.Contains("short standard of", run.Error!, StringComparison.Ordinal);

        // R-pcal7-7 — the remedy that binds. Both levers are named and the feeds are not the first
        // of them, because the electrostatic figure here is nine times the floor.
        //
        // **It names the two levers and no longer names a DIRECTION.** It used to say "narrowing the
        // sweep", measured on a PEC board at PCAL7; re-measured on real metal the sign is not fixed —
        // with the edge mesh on, narrowing goes the other way (PlanarGroupSeparationTests'
        // TheEdgeMeshMovesTheSameSeparationTheOtherWay). This file's own next test is the same point
        // from the other side, and it is why the assertion follows the message rather than the
        // recommendation.
        Assert.Contains("Your metal is not the problem", run.Error!, StringComparison.Ordinal);
        Assert.Contains("moving either band edge", run.Error!, StringComparison.Ordinal);
        Assert.Contains("NOT A RULE", run.Error!, StringComparison.Ordinal);
        Assert.Contains("standards' own MESH", run.Error!, StringComparison.Ordinal);

        // Nothing was published. A partially-correct modal de-embedding that publishes is the
        // failure the whole PCAL series exists to remove (R-pcal4-6).
        Assert.Null(run.SnpPath);
    }

    /// <summary>
    /// <b>The same file, the same decade band, the same 0 Hz — with the edge mesh on it publishes.</b>
    /// This is the other half of the sentence the refusal above prints, and it is here so that
    /// remedy is a tested claim rather than an assertion in a message.
    ///
    /// <para><c>Category=Benchmark</c> because it is ~27 s in Release and more under a test build:
    /// eleven de-embedded points whose calibration standards are 171 mm of line at the band's
    /// bottom. The refusal above is the routine gate; this is the one that proves the way out of
    /// it.</para>
    /// </summary>
    [Fact]
    [Trait("Category", "Benchmark")]
    public void TheSameSweepWithTheEdgeMeshOn_Publishes()
    {
        var (s, source) = OwnersRun(edgeMesh: true);
        var run = EmRunService.Run(s, source, _results);
        foreach (string n in run.Notes ?? [])
            if (n.Contains("separation", StringComparison.Ordinal)) output.WriteLine(n);

        Assert.Equal(EmRunStatus.Ok, run.Status);
        Assert.NotNull(run.SnpPath);
        Assert.True(File.Exists(run.SnpPath!));

        // The point that refuses at the coarser mesh, reported with both numbers beside each other.
        Assert.Contains(run.Notes ?? [], n => n.Contains("200 MHz, ports 1+3", StringComparison.Ordinal)
                                           && n.Contains("electrostatics", StringComparison.Ordinal));
    }
}
