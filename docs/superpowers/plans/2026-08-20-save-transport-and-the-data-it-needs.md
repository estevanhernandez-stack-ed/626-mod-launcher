# Save transport, and the data the manifest does not carry yet

**Date:** 2026-08-20 · **Follows:** `docs/superpowers/specs/2026-08-20-saves-beyond-palworld-design.md`
**Status:** plan. Phase 1 is buildable now; phases 2–3 are blocked on data that does not exist yet.

## The idea, in one sentence

Move a save between machines without the cloud — and because this is a mod manager, move **what the
save needs to run**, not just its bytes.

That last clause is the whole differentiator. Ludusavi already backs saves up and does it well. What
it cannot know is that a world was built with nine mods and which three are missing on the machine
you just restored it to. Cyberpunk records `isModded` in every save because the studio knows this
matters; we are the only tool in the chain that knows *which* mods.

## Three scopes, and why the third one solves the credential problem for free

An earlier draft proposed relaxing the byte-faithful-snapshot law to strip credentials from backups.
Wrong shape. A snapshot does not need a policy exception; it needs a **scope**.

| scope | contains | for |
|---|---|---|
| **mine** | everything, byte-faithful | today's backup. Unchanged. The law stands. |
| **portable** | everything of yours, no secrets | moving PC to PC. Your character comes with you. |
| **shareable** | the world, no character, no secrets | what you would post on Nexus |

Credentials do not travel in the outbound two — not as an exception, but because an artifact meant to
leave the machine cannot contain them. Cyberpunk's `user.gls` holds a CDPR refresh token valid to
2035; a bundle that carried it would be handing out an account.

**"Share a world without my character" and "do not leak my token" are the same mechanism.** That is
why the credential question stopped being a separate decision.

## The seam is real, and the game shows us where it is

Palworld, verified on disk:

```
WORLD    Level.sav 2.2M · LevelMeta.sav 4K · WorldOption.sav 8K
YOU      LocalData.sav 128K · Players/<char>.sav 21K
```

The proof is the *joined* world: when the world belongs to someone else, the only thing Palworld keeps
locally is `LocalData.sav`. The game has already drawn the line we need.

### First: does the player make a world at all?

**A correction, and it came from Este rather than from the code.** The model above assumes every game
has a world half and a character half. Many do not.

> *"Cyberpunk isn't a world-building game. The world already exists. You don't build in it. There would
> be some games where you just have characters — like Elden Ring."*

That is right, and it changes what `savePlayerPaths` even means. In Cyberpunk and Elden Ring the world
belongs to CDPR and FromSoft; your save is your character's state inside somebody else's world. There
is no world half to keep, so *"share the world without my character"* is not a smaller version of the
request — it is not a coherent request.

So shape comes first, and only world games get a seam:

| | what the save is | what "share" means |
|---|---|---|
| **world** | a place you made, plus who you are in it | the place, without you |
| **character** | you, inside a world the studio made | **you** — a different act, different consequences |

Sharing a character is a real thing people do — Elden Ring saves circulate on Nexus. But it is not the
same feature and must not wear the same word. And it carries a trap the world case does not:

**A character save is often account-stamped.** Every Elden Ring `CharacterSlot` the launcher already
parses carries a `SteamId`, and the `.sl2` holds ten of them in one file. That is why save re-signing
tools exist — a shared FromSoft save does not work for the recipient until the id is patched. So there,
"share" would mean writing an account id into somebody else's save: a long way from a folder copy, and
not something to do quietly.

Cyberpunk's metadata carries a `playthroughID` but no account id, so its saves look portable between
accounts — still your V, though, not a world.

### Then: where the seam is, for the games that have one

The seam is per-game and it is not guessable:

- **Terraria, Valheim** — world and character are separate files in separate directories. Nothing to
  strip; this is why world-sharing is routine in those communities.
- **Palworld** — same seam, one level in.
- **Minecraft** — player data lives *inside* the world (`playerdata/<uuid>.dat`, plus a player tag in
  `level.dat`). Must be stripped deliberately.
