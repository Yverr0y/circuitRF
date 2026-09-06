---
title: The Command Line
slug: reference/cli.html
doc-kind: Reference Guide
breadcrumb: Docs > Reference > The command line
lede: circuitRF runs without the GUI — not just its engines, but authoring, validation and resolution too. One executable, fourteen verbs — S-parameters, DC, harmonic balance, loadpull, loadpull pursuit, electromagnetic extraction, layout interchange, creating a workspace or a cell, importing a part, checking a design, explaining what it resolved to, reading a result back, an elaborated-netlist dump, and a protocol server. Every one of them answers --json. This chapter is the operational reference for all of them, including a worked EM run from an empty folder.
---

<nav class="toc">
<h2>On this page</h2>
<ol>
<li><a href="#invoking">Invoking it</a></li>
<li><a href="#verbs">The verbs at a glance</a></li>
<li><a href="#channels">Results on stdout, everything else on stderr</a></li>
<li><a href="#common">Options every verb takes</a></li>
<li><a href="#sparam"><code>sparam</code> — S-parameters</a></li>
<li><a href="#dc"><code>dc</code> — the operating point</a></li>
<li><a href="#hb"><code>hb</code> — harmonic balance</a></li>
<li><a href="#lp"><code>lp</code> — loadpull</a></li>
<li><a href="#lpp"><code>lpp</code> — loadpull pursuit</a></li>
<li><a href="#em"><code>em</code> — electromagnetic extraction</a></li>
<li><a href="#convert"><code>convert</code> — layout interchange</a></li>
<li><a href="#new"><code>new</code> — a workspace or a cell</a></li>
<li><a href="#import"><code>import part</code> — a footprint and its symbol</a></li>
<li><a href="#check"><code>check</code> — is it sound?</a></li>
<li><a href="#explain"><code>explain</code> — what did it resolve to?</a></li>
<li><a href="#read"><code>read</code> — a result or a document, back</a></li>
<li><a href="#elab"><code>elab</code> — the elaborated netlist</a></li>
<li><a href="#json"><code>--json</code> — one machine-readable document</a></li>
<li><a href="#serve"><code>serve</code> — a protocol server</a></li>
<li><a href="#exit">Exit codes</a></li>
<li><a href="#scripting">Scripting patterns</a></li>
</ol>
</nav>

## Invoking it {#invoking}

The command-line driver is the same program as the GUI's Run button with the window taken off. It
reads the same files, elaborates them with the same elaborator, runs the same engines, and evaluates
the test bench's `measure` lines with the same evaluator.

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf &lt;verb&gt; &lt;file&gt; [options]</code></pre>

From a source checkout there is no `circuitrf` on your path yet, so put `dotnet run --project src/Cli --`
wherever `circuitrf` appears:

<pre><code class="cmd"><span class="prompt">$ </span>dotnet run --project src/Cli -- sparam mycircuit.cnl --freq 1GHz:3GHz:50MHz</code></pre>

Run it with no arguments for the built-in help.

<div class="callout note">
<span class="label">A file that works headless works when opened</span>
<p>This is the point of the command line being the <em>same</em> code rather than a second
implementation. A <code>.cnl</code> that runs here runs when you open it in the workspace, and an EM
setup run with <code>em</code> writes the byte-identical Touchstone the <b>Simulate</b> button writes.
There is one elaborator, one set of engines, one measurement evaluator and one results-path
convention behind both.</p>
</div>

## The verbs at a glance {#verbs}

| Verb | Takes | Runs | Writes |
|---|---|---|---|
| `sparam` | `.cnl` | The linear S-parameter engine over a frequency sweep | A Touchstone `.sNp`, always |
| `dc` | `.cnl` | The nonlinear DC engine | Node voltages and probe currents, to stdout |
| `hb` | `.cnl` | Harmonic balance, single- or multi-tone | Spectra tables to stdout; `-o .mat/.npy/.txt` |
| `lp` | `.cnl` | Loadpull over the directive's Γ grid | A per-Γ-point table; `-o .mat/.npy/.txt/.spl/.lpcwave` |
| `lpp` | `.cnl` | Loadpull **pursuit** — searches for the optima | Optima + the follow-on grid; `-o` as `hb`; `--out-grid` writes a `.gam` |
| `em` | `.cem` | The EM kernel the setup resolves to | A Touchstone `.sNp` **and** a grouped `.npy`, where **Simulate** writes them |
| `convert` | any layout format | The same importer and exporter **File ▸ Import/Export** runs | The layout in the format you asked for |
| `new workspace` | a directory | The same code **File ▸ New Workspace** runs | A `.cws` and, unless you say otherwise, a copied technology |
| `new cell` | a workspace + a name | The same code **New Cell** runs | A cell folder and one empty-but-valid file per view |
| `import part` | a component file or folder | The same code **Import Component** runs | A cell folder holding the land patterns and the symbol |
| `check` | a workspace, a cell folder, or one document | Every validator the application already uses | **Nothing** — findings to stdout |
| `explain` | the same | Resolution only — no analysis | **Nothing** — the walk and the answer, to stdout |
| `read` | a result file, or one of circuitRF's own documents | The same loaders the Data Display reads a file with | **Nothing** — what the file holds, to stdout |
| `elab` | `.cnl` | Elaboration only, no analysis | The elaborated netlist, to stdout |
| `serve` | `--root <dir>` | A protocol server for an external client | Whatever the tool it is asked for writes |

