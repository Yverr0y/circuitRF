using CircuitRF.Core.Design;
using CircuitRF.Core.Elaboration;
using CircuitRF.Core.Expressions;
using CircuitRF.Core.Netlist;
using CircuitRF.Design.Cells;
using CircuitRF.Design.Layout;
using CircuitRF.Design.Layout.Em;
using CircuitRF.Design.Schematic;
using CircuitRF.Design.Workspace;
using System.Numerics;
using RfCore;
using RfCore.Data;
using RfCore.Export;

namespace CircuitRF.Cli;

/// <summary>
/// <c>circuitrf explain &lt;path&gt; [--expr … | --analysis [name] | --ref …]</c> — what did circuitRF
/// decide? (brief-automation-4-check-and-explain.md §3.)
///
/// <para><b>This verb answers a question that is not a failure.</b> <c>check</c> asks whether a
/// design is sound; <c>explain</c> asks what it RESOLVED TO — which technology a layout landed on and
/// through which workspace, which chain <c>SelectTop</c> would dispatch, what an expression evaluates
/// to, where a relative reference points. That question is asked constantly while authoring, and
/// before this verb the only way to answer it was to run something and read stderr.</para>
///
/// <para><b>The walk is reported, not just the answer</b> (R-aut4-7). A <c>.cem</c>'s layout walk and
/// its technology walk start from different files and can legitimately land on different workspaces
/// (<c>cli.md</c> §8.1) — that is deliberate, and it is exactly the thing a caller cannot otherwise
/// see. So every resolution comes back as a step: what was being resolved, from where, to what, by
/// which rule.</para>
///
/// <para><b>Nothing is guessed and nothing falls back silently</b> (R-aut4-8). Where resolution
/// fails, the failure IS the answer, reported as a diagnostic naming what was looked for and where it
/// was looked — because a caller reaches for this verb precisely when something did not resolve.</para>
/// </summary>
internal static class Explain
{
    /// <summary>
    /// The three analysis kinds that go through chain selection, with the arguments the run verbs
    /// themselves pass. Shared with <see cref="Check"/> so neither grows its own copy — and named
    /// here rather than in <c>Program.cs</c> because <c>Program.cs</c> is top-level statements and
    /// nothing outside it can reach a local.
    ///
    /// <para><c>sparam</c> and <c>dc</c> are deliberately absent: neither uses <c>SelectTop</c> —
    /// <c>sparam</c> takes the first typed <c>SParameterAnalysis</c> and <c>dc</c> needs no
    /// directive at all — so listing them here would claim a promotion rule they do not have. They
    /// still appear in the report, as declared analyses no chain selection applies to.</para>
    /// </summary>
    public static readonly (Func<Analysis, bool> IsBase, string Label, string Hint, string Verb)[] AnalysisKinds =
    [
        (a => a is HarmonicBalanceAnalysis,  "HB",               "analysis <name> type=hb ...",                "hb"),
        (a => a is LoadpullAnalysis,         "loadpull",         "analysis <name> type=loadpull ...",          "lp"),
        (a => a is LoadpullPursuitAnalysis,  "loadpull-pursuit", "analysis <name> type=loadpull_pursuit ...",  "lpp"),
    ];

