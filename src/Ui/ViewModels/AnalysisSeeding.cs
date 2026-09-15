using System;
using System.Collections.Generic;
using System.Linq;
using CircuitRF.Core.Design;

namespace CircuitRF.Ui.ViewModels;

/// <summary>
/// The values a NEW analysis body opens with, read off the schematic it is being added to.
/// Every field is null for "nothing to say" — a body applies only the non-null ones and keeps its
/// own default for the rest, so a seed can never blank a field out.
/// </summary>
internal sealed record AnalysisSeed
{
    public string? LoadTunerName   { get; init; }
    public string? SourceTunerName { get; init; }
    /// <summary>Tone coefficient and unit travel together or not at all — a coefficient carried
    /// without its unit is exactly the reading-a-mark-without-its-scale defect.</summary>
    public string? ToneCoeff       { get; init; }
    public string? ToneUnit        { get; init; }
    public string? PinStartExpr    { get; init; }
    public string? PinMaxExpr      { get; init; }
    public string? PinStepExpr     { get; init; }
    public string? CompressionExpr { get; init; }
    public string? GridPath        { get; init; }

    public static readonly AnalysisSeed Empty = new();
}

/// <summary>
/// Prefills the Add Analysis dialog from what the schematic already says.
///
/// <para>The rule is one sentence: <b>a field is prefilled only when the schematic answers it
/// unambiguously.</b> One load-tuner symbol answers LoadTuner=; two do not, and the field opens
/// blank rather than guessing. Nothing here is a fallback at run time — every seeded value lands in
/// a visible dialog field the user can see and overwrite before OK, and an ambiguous case simply
/// leaves the field alone.</para>
///
/// <para><b>Precedence, for every field: an analysis the design already carries, then the
/// schematic's own components.</b> A tone the user typed into an LPP is a statement about this
/// design; a tone source's Freq parameter is a plausible default. So a new Loadpull beside an
/// existing Loadpull-Pursuit opens on that pursuit's tone, tuners and drive ladder — which is the
/// case this exists for, since the two are authored as a pair and differ in almost nothing else.
/// Among analyses, the same kind wins over the other loadpull kind over HB, and an enabled one wins
/// over a disabled one; ties go to schematic order.</para>
///
/// <para>Seeding runs for every body the dialog builds fresh — on Add, all of them; on Edit, the
/// ones the edited analysis did not fill, so switching an LPP to an LP in the Edit dialog carries
/// the tuners and tone across instead of dropping them.</para>
/// </summary>
internal static class AnalysisSeeding
{
    /// <summary>Which body is being seeded. Sets the analysis-kind preference, and whether the
    /// loadpull-only fields (tuners, drive ladder, grid) are collected at all.</summary>
    public enum Target { Hb, Lp, Lpp }

    public static AnalysisSeed For(SchematicEditModel model, Target target)
    {
        var tone = PickTone(model, target);

        // HB has no tuner, no drive ladder and no grid — the tone is the whole of what it can borrow.
        if (target == Target.Hb)
            return new AnalysisSeed { ToneCoeff = tone?.Coeff, ToneUnit = tone?.Unit };

        var donor = PickLoadpullDonor(model, target);

        return new AnalysisSeed
        {
            LoadTunerName   = PickTuner(model, donor?.LoadTunerName,   SymbolKind.LoadTuner),
            SourceTunerName = PickTuner(model, donor?.SourceTunerName, SymbolKind.SourceTuner),
            ToneCoeff       = tone?.Coeff,
            ToneUnit        = tone?.Unit,
            PinStartExpr    = donor?.PinStartExpr,
            PinMaxExpr      = donor?.PinMaxExpr,
            PinStepExpr     = donor?.PinStepExpr,
            CompressionExpr = donor?.CompressionExpr,
            GridPath        = target == Target.Lp ? PickGrid(model) : null,
        };
    }

    // ── Tuners ────────────────────────────────────────────────────────────────

    private static bool IsTuner(SymbolKind k)
        => k is SymbolKind.Tuner or SymbolKind.SourceTuner or SymbolKind.LoadTuner;

