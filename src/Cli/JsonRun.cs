using System.Reflection;
using CircuitRF.Diagnostics;
using RfCore.Data;
using RfCore.Export;

namespace CircuitRF.Cli;

/// <summary>
/// <c>--json</c>: one machine-readable document per invocation, on stdout, and nothing else on
/// stdout (brief-automation-1-structured-output.md, R-aut0-7 for the spelling).
///
/// <para><b>This is additive and it must be invisible to anyone who does not pass the flag.</b>
/// R-aut0-3: the human tables, the stdout/stderr split (<c>cli.md</c> §3.1), the English-forever rule
/// (§7A) and the per-verb exit codes (§7) all survive unchanged, and a script parsing stderr today
/// must not notice this landing at all.</para>
///
/// <para><b>How "nothing else on stdout" is guaranteed, and why it is done this way.</b> The moment
/// the flag is seen, <see cref="Console.Out"/> is replaced with <see cref="TextWriter.Null"/> and
/// the real stdout is held here until <see cref="Finish"/> writes the document to it. So the
/// guarantee is structural rather than a rule every printer has to remember: a verb prints its
/// table exactly as it always did, into a sink, and there is no way for a later-added
/// <c>Console.WriteLine</c> to leak into a caller's parser. stderr is untouched, so progress, notes
/// and warnings still stream while a long run is going.</para>
///
/// <para><b>Why static mutable state.</b> <c>CliEntry</c> dispatches to static functions, and
/// threading a context object through every one of them would be the refactor R-aut0-4's spirit says
/// not to bundle into this change. One command line is one invocation and one document. The one
/// caller that invokes more than once per process — <c>serve</c> — calls <see cref="Reset"/> between
/// them and runs its calls one at a time, which is what this state costs and why the adapter is
/// serialized rather than concurrent.</para>
/// </summary>
internal static class JsonRun
{
    /// <summary>True once <c>--json</c> has been seen. Nothing else in this class does anything
    /// observable when it is false — the collectors still fill, and are then discarded.</summary>
    public static bool Enabled { get; private set; }

    /// <summary>The real stdout, held from the moment the flag is parsed.</summary>
    private static TextWriter? _stdout;

    private static string[]? _onlyCubes;
    private static string[]? _onlyGroups;

    /// <summary>Set by <c>lp</c>/<c>lpp</c> from their own <c>--all</c>. R-aut1-5: under
    /// <c>--json</c> a loadpull's default stays the one-row-per-grid-point summary — it is the useful
    /// projection, not a terminal compromise — and <c>--all</c> still means every cube.</summary>
    public static bool AllCubes;

    /// <summary>Set by <c>lp</c>/<c>lpp</c>, the two verbs whose default document is the summary.</summary>
    public static bool SummaryIsTheDefault;

    public static string  Verb      = "";
    public static string? InputPath;

    /// <summary>The chain that ACTUALLY ran, after <c>SelectTop</c>'s promotion — see
    /// <see cref="ResultInput"/>.</summary>
    public static string? Analysis;

    /// <summary>The run's cubes, already merged with its measurements by the verb, exactly as the
    /// verb hands them to the exporter.</summary>
    public static DataSet? Data;

    /// <summary>What <c>check</c> found, in counts (R-aut4-10). The findings themselves travel as
    /// diagnostics; this is the tally that tells "checked nothing" from "checked everything".</summary>
    public static CheckReportJson? Check;

    /// <summary>What <c>explain</c> resolved, and the walk it performed (R-aut4-7).</summary>
    public static ExplainReportJson? Explain;

    /// <summary>The document <c>read</c> handed back, when the file was one of circuitRF's own
    /// rather than a result.</summary>
    public static DocumentJson? Document;

    /// <summary>What <c>reference</c> was asked — the topic list, one topic, or the component
    /// catalogue. About no document at all, which is why it is its own payload
    /// (brief-automation-6-reference-and-components.md §5).</summary>
    public static ReferenceReportJson? Reference;

