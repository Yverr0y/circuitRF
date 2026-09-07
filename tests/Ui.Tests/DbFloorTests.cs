using System;
using System.Linq;
using System.Numerics;
using CircuitRF.Ui.DataDisplay;
using Xunit;

namespace CircuitRF.Ui.Tests;

/// <summary>
/// The Data Display's decibel floor (<see cref="DbFloor"/>).
///
/// <para>The fixture is the real shape that motivated it: S(1,1) of an ideal 50 Ω quarter-wave line
/// terminated in 50 Ω, which is analytically ZERO at every frequency. What the solver returns is
/// pure double-precision roundoff — the magnitudes below are the ones circuitRF actually produced
/// for that netlist, exact zeros included.</para>
/// </summary>
public class DbFloorTests
{
    /// <summary>Verbatim |S11| from a 1-10 GHz run of an ideal matched line: roundoff, plus the
    /// exact zeros that a bare log turns into −∞.</summary>
    private static readonly double[] MatchedLineS11Magnitudes =
    [
        6.280369835e-17, 1.899193e-16, 1.937787e-16, 6.280369835e-17, 9.395318e-17,
        5.674090727e-17, 2.708061e-16, 2.092290e-16, 1.078233e-16, 3.926428e-16,
        6.280369835e-17, 5.554736e-15, 1.366631e-15,
        0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
    ];

    [Fact]
    public void AnExactZero_LandsOnTheFloor_NotAtNegativeInfinityOrMinus6000()
    {
        // The old code floored the LINEAR magnitude at 1e-300, which is where -6000 dB came from.
        Assert.Equal(DbFloor.FloorDb, DbFloor.Db20(0.0));
        Assert.Equal(DbFloor.FloorDb, DbFloor.Db10(0.0));
        Assert.True(double.IsFinite(DbFloor.Db20(0.0)));
    }

    [Fact]
    public void dB10AndDb20_ShareOneFloor_RatherThanOneEach()
    {
        // Flooring in linear gave 20·log10 a bottom at -6000 dB and 10·log10 one at -3000 dB —
        // two cliffs for the same data, differing only by the factor in front of the log.
        Assert.Equal(DbFloor.Db20(0.0), DbFloor.Db10(0.0));
        Assert.Equal(DbFloor.Db20(1e-40), DbFloor.Db10(1e-40));
    }

    [Fact]
    public void TheWholeMatchedLineNoiseBand_RendersFlat()
    {
        var db = MatchedLineS11Magnitudes.Select(DbFloor.Db20).ToArray();

        Assert.All(db, v => Assert.Equal(DbFloor.FloorDb, v));
        Assert.Equal(0.0, db.Max() - db.Min());          // a flat line, not a 59 dB jitter band
    }

    [Fact]
    public void Minus300WouldNotHaveBeenEnough_WhichIsWhyTheFloorIsWhereItIs()
    {
        // Guards the choice of value, not the mechanism: 4 of the 13 non-zero roundoff samples
        // above sit ABOVE -300 dB, so a -300 dB floor would still leave visible jitter.
        var raw = MatchedLineS11Magnitudes.Where(m => m > 0).Select(m => 20.0 * Math.Log10(m)).ToArray();

        Assert.Contains(raw, v => v > -300.0);
        Assert.All(raw, v => Assert.True(v < DbFloor.FloorDb,
            $"{v} dB is above the floor — roundoff must sit below it or the floor does not flatten it."));
    }

    [Fact]
    public void RealData_IsUntouched()
    {
        Assert.Equal(-20.0, DbFloor.Db20(0.1), 12);
        Assert.Equal(0.0,   DbFloor.Db20(1.0), 12);
        Assert.Equal(-60.0, DbFloor.Db20(1e-3), 12);
        Assert.Equal(-3.0,  DbFloor.Db10(0.501187233627272), 9);

        // A thermal-noise power in WATTS — kTB at 1 Hz, -204 dB(W) — is why the floor is not -200.
        Assert.True(DbFloor.Db10(4.0e-21) > DbFloor.FloorDb);
    }

    [Fact]
    public void NaN_StillPropagates_SoThePointSkippingStillWorks()
    {
        // Every caller renders a point only when double.IsFinite(y); clamping must not turn a
        // missing sample into a plotted one sitting on the floor.
        Assert.True(double.IsNaN(DbFloor.Db20(double.NaN)));
        Assert.True(double.IsNaN(DbFloor.Db10(double.NaN)));
    }

    [Fact]
    public void AReadout_MarksAFlooredValue_RatherThanPrintingItAsAMeasurement()
    {
        Assert.Equal("≤ -250.00", DbFloor.Format(DbFloor.Db20(0.0), "F2"));
        Assert.Equal("-20.00",    DbFloor.Format(DbFloor.Db20(0.1), "F2"));
    }

    [Fact]
    public void TheFloorMark_ReplacesTheEqualsSign_RatherThanFollowingIt()
    {
        // A marker info box writes "name=value". Gluing "=" to a value that already begins with "≤"
        // produced "dB(S(1,1))=≤ -250.00", which reads as a typo instead of as an inequality.
        Assert.Equal("dB(S(1,1)) ≤ -250.00",
            DbFloor.Label("dB(S(1,1))", DbFloor.Format(DbFloor.Db20(0.0), "F2")));

        // An ordinary value keeps the "=".
        Assert.Equal("dB(S(1,1))=-20.00",
            DbFloor.Label("dB(S(1,1))", DbFloor.Format(DbFloor.Db20(0.1), "F2")));

        // The complex DB readout carries its angle after the value and joins the same way.
        Assert.Equal("S(1,1) ≤ -250.00∠0.0°", DbFloor.Label("S(1,1)", "≤ -250.00∠0.0°"));

        // A label whose value is not a dB reading at all is untouched.
        Assert.Equal("harmonic=3", DbFloor.Label("harmonic", "3"));
        Assert.Equal("freq=NaN",   DbFloor.Label("freq", "NaN"));
    }

    [Fact]
    public void TheDisplayFloor_DoesNotReachTheExportedNumbers()
    {
        // Display-only: a measurement or a .npy/.mat/Touchstone export must not depend on whether
        // the value happened to pass through a plot. RfCore's own dB still bottoms out far below.
        var axis = new RfCore.Data.Axis("f", [1e9, 2e9], "Hz");
        var cube = new RfCore.Data.DataCube([axis], [new Complex(0.0, 0.0), new Complex(1e-30, 0.0)]);
        var exported = cube.DB20();

        Assert.True(exported.RealValues[0] < DbFloor.FloorDb,
            "RfCore's exported dB must stay unclamped — clamping it would change written files.");
    }
}
