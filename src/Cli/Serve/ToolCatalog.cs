using System.Text.Json;
using System.Text.Json.Nodes;
using CircuitRF.Diagnostics;

namespace CircuitRF.Cli.Serve;

/// <summary>How a tool argument is spelled on the command line.</summary>
internal enum OptKind
{
    /// <summary>A boolean that becomes a bare flag when true, and nothing when false.</summary>
    Flag,
    Str,
    Number,
    Integer,
    /// <summary>A string resolved and confined by <see cref="PathRoot"/>.</summary>
    Path,
    /// <summary>An array joined with commas into one argument — <c>--views a,b</c>.</summary>
    StrList,
    /// <summary>An array emitted as the flag repeated — <c>--set a=1 --set b=2</c>.</summary>
    StrRepeat,
    /// <summary>As <see cref="StrRepeat"/>, with every item resolved and confined by
    /// <see cref="PathRoot"/> — <c>render --data a.npy --data b.npy</c>. A repeated flag whose items
    /// are FILES needs both halves; neither existing kind has both.</summary>
    PathRepeat,
    /// <summary>
    /// A string that is confined by <see cref="PathRoot"/> only when it names a FILE.
    ///
    /// <para><c>render --theme</c> is the case, and it is genuinely two things: a theme NAME resolved
    /// through a chain of directories the server root has nothing to do with (the workspace, the user
    /// themes folder, the shipped themes), or a <c>.ccolor</c> file the caller points at. Confining
    /// the first would turn <c>dark</c> into <c>&lt;root&gt;/dark</c>, which resolves to no theme at
    /// all; not confining the second would let a client name a file outside the root and learn from
    /// the refusal whether it exists.</para>
    ///
    /// <para>The split is <c>Render.ResolveTheme</c>'s own — a value carrying an extension, a
    /// directory separator or a root is a path — and the two must agree, which is why this comment
    /// names the other end.</para>
    /// </summary>
    PathOrName,
    /// <summary>A string whose flag takes its value OPTIONALLY: an empty string becomes the bare
    /// flag. <c>explain --analysis</c> is the case — with a name it asks about one chain, without
    /// one it asks about all of them, and passing "" as a value asks about a chain called "".
    /// </summary>
    StrOptional,
}

/// <param name="Json">The argument's name in the tool call.</param>
/// <param name="Cli">The flag it becomes. Empty for a positional.</param>
internal sealed record ToolOption(string Json, string Cli, OptKind Kind, string Description);

/// <param name="Json">The argument's name in the tool call.</param>
/// <param name="IsPath">Whether it is confined to the root.</param>
/// <param name="Required">
/// Whether the verb refuses without it. Every positional was required until <c>reference</c>, whose
/// no-argument form is the topic LIST — a real answer, not a usage error — and whose second
/// positional only means anything after the first.
///
/// <para>An optional positional is emitted only while the ones before it were given: argv is
/// positional, so passing a second argument with the first absent would silently make it the FIRST,
/// which is a different query answered with nothing said about it. That case is a refusal naming the
/// argument the caller left out.</para>
/// </param>
internal sealed record ToolPositional(string Json, bool IsPath, string Description, bool Required = true);

/// <param name="Verb">The argv prefix — <c>["hb"]</c>, <c>["new","workspace"]</c>.</param>
internal sealed record ToolMode(
    string                        Selector,
    string[]                      Verb,
    ToolPositional[]              Positionals,
    ToolOption[]                  Options,
    string                        Description);

/// <param name="SelectorName">The argument that chooses the mode, or null for a single-mode tool.</param>
/// <param name="Adapter">
/// Arguments the ADAPTER itself consumes and never forwards. Advertised like any other, translated
/// into nothing.
///
/// <para><b>This is the one deliberate exception to "every argument is named after the CLI flag it
/// becomes", and it exists for exactly one thing</b> (R-rnd5-4): whether a tool result carries the
/// rendered picture back as protocol content. There is no CLI spelling of it and there should not be
/// — a command line writes the file and the person opens it, where a protocol client may have no way
/// to read the path it was handed. So it is a property of the ENVELOPE, not of the render.</para>
///
/// <para>Kept as its own list rather than as a flavour of <see cref="ToolOption"/> so that the
/// invariant stays checkable by reading: everything in <c>Modes</c> becomes argv, and the only things
/// that do not are here, where <c>ServeProtocolAdapterTests</c>'
/// <c>EveryAdvertisedArgument_IsAFlagTheVerbActuallyReads</c> exempts them by name.</para>
/// </param>
internal sealed record ToolSpec(
    string        Name,
    string        Description,
    string?       SelectorName,
    string?       SelectorDescription,
    ToolMode[]    Modes,
    ToolOption[]? Adapter = null);

