using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CircuitRF.Design.Revision;

namespace CircuitRF.Ui.Views.Dialogs;

/// <summary>
/// <b>§12 Q4's workspace-root row, put as a question</b> (<c>docs/design/revision-control.md</c>
/// §12 Q4 <i>Refinement 3</i>; RC-6 R-rc6-7a).
///
/// <para>A repository the <b>user</b> created at the workspace root: their intent is unambiguous and
/// the root is exactly right, so taking it over unasked is presumptuous and refusing to offer is
/// unhelpful. <b>Three answers, not two</b> — and the third, the hold state, is rev 3's behaviour and
/// stays available.</para>
///
/// <para><b>The dialog is asked ONCE.</b> Whichever answer is given, the caller writes RC-3's
/// management marker into the repository's config, which is what stops the question arriving on every
/// open. Cancelling is not a fourth answer: nothing is recorded and the question is put again next
/// time, which is right — an unanswered question is not an answer, and the alternative is a workspace
/// held forever because somebody pressed Escape.</para>
///
/// <para><b>The costs are read from <see cref="RepositoryAdoption.KeepingCosts"/></b> rather than
/// transcribed into the XAML, so a configuration row added below the firewall cannot go unmentioned
/// here.</para>
/// </summary>
public partial class AdoptExistingHistoryDialog : Window
{
    // The XAML loader needs a parameterless constructor (AVLN3001).
    public AdoptExistingHistoryDialog() : this("") { }

    /// <param name="workspaceName">The workspace's folder name. Named, because "this workspace" is not
    /// enough when two windows are open.</param>
    public AdoptExistingHistoryDialog(string workspaceName)
    {
        InitializeComponent();

        HeadlineLabel.Text = workspaceName.Length > 0
            ? $"“{workspaceName}” already keeps a history of its own. Would you like circuitRF to look after it?"
            : "This workspace already keeps a history of its own. Would you like circuitRF to look after it?";

        CostList.ItemsSource = (IReadOnlyList<string>)RepositoryAdoption.KeepingCosts();

        // Said next to the costs, because a list of things you lose reads as a threat without it — and
        // because it is true: adoption changes how the repository is looked after, never what is in it.
        SurvivesLabel.Text =
            "Either way, everything already in this workspace's history stays exactly as it is. "
          + "circuitRF changes how the history is looked after, never what is in it.";

        SettingsOnlyLabel.Text = HoldMessages.AdoptionCoversSettingsOnly;
    }

    private void OnAdoptClick(object? sender, RoutedEventArgs e)   => Close(AdoptionAnswer.Adopt);
    private void OnKeepClick(object? sender, RoutedEventArgs e)    => Close(AdoptionAnswer.KeepUserSettings);
    private void OnDeclineClick(object? sender, RoutedEventArgs e) => Close(AdoptionAnswer.DontKeepHistory);
}
