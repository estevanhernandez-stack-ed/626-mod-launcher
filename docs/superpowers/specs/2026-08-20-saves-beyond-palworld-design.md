# Saves beyond Palworld

**Date:** 2026-08-20 · **Follows:** `2026-08-19-the-world-name-is-readable-design.md`
**Status:** design. Evidence gathered; one decision outstanding (§1).

## Why this exists

Palworld's saves panel can now name, duplicate, back up and restore a single world. The obvious next
question was *"which other games, and should we do it per engine?"* Four parallel investigations
answered that, and the answer to the second half is no. This records what they found, including three
places they overturned what I had already told Este.

**The one-line summary: engine predicts nothing, layout must be declared, name-rewriting must be
earned per game, and the thing that actually generalizes is cloud awareness — which we have none of.**

---

## 1. The thing to decide first: there is a live credential in our backups

`SaveManager.Backup` zips the whole save folder. Cyberpunk 2077's save root contains `user.gls`,
which holds a CD Projekt Red account **refresh token**:

```
issuer   accounts.cdprojektred.com/realms/cdp
scopes   openid aud_game_library aud_game_saves profile role_player …
expires  2035-01-18
```

Verified on the real install. So "Back up now" on Cyberpunk today writes a nine-year bearer token into
a zip under `_626mods/<game>/saves/` — a folder people copy between machines and hand to others for
support. A scan of every registered game's save folder found this in one game, but it is a **class**
of problem: any game with account integration can drop a token beside its saves.

**The tension is real.** Excluding a file means a snapshot is no longer a byte-faithful copy, and
faithful copies are the point. The argument for excluding anyway: an auth token is not save state. It
is an artifact the game re-mints on next sign-in, so skipping it costs the user nothing they cannot
get back, while including it costs them something they cannot take back.

**Proposed:** scan files under ~1 MB for a JWT signature on the way into a snapshot, skip them, and
record the exclusion in the snapshot so the artifact is honest about what it is. Signature-based, not
a filename denylist — `user.gls` is the one we found, not the one we should hard-code.

**Not shipped pending Este's call**, because it relaxes a stated law.

---

## 2. What the research overturned

Three claims I had already made to Este were wrong. Recording them because the corrections are more
useful than the originals.

**"Engine is the wrong axis" — right, but for a better reason than I gave.** I argued it from four
`ue-pak` games with four save shapes. The stronger predictor turns out to be **what characters the
game lets you type**. If a game restricts the name — Factorio's filename rules, Astroneer's 30-char
limit — the name *is* the filename. If it accepts free text containing characters illegal in Windows
filenames, it *must* live inside the save. Sons Of The Forest proves it in one line: its save is
called `No rmal 81 Days 18:16:54`, with a colon, so the on-disk marker is a lossy sanitised copy and
the truth is in the JSON.

**Second predictor: storefront beats engine.** Astroneer is a filename rename on Steam and a UTF-16
string inside a `wgs` container blob on the Microsoft Store. Same game, same engine, different tier.
**Every entry we curate is scoped to a storefront**, and the Store build is a separate question.

*(This one bites us already: the manifest's shipped `saveDirHint` for Palworld points at the Microsoft
Store `wgs` path, not the Steam path the app actually uses. See §5.)*

**"Terraria is Tier 0, a filesystem rename" — badly wrong, and it was my example.** The name lives
inside the `.wld` **twice** — header section and footer — and `LoadFooter` throws if they disagree, at
which point Terraria offers to restore `.wld.bak` and silently reverts the rename. There is an
absolute section-pointer table the loader hard-asserts against, so any length change shifts every
pointer. And each character's explored-map sidecar stores the world name and validates it; on mismatch
the catch block calls `Clear()`, so **renaming a Terraria world wipes every character's explored map**,
silently.

**"Palworld was the hard case" — also wrong, and this is the useful correction.** Across 18 games
surveyed, Palworld is the **only** one where the name sits inside a *compressed* payload and therefore
carries a byte budget. Everything else with a binary name has it outside compression, where a longer
name is fine and the work is mechanical. Palworld is not the general case. It is the outlier, and its
scar tissue must not be inherited by games that do not share the problem.

---

## 3. The model, corrected

The tier that matters is not *where the name lives*. It is **how many other things validate it**.

| | what else knows the name | cost of a rename |
|---|---|---|
| **0** | nothing; the filename is the display | `File.Move` — RimWorld, Project Zomboid, 7 Days to Die, Factorio |
| **1** | one plaintext field | a text edit — Cyberpunk, Core Keeper, V Rising, Minecraft |
| **2a** | one field, **inside compression** | bounded byte budget — **Palworld, alone** |
| **2b** | a binary field outside compression, plus offsets or a second copy that is validated | a real transaction — Valheim, Satisfactory, Terraria, Raft |
| **3** | there is no name, or it is encrypted | launcher label only — Subnautica, No Man's Sky |

Counts across 18: five at 0, seven at 1, one at 2a, four at 2b, two at 3.

**Duplicate is a separate axis and must not be inferred from the rename tier.** The question is
whether player-side state lives *outside* the save keyed by an id *inside* it — Valheim's `.fch`,
Terraria's `.map`, Core Keeper's `mapparts`, 7 Days to Die's shared `GeneratedWorlds`. Where it does,
there is no clean duplicate: keep the id and both copies share the player's map, change it and the
copy starts blank. That is a product decision to surface, never one to make silently.

---

## 4. What actually generalizes: cloud awareness

**The launcher has none. Zero.** And it is the one piece of this that needs no per-game curation at
all.

