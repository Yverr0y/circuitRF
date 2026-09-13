using System;
using System.IO;
using System.Linq;
using CircuitRF.Design.Layout;
using CircuitRF.Ui.Layout;
using Xunit;

namespace CircuitRF.Ui.Tests.StackupRender;

/// <summary>
/// brief-stackup-render-2-canvas-and-tab.md §2/§3's gate — the Stackup tab's re-layout.
///
/// <h3>Why these are source scans</h3>
/// <para>An .axaml layout change has no headlessly assertable rendered output — there is no
/// application host in this test project and no arrange pass to read back. That is the same fallback
/// <c>TechEditorNarrowWidthTests</c> and <c>TechEditorLayerColumnLayoutTests</c> already use for this
/// very view, and the reason those two are worth reading before changing anything here.</para>
///
/// <para><b>What keeps them from being tautologies</b> is that the sizes are not written in the
/// .axaml at all: the view reads them from <see cref="TechEditorMetrics"/> through <c>x:Static</c>,
/// so a scan for "the ListBox binds its MaxHeight to the constant" plus an arithmetic assertion about
/// the constant is a statement about what the control actually does. A typed number would make both
/// halves worthless.</para>
/// </summary>
public class TechEditorStackupTabLayoutTests
{
    // ── R-stk2-5 — the new structure, in order ───────────────────────────────────────────────────

    [Fact]
    public void TheStackupTabHolds_HeaderThenDrawingThenSplitterThenFilterThenCards_InThatOrder()
    {
        var tab = StackupTab();

        int header   = Require(tab, "ColumnDefinitions=\"Auto,Auto,Auto,Auto,*,Auto\"");
        int drawing  = Require(tab, "<ctrl:StackupCanvas");
        int splitter = Require(tab, "<GridSplitter");
        int filter   = Require(tab, "x:Name=\"StackupFilterBox\"");
        int cards    = Require(tab, "x:Name=\"StackupList\"");

        Assert.True(header < drawing,  "the boundary/Add/summary grid stays at the top");
        Assert.True(drawing < splitter, "the drawing is above the splitter");
        Assert.True(splitter < filter,  "the filter row moved BELOW the splitter — it filters the cards");
        Assert.True(filter < cards,     "the filter row still sits above the cards it filters");
    }

    /// <summary>A <c>DockPanel</c> cannot express a splitter between two proportional panes, which is
    /// the whole reason the tab's content stopped being one.</summary>
    [Fact]
    public void TheTabsContentIsAGridWithExplicitRowDefinitions()
    {
        var tab = StackupTab();

        int grid = Require(tab, "<Grid>");
        int rows = Require(tab, "<Grid.RowDefinitions>");
        Assert.True(grid < rows);

        // The drawing takes the leftover space; the cards pane opens at a stated height.
        Assert.Contains("<RowDefinition Height=\"*\"", tab);
    }

    // ── R-stk2-6 — the default split, and both minimums ──────────────────────────────────────────

    [Fact]
    public void BothPanesDeclareAMinimumHeight_SoTheSplitterCannotAnnihilateEither()
    {
        var tab = StackupTab();

        Assert.Contains("MinHeight=\"{x:Static lay:TechEditorMetrics.StackupDrawingMinHeight}\"", tab);
        Assert.Contains("MinHeight=\"{x:Static lay:TechEditorMetrics.StackupCardPaneMinHeight}\"", tab);

        Assert.True(TechEditorMetrics.StackupDrawingMinHeight > 0);
        Assert.True(TechEditorMetrics.StackupCardPaneMinHeight > 0);
    }

    [Fact]
    public void TheCardsPaneOpensAtRoughlyOneConductorCard()
    {
        Assert.Contains(
            "Height=\"{x:Static lay:TechEditorMetrics.StackupCardPaneOpeningHeight}\"", StackupTab());

        // "Roughly one conductor card" is exactly what the opening height IS — it is derived from the
        // card's own template rather than being a pixel count somebody liked the look of.
        Assert.Equal(TechEditorMetrics.ConductorCardHeight,
                     TechEditorMetrics.StackupCardPaneOpeningHeight.Value, 3);

        // …and it opens larger than its own floor, or the splitter would start pinned.
        Assert.True(TechEditorMetrics.ConductorCardHeight > TechEditorMetrics.StackupCardPaneMinHeight);
    }

