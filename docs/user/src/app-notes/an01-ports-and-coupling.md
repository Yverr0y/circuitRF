---
title: AN-01 — Ports, terminations and de-embedding
slug: app-notes/an01-ports-and-coupling.html
doc-kind: Application Note
breadcrumb: Docs > App Notes > AN-01 Ports and de-embedding
lede: The same layout simulated with four ports and with two gives different S11. Two separate things are going on, only one of them is physics, and the other one is not a mesh problem.
keywords: S11, ports, coupling, de-embedding, passivity, EM, coupled lines, termination, calibration, port feed, cross-section kernel
---

<nav class="toc">
<h2>On this page</h2>
<ol>
<li><a href="#question">The question</a></li>
<li><a href="#structure">The structure</a></li>
<li><a href="#termination">Answer 1: a port is a termination, and deleting one does not delete the metal</a></li>
<li><a href="#proof">Showing that on the data</a></li>
<li><a href="#unusable">Answer 2: neither run was usable, and circuitRF said so</a></li>
<li><a href="#cause">Finding the cause: a controlled experiment</a></li>
<li><a href="#notmesh">What did <em>not</em> fix it</a></li>
<li><a href="#fix">What to do about it</a></li>
<li><a href="#alsochanges">Three more things that change with the port count</a></li>
<li><a href="#practice">Best practice</a></li>
<li><a href="#compare">Comparing two port configurations properly</a></li>
<li><a href="#checklist">Checklist before you trust an EM result</a></li>
</ol>
</nav>

## The question {#question}

A two-layer board is imported, an [EM setup](../reference/em-setup.html) is built over part of it, and
the same layout is simulated twice:

1. **As drawn** — four ports, placed from the layout's own port labels.
2. **The same setup with ports 3 and 4 deleted** — two ports.

S(1,1) comes back different between the two runs. Ports 3 and 4 are on a *different trace*, so the
expectation is that S(1,1) should be more or less untouched. It is not. Is that a meshing difference?
A bug? Or is the expectation wrong?

Both, as it turns out, and in an instructive proportion. The expectation *is* wrong — but correcting it
only explains part of the difference, and chasing the rest turns up something worth knowing whether or
not you ever change a port count.

## The structure {#structure}

Two parallel microstrips over a ground plane. Each is **254 µm wide and 3.83 mm long**, and they are
**246 µm apart** edge to edge, on 0.9 mm FR-4 (ε<sub>r</sub> 4.4, tan δ 0.02) with the ground plane
below. Ports 1 and 2 are the ends of the lower line; ports 3 and 4 are the ends of the upper one.
The sweep is 1–7 GHz.

{{ui: an01-coupled-pair-4port}}

Delete ports 3 and 4 and what is left is this:

{{ui: an01-coupled-pair-2port}}

**Look at what did not change.** The upper line is still drawn, so it is still extracted, still meshed
and still coupled to the lower line. Two conductors 246 µm apart running alongside each other for
3.83 mm are a coupled pair, not two independent things.

## Answer 1: a port is a termination, and deleting one does not delete the metal {#termination}

S-parameters are defined one port at a time, and the definition includes what is happening at all the
*other* ports:

> S(1,1) = b<sub>1</sub> / a<sub>1</sub>, with a<sub>k</sub> = 0 for every k ≠ 1.

"a<sub>k</sub> = 0" is not a statement about geometry. It means **every other port is terminated in its
own reference impedance** — 50 Ω here — so that nothing reflects back in. S(1,1) is therefore a property
of the structure *and* of the loads on every other port.

The two runs are not the same experiment:

| | Upper line, 4-port run | Upper line, 2-port run |
|---|---|---|
| Metal | Present, meshed | Present, meshed |
| Left end | Driven, terminated in 50 Ω | **Open** |
| Right end | Driven, terminated in 50 Ω | **Open** |
| Can absorb power? | Yes | **No** |

A 50 Ω-terminated neighbour absorbs whatever couples into it, and that lost power shows up as a *lower*
|S(1,1)| on port 1. An open-circuited neighbour absorbs nothing — it stores the coupled energy and gives
it back, reactively, at a phase set by its own length. Those are different boundary conditions on a
conductor port 1 can see, so they give a different S(1,1).

