# The WSProbe — bidirectional impedances and the `wsp` matrix

Design note for the WSProbe series (`docs/sonnet-briefs/brief-wsprobe-0-overview.md` and its seven
implementation briefs). **WSP-1 created this note with §1–§4 (the probe, the `wsp` matrix, the
DataSet layout, the typo-register entries the engine half depends on).** Later briefs append their
sections: WSP-2 the single-probe derived metrics, WSP-3 probe pairs and the envelope, WSP-5
harmonic balance, WSP-6 NDF.

**Reference, cited by equation throughout:** T. A. Winslow, *General Circuit Analysis Using The
WSProbe*, January 29, 2023 (public technical report). Notation is the document's, verbatim — `wsp`,
`H0`, `Y0`, `ZG`, `ZL`, `YG`, `YL`, `LG`, `F`, `idx`, the Appendix E.13 letters `A B C D`. Nothing is
renamed to something more circuitRF-flavoured; a designer who has read the document must find every
quantity circuitRF reports under the name the document gave it.

---

## 1. What the probe is

A `WSProbe` is a **two-terminal series element with an orientation** — terminals `G` (generator
side) and `L` (load side), Fig. 15/16 — placed *in* a node so that it splits the node into a G-side
and an L-side terminal, exactly as an `IProbe` does. Electrically it is a **0 V short**: it perturbs
nothing (§4, p. 33, "completely nonperturbative … requiring only a single analysis sweep"). In the
code it is `WSProbeModel`, which shares its whole stamp with `IProbeModel` through
`SeriesProbeModelBase`: one branch unknown, the constraint `V(nG) − V(nL) = 0`, and the KCL coupling
with the branch current flowing G → L. Every engine site that used to match an `IProbeModel` — the
DC packer's `I:<label>` cubes, the harmonic-balance `I` cube and `__ProbeBranches`, the SDD
control-current resolvers — matches the base, so a WSProbe reports `I:<label>` in DC and HB exactly
as an IProbe does (gate R-wsp1-14(m)). A probe that is not transparent to the analyses it does not
serve is a probe that perturbs.

Netlist spelling: `WSProbe:<label> nG nL` — first net G, second net L, the same first/second rule
`IProbe np nm` uses. No parameters in v1 (a `Z0` for the synthetic circulator of Eq. 96–99 is a
function argument, not a probe property). The instance name is the document's "Label"; a probe in a
sub-cell is `X1.GATE` (§4.3, "at any depth of schematic hierarchy").

**`idx`** (Eq. 50–52) is assigned at elaboration, in **flattened netlist order, 1-based**, and is a
property of `ElaboratedNetlist` (`WspProbes`, a list of `(ComponentIndex, Label, Idx)`) rather than
of the model, because it depends on the other probes. It is reported in the run's `__WspProbes` cube
and in `explain --analysis`, so it is never guessed.

Its value is not the element but the matrix the analysis produces from it.

## 2. The `wsp` matrix

For each probe `i` the engine applies, mathematically, two vanishingly small auxiliary generators
(Fig. 16): a **series voltage** `vS` in the probe branch (− at G, + at L, so `v_L − v_G = vS`) and a
**shunt current** `iP` injected into the G-side node; and reads at *every* probe `j` two responses:
the branch current `iS` (flowing G → L through the probe) and the G-side node voltage `vP`.
Normalised, these are the four transfer functions per probe pair (Eq. 33), stored **1-based, rows =
stimulus probe, columns = response probe** (Eq. 32–34):

```
wsp(2i−1, 2j−1) = iS_j / vS_i     series current  response to series voltage   (Y0 when i = j, Eq. 52/133)
wsp(2i−1, 2j  ) = vP_j / vS_i     shunt  voltage  response to series voltage   (Eq. 135)
wsp(2i  , 2j−1) = iS_j / iP_i     series current  response to shunt current    (Eq. 136)
wsp(2i  , 2j  ) = vP_j / iP_i     shunt  voltage  response to shunt current    (H0 when i = j, Eq. 50/131)
```

