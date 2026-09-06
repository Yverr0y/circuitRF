# circuitRF — the command-line interface (`src/Cli`)

**Status:** current · **Covers:** `src/Cli/Program.cs` · **Related:** `ui-architecture.md`,
`loadpull.md`, `loadpull_pursuit.md`, `harmonic-balance.md`, `mom-engine.md`

## 1. What it is, and the one constraint that shapes it

`circuitRF.Cli` is the **headless driver**: it reads a `.cnl`, elaborates it, runs one analysis, and
reports. It is simultaneously the project's own **test harness** — a `.cnl` that works headless is a
`.cnl` that works when opened, because the CLI evaluates the TestBench's `measure` lines through the
same `MeasurementEvaluator` the GUI uses rather than re-deriving them.

The constraint that decides everything below is the **UI firewall** (`ui-architecture.md`):

```
src/Cli  ──►  src/Core  ──►  (expressions, design model, elaboration)
   └────►  src/Engine ──►  src/RfCore
                    NO Avalonia anywhere on this path
```

`tests/Firewall.Tests` fails the build if that is violated. So the CLI can drive **anything whose
engine lives in `src/Engine` or `src/RfCore`**, and nothing whose driver lives in `src/Ui`.

`src/Design` joined that path in 2026-08: it holds the design-layer artifacts an EM problem is built
from — the layout model, the technology model, the cell-folder format, the `.cem` and the extractors —
and it is gated by the same firewall test. That is what the `em` verb (§8) runs on.

## 2. The verbs

| Verb | Input | Runs | Writes |
|---|---|---|---|
| `sparam` | `.cnl` | `SParameterEngine` | Touchstone `.sNp` (always; `-o` names it) |
| `dc` | `.cnl` | `NonlinearDcEngine` | node voltages + probe currents to stdout |
| `hb` | `.cnl` | `HbEngine` (single- or multi-tone) | stdout tables; `-o .mat/.npy/.txt` |
| `lp` | `.cnl` | `LoadpullEngine` + `LoadpullPostProcessor` | stdout grid table; `-o .mat/.npy/.txt/.spl/.lpcwave` |
| `lpp` | `.cnl` | `LoadpullPursuitEngine` | stdout optima + follow-on grid; `-o` as `hb`; `--out-grid` writes the `.gam` |
| `em` | `.cem` | `EmSetupResolver` + `EmRunService` (kernel chosen by `EmKernelRegistry`) | Touchstone `.sNp` + grouped `.npy` at the path Simulate writes; `-o` moves the Touchstone |
| `elab` | `.cnl` | elaboration only | the elaborated netlist, for development |

Six verbs run no analysis, so none of §3-§6 applies to them and §7's exit codes reduce to 0-or-1:

| Verb | Input | Does | Writes |
|---|---|---|---|
| `convert` | any interchange format | one import, one export | the target format; documented in the repo-root `CLAUDE.md` |
| `new workspace` | a directory | `WorkspaceCreate.Create` | a `.cws` and, unless `--tech none`, a copied `.ctech` |
| `new cell` | a workspace + a name | `CellCreate.Create` | a cell folder and one empty-but-valid file per `--views` |
| `import part` | a component file or folder | `ComponentRead` + `ComponentImport.Import` | a cell folder holding the land patterns and the symbol |
| `check` | a workspace, a cell folder, or one document | the validators that already exist | **nothing** — §10 |
| `explain` | the same, plus `--expr` / `--analysis` / `--ref` | reports what resolution DECIDED | **nothing** — §10 |
| `read` | a result file, or one of circuitRF's own documents | loads it back through the readers the GUI reads through | **nothing** — §11.4 |
| `serve` | `--root <dir>` | a protocol server on stdin/stdout — §11 | whatever the tool it was asked for writes |

**`new` is one verb with a noun, not three** (`brief-automation-3-authoring-verbs.md` R-aut3-13): the
surface has a standing cost, and adding `new schematic` later is a noun rather than a fourth
top-level verb. The three authoring verbs share one rule that decides every default they have —
**whatever the GUI's dialog pre-selects, the verb selects with no flag, and anything the dialog would
have ASKED is a refusal that names the flag answering it.** So `--tech` defaults to the New Workspace
dialog's own pre-selected technology and an unknown id lists the real ones rather than falling back;
`--views` defaults to `schematic`, which is what the GUI's New Cell creates; and a source folder
holding several parts is refused with `--cell` / `--variant` / `--list-parts` named, never
resolved by taking the first. They add no import or creation logic of their own: each calls the same
function the GUI's own command calls, which is what
`tests/Ui.Tests/AuthoringCliVerbTests` gates, byte for byte and by scanning the view model's source.

The paths created ARE the result and go to stdout, because a caller's next step is almost always to
read or rewrite one of them — the documents are the interface (`automation-architecture.md` §4), and
there are deliberately no per-primitive edit verbs.

Three flags are pulled out of the argument list before dispatch, so **every** verb takes them and no
verb's own argument loop has to learn about any of them:

