using System;
using System.IO;
using System.Linq;
using CircuitRF.Ui.Layout;
using CircuitRF.Ui.Theming;
using SkiaSharp;
using Xunit;

namespace CircuitRF.Ui.Tests.StackupRender;

/// <summary>
/// R-stk7-1's half of brief 7's gate: the copy's colours come from <see cref="ClipboardRenderPolicy"/>
/// and from nowhere else.
///
/// <para>Separate from <see cref="StackupCopyTests"/> because these tests move a PROCESS-GLOBAL
/// preference store, so they join <c>AppDataRootCollection</c> — the one collection every test that
/// redirects <c>AppDataRoot</c> shares. Two collections that both redirect a process-global directory
/// still run concurrently and still clobber each other; that has already cost this repo one
/// full-solution-only failure.</para>
/// </summary>
[Collection(AppDataRootCollection.Name)]
public sealed class StackupCopyPolicyTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "crf-stackup-copy-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly ColorVariant _variantOnEntry = ThemeService.CurrentVariant;

    public StackupCopyPolicyTests()
    {
        Directory.CreateDirectory(_root);
        AppDataRoot.RedirectTo(_root);
    }

    public void Dispose()
    {
        ThemeService.CurrentVariant = _variantOnEntry;
        AppDataRoot.RedirectTo(null);
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static Technology Fixture() => ShippedTechnologies.Load(ShippedTechnologies.All.First().Id);

    /// <summary>The picture the copy would put on the clipboard, composed through the POLICY — which
    /// is the whole subject of this file: no test here passes a variant or a background of its own.</summary>
    private static string Compose(Technology tech)
    {
        var (theme, transparent) = StackupGraphicExport.ResolvePolicy();
        return StackupCopyTests.CanonicalSvg(StackupGraphicExport.BuildSvgString(tech, theme, transparent));
    }

    private static void SetMode(CopyColorMode mode)
        => AppPreferencesIo.Update(p => p.CopyColorMode = mode);

    private static void SetTransparent(bool transparent)
        => AppPreferencesIo.Update(p => p.CopyTransparentBackground = transparent);

    // ── The variant is the policy's, never the caller's ───────────────────────────────────────────

    /// <summary>
    /// A user who has set "always copy in light mode" has set it for this drawing too — so the two
    /// forced modes must produce genuinely different pictures.
    /// </summary>
    [Fact]
    public void ForceLightAndForceDarkProduceDifferentPictures()
    {
        var tech = Fixture();

        SetMode(CopyColorMode.ForceLight);
        string light = Compose(tech);

        SetMode(CopyColorMode.ForceDark);
        string dark = Compose(tech);

        Assert.NotEqual(light, dark);
    }

    /// <summary>
    /// <c>FollowSystem</c> is the ACTIVE variant, whichever that is — asserted in both directions, so
    /// a resolver that had hardcoded either one cannot pass.
    /// </summary>
    [Theory]
    [InlineData(ColorVariant.Light, CopyColorMode.ForceLight)]
    [InlineData(ColorVariant.Dark,  CopyColorMode.ForceDark)]
    public void FollowSystemMatchesTheActiveVariant(ColorVariant active, CopyColorMode equivalent)
    {
        var tech = Fixture();
        ThemeService.CurrentVariant = active;

        SetMode(equivalent);
        string forced = Compose(tech);

        SetMode(CopyColorMode.FollowSystem);
        string followed = Compose(tech);

        Assert.Equal(forced, followed, StringComparer.Ordinal);
    }

    // ── Determinism, where it is user-visible ─────────────────────────────────────────────────────

    /// <summary>
    /// Brief 1's determinism gate (R-stk1-11) at the place a user can actually see it break: copying
    /// the same stackup twice must put the same bytes on the clipboard. A layout pass that depended
    /// on a dictionary's hash order would produce a picture that moved between copies.
    /// </summary>
    [Fact]
    public void CopyingTwiceWithTheSamePreferencesIsByteIdentical()
    {
        var tech = Fixture();
        SetMode(CopyColorMode.ForceLight);

        Assert.Equal(Compose(tech), Compose(tech), StringComparer.Ordinal);

        // The PDF is compared RAW — it carries no generated ids, so there is nothing to canonicalise
        // and nothing excused.
        foreach (bool transparent in new[] { false, true })
            Assert.Equal(StackupGraphicExport.BuildPdfBytes(tech, StackupRenderTheme.Light, transparent),
                         StackupGraphicExport.BuildPdfBytes(tech, StackupRenderTheme.Light, transparent));
    }

    // ── The background half of the policy, which costs this drawing more than the others ─────────

    /// <summary>
    /// <b>A transparent copy really is transparent — including through a via's bore.</b>
    ///
    /// <para>This drawing is the one in the application that uses its background colour as PAINT: a
    /// plated via's bore is filled with it so the hole reads as a drill that removed material. With
    /// the ground gone, the renderer cuts the bands away from the bore instead, so what shows through
    /// the hole is whatever the picture was pasted onto. <b>The failure this pins is the near miss</b>
    /// — a bore simply left unpainted, which shows the dielectric the via passes through and reads as
    /// two metal walls with a gap between them rather than as one barrel with a hole in it. So it is
    /// not enough to assert the bore differs from the ground: it is asserted transparent, and
    /// asserted different from the band beside it at the same height.</para>
    /// </summary>
    [Fact]
    public void ATransparentCopyIsTransparentThroughAPlatedViasBore()
    {
        SetMode(CopyColorMode.ForceLight);
        SetTransparent(true);

        var (theme, transparent) = StackupGraphicExport.ResolvePolicy();
        Assert.True(transparent);

        var tech  = ShippedTechnologies.Load("pcb-2layer_FR-4_70mil_1oz");
        var scene = StackupGraphicExport.PageScene(tech);
        var probe = new BarrelProbe(scene, StackupViaLook.PlatedBarrel);

        using var raster = StackupCopyTests.Raster(tech, theme, transparent);

        for (float t = 0.2f; t <= 0.8f; t += 0.1f)
        {
            Assert.Equal((byte)0, raster.GetPixel(probe.BoreX, probe.Y(t)).Alpha);

            // Non-vacuity, twice over: the wall beside the bore IS painted, and so is the band the
            // via passes through — so this is a hole in a drawing rather than a blank page.
            Assert.True(raster.GetPixel(probe.WallX, probe.Y(t)).Alpha > 0, "the barrel wall vanished");
            Assert.True(raster.GetPixel(probe.BandX, probe.Y(t)).Alpha > 0, "the band vanished");
        }

        // And the drawing's own gutter — INSIDE the scene rect, where the opaque path paints its
        // ground — carries none either. (The page CORNER is outside the scene rect and is unpainted
        // on both paths, so sampling it would prove nothing.)
        Assert.Equal((byte)0, raster.GetPixel(probe.GutterX, probe.GutterY).Alpha);
    }

    /// <summary>
    /// An UNPLATED hole has no walls, so all of it is the hole — the same rule, on the via kind that
    /// is nothing but a bore.
    /// </summary>
    [Fact]
    public void ATransparentCopyIsTransparentThroughAnUnplatedHole()
    {
        SetMode(CopyColorMode.ForceLight);
        SetTransparent(true);

        var (theme, transparent) = StackupGraphicExport.ResolvePolicy();
        var tech = Unplated();
        var scene = StackupGraphicExport.PageScene(tech);
        var probe = new BarrelProbe(scene, StackupViaLook.UnplatedHole);

        using var raster = StackupCopyTests.Raster(tech, theme, transparent);

        Assert.Equal((byte)0, raster.GetPixel(probe.BoreX, probe.Y(0.5f)).Alpha);
        Assert.True(raster.GetPixel(probe.BandX, probe.Y(0.5f)).Alpha > 0, "the band vanished");
    }

    /// <summary>
    /// <b>And with the preference off, the drawing carries its own ground</b> — the bore is a disc of
    /// the pane colour, exactly as it is on screen. Both halves of the flag are asserted, because a
    /// renderer that had simply stopped painting the ground would pass the test above.
    /// </summary>
    [Fact]
    public void AnOpaqueCopyPaintsTheGround_AndTheBoreIsADiscOfIt()
    {
        SetMode(CopyColorMode.ForceLight);
        SetTransparent(false);

        var (theme, transparent) = StackupGraphicExport.ResolvePolicy();
        Assert.False(transparent);

        var tech  = ShippedTechnologies.Load("pcb-2layer_FR-4_70mil_1oz");
        var scene = StackupGraphicExport.PageScene(tech);
        var probe = new BarrelProbe(scene, StackupViaLook.PlatedBarrel);

        using var raster = StackupCopyTests.Raster(tech, theme, transparent);

        Assert.Equal(theme.Background, raster.GetPixel(probe.BoreX, probe.Y(0.5f)));
        Assert.Equal(theme.Background, raster.GetPixel(probe.GutterX, probe.GutterY));
    }

    /// <summary>The flag reaches the vector formats too, which are the two richest and the two a test
    /// cannot sample a pixel of.</summary>
    [Fact]
    public void TheBackgroundFlagChangesEveryFormat()
    {
        var tech  = ShippedTechnologies.Load("pcb-2layer_FR-4_70mil_1oz");
        var theme = StackupRenderTheme.Light;

        Assert.NotEqual(StackupGraphicExport.BuildSvgString(tech, theme, transparentBackground: false),
                        StackupGraphicExport.BuildSvgString(tech, theme, transparentBackground: true));

        Assert.NotEqual(StackupGraphicExport.BuildPdfBytes(tech, theme, transparentBackground: false),
                        StackupGraphicExport.BuildPdfBytes(tech, theme, transparentBackground: true));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Three page-space x columns through one barrel — its bore, one of its walls, and the
    /// band well clear of it — plus the y of a given fraction down it.</summary>
    private sealed class BarrelProbe
    {
        private readonly StackupBarrel _barrel;
        private readonly float _scale, _dx, _dy;

        public BarrelProbe(StackupScene scene, StackupViaLook look)
        {
            _barrel = scene.Barrels.First(b => b.Look == look);
            _scale  = StackupGraphicExport.FitScale(scene, 792f, 612f);
            _dx     = (792f - scene.Width  * _scale) * 0.5f;
            _dy     = (612f - scene.Height * _scale) * 0.5f;

            BoreX = (int)(_dx + _barrel.Rect.MidX * _scale);
            WallX = (int)(_dx + (_barrel.Rect.Left + _barrel.WallPx * 0.5f) * _scale);
            BandX = (int)(_dx + (scene.BandColumn.Left + 20f) * _scale);

            GutterX = (int)(_dx + 2f);
            GutterY = (int)(_dy + scene.Height * 0.5f * _scale);
        }

        public int BoreX { get; }
        public int WallX { get; }
        public int BandX { get; }

        /// <summary>A point inside the scene's own rect but outside every band — the drawing's left
        /// gutter, which is where its GROUND shows and nothing else does.</summary>
        public int GutterX { get; }
        public int GutterY { get; }

        public int Y(float fraction)
            => (int)(_dy + (_barrel.Rect.Top + _barrel.Rect.Height * fraction) * _scale);
    }

    /// <summary>Two conductors, a dielectric, and a hole that is not plated — the one via kind that is
    /// all bore. No shipped technology carries one.</summary>
    private static Technology Unplated()
    {
        var tech = new Technology { Name = "Unplated", DefaultDisplayUnit = LayoutUnit.Um };
        tech.Stackup.Layers.Add(new StackupLayer
        {
            Kind = StackupKind.Conductor, Name = "Top", ThicknessDbu = 35_000, SigmaSm = 5.8e7,
        });
        tech.Stackup.Layers.Add(new StackupLayer
        {
            Kind = StackupKind.Dielectric, Name = "Core", ThicknessDbu = 1_600_000, Epsr = 4.4, TanD = 0.02,
        });
        tech.Stackup.Layers.Add(new StackupLayer
        {
            Kind = StackupKind.Conductor, Name = "Bottom", ThicknessDbu = 35_000, SigmaSm = 5.8e7,
        });
        tech.Stackup.Layers.Add(new StackupLayer
        {
            Kind = StackupKind.Via, Name = "Mounting Hole", Plated = false,
            SpanFromLayer = "Top", SpanToLayer = "Bottom",
        });
        return tech;
    }
}
