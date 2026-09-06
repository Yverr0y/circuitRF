// ================================================================
//  TouchstoneHealth.cs  —  is this S-parameter file sound?
//
//  brief-passive-readouts-and-touchstone-check.md §3.
//
//  Why this lives in RfCore and not in the `check` verb. `circuitrf check` writes no validation
//  logic of its own, by design (cli.md): a rule that exists only in the CLI is a rule the
//  application does not enforce, so a file would pass headlessly and be accepted silently when
//  someone loaded it into a plot. Touchstone had no health validator anywhere, so the validator is
//  the new thing and the verb is the thin walk over it. Everything here is reachable from the GUI's
//  own data-source code for the same reason.
//
//  Everything is REPORTED, nothing is repaired and nothing is rejected. A file with gain is an
//  amplifier, a non-reciprocal file may be a circulator, and neither is this class's business to
//  refuse. It measures and states; the caller decides what the numbers mean for the part it thinks
//  it has.
// ================================================================

using System;
using System.Linq;
using System.Numerics;
using NumFlat;

namespace RfCore.Data
{
    /// <summary>Whether the causality test could run, and what it found.</summary>
    public enum CausalityVerdict
    {
        /// <summary>The frequency grid does not support the test. <see cref="TouchstoneHealthReport.CausalitySkipReason"/> says why.</summary>
        NotEvaluated,
        /// <summary>Pre-cursor energy is at or below what band truncation alone explains.</summary>
        Consistent,
        /// <summary>
        /// Pre-cursor energy is well above the truncation floor. <b>Two different things produce
        /// this and the measurement cannot separate them</b> — the file may genuinely not be
        /// causal, or the sweep may not resolve its own response (see
        /// <see cref="TouchstoneHealth.CausalityPrecursorThreshold"/>). Named for what was
        /// measured rather than for a cause, because naming it for a cause would be a claim the
        /// number does not support.
        /// </summary>
        PrecursorEnergyHigh,
    }

    /// <summary>
    /// What a Touchstone file's own contents say about it. Facts first, then the three soundness
    /// measures — each carried as a NUMBER and the frequency it occurred at, never as a verdict, so
    /// the caller can apply its own threshold and say where to look.
    /// </summary>
    /// <param name="Ports">Port count.</param>
    /// <param name="FrequencyCount">Number of frequency points.</param>
    /// <param name="FirstFrequencyHz">Lowest frequency in the file, in hertz.</param>
    /// <param name="LastFrequencyHz">Highest frequency in the file, in hertz.</param>
    /// <param name="Z0">The declared reference impedance.</param>
    /// <param name="GridUniform">True when the frequency step is constant to within a part in 10⁶.</param>
    /// <param name="FirstNonIncreasingIndex">
    /// Index of the first point that does not exceed its predecessor, or −1. A repeated or
    /// out-of-order frequency is an unambiguous defect: every interpolator here assumes a sorted
    /// axis and produces a plausible wrong answer rather than an error on one that is not.
    /// </param>
    /// <param name="WorstSigmaMax">Largest σ_max(S) over the sweep. Above 1 the network has gain.</param>
    /// <param name="WorstSigmaMaxFrequencyHz">Where that occurred.</param>
    /// <param name="WorstReciprocityError">Largest |S_ij − S_ji| over the sweep and over i &lt; j.</param>
    /// <param name="WorstReciprocityFrequencyHz">Where that occurred.</param>
    /// <param name="Causality">Whether the pre-cursor test ran, and its verdict.</param>
    /// <param name="CausalityPrecursorRatio">
    /// Fraction of impulse-response energy landing at negative time, or NaN when not evaluated.
    /// </param>
    /// <param name="CausalitySkipReason">Why the test did not run, or null.</param>
    public sealed record TouchstoneHealthReport(
        int     Ports,
        int     FrequencyCount,
        double  FirstFrequencyHz,
        double  LastFrequencyHz,
        Complex Z0,
        bool    GridUniform,
        int     FirstNonIncreasingIndex,
        double  WorstSigmaMax,
        double  WorstSigmaMaxFrequencyHz,
        double  WorstReciprocityError,
        double  WorstReciprocityFrequencyHz,
        CausalityVerdict Causality,
        double  CausalityPrecursorRatio,
        string? CausalitySkipReason);

