using CircuitRF.Ui.Layout;

namespace CircuitRF.Ui.Commands.Layout;

/// <summary>
/// Generic undoable field-set for a property of anything this <see cref="LayoutView"/> owns — mirrors
/// <c>SetSymbolPrimitiveFieldCommand&lt;T&gt;</c>. Stores old + new value; Execute applies new, Undo
/// applies old. The apply closure mutates the target directly (the same reference the view holds). A
/// multi-selection edit chains one of these per item via <c>CompositeCommand</c> so the whole edit is
/// a single undo entry.
///
/// <para><b>The name says <c>Shape</c>; the type is not limited to one</b> (docs/design/layout-view.md
/// §9B, R-rul-11a). Its body only ever touches the view for <see cref="LayoutView.NotifyChanged"/> and
/// mutates through a caller-supplied closure, so a <see cref="RulerAnnotation"/> — deliberately NOT a
/// <see cref="LayoutShape"/>, see §9B.1 — uses it verbatim, and the Properties Inspector's ruler
/// multi-edit is the same one-command-per-item-into-a-CompositeCommand fold as every shape field.
/// (Renaming it to <c>SetLayoutFieldCommand&lt;T&gt;</c> would be the honest name and is a small
/// mechanical change across three files; it is not required and was not done.)</para>
/// </summary>
internal sealed class SetShapeFieldCommand<T> : IUiCommand
{
    private readonly LayoutView _view;
    private readonly string _description;
    private readonly T _oldValue;
    private readonly T _newValue;
    private readonly Action<T> _apply;
    private readonly LayoutChangeInfo? _change;

    public string Description => _description;

    /// <param name="change">
    /// What to raise on <see cref="LayoutView.NotifyChanged"/>. Null — every caller's default —
    /// means <c>Full</c>, which is the safe answer for a field nobody has thought about.
    ///
    /// <para><b>Pass <see cref="LayoutChangeInfo.Updated"/> for a field that changes neither the
    /// shape list's CONTENT nor its ORDER</b>, and only for such a field. It is not an optimization:
    /// <c>LayoutEditorViewModel</c>'s own change handler CLEARS the <c>.cem</c>'s published
    /// internal-port marks on any kind but <c>Updated</c>, because anything else can renumber the
    /// ports and make "which .cem row means which label" unknowable. Re-seating a dragged port's
    /// direction or its <see cref="LabelShape.PortLayer"/> renumbers nothing — but it rode a
    /// <c>Full</c> here, so committing such a drag wiped the marks and every internal port in the
    /// drawing snapped to an edge port's bar-and-arrow until the <c>.cem</c> caught up. That is the
    /// same flash the handler's own note describes; this is the other way in.</para>
    /// </param>
    public SetShapeFieldCommand(LayoutView view, string description, T oldValue, T newValue, Action<T> apply,
                                LayoutChangeInfo? change = null)
    {
        _view = view;
        _description = description;
        _oldValue = oldValue;
        _newValue = newValue;
        _apply = apply;
        _change = change;
    }

    public void Execute() { _apply(_newValue); _view.NotifyChanged(_change); }
    public void Undo()    { _apply(_oldValue); _view.NotifyChanged(_change); }
}
