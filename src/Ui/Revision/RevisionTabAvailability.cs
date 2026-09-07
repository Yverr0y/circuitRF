namespace CircuitRF.Ui.Revision;

/// <summary>
/// Whether Settings ▸ Revision Control is USABLE on this machine — <b>never whether it exists</b>
/// (<c>docs/design/revision-control.md</c> §4.3, §10A; owner, 2026-09-07).
///
/// <para><b>The tab is always shown, and that is a change from §4.3 as first written.</b> rev 5 hid
/// the whole tab on a machine with no usable git, on the reasoning that absence should be silent: a
/// designer who does not want a history should never learn the feature exists. The owner's decision
/// reverses the trade for this one surface. Hiding it also hides the fact that circuitRF <i>can</i>
/// keep a history and is not keeping one here — which is the same false belief §1.4 is written
/// against, reached from the other side: the designer who would have wanted a history cannot discover
/// that the only thing missing is a program they could install in five minutes.</para>
///
/// <para><b>Disabled is the honest middle.</b> The rows are visible, so what circuitRF would keep is
/// legible; they are greyed, so nobody believes anything is being kept. The one row that stays live is
/// the git path and its Detect, because that row is the remedy — it is how somebody with git in an
/// unusual location, or none at all, finds out what circuitRF sees and fixes it. A disabled Detect
/// would leave a machine that cannot answer its own question.</para>
///
/// <para><b>This is deliberately NOT RC-6's hold state</b>, which is also visible-and-refusing but
/// means something else: held is a repository circuitRF may not write to, and the designer may believe
/// they are protected. Here nothing has been promised at all — the tab explains that, in one sentence,
/// where the controls are.</para>
///
/// <para><b>The git-path row's second host is gone with the hiding rule that created it.</b> It existed
/// only so that hiding the tab did not also hide the one field that makes an unusually-located git
/// findable; with the tab always present there is exactly one host, and a permanently-invisible copy of
/// a control on Security &amp; Permissions would be a row nobody could ever reach.</para>
/// </summary>
public static class RevisionTabAvailability
{
    /// <summary>
    /// The tab exists on every machine. A named constant rather than a deleted condition, so the rule
    /// is checkable and so the next reader finds the reasoning above rather than an absence.
    /// </summary>
    public const bool TabIsAlwaysShown = true;

    /// <summary>
    /// Whether everything on the tab EXCEPT the git-path row may be used.
    /// </summary>
    /// <param name="gitAvailable">
    /// Whether discovery found a usable git — which already means "at or above circuitRF's version
    /// floor", because an under-floor git counts as absent everywhere but the Detect line itself.
    /// </param>
    public static bool ControlsEnabled(bool gitAvailable) => gitAvailable;

    /// <summary>
    /// The sentence shown in place of the controls when there is none. <b>It states what is not
    /// happening before it states the remedy</b>, because the fact a designer needs first is that
    /// nothing is being kept.
    /// </summary>
    public const string NoGitNotice =
        "circuitRF keeps a history by running git, and there is no usable git on this machine — so "
      + "nothing below is in use and no history is being kept for any workspace. Name a git above, or "
      + "install one and press Detect, and these settings become available.";
}
