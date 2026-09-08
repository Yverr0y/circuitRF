using CircuitRF.Core.Devices;
using CircuitRF.Core.Devices.External;

namespace CircuitRF.Core.Netlist;

/// <summary>
/// How many nets a primitive's <c>.cnl</c> instance line binds — <c>R:R1 n1 n2 R=1k</c> is two, and
/// <c>Tuner:T1 n_drain 0 …</c> is two even though <c>TunerModel.PortCount</c> is one.
///
/// <para><b>Why this is not <see cref="ComponentModel.PortCount"/> (AUT-8 R-aut8-4, AUT-10 R-aut10-2).</b>
/// "Port" means three different things across this codebase and none of them is "net". A resistor's
/// two ports are its two terminals; a FET's two ports are (gate,source) and (drain,source), which is
/// three nets; an ideal S-block's ports each take a signal net and a reference net, which is two nets
/// each; a Tuner declares one port and takes two nets, because its reference terminal is implicit on
/// the glyph. Reading a net count off <c>PortCount</c> is how the generated catalogue came to
/// describe <c>Tuner</c> as a one-net part — and a client that wrote it that way got a circuit whose
/// bias tee delivered nothing, <c>status: ok</c>, and Pout at the engine's floor sentinel at every
/// drive point. It reported the model as broken. The model is correct.</para>
///
/// <para><b>What used to happen with the wrong count.</b> Nothing, loudly. A one-net resistor reached
/// <c>ResistorModel.Stamp</c>, which indexes <c>c.Nodes[1]</c>, and surfaced as
/// <c>Index was outside the bounds of the array</c> — naming neither the line, the instance nor the
/// count. A one-net <c>Tuner</c> did not even do that: the elaborator's own family expansions are
/// guarded by <c>resolvedNodes.Length == 3</c> and friends, so a short line SKIPS the expansion in
/// silence and a wired-wrong circuit simulates to completion.</para>
///
/// <para><b>A null is a deliberate blank, not an omission.</b> Every type in
/// <c>ComponentModelFactory</c>'s registry is accounted for here; the ones that return null return it
/// because the count genuinely is not knowable from the model alone, and each says why.
/// <c>InstanceNetContractTests</c> holds the table exhaustive over the registry, so a new primitive
/// cannot quietly become a third category — which is the shape of defect this whole brief removes.</para>
/// </summary>
public static class InstanceNetContract
{
    /// <summary>
    /// The number of nets this model's instance line must bind, or null when the model cannot state
    /// one. Asked of the CONSTRUCTED model, so the parameterised types answer from their own resolved
    /// parameters rather than from a number written down twice.
    /// </summary>
    public static int? Expected(ComponentModel model) => model switch
    {
        // ── Two-terminal linear parts. PortCount is the net count for these, and only these. ──
        ResistorModel or CapacitorModel or InductorModel or SeriesRlcModel or ParallelRlcModel
            or BeadModel or ShortModel or SemiCapacitorModel or MatchModel                    => 2,

        // Port and Term take a signal net and a reference net. The schematic draws one pin and the
        // extractor appends "0", which is why the symbol's pin count is not this number.
        PortModel or TermModel                                                                => 2,

        // ── Distributed and transmission-line parts ──
        TLineModel or MicrostripLineModel or MicrostripBendModel
            or MicrostripTaperModel or MicrostripKlopfModel                                   => 2,
        MicrostripTeeModel                                                                    => 3,
        MicrostripCrossModel                                                                  => 4,

        // The mutual-inductance element names two inductors by parameter, not two nets by position.
        MutualInductanceModel                                                                 => 0,

        // ── Sources, probes and terminations whose reference terminal is implicit on the glyph ──
        // Each declares ONE port and takes TWO nets. This is the family the catalogue got wrong.
        VdcModel or IProbeModel or TunerModel or ToneSourceModelBase
            or P1ToneModel or PnToneModel or NonlinearCModel                                  => 2,

        // ── Controlled sources: an output pair and a control pair, two ports of two nets each ──
        VccsModel or VcvsModel                                                                => 4,

        // The cascade block is a two-port with an explicit reference at each end.
        ChainModel                                                                            => 4,

        // ── Ideal system blocks: every RF port is a signal net and a reference net ──
        // Atten is 2 ports → 4 nets; Balun and Circulator 3 → 6; Coupler 4 → 8.
        IdealSBlockModel sb                                                                   => 2 * sb.PortCount,
        DuplexerModel dx                                                                      => 2 * dx.PortCount,
        MixerModel mx                                                                         => 2 * mx.PortCount,

        // ── Semiconductor families. The count is what the USER DRAWS, before the elaborator expands
        // it into the model's internal port pairs — which is the only count an instance line states.
        DiodeModel                                                                            => 2,
        Devices.Fet.FetModelBase                                                              => 3,   // gate, drain, source
        BjtModel                                                                              => 3,   // collector, base, emitter
        Devices.Igbt.IgbtModel                                                                => 3,   // collector, gate, emitter
        Devices.Mos.VdmosModel                                                                => 3,   // drain, gate, source
        Devices.Jfet.JfetModel                                                                => 3,   // drain, gate, source
        Devices.Mos.MosfetModelBase                                                           => 4,   // drain, gate, source, bulk

        // ── Parameterised counts, answered from the model's own resolved state ──
        // Two nets per port, both differential: this is the case CLAUDE.md already calls out as
        // differing from the symbol's pin count by construction.
        SddModel sdd                                                                          => 2 * sdd.PortCount,
        ZPortModel zp                                                                         => 2 * zp.PortCount,
        // ── Deliberately unstated, because a better statement of the rule already exists ──
        // wBond is an N-or-N+1 part like SnP: 2M nets for an M-wire array, with an OPTIONAL trailing
        // reference net that the model reads when RefPin says so and ignores otherwise. PortCount
        // looks like the net count and is not — it counts the reference pin only when the parameter
        // turned it on, while the netlist may supply the net either way. WBondModel's own refusals
        // (the ground-plane return path among them) name the array and the missing return.
        WBondModel                                                                            => null,
        // SnP takes N nets or N+1 (the trailing one being a floating reference). CnlReader's own
        // ValidateSnpNets names the instance and both legal counts; a number here would be a second,
        // wrong statement of a rule that is not a number.
        SnpModel                                                                              => null,
        // ExtDevice's count is the provider's EXTERNAL pin count, which is not PortCount — that
        // includes the internal nodes the model asked for. It is also not fixed per type: a component
        // may state a smaller ConnectedPinCount to place a five-terminal model as a four-pin part.
        // Elaborator.BuildExternalDeviceNodes refuses a mismatch already, naming the type id, the
        // pin count and which of the two rules it was measured against.
        ExternalDeviceModel                                                                   => null,

        // Anything not listed. Not silence: InstanceNetContractTests fails on a registered type that
        // lands here, so a new primitive is either given a count or given a reason in this file.
        _                                                                                     => null,
    };

    /// <summary>
    /// The refusal text for a line whose net count is wrong. Names the type, the instance, what was
    /// written and what is needed — all four, because the caller of a headless verb has no line to
    /// look at and the count alone does not say which end is wrong.
    /// </summary>
    public static string Refusal(string typeName, string instanceName, int given, int expected)
    {
        int shortBy = expected - given;
        string remedy = shortBy > 0
            ? (shortBy == 1 ? "Add the missing net" : $"Add the {shortBy} missing nets") +
              " before the first 'name=value' on the line."
            : "Nets come before the first 'name=value' on the line; anything after one is read as a unit.";

        return $"{typeName}:{instanceName} is wired to {given} {(given == 1 ? "net" : "nets")}, " +
               $"and a {typeName} takes {expected}. {remedy}";
    }
}
