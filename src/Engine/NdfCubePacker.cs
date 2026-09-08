using System.Numerics;
using CircuitRF.Core.Elaboration;
using RfCore.Data;
using RfCore.Stability;

namespace CircuitRF.Engine;

/// <summary>
/// What a run asks for when <c>NDF=yes</c> is on the directive — the two user-named passivation
/// routes and, when one of them is needed, the second elaboration it takes (R-wsp6-4).
///
/// <para>The passive netlist is a FACTORY rather than a netlist because the frequency-parallel path
/// runs each chunk on its own elaborated copy: one shared netlist would be stamped from several
/// threads at once, which is the very thing SP-P3's copies exist to prevent. Every call is made
/// serially, before any worker starts, for the reason SP-P3 already gives — the elaborator reads the
/// TestBench's own lists.</para>
/// </summary>
public sealed class NdfRequest
{
    /// <summary>Globals named in <c>PassiveVars=</c>, each forced to 0 in the passive
    /// elaboration.</summary>
    public IReadOnlyList<string> PassiveVars { get; init; } = [];

    /// <summary>Instance parameters named in <c>PassiveParams=</c> (<c>X1.gmscale</c>), each forced
    /// to 0 in the passive elaboration.</summary>
    public IReadOnlyList<string> PassiveParams { get; init; } = [];

    /// <summary>
    /// Makes one netlist elaborated with every name above at 0 — normally
    /// <c>NdfPassivation.BuildPassiveNetlist</c>, bound to the caller's own library and testbench.
    /// Null is legal and is the common case: a design whose active devices are all built-in needs no
    /// second elaboration at all, because each of those models stamps its own passivated form.
    /// </summary>
    public Func<ElaboratedNetlist>? PassiveNetlist { get; init; }
}

/// <summary>
/// The NDF result cubes and Platzker's five properties, checked on the engine's own output —
/// T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023), §8 p. 112;
/// brief-wsprobe-6 R-wsp6-2.
/// </summary>
internal static class NdfCubePacker
{
    /// <summary>How far <c>NDF(f_max)</c> may sit from 1 before the run says the sweep stops too
    /// low, and how large <c>|Im NDF(f_min)| / |NDF(f_min)|</c> may be before it says the sweep
    /// starts too high. The brief's own 5 %.</summary>
    private const double PropertyTol = 0.05;

    /// <summary>
    /// How far the running encirclement count may fall back below its own maximum before the run
    /// calls it a counter-clockwise ENCIRCLEMENT. A whole one, in the full-contour count the cube
    /// carries.
    ///
    /// <para>Platzker's property 2 forbids a counter-clockwise ENCIRCLEMENT, not a counter-clockwise
    /// stretch of phase: a locus is free to wander back and forth as long as it does not go round.
    /// The Ohtomo two-device amplifier of R-wsp6-9(k) backtracks by 0.74 of an encirclement near the
    /// top of a five-decade sweep with a perfectly passive Δ0 — every element of it is an ordinary
    /// R, L or C once the two transconductances are zeroed — so a threshold below one would report
    /// a passivation failure on a circuit that has none.</para>
    /// </summary>
    private const double CounterClockwiseTol = 1.0;

    /// <summary>
    /// <c>NDF {freq}</c> (Complex), <c>NDF_enc {freq}</c> (the running clockwise encirclement count)
    /// and the scalar <c>NDF_poles</c> — the number of right-half-plane poles of the network, which
    /// is what the whole construction is for (§8, p. 112: <c>Δ0</c> has no right-half-plane zeros by
    /// construction, so by the argument principle the clockwise encirclements of the origin by
    /// <c>NDF(jω)</c> ARE the right-half-plane poles of <c>Δ</c>).
    /// </summary>
    /// <returns>The rounded right-half-plane pole count, for the run summary.</returns>
    internal static int Add(
        DataSet ds, ElaboratedNetlist netlist, Axis axis, double[] axisValues, Complex[] ndf,
        string axisWhat)
    {
        ds.Add("NDF", new DataCube([axis], (Complex[])ndf.Clone()));

        // ── The factor of two, which is the whole subtlety of reading a count off half a contour ──
        //
        // The argument principle counts turns around the CLOSED Nyquist contour, ω from −∞ to +∞.
        // A sweep runs ω ≥ 0 only, and Platzker's property 4 — NDF(−ω) = conj(NDF(ω)) — is exactly
        // the statement that the missing half is the mirror image of this one and turns through the
        // same angle. So ONE right-half-plane pole shows up here as HALF a turn: the reference
        // document's Fig. 37 says so in as many words, calling a locus whose phase "passes through
        // π" a single encirclement. WspKurokawa.Encirclements is the running turn count of the
        // samples it is given, so the full-contour count is twice it, referred to the sweep's own
        // first sample (the locus starts wherever the lowest swept frequency puts it).
        var turns = WspKurokawa.Encirclements(ndf);
        var enc   = new double[turns.Length];
        for (int k = 0; k < turns.Length; k++) enc[k] = 2.0 * (turns[k] - turns[0]);
        ds.Add("NDF_enc", new DataCube([axis], enc));

        int poles = enc.Length > 0
            ? (int)Math.Round(enc[^1], MidpointRounding.AwayFromZero)
            : 0;
        ds.Add("NDF_poles", new DataCube([new Axis("one", [0.0])], new[] { (double)poles }));

        CheckProperties(netlist, axisValues, ndf, enc, axisWhat);
        return poles;
    }

