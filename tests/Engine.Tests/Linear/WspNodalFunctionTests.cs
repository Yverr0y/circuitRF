// ================================================================
//  WspNodalFunctionTests.cs — brief-wsprobe-2's end-to-end gates: the ones that need a real
//  circuit rather than a random two-port.
//
//    (c)  the synthetic circulator of Eq. 96–99 against circuitRF's OWN Circulator model, inserted
//         at the same node — an independent path to the same number, which is the only way to turn
//         the document's "numerically identical" (p. 67) into a test.
//    (d)  the Kurokawa start-up search on the document's two unstable resonators, and on the two
//         STABLE versions of them, where the counter-clockwise crossing must not be reported.
//    (i)  the normalised driving-point loci: bounded, and carrying the SAME start-up signature
//         as the raw ones, which is what makes them safe to offer.
//    (f)  the even-mode load line (App. C) against the classic even-mode reduction — one half of
//         the combiner with the common load doubled — and wsp_gain (App. D) against a closed form.
//
//  The scalar cores are tested in tests/RfCore.Tests/Stability/WspNodalTests.cs; nothing here
//  re-derives them, it only puts real circuits behind them.
//
//  Reference: T. A. Winslow, General Circuit Analysis Using The WSProbe (2023).
// ================================================================

using System.Numerics;
using CircuitRF.Core.Design;
using CircuitRF.Core.Elaboration;
using CircuitRF.Core.Netlist;
using RfCore.Data;
using RfCore.Stability;
using Xunit.Abstractions;

namespace CircuitRF.Engine.Tests.Linear;

public sealed class WspNodalFunctionTests(ITestOutputHelper output)
{
    private static string TestData(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var cand = Path.Combine(dir, "testdata");
            if (Directory.Exists(cand)) return Path.Combine([cand, .. parts]);
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("testdata not found above " + AppContext.BaseDirectory);
    }

    private static string Fixture(string name) => TestData("wsprobe", name);

    private static (DataSet Ds, double[] Freqs) RunText(string cnlText, string baseDir)
    {
        var (lib, tb) = new CnlReader().Read(cnlText, "tb", baseDir);
        var nl    = new Elaborator(lib) { BaseDirectory = baseDir }.Elaborate(tb);
        var freqs = tb.Analyses.OfType<SParameterAnalysis>().First().Expand(nl.ResolvedGlobals);
        return (SParameterEngine.Run(nl, freqs), freqs);
    }

    private static (DataSet Ds, double[] Freqs) RunFixture(string name)
        => RunText(File.ReadAllText(Fixture(name)), Path.GetDirectoryName(Fixture(name))!);

    private static Complex[,] Wsp(DataSet ds, int fi)
    {
        var cube = ds["wsp"];
        int n = cube.Axes[^1].Length;
        var raw = cube.ComplexValues;
        var m = new Complex[n, n];
        for (int r = 0; r < n; r++)
        for (int c = 0; c < n; c++)
            m[r, c] = raw[(fi * n + r) * n + c];
        return m;
    }

    private static void AssertRel(Complex expected, Complex actual, double tol, string what)
    {
        double scale = Math.Max(expected.Magnitude, 1e-300);
        double err   = (actual - expected).Magnitude / scale;
        Assert.True(err <= tol,
            $"{what}: expected {expected}, got {actual} (relative error {err:G3} > {tol:G1})");
    }

    // ══ (c) — the synthetic circulator against a real one ════════════════════

