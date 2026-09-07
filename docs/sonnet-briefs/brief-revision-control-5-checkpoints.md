# Sonnet Brief — RC-5: checkpoints and restore points

**Read `brief-revision-control-0-overview.md` first.** The architecture is
`docs/design/revision-control.md` §5.1, §5.2a, §5.3, §5.3a, §5.3b, §5.5, §5.8, §5.9, §4.4, §4.6, §8.2,
§8.2a, §8.1a, §9A.3 and §9A.6, plus **§5.3c and §5.7a (new in rev 4)**. **Depends on RC-3 and RC-4.**
**Six of those sections are new in rev 3 and this brief grew accordingly** — the checkpoint reference shape, the batch protocol, the
agent-facing contract, restoring, scratch, and the first commit into a workspace that already exists.

**Scope: the safety net.** Automatic checkpoints on three boundaries, committed to a reference outside
the branch, surfaced as restore points with no git vocabulary anywhere. **This is the stage that
discharges §1.2**, and per §12 Q5 it ships **before** AI design capability exists — the floor goes in
before the thing it is a floor under, not beside it.

---

## 1. Two histories, one repository

**R-rc5-1. Each checkpoint gets its OWN reference, in circuitRF's own namespace, outside the branch
the designer sees** (§5.2a), so none of them appears in the history a designer browses.

**The per-checkpoint shape is not a preference — it is what makes RC-6 possible.** RC-6 *thins*: it
drops individual checkpoints out of the middle of a series while keeping the newest *N*. A single
reference walking a chain of commits **cannot express that at all** — a chain can only be truncated
from the oldest end, and truncation leaves everything before the cut still reachable, so it thins
nothing. With a reference per checkpoint, dropping one is a single reference delete: the pointer goes,
the objects stay, which is exactly RC-6's "thins, never prunes".

**R-rc5-1a. Checkpoints are local and do not travel over a network** (§5.2a). A clone does not copy
them — git's default fetch takes branches and tags, and nothing under a private namespace — and a push
does not carry them. **This is correct rather than a defect to repair in RC-9**: the safety net is a
property of one machine and one designer's sessions, ordered by R-rc5-8's monotonic sequence, which
means nothing on anyone else's machine, and thinned by that machine's retention preference.

The consequence is that the three ways a workspace leaves a machine disagree, and **§10B.1 gets a row
for each rather than leaving anyone to discover it**: an archive with history carries checkpoints
(it copies the directory), a clone does not, and a Save Workspace As copy carries no history at all
(R-rc5-19).

**Why two histories at all.** A single history cannot serve both motives: the dense, automatic,
machine-written safety net and the sparse, deliberate, human-written narrative have different authors,
different granularity and different audiences. Conflating them produces a log no
human will read — **which then makes the safety net useless too, because nobody looks at it.**

The pattern is not novel: some development environments keep exactly such a local history, independent
of whatever version control the project itself uses, for exactly this reason. Git's own reflog is the
same idea.

**R-rc5-2. They are presented as restore points and nothing else** — *"14:32 · before 'Widen the
output match'"*. No commit, no branch, no reference name, no identifier (R-rc0-6). RC-7 adds the
narrative history; this brief adds none of its vocabulary.

**R-rc5-3. There is deliberately no relationship to undo** (§5.4). The undo stack is per-view,
keystroke-grained and session-lived; a checkpoint is workspace-wide, action-grained and durable.
Fusing them would force one granularity on both and gain nothing. **The checkpoint is the bridge
between them:** undo covers the last few minutes within a view, the checkpoint covers everything else.

---

## 2. The three boundaries

**R-rc5-4. A checkpoint marks a boundary, never a write, and never merely the passage of time.**

| boundary | why | switchable? |
|---|---|---|
| **before an AI-initiated batch** | §1.2 — this is the motive | **no** |
| **an explicit save-point the user asks for** | the user's own judgement about what matters beats any heuristic | n/a — it is the request |
| **on workspace close** | the one boundary that reliably exists in every session, including the ones where the designer never thought about history | yes (RC-4) |

**Not on every save**, and the reason is not cost — §2 measured a commit per save at single-digit KB
and it is affordable. It is wrong because **a save is not a boundary**: one logical design action can
write a `.clay`, the workspace file and a `.csch`, so a commit per file save records fragments of an
edit, some of which are not internally consistent. And because **a history nobody can read is not a
history**: hundreds of undifferentiated entries per session defeat the one thing the safety net is for.

