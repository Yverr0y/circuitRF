using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CircuitRF.Design.Revision;
using CircuitRF.Ui.Revision;
using CircuitRF.Ui.Theming;

namespace CircuitRF.Ui.Views.Dialogs;

/// <summary>
/// Settings ▸ Revision Control — <b>the place a designer reads to find out what is being kept</b>
/// (<c>docs/design/revision-control.md</c> §10A; RC-4).
///
/// <para><b>What the existing consent model says, and where git landed inside it</b> (R-rc4-2a). The
/// Security &amp; Permissions tab was read before this one was built, and it does not have one uniform
/// consent shape — it has three:</para>
/// <list type="bullet">
///   <item>a <b>per-subject prompt</b>: a kit's generated artwork asks once per kit, and the tab's row
///     only forgets those answers so it asks again;</item>
///   <item>a <b>standing on/off switch, default on</b>, with an installation-level override that beats
///     it: external device workers, and automatic updates;</item>
///   <item><b>no gate at all</b>: the Verilog-A compiler is a PATH and nothing more. Naming it is not
///     consent to run it, and there is no checkbox that stops circuitRF running it — pointing a
///     component at <c>.va</c> source is the consent, and the row exists so the user can say WHICH
///     program that is.</item>
/// </list>
/// <para><b>git is brought under the third of those, and deliberately so</b> — it is the same shape as
/// the Verilog-A compiler almost exactly: a program circuitRF starts as a subprocess, blank meaning
/// "search PATH", a named path outranking PATH, a status line reporting what answered. What differs is
/// that git also gets the second shape's standing switch, because
/// <b><i>keep a history of my workspaces</i> IS the on/off consent</b> for running it — off means
/// circuitRF never invokes git at all. So git is not outside the model and is not given a parallel one:
/// it has the compiler's path row and the updater's switch, in the tab that owns what is KEPT, and the
/// path row's second host is Security &amp; Permissions itself. The one thing that is genuinely new is
/// that the switch defaults ON where the others' subjects are opt-in per kit — which §4.3 pays for by
/// making the entire feature invisible to anyone who did not install git deliberately.</para>
///
/// <para><b>Every control that can reduce what is kept states its consequence beside itself</b>
/// (R-rc4-13), in this UI and not only in a tooltip. Those sentences are in the XAML; the ones that
/// name a number are completed here, because a sentence that says "after this long" when the field says
/// 30 is not the sentence a designer needs.</para>
///
/// <para><b>The two switches are not the same switch.</b> The preference answers "do I want this at
/// all"; the <c>.cws</c> flag answers "not for this one" and outranks it. Precedence lives in
/// <see cref="RevisionArming.IsArmed"/> so it cannot be spelled differently here and in RC-5.</para>
/// </summary>
public partial class RevisionControlSettingsView : UserControl
{
    /// <summary>The populate guard — opening and closing Settings without touching anything must write
    /// nothing, and must not write a workspace's <c>.cws</c> either.</summary>
    private bool _loading;

    /// <summary>
    /// The open workspace's folder, or null when Settings was opened with none. Null disables the
    /// per-workspace row and says why, rather than hiding it: a row that vanishes when no workspace is
    /// open reads as a feature that does not exist.
    /// </summary>
    private string? _workspaceDir;

    public RevisionControlSettingsView()
    {
        InitializeComponent();
        Load();
    }

    /// <summary>Tells the tab which workspace it is talking about. Called by the host before it is
    /// shown; the row's label and its enabled state both depend on it.</summary>
    public void SetWorkspace(string? workspaceDirectory)
    {
        _workspaceDir = workspaceDirectory;
        Load();
    }

    /// <summary>The git-path control, so a host can move it to its other home.</summary>
    public GitPathSettingsView PathControl => GitPath;

    // ── Load ─────────────────────────────────────────────────────────────────────────────────────

