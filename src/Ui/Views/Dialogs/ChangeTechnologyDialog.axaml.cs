using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CircuitRF.Ui.Layout;

namespace CircuitRF.Ui.Views.Dialogs;

/// <summary>Result of the Change Technology picker (docs/sonnet-briefs/brief-L1g-technology-retarget.md
/// §4). <see cref="AbsoluteTechPath"/> null means "(Workspace default)" — writes
/// <c>LayoutView.TechRef = null</c> and re-resolves through L0c's resolution order; a non-null path
/// (a <c>tech/</c> entry or a browsed file) becomes an explicit <c>TechRef</c>.</summary>
public sealed record ChangeTechnologyResult(string? AbsoluteTechPath, bool AdoptUnits);

/// <summary>
/// "Change Technology…" picker — the entry point for Gap 1 (docs/sonnet-briefs/brief-L1g-technology-retarget.md
/// §0: "there is no UI to change a layout's technology"). Offers <b>(Workspace default)</b> as an
/// explicit, always-selectable option (§4 — L0c's convention, not something only reachable by never
/// having chosen), every <c>.ctech</c> <b>anywhere in the workspace</b>, and <b>Browse…</b> for one
/// outside it. The unit-adoption checkbox defaults OFF — silently overwriting a user's working unit
/// mid-retarget would be exactly the kind of helpfulness that erodes trust (§4 point 3).
///
/// <para><b>The list was the workspace's <c>tech/</c> folder alone</b> until 2026-09-04 (owner report:
/// the picker does not offer all the workspace's technologies). <c>tech/</c> is where the workspace
/// TEMPLATE puts one and where a new one is written, but nothing stops a technology living beside the
/// cell it belongs to, arriving inside an imported cell folder, or being unpacked from an archive —
/// and a technology the user can see in the Project Tree but not choose here reads as the picker being
/// broken. Browse… could reach them, which is precisely the tell: the file was in the workspace all
/// along and the dialog was sending the user out to the filesystem to find it.</para>
/// </summary>
public partial class ChangeTechnologyDialog : Window
{
    public ChangeTechnologyDialog() => InitializeComponent();

    public ChangeTechnologyDialog(LayoutEditorViewModel vm) : this()
    {
        CurrentText.Text = $"Current: {vm.TechSummaryText}";

        var items = new List<ListBoxItem>
        {
            Row("(Workspace default)", null,
                "Whatever this workspace's default technology resolves to — no explicit reference is "
                + "written into the layout."),
        };

        foreach (var choice in WorkspaceTechnologyChoices.Enumerate(vm.WorkspaceRootDir, vm.WorkspaceTechDir))
            items.Add(Row(choice.Label, choice.AbsolutePath, choice.AbsolutePath));

        ChoiceList.ItemsSource = items;
        ChoiceList.SelectedIndex = 0;

        BrowseButton.Click += async (_, _) => await OnBrowseAsync();
        OkButton.Click      += (_, _) => Close(BuildResult());
        CancelButton.Click  += (_, _) => Close(null);
    }

    private async Task OnBrowseAsync()
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a Technology",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("circuitRF Technology") { Patterns = ["*.ctech"] },
                new FilePickerFileType("All Files")             { Patterns = ["*.*"] },
            ],
        });
        if (result.Count == 0) return;

        Close(new ChangeTechnologyResult(result[0].Path.LocalPath, AdoptUnitsCheck.IsChecked == true));
    }

    /// <summary>
    /// One row: its label, the file it stands for (in <c>Tag</c>), and <b>its full path as a
    /// tooltip</b>.
    ///
    /// <para>The tooltip is owner-requested (2026-09-09) and it answers the question no label can. A
    /// technology's row is named after the technology's own internal <c>Name</c>, which two files can
    /// share — a Save As from the technology editor produces exactly that — and while
    /// <see cref="WorkspaceTechnologyChoices"/> appends the workspace-relative path to a duplicated
    /// name, "which file is this actually" is a question a user can have about ANY row, including the
    /// unique ones. Hovering answers it without lengthening every label.</para>
    ///
    /// <para>Built as <c>ListBoxItem</c>s rather than data items with a template because a tooltip is
    /// per-container: an <c>ItemTemplate</c> would have to bind <c>ToolTip.Tip</c> through the item's
    /// own type, and this dialog carries no data context to bind against.</para>
    /// </summary>
    private static ListBoxItem Row(string label, string? absolutePath, string? tooltip)
    {
        var row = new ListBoxItem { Content = label, Tag = absolutePath };
        if (tooltip is { Length: > 0 }) ToolTip.SetTip(row, tooltip);
        return row;
    }

    private ChangeTechnologyResult BuildResult() =>
        new((ChoiceList.SelectedItem as ListBoxItem)?.Tag as string, AdoptUnitsCheck.IsChecked == true);
}