**R-rc5-5. Simulation run and idle timeout are excluded, and the reasons are recorded so they are not
revisited casually.** A run is attractive as a "this is the state I measured" marker, but runs are
frequent, often unchanged from the last one, and a parameter sweep would generate dozens of
near-identical checkpoints — and it silently couples history to the analysis engine, which the staging
is specifically arranged to avoid. An idle timeout produces a checkpoint the user cannot predict, at a
moment meaningful to nobody, labelled with a time rather than an intent; and "idle" during a long
simulation is not idle at all.

**R-rc5-6. The AI-batch boundary is defined where the BATCH is known, not where a file is written.**
The only agent-facing surface today is `src/Cli/Serve/` — out of process and headless — so the
checkpoint must be takeable with no display and no open window, which is why RC-3 put the git type in
`src/Design`. **The batch's own stated intent is what labels the checkpoint** (*"before: widen the
output match"*), because a label that says only a time is the unreadable log §5.3 opens by rejecting.

### 2.1 The batch protocol (§5.3a)

rev 2 of the architecture named the most important checkpoint in the design and gave it no mechanism.
§5.3a supplies one, and **it cannot be deferred to the AI work, because §12 Q5 says this ships first.**
Five rules:

**R-rc5-6a. The agent declares the batch; circuitRF does not infer it.** A batch is opened and closed
explicitly, and the checkpoint is taken **when it opens, before the first modification**. Inferring one
from a run of write operations requires a timeout, and a timeout is §5.3's rejected idle trigger under
another name (R-rc5-5).

**R-rc5-6b. The declaration carries the intent, in the agent's own words, because that is the label.**
A batch that supplies no intent is labelled as what it is — an unnamed batch — and never with a bare
time.

**R-rc5-6c. Batches do not nest**; a second open inside an open batch is the same batch. Two
checkpoints around one logical action is the unreadable log §5.3 rejects, and the designer's question —
*what did this look like before the agent touched it* — has exactly one answer.

**R-rc5-6d. An unclosed batch is not a failure and needs no repair.** The checkpoint was taken before
anything was modified, which is the whole of what §1.2 asks; if the agent dies mid-batch, the restore
point is exactly where it should be and the next boundary closes it. **The safety net must have no
failure mode that depends on an agent behaving well** — that is the population it exists to protect
against.

**R-rc5-6e. Reverting a batch is restoring to its checkpoint** (§5, R-rc5-12a), offered to the
**designer**, not to the agent. §1.2 asks for *revertible in one action*; it does not ask for a tool
call an agent can use to erase what it did.

**R-rc5-6f. A batch against a workspace that cannot be checkpointed is refused BEFORE anything is
modified.** Off (RC-6 R-rc6-15), held (RC-6 R-rc6-7) and scratch (R-rc5-20) all resolve the same way at
the same moment, because **a floor announced after the fall is not a floor.**

### 2.2 The agent-facing contract (§5.3b) — what `serve` advertises, and the rules it publishes

**R-rc5-6g. The state is advertised, not inferred.** The `serve` surface reports, for the open
workspace: whether revision control is **on, off, held or unavailable**; whether a batch is open and
what its intent was; and where the repository root is. **An agent left to deduce this from the
filesystem will deduce it wrongly** — a `.git` directory alone does not distinguish a repository
circuitRF created from one the user created, and RC-6 makes those opposite situations.

**R-rc5-6h. The rules are published by the surface, next to the state.** A capable agent has a shell
and a `git` of its own; it can read this repository and write to it whether or not anyone intended
that. Publishing the rules means an agent learns them from the server rather than from having read the
architecture. §5.3b holds the canonical ten; reproduce them, do not paraphrase them:

> 1. **Open a batch before your first modification, and state your intent in one line.** A batch
>    opened afterwards protects nothing.
> 2. **Read git freely; write only through the batch.** `log`, `show`, `diff`, `status` and
>    `rev-parse` are the fastest way to answer *what changed*. Every write is circuitRF's.
> 3. **Do not commit** — not to the designer's branch, which is their own record, and not to
>    circuitRF's checkpoint namespace, which carries metadata and an ordering sequence you cannot
>    supply.
> 4. **Do not rewrite, and do not reclaim.** No amend, rebase, reset, filter or forced push; no `gc`,
>    `prune` or `reflog expire`. RC-3's configuration exists so a mistaken deletion stays recoverable
>    for weeks, and one housekeeping command undoes that for everyone.
> 5. **Do not change what the working tree contains.** No checkout, branch, switch, stash or `restore`
>    — the files are open documents in a running application (R-rc5-12b).
> 6. **Do not touch configuration or `.git` itself.**
> 7. **Do not reach the network.** Fetch, push and clone are explicit user actions.
> 8. **Do not force a file past `.gitignore`.**
> 9. **If the batch cannot be opened — off, held, no git, or an unsaved workspace — stop and say so.**
>    Do not substitute a commit of your own, a folder copy, or any other improvised backup.
> 10. **Never report a checkpoint you did not take.**

