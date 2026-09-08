using System.Numerics;
using CircuitRF.Core.Elaboration;
using CircuitRF.Core.Expressions;

namespace CircuitRF.Core.Devices;

/// <summary>
/// A two-port given by its ABCD (chain / transmission) matrix, with entries as expressions in the
/// reserved keyword <c>freq</c> (Hz) — the same expression convention <see cref="ZPortModel"/> uses.
///
/// <para>Convention (standard, with both port currents defined flowing INTO the device so it matches
/// every other model here):</para>
/// <code>
///   V1 = A·V2 − B·I2
///   I1 = C·V2 − D·I2
/// </code>
/// <para>where V1 = V(Nodes[0]) − V(Nodes[1]) and V2 = V(Nodes[2]) − V(Nodes[3]). Four nets, ordered
/// as ± pairs exactly like a 2-port <c>Z_Port</c>: <c>[p1+, p1−, p2+, p2−]</c>.</para>
///
/// <para><b>Why this exists when Z_Port already does.</b> A chain matrix describes two-ports that
/// have no impedance matrix at all. The commonest case is the one that matters: a pure series
/// element has C = 0, and Z-parameters are then infinite — <c>Z11 = A/C</c>. Many frequency-domain
/// line models degenerate to exactly that at DC (A = D = 1, C = 0, B = the series resistance), so a
/// model that is perfectly well-behaved in ABCD form cannot be expressed as a Z-block at ω = 0.
/// Stamping the chain relations directly avoids the conversion entirely and stays non-singular
/// there — with C = 0 and D = 1 the second constraint reduces to I1 = −I2 and the first to
/// V1 − V2 = B·I1, which is just a series impedance.</para>
///
/// <para>Group 2: two branch-current unknowns (I1, I2), one per port.</para>
/// </summary>
public sealed class ChainModel : ComponentModel
{
    public override int       PortCount => 2;
    public override ModelKind Kind      => ModelKind.Linear;

    private readonly Expr?  _a, _b, _c, _d;
    /// <summary>
    /// Netlist-declared expression functions, or null. Needed because this model builds its
    /// evaluator at stamp time — long after elaboration — and one constructed empty cannot resolve
    /// a call to a function the netlist declared.
    /// </summary>
    private readonly IReadOnlyList<UserFunction>? _functions;

    private readonly IReadOnlyDictionary<string, Value> _scopeVars;
    private readonly string _name;

    /// <summary>Branch indices for port 1 and port 2, set during each Stamp. -1 before first stamp.</summary>
    public int[] PortBranchIndices { get; } = [-1, -1];

    public ChainModel(Expr? a, Expr? b, Expr? c, Expr? d,
        IReadOnlyDictionary<string, Value> scopeVars, string name,
        IReadOnlyList<UserFunction>? functions = null)
    {
        _a = a; _b = b; _c = c; _d = d;
        _scopeVars = scopeVars;
        _functions = functions;
        _name      = name;
    }

    // ── The NDF passivation contract (brief-wsprobe-6 §3) ────────────────────

    /// <summary>
    /// <see cref="Core.Activity.BlackBox"/> at the TYPE level: a user-written ABCD can express gain
    /// as freely as an S-parameter file can, and there is no transconductance in it to zero.
    /// <see cref="ActivityFor"/> measures the block instead.
    /// </summary>
    public override Activity Activity => Activity.BlackBox;

    /// <inheritdoc/>
    public override string? PassivationNote => _passivationNote;
    private string? _passivationNote;

    /// <summary>
    /// σ_max of the block's own S at every frequency the run will visit — the same test
    /// <c>SnpModel</c> applies to its file, over this block's evaluated ABCD instead. At or below
    /// <c>1 + 1e-6</c> the block is passive; above it, the run is refused by name.
    ///
    /// <para>Over the RUN's grid, not a file's, because a Chain has no grid of its own: its ABCD is
    /// a function of <c>freq</c> and is only defined where it is asked.</para>
    /// </summary>
    public override Activity ActivityFor(ElaboratedComponent c, IReadOnlyList<double> freqsHz)
    {
        const double Tol = 1e-6, Z0 = 50.0;
        double worst = 0.0, at = double.NaN;
        foreach (double hz in freqsHz)
        {
            var (a, b, cc, d) = Evaluate(hz);
            // ABCD → S at a common real reference (Pozar Table 4.2). The reference cancels out of
            // the "is σ_max ≤ 1" question — a passive block is passive at every reference.
            Complex den = a + b / Z0 + cc * Z0 + d;
            if (den == Complex.Zero) { worst = double.PositiveInfinity; at = hz; break; }
            Complex s11 = (a + b / Z0 - cc * Z0 - d) / den;
            Complex s12 = 2.0 * (a * d - b * cc) / den;
            Complex s21 = 2.0 / den;
            Complex s22 = (-a + b / Z0 - cc * Z0 + d) / den;
            double  sig = LargestSingularValue2x2(s11, s12, s21, s22);
            if (sig > worst) { worst = sig; at = hz; }
        }

        if (worst <= 1.0 + Tol)
        {
            _passivationNote = $"σ_max = {worst:G4} ≤ 1 over the swept band";
            return Activity.Passive;
        }
        _passivationNote =
            $"σ_max = {worst:G4} > 1 at {at / 1e9:G6} GHz — an ABCD block with gain hides its " +
            "dependent sources exactly as an S-parameter file with gain does";
        return Activity.BlackBox;
    }

