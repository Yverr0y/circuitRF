using CircuitRF.Design.Layout;
using CircuitRF.Design.Schematic;
using CircuitRF.Design.Symbol;

namespace CircuitRF.Design.Cells;

// ──────────────────────────────────────────────────────────────────────────────
//  CellCreate — writing a cell's view files, once.
//
//  brief-automation-3-authoring-verbs.md R-aut3-1: `circuitrf new cell` and the GUI's New Cell /
//  New Schematic / New Symbol / New Layout commands call THESE, and after this file there is exactly
//  one implementation of "write an empty view of type T into a cell folder".
//
//  WHY THE LEAF WRITERS ARE THE CAPABILITY, AND `Create` IS ONLY THEIR COMPOSITION. The GUI does not
//  create a cell in one call and it cannot be made to: R-cc-1 says a schematic that fails to write
//  must never roll back the cell that already exists, so New Cell reports the folder, refreshes the
//  tree, and only then writes the schematic — and New Schematic writes one into a cell that already
//  exists, with a file name that need not be the cell's. What both paths genuinely share is the
//  WRITE, so that is what lives here. `Create` is the headless composition of the same three writers
//  in the obvious order, and the byte-identity gate compares the two compositions.
//
//  R-aut3-7: a cell created here satisfies CellFolder.ResolvePrimary's rules or it is not created.
//  Every view written is the SOLE file in its sub-folder, which is CellFolder's `SoleFile` branch —
//  implicitly primary, with nothing named in `.ccell` to contradict it and therefore no way to
//  produce the `MissingNamedPrimary` state the project tree surfaces as a warning.
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>Which views <see cref="CellCreate.Create"/> writes. Flags, because the three are
/// independent and a cell may legitimately have any subset of them (including none — that is a cell
/// folder with three empty sub-folders, which is what the folder-creation call on its own makes).</summary>
[Flags]
public enum CellViews
{
    None      = 0,
    Schematic = 1,
    Symbol    = 2,
    Layout    = 4,
}

/// <param name="CellDir">The cell folder.</param>
/// <param name="SchematicPath">The written <c>.csch</c>, or null when it was not asked for.</param>
/// <param name="SymbolPath">The written <c>.csym</c>, or null when it was not asked for.</param>
/// <param name="LayoutPath">The written <c>.clay</c>, or null when it was not asked for.</param>
public sealed record CellCreateResult(
    string CellDir, string? SchematicPath, string? SymbolPath, string? LayoutPath)
{
    /// <summary>Every path the creation produced, cell folder first — what a <c>--json</c> caller
    /// reads, since its next step is usually to rewrite one of them.</summary>
    public IEnumerable<string> Paths
    {
        get
        {
            yield return CellDir;
            if (SchematicPath is { } s) yield return s;
            if (SymbolPath    is { } y) yield return y;
            if (LayoutPath    is { } l) yield return l;
        }
    }
}

public static class CellCreate
{
    /// <summary>
    /// What <c>new cell</c> creates when <c>--views</c> says nothing.
    ///
    /// <para><b>Schematic alone, because that is what the GUI's New Cell creates</b> — it calls
    /// <see cref="CellFolder.CreateCellFolder"/> and then writes one <c>.csch</c> (R-cc-1: a New Cell
    /// always creates that cell's primary schematic). The symbol and layout sub-folders are made and
    /// left empty, and a user makes those views when they want them. The brief's own text says the
    /// default is symbol and schematic "matching the GUI's own New Cell"; the tree says otherwise,
    /// and the RULE — headless matches the GUI — is what decides it, since a headless default that
    /// differs from the dialog's is a second product (R-aut3-3's reasoning). See
    /// <c>src/Cli/RESOLVED.md</c>.</para>
    /// </summary>
    public const CellViews DefaultViews = CellViews.Schematic;

