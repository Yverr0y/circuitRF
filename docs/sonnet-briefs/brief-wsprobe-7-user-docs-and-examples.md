# Sonnet Brief — WSP-7: the WSProbe user documentation, its appendix on stability, and the example designs

**Read `brief-wsprobe-0-overview.md` first**; this brief lands last and documents what WSP-1–6 (and
WSP-8) shipped. Reference: T. A. Winslow, *General Circuit Analysis Using The WSProbe* (2023) — cited
by section and equation on every page written here, as the code does. The document's own §2–§3 (the
history of stability methods) is the outline of the appendix; its reference list ([2]–[32]) is the
appendix's bibliography.

**Scope.** One dedicated reference page with its own appendix, the cross-links from the pages that
already exist, the example designs the pages render their figures from, and the docs-factory run.
Written for the designer the document addresses: someone who has read Winslow and expects to find
`H0`, `Y0`, `ZG`, `ZL`, `LG`, `F` and `wsp(2i−1, 2j)` under exactly those names.

**Rules that bind every sentence:** `docs/design/user-docs-factory.md` (Markdown source under
`docs/user/src/`, figures from the app, one nav order); the owner's note-writing rule (a note's
audience is self-selected — say what the reader's chosen path buys, never that a cheaper one exists);
the vendor-name rule of overview §1 — the document names a commercial simulator on nearly every page
and **none of those names may appear here**; no personal paths; every figure redrawn, none copied.

---

## 1. The page: `docs/user/src/reference/wsprobe.md`

Front matter as the other reference pages (`title: The WSProbe`, `slug: reference/wsprobe.html`,
`doc-kind: Reference Guide`, lede, keywords: `WSProbe, Winslow, stability, driving point impedance,
H0, Y0, loop gain, bidirectional impedance, NDF, Ohtomo, Kurokawa, envelope`). Nav entry in
`_nav.txt` under **Simulate**, directly after `derived-metrics.html`:

```
reference/wsprobe.html | The WSProbe: nodal transfer functions for stability and in-situ design — with an appendix on stability itself.
```

Sections, in this order (each `{#anchor}` is a Help deep-link target):

**§1 What the WSProbe is** — Two terminals, an orientation, electrically a 0 V short. What it
produces: the `wsp` matrix. The one-paragraph statement that it perturbs nothing and needs one
analysis sweep (document §4, p. 33). The symbol figure (`assets/symbols/wsprobe*.svg`, generated).
A redrawn Fig. 16 (the two injections and the four readings) as an inline SVG authored for this page —
**not** the document's figure.

**§2 Placing it** — Drop it into a wire; the wire is cut like an IProbe's. `G` and `L`, and what
flipping does. Label = the instance name; a probe inside a sub-cell is `X1.GATE`. The `.cnl`
spelling. Where to put probes: "on the terminals of active devices … where the transfer functions
have the greatest chance of detecting … the Kurokawa signature" (document §4, p. 36).

**§3 What it computes** — The `wsp` matrix (Eq. 31–34) with the indexing rule stated once
(`wsp(2i−1, 2j−1)` … Eq. 33) and the accessor spellings (`SP1.wsp(3, 13)`, `SP1.idx("GATE")`). The
six default outputs with their equations: `H0` (Eq. 50), `Y0` (Eq. 52), `ZG`/`ZL` (Eq. 67/68), `LG`
(Eq. 92), `F = 1 − LG`. The reduced two-port (Eq. 44/48) in a callout: everything else is derived from
it.

