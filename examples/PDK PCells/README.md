# PDK PCells — layout artwork from Python

The **artwork half** of a PDK: cells whose layout geometry is computed from parameters instead of
drawn once and stored. The kit lives inside this workspace, so there is nothing to install and
nothing to point at.

```
pcell-kit/
  pcell-generators.json   what circuitRF reads to find the kit
  kit.py                  the generators themselves
```

`kit.py` declares two, and each is one decorated function:

- **KIT_MLIN** — a straight microstrip line with `W` and `L`.
- **KIT_SPIRAL** — a square spiral with `Width`, `Space`, `Inner` and `Turns`.

Both declare **parameter handles**, so once placed you can drag the artwork and the drag edits the
parameter that produced it. You never state how much the parameter changes per unit of travel:
circuitRF measures it by perturbing the parameter and re-running the generator.

circuitRF will ask once whether to run this kit's scripts. Scripts in a workspace are somebody
else's code and are never started without being allowed.

## MicrostripLine

A testbench for the **built-in** MLIN — a real electrical model over the GaAs MMIC stackup, swept
1–40 GHz with an outer sweep stepping the line width 20 → 100 µm. `kit.py`'s `KIT_MLIN` draws the
same cell from the published contract; comparing the two is the most direct way to see what the
contract actually requires.

Run it from the Analyses panel to get all five widths. (`circuitrf sparam` runs the inner
frequency sweep only — a parametric sweep dispatches from the window.)

## SpiralInductor

A layout-only cell, 212 × 232 µm, three turns on Metal2 — and its `.clay` is exactly what `kit.py`'s
`KIT_SPIRAL` emits for Width 10 µm, Space 8 µm, Inner 120 µm, Turns 3. Place the generator from the
Library palette to get the same artwork as a live, parameterised instance you can drag.

Five rules are marked `RULE` in `kit.py`. The one that costs the most when it is missed: **a count
is declared as an integer, not a length.** A length is scaled into database units; a count is not.
Declare `Turns` as a length and the artwork comes out wrong by a factor of a thousand, with no
error anywhere.
