# FromSoft save library research — 2026-05-26

Research for Task 0 of the FromSoft Save Editor MVP plan. Decides whether we
adopt a third-party C# library for Elden Ring `.sl2` save handling or port the
logic ourselves.

## Key correction up front — ER saves are NOT AES-encrypted

The task brief specified "BND4 archive + AES-128-CBC slot encryption + MD5
checksum." That description fits **older** FromSoft games (Dark Souls 2 / 3,
Sekiro, Armored Core 6) but **not Elden Ring**. ER's `.sl2` is a BND4 container
with 10 character slots, each prefixed by a 16-byte MD5 checksum. **No
slot-level AES.** Confirmed independently across three reference
implementations:

- `BenGrn/EldenRingSaveCopier` (MIT, C#) — patches save bytes, recomputes MD5,
  no AES anywhere in the codebase.
- `alfizari/Elden-Ring-Save-Editor` (MIT, Python, last updated 2026-04) — same
  pattern: `hashlib.md5(...).hexdigest()` only.
- `Hapfel1/er-save-manager` (Python) — explicit comment: *"Save files appear to
  use MD5 checksums for integrity verification, not encryption."*

Older games (DS3, AC6) do use AES-CBC on the SL2 entries (the AC6 key is public:
`B1 56 87 9F 13 48 97 98 70 05 C4 87 00 AE F8 79`). We can defer that work
until/unless we expand the editor to DS3/Sekiro.

## Candidates evaluated

| Source | License | ER 1.13+ | Bundles? | Last commit | Decision |
|---|---|---|---|---|---|
| `JKAnderson/SoulsFormats` | **GPL-3.0** | yes | n/a | 2025-05-26 | **skipped — incompatible license** |
| `soulsmods/SoulsFormatsNEXT` | **GPL-3.0** | yes (active) | n/a | 2026-05-26 | **skipped — incompatible license** |
| `JuicerMV.SoulsFormats` (NuGet) | **GPL-3.0** | yes | yes | 2025-08-12 | **skipped — GPL package, viral on distribution** |
| `FrankvdStam/SoulMemory` (NuGet) | n/a — not relevant | n/a | n/a | n/a | **skipped — runtime memory reader, not save files** |
| `Nordgaren/Erd-Tools` | **no license** | partial | n/a | 2026-03-26 | **skipped — all rights reserved without a license** |
| `JKAnderson/Yabber` | **GPL-3.0** | n/a (CLI) | no | 2025-05-26 | **skipped — incompatible license + CLI shell-out** |
| `soulsmods/fstools-rs` | Apache-2.0 | partial | no (Rust) | 2026-03-04 | **skipped — Rust + game-asset focused, no save module** |
| `Nordgaren/dantelion-formats` | **no license** | yes (regulation key only) | n/a | 2022-12-31 | **skipped — no license + no save handling** |
| `ClayAmore/ER-Save-Editor` | **Apache-2.0** | yes (most-starred, 360★) | no (Rust app) | 2024-08-13 | **format reference** — used to confirm BND4 layout |
| `BenGrn/EldenRingSaveCopier` | **MIT** | yes (547★) | n/a (whole-app) | 2024-12-09 | **primary porting reference** — C# slot offsets + MD5 |
| `alfizari/Elden-Ring-Save-Editor` | **MIT** | yes (last touched 2026-04) | no (Python app) | 2026-04-11 | **format reference** — confirms offsets in current patch |
| `Hapfel1/er-save-manager` | n/a (no license shown but format confirmation only) | yes | n/a | recent | **format cross-check only** |
| `WarpZephyr/ac6sl2tool` | MIT | n/a (AC6, not ER) | n/a | 2024-02-10 | **future reference for DS3/AC6** if we expand |

**NuGet result:** no maintained permissive-licensed package exists for ER saves.
Every SoulsFormats fork is GPL-3, which is incompatible with the launcher's
distribution model (we'd have to ship source + offer relink rights for the
whole bundled exe).

## Decision

**Approach:** **Port ourselves**, using `BenGrn/EldenRingSaveCopier` (MIT) as the
primary C# reference and `ClayAmore/ER-Save-Editor` (Apache-2.0) as the
BND4-layout reference. **No third-party save library is added.**

**Rationale:** No permissive *library* exists — everything mature is GPL-3
(viral), unlicensed (all-rights-reserved), or Rust (wrong runtime). The two
strongest permissive references are MIT/Apache C# / Rust apps, not libraries,
so adoption means lifting code anyway. Since ER saves use only MD5 (no AES),
the implementation surface is tiny — a BND4 reader, 10 slot extractors, MD5
recompute on write. That's a few hundred lines of pure-core C#, fully testable
under `dotnet test`, with no DLL footprint and zero supply-chain exposure.
Porting also keeps the door open to extend to DS3/Sekiro/AC6 later (where the
AES key adds, but the surrounding code is reusable).

**Attribution plan:**

- In-app **About → Acknowledgements** panel: credit `BenGrn/EldenRingSaveCopier`
  (MIT) and `ClayAmore/ER-Save-Editor` (Apache-2.0) by name + repo URL + license.
- `THIRD_PARTY_NOTICES.md` at repo root (or alongside the save-editor module):
  full MIT + Apache-2.0 license texts with copyright notices preserved.
- The Apache-2.0 license requires a `NOTICE` mention if the source ships one —
  `ER-Save-Editor` does not, so a credit line suffices.
- Honor-the-builders law (CLAUDE.md) goes further than the licenses require:
  call out by author name in the in-app credit, not just the project.

## Format references used

- BND4 v4 layout — [`ClayAmore/ER-Save-Editor/src/util/bnd4.rs`](https://github.com/ClayAmore/ER-Save-Editor/blob/master/src/util/bnd4.rs)
- ER slot offsets — [`BenGrn/EldenRingSaveCopier/EldenRingSaveCopy/Saves/Model/SaveGame.cs`](https://github.com/BenGrn/EldenRingSaveCopier/blob/master/EldenRingSaveCopy/Saves/Model/SaveGame.cs)
- ER MD5 placement — [`alfizari/Elden-Ring-Save-Editor/src/Final.py`](https://github.com/alfizari/Elden-Ring-Save-Editor/blob/main/src/Final.py)
- ER format confirmation (no AES) — [`Hapfel1/er-save-manager/src/er_save_manager/parser/save.py`](https://github.com/Hapfel1/er-save-manager)
- DS3/AC6 family AES (for future scope) — [`WarpZephyr/ac6sl2tool/sl2decrypt/Program.cs`](https://github.com/WarpZephyr/ac6sl2tool/blob/master/sl2decrypt/Program.cs)

## Key bytes (for the implementer)

**Elden Ring saves do not need an AES key** — slot data is plaintext, MD5
checksummed. The implementer can skip AES code paths entirely for the MVP.

For future DS3 / Sekiro / AC6 scope, the **AC6 SL2 key** (publicly documented
across multiple MIT-licensed repos including `WarpZephyr/ac6sl2tool`) is:

```
B1 56 87 9F 13 48 97 98 70 05 C4 87 00 AE F8 79
```

DS3 and Sekiro keys are different; don't assume the same key works across the
older games. When that scope opens, re-research per-game.

The **ER regulation.bin** AES key (for game data, not saves) — also publicly
documented — is:

```
99 BF FC 36 6A 6B C8 C6 F5 82 7D 09 36 02 D6 76
C4 28 92 A0 1C 20 7F B0 24 D3 AF 4E 49 3F EF 99
```

This is irrelevant to the save editor but worth noting so it isn't confused
with the (non-existent) save key.

## BND4 layout notes (for the implementer)

### Header (offset 0x00, total 0x40 bytes)

| Offset | Size | Field | Notes |
|--------|------|-------|-------|
| 0x00 | 4 | magic | ASCII `"BND4"` = `0x42 0x4E 0x44 0x34` |
| 0x04 | 1 | unk04 | bool |
| 0x05 | 1 | unk05 | bool |
| 0x06 | 3 | padding | |
| 0x09 | 1 | big_endian | bool — ER PC is little-endian |
| 0x0A | 1 | bit_big_endian | bool |
| 0x0B | 1 | padding | |
| 0x0C | 4 | file_count | int32 |
| 0x10 | 8 | file_header_offset | int64, always `0x40` |
| 0x18 | 8 | version_string | 8-byte ASCII/Shift-JIS |
| 0x20 | 8 | file_header_size | int64 |
| 0x28 | 8 | unused | int64 |
| 0x30 | 1 | unicode | bool |
| 0x31 | 1 | format | uint8 (flag bits, bit-reversed if `bit_big_endian`) |
| 0x32 | 1 | extended | uint8 — observed values `0`, `1`, `4`, `0x80` |
| 0x33 | 13 | padding or hash table offset | depends on `extended` |

### Per-file entry (variable size by format flags)

Format flags decide whether 8-byte (Long) or 4-byte offsets, whether IDs are
present, whether names are stored. For ER saves, expect long offsets + names +
IDs:

| Offset | Size | Field |
|--------|------|-------|
| 0x00 | 1 | file_flags |
| 0x01 | 3 | padding |
| 0x04 | 4 | reserved (always -1) |
| 0x08 | 8 | compressed_size |
| 0x10 | 8 | uncompressed_size (or int32 if not long-offsets) |
| 0x18 | 8 | data_offset |
| +4 | 4 | file_id (if IDs flag) |
| +4 | 4 | name_offset (if Names flag) |

### ER `.sl2` overall layout (concrete constants)

| Section | Offset | Size | Purpose |
|---------|--------|------|---------|
| BND4 header | `0x000` | `0x300` | container header, ends with file table |
| Slot 0 MD5 | `0x300` | `0x10` | MD5 of slot 0 data |
| Slot 0 data | `0x310` | `0x280000` | character data (plaintext) |
| Slot 1 MD5 | `0x300 + 0x280010` | `0x10` | |
| Slot 1 data | `0x310 + 0x280010` | `0x280000` | |
| ... | each slot stride is `0x280010` | | |
| Slot 9 data ends at | `0x300 + 10 * 0x280010` | | |
| Save header MD5 | `0x1901D0E - 0x10` region around `0x019003A0..0x019003AF` | `0x10` | MD5 over the global save header |
| Save header | `0x1901D0E` | `0x24C` per slot | per-slot summary: name + level + playtime + active flag |
| Steam ID location | `0x19003B4` | `0x08` | for save-copying use case |

### Per-slot character header (offsets inside the save_header region)

| Field | Offset within header | Size |
|---|---|---|
| Character name (UTF-16) | `0x00` | `0x22` (17 chars + null) |
| Level | `0x22` | int16 / int32 — confirm in Task 3 fixture |
| Playtime (seconds) | `0x26` | int32 |
| Active flag (per-slot, `CHAR_ACTIVE_STATUS_START_INDEX`) | `0x1901D04` global | `0x0A` (10 bytes, one per slot) |

### Write flow

1. Patch slot data in place.
2. Recompute MD5 over the full `0x280000` slot bytes.
3. Write the new MD5 to the 16 bytes immediately preceding the slot data
   (i.e. `slot_offset - 0x10`).
4. If the save header changed (name/level/active flag): recompute the
   `SAVE_HEADERS_SECTION_START_INDEX` MD5 and write to its slot.
5. Atomic write — never modify the user's `.sl2` in place. Snapshot first
   (per the spec's snapshot-first safety law), then write to a temp file,
   then rename.

### What's deferred to later tasks

- Exact byte offsets for stats (vigor/mind/endurance/strength/dex/int/faith/arc)
  and rune count *inside* the slot body — Task 3 builds the fixture + decoder.
  The two strongest references (`BenGrn` for slot summary, `ClayAmore` for
  in-slot character data) both expose these; the implementer should diff a
  before/after `.sl2` from a controlled test character to lock the offsets.

## Status

Decision is final and unblocked. Tasks 1–4 can proceed against this porting
plan with the offsets and references above.
