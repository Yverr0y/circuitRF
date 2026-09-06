// ================================================================
//  TraceRowSelectionSurvivesReloadTests.cs
//
//  A user report carried a burst of four `row.group … -> ''` breadcrumbs on every post-run library
//  reload. Those cannot come from the restore at the end of RebuildSignals — that has always run
//  under _suppressDataCallback and prints nothing. They are the bound ComboBox writing back: the
//  two Clear() calls at the top of the rebuild ran with the guard DOWN, and AvailableGroups is a
//  combo's ItemsSource with SelectedGroup bound TwoWay.
//
//  The damaging push is not the obvious one. The null the control sends on Clear() is inert —
//  FilterSignalsToGroup empties AvailableSignals before its null check, so the follow-on
//  `SelectedSignal = AvailableSignals.FirstOrDefault()` is null and OnSelectedSignalChanged's own
//  null guard absorbs it. The push that hurts is the control re-selecting row 0 when the list
//  REFILLS: the signals are rebuilt by then, so the same assignment lands on the FIRST item and
//  re-points the trace, while the restore still puts the CARD back on the user's pick. The card and
//  the trace then disagree — the card reads DC1.I and the plot draws DC1.V, which is worse than
//  either being wrong on its own, because nothing on screen says so.
//
//  A coercing control's write-back is not an edit. The guard now covers the whole rebuild.
//
//  These tests stand in for the ComboBox in both directions and do nothing else — no Avalonia. On
//  the pre-fix arrangement three of the four fail; the fourth is the over-suppression guard and
//  must pass either way.
// ================================================================

using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using CircuitRF.Ui.DataDisplay;
using CircuitRF.Ui.DataDisplay.ViewModels;
using RfCore;
using RfCore.Data;
using RfCore.Export;
using Xunit;

namespace CircuitRF.Ui.Tests;

public sealed class TraceRowSelectionSurvivesReloadTests : IDisposable
{
    private readonly string _dir;

    public TraceRowSelectionSurvivesReloadTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "crf-rowreload-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>
    /// A DC operating point in the shape <c>DcResultPacker</c> writes: V on a labelled node axis, I
    /// on a labelled branch axis, and the scalars. V is added first, so I is never the fallback the
    /// broken path lands on — which is the whole point of binding the trace to I.
    /// </summary>
    private string WriteDcNpy(string fileName, double scale = 1.0)
    {
        var ds = new DataSet();

        string[] nodes = ["in", "out", "vd"];
        var nodeAxis = new Axis("node", [0, 1, 2], "V", nodes);
        ds.AddToGroup("DC1", "V", new DataCube([nodeAxis], [1.0 * scale, 2.0 * scale, 3.0 * scale]));

        ds.AddToGroup("DC1", "Converged", DataCube.Scalar(1.0));
        ds.AddToGroup("DC1", "Residual",  DataCube.Scalar(1e-12));

        string[] branches = ["IP1", "IP2"];
        var branchAxis = new Axis("branch", [0, 1], "A", branches);
        ds.AddToGroup("DC1", "I", new DataCube([branchAxis], [0.01 * scale, 0.02 * scale]));
        ds.AddToGroup("DC1", "__ProbeBranches", new DataCube(
            [new Axis("probe", [0, 1], "", branches)], new double[2]));

        var labels = new Axis("label", [0, 1, 2], "", nodes);
        ds.AddToGroup("DC1", "__LabeledNodes", new DataCube([labels], new double[3]));

        string path = Path.Combine(_dir, fileName);
        DataSetExporter.Export(ds, path, ExportFormat.Npy);
        return path;
    }

