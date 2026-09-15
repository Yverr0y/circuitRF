// ================================================================
//  ExampleWorkspacesTests.cs — Tools ▸ Examples.
//
//  Three things are gated here, and each of them has failed silently in this repo before:
//
//   1. THE EXAMPLES SHIP. `tools/pcell-python` was correct under `dotnet run` for months and absent
//      from every packaged build, because the item group that copied it was never checked anywhere
//      but in a development tree. So the assertion here is about the BUILD OUTPUT, not the source
//      folder, and about the item group reaching publish as well as build.
//
//   2. THE INDEX AND THE DISK AGREE. `examples.json` gives the menu its order and its titles; a
//      folder named there that is not on disk is skipped at run time rather than shown as a row that
//      fails when pressed — which is right, and is also exactly how a missing example would go
//      unnoticed. The test is the other direction too: a workspace nobody indexed never appears.
//
//   3. THE EXAMPLES RUN. Every one of them was authored against a real engine, and an example that
//      silently stopped elaborating is worse than no example. Elaboration, not simulation — the
//      loadpull and EM benches are minutes of work each and belong to nobody's routine gate.
// ================================================================

using System.Text.Json;
using CircuitRF.Design.Schematic;
using CircuitRF.Design.Workspace;
using CircuitRF.Ui.Schematic;
using CircuitRF.Ui.ViewModels;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests.Examples;