    /// <summary>
    /// The whole cell: folder, sub-folders, <c>.ccell</c>, and one empty-but-valid file per requested
    /// view, each named after the cell.
    /// </summary>
    /// <param name="schematic">
    /// The schematic model to write. Null writes an empty one — which is what the New Cell dialog's
    /// own pre-selected "(Empty)" template choice produces, so the headless default and the dialog's
    /// agree (R-aut3-3). The GUI passes a loaded shipped template here when the user picked one.
    /// </param>
    /// <param name="tech">
    /// The technology a <c>.clay</c> takes its display unit and snap from, resolved by the caller —
    /// the same values <c>New Layout</c> writes. Null is the supported no-technology state and gives
    /// the same defaults the GUI's own null-technology branch does.
    /// </param>
    /// <exception cref="ArgumentException">R-aut3-9: <paramref name="cellName"/> fails
    /// <see cref="NameValidator"/>. There is no second rule set — a headless caller must not be able
    /// to create a name the GUI rejects.</exception>
    public static CellCreateResult Create(
        string parentDir, string cellName, CellViews views = DefaultViews,
        SchematicEditModel? schematic = null, Technology? tech = null)
    {
        string cellDir = CellFolder.CreateCellFolder(parentDir, cellName);

        return new CellCreateResult(
            cellDir,
            views.HasFlag(CellViews.Schematic) ? WriteSchematicView(cellDir, cellName, cellName, schematic) : null,
            views.HasFlag(CellViews.Symbol)    ? WriteSymbolView(cellDir, cellName)                         : null,
            views.HasFlag(CellViews.Layout)    ? WriteLayoutView(cellDir, cellName, NewLayoutView(tech))    : null);
    }

    /// <summary>
    /// Writes one <c>.csch</c> into <paramref name="cellDir"/>'s schematic sub-folder and returns its
    /// path. <paramref name="cellName"/> is the cell the schematic belongs to (it is stored in the
    /// file); <paramref name="fileNameWithoutExt"/> is what the file is called, which is not always
    /// the same thing — New Schematic writes a second view into an existing cell.
    /// </summary>
    public static string WriteSchematicView(
        string cellDir, string cellName, string fileNameWithoutExt, SchematicEditModel? model = null)
    {
        string path = ViewPath(cellDir, ViewType.Schematic, fileNameWithoutExt);
        SchematicPersistence.SaveToFile(path, model ?? new SchematicEditModel(), cellName: cellName);
        return path;
    }

    /// <summary>Writes one empty <c>.csym</c> — no primitives, no pins, no ports — and returns its
    /// path. It loads through <c>SymbolPersistence</c> and opens on an empty canvas (R-aut3-8).</summary>
    public static string WriteSymbolView(string cellDir, string fileNameWithoutExt)
    {
        string path = ViewPath(cellDir, ViewType.Symbol, fileNameWithoutExt);
        SymbolPersistence.SaveToFile(path, new CircuitRF.Design.Symbol.Symbol([], [], portCount: 0));
        return path;
    }

    /// <summary>
    /// The empty layout a new <c>.clay</c> starts as. The display unit and snap come from
    /// <paramref name="tech"/>; <c>TechRef</c> is left null, so the layout resolves its technology
    /// the ordinary way (its own workspace's default) rather than pinning the one it was made under.
    ///
    /// <para>Separate from the write because the GUI opens an editor session on the very model it
    /// saved — reading it back off disk to get one would be a second object and a second chance to
    /// differ.</para>
    /// </summary>
    public static LayoutView NewLayoutView(Technology? tech) => new()
    {
        DbuPerMicron = LayoutUnits.DefaultDbuPerMicron,
        DisplayUnit  = tech?.DefaultDisplayUnit ?? LayoutUnit.Um,
        SnapDbu      = tech?.DefaultSnapDbu ?? 1000,
        AngleMode    = AngleMode.AnyAngle,
        TechRef      = null,
    };

    /// <summary>Writes one <c>.clay</c> into the cell folder's layout sub-folder and returns its
    /// path.</summary>
    public static string WriteLayoutView(string cellDir, string fileNameWithoutExt, LayoutView model)
    {
        string path = ViewPath(cellDir, ViewType.Layout, fileNameWithoutExt);
        LayoutPersistence.SaveToFile(path, model);
        return path;
    }

    /// <summary>
    /// The path a view file of this type and name takes inside a cell folder, and the sub-folder
    /// created if a hand-made cell folder is missing it. A caller that has just run
    /// <see cref="CellFolder.CreateCellFolder"/> already has all three.
    /// </summary>
    private static string ViewPath(string cellDir, ViewType type, string fileNameWithoutExt)
    {
        string dir = CellFolder.SubFolderPath(cellDir, type);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, fileNameWithoutExt + CellFolder.ViewExtension(type));
    }
}
