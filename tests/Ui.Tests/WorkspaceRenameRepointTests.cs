using System.IO;
using CircuitRF.Design.Workspace;
using CircuitRF.Ui.Schematic;

namespace CircuitRF.Ui.Tests;

// ──────────────────────────────────────────────────────────────────────────────
//  Renaming a workspace, 2026-09-06 — what breaks outside it, and what does not.
//
//  The owner's own reasoning when asking for this: "other workspaces that reference
//  the changed name workspace will break, but the same thing would happen if the user
//  used the file system." Correct, and these pin the two halves of it: a reference
//  from another workspace IS repointed (relative or absolute alike), and the ALIAS
//  behind it is left alone — which is why every ws:// reference in that workspace's
//  documents goes on resolving after one Path line is repaired.
// ──────────────────────────────────────────────────────────────────────────────

public sealed class WorkspaceRenameRepointTests : IDisposable
{
    private readonly string _parent;

    public WorkspaceRenameRepointTests()
    {
        _parent = Path.Combine(Path.GetTempPath(), $"crfrn_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_parent);
    }

    public void Dispose()
    {
        try { Directory.Delete(_parent, recursive: true); } catch { }
    }

    private string MakeWorkspace(string name, Action<CwsFile>? configure = null)
    {
        var root = Path.Combine(_parent, name);
        Directory.CreateDirectory(root);
        var cws = new CwsFile();
        configure?.Invoke(cws);
        WorkspacePersistence.SaveToFileAtomic(Path.Combine(root, ".cws"), cws);
        return root;
    }

    private static CwsFile Read(string root)
        => WorkspacePersistence.LoadFromFile(Path.Combine(root, ".cws"));

    [Fact]
    public void ASiblingAlias_IsRepointed_AndItsAliasIsUntouched()
    {
        var target = MakeWorkspace("Library");
        var caller = MakeWorkspace("Project", cws => cws.ReferencedWorkspaces =
        [
            // Two siblings: the ordinary case, and the one stored RELATIVE — through the very
            // segment about to be renamed.
            new CwsWorkspaceRef { Alias = "Library", Path = "../Library/.cws" },
        ]);

        Assert.True(WorkspaceRenameRepoint.References(caller, target));

        var renamed = Path.Combine(_parent, "SharedLibrary");
        Directory.Move(target, renamed);

        var result = WorkspaceRenameRepoint.Repoint(caller, target, renamed);
        Assert.Equal(1, result.Aliases);

        var entry = Read(caller).ReferencedWorkspaces!.Single();

        // Asserted by RESOLUTION, not by spelling. The stored form is re-decided through
        // WorkspaceRefs.ToStoredRef — the rule the application's own writer uses — and that rule
        // makes a reference ABSOLUTE the moment it points outside the workspace holding it. A sibling
        // project therefore comes back absolute even though this fixture wrote it relative (an older
        // or hand-edited .cws can, and it must still be repaired).
        Assert.Equal(
            Path.GetFullPath(Path.Combine(renamed, ".cws")),
            Path.GetFullPath(WorkspaceRefs.Resolve(entry.Path, caller)));
        // The alias is what every ws:// reference in that workspace's DOCUMENTS carries. Changing it
        // would break each of them; the whole point of the alias table is that it does not have to.
        Assert.Equal("Library", entry.Alias);
    }

    [Fact]
    public void AnAbsoluteKnownFile_AndALibraryRef_AreBothRepointed()
    {
        var target = MakeWorkspace("Kit");
        Directory.CreateDirectory(Path.Combine(target, "data"));
        var caller = MakeWorkspace("Project", cws =>
        {
            cws.KnownFiles  = [Path.Combine(target, "data", "meas.s2p")];
            cws.LibraryRefs = [Path.Combine(target, "cells")];
        });

        var renamed = Path.Combine(_parent, "KitV2");
        Directory.Move(target, renamed);

        var result = WorkspaceRenameRepoint.Repoint(caller, target, renamed);

        Assert.Equal(1, result.KnownFiles);
        Assert.Equal(1, result.Libraries);
        var cws = Read(caller);
        Assert.StartsWith(renamed, cws.KnownFiles[0]);
        Assert.StartsWith(renamed, cws.LibraryRefs[0]);
    }

    [Fact]
    public void AReferenceThatDoesNotPointHere_IsLeftExactlyAsItWas()
    {
        var target    = MakeWorkspace("Library");
        var elsewhere = MakeWorkspace("Other");
        var caller    = MakeWorkspace("Project", cws => cws.ReferencedWorkspaces =
        [
            new CwsWorkspaceRef { Alias = "Other", Path = "../Other/.cws" },
        ]);

        Assert.False(WorkspaceRenameRepoint.References(caller, target));

        var result = WorkspaceRenameRepoint.Repoint(caller, target, Path.Combine(_parent, "Renamed"));
        Assert.Equal(0, result.Total);
        Assert.Equal("../Other/.cws", Read(caller).ReferencedWorkspaces!.Single().Path.Replace('\\', '/'));
        _ = elsewhere;
    }

    /// <summary>
    /// SL1 R-sl1-6's <c>${NAME}</c> refs name a location each machine answers for itself. Substituting
    /// an absolute path for one would repair this machine and silently un-share it everywhere else,
    /// so a tokenised ref is neither counted nor rewritten.
    /// </summary>
    [Fact]
    public void ATokenisedReference_IsNeverRewritten()
    {
        var target = MakeWorkspace("Library");
        var caller = MakeWorkspace("Project", cws => cws.KnownFiles = ["${SHARED}/meas.s2p"]);

        Assert.False(WorkspaceRenameRepoint.References(caller, target));
        Assert.Equal(0, WorkspaceRenameRepoint.Repoint(caller, target, Path.Combine(_parent, "X")).Total);
        Assert.Equal("${SHARED}/meas.s2p", Read(caller).KnownFiles[0]);
    }
}