    /// <summary>What <c>history</c> answered — the restore-point list, or what one boundary or one
    /// restore did (R-rc5-23). About no analysis at all, which is why it is its own payload.</summary>
    public static HistoryReportJson? History;

    /// <summary>What <c>render</c> decided — the viewport, the theme, the layers, the detail tier and
    /// the frame's own work counters (R-rnd2-11). About no analysis either, and about a file the run
    /// WROTE rather than one it read, which is why <c>outputs</c> carries the picture and this carries
    /// everything the picture cannot say about itself.</summary>
    public static RenderReportJson? Render;

    /// <summary>
    /// Where <see cref="Finish"/> writes, instead of stdout. Set by <c>serve</c> only.
    ///
    /// <para>It exists because that adapter's stdout is the protocol stream: a document written to
    /// it directly would land in the middle of a JSON-RPC frame and the client would see a parse
    /// error with no indication of the cause (R-aut5-2).</para>
    /// </summary>
    public static TextWriter? Sink;

    /// <summary>
    /// Clears every collector so the next <see cref="CliEntry.Run"/> in this process starts from
    /// nothing.
    ///
    /// <para>A command line never calls this — one process, one invocation, one document, which is
    /// what the class was written for. <c>serve</c> calls it before every tool call, because the
    /// alternative is a document carrying the previous call's diagnostics and outputs: a stale
    /// success that a caller has no way to tell from a real one.</para>
    /// </summary>
    public static void Reset()
    {
        Enabled             = false;
        _stdout             = null;
        _onlyCubes          = null;
        _onlyGroups         = null;
        AllCubes            = false;
        SummaryIsTheDefault = false;
        Verb                = "";
        InputPath           = null;
        Analysis            = null;
        Data                = null;
        Check               = null;
        Explain             = null;
        Document            = null;
        Reference           = null;
        History             = null;
        Render              = null;
        Outputs.Clear();
        Diagnostics.Clear();
    }

    private static readonly List<ResultOutput>   Outputs     = [];
    private static readonly List<DiagnosticJson> Diagnostics = [];

