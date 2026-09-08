using Avalonia.Controls;
using Avalonia.Interactivity;
using CircuitRF.Design.Revision;

namespace CircuitRF.Ui.Views.Dialogs;

/// <summary>Which of §5.11's three cases this dialog is standing in.</summary>
public enum CorrectionCase
{
    /// <summary>(a) A restore point's label — parentless, local, and correctable at any time.</summary>
    RestorePointLabel,

    /// <summary>(b) An unshared version's title — corrected in place, because there is no second
    /// reader to invalidate.</summary>
    UnsharedTitle,

    /// <summary>(c) A shared version — annotated, never erased, and the dialog says so.</summary>
    SharedTitle,
}

/// <summary>What the designer decided. Null from the dialog means they cancelled.</summary>
/// <param name="Text">What it should say. Empty is meaningful only in case (c), where it takes the
/// correction back and puts the original in front again.</param>
public sealed record CorrectionChoice(string Text);

/// <summary>
/// <b>Correcting what you wrote</b> (<c>docs/design/revision-control.md</c> §5.11; RC-11 R-rc11-12 …
/// R-rc11-14).
///
/// <para><b>One dialog for all three cases, and that is the point.</b> A designer looking at a bad
/// line does not know, and should not have to know, whether the entry has left the machine — that is
/// a computation (<see cref="VersionSharing"/>) and the answer decides which of two things the
/// dialog offers. Three separate controls on the menu would make the designer perform the
/// computation before choosing one.</para>
///
/// <para><b>What changes between the cases is the wording and one notice.</b> Cases (a) and (b)
/// replace what is written; case (c) adds a correction beside it and <b>says plainly that the
/// original wording stays in the file</b> — R-rc11-14's sentence, which may not be softened. A
/// designer whose problem is embarrassment specifically needs it, and a UI that hid it would cause the
/// exact harm they came to it to avoid.</para>
///
/// <para><b>There is no delete on case (c) and no code path behind one.</b> §5.3b rule 10's reassuring
/// sentence about a protection that does not exist would be exactly what a delete here would be — told
/// to the designer this time instead of by an agent. The string is on somebody else's disk.</para>
/// </summary>
public partial class CorrectWhatYouWroteDialog : Window
{
    // The XAML loader needs a parameterless constructor (AVLN3001).
    public CorrectWhatYouWroteDialog() : this(CorrectionCase.UnsharedTitle, "", null) { }

    /// <param name="which">Which of §5.11's three cases applies — computed by the caller, never
    /// guessed here.</param>
    /// <param name="current">What the entry says now, which is what the field opens on.</param>
    /// <param name="original">
    /// On case (c) with a correction already in place: the wording underneath it. Null everywhere
    /// else, because everywhere else the current text IS the original.
    /// </param>
    public CorrectWhatYouWroteDialog(CorrectionCase which, string current, string? original)
    {
        InitializeComponent();

        Title       = which == CorrectionCase.RestorePointLabel ? "Rename This Entry"
                                                                : "Correct What You Wrote";
        TextBox.Text = current;

        switch (which)
        {
            case CorrectionCase.RestorePointLabel:
                LeadText.Text   = "Give this entry a line you will recognise. The state it holds does "
                                + "not change — this is the label, not the workspace.";
                FieldLabel.Text = "What should it say?";
                NoticeText.Text = "This entry is this machine's own safety net: it never travels with "
                                + "a copy and nobody else has seen it, so the label is yours alone. "
                                + "What it holds, when it was taken and how it came about are "
                                + "untouched.";
                break;

            case CorrectionCase.UnsharedTitle:
                LeadText.Text   = "Correct the title on this version. It is the line you come back to "
                                + "and the line anyone you send this to reads first.";
                FieldLabel.Text = "What should it say?";
                NoticeText.Text = HistoryMessages.ThisOneHasNotLeftTheMachine;
                break;

            default:
                LeadText.Text   = "This version has already gone to another copy of this workspace. "
                                + "Add a correction to it — the correction shows in place of the "
                                + "original wherever this version is listed, and it travels with the "
                                + "version when it is sent.";
                FieldLabel.Text = "What should it say instead?";

                // R-rc11-14. NOT AN EXPANDER, not a tooltip, not softened.
                NoticeText.Text = HistoryMessages.ACorrectionDoesNotErase;

                if (original is { Length: > 0 })
                {
                    OriginalPanel.IsVisible = true;
                    OriginalText.Text       = "It originally said: " + original;
                    RemoveButton.IsVisible  = true;
                }
                break;
        }

        _case = which;
    }

    private readonly CorrectionCase _case;

    private void OnApply(object? sender, RoutedEventArgs e)
        => Close(new CorrectionChoice((TextBox.Text ?? "").Trim()));

    /// <summary>
    /// Takes the correction back, which is offered only in case (c) and only when there is one.
    ///
    /// <para><b>It erases nothing</b>, which is why it can be offered at all: the original was never
    /// altered, so removing the annotation puts the original wording back in front and changes nothing
    /// else. This is deliberately not a way to delete a version, and there is no control here that
    /// is.</para>
    /// </summary>
    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (_case == CorrectionCase.SharedTitle) Close(new CorrectionChoice(""));
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
