#!/usr/bin/env python3
"""Example PCell generators: a microstrip line, two spiral inductors and a MIM capacitor.

This is the ARTWORK half of a PDK — the half that turns parameters into geometry. circuitRF starts
this file as an ordinary Python process and talks to it over a pipe; `pcell-generators.json` beside
this file is what points circuitRF at it.

Everything a generator needs is in the two arguments it is handed:

    params  the instance's parameter values, ALREADY CONVERTED to database units
    tech    the resolved technology - its layers, and the stackup underneath them

and everything it returns is in the `Result`: shapes, pins, optional parameter handles, and
optional diagnostics. There is no registration step and no second file: the `@generator` decorator
above each function is the declaration.

Read `Reference > PDK Authoring` in the in-app documentation for the full contract. The eleven
rules worth knowing before changing anything here are marked RULE below.
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

#: The MIM capacitor's own three names. `MIM Metal` is the thin top plate, 0.25 um above Metal1;
#: `MIM Via` is the post that carries it up to Metal2, which is the only metal it can reach. The
#: DIELECTRIC is not in this list on purpose - it is a STACKUP layer declared `PresentWithLayer:
#: MIM Metal`, so it exists exactly where the top plate is drawn and there is nothing to draw for
#: it. A generator that drew one anyway would put a second dielectric under the first.
MIM_METAL = "MIM Metal"
MIM_VIA_LAYER = "MIM Via"
MIM_DIELECTRIC = "MIM Dielectric"

#: How a capacitor sits in the circuit, which on this process is a question about ARTWORK and not
#: only about wiring: a shunt part grounds one of its plates itself, through a hole drilled clean
#: through the 100 um of GaAs to the metal on the back. Nothing above Metal1 can reach that.
SERIES = "Series"
SHUNT = "Shunt"

#: The backside via that does it, and this process's geometry for one. A 60 um hole through the
#: wafer inside an 80 um pad is a big thing next to a 60 um capacitor, and it is meant to look like
#: one - it is the reason a shunt part is not just a series part with a wire on it.
GROUND_VIA_LAYER = "Backside Via"
GROUND_VIA_UM = 60.0
GROUND_PAD_UM = 80.0

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
FALLBACK_LAYERS = {"Metal1": Layer(1, 0), "Metal2": Layer(2, 0), "Via": Layer(3, 0),
                   "MIM Metal": Layer(9, 0), "MIM Via": Layer(10, 0),
                   "Backside Via": Layer(8, 0)}
FALLBACK_LAYER = FALLBACK_LAYERS["Metal1"]

#: The permittivity of free space, in F/m, and this process's MIM dielectric as a fallback for a
#: layout whose technology carries no stackup. Only `KIT_MIMCAP` uses them, and only to report a
#: DERIVED value - see `_mim_capacitance_pf` for why that arithmetic is allowed to exist at all in a
#: package that otherwise has no metres in it.
EPS0_F_PER_M = 8.8541878128e-12
MIM_EPSR_FALLBACK = 6.8
MIM_THICKNESS_UM_FALLBACK = 0.2


def _metal_parameter(default="Metal1"):
    """The layer-choice parameter the line and both coils carry.

    RULE 1: `choices` makes this a DROPDOWN in the Properties Inspector rather than a free-text box,
    and a `default` is what puts the parameter on a newly-placed instance AT ALL. A parameter
    declared with no default is not sent to the generator and does not appear in the instance's
    parameter list -- the generator falls back to whatever its own accessor call names, so the
    artwork still looks right and there is nothing on screen to edit. That is the single most
    confusing way to ship a parameterised cell, because every symptom points somewhere else.

    `KIT_MIMCAP` deliberately does NOT carry one: its stack is Metal1, the MIM dielectric and the
    MIM top plate, in that order, and there is no second arrangement of it to choose. A parameter
    offering a choice that does not exist is worse than no parameter.
    """
    return Parameter(
        "Metal", "string", default=default, label="metal layer", choices=COIL_METALS)


def _layer_named(tech, name):
    """The drawing layer `name` refers to, or the best thing available when it does not resolve."""
    return ((tech.layer_named(name) if name else None)
            or FALLBACK_LAYERS.get(name)
            or tech.signal_layer
            or FALLBACK_LAYER)


def _ring_of(shape):
    """One closed ring of (x, y) points for a `Rect` or a `Polygon` - what `clip` takes."""
    if isinstance(shape, Rect):
        return [(shape.x1, shape.y1), (shape.x2, shape.y1),
                (shape.x2, shape.y2), (shape.x1, shape.y2)]
    return [(shape.xy[i], shape.xy[i + 1]) for i in range(0, len(shape.xy), 2)]


def _merged(layer, shapes):
    """`shapes` unioned into as few filled regions as they actually form.

    RULE 2: **circuitRF performs the boolean, the generator does not.** `clip` hands the rings back
    over the same pipe the generate request came in on, computed by the same Clipper2 the layout
    editor's own Boolean commands use. A union written here in Python would be a second
    implementation of one rule, and two clippers that disagree by a database unit produce artwork
    that renders perfectly and is wrong.

    Why bother, when overlapping shapes already draw as one solid figure: they are one PICTURE,
    not one figure. Every consumer downstream sees the seams - a DRC width check measures each
    piece rather than the conductor, an EM extraction meshes across internal edges that carry no
    current, and an exported GDSII or Gerber carries every overlap into whatever reads it next. The
    union is what makes the cell one piece of metal to everything and not just to the eye.

    Outside a host - a generator imported and called directly, from a test or a REPL - there is
    nobody to ask, and `clip` says so rather than substituting a different answer. The pieces
    are then returned as they stand: the same region, drawn in more parts.
    """
    try:
        regions = clip("or", [_ring_of(s) for s in shapes])
    except HostUnavailable:
        return list(shapes)

    merged = []
    for region in regions:
        merged.append(Polygon(
            layer,
            [c for point in region.outer for c in point],
            [[c for point in hole for c in point] for hole in region.holes] or None))
    return merged


def _um_dbu(micrometres, tech):
    """One of this kit's own process dimensions, in the database units of the layout being drawn into.

    `tech.dbu_per_micron` is handed over for exactly this and nothing else: a constant the generator
    itself holds, in micrometres, with no other way of becoming a coordinate. Length PARAMETERS are
    converted by circuitRF before they arrive and are never converted here.
    """
    return dbu(micrometres * (tech.dbu_per_micron or 1000))


def _min_feature_dbu(tech):
    """`MIN_FEATURE_UM` in the database units of the layout being drawn into."""
    return _um_dbu(MIN_FEATURE_UM, tech)


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


# ─────────────────────────────────────────────────────────────────────────────
#  The two spirals. They are ONE cell with two centre lines: everything below
#  `_spiral_cell` - the outer lead, the re-centring, the metal, the crossover,
#  the pins, the grips and the diagnostics - is shared, and each generator
#  supplies only the walk its own shape makes.
# ─────────────────────────────────────────────────────────────────────────────


def _spiral_centreline(a, pitch, turns):
    """The corner points of a SQUARE spiral's CENTRE LINE, innermost end first.

    A square spiral is a walk: go straight, turn ninety degrees, go straight again, with every side
    one pitch longer than the side two before it. That single rule is what keeps the gap between
    adjacent turns equal to `pitch` the whole way round, and it is why the walk - not a stack of
    concentric rings - is the construction to use.

    **The walk is also what makes a fractional turn mean anything.** `turns` is multiplied by four to
    get quarter turns; the whole ones are walked in full and whatever is left over is walked as a
    partial side. A count of 3.5 stops half way along the fourteenth quarter, and the outer terminal
    is wherever that leaves it. Nothing here special-cases it.

    `a` is the innermost side's distance from the centre - its inradius - and it means the same thing
    in `_octagon_centreline`, which is what lets one cell body drive both.

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