    /// <summary>
    /// The three of Platzker's five properties that a finite sweep can fail. Properties 1 (the NDF
    /// has zeros only) and 4 (<c>NDF(−ω) = conj(NDF(ω))</c>) hold by construction here — the
    /// denominators of <c>Δ</c> and <c>Δ0</c> are the same branch-elimination factor and cancel, and
    /// a real-valued netlist gives a conjugate-symmetric <c>M(jω)</c> — so there is nothing for a
    /// check to catch.
    /// </summary>
    private static void CheckProperties(
        ElaboratedNetlist netlist, double[] f, Complex[] ndf, double[] enc, string axisWhat)
    {
        if (ndf.Length == 0) return;

        // ── Property 3: NDF → 1 as ω → ∞, and the one case where it legitimately does not ──
        //
        // A pure dependent source contributes ADDITIVELY to the determinant, so removing it leaves
        // every constant factor alone and the ratio tends to exactly 1. A NEGATIVE RESISTANCE does
        // not: "rendered passive" changes an element VALUE, and an element value can appear as a
        // FACTOR of the network determinant — a series R does, so R → |R| multiplies the whole NDF
        // by −1 for all ω. The count is untouched (a constant factor turns through no angle), and
        // saying "extend the sweep" about a locus that has already arrived would send the user
        // hunting for a frequency range that does not exist. So the two are separated by asking
        // whether the locus has CONVERGED — compared against the sample a decade below the top —
        // before asking what it converged to.
        var top = ndf[^1];
        if (!double.IsNaN(top.Real) && (top - Complex.One).Magnitude > PropertyTol)
        {
            // Is the locus APPROACHING 1, or has it settled somewhere else? Compare its distance
            // from 1 against the same distance a decade lower: a tail heading for 1 closes that
            // distance in proportion to 1/ω, so it shrinks by about ten; a locus that has settled on
            // a constant other than 1 barely moves. This asks the question directly rather than
            // through a fitted model, so a sweep whose top decade is not yet asymptotic falls into
            // the "still moving" arm, which is the safe one.
            int  low = IndexAtOrBelow(f, f[^1] / 10.0, ndf.Length - 1);
            bool closing = low < 0
                        || double.IsNaN(ndf[low].Real)
                        || (top - Complex.One).Magnitude
                             < (ndf[low] - Complex.One).Magnitude / 3.0;

            if (closing)
                netlist.AddWarningOnce("ndf.no-asymptote",
                    "ndf.no-asymptote: " +
                    $"NDF({axisWhat}{f[^1] / 1e9:G6} GHz) = {Fmt(top)}, which is {(top - Complex.One).Magnitude:G3} " +
                    "away from 1 and still closing on it. Extend the sweep upward; the NDF must tend " +
                    "to 1 (Platzker's property 3, §8 p. 112), and an encirclement count read off a " +
                    "sweep that has not got there yet may be missing a turn.");
            else
                netlist.AddWarningOnce("ndf.constant-asymptote",
                    "ndf.constant-asymptote: " +
                    $"NDF tends to {Fmt(Limit(f, ndf, low))} rather than to 1 — it is no closer to 1 at " +
                    $"{axisWhat}{f[^1] / 1e9:G6} GHz than a decade below, so the sweep is long enough " +
                    "and the limit is simply not 1. A constant factor like this is what passivating a " +
                    "NEGATIVE RESISTANCE does: R → |R| changes an element VALUE, and an element value " +
                    "can be a FACTOR of the network determinant, where a dependent source is only ever " +
                    "a term in it. The right-half-plane pole count is unaffected — a constant turns " +
                    "through no angle — but Platzker's property 3 (§8 p. 112) is stated for a Δ0 that " +
                    "differs from Δ by removed dependent sources alone.");
        }

        // Property 5: Im(NDF) → 0 as ω → 0.
        var bot = ndf[0];
        double mag = bot.Magnitude;
        if (!double.IsNaN(bot.Real) && mag > 0 && Math.Abs(bot.Imaginary) / mag > PropertyTol)
            netlist.AddWarningOnce("ndf.dc-imaginary",
                "ndf.dc-imaginary: " +
                $"NDF({axisWhat}{f[0] / 1e9:G6} GHz) = {Fmt(bot)}, whose imaginary part is " +
                $"{Math.Abs(bot.Imaginary) / mag:P1} of its magnitude. Extend the sweep downward; the " +
                "NDF is real at DC (Platzker's property 5, §8 p. 112).");

        // Property 2: clockwise encirclements only.
        double peak = double.NegativeInfinity, worst = 0.0; int at = -1;
        for (int k = 0; k < enc.Length; k++)
        {
            if (double.IsNaN(enc[k])) continue;
            peak = Math.Max(peak, enc[k]);
            double back = peak - enc[k];
            if (back > worst) { worst = back; at = k; }
        }
        if (worst > CounterClockwiseTol)
            netlist.AddWarningOnce("ndf.counterclockwise",
                "ndf.counterclockwise: " +
                $"the NDF locus turns COUNTER-clockwise by {worst:G3} of an encirclement near " +
                $"{axisWhat}{f[Math.Max(at, 0)] / 1e9:G6} GHz. The NDF encircles the origin clockwise " +
                "only (Platzker's property 2, §8 p. 112), so this means Δ0 was not passive — some " +
                "dependent source survived the passivation. The pole count below is not to be " +
                "trusted; check the ndf.passivation-not-passive notes, and the PassiveVars entry of " +
                "any user-defined device.");
    }

