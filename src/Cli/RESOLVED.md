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

---

## AUT-4 — `check` and `explain`, and the DRC engine below the firewall (2026-09-05)

`brief-automation-4-check-and-explain.md`. Two read-only verbs that close a headless client's loop.
The contract is `docs/design/cli.md` §10; this records what turned out to be true while building it.

### The finding that changed the design: a `.csch` goes through the `.cnl`

**The first `check` reported four errors the application does not have**, on four schematics in the
owner's own workspace: *"elaboration failed — Unresolved name 'on' in scope 'global'"*.

The cause is an asymmetry between two readers that was already documented in two places, each
contradicting the other. A Tuner's `BiasTee` parameter is stored bare (`on` / `off`), and:

- `ComponentTypeRegistry`'s `BiasTeeOptions` says the value is committed bare because "`CnlReader` is
  what quotes it on the way to the elaborator";
- `HarmonicaSchematicExport` writes `"\"off\""` QUOTED, with a comment saying a bare `off` "resolves
  as a variable name and elaboration fails with Unresolved name 'off'".

Both are right, about different paths. `CnlReader` quotes a bare word; `NetExtractor` does not
(`AsLiteralExpression` exists and is applied only to a KIT's fixed parameters). And the GUI's Simulate
does not hand extraction straight to the elaborator — `WorkspaceViewModel.WriteNetlist` writes a
`.cnl` and `SchematicRunService.Prepare` reads it back, so every schematic parameter is laundered
through `CnlReader` on the way. Verified in both directions: `testdata/Hero3/hero3.cnl` carries
`BiasTee=on` and `circuitrf elab` resolves it without complaint.

**So the round trip is load-bearing, and `src/Cli/CircuitSource.cs` performs it — in memory, since
R-aut4-6 forbids writing.** This is R-aut4-2's rule in the mirror: a rule that lives only in `check`
is a rule the GUI does not enforce, and a rule `check` applies that the GUI does not is just as bad.
With the round trip, those four schematics check clean.

**Not fixed here, and worth deciding separately:** the two comments above still contradict each
other, and a `.csch` handed directly to `Elaborator` by any future caller will hit the same wall.
Either `NetExtractor` should apply `AsLiteralExpression` to a Tuner's string-valued parameters as it
does to a kit's, or `ComponentTypeRegistry` should commit them quoted as `HarmonicaSchematicExport`
already does. Both change what new schematics write, and neither repairs an existing file, which is
why this brief left it alone.

### R-aut4-3: the DRC engine moved, and the closure really was free

`src/Ui/Layout/Drc` → `src/Design/Layout/Drc`, plus `src/Ui/Layout/Assembly` (the `.wasm` rule-file
model) → `src/Design/Layout/Assembly`. **The whole move produced five compiler errors**, and none of
them was a coupling: a stale `using CircuitRF.Ui.Schematic` in `WasmPersistence`, an `AtomicFile`
that resolves to `CircuitRF.Design.Cells`' one anyway, and three fully-qualified `Ui.WBond.WBondSnap`
calls. `tests/Ui.Tests` passed **unchanged**, 12,053 of them.

**Two files stayed, on purpose, and neither is the engine:**

- `DrcRunReport` — posts a run's verdict to the Messages panel. It takes an `IMessageSink`; that is a
  UI surface, not a design rule.
- `WBondWireClearance` — reads the built-in wire clearance from the per-USER preferences file. The
  engine already takes the number as `DrcRunSettings.WireClearanceNm`, so the preference is the
  GUI's to read and the default (circuitRF's own half a mil) is the right answer for a caller with no
  user to ask.

**One thing had to move that the brief did not list**: `WBondClearance` converts a layout into
nanometres through `WBondSnap.ToNm`, and `WBondSnap` cannot cross — it needs `LayoutSnapQuery` and
`SnapFeatureKind`, which ARE the layout editor. The two-line integer pair moved to
`LayoutUnits.NmToDbu`/`DbuToNm` instead and `WBondSnap` forwards to them, so there is still exactly
one implementation — the property `WBondClearance`'s own header depends on, having shipped broken
twice already from a second copy. **The arithmetic is unchanged, `double` and all.** Re-deriving it
in `decimal` beside `LayoutUnits`' other pair would be more exact past 2^53 and would also change
measured clearances, which is a numeric change smuggled in under a file move.

**26 allow-list entries, moved not authored.** `UserFacingTextGateTests` fires on user-facing text
below the firewall, and the `.wasm` predicate parser's messages are user-facing text: they are what a
person who wrote a bad rule expression reads. They are listed in
`tests/Firewall.Tests/user-facing-text-allowlist.txt` under a dated "moved, not authored" heading
rather than converted, because R-aut4-3 moves whole files without reshaping them and converting 26
parser messages under cover of a file move is the change nobody could review. They are also the one
family where a plain sentence is nearly defensible — a parse error already carries the offending TEXT
and a character POSITION, which is the typed half a `Diagnostic` would have added.