    // ── R-stk2-7 — the drawing scrolls, and only vertically ──────────────────────────────────────

    [Fact]
    public void TheDrawingIsInAVerticalOnlyScrollViewer()
    {
        var tab = StackupTab();
        int canvas = Require(tab, "<ctrl:StackupCanvas");

        // The ScrollViewer immediately above the canvas is the drawing's.
        int open = tab.LastIndexOf("<ScrollViewer", canvas, StringComparison.Ordinal);
        Assert.True(open >= 0, "the drawing must sit inside a ScrollViewer");

        var element = tab[open..canvas];
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", element);

        // A horizontal scrollbar here would mean the scene got its own width wrong — brief 1 §2 has
        // it fit the pane by construction.
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", element);
    }

    // ── R-stk2-8 — the drawing-layer picker becomes 3 x 4 ────────────────────────────────────────

    [Fact]
    public void ThePickerDrawsThreeAcross()
    {
        var tab = StackupTab();

        int picker = Require(tab, "Classes=\"rows layerpicker\"");
        var block = tab[picker..(picker + 2400)];

        Assert.Contains("<UniformGrid Columns=\"{x:Static lay:TechEditorMetrics.DrawingLayerPickerColumns}\"", block);
        Assert.Equal(3, TechEditorMetrics.DrawingLayerPickerColumns);
    }

    /// <summary>
    /// <b>The picker must stay virtualized.</b> The obvious spelling of "three across" is a
    /// <c>UniformGrid</c> ITEMS PANEL over the flat option list — and a <c>UniformGrid</c> is not a
    /// virtualizing panel, so on the 377-layer technology the row VM's own comment names it would
    /// realize and arrange every one of them per conductor card. That is the same order of magnitude
    /// as the WrapPanel this control replaced, and it would have been reintroduced to save two rows
    /// of height.
    ///
    /// <para>So the three-across-ness lives in the ITEM TEMPLATE, over rows the view model chunked,
    /// and the ListBox keeps its default <c>VirtualizingStackPanel</c>. The gate is that the list
    /// binds to the chunked rows and declares no <c>ItemsPanel</c> of its own.</para>
    /// </summary>
    [Fact]
    public void ThePickerKeepsItsVirtualizingItemsPanel()
    {
        var tab = StackupTab();

        int picker = Require(tab, "Classes=\"rows layerpicker\"");
        var block = tab[picker..tab.IndexOf("</ListBox>", picker, StringComparison.Ordinal)];

        Assert.Contains("ItemsSource=\"{Binding FilteredDrawingLayerRows}\"", block);
        Assert.DoesNotContain("<ListBox.ItemsPanel>", block);
    }

    /// <summary>The chunking itself, on a technology with a ragged last row.</summary>
    [Fact]
    public void TheOptionsAreChunkedIntoRowsOfThree()
    {
        var vm = new TechEditorViewModel(
            Path.Combine(Path.GetTempPath(), "picker-chunking.ctech"),
            ShippedTechnologies.Load("pcb-4layer_FR-4_62mil_1oz"));

        var row = vm.StackupLayers.First(r => r.AllowMultipleDrawingLayers);
        int n = row.FilteredDrawingLayerOptions.Count;
        Assert.True(n > 3, "the shipped four-layer board must carry more drawing layers than one row");

        Assert.Equal((n + 2) / 3, row.FilteredDrawingLayerRows.Count);

        // Every option appears exactly once, in order, and the ragged last row carries nulls rather
        // than repeating anything.
        var flattened = row.FilteredDrawingLayerRows
            .SelectMany(r => new[] { r.A, r.B, r.C })
            .Where(i => i is not null)
            .ToList();
        Assert.Equal(row.FilteredDrawingLayerOptions, flattened!);

        var last = row.FilteredDrawingLayerRows[^1];
        Assert.Equal(last.B is not null, last.HasB);
        Assert.Equal(last.C is not null, last.HasC);
    }

