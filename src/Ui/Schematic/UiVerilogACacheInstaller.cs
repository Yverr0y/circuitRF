// Hands CircuitRF.Design the one thing VerilogAModelIntrospection could not bring across the UI
// firewall: WHERE this installation keeps its per-user cache.
//
// VerilogAModelIntrospection moved to CircuitRF.Design with the schematic document model
// (brief-automation-2-schematic-below-the-firewall.md) because a component's terminal labels are part
// of reading the schematic. AppDataRoot did not go with it — it is the single lever tools/DocGen
// redirects to make a capture reproducible, and it is also read by the updater, the crash reporter and
// preferences, none of which this brief moves. So the directory is supplied from here instead of
// recomputed there, which is the whole point of AppDataRoot existing.
//
// A MODULE INITIALIZER, for the same reason UiTypefaceInstaller is one: it runs before any type in
// this assembly is touched, so there is no startup ordering to get wrong and no second entry point
// (the standalone harmonicaRF and wBond binaries are this same assembly with a different Main) to
// remember. It reads AppDataRoot LAZILY, per call, so a later redirect still takes effect.

using System.Runtime.CompilerServices;

namespace CircuitRF.Ui.Schematic;

internal static class UiVerilogACacheInstaller
{
    [ModuleInitializer]
    internal static void Install() =>
        VerilogAModelIntrospection.CacheDirectory = static () => AppDataRoot.SubDir("cache");
}
