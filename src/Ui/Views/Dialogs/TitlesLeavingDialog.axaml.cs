using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CircuitRF.Design.Revision;

namespace CircuitRF.Ui.Views.Dialogs;

/// <summary>One row of the review — a version title about to leave this machine.</summary>
public sealed class LeavingTitleRow
{
    public LeavingTitleRow(LeavingTitle title, bool showAuthor, bool canCorrect)
    {
        Title      = title;
        ShowAuthor = showAuthor && title.Who.Length > 0;
        CanCorrect = canCorrect;
    }

    /// <summary>R-rc11-19. Whether the fix is offered on this row — false when the caller supplied no
    /// way to apply one, which is every context with no window behind it.</summary>
    public bool CanCorrect { get; }

    public LeavingTitle Title { get; }

    /// <summary>What the other side reads first — the correction if there is one, the title otherwise.</summary>
    public string Shown => Title.Shown;

    /// <summary>And what it corrects. <b>The pair is the answer</b>: a review that showed only the
    /// correction would be hiding the string that is actually in the file being sent.</summary>
    public string OriginalNote => Title.HasCorrection ? "originally: " + Title.Title : "";

    public bool   HasCorrection => Title.HasCorrection;
    public string When          => Title.WhenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string Who           => Title.Who;
    public bool   ShowAuthor    { get; }
}

/// <summary>
/// <b>The review in front of the three operations that let a history off this machine</b>
/// (<c>docs/design/revision-control.md</c> §5.11, §9, §9A.3; §12 Q36; RC-11 R-rc11-16 … R-rc11-19).
///
/// <para><b>It is worth more than every correction mechanism in §5.11</b> (R-rc11-17), and that is not
/// a rhetorical flourish. The corrections above answer a careless word; this answers a customer's name
/// or a part number in a title going to a <i>different</i> customer — which is far likelier and far
/// more expensive, and which nobody can catch from memory because nobody can recall what forty titles
/// say. §9A.3 already establishes where a computation the user cannot perform belongs: in front of the
/// operation.</para>
///
/// <para><b>It is not a confirmation prompt with a checkbox</b> (R-rc11-19). It is a readable list, and
/// the line under it says where the correction lives — because the useful response to reading a bad
/// title is fixing it, not abandoning the send. Stopping is the other button, and it is a way out
/// rather than the point.</para>
///
/// <para><b>On the archive it composes with §9A.3's file warning rather than replacing it</b>
/// (R-rc11-18). That warning is about files the history still holds after they were deleted from the
/// workspace; this is about titles. Both are true, they are about different things, and this dialog
/// carries both.</para>
/// </summary>
public partial class TitlesLeavingDialog : Window
{
    // The XAML loader needs a parameterless constructor (AVLN3001).
    public TitlesLeavingDialog() : this(LeavingJourney.Send, [], null, null) { }

    /// <param name="journey">Which operation is about to happen — it changes what leaves, not only
    /// the wording.</param>
    /// <param name="titles">What <see cref="TitlesLeaving.For"/> answered. <b>One function for all
    /// three journeys</b>, so no two of them can disagree about what is going.</param>
    /// <param name="fileWarning">
    /// §9A.3's own sentences about files the history holds and the workspace does not, on the archive
    /// journey. Null on the other two, where the archive's file question does not arise.
    /// </param>
    /// <param name="correct">
    /// R-rc11-19. <b>How to fix a title from here</b>, and what the list should say afterwards. Null
    /// when the caller has no way to apply one, and the buttons are then absent rather than inert.
    /// </param>
    public TitlesLeavingDialog(LeavingJourney journey, IReadOnlyList<LeavingTitle> titles,
                               string? fileWarning,
                               Func<LeavingTitle, Task<IReadOnlyList<LeavingTitle>>>? correct)
    {
        InitializeComponent();

        _journey = journey;
        _correct = correct;

        Show(titles);

        if (fileWarning is { Length: > 0 })
        {
            FileWarningText.IsVisible = true;
            FileWarningText.Text      = fileWarning;
        }

        GoOnButton.Content = journey switch
        {
            LeavingJourney.Send => "Send",
            LeavingJourney.Copy => "Copy",
            _                   => "Archive",
        };
    }

    private readonly LeavingJourney _journey;
    private readonly Func<LeavingTitle, Task<IReadOnlyList<LeavingTitle>>>? _correct;

    private void Show(IReadOnlyList<LeavingTitle> titles)
    {
        bool showAuthor = titles.Select(t => t.Who)
                                .Where(w => w.Length > 0)
                                .Distinct(System.StringComparer.Ordinal)
                                .Count() > 1;

        HeadingText.Text   = HistoryMessages.TitlesLeaving(titles.Count, TitlesLeaving.Describe(_journey));
        WhatToDoText.Text  = HistoryMessages.TitlesLeavingWhatToDo;
        Titles.ItemsSource = titles.Select(t => new LeavingTitleRow(t, showAuthor, _correct is not null))
                                   .ToList();
    }

    /// <summary>
    /// R-rc11-19. Corrects one row and <b>re-reads the list</b>, so what the sender is looking at after
    /// the fix is what is actually about to leave. A list that still showed the old wording would be
    /// the one thing this dialog exists to prevent, produced by the dialog itself.
    /// </summary>
    private async void OnCorrectRow(object? sender, RoutedEventArgs e)
    {
        if (_correct is null) return;
        if ((sender as Control)?.DataContext is not LeavingTitleRow row) return;

        Show(await _correct(row.Title));
    }

    private void OnGoOn(object? sender, RoutedEventArgs e) => Close(true);
    private void OnStop(object? sender, RoutedEventArgs e) => Close(false);
}
