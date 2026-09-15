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

- **KIT_MLIN** — a straight microstrip line: `W`, `L`, `Metal`.
- **KIT_SPIRAL** — a square spiral: `Width`, `Space`, `Inner`, `Turns` (fractional), `Metal`.

Place either from the **Library palette** into a layout. Every parameter above appears in the
Properties Inspector and re-draws the artwork when you change it; `Metal` is a dropdown, because the
generator declares its two legal values. Both cells also declare **parameter handles**, so you can
drag the artwork instead and the drag edits the parameter that produced it. You never state how much
the parameter changes per unit of travel: circuitRF measures it by perturbing the parameter and
re-running the generator.

circuitRF will ask once whether to run this kit's scripts. Scripts in a workspace are somebody
else's code and are never started without being allowed.

## Every parameter has a declared default, and that is not cosmetic

A parameter declared with **no default is not placed at all**. The generator still draws — it falls
back to its own internal number — so the artwork looks right, and the parameter simply is not on the
instance: nothing in the Properties Inspector to edit, and every drag handle naming it reported as
*"declares a drag handle for 'L', which is not one of its parameters."* That is the whole
explanation for a parameterised cell that turns out not to be adjustable, and no symptom points at
the declaration.

**A length default is declared in SI metres** (`Parameter.length("W", 10e-6)`), not in database
units. A length *value* reaches the generator already converted to DBU; a *default* is a value
circuitRF holds on the instance and converts like any other, so `10_000` there means ten kilometres.

The **dimension** you declare is also what decides the unit the parameter is shown and typed in:
`Width` reads `10` in a layout displaying µm, and `0.4` in one displaying mils, exactly like every
other length in the panel. Declare it as `Parameter.real` instead and you get the bare SI number —
`0.00001` — in a field that will not accept `10 um` either.

## Which metal, and why the default is Metal1

Look at the Stackup tab of `tech/mmic-GaAs_2LM_100um.ctech` from the top down:

| | |
|---|---|
| **Metal2** | 3 µm conductor |
| Air | 2.55 µm |
| MIM Metal / MIM Dielectric | only where a capacitor is drawn |
| **Metal1** | 3 µm conductor, sitting on the GaAs |
| GaAs | 100 µm, εr 12.9 |
| Backside Metal | ground |

**Metal2 is the AIR BRIDGE.** It is 3 µm up with nothing but air beneath it, which is what an air
bridge is and is not something a coil or a transmission line can be built out of. Metal1 is the
metal that lies on the substrate, and it is what both generators draw on by default. A line on
Metal1 is an ordinary GaAs microstrip — h = 100 µm, εr = 12.9 — and that is exactly what the
`MicrostripLine` testbench extracts.

Two consequences worth knowing, because neither announces itself:

- **circuitRF's own default signal layer is the TOPMOST conductor**, which on this stackup is the
  air bridge. So `TL1` in `MicrostripLine` carries an explicit **`SignalLayer = Metal1`** — a
  per-instance parameter every microstrip component has, in the Properties Inspector. Without it the
  extracted substrate is h = 102.75 µm over a mixture of air, nitride and GaAs, and the netlist is
  the only place that shows.
- **The generators name their layers rather than asking the technology for "the signal layer"**, for
  the same reason, and they carry their own fallback for those names so a layout with **no**
  technology still draws the coil, the bridge and the via on three distinct layers instead of
  collapsing them onto one.

## MicrostripLine

A testbench for the **built-in** MLIN — a real electrical model over the GaAs MMIC stackup, swept
1–40 GHz with an outer sweep stepping the line width 20 → 100 µm. `kit.py`'s `KIT_MLIN` draws the
same cell from the published contract; comparing the two is the most direct way to see what the
contract actually requires.

Run it from the Analyses panel to get all five widths. (`circuitrf sparam` runs the inner
frequency sweep only — a parametric sweep dispatches from the window.)

## SpiralInductor