    public void Load()
    {
        _loading = true;
        try
        {
            var prefs = AppPreferencesIo.Load();

            GitPath.Load();
            LoadIdentity(prefs);

            KeepHistoryCheck.IsChecked        = prefs.RevisionKeepHistory ?? RevisionArming.KeepHistoryDefault;
            CheckpointOnCloseCheck.IsChecked  = prefs.RevisionCheckpointOnClose ?? RevisionPreferenceDefaults.CheckpointOnClose;

            SetBounds(RetentionDaysUpDown,
                      RevisionPreferenceDefaults.RetentionDaysFloor,
                      RevisionPreferenceDefaults.RetentionDaysCeiling,
                      prefs.RevisionRetentionDays ?? RevisionPreferenceDefaults.RetentionDays);

            SetBounds(MinimumPointsUpDown,
                      RevisionPreferenceDefaults.MinimumRestorePointsFloor,
                      RevisionPreferenceDefaults.MinimumRestorePointsCeiling,
                      prefs.RevisionMinimumRestorePoints ?? RevisionPreferenceDefaults.MinimumRestorePoints);

            SetBounds(PackThresholdUpDown,
                      RevisionPreferenceDefaults.PackThresholdMbFloor,
                      RevisionPreferenceDefaults.PackThresholdMbCeiling,
                      prefs.RevisionPackThresholdMb ?? RevisionPreferenceDefaults.PackThresholdMb);

            SetBounds(ReclaimAgeUpDown,
                      RevisionPreferenceDefaults.ReclaimAgeDaysFloor,
                      RevisionPreferenceDefaults.ReclaimAgeDaysCeiling,
                      prefs.RevisionReclaimAgeDays ?? RevisionPreferenceDefaults.ReclaimAgeDays);

            LoadWorkspaceScopedControls();
            RefreshConsequences();
        }
        finally { _loading = false; }
    }

    /// <summary>
    /// Fills the identity boxes, <b>pre-filling from the user's existing global git identity when
    /// circuitRF has none of its own</b> (R-rc4-11a, §4.4).
    ///
    /// <para><b>Pre-filled, not written back.</b> Reading somebody's global config is unobjectionable;
    /// only writing it was ever the problem — circuitRF has no business changing a setting that affects
    /// every other repository on the machine. The pre-fill is not saved either: it sits in the boxes
    /// until the user commits it, so a machine whose global identity later changes is not silently
    /// pinned to the old one by a value circuitRF copied behind their back.</para>
    /// </summary>
    private void LoadIdentity(AppPreferences prefs)
    {
        IdentityNameBox.Text  = prefs.RevisionIdentityName  ?? "";
        IdentityEmailBox.Text = prefs.RevisionIdentityEmail ?? "";

        if (IdentityNameBox.Text.Length > 0 && IdentityEmailBox.Text.Length > 0) return;

        try
        {
            // Needs a git and a directory to run it in; any directory will do for a --global read.
            if (GitDiscovery.Find(out _) is not { } installation) return;
            var git = new GitCommand(installation, AppContext.BaseDirectory);

            if (RevisionIdentity.GlobalGitDefault(git) is not { } theirs) return;
            if (IdentityNameBox.Text.Length  == 0) IdentityNameBox.Text  = theirs.Name;
            if (IdentityEmailBox.Text.Length == 0) IdentityEmailBox.Text = theirs.Email;
        }
        catch (Exception) { /* no identity to borrow is an ordinary state, not a failure */ }
    }

