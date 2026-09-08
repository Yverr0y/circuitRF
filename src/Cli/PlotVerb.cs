using System.Globalization;
using System.Text.Json;
using CircuitRF.Render.DataDisplay;
using RfCore;
using RfCore.Data;

namespace CircuitRF.Cli;

/// <summary>
/// <c>circuitrf plot &lt;result&gt; -o out.{svg,pdf,png} --trace cube=…</c> — one picture out of one
/// result file, with no <c>.cdd</c> to hand-author first (brief-automation-11-missing-verbs.md
/// R-aut11-2).
///
/// <para><b>Why it exists.</b> Hand-authoring a data display to draw a single trace was the largest
/// piece of incidental work in an otherwise short task: a document with a tab, a plot container, a
/// placement, a source reference and a slice, every field of which has to be right before anything
/// appears. This verb takes the four things a caller actually has — a result, a cube, a matrix entry
/// and a format — and writes the picture.</para>
///
/// <para><b>It is the convenience over `.cdd` authoring, not a replacement for it.</b> Everything a
/// display can express stays reachable by writing one and calling <c>render</c>; what is here is one
/// plot with one axis pair.</para>
///
/// <para><b>There is ONE plotting path.</b> This file builds a <see cref="DataDisplayConfig"/> — the
/// document a `.cdd` deserializes to — and hands it to <see cref="RenderDataDisplay.Draw(string,
/// DataDisplayConfig, Request, string?)"/>, which is the same function <c>render</c>'s `.cdd` half
/// calls. So a plot drawn here is byte-identical to the same plot drawn from the equivalent
/// hand-authored display, and <c>--write-cdd</c> hands over that document so a caller can check it,
/// edit it, and go on with the full surface. A second composer here would have drifted invisibly: a
/// picture that is PLAUSIBLE is indistinguishable from one that is RIGHT.</para>
///
/// <para><b>The cube spec is the trace card's own.</b> <c>cube=</c> is parsed by
/// <see cref="CubeTraceSpecParser"/>, which is what the spec box on a trace card parses — so
/// <c>S[:,1,0]</c>, <c>Pout</c> and <c>mag(V[:,"X1.drain"])</c> mean here exactly what they mean
/// there, and an unparseable one is refused in the parser's own words. <c>i=</c>/<c>j=</c> are the
/// convenience over it: they pin the cube's axes NAMED <c>i</c> and <c>j</c> by PORT NUMBER, which is
/// what a caller holding an S-matrix has, and they are refused with a spec that already carries
/// brackets rather than silently losing to it.</para>
/// </summary>
internal static class PlotVerb
{
    /// <summary>One <c>--trace</c>, after parsing.</summary>
    private sealed class TraceSpec
    {
        public string  Text  = "";     // the cube spec as typed, before i/j and y are folded in
        public int?    I;
        public int?    J;
        public string? YText;
        public bool    Secondary;
        public string  Raw = "";       // the whole --trace argument, for refusals
    }

    private sealed class Options
    {
        public string?         Result;
        public string?         Output;
        public string?         Format;
        public List<TraceSpec> Traces = new();
        public PlotType        Type = PlotType.Rect;
        public FreqUnit        FreqUnit = FreqUnit.GHz;
        public string?         Title;
        public string?         XLabel, YLabel, Y2Label;
        public (double Lo, double Hi)? X, Y, Y2;
        public int?            Width, Height;
        public double          Scale = 1.0;
        public string?         ScaleOption;
        public bool?           Transparent;
        public bool            Dark;
        public string?         WriteCdd;
    }

    /// <summary>
    /// The plot box, in the canvas's logical units. Not a page size — <c>PlotComposer</c> normalizes
    /// by the bounding box of the placed plots, so what these decide is the drawn ASPECT. They are the
    /// application's own defaults (<c>DataDisplayViewModel.DefaultSquareSize</c> /
    /// <c>DefaultPlotWidth</c>, and <c>AppSettings.RectAspectRatio</c>'s golden ratio), so a plot this
    /// verb writes has the shape one added in the window has.
    /// </summary>
    private const double SquareSize = 420;
    private const double RectWidth  = 520;

