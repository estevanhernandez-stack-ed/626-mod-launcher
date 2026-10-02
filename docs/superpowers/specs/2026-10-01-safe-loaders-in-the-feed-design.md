# Ban-safe loaders in the feed — design

**Date:** 2026-10-01
**Status:** Spec, built in the same branch. Supersedes the "no new manifest field required" non-goal of
`2026-06-26-ban-safe-loaders-design.md`. That spec put the safe-path knowledge in compiled code so
growing it would need no feed change. In practice that made it need an app release instead.
**Board:** 626 Labs dashboard task "Manifest-track safe loaders (move the ban-safe-loader pointer into
the feed)". Este chose this ("option 2") on 2026-06-26.

## The problem

`banRisk`, the level, is manifest-tracked: a game newly flagged in the feed gets the warning the same
day. The cure is not. Which loaders are ban-safe, how to detect them and where to get them all live in
`KnownLoaderCatalog` (`src/ModManager.Core/Loaders/KnownLoader.cs`), which is compiled. So a game
flagged as ban-risk today shows the warning, but the gate can point at a safe loader only after a
binary release adds one.

## The shape

A top-level `loaders` list on the manifest, beside `games`:

```json
{
  "schemaVersion": 1,
  "games": [ ... ],
  "loaders": [
    {
      "id": "seamless-coop",
      "displayName": "Seamless Co-op",
      "engine": "fromsoft",
      "steamAppId": "1245620",
      "launcherExeNames": ["launch_elden_ring_seamlesscoop.exe", "ersc_launcher.exe"],
      "getUrl": "https://www.nexusmods.com/eldenring/mods/510",
      "author": "LukeYui",
      "banSafe": true
    }
  ]
}
```

**Top-level, not per game.** Mod Engine 2 is engine-wide: it applies to every FromSoft game,
including ones the manifest has never heard of. A per-game field would have to repeat it on every
FromSoft entry and still miss the unlisted ones. The fields mirror `KnownLoader` one for one, and
scoping is `engine`, optionally pinned by `steamAppId`, `gameIds`, or both (see Scoping below).

**No schema version bump.** Like `saveLayout` and `safeRoute` before it, an older binary ignores a
field it does not know, and that is the correct degradation: it keeps its own compiled list.

## Scoping

Added 2026-10-02, after the first cut shipped with Steam-only pins.

A loader always names its `engine`. With no pin it applies to every game on that engine. It can be
pinned two ways, and either one is enough:

- **`steamAppId`**: one Steam game. A Steam id names a game only on Steam, so this pin never reaches
  an EA app game (registered with no Steam id on purpose, so Play does not route through `steam://`)
  or the EA copy of a game sold on both stores.
- **`gameIds`**: manifest ids, the store-neutral pin. Every registration carries one, so this reaches
  EA-only games and every store's copy of a multi-store game. It is the same lesson the ban-risk work
  learned on 2026-09-13: `BanRiskCatalog.Effective(game)` resolves by manifest id as well as Steam id.

A `gameIds` pin is matched against every manifest id the registration **resolves** to
(`ManifestIdLookup.IdsFor`), case-insensitively, never against its raw id alone:

- its own id
- the manifest entry that claims its Steam app id
- the manifest entry that claims its EA content id

The raw id is not reliable. A second store copy of a game the user already has is renamed `<id>-2` by
`EnginePresets.UniqueId`, and a game added before `ManifestIdLookup` existed (or while the feed was
unreachable) carries a slug of its display name. Both still carry their store identity, and that names
the game. Found by the review of #356, whose first cut compared the raw id and so missed the very
multi-store case it was written for.

So `LoaderScan` takes the whole `GameEntry`, never `(engine, steamAppId)`. The old overloads are gone
rather than kept beside the new ones, so no caller can quietly stay Steam-only. A game-id pin narrows a
loader but never moves it to another engine; the engine must still match.

**It must ship in the same release as the `loaders` list itself.** A binary that read `loaders` but not
`gameIds` would see a `gameIds`-only loader as unpinned and offer it on every game on its engine. That
is safe today only because no released binary reads `loaders` yet (0.23.0 predates both). If a release
ever goes out with one and not the other, a feed using `gameIds` needs a `minBinaryVersion` that
excludes it.

