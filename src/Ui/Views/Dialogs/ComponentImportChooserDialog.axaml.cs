using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using CircuitRF.Ui.Layout;
using CircuitRF.Ui.Renderers;
using CircuitRF.Ui.Theming;

namespace CircuitRF.Ui.Views.Dialogs;

/// <summary>
/// The ranked list of what a scanned folder holds, which of it can be imported (R-PL1-4), and
/// <b>what the selected row actually looks like</b>.
///
/// <para><see cref="ComponentFolderScan"/> walks the folder, classifies every file by content
/// (R-PL1-28) and returns candidates ranked by completeness first and reader confidence second. This
/// dialog shows that list with the top row preselected, and beneath it one line naming each category
/// of file that was not read, with a count.</para>
///
/// <para>Not shown when the file the user pointed AT is itself the whole of the only candidate — there
/// is nothing to choose among. Returns the chosen <see cref="ComponentCandidate"/> via
/// <c>ShowDialog&lt;ComponentCandidate?&gt;</c>, or null on Cancel, which creates nothing.</para>
///
/// <para>Each row names the folder it came from (<see cref="ComponentCandidate.Location"/>). A folder
/// holding one part written out once per target format produces one candidate per format, and several
/// of them read identically without it.</para>
///
/// <para><b>The preview (owner, 2026-09-05).</b> Rows differing by a few characters can be entirely
/// different parts, and the list cannot say which — so the pane beside it draws the selected
/// candidate's symbol and land pattern. It is built through <see cref="ComponentPreview"/>, which is
/// the import's own reader and converter with the layer-mapping dialog left unasked, so the pictures
/// are what the import would write rather than a likeness of it. There is deliberately no zoom or
/// pan: this answers "is this the part I meant", not "is this artwork correct", and a viewport to
/// operate is a second thing to get wrong before importing anything.</para>
///
/// <para><b>Nothing is drawn on the UI thread.</b> Reading a component library and reconciling its
/// layers is not free, and it happens on every arrow-key press down the list — so both panes are
/// plain <see cref="Image"/>s fed finished bitmaps by <see cref="ComponentPreviewRenderer"/> on a
/// worker thread. A stale result is dropped rather than shown: every pass carries a generation
/// number and only the current one is allowed to touch a control.</para>
/// </summary>
public partial class ComponentImportChooserDialog : Window
{
    public ComponentImportChooserDialog()
    {
        InitializeComponent();
        ShowSearchField(false);          // the field is measured from the start, and not up
        Opened += (_, _) => ImportButton.Focus();
    }

    public ComponentImportChooserDialog(ComponentScanResult scan)
        : this(scan, "", null, LayoutUnits.DefaultDbuPerMicron)
    {
    }

    /// <param name="destTech">The workspace's own default technology — the one an import would
    /// reconcile against. Passed so the preview draws each pad in the colour it will actually be.
    /// Null is legitimate and draws through the fallback palette, which is also what the import would
    /// produce.</param>
    /// <param name="scanRoot">The folder the user chose. Its NAME is shown where the Project Tree
    /// shows the workspace's, and the whole path on its tooltip — a scan is only ever as
    /// comprehensible as knowing which folder it was of, and the <c>…</c> button beside it makes that
    /// change mid-dialog.</param>
    public ComponentImportChooserDialog(
        ComponentScanResult scan, string scanRoot, Technology? destTech, int dbuPerMicron) : this()
    {
        _destTech = destTech;
        _dbuPerMicron = dbuPerMicron;

        string folder = scanRoot.Length == 0
            ? ""
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(scanRoot));
        // A volume root trims to nothing; its full path is the only name it has.
        ScanRootText.Text = folder.Length > 0 ? folder : scanRoot;
        ToolTip.SetTip(ScanRootText, scanRoot.Length > 0 ? scanRoot : null);

        _all = scan.Candidates;
        _rows = [.. _all.Select(c => new CandidateRow(c))];
        BuildFilterPanel();
        ApplyFilter();                                                    // preselects the top row
        StartNameSweep();

        SkippedText.Text = scan.SkippedSummary.Count == 0
            ? $"{scan.FilesScanned:N0} file(s) scanned; nothing was skipped."
            : "Not read: " + string.Join(", ", scan.SkippedSummary) + ".";