**R-rc5-6i. Rules 3 and 9 are the load-bearing pair and the implementation must make them easy to
obey.** Everything else prevents damage; those two prevent the **helpful** failure — an agent that,
finding no checkpoint mechanism available, makes its own arrangements and reports success. That
produces either commits in a history the designer did not author (R-rc0-5) or a private backup nothing
knows about, and in both cases the designer is told they are protected by a mechanism they cannot
restore from. **A refusal must therefore be unambiguous and must name what the agent should do
instead**, because an agent given a vague refusal will improvise, which is what rule 9 forbids and what
good tool design should make unnecessary.

### 2.3 The window the batch is editing underneath (§5.3c, new in rev 4)

§5.8 works this out in full for a **restore** — reload the documents, discard their undo stacks — and
§5.3b rule 5 forbids the agent from producing the same state through `checkout` or `stash`. **The
agent's own edits, through the batch, are that identical situation arriving by the front door**, and rev
3 had no rule for it.

The answer today is nothing. `WorkspaceModel` records that refresh is **manual and on focus with no
`FileSystemWatcher`** (`src/Ui/Schematic/WorkspaceModel.cs:7`–`:9`, deferred by
`workspace-and-project-tree.md` §9) — and a project-tree rescan is not a document reload in any case. So
a batch that edits a `.csch` the designer has open leaves the window showing the old content over an
undo stack describing edits the file no longer contains, **and the window's next save discards
everything the agent did.** That is §7A.3's two-editors-one-file failure — which the architecture
already calls worse than the divergence RC-2 exists to prevent — reached through the mechanism §1.2 is
the motive for. A checkpoint protects the history and does nothing whatever about it.

**R-rc5-7a. A batch is refused while the window holds unsaved changes to that workspace, before anything
is modified.** §5.8 resolves this for a restore by *asking*, but a batch is headless and out of process
and there is nobody to answer a prompt. A refusal is the only honest answer; it is **the fourth member
of R-rc5-6f's family** — off, held, scratch, dirty — refused at the same moment and for the same reason,
and §5.3b rule 9 already tells the agent exactly what to do with it. The designer saves and asks again.

**R-rc5-7b. On batch close, the documents the batch modified are reloaded and their undo stacks
discarded.** §5.8's rule, for §5.8's reason, through §5.8's code path — R-rc5-12b builds it and this
requirement is a second caller, not a second implementation. **R-rc5-7a is what makes it cheap:** with
nothing unsaved anywhere, a reload cannot lose anything, which is why the refusal comes first.

**R-rc5-7c. The window learns the batch closed; it does not discover it on focus.** The on-focus rescan
is the wrong mechanism twice over — it rebuilds the tree rather than the documents, and a designer
watching an agent work never leaves the window, so it may not fire at all. The state is already
advertised (R-rc5-6g) and the two processes already share the workspace's advisory lock (R-rc3-1b); the
observation belongs on one of those. **Do not add a filesystem watcher** — `workspace-and-project-tree.md`
§9 defers one deliberately and this does not un-defer it.

**What this must not become is a window the agent shares.** One file, one editor (§7A.3) still holds:
during a batch the workspace's documents belong to the batch, and after it they belong to the window
again. Any design where both are live at once is the failure above with more machinery.

**R-rc5-7. A checkpoint captures everything, because §1.2's whole point is capturing files the user did
not open.** It is `commit -a`-shaped by nature. That is exactly why R-rc0-5 exists: in someone else's
repository this is not a safety net, it is an ambush. RC-6 builds the detection; this brief must not
commit without it.

---

## 3. Ordering, which does not come from the clock

**R-rc5-4a. A workspace is armed at the first boundary that would record something, never at open**
(§5.7a, §12 Q13, R-rc0-13). RC-4 ships the preference that decides *whether*; this brief owns *when*, and
the distinction is not cosmetic. **Opening a workspace to look at it creates nothing.** The case that
makes it matter is the one §7A and §4.7 both assume — a workspace on a share, belonging to somebody
else — where creating a repository because a colleague glanced at the folder is R-rc0-5's ambush pointed
at a directory instead of a history. It is also the whole of R-rc3-8's objection (*"a repository
appearing in a folder because the user launched circuitRF once is a surprise"*), answered without
defaulting the feature off.

