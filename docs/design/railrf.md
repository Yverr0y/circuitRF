# railRF — Power-Distribution-Network Impedance on Real Board Shapes

**Status:** Proposal — rev 1, **for external review** · **Date:** 2026-09-06 · **Phase:** unstarted
**Reads with:** `docs/design/data-display.md` (the plot layer this reuses),
`docs/design/mom-engine.md` (the full-wave solver this deliberately does *not* use, and why),
`docs/design/layout-view.md` and `src/Design/Layout/Interchange/` (the Gerber / Excellon /
`.kicad_pcb` readers this is built on), `docs/design/ui-architecture.md` (the firewall this obeys),
`docs/user/reference/derived-metrics.html` (the passive readouts that turn a vendor part file into
a PDN element).

> **The name is not decided.** *railRF* is a working title — the rail is what the tool is about, and it
> follows the house pattern (`harmonicaRF`, `wBond`). Alternatives considered: *quietRF*, *pdnRF*,
> *decapRF*. Whoever approves this proposal should pick.

**No code is written until this note is approved.**

---

## 1. What railRF is, in one paragraph

railRF answers a question circuitRF cannot answer today and neither can a SPICE-class simulator: **on
this board, with these decoupling capacitors in these positions, what impedance does the load actually
see, and where does the copper stop helping?** It takes the artwork you already have — Gerbers, or a
board file — plus a stackup, a parts list and a target impedance, and returns Z(f) at the load with
the target mask over it, the anti-resonances named, the plane's own resonances named, and a ranked
list of which capacitors are earning their place. It then does the same thing to a second board and
shows you the difference.

The gap it fills is specific. A SPICE simulator will model your capacitors with their parasitics
perfectly well and knows nothing about the copper they are mounted on. A full-wave EM tool will model
the copper and is far too slow to sweep a whole plane pair with forty capacitors on it. The useful
model sits between them, it is well established, and nothing on the desk of a working board designer
implements it.

---

# 2. How the tool works, from the outside

*This section is the proposal. Everything after it is implementation.*

## 2.1 The three questions

railRF is built around three questions a board designer asks in this order:

**Q1 — Does this rail meet its target?** You have a target impedance — `Z_target = ΔV_ripple / ΔI_step`,
or a frequency-dependent mask if your silicon vendor gave you one. You want Z(f) at the load, from DC
to wherever your transient content dies out, with that mask drawn on it and the violations named.

**Q2 — Which capacitors are actually doing anything?** You have forty decaps because the reference
design had forty decaps. Some of them are in parallel with a lower-ESL part two millimetres away and
contribute nothing. Today the way to find out is to build the board and start removing parts. railRF
ranks every capacitor by how much the worst mask violation would grow if you deleted it.

**Q3 — Did my form factor break it?** A supplier gave you a reference design — schematic, BOM,
Gerbers. You had to re-shape it to fit an enclosure. Same parts, same net topology, different outline
and different placement. railRF runs both and shows you the delta, before the board exists.

## 2.2 What you provide

Six inputs. Two are mandatory, four have defaults you can accept and revisit.

### The artwork — mandatory

Either a **board file** (`.kicad_pcb`) or a **Gerber set** with its Excellon drill file. circuitRF
already reads all of these; railRF adds no new import path.

> **The difference between the two matters and it is the single biggest practical constraint on this
> tool.** A board file carries **nets, footprints, refdes and values**. A Gerber set carries
> **geometry only** — it is a photoplot. There is no net in a Gerber, no component, no value. From
> Gerbers alone, railRF can see that a pour exists and that a drill hole passes through it; it cannot
> know that the pour is `+1V8` rather than `+3V3`, or that the two pads at (43.2, 18.7) belong to a
> 100 nF 0402 rather than a 0 Ω link.
>
> So the Gerber path is **assisted, not automatic**: you click the power pour and the reference pour to
> name them, and you supply a placement file (pick-and-place / centroid, which every fabricator already
> asks for) plus the BOM to attach parts to pads. That is two extra files you already have. The tool
> will say plainly which of the two paths it is on and what it therefore does not know. It will not
> guess a net name and it will not guess a capacitor value.

