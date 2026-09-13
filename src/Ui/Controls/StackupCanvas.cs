using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Styling;
using CircuitRF.Ui.Layout;

namespace CircuitRF.Ui.Controls;

/// <summary>
/// The stackup cross-section, in the Technology Editor's Stackup tab, above the cards.
///
/// <h3>It draws. It does not edit — yet</h3>
/// <para>brief 2 is deliberately READ-ONLY: no pointer handler, no selection, no menu. Briefs 3-7
/// add each of those, and each of them is small precisely because this one carries the hosting.
/// A pointer handler added here rather than there is what makes brief 3 impossible to review.</para>
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
        // Not Focusable: this control handles no key and no pointer in brief 2, and a focusable
        // canvas inside the drawing's ScrollViewer would re-point Page Up/Page Down at the drawing
        // (TechEditorView's TargetScrollViewer walks up from whatever holds focus). Those keys scroll
        // the CARD list and must keep scrolling it — R-stk2-10.
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
        _hooked = true;
    }

    private void Unhook()
    {
        if (!_hooked || _viewModel is null) return;
        _viewModel.StackupChanged -= OnStackupChanged;
        _hooked = false;
    }

    /// <summary>
    /// MEASURE as well as visual, and that is the whole of it: a thickness edit changes the scene's
    /// HEIGHT, and a visual-only invalidation would repaint new bands inside the old height — the
    /// drawing correct and the scrollbar lying about it.
    /// </summary>
    private void OnStackupChanged()
    {
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
        context.Custom(new StackupDrawOperation(new Rect(Bounds.Size), scene, StackupTheme));
    }

    // ── ICustomDrawOperation ──────────────────────────────────────────────────────────────────────

    private sealed class StackupDrawOperation : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly StackupScene _scene;
        private readonly StackupRenderTheme _theme;

        public StackupDrawOperation(Rect bounds, StackupScene scene, StackupRenderTheme theme)
        {
            _bounds = bounds;
            _scene = scene;
            _theme = theme;
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
            StackupRenderer.Draw(lease.SkCanvas, _scene, _theme);
        }
    }
}
