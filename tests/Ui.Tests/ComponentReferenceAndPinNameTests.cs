using CircuitRF.Ui.Schematic;
using CircuitRF.Ui.ViewModels;
using Xunit;

namespace CircuitRF.Ui.Tests;

// ──────────────────────────────────────────────────────────────────────────────
//  Two things a placed component from an imported library gets that it did not before
//  (owner, 2026-09-05).
//
//  1. THE NAME. A component library states the letter its parts are numbered from — the import
//     carries it onto the cell as a read-only `Reference` parameter — and an instance of such a cell
//     is now U1, U2, U3 rather than the generic X1, X2, X3.
//
//  2. THE PIN NAMES. A built-in component's pin labels are TEXT baked into its artwork, so the
//     schematic renderer never drew a pin name of its own; an imported symbol carries them as pin
//     NAMES instead, and a placed part therefore showed a body with unlabelled pins. The render model
//     now marks which pin names to draw, by a rule the symbol decides and the instance can override.
//
//  In CellStatGlobalsCollection: these build real cell folders and invalidate the resolver caches.
// ──────────────────────────────────────────────────────────────────────────────

[Collection(CellStatGlobalsCollection.Name)]
public sealed class ComponentReferenceAndPinNameTests : IDisposable
{
    private readonly string _root;
    private readonly string _ws;

    public ComponentReferenceAndPinNameTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "crf_refpin_" + Guid.NewGuid().ToString("N")[..8]);
        _ws   = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(_ws);
        WorkspacePersistence.SaveToFile(Path.Combine(_ws, ".cws"), new CwsFile());
        WorkspaceRootFinder.InvalidateCache();
        CellSymbolResolver.InvalidateAll();
    }

    public void Dispose()
    {
        CellSymbolResolver.InvalidateAll();
        WorkspaceRootFinder.InvalidateCache();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // ── Fixture ───────────────────────────────────────────────────────────────

    private static Symbol SymbolWithPins(params string[] pinNames)
    {
        var pins = new List<SymbolPin>();
        for (int i = 0; i < pinNames.Length; i++)
            pins.Add(new SymbolPin(i % 2 == 0 ? -300 : 300, 100 * i, i + 1, pinNames[i])
            {
                NameAlign = i % 2 == 0 ? SymbolPinNameAlign.Left : SymbolPinNameAlign.Right,
            });
        return new Symbol(
            [new RectPrimitive { ColorRole = SymbolColorRole.SymbolLine, Cx = 0, Cy = 0, W = 400, H = 400 }],
            pins, pinNames.Length);
    }

    /// <summary>A cell folder with a primary symbol and, when asked, the library's stated prefix.</summary>
    private string CreateCell(string name, Symbol symbol, string? referencePrefix)
    {
        string cellDir = CellFolder.CreateCellFolder(_ws, name);
        string symDir  = CellFolder.SubFolderPath(cellDir, ViewType.Symbol);
        Directory.CreateDirectory(symDir);
        SymbolPersistence.SaveToFile(Path.Combine(symDir, name + ".csym"), symbol);

        string ccellPath = Path.Combine(cellDir, CellFolder.CcellFileName);
        var ccell = CellPersistence.LoadFromFile(ccellPath);
        ccell.PrimarySymbol = name + ".csym";
        ccell.NumPorts      = symbol.Pins.Count;
        if (referencePrefix is not null)
            ccell.Parameters =
            [
                // Quoted exactly as ComponentImport writes it: a declared default is an EXPRESSION,
                // and a bare U would resolve as a variable reference rather than as the letter.
                new CcellParameter
                {
                    Name = CellReferenceDesignator.ParameterName,
                    DefaultExpression = $"\"{referencePrefix}\"",
                    ShowOnSchematic = false,
                },
            ];
        CellPersistence.SaveToFile(ccellPath, ccell);
        CellSymbolResolver.InvalidateAll();
        return cellDir;
    }

    /// <summary>An empty saved schematic to place into, and its edit model.</summary>
    private (SchematicEditModel Model, string CellDir) HostSchematic(string name = "Top")
    {
        string cellDir = CellFolder.CreateCellFolder(_ws, name);
        string schDir  = CellFolder.SubFolderPath(cellDir, ViewType.Schematic);
        Directory.CreateDirectory(schDir);
        string path = Path.Combine(schDir, name + ".csch");
        SchematicPersistence.SaveToFile(path, new SchematicEditModel());
        var (model, _, _) = SchematicPersistence.LoadFromFile(path);
        return (model, cellDir);
    }

    // ── 1. The name ───────────────────────────────────────────────────────────

    [Theory]
    // The bare letter, which is what most of the grammars state.
    [InlineData("\"U\"", "U")]
    // The number's own placeholder, and an already-numbered designator: three spellings of one
    // statement, and numbering from the second would give U?1 while the third would give U11.
    [InlineData("\"U?\"", "U")]
    [InlineData("\"U1\"", "U")]
    [InlineData("\"IC\"", "IC")]
    // Nothing usable falls back to what every cell instance was called before this existed.
    [InlineData("\"\"", "X")]
    [InlineData("\"123\"", "X")]
    [InlineData("", "X")]
    public void AStatedReference_IsReadAsAPrefix(string defaultExpression, string expected)
        => Assert.Equal(expected, CellReferenceDesignator.PrefixFrom(defaultExpression));

    [Fact]
    public async Task PlacingACellThatStatesAReference_NamesTheInstanceFromIt()
    {
        string part = CreateCell("Opamp", SymbolWithPins("IN+", "IN-", "VCC", "OUT"), referencePrefix: "U");
        var (model, _) = HostSchematic();
        var vm = new SchematicViewModel(model);

        await vm.CommitCellPlacementAsync(part, 0, 0, SymbolRotation.R0);
        await vm.CommitCellPlacementAsync(part, 1000, 0, SymbolRotation.R0);

        Assert.Equal(["U1", "U2"], model.Components.Select(c => c.InstanceName));
    }

    [Fact]
    public async Task PlacingACellThatStatesNone_IsStillX()
    {
        string part = CreateCell("Block", SymbolWithPins("1", "2"), referencePrefix: null);
        var (model, _) = HostSchematic();
        var vm = new SchematicViewModel(model);

        await vm.CommitCellPlacementAsync(part, 0, 0, SymbolRotation.R0);

        Assert.Equal("X1", Assert.Single(model.Components).InstanceName);
    }

    [Fact]
    public async Task TwoLIBRARIESWithDifferentPrefixes_NumberIndependently()
    {
        // The numbering is per-prefix, which is the whole point of honouring the library's letter:
        // two ICs and a transistor are U1, U2 and Q1, not X1, X2, X3.
        string ic  = CreateCell("Opamp", SymbolWithPins("A", "B"), referencePrefix: "U");
        string fet = CreateCell("Switch", SymbolWithPins("G", "D", "S"), referencePrefix: "Q");
        var (model, _) = HostSchematic();
        var vm = new SchematicViewModel(model);

        await vm.CommitCellPlacementAsync(ic,  0,    0, SymbolRotation.R0);
        await vm.CommitCellPlacementAsync(fet, 1000, 0, SymbolRotation.R0);
        await vm.CommitCellPlacementAsync(ic,  2000, 0, SymbolRotation.R0);

        Assert.Equal(["U1", "Q1", "U2"], model.Components.Select(c => c.InstanceName));
    }

    // ── 2. Which pin names are drawn ──────────────────────────────────────────

    [Theory]
    [InlineData("VDD",   true)]
    [InlineData("GND",   true)]
    [InlineData("P",     true)]     // a pin genuinely named P is a name, not a placeholder
    [InlineData("A1",    true)]
    [InlineData("1",     false)]    // what KitTemplateSymbol invents when the file names nothing
    [InlineData("12",    false)]
    [InlineData("P1",    false)]    // the renderer's own P<n> stand-in
    [InlineData("P12",   false)]
    [InlineData("",      false)]
    [InlineData(null,    false)]
    public void OnlyASTATEDNameIsWorthDrawing(string? name, bool expected)
        => Assert.Equal(expected, SymbolPinNames.IsStated(name));

    [Fact]
    public async Task APlacedImportedPart_DrawsItsPinNames()
    {
        string part = CreateCell("Opamp", SymbolWithPins("IN+", "IN-", "VCC", "OUT"), referencePrefix: "U");
        var (model, _) = HostSchematic();
        var vm = new SchematicViewModel(model);
        await vm.CommitCellPlacementAsync(part, 0, 0, SymbolRotation.R0);

        var rc = Assert.Single(vm.RenderModel!.Components);
        Assert.Equal(["IN+", "IN-", "VCC", "OUT"], rc.Ports.Select(p => p.Name));
        Assert.All(rc.Ports, p => Assert.True(p.ShowName));

        // The side each name runs to comes from the symbol, not from the renderer's guess.
        Assert.Equal(SymbolPinNameAlign.Left,  rc.Ports[0].NameAlign);
        Assert.Equal(SymbolPinNameAlign.Right, rc.Ports[1].NameAlign);
    }

    [Fact]
    public async Task APartWhOSEPinsAreOnlyNumbered_DrawsNothing()
    {
        // An auto-generated symbol already draws the port number INSIDE its body. Drawing the pin
        // name as well would put the same numeral beside it twice.
        string part = CreateCell("Block", SymbolWithPins("1", "2", "3"), referencePrefix: null);
        var (model, _) = HostSchematic();
        var vm = new SchematicViewModel(model);
        await vm.CommitCellPlacementAsync(part, 0, 0, SymbolRotation.R0);

        var rc = Assert.Single(vm.RenderModel!.Components);
        Assert.All(rc.Ports, p => Assert.False(p.ShowName));
    }

    [Fact]
    public async Task ABuiltIn_IsUntouched()
    {
        // Its pin labels are text inside its own artwork; a second copy drawn from the pin model
        // would double every one of them.
        var (model, _) = HostSchematic();
        model.Components.Add(new EditableComponent
        { InstanceName = "M1", Symbol = SymbolKind.FetCurtice, X = 0, Y = 0 });
        var vm = new SchematicViewModel(model);

        var rc = Assert.Single(vm.RenderModel!.Components);
        Assert.NotEmpty(rc.Ports);
        Assert.All(rc.Ports, p => Assert.False(p.ShowName));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TheInstanceOverride_ForcesBothWays_AndUndoRestoresFOLLOWTHESYMBOL()
    {
        string part = CreateCell("Opamp", SymbolWithPins("IN+", "OUT"), referencePrefix: "U");
        var (model, _) = HostSchematic();
        var vm = new SchematicViewModel(model);
        await vm.CommitCellPlacementAsync(part, 0, 0, SymbolRotation.R0);
        string id = model.Components[0].Id;

        Assert.True(vm.CanTogglePinNames(id));
        Assert.True(vm.PinNamesVisible(id));

        vm.TogglePinNames(id);
        Assert.False(vm.PinNamesVisible(id));
        Assert.All(vm.RenderModel!.Components[0].Ports, p => Assert.False(p.ShowName));

        vm.TogglePinNames(id);
        Assert.True(vm.PinNamesVisible(id));

        // Undo must land back on null — "follow the symbol" — and not merely on the opposite bool,
        // or the first toggle would pin the instance to whatever its symbol said at the time.
        vm.UndoRedo.Undo();
        vm.UndoRedo.Undo();
        Assert.Null(model.Components[0].ShowPinNames);
        Assert.True(vm.PinNamesVisible(id));
    }

    [Fact]
    public async Task ForcingItON_ShowsEvenTheORDINALNames()
    {
        // The point of an explicit override: a numbered part can be made to say which pin is which.
        string part = CreateCell("Block", SymbolWithPins("1", "2", "3"), referencePrefix: null);
        var (model, _) = HostSchematic();
        var vm = new SchematicViewModel(model);
        await vm.CommitCellPlacementAsync(part, 0, 0, SymbolRotation.R0);
        string id = model.Components[0].Id;

        Assert.False(vm.PinNamesVisible(id));
        vm.TogglePinNames(id);

        Assert.All(vm.RenderModel!.Components[0].Ports, p => Assert.True(p.ShowName));
    }

    [Fact]
    public async Task ABuiltIn_OffersNoToggle()
    {
        var (model, _) = HostSchematic();
        model.Components.Add(new EditableComponent
        { InstanceName = "R1", Symbol = SymbolKind.Resistor, X = 0, Y = 0 });
        var vm = new SchematicViewModel(model);

        Assert.False(vm.CanTogglePinNames(model.Components[0].Id));
        await Task.CompletedTask;
    }

    // ── 3. Which way a name runs: the side the pin is on ──────────────────────

    [Theory]
    // Horizontal beats vertical at a corner: a pin further out in x than in y is a side pin.
    [InlineData(-300,    0, SymbolPinNameAlign.Left)]
    [InlineData(+300,    0, SymbolPinNameAlign.Right)]
    [InlineData(-300, -100, SymbolPinNameAlign.Left)]
    [InlineData(   0, -300, SymbolPinNameAlign.Top)]
    [InlineData(   0, +300, SymbolPinNameAlign.Bottom)]
    [InlineData(-100, -300, SymbolPinNameAlign.Top)]
    // A pin AT the centre has no side, and picking one would be a guess.
    [InlineData(   0,    0, SymbolPinNameAlign.Center)]
    public void ASideIsTheDominantAxis(double x, double y, SymbolPinNameAlign expected)
        => Assert.Equal(expected, SymbolPinSides.SideOf(x, y, 0, 0));

    [Fact]
    public void AnAutoGeneratedCellSymbol_RunsEveryNameINWARD()
    {
        // The reported bug: a generated symbol left every pin on the default Left, so the whole
        // right-hand column's names ran outward, away from the body they belong to.
        var sym = AutoSymbolGenerator.Generate("Block", numPorts: 6);

        Assert.NotEmpty(sym.Pins);
        foreach (var pin in sym.Pins)
            Assert.Equal(pin.LocalX < 0 ? SymbolPinNameAlign.Left : SymbolPinNameAlign.Right, pin.NameAlign);
        Assert.Contains(sym.Pins, p => p.NameAlign == SymbolPinNameAlign.Left);
        Assert.Contains(sym.Pins, p => p.NameAlign == SymbolPinNameAlign.Right);
    }

    [Fact]
    public void ABuiltInsGeneratedPins_CarryTheSameRule()
    {
        // Nothing renders these — a built-in's pin labels are text inside its own artwork — but the
        // pins reach the symbol editor, and the rule is derived in one place for all of them.
        var fet = BuiltInSymbols.Primitives(SymbolKind.FetCurtice, 3);

        var gate   = fet.Pins.Single(p => p.Name == "g");
        var drain  = fet.Pins.Single(p => p.Name == "d");
        var source = fet.Pins.Single(p => p.Name == "s");
        Assert.Equal(SymbolPinNameAlign.Left,   gate.NameAlign);     // -200,   0
        Assert.Equal(SymbolPinNameAlign.Top,    drain.NameAlign);    //    0,-200
        Assert.Equal(SymbolPinNameAlign.Bottom, source.NameAlign);   //    0,+200
    }

    [Fact]
    public void ASideSurvivesAROUNDTRIPThroughTheFile()
    {
        string path = Path.Combine(_root, "s.csym");
        var sym = new Symbol([], [
            new SymbolPin(0, -300, 1, "TOP")    { NameAlign = SymbolPinNameAlign.Top },
            new SymbolPin(0, +300, 2, "BOTTOM") { NameAlign = SymbolPinNameAlign.Bottom },
        ], 2);
        SymbolPersistence.SaveToFile(path, sym);

        var back = SymbolPersistence.LoadFromFile(path);
        Assert.Equal(SymbolPinNameAlign.Top,    back!.Pins[0].NameAlign);
        Assert.Equal(SymbolPinNameAlign.Bottom, back.Pins[1].NameAlign);
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    [Fact]
    public void OnlyAnEXPLICITOverrideIsWritten()
    {
        string path = Path.Combine(_root, "t.csch");
        var model = new SchematicEditModel();
        model.Components.Add(new EditableComponent { InstanceName = "U1", Symbol = SymbolKind.Generic });
        SchematicPersistence.SaveToFile(path, model);

        // Untouched: nothing in the file, so the instance keeps following its symbol forever.
        Assert.DoesNotContain("ShowPinNames", File.ReadAllText(path), StringComparison.Ordinal);

        foreach (bool value in (bool[])[true, false])
        {
            model.Components[0].ShowPinNames = value;
            SchematicPersistence.SaveToFile(path, model);
            var (reloaded, _, _) = SchematicPersistence.LoadFromFile(path);
            Assert.Equal(value, reloaded.Components[0].ShowPinNames);
        }

        model.Components[0].ShowPinNames = null;
        SchematicPersistence.SaveToFile(path, model);
        var (back, _, _) = SchematicPersistence.LoadFromFile(path);
        Assert.Null(back.Components[0].ShowPinNames);
    }

    [Fact]
    public void ACloneCarriesTheOverride()
    {
        var comp = new EditableComponent { InstanceName = "U1", Symbol = SymbolKind.Generic, ShowPinNames = false };
        Assert.False(comp.Clone().ShowPinNames);
        Assert.Null(new EditableComponent().Clone().ShowPinNames);
    }
}