    // ── flags ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pulls <c>--json</c>, <c>--only</c> and <c>--group</c> out of the argument list before dispatch,
    /// the same way <c>--kits</c> is taken, so every verb gets the flag without repeating the parsing
    /// and no verb's own argument loop has to learn about it.
    /// </summary>
    public static string[] TakeFlags(string[] args)
    {
        var rest = new List<string>(args.Length);

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json":
                    Enabled = true;
                    continue;
                case "--only" when i + 1 < args.Length:
                    _onlyCubes = Split(args[++i]);
                    continue;
                case "--group" when i + 1 < args.Length:
                    _onlyGroups = Split(args[++i]);
                    continue;
            }
            rest.Add(args[i]);
        }

        if (Enabled)
        {
            // Sink is set by `serve`, which owns the real stdout for the protocol framing and must
            // never let a document reach it directly (R-aut5-2). Console.Out is redirected either
            // way, so the "nothing else on stdout" guarantee is the same one on both paths.
            _stdout = Sink ?? Console.Out;
            Console.SetOut(TextWriter.Null);
        }

        return rest.ToArray();

        static string[] Split(string s)
            => [.. s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    // ── collection ───────────────────────────────────────────────────────────

    /// <summary>Records a diagnostic that has already been written to stderr by its call site —
    /// the ones carrying a channel prefix (<c>warning: </c>, <c>note: </c>, <c>[circuitRF] </c>),
    /// which belongs to the channel and not to the message.</summary>
    public static void Note(Diagnostic d) => Diagnostics.Add(DiagnosticJson.From(d));

    /// <summary>Writes the diagnostic's own sentence to stderr and records it. The rendered text is
    /// byte-identical to what the call site wrote before this existed — that is the constraint the
    /// whole conversion is held to.</summary>
    public static int Fail(Diagnostic d)
    {
        Console.Error.WriteLine(d.Render());
        Diagnostics.Add(DiagnosticJson.From(d));
        return 1;
    }

    /// <summary>As <see cref="Fail"/>, but leaves the exit code to the caller.</summary>
    public static void Report(Diagnostic d)
    {
        Console.Error.WriteLine(d.Render());
        Diagnostics.Add(DiagnosticJson.From(d));
    }

    /// <summary>Every file the run wrote, with its kind. For <c>em</c> that is BOTH the <c>.sNp</c>
    /// and the <c>_em.npy</c> — they are not redundant (<c>cli.md</c> §8.2) and both belong here.</summary>
    public static void AddOutput(string kind, string path) => Outputs.Add(new ResultOutput(kind, path));

    /// <summary>The kind an export path implies, so <c>outputs</c> says what a file IS rather than
    /// leaving a caller to re-derive it from the extension.</summary>
    public static string KindOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".npy"                  => "npy",
        ".mat"                  => "mat",
        ".txt" or ".tsv"        => "tsv",
        ".spl"                  => "spl",
        ".lpcwave"              => "lpcwave",
        ".gam"                  => "gamma-grid",
        // `render`'s three (R-rnd2-2). Named rather than left as "file" for the reason every other row
        // here exists: `outputs` is meant to say what a file IS, not leave a caller re-deriving it.
        ".svg"                  => "svg",
        ".pdf"                  => "pdf",
        ".png"                  => "png",
        var e when e.StartsWith(".s", StringComparison.Ordinal) && e.EndsWith('p') => "touchstone",
        _                       => "file",
    };

    // ── the document ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds and writes the document, and returns the exit code unchanged. Called on every path out
    /// of the program, including the failing ones: R-aut1-3 — a failed run still emits a document,
    /// because a caller must not have to tell "no output" apart from "output I could not parse".
    /// </summary>
    public static int Finish(int exitCode)
    {
        if (!Enabled || _stdout is null) return exitCode;

        var doc = new ResultDocument(
            new ResultHeader(Version(), Verb),
            new ResultInput(InputPath, Analysis),
            ResultStatus.FromExitCode(exitCode),
            exitCode,
            Outputs,
            Diagnostics,
            BuildPayload());

        _stdout.WriteLine(ResultDocumentWriter.Serialize(doc));
        _stdout.Flush();
        return exitCode;
    }

    private static ResultPayload? BuildPayload()
    {
        // check, explain, a read-back document and the reference surface carry no DataSet — the
        // first two run nothing (R-aut4-1), the third is a file rather than a result, and the fourth
        // is about no document at all — so they are answered before the cube machinery, not folded
        // into it.
        if (Check is not null || Explain is not null || Document is not null || Reference is not null
         || History is not null || Render is not null)
            return new ResultPayload(null, null, Check, Explain, Document, Reference, History, Render);

        if (Data is not { } ds) return null;

        var summary = ResultDocumentWriter.SummarizeLoadpull(ds);

        // R-aut1-5. For lp/lpp the cubes are [gridPoint x pinStep] and there are eight of them, so
        // the default document is the summary — the same projection the terminal prints, and for the
        // same reason. Asking for cubes is what --all, --only and --group do.
        bool wantGroups = !SummaryIsTheDefault || AllCubes || _onlyCubes is not null || _onlyGroups is not null;

        var groups = wantGroups
            ? ResultDocumentWriter.ToJson(ds, _onlyGroups, _onlyCubes)
            : null;

        return summary is null && groups is null ? null : new ResultPayload(summary, groups);
    }

    /// <summary>
    /// The version, read from the assembly rather than written down — the repo-root <c>VERSION</c>
    /// file is the single source (see <c>src/Ui/AppVersion.cs</c> for why there is exactly one).
    /// </summary>
    private static string Version()
    {
        var asm = typeof(JsonRun).Assembly;
        string? v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? asm.GetName().Version?.ToString();
        if (string.IsNullOrWhiteSpace(v)) return "unknown";
        int plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }
}