<div class="callout note">
<span class="label">The general rule</span>
<p>Deleting a port label removes an <b>excitation and a load</b>. It never removes a conductor. If you
want the metal gone, delete the metal.</p>
</div>

## Showing that on the data {#proof}

This is checkable without re-running anything. Take the 4-port result and reduce it to two ports
numerically, loading ports 3 and 4 with a reflection coefficient Γ, and see which Γ reproduces the
2-port run:

S<sub>red</sub> = S<sub>kk</sub> + S<sub>kt</sub> Γ (I − S<sub>tt</sub> Γ)<sup>−1</sup> S<sub>tk</sub>

where *k* = {1, 2} are the kept ports and *t* = {3, 4} the terminated ones. Γ = 0 is a matched 50 Ω load,
Γ = +1 an open, Γ = −1 a short.

| f (GHz) | 4-port S(1,1) — ports 3,4 at 50 Ω | Reduced with ports 3,4 **open** | The actual 2-port run |
|---|---|---|---|
| 1.0 | −0.07 dB | −1.43 dB | −0.73 dB |
| 2.2 | −0.32 dB | −3.42 dB | −2.25 dB |
| 3.4 | −0.83 dB | −4.12 dB | −3.16 dB |
| 4.6 | −1.79 dB | −4.17 dB | −3.53 dB |
| 5.8 | −3.59 dB | −3.99 dB | −3.61 dB |
| 7.0 | −5.54 dB | −3.77 dB | −3.58 dB |

The **open** column tracks the 2-port run — same slope, same rough level, agreeing to within 0.2 dB at
the top of the band. The 50 Ω column does not resemble it at all. So the termination effect is real and
it is most of the difference.

But the open column and the 2-port run still disagree by up to 0.7 dB, and that residual is where the
rest of this note comes from.

## Answer 2: neither run was usable, and circuitRF said so {#unusable}

Both runs printed this in their notes:

> **NOT PASSIVE:** 51 of 51 de-embedded point(s) have σ<sub>max</sub>(S) > 1, worst 1.0044 at 7 GHz.
> A passive structure cannot do that, so the excess is this analysis, not your design, and the
> s-parameters at those points should not be used.

That is the run telling you its own answer is wrong. Confirming it independently is easy, because the
lower line is a plain microstrip whose response can be written down. At 254 µm over 0.9 mm FR-4 it is a
**≈117 Ω line with ε<sub>eff</sub> ≈ 2.96**, 3.83 mm long — λ/45 at 1 GHz, so almost all of the incident
power should simply go through:

| f (GHz) | S(2,1), ideal 117 Ω line | S(2,1), 2-port run | S(2,1), 4-port run |
|---|---|---|---|
| 1.0 | **−0.07 dB** | −7.93 dB | **−34.2 dB** |
| 7.0 | −2.08 dB | −2.59 dB | −5.23 dB |

At 7 GHz both runs are in the right neighbourhood. At 1 GHz the 4-port run claims that the two ends of a
single continuous 3.83 mm trace are 34 dB apart. They are not — and coupling cannot be the explanation,
because terminating the *other* line cannot switch off conduction along *this* one.

**So the honest reading of the comparison this note opened with is: two wrong answers, wrong by
different amounts.**

## Finding the cause: a controlled experiment {#cause}

To separate the two effects, the geometry above was rebuilt as a synthetic case with nothing else in
it — two straight 254 µm lines, 246 µm apart, 3.83 mm long, on the same 0.9 mm FR-4. No import, no pads,
no bends, no neighbouring copper: if the failure still appears here, it belongs to the ports and the
calibration rather than to anything particular about one board.

A pair of straight parallel lines is a **uniform cross-section**, so circuitRF's `Auto` analysis picks
the [cross-section kernel](../reference/mom-engine.html), which solves that geometry per unit length and
needs no de-embedding at all. Forcing the full-wave planar kernel on the *same file* gives the
comparison:

