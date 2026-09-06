using System;
using System.IO;
using CircuitRF.Core.Elaboration;
using CircuitRF.Core.Netlist;
using CircuitRF.Engine;
using Xunit;

namespace CircuitRF.Engine.Tests.Devices;

/// <summary>
/// <see cref="SParameterEngine"/>'s preliminary port/branch-label pass must stamp at a frequency
/// the caller actually asked for.
///
/// <para><b>What went wrong.</b> That pass exists to read TOPOLOGY — port branch indices and a
/// branch→component-name map — and topology does not depend on ω, so it passed ω=1. But a
/// <c>Stamp</c> produces more than a matrix: a model also WARNS from inside it, naming the
/// frequency it was handed. ω=1 rad/s is 0.159 Hz, which is in no sweep. A <c>TLIN</c>'s electrical
/// length is proportional to frequency, so at 0.159 Hz every ideal line in every design saw θ≈0,
/// reported a resonance at a frequency the user never swept, and — the warning being latched once
/// per instance — CONSUMED the report that a real resonance later in the sweep would have made.
/// Both halves are gated here.</para>
/// </summary>
public class TLineResonanceWarningTests
{
    /// <summary>A quarter-wave 50 Ω line between two matched ports. θ = 90°·(f/1 GHz), so θ is a
    /// multiple of 180° — a genuine open/short degeneracy — exactly at 2, 4, 6, 8, 10 GHz.</summary>
    private const string QuarterWaveLine = """
        Port:Term1  n1  0  Num=1  Z=50 Ohm
        Port:Term2  n2  0  Num=2  Z=50 Ohm
        TLIN:TL1  n1  n2  Z=50 Ohm  E=90 deg  F=1 GHz
        analysis SP1 type=sparam start="1" startUnit=GHz stop="10" stopUnit=GHz npts=101
        """;

    /// <summary>Runs the sweep with stderr captured. The redirect is process-global, so this may
    /// briefly swallow a concurrently-running test's stderr — harmless, since nothing else in this
    /// assembly asserts on stderr, and no other model emits the strings asserted below.</summary>
    private static string StdErrOfRun(double[] freqsHz)
    {
        var (lib, tb) = new CnlReader().Read(QuarterWaveLine);
        var nl   = new Elaborator(lib).Elaborate(tb);
        var sink = new StringWriter();
        var prev = Console.Error;
        try
        {
            Console.SetError(sink);
            SParameterEngine.Run(nl, freqsHz);
        }
        finally { Console.SetError(prev); }
        return sink.ToString();
    }

    [Fact]
    public void ASweepWithNoDegeneracy_WarnsAboutNothing()
    {
        // 1.0 and 1.5 GHz put θ at 90° and 135°; sin θ is nowhere near zero. Before the fix the
        // preliminary pass reported "sin(θ)≈0 at f=0.159155 Hz" here — on every run of every
        // netlist containing an ideal line.
        string err = StdErrOfRun([1.0e9, 1.5e9]);

        Assert.DoesNotContain("sin(θ)≈0", err);
        Assert.DoesNotContain("0.159155", err);
    }

    [Fact]
    public void ARealDegeneracyInTheSweep_IsStillReported_AtAFrequencyTheCallerAsked()
    {
        // θ = 360° at 4 GHz. This warning was previously SUPPRESSED: the phantom 0.159 Hz report
        // had already burned the once-per-instance latch before the sweep began.
        string err = StdErrOfRun([1.0e9, 4.0e9]);

        Assert.Contains("sin(θ)≈0", err);
        Assert.Contains("4E+09 Hz", err);
    }

    [Fact]
    public void TheWarnedFrequency_IsAlwaysOneOfTheSweptFrequencies()
    {
        string err = StdErrOfRun([2.0e9, 3.0e9]);

        // 2 GHz is θ=180° and warns; 0.159155 Hz is the placeholder and must never appear.
        Assert.Contains("2E+09 Hz", err);
        Assert.DoesNotContain("0.159155", err);
    }
}