    /// <summary>
    /// The NDF's limit as <c>ω → ∞</c>, extrapolated from the top of the sweep — quoted in the
    /// message, never used to decide anything.
    ///
    /// <para>The tail is <c>NDF(ω) ≈ c + d/ω</c>: <c>Δ</c> and <c>Δ0</c> are polynomials of the same
    /// degree with the same leading coefficient and differ first in the coefficient below it. Two
    /// samples therefore give <c>c</c> by Richardson elimination of the <c>1/ω</c> term.</para>
    /// </summary>
    private static Complex Limit(double[] f, Complex[] ndf, int low)
    {
        int top = ndf.Length - 1;
        if (low < 0 || low == top || double.IsNaN(ndf[low].Real)) return ndf[top];
        return (f[top] * ndf[top] - f[low] * ndf[low]) / (f[top] - f[low]);
    }

    /// <summary>The largest index below <paramref name="before"/> whose frequency is at or below
    /// <paramref name="target"/>, or −1 when the sweep does not reach that far down.</summary>
    private static int IndexAtOrBelow(double[] f, double target, int before)
    {
        for (int k = before - 1; k >= 0; k--) if (f[k] <= target) return k;
        return -1;
    }

    private static string Fmt(Complex z)
        => $"{z.Real:G4} {(z.Imaginary < 0 ? "−" : "+")} j{Math.Abs(z.Imaginary):G4}";

    /// <summary>
    /// R-wsp6-5's guard, reported: one note per device whose PASSIVATED linearised block fails
    /// <c>Y + Yᴴ ⪰ 0</c>, naming the device and the frequency. The NDF is still emitted — the count
    /// is then unreliable and the message says so. This is the check §8 p. 113 wishes for ("great
    /// care must be taken when constructing the NDF").
    /// </summary>
    internal static void ReportPassivationNotPassive(
        ElaboratedNetlist netlist, string instancePath, double hz, double margin, string axisWhat)
        => netlist.AddWarningOnce($"ndf.passivation-not-passive:{instancePath}",
            $"ndf.passivation-not-passive: the passivated block of '{instancePath}' is not passive at " +
            $"{axisWhat}{hz / 1e9:G6} GHz — the smallest eigenvalue of Y + Yᴴ is {margin:G3} of the " +
            "block's own scale, and a passive block's is never negative. Δ0 is then not the passive " +
            "determinant Platzker's construction needs, so the NDF is still reported but its " +
            "right-half-plane pole count is unreliable.");
}
