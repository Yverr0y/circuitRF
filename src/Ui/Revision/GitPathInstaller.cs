// Hands CircuitRF.Design the one thing git discovery needs from the application: which git the user
// named in Settings ▸ Revision Control.
//
// The preference lives above the firewall in AppPreferences and CircuitRF.Design may not reach it, so
// GitDiscovery exposes the seam and this fills it — exactly as VerilogACompilerInstaller fills
// VerilogACompilerDiscovery's, and for the same reason. The two features are the same shape: a program
// circuitRF starts as a subprocess, blank meaning "search PATH", and a named path outranking PATH.
//
// A MODULE INITIALIZER for the same reason as that one: it runs before any type in this assembly is
// touched, so there is no startup ordering to get wrong and no second entry point to remember — the
// standalone harmonicaRF and wBond binaries are this same assembly with a different Main, and both host
// the settings sub-view that writes this preference.
//
// Unset — which is what a headless `circuitrf` process gets — GitDiscovery falls back to CRF_GIT and
// then to PATH. That is deliberate and is not a gap: src/Cli has no preferences dialog, and the
// identity, which IS needed headlessly, is read from the preferences FILE by a src/Design reader rather
// than through this seam.

using System.Runtime.CompilerServices;
using CircuitRF.Design.Revision;
using CircuitRF.Ui.Theming;

namespace CircuitRF.Ui.Revision;

internal static class GitPathInstaller
{
    [ModuleInitializer]
    internal static void Install()
        // Read through the Func on every call rather than captured once: a user who names a git in
        // Settings and asks Detect again must get the one they just named. GitDiscovery memoises its
        // answer KEYED on this value, so changing it invalidates nothing else.
        => GitDiscovery.PreferredPath = () => AppPreferencesIo.Load().RevisionGitPath;
}
