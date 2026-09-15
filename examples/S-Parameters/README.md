# S-Parameters

Two testbenches, both driven by **Terms** — the element that defines an S-parameter port and its
reference impedance. Run either from the Analyses panel, then open the result in a Data Display.

## Amplifier

A two-port Touchstone file (`potentially_unstable_amp.s2p`) placed as an **SnP** block between two
50 Ω Terms, swept 1–5 GHz. The part is not unconditionally stable across that band, which is the
point of the measurement blocks:

| Block | What it computes |
|---|---|
| `MeasGain` | `S11_dB`, `S21_dB`, `S12_dB`, `S22_dB`, `VSWR_in` |
| `MeasStability` | `Delta`, Rollett `K`, `MU`, and `MSG_dB` |

Three things in those expressions are worth copying:

- **`SP1.S(2,1)` is S21.** The `i` and `j` arguments are 1-based **port numbers**, not array
  indices — `S(2,1)` is not "row 2, column 1 of a zero-based matrix".
- **`x*x`, not `x^2`.** Only `+ - * /` broadcast over a result cube; the power operator does not,
  and the run reports the measurement as failed rather than guessing.
- **A measure can use an earlier one.** `K` reads `Delta`, which is declared above it. Order is
  declaration order, across blocks as well as within one.

## CoupledInductors

Two 1 nH inductors with a 500 pH **Mutual** between them. Small enough to check by hand, and it
shows `phase()` — which is in **degrees**, not radians.
