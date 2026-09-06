using CircuitRF.Core.Design;
using CircuitRF.Core.Elaboration;
using CircuitRF.Core.Netlist;
using CircuitRF.Design.Cells;
using CircuitRF.Design.Layout;
using CircuitRF.Design.Layout.Assembly;
using CircuitRF.Design.Layout.Drc;
using CircuitRF.Design.Layout.Em;
using CircuitRF.Design.Schematic;
using CircuitRF.Design.Workspace;
using CircuitRF.Diagnostics;
using RfCore.Export;

namespace CircuitRF.Cli;

/// <summary>
/// <c>circuitrf check &lt;path&gt; [--recursive] [--severity warning|error]</c> — is this design well
/// formed, does it resolve, and is it sound? (brief-automation-4-check-and-explain.md §2.)
///
/// <para><b>This file writes no validation logic and that is the whole design.</b> R-aut4-2: the
/// repo is full of validators and they are scattered rather than missing, so what is new here is the
/// WALK and the reporting — which validator gets pointed at which file, and how its finding becomes
/// a <see cref="Diagnostic"/> with a stable id and the producing validator's own typed values. A
/// rule that existed only in <c>check</c> would be a rule the GUI does not enforce, which is worse
/// than no rule at all: a design would pass here and be refused when someone opened it.</para>
///
/// <para><b>It runs no analysis</b> (R-aut4-1). Elaboration is as far as it goes — that is what
/// answers "do the parameters, expressions and cycles resolve?" — and nothing here solves a matrix.
/// A check that needed a solve to answer belongs in the run verbs' own warnings.</para>
///
/// <para><b>It never writes</b> (R-aut4-6). Not a repair, not a re-save, not a cache file: a caller
/// must be able to run it on a read-only tree and on a workspace another process has open. The two
/// places that would otherwise be tempting are the technology cache (in memory, per invocation) and
/// the DRC waiver store (read from the <c>.clay</c>, never written back).</para>
/// </summary>
internal static class Check
{
    /// <summary>The severity at which a finding decides the exit code (R-aut4-5).</summary>
    private enum Threshold { Warning, Error }

    private sealed class Tally
    {
        public int Errors, Warnings, Notes;

        public void Count(DiagnosticSeverity s)
        {
            switch (s)
            {
                case DiagnosticSeverity.Error:   Errors++;   break;
                case DiagnosticSeverity.Warning: Warnings++; break;
                default:                         Notes++;    break;
            }
        }
    }

    /// <summary>
    /// The one place a finding is recorded. Everything goes to stderr — <c>check</c>'s findings are
    /// not a RESULT, they are what the tool has to say about a design (<c>cli.md</c> §3.1), and
    /// stdout carries the summary the way every other verb's stdout carries its table.
    /// </summary>
    private sealed class Findings
    {
        public readonly Tally Total = new();
        private readonly List<CheckedDocumentJson> _documents = [];
        private Tally _current = new();
        private string _currentPath = "";
        private DocumentKind _currentKind = DocumentKind.Unknown;
        private bool _open;

        public IReadOnlyList<CheckedDocumentJson> Documents => _documents;
        public int Count => _documents.Count;

        public void Begin(string path, DocumentKind kind)
        {
            End();
            _currentPath = path;
            _currentKind = kind;
            _current     = new Tally();
            _open        = true;
        }

        public void End()
        {
            if (!_open) return;
            _documents.Add(new CheckedDocumentJson(
                _currentPath, DocumentKinds.Name(_currentKind), _current.Errors, _current.Warnings));
            _open = false;
        }

        public void Add(Diagnostic d)
        {
            Total.Count(d.Severity);
            if (_open) _current.Count(d.Severity);

            Console.Error.WriteLine(d.Severity switch
            {
                DiagnosticSeverity.Error   => "error: "   + d.Render(),
                DiagnosticSeverity.Warning => "warning: " + d.Render(),
                _                          => "note: "    + d.Render(),
            });
            JsonRun.Note(d);
        }
    }

