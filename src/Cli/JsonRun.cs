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
/// <para><b>Why static mutable state.</b> <c>Program.cs</c> is top-level statements dispatching to
/// static local functions, and threading a context object through every one of them would be the
/// refactor R-aut0-4's spirit says not to bundle into this change. One process, one invocation, one
/// document.</para>
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
            _stdout = Console.Out;
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
