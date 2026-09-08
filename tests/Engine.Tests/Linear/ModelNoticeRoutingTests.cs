using CircuitRF.Core.Elaboration;
using CircuitRF.Core.Netlist;
using CircuitRF.Engine;

namespace CircuitRF.Engine.Tests.Linear;

/// <summary>
/// A resistor's non-physical-value notice (R &lt; 0, R = 0) goes through the keyed warning list,
/// not the console.
///
/// <para><b>Why.</b> <c>ResistorModel</c> and <c>ParallelRlcModel</c> wrote the notice to
/// <c>Console.Error</c> once per INSTANCE. Under the frequency-parallel path (SP-P3) a run
/// elaborates one copy of the netlist per worker, so a 251-point sweep of a negative-resistance
/// fixture printed the same line three times — and it never reached <c>ElaboratedNetlist.Warnings</c>
/// at all, so the Messages pane and <c>--json</c> never saw it. Routed through
/// <c>IReportsWarnings</c> it is drained after the stamp, keyed per instance path, and the merge
/// that folds each copy's diagnostics back into the caller's netlist dedupes it.</para>
/// </summary>
public class ModelNoticeRoutingTests
{
    private const string NegativeR = """
        Term:T1  p1 0  Num=1 Z=50 Ohm
        L:L1     p1 n  L=2 nH
        R:R2     n  0  R=-100 Ohm
        PRLC:P1  n  0  R=0 Ohm L=1 nH C=1 pF
        """;

    [Fact]
    public void NegativeAndZeroResistance_ReportOnce_InTheWarningsList_UnderTheParallelPath()
    {
        var (lib, tb) = new CnlReader().Read(NegativeR);
        using var nl  = new Elaborator(lib).Elaborate(tb);
        var freqs     = Enumerable.Range(0, 256).Select(i => 1e9 + i * 1e7).ToArray();

        Assert.True(SParameterEngine.PlanDegree(nl, freqs.Length, 3) > 1, "the parallel path was not taken");
        SParameterEngine.Run(nl, lib, tb, baseDirectory: null, freqs, settings: null, control: null,
                             maxDegreeOfParallelism: 3);

        Assert.Equal(1, nl.Warnings.Count(w => w.Contains("R:R2") && w.Contains("R=-100")));
        Assert.Equal(1, nl.Warnings.Count(w => w.Contains("PRLC:P1") && w.Contains("R=0")));
    }

    [Fact]
    public void TheSameNotice_ReachesTheWarningsList_OnTheSerialPath()
    {
        var (lib, tb) = new CnlReader().Read(NegativeR);
        using var nl  = new Elaborator(lib).Elaborate(tb);
        SParameterEngine.Run(nl, [1e9, 2e9, 3e9]);

        Assert.Equal(1, nl.Warnings.Count(w => w.Contains("R:R2") && w.Contains("non-physical/active")));
        Assert.Equal(1, nl.Warnings.Count(w => w.Contains("PRLC:P1") && w.Contains("Gmax")));
    }
}
