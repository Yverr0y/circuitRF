using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CircuitRF.Design.Layout;

namespace CircuitRF.Ui.Converters;

/// <summary>
/// Displays a <see cref="ConductorSheetSurface"/> as what it actually DOES, rather than as its
/// stored name.
///
/// <para>Owner, 2026-09-09: "EM sheet at: Bottom / Top" is unreadable without knowing what a
/// conductor's z <i>band</i> is — a word the control never defines and the user has no reason to
/// know. The stored value is a surface (which face of the band the zero-thickness analysis sheet
/// sits on); the consequence a process author is actually choosing between is which neighbouring
/// dielectric absorbs the metal's thickness, and those are two ends of one decision that is always
/// made as a pair (<c>PlanarExtractor</c>'s medium-cut fallback). Naming the consequence is what
/// makes the row answerable without the chapter open beside it.</para>
///
/// <para><b>View-only, and the persisted enum is untouched.</b> A <c>.ctech</c> still stores
/// <c>"SheetAt": "Bottom"</c>/<c>"Top"</c>, every technology ever written still reads back
/// identically, and nothing about the extraction changes — this renames a label, not a field.
/// Same contract as <see cref="ToLowerStringConverter"/>, which is why there is no
/// <c>ConvertBack</c>: a ComboBox's <c>SelectedItem</c> stays bound to the enum itself.</para>
/// </summary>
public sealed class SheetAtLabelConverter : IValueConverter
{
    public static readonly SheetAtLabelConverter Instance = new();

    /// <summary>The row text for one surface. Public so a test can assert the wording without a
    /// UI platform, and so nothing else has to restate it.</summary>
    public static string Label(ConductorSheetSurface surface) => surface switch
    {
        // Bottom: the sheet sits on the band's LOWER face, so the metal's thickness is given to the
        // dielectric ABOVE and the substrate under the line stays exactly as authored. This is what
        // every technology written before the field existed means, and it is what the validated
        // closed-form microstrip models agree with — they measure height to the metal's underside.
        ConductorSheetSurface.Bottom => "the layer above (default)",

        // Top: the sheet sits on the band's UPPER face, so the thickness is given to the dielectric
        // BELOW and the substrate grows by it. What a thin-film capacitor's LOWER plate needs, so
        // the modelled plate gap is the capacitor dielectric alone.
        ConductorSheetSurface.Top    => "the substrate below",

        _ => surface.ToString(),
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ConductorSheetSurface s ? Label(s) : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException($"{nameof(SheetAtLabelConverter)} does not support ConvertBack.");
}