- **Cyberpunk** — no seam. Character and world are one blob. "Share the world" is not a coherent
  request; you would be sharing someone's V.

**Where we do not know the seam, we offer *portable* and say so.** Never guess a split — the failure
mode is shipping a stranger someone's character.

**So `Shareable` needs three refusals, not one.** It currently throws a single message. They are three
different facts and only one of them is a gap in our data:

- *world game, seam known* — build it
- *world game, seam not curated yet* — "we haven't worked out which files are your character for this
  game." A to-do.
- *character game* — "this game has no world to share; the save **is** your character." Not a to-do,
  an answer.

---

## Phase 1 — portable, and it needs no new per-game data

Works for all 150 games on day one, because "everything of yours" requires no seam knowledge.

A bundle is a zip plus a manifest, camelCase per the on-disk rule:

```jsonc
{
  "bundleVersion": 1,
  "game":    { "id": "palworld", "steamAppId": "1623730", "name": "Palworld" },
  "scope":   "portable",
  "savePath": "…",                  // relative shape, never the source machine's absolute path
  "excluded": [                      // what was left out, and why. An honest artifact says so.
    { "path": "user.gls", "reason": "credential" }
  ],
  "mods": [                          // the part nobody else can produce
    { "name": "…", "version": "…", "nexusModId": 123, "sha256": "…", "enabled": true }
  ],
  "createdUtc": "…"
}
```

Import restores the save and reports the mod delta — *"this save was built with 9 mods; 3 are
missing"* — with the links to get them. It does not install anything without being asked.

**Credential detection is signature-based, not a filename denylist.** `user.gls` is the one we found,
not the one to hard-code. Scan files under ~1 MB for a JWT signature; skip, record, move on.

**Import is where cloud bites.** Steam will fight a restore, and the launcher currently has zero cloud
awareness. `remotecache.vdf` is plain VDF and answers it generically — see the parent spec §4. Phase 1
should at minimum *warn*; the reader is worth building first if it lands sooner.

## Phase 2 — shareable, blocked on data that does not exist

Needs the launcher to know which parts of a save unit are the player. That is a per-game fact, it is
descriptive, and it therefore belongs in the manifest rather than in compiled code.

## Phase 3 — the public surfaces

Covered below, because it is the part with an audience outside this repo.

---

# The remediation: what the manifest and the page must grow

**Not to be built as part of this plan.** Written down so the work is scoped when someone picks it up.

## A. `games-manifest.json` — three descriptive fields

The operating law holds: the manifest says **what shape a game's saves are**, never **how to rewrite
them**. Mechanism stays compiled, exactly as it does for mod enable/disable.

| field | values | why |
|---|---|---|
| `saveLayout` | `"typedFiles"` \| `"worlds"` \| absent | per-save-unit operations. Absent means *nobody looked*, not *flat*. |
| `savePlayerPaths` | globs relative to a save unit, e.g. `["Players/**", "LocalData.sav"]` | the world/character seam. Absent = we cannot offer *shareable*. |
| `saveNameSource` | `"folderName"` \| `"fileName"` \| `"sidecar"` \| `"inContainer"` \| `"none"` | lets the UI say what it can do before the user asks. The *reader* stays compiled — this is a category, not a parser. |

All three: `string?`/array, additive, **no `schemaVersion` bump and no `minBinaryVersion` bump** —
unknown members are skipped by default, so older binaries ignore them and keep the other 150 games.
`safeRoute` is the precedent (21 lines, four files).

**Prerequisite, and it is not optional.** `saveDirHint` is wrong-in-kind for roughly half the feed —
24 non-Windows paths, 38 config directories, 12 `<base>`-relative, including Palworld's own, which
points at the Microsoft Store `wgs` path rather than the Steam one. All three fields above describe
the folder that hint points at. Fix `LudusaviNormalize.ToCandidates` first: honour the per-path `tags`
it already parses and discards, filter to Windows-applicable paths, prefer non-`<base>`.

