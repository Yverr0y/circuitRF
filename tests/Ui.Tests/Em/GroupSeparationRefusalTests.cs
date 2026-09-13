// PCAL6 — "which calibration separation a grouped port uses".
// `docs/sonnet-briefs/brief-portcal-6-separation-selection.md`; findings in
// `src/Engine/Mom/RESOLVED.md`, §PCAL6.
//
// The decision, the ladder that produced it and the unit-level gates are
// `Engine.Tests/Mom/PlanarGroupSeparationTests.cs`. What lives here is the one thing none of those
// can see: the owner's own file shape — a four-port grouped board, a DECADE band, and 0 Hz in it —
// resolved exactly as the Simulate button resolves it and run through EmRunService, so the setup
// guard, the per-frequency refusal, LF1's DC split and LF3's two recoveries are all exercised
// together on one run.

using CircuitRF.Design.Layout.Em;
using CircuitRF.Ui.Layout.Em;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests.Em;

public sealed class GroupSeparationRefusalTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _results = Path.Combine(
        Path.GetTempPath(), "crf-pcal6-" + Guid.NewGuid().ToString("N")[..12], "results");

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

    /// <summary>
    /// <b>The owner's sweep shape, on the series' own four-port grouped fixture: 0 Hz and a decade
    /// of band.</b> Every wall this area has hit in the last week is on this one run — the DC point
    /// is a conduction solve (LF1), the sub-floor points take its answer (LF2), a severed mesh or a
    /// dense de-embedding ceiling re-meshes or turns the accelerator on (LF3), and the setup guard
    /// asks the group's electrostatics at every requested frequency (PCAL6/R-pcal6-7). It is the
    /// only gate that sees them interact.
    ///
    /// <para><b>At this mesh it REFUSES at 200 MHz, and that is the gated outcome.</b> PCAL6/M1
    /// measured that the refusal is about the instrument rather than the metal — the modes are
    /// 4.7° apart by the group's own electrostatics and read 0.19° through a short standard
    /// carrying 1.5° of phase — and PCAL6/M3 then measured that the one available remedy makes the
    /// published s-parameters worse. So the refusal stands, and what it owes the user is the PAIR of
    /// numbers that says which kind of refusal it is. That is what is asserted.</para>
    /// </summary>
    [Fact]
    public void TheOwnersSweepShape_ADecadeWithZeroHzOnAGroupedFourPortBoard_RefusesAndSaysWhy()
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
        // The coarsest mesh that still resolves the ports, for ModalErrorBoxTests' own recorded
        // reason: what is gated here is a set of DECISIONS, and the mesh moves only the clock.
        s.PlanarMesh = s.PlanarMesh with { CellsPerWavelength = 2, EdgeMesh = false };

        var run = EmRunService.Run(s, r.Source!, _results);
        output.WriteLine(run.Error ?? "(no error)");

        Assert.Equal(EmRunStatus.Refused, run.Status);
        Assert.NotNull(run.Error);

        // The pair of numbers, which is the whole diagnostic: what was measured, what the
        // cross-section says the same quantity is, and the standard it was measured on.
        Assert.Contains("not separable", run.Error!, StringComparison.Ordinal);
        Assert.Contains("electrostatics puts the same quantity at", run.Error!, StringComparison.Ordinal);
        Assert.Contains("short standard of", run.Error!, StringComparison.Ordinal);

        // Nothing was published. A partially-correct modal de-embedding that publishes is the
        // failure the whole PCAL series exists to remove (R-pcal4-6).
        Assert.Null(run.SnpPath);
    }
}