| Flag | What it does |
|---|---|
| `--kits <dir>` | makes an externally-supplied device model resolve headlessly, the way opening a workspace does in the GUI. Repeatable. |
| `--json` | one JSON document on stdout and nothing else — §3.2 |
| `--only`, `--group` | narrow that document's `result` — §3.2 |

## 3. The anatomy of a run verb

`hb`, `lp` and `lpp` are the same five steps. A new run verb should be the same five steps.

1. **Read** — `CnlReader.ReadFile` → `(Library, TestBench)`.
2. **Override globals** — each `--set name=expr` REPLACES the variable in `tb.GlobalVariables`, so it
   joins the netlist's own scope and everything derived from it re-derives. An override pushed at the
   engine instead would move one number and leave every expression computed from it stale.
3. **Elaborate** — `new Elaborator(lib).Elaborate(tb)`.
4. **Select the chain** — `SelectTop`, shared by all three (§4).
5. **Dispatch, report, export** — run; print warnings again *after* the run (the engine adds its own
   while assembling and solving, long after elaboration finished); evaluate measurements; print;
   export.

### 3.1 Two channels, and they are not interchangeable

**stdout is the result. stderr is everything else** — progress, per-grid-point and per-query
engine chatter, `[circuitRF]` notes, elaboration and engine warnings, device-worker logs. The split
is what makes `circuitrf lp x.cnl > table.txt` produce a table and still show progress, and it is
why the engines' own `Console.Error` progress lines need no CLI plumbing at all.

**`serve` is exempt, and only `serve`** (R-aut5-2). Its stdout carries the protocol framing, so
nothing else may be written there — ever. The stderr half is unchanged: every engine progress line,
`[circuitRF]` note and worker log still goes there, which is where a client's own logging picks it
up. The guarantee is structural rather than a rule each printer has to remember — `Console.Out` is
replaced with a sink before a single capability runs, and each verb's document is written to a
string (§11.2) — and it is audited rather than assumed, by exercising every exposed capability with
stdout captured and asserting that every byte of it is a protocol frame.

### 3.2 `--json`: the third channel rule, not a per-verb feature

`--json` is available on **every** verb, spelled that way everywhere — not `--format json`, not a
per-verb variant. It changes one thing: **stdout carries a single JSON document and nothing else.**
stderr is untouched, so progress, `[circuitRF]` notes, warnings and refusal sentences all stream
exactly as they did, and a script watching stderr cannot tell whether the flag was passed.

That guarantee is structural rather than a rule each printer has to remember: the moment the flag is
parsed, `Console.Out` is replaced with a null writer and the real stdout is held until the document
is written to it (`src/Cli/JsonRun.cs`). A verb prints its table exactly as before, into a sink.

| | Without `--json` | With `--json` |
|---|---|---|
| stdout | the human table | one JSON document |
| stderr | unchanged | unchanged |
| exit code | §7's per-verb rules | **the same**, and echoed in the document |

**A failed run still emits a document.** The failure is the payload, so a caller never has to tell
"no output" apart from "output I could not parse". `status` is `ok` / `not-converged` / `failed` and
always agrees with `exitCode`, which still follows §7 — including the deliberate difference between
`hb`'s convergence test and `lp`'s.

The shape is one schema across every verb (`RfCore.Export.ResultDocument`):

```
{ "circuitrf": {"version","verb"}, "input": {"path","analysis"},
  "status", "exitCode",
  "outputs":     [ {"kind","path"}, … ],      // every file written — for em, BOTH the .sNp and the .npy
  "diagnostics": [ {"id","severity","message","arguments"}, … ],
  "result":      { "summary": …, "groups": { "<group>": { "<cube>": {"kind","axes","values"} } } } }
```

(`result` also carries `check`, `explain` and `document` for the three verbs that produce one of
those instead of cubes — §10.5 and §11.4.)

- **`input.analysis` is the chain that ACTUALLY ran**, after §4's promotion — not what was requested.
- **`result.groups` mirrors the `DataSet`**; a cube's `kind` decides whether `values` holds numbers
  or `[re, im]` pairs. Numbers are raw, invariant and unrounded: the dB/percent presentation and the
  column widths are terminal concerns and none of them is encoded in the document. NaN and infinity
  are written as JSON's named literals (`"NaN"`), because a loadpull grid genuinely contains NaN
  wherever a point never converged and substituting a zero would turn "no measurement" into one.
- **`result.summary`** is `lp`/`lpp`'s one-row-per-grid-point projection — the same one §6.3 prints,
  from the same code (`RfCore.Loadpull.LoadpullResultSummary`), so the table and the document cannot
  disagree. For those two verbs it is the DEFAULT and the cubes are omitted; `--all` adds them.
- **`--only <cube>,…` and `--group <name>,…`** narrow `result` and nothing else. An unknown name is
  skipped silently, matching `DataSetSubset.SelectGroups`.

**§7A applies inside the document.** Every diagnostic carries `message` — always
`Diagnostic.Render()`, always English, always culture-invariant — alongside its stable dotted `id`
and its typed `arguments`. **The id is the contract; the message is not.** A caller matching on the
sentence is doing the thing the id exists to make unnecessary, and templates are reworded freely.