/// <summary>
/// The tool surface, and the ONLY thing that translates a tool call into a command line.
///
/// <para><b>Small and broad (R-aut5-4, R-aut-9).</b> NINE tools, not one per verb — the eight here
/// plus <c>HistoryBatch</c>'s, which is advertised beside them because it is the one tool that is not
/// a command line. A client that discovers tools up front carries every description for the whole
/// session whether or not it calls one, so the surface is a standing cost paid on every interaction.
/// <c>run</c> selects its analysis with an argument rather than being six tools; <c>create</c>,
/// <c>import</c> and <c>history</c> each carry the noun the CLI verb already takes; and RND-5's
/// <c>render</c> is one tool over every document kind rather than one per view (R-rnd0-4).</para>
///
/// <para><b>The schema is GENERATED from the same table that builds the command line.</b> That is
/// the point of the table: a description that says an argument exists and a translation that drops
/// it cannot happen, because there is one list. Adding a flag is one row.</para>
///
/// <para><b>The adapter makes no decisions (R-aut-1).</b> Every argument here is named after the CLI
/// flag it becomes, and nothing is defaulted, reshaped or inferred. Where the CLI refuses — a `--grid`
/// handed to a pursuit, an unstated Excellon coordinate format, a source folder holding several parts
/// — the refusal arrives from the verb, not from here. What this file refuses is only what it alone
/// can see: a tool that does not exist, an argument that does not belong to the chosen mode, an
/// argument of the wrong JSON type, and a path outside the root.</para>
///
/// <para><b>Descriptions are terse, English and invariant</b> (R-aut5-7, <c>cli.md</c> §7A). They are
/// read by a machine that pays for every word.</para>
/// </summary>
internal static class ToolCatalog
{
    // ── shared option groups ─────────────────────────────────────────────────

    private static readonly ToolOption Json  = new("only",  "--only",  OptKind.StrList,
        "Narrow the result to these cube names.");
    private static readonly ToolOption Group = new("group", "--group", OptKind.StrList,
        "Narrow the result to these group names.");

    private static readonly ToolOption Set = new("set", "--set", OptKind.StrRepeat,
        "Override a global variable, as name=expr. Applied before elaboration.");

    private static readonly ToolOption Analysis = new("name", "-a", OptKind.Str,
        "Which declared analysis to run. An inner analysis is promoted to the sweep that wraps it.");

    private static readonly ToolOption Output = new("output", "-o", OptKind.Path,
        "Where the result file is written. The extension picks the format.");

    private static readonly ToolOption All = new("all", "--all", OptKind.Flag,
        "Return every cube instead of the per-grid-point summary.");

    // Every option here is a flag the VERB'S OWN argument loop reads. That is not a comment, it is
    // a gate: ServeToolCatalogParityTests scans each verb's parser for the literal and fails on one
    // that is not there. It exists because an advertised flag a verb does not take does not read as
    // "unknown option" — the value is taken as the input PATH ("File not found: 3") or, where the
    // verb reads its path positionally, dropped in silence, which is a run answering a different
    // question than the one asked (R-aut-1: the adapter offers what the capability has, and nothing
    // else).
    private static readonly ToolOption MaxHarm = new("maxharm",  "--maxharm",  OptKind.Integer,
        "Override MaxHarm.");
    private static readonly ToolOption Tol     = new("tol",      "--tol",      OptKind.Number,
        "Override the convergence tolerance.");
    private static readonly ToolOption MaxIter = new("maxIter",  "--max-iter", OptKind.Integer,
        "Override the iteration cap.");

    /// <summary>What <c>hb</c> takes. <c>--maxmix</c> is HB's alone — the loadpull verbs' own loop
    /// does not read it.</summary>
    private static readonly ToolOption[] SolverOptions =
    [
        MaxHarm,
        new("maxmix", "--maxmix", OptKind.Integer, "Override MaxMixOrder (multi-tone)."),
        Tol,
        MaxIter,
    ];

    /// <summary>What <c>lp</c> and <c>lpp</c> take: <see cref="SolverOptions"/> without
    /// <c>--maxmix</c>.</summary>
    private static readonly ToolOption[] LoadpullSolverOptions = [MaxHarm, Tol, MaxIter];

    /// <summary>What <c>dc</c> takes. A DC solve has no harmonics and no tolerance override — its
    /// three knobs are the iteration cap, the bias ramp and the conductance floor.</summary>
    private static readonly ToolOption[] DcOptions =
    [
        MaxIter,
        new("dcSteps", "--dc-steps", OptKind.Integer, "Override the DC bias ramp step count."),
        new("gmin",    "--gmin",     OptKind.Number,  "Override the conductance floor."),
    ];

    private static readonly ToolOption[] LoadpullOptions =
    [
        new("pin",         "--pin",         OptKind.Str,     "Override the drive ladder, as start:step:max in dBm."),
        new("compression", "--compression", OptKind.Number,  "Override the compression target, dB."),
        new("grid",        "--grid",        OptKind.Path,    "lp only: the Gamma grid to read."),
        new("outGrid",     "--out-grid",    OptKind.Path,    "lpp only: where found terminations are written."),
    ];

