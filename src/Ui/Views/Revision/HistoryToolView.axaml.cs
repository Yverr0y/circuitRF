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
    ///
    /// <para><b>The panel's own WINDOW is activated first, and that is not the same operation</b>
    /// (owner, 2026-09-08: after Keep This Version the workspace window came forward, not the floated
    /// History window the button had been pressed in).</para>
    ///
    /// <para>Both keep dialogs are shown over the WORKSPACE window on purpose — they are modal to the
    /// shell, and owning them by a float would leave the shell clickable underneath. So the platform
    /// correctly hands activation back to the shell when the dialog closes, and a control focused in
    /// a window the operating system has just deactivated receives no keystrokes and shows no
    /// highlight: the ask was being honoured into a window nobody was looking at. Docked, this is a
    /// no-op, because the panel's top level IS the window already coming forward.</para>
    /// </summary>
    private void OnActivationFocusRequested() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                if (Avalonia.Controls.TopLevel.GetTopLevel(this) is Avalonia.Controls.Window window)
                    window.Activate();

                HistoryRows.Focus();
            },
            Avalonia.Threading.DispatcherPriority.Input);

    private HistoryTool? Tool => DataContext as HistoryTool;

    private void OnSavePointClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.SavePoint();

    private void OnKeepVersionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.KeepVersion();

    private void OnGoBackClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => AfterTheClickHasPainted(tool => tool.GoBack());

    private void OnKeepClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.Keep();

    private void OnBringBackClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.BringBack();

    private void OnCopyIdentityClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.CopyIdentity();

    private void OnCompareClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Tool?.CompareWithWorkspace();

    private void OnComeForwardClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => AfterTheClickHasPainted(tool => tool.ComeForward());

    /// <summary>
    /// <b>Runs an action only once the click that asked for it is off the screen.</b>
    ///
    /// <para>Owner report, 2026-09-08: the context menu stayed on screen while going back to a state.
    /// Avalonia does close a <see cref="ContextMenu"/> when one of its items is clicked — but the close
    /// is a layout pass, and the handler runs FIRST and inline. Every other item on that menu is
    /// instant, so the close painted at once and the behaviour looked right; going back is the one
    /// whose action is seconds long, so the close could not reach the screen until it was over and the
    /// menu sat on top of the work it had started.</para>
    ///
    /// <para><b>Both halves are needed.</b> Closing the menu explicitly is what makes it go at the
    /// moment of the click; posting the action at
    /// <see cref="Avalonia.Threading.DispatcherPriority.Background"/> is what lets the frame WITHOUT it
    /// reach the screen before the UI thread is busy again. Either one alone leaves the menu visible
    /// for the duration.</para>
    ///
    /// <para>The row menu is closed by NAME rather than by walking up from the sender, because these
    /// two actions are on a BUTTON as well as on the menu — and closing a menu that is not open is
    /// nothing, while a parent walk that finds nothing is a fix that silently did not apply.</para>
    ///
    /// <para>Only the two actions that start a restore come through here. An instant one has nothing
    /// to defer and would only gain a dispatcher turn.</para>
    /// </summary>
    private void AfterTheClickHasPainted(System.Action<HistoryTool> action)
    {
        if (Tool is not { } tool) return;

        HistoryRows.ContextMenu?.Close();

        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => action(tool), Avalonia.Threading.DispatcherPriority.Background);
    }

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