    public static int Run(string[] args)
    {
        string? path = null, expr = null, reference = null, analysisName = null;
        bool wantAnalyses = false, wantCells = false, wantLayers = false, wantExtents = false, all = false;
        ViewType? askedView = null;
        var sets = new List<(string Name, string Expr)>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                // RND-3's three. They are OPTIONS and not verbs for R-rnd3-1's reason, and they join
                // the one-question rule below rather than getting an exception from it (R-rnd3-2).
                case "--cells":   wantCells   = true; continue;
                case "--layers":  wantLayers  = true; continue;
                case "--extents": wantExtents = true; continue;
                case "--all":     all         = true; continue;
                case "--view" when i + 1 < args.Length:
                {
                    // Only ever a disambiguator for a cell folder, and spelled exactly as `render`
                    // spells it — the two verbs are used together and a second spelling of one idea is
                    // a trap.
                    string v = args[++i];
                    askedView = v.ToLowerInvariant() switch
                    {
                        "schematic" => ViewType.Schematic,
                        "symbol"    => ViewType.Symbol,
                        "layout"    => ViewType.Layout,
                        _           => null,
                    };
                    if (askedView is null)
                    {
                        JsonRun.Report(CliDiagnostics.ExplainViewUnknown(v));
                        return Usage();
                    }
                    continue;
                }
                case "--expr" when i + 1 < args.Length:
                    expr = args[++i];
                    continue;
                case "--ref" when i + 1 < args.Length:
                    reference = args[++i];
                    continue;
                case "--set" when i + 1 < args.Length:
                {
                    // The same override the run verbs take, applied the same way (cli.md §5): it
                    // REPLACES the variable in the netlist's own scope, so everything derived from
                    // it re-derives. That is the whole point of asking `explain` about it — "what
                    // does this expression become if I set Pavl_dbm to 0" is a question about the
                    // scope, and an override pushed anywhere else would answer a different one.
                    string kv = args[++i];
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) { JsonRun.Report(CliDiagnostics.SetMalformed("explain", kv)); return Usage(); }
                    sets.Add((kv[..eq].Trim(), kv[(eq + 1)..].Trim()));
                    continue;
                }
                case "--analysis":
                    wantAnalyses = true;
                    // The name is optional: `--analysis` alone lists every chain, `--analysis SW1`
                    // asks what would happen if SW1 were named to a run verb. A following token that
                    // starts with '-' is the next option, not a name.
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-')) analysisName = args[++i];
                    continue;
                default:
                    if (args[i].StartsWith('-'))
                    { JsonRun.Report(CliDiagnostics.ExplainUnknownOption(args[i])); return Usage(); }
                    if (path is not null)
                    { JsonRun.Report(CliDiagnostics.ExplainMultiplePaths()); return Usage(); }
                    path = args[i];
                    continue;
            }
        }

        if (path is null) { JsonRun.Report(CliDiagnostics.ExplainPathRequired()); return Usage(); }
        JsonRun.InputPath = path;

        int asked = (expr is null ? 0 : 1) + (reference is null ? 0 : 1) + (wantAnalyses ? 1 : 0)
                  + (wantCells ? 1 : 0) + (wantLayers ? 1 : 0) + (wantExtents ? 1 : 0);
        if (asked > 1) { JsonRun.Report(CliDiagnostics.ExplainOneQuestion()); return Usage(); }
        if (all && !wantCells) { JsonRun.Report(CliDiagnostics.ExplainAllNeedsCells()); return Usage(); }

        if (!File.Exists(path) && !Directory.Exists(path))
            return JsonRun.Fail(CliDiagnostics.ExplainPathNotFound(path));

        var kind  = DocumentKinds.Classify(path);
        var walks = new List<ResolutionStepJson>();
        int exit  = 0;

        IReadOnlyList<ExplainAnalysisJson>? analyses = null;
        ExplainExpressionJson?              value    = null;
        ExplainReferenceJson?               refRes   = null;
        IReadOnlyList<ExplainCellJson>?     cells    = null;
        ExplainLayersJson?                  layers   = null;
        ExplainExtentsJson?                 extents  = null;

        // The document's OWN resolution always runs, whatever was asked: "which workspace, which
        // technology" is context for every other answer, and a report that omitted it would leave a
        // caller unable to tell which process an expression or a rule was read against.
        switch (kind)
        {
            case DocumentKind.Layout:   ExplainLayout(path, walks); break;
            case DocumentKind.EmSetup:  exit |= ExplainEmSetup(path, walks); break;
            case DocumentKind.Netlist:
            case DocumentKind.Schematic:
            case DocumentKind.Cell:
            case DocumentKind.Workspace:
            case DocumentKind.Technology:
            case DocumentKind.Symbol:
            case DocumentKind.AssemblyRules:
            case DocumentKind.Folder:
                // Nothing but the workspace walk to report: none of these resolves a second
                // reference of its own. Reported anyway, because "which workspace" is the context
                // every other answer is read against.
                Workspace(path, walks);
                break;
            case DocumentKind.Interchange:
                walks.Add(new ResolutionStepJson(
                    "format", path, DocumentKinds.InterchangeFormat(path),
                    "content and extension, through `convert`'s own classifier"));
                break;
            case DocumentKind.Touchstone:
                exit |= ExplainTouchstone(path, walks);
                break;
            default:
                return JsonRun.Fail(CliDiagnostics.ExplainUnknownKind(path));
        }

        if (reference is not null)
        {
            var (resolved, refExit) = ExplainReference(path, reference);
            refRes = resolved;
            exit  |= refExit;
        }

        if (wantCells)
        {
            var (rows, cellExit) = ExplainQueries.Cells(path, kind, all);
            cells = rows.Count > 0 || cellExit == 0 ? rows : null;
            exit |= cellExit;
        }

        if (wantLayers)
        {
            var (report, layerExit) = ExplainQueries.Layers(path, kind, askedView, walks);
            layers = report;
            exit  |= layerExit;
        }

        if (wantExtents)
        {
            var (report, extentExit) = ExplainQueries.Extents(path, kind, askedView);
            extents = report;
            exit   |= extentExit;
        }

        if (expr is not null || wantAnalyses)
        {
            var circuit = ReadCircuit(path, kind);
            if (circuit is null)
            {
                exit |= JsonRun.Fail(CliDiagnostics.ExplainNotApplicable(
                    expr is not null ? "--expr" : "--analysis", DocumentKinds.Name(kind)));
            }
            else
            {
                var (lib, tb) = circuit.Value;

                foreach (var (name, expression) in sets)
                {
                    tb.GlobalVariables.RemoveAll(v => v.Name == name);
                    tb.GlobalVariables.Add(new Variable(name, expression));
                    Console.Error.WriteLine($"[circuitRF] set {name} = {expression}");
                }

                if (wantAnalyses)
                {
                    var (rows, analysisExit) = ExplainAnalyses(tb, analysisName);
                    analyses = rows;
                    exit    |= analysisExit;
                }
                if (expr is not null) exit |= ExplainExpression(lib, tb, expr, out value);
            }
        }

        JsonRun.Explain = new ExplainReportJson(
            path, DocumentKinds.Name(kind), walks, analyses, value, refRes, cells, layers, extents);

        Print(path, kind, walks, analyses, value, refRes, cells, layers, extents);
        return exit;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: circuitrf explain <path> [--expr \"<expression>\"] [--set var=expr]");
        Console.Error.WriteLine("                            [--analysis [<name>]] [--ref <relative-ref>]");
        Console.Error.WriteLine("                            [--cells [--all]] [--layers] [--extents] [--view <name>]");
        return 1;
    }

    // ── a Touchstone file ────────────────────────────────────────────────────

    /// <summary>
    /// <c>explain part.s2p</c> — what IS this part?
    ///
    /// <para><b>Why this belongs to <c>explain</c> and not to <c>check</c>.</b> The two verbs split
    /// on soundness versus decision, and "is this file passive, sorted and causal" is soundness
    /// while "what is its self-resonance and how many milliohms is it there" is not a defect at
    /// all — it is what the file SAYS. A designer holding a vendor's part file wants the second
    /// question answered far more often than the first, and before this there was no way to ask it
    /// but to build a schematic around the file and plot it.</para>
    ///
    /// <para><b>Every applicable fixture is reported, side by side, rather than one being picked.</b>
    /// Nothing in Touchstone records how the part was measured, and the readings differ by orders of
    /// magnitude — so choosing one silently would be exactly the failure this feature exists to
    /// prevent. Seeing all of them is also the fastest way to identify an unlabelled file: only the
    /// physical reading gives a sensible self-resonance and a milliohm-scale ESR, and the others are
    /// obviously nonsense next to it.</para>
    /// </summary>
    private static int ExplainTouchstone(string path, List<ResolutionStepJson> walks)
    {
        SNP snp;
        try { snp = TouchstoneIO.ReadFile(path, readComments: false); }
        catch (Exception ex) { return JsonRun.Fail(CliDiagnostics.CheckUnreadable(path, ex.Message)); }

        var h = TouchstoneHealth.Analyze(snp);

        walks.Add(new ResolutionStepJson("ports", path, h.Ports.ToString(),
            "the `.sNp` extension, cross-checked against the data block"));
        walks.Add(new ResolutionStepJson("sweep", path,
            h.FrequencyCount == 0 ? null : $"{h.FrequencyCount} points, {Check.Hz(h.FirstFrequencyHz)} to {Check.Hz(h.LastFrequencyHz)}"
            + (h.GridUniform ? ", uniform" : ", non-uniform"),
            "the file's own frequency column, in the unit its option line declares"));
        walks.Add(new ResolutionStepJson("reference impedance", path, Check.Ohms(h.Z0),
            "the `R` field of the option line"));

        if (h.FrequencyCount == 0 || h.Ports == 0) return 0;

        var z0   = Enumerable.Repeat(snp.Z0, snp.Ports).ToArray();
        var mats = snp.Matrices;

        var fixtures = h.Ports >= 2
            ? new[] { PassiveExtraction.ShuntThrough, PassiveExtraction.SeriesThrough }
            : [PassiveExtraction.OnePort];

        foreach (var mode in fixtures)
        {
            Complex[] z;
            try { z = PassiveMetrics.Impedance(mats, z0, mode, 1, Math.Min(2, h.Ports)); }
            catch (ArgumentException) { continue; }

            string how = mode switch
            {
                PassiveExtraction.ShuntThrough  => "the SHUNT-through reading, Z = (Z0/2)·S21/(1−S21)",
                PassiveExtraction.SeriesThrough => "the SERIES-through reading, Z = 2·Z0·(1−S21)/S21",
                _                               => "the 1-port reflection reading, Z = Z0·(1+S11)/(1−S11)",
            };
            string label = mode switch
            {
                PassiveExtraction.ShuntThrough  => "shunt-through",
                PassiveExtraction.SeriesThrough => "series-through",
                _                               => "1-port",
            };

            // The self-resonance and the impedance floor beside it: the two numbers a decoupling
            // capacitor is chosen on, and the pair that says at a glance whether this fixture is
            // the physical reading of the file.
            var srf = PassiveMetrics.SelfResonance(snp.Frequencies, z);
            walks.Add(new ResolutionStepJson(
                $"SRF ({label})", path,
                srf is { } f ? Check.Hz(f) : null,
                srf is null
                    ? how + " — no capacitive-to-inductive reactance crossing inside this sweep"
                    : how + ", lowest capacitive-to-inductive reactance crossing"));

            int iMin = -1;
            double best = double.PositiveInfinity;
            for (int i = 0; i < z.Length; i++)
            {
                double m = z[i].Magnitude;
                if (double.IsFinite(m) && m < best) { best = m; iMin = i; }
            }
            if (iMin >= 0)
                walks.Add(new ResolutionStepJson(
                    $"|Z| min ({label})", path,
                    $"|Z| = {Ohm(best)} at {Check.Hz(snp.Frequencies[iMin])}, ESR {Ohm(z[iMin].Real)}",
                    how + " — the sweep's minimum |Z| and the resistance there"));
        }

        return 0;
    }

    /// <summary>An impedance with an SI prefix, because a decoupling capacitor's floor is
    /// milliohms and a series capacitor's is megohms, in the same report.</summary>
    private static string Ohm(double r) =>
        !double.IsFinite(r)       ? "?"
        : Math.Abs(r) >= 1e6      ? $"{r / 1e6:0.###} MΩ"
        : Math.Abs(r) >= 1e3      ? $"{r / 1e3:0.###} kΩ"
        : Math.Abs(r) >= 1        ? $"{r:0.###} Ω"
        : Math.Abs(r) >= 1e-3     ? $"{r * 1e3:0.###} mΩ"
        :                           $"{r * 1e6:0.###} µΩ";

    // ── the walks ────────────────────────────────────────────────────────────

    /// <summary>The ancestor-workspace walk every document-relative reference resolves through.</summary>
    private static string? Workspace(string path, List<ResolutionStepJson> walks)
    {
        string full = Path.GetFullPath(path);
        string? cws = DocumentKinds.AncestorCws(full);
        walks.Add(new ResolutionStepJson(
            "workspace", full, cws,
            cws is null
                ? "nearest ancestor .cws — none found, so references resolve against the document's own directory"
                : "nearest ancestor .cws"));
        return cws;
    }

    private static void ExplainLayout(string path, List<ResolutionStepJson> walks)
    {
        string full = Path.GetFullPath(path);
        Workspace(path, walks);

        LayoutView? view = null;
        try { view = LayoutPersistence.LoadFromFile(full); }
        catch (Exception ex) { JsonRun.Report(CliDiagnostics.ExplainUnreadable(path, ex.Message)); }

        if (view is null) return;

        var (tech, ownCws) = TechnologyResolver.ResolveForDocument(
            view.TechRef, full, null, new TechnologyCache());

        // WHICH workspace resolved the technology, said separately from which workspace the document
        // belongs to. On a layout they are the same walk; on a `.cem` they are not, and reporting
        // them the same way in both places is what makes the difference visible.
        walks.Add(new ResolutionStepJson(
            "technology", view.TechRef ?? ownCws, tech.ResolvedPath,
            tech.Source switch
            {
                TechResolutionSource.LayoutRef        => "the layout's own TechRef, relative to its own directory",
                TechResolutionSource.WorkspaceDefault => "the workspace's DefaultTechRef — the layout states none",
                _                                     => "nothing resolved: no TechRef and no workspace default",
            }));

        foreach (var d in tech.Diagnostics)
            JsonRun.Report(CliDiagnostics.CheckResolverNote(path, d));
    }

    /// <summary>
    /// A <c>.cem</c>'s two walks, reported as two — which is the whole point of the option
    /// (<c>cli.md</c> §8.1). Resolution goes through <c>EmSetupResolver.Resolve</c> itself, so what
    /// is reported here is what <c>circuitrf em</c> actually used (Gate 5).
    /// </summary>
    private static int ExplainEmSetup(string path, List<ResolutionStepJson> walks)
    {
        string full = Path.GetFullPath(path);
        string? cws = Workspace(path, walks);

        EmSetup setup;
        try { setup = EmSetupPersistence.LoadFromFile(full); }
        catch (Exception ex) { return JsonRun.Fail(CliDiagnostics.ExplainUnreadable(path, ex.Message)); }

        var resolution = EmSetupResolver.Resolve(full, setup.LayoutRef, cws, new TechnologyCache());

        walks.Add(new ResolutionStepJson(
            "layout", setup.LayoutRef.Length > 0 ? setup.LayoutRef : null, resolution.LayoutPath,
            cws is null
                ? "relative to the .cem's own directory — no ancestor workspace"
                : "relative to the .cem's ancestor workspace"));

        // The technology walk starts from the LAYOUT, not from the .cem — so its own ancestor
        // workspace is the one that resolves it, and it may not be the one above.
        string? layoutCws = resolution.LayoutPath is { } lp ? DocumentKinds.AncestorCws(lp) : null;
        walks.Add(new ResolutionStepJson(
            "technology", layoutCws ?? resolution.LayoutPath, resolution.TechnologyPath,
            "resolved against the LAYOUT's own ancestor workspace, which need not be the .cem's"));

        foreach (var d in resolution.Diagnostics)
            JsonRun.Report(CliDiagnostics.CheckResolverNote(path, d));

        return resolution.Source is null ? 1 : 0;
    }

    // ── --ref ────────────────────────────────────────────────────────────────

    private static (ExplainReferenceJson?, int) ExplainReference(string path, string reference)
    {
        // A reference is relative to the DOCUMENT'S OWN directory, which for a cell folder or a
        // workspace is the folder itself. Anything else would answer a question about a different
        // base than the one the file's references are written against.
        string from = Directory.Exists(path)
            ? Path.GetFullPath(path)
            : Path.GetDirectoryName(Path.GetFullPath(path))!;

        var res = CellSymbolResolver.Resolve(reference, from);
        string? resolved = ExternalCellRef.ResolveCellDir(reference, from);

        string? workspaceRoot = WorkspaceRootFinder.FindAncestorCws(from) is { } cws
            ? Path.GetDirectoryName(cws)
            : null;

        bool? outside = resolved is not null && workspaceRoot is not null
            ? WorkspaceRootFinder.IsOutside(resolved, workspaceRoot)
            : null;

        string state = res.State switch
        {
            CellSymbolState.Resolved       => "resolved",
            CellSymbolState.PrimaryMissing => "primary-missing",
            _                              => "not-found",
        };

        int exit = 0;
        if (res.State == CellSymbolState.NotFound)
            exit = JsonRun.Fail(CliDiagnostics.ExplainRefNotFound(reference, from));
        else if (res.State == CellSymbolState.PrimaryMissing)
            exit = JsonRun.Fail(CliDiagnostics.ExplainRefPrimaryMissing(reference, resolved ?? "?"));

        return (new ExplainReferenceJson(
            reference, from, resolved, state, outside, res.Redirect?.To), exit);
    }

    // ── --analysis ───────────────────────────────────────────────────────────

    private static (IReadOnlyList<ExplainAnalysisJson>, int) ExplainAnalyses(
        TestBench tb, string? requested)
    {
        int exit = 0;

        if (requested is not null
            && !tb.Analyses.Any(a => a.Name.Equals(requested, StringComparison.OrdinalIgnoreCase)))
        {
            exit = JsonRun.Fail(CliDiagnostics.ExplainAnalysisNotFound(
                requested,
                tb.Analyses.Count > 0 ? string.Join(", ", tb.Analyses.Select(a => a.Name)) : "(none)"));
        }

        // One selection per kind, through the function the run verbs select with. A netlist can
        // declare an HB chain AND a loadpull chain, and each verb dispatches its own — so
        // "dispatched" is per verb rather than a single winner.
        var dispatched = new Dictionary<string, string>(StringComparer.Ordinal);   // analysis → verb
        var promoted   = new Dictionary<string, string>(StringComparer.Ordinal);   // analysis → promoted-from
        var reasons    = new List<string>();

        foreach (var (isBase, label, hint, verb) in AnalysisKinds)
        {
            // A kind the netlist declares NONE of is not a finding — chain selection is per kind, so
            // a bench declaring only an S-parameter sweep genuinely has no HB chain and saying so
            // three times would warn about every S-parameter and DC document there is.
            if (!tb.Analyses.Any(isBase)) continue;

            var sel = ChainSelector.Select(tb, requested, isBase, label, hint);

            // A NAMED analysis comes back from chain selection even when it is not of this verb's
            // kind — `SelectTop` hands back `owner ?? named`, so `lp -a HB1` returns HB1. Reporting
            // that as "lp dispatches HB1" would be a claim about a run that cannot happen, so the
            // selection is counted only when the chain it picked bottoms out in this verb's own
            // base analysis. The refusal a caller would actually get is `lp`'s own.
            if (sel.Selected is { } top
                && ChainSelector.BaseOfChain(top, tb) is { } base_ && isBase(base_))
            {
                dispatched[top.Name] = verb;
                if (sel.PromotedFrom is { } from) promoted[top.Name] = from.Name;
            }
            else if (sel.Why is { } why) reasons.Add(why);
        }

        if (!CircuitSource.DeclaresARunnableAnalysis(tb))
            JsonRun.Report(CliDiagnostics.ExplainNoRunnableAnalysis("The document declares no analysis."));
        else if (reasons.Count > 0)
            JsonRun.Report(CliDiagnostics.ExplainNoRunnableAnalysis(string.Join(" ", reasons)));

        var inner = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in tb.Analyses)
            if (a is ParametricSweepAnalysis ps && !string.IsNullOrEmpty(ps.InnerAnalysisName))
                inner.Add(ps.InnerAnalysisName);

        var rows = new List<ExplainAnalysisJson>(tb.Analyses.Count);
        foreach (var a in tb.Analyses)
        {
            var chain = new List<string>();
            Analysis? walk = a;
            for (int guard = 0; walk is not null && guard < 64; guard++)
            {
                chain.Add(walk.Name);
                if (walk is not ParametricSweepAnalysis ps) break;
                walk = AnalysisChain.ResolveEffectiveInner(ps.InnerAnalysisName, tb);
            }

            var top = AnalysisChain.ResolveEffectiveTop(a, tb);
            rows.Add(new ExplainAnalysisJson(
                a.Name,
                KindOf(a),
                a.Enabled,
                top is not null && AnalysisChain.IsChainRunnable(top, tb),
                !inner.Contains(a.Name),
                chain,
                dispatched.ContainsKey(a.Name),
                dispatched.TryGetValue(a.Name, out var byVerb) ? byVerb : null,
                promoted.TryGetValue(a.Name, out var from) ? from : null,
                SweepOf(a)));
        }

        return (rows, exit);
    }

    private static string KindOf(Analysis a) => a switch
    {
        DcAnalysis               => "dc",
        SParameterAnalysis       => "sparam",
        HarmonicBalanceAnalysis  => "hb",
        LoadpullAnalysis         => "loadpull",
        LoadpullPursuitAnalysis  => "loadpull-pursuit",
        ParametricSweepAnalysis  => "parametric-sweep",
        _                        => a.GetType().Name,
    };

    /// <summary>
    /// A sweep's resolved axis, in BASE SI, with the unit it was stated in and the scale that got it
    /// there (R-aut4-9). Reading a unit's mark without its scale has already produced a sweep that
    /// ran at 2 Hz and looked entirely normal, and that class of error is invisible without a run
    /// unless the numbers are shown next to both.
    ///
    /// <para><c>SweepValues</c> is the authority, not the spec — the spec's coefficients are in the
    /// stated unit and the array is what the engine actually steps through.</para>
    /// </summary>
    private static ExplainSweepJson? SweepOf(Analysis a)
    {
        if (a is not ParametricSweepAnalysis ps || ps.SweepValues.Length == 0) return null;

        string stated = ps.Spec?.Unit ?? "";
        double scale  = stated.Length == 0 ? 1.0 : Units.Scale(stated) ?? 1.0;

        // Only a step-SIZE sweep has a step. A point-count or explicit-list sweep does not, and a
        // step computed from the endpoints would be an invention — wrong outright on a log sweep.
        double? step = ps.Spec is { Mode: SweepAxisMode.StepSize } spec
            ? spec.StepOrCount * scale
            : null;

        return new ExplainSweepJson(
            ps.SweepVarName,
            ps.SweepValues.Length,
            ps.SweepValues[0],
            ps.SweepValues[^1],
            step,
            stated.Length == 0 ? "" : Units.BaseUnit(stated),
            stated,
            scale,
            (ps.Spec?.Kind ?? SweepKind.Linear).ToString().ToLowerInvariant());
    }

    // ── --expr ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates in the design's OWN resolved scope, through the one expression engine — never by
    /// substitution (<c>docs/design/expressions.md</c>). Elaboration runs first because that is what
    /// builds the scope, binds the ambient the design may not state, and applies every
    /// <c>--set</c>-style replacement that already landed in <c>GlobalVariables</c>.
    /// </summary>
    private static int ExplainExpression(
        Library lib, TestBench tb, string expression, out ExplainExpressionJson? value)
    {
        value = null;

        var elaborator = new Elaborator(lib);
        ElaboratedNetlist nl;
        try { nl = elaborator.Elaborate(tb); }
        catch (Exception ex)
        { return JsonRun.Fail(CliDiagnostics.ExplainExpressionFailed(expression, ex.Message)); }

        using (nl)
        {
            Value v;
            try { v = elaborator.EvaluateInGlobalScope(expression); }
            catch (Exception ex)
            { return JsonRun.Fail(CliDiagnostics.ExplainExpressionFailed(expression, ex.Message)); }

            value = new ExplainExpressionJson(
                expression,
                v.Kind.ToString().ToLowerInvariant(),
                v.ToString(),
                v.Kind == ValueKind.Real    ? v.AsReal() : null,
                v.Kind == ValueKind.Complex ? [v.AsComplex().Real, v.AsComplex().Imaginary] : null,
                v.Kind == ValueKind.Bool    ? v.AsBool() : null);
        }

        return 0;
    }

    // ── reading the circuit ──────────────────────────────────────────────────

    /// <summary>
    /// The circuit a document holds, read the way the application reads it — including a `.csch`'s
    /// `.cnl` round trip, which is not cosmetic (see <see cref="CircuitSource"/>). Reported through
    /// this verb's own diagnostic when the read fails.
    /// </summary>
    private static (Library Lib, TestBench Tb)? ReadCircuit(string path, DocumentKind kind)
        => CircuitSource.Read(path, kind,
               message => JsonRun.Report(CliDiagnostics.ExplainUnreadable(path, message)));

    // ── the human report ─────────────────────────────────────────────────────

    private static void Print(
        string path, DocumentKind kind,
        IReadOnlyList<ResolutionStepJson> walks,
        IReadOnlyList<ExplainAnalysisJson>? analyses,
        ExplainExpressionJson? value,
        ExplainReferenceJson? reference,
        IReadOnlyList<ExplainCellJson>? cells = null,
        ExplainLayersJson? layers = null,
        ExplainExtentsJson? extents = null)
    {
        Console.WriteLine($"{path}  ({DocumentKinds.Name(kind)})");

        foreach (var w in walks)
        {
            Console.WriteLine($"  {w.Step,-12} {w.Resolved ?? "(nothing)"}");
            // The RULE, always — the walk is the answer as much as the destination is (R-aut4-7),
            // and it is the half a caller cannot reconstruct from the path alone.
            Console.WriteLine($"  {"",-12} via {w.How}");
        }

        if (reference is not null)
        {
            Console.WriteLine($"  reference    '{reference.Ref}' from {reference.From}");
            Console.WriteLine($"  {"",-12} {reference.State}" +
                              (reference.ResolvedPath is { } r ? $" → {r}" : "") +
                              (reference.OutsideWorkspace == true ? "  (outside its workspace)" : ""));
            if (reference.Redirect is { } moved)
                Console.WriteLine($"  {"",-12} via a recorded move to '{moved}'");
        }

        if (value is not null)
            Console.WriteLine($"  {value.Expression} = {value.Text}   ({value.Kind})");

        if (cells is not null)
        {
            Console.WriteLine($"  cells: {cells.Count}");
            foreach (var c in cells)
            {
                Console.WriteLine($"    {c.Name,-20} {c.Folder}"
                                  + (c.OutsideWorkspace == true ? "  (outside its workspace)" : "")
                                  + (c.Generated == true ? "  (generated)" : ""));
                foreach (var v in c.Views)
                {
                    // A view that does not exist is a row too — "this cell has no symbol" is an answer
                    // a caller composing with `render --view` needs, and an omitted row reads as an
                    // oversight rather than as a fact.
                    Console.WriteLine($"      {v.Type,-10} {v.State,-22} {v.Primary ?? "(none)"}"
                                      + (v.Candidates.Count > 1 ? $"   of {v.Candidates.Count}: {string.Join(", ", v.Candidates)}" : ""));
                    if (v.Defect is { } d) Console.WriteLine($"      {"",-10} defect: {d}");
                }
            }
        }

        if (layers is not null)
        {
            // No heading here: the `layers` walk step above already named the technology, where it
            // came from and how it was found. Repeating it would be the one thing R-aut4-7's walk
            // format exists to avoid — the same fact twice, in two wordings.
            foreach (var l in layers.Layers)
                Console.WriteLine(
                    $"    {l.Name,-20} {l.Number}/{l.Datatype,-6} {l.Color}  {l.Fill,-10}"
                    + (l.Visible ? "" : " hidden") + (l.Selectable ? "" : " locked")
                    + (l.Purpose is { } p ? $"  purpose={p}" : "")
                    + (l.Shapes is { } n ? $"   {n} shape(s)" : "")
                    + (l.InstancesUsing is > 0 and { } u ? $", {u} instance(s)" : ""));
            if (layers.Truncated == true)
                Console.WriteLine("    (counts are a floor — the hierarchy is larger than this walk)");
        }

        if (extents is not null)
        {
            if (extents.Empty)
                Console.WriteLine($"  extents      (empty)   unit {extents.Unit}, scale {extents.Scale:G6}");
            else
                Console.WriteLine(
                    $"  extents      {extents.X0:G6} {extents.Y0:G6} .. {extents.X1:G6} {extents.Y1:G6}"
                    + $"  ({extents.Width:G6} x {extents.Height:G6} {extents.Unit}, scale {extents.Scale:G6})");
            if (extents.Note is { } en) Console.WriteLine($"  {"",-12} note: {en}");
            foreach (var pl in extents.PerLayer ?? [])
                Console.WriteLine($"    {pl.Name,-20} {pl.X0:G6} {pl.Y0:G6} .. {pl.X1:G6} {pl.Y1:G6}");
        }

        if (analyses is not null)
        {
            Console.WriteLine("  analyses:");
            foreach (var a in analyses)
            {
                string flags = string.Join(" ", new[]
                {
                    a.Enabled    ? "" : "disabled",
                    a.Runnable   ? "" : "not-runnable",
                    a.IsRoot     ? "" : "inner",
                    a.Dispatched ? $"DISPATCHED by {a.DispatchedBy}" : "",
                }.Where(s => s.Length > 0));

                Console.WriteLine($"    {a.Name,-16} {a.Kind,-18} {string.Join(" > ", a.Chain),-28} {flags}");

                if (a.PromotedFrom is { } from)
                    Console.WriteLine($"      promoted from '{from}' — naming the inner analysis would lose the sweep axis");

                if (a.Sweep is { } s)
                    Console.WriteLine(
                        $"      sweep {s.Variable}: {s.Start:G6} .. {s.Stop:G6}" +
                        (s.Step is { } st ? $" step {st:G6}" : "") +
                        $" {s.BaseUnit} ({s.Points} pts, {s.Kind}" +
                        (s.StatedUnit.Length > 0 ? $", stated in {s.StatedUnit} ×{s.Scale:G6}" : "") + ")");
            }
        }
    }
}
