---
title: Versions
slug: reference/versions.html
doc-kind: Reference Guide
breadcrumb: Docs > Reference > Versions
lede: Recording a version of a workspace on purpose, under a title you write — what you come back to, what you send out, and what happens when two of you edit the same design.
keywords: version, commit, history, release, share, compare, conflict
---

<nav class="toc">
<h2>On this page</h2>
<ol>
<li><a href="#what">A version is something you decide</a></li>
<li><a href="#keeping">"I want to keep this one — it's what I'm sending out"</a></li>
<li><a href="#panel">The Versions panel</a></li>
<li><a href="#comparing">What changed between two versions</a></li>
<li><a href="#restore-then">"I went back to an old version, then kept working"</a></li>
<li><a href="#gaps">Gaps in the list</a></li>
<li><a href="#other-copy">Exchanging versions with another copy</a></li>
<li><a href="#two-of-you">"Two of us are editing the same workspace"</a></li>
<li><a href="#rewriting">"Something went in that shouldn't have"</a></li>
</ol>
</nav>

## A version is something you decide {#what}

circuitRF keeps two kinds of history, and they are deliberately not the same list.

| | [Restore points](restore-points.html) | Versions |
|---|---|---|
| who makes them | circuitRF, mostly automatically | you, on purpose |
| how many | dozens, thinned as they age | as few as you decide |
| what they are for | getting back from something that went wrong | saying "this one is finished" |
| do they travel? | no — they belong to this machine | yes — they go with a copy or a clone |

A restore point is a safety net. It is taken when you close the workspace, when you ask for one, and
before an assistant changes anything, and nobody else ever sees it. **A version is a statement.** It has
a title you wrote, it is what you come back to six weeks later, and it is what goes with the workspace
when it leaves your machine.

They are kept in separate panels for the same reason. A list with three hundred automatic entries in it
and four deliberate ones mixed among them is a list nobody reads — and then the safety net stops working
too, because nobody looks at it either.

## "I want to keep this one — it's what I'm sending out" {#keeping}

**File ▸ Keep This Version…**, or the button at the top of the Versions panel.

One field: a title. Write what the design *is* at this moment, not what you did to it —
*"Output match retuned for 3.5 GHz"*, not *"changed some stuff"*. Six weeks from now that line is the
only thing that tells one version from another, and it is what anyone you send the workspace to will
read first.

What is recorded is **the whole workspace as it stands**: every cell, every schematic, every layout, the
technology, the workspace's own configuration. Not just the document you have open.

<div class="callout note">
<span class="label">Nothing changed means nothing is kept</span>
<p>If the workspace is exactly as it was when you kept the last version, circuitRF says so and keeps
nothing. Two entries holding identical content would be two entries you could not tell apart.</p>
</div>

Afterwards, one line appears in the Messages panel saying what was kept **and giving the version's
identity** — a short string of letters and digits. You will almost certainly never need it. It is there
for the day you do: it is what to give someone helping you look at the workspace with tools other than
circuitRF.