### The stackup — mandatory

Layer order, copper thicknesses, dielectric thicknesses, ε<sub>r</sub> and tan δ per dielectric. This
is circuitRF's existing technology model (`.ctech`) — the same one the EM solver reads, edited in the
same place. `.kicad_pcb` carries enough to pre-fill it.

The two numbers that matter most and are most often wrong: **the power-to-reference dielectric
thickness** (it sets the plane capacitance linearly and the spreading inductance linearly) and
**tan δ** (it sets how sharp the cavity resonances are, which is the difference between a 6 dB bump and
a 20 dB one).

### The rail — mandatory

Which conductor is the power net, which is its reference return, and **where the load is**. The load
port is the IC's power/ground pin field — a set of pads, not a point. railRF ties them into one port,
because that is what the die sees.

Optionally, more than one observation port: a second IC on the same rail, or a point you intend to
probe.

### The parts

Each decoupling capacitor is one of:

- **A Touchstone file** — the vendor's own measured `.sNp`. railRF reads its impedance through the
  shunt-through relation and uses it directly, which means you are simulating with the part's real
  ESR and real self-resonance rather than a datasheet triple. This is the recommended path and it is
  already built: see [Derived Metrics](../user/reference/derived-metrics.html#fixture).
- **An R-L-C triple** — ESR, ESL, C. Fine for a first pass.
- **A vendor SPICE/equivalent-circuit model** placed as an ordinary circuitRF subcircuit.

Plus, per part, the **mounting inductance**: the loop from the pad through its via to the plane pair
and back. railRF computes this from the actual via positions and the plane separation when it has the
artwork, which is the point — it is typically 0.3–1.5 nH, it dominates above roughly 50 MHz, and **it
is the thing your form factor change actually altered.** You can override it.

### The target

A flat `Z_target` in milliohms, or a piecewise mask (a table of frequency/limit points), or a
transient spec (`ΔI`, `ΔV`, rise time) from which railRF derives the flat target and the top of the
band that matters. Masks are per observation port.

### The band

Start, stop, points. The default runs from 1 kHz to the first plane resonance plus a decade, log
spaced, adaptively refined around the resonances — a flat grid either misses an anti-resonance or
wastes ten thousand points.

## 2.3 What you do

railRF opens as a document with its own window, like the Match Designer and harmonicaRF. Left to
right, the workflow is:

**1 — Load the board.** The layout appears. Copper is drawn as copper; you are looking at the real
artwork, not a sketch.

**2 — Identify the rail.** With a board file, pick the power net from a list. With Gerbers, click the
pour. railRF highlights everything galvanically connected to your pick — through vias, across layers —
so you immediately see whether the rail is one region or three islands joined by a 20 mil neck. *That
alone has caught real problems.*

**3 — Confirm the parts.** A table: refdes, value, model, position, computed mounting inductance.
Anything railRF could not resolve is listed as unresolved rather than defaulted.

**4 — Place the load port.** Click the IC's pin field, or accept the footprint if the board file named
one.

**5 — Set the target.** Type a number, or load a mask.

**6 — Run.** Seconds to a minute, depending on mesh and band. The result is live: change a capacitor
value, move a part, delete one, and the curve updates.

## 2.4 What you get

**The impedance plot.** |Z(f)| at each observation port, log-log, with the target mask drawn as a
shaded ceiling. Violations are marked with their frequency and their margin in dB. This is the
headline and it is the plot you screenshot into a design review.

Beside it, decomposed contributions on the same axes — the VRM, each capacitor bank, the plane pair
itself — so a violation is attributable rather than merely visible. An anti-resonance at 60 MHz is
labelled with the two things resonating against each other: *"L(mount, C3–C9 bank) against C(bulk
bank)"*. That label is the actionable output; the peak on its own is not.

**The anti-resonance table.** Frequency, peak |Z|, margin against the mask, and the two contributors.
Sorted by how badly they violate.

**The capacitor ranking.** One row per part: how much the worst violation grows if this part is
removed, in dB. Parts with a zero in that column are candidates for deletion, and the tool says so
without you having to build the board and desolder them. The ranking is computed by actually removing
each part and re-solving, not by a sensitivity approximation — the solve is cheap enough and the
approximation is not trustworthy near an anti-resonance.

