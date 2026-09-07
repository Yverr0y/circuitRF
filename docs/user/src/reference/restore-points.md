---
title: Restore Points
slug: reference/restore-points.html
doc-kind: Reference Guide
breadcrumb: Docs > Reference > Restore Points
lede: circuitRF quietly keeps the state of a workspace at a few moments that matter, so you can put it back the way it was. What is kept, what is not, and how to go back.
keywords: history, restore, undo, revert, safety net, save point, version
---

<nav class="toc">
<h2>On this page</h2>
<ol>
<li><a href="#what">What a restore point is</a></li>
<li><a href="#when">When one is kept</a></li>
<li><a href="#panel">The Restore Points panel</a></li>
<li><a href="#going-back">Going back — "I broke the match network yesterday"</a></li>
<li><a href="#results">"My results are gone"</a></li>
<li><a href="#started">"circuitRF says it started keeping a history"</a></li>
<li><a href="#copy">"I saved a copy and my history didn't come with it"</a></li>
<li><a href="#kept">What is kept, and what is not</a></li>
<li><a href="#tidying">Tidying up — "I can't get back to last month"</a></li>
<li><a href="#enclosing">"My workspace is inside another version-controlled folder"</a></li>
<li><a href="#already">"This workspace already had version control and circuitRF asked me something"</a></li>
<li><a href="#off">Turning it off, and getting rid of it entirely</a></li>
<li><a href="#disk">"My disk is full and it says the history is taking the space"</a></li>
<li><a href="#assistants">Restore points and AI assistants</a></li>
</ol>
</nav>

## What a restore point is {#what}

A **restore point** is the whole workspace, as it stood at one moment: every cell, every schematic,
every layout, the workspace's own configuration. Going back to one puts those files back the way they
were.

It is also not a [version](versions.html). A version is something you decide on and give a title to;
it travels with a copy of the workspace and it is what you send out. A restore point is a safety net
that never leaves this machine. The two are kept in separate panels on purpose — a list with three
hundred automatic entries and four deliberate ones mixed among them is a list nobody reads.

It is not undo either. **Undo** is per-editor and per-keystroke, it covers the last few minutes inside the
document you are looking at, and it ends when you close the window. A restore point is
workspace-wide, action-grained and durable — it is still there next week. The two are deliberately
unrelated, and between them they cover the whole range: undo for the last few minutes in one view,
restore points for everything else.

<div class="callout note">
<span class="label">Going back is never a one-way door</span>
<p>Before it replaces anything, circuitRF keeps a restore point of the state you are in right now —
including work you have done since the last one. So if you go back and it was the wrong moment, you
can come forward again. That entry appears in the list like any other.</p>
</div>

## When one is kept {#when}

Three moments, and no others:

| moment | why this one |
|---|---|
| **you ask for one** — File ▸ Keep This State… | your judgement about what matters beats any rule circuitRF could invent |
| **the workspace closes** | the one moment that reliably arrives in every session, including the ones where you never thought about history |
| **before an assistant changes anything** | so anything an AI assistant did can be undone in one action |

**Not on every save.** One thing you do — widening a match network — writes a layout, a schematic and
the workspace file, so a restore point per file save would keep fragments of an edit, some of them
half-finished. And a list with three hundred entries in it is a list nobody reads, which would make it
useless exactly when you needed it.

**Not on a simulation run, and not on a timer.** Runs are frequent and usually change nothing; a sweep
would leave dozens of near-identical entries. And an entry made because you went to lunch is one you
cannot predict, at a moment that meant nothing, labelled with a time instead of a reason.

**A moment where nothing changed keeps nothing.** Close a workspace you only looked at, or press Keep
This State twice, and there is one entry, not two.

<div class="callout note">
<span class="label">Nothing is created until something is worth keeping</span>
<p>Opening a workspace to look at it writes nothing at all — which matters most for a workspace on a
shared drive that belongs to somebody else. circuitRF starts keeping a history at the first moment that
would actually record something, and tells you once when it does.</p>
</div>

## The Restore Points panel {#panel}

**View ▸ Panels ▸ Restore Points.** A list, newest first: a time, what the state was, and how it came
to be kept.

The last of those is the part worth reading. **"you asked"** means you decided this state was worth
keeping. **"on closing"** means circuitRF kept it because you shut the workspace. The automatic one is
not the lesser of the two — it is frequently the one that saves you — but it means something
different, and the list says which is which so you never have to guess.

Three actions:

- **Keep this state** — the same thing File ▸ Keep This State… does. It asks for one optional line
  saying what this state is. Write one: six weeks from now that line is the only thing that tells one
  entry from another.
