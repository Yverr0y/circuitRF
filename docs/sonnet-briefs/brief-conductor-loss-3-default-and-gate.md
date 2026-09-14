# Brief CL3 — turning it on: the goldens, the docs, the phase gate

**Series:** [`brief-conductor-loss-0-overview.md`](brief-conductor-loss-0-overview.md) ·
**Tags:** `R-cl3-n` · **Area:** `src/Engine/Mom`, `docs/user/src/reference` ·
**Depends on:** CL1, CL2

**Read first:** CL1's `RESOLVED.md` §CL1 — **in particular R-cl1-9's table**, because if it did not
converge this brief does not start; the repo `CLAUDE.md`'s test-suite section in full;
`docs/user/src/reference/mom-engine.md:110-150`.

---

## The problem

CL1 and CL2 shipped behind `PlanarFillSettings.ConductorLoss = false`. Nothing the user runs has
changed. This brief flips it, absorbs the consequences, and puts a gate under the result.

**It is wide, not deep.** Roughly 90 files under `tests/Engine.Tests` touch `Planar*`, and every
golden taken on a structure with finite σ moves. That is the whole cost of this brief and it should
be planned as a re-bless, not as a debugging session.

## Milestones

1. **Flip the default.** `PlanarFillSettings.ConductorLoss = true`. The flag stays, permanently, as
   the PEC oracle — on the pattern of `UseSymmetricFactorization = false` and
   `UseRadialTable = false`, and for the same reason: every CL1 and CL2 accuracy gate compares
   against it.
2. **Triage the suite ONCE, from the TRX.** Run `dotnet test tests/Engine.Tests` once, read
   `tests/Engine.Tests/TestResults/last-run.trx`, and **classify every failure before changing
   anything**:
   - *Expected move* — a golden on lossy metal. Re-bless, and record the size of the move.
   - *Unexpected move* — a golden that should not have been sensitive (a reciprocity check, a
     passivity check, a mesh count, a PEC fixture). **Each one is a finding**, not a re-bless.
   - *Load-dependent* — attribute it per the repo `CLAUDE.md`'s three-check procedure before
     blaming the change.
   **Do not re-run the suite to find out what broke.** The TRX carries every failure message, stack
   and captured stdout.
3. **The re-bless, with the moves tabulated.** Every re-blessed golden gets its delta recorded in
   `RESOLVED.md`. A golden that moved by an implausible amount is the most likely place a sign error
   survives every gate CL1 and CL2 ran, because those gates were all on uniform lines and simple
   patches. **A bend, a stub, a spiral and a MIM fixture are where a wrong `Z_s` looks different.**
4. **Passivity and reciprocity get stricter, not looser.** Adding loss cannot make a passive
   structure active. If `PlanarPassiveNeighbourTests` or the reciprocity gates move in the wrong
   direction, stop — that is a real defect and this brief's re-bless would bury it.
5. **The A-vs-B agreement gate.** §10.9's kernel-A-versus-kernel-B comparison on a uniform line
   currently diverges on loss *by construction* and its text says so. It now compares two models
   that both have conductor loss. **Re-state what it gates**, and report the new agreement on ε_eff
   AND on α, on both starters, at the frequencies §5 already uses.
6. **The user-facing page.** `docs/user/src/reference/mom-engine.md:130-143`'s
   "Conductor loss, in the full-wave kernel" entry leaves the "Cannot" list. What replaces it is not
   a triumphal note but an honest one: conductor loss IS modelled, on the signal metal; the ground
   plane's share is or is not modelled depending on whether CL4 ran; and R-cl1-9's convergence
   result is what the accuracy claim rests on. **If CL4 has not run, the ground-plane omission is
   stated with its measured size** — 21% (FR-4) / ~11% (GaAs) / 25% (low-loss laminate) of the
   conductor term, overview §2.
7. **The sentence that was wrong anyway.** Whether or not it was already fixed per overview §4, this
   page must end up stating the FR-4 figure as an FR-4 figure and carrying the MMIC row beside it.
8. **Cost.** Fill and factor wall clock, and a full sweep, before/after on the hero, in `HISTORY.md`.
   Expected: unmeasurable. If it is measurable, the Gram matrix is being rebuilt per frequency.

## Gates

- **R-cl3-1** — the whole of `tests/Engine.Tests` green, every move accounted for in one of the
  three triage classes, and every re-bless justified by a number.
- **R-cl3-2** — `tests/Ui.Tests/Em` green: the `.cem` round trip, `EmRunService`, and the CLI `em`
  verb's byte-for-byte comparison against `EmRunService.Run`. **Nothing in this series adds a `.cem`
  key**, so the round trip must be bit-identical, and `EmSnpProvenance` already hashes σ and
  thickness (`EmSnpProvenance.cs:219`) so cached results invalidate correctly with no change. Assert
  that rather than assuming it.
- **R-cl3-3 — the phase gate, and it is one sentence per starter.** On the FR-4 hero and on the
  MMIC starter, a de-embedded uniform 50 Ω line's α from kernel B agrees with kernel A's total α
  within a stated band, at 2 / 10 / 20 GHz, **with the ground-plane term treated the same way in
  both kernels**. State the band from the measurement; do not choose it first and tune to it.
  Overview §0's α_c and α_d tables are the numbers to land beside.
- **R-cl3-4** — the five heroes' S-parameter regressions unchanged, because none of them routes
  through kernel B. Confirm rather than assume.

## Must NOT

- Re-run the full suite to discover a failure. Read the TRX.
- Run two test processes at once — they share `bin/` and the TRX and corrupt each other.
- Re-bless a golden whose move you cannot explain.
- Gate on `Category=Benchmark`. Measure a new slow test and tag it only if it is at or above ~5 s.
- Add a user control, a `.cem` key, or a way to turn conductor loss off from the GUI.
- Claim CL4's accuracy. If the ground plane is still PEC, the page says so with a number.

## On completion

- `src/Engine/Mom/RESOLVED.md` `§CL3`: the triage table, every re-blessed golden with its delta, the
  A-vs-B agreement, the phase-gate sentences, the cost measurement.
- `docs/design/mom-engine.md` §10.9's blockquote is rewritten — it is a design note and is edited
  normally. `docs/user/src/reference/mom-engine.md` per milestones 6 and 7.
- **No `CLAUDE.md` edit.** By this point `src/Engine/Mom/CLAUDE.md` §5's conductor-loss bullet, §7's
  entry and §3.7's metrics text are all stale. **Report all of them to the owner by file and line**
  in the completion message. This is the brief where that report is most important, because after
  CL3 the standing project memory says the kernel does something it no longer does.
