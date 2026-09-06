using CircuitRF.Ui.Schematic;

namespace CircuitRF.Ui.Commands.Schematic;

/// <summary>
/// Sets one component's per-instance pin-name override.
///
/// <para>Separate from <see cref="SetLabelVisibilityCommand"/> because the value is a
/// <c>bool?</c>: undoing a toggle has to restore <b>null</b> — "follow the symbol" — and not merely
/// the opposite boolean, or the first toggle would permanently pin an instance to whatever its
/// symbol happened to say at the time.</para>
/// </summary>
internal sealed class SetPinNameVisibilityCommand : IUiCommand
{
    private readonly SchematicEditModel _model;
    private readonly EditableComponent  _comp;
    private readonly bool?              _newValue;
    private readonly bool?              _oldValue;

    public string Description =>
        $"{(_newValue is true ? "Show" : "Hide")} pin names on {_comp.InstanceName}";

    public SetPinNameVisibilityCommand(SchematicEditModel model, EditableComponent comp, bool? newValue)
    {
        _model    = model;
        _comp     = comp;
        _newValue = newValue;
        _oldValue = comp.ShowPinNames;
    }

    public void Execute() => Apply(_newValue);
    public void Undo()    => Apply(_oldValue);

    private void Apply(bool? value)
    {
        _comp.ShowPinNames = value;
        _model.NotifyChanged();
    }
}
