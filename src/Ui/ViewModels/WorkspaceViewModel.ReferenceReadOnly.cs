using Dock.Model.Core;
using CircuitRF.Ui.Commands;
using CircuitRF.Ui.Messages;

namespace CircuitRF.Ui.ViewModels;

/// <summary>
/// RC-2 (brief-revision-control-2-read-only-references.md, <c>docs/design/revision-control.md</c>
/// §7A.2/§7A.3): a cell reached through a <c>ws://</c> workspace reference is not editable through
/// the window that merely references it.
///
/// <para><b>What this prevents is silent, and none of it needs revision control.</b> A designer
/// opens a library cell through a reference, fixes a wrong pad size and saves. The file is written —
/// filesystem-is-truth, circuitRF does not intercept ordinary saves — and the fix now works for that
/// designer and for nobody else. Every other designer goes on simulating the old cell, and the
/// librarian's next publish overwrites the edit or conflicts with it, with nothing to merge it back
/// from. Read-only turns that five-step chain into ONE refusal at the moment of editing, which is the
/// only point where the designer still has the context to do something sensible.</para>
///
/// <para><b>The refusal is not a dead end and that is the point (R-rc2-6/-7).</b> The library IS a
/// workspace: opening it as one is the supported way to edit it, with its own history, and the
/// refusal carries that as an invokable action rather than as advice. Once the owning workspace is
/// open the cell becomes editable THERE and not here — <c>ActivateIfOpenInAnotherWindow</c>'s own
/// invariant, <b>one file, one editor, across every open window</b>. The other route is not merely
/// different but harmful: two editors over one file means two undo stacks and two dirty flags, and
/// the second save silently discards the first — the same designer losing their own work minutes
/// apart in one session, which is worse than the divergence this exists to prevent.</para>
///
/// <para><b>This is a policy, not a permission system</b> (§7A.5). Someone determined to edit the
/// library's files outside circuitRF can; the goal is to make the ACCIDENTAL case impossible and the
/// DELIBERATE case visible, which is the whole of what is achievable without a server. It reuses
/// SL2's read-only behaviour rather than building a second one — see
/// <c>WorkspaceViewModel.ReadOnly.cs</c>, whose Save-disabled/Save-As path takes this policy
/// unchanged.</para>
/// </summary>
public partial class WorkspaceViewModel
{
    /// <summary>
    /// The access this window has to <paramref name="path"/> through its own workspace's references.
    /// A path in this workspace, in a library, or outside every reference answers
    /// <see cref="ReferenceAccess.NotReferenced"/>.
    /// </summary>
    internal ReferenceAccess ReferenceAccessFor(string? path, out string? ownerRoot, out string? alias)
        => ReferencedWorkspacePolicy.AccessFor(CurrentWorkspaceRoot, path, out ownerRoot, out alias);

    /// <summary>The workspace that owns <paramref name="path"/> when this window reaches it through a
    /// READ-ONLY reference; null otherwise.</summary>
    internal string? ReadOnlyReferenceOwnerOf(string? path)
        => ReferencedWorkspacePolicy.ReadOnlyOwnerRootFor(CurrentWorkspaceRoot, path);

    /// <summary>R-rc2-5: true when <paramref name="path"/> is reached through a reference somebody
    /// explicitly made editable — the state that is marked wherever it appears.</summary>
    internal bool IsThroughEditableReference(string? path)
        => ReferenceAccessFor(path, out _, out _) == ReferenceAccess.Editable;

    // ── The guard: refuse at the EDIT, never at the write ─────────────────────

    /// <summary>
    /// A refusal, as data: what the Messages panel says, and — when there is one — the button on the
    /// end of it and what invoking it does.
    ///
    /// <para><b>Separate from posting it</b> so the decision can be asserted without a message sink.
    /// <c>MessagesTool</c> marshals through <c>Dispatcher.UIThread</c>, which is a direct call in an
    /// isolated run and a queued one nobody pumps once another test in the process has bound the
    /// dispatcher to its own thread — so a gate written against the posted list would pass alone and
    /// fail under full-suite load. That trap is already recorded in <c>ReadOnlyWorkspaceTests</c>.</para>
    ///
    /// <para><see cref="ActionLabel"/> and <see cref="Invoke"/> are null exactly when the edit was
    /// ROUTED rather than refused — the owning workspace was already open, its window is now in
    /// front, and there is nothing left to offer.</para>
    /// </summary>
    internal sealed record ReferenceEditRefusal(string Text, string? ActionLabel, Func<Task>? Invoke);