    /// <summary>
    /// The largest rendered file handed back inside a tool result (R-rnd5-4).
    ///
    /// <para><b>The number comes from the measured table in <c>src/Cli/RESOLVED.md</c> (RND-2), not
    /// from taste.</b> A real 6-layer board at the default 1600x1200 page is 1.4 MB as a PNG, 9.6 MB
    /// as a <c>--detail full</c> PDF and 23.5 MB as a <c>--detail full</c> SVG; the same board at
    /// <c>--detail screen</c> is 1.0 MB / 2.6 MB / 6.2 MB. Four mebibytes therefore admits every
    /// raster this verb plausibly produces — which is the format an agent wants when it wants to LOOK
    /// at something — and every schematic and symbol in any format, while refusing exactly the two
    /// cases where a caller genuinely should have narrowed: a whole dense board as vector geometry.
    /// Base64 adds a third on top, which is why the cap is on the file rather than on the frame.</para>
    ///
    /// <para>A file over it is not truncated and not dropped in silence — see
    /// <c>CliDiagnostics.ServeImageTooLarge</c>.</para>
    /// </summary>
    public const long AttachmentCapBytes = 4L * 1024 * 1024;

    /// <summary>The adapter argument that asks for the rendered file back in the result. Named here
    /// so the row that advertises it and the code that reads it cannot part company.</summary>
    public const string AttachImageArgument = "attachImage";

    /// <summary>Whether this call asked for the picture. False for every tool that has no such
    /// argument, and false for the one that does unless the client said so — R-rnd5-4's default.</summary>
    public static bool AttachmentAsked(JsonObject? arguments)
        => arguments?[AttachImageArgument] is { } node
        && node.GetValue<JsonElement>().ValueKind == JsonValueKind.True;

    // ── the eight tools ──────────────────────────────────────────────────────