**R-rc5-4b. The first time a workspace gains a repository, that is announced — once.** One Messages
entry: a history is now being kept for this workspace, where the setting is, and — per §5.7's closing
paragraph — that removing it later is deleting one plainly-named folder and **cannot harm the design**.
§1.4's rule is that a state must be **visible**, not that it must be absent, and this is the cheap half
of that rule. It is the mirror of RC-6's "held" report: circuitRF says once, plainly, what it is and is
not keeping. **This is the exception to R-rc5-11**, which is otherwise the rule that automatic
checkpoints post nothing — and it is an exception because it is not a checkpoint, it is the creation of
the thing checkpoints go into.

**R-rc5-4c. Stage 2's two user-facing surfaces need a named home, and rev 3 gave them none.** §5.3's
second boundary is *"an explicit save-point the user asks for"* and §5.1 says checkpoints are
*"presented in the UI as restore points"* — neither of which says where. Both are this brief's, both are
Stage 2, and neither can be deferred to RC-7, which is a different stage and a different history:

- **the restore-point list is a dockable tool panel**, which is the application's own idiom for a place
  things are listed and is where the Messages panel already lives;
- **the explicit save-point is a File-menu action**, beside the save commands, because §5.3 describes it
  as something the user asks for and that is where a user looking for it will be.

**Neither carries git vocabulary** (R-rc5-2, R-rc0-6). If the implementation finds a better home for
either, take it and **report the choice** — what must not happen is a boundary with a test and no
affordance.

**R-rc5-8. Every checkpoint carries a monotonic sequence circuitRF maintains itself, in its own
metadata.** The wall clock supplies the *label* a human reads; **it never decides what is oldest.**

This is not defensive programming for a hypothetical. A wall clock is user-writable state: a machine
whose clock jumps forward makes every checkpoint expired on the next sweep; a clock set backwards makes
nothing expire and stamps new checkpoints as older than the ones they follow. Timezone changes, a dead
CMOS battery, a laptop resuming against a re-synced time server, and a dual-boot machine disagreeing
about whether the hardware clock is UTC all produce the same fault, and all are ordinary.

**The sequence is written by this brief even though the retention that consumes it is RC-6's.** A
sequence retrofitted later cannot order the checkpoints taken before it existed.

---

## 4. Failure is reported; success is not

**R-rc5-9. Identity is a prerequisite, not an error to translate** (§4.4). `git commit` refuses
outright when `user.name` and `user.email` are unset, and on a fresh Windows machine they are unset —
so the first checkpoint circuitRF ever takes would fail, **silently**, since a checkpoint is not
user-initiated and has no dialog to fail into. RC-4 captures identity as a **per-user preference**
(R-rc4-11a) before the feature arms; RC-3 supplies it **per invocation**, in no config file
(R-rc3-1a); this brief refuses to arm when neither circuitRF's preference nor git's own resolution can
name a committer.

**The headless path resolves it differently and that is correct.** `src/Cli` cannot read
`AppPreferences` across the firewall, so a headless checkpoint identifies itself through git's ordinary
resolution — whoever is running it. **Do not invent a synthetic committer** for the automatic or
AI-initiated cases: a fabricated author would be indistinguishable from a real person of that name, and
it would break `git log --author` for the actual designer. R-rc5-5's origin is already carried in the
commit *message* (RC-7 R-rc7-5), which is where the distinction belongs.

**R-rc5-10. A checkpoint that could not be taken is always reported.** §1.4 forbids a designer
believing they are protected when they are not, and a checkpoint failing quietly is the purest form of
that failure. The Messages entry says **what was not saved and why**.

**R-rc5-11. Automatic checkpoints do NOT post a Messages entry each time.** They would drown the panel
and defeat §5.3's readability. They appear in the restore-point list, which is where someone looking
for one looks. **Failure is the exception**, per R-rc5-10 — and a *skipped* AI-batch checkpoint posts
**once per session**: enough to be unmissable, not so often that the panel becomes noise the designer
learns to scroll past.

---

## 5. Restoring

**R-rc5-12. Restoring shows the designer their own design at an earlier moment.** The list is times and
intents. Restoring is one action.

**R-rc5-12a. A restore takes a checkpoint of the current state FIRST, always, and this is not
optional** (§5.8). The state being replaced may never have been checkpointed — by R-rc5-4's design it
is whatever the designer has done since the last boundary, which can be a whole afternoon. **A restore
that discards it is an automatic operation destroying history**, which R-rc0-7 forbids. It also makes
the operation symmetric: a designer who restores to the wrong point can get back, which is the
difference between a safety net and a second cliff. R-rc5-6e's batch revert goes through this path and
inherits it.