## 4. Chain selection: dispatch at the SWEEP, never at the inner analysis

`SelectTop(tb, requested, isBase, kindLabel, directiveHint, out why)` picks what runs. The rule it
exists to enforce:

> A `parametric_sweep` wrapping the analysis must be dispatched **at the sweep**. Naming the inner
> analysis runs one point and silently loses the sweep axis.

That failure produces a converged, plausible, complete-looking result for a run the user thinks
swept, so `-a <inner-name>` is **promoted** to its outermost enabled wrapper (with a note on stderr)
rather than being honoured literally. This is not HB-specific — a frequency-swept loadpull is exactly
this shape — which is why one function serves every verb and takes the base-analysis test as an
argument.

Ambiguity is reported, never guessed at silently: more than one runnable chain prints all their names
and runs the first; zero prints whether the netlist declares none or declares one that is disabled.

## 5. Overrides land in the DIRECTIVE, not at the engine

`--maxharm`, `--tol`, `--max-iter`, `--pin`, `--compression`, `--grid`, `--out-grid` all work by
**replacing the analysis directive in the TestBench** (`ApplyHbOverrides`, `ApplyLoadpullOverrides`).
The directive records are `init`-only, so "replacing" means rebuilding the record with every field
copied — verbose, and correct for a reason that is not obvious:

`ParametricSweepEngine` **re-elaborates and re-resolves the inner directive at every sweep point.** An
override handed to a freshly constructed engine would be discarded after the first point of a swept
run, and there would be nothing to see: the sweep would simply run at the directive's own values.

The netlist is the single source both the direct path and the sweep engine read. Put an override
anywhere else and the two paths disagree.

Two path rules follow from where the reader resolves things:

- `--grid` is made **absolute against the working directory** at parse time, because the directive's
  own `Grid=` was already resolved against the `.cnl`'s directory by `CnlReader`. A relative override
  left alone would silently change which directory it is relative to.
- `--out-grid` likewise.

## 6. `lp` and `lpp`

### 6.1 One function, two verbs

Loadpull and pursuit differ only in which directive is dispatched and which overrides apply.
Everything around that — selection, `--set`, measurements, printing, export — is identical, so
`RunLoadpull(args, pursuit:)` is one function. Options that belong to only one of them are
**refused, not ignored**: `--grid` on `lpp` (a pursuit searches for its terminations, it does not read
a grid) and `--out-grid` on `lp` both stop the run with a sentence saying which verb owns them.

### 6.2 `lp` enriches; `lpp` does not

`lp` runs `LoadpullPostProcessor.Enrich` on its result, exactly as `SchematicRunService` does, so a
headless export carries the same derived display metrics (`Pout_dBm`, `Zin`, `IRL_dB`, `AMPM_deg`)
as a GUI run. Without it a `.npy` written here and one written by the GUI would not carry the same
cubes.

A pursuit's follow-on loadpull grid is embedded under the engine's **raw** cube names, matching what
`SchematicRunService` publishes. The console printer therefore reads **both** spellings —
`Pout_dBm`/`Pout`, `Gt_dB`/`Gt`, `Efficiency`/`DE` — and scales the raw fractions to percent. Reading
only one set prints a table of em-dashes for the other, which looks like a run that produced no
figures of merit rather than like a naming mismatch.

### 6.3 What gets printed, and why it is not the cubes

A loadpull's cubes are `[gridPoint × pinStep]`. A 61-point grid driven up in 1 dB steps is a 61 × 30
table **per figure of merit**, and eight of those scroll a terminal without answering the question
anyone runs a loadpull to ask. So the default is **one row per Γ grid point**: where it was, how it
stopped, and its FOMs at the **last converged, non-tickle drive step** — the compression point when
the point compressed, the highest drive it managed otherwise. Reading a fixed drive index instead
would mix compressed and uncompressed points in one column. `--all` still dumps every cube.

A pursuit prints its MXP and MXE optima first, including a **non-converged** one: the engine still
publishes the last termination it looked at, and printing nothing there reads as "the search found
nothing" when what happened is "nothing it tried reached compression".

Swept results are printed **per sweep point**. The grid axis is located by NAME (`gridPoint`), not by
position, because a sweep prepends one axis per nesting level: taking the last axis would read a
two-frequency run as one grid of twice the size with half its rows mislabelled.

### 6.4 `.spl` / `.lpcwave` export

`lp -o out.spl` writes through `RfCore.Loadpull.SplWriter` rather than `DataSetExporter`. These are
the loadpull interchange formats the Data Display reads back, so a headless run can produce a file
the GUI opens as a measured surface. The writers take the **group** holding the loadpull cubes; it is
searched for (`GammaLoad`) rather than assumed, because a swept run leaves the cubes in the sweep's
own group — an unfound group would otherwise surface as "no frequency blocks", which describes the
symptom and not the cause.

## 7. Exit codes