    public static readonly ToolSpec[] Tools =
    [
        new("run",
            "Run one analysis on a netlist or an EM setup and return its result document.",
            "analysis",
            "Which analysis to run.",
            [
                // No `-a` and no `--set`: RunSparam's loop reads neither. It takes the first typed
                // SParameterAnalysis and has no override path — src/Cli/RESOLVED.md records that as
                // a capability gap in the two oldest verbs rather than one this table may paper over.
                new("sparam", ["sparam"],
                    [new("path", true, "The .cnl to run.")],
                    [Output, Json, Group,
                     new("freq", "--freq", OptKind.Str, "Frequency sweep as start:stop:step (1GHz, 100MHz, 1e9).")],
                    "S-parameters."),
                // Likewise no `--set`, and DcSettingsFrom's own three knobs rather than HB's.
                new("dc", ["dc"],
                    [new("path", true, "The .cnl to run.")],
                    [Json, Group, .. DcOptions],
                    "DC operating point."),
                new("hb", ["hb"],
                    [new("path", true, "The .cnl to run.")],
                    [Analysis, Set, Output, All, Json, Group, .. SolverOptions],
                    "Harmonic balance. Runs the parametric sweep when one wraps it."),
                new("lp", ["lp"],
                    [new("path", true, "The .cnl to run.")],
                    [Analysis, Set, Output, All, Json, Group, .. LoadpullSolverOptions, .. LoadpullOptions],
                    "Loadpull over the directive's Gamma grid. The default result is one row per grid point."),
                new("lpp", ["lpp"],
                    [new("path", true, "The .cnl to run.")],
                    [Analysis, Set, Output, All, Json, Group, .. LoadpullSolverOptions, .. LoadpullOptions],
                    "Loadpull pursuit: searches for the MXP and MXE terminations."),
                new("em", ["em"],
                    [new("path", true, "The .cem to run.")],
                    [Output, Json, Group,
                     new("workspace", "--workspace", OptKind.Path,
                         "The .cws paths resolve against. Default: the nearest one above the .cem.")],
                    "Electromagnetic extraction of the layout the .cem names. Writes a Touchstone and a .npy."),
            ]),

        new("check",
            "Validate a workspace, a cell folder or one document. Runs no analysis and writes nothing.",
            null, null,
            [
                new("", [ "check" ],
                    [new("path", true, "A workspace, a cell folder, or one .csch .csym .clay .ctech .cem .cnl .wasm.")],
                    [
                        new("recursive", "--recursive", OptKind.Flag, "Descend a plain folder. A workspace always descends."),
                        new("severity",  "--severity",  OptKind.Str,
                            "What decides the exit code: warning or error. Default error; warnings are reported either way."),
                    ],
                    ""),
            ]),

        new("explain",
            "Report what circuitRF resolved a document to, and the walk it took. Runs no analysis and writes nothing.",
            null, null,
            [
                new("", [ "explain" ],
                    [new("path", true, "The document to explain.")],
                    [
                        Set,
                        new("expr",     "--expr",     OptKind.Str, "Evaluate an expression in the design's own resolved scope."),
                        // StrOptional, not Str: `explain` reads the name as an OPTIONAL token after
                        // the flag, so an empty string handed through as a value is a name — and
                        // `--analysis ""` is refused with "No analysis named ''". The empty string
                        // has to become the bare flag for the description below to be true.
                        new("analysis", "--analysis", OptKind.StrOptional,
                            "Report the analysis chains. A name asks about that one; an empty string asks about all of them."),
                        new("ref",      "--ref",      OptKind.Str, "What a relative cell reference resolves to from this document."),

                        // RND-3's three, and the two arguments that ANSWER their refusals. The
                        // brief asks for three; `--all` and `--view` are here because R-aut-13 says
                        // nothing reachable from the command line may be unreachable here, and
                        // without them a client that asks `--extents` of a cell folder holding two
                        // views is handed a refusal naming a flag it cannot pass. See
                        // src/Cli/RESOLVED.md, RND-5.
                        new("cells",   "--cells",   OptKind.Flag,
                            "List the cells and, for each, which file each view resolves to and in what state."),
                        new("layers",  "--layers",  OptKind.Flag,
                            "List the layers of the resolved technology, with how many shapes the document draws on each."),
                        new("extents", "--extents", OptKind.Flag,
                            "The document's bounding box, in base SI with its unit and scale — what render --fit frames on."),
                        new("all",     "--all",     OptKind.Flag,
                            "Only with cells: include generated cells, which are hidden by default. On its own it is refused."),
                        new("view",    "--view",    OptKind.Str,
                            "Which view of a cell folder: schematic, symbol or layout. Spelled as render spells it."),
                    ],
                    ""),
            ]),

        new("create",
            "Create a correct initial document — a workspace, or a cell folder with its view files.",
            "what",
            "What to create.",
            [
                new("workspace", ["new", "workspace"],
                    [new("path", true, "The workspace directory, or its parent when name is given.")],
                    [
                        new("name", "--name", OptKind.Str,  "The workspace name."),
                        new("tech", "--tech", OptKind.Str,  "A shipped technology id, or none. Default: the one the New Workspace dialog pre-selects."),
                    ],
                    "A workspace, with a shipped technology copied in."),
                new("cell", ["new", "cell"],
                    [
                        new("workspace", true,  "The workspace the cell is created in."),
                        new("name",      false, "The cell name."),
                    ],
                    [new("views", "--views", OptKind.StrList, "Which views to create: schematic, symbol, layout. Default schematic.")],
                    "A cell folder and one empty-but-valid file per view."),
            ]),

        new("import",
            "Bring foreign artwork or a component in: a part as a cell, or one interchange format converted to another.",
            "what",
            "Which import.",
            [
                new("part", ["import", "part"],
                    [new("path", true, "The component file or folder.")],
                    [
                        new("into",       "--into",        OptKind.Path, "The workspace the cell is created in."),
                        new("cell",       "--cell",        OptKind.Str,  "Which part, when the source holds several."),
                        new("variant",    "--variant",     OptKind.Str,  "Which variant, when the part has several."),
                        new("listParts",  "--list-parts",  OptKind.Flag, "Report what the source holds and create nothing."),
                        new("tech",       "--tech",        OptKind.Path, "The .ctech the layers reconcile against."),
                        new("addLayers",  "--add-layers",  OptKind.Flag, "Write the part's new layers into that technology."),
                    ],
                    "A footprint and its symbol, as a cell."),
                new("convert", ["convert"],
                    [new("path", true, "The file or folder to import. A folder is a Gerber file set.")],
                    [
                        Output,
                        new("from",       "--from",        OptKind.Str,  "The source format when the path does not say: clay, gdsii, dxf, gerber, board."),
                        new("to",         "--to",          OptKind.Str,  "The target format when the path does not say."),
                        new("cell",       "--cell",        OptKind.Str,  "Which cell, when the source holds several."),
                        new("name",       "--name",        OptKind.Str,  "What to call the written file set (gerber)."),
                        new("listCells",  "--list-cells",  OptKind.Flag, "Report what the input holds and write nothing."),
                        new("tech",       "--tech",        OptKind.Path, "The technology to convert against."),
                        new("keepCells",  "--keep-cells",  OptKind.Path, "Keep the cells the import produced, here."),
                        new("workspace",  "--workspace",   OptKind.Path,
                            "The .cws the layers graft onto. Without one a .ctech of its own is written."),
                        new("dbu",        "--dbu",         OptKind.Integer, "Database units per micron. Default 1000."),
                        new("dxfVersion", "--dxf-version", OptKind.Str,  "AC1015, AC1018 or AC1032."),
                        new("dxfUnits",   "--dxf-units",   OptKind.Integer, "The DXF units code."),
                        new("drillUnits", "--drill-units", OptKind.Str,  "mm or inch."),
                        new("drillFormat","--drill-format",OptKind.Str,  "Excellon coordinate format, as int:dec."),
                        new("drillZeros", "--drill-zeros", OptKind.Str,  "leading or trailing suppression."),
                        new("acceptInferredDrillFormat", "--accept-inferred-drill-format", OptKind.Flag,
                            "Proceed on a guessed Excellon format. Unstated, it is refused: the two readings differ by four orders of magnitude."),
                    ],
                    "One import and one export, between any two interchange formats."),
            ]),

        // RND-5's one new tool, and it is one (R-rnd0-4 / R-rnd5-1): the document kind comes from the
        // path exactly as `check`'s does, so there is no `render-schematic` and no selector. Making
        // the view type a selector would advertise three modes where there is one verb.
        //
        // `output` is a Path like every other, and nothing about the confinement changes because this
        // is the first tool whose PURPOSE is to write a file: it writes only where the caller named,
        // and there is still no tool that deletes (cli.md 11.5).
        new("render",
            "Draw a schematic, a symbol, a layout or a data display as a picture: .svg, .pdf or .png. "
          + "The document kind comes from the path. Ask explain --extents and --layers first if you "
          + "need a window or a layer name.",
            null, null,
            [
                new("", [ "render" ],
                    [new("path", true,
                         "The .csch .csym .clay or .cdd, a cell folder, or a workspace with cell.")],
                    [
                        new("output", "-o", OptKind.Path,
                            "Required. Where the picture is written; its extension picks the format: .svg, .pdf or .png."),
                        new("format", "--format", OptKind.Str,
                            "Override the format the extension implies: svg, pdf or png."),
                        new("view", "--view", OptKind.Str,
                            "Which view of a cell folder: schematic, symbol or layout. More than one is refused, not ordered."),
                        new("cell", "--cell", OptKind.Str,
                            "Which cell, when the path is a workspace."),

                        // The three viewport modes. They are REFUSED together rather than ordered
                        // (R-rnd2-3), so all three are advertised and the verb decides.
                        new("fit", "--fit", OptKind.Flag,
                            "Frame the whole document. The default."),
                        new("window", "--window", OptKind.Str,
                            "An explicit world rectangle, x0,y0,x1,y1. ON A LAYOUT EVERY COORDINATE CARRIES AN SI UNIT, "
                          + "zero included (0um,0um,500um,300um): a bare number is refused, because DBU, um and mm are "
                          + "three plausible pictures. A schematic or a symbol takes bare design units."),
                        new("center", "--center", OptKind.Str,
                            "The centre of the window, x,y. Same unit rule. Needs span."),
                        new("span", "--span", OptKind.Str,
                            "The width of the window; the height follows the output aspect. Same unit rule."),
                        new("margin", "--margin", OptKind.Number,
                            "Fraction of the page left around a fit. 0 to 0.45."),

                        new("size", "--size", OptKind.Str,
                            "Output size as WxH. Device pixels for png, points for svg and pdf. Default 1600x1200."),
                        new("scale", "--scale", OptKind.Number,
                            "png only: raster multiplier. Refused on a vector format, and refused together with dpi."),
                        new("dpi", "--dpi", OptKind.Number,
                            "png only: the same multiplier spelled relative to 96 dpi."),

                        new("layers", "--layers", OptKind.StrList,
                            "Layout only: draw only these layers. An undefined name is refused — ask explain --layers."),
                        new("hideLayers", "--hide-layers", OptKind.StrList,
                            "Layout only: draw everything but these. Refused together with layers."),
                        new("detail", "--detail", OptKind.Str,
                            "full (default: every level-of-detail tier off, what is stored is what is drawn), screen "
                          + "(the tiers as a canvas engages them), or a pixel budget. A vector file is several times "
                          + "larger at full."),

                        new("theme", "--theme", OptKind.PathOrName,
                            "A colour theme name, or a .ccolor file. Default: the workspace's own, else the shipped one."),
                        new("variant", "--variant", OptKind.Str, "light or dark. Default light."),
                        new("background", "--background", OptKind.Str, "opaque or transparent. Default opaque."),
                        new("grid", "--grid", OptKind.Flag, "Draw the grid. Off by default, as every export is."),
                        new("noRulers", "--no-rulers", OptKind.Flag,
                            "Leave the rulers out. They are on by default because a ruler is in the document."),

                        // The .cdd half (RND-4). Options of the one verb, not a second tool: a data
                        // display is a document kind like the other three.
                        new("data", "--data", OptKind.PathRepeat,
                            ".cdd only: a result file to bind. The first binds the document's own selected source. "
                          + "A .cdd whose sources do not resolve is a refusal, not an empty plot."),
                        new("tab", "--tab", OptKind.Str, ".cdd only: which tab, by name or by number."),
                        new("plot", "--plot", OptKind.Integer, ".cdd only: which plot on that tab, from 1."),
                        new("allTabs", "--all-tabs", OptKind.Flag,
                            ".cdd only: every tab, one page each. pdf only."),
                    ],
                    ""),
            ],
            // R-rnd5-4, and the whole point of this tool for an agent: a client that receives only a
            // path has to be able to READ that path, and many cannot. Default OFF because an image is
            // expensive in a way a JSON document is not.
            Adapter:
            [
                new(AttachImageArgument, "", OptKind.Flag,
                    "Return the rendered file itself in the result, as well as writing it. Off by default; "
                  + "a file over " + (AttachmentCapBytes / (1024 * 1024)) + " MB comes back as its path with a "
                  + "note saying what to narrow."),
            ]),

        new("read",
            "Read a file back: a result file as cubes, or one of circuitRF's own documents as its own text.",
            null, null,
            [
                new("", [ "read" ],
                    [new("path", true, "A .npy or Touchstone result, or a .cws .csch .csym .clay .ctech .cem .cnl document.")],
                    [Json, Group],
                    ""),
            ]),

        // The seventh, and the only one that names no file. It is also reachable as MCP RESOURCES,
        // which is the cheaper channel — a resource costs a URI and a title until it is read, where
        // this description is a standing per-session cost. It exists anyway because not every client
        // surfaces resources to the model, and a capability the model cannot reach is not a
        // capability (R-aut6-4). Two lines, and it earns them by being the thing that unblocks
        // writing a document at all.
        // R-rc5-23, R-rc5-6h. Three of the four nouns are the CLI's verbs by construction; the
        // batch's own open and close are NOT here, because they hold session state and a process
        // that exits after one command cannot. They live on this server (see HistoryBatchTool),
        // which is the only surface that can hold them.
        new("history",
            "This workspace's restore points: keep one, list them, or put the workspace back to one. "
          + "Open a batch with 'batch' BEFORE your first modification — a restore point taken "
          + "afterwards protects nothing.",
            "action",
            "What to do.",
            [
                new("checkpoint", ["history", "checkpoint"],
                    [new("path", true, "The workspace folder.")],
                    [
                        new("intent", "--intent", OptKind.Str,
                            "One line saying what this state is. It is the label the designer reads."),
                        new("leaveOut", "--leave-out", OptKind.StrRepeat,
                            "A workspace-relative path to leave out of this one."),
                        new("includeLarge", "--include-large", OptKind.Flag,
                            "Keep unusually large new files too. Without this, an unanswered one is "
                          + "a refusal naming it: keeping a file cannot be undone, leaving it out can."),
                    ],
                    "Keep a restore point of the workspace as it stands."),
                new("list", ["history", "list"],
                    [new("path", true, "The workspace folder.")],
                    [new("limit", "--limit", OptKind.Integer, "Return at most this many, newest first.")],
                    "The restore points, newest first."),
                new("restore", ["history", "restore"],
                    [new("path", true, "The workspace folder.")],
                    [new("point", "--point", OptKind.Integer,
                         "Which restore point, as the number 'list' reports.")],
                    "Put the workspace back to one restore point. The state being replaced is kept first."),
            ]),

        new("reference",
            "What may be written in circuitRF's documents: the reference pages, and the generated " +
            "catalogue of every netlist primitive with its terminals and parameters. " +
            "No arguments lists the topics and their sizes. Reads no file and writes nothing.",
            null, null,
            [
                new("", [ "reference" ],
                    [
                        new("topic", false,
                            "Which topic. Omit for the list. 'components' is the generated catalogue.",
                            Required: false),
                        new("type",  false,
                            "With topic 'components', one primitive's .cnl type token — MLIN, SDD, FET_Statz.",
                            Required: false),
                    ],
                    [],
                    ""),
            ]),
    ];