**R-rc5-12b. Unsaved work is offered up first, and open documents are reloaded with their undo stacks
DISCARDED** (§5.8). Two halves, and the second is the one that is silent if missed:

- A restore is built from what is on disk. On top of dirty documents it produces a workspace matching
  neither state — so it asks, through the prompt that already guards a close, an archive and a
  workspace copy. Reuse it; do not write a fourth.
- **An undo after a restore would re-apply the last few minutes of the REPLACED state onto the
  RESTORED file**, producing a document that existed at no moment ever: well-formed, openable, and
  wrong. §1.3 names that as the failure this whole feature is written against, and it would be this
  feature causing it. R-rc5-3 says undo and checkpoints are unrelated — that governs how they are
  *implemented*, and it is not permission to leave a stack describing a file that no longer contains
  what the stack describes.

**R-rc5-13. Restore-then-keep-editing creates a branch silently, and the word never appears** (§6.3).
Git requires a branch there; the architecture's answer is that it is created silently and **named after
the design intent**, surfaced as a *variant* — *"You are editing from an earlier version; saving will
create a new variant."* The words branch, checkout and HEAD appear nowhere. **RC-7 owns the variant UI
in full**; this brief must not paint itself into a corner that makes it impossible — specifically, the
restore operation must not leave a state RC-7 cannot name.

**R-rc5-13a. There is no reference the designer sees, and the words never appear** — R-rc5-2 governs
the restore list too, including whatever R-rc5-12a's pre-restore checkpoint is called in it. It is a
restore point like any other; it is not "the state before you went back", which is a sentence with a
git shape.

**R-rc5-14. A restore restores this workspace's files and nothing else, and says so where it could
mislead.** Content in a referenced workspace is not in this repository (§7A.1), so *"restore my
workspace"* sounds more total than it is. RC-9's pins are what make a restore complete; until then the
list must not imply otherwise.

**R-rc5-20. A scratch workspace has no folder, therefore no repository, and the affordances are simply
not there** (§5.9). `scratch-and-save-lifecycle.md` specifies an in-memory workspace with no disk
presence that materialises on save; a repository is a directory inside a workspace folder. Three
consequences:

- The posture is RC-3 R-rc3-3's — **absent, not disabled** — for a better reason: there is nothing to
  record into.
- **The repository becomes possible at materialisation**, and RC-3 R-rc3-11 already writes the
  `.gitignore` from the function that creates the folder, so a materialised scratch workspace is not a
  special case.
- **An AI batch against scratch is R-rc5-6f's refusal**, and the remedy — save the workspace — is
  *offered* rather than named, using the save lifecycle's own path.

**Crash recovery is not this feature and must not become it.** Scratch has its own autosave and its own
recovery cache. A checkpoint is a boundary in a workspace's history, not a copy of unsaved bytes;
conflating them would hand the safety net a second, differently-shaped job and make both worse.

**R-rc5-21. On workspace close, the `.cws` is written BEFORE the checkpoint is taken**, or the
checkpoint records a workspace file one save out of date. This is ordering, it costs nothing, and it is
invisible when wrong — the restored workspace simply comes up with slightly stale configuration and
nobody connects it to the close.

**R-rc5-22. The close checkpoint must not make quitting feel broken, and this is the one boundary that
sits in front of the user.** RC-3 R-rc3-14 rightly keeps *packing* away from a save; the close
checkpoint is a `commit -a` over a workspace that may hold several multi-megabyte layouts, and it runs
at the moment the designer has asked to leave. **Measure it and report it** (R-rc0-8 — measured, not
asserted in a timing test). If it is slow enough to notice, the window may close ahead of it, but the
process must not exit until it has completed or failed, and a failure still reports (R-rc5-10).

---

## 6. The large-file guard (§8.2)

Per R-rc0-2 this lands here rather than in RC-3, because Stage 1 takes no commits and a dialog with no
call site is a dead feature.

An RF designer unfamiliar with git will eventually commit a very large file — an imported GDSII, a
board's Gerber set, a results cube — then delete it, and be surprised the repository stayed large. This
is predictable enough to design against directly, and prevention already shipped in RC-3's
`.gitignore`.

**R-rc5-15. Before a commit, an unexpectedly large newly-added file is named, with the choice offered
plainly — and the choices are about the file's ROLE, not about git mechanics:**

