using System.Text.Json;
using System.Text.Json.Serialization;

namespace CircuitRF.Design.Revision;

/// <summary>What a restore left behind, so the commit that follows can say where the content came
/// from.</summary>
/// <param name="Label">The restore point's own line, as the designer read it when they chose it.</param>
/// <param name="TakenUtc">When that state was kept — what tells two identically-labelled entries
/// apart.</param>
public sealed record RestoredState(
    [property: JsonPropertyName("label")] string         Label,
    [property: JsonPropertyName("taken")] DateTimeOffset TakenUtc);

/// <summary>
/// <b>The one piece of state a restore leaves for the next commit</b>
/// (<c>docs/design/revision-control.md</c> §5.5, §6.3; RC-7 R-rc7-6, R-rc7-16).
///
/// <para><b>Why anything is recorded at all.</b> R-rc7-16 makes restore-then-keep-editing linear:
/// nothing branches, <c>HEAD</c> never moves, and the next commit records the restored content as the
/// next step on the same line of work. That is the right shape and it has one cost — <b>from the
/// content alone, a commit that went back to Tuesday is indistinguishable from a commit that undid
/// three days of work by hand.</b> The first is a decision the designer would want recorded; the
/// second is a different decision entirely. So the restore says which it was, and the commit that
/// follows reads it.</para>
///
/// <para><b>It cannot be derived, which is why it is written down.</b> Comparing the new tree against
/// every restore point would find a match for the first commit after a restore and none at all after
/// one more edit — so the line would appear or vanish depending on how much work happened in between,
/// which is the least predictable behaviour available.</para>
///
/// <para><b>The LAST restore wins.</b> Restore to A, then to B, then commit: the content is B's, so
/// naming A would be a sentence that is simply untrue. Each restore overwrites this.</para>
///
/// <para><b>Cleared by the commit that reports it</b>, and by nothing else. The next commit after that
/// one carries no line, because by then the content is the designer's own work rather than a state
/// brought back — and a line repeated on every commit from then on would say nothing.</para>
///
/// <para><b>Inside <c>.git/circuitrf/</c></b>, beside <see cref="RestoreMarker"/> and RC-6's journal:
/// never mistaken for a design file, never captured by a restore point, and gone when the history is.
/// It is deliberately NOT in the <c>.cws</c> — a designer's own workspace file must not gain a field
/// that changes on every restore, and one that travelled with a Save As copy would put a sentence
/// about this machine's history into somebody else's.</para>
/// </summary>
public static class RestoreProvenance
{
    public const string FileName = "restored-from.json";

    /// <summary>Where it lives for a workspace.</summary>
    public static string PathFor(string workspaceRoot)
        => Path.Combine(workspaceRoot, ".git", ThinningJournal.DirectoryName, FileName);

    /// <summary>
    /// Records what a restore just brought the tree back to.
    ///
    /// <para><b>A failure here is not a reason to fail the restore.</b> The restore itself succeeded
    /// and the workspace holds what was asked for; all that is lost is one line in a commit message
    /// that may never be written. Refusing the restore over it would trade the whole operation for a
    /// sentence.</para>
    /// </summary>
    public static bool Write(string workspaceRoot, RestoredState state)
    {
        string path = PathFor(workspaceRoot);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(state));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>What the tree was restored from since the last commit, or null. <b>Never throws</b>:
    /// an unreadable file means the line is not written, which is the same outcome as no restore
    /// having happened and is the safe direction to fail in.</summary>
    public static RestoredState? Read(string workspaceRoot)
    {
        try
        {
            string path = PathFor(workspaceRoot);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<RestoredState>(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Removes it, after the commit that named it succeeded.</summary>
    public static void Clear(string workspaceRoot)
    {
        try { File.Delete(PathFor(workspaceRoot)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
