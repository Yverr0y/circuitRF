using System;
using System.Linq;
using System.Numerics;
using CircuitRF.Render.DataDisplay;
using RfCore;
using RfCore.Data;
using Xunit;

namespace CircuitRF.Ui.Tests.DataDisplay;

/// <summary>
/// Owner report, 2026-09-08: the <c>wsp</c> matrix's <c>row</c>/<c>col</c> read 0-based everywhere a
/// user sees them — the trace card offered <c>mag(SP1.wsp[:, 0, 0])</c> for the first element, and
/// the Y-axis label read <c>SP1.wsp(row=1,col=1)</c>.
///
/// <para><b>Both were wrong for the same reason.</b> <c>WspCubePacker</c> writes those axes with
/// 1-based VALUES (<c>k + 1</c>) precisely so the reference document's <c>wsp(r, c)</c> needs no
/// index arithmetic — but only <c>i</c>/<c>j</c>, the S/Y/Z port axes, were being read that way. A
/// matrix element is read positionally in RF and the axis names carry nothing, so <c>row</c>/<c>col</c>
/// now join them in all three places a user meets them: the spec text, the typed slice, and the
/// label.</para>
/// </summary>
public class WspMatrixIndexingTests
{
    /// <summary>A minimal <c>wsp</c>-shaped cube: {freq, row, col} with 1-based row/col values.</summary>
    private static DataSet OneProbeWsp()
    {
        var freq = new Axis("freq", [1e9, 2e9], "Hz");
        var rc   = new double[] { 1, 2 };
        var vals = new Complex[2 * 2 * 2];
        for (int f = 0; f < 2; f++)
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 2; c++)
                    vals[(f * 2 + r) * 2 + c] = new Complex(10 * (r + 1) + (c + 1), 0);

        var ds = new DataSet();
        ds.Add("SP1.wsp", new DataCube([freq, new Axis("row", rc), new Axis("col", (double[])rc.Clone())], vals));
        return ds;
    }

    [Fact]
    public void TypedSpec_ReadsRowAndCol_OneBased()
    {
        var ds = OneProbeWsp();

        Assert.True(CubeTraceSpecParser.TryParse("mag(SP1.wsp[:, 2, 1])", ds,
                                                 out string? cube, out var slice, out _, out string? err), err);
        Assert.Equal("SP1.wsp", cube);
        Assert.Equal(1, slice!.First(s => s.AxisName == "row").Index);   // wsp(2,1) -> index 1
        Assert.Equal(0, slice!.First(s => s.AxisName == "col").Index);
    }

    [Fact]
    public void IndexZero_IsRefused_BecauseThereIsNoRowZero()
    {
        var ds = OneProbeWsp();

        Assert.False(CubeTraceSpecParser.TryParse("mag(SP1.wsp[:, 0, 1])", ds,
                                                  out _, out _, out _, out string? err));
        Assert.Contains("row", err);
    }

    [Fact]
    public void SpecShorthand_AndAxisLabel_AreBothOneBasedAndPositional()
    {
        var ds = OneProbeWsp();
        Assert.True(CubeTraceSpecParser.TryParse("mag(SP1.wsp[:, 2, 1])", ds,
                                                 out string? cube, out var slice, out var transform, out string? err), err);

        var t = new Trace(new SNP([1e9], 2), MatrixType.S, 0, 0, DependentVarFormat.Db)
        {
            CubeName  = cube,
            Slice     = slice,
            Transform = transform,
        };

        // The card's own round-trip: what the picker would write for this trace.
        Assert.Equal("mag(SP1.wsp[:, 2, 1])", t.BuildPickerExpression());

        // The plot's Y-axis label: positional, exactly as S(2,1) is — never "row=2,col=1".
        string label = TraceLabeler.ComputeMinimalLabels([t]).Single();
        Assert.Equal("SP1.wsp(2,1) Mag", label);
        Assert.DoesNotContain("row=", label);
        Assert.DoesNotContain("col=", label);
    }
}
