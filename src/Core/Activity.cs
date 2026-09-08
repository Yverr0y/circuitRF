namespace CircuitRF.Core;

/// <summary>
/// How a component model participates in the <b>normalized determinant function</b> — whether it
/// contains a dependent source, and if so whether circuitRF knows enough about that source to
/// render it passive exactly (brief-wsprobe-6 R-wsp6-3; overview D-11).
///
/// <para>The NDF is <c>Δ / Δ0 = |Y| / |Y_passive|</c> (T. A. Winslow, <i>General Circuit Analysis
/// Using The WSProbe</i> (2023), Eq. 181; Bode Eq. 16), where <c>Y_passive</c> is the same network
/// with <b>every dependent source, negative resistance and non-Foster element rendered passive</b>
/// (§8, p. 111). <c>Δ0</c> then has no right-half-plane zeros by construction, and the clockwise
/// encirclements of the origin by <c>NDF(jω)</c> count the network's right-half-plane poles.</para>
///
/// <para><b>Why circuitRF can compute this and the document's designer usually cannot</b> (§8
/// p. 113, §5.4 pp. 95–99): the passive determinant "requires having precise access to the
/// transconductance elements in all active devices", which a black-box vendor model withholds.
/// Every built-in active model in <c>src/Core/Devices</c> is circuitRF's own and knows its own
/// controlled sources exactly — so it declares <see cref="ActiveExact"/> and stamps the passivated
/// form itself. A model whose activity lives in user-supplied equations declares
/// <see cref="ActiveUserScaled"/> and is passivated through a variable the user names. Anything
/// else is a <see cref="BlackBox"/> and the run is <b>refused by name</b> rather than given a
/// silently passive-looking NDF.</para>
/// </summary>
public enum Activity
{
    /// <summary>
    /// No dependent source: the model's ordinary stamp already IS its passive stamp, so the base
    /// <c>StampPassive</c>/<c>StampLinearizedPassive</c> forwarding is correct by definition.
    /// </summary>
    Passive,

    /// <summary>
    /// Contains a dependent source (or a negative resistance) that this model knows exactly. It
    /// <b>must</b> override <c>StampPassive</c> or <c>StampLinearizedPassive</c> — a reflection test
    /// over every <c>ComponentModel</c> subclass asserts it (R-wsp6-9(h)), so the next active device
    /// cannot be added without deciding its passivation.
    /// </summary>
    ActiveExact,

    /// <summary>
    /// The activity lives in equations or a binary this repository did not write, and can only be
    /// switched off through a scaling quantity the USER names: a global in <c>PassiveVars=</c> (the
    /// SDD) or an instance parameter in <c>PassiveParams=</c> (Verilog-A, an external device). With
    /// no such entry reaching the instance the run is refused — an unpassivated device would make
    /// <c>Δ0</c> active and the encirclement count meaningless.
    /// </summary>
    ActiveUserScaled,

    /// <summary>
    /// The model cannot be passivated at all: an S-parameter block with gain hides its dependent
    /// sources inside its data. The run is refused, naming the instance.
    /// </summary>
    BlackBox,
}
