using CircuitRF.Diagnostics;

namespace CircuitRF.Cli;

/// <summary>
/// The CLI's own refusals, argument errors and forwarded run notes, as coded diagnostics
/// (brief-automation-1-structured-output.md R-aut1-6/R-aut1-7).
///
/// <para><b>Every template here renders to the sentence stderr already printed, character for
/// character.</b> That is the whole constraint: R-aut0-3 says a script parsing stderr today must not
/// notice this landing, and the way to be sure of it is to make the diagnostic the SOURCE of the
/// sentence rather than a second copy of it. Where a call site wrote a prefix (<c>warning: </c>,
/// <c>note: </c>, <c>[circuitRF] </c>) the prefix stays at the call site, because it is a channel
/// convention rather than part of the message.</para>
///
/// <para><b>The ids are permanent.</b> <see cref="Diagnostic"/>'s own remarks are explicit: reword a
/// template freely, change an id and you have made a new diagnostic. <c>CliDiagnosticIdTests</c>
/// exists to turn an accidental id change into a failing test rather than a silently broken caller.
/// The first segment scopes to the producing area — <c>cli.</c> for this program's own argument and
/// file handling, <c>elab.</c> for what elaboration said, <c>lp.</c> for the loadpull verbs,
/// <c>convert.</c> for the interchange verb. <c>em.</c> is not minted here: those already exist in
/// <c>EmDiagnostics</c> and arrive on <c>EmRunResult.Diagnostic</c>.</para>
///
/// <para><b>What is deliberately NOT converted.</b> R-aut1-7 draws the line at the sites this
/// brief's verbs actually reach. Warnings authored deep in the engines still cross as finished
/// English and are wrapped here argument-free, with an honest id, rather than being re-authored or —
/// worse — having arguments invented by parsing the prose back apart. The count that remains is
/// reported in <c>src/Cli/RESOLVED.md</c> rather than left as a guess.</para>
/// </summary>
internal static class CliDiagnostics
{
    // ── arguments and files ──────────────────────────────────────────────────

    /// <summary>A verb invoked with no input file.</summary>
    public static Diagnostic InputRequired(string verb, string what) => Diagnostic.Create(
        "cli.args.input-required",
        DiagnosticSeverity.Error,
        "{verb}: input {what} file required",
        ("verb", verb), ("what", what));

    /// <summary>The input path does not exist.</summary>
    public static Diagnostic FileNotFound(string path) => Diagnostic.Create(
        "cli.input.not-found",
        DiagnosticSeverity.Error,
        "File not found: {path}",
        ("path", path));

    /// <summary><c>--set</c> without an <c>=</c>.</summary>
    /// <summary>
    /// An option the run verb does not read.
    ///
    /// <para><b>Refused rather than ignored, for the reason every other refusal in this file
    /// exists.</b> The five run verbs used to drop an unrecognised flag in silence, and the silence
    /// was not the whole cost: a verb that finds its input by "the first token that does not start
    /// with a dash" then takes the DROPPED flag's VALUE as its input path, so
    /// <c>lp x.cnl --maxmix 3</c> answered <c>File not found: 3</c> — a refusal naming neither the
    /// real problem nor the file the caller actually gave. Where the value was not swallowed, as in
    /// <c>dc x.cnl --set Vg=1</c>, nothing was reported at all and the run answered a different
    /// question than the one asked. <c>convert</c>, <c>new</c>, <c>import</c>, <c>check</c>,
    /// <c>explain</c> and <c>read</c> have refused an unknown option since they were written; this
    /// is the same rule reaching the verbs that predate it.</para>
    /// </summary>
    public static Diagnostic RunUnknownOption(string verb, string option) => Diagnostic.Create(
        "cli.args.unknown-option",
        DiagnosticSeverity.Error,
        "{verb}: unknown option '{option}'.",
        ("verb", verb), ("option", option));

    /// <summary>A second positional where the verb takes one. <c>convert</c>, <c>check</c>,
    /// <c>explain</c> and <c>read</c> have said this since they were written.</summary>
    public static Diagnostic RunMultipleInputs(string verb, string extra) => Diagnostic.Create(
        "cli.args.multiple-inputs",
        DiagnosticSeverity.Error,
        "{verb}: one input file, please — '{extra}' is a second one.",
        ("verb", verb), ("extra", extra));

    public static Diagnostic SetMalformed(string verb, string text) => Diagnostic.Create(
        "cli.args.set-malformed",
        DiagnosticSeverity.Error,
        "{verb}: --set expects name=expr, got '{text}'",
        ("verb", verb), ("text", text));

    /// <summary><c>--pin</c> that is not three parseable numbers.</summary>
    public static Diagnostic PinMalformed(string verb, string text) => Diagnostic.Create(
        "cli.args.pin-malformed",
        DiagnosticSeverity.Error,
        "{verb}: --pin expects start:step:max in dBm, got '{text}'",
        ("verb", verb), ("text", text));

    /// <summary>A Γ grid handed to a pursuit, which searches for its terminations instead of
    /// reading them. Refused rather than ignored: a grid silently not applied is a run that answers
    /// a different question and reports nothing about it.</summary>
    public static Diagnostic GridNotForPursuit() => new(
        "cli.args.grid-not-for-pursuit",
        DiagnosticSeverity.Error,
        "lpp: --grid does not apply to a pursuit — a pursuit SEARCHES for its terminations rather " +
        "than reading a grid. Use --out-grid to say where the terminations it finds are written.");

    /// <summary><c>--out-grid</c> on a loadpull, which reads a grid rather than writing one.</summary>
    public static Diagnostic OutGridNotForLoadpull() => new(
        "cli.args.out-grid-not-for-loadpull",
        DiagnosticSeverity.Error,
        "lp: --out-grid applies to lpp (the pursuit writes a grid; a loadpull reads one).");

    /// <summary>A workspace named by <c>--workspace</c> that is not there.</summary>
    public static Diagnostic WorkspaceNotFound(string path) => Diagnostic.Create(
        "cli.workspace.not-found",
        DiagnosticSeverity.Error,
        "Workspace file not found: {path}",
        ("path", path));

    /// <summary>No verb, or one this program does not have.</summary>
    public static Diagnostic UnknownVerb(string verb) => Diagnostic.Create(
        "cli.verb.unknown",
        DiagnosticSeverity.Error,
        "No such command: '{verb}'",
        ("verb", verb));

    /// <summary>Invoked with nothing at all.</summary>
    public static Diagnostic NoCommand() => new(
        "cli.verb.none",
        DiagnosticSeverity.Error,
        "No command given.");

    // ── running ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The chain selection said no — the netlist declares no analysis of the verb's kind, its chain
    /// is disabled, or the name asked for is not there. The reason is <c>SelectTop</c>'s own
    /// sentence, forwarded rather than re-authored: it names the declared analyses, which is the
    /// part that is actually actionable.
    /// </summary>
    public static Diagnostic NoAnalysis(string verb, string reason) => Diagnostic.Create(
        "cli.analysis.not-selected",
        DiagnosticSeverity.Error,
        "{verb}: {reason}",
        ("verb", verb), ("reason", reason));

