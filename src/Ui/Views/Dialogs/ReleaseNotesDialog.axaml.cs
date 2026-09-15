using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using CircuitRF.Ui.Markdown;
using CircuitRF.Ui.Updates;

namespace CircuitRF.Ui.Views.Dialogs;

/// <summary>
/// The <b>Release Notes</b> window — what changed in the version that has just been installed.
///
/// <para>It renders a <see cref="ReleaseNotesResult"/> and does nothing else: it does not decide
/// whether to open (<see cref="ReleaseNotesGate"/>), does not fetch (<see cref="ReleaseNotesFetcher"/>)
/// and does not parse (<see cref="SmallMarkdown"/>). All three of those are testable without
/// Avalonia, which is the point of the split — <c>Ui.Tests</c> calls no Avalonia runtime API, so
/// anything decided in this file is decided where no test can reach it.</para>
/// </summary>
public partial class ReleaseNotesDialog : Window
{
    /// <summary>
    /// The per-release banner, above every section when there is more than one — larger than any
    /// heading a release body can contain (15), smaller than the window's own title (18), so the
    /// boundary between two releases never reads as a section inside one of them.
    /// </summary>
    private const double BannerFontSize = 16;

    private string _browseUrl = "";

    /// <summary>
    /// The populate guard, for the same reason <c>UpdateSettingsView</c> has one: setting
    /// <c>IsChecked</c> raises <c>IsCheckedChanged</c>, so without it opening the dialog would write
    /// the preference it just read.
    /// </summary>
    private bool _loading;

    public ReleaseNotesDialog() : this(new ReleaseNotesResult(
        ReleaseNotesOutcome.Unavailable, AppVersion.Display, [],
        ReleaseNotesFetcher.BrowseUrl(GitHubReleasesFeed.DefaultApiUrl)))
    {
        // Parameterless for the XAML previewer only; nothing in the application uses it.
    }

    public ReleaseNotesDialog(ReleaseNotesResult result)
    {
        InitializeComponent();
        Render(result);
    }

    private void Render(ReleaseNotesResult result)
    {
        _browseUrl = result.BrowseUrl;

        HeadingText.Text = $"{UpdateApp.Name} {result.Version}";

        _loading = true;
        try { AlwaysShowCheck.IsChecked = ReleaseNotesGate.ShowPreference; }
        finally { _loading = false; }

        NotesText.Inlines?.Clear();

        switch (result.Outcome)
        {
            case ReleaseNotesOutcome.Found:
                AppendSections(result.Sections);
                break;

            case ReleaseNotesOutcome.NotPublished:
                BrowseButton.IsVisible = true;
                AppendFailure(
                    $"No release notes have been published for {UpdateApp.Name} {result.Version}.",
                    result.BrowseUrl);
                break;

            default:
                BrowseButton.IsVisible = true;
                AppendFailure(
                    $"The release notes for {UpdateApp.Name} {result.Version} could not be downloaded. "
                    + "The repository may be unreachable from this network.",
                    result.BrowseUrl);
                break;
        }
    }

    /// <summary>
    /// Renders every release the user has not been shown, newest at the top, into the one selectable
    /// block — which is why they are appended to a single <c>Inlines</c> collection rather than
    /// stacked as one control per release: a selection cannot cross two controls, and the owner's
    /// requirement is that the whole thing drags and copies in one go.
    ///
    /// <para><b>The version banner appears only when there is more than one section.</b> With a single
    /// release it would restate the window heading immediately beneath itself; with several it is the
    /// only thing separating one release's notes from the next, since a body's own headings render at
    /// the same weight whichever release they came from.</para>
    /// </summary>
    private void AppendSections(IReadOnlyList<ReleaseNoteSection> sections)
    {
        InlineCollection? inlines = NotesText.Inlines;
        if (inlines is null) return;

        bool banners = sections.Count > 1;

        for (int i = 0; i < sections.Count; i++)
        {
            if (i > 0)
            {
                // Two breaks, not one: a blank row between releases, matching the gap the parser
                // already puts between paragraphs.
                inlines.Add(new LineBreak());
                inlines.Add(new LineBreak());
            }

            if (banners)
            {
                inlines.Add(new Run($"{UpdateApp.Name} {sections[i].Version}")
                {
                    FontSize   = BannerFontSize,
                    FontWeight = FontWeight.Bold,
                });
                inlines.Add(new LineBreak());
                inlines.Add(new LineBreak());
            }

            MarkdownInlines.Append(inlines, SmallMarkdown.Parse(sections[i].Markdown),
                                   MarkdownInlines.ReleaseNotesBodyFontSize);
        }
    }

    /// <summary>
    /// The two failure forms. The URL is written into the selectable block as well as being behind the
    /// button, because a user on a machine with no browser association still has to be able to read
    /// and copy it — which is the whole reason this text is selectable.
    /// </summary>
    private void AppendFailure(string message, string url)
    {
        InlineCollection? inlines = NotesText.Inlines;
        if (inlines is null) return;

        inlines.Add(new Run(message));
        inlines.Add(new LineBreak());
        inlines.Add(new LineBreak());
        inlines.Add(new Run("They can be read at:"));
        inlines.Add(new LineBreak());
        inlines.Add(new Run(url) { FontWeight = FontWeight.Bold });
    }

    private void OnAlwaysShowChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ReleaseNotesGate.SetShowPreference(AlwaysShowCheck.IsChecked == true);
    }

    /// <summary>
    /// Opens the releases page in the default browser. Mirrors <c>DocLauncher</c>'s cross-platform
    /// open, and swallows everything for the same reason it does: a dialog offering a link must not be
    /// able to take the application down with it.
    /// </summary>
    private void OnBrowseClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start(new ProcessStartInfo("open", _browseUrl) { UseShellExecute = false });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(_browseUrl) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("xdg-open", _browseUrl) { UseShellExecute = false });
        }
        catch (Exception) { /* no browser, no association — the URL is on screen either way */ }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();
}
