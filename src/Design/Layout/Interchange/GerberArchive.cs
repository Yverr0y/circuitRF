// Looking inside an archive that sits beside the artwork — GI4 R-gi4-10 / R-gi4-11.
//
// A folder whose ONLY artwork is inside an archive is currently a dead end: the archive is classified
// "not text" and skipped, which is true and useless. It should be an OFFER — never an action.
// R-L4g-3's doctrine is exact and applies here word for word: an import that quietly reached outside
// what the user pointed at is a surprise, and a surprise in a file importer is a support question
// forever. So this file only ever unpacks something a caller has already been told about and has
// already said yes to, it unpacks to a TEMPORARY location, and it leaves nothing behind.
//
// R-gi4-11, which is the rule that stops this being harmful: a set that already yields artwork
// imports from the files on disk and never touches an archive sitting beside them, even if it holds
// the same board. Reading both is how two versions of one board get silently merged. That rule lives
// at the call site — GerberImport only reaches this file when the folder yielded no artwork of its
// own — and it is what makes "offer" the only safe shape for this.

using System.IO.Compression;

namespace CircuitRF.Design.Layout.Interchange;

/// <summary>What one archive turned out to hold, unpacked. <b>Dispose deletes the whole temporary
/// tree</b> — "leave nothing behind" is a gate, not a courtesy, and the import that reads
/// <see cref="Files"/> has finished with them by the time it returns.</summary>
public sealed class ExtractedArchive : IDisposable
{
    private ExtractedArchive(string root, IReadOnlyList<string> files, string sourceName)
    {
        Root = root;
        Files = files;
        SourceName = sourceName;
    }

    /// <summary>The temporary directory everything was unpacked into.</summary>
    public string Root { get; }

    /// <summary>The files of the ONE folder inside the archive that holds the most Gerber content —
    /// an output set is routinely archived with its own folder around it, and importing the archive's
    /// top level would then import nothing.</summary>
    public IReadOnlyList<string> Files { get; }

    /// <summary>The archive's own file name, for the report.</summary>
    public string SourceName { get; }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Unpacks <paramref name="archivePath"/> to a temporary directory and returns the folder inside
    /// it that actually holds a file set. Returns null with a sentence in <paramref name="error"/>
    /// when the archive cannot be read or holds no Gerber content — a caller that offered to look
    /// inside owes the user an answer either way.
    /// </summary>
    public static ExtractedArchive? Open(string archivePath, out string? error)
    {
        error = null;
        string root = Directory.CreateTempSubdirectory("crf-gerber-zip-").FullName;
        try
        {
            using (var zip = ZipFile.OpenRead(archivePath))
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
                    if (Destination(root, entry.FullName) is not { } destination) continue;

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: true);
                }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                                      or NotSupportedException)
        {
            error = $"{Path.GetFileName(archivePath)} could not be opened ({ex.GetType().Name}), so nothing " +
                    "was imported from it.";
            Delete(root);
            return null;
        }

        // The folder with the most Gerber content wins, and ties go to the shallowest — an output set
        // archived with a "gerber" folder around it is the common shape, and its top level is often a
        // read-me and nothing else.
        string? best = null;
        int bestScore = 0, bestDepth = int.MaxValue;
        foreach (string dir in Folders(root))
        {
            int score = GerberFileClassifier.ClassifyFolder(dir)
                .Count(c => c.Kind is GerberFileKind.Artwork or GerberFileKind.Drill);
            int depth = dir.Count(c => c == Path.DirectorySeparatorChar);
            if (score > bestScore || (score == bestScore && score > 0 && depth < bestDepth))
            {
                best = dir;
                bestScore = score;
                bestDepth = depth;
            }
        }

        if (best is null)
        {
            error = $"{Path.GetFileName(archivePath)} holds no Gerber artwork or drill data, so nothing " +
                    "was imported from it.";
            Delete(root);
            return null;
        }

        return new ExtractedArchive(
            root,
            [.. Directory.EnumerateFiles(best).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)],
            Path.GetFileName(archivePath));
    }

    /// <summary>Where one entry lands, or null for an entry that would land outside
    /// <paramref name="root"/>. An archive is an untrusted file like any other, and an entry named
    /// <c>../../…</c> writing outside the temporary tree would be exactly the surprise this whole
    /// path exists to avoid.</summary>
    private static string? Destination(string root, string entryName)
    {
        string full = Path.GetFullPath(Path.Combine(root, entryName.Replace('\\', '/')));
        string prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.Ordinal) ? full : null;
    }

    private static IEnumerable<string> Folders(string root)
    {
        yield return root;
        foreach (string dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            yield return dir;
    }

    private static void Delete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
