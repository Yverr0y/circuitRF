# src/Cli — resolved findings

Findings worth keeping from work on the command-line driver. The design note is
`docs/design/cli.md`; this file records what turned out to be true while building against it, not
what the design says.

---

## AUT-1 — `--json`: structured results and structured failures (2026-09-05)

`brief-automation-1-structured-output.md`. Every verb gained `--json`; nothing that does not pass the
flag changed. The contract is now `docs/design/cli.md` §3.2.

### The gate that mattered, and what it actually proved

§6.1 asked for a golden-file test per verb asserting the human output is byte-identical before and
after. It is `tests/Ui.Tests/Cli/CliStructuredOutputTests` with committed bytes in
`tests/Ui.Tests/Cli/golden/`.

**It was also run the other way, against a pristine `git worktree` of HEAD, before any golden was
committed** — every verb, plus twelve `convert` refusal paths and one real DXF→GDSII conversion,
comparing stdout, stderr AND exit code. Result: **byte-identical everywhere except two absolute
paths** (the repository root inside a resolved SnP reference, and a scratch GUID in a temp
directory), neither of which is a behaviour difference. That is the evidence that moving
`PrintLoadpullGrid`'s and `PrintPursuitOptima`'s selections into `LoadpullResultSummary` moved no
number.

**The goldens carry `<ROOT>` and `<TMP>` placeholders, not paths.** A netlist's resolved SnP
reference and an explicit `-o` both print absolute paths, and a public repository must not carry
anybody's home directory. Two substitutions are applied to both sides and nothing else is
normalised.

### What "never a second computation" had to mean mechanically

R-aut1-1 is easy to agree with and easy to violate by accident, because the violation looks like
ordinary code. The console's loadpull table was making four decisions that are not formatting:

- each grid point is read at its **last converged, non-tickle drive step** — not a fixed index,
  which would mix compressed and uncompressed points in one column;
- **both cube spellings** are read (`Pout_dBm`/`Pout`, `Gt_dB`/`Gt`, `Efficiency`/`DE`), because a
  plain loadpull is Enriched and a pursuit's follow-on grid is not;
- efficiency's **scale follows from which spelling was found** (percent vs fraction);
- the grid axis is located **by name** (`gridPoint`), never by position.

Writing those a second time for the JSON would have produced two answers to the same question the
first time anyone changed one. They now live once, in `RfCore.Loadpull.LoadpullResultSummary`, which
returns values; the console formats them and the document serializes them. **The scale is carried,
not applied** — the summary hands out the engine's own number plus the factor the terminal
multiplies by, so a column headed `DE%` and a document field holding `0.6961…` are the same
measurement and say so.

### The two verbs whose result could not be shared, and why

Reported rather than absorbed, per the brief's own instruction:

- **`elab` has no `result`.** It is a development dump of the elaborated netlist — components,
  nodes, resolved parameters — and there is no `DataSet` anywhere in its path. Giving it one would
  mean inventing a result shape, which is a format decision this brief does not get to make. Its
  document carries `status`, `exitCode`, `diagnostics` and `outputs`, and that is honest.
- **`dc` HAD no shared selection, but it did have a DataSet.** `NonlinearDcEngine.Run` returns a
  `DcResult`, not a `DataSet`, so the console table reads node voltages and probe currents straight
  off it. `DcResultPacker.Pack` — the packer the GUI and `ParametricSweepEngine` already use — turns
  that same `DcResult` into the canonical cubes, so the document uses it. The two read one object
  and cannot disagree about a number; what they do not share is a selection **because the table has
  none** (it prints every node and every probe). Worth stating plainly rather than claiming a
  sharing that is not there.

### `--json` guarantees "nothing else on stdout" structurally, not by convention

The moment the flag is parsed, `Console.Out` is replaced with `TextWriter.Null` and the real stdout
is held in `JsonRun` until the document is written to it. A verb prints its table exactly as it
always did, into a sink. **This is why a later-added `Console.WriteLine` cannot leak into a caller's
parser** — a rule that every printer has to remember is a rule that eventually gets forgotten, and
the failure mode is a caller's parser breaking rather than a test going red.

`--json`, `--only` and `--group` are pulled out of the argument list before dispatch, the way
`--kits` already was. That is not only tidiness: `convert` refuses an unrecognised `-`-prefixed
argument, so a flag left in the list would have made `circuitrf convert x.dxf -o y.gds --json` a
usage error.

