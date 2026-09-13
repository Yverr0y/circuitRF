---
title: AN-01 — Coupled Lines, EM Ports, terminations and de-embedding
slug: app-notes/an01-ports-and-coupling.html
doc-kind: Application Note
breadcrumb: Docs > App Notes > AN-01 Coupled lines and EM ports
lede: How to EM-simulate a pair of coupled lines — which kernel, where the ports go, what circuitRF does when two feeds are coupled at the same reference plane, and how to read the run.
keywords: coupled lines, coupler, directional coupler, S11, ports, coupling, de-embedding, calibration group, modal error box, even mode, odd mode, passivity, EM, termination, port feed, cross-section kernel
---

<nav class="toc">
<h2>On this page</h2>
<ol>
<li><a href="#structure">The structure</a></li>
<li><a href="#kernel">Choosing the kernel</a></li>
<li><a href="#group">Coupled feeds are calibrated together</a></li>
<li><a href="#results">The result</a></li>
<li><a href="#refused">When a group cannot be formed, and what to do</a>
  <ul>
  <li><a href="#passive">A neighbour with no port on it</a></li>
  <li><a href="#moveports">Move the ports, not the circuit</a></li>
  <li><a href="#isolate">Give every port an isolated feed</a></li>
  </ul></li>
<li><a href="#practice">Best practice</a></li>
<li><a href="#compare">Comparing two port configurations properly</a></li>
<li><a href="#checklist">Checklist before you trust an EM result</a></li>
</ol>
</nav>

## The structure {#structure}

Two parallel microstrips over a ground plane. Each is **254 µm wide and 3.83 mm long**, and they are
**246 µm apart** edge to edge, on 0.9 mm FR-4 (ε<sub>r</sub> 4.4, tan δ 0.02). Ports 1 and 2 are the
ends of the lower line, ports 3 and 4 the ends of the upper one. The sweep is 1–7 GHz.

{{ui: an01-coupled-pair-4port}}

Every number and every curve on this page came out of this design or one of its siblings in
`testdata/portcal/`. One command runs it:

<pre>
circuitrf em testdata/portcal/coupled-pair/em/coupled-pair.cem
</pre>

## Choosing the kernel {#kernel}

Two straight coupled lines are a **uniform cross-section**, and circuitRF has a
[kernel](../reference/mom-engine.html) for exactly that: it solves the section per unit length and
has no de-embedding step to get wrong. Leaving Analysis on `Auto` picks it and says so:

<pre>
Automatic chose "Uniform transmission line": this geometry is a uniform cross-section, which
that analysis solves exactly and is about a thousand times cheaper than "Full-wave planar".
</pre>

On this pair that is **0.16 s against 9.0 s** for the same seven points on ten cores. So the first
question to ask about any coupled-line structure is whether it is uniform end to end. If it is —
straight coupled lines, a broadside pair, a single transmission line — you are done, and nothing
below applies.

**Reach for the full-wave planar kernel when the cross-section is not uniform**: a coupler with bends
or tapers at its ends, lines that fan in and out, anything with a pad or a stub on it. That kernel
does have a de-embedding step, and the rest of this note is about the one part of it a coupled-line
layout makes interesting.

<div class="callout note">
<span class="label">Why this page forces the expensive kernel</span>
<p>The fixture's <code>.cem</code> sets <code>Analysis: Full-wave planar</code> deliberately. Because
the cheap kernel solves this geometry essentially exactly, it is an <b>oracle</b> — running both on
one unmodified file measures the full-wave kernel's de-embedding and nothing else. That is what the
agreement figures below are.</p>
</div>

## Coupled feeds are calibrated together {#group}

This is the part that is specific to coupled lines, and **it needs no action from you** — but it is
worth knowing what circuitRF is doing, because it decides what the run can and cannot accept.