| Code | Meaning |
|---|---|
| 0 | ran, and produced something usable |
| 1 | could not run — bad arguments, missing file, no matching analysis, a refusal, an exception |
| 2 | ran, but did not converge |
| 130 | stopped — `em` only, and only when the run was cancelled at a work boundary (§8.4) |

`2` is deliberately **not** the same test for every verb. `hb` and `dc` fail on any non-converged
solve. A loadpull grid in which some points do not converge is a normal, useful result — the edge of
a Γ grid routinely will not — so `lp` returns `2` only when **every** grid point failed, and `lpp`
only when neither optimum converged and there is no follow-on grid. A rule that failed the whole run
on one bad point would make the exit code useless in a script.

## 7A. The CLI stays English, permanently

**Decided, not assumed.** If the GUI is ever localized, the CLI is not.

Every diagnostic the run services produce goes to two places: the Messages window and this program's
stderr. A localized error on stderr breaks every user's `grep`, every log scraper, and every CI job
that matches on a message — silently, and in a way that only shows up on machines in one country.
So the split is by SURFACE, not by user:

| | Follows the user's locale? |
|---|---|
| GUI display text — status lines, Messages entries, dialogs | yes, when localization lands |
| CLI stdout and stderr | **no, ever** |
| Every file format (`.cnl`, `.clay`, Touchstone, Gerber, DXF, `.kicad_pcb`, …) | no — see `FormatCultureInvarianceTests` |
| The expression language | no — see `expressions.md` §15A |

Mechanically this costs nothing, because of how coded diagnostics are shaped
(`brief-localization-groundwork.md` R-loc-5). A `CircuitRF.Diagnostics.Diagnostic` carries an id,
typed arguments **and an English default template**. The GUI renders it through the one render point
in `src/Ui` — the place a resource lookup would later be inserted. The CLI calls `Render()` and gets
the English template, always, with no lookup and no language setting consulted. Numbers inside a
diagnostic render invariantly for the same reason: `2.5` on stderr must not become `2,5` because of
where the machine is.

This is also why `EmRunResult` carries **both** `Error` (a plain string) and `Diagnostic`. The
redundancy is deliberate: the string is the contract §8 already promises — a refusal stays a refusal,
exit 1 with the run service's own sentence, `Cancelled` exits 130 — and the diagnostic is the
structure the Messages window needs to group, deduplicate and act on it. Neither replaces the other.

## 8. `em`

```
circuitrf em Amp.cem                     # → <workspace>/results/Amp.s2p (+ Amp_em.npy)
circuitrf em Amp.cem -o /tmp/amp.s2p     # explicit Touchstone destination
```

**The verb owns no EM logic.** It resolves two paths, calls `EmSetupResolver.Resolve` and
`EmRunService.Run`, and reports. Which kernel runs, how the geometry is meshed and what is refused all
live in `CircuitRF.Design` and `src/Engine/Mom`, and are the same code the Simulate button drives —
which is what makes "a headless run and a Simulate produce the same file" true by construction rather
than by care.

### 8.1 Both paths resolve by a WALK-UP, and neither is a flag

A `.cem` names a layout; the layout names (or inherits) a technology. Neither reference is stored
absolutely and neither needs an argument:

- **The layout.** `EmSetup.LayoutRef` is relative to the **workspace root** — the nearest ancestor
  `.cws` walking up from the `.cem` — and absolute when it names something outside it. With no
  workspace above it at all, the reference falls back to the `.cem`'s own directory, so a loose `.cem`
  beside its `.clay` works. That fallback is the GUI's own rule, not a headless special case.
- **The technology.** Resolved against **the layout's own parent workspace**, found by walking up from
  the `.clay` (`brief-foreign-documents.md` R-fgn-3) — never against "the current workspace", of which
  there is none here. A `.clay` with a null `TechRef` is the normal case and picks up the `.cws`'s
  `DefaultTechRef`.

The two walks start from different files and can land on different workspaces. That is deliberate: a
`.cem` in one workspace may point at a layout in another, and that layout's layers must be read by
*its* technology.

`--workspace <path.cws>` overrides the first walk, for a `.cem` being run from outside its own tree.
It is never required.

### 8.2 Where the results go, and why `-o` moves only one of them

Without `-o`, the run writes exactly where Simulate writes: `<workspace>/results/`, through
`EmRunService.ResolveSnpPath`. **That path is predictable by design** (R-em-19) so a schematic's SnP
reference stays valid across re-runs — a headless run that minted its own filename would orphan every
one of them, which is why the CLI does not get to choose a default here.

Two files come out, and they are not redundant:

| File | Holds |
|---|---|
| `<key>.sNp` | S only — the artifact a schematic REFERENCES by path |
| `<key>_em.npy` | the whole `DataSet`, including the diagnostics group (`tline` or `planar`) that makes a wrong answer diagnosable |

`-o` sets `EmSetup.SnpOutputPathOverride` — the same field the EM panel writes — so the Touchstone
moves and the `.npy` does not. There is no second naming rule to keep in step, and a `.sNp` extension
typed into `-o` is not doubled: the exporter appends the real one from the port count it finds.

