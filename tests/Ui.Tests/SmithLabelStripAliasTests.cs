// ================================================================
//  SmithLabelStripAliasTests.cs — a Smith chart's Y label honours the dataset alias
//
//  Owner report, 2026-09-09: every plot in a .cdd rendered its Y-axis label from the dataset
//  ALIASES except the Smith chart, which kept showing the file stem.
//
//  The alias resolution was never at fault — the ORDER was. A Rect plot's Y label is drawn inside
//  the Skia canvas and is rebuilt from the live library on every frame (PlotControl.Render builds
//  its own `aliasFor`), so it picks an alias up whenever one exists. Smith/Polar labels are
//  EXTERNAL strips: computed once per rebuild and cached on LabelStripViewModel.AutoLabel. The
//  container's constructor runs its first UpdateLabelStrips() while `Library` is still null,
//  because `Library` is set by the object initializer AFTER the constructor body — and nothing
//  rebuilt the strips when it arrived. Reopening a .cdd is exactly that shape: the Plot is fully
//  built (traces and all) before the container is constructed.
// ================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CircuitRF.Ui.DataDisplay;
using CircuitRF.Ui.DataDisplay.ViewModels;
using RfCore;
using RfCore.Data;
using RfCore.Export;
using Xunit;

namespace CircuitRF.Ui.Tests;

public sealed class SmithLabelStripAliasTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
            try { Directory.Delete(dir, recursive: true); } catch { }
    }

    private string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"crf_smithalias_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static string WriteNpy(string dir, string fileName)
    {
        var axis = new Axis("freq", new[] { 1e9, 2e9 }, "Hz");
        var data = new System.Numerics.Complex[] { new(0.1, -0.2), new(0.2, -0.1) };
        var cube = new DataCube(new[] { axis }, data);
        var ds   = new DataSet();
        ds.AddToGroup("SP1", "S", cube);
        var path = Path.Combine(dir, fileName);
        DataSetExporter.Export(ds, path, ExportFormat.Npy);
        return path;
    }

    /// <summary>
    /// Builds the container the way <c>DataDisplayViewModel.LoadPlotContainerConfigAsync</c> does
    /// when a <c>.cdd</c> is reopened: a Plot that already holds its traces, then the container,
    /// then <c>Library</c> through the object initializer. That order is the whole point of the
    /// test — constructing it any other way cannot reproduce what the owner saw.
    /// </summary>
    private static PlotContainerViewModel BuildRestoredContainer(
        DataDisplayViewModel display, DataSourceLibraryViewModel lib, PlotType plotType,
        string pathA, string pathB)
    {
        var snp  = new SNP(new[] { 1e9, 2e9 }, 2, MatrixType.S, MatrixFormat.MA);
        var plot = new Plot(plotType, FreqUnit.GHz);
        plot.Traces.Add(new Trace(snp, MatrixType.S, 0, 0, DependentVarFormat.Complex) { SourcePath = pathA });
        plot.Traces.Add(new Trace(snp, MatrixType.S, 0, 0, DependentVarFormat.Complex) { SourcePath = pathB });

        var plotVm    = new PlotViewModel(plot);
        var inspector = new PlotInspectorViewModel(plot, () => { }, lib);

        return new PlotContainerViewModel(plotVm, inspector, display)
        {
            Left = 0, Top = 0, Width = 400, Height = 400,
            Library = lib,
        };
    }

    private async Task<(DataDisplayViewModel Display, DataSourceLibraryViewModel Lib, string A, string B)>
        TwoAliasedSourcesAsync()
    {
        var dir   = MakeTempDir();
        var pathA = WriteNpy(dir, "edge_port_em.npy");
        var pathB = WriteNpy(dir, "internal_port_em.npy");

        var lib = new DataSourceLibraryViewModel();
        await lib.LoadFileAsync(pathA);
        await lib.LoadFileAsync(pathB);
        Assert.True(lib.TrySetAlias(lib.Entries[0], "Edge Port"));
        Assert.True(lib.TrySetAlias(lib.Entries[1], "Internal Port"));

        var display = new DataDisplayViewModel(lib, addEmptyPlot: false);
        display.CanvasSizeProvider = () => (800.0, 600.0);
        return (display, lib, pathA, pathB);
    }

    [Fact]
    public async Task ReopenedSmithPlot_LabelStripsUseTheDatasetAlias_NotTheFileStem()
    {
        var (display, lib, a, b) = await TwoAliasedSourcesAsync();

        var container = BuildRestoredContainer(display, lib, PlotType.Smith, a, b);

        var labels = container.LeftLabelStrips.Select(s => s.AutoLabel).ToList();
        Assert.Equal(2, labels.Count);
        Assert.All(labels, l => Assert.False(string.IsNullOrEmpty(l)));

        Assert.Contains(labels, l => l!.StartsWith("Edge Port", StringComparison.Ordinal));
        Assert.Contains(labels, l => l!.StartsWith("Internal Port", StringComparison.Ordinal));

        // And the file stem — the thing the owner actually saw on screen — is gone from both.
        Assert.All(labels, l => Assert.DoesNotContain("_em", l));
    }

    /// <summary>
    /// The Polar chart is the other external-strip plot type and went through the identical path,
    /// so it is fixed by the same line and is pinned here rather than left to be rediscovered.
    /// </summary>
    [Fact]
    public async Task ReopenedPolarPlot_LabelStripsUseTheDatasetAlias()
    {
        var (display, lib, a, b) = await TwoAliasedSourcesAsync();

        var container = BuildRestoredContainer(display, lib, PlotType.Polar, a, b);

        var labels = container.LeftLabelStrips.Select(s => s.AutoLabel).ToList();
        Assert.Equal(2, labels.Count);
        Assert.Contains(labels, l => l!.StartsWith("Edge Port", StringComparison.Ordinal));
        Assert.Contains(labels, l => l!.StartsWith("Internal Port", StringComparison.Ordinal));
    }
}
