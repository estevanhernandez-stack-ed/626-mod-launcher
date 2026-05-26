# Saves editor (FromSoft MVP) — Design Spec

**Date:** 2026-05-26
**Status:** Approved (Este, in-chat — full brainstorm, character-name edit added to MVP)
**Branch:** `feat/saves-editor-fromsoft-mvp`
**Memory:** [[saves-editor-fromsoft]] — captures the brainstorm + future cross-game ambition

## Why

Today's Saves dialog is functional but text-heavy and read-only: backup, snapshot, clone, auto-backup, save-mod install. The user can SEE save files but can't INSPECT or EDIT them.

Este's ask (2026-05-26 smoke of Cyberpunk's Saves dialog): *"The Saves interface needs a better interface. We can look at and think about some of the save editor interfaces that are out there because we eventually want to build some of those features in so that day one people have options to edit their saves and mod their saves."*

The launcher's differentiation lever is day-one feature reach. Save-editing is a power-user surface that established mod managers (Vortex, MO2) don't tackle. Doing it well — and doing it safely — is a real wedge.

## Goal

Build a working **save editor MVP** scoped to **one engine family (FromSoft)** with a **fool-proof safety rule (auto-snapshot before every edit)** as its foundation. Lay the architecture so additional fields (inventory, world flags) and additional FromSoft games (DS3, Sekiro, AC6) compose in cleanly later.

The MVP must:

1. **Read** ER save files end-to-end (BND4 archive, AES-128-CBC decryption, per-slot data parsing).
2. **Edit** a focused field set with confidence (stats, runes, character name).
3. **Re-sign and re-encrypt** the modified save with valid checksums so the game loads it cleanly.
4. **Snapshot before every edit, atomically.** No edit without a successful pre-snapshot. The user can restore in one click from the existing Snapshots list.

What it does NOT do (deliberately out of scope for MVP):

- Inventory editing (item picker UI + quest-item softlock risk → phase 2)
- World flag editing (region progress, quest state → phase 2 or 3)
- DS3 / Sekiro / AC6 support (same engine family, but ER is the MVP target; format adapter pattern lets us add them later)
- Steam ID rebinding (cross-account transfer — separate feature)
- Cross-game character transfer (the "killer" future feature — logged in memory, deferred)

## Approach

### Architecture

Three layers, bottom-up:

1. **Pure-core format layer** (`ModManager.Core.SaveEditor.FromSoft.*`) — no UI, fully unit-testable. Reads a `.sl2` path → returns a `CharacterSlot[]` model. Takes an edit + slot index → writes the file back with valid checksums + re-encryption. Lives next to existing pure cores so it composes with `Scanner` for snapshot integration.

2. **App-layer service** (`ModManager.App.Services.SaveEditorService`) — bridges the Core to the VM. Reads/writes via Core, calls `SaveManager.Backup` before any write, surfaces errors. The service owns the rule "edit ⇒ snapshot-then-write atomically."

3. **UI section** in the existing Saves dialog — a new "Characters" section above the existing Snapshots list, with a row per character (name/level/runes/class summary) and an Edit button per row.

### Foundational safety rule (locked, non-negotiable)

Every edit operation runs `SaveManager.Backup(saveDir, savesDir, label, auto: false)` BEFORE any save mutation. The label format:

```
before-edit: <action> on <character> — <yyyy-MM-dd HH:mm:ss>
```

e.g.: `before-edit: respec Yuka — 2026-05-26 14:32:41`

- `auto: false` — these snapshots are NOT subject to the KeepBox pruning quota. They're safety nets, not routine.
- Atomic — if the snapshot fails (disk full, permission denied), the edit aborts before any save-file write.
- The snapshot lands in the existing Snapshots list. The user sees it appear right after every edit, restorable in one click.

### Editable fields (MVP)

