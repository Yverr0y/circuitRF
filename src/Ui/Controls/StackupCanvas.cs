using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Styling;
using CircuitRF.Ui.Layout;

namespace CircuitRF.Ui.Controls;

/// <summary>
/// The stackup cross-section, in the Technology Editor's Stackup tab, above the cards.
///
/// <h3>It draws, and it SELECTS. It does not edit</h3>
/// <para>brief 2 was deliberately read-only; brief 3 adds the one pointer gesture — a click selects,
/// hover marks what a click would take — and nothing else. No drag, no menu, no inline editor: those
/// are briefs 4-6, and each is small precisely because this one carries the hosting.</para>
///
/// <h3>Selection is the VIEW MODEL's, held by name</h3>
/// <para>This control neither owns nor caches what is selected. It writes
/// <see cref="TechEditorViewModel.SelectedStackupLayerName"/> and reads it back, because the card
/// list below is looking at the same property — and because a reference to a band or a row VM would
/// not survive the next committed edit (R-stk3-1).</para>
///
/// <h3>What it is, structurally</h3>
/// <para>A <c>Control</c> on <c>WBondProfileCanvas</c>'s pattern: an <see cref="ICustomDrawOperation"/>
/// leases the Skia canvas and hands it to <c>StackupRenderer.Draw</c>. Everything that decides WHERE
/// anything goes is in <see cref="StackupScene"/>, below the firewall, and is shared with the
/// clipboard export and the documentation figures. There is no second renderer and no second
/// placement arithmetic.</para>
///
/// <h3>It measures to the scene, which is what makes the pane scroll</h3>
/// <para><see cref="MeasureOverride"/> returns the scene's INTRINSIC height (brief 1 §2) against the
/// pane's width. Nothing compresses the drawing to fit a viewport — a tall stack is a tall drawing
/// and the <c>ScrollViewer</c> around it scrolls. The width is taken as given, which is why that
/// <c>ScrollViewer</c> has its horizontal bar disabled: a horizontal scrollbar here would mean the
/// scene got its own width wrong.</para>
/// </summary>
public sealed class StackupCanvas : Control
{
    /// <summary>The width to lay a scene out at when the measure constraint gives none — a design
    /// surface, a measure outside a <c>ScrollViewer</c> with a horizontal bar. Wide enough for the
    /// two-column layout rather than the narrow fallback, so a canvas with no constraint does not
    /// look broken.</summary>
    public const double FallbackWidth = 600;

    // ── ViewModel ─────────────────────────────────────────────────────────────────────────────────

    public static readonly DirectProperty<StackupCanvas, TechEditorViewModel?> ViewModelProperty =
        AvaloniaProperty.RegisterDirect<StackupCanvas, TechEditorViewModel?>(
            nameof(ViewModel), o => o.ViewModel, (o, v) => o.ViewModel = v);

    private readonly StackupSceneCache _cache = new();
    private TechEditorViewModel? _viewModel;
    private bool _hooked;

    public TechEditorViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (ReferenceEquals(_viewModel, value)) return;

            // Unhooked FIRST, on the OLD view model, and never on the new one. A handler left on a
            // technology this canvas no longer shows repaints it for edits nobody here can see, and
            // keeps that editor alive — the bug WBondProfileCanvas's own setter carries a comment
            // about, and the same one R-stk2-2 states for the theme handler.
            Unhook();

            SetAndRaise(ViewModelProperty, ref _viewModel, value);

