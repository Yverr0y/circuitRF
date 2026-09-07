namespace CircuitRF.Design.Revision;

/// <summary>
/// The three answers to §12 Q4 <i>Refinement 3</i> — <b>three, not two</b> (RC-6 R-rc6-7a).
///
/// <para>rev 3 offered adoption as a one-click action and never said what the user was choosing
/// between. The user is asked, told what keeping their own configuration costs, and encouraged to
/// adopt circuitRF's.</para>
/// </summary>
public enum AdoptionAnswer
{
    /// <summary><b>Recommended, and said to be.</b> §4.5's configuration and §8.1a's policy files are
    /// written; normal operation from then on.</summary>
    Adopt,

    /// <summary>
    /// circuitRF keeps history in the repository under the user's own configuration. A legitimate
    /// choice, whose consequences are stated at the point of choosing.
    /// </summary>
    KeepUserSettings,

    /// <summary>The hold state, unchanged — rev 3's behaviour, still available.</summary>
    DontKeepHistory,
}

/// <summary>
/// <b>What each of R-rc6-7a's three answers actually does to the user's repository.</b>
///
/// <para><b>The line that must be drawn, or "keep my settings" is read as "do everything their
/// way":</b> the answer governs <b>repository configuration only.</b> R-rc3-1a's per-invocation set is
/// circuitRF's whichever answer is given — the commit identity, and the hook bypass. <b>Bypassing a
/// hook is not a setting the user can keep</b>, because it is not a property of the repository at all:
/// it is R-rc6-6's rule that an automatic recording must neither fire somebody's tooling nor be
/// blocked by it, and it holds in every case. Nothing here can weaken it, because it lives in
/// <see cref="GitEnvironment"/>'s per-invocation arguments and never in a config file.</para>
///
/// <para><b>The management marker is written whichever answer is given</b> (R-rc3-7b) — that is what
/// makes the question asked ONCE rather than on every open, and what lets the agent surface advertise
/// a state it can actually determine. <b>The marker is the one thing "keep my settings" does write</b>,
/// and the brief's gate 7a asks for the repository's configuration to be byte-for-byte unchanged; the
/// two cannot both be literally true, since the marker is repository configuration. The reading built
/// here is the one that keeps both requirements meaningful: <b>none of §4.5's rows is written, and none
/// of the user's own settings is touched</b> — only circuitRF's two marker keys, which exist solely so
/// nobody is asked twice. Recorded in <c>src/Design/RESOLVED.md</c>.</para>
///
/// <para><b>The ancestor row never reaches here.</b> circuitRF may not write into somebody else's
/// repository root at all, so there is nothing to offer and nothing to apply.</para>
/// </summary>
public static class RepositoryAdoption
{
    /// <summary>
    /// Applies one answer to the repository at <paramref name="git"/>'s workspace root.
    ///
    /// <para>Refuses outright anywhere the question should not have been asked — a repository circuitRF
    /// already manages, or a workspace that is not the repository root. Both are programming errors
    /// rather than user states, and writing configuration into somebody's repository on the strength of
    /// a mis-routed call is exactly the ambush R-rc6-6 exists to prevent.</para>
    /// </summary>
    public static RevisionOutcome Apply(GitCommand git, AdoptionAnswer answer)
    {
        if (!git.IsRepositoryRoot())
            return RevisionOutcome.Failed(GitFailures.Unrecognised(
                "setting up this workspace's history",
                "this workspace is not the root of the repository it sits in"));

        return answer switch
        {
            // Everything §4.5 configures, the policy files, and the marker. GitRepository.Create is
            // idempotent on an existing repository and is deliberately the same function circuitRF's
            // own creation path calls — a second implementation of "what a managed repository looks
            // like" is how an adopted repository ends up configured differently from a created one.
            AdoptionAnswer.Adopt => GitRepository.Create(git, RevisionManagement.Adopted),

            // The marker ONLY. No §4.5 rows, no .gitignore, no .gitattributes.
            AdoptionAnswer.KeepUserSettings =>
                GitRepository.WriteMarker(git, RevisionManagement.KeptUserSettings),

            _ => GitRepository.WriteMarker(git, RevisionManagement.Declined),
        };
    }

    /// <summary>
    /// The consequences of <see cref="AdoptionAnswer.KeepUserSettings"/>, <b>named specifically rather
    /// than generally</b> — each of §4.5's rows exists to prevent one particular failure, and the first
    /// two silently revoke guarantees circuitRF makes elsewhere, which is why they lead.
    ///
    /// <para>Read from <see cref="GitRepositoryConfig"/> rather than transcribed, so a row added there
    /// cannot go unmentioned here.</para>
    /// </summary>
    public static IReadOnlyList<string> KeepingCosts() =>
    [
        "A restore point circuitRF tidies away is destroyed permanently after a fortnight, rather than "
      + "staying recoverable for as long as you leave it — which is inside the few weeks it usually "
      + "takes to notice that something was wrong.",

        "The way back from a mistaken reset or rewrite of your own work disappears after thirty days.",

        "circuitRF's tidying-up and your own git's run against each other rather than together.",

        OperatingSystem.IsWindows()
            ? "On this machine, git's end-of-line conversion makes a saved layout a different file "
            + "from the one circuitRF wrote, and a long path is refused."
            : "circuitRF cannot guarantee a saved layout reaches disk byte-for-byte as it wrote it on "
            + "every machine that opens this workspace.",
    ];
}
