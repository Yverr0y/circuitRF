using CircuitRF.Ui.Schematic;

namespace CircuitRF.Ui.Commands.Schematic;

/// <summary>
/// Removes the stretch of one wire between two points on a single segment of it, leaving the
/// wire's two remaining pieces exactly where they were. This is the cut a user performs by hand
/// after dropping an IProbe onto a wire — see <see cref="SeriesProbeInsertion"/> for when it is
/// allowed to happen for them.
///
/// <para>Unlike <see cref="DeleteSegmentsCommand"/> the cut is PARTIAL: it takes a sub-run of one
/// segment rather than whole segments, because the two probe pins land wherever the user dropped
/// them and need not coincide with the segment's own vertices.</para>
///
/// <para>Either piece that collapses to fewer than two distinct points is simply not created — a
/// probe pin landing on the wire's own end leaves nothing on that side, which is correct: whatever
/// held that end (a component pin) now meets the probe pin at the same point instead. Undo restores
/// the original wire object, so anything keyed on its identity comes back with it.</para>
/// </summary>
internal sealed class CutWireSpanCommand : IUiCommand
{
    private readonly SchematicEditModel _model;
    private readonly EditableWire       _original;
    private readonly int                _originalIndex;
    private readonly List<EditableWire> _replacements = [];

    public string Description => "Clear Wire Under Probe";

    public CutWireSpanCommand(SchematicEditModel model, SeriesProbeInsertion.ShortedSpan span)
    {
        _model         = model;
        _original      = span.Wire;
        _originalIndex = model.Wires.IndexOf(span.Wire);

        var pts = span.Wire.Points;

        // Before the cut: everything up to the segment's start vertex, then the near cut point.
        var head = new List<(double X, double Y)>();
        for (int i = 0; i <= span.SegmentIndex && i < pts.Count; i++) head.Add(pts[i]);
        head.Add(span.First);

        // After the cut: the far cut point, then everything from the segment's end vertex on.
        var tail = new List<(double X, double Y)> { span.Second };
        for (int i = span.SegmentIndex + 1; i < pts.Count; i++) tail.Add(pts[i]);

        AddPiece(head);
        AddPiece(tail);
    }

    private void AddPiece(List<(double X, double Y)> piece)
    {
        var norm = WireGeometry.NormalizePoints(piece);
        if (norm.Count < 2) return;
        var w = new EditableWire();
        w.Points.AddRange(norm);
        _replacements.Add(w);
    }

    public void Execute()
    {
        int insertAt = Math.Min(Math.Max(_originalIndex, 0), _model.Wires.Count);
        _model.Wires.Remove(_original);
        for (int i = 0; i < _replacements.Count; i++)
            _model.Wires.Insert(Math.Min(insertAt + i, _model.Wires.Count), _replacements[i]);
        _model.NotifyChanged();
    }

    public void Undo()
    {
        foreach (var r in _replacements) _model.Wires.Remove(r);
        _model.Wires.Insert(Math.Min(Math.Max(_originalIndex, 0), _model.Wires.Count), _original);
        _model.NotifyChanged();
    }
}