### NaN is not a JSON number, and pretending otherwise loses a fact

A loadpull grid genuinely contains NaN wherever a point never converged, and the console prints an
em dash for it. JSON has no number for NaN. The document uses System.Text.Json's
`AllowNamedFloatingPointLiterals`, so it arrives as the string `"NaN"`. **Dropping the key or
substituting a zero would turn "no measurement" into a measurement**, which is the same class of
error as printing `Pout=0 dBm` beside "DID NOT converge" — a mistake the pursuit printer had already
been written to avoid.

### Diagnostics: what was converted, and what is deliberately still prose

R-aut1-7 asked for counts rather than a guess.

| | Count |
|---|---|
| Ids in `src/Cli/CliDiagnostics.cs` (new) | **50** |
| Un-diagnosed refusals left in `Program.cs` / `LayoutConvert.cs` | **0** |
| Pre-existing ids elsewhere (`EmDiagnostics` 8, `FileAccessDiagnostics` 3) | 11 |
| Still prose: `Messages.Warning`/`Error` call sites in `src/Ui` | **296**, of which **124** launder an exception's `.Message` |
| Still prose: `Console.Error` sites in `src/Engine`, `src/Core`, `src/Design` | **41** |

Every `Console.Error.WriteLine` that precedes a `return 1` in either CLI file now goes through a
`Diagnostic`. What remains on those two files' stderr and is not itself a diagnostic is **usage text
and continuation lines** — the "Inferred: …" evidence under the Excellon refusal, the offending
coordinates under the GDSII overflow refusal, the pending layer mappings under the Gerber refusal.
Each of those already has a recorded diagnostic beside it carrying the sentence and the typed
values; the extra lines are detail for a human reading a terminal.

**Warnings authored deep in the engines are wrapped, not re-authored.** `elab.note`,
`elab.warning`, `em.run.note`/`warning`/`error`, `cli.measurement.failed` and `cli.worker.output`
each take an already-finished English sentence and give it an id. That is R-aut1-7's own last clause
and it is the right trade: an id is enough to filter, group and deduplicate on, whereas inventing
typed arguments would mean parsing the prose back apart — which is precisely what the coded form
exists to stop anyone doing.

### Brief premises that did not survive contact

- **§4 says `EmRunResult`'s "three lists … already carry a `Diagnostic` alongside the string".** They
  do not. `Notes`, `Warnings` and `Errors` are `IReadOnlyList<string>`; only the top-level refusal
  has a coded twin (`EmRunResult.Diagnostic`, non-null for every non-Ok status). The refusal is now
  read structurally — that part of the brief was right and the CLI had genuinely been discarding it
  — and the three lists are wrapped argument-free with an id naming which list they came from, which
  is the distinction the split exists for in the first place.
- **§4 and the architecture note both say adoption is "6 construction sites in 2 files".** It is 11
  across two files (8 in `EmDiagnostics`, 3 in `FileAccessDiagnostics`). Immaterial to the work, but
  the number is quoted in two documents and someone will check it.

### `convert`'s "Input not found" is not `sparam`'s "File not found"

Merging the two into one diagnostic changed one line of stderr and the before/after comparison
caught it immediately. It stays separate on its own merits as well: **`convert`'s input may be a
folder** (a Gerber file set), so "Input" is the accurate word and always was.

### An id minted from a caller's string is not a contract

The first cut had one `Convert(kind, text)` factory building `"convert." + kind` at each call site.
That is a set of permanent contracts nobody can enumerate, review, or hold still — and R-aut1-8 says
an id is chosen once. They are 29 named factories now (14 more scoped `cli.`, plus 7 forwarding and refusal ids), and
`CliStructuredOutputTests.DiagnosticIds_AreTheCommittedSet_UniqueAndCaseDistinct` asserts the whole
list against committed bytes, so adding one tells you to record it and renaming one tells you that
you have made a new diagnostic.

That test reads the ids **out of the source file** rather than by reflection, because
`tests/Ui.Tests` references `src/Cli` with `ReferenceOutputAssembly="false"` — the CLI is launched
as a process, not linked, so its types cannot be bound against. The regex anchors on
`DiagnosticSeverity.` following the literal, not on line layout: the first attempt anchored on
"a string alone on a line ending in a comma" and silently missed every single-line factory.

### A test trap worth remembering

