using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace CircuitRF.Core;

/// <summary>
/// The one place a STORED relative reference is turned into a filesystem path, and back.
///
/// <para><b>Every relative reference circuitRF writes into a document is stored with <c>/</c>
/// separators</b> — a <c>.clay</c>'s <c>TechRef</c>, a <c>.cws</c>'s <c>DefaultTechRef</c>, an
/// instance's <c>CellRef</c>, a bitmap's <c>ImagePathRef</c>, a <c>.cem</c>'s <c>LayoutRef</c>.
/// That is the git/URI convention, and <c>src/Ui/Schematic/WorkspaceRefs.cs</c> already states the
/// reason: the separator problem only bites when a workspace crosses platforms, i.e. never on the
/// machine that wrote it.</para>
///
/// <para><b>The breakage is one-way, which is why it survives so long.</b> Windows accepts
/// <c>/</c> as well as <c>\</c>, so a macOS- or Linux-authored ref opens everywhere; a
/// Windows-authored <c>..\..\x.ctech</c> is, on Unix, a single filename containing three
/// backslashes, and <c>Path.Combine</c> resolves it happily into a path that does not exist. The
/// symptom is a "not found" message quoting a path that has both separators in it.</para>
///
/// <para><b>So resolution is tolerant and writing is strict.</b> <see cref="ToNative"/> accepts
/// either separator, because files written by older versions and by other platforms are already out
/// there and re-writing them is not something a reader may do; <see cref="ToStored"/> is what every
/// site that computes a ref must pass its <c>Path.GetRelativePath</c> result through, so nothing new
/// leaves this machine in a form the next one cannot read.</para>
///
/// <para><b>The one thing given up:</b> a <c>\</c> is a legal character in a Unix filename, so a
/// reference to a file genuinely named that way no longer resolves. Since every ref circuitRF writes
/// is separator-normalized, a <c>\</c> in a stored ref can only be a Windows separator.</para>
/// </summary>
public static class RefPath
{
    /// <summary>
    /// The stored form of a computed relative path: <c>/</c>-separated, on every platform.
    ///
    /// <para>Null in, null out — a <c>TechRef</c> of null is the ordinary case ("use the workspace
    /// default") and must stay null rather than becoming an empty ref that resolves to a directory.</para>
    /// </summary>
    [return: NotNullIfNotNull(nameof(relativePath))]
    public static string? ToStored(string? relativePath) => relativePath?.Replace('\\', '/');

    /// <summary>A stored ref in this platform's separators, accepting either separator on input.</summary>
    public static string ToNative(string storedRef) =>
        Path.DirectorySeparatorChar == '/'
            ? storedRef.Replace('\\', '/')
            : storedRef.Replace('/', '\\');

    /// <summary>
    /// A stored ref resolved against a base directory, absolute and normalized — the shape almost
    /// every call site wants. A rooted ref wins over <paramref name="baseDir"/>, exactly as
    /// <see cref="Path.Combine(string,string)"/> already does.
    /// </summary>
    public static string Resolve(string baseDir, string storedRef) =>
        Path.GetFullPath(Path.Combine(baseDir, ToNative(storedRef)));
}