The planar kernel de-embeds each port by solving **calibration standards** beside your structure and
peeling the port discontinuity off the raw answer. A standard is a uniform extrusion of whatever
crosses the reference plane. For a single isolated line that is one conductor, one mode, and an error
box that is one number per port.

**Two driven conductors at one plane are not that.** They support **two modes** there — with
different propagation constants and different characteristic impedances — and no pair of per-port
scalars can describe them. So the unit of calibration stops being the port and becomes the **group**:

- **one standard carrying both conductors**, built from your own mesh at your own gridlines, with a
  port on each conductor at each end — a four-port standard for a pair;
- **one modal error box**, whose blocks are matrices rather than scalars;
- **one γ and one Z<sub>c</sub> per mode**, reported per frequency.

The run says so by name, and prints what it extracted:

<pre>
Ports 1, 3 form one CALIBRATION GROUP: their feeds are mutually coupled at the reference plane,
the nearest pair 246 µm apart. They share one 2-conductor calibration standard and one MODAL
error box of 2×2 blocks, because 2 coupled conductors support 2 modes there and a per-port
scalar box cannot represent them. The profile spans 0 µm to 754 µm across.
  1 GHz, ports 1+3: modes 40.2° / ε_eff 2.718 / Z_c 71.53Ω · 42.8° / ε_eff 3.073 / Z_c 157.51Ω;
  separation 2.55°, …
</pre>

### What this means for you {#group-meaning}

- **The modes are extracted from the standard, never assumed.** Even and odd is right for two
  identical lines and wrong for almost anything else, so nothing here assumes it — an asymmetric pair
  and a group of three unequal conductors are the same code path.
- **The two numbers in that line are the design quantities.** 71.53 Ω and 157.51 Ω with
  ε<sub>eff</sub> 2.718 and 3.073 are this pair's own modal impedances and effective permittivities,
  per frequency, for free, on every run that forms a group.
- **Mode separation is the number everything rests on**, and it is reported on every point. If two
  modes are too close to tell apart (the floor is 0.5° of electrical length) the run **refuses**
  rather than publishing a smooth, plausible answer built on a modal basis decided by round-off.
  Three conductors of *equal* width at this spacing are refused for exactly that reason — 0.369°
  apart at 1 GHz.
- **It costs almost nothing.** The standard carries the same metal either way, so it is the same mesh
  and the same ~9× the DUT's unknowns; the four-port coupled pair runs about 13 % slower than the
  two-port case.
- **Groups hold up to three conductors.** More than that is refused by name rather than discovered as
  a run time.

<div class="callout note">
<span class="label">Where a group forms</span>
<p>Automatically, wherever two or more <b>de-embedded ports at the same reference plane, facing the
same way, on the same conductor level</b> sit closer together than <b>5 substrate heights</b>. On a
board that is the near end of a coupler, the far end of a coupler, and the two halves of a
differential pair. The ports at each plane group with each other and with nothing else — this design
gets two groups, 1+3 and 2+4.</p>
</div>

## The result {#results}

Same file, same mesh, 1–7 GHz at 200 MHz — plotted in the Data Display exactly as any other result.

{{ui: an01-coupled-pair-return-loss}}

{{ui: an01-coupled-pair-through-coupled}}

A 254 µm line on 0.9 mm FR-4 is about 116 Ω, so against a 50 Ω reference it is a mismatch that grows
with frequency: S(1,1) climbs from −17 dB to −5.7 dB across the band while S(2,1) falls from −0.14 dB
to −2.1 dB. S(3,1) — the backward-coupled port — rises from −20.8 dB to −11.9 dB.

**Ports 1 and 2 are the two ends of one uniform conductor, so S(1,1) and S(2,2) must coincide.** They
do, to every digit plotted. That is worth checking on your own structures: where they separate on
geometry that is symmetric, the mesh or the ports are not.

Against the cross-section kernel on the same unmodified file:

