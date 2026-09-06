using System.IO;
using System.Text.Json;
using CircuitRF.Design.Cells;
using CircuitRF.Design.Layout;
using CircuitRF.Design.Workspace;
using CircuitRF.Ui.Schematic;

namespace CircuitRF.Ui.Tests;

// ──────────────────────────────────────────────────────────────────────────────
//  Removing a document, 2026-09-06.
//
//  Two halves, both from the same owner report ("not possible to delete a tech …
//  unless I use the explorer"):
//    · PrimaryViewRepair — a removed .csch/.csym/.clay may be its cell's PRIMARY, and
//      the .ccell naming it is wrong the moment it goes.
//    · DocumentRemovalImpact — what still resolves to a .ctech, which the
//      confirmation has to state BEFORE the file moves, since there is no undo.
// ──────────────────────────────────────────────────────────────────────────────

public sealed class PrimaryViewRepairTests : IDisposable
{
    private readonly string _root;

    public PrimaryViewRepairTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"crfprim_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string MakeCell(string name, params string[] schematics)
    {
        var cellDir = CellFolder.CreateCellFolder(_root, name);
        var subDir  = CellFolder.SubFolderPath(cellDir, ViewType.Schematic);
        foreach (var f in schematics) File.WriteAllText(Path.Combine(subDir, f), "{}");
        return cellDir;
    }

    private static void NamePrimary(string cellDir, string fileName)
    {
        var path  = Path.Combine(cellDir, CellFolder.CcellFileName);
        var ccell = CellPersistence.LoadFromFile(path);
        ccell.PrimarySchematic = fileName;
        CellPersistence.SaveToFile(path, ccell);
    }

    private static string? NamedPrimary(string cellDir)
        => CellPersistence.LoadFromFile(Path.Combine(cellDir, CellFolder.CcellFileName)).PrimarySchematic;

    [Fact]
    public void SoleFile_IsPrimary_EvenThoughTheCcellNamesNothing()
    {
        var cellDir = MakeCell("Amp", "amp.csch");
        var plan = PrimaryViewRepair.Plan(
            Path.Combine(CellFolder.SubFolderPath(cellDir, ViewType.Schematic), "amp.csch"));

        // PrimaryState.SoleFile — implicitly primary, which is exactly the case a name-only check
        // would miss, and it is the common one.
        Assert.True(plan.WasPrimary);
        Assert.Empty(plan.Survivors);
    }

    [Fact]
    public void RemovingTheOnlyView_ClearsTheEntry_AndReportsNoViewsLeft()
    {
        var cellDir = MakeCell("Amp", "amp.csch");
        var file    = Path.Combine(CellFolder.SubFolderPath(cellDir, ViewType.Schematic), "amp.csch");
        NamePrimary(cellDir, "amp.csch");

        var plan = PrimaryViewRepair.Plan(file);
        File.Delete(file);
        var action = PrimaryViewRepair.Apply(plan, out var promoted);

        Assert.Equal(PrimaryRepairAction.NoViewsLeft, action);
        Assert.Null(promoted);
        Assert.Null(NamedPrimary(cellDir));
    }

    [Fact]
    public void RemovingThePrimaryOfTwo_PromotesTheSurvivor()
    {
        var cellDir = MakeCell("Amp", "amp.csch", "alt.csch");
        var subDir  = CellFolder.SubFolderPath(cellDir, ViewType.Schematic);
        NamePrimary(cellDir, "amp.csch");

        var plan = PrimaryViewRepair.Plan(Path.Combine(subDir, "amp.csch"));
        Assert.True(plan.WasPrimary);

        File.Delete(Path.Combine(subDir, "amp.csch"));
        var action = PrimaryViewRepair.Apply(plan, out var promoted);

        Assert.Equal(PrimaryRepairAction.PromotedSurvivor, action);
        Assert.Equal("alt.csch", promoted);
        Assert.Equal("alt.csch", NamedPrimary(cellDir));
    }

