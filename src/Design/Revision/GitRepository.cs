using CircuitRF.Diagnostics;

namespace CircuitRF.Design.Revision;

/// <summary>What an operation on a repository produced.</summary>
/// <param name="Ok">Whether it did what was asked.</param>
/// <param name="Diagnostic">The translated failure, or null. Info-severity on some successes.</param>
/// <param name="CommitId">
/// What the entry's identity is now, for the operations that REWRITE a commit rather than add one —
/// a rename, a keep. Empty everywhere else. A caller holding a copy of the old title needs it to keep
/// its link to the entry alive; without it, that link goes stale silently and a corrected title starts
/// leaking again on the second correction rather than the first.
/// </param>
public sealed record RevisionOutcome(bool Ok, Diagnostic? Diagnostic = null, string CommitId = "")
{
    public static readonly RevisionOutcome Success = new(true);
    public static RevisionOutcome Failed(Diagnostic d) => new(false, d);
}

/// <summary>
/// Creating and configuring the one repository circuitRF writes to — the one whose root IS the open
/// workspace (R-rc0-5, §7A.1). Not an ancestor, not a referenced workspace, not a nested one.
///
/// <para><b>Creation is not automatic in this brief</b> (R-rc3-8). RC-3 provides the operation; RC-4's
/// settings and RC-5's first checkpoint decide when it runs. A repository appearing in a folder because
/// the user launched circuitRF once is a surprise, and RC-6 owns the detection that makes "may I?"
/// answerable.</para>
/// </summary>
public static class GitRepository
{
    /// <summary>
    /// Creates a repository at <paramref name="git"/>'s workspace root, configures it (§4.5), writes
    /// the management marker and the policy files.
    ///
    /// <para>Idempotent on an existing repository: the config and the marker are (re)written, the
    /// policy files are APPENDED to if circuitRF's block is not already there. That is what makes this
    /// the same function RC-6's adoption calls — <b>a repository circuitRF created has circuitRF's
    /// policy files</b>, and adoption is the moment the user said yes.</para>
    /// </summary>
    public static RevisionOutcome Create(GitCommand git, RevisionManagement management)
    {
        if (!Directory.Exists(git.WorkspaceRoot))
            return RevisionOutcome.Failed(GitFailures.Unrecognised(
                "starting a history for this workspace", $"'{git.WorkspaceRoot}' is not a folder"));

        if (!git.IsRepositoryRoot())
        {
            // --initial-branch is deliberately not passed: it is git 2.28 and would raise R-rc3-3a's
            // floor for a name nothing in this design ever shows (R-rc0-6 — no git vocabulary reaches
            // a designer, and "branch" appears in no user-visible string in RC-1 … RC-6).
            var init = git.Run(["init", "--quiet", "--", git.WorkspaceRoot]);
            if (!init.Ok)
                return RevisionOutcome.Failed(
                    GitFailures.Translate(init, "starting a history for this workspace", git.WorkspaceRoot));
        }

        if (Configure(git, management) is { Ok: false } configFailure) return configFailure;

        WorkspacePolicyFiles.Ensure(git.WorkspaceRoot);
        return RevisionOutcome.Success;
    }

    /// <summary>Writes every row of §4.5's table plus the marker. Separate from <see cref="Create"/> so
    /// RC-6's adoption of an existing repository can write them without an <c>init</c>.</summary>
    public static RevisionOutcome Configure(GitCommand git, RevisionManagement? management = null)
    {
        foreach (var (key, value, _) in GitRepositoryConfig.RowsForThisPlatform())
        {
            // --local, explicitly: the default scope for a write is already local, but this is the one
            // place in circuitRF that writes any git config at all, and "never the user's global" is
            // the rule most worth making unmistakable at the call site.
            var r = git.Run(["config", "--local", key, value]);
            if (!r.Ok)
                return RevisionOutcome.Failed(
                    GitFailures.Translate(r, "setting up this workspace's history", git.WorkspaceRoot));
        }

        if (management is { } answer) return WriteMarker(git, answer);
        return RevisionOutcome.Success;
    }

    /// <summary>
    /// R-rc3-7b. <b>"Did circuitRF create this repository" is load-bearing three times and a
    /// <c>.git</c> directory alone cannot answer it</b> — a user may perfectly well have written
    /// <c>.git</c>, <c>.gitignore</c> and <c>.gitattributes</c> themselves.
    ///
    /// <para><b>It is REPOSITORY configuration, and that placement produces exactly the two behaviours
    /// wanted.</b> An ARCHIVE carries it, because §9A copies the directory — so RC-8's extracted
    /// archive is recognised as circuitRF's without ceremony. A CLONE does not, because git does not
    /// clone a repository's config — so a clone reaches R-rc0-13's arming path on its own terms. A
    /// marker held in a REFERENCE instead would be pushed, carrying one designer's management decision
    /// into everybody else's clone.</para>
    ///
    /// <para>It records the ANSWER rather than merely circuitRF's presence, which is what stops RC-6
    /// asking the question on every open.</para>
    /// </summary>
    public static RevisionOutcome WriteMarker(GitCommand git, RevisionManagement management)
    {
        var a = git.Run(["config", "--local", GitRepositoryConfig.MarkerManagedKey, "true"]);
        if (!a.Ok)
            return RevisionOutcome.Failed(
                GitFailures.Translate(a, "setting up this workspace's history", git.WorkspaceRoot));

        var b = git.Run(["config", "--local", GitRepositoryConfig.MarkerAnswerKey, management.ToString()]);
        if (!b.Ok)
            return RevisionOutcome.Failed(
                GitFailures.Translate(b, "setting up this workspace's history", git.WorkspaceRoot));

        return RevisionOutcome.Success;
    }

    /// <summary>The recorded answer, or null when there is no marker — i.e. this is not a repository
    /// circuitRF manages, whatever else is in the folder.</summary>
    public static RevisionManagement? ReadMarker(GitCommand git)
    {
        var managed = git.Run([
            "config", "--local", "--get", GitRepositoryConfig.MarkerManagedKey],
            new GitRunOptions(ReadOnly: true));
        if (!managed.Ok || !string.Equals(managed.Line, "true", StringComparison.OrdinalIgnoreCase))
            return null;

        var answer = git.Run([
            "config", "--local", "--get", GitRepositoryConfig.MarkerAnswerKey],
            new GitRunOptions(ReadOnly: true));

        return answer.Ok && Enum.TryParse<RevisionManagement>(answer.Line, ignoreCase: true, out var parsed)
            ? parsed
            : RevisionManagement.Created;   // an old marker with no answer recorded predates the question
    }

    /// <summary>Whether circuitRF manages this repository — the question §12 Q4, §9A.5 and RC-5's
    /// agent-facing state all turn on.</summary>
    public static bool IsManagedByCircuitRf(GitCommand git) => ReadMarker(git) is not null;
}