With `N` probes this is `2N × 2N`. Everything else in the document — the reduced two-port at the
probe (Eq. 44/48), the bidirectional impedances (Eq. 65–68), every loop gain, the synthetic
circulator, probe-pair blocks, bifurcation, Ohtomo, the NDF reductions, the envelope — is
**post-processing of `wsp`**. That is the architecture: one engine addition (compute `wsp`), one
library of pure functions over it (`src/RfCore/Stability/`), and their surfacing in measurements,
the Data Display and the CLI.

### 2.1 How the engine computes it (`SParameterEngine.SolveProbes`)

Inside the per-frequency loop of **both** the wave path and the legacy path, after that frequency's
S-parameter solves and **against the same factorisation**: with `N` probes the extra work is `2N`
back-substitutions and no factorisation, and the extra solves stamp nothing, so the `MnaSystem`
pattern cache is untouched. The mapping of the document's conventions onto circuitRF's (overview
§3), fixed here once because a sign flip in any of them produces plausible wrong answers:

| Document (Fig. 16) | circuitRF |
|---|---|
| Probe terminals `G` then `L` | `WSProbe:<label> nG nL` — first net is G, second is L |
| `iS` flows G → L through the probe | Engine branch-current convention: *first node → second node*. `iS` **is** the branch current unknown, unchanged in sign |
| `vS` has − at G, + at L: `v_L − v_G = vS` | The 0 V constraint row is `V(nG) − V(nL) = value`; the series injection is `b[br] = −1` for unit `vS` |
| `iP` injected **into** the G-side node | `b[nG − 1] = +1` — the engine's current-source convention already injects into its first node |
| `vP` = voltage of the G-side node to ground | `x[nG − 1]` of the solve; a grounded G node reads 0 |
| `i1` into port 1 of the reduced two-port (G side): `i1 = iP − iS`; `i2 = iS` | Consequences the reduction (Eq. 38, 45) already encodes; nothing to stamp |

The branch index is read off the model *after* the stamp at each frequency, because the wave and
legacy assemblies number branches differently (the wave path skips port branches) and a precomputed
index would be the wrong one on one of them.

**The series-source sign is the one place a wrong guess survives every symmetric test.** With `−1`, a
zero-feedback cascade gives `vP/vS = −ZG/(ZG + ZL)` (Eq. 67, 69); with `+1` it gives the negative and
`ZG` comes out negated. `WSProbeTests.A_ZeroFeedbackCascade_…` holds it to 1e-12.

**The network the probes see is the terminated one** (§4.2: "All external reference terminations
(Zo) and any DC supplies and all bypassing are contained within the reduced Y matrix"). On the wave
path that is exactly the assembly already on the stack — every `Port`/`Term` stamps its `1/Z0`,
independent sources are off, nonlinear devices are linearised at the DC operating point. On the
legacy path (some port with `Re(Z0) ≤ 0`) a port is a 0 V driven branch, which is *not* a
termination; the probe solves there use a second assembly on its own `MnaSystem` — the same stamp
sequence (so every model's branch index and every SDD's resolved control branch are the ones that
assembly has too) plus one diagonal entry `−Z0` per port branch, which turns `V(n0) − V(n1) = 0` into
`V(n0) − V(n1) − Z0·I = 0`: the port terminated in its own `Z0` with no drive. A port with `Z0 = 0`
exactly cannot be terminated and is a refusal naming the port (`wsprobe.port-short`), never a
large-conductance stand-in.

**Port-less analyses are allowed when a probe is present.** The document's own fixtures (Fig. 31,
34) have no ports. The run emits the `wsp` cubes with no `S` and no `Z0` cube; nothing downstream
assumes `S` exists (`sparam` refuses a Touchstone of such a run by name, and says which spellings
carry the result).

The regularisation retry applies to the probe solves too — they use the retried factorisation, and
the `sparam-regularization` warning is emitted once as today. A probe whose G node is floating
produces a singular matrix and the existing zero-row diagnostic names the node.

### 2.2 The six default outputs (`WspReduction`, `src/RfCore/Stability/WspReduction.cs`)