    /// <summary>
    /// Where an edit goes when the workspace that owns the file is already open: the window to bring
    /// forward, and the view model to ask for the document.
    ///
    /// <para>A record of two callbacks rather than a <c>Window</c> so the routing decision is
    /// testable — a headless test can observe which workspace was asked for which file without an
    /// operating-system window existing.</para>
    /// </summary>
    internal sealed record EditRouteTarget(Action Activate, Action<string> OpenDocument);

    /// <summary>
    /// How <see cref="RouteEditToOwningWindow"/> finds the window that owns a workspace root. Null —
    /// the default — scans the open workspace windows.
    /// </summary>
    internal Func<string, EditRouteTarget?>? OwningWindowLookup { get; set; }

    /// <summary>
    /// How the refusal's action opens a workspace that is NOT already open, landing on the document.
    /// Null — the default — is <c>App.OpenWorkspaceInNewWindow</c>. Arguments are the owning
    /// workspace's <c>.cws</c> and the document to open in it.
    /// </summary>
    internal Action<string, string>? OpenWorkspaceForEditHook { get; set; }

    /// <summary>
    /// R-rc2-6/-7 and the brief's gate 2. Answers <see cref="UndoRedoStack.EditGuard"/>: false stops
    /// the edit before the command runs, and takes the designer to the workspace that owns the file.
    /// </summary>
    internal bool AllowEditOfDocument(IDockable? dockable)
    {
        if (RefusalForEditOf(dockable) is not { } refusal) return true;

        if (refusal is { ActionLabel: { } label, Invoke: { } invoke })
            // §2.9's message-with-an-action. The text still reads correctly with no button on the end
            // (IMessageSink.PostAction's default drops it), because a sink behind a headless run has
            // no way to offer one — so the remedy is stated in the sentence and the button is a
            // shortcut for exactly what the sentence says.
            Messages.PostAction(MessageLevel.Warning, refusal.Text, label, invoke);
        else
            // Routed, not refused: nothing went wrong and nothing needs deciding, so Info rather than
            // Warning — §5A.4's rule that an unusual-but-fine state never takes an error colour.
            Messages.Info(refusal.Text);

        return false;
    }

    /// <summary>
    /// The decision behind <see cref="AllowEditOfDocument"/> — null when the edit may proceed.
    ///
    /// <para><b>Closing this window's view of the document is part of routing, not tidying.</b> The
    /// invariant is one file, one editor: leaving the tab here would mean two views of one file, and
    /// the one in this window would go stale the moment the owner saved — showing content that is no
    /// longer on disk with nothing saying so. It can never cost anything, because every edit through
    /// it was refused by this same guard, so the document cannot be dirty.</para>
    ///
    /// <para>The tab is closed only when the edit is actually ROUTED. A refusal the designer has not
    /// acted on yet leaves them looking at the cell they were reading.</para>
    /// </summary>
    internal ReferenceEditRefusal? RefusalForEditOf(IDockable? dockable)
    {
        string? path = DocumentFilePath(dockable);
        if (ReferenceAccessFor(path, out string? ownerRoot, out string? alias) != ReferenceAccess.ReadOnly)
            return null;

        string name      = Path.GetFileNameWithoutExtension(path!);
        string ownerName = Path.GetFileName(ownerRoot!);

        // Both workspaces already open: the edit is not refused, it is MOVED. From the designer's
        // point of view they asked to edit the cell and are now editing the cell; what they get in
        // addition is the true answer to where the cell actually lives.
        if (RouteEditToOwningWindow(path!, ownerRoot!, dockable))
            return new ReferenceEditRefusal(
                $"'{name}' belongs to workspace '{ownerName}', which is referenced here as " +
                $"\"{alias}\" — it is edited there, not through this workspace.",
                null, null);

        string ownerCws = Path.Combine(ownerRoot!, ".cws");
        return new ReferenceEditRefusal(
            $"'{name}' belongs to workspace '{ownerName}', which this workspace only references, so " +
            $"it is read-only here. Open '{ownerName}' as a workspace to edit it there, with its own " +
            $"history. (To edit it through this reference instead, right-click \"{alias}\" in the " +
            "Project panel and allow editing.)",
            $"Open '{ownerName}'",
            () =>
            {
                OpenOwningWorkspaceForEdit(path!, ownerCws, dockable);
                return Task.CompletedTask;
            });
    }

