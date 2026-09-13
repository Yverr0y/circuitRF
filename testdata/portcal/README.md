# `testdata/portcal` — the port-calibration clearance fixtures

Seven cells, one technology, and a `.cem` on each. They exist because the port-calibration series
(`docs/sonnet-briefs/brief-portcal-0-overview.md`) needs a failing case and a passing one that differ
in **nothing but the separation of the feeds**, and because every measurement in PCAL1's findings
(`src/Engine/Mom/RESOLVED.md`, "PCAL1 — how much clearance a calibrated port actually needs") was made
on this geometry.

```
dotnet run --project src/Cli -- em testdata/portcal/coupled-pair/em/coupled-pair.cem
dotnet run --project src/Cli -- em testdata/portcal/coupled-pair-passive/em/coupled-pair-passive.cem
dotnet run --project src/Cli -- em testdata/portcal/separated-pair/em/separated-pair.cem
dotnet run --project src/Cli -- em testdata/portcal/coupled-asym/em/coupled-asym.cem
dotnet run --project src/Cli -- em testdata/portcal/coupled-triple/em/coupled-triple.cem
dotnet run --project src/Cli -- em testdata/portcal/offset-pair/em/offset-pair.cem
dotnet run --project src/Cli -- em testdata/portcal/pad-coupled-pair/em/pad-coupled-pair.cem
```

## What each one is

**`coupled-pair`** — two straight microstrips, 254 µm wide and 3.83 mm long, 246 µm apart edge to
edge, on 0.9 mm FR-4 (εr 4.4, tan δ 0.02) over a ground plane, with an edge port at each of the four
ends. The pair is a **uniform cross-section**, which is the whole point: the cross-section kernel
solves it essentially exactly and is the oracle, so forcing the full-wave planar kernel on the same
file measures the planar kernel's de-embedding and nothing else. The `.cem` sets `AnalysisKind:
Planar` explicitly for that reason; leaving it on Automatic picks the oracle instead.

PCAL2 **refused** it — every one of its four ports has metal 246 µm away, 0.27 substrate heights
against the 5 a neighbour that carries a port of its own needs. **Since PCAL4 it RUNS**: the two ports
at each reference plane are a CALIBRATION GROUP, sharing one two-conductor standard and one modal
error box, and the answer comes back at **max |ΔS| 0.0454 against the two kernels' own 0.0521
agreement floor** — below the floor at every frequency, with S₂₁ within 0.04 dB of the oracle where it
used to be 22.6 dB adrift. N = 298; the standards are 9.14× that, which is exactly what PCAL3's
widened ones cost on the same metal.

To see what the per-port scalar calibration used to publish, run it with
`DeembedOutsideCalibrationValidity: true` **and** the grouping off (`IncludeDrivenGroups = false` on
`PlanarCalibrationSettings`, engine-side — it has no `.cem` field): **non-passive at 6 of the 7
frequencies**, worst σ_max 1.0041, max |ΔS| 0.985.

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

**`coupled-asym`** — a 254 µm line beside a **508 µm** one, the same 246 µm apart, four ports. PCAL4's
gate 4: a symmetric pair is the one case where assuming even and odd modes would also have worked, so
an asymmetric one is what proves the modal basis is extracted rather than assumed. Max |ΔS| 0.0456.

**`coupled-triple`** — **three** conductors, 254 / 432 / 660 µm wide, 246 µm apart, six ports. PCAL4's
gate 3: one group of three, three modes, one six-port standard. Max |ΔS| 0.0469. **The widths are
unequal on purpose**: three conductors of EQUAL width at this spacing have two modes only 0.369° apart
in electrical length at 1 GHz and are REFUSED for it (the floor is 0.5°), which is R-pcal4-6 working
and is worth knowing about.

**`pad-coupled-pair`** — **PCAL5's fixture, and the first one here whose ports do not sit on the end
of a straight line.** Two 254 µm lines 812.8 µm apart, each ending in a 558.8 µm square pad, the two
pads 508 µm apart edge to edge — 0.56 substrate heights, well inside the 5 a driven neighbour needs.
So the far plane's two ports are a calibration group AND the pad is shorter than the 2700 µm the
standard reproduces, which makes `PlanarFeedExtension` grow each of them a 2151.563 µm lead.

Until PCAL5 that was a **refusal**, and by name: PCAL4's R-pcal4-6 declined any group whose members
had grown a lead, because R-fed-2's peel states one γ per port while a group's region carries one per
mode. The geometry says otherwise — both leads are grown, collinear, equal in length and side by side
at the group's own separation, so together they are a uniform two-conductor section of exactly the
cross-section the group's standard reproduces, and the peel is a matched length of the GROUP's modes.
It runs now, as two groups, passive, with no caveat on the file.

**It exists because no other fixture here could reach that code at all** — every one of the others is
a straight line with its ports at the drawn ends, so not one of them ever grows a lead. That is why
PCAL4 shipped with the decline in it and nothing caught it until a real board hit it: a port that
lands on a PAD is what a board is made of.

**`offset-pair`** — the coupled pair with the neighbour **shorter and offset**, so its two ports sit at
a different station. The neighbour carries ports, so it is not PCAL3's case; the ports do not share a
reference plane, so it is not PCAL4's either. It is what **still refuses**, and it is the commoner
shape on a real board — PCAL2's own gates moved onto it when `coupled-pair` stopped refusing.

## Which brief consumes which

| brief | uses | as |
|---|---|---|
| **PCAL2** (`brief-portcal-2-refuse-not-warn.md`) | `coupled-pair` | **done** — refuses, naming port 1 and 246 µm, exits non-zero and writes no `.sNp` |
| **PCAL2** | `separated-pair` | **done** — runs clean and byte-identically; widened from 4 mm to 6 mm of separation for it, see above |
| **PCAL3** (`brief-portcal-3-passive-neighbour.md`) | `coupled-pair-passive` | **done** — the passive-neighbour gate: was 18.0 dB out in S₁₁ at 1 GHz, now inside the floor and passive, with the neighbour reproduced in the standard |
| **PCAL4** (`brief-portcal-4-modal-error-box.md`) | `coupled-pair` as drawn | **done** — one calibration group per reference plane, a modal error box, and max \|ΔS\| 0.0454 against a 0.0521 floor |
| **PCAL4** | `coupled-asym` | **done** — gate 4, the asymmetric pair |
| **PCAL4** | `coupled-triple` | **done** — gate 3, a group of three |
| **PCAL2/PCAL4** | `offset-pair` | **done** — what still refuses, and where PCAL2's own gates live now |
| **PCAL5** | `pad-coupled-pair` | **done** — a group whose every member grew a feed lead, peeled with the MODE's own γ; `tests/Ui.Tests/Em/PadTerminatedGroupTests.cs` |

## What is deliberately not here

**No reference Touchstone is committed.** The oracle is a *run*, not a file: point the same `.cem` at
the cross-section kernel (set `AnalysisKind` to `Auto` or `CrossSection`) and it produces the exact
answer for this geometry in about a thousandth of the time. Committing a golden `.s4p` would freeze one
kernel's discretisation error into the gate for the other.

**No golden A-vs-B FLOOR is committed either, for the same reason.** Every \|ΔS\| quoted above is
measured against that fixture's own floor, and the floor is measured by moving the same conductors
9 mm apart and comparing the two kernels there. All three coupled fixtures read **0.0521**.

`results/` is git-ignored — see `.gitignore`.
