# src/Render — resolved briefs (detail, off the CLAUDE.md growth path)

## Owner report, 2026-09-08 — the Polar grid only ever looked right at unity

`AxesRenderer.DrawPolarGrid` took its ring radii from `axes.Ticks(true).MinorX`. That tick set is
built for a RECTANGULAR axis, and `MinorX` is the lattice with every MAJOR multiple **removed** —
correct when the majors are drawn separately as their own darker gridlines, and wrong as a set of
radii, because nothing draws them here. The ring sequence therefore had a hole in it wherever a
major tick fell.

It went unnoticed because the unit circle is the one window where the arithmetic comes out even:
width 2 gives a 0.2 minor spacing and a major multiplier of 2, so the surviving radii are 0.2, 0.6,
1.0 — three evenly spaced rings with the outermost exactly at the frame. Nothing about that is a
property of the code; it is a coincidence of that one window. Off unity it failed three different
ways at once:

| window | rings drawn |
|---|---|
| ±1 | 0.2, 0.6, 1.0 — even, and the boundary is on the lattice |
| ±1.2 | 0.2, 0.6, 1.0 — **no ring at the frame at all**, and nothing says the radius is 1.2 |
| ±1.5 | 0.2 … 1.4 with **1.0 missing**, six rings, no frame |
| ±2 | 0.2 … 1.8 with **1.0 missing**, eight rings, no frame |
| ±3 | 0.5, 1.5, 2.5 — half-integers only, no frame |

**The rings are computed from the framed radius now, not from the tick set.** `PolarRings(rMax)`
picks the nearest 1/2/5 decade to `rMax / 5`, every multiple of it is drawn, and the boundary ring
at exactly `rMax` is always drawn and always labelled — so the ring COUNT is roughly the same at
every scale, which is the property that was missing. A window is a square centred on the origin on
every complex plot type (`Plot.SquareCentredOnOrigin` serves both `Plot.Autoscale` and the
axis-limits flyout), so its half-width IS that radius; it is taken from the X edges because the
rings are drawn in X pixels.

Three details are load-bearing and each came from a case that looked wrong on screen:

- **The nice-number thresholds are nudged in by a tolerance**, because the mantissa is a QUOTIENT and
  a decade boundary does not survive one: `rMax = 1.5` gives `m = 2.9999999999999996` and
  `rMax = 0.75` gives `m = 1.4999999999999998`, so a bare `m < 3` / `m < 1.5` takes the finer branch
  and the plot comes back with seven rings and a tick comb at twice its neighbours' density — for a
  radius one digit long.
- **A multiple landing within two fifths of a step of the frame is dropped**, not drawn beside it.
  `rMax = 12.5` has 12 on its lattice, half a step from the edge, and two circles that close read as
  a rendering fault rather than as a grid. A quarter of a step was not enough for that case.
- **Minor radii are short ticks ACROSS the two diameters, not rings of their own.** Five
  subdivisions of five rings is twenty-five circles, which stops reading as a grid and starts
  reading as shading; on the axes they give the same fine reading at a fifth of the ink. They are
  suppressed below 4 canvas pixels of spacing — absolute pixels, because this is a question about
  what the eye can separate, and a comb finer than that is a grey band however thin its strokes.

The radius numbers now carry the axis's SI prefix through the same `EngineeringFormat` path the
rectangular ticks use (one group for the whole axis, never one per ring — `G4` printed `0.0002` on a
milli-scale plot), sit clear of the tick comb rather than on the axis line, and are dropped
**outwards-in** where the lattice is too tight to label completely, so the boundary number — the one
that says what scale the plot is at — is the one that survives.

### The outline was clipped where it touched the box — on the Smith chart too

Both grids clip to `PlotRenderer.ViewportClipRect`, and both draw an OUTLINE whose radius is on the
boundary that clip is cut to, so the outer half of that stroke was taken away. Measured as ink
across the stroke on a 900 px polar render: 224 and 232 at 45° and 135°, **112 at the top and 94 at
the bottom** — half a line, exactly at the four points where the disc is tangent to the box.

**The Smith chart had a second, worse instance** running all the way round rather than at four
points: with the window at exactly the unit square the grid is also clipped to a `ClipPath` of the
unit circle, and the `r = 0` circle IS that circle — so the chart's outline was half-width
everywhere, which is why it read lighter than the constant-R circles inside it.

**Widening both clips fixes the outline and is the wrong fix**, which the first attempt did and an
owner caught the same day: the reactance arcs then ran past |Γ| = 1 by their own thickness. An arc
meets the boundary at a shallow angle, so a stroke's width of RADIAL slack shows up as a much longer
tail along the edge — an arc leaves the disc visibly where a circle concentric with it would not.

**The outline is separated from the grid it bounds instead.** The arcs keep the exact clips they
always had; the outline is drawn afterwards, outside them, under a box one stroke wider — it is the
only thing that needs the slack, and it needs it precisely because it sits on the boundary. Polar
got the same split, where it also stops the diameters' square end caps poking a pixel past the
frame. After: 222 / 210 / 213 around the polar ring, and 23 near-white antialiasing pixels beyond
the Smith disc against 0 before, none of them an arc.

### Every crossing on the Smith grid was darker than the lines that made it

The constant-R and constant-X arcs were each stroked at `MinorTransparencyScale`, so an overlap
composited: two 50 % strokes read 75 %, three read 87.5 %. Sampled on a 900 px render, a plain arc
is **208** and the r=1 × x=1, r=1 × x=2, r=2 × x=1 and r=0.5 × x=1 crossings were **185, 185, 185,
187** — so the grid was darkest exactly where it is busiest, and the chart read stippled rather than
ruled.

**The family is drawn into one `SaveLayer` at full opacity and composited once.** Inside the layer
an overlap is the same colour as a single stroke, because opaque over opaque is opaque; the layer's
own alpha is then the transparency that was wanted. Every one of those samples reads 207-208 now.

**The outline and the real axis stay out of the layer, at full strength.** They are the chart's frame
and its reference rather than grid, they are meant to be the darker lines, and putting them inside
would have faded them to the arcs' tone.

### The Smith chart's numbers did not shrink with the chart