    /// <summary>The run threw. The message is the exception's, which is where the only detail is.</summary>
    public static Diagnostic RunFailed(string message) => Diagnostic.Create(
        "cli.run.failed",
        DiagnosticSeverity.Error,
        "Error: {message}",
        ("message", message));

    /// <summary>A <c>.cem</c> that could not be read at all.</summary>
    public static Diagnostic SetupUnreadable(string path, string message) => Diagnostic.Create(
        "cli.em.setup-unreadable",
        DiagnosticSeverity.Error,
        "Could not read '{path}': {message}",
        ("path", path), ("message", message));

    /// <summary>A <c>.spl</c>/<c>.lpcwave</c> asked of a result that carries no Γ surface.</summary>
    public static Diagnostic NoLoadpullSurface(string extension) => Diagnostic.Create(
        "lp.export.no-surface",
        DiagnosticSeverity.Error,
        "Cannot write {extension}: this result carries no loadpull surface (no GammaLoad cube). " +
        "A pursuit that found no optimum has no follow-on grid to export — use .npy/.mat to " +
        "keep what it did produce.",
        ("extension", extension));

    // ── forwarded, argument-free ─────────────────────────────────────────────
    //
    // R-aut1-7's last clause: where the producing site does not have typed values to hand, wrap the
    // existing sentence with an honest id rather than inventing arguments by parsing prose. Each of
    // these is a whole English sentence authored somewhere below, and the id says only where it came
    // from — which is still enough to filter, group and deduplicate on, and is the entire point.

    /// <summary>Something elaboration reported about itself. Not a problem.</summary>
    public static Diagnostic ElaborationNote(string text) => Diagnostic.Create(
        "elab.note", DiagnosticSeverity.Info, "{text}", ("text", text));

    /// <summary>Something elaboration or an engine wants looked at.</summary>
    public static Diagnostic ElaborationWarning(string text) => Diagnostic.Create(
        "elab.warning", DiagnosticSeverity.Warning, "{text}", ("text", text));

    /// <summary>A <c>measure</c> line that would not evaluate.</summary>
    public static Diagnostic MeasurementFailed(string text) => Diagnostic.Create(
        "cli.measurement.failed", DiagnosticSeverity.Warning, "{text}", ("text", text));

    /// <summary>A warning from the EM setup resolver — a technology that would not resolve, say.</summary>
    public static Diagnostic EmSetupWarning(string text) => Diagnostic.Create(
        "em.setup.warning", DiagnosticSeverity.Warning, "{text}", ("text", text));

    /// <summary>An <see cref="Diagnostic"/>-free entry from one of <c>EmRunResult</c>'s three
    /// lists. The lists are strings — only the top-level refusal carries a coded form — so the id
    /// records which of the three it was, which is exactly the distinction the split exists for.</summary>
    public static Diagnostic EmRunNote(string text) => Diagnostic.Create(
        "em.run.note", DiagnosticSeverity.Info, "{text}", ("text", text));

    /// <inheritdoc cref="EmRunNote"/>
    public static Diagnostic EmRunWarning(string text) => Diagnostic.Create(
        "em.run.warning", DiagnosticSeverity.Warning, "{text}", ("text", text));

    /// <inheritdoc cref="EmRunNote"/>
    public static Diagnostic EmRunError(string text) => Diagnostic.Create(
        "em.run.error", DiagnosticSeverity.Error, "{text}", ("text", text));

    /// <summary>A device worker's own log, which holds facts stated nowhere else.</summary>
    public static Diagnostic WorkerOutput(string provider, string text) => Diagnostic.Create(
        "cli.worker.output",
        DiagnosticSeverity.Info,
        "{text}",
        ("provider", provider), ("text", text));

    // ── convert ──────────────────────────────────────────────────────────────
    //
    // Named one by one rather than through a generic `Convert(kind, text)` factory, because an id is
    // a permanent contract (R-aut1-8) and a factory that MINTS ids from a caller's string is a
    // contract nobody can enumerate, review or hold still.

    public static Diagnostic ConvertUnknownDxfVersion(string version) => Diagnostic.Create(
        "convert.args.dxf-version", DiagnosticSeverity.Error,
        "Unknown DXF version '{version}'. Known: AC1015, AC1018, AC1032.", ("version", version));

    public static Diagnostic ConvertUnknownDrillUnit(string unit) => Diagnostic.Create(
        "convert.args.drill-units", DiagnosticSeverity.Error,
        "Unknown drill unit '{unit}'. Known: mm, inch.", ("unit", unit));

    public static Diagnostic ConvertUnknownDrillZeros(string zeros) => Diagnostic.Create(
        "convert.args.drill-zeros", DiagnosticSeverity.Error,
        "Unknown zero suppression '{zeros}'. Known: leading, trailing.", ("zeros", zeros));

    public static Diagnostic ConvertBadDrillFormat(string text) => Diagnostic.Create(
        "convert.args.drill-format", DiagnosticSeverity.Error,
        "--drill-format wants integer:decimal digits, e.g. 2:4 — got '{text}'.", ("text", text));

    /// <summary>
    /// Distinct from <see cref="FileNotFound"/>, and not merged with it: <c>convert</c>'s input may be
    /// a FOLDER (a Gerber file set), so it has always said "Input not found" rather than "File not
    /// found". Changing the word would change stderr, which R-aut0-3 forbids — and it would be the
    /// wrong word besides.
    /// </summary>
    public static Diagnostic ConvertInputNotFound(string path) => Diagnostic.Create(
        "convert.input.not-found", DiagnosticSeverity.Error,
        "Input not found: {path}", ("path", path));

    public static Diagnostic ConvertUnknownOption(string option) => Diagnostic.Create(
        "convert.args.unknown-option", DiagnosticSeverity.Error,
        "Unknown option: {option}", ("option", option));

    public static Diagnostic ConvertMultipleInputs() => new(
        "convert.args.multiple-inputs", DiagnosticSeverity.Error,
        "convert takes one input.");

    public static Diagnostic ConvertOutputRequired() => new(
        "convert.args.output-required", DiagnosticSeverity.Error,
        "convert needs an output: -o <path>.");

    public static Diagnostic ConvertUnknownFormat(string format) => Diagnostic.Create(
        "convert.args.unknown-format", DiagnosticSeverity.Error,
        "Unknown format '{format}'. Known: clay, gdsii, dxf, gerber, board.", ("format", format));

    /// <summary>The usage text itself, recorded so a document says what stderr said. Both lines,
    /// because both were printed.</summary>
    public static Diagnostic ConvertUsage() => new(
        "convert.args.usage", DiagnosticSeverity.Error,
        "Usage: circuitrf convert <input> -o <output> [--from f] [--to f] [--cell name]\n" +
        "       formats: clay | gdsii | dxf | gerber | board");

