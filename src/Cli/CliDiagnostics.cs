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
}
