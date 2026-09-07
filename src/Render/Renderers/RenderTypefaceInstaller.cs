// Hands CircuitRF.Design the four embedded IBM Plex faces a label is rendered and flattened with.
//
// ── Why this file is in src/Render and not src/Ui, which is where it started ──────────────────────
//
// It WAS `UiTypefaceInstaller`, in src/Ui, and that was correct for as long as loading a face needed
// Avalonia: LayoutTextOutline had crossed the firewall so `circuitrf convert` could flatten a label
// to polygons headlessly, and the one thing it had to give up was SkiaFonts' AssetLoader. Unset, it
// fell back to SKTypeface.Default — a different glyph SHAPE, which every label-carrying export
// reported rather than hid.
//
// R-rnd1-4 removed the reason. SkiaFonts reads the faces out of CircuitRF.Render's own manifest
// resources now, with no platform under it, so this mapping can live on THIS side of the wall and
// `convert` gets the same glyph outlines the application does.
//
// ── And moving it was not optional, which is the part worth writing down ──────────────────────────
//
// RND-1's brief predicted that wiring this seam to the embedded loader WOULD change `convert`'s
// output and told the implementer to report it and leave it. The measurement inverted that: leaving
// it is what changes behaviour. Fixing SkiaFonts changed what the src/Ui installer SUPPLIES — real
// IBM Plex instead of a face that fell back to the default — so the GUI's own export moved and the
// CLI's did not, and `ConvertCliVerbTests.ConvertingAClayToGerber_WritesWhatTheApplicationsOwnExport
// Writes` failed on the label's flattened coordinates (X35400 vs X37012). The two sides had agreed
// only because BOTH were substituting. Moving the installer is what puts them back in step, on the
// real face rather than on the substitute.
//
// A MODULE INITIALIZER, unchanged in kind: it runs before any type in this assembly is touched, so
// there is no startup ordering to get wrong and no entry point to remember — and src/Ui has three of
// those (circuitRF, harmonicaRF and wBond are one assembly with different Mains), which is why it was
// one there too.
//
// ── But a module initializer is NOT enough for src/Cli, and that is a trap worth naming ───────────
//
// .NET loads an assembly on first use, and a module initializer runs when its module loads. A CLI
// verb that flattens a label — `convert` — reaches GerberExport in CircuitRF.Design and never names
// a CircuitRF.Render type at all, so this assembly is never loaded and this initializer never runs.
// The referenced-but-untouched assembly is invisible. So `Install` is PUBLIC and CliEntry calls it
// once at startup: the call is what loads the module, and it is greppable, which a load-order
// accident is not.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace CircuitRF.Render;

public static class RenderTypefaceInstaller
{
    // CA2255 warns that ModuleInitializer is meant for application code, and its concern is a
    // LIBRARY surprising its consumer with work at load time. This does the opposite of surprising:
    // it fills a seam CircuitRF.Design declares and would otherwise fall back on, it assigns one
    // delegate and touches nothing, and both consumers of this assembly want it filled. The
    // alternative — a call from each of src/Ui's three entry points and every src/Cli verb that can
    // reach a label — is the startup ordering this exists to have no part of.
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
                     Justification = "Fills a seam CircuitRF.Design declares; see the comment above.")]
    public static void Install() => LayoutTextOutline.TypefaceSource = style => style switch
    {
        // Condensed intentionally maps to the Light weight — matching SchematicRenderer's own
        // TextPrimitive.FontStyle mapping exactly, not a typo. This is the mapping that used to sit
        // inside LayoutTextOutline.ResolveTypeface and it has not changed.
        //
        // The Real* accessors, NOT the public properties: those honour
        // SkiaFonts.TestOverrideTypeface, and label GEOMETRY is compared byte for byte against a
        // separate process that has no override to read. LayoutTextOutline has its own override for
        // the tests that actually mean labels. SkiaFonts' note says the rest.
        LabelFontStyle.Bold      => SkiaFonts.RealPlexBold,
        LabelFontStyle.Italic    => SkiaFonts.RealPlexItalic,
        LabelFontStyle.Condensed => SkiaFonts.RealPlexLight,
        _                        => SkiaFonts.RealPlexRegular,
    };
}
