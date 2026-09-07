using Avalonia.Controls;
using Avalonia.Interactivity;
using CircuitRF.Ui.ViewModels.Dock;

namespace CircuitRF.Ui.Views.Revision;

/// <summary>
/// The restore-point panel's view (RC-5 R-rc5-4c).
///
/// <para>Three buttons and a list. <b>No git vocabulary appears anywhere in it</b> — not in a label,
/// not in a tooltip, not in the sentence at the foot — which is R-rc0-6 applied to the one surface a
/// designer actually meets this feature through.</para>
/// </summary>
public partial class RestorePointsToolView : UserControl
{
    public RestorePointsToolView() => InitializeComponent();

    private void OnSavePointClick(object? sender, RoutedEventArgs e)
        => (DataContext as RestorePointsTool)?.SavePoint();

    private void OnRestoreClick(object? sender, RoutedEventArgs e)
        => (DataContext as RestorePointsTool)?.Restore();

    private void OnKeepClick(object? sender, RoutedEventArgs e)
        => (DataContext as RestorePointsTool)?.Keep();

    private void OnBringBackClick(object? sender, RoutedEventArgs e)
        => (DataContext as RestorePointsTool)?.BringBack();
}
