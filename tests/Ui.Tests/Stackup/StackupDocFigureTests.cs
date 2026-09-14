using System;
using System.Collections.Generic;
using System.Linq;
using CircuitRF.Design.Layout;
using CircuitRF.Render;
using CircuitRF.Ui.Diagnostics;
using CircuitRF.Ui.Diagnostics.Fixtures;
using CircuitRF.Ui.Layout;
using Xunit;

namespace CircuitRF.Ui.Tests.StackupRender;

/// <summary>
/// brief-stackup-render-8-docs-and-figures.md <c>R-stk8-5</c>/<c>R-stk8-7</c> — the documentation
/// figures now draw through <see cref="StackupScene"/> and <see cref="StackupRenderer"/>, so the
/// three numbers that used to be typed beside a hand-built <c>Canvas</c> are derived and can be
/// asserted against their source.
///
/// <para><b>These are not a substitute for looking at the pictures.</b> The repoint was judged on
/// three rendered comparisons at reading size, recorded in <c>src/Ui/Diagnostics/RESOLVED.md</c>.
/// What these hold is the part a reader of the page cannot check: that the frame the catalog states
/// is the frame the drawing needs, and that nothing in it wraps or is cut off.</para>
/// </summary>
public class StackupDocFigureTests
{
    // ── The width (R-stk8-7) ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Nothing in the label column wraps at <c>DocStackupFixtures.Width</c>.
    ///
    /// <para>A figure is not a dockable pane: it is as wide as it is asked to be, and a spec broken
    /// across two lines in a printed picture is a picture that looks like a mistake. The width is
    /// chosen from <see cref="StackupScene.WidthThatFitsLabels"/>, so this asserts the choice is
    /// still right — a longer layer name in a shipped technology fails here rather than shipping a
    /// wrapped figure nobody looked at.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(FigureTechnologies))]
    public void NoLabelGroupWraps_AtTheFiguresWidth(string _, Technology tech, bool boundaries)
    {
        var scene = StackupScene.Build(
            tech, DocStackupFixtures.Width,
            new StackupSceneOptions { ShowBoundaryConditions = boundaries });

        Assert.False(scene.LabelsWrap,
            $"the label column is {scene.LabelColumnWidth:0.#} px and the widest group needs "
          + $"{scene.WidestLabelGroup:0.#}; DocStackupFixtures.Width must be at least "
          + $"{StackupScene.WidthThatFitsLabels(tech, DocStackupFixtures.Width, 2000f):0.#}.");
    }

    /// <summary>Nothing is drawn past the right gutter either — the separate failure from wrapping,
    /// and the one the hand-built figures had no way to notice because they measured nothing.</summary>
    [Theory]
    [MemberData(nameof(FigureTechnologies))]
    public void NothingIsDrawnPastTheRightGutter(string _, Technology tech, bool boundaries)
    {
        var scene = StackupScene.Build(
            tech, DocStackupFixtures.Width,
            new StackupSceneOptions { ShowBoundaryConditions = boundaries });

        float limit = DocStackupFixtures.Width - StackupScene.Gutter;
        foreach (var label in scene.Labels)
            Assert.True(label.Rect.Right <= limit + 0.01f,
                $"'{label.Text}' ends at {label.Rect.Right:0.#}, past the {limit:0.#} gutter.");
    }

    // ── The height (R-stk8-5's rule, applied to the three cross-sections) ─────────────────────────

    /// <summary>
    /// Each catalog row is at least as tall as the scene it frames.
    ///
    /// <para>The fixture's control reports <see cref="StackupScene.Height"/> as its desired height,
    /// so a row shorter than that does not scroll — it CLIPS, silently, at the bottom band. The
    /// generosity is bounded too: a row far taller than its drawing is a page of white space, and it
    /// is how a figure comes to be sized by whatever it happened to be when someone looked.</para>
    /// </summary>
    [Theory]
    [InlineData("stackup-mmic", 0)]
    [InlineData("mom-bend-stackup", 0)]
    // The MIM figure carries a footer line UNDER the drawing — prose about what the window leaves
    // out, outside the picture — so its row is the scene plus that, not the scene.
    [InlineData("stackup-mim", 32)]
    public void EachCrossSectionRow_FramesItsOwnSceneHeight(string id, float extraForFooter)
    {
        var row   = FigureCatalog.Catalog.Single(r => r.Id == id);
        var scene = SceneFor(id);

        Assert.Equal(DocStackupFixtures.Width, row.Width);
        Assert.True(row.Height >= scene.Height + extraForFooter,
            $"{id} is framed at {row.Height} px and its drawing needs "
          + $"{scene.Height:0.#}{(extraForFooter > 0 ? $" + {extraForFooter} for the footer" : "")}.");
        Assert.True(row.Height <= scene.Height + extraForFooter + 24,
            $"{id} is framed at {row.Height} px for a drawing of {scene.Height:0.#} — re-derive it "
          + "rather than leaving the slack.");
    }

    // ── The window (R-stk8-7) ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A WINDOW on a stack states no boundary conditions, and reserves no space for them.
    ///
    /// <para>The MIM figure shows five of the MMIC process's seven bands. "Top: Open — free space
    /// above" is true of the stack and false of a slice of it, and a figure that printed it would be
    /// claiming to show the whole sandwich.</para>
    /// </summary>
    [Fact]
    public void TheWindowedFigure_StatesNeitherBoundaryCondition()
    {
        var scene = SceneFor("stackup-mim");

        Assert.DoesNotContain(scene.Labels, l => l.Style == StackupLabelStyle.Note);
        Assert.DoesNotContain(scene.Labels, l => l.Text.StartsWith("Top:", StringComparison.Ordinal)
                                              || l.Text.StartsWith("Bottom:", StringComparison.Ordinal));

        // And the space they would have taken is not reserved: the topmost band starts at TopPad.
        Assert.Equal(StackupScene.TopPad, scene.Bands[0].Rect.Top, 3);
    }

