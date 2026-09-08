# Sonnet Brief — WSP-4: the WSProbe symbol, its placement, and the derived metrics in the Data Display

**Read `brief-wsprobe-0-overview.md` first**; depends on WSP-1 (the cube and the accessors) and on
WSP-2/WSP-3 (the functions this brief only *surfaces*). Reference: T. A. Winslow, *General Circuit
Analysis Using The WSProbe* (2023), Fig. 13 (the default outputs), Fig. 14 (the Kurokawa signature on
a polar chart), Fig. 15 (the symbol and G/L orientation), Fig. 33/36 (polar plots of `1/H0`, `1/Y0`),
Fig. 37 (NDF encirclements).

**Scope.** Everything in `src/Ui` and `src/Render`: the palette tile and symbol, series placement,
netlist extraction, the trace card's WSProbe section, plot-type gating, readouts and markers, `.cdd`
persistence, and the `plot` verb's spelling for probe traces. **No numerics live here**: every value
the Data Display shows is a call into `src/RfCore/Stability/` (overview D-2/D-6), and the gate is
bit-identity with the library.

---

## 1. Symbol, palette, placement, extraction

**R-wsp4-1. `SymbolKind.WSProbe`.** Follow `docs/skills/adding-a-library-component.md`'s device
recipe with `SymbolKind.IProbe` as the analog (grep it; every hit is a touchpoint — the earlier count
was nine files across `src/Design/Schematic`, `src/Ui/Schematic`, `src/Ui/ViewModels`,
`src/Ui/Diagnostics`, `src/Ui/Harmonica`). Registry entry: `DisplayName "WSProbe"`, `InstancePrefix
"WSP"`, `Category Terminals`, `SearchTerms ["WSProbe", "Winslow", "stability", "probe", "H0", "Y0",
"loop gain", "driving point"]`, `IsCommon true`. `EngineReference` returns `"WSProbe"`; extraction
emits `WSProbe:<name> <netG> <netL>` — the **G pin first**.