    // ── translation ──────────────────────────────────────────────────────────

    /// <summary>
    /// Turns one tool call into the argument vector the CLI would have been given, or refuses.
    /// <c>--json</c> is appended here and not offered as an argument: a document is what a tool
    /// call returns, always (R-aut5-5).
    /// </summary>
    public static string[]? ToArgv(string tool, JsonObject? arguments, PathRoot root, out Diagnostic? refusal)
    {
        refusal = null;

        var spec = Tools.FirstOrDefault(t => t.Name == tool);
        if (spec is null)
        {
            refusal = CliDiagnostics.ServeUnknownTool(tool, string.Join(", ", Tools.Select(t => t.Name)));
            return null;
        }

        arguments ??= [];

        ToolMode mode;
        if (spec.SelectorName is { } selector)
        {
            string? chosen = arguments[selector]?.GetValue<JsonElement>().ValueKind == JsonValueKind.String
                ? arguments[selector]!.GetValue<string>()
                : null;

            if (chosen is null)
            {
                refusal = CliDiagnostics.ServeArgumentRequired(tool, selector);
                return null;
            }
            if (spec.Modes.FirstOrDefault(m => m.Selector == chosen) is not { } found)
            {
                refusal = CliDiagnostics.ServeArgumentUnknownValue(
                    tool, selector, chosen, string.Join(", ", spec.Modes.Select(m => m.Selector)));
                return null;
            }
            mode = found;
        }
        else mode = spec.Modes[0];

        var argv = new List<string>(mode.Verb);

        // Positionals first, in declaration order — the verbs take them that way.
        //
        // An OPTIONAL one that is absent stops the run: everything after it is positional too, so
        // emitting a later argument into an earlier slot would answer a different question in
        // silence. That is refused by name instead.
        bool stopped = false;
        foreach (var p in mode.Positionals)
        {
            if (arguments[p.Json] is not { } node)
            {
                if (p.Required)
                {
                    refusal = CliDiagnostics.ServeArgumentRequired(tool, p.Json);
                    return null;
                }
                stopped = true;
                continue;
            }
            if (stopped)
            {
                refusal = CliDiagnostics.ServeArgumentRequired(tool, LastMissingBefore(mode, p));
                return null;
            }

            string? text = AsString(node, tool, p.Json, ref refusal);
            if (text is null) return null;

            if (p.IsPath)
            {
                text = root.Resolve(text, out refusal);
                if (text is null) return null;
            }
            argv.Add(text);
        }

        // Then every option the call carries. An argument that belongs to no other mode of this tool
        // is unknown; one that belongs to another mode is named as such, because "unknown option
        // grid" is a worse answer than "grid is lp's, not lpp's".
        foreach (var (name, node) in arguments)
        {
            if (name == spec.SelectorName) continue;
            if (mode.Positionals.Any(p => p.Json == name)) continue;
            // An adapter argument becomes nothing. It is still TYPE-checked, because a client that
            // wrote `"attachImage": "yes"` and was handed a picture-less result with nothing said
            // would have no way to find its own mistake.
            if (spec.Adapter?.FirstOrDefault(o => o.Json == name) is { } own)
            {
                if (node is not null && !Emit(null, own, node, tool, root, ref refusal)) return null;
                continue;
            }
            if (node is null) continue;

            if (mode.Options.FirstOrDefault(o => o.Json == name) is not { } opt)
            {
                refusal = spec.Modes.Any(m => m.Options.Any(o => o.Json == name) ||
                                              m.Positionals.Any(p => p.Json == name))
                    ? CliDiagnostics.ServeArgumentNotForMode(tool, name, mode.Selector.Length == 0 ? tool : mode.Selector)
                    : CliDiagnostics.ServeArgumentUnknownValue(
                          tool, "arguments", name,
                          string.Join(", ", mode.Options.Select(o => o.Json)
                                                        .Concat(mode.Positionals.Select(p => p.Json))));
                return null;
            }

            if (!Emit(argv, opt, node, tool, root, ref refusal)) return null;
        }

        argv.Add("--json");
        return [.. argv];
    }