With no workspace above the `.cem`, `results/` is created beside the `.cem` itself. The GUI's own
fallback there is the scratch recovery session, which does not exist headlessly; using the `.cem`'s
directory reuses the fallback its `LayoutRef` already has rather than inventing a third rule.

### 8.3 What goes where, and the three lists

§3.1's split, applied: the summary and the written file paths are **stdout**; progress, the resolved
workspace/layout/technology, and the run's own three lists are **stderr**.

`EmRunResult` separates `Notes` / `Warnings` / `Errors` by what the reader is expected to DO about
each, and the verb prints all three under those labels rather than flattening them:

| Prefix | Means |
|---|---|
| `note:` | the run explaining itself — which kernel ran and why, the mesh's own sentences, RLGC, ports |
| `warning:` | something to act on — a stale `.sNp` about to be replaced, a technology that resolved but failed validation |
| `error:` | something the user asked for and did not get — a results file that could not be written |

Flattening them into one list is the exact defect the three-list split was introduced to fix, and it
is just as wrong on a terminal as it was in the Messages region.

### 8.4 Exit codes: a refusal stays a refusal

`EmRunStatus` distinguishes `Refused` / `NoLayout` / `EngineError` / `Cancelled`, and each carries a
written explanation of what is wrong with *this* setup. The verb prints that explanation and exits
non-zero; it never collapses them into "EM failed", because the explanation is the only part a user
can act on.

| Status | Code |
|---|---|
| `Ok` | 0 |
| `Refused`, `NoLayout`, `EngineError` | 1 |
| `Cancelled` | 130 |

### 8.5 What the verb does NOT do

- **Create or edit a `.cem`.** It runs one. A setup with no ports, no technology or no signal
  conductor is REFUSED with the sentence the run service already writes.
- **Back-annotate.** Writing an SnP component into a schematic is an editor operation and stays in
  `src/Ui`.

### 8.6 The gate

`tests/Ui.Tests/Em/EmCliVerbTests.cs` builds a real workspace on disk, runs the real `Cli em` process,
and compares the `.sNp` **byte for byte** against what `EmRunService.Run` writes for the same setup —
and asserts the file lands at the same PATH. A tolerance-based comparison would pass just as happily
if the two paths had drifted onto different geometry, a different technology or a different filename,
which are the three failures the project split could plausibly have introduced.

One line is exempt and only one: `EmSnpProvenance` stamps the UTC time the file was written, so two
runs a second apart can never match byte for byte. Everything else does, **including all three
provenance hashes** — geometry, mesh and ports — which is what proves both paths resolved the same
layout, stackup and ports. The `.npy` matches with no exception.

**Any test that launches a verb as a process follows `Engine.Tests`' pattern** (`MatchStampTests`,
which learned it first): a `ReferenceOutputAssembly="false"` project reference on `src/Cli` plus a
`CliDir` assembly-metadata attribute, and the DLL exec'd directly. A nested `dotnet run` starts an
MSBuild inside a `dotnet test` that already holds the build locks and does not finish — silently, with
no CPU and no child process. Drain both of the child's pipes concurrently too: `em` says enough on
stderr to fill that pipe's buffer and deadlock a sequential reader.

## 9. Adding a verb

1. Add the case to the dispatch switch and a line to `PrintHelp`.
2. Follow §3's five steps; use `SelectTop` with your base-analysis test.
3. Put overrides in the directive (§5), not at the engine.
4. Results to stdout, everything else to stderr (§3.1).
5. Pick the exit-code rule that is honest for that analysis (§7) — do not copy `hb`'s by reflex.
6. **Give the structured document its three lines** (§3.2): set `JsonRun.InputPath` when the input is
   known, `JsonRun.Analysis` to the chain that actually ran, and `JsonRun.Data` to the same `DataSet`
   the export writes. Call `JsonRun.AddOutput` beside every "Wrote …", and refuse through
   `JsonRun.Fail(CliDiagnostics.…)` rather than `Console.Error.WriteLine` + `return 1` — a refusal
   that is only prose is a refusal a caller has to parse back apart.
7. Update this file and the verb list in the repo-root `CLAUDE.md`.

`em` follows 1, 4, 5 and 6 and is deliberately outside 2 and 3: it does not read a `.cnl`, so there is
no chain to select and no directive to override. Its analogue of §5's rule is §8.2's — the one
override it takes lands in the `EmSetup`, not at the run service, for the same reason.

`check`, `explain` and `read` follow 1, 4, 6 and 7, and their §5 analogue is §10's — they run
nothing and they write nothing.

`serve` follows 1 and 7 and is outside all the rest, because it is not a verb that does work: it is
the one adapter that dispatches to the others (§11). Its §4 analogue is the inversion of §3.1 —
stdout is the protocol and the result goes into a frame — and its §5 analogue is R-aut-1: it owns
no logic at all, so there is nothing for an override to land in.

