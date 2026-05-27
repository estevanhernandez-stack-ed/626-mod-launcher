# BND4 file-table walk hardening (FromSoft save editor) — Design Spec

**Date:** 2026-05-26
**Status:** Drafted (auto-mode write — pending Este review before implementation)
**Branch (proposed):** `harden/saves-editor-bnd4-walk`
**Builds on:** [`2026-05-26-saves-editor-fromsoft-mvp-design.md`](2026-05-26-saves-editor-fromsoft-mvp-design.md) — MVP that locked the ER save-editor architecture.
**Memory:** [[fromsoft-bnd4-walk]] — patch-resilience layer for ER save edits.

## Why

The MVP shipped with hardcoded section offsets — `SaveHeadersSectionStart = 0x019003B0`, slot bodies at `0x310 + i * 0x280010`, the save-header MD5 at `0x019003A0`. Today's ER patches match these constants. **Today is the load-bearing word in that sentence.**

ER patches have already shifted the BND4 file table once — Shadow of the Erdtree DLC grew the file from 11 → 12 BND4 entries. Our diagnostic confirmed the modern save reads `File count: 12`. The save-header section did not move on that patch, so the hardcoded offset still works. Next time it might. The fix is patch-resilient by design: read the BND4 file table at runtime, find the SAVE_HEADER entry by name, use its `data_offset` for the reads.

**The single biggest risk this fixes:** silent corruption on a future ER patch. Hardcoded offsets that point at the wrong region read garbage bytes as stats / runes / names, then re-MD5 over a region whose semantics we no longer understand, then atomic-write the result. The user's save is bricked, the snapshot is the line back, and we'd never have known the binary was reading the wrong section. Patch resilience moves this failure mode from "silent" to "loud" — if the entry name is missing or the layout changed, we throw `InvalidDataException` before any write.

A second, narrower risk it fixes: the slot stride formula `0x310 + i * 0x280010` assumes slot bodies start at a fixed offset and are contiguous. The BND4 layout doesn't promise that — it's just true today. Reading `data_offset` from the file table per slot makes the slot reads self-locating too.

## Goal

`EldenRingSave.ReadCharacters` and `EldenRingSave.WriteEdit` resolve every save-section offset by walking the BND4 file table at runtime instead of using hardcoded constants. Specifically:

1. **`Bnd4Reader.Parse(byte[])` returns `IReadOnlyList<Bnd4Entry>`** — one entry per file in the BND4 archive, with `Name`, `DataOffset`, `DataSize`.
2. **`EldenRingSave` locates the SAVE_HEADER entry by name (`USER_DATA011`)** and reads / writes the per-slot summary region at offsets relative to that entry's `DataOffset`.
3. **`EldenRingSave` locates each slot's body by name (`USER_DATA000`..`USER_DATA009`)** instead of computing `0x310 + i * 0x280010`. The per-slot 16-byte MD5 lives immediately before `DataOffset` (still 16 bytes; that's the per-slot stride from the slot data, not the file table).
4. **Missing entries fail loud.** If `USER_DATA011` is not in the file table (renamed in a future patch), throw `InvalidDataException` with the entry names we DID find, so a future-us can bisect quickly.

Out of scope for this hardening pass:
- BND4 v4 format flag interpretation. We assume long-offsets + IDs + names (the ER configuration). If a future patch changes the format flags, that's a separate hardening pass.
- Compressed entries. ER save entries are stored, not compressed; `compressed_size == uncompressed_size`. We read uncompressed_size and skip the decompress path entirely.
- DS3 / Sekiro / AC6 saves. The BND4 layout transfers, but those games also need AES decrypt — that's a different scope.
- Renaming the constants in `EldenRingFixture`. The fixture's constants stay as named anchors for the *content* of each section; only the EldenRingSave reader stops treating them as absolute file offsets.

## Approach

### `Bnd4Reader` — the new pure module

Lives at `src/ModManager.Core/SaveEditor/FromSoft/Bnd4Reader.cs`. No Electron, no UI, no real-save bytes in tests. The shape:

