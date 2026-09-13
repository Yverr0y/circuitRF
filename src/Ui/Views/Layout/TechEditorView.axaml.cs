using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CircuitRF.Ui.Controls;
using CircuitRF.Ui.Layout;

namespace CircuitRF.Ui.Views.Layout;

/// <summary>
/// Code-behind for the .ctech editor. Every editable cell across the three sections (layer
/// table, stackup, DRC rules) commits through one of two generic dispatchers keyed by the
/// control's <see cref="Control.Tag"/> and its DataContext's row-VM type — avoids one handler
/// method per field across three different row VMs.
/// </summary>
public partial class TechEditorView : UserControl
{
    public TechEditorView()
    {
        InitializeComponent();

        // TUNNELLING, not bubbling: a ListBox handles Page Up/Down and Home/End itself (moving the
        // selection), and these lists are flattened so that selection is invisible — the keystroke
        // would appear to do nothing while quietly changing what is selected. Getting there first is
        // what makes the key scroll the pane instead.
        AddHandler(KeyDownEvent, OnScrollKeyDown, RoutingStrategies.Tunnel);

        // R-stk3-9 — Esc clears the stackup selection.
        //
        // TUNNELLING FROM THE VIEW, and not a key handler on the drawing, which is what brief 3
        // sketched. The canvas is deliberately NOT focusable (R-stk2-10: a focusable control inside
        // the drawing's ScrollViewer re-points Page Up/Down at the drawing, because TargetScrollViewer
        // below walks up from whatever holds focus) — and the owner's ask is "pressing Esc will
        // unselect", not "pressing Esc while the drawing happens to have focus", so the handler had to
        // cover the card list as well either way. One handler covers both surfaces.
        AddHandler(KeyDownEvent, OnEscapeKeyDown, RoutingStrategies.Tunnel);

        // The scroll handler above is TUNNELLING FROM THIS CONTROL, so it only ever sees a keystroke
        // that is already routing through this view — which means something inside the view has to
        // hold focus for Page Up/Down to work at all. On first open nothing does: the tab is
        // activated before the view is bound, so focus is still wherever it was and the keystroke
        // routes somewhere else entirely. The three other document views already take focus on
        // activation through this same hook; this one never subscribed, which is the whole bug.
        DataContextChanged += OnDataContextChanged;

        // R-stk4-2. The drawing and the box are siblings in one Panel; this is what joins them. The
        // canvas owns the half that needs the scene (which label was hit, what it seeds from, where
        // exactly it goes) and the box stays the host's, because the three-key contract below is
        // wired to it here.
        StackupInlineEditor = new StackupInlineEditor(StackupInlineEdit);
        StackupDrawing.InlineEditor = StackupInlineEditor;

        // Focus needs a visual root, so the editor raises this rather than taking focus itself.
        // Posted at Input priority for the same reason SchematicView posts its own: the box has just
        // been made visible and is not yet realised, and Focus() on an unrealised control does
        // nothing at all.
        StackupInlineEditor.Opened += () =>
            Dispatcher.UIThread.Post(() => StackupInlineEdit.Focus(), DispatcherPriority.Input);

        // The destination follows the visible tab (TechEditorViewModel.HelpDestinationFor) — this
        // window edits four unrelated things and no one chapter covers all of them.
        HelpButton.Click += (_, _) =>
        {
            var (page, anchor) = (DataContext as TechDocument)?.ViewModel.HelpDestination
                                 ?? TechEditorViewModel.HelpDestinationFor(0);
            DocLauncher.Open(page, anchor.Length == 0 ? null : anchor);
        };
    }

    private TechDocument? _subscribedDoc;
    private TechEditorViewModel? _subscribedVm;

    /// <summary>The one inline editor over the cross-section (brief 4). Internal for the gate, which
    /// has no application host to raise a real double-click in.</summary>
    internal StackupInlineEditor StackupInlineEditor { get; }