| choice | what it means | what it does |
|---|---|---|
| **Include it** | this is design input — the GDSII the layout was built from | tracked; one copy now and one delta whenever it changes, which §2 says is cheap |
| **Leave it out this time** | undecided; ask again | nothing is written; the file stays untracked |
| **Never include files like this** | this is a by-product, not a design | a pattern is added to `.gitignore`, applying to a file that is **not yet tracked** |

**R-rc5-16. The third choice carries its consequence at the point of choosing.** A file matching that
pattern is **not in the history**, so it is **not in a restore, and not in a clone.** §10B requires that
sentence to appear where the choice is made, not only in a manual.

**R-rc5-17. There is no fourth button, and "add it once, then ignore it" is not built.** The reason is
mechanical rather than a matter of taste: **`.gitignore` has no effect on a file that is already
tracked**, so the option resolves to one of two implementations and both are worse than either plain
choice.

- *Add the pattern only.* Nothing changes; the file goes on being committed exactly as under "Include
  it". **The user believes they turned something off; they did not.** Under §1.4 that is the worst
  outcome in the architecture — not for the disk cost, but because the UI told the designer something
  false about what is kept.
- *Add the pattern and untrack the file.* The file is now in history at one old version, **absent from
  the current state**, and present only in the working copy of whoever was at the machine. A clone gets
  the workspace with that file **missing** — and if a `.clay` references it, the reference dangles. A
  restore brings back the *stale* version. That is design-IP loss wearing the costume of a convenience.

**R-rc5-17a. The FIRST commit into a workspace that already exists is a different operation, and
R-rc5-15's dialog does not fit it** (§8.1a). That guard names one unexpectedly large newly-added file;
the first commit into a workspace with four years of accumulated output has hundreds of them, and **a
dialog naming hundreds of files is not a dialog.** So the first commit **summarises by category and by
size**, offers the same three choices **per pattern** rather than per file, and shows what RC-3's
generated `.gitignore` already excluded before asking about anything else.

What it must not do is proceed silently. A designer whose repository is three gigabytes on its first
day is the complaint §8 exists to prevent, arriving on day one instead of in month six. RC-3
R-rc3-11a guarantees the `.gitignore` is present before this runs, which is what keeps the summary
short enough to read.

**And the benefit does not exist:** §2 measured that a tracked file which does not change costs
**nothing per commit**. "Include it" already has the property the option was invented to buy. The only
case where they differ is a file that is large *and* changes often *and* whose current content matters
while its history does not — and the correct answer there is "Never include files like this", which is
one row above it in the same dialog.

---

## 7. The archive gate — mandatory, and it is this phase's

**R-rc5-18. The archive scanner is taught to exclude `.git`, in this brief, because this is the phase
that first puts one inside the folder the scanner walks.**

`WorkspaceArchiveScanner.IsSkipped` (`src/Ui/Archive/WorkspaceArchiveScanner.cs:53`) filters three
lists at `:36`, `:42` and `:47`, and **`.git` is in none of them.** The moment a checkpoint creates a
repository, an archive would carry the whole history — including **every earlier version of every
tracked file, and files no longer in the workspace at all.** A designer who imported a customer's
GDSII, finished with it, deleted it, and then archived the workspace for a different customer would
ship that GDSII, with nothing in the visible file tree showing it.

**That is disclosure, and unlike every loss in the architecture it cannot be undone by anyone at any
later time.** RC-8 adds the *choice* to include history; **the exclusion is not RC-8's to defer.** An
archive that carries history because nobody told the scanner is that failure arrived at through pure
inattention, and it would ship in the first build that takes a checkpoint.

**R-rc5-19. The same exclusion silently changes File ▸ Save Workspace As, and the copy must say so**
(§9A.6). `WorkspaceCopy.Run` (`src/Ui/Schematic/WorkspaceCopy.cs:130`) filters through that **same**
`IsSkipped`, so adding `.git` to it stops Save As copying the repository too.

**`.git` is the case where both consumers want the same answer — and RC-1's `.cwsuser` is the case where
they do not** (R-rc1-15a). Note the pair where the lists are declared: one shared skip list, two
consumers, and the correct resolution differs by entry. That is the trap, and it is only visible if both
entries are read together.

**That outcome is right and the silence is not.** Right, because §9A.1's reasoning is identical and the
irreversible direction is the same — a copy made for a different customer must not carry the first
customer's deleted files. Wrong, because *"I saved a copy and my history is gone"* is then a discovery
rather than a decision, and Save As is **the only one of the three journeys that gives the user no
dialog to read**.