**The plane resonances.** The cavity modes of your actual shape, listed by frequency, each with a
field map showing where the voltage maxima sit. This is the *"when do these shapes resonate and
misbehave"* answer. A mode whose maximum sits on top of your load pin field is a problem; the same
mode with its maximum in a corner is not, and only the map distinguishes them.

**The impedance map.** Pick a frequency, get |Z| across the whole plane as a colour map — the transfer
impedance from every point to your load port. Blue where the copper is doing its job, hot where it is
not. This is the picture that shows a designer *why* moving the regulator 30 mm cost them 8 dB.

**Everything exports.** Z(f) as Touchstone or `.npy`, the tables as CSV, the maps as vector graphics.
The impedance curves land in an ordinary circuitRF Data Display, so they can be overlaid on anything
else — including a measurement.

## 2.5 The A/B comparison

This is Q3 and it is the workflow that motivated the whole tool.

Open the reference design and the target design side by side. railRF matches the two by **net name**
where it can (both from board files, or both with pours you have named), and by **refdes** for the
parts. Where it cannot match something, it says so and asks — it does not pair things by proximity or
by guessing.

Then:

- **Both impedance curves on one plot**, with the target mask. The reference passes; does yours?
- **A delta trace** — Δ|Z| in dB versus frequency — with the frequencies where it moved most called
  out. Below about 10 MHz you will usually see nothing, because down there it is the capacitors and
  you kept them. What moves is the region above 50 MHz, where the mounting and spreading inductance
  live, and that region is exactly what a form-factor change alters.
- **A per-part comparison table**: the same capacitor's mounting inductance on both boards. This is
  usually where the answer is. A part that was 0.4 nH on the reference and is 1.1 nH on yours because
  its return via moved 4 mm is a finding you can act on in an afternoon.
- **Both mode lists.** A reshaped board has different cavity modes. If one of yours has moved onto a
  frequency your load actually excites, that is the headline.

The output is a short report, not just a plot: *these three parts got worse mounting, this mode moved
from 810 MHz to 640 MHz, and the net effect is a 6 dB mask violation at 71 MHz that the reference did
not have.*

## 2.6 Worked example, end to end

> A supplier's reference design for a 1.8 V rail: 100 × 80 mm, four layers, plane pair on L2/L3,
> 22 decaps, a 470 µF bulk, target 12 mΩ to 200 MHz. You have re-laid it as 140 × 45 mm to fit a
> chassis, same schematic, same BOM.
>
> 1. Load the reference `.kicad_pcb`, pick net `+1V8`, pick `U1` as the load, type `12m`. Run: **passes,
>    worst margin 3.1 dB at 84 MHz.**
> 2. Load yours. Same picks. Run: **fails, +5.4 dB over at 71 MHz.**
> 3. Open the comparison. The delta trace is flat below 20 MHz and diverges above it. The per-part
>    table shows C7, C8 and C11 — the three nearest the load on the reference — now sit 11 mm away
>    with 1.3 nH of mounting inductance instead of 0.45 nH. The anti-resonance table names the
>    violation: *L(mount, C7–C11) against C(bulk)*.
> 4. Move three parts in your layout tool, re-import, re-run. **Passes, 1.9 dB margin.**
> 5. Open the capacitor ranking on the passing design. Six parts show 0.0 dB — they are shadowed by
>    lower-inductance neighbours. Delete them: **still passes at 1.8 dB.** Six parts and six placements
>    saved, on a board that does not exist yet.

Steps 1–4 are the form-factor question. Step 5 is the cost question, and today it happens after
tooling, with a soldering iron.

## 2.7 What railRF will not do

Stated plainly, because a tool that is vague about its boundary gets trusted past it.

- **It is not a full-wave solver and does not become one.** It models the parallel-plate mode between
  a power/reference plane pair. That is the dominant physics of a PDN below the first few cavity modes
  and through them; it is not a radiated-emissions tool and it is not a signal-integrity tool.