| Field | Type | Edit? | Validation |
|---|---|---|---|
| Character name | UTF-16LE string | **write** | Length ≤ 16 code units; non-empty after trim; no surrogate-only |
| Class / origin | string | read-only | n/a (set at character creation; out of MVP scope to change) |
| Level | int | read-only (computed) | recomputed from stat sum after edit; shown |
| Runes | uint32 | **write** | 0 – 999,999,999 |
| VIG / MND / END / STR / DEX / INT / FAI / ARC | uint8 | **write** | 1 – 99 per field |
| Steam ID | string | read-only | shown for awareness |

**Validation level: Light.** Per-field floor/ceiling, but no total-budget enforcement (you can bump every stat to 99 even if your level math doesn't add up). The save file stores derived stats (HP/FP/Stamina/equip-load) separately — those get recomputed on write from the new attributes using a known formula table.

### Format work (research-pending, criteria locked)

ER save format:
- **Container:** BND4 archive containing per-slot `USER_DATAxxx` blobs plus a `USER_DATA011` global slot.
- **Encryption:** AES-128-CBC with a known game-specific key, per-blob.
- **Checksum:** MD5 of the decrypted blob, prepended.
- **Per-character:** Each user-data slot holds a structured character record (stats, inventory, equip, etc.) plus a slot-summary block.

**Library strategy** (decision pending research):

1. **Preferred:** Use an existing permissive-license C# library (e.g. `SoulsFormatsNext` if it covers ER; community ports of `SoulsFormats` to .NET). License must be MIT/Apache/similar. Attribute in-app (Settings → About) and in the repo (NOTICE file).
2. **Fallback:** Port the AES-CBC + checksum logic from established Python implementations (e.g. `er-savefile-decryptor` lineage) into pure-core C#. The encryption layer is small (~200 lines).
3. **Honor-the-builders law applies:** Whoever did the reverse-engineering gets credit. NOTICE file, in-app attribution under the Characters section ("Save format support based on work by [name + link]").

### UI surface (existing Saves dialog grows)

The current Saves dialog already has:
- Save folder picker
- Manual backup + label
- Auto-backup toggle + retention
- Save files list (with clone-to menu)
- Save mods list
- Snapshots list

New section, inserted between **Save files** and **Save mods**:

```
Save snapshots
─────────────────────────────────────────────
Save folder: C:\Users\...   [Open folder] [Change…]
[ ] Auto-backup before launch    keep [25]

Save files
  ER0000.sl2        Slot 1     [Clone to…]

Characters                                          ← NEW
  Yuka              Lv 120  198,500 runes  Vagabond  [Edit]
  Tarnished         Lv  85   42,800 runes  Astrologer  [Edit]
  Test build        Lv  60   15,000 runes  Wretch     [Edit]
  Save format work by [Author] (link)               ← attribution

Installed save mods
  …

Snapshots
  before-edit: respec Yuka     just now           [Restore]
  2026-05-24 manual: before raid                  [Restore]
  …
```

Clicking [Edit] opens a per-character edit `ContentDialog`:

```
Edit character — Yuka (Lv 120 Vagabond)
─────────────────────────────────────
Name:    [Yuka                          ]
Runes:   [198500          ]

Attributes (level adjusts)
  VIG   [40 ]    MND   [16 ]
  END   [30 ]    STR   [50 ]
  DEX   [12 ]    INT   [12 ]
  FAI   [12 ]    ARC   [12 ]
  → Level 120 (recomputed)

⚠ This will create a snapshot before saving.
                       [Cancel]  [Save edit]
```

[Save edit] flow:
1. Run validation (per-field floor/ceiling, name length, non-empty)
2. Call `SaveManager.Backup(…, "before-edit: <action> — <ts>", auto: false)` → must succeed
3. Call `SaveEditorService.WriteEdit(savePath, slotIndex, edit)` → core mutation
4. Refresh Characters + Snapshots lists
5. Surface result in `StatusText`

Any failure between steps 2 and 4 leaves the snapshot in place — the user can restore.

### Honor the builders

Three places attribution surfaces:

1. **In the Saves dialog** — under the Characters section, one line: `Save format support by [Author] — [link]`.
2. **Settings → About** — full credit block listing the format library + version + license.
3. **Repo NOTICE file** — machine-readable credit with license text.

The phrase from the keystone applies directly: "Never monetize an author's work without permission. This is the product's spine, not a feature." Save-editor format work is exactly that author work.

## File structure

| File | Role |
|---|---|
| `src/ModManager.Core/SaveEditor/FromSoft/EldenRingSave.cs` | New: BND4 reader/writer, AES decrypt/encrypt, per-slot parser |
| `src/ModManager.Core/SaveEditor/FromSoft/CharacterSlot.cs` | New: model for one character (name, class, stats, runes, level) |
| `src/ModManager.Core/SaveEditor/FromSoft/CharacterEdit.cs` | New: edit struct (new name, new runes, new stats) — what gets applied |
| `src/ModManager.App/Services/SaveEditorService.cs` | New: app-layer wrapper that snapshot-first then writes |
| `src/ModManager.App/SavesDialog.xaml` | Modify: add Characters section between Save files and Save mods |
| `src/ModManager.App/SavesDialog.xaml.cs` | Modify: load characters, handle Edit click, route through SaveEditorService |
| `src/ModManager.App/CharacterEditDialog.xaml(.cs)` | New: per-character edit popover |
| `tests/ModManager.Tests/EldenRingSaveTests.cs` | New: read/write round-trip against a fixture save, stat-edit, name-edit, validation tests |
| `tests/fixtures/saves/ER0000_test.sl2` | New: a minimal fixture save for tests (a fresh character we'll generate / commit) |
| `NOTICE` (or extend existing) | New: format library + author credit |

## Tech stack

.NET 10, ModManager.Core (pure), ModManager.App (WinUI 3), xUnit. Dependency adds (subject to research):

- **One C# lib for the FromSoft save format**, or a small ported encryption helper (zero dep). Whatever lands must bundle cleanly into the portable build (per the deps policy: zero user-installed prerequisites).

No other new NuGets.

## Risk

**Moderate — higher than recent PRs.** The bricked-save scenario is real: a bad write that re-encrypts wrong, or a checksum that doesn't match, means the game refuses to load the save. The mitigation is the safety law — every edit snapshots first, restorable in one click — but the user could still get a `before-edit:` snapshot that ALSO has bad data if the read parse was wrong.

**Mitigations baked in:**

1. **Round-trip tests** against a fixture save: read → no-op edit → write → re-read → fields match. This catches parse-corruption.
2. **Validation guard** on every write: re-decrypt the just-written file, verify the edit landed, verify the rest of the slot is byte-identical to pre-edit (modulo the changed fields). If verification fails, the snapshot is the user's only line back — but at least we caught the corruption before they noticed.
3. **Conservative scope** — runes/stats/name. We're NOT touching inventory item arrays where one wrong index can softlock a character.
4. **Honor the existing safety culture** — `fs-atomic` writes, `SaveManager.Backup` integration, status-text surfacing of every error.

## Approval gates

- [x] Layer 1 — Pure-core format work (`EldenRingSave`, `CharacterSlot`, `CharacterEdit` + round-trip tests against a fixture)
- [x] Layer 2 — `SaveEditorService` with snapshot-first-then-write atomicity
- [x] Layer 3 — Saves dialog Characters section + per-character edit popover
- [x] Layer 4 — Honor-the-builders surfacing (in-dialog credit + NOTICE)

Future (logged in memory, NOT in this PR):

- Inventory edit (item picker, infusion levels, quest-item warnings)
- World flag edit (Sites of Grace, boss kills, area progress)
- DS3 / Sekiro / AC6 format adapters
- Cross-FromSoft-game character transfer (host-game-wins on conflicting fields)
- Steam ID rebinding
