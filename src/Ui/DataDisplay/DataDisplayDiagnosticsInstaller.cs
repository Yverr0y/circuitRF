// Fills CircuitRF.Render.DataDisplay's one upward seam: a failed trace resolve goes on the crash
// trail. Same shape and same reason as RenderTypefaceInstaller — a module initializer, because
// src/Ui has three entry points (circuitRF, harmonicaRF, wBond are one assembly with three Mains)
// and an installer called from one of them is an installer the other two silently do without.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CircuitRF.Ui.Diagnostics;

namespace CircuitRF.Ui.DataDisplay;

internal static class DataDisplayDiagnosticsInstaller
{
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
                     Justification = "Fills a seam CircuitRF.Render.DataDisplay declares; see the comment above.")]
    internal static void Install() =>
        DataDisplayDiagnostics.NoteSink = CrashReporter.Note;
}