    /// <summary>
    /// Stands in for the two bound ComboBoxes, in both directions a SelectingItemsControl actually
    /// pushes: null when its ItemsSource is emptied, and the first row again when items reappear
    /// under a null selection. Nothing else — no rendering, no Avalonia.
    ///
    /// <para>Only the refill push is damaging — see the file header for why the null one is not.</para>
    /// </summary>
    private static void AttachComboWriteBack(TraceRowViewModel row)
    {
        row.AvailableGroups.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset && row.AvailableGroups.Count == 0)
                row.SelectedGroup = null;
            else if (e.Action == NotifyCollectionChangedAction.Add && row.SelectedGroup is null)
                row.SelectedGroup = row.AvailableGroups[0];
        };
        row.AvailableSignals.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset && row.AvailableSignals.Count == 0)
                row.SelectedSignal = null;
        };
    }

    private async Task<(DataSourceLibraryViewModel Lib, TraceRowViewModel Row, string Path)>
        BuildRowOnCurrentAsync()
    {
        string path = WriteDcNpy("DC_run.npy");

        var lib = new DataSourceLibraryViewModel();
        await lib.LoadFileAsync(path);
        await lib.SelectDataSourceAsync(path);

        var trace = new Trace(new SNP([1e9], 1), MatrixType.S, 0, 0, DependentVarFormat.Mag)
        {
            SourcePath = path, CubeName = "DC1.I", Transform = CubeTransform.None,
        };
        var plot = new Plot(PlotType.Rect, FreqUnit.GHz);
        plot.Traces.Add(trace);

        var inspector = new PlotInspectorViewModel(plot, () => { }, lib);
        inspector.RebuildAndNotify();
        var row = inspector.Traces[0];

        // Pre-condition: the card is on the CURRENT cube, and it is not the first one in the group.
        Assert.Equal("DC1.I", row.Trace.CubeName);
        Assert.Equal("DC1.I", row.SelectedSignal?.CubeName);

        return (lib, row, path);
    }

    // ---- The reported bug ---------------------------------------------------

    [Fact]
    public async Task PostRunReload_KeepsTheTraceOnTheSignalTheUserPicked()
    {
        var (lib, row, path) = await BuildRowOnCurrentAsync();
        AttachComboWriteBack(row);

        // Exactly what a re-run does: rewrite the .npy, then refresh the entries that changed.
        WriteDcNpy("DC_run.npy", scale: 2.0);
        await lib.ReloadChangedAsync([path]);

        Assert.Equal("DC1.I", row.Trace.CubeName);          // what the plot draws
        Assert.Equal("DC1.I", row.SelectedSignal?.CubeName); // what the card says it draws
        Assert.Equal("DC1",   row.SelectedGroup);
    }

    /// <summary>
    /// Three runs, because the report is about a pick that has to be made again after EVERY run —
    /// a guard that only survives the first reload would still leave the user re-picking.
    /// </summary>
    [Fact]
    public async Task RepeatedReloads_NeverWalkTheSelectionBackToTheFirstSignal()
    {
        var (lib, row, path) = await BuildRowOnCurrentAsync();
        AttachComboWriteBack(row);

        for (int run = 1; run <= 3; run++)
        {
            WriteDcNpy("DC_run.npy", scale: 1.0 + run);
            await lib.ReloadChangedAsync([path]);
            Assert.Equal("DC1.I", row.Trace.CubeName);
            Assert.Equal("DC1.I", row.SelectedSignal?.CubeName);
        }
    }

    /// <summary>
    /// The same guard, driven through the row's own refresh entry point rather than the library —
    /// so a future caller that rebuilds the signal list some other way is covered too.
    /// </summary>
    [Fact]
    public async Task RefreshDataSources_IsNotAnEdit()
    {
        var (_, row, _) = await BuildRowOnCurrentAsync();
        AttachComboWriteBack(row);

        row.RefreshDataSources();

        Assert.Equal("DC1.I", row.Trace.CubeName);
        Assert.Equal("DC1.I", row.SelectedSignal?.CubeName);
    }

    /// <summary>
    /// A genuine user pick still works — the guard must absorb the control's write-back without
    /// also deafening the card to the click it exists to hear.
    /// </summary>
    [Fact]
    public async Task AUserPickStillChangesTheTrace()
    {
        var (_, row, _) = await BuildRowOnCurrentAsync();
        AttachComboWriteBack(row);

        var v = row.AvailableSignals.First(s => s.CubeName == "DC1.V");
        row.SelectedSignal = v;

        Assert.Equal("DC1.V", row.Trace.CubeName);
    }
}
