using System.Text.Json;
using System.Text.Json.Serialization;

namespace CircuitRF.Design.Revision;

/// <summary>One end of an interrupted restore — enough to name it in a sentence and to finish it.</summary>
/// <param name="Reference">Where the entry lives. An address, never shown.</param>
/// <param name="CommitId">What it is. An address, never shown.</param>
/// <param name="Label">What the designer would recognise it by.</param>
public sealed record RestoreEnd(
    [property: JsonPropertyName("ref")]    string Reference,
    [property: JsonPropertyName("state")]  string CommitId,
    [property: JsonPropertyName("label")]  string Label);

/// <summary>What was in flight when a restore stopped.</summary>
/// <param name="Target">Where it was going.</param>
/// <param name="Fallback">The state it was in before it started — R-rc5-12a's pre-restore entry.</param>
/// <param name="StartedUtc">When, for the report.</param>
public sealed record RestoreInFlight(
    [property: JsonPropertyName("target")]   RestoreEnd     Target,
    [property: JsonPropertyName("fallback")] RestoreEnd     Fallback,
    [property: JsonPropertyName("started")]  DateTimeOffset StartedUtc);

/// <summary>
/// <b>An interrupted restore is detected on the next open, not discovered by simulating</b>
/// (<c>docs/design/revision-control.md</c> §5.8, R-rc5-12c).
///
/// <para>A restore over thousands of files on a share can be cut off by a crash or a dropped
/// connection, and what it leaves is §1.3's failure exactly: a workspace that opens, is well-formed,
/// and is half of two states. So a marker is written before the first file and removed after the last,
/// and a marker found on open is reported — naming BOTH ends and the two ways out, because a report
/// that named only the destination would leave a designer who has changed their mind with nothing to
/// go back to.</para>
///
/// <para><b>Inside <c>.git/circuitrf/</c></b>, beside RC-6's journal: never mistaken for a design file,
/// never captured by a restore point, and gone when the history is.</para>
/// </summary>
public static class RestoreMarker
{
    public const string FileName = "restore-in-progress.json";

    /// <summary>Where the marker lives for a workspace.</summary>
    public static string PathFor(string workspaceRoot)
        => Path.Combine(workspaceRoot, ".git", ThinningJournal.DirectoryName, FileName);

    /// <summary>
    /// Writes the marker. <b>Returns false when it could not be written, and that is a refusal to
    /// proceed</b> — a restore with no marker is one whose interruption nobody can detect, which is
    /// the failure this exists to prevent rather than a detail to press on without.
    /// </summary>
    public static bool Write(string workspaceRoot, RestoreInFlight inFlight)
    {
        string path = PathFor(workspaceRoot);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(inFlight));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// What was in flight, or null when nothing was. <b>Never throws</b>.
    ///
    /// <para><b>A truncated marker is still a marker</b>, and this is the case the type exists for: a
    /// file half-written by the very crash that interrupted the restore is evidence of exactly the
    /// state being looked for. Treating an unparseable one as absent would make the marker most likely
    /// to be dropped precisely when it is most likely to be true — so a file that is there but
    /// unreadable answers with <see cref="Unreadable"/>, whose ends are blank and whose report
    /// therefore says a restore was interrupted without naming states it cannot name.</para>
    ///
    /// <para>Only an <b>absent</b> file, or one that cannot be read at all, is "nothing was in
    /// flight".</para>
    /// </summary>
    public static RestoreInFlight? Read(string workspaceRoot)
    {
        string path = PathFor(workspaceRoot);
        string text;

        try
        {
            if (!File.Exists(path)) return null;
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }

        try
        {
            return JsonSerializer.Deserialize<RestoreInFlight>(text) ?? Unreadable;
        }
        catch (JsonException) { return Unreadable; }
    }

    /// <summary>
    /// The marker that says only <i>a restore was interrupted</i> — what a truncated file resolves to.
    /// Both ends are blank, and every caller that renders one already handles a blank label, because a
    /// restore point with no label is an ordinary thing.
    /// </summary>
    public static RestoreInFlight Unreadable { get; } =
        new(new RestoreEnd("", "", ""), new RestoreEnd("", "", ""), DateTimeOffset.UnixEpoch);

    /// <summary>Removes it, after the last file.</summary>
    public static void Clear(string workspaceRoot)
    {
        try { File.Delete(PathFor(workspaceRoot)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