Reported the same day, against wide axis limits: past about ±5 the grid numbers ran together into a
blob. The grid of a Smith chart is FIXED in the Γ plane — its arcs ARE the unit disc's — so unlike a
polar plot's rings it cannot be re-latticed for a wider window. Zoom out and the whole chart is just
a smaller object in a bigger box. Every part of it scaled with the window except the numbers, whose
size came from the CANVAS (`FontSizeTicks * lw`), so the anchors closed on each other at constant
text size: crowded at ±2, illegible by ±5, a smudge at ±10.

**The text scales with the disc, and what still will not fit is dropped.** Both halves are needed.
The scale is `2 / Window.Width` — the disc's radius runs as 1/(half-window), so that IS the chart's
own zoom, and it is exactly 1 at the unit window, where the picture is byte-identical to before
(checked at two canvas sizes). It comes from the window's WIDTH rather than from the measured disc
radius so that panning, which moves the disc without resizing it, cannot change the size of a
number. Below `SmithLabelMinScale` (0.45) it stops shrinking, because a face much under half the
tick size is texture rather than text — and past that floor no size makes room, so a label landing
on one already placed is dropped instead.

Three details:

- **The thinning test is a rect overlap, not `DrawPolarGrid`'s one-dimensional "taken so far" edge.**
  A Smith chart's numbers crowd in two dimensions: the reactance labels come down the outside of the
  disc towards the same r → ∞ point the resistance numbers run into.
- **The placement ORDER is the priority, and the unit circle goes first.** Ascending order alone
  dropped `1` — the chart's own centre — because `0.5`'s label reaches it first. r = 1 and x = ±1 are
  placed before anything else; the rest run outwards-in from the roomy r = 0 side, so what is lost is
  what piles into r → ∞.
- **Under `SmithLabelMinDiscFaces` (4) font sizes of disc radius the grid carries no numbers at
  all.** A number is about two font sizes wide at the floor scale, so a disc smaller than that is one
  a single label would very nearly span; thinning would leave one arbitrary survivor sitting on a
  smudge, and nothing is the better answer.

The old `axes.Window.Width > 3` rule — which dropped the two 10s and nothing else — is gone: it was a
two-step approximation of a continuous problem, and at ±2 it threw away numbers that now fit,
because the text is half the size there.

harmonicaRF's Smith panels draw through this same code with the default unit window, so they are
untouched.

### A Y-axis label rendered a rectangle — and THREE renderers draw one

Skia draws a glyph the typeface lacks as NOTDEF — a box — and substitutes nothing. Checked against
the shipped `cmap`s rather than assumed: of the characters these labels can carry, **IBM Plex lacks
U+25B8 `▸`, U+2220 `∠`, U+2225 `∥`, U+25CF `●` and U+25B2 `▲`, and HAS U+2192 `→`**. So in
`WSProbe GATE→DRAIN ▸ block` the arrow was never the problem and the group separator beside it
always was — the report called it "the -> glyph", which is what a small right-pointing triangle
looks like.

`RendererText` already splits a string into runs by coverage and falls back to DejaVu; the marker
info box and the Table have used it for `∠` since they were written. Three places draw a Data
Display axis label and **none of them did**:

| renderer | what it draws | reached by |
|---|---|---|
| `AxesRenderer.DrawTitleAndAxisLabels` | the title, the X label(s), the Y label in the Skia margin | a Rect plot's first left/right trace, or a custom label |
| `AxisLabelControl` (`src/Ui`) | the per-trace label STRIPS beside the plot | **every Smith/Polar trace, and Rect traces past the first** |
| `PlotComposer` | those same strips, in an exported document | Export / `render --data` |

**Fixing only the first fixed nothing the reporter could see**, and that is the finding: a WSProbe
trace's label lands in the strips, which are a different renderer in a different project, reached
through an Avalonia custom draw operation rather than through `AxesRenderer` at all. All three go
through the fallback now. The title's shrink-to-fit resizes both faces together — measuring with one
size and drawing with another is how a centred string ends up off-centre.

Two text sites were checked and deliberately left alone: `ContourRenderer`'s level labels and marker
letters are CENTRE-aligned and numeric, and `RendererText`'s run splitter is exact only for
left-aligned text; and `PlotRenderer`'s VSWR readout can only emit `∞`, which Plex covers.
`HarmonicaPanelRenderer`'s own rotated Y2 label has the same shape and the same gap, but it draws
harmonicaRF's labels rather than Data Display ones — it is not this report and was not changed.

The gate is `tests/Ui.Tests/DataDisplay/AxisLabelGlyphFallbackTests.cs`, which asserts the coverage
facts above against the shipped faces, asserts the fallback changes the drawn PICTURE for the
reported label while leaving an ASCII one byte-identical (a splitter that measured with DejaVu and
still drew with Plex would leave the box on screen and pass every width assertion), and scans the
two renderers that cannot be driven without a canvas. **Load a face for an assertion through
`SkiaFonts.RealFace`, never `RealPlexRegular`** — the latter hands back the lazy cached instance the
renderers draw with, and disposing it takes the typeface out from under the whole process; it
crashed the test host here before the test ever ran.


## Owner report round, 2026-09-08 — the plot scales and the wsp matrix's indices

Three reports whose fixes live below the firewall. `src/Ui/RESOLVED.md` carries the two that do not.

### A Polar plot could not be scaled inside the unit circle

`Plot.AutoscaleEnforceUnityMinimum` — on by default, never set anywhere — unioned every complex-plane
autoscale with the unit square. That is right for a Smith chart, whose grid IS the unit disc: a Smith
chart framed smaller than |Γ| = 1 has no boundary. It is wrong for a Polar plot, which carries
whatever the trace is — ohms, siemens, a loop gain — and the floor made every small quantity a dot on
the origin, which looks exactly like a trace that is not being drawn. The WSProbe chapter's `1/H0`
locus (about 0.05 S) beside `1/Y0` (10-30 Ω) is the case that surfaced it.

**`UnityMinimumApplies` is the property AND `PlotType != Polar`.** Two consequences of taking the
floor away that were not obvious until it was gone:

- **`paddingComplex` existed and was applied to nothing.** The complex branch of `AutoscaleCore` read
  neither `padX` nor `padY` — the union with unity always left slack, so nothing missed them. Without
  the floor a locus would have touched the frame, so the Polar branch now runs `EnsureMinExtent` and
  the same `InflateRect` padding the Rect branch has always used. The constant was already there and
  already 0.02.
- **The radius numbers were gated on `axes.Window.Width < 8`** in `AxesRenderer.DrawPolarGrid`, which
  is safe only while a Polar plot cannot frame much more than the unit circle. A 59-unit-wide window
  drew a grid of unlabelled rings with nothing anywhere to say what scale it was at. The gate is now
  the canvas-width one alone.

