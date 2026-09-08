// ================================================================
//  LoadpullRunFindings.cs — what the SHAPE of a loadpull result says about the run.
//
//  AUT-9 R-aut9-4, R-aut9-5, R-aut9-6. Three observations from one exercise, all of them things a
//  human reads off the log and a machine caller cannot:
//
//    • A bench whose device was inert returned Pout = -300 dBm — the engine's floor sentinel, not a
//      measurement — at all 56 drive points, and exited `status: ok` with no diagnostic. On that
//      evidence a client wrote up a working component as defective.
//    • A loadpull that converged at no grid point returned `status: not-converged` and an EMPTY
//      diagnostics array, while stderr carried a full per-grid-point account.
//    • Every one of 44 points then failed at the FIRST real drive step, 30 dB above the tickle.
//      Lowering PinStart by 25 dB made the identical bench converge in 38 s.
//
//  Nothing here computes anything about the circuit. Every number below was already in the result;
//  what was missing was somebody willing to look at it and say so.
//
//  WHY RfCore: the same reason LoadpullResultSummary lives here (R-aut1-9) — a protocol adapter has
//  to reach this without going through a console program, and it reads nothing but DataCube.
// ================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using RfCore.Data;

namespace RfCore.Loadpull
{
    /// <summary>What a finding IS, as an id a caller keys on rather than a sentence it matches.</summary>
    public enum LoadpullFindingKind
    {
        /// <summary>Every converged drive step delivered the engine's floor power. R-aut9-4.</summary>
        DeviceInert,

        /// <summary>No grid point converged a single real drive step. R-aut9-5.</summary>
        NothingConverged,

        /// <summary>The first drive step sits far above the tickle, on a run where nothing past the
        /// tickle converged. R-aut9-6.</summary>
        TickleGap,
    }

    /// <param name="Attempted">Grid points the run walked.</param>
    /// <param name="Converged">Grid points that converged at least one non-tickle drive step.</param>
    /// <param name="DriveSteps">Non-tickle drive steps attempted across the whole grid.</param>
    /// <param name="TickleDbm">The tickle level, or NaN when the run had none.</param>
    /// <param name="FirstDriveDbm">The first non-tickle rung, or NaN.</param>
    /// <param name="StopCodes">How the grid points stopped, by the summary's own stop names.</param>
    public sealed record LoadpullRunFinding(
        LoadpullFindingKind Kind,
        int                 Attempted,
        int                 Converged,
        int                 DriveSteps,
        double              TickleDbm,
        double              FirstDriveDbm,
        IReadOnlyDictionary<string, int> StopCodes);

    /// <summary>
    /// Reads a loadpull or pursuit <see cref="DataSet"/> and reports the states that a caller cannot
    /// tell from a successful run by looking at <c>status</c>.
    /// </summary>
    public static class LoadpullRunFindings
    {
        /// <summary>
        /// The output power below which the engine's own dBm conversion returns its floor sentinel
        /// (<c>LoadpullEngine.ToDbm</c>: <c>w &gt; 1e-30</c>). A step at or under this delivered
        /// nothing; <c>-300 dBm</c> is what a reader sees, and it is not a measurement.
        /// </summary>
        public const double FloorWatts = 1e-30;

        /// <summary>
        /// How far above the tickle a first drive step has to sit before it is worth naming as the
        /// likely cause of a blanket non-convergence, in dB.
        ///
        /// <para><b>It is not a rule about good practice.</b> The tickle is DESIGNED to sit tens of
        /// dB below PinStart — the default pair is -50 and -20 — so a warning that fired on the gap
        /// alone would fire on every loadpull ever run and mean nothing. It fires only together with
        /// "nothing past the tickle converged", where a large gap is the first thing to try: the
        /// exercise's own 30 dB jump converged in 38 s once PinStart moved to -45.</para>
        /// </summary>
        public const double TickleGapDb = 15.0;

