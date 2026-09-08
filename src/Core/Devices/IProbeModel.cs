namespace CircuitRF.Core.Devices;

/// <summary>
/// Zero-volt series ammeter (IProbe). Stamps a 0 V branch constraint; the branch current
/// is the measurement quantity, accessible via I(probeName) in measurement expressions.
///
/// Semantically identical to a 0 V VoltageSourceModel; separated so the branch current
/// is always available by the probe's instance name rather than a node label.
///
/// The stamp lives in <see cref="SeriesProbeModelBase"/>, which the WSProbe shares — the two are
/// one element electrically, and the base is what every engine site matches so both report alike.
///
/// .cnl syntax: IProbe:IP1 n_plus n_minus
/// </summary>
public sealed class IProbeModel : SeriesProbeModelBase
{
}