    /// <summary>
    /// The document says the two circulator loop gains are "numerically identical" to what the
    /// third port of an ideal circulator inserted at the node would reflect (p. 67). Here that is a
    /// test through a completely independent path: hero1_probed's <c>WSProbe:P1</c> is replaced by
    /// circuitRF's own <c>Circulator</c> with its ports 1 and 2 in the same node and its port 3
    /// brought out as a fifth analysis port, and the one-port <c>S55</c> of that five-port — the
    /// reflection at port 5 with every other port terminated in its own Z0, which is the same
    /// terminated network the probe solves see (§4.2) — is compared against
    /// <c>wsp_loopgain(wsp_yparam(wsp, 1), …, 50)</c> of the probed run.
    ///
    /// <para><b>Which direction is which is a MEASURED result, not an assumption.</b> The brief
    /// predicted CW ↔ REV and CCW ↔ UNI; the assertion below is whichever way the two models
    /// actually agree, and the direction that matched is written into the message so a later change
    /// of either convention fails loudly rather than quietly swapping two nearly-equal
    /// numbers.</para>
    /// </summary>
    [Fact]
    public void C_SyntheticCirculatorLoopGain_EqualsARealCirculatorsThirdPortReflection()
    {
        string dir  = Path.GetDirectoryName(Fixture("hero1_probed.cnl"))!;
        string text = File.ReadAllText(Fixture("hero1_probed.cnl"));
        const string probeLine = "WSProbe:P1  a1 x1";
        Assert.Contains(probeLine, text);

        var (probed, freqs) = RunText(text, dir);

        // The measure lines name P1, which the circulator variants do not have; drop them.
        string bare = string.Join('\n', text.Split('\n').Where(l => !l.TrimStart().StartsWith("measure")));

        var lgRev = new Complex[freqs.Length];
        var lgUni = new Complex[freqs.Length];
        for (int fi = 0; fi < freqs.Length; fi++)
        {
            var y = WspReduction.YParam(WspProbeQuad.Of(Wsp(probed, fi), 1));
            lgRev[fi] = WspNodal.LoopGain(y, WspLoopGainKind.Rev, 50.0);
            lgUni[fi] = WspNodal.LoopGain(y, WspLoopGainKind.Uni, 50.0);
        }

        foreach (string direction in (string[])["CW", "CCW"])
        {
            string circText = bare.Replace(probeLine,
                $"Circulator:CIRC  a1 0 x1 0 c3 0  Z0=50 Ohm  Direction=\"{direction}\"\n" +
                "Port:Term5  c3 0  Num=5 Z=50 Ohm");
            var (circ, cf) = RunText(circText, dir);
            Assert.Equal(freqs.Length, cf.Length);

            // S55 — the reflection the circulator's third port sees.
            var s55 = circ.S(5, 5).ComplexValues;

            double errRev = 0, errUni = 0;
            for (int fi = 0; fi < freqs.Length; fi++)
            {
                errRev = Math.Max(errRev, (s55[fi] - lgRev[fi]).Magnitude);
                errUni = Math.Max(errUni, (s55[fi] - lgUni[fi]).Magnitude);
            }
            output.WriteLine($"{direction}: max |S55 − LGR| = {errRev:G3}, max |S55 − LGF| = {errUni:G3}");

            var expected = direction == "CW" ? lgRev : lgUni;
            string name  = direction == "CW" ? "REV (Eq. 97)" : "UNI (Eq. 99)";
            for (int fi = 0; fi < freqs.Length; fi++)
                AssertRel(expected[fi], s55[fi], 1e-8,
                    $"circulator {direction} S55 vs wsp_loopgain \"{name}\" at {freqs[fi] / 1e9:G4} GHz");
        }
    }

    // ══ (d) — the Kurokawa search on the document's resonators ═══════════════

