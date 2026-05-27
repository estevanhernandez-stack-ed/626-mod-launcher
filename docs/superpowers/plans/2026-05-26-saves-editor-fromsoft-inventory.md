# FromSoft Save Editor — Phase 2 (ER Inventory) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax. Test command: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (NEVER bare `dotnet test` — hangs building WinUI). Build (App): `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. Kill `ModManager.App.exe` first if the build complains about a locked Core DLL.

**Goal:** Ship Elden Ring inventory editing (add / remove / modify) inside the existing `CharacterEditDialog`, with item picker UI, quest-locked softlock warnings, snapshot-first safety, and a community-sourced item catalog (~1500 items).

**Architecture:** Extends the MVP. Pure-core item catalog (embedded JSON resource) + pure-core inventory format layer (`Inventory.cs`) compose on top of the existing `EldenRingSave` + GA-items walk. App-layer reuses `SaveEditorService.EditCharacter`'s snapshot-first wrapper for symmetry. UI extends `CharacterEditDialog` with a tabbed surface (`Identity` + `Inventory`).

**Tech Stack:** .NET 10, ModManager.Core (pure), ModManager.App (WinUI 3), xUnit. No new NuGets — `System.Text.Json` (BCL) parses the catalog.

**Spec:** [`docs/superpowers/specs/2026-05-26-saves-editor-fromsoft-inventory-design.md`](../specs/2026-05-26-saves-editor-fromsoft-inventory-design.md)

**Predecessor work shipped:** PRs #44 (Core + fixture + MD5), #45 (anchor walk + real-save round-trip), #46 (App-layer snapshot-first), #47 (CharacterEditDialog wiring).

---

## Task 0: Inventory format + catalog research

**Files:**
- Create: `docs/superpowers/research/2026-05-26-inventory-format.md`

Lock the two facts the implementation rests on:

1. **The held-items array's offset relative to the slot's magic anchor.** Pull from ClayAmore + alfizari. Cross-check. Pin the exact relative offset (one number, like `RunesRelative = -331`) + the per-entry layout (handle u32 + quantity u32 + slot-index u32 + flags u32 = 16 bytes/entry, confirm).
2. **The quest-locked item list.** ClayAmore's catalog tags some items; the Elden Ring wiki's questline trackers are the gold reference. Diff the two, lock the count + the canonical list.

This is non-negotiable Task 0 because guessing inventory layout is how you brick a real save. PR #44's Task 0 set the precedent — same shape here.

- [ ] **Step 1: Pull ClayAmore's inventory references**

Clone or browse:
- `https://github.com/ClayAmore/ER-Save-Editor/tree/master/src/save/common/save_slot.rs` — the `PlayerGameData` struct
- `https://github.com/ClayAmore/ER-Save-Editor/tree/master/src/save/common/equip_inventory_data.rs` — the held-items reader
- `https://github.com/ClayAmore/ER-Save-Editor/tree/master/data/` — the items table dumps (TOML/JSON)

Record:
- The held-items array's offset relative to the magic anchor
- The per-entry layout (sizes + field order)
- The item table format (one row per item: ID, name, category)
- The license file location

- [ ] **Step 2: Cross-check against alfizari**

Browse `https://github.com/alfizari/Elden-Ring-Save-Editor/blob/main/src/Final.py`:
- Find the held-items read path (search for "held_items", "inventory_array", or similar)
- Confirm the relative offset matches ClayAmore's number (or document the diff)
- alfizari is ER 1.13+ authoritative — if there's a delta, alfizari wins for the offset