    [Fact]
    public void RemovingThePrimaryOfThree_ClearsRatherThanGuessing()
    {
        var cellDir = MakeCell("Amp", "amp.csch", "alt.csch", "third.csch");
        var subDir  = CellFolder.SubFolderPath(cellDir, ViewType.Schematic);
        NamePrimary(cellDir, "amp.csch");

        var plan = PrimaryViewRepair.Plan(Path.Combine(subDir, "amp.csch"));
        File.Delete(Path.Combine(subDir, "amp.csch"));
        var action = PrimaryViewRepair.Apply(plan, out _);

        // NoPrimary ("not chosen yet"), never MissingNamedPrimary — the state the Project Tree
        // renders as a warning, and the one this whole repair exists to avoid creating.
        Assert.Equal(PrimaryRepairAction.ClearedForChoice, action);
        Assert.Null(NamedPrimary(cellDir));
        Assert.Equal(PrimaryState.NoPrimary,
                     CellFolder.ResolvePrimary(cellDir, ViewType.Schematic).State);
    }

    [Fact]
    public void RemovingANonPrimary_LeavesTheCcellAlone()
    {
        var cellDir = MakeCell("Amp", "amp.csch", "alt.csch");
        var subDir  = CellFolder.SubFolderPath(cellDir, ViewType.Schematic);
        NamePrimary(cellDir, "amp.csch");

        var plan = PrimaryViewRepair.Plan(Path.Combine(subDir, "alt.csch"));
        Assert.False(plan.WasPrimary);

        File.Delete(Path.Combine(subDir, "alt.csch"));
        Assert.Equal(PrimaryRepairAction.NotPrimary, PrimaryViewRepair.Apply(plan, out _));
        Assert.Equal("amp.csch", NamedPrimary(cellDir));
    }

    [Fact]
    public void ALooseViewFileOutsideACell_HasNoPrimacyToRepair()
    {
        var loose = Path.Combine(_root, "stray.csch");
        File.WriteAllText(loose, "{}");

        var plan = PrimaryViewRepair.Plan(loose);
        Assert.False(plan.InCell);
        Assert.False(plan.WasPrimary);
    }
}

public sealed class TechnologyRemovalImpactTests : IDisposable
{
    private readonly string _root;

    public TechnologyRemovalImpactTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"crftech_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string WriteTech(string name)
    {
        var techDir = Path.Combine(_root, "tech");
        Directory.CreateDirectory(techDir);
        var path = Path.Combine(techDir, name + ".ctech");
        TechPersistence.SaveToFile(path, new Technology { Name = name });
        return path;
    }

    private void WriteCws(string? defaultTechRel)
    {
        var cws = new CwsFile { DefaultTechRef = defaultTechRel };
        WorkspacePersistence.SaveToFileAtomic(Path.Combine(_root, ".cws"), cws);
    }

    private string WriteCellWithMicrostrip(string cellName)
    {
        var cellDir = CellFolder.CreateCellFolder(_root, cellName);
        var subDir  = CellFolder.SubFolderPath(cellDir, ViewType.Schematic);
        // Only the Symbol discriminator is read, which is the whole point of the scan being a JSON
        // read rather than a full schematic load.
        File.WriteAllText(Path.Combine(subDir, cellName + ".csch"),
            """{"Components":[{"Symbol":"Mlin","InstanceName":"TL1"}]}""");
        return cellDir;
    }

    private string WriteLayoutWithTechRef(string cellName, string techAbsPath)
    {
        var cellDir   = CellFolder.CreateCellFolder(_root, cellName);
        var layoutDir = CellFolder.SubFolderPath(cellDir, ViewType.Layout);
        var clay      = Path.Combine(layoutDir, cellName + ".clay");
        LayoutPersistence.SaveToFile(clay, new LayoutView
        {
            TechRef = Path.GetRelativePath(layoutDir, techAbsPath),
        });
        return clay;
    }

    [Fact]
    public void TheWorkspaceDefault_ReportsTheLayoutsThatFollowItAndTheMicrostripCells()
    {
        var tech = WriteTech("pcb");
        WriteCws(Path.GetRelativePath(_root, tech));
        WriteCellWithMicrostrip("Amp");

        // A layout with no TechRef of its own — the ordinary case, which follows the default.
        var plainCell = CellFolder.CreateCellFolder(_root, "Plain");
        LayoutPersistence.SaveToFile(
            Path.Combine(CellFolder.SubFolderPath(plainCell, ViewType.Layout), "plain.clay"),
            new LayoutView());

        var impact = DocumentRemovalImpact.ForTechnology(_root, tech);

        Assert.True(impact.IsWorkspaceDefault);
        Assert.Single(impact.LayoutsFollowingDefault);
        Assert.Single(impact.MicrostripCells);
        Assert.Equal("Amp", Path.GetFileName(impact.MicrostripCells[0]));
    }