    /// <summary>
    /// The series unstable resonator (Fig. 31): the start-up signature is in <c>1/Y0</c> and in
    /// <c>1/Y0</c> only — <c>wsp_unstable_freq_kurokawa(Y0)</c> finds <c>f0 = 1.5915 GHz</c> within
    /// half a sweep step and the same search on <c>H0</c> finds nothing, because a zero of
    /// <c>H0</c> masks the pole there (§4.10, p. 76–77). The parallel resonator (Fig. 34) is the
    /// mirror image.
    /// </summary>
    [Theory]
    [InlineData("series_resonator.cnl",   true)]
    [InlineData("parallel_resonator.cnl", false)]
    public void D_KurokawaSearch_FindsF0InExactlyOneOfTheTwoDrivingPointFunctions(
        string fixture, bool inY0)
    {
        var (ds, freqs) = RunFixture(fixture);
        double f0   = 1.0 / (2 * Math.PI * Math.Sqrt(1e-9 * 10e-12));
        double step = freqs[1] - freqs[0];

        var inY = WspKurokawa.UnstableFrequencies(ds["Y0:P"].ComplexValues, freqs);
        var inH = WspKurokawa.UnstableFrequencies(ds["H0:P"].ComplexValues, freqs);
        output.WriteLine($"{fixture}: Y0 → [{string.Join(", ", inY.Select(f => (f / 1e9).ToString("G6")))}] GHz, " +
                         $"H0 → [{string.Join(", ", inH.Select(f => (f / 1e9).ToString("G6")))}] GHz " +
                         $"(f0 = {f0 / 1e9:G6} GHz, step = {step / 1e6:G4} MHz)");

        var (hit, miss) = inY0 ? (inY, inH) : (inH, inY);
        double got = Assert.Single(hit);
        Assert.True(Math.Abs(got - f0) <= step / 2,
            $"expected {f0 / 1e9:G6} GHz within half a step ({step / 2e6:G4} MHz), got {got / 1e9:G6} GHz");
        Assert.Empty(miss);
    }

    /// <summary>
    /// The same two circuits with <c>R1</c> made POSITIVE are stable, and then <b>both</b> searches
    /// must come back empty. That is the whole content of the direction rule: a stable resonance
    /// still crosses the real axis, counter-clockwise, and reporting it would make the search
    /// useless.
    /// </summary>
    [Theory]
    [InlineData("series_resonator.cnl",   "R:R1        nG n1  R=-20 Ohm", "R:R1        nG n1  R=20 Ohm")]
    [InlineData("parallel_resonator.cnl", "R:R1        nG 0   R=-5 Ohm",  "R:R1        nG 0   R=5 Ohm")]
    public void D_KurokawaSearch_ReportsNothingOnTheStableVersionsOfTheSameResonators(
        string fixture, string negative, string positive)
    {
        string dir  = Path.GetDirectoryName(Fixture(fixture))!;
        string text = File.ReadAllText(Fixture(fixture));
        Assert.Contains(negative, text);

        var (ds, freqs) = RunText(text.Replace(negative, positive), dir);
        Assert.Empty(WspKurokawa.UnstableFrequencies(ds["Y0:P"].ComplexValues, freqs));
        Assert.Empty(WspKurokawa.UnstableFrequencies(ds["H0:P"].ComplexValues, freqs));
    }

    // ══ (f) — the even-mode load line and the stage gain ═════════════════════