- **Go back to this** — see below.
- **Keep permanently** — marks an entry so that circuitRF's own tidying-up will never remove it. Ones
  you asked for are already marked.
- **Bring this back** — appears only on an entry marked *tidied away*, and puts it back in the list as
  an ordinary one. See [Tidying up](#tidying).

**If circuitRF is not recording**, the buttons stay where they are and say why rather than
disappearing, and a strip at the foot of the window says so for as long as the workspace is open. A
button that quietly vanished would look exactly like a feature that had never been built.

## Going back — "I broke the match network yesterday" {#going-back}

Open **View ▸ Panels ▸ Restore Points**, find the entry — the times and the lines are what you are
looking for — select it, and press **Go back to this**.

What happens, in order:

1. If anything is unsaved, circuitRF asks about it, in the same words it asks when you close a
   workspace. A restore is built from what is on disk, so unsaved edits sitting on top of it would
   leave you with a workspace matching neither state.
2. The state you are in now is kept, so you can come forward again.
3. The files go back. Anything created since is taken away, because yesterday's files plus today's new
   cell is a workspace that never existed.
4. Open documents are re-opened from the files that are now there.

<div class="callout warning">
<span class="label">Undo does not survive going back</span>
<p>Every open document's undo history is cleared, deliberately. An undo after going back would re-apply
the last few minutes of the state you <i>replaced</i> onto the file you just brought <i>back</i> —
producing a document that never existed at any moment: perfectly well-formed, and wrong.</p>
</div>

Going back covers **this workspace's files**. Anything in a workspace this one refers to is not part of
this workspace's history and is left exactly as it is.

## "My results are gone" {#results}

They were never kept, and that is deliberate — go back to a restore point and your `.npy`, `.spl`,
`.lpcwave` and `.mat` files are still sitting exactly where they were.

**A result is a function of the design and the engine**, so the design is the thing worth keeping: run
the analysis again and you have the results back. It is not about disk space. It is also why going back
does *not* delete them: bringing back last Tuesday's design should not destroy the hours of simulation
sitting beside it.

## "circuitRF says it started keeping a history" {#started}

The first time a workspace reaches one of the three moments above, circuitRF creates a small folder
inside the workspace to keep restore points in, and says so once in the Messages panel.

- **Where the setting is** — Settings ▸ Revision Control. You can turn it off entirely, or off for one
  workspace.
- **It cannot harm your design.** The folder is called `.git`; it sits beside your cells and holds only
  copies of them. Deleting it removes every restore point and touches nothing else.
- **Nothing was created before that moment.** If you only opened the workspace and looked, nothing was
  written at all.

## "I saved a copy and my history didn't come with it" {#copy}

**File ▸ Save Workspace As** makes a copy of your design and **the copy starts a history of its own.**
The original keeps every restore point it had.

That is on purpose. A history holds *every earlier version of every file it ever kept*, including files
you deleted long ago — so a copy made for one customer must not carry another customer's deleted
artwork inside it, invisibly. Save Workspace As says so on the message it posts when the copy is made.

If you want the history to travel, use **File ▸ Archive Workspace** and include history.

## What is kept, and what is not {#kept}

| | in a restore point? |
|---|---|
| cells, schematics, symbols, layouts, technologies | **yes** |
| the workspace file — analyses, references, configuration | **yes** |
| simulation results (`.npy`, `.spl`, `.lpcwave`, `.mat`) | no — re-run the analysis |
| your own panel layout, open tabs and colour theme | no — they are yours, not the design's |
| generated PCell artwork | no — it is rebuilt from the layout |
| a folder inside the workspace that keeps a history of its own | no — it is left entirely alone |
| a file you answered **"never include files like this"** about | no — **and it is not in a restore, and not in a copy anyone takes** |
| a file left out because nobody was there to be asked | **not yet** — the entry says so and names it, and circuitRF asks the next time you keep a state yourself |
| anything saved while history is switched **off** for the workspace | no — **but everything recorded before you switched it off is still there** |
| anything at all, when your workspace sits inside another version-controlled folder | no — circuitRF records nothing there, and says so |
| a restore point circuitRF **tidied away** | **yes, still** — it stays in the list marked *tidied away*, and you can bring it back, until you reclaim the space (which asks first) |

### Unusually large files

The first time something much larger than a design document would go into the history — an imported
artwork file, a whole fabrication set — circuitRF asks what it is:

- **Include it** — this is design input. It is kept: one copy now, and one more only when it changes.
- **Leave it out this time** — nothing is written, and you will be asked again.
- **Never include files like this** — it is a by-product. Files matching it are not kept, so they are
  **not in a restore and not in a copy**. They stay on disk exactly as they are.

There is deliberately no "keep it once, then ignore it": it cannot be made to work honestly. Either
nothing would change while the button said it had, or your history would hold one stale version of a
file that is missing from every copy anyone else takes.

**At a moment nobody is at** — a workspace closing, or an assistant working — circuitRF does not stop
to ask. It leaves the file out, records that it did, shows the entry as incomplete with the file's
name, and asks you the next time you keep a state yourself. Leaving a file out can be undone; putting
one in cannot.

## Tidying up — "I can't get back to last month" {#tidying}

circuitRF thins the automatic restore points as they get old, so a workspace you have had open every
day for two years does not carry two years of entries. **Two rules decide what goes**, and the first
one is the one worth knowing:

- **The newest ones are always kept, however old they are.** Settings ▸ Revision Control ▸ *Always keep
  at least* sets how many. This is not a nicety: a computer's clock is something anyone can change, and
  a machine that wakes up believing it is 2140 would otherwise think every restore point had expired.
  The count floor means a wrong clock costs you nothing at all, which is also why the field will not go
  below its minimum.
- **After that, age.** Settings ▸ Revision Control ▸ *Keep them for* sets how long.

**Ones you asked for are never thinned.** Nor is anything you marked **Keep permanently**, nor the
entries that mark where you switched history off and on again.

<div class="callout note">
<span class="label">Thinned is not deleted, and the list still shows it</span>
<p>A restore point circuitRF tidies away stays in the list, marked <b>tidied away</b> and shown a little
dimmer. Select it and press <b>Bring this back</b> and it is an ordinary entry again. Thinning stops
circuitRF <i>offering</i> a state; it does not destroy it, and nothing destroys it until you ask —
see <a href="#disk">"my disk is full"</a> below.</p>
</div>

**So turning the retention setting down is not the door it looks like.** If you set it to seven days
and then want last month back, the entries are still there and still marked; bring one back and go to
it. The only thing that ever actually removes them is **Reclaim Space**, which asks first and says
exactly what it will destroy.

**If circuitRF ever tells you it was about to tidy away far more than usual and did not**, that is the
safety catch working: it means the dates it is reading do not make sense — almost always a clock that
has jumped. **Nothing was removed.** Check the machine's date and time, and the next tidy-up will be
ordinary.

## "My workspace is inside another version-controlled folder" {#enclosing}

If your workspace folder sits *inside* a folder that already has a version history of its own — a
project repository, a shared checkout — **circuitRF stops. It keeps no history for that workspace**, it
says so once when you open it, and a strip at the foot of the window says **History held** for as long
as the workspace is open.

**Why it stops rather than joining in.** A restore point captures *everything* in the workspace,
including files you never opened — that is the whole point of it. Doing that inside a folder somebody
is also using for something else would sweep up work in progress that was nothing to do with circuitRF,
under a message circuitRF wrote. And circuitRF will not add its own settings files to a folder that
belongs to someone else's project.

**What is therefore not being kept:** nothing at all. No restore points are taken, none of the three
moments records anything, and an AI assistant is stopped rather than allowed to edit with no floor
under it.

**How to fix it:** move or copy the workspace to a folder that is *not* inside the other one. As soon
as it is on its own, circuitRF starts keeping restore points at the next moment worth keeping, and says
so.

<div class="callout note">
<span class="label">A folder inside your workspace is a different thing</span>
<p>A library somebody cloned <i>into</i> your workspace keeps its own history and is left entirely
alone — circuitRF excludes it, tells you once which folder it skipped, and everything else in the
workspace is kept normally. That is not the held state.</p>
</div>

## "This workspace already had version control and circuitRF asked me something" {#already}

If the workspace folder *itself* already has a version history — you set one up, or you were given one
— circuitRF does not take it over. It asks, once, and remembers your answer.

| your answer | what happens |
|---|---|
| **Use circuitRF's settings** *(recommended)* | circuitRF keeps restore points here and applies its own settings for how the history is looked after. Everything already in the history stays exactly as it is. |
| **Keep my settings** | circuitRF keeps restore points here under your existing settings. See below for what that costs. |
| **Don't keep a history here** | circuitRF records nothing for this workspace, and the window says **History held**. You can change your mind in Settings ▸ Revision Control. |

**What "keep my settings" actually costs.** Two things, and both are about *getting work back*, not
about tidiness:

- **A restore point circuitRF tidies away is destroyed permanently after a fortnight**, instead of
  staying recoverable for as long as you leave it. The whole reason the recovery window is measured in
  weeks is that a design that is wrong but well-formed often is not noticed for weeks — so a fortnight
  is inside the window this feature exists to cover.
- **The way back from a mistaken reset or rewrite of your own work disappears after thirty days.**

Everything already in your history survives whichever answer you give. **And one thing is circuitRF's
either way:** it records under the name in Settings ▸ Revision Control, and it never runs the scripts
your own tools attach to a change. A restore point that fired somebody's build hook — or that a hook
could refuse — would not be a safety net.

## Turning it off, and getting rid of it entirely {#off}

These are two different things and it matters which one you mean.

| | what circuitRF does | what happens to the history you already have |
|---|---|---|
| **Off** | writes nothing at all | **kept, listed, and still restorable** |
| **Removed** | not something circuitRF does — you delete one folder | gone, permanently |

**Off — Settings ▸ Revision Control ▸ *Keep a history of this workspace*.** Untick it and circuitRF
stops writing. **It deletes nothing.** Every restore point you already have stays in the list and you
can still go back to any of them; tick it again and it carries on where it left off. There is no
checkbox anywhere in circuitRF that destroys a history, and there is no "delete all history" command,
deliberately.

The setting belongs to the workspace, not to you, so:

- **The stretch while it was off is shown as a gap**, with an entry at each end saying that recording
  stopped and started. It is not shown as a quiet week in which you happened not to save anything.
- **It travels.** A copy or an archive of a workspace that was switched off arrives switched off,
  whatever the person opening it prefers — and they are told, with the setting one click away.
- **An AI assistant is stopped**, told plainly that no restore point can be kept, and offered the
  switch — before anything is changed. See [below](#assistants).

**Removed — delete the `.git` folder.** That is the whole of it: one folder, plainly named, sitting
beside your cells, using your file manager. It removes every restore point and touches nothing else.

<div class="callout note">
<span class="label">Removing the history cannot harm your design</span>
<p>Your design was never <i>inside</i> the history in any meaningful sense. A workspace is ordinary
files in a folder; the <code>.git</code> folder sits beside them and holds copies. Delete it and the
workspace opens exactly as it did, with every file present and current. Nothing you can do to the
history can damage the design.</p>
</div>

## "My disk is full and it says the history is taking the space" {#disk}

Two settings answer this, and they do completely different things.

**Compact — Settings ▸ Revision Control ▸ *Compact Now*.** Stores the same history in less space.
Nothing is discarded, nothing becomes unavailable, and it is safe to press at any time. Try this first;
on a workspace with large layouts in it, it very often is the whole answer.

**Reclaim Space — the same tab, below it.** This is the one destructive control in the feature, and it
exists because thinning frees a restore point's *listing* and not its *contents*: a large file you once
included and later regretted goes on taking space for as long as you leave it. Reclaiming frees that
space for good.

It destroys **only states that were already tidied away**, and only ones tidied away longer ago than
the age you set. It asks first, tells you how many, and says in plain words that after it nobody can
bring them back. **Every restore point still listed, and every change you recorded yourself, survives
it untouched, and your design files are never touched at all.**

**Nothing reclaims on a schedule.** circuitRF never destroys a state on its own; the age field on its
own does nothing until you press the button.

## Restore points and AI assistants {#assistants}

**Before an AI assistant changes anything in your workspace, circuitRF keeps a restore point**, labelled
with what the assistant said it was about to do — *"before: widen the output match"*. Going back to that
entry undoes everything the assistant did, in one action.

This is automatic, you do not have to ask for it, and it happens **before** the first change rather than
after the last: an assistant that stopped halfway still leaves the restore point exactly where it should
be.

It is also a floor the assistant cannot go around. If circuitRF cannot keep a restore point first —
history is off for this workspace, the workspace sits inside another version-controlled folder, the
workspace has not been saved anywhere yet, or you have unsaved changes open — **the assistant is
stopped and told, and nothing is changed.**

**Switching history off switches this off too**, which is the one case where two settings pull against
each other. That is a perfectly reasonable thing to choose and a bad thing to discover by accident, so
circuitRF says it out loud at the moment it matters: an AI edit asked for while history is off is
refused **before anything is modified**, with a button offering to turn history back on. Being stopped is the
feature working, not failing: it is not permitted to make its own backup arrangements instead, because
you would then be told you were protected by something you could not go back through.

An assistant's restore points are subject to the same tidying-up as the automatic ones. If one of them
matters, select it and press **Keep permanently**.
