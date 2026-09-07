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
<li><a href="#assistants">Restore points and AI assistants</a></li>
</ol>
</nav>

## What a restore point is {#what}

A **restore point** is the whole workspace, as it stood at one moment: every cell, every schematic,
every layout, the workspace's own configuration. Going back to one puts those files back the way they
were.

It is not undo. **Undo** is per-editor and per-keystroke, it covers the last few minutes inside the
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

## Restore points and AI assistants {#assistants}

**Before an AI assistant changes anything in your workspace, circuitRF keeps a restore point**, labelled
with what the assistant said it was about to do — *"before: widen the output match"*. Going back to that
entry undoes everything the assistant did, in one action.

This is automatic, you do not have to ask for it, and it happens **before** the first change rather than
after the last: an assistant that stopped halfway still leaves the restore point exactly where it should
be.

It is also a floor the assistant cannot go around. If circuitRF cannot keep a restore point first —
history is off for this workspace, the workspace has not been saved anywhere yet, or you have unsaved
changes open — **the assistant is stopped and told, and nothing is changed.** Being stopped is the
feature working, not failing: it is not permitted to make its own backup arrangements instead, because
you would then be told you were protected by something you could not go back through.

An assistant's restore points are subject to the same tidying-up as the automatic ones. If one of them
matters, select it and press **Keep permanently**.
