namespace CircuitRF.Ui.Commands;

/// <summary>
/// One edit history as an undo target, for a surface that edits a session DIRECTLY rather than
/// through a document tab.
///
/// <para>The Analyses panel is the case this exists for. It is a tool, so it is never the document
/// dock's active dockable; it deliberately RETAINS its schematic when focus moves elsewhere (so its
/// analyses stay readable beside the Data Display a run just opened); and it edits the BASE session
/// even while that tab is pushed into a sub-cell, because a TestBench belongs to the top-level cell.
/// All three mean the stack its edits land on is routinely not the active document's — so the shell's
/// Undo, which follows the active document, had nothing to do with the edit the user just made.</para>
///
/// <para>Holds the stack rather than a view model: this is the whole of what the shell's Undo/Redo
/// need (see <see cref="IUndoableDocument"/>), and keeping it that narrow is what stops it becoming a
/// second way to reach an editor.</para>
/// </summary>
internal sealed class SessionUndoTarget(UndoRedoStack stack) : IUndoableDocument
{
    public UndoRedoStack UndoRedo => stack;
}