| f (GHz) | S(1,1) planar | S(1,1) exact | S(2,1) planar | S(2,1) exact | S(3,1) planar | S(3,1) exact |
|---|---|---|---|---|---|---|
| 1.0 | −17.00 dB | −19.09 dB | −0.14 dB | −0.11 dB | −20.84 dB | −22.22 dB |
| 2.0 | −12.40 dB | −13.54 dB | −0.42 dB | −0.36 dB | −16.71 dB | −16.78 dB |
| 3.0 |  −9.76 dB | −10.69 dB | −0.78 dB | −0.70 dB | −14.39 dB | −14.11 dB |
| 4.0 |  −8.11 dB |  −8.97 dB | −1.16 dB | −1.05 dB | −13.07 dB | −12.62 dB |
| 5.0 |  −7.02 dB |  −7.84 dB | −1.52 dB | −1.38 dB | −12.36 dB | −11.77 dB |
| 6.0 |  −6.26 dB |  −7.07 dB | −1.83 dB | −1.66 dB | −12.00 dB | −11.29 dB |
| 7.0 |  −5.73 dB |  −6.52 dB | −2.08 dB | −1.88 dB | −11.87 dB | −11.05 dB |

Worst-case **|ΔS| = 0.0454 over the whole matrix and the whole band**, against the **0.0521** the two
kernels differ by on the *same* conductors moved 9 mm apart — where there is no coupled feed and
nothing to get wrong. The de-embedding is inside the floor it is being measured against.