- **It stops at the package.** The answer is the impedance at the board-side pads. On-die capacitance
  and package inductance sit between that and the transistor and are typically the dominant terms
  above a few hundred megahertz. If your silicon vendor supplies a package model you can cascade it;
  railRF will not invent one.
- **It does not do transient.** The output is Z(f). Converting that into a voltage waveform for a
  given current profile is a defensible v2 feature and is deliberately not v1 — doing it properly
  needs the causality of every element, which is exactly what `circuitrf check` was built to tell you
  about.
- **It does not model a split plane as if it were solid**, and it does not silently bridge a split. If
  your rail is two islands with a stitch, you will see two islands with a stitch.
- **It does not know your capacitors are derated.** *See §9 — this is the largest accuracy risk in the
  whole tool and it is not a solver problem.*
- **It does not route, place or optimise your board.** It measures.

---

# 3. Why this is not the existing EM solver

circuitRF has a planar method-of-moments engine with layered Green's functions, and the instinct is to
point it at the plane pair. **That is the wrong tool and it is worth being explicit about why**, since
this decision shapes the whole implementation.

A MoM solve builds a dense N × N system over current basis functions on the metal. Our engine refuses
above a few thousand unknowns by design and costs tens of seconds per frequency point at that size. A
100 × 80 mm plane pair meshed finely enough to resolve its own modes is tens of thousands of cells,
and a PDN sweep wants several hundred frequency points. That is not a tuning problem; it is three
orders of magnitude.

The parallel-plate model is cheap for a structural reason: between two closely-spaced planes
(`h ≪ λ`), the field is essentially TM<sub>z</sub> with no z variation, and the problem collapses to a
two-dimensional one. It is a **sparse** system whose size is the cell count, solved by exactly the
sparse complex LU circuitRF already carries for MNA. That is the difference between a minute and a
day, and nothing is given up that a PDN analysis needed.

**So the deliverable of the extraction step is a NETLIST, not an `EmProblem`.** That distinction is
the architectural heart of this proposal and §5 turns on it.

---

# 4. The physical model

## 4.1 The plane pair as a mesh of unit cells

Divide the overlap region of the two planes into cells of side Δ. Each cell contributes:

```
Shunt capacitance to the reference plane   C = ε₀·εᵣ·Δ² / h
Dielectric loss                            G = ω·C·tan δ
Series inductance along each cell edge     L = µ₀·h                    (square cells)
Series resistance along each cell edge     R = 2·Rs                    (both planes)
    with the skin-effect sheet resistance  Rs = √(π·f·µ / σ)
```

`h` is the dielectric separation, `σ` the copper conductivity. The factor of two on R is the two
planes in series in the loop. For non-square cells L and R scale by the aspect ratio
(along/across). This is the standard plane-pair unit-cell model and its accuracy against measurement
is well documented in the SI literature.

**Cells are square-ish and sized by the shortest wavelength in the dielectric**, Δ ≤ λ<sub>min</sub>/20.
On FR-4 (ε<sub>r</sub> ≈ 4.3) that is 7.2 mm at 1 GHz and 1.4 mm at 5 GHz. A 100 × 80 mm board at
1.4 mm is about 4,000 cells — trivially sparse.

The mesh follows the copper. A cell is present where **both** planes have copper; a cutout, an antipad
field, a split or a board edge simply removes cells. That is what makes arbitrary shapes work, and it
is why *"the actual shapes used"* is not a stretch goal here — it falls out of the meshing.

## 4.2 What attaches to the mesh

- **Each capacitor** at the cell under its pads: its own model (Touchstone, R-L-C, or subcircuit) in
  series with its **mounting inductance**, connecting the power node to the reference node.
- **The mounting loop** is computed from the actual via geometry: the partial self-inductance of the
  power via and the return via, minus twice their partial mutual inductance, plus the pad-to-via trace.
  The dominant term is the via pair's separation and the plane separation `h`, which is precisely the
  quantity that changes when a part moves.
- **The VRM** at its own cells: a series R-L to the reference plus, optionally, a control-loop output
  impedance the user supplies as a Touchstone file.
- **The load port** across the power and reference nodes of the IC's pin-field cells, tied together.

