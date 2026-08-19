# Wave 5 — what's actually there, and whether it worked

**Date:** 2026-08-18 · **Items:** A9, B5
**Why now:** the A's are otherwise closed, and B5 is the one entry today's walk argued for with
evidence rather than reasoning.

## The theme

Both entries are the launcher being wrong about disk, from opposite ends of the same act.

- **A9** — it places a SMAPI mod in the wrong SHAPE: a loose `.pak` flat in `Mods/`, when a SMAPI mod
  is always a folder with a `manifest.json` in it.
- **B5** — it never finds out whether what it placed actually ran. It launches the game and stops
  looking.

One is wrong about what it put there. The other never asks whether it worked.

---

## A9 — SMAPI intake places a stray `.pak` flat in `Mods/` · `S`

**Cause.** `smapi` has no `loose-root` form and is not direct-inject-backed, so it reaches
`Scanner.PlanIntake` with the empty→`["pak"]` substitution applied. A `.pak` inside a dropped archive
then classifies as a mod for a Stardew game and lands flat in `Mods/`. A SMAPI mod is a **folder
containing `manifest.json`**, never a loose pak.

**Not fixable by removing the substitution.** A7's verdict already settled that: an extension-less
registration is a pak game to `FileRe` and `ModKey`, so taking the substitution away from intake would
make intake refuse a mod the scanner's listing lane already shows. The two lanes have to keep agreeing.

**Fix.** Folder-shaped SMAPI intake: recognise the `manifest.json` marker inside the archive and place
the **containing folder**, the way the framework installers already do. The marker is the signal, not
the extension — which is what makes it robust to whatever else is in the zip.

**Tests.** An archive holding `MyMod/manifest.json` places the folder, not its files; a nested wrapper
(`MyMod-1.2.3/MyMod/manifest.json`) still places `MyMod`; an archive with several mod folders places
each; a pak-game archive is untouched by the new branch; and — the regression that matters — a SMAPI
archive with a stray `.pak` beside the folder does not place the pak flat.

---

## B5 — tell the user whether their mods actually loaded · `M`

**Observed, on this machine, today:**

| file | last written |
|---|---|
| Death Stranding 2 `ReShade.log` | 2026-08-18 15:29 |
| Death Stranding 2 `dlss-enabler.log` | 2026-08-18 15:29 |
| `launch-log.json` (626's own record) | 2026-08-18 15:29 |
| Windrose `UE4SS.log` | **2026-08-02** |

The first three agree: the game launched and both loaders wrote proof they ran. The fourth is the A13
signature still sitting there — a loader that has not run in over two weeks, on a game the launcher
happily reports as healthy.

**626 launches the game and stops looking.** It knows exactly what it enabled and never finds out
whether any of it ran. That is the whole A13 class of bug, and its only witness today is the user, at
the crash.

### Two signals, neither of which needs a person

1. **A loader log advancing past the launch.** 626 already stamps every launch in `launch-log.json`.
   Comparing that stamp to a loader log's mtime answers "did it run this time" with no new machinery.
2. **The loaded modules of the running game process.** If the proxy DLL from the game folder appears
   in the process's module list, the chain is live *in memory* rather than merely present on disk.
   This is the one that would have caught A13 at launch instead of at the crash.

### Shape

The **decision is pure and lives in Core**: given a launch time, the loaders we expect, and the
evidence (log paths with their mtimes, and the module names the process has loaded), say what ran,
what did not, and what we could not tell. Three answers, not two — "no log and no module list" is not
the same as "did not load", and reporting it as such would be a false alarm on the first launch after
a fresh install.

Gathering the evidence is App-side: `Process.Modules` for the running game, `File.GetLastWriteTimeUtc`
for the logs.

The sentence is the point: *"Windrose started. UE4SS did not load — the last time it ran was 2
August."* Nobody can tell the user that today.

**Deliberately not doing** anything about it automatically. This reports; it does not re-enable,
re-install or repair. A launcher that reacts to its own diagnosis is a launcher that surprises people
mid-session.

**Care.** Reading another process's modules is diagnostic, not invasive, and must never become a reason
to touch the game while it runs. And nothing here changes the ban-risk position: observing a launch is
free, and the acknowledgment to enable mods on a high-risk title stays human.

**Tests.** The pure decision, exhaustively: log advanced → ran; log stale → did not; module present →
ran even with no log; no evidence at all → unknown, never "did not load"; a loader with no known log
and no module → unknown. Plus the sentence each verdict produces.

**What stays human even after this.** Whether the mod does its thing in the world. "The zipline is
there" is a different question from "the plugin loaded", and only the second is answerable from
outside the game.

---

## Sequence

A9 first — it is smaller, self-contained, and finishes the A's. Then B5, decision before evidence
before surface.

## Done when

- A SMAPI archive places a folder, and a stray pak beside it is not placed flat.
- The launcher can say which loaders ran on the last launch, and says "unknown" where it does not know.
- Full suite green, `CorePurityTests` green.
- Verified on the rig the way the evidence was found: launch Windrose, and the report should name UE4SS
  as not having run if its log stays at 2 August.
