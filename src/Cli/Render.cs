using System.Globalization;
using System.Text;
using CircuitRF.Design.Cells;
using CircuitRF.Design.Layout;
using CircuitRF.Design.Schematic;
using CircuitRF.Design.Symbol;
using CircuitRF.Design.Workspace;
using CircuitRF.Diagnostics;
using CircuitRF.Render;
using RfCore.Export;
using SkiaSharp;

namespace CircuitRF.Cli;

/// <summary>
/// <c>circuitrf render &lt;path&gt; -o &lt;out.svg|.pdf|.png&gt;</c> — turn a schematic, a symbol or a
/// layout into a picture, headlessly (brief-render-2-render-verb.md).
///
/// <para><b>This file draws nothing and it must never start.</b> R-rnd0-1: there is one renderer, and
/// every pixel here comes out of <c>SchematicRenderer</c>, <c>SymbolEditorRenderer</c> and
/// <c>LayoutRenderer</c> in <c>CircuitRF.Render</c> — the same ~7,000 lines of measured Skia that draw
/// every frame the application shows and produce the SVG and PDF on its clipboard. What is here is
/// argument parsing, viewport arithmetic, refusals and reporting, which is what
/// <c>src/Cli/Authoring.cs</c> already established a CLI verb is allowed to be. A CLI that
/// re-implemented any of the drawing would drift, and the drift would be invisible: a picture that is
/// PLAUSIBLE is indistinguishable from a picture that is RIGHT.</para>
///
/// <para><b>One verb over every document kind</b> (R-rnd0-4/R-rnd2-1), with the kind inferred from the
/// path through <see cref="DocumentKinds.Classify"/> — the same function <c>check</c> and
/// <c>explain</c> infer with. There is no <c>render-schematic</c>.</para>
///
/// <para><b>An orphan document is a first-class input, not a degraded one.</b> A <c>.clay</c> with no
/// workspace above it resolves no technology, renders on the fallback palette exactly as the layout
/// editor does with an unresolved technology, and says so as a NOTE. A caller rendering a bare
/// <c>.clay</c> handed to it by a converter already knows there is no workspace; making that a warning
/// would train it to ignore warnings.</para>
///
/// <para><b>Every question the GUI would have asked in a dialog is a refusal naming the flag that
/// answers it</b> (R-rnd0-6) — a cell folder holding three views, an output extension this verb does
/// not write, a layer the technology does not define, a bare number where a layout coordinate belongs.
/// Nothing here defaults its way past a question the caller has not answered.</para>
/// </summary>
internal static class Render
{
    // ── the shapes of the argument surface ───────────────────────────────────

    private enum Format { Svg, Pdf, Png }

    /// <summary>Which of R-rnd2-3's three the caller asked for. They are refused TOGETHER rather than
    /// ordered, so exactly one is ever in force.</summary>
    private enum ViewportMode { Fit, Window, Center }

    private enum Detail { Full, Screen, Budget }

    /// <summary>
    /// A world-space rectangle as the caller wrote it, in the document's own coordinate space —
    /// integer DBU for a layout, dimensionless design units for a schematic or symbol.
    /// </summary>
    private readonly record struct WorldRect(double X0, double Y0, double X1, double Y1)
    {
        public double W => X1 - X0;
        public double H => Y1 - Y0;
    }

    /// <summary>Everything the run decided, gathered so the report and the picture cannot disagree
    /// about what was drawn.</summary>
    private sealed class Options
    {
        public string?      Path;
        public string?      Output;
        public Format?      Format;
        public ViewportMode Mode = ViewportMode.Fit;
        /// <summary>Whether <c>--fit</c> was actually TYPED. It is also the default, so "the mode is
        /// fit" cannot stand in for "the caller asked for fit" — without this the three-modes refusal
        /// would never fire, because the flag that lost the race sets the mode away from Fit.</summary>
        public bool         FitStated;
        public string?      WindowText;
        public string?      CenterText;
        public string?      SpanText;
        public double       Margin = DefaultMargin;
        public int          Width  = DefaultWidth;
        public int          Height = DefaultHeight;
        public double       Scale  = 1.0;
        public string?      ScaleOption;          // which spelling asked for it, for the refusal
        public ViewType?    View;
        public string?      Cell;
        public string[]?    OnlyLayers;
        public string[]?    HideLayers;
        public Detail       Detail = Detail.Full;
        public string       DetailText = "full";
        public double       DetailBudget;
        public string?      Theme;
        public ColorVariant Variant = ColorVariant.Light;
        public bool         Transparent;
        public bool         Grid;
        public bool         NoRulers;
    }

    /// <summary>R-rnd2-3's default page. Points for a vector format, device pixels for a raster one —
    /// 1600x1200 is a 4:3 page that reads at a glance in either.</summary>
    private const int DefaultWidth  = 1600;
    private const int DefaultHeight = 1200;

    /// <summary><c>LayoutViewport.ZoomToFit</c>'s own default, which is the margin the application's
    /// Zoom to Fit uses. The schematic and symbol halves take the same number so a fitted picture of
    /// one document kind is framed like a fitted picture of another.</summary>
    private const double DefaultMargin = 0.10;

    // ── entry ────────────────────────────────────────────────────────────────

    public static int Run(string[] args)
    {
        var o = new Options();
        if (Parse(args, o) is { } bad) return bad;

        if (o.Path is null)   { JsonRun.Report(CliDiagnostics.RenderPathRequired());   return Usage(); }
        if (o.Output is null) { JsonRun.Report(CliDiagnostics.RenderOutputRequired()); return Usage(); }
        JsonRun.InputPath = o.Path;

        if (!File.Exists(o.Path) && !Directory.Exists(o.Path))
            return JsonRun.Fail(CliDiagnostics.RenderPathNotFound(o.Path));

        if (ResolveFormat(o) is { } formatRefusal) return formatRefusal;

        // The three viewport modes, refused together. Done before anything is read: a caller that
        // asked two incompatible questions gets the same answer whether or not the file parses.
        if (ViewportRefusal(o) is { } viewportRefusal) return viewportRefusal;

        try
        {
            return Draw(o);
        }
        catch (OperationCanceledException)
        {
            // §7 / gate 7: a cancelled run abandons its result rather than publishing a partial one.
            // Nothing is written because nothing is written until the bytes are complete — see Emit.
            JsonRun.Report(CliDiagnostics.RenderCancelled());
            return 130;
        }
    }

    private static int Usage()
    {
        Console.Error.WriteLine(
            "Usage: circuitrf render <path> -o <out.svg|.pdf|.png> [--view V] [--cell N]\n" +
            "                        [--fit | --window x0,y0,x1,y1 | --center x,y --span w]\n" +
            "                        [--margin f] [--size WxH] [--scale n | --dpi n]\n" +
            "                        [--layers a,b | --hide-layers a,b] [--detail full|screen|<px>]\n" +
            "                        [--theme name|file.ccolor] [--variant light|dark]\n" +
            "                        [--background opaque|transparent] [--grid] [--no-rulers]");
        return 1;
    }

    // ── arguments ────────────────────────────────────────────────────────────

    private static int? Parse(string[] args, Options o)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-o" or "--output" when i + 1 < args.Length: o.Output = args[++i]; continue;
                case "--format" when i + 1 < args.Length:
                    switch (args[++i].ToLowerInvariant())
                    {
                        case "svg": o.Format = Format.Svg; break;
                        case "pdf": o.Format = Format.Pdf; break;
                        case "png": o.Format = Format.Png; break;
                        default: return JsonRun.Fail(CliDiagnostics.RenderUnknownFormatName(args[i]));
                    }
                    continue;

                case "--view" when i + 1 < args.Length:
                    switch (args[++i].ToLowerInvariant())
                    {
                        case "schematic": o.View = ViewType.Schematic; break;
                        case "symbol":    o.View = ViewType.Symbol;    break;
                        case "layout":    o.View = ViewType.Layout;    break;
                        default: return JsonRun.Fail(CliDiagnostics.RenderNoSuchView("--view", args[i]));
                    }
                    continue;

                case "--cell" when i + 1 < args.Length: o.Cell = args[++i]; continue;

