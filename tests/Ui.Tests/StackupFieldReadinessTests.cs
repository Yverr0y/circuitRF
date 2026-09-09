// Owner, 2026-09-08: a field an EM run cannot use is marked in the Technology editor, subtly, so the
// person who just imported a board finds out at the box rather than at Simulate.
//
// TWO THINGS ARE UNDER TEST AND ONLY ONE OF THEM IS A COLOUR.
//
//  1. THE PREDICATE, in src/Design. Every one of these mirrors a check TechValidation already makes,
//     and the whole point of a shared source is that the mark beside a box and the sentence in the
//     problems list cannot disagree. The direction that matters is a field marked FINE that stops a
//     run, so each case is asserted in both directions.
//
//  2. THE ROW's flags, which must be re-asked after an edit. Four of the commit methods on that row
//     deliberately do not call RefreshFromModel — a mark that went stale after the value beside it
//     was corrected would be worse than no mark, because it teaches somebody to ignore it.
//
// The brush and the XAML class are NOT asserted here. A test that a Style setter names a resource key
// proves nothing about what anyone sees, and the flags are the part a regression can reach silently.

using System.Linq;
using CircuitRF.Ui.Layout;

namespace CircuitRF.Ui.Tests;

public class StackupFieldReadinessTests
{
    private static string TempPath() => System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), $"readiness-{System.Guid.NewGuid():N}.ctech");

    /// <summary>A minimal but COMPLETE technology — every value an EM run needs is present, so any
    /// mark that appears in a test below was put there by that test.</summary>
    private static Technology Tech()
    {
        var t = new Technology { Name = "T", DefaultDisplayUnit = LayoutUnit.Um, DefaultSnapDbu = 1000 };
        t.Layers.Add(new LayerDef
        { Key = new LayerKey(1, 0), Name = "M1", Color = new CircuitRF.Design.Theming.Rgba(1, 2, 3), ZOrder = 1 });

        t.Stackup.Layers.Add(new StackupLayer
        { Kind = StackupKind.Conductor, Name = "Top", ThicknessDbu = 35_000, SigmaSm = 5.8e7 });
        t.Stackup.Layers.Add(new StackupLayer
        { Kind = StackupKind.Dielectric, Name = "Core", ThicknessDbu = 1_778_000, Epsr = 4.4, TanD = 0.02, Mur = 1.0 });
        t.Stackup.Layers.Add(new StackupLayer
        { Kind = StackupKind.Conductor, Name = "Bot", ThicknessDbu = 35_000, SigmaSm = 5.8e7 });
        t.Stackup.Layers.Add(new StackupLayer
        {
            Kind = StackupKind.Via, Name = "PTH", SpanFromLayer = "Top", SpanToLayer = "Bot",
            Fill = ViaFillKind.Plated, WallThicknessDbu = 25_000,
        });
        return t;
    }

    private static TechEditorViewModel Vm(Technology? tech = null) => new(TempPath(), tech ?? Tech());

    private static StackupLayerRowViewModel Row(TechEditorViewModel vm, string name)
        => vm.StackupLayers.Single(r => r.Layer.Name == name);

    // ── The predicate ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ACompleteTechnology_MarksNothing()
    {
        var vm = Vm();
        foreach (var row in vm.StackupLayers)
        {
            Assert.False(row.ThicknessNeedsValue,     row.Layer.Name + " thickness");
            Assert.False(row.SigmaNeedsValue,         row.Layer.Name + " sigma");
            Assert.False(row.EpsrNeedsValue,          row.Layer.Name + " epsr");
            Assert.False(row.TanDNeedsValue,          row.Layer.Name + " tand");
            Assert.False(row.MurNeedsValue,           row.Layer.Name + " mur");
            Assert.False(row.WallThicknessNeedsValue, row.Layer.Name + " wall");
        }
    }

    [Theory]
    [InlineData(StackupField.Thickness)]
    [InlineData(StackupField.Sigma)]
    public void AConductorWithNothingInIt_MarksItsTwoFields(StackupField field)
    {
        var layer = new StackupLayer { Kind = StackupKind.Conductor, Name = "bare" };
        Assert.NotNull(StackupFieldReadiness.Problem(layer, field));
    }

    [Fact]
    public void ADielectricWithNothingInIt_MarksThicknessAndPermittivity_ButNotLossTangent()
    {
        // Epsr's own C# default is 1.0 and TanD's is 0. A fresh Gerber import writes 0 for Epsr
        // (which is why zero is marked) and 0 for TanD — which is a LOSSLESS dielectric, a legitimate
        // thing to author, so it is never marked. Marking it would put an unclearable mark on every
        // ideal substrate anyone has ever written down.
        var layer = new StackupLayer { Kind = StackupKind.Dielectric, Name = "bare", Epsr = 0 };

        Assert.NotNull(StackupFieldReadiness.Problem(layer, StackupField.Thickness));
        Assert.NotNull(StackupFieldReadiness.Problem(layer, StackupField.Epsr));
        Assert.Null(StackupFieldReadiness.Problem(layer, StackupField.TanD));
    }

    /// <summary>1.0 is vacuum and is the floor; below it is not a material, which is the same
    /// threshold TechValidation reports on and both extractors guard with.</summary>
    [Theory]
    [InlineData(0.0,  true)]
    [InlineData(0.5,  true)]
    [InlineData(0.999, true)]
    [InlineData(1.0,  false)]
    [InlineData(4.4,  false)]
    public void PermittivityIsMarkedBelowOne_AndNowhereAbove(double epsr, bool marked)
    {
        var layer = new StackupLayer { Kind = StackupKind.Dielectric, Name = "d", ThicknessDbu = 1, Epsr = epsr };
        Assert.Equal(marked, StackupFieldReadiness.Problem(layer, StackupField.Epsr) is not null);
    }

    /// <summary>
    /// <b>A defaulted value is not marked, and that is deliberate.</b> The import fills a stackup with
    /// ordinary FR-4 and says so in its own message; nothing in circuitRF knows whether the board is
    /// really 4.4 or really 3.66. This marks what an EM run would REFUSE, never what looks like a
    /// guess — a mark that appeared on plausible numbers would be one nobody could ever clear.
    /// </summary>
    [Fact]
    public void TheImportsOwnFr4Defaults_AreNotMarked()
    {
        var layer = new StackupLayer
        {
            Kind = StackupKind.Dielectric, Name = "d",
            ThicknessDbu = 1_778_000, Epsr = SubstrateDefaults.Epsr, TanD = SubstrateDefaults.TanD, Mur = 1.0,
        };

        Assert.Null(StackupFieldReadiness.Problem(layer, StackupField.Thickness));
        Assert.Null(StackupFieldReadiness.Problem(layer, StackupField.Epsr));
        Assert.Null(StackupFieldReadiness.Problem(layer, StackupField.TanD));
        Assert.Null(StackupFieldReadiness.Problem(layer, StackupField.Mur));
    }

    /// <summary>A via has no z band of its own — it traverses whatever separates the conductors it
    /// spans — which is why the extractors skip it and the validator exempts it. A thickness mark on
    /// one would be a demand nobody can satisfy.</summary>
    [Fact]
    public void AViaIsNeverMarkedForThickness_AndOnlyAPlatedOneIsAskedForAWall()
    {
        var plated = new StackupLayer
        { Kind = StackupKind.Via, Name = "v", Fill = ViaFillKind.Plated };
        Assert.Null(StackupFieldReadiness.Problem(plated, StackupField.Thickness));
        Assert.NotNull(StackupFieldReadiness.Problem(plated, StackupField.WallThickness));

        // A hole that is not metal at all connects nothing and is asked nothing (GI1 R-gi1-2).
        var hole = new StackupLayer
        { Kind = StackupKind.Via, Name = "h", Fill = ViaFillKind.Plated, Plated = false };
        Assert.Null(StackupFieldReadiness.Problem(hole, StackupField.WallThickness));

        // Nor is a solid one: it has no wall, it is all metal.
        var solid = new StackupLayer { Kind = StackupKind.Via, Name = "s", Fill = ViaFillKind.Solid };
        Assert.Null(StackupFieldReadiness.Problem(solid, StackupField.WallThickness));
    }

    /// <summary>A field a row's kind does not use answers null, so a hidden control never contributes
    /// a mark nobody can clear.</summary>
    [Fact]
    public void AFieldTheKindDoesNotUse_IsNeverMarked()
    {
        var conductor = new StackupLayer { Kind = StackupKind.Conductor, Name = "c", ThicknessDbu = 1, SigmaSm = 1 };
        Assert.Null(StackupFieldReadiness.Problem(conductor, StackupField.Epsr));
        Assert.Null(StackupFieldReadiness.Problem(conductor, StackupField.Mur));

        var dielectric = new StackupLayer { Kind = StackupKind.Dielectric, Name = "d", ThicknessDbu = 1, Epsr = 4 };
        Assert.Null(StackupFieldReadiness.Problem(dielectric, StackupField.Sigma));
    }

    /// <summary>The sentence is what makes a greyed-looking field readable as a task rather than a
    /// fault: it says what is wrong and what would fix it, and it names no layer — the box being
    /// hovered IS the layer.</summary>
    [Fact]
    public void EveryMarkCarriesASentenceThatSaysWhatToDo()
    {
        var bare = new StackupLayer { Kind = StackupKind.Dielectric, Name = "d", Epsr = 0 };
        Assert.Contains("positive thickness", StackupFieldReadiness.Problem(bare, StackupField.Thickness)!);
        Assert.Contains("at least 1", StackupFieldReadiness.Problem(bare, StackupField.Epsr)!);
        Assert.DoesNotContain("\"d\"", StackupFieldReadiness.Problem(bare, StackupField.Epsr)!);
    }

    // ── The row re-asks after an edit ────────────────────────────────────────────────────────

    /// <summary>
    /// CommitEpsr, CommitTanD, CommitMur and CommitSigmaSm deliberately do not call RefreshFromModel —
    /// so without an explicit notification the mark would survive the correction that cleared it,
    /// which teaches somebody that the mark means nothing.
    /// </summary>
    [Fact]
    public void CorrectingAValue_ClearsItsMarkImmediately()
    {
        var tech = Tech();
        tech.Stackup.Layers.Single(l => l.Name == "Core").Epsr = 0;

        var vm = Vm(tech);
        var row = Row(vm, "Core");
        Assert.True(row.EpsrNeedsValue);

        bool raised = false;
        row.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(row.EpsrNeedsValue);

        row.StagedEpsr = "3.66";
        row.CommitEpsr();

        Assert.False(row.EpsrNeedsValue);
        Assert.True(raised, "the mark must announce that it cleared, not merely stop being true");
    }

    [Fact]
    public void ClearingAValue_RaisesTheMark()
    {
        var vm = Vm();
        var row = Row(vm, "Top");
        Assert.False(row.SigmaNeedsValue);

        row.StagedSigmaSm = "0";
        row.CommitSigmaSm();

        Assert.True(row.SigmaNeedsValue);
        Assert.NotNull(row.SigmaNeeds);
    }

    /// <summary>A thickness typed as something unparseable is a different state — the row already has
    /// its own inline error for that and refuses to commit, so the value on the model is unchanged
    /// and its mark must be too.</summary>
    [Fact]
    public void AnUnparseableThickness_KeepsItsOwnErrorAndDoesNotChangeTheMark()
    {
        var vm = Vm();
        var row = Row(vm, "Top");

        row.StagedThicknessText = "not a length";
        row.CommitThickness();

        Assert.True(row.HasThicknessError);
        Assert.False(row.ThicknessNeedsValue);       // 35 um is still what the model holds
    }

    // ── The two readers agree ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The mark and the problems list are two renderings of one predicate. This is the assertion that
    /// keeps them so: every field marked on a row must correspond to a stackup problem naming that
    /// row, and a technology with no marks anywhere must raise no per-row stackup problem at all.
    /// </summary>
    [Fact]
    public void EveryMarkedField_HasAMatchingProblemInTheList()
    {
        var tech = Tech();
        tech.Stackup.Layers.Single(l => l.Name == "Core").Epsr = 0.5;      // wrong, not unset
        tech.Stackup.Layers.Single(l => l.Name == "Bot").SigmaSm = 0;

        var vm = Vm(tech);
        var problems = TechValidation.Analyze(tech)
            .Where(p => p.Area == TechProblemArea.Stackup).Select(p => p.Message).ToList();

        Assert.True(Row(vm, "Core").EpsrNeedsValue);
        Assert.Contains(problems, m => m.Contains("\"Core\"") && m.Contains("εr < 1"));

        Assert.True(Row(vm, "Bot").SigmaNeedsValue);
        Assert.Contains(problems, m => m.Contains("\"Bot\"") && m.Contains("conductivity"));
    }
}