### `SelectTop` had to become a function that returns a decision

`explain --analysis` reports what chain selection would do WITHOUT doing it, and the old `SelectTop`
had no account of itself beyond two `Console.Error.WriteLine` calls. It is now
`ChainSelector.Select`, returning `Selected` / `Candidates` / `Requested` / `PromotedFrom` / `Why`,
and the CALLER writes the sentence — the run verbs write exactly the two they always wrote (R-aut0-3
holds: stderr is unchanged character for character), `explain` writes none and renders the same facts.

**One honesty problem surfaced immediately.** `SelectTop` ends `return owner ?? named`, so
`lp -a HB1` hands back HB1 — an HB, to the loadpull verb. The first `explain` reported that as "lp
dispatches HB1", which describes a run that cannot happen. It now counts a selection only when the
chain it picked bottoms out in that verb's own base analysis. The behaviour of the RUN verbs is
untouched; what changed is what `explain` claims about them.

### What `check` needed and no existing validator provided — three gaps

R-aut4-2 says a finding with no validator behind it is the most valuable thing this brief can turn
up. Three:

1. **"Does this document declare a runnable analysis at all?"** exists exactly once, as a private
   pair inside `SchematicRunService.Prepare` (`src/Ui`, above the firewall): a typed analysis, OR a
   RAW `analysis … type=sparam` directive, which never becomes a typed one. `check` could not call it
   and `CircuitSource.DeclaresARunnableAnalysis` restates it. **It is worth pulling down beside the
   netlist model** — it is the GUI's own `RunStatus.NoAnalysis` test and nothing about it is a UI
   concern. Asking `ChainSelector` instead is NOT equivalent and the first attempt proved it: chain
   selection is per KIND, so a bench declaring only an S-parameter sweep has no HB chain and every
   S-parameter and DC document in the tree was warned about.
2. **A `.cws`'s own reference lists.** `LibraryRefs`, `KnownFiles` and `DefaultTechRef` are shown as
   warning nodes by the project tree, but that rule lives in the tree's view models rather than in a
   validator, so `check` tests existence itself. A `WorkspaceValidation.Analyze` returning typed
   problems the way `TechValidation` does would serve both.
3. **Unconnected nets have no validator anywhere.** The brief's §1 lists them among the soundness
   questions and nothing in the tree answers one: elaboration numbers nodes and `NetExtractor`
   treats an unconnected pin as ground "for safety" (`NetExtractor.cs:1730`). `check` reports
   nothing about them, deliberately — inventing the rule here would have been the thing R-aut4-2
   forbids.

### §5.2 — the shipped documents, in full

| Tree | Result |
|---|---|
| `src/Ui/resources/schematic-templates` (4 `.csch`) | **clean** — 0 errors, 0 warnings |
| `src/Ui/resources/doc-schematics` (4 `.csch`) | 0 errors; **1 warning**, `Inline_Value_Editor.csch` declares no analysis — which is correct, it is a documentation figure |
| `src/Design/resources/technologies` (5 `.ctech`) | **clean at `--severity warning`** |

Both schematic trees are asserted in `CheckAndExplainCliVerbTests.ShippedSchematics_CheckClean`, so
nobody has to remember to look.

