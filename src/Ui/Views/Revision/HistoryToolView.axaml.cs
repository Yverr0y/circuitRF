using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CircuitRF.Ui.ViewModels.Dock;

namespace CircuitRF.Ui.Views.Revision;

/// <summary>
/// RC-10's one history panel (<c>docs/design/revision-control.md</c> §5.10). <b>Replaces the Restore
/// Points and Versions views</b>, which were two windows spent on an argument about two kinds of
/// entry.
///
/// <para>Click handlers only, in the shape both retired views used: the panel's own view model owns
/// every decision, and the host wires up what happens.</para>
/// </summary>
public partial class HistoryToolView : UserControl
{
    public HistoryToolView()
    {
        InitializeComponent();

        // Escape closes the search field (which clears it, so the whole list comes back).
        //
        // handledEventsToo: true is REQUIRED, not defensive. WorkspaceWindow.axaml binds Escape to
        // DisarmPlacementCommand, and Window.KeyBindings are processed before visual-tree routing and
        // always mark the event Handled — so a handler that skips handled events never sees Escape at
        // all. SchematicView, SymbolEditorView, LayoutEditorView and ProjectTreeView all carry the
        // identical argument on their own Escape handlers; this panel is the fifth to need it.
        AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);

        // …and the panel asking for the keyboard back, which is what a dismissed dialog does.
        DataContextChanged += OnDataContextChangedForActivation;
    }

    // ── Activation focus ──────────────────────────────────────────────────────

    private HistoryTool? _activationTool;

    private void OnDataContextChangedForActivation(object? sender, System.EventArgs e)
    {
        if (_activationTool is not null) _activationTool.ActivationFocusRequested -= OnActivationFocusRequested;
        _activationTool = Tool;
        if (_activationTool is null) return;

        _activationTool.ActivationFocusRequested += OnActivationFocusRequested;

        // The panel can be asked BEFORE this view exists — a restored dock arrangement, or a workspace
        // switch that rebuilt it. The request is held until a view turns up to consume it, exactly as
        // ProjectTreeView does.
        if (_activationTool.ConsumeActivationFocus()) OnActivationFocusRequested();
    }

    /// <summary>
    /// Puts keyboard focus in the list. Posted, because the ask arrives as a dialog is closing and the
    /// window has not finished handing focus back yet — grabbing before that lands means grabbing
    /// something the window then takes away again.
    /// </summary>
    private void OnActivationFocusRequested() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => HistoryRows.Focus(), Avalonia.Threading.DispatcherPriority.Input);

    private HistoryTool? Tool => DataContext as HistoryTool;

    private void OnSavePointClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.SavePoint();

    private void OnKeepVersionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.KeepVersion();

    private void OnGoBackClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.GoBack();

    private void OnKeepClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.Keep();

    private void OnBringBackClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.BringBack();

    private void OnCopyIdentityClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.CopyIdentity();

    private void OnCompareClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.CompareWithWorkspace();

    private void OnComeForwardClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.ComeForward();

    // RC-11 §5.11. The three corrections, on the menu R-rc10-17 built for them.

    private void OnRenameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.Rename();

    private void OnCorrectClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.Correct();

    private void OnLetGoClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.LetGo();

    private void OnToggleSearchClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Tool is not { } tool) return;

        tool.ToggleSearch();

        // The field is created by the visibility binding, so focus is taken after it exists. Opening a
        // search box the designer then has to click into is a control that asks for two gestures where
        // one was meant.
        if (tool.IsSearchOpen) Avalonia.Threading.Dispatcher.UIThread.Post(() => SearchBox.Focus());
        else GiveFocusBackToRows();
    }

    private void OnClearSearchClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.ClearSearch();

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (Tool is not { IsSearchOpen: true } tool) return;

        tool.IsSearchOpen = false;   // clears the query on its way (see OnIsSearchOpenChanged)
        GiveFocusBackToRows();
        e.Handled = true;
    }

    /// <summary>
    /// Closing the field leaves the caret inside a control that is no longer on screen, which swallows
    /// every subsequent keystroke. The list is where the keys are meant to land once the search is put
    /// away.
    /// </summary>
    private void GiveFocusBackToRows() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => HistoryRows.Focus(), Avalonia.Threading.DispatcherPriority.Input);
}
