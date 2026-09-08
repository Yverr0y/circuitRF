using System;
using System.IO;
using System.Linq;
using CircuitRF.Ui.Commands;
using CircuitRF.Ui.Schematic;
using CircuitRF.Ui.ViewModels;
using Xunit;

namespace CircuitRF.Ui.Tests;

/// <summary>
/// Owner report, 2026-09-07: add a cell to a workspace, edit its schematic, remove the cell with the
/// Project Tree's Remove Cell — then close the workspace, and it asks whether to save the schematic
/// of a cell that is already in the Trash.
///
/// <para>Removal force-closed the tabs and RETIRED their sessions, and
/// <see cref="SchematicSessionRegistry.RetireIfUnreferenced"/> refuses on purpose to drop a DIRTY
/// session — the right rule for a tab close, where the file still exists and reopening must bring the
/// edit back, and the wrong one for a removal, where the file is gone. What survived was an orphaned
/// dirty session, which is exactly what <c>HasAnyDirtyWork</c>/<c>PromptSaveBeforeClose</c> count.</para>
///
/// <para><c>WorkspaceViewModel</c> needs an Avalonia host and cannot be constructed here (the same
/// note <c>DirtySessionDiscardOnCloseTests</c> carries), so these drive the two halves the fix is made
/// of: the static subtree scope, and the registry decision it feeds.</para>
/// </summary>
public sealed class RemovedCellDirtySessionTests : IDisposable
{
    private sealed class TouchCommand : IUiCommand
    {
        public string Description => "edit";
        public void Execute() { }
        public void Undo() { }
    }

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "crf-remove-cell-" + Guid.NewGuid().ToString("N"));

    private string Cell(string name, string view)
    {
        var dir = Path.Combine(_root, name, "schematic");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, view);
        File.WriteAllText(file, "");
        return file;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static SchematicSessionRegistry WithDirtySessions(params string[] paths)
    {
        var registry = new SchematicSessionRegistry();
        foreach (var path in paths)
        {
            var vm = new SchematicViewModel(new SchematicEditModel());
            registry.Register(path, vm, _ => { });
            vm.Execute(new TouchCommand());
            Assert.True(registry.IsDirty(path), "fixture is not dirty, so it proves nothing");
        }
        return registry;
    }

    private static bool NothingReferencesIt(string _) => false;

    [Fact]
    public void RemovingACell_DiscardsTheDirtySessionsUnderIt_SoTheCloseNeverAsksAboutThem()
    {
        var doomed   = Cell("Amp", "amp.csch");
        var survivor = Cell("Keep", "keep.csch");
        var registry = WithDirtySessions(doomed, survivor);

        var cellDir  = Path.Combine(_root, "Amp");
        var captured = WorkspaceViewModel.SessionPathsUnder(registry.AllPaths, cellDir);
        foreach (var path in captured) registry.DiscardIfUnreferenced(path, NothingReferencesIt);

        Assert.False(registry.IsDirty(doomed));
        Assert.False(registry.TryGet(doomed, out _));

        // The control: a sibling cell's unsaved work is untouched, and the close prompt still has
        // that one to ask about. A fix that cleared the registry would pass the assertion above.
        Assert.True(registry.IsDirty(survivor));
        Assert.True(registry.HasOrphanedDirtySession(NothingReferencesIt));
    }

    [Fact]
    public void TheScope_IsTheWholeCellSubtree_AndNothingBesideIt()
    {
        var inside  = Cell("Amp", "amp.csch");
        var beside  = Cell("Amplifier2", "a2.csch");   // shares a name PREFIX, not a folder
        var outside = Cell("Other", "other.csch");
        var registry = WithDirtySessions(inside, beside, outside);

        var captured = WorkspaceViewModel.SessionPathsUnder(registry.AllPaths, Path.Combine(_root, "Amp"));

        Assert.Equal([inside], captured);
    }

    [Fact]
    public void RemovingASingleDocument_TakesThatFileOnly()
    {
        var removed = Cell("Amp", "amp.csch");
        var sibling = Path.Combine(Path.GetDirectoryName(removed)!, "amp-alt.csch");
        File.WriteAllText(sibling, "");
        var registry = WithDirtySessions(removed, sibling);

        // Remove File names a FILE, not a folder — IsPathOrUnder's exact-match arm.
        var captured = WorkspaceViewModel.SessionPathsUnder(registry.AllPaths, removed);

        Assert.Equal([removed], captured);
    }

    [Fact]
    public void ASessionATornOffWindowStillHolds_IsNotDiscarded()
    {
        // Unreferenced-guarded, exactly as the workspace-close discard is: a document open in its own
        // window is showing that session, and removing its file is not licence to tear it down.
        var doomed   = Cell("Amp", "amp.csch");
        var registry = WithDirtySessions(doomed);

        foreach (var path in WorkspaceViewModel.SessionPathsUnder(registry.AllPaths, Path.Combine(_root, "Amp")))
            registry.DiscardIfUnreferenced(path, p => p == doomed);

        Assert.True(registry.IsDirty(doomed));
        Assert.True(registry.TryGet(doomed, out _));
    }
}
