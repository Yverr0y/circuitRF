using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace CircuitRF.Ui.Tests.Harmonica;

/// <summary>
/// brief-harmonicarf-r3a §2.4 — a FLOOR, not the fix (the fix is
/// <c>HarmonicaMenuView.RecomputeAttachment</c> never handing a window a second <c>NativeMenu</c>
/// instance — see <c>HarmonicaMenuNativeAttachTests</c> and <c>src/Ui/RESOLVED.md</c>). Even so, a
/// queued <c>AvaloniaNativeMenuExporter.DoLayoutReset</c> that throws runs on the dispatcher, where no
/// call-site <c>try</c>/<c>catch</c> can reach it. <c>App</c> cannot be constructed headlessly (no
/// Avalonia platform in this suite), so this pins the wiring by source scan, the same fallback this
/// codebase already uses for App-host wiring.
/// </summary>
public class HarmonicaMenuDispatcherBackstopTests
{
    private static string AppSource([CallerFilePath] string here = "")
    {
        var dir = Path.GetDirectoryName(here);
        while (dir is not null && !File.Exists(Path.Combine(dir, "CLAUDE.md")))
            dir = Path.GetDirectoryName(dir);
        Assert.True(dir is not null, "Could not locate the repo root (no CLAUDE.md found walking up from this test file).");
        return File.ReadAllText(Path.Combine(dir!, "src", "Ui", "App.axaml.cs"));
    }

    [Fact]
    public void WireNativeMenuDispatcherBackstop_IsCalled_FromTheMacOsStartupBlock()
    {
        string src = AppSource();

        // OnFrameworkInitializationCompleted has TWO "if (OperatingSystem.IsMacOS())" blocks — the
        // one this backstop must sit beside is the one that also wires
        // ApplyMacOsDockIcon/WireAppMenuItems/BuildBgMenuWindow, found by anchoring on that call.
        int anchorIdx = src.IndexOf("WireAppMenuItems();", System.StringComparison.Ordinal);
        Assert.True(anchorIdx >= 0, "Expected to find the WireAppMenuItems() call.");
        int macBlockIdx = src.LastIndexOf("if (OperatingSystem.IsMacOS())", anchorIdx, System.StringComparison.Ordinal);
        Assert.True(macBlockIdx >= 0);
        int blockEnd = src.IndexOf("\n            }", macBlockIdx, System.StringComparison.Ordinal);
        Assert.True(blockEnd >= 0);
        string block = src[macBlockIdx..blockEnd];

        Assert.Contains("WireNativeMenuDispatcherBackstop();", block, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Backstop_SubscribesToDispatcherUnhandledException_AndOnlyHandlesTheKnownException()
    {
        string src = AppSource();

        int methodStart = src.IndexOf("private static void WireNativeMenuDispatcherBackstop()", System.StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "Expected to find WireNativeMenuDispatcherBackstop.");
        int methodEnd = src.IndexOf("\n    }", methodStart, System.StringComparison.Ordinal);
        Assert.True(methodEnd >= 0);
        string body = src[methodStart..methodEnd];

        Assert.Contains("Dispatcher.UIThread.UnhandledException +=", body, System.StringComparison.Ordinal);
        Assert.Contains("IsKnownNativeMenuMismatch(", body, System.StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", body, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The narrowing itself, wherever it is written. It moved out of the handler when the call-site
    /// guard in <c>WorkspaceViewModel.AttachSharedNativeMenuIfMacOS</c> needed the same test
    /// (2026-09-08), so this scans the predicate the handler calls — but the property it pins is
    /// unchanged: never a blanket "is this an ArgumentException", always the exception TYPE, the
    /// specific message, AND a stack that actually originates in Avalonia.Native.
    /// </summary>
    [Fact]
    public void TheKnownExceptionPredicate_IsNarrow()
    {
        string src = AppSource();

        int start = src.IndexOf("internal static bool IsKnownNativeMenuMismatch(", System.StringComparison.Ordinal);
        Assert.True(start >= 0, "Expected to find IsKnownNativeMenuMismatch.");
        int end = src.IndexOf(";", start, System.StringComparison.Ordinal);
        Assert.True(end >= 0);
        string body = src[start..end];

        Assert.Contains("is ArgumentException", body, System.StringComparison.Ordinal);
        Assert.Contains("menu being updated does not match", body, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Avalonia.Native", body, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The call-site guard the dispatcher backstop cannot stand in for. Avalonia raises a window's
    /// <c>Activated</c> from <c>DispatcherImpl.RunLoop</c>'s native callback rather than from a
    /// dispatcher job, so a throw out of <c>NativeMenu.SetMenu</c> there never reaches
    /// <c>Dispatcher.UIThread.UnhandledException</c> and takes the process down instead — which is
    /// what an owner hit closing a workspace on macOS (2026-09-08). A menu bar that fails to attach
    /// is cosmetic and may never be fatal, so the <c>SetMenu</c> must stay wrapped, and wrapped by
    /// the SAME predicate rather than by a bare <c>catch</c>.
    /// </summary>
    [Fact]
    public void TheSharedMenuAttach_GuardsItsSetMenuWithTheSamePredicate()
    {
        string src = WorkspaceViewModelSource();

        int start = src.IndexOf("private static void AttachSharedNativeMenuIfMacOS(", System.StringComparison.Ordinal);
        Assert.True(start >= 0, "Expected to find AttachSharedNativeMenuIfMacOS.");
        int end = src.IndexOf("\n    }", start, System.StringComparison.Ordinal);
        Assert.True(end >= 0);
        string body = src[start..end];

        Assert.Contains("NativeMenu.SetMenu(", body, System.StringComparison.Ordinal);
        Assert.Contains("catch", body, System.StringComparison.Ordinal);
        Assert.Contains("App.IsKnownNativeMenuMismatch(", body, System.StringComparison.Ordinal);
    }

    private static string WorkspaceViewModelSource([CallerFilePath] string here = "")
    {
        var dir = Path.GetDirectoryName(here);
        while (dir is not null && !File.Exists(Path.Combine(dir, "CLAUDE.md")))
            dir = Path.GetDirectoryName(dir);
        Assert.True(dir is not null, "Could not locate the repo root (no CLAUDE.md found walking up from this test file).");
        return File.ReadAllText(Path.Combine(dir!, "src", "Ui", "ViewModels", "WorkspaceViewModel.cs"));
    }
}