    // ── The three-key contract (R-stk4-6) ─────────────────────────────────────────────────────────
    //
    // The host's to wire, and stated here for both surfaces that host this box: SchematicInlineEditBox
    // raises nothing and handles no key itself, which is exactly what lets two hosts adopt it without
    // either changing behaviour.
    //
    //   Return    commits, and marks the key handled.
    //   LostFocus commits. This is the one that costs the user an edit if it is missed.
    //   Escape    reverts — closes the box, writes nothing.

    private void OnStackupInlineEditKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Return or Key.Enter:
                StackupInlineEditor.Commit();
                e.Handled = true;
                break;
            case Key.Escape:
                StackupInlineEditor.Revert();
                e.Handled = true;
                break;
        }
    }

    private void OnStackupInlineEditLostFocus(object? sender, RoutedEventArgs e)
        => StackupInlineEditor.Commit();

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_subscribedDoc is not null) _subscribedDoc.ActivationFocusRequested -= OnActivationFocusRequested;
        if (_subscribedVm is not null) _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribedVm = null;

        _subscribedDoc = DataContext as TechDocument;
        if (_subscribedDoc is null) return;

        _subscribedVm = _subscribedDoc.ViewModel;
        _subscribedVm.PropertyChanged += OnViewModelPropertyChanged;

        _subscribedDoc.ActivationFocusRequested += OnActivationFocusRequested;
        // Activated BEFORE the view bound — the first-open case — so the request is sitting pending.
        if (_subscribedDoc.ConsumeActivationFocus()) FocusForScrollingDeferred();
    }

    // ── The stackup selection scrolls the card list to its card (R-stk3-5) ─────

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TechEditorViewModel.SelectedStackupLayerName))
            ScrollStackupSelectionIntoView();
    }

    /// <summary>
    /// Brings the selected entry's card into view, so a click on a band lands on its fields.
    ///
    /// <para><b>Posted at Background priority, and not run inline.</b> The view model may have just
    /// cleared the stackup filter (R-stk3-5 — a selection the filter would hide clears it, because a
    /// click that appears to do nothing is worse than a filter the user has to re-type), which
    /// rebuilds <c>FilteredStackupLayers</c> SYNCHRONOUSLY but leaves the <c>ListBox</c> to arrange
    /// later. <c>ScrollIntoView</c> against containers that have not been re-materialised scrolls to
    /// the wrong place or to nothing. This is the same deferral
    /// <see cref="FocusForScrollingDeferred"/> already makes in this file for the same reason — and
    /// deliberately not a timer.</para>
    /// </summary>
    private void ScrollStackupSelectionIntoView()
    {
        var row = _subscribedVm?.SelectedStackupLayerRow;
        if (row is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            // Re-read rather than closing over `row`: at Background priority an edit, an undo or a
            // second click may have landed in between, and every one of those replaces the row VMs.
            if (_subscribedVm?.SelectedStackupLayerRow is { } current)
                StackupList?.ScrollIntoView(current);
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// R-stk3-9. Clears the selection — which clears the drawing's outline, the card's shading and
    /// the <c>ListBox</c>'s own selection together, because all three read the one property.
    ///
    /// <para>It does NOT put back a filter the selection cleared, and it scrolls nowhere. Undoing the
    /// filter clear on <c>Esc</c> would make <c>Esc</c> a second undo, which it is not.</para>
    ///
    /// <para><b>Precedence, for brief 4 (R-stk4-6):</b> while an inline editor is open, <c>Esc</c>
    /// reverts the edit and the selection stands; a second <c>Esc</c>, with no editor open, clears the
    /// selection. This handler tunnels, so it gets there first — brief 4's gate is therefore a check
    /// HERE for "is an editor open", returning without handling so the editor's own handler takes it.
    /// Nothing opens an inline editor yet, so there is nothing to check for.</para>
    /// </summary>
    private void OnEscapeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        // R-stk4-6's two jobs for one key. This handler TUNNELS, so it gets there before the open
        // box's own KeyDown — and the first Esc belongs to the box: it reverts the edit and the
        // selection stands. Returning without handling is what lets the keystroke carry on down to
        // it. The second Esc, with the box shut, falls through to the line below and clears the
        // selection.
        if (StackupDrawing?.InlineEditIsOpen == true) return;

        // R-stk5-3's third job for this key, between the two above it. A live drag outranks the
        // clear-selection below — Esc mid-drag means "forget this gesture", and clearing the
        // selection as well would throw away the entry the user is still looking at. It cannot
        // collide with the box: a drag and an open editor cannot both be live, because opening the
        // box rebuilds nothing and starting a drag needs a press the box has swallowed.
        if (StackupDrawing?.CancelDrag() == true) { e.Handled = true; return; }

        if (DataContext is not TechDocument doc) return;
        if (doc.ViewModel.SelectedStackupLayerName is null) return;

        doc.ViewModel.ClearStackupSelection();
        e.Handled = true;
    }

    private void OnActivationFocusRequested()
    {
        _subscribedDoc?.ConsumeActivationFocus();
        FocusForScrollingDeferred();
    }

    /// <summary>
    /// Takes keyboard focus for the editor as a whole, so Page Up/Down reach
    /// <see cref="OnScrollKeyDown"/>.
    ///
    /// <para><b>The view itself, not the visible tab's list</b>, and not a field in a row. Focusing
    /// the list would make the FIRST thing the user sees a control with a selection, in lists whose
    /// rows are deliberately flattened so selection is invisible; focusing a row's text box would put
    /// a caret in an editable process value nobody asked to edit. Focusing the view lands on
    /// <see cref="TargetScrollViewer"/>'s own documented fallback — the visible tab's row list —
    /// which already resolves correctly for whichever tab is showing, including after a tab change.</para>
    ///
    /// <para>Deferred to Background priority for the same reason every other view here defers it: on
    /// first open the visual tree is still being realized and a synchronous Focus() lands on a
    /// control that has not been attached yet. <c>IsTabStop="False"</c> keeps this out of the Tab
    /// order — it is a programmatic focus target, never a stop the user cycles through.</para>
    /// </summary>
    /// <summary>
    /// Undocking is the same dead keyboard by a different route.
    ///
    /// <para>Floating the editor builds a NEW window around this view, and a new window's activation
    /// is not the dock's activation — no <c>IActivatableDocument</c> request fires, so the hook above
    /// never runs, nothing inside the view holds focus, and Page Up/Down are dead again until
    /// something is clicked. Attaching to a visual tree is the one event both routes share.</para>
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FocusForScrollingDeferred(onlyIfUnclaimed: true);
    }

    /// <param name="onlyIfUnclaimed">Take focus only when nothing else already holds it. The
    /// activation hook may pass false because an explicit activation IS the claim; an attach may
    /// not, because a view can be re-attached by an ordinary dock rearrangement while the user is
    /// typing somewhere else entirely, and yanking the caret out of another panel would be a worse
    /// bug than the one being fixed.</param>
    private void FocusForScrollingDeferred(bool onlyIfUnclaimed = false) =>
        Dispatcher.UIThread.Post(() =>
        {
            // Evaluated inside the posted action, not before it: on the undock path the view is
            // still moving between windows when the attach fires, so the top level asked any earlier
            // is the one being left rather than the one being entered.
            if (onlyIfUnclaimed && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is { } held
                && held is not TopLevel)
                return;

            Focus();
        }, DispatcherPriority.Background);

    // ── Page Up / Page Down / Home / End over the row lists ────────────────────

    private void OnScrollKeyDown(object? sender, KeyEventArgs e)
    {
        // An open dropdown owns all four keys — it is navigating its own items, and the list behind
        // it is not what the user is looking at.
        if (e.Source is ComboBox { IsDropDownOpen: true }) return;

        // R-stk2-10, which brief 4 is the first thing to test. The inline edit box is the ONLY
        // focusable control inside the drawing's ScrollViewer, so without this line Page Up/Down
        // typed into an open editor would resolve to the DRAWING's scroller and scroll the label the
        // box is sitting on out of the pane — the exact quiet re-pointing R-stk2-10 exists to
        // prevent. Home and End are already excused for any text input by PanelScrollKeys.ActionFor;
        // these two are not, because a row's text box is not something anyone pages through.
        if (ReferenceEquals(e.Source, StackupInlineEdit)) return;

        var action = PanelScrollKeys.ActionFor(e.Key, e.Source is TextBox);
        if (action is null) return;

        var scroll = TargetScrollViewer(e.Source);
        if (scroll is null) return;

        PanelScrollKeys.Apply(action.Value, scroll);
        e.Handled = true;
    }

    /// <summary>
    /// The scroller the keystroke belongs to: the one the focused control is INSIDE, if any — which
    /// is the row list when focus is in a row, and the Stackup tab's own drawing-layer picker when
    /// focus is in that — falling back to the visible tab's row list, which is where focus sits when
    /// the user has just typed in the filter box (that box is deliberately outside the list).
    /// </summary>
    private ScrollViewer? TargetScrollViewer(object? source)
    {
        if (source is Visual v && v.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is { } inner)
            return inner;

        var list = SectionTabs?.SelectedIndex switch
        {
            0 => LayersList,
            1 => StackupList,
            2 => DrcRulesList,
            3 => InterchangeList,
            _ => null,
        };
        return list?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    }

    private void OnFieldLostFocus(object? sender, RoutedEventArgs e) => CommitField(sender);

    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            CommitField(sender);
            e.Handled = true;
        }
    }

    private static void CommitField(object? sender)
    {
        if (sender is not Control c) return;
        var tag = c.Tag as string;

        switch (c.DataContext)
        {
            case LayerRowViewModel lr:
                switch (tag)
                {
                    case "Name":        lr.CommitName();        break;
                    case "LayerNumber": lr.CommitLayerNumber(); break;
                    case "Datatype":    lr.CommitDatatype();    break;
                    case "FillOpacity": lr.CommitFillOpacity(); break;
                    case "ZOrder":      lr.CommitZOrder();      break;
                    case "Purpose":     lr.CommitPurpose();     break;
                    case "GdsiiLayer":         lr.CommitGdsiiLayer();         break;
                    case "GdsiiDatatype":      lr.CommitGdsiiDatatype();      break;
                    case "DxfLayerName":       lr.CommitDxfLayerName();       break;
                    case "GerberSuffix":       lr.CommitGerberSuffix();       break;
                    case "GerberFileFunction": lr.CommitGerberFileFunction(); break;
                }
                break;

            case StackupLayerRowViewModel sr:
                switch (tag)
                {
                    case "Name":      sr.CommitName();      break;
                    case "Thickness": sr.CommitThickness(); break;
                    case "Epsr":      sr.CommitEpsr();      break;
                    case "TanD":      sr.CommitTanD();      break;
                    case "Mur":       sr.CommitMur();       break;
                    case "Sigma":     sr.CommitSigmaSm();   break;
                    case "WallThickness": sr.CommitWallThickness(); break;
                }
                break;

            case DrcRuleRowViewModel dr:
                switch (tag)
                {
                    case "Name":     dr.CommitName();     break;
                    case "Value":    dr.CommitValue();    break;
                    case "RegionA":  dr.CommitRegionA();  break;
                    case "RegionB":  dr.CommitRegionB();  break;
                    case "Window":   dr.CommitWindow();   break;
                    case "MinRatio": dr.CommitMinRatio(); break;
                    case "MaxRatio": dr.CommitMaxRatio(); break;
                }
                break;
        }
    }

    private void OnComboSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not Control c) return;
        var tag = c.Tag as string;

        if (c.DataContext is DrcRuleRowViewModel dr)
        {
            switch (tag)
            {
                case "Kind":     dr.CommitKind();     break;
                case "Layer":    dr.CommitLayer();    break;
                case "Severity": dr.CommitSeverity(); break;
                case "NetScope": dr.CommitNetScope(); break;
            }
        }
    }
}
