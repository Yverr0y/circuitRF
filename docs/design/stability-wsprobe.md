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
