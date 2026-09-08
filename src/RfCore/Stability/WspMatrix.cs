using System;
using System.Numerics;

namespace RfCore.Stability;

/// <summary>
/// The four <c>N×N</c> blocks of a <c>wsp</c> matrix over a probe subset, each indexed
/// <c>[stimulus i, response j]</c> in the order of <see cref="Probes"/> — the reference document's
/// §6 extraction (<c>wsp(1::2::N, 2::2::N)</c> and its three siblings), written out:
///
/// <code>
///   VV[i,j] = wsp(2i−1, 2j)      voltage at j  per series voltage at i      (Eq. 164, "A")
///   VI[i,j] = wsp(2i−1, 2j−1)    current at j  per series voltage at i      (Eq. 166, "C")
///   IV[i,j] = wsp(2i,   2j)      voltage at j  per shunt current at i       (Eq. 174)
///   II[i,j] = wsp(2i,   2j−1)    current at j  per shunt current at i       (Eq. 172)
/// </code>
///
/// <para>T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023), §6; brief-wsprobe-3
/// §1.</para>
/// </summary>
/// <param name="Probes">The document's 1-based probe indices, in the order the blocks are laid out.</param>
public readonly record struct WspBlocks(Complex[,] VV, Complex[,] VI, Complex[,] IV, Complex[,] II, int[] Probes)
{
    public int Count => Probes.Length;
}

/// <summary>
/// Small dense complex-matrix arithmetic for the multi-probe functions — block extraction, products,
/// an LU solve, and the determinant in <b>log form</b>. Nothing here is numerically clever; the
/// matrices are <c>2N × 2N</c> for <c>N</c> probes and the point is to have one self-contained
/// solver whose log-determinant a 30-probe NDF at 1,000 frequencies can take without overflow or
/// underflow (brief-wsprobe-3 §5).
/// </summary>
public static class WspMatrix
{
    // ── Block extraction ─────────────────────────────────────────────────────

    /// <summary>The number of probes a <c>2N × 2N</c> wsp matrix describes.</summary>
    public static int ProbeCount(Complex[,] wsp)
    {
        int n = wsp.GetLength(0);
        if (n != wsp.GetLength(1) || (n & 1) != 0)
            throw new ArgumentException($"a wsp matrix is 2N × 2N; this one is {n} × {wsp.GetLength(1)}.", nameof(wsp));
        return n / 2;
    }

    /// <summary>Every probe, <c>1 … N</c>, in idx order — the default probe list.</summary>
    public static int[] AllProbes(Complex[,] wsp)
    {
        int n = ProbeCount(wsp);
        var p = new int[n];
        for (int i = 0; i < n; i++) p[i] = i + 1;
        return p;
    }

    /// <summary>
    /// The four blocks over <paramref name="probes"/> (1-based idx values, any order, the whole set
    /// when null). An index outside <c>1 … N</c> or repeated is refused by name.
    /// </summary>
    public static WspBlocks Blocks(Complex[,] wsp, int[]? probes = null)
    {
        int total = ProbeCount(wsp);
        probes ??= AllProbes(wsp);
        int n = probes.Length;
        if (n == 0) throw new ArgumentException("the probe list is empty.", nameof(probes));
        for (int k = 0; k < n; k++)
        {
            if (probes[k] < 1 || probes[k] > total)
                throw new ArgumentException(
                    $"probe index {probes[k]} is outside 1..{total} (the wsp matrix is 2N × 2N for N probes).",
                    nameof(probes));
            for (int m = 0; m < k; m++)
                if (probes[m] == probes[k])
                    throw new ArgumentException($"probe index {probes[k]} is listed twice.", nameof(probes));
        }

        var vv = new Complex[n, n]; var vi = new Complex[n, n];
        var iv = new Complex[n, n]; var ii = new Complex[n, n];
        for (int a = 0; a < n; a++)
        {
            int rs = 2 * probes[a] - 2, rp = 2 * probes[a] - 1;      // 0-based rows of 2i−1 and 2i
            for (int b = 0; b < n; b++)
            {
                int ci = 2 * probes[b] - 2, cv = 2 * probes[b] - 1;  // 0-based cols of 2j−1 and 2j
                vv[a, b] = wsp[rs, cv];
                vi[a, b] = wsp[rs, ci];
                iv[a, b] = wsp[rp, cv];
                ii[a, b] = wsp[rp, ci];
            }
        }
        return new WspBlocks(vv, vi, iv, ii, (int[])probes.Clone());
    }