| f (GHz) | S(1,1) cross-section | S(1,1) planar | S(2,1) cross-section | S(2,1) planar | σ<sub>max</sub> xsec | σ<sub>max</sub> planar |
|---|---|---|---|---|---|---|
| 1.0 | −19.09 dB | −0.08 dB | **−0.11 dB** | **−22.75 dB** | 0.9992 | 1.0008 |
| 2.2 | −12.83 dB | −0.36 dB | −0.42 dB | −16.29 dB | 0.9983 | 1.0017 |
| 3.4 | −9.91 dB | −0.85 dB | −0.84 dB | −13.23 dB | 0.9973 | 1.0023 |
| 4.6 | −8.24 dB | −1.55 dB | −1.25 dB | −11.75 dB | 0.9973 | 1.0029 |
| 5.8 | −7.20 dB | −2.51 dB | −1.60 dB | −11.57 dB | 0.9972 | 1.0035 |
| 7.0 | −6.52 dB | −4.01 dB | −1.88 dB | −13.07 dB | 0.9970 | 1.0041 |

The cross-section column is the textbook answer for a short, mismatched 117 Ω line, and it is passive
everywhere. The planar column on the identical geometry is out by **22 dB in S(2,1) at 1 GHz** and is
non-passive at 48 of the 51 frequencies. The same failure, in the same direction and worst at the same
end of the band, on geometry with no import, no bends and no pads in it. (It is not the same size — the
original board is worse — because that one also has pads, a flare at two ports and a coarser mesh. The
mechanism is what reproduces, not the magnitude.)

And the run names the reason, once for each port:

> Port 1's feed has other metal **246 µm away**, inside the 2.7 mm the calibration standard assumes is
> empty. The de-embedding replaces the port's neighbourhood with an isolated line of the same width, so
> whatever is closer than that is not removed correctly.

The planar kernel de-embeds each edge port with a **two-line calibration**: it solves calibration
standards beside your structure and peels the port discontinuity off the raw answer. Those standards are
*isolated uniform lines*. Here every port sits on a coupled pair from its very first cell, so the error
box being peeled was measured on a structure that is not the one being de-embedded. The peel then divides
by a<sub>21</sub>², which is of order 10⁴ at 1 GHz — which is why the damage is catastrophic at the
bottom of the band and merely bad at the top.

## What did *not* fix it {#notmesh}

The natural first suspicion is the mesh, and this setup did have a coarse one: **cells per
wavelength 5** (the default is 20) and **cells across 2** (default 4), with auto meshing off.

{{ui: an01-coupled-pair-coarse-mesh}}

That is genuinely too coarse — one cell landed across each 254 µm conductor, the cells along the line
were a large fraction of its length, and two ports ended up with their reference plane 1083 µm *inside*
the drawn metal. But it is **not what produced the wrong answer**, and that was measured rather than
assumed:

- **Doubling cells per wavelength on the original board** (5 → 10, cells across 2 → 4, accelerated solve
  on, 26 minutes of run time) moved S(1,1) by at most 0.19 dB, left S(2,1) at −34.2 dB at 1 GHz, and left
  the result non-passive at 51 of 51 points (worst σ<sub>max</sub> 1.0044 → 1.0042).
- **The clean synthetic case above meshed at 7 cells across** the 254 µm conductor — a perfectly healthy
  mesh — and was still non-passive at 48 of 51 points.

<div class="callout note">
<span class="label">Worth internalising</span>
<p>A non-passive de-embedded result is not automatically a mesh-density problem. If the calibration is
being applied to the wrong structure, a finer mesh just computes the wrong thing more accurately.</p>
</div>

## What to do about it {#fix}

The calibration needs the port's feed to be an isolated uniform line for the length of the standard.
There are three ways to give it one, and **only the last involves changing your artwork**.

### 1. Check whether you need the planar kernel at all {#usexsec}

A **uniform cross-section** — straight coupled lines, a single transmission line, a broadside pair —
is solved by the [cross-section kernel](../reference/mom-engine.html) per unit length. It has no
calibration step, so it has nothing to get wrong, and it is about a thousand times cheaper.

That is not a hypothetical: the cross-section column in the table above came from **the same
unmodified file**. Leaving Analysis on `Auto` picks that kernel for this geometry and says so in the
notes. Forcing the planar kernel on a structure that does not need it buys a calibration you then have
to satisfy.

### 2. Move the ports, not the circuit {#moveports}

This is usually the real answer on a board, and it is not a redesign.

A coupled section on a real layout is almost never coupled all the way to where you would naturally
put a port — the traces arrive from somewhere, and that approach is often already separated. If there
is isolated uniform line anywhere on the way in, **put the port there instead**, and the coupled
section you care about stays exactly as drawn. You are choosing where the reference plane sits, not
changing the circuit.

