using CircuitRF.Design.Workspace;
using CircuitRF.Diagnostics;

namespace CircuitRF.Design.Revision;

/// <summary>What <c>git count-objects -v</c> says the repository is carrying.</summary>
/// <param name="LooseCount">Loose objects — git's own trigger, and the one that does not work here.</param>
/// <param name="LooseBytes">Their total size. <b>This is the number that matters.</b></param>
/// <param name="PackedCount">Objects already in packs.</param>
/// <param name="PackedBytes">The packs' size.</param>
public sealed record RepositorySize(long LooseCount, long LooseBytes, long PackedCount, long PackedBytes);

/// <summary>Why a pack did not run, or that it did.</summary>
public enum PackOutcome
{
    /// <summary>It ran to completion.</summary>
    Packed,
    /// <summary>Below the threshold. Nothing to do, and nothing was said.</summary>
    NotNeeded,
    /// <summary>Somebody else has the workspace open — packing YIELDS rather than queues.</summary>
    Yielded,
    /// <summary>Abandoned. An abandoned pack leaves a correct repository.</summary>
    Cancelled,
    /// <summary>git refused. The diagnostic says what happened.</summary>
    Failed,
}

/// <summary>
/// <b>Packing, which is the real cost and which nothing else runs</b> (<c>revision-control.md</c> §2.4,
/// R-rc3-13 … R-rc3-17).
///
/// <para><b>The finding this exists for.</b> Between commits git writes each new version of a file as a
/// LOOSE OBJECT: a standalone zlib-compressed copy of the ENTIRE blob, with no delta against anything.
/// Measured on a real 28.4 MB board: 21 commits with no <c>gc</c> is <b>63 loose objects and 124.3
/// MB</b>; the same repository after one <c>gc</c> is <b>12 MB</b>. A tenfold overhang, and
/// <b>it does not clear itself</b> — git's <c>gc --auto</c> triggers on <c>gc.auto</c>, whose default is
/// 6,700 loose objects, a COUNT with no notion of size. A workspace whose history is a handful of
/// enormous files sits at a few dozen loose objects indefinitely, hundreds of megabytes over its packed
/// size, and git never once decides to do anything about it. That is exactly the shape of complaint the
/// feature exists to avoid.</para>
///
/// <para><b>So circuitRF owns packing, on a byte-based trigger</b> (R-rc3-13), with <c>gc.auto = 0</c>
/// in the repository so the two schedulers cannot fight. <b>It runs where it cannot be noticed</b>
/// (R-rc3-14) — after a workspace closes, or idle; never in front of a save or a checkpoint, because
/// packing 124 MB is seconds of CPU and heavy I/O. <b>It is interruptible and safe to abandon</b>
/// (R-rc3-15), which is part of why <c>git gc</c> is the right tool.</para>
///
/// <para><b>And it never prunes.</b> No code path here passes <c>--prune</c>, and that was never
/// sufficient on its own: <c>git gc</c> prunes at <c>gc.pruneExpire</c> (two weeks) and expires reflogs
/// at 90/30 days, all unasked and all invisible. The guarantee lives in
/// <see cref="GitRepositoryConfig"/>'s three <c>never</c> rows. Reclaiming is a separate, explicit,
/// person-initiated operation — <see cref="GitReclaim"/>.</para>
/// </summary>
public static class GitPacking
{
    /// <summary>
    /// The default trigger: loose-object bytes over this and the workspace is worth packing when it
    /// next closes.
    ///
    /// <para><b>A constant in this brief and a setting in RC-4</b> (R-rc3-17). Ordinary users never
    /// touch it; it exists because §2.4 means somebody eventually asks where the disk went. 64 MB sits
    /// well below the 124 MB overhang §2.4 measured after 21 commits on one real board, and well above
    /// the few megabytes a text-only design workspace ever accumulates — so a schematic-only workspace
    /// packs approximately never and a large-layout workspace packs every few sessions.</para>
    /// </summary>
    public const long DefaultThresholdBytes = 64L * 1024 * 1024;

    /// <summary>
    /// What the repository is carrying, from <c>count-objects -v</c>. A READ: it must not wait behind a
    /// writer (R-rc3-1b).
    /// </summary>
    public static RepositorySize? Measure(GitCommand git)
    {
        var r = git.Run(["count-objects", "-v"], new GitRunOptions(ReadOnly: true));
        if (!r.Ok) return null;

        long count = 0, size = 0, inPack = 0, sizePack = 0;
        foreach (string line in r.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string key = line[..colon].Trim();
            if (!long.TryParse(line[(colon + 1)..].Trim(), out long value)) continue;

            switch (key)
            {
                // `size` and `size-pack` are in KiB — a unit that is easy to read past, and reading it
                // past is a threshold three orders of magnitude wrong in the harmless direction.
                case "count":     count    = value;               break;
                case "size":      size     = value * 1024;        break;
                case "in-pack":   inPack   = value;               break;
                case "size-pack": sizePack = value * 1024;        break;
            }
        }

        return new RepositorySize(count, size, inPack, sizePack);
    }

    /// <summary>Whether the byte trigger has fired.</summary>
    public static bool IsWorthPacking(GitCommand git, long thresholdBytes = DefaultThresholdBytes)
        => Measure(git) is { } size && size.LooseBytes >= thresholdBytes;

    /// <summary>
    /// Packs the repository, if it is worth it and if nobody else has the workspace open.
    ///
    /// <para><b>Packing yields to the advisory notice, and that is the notice used AS a notice</b>
    /// (R-rc3-14, §4.6). <see cref="WorkspaceLock"/>'s own header is explicit that it holds no file
    /// handle and must never be treated as authoritative — so it is not what keeps two WRITERS apart
    /// (git's own atomic lock files and a private index do that). But "is anyone else in here" is
    /// exactly the question it answers, and packing is exactly the operation that should not start
    /// while somebody is, and should abandon rather than queue when somebody arrives.</para>
    /// </summary>
    /// <param name="ct">Cancel freely. An abandoned <c>git gc</c> leaves a correct repository, and the
    /// operation can simply be run again (R-rc3-15).</param>
    public static (PackOutcome Outcome, Diagnostic? Diagnostic) Pack(
        GitCommand git, long thresholdBytes = DefaultThresholdBytes, CancellationToken ct = default)
    {
        if (!IsWorthPacking(git, thresholdBytes)) return (PackOutcome.NotNeeded, null);

        // A stale notice is not somebody, and neither is our own — the notice this process wrote when
        // it opened the workspace is not a reason to refuse to tidy it up on the way out.
        if (WorkspaceLock.Read(git.WorkspaceRoot) is { } held
            && !WorkspaceLock.IsOurs(held) && !WorkspaceLock.IsStale(held))
            return (PackOutcome.Yielded, null);

        // NO --prune, and no --aggressive: aggressive is minutes of CPU for a one-off gain nothing here
        // needs, and it is exactly the wrong thing to start on a machine somebody is trying to close.
        var r = git.Run(["gc", "--quiet"],
                        new GitRunOptions(Timeout: GitCommand.PackTimeout), ct);

        if (r.Cancelled) return (PackOutcome.Cancelled, null);
        if (!r.Ok)
            return (PackOutcome.Failed,
                    GitFailures.Translate(r, "tidying up this workspace's history", git.WorkspaceRoot));

        return (PackOutcome.Packed, null);
    }
}