    /// <summary>The filter narrows the ROWS as well, or the picker would keep scrolling to blanks.</summary>
    [Fact]
    public void TheFilterNarrowsTheRowsToo()
    {
        var vm = new TechEditorViewModel(
            Path.Combine(Path.GetTempPath(), "picker-filter.ctech"),
            ShippedTechnologies.Load("pcb-4layer_FR-4_62mil_1oz"));

        var row = vm.StackupLayers.First(r => r.AllowMultipleDrawingLayers);
        row.DrawingLayerFilter = row.FilteredDrawingLayerOptions[0].Name;

        Assert.NotEmpty(row.FilteredDrawingLayerOptions);
        Assert.Equal((row.FilteredDrawingLayerOptions.Count + 2) / 3, row.FilteredDrawingLayerRows.Count);
    }

    [Fact]
    public void ThePickersMaxHeightAdmitsExactlyFourRows()
    {
        var tab = StackupTab();

        Assert.Contains("MaxHeight=\"{x:Static lay:TechEditorMetrics.DrawingLayerPickerMaxHeight}\"", tab);

        // "Exactly four rows" is only a true statement because the ROW height is pinned too — the
        // theme's own CheckBox height is not ours and can change under us. That style is scoped to
        // the picker but declared with the view's other styles, so it is outside the tab's own text.
        var styles = Axaml();
        Assert.Contains("Selector=\"ListBox.layerpicker CheckBox\"", styles);
        Assert.Contains("Value=\"{x:Static lay:TechEditorMetrics.DrawingLayerRowHeight}\"", styles);

        Assert.Equal(4, TechEditorMetrics.DrawingLayerPickerRows);
        Assert.Equal(TechEditorMetrics.DrawingLayerRowHeight * 4,
                     TechEditorMetrics.DrawingLayerPickerMaxHeight, 6);
    }

    /// <summary>Avalonia clamps <c>Height</c> UP to <c>MinHeight</c>, so the theme's would simply win
    /// and the picker would be three rows tall instead of four with nothing saying so.</summary>
    [Fact]
    public void ThePickerItemZeroesTheThemesMinimumHeight()
    {
        var axaml = Axaml();
        int style = Require(axaml, "Selector=\"ListBox.layerpicker CheckBox\"");
        var block = axaml[style..axaml.IndexOf("</Style>", style, StringComparison.Ordinal)];

        Assert.Contains("Property=\"MinHeight\" Value=\"0\"", block);
    }

    /// <summary>Twelve visible instead of four, which is what makes the shorter picker a better one
    /// rather than merely a smaller one.</summary>
    [Fact]
    public void TwelveLayersAreVisibleAtOnce()
    {
        Assert.Equal(12,
            TechEditorMetrics.DrawingLayerPickerColumns * TechEditorMetrics.DrawingLayerPickerRows);
    }

    /// <summary>Three columns does not make a several-hundred-layer list findable, and the summary is
    /// what says what is ticked once the ticked ones have scrolled away. Both survive R-stk2-8.</summary>
    [Fact]
    public void TheFilterBoxAndTheSummaryBothSurvive()
    {
        var tab = StackupTab();
        Assert.Contains("Text=\"{Binding DrawingLayerFilter, Mode=TwoWay}\"", tab);
        Assert.Contains("Text=\"{Binding DrawingLayerSummary}\"", tab);
    }

    // ── R-stk2-9 — the conductor card got shorter ────────────────────────────────────────────────

