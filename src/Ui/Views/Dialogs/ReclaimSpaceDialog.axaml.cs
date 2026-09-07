using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CircuitRF.Ui.Views.Dialogs;

/// <summary>
/// Asks before reclaiming, and says what reclaiming destroys (<c>docs/design/revision-control.md</c>
/// §5.6a; RC-4 R-rc4-10, gate 9a).
///
/// <para>Returns true only when the user pressed the affirmative button. Cancelling — or closing the
/// window — reclaims nothing, because the caller is what invokes the operation and it does so only on
/// true.</para>
/// </summary>
public partial class ReclaimSpaceDialog : Window
{
    // The XAML loader needs a parameterless constructor (AVLN3001).
    public ReclaimSpaceDialog() : this(0, 0) { }

    /// <param name="count">How many thinned states this would destroy.</param>
    /// <param name="days">The age that selected them — thinned more than this many days ago.</param>
    public ReclaimSpaceDialog(int count, int days)
    {
        InitializeComponent();

        string states = count == 1 ? "1 restore point" : $"{count} restore points";
        string ago    = days  == 1 ? "1 day"           : $"{days} days";

        HeadlineLabel.Text = $"Reclaim the space held by {states}?";

        // Names the count AND the age, because either alone leaves the user unable to tell whether the
        // set is the one they meant.
        DestroysLabel.Text =
            $"These are {states} that retention already thinned, more than {ago} ago — they are no "
          + "longer offered, and their contents are still on disk only because circuitRF never "
          + "discards anything on its own. Reclaiming frees that space permanently. After it, nobody "
          + "can bring those states back.";

        SurvivesLabel.Text =
            "Every restore point still listed, and every change you recorded yourself, survives this "
          + "unchanged. Your design files are not touched.";

        ReclaimButton.Content = "Reclaim Space";
    }

    private void OnReclaimClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
