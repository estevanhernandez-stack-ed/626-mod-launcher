# Windrose save format — 2026-05-26

Research artifact for the Windrose Save Editor MVP (Task 0 of 8). This pins the on-disk schema we will read and write through `RocksDB.NET` + a from-scratch BSON codec.

**Critical correction up front.** The plan template assumes "byte offsets into a binary character struct" (Souls/From-style). Windrose is **not** that shape. Per-character data lives as a **BSON document** stored as a single key/value in a **RocksDB column family**. So the schema is field names + BSON types, not hex offsets. Task 4's RocksDB round-trip test will assert on `BSONDoc → bytes → BSONDoc` equality, not on offset reads.

## Save folder location

```
%LOCALAPPDATA%\R5\Saved\SaveProfiles\<AccountID>\RocksDB\0.10.0\Players\<CharacterGUID>\
```

(Resolved by Ludusavi + Steam-user-id wiring already shipped in PR #45.)

**Account identifier varies by store:**
- **Steam:** numeric SteamID64 (≥10 digits), e.g. `76561197993424152`.
- **Epic Games:** 32 hex chars, e.g. `a1b2c3d4e5f6...`. (RimmyCode `save/location.py::account_type` distinguishes them by shape.)

**Linux / Proton path:**
```
~/.local/share/Steam/steamapps/compatdata/3041230/pfx/drive_c/users/steamuser/AppData/Local/R5/Saved/SaveProfiles/
```

**Save format version is part of the path** — the `0.10.0` segment is the on-disk schema version. RimmyCode hardcodes it (`find_player_dirs` uses literal `RocksDB/0.10.0/Players`). BlackDaiver parses it out of the `_Latest.zip` filename. If the game ships a future `0.11.x`, this path moves.

**Newer save layout also exists** — BlackDaiver references `RocksDB_v2/<version>/Players/<guid>` and a `RocksDB_v2_Backups/Players/<guid>/<guid>_<version>_Latest.zip` checkpoint that the game restores from on every launch. **The game IGNORES WAL files** in this newer layout — see "Write strategy contradiction" in Open Questions.

**Steam App ID:** **`3041230`** (verified — SteamDB, Steam Support URL, PCGamingWiki, RimmyCode `_APP_ID = '3041230'` in `save/location.py`). The earlier handoff value `2399830` and the plan placeholder were both wrong. The Windrose Dedicated Server is a separate app at `4129620`.

## RocksDB store contents

Standard RocksDB layout, per-character folder:

| File | Purpose |
|---|---|
| `CURRENT` | Points at the active `MANIFEST-*` file. |
| `MANIFEST-NNNNNN` | Column-family metadata, last sequence number, log number. |
| `<N>.sst` (typically zero-padded, e.g. `000007.sst`) | Sorted-string tables — the bulk of compacted save data. |
| `<NNNNNN>.log` | Write-Ahead Log — recent writes, replayed into memtables. |
| `LOG` | RocksDB's own diagnostic log. Not save data. |
| `OPTIONS-NNNNNN` | RocksDB options snapshot. Not save data. |
| `LOCK` | Exclusive lock file. Must be released (game closed) before opening for write. |
| `IDENTITY` | Per-database UUID. Generated if missing on ZIP extraction. |

**Compression**: `kNoCompression` (per BlackDaiver `RocksDbAccess.ReadFromSstDirect` comment — the pure-C# SST scanner works precisely because the game doesn't compress).

**WAL block size**: `32768` bytes (`BLOCK_SIZE = 32768` in both BlackDaiver's `RocksDbAccess.cs` and RimmyCode's `rocksdb/wal.py`).

**Checksum**: RocksDB CRC32C (Castagnoli polynomial `0x82F63B78`), **NOT** standard CRC32-IEEE. WAL uses a masked variant (`wal_masked_crc` in `crc.py`). The .NET impl needs the same Castagnoli table.

## Column family (CF) schema

Both reference editors agree on these CF IDs (BlackDaiver `RocksDbAccess.cs` constants; RimmyCode `CLAUDE.md` "NEVER" block):

| CF ID | Name | Contents | Write rule |
|---|---|---|---|
| 0 | `default` | (RocksDB default — usually empty in Windrose) | Do not touch. |
| 1 | `R5LargeObjects` | (Engine-managed large blob overflow.) | Do not touch. |
| **2** | **`R5BLPlayer`** | **Player BSON document, keyed by 32-byte player key.** | **Primary write target.** |
| 3 | `R5BLShip` | Per-ship BSON documents, keyed by ASCII GUID (`Encoding.ASCII.GetBytes(guid.ToUpperInvariant())`). | Write only when editing ships. |
| 4 | `R5BLBuilding` | Per-building BSON documents. | Editor scope: **do not touch.** |
| 5 | `R5BLActor_BuildingBlock` | Player actor data on newer save versions; same BSON shape as CF 2. | BlackDaiver writes player BSON to BOTH CF 2 and CF 5 to cover post-update saves. RimmyCode writes CF 2 only. See Open Questions. |

**Conflict to flag:** RimmyCode's `CLAUDE.md` says `NEVER write to column families other than CF 2`. BlackDaiver explicitly writes both CF 2 **and** CF 5 because "the editor may detect CF 5 as primary because it has a higher SST file number" (SaveFile.cs:921-926). The .NET MVP should follow BlackDaiver's belt-and-suspenders pattern — write to both CFs when a primary-CF-detection step picks CF 5, fall back to CF 2 only when CF 5 is empty.

## Key schema

| Column family | Key pattern | Value | Notes |
|---|---|---|---|
| CF 2 (`R5BLPlayer`) | **32-byte binary key** (single key per character) | BSON document | The WAL scanner identifies the player entry by `(cf_id == 2 AND key_len == 32 AND val_len > 1000 AND val[0..4] == val_len_le32)`. The BSON document begins with a little-endian uint32 = document length, which is the disambiguator. Key bytes are not human-readable text; they're a stable identifier derived from the character GUID by the game. **Do not invent keys** — copy them verbatim from the existing save. |
| CF 3 (`R5BLShip`) | ASCII-encoded uppercase GUID-N (`"C0F5..."`, 32 hex chars) | BSON document | E.g. `Encoding.ASCII.GetBytes(ship.Guid.ToUpperInvariant())` — 32 bytes of ASCII. |
| CF 5 (`R5BLActor_BuildingBlock`) | Same 32-byte key as the matching CF 2 entry | BSON document (same shape as CF 2 value) | Mirror of CF 2 on post-update saves. |

**No string-prefix scheme** like `Profile/<guid>` or `Players/<guid>`. The "Players/" segment in `R5\...\Players\<GUID>\` is a **filesystem path**, not a RocksDB key. Inside the database, keys are opaque bytes scoped by column family.

**Sentinel / version stamps inside the BSON**: `_guid` (string), `PlayerName` (string). The `_guid` field is the verify-on-read sanity check (RimmyCode `commit.py::verify_wal`: `if not doc.get("_guid"): return False`).

## Per-character BSON layout

The save root is a single BSON document. Field names are PascalCase, exactly as the game writes them. All offsets within the BSON are determined by the BSON spec itself — there is no fixed table to pin.

**Top-level fields confirmed by source code** (BlackDaiver SaveFile.cs, RimmyCode reader.py + editors):

| Path | BSON type | Meaning | Source |
|---|---|---|---|
| `_guid` | string (0x02) | Character GUID. Read for verify. Do not change. | SaveFile.cs:186, commit.py:67 |
| `PlayerName` | string (0x02) | Display name. | SaveFile.cs:185, location.py::peek_player_name |
| `ShipOwner` | document (0x03) | Active ship metadata. Sub-fields: `FlagshipId`, `PossessedShipId`, `DefaultShipData.DefaultShipId`, `DefaultShipData.bIsUnlocked`. | SaveFile.cs:201-209, 720-723 |
| `Inventory.Modules.<idx>.Slots.<idx>` | document tree | Per-module inventory grid. See "Inventory" subsection. | reader.py::get_all_items |
| **`PlayerMetadata.PlayerProgression.StatTree`** | **document** | **Stats — six core attributes.** | reader.py:`get_progression`, stats.py |
| **`PlayerMetadata.PlayerProgression.TalentTree`** | **document** | **Talents — 37 nodes across 4 trees.** | reader.py:`get_progression`, skills.py |
| `WasTouchedItems` | array (0x04) | Items the game has registered. Add new items here when inserting. | GUIDE.md "registers the item in the game's internal `WasTouchedItems` list" |

**Critical path correction.** RimmyCode's `get_progression` resolves to **`doc['PlayerMetadata']['PlayerProgression']`** — not `doc['Progression']` directly. Every stats/skills field path below is rooted there.

### Stats subtree

```
PlayerMetadata.PlayerProgression.StatTree.ProgressionPoints           # int32, sum of NodeLevels (derived; rewrite on every edit)
PlayerMetadata.PlayerProgression.StatTree.Nodes.<key>                 # document, key is "0".."5"
  ├─ NodeLevel                                                         # int32, the editable stat value [0..MaxNodeLevel]
  └─ NodeData
       ├─ MaxNodeLevel                                                  # int32, cap (default 60)
       └─ Perks.<key>                                                   # string, DA asset path identifying the stat
                                                                        # e.g. "/R5BusinessRules/.../DA_Strength_Stat.DA_Strength_Stat"
```

**Which stat is which** is determined by looking at the DA asset name inside `Perks` and matching against this table (RimmyCode `game_data.py::STAT_NAMES`):

| DA asset name | Display name |
|---|---|
| `DA_Strength_Stat` | Strength |
| `DA_Agility_Stat` | Agility |
| `DA_Precision_Stat` | Precision |
| `DA_Mastery_Stat` | Mastery |
| `DA_Vitality_Stat` | Vitality |
| `DA_Endurance_Stat` | Endurance |

**Caps**: `MaxNodeLevel` per node, default `60`. Floor is `0` (RimmyCode clamps `max(0, min(level, max_level))`).

**ProgressionPoints invariant**: after every stat edit, recompute as `sum(NodeLevel)` across all nodes. Editor must do this — the game uses it for unlock thresholds (GUIDE: "ProgressionPoints… is recalculated automatically").

### Talents subtree

```
PlayerMetadata.PlayerProgression.TalentTree.ProgressionPoints          # int32, sum of NodeLevels
PlayerMetadata.PlayerProgression.TalentTree.Nodes.<key>                 # document
  ├─ NodeLevel                                                          # int32, [0..MaxNodeLevel], default cap is 3
  ├─ ActivePerk                                                         # string, equals Perks[0] when NodeLevel>0 else ""
  └─ NodeData
       ├─ MaxNodeLevel                                                   # int32, default 3
       └─ Perks.<key>                                                    # string, DA asset path
                                                                         # e.g. ".../DA_Talent_Fencer_SlashDamage.DA_Talent_Fencer_SlashDamage"
```

**Tree categories** (per game_data.py::SKILL_CATEGORIES):

| Category | UI direction | DA prefix |
|---|---|---|
| Fencer | UP | `DA_Talent_Fencer_` |
| Toughguy | LEFT | `DA_Talent_Toughguy_` |
| Marksman | DOWN | `DA_Talent_Marksman_` |
| Crusher | RIGHT | `DA_Talent_Crusher_` |

**Per-talent caps**: `MaxNodeLevel = 3` per RimmyCode default + GUIDE confirmation ("Each skill has a level of 0–3").

**ActivePerk write rule**: set to the perk path string when `NodeLevel > 0`, set to `""` when `NodeLevel == 0`. (RimmyCode `skills.py::set_skill_level`.)

**Auto-create unlocked nodes**: if a talent isn't in `Nodes` yet (player hasn't unlocked it), the editor inserts a new node before setting its level. The .NET MVP can defer this and ship "edit existing talents only" first.

### Inventory subtree (out of MVP scope — captured for completeness)

```
Inventory.Modules.<idx>.Slots.<idx>.ItemsStack.Count          # int32 stack size
Inventory.Modules.<idx>.Slots.<idx>.ItemsStack.Item.ItemId    # string GUID
Inventory.Modules.<idx>.Slots.<idx>.ItemsStack.Item.ItemParams        # string DA asset path
Inventory.Modules.<idx>.Slots.<idx>.ItemsStack.Item.QualityLevel      # int32, 0 = none
Inventory.Modules.<idx>.Slots.<idx>.ItemsStack.Item.Attributes.<key>  # array element with Tag/Value/MaxValue
Inventory.Modules.<idx>.Slots.<idx>.ItemsStack.Item.Effects           # array
```

The MVP doc covers stats + (optional) talents; inventory is shipped by both reference editors and we have a working blueprint if it lands in a future task.

## Talent node ID table (canonical)

42 DA talent assets are catalogued in `_TALENT_NODE_DATA` (RimmyCode `editors/skills.py`). README says 37 nodes "across the talent tree" — the delta is that not all DA assets are slotted into the live UI tree (some have empty `UISlotTag`). The .NET impl should treat the full 42-asset catalog as canonical when authoring talents.

Full asset list (`DA_Talent_<Category>_<Suffix>`):

**Fencer** (11): ConsecutiveMeleeHitsBonus, CritChanceForPerfectBlock, DamageForSoloEnemy, HealForKill, LessStaminaForDash, OneHandedDamage, OneHandedMeleeCritChance, PassiveReloadBoostForPerfectBlock, PassiveReloadBoostForPerfectDodge, RiposteDamageBonus, SlashDamage.

**Crusher** (9): Berserk, CrudeDamage, DamageForDeathNearby, DamageForMultipleTargets, DamageResistWithTwoHandedWpn, TemporalHPHealBuff, TwoHandedDamage, TwoHandedMeleeCritChance, TwoHandedStaminaReduced.

**Marksman** (11): ActiveReloadSpeedBonus, ConsecutiveRangeHitsBonus, DamageForAimingState, DamageForDistance, DamageForPointBlank, Overpenetration, PassiveReloadBonus, PierceDamage, RangeCritDamageBonus, RangeDamageBonus, ReloadForKill.

**Toughguy** (11): BlockPostureConsumptionBonus, DamageForManyEnemies, DamageResistForHP, ExtraHP, GlobalDamageResist, HealEffectiveness, MeleeDamageResist, ResistForManyEnemies, SaveOnLowHP, StaminaBonus, TempHPForDamageRecivedBonus.

Full path pattern: `/R5BusinessRules/EntityProgression/Talents/<Category>/<Asset>.<Asset>`.

## Value caps (for validation)

| Field | Min | Max | Source |
|---|---|---|---|
| Stat `NodeLevel` | 0 | `NodeData.MaxNodeLevel` (typically 60) | RimmyCode `set_stat_level` clamps; GUIDE.md: "between 0 and its maximum (usually 60)" |
| Stat `ProgressionPoints` | 0 | sum of all stat NodeLevels | Derived, not directly capped — recompute on edit |
| Talent `NodeLevel` | 0 | `NodeData.MaxNodeLevel` (default 3) | RimmyCode `set_skill_level`; GUIDE: "Each skill has a level of 0–3" |
| Talent `ProgressionPoints` | 0 | sum of all talent NodeLevels | Derived |
| Inventory item `Level` | 1 | 15 (for equipment) | BlackDaiver SaveFile.cs:759 sets `MaxValue=15`; GUIDE: "level (1–15 for equipment, 0 for non-equipment)" |
| Inventory item `Count` | 1 | (unspecified — community item DB may cap per item) | BlackDaiver `Math.Max(1, count)` clamp |
| `PlayerName` length | 1 | (unspecified — game UI cap probably ~24) | Neither editor enforces — keep existing if user doesn't override |

**Unverified for MVP**: gold (no `Gold` field surfaced in either editor — likely lives under `Inventory.Modules.<resource_module>.Slots` as a stacked currency item, not as a top-level scalar). XP is not exposed as a freely-editable field by either reference editor — `ProgressionPoints` is the derived progression metric. **Plan template's "Gold" and "XP" fields do not exist as direct scalars** — flag in Open Questions.

## Sentinel keys / fields to leave alone

Per-character BSON document fields the editor MUST NOT modify (in addition to "do not invent CF 2 keys"):

- `_guid` — identity verifier; used by the WAL readback sanity check.
- `Inventory.Modules.<idx>.AdditionalSlotsData` — module capacity grants from gear; modifying breaks capacity invariants.
- `ShipOwner.DefaultShipData` — first-run-only data; touching it can prevent the game from restoring the player's default ship.
- `ScenarioSave.ExecutorId` — owned by the game; non-empty arbitrary values crash on ship-click (BlackDaiver SaveFile.cs:600 comment).
- Any field whose name starts with `_` (BSON convention for engine-private fields).

RocksDB-level "do not touch":
- `CURRENT`, `MANIFEST-*`, `LOG`, `OPTIONS-*`, `IDENTITY` — engine-managed metadata.
- `LOCK` — must be acquired exclusively; the game must be closed first.
- Column families 0 (default), 1 (`R5LargeObjects`), 4 (`R5BLBuilding`) — outside our edit scope.

## Write strategy — two paths

The two reference editors take **different** write paths because they target different save layouts. We document both because Task 1's RocksDB layer chooses one.

### Path A — WAL-append (RimmyCode, "RocksDB/0.10.0" layout)

1. Read MANIFEST (`parse_manifest`) for `last_sequence` + `log_number`.
2. Scan all existing `.log` files; compute `write_seq = max(manifest.last_sequence, max_existing_wal_sequence) + 1`.
3. Create a **new** `.log` file at `(highest_existing_log_num + 1).log` (NEVER overwrite an existing WAL — game corruption).
4. Frame the BSON value as a single `WriteBatch` with `seq=write_seq`, `count=1`, entry type `0x05` (`kTypeColumnFamilyValue`), CF id 2.
5. Fragment to 32KB blocks with CRC32C-Castagnoli per fragment header.
6. Read back via `read_wal` and verify `entry.player_key == expected_key` AND `parse_bson(value).get('_guid')` is non-empty.

**Hard rules from RimmyCode `CLAUDE.md`:**
- Never modify the existing WAL file in place — RocksDB considers that data already applied and the game gets an infinite loading screen.
- Never write CFs other than 2.
- Backup first (`save_backup`) before any write; track via `SaveSession.backed_up` flag.
- BSON byte-perfect round-trip self-check on unmodified loads: `serialize_bson_doc(parse_bson(original_bytes)) == original_bytes`. This is the primary safeguard against silent corruption — Task 4 must replicate it.

### Path B — Direct DB write + checkpoint + ZIP (BlackDaiver, "RocksDB_v2" layout)

Per BlackDaiver SaveFile.cs:946-967 comment block:

> The game IGNORES WAL files: on every launch it restores the live DB from `_Latest.zip` (SSTs only), discarding any WAL we injected. The only reliable path is: open the live DB in write mode via `rocksdb.dll`, write the BSON with `put_cf`, force a flush to SST, then create a checkpoint and package it as the new `_Latest.zip`.

1. Acquire LOCK (game must be closed).
2. Open live DB via `RocksDB.NET` with column families [`default`, `R5LargeObjects`, `R5BLPlayer`, `R5BLShip`, `R5BLBuilding`, `R5BLActor_BuildingBlock`].
3. `put_cf(CF_PLAYER, key, bson_bytes)` AND `put_cf(CF_ACTOR, key, bson_bytes)` (write to both — game may use either).
4. Force flush all CFs to SST.
5. Create RocksDB checkpoint.
6. Zip checkpoint contents in the specific structure: `Checkpoint/shared_checksum/*.{blob,sst}` + `Checkpoint/private/1/{CURRENT,MANIFEST-*,OPTIONS-*}`.
7. Write as `<guid>_<version>_Latest.zip` into `RocksDB_v2_Backups/Players/<guid>/`.

**Open question for Task 1**: which layout does the current Windrose release ship? The 0.10.0 path is from Early Access launch (Apr 2026). The "RocksDB_v2" + ZIP-checkpoint path may be a post-EA update. Task 1's pre-flight detection should look for `RocksDB_v2/` first; fall back to `RocksDB/0.10.0/` if absent.

## Backup policy (locked from references)

**MANDATORY** before any write:
- BlackDaiver: copies the entire account-root folder (climbs up from save dir until it finds a SteamID-shaped folder, refuses to back up `C:\` or `C:\Users`). Skips `LOCK` and `*.tmp` files.
- RimmyCode: `save_backup()` must be called before `commit_changes()`. Tracked by `SaveSession.backed_up` flag. **No bypass.**

Both back up the full save root (Players + Accounts + Worlds), not just the Player CF. The .NET MVP should match — back up everything under the SteamID folder timestamped, then write. Restore is the user's escape hatch.

## Sources cited

- **RimmyCode/Windrose-Save-Editor** (a.k.a. WSE Project) — Python, GitHub default branch `main`, no SPDX license declared. Repository: <https://github.com/RimmyCode/Windrose-Save-Editor>. README states "provided as-is for personal use; see Nexus Mods page for terms" (<https://www.nexusmods.com/windrose/mods/153>). Key files cited:
  - `windrose_save_editor/game_data.py` — STAT_NAMES, SKILL_CATEGORIES, TALENT_NAMES, TALENT_DESCS, _TALENT_NODE_DATA.
  - `windrose_save_editor/editors/stats.py` — stat node path, ProgressionPoints recalc.
  - `windrose_save_editor/editors/skills.py` — talent node path, ActivePerk rule, full DA asset table.
  - `windrose_save_editor/inventory/reader.py` — `get_progression` (`doc['PlayerMetadata']['PlayerProgression']`), inventory tree.
  - `windrose_save_editor/save/commit.py` — write strategy (WAL-append, new file number, CF 2 only).
  - `windrose_save_editor/rocksdb/wal.py` — WAL block format, CF/key/value framing.
  - `windrose_save_editor/save/location.py` — Steam App ID `3041230`, account-type detection (Steam vs Epic), Linux/Proton paths.
  - `windrose_save_editor/crc.py` — CRC32C (Castagnoli).
  - `CLAUDE.md` — write rules ("NEVER write CFs other than 2", "NEVER modify existing WAL", "NEVER skip BSON round-trip check").

- **BlackDaiver/WindroseEditor** — C#, GitHub default branch `master`, no SPDX license declared. Repository: <https://github.com/BlackDaiver/WindroseEditor>. Key files cited:
  - `SaveFile.cs` — top-level BSON fields (`_guid`, `PlayerName`, `ShipOwner`), inventory tree, ship handling, ZIP-checkpoint write path, backup policy.
  - `RocksDbAccess.cs` — column family IDs (CF_PLAYER=2, CF_SHIP=3, CF_BUILDING=4, CF_ACTOR=5), CRC32C Castagnoli implementation, WAL block constants, "game ignores WAL" comment.
  - `BsonDocument.cs` — BSON type constants (subset of MongoDB BSON: 0x01 double, 0x02 string, 0x03 doc, 0x04 array, 0x05 binary, 0x08 bool, 0x09 datetime, 0x0A null, 0x10 int32, 0x12 int64), ordered-document round-trip parser/serializer.
  - `README.md` — confirms save folder path and `.NET 8` runtime baseline.

- **Chris971991/windrose-character-editor-releases** — binary-only release repo (<https://github.com/Chris971991/windrose-character-editor-releases>). **Source is private.** MIT license claimed in description but source is not inspectable. Cannot use as an offsets/schema source for Task 0. Documented here only because the plan named it as a reference.

- **Steam App ID verification**: SteamDB (<https://steamdb.info/sub/1081129/>), Steam Support (<https://help.steampowered.com/en/wizard/HelpWithGame/?appid=3041230>), PCGamingWiki (<https://www.pcgamingwiki.com/wiki/Windrose>), cross-checked against RimmyCode `_APP_ID = '3041230'`.

- **Item ID Database HTML** — `Item ID Database.html` lives in the RimmyCode repo (<https://github.com/RimmyCode/Windrose-Save-Editor/blob/main/Item%20ID%20Database.html>). License situation flagged below.

- **facebook/rocksdb** — the engine `RocksDB.NET` wraps. BSD-3-Clause + Apache-2.0 dual license. <https://github.com/facebook/rocksdb>. Used implicitly via `RocksDB.NET` NuGet on the .NET side.

- **AllThings.How write-up** — secondary press coverage, not a schema source. <https://allthings.how/windrose-save-editor-how-khbins-python-tool-edits-r5-inventories/>. Contains the BSON-document framing claim but no specifics.

## Open questions / risks

1. **Write strategy ambiguity (HIGH).** RimmyCode writes a new WAL file and trusts RocksDB to replay it. BlackDaiver says the game IGNORES WAL on the newer save layout and writes directly + checkpoints + repackages the ZIP. **Task 1's RocksDB layer must detect which layout the user's save is on** (presence of `RocksDB_v2/` vs `RocksDB/0.10.0/`) and pick a write path accordingly — or pick one path (preferably the BlackDaiver direct-write-and-checkpoint, since it's the more recent observation and survives game-restart) and require the corresponding save layout. **Task 4's round-trip test should write, then close the DB, then re-open, then read — to confirm the write actually persisted past a close cycle.**

2. **CF 5 (`R5BLActor_BuildingBlock`) presence (MEDIUM).** Newer saves may have player data in CF 5 instead of, or in addition to, CF 2. BlackDaiver writes both. RimmyCode `CLAUDE.md` forbids writing CFs other than 2 — but `CLAUDE.md` was clearly written before the post-update Actor CF appeared (and is referenced in RimmyCode's own `_GAME_PROCESS_NAMES`). **Recommend: write both CF 2 and CF 5 when both are non-empty, log when one is missing.**

3. **Gold and XP are NOT top-level scalars (MEDIUM — plan template assumption wrong).** Neither reference editor exposes a `Gold` or `XP` field on the player BSON. Gold is almost certainly a currency item under `Inventory.Modules.<idx>.Slots` (a stackable `Resource_` item). XP doesn't appear at all — character progression is driven by `ProgressionPoints` (sum of stat NodeLevels), which is derived, not freely editable. **If the MVP UI advertises "edit gold and XP," either (a) wire gold to its inventory slot and drop XP entirely, or (b) drop both for v1 and ship stats + talents only.**

4. **WSE Project (RimmyCode) license is NOT a standard OSS license (HIGH — affects Task 7).** RimmyCode README: "This project is provided as-is for personal use. See the Nexus Mods page for terms of use and redistribution details." Same wording in Item ID Database HTML's parent repo. **This is a "personal use" license, not MIT/Apache/BSD/CC0.** Task 7 plans to ship the Item ID Database with the 626 Mod Launcher — that may not be allowed without explicit author permission. **Recommend: ask the author (via Nexus Mods DM or a GitHub issue on the repo) for permission, OR re-derive an item DB from FModel-extracted game assets (RimmyCode README credits FModel for the original extraction).** Plan template was right to call this out as "WSE Project license needs to be confirmed" — confirmation is now "non-OSS, requires permission."

5. **BlackDaiver license also not declared (LOW).** No LICENSE file in the BlackDaiver repo. GitHub treats undeclared as all-rights-reserved by default. We don't need to redistribute BlackDaiver's source — only learn from it — so this is informational, not blocking. If we lift specific code patterns, attribute the author in code comments.

6. **Talent node count: 37 vs 42 (LOW).** Source disagreement: RimmyCode README + GUIDE say "37 talent nodes". RimmyCode `_TALENT_NODE_DATA` lists 42 DA assets. The 5-asset delta has empty `UISlotTag` — they're catalogued but not slotted in the live UI grid. The MVP should treat the live-tree count (37) as the user-facing number and the 42-asset table as the canonical write-target catalog (so an unlocked-via-editor talent that the UI later promotes to a real slot still works).

7. **Steam vs Epic SaveProfiles folder (LOW).** The Epic save folder is 32 hex chars; Steam is numeric ≥10 digits. The launcher's Ludusavi wiring resolves the Steam path. **Epic Games delivery of Windrose may or may not be live as of 2026-05-26** — if it is, we need an Epic-account-folder detection branch. RimmyCode handles it via `account_type` regex.

8. **Save schema version drift (MEDIUM, future).** Path version `0.10.0` is hardcoded in RimmyCode. When Windrose ships a `0.11.x` save format, this path becomes wrong. The .NET MVP should glob `RocksDB/*/Players/` (or `RocksDB_v2/*/Players/`) rather than hardcoding `0.10.0`.

9. **Multi-character saves.** A SteamID folder can contain multiple `Players/<GUID>` folders (one per character). RimmyCode prompts the user to pick. The .NET MVP needs the same character-picker before opening any character for edit.

10. **`WasTouchedItems` registration.** Per RimmyCode GUIDE.md ("registers the item in the game's internal `WasTouchedItems` list so the game recognises it correctly"), inventory inserts must also update `WasTouchedItems`. The exact path within the BSON wasn't quoted in any file I fetched directly — it's referenced in `inventory/writer.py` past line 80. Out of MVP scope for stats/talents, but Task 5+ (inventory) will need to pin this exact path.

## Self-review

- I fetched real source files from real repos. Specifically: BlackDaiver `SaveFile.cs`, `RocksDbAccess.cs`, `BsonDocument.cs`; RimmyCode `editors/stats.py`, `editors/skills.py`, `game_data.py`, `inventory/reader.py`, `inventory/writer.py`, `save/commit.py`, `save/location.py`, `rocksdb/wal.py`, `README.md`, `GUIDE.md`, `CLAUDE.md`.
- The two references agree on: column family IDs, BSON-document structure, CRC32C-Castagnoli, WAL block size (32KB), the six stat names, the four talent trees, the 0–60 stat cap, the 0–3 talent cap, the save folder path shape.
- The two references **disagree** on write strategy (WAL-append vs ZIP-checkpoint) and CF coverage (CF 2 only vs CF 2 + CF 5). Documented explicitly as Open Questions 1 and 2, not papered over.
- Steam App ID `3041230` verified against four independent sources (SteamDB, Steam Support URL, PCGamingWiki, RimmyCode source code).
- Plan template's "byte offsets" framing was wrong — Windrose is BSON, not a packed struct. The doc reframes the schema as field paths + BSON types. Task 4's round-trip test will assert on `BSONDoc` equality, not on offset reads.
- Plan template's "Gold/XP fields" were unverified assumptions — flagged in Open Question 3.
- Chris971991's source is private — cannot use as a third confirming reference. Acknowledged as a hard limitation, not papered over.
