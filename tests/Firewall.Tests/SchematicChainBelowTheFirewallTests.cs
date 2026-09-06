using System;
using System.IO;
using CircuitRF.Core.Netlist;
using CircuitRF.Design.Schematic;

namespace CircuitRF.Firewall.Tests;

/// <summary>
/// Gate 2 of brief-automation-2-schematic-below-the-firewall.md: the whole chain
///
/// <code>
/// read a .csch  →  SchematicEditModel  →  NetExtractor.Extract  →  TestBench + Library
///               →  CnlWriter.Write     →  a .cnl the existing verbs already run
/// </code>
///
/// assembled and RUN in a project that references no UI framework and no <c>src/Ui</c>.
///
/// <para><b>Why this test lives here rather than in Ui.Tests.</b> The claim is not that the chain
/// produces the right netlist — <c>NetExtractorTests</c> and the hero regressions have always covered
/// that. The claim is that it can be assembled AT ALL with nothing from <c>src/Ui</c> on the path, and
/// the only way to state that so it stays true is to put it in a project whose own references make it
/// impossible to reach for one. This project already had exactly that shape (see the reference list in
/// the <c>.csproj</c>) and already exists to hold assertions about the boundary rather than about
/// behaviour, so the gate costs no new project and cannot be weakened without the reference list
/// visibly changing.</para>
///
/// <para><b>What byte identity is against.</b> The golden was written by the GUI's own chain —
/// <c>src/Ui/Diagnostics/Fixtures/DocRunData.cs</c> is the code that has always performed these three
/// calls, and its companion <c>SchematicChainMatchesTheHeadlessOneTests</c> in <c>Ui.Tests</c> asserts
/// the same golden from that side. Neither test can drift from the other without one of them going
/// red, which is the point: a headless netlist and the one a Simulate press produces are the same
/// bytes, not merely the same intent (R-aut0-6, the standard <c>EmCliVerbTests</c> set).</para>
/// </summary>
public class SchematicChainBelowTheFirewallTests
{
    /// <summary>The authored input, read as a FILE — the same bytes src/Ui embeds as a resource.</summary>
    internal const string DocSchematicRelPath = "src/Ui/resources/doc-schematics/Example_SParam_LC.csch";

    /// <summary>The golden, shared with the Ui.Tests companion so the two cannot drift apart.</summary>
    internal const string GoldenRelPath = "testdata/schematic-chain/Example_SParam_LC.cnl";

    /// <summary>DocRunData's own arguments for this fixture, repeated exactly — the extraction's
    /// TestBench name and the <c>.cnl</c> header both land in the written bytes.</summary>
    internal const string TestBenchName = "ExampleSParam";
    internal const string CnlHeader     = "generated for the user documentation from Example_SParam_LC";

    [Fact]
    public void ACschBecomesACnl_WithNothingFromSrcUiOnThePath()
    {
        string repoRoot = FindRepoRoot();
        string csch     = Path.Combine(repoRoot, DocSchematicRelPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(csch), $"The authored fixture schematic is missing: {csch}");

        // ── The chain, in full, with no type from src/Ui named anywhere in it ──────────────
        var (model, _, _) = SchematicPersistence.Deserialize(File.ReadAllText(csch), null);
        var extracted     = NetExtractor.Extract(model, TestBenchName);
        string cnl        = CnlWriter.Write(extracted.TestBench, extracted.Library, CnlHeader);

        string golden = Path.Combine(repoRoot, GoldenRelPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(golden), $"The golden netlist is missing: {golden}");

        Assert.Equal(Normalize(File.ReadAllText(golden)), Normalize(cnl));
    }

    /// <summary>Line endings only — the golden is committed text and git may hand it back either
    /// way on Windows. Every other byte is compared as written.</summary>
    internal static string Normalize(string s) => s.Replace("\r\n", "\n");

    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "circuitRF.slnx")))
            dir = dir.Parent;

        Assert.True(dir is not null,
            "Could not locate the repo root (the folder holding circuitRF.slnx) from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