`RunCli(params string[] args)` beside `RunCli(string? env, params string[] args)` compiles, and
`RunCli("hb", "file.cnl")` binds **`"hb"` to `env`**. Ten tests failed with
`No such command: 'file.cnl'` before the cause was obvious. A `params` overload whose extra leading
parameter is also a string is not an overload; it is a trap. The culture variant is named
`Launch(culture, …)` now.

---

## AUT-3 — `new workspace`, `new cell`, `import part` (2026-09-05)

`brief-automation-3-authoring-verbs.md`. Three verbs that create a correct INITIAL document with no
display attached, plus the extraction that lets them exist. The contract is `docs/design/cli.md` §2
and §9; the capabilities themselves are recorded in `src/Design/RESOLVED.md`.

`src/Cli/Authoring.cs` is ~590 lines and none of them create anything. That is the point: every verb
calls the function the GUI's own command calls, so what is left in the CLI is argument parsing,
refusals and reporting. The gate is `tests/Ui.Tests/AuthoringCliVerbTests` (25 tests, ~2 s), written
the way `EmCliVerbTests` is — the real CLI as a process, compared byte for byte against the
in-process call.

### The brief's stated `--views` default contradicts its own reason, and the reason won

R-aut3-8 says the default is "symbol and schematic, matching the GUI's own New Cell". **The GUI's New
Cell creates a schematic and nothing else** — `NewCellAsync` calls `CellFolder.CreateCellFolder` and
then `CreateAndOpenSchematicFileAsync`; the symbol and layout sub-folders are made and left empty
(R-cc-1: "a New Cell always creates that cell's primary schematic", singular). So the stated value
and the stated reason disagree, and the reason is the rule the whole brief runs on — R-aut3-3's "a
headless default that differs from the dialog's is a second product". **`--views` defaults to
`schematic`.** `--views symbol,schematic` is one flag away for a caller that wants both.

### `import part` has no session, so the layer install cannot be copied — only reported or written

The GUI's `ApplyImportToTechnology` installs the part's new layers into the SESSION's live technology
and says in as many words that **nothing was written to disk**; the user keeps them by opening the
technology and saving it. Headless there is no session, so "do what the GUI does" is not available:
the honest options are to report them or to write them, and which one a caller wants is not
something to guess. So they are **always reported**, and `--add-layers` writes them into the
resolved `.ctech`. Reporting is not optional — a layer silently dropped is the trap
`src/Design/RESOLVED.md` already records for `convert`.

**A null destination technology reports NO layers as new**, which is the same trap from the other
side: with nothing to reconcile against, `LayersToAdd` comes back empty and the part's layers land
with numeric keys and no names. The GUI is in exactly that state with a technology-less workspace, so
the verb matches it and says so (`import.no-technology`) rather than inventing a technology the GUI
would not have had.

### `--variant` cannot be the candidate's Location, which is what the chooser shows

The brief's `--cell N` / `--variant V` map onto the chooser dialog's two distinguishing columns, and
the obvious reading of `--variant` is `ComponentCandidate.Location` — the folder a candidate came
from, which is what separates "one part written out once per target format". **That reading is inert
on the ordinary case.** `testdata/component-samples/widget9` holds one part as three candidates in
ONE folder — a `.kicad_sym` symbol, a `.lib` symbol, and the bare land pattern — so all three share a
name, a Location of `""` and a family. Neither flag could name one.

`--variant` therefore takes the Location when there is one and otherwise the **extension of the file
the candidate begins at** (its symbol file, else its first footprint) — which is the same file
`DisplayName` is taken from, so nothing new is invented. `--list-parts` prints exactly that key in
its second column, and the ambiguity refusal prints the whole listing.

### Two things the argument shape had to settle

- **`new workspace <dir>` with no `--name` treats `<dir>` as the workspace itself**, and with
  `--name` as its parent. Both are how a person types it and the flag says which was meant.
- **`--tech none` is how a caller asks for the dialog's own "None" row.** An absent `--tech` cannot
  mean "none": R-aut3-3 binds it to what the combobox opens on, which is a real technology.

### 29 new diagnostic ids

`new.*` (14) and `import.*` (15), all recorded in `CliStructuredOutputTests`' committed list. That
list is asserted in **ordinal order over the whole set**, so a new group cannot simply be appended —
the first attempt appended `import.*` after `lp.export.no-surface` and failed on position 49.