What you get back is then referenced to that plane, so the extra line is part of the answer. De-embed
it in the circuit — a length of ideal line of the same impedance in series, or the
[cross-section kernel's](../reference/mom-engine.html) own answer for that run — the same thing you
would do with a measured fixture.

### 3. Give every port an isolated feed {#isolate}

If neither of the above applies, the structure has to supply the isolated run. Same coupled section as
before, unchanged — 3 mm of line added at each port with the other conductor held 4 mm away there:

{{ui: an01-coupled-pair-isolated-feeds}}

| f (GHz) | S(1,1) | S(2,1) | S(3,1) | σ<sub>max</sub> |
|---|---|---|---|---|
| 1.0 | −10.07 dB | −0.49 dB | −34.32 dB | 0.9986 |
| 2.2 | −5.34 dB | −1.64 dB | −22.05 dB | 0.9966 |
| 3.4 | −4.01 dB | −2.48 dB | −18.77 dB | 0.9934 |
| 4.6 | −4.06 dB | −2.78 dB | −14.05 dB | 0.9905 |
| 5.8 | −4.95 dB | −2.60 dB | −11.26 dB | 0.9904 |
| 7.0 | −7.65 dB | −1.69 dB | −11.05 dB | 0.9901 |

Same mesh settings, same kernel, same sweep. **Passive at every frequency**, no `NOT PASSIVE` note, no
port-feed note, and S(2,1) behaves like a transmission line again. As in option 2, the 6 mm of extra
line is genuinely part of the structure now and comes out in the circuit if you need the planes back
at the coupled section's own ends.

<div class="callout note">
<span class="label">Why the solver cannot just do this for you</span>
<p>circuitRF <b>does</b> already add metal for the solve and remove it again — that is the
automatic feed lead reported in the notes, and it is what saves a port sitting on a taper. But that
lead is <b>collinear and uniform</b>, which is the only reason it can be removed exactly: it is a
matched section of the port's own line whose propagation constant the calibration has measured.
Separating two coupled feeds means routing them apart, and a bend is not a matched uniform section —
the solver would be putting a discontinuity it invented between your metal and the reference plane,
and could not take it out again. So it declines, tells you, and leaves the decision with you.</p>
</div>

## Three more things that change with the port count {#alsochanges}

Even setting all of the above aside, two runs with different port counts are not a controlled comparison.
For the runs in question:

1. **The mesh is not the same.** Port labels seed gridlines, so the DUT went from **2,067 unknowns**
   (2 ports) to **2,112** (4 ports), and the maximum y pitch from 205.8 µm to 258.3 µm.

   <div class="callout note">
   <span class="label">The provenance hash does not contradict this</span>
   <p>Both Touchstone files carried the same <code>circuitRF-EM mesh:</code> stamp. That hash is over the
   <b>mesh settings</b>, which genuinely did not change — it is what tells you a result is stale against
   an edited setup. It is not a hash of the resulting mesh.</p>
   </div>

2. **The solved geometry is not the same.** De-embedding grows a uniform feed lead where a port sits on
   metal that changes cross-section. Port 4 got **2,573 µm of lead added** to the upper line for the
   solve — metal that does not exist at all in the 2-port run, on the very conductor whose coupling is
   under discussion.

3. **The solved frequencies are not the same.** [Adaptive sampling](../reference/em-setup.html#solver)
   converges against the whole S-matrix, which is 16 entries in one run and 4 in the other. The 4-port
   run converged on 22 of 51 points; the 2-port run **did not converge** (|ΔS| = 0.00227 against a 0.001
   tolerance) on 21 points, so part of that curve is spline interpolation.

## Best practice {#practice}

**Let `Auto` choose the kernel.** If your structure is a uniform cross-section, the cross-section
kernel gives an exact answer in a fraction of the time and skips the de-embedding step entirely.
Forcing the planar kernel on geometry that does not need it buys you a calibration you then have to
satisfy.

**Put edge ports where the line is already isolated, and measure the clearance in SUBSTRATE
HEIGHTS.** The two-line calibration assumes the port's feed is an isolated uniform line. How much
clearance that actually needs has been measured on this very geometry, by sweeping the separation and
comparing against the cross-section kernel at every point:

| Neighbour | Clearance it needs | On the 0.9 mm board above |
|---|---|---|
| A conductor with a **port on it** | about **5 × the substrate height** | ≈ 4.5 mm |
| A conductor with **no port** — a passive trace, a ground pour | about **2 × the substrate height** | ≈ 1.8 mm |

Three things are worth knowing about that table.

- **It is the substrate height that matters, not the line width.** Making the lines four times wider
  moved the requirement by 5 %. Making the substrate four times thinner moved it by nearly four times.
  A ground pour behaves exactly like a thin trace at the same distance: the neighbour's own width does
  not enter.
- **The message the solver prints today quotes 2.7 mm on this board** — three substrate heights, which
  is the length of the calibration standard rather than a measured requirement. At exactly that
  distance the coupled pair above is still wrong enough to be non-passive, so treat the printed figure
  as a floor and not as a target.
- **A pad or flare on the port's *own* conductor is a different thing and is already handled** —
  circuitRF grows a uniform lead for it and removes it exactly, which is the automatic feed lead
  reported in the notes. What the table is about is metal belonging to something else.

Placing the port a few millimetres further back along line that is already clear costs nothing and is
usually available; de-embed the extra length in the circuit.

**Read the notes under the run, every time.** Everything diagnosed here was printed by the run itself,
before anyone plotted anything — the passivity verdict, the port-feed warning, the reference-plane
positions. `NOT PASSIVE` is not a remark about a marginal structure; it is the solver saying the numbers
it just wrote should not be used.

**Start from the default mesh and justify every reduction.** Cells per wavelength defaults to 20; below
about 10 is not a serious answer. It was not the cause here, but a coarse mesh does move the reference
planes off your drawn edges, and that *is* a mesh-density problem.

**Sanity-check one number by hand.** A single microstrip section is a closed-form calculation. If the
solver disagrees with it by 20 dB at the bottom of the band, stop there.

**Check passivity across the whole band, not at one marker.** The failure here was strongly
frequency-dependent — nearly fine at 7 GHz, useless at 1 GHz. A spot check at the design frequency would
have passed. That is not a quirk of this board: the peel divides by a<sub>21</sub><sup>2</sup>, which
grows as the frequency falls, so **the bottom of the sweep is always the worst place** and is always
where to look first.

**And read `NOT PASSIVE` as a smoke alarm, not as a thermometer.** It is reliable at telling you
*that* something is wrong — on the sweeps behind the table above, every separation with a material
error was flagged at one frequency or more. It is not reliable at telling you *how* wrong: the worst
case measured reported a *smaller* passivity excess than a case with half the error. Do not rank two
runs by their σ<sub>max</sub>.

## Comparing two port configurations properly {#compare}

If what you actually want to know is *how does this network behave with its other ports loaded
differently*, **do not delete the ports.** Simulate once with every port present, then change the loads
in the circuit:

- Place the resulting `.sNp` as an [SnP](../reference/dynamic-symbols.html#snp) component in a schematic.
- Put [Terms](../reference/pins-ports-terms.html#term) on the ports you want to measure.
- Put the real load on each remaining port — a resistor to ground for a matched termination, whatever the
  neighbour actually drives otherwise.
- Run S-parameters.

The mesh, the calibration, the feed leads and the frequency plan are then identical across every
comparison, and the only thing that varies is the thing you meant to vary. It is also faster: one EM run
serves every load case.

Delete ports from the layout only when you are deliberately changing the experiment — and expect the
answer to change when you do.

## Checklist before you trust an EM result {#checklist}

| Check | Where it is reported |
|---|---|
| No `NOT PASSIVE` note, at any frequency | Run notes |
| No port whose feed has other metal inside the calibration length | Per-port notes |
| Every port's reference plane on the drawn metal edge, not inside it | Per-port notes |
| Adaptive sampling **converged** | Run notes |
| The kernel `Auto` would have picked is the one that ran, or you know why not | Run notes |
| At least ~10 cells per wavelength, more than one cell across the narrowest conductor | Mesh summary |
| One closed-form or previously-trusted number agrees | Your own arithmetic |
| Every port you care about is present and loaded the way you mean | The layout and the `.cem` |