    /// <summary>
    /// Measures a Touchstone sweep against the three properties an S-parameter file is normally
    /// assumed to have — passivity, reciprocity and causality — plus the structural facts about its
    /// frequency axis and reference impedance.
    /// </summary>
    public static class TouchstoneHealth
    {
        /// <summary>
        /// Tolerance on σ_max before a network is called active. Set at 10⁻⁶ to match the PRD's
        /// S-parameter tolerance: below that a σ_max over 1 is the solver's or the file's last
        /// digit, not gain.
        /// </summary>
        public const double PassivityTolerance = 1e-6;

        /// <summary>
        /// Tolerance on |S_ij − S_ji|. Looser than the passivity tolerance on purpose: reciprocity
        /// is asserted against MEASURED data, where the two directions were acquired through
        /// different hardware paths and agreeing to 10⁻⁴ is already a well-calibrated fixture.
        /// </summary>
        public const double ReciprocityTolerance = 1e-4;

        /// <summary>
        /// Pre-cursor energy fraction above which the transform is reported.
        ///
        /// <para><b>Set at 25 % from measurement, not from theory, and the measurements are worth
        /// recording because both of the effects that set it are invisible in the result.</b>
        /// Across causal reference responses whose sweep resolves them — shunt and series C and L
        /// with parasitics, an RC low-pass, a pure delay, a plain through — the measured ratio is
        /// at most 5 %. A response fitted with a right-half-plane pole, or a delay run backwards,
        /// measures 56-98 %. So the separation is genuine and wide. The whole table is gated in
        /// <c>TouchstoneHealthTests.ThePrecursorMeasurementSeparatesCausalFromAcausal</c>, which
        /// asserts a 2× margin on each side rather than only the side of the line — so narrowing
        /// the separation fails loudly instead of quietly.</para>
        ///
        /// <para><b>What sits in between is a sweep that does not resolve its own response, and it
        /// is why this number cannot be reported as "not causal".</b> A ferrite bead whose
        /// impedance corner is at 95 MHz, sampled on a 25 MHz uniform grid to 10 GHz, measures
        /// 37 % — the same side of the line as a genuinely acausal file, and it is neither acausal
        /// nor defective, merely under-sampled at the bottom of a very wide uniform band (refining
        /// the step to 1 MHz takes the same network to 0.02 %). A uniform grid fine enough to resolve a
        /// hundred-megahertz corner up to ten gigahertz needs tens of thousands of points, which
        /// nobody ships. Both causes matter to anyone using the file in the time domain, so the
        /// finding is worth reporting — as what was measured, naming both readings, never as a
        /// verdict on the file.</para>
        /// </summary>
        public const double CausalityPrecursorThreshold = 0.25;

        /// <summary>Relative tolerance on the frequency step before a grid is called non-uniform.</summary>
        private const double GridUniformityTolerance = 1e-6;