    /// <summary>
    /// Everything on this tab whose meaning depends on WHICH workspace is open — the per-workspace
    /// switch, and the two Disk Space actions.
    ///
    /// <para><b>Each of them names the workspace it would act on</b> (owner, 2026-09-06). Neither
    /// button opens a picker: they act on the workspace that is already open, and a button that
    /// silently picks its own subject is one a user has to test to understand. The other four rows on
    /// this tab are per-user preferences and are deliberately not named this way — the naming is what
    /// separates them.</para>
    ///
    /// <para>Read straight from the <c>.cws</c> rather than from anything the window is holding, so
    /// what the checkbox shows is what is actually recorded — which is the only reading that survives
    /// two workspaces being open at once with different values.</para>
    /// </summary>
    private void LoadWorkspaceScopedControls()
    {
        bool preference = KeepHistoryCheck.IsChecked == true;

        if (_workspaceDir is not { Length: > 0 } dir)
        {
            WorkspaceRevisionCheck.IsEnabled     = false;
            WorkspaceRevisionCheck.IsChecked     = preference;
            WorkspaceRevisionCheck.Content       = "Keep a history of this workspace";
            WorkspaceRevisionConsequence.IsVisible = false;
            WorkspaceRevisionScope.IsVisible     = true;
            WorkspaceRevisionScope.Text =
                "No workspace is open, so there is nothing for this to apply to. Open a workspace and "
              + "this row will name it.";

            // Disabled rather than left to refuse on click: with nothing to name, the button cannot
            // say what it would do, and the line below says why instead.
            PackNowButton.IsEnabled  = false;
            ReclaimButton.IsEnabled  = false;
            PackNowButton.Content    = "Compact Now";
            ReclaimButton.Content    = "Reclaim Space…";
            PackStatusText.Text      =
                "No workspace is open, so there is nothing to compact or reclaim. These act on the "
              + "workspace you have open, and name it.";
            ShowReclaimStatus("");
            return;
        }

        string cws      = WorkspaceRevisionSetting.CwsPathFor(dir);
        bool?  recorded = WorkspaceRevisionSetting.Read(cws);
        string named    = QuotedWorkspaceName(dir);

        WorkspaceRevisionCheck.IsEnabled = true;
        WorkspaceRevisionCheck.IsChecked = RevisionArming.IsArmed(preference, recorded);
        WorkspaceRevisionCheck.Content   = $"Keep a history of {named}";

        WorkspaceRevisionConsequence.IsVisible = true;
        WorkspaceRevisionScope.IsVisible       = recorded is null;
        WorkspaceRevisionScope.Text = recorded is null
            ? "This workspace has not been given an answer of its own, so it follows the setting above. "
            + "Changing it here records one for this workspace only."
            : "";

        PackNowButton.IsEnabled = true;
        ReclaimButton.IsEnabled = true;
        PackNowButton.Content   = $"Compact {named}";
        ReclaimButton.Content   = $"Reclaim Space in {named}…";
        PackStatusText.Text     =
            "Stores the same history in less space. Nothing is discarded and nothing becomes "
          + "unavailable.";
        ShowReclaimStatus("");
    }

    /// <summary>
    /// A workspace's name, quoted, for a control that has to say which one it acts on.
    ///
    /// <para><b>Truncated</b>, because this goes inside a button: a folder named after a part number
    /// and a date would otherwise stretch the Disk Space row past the dialog's own minimum width, and
    /// a button that reflows the layout it sits in is worse than one that abbreviates. The quotes stay
    /// on both sides of a truncation so it still reads as a name.</para>
    /// </summary>
    private static string QuotedWorkspaceName(string directory)
    {
        string name;
        try { name = new DirectoryInfo(directory).Name; }
        catch (Exception e) when (e is ArgumentException or PathTooLongException) { name = directory; }

        const int max = 28;
        if (name.Length > max) name = name[..(max - 1)] + "…";
        return $"“{name}”";
    }