#: A regular octagon's inradius over its circumradius. Only `_octagon_centreline` needs it, and only
#: to say where a corner is; the SIDES are placed by their own distance from the centre, which is
#: what makes the gap between adjacent turns exact rather than nearly right.
_OCT_APOTHEM = math.cos(math.pi / 8)

#: The outward normal of the innermost side. 270 degrees points south, so that side runs due EAST
#: and the walk leaves the middle heading the same way the square one does - which is what puts the
#: inner terminal on the same side of the coil, so the crossover below needs no second version.
_OCT_FIRST_NORMAL_DEG = 270.0


def _octagon_centreline(a, pitch, turns):
    """The corner points of an OCTAGONAL spiral's centre line, innermost end first.

    Same walk, eight headings instead of four: each side turns forty-five degrees from the last, so
    four of them are axial and four are diagonal. `turns` is multiplied by EIGHT rather than four,
    and a fraction is walked as a partial side exactly as it is for the square - which is why the
    outer terminal moves an eighth of a lap at a time here and a quarter there.

    **The sides are placed by their distance from the centre, not by their corners.** Side *k* is the
    line whose outward normal is `270 + 45k` degrees at distance `a + k*pitch/8`; a corner is then
    simply where two consecutive sides cross. Stated that way the turn-to-turn gap is exactly `pitch`
    by construction - side *k* and side *k+8* are parallel and `pitch` apart, which is the property
    the whole coil exists to hold. Walking corner-to-corner along rays from the centre instead looks
    identical and is NOT exact: the two ends of a side then sit at slightly different radii, so
    adjacent turns are very slightly non-parallel and the gap drifts across the side.

    Why an octagon at all, measured on this kit's own default coil at three turns: the winding is
    14% shorter than the square one and encloses 11% less area, so it gives up rather less inductance
    than it saves in conductor. On top of that arithmetic, current in a square spiral crowds on the
    inside of each ninety-degree bend; a forty-five-degree one spreads it, which is where the rest of
    the difference in Q comes from and is not something these coordinates can show you.
    """
    sides    = turns * 8.0
    whole    = int(math.floor(sides + 1e-9))
    leftover = sides - whole

    def corner(k):
        """Where side *k-1* crosses side *k*."""
        psi_a = math.radians(_OCT_FIRST_NORMAL_DEG + 45.0 * (k - 1))
        psi_b = math.radians(_OCT_FIRST_NORMAL_DEG + 45.0 * k)
        da    = a + (k - 1) * pitch / 8.0
        db    = a + k * pitch / 8.0
        den   = math.sin(psi_b - psi_a)
        x = (da * math.sin(psi_b) - db * math.sin(psi_a)) / den
        y = (db * math.cos(psi_a) - da * math.cos(psi_b)) / den
        return dbu(x), dbu(y)

    points = [corner(k) for k in range(whole + 1)]
    if leftover > 1e-9:
        (x1, y1), (x2, y2) = points[-1], corner(whole + 1)
        points.append((dbu(x1 + (x2 - x1) * leftover), dbu(y1 + (y2 - y1) * leftover)))
    return points