The repository ships no example WORKSPACE — `circuitRF_demo/` is the owner's own tree and is not
committed — so it was checked as evidence rather than as a gate: **30 documents, 0 errors, 2
warnings.** Both warnings are real states the application would also show (one cell declares no
analysis; one cell's symbol sub-folder holds two files with no primary chosen).

### §5.7 — the measurement, and where the time actually goes

Debug build, warm, wall clock including the ~35 ms process start.

| Input | Time |
|---|---|
| 30-document workspace, layouts empty | **0.20 s** |
| 4 documents, one 5,000-shape layout, spacing rule, no violations | **0.45 s** |
| the same 5,000 shapes with 19,577 violations | **0.95 s** |
| 20,000 shapes with 79,154 violations | **4.9 s** |

**Fast enough to call after every edit, and the cost is DRC, not the walk.** Reading and elaborating
30 documents is a fifth of a second; a single layout with real geometry is more than all of them
together, and the violation COUNT costs as much as the shape count because every violation is
rendered and recorded. A design with thousands of outstanding violations is not the case to optimise
for — it is the case to fix — but a caller checking a large board on every edit should point `check`
at the document it is editing rather than at the workspace.

No timing test was added (`feedback-no-new-timing-benchmark-tests`): a wall-clock assertion measures
the machine and flakes.

### `convert`'s carry-through, which was asked for and turned up three real gaps

`convert` answered `--json` from AUT-1, but three things a caller needs reached only the terminal.
Each was found by RUNNING the verb, not by reading it:

1. **`--list-cells` produced an empty document.** The listing goes to stdout, which `--json`
   replaces — so the one invocation whose entire result is a list answered with `"diagnostics": []`.
   Now `convert.cell.listed`, one per cell.
2. **The import's own notes were not in `diagnostics`.** "1 × unfilled zone … not imported", "F.Cu→
   added", "the technology's stackup was left EMPTY" — these are how a caller learns what a
   conversion DROPPED, and a `--json` consumer was reading a report that omitted the losses. Now
   `convert.note`, forwarded argument-free like the other engine-authored sentences.
3. **The minted `.ctech` was not in `outputs`.** Headless there is no workspace to graft layers onto,
   so an import writes a technology of its own — and the cells it produced reference it by RELATIVE
   PATH, so a caller that took the cells and not that file has a design whose layers resolve to
   nothing. It is now reported **exactly when it survives**: the target was `clay`, or `--keep-cells`
   named somewhere. Reporting a path that is about to be deleted with the scratch directory would be
   worse than reporting nothing, which is why `MintTechnology` records it and `Run` decides.

The fourth carry-through is in the other direction and is in `check`/`explain`: an interchange file
is classified through **`convert`'s own `DetectSource`** — including its content sniff, the only thing
that can name a Gerber or Excellon file — rather than through a second table. A GDSII file `convert`
can read is reported as interchange, never as something circuitRF does not handle.

### Smaller things worth keeping

- **`check`'s findings go to stderr, not stdout.** They are not a RESULT (`cli.md` §3.1); stdout
  carries the one-line tally the way every other verb's stdout carries its table. That is also what
  lets `check … --json` put the findings in `diagnostics` with nothing else in the way.
- **A waived DRC violation is reported as a NOTE, not at the rule's own severity.** §9A.1 requires
  waiving to be "persisted, and visible", and counting a waived violation against the exit code would
  make a fully-waived design fail CI forever.
- **`explain`'s three questions are refused together rather than ordered.** `--expr`, `--analysis`
  and `--ref` ask different things and a document answering two would need a precedence nobody stated.
- **`--severity` decides only the exit code.** Warnings are always reported. R-aut4-5 is explicit
  about why: a check that hid warnings to keep the exit code clean makes the exit code useless.
- **`NameValidator`'s finding is unreachable on Windows and the test says so.** Every name it rejects
  is a name Windows itself refuses — that is why the rule exists — so the broken fixture cannot be
  created there. `NonWindowsFactAttribute` skips WITH A REASON rather than asserting something weaker.
- **A kit part that does not resolve is a WARNING naming the registry, not a missing-cell error.**
  A `pdk://` reference lives in a registry the GUI populates when it opens a workspace and nothing
  populates headlessly; `check.ref.kit-not-loaded` says that. Reporting every part in a PDK design as
  a missing cell folder names the wrong repair and is exactly the noise that stops a check being run.
- **43 new diagnostic ids** — `check.*` (28), `explain.*` (13) and `convert.note` /
  `convert.cell.listed` — bringing `CliStructuredOutputTests`' committed list to 122. That list is
  asserted in ordinal order over the whole set, so a new group cannot be appended.

---

## AUT-5 — `circuitrf serve`, and `circuitrf read` (2026-09-05)

`brief-automation-5-protocol-adapter.md`. One protocol adapter, a parity gate, and the one capability
gap building it exposed. `docs/design/cli.md` §11 is the design; this is what turned out to be true.

### The adapter calls the verb, and that decided the shape of the whole change

R-aut-13 says nothing may be reachable through the server that is not reachable through the CLI, and
vice versa. There are two ways to satisfy it: build a second caller of the capability layer and write
a test that compares the two, or **make them one caller**. The second is strictly better here,
because the first is a second copy of the wiring — which agrees with the original right up until one
of them is edited, and the test then tells you a year later.

So a tool call becomes an **argument vector** and is handed to `CliEntry.Run`, the same function
`Program.cs` hands the real command line to. The parity gate then compares two documents that came
out of one function, and a drift between the adapters is not a thing that can happen.

**What that cost: `Program.cs`'s top-level statements and its 40-odd static local functions moved
wholesale into `CliEntry`.** A local function of a top-level program is a member of `<Main>$` and is
private to it; `public partial class Program` makes the generated CLASS accessible but not those. The
move is mechanical — the functions were already `static`, so they became static methods unchanged —
and `Program.cs` is now three lines.

### Three pieces of state outlived the single invocation the CLI was written for

Calling `Run` twice in one process is not what any of this was built for, and each of the three was
found by running the server rather than by reading the code:

- **`JsonRun`'s collectors.** A second document carried the first call's diagnostics and outputs — a
  stale success a caller cannot tell from a real one. `JsonRun.Reset()` now runs before every call.
- **The device-worker log subscription is an EVENT.** A second `Run` subscribed a second handler and
  every worker line printed twice, which reads as the worker having said it twice. Hooked once.
- **`ExternalDeviceRegistry.AddResolver` has no remove.** The same `--kits` folder set would stack an
  identical resolver per call; the folder sets already added are remembered.

None of the three is observable from a command line, and all three would have been observable to a
client as something wrong with circuitRF rather than with its adapter.

### The capability gap: nothing could hand a file back

**Reported as the brief asks, because by R-aut-1 it is a capability gap and should have been caught
in an earlier brief.** The tool surface §3 specifies has a `read` in it — "read a document or a
result file back" — and there was no verb behind it. A tool with no verb is exactly the privileged
adapter R-aut-13 forbids, and the parity gate for it could not have been written at all.

It is now `circuitrf read`, exposed on **both** adapters in the same commit. It adds no logic: a
`.npy` goes through `DataSetImporter`, a Touchstone through `TouchstoneIO` + `DataSetBuilder.FromSnp`
— the same pair `DataSourceEntryViewModel` uses to put a file into the Data Display's source library
— and one of circuitRF's own documents comes back as its own bytes. `ResultPayload` gained one field,
`document`.

**Verbatim, not re-serialized**, and that is a decision worth recording: the formats ARE the
interface, so a client that just wrote a `.csch` needs the file back, not a round trip through a
reader and a writer that would differ from disk wherever the reader is lossy — invisibly.

Two things `read` refuses rather than guessing at: a **directory** (what a workspace holds is what
`check` and `explain` answer, and walking one returns an unbounded document nobody asked for), and an
**interchange file**, which is refused *naming `convert`* — half those formats are binary, and
handing back a GDSII stream as a JSON string is an encoding decision this verb has no business
making.

### Every stdout leak §5.2 found: none — and why that is not luck

The audit runs every tool the server exposes, including a real EM run and a DC run whose device model
lives in a second process, with stdout captured, and asserts every line of it is a protocol frame.
**Nothing leaked.**

That is structural rather than fortunate. `JsonRun` already replaced `Console.Out` with a sink the
moment `--json` was parsed — for its own reason, R-aut1-3 — and `serve` replaces it again at startup
before a single capability runs, so a stray `Console.WriteLine` on a path nobody thought about has
nowhere to land. The three chatterers the brief names (`PrintWorkerOutput`, the
`ProcessDeviceWorkerTransport.Logged` hook, `EmProgressToStderr`) were already on stderr and are
untouched.

**The one thing a `Console.Out` redirect cannot cover is a CHILD process inheriting the real stdout
handle**, and that was checked rather than assumed: every process this program starts — the device
worker, the PCell host, the Python interpreter probe, the updater — sets
`RedirectStandardOutput = true`. Four call sites, all of them. A future one that does not would leak
into the protocol stream, and the §5.2 audit only catches it if the new launcher is on a path the
audit exercises.

`--json` on `serve` itself is **refused**, not silently ignored: it would have captured the very
stream the framing needs, and two writers on one stream is precisely R-aut5-2's failure. It is
refused **after** the argument and root checks, not before — so `serve --root <missing> --json` still
answers with a document carrying `serve.root.not-found`, the way every other verb's refusals do
(R-aut-7). Only the case where the server would actually start has no document to give, because from
that point stdout belongs to the protocol.

### Root confinement: the escape a string comparison cannot see

`../` and an absolute path outside the root are both easy. The third is not, and the first
implementation had it wrong: **`ResolveLinkTarget` answers about the item you call it on**, so asking
it about `<root>/link/file.cnl` reports "not a link" — the *file* is not one, the *directory* above it
is — and the escape goes straight through a comparison of resolved strings. The path is now walked
from its root downward with every component resolved in turn. The test that caught it is the symlink
case in `APathLeavingTheRoot_IsRefused_NamingTheRoot`, which failed on the first run.

Both sides are resolved, not just the candidate: on macOS `/tmp` is itself a link to `/private/tmp`,
so resolving only one side refuses perfectly legitimate paths.

A non-existent path — an output file — resolves its deepest existing ancestor and re-appends the
tail, which is the only thing that can be done and is what the check has to mean anyway: a file is
created inside the directory it lands in.

### Where the parity test could not be written exactly as specified

**Two normalizations, and both are properties of the operation rather than of the adapter.** The
brief allows "only what is legitimately variable (a write timestamp, as `EmCliVerbTests` already
exempts for provenance)"; these are the analogues.

