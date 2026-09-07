using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CircuitRF.Design.Revision;
using CircuitRF.Ui.Theming;

namespace CircuitRF.Ui.Views.Dialogs;

/// <summary>
/// Names the git circuitRF runs, and reports what answered.
///
/// <para><b>The one row on Settings ▸ Revision Control that stays live when there is no git</b>
/// (owner, 2026-09-07). Everything else on that tab is greyed there, because none of it is keeping
/// anything; this is greyed nowhere, because it is the remedy.</para>
///
/// <para><b>Blank is the default and means "look on PATH".</b> It is never seeded with a discovered
/// path: a seeded value would freeze today's PATH answer into <c>preferences.json</c> and stop tracking
/// a git the user later installs or upgrades — the same "absence IS the default" rule every nullable
/// preference beside it follows, and the reason
/// <see cref="VerilogACompilerSettingsView"/> states it too.</para>
/// </summary>
public partial class GitPathSettingsView : UserControl
{
    /// <summary>
    /// Raised whenever the answer to <i>"is there a usable git"</i> may have changed — a path named or
    /// cleared, or a Detect that resolved one.
    ///
    /// <para><b>Detect raises it too, and that is the half that is easy to leave out.</b> Detect writes
    /// no preference, so a handler keyed on the PATH changing would miss the ordinary sequence of
    /// installing git and pressing the button that exists to find it — and the controls it should have
    /// enabled would stay grey until Settings was closed and reopened, with nothing saying why.</para>
    /// </summary>
    public event EventHandler? GitAvailabilityChanged;

    /// <summary>The populate guard — opening and closing Settings without touching anything must
    /// write nothing. Same reason as <see cref="VerilogACompilerSettingsView"/>'s.</summary>
    private bool _loading;

    public GitPathSettingsView()
    {
        InitializeComponent();
        Load();
    }

    /// <summary>Hides the section header, for a host that supplies its own (a tab title).</summary>
    public bool ShowSectionHeader
    {
        get => SectionHeader.IsVisible;
        set => SectionHeader.IsVisible = value;
    }

    public void Load()
    {
        _loading = true;
        try { GitPathBox.Text = AppPreferencesIo.Load().RevisionGitPath ?? ""; }
        finally { _loading = false; }
    }

    /// <summary>
    /// Writes the named git. Blank clears it back to "look on PATH", stored as null rather than as an
    /// empty string so "never chosen" and "deliberately cleared" stay the same state — which is what
    /// the default rests on.
    /// </summary>
    private void OnGitPathCommitted(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;

        string typed = GitPathBox.Text?.Trim() ?? "";
        AppPreferencesIo.Update(p => p.RevisionGitPath = typed.Length == 0 ? null : typed);

        // A settings change that only mutates JSON is incomplete. Discovery memoises its answer keyed
        // on the preference, so a stale entry would not survive anyway — but dropping it here means the
        // NEXT thing to ask, including this control's own Detect, resolves through the git just named
        // rather than the change appearing to take effect only after a restart.
        GitDiscovery.InvalidateCache();

        ShowStatus("");
        GitAvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OnBrowseForGit(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title         = "Choose git",
                AllowMultiple = false,
            });

            if (files.Count != 1 || files[0].TryGetLocalPath() is not { Length: > 0 } path) return;

            GitPathBox.Text = path;
            OnGitPathCommitted(sender, e);
        }
        catch (Exception) { /* a cancelled or unavailable picker is not an error */ }
    }

    /// <summary>
    /// Finds git and reports the resolved path and version, or why there is none.
    ///
    /// <para>It goes through the SAME discovery every other caller uses rather than probing the text
    /// box directly, so a blank box genuinely answers "here is what PATH would give you" — the question
    /// a user with nothing configured actually has, and one a direct probe could not answer at
    /// all.</para>
    ///
    /// <para><b>An under-floor git is reported here as TOO OLD, naming both versions.</b> Everywhere
    /// else in the application it is treated as absent, because a designer with an old git does not
    /// want to be told about a feature they cannot have — but somebody who pressed this button asked,
    /// and "not found" would send them looking for an installation that is right there.</para>
    /// </summary>
    private void OnDetectGit(object? sender, RoutedEventArgs e)
    {
        // Ask the machine again rather than the memo. Detect is the button somebody presses BECAUSE
        // they just changed something outside circuitRF — installed git, fixed a PATH, mounted a share
        // — and a memoised "no" would answer for the machine as it was before they did.
        GitDiscovery.InvalidateCache();

        try
        {
            if (GitDiscovery.Find(out var rejected) is { } found)
            {
                ShowStatus($"Found, {found.HowFound}: {found.Path} — {found.Banner}");
                GitAvailabilityChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Discovery's own rejection notes already carry the too-old sentence, with the version
            // found and the version needed, because TryProbe writes it — so this reports rather than
            // re-deriving it, and cannot drift from the floor discovery actually enforces.
            ShowStatus(rejected.Count == 0
                ? "No git was found on PATH. Install git, or name one above."
                : "No usable git: " + string.Join("; ", rejected));

            // Raised on the failing path as well: a git that used to resolve and no longer does is
            // exactly as much of a change to what this tab may claim as one that has just appeared.
            GitAvailabilityChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { ShowStatus(ex.Message); }
    }

    private void ShowStatus(string text)
    {
        GitStatusText.Text      = text;
        GitStatusText.IsVisible = text.Length > 0;
    }
}
