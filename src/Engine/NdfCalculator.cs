using System.Numerics;
using CSparse.Complex.Factorization;
using CSparse.Storage;
using NumFlat;
using RfCore.Stability;

namespace CircuitRF.Engine;

/// <summary>
/// The normalized determinant function of one frequency point, from the ACTIVE and PASSIVE
/// assemblies of the same terminated network — <c>NDF = Δ/Δ0 = |Y|/|Y_passive|</c>
/// (T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023), Eq. 181; Bode Eq. 16),
/// computed <b>without either determinant</b>.
///
/// <para><b>The lemma</b> (brief-wsprobe-6 R-wsp6-1). <c>ΔM = M − M0</c> holds only the
/// dependent-source entries, and every one of them lives in a CONTROL COLUMN — a controlled current
/// source writes into the columns of its sensing pair, a controlled voltage source into the columns
/// of its control pair, a linearised FET's <c>gm</c> into the gate columns. So with
/// <c>c₁ … c_r</c> those columns, <c>U = ΔM[:, c]</c> and <c>E = [e_{c₁} … e_{c_r}]</c>,</para>
/// <code>
///   ΔM     = U·Eᵀ
///   det(M) = det(M0 + U·Eᵀ) = det(M0)·det(I_r + Eᵀ·M0⁻¹·U)     (matrix determinant lemma)
///   NDF    = det(I_r + Eᵀ·M0⁻¹·U)
/// </code>
/// <para>which is <c>r</c> sparse solves against the PASSIVE factorisation, the <c>r</c> rows at
/// <c>c</c> of the result, plus <c>I_r</c>, and one dense <c>r × r</c> determinant. <c>r</c> is the
/// number of control columns in the whole circuit — two or three per transistor, not the size of the
/// network — so there is no large determinant, no overflow, no underflow, and no tiny ratio of two
/// huge numbers, which are exactly the numerical troubles the document reports for the NDF
/// (pp. 95, 112). <b><c>M</c> is never factored.</b></para>
///
/// <para><b>MNA versus nodal.</b> The document writes <c>|Y|</c> for the nodal matrix; circuitRF's
/// <c>M</c> carries branch rows as well. <c>M</c> and <c>M0</c> have identical branch blocks — the
/// passivation of a controlled voltage source keeps its branch and zeroes its gain, and a 0 V branch
/// (a source that is off, an <c>IProbe</c>, a <c>WSProbe</c>) merges two rows in both alike — so
/// eliminating the branches multiplies both determinants by the same factor and the ratio is the
/// nodal one. <c>NdfTests</c> asserts it against explicit determinants.</para>
///
/// <para><b>Struble's sequential form (Eq. 17).</b> <c>I_r + Eᵀ M0⁻¹ U</c> is Bode's
/// return-difference MATRIX of the dependent sources; its LU pivots, taken in DEVICE order — which
/// is the order <see cref="DeltaColumns"/> returns — are Struble's sequential return differences
/// <c>F_i = 1 + T_i</c>, and their product is the NDF. That is a free cross-check, not a feature,
/// and it is why the column order is device-major rather than ascending.</para>
/// </summary>
internal static class NdfCalculator
{
    /// <summary>
    /// The nonzero columns of <c>M − M0</c> and the corresponding columns of the difference, dense.
    /// Both matrices must have the same dimensions; their SPARSITY PATTERNS may differ, because a
    /// passivated stamp legitimately writes fewer entries (a <c>VCCS</c> with <c>G → 0</c> writes
    /// none at all).
    ///
    /// <para>An entry present in both with the same value differs by EXACTLY zero — subtraction of
    /// equal doubles is exact — so a passive component contributes no column however many entries it
    /// stamped.</para>
    /// </summary>
    /// <param name="preferredOrder">
    /// Columns to place first, in this order, when they turn out to be nonzero — the control columns
    /// of the active devices in netlist order, so the return-difference matrix's pivots are
    /// Struble's. Columns not listed follow in ascending order.
    /// </param>
    internal static (int[] Cols, Complex[,] U) DeltaColumns(
        CompressedColumnStorage<Complex> m,
        CompressedColumnStorage<Complex> m0,
        IReadOnlyList<int>? preferredOrder = null)
    {
        int n = m.RowCount;
        if (m0.RowCount != n || m.ColumnCount != m0.ColumnCount)
            throw new InvalidOperationException(
                $"ndf.assembly-mismatch: the active assembly is {m.RowCount}×{m.ColumnCount} and the " +
                $"passive one is {m0.RowCount}×{m0.ColumnCount}. The two must have the same node map " +
                "and the same branch order — a passivated model that allocates a different number of " +
                "branch unknowns is the usual cause.");

        var (ap, ai, av) = (m.ColumnPointers, m.RowIndices, m.Values);
        var (bp, bi, bv) = (m0.ColumnPointers, m0.RowIndices, m0.Values);

        // Pass 1: which columns differ at all.
        var nz = new List<int>();
        for (int c = 0; c < m.ColumnCount; c++)
            if (ColumnDiffers(ap, ai, av, bp, bi, bv, c)) nz.Add(c);
        if (nz.Count == 0) return ([], new Complex[n, 0]);

        // Pass 2: device order first, the rest ascending (they are already ascending in nz).
        var cols = new List<int>(nz.Count);
        var seen = new HashSet<int>(nz);
        if (preferredOrder is not null)
            foreach (int c in preferredOrder)
                if (seen.Remove(c)) cols.Add(c);
        foreach (int c in nz)
            if (seen.Contains(c)) { cols.Add(c); seen.Remove(c); }

        // Pass 3: the dense difference of those columns.
        var u = new Complex[n, cols.Count];
        for (int k = 0; k < cols.Count; k++)
        {
            int c = cols[k];
            for (int p = ap[c]; p < ap[c + 1]; p++) u[ai[p], k] += av[p];
            for (int p = bp[c]; p < bp[c + 1]; p++) u[bi[p], k] -= bv[p];
        }
        return ([.. cols], u);
    }