                case "--fit":    o.FitStated = true; continue;
                case "--window" when i + 1 < args.Length:
                    o.WindowText = args[++i]; o.Mode = ViewportMode.Window; continue;
                case "--center" when i + 1 < args.Length:
                    o.CenterText = args[++i]; o.Mode = ViewportMode.Center; continue;
                case "--span" when i + 1 < args.Length:
                    o.SpanText = args[++i]; o.Mode = ViewportMode.Center; continue;

                case "--margin" when i + 1 < args.Length:
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double m)
                        || m < 0 || m > 0.45)
                        return JsonRun.Fail(CliDiagnostics.RenderMarginMalformed(args[i]));
                    o.Margin = m;
                    continue;

                case "--size" when i + 1 < args.Length:
                {
                    var parts = args[++i].Split('x', 'X');
                    if (parts.Length != 2
                        || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
                        || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
                        || w < 8 || h < 8 || w > 20000 || h > 20000)
                        return JsonRun.Fail(CliDiagnostics.RenderSizeMalformed(args[i]));
                    o.Width = w; o.Height = h;
                    continue;
                }

                case "--scale" when i + 1 < args.Length:
                {
                    if (o.ScaleOption is not null) return JsonRun.Fail(CliDiagnostics.RenderScaleAndDpi());
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double s)
                        || !(s > 0) || s > 16)
                        return JsonRun.Fail(CliDiagnostics.RenderScaleMalformed("--scale", args[i]));
                    o.Scale = s; o.ScaleOption = "--scale";
                    continue;
                }

                case "--dpi" when i + 1 < args.Length:
                {
                    if (o.ScaleOption is not null) return JsonRun.Fail(CliDiagnostics.RenderScaleAndDpi());
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                        || !(d > 0) || d > 1536)
                        return JsonRun.Fail(CliDiagnostics.RenderScaleMalformed("--dpi", args[i]));
                    // A spelling, not a second mechanism: 96 dpi is 1x, which is what every desktop
                    // toolkit means by an unscaled pixel.
                    o.Scale = d / 96.0; o.ScaleOption = "--dpi";
                    continue;
                }

                case "--layers" when i + 1 < args.Length:      o.OnlyLayers = SplitList(args[++i]); continue;
                case "--hide-layers" when i + 1 < args.Length: o.HideLayers = SplitList(args[++i]); continue;

                case "--detail" when i + 1 < args.Length:
                    o.DetailText = args[++i];
                    switch (o.DetailText.ToLowerInvariant())
                    {
                        case "full":   o.Detail = Detail.Full;   break;
                        case "screen": o.Detail = Detail.Screen; break;
                        default:
                            if (!double.TryParse(o.DetailText, NumberStyles.Float, CultureInfo.InvariantCulture,
                                                 out double px) || !(px > 0) || px > 4096)
                                return JsonRun.Fail(CliDiagnostics.RenderDetailMalformed(o.DetailText));
                            o.Detail = Detail.Budget; o.DetailBudget = px;
                            break;
                    }
                    continue;

                case "--theme" when i + 1 < args.Length: o.Theme = args[++i]; continue;
                case "--variant" when i + 1 < args.Length:
                    switch (args[++i].ToLowerInvariant())
                    {
                        case "light": o.Variant = ColorVariant.Light; break;
                        case "dark":  o.Variant = ColorVariant.Dark;  break;
                        default: return JsonRun.Fail(CliDiagnostics.RenderUnknownVariant(args[i]));
                    }
                    continue;
                case "--background" when i + 1 < args.Length:
                    switch (args[++i].ToLowerInvariant())
                    {
                        case "opaque":      o.Transparent = false; break;
                        case "transparent": o.Transparent = true;  break;
                        default: return JsonRun.Fail(CliDiagnostics.RenderUnknownBackground(args[i]));
                    }
                    continue;
                case "--grid":      o.Grid = true;     continue;
                case "--no-rulers": o.NoRulers = true; continue;

                default:
                    if (a.StartsWith('-'))
                    { JsonRun.Report(CliDiagnostics.RenderUnknownOption(a)); return Usage(); }
                    if (o.Path is not null)
                    { JsonRun.Report(CliDiagnostics.RenderMultiplePaths()); return Usage(); }
                    o.Path = a;
                    continue;
            }
        }
        return null;
    }

    private static string[] SplitList(string s)
        => [.. s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>R-rnd2-2: <c>-o</c>'s extension chooses the format, exactly as <c>convert</c> infers
    /// one from a path; <c>--format</c> overrides. An extension this verb does not write is a refusal
    /// listing the three, never a default.</summary>
    private static int? ResolveFormat(Options o)
    {
        if (o.Format is not null) return null;

        string ext = Path.GetExtension(o.Output!).ToLowerInvariant();
        o.Format = ext switch
        {
            ".svg" => Format.Svg,
            ".pdf" => Format.Pdf,
            ".png" => Format.Png,
            _      => null,
        };
        return o.Format is null
            ? JsonRun.Fail(CliDiagnostics.RenderUnknownOutputFormat(o.Output!, ext.Length == 0 ? "(none)" : ext))
            : null;
    }

    private static int? ViewportRefusal(Options o)
    {
        // R-rnd2-3: the three are refused TOGETHER rather than ordered. --fit is also the DEFAULT, so
        // it is counted only when it was actually typed — otherwise every invocation would look like
        // two modes.
        var stated = new List<string>();
        if (o.FitStated) stated.Add("--fit");
        if (o.WindowText is not null) stated.Add("--window");
        if (o.CenterText is not null || o.SpanText is not null) stated.Add("--center/--span");
        if (stated.Count > 1) return JsonRun.Fail(CliDiagnostics.RenderViewportModes(string.Join(" and ", stated)));

        if (o.Mode == ViewportMode.Center && (o.CenterText is null || o.SpanText is null))
            return JsonRun.Fail(CliDiagnostics.RenderSpanRequired());

        if (o.ScaleOption is not null && o.Format is not Format.Png)
            return JsonRun.Fail(CliDiagnostics.RenderScaleOnVector(
                o.ScaleOption, o.Format == Format.Svg ? "svg" : "pdf"));

        if (o.OnlyLayers is not null && o.HideLayers is not null)
            return JsonRun.Fail(CliDiagnostics.RenderLayersConflict());

        return null;
    }

    // ── which document ───────────────────────────────────────────────────────

    /// <summary>The one document this run draws, and how it was arrived at.</summary>
    private readonly record struct Target(string File, ViewType View, string? ViewName);

    /// <summary>
    /// R-rnd2-1's three inputs: a view FILE, a cell FOLDER (the view chosen by <c>--view</c>, or the
    /// sole one it holds), and a WORKSPACE with <c>--cell</c>. Primacy inside a view sub-folder is
    /// <c>CellFolder.ResolvePrimary</c>'s answer and no other.
    /// </summary>
    private static (Target? Target, int? Refusal) ResolveTarget(Options o)
    {
        string path = o.Path!;
        var kind = DocumentKinds.Classify(path);

        switch (kind)
        {
            case DocumentKind.Schematic: return (new Target(path, ViewType.Schematic, null), null);
            case DocumentKind.Symbol:    return (new Target(path, ViewType.Symbol,    null), null);
            case DocumentKind.Layout:    return (new Target(path, ViewType.Layout,    null), null);

            case DocumentKind.Cell:
                return ResolveCellFolder(path, o);

            case DocumentKind.Workspace:
            {
                string root = Directory.Exists(path)
                    ? Path.GetFullPath(path)
                    : Path.GetDirectoryName(Path.GetFullPath(path))!;
                if (o.Cell is null)
                    return (null, JsonRun.Fail(CliDiagnostics.RenderCellRequired(path)));

                var found = CellLookup.Find(root, o.Cell);
                if (found.Count == 0)
                    return (null, JsonRun.Fail(CliDiagnostics.RenderNoSuchCell(
                        path, o.Cell, Join(CellLookup.Names(root)))));
                if (found.Count > 1)
                    return (null, JsonRun.Fail(CliDiagnostics.RenderAmbiguousCell(o.Cell, Join(found))));
                return ResolveCellFolder(found[0], o);
            }

            case DocumentKind.Interchange:
                return (null, JsonRun.Fail(CliDiagnostics.RenderNotDrawable(
                    path, DocumentKinds.InterchangeFormat(path) ?? "interchange")));

            default:
                return (null, JsonRun.Fail(CliDiagnostics.RenderNotDrawable(path, DocumentKinds.Name(kind))));
        }
    }

    private static (Target? Target, int? Refusal) ResolveCellFolder(string cellDir, Options o)
    {
        var held = new List<ViewType>();
        foreach (var v in DocumentKinds.AllViewTypes)
            if (CellFolder.ResolvePrimary(cellDir, v).State != PrimaryState.NoView) held.Add(v);

        if (held.Count == 0)
            return (null, JsonRun.Fail(CliDiagnostics.RenderNothingToDraw(cellDir)));

        ViewType view;
        if (o.View is { } asked)
        {
            if (!held.Contains(asked))
                return (null, JsonRun.Fail(CliDiagnostics.RenderNoSuchView(cellDir, asked.ToString().ToLowerInvariant())));
            view = asked;
        }
        else if (held.Count > 1)
        {
            // R-rnd0-6: the dialog's own question, so it is a refusal that LISTS them and names --view.
            return (null, JsonRun.Fail(CliDiagnostics.RenderViewRequired(
                cellDir, Join([.. held.Select(v => CellFolder.SubFolderName(v))]))));
        }
        else view = held[0];

        var primary = CellFolder.ResolvePrimary(cellDir, view);
        if (primary.ResolvedName is not { Length: > 0 } name)
            return (null, JsonRun.Fail(CliDiagnostics.RenderNoPrimary(
                cellDir, CellFolder.SubFolderName(view), primary.State.ToString())));

        return (new Target(Path.Combine(CellFolder.SubFolderPath(cellDir, view), name), view, name), null);
    }

    private static string Join(IReadOnlyList<string> items)
        => items.Count == 0 ? "(none)" : string.Join(", ", items);

    // ── the run ──────────────────────────────────────────────────────────────

    private static int Draw(Options o)
    {
        var control = RunHost.Control;
        control?.BeginStage("resolve");
        Progress("resolve");

        var (target, refusal) = ResolveTarget(o);
        if (refusal is { } r) return r;
        var t = target!.Value;

        RunHost.Cancellation.ThrowIfCancellationRequested();

        return t.View switch
        {
            ViewType.Layout => DrawLayout(o, t),
            ViewType.Symbol => DrawSymbol(o, t),
            _               => DrawSchematic(o, t),
        };
    }

    /// <summary>
    /// R-rnd2-10's stderr half. §3.1: stdout is the result, stderr is everything else — so the stage
    /// lines go here and cost the <c>--json</c> document nothing.
    /// </summary>
    private static void Progress(string stage) => Console.Error.WriteLine($"[circuitRF] {stage}...");

    // ── layout ───────────────────────────────────────────────────────────────

    private static int DrawLayout(Options o, Target t)
    {
        LayoutView view;
        try { view = LayoutPersistence.LoadFromFile(t.File); }
        catch (Exception ex) { return JsonRun.Fail(CliDiagnostics.RenderDocumentUnreadable(t.File, ex.Message)); }

        string full    = Path.GetFullPath(t.File);
        string baseDir = Path.GetDirectoryName(Path.GetDirectoryName(full)!) ?? "";
        var cache      = new TechnologyCache();
        var (resolved, _) = TechnologyResolver.ResolveForDocument(view.TechRef, full, null, cache);

        foreach (var d in resolved.Diagnostics)
            Note(CliDiagnostics.RenderResolverNote(t.File, d));

        // R-rnd2-1: a NOTE, never a warning and never a refusal.
        if (resolved.Source == TechResolutionSource.None)
            Note(CliDiagnostics.RenderNoTechnology(t.File));

        var tech = resolved.Tech;

        // R-rnd2-8: the selection is applied to a CLONE. TechnologyCache hands back a SHARED instance,
        // and flipping LayerDef.Visible on it would leak into the next render in the same process —
        // which is not hypothetical, because `serve` runs many calls in one. This is the class of
        // defect that only ever appears on the SECOND call.
        var (drawTech, layerReport, layerRefusal) = ApplyLayerSelection(tech, view, o);
        if (layerRefusal is { } lr) return lr;

        RunHost.Cancellation.ThrowIfCancellationRequested();
        RunHost.Control?.BeginStage("measure");
        Progress("measure");

        int pxW = (int)Math.Round(o.Width  * o.Scale);
        int pxH = (int)Math.Round(o.Height * o.Scale);

        var extents = LayoutExtents(view, drawTech, baseDir, pxW, pxH);
        if (extents.IsEmpty) return JsonRun.Fail(CliDiagnostics.RenderNothingToDraw(t.File));

        Console.Error.WriteLine(
            $"[circuitRF] {view.Shapes.Count} shape(s), {view.Instances.Count} instance placement(s), " +
            $"{(drawTech?.Layers.Count ?? 0)} layer(s)");

        var (vp, letterboxed, vpRefusal) = LayoutViewportFor(o, view, extents, pxW, pxH);
        if (vpRefusal is { } vr) return vr;

        var (theme, themeName, themeFrom, themeRefusal) = ResolveTheme(o, full);
        if (themeRefusal is { } tr) return tr;

        var opts = LayoutOptionsFor(o, theme, baseDir);

        RunHost.Cancellation.ThrowIfCancellationRequested();
        RunHost.Control?.BeginStage("draw");
        Progress("draw");

        LayoutRenderResult result = default;
        byte[] bytes = Emit(o, pxW, pxH,
            canvas => result = LayoutRenderer.Draw(canvas, view, drawTech, vp, opts));

        foreach (var missing in result.MissingInstanceCellRefs ?? [])
            Note(CliDiagnostics.RenderResolverNote(t.File, $"instance reference '{missing}' did not resolve"));

        double metresPerDbu = 1e-6 / Math.Max(1, view.DbuPerMicron);
        double displayScale = MetresPerUnit(view.DisplayUnit);

        long tolerance = o.Detail switch
        {
            Detail.Budget => LayoutRenderDetail.ToleranceDbu(o.DetailBudget, vp.Zoom),
            Detail.Screen => LayoutRenderDetail.ToleranceDbu(LayoutRenderer.DefaultDetailPixelThreshold, vp.Zoom),
            _             => 0,
        };

        return Publish(o, t, DocumentKind.Layout, bytes,
            new RenderViewportJson(
                ModeName(o.Mode),
                vp.VisibleMinX * metresPerDbu, vp.VisibleMinY * metresPerDbu,
                vp.VisibleMaxX * metresPerDbu, vp.VisibleMaxY * metresPerDbu,
                "m", displayScale, vp.Zoom, letterboxed),
            new RenderExtentsJson(
                extents.MinX * metresPerDbu, extents.MinY * metresPerDbu,
                extents.MaxX * metresPerDbu, extents.MaxY * metresPerDbu,
                "m", displayScale),
            new RenderThemeJson(themeName, o.Variant == ColorVariant.Dark ? "dark" : "light", themeFrom),
            layerReport,
            new RenderDetailJson(o.DetailText.ToLowerInvariant(), tolerance > 0 ? tolerance : null),
            new RenderCountersJson(
                result.ShapesExamined, result.ShapesDrawn, result.VerticesEmitted,
                result.InstancesExamined, result.InstancesDrawn,
                result.PathsConstructed, result.DrawCalls, result.LayersVisited),
            pxW, pxH);
    }

    /// <summary>
    /// R-rnd2-6. <c>full</c> turns every level-of-detail tier off, so what is STORED is what is drawn;
    /// <c>screen</c> leaves every knob at 0, which is what the interactive canvas passes, so the tiers
    /// engage exactly as they would at this zoom; a budget sets the decimation tier and leaves the
    /// others at their measured defaults.
    ///
    /// <para><b><c>PathCache</c> is null on this path</b> — one-shot render, nothing to persist across
    /// frames — which is what every existing export already passes.</para>
    ///
    /// <para><b>The overlay is null and stays null.</b> That alone suppresses the ghost, the selection
    /// outlines, the handles and the marquee; the PCell pins, the EM mesh, the reference planes and the
    /// DRC markers all default to false and this verb never sets one. <b>Rulers are the exception and
    /// they stay ON</b>, because <c>ShowRulers</c>' own remarks say a ruler is document content rather
    /// than overlay state: it is in the <c>.clay</c> and an export that dropped it would contradict the
    /// layout design doc.</para>
    /// </summary>
    private static LayoutRenderOptions LayoutOptionsFor(Options o, ColorTheme theme, string baseDir)
    {
        var t = LayoutRenderTheme.FromTheme(theme, o.Variant);
        bool full = o.Detail == Detail.Full;

        return new LayoutRenderOptions
        {
            Theme                 = t,
            ShowGrid              = o.Grid,
            Overlay               = null,
            ShowRulers            = !o.NoRulers,
            TransparentBackground = o.Transparent,
            BaseDir               = baseDir,
            PathCache             = null,
            DetailPixelThreshold  = o.Detail switch
            {
                Detail.Full   => -1,
                Detail.Budget => o.DetailBudget,
                _             => 0,
            },
            // The other six tiers, each on its own documented "a NEGATIVE value disables the tier
            // outright" contract. DetailPixelThreshold < 0 already implies the outline and raster
            // tiers; the rest are set explicitly rather than relied upon, so `full` means what it says
            // even if one of those implications is ever narrowed.
            LodPixelThreshold            = full ? -1 : 0,
            MergeShapeCountThreshold     = full ? -1 : 0,
            OutlineVertexBudget          = full ? -1 : 0,
            InstanceRasterMaxDevicePixels = full ? -1 : 0,
            StrokeElisionPixelThreshold  = full ? -1 : 0,
            HairlineFillPixelThreshold   = full ? -1 : 0,
            CoarseCoverageThreshold      = full ? -1 : 0,
        };
    }

    /// <summary>
    /// What will actually be PAINTED, which is not the same as what is stored.
    ///
    /// <para>Three of the contributions are not <c>LayoutGeometry.BboxOf</c>, and each of them is a
    /// framing bug someone has already shipped: a <c>LabelShape</c>'s stored bbox is its ANCHOR (a
    /// point), an EM port additionally paints a width bar and an arrow at the conductor end, and an
    /// instance's extent has to be resolved through its cell. Framing on the stored boxes is exactly
    /// what cropped a pasted page's ports off the bottom — see
    /// <c>LayoutClipboard.ComputeSelectionBounds</c>, whose header records it.</para>
    ///
    /// <para><b>Rulers in exactly two passes</b>, for the reason that method also records: a Fixed-mode
    /// ruler's readout is n screen POINTS, so its world extent depends on the scale, which depends on
    /// these bounds. Pass one takes everything that does not depend on the scale; pass two measures the
    /// Fixed text at the scale pass one chose. The iteration is monotone, so a second pass cannot make
    /// it worse — do not iterate to a fixed point, and do not skip pass two.</para>
    /// </summary>
    private static Bbox LayoutExtents(LayoutView view, Technology? tech, string baseDir, double pxW, double pxH)
    {
        var bbox = Bbox.Empty;
        var conductorAt = LayoutPortDirection.LookupFor(view.Shapes);
        var visible = LayerVisibility(tech);

        foreach (var s in view.Shapes)
        {
            if (!visible(s.Layer)) continue;
            bbox = bbox.Union(LayoutGeometry.BboxOf(s));

            if (s is not LabelShape label) continue;
            if (LayoutRenderer.MeasureLabelWorldBbox(label, label.IsPort) is { } textBb)
                bbox = bbox.Union(textBb);
            if (LayoutPortDirection.Resolve(conductorAt, label) is { } hint)
            {
                long rad = Math.Max(hint.WidthDbu, label.Height);
                bbox = bbox.Union(new Bbox(hint.PlaneX - rad, hint.PlaneY - rad,
                                           hint.PlaneX + rad, hint.PlaneY + rad));
            }
        }

        foreach (var inst in view.Instances)
        {
            var ib = CellHierarchy.InstanceBbox(inst, baseDir, visible);
            if (!ib.IsEmpty) bbox = bbox.Union(ib);
        }

        foreach (var ruler in view.Rulers)
        {
            bbox = bbox.Union(new Bbox(Math.Min(ruler.X1, ruler.X2), Math.Min(ruler.Y1, ruler.Y2),
                                       Math.Max(ruler.X1, ruler.X2), Math.Max(ruler.Y1, ruler.Y2)));
            if (ruler.SizeMode == RulerSizeMode.Scaled)
                bbox = bbox.Union(LayoutRenderer.MeasureRulerWorldBbox(
                    ruler, view.DisplayUnit, view.DbuPerMicron, 0));
        }

        if (bbox.IsEmpty) return bbox;

        // A legitimately ONE-DIMENSIONAL document is not an empty one — a purely horizontal ruler, or a
        // single zero-height trace, has no extent on one axis and would otherwise be refused outright.
        bbox = InflateDegenerateAxes(bbox);

        if (view.Rulers.Any(r => r.SizeMode == RulerSizeMode.Fixed))
        {
            var pass1 = LayoutViewport.ZoomToFit(bbox, pxW, pxH, marginFrac: DefaultMargin);
            if (pass1.Zoom > 0)
                foreach (var ruler in view.Rulers)
                    if (ruler.SizeMode == RulerSizeMode.Fixed)
                        bbox = bbox.Union(LayoutRenderer.MeasureRulerWorldBbox(
                            ruler, view.DisplayUnit, view.DbuPerMicron, pass1.Zoom));
        }

        return bbox;
    }

    private static Bbox InflateDegenerateAxes(Bbox bb)
    {
        long w = bb.MaxX - bb.MinX, h = bb.MaxY - bb.MinY;
        if (w >= 1 && h >= 1) return bb;

        long span = Math.Max(Math.Max(w, h), 1);
        long padX = w >= 1 ? 0 : Math.Max(1, span / 8);
        long padY = h >= 1 ? 0 : Math.Max(1, span / 8);
        return new Bbox(bb.MinX - padX, bb.MinY - padY, bb.MaxX + padX, bb.MaxY + padY);
    }

    /// <summary>Which layers the page may be sized by — <c>LayerDef.Visible</c>, the same flag
    /// <c>LayoutRenderer.Draw</c> gates each layer on. Sizing a page from geometry nothing then paints
    /// produces a mostly-empty picture with the visible content too small to read.</summary>
    private static Func<LayerKey, bool> LayerVisibility(Technology? tech)
    {
        if (tech is null) return static _ => true;
        var map = new Dictionary<LayerKey, bool>();
        foreach (var l in tech.Layers) map[l.Key] = l.Visible;
        return key => !map.TryGetValue(key, out bool v) || v;
    }

    private static (LayoutViewport Vp, bool Letterboxed, int? Refusal) LayoutViewportFor(
        Options o, LayoutView view, Bbox extents, int pxW, int pxH)
    {
        switch (o.Mode)
        {
            case ViewportMode.Window:
            {
                var (rect, refusal) = ParseWorldRect(o.WindowText!, "--window", view);
                if (refusal is { } f) return (default, false, f);
                return Letterbox(rect!.Value, pxW, pxH);
            }
            case ViewportMode.Center:
            {
                var (centre, cRefusal) = ParseWorldPoint(o.CenterText!, "--center", view);
                if (cRefusal is { } cf) return (default, false, cf);
                var (span, sRefusal) = ParseWorldLength(o.SpanText!, "--span", view);
                if (sRefusal is { } sf) return (default, false, sf);
                double halfW = span / 2.0;
                double halfH = halfW * pxH / Math.Max(1, pxW);
                return Letterbox(new WorldRect(centre.X - halfW, centre.Y - halfH,
                                               centre.X + halfW, centre.Y + halfH), pxW, pxH);
            }
            default:
                return (LayoutViewport.ZoomToFit(extents, pxW, pxH, marginFrac: o.Margin), false, null);
        }

        static (LayoutViewport, bool, int?) Letterbox(WorldRect w, int pxW, int pxH)
        {
            // R-rnd2-5: the requested window is honoured EXACTLY. Where its aspect differs from the
            // output's, the extra is filled — never cropped and never stretched. A caller that asked
            // for a region and silently got less of it than it asked for has no way to notice.
            double zoom = Math.Min(pxW / w.W, pxH / w.H);
            double cx = (w.X0 + w.X1) / 2.0, cy = (w.Y0 + w.Y1) / 2.0;
            var vp = new LayoutViewport(cx - pxW / (2 * zoom), cy - pxH / (2 * zoom), zoom, pxW, pxH);
            bool letterboxed = Math.Abs(w.W / w.H - (double)pxW / pxH) > 1e-9;
            return (vp, letterboxed, null);
        }
    }

    /// <summary>
    /// R-rnd2-8: the selection is applied to a CLONE of the resolved technology, never to the cached
    /// one, and the clone is a reflective field-for-field copy rather than a hand-written one — a
    /// hand-written copy silently drops any field added to <c>LayerDef</c> afterwards, and the symptom
    /// would be a layer that renders differently only when <c>--layers</c> is passed.
    /// </summary>
    private static (Technology? Tech, IReadOnlyList<RenderLayerJson>? Report, int? Refusal)
        ApplyLayerSelection(Technology? tech, LayoutView view, Options o)
    {
        var counts = new Dictionary<LayerKey, int>();
        foreach (var s in view.Shapes)
            counts[s.Layer] = counts.TryGetValue(s.Layer, out int n) ? n + 1 : 1;

        string[]? named = o.OnlyLayers ?? o.HideLayers;
        if (named is null)
            return (tech, LayerReport(tech, counts, static l => l.Visible), null);

        if (tech is null)
            return (null, null, JsonRun.Fail(CliDiagnostics.RenderNoTechnologyForLayers()));

        var known = new Dictionary<string, LayerKey>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in tech.Layers)
        {
            known[l.Name] = l.Key;
            known[l.Key.ToString()] = l.Key;   // a caller that has only the numeric key from an import
        }

        var chosen = new HashSet<LayerKey>();
        foreach (string name in named)
        {
            // R-rnd2-7: not a silent skip. A misspelling that produced a picture without that layer is
            // indistinguishable from a layer that is genuinely empty.
            if (!known.TryGetValue(name, out var key))
                return (null, null, JsonRun.Fail(CliDiagnostics.RenderUnknownLayer(
                    name, Join([.. tech.Layers.Select(l => l.Name).Where(n => n.Length > 0).Distinct().Order(StringComparer.Ordinal)]))));
            chosen.Add(key);
        }

        bool only = o.OnlyLayers is not null;
        // R-rnd2-8's copy lives in src/Design beside Technology itself, not here: it is data
        // manipulation on the design model rather than a CLI concern, RND-3's `explain --layers` and
        // RND-4 both want it, and a second copy of it would be free to disagree about what was drawn.
        var clone = TechnologyLayerSelection.WithVisibility(
            tech, l => only ? chosen.Contains(l.Key) : l.Visible && !chosen.Contains(l.Key));
        return (clone, LayerReport(clone, counts, static l => l.Visible), null);
    }

    private static IReadOnlyList<RenderLayerJson>? LayerReport(
        Technology? tech, Dictionary<LayerKey, int> counts, Func<LayerDef, bool> rendered)
    {
        if (tech is null)
        {
            // No technology at all: every layer the document draws on is a fallback-palette layer, and
            // reporting an empty list would read as "this document uses none".
            return [.. counts.OrderBy(kv => kv.Key.ToString(), StringComparer.Ordinal)
                             .Select(kv => new RenderLayerJson(kv.Key.ToString(), true, kv.Value))];
        }
        return [.. tech.Layers.Select(l => new RenderLayerJson(
            l.Name.Length > 0 ? l.Name : l.Key.ToString(),
            rendered(l),
            counts.TryGetValue(l.Key, out int n) ? n : 0))];
    }

    // ── schematic ────────────────────────────────────────────────────────────

    private static int DrawSchematic(Options o, Target t)
    {
        if (o.OnlyLayers is not null || o.HideLayers is not null)
            return JsonRun.Fail(CliDiagnostics.RenderLayersNotApplicable(
                o.OnlyLayers is not null ? "--layers" : "--hide-layers", "schematic"));
        if (o.Detail != Detail.Full && o.DetailText != "full")
            return JsonRun.Fail(CliDiagnostics.RenderDetailNotApplicable("schematic"));

        SchematicEditModel model;
        try { (model, _, _) = SchematicPersistence.LoadFromFile(t.File); }
        catch (Exception ex) { return JsonRun.Fail(CliDiagnostics.RenderDocumentUnreadable(t.File, ex.Message)); }

        RunHost.Cancellation.ThrowIfCancellationRequested();
        RunHost.Control?.BeginStage("measure");
        Progress("measure");

        var (rm, idx) = model.BuildRenderModel();
        var extents = SchematicExtents(model, rm);
        if (extents is not { } bb) return JsonRun.Fail(CliDiagnostics.RenderNothingToDraw(t.File));

        Console.Error.WriteLine(
            $"[circuitRF] {model.Components.Count} component(s), {model.Wires.Count} wire(s)");

        int pxW = (int)Math.Round(o.Width  * o.Scale);
        int pxH = (int)Math.Round(o.Height * o.Scale);

        var (pan, zoom, letterboxed, refusal) = ScreenSenseViewport(o, bb, pxW, pxH, null);
        if (refusal is { } f) return f;

        var (theme, themeName, themeFrom, themeRefusal) = ResolveTheme(o, Path.GetFullPath(t.File));
        if (themeRefusal is { } tr) return tr;
        var rt = SchematicRenderTheme.FromTheme(theme, o.Variant);

        RunHost.Cancellation.ThrowIfCancellationRequested();
        RunHost.Control?.BeginStage("draw");
        Progress("draw");

        byte[] bytes = Emit(o, pxW, pxH, canvas =>
            SchematicRenderer.Draw(canvas, (pxW, pxH), rm, idx, pan.X, pan.Y, zoom, rt,
                overlay: null, useTransparentBackground: o.Transparent, excludeGrid: !o.Grid));

        return Publish(o, t, DocumentKind.Schematic, bytes,
            ScreenSenseViewportJson(o, pan, zoom, pxW, pxH, letterboxed),
            new RenderExtentsJson(bb.X0, bb.Y0, bb.X1, bb.Y1, DesignUnits, 1.0),
            new RenderThemeJson(themeName, o.Variant == ColorVariant.Dark ? "dark" : "light", themeFrom),
            null, null, null, pxW, pxH);
    }

    /// <summary>
    /// What the schematic renderer will paint. The render model's own bbox covers components and
    /// wires; bitmaps and net labels are unioned separately because neither contributes to it — a
    /// bitmap-only selection otherwise sizes to a dummy extent, and a long net label near the edge is
    /// clipped. <c>SchematicClipboard.BuildSelectionModel</c> is where both were learned.
    /// </summary>
    private static WorldRect? SchematicExtents(SchematicEditModel model, SchematicModel rm)
    {
        bool hasCompWire = model.Components.Count > 0 || model.Wires.Count > 0;
        double x0, y0, x1, y1;
        if (hasCompWire) { x0 = rm.BbMinX; y0 = rm.BbMinY; x1 = rm.BbMaxX; y1 = rm.BbMaxY; }
        else             { x0 = y0 = double.MaxValue; x1 = y1 = double.MinValue; }

        foreach (var bm in rm.Bitmaps)
        {
            x0 = Math.Min(x0, bm.X);              y0 = Math.Min(y0, bm.Y);
            x1 = Math.Max(x1, bm.X + bm.Width);   y1 = Math.Max(y1, bm.Y + bm.Height);
        }

        foreach (var nl in rm.NetLabels)
        {
            x0 = Math.Min(x0, nl.X);
            y0 = Math.Min(y0, nl.Y - 55.0);
            x1 = Math.Max(x1, nl.X + Math.Max(1, nl.Name.Length) * 40.0);
            y1 = Math.Max(y1, nl.Y + 20.0);
        }

        if (x0 == double.MaxValue) return null;
        if (x1 - x0 < 1 || y1 - y0 < 1) return null;
        return new WorldRect(x0, y0, x1, y1);
    }

    // ── symbol ───────────────────────────────────────────────────────────────

    private static int DrawSymbol(Options o, Target t)
    {
        if (o.OnlyLayers is not null || o.HideLayers is not null)
            return JsonRun.Fail(CliDiagnostics.RenderLayersNotApplicable(
                o.OnlyLayers is not null ? "--layers" : "--hide-layers", "symbol"));
        if (o.Detail != Detail.Full && o.DetailText != "full")
            return JsonRun.Fail(CliDiagnostics.RenderDetailNotApplicable("symbol"));

        Symbol symbol;
        try { symbol = SymbolPersistence.LoadFromFile(t.File); }
        catch (Exception ex) { return JsonRun.Fail(CliDiagnostics.RenderDocumentUnreadable(t.File, ex.Message)); }

        if (symbol.Primitives.Count == 0 && symbol.Pins.Count == 0)
            return JsonRun.Fail(CliDiagnostics.RenderNothingToDraw(t.File));

        RunHost.Cancellation.ThrowIfCancellationRequested();
        RunHost.Control?.BeginStage("measure");
        Progress("measure");

        int pxW = (int)Math.Round(o.Width  * o.Scale);
        int pxH = (int)Math.Round(o.Height * o.Scale);

        var bodyBb = SymbolBodyBox(symbol);
        if (bodyBb is not { } body) return JsonRun.Fail(CliDiagnostics.RenderNothingToDraw(t.File));

        // The SOLVED box — pin marks folded in. See SymbolPinMarksSolved for why this is an iteration
        // rather than a computation, and for the two errors that pass has already made once.
        var extents = SymbolPinMarksSolved(symbol, body, pxW, pxH, o.Margin);

        Console.Error.WriteLine(
            $"[circuitRF] {symbol.Primitives.Count} primitive(s), {symbol.Pins.Count} pin(s)");

        var (pan, zoom, letterboxed, refusal) = ScreenSenseViewport(o, extents, pxW, pxH, null);
        if (refusal is { } f) return f;

        var (theme, themeName, themeFrom, themeRefusal) = ResolveTheme(o, Path.GetFullPath(t.File));
        if (themeRefusal is { } tr) return tr;
        var rt = SchematicRenderTheme.FromTheme(theme, o.Variant);

        RunHost.Cancellation.ThrowIfCancellationRequested();
        RunHost.Control?.BeginStage("draw");
        Progress("draw");

        byte[] bytes = Emit(o, pxW, pxH, canvas =>
        {
            canvas.Clear(o.Transparent ? SKColors.Transparent : rt.Background);
            SchematicRenderer.DrawSymbol(
                canvas, symbol.Primitives, compX: 0, compY: 0,
                rotation: SymbolRotation.R0, mirrorX: false,
                panX: pan.X, panY: pan.Y, zoom: zoom, theme: rt);
            // The same dot-and-port-label pass the editor and the symbol clipboard export both use, so
            // a symbol rendered here and the same symbol opened afterwards look alike.
            SymbolEditorRenderer.DrawPinMarkersPlain(canvas, symbol.Pins, pan.X, pan.Y, zoom, rt);
        });

        return Publish(o, t, DocumentKind.Symbol, bytes,
            ScreenSenseViewportJson(o, pan, zoom, pxW, pxH, letterboxed),
            new RenderExtentsJson(extents.X0, extents.Y0, extents.X1, extents.Y1, DesignUnits, 1.0),
            new RenderThemeJson(themeName, o.Variant == ColorVariant.Dark ? "dark" : "light", themeFrom),
            null, null, null, pxW, pxH);
    }

    private static WorldRect? SymbolBodyBox(Symbol symbol)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        if (symbol.Primitives.Count > 0)
        {
            var (p0, q0, p1, q1) = SymbolGeometry.ComputeBb(symbol.Primitives);
            minX = p0; minY = q0; maxX = p1; maxY = q1;
        }
        foreach (var pin in symbol.Pins)
        {
            minX = Math.Min(minX, pin.LocalX); minY = Math.Min(minY, pin.LocalY);
            maxX = Math.Max(maxX, pin.LocalX); maxY = Math.Max(maxY, pin.LocalY);
        }
        if (minX == double.MaxValue) return null;
        if (maxX - minX < 1e-9) { minX -= 50; maxX += 50; }
        if (maxY - minY < 1e-9) { minY -= 50; maxY += 50; }
        return new WorldRect(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// The pin marks are drawn in PIXELS and cannot be put in a world-space box, so the fit is SOLVED
    /// rather than computed: each pass measures the marks at the current zoom, widens the box by what
    /// they need, and re-fits at the smaller zoom that results. It converges downward in two or three
    /// passes and is capped.
    ///
    /// <para><b>The two errors this pass has already made once</b>, both recorded on
    /// <c>ComponentPreviewRaster.RasterSymbol</c> and both available to make again here:</para>
    /// <list type="number">
    /// <item><b>Reserving room on the wrong side.</b> A name drawn on the LEFT with room reserved on
    /// the right is the off-centre error made twice — room where nothing is drawn AND none where
    /// something is. The side comes from the pin's own <c>NameAlign</c>, measured, never assumed.</item>
    /// <item><b>Reserving the widest label at the rightmost pin.</b> Wrong the moment the longest name
    /// is not the rightmost pin, which is the ordinary case — a THERMAL pad among pads called 1..8.
    /// Each pin is measured AT ITS OWN POSITION.</item>
    /// </list>
    /// </summary>
    private static WorldRect SymbolPinMarksSolved(
        Symbol symbol, WorldRect body, int pxW, int pxH, double margin)
    {
        double x0 = body.X0, y0 = body.Y0, x1 = body.X1, y1 = body.Y1;
        double zoom = FitZoom(x1 - x0, y1 - y0, pxW, pxH, margin);
        if (zoom <= 0 || symbol.Pins.Count == 0) return body;

        for (int pass = 0; pass < 3; pass++)
        {
            float fontSize = (float)Math.Max(8.0, zoom * 12.0);
            float dot      = (float)Math.Max(3.0, zoom * 5.0);
            using var font = new SKFont(SkiaFonts.PlexBold, fontSize);

            double a0 = x0, b0 = y0, a1 = x1, b1 = y1;
            foreach (var pin in symbol.Pins)
            {
                string label = pin.Name is { Length: > 0 } n ? n : $"P{pin.PortIndex + 1}";
                double text = font.MeasureText(label);
                var (left, right, up, down) = pin.NameAlign switch
                {
                    SymbolPinNameAlign.Right  => (dot + 2 + text, (double)dot, (double)dot, (double)(fontSize * 0.85)),
                    SymbolPinNameAlign.Center => (text / 2, text / 2, (double)dot, (double)(fontSize * 0.85)),
                    SymbolPinNameAlign.Top    => ((double)(fontSize * 0.85), (double)(fontSize * 0.85), (double)dot, dot + 2 + text),
                    SymbolPinNameAlign.Bottom => ((double)(fontSize * 0.85), (double)(fontSize * 0.85), dot + 2 + text, (double)dot),
                    _                         => ((double)dot, dot + 2 + text, (double)dot, (double)(fontSize * 0.85)),
                };
                a0 = Math.Min(a0, pin.LocalX - left  / zoom);
                b0 = Math.Min(b0, pin.LocalY - up    / zoom);
                a1 = Math.Max(a1, pin.LocalX + right / zoom);
                b1 = Math.Max(b1, pin.LocalY + down  / zoom);
            }

            double next = FitZoom(a1 - a0, b1 - b0, pxW, pxH, margin);
            x0 = a0; y0 = b0; x1 = a1; y1 = b1;
            if (next <= 0) break;
            if (Math.Abs(next - zoom) / zoom < 0.01) break;
            zoom = next;
        }
        return new WorldRect(x0, y0, x1, y1);
    }

    private static double FitZoom(double w, double h, int pxW, int pxH, double margin)
        => Math.Min(pxW / Math.Max(w, 1e-9), pxH / Math.Max(h, 1e-9)) * (1.0 - 2.0 * margin);

    // ── the schematic/symbol viewport, which is Y-DOWN ───────────────────────

    /// <summary>The spelling the schematic and symbol halves report as their coordinate space
    /// (R-rnd2-4): dimensionless design units, reported as what they are rather than dressed up in
    /// metres.</summary>
    private const string DesignUnits = "design-units";

    /// <summary>
    /// The schematic and symbol canvases are SCREEN-SENSE (+y down), unlike the layout's physical
    /// Y-up, so their viewport is a pan/zoom pair rather than a <c>LayoutViewport</c>. The letterbox
    /// rule is the same one (R-rnd2-5): the requested window is honoured exactly and the extra is
    /// filled.
    /// </summary>
    private static ((double X, double Y) Pan, double Zoom, bool Letterboxed, int? Refusal)
        ScreenSenseViewport(Options o, WorldRect extents, int pxW, int pxH, LayoutView? _)
    {
        switch (o.Mode)
        {
            case ViewportMode.Window:
            {
                var (rect, refusal) = ParseWorldRect(o.WindowText!, "--window", null);
                if (refusal is { } f) return (default, 0, false, f);
                return Box(rect!.Value);
            }
            case ViewportMode.Center:
            {
                var (centre, cRefusal) = ParseWorldPoint(o.CenterText!, "--center", null);
                if (cRefusal is { } cf) return (default, 0, false, cf);
                var (span, sRefusal) = ParseWorldLength(o.SpanText!, "--span", null);
                if (sRefusal is { } sf) return (default, 0, false, sf);
                double halfW = span / 2.0;
                double halfH = halfW * pxH / Math.Max(1, pxW);
                return Box(new WorldRect(centre.X - halfW, centre.Y - halfH,
                                         centre.X + halfW, centre.Y + halfH));
            }
            default:
            {
                double zoom = FitZoom(extents.W, extents.H, pxW, pxH, o.Margin);
                double panX = (extents.X0 + extents.X1) * 0.5 - pxW / (2.0 * zoom);
                double panY = (extents.Y0 + extents.Y1) * 0.5 - pxH / (2.0 * zoom);
                return ((panX, panY), zoom, false, null);
            }
        }

        ((double, double), double, bool, int?) Box(WorldRect w)
        {
            double zoom = Math.Min(pxW / w.W, pxH / w.H);
            double cx = (w.X0 + w.X1) / 2.0, cy = (w.Y0 + w.Y1) / 2.0;
            bool letterboxed = Math.Abs(w.W / w.H - (double)pxW / pxH) > 1e-9;
            return ((cx - pxW / (2 * zoom), cy - pxH / (2 * zoom)), zoom, letterboxed, null);
        }
    }

    private static RenderViewportJson ScreenSenseViewportJson(
        Options o, (double X, double Y) pan, double zoom, int pxW, int pxH, bool letterboxed)
        => new(ModeName(o.Mode),
               pan.X, pan.Y, pan.X + pxW / zoom, pan.Y + pxH / zoom,
               DesignUnits, 1.0, zoom, letterboxed);

    private static string ModeName(ViewportMode m) => m switch
    {
        ViewportMode.Window => "window",
        ViewportMode.Center => "center",
        _                   => "fit",
    };

    // ── coordinates, and the unit rule ───────────────────────────────────────

    /// <summary>
    /// R-rnd2-4 / R-rnd0-5. <b>On a layout, every coordinate carries an SI unit and a bare number is a
    /// refusal.</b> <c>--window 0,0,500,300</c> could mean DBU, micrometres or millimetres; those are
    /// three pictures six orders of magnitude apart and all three are plausible, and the picture that
    /// comes back from the wrong one is a plausible picture of the wrong thing. This is
    /// <c>sweep-unit-scale-and-mark</c>'s failure class exactly.
    ///
    /// <para>A schematic or a symbol takes bare numbers, because its coordinates ARE dimensionless
    /// design units — a unit suffix there would be the invention.</para>
    /// </summary>
    private static (double Value, int? Refusal) ParseCoordinate(string text, string option, LayoutView? view)
    {
        if (view is null)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                ? (v, null)
                : (0, JsonRun.Fail(CliDiagnostics.RenderCoordinateMalformed(option, text)));
        }

        string trimmed = text.Trim();
        bool hasUnit = trimmed.Length > 0 && char.IsLetter(trimmed[^1]);
        if (!hasUnit)
            return (0, JsonRun.Fail(CliDiagnostics.RenderCoordinateNeedsUnit(
                option, text, SuggestUnits(trimmed, view))));

        return LayoutUnits.TryParse(trimmed, LayoutUnit.Um, view.DbuPerMicron, out long dbu)
            ? (dbu, null)
            : (0, JsonRun.Fail(CliDiagnostics.RenderCoordinateMalformed(option, text)));
    }

    /// <summary>What the refusal offers instead — the same number spelled in the two units a caller
    /// most plausibly meant, plus the document's own display unit when it is neither.</summary>
    private static string SuggestUnits(string bare, LayoutView view)
    {
        var units = new List<LayoutUnit> { LayoutUnit.Um, LayoutUnit.Mm };
        if (!units.Contains(view.DisplayUnit)) units.Add(view.DisplayUnit);
        return string.Join(" or ", units.Select(u => $"'{bare}{Suffix(u)}'"));

        static string Suffix(LayoutUnit u) => u switch
        {
            LayoutUnit.Nm => "nm", LayoutUnit.Um => "um", LayoutUnit.Mm => "mm",
            LayoutUnit.Mil => "mil", _ => "in",
        };
    }

    private static (WorldRect? Rect, int? Refusal) ParseWorldRect(string text, string option, LayoutView? view)
    {
        var parts = text.Split(',');
        if (parts.Length != 4) return (null, JsonRun.Fail(CliDiagnostics.RenderWindowMalformed(text)));

        var v = new double[4];
        for (int i = 0; i < 4; i++)
        {
            var (value, refusal) = ParseCoordinate(parts[i], option, view);
            if (refusal is { } f) return (null, f);
            v[i] = value;
        }

        var rect = new WorldRect(Math.Min(v[0], v[2]), Math.Min(v[1], v[3]),
                                 Math.Max(v[0], v[2]), Math.Max(v[1], v[3]));
        if (!(rect.W > 0) || !(rect.H > 0))
            return (null, JsonRun.Fail(CliDiagnostics.RenderWindowEmpty(text)));
        return (rect, null);
    }

    private static ((double X, double Y) Point, int? Refusal) ParseWorldPoint(
        string text, string option, LayoutView? view)
    {
        var parts = text.Split(',');
        if (parts.Length != 2) return (default, JsonRun.Fail(CliDiagnostics.RenderCenterMalformed(text)));

        var (x, xr) = ParseCoordinate(parts[0], option, view);
        if (xr is { } xf) return (default, xf);
        var (y, yr) = ParseCoordinate(parts[1], option, view);
        if (yr is { } yf) return (default, yf);
        return ((x, y), null);
    }

    private static (double Value, int? Refusal) ParseWorldLength(string text, string option, LayoutView? view)
    {
        var (v, refusal) = ParseCoordinate(text, option, view);
        if (refusal is { } f) return (0, f);
        return v > 0 ? (v, null) : (0, JsonRun.Fail(CliDiagnostics.RenderCoordinateMalformed(option, text)));
    }

    private static double MetresPerUnit(LayoutUnit u) => u switch
    {
        LayoutUnit.Nm   => 1e-9,
        LayoutUnit.Um   => 1e-6,
        LayoutUnit.Mm   => 1e-3,
        LayoutUnit.Mil  => 2.54e-5,
        _               => 2.54e-2,
    };

    // ── the theme ────────────────────────────────────────────────────────────

    /// <summary>
    /// R-rnd2-9: <c>ThemeResolver</c>'s existing chain and nothing new — an explicit file first, then,
    /// for a name, workspace directory, user themes directory, shipped <c>.ccolor</c>. With no
    /// <c>--theme</c>, the workspace's own recorded theme; with no workspace, the shipped default.
    ///
    /// <para><b>Resolvability is decided HERE, before the chain runs, and that is not a duplicate of
    /// it.</b> <c>ThemeResolver.Resolve</c> cannot fail: its last step is <c>ColorTheme.BuiltIn</c>,
    /// which always succeeds. So a misspelled name would resolve to a DIFFERENT PICTURE and be reported
    /// as a success — the same silent fallback R-rnd1-5 removed from the resolver itself. What this
    /// adds is the answer to "did a step actually match", which the resolver does not return.</para>
    /// </summary>
    private static (ColorTheme Theme, string Name, string From, int? Refusal) ResolveTheme(
        Options o, string documentPath)
    {
        string? cws = DocumentKinds.AncestorCws(documentPath);
        string? workspaceDir = cws is null ? null : Path.GetDirectoryName(Path.GetFullPath(cws));

        if (o.Theme is { } asked)
        {
            // A path is taken as a path — the ONE step of the chain that is not a name lookup.
            if (asked.EndsWith(".ccolor", StringComparison.OrdinalIgnoreCase) || File.Exists(asked))
            {
                if (!File.Exists(asked))
                    return (ColorTheme.BuiltIn, asked, "file",
                            JsonRun.Fail(CliDiagnostics.RenderThemeUnresolved(asked, $"'{asked}'")));
                try { return (ColorThemeIo.LoadFile(asked), Path.GetFileNameWithoutExtension(asked), "file", null); }
                catch (Exception ex)
                {
                    return (ColorTheme.BuiltIn, asked, "file",
                            JsonRun.Fail(CliDiagnostics.RenderThemeFileUnreadable(asked, ex.Message)));
                }
            }

            string? from = WhereItResolves(asked, workspaceDir);
            if (from is null)
                return (ColorTheme.BuiltIn, asked, "none", JsonRun.Fail(CliDiagnostics.RenderThemeUnresolved(
                    asked, LookedIn(workspaceDir))));
            return (ThemeResolver.Resolve(asked, workspaceDir), asked, from, null);
        }

        string name = ThemeResolver.DefaultThemeName;
        if (cws is not null)
        {
            try
            {
                if (WorkspacePersistence.LoadFromFile(cws).ColorSchemeName is { Length: > 0 } recorded)
                    name = recorded;
            }
            catch { /* a .cws that will not parse is `check`'s finding, not a reason to refuse a picture */ }
        }

        // A recorded name that no longer resolves is the workspace's own state, not the caller's
        // mistake, so it falls through the chain exactly as the application's does and says where it
        // landed rather than refusing.
        return (ThemeResolver.Resolve(name, workspaceDir), name,
                WhereItResolves(name, workspaceDir) ?? "built-in", null);
    }

    private static string? WhereItResolves(string name, string? workspaceDir)
    {
        if (workspaceDir is not null && File.Exists(Path.Combine(workspaceDir, name + ".ccolor")))
            return "workspace";
        if (File.Exists(Path.Combine(ThemeResolver.UserThemesDir, name + ".ccolor")))
            return "user";
        foreach (string builtIn in ThemeResolver.BuiltInThemeNames)
            if (string.Equals(builtIn, name, StringComparison.OrdinalIgnoreCase)) return "shipped";
        return null;
    }

    private static string LookedIn(string? workspaceDir)
    {
        var places = new List<string>();
        if (workspaceDir is not null) places.Add($"the workspace ({workspaceDir})");
        places.Add($"the user themes folder ({ThemeResolver.UserThemesDir})");
        places.Add($"the shipped themes ({string.Join(", ", ThemeResolver.BuiltInThemeNames)})");
        return string.Join(", ", places);
    }

    // ── encoding, writing and reporting ──────────────────────────────────────

    /// <summary>
    /// R-rnd2-2's three formats, each drawn ONCE by the caller's own delegate onto whichever surface
    /// the format needs — so there is one drawing path and three encoders rather than three renders.
    ///
    /// <para><b>The bytes are complete before anything reaches the filesystem.</b> That is what makes
    /// gate 7 true by construction: a cancelled run leaves no output file, because the file is not
    /// opened until the picture is finished.</para>
    /// </summary>
    private static byte[] Emit(Options o, int pxW, int pxH, Action<SKCanvas> draw)
    {
        switch (o.Format)
        {
            case Format.Pdf:
            {
                var metadata = new SKDocumentPdfMetadata { Creator = "circuitRF" };
                using var stream = new SKDynamicMemoryWStream();
                using (var doc = SKDocument.CreatePdf(stream, metadata))
                {
                    var canvas = doc.BeginPage(pxW, pxH);
                    draw(canvas);
                    doc.EndPage();
                    doc.Close();
                }
                return stream.DetachAsData().ToArray();
            }

            case Format.Png:
            {
                using var bitmap = new SKBitmap(pxW, pxH, SKColorType.Rgba8888, SKAlphaType.Premul);
                // LayoutRenderer never Clears (see its header comment) — the destination must arrive
                // already zero-initialised, which is what makes --background transparent mean anything.
                bitmap.Erase(SKColors.Transparent);
                using (var canvas = new SKCanvas(bitmap)) draw(canvas);
                using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                return data?.ToArray() ?? [];
            }

            default:
            {
                using var stream = new SKDynamicMemoryWStream();
                using (var canvas = SKSvgCanvas.Create(new SKRect(0, 0, pxW, pxH), stream))
                    draw(canvas);
                // Skia writes each text run's per-glyph x/y list with a trailing separator, which
                // Firefox reads as invalid and drops — putting every run a line above its baseline,
                // where the clip eats it. The same repair every clipboard export applies, from the
                // same function, which is why it had to come below the firewall with RND-2.
                return Encoding.UTF8.GetBytes(
                    SvgFontNormalizer.RepairPositionLists(
                        Encoding.UTF8.GetString(stream.DetachAsData().ToArray())));
            }
        }
    }

    private static int Publish(
        Options o, Target t, DocumentKind kind, byte[] bytes,
        RenderViewportJson viewport, RenderExtentsJson extents, RenderThemeJson theme,
        IReadOnlyList<RenderLayerJson>? layers, RenderDetailJson? detail, RenderCountersJson? counters,
        int pxW, int pxH)
    {
        RunHost.Cancellation.ThrowIfCancellationRequested();
        RunHost.Control?.BeginStage("encode");
        Progress("encode");

        if (bytes.Length == 0)
            return JsonRun.Fail(CliDiagnostics.RenderWriteFailed(o.Output!, "the encoder produced no bytes"));

        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(o.Output!));
            if (dir is { Length: > 0 }) Directory.CreateDirectory(dir);
            File.WriteAllBytes(o.Output!, bytes);
        }
        catch (Exception ex) { return JsonRun.Fail(CliDiagnostics.RenderWriteFailed(o.Output!, ex.Message)); }

        string format = o.Format switch { Format.Pdf => "pdf", Format.Png => "png", _ => "svg" };
        string unitKind = o.Format == Format.Png ? "device-pixels" : "points";

        JsonRun.AddOutput(format, o.Output!);
        JsonRun.Render = new RenderReportJson(
            t.File, DocumentKinds.Name(kind), t.ViewName, format,
            viewport, extents,
            new RenderSizeJson(pxW, pxH, unitKind, o.Scale),
            theme, layers, detail, counters, bytes.Length);

        // §3.1: the file written IS the result, so it goes to stdout — the same rule the authoring
        // verbs follow, and for the same reason: a caller's next step is to read or ship that file.
        Console.WriteLine($"Wrote {o.Output} ({pxW}x{pxH} {unitKind}, {bytes.Length:N0} bytes)");
        if (counters is { } c)
            Console.WriteLine(
                $"  {c.ShapesDrawn:N0} of {c.ShapesExamined:N0} shape(s) drawn, " +
                $"{c.VerticesEmitted:N0} vertices emitted, {c.DrawCalls:N0} draw call(s)");
        return 0;
    }

    private static void Note(Diagnostic d)
    {
        Console.Error.WriteLine("note: " + d.Render());
        JsonRun.Note(d);
    }
}
