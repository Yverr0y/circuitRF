using Avalonia.Controls;
using Avalonia.Interactivity;
using CircuitRF.Ui.ViewModels.Dock;

namespace CircuitRF.Ui.Views.Revision;

/// <summary>
/// The version-history panel's view (RC-7 R-rc7-9, R-rc7-10, R-rc7-11).
///
/// <para><b>No git vocabulary appears in it</b> — not in a label, not in a tooltip, not in the
/// sentence at the foot (R-rc0-6, R-rc7-3). A designer is shown their own design at an earlier moment,
/// never a graph, a branch name or a merge. The one identifier in the whole feature is
/// <see cref="VersionRow.Identity"/>, and R-rc7-4 is why it is allowed: an explicit action produced
/// it.</para>
///
/// <para><b>A separate panel from the restore points, deliberately</b> (R-rc7-9). They sit side by
/// side and never interleave.</para>
/// </summary>
public partial class VersionHistoryToolView : UserControl
{
    public VersionHistoryToolView() => InitializeComponent();

    private void OnKeepVersionClick(object? sender, RoutedEventArgs e)
        => (DataContext as VersionHistoryTool)?.KeepVersion();

    private void OnGoBackClick(object? sender, RoutedEventArgs e)
        => (DataContext as VersionHistoryTool)?.GoBack();
}