public sealed class ExampleWorkspacesTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _tmp = Path.Combine(
        Path.GetTempPath(), "crf-examples-" + Guid.NewGuid().ToString("N")[..12]);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "circuitRF.slnx")))
            dir = Path.GetDirectoryName(dir) ?? "";
        return dir.Length > 0 ? dir : AppContext.BaseDirectory;
    }

    private static string SourceExamplesRoot() => Path.Combine(RepoRoot(), "examples");

    // ══ 1. They ship ════════════════════════════════════════════════════════

    /// <summary>
    /// The examples are in the BUILD OUTPUT beside the test assembly, not merely in the repository.
    ///
    /// <para>This is the assertion `tools/pcell-python` did not have. `ExampleWorkspaces.ResolveRoot`
    /// looks beside the executable first and only then walks up for the source tree, so a broken
    /// item group is invisible in a development build: the walk-up finds the repository and
    /// everything works until somebody installs it.</para>
    /// </summary>
    [Fact]
    public void TheExamplesAreCopiedBesideTheExecutable()
    {
        string beside = Path.Combine(AppContext.BaseDirectory, ExampleWorkspaces.RootFolderName);

        Assert.True(File.Exists(Path.Combine(beside, ExampleWorkspaces.IndexFileName)),
            $"No {ExampleWorkspaces.IndexFileName} at '{beside}'. The examples did not reach the "
          + "build output, so an installed copy would have no Tools ▸ Examples menu — check the "
          + "None/CopyToOutputDirectory item group in src/Ui/CircuitRF.Ui.csproj.");

        // Searched from the output directory ONLY, with no walk-up available to rescue it: passing
        // the base directory is not enough, because ResolveRoot walks up from whatever it is given.
        foreach (var example in ExampleWorkspaces.All(AppContext.BaseDirectory))
            Assert.True(File.Exists(Path.Combine(beside, example.Folder, ".cws")),
                $"'{example.Folder}' has no .cws in the build output — a workspace whose manifest "
              + "did not travel is not a workspace.");
    }

    /// <summary>
    /// The item group carries the examples to PUBLISH as well as to build, and excludes results.
    ///
    /// <para>A source scan rather than a publish run: publishing every runtime identifier costs
    /// minutes, and what can actually go wrong here is a metadata element, not a race. `None` with
    /// `CopyToOutputDirectory` flows to publish by default — what would break it is somebody adding
    /// `CopyToPublishDirectory=Never`, and that is what this reads for.</para>
    /// </summary>
    [Fact]
    public void TheItemGroupShipsTheExamplesAndNotTheirResults()
    {
        string proj = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Ui", "CircuitRF.Ui.csproj"));

        int at = proj.IndexOf("../../examples/**", StringComparison.Ordinal);
        Assert.True(at >= 0, "src/Ui/CircuitRF.Ui.csproj no longer copies examples/ into the app output.");

        string group = proj[at..proj.IndexOf("</None>", at, StringComparison.Ordinal)];
        Assert.Contains("<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>", group, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyToPublishDirectory>Never", group, StringComparison.Ordinal);
        Assert.Contains("results/**", group, StringComparison.Ordinal);
    }

    // ══ 2. The index and the disk agree ═════════════════════════════════════

    [Fact]
    public void EveryIndexedExampleIsOnDiskAndEveryWorkspaceOnDiskIsIndexed()
    {
        string root = SourceExamplesRoot();

        var indexed = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, ExampleWorkspaces.IndexFileName)))
            .RootElement.GetProperty("Examples")
            .EnumerateArray()
            .Select(e => e.GetProperty("Folder").GetString()!)
            .ToList();

        foreach (string folder in indexed)
            Assert.True(File.Exists(Path.Combine(root, folder, ".cws")),
                $"examples.json names '{folder}', which has no .cws. A named folder that is not "
              + "there is SKIPPED at run time, so the menu would quietly be one row shorter.");

        var onDisk = Directory.EnumerateDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, ".cws")))
            .Select(Path.GetFileName)
            .ToList();

        foreach (string? folder in onDisk)
            Assert.True(indexed.Contains(folder!, StringComparer.Ordinal),
                $"'{folder}' is a workspace under examples/ that examples.json does not name, so "
              + "nothing offers it. Add it to the index or move it out of examples/.");

        Assert.NotEmpty(indexed);
    }

    /// <summary>
    /// Discovery returns the index's own ORDER, with a title and a summary on every row — the three
    /// things a directory scan cannot supply, and the reason the index exists at all.
    /// </summary>
    [Fact]
    public void DiscoveryPreservesIndexOrderAndCarriesTitles()
    {
        var found = ExampleWorkspaces.All(SourceExamplesRoot());
        Assert.NotEmpty(found);

        var indexed = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(SourceExamplesRoot(), ExampleWorkspaces.IndexFileName)))
            .RootElement.GetProperty("Examples")
            .EnumerateArray().Select(e => e.GetProperty("Folder").GetString()!).ToList();

        Assert.Equal(indexed, found.Select(f => f.Folder).ToList());

        foreach (var e in found)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Title));
            Assert.False(string.IsNullOrWhiteSpace(e.Summary));
            Assert.True(File.Exists(e.CwsPath));
        }
    }

    /// <summary>A folder entry that tries to leave the examples tree is dropped, not resolved.</summary>
    [Fact]
    public void AnIndexEntryCannotNameAPathOutsideTheExamplesTree()
    {
        string root = Path.Combine(_tmp, "examples");
        Directory.CreateDirectory(Path.Combine(root, "Good"));
        File.WriteAllText(Path.Combine(root, "Good", ".cws"), "{}");
        File.WriteAllText(Path.Combine(root, ExampleWorkspaces.IndexFileName), """
            { "SchemaVersion": 1, "Examples": [
                { "Folder": "../..", "Title": "Escape" },
                { "Folder": "sub/dir", "Title": "Nested" },
                { "Folder": "Good", "Title": "Good" } ] }
            """);

        var found = ExampleWorkspaces.All(root);
        Assert.Equal(["Good"], found.Select(f => f.Folder));
    }

    // ══ 3. Installing one ═══════════════════════════════════════════════════

    /// <summary>
    /// The copy is a working workspace at the picked location, and the ORIGINAL is untouched — the
    /// examples are shipped files, and an install that wrote into them would corrupt the one copy
    /// every later install comes from.
    /// </summary>
    [Fact]
    public void InstallingAnExampleCopiesItAndLeavesTheOriginalAlone()
    {
        var example = ExampleWorkspaces.All(SourceExamplesRoot())[0];

        int before = Directory.GetFiles(example.Directory, "*", SearchOption.AllDirectories).Length;
        string parent = Path.Combine(_tmp, "picked");
        Directory.CreateDirectory(parent);

        var result = ExampleWorkspaceInstall.Run(example, parent);

        Assert.True(File.Exists(result.CwsPath));
        Assert.Empty(result.Failures);
        Assert.Equal(Path.Combine(parent, example.Folder), result.WorkspaceDir);
        Assert.True(result.FileCount > 0);

        Assert.Equal(before,
            Directory.GetFiles(example.Directory, "*", SearchOption.AllDirectories).Length);

        // Every cell folder made the trip. A .cws with no cells under it opens to an empty tree.
        Assert.Contains(Directory.EnumerateDirectories(result.WorkspaceDir),
            d => File.Exists(Path.Combine(d, ".ccell")));

        output.WriteLine($"{example.Title}: {result.FileCount} file(s) -> {result.WorkspaceDir}");
    }

    /// <summary>
    /// Installing twice into one folder is REFUSED by name, never overwritten and never renamed.
    /// The whole point of handing someone an editable copy is that their edits survive.
    /// </summary>
    [Fact]
    public void InstallingOverAnExistingFolderIsRefused()
    {
        var example = ExampleWorkspaces.All(SourceExamplesRoot())[0];
        string parent = Path.Combine(_tmp, "twice");
        Directory.CreateDirectory(parent);

        ExampleWorkspaceInstall.Run(example, parent);

        string? refusal = ExampleWorkspaceInstall.Refusal(example, parent);
        Assert.NotNull(refusal);
        Assert.Contains(example.Folder, refusal!, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => ExampleWorkspaceInstall.Run(example, parent));
    }

    // ══ 4. They still work ══════════════════════════════════════════════════

    /// <summary>
    /// Every schematic in every example still extracts to a netlist that elaborates, with the
    /// hierarchy intact.
    ///
    /// <para><b>The instance count is asserted, not just the absence of an exception.</b> A cell
    /// instance that cannot be resolved is SKIPPED by the extractor, not refused — which is how the
    /// harmonic-balance benches came to be authored around a device that was silently not in the
    /// netlist. A bench that elaborates to its bias network and nothing else raises no error
    /// anywhere.</para>
    /// </summary>
    [Fact]
    public void EveryExampleSchematicExtractsWithItsDeviceStillInIt()
    {
        int checkedDocs = 0;

        foreach (var example in ExampleWorkspaces.All(SourceExamplesRoot()))
        foreach (string csch in Directory.EnumerateFiles(example.Directory, "*.csch",
                                                         SearchOption.AllDirectories))
        {
            var (model, _, _) = SchematicPersistence.LoadFromFile(csch);
            model.SchematicDirectory = Path.GetDirectoryName(csch);

            var extracted = NetExtractor.Extract(model, Path.GetFileNameWithoutExtension(csch),
                                                 DiskCellResolver.Instance);

            Assert.True(extracted.Conflicts.Count == 0,
                $"{example.Folder}/{Path.GetFileName(csch)}: {string.Join("; ", extracted.Conflicts)}");

            int placed = model.Components.Count(c =>
                c.CellRef is null
                    ? c.Symbol is not (SymbolKind.Var or SymbolKind.Meas or SymbolKind.Ground
                                       or SymbolKind.Pin)
                    : true);

            Assert.True(extracted.TestBench.Instances.Count > 0,
                $"{example.Folder}/{Path.GetFileName(csch)} extracted to nothing.");
            Assert.True(extracted.TestBench.Instances.Count >= placed - 2,
                $"{example.Folder}/{Path.GetFileName(csch)}: {placed} placed part(s) but only "
              + $"{extracted.TestBench.Instances.Count} reached the netlist. A cell instance that "
              + "does not resolve is skipped in silence.");

            checkedDocs++;
        }

        Assert.True(checkedDocs >= 6, $"only {checkedDocs} example schematic(s) were checked");
        output.WriteLine($"{checkedDocs} example schematic(s) extracted cleanly");
    }

    /// <summary>
    /// Every example carries at least one analysis somewhere, and no example carries an absolute
    /// path out of somebody's home directory.
    /// </summary>
    [Fact]
    public void EveryExampleDeclaresAnalysesAndNamesNobodysMachine()
    {
        foreach (var example in ExampleWorkspaces.All(SourceExamplesRoot()))
        {
            bool runnable = false;
            foreach (string doc in Directory.EnumerateFiles(example.Directory, "*",
                                                            SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(doc);
                if (ext is not (".csch" or ".cem" or ".clay" or ".cws" or ".ccell")) continue;

                string text = File.ReadAllText(doc);
                Assert.DoesNotContain("/Users/", text, StringComparison.Ordinal);
                Assert.DoesNotContain("C:\\\\Users", text, StringComparison.Ordinal);
                Assert.DoesNotContain("/home/", text, StringComparison.Ordinal);

                if (ext is ".cem") runnable = true;
                if (ext is ".csch" && text.Contains("\"Analyses\"", StringComparison.Ordinal)
                                   && text.Contains("\"Type\"", StringComparison.Ordinal))
                    runnable = true;
            }
            Assert.True(runnable, $"'{example.Title}' has nothing to run.");
        }
    }

    // ══ 5. Both menu surfaces ═══════════════════════════════════════════════

    /// <summary>
    /// The in-window rows carry a real command and the example's own folder as the parameter.
    ///
    /// <para>The row's <c>Command</c> is assigned rather than bound, and this is what says so: a
    /// binding written in code would resolve against whatever DataContext the row inherits once it
    /// is handed to a parent through <c>ItemsSource</c>, and would simply do nothing when pressed.
    /// A menu item that is present and inert is the failure worth a test.</para>
    /// </summary>
    [Fact]
    public void TheInWindowMenuRowsAreWiredToTheCommand()
    {
        var vm = new WorkspaceViewModel();

        Assert.Equal(WorkspaceViewModel.Examples.Count, vm.ExampleMenuItems.Count);

        foreach (var (control, example) in vm.ExampleMenuItems.Zip(WorkspaceViewModel.Examples))
        {
            var item = Assert.IsType<Avalonia.Controls.MenuItem>(control);
            Assert.Equal(example.Title, item.Header);
            Assert.Equal(example.Folder, item.CommandParameter);
            Assert.Same(vm.OpenExampleCommand, item.Command);
            Assert.True(item.Command!.CanExecute(example.Folder));
        }
    }

    /// <summary>
    /// Both menu surfaces offer Examples. They are hand-mirrored — the macOS <c>NativeMenu</c> and
    /// the in-window <c>Menu</c> — and every comment in that file says the two must not drift.
    /// </summary>
    [Fact]
    public void BothMenuSurfacesCarryTheExamplesEntry()
    {
        string xaml = File.ReadAllText(RepoFile("src", "Ui", "Views", "WorkspaceWindow.axaml"));
        Assert.Contains("<NativeMenuItem Header=\"Examples\">", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ExampleMenuItems}\"", xaml, StringComparison.Ordinal);

        // The macOS side is filled in from code, so the item existing in XAML proves nothing on its
        // own — the rebuild has to be reachable from the window's own DataContext wiring.
        string codeBehind = File.ReadAllText(RepoFile("src", "Ui", "Views", "WorkspaceWindow.axaml.cs"));
        Assert.Contains("RebuildNativeExamplesMenu();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("WorkspaceViewModel.Examples", codeBehind, StringComparison.Ordinal);
    }

    private static string RepoFile(params string[] parts)
        => Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray());
}
