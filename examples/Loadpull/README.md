# Loadpull and Pursuit

Two ways to find a device's optimum load, over the same GaN model.

## Loadpull — a grid you specify

`Loadpull` terminates the device between a **SourceTuner** and a **LoadTuner**, both with their
bias tees on, and sweeps the load over the grid in `load_grid.gam`. At each grid point the engine
walks a drive ladder up to 3 dB gain compression and reports the operating point there — which is
what makes the resulting contours comparable point to point.

The grid file is plain text and worth reading:

```
# gamma Z0=50 mag_ang
0.0  0
0.20  0
0.20  30
...
```

The header tags say what the numbers mean. **With no `gamma` tag the file is read as impedance**, so
a grid written without one is a completely different sweep that runs without complaint.

`RFfreq` is authored as expression `2` with unit `GHz` in the VAR's own unit column. A unit written
**inside** the expression (`2 GHz`) is a parse error — circuitRF's grammar has no unit suffix — and
an unresolvable variable is skipped rather than reported where you typed it.

## LoadpullPursuit — a search, not a grid

`LoadpullPursuit` starts from a guess and walks toward the optimum, then draws two constant-VSWR
rings around what it found. It needs no grid file. The outer sweep takes it across 1.8–2.2 GHz;
a second, disabled sweep over `VDD` is there to enable when you want a surface.

Both benches carry `Meas` blocks reading `V` at the drain by net name and harmonic index — the same
accessor the harmonic-balance benches use, because a loadpull result is a cube like any other.