    public static Diagnostic ConvertListCellsNotApplicable() => new(
        "convert.args.list-cells-not-applicable", DiagnosticSeverity.Error,
        "A .clay names one cell — --list-cells applies to a file that can hold several.");

    public static Diagnostic ConvertSourceUnrecognised(string fileName) => Diagnostic.Create(
        "convert.source.unrecognised", DiagnosticSeverity.Error,
        "Could not tell what '{fileName}' is from its name or its content. " +
        "Name it with --from clay|gdsii|dxf|gerber|board.", ("fileName", fileName));

    public static Diagnostic ConvertTargetUnrecognised(string output) => Diagnostic.Create(
        "convert.target.unrecognised", DiagnosticSeverity.Error,
        "'{output}' does not name a format. Give it a known extension " +
        "(.clay, .gds, .dxf, .kicad_pcb) or say --to clay|gdsii|dxf|gerber|board.", ("output", output));

    public static Diagnostic ConvertClayToClay() => new(
        "convert.clay-to-clay", DiagnosticSeverity.Error,
        "clay to clay is a file copy, not a conversion — nothing to do.");

    public static Diagnostic ConvertFailed(string message) => Diagnostic.Create(
        "convert.failed", DiagnosticSeverity.Error, "{message}", ("message", message));

    public static Diagnostic ConvertTechnologyUnreadable(string path, string message) => Diagnostic.Create(
        "convert.technology.unreadable", DiagnosticSeverity.Error,
        "Could not read technology '{path}': {message}", ("path", path), ("message", message));

    public static Diagnostic ConvertLayoutUnreadable(string path, string message) => Diagnostic.Create(
        "convert.layout.unreadable", DiagnosticSeverity.Error,
        "Could not read layout '{path}': {message}", ("path", path), ("message", message));

    public static Diagnostic ConvertNoCell() => new(
        "convert.import.no-cell", DiagnosticSeverity.Error,
        "The Gerber import produced no cell.");

    public static Diagnostic ConvertNothingConverted() => new(
        "convert.import.cancelled", DiagnosticSeverity.Error,
        "Nothing was converted.");

    /// <summary>
    /// The one refusal that exists because a guess would be worse than a stop: leading vs trailing
    /// zero suppression differ by four orders of magnitude on identical text. The evidence lines stay
    /// on stderr where they were; the typed half is here.
    /// </summary>
    public static Diagnostic ConvertDrillFormatUnstated(string fileName, string inferred) => Diagnostic.Create(
        "convert.drill.format-unstated", DiagnosticSeverity.Error,
        "{fileName} does not state its coordinate format, and the inference had to guess.",
        ("fileName", fileName), ("inferred", inferred));

    public static Diagnostic ConvertCellHasNoLayout(string cellName) => Diagnostic.Create(
        "convert.cell.no-layout", DiagnosticSeverity.Error,
        "'{cellName}' holds no layout view to convert.", ("cellName", cellName));

    public static Diagnostic ConvertCellNotFound(string wanted, string held) => Diagnostic.Create(
        "convert.cell.not-found", DiagnosticSeverity.Error,
        "No cell named '{wanted}' in this file. It holds: {held}", ("wanted", wanted), ("held", held));

    public static Diagnostic ConvertCellAmbiguous(int count, string what) => Diagnostic.Create(
        "convert.cell.ambiguous", DiagnosticSeverity.Error,
        "This file holds {count} cells and none of them is an unambiguous top ({what} definitions " +
        "are all referenced by something else). Name one with --cell, or list them with --list-cells.",
        ("count", count), ("what", what));

    public static Diagnostic ConvertGdsiiCoordinateOverflow() => new(
        "convert.gdsii.coordinate-overflow", DiagnosticSeverity.Error,
        "coordinates overflow GDSII's 32-bit integer range — nothing written.");

    public static Diagnostic ConvertGerberDiagnostic(string text) => Diagnostic.Create(
        "convert.gerber.refused", DiagnosticSeverity.Error, "{text}", ("text", text));

    public static Diagnostic ConvertGerberHierarchyCeiling() => new(
        "convert.gerber.hierarchy-ceiling", DiagnosticSeverity.Error,
        "The design's cell hierarchy exceeds what the Gerber export can flatten.");

    public static Diagnostic ConvertGerberCrossTechnology() => new(
        "convert.gerber.cross-technology-mapping", DiagnosticSeverity.Error,
        "this design instantiates cells from another technology, and the layer mapping " +
        "has to be confirmed. Open it in circuitRF and export once, or flatten the design first.");

    public static Diagnostic ConvertBoardRefused(string reason) => Diagnostic.Create(
        "convert.board.refused", DiagnosticSeverity.Error, "{reason}", ("reason", reason));

    public static Diagnostic ConvertTargetUnsupported(string format) => Diagnostic.Create(
        "convert.target.unsupported", DiagnosticSeverity.Error,
        "Cannot write {format}.", ("format", format));

    /// <summary>
    /// One of the import's own messages — a layer that was mapped, a zone that was not imported, a
    /// stackup section the file did not carry. On stderr since the verb was written, and in the
    /// document since AUT-4: they are how a caller learns what was DROPPED, and a `--json` caller
    /// that could not read them would be reading a conversion report that omits the losses.
    ///
    /// <para>Argument-free by the same rule the forwarded block above follows: an importer hands over
    /// a finished English sentence and has no typed values to give, so the id says where it came from
    /// and nothing is invented by parsing the prose apart.</para>
    /// </summary>
    public static Diagnostic ConvertNote(string text) => Diagnostic.Create(
        "convert.note", DiagnosticSeverity.Info, "{text}", ("text", text));

    /// <summary>
    /// What <c>--list-cells</c> found. The listing is the RESULT of that invocation — the whole
    /// answer to "what does this file hold?" — so under `--json` it belongs in the document rather
    /// than only on the stdout the flag replaces (R-aut1-3).
    /// </summary>
    public static Diagnostic ConvertCellListed(string cell) => Diagnostic.Create(
        "convert.cell.listed", DiagnosticSeverity.Info, "{cell}", ("cell", cell));

    // ── new / import part (brief-automation-3-authoring-verbs.md) ─────────────
    //
    // The authoring verbs' whole contract is "it was created, or it was refused and the sentence says
    // what would fix it" (§7.5), so every refusal here NAMES the flag or the value that answers it.
    // Ids are `new.` and `import.`, and permanent like every other one.

    public static Diagnostic NewNounRequired() => new(
        "new.args.noun-required", DiagnosticSeverity.Error,
        "new: say what to create — 'new workspace' or 'new cell'.");

    public static Diagnostic NewUnknownNoun(string noun) => Diagnostic.Create(
        "new.args.unknown-noun", DiagnosticSeverity.Error,
        "new: there is nothing called '{noun}' to create. Known: workspace, cell.", ("noun", noun));

