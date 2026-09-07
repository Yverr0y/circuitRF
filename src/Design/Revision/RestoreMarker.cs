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
    /// What was in flight, or null when nothing was. <b>Never throws</b>: a truncated marker is a
    /// marker, so it is reported rather than dropped — a file half-written by the same crash is
    /// evidence of exactly the state this is looking for.
    /// </summary>
    public static RestoreInFlight? Read(string workspaceRoot)
    {
        string path = PathFor(workspaceRoot);
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<RestoreInFlight>(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Removes it, after the last file.</summary>
    public static void Clear(string workspaceRoot)
    {
        try { File.Delete(PathFor(workspaceRoot)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