Writing `A = vP/vS = wsp(2i−1, 2i)`, `B = iS/vS = wsp(2i−1, 2i−1) = Y0`, `C = vP/iP = wsp(2i, 2i) =
H0`, `D = iS/iP = wsp(2i, 2i−1)` (the Appendix E.13 letters):

```
|P| = B·C − A·D                                                 (Eq. 42)
α = |P|,  β = |P| + A,  γ = |P| − D,  δ = |P| + A − D + 1        (Eq. 43)
[Y] = (1/H0)·[[ δ, −β ], [ −γ, α ]]                             (Eq. 44)   — never Eq. 40 as printed (T-1)
[Z] = (1/Y0)·[[ α,  β ], [  γ, δ ]]                             (Eq. 48)   — never Eq. 47 as printed (T-2)
H0 = C  (Eq. 50)      Y0 = B  (Eq. 52)
LG = −(y12 + y21)/(y11 + y22) = (z12 + z21)/(z11 + z22)         (Eq. 58/92, 60)   bilateral (Tian) loop gain
F  = 1 − LG                                                     (Eq. 57, p. 33)
ZG = z11 − z12 = −A/B                                           (Eq. 65, 67)
ZL = z22 − z21 = (1 + A)/B                                      (Eq. 66, 68)
```

`ZG` and `ZL` ship through the **Z form** (`−A/B`, `(1 + A)/B`): one fewer cancellation than the `|Y|`
form of Eq. 65/66, and algebraically exact; the `|Y|` form is kept as a test oracle only
(`ZGFromY`/`ZLFromY`). The reduced two-port has **port 1 = the G-side terminal, port 2 = the L-side
terminal**; `ZG` looks into port 1 (the generator direction), `ZL` into port 2 (the load direction)
(Eq. 64–66). In the zero-feedback limit `ZG = 1/y11` and `ZL = 1/y22` (Eq. 89).

**Guards (overview D-7).** `C = 0` (`H0 = 0`, an exact short from the G node to ground) makes `[Y]`
undefined; `B = 0` (`Y0 = 0`, an exact open in the probe branch) makes `[Z]` undefined. The library
returns NaN for the affected outputs at that frequency and the engine adds one run warning per
probe (`wsprobe.degenerate-node`) naming the probe and the first offending frequency. **No epsilon is
ever added to a denominator** — the document's own `wsp_yop`/`wsp__zop` add `1e-15`; circuitRF does
not.

These are computed by the engine at run time *through the same library functions every derived
metric calls* (overview D-2), so a run's `ZG:GATE` cube and a trace card's `wsp_ZG` of the same probe
are one implementation.

### 2.3 What a loop gain is, and is not