    /// <summary>
    /// σ_max of a 2×2 complex matrix, from the closed form for the eigenvalues of <c>SᴴS</c> — a
    /// 2×2 needs no SVD and this keeps <c>src/Core</c> free of a dense-linear-algebra dependency it
    /// otherwise has no use for.
    /// </summary>
    private static double LargestSingularValue2x2(Complex s11, Complex s12, Complex s21, Complex s22)
    {
        // trace and determinant of SᴴS, both real
        double tr = s11.Magnitude * s11.Magnitude + s12.Magnitude * s12.Magnitude
                  + s21.Magnitude * s21.Magnitude + s22.Magnitude * s22.Magnitude;
        double det = (s11 * s22 - s12 * s21).Magnitude;
        double disc = Math.Max(0.0, tr * tr - 4.0 * det * det);
        return Math.Sqrt(0.5 * (tr + Math.Sqrt(disc)));
    }

    public override void Stamp(IMnaContext mna, ElaboratedComponent comp, double omega)
    {
        double freqHz = omega / (2.0 * Math.PI);
        var (a, b, c, d) = Evaluate(freqHz);

        int n1p = comp.Nodes[0], n1m = comp.Nodes[1];
        int n2p = comp.Nodes[2], n2m = comp.Nodes[3];

        int br1 = mna.AddBranch();
        int br2 = mna.AddBranch();
        PortBranchIndices[0] = br1;
        PortBranchIndices[1] = br2;

        // KCL: each port current flows from that port's + node to its − node.
        mna.AddBranchCurrent(br1, n1p, n1m);
        mna.AddBranchCurrent(br2, n2p, n2m);

        // Constraint 1:  V1 − A·V2 + B·I2 = 0
        mna.AddConstraint(br1, n1p, Complex.One);
        if (n1m > 0) mna.AddConstraint(br1, n1m, -Complex.One);
        mna.AddConstraint(br1, n2p, -a);
        if (n2m > 0) mna.AddConstraint(br1, n2m, a);
        mna.AddBranchConstraint(br1, br2, b);

        // Constraint 2:  I1 − C·V2 + D·I2 = 0
        mna.AddBranchConstraint(br2, br1, Complex.One);
        mna.AddConstraint(br2, n2p, -c);
        if (n2m > 0) mna.AddConstraint(br2, n2m, c);
        mna.AddBranchConstraint(br2, br2, d);
    }

    // ABCD(freq) is a PURE function of freq for the life of the model — the expressions, the
    // resolved scope variables and the declared functions are all fixed at construction. The memo
    // is therefore exact, and it is what keeps a repeated stamp of one topology (the HB extractor
    // re-stamps the linear partition once per harmonic per solve; a loadpull runs hundreds of
    // solves) from rebuilding a Scope and an Evaluator and re-injecting every global each time.
    // Same reasoning, same shape, as ZPortModel.EvaluateZ.
    private Dictionary<double, (Complex A, Complex B, Complex C, Complex D)>? _abcdCache;
    private Scope?     _scope;
    private Evaluator? _ev;

    private (Complex A, Complex B, Complex C, Complex D) Evaluate(double freqHz)
    {
        _abcdCache ??= [];
        if (_abcdCache.TryGetValue(freqHz, out var cached)) return cached;

        if (_scope is null || _ev is null)
        {
            _scope = new Scope($"Chain:{_name}");
            foreach (var kv in _scopeVars)
                _scope.Bind(kv.Key, kv.Value.ToString()!);

            _ev = new Evaluator();
            foreach (var fn in _functions ?? []) _ev.RegisterFunction(fn);
            foreach (var kv in _scopeVars)
                _ev.InjectResolved($"Chain:{_name}", kv.Key, kv.Value);
        }

        _scope.Bind("freq", freqHz.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        _ev.InjectResolved($"Chain:{_name}", "freq", new Value(freqHz));

        Complex One(Expr? e, Complex fallback)
        {
            if (e is null) return fallback;
            var v = _ev.EvalExpr(e, _scope);
            return v.Kind == ValueKind.Real ? new Complex(v.AsReal(), 0) : v.AsComplex();
        }

        // Defaults form the identity two-port (a through connection), so a partially-specified
        // chain block degrades to a wire rather than to a silent zero matrix.
        var abcd = (One(_a, Complex.One), One(_b, Complex.Zero),
                    One(_c, Complex.Zero), One(_d, Complex.One));
        _abcdCache[freqHz] = abcd;
        return abcd;
    }
}