            Hook();
            _cache.Invalidate();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Resolved from <c>ThemeService</c> on attach and on every theme change, exactly as
    /// <c>WBondProfileCanvas.RefreshTheme</c> resolves its own. Settable, for a caller with no visual
    /// tree.
    ///
    /// <para><b>Not called <c>Theme</c></b>, which brief 2 §1 spelled it: <c>StyledElement.Theme</c>
    /// already exists and is the STYLING theme (a <c>ControlTheme</c>). Shadowing it with <c>new</c>
    /// would leave a <c>Theme="…"</c> attribute in any .axaml meaning whichever of the two the
    /// compiler picked. <c>WBondProfileCanvas</c> hit the same wall and answered it the same way —
    /// <c>WireTheme</c>, <c>LayoutTheme</c>.</para>
    /// </summary>
    public StackupRenderTheme StackupTheme { get; set; } = StackupRenderTheme.Fallback;

    public StackupCanvas()
    {
        // STILL not Focusable, though this control now handles a pointer — R-stk2-10 has not moved.
        // A focusable canvas inside the drawing's ScrollViewer re-points Page Up/Page Down at the
        // drawing, because TechEditorView's TargetScrollViewer walks up from whatever holds focus;
        // those keys scroll the CARD list and must keep scrolling it. Pointer input needs no focus.
        //
        // Esc therefore cannot be a key handler HERE. It is a tunnelling handler on the view instead,
        // which is the half of R-stk3-9 that was going to be needed anyway ("pressing Esc will
        // unselect", not "pressing Esc while the drawing happens to have focus") and which covers the
        // canvas as well as the card list. See src/Ui/RESOLVED.md.
        Focusable = false;
        ClipToBounds = true;

        ActualThemeVariantChanged += (_, _) => RefreshTheme();
    }

    // ── Theme (R-stk2-2) ──────────────────────────────────────────────────────────────────────────

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeService.ThemeChanged += OnThemeChanged;
        RefreshTheme();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ThemeService.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => RefreshTheme();

    private void RefreshTheme()
    {
        var variant = ActualThemeVariant == ThemeVariant.Dark ? ColorVariant.Dark : ColorVariant.Light;
        StackupTheme = StackupRenderTheme.FromTheme(ThemeService.Active, variant);
        InvalidateVisual();
    }

    // ── Live repaint (R-stk2-3) ───────────────────────────────────────────────────────────────────

    private void Hook()
    {
        if (_hooked || _viewModel is null) return;
        _viewModel.StackupChanged += OnStackupChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _hooked = true;
    }

    private void Unhook()
    {
        if (!_hooked || _viewModel is null) return;
        _viewModel.StackupChanged -= OnStackupChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _hooked = false;
    }