### The `wsp` matrix's `row`/`col` read 0-based everywhere a user met them

`WspCubePacker` writes those axes with 1-based VALUES (`k + 1`) precisely so the reference document's
`wsp(r, c)` needs no index arithmetic. Only `i`/`j` — the S/Y/Z port axes — were being READ that way,
so the trace card offered `mag(SP1.wsp[:, 0, 0])` for the first element and the Y-axis label read
`SP1.wsp(row=1,col=1)`.

**Four sites, and they must move together** — the same rule `i`/`j` are already under:
`SliceTokenParser` (typed slice), `Trace.BuildPickerYExpression` (what the picker writes back),
`TraceLabeler.BuildCubeQuantity` (positional, so it reads `(2,1)` exactly as `S(2,1)` does), and
`Evaluator.ResolvePin`, which is the measure-line and expression path and resolves by axis VALUE
rather than by arithmetic. `TraceResolve.ApplyPinnedAxisDisplay` skips them for the same reason it
skips `i`/`j`: the label is positional, so a display token built there could never be used.

Note this is only ever the `wsp`/`wsp_passive` matrices — nothing else in the repo names an axis
`row` or `col`, and both cubes are 1-based by construction.

**A WSProbe TRACE was never affected**, and that is worth separating: `WspMetrics.PinToken` returns
null for `row`/`col`, so a `1/H0 @ GATE` trace never printed them at all. This was the plain-cube
route — picking `SP1.wsp` out of the analysis group and pinning the two axes by hand.

Gate: `tests/Ui.Tests/DataDisplay/WspMatrixIndexingTests.cs` and `PolarScaleTests.cs`.


## RND-1 — the render layer below the UI firewall (2026-09-07)

`docs/sonnet-briefs/brief-render-1-render-layer-below-the-firewall.md`. `CircuitRF.Render` exists,
`src/Ui` and `src/Cli` both reference it, `tests/Firewall.Tests` gates it, and the application looks
and behaves exactly as it did. Every visible feature is RND-2 onward.

### The measured closure, against the brief's prediction

§2's table predicted the renderers reach four `src/Ui` namespaces and named the types. The compiler was
used as the oracle rather than a grep: the files were moved, and each error was resolved by moving the
one file it named or by reporting what blocked it. The result is **larger than the table in eight files
and smaller in one**, and the differences are worth knowing about because each one says something about
where the seam actually is.

**Came down, and the table did not name them:**

| File | Why the renderer needs it |
|---|---|
| `LayoutGridMath.cs` | the grid pitch and the ruler tick step ARE the drawing |
| `LayoutShapeEditing.cs` | `XyOf` / `IsClosed` — how a shape's vertex list is read |
| `LayoutScaleHandles.cs` | `Build(bb)` — where the eight scale grips sit |
| `LayoutPortDirection.cs` | the conductor lookup a port arrow's direction and length come from |
| `LayoutSnapFeatures.cs` | declares `SnapCandidate` and `SnapFeatureKind`, which the snap overlay draws |
| `LayoutSnapQuery.cs` | `SnapQueryCounters` is in `LayoutSnapFeatures.QueryNear`'s signature |
| `LayoutSnapCandidateSet.cs` | in `LayoutSnapQuery`'s |
| `LayoutFlattenToPolygon.cs` | `LayoutPortDirection` measures a port's edge on the flattened outline |

None of them is a view model, a command or a canvas — they are geometry the renderer and the editors
share, which is the same category the brief's own table is made of. **`LayoutSnapFeatures.cs` is the
one worth a second look**: what the renderer needs from it is two type declarations at the top of the
file, and what came with them is a 615-line snap INDEX plus its two query files. R-rnd1-2 forbids
reshaping a file on the way down, and splitting a type out of one is reshaping it, so the whole file
moved. If `src/Render` is ever felt to be carrying editor machinery, this is the thing to look at
first — and the fix is a file split done deliberately, not a move done quietly.

**Did NOT come down, and the table said it would:**

