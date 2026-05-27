# Saves editor (FromSoft phase 2 — ER inventory) — Design Spec

**Date:** 2026-05-26
**Status:** Drafted (sequel to MVP — Phase 2 of the FromSoft save-editor track)
**Branch (intended):** `feat/saves-editor-fromsoft-inventory`
**Memory:** [[saves-editor-fromsoft]] — the cross-iteration thread; this spec extends it
**Predecessors:**
- Spec: [`2026-05-26-saves-editor-fromsoft-mvp-design.md`](2026-05-26-saves-editor-fromsoft-mvp-design.md)
- Format research: [`docs/superpowers/research/2026-05-26-fromsoft-save-libs.md`](../research/2026-05-26-fromsoft-save-libs.md)
- Shipped PRs: #44 (Core + fixture + MD5), #45 (anchor walk + real-save round-trip), #46 (App-layer service + snapshot-first), #47 (CharacterEditDialog wiring + Saves dialog Characters section)

## Why

The MVP edits **identity** — name, level (via stats), runes. That's the introductory surface: instant gratification, low risk-per-edit, every test player can verify it landed by booting ER and looking at the character.

**Phase 2 = inventory.** It's the natural sequel because:

1. **It's what the user-facing save-editors out there actually do.** ClayAmore (360★), WSE-Project, BenGrn's tools — the headline feature is the item picker, not stat sliders. Every community save editor's home page screenshots an inventory grid. Day-one parity is what closes the "why use ours" gap.
2. **It's where the differentiation surface lives.** The launcher already has snapshot-first safety, agentic game profiles, save-mod auto-install, Nexus + CurseForge metadata. Layering a *safe* item editor on top of those — restore-in-one-click after a bad add, quest-item softlock warnings the others skip — is the kind of "Vortex / MO2 don't tackle this" lever we keep saying out loud.
3. **The bones are already there.** PR #45 landed the runtime GA-items walk. That walk is *the inventory's address book* — every item the player owns is already being read by `DiscoverMagicOffset`, we just throw the data away after using it to locate the anchor. Inventory editing is asking the walk to also surface what it saw.

Este's framing from the brainstorm memory: *"Inventory editing — phase 2."* This is that.

## Goal

Ship **Elden Ring inventory editing** with three operations and a real picker UI:

1. **Add** an item to the character's inventory (catalog → quantity → optional infusion / upgrade level for weapons).
2. **Remove** an item from the character's inventory (one row at a time).
3. **Modify** an existing inventory entry (quantity for consumables, infusion + +N for weapons).

It must:

1. **Read** the GA-items table + the held-items list end-to-end; produce a typed `InventoryEntry[]` per character.
2. **Match** each entry against the **item catalog** (a static JSON shipped in `ModManager.Core`) so the UI shows real names + categories + icons (text-only icons for v1 — image assets deferred).
3. **Write** the edit by patching the GA-items table + held-items array in place, recomputing the slot MD5 + save-header MD5 (already done by `WriteEdit`), atomic-write with post-write byte-mask verification.
4. **Snapshot first**, every time, via the existing `SaveEditorService.EditCharacter` wrapper — non-negotiable.
5. **Surface quest-item softlock risk** — a `⚠ quest-locked` badge on flagged items + a confirm-before-add modal.
6. **Honor the builders** — the catalog is sourced from community work; attribute in-app + NOTICE.

What it does NOT do (deferred to phase 3+):

- Equipment management (active weapon slots, talisman slots, ash-of-war binding to weapons). Inventory CONTAINS these items; equipping them onto the character's loadout is a separate set of fields.
- World flags (Sites of Grace lit, boss kills, area progress, region clears). Different byte region inside the slot, different risk class.
- Regulation.bin parameters (item properties themselves — damage, scaling, weight). Out of save-editor scope entirely; that's a mod-authoring surface.
- DS3 / Sekiro / AC6 inventory layouts. **Same engine family, but per-game-specific byte offsets and per-game catalogs.** Cross-game is deferred; the architecture below keeps the door open via the same format-adapter shape used in `EldenRingSave`.
- Multiplayer/Seamless-Co-op-only items (e.g., co-op-spawned consumables) — they read fine, they write fine, but the catalog flags them so the UI can show a `co-op` badge later. The flag exists; the badge is phase 3.

## Approach

### Architecture

