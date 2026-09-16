# PDK PCells — layout artwork from Python

The **artwork half** of a PDK: cells whose layout geometry is computed from parameters instead of
drawn once and stored. The kit lives inside this workspace, so there is nothing to install and
nothing to point at.

```
pcell-kit/
  pcell-generators.json   what circuitRF reads to find the kit
  kit.py                  the generators themselves
  KIT_MLIN.csym           their schematic symbols - see below
  KIT_SPIRAL.csym
  KIT_OSPIRAL.csym
  KIT_MIMCAP.csym
```

`kit.py` declares four, and each is one decorated function:

- **KIT_MLIN** — a straight microstrip line: `W`, `L`, `Metal`.
- **KIT_SPIRAL** — a square spiral: `Width`, `Space`, `Inner`, `Turns` (fractional), `Metal`.
- **KIT_OSPIRAL** — the same spiral with its corners cut, the same five parameters meaning the same
  things, and the same cell body underneath. Only the walk differs.
- **KIT_MIMCAP** — a metal-insulator-metal capacitor: `W`, `L`, `Connection` (series or shunt — a
  shunt one grounds a plate itself, through the wafer), and `C`, which is a **readout** rather than
  a field: the generator derives it and reports it, and typing into it could not do anything.

Place any of them from the **Library palette** into a layout. Every parameter above appears in the
Properties Inspector and re-draws the artwork when you change it; `Metal` is a dropdown, because the
generator declares its two legal values. All four cells also declare **parameter handles**, so you
can drag the artwork instead and the drag edits the parameter that produced it. You never state how
much the parameter changes per unit of travel: circuitRF measures it by perturbing the parameter and
re-running the generator.

## One tile, two views, because the kit ships a symbol

A generator draws artwork; it says nothing about what the cell looks like on a **schematic**. So each
of these ships a `.csym` beside the manifest, named after the generator it belongs to — that name
is the whole declaration, and the file is an ordinary circuitRF symbol drawn in circuitRF's symbol
editor.

That is what makes these cells whole parts rather than layout-only ones. Each appears as **one**
palette tile carrying both views, and the drop target decides which is placed: drop on a layout and
you get the artwork, drop on a schematic and you get the symbol. Open `SpiralInductor.clay` and run
**Update Schematic from Layout** and the coil arrives in the schematic with `Width`, `Space`, `Inner`,
`Turns` and `Metal` already set to what the artwork was drawn at — in µm, because that is how this
technology displays lengths, even though `kit.py` declares them in SI metres.

**The pins come from the symbol and the parameters come from the generator**, which is why the symbol
declares none: one list, read by the cell that uses it. Every one of them arrives **annotated on the
sheet**, because a parametric cell is its parameters — a spiral is three turns of 10 µm metal, and
that should be readable without clicking the part. Untick `Show on schematic` on any you would rather
not see.

A symbol is not a model. These cells have artwork and a glyph and no device equations, so a run
says so rather than inventing an answer; the built-in MLIN in `MicrostripLine` is what the electrical
half of a microstrip line looks like. `KIT_MIMCAP` reports a capacitance and that is still not a
model — it is a number the artwork implies, shown where a stale one would otherwise sit.

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
metal that lies on the substrate, and it is what every generator here draws on by default. A line on
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

`KIT_MIMCAP` is the one cell here with no `Metal` parameter, and the table above is why: its bottom
plate is Metal1, its top plate is `MIM Metal`, and the dielectric between them is the row that says
*only where a capacitor is drawn*. There is no second arrangement of that stack to choose, and a
dropdown offering one would be a lie with two entries in it. What it has instead is `Connection`,
which is a choice that does exist — and the bottom of the table is the reason it changes the
artwork rather than only the wiring.

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

Nine rules are marked `RULE` in `kit.py`. The one that costs the most when it is missed: **a count
is dimensionless, not a length.** circuitRF scales a length from SI metres into database units; a
count it leaves alone. Declare `Turns` as a length and the artwork comes out wrong by a factor of a
billion and still looks entirely plausible.

## The octagonal spiral, and what it reuses