def _extend_last_side(points, lead):
    """Append the OUTER LEAD: `lead` further along the direction the last side was already going.

    RULE 8: the lead continues the winding rather than hanging off it. It used to be a stub on the
    middle of the outermost side instead, and that is worth knowing about because the artwork looked
    right: metal continued from the lead in BOTH directions, one way spiralling inward and the other
    running most of a lap to the winding's real free end and stopping. A dead-ended stub, drawn in
    the same metal as the coil, that nothing in the picture distinguishes from the inductor.

    Written as a direction rather than as a sign test on the two coordinates, because an octagonal
    spiral's last side is as often diagonal as axial. On an axial side the two agree exactly.
    """
    (px, py), (qx, qy) = points[-2], points[-1]
    dx, dy = qx - px, qy - py
    span   = math.hypot(dx, dy)
    return points + [(dbu(qx + dx / span * lead), dbu(qy + dy / span * lead))]


def _outward_deg(previous, last):
    """The direction `last` continues in, in degrees, as a pin's outward angle in [0, 360)."""
    (px, py), (qx, qy) = previous, last
    return math.degrees(math.atan2(qy - py, qx - px)) % 360.0


def _advanced(point, deg, distance):
    """`point` moved `distance` in the direction `deg` - how a centre line's last point becomes the
    METAL's own end face, which is where a pin belongs (RULE 11)."""
    x, y = point
    return dbu(x + math.cos(math.radians(deg)) * distance), dbu(y + math.sin(math.radians(deg)) * distance)