```csharp
namespace ModManager.Core.SaveEditor.FromSoft;

/// <summary>One file-table entry in a BND4 archive — name + payload location.</summary>
public sealed record Bnd4Entry(string Name, long DataOffset, long DataSize);

/// <summary>BND4 v4 file-table reader. Walks the file table starting at 0x40, reads each
/// entry's data_offset / sizes / name_offset, decodes the name from the name table as UTF-16LE.
/// Throws InvalidDataException if the magic doesn't match BND4, file_count is implausible,
/// or any entry's name_offset is outside the buffer.</summary>
public static class Bnd4Reader
{
    public static IReadOnlyList<Bnd4Entry> Parse(byte[] bytes);
    public static Bnd4Entry GetByName(IReadOnlyList<Bnd4Entry> entries, string name); // throws if missing
}
```

### BND4 v4 file-table layout (the bytes we walk)

Per `docs/superpowers/research/2026-05-26-fromsoft-save-libs.md` and `ClayAmore/ER-Save-Editor/src/util/bnd4.rs`:

**Header at offset 0x00 (we read just what we need):**
- `0x00` (4): magic = ASCII `"BND4"`
- `0x0C` (4): `file_count` (int32 LE)
- `0x10` (8): `file_header_offset` (int64 LE) — always 0x40 for ER saves; we honor whatever the header says
- `0x30` (1): `unicode` flag — 1 for ER saves; names are UTF-16LE
- `0x31` (1): `format` byte — flag bits. ER configuration = long offsets + IDs + names; entry size = 40 bytes (0x28)