So the copy adds **one sentence to the message it already posts** — the one reporting how many files
were copied and how many references were repointed: the copy starts a history of its own, and the
original keeps every restore point and every commit. True, short, and it turns a silent change into a
journey the designer knows they took. **A copy is not a fork of the history and is not presented as
one**; anyone wanting the history to travel has RC-8's archive.

---

## 8. Gates

1. **The reference is outside the branch**: after ten checkpoints, the branch the designer would browse
   has no new commits on it (R-rc5-1).
2. **No git vocabulary** (R-rc0-6): source-scan every user-visible string this brief adds for branch,
   checkout, HEAD, commit, detached, ref, stash — comments stripped before scanning.
3. **Each of the three boundaries takes exactly one checkpoint**, and a sequence of file saves between
   two boundaries takes none (R-rc5-4).
4. **Simulation runs and idle time take none** (R-rc5-5) — assert directly, because these are the two
   most likely to be re-added by someone who did not read the reasons.
5. **A checkpoint spans files the user did not open** (R-rc5-7): modify three files outside any open
   document and assert all three are captured.
6. **Headless.** The AI-batch checkpoint is takeable from `src/Cli` with no display, and the result is
   **byte-identical** to the one the GUI's own path produces for the same workspace — the standard
   `EmCliVerbTests` already holds for a verb. Where a legitimate difference exists (a timestamp), exempt
   it explicitly and name it.
7. **Ordering survives a clock change** (R-rc5-8): set the fixture clock backwards between two
   checkpoints and assert the sequence still orders them correctly. This is the gate RC-6's retention
   depends on.
8. **No identity means no arming** (R-rc5-9), with the failure named — assert the translated sentence,
   not a git message.
9. **Failure is reported, success is not** (R-rc5-10, R-rc5-11): a failing checkpoint posts once; ten
   successful ones post nothing; a skipped AI-batch checkpoint posts **once per session** regardless of
   how many are skipped.
10. **The large-file guard's three choices each do what they say** (R-rc5-15), and **there is no fourth
    path** — assert no code path adds a `.gitignore` pattern for an already-tracked file (R-rc5-17).
11. **`.git` is excluded from an archive** (R-rc5-18). Take a checkpoint, archive, extract, and assert
    no repository arrives. **This test must exist in this brief**, not in RC-8.
12. **And from a Save Workspace As copy** (R-rc5-19) — assert no repository arrives **and that the
    copy's report says so**. Assert the sentence, not merely the absence: the absence was already true
    the moment `.git` joined the skip list, and the sentence is the part that is this brief's.
13. **Cost, measured not asserted** (R-rc0-8): the wall-clock cost of a checkpoint on a large workspace,
    and separately **the close checkpoint's contribution to quitting** (R-rc5-22), reported in the
    write-up.
14. **One reference per checkpoint** (R-rc5-1): after ten checkpoints there are ten references, and
    deleting one leaves the other nine intact and restorable. **RC-6 cannot be built without this**, so
    it is gated here rather than there.
15. **A restore checkpoints first** (R-rc5-12a): restore with uncommitted work present, then assert the
    pre-restore state is itself restorable. This is the gate that keeps a restore from being a second
    cliff.
16. **Undo stacks do not survive a restore** (R-rc5-12b): open a document, edit it, restore, then
    assert an undo cannot re-apply the replaced edits onto the restored file.
17. **The close checkpoint sees the final `.cws`** (R-rc5-21): change something that lands in the
    workspace file, close, restore, and assert the change is present.
18. **Scratch takes no checkpoint and refuses a batch** (R-rc5-20), and the refusal offers the save.
18a. **Arming happens at a boundary, not at open** (R-rc5-4a): open a workspace with the preference on,
    change nothing, close **without** the close boundary firing — assert no repository was created. Then
    take a boundary and assert one was. **The share case is the one that matters**: assert nothing is
    written into a workspace folder merely because it was opened.
18b. **The announcement fires once** (R-rc5-4b): the first arming posts exactly one entry; the second
    session posts none. Assert the sink, not the text alone.
18c. **Both Stage 2 affordances exist** (R-rc5-4c): the restore-point list has a home and the explicit
    save-point has an invokable command — assert the command exists and reaches the boundary, since a
    boundary with a test and no affordance is the gap this requirement was written for.
19a. **A batch is refused while a document is dirty** (R-rc5-7a): open a document, modify it without
    saving, open a batch — assert the refusal, and assert **file mtimes are unchanged**, exactly as
    gate 19 does for off/held/scratch.
19b. **A batch close reloads and clears undo** (R-rc5-7b): with the workspace open and clean, run a batch
    that modifies an open document, close it, and assert the window shows the new content **and** that
    an undo cannot re-apply the pre-batch edits onto it. This is gate 16 seen from the batch side and it
    must use the same path.