    public static int Run(string[] args)
    {
        string? path = null;
        bool recursive = false;
        var threshold = Threshold.Error;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--recursive" or "-r":
                    recursive = true;
                    continue;
                case "--severity" when i + 1 < args.Length:
                    switch (args[++i].ToLowerInvariant())
                    {
                        case "warning": threshold = Threshold.Warning; break;
                        case "error":   threshold = Threshold.Error;   break;
                        default: return JsonRun.Fail(CliDiagnostics.CheckUnknownSeverity(args[i]));
                    }
                    continue;
                default:
                    if (args[i].StartsWith('-'))
                    { JsonRun.Report(CliDiagnostics.CheckUnknownOption(args[i])); return Usage(); }
                    if (path is not null)
                    { JsonRun.Report(CliDiagnostics.CheckMultiplePaths()); return Usage(); }
                    path = args[i];
                    continue;
            }
        }

        if (path is null) { JsonRun.Report(CliDiagnostics.CheckPathRequired()); return Usage(); }
        JsonRun.InputPath = path;

        if (!File.Exists(path) && !Directory.Exists(path))
            return JsonRun.Fail(CliDiagnostics.CheckPathNotFound(path));

        var findings = new Findings();
        var cache    = new TechnologyCache();

        CheckPath(path, DocumentKinds.Classify(path), findings, cache, recursive);
        findings.End();

        int exit = threshold == Threshold.Error
            ? (findings.Total.Errors > 0 ? 1 : 0)
            : (findings.Total.Errors > 0 || findings.Total.Warnings > 0 ? 1 : 0);

        JsonRun.Check = new CheckReportJson(
            path,
            threshold == Threshold.Error ? "error" : "warning",
            findings.Count,
            findings.Total.Errors, findings.Total.Warnings, findings.Total.Notes,
            findings.Documents);

        Console.WriteLine(
            $"{findings.Count} document(s) checked: " +
            $"{findings.Total.Errors} error(s), {findings.Total.Warnings} warning(s), " +
            $"{findings.Total.Notes} note(s).");

        return exit;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage: circuitrf check <path> [--recursive] [--severity warning|error]");
        return 1;
    }

    // ── the walk ─────────────────────────────────────────────────────────────

    private static void CheckPath(
        string path, DocumentKind kind, Findings f, TechnologyCache cache, bool recursive)
    {
        switch (kind)
        {
            case DocumentKind.Workspace:  CheckWorkspace(path, f, cache); break;
            case DocumentKind.Cell:       CheckCell(path, f, cache);      break;
            case DocumentKind.Folder:     CheckFolder(path, f, cache, recursive); break;

            case DocumentKind.Schematic:  Scoped(path, kind, f, () => CheckSchematic(path, f)); break;
            case DocumentKind.Symbol:     Scoped(path, kind, f, () => CheckViewFile(path, ViewType.Symbol, f)); break;
            case DocumentKind.Layout:     Scoped(path, kind, f, () => CheckLayout(path, f, cache)); break;
            case DocumentKind.Technology: Scoped(path, kind, f, () => CheckTechnology(path, f)); break;
            case DocumentKind.EmSetup:    Scoped(path, kind, f, () => CheckEmSetup(path, f, cache)); break;
            case DocumentKind.Netlist:    Scoped(path, kind, f, () => CheckNetlist(path, f)); break;
            case DocumentKind.AssemblyRules:
                                          Scoped(path, kind, f, () => CheckAssemblyRules(path, f)); break;

            case DocumentKind.Interchange:
                f.Begin(path, kind);
                f.Add(CliDiagnostics.CheckInterchangeNotADocument(
                    path, DocumentKinds.InterchangeFormat(path) ?? "foreign"));
                break;

            default:
                f.Begin(path, kind);
                f.Add(CliDiagnostics.CheckUnknownKind(path));
                break;
        }
    }

    private static void Scoped(string path, DocumentKind kind, Findings f, Action body)
    {
        f.Begin(path, kind);
        body();
        f.End();
    }

    /// <summary>
    /// A workspace checks everything under it (§2). <c>--recursive</c> is what a plain FOLDER needs;
    /// a workspace implies it, because "check my workspace" that stopped at the top level would be
    /// answering a question nobody asks.
    /// </summary>
    private static void CheckWorkspace(string path, Findings f, TechnologyCache cache)
    {
        string cwsPath = Directory.Exists(path) ? Path.Combine(path, DocumentKinds.CwsFileName) : path;
        string root    = Path.GetDirectoryName(Path.GetFullPath(cwsPath))!;

        f.Begin(cwsPath, DocumentKind.Workspace);

        CwsFile? cws = null;
        try { cws = WorkspacePersistence.LoadFromFile(cwsPath); }
        catch (Exception ex) { f.Add(CliDiagnostics.CheckUnreadable(cwsPath, ex.Message)); }

        if (cws is not null)
        {
            // The three reference lists a `.cws` carries. Each is exactly what the project tree
            // already shows as a warning node when it does not resolve — same finding, no new rule.
            if (cws.DefaultTechRef is { Length: > 0 } techRef
                && !File.Exists(Path.Combine(root, techRef)))
                f.Add(CliDiagnostics.CheckWorkspaceRefUnresolved(cwsPath, "default technology", techRef));

            foreach (var lib in cws.LibraryRefs)
                if (!Directory.Exists(Resolve(root, lib)) && !File.Exists(Resolve(root, lib)))
                    f.Add(CliDiagnostics.CheckWorkspaceRefUnresolved(cwsPath, "library", lib));

            foreach (var known in cws.KnownFiles)
                if (!File.Exists(Resolve(root, known)) && !Directory.Exists(Resolve(root, known)))
                    f.Add(CliDiagnostics.CheckWorkspaceRefUnresolved(cwsPath, "known file", known));
        }

        f.End();
        WalkFolder(root, f, cache, recursive: true);

        static string Resolve(string root, string reference) =>
            Path.IsPathRooted(reference) ? reference : Path.Combine(root, reference);
    }

    private static void CheckFolder(string path, Findings f, TechnologyCache cache, bool recursive)
        => WalkFolder(path, f, cache, recursive);

    private static void WalkFolder(string dir, Findings f, TechnologyCache cache, bool recursive)
    {
        foreach (var file in Enumerate(() => Directory.EnumerateFiles(dir), f, dir).Order(StringComparer.Ordinal))
        {
            // By NAME only: a walk must not open every foreign file to discover it is not Gerber.
            // The content sniff is right when a caller named the path, and it is what the
            // Interchange arm below relies on — but it is answered there, not here.
            var kind = DocumentKinds.Classify(file, contentSniff: false);
            // A folder walk reports what it RECOGNISES. Anything else is somebody's `.md`, `.s2p` or
            // `.DS_Store`, and a walk that called each of those an unknown kind would drown its own
            // findings — which is exactly the failure mode that makes a check stop being run.
            if (kind is DocumentKind.Unknown or DocumentKind.Interchange or DocumentKind.Workspace) continue;
            CheckPath(file, kind, f, cache, recursive);
        }

        foreach (var sub in Enumerate(() => Directory.EnumerateDirectories(dir), f, dir).Order(StringComparer.Ordinal))
        {
            var kind = DocumentKinds.Classify(sub);
            if (kind == DocumentKind.Cell) { CheckCell(sub, f, cache); continue; }

            // A nested workspace is somebody else's tree. It is walked when it is what was asked for,
            // never dragged in by an enclosing folder — the two have different default technologies
            // and a finding attributed to the wrong one is worse than no finding.
            if (kind == DocumentKind.Workspace) continue;
            if (recursive) WalkFolder(sub, f, cache, recursive);
        }
    }

    /// <summary>An unreadable directory is a finding, not a crash — a check pointed at a tree it
    /// cannot fully read must still report what it did read.</summary>
    private static IEnumerable<string> Enumerate(Func<IEnumerable<string>> get, Findings f, string dir)
    {
        try { return [.. get()]; }
        catch (Exception ex) { f.Add(CliDiagnostics.CheckUnreadable(dir, ex.Message)); return []; }
    }

    // ── one cell folder ──────────────────────────────────────────────────────

    private static void CheckCell(string cellDir, Findings f, TechnologyCache cache)
    {
        f.Begin(cellDir, DocumentKind.Cell);

        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(cellDir)));
        if (NameValidator.Validate(name) is { } why)
            f.Add(CliDiagnostics.CheckInvalidName(cellDir, "cell", name, why));

        foreach (var view in DocumentKinds.AllViewTypes)
        {
            PrimaryResolution primary;
            try { primary = CellFolder.ResolvePrimary(cellDir, view); }
            catch (Exception ex) { f.Add(CliDiagnostics.CheckUnreadable(cellDir, ex.Message)); continue; }

            switch (primary.State)
            {
                case PrimaryState.MissingNamedPrimary:
                    f.Add(CliDiagnostics.CheckPrimaryMissing(
                        cellDir, CellFolder.SubFolderName(view), primary.MissingName ?? "?"));
                    break;
                case PrimaryState.NoPrimary:
                    f.Add(CliDiagnostics.CheckNoPrimary(
                        cellDir, CellFolder.SubFolderName(view), CountViews(cellDir, view)));
                    break;
            }
        }

        f.End();

        // Then every view file in the cell, each in its own scope so the report says which file a
        // finding belongs to.
        foreach (var view in DocumentKinds.AllViewTypes)
        {
            string sub = CellFolder.SubFolderPath(cellDir, view);
            if (!Directory.Exists(sub)) continue;

            foreach (var file in Enumerate(() => Directory.EnumerateFiles(sub, "*" + CellFolder.ViewExtension(view)), f, sub)
                                 .Order(StringComparer.Ordinal))
                CheckPath(file, DocumentKinds.Classify(file), f, cache, recursive: false);
        }
    }

    private static int CountViews(string cellDir, ViewType view)
    {
        string sub = CellFolder.SubFolderPath(cellDir, view);
        try { return Directory.Exists(sub) ? Directory.GetFiles(sub, "*" + CellFolder.ViewExtension(view)).Length : 0; }
        catch { return 0; }
    }

    // ── one document of each kind ────────────────────────────────────────────

    /// <summary>
    /// Step one for every view file, and the one an extension alone cannot do:
    /// <c>CellViewFileValidator</c> requires a JSON key only that format writes, then runs the
    /// format's own reader. A `.clay` renamed to `.csch` deserializes CLEANLY into an empty
    /// schematic without it.
    /// </summary>
    private static bool CheckViewFile(string path, ViewType view, Findings f)
    {
        if (CellViewFileValidator.DescribeDefect(path, view) is { } defect)
        {
            f.Add(CliDiagnostics.CheckViewDefect(path, CellFolder.SubFolderName(view), defect));
            return false;
        }
        return true;
    }

    private static void CheckSchematic(string path, Findings f)
    {
        if (!CheckViewFile(path, ViewType.Schematic, f)) return;

        SchematicEditModel model;
        try { (model, _, _) = SchematicPersistence.LoadFromFile(path); }
        catch (Exception ex) { f.Add(CliDiagnostics.CheckUnreadable(path, ex.Message)); return; }

        string baseDir = Path.GetDirectoryName(Path.GetFullPath(path))!;

        // Every cell reference the schematic carries, through the resolver the editor draws with —
        // so "the placeholder glyph you would see if you opened this" and "what check says" cannot
        // disagree. The three states stay three states (R-aut4-4).
        foreach (var comp in model.Components)
        {
            if (comp.CellRef is not { Length: > 0 } cellRef) continue;

            var res = CellSymbolResolver.Resolve(cellRef, baseDir);
            string who = comp.InstanceName is { Length: > 0 } n ? n : comp.Id;

            switch (res.State)
            {
                case CellSymbolState.NotFound when PdkKitRegistry.IsKitRef(cellRef):
                    // Not a broken reference: a kit part lives in a registry the GUI populates when
                    // it opens a workspace, and nothing populates it here. Saying "no cell folder"
                    // would name the wrong repair for every part in a PDK design.
                    f.Add(CliDiagnostics.CheckKitRefUnresolved(path, who, cellRef));
                    break;
                case CellSymbolState.NotFound:
                    f.Add(CliDiagnostics.CheckRefNotFound(path, who, cellRef));
                    break;
                case CellSymbolState.PrimaryMissing:
                    f.Add(CliDiagnostics.CheckRefPrimaryMissing(path, who, cellRef));
                    break;
            }

            if (res.Redirect is { } moved)
                f.Add(CliDiagnostics.CheckRefRedirected(path, cellRef, moved.To));
        }

        // Extraction is the schematic's own "does this make a netlist?" It reports naming conflicts
        // — two different labels on one physical net — which nothing else in the tree reports.
        NetExtractor.ExtractionResult extracted;
        try { extracted = NetExtractor.Extract(model, Path.GetFileNameWithoutExtension(path)); }
        catch (Exception ex) { f.Add(CliDiagnostics.CheckUnreadable(path, ex.Message)); return; }

        foreach (var conflict in extracted.Conflicts)
            f.Add(CliDiagnostics.CheckExtractionConflict(path, conflict));

        // Elaboration runs on what the `.cnl` round trip produces, not on the extraction directly —
        // see CircuitSource for why those are not the same TestBench, and for the four shipped
        // schematics that prove it.
        Library   lib;
        TestBench tb;
        try
        {
            (lib, tb) = CircuitSource.FromSchematic(
                model, Path.GetFileNameWithoutExtension(path),
                Path.GetDirectoryName(Path.GetFullPath(path)));
        }
        catch (Exception ex) { f.Add(CliDiagnostics.CheckUnreadable(path, ex.Message)); return; }

        Elaborate(path, tb, lib, f);
    }

    private static void CheckLayout(string path, Findings f, TechnologyCache cache)
    {
        if (!CheckViewFile(path, ViewType.Layout, f)) return;

        LayoutView view;
        try { view = LayoutPersistence.LoadFromFile(path); }
        catch (Exception ex) { f.Add(CliDiagnostics.CheckUnreadable(path, ex.Message)); return; }

        string full    = Path.GetFullPath(path);
        var (tech, _)  = TechnologyResolver.ResolveForDocument(view.TechRef, full, null, cache);

        foreach (var d in tech.Diagnostics) f.Add(CliDiagnostics.CheckResolverNote(path, d));
        if (tech.Source == TechResolutionSource.None) f.Add(CliDiagnostics.CheckNoTechnology(path));

        RunDrc(path, view, full, tech.Tech, f, cache);
    }

    /// <summary>
    /// The design rules, run exactly as <c>LayoutEditorViewModel.RunDrc</c> runs them — the same
    /// flatten, the same engine, the same waiver store (R-aut4-3). What is deliberately NOT here is
    /// the wire half: a <c>WBondCheckContext</c> needs the wBond design the layout editor's document
    /// installs at runtime, which is a property of what is OPEN rather than of the artwork, so a
    /// headless check of a `.clay` alone has no wires to check and says so by checking none.
    /// </summary>
    private static void RunDrc(
        string path, LayoutView view, string fullPath, Technology? tech, Findings f, TechnologyCache cache)
    {
        string cellDir = CellDirOf(fullPath);

        LayoutDesignFlatten.FlattenResult flat;
        try
        {
            flat = LayoutDesignFlatten.Flatten(
                view, cellDir, tech,
                (techRef, subCellLayoutDir) =>
                    TechnologyResolver.ResolveForDocument(techRef, subCellLayoutDir, null, cache).Resolution,
                resolvedCrossTechMappings: null);
        }
        catch (Exception ex) { f.Add(CliDiagnostics.CheckDrcNote(path, ex.Message)); return; }

        if (flat.ExceedsCeiling)
        {
            f.Add(CliDiagnostics.CheckDrcNote(path,
                $"This design flattens to more than {LayoutDesignFlatten.HardCeiling:N0} shapes — " +
                "the check was refused rather than run."));
            return;
        }

        foreach (var u in flat.UnresolvedInstances) f.Add(CliDiagnostics.CheckDrcNote(path, u));

        foreach (var pending in flat.PendingCrossTechMappings)
            f.Add(CliDiagnostics.CheckDrcNote(path,
                $"\"{Path.GetFileName(pending.Key)}\" is drawn against a different technology and its " +
                "layers have not been mapped onto this one, so it was not checked."));

        DrcRunResult result;
        try { result = DrcEngine.Run(flat.Shapes, tech, view.DrcWaivers); }
        catch (Exception ex) { f.Add(CliDiagnostics.CheckDrcNote(path, ex.Message)); return; }

        foreach (var d in result.Diagnostics) f.Add(CliDiagnostics.CheckDrcNote(path, d));

        foreach (var v in result.Violations)
            f.Add(CliDiagnostics.CheckDrcViolation(
                path, v.RuleName, v.Kind.ToString(),
                // A WAIVED violation is reported and does not count (§9A.1: waiving must be
                // "persisted, and visible"), so it lands as a note rather than at the rule's own
                // severity — which is what keeps a fully-waived design exiting 0.
                v.Waived ? DiagnosticSeverity.Info
                         : v.Severity == DrcSeverity.Error ? DiagnosticSeverity.Error
                                                           : DiagnosticSeverity.Warning,
                v.Layer?.ToString(), v.MeasuredText, v.Waived));
    }

    /// <summary>The cell folder a view file belongs to: <c>&lt;cell&gt;/&lt;view&gt;/&lt;file&gt;</c>.
    /// A loose file outside a cell folder resolves to its own directory's parent, which is what the
    /// flatten already treats as the root for relative instance references.</summary>
    private static string CellDirOf(string viewFilePath)
        => Path.GetDirectoryName(Path.GetDirectoryName(viewFilePath)!) ?? "";

    private static void CheckTechnology(string path, Findings f)
    {
        Technology tech;
        try { tech = TechPersistence.LoadFromFile(path); }
        catch (Exception ex) { f.Add(CliDiagnostics.CheckUnreadable(path, ex.Message)); return; }

        foreach (var p in TechValidation.Analyze(tech))
            f.Add(CliDiagnostics.CheckTechProblem(path, p.Area.ToString(), p.Message));
    }

    private static void CheckEmSetup(string path, Findings f, TechnologyCache cache)
    {
        EmSetup setup;
        try { setup = EmSetupPersistence.LoadFromFile(path); }
        catch (Exception ex) { f.Add(CliDiagnostics.CheckUnreadable(path, ex.Message)); return; }

        string full = Path.GetFullPath(path);
        // The same walk-up `circuitrf em` performs, and for the same reason (cli.md §8.1). No flag
        // and no "currently open workspace": the `.cem`'s own ancestor is what its LayoutRef is
        // relative to.
        string? cws = DocumentKinds.AncestorCws(full);

        var resolution = EmSetupResolver.Resolve(full, setup.LayoutRef, cws, cache);

        foreach (var d in resolution.Diagnostics) f.Add(CliDiagnostics.CheckResolverNote(path, d));

        if (resolution.Source is null)
            f.Add(CliDiagnostics.CheckEmUnresolved(path,
                resolution.Diagnostics.Count > 0
                    ? "its layout did not resolve, so there is no geometry to analyse."
                    : "its layout reference resolves to nothing."));
    }

    private static void CheckAssemblyRules(string path, Findings f)
    {
        WasmFile wasm;
        try { wasm = WasmPersistence.LoadFromFile(path); }
        catch (Exception ex) { f.Add(CliDiagnostics.CheckUnreadable(path, ex.Message)); return; }

        // The `.wasm`'s own predicate parser is the authority on its rules — the same one the DRC
        // engine compiles them with, so a rule that checks clean here is one the engine will run.
        foreach (var (_, rule) in wasm.AllRules())
            if (!DrcPredicateParser.TryParse(rule.Expression, out _, out var error))
                f.Add(CliDiagnostics.CheckAssemblyRuleInvalid(path, rule.Name, error ?? "unparseable"));
    }

    private static void CheckNetlist(string path, Findings f)
    {
        Library   lib;
        TestBench tb;
        try { (lib, tb) = CnlReader.ReadFile(path); }
        catch (Exception ex) { f.Add(CliDiagnostics.CheckUnreadable(path, ex.Message)); return; }

        Elaborate(path, tb, lib, f);
    }

    /// <summary>
    /// Elaboration and chain selection — the two questions a document holding a circuit has that a
    /// document holding artwork does not. Shared by `.cnl` and `.csch` because the `.csch` becomes
    /// exactly the same <c>TestBench</c> and <c>Library</c> on the way (AUT-2), so checking it
    /// differently would mean checking something the run verbs never see.
    /// </summary>
    private static void Elaborate(string path, TestBench tb, Library lib, Findings f)
    {
        ElaboratedNetlist nl;
        try { nl = new Elaborator(lib).Elaborate(tb); }
        catch (Exception ex) { f.Add(CliDiagnostics.CheckElaborationFailed(path, ex.Message)); return; }

        using (nl)
        {
            foreach (var w in nl.Warnings) f.Add(CliDiagnostics.CheckElaborationWarning(path, w));
            foreach (var n in nl.Notes)    f.Add(CliDiagnostics.CheckElaborationNote(path, n));

            // Will anything dispatch? Two questions, and only the second one uses ChainSelector.
            //
            // First: does the document declare an analysis AT ALL — the GUI's own NoAnalysis test.
            // A cell's schematic is not supposed to declare one and a `.cnl` written to be included
            // by another is not either, so this is a warning rather than an error.
            if (!CircuitSource.DeclaresARunnableAnalysis(tb))
            {
                f.Add(CliDiagnostics.CheckNoRunnableAnalysis(
                    path, "The document declares no analysis."));
                return;
            }

            // Second: of the three kinds that go through chain selection, does a DECLARED one fail
            // to select? Asked through ChainSelector — the function the run verbs themselves select
            // with — so "check says it will run" and "it runs" cannot part company (Gate 4). A kind
            // the netlist declares NONE of is not a finding; a kind it declares and cannot run is.
            foreach (var (isBase, label, hint, _) in Explain.AnalysisKinds)
            {
                if (!tb.Analyses.Any(isBase)) continue;
                var sel = ChainSelector.Select(tb, null, isBase, label, hint);
                if (sel.Selected is null && sel.Why is { } why)
                    f.Add(CliDiagnostics.CheckNoRunnableAnalysis(path, why));
            }
        }
    }
}