    /// <summary>
    /// Completes the consequence sentences that name a number, and reports what the reclaim would act
    /// on. Called on load and after any of the four numbers change.
    /// </summary>
    private void RefreshConsequences()
    {
        int days     = (int)(RetentionDaysUpDown.Value ?? RevisionPreferenceDefaults.RetentionDays);
        int floor    = (int)(MinimumPointsUpDown.Value ?? RevisionPreferenceDefaults.MinimumRestorePoints);
        int reclaim  = (int)(ReclaimAgeUpDown.Value    ?? RevisionPreferenceDefaults.ReclaimAgeDays);

        RetentionConsequence.Text =
            $"After {Days(days)}, automatic restore points are thinned and the states they held are no "
          + $"longer offered. The newest {floor} are always kept whatever their age, and changes you "
          + "recorded yourself are never thinned.";

        MinimumPointsConsequence.Text =
            $"The newest {floor} restore points are kept however old they are, so a machine whose clock "
          + "is wrong cannot expire them. That is why this field will not go below "
          + $"{RevisionPreferenceDefaults.MinimumRestorePointsFloor}.";

        ReclaimConsequence.Text =
            $"Reclaiming permanently destroys the states behind restore points thinned more than "
          + $"{Days(reclaim)} ago — after it, nobody can bring them back. Every restore point still "
          + "listed, and every change you recorded yourself, survives it. Nothing reclaims on a "
          + "schedule; this only happens when you ask.";
    }

    private static string Days(int n) => n == 1 ? "1 day" : $"{n} days";

    /// <summary>
    /// Bounds first, then the value.
    ///
    /// <para><b>The order is the point, not tidiness.</b> A <c>NumericUpDown</c> coerces whatever it is
    /// given into its current <c>Minimum</c>/<c>Maximum</c>, so setting a value of 30 while the minimum
    /// is still 0 and then raising the minimum leaves the control holding a number the preference never
    /// said. This repo has already been bitten by a coercing control writing back — see
    /// <c>src/Ui/RESOLVED.md</c> on the Match slider, where a <c>RangeBase</c> clamp reached an
    /// unguarded setter mid-Undo.</para>
    /// </summary>
    private static void SetBounds(NumericUpDown box, int min, int max, int value)
    {
        box.Minimum = min;
        box.Maximum = max;
        box.Value   = RevisionPreferenceDefaults.Clamp(value, min, max);
    }

    // ── Writers ──────────────────────────────────────────────────────────────────────────────────

    private void OnIdentityCommitted(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;

        string name  = IdentityNameBox.Text?.Trim()  ?? "";
        string email = IdentityEmailBox.Text?.Trim() ?? "";

        // Blank stores null rather than "", so "never set" and "cleared" are the same state — which is
        // what RevisionIdentity's both-or-neither rule rests on. Half an identity leaves git to guess
        // the other half at `user@hostname`, which is the silent wrong answer §4.4 exists to stop.
        AppPreferencesIo.Update(p =>
        {
            p.RevisionIdentityName  = name.Length  == 0 ? null : name;
            p.RevisionIdentityEmail = email.Length == 0 ? null : email;
        });

        RefreshIdentityRequiredText();
    }

    private void RefreshIdentityRequiredText()
    {
        bool complete = (IdentityNameBox.Text?.Trim().Length  ?? 0) > 0
                     && (IdentityEmailBox.Text?.Trim().Length ?? 0) > 0;

        IdentityRequiredText.Text = complete
            ? "Used for every change circuitRF records. It is not written into the workspace, and not "
            + "into your own git configuration."
            : "Both are needed before circuitRF starts keeping a history. Until they are filled in, "
            + "nothing is recorded.";
        IdentityRequiredText.Opacity = complete ? 0.6 : 0.85;
    }

    private void OnKeepHistoryChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppPreferencesIo.Update(p => p.RevisionKeepHistory = KeepHistoryCheck.IsChecked == true);