19c. **No filesystem watcher was added** (R-rc5-7c): source-scan for `FileSystemWatcher` in the paths
    this brief touches and fail on a hit, comments stripped.
19. **The batch protocol** (R-rc5-6a…f): a batch opened twice is one batch and one checkpoint; a batch
    never closed still leaves a usable restore point; a batch opened against an off, held or scratch
    workspace is refused **with nothing modified** — assert file mtimes, not just the refusal.
20. **`serve` advertises the state and publishes the rules** (R-rc5-6g, R-rc5-6h): assert the state is
    reported for each of on / off / held / unavailable, and that the ten rules are present in the
    surface's own output rather than only in a document. A rule an agent cannot read is a rule that
    does not exist.
21. **The first commit into an existing workspace summarises rather than enumerating** (R-rc5-17a):
    build a fixture with a hundred result files and assert the prompt groups them.
22. **Git-absent machines skip with a reason** (R-rc0-10b), following `RfCore.Tests`'
    `FixtureFact`/`FixtureTheory` idiom. Never fail; never vendor a git.

---

## 9. Documentation (§10B.1, §10B.2 scenarios 1 and 6, §10B.3)

- **Scenario 1** — *"I broke the match network yesterday and I want it back."* The ordinary case, start
  to finish. This is the chapter's spine.
- **Scenario 6** — *"My results are gone."* Not a bug: why results are not versioned, and that the
  design that produced them is.
- **Scenario 12** — *"I saved a copy of my workspace and the history didn't come with it."* (§9A.6,
  R-rc5-19.) Short, and it exists because this is the one journey of the three with no dialog to read.
- **The §10B.1 row for a "never include files like this" choice**: not kept, **not recoverable, and
  absent from a clone too.**
- **Scenario 13** — *"circuitRF says it started keeping a history of this workspace."* (§5.7a,
  R-rc5-4b.) What was just created, where the setting is, and that removing it later cannot harm the
  design. **It exists because this is the first thing most users will ever see of this feature, and it
  arrives unasked** — which makes it the one piece of documentation whose absence a user will notice.
- **The §10B.1 row for a workspace that is not yet armed**: nothing is kept before the first boundary,
  and the workspace says so once when it is.
- **The §10B.1 rows for a Save As copy and for a scratch workspace**: nothing is kept in either, and
  the reason differs — a copy starts fresh, a scratch workspace has nowhere to keep anything yet.
- **§10B.3 — AI checkpoints get their OWN chapter, not interleaved.** Two reasons, and the second is
  the operative one: someone who never uses AI features should be able to read the history
  documentation without encountering them; and **they mean something different from a commit the
  designer made.** A restore point taken before an agent edit is a safety device the user did not ask
  for; a commit is a statement the user made. Presenting them together invites the belief that one
  substitutes for the other. The chapter states what triggers a checkpoint, that they are automatic,
  that they are subject to retention while explicit commits are not, and how to turn one into a
  permanent commit.

---

## 10. On completion

Findings to `src/Design/RESOLVED.md`, `src/Ui/RESOLVED.md` and `src/Cli/RESOLVED.md` — **never to a
`CLAUDE.md`**.

**Report, do not silently absorb:**
- The measured checkpoint cost, the repository growth over a realistic session (R-rc0-9), and **the
  close checkpoint's share of quitting** (R-rc5-22).
- Anything the AI-batch boundary needed that `src/Cli/Serve` could not supply — that is a gap in the
  agent surface and it is the most valuable thing this brief can find, because §12 Q5 says this floor
  must be in place before the capability arrives.
- Whether the restore path leaves any state RC-7's variant handling cannot name (R-rc5-13).
- **Whether the ten rules survived contact with a real agent** (R-rc5-6h). If one of them is ambiguous
  enough that a capable agent could obey it and still do the wrong thing, that is a defect in the rule,
  not in the agent — report the wording, and say which rule.
- **Any route by which an agent could take a checkpoint circuitRF did not take**, or report one that
  was not taken (rule 10). That is R-rc5-6i's failure and it is the one this brief most needs to hear
  about.
- **Whether R-rc5-7b genuinely reused R-rc5-12b's path** rather than growing a second reload. Two
  reload implementations that drift is the shape of defect §5.8 is written against.
- **Where the restore-point list and the save-point action ended up** (R-rc5-4c), and whether either
  home fought the dock or the menu.
- Whether reusing the existing save-before-close prompt for the restore worked unmodified
  (R-rc5-12b). A fourth prompt would be a finding.