Four layers, bottom-up — extends the MVP architecture rather than parallel-tracking it.

1. **Pure-core item catalog** (`ModManager.Core.SaveEditor.FromSoft.ItemCatalog.*`) — `ItemDefinition` record + a `ItemCatalog.LoadAll()` reader. Data lives as a JSON resource embedded into `ModManager.Core.dll` (no separate file to ship → no "did the catalog get copied?" failure mode). Pure-testable: load the catalog, assert known IDs are present, assert the quest-locked count matches expectation.

2. **Pure-core inventory format layer** (`ModManager.Core.SaveEditor.FromSoft.Inventory.*`) — extends `SlotData` / `EldenRingSave` with:
   - `Inventory.ReadEntries(slotBody)` — walks the GA-items table + the held-items array, returns `InventoryEntry[]`.
   - `Inventory.ApplyEdit(slotBody, InventoryEdit edit)` — applies one add/remove/modify in place, returns the new slot bytes (the caller wraps in `WriteEdit`'s atomic + verify flow).
   - `InventoryEdit` discriminated record (`Add`, `Remove`, `Modify`) — keeps the public surface small.

3. **App-layer service extension** (`ModManager.App.Services.SaveEditorService.EditInventory`) — reuses the same snapshot-first wrapper as `EditCharacter`. One method per edit type or one that takes the discriminated `InventoryEdit`; we go with the latter for symmetry with `CharacterEdit`.

4. **UI** — a new **Inventory tab** inside the existing `CharacterEditDialog` (not a separate dialog — keeps the edit context per-character). Search box + category filter + scrollable list + Add / Remove / Modify buttons. Quest-item softlock badge on the row + confirm modal before add.

### Item catalog acquisition

**Decision (locked):** **Lift from `ClayAmore/ER-Save-Editor` (Apache-2.0)** as the primary source, cross-checked against `alfizari/Elden-Ring-Save-Editor` (MIT).

Why ClayAmore:

- **Apache-2.0** — permissive, compatible with our distribution, attribution-required (which we'd do anyway under the honor-the-builders law).
- **Most-starred ER save editor (360★)** — most actively maintained, most thoroughly community-vetted.
- **Has a normalized item dump** — the Rust codebase carries the items as TOML / JSON tables already, not as a serialized binary regulation.bin walk. We re-serialize to our own JSON shape.
- **Categories already present** — weapons / armor / talismans / spells / ashes-of-war / consumables / key-items / crafting materials are already grouped in ClayAmore's data. We keep the grouping; the UI's filter pivots on it.

Why we don't go alfizari-only: alfizari's catalog is in Python source files (literal dict literals embedded in `.py`), harder to mechanically re-serialize, but it IS the authoritative source for **ER 1.13+ specific changes** so we use it to **diff** ClayAmore's catalog against the current patch and flag any items that moved/changed.

Why we don't generate from regulation.bin: it's the most authoritative source, but it requires the ER regulation.bin AES key (publicly documented but politically charged) and parsing a 30+ MB binary param file. The shipped community catalogs already encode the same data the param file would; lifting + crediting is the lower-risk path.

**Catalog file shape** (one JSON file, embedded resource):

```json
{
  "version": "2026-05-26",
  "source": "ClayAmore/ER-Save-Editor (Apache-2.0) + alfizari diff for ER 1.13+",
  "items": [
    {
      "id": 1040000,
      "name": "Rune Arc",
      "category": "consumable",
      "maxQuantity": 99,
      "questLocked": false,
      "infusable": false,
      "upgradeable": false
    },
    {
      "id": 2030000,
      "name": "Uchigatana",
      "category": "weapon",
      "maxQuantity": 1,
      "questLocked": false,
      "infusable": true,
      "upgradeable": true,
      "maxUpgrade": 25
    },
    {
      "id": 8050000,
      "name": "Fia's Mist",
      "category": "key-item",
      "maxQuantity": 1,
      "questLocked": true,
      "questNotes": "Fia's questline — required to progress to Deathbed Smalls."
    }
  ]
}
```

The catalog ships as **embedded resource** (`<EmbeddedResource Include="SaveEditor\FromSoft\ItemCatalog\items.json" />` in `ModManager.Core.csproj`). One file. No copy-to-output ceremony. Load via `Assembly.GetManifestResourceStream` and parse via `System.Text.Json`. Approx size: 1500 items × ~120 bytes each → ~180 KB embedded. Negligible.

### Inventory format research (locked by reference, validated in Task 0)

The inventory in an ER slot lives in two places:

1. **GA-items table** — already walked by `DiscoverMagicOffset` (slot offset 0x20, 5120 entries, variable size by handle type). This is the **ID database** — each entry maps a "ga_item_handle" (the in-save reference key) to an item ID + the weapon's infusion + upgrade level for weapon entries.
2. **Held-items array** — a separate table further inside the slot body. Each entry is `{ ga_item_handle (u32), quantity (u32), inventory_slot_index (u32), flags (u32) }`. The held-items array's offset is **relative to the magic anchor**, just like stats and runes are. ClayAmore + alfizari both surface it; the exact relative offset is locked in Task 0.

To **add** an item:
1. Find a free GA-items slot (handle = 0). Write the new handle + ID + (for weapons) infusion + upgrade level.
2. Find a free held-items slot. Write the GA-item handle reference + quantity + inventory-slot-index + flags.
3. Recompute MD5s, atomic-write, verify post-write byte mask.

To **remove** an item:
1. Find the held-items entry matching the row the user picked (by GA-item handle).
2. Zero it out (set handle to 0, quantity to 0, slot index to -1, flags to 0).
3. **Don't touch the GA-items entry** — multiple held-items entries can reference one GA-item handle (e.g. weapon + ash-of-war combo); we let the GA-items garbage stay until a future write reuses the slot. Conservative; matches alfizari's behavior.
4. MD5s, atomic-write, verify.

To **modify** an item:
1. For consumables: patch the held-items entry's quantity in place.
2. For weapons (infusion / upgrade): patch the GA-items entry's infusion + upgrade fields in place.
3. MD5s, atomic-write, verify.

The **post-write byte-mask verification** (the bricked-save safety net from `EldenRingSave.VerifyPostWrite`) extends to cover the new edited ranges:

- For an Add: GA-items entry at the chosen index + held-items entry at the chosen index.
- For a Remove: held-items entry at the freed index.
- For a Modify: held-items quantity field OR GA-items infusion/upgrade fields.

Anything OUTSIDE those ranges must be byte-identical to the pre-edit slot. The verify routine becomes "compute the touched mask for this edit type, byte-compare everything else." The MVP's pattern generalizes cleanly.

### UI shape

The existing `CharacterEditDialog` becomes tabbed (a `Pivot` or `NavigationView`):

```
┌─ Edit character — Yuka (Lv 120 Vagabond) ───────────────────┐
│  [ Identity ]  [ Inventory ]                                 │
│                                                              │
│  Inventory (1,247 items in the catalog)                      │
│  ┌──────────────────────────┐  ┌─────────────────────────┐   │
│  │ [search…]                │  │ Held inventory          │   │
│  │ Category: [All ▾]        │  │ ─────────────────────── │   │
│  │ ─────────────────────── │  │ • Uchigatana +12 (Keen)  │   │
│  │ • Uchigatana             │  │ • Rune Arc × 12         │   │
│  │   ⚠ quest-locked         │  │ • Erdtree Greatshield   │   │
│  │ • Rune Arc               │  │ • Hefty Smithing Stone  │   │
│  │ • Smithing Stone (1)     │  │   × 8                   │   │
│  │ • Fia's Mist             │  │ • Fia's Mist  ⚠         │   │
│  │   ⚠ quest-locked         │  │ ─────────────────────── │   │
│  │ • …                      │  │ [Modify] [Remove]       │   │
│  │ [Add →]                  │  │                         │   │
│  └──────────────────────────┘  └─────────────────────────┘   │
│                                                              │
│  ⚠ Quest-locked items can soft-lock your character. Read     │
│     each item's notes before adding. Snapshot-on-edit means  │
│     you can restore — but only if you snapshot the right     │
│     character state first.                                   │
│                                                              │
│  Save format support + item catalog by:                      │
│  ClayAmore/ER-Save-Editor (Apache-2.0)                      │
│  alfizari/Elden-Ring-Save-Editor (MIT)                       │
│                                                              │
│                          [ Cancel ]  [ Save edit ]           │
└──────────────────────────────────────────────────────────────┘
```

The dialog stays one `ContentDialog` (no multi-window juggling). Both tabs commit through one `Save edit` — the edit struct carries both `CharacterEdit` (identity) and `InventoryEdit[]` (a list, so the user can queue multiple inventory ops in one click and the snapshot covers them all atomically).

### Quest-item softlock warnings

"Quest-locked" means: **adding this item to a character at the wrong story progress can break the questline that would normally award it.** Examples:

- **Fia's Mist** — required at a specific point in Fia's questline. Adding it before triggers the gate to skip; adding after it was supposed to be consumed creates a duplicate that confuses the next quest step.
- **Volcano Manor invitation letters** — adding out-of-order skips Tanith's recruitment dialog.
- **Great Runes** — adding a Great Rune you haven't beaten the matching demigod for can flag the boss as dead and skip the cinematic.
- **Memory Stones** — non-progression but limited-count; adding extras stacks past the intended cap.

The catalog's `questLocked: true` flag marks ~80–120 items (rough estimate, locked by Task 0). Each gets a `questNotes` string the UI surfaces.

**UX flow:**

1. Each quest-locked item in the picker shows a `⚠ quest-locked` badge inline.
2. Clicking `Add` on a quest-locked item triggers a **confirm modal**:
   ```
   ┌─ Add quest-locked item: Fia's Mist ─────────────────────┐
   │                                                          │
   │  Quest notes:                                            │
   │  Fia's questline — required to progress to Deathbed     │
   │  Smalls. Adding this out-of-order can soft-lock the      │
   │  Fia / D / Lichdragon questline branch.                  │
   │                                                          │
   │  Quest-locked items are flagged because they can break   │
   │  questlines. A snapshot will be taken before any edit.   │
   │                                                          │
   │              [ Cancel ]  [ I understand — add anyway ]   │
   └──────────────────────────────────────────────────────────┘
   ```
3. Only after explicit confirm does the item land in the staged inventory edits.

**Source of quest-locked flags:** ClayAmore's catalog tags some items with quest references; the **canonical reference** is the [Elden Ring wiki](https://eldenring.wiki.fextralife.com/) questline tracker pages, which Phase 2's Task 0 will use to **diff against ClayAmore's tags** and produce the final flag list. The flag list goes in our JSON catalog under `questLocked: bool` + `questNotes: string` — we don't link out at runtime; the notes ship in the catalog so the dialog works offline.

Rough quest-item count (from the wiki + ClayAmore cross-reference):

- ~30 Fia / Ranni / Goldmask / Yura / D / Volcano Manor / Hyetta questline progression items
- ~10 Great Runes + Memory Stones + Stonesword Keys at canonical limits
- ~20 NPC summon ashes that gate dialog progression
- ~20 NPC bell-bearings that gate Roundtable shop unlocks
- ~10 letters / scrolls / cookbooks that gate dialog branches
- **Estimated total: 90 ± 20 items flagged `questLocked: true`**

Task 0 outputs the exact count.

### Save format compatibility — Seamless Co-op (`.co2`)

The MVP already handles both `.sl2` (vanilla) and `.co2` (Seamless Co-op). **The inventory layout is byte-identical between the two** — Seamless Co-op uses the same anchor + same GA-items + same held-items structure, just with a different file extension and a slightly modified save-header for the SCO networking metadata. The MVP's `EldenRingSave` reader doesn't case on extension and Phase 2 inherits that — no code branch needed.

The Phase 2 test fixture extends the MVP's `EldenRingFixture` to plant inventory entries; same builder, additional inventory-planting methods.

### Honor the builders (extended)

Three surfaces, MVP carries; Phase 2 adds one more line per:

1. **In-dialog credit (Inventory tab footer):**
   ```
   Item catalog by:
     ClayAmore/ER-Save-Editor (Apache-2.0)
     alfizari/Elden-Ring-Save-Editor (MIT) — ER 1.13+ diff
   Save format support: see Identity tab.
   ```
2. **Settings → About — Acknowledgements panel:** add the two repos with name + URL + license.
3. **Repo `THIRD_PARTY_NOTICES.md`** (already exists per MVP Task 0): append the Apache-2.0 full license + ClayAmore copyright.

The phrase from CLAUDE.md applies directly: *"Never monetize an author's work without permission."* The item catalog is exactly that author work — community reverse-engineering, painstakingly maintained. We credit by name, by repo, by license. No exceptions.

## Editable surface (Phase 2)

| Edit type | Operates on | Validation | Notes |
|---|---|---|---|
| **Add item** | new GA-items slot + new held-items slot | item ID ∈ catalog; quantity ∈ [1, maxQuantity]; weapon → upgrade ∈ [0, maxUpgrade]; weapon → infusion ∈ valid-set | Quest-locked items require confirm modal |
| **Remove item** | held-items slot zeroed | nothing — removal is always safe (the worst case is the user removes a quest item, which the snapshot recovers) | Doesn't touch GA-items entry (conservative — see Approach) |
| **Modify item** | held-items quantity OR GA-items infusion+upgrade | same as Add | "Modify" for a weapon's infusion is the canonical use case (re-roll a weapon's Keen → Heavy) |

What's NOT editable (deferred):

- **Equipped slot** — which weapon is in active slot 1 vs 2 vs 3. Different byte region (the "loadout" array near the slot's start). Phase 3.
- **Bulk operations** — "give me 99 of every consumable" / "max upgrade every weapon I own." Power-user moves; the picker UI doesn't preclude them but Phase 2 ships single-row edits only. Phase 3.
- **Custom item IDs** — typing a raw item ID not in the catalog. The catalog is the safety rail. Power users can fork; we don't ship a "type a number, hope it loads" affordance.

## File structure

| File | Role |
|---|---|
| `src/ModManager.Core/SaveEditor/FromSoft/ItemCatalog/items.json` | New — embedded resource, 1500-ish items |
| `src/ModManager.Core/SaveEditor/FromSoft/ItemCatalog/ItemDefinition.cs` | New — pure record |
| `src/ModManager.Core/SaveEditor/FromSoft/ItemCatalog/ItemCatalog.cs` | New — `LoadAll()` from embedded resource |
| `src/ModManager.Core/SaveEditor/FromSoft/Inventory/InventoryEntry.cs` | New — pure record, one held-items row |
| `src/ModManager.Core/SaveEditor/FromSoft/Inventory/InventoryEdit.cs` | New — discriminated record (`Add` / `Remove` / `Modify`) |
| `src/ModManager.Core/SaveEditor/FromSoft/Inventory/Inventory.cs` | New — `ReadEntries(slotBody)` + `ApplyEdit(slotBody, edit)` |
| `src/ModManager.Core/SaveEditor/FromSoft/EldenRingSave.cs` | Modify — add `WriteInventoryEdit(savePath, slotIndex, InventoryEdit edit)` + extend `VerifyPostWrite` mask |
| `src/ModManager.App/Services/SaveEditorService.cs` | Modify — add `EditInventory(saveDir, snapshotsDir, savePath, slotIndex, CharacterSlot beforeEdit, InventoryEdit edit)` |
| `src/ModManager.App/CharacterEditDialog.xaml(.cs)` | Modify — wrap content in a `Pivot`/`NavigationView`, add Inventory tab |
| `src/ModManager.App/InventoryPicker.xaml(.cs)` | New — the picker UserControl embedded in the dialog's Inventory tab |
| `src/ModManager.App/QuestLockConfirmDialog.xaml(.cs)` | New — the confirm modal |
| `tests/ModManager.Tests/SaveEditor/FromSoft/ItemCatalogTests.cs` | New — embedded-resource load + known-IDs assertions + quest-locked count |
| `tests/ModManager.Tests/SaveEditor/FromSoft/InventoryTests.cs` | New — read-entries fixture + round-trip add/remove/modify |
| `tests/ModManager.Tests/SaveEditor/FromSoft/EldenRingFixture.cs` | Modify — add inventory-planting builder methods |
| `THIRD_PARTY_NOTICES.md` | Modify — append ClayAmore + alfizari licenses |
| `docs/superpowers/research/2026-05-26-inventory-format.md` | New (Task 0 output) — pins the held-items relative offset + quest-locked count |

## Tech stack

.NET 10, ModManager.Core (pure), ModManager.App (WinUI 3), xUnit. **No new NuGet dependencies.** The catalog is a JSON resource parsed via `System.Text.Json` (already in the BCL).

## Risk

**Medium** — higher than MVP's identity edits, lower than the "edit world flags / regulation params" cliff.

Why higher:

- Inventory is a bigger touched surface. Adding an item touches 2 places (GA-items + held-items); a wrong write at the wrong index can scramble the inventory array in a way the game won't load.
- Quest-item softlocks are a **content-layer** failure mode our byte-mask verification can't catch. The bytes can be perfect, the file can load, and the questline is still broken. The mitigation is the warning UI + the snapshot — not byte verification.

Why not higher than that:

- Same atomic-write + post-write byte-mask verify as MVP, extended in scope.
- Same snapshot-first law as MVP, identical wrapper service.
- The catalog is the safety rail — no "type a raw ID, hope it works." Every editable ID is from a vetted, attributed catalog.

**Mitigations baked in:**

1. **Byte-mask verification** extends to cover the new edited ranges per edit type. Anything outside the mask must match the pre-edit slot, byte-identical.
2. **Round-trip tests** against the inventory fixture: read entries → no-op edit (Add then Remove the same item) → write → re-read → all original entries match.
3. **Quest-locked flag + confirm modal** — content-layer protection. The user can still nuke their questline, but they did it on purpose.
4. **Snapshot covers the entire dialog session** — both Identity and Inventory edits commit through one `Save edit` click; one snapshot, all edits, atomic at the file level. Restore-in-one-click works for the whole session.
5. **Conservative remove** — doesn't touch GA-items entries, just zeros held-items rows. Garbage accumulates; that's a known cost for not corrupting cross-references.

## Approval gates

- [ ] Layer 1 — Item catalog pulled from ClayAmore + alfizari, normalized to our JSON schema, embedded as resource, quest-locked flag list locked
- [ ] Layer 2 — Pure-core inventory read + edit (`Inventory.cs`) with round-trip tests against the extended fixture
- [ ] Layer 3 — `EldenRingSave.WriteInventoryEdit` + extended `VerifyPostWrite` byte-mask
- [ ] Layer 4 — `SaveEditorService.EditInventory` (snapshot-first wrapper, mirrors `EditCharacter`)
- [ ] Layer 5 — `CharacterEditDialog` tabbed with Inventory tab + `InventoryPicker` UserControl
- [ ] Layer 6 — `QuestLockConfirmDialog` + quest-locked badge in the picker
- [ ] Layer 7 — Attribution surfaced (in-dialog footer + Settings → About + `THIRD_PARTY_NOTICES.md`)
- [ ] Smoke — real save round-trip on Este's box: add a Rune Arc → quantity 12, infuse Uchigatana → Keen +12, remove a Smithing Stone, save, boot ER, verify in-game

Future (logged in memory, NOT in this PR):

- Equip-slot editing (active weapon / talisman slots)
- Bulk operations ("max every weapon I own")
- DS3 / Sekiro / AC6 inventory adapters (per-game catalogs + per-game offsets)
- World flag editing (Sites of Grace, boss kills, region progress)
- Cross-FromSoft-game character transfer (the long-tail killer feature in [[saves-editor-fromsoft]])

## Cross-game disclaimer

**This is an Elden Ring-only iteration.** DS3, Sekiro, and AC6 share the BND4 container shape and the slot-stride concept, but:

- DS3 / Sekiro / AC6 SL2 saves use AES-128-CBC on each slot (the MVP's research note documents the AC6 key).
- The held-items array's *relative offset to the slot's magic anchor* differs per game.
- Item ID ranges differ per game; each game needs its own catalog.

Cross-game inventory is a phase 3+ ask. The format-adapter architecture (`EldenRingSave` as a sealed class; future `DarkSouls3Save`, `SekiroSave`, etc.) keeps the door open without forcing the work now.

## References

- Predecessor spec: [`2026-05-26-saves-editor-fromsoft-mvp-design.md`](2026-05-26-saves-editor-fromsoft-mvp-design.md)
- Format research: [`docs/superpowers/research/2026-05-26-fromsoft-save-libs.md`](../research/2026-05-26-fromsoft-save-libs.md)
- ClayAmore/ER-Save-Editor (Apache-2.0): https://github.com/ClayAmore/ER-Save-Editor
- alfizari/Elden-Ring-Save-Editor (MIT): https://github.com/alfizari/Elden-Ring-Save-Editor
- BenGrn/EldenRingSaveCopier (MIT): https://github.com/BenGrn/EldenRingSaveCopier
- Elden Ring wiki — questline trackers (quest-locked flag source): https://eldenring.wiki.fextralife.com/