1. **The adapter resolves paths** — that IS R-aut5-8's confinement — so the document records an
   absolute path where a CLI invoked with a relative one records the relative one. The CLI side of
   each comparison is therefore given the resolved path: the same path, spelled the way the server
   had to spell it.
2. **A verb that CREATES something cannot create it twice.** AUT-3's R-aut3-6 refuses to overwrite an
   existing workspace, correctly, so `create` and `import` run into two different destinations and
   the destination string is substituted out of both documents. Everything else — including the
   copied technology's file name and the full outputs list — is compared verbatim.

Nothing else is exempted. For `run`, `check`, `explain` and `read` the documents are byte-identical
with no substitution at all.

**`--kits` is not a tool argument, deliberately, and it is not a parity hole.** It is one of the flags
taken before dispatch, so `serve --root <dir> --kits <dir>` registers the resolver for the whole
server and every `run` through it resolves an external device model. Making it a tool argument would
let a client point the server at an arbitrary directory on its say-so, and a kit folder is installed
software that lives outside the design root by nature. The parity test passes `--kits` to both sides
equally.

### Cancellation is pinned through the queue, not through a stopwatch

R-aut5-8 wants a long run cancellable. Wiring it was small — `RunHost` carries the host's
`RunControl`, the same one `em` already used, into the sparam, sweep, loadpull and pursuit call sites,
and is null on a command line so nothing there changed.

