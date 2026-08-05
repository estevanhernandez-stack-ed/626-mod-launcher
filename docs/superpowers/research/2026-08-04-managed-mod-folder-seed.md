# Spec seed — managed mod folders (external-mod-location games)

> Seed, not a spec. Captured 2026-08-04 from Este's direction during the batch-3 mining kickoff.
> Feed it to a brainstorm → spec pass before building anything.

## The problem it unlocks

The manifest's `modPath` is game-root-relative by law (validator rejects absolute / `..`). A large
class of moddable games keep their mods OUTSIDE the game root — Paradox titles
(`Documents/Paradox Interactive/<game>/mod`), Factorio (`%APPDATA%/Factorio/mods`), BG3
(`%LOCALAPPDATA%/Larian Studios/.../Mods`), Sims 4 (`Documents/Electronic Arts/.../Mods`), Civ V/VI,
tModLoader-managed Terraria, and more. Today these can only ship nexus-only (runtime folder-detect),
and the engine-upgrade queue re-surfaces them forever even though they're "correctly" classified.

## Este's direction (verbatim intent)

- The launcher can start **managing a mod folder** of its own for these games.
- **Ask the user** where they want it — specifically **which drive**, defaulting sensibly: if the
  game is installed on a given drive, offer the mod folder on that same drive.
- **Suggest locations**: a main folder near the top of the drive (above Program Files "and all of
  that nonsense"), or next to the game files — though Este leans **against** next-to-game.

## Two distinct capabilities hiding in here (untangle in the spec)

1. **Expressing external loader paths in the manifest** — the loader itself reads from a fixed
   external location. Needs a schema extension (optional-with-fallback per invariant 1), e.g. a
   tokenized `externalModPath` (`{Documents}/Paradox Interactive/Crusader Kings III/mod`,
   `{AppData}/Factorio/mods`). The validator would need a token allowlist (never raw absolute
   paths). This is what flips the "NEXUS_ONLY_CORRECT" pile to fully-managed.
2. **The launcher's own managed mod storage** — where the launcher keeps mod payloads/holding
   areas, user-placed per drive (Este's drive-picker idea). Interacts with the reversibility law:
   disable-moves-to-holding should land on the SAME volume as the target for atomic moves.

## Constraints that must survive any design

- Reversibility: moves stay same-volume atomic where possible; cross-volume = copy+verify+delete
  with a snapshot trail.
- The manifest stays descriptive: it may NAME an external location; the enable/disable mechanism
  stays compiled code per the layer-3 law.
- Validator: tokens only, no absolute paths, no `..` — same forbidden-paths spirit.
- First-run UX: the drive/location ask must not interrogate users who never touch an
  external-mods game.

## Immediate consequence for batch 3 (already applied)

Queue games verified as external-mod-location get classified **NEXUS_ONLY_CORRECT (pending
managed-mod-folder)** in the batch-3 report — settled for now, pre-staged as the feature's future
beneficiary list, and no longer treated as curation debt.

## Addendum (Este, same session): the ammunition angle

Beyond managing a folder, SURFACE the intel: for external-mod games the launcher should tell the
user where mods live and where to get them ("a true gamer gives their gamer more ammunition").
Schema idea for the spec pass: optional `externalModHint` (descriptive path, tokenized) +
`loaderName`/`loaderUrl` (catalog-style attribution, NOTICE rules apply) — optional-with-fallback
per invariant 1. The batch-3 report's ammunition table is the seed content.