    public static Diagnostic NewUnknownOption(string option) => Diagnostic.Create(
        "new.args.unknown-option", DiagnosticSeverity.Error,
        "new: unknown option '{option}'.", ("option", option));

    public static Diagnostic NewMultipleDirectories() => new(
        "new.args.multiple-directories", DiagnosticSeverity.Error,
        "new workspace: one directory, please — use --name to give the workspace a name inside it.");

    public static Diagnostic NewWorkspaceDirRequired() => new(
        "new.args.directory-required", DiagnosticSeverity.Error,
        "new workspace: a directory is required.");

    public static Diagnostic NewCellArgsRequired() => new(
        "new.args.cell-args-required", DiagnosticSeverity.Error,
        "new cell: a workspace (or a folder inside one) and a cell name are required.");

    public static Diagnostic NewCellExtraArgument(string arg) => Diagnostic.Create(
        "new.args.cell-extra-argument", DiagnosticSeverity.Error,
        "new cell: unexpected argument '{arg}' — it takes a workspace and one cell name.", ("arg", arg));

    /// <summary>R-aut3-9: NameValidator's reason, never a second rule set — a headless caller must
    /// not be able to create a name the GUI would reject.</summary>
    public static Diagnostic NewInvalidName(string kind, string name, string reason) => Diagnostic.Create(
        "new.name.invalid", DiagnosticSeverity.Error,
        "Invalid {kind} name '{name}': {reason}", ("kind", kind), ("name", name), ("reason", reason));

    /// <summary>R-aut3-5: an unknown shipped-technology id lists the ones that exist. Not a fallback
    /// to the default — a caller that asked for a specific process and silently got another has a
    /// wrong design and no way to know.</summary>
    public static Diagnostic NewUnknownTechnology(string id, string known) => Diagnostic.Create(
        "new.tech.unknown", DiagnosticSeverity.Error,
        "No shipped technology named '{id}'. The shipped technologies are: {known}. "
        + "Use --tech none for a workspace with no technology.", ("id", id), ("known", known));

    public static Diagnostic NewParentNotFound(string path) => Diagnostic.Create(
        "new.parent.not-found", DiagnosticSeverity.Error,
        "No such directory: {path}", ("path", path));

    /// <summary>R-aut3-6, and the read-only-parent refusal: the capability's own sentence, forwarded
    /// rather than re-worded, so the GUI and the verb refuse in the same words.</summary>
    public static Diagnostic NewRefused(string reason) => Diagnostic.Create(
        "new.refused", DiagnosticSeverity.Error, "{reason}", ("reason", reason));

    public static Diagnostic NewCellExists(string name) => Diagnostic.Create(
        "new.cell.exists", DiagnosticSeverity.Error,
        "A cell named '{name}' already exists there. Nothing was created.", ("name", name));

    public static Diagnostic NewUnknownView(string view) => Diagnostic.Create(
        "new.views.unknown", DiagnosticSeverity.Error,
        "--views: '{view}' is not a view. Known: schematic, symbol, layout.", ("view", view));

    public static Diagnostic ImportNounRequired() => new(
        "import.args.noun-required", DiagnosticSeverity.Error,
        "import: say what to import — 'import part'.");

    public static Diagnostic ImportUnknownNoun(string noun) => Diagnostic.Create(
        "import.args.unknown-noun", DiagnosticSeverity.Error,
        "import: there is nothing called '{noun}' to import. Known: part. "
        + "For layout interchange (GDSII, DXF, Gerber, boards) use `convert`.", ("noun", noun));

    public static Diagnostic ImportUnknownOption(string option) => Diagnostic.Create(
        "import.args.unknown-option", DiagnosticSeverity.Error,
        "import part: unknown option '{option}'.", ("option", option));

    public static Diagnostic ImportMultipleSources() => new(
        "import.args.multiple-sources", DiagnosticSeverity.Error,
        "import part: one file or folder, please.");

    public static Diagnostic ImportSourceRequired() => new(
        "import.args.source-required", DiagnosticSeverity.Error,
        "import part: a component file or folder is required.");

    public static Diagnostic ImportIntoRequired() => new(
        "import.args.into-required", DiagnosticSeverity.Error,
        "import part: --into <workspace-or-dir> says where the cell is created.");

    public static Diagnostic ImportSourceNotFound(string path) => Diagnostic.Create(
        "import.source.not-found", DiagnosticSeverity.Error,
        "No such file or folder: {path}", ("path", path));

    /// <summary>A reader's or a scan's own refusal, forwarded whole.</summary>
    public static Diagnostic ImportRefused(string reason) => Diagnostic.Create(
        "import.refused", DiagnosticSeverity.Error, "{reason}", ("reason", reason));

    /// <summary>R-aut3-12: one import message, on stderr and in the document. They are how a caller
    /// learns a pin was inferred, a layer was dropped or a variant was skipped.</summary>
    public static Diagnostic ImportMessage(string text) => Diagnostic.Create(
        "import.note", DiagnosticSeverity.Info, "{text}", ("text", text));

    /// <summary>R-aut3-11: the chooser dialog's question, refused rather than guessed at.</summary>
    public static Diagnostic ImportAmbiguous(int count, string listing) => Diagnostic.Create(
        "import.part.ambiguous", DiagnosticSeverity.Error,
        "{count} parts here, and nothing said which. Name one with --cell (and --variant when two "
        + "share a name), or run with --list-parts:\n{listing}", ("count", count), ("listing", listing));

    public static Diagnostic ImportPartNotFound(string wanted, string held) => Diagnostic.Create(
        "import.part.not-found", DiagnosticSeverity.Error,
        "No part named '{wanted}' here. This holds: {held}.", ("wanted", wanted), ("held", held));

    public static Diagnostic ImportVariantNotFound(string variant) => Diagnostic.Create(
        "import.variant.not-found", DiagnosticSeverity.Error,
        "No part variant '{variant}' here — --variant takes the key in --list-parts' second column.",
        ("variant", variant));

    public static Diagnostic ImportNothingCreated() => new(
        "import.nothing-created", DiagnosticSeverity.Error,
        "The part was read but no cell was created.");

    /// <summary>The layers a part needs that its technology does not define. Reported ALWAYS, and
    /// written only when --add-layers says so — the GUI's own install is session-only and says in as
    /// many words that nothing was written to disk.</summary>
    public static Diagnostic ImportLayersNotInstalled(int count, string names) => Diagnostic.Create(
        "import.layers.not-installed", DiagnosticSeverity.Warning,
        "{count} layer(s) this part uses are not in the technology: {names}. The cell was created "
        + "with them; the technology was not changed. Pass --add-layers to write them into it.",
        ("count", count), ("names", names));