**Gating it honestly was the harder half, and the finding is that there is no long run in the default
test tier.** Measured: the `em` fixture at 3 frequency points is 180 ms; at 400 points it is still
under a second; `lp` on Hero3 is 0.20 s; a 10,001-point `sparam` on Hero1 is 0.17 s. The one thing
that genuinely takes minutes is a de-embedded full-wave point, which is `Category=Benchmark`
territory and cannot live in the routine gate. A sleep-then-cancel test would therefore be measuring
the machine, which is exactly what this repo says not to write.

So what is pinned is the **mechanism**: capability calls are serialized, so of two calls sent
together the second is queued while the first runs, and cancelling *that* one is deterministic. The
token still reaches the run, the run still refuses to produce a result, and 130 still comes back — the
code `cli.md` §7 already gives a run stopped at a work boundary. The in-flight case is the same code
path with the token cancelled a moment later.

**Progress is gated on a 4,000-point EM sweep** (~0.3 s), which is enough for the throttle to deliver
several observations and for the assertion "it advanced" to mean something. A single observation
repeated is a bar that never moves, which is what a client with no progress already has.

### A client that disconnects: the deadlock was in the TEST, and the fix belongs in both

The first version of the disconnect gate took 30 seconds and passed. The server's shutdown joins its
worker for 30 s, and the worker was blocked **writing a result document to a pipe nobody was
reading** — the test had closed stdin but still held the stdout handle open, so there was no `EPIPE`,
just a full buffer.

Two changes came out of it, and only one is a test change:

- the session drains **both** pipes continuously on their own threads. A result document for a
  400-point sweep is comfortably past a pipe's buffer, so reading stdout only when an answer is
  wanted deadlocks — the same trap `EmCliVerbTests` already records for stderr, on the other stream.
- `JsonRpc.Send` treats a broken pipe as **the far end being gone** and stops writing. A real client
  that exits closes its read end, and every subsequent frame would otherwise throw on a worker thread
  for a frame nobody will read.

### The tool schema is generated from the translation table

`ToolCatalog` is one list of rows, and both the advertised JSON schema and the argument vector are
built from it. This is not tidiness: an advertised argument that the translation drops is a client
paying for a description of something that does not work, and it is the single most likely way this
file rots. Adding a flag is one row.

An argument that belongs to another mode of the same tool is refused **naming that** — "`grid` does
not apply to lpp" rather than "unknown option `grid`" — because the two sentences send a reader to
different places.

### The README's three source-tree lists had drifted, and now have a gate

§7 asked for the annotations this series falsified to be corrected. Read against the tree rather than
assumed, the corrections were: `src/Design` gained `Schematic/`, `Symbol/`, `Layout/Assembly/`,
`Layout/Interchange/`, `Theming/` and `resources/`; the `Drc/` annotation was **inverted** (the engine
moved in AUT-4, so "the DRC engine stays in src/Ui" was exactly backwards, and what is left in
`src/Ui/Layout/Drc` is the Messages report and a per-user preference); `src/Ui/Layout/Interchange/`
does not exist any more; `src/Ui/Schematic/` is the editor half only; and the firewall paragraph said
**seven** assemblies while the gate lists **eight** — `src/Diagnostics` was missing from all three
lists and from the source-layout tree entirely.

`tests/Ui.Tests/ReadmeSourceLayoutTests` now holds it: every `src/…` folder the tree draws must exist,
every project under `src/` must be drawn, and the firewall paragraph must name every project
`UiFirewallTests.NonUiAssemblies` gates — including the count, spelled out. It cannot check prose, and
does not pretend to; what it does is fail at the moment somebody is already reading the sentence next
to the stale path.

### Firewall

`serve` lives in `src/Cli/Serve/`, so it is inside a project `UiFirewallTests.NonUiAssemblies`
already gates (R-aut5-3 needed no new entry). It has no dependency of its own: the JSON-RPC framing
is hand-rolled over `System.Text.Json`, ~130 lines, because the protocol layer is the disposable one
by design and a package here would outlive the adapter it serves.

---

## Post-series review — the catalog advertised flags the verbs did not read (2026-09-05)

A review of the whole AUT-1 … AUT-5 series, after it landed. Three defects and one latent one; the
findings below are what the per-tool parity gate could not see, and why.