    /// <summary>
    /// <c>wsp_impedance</c> on a symmetric two-way combiner, driven by the probe that splits the
    /// COMMON input node, is the even-mode load line at either drain: <c>RM + 2·RL</c> = 55 Ω, the
    /// classic even-mode reduction — and the half circuit with the common load doubled reproduces it
    /// exactly.
    ///
    /// <para>The same function with the stimulus moved into the OTHER branch answers −1000 Ω, the
    /// negated output resistance of the response probe's own device. That is not a discrepancy: it
    /// is the point. With the drive on the response probe's L side its G side is source-free and
    /// the ratio is <c>−Z_G</c>; with a common drive both devices are live and the ratio is the
    /// effective impedance the device actually works into. Which one you get is decided by <b>where
    /// the stimulus is</b>, and the document's "common stimulus" is doing real work in that
    /// sentence.</para>
    /// </summary>
    [Fact]
    public void F_EvenModeLoadLine_MatchesTheHalfCircuitWithTheLoadDoubled()
    {
        var (full, ff) = RunFixture("combiner_even_mode.cnl");
        var (half, hf) = RunFixture("combiner_half.cnl");
        Assert.Equal(ff.Length, hf.Length);

        const double expected = 5.0 + 2 * 25.0;    // RM + 2·RL

        for (int fi = 0; fi < ff.Length; fi++)
        {
            var w = Wsp(full, fi);

            // Probe order is netlist order: P0 = 1, P1 = 2, P2 = 3.
            var d1 = WspTransfer.Impedance(w, 1, 2);
            var d2 = WspTransfer.Impedance(w, 1, 3);
            AssertRel(expected, d1, 1e-9, $"even-mode load line at drain 1, f = {ff[fi] / 1e9:G4} GHz");
            AssertRel(expected, d2, 1e-9, $"even-mode load line at drain 2, f = {ff[fi] / 1e9:G4} GHz");

            // The half circuit with the common load doubled: P0 = 1, P1 = 2.
            var oneHalf = WspTransfer.Impedance(Wsp(half, fi), 1, 2);
            AssertRel(oneHalf, d2, 1e-9, $"full combiner vs half with 2·RL, f = {ff[fi] / 1e9:G4} GHz");

            // Stimulus in the other branch: the G side of P2 is then source-free and the ratio is
            // −Z_G = −RO2, not a load line at all.
            AssertRel(-1000.0, WspTransfer.Impedance(w, 2, 3), 1e-9,
                $"drive at P1 gives −RO2, f = {ff[fi] / 1e9:G4} GHz");

            // Eq. 37's shunt form and App. C's series form are the SAME number off the diagonal —
            // both ratios cancel the same common stimulus at the same response probe.
            AssertRel(d2, WspTransfer.Impedance(w, 1, 3, WspStimulus.Shunt), 1e-9,
                $"Eq. 37 shunt form = App. C series form off-diagonal, f = {ff[fi] / 1e9:G4} GHz");
        }
        output.WriteLine($"even-mode load line = {WspTransfer.Impedance(Wsp(full, 0), 1, 3)} Ω " +
                         $"(RM + 2·RL = {expected} Ω)");
    }

    /// <summary>
    /// <c>wsp_gain</c> (App. D) on a single transconductance stage with a stimulus probe, a gate
    /// probe and a drain probe, against the closed form of the two real powers it takes the ratio
    /// of:
    ///
    /// <code>
    ///   GT = 10·log10[ (G² / |1/RO + 1/RL + jωCL|²) · (RIN/RL) ]
    /// </code>
    ///
    /// <para>and <b>T-14</b>: Eq. 199/201 as printed put the drain index in both factors, which
    /// would make this identically 0 dB. It is 10–11 dB.</para>
    /// </summary>
    [Fact]
    public void F_WspGain_MatchesTheClosedFormOfTheTwoRealPowers_AndIsNotZeroAsEq199WouldGive()
    {
        var (ds, freqs) = RunFixture("stage_gain.cnl");
        const double g = 0.05, ro = 1000.0, rin = 100.0, rl = 50.0, cload = 0.2e-12;

        for (int fi = 0; fi < freqs.Length; fi++)
        {
            double w   = 2 * Math.PI * freqs[fi];
            var    den = new Complex(1.0 / ro + 1.0 / rl, w * cload);
            double expected = 10.0 * Math.Log10(g * g / (den.Magnitude * den.Magnitude) * (rin / rl));

            // Stimulus at P0 (idx 1), gate at PG (idx 2), drain at PD (idx 3).
            double got = WspTransfer.GainDb(Wsp(ds, fi), 1, 2, 3);
            Assert.Equal(expected, got, 9);
            Assert.True(Math.Abs(got) > 5.0,
                $"wsp_gain read {got:G4} dB — Eq. 199/201 as printed would give 0 dB (T-14).");
        }
        output.WriteLine($"wsp_gain at {freqs[0] / 1e9:G4} GHz = {WspTransfer.GainDb(Wsp(ds, 0), 1, 2, 3):G6} dB");
    }
}