**Unknown fields fail closed.** A loader carrying a property this binary does not know is skipped, not
used. A newer feed might pin a loader by a field an older binary cannot read, and ignoring that field
would turn a game-specific loader into an engine-wide one, offered as the safe path on every game on
the engine. The miner, built from the same source, treats an unknown field as a typo and stops.

The validator refuses a `gameIds` entry that is not lowercase kebab-case (a manifest id never is) and
an empty list, which, like an empty `steamAppId`, would be ambiguous between "pinned" and "engine-wide".

## Descriptive only

The manifest's law is that it describes and never prescribes. That holds:

- The feed says which exe names a loader ships, where to get it, and whether its path avoids
  anti-cheat. All of these are facts about the loader.
- The mechanism stays compiled: look for the exe in the game's play folder with `File.Exists`, and
  launch it with the existing tools-row launcher. The feed cannot add a new kind of action.
- The ban-risk gate and its acknowledgement are untouched. A ban-safe loader is guidance shown inside
  the gate, never a way past it.

## Trust

The feed is signed against a pinned key, so this is defence in depth. A loader entry still carries one
trust-sensitive field, because the app launches whatever file has that name in the game folder.
`ManifestValidator` gates every loader entry on both the embedded and the remote path:

- **Skipped:** an engine this binary does not know, the same forward-compatible rule games follow.
- **Rejected:**
  - a blank `id`, `displayName` or `engine`
  - an `id` that isn't lowercase kebab-case. The merge matches ids exactly, so a case variant would
    sit beside the built-in as a second loader instead of correcting it
  - a duplicate `id`, keeping the first. The ban-risk gate keys on the id
  - a `steamAppId` that isn't all digits. An empty pin would match no game at all
  - a null list or a null entry, which degrade like any bad entry, so a bad feed still falls back to
    the embedded manifest instead of throwing
  - an empty `launcherExeNames`
  - any exe name that is not a bare `*.exe` filename: no `..`, no leading or trailing whitespace, and
    none of Windows' invalid filename characters (`<>:"/\|?*` and control characters). That set is
    fixed, not taken from the platform, so the miner on Linux and the launcher on Windows agree
  - a `getUrl` that is not an absolute `https` URL

A rejected loader is dropped alone. The rest of the feed still applies, as with an unsafe `modPath`.

## Merge

The embedded list is the baseline. A remote loader with a new id is appended, and absence from the
feed never removes a baseline loader. A remote loader with an existing id **replaces** it.

This differs from game entries, which field-merge, on purpose. The remote manifest is validated on its
own before the merge, and the validator rejects a loader missing its identity, exe names or URL. So a
partial "just flip `banSafe`" entry never arrives, and a feed loader is always complete. Replacement
says exactly what the feed said. It also lets the feed:

- withdraw a ban-safety claim, with `"banSafe": false`
- widen a pinned loader to engine-wide, with no `steamAppId`

To correct one field, the feed carries the whole loader with that field changed.

## Migration

The two compiled entries, Mod Engine 2 (engine-wide) and Seamless Co-op (Elden Ring), move into the
embedded `games-manifest.json` unchanged. `KnownLoaderCatalog.Catalog` becomes a projection of
`EffectiveManifest.Current.Loaders`, cached by `EffectiveManifest.Generation`. `KnownLoader` and
`LoaderScan` keep their shapes, so the App's three call sites do not change. A golden test pins the
projection to exactly what the compiled list held, so the move cannot quietly change behaviour.

## The miner

Curated loaders live in `overrides/loaders/*.json`, one loader per file. The game override loader
reads only the top level of `overrides/`, so the subfolder does not collide with it. The
`--with-overrides` step loads them, refuses the run on a duplicate id, and puts them on the draft. The
draft then goes through the same `ManifestValidator` gate as everything else.

## Known gap: `editsSaves`

The field is carried so the runtime `KnownLoader` can say a loader writes to saves, but launching a
loader does not snapshot saves today. Tools do; loaders never needed to, since no shipped loader sets
it. Honouring it is a launcher change, not a feed one, and a curator should not rely on it until then.

## Out of scope

- **The feed data PR.** The embedded list already carries both loaders, so the feed needs nothing until
  a new loader is curated. That is an `overrides/loaders/` file in `626-game-manifest`, with no
  launcher release.
- **Frameworks and direct-inject mods.** `KnownFramework` and `KnownDirectInjectMod` stay compiled;
  they install and toggle, which is behaviour rather than description.
