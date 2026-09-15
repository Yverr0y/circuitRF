// ================================================================
//  CliHierarchyExtractionTests.cs — the headless side descends into a cell.
//
//  THE DEFECT (2026-09-15). NetExtractor.Extract takes an ICellResolver as an OPTIONAL argument and
//  treats a null one as "flat caller — skip silently"; both CLI extraction sites passed null. The
//  result was not a failure anywhere. A design whose device lives in a sub-cell netlisted to the
//  passive network around the hole where the device used to be, RAN, converged on every point, and
//  reported a power amplifier at -72 dB of gain. `circuitrf check` called the same design clean,
//  because it extracted the same way. The window has always passed its own resolver, so the two
//  disagreed about what the design WAS, and the root CLAUDE.md's promise — "a `.cnl` that works
//  headless works when opened" — was false in the one direction nobody looks.
//
//  These tests are written against a REAL shipped example rather than a fixture built here: the
//  claim is about what the application extracts, and a two-cell model assembled in a test is one
//  this test also decided the shape of.
// ================================================================

using System.Diagnostics;
using CircuitRF.Cli;
using CircuitRF.Core.Design;
using CircuitRF.Core.Netlist;
using CircuitRF.Design.Schematic;
using CircuitRF.Design.Workspace;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests.Cli;