    // ── Elementary operations ────────────────────────────────────────────────

    public static Complex[,] Identity(int n)
    {
        var m = new Complex[n, n];
        for (int i = 0; i < n; i++) m[i, i] = Complex.One;
        return m;
    }

    public static Complex[,] Transpose(Complex[,] a)
    {
        int r = a.GetLength(0), c = a.GetLength(1);
        var t = new Complex[c, r];
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++) t[j, i] = a[i, j];
        return t;
    }

    public static Complex[,] Add(Complex[,] a, Complex[,] b)
    {
        int r = a.GetLength(0), c = a.GetLength(1);
        var m = new Complex[r, c];
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++) m[i, j] = a[i, j] + b[i, j];
        return m;
    }

    public static Complex[,] Subtract(Complex[,] a, Complex[,] b)
    {
        int r = a.GetLength(0), c = a.GetLength(1);
        var m = new Complex[r, c];
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++) m[i, j] = a[i, j] - b[i, j];
        return m;
    }

    public static Complex[,] Scale(Complex s, Complex[,] a)
    {
        int r = a.GetLength(0), c = a.GetLength(1);
        var m = new Complex[r, c];
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++) m[i, j] = s * a[i, j];
        return m;
    }

    public static Complex[,] Multiply(Complex[,] a, Complex[,] b)
    {
        int r = a.GetLength(0), k = a.GetLength(1), c = b.GetLength(1);
        if (b.GetLength(0) != k)
            throw new ArgumentException($"cannot multiply {r}×{k} by {b.GetLength(0)}×{c}.");
        var m = new Complex[r, c];
        for (int i = 0; i < r; i++)
            for (int j = 0; j < c; j++)
            {
                Complex s = Complex.Zero;
                for (int t = 0; t < k; t++) s += a[i, t] * b[t, j];
                m[i, j] = s;
            }
        return m;
    }

    /// <summary>The trailing principal submatrix on rows and columns <paramref name="from"/> … N−1
    /// (0-based) — Eq. 180's <c>M_{N−i+1}</c> with <c>i − 1 = from</c> (typo register T-10).</summary>
    public static Complex[,] TrailingPrincipal(Complex[,] a, int from)
    {
        int n = a.GetLength(0), k = n - from;
        var m = new Complex[k, k];
        for (int i = 0; i < k; i++)
            for (int j = 0; j < k; j++) m[i, j] = a[from + i, from + j];
        return m;
    }

    /// <summary>The scattering matrix of an admittance N-port at a uniform reference,
    /// <c>S = (I − Z0·Y)(I + Z0·Y)⁻¹</c> (Eq. 142/143; §7).</summary>
    public static Complex[,] ScatteringOfY(Complex[,] y, Complex z0)
    {
        int n = y.GetLength(0);
        var zy = Scale(z0, y);
        return Multiply(Subtract(Identity(n), zy), Inverse(Add(Identity(n), zy)));
    }

    // ── LU, solve, inverse, determinant ──────────────────────────────────────

    /// <summary>
    /// A partial-pivot LU factorisation of a square complex matrix, kept explicit so that the
    /// determinant can be read off in log form. <see cref="LogAbsDet"/> and <see cref="ArgDet"/>
    /// are <c>Σ log|u_ii|</c> and <c>Σ arg(u_ii) + π·(row swaps)</c>, so
    /// <c>det = exp(LogAbsDet)·e^{j·ArgDet}</c> without ever forming a product that could overflow.
    /// </summary>
    public sealed class Lu
    {
        private readonly Complex[,] _lu;
        private readonly int[]      _perm;
        public int N { get; }
        public bool Singular { get; }
        public double LogAbsDet { get; }
        public double ArgDet { get; }

        public Lu(Complex[,] a)
        {
            N = a.GetLength(0);
            if (a.GetLength(1) != N) throw new ArgumentException("LU needs a square matrix.", nameof(a));
            _lu   = (Complex[,])a.Clone();
            _perm = new int[N];
            for (int i = 0; i < N; i++) _perm[i] = i;

            double logAbs = 0.0, arg = 0.0;
            bool singular = false;
            for (int k = 0; k < N; k++)
            {
                int p = k; double best = _lu[k, k].Magnitude;
                for (int i = k + 1; i < N; i++)
                {
                    double m = _lu[i, k].Magnitude;
                    if (m > best) { best = m; p = i; }
                }
                if (best == 0.0) { singular = true; continue; }
                if (p != k)
                {
                    for (int j = 0; j < N; j++) (_lu[k, j], _lu[p, j]) = (_lu[p, j], _lu[k, j]);
                    (_perm[k], _perm[p]) = (_perm[p], _perm[k]);
                    arg += Math.PI;
                }
                var piv = _lu[k, k];
                logAbs += Math.Log(piv.Magnitude);
                arg    += piv.Phase;
                for (int i = k + 1; i < N; i++)
                {
                    var f = _lu[i, k] / piv;
                    _lu[i, k] = f;
                    if (f == Complex.Zero) continue;
                    for (int j = k + 1; j < N; j++) _lu[i, j] -= f * _lu[k, j];
                }
            }
            Singular  = singular;
            LogAbsDet = singular ? double.NegativeInfinity : logAbs;
            ArgDet    = singular ? 0.0 : arg;
        }

        /// <summary>The determinant as a number — <c>exp(LogAbsDet)·e^{j·ArgDet}</c>; zero when
        /// singular, and it can overflow or underflow, which is what the log form is for.</summary>
        public Complex Determinant
            => Singular ? Complex.Zero : Complex.FromPolarCoordinates(Math.Exp(LogAbsDet), ArgDet);

        /// <summary>Solve <c>A·x = b</c> in place into a new vector.</summary>
        public Complex[] Solve(ReadOnlySpan<Complex> b)
        {
            if (Singular) throw new InvalidOperationException("the matrix is singular.");
            var x = new Complex[N];
            for (int i = 0; i < N; i++) x[i] = b[_perm[i]];
            for (int i = 0; i < N; i++)
                for (int j = 0; j < i; j++) x[i] -= _lu[i, j] * x[j];
            for (int i = N - 1; i >= 0; i--)
            {
                for (int j = i + 1; j < N; j++) x[i] -= _lu[i, j] * x[j];
                x[i] /= _lu[i, i];
            }
            return x;
        }

        /// <summary>Solve <c>A·X = B</c> column by column.</summary>
        public Complex[,] Solve(Complex[,] b)
        {
            int c = b.GetLength(1);
            var x   = new Complex[N, c];
            var col = new Complex[N];
            for (int j = 0; j < c; j++)
            {
                for (int i = 0; i < N; i++) col[i] = b[i, j];
                var s = Solve(col);
                for (int i = 0; i < N; i++) x[i, j] = s[i];
            }
            return x;
        }
    }

    /// <summary>Solve <c>A·X = B</c>.</summary>
    public static Complex[,] Solve(Complex[,] a, Complex[,] b) => new Lu(a).Solve(b);

    /// <summary><c>A⁻¹</c>. A singular matrix is an <see cref="InvalidOperationException"/>, never a
    /// regularised stand-in.</summary>
    public static Complex[,] Inverse(Complex[,] a) => new Lu(a).Solve(Identity(a.GetLength(0)));

    /// <summary>The determinant as a number. Use <see cref="Lu.LogAbsDet"/>/<see cref="Lu.ArgDet"/>
    /// when a ratio of two large determinants is wanted.</summary>
    public static Complex Determinant(Complex[,] a)
    {
        int n = a.GetLength(0);
        if (n == 0) return Complex.One;
        return new Lu(a).Determinant;
    }
}
