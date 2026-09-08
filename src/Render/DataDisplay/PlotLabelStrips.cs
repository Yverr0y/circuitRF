// ================================================================
//  PlotLabelStrips.cs  —  which traces get a Y-axis label strip, and
//  what each strip says
//
//  RND-4 (R-rnd4-2/R-rnd4-7). The strips are CONTENT — R-rnd4-7 lists
//  them among the things that come out in an export, beside the markers
//  and the info boxes — so a headless render has to produce exactly the
//  set the window produces, on the same side, in the same order. That
//  rule was inside `PlotContainerViewModel.UpdateLabelStrips`, mixed in
//  with the strip VIEW MODELS it also builds (their width, their theme,
//  the on-screen AutoLabel that the export deliberately does not use).
//
//  Split so the rule is said once: the view model maps these onto its own
//  LabelStripViewModels, and `circuitrf render` places them directly.
//
//  Smith and Polar only — a Rect plot renders its Y-axis labels INSIDE
//  the Skia canvas margin and has no strips at all.
// ================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace CircuitRF.Render.DataDisplay;

public static class PlotLabelStrips
{
    /// <summary>
    /// The left and right strips for <paramref name="plot"/>.
    ///
    /// <para>A CUSTOM axis label collapses that side to ONE strip carrying the custom text — it
    /// names the axis, not a trace, so per-trace strips beside it would be saying two different
    /// things about the same axis. With no custom label there is one strip per trace on that side;
    /// contour traces are excluded, having no Y axis of their own.</para>
    /// </summary>
    public static (IReadOnlyList<PlacedLabelStrip> Left, IReadOnlyList<PlacedLabelStrip> Right)
        For(Plot plot, bool showFilePrefix)
    {
        if (!plot.PlotType.IsComplex())
            return (Array.Empty<PlacedLabelStrip>(), Array.Empty<PlacedLabelStrip>());

        var leftTraces  = plot.LeftAxisTraces.Where(t => !t.IsContourTrace).ToList();
        var rightTraces = plot.RightAxisTraces.Where(t => !t.IsContourTrace).ToList();

        return (Side(leftTraces,  plot.CustomYLabelOn,  plot.CustomYLabel,  showFilePrefix),
                Side(rightTraces, plot.CustomY2LabelOn, plot.CustomY2Label, showFilePrefix));
    }

    private static IReadOnlyList<PlacedLabelStrip> Side(
        IReadOnlyList<Trace> traces, bool hasCustom, string? customLabel, bool showFilePrefix)
    {
        if (hasCustom)
            return traces.Count > 0
                ? [new PlacedLabelStrip(traces[0], customLabel, showFilePrefix)]
                : Array.Empty<PlacedLabelStrip>();

        return traces.Select(t => new PlacedLabelStrip(t, null, showFilePrefix)).ToList();
    }
}