### Six advertised arguments were not flags at all

`ServeProtocolAdapterTests`' parity tests make **one call per tool**, so they compare the two
adapters on the arguments that call happens to pass and say nothing about the rest of the table.
Read against the verbs' own argument loops, `ToolCatalog` was offering:

| Mode | Advertised | The verb's loop reads |
|---|---|---|
| `run/sparam` | `-a`, `--set` | neither — `--freq` and `-o` only |
| `run/dc` | `--set`, `--tol`, `--maxharm`, `--maxmix` | none of them — `--max-iter`, `--dc-steps`, `--gmin` |
| `run/lp`, `run/lpp` | `--maxmix` | HB's alone; the loadpull loop has no case for it |

**And the failure was not "unknown option".** Four of the five run verbs find their input by *"the
first token that does not start with a dash"*, so a dropped flag's **value** became the input path —
`lp x.cnl --maxmix 3` answered `File not found: 3`, a refusal naming neither the real problem nor the
file the caller gave. `dc` reads its path positionally instead, so `dc x.cnl --set Vg=1` reported
nothing at all and ran without the override: a run answering a different question than the one asked.

Fixed in both directions. The catalog now names only flags the verb reads (`SolverOptions` is HB's;
`LoadpullSolverOptions` and `DcOptions` are the other two), **and all five run verbs now refuse an
unrecognised option** (`cli.args.unknown-option`), which `convert`, `new`, `import`, `check`,
`explain` and `read` have done since they were written. `dc` also gained
`cli.args.multiple-inputs`, since its own scan is the only place a second positional is visible.

**That refusal is what makes the gate behavioural rather than a source scan.** With it,
`EveryAdvertisedArgument_IsAFlagTheVerbActuallyReads` asks the real server for the real schema, calls
**every** advertised argument of **every** mode against a path that does not exist — every argument
loop runs to completion before the file is looked at, so nothing is read, run or written — and fails
on any `.args.unknown-option`. Verified red by putting `--maxmix` back on `LoadpullSolverOptions`.

### `--analysis ""` was refused by the verb its own description described

`explain --analysis` takes its name as an **optional following token**, so the catalog's `OptKind.Str`
forwarded an empty string as a value and `explain` looked for a chain called `""` — *"No analysis
named ''"* — while the argument's description said an empty string asks about all of them. The
description was the true half. `OptKind.StrOptional` emits the bare flag for an empty string; a name
still selects one chain.

### `convert` could do two things through the CLI that the protocol could not

`--workspace` and `--dbu` are both real `LayoutConvert` flags and both are documented in
`docs/user/src/reference/cli.md`; neither was in the catalog. That is R-aut-13's *"or vice versa"*
half — the direction a per-tool parity test cannot fail on, because it only compares the calls it
makes. Both added.

### Progress notifications raced the result they were meant to precede

`RunHost.Install` wrapped the observer in `Progress<RunProgress>`, which captures the
`SynchronizationContext` at construction and, where there is none, posts every observation to the
thread pool. The tool call runs on `McpServer`'s worker thread, which has none — so notifications
raced each other and the result frame. A client would see a bar that jumps backwards and observations
arriving for a call it has already been told finished. Replaced with a synchronous `IProgress<T>`;
the only observer is a JSON-RPC write, which is serialized and already throttled by
`RunControl.MinReportIntervalMs`.

### Left standing, and reported rather than absorbed

- **`sparam` and `dc` take no `--set`.** Every other run verb does, `cli.md` §5 describes it as the
  CLI's override mechanism, and its absence means a bias or frequency global cannot be overridden for
  the two oldest verbs — headlessly or through the protocol. Adding it is a capability change to
  verbs this series did not otherwise touch, so it is named here rather than folded into a review.
- **`sparam` takes no `-a`.** It runs the first typed `SParameterAnalysis`; a netlist declaring two
  has no way to say which.
- **`elab` is a verb with no tool.** It is reachable from the command line and not through `serve`,
  which is the R-aut-13 asymmetry the series otherwise closed. It is a development dump rather than a
  capability, and `explain` answers most of what it is reached for — but the omission is not recorded
  anywhere as deliberate, so it is recorded here.

### `convert`'s treatment at each layer, audited

Asked for directly, because the briefs name `convert` only in passing.

- **AUT-1 (`--json`)** — complete. `LayoutConvert` carries 41 `JsonRun` calls: `InputPath`, six
  `AddOutput` sites (a Gerber file set writes several), and **11 distinct refusal ids plus 12
  `Note`s**. It is the single most `Diagnostic`-dense verb in the CLI, which is right — it is the one
  with the most ways to be told no.
- **AUT-5 (`serve`)** — reachable, as `import { what: "convert" }`, and **parity-tested byte for
  byte** against `circuitrf convert --json` (`Import_ThroughTheServer_IsTheDocumentTheCliWrites`).
  Two flags were missing from the catalog and are now added; see above.
