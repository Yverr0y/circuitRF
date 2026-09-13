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

public sealed class GroupSeparationRecoveryTests(ITestOutputHelper output) : IDisposable
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
    /// <b>The owner's sweep, on the series' own four-port grouped fixture: 0 Hz and a decade of
    /// band.</b> Every wall this area has hit in the last week is on this one run — the DC point is
    /// a conduction solve (LF1), the sub-floor points take its answer (LF2), a severed mesh or a
    /// dense de-embedding ceiling re-meshes or turns the accelerator on (LF3), the setup guard asks
    /// the group's electrostatics at every requested frequency (PCAL6/R-pcal6-7), and the bottom of
    /// the band is where the mode-separation measurement fails and the short standard is regrown
    /// (PCAL6/M3). It is the only gate that sees them interact.
    /// </summary>
    [Fact]
    public void TheOwnersSweepShape_ADecadeWithZeroHzOnAGroupedFourPortBoard_Runs()
    {
        string portcal = Path.Combine(RepoRoot(), "testdata", "portcal");
        string cem     = Path.Combine(portcal, "coupled-pair", "em", "coupled-pair.cem");

        var setup = EmSetupPersistence.LoadFromFile(cem);
        var r = EmSetupResolver.Resolve(cem, setup.LayoutRef, Path.Combine(portcal, ".cws"),
                                        new TechnologyCache());
        Assert.NotNull(r.Source);

        var s = setup.Clone();
        // 0, 100, 200 … 1000 MHz — the owner's own §1 row 1, which refused 51.9 s in.
        s.Frequency = new CircuitRF.Core.Design.FrequencySpec(
            "0", "1", 11, CircuitRF.Core.Design.SweepKind.Linear, "GHz", "GHz");
        // The coarsest mesh that still resolves the ports, for ModalErrorBoxTests' own recorded
        // reason: what is gated here is a set of DECISIONS, and the mesh moves only the clock.
        s.PlanarMesh = s.PlanarMesh with { CellsPerWavelength = 2, EdgeMesh = false };

        var run = EmRunService.Run(s, r.Source!, _results);
        output.WriteLine(run.Error ?? "(no error)");
        foreach (string n in run.Notes) output.WriteLine("NOTE: " + n);

        Assert.Equal(EmRunStatus.Ok, run.Status);
        Assert.NotNull(run.SnpPath);

        // The two groups formed, and the run says what it did about the short standard rather than
        // asking the user to work it out.
        Assert.Equal(2, run.Notes.Count(n => n.Contains("CALIBRATION GROUP", StringComparison.Ordinal)));
        string recovery = Assert.Single(
            run.Notes, n => n.Contains("SHORT standard", StringComparison.Ordinal));
        Assert.Contains("20° electrical", recovery, StringComparison.Ordinal);

        // 0 Hz is in the written file, with the conduction answer LF1 put there.
        string[] lines = File.ReadAllLines(run.SnpPath!);
        Assert.Contains(lines, l => l.TrimStart().StartsWith('0') && !l.TrimStart().StartsWith("0.", StringComparison.Ordinal));
    }
}
