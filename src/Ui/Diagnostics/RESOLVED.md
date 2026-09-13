# Diagnostics — resolved findings (detail, off the CLAUDE.md growth path)

Same pattern as the other `RESOLVED.md` files in this repo: a completed investigation's detail lands
here, and `CLAUDE.md` stays for durable, still-true conventions only.

## The crash report says how the process EXECUTES code, not only what it runs on (2026-09-03)

Four header lines were added for one specific dead end. A field report that has survived six rounds
(`src/RfCore/RESOLVED.md`) is a managed exception whose own arithmetic says it was unreachable: an
in-range array index that faulted anyway, on an immutable object, with the read's own replay
succeeding. Once the application's state is exhausted as an explanation — and it now is, positively
rather than by failure to reproduce — what remains is the layer underneath. None of it appeared in any
report:

```
debugger    : no
gc          : workstation, Interactive
profiler    : none
codegen env : all default
modules     : 214 loaded, from elsewhere: SomeEndpointHook.dll
```

- **`profiler`** covers `CORECLR_ENABLE_PROFILING`/`CORECLR_PROFILER` and the legacy `COR_*` spelling,
  because injectors set either. A CLR profiler can rewrite IL and force rejits, so its presence
  changes what "the same code" means — and endpoint-protection and APM agents inject them routinely
  on managed corporate desktops, which is exactly the environment this report comes from.
- **`codegen env`** lists only knobs that change codegen (`DOTNET_TieredCompilation`,
  `DOTNET_TieredPGO`, `DOTNET_TC_QuickJitForLoops`, `DOTNET_ReadyToRun`, `DOTNET_JitMinOpts`,
  `DOTNET_ZapDisable`). Unset is the shipping configuration; saying "all default" explicitly is what
  makes a set one stand out.
- **`modules`** lists native modules loaded from neither the OS nor the application directory — i.e.
  injected. Classified by path, reported by **name only**: which DLLs are in the process is
  diagnostic, where they live on the reporter's disk is not ours to collect.

**The negative answer is the point.** "no / none / all default / nothing injected" narrows the field
as usefully as a named module does, which is why all five lines are written unconditionally rather
than only when something is set.

**Platform caveat, not a bug:** `Process.Modules` enumerates the full loaded-module list on Windows
and reports only the main module on macOS. The line therefore reads `1 loaded` on a Mac. Windows is
where the report comes from and where the enumeration works, so this was not worth a second
mechanism — but do not read a Mac report's `modules` line as evidence of anything.

### `Dispatcher.UIThread.CheckAccess()` is not a free read

Every trail note now carries its thread (`[07:34:56.246 t1]`, and `t7!ui` off the UI thread), because
a burst of identical notes is a different event depending on whether one thread produced it or
several did.

The obvious implementation is wrong. **Reading `Dispatcher.UIThread` has a side effect: the first
access CREATES the dispatcher, bound to whichever thread asked.** A diagnostic that consulted it could
bind the UI thread to a worker merely by noting something early in startup — a diagnostic that changes
the thing it observes, in the one direction that would be hardest to notice.

`CrashReporter.MarkUiThread()` captures the managed thread id once from
`App.OnFrameworkInitializationCompleted`, which runs on the UI thread by definition, and `Note`
compares integers. Before it has run there is no UI thread to be off, so an early note is unannotated
rather than wrongly flagged. It also keeps `CrashReporter` free of any Avalonia reference. Held by
`CrashReporterTests.EveryNote_CarriesItsThread_AndMarksTheOnesThatAreNotTheUiThread` and
`TheHeader_RecordsHowTheProcessIsExecutingCode_NotOnlyWhatItRunsOn`.

---

## Adding a docs page in a NEW section: two traps, both silent (2026-09-12, AN-01)

Found while adding `docs/user/src/app-notes/` — the first section outside `reference/`,
`quick-start/` and `new-user-guide/`. Neither trap produces an error; both produce a page that
generates cleanly and is wrong.

**1. The middle breadcrumb segment IS the directory name, slugified.**
`HtmlEmitter.Breadcrumb` links every segment except the last, and builds the href by
`Slugify(segment) + "/index.html"` where `Slugify` is `ToLowerInvariant().Replace(' ', '-')`. So a
page in `app-notes/` whose front matter says `breadcrumb: Docs > Application Notes > …` emits a link
to `application-notes/index.html`, which does not exist. **Nothing checks it** — the orphan check is
over `_nav.txt`, not over hrefs. The fix is to spell the middle segment so it slugifies to the real
directory (`Docs > App Notes > …`). The existing sections hide this because their section word
already equals their directory (`Reference` → `reference/`). A section INDEX page is unaffected: its
section name is the last segment and last segments are not linked, which is why
`reference/index.md` can say `Docs > Reference Guide` and be fine.

A cheap guard, worth running after any docs change, is a site-wide href existence sweep over
`docs/user/**/*.html`; it takes under a second and would have caught this.