    /// <summary>
    /// The instance an existing analysis names, when that instance is still on the schematic;
    /// otherwise the one tuner placed on <paramref name="side"/>, when there is exactly one.
    /// The analysis comes first because it disambiguates a schematic carrying two of a side; the
    /// presence check is what stops a renamed or deleted tuner from being carried forward.
    /// A generic <see cref="SymbolKind.Tuner"/> is never picked by side — its glyph does not say
    /// which end it is on, and only the analysis that names it does.
    /// </summary>
    private static string? PickTuner(SchematicEditModel model, string? namedByAnalysis, SymbolKind side)
    {
        var tuners = model.Components.Where(c => IsTuner(c.Symbol)).ToList();

        if (!string.IsNullOrWhiteSpace(namedByAnalysis))
        {
            var placed = tuners.FirstOrDefault(c => string.Equals(
                c.InstanceName, namedByAnalysis.Trim(), StringComparison.OrdinalIgnoreCase));
            if (placed is not null) return placed.InstanceName;
        }

        var sided = tuners.Where(c => c.Symbol == side).ToList();
        return sided.Count == 1 ? sided[0].InstanceName : null;
    }

    // ── Drive ladder + grid, from an existing loadpull analysis ───────────────

    private sealed record LoadpullDonor(
        string LoadTunerName, string SourceTunerName,
        string PinStartExpr, string PinMaxExpr, string PinStepExpr, string CompressionExpr);

    private static LoadpullDonor? PickLoadpullDonor(SchematicEditModel model, Target target)
        => model.Analyses
            .Select(a => (Analysis: a, Donor: DonorOf(a)))
            .Where(x => x.Donor is not null)
            .OrderBy(x => KindRank(x.Analysis, target))
            .ThenBy(x => x.Analysis.Enabled ? 0 : 1)
            .Select(x => x.Donor)
            .FirstOrDefault();

    private static LoadpullDonor? DonorOf(Analysis a) => a switch
    {
        LoadpullAnalysis lp => new LoadpullDonor(
            lp.LoadTunerName, lp.SourceTunerName,
            lp.PinStartExpr, lp.PinMaxExpr, lp.PinStepExpr, lp.CompressionExpr),
        LoadpullPursuitAnalysis lpp => new LoadpullDonor(
            lpp.LoadTunerName, lpp.SourceTunerName,
            lpp.PinStartExpr, lpp.PinMaxExpr, lpp.PinStepExpr, lpp.CompressionExpr),
        _ => null,
    };

