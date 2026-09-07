namespace CircuitRF.Ui.Commands;

/// <summary>
/// RC-2 R-rc2-5: a document that can carry the <b>editable reference</b> mark in its tab.
///
/// <para>An editably-referenced workspace is one somebody explicitly opted out of §7A.2's default
/// for, and two designers doing that in one library is exactly the concurrent-edit problem §6.2
/// describes — which circuitRF is not solving. So the state has to be VISIBLE rather than inferable:
/// the reference is marked in the Project panel, and a document opened through it is marked in its
/// tab.</para>
///
/// <para><b>Why an interface rather than a property on a base class.</b> The document types are Dock
/// <c>Document</c> subclasses with no circuitRF base of their own, and the tab header binds by
/// reflection over the runtime type — a default interface member would not be found. Each
/// implementer therefore declares a real property; this states the contract once so
/// <c>WorkspaceViewModel</c>'s sweep sets it in ONE place rather than through a switch that silently
/// misses the next document type added.</para>
///
/// <para>Only the document kinds a referenced workspace's Project-panel sub-tree can actually open —
/// a cell's schematic, symbol, layout and parameters — implement it. Every other type binds null in
/// the tab template and renders unmarked, exactly as they already do for
/// <c>LayoutDocument.IsForeign</c>.</para>
/// </summary>
public interface IReferenceMarkedDocument
{
    /// <summary>True when this document's file lives in a workspace the open workspace references
    /// EDITABLY. Set by <c>WorkspaceViewModel</c>; never computed by the document itself, because the
    /// answer depends on which workspace is asking.</summary>
    bool IsEditableReference { get; set; }
}