    /// <summary>The positional the caller left out — named, so the refusal says what to add rather
    /// than what to remove.</summary>
    private static string LastMissingBefore(ToolMode mode, ToolPositional given)
        => mode.Positionals.TakeWhile(p => p.Json != given.Json).Last().Json;

    /// <summary>
    /// Appends one argument's flag and value to <paramref name="argv"/>, or refuses.
    ///
    /// <para><paramref name="argv"/> is null for an ADAPTER argument (<see cref="ToolSpec.Adapter"/>),
    /// which becomes no command line at all: the call then does the type check and nothing else, so
    /// there is one place a JSON type is decided rather than two that could disagree.</para>
    /// </summary>
    private static bool Emit(List<string>? argv, ToolOption opt, JsonNode node, string tool,
                             PathRoot root, ref Diagnostic? refusal)
    {
        switch (opt.Kind)
        {
            case OptKind.Flag:
            {
                if (node.GetValue<JsonElement>().ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    refusal = CliDiagnostics.ServeArgumentWrongType(tool, opt.Json, "a boolean");
                    return false;
                }
                if (node.GetValue<bool>()) argv?.Add(opt.Cli);
                return true;
            }

            case OptKind.Number or OptKind.Integer:
            {
                if (node.GetValue<JsonElement>().ValueKind != JsonValueKind.Number)
                {
                    refusal = CliDiagnostics.ServeArgumentWrongType(tool, opt.Json, "a number");
                    return false;
                }
                argv?.Add(opt.Cli);
                // Invariant, because the CLI parses it invariantly (cli.md §7A) — a decimal comma
                // here would be a number the verb cannot read, on one machine only.
                argv?.Add(node.GetValue<JsonElement>().GetRawText());
                return true;
            }

            case OptKind.StrOptional:
            {
                string? optional = AsString(node, tool, opt.Json, ref refusal);
                if (optional is null) return false;
                argv?.Add(opt.Cli);
                if (optional.Length > 0) argv?.Add(optional);
                return true;
            }

            case OptKind.Str or OptKind.Path or OptKind.PathOrName:
            {
                string? text = AsString(node, tool, opt.Json, ref refusal);
                if (text is null) return false;
                if (opt.Kind == OptKind.Path || (opt.Kind == OptKind.PathOrName && NamesAFile(text)))
                {
                    text = root.Resolve(text, out refusal);
                    if (text is null) return false;
                }
                argv?.Add(opt.Cli);
                argv?.Add(text);
                return true;
            }

            case OptKind.StrList or OptKind.StrRepeat or OptKind.PathRepeat:
            {
                var items = AsStrings(node, tool, opt.Json, ref refusal);
                if (items is null) return false;
                if (items.Count == 0) return true;

                if (opt.Kind == OptKind.StrList) { argv?.Add(opt.Cli); argv?.Add(string.Join(',', items)); }
                else foreach (string item in items)
                {
                    string value = item;
                    if (opt.Kind == OptKind.PathRepeat)
                    {
                        string? resolved = root.Resolve(value, out refusal);
                        if (resolved is null) return false;
                        value = resolved;
                    }
                    argv?.Add(opt.Cli);
                    argv?.Add(value);
                }
                return true;
            }

            default: return true;
        }
    }