    /// <summary>
    /// There is no arrange pass here to measure a card in, so what stands in for it is the card's own
    /// stated height — a sum over its template in <see cref="TechEditorMetrics"/>, which the class
    /// remarks are explicit about being nominal. What the assertion is really about is the only term
    /// in that sum this brief changed: the picker, which fell from the old typed 140 to four pinned
    /// rows. The ceiling sits between the two, so it fails if the picker grows back.
    /// </summary>
    [Fact]
    public void AConductorCardIsBelowItsStatedCeiling_AndShorterThanItWas()
    {
        Assert.True(TechEditorMetrics.DrawingLayerPickerMaxHeight
                    < TechEditorMetrics.DrawingLayerPickerMaxHeightBefore,
            "the 3-wide picker must be shorter than the 1-wide one it replaced");

        double before = TechEditorMetrics.ConductorCardHeight
                        - TechEditorMetrics.DrawingLayerPickerMaxHeight
                        + TechEditorMetrics.DrawingLayerPickerMaxHeightBefore;

        Assert.True(TechEditorMetrics.ConductorCardHeight <= TechEditorMetrics.ConductorCardHeightCeiling,
            $"the conductor card measures {TechEditorMetrics.ConductorCardHeight}, ceiling is "
            + TechEditorMetrics.ConductorCardHeightCeiling);
        Assert.True(before > TechEditorMetrics.ConductorCardHeightCeiling,
            "the ceiling must be low enough that the PREVIOUS card would have failed it");
    }

    // ── R-stk2-10 — Page Down still scrolls the card list ────────────────────────────────────────

    /// <summary>
    /// <c>TechEditorView.TargetScrollViewer</c> scrolls the ScrollViewer the FOCUSED control is
    /// inside, falling back to the visible tab's row list. Adding a second scrollable pane to this tab
    /// is exactly the kind of change that quietly re-points a keystroke, and two things keep it from
    /// doing so: the fallback for tab 1 is still <c>StackupList</c>, and the only thing inside the
    /// drawing's ScrollViewer is a canvas that takes no focus (asserted as a runtime fact in
    /// <see cref="StackupCanvasTests.TheCanvasTakesNoFocus_SoItCannotClaimPageUpAndPageDown"/>).
    /// </summary>
    [Fact]
    public void PageKeysStillFallBackToTheCardList_NotTheDrawing()
    {
        var code = RepoFile(Path.Combine("src", "Ui", "Views", "Layout", "TechEditorView.axaml.cs"));

        int map = Require(code, "SectionTabs?.SelectedIndex switch");
        var block = code[map..code.IndexOf("};", map, StringComparison.Ordinal)];
        Assert.Contains("1 => StackupList,", block);

        // The drawing's ScrollViewer holds the canvas and ONE focusable sibling — brief 4's inline
        // edit box (R-stk4-2), which is a TextBox and therefore could put focus inside this scroller
        // while it is open. Nothing else may join it: no row control, no button.
        var tab = StackupTab();
        int canvas = Require(tab, "<ctrl:StackupCanvas");
        int close  = tab.IndexOf("</ScrollViewer>", canvas, StringComparison.Ordinal);
        Assert.True(close > canvas);

        var inside = tab[canvas..close];
        Assert.DoesNotContain("<TextBox", inside);
        Assert.DoesNotContain("<Button", inside);
        Assert.Contains("<ctrl:SchematicInlineEditBox x:Name=\"StackupInlineEdit\"", inside);

        // …and because it IS focusable, the scroll handler excuses it by name rather than letting it
        // resolve to the drawing's own scroller. Without this line, Page Up typed into an open editor
        // scrolls the label the box is sitting on out of the pane.
        Assert.Contains("if (ReferenceEquals(e.Source, StackupInlineEdit)) return;", code);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>The Stackup TabItem's own text, so an assertion cannot accidentally be satisfied by
    /// the Layers or DRC tab, which carry very similar controls.</summary>
    private static string Axaml() =>
        RepoFile(Path.Combine("src", "Ui", "Views", "Layout", "TechEditorView.axaml"));

    private static string StackupTab()
    {
        var axaml = Axaml();
        int start = axaml.IndexOf("<TabItem Header=\"{Binding ViewModel.StackupTabHeader}\">",
                                  StringComparison.Ordinal);
        Assert.True(start >= 0, "the Stackup TabItem must still be present");

        int end = axaml.IndexOf("<TabItem Header=\"{Binding ViewModel.DrcTabHeader}\">", start,
                                StringComparison.Ordinal);
        Assert.True(end > start, "the DRC TabItem must still follow it");
        return axaml[start..end];
    }

    private static int Require(string haystack, string needle)
    {
        int i = haystack.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(i >= 0, $"expected to find: {needle}");
        return i;
    }

    private static string RepoFile(string rel)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "circuitrf.slnx")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!, rel));
    }
}