**A live PCell instance, not stored artwork.** The `.clay` holds one instance of `KIT_SPIRAL` and
the parameters it was placed at; the geometry itself is rebuilt by running `kit.py` when the
workspace opens. Select the instance and change `Turns` or `Inner` in the Properties Inspector and
the coil is redrawn — there is no stored copy to go stale.

Three turns, 10 µm wide on 8 µm spaces, around a 120 µm opening. Pin 2 is the **inner** terminal, and
getting it out is the part worth reading: it cannot cross the turns on their own metal, so the cell
takes it up through a via post, west along an **air bridge on Metal2** clear of every turn, and back
down onto Metal1 — so both terminals end up on one layer and abut the same things. Set `Metal` to
`Metal2` and the whole arrangement inverts: the coil moves up onto the bridge metal and the escape
becomes an underpass on Metal1.

### The winding is a walk, which is what makes `Turns` a real number

A square spiral is not a stack of rings; it is a walk. Go straight, turn ninety degrees, go straight
again, with every side one pitch longer than the side two before it — that one rule is what holds the
gap between adjacent turns at exactly `Space` the whole way round.

Because it is a walk, **`Turns` is a real number and a fraction means something**: it is multiplied
by four, the whole quarters are walked in full and the remainder is walked as a partial side. At 3
turns the outer lead leaves heading south; at 3.25 it leaves east, at 3.5 north, at 3.75 west. Pin 1
goes wherever the winding ends, and nothing in the generator special-cases it. (`Turns` is declared
`Parameter.real`, not `Parameter.integer` — an integer parameter refuses "3.5" in the Properties
Inspector, correctly, which makes the declaration the bug.)

**Pin 1 is at the end of the winding, and that is not a detail.** It used to be hung off the middle
of the outermost side, which looked right and was not: metal continued from the lead in *both*
directions — one way spiralling inward, the other running three-quarters of a lap to the winding's
real free end and stopping dead. A ¾-lap open stub, in the same metal as the coil, with nothing in
the picture to distinguish it. An inductor with one of those on it is a different part.

### Drawn as rectangles, emitted as one region

One rectangle per straight run is the easiest way to *say* a square spiral — integer rectangles union
exactly and every corner comes out square by construction, where a single hand-built outline needs a
mitre rule at every corner and getting that rule wrong is the classic way a spiral's inductance comes
out plausible and wrong. But rectangles are one **picture**, not one **figure**: a DRC width check
measures each rectangle rather than the conductor, an EM extraction meshes internal edges that carry
no current, and an exported GDSII or Gerber carries every seam into whatever reads it next.

So the generator asks circuitRF to union them — `clip("or", rings)` — and emits the regions that come
back. **The boolean is performed by circuitRF, not by the script**, over the same Clipper2 the layout
editor's own Boolean commands use, because two clippers that disagree by a database unit produce
artwork that renders perfectly and is wrong. The winding comes back as **one** polygon; the landing
pad is a **second**, and deliberately so — it is joined to the coil through a via, not through metal,
which is the whole reason the crossover exists.

`KIT_MLIN` is one rectangle and stays one rectangle. There is nothing to merge, and a union that
turns a rectangle into a polygon buys nothing.

### Every grip has a floor, and so does the field beside it

Drag the turn width through zero and every rectangle in the winding inverts; what reaches the
clipper is a self-intersecting mess that still renders. So each `Handle` declares a **`min`**, and
the drag solver clamps every proposal to it — the grip simply stops at 4 µm, this process's minimum
feature.

**That bound binds the gesture and nothing else**, which is why the generator also refuses the same
values outright: a number typed into the Properties Inspector goes straight through, and no handle
is consulted on the way. Stating the floor once would leave it holding in exactly one of the two
places people reach it from.

A bound is a parameter **value**, so like a default it is declared in **SI metres** — not in the
database units the four handle coordinates beside it are in.

Eight rules are marked `RULE` in `kit.py`. The one that costs the most when it is missed: **a count
is dimensionless, not a length.** circuitRF scales a length from SI metres into database units; a
count it leaves alone. Declare `Turns` as a length and the artwork comes out wrong by a factor of a
billion and still looks entirely plausible.