Simulation results are not kept in a version, for the same reason they are not kept in a restore point:
a result is a function of the design and the engine, so the design is the thing worth recording. See
[what is kept, and what is not](restore-points.html#kept) — the same table applies.

## The Versions panel {#panel}

**View ▸ Panels ▸ Versions.** A list, newest first: when, the title you wrote, and who kept it.

Two actions:

- **Keep this version** — the same thing File ▸ Keep This Version… does.
- **Go back to this** — puts the whole workspace back to that version. It works exactly like
  [going back to a restore point](restore-points.html#going-back), and everything on that page applies:
  unsaved work is asked about first, **the state you are in now is kept first so you can come forward
  again**, files created since are taken away, your results are left alone, and every open document's
  undo history is cleared.

**If circuitRF is not recording** — history is off for this workspace, or the workspace sits inside
another version-controlled folder — the buttons stay where they are and say why rather than
disappearing.

## What changed between two versions {#comparing}

Select a version and the panel lists **the documents that differ** between it and the one before it:
which cells changed, which were added, which were removed.

That is deliberately where it stops. circuitRF does not show you a line-by-line comparison of what is
inside a schematic or a layout — open both and look, or go back to one of them. A list of changed
coordinates is not a picture of what moved.

## "I went back to an old version, then kept working" {#restore-then}

This is one line of work, not two, and there is nothing to manage.

1. You go back to Tuesday's version. The state you were in a moment ago is kept first, so nothing is
   lost — it is in the [restore points](restore-points.html) list like any other entry, and you can go
   forward again if you picked the wrong moment.
2. You keep working. The files on disk are Tuesday's, plus whatever you do next.
3. You keep a version. **That version records that it was brought back from Tuesday's**, naming it and
   the date it was kept.

That last part is the one worth knowing about. Without it, the list would show two versions in a row
where the second undoes most of the first, and there would be nothing anywhere saying why — it would
read as a change of mind. The line says what actually happened, and it appears **only** on the version
that followed a going-back. The next one after that is your own work again and carries no such line.

<div class="callout note">
<span class="label">Going back is still not a one-way door, even after you keep a version</span>
<p>The restore point taken just before you went back survives everything you do afterwards, including
keeping a version on top of the mistake. If you went back to the wrong moment and only noticed later,
that entry is still in the Restore Points panel.</p>
</div>

## Gaps in the list {#gaps}

If history was [switched off](restore-points.html#off) for a stretch, the Versions list shows that
stretch **as a gap**, with its dates and the reason — *"Recording was off from 3 Mar to 17 Mar. Anything
done in between is not in this history."*

It is not shown as a quiet fortnight in which you happened not to keep anything. Those are completely
different situations and only one of them means the work is recoverable.

## Exchanging versions with another copy {#other-copy}

If this workspace was brought here with
[File ▸ Copy Workspace Here…](workspace.html#copying-a-library), two more items on the File menu apply
to it.

- **Bring In Changes** shows what is new on the copy it came from. **Nothing in your own files is
  touched** — what arrives appears in the Versions panel, and if a document has changed on both sides
  you are asked which version you want, exactly as [below](#two-of-you).
- **Send Changes** sends the versions *you* have kept back to that copy. Your restore points stay here:
  they are your machine's safety net and mean nothing on anybody else's.

**Neither happens by itself.** circuitRF never contacts anything without being asked, and it holds no
sign-in of its own — it uses whatever your machine's `git` is already set up with. If a sign-in is
needed that cannot be supplied, the operation stops and says what was wanted rather than waiting.

If the other side has moved on since you last brought its changes in, **Send Changes** says so and
sends nothing. Bring the changes in first, choose where the two disagree, and send again.

## "Two of us are editing the same workspace" {#two-of-you}

If two people change the same document and the changes come together, circuitRF shows you **both
versions and asks which one you want**. Who wrote each, when, and what else changed alongside it. You
pick one, and that document becomes that version **whole** — exactly the bytes that person wrote.

**circuitRF will not combine them, and that is a statement about geometry and connectivity rather than
about effort.** Interleaving two sets of edits to a polygon's outline can produce a shape that is
invalid, or valid and *wrong*, in a file that opens without a word of complaint. The same is true of a
schematic's connections: two people's wires merged line by line can produce a netlist that elaborates,
simulates, and is not the circuit either of them drew. **A design that is silently wrong is worse than
being asked a question**, because at least the question is visible.

This is also why the big centralised design-management systems in this industry lock a cellview while
someone has it open rather than merging afterwards. When merging is not possible, keeping two people out
of one file is the only strategy left. circuitRF does not run a lock server — but it is why
[referenced workspaces are read-only by default](workspace.html), which is the cheapest version of the
same idea.

<div class="callout warning">
<span class="label">Choose, do not reconcile</span>
<p>If both versions contain work you need, the answer is not to hand-merge the file. Keep one, open the
other version's copy of the document beside it, and redo the changes you want in the editor — where the
tool can tell you whether the result is a valid design.</p>
</div>

## "Something went in that shouldn't have" {#rewriting}

A version you kept is a record, and **circuitRF will not alter one after the fact.** That is the whole
value of it: a version that could be quietly edited later is not something you can point at and say
"this is what I sent".

So if something is in the history that must not be — a very large file you regret including, or
something that should never have left your machine — circuitRF has no button for it, deliberately, and
this is the honest answer rather than a dead end:

**It is possible, and it is yours to do, with `git` on the command line.** The history circuitRF keeps
is an ordinary git repository sitting in a folder called `.git` beside your cells; every standard tool
reads it, including whatever your IT department already supports. Rewriting a history to remove
something is a well-documented operation and there are good guides to it.

**Why circuitRF will not do it for you:** rewriting replaces every entry in the history, so every copy
anyone else has taken of this workspace stops matching and cannot be brought back into line. That is a
consequence nobody should trigger from a menu item, and it is worth doing when it is worth that.

If it is only about disk space, it probably is not what you want —
[Compact and Reclaim Space](restore-points.html#disk) do that without breaking anything.
