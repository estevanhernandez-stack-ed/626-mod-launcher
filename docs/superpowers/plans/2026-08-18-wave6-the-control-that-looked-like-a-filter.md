# Wave 6 — the control that looked like a filter

**Date:** 2026-08-18 · **Items:** the round table's #1, plus the switcher's bulk-op finding
**Why first:** it is the only item on the approved order that costs a user rather than a feature.

## What is actually there

`MainViewModel.SetMode` → `Scanner.ApplyModeAsync` → `Scanner.ApplyMode`:

```csharp
foreach (var m in ListWithClass(c))
{
    if (m.ReadOnly) continue;
    var want = Classification.ModeFilter(mode, m.Class ?? "both");
    if (m.Enabled && !want) DisableEntry(m, c);
    else if (!m.Enabled && want) EnableMod(m.Name, c);
}
```

Three segmented buttons labelled **All / MP / SP**, under a heading that says `LOADOUT`, **enable and
disable every mod in the game**. And `SetMode` returns early — setting `ActiveMode` and nothing else —
for Mod Engine 2, direct-inject and loose-root games.

So the same control is a bulk file operation on one game and a cosmetic highlight on the next, with
identical styling, in a shape that means *change what I see* in every other application on the machine.

Two things in the app's favour, worth stating before changing it: the apply **does** pass the ban-risk
gate once before the bulk run, and it **does** skip `ReadOnly` rows, so it never touches a folder
another tool owns.

## The design decision

This needs a product call, not an implementation choice. Three options were considered.

**A — the segments become a genuine filter, and applying disappears.**
Safe and simple, and it throws away something valuable: *"I am about to play online, turn off the risky
mods"* is arguably the best safety feature the app has on a ban-risk title. Rejected.

**B — the segments keep applying, but stop looking like a filter.**
Rename, add a confirm, make the behaviour consistent across engines. Keeps the capability, but leaves
the good half — filtering the view by MP/SP, which is useful on every engine and costs nothing —
unbuilt, and it puts a confirm in front of a thing that used to be one click.

**C — both, each named honestly. ← chosen**
The segments filter the view. Applying becomes an explicit, named action that says what it will change
before it does it. Filtering then works on **every** engine, which fixes the inconsistency rather than
papering over it, and the dangerous act stops wearing a view control's clothes.

**Why C.** The two behaviours were never the same thing; they were one control because one control was
cheaper. Splitting them is what lets each be honest: a filter that is instant and universal and touches
nothing, and an apply that names its consequence. It also costs the power user nothing — the filter is
still one click, and the apply he uses deliberately is still one click plus a confirm he asked for.

**Este can overrule this.** If the apply is wanted back on the segments, the smaller version of this
wave is option B and the filter work is dropped.

## What gets built

### 1. The segments filter, and touch no files

`SetMode` sets `ActiveMode` and re-runs the row filter. Nothing else. This is safe by an invariant the
codebase already states, at `FilterRows`:

> *"Mods is the RENDER list; `_allRows` is the STATE list. Every write/safety/status path — load order,
> play-vanilla step-aside, launch guard, enable-all, MP warnings, loose-identify — reads `_allRows`. A
> typed filter must never narrow a file op."*

So adding MP/SP to `FilterRows` narrows the render list only, and the rule that keeps it safe is
already written down and already enforced everywhere else.

The classification rule is reused, not reimplemented: `Classification.ModeFilter(mode, cls)` is the
same pure function the apply used.

### 2. Applying becomes an explicit action

A named action — *"Apply this set…"* — that states what it will do in a confirm: how many mods turn on,
how many turn off, and on an engine where mode does not apply, that it will do nothing and why. The
ban-risk gate stays exactly where it is, ahead of the apply.

### 3. Bulk operations snapshot first

The switcher's separate finding, and it lands in the same wave because it is the same act:

> *"If I press `Disable all` on a 200-mod install, every file moves safely to the holding folder — and
> the knowledge of which 140 of those 200 were on is gone. `Enable all` does not undo it."*

The fix already exists and was never connected: `Scanner.SaveProfileAsync` saves exactly that set.
Before `Enable all`, `Disable all` and the mode apply, snapshot the current set under a generated name,
and **say so in the status line** — an undo nobody is told about is not an undo.

## Tests

The snapshot naming and the filter decision are pure and get real coverage: a generated name is stable,
readable and collision-free; the mode filter narrows the render list and never the state list; an
engine with no mode support filters normally and applies nothing. The App wiring is headless-untestable
and gets a smoke entry plus a harness assertion that the segments change the visible row count and
leave the enabled count alone — which is the whole fix, stated as an assertion.

## Done when

- Clicking All / MP / SP changes what is listed and moves no files, on every engine.
- Applying a mode is a separate, named act that says what it will change first.
- No bulk operation destroys the enabled set without saving it and saying where it went.
- Full suite green, `CorePurityTests` green, harness green.
- Verified on Windrose (27 mods, real MP/SP tags) that the segments filter and the enabled count holds.