The two driving-point functions `H0` and `Y0` carry the full network determinant in their
denominators (Eq. 28–30) and are the document's *primary* stability metric (§4.9–4.10): plot `1/H0`
and `1/Y0` on a polar chart and look for Kurokawa's start-up signature — a clockwise crossing of the
negative real axis (Eq. 107/108). **Both must be checked**, because a zero can mask the pole in one
of them but never in both: the series resonator (Fig. 31) shows it in `1/Y0` only, the parallel
resonator (Fig. 34) in `1/H0` only (§4.10, Eq. 109–128; both are gates in `WSProbeTests`). Every
loop gain — including the default `LG` and `F = 1 − LG` — is **incomplete** (§4.4, p. 50: "not
fundamental circuit quantities and are therefore not rigorous stability measurements"). They are
diagnostic, and useful, and the user docs must carry the caveat with the weight the document gives it.

### 2.4 Where feedback around a probe comes from

A probe has feedback around it — `y12 ≠ 0`, `ZG ≠ 1/YG` (Eq. 79) — only when some path joins its
two sides *other than through the probe itself*. Ground does not count: two one-ports that share only
the reference have `z12 = 0`. A probe placed between a series element and a node whose only other
connection is a termination to ground (Hero 1's `L1`–`a1` wire, for instance) has a dangling one-port
on its G side and `ZG = 1/YG` exactly, however much feedback the rest of the network carries.
Algebraically `ZG·YG = 1 ⟺ z12·(z12 + z21 − z11 − z22) = 0`, i.e. `z12 = 0` or `LG = 1`. The gate
fixture (`testdata/wsprobe/hero1_probed.cnl`) puts the probes at the two-port's ports, where `C5`
plus the two-port closes a loop around each.

## 3. DataSet layout and accessors

Per run (per sweep point when swept), all Complex:

| Cube | Axes | Content |
|---|---|---|
| `wsp` | `{freq, row, col}` | `row`/`col` are 1-based integer values `1 … 2N` (no unit); `wsp[f, r, c]` is the document's `wsp(r, c)` with no index arithmetic between |
| `H0:<label>`, `Y0:<label>`, `ZG:<label>`, `ZL:<label>`, `LG:<label>`, `F:<label>` | `{freq}` | one set per probe, the `:` spelling of `I:IP1`; `H0`/`ZG`/`ZL` in Ω, `Y0` in S |
| `__WspProbes` | `{probe}` (labels = the probe labels, in idx order); Real values = `idx` | metadata; excluded from pickers like every `__` cube |

Under `ParametricSweepEngine` the sweep axis is prepended (`wsp[Vgg, freq, row, col]`,
`ZG:P1[Vgg, freq]`) and `__WspProbes` passes through unstacked, by the existing `StackSweepAxis`
rules (gate R-wsp1-14(h)).

Measurement accessors (`Evaluator.EvalQualifiedAccessor`), alongside `HB1.V(...)`:

```
SP1.wsp                 → the {freq,row,col} cube (or {sweep…,freq,row,col})
SP1.wsp(3, 13)          → one element traced over freq: the document's wsp(3,13) (Eq. 36), matched
                          by axis VALUE — never positional, since a positional slice would pin freq
SP1.idx("GATE")         → Real scalar, the probe's idx (from __WspProbes)
SP1.H0("GATE")  SP1.Y0("GATE")  SP1.ZG("GATE")  SP1.ZL("GATE")  SP1.LG("GATE")  SP1.F("GATE")
                        → the per-probe cubes; trailing sweep axes kept, as V(...) keeps them
```

An unknown label is an error that lists the probes present. `measure` lines using these evaluate
identically in the GUI and under `circuitrf sparam`, which runs the TestBench's measurements through
the one `MeasurementEvaluator`.

CLI: `circuitrf sparam` prints one line per probe after the S summary
(`WSProbe GATE idx=1  H0(f_lo)=… ZG(f_lo)=…`) and `--json` carries `wsprobes: [{label, idx}]`;
`-o out.npy`/`.mat`/`.txt` carry every cube; a Touchstone of a port-less run is a refusal naming
those spellings. `explain --analysis` lists each probe with its `idx` and both terminal nets, and
says `S-parameters: none (no ports)` when that is the case. `check` warns `wsprobe.shorted` when a
probe's two nets are the same net — the headless twin of the GUI's series-probe insertion cut.

## 4. Typo-register entries this half depends on

From the overview's §5; **implement the corrected form, never the printed one.**

- **T-1 — Eq. 40, `y12` and `y21`.** Both carry the `|P|` term with the wrong sign as printed. The
  correct values are `y12 = (AD − BC − A)/C` and `y21 = (AD − BC + D)/C`, which is what Eq. 44 and
  the Appendix E.13 code give. **Use Eq. 44.**
- **T-2 — Eq. 47, `z22`.** The first factor of its numerator is printed `iS/vP · vP/iP`; it is
  `iS/vS · vP/iP`. **Use Eq. 48.**
- **T-3 — Eq. 70.** `1/Y0 = z11 + z22 − z21 − y12` should read `− z12`. (`α + δ − β − γ = 1`.)
- **T-5 — Eq. 86/88.** The document contradicts itself on the asymmetric pure-feedback case
  (`CG = (1 + G2/G1)C` vs `(1 + G1/G2)C`). Only the symmetric case `G1 = G2`, `CG = CL = 2C`
  (Eq. 87) is a gate; the asymmetric case is not used.

One premise of the implementation brief that the physics overrode (recorded in
`src/Engine/RESOLVED.md`): the parallel resonator gate cannot use the series fixture's values. A
parallel negative resistance starts up only when its conductance exceeds the load's (`|R1| < RS`);
with `R1 = −20 Ω` against `RS = 10 Ω` the circuit is stable and the Kurokawa signature is absent.
The fixture uses `R1 = −5 Ω`.

---

## 5. Single-probe derived metrics (WSP-2)

Everything the document derives from **one** probe's 2×2 block of `wsp`, plus the small utility
functions of its appendix. The owner's instruction: these are **derived metrics**, never "scripts".
They keep the document's names and argument order exactly, so a designer who has read it can type
`wsp_yparam(SP1.wsp, SP1.idx("GATE"))` and get Eq. 44.

Probe *pairs* and the multi-probe functions (`wsp_yparam2`, `wsp_block_calc`, bifurcation, Ohtomo,
`wsp_ymatrix`, the envelope) are WSP-3's.

### 5.1 Where they live

`src/RfCore/Stability/`, framework-free, namespace `RfCore.Stability`, pure functions over
`Complex`. **The scalar core is the tested unit** (`tests/RfCore.Tests/Stability/WspNodalTests.cs`);
the cube-facing wrappers are a map over the frequency axis and any prepended sweep axes, and they
live in `src/Core/Expressions/Evaluator.Wsp.cs` — the expression engine's own file, which computes
nothing (overview D-6).

| File | Contents |
|---|---|
| `WspReduction.cs` | (WSP-1) `wsp_yparam`, `wsp_zparam`, the six defaults, `ZG`/`ZL`/`YG`/`YL` |
| `WspNodal.cs` | the document's names for the bidirectional immittances, the open-port immittances, all eight `wsp_loopgain` kinds and their S-parameter oracles, `wsp_nodal_gamma`, the normalised driving-point loci, the margin slot |
| `WspKurokawa.cs` | `wsp_unstable_freq_kurokawa`, `encirculations`/`enc` |
| `WspTransfer.cs` | `wsp_impedance`, `wsp_gain` (App. C, D) |
| `ImmittanceModels.cs` | `wsp_zsrc`, `wsp_zprc`, `z_to_*`, `y_to_*` |
| `GainDefinitions.cs` | `GainDEFs`, `_dB` |
| `WspRenorm.cs` | `wsp_rc_renorm_s`, `wsp_zo_renorm_s` |

`WspNodal` does not re-implement `ZG`, `ZL`, `YG` or `YL`; it names them and forwards to
`WspReduction`, which is what the engine computes a run's `ZG:<label>` cubes with. That is why
`wsp_ZG(SP1.wsp, SP1.idx("P1"))` and the run's own `ZG:P1` are **bit-identical** — one
implementation, two callers, held by test.

### 5.2 The built-ins, and what each returns

Every one is a built-in of `Evaluator` with the document's exact name and arity. Arguments: a `wsp`
cube (`{…, freq, row, col}`), a probe index (`SP1.idx("GATE")` or an integer literal), a 2×2 network
(`{…, freq, i, j}` — the same shape the `S` cube has, so `NetworkMetrics` and the Data Display's
network-parameter paths accept it), or a scalar-per-frequency cube (`{…, freq}`).

| Function | Equation | Returns |
|---|---|---|
| `wsp_yparam(wsp, idx)` | Eq. 44 | `{…, freq, i, j}` S |
| `wsp_zparam(wsp, idx)` | Eq. 48 | `{…, freq, i, j}` Ω |
| `wsp_H0(wsp, idx)`, `wsp_Y0(wsp, idx)` | Eq. 50/132, 52/134 | `{…, freq}` Ω, S |
| `wsp_ZG`, `wsp_ZL` | Eq. 67, 68 (E.14) | `{…, freq}` Ω |
| `wsp_YG`, `wsp_YL` | Eq. 76 (E.14) | `{…, freq}` S |
| `wsp_zop`, `wsp_yop` | Eq. 89, 90 (E.16) | `{…, freq}` Ω, S |
| `wsp_loopgain(Y, kind [, Z0])` | Eq. 92, 97, 99–104 | `{…, freq}` |
| `wsp_nodal_gamma(wsp, idx)` | E.7 | `{…, freq}` |
| `wsp_nZ(wsp, idx)`, `wsp_nY(wsp, idx)` | circuitRF's own, §5.6 | `{…, freq}` |
| `wsp_stability_margin(wsp, idx)` | — | **refused**, §5.6 |
| `wsp_unstable_freq_kurokawa(T)` | Eq. 107/108 (E.12) | `{n}` Hz, possibly empty |
| `encirculations(SP)`, `enc(SP)` | E.3 | `{…, freq}` Real, the running count |
| `_dB(M)` | E.2 | `10·log10\|M\|` |
| `wsp_impedance(wsp, i, j [, stimulus])` | Eq. 196/197 (App. C), Eq. 37 | `{…, freq}` Ω |
| `wsp_gain(wsp, S, G, D)` | App. D code (T-14) | `{…, freq}` dB |
| `GainDEFs(GamS, SM, GamL)` | Eq. 203–208 (E.1) | `{…, freq, gaindef}` dB, labels `GT_dB`, `GP_dB`, `GA_dB`, `Gmax_dB` |
| `wsp_zsrc(R, C [, f])`, `wsp_zprc(R, C [, f])` | E.4 | `{…, freq}` Ω |
| `z_to_pr/pc/pl/sr/sc/sl`, `y_to_…` | E.11 | `{…, freq}` Ω / **F** / **H** |
| `wsp_zo_renorm_s(ZG, SP, ZL [, f])` | E.10 | `{…, freq, i, j}` |
| `wsp_rc_renorm_s(RG, CG, SP, RL, CL [, f])` | E.10 | `{…, freq, i, j}` |

Kind strings for `wsp_loopgain` are the document's: `BI` (bilateral/Tian), `UNI` (forward
circulator; **`FOR` is an accepted alias**, p. 68), `REV`, `HST`, `MB`, `MBR`, `GFT`, `GFTR`. `Z0`
defaults to 50 Ω and is read only by the two circulator kinds. `wsp_loopgain(…, "BI")` is the same
function the run's `LG:<label>` cube comes from.

The document's `wsp__yparam` and `wsp__zop` (double underscore, E.13/E.16) are its own redundant
re-implementations of its own built-ins; circuitRF has one implementation and registers the
single-underscore names only.

**Free, and the document does not mention it:** `wsp_yparam` returns a cube shaped exactly like an
`S` cube, so once WSP-4 exposes a probe's reduced network as a Data Display source, **Rollett K, μ,
μ′, |Δ|, MAG/MSG and the stability circles of the reduced two-port at a probe** come from the
`NetworkMetrics` code already shipped, with nothing new written.

### 5.3 ZG is not 1/YG, and that is the point

`ZG`/`ZL` are the impedances the two sides present under **series-voltage** stimulation; `YG`/`YL`
are the admittances they present under **shunt-current** stimulation. They agree only when there is
no feedback across the probe, `y12 = y21 = 0` (Eq. 79–82; §4.5, p. 56–57). Reading one as the
reciprocal of the other is the single most common misreading of the probe, so the doc-comments say
so. Only the sums are fundamental: `ZG + ZL = 1/Y0` (Eq. 69), `YG + YL = 1/H0` (Eq. 77).

`Zop`/`Yop` are Ochoa's open-loop port immittances (§4.7): the part of `1/H0` and `1/Y0` that
survives when the bilateral feedback is removed, and therefore the part that carries an instability
*between* two otherwise-unconnected blocks — Kurokawa's own case, where every loop gain reads zero
(p. 62–63). `1/H0 = (1 − LG)/Zop`, `1/Y0 = (1 − LG)/Yop`, `Zop/H0 = Yop/Y0`, `YG + YL = |Y|(ZG + ZL)`
(Eq. 93–95) are all gates on 3,000 random two-ports.

The document's note that `YG`/`YL` are "NOT outputs of the WSProbes" (E.14) describes its own
implementation, not the quantity. Here they are first-class functions and Data Display items.

### 5.4 The two circulator loop gains are verified through an independent path

Eq. 97 and Eq. 99 are implemented in the printed `ȳ = Z0·y` form. Their S-parameter equivalents
`LGR = S21 + S11·S22/(1 − S12)` and `LGF = S12 + S11·S22/(1 − S21)` (Eq. 96, 98, with
`S = (I − ȳ)(I + ȳ)⁻¹`) agree to 2e-15 on random two-ports and are kept as the test oracle only.

The document says the two are "numerically identical" to what an ideal circulator's third port would
reflect (p. 67). **That is now a test through a completely independent path**
(`WspNodalFunctionTests.C_…`): `hero1_probed.cnl`'s `WSProbe:P1` is replaced by circuitRF's own
`Circulator` model, ports 1 and 2 in the same node and port 3 brought out as a fifth analysis port,
and `S55` of that five-port is compared against `wsp_loopgain` of the probed run. **Measured:
`Direction="CW"` matches `"REV"` and `Direction="CCW"` matches `"UNI"`, to 4.7e-15 — against 0.18
the other way round.** The mapping was a prediction of the brief; it is a measurement now.

### 5.5 Kurokawa's start-up search, and why the step direction is the whole test

`wsp_unstable_freq_kurokawa(T)` forms `g(ω) = 1/T(ω)` on the sampled sweep and reports every
frequency where `Re(g) ≤ 0`, `Im(g) = 0` and `∂Im(g)/∂ω > 0` (Eq. 107 for `H0`, Eq. 108 for `Y0`).
Concretely: for consecutive samples with `Im(g_k) < 0 ≤ Im(g_{k+1})` — a **clockwise** crossing of
the negative real axis, decreasing argument through `π` — the zero of `Im(g)` is interpolated
linearly in `ω`, `Re(g)` is interpolated there, and the frequency is reported when that `Re(g) ≤ 0`.
A counter-clockwise crossing is the steady-state side of a *stable* resonance and is **not**
reported; the two stable versions of the document's own resonators (`R1` made positive) return empty
from both searches, which is the gate.

**Both `H0` and `Y0` must be checked** (§4.9–4.10): the series resonator shows the signature in
`1/Y0` only, the parallel resonator in `1/H0` only, and the gate measures exactly that — 1.59155 GHz
in one, nothing in the other, on a 10 MHz grid.

An empty result is an **empty cube**, not a zero (the document returns "a zero in case there are no
frequencies", E.12). It means "no crossing was sampled", not "the circuit is stable": **a sweep
coarser than the resonance can step over a crossing entirely**, the same caveat the group-delay page
carries for phase unwrapping. Swept data is refused with the document's own note ("does not work
with multi-index swept data", E.12), naming `at(...)` as the way to pin the sweep first — a list of
frequencies has no shape once there is more than one sweep point.

`encirculations`/`enc` is `−unwrap(phase(SP))/360`, returned as the running count over the sweep;
the net count is the last value rounded. The minus makes a **clockwise** encirclement positive,
which is the sign NDF and the Nyquist argument want (§8).

### 5.6 What is circuitRF's own, and what is refused

`wsp_stability_margin` is **registered and refused** (overview D-12) with
`wsprobe.margin-not-transcribed` and the paper's DOI: the definition of T. A. Winslow, "A Novel
Stability Margin for Transfer Functions", EuMIC 2024 (DOI 10.23919/EuMIC61603.2024.10732614) is not
in the public abstract and **is not guessed**. The name and the Data Display slot exist so that the
day the paper arrives the body replaces the refusal and nothing that referenced it is renamed.

What ships in the meantime, under circuitRF's own name and labelled as such, are the two ingredients
the abstract names — unitless, bounded proxies for the driving-point loci:

```
circuitRF normalized driving-point locus (series):  nZ = (ZG + ZL)/(|ZG| + |ZL|) = (1/Y0)/(|ZG|+|ZL|)
circuitRF normalized driving-point locus (shunt):   nY = (YG + YL)/(|YG| + |YL|) = (1/H0)/(|YG|+|YL|)
```

Both are unitless with `|n| ≤ 1`. Kurokawa's three conditions are invariant under division by a
positive real, so the search reports the **same frequencies** on `nZ` as on `Y0` and on `nY` as on
`H0` — a gate, and the reason these are safe to offer beside the raw loci. Their doc-comments and
Data Display descriptions carry the sentence **"not the published margin"**.

### 5.7 Deliberate deviations from the document

- **Base SI, not pF/nH.** The document's `y_to_pc` returns picofarads (`1e12·…`) and `y_to_pl`
  nanohenries. circuitRF returns **farads and henries**. Every trace and every derived metric here
  is base SI, and a scale factor hidden inside a function is exactly the class of defect that once
  produced a run at 2 Hz that looked entirely normal. The names are unchanged.
- **No epsilon in a denominator.** The document's `wsp_yop`/`wsp__zop` (E.16) add `1e-15`, and its
  `wsp_zsrc` (E.4) adds `1e-9` to the frequency to survive DC. circuitRF adds neither: a vanishing
  denominator returns NaN, which is what an exact short, an exact open, or a series capacitor at DC
  actually is (overview D-7).
- **A negative reference resistance is refused, not absorbed.** E.10 writes `abs(real(RG))` under
  its square root and carries on; circuitRF refuses with `wsprobe.renorm-negative-reference` — a
  reference impedance with `Re ≤ 0` is a wrong input, not a case, and the power-wave definition
  divides by `√Re(Z0)`.
- **`wsp_zo_renorm_s` calls the repository's own complex-reference renormalisation** (`RFNetwork.SToS`,
  the Z0-override path's) rather than transcribing E.10's matrix expression. The transcription lives
  in the test, as the oracle, and nowhere else.

### 5.8 Typo-register entries this half depends on

- **T-6 — Eq. 100 sign.** The single-probe Hurst loop gain is printed `LG_H = −y21·y12/(y11·y22)`
  while the two-block Hurst form (Eq. 141/150) has no minus. The minus follows the convention of
  Eq. 53 (`T ≡ −Vp/Vx`, `LG = −T`); it was not independently re-derived. **Implemented as printed**,
  and the doc-comment says so.
- **T-14 — Eq. 199 and Eq. 201.** As printed, both factors of `wsp_gain`'s ratio use the **drain**
  index — which would make the gain identically 0 dB. The App. D code
  (`GT = 10*log(mag(real(…D…)/real(…G…)))`) is unambiguous: drain in the numerator, gate in the
  denominator, and Eq. 198/200 agree. **The code is implemented**, and the gate asserts the answer
  is not 0 dB for exactly this reason. The document's `log` is `log10`.

### 5.9 What `wsp_impedance` actually measures, and why the stimulus probe matters

Off the diagonal the ratio is `V_j / iS_j` at the response probe. Whether that is the **load line**
or a **one-port impedance** is decided by where the stimulus is, and the document's phrase "common
stimulus" is doing real work:

- With the drive on the response probe's **L side**, its G side is source-free and the ratio is
  `−Z_G` — the negated impedance looking the generator way, with no dependence on the drive at all.
- With a **common** drive that makes both halves of a symmetric structure live, the response probe's
  L side contains an active device and the ratio is the effective impedance that device works into
  — the even-mode load line.

`combiner_even_mode.cnl` measures both on one circuit: driven from the probe that splits the common
input node, either drain reads `RM + 2·RL = 55 Ω` exactly (independent of gm, of the device output
resistance and of frequency), matching `combiner_half.cnl` — one branch with the common load doubled,
the classic even-mode reduction. Driven from the *other branch's* probe instead, the same function
reads `−1000 Ω`, that device's own output resistance negated.

**Eq. 37's shunt form and App. C's series form are the same number off the diagonal**, exactly — both
ratios cancel the same common stimulus at the same response probe. They differ only on the diagonal,
where the series form is `−ZG` and the shunt form is `1/YL`. The `stimulus` argument therefore exists
for the diagonal case and for fidelity to the document, not because the two disagree where the
function is normally used.
