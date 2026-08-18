# Wave 1 — make the agent's view true

**Date:** 2026-08-18 · **Items:** A21, A8, A12
**Why first:** every wave after this verifies *through* these tools. A read surface that lies makes
every later result unfalsifiable.

## The theme

Three separate entries, one fault: **the launcher tells an agent something different from what it
tells a person, confidently.** Not less detail — a different answer. Each was found by asking a
question the UI never asks, and each currently returns a wrong answer rather than an error, which is
the failure mode this repo keeps rediscovering.

---

## A21 — `GameShape` counts files that are not on disk · `S`

**Observed.** `get_game_shape` on Monster Hunter Wilds returned, in one payload:

```jsonc
"declaredLocations": [{ "path": "mods", "exists": false }],
"contentRoots":      [{ "relativePath": "mods", "fileCount": 6, "insideDeclared": true }],
"alignment": "Aligned",
"notes": [ "Declared mod location 'mods' does not exist on disk",
           "Mods are where the registration says they are." ]
```

Six files inside a folder it also says is absent, and both notes at once. Verified on disk: the
folder is not there.

**Cause.** `GameShape` builds `ContentRoots` from the mod ROWS — each mod's declared location joined
to its file paths — and never asks the filesystem. All six mods were **disabled**, so their files sit
in the holding folder while the rows still name the location they would occupy. `Alignment` then
reads `Aligned` because every fabricated root is `InsideDeclared`, and the two notes are computed
from different inputs and never reconciled.

**Fix.** A content root is a claim about disk, so verify it against disk:

1. Drop a root whose directory does not exist.
2. Count only files that exist.
3. Let `Alignment` fall out of what survives.
4. Build the notes from one computed picture rather than two.

**The judgement call this needs.** A game whose mods are all disabled has no content on disk, but it
is not the same as a game with no mods at all — and `LocationAlignment.NoMods` currently means the
second. Options: reuse `NoMods` (simple, loses a distinction the caller may want), or add a state
meaning *"mods are registered and none is currently placed"*. **Recommend the new state**: an agent
asking "is this install healthy" gets a materially different answer from "you have no mods" versus
"your mods are all switched off", and conflating them is the same class of error as this entry.

**Tests.** Disabled-only game reports no phantom roots; a root whose directory vanished is dropped; a
mixed enabled/disabled game counts only what is placed; notes never contradict.

---

## A8 — `get_game_shape` does not expose the declared-vs-derived flag · `XS`

`GameShape.DeclaredLocation` gained a `Declared` flag (#266) so a launcher-appended location — the
synthetic `ue4ss-mods` entry `Scanner.GameContext` adds — is not presented as something the
registration declares. The MCP projection never followed, so for a derived entry `path` silently
changed meaning (a bare label became an absolute path) with nothing to explain it.

**Fix.** Add `declared = d.Declared` to the projection in `ModTools.GetGameShape`, and say in the
hint what a `false` means.

**Test.** The projection carries the flag; a derived location reports `declared: false`.

---

## A12 — the mod count disagrees with itself across three surfaces · `S`

Windrose: the library home says **30**, the MCP says **30**, the game view says **27 of 27**. The gap
is the Faster Ships family — four mod keys the game view groups into one variant row.

**Neither number is wrong on its own terms.** One counts mod keys, the other counts rows a human
sees. The fault is that only one of them is reported, so an agent says "you have 30 mods", the user
counts 27, and concludes the agent is broken.

**Fix.** Report both, and name the grouping. `get_game_shape` and `list_mods` should carry the row
count a human would count *and* the key count, with the difference explained rather than left as a
discrepancy for the reader to discover.

**Not in scope here.** Whether the home row should show 27 instead of 30 is a product call about
which number a person wants on a card, and it belongs with A11 (the variant-family cluster) rather
than with making the agent honest. This wave makes the disagreement *legible*; A11 decides which
number wins where.

**Tests.** A game with a variant family reports both counts and they differ by the expected amount; a
game with no families reports them equal.

---

## Sequence

A8 first — one line, and it makes the shape payload's shape final before A21 and A12 both add to it.
Then A21, since it changes what `ContentRoots` and `Alignment` mean. Then A12, which adds counts on
top of the corrected picture.

## Done when

- The three payload defects are gone and each has a test that would have caught it.
- `get_game_shape` on Wilds no longer claims `Aligned` for a folder that does not exist.
- Full suite green; `CorePurityTests` green.
- Verified live through the MCP against the real install, not only in tests — the tool being honest
  is the point, so the check has to run through the tool.