- **The §6.1 human-output golden set is `sparam`, `dc`, `hb`, `lp`, `lpp`, `elab` — and that is not
  an oversight about `convert`.** Those goldens compare **stdout only**, and `convert`'s entire human
  report is on **stderr**: its stdout is one line (the written path, or the cell names under
  `--list-cells`). A stdout golden for it would pin almost nothing, which is why AUT-1 verified it
  the other way instead — 12 refusal paths and one real DXF→GDSII conversion compared against a
  `git worktree` of HEAD, stdout, stderr AND exit code. `em` is absent for the same reason, and has
  `EmCliVerbTests`' byte-for-byte `.sNp` comparison instead.
- What WAS missing: `Json_AlwaysParses_AndStatusAgreesWithTheExitCode` exercised `convert` only on
  its **refusal**. A verb whose document is tested only on the failing path is a verb whose
  successful document is untested. The theory now covers a real conversion, and `check`, `explain`
  and both halves of `read` alongside it — the five verbs that produce no `DataSet` and whose
  payload is therefore the one least like every other verb's.

---

## AUT-6 — `circuitrf reference`, and the component catalogue (2026-09-05)

`brief-automation-6-reference-and-components.md`. One read-only verb and one MCP resource surface,
answering the question the rest of the series left open: **what may a caller write, before it writes
it.** No new document, no new analysis, no change to any existing verb.

### Did the seventh tool earn its place?

**Yes, and the margin is not large.** Measured against what a resource-only surface would have cost:

- **Resources are genuinely cheaper.** `resources/list` publishes nine entries of a URI, a title, a
  one-line description and a size. The `reference` tool costs a name, a three-line description and a
  two-property schema, carried for the whole session whether or not anything calls it. If every
  client surfaced resources to the model, the tool would be pure overhead and should not exist.
- **They do not.** Resource support is optional in the protocol and a client that implements it may
  still not put resources in front of the model — and a capability the model cannot reach is not a
  capability. That is the whole of the argument, and it is enough.
- **The cost is bounded because the surface is not per-topic.** One tool with a topic argument, not
  nine. `tools/list` grew by ~330 bytes.

**The thing that made this cheap is that both channels call the same verb.** `resources/read` and
`tools/call` both translate to `reference <topic> --json` and hand back `CliEntry.Run`'s bytes, so
the second channel is an envelope rather than a second implementation, and
`EveryResource_ReturnsTheBytesTheCliWritesForItsTopic` compares them against the CLI byte for byte.
Had the resource returned Markdown and the tool returned JSON, this would have been two surfaces that
drift, and the honest answer would have been to ship one.

**If it turns out not to pay, removing it is one `ToolSpec` and two test lines.** Adding it later,
after clients have been written against a resource-only surface, would have been worse.

### The brief's own naming collision, resolved

R-aut6-1 spells the generated catalogue `circuitrf reference components`; R-aut6-2 and R-aut6-8 spell
the PROSE page `components` as well. They cannot both be, and the brief did not notice.

**The catalogue keeps `components`** — it is the machine-facing answer the whole brief exists for, and
`reference components MLIN` only reads correctly that way. **The page ships as `component-notes`.**
Nothing was dropped: R-aut6-8's own words are that the catalogue answers "what may I write" and the
page answers "what does it mean", and a client that wants both asks for both. Each says so in its own
listing line.

Of R-aut6-8's two offered routes — strip the placeholders and serve the page, or exclude it — the
**first** was taken. Read without its tables the page is still 71 kB of prose that nothing else in the
program carries: what a ferrite bead is *not*, why an SRLC is the shape a real capacitor takes above a
few hundred megahertz, which of two FET laws to reach for. The 70 `{{symbol:}}` placeholders were
figures a text client could not use anyway, and the 67 `{{table:}}` placeholders are exactly the
generated half the catalogue serves better. Removing a placeholder that is alone on its line takes the
line with it, so the result reads as prose rather than as a page with holes in it.

### Both §2.3 lists, re-measured on the day it was built

Unchanged from the brief's own measurement. Asserted by token in
`ReferenceCliVerbTests`, and derived from the registries in the test rather than typed, so a change
in either direction fails by name:

- **5 `EngineReference` targets no factory entry answers to**: `GND`, `MEAS`, `Pin`, `SpiceModel`,
  `VAR`. Four of those are not components at all — schematic elements the extractor consumes — which
  is itself the answer for them, and the catalogue says so.
- **7 factory types no `EngineReference` maps to**: `Chain`, `ExtDevice`, `I_nTone`, `SemiC`,
  `Short`, `Term`, `V_nTone`. Writable in a `.cnl`, drawn by nothing, and therefore carrying no
  declared parameters.

The catalogue is **73 entries over 68 factory tokens** — the 68 plus those 5.

### What the registries could not answer

Two gaps, and the second is the more valuable one.