## 4.3 The solve

Assemble one sparse complex MNA system per frequency, solve for the port impedances via CSparse's LU.
This is the same numerical layer every other circuitRF analysis uses; nothing new is introduced. The
result is a `DataSet` carrying a Z cube over `[freq, port, port]` plus, when asked, the full node
voltage field for the impedance maps.

**Adaptive frequency sampling** is not optional here — plane resonances are narrow and a log grid will
step straight over one. The existing adaptive sweep from the EM engine is the right mechanism.

## 4.4 Modes

The cavity modes come out of the same discretisation as a generalised eigenproblem on the loss-free
system, so the mode list and the sweep cannot disagree about the structure. For a rectangle they
reduce to the textbook `f_mn = (c / 2√εᵣ)·√((m/a)² + (n/b)²)`, which is the acceptance check.

---

# 5. Architecture

Where each piece lives, and why. The rule is `docs/design/ui-architecture.md`: nothing below the UI
may reference a UI framework.

| Piece | Home | Why |
|---|---|---|
| Board/Gerber import, stackup | `src/Design` (existing) | Already there. railRF adds no importer. |
| Plane-pair extraction → netlist | `src/Design/Layout/Pdn/` | It is a document-to-model transform, like the EM extractors beside it. Produces an elaborated netlist. |
| Mode eigensolve, field maps | `src/Engine/Pdn/` | Numerics. No domain types. |
| Target masks, ranking, A/B diff | `src/Engine/Pdn/` | All arithmetic over a `DataSet`. |
| The solve | `src/Engine` (existing MNA) | Nothing new. |
| The editor, maps, plots | `src/Ui/RailRf/` | The only piece that draws. |
| `rail` CLI verb | `src/Cli` | Headless, per `docs/design/cli.md`. |

**The extraction produces an elaborated netlist and that is the whole trick.** It means the sweep,
the parametric machinery, the measurement engine, the `DataSet`/`DataCube` result model, the Data
Display and every export path are reused rather than rebuilt — and it means a PDN model can be
*inspected* as a netlist, which is the difference between a tool you trust and a black box.

Consequences worth stating: railRF runs headless, so a board can be gated in CI; and because the
result is a `DataSet`, a PDN curve overlays a measurement in an ordinary Data Display with no special
support.

---

# 6. Phasing

Three phases, each shippable and independently useful.

**P1 — Lumped PDN.** Target mask, part models from Touchstone or R-L-C, VRM, user-entered mounting
inductance, Z(f) at one port, anti-resonance table, capacitor ranking. **No artwork and no EM.** This
is a schematic-shaped problem and it is worth shipping alone: it already beats a spreadsheet, and it
answers Q1 and Q2 for anyone willing to type their mounting inductances. It also builds the mask, the
ranking and the plot layer that P2 needs.

**P2 — The plane.** Extraction from imported copper, the sparse solve, modes, field maps, computed
mounting inductance. This is the substantial phase and the one that makes railRF unlike anything else
on the desk.

**P3 — A/B.** Two designs, net and refdes matching, the delta trace, the per-part table, the report.
Cheap once P2 exists.

An honest note on order: **P1 has real standalone value and P3 has almost none without P2.** If only
one phase is ever built, it should be P1.

---

# 7. Acceptance

Each phase is gated against something that is not our own arithmetic.

- **The rectangular cavity.** A uniform rectangular plane pair has closed-form modes and a closed-form
  input impedance. The mesh must reproduce the first six modes to better than 2 %, and the
  convergence with cell size must be monotone.
- **The lumped limit.** Well below the first mode, a plane pair is a parallel-plate capacitor. The
  extracted Z must approach `1/(jωC)` with `C = ε₀εᵣA/h` to under 1 %.
- **A published measured PDN.** At least one board from the open SI literature with measured Z(f),
  reproduced within the tolerance that literature states. This is the acceptance anchor, and it is
  external data in the sense the PRD requires.
- **Mounting inductance** against a closed-form partial-inductance calculation for a via pair.
- **The A/B report** against two synthetic boards differing in exactly one known way, where the
  correct answer is constructed rather than solved for.

