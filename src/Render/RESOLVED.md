# src/Render — resolved briefs (detail, off the CLAUDE.md growth path)


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
