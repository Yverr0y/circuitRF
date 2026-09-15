using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using CircuitRF.Design.Workspace;
using CircuitRF.Ui.Schematic;

namespace CircuitRF.Ui.ViewModels;

/// <summary>
/// <b>Tools ▸ Examples</b> — pick a folder, get a copy of a shipped example workspace, and it opens.
///
/// <para>Kept in its own file for the same reason the revision and sharing partials are: none of it
/// is about editing a design, and the two things that decide anything —
/// <see cref="ExampleWorkspaces"/> (what there is) and <see cref="ExampleWorkspaceInstall"/> (the
/// copy) — both live outside this view model. What is left here is a folder picker, a refusal and a
/// report.</para>
///
/// <para><b>It opens in a NEW WINDOW.</b> Somebody reaching for an example usually has a design
/// open and is looking something up; replacing their workspace to show them an example is the
/// opposite of helping, and it would drag the dirty-work prompt in with it.</para>
/// </summary>
public partial class WorkspaceViewModel
{
    /// <summary>
    /// The examples this build ships, in index order — what the Tools ▸ Examples submenu is built
    /// from. Empty is a supported state: the menu hides rather than showing rows that cannot work.
    /// </summary>
    public static IReadOnlyList<ExampleWorkspace> Examples { get; } = ExampleWorkspaces.All();

    /// <summary>Whether there is an Examples submenu to show at all.</summary>
    public static bool HasExamples => Examples.Count > 0;

    /// <summary>
    /// The in-window Tools ▸ Examples rows. Built once on first use, because the shipped set cannot
    /// change while the application is running — unlike Open Recent, which is rebuilt on every push.
    ///
    /// <para>Each row's <c>Command</c> is ASSIGNED, not bound, which is what
    /// <c>RebuildRecentMenuItems</c> already does for the same reason: a <c>MenuItem</c> handed to a
    /// parent through <c>ItemsSource</c> is its own container, and a binding written in code would
    /// resolve against whatever DataContext it inherits rather than against this view model.</para>
    /// </summary>
    /// <remarks>The macOS menu bar is populated from <see cref="Examples"/> directly by
    /// <c>WorkspaceWindow.axaml.cs</c>: a <c>NativeMenuItem</c> is not a <c>Control</c>, so the two
    /// surfaces cannot share one collection — but they do share the one enumeration above.</remarks>
    public ObservableCollection<Control> ExampleMenuItems => _exampleMenuItems ??= BuildExampleMenuItems();

    private ObservableCollection<Control>? _exampleMenuItems;

    private ObservableCollection<Control> BuildExampleMenuItems()
    {
        var items = new ObservableCollection<Control>();
        foreach (var e in Examples)
        {
            var item = new MenuItem
            {
                Header           = e.Title,
                Command          = OpenExampleCommand,
                CommandParameter = e.Folder,
            };
            ToolTip.SetTip(item, e.Summary);
            items.Add(item);
        }
        return items;
    }

    /// <summary>
    /// Installs the example whose <see cref="ExampleWorkspace.Folder"/> is
    /// <paramref name="folder"/> — the menu passes the folder name rather than the record, because a
    /// <c>NativeMenuItem</c>'s CommandParameter has to survive being written in XAML.
    /// </summary>
    [RelayCommand]
    private async Task OpenExample(string? folder)
    {
        var window = ResolveOwner(null);
        if (window is null) return;

        ExampleWorkspace? example = null;
        foreach (var e in Examples)
            if (string.Equals(e.Folder, folder, StringComparison.OrdinalIgnoreCase)) { example = e; break; }

        if (example is null)
        {
            Messages.Error($"There is no example called '{folder}' in this build.");
            return;
        }

        // Where the picker opens: beside the workspace that is already open, which is where a
        // sibling project belongs — the same answer the Clone Workspace dialog's Browse gives.
        IStorageFolder? start = null;
        try { start = await window.StorageProvider.TryGetFolderFromPathAsync(_lastWorkspaceParentDir); }
        catch { /* the picker opens wherever the platform likes */ }

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title                  = $"Put the '{example.Title}' example in…",
            AllowMultiple          = false,
            SuggestedStartLocation = start,
        });
        if (folders.Count == 0) return;

        string parentDir = folders[0].Path.LocalPath;

        // Asked before anything is written. A copy has no rollback, so a refusal that arrives
        // halfway through leaves a workspace nobody asked for.
        if (ExampleWorkspaceInstall.Refusal(example, parentDir) is { } refusal)
        {
            Messages.Error(refusal);
            return;
        }

        ExampleInstallResult result;
        try
        {
            // Off the UI thread: this walks and copies a directory tree, and WorkspaceCopy is
            // framework-free by design so that it can be.
            result = await Task.Run(() => ExampleWorkspaceInstall.Run(example, parentDir));
        }
        catch (Exception ex)
        {
            Messages.Error($"The '{example.Title}' example could not be copied: {ex.Message}");
            return;
        }

        // Reported, not swallowed: the workspace opens either way, and a file that did not arrive
        // is something the user finds out about now rather than when a cell fails to resolve.
        foreach (string failure in result.Failures)
            Messages.Warning($"Example '{example.Title}': {failure}");

        _lastWorkspaceParentDir = parentDir;
        Messages.Info($"Copied the '{example.Title}' example to '{result.WorkspaceDir}' "
                    + $"({result.FileCount} file(s)). It is yours to edit.");

        App.OpenWorkspaceInNewWindow(result.CwsPath);
    }
}