`hb`, `lp` and `lpp` all run **the whole parametric sweep** when one wraps the analysis — see
[naming the wrapper](#wrapper).

## Results on stdout, everything else on stderr {#channels}

**stdout is the result. stderr is everything else** — progress, per-grid-point engine chatter,
`[circuitRF]` notes, elaboration and engine warnings, device-worker logs.

That split is what makes the output pipeable while the terminal still shows a long run moving:

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf lp hero3.cnl &gt; table.txt</code></pre>

`table.txt` gets the loadpull table and nothing else; the per-drive-step `[LP]` lines and the
convergence notes still scroll past on screen. Redirect `2&gt;/dev/null` to silence them, or
`2&gt;run.log` to keep them.

## Options every verb takes {#common}

| Option | What it does |
|---|---|
| `--kits <dir>` | A folder of installed kits, so an externally-supplied device model (`ExtDevice Provider=…`) resolves headlessly the way opening a workspace resolves it in the GUI. Repeatable. |
| `--json` | Put **one JSON document** on stdout and nothing else — [see below](#json). stderr is untouched. |
| `--only a,b` | Narrow that document's result to these cubes. |
| `--group g,h` | Narrow that document's result to these groups. |

Frequencies are written as `1GHz`, `100MHz`, or bare Hz (`1e9`) anywhere a frequency is accepted.

<div class="callout">
<span class="label">An option a verb does not take is refused, never ignored</span>
<p>Every verb stops with <code>unknown option '…'</code> and exit&nbsp;1 rather than dropping a flag it
does not recognise. This matters more than it sounds: most verbs find their input file as
<i>the first argument that is not an option</i>, so a silently dropped flag's <b>value</b> would be
read as the file name — and a flag that carries an override, like
<code class="nowrap">--set</code>, would simply not be applied, giving you a run that answers a
different question with nothing to say so.</p>
</div>

---

## `sparam` — S-parameters {#sparam}

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf sparam &lt;file.cnl&gt; [--freq start:stop:step] [-o out.sNp]</code></pre>

```text
$ circuitrf sparam hero1.cnl --freq 1GHz:3GHz:1GHz -o hero1.s2p
S-parameter analysis: 3 points, 1–3 GHz
Wrote hero1.s2p
```

| Option | What it does |
|---|---|
| `--freq start:stop:step` | Override the sweep. **Omit it and the netlist's own `sparam` analysis is used**, segments and all — which is almost always what you want, because it is the sweep the design was set up with. |
| `-o`, `--output <path>` | Where the Touchstone goes. Omitted, it is the input file with its extension changed to `.sNp` for the port count found. |

`sparam` **always** writes a Touchstone; there is no stdout table. The port count in the extension
comes from the network, so a circuit that grew a port writes `.s3p` without you editing the command.

## `dc` — the operating point {#dc}

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf dc &lt;file.cnl&gt;</code></pre>

```text
$ circuitrf dc hero2.cnl
DC: converged in 3 iteration(s), residual 7.27E-16
Node voltages:
  0                                         0
  n_src                                     0
  n_gate                                -3.05
  n_drain                                  48
```

No options beyond the common ones. It prints the converged node voltages and any probe currents, and
[exits 2](#exit) if the solve did not converge — the operating point is the one thing every nonlinear
analysis is built on, so a non-converged DC is a failed run, not a partial one.

## `hb` — harmonic balance {#hb}

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf hb &lt;file.cnl&gt; [-a name] [--set var=expr] [-o out.npy]</code></pre>

The same verb runs **single- and multi-tone** — which it is comes from the netlist's directive, not
from a flag.

```text
$ circuitrf hb hero2.cnl --rows 6
HB 'HB1': f0=2 GHz, MaxHarm=4, tol=1E-06
Analysis: HB1   (hero2.cnl)
  Converged: yes (1 solve(s))
  Residual:  1.24E-09 (worst)
  Tones:     2 GHz
  V  [node:7 x harmonic:5]  (mag ∠deg)
                        0                     1                     2
    n_gate              3.05 ∠  180.0         0.029814 ∠    0.1     0
    n_drain             48 ∠    0.0           0.15004 ∠ -172.4      1.6824E-05 ∠ -179.8
    … 1 more row(s) — use --all or --rows N
```

| Option | What it does |
|---|---|
| `-a`, `--analysis <name>` | Which analysis to run. Optional when the file declares one HB chain. |
| `--set <var=expr>` | Override a global variable **before elaboration**. Repeatable. |
| `--maxharm K` | Override `MaxHarm`. |
| `--maxmix M` | Override `MaxMixOrder` (multi-tone only). |
| `--tol t`, `--max-iter N` | Override the convergence tolerance and the iteration cap. |
| `--rows N`, `--all` | How much of each printed table to show. Default is a truncated head. |
| `--diag` | Engine convergence diagnostics, on stderr. |
| `-o`, `--export <path>` | Export the results. **The extension picks the format**: `.mat`, `.npy` or `.txt`. |

### `--set` overrides the VARIABLE, not the number {#set}

`--set Pavl_dbm=0` replaces the global variable in the test bench's own scope, then elaborates. So
every expression derived from it re-derives — a bias that was written `Vg = Vth + 0.2` follows a
changed `Vth`, and a sweep computed from the variable sweeps the new values.

An override pushed at the engine instead would move one number and leave everything computed from it
stale, which is why there is no such option.

### Name the wrapper, or name nothing {#wrapper}

When a [parametric sweep](simulations.html#parametric-sweep) wraps an analysis, the sweep is what
runs. Naming the inner analysis with `-a` is **promoted** to its outermost enabled wrapper, and the
promotion is announced:

```text
[circuitRF] 'HB1' is the inner analysis of 'SW1' — running 'SW1' so the sweep axis is not lost.
```

<div class="callout note">
<span class="label">Why it is promoted rather than obeyed</span>
<p>Running the inner analysis alone produces a converged, plausible, complete-looking result at one
operating point — <em>with the sweep axis silently missing</em>. Nothing about it looks wrong. A
frequency-swept loadpull has exactly this shape, which is why the rule is the same for every verb
rather than something harmonic balance does on its own.</p>
</div>

If more than one runnable chain exists, all their names are printed and the first runs; if none does,
the message says whether the netlist declares no such analysis or declares one that is disabled.

### Measurements {#measurements}

The `measure` lines on the test bench are evaluated exactly as the GUI evaluates them, and the results
join the exported `DataSet` as named cubes. A measurement that fails to evaluate is **reported on
stderr and the run continues** — one bad expression does not throw away a run that took minutes:

```text
[circuitRF] measurement: Measurement 'Gain_dB': failed to evaluate 'Pout_dBm - Pavl_dbm':
                         Unresolved name 'Pout_dBm' in scope 'measurements'
```

## `lp` — loadpull {#lp}

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf lp &lt;file.cnl&gt; [--grid grid.gam] [--pin start:step:max] [-o out.spl]</code></pre>

`lp` sweeps the load (or source) termination over the directive's Γ grid, runs a harmonic-balance
drive ladder at each point, and reports the figures of merit.

```text
$ circuitrf lp hero3.cnl --rows 8
Analysis: LP1   (hero3.cnl)
  Grid: 20 point(s) — 0 reached compression, 20 stopped at max drive
  Nothing reached compression — raise --pin's max (or the directive's PinMax).

      #  GammaLoad           ZLoad (ohm)           stop              Pavl     Pout      Gt     DE%    PAE%
      0  0.0000 ∠    0.0     50.00+j0.00           max drive        10.00    20.54   10.54    3.35    3.09
      1  0.2000 ∠    0.0     75.00+j0.00           max drive        10.00    22.31   12.31    5.03    4.76
      2  0.2000 ∠   90.0     46.15+j19.23          max drive        10.00    20.19   10.19    3.09    2.83
    … 12 more point(s) — use --all or --rows N
```

| Option | What it does |
|---|---|
| `-a`, `--analysis <name>` | Which loadpull analysis to run. |
| `--set <var=expr>` | Override a global variable before elaboration. Repeatable. |
| `--grid <file.gam>` | Override the Γ grid the directive reads. **Resolved against your working directory**, not the netlist's. |
| `--pin start:step:max` | Override the drive ladder, in dBm. |
| `--compression dB` | Override the compression target. |
| `--maxharm K`, `--tol t`, `--max-iter N` | Override the inner HB settings. |
| `--rows N`, `--all` | `--all` dumps every cube instead of the summary table. |
| `--diag` | Engine diagnostics, on stderr. |
| `-o`, `--export <path>` | `.mat`, `.npy`, `.txt` — **or `.spl` / `.lpcwave`**, the loadpull interchange formats. |

### One row per Γ point, at the point that answers the question {#lp-rows}

A loadpull's raw cubes are `[gridPoint × driveStep]` — a 61-point grid driven up in 1 dB steps is a
61 × 30 table *per figure of merit*, and eight of those scroll a terminal without answering anything.

So the default table is **one row per Γ grid point**: where it was, how it stopped, and its FOMs at
the **last converged, non-tickle drive step** — the compression point where the point compressed, the
highest drive it managed otherwise. Reading a fixed drive index instead would mix compressed and
uncompressed points in one column. `--all` still dumps everything.

A swept run prints one table per sweep point.

### `.spl` and `.lpcwave` {#lp-export}

`-o out.spl` writes the loadpull interchange format the [Data Display](data-display.html) reads back
as a measured surface, so a headless run can produce a file the GUI opens. `lp` also runs the same
post-processor a GUI run does, so the exported cubes carry the derived display metrics (`Pout_dBm`,
`Zin`, `IRL_dB`, `AMPM_deg`) — a `.npy` written here and one written by the GUI carry the same cubes.

## `lpp` — loadpull pursuit {#lpp}

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf lpp &lt;file.cnl&gt; [--out-grid found.gam] [-o out.npy]</code></pre>

A pursuit **searches** for the max-power (MXP) and max-efficiency (MXE) terminations rather than
reading a grid, then runs a follow-on loadpull over the terminations it recommends.

```text
$ circuitrf lpp hero3B_at_compression.cnl
Analysis: LP1   (hero3B_at_compression.cnl)
  Pursuit optima:
  MXP (max power)            converged   Pout=40.625 dBm   Zload=80.48+j0.00   Zsource=50.00+j0.00
  MXE (max efficiency)       converged   Eff=69.617 %   Zload=140.31-j4.95   Zsource=50.00+j0.00
  21 termination(s) queried, 45 recommended termination(s)

  Grid: 45 point(s) — 45 reached compression

      #  GammaLoad           ZLoad (ohm)           stop              Pavl     Pout      Gt     DE%    PAE%
      0  0.2690 ∠    7.0     86.15+j6.11           compressed       26.00    40.56   14.56   67.08   64.74
      1  0.2030 ∠    9.6     74.80+j5.30           compressed       27.00    40.68   13.68   63.55   60.82
```

`lpp` takes every `lp` option **except `--grid`**, and adds `--out-grid`:

| Option | What it does |
|---|---|
| `--out-grid <file.gam>` | Where the terminations the pursuit found are written, as a `.gam` you can feed back to `lp`. Resolved against your working directory. |

<div class="callout warn">
<span class="label">The two grid options are refused, not ignored</span>
<p><code>--grid</code> on <code>lpp</code> and <code>--out-grid</code> on <code>lp</code> each stop the
run with a sentence naming the verb that owns them. A grid option silently doing nothing would be a
run that answered a different question and said nothing about it.</p>
</div>

A **non-converged** optimum is still printed, with its status. The engine publishes the last
termination it looked at, and printing nothing there reads as "the search found nothing" when what
actually happened is "nothing it tried reached compression".

---

## `em` — electromagnetic extraction {#em}

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf em &lt;setup.cem&gt; [-o out.sNp] [--workspace file.cws]</code></pre>

`em` is the only verb that does not take a `.cnl`. It takes a **`.cem` EM setup** — the document the
[EM Setup panel](em-setup.html) edits — and runs it: extracts the geometry from the layout the setup
names, resolves the stackup, meshes, solves the frequency plan, de-embeds, and writes the results.

**It needs no other arguments.** Everything else it needs is already recorded in the files.

### What an EM run takes {#em-inputs}

Four files, and three of them are things you already have if you have drawn a layout:

| File | What it supplies | Where it comes from |
|---|---|---|
| **`.cem`** | The setup: which layout, which analysis, the frequency plan, port impedances and types, mesh settings, solver switches | **File ▸ New ▸ EM Setup…**, or the layout editor's **EM** button |
| **`.clay`** | The artwork — the metal, and the port labels for a full-wave run | The [layout editor](layout-editor.html) |
| **`.ctech`** | The [stackup](stackup.html): layer thicknesses, ε_r, tanδ, conductivity, which conductor is ground, and which drawing layers map onto what | The technology editor, or one of the shipped starter technologies |
| **`.cws`** | The workspace marker, carrying `DefaultTechRef` — the technology a layout uses when it does not name one itself | Created with the workspace |

<div class="callout note">
<span class="label">Author the setup in the GUI; run it from the command line</span>
<p>The <code>em</code> verb <b>runs</b> a setup — it does not create or edit one, and it will not
repair one. A setup with no ports, no technology or no signal conductor is <a href="#em-refusals">refused
with the sentence explaining what is missing</a>. Build the <code>.cem</code> once in the
<a href="em-setup.html">EM Setup panel</a>, where every control tells you as you type whether the run
is blocked and why, then commit it beside the layout and run it headlessly from then on.</p>
</div>

### Both file references resolve by walking UP, and neither is a flag {#em-resolution}

A `.cem` names a layout; the layout names — or inherits — a technology. Neither reference is stored
absolutely, and neither needs an argument:

- **The layout.** The setup's layout reference is relative to the **workspace root**: the nearest
  ancestor `.cws` found by walking up from the `.cem`. With no workspace above it at all, the
  reference falls back to the `.cem`'s own directory, so a loose `.cem` sitting beside its `.clay`
  simply works.
- **The technology.** Resolved against **the layout's own parent workspace**, found by walking up from
  the `.clay` — never against "the workspace you are in", of which there is none headlessly. A `.clay`
  that names no technology picks up its workspace's `DefaultTechRef`.

**The two walks start from different files, and that is deliberate.** A `.cem` in one workspace may
point at a layout in another, and that layout's layers have to be read by *its* technology, not by
whichever workspace the setup happened to live in.

`--workspace <file.cws>` overrides the first walk, for a `.cem` being run from outside its own tree.
It is never required.

The three resolutions are echoed on stderr before anything expensive starts, so you can see what the
run is actually about to read:

```text
[circuitRF] workspace: /work/amp/.cws
[circuitRF] layout: /work/amp/Line/layout/Line.clay
[circuitRF] technology: /work/amp/pcb.ctech
```

### A worked example, from an empty folder {#em-example}

Here is a complete, minimal EM workspace — a single 20 mm × 2.9 mm microstrip line on a two-layer PCB
technology, swept 1–10 GHz in 3 points. Four files:

```text
amp/
├─ .cws                        the workspace marker, naming the default technology
├─ pcb.ctech                   the stackup
├─ line.cem                    the EM setup
└─ Line/
   └─ layout/
      └─ Line.clay             the artwork
```

The `.cem` is JSON, and this is all of it — every field not written takes its documented default:

```json
{
  "FormatVersion": 1,
  "Name": "line",
  "LayoutRef": "Line/layout/Line.clay",
  "Frequency": {
    "StartExpr": "1", "StopExpr": "10", "NumPoints": 3,
    "Mode": "PointCount", "Kind": "Linear",
    "StartUnit": "GHz", "StopUnit": "GHz"
  },
  "Port1Z0Real": 50, "Port2Z0Real": 50
}
```

`LayoutRef` is **workspace-relative** — relative to the directory holding `.cws`, not to the `.cem`.
The `.cws` supplies the technology:

```json
{ "DefaultTechRef": "pcb.ctech" }
```

Nothing in the `.cem` names a technology, a kernel, a mesh or a port. The technology is inherited, the
kernel is chosen from the geometry, the mesh settings are the engine's own defaults, and this
structure's ports are the two ends of a uniform line by construction. Then:

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf em amp/line.cem</code></pre>

```text
[circuitRF] workspace: amp/.cws
[circuitRF] layout: amp/Line/layout/Line.clay
[circuitRF] technology: amp/pcb.ctech
[0] solving the cross-section
[3] solving the cross-section
note: Automatic chose "Uniform transmission line": this geometry is a uniform cross-section, which
      that analysis solves exactly and is about a thousand times cheaper than "Full-wave planar".
      Set Analysis to "Full-wave planar" if you want the full-wave answer anyway.
note: Dielectric interfaces truncated 20 substrate heights (32000 µm) beyond the outermost conductor
      on each side.
EM setup:  line
Kernel:    Quasi-static cross-section (CrossSection)
Points:    3
Wrote amp/results/line.s2p
Wrote amp/results/line_em.npy
```

Everything from `EM setup:` down is on **stdout**; the resolution lines, the progress and the notes are
on stderr.

A **full-wave** run differs only in what the files say, not in how you invoke it: draw port labels in
the layout with the layout editor's **Port** tool, set the setup's analysis to `Planar` (or leave it
`Auto` and let the geometry decide), and run exactly the same command. It will take very much longer —
a de-embedded full-wave point costs tens of seconds at the shipping mesh — which is why the progress
lines exist.

### Where the results go, and what `-o` moves {#em-output}

With no `-o`, the run writes **exactly where the Simulate button writes**: into the workspace's
`results/` folder. Two files come out, and they are not redundant:

| File | Holds |
|---|---|
| `<name>.sNp` | S-parameters only — the artefact a schematic's [SnP component](components.html#snp) references by path |
| `<name>_em.npy` | The whole `DataSet`, including the per-kernel **diagnostics** group — Z_c, γ, ε_eff, RLGC for the cross-section kernel; the calibration residual and usability flags for the full-wave one |

<div class="callout warn">
<span class="label">Why the default path is not the CLI's to choose</span>
<p>That results path is <b>predictable by design</b>, so a schematic's SnP reference stays valid across
re-runs. A headless run that minted its own file name would orphan every one of them — so
<code>circuitrf em</code> writes the same file <b>Simulate</b> does, and the acceptance test for the
verb compares the two Touchstones <em>byte for byte</em>.</p>
</div>

`-o` moves **the Touchstone only**. The `.npy` stays where it was, because it is the diagnostics
record of the run rather than the deliverable:

```text
$ circuitrf em amp/line.cem -o /tmp/mine.s2p
Wrote /tmp/mine.s2p
Wrote amp/results/line_em.npy
```

You do not have to get the extension right — the port count decides it, so a `.s2p` you typed for a
structure that turned out to have four ports is written `.s4p`.

With no workspace above the `.cem`, `results/` is created beside the `.cem` itself.

### note, warning, error — three lists, kept apart {#em-messages}

An EM run has three different things to say and they ask three different things of you, so they are
printed under three labels rather than flattened into one stream:

| Prefix | Means |
|---|---|
| `note:` | The run explaining itself — which kernel it chose and why, the mesh's own sentences, RLGC, the ports it found. Read these; they are the cheapest check that the tool is looking at the structure you think it is. |
| `warning:` | Something to act on — a stale `.sNp` about to be replaced, a technology that resolved but failed validation. |
| `error:` | Something you asked for and did not get — a results file that could not be written. |

### A refusal is a result {#em-refusals}

The EM engine declines geometry it cannot solve *correctly* rather than returning a plausible number.
Each refusal carries a written explanation of what is wrong with **this** setup, and `em` prints that
explanation rather than collapsing it into "EM failed":

```text
[circuitRF] workspace: amp/.cws
warning: Layout file not found: amp/Line/layout/Missing.clay
No layout: The layout 'Line/layout/Missing.clay' could not be found, so there is no geometry to
analyse. Point this EM setup at a layout that exists.
```

| Status | Means | Exit |
|---|---|---|
| **Refused** | The extractor or the kernel declined this geometry — see [what the engine refuses](mom-engine.html#refusals) | 1 |
| **No layout** | The layout reference did not resolve | 1 |
| **Engine error** | The solve failed | 1 |
| **Cancelled** | Stopped at a work boundary | 130 |

## `convert` — layout interchange {#convert}

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf convert &lt;input&gt; -o &lt;output&gt; [options]</code></pre>

Reads a layout in any format circuitRF understands and writes it in any other. It is the same reader
and the same writer **File ▸ Import** and **File ▸ Export** run — see
[Interchange](layout-editor.html#interchange) for what each format can and cannot carry — so a
conversion here and the same conversion through the GUI produce the same bytes.

| Format | Named by | As input | As output |
|---|---|---|---|
| circuitRF layout | `.clay` | the file | a **folder** of cells plus a `.ctech` |
| GDSII | `.gds`, `.gdsii`, `.gds2` | ✓ | ✓ |
| DXF | `.dxf` | ✓ | ✓ |
| Gerber + Excellon | a **folder**, or one Gerber/drill file | ✓ | a **folder** |
| Board | `.kicad_pcb` | ✓ | ✓ |

**Every ordered pair works** — DXF to Gerber, Gerber to board, GDSII to DXF, board to GDSII, and the
rest. There is no privileged direction and no hub format you have to route through by hand: a
conversion is an import followed by an export, and `convert` does both.

Formats are read off the paths. A folder means Gerber; a file with no telling extension is classified
by its *content*, through the same classifier the Gerber import uses. `--from` and `--to` override
that, and `--to` is **required** when the output is a folder, since a folder could be either Gerber or
`.clay`.

### Examples {#convert-examples}

Board file out to a fab house as artwork plus drill:

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf convert board.kicad_pcb -o fab/ --to gerber</code></pre>

A folder of Gerbers back to a board file:

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf convert fab/ -o recovered.kicad_pcb</code></pre>

A mechanical drawing straight to artwork — no board tool in the middle:

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf convert outline.dxf -o gerbers/ --to gerber</code></pre>

A mask set to a drawing your mechanical engineer can open, at the DXF version their tool wants:

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf convert mmic.gds -o mmic.dxf --dxf-version AC1015</code></pre>

Bring a board in as editable circuitRF cells and keep the technology it declared:

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf convert board.kicad_pcb -o cells/ --to clay</code></pre>

One cell out of a GDSII library that holds many:

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf convert lib.gds --list-cells
<span class="prompt">$ </span>circuitrf convert lib.gds -o coupler.dxf --cell COUPLER</code></pre>

Convert a directory of drawings in one line:

<pre><code class="cmd"><span class="prompt">$ </span>for f in dxf/*.dxf; do circuitrf convert "$f" -o "gds/$(basename "${f%.dxf}").gds"; done</code></pre>

### Options {#convert-options}

| Option | What it does |
|---|---|
| `-o, --output <path>` | The file to write — or the **folder**, for `gerber` and `clay`. Required. |
| `--from <fmt>`, `--to <fmt>` | `clay`, `gdsii`, `dxf`, `gerber`, `board`. Say it when the path does not. |
| `--cell <name>` | Which cell to export, when the source holds several. |
| `--list-cells` | Report what the input holds and write nothing. |
| `--name <stem>` | What to call the written Gerber file set. Default: the cell's name. |
| `--tech <file.ctech>` | The technology to convert against, instead of the one the layout resolves. |
| `--workspace <file.cws>` | The workspace a `.clay`'s references resolve against. Default: the nearest one above it. |
| `--keep-cells <dir>` | Keep the cells the import produced instead of discarding them. |
| `--dbu <n>` | Database units per micron for an imported design. Default `1000` — one DBU is one nanometre. |
| `--dxf-version <v>` | `AC1015` (R2000), `AC1018` (R2004), `AC1032` (R2018, the default). |
| `--dxf-units <n>` | The `$INSUNITS` value for a DXF that declares none. |
| `--drill-units <mm or inch>` | Excellon coordinate units, when the file does not say. Applies to **every** drill file in the set. |
| `--drill-format <int>:<dec>` | Excellon digit counts, e.g. `2:4`. Applies to every drill file in the set. |
| `--drill-zeros <leading or trailing>` | Excellon zero suppression. Applies to every drill file in the set. |
| `--accept-inferred-drill-format` | Take each drill file's own inference rather than refusing. |

### Which cell gets exported {#convert-cell}

A GDSII library, a DXF drawing and a board file can all hold more than one cell, and an export writes
one design. Unless `--cell` says otherwise, `convert` takes the source's own idea of the top: the
GDSII structure nothing else instances, DXF's model space (the drawing itself, not a `BLOCK`
definition), the board rather than one of its footprints. A Gerber set is always one flat cell. When
the source genuinely has no unambiguous top, the conversion stops and tells you to name one —
`--list-cells` prints the choices.

### The technology, and why it matters here {#convert-tech}

An import brings a layer table with it, and in the GUI those layers land on the technology your
workspace already has open. Headless there is no open workspace, so `convert` **writes a `.ctech` of
its own** from what the file declared, exactly as **File ▸ Import ▸ Gerber** does. That is what keeps
layer names, colours and Gerber file suffixes alive across a conversion instead of leaving every layer
a bare number.

Two consequences worth knowing:

- **`--tech` is how you convert against a process you already have.** Point it at a `.ctech` and the
  source's layers reconcile against it — matched layers keep your names and your Gerber suffixes,
  unmatched ones are added. Without it, an intermediate technology is invented from the file alone,
  and a Gerber export then names its files from synthetic suffixes.
- **`--keep-cells <dir>` leaves a design you can open.** Cells plus the technology they point at —
  the honest way to see what a conversion actually understood before you send the result anywhere.

**GDSII is the one exception, and it is the format's own doing.** GDSII identifies a layer by a
number, not a name, so an import has nothing to name it *with*: the numbers come through exactly, the
names do not. Convert from GDSII with `--tech` pointing at the technology those numbers belong to and
the names come back.

### When it refuses {#convert-refusals}

<div class="callout note">
<span class="label">A drill file that does not state its format is a refusal, not a guess</span>
<p>Many Excellon files do not say whether their coordinates are inches or millimetres, or whether
leading or trailing zeros are suppressed — and leading versus trailing differ by <em>four orders of
magnitude</em> on identical text. The GUI asks you. There is nobody to ask here, so the conversion
stops, prints what it inferred and the evidence behind it — including whether the holes land inside
the artwork's own outline — and names the flags that answer it. Accept the inference with
<code>--accept-inferred-drill-format</code>, or state it outright with <code>--drill-units</code>,
<code>--drill-format</code> and <code>--drill-zeros</code>.</p>
</div>

**A `--drill-*` flag settles the whole set, not the first file.** A drill flag is a statement about
the run — one exporter wrote the `.drl` and the `.rou` next to it in one format — so it applies to
every drill file the conversion reads, and the refusal is printed once rather than once per file.
`--accept-inferred-drill-format` works the same way, with one difference worth knowing: it accepts
**each file's own** inference rather than forcing the first file's format onto the rest.

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf convert fab/ -o board.kicad_pcb --drill-units mm --drill-format 3:4 --drill-zeros leading</code></pre>

Reach for the flags less often than you might expect: a file that writes every coordinate at its full
width — same number of digits throughout, leading zeros intact — states its own format by doing so,
and the conversion reads it off the coordinates and says as much. The flags are for the files that
leave a genuine question, and the note printed for every drill file names which parts of its format
were **declared**, which were **inferred**, and from what.

It also stops, rather than guessing, when a design instantiates cells drawn against a *different*
technology and the layer mapping needs confirming; when coordinates overflow GDSII's 32-bit range; and
when the source holds several cells and none of them is an unambiguous top. Every refusal exits `1`
and writes nothing at all.

Everything short of a refusal is a **note on stderr**, counted and named: labels flattened to
geometry, curves turned into polygons, holes keyholed, bitmaps dropped, unresolved instance
references, layers with no mapping in the target format. stdout carries only the paths written, one
per line, so a script can consume them directly:

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf convert board.kicad_pcb -o fab/ --to gerber 2&gt; convert.log | zip -j fab.zip -@</code></pre>

## `new` — a workspace or a cell {#new}

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf new workspace &lt;dir&gt; [--name N] [--tech &lt;id&gt;|none]
<span class="prompt">$ </span>circuitrf new cell &lt;workspace&gt; &lt;cellName&gt; [--views schematic,symbol,layout]</code></pre>

These create the **first correct document** — the thing that is awkward to write by hand because the
folder structure and the primacy files have to be right before anything will open it.

They are not a second implementation. `new workspace` calls the same function **File ▸ New
Workspace** calls, and `new cell` the same one **New Cell** calls, so a tree created here is a tree
the application created.

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf new workspace ~/designs/Amp --tech pcb-4layer_FR-4_62mil_1oz
<span class="output">/home/you/designs/Amp
/home/you/designs/Amp/.cws
/home/you/designs/Amp/tech/pcb-4layer_FR-4_62mil_1oz.ctech</span>

<span class="prompt">$ </span>circuitrf new cell ~/designs/Amp Stage1 --views schematic,symbol
<span class="output">/home/you/designs/Amp/Stage1
/home/you/designs/Amp/Stage1/schematic/Stage1.csch
/home/you/designs/Amp/Stage1/symbol/Stage1.csym</span></code></pre>

**The paths it creates are the result**, on stdout, because what you do next is almost always read or
rewrite one of them.

<div class="callout note">
<span class="label">Every default is the dialog's</span>
<p>Whatever the GUI's dialog pre-selects, the verb selects with no flag: <code>--tech</code> opens on
the same technology the <b>New Workspace</b> combo box opens on (<code>--tech none</code> is its
"None" row), and <code>--views</code> defaults to <code>schematic</code>, which is what <b>New
Cell</b> creates. Anything the dialog would have <em>asked</em> is a refusal that names the flag
answering it — never a guess.</p>
</div>

**There are deliberately no per-primitive edit verbs.** There is no `place-instance` and no
`set-parameter`: once a document exists, the way to change it is to **write** it. Every format
circuitRF owns is readable, versioned JSON — see [File formats](file-formats.html) — and that file
*is* the interface.

<h3 id="new-add">`new` is one verb with a noun</h3>

`new workspace` and `new cell` are two nouns of one verb, not two verbs. It reads better and, more to
the point, the number of top-level verbs is a cost every reader of `--help` pays.

---

## `import part` — a footprint and its symbol {#import}

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf import part &lt;file-or-folder&gt; --into &lt;workspace&gt; [--cell N] [--variant V]
                                            [--list-parts] [--tech f.ctech] [--add-layers]</code></pre>

The same code **Import Component** runs: it reads a downloaded component — a land pattern, a symbol,
and the pin-to-pad map that joins them — and writes it into your workspace as one cell.

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf import part downloads/SOT-23.zip --into ~/designs/Amp --list-parts
<span class="output">SOT-23-3
SOT-23-5</span>

<span class="prompt">$ </span>circuitrf import part downloads/SOT-23.zip --into ~/designs/Amp --cell SOT-23-3
<span class="output">/home/you/designs/Amp/SOT-23-3
/home/you/designs/Amp/SOT-23-3/layout/SOT-23-3.clay
/home/you/designs/Amp/SOT-23-3/symbol/SOT-23-3.csym</span></code></pre>

A source holding several parts is **refused with them listed**, never resolved by taking the first.

**Layers the technology does not have are reported, and nothing is written**, unless you pass
`--add-layers`. The GUI's own install is session-only and writes nothing to disk either, so this is
the same behaviour and not a headless restriction.

---

## `check` — is it well formed, does it resolve, is it sound? {#check}

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf check &lt;path&gt; [--recursive] [--severity warning|error]</code></pre>

Point it at a workspace, a cell folder, or one document. **It runs no analysis and it writes
nothing**, which is what makes it cheap enough to call after every edit — and safe to run on a
read-only tree, or on a workspace you have open in the GUI at the same time.

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf check ~/designs/Amp
<span class="output">Amp/Stage1/schematic/Stage1.csch
  error   two labels name one physical net: 'vout' and 'out'
Amp/Stage1/layout/Stage1.clay
  warning no technology resolves for this layout
5 document(s): 1 error, 1 warning</span></code></pre>

**Every finding comes from a validator the application already uses** — the same view-file check, the
same primacy rule, the same technology walk-up, the same net extractor, the same elaborator, the same
DRC engine. A rule that lived only in `check` would be a rule the application does not enforce, and a
design would pass here and be refused the moment somebody opened it.

<div class="callout note">
<span class="label">Warnings are reported and still exit 0</span>
<p><code>--severity</code> decides the exit code, and it defaults to <code>error</code>. Warnings are
<em>always</em> printed — a check that hid them to keep the exit code clean would make the exit code
useless. Two states are warnings on purpose: a cell folder holding several views with none named
primary, and a layout that resolves no technology. Both are normal.</p>
</div>

The kind of document is inferred from the path, exactly as `convert` infers a format. A GDSII or
Gerber file is **named as interchange** rather than called unreadable — it is simply not validated,
because there is nothing to validate it against.

---

## `explain` — what did circuitRF decide? {#explain}

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf explain &lt;path&gt; [--expr "&lt;expression&gt;"] [--set var=expr]
                            [--analysis [&lt;name&gt;]] [--ref &lt;relative-ref&gt;]</code></pre>

`check` answers "is something wrong". `explain` answers the question that is **not** a failure: which
technology did this layout get, which analysis would actually run, what does this expression evaluate
to here, and what does this cell reference point at?

It reports **the walk as well as the answer**, and that is the useful half. Resolution in circuitRF is
a series of walk-ups — a document's ancestor workspace, a layout's technology, a `.cem`'s two
independent references — and which one produced an answer is exactly what you cannot see from the
file.

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf explain Amp.cem
<span class="output">workspace   from Amp.cem            → /home/you/designs/Amp/.cws        (nearest ancestor .cws)
layout      from Amp.cem            → Amp/Line/layout/Line.clay         (workspace-relative)
workspace   from Line.clay          → /home/you/parts/.cws              (nearest ancestor .cws)
technology  from /home/you/parts    → parts/tech/pcb-2layer.ctech       (the .cws DefaultTechRef)</span></code></pre>

Two different workspaces there, and that is legitimate: a `.cem` in one workspace may point at a
layout in another, and that layout's layers must be read by *its* technology.

<h3 id="explain-analysis">`--analysis` — which chain would run</h3>

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf explain pa.cnl --analysis
<span class="output">SWEEP1   parametric_sweep   enabled  runnable  root   → dispatched by hb
  chain: SWEEP1 → HB1
  sweep: Pavl over 1.0000e+09 … 3.0000e+09 step 5.0000e+08 Hz  (stated GHz, scale 1e9)
HB1      hb                 enabled  runnable         → promoted to SWEEP1</span></code></pre>

A sweep is reported in **base SI with its unit and its scale**. Reading a mark without its scale has
already produced a run at 2 Hz that looked entirely normal.

<h3 id="explain-expr">`--expr` — evaluate in the design's own scope</h3>

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf explain pa.cnl --expr "Zopt*2" --set Zopt=12.5
<span class="output">Zopt*2 = 25   (real)</span></code></pre>

Through the one expression engine, in the design's own resolved scope — never by substitution — with
`--set` applied first exactly as a run verb applies it. The kind is reported, never coerced.

<h3 id="explain-ref">`--ref` — where does this reference land</h3>

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf explain Stage1.csch --ref ../parts/SOT-23-3
<span class="output">../parts/SOT-23-3 → /home/you/designs/parts/SOT-23-3   resolved
                     outside this workspace: no</span></code></pre>

Where resolution fails, **that is the answer** — a sentence naming what was looked for and where it
was looked. You are usually running this verb precisely because something did not resolve.

---

## `read` — a result, or a document, back {#read}

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf read &lt;path&gt; [--only a,b] [--group g] [--json]</code></pre>

The inverse of a run verb. A `.npy` or a Touchstone file is loaded back into cubes — through the same
two loaders the Data Display's source library reads a file with — and one of circuitRF's own
documents comes back as **its own bytes**, unchanged.

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf read results/Amp_em.npy
<span class="output">results/Amp_em.npy  (npy)
  group (default):
    S                        Complex  freq[201] Hz x i[2] port x j[2] port
    Z0                       Complex  port[2] port
  group planar:
    MeshCells                Real     cell[1544]</span></code></pre>

It writes nothing, and it takes one file at a time. For "what is in this workspace", use `check` or
`explain`; for a GDSII or a Gerber set, use `convert`.

With `--json`, `--only` and `--group` narrow what comes back — which matters, because reading a
20,000-point swept loadpull in full is the expensive direction:

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf read hero3.npy --only Pout_dBm --json</code></pre>

---

## `elab` — the elaborated netlist {#elab}

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf elab &lt;file.cnl&gt;</code></pre>

Elaborates and stops: flattens the hierarchy, resolves every parameter and expression top-down, and
numbers the nodes, then prints [the elaborated netlist](netlist.html) — the exact thing the engines
consume. No analysis runs.

This is the debugging verb. When a value is not what you expected, `elab` is where you find out
whether the expression resolved to something different from what you meant, or resolved correctly and
the analysis is doing something else.

## `--json` — one machine-readable document {#json}

Every verb takes `--json`, spelled that way everywhere. It changes exactly one thing: **stdout carries
a single JSON document and nothing else.**

stderr is untouched — progress, `[circuitRF]` notes, warnings and refusal sentences stream exactly as
they always did — so a script watching stderr cannot tell whether the flag was passed.

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf sparam amp.cnl --json | jq '.outputs[].path'
<span class="output">"amp.s2p"</span></code></pre>

One schema serves every verb:

<pre><code>{ "circuitrf": { "version": …, "verb": … },
  "input":     { "path": …, "analysis": … },
  "status":    "ok" | "not-converged" | "failed",
  "exitCode":  0 | 1 | 2 | 130,
  "outputs":     [ { "kind": …, "path": … }, … ],
  "diagnostics": [ { "id": …, "severity": …, "message": …, "arguments": { … } }, … ],
  "result":      { … } }</code></pre>

- **`input.analysis` is the chain that actually ran**, after promotion — not what you asked for. The
  difference is a whole sweep axis, so you must be able to see it from the document alone.
- **A failed run still emits a document.** The failure *is* the payload, so you never have to tell
  "no output" apart from "output I could not parse".
- **The diagnostic `id` is the contract; the `message` is not.** Match on `id`. Templates are
  reworded freely, and the sentence is always English and culture-invariant.
- **`result` holds cubes** (`groups`) for a run, a **summary** for `lp`/`lpp` — the same one-row-per-Γ-
  point projection the table prints, and `--all` adds the cubes — a **check** or **explain** report for
  those two verbs, and a **document** for `read`.
- Numbers are raw, invariant and unrounded. `NaN` and infinity are written as JSON's named literals,
  because a loadpull grid genuinely contains NaN wherever a point never converged.

---

## `serve` — a protocol server {#serve}

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf serve --root &lt;dir&gt; [--kits &lt;dir&gt;]</code></pre>

Speaks a JSON-RPC tool protocol over stdin and stdout, so an external program — a CI job, an
automation harness, a design agent — can discover what circuitRF can do and ask it to do it. It is
started by that program, not by you, and it ends when that program disconnects.

**Six tools, and each is a verb you already have:**

| Tool | Runs |
|---|---|
| `run` | `sparam`, `dc`, `hb`, `lp`, `lpp` or `em`, chosen by an argument |
| `check` | `check` |
| `explain` | `explain` |
| `create` | `new workspace` or `new cell` |
| `import` | `import part` or `convert` |
| `read` | `read` |

**Every tool returns exactly the document `--json` writes**, byte for byte, because the server calls
the verb rather than re-implementing it. Nothing is reachable through the server that is not
reachable from your own shell, and nothing is reachable from your shell that the server cannot do.

<div class="callout note">
<span class="label">What it will not do</span>
<ul>
<li><b><code>--root</code> is required</b>, and every path a client names resolves under it. A path
that escapes — through <code>../</code>, through an absolute path, or through a symbolic link — is
<b>refused, naming the root</b>. It is never quietly clamped to something inside.</li>
<li><b>Nothing deletes, and nothing overwrites an existing workspace.</b> There is no person at the
other end to confirm with, so the answer is no. A client that wants a file gone deletes it itself.</li>
<li><b>No shell, and no program a client names.</b> Device workers and PCell generators still run as
they always did; nothing new becomes launchable because something asked.</li>
</ul>
</div>

**A long run reports progress and can be cancelled** — a client that asks for progress is sent it as
the run moves, and a cancellation stops the run at a work boundary and returns exit code 130 having
written nothing.

`serve` is the one verb whose stdout is not the result: it carries the protocol, so everything else —
progress, notes, warnings, device-worker logs — goes to stderr, where the program that started it
picks it up. For that reason it takes no `--json` of its own; every call through it already returns
one.

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf serve --root ~/designs 2&gt; serve.log</code></pre>

---

## Exit codes {#exit}

| Code | Meaning |
|---|---|
| **0** | Ran, and produced something usable |
| **1** | Could not run — bad arguments, a missing file, no matching analysis, a refusal, an exception |
| **2** | Ran, but did not converge |
| **130** | Stopped — a run cancelled at a work boundary, by `em`'s own stop or by a `serve` client's cancellation |

**`2` is deliberately not the same test for every verb.** `hb` and `dc` fail on any non-converged
solve. A loadpull grid in which some points do not converge is a normal and useful result — the edge
of a Γ grid routinely will not — so `lp` returns `2` only when **every** grid point failed, and `lpp`
only when neither optimum converged and there is no follow-on grid. A rule that failed the whole run
on one bad point would make the exit code useless in a script.

## Scripting patterns {#scripting}

**Keep the table, keep the log, and still see it run.**

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf lp hero3.cnl -o hero3.npy &gt; hero3-table.txt 2&gt; hero3-run.log</code></pre>

**Sweep a variable the netlist already has**, without editing the netlist:

<pre><code class="cmd"><span class="prompt">$ </span>for p in -10 -5 0 5; do circuitrf hb pa.cnl --set Pavl_dbm=$p -o pa_$p.npy; done</code></pre>

**Fail a build on a regression**, using the exit code:

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf hb pa.cnl -o out.npy || echo "PA did not converge" &gt;&amp;2</code></pre>

**Validate a whole workspace in CI**, before anything is run:

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf check ~/designs/Amp --severity error || exit 1</code></pre>

**Author, validate and simulate with no display at any step** — the whole loop:

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf new workspace build/Amp
<span class="prompt">$ </span>circuitrf new cell build/Amp Stage1
<span class="prompt">$ </span>cat &gt; build/Amp/Stage1/Stage1.cnl &lt;&lt;'EOF'
<span class="prompt">  </span>… your netlist …
<span class="prompt">$ </span>EOF
<span class="prompt">$ </span>circuitrf check build/Amp || exit 1
<span class="prompt">$ </span>circuitrf hb build/Amp/Stage1/Stage1.cnl -o out.npy</code></pre>

**Pull one number out of a result**, without parsing a table:

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf read out.npy --only Pout_dBm --json | jq '.result.groups[""].Pout_dBm.values[-1]'</code></pre>

**Ask why a file resolved the way it did**, when a run used a technology you did not expect:

<pre><code class="cmd"><span class="prompt">$ </span>circuitrf explain Amp.cem</code></pre>

**Re-extract every EM setup in a workspace** after a technology edit — the layout and stackup
references resolve themselves, so the loop needs nothing but the file names:

<pre><code class="cmd"><span class="prompt">$ </span>for f in em/*.cem; do circuitrf em "$f" || exit 1; done</code></pre>

Because each of those writes the same file **Simulate** writes, a schematic that references the
extracted Touchstones picks the new results up with no further action.

---

<p class="small">See also: <a href="simulations.html">Simulations</a> (what each analysis computes) ·
  <a href="netlist.html">The netlist format</a> · <a href="em-setup.html">EM Setup</a> ·
  <a href="mom-engine.html">The MoM engine</a> ·
  <a href="npy-export.html">Results &amp; data export</a> ·
  <a href="pdk-integration.html">Kits and external device models</a>.</p>
