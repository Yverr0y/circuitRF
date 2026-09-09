using System;
using Avalonia.Controls;
using Avalonia.Input;

namespace CircuitRF.Ui.Views;

/// <summary>
/// <b>The platform's busy cursor, for the length of an operation the window cannot paint through.</b>
///
/// <para>Owner, 2026-09-08: opening or switching a workspace is seconds of work with no pointer
/// feedback at all, so the only signal that anything is happening is that the window has stopped
/// responding — which is indistinguishable from the application having hung.</para>
///
/// <para><b>The previous cursor is restored, not the default one.</b> Assigning
/// <see cref="Cursor.Default"/> on the way out would silently discard a cursor something else had set
/// on the window; assigning what was read on the way in restores whatever was true, including null,
/// which is what makes the pointer an ordinary arrow again.</para>
///
/// <para>A null window is a legitimate caller — a headless run, or a view model whose window has gone
/// — and produces a scope that does nothing rather than a null the call site has to test.</para>
/// </summary>
internal sealed class BusyCursorScope : IDisposable
{
    private readonly Window? _window;
    private readonly Cursor? _previous;
    private bool _restored;

    private BusyCursorScope(Window? window)
    {
        _window = window;
        if (window is null) return;

        _previous     = window.Cursor;
        window.Cursor = new Cursor(StandardCursorType.Wait);
    }

    /// <summary>The scope, with no guarantee that it has reached the screen. Prefer
    /// <see cref="WhileAsync"/> anywhere the next thing the caller does is block.</summary>
    public static IDisposable While(Window? window) => new BusyCursorScope(window);

    /// <summary>
    /// The scope, <b>and a frame in which to draw it</b>:
    /// <c>using var _ = await BusyCursorScope.WhileAsync(window);</c>
    ///
    /// <para>Owner, 2026-09-08: on macOS the wait cursor sometimes did not appear at all. Assigning
    /// <see cref="Window.Cursor"/> reaches the platform, but the platform decides what the pointer
    /// looks like when it next services events — and the callers here assign it and then block the UI
    /// thread on the very next line, so there was no such moment until the work was already over. It
    /// was intermittent rather than absent because whether it showed depended on where the pointer was
    /// and whether anything happened to pump the loop.</para>
    ///
    /// <para>The context-menu case made it reliably worse: the pointer is over the menu's own popup
    /// when the item is clicked, so the cursor of the window UNDERNEATH it cannot take effect until
    /// that popup is gone. Yielding one dispatcher turn at
    /// <see cref="Avalonia.Threading.DispatcherPriority.Background"/> is what lets the popup close, the
    /// frame draw and the pointer update, all before the caller's first blocking line.</para>
    /// </summary>
    public static async System.Threading.Tasks.Task<IDisposable> WhileAsync(Window? window)
    {
        var scope = new BusyCursorScope(window);

        if (window is not null)
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                static () => { }, Avalonia.Threading.DispatcherPriority.Background);

        return scope;
    }

    public void Dispose()
    {
        if (_restored || _window is null) return;
        _restored      = true;
        _window.Cursor = _previous;
    }
}