    /// <summary>
    /// R-rc2-7's routing, when the owner is already open: bring that window forward and put the
    /// document in front of the designer there. Returns false when no open window holds the owning
    /// workspace, which is what makes the refusal's action worth offering.
    /// </summary>
    private bool RouteEditToOwningWindow(string documentPath, string ownerRoot, IDockable? here)
    {
        if ((OwningWindowLookup ?? FindOwningWindow)(ownerRoot) is not { } target) return false;

        // Ours goes FIRST. OpenDocumentByPath ends at ActivateIfOpenInAnotherWindow, which would
        // otherwise find this window's own read-only view and route the request straight back
        // here — the owner would raise our tab instead of opening its own.
        ReleaseReadOnlyView(here);

        target.Activate();
        target.OpenDocument(documentPath);
        return true;
    }

    private EditRouteTarget? FindOwningWindow(string ownerRoot)
    {
        string wantedRoot = WorkspaceRootFinder.Normalize(ownerRoot);

        foreach (var window in Views.WorkspaceLocator.AllWindows())
        {
            if (window.DataContext is not WorkspaceViewModel other || ReferenceEquals(other, this)) continue;
            if (other.CurrentWorkspaceRoot is not { } theirRoot) continue;
            if (!string.Equals(WorkspaceRootFinder.Normalize(theirRoot), wantedRoot,
                               StringComparison.OrdinalIgnoreCase)) continue;

            return new EditRouteTarget(window.Activate, p => other.OpenDocumentByPath(p));
        }
        return null;
    }

    /// <summary>
    /// The refusal's action (R-rc2-6): open the owning workspace in a window of its own and land on
    /// the cell there. One click, rather than a File ▸ Open and a hunt for the folder — which is the
    /// difference between correct advice and a supported path.
    /// </summary>
    private void OpenOwningWorkspaceForEdit(string documentPath, string ownerCwsPath, IDockable? here)
    {
        if (!File.Exists(ownerCwsPath))
        {
            Messages.Error(
                $"Workspace '{Path.GetFileName(Path.GetDirectoryName(ownerCwsPath))}' was not found.");
            return;
        }

        ReleaseReadOnlyView(here);

        if (OpenWorkspaceForEditHook is { } hook) hook(ownerCwsPath, documentPath);
        else App.OpenWorkspaceInNewWindow(ownerCwsPath, documentPath);
    }

    /// <summary>
    /// Closes this window's read-only view of a document whose editing has just been handed to the
    /// workspace that owns it. Safe unconditionally: the guard refused every edit through it, so
    /// there is nothing unsaved to lose.
    /// </summary>
    private void ReleaseReadOnlyView(IDockable? dockable)
    {
        if (dockable is null) return;
        try { _factory.ForceCloseDockable(dockable); }
        catch (Exception ex) { Messages.Warning($"Could not close the read-only view: {ex.Message}"); }
    }

    // ── Installing the guard, and the editable-reference mark ────────────────

    /// <summary>
    /// Puts <see cref="AllowEditOfDocument"/> on every open document's edit history, and marks the
    /// documents reached through an EDITABLE reference (R-rc2-5).
    ///
    /// <para><b>A sweep rather than a line at each of the twenty-seven places a document is
    /// registered.</b> A rule enforced by every opener remembering it is a rule that is true in
    /// twenty-six of them and found by a user in the twenty-seventh — the same reasoning SL2 used to
    /// put the <c>.cws</c> write rule in one choke point. It rides
    /// <see cref="RefreshReadOnlyMenuState"/>, which already runs on both activation fan-outs, so a
    /// document that has just been opened is swept before it can be typed into.</para>
    ///
    /// <para>The guard is installed once per history and never removed: it re-reads the document's
    /// own path and the reference table on every edit, so a Save As, a re-pointed reference or an
    /// editability change is picked up without re-wiring anything.</para>
    /// </summary>
    private void ApplyReferencePolicyToOpenDocuments()
    {
        // Over a SNAPSHOT: the fan-out this rides can be re-entered from inside a close, and
        // ForceCloseDockable removes the entry it is closing from this very dictionary.
        foreach (var dockable in _openDocsByPath.Values.ToList())
        {
            if (dockable is IUndoableDocument { UndoRedo: { } stack })
                stack.EditGuard ??= () => AllowEditOfDocument(dockable);

            if (dockable is IReferenceMarkedDocument marked)
                marked.IsEditableReference = IsThroughEditableReference(DocumentFilePath(dockable));
        }
    }
}
