using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Dock.Controls.ProportionalStackPanel;

namespace CircuitRF.Ui.Views.Palette;

/// <summary>
/// Keeps a docked Library palette showing the number of component-glyph columns it is showing now,
/// whatever the user has dragged that to, while the workspace window is resized.
///
/// <para><b>Why this is not just a better default number.</b> Dock sizes a column as a FRACTION of
/// the window, so a palette that is two glyphs wide at 1200 px is two and a half at 1500 and three
/// at 1800 — the glyphs reflow at whole slots, so the extra width is not a wider palette, it is a
/// strip of empty space that grows and then jumps to another column. The fraction has to be
/// recomputed from the pixel width every time the pool it divides changes, which is what this
/// does.</para>
///
/// <para><b>The count is read off the palette, never assumed.</b> It is whatever whole number of
/// glyph columns the tile area is showing (<see cref="PaletteColumnWidth.GlyphColumnsIn"/>), re-read
/// on every layout pass in which the window has not moved — so dragging the splitter to a three- or
/// five-glyph palette is all it takes to make the window keep it at three or five. Only a palette
/// too narrow to show one whole column has no count to preserve, and that one is left to scale as it
/// always did.</para>
///
/// <para><b>Two things must never be mistaken for the user choosing a narrower palette, because the
/// count is a LATCH and a wrong read of it does not wash out</b> (owner, 2026-09-14: the docked
/// Library sometimes dropped from two columns to one on a window resize, noticeably more often in a
/// Debug build, where layout is the slower half of the race). Once the latch reads one, the pin
/// holds one, and widening the window back does not bring the second column back — so both of these
/// are refusals to read rather than corrections after the fact:</para>
///
/// <list type="bullet">
/// <item><description>A pass that belongs to a window resize. The pool is compared against the last
/// MEASUREMENT, never against the prediction <see cref="OnWindowPropertyChanged"/> acts on — see the
/// note there for why storing the prediction quietly turned most resize passes into still
/// ones.</description></item>
/// <item><description>A pass in which the pin asked for a width the row could not give
/// (<see cref="PaletteColumnWidth.HasRoomFor"/>). The palette is then scaled to its share of a
/// too-narrow window, which is not a width anybody chose.</description></item>
/// </list>
///
/// <para><b>The splitter still goes anywhere, because nothing is applied DURING a drag.</b> The pin
/// acts when the pool the columns divide up changes — that is, when the window is resized — and never
/// merely because the column width did. Re-applying on every layout pass would peg the splitter to
/// whole glyph slots and make every width between them unreachable, which is the one thing this must
/// not break. The consequence, and it is deliberate: a palette dragged to four columns and a bit
/// tightens to exactly four the first time the window moves, because the bit is a strip no glyph was
/// in.</para>
///
/// <para><b>The window's ClientSize is the trigger, not the layout that follows it.</b> Avalonia
/// publishes the new client size and then runs the layout pass, so re-proportioning there lands
/// before anything is arranged and the palette never flashes at the scaled width — which it would if
/// the correction were made after the fact, most visibly on a maximise, where one frame's worth of
/// scaling is the whole jump. <see cref="OnPanelLayoutUpdated"/> then re-applies against the width
/// the pass actually produced — always, since the prediction is never written into the measurement —
/// which is what catches any case where the panel's width does not track the window's
/// one-for-one.</para>
/// </summary>
public sealed class PaletteColumnPin
{
    // One pin per workspace window. A tear-off host window holds a PaletteToolView too and has no
    // pin registered, which is exactly right: a floated palette has no column to size.
    private static readonly ConditionalWeakTable<TopLevel, PaletteColumnPin> Pins = new();

    private readonly Window _window;

    private ProportionalStackPanel? _panel;   // the row of columns
    private ContentPresenter?       _column;  // the palette's column within it
    private PaletteToolView?        _view;    // what measures the chrome

    private bool   _settled;                  // a usable measurement has been seen for this arrangement
    private int    _columns;                  // glyph columns the palette is showing; 0 = nothing to hold
    private double _chrome;                   // column width less tile-area width, as last measured
    private double _pool;                     // the panel width proportions divide up — MEASURED, only ever
    private double _predicted;                // that width as predicted since the last pass; 0 = none pending
    private bool   _hasRoom = true;           // the last Apply could get the width it asked for
    private double _clientWidth;

    private PaletteColumnPin(Window window)
    {
        _window      = window;
        _clientWidth = window.ClientSize.Width;
        window.PropertyChanged += OnWindowPropertyChanged;
    }