---

# 8. Open questions for review

Numbered, because these are what the reviewer is being asked to decide.

**Q-1 — Is the Gerber path worth building, given §2.2?** Gerbers carry no nets and no parts, so that
path needs the user to identify pours and to supply placement + BOM. The board-file path needs none of
that. Is assisted-Gerber a day-one requirement or a v2 feature? *This is the question with the biggest
effect on scope.*

**Q-2 — Where does the load port come from?** For a supplier reference design you may have the
schematic, in which case the IC's pin field is known. Without it, is clicking the pin field acceptable?

**Q-3 — How far up?** The plane model is good through the cavity modes. Above roughly 2–3 GHz the
package dominates anyway and the board answer is academic. Is stopping at "first mode + a decade"
right, or is there a reason to go higher?

**Q-4 — Is the capacitor ranking the right shape?** It currently answers *"what does removing this
part cost me"*. The alternatives are *"what is the cheapest set that passes"* (a combinatorial search)
and *"what is the best value to put here"* (a substitution sweep). All three are useful; which is worth
building first?

**Q-5 — VRM.** Is a series R-L adequate for v1, or does the tool need a real control-loop output
impedance from the start? Below the bulk capacitance the VRM is the whole answer, and a wrong one
makes the sub-100 kHz region fiction.

**Q-6 — Multiple rails at once?** A 1.8 V and a 1.0 V plane sharing one reference are coupled through
it. v1 assumes one rail at a time. Is that acceptable?

---

# 9. Risks

**The largest one is not the solver — it is capacitance derating.** An MLCC's capacitance falls with
applied DC bias, and for a small-case high-value part it falls a very long way: a 10 µF 0402 X5R can
be under 2 µF at its rated voltage, before temperature. A PDN answer computed with the marked value
instead of the biased one is wrong by a factor of several in exactly the band the bulk capacitors own,
and it is wrong *optimistically*. Nothing about the plot will look suspicious.

So: railRF must take capacitance as a function of bias where the vendor supplies one, must show the
derated value it actually used next to the marked value in the parts table, and must warn when it has
been given a marked value with no derating data. A vendor Touchstone file measured at bias already
carries this correctly, which is another reason to prefer that path — and another reason the passive
readouts shipped first.

**Second: the stackup is usually wrong.** Designers copy a stackup from the last board. The
plane-to-plane dielectric thickness sets the plane capacitance linearly, and a 2× error there is a 2×
error in the answer at every frequency below the first mode. railRF should show the extracted plane
capacitance as a single number early and prominently, because a designer will recognise a wrong one
instantly and would never notice it buried in a curve.

**Third: the mesh at antipad fields.** A BGA's antipad array removes a large fraction of the copper in
a small region and it is precisely under the load port. Too coarse a mesh there and the spreading
inductance is underestimated — again optimistically. Local refinement under port regions is not an
optimisation; it is a correctness requirement.

**Fourth: scope creep toward signal integrity.** Every one of these primitives — a plane pair, a mesh,
a port — is one step from a return-path-discontinuity tool. That is a different product. §2.7 is the
boundary and it should be defended.

---

# 10. What this reuses

Worth listing, because it is the argument that this is a large feature rather than a new product:

- Gerber, Excellon, DXF and `.kicad_pcb` import — **built**
- The technology/stackup model and its editor — **built**
- Net extraction and connectivity — **built**
- Sparse complex MNA with CSparse — **built**
- Adaptive frequency sweeps — **built**
- The `DataSet`/`DataCube` result model, `.npy`/MATLAB/Touchstone export — **built**
- The Data Display: plots, masks as traces, markers, overlays, tear-off windows — **built**
- Passive-part readouts, so a vendor `.sNp` becomes a PDN element — **built 2026-09-06**
- Touchstone health checking, so a vendor file is trusted for a reason — **built 2026-09-06**
- A tool-as-document shell with its own menus and a standalone binary — **built twice**, for the
  Match Designer and harmonicaRF

What is genuinely new is the plane-pair extractor, the mode solve, the mask/ranking arithmetic and the
comparison report. That is the honest measure of the work.
