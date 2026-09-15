using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using CircuitRF.Design.Layout;
using Xunit;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests.Layout;

/// <summary>
/// <b>Update Layout from Schematic says where it put things.</b>
///
/// <para>An instance is the one thing a user cannot find by looking: it lands where the command puts
/// it, not where they clicked. So the command brings what it wrote on screen — the region the new
/// instances occupy, or, when it wrote no instances at all and the document was empty, a plain fit.
/// The second case is the wBond one, where the only content is an overlay of wires.</para>
///
/// <para><b>This is the SECOND half of an owner report (2026-09-15), and the first diagnosis of that
/// report was wrong.</b> "Only one of three parts is visible" was read here as a framing problem and
/// it was not: the parts had been scattered 10 mm apart by a fixed placement pitch, which is fixed at
/// its own source and pinned by
/// <c>PdkPCellExampleTests.UpdateLayoutFromSchematic_PlacesEveryPartWhereTheOthersCanBeSeen</c>.
/// Framing is kept because it is separately true and cheap to hold: the numbers below are what an
/// empty MMIC layout is actually framed on, and they are small.</para>
///
/// <para><see cref="LayoutViewport.Default"/> frames an EMPTY layout on 200 of its own snap steps,
/// which is right — an empty document has no content to frame and a canvas has to start somewhere
/// physically drawable. On the MMIC starter technology's 5 DBU snap that is one micrometre across the
/// whole canvas, so anything written into it afterwards is off screen unless somebody says so.</para>
///
/// <para>A coil is the case where that shows as nothing at all rather than as a wash of metal: the
/// cell re-centres on its winding, so the ORIGIN of a coil is the middle of its own opening. See
/// <c>PdkPCellExampleTests.TheCoilsOriginIsInsideItsOwnOpening</c>.</para>
/// </summary>
public sealed class EmptyLayoutIsFramedOnWhatIsWrittenTests(ITestOutputHelper output)
{
    private const int Dbu = 1000;          // the example technology's database resolution
    private const double W = 1200, H = 800;

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "circuitRF.slnx")))
            dir = Path.GetDirectoryName(dir) ?? "";
        return dir.Length > 0 ? dir : AppContext.BaseDirectory;
    }

    /// <summary>The snap the shipped MMIC technology actually declares — read from it rather than
    /// written here, so this measurement tracks the technology instead of a copy of one number.</summary>
    private static long ShippedMmicSnapDbu()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepoRoot(), "examples", "PDK PCells", "tech", "mmic-GaAs_2LM_100um.ctech")));
        return doc.RootElement.GetProperty("DefaultSnapDbu").GetInt64();
    }

    /// <summary>
    /// How small the default window is, in the units the parts are drawn in. Not a complaint about
    /// the default — it is the right answer for an empty document — but the measurement that says a
    /// document is not still empty by the time the user looks at it.
    /// </summary>
    [Fact]
    public void AnEmptyMmicLayoutIsFramedOnAboutOneMicrometre()
    {
        long snap = ShippedMmicSnapDbu();
        var vp = LayoutViewport.Default(W, H, snap, Dbu);

        double acrossUm = (W / vp.Zoom) / Dbu;
        output.WriteLine($"snap {snap} DBU → default window {acrossUm:N2} µm across");

        Assert.Equal(0, vp.VisibleMinX + (W / vp.Zoom) / 2, 6);   // centred on the origin
        Assert.InRange(acrossUm, 0.5, 2.0);
    }

    /// <summary>
    /// And what a kit cell of ordinary size looks like in it: a 250 µm coil centred on the origin has
    /// every edge far outside the window, and the window itself lands in the coil's own opening — so
    /// there is not even a wash of metal to say something is there.
    /// </summary>
    [Fact]
    public void AQuarterMillimetreCellIsEntirelyOutsideThatWindow()
    {
        var vp = LayoutViewport.Default(W, H, ShippedMmicSnapDbu(), Dbu);

        // The shipped octagonal coil at its declared defaults, in DBU.
        var coil = new Bbox(-135_000, -128_642, 115_000, 126_571);
        const long OpeningHalfWidth = 64_000;    // half the clear opening at its centre

        Assert.True(coil.MinX < vp.VisibleMinX && coil.MaxX > vp.VisibleMaxX,
            "the cell is not even as wide as the window — this measurement has gone stale.");
        Assert.True(OpeningHalfWidth > vp.VisibleMaxX && -OpeningHalfWidth < vp.VisibleMinX,
            "the window is no longer inside the coil's opening, so the symptom would be a wash of " +
            "metal rather than a blank canvas.");

        // Framing what was written puts the whole of it on screen, which is the fix.
        var fitted = LayoutViewport.ZoomToFit(coil, W, H);
        Assert.True(fitted.VisibleMinX <= coil.MinX && fitted.VisibleMaxX >= coil.MaxX);
        Assert.True(fitted.VisibleMinY <= coil.MinY && fitted.VisibleMaxY >= coil.MaxY);
        output.WriteLine($"fitted window {(W / fitted.Zoom) / Dbu:N1} µm across");
    }

    /// <summary>
    /// <b>The ordering is the whole fix, and only code can hold it.</b> The command opens the layout
    /// document BEFORE it writes into it, so emptiness has to be recorded before the generator runs
    /// and the framing requested after everything — the instances AND the wBond sidecar, which is an
    /// overlay that Zoom to Fit counts.
    ///
    /// <para>Re-ordering the open to come last was considered and is deliberately not the fix: it
    /// would only put the canvas's own one-shot initial fit on the right side of the write WHEN that
    /// layout pass happens to fall after it, which is the timing this defect already turns on.</para>
    /// </summary>
    [Fact]
    public void TheCommandFramesWhatItWrote_AfterEverythingItWrites()
    {
        string src = StripComments(File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Ui", "ViewModels", "WorkspaceViewModel.SchematicToLayout.cs")));

        int captured  = src.IndexOf("bool layoutWasEmpty", StringComparison.Ordinal);
        int generated = src.IndexOf("SchematicToLayoutGenerator.Run", StringComparison.Ordinal);
        int seeded    = src.IndexOf("SeedWBondSidecar(", StringComparison.Ordinal);
        int region    = src.IndexOf("RequestZoomToRegion(", StringComparison.Ordinal);
        int fitted    = src.IndexOf("RequestZoomToFit()", StringComparison.Ordinal);

        Assert.True(region > 0, "the command no longer brings the instances it added on screen.");
        Assert.True(fitted > 0, "the command no longer frames a layout that had nothing in it.");
        Assert.True(captured > 0 && captured < generated,
            "emptiness is recorded after the generator has already run — by then it is never empty.");

        // Both framings run after EVERYTHING is written — the instances and the wBond sidecar, which
        // is an overlay that Zoom to Fit counts and Zoom to Region does not.
        Assert.True(region > seeded && fitted > seeded,
            "the framing runs before the wBond sidecar is seeded, so a cell whose only content is " +
            "wires frames an empty extent.");

        // The region wins where there is one, and the plain fit is the fallback — not a second,
        // unconditional re-frame of a layout the user had already set up.
        Assert.Matches(@"if \(!addedRegion\.IsEmpty\)[\s\S]{0,200}?RequestZoomToRegion\(", src);
        Assert.Matches(@"else if \(layoutWasEmpty[\s\S]{0,400}?RequestZoomToFit\(\)", src);
    }

    /// <summary>Comments describe the rule; only code can break it.</summary>
    private static string StripComments(string src) =>
        Regex.Replace(Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline),
                      @"//.*?$", "", RegexOptions.Multiline);
}