`convert`, `new` and `import part` follow 1, 4, 6 and 7 and are outside 2, 3 and 5 for the same
reason: they run no analysis. Their §5 analogue is stronger and is the whole of
`brief-automation-3-authoring-verbs.md` R-aut3-1: **an authoring verb calls the capability the GUI's
own command calls, and adds nothing of its own.** A verb that re-implements what a view model does
will diverge from it silently, and the first symptom is a document created headlessly that the
application treats as subtly malformed — so step 2, for these, is "find the function the GUI calls,
and if it is trapped inside a view model, extract it and change the view model to call it too". That
extraction is not optional and it is not a follow-up.

## 10. `check` and `explain`

`brief-automation-4-check-and-explain.md`. These are the two verbs that close a headless client's
loop: it writes a document, asks whether the document is sound, and asks what circuitRF made of it —
without paying for a run.

```
circuitrf check   <path> [--recursive] [--severity warning|error]
circuitrf explain <path> [--expr "<expression>"] [--set var=expr]
                         [--analysis [<name>]] [--ref <relative-ref>]
```

### 10.1 Neither runs an analysis, and neither writes

**R-aut4-1.** `check` on a large design has to be cheap enough to call after every edit, so it stops
at elaboration — which is what answers "do the parameters, expressions and cycles resolve?" — and
nothing here solves a matrix. A check that needs a solve to answer belongs in the run verbs' own
warnings.

**R-aut4-6.** Not a repair, not a re-save, not a cache file. A caller must be able to run either verb
on a read-only tree and on a workspace another process has open. The technology cache is per
invocation and in memory; DRC waivers are read from the `.clay` and never written back;
a `.csch`'s `.cnl` round trip (§10.3) happens as a string.

### 10.2 `check` calls the validators that already exist

**R-aut4-2, and it is the whole design.** The repo is full of validators and they are scattered
rather than missing; what `src/Cli/Check.cs` adds is the WALK and the reporting, never a rule.

| Validator | Where | What it answers |
|---|---|---|
| `CellViewFileValidator.DescribeDefect` | `src/Design/Cells` | is the file the view its extension claims? |
| `CellFolder.ResolvePrimary` | `src/Design/Cells` | primacy — a named primary that is missing, or none chosen |
| `NameValidator` | `src/Design/Cells` | a cell name the GUI would reject |
| `TechValidation.Analyze` | `src/Design/Layout` | a `.ctech`'s problems, already typed as `TechProblem` |
| `TechnologyResolver.ResolveForDocument` | `src/Design/Layout` | the technology walk-up |
| `EmSetupResolver.Resolve` | `src/Design/Layout/Em` | a `.cem`'s layout and technology, and its refusals |
| `CellSymbolResolver` | `src/Design/Schematic` | every cell reference on a schematic |
| `NetExtractor` | `src/Design/Schematic` | naming conflicts — two labels on one physical net |
| `Elaborator.Elaborate` | `src/Core/Elaboration` | parameters, expressions, cycles, node numbering |
| `ChainSelector` | `src/Cli/ChainSelection.cs` | whether a declared analysis chain will dispatch |
| `DrcPredicateParser` | `src/Design/Layout/Drc` | a `.wasm` rule that will not parse |
| `DrcEngine` | `src/Design/Layout/Drc` | layout design rules |

**A rule that exists only in `check` is a rule the GUI does not enforce** — a design would pass here
and be refused when someone opened it. The converse matters just as much and cost a round to find:
see §10.3.

**Every finding is a `Diagnostic`** with a stable `check.` id and the producing validator's own typed
values (R-aut4-4) — `TechProblem`'s `Area`, a DRC violation's rule name, layer and measurement. The
id is the contract; the sentence is not.