The run still reports a small **passivity excess** here — σ<sub>max</sub>(S) = 1.0023 at 1 GHz, on 4
of the 31 points plotted. Read it as the smoke alarm it is: [below](#practice) for what it is
reliable at and what it is not.

## When a group cannot be formed, and what to do {#refused}

A group is one standard cut at **one plane**, so every member has to cross that plane, running
straight, on the same level, facing the same way. Where that does not hold — the neighbour's own port
is somewhere *else* along the line, or the neighbour bends or ends inside the run the standard
reproduces, or it is on another conductor level, or the group would exceed three conductors — the run
is **refused**, and no Touchstone is written. This is `testdata/portcal/offset-pair` — the same two
lines, with the neighbour shortened so its ports sit at a different station:

<pre>
Calibrated port feeds are not isolated: port 1 has other metal 246 µm away (0.27 substrate
heights, against the 5 a neighbour that carries a port of its own needs)… Move the feed away
from its neighbour, or put the port where the line is already isolated.

Why this feed's calibration standard could not simply reproduce the neighbour: Port 3's feed
has a conductor 246 µm away carrying a port of its own (2 port(s) on it), none of which sits at
this reference plane facing the same way…
</pre>

**It refuses rather than warns because a `.sNp` on disk carries no notes.** Whoever opens that file
next — in the Data Display, in a circuit, next month — would see a plausible curve and nothing else.
The refusal is on a measurable geometric fact about the port, and it names both what it found and
which flag answers it. Two escapes exist in
[Solver options](../reference/em-setup.html#deembedding) and neither is quiet: turn **port
de-embedding off** and read the raw solve, which includes the port discontinuity; or turn on
**"de-embed outside the calibration's validity"**, which publishes and stamps the Touchstone with a
line recording that the calibration was applied outside the geometry it is valid for.

### A neighbour with no port on it {#passive}

The commonest case on a board is not two driven lines at all — it is a conductor that is simply near
your feed and carries no port: the other half of a pair you are not driving, an adjacent net, a
ground pour. **That needs nothing from you either.** One driven conductor at the plane means one
mode, so the error box stays scalar and the standard is simply **widened to contain the neighbour**,
copied from your own mesh with the gap reproduced as a gap and the neighbour driven by nothing:

<pre>
Its calibration standard reproduces 1 neighbouring conductor(s) beside the feed, the nearest
246 µm away, over the whole of the standard's run: the profile spans 0 µm to 754 µm across,
against 0 µm to 254 µm for the port's own conductor. The neighbour carries no port, so the error
box is still the scalar one; it is present in the standard and driven by nothing, exactly as it
is in the structure.
</pre>

Three things are still declined and each falls back to the refusal above: a neighbour that **bends,
ends or changes width** inside the standard's run (a standard is a uniform extrusion, and guessing
would invent metal you did not draw); a neighbour **on another conductor level**; and a neighbour
**carrying a port**, which is the [group](#group) case. There is one cost and one caveat: the
standards get about twice as large, which the run reports, and the reproduced neighbour is open at
both ends, so where the standard is half a wavelength long it resonates — the run lists those
frequencies.

### Move the ports, not the circuit {#moveports}

This is usually the real answer on a board, and it is not a redesign.

A coupled section is almost never coupled all the way to where you would naturally put a port — the
traces arrive from somewhere, and that approach is often already separated. **Put the port on the
isolated run instead**, and the coupled section you care about stays exactly as drawn. You are
choosing where the reference plane sits, not changing the circuit.

What comes back is then referenced to that plane, so the extra line is part of the answer. De-embed
it in the circuit — a length of ideal line of the same impedance in series, or the
[cross-section kernel's](../reference/mom-engine.html) own answer for that run — the same thing you
would do with a measured fixture.

### Give every port an isolated feed {#isolate}

If neither applies, the structure has to supply the isolated run. Same coupled section, unchanged:
4 mm of line added at each port with the other conductor held 6 mm away there.

{{ui: an01-coupled-pair-isolated-feeds}}

| f (GHz) | S(1,1) | S(2,1) | S(3,1) | σ<sub>max</sub> |
|---|---|---|---|---|
| 1.0 |  −8.71 dB | −0.69 dB | −26.60 dB | 0.9983 |
| 3.0 |  −3.85 dB | −2.60 dB | −18.54 dB | 0.9927 |
| 5.0 |  −4.96 dB | −2.32 dB | −13.13 dB | 0.9900 |
| 7.0 | −26.85 dB | −0.74 dB | −11.61 dB | 0.9884 |

Every port reports its own margin — *"6.38 substrate heights, against the 5 a neighbour that carries
a port of its own needs"* — and the result is passive at every frequency. As in the option above, the
8 mm of extra line is genuinely part of the structure now and comes out in the circuit if you want
the planes back at the coupled section's own ends.

## Best practice {#practice}

**Let `Auto` choose the kernel.** A uniform cross-section is solved exactly, in a fraction of the
time, with no de-embedding step to get wrong. Forcing the planar kernel on geometry that does not
need it buys a calibration you then have to satisfy.

**Measure port clearance in SUBSTRATE HEIGHTS, not in millimetres and not in line widths.** How much
a calibrated feed needs was measured on this very geometry, by sweeping the separation and comparing
against the cross-section kernel at every point:

| Neighbour | Clearance it needs | On the 0.9 mm board above | Inside that, circuitRF… |
|---|---|---|---|
| A conductor with a **port on it, at the same reference plane** | about **5 × the substrate height** | ≈ 4.5 mm | [calibrates the two together](#group) |
| A conductor with a **port on it, somewhere else** | about **5 × the substrate height** | ≈ 4.5 mm | [refuses the run](#refused) |
| A conductor with **no port** — a passive trace, a ground pour | about **2 × the substrate height** | ≈ 1.8 mm | [puts it in the standard](#passive) |

Three things are worth knowing about that table.

- **It is the substrate height that matters.** Making the lines four times wider moved the
  requirement by 5 %; making the substrate four times thinner moved it by nearly four times. A ground
  pour behaves exactly like a thin trace at the same distance — the neighbour's own width does not
  enter.
- **You get the margin even when nothing is wrong.** Every de-embedded port reports its clearance in
  substrate heights on every run, so "comfortable" and "just passed" are distinguishable without
  re-running anything. Do not confuse that margin with the separate sentence about the **2.7 mm of
  line the standard reproduces**: the first is the measured requirement, the second is how far into
  the structure the standard looks.
- **A pad or flare on the port's *own* conductor is a different thing and is already handled** —
  circuitRF grows a uniform lead for it and removes it exactly, which is the automatic feed lead
  reported in the notes, and a port that sits on a pad can still join a group.

**Read the notes under the run, every time.** The kernel choice, the calibration groups and their
modes, every port's clearance and reference plane, the passivity verdict — all of it is printed
before you plot anything.

**Start from the default mesh and justify every reduction.** Cells per wavelength defaults to 20 and
cells across to 4. The fixture on this page runs at 5 and 2 because it is a deliberately hard case;
below about 10 is not a serious answer on real work, and a coarse mesh moves the reference planes off
your drawn edges.

{{ui: an01-coupled-pair-coarse-mesh}}

**Sanity-check one number by hand.** A single microstrip section is a closed-form calculation. If the
solver disagrees with it by 20 dB at the bottom of the band, stop there.

**Check passivity across the whole band, not at one marker.** De-embedding error is strongly
frequency-dependent: the peel divides by a<sub>21</sub><sup>2</sup>, which grows as frequency falls,
so **the bottom of the sweep is always the worst place** and is always where to look first.

**And read `NOT PASSIVE` as a smoke alarm, not as a thermometer.** It is reliable at telling you
*that* something is wrong. It is not reliable at telling you *how* wrong — the worst case measured
during this work reported a *smaller* passivity excess than a case with half the error. Do not rank
two runs by their σ<sub>max</sub>.

## Comparing two port configurations properly {#compare}

An S-parameter is defined with **every other port terminated in its own reference impedance** — that
is what a<sub>k</sub> = 0 means — so a port is a **load as well as a source**, and on coupled lines
the neighbour's load is exactly where the coupled power goes. Deleting a port label removes the load
and the excitation; it never removes the conductor, which goes on being meshed and goes on coupling,
now open at both ends.

So if what you want to know is *how does this network behave with its other ports loaded
differently*, **do not delete the ports.** Simulate once with every port present, then change the
loads in the circuit:

- Place the resulting `.sNp` as an [SnP](../reference/dynamic-symbols.html#snp) component in a
  schematic.
- Put [Terms](../reference/pins-ports-terms.html#term) on the ports you want to measure.
- Put the real load on each remaining port — a resistor to ground for a matched termination, whatever
  the neighbour actually drives otherwise.
- Run S-parameters.

The mesh, the calibration, the feed leads and the frequency plan are then identical across every
comparison, and the only thing that varies is the thing you meant to vary. It is also faster: one EM
run serves every load case.

Two runs with different port counts are **not** a controlled comparison in any case. Port labels seed
gridlines, so the mesh can differ; de-embedding grows a feed lead where a port sits on metal that
changes cross-section, so the solved geometry can differ; and
[adaptive sampling](../reference/em-setup.html#solver) converges against the whole S-matrix, which is
16 entries in one run and 4 in the other, so the solved frequencies can differ too.

Delete ports from the layout only when you are deliberately changing the experiment.

## Checklist before you trust an EM result {#checklist}

| Check | Where it is reported |
|---|---|
| The kernel `Auto` would have picked is the one that ran, or you know why not | Run notes |
| Every port's feed-clearance margin comfortable, not marginal | Per-port notes, in substrate heights |
| For a calibration group: the modes are separable, and the run says by how much | Run notes — `MODAL CALIBRATION`, per frequency |
| Every port's reference plane on the drawn metal edge, not inside it | Per-port notes |
| No `NOT PASSIVE` note, at any frequency | Run notes |
| Adaptive sampling **converged** | Run notes |
| At least ~10 cells per wavelength, more than one cell across the narrowest conductor | Mesh summary |
| S(1,1) = S(2,2) where the geometry says it must | Your own plot |
| One closed-form or previously-trusted number agrees | Your own arithmetic |
| Every port you care about is present and loaded the way you mean | The layout and the `.cem` |