    /// <summary>
    /// A via with one end outside the window is DROPPED, not drawn as a refusal.
    ///
    /// <para>R-stk1-6 draws an unresolvable span in red, and is right to: a stackup whose via names a
    /// conductor that is not there is broken and must not look fine. The MMIC's backside via is not
    /// broken — <c>Backside Metal</c> is in the technology, just not in this picture — so a figure
    /// that printed "span does not resolve" over it would be reporting its own cropping as a fault
    /// in the shipped process. Caught by looking at the first repointed figure, not by reasoning.</para>
    /// </summary>
    [Fact]
    public void TheWindowedFigure_DropsAViaThatLeavesTheWindow_RatherThanRefusingIt()
    {
        var scene = SceneFor("stackup-mim");

        Assert.DoesNotContain(scene.Labels, l => l.Style == StackupLabelStyle.Refusal);
        Assert.DoesNotContain(scene.Barrels, b => b.Name == "Backside Via");

        // The two that are wholly inside it are still there, so this is a filter and not a silence.
        Assert.Contains(scene.Barrels, b => b.Name == "MIM Via");
        Assert.Contains(scene.Barrels, b => b.Name == "Metal1-Metal2 Post");
    }

    /// <summary>
    /// The tie and the ground reference are in the CAPTIONS, because the drawing no longer spells
    /// them out (R-stk1-4, and R-stk8-3's rule that a qualification belongs beside a picture).
    ///
    /// <para>The hand-built figures wrote "◄ ground reference: every port's − terminal" and "◄
    /// patterned with '…' — only in runs that analyse it" into the picture itself. The renderer
    /// writes "gnd" and "patterned: …", which is right for a pane the user is editing in and thin
    /// for a page they are reading. The sentences did not disappear; they moved one layer out.</para>
    /// </summary>
    [Fact]
    public void TheCaptions_CarryWhatTheDrawingAbbreviates()
    {
        string mmic = FigureCatalog.Catalog.Single(r => r.Id == "stackup-mmic").Caption;
        Assert.Contains("negative terminal of every port", mmic, StringComparison.Ordinal);
        Assert.Contains("patterned with", mmic, StringComparison.Ordinal);

        // R-stk8-3: the height rule is never on the drawing, so every cross-section caption states
        // it. A reader who has just measured a 500:1 dielectric range and found nothing about it
        // anywhere concludes the picture is wrong.
        foreach (var id in new[] { "stackup-mmic", "stackup-mim", "mom-bend-stackup" })
        {
            string caption = FigureCatalog.Catalog.Single(r => r.Id == id).Caption;
            // Case-insensitive: one caption emphasises the WITHIN, which is the half readers skip.
            Assert.Contains("within a kind", caption, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("not to scale", caption, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>The three technologies the cross-section figures draw, and whether each shows the
    /// stack's boundary conditions — the MIM figure is a window and does not.</summary>
    public static TheoryData<string, Technology, bool> FigureTechnologies() => new()
    {
        { "stackup-mmic",     StarterTechnologies.MmicGaAs(),  true },
        { "mom-bend-stackup", StarterTechnologies.Pcb2Layer(), true },
        { "stackup-mim",      MimWindow(),                     false },
    };

    private static StackupScene SceneFor(string id) => id switch
    {
        "stackup-mmic"     => StackupScene.Build(StarterTechnologies.MmicGaAs(), DocStackupFixtures.Width),
        "mom-bend-stackup" => StackupScene.Build(StarterTechnologies.Pcb2Layer(), DocStackupFixtures.Width),
        "stackup-mim"      => StackupScene.Build(MimWindow(), DocStackupFixtures.Width,
                                                 new StackupSceneOptions { ShowBoundaryConditions = false }),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "not a cross-section figure"),
    };

    /// <summary>
    /// The MIM module window, spelled the way <c>DocStackupFixtures</c> spells it.
    ///
    /// <para>A second copy, and deliberately: the point of the two windowed tests above is that the
    /// FIXTURE's filter drops a via whose span leaves the window, and a test calling the fixture's
    /// own private filter would pass whatever that filter did.</para>
    /// </summary>
    private static Technology MimWindow()
    {
        var tech = StarterTechnologies.MmicGaAs();
        var kept = new HashSet<string>(
            ["Metal2", "Air", "MIM Metal", "MIM Dielectric", "Metal1"], StringComparer.Ordinal);

        return new Technology
        {
            Name               = tech.Name,
            DefaultDisplayUnit = tech.DefaultDisplayUnit,
            Layers             = tech.Layers,
            Stackup = new Stackup
            {
                Top    = tech.Stackup.Top,
                Bottom = tech.Stackup.Bottom,
                Layers = tech.Stackup.Layers.Where(l => l.Kind == StackupKind.Via
                    ? kept.Contains(l.SpanFromLayer ?? "") && kept.Contains(l.SpanToLayer ?? "")
                    : kept.Contains(l.Name)).ToList(),
            },
        };
    }
}