        // The per-workspace row FOLLOWS this one wherever it has no answer of its own — and must not be
        // rewritten where it has. Re-reading is what keeps those two cases apart.
        _loading = true;
        try { LoadWorkspaceScopedControls(); } finally { _loading = false; }
    }

    /// <summary>
    /// Records this workspace's own answer, in the <c>.cws</c> and never in <c>AppPreferences</c>
    /// (R-rc4-12).
    ///
    /// <para>Read-modify-write against the file, which is the idiom every other <c>.cws</c> writer in
    /// this repository uses. A read-only workspace declines the write silently, exactly as it declines
    /// every other one — so the row is re-read afterwards rather than left showing a value that was not
    /// stored.</para>
    /// </summary>
    private void OnWorkspaceRevisionChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (_workspaceDir is not { Length: > 0 } dir) return;

        WorkspaceRevisionSetting.Write(WorkspaceRevisionSetting.CwsPathFor(dir),
                                       WorkspaceRevisionCheck.IsChecked == true);

        _loading = true;
        try { LoadWorkspaceScopedControls(); } finally { _loading = false; }
    }

    private void OnRetentionDaysChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loading) return;
        int days = Clamped(RetentionDaysUpDown,
                           RevisionPreferenceDefaults.RetentionDaysFloor,
                           RevisionPreferenceDefaults.RetentionDaysCeiling,
                           RevisionPreferenceDefaults.RetentionDays);
        AppPreferencesIo.Update(p => p.RevisionRetentionDays = days);
        RefreshConsequences();
    }

    private void OnMinimumPointsChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loading) return;
        int n = Clamped(MinimumPointsUpDown,
                        RevisionPreferenceDefaults.MinimumRestorePointsFloor,
                        RevisionPreferenceDefaults.MinimumRestorePointsCeiling,
                        RevisionPreferenceDefaults.MinimumRestorePoints);
        AppPreferencesIo.Update(p => p.RevisionMinimumRestorePoints = n);
        RefreshConsequences();
    }

    private void OnCheckpointOnCloseChanged(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        AppPreferencesIo.Update(p => p.RevisionCheckpointOnClose = CheckpointOnCloseCheck.IsChecked == true);
    }

    private void OnPackThresholdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loading) return;
        int mb = Clamped(PackThresholdUpDown,
                         RevisionPreferenceDefaults.PackThresholdMbFloor,
                         RevisionPreferenceDefaults.PackThresholdMbCeiling,
                         RevisionPreferenceDefaults.PackThresholdMb);
        AppPreferencesIo.Update(p => p.RevisionPackThresholdMb = mb);
    }

    private void OnReclaimAgeChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loading) return;
        int days = Clamped(ReclaimAgeUpDown,
                           RevisionPreferenceDefaults.ReclaimAgeDaysFloor,
                           RevisionPreferenceDefaults.ReclaimAgeDaysCeiling,
                           RevisionPreferenceDefaults.ReclaimAgeDays);
        AppPreferencesIo.Update(p => p.RevisionReclaimAgeDays = days);
        RefreshConsequences();
    }

    /// <summary>
    /// The value a numeric field settled on, forced inside its own bounds and written back to the
    /// control if it was not.
    ///
    /// <para><b>The field cannot be set below its minimum, including by TYPING one</b> (R-rc4-10, gate
    /// 7). <c>NumericUpDown</c> coerces a typed value on commit, but a null — which is what an empty or
    /// unparseable box gives — coerces to nothing at all, and leaving that to reach the preference is
    /// how a hard floor becomes an advisory one.</para>
    /// </summary>
    private static int Clamped(NumericUpDown box, int min, int max, int fallback)
    {
        int raw     = box.Value is { } v ? (int)v : fallback;
        int clamped = RevisionPreferenceDefaults.Clamp(raw, min, max);
        if (clamped != raw || box.Value is null)
            box.Value = clamped;
        return clamped;
    }

    // ── Actions ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stores this workspace's history more compactly, now. <b>Packing never reclaims</b> — the
    /// repository's own configuration carries three <c>never</c> rows for exactly that, and this path
    /// passes no expiry of its own.
    /// </summary>
    private void OnPackNow(object? sender, RoutedEventArgs e)
    {
        if (RepositoryOrExplain(t => PackStatusText.Text = t) is not { } git) return;

        try
        {
            var before = GitPacking.Measure(git);
            var (outcome, diagnostic) = GitPacking.Pack(git);

            if (diagnostic is not null) { PackStatusText.Text = diagnostic.Render(); return; }

            var after = GitPacking.Measure(git);
            PackStatusText.Text = outcome switch
            {
                PackOutcome.Packed when before is not null && after is not null =>
                    $"Compacted: {Megabytes(before.LooseBytes + before.PackedBytes)} → "
                  + $"{Megabytes(after.LooseBytes + after.PackedBytes)}. Nothing was discarded.",
                PackOutcome.Packed  => "Compacted. Nothing was discarded.",
                _                   => "There was nothing worth compacting.",
            };
        }
        catch (Exception ex) { PackStatusText.Text = ex.Message; }
    }

    /// <summary>
    /// The tab's one destructive action (§5.6a, R-rc4-10).
    ///
    /// <para><b>It asks first, and the question names what it will destroy</b> — how many states, and
    /// how long ago they were thinned. Cancelling reclaims nothing: the confirmation is what calls
    /// <c>GitReclaim.Reclaim</c>, and this is the only path in <c>src/Ui</c> that reaches it at all.
    /// The count comes from the thinning journal rather than from git's own object age, because git
    /// expires by when a state was MADE and the question here is when it was THINNED — a two-year-old
    /// restore point thinned yesterday must not be destroyed by a one-month reclaim.</para>
    /// </summary>
    private async void OnReclaimSpace(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        if (RepositoryOrExplain(ShowReclaimStatus) is not { } git) return;

        int  days    = (int)(ReclaimAgeUpDown.Value ?? RevisionPreferenceDefaults.ReclaimAgeDays);
        var  age     = TimeSpan.FromDays(days);
        var  journal = ThinningJournal.Read(git.WorkspaceRoot);
        int  count   = ThinningJournal.CountOlderThan(journal, age);

        if (count == 0)
        {
            ShowReclaimStatus(
                $"Nothing has been thinned more than {Days(days)} ago, so there is nothing to reclaim.");
            return;
        }

        if (await new ReclaimSpaceDialog(count, days).ShowDialog<bool>(owner) is not true)
        {
            ShowReclaimStatus("Nothing was reclaimed.");
            return;
        }

        try
        {
            var result = GitReclaim.Reclaim(git, journal, age);
            ShowReclaimStatus(result.Diagnostic is { } d
                ? d.Render()
                : $"Reclaimed the space held by {result.Reclaimed.Count} thinned "
                + $"{(result.Reclaimed.Count == 1 ? "state" : "states")}. "
                + $"{result.Protected.Count} newer {(result.Protected.Count == 1 ? "one was" : "were")} "
                + "left alone, and every restore point still listed is unaffected.");
        }
        catch (Exception ex) { ShowReclaimStatus(ex.Message); }
    }

    private void ShowReclaimStatus(string text)
    {
        ReclaimStatusText.Text      = text;
        ReclaimStatusText.IsVisible = text.Length > 0;
    }

    /// <summary>
    /// A driver for the open workspace's repository, or null with the reason already shown.
    ///
    /// <para>Both actions here are about a repository that exists; neither creates one, and neither is
    /// the thing that arms a workspace. There being nothing to act on is an ordinary answer.</para>
    /// </summary>
    private GitCommand? RepositoryOrExplain(Action<string> report)
    {
        if (_workspaceDir is not { Length: > 0 } dir)
        {
            // Not reachable from a click — both buttons are disabled with no workspace — but the two
            // states are checked in the same place so a later caller cannot skip one.
            report("No workspace is open.");
            return null;
        }

        var git = GitCommand.For(dir);
        if (git is null || !git.IsRepositoryRoot())
        {
            report($"{QuotedWorkspaceName(dir)} has no history yet, so there is nothing to act on.");
            return null;
        }

        return git;
    }

    private static string Megabytes(long bytes)
        => (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
}