**2. `DocLayoutFixtures.Framed` does not centre correctly at extreme aspect ratios.**
Its arithmetic (zoom = min(w/worldW, h/worldH), then an x0 that centres) is right, and it is right on
screen for the existing figures — all of which are between about 1.3:1 and 3:1. On a 5:1 part in a
4.6:1 frame the rendered result came out with the content scaled ~1.15× about the top-left rather
than centred, so it overran the right edge and cropped the outermost port while leaving a large left
margin. Root cause not chased; the practical rule is:

- **Keep the figure WIDTH-limited**, i.e. choose `height` so `height/worldH > width/worldW`. That is
  the regime every correctly-framed existing figure is in (`ports-edge` frames exactly as computed).
- **Then leave real slack** — `marginX: 0.32` put the four-port coupled-pair figure comfortably
  inside its frame at 880×190, where 0.12 and 0.22 both cropped it.
- **Verify by rendering, do not trust the arithmetic.** `qlmanage -t -s 1200 -o <dir> fig.svg`
  produces a PNG that can be read directly. Note that qlmanage does not draw the port bar/arrow
  markers — the reference figure `ports-edge.svg` behaves the same way, so a missing marker in a
  qlmanage render is the renderer, not the figure.

**3. Unrelated: the committed `docs/user` output is STALE against the current build.** A regeneration
with no source change rewrites ~65 figures and pages — a toolbar icon (`SineWave` → `none`), a
Settings string, a ~2 px shift in the wBond profile figure, and others. These are real content
drift, not the hex-id churn and not the three known nondeterministic families: they are stable across
two consecutive runs and differ from `HEAD`. So `tools/DocGen/check-docs-current.sh` fails on a clean
tree for reasons that predate any current change, and anyone adding one page must revert the other 65
rather than commit them.

---

## AN-01's measured data was stale against the port-calibration series (2026-09-13)

The app note was written while a port whose feed had a driven neighbour inside the clearance
threshold could not be calibrated at all, so **every number in it was of a run that no longer
happens**. PCAL3, PCAL4 and PCAL5 changed the outcome, not the wording: a portless neighbour goes
into the standard, two ports at one reference plane are one calibration group with a modal error
box, and a group member that grew a feed lead is peeled with the mode's own γ. On
`testdata/portcal/coupled-pair` the note's headline figures went from S(2,1) = −34.2 dB at 1 GHz and
non-passive everywhere to **−0.14 dB and max |ΔS| 0.0454 against the two kernels' own 0.0521
agreement floor** — so the table the note argued from was not merely imprecise, it was of the
opposite result.

**The lesson is about app notes specifically.** A reference page describes a control and ages slowly;
an app note is an argument built on a measurement, and when the engine's answer changes the argument
inverts silently. Nothing in the build checks a note's numbers against a run — the figures are
regenerated, the prose is not. Two cheap habits follow:

- **Name the fixture in the note** and have the note's numbers come from it, so re-measuring is one
  command rather than an archaeology exercise. AN-01 now names
  `testdata/portcal/coupled-pair` and quotes the `em` verb that runs it.
- **Write what the tool does, never what it used to do.** The old note's structure was a narrative of
  a failure and its diagnosis, which is exactly the shape that goes stale: half of it existed only to
  describe behaviour that no longer exists (owner, 2026-09-13 — a reader new to circuitRF is asking
  how to do the thing, not what used to go wrong).

**A result figure that costs a full-wave sweep goes in the catalog as `Static`.** AN-01's two plots
(`an01-coupled-pair-return-loss`, `an01-coupled-pair-through-coupled`) are drawn by
`DocCoupledLineFixtures`, which resolves the committed `.cem` through `EmSetupResolver` and runs
`EmRunService.Run` at 31 points into `DocRunData.ResultsRoot` — never into `testdata/`. That is ~30 s
built Release and several times that in the Debug build a plain `dotnet run` produces, which does not
belong in an ordinary regeneration; `--rebuild-static` redraws them, exactly as the antenna patterns
work.

**The two-port figure went with the argument.** `an01-coupled-pair-2port` existed only so the note
could reason from the difference between four ports and two. With that section gone it is worse than
unused: a picture of a coupled pair carrying ports on one line only, under a page about coupled
lines, reads as the recommended arrangement to anyone scrolling (owner, 2026-09-13). Its catalog row
and `DocLayoutFixtures.CoupledPairTwoPorts` are deleted along with the two committed SVGs — nothing
in the build forbids an unreferenced catalog row, which is exactly why one has to be removed by hand
when the page that cited it stops citing it.

Two mechanical traps met on the way:

- **The data-source picker enumerates `results/*.npy` only.** Handing `DocDataDisplayFixtures.Sourced`
  the `.sNp` beside it still loads and still plots, so nothing fails — but the toolbar's source combo
  renders **empty**, which no other Data Display figure does. Use `EmRunResult.NpyPath`.
- **Two coincident curves are indistinguishable from one.** S(1,1) and S(2,2) of a uniform line agree
  to 0.0008 dB, so the return-loss figure drew as a single trace with two Y-axis label strips beside
  it. `DocDataDisplayFixtures.Dashed` sets the second trace's line style through the trace card's own
  picker; the coincidence is then the thing the reader can see.
