using System;
using System.Collections.Generic;
using System.Linq;

namespace CircuitRF.Core.Design;

/// <summary>
/// Resolves parametric-sweep chains honoring <see cref="Analysis.Enabled"/>.
/// A disabled sweep "collapses": its axis is dropped and its own inner is adopted in its place.
/// A disabled base analysis makes the whole chain inert.
/// The chain is linked by <see cref="ParametricSweepAnalysis.InnerAnalysisName"/>.
/// </summary>
public static class AnalysisChain
{
    private const int MaxDepth = 64;   // cycle guard

    private static Analysis? Find(string name, TestBench tb)
        => tb.Analyses.FirstOrDefault(x => x.Name == name);

    /// <summary>
    /// The next analysis to actually run when descending into <paramref name="innerName"/>, skipping
    /// disabled parametric sweeps. Returns the first ENABLED sweep or ANY base analysis reached, or null
    /// if the name resolves to nothing.
    /// </summary>
    public static Analysis? ResolveEffectiveInner(string innerName, TestBench tb)
    {
        Analysis? a = Find(innerName, tb);
        int guard = 0;
        while (a is ParametricSweepAnalysis ps && !a.Enabled && guard++ < MaxDepth)
            a = Find(ps.InnerAnalysisName, tb);
        return a;
    }

    /// <summary>
    /// From a chain root, descend past disabled OUTER sweeps to the outermost analysis that runs
    /// (an enabled sweep, or a base). Null if it runs off the end.
    /// </summary>
    public static Analysis? ResolveEffectiveTop(Analysis root, TestBench tb)
    {
        Analysis? a = root;
        int guard = 0;
        while (a is ParametricSweepAnalysis ps && !a.Enabled && guard++ < MaxDepth)
            a = Find(ps.InnerAnalysisName, tb);
        return a;
    }

    /// <summary>
    /// True when <paramref name="top"/> bottoms out at an ENABLED base analysis after skipping disabled
    /// sweeps. A disabled base ⇒ the whole chain is inert ⇒ false.
    /// </summary>
    public static bool IsChainRunnable(Analysis top, TestBench tb)
    {
        Analysis? a = top;
        int guard = 0;
        while (a is ParametricSweepAnalysis ps && guard++ < MaxDepth)
            a = ResolveEffectiveInner(ps.InnerAnalysisName, tb);
        return a is { Enabled: true };
    }

    /// <summary>
    /// Every chain ROOT that actually runs, in declaration order: an analysis nobody references as
    /// their inner, resolved past disabled outer sweeps, whose chain bottoms out at an enabled base.
    /// This is the list a full run dispatches, and the list a named analysis is promoted INTO.
    /// </summary>
    public static IReadOnlyList<Analysis> RunnableTops(TestBench tb)
    {
        var inner = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in tb.Analyses)
            if (a is ParametricSweepAnalysis ps && !string.IsNullOrEmpty(ps.InnerAnalysisName))
                inner.Add(ps.InnerAnalysisName);

        var tops = new List<Analysis>();
        foreach (var root in tb.Analyses)
        {
            if (inner.Contains(root.Name)) continue;          // not a root — runs via its outer
            var top = ResolveEffectiveTop(root, tb);
            if (top is null || !top.Enabled) continue;        // whole chain disabled
            if (!IsChainRunnable(top, tb)) continue;          // base disabled → nothing runs
            tops.Add(top);
        }
        return tops;
    }

    /// <summary>
    /// True when <paramref name="name"/> is <paramref name="top"/> itself or something it wraps,
    /// descending past disabled sweeps exactly as a run does.
    /// </summary>
    public static bool ChainContains(Analysis top, string name, TestBench tb)
    {
        Analysis? a = top;
        for (int guard = 0; a is not null && guard < MaxDepth; guard++)
        {
            if (a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
            if (a is not ParametricSweepAnalysis ps) return false;
            a = ResolveEffectiveInner(ps.InnerAnalysisName, tb);
        }
        return false;
    }

    /// <summary>
    /// The analysis a run DISPATCHES when the caller names <paramref name="named"/>: the outermost
    /// runnable chain containing it, or <paramref name="named"/> itself when it belongs to none.
    /// <para/>
    /// <b>Naming an inner analysis is promoted rather than honoured</b>, because the alternative is a
    /// converged, plausible, complete-looking result with the sweep axis silently missing — a
    /// freq-swept loadpull run at one frequency looks exactly like a loadpull. The CLI's run verbs and
    /// the Analyses panel card's own Run item share this one rule, so the same card runs the same
    /// thing whichever surface asked.
    /// </summary>
    public static Analysis PromoteToRunnableTop(Analysis named, TestBench tb)
    {
        foreach (var top in RunnableTops(tb))
            if (ChainContains(top, named.Name, tb)) return top;
        return named;
    }

    /// <summary>The promotion note, worded once for every surface that reports it.</summary>
    public static string PromotionNote(Analysis promotedFrom, Analysis owner) =>
        $"'{promotedFrom.Name}' is the inner analysis of '{owner.Name}' — running '{owner.Name}' " +
        $"so the sweep axis is not lost.";
}