        // A capped scan reporting a short list looks exactly like a small folder, so the list says
        // which it is rather than leaving the count to be read as the whole truth.
        TruncationText.Text = scan.TruncationNote ?? "";
        TruncationText.IsVisible = scan.TruncationNote is not null;

        // A pane cannot be rasterized before it has a size, and it has none until the window lays
        // out. Re-running on every size change is also what keeps a resized dialog sharp rather than
        // stretching the first render.
        // A right-click does not select in Avalonia, so without this the menu would act on whichever
        // row happened to be selected already — the one case where a "Reveal" opens somewhere the
        // user did not point at. Tunnelled, so it runs before the ListBox opens its menu.
        CandidateList.AddHandler(PointerPressedEvent, OnListPointerPressed, RoutingStrategies.Tunnel);

        SymbolPane.SizeChanged += (_, _) => SchedulePreview(ResizeDebounceMs);
        FootprintPane.SizeChanged += (_, _) => SchedulePreview(ResizeDebounceMs);
        Closed += (_, _) =>
        {
            _generation++;
            _sweep.Cancel();
            SetSymbolImage(null);
            SetFootprintImage(null);
        };
    }

    /// <summary>True when the <c>…</c> beside the folder name was pressed. Avalonia's
    /// <c>StorageProvider</c> exposes
    /// <c>OpenFilePickerAsync</c> and <c>OpenFolderPickerAsync</c> as separate calls and one dialog
    /// cannot return both, so this is how the caller knows to open the folder picker — the same shape
    /// R-L4h-5 uses for Gerber.</summary>
    public bool ChooseAnotherFolder { get; private set; }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnAnotherFolderClick(object? sender, RoutedEventArgs e)
    {
        ChooseAnotherFolder = true;
        Close(null);
    }

    private void OnImportClick(object? sender, RoutedEventArgs e) => Close(Selected);

    /// <summary>The selected row's candidate, or null — every consumer wants the candidate, not the
    /// row that carries it.</summary>
    private ComponentCandidate? Selected => (CandidateList.SelectedItem as CandidateRow)?.Candidate;

    // ── Reveal in Finder / Explorer / File Manager ──────────────────────────────────────────────

    /// <summary>
    /// Opens the folder a row's files came from.
    ///
    /// <para>The candidate's FIRST file rather than its folder, because <see cref="FileReveal"/>
    /// highlights the file it is given — and a candidate can be several files in one directory, so
    /// pointing at the one the read begins with says which row this is. A group whose files span two
    /// directories (a symbol one level above its land patterns is the ordinary shape) reveals the
    /// symbol's, which is the one the row is named after.</para>
    ///
    /// <para>The target is not required to exist: <c>FileReveal</c> walks up to the nearest folder
    /// that does. A scan result can be stale by the time it is right-clicked — the folder was chosen
    /// by pointing at it, and nothing here holds a lock on it.</para>
    /// </summary>
    /// <summary>Selects the row under a right-click before its menu opens.</summary>
    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(CandidateList).Properties.IsRightButtonPressed) return;

        for (var v = e.Source as Visual; v is not null; v = v.GetVisualParent())
            if (v is ListBoxItem { DataContext: CandidateRow row })
            {
                CandidateList.SelectedItem = row;
                return;
            }

        // A right-click on empty list space selects nothing, and the menu declines to open.
        CandidateList.SelectedItem = null;
    }

    private void OnRowContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // The label carries the platform's own word for it, so it is set here rather than in XAML.
        RevealMenuItem.Header = FileReveal.Label;

        var candidate = Selected;
        RevealMenuItem.IsEnabled = RevealTarget(candidate) is not null;

        // A menu whose only item does nothing is worse than no menu: right-clicking with nothing
        // selected should look like the miss it is.
        if (candidate is null) e.Cancel = true;
    }

    private void OnRevealClick(object? sender, RoutedEventArgs e)
    {
        if (RevealTarget(Selected) is { } path)
            FileReveal.Reveal(path);
    }

    private static string? RevealTarget(ComponentCandidate? candidate)
        => candidate is null
            ? null
            : (candidate.SymbolFile ?? candidate.FootprintFiles.FirstOrDefault()
                                    ?? candidate.Files.FirstOrDefault())?.Path;

    // ── Search and filter (the Project Tree's idiom, in code-behind) ────────────────────────────
    //
    // Same behaviours, same reasons — see ProjectTreeTool's own notes. The one difference is where
    // they live: this dialog has no view model, so the coalescing that ProjectTreeTool does with
    // FilterScheduler is done here with the same Dispatcher.Post at Background priority. Input is
    // dispatched above Background, so the keystroke always renders first and the filter pass runs in
    // whatever gap follows — no delay to tune, and no lag on a single character typed and left.

    private IReadOnlyList<ComponentCandidate> _all = [];

    /// <summary>One row per candidate, built ONCE and re-filtered rather than rebuilt: a row carries
    /// the declared name a read has filled in, and rebuilding the list would throw that away every
    /// time the search text changed.</summary>
    private readonly List<CandidateRow> _rows = [];

    /// <summary>Declared names by candidate, from whichever produced one first — the preview's own
    /// read, or the background sweep.</summary>
    private readonly Dictionary<ComponentCandidate, string> _declaredNames = [];

    private bool _suppressSelectionEvent;

    /// <summary>Which format families are showing. Built from what the scan HOLDS.</summary>
    private readonly Dictionary<ComponentFormatFamily, bool> _families = [];

    /// <summary>And which degrees of completeness — "does this row have a symbol" is the other half
    /// of what a user is choosing between, and the one the drawings make vivid.</summary>
    private readonly Dictionary<ComponentCompleteness, bool> _completeness = [];

    private bool _filterApplyPending;

    /// <summary>
    /// One checkbox per category the scan actually found, each with its count.
    ///
    /// <para>Not one per category circuitRF can read: a checkbox for a format this folder does not
    /// hold filters nothing, and a list of ten toggles where two of them do anything is a worse
    /// control than a list of two. The counts are what make the flyout worth opening at all — they
    /// say what the folder is made of before anything has been narrowed.</para>
    /// </summary>
    private void BuildFilterPanel()
    {
        FilterPanel.Children.Clear();

        var families = _all.GroupBy(c => c.Family).OrderBy(g => g.Key).ToList();
        var kinds = _all.GroupBy(c => c.Completeness).OrderBy(g => g.Key).ToList();

        // A group with one member cannot narrow anything — every row is in it — so it is not offered.
        // Both groups collapsing means there is nothing to filter by, and the button says so rather
        // than opening an empty flyout.
        bool any = false;
        if (families.Count > 1)
        {
            AddHeader("Format");
            foreach (var g in families)
            {
                _families[g.Key] = true;
                AddToggle($"{FamilyLabel(g.Key)}  ({g.Count()})", v => _families[g.Key] = v);
            }
            any = true;
        }
        else if (families.Count == 1) _families[families[0].Key] = true;

        if (kinds.Count > 1)
        {
            if (any) AddHeader("Contents");
            foreach (var g in kinds)
            {
                _completeness[g.Key] = true;
                AddToggle($"{CompletenessLabel(g.Key)}  ({g.Count()})", v => _completeness[g.Key] = v);
            }
            any = true;
        }
        else if (kinds.Count == 1) _completeness[kinds[0].Key] = true;

        FilterButton.IsEnabled = any;
        if (!any)
            FilterButton.SetValue(ToolTip.TipProperty, "Every candidate in this folder is the same type.");

        void AddHeader(string text)
        {
            if (FilterPanel.Children.Count > 0)
                FilterPanel.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 2) });
            FilterPanel.Children.Add(new TextBlock
            {
                Text = text, FontSize = 11, FontWeight = Avalonia.Media.FontWeight.SemiBold, Opacity = 0.7,
            });
        }

        void AddToggle(string text, Action<bool> set)
        {
            var box = new CheckBox { Content = text, FontSize = 12, IsChecked = true };
            box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); ApplyFilter(); };
            FilterPanel.Children.Add(box);
        }
    }

    /// <summary>The format names as a USER can act on them: what the files are called, never a
    /// vendor's name for them.</summary>
    private static string FamilyLabel(ComponentFormatFamily family) => family switch
    {
        ComponentFormatFamily.Pl1     => "Symbol / footprint files",
        ComponentFormatFamily.Records => "Record triples (.p / .d / .c)",
        ComponentFormatFamily.Hkp     => "Keyword sets (.hkp)",
        ComponentFormatFamily.Plx     => "Library dialect (.PLX / .DSL)",
        ComponentFormatFamily.Cxf     => "Flat library (.cxf)",
        ComponentFormatFamily.Script  => "Command scripts (.scr)",
        _                             => family.ToString(),
    };

    private static string CompletenessLabel(ComponentCompleteness completeness) => completeness switch
    {
        ComponentCompleteness.SymbolFootprintAndMap => "Symbol and footprint",
        ComponentCompleteness.FootprintOnly         => "Footprint only",
        ComponentCompleteness.SymbolOnly            => "Symbol only",
        _                                           => completeness.ToString(),
    };

    private void OnToggleSearchClick(object? sender, RoutedEventArgs e)
    {
        ShowSearchField(!_searchOpen);

        if (_searchOpen)
        {
            // Posted: the box is only just becoming visible, and a control that has not been laid out
            // yet cannot take focus.
            Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Input);
            ApplyFilter();          // the count and the field share a cell; one hides the other
            return;
        }

        // Closing CLEARS, always — same reason as the Project Tree: a query still applied with the
        // field put away hides rows with nothing on screen to explain it.
        SearchBox.Text = "";
        ApplyFilter();
    }

    /// <summary>Whether the search field is up. Its own flag rather than the host's
    /// <c>IsVisible</c>, because the host is never taken out of the layout — see the XAML.</summary>
    private bool _searchOpen;

    /// <summary>
    /// Raises or hides the search field WITHOUT changing what the row measures.
    ///
    /// <para>The field and the folder name share one cell, and the field is the taller of the two, so
    /// the row's height is the field's whether or not it is up. Hiding it with <c>IsVisible</c> takes
    /// it out of the measure and the row shrinks onto the label — which slides the list under it up,
    /// and back down again the moment the field returns.</para>
    /// </summary>
    private void ShowSearchField(bool open)
    {
        _searchOpen = open;
        SearchFieldHost.Opacity = open ? 1 : 0;
        SearchFieldHost.IsHitTestVisible = open;
        SearchBox.IsTabStop = open;                 // opacity alone still takes Tab
        ClearSearchButton.IsTabStop = open;
        ScanRootText.IsVisible = !open;
    }

    private void OnClearSearchClick(object? sender, RoutedEventArgs e) => CloseSearch();

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CloseSearch();
        e.Handled = true;
    }

    /// <summary>Puts the field away and CLEARS it, uncovering the folder name. Both halves together:
    /// a query still applied with the field gone hides rows with nothing on screen to explain it.
    /// </summary>
    private void CloseSearch()
    {
        SearchBox.Text = "";
        ShowSearchField(false);
        ApplyFilter();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        ClearSearchButton.IsVisible = !string.IsNullOrEmpty(SearchBox.Text);

        // Coalesced: a burst of keystrokes costs one filter pass against the LATEST text, not one per
        // character. Reading the text inside the callback rather than here is what makes that true.
        if (_filterApplyPending) return;
        _filterApplyPending = true;
        Dispatcher.UIThread.Post(() => { _filterApplyPending = false; ApplyFilter(); },
                                 DispatcherPriority.Background);
    }

    /// <summary>
    /// Rebuilds the list from the search text and the type toggles, <b>keeping the selected candidate
    /// selected</b> when it survives the filter — otherwise typing one more character of a search
    /// that still matches the chosen row would throw the preview away and rebuild it.
    /// </summary>
    private void ApplyFilter()
    {
        string term = (SearchBox.Text ?? "").Trim();
        var selected = CandidateList.SelectedItem as CandidateRow;

        var shown = _rows.Where(r => Matches(r.Candidate)).ToList();

        // ── Why the selection event is suppressed across the swap ────────────────────────────────
        //
        // Assigning ItemsSource makes the ListBox drop its selection and then take the restored one,
        // so a filter pass that changes NOTHING still raises two SelectionChanged events — a null one
        // and a real one. The null one blanks both preview panes on the spot, and the pane is only
        // dark for as long as the re-selected candidate takes to come back out of the model cache:
        // a flash, on every keystroke of a search and on merely OPENING the search field, which
        // re-filters because the field and the count share a cell (owner, 2026-09-05).
        //
        // So the swap is silent and the preview is scheduled once, afterwards, and only if the
        // selected row actually changed.
        _suppressSelectionEvent = true;
        try
        {
            CandidateList.ItemsSource = shown;
            if (selected is not null && shown.Contains(selected)) CandidateList.SelectedItem = selected;
            else if (shown.Count > 0) CandidateList.SelectedIndex = 0;
            else CandidateList.SelectedItem = null;
        }
        finally { _suppressSelectionEvent = false; }

        if (!ReferenceEquals(CandidateList.SelectedItem, selected))
        {
            _shownModel = null;                  // a new candidate; the variant list belongs to the old one
            SchedulePreview(SelectionDebounceMs);
        }

        FilterCountText.Text = $"{shown.Count:N0} of {_all.Count:N0}";
        FilterCountText.IsVisible = shown.Count != _all.Count && !_searchOpen;

        // Nothing selectable is nothing to import. Leaving the default button live over an empty list
        // makes Return do nothing, silently, which reads as the dialog having hung.
        ImportButton.IsEnabled = shown.Count > 0;

        bool Matches(ComponentCandidate c)
        {
            if (_families.TryGetValue(c.Family, out bool onFamily) && !onFamily) return false;
            if (_completeness.TryGetValue(c.Completeness, out bool onKind) && !onKind) return false;
            if (term.Length == 0) return true;

            // The name and the folder both, because both are in the row: a search that matched only
            // the name could not find "the one under partB", which is the field the row carries
            // precisely because names repeat. The DECLARED name too, once a read has produced it —
            // it is on screen, and a term that is visibly there must find its row.
            return c.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || c.Location.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (_declaredNames.TryGetValue(c, out string? declared)
                    && declared.Contains(term, StringComparison.OrdinalIgnoreCase));
        }
    }

    // ── The preview ─────────────────────────────────────────────────────────────────────────────

    private readonly Technology? _destTech;
    private readonly int _dbuPerMicron = LayoutUnits.DefaultDbuPerMicron;

    /// <summary>Bumped by anything that invalidates a preview in flight. A pass whose number no longer
    /// matches drops its result instead of writing it to a control — which is what keeps a slow
    /// candidate's drawing from landing over a fast one the user has since selected.</summary>
    private int _generation;

    /// <summary>
    /// Built models, by candidate. The READ is the expensive half — a whole component library, parsed
    /// — and it does not change when the pane is resized or a density variant is picked, so arrowing
    /// back up a list is instant after the first pass down it.
    /// </summary>
    private readonly Dictionary<ComponentCandidate, ComponentPreview.Model> _models = [];

    private ComponentPreview.Model? _shownModel;
    private bool _suppressVariantEvent;

    private void OnCandidateSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvent) return;     // a filter swap; ApplyFilter schedules the one pass
        _shownModel = null;      // a new candidate; the variant list belongs to the old one
        SchedulePreview(SelectionDebounceMs);
    }

    private void OnVariantSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressVariantEvent) return;
        SchedulePreview(SelectionDebounceMs);
    }

    /// <summary>Coalesces a burst of arrow-key presses down the list without being felt as lag.</summary>
    private const int SelectionDebounceMs = 50;

    /// <summary>A resize drag raises a great many size changes, and each one is a whole re-raster.</summary>
    private const int ResizeDebounceMs = 150;

    /// <summary>
    /// Samples per logical pixel the preview is rastered at.
    ///
    /// <para><b>Why this is a FLOOR and not simply <c>RenderScaling</c>.</b> The panes are rendered at
    /// their logical size times this number, so the bitmap carries at least one sample per physical
    /// device pixel and <see cref="Controls.PreviewImage"/> never has to invent any. Reading the scaling and
    /// trusting it outright is what the first attempt did, and it came back blurry on a Retina display
    /// (owner, 2026-09-05) — a window reports 1.0 until its platform impl is attached, and this runs
    /// off a layout pass, which is early. A floor of 2 costs a downsample on a 1x display, which is
    /// sharp, and removes the failure mode entirely on the displays that have it. A 3x display reports
    /// its scaling correctly by the time anything is drawn and gets all three samples.</para>
    /// </summary>
    private static double Supersample(double renderScaling)
        => Math.Clamp(renderScaling > 0 ? Math.Max(renderScaling, 2.0) : 2.0, 2.0, 4.0);

    /// <summary>How long a read may take before the panes are blanked for it. Under this, the old
    /// drawing simply stays up until the new one replaces it in one assignment.</summary>
    private const int ReadPatienceMs = 150;

    /// <summary>
    /// Produces the preview for whatever is selected now, off the UI thread, and applies it only if it
    /// is still what is selected when it finishes.
    /// </summary>
    private async void SchedulePreview(int debounceMs)
    {
        int gen = ++_generation;

        if (CandidateList.SelectedItem is not CandidateRow row)
        {
            PreviewNameText.Text = "";
            PreviewCompositionText.Text = "";
            SetSummary("");
            Say(_all.Count > 0 ? "No component matches the search or filter." : "", "");
            return;
        }

        var candidate = row.Candidate;

        // The header comes off the ROW, so it is right BEFORE anything is read — the file's own name
        // to begin with, and the declared name beside it the moment either this read or the background
        // sweep has one. It is also what makes the row text short: the completeness and the format
        // list are the same few phrases the whole way down a list, and they belong beside the one row
        // being looked at rather than in every row (owner, 2026-09-05).
        PreviewNameText.Text = row.Title;
        PreviewCompositionText.Text = candidate.HasLocation
            ? $"{candidate.Composition}  ·  in {candidate.Location}"
            : candidate.Composition;
        ToolTip.SetTip(PreviewCompositionText, PreviewCompositionText.Text);

        if (debounceMs > 0)
        {
            await Task.Delay(debounceMs);
            if (gen != _generation) return;
        }

        // A pane with no size yet cannot be drawn into, and this runs once before the first layout
        // pass (the constructor selects row 0 while every Bounds is still zero). Returning leaves the
        // panes exactly as they are; the SizeChanged that follows layout runs the real pass.
        if (SymbolPane.Bounds.Width < 1 || FootprintPane.Bounds.Width < 1) return;

        // Sizes and themes are read HERE, on the UI thread, and passed by value: a worker thread may
        // not touch a control's bounds or the theme service.
        double scaling = Supersample(TopLevel.GetTopLevel(this)?.RenderScaling ?? RenderScaling);
        int symbolW = (int)Math.Round(SymbolPane.Bounds.Width * scaling);
        int symbolH = (int)Math.Round(SymbolPane.Bounds.Height * scaling);
        int footW = (int)Math.Round(FootprintPane.Bounds.Width * scaling);
        int footH = (int)Math.Round(FootprintPane.Bounds.Height * scaling);
        var variant = ActualThemeVariant == ThemeVariant.Dark ? ColorVariant.Dark : ColorVariant.Light;
        var symbolTheme = SchematicRenderTheme.FromTheme(ThemeService.Active, variant);
        var layoutTheme = LayoutRenderTheme.FromTheme(ThemeService.Active, variant);

        var model = _models.TryGetValue(candidate, out var cached) ? cached : null;
        if (model is null)
        {
            var tech = _destTech;
            int dbu = _dbuPerMicron;
            var reading = Task.Run(() =>
            {
                // A reader fault must degrade to a sentence in the pane, never take down the dialog:
                // this runs over files circuitRF did not write, chosen by pointing at a folder.
                try { return ComponentPreview.Build(candidate, tech, dbu); }
                catch (Exception ex) { return ComponentPreview.Model.Refused(ex.Message); }
            });

            // The panes are blanked ONLY for a read slow enough to be noticed (owner, 2026-09-05: the
            // previews flashed while clicking through the list). Announcing every read costs a blank
            // frame on every click, and most reads finish inside one — so the previous drawing stays
            // up and is replaced in a single assignment. It is left up only for this long: a stale
            // drawing under a new selection is worse than an empty pane once it lasts long enough to
            // be read as an answer.
            if (await Task.WhenAny(reading, Task.Delay(ReadPatienceMs)) != reading)
            {
                if (gen != _generation) return;
                Say("Reading…", "Reading…");
                SetSummary("");
            }

            model = await reading;
            if (gen != _generation) return;
            _models[candidate] = model;
        }

        // Whichever read got here first fills the row in, so a candidate the user clicks before the
        // sweep reaches it is named immediately rather than a moment later.
        NameRow(row, model.Name);
        PreviewNameText.Text = row.Title;

        if (model.Refusal is { } refusal)
        {
            _shownModel = null;
            VariantCombo.IsVisible = false;
            Say(refusal, refusal);
            SetSummary("");
            return;
        }

        int index = SyncVariants(model);
        var view = index >= 0 && index < model.Layouts.Count ? model.Layouts[index].View : null;
        var symbol = model.Symbol;
        var previewTech = model.Technology;

        var (symbolBitmap, footprintBitmap) = await Task.Run(() =>
        {
            try
            {
                return (ComponentPreviewRenderer.RenderSymbol(symbol, symbolTheme, symbolW, symbolH, scaling),
                        ComponentPreviewRenderer.RenderFootprint(view, previewTech, layoutTheme, footW, footH, scaling));
            }
            catch (Exception) { return (null, null); }
        });

        if (gen != _generation)
        {
            symbolBitmap?.Dispose();
            footprintBitmap?.Dispose();
            return;
        }

        SetSymbolImage(symbolBitmap);
        SetFootprintImage(footprintBitmap);
        SymbolPlaceholder.Text = symbolBitmap is null ? "This candidate has no symbol." : "";
        FootprintPlaceholder.Text = footprintBitmap is null ? "This candidate has no footprint." : "";
        // The declared name used to be said here, as "Declared as …". It is in the header and in the
        // row now, so repeating it under the drawings is one more line to read saying nothing new.
        SetSummary(model.Summary);
    }

    // ── The declared names ──────────────────────────────────────────────────────────────────────

    /// <summary>What separates the declared name from the file's in a row and in the header.</summary>
    internal const string NameSeparator = "•";

    /// <summary>Stops the sweep when the dialog closes. A scan can rank hundreds of candidates, and
    /// every one of them is a library to parse — none of that may outlive the window that wanted it.
    /// </summary>
    private readonly CancellationTokenSource _sweep = new();

    /// <summary>Records a read's declared name on its row, and on nothing else. Empty names and names
    /// equal to the file's are stored too, so the sweep never re-reads a candidate it has settled.
    /// </summary>
    private void NameRow(CandidateRow row, string declared)
    {
        _declaredNames[row.Candidate] = declared;
        row.DeclaredName = declared;
    }

    /// <summary>
    /// Reads every candidate, in list order, on one background worker, and names its row as each
    /// finishes.
    ///
    /// <para><b>Why this is a sweep and not part of the scan.</b> The scan classifies files by their
    /// first bytes and deliberately parses none of them (R-PL1-28) — reading a whole library per row
    /// before the user has chosen anything is the import run over again, several dozen times, on the
    /// UI thread. So the list opens named after its files, exactly as it did, and the declared names
    /// arrive as they are read. A row that never gets one keeps the file's name, which is the answer
    /// the scan itself falls back to.</para>
    ///
    /// <para>One worker, in order, deliberately: the rows are read top-down, which is the order the
    /// user is looking at them in, and a parallel sweep would spend the machine on rows nobody has
    /// scrolled to yet.</para>
    /// </summary>
    private async void StartNameSweep()
    {
        var token = _sweep.Token;
        int dbu = _dbuPerMicron;

        foreach (var row in _rows)
        {
            if (token.IsCancellationRequested) return;
            if (_declaredNames.ContainsKey(row.Candidate)) continue;

            var candidate = row.Candidate;
            string name;
            try
            {
                name = await Task.Run(() =>
                {
                    // A reader fault must cost this row its declared name and nothing else: this runs
                    // over files circuitRF did not write, chosen by pointing at a folder.
                    try
                    {
                        var read = ComponentRead.Read(candidate, dbu);
                        return read.Part?.Name ?? "";
                    }
                    catch (Exception) { return ""; }
                }, token);
            }
            catch (OperationCanceledException) { return; }

            if (token.IsCancellationRequested) return;

            // The preview may have named it while this read was in flight; its model is the same read,
            // so either answer is the same one, and the first to arrive wins.
            if (!_declaredNames.ContainsKey(candidate)) NameRow(row, name);
            if (ReferenceEquals(CandidateList.SelectedItem, row)) PreviewNameText.Text = row.Title;

            // A row whose new name matches a search already typed has to join the results now, not at
            // the next keystroke. Only when it actually matches: re-filtering per row would rebuild
            // the list once per candidate to change nothing.
            string term = (SearchBox.Text ?? "").Trim();
            if (term.Length > 0 && name.Contains(term, StringComparison.OrdinalIgnoreCase)) ApplyFilter();
        }
    }

    /// <summary>
    /// Fills the variant chooser for <paramref name="model"/> and returns which land pattern to draw.
    ///
    /// <para>The combo's own event is suppressed while its items are replaced: a selection assigned
    /// before its items are attached is silently dropped by Avalonia, and one raised while they are
    /// being replaced would re-enter this whole pass (see <c>src/Ui/CLAUDE.md</c> on ComboBox
    /// notification order).</para>
    /// </summary>
    private int SyncVariants(ComponentPreview.Model model)
    {
        if (ReferenceEquals(_shownModel, model))
            return VariantCombo.IsVisible ? Math.Max(VariantCombo.SelectedIndex, 0) : 0;

        _suppressVariantEvent = true;
        try
        {
            _shownModel = model;
            VariantCombo.IsVisible = model.Layouts.Count > 1;
            VariantCombo.ItemsSource = model.Layouts.Count > 1
                ? model.Layouts.Select(l => l.Variant.Length == 0 ? "Nominal" : l.Variant.TrimStart('-', '_')).ToList()
                : null;
            VariantCombo.SelectedIndex = model.Layouts.Count > 1 ? 0 : -1;
        }
        finally { _suppressVariantEvent = false; }

        return 0;
    }

    /// <summary>The summary line, whose box is a FIXED height so the drawings above it are the same
    /// size for every candidate. Anything past that height is trimmed on screen and kept in full on
    /// the tooltip — a part with several unimported sections has a long summary, and shrinking the
    /// pictures to fit it defeats the pane.</summary>
    private void SetSummary(string text)
    {
        PreviewSummaryText.Text = text;
        ToolTip.SetTip(PreviewSummaryText, text.Length > 0 ? text : null);
    }

    /// <summary>Both placeholders at once — the state where there is nothing to draw and a reason.</summary>
    private void Say(string symbolText, string footprintText)
    {
        SetSymbolImage(null);
        SetFootprintImage(null);
        SymbolPlaceholder.Text = symbolText;
        FootprintPlaceholder.Text = footprintText;
    }

    /// <summary>Assigns the new bitmap FIRST, then disposes the one it replaced: a
    /// <see cref="Bitmap"/> disposed while an <see cref="Image"/> still points at it is a crash in
    /// the compositor, not a blank pane.</summary>
    private void SetSymbolImage(Bitmap? bitmap)
    {
        var old = SymbolImage.Source;
        SymbolImage.Source = bitmap;
        if (!ReferenceEquals(old, bitmap)) old?.Dispose();
    }

    private void SetFootprintImage(Bitmap? bitmap)
    {
        var old = FootprintImage.Source;
        FootprintImage.Source = bitmap;
        if (!ReferenceEquals(old, bitmap)) old?.Dispose();
    }
}

