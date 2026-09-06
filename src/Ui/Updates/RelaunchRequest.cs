using System.Threading.Tasks;

namespace CircuitRF.Ui.Updates;

/// <summary>
/// The one seam between "an update is installed and waiting" and "shut this application down and
/// start the new one".
///
/// <para><b>Why a hook rather than a direct call.</b> <see cref="UpdateService"/> decides that a
/// relaunch is worth OFFERING; only <c>App</c> can carry one out, because it owns the windows that
/// have to be asked about unsaved work first. A direct call from the service to <c>App</c> would
/// also make the service unconstructable in a test host, where there is no application object at
/// all — and the service is covered by a great many tests that must keep running headless.</para>
///
/// <para><b>Unset means the offer is not made</b>, which is the correct behaviour rather than a
/// degraded one: harmonicaRF and wBond share this update machinery, and a build with no handler
/// installed simply posts the announcement with no button, exactly as it did before this existed.</para>
/// </summary>
public static class RelaunchRequest
{
    /// <summary>
    /// Installed by <c>App</c> at startup. Returns when the relaunch has been ARRANGED or REFUSED —
    /// not when the new process is up, which this process will not be alive to see.
    /// </summary>
    public static System.Func<Task>? Handler { get; set; }

    /// <summary>True when a relaunch can actually be offered — the only test any caller should make.</summary>
    public static bool IsAvailable => Handler is not null;
}
