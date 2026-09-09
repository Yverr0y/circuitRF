using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace CircuitRF.Ui.Views;

/// <summary>
/// <b>A floating panel that goes half-transparent on a click and stays that way</b> (owner,
/// 2026-09-08, against the History panel floated out of the shell).
///
/// <h3>What is actually happening</h3>
/// <para>Dock 12.0.0.2's <c>HostWindow</c> sets the <c>:dragging</c> pseudo-class in
/// <c>OnPointerPressed</c> — before it knows whether a drag will follow — and clears it only from
/// <c>OnPointerReleased</c>, guarded on its own <c>_draggingWindow</c> flag. Its Fluent theme styles
/// <c>HostWindow:toolwindow:dragging</c> with <c>Opacity 0.5</c>, a null background and
/// <c>TransparencyLevelHint = Transparent</c>, which is the translucency being reported. That is a
/// deliberate drag affordance and there is nothing wrong with it.</para>
///
/// <para><b>The press hands the pointer to the operating system, and the release never comes back.</b>
/// Between those two handlers sits <c>Window.BeginMoveDrag</c>, which on macOS is
/// <c>[NSWindow performWindowDragWithEvent:]</c> — an AppKit event loop that swallows the mouse-up.
/// Avalonia is never told the button came up, so <c>OnPointerReleased</c> does not run and
/// <c>:dragging</c> is left set. It is cleared by the NEXT release the window happens to see, which is
/// exactly the reported remedy of clicking the title bar a second time. It is not specific to a click
/// that never moved: a real drag leaves the class set too, and is simply less likely to be noticed
/// because the click that follows a drag usually lands in the window and clears it.</para>
///
/// <h3>Why a pointer MOVE, and not a release</h3>
/// <para>There is no release to listen for — that is the defect. What always follows an OS window drag
/// is a pointer move over the window with no button held, and that is the one signal available on all
/// three platforms without reaching into the library's private state. The button test is what keeps a
/// genuine drag translucent: while one is in progress the platform delivers nothing at all here, and
/// if it ever did, the left button would be down and this would stand aside.</para>
///
/// <para>Only the pseudo-class is cleared. Dock's own <c>_draggingWindow</c>/<c>_mouseDown</c> flags are
/// private, and they already settle on the next release exactly as they do today — the difference is
/// that the designer is no longer looking through the window while they wait for it.</para>
/// </summary>
public static class HostWindowDragLatch
{
    /// <summary>Dock's own name for the class its Fluent theme hangs the translucency on.</summary>
    private const string Dragging = ":dragging";

    /// <summary>Registers the latch clear on <paramref name="top"/>. Safe on any top level: a window
    /// that never carries the class never does anything here.</summary>
    public static void Attach(TopLevel top)
    {
        top.AddHandler(
            InputElement.PointerMovedEvent,
            (_, e) =>
            {
                if (top.Classes is not IPseudoClasses classes || !classes.Contains(Dragging)) return;

                // A move with a button still down is a drag the platform IS reporting — leave it alone.
                var point = e.GetCurrentPoint(top).Properties;
                if (point.IsLeftButtonPressed || point.IsRightButtonPressed || point.IsMiddleButtonPressed)
                    return;

                classes.Remove(Dragging);
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }
}
