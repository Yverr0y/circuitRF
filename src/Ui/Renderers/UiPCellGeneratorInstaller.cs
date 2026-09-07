// Hands CircuitRF.Render the built-in PCell generators a generated cell's pins are re-derived from.
//
// CellPins moved to CircuitRF.Render with the renderer that reads it (R-rnd1-3): the layout renderer
// draws a sub-cell instance's pin markers through it, and so do LayoutPortDirection and
// LayoutSnapFeatures, which the renderer also reads. What it could NOT take with it is
// PCellRegistry — behind that sit the six built-in generators, the out-of-process worker transports,
// the Python interpreter discovery, and PCellTrust, which reads a per-USER preference through
// AppPreferencesIo. A per-user preference store is src/Ui's business (R-rnd1-3's table says so of
// AppPreferences explicitly), so the registry stays here and reaches down through the seam instead.
//
// A MODULE INITIALIZER for RenderTypefaceInstaller's reason, stated once there: it runs before any type
// in this assembly is touched, so there is no startup ordering to get wrong and no second entry
// point (the standalone harmonicaRF and wBond binaries are this same assembly with a different Main)
// to remember. Every frame the application draws therefore resolves generators exactly as it did
// before the move.
//
// Unset — which is what a headless process gets — only the SECOND branch of CellPins.Resolve is
// lost: a generated cell's pins are persisted in its own cell folder, so the first branch answers
// for every such cell written since. See CellPins.GeneratorSource.

using System.Runtime.CompilerServices;
using CircuitRF.Ui.Layout.PCells;

namespace CircuitRF.Ui.Renderers;

internal static class UiPCellGeneratorInstaller
{
    [ModuleInitializer]
    internal static void Install() => CellPins.GeneratorSource =
        id => PCellRegistry.TryGet(id, out var generator) ? generator : null;
}
