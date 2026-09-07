namespace CircuitRF.Design.Revision;

/// <summary>
/// Whether circuitRF keeps a history for a given workspace — <b>one function, because two switches
/// answer two different questions and the precedence between them is the thing that gets got wrong</b>
/// (<c>docs/design/revision-control.md</c> §5.7a, §12 Q13; RC-4 R-rc4-12a).
///
/// <para><b>The per-USER preference answers "do I want this at all".</b> It ships ON — §12 Q5 puts the
/// floor in before the capability arrives, and a floor that has to be found in a settings tab is not
/// one. The population it reaches is already narrow, because §4.3 makes the whole feature invisible to
/// anyone without git, so the default only ever applies to somebody who installed git deliberately.</para>
///
/// <para><b>The per-WORKSPACE flag answers "not for this one", and it outranks the preference.</b> It
/// lives in the <c>.cws</c> (<see cref="CircuitRF.Design.Workspace.CwsFile.RevisionControl"/>) and NOT
/// in <c>AppPreferences</c>, because §5.7's rule stands untouched: an installation-wide flag cannot
/// gate per-workspace state — it is correct for the first workspace and silently wrong for the second,
/// which is a mistake this repository has already made once and recorded (<c>src/Ui/RESOLVED.md</c>,
/// the wirebond group work, where a per-installation flag failed on the second workspace as floating
/// panels). The preference is not that flag; it is the value the flag falls back to.</para>
///
/// <para><b>The case that is easy to get wrong is the fourth one</b> (RC-4 gate 6a): a workspace that
/// RECORDED a setting keeps it when the preference changes. A per-workspace decision silently rewritten
/// by a global one is the same class of failure as §4.4's identity — per-user state deciding something
/// about a shared artifact — and it is why "absent" and "false" are different states here rather than
/// both being <c>false</c>.</para>
///
/// <para><b>This says nothing about WHEN a workspace is armed.</b> §5.7a's guard — armed at the first
/// boundary that would record something, never at open — is RC-5's, and this function is what it
/// consults. Being armed is not the same as having a repository.</para>
/// </summary>
public static class RevisionArming
{
    /// <summary>
    /// The documented default of the per-user preference: <b>on</b> (§5.7a). Named rather than written
    /// as a literal at each reader, so a test can assert against the DOCUMENTED default instead of
    /// against a transcription of whatever the writer happens to do.
    /// </summary>
    public const bool KeepHistoryDefault = true;

    /// <summary>
    /// Whether this workspace is armed.
    /// </summary>
    /// <param name="keepHistoryPreference">
    /// The per-user preference. Pass <see cref="KeepHistoryDefault"/> where none is recorded.
    /// </param>
    /// <param name="workspaceSetting">
    /// What the <c>.cws</c> recorded, or <c>null</c> when it has never recorded one — which is the
    /// state that falls back, and is why this is nullable rather than a bool with a default.
    /// </param>
    public static bool IsArmed(bool keepHistoryPreference, bool? workspaceSetting)
        => workspaceSetting ?? keepHistoryPreference;
}
