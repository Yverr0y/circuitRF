// ================================================================
//  PlotAccentColor.cs  —  the system accent colour, for the plot canvas'
//  transient chrome (a marquee, a selected marker, a selected info box)
//
//  This was RenderTheme.GetTransparentAccent until RND-4 moved RenderTheme
//  below the UI firewall (brief-render-4-data-display.md R-rnd4-1). It did
//  not go with it, and the measurement is why: no RENDERER ever called it.
//  Its three callers are PlotControl, DragSelectOverlay and
//  MarkerInfoBoxView — all of them the window's, all of them drawing
//  SELECTION, which R-rnd4-7 lists among the things that do not come out
//  in an export. What it reads is the application's own resource
//  dictionary on the UI thread, which is exactly the dependency the
//  firewall exists to keep out of the drawing code.
// ================================================================

using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;

namespace CircuitRF.Ui.DataDisplay;

public static class PlotAccentColor
{
    /// <summary>
    /// The current Fluent theme's system accent colour at the given alpha, falling back to
    /// <see cref="RenderTheme.SelectionColorFallback"/> when there is no live application.
    /// </summary>
    public static SKColor GetTransparentAccent(byte alpha)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return FetchColorFromResources(alpha);

        return Dispatcher.UIThread.Invoke(() => FetchColorFromResources(alpha));
    }

    private static SKColor FetchColorFromResources(byte alpha)
    {
        if (Application.Current != null)
        {
            ThemeVariant currentTheme = Application.Current.ActualThemeVariant;

            if (Application.Current.TryGetResource("SystemAccentColor", currentTheme, out var resource) &&
                resource is Color systemAccent)
            {
                return new SKColor(systemAccent.R, systemAccent.G, systemAccent.B, alpha);
            }
        }
        var f = RenderTheme.SelectionColorFallback;
        return new SKColor(f.Red, f.Green, f.Blue, alpha);
    }
}
