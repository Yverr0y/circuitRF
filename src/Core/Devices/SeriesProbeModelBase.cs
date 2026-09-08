using System.Numerics;
using CircuitRF.Core.Elaboration;

namespace CircuitRF.Core.Devices;

/// <summary>
/// The zero-volt series ammeter every probe that sits IN a wire is built on: one branch unknown,
/// the constraint <c>V(first) − V(second) = 0</c>, and the KCL coupling with the branch current
/// flowing first node → second node (the engine's own branch convention, <c>src/Engine/CLAUDE.md</c>).
///
/// <para><b>Why a base rather than a second copy.</b> <see cref="IProbeModel"/> and
/// <see cref="WSProbeModel"/> are electrically the same element — a probe that perturbs nothing —
/// and differ only in what the analyses read off them: an IProbe reports its branch current, a
/// WSProbe additionally has the S-parameter engine inject through it. Every engine site that used to
/// pattern-match <c>IProbeModel</c> (the DC packer's <c>I:&lt;label&gt;</c> cubes, the HB back-solved
/// <c>I</c> cube and <c>__ProbeBranches</c>, the SDD control-current resolvers) matches this base
/// instead, so a WSProbe is transparent to the analyses it does not serve exactly as an IProbe is —
/// brief-wsprobe-1 R-wsp1-1 and its gate R-wsp1-14(m).</para>
/// </summary>
public abstract class SeriesProbeModelBase : ComponentModel
{
    public override int       PortCount => 1;
    public override ModelKind Kind      => ModelKind.Linear;

    /// <summary>Branch index set on the most recent Stamp call. Used by HbLinearBackSolver and by
    /// the S-parameter engine's probe injections.</summary>
    public int LastBranchIndex { get; private set; } = -1;

    public override void Stamp(IMnaContext mna, ElaboratedComponent c, double omega)
    {
        if (c.Nodes.Length < 2) return;
        int np = c.Nodes[0];
        int nm = c.Nodes[1];

        int br = mna.AddBranch();
        LastBranchIndex = br;

        mna.AddConstraint(br, np, new Complex(+1, 0));
        mna.AddConstraint(br, nm, new Complex(-1, 0));
        mna.AddSourceValue(br, Complex.Zero);   // V = 0 (ideal ammeter)
        mna.AddBranchCurrent(br, np, nm);
    }
}