    /// <summary>No destination technology resolved at all. The reconciliation has nothing to compare
    /// the part's layers against, so it reports none as new and they land with numeric keys and no
    /// names — the same state the GUI is in with a technology-less workspace, said out loud rather
    /// than left to be discovered when the layout opens grey.</summary>
    public static Diagnostic ImportNoTechnology() => new(
        "import.no-technology", DiagnosticSeverity.Warning,
        "No technology resolved for this destination, so the part's layers arrive unnamed and none "
        + "is reported as new. Name one with --tech, or set the workspace's default technology.");

    public static Diagnostic ImportNoTechnologyToAddTo(string names) => Diagnostic.Create(
        "import.layers.no-technology", DiagnosticSeverity.Error,
        "--add-layers was given but no technology file resolved, so there is nowhere to put "
        + "{names}. Name one with --tech.", ("names", names));

    // ── check (brief-automation-4-check-and-explain.md) ───────────────────────
    //
    // R-aut4-2: `check` writes no validation logic of its own. Every id below names an EXISTING
    // validator's finding, and the typed arguments are that validator's own values, not a sentence
    // parsed back apart (R-aut4-4). Where a validator hands over only prose — the `.wasm` predicate
    // parser, the technology resolver, the EM setup resolver — the sentence is forwarded whole under
    // an honest id, exactly as the forwarded block above does for the engines.
    //
    // Two of these are deliberately NOT errors. A cell sub-folder holding several views and no named
    // primary is `PrimaryState.NoPrimary`, which that enum's own remarks call "not an error"; a
    // technology that resolves to nothing is `TechResolutionSource.None`, which layout-view.md §2.4
    // calls a normal, fully-supported state. Reporting either as an error would make `check` refuse
    // designs the application opens happily, which is the one thing it must never do.

    public static Diagnostic CheckPathRequired() => new(
        "check.args.path-required", DiagnosticSeverity.Error,
        "check: a path is required — a workspace, a cell folder, or one document.");

    public static Diagnostic CheckUnknownOption(string option) => Diagnostic.Create(
        "check.args.unknown-option", DiagnosticSeverity.Error,
        "check: unknown option '{option}'.", ("option", option));

    public static Diagnostic CheckMultiplePaths() => new(
        "check.args.multiple-paths", DiagnosticSeverity.Error,
        "check: one path, please.");

    public static Diagnostic CheckUnknownSeverity(string text) => Diagnostic.Create(
        "check.args.unknown-severity", DiagnosticSeverity.Error,
        "check: --severity takes warning or error, got '{text}'.", ("text", text));

    public static Diagnostic CheckPathNotFound(string path) => Diagnostic.Create(
        "check.path.not-found", DiagnosticSeverity.Error,
        "No such file or folder: {path}", ("path", path));

    /// <summary>A path circuitRF has no reader for. Not silence: a caller that pointed `check` at
    /// the wrong file learns that here rather than from a clean exit that checked nothing.</summary>
    public static Diagnostic CheckUnknownKind(string path) => Diagnostic.Create(
        "check.path.unknown-kind", DiagnosticSeverity.Error,
        "Nothing circuitRF reads is named '{path}' — check takes a workspace, a cell folder, or a "
        + ".csch, .csym, .clay, .ctech, .cem, .cnl or .wasm.", ("path", path));

    /// <summary>An interchange file. Reported rather than checked, because there is nothing to check
    /// it AGAINST: a GDSII or Gerber file is not a circuitRF document and has no primacy, no
    /// technology reference and no analysis chain. `convert` is what reads it.</summary>
    public static Diagnostic CheckInterchangeNotADocument(string path, string format) => Diagnostic.Create(
        "check.path.interchange", DiagnosticSeverity.Info,
        "'{path}' is {format} interchange, not a circuitRF document — there is nothing to validate "
        + "until it is imported. Read it with `circuitrf convert`.", ("path", path), ("format", format));

    /// <summary><c>CellViewFileValidator.DescribeDefect</c>'s own sentence, which is written to be
    /// shown verbatim.</summary>
    public static Diagnostic CheckViewDefect(string path, string view, string defect) => Diagnostic.Create(
        "check.view.defect", DiagnosticSeverity.Error,
        "{path}: {defect}", ("path", path), ("view", view), ("defect", defect));

    public static Diagnostic CheckUnreadable(string path, string message) => Diagnostic.Create(
        "check.file.unreadable", DiagnosticSeverity.Error,
        "{path}: could not be read ({message}).", ("path", path), ("message", message));

    /// <summary>R-aut4-2's <c>MissingNamedPrimary</c> — the `.ccell` names a primary that is not
    /// there. <c>PrimaryState</c>'s own remarks say do not collapse this into NoPrimary.</summary>
    public static Diagnostic CheckPrimaryMissing(string cellDir, string view, string named) => Diagnostic.Create(
        "check.cell.primary-missing", DiagnosticSeverity.Error,
        "{cellDir}: the {view} view names '{named}' as primary and that file is not there.",
        ("cellDir", cellDir), ("view", view), ("named", named));

    public static Diagnostic CheckNoPrimary(string cellDir, string view, int count) => Diagnostic.Create(
        "check.cell.no-primary", DiagnosticSeverity.Warning,
        "{cellDir}: the {view} view holds {count} files and none is primary, so nothing resolves it.",
        ("cellDir", cellDir), ("view", view), ("count", count));

    /// <summary>R-aut3-9's rule, read the other way: a name the GUI would reject must not be
    /// reported clean here either. <c>NameValidator</c>'s own reason, never a second rule set.</summary>
    public static Diagnostic CheckInvalidName(string path, string kind, string name, string reason) =>
        Diagnostic.Create(
            "check.name.invalid", DiagnosticSeverity.Error,
            "{path}: invalid {kind} name '{name}' — {reason}",
            ("path", path), ("kind", kind), ("name", name), ("reason", reason));

    /// <summary><c>TechValidation.Analyze</c>'s finding, with its <c>TechProblemArea</c> kept as an
    /// argument rather than flattened into the sentence (R-aut4-4).</summary>
    public static Diagnostic CheckTechProblem(string path, string area, string message) => Diagnostic.Create(
        "check.tech.problem", DiagnosticSeverity.Warning,
        "{path}: {message}", ("path", path), ("area", area), ("message", message));

    /// <summary>Whatever <c>TechnologyResolver</c> or <c>EmSetupResolver</c> had to say. Both return
    /// diagnostics as strings and neither has typed values to hand, so the sentence is forwarded.</summary>
    public static Diagnostic CheckResolverNote(string path, string text) => Diagnostic.Create(
        "check.resolver.note", DiagnosticSeverity.Warning,
        "{path}: {text}", ("path", path), ("text", text));

    /// <summary>A layout that resolves no technology at all. A WARNING, not an error — see this
    /// region's own header for why.</summary>
    public static Diagnostic CheckNoTechnology(string path) => Diagnostic.Create(
        "check.technology.none", DiagnosticSeverity.Warning,
        "{path}: no technology resolves for this layout — its own reference names none and the "
        + "workspace states no default, so its layers have no names and no rules to check against.",
        ("path", path));

