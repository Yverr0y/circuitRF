using System.IO;
using CircuitRF.Design.Cells;
using CircuitRF.Design.Layout;
using CircuitRF.Design.Workspace;
using CircuitRF.Ui.Layout;
using CircuitRF.Ui.Schematic;
using CircuitRF.Ui.ViewModels;

namespace CircuitRF.Ui.Tests;

// ──────────────────────────────────────────────────────────────────────────────
//  "I can't see which tech is used by the mlin component" (owner-relayed user
//  feedback, 2026-09-06) — and the follow-up: it has to be a PICKER, because nobody
//  can be expected to type a .ctech file name.
//
//  Neither half was visible anywhere before. A microstrip's H/T/Er/Sigma/TanD are
//  injected at extraction time and deliberately are not declared parameters, so the
//  schematic showed a width and a length and named nothing about the substrate they
//  describe.
// ──────────────────────────────────────────────────────────────────────────────

public sealed class MicrostripTechnologyPickerTests : IDisposable
{
    private readonly string _root;

    public MicrostripTechnologyPickerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"crfmstech_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string WriteTech(Technology tech, string fileStem)
    {
        var techDir = Path.Combine(_root, "tech");
        Directory.CreateDirectory(techDir);
        var path = Path.Combine(techDir, fileStem + ".ctech");
        TechPersistence.SaveToFile(path, tech);
        return path;
    }

    private void WriteCws(string? defaultTechAbs)
    {
        WorkspacePersistence.SaveToFileAtomic(Path.Combine(_root, ".cws"), new CwsFile
        {
            DefaultTechRef = defaultTechAbs is null ? null : Path.GetRelativePath(_root, defaultTechAbs),
        });
    }

    /// <summary>A schematic inside a cell of this workspace — the ancestor-.cws walk is what decides
    /// which technology a microstrip resolves, so the fixture has to have a real one.</summary>
    private string MakeSchematicDir()
    {
        var cellDir = CellFolder.CreateCellFolder(_root, "Amp");
        return CellFolder.SubFolderPath(cellDir, ViewType.Schematic);
    }

    private static ParameterEditorViewModel EditorFor(string schematicDir, SymbolKind kind)
    {
        var model = new SchematicEditModel { SchematicDirectory = schematicDir };
        var comp  = new EditableComponent { InstanceName = "TL1", Symbol = kind };
        model.Components.Add(comp);

        var vm = new ParameterEditorViewModel();
        vm.SetTargetDirect(new SchematicViewModel(model), comp, showClose: false);
        return vm;
    }

    [Fact]
    public void ThePanelNamesTheWorkspaceDefault_AndItsFile()
    {
        var tech = WriteTech(StarterTechnologies.Pcb2Layer(), "pcb");
        WriteCws(tech);

        var vm = EditorFor(MakeSchematicDir(), SymbolKind.Mlin);

        Assert.True(vm.IsMicrostripTarget);
        Assert.True(vm.MicrostripSubstrateResolved);
        Assert.Equal(StarterTechnologies.Pcb2Layer().Name, vm.MicrostripTechnologyText);

        // The tooltip is dynamic and carries the FILE, relative — that is the only thing that tells
        // two same-named technologies apart now that the label is the bare name.
        Assert.Contains("tech/pcb.ctech", vm.MicrostripTechnologyTooltip.Replace('\\', '/'));
        Assert.DoesNotContain(_root, vm.MicrostripTechnologyTooltip);
    }

