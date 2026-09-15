#!/usr/bin/env python3
"""Example PCell generators: a microstrip line and a spiral inductor.

This is the ARTWORK half of a PDK — the half that turns parameters into geometry. circuitRF starts
this file as an ordinary Python process and talks to it over a pipe; `pcell-generators.json` beside
this file is what points circuitRF at it.

Everything a generator needs is in the two arguments it is handed:

    params  the instance's parameter values, ALREADY CONVERTED to database units
    tech    the resolved technology - its layers, and the stackup underneath them

and everything it returns is in the `Result`: shapes, pins, optional parameter handles, and
optional diagnostics. There is no registration step and no second file: the `@generator` decorator
above each function is the declaration.

Read `Reference > PDK Authoring` in the in-app documentation for the full contract. The five rules
worth knowing before changing anything here are marked RULE below.
"""

from circuitrf_pcell import (
    Handle,
    Layer,
    Parameter,
    Pin,
    Rect,
    Result,
    dbu,
    generator,
    run,
)

#: Used only when no technology resolves. A layout with no technology still generates geometry;
#: only the electrical stamp refuses without one.
FALLBACK_LAYER = Layer(1, 0)


@generator("KIT_MLIN", [Parameter.length("W"), Parameter.length("L")])
def mlin(params, tech):
    """A straight microstrip line: the smallest cell that is still real."""
    w      = params.length("W", 10_000)
    length = params.length("L", 200_000)
    layer  = tech.signal_layer or FALLBACK_LAYER

    # RULE 1: the width is halved in INTEGER arithmetic, so an odd width straddles the axis the
    # same way circuitRF's own built-in MLIN straddles it. Float division does not.
    half = w // 2

    return Result(
        # RULE 2: pin 1 sits at the cell ORIGIN and the principal axis runs along +X. Every PCell
        # follows this, or nothing abuts anything else.
        shapes=[Rect(layer, 0, -half, length, half)],
        pins=[
            Pin("1", 0,      0, layer, w, 180.0),   # facing outward, to the left
            Pin("2", length, 0, layer, w,   0.0),
        ],
        # RULE 3: a handle makes a piece of the artwork DRAGGABLE, with the drag editing the
        # parameter that produced it. You never state how much the parameter changes per unit of
        # travel - circuitRF measures it by perturbing the parameter and re-running this function.
        handles=[
            Handle("L", anchor=(0, 0),            at=(length, 0),   axis=0),
            Handle("W", anchor=(length // 2, 0),  at=(length // 2, half), axis=90),
        ],
    )


@generator("KIT_SPIRAL", [Parameter.length("Width"), Parameter.length("Space"),
                          Parameter.length("Inner"), Parameter.integer("Turns")])
def spiral(params, tech):
    """A square spiral inductor - four parameters that interact, which is what a parameterised
    cell actually buys you."""
    w     = params.length("Width", 10_000)
    s     = params.length("Space", 8_000)
    inner = params.length("Inner", 100_000)
    # RULE 4: Turns is an INTEGER, not a length. It carries no dimension, so circuitRF does not
    # scale it. Declaring a count as a length is the single most common authoring mistake, and its
    # symptom is artwork that is wrong by a factor of 1000 and looks entirely plausible.
    turns = params.integer("Turns", 3)

    if turns < 1:
        # RULE 5: raising is how a generator REFUSES, and circuitRF reports it naming the cell.
        # Returning an empty Result would be a silently empty cell, which looks like it worked.
        raise ValueError(f"a spiral needs at least one turn; got {turns}")

    layer = tech.layer_named("Metal2") or tech.signal_layer or FALLBACK_LAYER
    pitch = w + s
    half, half_inner = dbu(w / 2), dbu(inner / 2)

    # The artwork is a chain of OVERLAPPING RECTANGLES, not one closed outline. A spiral drawn as a
    # single polygon needs a mitre rule at every corner, and getting that rule wrong is the classic
    # way a spiral's inductance comes out plausible and wrong. Integer rectangles union exactly and
    # every corner is square by construction.
    shapes = []
    for t in range(turns):
        r      = half_inner + half + (turns - 1 - t) * pitch    # this turn's centre-line radius
        r_next = r - pitch
        shapes.append(Rect(layer, -r - half,  r - half, r + half,  r + half))    # top
        shapes.append(Rect(layer,  r - half, -r - half, r + half,  r + half))    # right
        shapes.append(Rect(layer, -r - half, -r - half, r + half, -r + half))    # bottom
        top = r_next + half if t < turns - 1 else half                           # left, stopped short
        shapes.append(Rect(layer, -r - half, -r + half, -r + half, top))
        if t < turns - 1:                                                        # step inward
            shapes.append(Rect(layer, -r - half, r_next - half, -r_next + half, r_next + half))

    # The OUTER LEAD. Without it the outermost turn is a closed ring with no terminal on it, and
    # the cell cannot be connected to, abutted or driven by an EM port. A cell whose artwork is
    # right and whose terminals are nowhere is the kind of cell that looks finished.
    r_out = half_inner + half + (turns - 1) * pitch
    outer = r_out + half
    lead  = 2 * w
    shapes.append(Rect(layer, -half, -outer - lead, half, -r_out + half))

    diagnostics = []
    if s < w // 4:
        diagnostics.append(
            f"turn spacing {s} is under a quarter of the turn width {w}: inter-turn capacitance "
            "will dominate and the self-resonant frequency will be low")

    return Result(
        shapes=shapes,
        pins=[
            # Pin 1 is the tip of the outer lead, facing away from the coil. Pin 2 is the INNER
            # terminal, at the top face of the innermost rail, facing +Y -- left unrouted on
            # purpose: reaching it needs an air bridge or an underpass on another layer, and that
            # belongs to whatever PLACES the cell, not to the cell.
            Pin("1", 0, -outer - lead, layer, w, 270.0),
            Pin("2", -(half_inner + half), half, layer, w, 90.0),
        ],
        handles=[
            Handle("Inner", anchor=(0, 0), at=(half_inner, 0), axis=0,
                   label="inner opening"),
            Handle("Width", anchor=(0, r_out - half), at=(0, outer), axis=90,
                   label="turn width"),
        ],
        diagnostics=diagnostics,
    )


if __name__ == "__main__":
    run()