    /// <summary>Arms the pin for one workspace window. Idempotent.</summary>
    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (Pins.TryGetValue(window, out _)) return;
        Pins.Add(window, new PaletteColumnPin(window));
    }

    // ── What the palette tells us ─────────────────────────────────────────────
    //
    //  The view reports itself rather than being searched for. A search would have to run on every
    //  layout pass to notice a rebuilt dock tree, and walking the whole dock's visual tree that often
    //  — in the arrangements where the palette is a background tab and the search finds nothing at
    //  all — is a real cost for no result.

    internal static void NotifyPaletteAttached(PaletteToolView view)
    {
        if (TopLevel.GetTopLevel(view) is { } top && Pins.TryGetValue(top, out var pin)) pin.Adopt(view);
    }

    internal static void NotifyPaletteDetached(PaletteToolView view)
    {
        // The view is usually already off its root by now, so the window lookup often misses — the
        // sweep is what actually drops it. Cheap: one pin per open workspace window.
        if (TopLevel.GetTopLevel(view) is { } top && Pins.TryGetValue(top, out var owner)) owner.Release(view);
        else foreach (var (_, pin) in Pins) pin.Release(view);
    }

    private void Adopt(PaletteToolView view)
    {
        Release(_view);
        _view = view;

        // The column is the nearest ancestor that is a child of a row of columns. Found by walking UP
        // from the palette, which is a handful of steps, and which also settles the two arrangements
        // the palette can be in: its own right-hand column, or sharing the left column with the
        // Project Tree. In the second it will simply never be two glyphs wide, and never arm.
        for (Visual? v = view; v is not null; v = v.GetVisualParent())
        {
            if (v is ContentPresenter presenter
                && presenter.GetVisualParent() is ProportionalStackPanel { Orientation: Orientation.Horizontal } row)
            {
                _column = presenter;
                _panel  = row;
                _panel.LayoutUpdated += OnPanelLayoutUpdated;
                break;
            }
        }

        _settled   = false;
        _columns   = 0;
        _pool      = 0.0;
        _predicted = 0.0;
        _hasRoom   = true;
    }

    private void Release(PaletteToolView? view)
    {
        if (view is not null && !ReferenceEquals(view, _view)) return;
        if (_panel is not null) _panel.LayoutUpdated -= OnPanelLayoutUpdated;
        _panel     = null;
        _column    = null;
        _view      = null;
        _settled   = false;
        _columns   = 0;
        _predicted = 0.0;
        _hasRoom   = true;
    }

    // ── The two triggers ──────────────────────────────────────────────────────

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TopLevel.ClientSizeProperty) return;

        double width = _window.ClientSize.Width;
        double delta = width - _clientWidth;
        _clientWidth = width;

        if (_columns <= 0 || !_settled || _panel is null || Math.Abs(delta) < 0.5) return;

        // The pool has not been re-measured yet — the layout pass this change is about to start is
        // what does that. Every column in the row is inside the window and none of the chrome around
        // them is elastic, so the pool moves with the window pixel for pixel; OnPanelLayoutUpdated
        // checks the answer once the pass has run.
        //
        // **The prediction is kept apart from _pool, and that is the whole of it.** _pool is what
        // OnPanelLayoutUpdated compares against to decide whether the window has moved, and writing
        // the prediction into it made a resize pass look STILL whenever the prediction was right —
        // which is most of the time. The count was then re-read from a width the pin had only just
        // asked for and the pass had not necessarily delivered, and a width one pixel short of two
        // glyph slots reads as one column. That read latches: the pin holds the smaller number from
        // then on, and widening the window back does not bring the column back. Leaving _pool a
        // MEASUREMENT means every pass that follows a resize takes the poolChanged branch, which
        // re-applies against the real width and never re-reads the count — which is what the note
        // below, and this class's whole contract, already said happens.
        //
        // Several ClientSize changes can arrive between two layout passes (a fast drag, and a Debug
        // build where layout is the slower half), so the deltas accumulate here rather than each
        // being added to a measurement that is by then several steps stale.
        double pool = (_predicted > 0.0 ? _predicted : _pool) + delta;
        if (pool <= 0.0) return;

        _predicted = pool;
        Apply(pool);
    }

    private void OnPanelLayoutUpdated(object? sender, EventArgs e)
    {
        if (_panel is null || _column is null || _view is null) return;

        double pool        = PoolOf(_panel);
        double columnWidth = _column.Bounds.Width;
        double tileArea    = _view.TileAreaWidth;
        if (!(pool > 0.0) || !(columnWidth > 0.0) || !(tileArea > 0.0)) return;

        _chrome = columnWidth - tileArea;

        bool poolChanged = Math.Abs(pool - _pool) > 0.5;
        _pool      = pool;
        _predicted = 0.0;

        if (!_settled)
        {
            // First measurable layout of a NEW arrangement, and the only place the pending count is
            // taken. Two things land here:
            //
            //  - Reset Layout, and choosing a Window Layout, mean "the shipped arrangement, now", and
            //    the shipped palette is DefaultGlyphColumns wide whatever size the window has been
            //    dragged to since. The layout itself carries only a FRACTION, which is that many
            //    glyphs at the window's OPENING size and one more on a window since widened — so the
            //    count travels separately and is NOT read off the panel here.
            //
            //  - Everything else: read the count off the panel. A brand-new workspace becomes exactly
            //    two glyphs wide this way, the shipped proportion having opened within a pixel of it.
            //
            // **The request is taken only in this branch, and that is load-bearing.** It is raised
            // while the OUTGOING tree is still up, and a settled pin gets one more layout pass before
            // its panel is replaced — consuming there honoured it on the arrangement about to be
            // thrown away, and the new one then read three columns off its own fresh fraction. The
            // latch belongs to the arrangement that has not measured yet.
            _settled = true;
            _columns = _view.ConsumeDefaultWidthRequest()
                     ? PaletteColumnWidth.DefaultGlyphColumns
                     : PaletteColumnWidth.GlyphColumnsIn(tileArea);
            if (_columns > 0) Apply(pool);
            return;
        }

        if (poolChanged)
        {
            // The window moved. The count is the one from BEFORE it moved — re-reading it here would
            // read it off the width that has just scaled, which is the very thing being corrected.
            if (_columns > 0) Apply(pool);
            return;
        }

        // The pin asked for a width this row could not give — the window is too narrow to hold the
        // count the user set beside a document column that still has something in it. The palette is
        // scaled to its share of that window, which is NOT the count they set, so there is nothing
        // here to read. Reading it anyway is a one-way ratchet: the smaller number becomes the one
        // the pin holds, and their columns do not come back when the window is widened again.
        if (!_hasRoom) return;

        // Same window, different column: the user moved the splitter, and the count they have landed
        // on is the one to keep. Nothing is applied — see the class note on why a pin that snapped
        // back mid-drag would be a bug.
        _columns = PaletteColumnWidth.GlyphColumnsIn(tileArea);
    }

    // ── Doing it ──────────────────────────────────────────────────────────────

    private void Apply(double pool)
    {
        if (_panel is null || _column is null) return;

        var columns = _panel.Children.Where(c => !IsSplitter(c)).ToList();
        int index   = columns.IndexOf(_column);
        if (index < 0) return;

        double target  = PaletteColumnWidth.TargetWidth(_chrome, _columns);
        var    current = columns.Select(ProportionalStackPanel.GetProportion).ToArray();

        // Recorded rather than inferred from the refusal below, which also refuses a column that is
        // already exactly where it should be — the opposite case entirely.
        _hasRoom = PaletteColumnWidth.HasRoomFor(current, index, pool, target);

        if (!PaletteColumnWidth.TryPin(current, index, pool, target, out var pinned)) return;

        // Set on the PRESENTER, at local value, which is where Dock itself writes a resize: the
        // theme binds the presenter's proportion two-way to the dockable's, so this still reaches the
        // model and is still what gets saved into the .cws. Writing the dockable instead would not
        // move the layout at all — Dock's own arrange has already written a local value here, and a
        // local value outranks the style-priority binding that would carry a model change back.
        for (int i = 0; i < columns.Count; i++)
            if (Math.Abs(pinned[i] - current[i]) > 0.0)
                ProportionalStackPanel.SetProportion(columns[i], pinned[i]);
    }

    /// <summary>The width proportions divide up: the row less the splitters between its columns.</summary>
    private static double PoolOf(ProportionalStackPanel panel)
        => panel.Bounds.Width - panel.Children.Where(IsSplitter).Sum(c => c.Bounds.Width);

    /// <summary>
    /// Dock wraps every child of the row in a <see cref="ContentPresenter"/>, splitters included, so
    /// a splitter is recognised by what is inside one. (Dock's own predicate is internal.)
    /// </summary>
    private static bool IsSplitter(Control control)
        => control is ProportionalStackPanelSplitter
        || control is ContentPresenter { Child: ProportionalStackPanelSplitter };
}
