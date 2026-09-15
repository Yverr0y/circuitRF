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

Read `Reference > PDK Authoring` in the in-app documentation for the full contract. The eight rules
worth knowing before changing anything here are marked RULE below.
"""

import math

from circuitrf_pcell import (
    LENGTH,
    Handle,
    HostUnavailable,
    Layer,
    Parameter,
    Pin,
    Polygon,
    Rect,
    Result,
    clip,
    dbu,
    generator,
    run,
)

#: The two drawing metals of the GaAs MMIC technology this kit is written for, and the via that
#: joins them. Named rather than inferred: a generator that picked "the topmost conductor" would put
#: this kit's artwork on the AIR BRIDGE, which is 3 um up in the air with nothing under it (see the
#: workspace README). Metal1 is the metal that sits on the GaAs.
COIL_METALS = ("Metal1", "Metal2")
VIA_LAYER = "Via"

#: The smallest feature this process will print, in micrometres - the Metal1/Metal2 minimum width
#: and minimum spacing of `mmic-GaAs_2LM_100um.ctech`, which are both 4 um. A generator is not handed
#: its technology's DRC rules (`tech` carries layers and a stackup, nothing else), so a kit that wants
#: a floor states its own; this kit already states this process's layer numbers a few lines up.
MIN_FEATURE_UM = 4.0

#: The same number in SI metres, which is the unit a declared BOUND is in - see `_bounded_handle`.
MIN_FEATURE_M = MIN_FEATURE_UM * 1e-6

#: What each of those names is when NO TECHNOLOGY RESOLVES. A layout with no technology still
#: generates geometry - only the electrical stamp refuses without one - and `tech.layer_named`
#: answers nothing there, so without this the whole cell collapses onto one layer and the air bridge
#: silently shorts the coil it crosses. A kit knows its own process's layer numbers; that is the one
#: thing it may state on its own account.
FALLBACK_LAYERS = {"Metal1": Layer(1, 0), "Metal2": Layer(2, 0), "Via": Layer(3, 0)}
FALLBACK_LAYER = FALLBACK_LAYERS["Metal1"]


def _metal_parameter(default="Metal1"):
    """The layer-choice parameter both generators carry.

    RULE 1: `choices` makes this a DROPDOWN in the Properties Inspector rather than a free-text box,
    and a `default` is what puts the parameter on a newly-placed instance AT ALL. A parameter
    declared with no default is not sent to the generator and does not appear in the instance's
    parameter list -- the generator falls back to whatever its own accessor call names, so the
    artwork still looks right and there is nothing on screen to edit. That is the single most
    confusing way to ship a parameterised cell, because every symptom points somewhere else.
    """
    return Parameter(
        "Metal", "string", default=default, label="metal layer", choices=COIL_METALS)


def _layer_named(tech, name):
    """The drawing layer `name` refers to, or the best thing available when it does not resolve."""
    return ((tech.layer_named(name) if name else None)
            or FALLBACK_LAYERS.get(name)
            or tech.signal_layer
            or FALLBACK_LAYER)


def _merged(layer, rects):
    """`rects` unioned into as few filled regions as they actually form.

    RULE 2: **circuitRF performs the boolean, the generator does not.** `clip` hands the rings back
    over the same pipe the generate request came in on, computed by the same Clipper2 the layout
    editor's own Boolean commands use. A union written here in Python would be a second
    implementation of one rule, and two clippers that disagree by a database unit produce artwork
    that renders perfectly and is wrong.

    Why bother, when overlapping rectangles already draw as one solid shape: they are one PICTURE,
    not one figure. Every consumer downstream sees the seams - a DRC width check measures each
    rectangle rather than the conductor, an EM extraction meshes across internal edges that carry no
    current, and an exported GDSII or Gerber carries every overlap into whatever reads it next. The
    union is what makes the cell one piece of metal to everything and not just to the eye.

    Outside a host - a generator imported and called directly, from a test or a REPL - there is
    nobody to ask, and `clip` says so rather than substituting a different answer. The rectangles
    are then returned as they stand: the same region, drawn in more pieces.
    """
    rings = [[(r.x1, r.y1), (r.x2, r.y1), (r.x2, r.y2), (r.x1, r.y2)] for r in rects]
    try:
        regions = clip("or", rings)
    except HostUnavailable:
        return list(rects)

    merged = []
    for region in regions:
        merged.append(Polygon(
            layer,
            [c for point in region.outer for c in point],
            [[c for point in hole for c in point] for hole in region.holes] or None))
    return merged


def _min_feature_dbu(tech):
    """`MIN_FEATURE_UM` in the database units of the layout being drawn into."""
    return dbu(MIN_FEATURE_UM * (tech.dbu_per_micron or 1000))


def _at_least(name, value, floor, tech):
    """Refuse `value` if it is under `floor`, naming both in micrometres.

    RULE 3: **a grip needs a floor, and so does the field beside it.** A `Handle` declares `min` so
    the drag solver never PROPOSES a value below it, which is what stops a grip dragged past zero
    from inverting every rectangle in the cell and handing the clipper a self-intersecting mess. That
    bound is not enforcement, though - it binds the gesture and nothing else, and a number typed into
    the Properties Inspector goes straight to the generator. So the floor is stated twice on purpose:
    once on the handle, where it steers, and once here, where it refuses.
    """
    if value < floor:
        per_um = tech.dbu_per_micron or 1000
        raise ValueError(
            f"{name} is {value / per_um:g} um; this process cannot print a feature under "
            f"{MIN_FEATURE_UM:g} um. Nothing was changed.")
    return value


@generator("KIT_MLIN", [Parameter("W", "real", LENGTH,  10e-6, minimum=MIN_FEATURE_M),
                        Parameter("L", "real", LENGTH, 200e-6, minimum=MIN_FEATURE_M),
                        _metal_parameter()])
def mlin(params, tech):
    """A straight microstrip line: the smallest cell that is still real."""
    # RULE 4: a length VALUE arrives in database units, but a length DEFAULT is declared in SI
    # METRES - the default is a host-side parameter value and goes through the same metres-to-DBU
    # conversion every other length parameter does. Declaring 10_000 there means ten kilometres.
    floor  = _min_feature_dbu(tech)
    w      = _at_least("W", params.length("W", 10_000), floor, tech)
    length = _at_least("L", params.length("L", 200_000), floor, tech)
    layer  = _layer_named(tech, params.text("Metal", "Metal1"))

    # RULE 5: the width is halved in INTEGER arithmetic, so an odd width straddles the axis the
    # same way circuitRF's own built-in MLIN straddles it. Float division does not.
    half = w // 2

    return Result(
        # RULE 6: pin 1 sits at the cell ORIGIN and the principal axis runs along +X. Every PCell
        # follows this, or nothing abuts anything else.
        shapes=[Rect(layer, 0, -half, length, half)],
        pins=[
            Pin("1", 0,      0, layer, w, 180.0),   # facing outward, to the left
            Pin("2", length, 0, layer, w,   0.0),
        ],
        # RULE 7: a handle makes a piece of the artwork DRAGGABLE, with the drag editing the
        # parameter that produced it. You never state how much the parameter changes per unit of
        # travel - circuitRF measures it by perturbing the parameter and re-running this function.
        # A handle may only name a parameter this generator DECLARES; one that names anything else
        # is reported and ignored, which is what happens to every handle on a cell whose parameters
        # were declared without defaults (RULE 1).
        handles=[
            Handle("L", anchor=(0, 0),           at=(length, 0),         axis=0,  min=MIN_FEATURE_M),
            Handle("W", anchor=(length // 2, 0), at=(length // 2, half), axis=90, min=MIN_FEATURE_M),
        ],
    )


def _spiral_centreline(a, pitch, turns):
    """The corner points of a square spiral's CENTRE LINE, innermost end first.

    A square spiral is a walk: go straight, turn ninety degrees, go straight again, with every side
    one pitch longer than the side two before it. That single rule is what keeps the gap between
    adjacent turns equal to `pitch` the whole way round, and it is why the walk - not a stack of
    concentric rings - is the construction to use.

    **The walk is also what makes a fractional turn mean anything.** `turns` is multiplied by four to
    get quarter turns; the whole ones are walked in full and whatever is left over is walked as a
    partial side. A count of 3.5 stops half way along the fourteenth quarter, and the outer terminal
    is wherever that leaves it. Nothing here special-cases it.

    Returns points in raw coordinates, centred on nothing in particular; the caller re-centres.
    """
    quarters = turns * 4.0
    whole    = int(math.floor(quarters + 1e-9))
    leftover = quarters - whole

    def side(k):
        # Ulam's rule: two sides of each length, then one pitch longer. 2a is the innermost side.
        return 2 * a + (k // 2) * pitch

    east, north, west, south = (1, 0), (0, 1), (-1, 0), (0, -1)
    headings = (east, north, west, south)

    x, y = -a, -a
    points = [(x, y)]
    for k in range(whole):
        dx, dy = headings[k % 4]
        x += dx * side(k)
        y += dy * side(k)
        points.append((x, y))
    if leftover > 1e-9:
        dx, dy = headings[whole % 4]
        run = dbu(side(whole) * leftover)
        x += dx * run
        y += dy * run
        points.append((x, y))
    return points


def _segment_rects(layer, points, half):
    """One rectangle per straight run of `points`, each grown by `half` on every side.

    Consecutive rectangles overlap by a full width at each corner, which is what squares the corner
    off exactly. They are unioned before they leave the generator - see `_merged`.
    """
    rects = []
    for (x1, y1), (x2, y2) in zip(points, points[1:]):
        if (x1, y1) == (x2, y2):
            continue
        rects.append(Rect(layer,
                          min(x1, x2) - half, min(y1, y2) - half,
                          max(x1, x2) + half, max(y1, y2) + half))
    return rects


@generator("KIT_SPIRAL", [Parameter("Width", "real", LENGTH, 10e-6, minimum=MIN_FEATURE_M),
                          Parameter("Space", "real", LENGTH,  8e-6, minimum=MIN_FEATURE_M),
                          Parameter("Inner", "real", LENGTH, 120e-6, minimum=MIN_FEATURE_M),
                          Parameter("Turns", "real", default=3.0, minimum=0.25),
                          _metal_parameter()])
def spiral(params, tech):
    """A square spiral inductor - five parameters that interact, which is what a parameterised
    cell actually buys you."""
    floor = _min_feature_dbu(tech)
    w     = _at_least("Width", params.length("Width", 10_000), floor, tech)
    s     = _at_least("Space", params.length("Space",  8_000), floor, tech)
    inner = _at_least("Inner", params.length("Inner", 120_000), floor, tech)
    # RULE 8: Turns is DIMENSIONLESS, not a length. circuitRF scales a length from SI metres into
    # database units; a count it leaves alone. Declaring a count as a length is the single most
    # common authoring mistake, and its symptom is artwork wrong by a factor of a billion that still
    # looks entirely plausible. It is a REAL rather than an integer because a spiral's outer terminal
    # comes out on a different side for 3.5 turns than for 3, and that is a real thing to want.
    turns = params.real("Turns", 3.0)

    if turns < 0.25:
        # Raising is how a generator REFUSES, and circuitRF reports it naming the cell. Returning an
        # empty Result would be a silently empty cell, which looks like it worked.
        raise ValueError(
            f"a spiral needs at least a quarter turn - one side - to have two ends; got {turns}")

    coil_name = params.text("Metal", "Metal1")
    layer     = _layer_named(tech, coil_name)
    # The inner terminal has to get out over the turns, and it cannot do that on the coil's own
    # metal. It crosses on the OTHER one: an air bridge over the coil when the coil is on Metal1, an
    # underpass beneath it when the coil is on Metal2. Either way the two never touch except through
    # the via posts placed below.
    bridge_name = COIL_METALS[0] if coil_name == COIL_METALS[1] else COIL_METALS[1]
    bridge      = _layer_named(tech, bridge_name)
    via         = _layer_named(tech, VIA_LAYER)

    pitch = w + s
    half  = dbu(w / 2)
    # The innermost side's half-length. `Inner` is the CLEAR opening across it: the side's inner face
    # sits at inner/2. The opening along the other axis comes out one pitch wider, which is what a
    # square spiral does - the turn has to step outward somewhere, and it steps once per lap.
    a     = dbu(inner / 2) + half
    lead  = 2 * w

    points = _spiral_centreline(a, pitch, turns)

    # The OUTER LEAD is an extension of the last side, in the direction that side was already
    # travelling - so it leaves the coil at the outermost END of the winding.
    #
    # It used to be a stub hung off the middle of the outermost side instead, and that is worth
    # knowing about because the artwork looked right: metal continued from the lead in BOTH
    # directions, one way spiralling inward and the other running three-quarters of a lap to the
    # winding's real free end and stopping. A dead-ended quarter-wave-ish stub, drawn in the same
    # metal as the coil, that nothing in the picture distinguishes from the inductor.
    (px, py), (qx, qy) = points[-2], points[-1]
    step = lead if qx > px or qy > py else -lead
    points.append((qx + step, qy) if qx != px else (qx, qy + step))

    # Re-centre on the winding's own bounding box, so the cell sits where it is placed rather than
    # drifting as `Turns` grows. Everything below is in the re-centred frame.
    xs = [x for x, _ in points]
    ys = [y for _, y in points]
    ox = -(min(xs) + max(xs)) // 2
    oy = -(min(ys) + max(ys)) // 2
    points = [(x + ox, y + oy) for x, y in points]

    coil = _segment_rects(layer, points, half)

    # The INNER ESCAPE: a via post onto the inner end, a span on the other metal running back the way
    # the first side came so that it clears every turn, a second via post, and a landing pad on the
    # coil's own metal so BOTH terminals of this cell are on one layer and abut the same things.
    inner_x, inner_y = points[0]
    coil_min_x = min(x for x, _ in points) - half
    out_x      = coil_min_x - lead
    coil.append(Rect(layer, out_x, inner_y - half, out_x + w, inner_y + half))   # landing pad

    # Drawn as overlapping rectangles and emitted as the regions they actually form - one continuous
    # winding, plus the landing pad, which the escape reaches by via and so is deliberately a
    # separate piece of metal. The rectangles are not the cell's answer; they are how it is easiest
    # to say. (KIT_MLIN is one rectangle and is emitted as one: there is nothing to merge, and a
    # union that turns a rectangle into a polygon buys nothing.)
    shapes = _merged(layer, coil)
    shapes.append(Rect(via,    inner_x - half, inner_y - half, inner_x + half, inner_y + half))
    shapes.append(Rect(bridge, out_x,          inner_y - half, inner_x + half, inner_y + half))
    shapes.append(Rect(via,    out_x,          inner_y - half, out_x + w,      inner_y + half))

    diagnostics = []
    if s < w // 4:
        diagnostics.append(
            f"turn spacing {s} is under a quarter of the turn width {w}: inter-turn capacitance "
            "will dominate and the self-resonant frequency will be low")
    if tech.layers and tech.layer_named(coil_name) is None:
        diagnostics.append(
            f"this technology has no layer named '{coil_name}'; the coil was drawn on this kit's "
            "own fallback layer for that name instead")

    outer_x, outer_y = points[-1]
    prev_x, prev_y   = points[-2]
    outward = (0.0 if outer_x > prev_x else 180.0) if outer_x != prev_x else \
              (90.0 if outer_y > prev_y else 270.0)

    return Result(
        shapes=shapes,
        pins=[
            # Pin 1 is the tip of the outer lead, facing away from the coil along the side it
            # continues - so it moves with `Turns`, a quarter of a lap at a time. Pin 2 is the far
            # end of the inner escape, on the coil metal like pin 1.
            Pin("1", outer_x, outer_y, layer, w, outward),
            Pin("2", out_x,   inner_y, layer, w, 180.0),
        ],
        handles=[
            # Both measure from the cell's own centre, which is where re-centring put it. `min` is in
            # SI METRES - a bound is a parameter VALUE, and a parameter value on the host's side of
            # the wire is SI, however the generator received it. See RULE 3.
            Handle("Inner", anchor=(0, 0), at=(dbu(inner / 2) + ox, oy), axis=0,
                   label="inner opening", min=MIN_FEATURE_M),
            Handle("Width", anchor=(0, 0), at=(ox, -a - half + oy), axis=270,
                   label="turn width", min=MIN_FEATURE_M),
        ],
        diagnostics=diagnostics,
    )


if __name__ == "__main__":
    run()
