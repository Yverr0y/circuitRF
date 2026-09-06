using CircuitRF.Core.Design;
using CircuitRF.Core.Netlist;
using CircuitRF.Design.Cells;
using CircuitRF.Design.Schematic;

namespace CircuitRF.Cli;

/// <summary>
/// The <c>(Library, TestBench)</c> a document holds — read the way the APPLICATION reads it, which
/// for a schematic is not the obvious way.
///
/// <para><b>A `.csch` goes through the `.cnl` on its way to the elaborator, and that round trip is
/// load-bearing.</b> The GUI's Simulate is
/// <c>NetExtractor.Extract → CnlWriter.Write → CnlReader.Read → Elaborator</c>
/// (<c>WorkspaceViewModel.WriteNetlist</c> then <c>SchematicRunService.Prepare</c>), and the two
/// readers do NOT agree about bare words: a schematic parameter is an EXPRESSION, so
/// <c>BiasTee=on</c> read straight out of extraction fails elaboration with "Unresolved name 'on'",
/// while the same value written to a `.cnl` and read back is quoted by <c>CnlReader</c> and
/// elaborates. Four of the shipped example schematics carry exactly that value.</para>
///
/// <para>So a <c>check</c> that skipped the round trip would report four errors the application does
/// not have — which is the precise failure R-aut4-2 exists to prevent, in the other direction: not a
/// rule the GUI does not enforce, but a rule the GUI does not APPLY. The finding is recorded in
/// <c>src/Cli/RESOLVED.md</c>; what this file does is refuse to have a second opinion about it.</para>
///
/// <para><b>Nothing is written</b> (R-aut4-6). The `.cnl` exists as a string and is handed straight
/// to <c>CnlReader.Read</c> with the schematic's own directory as the source directory — the same
/// argument <c>CnlReader.ReadFile</c> derives from a real path, so relative SnP and model references
/// resolve identically.</para>
/// </summary>
internal static class CircuitSource
{
    /// <summary>
    /// Reads <paramref name="path"/> as a circuit, or returns null when it holds none.
    /// <paramref name="onError"/> receives the reader's own message; a caller that wants to report
    /// it as its own diagnostic supplies one.
    /// </summary>
    public static (Library Lib, TestBench Tb)? Read(
        string path, DocumentKind kind, Action<string>? onError = null)
    {
        try
        {
            switch (kind)
            {
                case DocumentKind.Netlist:
                {
                    var (lib, tb) = CnlReader.ReadFile(path);
                    return (lib, tb);
                }

                case DocumentKind.Schematic:
                    return FromSchematic(path);

                case DocumentKind.Cell:
                {
                    // A cell folder's PRIMARY schematic is what a reference to that cell resolves
                    // to, so it is the one this question is about.
                    var primary = CellFolder.ResolvePrimary(path, ViewType.Schematic);
                    if (primary.ResolvedName is not { } file) return null;
                    return FromSchematic(Path.Combine(
                        CellFolder.SubFolderPath(path, ViewType.Schematic), file));
                }

                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Extraction, then the `.cnl` round trip — see this type's own remarks for why the second half
    /// is not optional.
    /// </summary>
    public static (Library Lib, TestBench Tb) FromSchematic(string cschPath)
    {
        var (model, _, _) = SchematicPersistence.LoadFromFile(cschPath);
        return FromSchematic(model, Path.GetFileNameWithoutExtension(cschPath),
                             Path.GetDirectoryName(Path.GetFullPath(cschPath)));
    }

    /// <inheritdoc cref="FromSchematic(string)"/>
    public static (Library Lib, TestBench Tb) FromSchematic(
        SchematicEditModel model, string testBenchName, string? sourceDir)
    {
        var extracted = NetExtractor.Extract(model, testBenchName);
        string cnl    = CnlWriter.Write(extracted.TestBench, extracted.Library,
                                        $"check/explain — from {testBenchName}");
        var (lib, tb) = new CnlReader().Read(cnl, testBenchName, sourceDir);
        return (lib, tb);
    }

    /// <summary>
    /// Whether the netlist declares anything a run verb could dispatch. The GUI's own
    /// <c>RunStatus.NoAnalysis</c> test (<c>SchematicRunService.Prepare</c> step 2): a typed analysis,
    /// or a RAW <c>analysis … type=sparam</c> directive, which never becomes a typed one and is
    /// dispatched straight from its text.
    ///
    /// <para>Asked this way rather than through <c>ChainSelector</c> because chain selection is
    /// per-KIND: a bench declaring only an S-parameter sweep has no HB chain, and reporting that as
    /// "no analysis will dispatch" would warn about every S-parameter and DC bench in the tree.</para>
    /// </summary>
    public static bool DeclaresARunnableAnalysis(TestBench tb)
    {
        if (tb.Analyses.Count > 0) return true;

        foreach (var raw in tb.RawDirectives)
            if (raw.Kind == "analysis" && IsSparamRaw(raw.RawLine))
                return true;

        return false;
    }

    private static bool IsSparamRaw(string rawLine)
    {
        foreach (var t in rawLine.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries))
            if (t.Equals("type=sparam", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