**Per-entry layout (40 bytes for ER's long-offsets+IDs+names configuration):**

| Offset | Size | Field |
|---|---|---|
| 0x00 | 1 | `file_flags` |
| 0x01 | 3 | padding |
| 0x04 | 4 | `reserved` (int32, always -1) |
| 0x08 | 8 | `compressed_size` (int64 LE) |
| 0x10 | 8 | `uncompressed_size` (int64 LE) |
| 0x18 | 8 | `data_offset` (int64 LE) |
| 0x20 | 4 | `file_id` (int32 LE) |
| 0x24 | 4 | `name_offset` (int32 LE) |

Total: 40 bytes. The **existing fixture writes 32-byte entries** (it stops at `data_offset` because the original MVP reader didn't need IDs / names). The fixture must extend to 40-byte entries with real names plus a name table — that's Task 2.

**Name table** (lives after the file-table region, typically starting around offset 0x180 in ER saves but always pointed to by each entry's `name_offset`): UTF-16LE, null-terminated. We read from `name_offset` until the first `0x0000 0x0000` pair.

### Entry naming convention (the load-bearing detail)

Verified from the MVP design spec, `BenGrn/EldenRingSaveCopier` source, and `alfizari/Elden-Ring-Save-Editor`:

- Slots 0..9: **`USER_DATA000`** through **`USER_DATA009`** — the 10 character slots.
- Slot 10: **`USER_DATA010`** — historically a small "menu state" / steam-ID region; on the post-DLC 12-entry save it's still present.
- Slot 11: **`USER_DATA011`** — the **SAVE_HEADER** — the global slot containing per-slot name/level/playtime summaries + the active-flag array + the steam ID + the save-header MD5 region. This is the entry the editor's per-slot summary reads/writes against.
- Slot 12 (post-DLC): a 12th `USER_DATAxxx` entry that the DLC added. We don't read it — we just need the walk to not fall over when it's present.

Names are UTF-16LE encoded ASCII — `U` is `0x55 0x00`, etc. Our diagnostic earlier in chat decoded them as garbage CJK (`乂㑄`) because the diagnostic walked 32-byte entries; the name_offsets at +0x18 and +0x1C were treated as 64-bit data_offsets, putting the name pointer outside the buffer. The 40-byte stride fixes that.

### Integration points in EldenRingSave

`ReadCharacters` flow becomes:

1. Read all bytes.
2. `var entries = Bnd4Reader.Parse(bytes);` — throws if BND4 magic missing / file_count implausible.
3. `var saveHeader = Bnd4Reader.GetByName(entries, "USER_DATA011");` — throws `InvalidDataException` with a list of found names if missing.
4. For each slot i in 0..9: `var slot = Bnd4Reader.GetByName(entries, $"USER_DATA{i:D3}");` — same throw rule.
5. Compute `slotMd5Offset = (int)slot.DataOffset - 0x10` (the MD5 lives in the 16 bytes immediately before the entry's data — that's the in-slot layout, not part of the file table).
6. Compute `slotDataOffset = (int)slot.DataOffset`. The slot body is `SlotData.SlotSize` bytes starting there.
7. Compute `saveHeaderDataOffset = (int)saveHeader.DataOffset`. The save-header MD5 lives at `saveHeaderDataOffset - 0x10`. Per-slot summaries, active-flag array, char-name regions are all at offsets relative to `saveHeaderDataOffset` (the existing constants `CharActiveStatusOffset - SaveHeadersSectionStart`, `PerSlotSummaryStart - SaveHeadersSectionStart`, etc., become the new relative constants).

`WriteEdit` flow takes the same offsets from the same file-table walk, then proceeds with the existing patch / MD5 recompute / atomic-write flow.

### What happens if an entry name changed in a future patch

We throw `InvalidDataException`:

```
Save file is a valid BND4 archive but is missing the expected SAVE_HEADER entry 'USER_DATA011'.
Found entries: USER_DATA000, USER_DATA001, ..., USER_DATA009, USER_DATA010, USER_DATA013.
The Elden Ring save format may have changed in a recent patch — please report the patch
version so the editor can be updated. Your save was NOT modified.
```

The "Your save was NOT modified" line is the critical reassurance. We fail at read time, before the user has even clicked Edit, so there's no atomic-write to roll back. The snapshot-first safety law from the MVP still applies on top of this — but with the file-table walk in place, the throw lands earlier in the flow.

### Risk

**Low.** Three reasons:

1. The file-table walk is purely additive — we add `Bnd4Reader.Parse` as a new pure module with its own unit tests. The existing tests for `EldenRingSave` keep passing because the fixture's slot 0 still ends up at the same `DataOffset` (the fixture's entry table already writes `data_offset = FirstSlotMd5Offset + i * SlotStride` per slot; the walk reads the same number back).
2. The fixture extension (40-byte entries + name table) is a one-time format-bump in `EldenRingFixture.WriteBnd4Header`. Every existing test calls `BuildSaveWithOneCharacter` / `BuildSaveWithInventory` / `BuildEmptySave` and gets the new format for free.
3. A new test against a fixture with a **12th entry** (`USER_DATA012`) — simulating the DLC patch — pins the resilience claim. If a future change to `Bnd4Reader` breaks on extra entries, the test catches it.

The one place this could go wrong: if a future patch renames an entry instead of adding one, the file-table walk doesn't help — we throw `InvalidDataException` and the user reports it. The mitigation is the throw message itself: it dumps every entry name we found, so the patch-update PR is one find/replace away.

## Operating-laws check

Honors the law set from CLAUDE.md and the roadmap:

- **Honor the builders.** No behavior change for honoring; attribution to `BenGrn` and `ClayAmore` from the MVP carries forward (the BND4 format work credits both).
- **No baked keys.** N/A — no keys involved in BND4 parsing.
- **Reversible / safe.** The atomic-write + post-write verify pipeline from the MVP is unchanged. We fail loud (throw) on a missing entry instead of writing garbage.
- **Pure-core / thin-shell.** `Bnd4Reader` is a pure static class. No `using Electron;` equivalent — it's pure `byte[]` in, records out.
- **Test-first.** The plan opens with `Bnd4Reader` tests against a synthetic BND4 before any reader code lands.

## What's deferred

- A BND4 *writer* — we only read here. The MVP writes save-section bytes in place; it doesn't rewrite the file table. If a future feature needs to add entries (e.g. a custom save-mod), that's a separate spec.
- AES-CBC paths for DS3 / Sekiro / AC6. Same engine family, different scope.
- BND4 format flag interpretation beyond ER's known configuration. If a non-ER FromSoft save ever feeds in, the read will throw with a clear-enough message that the next implementer can diagnose.