**1. Nothing states which parameter sets a variadic component's port count.** R-aut6-9 requires the
catalogue to report "it depends, and here is what on", and no registry held that fact —
`LibraryCatalog` knows Snp/ZPort/Sdd are dynamic for the palette's sake, `IsRemovableParameter` knows
ZPort's `NumPorts` is structural, and neither is an answer to the question. Per R-aut-1 the fix went
into the CAPABILITY layer rather than into the adapter: `ComponentTypeRegistry.PortCountParameter`, a
pure lookup beside `OwnsUniquePortNum`. It is not a transcription because the gate does not trust it —
`AKindWhosePinsDependOnN_ReportsThatAndNamesTheParameter` MEASURES variadicity (`SymbolPortDefs.For(k,
2).Length != SymbolPortDefs.For(k, 3).Length`) and fails any kind that answers differently at 2 and 3
without naming its parameter. A future variadic component added without an arm fails there.

**2. `ComponentModel.PortCount` is not the number of nets an instance line writes, and nothing below
the firewall states that number for a type with no symbol.**

This is the finding worth keeping. The brief says to take the port count "from the model", and the
model cannot give it: `PortCount` is the model's own port count in the MNA sense. `IProbeModel`
reports **1** and takes two nets. `FetModelBase` reports **2** and takes three. A 2-port `SddModel`
reports **2** and takes four (`SDD:M1 Vin 0 Vout 0`). `ResistorModel` reports 2 and takes two, which
is why the discrepancy is easy to miss — the commonest components agree by coincidence.

So the catalogue reads `SymbolPortDefs`, which IS the contract `NetExtractor` emits nets by, and
constructs no model at all — not even a parameterless one, where R-aut6-9 would have allowed it. The
consequence is the gap: for the seven factory-only tokens above there is no symbol, so **nothing below
the UI firewall states how many nets they take**, and the catalogue says exactly that rather than
guessing. `ExtDevice` is the honest case (its node count is the provider's descriptor's, known only at
elaboration); `Short` and `Term` are the ones a declarative fact could close, and closing it is a
registry question rather than one this brief may answer by inventing a number.

### R-aut6-12's four unarmed kinds

`DefaultParameters` has an explicit arm for 71 of 75 `SymbolKind`s. The four without one, split as the
brief asks:

| Kind | Which it is |
|---|---|
| `Ground` | **genuinely parameterless** — a ground symbol has no values to carry |
| `IProbe` | **genuinely parameterless** — `IProbeModel` reads no parameters at all; it stamps a 0 V branch |
| `Generic` | **the question does not arise** — an internal fallback glyph, in `LibraryCatalog.InternalOnlyKinds`, not user-placeable |
| `Unknown` | **likewise** — the sentinel a newer file's unrecognised component loads as |

So there is no undescribed component among them, and **no arm was added**. Two other kinds return an
empty list from an arm that means it — `Var` and `Meas` author their own rows — and `Mutual` returns
none because it references instances by name rather than connecting to nets.

### The total embedded size

**145,410 bytes** of reference text, across 8 topics, in `CircuitRF.Design` — so in every binary on
every platform, the GUI included. `component-notes` is half of it at 73 kB. It is reported by
`EveryTopic_IsEmbedded_AndItsBytesAreTheAuthoredFile`'s test output rather than asserted, so the
number is always current in the TRX without a threshold anyone has to maintain.

### Two things found on the way that were not this brief's

- **`tools/DocGen` had not compiled since AUT-2.** `Placeholders.cs` uses `ComponentTypeRegistry` and
  `SymbolKind`, which moved to `CircuitRF.Design.Schematic`; `src/Ui` absorbed that with its own
  `GlobalUsings.cs`, but global usings are per-project and **DocGen is not in `circuitRF.slnx`**, so a
  plain `dotnet build` never noticed. One `using` line. The lesson is the one the missing-resource
  trap teaches in a different key: a project outside the solution is a project nothing tells you
  about.
- **`docs/user`'s generated HTML was already stale before this change** — `cli.html` by ~315 lines,
  from AUT-5's edit to `cli.md`. See "The docs check" below.

### The docs check

`tools/DocGen/check-docs-current.sh` **fails**, and it regenerates as its mechanism, so the run was
reverted afterwards and only the Markdown sources under `docs/user/src/` are changed here. What it
reported, classified:

| Page | Why |
|---|---|
| `reference/components.html` | **this change** — 67 new terminal tables, one per component section |
| `reference/cli.html` | partly this change (the new `reference` section), partly AUT-5's already-pending edit |
| `reference/{layout-editor,schematic-editor,veriloga,wbond}.html`, `assets/js/search-index.js` | already pending before this change |
| `assets/figures/workspace-{overview,regions}*.svg`, `workspace.html`, `quick-start/`, `new-user-guide/` | the known non-deterministic figure families — a live capture, not a content change |

Regenerating is one deliberate pass and belongs to whoever is ready to review 2,200 lines of it.