def _segment_runs(layer, points, half):
    """One filled piece per straight run of `points`, each offset by `half` to either side.

    Every run also reaches PAST each of its ends, by just enough to close the joint there: at a
    corner turning by `t`, the two outer faces cross `half*tan(t/2)` beyond the corner, so reaching
    exactly that far makes the two pieces meet on the mitre with nothing left over. At ninety degrees
    that is `half`, which is the same thing as growing a rectangle by half a width on every side -
    which is why a square spiral's artwork comes out of this unchanged. At forty-five it is 0.41 of a
    width, and reaching `half` there instead leaves a small spur of metal sticking out of every
    corner: the union is still one conductor and the spur is still metal nobody asked for.

    A free end - the outer lead's tip, or the first side's - has no joint to close, so it reaches
    `half` past the centre line's end and is cut square, exactly as a rectangle would.

    An axial run is emitted as a `Rect` and a diagonal one as the four-cornered `Polygon` it is.
    That is not a special case so much as the absence of one: a rectangle is what an axis-aligned
    quad is called, and saying so keeps a square spiral's artwork rectangles all the way to the
    clipper. They are unioned before they leave the generator - see `_merged`.
    """
    runs = [(p, q) for p, q in zip(points, points[1:]) if p != q]
    units = []
    for (x1, y1), (x2, y2) in runs:
        span = math.hypot(x2 - x1, y2 - y1)
        units.append(((x2 - x1) / span, (y2 - y1) / span))

    def overhang(i, j):
        """How far run `i` reaches past the corner it shares with run `j`."""
        if not 0 <= j < len(runs):
            return float(half)                      # a free end: cut square, half a width past
        (ax, ay), (bx, by) = units[i], units[j]
        turn = abs(math.atan2(ax * by - ay * bx, ax * bx + ay * by))
        # Two runs in a straight line have no corner to mitre, and the formula below would have them
        # abut exactly. Reaching `half` there instead simply doubles back over the same band, which
        # gives the clipper an overlap rather than a shared edge. The outer lead is this case.
        if turn < 1e-9:
            return float(half)
        return half * math.tan(min(turn, math.pi / 2) / 2)

    shapes = []
    for i, ((x1, y1), (x2, y2)) in enumerate(runs):
        ux, uy = units[i]
        start, finish = overhang(i, i - 1), overhang(i, i + 1)
        ax, ay = x1 - ux * start,  y1 - uy * start
        bx, by = x2 + ux * finish, y2 + uy * finish
        nx, ny = -uy * half, ux * half       # the offset to either side
        corners = [(ax + nx, ay + ny), (bx + nx, by + ny),
                   (bx - nx, by - ny), (ax - nx, ay - ny)]
        xs = [dbu(x) for x, _ in corners]
        ys = [dbu(y) for _, y in corners]
        if x1 == x2 or y1 == y2:
            shapes.append(Rect(layer, min(xs), min(ys), max(xs), max(ys)))
        else:
            shapes.append(Polygon(layer, [c for xy in zip(xs, ys) for c in xy]))
    return shapes