**R-wsp4-2. The glyph** (`BuiltInSymbols.BuildWSProbe`, beside `BuildIProbe`): a small square in the
wire with the letters `G` and `L` inside at the two pin ends and a two-headed arrow above it (Fig.
15's "cool symbol", redrawn — not copied). The letters are the orientation; a flipped placement
swaps which wire end is G, and the extraction must follow the pins, not the drawing. Terminal note
(the `IProbe` one's pattern): *"G is the generator-side terminal, L the load-side. ZG looks out of G,
ZL out of L; swapping them swaps every G/L-labelled output and negates nothing else."*

**R-wsp4-3. Placement cuts the wire, like an IProbe.** `SeriesProbeInsertion.FindShortedSpan` is
written for `SymbolKind.IProbe` by name; generalise the kind check to "any series probe" so a
WSProbe dropped onto a wire breaks into it under the same rule (single straight segment, nothing
between the pins) and otherwise leaves the wire alone. `check`'s `wsprobe.shorted` (WSP-1) is the
headless twin.

**R-wsp4-4. Docs figure.** `SymbolArtworkGenerator` picks the new kind up automatically; confirm the
light/dark pair appears under `docs/user/assets/symbols/` (WSP-7 references it).

---

## 2. The trace card: a WSProbe section

**R-wsp4-5. When it appears.** The Plot Inspector's item selector already lists a source group's
cubes and, beneath them, the derived metrics (`DerivedParameters`). When the selected group carries a
`wsp` cube and a `__WspProbes` cube, add a **WSProbe** section with:

- a **probe** picker (labels from `__WspProbes`, in `idx` order, showing `idx` beside each);
- a **metric** list, grouped, each with its plot-type gating (disabled-with-reason on the wrong plot
  type, exactly as the derived metrics already do):

| Group | Items (document name → description) | Plots on |
|---|---|---|
| Driving point | `H0` (driving-point impedance), `Y0` (driving-point admittance), `1/H0`, `1/Y0` | Polar, Rect (Re/Im/dB), Smith for `H0`/`Y0` |
| Bidirectional | `ZG`, `ZL` (impedances, series stimulus), `YG`, `YL` (admittances, shunt stimulus), `Zop`, `Yop` (open-port) | Smith, Polar, Rect |
| Loop gain | `LG` (bilateral, Tian), `F = 1 − LG`, `LGF` (forward circulator), `LGR` (reverse circulator), `LG_H`, `LG_MF`, `LG_MR`, `LG_MGF`, `LG_MGR` | Polar, Rect (dB/phase) |
| Match | nodal Γ (`wsp_nodal_gamma`), its dB | Smith, Polar, Rect |
| Normalised | `nZ`, `nY` (*circuitRF normalized driving-point locus — not the published margin*), `Stability margin (Winslow 2024)` **disabled** with the D-12 reason | Polar, Rect |
| Reduced two-port | `[Y]`, `[Z]` at the probe as a **network source** (see R-wsp4-8) | — |

The circulator kinds carry a `Z0` field (default: the source group's port-1 `Re(Z0)`, else 50 Ω).

**R-wsp4-6. Probe pairs and sets.** A second picker ("with probe") enables the pair group:
`wsp_block_calc` outputs 9–16 as scalar-vs-frequency items, and the inner/feedback blocks (`{1..4}`,
`{5..8}`, and their in-situ renormalised forms `wsp_block_design`/`wsp_fb_design`) as **network
sources**. A multi-select ("probe set", ordered) enables Ohtomo (`G_i` per node, with the `active`
side control) and `wsp_ymatrix`. These are the document's own workflows (p. 87, 94, 110) and need no
new plumbing beyond the pickers.

**R-wsp4-7. Kurokawa readouts and markers.** For a `1/H0` or `1/Y0` trace (and `nZ`/`nY`), the card
shows **"Kurokawa start-up: f₁, f₂, … / none"** from `wsp_unstable_freq_kurokawa`, and offers **"Mark
crossings"**, which drops one marker per reported frequency on the trace (the marker system already
supports placing at a frequency). For a loop-gain trace the same readout uses
`wsp_unstable_freq_loopgain` (critical point `+1`). For any polar trace, an **encirclements** readout
(`enc` at the last frequency, rounded, and the running count on hover) — the reading NDF needs (§8) and
`F` invites (p. 34: "plotted on a polar plot and the designer can look for origin encirculations").
Polar plots of these traces draw the origin **and the `+1` point** as reference marks, since the two
families have different critical points.

**R-wsp4-8. The reduced two-port as a network source.** The `DataSourceView` mechanism that adds
virtual `Z`/`Y` cubes to a group is the precedent: expose `[Y]` (Eq. 44) of a chosen probe, and the
pair blocks of R-wsp4-6, as a *virtual network group* (`S` at the group's reference plus `Z0`), so the
existing μ, μ′, K, |Δ|, MAG/MSG and the stability circles apply to it unchanged (WSP-2 §3). Name it in
the source tree as `<analysis> ▸ WSProbe <label> ▸ reduced 2-port`. This is the cheapest new
capability in the series and it is the document's "two-port network reduction" (§4.2) made visible.

**R-wsp4-9. The envelope card.** With a source probe, a load probe and a suspect probe chosen, an
**Envelope** sub-card takes `|ΓS|`, `|ΓL|` (each with an "off" state = 0), `θ` step, and `Z0`, and
adds two families of traces per θ (`1/H0'`, `1/Y0'`) on a polar plot plus a **stability map**
(`wsp_loadpull_unstable`) drawn as a θS × θL grid coloured by the unstable count. It calls
`wsp_loadpull`/`wsp_loadpull_unstable` and nothing else; the refusal
`wsprobe.envelope-probe-not-at-termination` shows on the card with its text.

**R-wsp4-10. Sweeps.** A swept `wsp` (`[Pin, freq, row, col]`, or `[Pin, ssfreq, …]` under WSP-5)
uses the existing family/slider mechanism: the probe metric is computed per slice, and the family
marker `~` on the sweep axis draws one curve per sweep point — which is how a drive-dependent
`1/H0(ssfreq)` fan (WSP-5's whole point) is read. `wsp_unstable_freq_kurokawa`'s readout follows the
slider position (App. E.12's own instruction).

**R-wsp4-11. Persistence and identity.** `TraceConfig` gains the probe label(s), the metric enum and
its options; `.cdd` round-trips them (append-only ordinals, as `DerivedParameters` warns). The trace
path is built by one function that calls the library; **the gate is that a trace's samples equal the
library function's cube for the same inputs, bit for bit** (the same rule `NetworkMetrics` is under).

**R-wsp4-12. `plot` verb.** Extend the trace shorthand: `--trace cube=wsp,probe=GATE,metric=invH0,y=polar`
and `--trace cube=wsp,probe=GATE,with=DRAIN,metric=LGM`. The document the verb builds is the same
`.cdd` the GUI writes, so the byte-identity gate of the `plot` verb (`MissingVerbsCliTests`) extends
to probe traces with no new plotting path.

**R-wsp4-13. Messages.** The run diagnostics WSP-1/2/3 emit (`wsprobe.degenerate-node`,
`wsprobe.margin-not-transcribed`, `wsprobe.envelope-probe-not-at-termination`, `wsprobe.shorted`)
reach the Messages pane through the existing warnings channel; nothing new to build, but assert each
one lands.

---

## 3. Gates

`tests/Ui.Tests/DataDisplay/WSProbeTraceTests.cs`, `tests/Ui.Tests/Schematic/WSProbePlacementTests.cs`,
`tests/Ui.Tests/Cli/...` for the verb.

**R-wsp4-14(a)** — every WSProbe trace's samples are bit-identical to the library call on the run's
cubes (loop over all metrics, both plot types, a swept and an unswept source).
**(b)** — the reduced two-port network source yields μ/K/circles identical to running
`NetworkMetrics` on `wsp_yparam` converted to S (the same code path, but the test proves the virtual
group is wired to it).
**(c)** — `.cdd` round trip of every new field; an old `.cdd` without them loads unchanged.
**(d)** — placement onto a straight wire cuts it; onto a junction does not; extraction order G then L
after each of the four rotations and both flips (the *drawing* rotates, the pin order does not).
**(e)** — `plot --trace cube=wsp,…` output is byte-identical to the GUI's `.cdd` rendered through
`render --data`.
**(f)** — the Kurokawa readout on WSP-1's series resonator run reads `1.5915 GHz` on `1/Y0` and
`none` on `1/H0`; "Mark crossings" places exactly one marker at that frequency.
**(g)** — the disabled margin item carries the D-12 reason text; a rect plot lists the polar-only
items disabled with a reason, never absent.
**(h)** — the docs factory renders a symbol pair and the trace-card fixture with the WSProbe section
open (`FigureCatalog` entry `plot-inspector-wsprobe`), for WSP-7.

## On completion

Findings to `src/Ui/RESOLVED.md` and `src/Render/RESOLVED.md`; **never a `CLAUDE.md`**. Update
`docs/design/trace-card.md` and `docs/design/data-display.md` (the WSProbe section, the virtual
reduced-two-port group), and `docs/design/cli.md` for the `plot` shorthand.