    /// <summary>
    /// Whether a <see cref="OptKind.PathOrName"/> value is a FILE rather than a name.
    ///
    /// <para>The rule is <c>Render.ResolveTheme</c>'s, one step earlier: a value carrying an
    /// extension, a directory separator or a drive/root is a path and is confined; anything else is a
    /// bare name that resolves through a chain of directories the server root has nothing to do with.
    /// Both ends have to make the same split, which is why each names the other.</para>
    /// </summary>
    private static bool NamesAFile(string value)
        => Path.IsPathRooted(value)
        || value.Contains('/') || value.Contains('\\')
        || Path.GetExtension(value).Length > 0;

    private static string? AsString(JsonNode node, string tool, string name, ref Diagnostic? refusal)
    {
        if (node.GetValue<JsonElement>().ValueKind == JsonValueKind.String) return node.GetValue<string>();
        refusal = CliDiagnostics.ServeArgumentWrongType(tool, name, "a string");
        return null;
    }

    private static List<string>? AsStrings(JsonNode node, string tool, string name, ref Diagnostic? refusal)
    {
        if (node is JsonArray array)
        {
            var items = new List<string>(array.Count);
            foreach (var element in array)
            {
                if (element is null || element.GetValue<JsonElement>().ValueKind != JsonValueKind.String)
                {
                    refusal = CliDiagnostics.ServeArgumentWrongType(tool, name, "an array of strings");
                    return null;
                }
                items.Add(element.GetValue<string>());
            }
            return items;
        }
        if (node.GetValue<JsonElement>().ValueKind == JsonValueKind.String) return [node.GetValue<string>()];

        refusal = CliDiagnostics.ServeArgumentWrongType(tool, name, "an array of strings");
        return null;
    }