**Exit code (R-aut4-5): 0 if nothing at or above `--severity` was found, 1 otherwise.** Default
severity is `error`. There is no `2` — nothing here converges. A check that found warnings and no
errors **exits 0 and still reports them**, because the alternative makes the exit code useless in CI.
Two states are deliberately warnings rather than errors: a cell sub-folder holding several views and
no named primary (`PrimaryState.NoPrimary`, which that enum's own remarks call "not an error"), and a
layout that resolves no technology (`layout-view.md` §2.4's normal, fully-supported state).

**One verb over every document type** (R-aut4-11, R-aut-9). The kind comes from the path — by
extension, and for a directory by what it contains — and an extension circuitRF does not own is
offered to **`convert`'s own classifier**, which reads content, before being called unknown. A GDSII
or Gerber file is reported as interchange rather than as something circuitRF cannot read; it is not
VALIDATED, because there is nothing to validate it against.

### 10.3 A `.csch` goes through the `.cnl` on its way to the elaborator

The GUI's Simulate is `NetExtractor.Extract → CnlWriter.Write → CnlReader.Read → Elaborator`
(`WorkspaceViewModel.WriteNetlist`, then `SchematicRunService.Prepare`), and the round trip is
load-bearing: a schematic parameter is an EXPRESSION, so `BiasTee=on` read straight out of extraction
fails elaboration with "Unresolved name 'on'", while the same value written to a `.cnl` and read back
is quoted by `CnlReader` and elaborates.

`check` and `explain` therefore both read a schematic through `src/Cli/CircuitSource.cs`, which
performs that round trip in memory. Skipping it made `check` report errors the application does not
have — the mirror image of R-aut4-2's rule, and just as bad.

### 10.4 `explain` reports resolution, and shows the walk

**R-aut4-7.** The value is as much in the path taken as in the answer, so every resolution comes back
as a step: what was being resolved, from where, to what, and by which rule.

- **A `.cem` or `.clay`** — the workspace found by walking up, the layout it resolved to, the
  technology and *which* workspace resolved it. The two walks start from different files and can land
  on different workspaces; that is deliberate (§8.1) and is exactly the thing a caller cannot
  otherwise see.
- **`--analysis`** — every declared chain, whether it is runnable, which one would dispatch and for
  which verb, and whether a named inner analysis would be **promoted** to its wrapper (§4). Chain
  selection goes through `ChainSelector`, the same function the run verbs select with, so the report
  and the run cannot part company. A named analysis that comes back from selection but is not of that
  verb's kind is **not** reported as dispatched: `SelectTop` hands back `owner ?? named`, so
  `lp -a HB1` returns HB1, and calling that "lp dispatches HB1" would describe a run that cannot happen.
- **`--expr`** — evaluated in the design's own resolved scope, through the one expression engine
  (`Elaborator.EvaluateInGlobalScope`), never by substitution. The kind is reported, never coerced.
  `--set` applies first, exactly as it does for a run verb (§5).
- **`--ref`** — what a relative cell reference resolves to from that document's directory, its
  three-state result (`resolved` / `not-found` / `primary-missing`), whether it leaves the workspace,
  and whether it only resolved through a recorded move.

The three questions are **refused together rather than ordered** — each asks something different, and
a precedence nobody stated would be an invention.

**R-aut4-8: `explain` never guesses and never falls back silently.** Where resolution fails, that is
the answer — a diagnostic naming what was looked for and where it was looked, because a caller uses
this verb precisely when something did not resolve.

**R-aut4-9: sweep units are reported with their scale.** `--analysis` prints a sweep's resolved
start, stop and step in **base SI**, with the unit it was stated in AND the scale that got it there.
Reading a mark without its scale has already produced a run at 2 Hz that looked entirely normal.

### 10.5 `--json`

Per §3.2, with `diagnostics` carrying the findings and `result` carrying the report:

```
"result": { "check":   { "root","severity","documentsChecked","errors","warnings","notes",
                         "documents":[{"path","kind","errors","warnings"}] } }

"result": { "explain": { "path","kind",
                         "walks":[{"step","from","resolved","how"}],
                         "analyses":[{"name","kind","enabled","runnable","isRoot","chain",
                                      "dispatched","promotedFrom","sweep"}],
                         "expression":{"expression","kind","text","real","complex","boolean"},
                         "reference":{"ref","from","resolvedPath","state","outsideWorkspace","redirect"} } }
```

`read` uses the same two halves the run verbs do — `result.groups` for a result file it loaded back —
plus one field of its own for a document returned verbatim:

```
"result": { "document": { "path","kind","text" } }
```

`outputs` is empty for both — neither verb writes a file, and a caller looking for one must not find
one invented.

---

## 11. `serve` — the protocol adapter

`brief-automation-5-protocol-adapter.md`. A **stdio protocol server** that advertises circuitRF's
capabilities to an external client and invokes them on request. The concrete target is the Model
Context Protocol — a JSON-RPC convention in which a server advertises tools over stdin/stdout and a
client discovers and calls them — but the protocol is the first adapter, not the architecture
(`automation-architecture.md` R-aut-13).

```
circuitrf serve --root <dir> [--kits <dir>]
```

**This is the disposable layer, and it is written to be deleted.** Everything durable was built in
AUT-1 through AUT-4; `src/Cli/Serve/` is five files that translate, dispatch and report, and
removing them takes nothing with it.

### 11.1 It calls the verb — it does not re-implement it

**R-aut-1, and it is the whole design.** A tool call becomes an argument vector and is handed to
`CliEntry.Run`, which is the same function `Program.cs` hands the real command line to. So R-aut-13
— nothing reachable here that is not reachable from the command line, and vice versa — is a
property of the code rather than a rule to remember, and the parity gate compares two documents that
came out of one function.

That is why `Program.cs` is now three lines and `CliEntry.cs` holds the dispatch: a local function of
a top-level program is private to `<Main>$` and callable by nobody. Nothing about the verbs changed.

**The adapter refuses only what it alone can see** — a tool that does not exist, an argument that
belongs to another mode of the same tool, an argument of the wrong JSON type, and a path outside the
root. Everything else is the verb's own refusal, arriving unchanged: `--grid` handed to a pursuit, an
unstated Excellon coordinate format, a source folder holding several parts.

### 11.2 stdout is the protocol, and nothing else may reach it

§3.1's exemption. The real stdout is taken at startup and held for the framing alone; `Console.Out`
is replaced with a sink before any capability runs, and each verb's document is written to a string
through `JsonRun.Sink`. `--json` on `serve` itself is **refused** rather than silently one-or-the-
other: it would have captured the stream the framing needs, and every tool call already returns a
document.

Nothing this program launches can leak there either — every child process it starts (the device
worker, the PCell host, the interpreter probe, the updater) redirects its own stdout to a pipe, which
was checked rather than assumed.

### 11.3 The tool surface

**Six tools, and the count is the point** (R-aut-9). A client that discovers tools up front carries
every description for the whole session whether or not it calls one, so the surface is a standing
cost paid on every interaction.

| Tool | Becomes |
|---|---|
| `run` | `sparam` / `dc` / `hb` / `lp` / `lpp` / `em`, selected by an argument — one tool, not six |
| `check` | `check` |
| `explain` | `explain` |
| `create` | `new workspace` / `new cell` |
| `import` | `import part` / `convert` |
| `read` | `read` |

`ToolCatalog` is one table, and **the JSON schema is generated from the same rows that build the
command line**. A description that says an argument exists and a translation that drops it cannot
happen, because there is one list; adding a flag is one row. Every argument is named after the CLI
flag it becomes.

`--only` and `--group` are reachable as tool arguments (R-aut5-6) — reading is the expensive
direction, and a client that receives eight full loadpull cubes when it wanted one number is the
failure mode this whole series is about.

**`--kits` is the operator's, not the client's.** It is one of the flags taken before dispatch, so
`circuitrf serve --root <dir> --kits <dir>` registers the resolver for the whole server and every
`run` resolves an externally-supplied device model with it. It is deliberately not a tool argument: a
kit folder is installed software rather than design data, it lives outside the root by nature, and
letting a client name one would be the server pointing at an arbitrary directory on its say-so.

### 11.4 `read` — the verb the tool table needed

`read` is the inverse of a run verb: a `.npy` through `DataSetImporter`, a Touchstone through
`TouchstoneIO` plus `DataSetBuilder.FromSnp` — **the same pair the GUI's own source library reads a
file with** — and one of circuitRF's own documents returned as its own bytes, because the formats
ARE the interface (`automation-architecture.md` §4) and a round trip through a reader and a writer
would hand back something that differs from disk wherever the reader is lossy.

It was added because R-aut-13 required it: the tool table has a `read` in it, and a tool with no verb
behind it is exactly the privileged adapter that rule forbids. It writes nothing, for `check`'s
reason (§10.1). A directory is refused rather than walked — what a workspace holds is what `check`
and `explain` answer — and an interchange file is refused NAMING `convert`, since half those formats
are binary and handing back a GDSII stream as a JSON string would be an encoding decision this verb
has no business making.

### 11.5 What it refuses

**R-aut5-8. The server runs with the invoking user's authority, and it constrains itself in one
place.**

- **A root is required at startup**, and every path a client names resolves under it — a relative one
  against the root, since the client cannot see the server's working directory. A path that escapes
  is a **refusal naming the root, never a silent clamp**: clamping runs a different operation than
  the one asked for and says nothing about it. Symlinks are resolved on both sides and at **every
  level of the path**, not just its last component — `ResolveLinkTarget` answers about the item it is
  called on, so asking it about `<root>/link/file` reports "not a link" and the escape goes straight
  through.
- **No shell and no arbitrary process launch.** The device-worker and PCell paths still start their
  own; nothing new becomes launchable because a client asked.
- **Destructive operations are refusals, not confirmations** — and by omission rather than by a
  filter: there is no tool that deletes, and no capability below writes outside the paths it chooses
  itself. There is no user at the other end to confirm with. A client that wants a file gone deletes
  it itself.

### 11.6 Progress, cancellation, and one call at a time

Capability calls are **serialized** — the verbs use process-wide state (`JsonRun`, `Console.Out`), so
two cannot be in flight together — but **the reader loop never blocks on one**. That split is the
whole reason it exists: `notifications/cancelled` and `ping` have to be answerable while a run is
going, and a run that cannot be cancelled is one a client times out on and retries, doubling the cost
of the run it gave up on.

Cancellation and progress both go through **the same `RunControl` the `em` verb already uses**
(`RunHost`), so cancellation lands at a work boundary and progress counts leaf units exactly as that
type's contract describes. A client that sends a progress token is sent `notifications/progress`; one
that does not is not sent notifications it never asked for. A cancelled run answers with **130**, the
code §7 already gives a run stopped at a work boundary, and it writes nothing — a cancelled run
abandons its result rather than publishing a partial one.

With no host installed, `RunHost.Control` is null and every engine takes the same optional argument
it always took, so a command line behaves exactly as it did.

### 11.7 The gate

`tests/Ui.Tests/ServeProtocolAdapterTests.cs`. For every tool, the document that comes back through
the server is compared **byte for byte** against the one `circuitrf <verb> --json` writes. Two things
are normalized and nothing else: the adapter RESOLVES paths, so the CLI side is given the resolved
path; and a verb that CREATES something cannot create it twice, so those two calls are given
different destinations and the destination is substituted out. The stdout audit, the three
root-escapes, the lifecycle and the cancellation are in the same file.