    /// <summary>
    /// The .gam a loadpull would read: another Loadpull's Grid, or else the file a Pursuit on this
    /// schematic is set to WRITE — which is the shape of the pair, the pursuit finding the region
    /// and the loadpull sweeping the grid it produced.
    /// </summary>
    private static string? PickGrid(SchematicEditModel model)
    {
        var fromLp = model.Analyses.OfType<LoadpullAnalysis>()
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.GridPath));
        if (fromLp is not null) return fromLp.GridPath.Trim();

        var fromLpp = model.Analyses.OfType<LoadpullPursuitAnalysis>()
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.OutputGridPath));
        return fromLpp?.OutputGridPath?.Trim();
    }

    // ── Tone ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The tone, from (1) an analysis that already declares one, (2) the single tone SOURCE on the
    /// schematic, (3) the single frequency-dimensioned VAR. Each step needs the answer to be
    /// unambiguous; a schematic with two tone sources or two frequency VARs falls through to null
    /// and the field keeps the body's own default.
    /// </summary>
    private static (string Coeff, string Unit)? PickTone(SchematicEditModel model, Target target)
    {
        var fromAnalysis = model.Analyses
            .Select(a => (Analysis: a, Tone: ToneOf(a)))
            .Where(x => x.Tone is not null)
            .OrderBy(x => KindRank(x.Analysis, target))
            .ThenBy(x => x.Analysis.Enabled ? 0 : 1)
            .Select(x => x.Tone)
            .FirstOrDefault();
        if (fromAnalysis is not null) return fromAnalysis;

        var sources = model.Components
            .Where(c => c.Symbol is SymbolKind.P1Tone or SymbolKind.ToneSource
                                 or SymbolKind.CurrentToneSource)
            .ToList();
        if (sources.Count == 1)
        {
            var freq = sources[0].Parameters.FirstOrDefault(p => p.Name == "Freq");
            if (freq is not null)
            {
                var tone = FromComponentFreq(freq.Expression, freq.Unit);
                if (tone is not null) return tone;
            }
        }

        return PickFreqVar(model);
    }

    /// <summary>The one frequency-unit VAR parameter on the schematic, seeded by NAME so the tone
    /// stays a reference to it (var-unit-wins resolves it exactly at run time) rather than a copy of
    /// the number it holds today. Several of them: prefer one whose name reads as a frequency, and
    /// give up if that is not unique either.</summary>
    private static (string Coeff, string Unit)? PickFreqVar(SchematicEditModel model)
    {
        var vars = model.Components
            .Where(c => c.Symbol == SymbolKind.Var)
            .SelectMany(c => c.Parameters)
            .Where(p => !string.IsNullOrWhiteSpace(p.Name) && IsDialogFreqUnit(p.Unit))
            .ToList();

        if (vars.Count != 1)
            vars = vars.Where(p => p.Name.Contains("freq", StringComparison.OrdinalIgnoreCase)).ToList();
        if (vars.Count != 1) return null;

        return (vars[0].Name.Trim(), vars[0].Unit.Trim());
    }

    /// <summary>
    /// Kind preference for a donor analysis: the same kind first, then the other loadpull kind,
    /// then HB. <see cref="Enumerable.OrderBy{TSource,TKey}(IEnumerable{TSource},Func{TSource,TKey})"/>
    /// is stable, so within one rank the schematic's own order decides.
    /// </summary>
    private static int KindRank(Analysis a, Target target) => (target, a) switch
    {
        (Target.Lp,  LoadpullAnalysis)        => 0,
        (Target.Lp,  LoadpullPursuitAnalysis) => 1,
        (Target.Lpp, LoadpullPursuitAnalysis) => 0,
        (Target.Lpp, LoadpullAnalysis)        => 1,
        (Target.Hb,  HarmonicBalanceAnalysis) => 0,
        (Target.Hb,  _)                       => 1,
        _                                     => 2,   // HB seen from a loadpull target
    };

    private static (string Coeff, string Unit)? ToneOf(Analysis a) => a switch
    {
        HarmonicBalanceAnalysis hb  => FromAnalysisTone(hb.ToneExpr,  hb.ToneUnit),
        LoadpullAnalysis lp         => FromAnalysisTone(lp.ToneExpr,  lp.ToneUnit),
        LoadpullPursuitAnalysis lpp => FromAnalysisTone(lpp.ToneExpr, lpp.ToneUnit),
        _                           => null,
    };

    /// <summary>
    /// A stored tone (coefficient + unit) as the dialog's two fields. "0" is the unset default every
    /// analysis type carries, not a frequency. A Hz-stored plain number is split for display exactly
    /// as the bodies' own FromAnalysis does, so a seeded tone reads the same as an edited one.
    /// </summary>
    private static (string Coeff, string Unit)? FromAnalysisTone(string expr, string unit)
    {
        string e = expr?.Trim() ?? "";
        if (e.Length == 0 || e == "0") return null;

        string u = string.IsNullOrWhiteSpace(unit) ? "Hz" : unit.Trim();
        if (u == "Hz") return FreqUnitHelper.Split(e);
        return IsDialogFreqUnit(u) ? (e, u) : null;
    }

    /// <summary>
    /// A component's Freq parameter as the dialog's two fields. Unlike a stored analysis tone this
    /// is NOT Hz-split and NOT accepted without a unit: a unitless "2" on a source means whatever
    /// the schematic author meant, and reading it into a GHz field would be off by a factor of 1e9
    /// in a way nothing downstream would flag.
    /// </summary>
    private static (string Coeff, string Unit)? FromComponentFreq(string expr, string unit)
    {
        string e = expr?.Trim() ?? "";
        return e.Length > 0 && IsDialogFreqUnit(unit) ? (e, unit.Trim()) : null;
    }

    /// <summary>True for a unit the tone dropdown actually offers. A frequency parameter may carry
    /// "THz" or "None", which the dropdown cannot show — those seed nothing rather than being
    /// silently rewritten into a unit that means something else.</summary>
    private static bool IsDialogFreqUnit(string? unit)
        => !string.IsNullOrWhiteSpace(unit)
           && FreqUnitHelper.Units.Contains(unit.Trim(), StringComparer.Ordinal);
}