    [Fact]
    public void ThePickerOffersEveryTechnologyInTheWorkspace_AndSelectsTheDefault()
    {
        var pcb  = WriteTech(StarterTechnologies.Pcb2Layer(), "pcb");
        var mmic = WriteTech(StarterTechnologies.MmicGaAs(), "mmic");
        WriteCws(mmic);

        var vm = EditorFor(MakeSchematicDir(), SymbolKind.Mlin);

        Assert.Equal(2, vm.MicrostripTechnologyOptions.Count);
        Assert.Equal(
            Path.GetFullPath(mmic),
            Path.GetFullPath(vm.SelectedMicrostripTechnology!.Path));
        Assert.Contains(vm.MicrostripTechnologyOptions, o => Path.GetFullPath(o.Path) == Path.GetFullPath(pcb));

        // The label is the NAME alone (owner: the option text had too many characters); the file
        // rides along for the tooltips, and never as an absolute path.
        Assert.All(vm.MicrostripTechnologyOptions, o => Assert.DoesNotContain(".ctech", o.Display));
        Assert.All(vm.MicrostripTechnologyOptions, o => Assert.Contains(".ctech", o.RelativePath));
        Assert.All(vm.MicrostripTechnologyOptions, o => Assert.DoesNotContain(_root, o.RelativePath));
        Assert.Equal(StarterTechnologies.MmicGaAs().Name, vm.SelectedMicrostripTechnology.Display);
    }

    /// <summary>
    /// The picker cannot be used unless a write path exists — a schematic has no technology of its own,
    /// so the only thing a selection can mean is the workspace default, and something has to be able
    /// to record it.
    /// </summary>
    [Fact]
    public void ThePickerIsDisabled_WhenThereIsNoWorkspaceToRecordTheChoiceIn()
    {
        var loose = Path.Combine(_root, "loose");   // no .cws anywhere above it
        Directory.CreateDirectory(loose);

        var vm = EditorFor(loose, SymbolKind.Mlin);

        Assert.True(vm.IsMicrostripTarget);
        Assert.Empty(vm.MicrostripTechnologyOptions);
        Assert.False(vm.CanChangeMicrostripTechnology);
        Assert.False(vm.CanEditMicrostripTechnology);
        // The one line left beside the picker: a FAILURE to resolve a substrate, which nothing else
        // in the application reports until a run.
        Assert.False(vm.MicrostripSubstrateResolved);
        Assert.Contains("defaults", vm.MicrostripSubstrateWarning);
    }

    [Fact]
    public void DecliningTheConfirmation_SnapsTheSelectionBack()
    {
        var pcb  = WriteTech(StarterTechnologies.Pcb2Layer(), "pcb");
        var mmic = WriteTech(StarterTechnologies.MmicGaAs(), "mmic");
        WriteCws(mmic);

        var vm = EditorFor(MakeSchematicDir(), SymbolKind.Mlin);
        vm.ChangeWorkspaceTechnologyAsync = _ => Task.FromResult(false);   // the user cancelled

        var before = vm.SelectedMicrostripTechnology;
        vm.SelectedMicrostripTechnology =
            vm.MicrostripTechnologyOptions.First(o => Path.GetFullPath(o.Path) == Path.GetFullPath(pcb));

        // A combo left showing a technology the workspace is not on would be a wrong readout produced
        // by the act of reading it.
        Assert.Equal(before, vm.SelectedMicrostripTechnology);
    }

    /// <summary>Edit… is how a user reaches the substrate values that used to be printed here (owner,
    /// 2026-09-06: they belong to the technology, and the panel is not the place to restate them). It
    /// opens the SELECTED file, not the workspace default read afresh.</summary>
    [Fact]
    public void Edit_OpensTheSelectedTechnologyFile()
    {
        var mmic = WriteTech(StarterTechnologies.MmicGaAs(), "mmic");
        WriteCws(mmic);

        var vm = EditorFor(MakeSchematicDir(), SymbolKind.Mlin);
        string? opened = null;
        vm.OpenTechnologyFile = p => opened = p;

        Assert.True(vm.CanEditMicrostripTechnology);
        vm.EditMicrostripTechnologyCommand.Execute(null);

        Assert.Equal(Path.GetFullPath(mmic), Path.GetFullPath(opened!));
    }

    [Fact]
    public void ANonMicrostripComponent_ShowsNoSubstratePanelAtAll()
    {
        WriteCws(WriteTech(StarterTechnologies.Pcb2Layer(), "pcb"));

        var vm = EditorFor(MakeSchematicDir(), SymbolKind.Resistor);

        Assert.False(vm.IsMicrostripTarget);
        Assert.Equal("", vm.MicrostripTechnologyText);
    }
}