`KIT_OSPIRAL` is not a second inductor generator. It is the same one: `_spiral_cell` in `kit.py`
takes a **centre line function** and does everything else — the outer lead, the re-centring, the
metal, the crossover, the two pins, the two grips, the diagnostics. `KIT_SPIRAL` hands it
`_spiral_centreline` and `KIT_OSPIRAL` hands it `_octagon_centreline`, and neither generator has a
body beyond that line. The five parameters are one list, declared once, for the same reason: two
cells that differ only in the shape of a walk must not be able to drift into offering different
parameters for it.

Three things had to become general for that to be true, and each of them is exactly as correct for
the square coil as the special case it replaced:

- **The outer lead is a direction, not a sign test.** It continues the last side by two turn widths
  along the way that side was already going. An octagon's last side is as often diagonal as axial.
- **A straight run is a quad.** It is extended by half a width at *both* ends as well as offset half
  a width to each side, and that is what squares off the joint: the mitre tip of a corner turning by
  anything up to ninety degrees lands exactly on the end of the extended run, so consecutive runs
  cover it between them with nothing to compute. An axial run is still emitted as a **rectangle** —
  which is what an axis-aligned quad is called — so a square spiral's artwork is rectangles all the
  way to the clipper.
- **A pin's outward direction is an angle.** `atan2` of the last side, wrapped into [0, 360). On an
  axial side it gives back exactly the 0 / 90 / 180 / 270 the sign test did.

### The sides are placed by their distance from the centre, not by their corners

Side *k* of the octagon is the line whose outward normal is `270 + 45k` degrees at distance
`a + k·pitch/8` from the centre; a corner is then simply where two consecutive sides cross. Stated
that way, side *k* and side *k+8* are parallel and exactly one pitch apart **by construction**, which
is the property the whole coil exists to hold.

The obvious alternative is to walk corner to corner along rays from the centre — put vertex *k* at
angle `45k` and grow its radius a little each side. It draws something that looks identical. It is
not the same: the two ends of a side then sit at slightly different radii, so adjacent turns are very
slightly non-parallel and the gap **drifts across the side**. Nothing on screen shows it; a
min-spacing check finds it, on somebody else's machine, later.

`Turns` means laps here as it does for the square, so it is multiplied by **eight** rather than four —
the outer terminal moves an eighth of a lap at a time, and `3.125` is a real and useful value.

Measured on this kit's default coil at three turns, the octagon's winding is **14% shorter** than the
square one and encloses **11% less area**: it gives up rather less inductance than it saves in
conductor. The rest of what an octagon buys is not in these coordinates at all — current in a square
spiral crowds on the inside of each ninety-degree bend, and a forty-five-degree one spreads it.

## The MIM capacitor

`KIT_MIMCAP` is four storeys of the stackup, and **one of them is drawn by not drawing it**:

| | |
|---|---|
| Metal2 | the strap that carries the top plate away |
| `MIM Via` | the post up to it |
| `MIM Metal` | the top plate — `W` × `L`, and the plate that sets `C` |
| *MIM Dielectric* | **not drawn** — 0.2 µm of εr 6.8, present wherever the top plate is |
| Metal1 | the bottom plate, which encloses the top plate by one minimum feature |
| `Backside Via` | in `Shunt` only — through the wafer to the metal on the back |

Open the technology and look at `MIM Dielectric`: it carries **no drawing layer at all** and is
declared `PresentWithLayer: MIM Metal`. Drawing the top plate is what puts it there. A generator that
helpfully drew one as well would stack a second insulator under the first, and the artwork would look
exactly the same.

**The top plate can only leave upwards.** `MIM Via` spans MIM Metal to Metal2 and nothing in this
stackup spans MIM Metal to Metal1, so the escape is the spiral's crossover one storey higher: a post
off the plate, a span on Metal2 over the bottom plate's edge, an ordinary Metal1–Metal2 post, and a
landing pad — so both terminals end up on Metal1 and abut the same things as everything else in the
kit.

`W` × `L` is the **top** plate rather than the bottom one, and that is the whole reason `C` can be
stated at all: the bottom plate is it grown by the process enclosure, and if the parameters named the
bottom plate instead then the capacitance would depend on an enclosure rule nobody typed.

