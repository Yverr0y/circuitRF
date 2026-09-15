# Patch Antenna

A 5.8 GHz rectangular microstrip patch with an inset feed on 30 mil RO4350B, with a ground plane
below it, set up for a full-wave planar solve.

`patch/em/patch-5p8GHz.cem` sweeps 5.3–6.3 GHz with **resonance search** and **radiation pattern**
both on. Resonance search is the one mode allowed to add frequencies between the ones you asked
for, which is what puts points where the resonance actually is rather than where the grid happened
to fall.

Open the `.cem` and press **Simulate**. No results ship with this example.

Worth looking at afterwards: the input match around resonance, the surface-current map, and the
pattern cuts. The patch is fed at a single edge port, so it is a one-port structure — the antenna's
behaviour is entirely in S11 and in what the pattern shows is being radiated rather than reflected.
