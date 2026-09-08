namespace CircuitRF.Core.Devices;

/// <summary>
/// The WSProbe — T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023), §4 and
/// Fig. 15/16. A two-terminal series element with an orientation, placed IN a node so that it
/// splits the node into a generator-side (<c>G</c>) and a load-side (<c>L</c>) terminal.
///
/// <para><b>Electrically it is an <see cref="IProbeModel"/>:</b> a 0 V short that perturbs nothing
/// (§4, p. 33: "completely nonperturbative … requiring only a single analysis sweep"). Its value is
/// not the element but the <c>wsp</c> matrix the S-parameter engine produces from it (§4.1,
/// Eq. 31–36): for each probe the engine applies a unit series voltage <c>vS</c> in the probe branch
/// (− at G, + at L) and a unit shunt current <c>iP</c> into the G-side node, and reads at every probe
/// the branch current <c>iS</c> (flowing G → L) and the G-side node voltage <c>vP</c>. Those four
/// transfer functions per probe pair are the whole of the document's method; everything else is
/// post-processing of <c>wsp</c> (<c>docs/design/stability-wsprobe.md</c>).</para>
///
/// <para>The document's <c>iS</c> IS this element's branch current in the engine's own sign
/// convention (branch current flows first node → second node, and the first node is G), which is
/// why nothing about the stamp differs from the IProbe's.</para>
///
/// <para>No parameters in v1: a <c>Z0</c> for the synthetic circulator (Eq. 96–99) is a FUNCTION
/// argument there, not a probe property.</para>
///
/// .cnl syntax: <c>WSProbe:&lt;label&gt;  nG  nL</c> — the first net is G, the second L, the same
/// first/second rule <c>IProbe np nm</c> already uses. The label is the document's "Label";
/// <c>idx</c> (Eq. 50–52) is assigned by the elaborator in flattened netlist order, 1-based, and
/// reported in the run's <c>__WspProbes</c> cube so it is never guessed.
/// </summary>
public sealed class WSProbeModel : SeriesProbeModelBase
{
    public override string[] TerminalNames => ["G", "L"];
}