    public static int Run(string[] args)
    {
        var o = new Options();
        if (Parse(args, o) is { } bad) return bad;

        if (o.Result is null) { JsonRun.Report(CliDiagnostics.PlotResultRequired()); return Usage(); }
        if (o.Output is null) { JsonRun.Report(CliDiagnostics.PlotOutputRequired()); return Usage(); }
        JsonRun.InputPath = o.Result;

        if (!File.Exists(o.Result)) return JsonRun.Fail(CliDiagnostics.PlotResultNotFound(o.Result));

        string format = o.Format ?? Path.GetExtension(o.Output).ToLowerInvariant() switch
        {
            ".svg" => "svg", ".pdf" => "pdf", ".png" => "png", _ => "",
        };
        if (format.Length == 0)
            return JsonRun.Fail(CliDiagnostics.PlotUnknownOutputFormat(
                o.Output, Path.GetExtension(o.Output) is { Length: > 0 } e ? e : "(none)"));

        if (o.ScaleOption is not null && format != "png")
            return JsonRun.Fail(CliDiagnostics.PlotScaleOnVector(o.ScaleOption, format));

        if (o.Traces.Count == 0)
        {
            // R-rnd4-4's rule, at the one place this verb can break it: an empty plot is a valid
            // picture that exports cleanly and looks exactly like a measurement that came back
            // empty. A plot with no trace is that picture, and it is refused rather than drawn.
            JsonRun.Report(CliDiagnostics.PlotTraceRequired());
            return Usage();
        }

        // A Smith or a Polar chart's window is the complex plane and is framed on the unit circle;
        // there is no X and no Y for a range to be a range of. Refused rather than dropped, for
        // `render`'s reason: a flag that did nothing leaves a caller with a picture it cannot tell
        // from the one it asked for.
        if (o.Type is PlotType.Smith or PlotType.Polar
            && (o.X is not null || o.Y is not null || o.Y2 is not null))
            return JsonRun.Fail(CliDiagnostics.PlotWindowOnComplex(o.Type.ToString().ToLowerInvariant()));

        // ── the data, read once, before anything is authored ─────────────────
        //
        // A spec has to be checked against the cubes that are actually in the file: "no cube 'Pout'"
        // is an answer a caller can act on, where a picture with no curve in it is not.
        var (data, _, error) = CddSources.LoadResult(o.Result);
        if (error is not null) return JsonRun.Fail(CliDiagnostics.PlotResultUnreadable(o.Result, error));
        if (data is null || data.Groups.Count == 0)
            return JsonRun.Fail(CliDiagnostics.PlotResultUnreadable(o.Result, "it holds no cubes"));

        var traces = new List<TraceConfig>(o.Traces.Count);
        for (int i = 0; i < o.Traces.Count; i++)
        {
            var (tc, refusal) = BuildTrace(o.Traces[i], data, Path.GetFileName(o.Result), i);
            if (refusal is { } r) return r;
            traces.Add(tc!);
        }

        var config = BuildConfig(o, traces);

        if (o.WriteCdd is { } cddPath)
        {
            // The document, as a document. It is exactly what was rendered — serialized from the
            // same object — so a caller can open it, edit it and carry on with the full surface.
            try
            {
                string? dir = Path.GetDirectoryName(Path.GetFullPath(cddPath));
                if (dir is { Length: > 0 }) Directory.CreateDirectory(dir);
                File.WriteAllText(cddPath,
                    JsonSerializer.Serialize(config, DataDisplayJson.Options),
                    new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex) { return JsonRun.Fail(CliDiagnostics.PlotWriteFailed(cddPath, ex.Message)); }

            JsonRun.AddOutput("cdd", cddPath);
            Console.Error.WriteLine($"[circuitRF] wrote {cddPath}");
        }

        // The result file is BOTH the document's directory (so its own bare-name reference resolves
        // beside it) and the bound --data, which is how `render --data` binds the same file.
        var request = new RenderDataDisplay.Request(
            o.Output, format, [o.Result], Tab: null, PlotIndex: null, AllTabs: false,
            o.Width, o.Height, o.Scale, o.Transparent, o.Dark);

        return RenderDataDisplay.Draw(o.Result, config, request, reportKind: "result");
    }

    private static int Usage()
    {
        Console.Error.WriteLine(
            "Usage: circuitrf plot <result.npy|.sNp> -o <out.svg|.pdf|.png> --trace <spec> [--trace <spec>]...\n" +
            "                     [--type rect|smith|polar] [--freq-unit Hz|kHz|MHz|GHz]\n" +
            "                     [--title T] [--xlabel T] [--ylabel T] [--y2label T]\n" +
            "                     [--x lo:hi] [--y lo:hi] [--y2 lo:hi]\n" +
            "                     [--size WxH] [--scale n | --dpi n] [--variant light|dark]\n" +
            "                     [--background opaque|transparent] [--write-cdd out.cdd]\n" +
            "  a trace spec is comma-separated key=value: cube=S i=2 j=1 y=db axis=left|right\n" +
            "  cube= takes the trace card's own shorthand — S[:,1,0], Pout, mag(V[:,\"X1.drain\"])");
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
                        case "svg": o.Format = "svg"; break;
                        case "pdf": o.Format = "pdf"; break;
                        case "png": o.Format = "png"; break;
                        default: return JsonRun.Fail(CliDiagnostics.PlotUnknownFormatName(args[i]));
                    }
                    continue;

                case "--trace" when i + 1 < args.Length:
                {
                    var (spec, refusal) = ParseTrace(args[++i]);
                    if (refusal is { } r) return r;
                    o.Traces.Add(spec!);
                    continue;
                }

                case "--type" when i + 1 < args.Length:
                    switch (args[++i].ToLowerInvariant())
                    {
                        case "rect":  o.Type = PlotType.Rect;  break;
                        case "smith": o.Type = PlotType.Smith; break;
                        case "polar": o.Type = PlotType.Polar; break;
                        case "table": o.Type = PlotType.Table; break;
                        default: return JsonRun.Fail(CliDiagnostics.PlotUnknownType(args[i]));
                    }
                    continue;

                case "--freq-unit" when i + 1 < args.Length:
                    switch (args[++i].ToLowerInvariant())
                    {
                        case "hz":  o.FreqUnit = FreqUnit.Hz;  break;
                        case "khz": o.FreqUnit = FreqUnit.kHz; break;
                        case "mhz": o.FreqUnit = FreqUnit.MHz; break;
                        case "ghz": o.FreqUnit = FreqUnit.GHz; break;
                        default: return JsonRun.Fail(CliDiagnostics.PlotUnknownFreqUnit(args[i]));
                    }
                    continue;

                case "--title"   when i + 1 < args.Length: o.Title   = args[++i]; continue;
                case "--xlabel"  when i + 1 < args.Length: o.XLabel  = args[++i]; continue;
                case "--ylabel"  when i + 1 < args.Length: o.YLabel  = args[++i]; continue;
                case "--y2label" when i + 1 < args.Length: o.Y2Label = args[++i]; continue;

                case "--x"  when i + 1 < args.Length:
                    if (ParseRange("--x", args[++i]) is not { } xr) return 1;
                    o.X = xr; continue;
                case "--y"  when i + 1 < args.Length:
                    if (ParseRange("--y", args[++i]) is not { } yr) return 1;
                    o.Y = yr; continue;
                case "--y2" when i + 1 < args.Length:
                    if (ParseRange("--y2", args[++i]) is not { } y2r) return 1;
                    o.Y2 = y2r; continue;

                case "--size" when i + 1 < args.Length:
                {
                    var parts = args[++i].Split('x', 'X');
                    if (parts.Length != 2
                        || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
                        || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
                        || w < 8 || h < 8 || w > 20000 || h > 20000)
                        return JsonRun.Fail(CliDiagnostics.PlotSizeMalformed(args[i]));
                    o.Width = w; o.Height = h;
                    continue;
                }

                case "--scale" when i + 1 < args.Length:
                {
                    if (o.ScaleOption is not null) return JsonRun.Fail(CliDiagnostics.PlotScaleAndDpi());
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double s)
                        || !(s > 0) || s > 16)
                        return JsonRun.Fail(CliDiagnostics.PlotScaleMalformed("--scale", args[i]));
                    o.Scale = s; o.ScaleOption = "--scale";
                    continue;
                }
                case "--dpi" when i + 1 < args.Length:
                {
                    if (o.ScaleOption is not null) return JsonRun.Fail(CliDiagnostics.PlotScaleAndDpi());
                    if (!double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                        || !(d > 0) || d > 1536)
                        return JsonRun.Fail(CliDiagnostics.PlotScaleMalformed("--dpi", args[i]));
                    o.Scale = d / 96.0; o.ScaleOption = "--dpi";
                    continue;
                }

                case "--variant" when i + 1 < args.Length:
                    switch (args[++i].ToLowerInvariant())
                    {
                        case "light": o.Dark = false; break;
                        case "dark":  o.Dark = true;  break;
                        default: return JsonRun.Fail(CliDiagnostics.PlotUnknownVariant(args[i]));
                    }
                    continue;
                case "--background" when i + 1 < args.Length:
                    switch (args[++i].ToLowerInvariant())
                    {
                        case "opaque":      o.Transparent = false; break;
                        case "transparent": o.Transparent = true;  break;
                        default: return JsonRun.Fail(CliDiagnostics.PlotUnknownBackground(args[i]));
                    }
                    continue;

                case "--write-cdd" when i + 1 < args.Length: o.WriteCdd = args[++i]; continue;

                default:
                    if (a.StartsWith('-'))
                    { JsonRun.Report(CliDiagnostics.PlotUnknownOption(a)); return Usage(); }
                    if (o.Result is not null)
                    { JsonRun.Report(CliDiagnostics.PlotMultipleResults()); return Usage(); }
                    o.Result = a;
                    continue;
            }
        }
        return null;
    }

    private static (double Lo, double Hi)? ParseRange(string option, string text)
    {
        int colon = text.LastIndexOf(':');
        if (colon > 0
            && double.TryParse(text[..colon], NumberStyles.Float, CultureInfo.InvariantCulture, out double lo)
            && double.TryParse(text[(colon + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double hi)
            && hi > lo)
            return (lo, hi);

        JsonRun.Fail(CliDiagnostics.PlotRangeMalformed(option, text));
        return null;
    }

    /// <summary>
    /// One <c>--trace</c>: comma-separated <c>key=value</c>, split on TOP-LEVEL commas only. The
    /// separator has to be bracket- and quote-aware because the cube shorthand it carries is full of
    /// commas — <c>cube=S[:,1,0],y=db</c> is two fields, not four.
    /// </summary>
    private static (TraceSpec? Spec, int? Refusal) ParseTrace(string raw)
    {
        var spec = new TraceSpec { Raw = raw };

        foreach (string field in SplitFields(raw))
        {
            int eq = field.IndexOf('=');
            if (eq <= 0) return (null, JsonRun.Fail(CliDiagnostics.PlotTraceFieldMalformed(raw, field)));

            string key   = field[..eq].Trim().ToLowerInvariant();
            string value = field[(eq + 1)..].Trim();

            switch (key)
            {
                case "cube": spec.Text  = value; break;
                case "y":    spec.YText = value; break;
                case "i" or "j":
                {
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 1)
                        return (null, JsonRun.Fail(CliDiagnostics.PlotTracePortMalformed(raw, key, value)));
                    if (key == "i") spec.I = n; else spec.J = n;
                    break;
                }
                case "axis":
                    switch (value.ToLowerInvariant())
                    {
                        case "left":  spec.Secondary = false; break;
                        case "right": spec.Secondary = true;  break;
                        default: return (null, JsonRun.Fail(CliDiagnostics.PlotTraceAxisUnknown(raw, value)));
                    }
                    break;
                default:
                    return (null, JsonRun.Fail(CliDiagnostics.PlotTraceUnknownKey(raw, key)));
            }
        }

        if (spec.Text.Length == 0)
            return (null, JsonRun.Fail(CliDiagnostics.PlotTraceCubeRequired(raw)));

        return (spec, null);
    }

    /// <summary>Top-level commas only — a comma inside <c>[…]</c> or <c>"…"</c> is part of a cube
    /// shorthand, not a field separator.</summary>
    private static List<string> SplitFields(string text)
    {
        var fields  = new List<string>();
        int start   = 0, depth = 0;
        bool quoted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"') quoted = !quoted;
            else if (!quoted && c == '[') depth++;
            else if (!quoted && c == ']') depth--;
            else if (!quoted && depth == 0 && c == ',')
            {
                if (i > start) fields.Add(text[start..i].Trim());
                start = i + 1;
            }
        }
        if (start < text.Length) fields.Add(text[start..].Trim());
        return fields;
    }

    // ── the document ─────────────────────────────────────────────────────────

    /// <summary>
    /// One <c>--trace</c> as a <see cref="TraceConfig"/>, with its slice resolved against the cubes
    /// the result actually holds.
    /// </summary>
    private static (TraceConfig? Trace, int? Refusal) BuildTrace(
        TraceSpec spec, DataSet data, string sourceRef, int index)
    {
        string text = spec.Text;

        // The cube NAME, checked first and in every branch. The parser's own answer for a bare name
        // it does not recognise is "Missing '['" — correct from where it stands, and useless to a
        // caller who simply mistyped a cube or is looking at the wrong run. What that caller needs
        // is the list of what the file holds, so this refusal is made here rather than forwarded.
        if (BareCubeName(text) is { Length: > 0 } bare && !data.Contains(bare))
            return (null, JsonRun.Fail(CliDiagnostics.PlotNoSuchCube(bare, CubeNames(data))));

        // i/j pin the axes NAMED i and j, by port number. They are the convenience over the
        // shorthand, so a spec that already carries a slice is a caller saying both things at once,
        // and it is refused rather than one of them being dropped.
        if (spec.I is not null || spec.J is not null)
        {
            if (text.Contains('['))
                return (null, JsonRun.Fail(CliDiagnostics.PlotTracePortsWithSlice(spec.Raw)));

            var cube = data[text];

            if (spec.I is not null && !cube.Axes.Any(a => a.Name.Equals("i", StringComparison.OrdinalIgnoreCase)))
                return (null, JsonRun.Fail(CliDiagnostics.PlotNoPortAxis(text, "i", AxisNames(cube))));
            if (spec.J is not null && !cube.Axes.Any(a => a.Name.Equals("j", StringComparison.OrdinalIgnoreCase)))
                return (null, JsonRun.Fail(CliDiagnostics.PlotNoPortAxis(text, "j", AxisNames(cube))));

            var tokens = new string[cube.Rank];
            for (int d = 0; d < cube.Rank; d++)
            {
                var  axis = cube.Axes[d];
                int? want = axis.Name.Equals("i", StringComparison.OrdinalIgnoreCase) ? spec.I
                            : axis.Name.Equals("j", StringComparison.OrdinalIgnoreCase) ? spec.J
                            : null;
                // The PORT NUMBER, verbatim: the shorthand's own integer token on an `i`/`j` axis is
                // 1-based (S[:,2,1] is S21, which is what makes that spelling readable), and the
                // range check that goes with it is the parser's. Emitting a 0-based index here would
                // have been the same number shifted by one — the quietest possible wrong answer,
                // since S12 and S21 are both legal.
                tokens[d] = want is null ? ":" : want.Value.ToString(CultureInfo.InvariantCulture);
            }

            text = $"{text}[{string.Join(", ", tokens)}]";
        }

        // `y=` becomes the transform PREFIX on the spec, in the function-call form the trace card's
        // own picker emits — so there is one table of transform names and it is the parser's.
        if (spec.YText is { } y)
        {
            string t = y.Equals("imaginary", StringComparison.OrdinalIgnoreCase) ? "imag" : y.ToLowerInvariant();
            text = $"{t}({text})";
        }

        if (!CubeTraceSpecParser.TryParse(text, data, out string cubeName, out var slice,
                                          out var transform, out string parseError))
            return (null, JsonRun.Fail(CliDiagnostics.PlotTraceUnresolved(spec.Raw, text, parseError)));

        // A rank-0 cube is one number. It is a legal thing to put on a Table and not a curve, and a
        // plot of it would be an empty picture — R-rnd4-4's most dangerous output.
        if (slice is null || slice.Length == 0)
            return (null, JsonRun.Fail(CliDiagnostics.PlotCubeIsScalar(cubeName)));

        return (new TraceConfig
        {
            SourcePath       = sourceRef,
            CubeName         = cubeName,
            CubeSlice        = [.. slice.Select(AxisSliceConfig.From)],
            CubeTransform    = transform,
            UseSecondaryAxis = spec.Secondary,
            // The colour wheel the window walks, in its own order — TraceProperties.LineColorOrder,
            // whose first entry is 12 (red) rather than 0. Using the trace's ordinal directly would
            // have given the first trace colour 0, which is black: invisible on a dark variant, and
            // nothing would have said so.
            Properties       = new TracePropertiesConfig
            {
                LineColorIndex   = WheelColor(index),
                MarkerColorIndex = WheelColor(index),
            },
        }, null);
    }

    /// <summary>
    /// The cube a spec names, before any slice or transform — the whole of <c>S</c>, <c>S[:,2,1]</c>,
    /// <c>mag(Pout)</c> and <c>db S[:,2,1]</c>. Deliberately loose: it exists only to make a
    /// mistyped NAME a refusal that lists the real ones, and anything it gets wrong falls through to
    /// the parser, which is the authority on the rest of the syntax.
    /// </summary>
    private static string BareCubeName(string spec)
    {
        string t = spec.Trim();

        int bracket = t.IndexOf('[');
        if (bracket >= 0) t = t[..bracket];

        // `mag(Pout)` and the half-open `mag(S` left behind by dropping the slice.
        int paren = t.LastIndexOf('(');
        if (paren >= 0) t = t[(paren + 1)..];

        // `db S` — the space-separated transform form.
        int space = t.LastIndexOfAny([' ', '\t']);
        if (space >= 0) t = t[(space + 1)..];

        return t.Trim().TrimEnd(')');
    }

    /// <summary>The nth trace's colour, from the order the window rotates through.</summary>
    private static int WheelColor(int index)
        => TraceProperties.LineColorOrder[index % TraceProperties.LineColorOrder.Length];

    private static string AxisNames(DataCube cube) => string.Join(", ", cube.Axes.Select(a => a.Name));

    private static string CubeNames(DataSet ds)
        => string.Join(", ", ds.Groups.SelectMany(g => ds.CubesIn(g).Keys
            .Select(c => g == DataSet.DefaultGroup ? c : $"{g}.{c}")));

    /// <summary>
    /// The whole document: one tab, one plot, the traces given. Written as the CURRENT format
    /// version with a tab, rather than as the legacy top-level plot list, so <c>--write-cdd</c>
    /// produces a file the application opens as itself.
    /// </summary>
    private static DataDisplayConfig BuildConfig(Options o, List<TraceConfig> traces)
    {
        bool   square = o.Type is PlotType.Smith or PlotType.Polar;
        double w      = square ? SquareSize : RectWidth;
        double ratio  = AppSettings.Current.RectAspectRatio;
        double h      = square ? SquareSize
                      : o.Type == PlotType.Rect && ratio > 0 ? w / ratio
                      : 360;

        var container = new PlotContainerConfig
        {
            Left     = 0,
            Top      = 0,
            Width    = w,
            Height   = h,
            PlotType = o.Type,
            FreqUnit = o.FreqUnit,
            Traces   = traces,
            Axes     = BuildAxes(o),
        };

        if (o.Title   is { } title)  { container.CustomTitle   = title;  container.CustomTitleOn   = true; }
        if (o.XLabel  is { } xl)     { container.CustomXLabel  = xl;     container.CustomXLabelOn  = true; }
        if (o.YLabel  is { } yl)     { container.CustomYLabel  = yl;     container.CustomYLabelOn  = true; }
        if (o.Y2Label is { } y2l)    { container.CustomY2Label = y2l;    container.CustomY2LabelOn = true; }

        return new DataDisplayConfig
        {
            FormatVersion      = DataDisplayConfig.CurrentFormatVersion,
            SelectedDataSource = Path.GetFileName(o.Result!),
            Tabs               = [new TabConfig { Name = "Tab 1", Plots = [container] }],
        };
    }

    /// <summary>
    /// The axis window, or null for full autoscale.
    ///
    /// <para><b>A HALF window is deliberate and it works.</b> <c>Plot.RestoreAxesFromConfig</c> tests
    /// the window's validity as width AND height, and re-autoscales only the axes whose own flag is
    /// still set — and <c>AutoscaleCore("x")</c> preserves Y while <c>("y")</c> preserves X. So
    /// <c>--x</c> alone pins X and autoscales Y, which is what a caller asking for one of them
    /// means.</para>
    /// </summary>
    private static AxesConfig? BuildAxes(Options o)
    {
        if (o.X is null && o.Y is null && o.Y2 is null) return null;

        var axes = new AxesConfig
        {
            AutoscaleX      = o.X is null,
            AutoscaleY      = o.Y is null,
            AutoscaleRightY = o.Y2 is null,
            AutoscaleMag    = true,
            WindowX      = o.X?.Lo ?? 0, WindowWidth  = o.X is { } x ? x.Hi - x.Lo : 0,
            WindowY      = o.Y?.Lo ?? 0, WindowHeight = o.Y is { } y ? y.Hi - y.Lo : 0,
            WindowSecondaryX      = o.X?.Lo ?? 0,
            WindowSecondaryWidth  = o.X is { } sx ? sx.Hi - sx.Lo : 0,
            WindowSecondaryY      = o.Y2?.Lo ?? 0,
            WindowSecondaryHeight = o.Y2 is { } y2 ? y2.Hi - y2.Lo : 0,
        };
        return axes;
    }
}