def _spiral_cell(params, tech, centreline):
    """Everything both spirals do, which is everything except the shape of the walk.

    `centreline` is `_spiral_centreline` or `_octagon_centreline` - one function of
    `(a, pitch, turns)`, where `a` is the innermost side's distance from the centre. Nothing below
    asks which it was handed, and nothing below needs to: the lead, the crossover, the pins and the
    grips are all stated in terms of the walk's own points.
    """
    floor = _min_feature_dbu(tech)
    w     = _at_least("Width", params.length("Width", 10_000), floor, tech)
    s     = _at_least("Space", params.length("Space",  8_000), floor, tech)
    inner = _at_least("Inner", params.length("Inner", 120_000), floor, tech)
    # RULE 9: Turns is DIMENSIONLESS, not a length. circuitRF scales a length from SI metres into
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
    # The innermost side's distance from the centre. `Inner` is the CLEAR opening across it: the
    # side's inner face sits at inner/2. The opening measured across the NEXT pair of sides comes out
    # wider, which is what a spiral does - the turn has to step outward somewhere, and it steps once
    # per lap however many sides that lap has.
    a     = dbu(inner / 2) + half
    lead  = 2 * w

    points = _extend_last_side(centreline(a, pitch, turns), lead)

    # Re-centre on the winding's own bounding box, so the cell sits where it is placed rather than
    # drifting as `Turns` grows. Everything below is in the re-centred frame.
    xs = [x for x, _ in points]
    ys = [y for _, y in points]
    ox = -(min(xs) + max(xs)) // 2
    oy = -(min(ys) + max(ys)) // 2
    points = [(x + ox, y + oy) for x, y in points]

    coil = _segment_runs(layer, points, half)

    # The INNER ESCAPE: a via post onto the inner end, a span on the other metal running back the way
    # the first side came so that it clears every turn, a second via post, and a landing pad on the
    # coil's own metal so BOTH terminals of this cell are on one layer and abut the same things.
    #
    # RULE 10: THE LANDING PAD RUNS PAST THE VIA, AND THE OVERHANG IS THE WHOLE REASON IT IS THERE.
    # The pad used to be exactly the via's own footprint, with the bridge ending on top of it - so
    # every point of it carried metal on BOTH conductor levels at once. An EM setup refuses a port
    # standing there by name ("a port's LEVEL is part of its identity: driving the wrong one drives a
    # different conductor with the same footprint"), which made pin 2 a terminal nothing could be
    # driven from. `lead` of clean single-level metal past the via is what a port needs, and it costs
    # two turn widths of artwork.
    inner_x, inner_y = points[0]
    coil_min_x = min(x for x, _ in points) - half
    out_x      = coil_min_x - lead          # where the bridge and its via post stop
    pad_x      = out_x - lead               # ...and where the pad, and therefore pin 2, reaches
    coil.append(Rect(layer, pad_x, inner_y - half, out_x + w, inner_y + half))   # landing pad

    # Drawn as overlapping pieces and emitted as the regions they actually form - one continuous
    # winding, plus the landing pad, which the escape reaches by via and so is deliberately a
    # separate piece of metal. The pieces are not the cell's answer; they are how it is easiest
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

    # RULE 11: A PIN SITS ON THE METAL'S OWN EDGE, NOT ON THE END OF THE CENTRE LINE.
    # `points` is a centre line and `_segment_runs` cuts a free end square `half` PAST its last
    # point, so pin 1 used to sit half a turn width inside the metal while pin 2 - on the landing
    # pad's drawn edge - sat exactly on it. That asymmetry is not cosmetic: an EM port takes its
    # width from the cell's pin only when it lands on the pin EXACTLY, and a user aiming at the
    # visible end of the metal lands on the edge, five micrometres short. The port then falls back to
    # the INSTANCE's bounding box and reports the whole coil's 250 um as its excitation width, while
    # the other port - the one whose pin is on its edge - reports the 10 um it should. One port right
    # and one port 25x wrong, from a pin half a line width out of place. KIT_MLIN has always put its
    # pins on the rectangle's own faces; this is the same rule, stated for a walk.
    outward_deg = _outward_deg(points[-2], points[-1])
    outer_x, outer_y = _advanced(points[-1], outward_deg, half)

    return Result(
        shapes=shapes,
        pins=[
            # Pin 1 is the tip of the outer lead, facing away from the coil along the side it
            # continues - so it moves with `Turns`, one side of the walk at a time. Pin 2 is the far
            # end of the inner escape, on the coil metal like pin 1.
            Pin("1", outer_x, outer_y, layer, w, outward_deg),
            Pin("2", pad_x,   inner_y, layer, w, 180.0),
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


#: The five parameters both coils take. One list, declared once: two cells that differ only in the
#: shape of their walk must not be able to drift into offering different parameters for it.
_SPIRAL_PARAMETERS = [Parameter("Width", "real", LENGTH, 10e-6, minimum=MIN_FEATURE_M),
                      Parameter("Space", "real", LENGTH,  8e-6, minimum=MIN_FEATURE_M),
                      Parameter("Inner", "real", LENGTH, 120e-6, minimum=MIN_FEATURE_M),
                      Parameter("Turns", "real", default=3.0, minimum=0.25),
                      _metal_parameter()]


@generator("KIT_SPIRAL", _SPIRAL_PARAMETERS)
def spiral(params, tech):
    """A square spiral inductor - five parameters that interact, which is what a parameterised
    cell actually buys you."""
    return _spiral_cell(params, tech, _spiral_centreline)


@generator("KIT_OSPIRAL", _SPIRAL_PARAMETERS)
def octagonal_spiral(params, tech):
    """The same inductor with its corners cut - the same five parameters, meaning the same things,
    and the same cell body underneath. Only the walk differs."""
    return _spiral_cell(params, tech, _octagon_centreline)


# ─────────────────────────────────────────────────────────────────────────────
#  The MIM capacitor.
# ─────────────────────────────────────────────────────────────────────────────


def _mim_stack(tech):
    """This process's MIM dielectric as `(relative permittivity, thickness in micrometres)`.

    Read out of the STACKUP rather than stated here, because it is the stackup's to say: the
    `.ctech` is what an EM extraction and a DRC deck already agree with, and a second copy of 6.8 in
    this file is a number that can go stale without anything noticing. The fallback is for the
    layout that resolves no technology at all, which still draws.
    """
    stack = tech.stackup.named(MIM_DIELECTRIC) if tech.stackup else None
    if stack is None or not stack.thickness or not stack.epsr:
        return MIM_EPSR_FALLBACK, MIM_THICKNESS_UM_FALLBACK
    return stack.epsr, stack.thickness / float(tech.dbu_per_micron or 1000)


def _mim_capacitance_pf(tech, area_dbu2):
    """The parallel-plate capacitance of `area_dbu2` of top plate, in picofarads.

    **This is the one place in the kit that turns coordinates back into a physical size**, and it is
    allowed to because `tech.dbu_per_micron` is handed over for exactly this: a constant the
    generator itself holds, in micrometres, with no other way of becoming a number. Length
    PARAMETERS are still converted by circuitRF and never here.

    Fringing is not in it. A MIM plate is 0.2 um from its counter-electrode and tens of micrometres
    across, so the parallel-plate value is within a percent or so of the truth - and the honest
    reason to report it is that it tracks the artwork, not that it replaces a solve.
    """
    per_um = float(tech.dbu_per_micron or 1000)
    epsr, thickness_um = _mim_stack(tech)
    area_um2 = area_dbu2 / (per_um * per_um)
    return EPS0_F_PER_M * epsr * area_um2 / thickness_um * 1e6


def _reflected(shapes, pins, axis):
    """`shapes` and `pins` mirrored about the vertical line `x = axis`.

    A polygon's ring is reversed as well as mirrored, because reflecting a ring reverses the
    direction it winds in and a region that comes back wound the other way is a HOLE.
    """
    out = []
    for shape in shapes:
        if isinstance(shape, Rect):
            out.append(Rect(shape.layer, axis - shape.x2, shape.y1, axis - shape.x1, shape.y2))
            continue

        def ring(xy):
            points = [(axis - xy[i], xy[i + 1]) for i in range(0, len(xy), 2)]
            return [c for point in reversed(points) for c in point]

        out.append(Polygon(shape.layer, ring(shape.xy),
                           [ring(hole) for hole in shape.holes] if shape.holes else None))

    return out, [Pin(p.name, axis - p.x, p.y, p.layer, p.width, (180.0 - p.outward_deg) % 360.0)
                 for p in pins]


@generator("KIT_MIMCAP", [Parameter("W", "real", LENGTH, 60e-6, minimum=MIN_FEATURE_M),
                          Parameter("L", "real", LENGTH, 60e-6, minimum=MIN_FEATURE_M),
                          Parameter("Connection", "string", default=SERIES,
                                    label="series or shunt", choices=(SERIES, SHUNT)),
                          Parameter("C", "real", default=1.0838,
                                    label="capacitance (pF)", computed=True)])
def mimcap(params, tech):
    """A metal-insulator-metal capacitor: a Metal1 bottom plate, the MIM dielectric, and a MIM Metal
    top plate that escapes on Metal2. `Connection` decides whether it is drawn in SERIES with a line
    or SHUNT to ground.

    **Nothing here draws the dielectric.** It is a stackup layer declared present wherever `MIM
    Metal` is drawn, so the top plate IS the statement that it is there; a drawn one would be a
    second insulator under the first. Look at `tech/mmic-GaAs_2LM_100um.ctech` - `MIM Dielectric`
    carries no drawing layer at all, and that is what `PresentWithLayer` means.

    **A shunt capacitor is not a series one with a wire on it**, which is the whole reason this is a
    parameter on the cell rather than something to draw around it:

    - It grounds a plate ITSELF, through a hole drilled clean through the 100 um of GaAs onto the
      metal on the back. Nothing above Metal1 can reach that, so the ground terminal has to be on
      the plate that lies on the substrate - and the via and its pad are a big thing beside a 60 um
      capacitor, not an afterthought.
    - Which plate that is matters. The GROUNDED one has to be the bottom plate: it is the one facing
      the substrate, and grounding it shorts out the plate-to-backside capacitance that would
      otherwise hang off the signal node through 100 um of er 12.9. Ground the top plate instead and
      the cell still works, still draws, and carries a parasitic nobody declared.
    - So the signal terminal is the TOP plate's, and pin 1 lives at the cell origin (RULE 6). That
      is the other end of the cell from where the series part's pin 1 is, so the shunt cell is the
      series cell REFLECTED, with the ground via added to the plate whose own terminal it replaces.
      One layout routine, read in either direction.
    """
    floor = _min_feature_dbu(tech)
    w     = _at_least("W", params.length("W", 60_000), floor, tech)
    l     = _at_least("L", params.length("L", 60_000), floor, tech)
    shunt = params.text("Connection", SERIES) == SHUNT

    plate   = _layer_named(tech, COIL_METALS[0])    # Metal1: the bottom plate, on the GaAs
    top     = _layer_named(tech, MIM_METAL)         # the thin top plate, 0.2 um above it
    strap   = _layer_named(tech, COIL_METALS[1])    # Metal2: the only metal the top plate can reach
    mim_via = _layer_named(tech, MIM_VIA_LAYER)
    via     = _layer_named(tech, VIA_LAYER)

    half = w // 2           # RULE 5 again: integer arithmetic, so an odd width straddles the axis
    enc  = floor            # the bottom plate encloses the top plate by one minimum feature

    # W x L is the TOP plate, because that is the plate that sets the capacitance; the bottom plate
    # is it grown by `enc` on every side. Stated the other way round, `C` would depend on a process
    # enclosure rule nobody typed.
    top_x1, top_x2 = enc, enc + l
    bottom_x2      = l + 2 * enc
    bottom = Rect(plate, 0, -(half + enc), bottom_x2, half + enc)
    shapes = [Rect(top, top_x1, -half, top_x2, half)]

    # The escape, which is the spiral's crossover one storey higher: a via post off the top plate, a
    # span on the metal above, a second post, and a landing pad back on Metal1 so BOTH terminals of
    # this cell are on one layer and abut the same things. The top plate can reach nothing else -
    # `MIM Via` spans MIM Metal to Metal2 and nothing spans MIM Metal to Metal1, which is the whole
    # reason the bridge goes up and over rather than sideways.
    bridge = min(2 * half, 4 * floor)           # 16 um across the default plate, never wider than it
    post   = min(max(floor, bridge - 2 * enc), l, 2 * half)
    inset  = min(enc, l - post)                 # off the plate's east edge, where there is room for it
    post_x2 = top_x2 - inset
    shapes.append(Rect(mim_via, post_x2 - post, -(post // 2), post_x2, post - post // 2))

    # Clear of the bottom plate by twice the minimum spacing: at exactly the minimum a DRC deck is
    # entitled to call it, and a cell that ships its own violation is a cell nobody trusts.
    pad_x1 = bottom_x2 + 2 * floor
    pad_x2 = pad_x1 + 2 * floor
    # RULE 10 again, and for the same reason the spirals' landing pad carries it: the Metal1 pad
    # reaches PAST the via and past the end of the strap above it, so its outer end is metal on one
    # conductor level only and a port may stand there. Drawn on top of each other, every point of the
    # terminal carries Metal1 and Metal2 at once and an EM setup refuses a port on it by name.
    pad_x3 = pad_x2 + bridge
    shapes.append(Rect(strap, post_x2 - post, -(bridge // 2), pad_x2, bridge - bridge // 2))
    shapes.append(Rect(via,   pad_x1,         -(bridge // 2), pad_x2, bridge - bridge // 2))
    shapes.append(Rect(plate, pad_x1,         -(bridge // 2), pad_x3, bridge - bridge // 2))

    diagnostics = []

    if shunt:
        ground_pad = _um_dbu(GROUND_PAD_UM, tech)
        ground_via = _um_dbu(GROUND_VIA_UM, tech)
        arm_x2     = enc                        # into the plate, so the two are one piece of metal
        arm_x1     = arm_x2 - ground_pad
        arm_y1     = -(ground_pad // 2)
        arm_y2     = arm_y1 + ground_pad
        drill      = (ground_pad - ground_via) // 2

        # The pad and the plate overlap, so they go through the clipper for the reason everything
        # else here does not: these two ARE one conductor, and the seam between them would be
        # measured by a width check and meshed by an extraction. The rest of this cell's metal is
        # disjoint rectangles that stay rectangles.
        metal = _merged(plate, [bottom, Rect(plate, arm_x1, arm_y1, arm_x2, arm_y2)])
        metal.append(Rect(_layer_named(tech, GROUND_VIA_LAYER),
                          arm_x1 + drill, arm_y1 + drill,
                          arm_x1 + drill + ground_via, arm_y1 + drill + ground_via))

        pins = [Pin("1", pad_x3, 0, plate, bridge,     0.0),     # the top plate: the signal
                Pin("2", arm_x1, 0, plate, ground_pad, 180.0)]   # the bottom plate: ground
        shapes, pins = _reflected(metal + shapes, pins, pad_x3)
        bottom_x1, bottom_x2 = pad_x3 - bottom_x2, pad_x3        # the same plate, read back the other way

        if tech.layers and tech.layer_named(GROUND_VIA_LAYER) is None:
            diagnostics.append(
                f"this technology has no layer named '{GROUND_VIA_LAYER}', so the ground terminal "
                "was drawn on this kit's own fallback layer for it; nothing here can check that it "
                "reaches a ground plane")
    else:
        shapes = [bottom] + shapes
        pins = [Pin("1", 0,      0, plate, 2 * (half + enc), 180.0),   # the bottom plate's own edge
                Pin("2", pad_x3, 0, plate, bridge,             0.0)]   # the top plate, brought down
        bottom_x1 = 0

    if tech.layers and tech.layer_named(MIM_METAL) is None:
        diagnostics.append(
            f"this technology has no layer named '{MIM_METAL}'; the top plate was drawn on this "
            "kit's own fallback layer for that name instead, and the capacitance below is this "
            "kit's own dielectric rather than this technology's")
    if tech.stackup and tech.stackup.named(MIM_DIELECTRIC) is None:
        diagnostics.append(
            f"this technology's stackup has no '{MIM_DIELECTRIC}' layer; the reported capacitance "
            f"assumes er = {MIM_EPSR_FALLBACK:g} over {MIM_THICKNESS_UM_FALLBACK:g} um")

    # Stated in the frame the cell ended up in rather than the one it was drawn in, so one pair of
    # grips serves both connections: the bottom plate's near edge does not move when `L` changes and
    # its far edge does, whichever way round the cell was finally laid out.
    middle = (bottom_x1 + bottom_x2) // 2
    return Result(
        shapes=shapes,
        pins=pins,
        handles=[
            Handle("L", anchor=(bottom_x1, 0), at=(bottom_x2, 0), axis=0,
                   label="plate length", min=MIN_FEATURE_M),
            Handle("W", anchor=(middle, 0), at=(middle, half + enc), axis=90,
                   label="plate width", min=MIN_FEATURE_M),
        ],
        # What makes `C` a readout rather than a stale number in a box nobody can type into: the
        # declaration says it is derived, and this says what it was derived TO for this artwork. With
        # the declaration alone circuitRF can only keep showing whatever the instance was stored
        # with, while the geometry that determines it moves underneath. Measured off the plate that
        # was actually DRAWN, not off W x L, so integer halving is in it like everything else. It is
        # the same number in series and in shunt: the plates did not move, only what they face.
        computed={"C": _mim_capacitance_pf(tech, (top_x2 - top_x1) * (2 * half))},
        diagnostics=diagnostics,
    )


if __name__ == "__main__":
    run()