        /// <summary>Measures <paramref name="snp"/>. Never throws for data reasons — an empty or
        /// degenerate sweep comes back as a report saying so.
        ///
        /// <para>The zero-point branch below is defensive rather than reachable: both of
        /// <see cref="SNP"/>'s constructors refuse an empty sweep, so a file that produced one has
        /// already thrown in the reader and been reported there. It stays because this method takes
        /// an SNP it did not build, and returning a report is the right answer for a caller that
        /// somehow has one — but it is deliberately not gated by a test, since constructing the
        /// input would mean defeating SNP's own guard.</para></summary>
        public static TouchstoneHealthReport Analyze(SNP snp)
        {
            var freqs = snp.Frequencies;
            int n     = freqs.Length;

            if (n == 0)
                return new TouchstoneHealthReport(
                    snp.Ports, 0, double.NaN, double.NaN, snp.Z0, false, -1,
                    double.NaN, double.NaN, double.NaN, double.NaN,
                    CausalityVerdict.NotEvaluated, double.NaN, "the file carries no frequency points");

            int firstBad = -1;
            for (int i = 1; i < n; i++)
                if (!(freqs[i] > freqs[i - 1])) { firstBad = i; break; }

            bool uniform = IsUniform(freqs);

            // Passivity and reciprocity are measured on the file's own matrices, renormalised to a
            // uniform real reference exactly as every other metric here is (R-stb-2) — σ_max is not
            // reference-independent, so measuring it against a complex declared Z0 would answer a
            // different question than the one asked.
            var z0   = Enumerable.Repeat(snp.Z0, snp.Ports).ToArray();
            var mats = NetworkMetrics.FullUniformReal(snp.Matrices, z0);

            double worstSigma = double.NaN, worstSigmaF = double.NaN;
            double worstRecip = double.NaN, worstRecipF = double.NaN;

            for (int f = 0; f < mats.Length && f < n; f++)
            {
                double sigma = RFNetwork.Passivity(mats[f]);
                if (double.IsNaN(worstSigma) || sigma > worstSigma) { worstSigma = sigma; worstSigmaF = freqs[f]; }

                double recip = ReciprocityError(mats[f]);
                if (!double.IsNaN(recip) && (double.IsNaN(worstRecip) || recip > worstRecip))
                { worstRecip = recip; worstRecipF = freqs[f]; }
            }

            var (verdict, ratio, skip) = Causality(snp, uniform, firstBad);

            return new TouchstoneHealthReport(
                snp.Ports, n, freqs[0], freqs[n - 1], snp.Z0,
                uniform, firstBad,
                worstSigma, worstSigmaF,
                worstRecip, worstRecipF,
                verdict, ratio, skip);
        }

        /// <summary>Largest |S_ij − S_ji| over i &lt; j; NaN for a 1-port, which is reciprocal by
        /// construction and where the question does not arise.</summary>
        public static double ReciprocityError(Mat<Complex> s)
        {
            int n = s.RowCount;
            if (n < 2) return double.NaN;

            double worst = 0.0;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    worst = Math.Max(worst, (s[i, j] - s[j, i]).Magnitude);
            return worst;
        }

        private static bool IsUniform(double[] f)
        {
            if (f.Length < 3) return false;
            double step = f[1] - f[0];
            if (!(step > 0)) return false;
            for (int i = 2; i < f.Length; i++)
                if (Math.Abs((f[i] - f[i - 1]) - step) > GridUniformityTolerance * step) return false;
            return true;
        }

        // ── causality ────────────────────────────────────────────────────────