/// <summary>
/// One row of the chooser's list.
///
/// <para><b>Why a row exists at all, rather than binding the candidate directly.</b> A scan
/// CLASSIFIES files and does not parse them (R-PL1-28), so <see cref="ComponentCandidate.DisplayName"/>
/// is the base name of the file the candidate begins at — and for a format written as a fixed triple
/// of files, every part in every library is called the same thing. The name the part DECLARES inside
/// itself is the one the user is looking for, and it can only be had by reading. So the row carries
/// the file's name from the start and takes the declared one when a read produces it, which is what
/// makes this an observable object and not a record.</para>
/// </summary>
public sealed class CandidateRow(ComponentCandidate candidate) : INotifyPropertyChanged
{
    public ComponentCandidate Candidate { get; } = candidate;

    public string Location => Candidate.Location;

    public bool HasLocation => Candidate.HasLocation;

    private string _declaredName = "";

    /// <summary>The name the part states inside itself, once something has read it — empty until
    /// then, which is the ordinary state of a row nobody has looked at yet.</summary>
    public string DeclaredName
    {
        get => _declaredName;
        set
        {
            if (_declaredName == value) return;
            _declaredName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeclaredName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    }

    /// <summary>
    /// What the row says: the declared name and the file's name together, because both matter and
    /// neither replaces the other. The declared name identifies the PART; the file's name is what
    /// distinguishes one row from the several others in the same folder that hold the same part in a
    /// different format — which is the whole reason there is a list to choose from.
    /// </summary>
    public string Title => DeclaredName.Length > 0 && DeclaredName != Candidate.DisplayName
        ? $"{DeclaredName} {ComponentImportChooserDialog.NameSeparator} {Candidate.DisplayName}"
        : Candidate.DisplayName;

    public event PropertyChangedEventHandler? PropertyChanged;
}