### `Connection`: series, or shunt to ground

A shunt capacitor **is not a series one with a wire on it**, which is why this is a parameter on the
cell rather than something you draw around it. Set `Connection` to `Shunt` and the cell grounds a
plate itself, through a hole drilled clean through the 100 µm of GaAs onto the metal on the back —
60 µm of hole inside an 80 µm pad, which is a big thing beside a 60 µm capacitor and is meant to look
like one.

**Which plate is grounded is not a free choice, twice over.** `Backside Via` spans Metal1 to Backside
Metal and nothing spans the MIM top plate to anything below it, so the only plate that can *reach*
ground is the bottom one. That is also the plate you *want* grounded: it is the one facing 100 µm of
εr 12.9, and grounding it shorts out a plate-to-backside capacitance that would otherwise hang off
the signal node. Ground the top plate instead and the cell still works, still draws, and carries a
parasitic nobody declared.

So the signal terminal is the **top** plate's — and pin 1 lives at the cell origin (RULE 6), which is
the other end of the cell from where the series part's pin 1 is. **The shunt cell is therefore the
series cell reflected**, with the ground via added to the plate whose own terminal it replaces: one
layout routine, read in either direction. The two grips are stated in the frame the cell ended up in
rather than the one it was drawn in, which is the part that is easy to get wrong — a handle mirrored
along with the artwork names an anchor that *moves* when the parameter changes, and the drag solver
then measures a sensitivity that is not the one being dragged.

`C` is the same number either way. The plates did not move; only what they face did.

**The symbol cannot follow, and that is a real constraint on how you parameterise a cell.** A kit
ships one `.csym` per *generator*, so a parameter that changed the pin count or the glyph would leave
the schematic side saying something the artwork does not. `KIT_MIMCAP` stays a two-terminal capacitor
in both connections — in shunt, pin 2 is simply the ground terminal, which it also is on the sheet —
and that is what makes `Connection` a parameter the cell can honestly carry.

### `C` is a readout, and that takes two separate statements

`C` is declared `computed=True`, which is what makes circuitRF render it as text instead of an edit
box — typing into a derived value cannot do anything, and offering the box says otherwise. That
declaration alone is *not* enough. A value that is never read is a value nobody updates, so the
parameter list would keep showing whatever number the instance was stored with while the geometry
that determines it moved underneath. The second statement is per-run: the generator returns
`computed={"C": …}` from the same call that drew the plate, and that is what makes the number track
the artwork.

It is computed from the plate that was actually **drawn**, not from `W` × `L`, so the integer halving
of the width is in it like everything else. The permittivity and the thickness come out of the
**stackup** rather than being stated in `kit.py` — the `.ctech` is what an EM extraction and a DRC
deck already agree with, and a second copy of 6.8 in a script is a number that can go stale with
nothing to notice.

This is also the one place in the kit that turns coordinates back into a physical size, and it is
allowed to: `tech.dbu_per_micron` is handed over for exactly that — a constant the generator itself
holds, with no other way of becoming a number. Length *parameters* are still converted by circuitRF
and never here.

### And an EM run of this cell returns that capacitance — which it did not until 2026-09-15

Worth saying because the readout and the EM run are two independent statements about the same plate,
and for a while they disagreed. A 0.2 µm dielectric between two meshed levels is a hundredth of the
cell a 60 µm plate is meshed at, and the cross-level part of the matrix fill was integrating across a
peak that narrow instead of resolving it: the same capacitor whose parameter list said 1.084 pF
extracted as an OPEN, with the wrong sign, and nothing in the run report said so — the answer was
still reciprocal and still passive. It now extracts at 1.089 pF, and in series with a 3.8 nH spiral
it resonates where the design intends rather than reading as a break in the circuit.

**What to watch, if you draw something unusual.** The condition is the CELL against the LEVEL
SEPARATION, and the run reports it by name. It is measured out to cell/separation 200; this kit's own
60 µm capacitor on the default mesh sits at 75, and the smaller ones further inside still. Past 200 a
note fires and says what it means — that nothing was measured up there, not that the answer is
known to be wrong. `src/Engine/Mom/HISTORY.md` §MIM-8 has the ladders.