Record any discrepancy + the resolution (which one's right for ER 1.13+).

- [ ] **Step 3: Pull the quest-locked item list**

Cross-reference:
- ClayAmore's `quest_items` tagged entries (if present)
- Elden Ring wiki questline trackers — Fia, Ranni, Goldmask, Yura, D, Volcano Manor, Hyetta, Hewg, Boc, Roderika, Diallos, Nepheli, Kenneth, Gostoc, Patches, Bernahl
- Great Runes + Memory Stones + Stonesword Keys + NPC summon ashes + NPC bell-bearings

For each quest-locked item record: ID, name, questline, brief notes (one sentence: what happens if added at wrong time).

Target count: 90 ± 20 items. If the number lands outside that range, surface in the research doc with a note explaining why.

- [ ] **Step 4: Write the decision document**

Create `docs/superpowers/research/2026-05-26-inventory-format.md` with:

```markdown
# Inventory format + item catalog research — 2026-05-26

## Held-items array layout (locked)

**Relative offset from magic anchor:** (one number)
**Entry stride:** 16 bytes
**Entry layout:**
| Offset | Size | Field | Notes |
|--------|------|-------|-------|
| 0x00 | 4 | ga_item_handle (u32) | references GA-items table; 0 = empty slot |
| 0x04 | 4 | item_id (u32) | redundant copy of ID; ClayAmore relies on this for category lookup |
| 0x08 | 4 | quantity (u32) | 0 when slot is empty |
| 0x0C | 4 | inventory_slot_index (i32) | -1 when empty |

**Entry count:** (locked — usually 5120 like GA-items, confirm)

## GA-items extended layout (for weapons — already partly known)

(Pull the weapon-entry infusion + upgrade field offsets from ClayAmore's `equip_inventory_data.rs`. SlotData.cs only documents sizes today — Phase 2 needs the within-entry field map.)

| Offset within weapon entry | Size | Field |
|----|----|----|
| 0x00 | 4 | ga_item_handle |
| 0x04 | 4 | item_id |
| 0x08 | 4 | upgrade_level (uint, 0-25) |
| 0x0C | 4 | infusion_id (uint, e.g. 100=Standard, 600=Keen, ...) |
| 0x10 | 4 | unused-or-flags |
| (0x14 | 1 | trailing byte — 21-byte total per SlotData.cs) |

## Quest-locked items (locked)

**Final count:** (N) items flagged `questLocked: true`
**Source:** (ClayAmore tags + wiki cross-reference, with the wiki winning conflicts)

(Full list in CSV form below this header — ~90 rows expected.)

## Item catalog format (locked)

JSON shape per spec. ~1500 items total. Source: ClayAmore primary, alfizari diff for ER 1.13+ deltas.

## Decision

Approach is locked. Tasks 1-3 can proceed against these constants.
```

- [ ] **Step 5: Commit the research document**

```bash
git add docs/superpowers/research/2026-05-26-inventory-format.md
git commit -m "research: ER inventory layout + quest-locked item list"
```

---

## Task 1: Core — `ItemDefinition` record

**Files:**
- Create: `src/ModManager.Core/SaveEditor/FromSoft/ItemCatalog/ItemDefinition.cs`
- Create: `tests/ModManager.Tests/SaveEditor/FromSoft/ItemDefinitionTests.cs`

Pure data shape. No I/O. Tested for record-property semantics.

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core.SaveEditor.FromSoft.ItemCatalog;

namespace ModManager.Tests.SaveEditor.FromSoft;

public class ItemDefinitionTests
{
    [Fact]
    public void ItemDefinition_carries_identity_category_and_quest_flag()
    {
        var item = new ItemDefinition(
            Id: 1040000,
            Name: "Rune Arc",
            Category: "consumable",
            MaxQuantity: 99,
            QuestLocked: false,
            QuestNotes: null,
            Infusable: false,
            Upgradeable: false,
            MaxUpgrade: 0);

        Assert.Equal(1040000u, item.Id);
        Assert.Equal("Rune Arc", item.Name);
        Assert.Equal("consumable", item.Category);
        Assert.Equal(99, item.MaxQuantity);
        Assert.False(item.QuestLocked);
    }

    [Fact]
    public void ItemDefinition_carries_quest_notes_when_quest_locked()
    {
        var item = new ItemDefinition(
            Id: 8050000,
            Name: "Fia's Mist",
            Category: "key-item",
            MaxQuantity: 1,
            QuestLocked: true,
            QuestNotes: "Required to progress to Deathbed Smalls.",
            Infusable: false,
            Upgradeable: false,
            MaxUpgrade: 0);

        Assert.True(item.QuestLocked);
        Assert.NotNull(item.QuestNotes);
    }
}
```

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ItemDefinitionTests"`
Expected: compilation failure (type doesn't exist).

- [ ] **Step 2: Implement `ItemDefinition`**

Create `src/ModManager.Core/SaveEditor/FromSoft/ItemCatalog/ItemDefinition.cs`:

```csharp
namespace ModManager.Core.SaveEditor.FromSoft.ItemCatalog;

/// <summary>One row from the ER item catalog. The catalog ships embedded in
/// ModManager.Core.dll as a JSON resource — see <see cref="ItemCatalog"/>. The
/// catalog is sourced from ClayAmore/ER-Save-Editor (Apache-2.0) and
/// alfizari/Elden-Ring-Save-Editor (MIT); attribution surfaces in the UI footer
/// and in THIRD_PARTY_NOTICES.md.
///
/// <see cref="QuestLocked"/> flags items that can soft-lock a character if added
/// at the wrong story progress. The UI shows a ⚠ badge + a confirm modal before
/// any quest-locked Add lands in the staged edit list.</summary>
public sealed record ItemDefinition(
    uint Id,
    string Name,
    string Category,
    int MaxQuantity,
    bool QuestLocked,
    string? QuestNotes,
    bool Infusable,
    bool Upgradeable,
    int MaxUpgrade);
```

- [ ] **Step 3: Run the test**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ItemDefinitionTests"
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.Core/SaveEditor/FromSoft/ItemCatalog/ItemDefinition.cs \
        tests/ModManager.Tests/SaveEditor/FromSoft/ItemDefinitionTests.cs
git commit -m "core: ItemDefinition record for ER item catalog"
```

---

## Task 2: Core — `ItemCatalog` loader + embedded JSON resource

**Files:**
- Create: `src/ModManager.Core/SaveEditor/FromSoft/ItemCatalog/items.json` (the ~1500-row catalog, derived from Task 0's sources)
- Create: `src/ModManager.Core/SaveEditor/FromSoft/ItemCatalog/ItemCatalog.cs`
- Modify: `src/ModManager.Core/ModManager.Core.csproj` (embed the JSON)
- Create: `tests/ModManager.Tests/SaveEditor/FromSoft/ItemCatalogTests.cs`

The JSON ships embedded so there's no separate file to copy at build time. Test asserts known IDs + quest-locked count.

- [ ] **Step 1: Generate the JSON catalog from Task 0's research**

Take ClayAmore's items table + alfizari's ER 1.13+ deltas + the wiki's quest-lock flags and serialize to the schema documented in the spec. Output: `src/ModManager.Core/SaveEditor/FromSoft/ItemCatalog/items.json`.

Shape:

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
      "upgradeable": false,
      "maxUpgrade": 0
    },
    ...
  ]
}
```

Sanity-check size: ~1500 items × ~150 bytes = ~225 KB. Reasonable embedded payload.

- [ ] **Step 2: Embed the JSON via csproj**

Modify `src/ModManager.Core/ModManager.Core.csproj`, add inside the existing `<ItemGroup>` (or create one):

```xml
<ItemGroup>
  <EmbeddedResource Include="SaveEditor\FromSoft\ItemCatalog\items.json" />
</ItemGroup>
```

- [ ] **Step 3: Write the failing tests**

Create `tests/ModManager.Tests/SaveEditor/FromSoft/ItemCatalogTests.cs`:

```csharp
using ModManager.Core.SaveEditor.FromSoft.ItemCatalog;

namespace ModManager.Tests.SaveEditor.FromSoft;

public class ItemCatalogTests
{
    [Fact]
    public void LoadAll_returns_a_nonempty_catalog()
    {
        var items = ItemCatalog.LoadAll();
        Assert.NotEmpty(items);
        Assert.True(items.Count >= 1000, $"Expected at least 1000 items; got {items.Count}.");
    }

    [Fact]
    public void LoadAll_contains_rune_arc()
    {
        var items = ItemCatalog.LoadAll();
        var runeArc = items.FirstOrDefault(i => i.Name == "Rune Arc");
        Assert.NotNull(runeArc);
        Assert.Equal("consumable", runeArc.Category);
        Assert.Equal(99, runeArc.MaxQuantity);
    }

    [Fact]
    public void LoadAll_contains_quest_locked_items_with_notes()
    {
        var items = ItemCatalog.LoadAll();
        var questLocked = items.Where(i => i.QuestLocked).ToList();

        // Spec rough estimate: 90 ± 20. Allow generous bounds; tightens after Task 0.
        Assert.InRange(questLocked.Count, 60, 130);

        // Every quest-locked item MUST carry notes — the confirm modal reads them.
        Assert.All(questLocked, i => Assert.False(string.IsNullOrWhiteSpace(i.QuestNotes)));
    }

    [Fact]
    public void LoadAll_weapon_items_carry_upgrade_metadata()
    {
        var items = ItemCatalog.LoadAll();
        var weapons = items.Where(i => i.Category == "weapon").ToList();
        Assert.NotEmpty(weapons);

        // Every weapon should be upgradeable + report a MaxUpgrade in [0, 25].
        Assert.All(weapons, w =>
        {
            Assert.True(w.Upgradeable);
            Assert.InRange(w.MaxUpgrade, 0, 25);
        });
    }

    [Fact]
    public void LoadAll_returns_same_collection_on_repeat_calls()
    {
        // The implementation may cache; both runs must return the same data.
        var first = ItemCatalog.LoadAll();
        var second = ItemCatalog.LoadAll();
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first.First().Id, second.First().Id);
    }
}
```

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ItemCatalogTests"`
Expected: compilation failure (`ItemCatalog` doesn't exist).

- [ ] **Step 4: Implement `ItemCatalog`**

Create `src/ModManager.Core/SaveEditor/FromSoft/ItemCatalog/ItemCatalog.cs`:

```csharp
using System.Reflection;
using System.Text.Json;

namespace ModManager.Core.SaveEditor.FromSoft.ItemCatalog;

/// <summary>Loads the embedded ER item catalog. The JSON resource ships with
/// ModManager.Core.dll (see ModManager.Core.csproj <c>&lt;EmbeddedResource&gt;</c>).
/// The catalog is sourced from ClayAmore/ER-Save-Editor (Apache-2.0) and
/// alfizari/Elden-Ring-Save-Editor (MIT) — see THIRD_PARTY_NOTICES.md.
///
/// The first call parses the JSON; subsequent calls return the cached list.</summary>
public static class ItemCatalog
{
    private const string ResourceName = "ModManager.Core.SaveEditor.FromSoft.ItemCatalog.items.json";

    private static readonly Lazy<IReadOnlyList<ItemDefinition>> _items = new(LoadFromResource);

    /// <summary>Return every item in the catalog. Cached after the first call.</summary>
    public static IReadOnlyList<ItemDefinition> LoadAll() => _items.Value;

    private static IReadOnlyList<ItemDefinition> LoadFromResource()
    {
        var asm = typeof(ItemCatalog).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found. Confirm <EmbeddedResource> in ModManager.Core.csproj.");
        var doc = JsonDocument.Parse(stream);
        var items = new List<ItemDefinition>();
        foreach (var el in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            items.Add(new ItemDefinition(
                Id: el.GetProperty("id").GetUInt32(),
                Name: el.GetProperty("name").GetString() ?? string.Empty,
                Category: el.GetProperty("category").GetString() ?? "unknown",
                MaxQuantity: el.GetProperty("maxQuantity").GetInt32(),
                QuestLocked: el.TryGetProperty("questLocked", out var ql) && ql.GetBoolean(),
                QuestNotes: el.TryGetProperty("questNotes", out var qn) ? qn.GetString() : null,
                Infusable: el.TryGetProperty("infusable", out var inf) && inf.GetBoolean(),
                Upgradeable: el.TryGetProperty("upgradeable", out var up) && up.GetBoolean(),
                MaxUpgrade: el.TryGetProperty("maxUpgrade", out var mu) ? mu.GetInt32() : 0));
        }
        return items;
    }
}
```

- [ ] **Step 5: Run the tests**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ItemCatalogTests"
```

Expected: all five tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/SaveEditor/FromSoft/ItemCatalog/items.json \
        src/ModManager.Core/SaveEditor/FromSoft/ItemCatalog/ItemCatalog.cs \
        src/ModManager.Core/ModManager.Core.csproj \
        tests/ModManager.Tests/SaveEditor/FromSoft/ItemCatalogTests.cs
git commit -m "core: embed ER item catalog + LoadAll() reader"
```

---

## Task 3: Core — `InventoryEntry` + `InventoryEdit` records

**Files:**
- Create: `src/ModManager.Core/SaveEditor/FromSoft/Inventory/InventoryEntry.cs`
- Create: `src/ModManager.Core/SaveEditor/FromSoft/Inventory/InventoryEdit.cs`
- Create: `tests/ModManager.Tests/SaveEditor/FromSoft/InventoryEntryTests.cs`

Pure shapes. No I/O.

- [ ] **Step 1: Write the failing tests**

```csharp
using ModManager.Core.SaveEditor.FromSoft.Inventory;

namespace ModManager.Tests.SaveEditor.FromSoft;

public class InventoryEntryTests
{
    [Fact]
    public void InventoryEntry_carries_handle_id_quantity_slot()
    {
        var entry = new InventoryEntry(
            GaItemHandle: 0x80000001,
            ItemId: 2030000,
            Quantity: 1,
            InventorySlotIndex: 0,
            UpgradeLevel: 12,
            InfusionId: 600);

        Assert.Equal(0x80000001u, entry.GaItemHandle);
        Assert.Equal(2030000u, entry.ItemId);
        Assert.Equal(1, entry.Quantity);
        Assert.Equal(12, entry.UpgradeLevel);
        Assert.Equal(600, entry.InfusionId);
    }

    [Fact]
    public void InventoryEdit_Add_carries_target_item_and_quantity()
    {
        var add = new InventoryEdit.Add(
            ItemId: 1040000,
            Quantity: 12,
            UpgradeLevel: 0,
            InfusionId: 0);

        Assert.Equal(1040000u, add.ItemId);
        Assert.Equal(12, add.Quantity);
    }

    [Fact]
    public void InventoryEdit_Remove_carries_target_handle()
    {
        var remove = new InventoryEdit.Remove(GaItemHandle: 0x80000005);
        Assert.Equal(0x80000005u, remove.GaItemHandle);
    }

    [Fact]
    public void InventoryEdit_Modify_carries_target_handle_and_new_values()
    {
        var modify = new InventoryEdit.Modify(
            GaItemHandle: 0x80000005,
            NewQuantity: 5,
            NewUpgradeLevel: 25,
            NewInfusionId: 700);

        Assert.Equal(25, modify.NewUpgradeLevel);
        Assert.Equal(700, modify.NewInfusionId);
    }
}
```

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~InventoryEntryTests"`
Expected: compilation failure.

- [ ] **Step 2: Implement `InventoryEntry`**

Create `src/ModManager.Core/SaveEditor/FromSoft/Inventory/InventoryEntry.cs`:

```csharp
namespace ModManager.Core.SaveEditor.FromSoft.Inventory;

/// <summary>One held-items row in an ER save slot. Discovered by walking the
/// held-items array at <c>magicOffset + Inventory.HeldItemsRelative</c> per Task 0
/// research. <see cref="UpgradeLevel"/> + <see cref="InfusionId"/> are populated
/// from the corresponding GA-items entry (cross-referenced by
/// <see cref="GaItemHandle"/>); for non-weapon entries they're 0.</summary>
public sealed record InventoryEntry(
    uint GaItemHandle,
    uint ItemId,
    int Quantity,
    int InventorySlotIndex,
    int UpgradeLevel,
    int InfusionId);
```

- [ ] **Step 3: Implement `InventoryEdit` (discriminated record)**

Create `src/ModManager.Core/SaveEditor/FromSoft/Inventory/InventoryEdit.cs`:

```csharp
namespace ModManager.Core.SaveEditor.FromSoft.Inventory;

/// <summary>One inventory operation. Pattern-matched in <see cref="Inventory.ApplyEdit"/>.
/// Add allocates new GA-items + held-items entries; Remove zeroes a held-items entry;
/// Modify patches an existing entry in place (quantity for consumables, upgrade +
/// infusion for weapons).</summary>
public abstract record InventoryEdit
{
    private InventoryEdit() { }  // sealed hierarchy — only the nested types below.

    /// <summary>Add a new item to the inventory. Allocates one GA-items slot + one
    /// held-items slot. For consumables, upgrade + infusion are 0. For weapons, both
    /// land in the GA-items entry.</summary>
    public sealed record Add(uint ItemId, int Quantity, int UpgradeLevel, int InfusionId) : InventoryEdit;

    /// <summary>Remove one held-items entry by its GA-item handle. The GA-items entry
    /// stays in place (conservative — multiple held-items entries can reference one
    /// GA-item handle).</summary>
    public sealed record Remove(uint GaItemHandle) : InventoryEdit;

    /// <summary>Modify an existing entry. Quantity patches the held-items entry;
    /// upgrade + infusion patch the GA-items entry.</summary>
    public sealed record Modify(uint GaItemHandle, int NewQuantity, int NewUpgradeLevel, int NewInfusionId) : InventoryEdit;
}
```

- [ ] **Step 4: Run the tests**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~InventoryEntryTests"
```

Expected: all four tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/SaveEditor/FromSoft/Inventory/InventoryEntry.cs \
        src/ModManager.Core/SaveEditor/FromSoft/Inventory/InventoryEdit.cs \
        tests/ModManager.Tests/SaveEditor/FromSoft/InventoryEntryTests.cs
git commit -m "core: InventoryEntry + InventoryEdit pure records"
```

---

## Task 4: Core — `Inventory.ReadEntries` + extend fixture (the load-bearing task)

**Files:**
- Create: `src/ModManager.Core/SaveEditor/FromSoft/Inventory/Inventory.cs` (reader portion)
- Modify: `tests/ModManager.Tests/SaveEditor/FromSoft/EldenRingFixture.cs` (add inventory planting)
- Create: `tests/ModManager.Tests/SaveEditor/FromSoft/InventoryTests.cs`

This is the load-bearing task. The reader proves the offset constants from Task 0 are right against a synthetic fixture before any write code lands.

- [ ] **Step 1: Extend the fixture with an inventory planter**

Modify `tests/ModManager.Tests/SaveEditor/FromSoft/EldenRingFixture.cs`. Add (alongside `BuildSaveWithOneCharacter`):

```csharp
/// <summary>Builds a save where slot 0 has stats + runes + N inventory entries planted
/// in the GA-items table + held-items array. Each planted entry uses a deterministic
/// handle (0x80000001..0x80000000 + N) so tests can locate them by handle.
///
/// Per Task 0 research: the held-items array lives at
/// <c>magicOffset + HeldItemsRelative</c> (see Inventory.cs for the constant).
/// Each entry is 16 bytes: handle u32 + item_id u32 + quantity u32 + slot_index i32.
/// </summary>
public static byte[] BuildSaveWithInventory(uint runes, byte vig, byte mnd, byte end_,
    byte str, byte dex, byte int_, byte fai, byte arc,
    params (uint itemId, int quantity)[] inventory)
{
    var buffer = BuildSaveWithOneCharacter(runes, vig, mnd, end_, str, dex, int_, fai, arc);

    // Plant inventory entries into slot 0 (the only active slot in the fixture).
    var slot0 = buffer.AsSpan(FirstSlotDataOffset, SlotDataSize);

    // 1) Plant GA-items entries (handles 0x80000001..) starting at GaItemsStart.
    //    All planted entries use the empty-size (8 bytes: handle + item_id) — they're
    //    consumables in the fixture. Weapon entries (21 bytes) are exercised in Task 5.
    var gaCursor = SlotData.GaItemsStart;
    for (int i = 0; i < inventory.Length; i++)
    {
        uint handle = 0x80000001u + (uint)i;
        BitConverter.TryWriteBytes(slot0.Slice(gaCursor, 4), handle);
        BitConverter.TryWriteBytes(slot0.Slice(gaCursor + 4, 4), inventory[i].itemId);
        gaCursor += SlotData.EmptyGaItemSize;  // 8 bytes — consumable shape.
    }

    // 2) Recompute the magic anchor for THIS layout (planted entries change the walk).
    int magicOffset = ModManager.Core.SaveEditor.FromSoft.EldenRingSave.DiscoverMagicOffset(slot0);

    // 3) Plant held-items entries at magicOffset + HeldItemsRelative.
    int heldItemsStart = magicOffset + Inventory.HeldItemsRelative;
    for (int i = 0; i < inventory.Length; i++)
    {
        int entryOffset = heldItemsStart + i * Inventory.HeldItemEntrySize;
        uint handle = 0x80000001u + (uint)i;
        BitConverter.TryWriteBytes(slot0.Slice(entryOffset, 4), handle);
        BitConverter.TryWriteBytes(slot0.Slice(entryOffset + 4, 4), inventory[i].itemId);
        BitConverter.TryWriteBytes(slot0.Slice(entryOffset + 8, 4), (uint)inventory[i].quantity);
        BitConverter.TryWriteBytes(slot0.Slice(entryOffset + 12, 4), i);  // inventory_slot_index
    }

    // 4) Re-write stats + runes at the NEW anchor (the fixture builder put them at the
    //    fixture anchor; now we shift them to where the walk says they go).
    //    Clear the fixture-anchor stats first so they don't double-count.
    var clearStart = SlotData.OffsetVigor;
    var clearEnd = SlotData.OffsetArcane + 4;
    slot0.Slice(clearStart, clearEnd - clearStart).Clear();
    SlotData.WriteRunes(slot0, runes, magicOffset);
    SlotData.WriteStats(slot0, vig, mnd, end_, str, dex, int_, fai, arc, magicOffset);

    // 5) Recompute slot 0's MD5 + save-header MD5.
    var slot0Md5 = buffer.AsSpan(FirstSlotMd5Offset, 0x10);
    SlotChecksum.ComputeMd5(slot0).CopyTo(slot0Md5);
    var saveHeader = buffer.AsSpan(SaveHeadersSectionStart, SaveHeadersSectionLength);
    var saveHeaderMd5 = buffer.AsSpan(SaveHeaderMd5Offset, 0x10);
    SlotChecksum.ComputeMd5(saveHeader).CopyTo(saveHeaderMd5);

    return buffer;
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/ModManager.Tests/SaveEditor/FromSoft/InventoryTests.cs`:

```csharp
using ModManager.Core.SaveEditor.FromSoft;
using ModManager.Core.SaveEditor.FromSoft.Inventory;

namespace ModManager.Tests.SaveEditor.FromSoft;

public class InventoryReadTests
{
    [Fact]
    public void ReadEntries_returns_planted_entries_in_order()
    {
        var bytes = EldenRingFixture.BuildSaveWithInventory(
            runes: 50000,
            vig: 20, mnd: 10, end_: 15, str: 25, dex: 12, int_: 9, fai: 9, arc: 9,
            inventory: new (uint, int)[] {
                (1040000, 12),   // Rune Arc × 12
                (1010000, 5),    // Smithing Stone (1) × 5
                (8050000, 1),    // Fia's Mist × 1
            });

        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, bytes);
        try
        {
            var slotBody = bytes.AsSpan(
                ModManager.Core.SaveEditor.FromSoft.EldenRingSave.FirstSlotDataOffset,
                SlotData.SlotSize);
            int magicOffset = EldenRingSave.DiscoverMagicOffset(slotBody);

            var entries = Inventory.ReadEntries(slotBody.ToArray(), magicOffset);

            Assert.Equal(3, entries.Count);
            Assert.Equal(1040000u, entries[0].ItemId);
            Assert.Equal(12, entries[0].Quantity);
            Assert.Equal(1010000u, entries[1].ItemId);
            Assert.Equal(5, entries[1].Quantity);
            Assert.Equal(8050000u, entries[2].ItemId);
            Assert.Equal(1, entries[2].Quantity);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadEntries_skips_empty_slots()
    {
        // Empty inventory — every held-items slot is zero. Expected: empty list.
        var bytes = EldenRingFixture.BuildSaveWithOneCharacter(
            runes: 0, vig: 1, mnd: 1, end_: 1, str: 1, dex: 1, int_: 1, fai: 1, arc: 1);

        var slotBody = bytes.AsSpan(
            ModManager.Core.SaveEditor.FromSoft.EldenRingSave.FirstSlotDataOffset,
            SlotData.SlotSize);
        int magicOffset = EldenRingSave.DiscoverMagicOffset(slotBody);

        var entries = Inventory.ReadEntries(slotBody.ToArray(), magicOffset);
        Assert.Empty(entries);
    }
}
```

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~InventoryReadTests"`
Expected: compilation failure (`Inventory` doesn't exist).

- [ ] **Step 3: Implement `Inventory.ReadEntries` (skeleton — write portion comes in Task 5)**

Create `src/ModManager.Core/SaveEditor/FromSoft/Inventory/Inventory.cs`:

```csharp
namespace ModManager.Core.SaveEditor.FromSoft.Inventory;

/// <summary>Inventory read/write primitives for ER save slots. Operates on a slot body
/// (0x280000 bytes) at a runtime-discovered magic anchor — same anchor model as
/// <see cref="SlotData"/>, just for the inventory tables instead of stats / runes.
///
/// Sources (locked by docs/superpowers/research/2026-05-26-inventory-format.md):
/// - Held-items relative offset + per-entry layout: ClayAmore/ER-Save-Editor (Apache-2.0),
///   src/save/common/equip_inventory_data.rs.
/// - ER 1.13+ delta confirmation: alfizari/Elden-Ring-Save-Editor (MIT),
///   src/Final.py held_items read path.</summary>
public static class Inventory
{
    // --- Held-items array layout (locked by Task 0 research) ---
    // The implementer fills in the actual offset from the research doc; this constant
    // exists ONLY in this file so changing it doesn't ripple through call sites.
    internal const int HeldItemsRelative = /* TASK 0 — pin the relative offset here */ 0;
    internal const int HeldItemEntrySize = 16;        // 4 u32 fields
    internal const int HeldItemEntryCount = 5120;     // confirm in Task 0 — likely matches GA-items count

    /// <summary>Walk the held-items array, returning one <see cref="InventoryEntry"/>
    /// per non-empty row. Empty rows (handle == 0) are skipped. For weapon entries,
    /// the upgrade + infusion are pulled from the corresponding GA-items entry by
    /// cross-referencing the handle.</summary>
    public static IReadOnlyList<InventoryEntry> ReadEntries(byte[] slotBody, int magicOffset)
    {
        ArgumentNullException.ThrowIfNull(slotBody);

        var results = new List<InventoryEntry>();
        int start = magicOffset + HeldItemsRelative;
        for (int i = 0; i < HeldItemEntryCount; i++)
        {
            int o = start + i * HeldItemEntrySize;
            if (o + HeldItemEntrySize > slotBody.Length) break;
            uint handle = BitConverter.ToUInt32(slotBody.AsSpan(o, 4));
            if (handle == 0) continue;

            uint itemId = BitConverter.ToUInt32(slotBody.AsSpan(o + 4, 4));
            int quantity = (int)BitConverter.ToUInt32(slotBody.AsSpan(o + 8, 4));
            int slotIndex = BitConverter.ToInt32(slotBody.AsSpan(o + 12, 4));

            // For weapons, look up the GA-items entry for upgrade + infusion.
            // (Task 0 locks the per-weapon-entry field offsets; placeholder constants here.)
            int upgrade = 0;
            int infusion = 0;
            if ((handle & 0xF0000000u) == 0x80000000u)
            {
                // Weapon: walk GA-items table to find the handle and read upgrade + infusion.
                // Implementation in Task 5 — read-only for now reads the held-items only;
                // weapon-detail enrichment is a Task 5 follow-up. The fixture in this task
                // only plants consumables, so the placeholder zeros are correct for the fixture.
            }

            results.Add(new InventoryEntry(handle, itemId, quantity, slotIndex, upgrade, infusion));
        }
        return results;
    }
}
```

**Note for the implementer:** the `HeldItemsRelative` constant MUST be set to Task 0's locked value before this code can pass the tests. Look up the number from `docs/superpowers/research/2026-05-26-inventory-format.md`. If Task 0 wasn't done, STOP and do Task 0 first.

- [ ] **Step 4: Run the tests**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~InventoryReadTests"
```

Expected: both tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/SaveEditor/FromSoft/Inventory/Inventory.cs \
        tests/ModManager.Tests/SaveEditor/FromSoft/EldenRingFixture.cs \
        tests/ModManager.Tests/SaveEditor/FromSoft/InventoryTests.cs
git commit -m "core: Inventory.ReadEntries + fixture inventory planter"
```

---

## Task 5: Core — `Inventory.ApplyEdit` (write portion) + GA-items weapon enrichment

**Files:**
- Modify: `src/ModManager.Core/SaveEditor/FromSoft/Inventory/Inventory.cs` (write methods)
- Modify: `tests/ModManager.Tests/SaveEditor/FromSoft/EldenRingFixture.cs` (weapon-entry planter)
- Modify: `tests/ModManager.Tests/SaveEditor/FromSoft/InventoryTests.cs` (add/remove/modify round-trips)

- [ ] **Step 1: Write the failing tests**

Append to `InventoryTests.cs`:

```csharp
public class InventoryRoundTripTests
{
    [Fact]
    public void ApplyEdit_Add_inserts_a_new_held_items_entry()
    {
        var bytes = EldenRingFixture.BuildSaveWithInventory(
            runes: 0, vig: 1, mnd: 1, end_: 1, str: 1, dex: 1, int_: 1, fai: 1, arc: 1,
            inventory: new (uint, int)[] { (1040000, 5) });  // Rune Arc × 5

        var slotBody = bytes.AsSpan(
            ModManager.Core.SaveEditor.FromSoft.EldenRingSave.FirstSlotDataOffset,
            SlotData.SlotSize).ToArray();
        int magicOffset = EldenRingSave.DiscoverMagicOffset(slotBody);

        Inventory.ApplyEdit(slotBody, magicOffset, new InventoryEdit.Add(
            ItemId: 1010000, Quantity: 8, UpgradeLevel: 0, InfusionId: 0));

        var entries = Inventory.ReadEntries(slotBody, magicOffset);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.ItemId == 1010000 && e.Quantity == 8);
    }

    [Fact]
    public void ApplyEdit_Remove_zeros_a_held_items_entry()
    {
        var bytes = EldenRingFixture.BuildSaveWithInventory(
            runes: 0, vig: 1, mnd: 1, end_: 1, str: 1, dex: 1, int_: 1, fai: 1, arc: 1,
            inventory: new (uint, int)[] { (1040000, 5), (1010000, 8) });

        var slotBody = bytes.AsSpan(
            ModManager.Core.SaveEditor.FromSoft.EldenRingSave.FirstSlotDataOffset,
            SlotData.SlotSize).ToArray();
        int magicOffset = EldenRingSave.DiscoverMagicOffset(slotBody);

        // Fixture-planted handles start at 0x80000001 — remove the first.
        Inventory.ApplyEdit(slotBody, magicOffset, new InventoryEdit.Remove(GaItemHandle: 0x80000001));

        var entries = Inventory.ReadEntries(slotBody, magicOffset);
        Assert.Single(entries);
        Assert.Equal(1010000u, entries[0].ItemId);
    }

    [Fact]
    public void ApplyEdit_Modify_changes_quantity_in_place()
    {
        var bytes = EldenRingFixture.BuildSaveWithInventory(
            runes: 0, vig: 1, mnd: 1, end_: 1, str: 1, dex: 1, int_: 1, fai: 1, arc: 1,
            inventory: new (uint, int)[] { (1040000, 5) });

        var slotBody = bytes.AsSpan(
            ModManager.Core.SaveEditor.FromSoft.EldenRingSave.FirstSlotDataOffset,
            SlotData.SlotSize).ToArray();
        int magicOffset = EldenRingSave.DiscoverMagicOffset(slotBody);

        Inventory.ApplyEdit(slotBody, magicOffset, new InventoryEdit.Modify(
            GaItemHandle: 0x80000001, NewQuantity: 50, NewUpgradeLevel: 0, NewInfusionId: 0));

        var entries = Inventory.ReadEntries(slotBody, magicOffset);
        Assert.Single(entries);
        Assert.Equal(50, entries[0].Quantity);
    }

    [Fact]
    public void ApplyEdit_AddThenRemove_is_a_no_op_for_other_entries()
    {
        var bytes = EldenRingFixture.BuildSaveWithInventory(
            runes: 0, vig: 1, mnd: 1, end_: 1, str: 1, dex: 1, int_: 1, fai: 1, arc: 1,
            inventory: new (uint, int)[] { (1040000, 5) });

        var slotBody = bytes.AsSpan(
            ModManager.Core.SaveEditor.FromSoft.EldenRingSave.FirstSlotDataOffset,
            SlotData.SlotSize).ToArray();
        int magicOffset = EldenRingSave.DiscoverMagicOffset(slotBody);

        var before = Inventory.ReadEntries(slotBody, magicOffset);
        Inventory.ApplyEdit(slotBody, magicOffset, new InventoryEdit.Add(2010000, 1, 0, 0));
        var newEntry = Inventory.ReadEntries(slotBody, magicOffset).First(e => e.ItemId == 2010000);
        Inventory.ApplyEdit(slotBody, magicOffset, new InventoryEdit.Remove(newEntry.GaItemHandle));
        var after = Inventory.ReadEntries(slotBody, magicOffset);

        Assert.Equal(before.Count, after.Count);
        Assert.Equal(before[0].ItemId, after[0].ItemId);
        Assert.Equal(before[0].Quantity, after[0].Quantity);
    }
}
```

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~InventoryRoundTripTests"`
Expected: compilation failure (`ApplyEdit` doesn't exist).

- [ ] **Step 2: Implement `Inventory.ApplyEdit`**

Append to `src/ModManager.Core/SaveEditor/FromSoft/Inventory/Inventory.cs`:

```csharp
    /// <summary>Apply one inventory edit to the slot body in place. The caller is
    /// responsible for recomputing the slot MD5 + save-header MD5 (handled by
    /// <see cref="EldenRingSave.WriteInventoryEdit"/>). This method ONLY mutates the
    /// slot body — no checksum work, no I/O.</summary>
    public static void ApplyEdit(byte[] slotBody, int magicOffset, InventoryEdit edit)
    {
        ArgumentNullException.ThrowIfNull(slotBody);
        ArgumentNullException.ThrowIfNull(edit);

        switch (edit)
        {
            case InventoryEdit.Add add:
                ApplyAdd(slotBody, magicOffset, add);
                break;
            case InventoryEdit.Remove remove:
                ApplyRemove(slotBody, magicOffset, remove);
                break;
            case InventoryEdit.Modify modify:
                ApplyModify(slotBody, magicOffset, modify);
                break;
        }
    }

    private static void ApplyAdd(byte[] slotBody, int magicOffset, InventoryEdit.Add add)
    {
        // 1) Find a free GA-items slot (handle == 0).
        int gaCursor = SlotData.GaItemsStart;
        int gaFreeOffset = -1;
        uint nextHandle = 0x80000001u;
        uint maxHandleSeen = 0;
        for (int i = 0; i < SlotData.GaItemCount; i++)
        {
            uint handle = BitConverter.ToUInt32(slotBody.AsSpan(gaCursor, 4));
            if (handle == 0 && gaFreeOffset < 0) gaFreeOffset = gaCursor;
            if (handle > maxHandleSeen) maxHandleSeen = handle;

            // Step the cursor by the entry size (same logic as DiscoverMagicOffset).
            int entrySize = handle == 0
                ? SlotData.EmptyGaItemSize
                : (handle & 0xF0000000u) switch
                {
                    0x80000000u => SlotData.WeaponGaItemSize,
                    0x90000000u => SlotData.ArmorGaItemSize,
                    _ => SlotData.EmptyGaItemSize,
                };
            gaCursor += entrySize;
        }
        if (gaFreeOffset < 0)
            throw new InvalidOperationException("GA-items table is full — no free slot for the new item.");

        nextHandle = maxHandleSeen + 1;
        if (nextHandle == 0) nextHandle = 0x80000001u;

        // 2) Find a free held-items slot.
        int heldStart = magicOffset + HeldItemsRelative;
        int heldFreeIndex = -1;
        for (int i = 0; i < HeldItemEntryCount; i++)
        {
            int o = heldStart + i * HeldItemEntrySize;
            if (BitConverter.ToUInt32(slotBody.AsSpan(o, 4)) == 0) { heldFreeIndex = i; break; }
        }
        if (heldFreeIndex < 0)
            throw new InvalidOperationException("Held-items array is full — no free slot for the new item.");

        // 3) Write the GA-items entry.
        BitConverter.TryWriteBytes(slotBody.AsSpan(gaFreeOffset, 4), nextHandle);
        BitConverter.TryWriteBytes(slotBody.AsSpan(gaFreeOffset + 4, 4), add.ItemId);
        // For weapons, write upgrade + infusion into the GA-items entry.
        // (Per Task 0 research — locked field offsets within the 21-byte weapon entry.)
        // Constants here use placeholders consistent with the spec; implementer pins to research doc.
        if (add.UpgradeLevel > 0 || add.InfusionId > 0)
        {
            // Weapon entry — fields at offsets 0x08 (upgrade), 0x0C (infusion) within the 21-byte entry.
            BitConverter.TryWriteBytes(slotBody.AsSpan(gaFreeOffset + 8, 4), (uint)add.UpgradeLevel);
            BitConverter.TryWriteBytes(slotBody.AsSpan(gaFreeOffset + 12, 4), (uint)add.InfusionId);
        }

        // 4) Write the held-items entry.
        int heldOffset = heldStart + heldFreeIndex * HeldItemEntrySize;
        BitConverter.TryWriteBytes(slotBody.AsSpan(heldOffset, 4), nextHandle);
        BitConverter.TryWriteBytes(slotBody.AsSpan(heldOffset + 4, 4), add.ItemId);
        BitConverter.TryWriteBytes(slotBody.AsSpan(heldOffset + 8, 4), (uint)add.Quantity);
        BitConverter.TryWriteBytes(slotBody.AsSpan(heldOffset + 12, 4), heldFreeIndex);
    }

    private static void ApplyRemove(byte[] slotBody, int magicOffset, InventoryEdit.Remove remove)
    {
        // Find the held-items entry by handle, zero it.
        int heldStart = magicOffset + HeldItemsRelative;
        for (int i = 0; i < HeldItemEntryCount; i++)
        {
            int o = heldStart + i * HeldItemEntrySize;
            if (BitConverter.ToUInt32(slotBody.AsSpan(o, 4)) == remove.GaItemHandle)
            {
                slotBody.AsSpan(o, HeldItemEntrySize).Clear();
                BitConverter.TryWriteBytes(slotBody.AsSpan(o + 12, 4), -1);  // slot index = -1 per fixture convention
                return;
            }
        }
        throw new InvalidOperationException($"GA-item handle 0x{remove.GaItemHandle:X8} not found in held-items.");
    }

    private static void ApplyModify(byte[] slotBody, int magicOffset, InventoryEdit.Modify modify)
    {
        int heldStart = magicOffset + HeldItemsRelative;
        bool patched = false;
        for (int i = 0; i < HeldItemEntryCount; i++)
        {
            int o = heldStart + i * HeldItemEntrySize;
            if (BitConverter.ToUInt32(slotBody.AsSpan(o, 4)) == modify.GaItemHandle)
            {
                BitConverter.TryWriteBytes(slotBody.AsSpan(o + 8, 4), (uint)modify.NewQuantity);
                patched = true;
                break;
            }
        }
        if (!patched)
            throw new InvalidOperationException($"GA-item handle 0x{modify.GaItemHandle:X8} not found in held-items.");

        // Weapon entries: also patch upgrade + infusion in the GA-items table.
        if (modify.NewUpgradeLevel > 0 || modify.NewInfusionId > 0)
        {
            int gaCursor = SlotData.GaItemsStart;
            for (int i = 0; i < SlotData.GaItemCount; i++)
            {
                uint handle = BitConverter.ToUInt32(slotBody.AsSpan(gaCursor, 4));
                if (handle == modify.GaItemHandle)
                {
                    BitConverter.TryWriteBytes(slotBody.AsSpan(gaCursor + 8, 4), (uint)modify.NewUpgradeLevel);
                    BitConverter.TryWriteBytes(slotBody.AsSpan(gaCursor + 12, 4), (uint)modify.NewInfusionId);
                    break;
                }
                int entrySize = handle == 0
                    ? SlotData.EmptyGaItemSize
                    : (handle & 0xF0000000u) switch
                    {
                        0x80000000u => SlotData.WeaponGaItemSize,
                        0x90000000u => SlotData.ArmorGaItemSize,
                        _ => SlotData.EmptyGaItemSize,
                    };
                gaCursor += entrySize;
            }
        }
    }
```

- [ ] **Step 3: Run the tests**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~InventoryRoundTripTests"
```

Expected: all four tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.Core/SaveEditor/FromSoft/Inventory/Inventory.cs \
        tests/ModManager.Tests/SaveEditor/FromSoft/InventoryTests.cs
git commit -m "core: Inventory.ApplyEdit (add/remove/modify) with round-trip tests"
```

---

## Task 6: Core — `EldenRingSave.WriteInventoryEdit` + extended byte-mask verify

**Files:**
- Modify: `src/ModManager.Core/SaveEditor/FromSoft/EldenRingSave.cs`
- Create: `tests/ModManager.Tests/SaveEditor/FromSoft/EldenRingSaveInventoryTests.cs`

Wraps `Inventory.ApplyEdit` in the MVP's atomic-write + MD5-recompute + post-write byte-mask verification.

- [ ] **Step 1: Write the failing test**

Create `tests/ModManager.Tests/SaveEditor/FromSoft/EldenRingSaveInventoryTests.cs`:

```csharp
using ModManager.Core.SaveEditor.FromSoft;
using ModManager.Core.SaveEditor.FromSoft.Inventory;

namespace ModManager.Tests.SaveEditor.FromSoft;

public class EldenRingSaveInventoryTests
{
    [Fact]
    public void WriteInventoryEdit_Add_lands_on_disk_and_round_trips()
    {
        var bytes = EldenRingFixture.BuildSaveWithInventory(
            runes: 1000, vig: 10, mnd: 10, end_: 10, str: 10, dex: 10, int_: 10, fai: 10, arc: 10,
            inventory: new (uint, int)[] { (1040000, 5) });

        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, bytes);

            EldenRingSave.WriteInventoryEdit(path, slotIndex: 0,
                new InventoryEdit.Add(ItemId: 1010000, Quantity: 8, UpgradeLevel: 0, InfusionId: 0));

            // Read back via the same Inventory.ReadEntries path.
            var verifyBytes = File.ReadAllBytes(path);
            var slotBody = verifyBytes.AsSpan(EldenRingSave.FirstSlotDataOffset, SlotData.SlotSize).ToArray();
            int magicOffset = EldenRingSave.DiscoverMagicOffset(slotBody);
            var entries = Inventory.ReadEntries(slotBody, magicOffset);

            Assert.Contains(entries, e => e.ItemId == 1010000 && e.Quantity == 8);
            Assert.Contains(entries, e => e.ItemId == 1040000 && e.Quantity == 5);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void WriteInventoryEdit_refuses_inactive_slot()
    {
        var bytes = EldenRingFixture.BuildSaveWithInventory(
            runes: 0, vig: 1, mnd: 1, end_: 1, str: 1, dex: 1, int_: 1, fai: 1, arc: 1,
            inventory: new (uint, int)[] { });

        // Slot 0 is active in the fixture; slot 1 is not.
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, bytes);
            var ex = Assert.Throws<InvalidOperationException>(() =>
                EldenRingSave.WriteInventoryEdit(path, slotIndex: 1,
                    new InventoryEdit.Add(1040000, 1, 0, 0)));
            Assert.Contains("inactive", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }
}
```

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~EldenRingSaveInventoryTests"`
Expected: compilation failure.

- [ ] **Step 2: Implement `WriteInventoryEdit`**

Append to `src/ModManager.Core/SaveEditor/FromSoft/EldenRingSave.cs`:

```csharp
    /// <summary>Apply one inventory edit to a slot. Wraps <see cref="Inventory.ApplyEdit"/>
    /// in the same atomic-write + MD5-recompute + post-write byte-mask verify flow as
    /// <see cref="WriteEdit"/>. The byte-mask is computed per-edit-type so unmasked bytes
    /// outside the touched ranges must be byte-identical to the pre-edit slot.</summary>
    public static void WriteInventoryEdit(string savePath, int slotIndex, InventoryEdit edit)
    {
        ArgumentNullException.ThrowIfNull(savePath);
        ArgumentNullException.ThrowIfNull(edit);
        if (slotIndex < 0 || slotIndex >= SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        if (!File.Exists(savePath)) throw new FileNotFoundException(null, savePath);

        var bytes = File.ReadAllBytes(savePath);
        if (bytes.Length < MinimumFileSize)
            throw new InvalidDataException($"Save file too small ({bytes.Length}).");

        if (bytes[CharActiveStatusOffset + slotIndex] == 0)
            throw new InvalidOperationException($"Slot {slotIndex} is inactive — cannot edit.");

        // Snapshot the pre-edit slot for verification.
        var preSlot = bytes.AsSpan(FirstSlotDataOffset + slotIndex * SlotStride, SlotDataSize).ToArray();

        // 1) Discover the magic anchor + apply the inventory edit in place.
        var slotData = bytes.AsSpan(FirstSlotDataOffset + slotIndex * SlotStride, SlotDataSize);
        int magicOffset = DiscoverMagicOffset(slotData);

        // ApplyEdit needs a mutable byte[] (not Span) because the GA-items + held-items
        // edits may both touch the slot. Copy out → mutate → copy back.
        var slotCopy = slotData.ToArray();
        Inventory.ApplyEdit(slotCopy, magicOffset, edit);
        slotCopy.AsSpan().CopyTo(slotData);

        // 2) Recompute slot MD5.
        var slotMd5 = bytes.AsSpan(FirstSlotMd5Offset + slotIndex * SlotStride, 0x10);
        SlotChecksum.ComputeMd5(slotData).CopyTo(slotMd5);

        // 3) Recompute save-header MD5 (covers per-slot summary, which the inventory edit
        //    doesn't touch — but recomputing is cheap and keeps the file's invariants whole).
        var saveHeader = bytes.AsSpan(SaveHeadersSectionStart, SaveHeadersSectionLength);
        var saveHeaderMd5 = bytes.AsSpan(SaveHeaderMd5Offset, 0x10);
        SlotChecksum.ComputeMd5(saveHeader).CopyTo(saveHeaderMd5);

        // 4) Atomic write.
        WriteBytesAtomic(savePath, bytes);

        // 5) Post-write byte-mask verify — see VerifyPostWriteInventory below.
        VerifyPostWriteInventory(savePath, slotIndex, edit, preSlot, magicOffset);
    }

    private static void VerifyPostWriteInventory(string savePath, int slotIndex, InventoryEdit edit, byte[] preSlot, int magicOffset)
    {
        var verifyBytes = File.ReadAllBytes(savePath);
        var postSlot = verifyBytes.AsSpan(FirstSlotDataOffset + slotIndex * SlotStride, SlotDataSize);

        // Compute the touched-range mask per edit type. Anything OUTSIDE the mask must be
        // byte-identical to preSlot.
        var touched = new List<(int start, int endExclusive)>();
        int heldStart = magicOffset + Inventory.HeldItemsRelative;
        int heldEnd = heldStart + Inventory.HeldItemEntryCount * Inventory.HeldItemEntrySize;

        // For all edit types: every held-items entry MAY have been touched (a defensive
        // mask covering the whole held-items region — narrower mask requires re-reading
        // which entry index the Add/Remove/Modify landed on; defensive is simpler + safe).
        touched.Add((heldStart, heldEnd));

        // The GA-items table is also potentially touched (Add allocates a new GA entry).
        touched.Add((SlotData.GaItemsStart, heldStart));  // conservative: everything before held-items.

        // Anything outside those touched ranges must match.
        for (int i = 0; i < SlotDataSize; i++)
        {
            bool inMask = false;
            foreach (var (s, e) in touched)
            {
                if (i >= s && i < e) { inMask = true; break; }
            }
            if (inMask) continue;

            if (postSlot[i] != preSlot[i])
            {
                throw new InvalidDataException(
                    $"Post-write inventory verify: byte drift at slot offset 0x{i:X} (pre=0x{preSlot[i]:X2}, post=0x{postSlot[i]:X2}). " +
                    "The write touched bytes outside the edit's mask.");
            }
        }
    }
```

- [ ] **Step 3: Run the tests**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~EldenRingSaveInventoryTests"
```

Expected: both tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.Core/SaveEditor/FromSoft/EldenRingSave.cs \
        tests/ModManager.Tests/SaveEditor/FromSoft/EldenRingSaveInventoryTests.cs
git commit -m "core: EldenRingSave.WriteInventoryEdit + post-write byte-mask verify"
```

---

## Task 7: App service — `SaveEditorService.EditInventory`

**Files:**
- Modify: `src/ModManager.App/Services/SaveEditorService.cs`

Reuses the snapshot-first wrapper pattern from `EditCharacter`. Symmetry over novelty.

- [ ] **Step 1: Add the method**

Modify `src/ModManager.App/Services/SaveEditorService.cs`. Append:

```csharp
    /// <summary>Apply one inventory edit. Snapshots FIRST; if that fails, throws before
    /// any write. Returns the snapshot so the UI can surface it.</summary>
    /// <exception cref="InvalidOperationException">Snapshot failed — edit aborted.</exception>
    public SaveSnapshot EditInventory(
        string saveDir, string snapshotsDir, string savePath,
        int slotIndex, CharacterSlot beforeEdit, InventoryEdit edit)
    {
        // Auto-label using the edit's shape so the snapshot is self-explanatory.
        string editKind = edit switch
        {
            InventoryEdit.Add => "add-item",
            InventoryEdit.Remove => "remove-item",
            InventoryEdit.Modify => "modify-item",
            _ => "inventory-edit",
        };
        var label = $"before-edit: {editKind} ({beforeEdit.Name}) — {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

        SaveSnapshot snap;
        try
        {
            snap = SaveManager.Backup(saveDir, snapshotsDir, label, auto: false);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"Couldn't snapshot the save before editing ({e.Message}). Edit was NOT applied.", e);
        }

        try
        {
            EldenRingSave.WriteInventoryEdit(savePath, slotIndex, edit);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"Inventory edit failed ({e.Message}). Your save is still intact, and a pre-edit snapshot is in the Snapshots list.", e);
        }

        return snap;
    }
```

The `using ModManager.Core.SaveEditor.FromSoft.Inventory;` namespace import goes at the top of the file.

- [ ] **Step 2: Confirm the App builds**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: success. If a locked Core DLL: kill `ModManager.App.exe` and retry.

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.App/Services/SaveEditorService.cs
git commit -m "app: SaveEditorService.EditInventory (snapshot-first wrapper)"
```

---

## Task 8: UI — `InventoryPicker` UserControl + tabbed `CharacterEditDialog`

**Files:**
- Create: `src/ModManager.App/InventoryPicker.xaml`
- Create: `src/ModManager.App/InventoryPicker.xaml.cs`
- Modify: `src/ModManager.App/CharacterEditDialog.xaml` (wrap in Pivot, add Inventory tab)
- Modify: `src/ModManager.App/CharacterEditDialog.xaml.cs` (handle picker → staged-edits list)
- Modify: `src/ModManager.App/SavesDialog.xaml.cs` (wire through both EditCharacter + EditInventory)

The dialog stays one `ContentDialog`. The picker is a UserControl embedded inside the Inventory tab.

- [ ] **Step 1: Create `InventoryPicker.xaml`**

Create `src/ModManager.App/InventoryPicker.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<UserControl
    x:Class="ModManager.App.InventoryPicker"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Grid ColumnSpacing="12" RowSpacing="8">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!-- Left column: catalog -->
        <StackPanel Grid.Row="0" Grid.Column="0" Spacing="6">
            <TextBox x:Name="SearchBox" PlaceholderText="Search items…" TextChanged="OnSearchChanged" />
            <ComboBox x:Name="CategoryFilter" HorizontalAlignment="Stretch"
                      SelectionChanged="OnCategoryChanged" Header="Category">
                <ComboBoxItem Content="All" IsSelected="True" />
                <ComboBoxItem Content="weapon" />
                <ComboBoxItem Content="armor" />
                <ComboBoxItem Content="talisman" />
                <ComboBoxItem Content="spell" />
                <ComboBoxItem Content="ash-of-war" />
                <ComboBoxItem Content="consumable" />
                <ComboBoxItem Content="key-item" />
                <ComboBoxItem Content="crafting" />
            </ComboBox>
        </StackPanel>

        <ListView x:Name="CatalogList" Grid.Row="1" Grid.Column="0"
                  SelectionChanged="OnCatalogSelectionChanged" />

        <Button x:Name="AddButton" Grid.Row="2" Grid.Column="0" Content="Add →"
                Click="OnAddClicked" HorizontalAlignment="Right" IsEnabled="False" />

        <!-- Right column: staged + existing -->
        <StackPanel Grid.Row="0" Grid.Column="1" Spacing="6">
            <TextBlock Text="Held inventory + staged edits" FontWeight="SemiBold" />
        </StackPanel>
        <ListView x:Name="StagedList" Grid.Row="1" Grid.Column="1" />
        <StackPanel Grid.Row="2" Grid.Column="1" Orientation="Horizontal" Spacing="8"
                    HorizontalAlignment="Right">
            <Button x:Name="ModifyButton" Content="Modify" Click="OnModifyClicked" IsEnabled="False" />
            <Button x:Name="RemoveButton" Content="Remove" Click="OnRemoveClicked" IsEnabled="False" />
        </StackPanel>
    </Grid>
</UserControl>
```

- [ ] **Step 2: Create `InventoryPicker.xaml.cs`**

Create `src/ModManager.App/InventoryPicker.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core.SaveEditor.FromSoft.Inventory;
using ModManager.Core.SaveEditor.FromSoft.ItemCatalog;

namespace ModManager.App;

public sealed partial class InventoryPicker : UserControl
{
    private IReadOnlyList<ItemDefinition> _catalog = Array.Empty<ItemDefinition>();
    private List<InventoryEntry> _existing = new();

    /// <summary>Edits staged in the UI but not yet committed. Read by the dialog at Save.</summary>
    public List<InventoryEdit> StagedEdits { get; } = new();

    public InventoryPicker()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public void Initialize(IReadOnlyList<InventoryEntry> existing)
    {
        _existing = existing.ToList();
        RefreshStagedList();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _catalog = ItemCatalog.LoadAll();
        RefreshCatalogList();
    }

    private void RefreshCatalogList()
    {
        var query = SearchBox.Text ?? "";
        var category = (CategoryFilter.SelectedItem as ComboBoxItem)?.Content as string ?? "All";

        var filtered = _catalog
            .Where(i => category == "All" || i.Category == category)
            .Where(i => string.IsNullOrWhiteSpace(query) || i.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Name)
            .Take(200)  // cap render — search to narrow further
            .ToList();

        CatalogList.ItemsSource = filtered.Select(i => new
        {
            Item = i,
            Display = i.QuestLocked ? $"{i.Name}  ⚠ quest-locked" : i.Name,
        }).ToList();
        CatalogList.DisplayMemberPath = "Display";
    }

    private void RefreshStagedList()
    {
        var rows = new List<object>();
        // Existing entries the user hasn't touched.
        foreach (var entry in _existing)
        {
            var def = _catalog.FirstOrDefault(d => d.Id == entry.ItemId);
            var name = def?.Name ?? $"Unknown (0x{entry.ItemId:X8})";
            rows.Add(new
            {
                Display = $"{name} × {entry.Quantity}",
                Entry = entry,
            });
        }
        // Staged additions.
        foreach (var add in StagedEdits.OfType<InventoryEdit.Add>())
        {
            var def = _catalog.FirstOrDefault(d => d.Id == add.ItemId);
            var name = def?.Name ?? $"Unknown (0x{add.ItemId:X8})";
            rows.Add(new
            {
                Display = $"+ {name} × {add.Quantity}",
                Entry = (InventoryEntry?)null,
            });
        }
        StagedList.ItemsSource = rows;
        StagedList.DisplayMemberPath = "Display";
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RefreshCatalogList();
    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e) => RefreshCatalogList();

    private void OnCatalogSelectionChanged(object sender, SelectionChangedEventArgs e)
        => AddButton.IsEnabled = CatalogList.SelectedItem != null;

    private async void OnAddClicked(object sender, RoutedEventArgs e)
    {
        if (CatalogList.SelectedItem is null) return;
        var selected = ((dynamic)CatalogList.SelectedItem).Item as ItemDefinition;
        if (selected is null) return;

        // Quest-locked confirm modal — see Task 9.
        if (selected.QuestLocked)
        {
            var confirm = new QuestLockConfirmDialog(selected);
            confirm.XamlRoot = this.XamlRoot;
            var result = await confirm.ShowAsync();
            if (result != ContentDialogResult.Primary) return;
        }

        // For Phase 2, default Quantity = 1 (modify-after-add covers quantity tuning).
        StagedEdits.Add(new InventoryEdit.Add(
            ItemId: selected.Id,
            Quantity: 1,
            UpgradeLevel: 0,
            InfusionId: 0));
        RefreshStagedList();
    }

    private void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        if (StagedList.SelectedItem is null) return;
        var entry = ((dynamic)StagedList.SelectedItem).Entry as InventoryEntry?;
        if (entry is null) return;
        StagedEdits.Add(new InventoryEdit.Remove(entry.Value.GaItemHandle));
        RefreshStagedList();
    }

    private void OnModifyClicked(object sender, RoutedEventArgs e)
    {
        // Phase 2 v1: a minimal quantity prompt. A full modify-dialog with infusion picker
        // for weapons can land in a follow-up — defer to keep PR scope sane.
        // Implementer: drop in a simple NumberBox dialog here that asks for a new quantity.
    }
}
```

- [ ] **Step 3: Modify `CharacterEditDialog.xaml` to tab the content**

Modify `src/ModManager.App/CharacterEditDialog.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ContentDialog
    x:Class="ModManager.App.CharacterEditDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:ModManager.App"
    Title="Edit character"
    PrimaryButtonText="Save edit"
    CloseButtonText="Cancel"
    DefaultButton="Primary">

    <Pivot Width="640" Height="480">
        <PivotItem Header="Identity">
            <StackPanel Spacing="12">
                <TextBlock x:Name="IntroText" TextWrapping="Wrap" Opacity="0.85" FontSize="12" />
                <TextBox x:Name="NameBox" Header="Name" MaxLength="16"
                         PlaceholderText="Character name (max 16)" />
                <NumberBox x:Name="RunesBox" Header="Runes" Minimum="0" Maximum="999999999"
                           SpinButtonPlacementMode="Compact" />

                <TextBlock Text="Attributes" FontWeight="SemiBold" Margin="0,4,0,0" />
                <Grid ColumnSpacing="12" RowSpacing="8">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="*" />
                    </Grid.ColumnDefinitions>
                    <Grid.RowDefinitions>
                        <RowDefinition /><RowDefinition /><RowDefinition /><RowDefinition />
                    </Grid.RowDefinitions>
                    <NumberBox x:Name="VigBox" Grid.Row="0" Grid.Column="0" Header="VIG" Minimum="1" Maximum="99" SpinButtonPlacementMode="Compact" ValueChanged="OnStatChanged" />
                    <NumberBox x:Name="MndBox" Grid.Row="0" Grid.Column="1" Header="MND" Minimum="1" Maximum="99" SpinButtonPlacementMode="Compact" ValueChanged="OnStatChanged" />
                    <NumberBox x:Name="EndBox" Grid.Row="1" Grid.Column="0" Header="END" Minimum="1" Maximum="99" SpinButtonPlacementMode="Compact" ValueChanged="OnStatChanged" />
                    <NumberBox x:Name="StrBox" Grid.Row="1" Grid.Column="1" Header="STR" Minimum="1" Maximum="99" SpinButtonPlacementMode="Compact" ValueChanged="OnStatChanged" />
                    <NumberBox x:Name="DexBox" Grid.Row="2" Grid.Column="0" Header="DEX" Minimum="1" Maximum="99" SpinButtonPlacementMode="Compact" ValueChanged="OnStatChanged" />
                    <NumberBox x:Name="IntBox" Grid.Row="2" Grid.Column="1" Header="INT" Minimum="1" Maximum="99" SpinButtonPlacementMode="Compact" ValueChanged="OnStatChanged" />
                    <NumberBox x:Name="FaiBox" Grid.Row="3" Grid.Column="0" Header="FAI" Minimum="1" Maximum="99" SpinButtonPlacementMode="Compact" ValueChanged="OnStatChanged" />
                    <NumberBox x:Name="ArcBox" Grid.Row="3" Grid.Column="1" Header="ARC" Minimum="1" Maximum="99" SpinButtonPlacementMode="Compact" ValueChanged="OnStatChanged" />
                </Grid>
                <TextBlock x:Name="LevelText" Opacity="0.7" FontSize="12" />
            </StackPanel>
        </PivotItem>

        <PivotItem Header="Inventory">
            <Grid RowSpacing="8">
                <Grid.RowDefinitions>
                    <RowDefinition Height="*" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>
                <local:InventoryPicker x:Name="InventoryPickerControl" Grid.Row="0" />
                <TextBlock Grid.Row="1" TextWrapping="Wrap" Opacity="0.7" FontSize="11"
                           Text="⚠ Quest-locked items can soft-lock questlines. A snapshot is taken before any edit. Item catalog by ClayAmore/ER-Save-Editor (Apache-2.0) + alfizari/Elden-Ring-Save-Editor (MIT)." />
            </Grid>
        </PivotItem>
    </Pivot>
</ContentDialog>
```

- [ ] **Step 4: Modify `CharacterEditDialog.xaml.cs`**

Modify `src/ModManager.App/CharacterEditDialog.xaml.cs`. Update the constructor + add an `InventoryEdits` accessor:

```csharp
using Microsoft.UI.Xaml.Controls;
using ModManager.Core.SaveEditor.FromSoft;
using ModManager.Core.SaveEditor.FromSoft.Inventory;

namespace ModManager.App;

public sealed partial class CharacterEditDialog : ContentDialog
{
    public CharacterEditDialog(CharacterSlot slot, IReadOnlyList<InventoryEntry> inventory)
    {
        InitializeComponent();
        IntroText.Text = $"Editing \"{slot.Name}\" — {slot.Class}, currently Lv {slot.Level}.";
        NameBox.Text = slot.Name;
        RunesBox.Value = slot.Runes;
        VigBox.Value = slot.Vig; MndBox.Value = slot.Mnd;
        EndBox.Value = slot.End; StrBox.Value = slot.Str;
        DexBox.Value = slot.Dex; IntBox.Value = slot.Int;
        FaiBox.Value = slot.Fai; ArcBox.Value = slot.Arc;
        UpdateLevelText();
        InventoryPickerControl.Initialize(inventory);
    }

    public CharacterEdit GetEdit() => new(
        Name: (NameBox.Text ?? "").Trim(),
        Runes: ToUInt32(RunesBox.Value),
        Vig: ToByte(VigBox.Value), Mnd: ToByte(MndBox.Value),
        End: ToByte(EndBox.Value), Str: ToByte(StrBox.Value),
        Dex: ToByte(DexBox.Value), Int: ToByte(IntBox.Value),
        Fai: ToByte(FaiBox.Value), Arc: ToByte(ArcBox.Value));

    /// <summary>The inventory edits the user staged. Read by the caller on Primary result.</summary>
    public IReadOnlyList<InventoryEdit> GetInventoryEdits() => InventoryPickerControl.StagedEdits;

    public bool IsValid()
    {
        var name = (NameBox.Text ?? "").Trim();
        return name.Length is > 0 and <= 16;
    }

    private void OnStatChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        => UpdateLevelText();

    private void UpdateLevelText()
    {
        var sum = ToByte(VigBox.Value) + ToByte(MndBox.Value) + ToByte(EndBox.Value) + ToByte(StrBox.Value)
                + ToByte(DexBox.Value) + ToByte(IntBox.Value) + ToByte(FaiBox.Value) + ToByte(ArcBox.Value);
        LevelText.Text = $"→ Level {sum - 79} (recomputed from stats)";
    }

    private static uint ToUInt32(double v) => double.IsNaN(v) ? 0u : (uint)Math.Clamp(v, 0, 999_999_999);
    private static byte ToByte(double v) => double.IsNaN(v) ? (byte)1 : (byte)Math.Clamp(v, 1, 99);
}
```

- [ ] **Step 5: Wire through `SavesDialog.xaml.cs`**

In `src/ModManager.App/SavesDialog.xaml.cs`, find the existing `EditCharacter`-click handler (search for `CharacterEditDialog`). After the existing `service.EditCharacter(...)` call, apply any staged inventory edits sequentially through `service.EditInventory(...)`. Each inventory edit takes its own snapshot — they pile up in the snapshots list but the user can restore from any of them.

Pseudo-code skeleton (the exact handler name varies — locate by grep):

```csharp
// Existing identity edit (unchanged).
var identityEdit = dialog.GetEdit();
var charSlot = /* ... existing lookup ... */;
service.EditCharacter(saveDir, snapshotsDir, savePath, slotIndex, charSlot, identityEdit);

// New: apply staged inventory edits.
foreach (var invEdit in dialog.GetInventoryEdits())
{
    service.EditInventory(saveDir, snapshotsDir, savePath, slotIndex, charSlot, invEdit);
}
```

Surface any failure in `StatusText` the same way the MVP does.

- [ ] **Step 6: Build + smoke**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: success.

- [ ] **Step 7: Commit**

```bash
git add src/ModManager.App/InventoryPicker.xaml \
        src/ModManager.App/InventoryPicker.xaml.cs \
        src/ModManager.App/CharacterEditDialog.xaml \
        src/ModManager.App/CharacterEditDialog.xaml.cs \
        src/ModManager.App/SavesDialog.xaml.cs
git commit -m "app: tabbed CharacterEditDialog + InventoryPicker UserControl"
```

---

## Task 9: UI — `QuestLockConfirmDialog`

**Files:**
- Create: `src/ModManager.App/QuestLockConfirmDialog.xaml`
- Create: `src/ModManager.App/QuestLockConfirmDialog.xaml.cs`

The confirm modal that gates adds of quest-locked items.

- [ ] **Step 1: Create the XAML**

Create `src/ModManager.App/QuestLockConfirmDialog.xaml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ContentDialog
    x:Class="ModManager.App.QuestLockConfirmDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Add quest-locked item"
    PrimaryButtonText="I understand — add anyway"
    CloseButtonText="Cancel"
    DefaultButton="Close">
    <StackPanel Width="420" Spacing="12">
        <TextBlock x:Name="ItemNameText" FontWeight="SemiBold" />
        <TextBlock Text="Quest notes:" Opacity="0.8" />
        <TextBlock x:Name="QuestNotesText" TextWrapping="Wrap" />
        <TextBlock TextWrapping="Wrap" Opacity="0.7" FontSize="12"
                   Text="Quest-locked items can soft-lock questlines if added at the wrong story progress. A snapshot will be taken before any edit — restore from the Snapshots list if something goes wrong." />
    </StackPanel>
</ContentDialog>
```

- [ ] **Step 2: Create the code-behind**

Create `src/ModManager.App/QuestLockConfirmDialog.xaml.cs`:

```csharp
using Microsoft.UI.Xaml.Controls;
using ModManager.Core.SaveEditor.FromSoft.ItemCatalog;

namespace ModManager.App;

public sealed partial class QuestLockConfirmDialog : ContentDialog
{
    public QuestLockConfirmDialog(ItemDefinition item)
    {
        InitializeComponent();
        ItemNameText.Text = item.Name;
        QuestNotesText.Text = item.QuestNotes ?? "(No quest notes in catalog.)";
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: success.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/QuestLockConfirmDialog.xaml \
        src/ModManager.App/QuestLockConfirmDialog.xaml.cs
git commit -m "app: QuestLockConfirmDialog (gates adds of quest-locked items)"
```

---

## Task 10: Attribution — `THIRD_PARTY_NOTICES.md` + Settings → About

**Files:**
- Modify: `THIRD_PARTY_NOTICES.md` (append ClayAmore + alfizari)
- Modify: `src/ModManager.App/SettingsDialog.xaml` (append the Acknowledgements panel rows)

Honor the builders. The catalog is community work; we credit by name + repo + license.

- [ ] **Step 1: Append to `THIRD_PARTY_NOTICES.md`**

Append the full Apache-2.0 license text for ClayAmore + the MIT license text for alfizari, each with the copyright lines preserved as shipped in their repos.

- [ ] **Step 2: Append to Settings → About / Acknowledgements panel**

Find the Acknowledgements section in `src/ModManager.App/SettingsDialog.xaml`. Add two new rows:

```xml
<HyperlinkButton Content="ClayAmore/ER-Save-Editor (Apache-2.0) — ER item catalog"
                 NavigateUri="https://github.com/ClayAmore/ER-Save-Editor" />
<HyperlinkButton Content="alfizari/Elden-Ring-Save-Editor (MIT) — ER 1.13+ catalog diff"
                 NavigateUri="https://github.com/alfizari/Elden-Ring-Save-Editor" />
```

- [ ] **Step 3: Commit**

```bash
git add THIRD_PARTY_NOTICES.md src/ModManager.App/SettingsDialog.xaml
git commit -m "honor: credit ClayAmore + alfizari for ER item catalog"
```

---

## Task 11: Smoke + PR

**Files:**
- None (smoke test + PR open)

- [ ] **Step 1: Full test run**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

Expected: all green. MVP's existing tests + Phase 2's new tests both pass.

- [ ] **Step 2: Build + run**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
# Launch the app — bind to Elden Ring profile — open Saves dialog — pick a character — Edit
```

- [ ] **Step 3: Smoke flow on a real save**

Per the Spec's smoke gate:

1. Pick a test character in the Saves dialog.
2. Open Edit. Identity tab should look like MVP — name + runes + stats.
3. Switch to Inventory tab. Catalog list populates from `ItemCatalog.LoadAll()` — should see 200 items (capped), filterable, searchable.
4. Add a Rune Arc (quantity 1, non-quest-locked). It should land in the staged list with `+ Rune Arc × 1`.
5. Click an existing inventory entry, click Remove. It should land in the staged list with `× <entry>`.
6. Click `Save edit`. Snapshot lands in the Snapshots list immediately (with timestamp). The inventory edits commit one-by-one.
7. Re-open the dialog — the new entries should be there. The removed entry should be gone.
8. Boot Elden Ring. Load the character. Verify the inventory changes are real.

If any step fails, the snapshot is in the list — restore in one click.

- [ ] **Step 4: Open the PR**

```bash
gh pr create --base master --title "feat: save-editor phase 2 — ER inventory editing" --body "$(cat <<'EOF'
## Summary

- Adds Elden Ring inventory editing (add / remove / modify) to the existing Saves dialog.
- Ships an embedded item catalog (~1500 items) sourced from ClayAmore (Apache-2.0) + alfizari (MIT) — attributed in-app + THIRD_PARTY_NOTICES.md.
- Quest-locked items get a ⚠ badge + a confirm-before-add modal. ~90 items flagged.
- Same snapshot-first safety law as MVP — every inventory edit creates a restorable snapshot first.
- Post-write byte-mask verification extends to cover the new edited ranges.

## Test plan

- [ ] `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` all green (MVP + Phase 2)
- [ ] Smoke on a real save: add Rune Arc, remove a Smithing Stone, save, boot ER, verify
- [ ] Quest-locked item: try to add Fia's Mist → confirm modal appears → cancel = no stage; confirm = stage
- [ ] Snapshot list: after a save, the new snapshot appears with the auto-label
- [ ] Restore from snapshot → inventory matches pre-edit state

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Quick reference

| Task | Output | Time estimate |
|---|---|---|
| 0 | Research doc (inventory offsets + quest-locked list) | 4–6 h (the long pole) |
| 1 | `ItemDefinition` record + tests | 30 min |
| 2 | Embedded catalog JSON + `ItemCatalog` loader + tests | 1–2 h (depends on Task 0 catalog dump quality) |
| 3 | `InventoryEntry` + `InventoryEdit` records + tests | 45 min |
| 4 | `Inventory.ReadEntries` + extended fixture + tests | 2–3 h (load-bearing) |
| 5 | `Inventory.ApplyEdit` + round-trip tests | 2–3 h |
| 6 | `EldenRingSave.WriteInventoryEdit` + byte-mask verify | 1–2 h |
| 7 | `SaveEditorService.EditInventory` | 30 min |
| 8 | `InventoryPicker` UserControl + tabbed dialog | 3–4 h |
| 9 | `QuestLockConfirmDialog` | 1 h |
| 10 | Attribution | 30 min |
| 11 | Smoke + PR | 1 h |

**Total: ~17–24 hours of focused implementation.** Task 0 is the long pole and the riskiest because guessing inventory offsets is how saves get bricked. Task 4 is load-bearing — get the read working against the fixture and Tasks 5–6 fall into place.

## Cross-game disclaimer

This plan ships **Elden Ring only**. DS3 / Sekiro / AC6 share the BND4 shape but have:

- Different held-items relative offsets per game.
- Different item ID ranges per game.
- AES-128-CBC slot encryption on the older games (MVP research note documents the AC6 key).

Cross-game inventory is phase 3+. The `EldenRingSave` class is sealed; future `DarkSouls3Save`, `SekiroSave`, etc. live alongside it. Memory: [[saves-editor-fromsoft]].
