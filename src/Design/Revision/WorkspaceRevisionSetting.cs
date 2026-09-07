using CircuitRF.Design.Workspace;

namespace CircuitRF.Design.Revision;

/// <summary>
/// Reads and writes <see cref="CwsFile.RevisionControl"/> — the per-workspace half of RC-4's two
/// switches (<c>docs/design/revision-control.md</c> §5.7, §5.7a; R-rc4-12).
///
/// <para><b>It exists so there is ONE place, not because the read is hard.</b> The settings tab writes
/// it, RC-5's arming boundary reads it and <c>src/Cli</c> reads it, and a second copy of
/// "load the <c>.cws</c>, look at one field" is how a per-workspace setting starts being honoured in
/// two places out of three.</para>
///
/// <para><b>Read-modify-write against the file, every time</b> — the idiom every other <c>.cws</c>
/// writer in this repository already uses. The alternative, holding the open workspace's in-memory
/// <c>CwsFile</c> and writing that, loses whatever another writer recorded between the load and the
/// save; here the window between the two is a few milliseconds and covers exactly one field.</para>
///
/// <para><b>A read never throws.</b> A missing, unreadable or malformed <c>.cws</c> answers
/// <c>null</c> — "this workspace has never recorded a setting" — which falls back to the preference.
/// The alternative would let a damaged file decide whether a safety net exists.</para>
/// </summary>
public static class WorkspaceRevisionSetting
{
    /// <summary>The <c>.cws</c> inside a workspace folder.</summary>
    public static string CwsPathFor(string workspaceDirectory)
        => Path.Combine(workspaceDirectory, WorkspacePersistence.FileName);

    /// <summary>
    /// What this workspace recorded, or null when it has recorded nothing.
    /// </summary>
    /// <param name="cwsPath">The <c>.cws</c> itself, not its folder.</param>
    public static bool? Read(string cwsPath)
    {
        try
        {
            return File.Exists(cwsPath) ? WorkspacePersistence.LoadFromFile(cwsPath).RevisionControl : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
                                    or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Records this workspace's own setting, or clears it back to "falls back to the preference" with
    /// <paramref name="value"/> null.
    ///
    /// <para>Returns false when nothing was written — no <c>.cws</c> there, or a read-only workspace,
    /// which <see cref="WorkspacePersistence.SaveToFileAtomic"/> refuses silently by design. The caller
    /// is the one place that knows whether that is worth saying to anybody.</para>
    ///
    /// <para><b>This writes the flag and nothing else.</b> §5.7's ordering — write the <c>.cws</c>,
    /// take one final checkpoint recording that change, and only THEN stop writing — belongs to the
    /// caller that owns the checkpoint, which is RC-6. Reverse those two and the flag is set, circuitRF
    /// is already off, nothing is recorded, and the history simply stops with no entry saying why.</para>
    /// </summary>
    public static bool Write(string cwsPath, bool? value)
    {
        try
        {
            if (!File.Exists(cwsPath)) return false;
            var ws = WorkspacePersistence.LoadFromFile(cwsPath);
            ws.RevisionControl = value;
            return WorkspacePersistence.SaveToFileAtomic(cwsPath, ws);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
                                    or System.Text.Json.JsonException)
        {
            return false;
        }
    }
}