    /// <summary>A <c>.cem</c> that resolves no layout: <c>EmSetupResolver</c> returned no source.</summary>
    public static Diagnostic CheckEmUnresolved(string path, string reason) => Diagnostic.Create(
        "check.em.unresolved", DiagnosticSeverity.Error,
        "{path}: {reason}", ("path", path), ("reason", reason));

    /// <summary>A cell reference on a schematic that resolves to nothing. The three states
    /// <c>CellSymbolResolver</c> keeps distinct stay distinct: this is <c>NotFound</c>.</summary>
    public static Diagnostic CheckRefNotFound(string path, string component, string cellRef) =>
        Diagnostic.Create(
            "check.ref.not-found", DiagnosticSeverity.Error,
            "{path}: '{component}' references '{cellRef}', which resolves to no cell folder.",
            ("path", path), ("component", component), ("cellRef", cellRef));

    /// <summary><c>CellSymbolResolver</c>'s <c>PrimaryMissing</c> — the cell is there, its symbol is
    /// not.</summary>
    public static Diagnostic CheckRefPrimaryMissing(string path, string component, string cellRef) =>
        Diagnostic.Create(
            "check.ref.primary-missing", DiagnosticSeverity.Error,
            "{path}: '{component}' references '{cellRef}', which resolves to a cell with no primary symbol.",
            ("path", path), ("component", component), ("cellRef", cellRef));

    /// <summary>
    /// A kit part reference that did not resolve, which headlessly is usually not a broken reference
    /// at all: a kit lives in a REGISTRY the GUI populates when it opens a workspace, and nothing
    /// populates it here. Reported as a warning naming that, rather than as the missing-cell-folder
    /// error a path reference gets — the two have completely different repairs, and calling every
    /// kit part in a PDK design a missing cell is exactly the noise that makes a check stop being run.
    /// </summary>
    public static Diagnostic CheckKitRefUnresolved(string path, string component, string cellRef) =>
        Diagnostic.Create(
            "check.ref.kit-not-loaded", DiagnosticSeverity.Warning,
            "{path}: '{component}' references the kit part '{cellRef}', and no kit registry is "
            + "loaded headlessly — the reference was not checked.",
            ("path", path), ("component", component), ("cellRef", cellRef));

    /// <summary>A reference that only resolved through the workspace's own move record. Not a
    /// failure — the cell was found — but the stored spelling is stale and will not survive the
    /// record being pruned.</summary>
    public static Diagnostic CheckRefRedirected(string path, string cellRef, string movedTo) =>
        Diagnostic.Create(
            "check.ref.redirected", DiagnosticSeverity.Warning,
            "{path}: '{cellRef}' resolved only through a recorded move, to '{movedTo}'. "
            + "Re-save the document to write the current path.",
            ("path", path), ("cellRef", cellRef), ("movedTo", movedTo));

    /// <summary><c>NetExtractor</c>'s own non-fatal naming conflicts.</summary>
    public static Diagnostic CheckExtractionConflict(string path, string text) => Diagnostic.Create(
        "check.schematic.conflict", DiagnosticSeverity.Warning,
        "{path}: {text}", ("path", path), ("text", text));

    /// <summary>Elaboration threw. This is the one that answers "does it resolve?" for parameters,
    /// expressions and cycles — all three arrive here as the expression engine's own message.</summary>
    public static Diagnostic CheckElaborationFailed(string path, string message) => Diagnostic.Create(
        "check.elaboration.failed", DiagnosticSeverity.Error,
        "{path}: elaboration failed — {message}", ("path", path), ("message", message));

    public static Diagnostic CheckElaborationWarning(string path, string text) => Diagnostic.Create(
        "check.elaboration.warning", DiagnosticSeverity.Warning,
        "{path}: {text}", ("path", path), ("text", text));

    public static Diagnostic CheckElaborationNote(string path, string text) => Diagnostic.Create(
        "check.elaboration.note", DiagnosticSeverity.Info,
        "{path}: {text}", ("path", path), ("text", text));

    /// <summary>
    /// No analysis of any kind will dispatch. <c>SelectTop</c>'s own sentence per kind, forwarded.
    /// A WARNING: a cell's schematic is not supposed to declare an analysis, and a `.cnl` written to
    /// be included by another is not either.
    /// </summary>
    public static Diagnostic CheckNoRunnableAnalysis(string path, string reasons) => Diagnostic.Create(
        "check.analysis.none", DiagnosticSeverity.Warning,
        "{path}: no analysis will dispatch. {reasons}", ("path", path), ("reasons", reasons));

    /// <summary>One DRC violation, at the severity the RULE states. The rule's own name, layer and
    /// measurement travel as arguments — a caller filtering on "every clearance violation" reads
    /// them, never the sentence.</summary>
    public static Diagnostic CheckDrcViolation(
        string path, string rule, string kind, DiagnosticSeverity severity,
        string? layer, string? measured, bool waived) => Diagnostic.Create(
        "check.drc.violation", severity,
        "{path}: {rule} ({kind}){onLayer}{measurement}{waiver}",
        ("path", path), ("rule", rule), ("kind", kind),
        ("layer", layer), ("measured", measured), ("waived", waived),
        // The three rendered fragments are separate arguments from the three TYPED ones above, and
        // named differently: a consumer reads `layer`, a reader reads `onLayer`. Reusing one name
        // for both would mean the typed value could not be read back without re-parsing the prose,
        // which is the exact thing R-aut4-4 exists to prevent.
        ("onLayer",     layer    is { Length: > 0 } ? $" on {layer}" : ""),
        ("measurement", measured is { Length: > 0 } ? $" — {measured}" : ""),
        ("waiver",      waived ? " (waived)" : ""));

    /// <summary>Anything the DRC run could not do — an unresolved instance, an unmapped
    /// cross-technology sub-cell, the flatten ceiling. Stated rather than dropped.</summary>
    public static Diagnostic CheckDrcNote(string path, string text) => Diagnostic.Create(
        "check.drc.note", DiagnosticSeverity.Warning,
        "{path}: {text}", ("path", path), ("text", text));

    /// <summary>A `.cws` entry — a library, a bookmarked file, the default technology — that names
    /// nothing. The GUI shows these as warning nodes in the project tree; this is the same finding.</summary>
    public static Diagnostic CheckWorkspaceRefUnresolved(string path, string what, string reference) =>
        Diagnostic.Create(
            "check.workspace.ref-unresolved", DiagnosticSeverity.Warning,
            "{path}: its {what} '{reference}' does not resolve.",
            ("path", path), ("what", what), ("reference", reference));

    /// <summary>The `.wasm` rule file would not read, or holds a rule its own parser rejects.</summary>
    public static Diagnostic CheckAssemblyRuleInvalid(string path, string rule, string message) =>
        Diagnostic.Create(
            "check.wasm.rule-invalid", DiagnosticSeverity.Error,
            "{path}: rule '{rule}' does not parse — {message}",
            ("path", path), ("rule", rule), ("message", message));