    private static bool ColumnDiffers(
        int[] ap, int[] ai, Complex[] av, int[] bp, int[] bi, Complex[] bv, int c)
    {
        int x = ap[c], xe = ap[c + 1], y = bp[c], ye = bp[c + 1];
        while (x < xe && y < ye)
        {
            if (ai[x] == bi[y])
            {
                if (!av[x].Equals(bv[y])) return true;
                x++; y++;
            }
            else if (ai[x] < bi[y])
            {
                if (av[x] != Complex.Zero) return true;
                x++;
            }
            else
            {
                if (bv[y] != Complex.Zero) return true;
                y++;
            }
        }
        for (; x < xe; x++) if (av[x] != Complex.Zero) return true;
        for (; y < ye; y++) if (bv[y] != Complex.Zero) return true;
        return false;
    }

    /// <summary>
    /// <c>NDF = det(I_r + Eᵀ·M0⁻¹·U)</c> at one frequency. <paramref name="luPassive"/> is the
    /// factorisation of <c>M0</c> — the only factorisation this costs, and it is the one the caller
    /// needed anyway.
    /// </summary>
    /// <returns>
    /// The NDF, and the number of back-substitutions performed (<c>r</c>, for the structural
    /// counters of R-wsp6-9(j)). An empty <paramref name="cols"/> — a circuit with no dependent
    /// source anywhere — gives exactly <c>1</c>, which is the right answer: a passive network's
    /// determinant IS its passive determinant.
    /// </returns>
    internal static (Complex Ndf, int Solves) Evaluate(
        SparseLU luPassive, int[] cols, Complex[,] u, Complex[] xBuf, Complex[] bBuf)
    {
        int r = cols.Length;
        if (r == 0) return (Complex.One, 0);

        int n = u.GetLength(0);
        var f = new Complex[r, r];
        for (int k = 0; k < r; k++)
        {
            Array.Clear(bBuf);
            for (int i = 0; i < n; i++) bBuf[i] = u[i, k];
            luPassive.Solve(bBuf, xBuf);
            for (int i = 0; i < r; i++) f[i, k] = xBuf[cols[i]];
        }
        for (int i = 0; i < r; i++) f[i, i] += Complex.One;

        return (WspMatrix.Determinant(f), r);
    }

    // ── Platzker's five properties, as checks the engine performs on its own output ──

    /// <summary>
    /// The worst violation of <c>Y + Yᴴ ⪰ 0</c> for one device block, NORMALISED by <c>‖Y‖₂</c>:
    /// zero or positive when the block is passive, negative by the size of the failure when it is
    /// not. <c>null</c> when the block is identically zero (there is nothing to violate).
    ///
    /// <para><b>How the minimum eigenvalue is read off an SVD.</b> <c>H = Y + Yᴴ</c> is Hermitian,
    /// so its singular values are the magnitudes of its eigenvalues and cannot tell a negative one
    /// from a positive one. Shifting by <c>s = σ_max(H)</c> makes <c>H + s·I</c> positive
    /// semidefinite — every eigenvalue lands in <c>[0, 2s]</c> — so ITS singular values ARE its
    /// eigenvalues, and <c>λ_min(H) = σ_min(H + s·I) − s</c>. One extra SVD of a matrix at most a
    /// handful of ports wide.</para>
    /// </summary>
    internal static double? PassivityMargin(Complex[,] y)
    {
        int n = y.GetLength(0);
        var h = new Mat<Complex>(n, n);
        double scale = 0.0;
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                h[i, j] = y[i, j] + Complex.Conjugate(y[j, i]);
                scale   = Math.Max(scale, y[i, j].Magnitude);
            }
        if (scale == 0.0) return null;

        double s = h.Svd().S[0];
        if (s == 0.0) return 0.0;
        for (int i = 0; i < n; i++) h[i, i] += s;
        double minEig = h.Svd().S[n - 1] - s;
        return minEig / (2.0 * scale);
    }
}