        /// <summary>
        /// The pre-cursor test: extend each S element to a Hermitian two-sided spectrum, transform
        /// to the time domain, and measure what fraction of the energy lands before t = 0. A causal
        /// network has none there; a fitted model that was never constrained to be causal has a
        /// great deal.
        ///
        /// <para><b>It runs only on a uniform grid that reaches DC in one step</b>, and says so
        /// rather than guessing when it does not. The transform needs equally-spaced samples of a
        /// spectrum starting at zero; a log-spaced sweep — which is what most vendor capacitor
        /// files are — cannot be resampled onto one without interpolating, and interpolation is
        /// itself a low-pass that would manufacture the very smoothness the test looks for. So a
        /// log sweep returns <see cref="CausalityVerdict.NotEvaluated"/> with the reason, which is
        /// the honest answer and a common one.</para>
        /// </summary>
        private static (CausalityVerdict, double, string?) Causality(SNP snp, bool uniform, int firstBad)
        {
            if (firstBad >= 0)
                return (CausalityVerdict.NotEvaluated, double.NaN,
                        "the frequency axis is not strictly increasing");
            if (!uniform)
                return (CausalityVerdict.NotEvaluated, double.NaN,
                        "the frequency grid is not uniformly spaced (a log sweep cannot be transformed without interpolating, which would itself smooth the data)");

            var f    = snp.Frequencies;
            double df = f[1] - f[0];

            // The grid must land on DC in one step: either it starts there, or its first point IS
            // the step. Anything else means the samples between DC and f[0] are missing, and
            // inventing them would decide the answer.
            int dcOffset;
            if (Math.Abs(f[0]) <= GridUniformityTolerance * df)      dcOffset = 0;
            else if (Math.Abs(f[0] - df) <= GridUniformityTolerance * df) dcOffset = 1;
            else return (CausalityVerdict.NotEvaluated, double.NaN,
                         $"the grid does not reach DC in one step (starts at {f[0]:G6} Hz with a {df:G6} Hz step)");

            int nPorts = snp.Ports;
            int m      = f.Length + dcOffset;              // spectrum length including DC
            int half   = NextPowerOfTwo(m);                // zero-pad above f_max; already band-limited there
            int len    = 2 * half;

            double worst = 0.0;
            var h = new Complex[len];

            for (int i = 0; i < nPorts; i++)
            for (int j = 0; j < nPorts; j++)
            {
                Array.Clear(h);

                // Subtract the REAL part of the value at the top of the band, per element.
                //
                // A real constant across frequency is δ(t) scaled — perfectly causal — so removing
                // it cannot turn an acausal response causal or the reverse. What it removes is the
                // band-edge STEP: the samples stop at f_max and everything above is treated as
                // zero, and a response still at 0.8 there is a step of 0.8 whose sinc ringing is
                // symmetric about t = 0, putting half its energy at negative time for reasons that
                // have nothing to do with the part. Measured on a causal shunt capacitor: 28.9 %
                // without this subtraction, 4.9 % with it; on a plain through, 10.9 % and 0 %.
                //
                // Only the REAL part, and that is not a simplification. Subtracting a complex
                // constant subtracts c_re·δ(t) plus a Hilbert kernel c_im/(πt), which is spread
                // over all time INCLUDING negative time — it injects the very thing being
                // measured. Doing it made every case worse, which is how the asymmetry was found.
                double asymptote = snp.Matrices[^1][i, j].Real;

                for (int k = 0; k < m; k++)
                {
                    Complex v;
                    if (dcOffset == 1 && k == 0)
                        // DC of a physical network is real. Taking the first point's real part is
                        // the mildest extrapolation available and does not bias the pre-cursor
                        // measurement, which is dominated by the high-frequency truncation.
                        v = new Complex(snp.Matrices[0][i, j].Real, 0.0);
                    else
                        v = snp.Matrices[k - dcOffset][i, j];
                    h[k] = v - asymptote;
                }

                // Hermitian extension — the impulse response of a real-valued network is real.
                for (int k = 1; k < half; k++) h[len - k] = Complex.Conjugate(h[k]);

                InverseFft(h);

                double total = 0.0, pre = 0.0;
                for (int t = 0; t < len; t++)
                {
                    double e = h[t].Real * h[t].Real + h[t].Imaginary * h[t].Imaginary;
                    total += e;
                    if (t >= len / 2) pre += e;          // second half IS negative time
                }
                if (total > 0.0) worst = Math.Max(worst, pre / total);
            }

            return (worst > CausalityPrecursorThreshold
                        ? CausalityVerdict.PrecursorEnergyHigh
                        : CausalityVerdict.Consistent,
                    worst, null);
        }

        private static int NextPowerOfTwo(int v)
        {
            int p = 1;
            while (p < v) p <<= 1;
            return p;
        }

        /// <summary>
        /// In-place radix-2 inverse FFT, length a power of two.
        ///
        /// <para>Written here rather than taken from a package because RfCore references no FFT
        /// library and this is its only transform — adding a dependency to the whole project for one
        /// diagnostic would be the larger change. It is the textbook iterative Cooley-Tukey, and the
        /// only thing that matters for the measurement above is the ENERGY split between halves,
        /// which any correct transform gives identically.</para>
        /// </summary>
        private static void InverseFft(Complex[] a)
        {
            int n = a.Length;

            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) (a[i], a[j]) = (a[j], a[i]);
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2.0 * Math.PI / len;          // +, for the INVERSE transform
                var wl = new Complex(Math.Cos(ang), Math.Sin(ang));
                for (int i = 0; i < n; i += len)
                {
                    var w = Complex.One;
                    for (int k = 0; k < len / 2; k++)
                    {
                        var u = a[i + k];
                        var v = a[i + k + len / 2] * w;
                        a[i + k]             = u + v;
                        a[i + k + len / 2]   = u - v;
                        w *= wl;
                    }
                }
            }

            for (int i = 0; i < n; i++) a[i] /= n;
        }
    }
}