    /// <summary>
    /// The half nothing else in the application reports. A microstrip's substrate comes from the
    /// workspace default and from nowhere else, so removing it changes the SIMULATED answer while the
    /// schematic goes on looking identical.
    /// </summary>
    [Fact]
    public void ASchematicWithNoMicrostrip_IsNotCounted()
    {
        var tech = WriteTech("pcb");
        WriteCws(Path.GetRelativePath(_root, tech));

        var cellDir = CellFolder.CreateCellFolder(_root, "Lumped");
        File.WriteAllText(
            Path.Combine(CellFolder.SubFolderPath(cellDir, ViewType.Schematic), "l.csch"),
            """{"Components":[{"Symbol":"Resistor","InstanceName":"R1"}]}""");

        Assert.Empty(DocumentRemovalImpact.ForTechnology(_root, tech).MicrostripCells);
    }

    [Fact]
    public void ALayoutPointingAtItDirectly_IsReported_EvenWhenItIsNotTheDefault()
    {
        var deflt = WriteTech("pcb");
        var other = WriteTech("mmic");
        WriteCws(Path.GetRelativePath(_root, deflt));
        WriteLayoutWithTechRef("Die", other);

        var impact = DocumentRemovalImpact.ForTechnology(_root, other);

        Assert.False(impact.IsWorkspaceDefault);
        Assert.Single(impact.LayoutsPointingHere);
        // A layout that has deliberately deviated does NOT follow the default, so removing the
        // default must not claim it.
        Assert.Empty(impact.LayoutsFollowingDefault);
    }

    [Fact]
    public void TheOtherTechnologies_AreOfferedAsAReplacementDefault()
    {
        var deflt = WriteTech("pcb");
        WriteTech("mmic");
        WriteCws(Path.GetRelativePath(_root, deflt));

        var impact = DocumentRemovalImpact.ForTechnology(_root, deflt);

        Assert.Single(impact.OtherTechnologies);
        Assert.Equal("mmic.ctech", Path.GetFileName(impact.OtherTechnologies[0]));
    }

    [Fact]
    public void AWorkspaceColorScheme_IsMatchedByName()
    {
        var cws = new CwsFile { ColorSchemeName = "Dusk" };
        WorkspacePersistence.SaveToFileAtomic(Path.Combine(_root, ".cws"), cws);

        Assert.True(DocumentRemovalImpact.IsWorkspaceColorScheme(_root, Path.Combine(_root, "Dusk.ccolor")));
        Assert.False(DocumentRemovalImpact.IsWorkspaceColorScheme(_root, Path.Combine(_root, "Dawn.ccolor")));
    }

    /// <summary>
    /// Matched by RESOLUTION, not by file name — two cells can each own a <c>layout/top.wBond</c>, and
    /// a name-only match would report the wrong one as broken.
    /// </summary>
    [Fact]
    public void AWBondLink_IsMatchedByResolution()
    {
        var cellA   = CellFolder.CreateCellFolder(_root, "A");
        var cellB   = CellFolder.CreateCellFolder(_root, "B");
        var aDesign = Path.Combine(CellFolder.SubFolderPath(cellA, ViewType.Layout), "top.wBond");
        var bDesign = Path.Combine(CellFolder.SubFolderPath(cellB, ViewType.Layout), "top.wBond");
        File.WriteAllText(aDesign, "{}");
        File.WriteAllText(bDesign, "{}");

        // A's schematic links A's own design.
        File.WriteAllText(
            Path.Combine(CellFolder.SubFolderPath(cellA, ViewType.Schematic), "a.csch"),
            """{"Components":[{"Symbol":"WBond","Parameters":[{"Name":"File","Expression":"../layout/top.wBond"}]}]}""");

        var hits = DocumentRemovalImpact.SchematicsLinkingWBond(_root, aDesign);
        Assert.Single(hits);

        Assert.Empty(DocumentRemovalImpact.SchematicsLinkingWBond(_root, bDesign));
    }
}