- **`HarmonicaRenderTheme.cs`.** The table records it as Avalonia-free (true) and says to move it as a
  neighbour. It has a `ToPlotTheme` that returns `CircuitRF.Ui.DataDisplay.RenderTheme` — and the Data
  Display is **RND-4's** carve-out, explicitly separated so a hard half cannot hold an easy half
  hostage. Moving it now would either drag `RenderTheme` across (RND-4's job) or split the file
  (R-rnd1-2 forbids it). Nothing in `src/Render` references it; every caller is under
  `src/Ui/Harmonica/`. **It should move with RND-4, in one step, with the Data Display's own renderers.**
- **`ClipboardRenderPolicy.cs`.** Listed in §2's `CircuitRF.Ui.Theming` row. It reads
  `AppPreferencesIo` — the per-user preference store the same row says stays in `src/Ui` — and no
  renderer names it; its callers are the three clipboard copy paths. It stayed, on `AppPreferences`'
  own terms.

### R-rnd1-3: the one thing that would not come cleanly, and what was done instead

**`CellPins.Resolve` reaches `PCellRegistry`**, and the brief's grep did not see it because the grep was
over the renderers and this is one level down. `CellPins` itself had to move — `LayoutRenderer`
draws a sub-cell instance's pin markers through it, and so do `LayoutPortDirection` and
`LayoutSnapFeatures`, which the renderer also reads.

Behind `PCellRegistry` sit the six built-in generators, `Wire/`'s out-of-process worker transports and
Python-interpreter discovery, and **`PCellTrust`, which reads a per-USER preference through
`AppPreferencesIo`** — plus `GeneratedCellStore`, which reaches `CircuitRF.Ui.Schematic`. So the whole
of `src/Ui/Layout/PCells` (26 files, 6,783 lines) is Avalonia-free at the source level and could
*compile* below the wall, but two of its files are exactly the kind the brief says stays.

**What was done: `CellPins.GeneratorSource`, the same seam `LayoutTextOutline.TypefaceSource` already
is**, installed from `src/Ui` by a module initializer (`UiPCellGeneratorInstaller`, beside
`UiTypefaceInstaller`, which exists for the same reason and is documented there). Every frame the
application draws resolves generators exactly as before.

**What a headless process therefore loses, stated precisely rather than as a caveat:** `CellPins`
answers in two branches — the cell's own persisted pin list, else re-invoke its generator. A generated
PCell cell is an ordinary cell folder on disk and its pins ARE persisted, so branch one answers for
every such cell written since pins were persisted. Only a cell written BEFORE that loses its pin
markers, and it renders without them rather than with wrong ones. **The follow-up that would close it
is to move the built-in generators to `src/Design/Layout/PCells`, where `PCellContract` and the
`PCellGenerator` delegate already live** — blocked today by exactly two files, `Wire/PCellTrust.cs` and
`GeneratedCellStore.cs`.

### R-rnd1-4 — the fonts, and why the fix is not "it now works headlessly"

`SkiaFonts.Load` went through `Avalonia.Platform.AssetLoader`, which throws
`InvalidOperationException: Unable to locate 'Avalonia.Platform.IAssetLoader'` with no live app host —
and it **caught that and returned `SKTypeface.Default`**. Nothing errored; the picture was simply drawn
in a different typeface at different metrics, and reported as a success.

The nine faces `SkiaFonts` actually loads are `EmbeddedResource`s in this project now, read with
`Assembly.GetManifestResourceStream`, which needs no platform at all — the same fix `ShippedTechnologies`
and the `reference` pages in `src/Design` already use, for the same reason.

- **Nine faces, not the folder.** The folder is 15 MB; the faces `SkiaFonts` names are 3.8 MB of it.
  The rest exist for Avalonia's own `FontFamily` resources and `WindowsClipboard`'s RTF font table,
  which reach them through the `AvaloniaResource` link. Embedding all of them would have put 15 MB in
  two assemblies. **The consequence is a rule: add a face to `SkiaFonts` and add it to the `.csproj`
  in the same edit** — a named-but-not-embedded face resolves to `SKTypeface.Default`, silently.
- **`src/Ui` LINKS the files back, it does not copy them.** Its include is a blanket `Assets\**`, so
  the two link items had to be added deliberately, and the published output was checked rather than the
  `dotnet run` one: this is the class of build-file change that works in one and fails in the other,
  which is how `tools/pcell-python` shipped broken once.

### R-rnd1-5 — the theme, which is the same bug in a different costume

`ThemeResolver`'s chain is workspace dir → user themes dir → built-in provider → `ColorTheme.BuiltIn`,
and step 3 was installed only by `App.axaml.cs` (and the two standalone `App`s), out of
`avares://CircuitRF.Ui/Assets/Color/`. With no app host it was **never installed**, so a theme name that
resolves in the GUI fell silently through to `BuiltIn`.

The provider now DEFAULTS to a framework-free reader over this assembly's own resources, and the three
`App` classes no longer register one — a second copy of the same file reached a second way, shadowing a
default that works everywhere, is the arrangement that produced the bug.

**One trap this created, and it bit immediately:** `ThemeResolverTests` restored the provider by
installing a no-op (`SetBuiltInProvider(_ => null)`), which was correct when the default WAS nothing and
is now a way for one test to make every later test in the process resolve every theme to `BuiltIn`.
`ThemeResolver.ResetBuiltInProvider()` exists for that, and its doc comment says why.

### What gate 2 can be at RND-1, measured rather than assumed

§5.2 asks for byte identity between a render driven from a project with no Avalonia reference and the
GUI's own clipboard export. **The literal cross-process form is not constructible until RND-2**, for two
independent reasons, and both were measured:

1. **There is no second process to run it in.** `src/Cli` references `CircuitRF.Render` from this brief
   on, but has no verb that draws.
2. **A same-process comparison proves nothing.** Both sides call the same `SchematicRenderer.Draw` in
   `CircuitRF.Render` with the same `SkiaFonts`, so a typeface difference — the one failure mode gate 2
   names — cannot appear between them.

So the gate was built in the form that does bite, in
`tests/Ui.Tests/Render/RenderLayerBelowFirewallTests.cs`: **Skia's SVG device writes the typeface's
family name into every text run, so the exported file SAYS which face drew it.** The application's own
clipboard export is asserted to name `IBM Plex Sans` and NOT `SKTypeface.Default`'s family — which is
gate 3's claim through the production path, and is exactly what a headless render could not have
satisfied before R-rnd1-4. A vacuity guard asserts that `AssetLoader` still fails in that process, so
the assertion is known not to be true for the wrong reason.

**Two measurements taken while building it, both worth keeping:**

- **In-process byte identity IS available for the schematic and the symbol, and is NOT for the layout.**
  Two identical renders come back byte-identical for the first two; the layout differs in exactly one
  thing — Skia's `clipPath` id (`cl_3` vs `cl_4`), from a counter its SVG device does not reset per
  canvas. It appears there and not in the other two because only the layout renderer emits a clip.
  `LayoutClipboardExportTests`' note about that counter is right but narrower than it reads. The id is
  normalised only where the raw bytes actually differ.
- **The PDF needed NO exclusion.** §5.2 predicted a `SKDocumentPdfMetadata` creation date would have to
  be excluded by name; two renders of every fixture came back byte-identical without one. The stripper
  is written and applied only on a difference, so if a stamp ever appears it is handled and named rather
  than silently tolerated.

### The finding about a shipped verb, and why it turned out NOT to be optional

**R-rnd1-4 says: if wiring `LayoutTextOutline.TypefaceSource` to the new embedded loader would make
`convert`'s label flattening match the GUI's, say so and leave it — a finding, not this brief's
business. The measurement inverted that. LEAVING it is what changes behaviour.**

The seam was filled by `UiTypefaceInstaller` in `CircuitRF.Ui`, from `SkiaFonts.Plex*`. Before
R-rnd1-4 those properties fell back to `SKTypeface.Default` in every process, so the application and
`circuitrf convert` both flattened labels against the substitute and **agreed by accident**. Fixing
`SkiaFonts` moved the application's side to real IBM Plex and left the CLI's on the default, and
`ConvertCliVerbTests.ConvertingAClayToGerber_WritesWhatTheApplicationsOwnExportWrites` — which
compares the two byte for byte — failed on the label's flattened coordinates (`X35400` vs `X37012`).

So the installer moved to `src/Render` as `RenderTypefaceInstaller`, and the two sides agree again,
on the real face. `convert` now produces the glyph outlines the application produces. **Three things
had to be got right and each is a trap on its own:**

1. **A `[ModuleInitializer]` in a referenced-but-untouched assembly never runs.** .NET loads an
   assembly on first use, and `convert` reaches `GerberExport` in `CircuitRF.Design` without ever
   naming a `CircuitRF.Render` type. `CliEntry.Run` calls `RenderTypefaceInstaller.Install()`
   explicitly; the call is what loads the module, and it is greppable where a load-order accident is
   not.
2. **The installer reads `SkiaFonts.RealPlex*`, not the public properties.** Those honour
   `SkiaFonts.TestOverrideTypeface`, and label GEOMETRY is now compared against a separate process
   that has no override to read. `LayoutTextOutline` keeps its OWN override, which is the one the
   label tests actually mean.
3. **`CA2255`.** A `[ModuleInitializer]` in a library is a warning, and this project builds with
   `TreatWarningsAsErrors`. Suppressed at the declaration with the reasoning, which is that the rule's
   concern — a library surprising its consumer with work at load time — is the opposite of what this
   does.

**The wider point, and the follow-up it implies:** `LayoutTextOutline.TestOverrideTypeface` exists
because the embedded faces could not load headlessly. That reason is gone. What the field now selects
is a DIFFERENT FACE rather than "a face at all", so ~11 more test classes had to join
`LayoutTextOutlineTypefaceCollection` to stop them changing each other's glyph geometry. **The real
end state is to delete that override and let those tests use the real face** — a change to a lot of
fixtures, and its own piece of work.

### Two smaller things

- **`src/Design` grants `InternalsVisibleTo("CircuitRF.Render")`**, for the grant `src/Ui` already had
  and for the same reason: the renderers reach `CellHierarchy.LayoutBaseDirOf`,
  `LayoutFlattener.FlattenOpenEdgeList` and `LayoutTextOutline.ResolveLabelAnchor`, all `internal`.
- **`ComponentPreviewRenderer` split where it was already split** (R-rnd1-6): the `SKBitmap` halves are
  `CircuitRF.Render.ComponentPreviewRaster`, and `src/Ui` keeps a `ComponentPreviewRenderer` holding
  only `Wrap` and the two `Render*` methods. The Render-side class is deliberately NOT called
  `ComponentPreviewRenderer` — `src/Ui` has a `global using CircuitRF.Render`, and two types of one name
  in scope is an ambiguity error at every call site.


## RND-2 — what the `render` verb needed from this project (2026-09-07)

`brief-render-2-render-verb.md`. The verb itself is `src/Cli/Render.cs` and its findings are in
`src/Cli/RESOLVED.md`; three things landed here.

- **`SvgFontNormalizer.cs` came down from `src/Ui/Diagnostics`**, beyond RND-1's measured closure. It is
  framework-free and it was already on every SVG path in the repository — the three clipboard exports,
  the plot exporter and wBond's all pass Skia's output through `RepairPositionLists`, because Skia
  writes each text run's per-glyph position list with a trailing separator that Firefox reads as invalid
  and drops. Leaving it up there would have made the headless SVG differ from the application's by
  exactly that defect: correct in Chrome and Safari, unreadable in Firefox, reported as a success.
  `SvgPostPass` stayed — it is the docs generator's size pass, reaches `System.Xml.Linq`, and is not a
  renderer's concern.
- **`LodPixelThreshold` and `MergeShapeCountThreshold` grew the "off" branch the other six tier knobs
  already had.** `LayoutRenderOptions` documents that "a NEGATIVE value disables the tier outright,
  which is how an export pins exact vector geometry", and those two read `> 0 ? value : default` — so a
  caller asking for the tier to be OFF silently got the DEFAULT, in the one direction where the mistake
  produces a plausible picture of less geometry than the document holds. `EffectiveLodPixelThreshold`
  and `EffectiveMergeShapeCountThreshold` are the fix; nothing passed a negative before, so no existing
  caller changed behaviour.
- **`LayoutRenderResult.VerticesEmitted`**, on `PathsConstructed`' own terms and for its own reason —
  see that field's doc comment for what it deliberately does not count, and `src/Cli/RESOLVED.md` for
  the measurement that made it necessary.

`InternalsVisibleTo("CircuitRF.Cli")` was added for `MeasureLabelWorldBbox` (a label's stored bbox is
its ANCHOR, and framing on that is what cropped ports off a pasted page) and
`LayoutRenderDetail.ToleranceDbu` (the effective decimation tolerance the verb REPORTS). A second copy
of either measurement would be free to drift, silently.


## Post-RND-5 review — the LOD budget disagreed with the renderer about an undeclared layer (2026-09-07)

Found while reviewing the whole render series, not looked for. Older than the series: it moved here
from `src/Ui/Renderers` unchanged.

`LayoutRenderDetail.CanAffordOutlines` estimates a frame's outline cost by summing the vertices on
layers that will be painted. It built a set of **visible** keys from the technology and skipped any
shape whose key was not in that set — so a shape on a layer the technology **does not declare at all**
was not counted.

`LayoutRenderer.Draw` draws that shape. An undeclared key resolves through `FallbackPalette.For`,
whose `Visible` is `true`, and it is painted like any other. So the two disagreed, in the direction
that costs: the budget undercounted, and it undercounted **worst on exactly the documents where an
undeclared key is ordinary** — an import, which is also the only kind of document dense enough for the
budget to matter. The answer was "outlines are affordable" about a frame they were not affordable for,
and the symptom is a slow frame rather than a wrong picture, which is why nothing reported it.

It is `HiddenLayers` now — the keys the technology declares and hides — and the walk skips only those.
Null still means "no technology resolved, so nothing is hidden", unchanged. The rule is the one
`DocumentExtents.LayerVisibility` already followed and `LayoutRenderer.Draw` already implements: **a
key the technology does not mention is visible**, in all three places.

`src/Cli/RESOLVED.md`'s post-series section records the sibling defect this was found beside — the
`render` verb's own layer report and layer selection could not see those layers either.

---


## RND-4 — rendering a `.cdd` (2026-09-07)

`docs/sonnet-briefs/brief-render-4-data-display.md`. `circuitrf render <path.cdd>` draws a data
display, and the Data Display's models, renderers and trace resolution live in
`CircuitRF.Render.DataDisplay`. The verb's own half is `src/Cli/RenderDataDisplay.cs` +
`src/Cli/CddSources.cs`; `docs/design/cli.md` §13.7 and `docs/design/data-display.md` §2.9 are the
standing description.

### R-rnd4-1's measurement, in full — the finding this brief existed to produce

The brief's §2 predicted "the models and renderers can come down; the view models must not", and asked
whether trace resolution lives inside the 3,738-line `TraceRowViewModel`. **It does not, and that is
the whole answer to the question of whether this was bounded.**

**Where trace resolution actually lived, measured before any code moved:**

| Kind of trace | Resolver | Where it was |
|---|---|---|
| plain cube, expression, versus, network-parameter substitution, family | `SetCubeDataFrom` + `SetCubeDataFromCore` and ~12 private helpers | `PlotInspectorViewModel`, **all `static`, all taking a `DataSet`** |
| network element (S/Z/Y), derived scalar, stability circle | `Trace.BuildPath` → `BuildCubePath`/`BuildMatrixPath`/`BuildDerivedPath` | `Models/Trace.cs` — **a model, not a view model** |
| loadpull contour | `RebuildContour` (~90 lines) | `TraceRowViewModel`, entangled with a library lookup and three picker lists |
| summary table column | `RebuildSummary` + two column builders (~150 lines) | `PlotInspectorViewModel`, same shape |

So the 3,738-line view model is the trace CARD — pickers, combo synchronisation, undo — and it holds
exactly one resolver, whose ~90 lines of arithmetic are separable from the ~40 that find the surface.
`SetCubeDataFrom` was already cut as a static seam for harmonicaRF (R-h7-5). **The extraction was
bounded, and nothing in it required `DataDisplayViewModel` or `DisplayWindowViewModel` to come down.**

**The Avalonia surface, re-measured (the brief's own numbers were one round stale):**

| File | What it named |
|---|---|
| `Models/Plot.cs` | `Rect` × 47, `Size` × 1 |
| `Models/Trace.cs` | `Rect` × 27 |
| `Models/Axes.cs` | `Rect` × 14, `Color` × 5 |
| `Models/Misc.cs` | `Color`/`Colors.` × 35 |
| `Models/Marker.cs` | `Avalonia.Point` × 2, fully qualified |
| `Renderers/PlotRenderer.cs` | `Avalonia.Rect` × 7, fully qualified |
| `Renderers/RenderTheme.cs` | `Media.Color`, `Styling.ThemeVariant`, `Threading.Dispatcher`, `Application.Current` |

`TraceLabeler` and `AxesRenderer` named Avalonia only in COMMENTS — the brief counted them. Everything
else is `Rect`, `Point` and `Color`: value types with framework-free equivalents.

### What moved, and the four extractions

Fourteen `Models`, eight `Renderers`, eight parsers/resolvers, and `ComplexStringHelper` moved whole,
namespace `CircuitRF.Ui.DataDisplay` → `CircuitRF.Render.DataDisplay`, with a single `global using` in
`src/Ui/GlobalUsings.cs` and its mirror in `tests/Ui.Tests` — RND-1's precedent, and the reason the
~200 files that name these types were not touched. **All 12,885 `Ui.Tests` passed unchanged after the
move**, which is the evidence that it was mechanical.

Then four extractions, each a function BOTH sides now call: `TraceResolve`, `ContourResolve`,
`SummaryResolve`, `PlotConfigLoader` — plus `DataSourceView`, `PlotComposer`, `PlotDocumentWriter`,
`PlotCanvasGeometry`, `PlotLabelStrips`, `DataDisplayJson`. `PlotExporter` is now ~40 lines of
`Place(container)` plus its dialog and clipboard plumbing; `ExportAsync` had its own single-container
copy of the bounding-box fit and calls the shared composition instead.

### Five things that had to change, and why each was load-bearing

1. **`PlotRect`/`PlotPoint`, not `Rect`/`Point`.** src/Ui consumes this namespace through a global
   using and is full of `Avalonia.Rect`; a same-named type would have turned every one of those files
   into CS0104. The prefix is what keeps them apart, and a plot rectangle is not a control rectangle.
   The semantics are Avalonia's exactly — **including `Union`'s empty-operand special case**, which
   `Plot.Autoscale` depends on (it starts from `default(PlotRect)`) and which
   `tests/Ui.Tests/Render/PlotGeometryParityTests.cs` holds over all 49 ordered pairs of a fixture set.

2. **The trace-colour LUT is written out as explicit ARGB, not swapped for `SKColors.*`.** The two
   libraries disagree on one name: Avalonia's `Transparent` is `#00FFFFFF`, Skia's is `#00000000`. That
   is invisible while the colour is transparent — and not invisible at all once
   `RenderTheme.ToSKColor` overrides the alpha with the trace's own opacity, where the same stored
   choice renders white on one and black on the other. Only the colour INDEX is persisted, so no
   `.cdd` moved.

3. **`RenderTheme.GetTransparentAccent` did NOT come down.** It reads `Application.Current`'s resource
   dictionary on the UI thread — and the measurement is why it stayed: no renderer ever called it. Its
   three callers are `PlotControl`, `DragSelectOverlay` and `MarkerInfoBoxView`, all drawing SELECTION,
   which R-rnd4-7 lists among the things that do not come out in an export. It is
   `src/Ui/DataDisplay/PlotAccentColor.cs`.

4. **`AppSettings` came down and gained `Current`.** `AxesRenderer` read
   `AppSettingsViewModel.Instance.AlwaysDisplayDataSourcePrefix` and `PlotExporter` read three more.
   `AppSettingsViewModel` now wraps `AppSettings.Current` rather than its own `Load()`, so the Settings
   dialog and a headless render read one object. It has no disk persistence today, which is what keeps
   a CLI run reproducible; **if that changes, `RenderDataDisplay.Draw` is where a headless render would
   start depending on a preference file, and must not.**

5. **`DataDisplayJson`.** The `.cdd` serializer options were `DataDisplayViewModel.JsonOpts`. A second
   copy without `JsonStringEnumConverter` would read every `PlotType`, `FreqUnit`, `MatrixType` and
   `ContourColorMap` as its default — a Smith plot opening as a Rect one, which draws and is wrong.

### Two things the per-kind gate found that reading would not have

§5.2 asks for one fixture per trace kind. Two of the eight failed, and both were the same shape: a plot
that draws, exports cleanly, and is missing a curve.

- **The virtual `Z`/`Y` cubes** are materialized on the first read of a source's `DataSet`, inside
  `DataSourceEntryViewModel.Data`. A simulated S-parameter run carries `S` and `Z0` and no `Z` at all,
  so a trace on `SP1.Z` resolved to nothing headlessly.
- **The `NetworkView` SNP** is built from a grouped run's own S cube, likewise in the view model. A
  simulated run has no SNP by design, so `PlotConfigLoader`'s `snp is null → continue` guard dropped
  **every derived trace** — Max Gain, µ, µ′, |Δ|, passivity, and every stability circle — as the
  display opened.

Both are `DataSourceView` now, called by the library entry and by `CddSources`.

### What the gates measured

- **Byte identity against `PlotExporter`**, SVG and PDF, over eight trace kinds: a cube slice, an
  expression, a "plot versus", a derived metric, an S→Z conversion, a stability circle, a loadpull
  contour and a summary-table column. **The only normalisation is Skia's SVG element id counter**,
  which is per PROCESS and in hex — the test process has emitted other SVGs, so its ids are past
  `cl_29` while a freshly started CLI's start at `cl_3`, and they are not the same LENGTH. The PDF is
  compared with **no exclusion at all**: `PlotDocumentWriter` writes no CreationDate, so two runs of
  one composition are identical. (The written PDF's metadata `Title` is the output file's own name,
  which is what the GUI's Export writes; the test renders the application's side through the same
  writer with the same title rather than excluding bytes.)
- **The composition survived being parameterised** (R-rnd4-6): rendering one display at 792×612 and at
  1584×1224 puts every path coordinate at exactly 2×, which a re-fit would not — a re-fit changes the
  pad, and the pad is what would move a dragged marker info box away from its place.
- **The firewall row still passes.** `CircuitRF.Render.dll` references no Avalonia.

### A trap worth naming: `run.npy` is both a file name and the sentinel

`DataSourceRef.Selected` is the literal string `"run.npy"`, and `DataSourceLibraryViewModel.ResolveAbs`
short-circuits on it. So a results file actually CALLED `run.npy` cannot be selected — `ResolveAbs`
returns the (still null) `SelectedDataSourceAbs` instead of the path, `SelectedEntry` stays null, and
Add Trace silently does nothing. The flat results directory names a run after its schematic, so this
does not arise in practice; it cost an afternoon in a test fixture, and `CddSources` locates the
document's recorded `SelectedDataSource` as a NAME rather than through the sentinel branch for exactly
this reason.

### And one that only a byte-identity gate could have found: `SkiaFonts.TestOverrideTypeface`

`ScalarCubeTests` swaps the face every Data Display renderer draws text with, for one test, and
restores it in a `finally`. That is correct within its own class and no protection at all against a
class running CONCURRENTLY in another xUnit collection — and the window is invisible until something
compares rendered BYTES. `RenderDataDisplayCliTests` draws the same display in this process and in a
fresh CLI process, and the CLI has no override to read, so a plot drawn inside that window comes back
in **Helvetica** on one side and **IBM Plex Sans** on the other. Reliably green alone, reliably red
beside its neighbours — the shape of every shared-static hazard.

`tests/Ui.Tests/SkiaFontsTypefaceCollection.cs` is the sibling of RND-1's
`LayoutTextOutlineTypefaceCollection` (they are *different* statics: one is the face a layout LABEL
is flattened with, the other the face a PLOT draws with). `ScalarCubeTests`,
`PanAndMarkerLabelTests` — which renders pixels and was silently party to it — and
`RenderDataDisplayCliTests` are in it.

---

## WSP-4 — the WSProbe metrics, and the four things that were not obvious (2026-09-08)

`brief-wsprobe-4-ui-symbol-and-data-display.md`. The Data Display half landed here rather than in
`src/Ui` because `src/Cli`'s `plot` verb builds the same trace: the metric table, its gating, the
value production and the readouts are in `src/Render/DataDisplay/` and both consumers call them.

**A probe trace is a cube trace, and that decided everything downstream.** Its `CubeName` is the run's
own `…wsp` matrix and its values are substituted at `TraceResolve.SetCubeDataFromCore`'s single
interception point — the one a renormalized S/Z/Y cube already used. The slice, the family/slider
mechanism, the markers, the Table, the export, `.cdd` persistence and `render --data` then needed no
probe-specific path at all, and a swept `wsp` (`{Pin, freq, row, col}`) becomes a metric cube over
`{Pin, freq}`, which the family marker `~` already draws one curve per sweep point of. R-wsp4-10 cost
nothing because of this; a parallel resolve path would have cost it twice and drifted.

### 1. Case is load-bearing in the document's metric names, and folding it is a silent wrong answer

The first `WspMetrics` name table canonicalised to lowercase-alphanumeric and threw at class-init on a
duplicate key. The duplicates are real quantities: the document writes **`LGF`** for one probe's
forward synthetic-circulator loop gain (Eq. 99) and **`LGf`** for a probe PAIR's
feedback-as-synthetic-FET loop gain (Eq. 149), and the same collision exists for `LG_H`/`LGH` and
`LG_MF`/`LG_MR` against `LGM`. Had the dictionary been built with an overwrite instead of an `Add`,
`metric=lgf` would have resolved to whichever came last and drawn a plausible curve.

The lookup now resolves the exact spelling first, then the enum member name case-SENSITIVELY, then a
small alias table for the names a shell cannot type (`1/H0` → `invH0`), and only then the loose form —
built from names that have **no** collision under it, so `lgf` resolves to neither rather than to one.
A name that collides is reachable only by its own spelling, which is the honest behaviour.

### 2. Skia's SVG device silently drops a stroke's path effect

`SKPathEffect.CreateDash` on a `DrawLine` renders on the PNG and PDF backends and produces **nothing
at all** in the SVG — no `stroke-dasharray`, no path, no error. The margin threshold line was missing
from every `.svg` while the solid floor line beside it was present. `DrawWspMarginReferenceLines`
emits the dash as segments instead, so the three exports agree — which is the property
`render`'s own byte-identity gate rests on. Anything else in the renderers that reaches for a path
effect will have the same problem.

### 3. A margin level is stated in dB and the axis may not be

`Trace.WspMarginLevelInDisplayUnits` first passed a dB level straight through for `dB20`, `dB10` and
`dB` alike. `dB10`/`dB` render `10·log10` of the same ratio, so the −12 dB floor belongs at **−6** on
that axis; unconverted, both reference lines sat below every curve. Visibly wrong on a linear axis,
and quietly wrong on a `10·log10` one — which is exactly where a reader would take a margin off the
drawing and believe it. The level is now converted per transform (overview D-16 fixes the margin's own
convention at `20·log10`).

### 4. The virtual reduced-two-port groups must be appended AFTER the analysis groups

`DataSourceView` adds one network group per probe (`<analysis> ▸ WSProbe <label> ▸ reduced 2-port`,
carrying `S`/`Y`/`Z`/`Z0`) so that µ, µ′, K, |Δ|, MAG/MSG and both stability circles apply to the
document's own two-port reduction (Eq. 44) through the code that already computes them — the cheapest
new capability in the series, and free only because it is an ordinary network group.

`DataSetBuilder.FindCubeSpec` answers with the FIRST group carrying an `S`, and that has to keep being
the run's own: a network view built from a probe's reduction would quietly replace the amplifier's own
S-parameters in every metric that takes one. The groups are therefore materialized after the
`Z`/`Y` pass, and a test asserts `FindSCubeSpec` still names the analysis group.

**The scattering conversion is `WspMatrix.ScatteringOfY`**, the same one WSP-3 scatters a probe pair's
blocks with (Eq. 142/143) — not a second S-from-Y — so a circle read off the reduction and a pair
block's own S-parameters agree by construction.

---

## WSP-4 (finished) — the Envelope sub-card, and the rest of the virtual network groups (2026-09-08)

R-wsp4-9 and the second half of R-wsp4-6, the two things the entry above records as absent. Four
findings.

### 1. A virtual network group must FILE the matrix the library returned, not a round trip of it

`AddNetworkGroup` first built `S` from whatever it was handed and then derived `Y` and `Z` back out of
that `S`. For a group whose source quantity IS `Y` — `wsp_ymatrix` over a probe set (Eq. 185) — that
puts a Y→S→Y round trip between the library call and the cube, and the group's own `Y` then differs
from `WspGlobal.Ymatrix` in the last few digits: measured `0.02666666666666666` against
`0.026666666666666717`.

The failure is not the error, it is what the error invites. R-wsp4-14(a)'s gate is **bit identity with
the library**, so a test comparing the group against `WspGlobal.Ymatrix` fails, and the only fix
available to it is a tolerance — which then hides a second implementation for good. The cube the
function returned is now filed unconverted and only the other two are derived, so the gate can stay
exact. `wsp_block_breakout` returns S and is filed as S by the same rule.

### 2. The pair blocks cannot be materialized eagerly, and the brief's own sentence says so

The reduced two-port is one group per probe: N groups. A probe PAIR is `N(N−1)` ORDERED pairs and four
blocks each, so eager materialization is `4N(N−1)` groups of four cubes — on the 30-probe matrices §8's
NDF work contemplates, thousands of cubes nobody asked for, each the size of the sweep.

R-wsp4-6 reads "a second picker (\"with probe\") **enables** the pair group", and that is the design
rather than a phrasing: `DataSourceView.EnsureWspPairBlockGroups` is called when the card's picker
names a pair, and a `.cdd` that names one re-creates it by the same call. It is idempotent, so changing
the pair adds the new blocks and leaves the old ones — which are still true. The FULL probe set's `[Y]`
is materialized eagerly, because it is the one set that needs no picker at all.

**The arrow is part of the identity.** `A→B` and `B→A` bracket two different two-ports (Fig. 40 is
GEN → LOAD), so they are two groups; the gate asserts their `S` actually differs, rather than trusting
the naming.

### 3. `wsp_block_design` is INVARIANT to the frequency it is given, on the default RC pair

Written expecting the opposite. `WspPair.BlockDesign` turns each side's bidirectional impedance into a
parallel RC pair at `freqHz` (`ImmittanceModels.ZToPc`) and then renormalises to that pair **at the
same `freqHz`** — so the frequency cancels and the reference is the impedance it came from, whatever
was passed. A test written to catch a frozen frequency therefore passed under the frozen frequency too,
which is how this was found.

It stops cancelling the moment the design use the document describes is taken up (p. 88: "any RC pair
may be replaced by the user's own"), so each point is still handed its own frequency. The consequence
worth carrying forward is the other way round: **a wrong frequency here would be invisible on this
path**, so nothing downstream may take the frequency broadcast on trust. `FreqPerBlock` is written to
broadcast along the freq axis's own POSITION for that reason, rather than assuming it is outermost.

### 4. The envelope cube has four grid axes, always, and θ is in degrees

The expression engine returns `{…, gS, gL, …}` — one flat, index-valued Γ axis per side, labelled
`0.9@60`. The card cannot use that shape: [E] Fig. 6–9 read the margin **against phase**, and an
ordinal axis cannot be read that way. So `WspEnvelopeSource` lays the same flat grid out over `rhoS`,
`thetaS`, `rhoL`, `thetaL`, with θ carried in degrees and ρ as the ladder's own magnitudes. The samples
underneath are the library's, in the library's own grid order, which is what the gate asserts.

**All four axes are present whichever side is pulled** — an off side contributes two length-1 axes
rather than disappearing. A trace's slice is matched by axis NAME, so a rank that changed when a ladder
was typed would leave the slice describing a cube that no longer exists. That also decided
`TraceRowViewModel.SyncWspSlice`: a probe metric whose axis NAMES differ from the trace's current slice
re-authors it, and only then — an ordinary edit (a different probe, a new Z0) leaves a slice the reader
has arranged exactly as it was.

**The `plot` verb had the same defect and it was the same one line.** It built the slice from
`WspSource.LeadingAxes`, which for an envelope metric pins `freq` — an axis `SMenv` does not have —
and leaves the four grid axes unpinned. It now slices against the cube the resolve already produced,
and picks the pulled side's θ as x when there is no frequency axis.

### Not built, deliberately: the θS × θL grid as a COLOURED map

R-wsp4-9 asks for the stability map "drawn as a θS × θL grid coloured by the unstable count". The count
is produced and is an ordinary real cube over the grid axes, so it draws through the family mechanism
(one curve per θL) and reads in the Table. A filled raster of the grid would need the Data Display's
**heatmap fill**, which `brief-dd-loadpull-contour-ux-round8` §3 deliberately withholds from the UI as
experimental — the picker offers only None/Topography, and the machinery is kept only so a saved
`.cdd` still loads. Adding a second, rectangular-grid raster path beside a withheld one is not this
brief's call to make. The readout names the first flagged termination and how many there are, which is
the reading the map exists for.