    // ── explain ──────────────────────────────────────────────────────────────
    //
    // R-aut4-8: `explain` never guesses and never falls back silently. Where resolution failed, the
    // failure IS the answer, and it names what was looked for and where it was looked — which is the
    // whole reason a caller reaches for this verb.

    public static Diagnostic ExplainPathRequired() => new(
        "explain.args.path-required", DiagnosticSeverity.Error,
        "explain: a path is required — a document, a cell folder or a workspace.");

    public static Diagnostic ExplainUnknownOption(string option) => Diagnostic.Create(
        "explain.args.unknown-option", DiagnosticSeverity.Error,
        "explain: unknown option '{option}'.", ("option", option));

    public static Diagnostic ExplainMultiplePaths() => new(
        "explain.args.multiple-paths", DiagnosticSeverity.Error,
        "explain: one path, please.");

    /// <summary>More than one of <c>--expr</c>, <c>--analysis</c> and <c>--ref</c>. Refused rather
    /// than ordered: each asks a different question and a document answering two of them at once
    /// would have to invent a precedence nobody stated.</summary>
    public static Diagnostic ExplainOneQuestion() => new(
        "explain.args.one-question", DiagnosticSeverity.Error,
        "explain: --expr, --analysis and --ref ask different questions — pass one.");

    public static Diagnostic ExplainPathNotFound(string path) => Diagnostic.Create(
        "explain.path.not-found", DiagnosticSeverity.Error,
        "No such file or folder: {path}", ("path", path));

    public static Diagnostic ExplainUnknownKind(string path) => Diagnostic.Create(
        "explain.path.unknown-kind", DiagnosticSeverity.Error,
        "Nothing circuitRF reads is named '{path}'.", ("path", path));

    public static Diagnostic ExplainUnreadable(string path, string message) => Diagnostic.Create(
        "explain.file.unreadable", DiagnosticSeverity.Error,
        "Could not read '{path}': {message}", ("path", path), ("message", message));

    /// <summary>The question needs a netlist or a schematic and this document is neither. Named
    /// rather than answered emptily.</summary>
    public static Diagnostic ExplainNotApplicable(string option, string kind) => Diagnostic.Create(
        "explain.option.not-applicable", DiagnosticSeverity.Error,
        "{option} asks about analyses and expressions, which a {kind} does not hold.",
        ("option", option), ("kind", kind));

    /// <summary>The expression would not evaluate. The engine's own message — an unresolved name, a
    /// cycle, a parse error — which is the whole content of the answer.</summary>
    public static Diagnostic ExplainExpressionFailed(string expression, string message) =>
        Diagnostic.Create(
            "explain.expr.failed", DiagnosticSeverity.Error,
            "'{expression}' does not evaluate here: {message}",
            ("expression", expression), ("message", message));

    /// <summary>A relative reference that resolved to nothing. Names what was looked for and the
    /// directory it was looked in (R-aut4-8).</summary>
    public static Diagnostic ExplainRefNotFound(string reference, string from) => Diagnostic.Create(
        "explain.ref.not-found", DiagnosticSeverity.Error,
        "'{reference}' resolves to no cell folder from '{from}'.",
        ("reference", reference), ("from", from));

    public static Diagnostic ExplainRefPrimaryMissing(string reference, string resolved) =>
        Diagnostic.Create(
            "explain.ref.primary-missing", DiagnosticSeverity.Error,
            "'{reference}' resolves to '{resolved}', which has no primary symbol.",
            ("reference", reference), ("resolved", resolved));

    /// <summary>The netlist declares nothing this verb would dispatch. <c>SelectTop</c>'s own
    /// sentences, one per analysis kind, forwarded rather than re-authored.</summary>
    public static Diagnostic ExplainNoRunnableAnalysis(string reasons) => Diagnostic.Create(
        "explain.analysis.none", DiagnosticSeverity.Warning,
        "No analysis will dispatch. {reasons}", ("reasons", reasons));

    /// <summary>A named analysis that is not declared at all.</summary>
    public static Diagnostic ExplainAnalysisNotFound(string name, string declared) => Diagnostic.Create(
        "explain.analysis.not-found", DiagnosticSeverity.Error,
        "No analysis named '{name}'. Declared: {declared}", ("name", name), ("declared", declared));

    // ── read (brief-automation-5-protocol-adapter.md §3) ─────────────────────

    public static Diagnostic ReadPathRequired() => new(
        "read.args.path-required", DiagnosticSeverity.Error,
        "read: a file is required. Give a result file (.npy or a Touchstone .sNp) or one of " +
        "circuitRF's own documents (.cws .csch .csym .clay .ctech .cem .cnl).");

    public static Diagnostic ReadUnknownOption(string option) => Diagnostic.Create(
        "read.args.unknown-option", DiagnosticSeverity.Error,
        "read: unknown option '{option}'", ("option", option));

    public static Diagnostic ReadMultiplePaths() => new(
        "read.args.multiple-paths", DiagnosticSeverity.Error,
        "read: one file at a time.");

    public static Diagnostic ReadPathNotFound(string path) => Diagnostic.Create(
        "read.path.not-found", DiagnosticSeverity.Error,
        "File not found: {path}", ("path", path));

    /// <summary>A directory. Refused rather than walked — what a workspace holds is what `check`
    /// and `explain` answer, and walking one here would return an unbounded document.</summary>
    public static Diagnostic ReadPathIsAFolder(string path) => Diagnostic.Create(
        "read.path.is-a-folder", DiagnosticSeverity.Error,
        "read: '{path}' is a folder. read takes one file; use check or explain for a workspace or a cell.",
        ("path", path));

    public static Diagnostic ReadFileUnreadable(string path, string message) => Diagnostic.Create(
        "read.file.unreadable", DiagnosticSeverity.Error,
        "Cannot read '{path}': {message}", ("path", path), ("message", message));

    public static Diagnostic ReadUnsupported(string path, string extension) => Diagnostic.Create(
        "read.file.unsupported", DiagnosticSeverity.Error,
        "read: nothing in circuitRF reads '{extension}' ({path}).",
        ("path", path), ("extension", extension.Length == 0 ? "(no extension)" : extension));

    /// <summary>An interchange file. Named rather than refused blankly, and pointed at the verb that
    /// does read it: half of these formats are binary, and handing back a GDSII stream as a JSON
    /// string would be an encoding decision this verb has no business making.</summary>
    public static Diagnostic ReadInterchange(string path, string format) => Diagnostic.Create(
        "read.file.interchange", DiagnosticSeverity.Error,
        "read: '{path}' is {format}, which convert reads. read takes a result file or one of " +
        "circuitRF's own documents.",
        ("path", path), ("format", format));

    // ── reference (brief-automation-6-reference-and-components.md) ───────────

