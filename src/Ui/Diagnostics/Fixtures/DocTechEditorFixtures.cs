using System.IO;
using CircuitRF.Design.Layout;
using CircuitRF.Ui.Layout;
using CircuitRF.Ui.Views.Layout;

namespace CircuitRF.Ui.Diagnostics.Fixtures;

/// <summary>
/// The Technology Editor itself — the window a <c>.ctech</c> is edited in.
///
/// <para>Built on a <b>shipped</b> technology loaded through <see cref="ShippedTechnologies"/>, for
/// the reason every fixture in this folder is built from real objects rather than invented ones: a
/// figure assembled from a hand-written stackup would go on agreeing with itself after the shipped
/// file changed. Every row, number and unit in the picture is read off the <c>.ctech</c> that lands
/// in a new workspace's <c>tech/</c> folder, so a reader can open the same file and see the same
/// thing.</para>
/// </summary>
public static class DocTechEditorFixtures
{
    /// <summary>
    /// The Stackup tab on the shipped four-layer FR-4 board technology.
    ///
    /// <para>Four layers is the smallest board that shows every feature the chapter describes at
    /// once: two signal coppers and two planes, so <b>ground reference</b> is a designation and not
    /// just "the bottom one"; three dielectrics, so the stack is genuinely stratified; and two via
    /// entries with <em>different spans</em>, so "a via is a connection between two named conductors"
    /// is visible rather than asserted. A two-layer board makes all three of those look degenerate.</para>
    /// </summary>
    public static FigureScene StackupTab() => Tab(tabIndex: 1);

    private static FigureScene Tab(int tabIndex)
    {
        var tech = ShippedTechnologies.Load("pcb-4layer_FR-4_62mil_1oz");
        var vm   = new TechEditorViewModel(
            Path.Combine(Path.GetTempPath(), "pcb-4layer_FR-4_62mil_1oz.ctech"), tech)
        {
            SelectedTabIndex = tabIndex,
        };
        var view = new TechEditorView { DataContext = new TechDocument(tech.Name, vm, vm.FilePath) };
        return new FigureScene(view);
    }
}