        /// <summary>
        /// Every finding this result supports, in the order they are worth reading. Empty for an
        /// ordinary run, which is the common case and costs one pass over two cubes.
        /// </summary>
        public static IReadOnlyList<LoadpullRunFinding> For(DataSet ds)
        {
            var findings = new List<LoadpullRunFinding>();

            foreach (string group in ds.Groups)
            {
                var cubes = ds.CubesIn(group);
                if (!LoadpullResultSummary.HasGrid(cubes)) continue;

                var conv   = Real(cubes, "Converged");
                var tickle = Real(cubes, "IsTickle");
                var pavl   = Real(cubes, "PavlDbm");
                var stop   = Real(cubes, "StopCode");
                if (conv is null || stop is null) continue;

                // Pout in WATTS — the engine's own cube, or Enrich's renamed copy of it. The dBm
                // spelling is NaN wherever the power is non-positive, so it cannot tell "delivered
                // nothing" from "never ran"; the watt cube can.
                var poutW = Real(cubes, "Pout_W") ?? Real(cubes, "Pout");

                int nPin = conv.Axes.Count > 0 ? conv.Axes[^1].Length : 1;
                if (nPin <= 0) continue;
                int nPoints = conv.RealValues.Length / nPin;
                if (nPoints <= 0) continue;

                var convV   = conv.RealValues;
                var tickleV = tickle?.RealValues;
                var pavlV   = pavl?.RealValues;
                var poutV   = poutW?.RealValues;

                int  pointsConverged = 0, driveSteps = 0, atFloor = 0, convergedDrives = 0;
                double tickleDbm = double.NaN, firstDriveDbm = double.NaN;

                for (int p = 0; p < nPoints; p++)
                {
                    bool any = false;
                    for (int k = 0; k < nPin; k++)
                    {
                        int idx     = p * nPin + k;
                        bool isTick = tickleV is not null && tickleV[idx] != 0.0;

                        if (pavlV is not null)
                        {
                            if (isTick && double.IsNaN(tickleDbm))          tickleDbm     = pavlV[idx];
                            if (!isTick && double.IsNaN(firstDriveDbm))     firstDriveDbm = pavlV[idx];
                        }

                        if (isTick) continue;
                        driveSteps++;
                        if (convV[idx] == 0.0) continue;

                        any = true;
                        convergedDrives++;
                        if (poutV is not null && poutV[idx] <= FloorWatts) atFloor++;
                    }
                    if (any) pointsConverged++;
                }

                var stopCodes = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (double code in stop.RealValues)
                {
                    string name = LoadpullResultSummary.StopName(code);
                    stopCodes[name] = stopCodes.GetValueOrDefault(name) + 1;
                }

                LoadpullRunFinding Make(LoadpullFindingKind kind) => new(
                    kind, nPoints, pointsConverged, driveSteps, tickleDbm, firstDriveDbm, stopCodes);

                // R-aut9-4. Converged everywhere and delivered nothing anywhere: the floor is not a
                // measurement, and a run reporting it at EVERY rung is describing a device that is
                // off, not one that is bad.
                if (convergedDrives > 0 && atFloor == convergedDrives && poutV is not null)
                    findings.Add(Make(LoadpullFindingKind.DeviceInert));

                // R-aut9-5. `status` alone is not a diagnosis; the counts and the stop distribution
                // are what the stderr log carried and the document did not.
                if (pointsConverged == 0 && driveSteps > 0)
                {
                    findings.Add(Make(LoadpullFindingKind.NothingConverged));

                    // R-aut9-6, and only here — see TickleGapDb for why this is not a standing
                    // warning about the default ladder.
                    if (!double.IsNaN(tickleDbm) && !double.IsNaN(firstDriveDbm) &&
                        firstDriveDbm - tickleDbm > TickleGapDb)
                        findings.Add(Make(LoadpullFindingKind.TickleGap));
                }
            }

            return findings;
        }

        private static DataCube? Real(IReadOnlyDictionary<string, DataCube> cubes, string name)
            => cubes.TryGetValue(name, out var c) && c.DataKind == DataKind.Real ? c : null;
    }
}
