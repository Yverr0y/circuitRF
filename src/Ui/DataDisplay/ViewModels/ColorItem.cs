using Avalonia.Media;
using SkiaSharp;

namespace CircuitRF.Ui.DataDisplay.ViewModels;

/// <summary>
/// A color entry from <see cref="TraceProperties.ColorLUT"/> formatted for
/// display in a ComboBox color picker.  Brush is created once and reused.
/// </summary>
public sealed class ColorItem
{
    public int     Index { get; }
    public SKColor Color { get; }
    public string  Name  { get; }
    public IBrush  Brush { get; }

    // The LUT is SKColor since RND-4 moved TraceProperties below the UI firewall; the swatch this
    // item paints is still an Avalonia brush, so the conversion happens once, here, at the boundary.
    public ColorItem(int index, SKColor color, string name)
    {
        Index = index;
        Color = color;
        Name  = name;
        Brush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
    }

    public override string ToString() => Name;
}
