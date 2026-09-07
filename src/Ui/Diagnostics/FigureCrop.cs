using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace CircuitRF.Ui.Diagnostics;

/// <summary>
/// <b>A figure that is one small part of a real window, at a size a reader can actually see it at.</b>
///
/// <para>Most rows in <see cref="FigureCatalog"/> photograph a whole window or a whole panel. Some
/// facts are not whole-panel facts — an 11 px mark on a document tab is invisible in a 1400 px-wide
/// workspace capture, and a reader told to look for it in that figure cannot find it. The answer is
/// not to draw a mock-up of the tab, because a mock-up stops tracking the application the moment the
/// real control changes. It is to capture the real window and show one rectangle of it.</para>
///
/// <para><b>The rectangle is found from the live visual tree, never hard-coded.</b> A pixel offset
/// measured once is right until the toolbar gains a row, and then the figure is of the wrong part of
/// the window with nothing to say so. <see cref="Around"/> takes the union of the bounds of whichever
/// controls the caller names and refuses if it finds none — a crop that silently found nothing is a
/// figure of the top-left corner, which is the failure mode that looks plausible.</para>
///
/// <para><b>Everything here is layout, not drawing.</b> The content is the real control, hosted
/// unchanged inside a clipping <see cref="Border"/> at its real size and simply offset — so the
/// capture is vector output of the same visuals the whole-window figure draws, at the same scale, and
/// a change to the tab template shows up here without anything in this file being touched.</para>
/// </summary>
public static class FigureCrop
{
    /// <summary>What a crop needs after layout: it moves the content under the window.</summary>
    /// <param name="Content">Hand this to <c>FigureScene</c> as the captured control.</param>
    /// <param name="Apply">Call from <c>AfterLayout</c>, once the real control has a size.</param>
    public readonly record struct Cropped(Control Content, Action Apply);

    /// <summary>
    /// Crops <paramref name="full"/> — laid out at its own natural size
    /// <paramref name="fullWidth"/> x <paramref name="fullHeight"/> — to the box around whatever
    /// <paramref name="locate"/> finds, grown by <paramref name="pad"/> on every side.
    ///
    /// <para>The capture size is the catalog row's, as for every other figure; this only decides which
    /// part of the window lands inside it. Give the row a size a little larger than the region so the
    /// crop has somewhere to sit, and the region is centred in it.</para>
    /// </summary>
    public static Cropped Around(Control full, int fullWidth, int fullHeight,
                                 Func<Control, System.Collections.Generic.IEnumerable<Visual>> locate,
                                 double pad, string describeWhatIsMissing)
    {
        full.Width  = fullWidth;
        full.Height = fullHeight;

        var canvas = new Canvas { ClipToBounds = true };
        canvas.Children.Add(full);

        var frame = new Border { ClipToBounds = true, Child = canvas };

        return new Cropped(frame, () =>
        {
            var found = locate(full).ToList();
            if (found.Count == 0)
                throw new InvalidOperationException(
                    "A cropped figure found none of the controls it crops to, so the capture would "
                  + "silently be of the window's top-left corner instead. " + describeWhatIsMissing);

            Rect box = default;
            bool any = false;
            foreach (var v in found)
            {
                if (v.TranslatePoint(default, full) is not { } origin) continue;
                var r = new Rect(origin, v.Bounds.Size);
                box = any ? box.Union(r) : r;
                any = true;
            }

            if (!any)
                throw new InvalidOperationException(
                    "A cropped figure's controls are not in the captured tree, so none of them could "
                  + "be located. " + describeWhatIsMissing);

            box = box.Inflate(pad);

            // Centred in whatever the catalog row asked for, so a region narrower than the row is not
            // jammed against the left edge and a region wider than it is trimmed evenly.
            double x = box.X - Math.Max(0, (frame.Bounds.Width  - box.Width)  / 2);
            double y = box.Y - Math.Max(0, (frame.Bounds.Height - box.Height) / 2);

            Canvas.SetLeft(full, -Math.Round(x));
            Canvas.SetTop (full, -Math.Round(y));
        });
    }
}
