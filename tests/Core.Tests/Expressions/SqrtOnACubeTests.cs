// ================================================================
//  SqrtOnACubeTests.cs — sqrt() over a swept quantity, which had no spelling at all
//
//  Found writing the shipped CoupledInductors example (2026-09-15): a coupling factor read out of a
//  two-port is k = M/sqrt(L1*L2), and every way of writing that square root over a frequency-swept
//  cube failed with "Value is Cube, not Complex":
//
//      sqrt(x)              -> UnaryMath, scalar only
//      x^0.5 / pow(x, 0.5)  -> Value.Pow,  scalar only
//      exp(0.5*log(x))      -> log IS cube-aware, exp is not
//
//  So sqrt is the one made cube-aware, and these tests pin both halves of what that means: the
//  element-wise answer, and the Real -> Complex promotion on a negative element, which is the rule
//  Value.Pow already applies to a negative base with a fractional exponent. The two spellings of a
//  square root have to agree about that or the choice between them changes the answer.
// ================================================================

using System;
using System.Collections.Generic;
using System.Numerics;
using CircuitRF.Core.Expressions;
using RfCore.Data;
using Xunit;

namespace CircuitRF.Core.Tests.Expressions;

public class SqrtOnACubeTests
{
    private static readonly Axis Freq = new("freq", [1e9, 2e9, 3e9], "Hz");

    private static (MeasurementContext Ctx, Scope Scope) With(params (string Name, DataCube Cube)[] cubes)
    {
        var ds = new DataSet();
        foreach (var (name, cube) in cubes) ds.Add(name, cube);
        return (new MeasurementContext(new Dictionary<string, DataSet> { ["SP1"] = ds }), new Scope("t"));
    }

    private static DataCube Real(params double[] v) => new([Freq], v);
    private static DataCube Cplx(params Complex[] v) => new([Freq], v);

    private static DataCube Eval(string expr, params (string, DataCube)[] cubes)
    {
        var (ctx, scope) = With(cubes);
        return new Evaluator(ctx).Eval(expr, scope).AsCube();
    }

    // ── The element-wise answer ───────────────────────────────────────────────────────────────

    [Fact]
    public void ARealCube_TakesTheRealSquareRoot_AndStaysReal()
    {
        var r = Eval("sqrt(SP1.x)", ("x", Real(4.0, 9.0, 16.0)));

        Assert.Equal(DataKind.Real, r.DataKind);
        Assert.Equal([2.0, 3.0, 4.0], r.RealValues);
        // The axis rides through untouched — a transform is the same quantity at the same points.
        Assert.Equal("freq", r.Axes[0].Name);
        Assert.Equal(Freq.Values, r.Axes[0].Values);
    }

    [Fact]
    public void AComplexCube_TakesThePrincipalBranch()
    {
        var r = Eval("sqrt(SP1.x)", ("x", Cplx(new(0, 4), new(-9, 0), new(16, 0))));

        Assert.Equal(DataKind.Complex, r.DataKind);
        var got = r.ComplexValues;
        Assert.Equal(Complex.Sqrt(new Complex(0, 4)).Real,      got[0].Real,      12);
        Assert.Equal(Complex.Sqrt(new Complex(0, 4)).Imaginary, got[0].Imaginary, 12);
        // sqrt(-9) on the principal branch is +3j, not -3j and not NaN.
        Assert.Equal(0.0, got[1].Real,      12);
        Assert.Equal(3.0, got[1].Imaginary, 12);
        Assert.Equal(4.0, got[2].Real,      12);
    }

    /// <summary>
    /// <b>A negative element PROMOTES the whole cube to Complex</b> rather than producing NaN — the
    /// rule <see cref="Value.Pow"/> already applies to a negative base, so <c>sqrt(x)</c> and
    /// <c>x^0.5</c> cannot disagree about it. Promoting the WHOLE cube rather than per element is
    /// forced: a cube is single-kind by construction.
    /// </summary>
    [Fact]
    public void ARealCubeWithANegativeElement_PromotesToComplex_RatherThanGoingNaN()
    {
        var r = Eval("sqrt(SP1.x)", ("x", Real(4.0, -9.0, 16.0)));

        Assert.Equal(DataKind.Complex, r.DataKind);
        var got = r.ComplexValues;
        Assert.Equal(2.0, got[0].Real, 12);
        Assert.Equal(0.0, got[1].Real,      12);
        Assert.Equal(3.0, got[1].Imaginary, 12);
        Assert.Equal(4.0, got[2].Real, 12);
        Assert.All(got, z => Assert.False(double.IsNaN(z.Real) || double.IsNaN(z.Imaginary)));
    }

    // ── The scalar behaviour is UNCHANGED ─────────────────────────────────────────────────────

    [Fact]
    public void AScalarStillBehavesExactlyAsBefore()
    {
        var ev = new Evaluator();
        Assert.Equal(3.0, ev.Eval("sqrt(9)", new Scope("t")).AsReal(), 12);
        // A negative REAL scalar is still a refusal by name, not a silent promotion: the scalar path
        // has said so since the language shipped and nothing here widens it.
        Assert.Throws<DomainException>(() => ev.Eval("sqrt(0-9)", new Scope("t")));
    }

    // ── The case it was written for ───────────────────────────────────────────────────────────

    /// <summary>
    /// k = M/√(L₁L₂) over a sweep, on the numbers the shipped CoupledInductors example uses —
    /// ωL₁ = ωL₂ = ω·1 nH and ωM = ω·500 pH, so k is 0.5 at every frequency and the ω cancels.
    /// </summary>
    [Fact]
    public void ACouplingFactorOverASweep_IsOneExpression()
    {
        double[] w = [2 * Math.PI * 1e9, 2 * Math.PI * 2e9, 2 * Math.PI * 3e9];

        var r = Eval("SP1.wM/sqrt(SP1.wL1*SP1.wL2)",
            ("wL1", Real(w[0] * 1e-9, w[1] * 1e-9, w[2] * 1e-9)),
            ("wL2", Real(w[0] * 1e-9, w[1] * 1e-9, w[2] * 1e-9)),
            ("wM",  Real(w[0] * 500e-12, w[1] * 500e-12, w[2] * 500e-12)));

        Assert.All(r.RealValues, k => Assert.Equal(0.5, k, 12));
    }
}