    /// <summary>
    /// A selection change repaints and NOTHING ELSE — no measure, no scene rebuild.
    ///
    /// <para>The outline is overlay chrome laid over rects the scene already placed (R-stk1-1), so
    /// selecting a band changes not one thing about the layout. Invalidating measure here would drop
    /// and rebuild the scene on every click, and on a large stackup that is the whole layout pass for
    /// a two-pixel outline.</para>
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TechEditorViewModel.SelectedStackupLayerName))
            InvalidateVisual();
    }

    /// <summary>
    /// MEASURE as well as visual, and that is the whole of it: a thickness edit changes the scene's
    /// HEIGHT, and a visual-only invalidation would repaint new bands inside the old height — the
    /// drawing correct and the scrollbar lying about it.
    /// </summary>
    private void OnStackupChanged()
    {
        // R-stk4-8, FIRST: an undo, a redo, an edit committed from the card below — every one of them
        // rebuilds the scene, and a box left open is pointing at a rect that no longer exists. A
        // commit made through the box has already closed it (R-stk4-7), so this is a no-op there.
        _inlineEdit?.Close();

        _cache.Invalidate();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>The scene this canvas is holding. Brief 3's pointer handlers read it — the same one
    /// the renderer drew, which is R-stk1-1 — and the tests read it because there is no headless
    /// render here to read instead.</summary>
    internal StackupSceneCache SceneCache => _cache;

    /// <summary><see cref="MeasureOverride"/> at a stated width, reachable without a layout pass.</summary>
    internal Size MeasureForWidth(double width) => MeasureOverride(new Size(width, double.PositiveInfinity));

    // ── Pointer: click selects, hover marks (R-stk3-2, R-stk3-4) ──────────────────────────────────

    /// <summary>The layer under the pointer. Canvas-local and deliberately NOT on the view model:
    /// hover is a property of one pointer over one drawing, it survives nothing and nobody else reads
    /// it, whereas the SELECTION is shared with the card list and is the view model's.</summary>
    private string? _hoverLayer;

    /// <summary>What the renderer is told about this frame's transient state. Rebuilt per paint from
    /// the two pieces of state above, so there is no third copy to keep in step.</summary>
    private StackupOverlay Overlay => new()
    {
        HoverLayer    = _hoverLayer,
        SelectedLayer = _viewModel?.SelectedStackupLayerName,
    };

    /// <summary>
    /// The scene coordinates of a pointer event.
    ///
    /// <para>The scene is drawn at the control's own origin (<see cref="Render"/> hands the draw
    /// operation <c>Bounds.Size</c> and the renderer draws in scene coordinates from 0,0), so a
    /// position relative to this control IS a scene position. No transform, and no second one to keep
    /// in step with the renderer's.</para>
    /// </summary>
    private StackupHit? HitAt(Point p)
    {
        var scene = _cache.Current ?? _cache.Get(_viewModel?.Working, (float)Bounds.Width);
        return scene.HitTest((float)p.X, (float)p.Y);
    }

    /// <summary>
    /// R-stk3-2. A <c>Band</c> or <c>ViaBarrel</c> hit selects that entry; a <c>Label</c> hit selects
    /// the layer the label belongs to (brief 4 makes a DOUBLE click on a value open an editor — a
    /// single click still just selects); a via's grippers belong to their via and select it.
    ///
    /// <para><b>A hit on nothing CLEARS the selection</b> — clicking the background is how a drawing
    /// surface has always meant "nothing", and a drawing where the only way to deselect is a keystroke
    /// is one the user fights.</para>
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        if (!PressAt(p)) return;

        // R-stk4-3. The second press of a double-click, and only the second: a single click that
        // opened an editor would be an editor the user only meant to click past — InlineEditText's
        // own doc comment records that finding. The first press has already selected, and this one
        // selects the same entry again on its way through.
        if (e.ClickCount == 2) DoubleClickAt(p);

        e.Handled = true;
    }

    /// <summary>The gesture itself, without the pointer event — the same kind of seam
    /// <see cref="MeasureForWidth"/> and <see cref="SceneCache"/> already are, and for the same
    /// reason: there is no input device in this project's tests to raise a real press with.</summary>
    /// <returns>False when there is nothing to select into (no view model).</returns>
    internal bool PressAt(Point p)
    {
        if (_viewModel is null) return false;
        _viewModel.SelectedStackupLayerName = HitAt(p)?.LayerName;
        return true;
    }

    /// <summary>
    /// R-stk3-4. Without hover the first click is a guess: nothing on this drawing says it is
    /// clickable at all. A hovered band takes a lighter outline and a hovered via reveals brief 1's
    /// grippers, which the scene has already placed whether or not a frame drew them.
    /// </summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        MoveAt(e.GetPosition(this));
    }

    /// <summary>The hover half of the same seam. <see cref="HoverLayer"/> reads back what it decided.</summary>
    internal void MoveAt(Point p) => SetHover(HitAt(p)?.LayerName);

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(null);
    }

    /// <summary>Repaints only when the hovered layer actually CHANGED — a pointer crossing a band
    /// raises a move event per pixel, and repainting the whole drawing for each of them would make a
    /// mouse sweep the most expensive thing this tab does.</summary>
    private void SetHover(string? name)
    {
        if (string.Equals(_hoverLayer, name, StringComparison.Ordinal)) return;
        _hoverLayer = name;
        InvalidateVisual();
    }

    /// <summary>What the pointer is over, for the tests and for nobody else — the renderer is handed
    /// <see cref="Overlay"/>, which is built from this.</summary>
    internal string? HoverLayer => _hoverLayer;

    // ── The inline editor (brief 4) ───────────────────────────────────────────────────────────────

    private StackupInlineEditor? _inlineEdit;

    /// <summary>
    /// The box this canvas opens over a double-clicked value, handed in by the host.
    ///
    /// <para>The BOX is the host's — it is a sibling in the host's panel, because a box floated over
    /// this control has to be laid out beside it rather than inside it, and because the host is what
    /// wires the three-key contract to it. What the canvas owns is the half that needs the scene:
    /// which label was hit, what it seeds from and where exactly it goes.</para>
    /// </summary>
    internal StackupInlineEditor? InlineEditor
    {
        get => _inlineEdit;
        set => _inlineEdit = value;
    }

    /// <summary>Whether an inline editor is open over this drawing — the gate
    /// <c>TechEditorView.OnEscapeKeyDown</c> checks so that <c>Esc</c> reverts the edit before it
    /// clears the selection (R-stk4-6).</summary>
    internal bool InlineEditIsOpen => _inlineEdit?.IsOpen == true;

    /// <summary>
    /// R-stk4-3's gesture, without the pointer event — the same seam <see cref="PressAt"/> is, and
    /// for the same reason.
    /// </summary>
    /// <returns>True when a box was opened.</returns>
    internal bool DoubleClickAt(Point p)
    {
        if (_inlineEdit is null || _viewModel is null) return false;
        var scene = _cache.Current ?? _cache.Get(_viewModel.Working, (float)Bounds.Width);
        return _inlineEdit.TryOpen(scene, _viewModel, (float)p.X, (float)p.Y);
    }

    /// <summary>The overlay this canvas would hand the renderer right now.</summary>
    internal StackupOverlay CurrentOverlay => Overlay;

    // ── Measure and draw ──────────────────────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size available)
    {
        double w = available.Width;
        if (double.IsNaN(w) || double.IsInfinity(w) || w <= 0) w = FallbackWidth;

        var scene = _cache.Get(_viewModel?.Working, (float)w);
        return new Size(w, scene.Height);
    }

    public override void Render(DrawingContext context)
    {
        // Current, not a fresh build: MeasureOverride has already run this layout pass and the scene
        // it produced is the one whose height the ScrollViewer is scrolling. Building a second one
        // here — at Bounds.Width, which is not necessarily the measured width — is how a picture and
        // a scrollbar come to disagree.
        var scene = _cache.Current ?? _cache.Get(_viewModel?.Working, (float)Bounds.Width);
        context.Custom(new StackupDrawOperation(new Rect(Bounds.Size), scene, StackupTheme, Overlay));
    }

    // ── ICustomDrawOperation ──────────────────────────────────────────────────────────────────────

    private sealed class StackupDrawOperation : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly StackupScene _scene;
        private readonly StackupRenderTheme _theme;
        private readonly StackupOverlay _overlay;

        public StackupDrawOperation(
            Rect bounds, StackupScene scene, StackupRenderTheme theme, StackupOverlay overlay)
        {
            _bounds = bounds;
            _scene = scene;
            _theme = theme;
            _overlay = overlay;
        }

        /// <summary>Never equal, so a repaint is never skipped — the same answer
        /// <c>WBondProfileCanvas.ProfileDrawOperation</c> gives, for the same reason: the scene is a
        /// reference and comparing two of them here would re-derive what the cache already knows.</summary>
        public bool Equals(ICustomDrawOperation? other) => false;

        public Rect Bounds => _bounds;
        public bool HitTest(Point p) => _bounds.Contains(p);
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature is null) return;

            using var lease = leaseFeature.Lease();
            StackupRenderer.Draw(lease.SkCanvas, _scene, _theme, _overlay);
        }
    }
}