`Steam\userdata\<uid>\<appid>\remotecache.vdf` is plain VDF listing every synced file with its
relative path, size, sha1 and sync state. It is what proved, this morning, that a deleted Palworld
world was going to come back — and later that the in-game delete had genuinely cleared it. One Core
parser answers, for every Steam game at once:

- **is this save cloud-tracked?** — which decides whether a delete will stick
- **what would a delete leave behind?** — the entries that would resurrect it
- **what does a duplicate cost?** — Palworld's two worlds are 30.92 MB and 0.31 MB of tracked quota,
  so duplicating the big one silently adds ~31 MB to the user's cloud allowance

Corroborated independently across Grounded, Core Keeper and Stardew Valley, whose documented restore
recipe is *exactly* the workaround we stumbled into: change the files while the game is running, so
the client notices a mid-session change.

Two shapes exist and they behave differently: `steam_autocloud.vdf` (Auto-Cloud, glob-based — the
resurrection case we hit) and the Cloud API `remote\` folder, which **relocates the entire save path**
when cloud is enabled. Enshrouded is the latter. Of the 18 surveyed, only 7 Days to Die has no cloud
at all.

---

## 5. The prerequisite nobody asked for

`saveDirHint` is wrong-in-kind for roughly half the feed. The miner reads Ludusavi's per-path `tags`
and throws them away, and ignores the `when`/OS clause entirely, then takes `SavePaths[0]`. Result
across 148 hints: 24 non-Windows paths, 38 config directories, 12 `<base>`-relative — including
`monster-hunter-wilds` → `<base>/config.ini` and `7-days-to-die` → a Linux config path.

Nothing in the app reads `saveDirHint` today (verified: zero consumers outside the manifest types), so
this is inert and free to fix. **It stops being free the moment anything reads it** — and a
`saveLayout` field describes the folder that hint points at, so shipping layout on top of bad hints
would be worse than shipping nothing.

Fix `LudusaviNormalize.ToCandidates` first: honour the tags it already parses, filter to
Windows-applicable paths, prefer non-`<base>`.

---

## 6. Layout must be declared, not derived or sniffed

Both cheaper options were tested and both are dead.

**Not derivable from Ludusavi.** Its schema has no directory-vs-file marker — the entire tag
vocabulary across 53,006 games is `save` and `config`. Palworld's entry and Elden Ring's are the same
shape of statement, no wildcard, no recursion, because Ludusavi zips whole trees and never needs to
know the shape. The two games the enum exists to distinguish are indistinguishable in the source data.

**Not safely sniffable at runtime.** Every verified folder-per-save game on this machine also has
loose files at the top level (Palworld's `GlobalPalStorage.sav`, Sons Of The Forest's
`PlayerProfile.json`), and two flat games have subdirectories — Elden Ring's, created by a mod.

So: an explicit field, which is also the honest one. Layout is a fact about the game, exactly like
`banRisk` and `groupingRule` already in the schema, and it stays descriptive — *what the folder looks
like*, never *how to enable a mod*.

```jsonc
"saveLayout": "worlds"     // camelCase on disk; "typedFiles" | "worlds" | absent
```

Typed `string?`, not the enum, so an unrecognised value from a newer feed degrades instead of failing
deserialization and dropping the whole feed. **`null` means "nobody looked", not "flat"** — today's
code claims `TypedFiles` for 149 games it has never checked. `TypedFiles` stays the runtime fallback;
the manifest gets to distinguish a checked flat game from an unexamined one.

No `schemaVersion` bump and no `minBinaryVersion` bump: unknown JSON members are skipped by default,
so old binaries ignore the field and keep the other 150 games. Bumping either would make every shipped
install reject the entire feed to protect a field they would have ignored. `safeRoute` is the
precedent — 21 lines across four files — and its bug is already fixed (`dbb223e`).

Roughly 30 of 150 games look folder-per-save. Palworld is not a one-off and not close to a majority.

---

## 7. Order

1. **The credential exclusion** (§1) — pending a decision, and ahead of everything if the answer is
   yes. It is the only item here with a live user consequence.
2. **`LudusaviNormalize` tag + OS filter** (§5). Small, testable, zero blast radius today.
3. **Steam Cloud reader** (§4). Pure Core, no curation, and it retires the guesswork behind
   *"will this delete stick?"* for every Steam game at once.
4. **`saveLayout` in the manifest** (§6), then retire the hardcoded `PalworldAppId`. Back up and
   restore per save-unit — already built, format-free — light up wherever the feed declares `worlds`.
5. **A save-metadata reader seam, with Cyberpunk as the second implementation.** Two implementations
   is where the right shape becomes visible. Designing the abstraction off Palworld alone would bake
   in a byte budget that, per §2, no other game in the survey has.

## Non-goals

- **Rename and duplicate as generic features.** They are earned per game, against evidence, and for
  some games the honest answer is that we do not offer them. Terraria is the example: its rename eats
  every character's explored map, and the game has shipped its own rename since 1.4.2.
- **Anything Microsoft Store.** Every curated entry is Steam-scoped until Store containers are their
  own piece of work.
- **A generic save-format framework.** Five tiers and two orthogonal axes is a description of reality,
  not an interface to build against yet.

## Open questions, to be answered by evidence and not by guessing

- Does Terraria's own 1.4.2 rename fix up the `.map` sidecars, or does vanilla eat the explored map
  too? Decides whether our rename would be *worse than vanilla* or merely *the same*.
- Grounded: which file holds the world name on a current build. One five-minute test — create a world
  named `ZZQQ_MARKER`, grep the folder as ASCII and UTF-16LE — decides tier 1 vs 2.
- Whether `remotecache.vdf`'s sync-state field distinguishes "pending upload" from "synced". That
  decides whether we can say *"this delete will stick"* or only *"this file is cloud-tracked"*.
