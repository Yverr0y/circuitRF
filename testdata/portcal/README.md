# `testdata/portcal` — the port-calibration clearance fixtures

Three cells, one technology, and a `.cem` on each. They exist because the port-calibration series
(`docs/sonnet-briefs/brief-portcal-0-overview.md`) needs a failing case and a passing one that differ
in **nothing but the separation of the feeds**, and because every measurement in PCAL1's findings
(`src/Engine/Mom/RESOLVED.md`, "PCAL1 — how much clearance a calibrated port actually needs") was made
on this geometry.

```
dotnet run --project src/Cli -- em testdata/portcal/coupled-pair/em/coupled-pair.cem
dotnet run --project src/Cli -- em testdata/portcal/coupled-pair-passive/em/coupled-pair-passive.cem
dotnet run --project src/Cli -- em testdata/portcal/separated-pair/em/separated-pair.cem
```

## What each one is

**`coupled-pair`** — two straight microstrips, 254 µm wide and 3.83 mm long, 246 µm apart edge to
edge, on 0.9 mm FR-4 (εr 4.4, tan δ 0.02) over a ground plane, with an edge port at each of the four
ends. The pair is a **uniform cross-section**, which is the whole point: the cross-section kernel
solves it essentially exactly and is the oracle, so forcing the full-wave planar kernel on the same
file measures the planar kernel's de-embedding and nothing else. The `.cem` sets `AnalysisKind:
Planar` explicitly for that reason; leaving it on Automatic picks the oracle instead.

Since PCAL2 it is **refused**: every one of its four ports has metal 246 µm away — 0.27 substrate
heights, against the 5 a neighbour that carries a port of its own needs — and the run exits non-zero
having written no `.sNp`. Run it with `DeembedOutsideCalibrationValidity: true` in the `.cem` to see
what it used to publish: **non-passive at 6 of the 7 frequencies** (worst σ_max 1.0041), with S₂₁
22.6 dB adrift of the oracle at 1 GHz, and a provenance line on the Touchstone saying so. N = 298.

**`coupled-pair-passive`** — `coupled-pair` with **ports 3 and 4 deleted**, and nothing else changed:
the same two rectangles, the same 246 µm, the same `.cem`. The second conductor is therefore present,
passive and undriven — which is PCAL1's measured case 2, and the commonest shape of this problem on a
real board.

Until PCAL3 it was **refused** like its four-port sibling, at 0.27 substrate heights against the 2 a
portless neighbour needs. Since PCAL3 the calibration standard **reproduces the neighbour** instead and
the run publishes: 18.0 dB out in S₁₁ at 1 GHz and non-passive at 3 of 7 points becomes |ΔS| inside the
kernel-A-vs-kernel-B agreement floor and passive at every point. The standards roughly double, from
4.57× to 9.14× the DUT's own unknowns, which the run's own cost note states. See `src/Engine/Mom/
RESOLVED.md`, "PCAL3", for the measurement and for the one frequency it does not cover — the standard's
own neighbour resonance, which the run names.

**`separated-pair`** — the identical coupled section with 4 mm of line added at each port and the
other conductor held **6 mm** away there (5746 µm edge to edge = **6.38 substrate heights**, against
the 5 the driven threshold asks for). Same mesh settings, same sweep. No clearance breach, no
passivity note. This is AN-01's own remedy figure, drawn.

<b>It was 4 mm until PCAL2 and that is why it moved.</b> 4 mm is 4.16 substrate heights on this
stackup — above PCAL1's measured requirement at h = 0.9 mm (≈ 3.9 h) but below the 5 the shipped
threshold asks for, which is sized to cover the 0.225 mm case as well (≈ 5.5 h there). A fixture whose
whole job is to be the clean case cannot sit inside the refusal it exists to contrast with. The
pre-change build's `.s4p` for this widened geometry is byte-identical to the post-change build's,
which is how R-pcal2-6 was proved; see `src/Engine/Mom/RESOLVED.md`, "PCAL2".

## Which brief consumes which

| brief | uses | as |
|---|---|---|
| **PCAL2** (`brief-portcal-2-refuse-not-warn.md`) | `coupled-pair` | **done** — refuses, naming port 1 and 246 µm, exits non-zero and writes no `.sNp` |
| **PCAL2** | `separated-pair` | **done** — runs clean and byte-identically; widened from 4 mm to 6 mm of separation for it, see above |
| **PCAL3** (`brief-portcal-3-passive-neighbour.md`) | `coupled-pair-passive` | **done** — the passive-neighbour gate: was 18.0 dB out in S₁₁ at 1 GHz, now inside the floor and passive, with the neighbour reproduced in the standard |
| **PCAL4** (`brief-portcal-4-modal-error-box.md`) | `coupled-pair` as drawn | the driven-neighbour gate — the case the series was opened on; nothing else substitutes |

## What is deliberately not here

**No reference Touchstone is committed.** The oracle is a *run*, not a file: point the same `.cem` at
the cross-section kernel (set `AnalysisKind` to `Auto` or `CrossSection`) and it produces the exact
answer for this geometry in about a thousandth of the time. Committing a golden `.s4p` would freeze one
kernel's discretisation error into the gate for the other.

`results/` is git-ignored — see `.gitignore`.
