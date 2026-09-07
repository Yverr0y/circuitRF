namespace CircuitRF.Ui.Revision;

/// <summary>
/// Whether Settings ▸ Revision Control exists on this machine (§4.3, RC-4 R-rc4-3).
///
/// <para><b>A named decision rather than a condition inline in a dialog</b>, because it is the rule
/// §4.3 is made of and it has to be checkable without rendering a window: "the tab is absent AND the
/// path field is still reachable" is one of RC-4's gates, and the exception is the whole point of the
/// rule.</para>
///
/// <para><b>Hidden, not disabled.</b> Absence is silent — a designer who does not want a history should
/// never learn the feature exists. That is deliberately the opposite of RC-6's HOLD state, where the
/// affordances stay VISIBLE and refuse: absent is harmless, while a hidden control that refuses is
/// indistinguishable from a feature that was never built, and a designer who believes they are
/// protected and is not is the failure the whole document guards against.</para>
/// </summary>
public static class RevisionTabVisibility
{
    /// <summary>
    /// The tab is shown when there is a usable git, <b>or</b> when the user has named a path — even one
    /// that does not resolve.
    /// </summary>
    /// <param name="pathConfigured">
    /// Whether the user has named a git in Settings. <b>This is what makes a WRONG path recoverable:</b>
    /// somebody who named a git and got it wrong needs to see the field they got wrong and the Detect
    /// line that says why, and hiding the tab because their answer did not resolve would leave them with
    /// no way back.
    /// </param>
    /// <param name="gitAvailable">
    /// Whether discovery found a usable git — which already means "at or above circuitRF's version
    /// floor", because an under-floor git counts as absent everywhere but the Detect line itself.
    /// </param>
    public static bool ShouldShowTab(bool pathConfigured, bool gitAvailable)
        => pathConfigured || gitAvailable;

    /// <summary>
    /// Whether the git-path row appears in its FALLBACK host, Security &amp; Permissions.
    ///
    /// <para>Exactly when its own tab is not there, and never at the same time: it is one control with
    /// two hosts, and two identical rows in one dialog is worse than either placement. This is
    /// R-rc4-3's single exception — hiding everything would hide the one field that makes a git in an
    /// unusual location findable in the first place, so the field survives its own tab and lands on the
    /// tab that already answers "what may circuitRF RUN".</para>
    /// </summary>
    public static bool ShouldShowPathFallback(bool pathConfigured, bool gitAvailable)
        => !ShouldShowTab(pathConfigured, gitAvailable);
}