**Storefront scoping.** Astroneer is a filename rename on Steam and a UTF-16 blob in a `wgs` container
on the Microsoft Store — same game, same engine, different answer. Every curated save fact is
Steam-scoped until Store containers are their own piece of work. If entries ever need to disagree per
storefront, that is a schema question to settle *before* curating at scale, not after.

## B. Miner + overrides — `tools/ManifestMiner/`

`OverrideEntry`, plus **both** arms of `OverridesMerge` (`ApplyTo` for updates, `NewFrom` for adds),
plus `EffectiveManifest.MergeEntry` in the launcher. That last one is the step `safeRoute` forgot,
which silently dropped remote data for elden-ring, dark-souls-iii and palworld until `dbb223e`.
`ManifestMergeCompletenessTests` now walks the record by reflection, so a field added and not merged
fails immediately — but only if it is declared on `GameManifestEntry`.

Watch `PublishManifest.ForPublish`: it drops any entry earning none of `known-engines` /
`nexus-domains` / `popular-games`. A game added **purely** for a save fact would vanish from the
published feed without a word.

## C. The public surfaces — `626-game-manifest`

Generated by `tools/generate-public.py` from the built manifest. Today they are entirely mod-shaped
and say nothing about saves:

```
SUPPORTED-GAMES.md      | Game | Engine | Mod path | Steam | Nexus |
supported-games.json    id, name, tier, steamAppId, steamUrl, engine, modPath, nexusUrl
```

**`supported-games.json` is a stable consumer contract** — the hub website and the Discord bot read
it. Growing it is an API change with downstream users, so it is additive-only and the existing eight
fields keep their names and meanings.

Proposed addition, one field, deliberately a summary rather than the raw manifest fields — consumers
should not have to reimplement the capability logic:

```jsonc
"saves": "shareable"   // "backup" | "per-save" | "named" | "shareable"
```

- **backup** — whole-folder snapshot and restore. Every game gets this; it needs no curation.
- **per-save** — `saveLayout` known, so back up and restore one save unit.
- **named** — we can read the save's own name, so the panel shows it instead of an ordinal.
- **shareable** — the player seam is known, so a world can be shared without a character.

Each tier strictly contains the one above, so a single ordered value is honest. `SUPPORTED-GAMES.md`
grows a **Saves** column carrying the same value, and the counts line at the top grows a saves
breakdown beside the existing engine-curated / Nexus-only split.

`SCHEMA.md`, `CONTRIBUTING.md`, `overrides/README.md` and the game-request issue template all need the
new fields, because the whole point of the feed is that a contributor can add a game without an app
release.

## D. Sequencing

The public surfaces go **last**. A Saves column is a promise, and the launcher has to be able to keep
it before the page makes it — a page claiming *shareable* for a game the binary cannot share is worse
than a page that says nothing.

1. `LudusaviNormalize` tag + OS filter — inert today, free to fix, blocks everything else
2. Steam Cloud reader — pure Core, no curation, retires the guesswork behind *"will this delete stick"*
3. `saveLayout` + retire the hardcoded `PalworldAppId`
4. Phase 1 bundles — portable, no new per-game data
5. `savePlayerPaths` + `saveNameSource`, curated against evidence, Steam-scoped
6. Phase 2 shareable
7. **Then** the public surfaces, once the binary can back the claim

## Open questions

- Does `savePlayerPaths` want to be globs, or a small named vocabulary the binary interprets? Globs are
  more expressive and harder to validate; a vocabulary is safer and needs an app release per new shape.
  Decide before curating more than a handful.
- Does a bundle carry the game's *own* backup history (Palworld's `backup/`, Terraria's `.bak*`)?
  `DuplicateWorld` already drops it. For transport the answer is probably the same, but a restore that
  silently loses the game's own rollback points deserves a sentence rather than a default.
- Does `remotecache.vdf`'s sync-state field distinguish *pending upload* from *synced*? That decides
  whether we can say *"this will stick"* or only *"this file is cloud-tracked"*.
