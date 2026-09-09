using System.IO;
using System.Linq;
using CircuitRF.Ui.Layout;
using CircuitRF.Ui.Schematic;
using CircuitRF.Ui.WBond;
using Xunit;

namespace CircuitRF.Ui.Tests;

/// <summary>
/// Duplicate Cell renamed the primary schematic and symbol of the copy and stopped there, so a cell
/// with artwork produced a folder named for the new cell holding a <c>.clay</c> named for the old
/// one — and a <c>.ccell</c> that agreed with the file, which is why nothing ever warned. Rename
/// Cell had already been taught the layout; the two operations kept separate copies of the same
/// list, so only one of them learned.
///
/// <para>These exercise <see cref="PrimaryViewRename"/>, which is now that one list, plus the wire
/// pairing the layout rename drags along with it. The property that is Duplicate's alone is the
/// last one: the ORIGINAL cell is still sitting there, and nothing done to the copy may reach it.
/// </para>
/// </summary>
public class DuplicateCellPrimariesTests : IDisposable
{
    private readonly string _ws;

    public DuplicateCellPrimariesTests()
    {
        _ws = Path.Combine(Path.GetTempPath(), $"crf_dup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_ws);
    }

    public void Dispose() { try { Directory.Delete(_ws, recursive: true); } catch { } }

    private string Cell(string name) => CellFolder.CreateCellFolder(_ws, name);

    private static string LayoutDir(string cellDir) => CellFolder.SubFolderPath(cellDir, ViewType.Layout);

    private static void WriteClay(string cellDir, string stem)
        => LayoutPersistence.SaveToFile(Path.Combine(LayoutDir(cellDir), stem + ".clay"),
            new LayoutView { DbuPerMicron = 1000, DisplayUnit = LayoutUnit.Um, SnapDbu = 1000 });

    private static void WriteWBond(string cellDir, string stem)
        => File.WriteAllText(Path.Combine(LayoutDir(cellDir), stem + WBondCell.FileExtension), "{}");

    private static string WriteSchematicLinking(string cellDir, string stem, string link)
    {
        var path  = Path.Combine(CellFolder.SubFolderPath(cellDir, ViewType.Schematic), stem + ".csch");
        var model = new SchematicEditModel();
        var comp  = new EditableComponent { InstanceName = "WB1", Symbol = SymbolKind.WBond };
        comp.Parameters.Add(new EditableParameter
        {
            Name = WBondPlacement.FileParameter, Expression = link,
        });
        model.Components.Add(comp);
        SchematicPersistence.SaveToFile(path, model);
        return path;
    }

    private static string LinkIn(string cschPath)
    {
        var (model, _, _) = SchematicPersistence.LoadFromFile(cschPath);
        return model.Components.Single()
            .Parameters.Single(p => p.Name == WBondPlacement.FileParameter).Expression;
    }

    private static void CopyDirectoryRecursive(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(src))
            CopyDirectoryRecursive(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    /// <summary>Copies "Amp" to "Amp_v2" and gives the copy its own name, as Duplicate Cell does.</summary>
    private string Duplicate(string sourceCellDir, string newName)
    {
        var newDir = Path.Combine(_ws, newName);
        CopyDirectoryRecursive(sourceCellDir, newDir);
        PrimaryViewRename.ToCellName(newDir, newName);
        return newDir;
    }

    private static CcellFile Ccell(string cellDir)
        => CellPersistence.LoadFromFile(Path.Combine(cellDir, CellFolder.CcellFileName));

    // ── the view that was missing ─────────────────────────────────────────────

    [Fact]
    public void TheDuplicatesLayout_TakesTheNewCellsName()
    {
        var amp = Cell("Amp");
        WriteClay(amp, "Amp");

        var copy = Duplicate(amp, "Amp_v2");

        Assert.True(File.Exists(Path.Combine(LayoutDir(copy), "Amp_v2.clay")));
        Assert.False(File.Exists(Path.Combine(LayoutDir(copy), "Amp.clay")));
        Assert.Equal("Amp_v2.clay", Ccell(copy).PrimaryLayout);
    }

    [Fact]
    public void EveryPrimaryTheCellHas_TakesTheNewName_InOneOperation()
    {
        var amp = Cell("Amp");
        SchematicPersistence.SaveToFile(
            Path.Combine(CellFolder.SubFolderPath(amp, ViewType.Schematic), "Amp.csch"),
            new SchematicEditModel());
        SymbolPersistence.SaveToFile(
            Path.Combine(CellFolder.SubFolderPath(amp, ViewType.Symbol), "Amp.csym"),
            new Symbol([], [], 10));
        WriteClay(amp, "Amp");

        var copy  = Duplicate(amp, "Amp_v2");
        var ccell = Ccell(copy);

        Assert.Equal("Amp_v2.csch", ccell.PrimarySchematic);
        Assert.Equal("Amp_v2.csym", ccell.PrimarySymbol);
        Assert.Equal("Amp_v2.clay", ccell.PrimaryLayout);
    }

    /// <summary>A second layout is the author's file, under the author's name. Only the PRIMARY is
    /// the cell's own.</summary>
    [Fact]
    public void ANonPrimaryLayout_KeepsItsName()
    {
        var amp = Cell("Amp");
        WriteClay(amp, "Amp");
        WriteClay(amp, "Amp_stripline_variant");
        var ccellPath = Path.Combine(amp, CellFolder.CcellFileName);
        var ccell     = CellPersistence.LoadFromFile(ccellPath);
        ccell.PrimaryLayout = "Amp.clay";
        CellPersistence.SaveToFile(ccellPath, ccell);

        var copy = Duplicate(amp, "Amp_v2");

        Assert.True(File.Exists(Path.Combine(LayoutDir(copy), "Amp_v2.clay")));
        Assert.True(File.Exists(Path.Combine(LayoutDir(copy), "Amp_stripline_variant.clay")));
    }

    /// <summary>A cell with two layouts and no primary chosen has nothing to rename — picking one
    /// alphabetically would be this operation inventing a design decision.</summary>
    [Fact]
    public void NoPrimaryChosen_IsLeftAlone()
    {
        var amp = Cell("Amp");
        WriteClay(amp, "left");
        WriteClay(amp, "right");

        var copy   = Duplicate(amp, "Amp_v2");
        var result = PrimaryViewRename.ToCellName(copy, "Amp_v2")
            .Single(r => r.ViewType == ViewType.Layout);

        Assert.Equal(PrimaryRenameOutcome.NoPrimary, result.Outcome);
        Assert.True(File.Exists(Path.Combine(LayoutDir(copy), "left.clay")));
        Assert.True(File.Exists(Path.Combine(LayoutDir(copy), "right.clay")));
    }

    /// <summary>The target name is taken by another view of the same cell. Nothing is overwritten,
    /// and the caller has an outcome to report rather than a silently skipped file.</summary>
    [Fact]
    public void AnOccupiedTargetName_IsRefused_NotOverwritten()
    {
        var amp = Cell("Amp");
        WriteClay(amp, "Amp");
        WriteClay(amp, "Amp_v2");
        var ccellPath = Path.Combine(amp, CellFolder.CcellFileName);
        var ccell     = CellPersistence.LoadFromFile(ccellPath);
        ccell.PrimaryLayout = "Amp.clay";
        CellPersistence.SaveToFile(ccellPath, ccell);

        var newDir = Path.Combine(_ws, "Amp_v2");
        CopyDirectoryRecursive(amp, newDir);
        var result = PrimaryViewRename.ToCellName(newDir, "Amp_v2")
            .Single(r => r.ViewType == ViewType.Layout);

        Assert.Equal(PrimaryRenameOutcome.Blocked, result.Outcome);
        Assert.True(File.Exists(Path.Combine(LayoutDir(newDir), "Amp.clay")));
        Assert.Equal("Amp.clay", Ccell(newDir).PrimaryLayout);
    }

    // ── the wires, and the cell left behind ───────────────────────────────────

    /// <summary>
    /// The copy's wires pair with its artwork by shared stem, so they move with it — otherwise the
    /// duplicate opens with no wirebonds at all while the original still has its.
    /// </summary>
    [Fact]
    public void TheDuplicatesWires_FollowItsArtwork()
    {
        var amp = Cell("Amp");
        WriteClay(amp, "Amp");
        WriteWBond(amp, "Amp");

        var copy = Duplicate(amp, "Amp_v2");
        WBondCell.RenamePairedWires(LayoutDir(copy), "Amp", "Amp_v2");

        Assert.Equal(
            Path.Combine(LayoutDir(copy), "Amp_v2" + WBondCell.FileExtension),
            WBondCell.FindFor(Path.Combine(LayoutDir(copy), "Amp_v2.clay")));
    }

    /// <summary>
    /// Duplicate's own hazard, which Rename never had: the source cell still exists, with a layout
    /// and a wirebond design of exactly the names being renamed away in the copy. The link is
    /// matched by where it RESOLVES, so the original's schematic keeps pointing at the original's
    /// wires.
    /// </summary>
    [Fact]
    public void TheOriginalCell_IsNotTouched()
    {
        var amp     = Cell("Amp");
        WriteClay(amp, "Amp");
        WriteWBond(amp, "Amp");
        var ampCsch = WriteSchematicLinking(amp, "Amp", "../layout/Amp.wBond");

        var copy = Duplicate(amp, "Amp_v2");
        WBondCell.RenamePairedWires(LayoutDir(copy), "Amp", "Amp_v2");
        var rewritten = CellUsageScanner.RewriteWBondLinks(
            _ws, LayoutDir(copy), "Amp", "Amp_v2", "Amp", "Amp_v2", out var failed);

        Assert.Empty(failed);

        // The copy's own schematic — renamed to Amp_v2.csch by the primary rename — is repointed.
        var copyCsch = Path.Combine(CellFolder.SubFolderPath(copy, ViewType.Schematic), "Amp_v2.csch");
        Assert.Equal([copyCsch], rewritten);
        Assert.Equal("../layout/Amp_v2.wBond", LinkIn(copyCsch));

        // The original still has its own artwork, its own wires, and its own link.
        Assert.True(File.Exists(Path.Combine(LayoutDir(amp), "Amp.clay")));
        Assert.True(File.Exists(Path.Combine(LayoutDir(amp), "Amp" + WBondCell.FileExtension)));
        Assert.Equal("../layout/Amp.wBond", LinkIn(ampCsch));
        Assert.Equal("Amp.csch", Path.GetFileName(ampCsch));
    }
}