    /// <summary>An unknown topic, LISTING the real ones — <c>--tech</c>'s precedent (AUT-3): never
    /// a fallback, because a fallback answers a different question than the one asked and says
    /// nothing about it.</summary>
    public static Diagnostic ReferenceUnknownTopic(string topic, string known) => Diagnostic.Create(
        "reference.topic.unknown", DiagnosticSeverity.Error,
        "No reference topic '{topic}'. Topics: {known}", ("topic", topic), ("known", known));

    /// <summary>A primitive nobody has heard of. The list is the whole catalogue, which is exactly
    /// the thing the caller was missing.</summary>
    public static Diagnostic ReferenceUnknownComponent(string type, string known) => Diagnostic.Create(
        "reference.component.unknown", DiagnosticSeverity.Error,
        "No primitive type '{type}'. Types: {known}", ("type", type), ("known", known));

    /// <summary>A second argument on a topic that does not have items. Only <c>components</c>
    /// does.</summary>
    public static Diagnostic ReferenceItemNotForTopic(string topic) => Diagnostic.Create(
        "reference.args.item-not-for-topic", DiagnosticSeverity.Error,
        "reference: '{topic}' is one page and names nothing inside it. Only 'components' takes a type.",
        ("topic", topic));

    /// <summary>A type named with no topic. Reachable from a tool call, where the two arguments are
    /// named rather than positional — refused rather than silently promoted to a topic, which would
    /// run a different query than the one asked.</summary>
    public static Diagnostic ReferenceItemWithoutTopic() => new(
        "reference.args.item-without-topic", DiagnosticSeverity.Error,
        "reference: a type was named with no topic. Ask for 'components' and the type.");

    public static Diagnostic ReferenceTooManyArguments() => new(
        "reference.args.too-many", DiagnosticSeverity.Error,
        "reference: at most a topic and one name. Usage: circuitrf reference [topic] [type]");

    /// <summary>A <c>resources/read</c> for a URI this server does not publish. Answered with a
    /// document naming the ones it does, not a protocol error frame — an unknown resource is the same
    /// mistake as an unknown topic and gets the same answer (R-aut-7).</summary>
    public static Diagnostic ReferenceUnknownResource(string uri, string known) => Diagnostic.Create(
        "reference.resource.unknown", DiagnosticSeverity.Error,
        "No reference resource '{uri}'. Resources: {known}", ("uri", uri), ("known", known));

    public static Diagnostic ReferenceUnknownOption(string option) => Diagnostic.Create(
        "reference.args.unknown-option", DiagnosticSeverity.Error,
        "reference: unknown option '{option}'", ("option", option));

    // ── serve (brief-automation-5-protocol-adapter.md) ───────────────────────

    /// <summary>No <c>--root</c>. Required at startup rather than defaulted: the server runs with
    /// the invoking user's authority, and a default of "the current directory" would be a policy
    /// nobody stated (R-aut5-8).</summary>
    public static Diagnostic ServeRootRequired() => new(
        "serve.args.root-required", DiagnosticSeverity.Error,
        "serve: --root <dir> is required. Every path a client names resolves under it, and there " +
        "is no default.");

    /// <summary><c>--json</c> on <c>serve</c>. It would take the stdout the protocol framing needs,
    /// and every tool call already returns a document.</summary>
    public static Diagnostic ServeJsonNotApplicable() => new(
        "serve.args.json-not-applicable", DiagnosticSeverity.Error,
        "serve: --json does not apply — stdout carries the protocol, and every tool call returns a " +
        "document of its own.");

    public static Diagnostic ServeRootNotFound(string path) => Diagnostic.Create(
        "serve.root.not-found", DiagnosticSeverity.Error,
        "serve: --root '{path}' is not a directory.", ("path", path));

    public static Diagnostic ServeUnknownOption(string option) => Diagnostic.Create(
        "serve.args.unknown-option", DiagnosticSeverity.Error,
        "serve: unknown option '{option}'", ("option", option));

    /// <summary>A path that leaves the root. Names the root, because that is the fact the caller is
    /// missing — and it is a refusal rather than a silent clamp, which would run a different
    /// operation than the one asked for and say nothing about it.</summary>
    public static Diagnostic ServePathOutsideRoot(string path, string root) => Diagnostic.Create(
        "serve.path.outside-root", DiagnosticSeverity.Error,
        "'{path}' is outside the server root '{root}'.", ("path", path), ("root", root));

    public static Diagnostic ServeUnknownTool(string tool, string known) => Diagnostic.Create(
        "serve.tool.unknown", DiagnosticSeverity.Error,
        "No tool named '{tool}'. Tools: {known}", ("tool", tool), ("known", known));

    /// <summary>A required tool argument that was not given. The adapter refuses rather than
    /// choosing for the client (R-aut-1).</summary>
    public static Diagnostic ServeArgumentRequired(string tool, string argument) => Diagnostic.Create(
        "serve.args.required", DiagnosticSeverity.Error,
        "{tool}: '{argument}' is required.", ("tool", tool), ("argument", argument));

    public static Diagnostic ServeArgumentUnknownValue(string tool, string argument, string value, string allowed) =>
        Diagnostic.Create(
            "serve.args.unknown-value", DiagnosticSeverity.Error,
            "{tool}: '{argument}' does not take '{value}'. One of: {allowed}",
            ("tool", tool), ("argument", argument), ("value", value), ("allowed", allowed));

    /// <summary>An argument that belongs to another mode of the same tool. Named as such, because
    /// "unknown option grid" is a worse answer than "grid is lp's, not lpp's".</summary>
    public static Diagnostic ServeArgumentNotForMode(string tool, string argument, string mode) =>
        Diagnostic.Create(
            "serve.args.not-for-mode", DiagnosticSeverity.Error,
            "{tool}: '{argument}' does not apply to {mode}.",
            ("tool", tool), ("argument", argument), ("mode", mode));

    public static Diagnostic ServeArgumentWrongType(string tool, string argument, string expected) =>
        Diagnostic.Create(
            "serve.args.wrong-type", DiagnosticSeverity.Error,
            "{tool}: '{argument}' expects {expected}.",
            ("tool", tool), ("argument", argument), ("expected", expected));

    /// <summary>The invocation threw. A server survives it and reports it; the alternative is a
    /// client whose connection simply ends.</summary>
    public static Diagnostic ServeToolFailed(string tool, string message) => Diagnostic.Create(
        "serve.tool.failed", DiagnosticSeverity.Error,
        "{tool} failed: {message}", ("tool", tool), ("message", message));

    /// <summary>The client cancelled. Its own exit code is 130, the one `em` already uses for a run
    /// stopped at a work boundary (cli.md §7).</summary>
    public static Diagnostic ServeCancelled(string tool) => Diagnostic.Create(
        "serve.tool.cancelled", DiagnosticSeverity.Error,
        "{tool} was cancelled.", ("tool", tool));
}