**§4 Reading the results** — The primary metric: polar plots of `1/H0` and `1/Y0`; Kurokawa's
three conditions (Eq. 107/108) in words; a figure of the series resonator's two loci (§4.10's own
example, rendered by the factory from the example workspace) showing the signature on one and not
the other; the **check both** rule and why (pole masking, p. 73–79). The Kurokawa readout and "Mark
crossings" on the trace card. Then the loop gains, with the document's caveat carried at full weight
(p. 50: loop gains "are not fundamental circuit quantities … not rigorous stability measurements …
not necessarily wrong, just incomplete"), and the zero-feedback case where every loop gain is 0 and
the open-port immittances carry the signature (p. 62–63). The bidirectional impedances and the
`ZG ≠ 1/YG` warning (p. 56).

**§5 The derived metrics** — The complete catalogue, one table per group, each row: the document's
function name, its arguments, the equation and its citation, and where it is on the trace card. Groups
and rows exactly as WSP-2 §1–2 and WSP-3 §2–6 define them. The units deviation for the immittance
models (farads and henries; WSP-2 §2.8) in a callout. The margin slot with its D-12 sentence and the
EuMIC-2024 citation. The normalised loci under circuitRF's own name.

**§6 Probe pairs: a block in situ** — `wsp_yparam2`, `wsp_block_calc`'s 16 outputs, in-situ
renormalisation; the residual diagnostic; the reduced two-port as a network source so μ/K/circles
apply to a stage *inside* the amplifier.

**§7 Global stability: bifurcation and Ohtomo** — Sides (G/L), the same-orientation requirement,
the prerequisite each subnetwork be stable alone; `wsp_loopgain_ohtomo` and the `+1` critical point;
`Π(G_i − 1) = det(M)` stated as the reason the method is global.

**§8 The stability envelope** — What it answers; the probe-at-the-termination precondition and its
refusal; `wsp_loadpull`/`wsp_loadpull_unstable`; the envelope card; that it costs no re-simulation
(document §9, p. 120) and works on a large-signal `wsp` too.

**§9 Under harmonic balance** — The `SS*` keys; what "tickle" means here; the drive-swept fan of
`1/H0(ssfreq)`; parametric instability at `f0/2`; the low-drive limit equals the S-parameter answer;
the cost model in one paragraph with a pointer to the performance notes (WSP-8: aligned grids, the
sideband order, the drive-sweep reuse).

**§10 NDF** — What it is (Eq. 181), what it counts, the five properties as the run reports them, the
passivation table in user terms (which components are exact, which need `PassiveVars`, which are
refused and why), the SDD pattern for a scaling variable, the probe route (Eq. 186). The "K is not
enough" example (WSP-6 gate (i)) with its figure.

**§11 Headless** — `circuitrf sparam … NDF=yes`, `measure` lines for every function, `explain
--analysis` listing probes and passivation, `plot --trace cube=wsp,probe=…`, `--json`.

**§12 Caveats, in the document's own words** — one list: loop gains incomplete; pole masking; the
envelope's precondition; Ohtomo's prerequisite; circuit fidelity ("no stability detection method will
capture an instability that is mathematically NOT contained in your simulation construction", p.
113); the margin not yet transcribed.

**§13 References** — the four Winslow works (overview §1.1, with DOIs), then the document's own
list [2]–[32] as bibliographic entries. Public bibliographic data; nothing else from the document is
reproduced.

**Appendix A — Stability, from the beginning** `{#appendix-stability}` — A self-contained
introduction in the order the document takes (its §2–§3), one short section each: BIBO and
right-half-plane poles; negative resistance (Kurokawa's oscillator, Eq. 9–10); Rollett's `K` and `B`
and **the proviso** (Eq. 5–8; the derived-metrics page's μ/K are linked, not repeated); Barkhausen's
loop gain (Eq. 11–13); Bode's return ratio and return difference `F = Δ/Δ0` (Eq. 14–16); NDF
(Eq. 17); Jackson's stability index (Eq. 18–20, App. B); Middlebrook's double injection (Eq. 21–23);
Tian's bilateral return ratio (Eq. 24–25); auxiliary generators and pole-zero identification
(Eq. 26–30 — why `H0`/`Y0` carry the determinant); the S-parameter probe and why it fails with
feedback; Ohtomo; and how the WSProbe's four transfer functions reproduce all of them (the
document's §3.12 summary). Each subsection ends with "in circuitRF:" — where that quantity is (the
derived-metrics page, this page's §5, or "not available: …"). Redrawn figures only where a diagram
earns its place (Kurokawa's two-block model; Bode's `gm` with return path; the double-injection node).

---

## 2. Cross-links and edits to existing pages

- `derived-metrics.md`: a new short section "Stability from inside the circuit" pointing at
  `wsprobe.html`, and one sentence under μ/K: "a two-port that passes K and |Δ| can still oscillate —
  see the WSProbe page's NDF example."
- `components.md`: a `WSProbe` entry beside `IProbe` (symbol, pins G/L, parameters: none, "what it
  is for", link).
- `simulations.md`: under S-Parameters, the `NDF=yes` row and a pointer; under Harmonic Balance, the
  `SS*` rows and a pointer.
- `measurements.md`: the probe accessors and one worked `measure` example.
- `cli.md`: the `sparam` additions and the `plot` shorthand.
- `plot-types.md`: the polar-only items and the `+1` reference mark.
- `index.md` (reference contents): the new page in the Simulate group.

---

## 3. Example designs (fixtures the figures come from)

Under `src/Ui/Diagnostics/Fixtures/` (the factory's home; not `circuitRF_demo/`, which is
git-ignored), each also usable as a `testdata/` case by the briefs that created it:

1. **The two unstable resonators** (document §4.10; WSP-1 gates (b)/(c)) — one schematic each, a
   WSProbe in the node, S-parameter sweep 0.5–3 GHz; the figures of §4 come from here. Redrawn
   schematics, our own component values (the document gives none).
2. **A two-stage FET amplifier** with a WSProbe on every gate and drain and at both `Term`s — the
   workhorse for §3, §5, §6, §7, §8 figures and for the pair/Ohtomo/envelope examples; the SDD FET
   of the Hero series with the `NDFgm` scaling pattern of WSP-6 written in so the NDF section's figure
   is real.
3. **The pumped varactor** (WSP-5 gate (e)) — the HB `SS*` figure of §9 with the drive-swept fan
   and the `f0/2` signature.
4. **"K is not enough"** (WSP-6 gate (i)) — the §10 figure.

Each fixture carries a `README` sentence saying which page and which gate it serves, so it cannot
silently become orphaned.

---

## 4. The factory run

Run `tools/DocGen` **twice** and diff run 1 against run 2 before reporting the change set — three
figure families are nondeterministic (`docgen-nondeterministic-figure`), and a report of hundreds of
changed `.svg`s is a red flag, not a result. Classify id-churn against real change (the id counter is
hex). The orphan check must pass (the new page is in `_nav.txt`; every `{{ui: …}}` has a
`FigureCatalog` entry — `plot-inspector-wsprobe` from WSP-4, `wsprobe-resonator-polar`,
`wsprobe-envelope-card`, `wsprobe-hb-fan`, `wsprobe-ndf-k`).

---

## 5. Gates

- The page builds, appears in the site TOC and the Previous/Next chain, and every internal anchor
  resolves (the factory's own link check).
- **Every equation on the page is one of the equation register's rows** (overview §4) with its
  citation; a script in the test compares the page's `(Eq. n)` citations against the register and
  fails on an unlisted one — the way a formula gets in that nobody checked.
- A grep of `docs/user/src/` for the vendor names of overview §1 returns nothing.
- A grep for absolute paths under a home directory returns nothing.
- `SvgLint` passes on every new figure; the resonator figure shows two loci with exactly one
  negative-real-axis crossing between them (assert on the rendered SVG path, as the docs-factory
  tests already do for other figures).
- The fixtures' `.cnl` extractions run headless (`circuitrf sparam`/`hb`) and reproduce the numbers
  the page quotes (1.5915 GHz; the envelope's unstable arc; `NDF_poles`).

## On completion

Findings to `src/Ui/RESOLVED.md` § User-Docs Factory (where the factory's traps are recorded);
**never a `CLAUDE.md`**. Update `docs/design/user-docs-factory.md`'s figure catalogue list and
`docs/design/stability-wsprobe.md`'s "where documented" table.