public sealed class CliHierarchyExtractionTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _tmp = Path.Combine(
        Path.GetTempPath(), "crf-hier-" + Guid.NewGuid().ToString("N")[..12]);

    public void Dispose() { try { Directory.Delete(_tmp, true); } catch { /* best effort */ } }

    /// <summary>The harmonic-balance power amplifier: a bias network plus one instance of a FET cell
    /// whose SDD lives a level down. The bench that exposed this.</summary>
    private static string HierarchicalSchematic()
    {
        var hb = ExampleWorkspaces.All(Path.Combine(RepoRoot(), "examples"))
            .First(e => e.Folder == "Harmonic Balance");
        return Path.Combine(hb.Directory, "PowerAmplifier", "schematic", "PowerAmplifier.csch");
    }

    // ══ The extraction itself ═══════════════════════════════════════════════

    /// <summary>
    /// The netlist the CLI produces contains the SUB-CELL's device.
    ///
    /// <para>Asserted on the device rather than on an instance count, because the failure was not a
    /// count being low — it was an SDD not being there at all while everything else was.</para>
    /// </summary>
    [Fact]
    public void ExtractingASchematicDescendsIntoItsCells()
    {
        string cnl = CircuitSource.CnlTextOf(HierarchicalSchematic());

        Assert.Contains("define FET", cnl, StringComparison.Ordinal);
        Assert.Contains("SDD:", cnl, StringComparison.Ordinal);
        Assert.Contains("FET:X1", cnl, StringComparison.Ordinal);

        output.WriteLine(cnl[..Math.Min(400, cnl.Length)]);
    }

    /// <summary>
    /// The headless resolver and the WINDOW's resolver produce the same netlist, byte for byte.
    ///
    /// <para>This is the assertion that makes <see cref="DiskCellResolver"/> a shared descent rather
    /// than a second one. The window's own <c>Resolve</c> adds exactly one thing this cannot have —
    /// memory-else-disk, so an unsaved tab is what a run sees — and with nothing open the two must
    /// agree completely. If they ever stop agreeing, a design simulates differently depending on
    /// whether a window was involved, which is precisely what was wrong before.</para>
    /// </summary>
    [Fact]
    public void TheHeadlessResolverAgreesWithTheWindowsOwnDescent()
    {
        string path = HierarchicalSchematic();
        var (model, _, _) = SchematicPersistence.LoadFromFile(path);
        model.SchematicDirectory = Path.GetDirectoryName(path);

        var headless = NetExtractor.Extract(model, "tb", DiskCellResolver.Instance);
        var windowed = NetExtractor.Extract(model, "tb", new WindowShapedResolver());

        Assert.Equal(CnlWriter.Write(windowed.TestBench, windowed.Library, "x"),
                     CnlWriter.Write(headless.TestBench, headless.Library, "x"));
        Assert.Empty(headless.Conflicts);
    }

    /// <summary>
    /// <c>WorkspaceViewModel.Resolve</c>'s shape, transcribed — the same
    /// <see cref="HierarchyResolver.ResolvePrimaryPath"/>, the same cell folder two levels up, the
    /// same <c>.ccell</c> parameter interface, the same absolute folder as the key. Written out
    /// rather than calling the view model, which needs a workspace, a dock factory and a window.
    /// </summary>
    private sealed class WindowShapedResolver : ICellResolver
    {
        public CellResolution? Resolve(EditableComponent cellInstance, SchematicEditModel containing)
        {
            if (HierarchyResolver.ResolvePrimaryPath(cellInstance, containing) is not { } primary)
                return null;

            var (schematic, _, _) = SchematicPersistence.LoadFromFile(primary);
            schematic.SchematicDirectory = Path.GetDirectoryName(primary);

            string cellDir = Path.GetDirectoryName(Path.GetDirectoryName(primary))!;
            IReadOnlyList<ParameterDeclaration> parameters = [];
            string ccell = Path.Combine(cellDir, CellFolder.CcellFileName);
            if (File.Exists(ccell))
                parameters = CellPersistence.LoadFromFile(ccell).Parameters
                    .Select(p => new ParameterDeclaration(
                        p.Name, p.DefaultExpression,
                        string.IsNullOrEmpty(p.Unit) ? null : p.Unit, hidden: !p.ShowOnSchematic))
                    .ToList();

            return new CellResolution(Path.GetFileName(cellDir), schematic, parameters,
                                      Path.GetFullPath(cellDir));
        }
    }

    // ══ Through the real process ════════════════════════════════════════════

    /// <summary>
    /// The verb, as a separate process, writes the same bytes — and a RUN of the same schematic
    /// produces a result the device is actually in.
    ///
    /// <para>The gain assertion is the one that would have caught the original defect and nothing
    /// else would: every structural check passed while the amplifier was reporting −72 dB.</para>
    /// </summary>
    [Fact]
    public void TheVerbRunsTheDesignWithItsDeviceInIt()
    {
        Directory.CreateDirectory(_tmp);
        string cnlPath = Path.Combine(_tmp, "pa.cnl");

        var extract = RunCli("netlist", HierarchicalSchematic(), "-o", cnlPath);
        Assert.Equal(0, extract.ExitCode);
        Assert.Equal(CircuitSource.CnlTextOf(HierarchicalSchematic()), File.ReadAllText(cnlPath));

        // Exported as text and READ BACK, rather than scraped off stdout: the console prints a
        // measurement as a formatted table with an axis header, which is a presentation choice this
        // test has no business depending on. The `.txt` export is a documented result format.
        string txt = Path.Combine(_tmp, "pa.txt");
        var run = RunCli("hb", cnlPath, "-o", txt);
        Assert.Equal(0, run.ExitCode);

        // Gain, from the bench's own measurement. A bias network with no device in it cannot make
        // one: the amplifier that exposed this reported Gt_dB around -72 at every drive level.
        double bestGain = MaxOf(txt, "measurements.Gt_dB");
        output.WriteLine($"peak Gt_dB = {bestGain:F2}");
        Assert.True(bestGain > 0,
            $"The best Gt_dB over the drive sweep was {bestGain:F2} dB. A passive network cannot "
          + "have gain — the device is not in the netlist, which is what a silently dropped cell "
          + "instance looks like from here.");
    }

    /// <summary>The largest value in one named block of a `-o out.txt` export.</summary>
    private static double MaxOf(string txtPath, string block)
    {
        var lines = File.ReadAllLines(txtPath);
        int at = Array.FindIndex(lines, l => l.Trim() == "# " + block);
        Assert.True(at >= 0, $"'{block}' is not in the exported result.");

        double best = double.NegativeInfinity;
        for (int i = at + 2; i < lines.Length && !lines[i].StartsWith('#'); i++)
        {
            string[] cols = lines[i].Split('\t');
            if (cols.Length >= 2 && double.TryParse(cols[^1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
                best = Math.Max(best, v);
        }
        Assert.True(best > double.NegativeInfinity, $"'{block}' held no numbers.");
        return best;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory       = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        psi.ArgumentList.Add(CliDll());
        foreach (string a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;
        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        proc.WaitForExit();
        return (proc.ExitCode, outTask.GetAwaiter().GetResult(), errTask.GetAwaiter().GetResult());
    }

    private static string CliDll()
    {
        string cliDir = System.Reflection.CustomAttributeExtensions
            .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(
                typeof(CliHierarchyExtractionTests).Assembly)
            .First(a => a.Key == "CliDir").Value!;
        return Path.Combine(cliDir, "CircuitRF.Cli.dll");
    }

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "circuitRF.slnx")))
            dir = Path.GetDirectoryName(dir) ?? "";
        return dir.Length > 0 ? dir : AppContext.BaseDirectory;
    }
}