    // ── advertisement ────────────────────────────────────────────────────────

    /// <summary>The <c>tools/list</c> payload, built from the same table the translation reads.</summary>
    public static JsonArray Advertise()
    {
        var tools = new JsonArray();
        foreach (var spec in Tools)
        {
            var properties = new JsonObject();
            var required   = new JsonArray();

            if (spec.SelectorName is { } selector)
            {
                var values = new JsonArray();
                foreach (var m in spec.Modes) values.Add(m.Selector);
                properties[selector] = new JsonObject
                {
                    ["type"]        = "string",
                    ["enum"]        = values,
                    ["description"] = spec.SelectorDescription + " " +
                                      string.Join(" ", spec.Modes.Select(m => $"{m.Selector}: {m.Description}")).Trim(),
                };
                required.Add(selector);
            }

            // The union across modes. A per-mode requirement is enforced at call time with a refusal
            // naming the argument, which is the same shape every authoring verb already uses.
            foreach (var mode in spec.Modes)
            {
                foreach (var p in mode.Positionals)
                    properties[p.Json] ??= new JsonObject { ["type"] = "string", ["description"] = p.Description };

                foreach (var o in mode.Options)
                    properties[o.Json] ??= Describe(o);
            }

            // The adapter's own, advertised beside the rest because a client cannot tell — and does
            // not need to — which side of the translation an argument is read on.
            foreach (var o in spec.Adapter ?? [])
                properties[o.Json] ??= Describe(o);

            if (spec.SelectorName is null)
                foreach (var p in spec.Modes[0].Positionals.Where(p => p.Required)) required.Add(p.Json);

            tools.Add(new JsonObject
            {
                ["name"]        = spec.Name,
                ["description"] = spec.Description,
                ["inputSchema"] = new JsonObject
                {
                    ["type"]       = "object",
                    ["properties"] = properties,
                    ["required"]   = required,
                },
            });
        }
        return tools;
    }

    private static JsonObject Describe(ToolOption o) => o.Kind switch
    {
        OptKind.Flag    => new JsonObject { ["type"] = "boolean", ["description"] = o.Description },
        OptKind.Number  => new JsonObject { ["type"] = "number",  ["description"] = o.Description },
        OptKind.Integer => new JsonObject { ["type"] = "integer", ["description"] = o.Description },
        OptKind.StrList or OptKind.StrRepeat or OptKind.PathRepeat => new JsonObject
        {
            ["type"]        = "array",
            ["items"]       = new JsonObject { ["type"] = "string" },
            ["description"] = o.Description,
        },
        _ => new JsonObject { ["type"] = "string", ["description"] = o.Description },
    };
}
