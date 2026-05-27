# BND4 file-table walk hardening — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:subagent-driven-development` (or `superpowers:executing-plans` for solo runs). Test command: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (NEVER bare `dotnet test` — hangs building WinUI). Build (App): `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. Kill `ModManager.App.exe` first if the build complains about a locked Core DLL.

**Goal:** Replace hardcoded section offsets in `EldenRingSave` with a runtime BND4 file-table walk. The reader/writer find the SAVE_HEADER section (and each slot body) by entry NAME — `USER_DATA011` for the save header, `USER_DATA000`..`USER_DATA009` for the 10 character slots. Patch-resilient by design.

**Architecture:** New pure module `ModManager.Core.SaveEditor.FromSoft.Bnd4Reader` with a `Bnd4Entry(string Name, long DataOffset, long DataSize)` record. `EldenRingSave.ReadCharacters` and `WriteEdit` parse entries once, look up by name, derive every section offset from the result. Fixture is extended to 40-byte entries with real names + a name table. A new fixture builder simulates a future-patch save with a 12th entry to pin resilience.

**Tech Stack:** .NET 10, ModManager.Core (pure), xUnit. No new NuGets.

**Spec:** `docs/superpowers/specs/2026-05-26-saves-editor-fromsoft-bnd4-walk-design.md`

**Entry-name convention (verified from BenGrn + ClayAmore + alfizari + the MVP spec):**
- `USER_DATA000` .. `USER_DATA009` — character slots 0..9
- `USER_DATA010` — pre-DLC global region (steam ID neighbor)
- `USER_DATA011` — **SAVE_HEADER** (per-slot summaries, active-flag array, save-header MD5)
- `USER_DATA012` (post-DLC) — DLC-added entry; we don't read it, but the walk MUST enumerate it without falling over

Existing tests MUST stay green. The current fixture writes `data_offset` for each slot at `FirstSlotMd5Offset + i * SlotStride = 0x300 + i * 0x280010` — that's the same offset the hardcoded reader uses. The walk reads back the same number. Fixture extension changes byte layout, not slot positions.

---

## Task 1: Core — `Bnd4Entry` record + `Bnd4Reader.Parse` (TDD)

**Files:**
- Create: `src/ModManager.Core/SaveEditor/FromSoft/Bnd4Reader.cs`
- Create: `tests/ModManager.Tests/SaveEditor/FromSoft/Bnd4ReaderTests.cs`

The pure reader. Walks the BND4 file table starting at the header-provided offset, reads 40-byte entries, decodes UTF-16LE names from the name table.

- [ ] **Step 1: Write failing tests against a synthetic BND4**

Build a tiny BND4 in-memory in the test (small — say 3 entries, names `A`, `BB`, `CCC`, distinct data_offsets / sizes). Cases:

1. `Parse` returns exactly 3 entries, in file-table order, with the right names + offsets + sizes.
2. `Parse` throws `InvalidDataException` when magic is not `BND4`.
3. `Parse` throws `InvalidDataException` when `file_count` exceeds the buffer.
4. `Parse` throws `InvalidDataException` when an entry's `name_offset` points outside the buffer.
5. `GetByName` returns the matching entry.
6. `GetByName` throws `InvalidDataException` with a found-names list when the name is absent.

- [ ] **Step 2: Implement `Bnd4Reader`**

```csharp
namespace ModManager.Core.SaveEditor.FromSoft;

public sealed record Bnd4Entry(string Name, long DataOffset, long DataSize);

public static class Bnd4Reader
{
    private const int EntrySize = 0x28;     // 40 bytes: long-offsets + IDs + names
    private const int FileCountOffset = 0x0C;
    private const int FileHeaderOffsetOffset = 0x10;

    public static IReadOnlyList<Bnd4Entry> Parse(byte[] bytes)
    {
        // Validate magic, read file_count, read file_header_offset, loop entries:
        //   - data_offset @ +0x18 (int64 LE)
        //   - uncompressed_size @ +0x10 (int64 LE) — that's DataSize
        //   - name_offset @ +0x24 (int32 LE) — UTF-16LE null-terminated
        // Throw InvalidDataException on any malformed read.
    }

    public static Bnd4Entry GetByName(IReadOnlyList<Bnd4Entry> entries, string name)
    {
        // Linear scan; throw with "Found entries: a, b, c" on miss.
    }
}
```

UTF-16LE name read: scan from `name_offset` in 2-byte steps until `0x0000`, decode with `Encoding.Unicode`.

- [ ] **Step 3: Run tests — all green**

```
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~Bnd4ReaderTests"
```

---

## Task 2: Extend `EldenRingFixture` — write 40-byte entries with names

**Files:**
- Modify: `tests/ModManager.Tests/SaveEditor/FromSoft/EldenRingFixture.cs`

Today the fixture writes 32-byte entries (no IDs / no names). The new reader needs names. We bump the format to 40-byte entries (`0x28` per entry) + a name table immediately after the file table, populated with `USER_DATA000`..`USER_DATA009` for the 10 character slots and `USER_DATA011` for the save header.

**Constraint:** every existing test that uses the fixture (slot reads, name reads, write-edit round-trip, post-write verify) must stay green. The fixture's `data_offset` per slot stays at `FirstSlotMd5Offset + i * SlotStride` so the BND4 walk lands on the same byte ranges the existing tests expect.

- [ ] **Step 1: Add an 11th file-table entry for the save header**

The current fixture writes 10 entries. Add an 11th pointing at the save-header section: `data_offset = SaveHeadersSectionStart` (0x019003B0), `size = SaveHeadersSectionLength` (0x60000). Name it `USER_DATA011`. The fixture's `BndHeaderSize = 0x300` stays — the file table + name table both fit comfortably in that range:

- 11 entries × 40 bytes = 440 bytes (`0x1B8`), starting at 0x40 → ends at `0x1F8`.
- Name table starts at `0x1F8`. Each name is `USER_DATAxxx` = 12 chars × 2 bytes (UTF-16LE) + 2 null bytes = 26 bytes. 11 names × 26 bytes = 286 bytes (`0x11E`) → ends at `0x316`.

That overflows the 0x300 BND4 header region by 0x16 bytes. **Either** bump `BndHeaderSize` to `0x320` (and adjust `SlotsRegionStart` + `FirstSlotMd5Offset` + `FirstSlotDataOffset` accordingly — every dependent constant gets a `0x20` bump), **or** pack names tighter (skip the trailing null after `USER_DATA010` and `USER_DATA011` — UTF-16 names are length-discoverable by the next `0x0000 0x0000`, but the safer move is to keep nulls).

**Decision (lock in the plan):** bump `BndHeaderSize` to `0x320`. Cascading constant updates:

- `BndHeaderSize`: 0x300 → 0x320
- `SlotsRegionStart`: 0x300 → 0x320
- `FirstSlotMd5Offset`: 0x300 → 0x320
- `FirstSlotDataOffset`: 0x310 → 0x330

`EldenRingSave.cs` constants update in lockstep — but the cleanest move is to **make `EldenRingSave` stop referencing the fixture's constants at all** (that's Tasks 3 + 5). For now, in this task, bump both files together so existing tests pass after the format change.

- [ ] **Step 2: Write 40-byte entries with `file_id` and `name_offset`**

In `WriteBnd4Header`, expand each entry to 40 bytes:

```csharp
int entryStart = 0x40 + i * 0x28;
buffer[entryStart] = 0x40;                                      // file_flags
BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x04, 4), -1); // reserved
BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x08, 8), (long)SlotStride);     // compressed_size
BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x10, 8), (long)SlotStride);     // uncompressed_size
BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x18, 8), (long)(FirstSlotMd5Offset + i * SlotStride)); // data_offset
BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x20, 4), i);                    // file_id
BitConverter.TryWriteBytes(buffer.Slice(entryStart + 0x24, 4), nameOffsetForI);       // name_offset
```

Then add the 11th entry for `USER_DATA011` (data_offset = `SaveHeadersSectionStart`, size = `SaveHeadersSectionLength`, file_id = 11).

- [ ] **Step 3: Write the name table immediately after the file table**

Name table starts at `0x40 + 11 * 0x28 = 0x1F8`. For each slot i, write `USER_DATA000`..`USER_DATA009` as UTF-16LE + null terminator. Then write `USER_DATA011`. Track each name's start offset so the per-entry `name_offset` writes the right value.

- [ ] **Step 4: Update `BuildEmptySave` and `BuildSaveWithInventory`**

Same fixture-format bump applies. Both already share `WriteBnd4Header` — if step 2 modifies that helper, both consumers get the new format for free.

- [ ] **Step 5: Run the full test suite — green**

```
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

If any tests fail on the offset bump (`0x300` → `0x320`), find the hardcoded `0x300` / `0x310` references in `EldenRingSave` and update them. **DO NOT update them past `EldenRingSave` itself** — they're locked there too in Task 3.

---

## Task 3: `EldenRingSave.ReadCharacters` — find SAVE_HEADER by name (TDD)

**Files:**
- Modify: `src/ModManager.Core/SaveEditor/FromSoft/EldenRingSave.cs`
- Modify: `tests/ModManager.Tests/SaveEditor/FromSoft/EldenRingSaveTests.cs` (extend, don't rewrite)

Replace the hardcoded `SaveHeadersSectionStart = 0x019003B0` lookup with `Bnd4Reader.GetByName(entries, "USER_DATA011").DataOffset`. Every section read derives from there.

- [ ] **Step 1: Add a failing test that pins the lookup-by-name behavior**

A test that builds a save with the fixture, then asserts `ReadCharacters` returns the same character it would have returned before — but routed through the file-table walk. Use a moq-style observation: monkey-patch the fixture so `USER_DATA011`'s `data_offset` is shifted by some delta (e.g. add a 1-KB pad to the BND4 header before the save-header section, update the entry's data_offset to match). The hardcoded reader would read garbage; the walking reader still finds the right character.

**Why this test matters:** it's the resilience claim. If the test passes only because the data_offset still happens to be 0x019003B0, we haven't actually proven patch resilience.

- [ ] **Step 2: Refactor `ReadCharacters` to walk the file table**

```csharp
public static IReadOnlyList<CharacterSlot> ReadCharacters(string savePath)
{
    // ... existing arg-null / file-exists / read-all-bytes ...

    var entries = Bnd4Reader.Parse(bytes);
    var saveHeader = Bnd4Reader.GetByName(entries, "USER_DATA011");
    int saveHeaderStart = checked((int)saveHeader.DataOffset);
    int saveHeaderMd5 = saveHeaderStart - 0x10;

    // Relative offsets inside the save-header section (these stay constants — they're
    // the IN-SECTION layout, not the file-level offset of the section).
    int charActiveStatus = saveHeaderStart + CharActiveStatusRelative;     // was 0x01901D04
    int perSlotSummaryStart = saveHeaderStart + PerSlotSummaryRelative;    // was 0x01901D0E

    var result = new List<CharacterSlot>(SlotCount);
    for (int i = 0; i < SlotCount; i++)
    {
        var slotEntry = Bnd4Reader.GetByName(entries, $"USER_DATA{i:D3}");
        var slot = TryReadSlot(bytes, slotEntry, charActiveStatus, perSlotSummaryStart, i);
        if (slot is not null) result.Add(slot);
    }
    return result;
}
```

Add new internal constants for the **relative** offsets:

```csharp
internal const int CharActiveStatusRelative = 0x01901D04 - 0x019003B0; // = 0x196954
internal const int PerSlotSummaryRelative   = 0x01901D0E - 0x019003B0; // = 0x19695E
```

Compute them as compile-time `const`s from the existing absolute constants — pre-DLC ER puts these at `0x196954` / `0x19695E` from the save-header start. **Verify the math** before committing — these are load-bearing.

- [ ] **Step 3: Update `TryReadSlot` signature**

Take the `slotEntry` (so the MD5 + data offsets come from the file table), the resolved `charActiveStatus` + `perSlotSummaryStart` absolute offsets, and the slot index.

- [ ] **Step 4: Run the existing tests — all green**

```
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~EldenRing"
```

The existing tests should pass because the fixture's `USER_DATA011` data_offset is the same byte address as the hardcoded `SaveHeadersSectionStart`. The walk reads back the same number.

---

## Task 4: `EldenRingSave.WriteEdit` — same lookup-by-name treatment

**Files:**
- Modify: `src/ModManager.Core/SaveEditor/FromSoft/EldenRingSave.cs`

Mirror the Task 3 changes on the write path. Every offset that was a hardcoded constant now derives from `Bnd4Reader.Parse(bytes)` + `GetByName`.

- [ ] **Step 1: Refactor `WriteEdit`**

```csharp
public static void WriteEdit(string savePath, int slotIndex, CharacterEdit edit)
{
    // ... existing arg checks, file-exists, read-all-bytes, size check ...

    var entries = Bnd4Reader.Parse(bytes);
    var saveHeader = Bnd4Reader.GetByName(entries, "USER_DATA011");
    var slotEntry = Bnd4Reader.GetByName(entries, $"USER_DATA{slotIndex:D3}");
    int saveHeaderStart = checked((int)saveHeader.DataOffset);
    int charActiveStatus = saveHeaderStart + CharActiveStatusRelative;
    int perSlotSummaryStart = saveHeaderStart + PerSlotSummaryRelative;
    int slotDataStart = checked((int)slotEntry.DataOffset);
    int slotMd5Start = slotDataStart - 0x10;
    int saveHeaderMd5Start = saveHeaderStart - 0x10;

    // ... rest of the existing flow, but with all offsets resolved from the walk ...
}
```

- [ ] **Step 2: Update `VerifyPostWrite` to take the resolved offsets**

It currently uses the hardcoded `FirstSlotDataOffset + slotIndex * SlotStride` for the post-write slot slice. Take `slotDataStart` from the walk instead. The post-write re-read uses the same `Bnd4Reader.Parse` path.

- [ ] **Step 3: Run the full test suite — green**

```
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

Round-trip + post-write-verify tests still pass against the fixture.

---

## Task 5: Slot stride is now informational only — remove `SlotStride` from offset math

**Files:**
- Modify: `src/ModManager.Core/SaveEditor/FromSoft/EldenRingSave.cs`

`SlotStride = 0x280010` was a load-bearing constant that computed the offset of slot i. After Tasks 3 + 4, `slotEntry.DataOffset` does that lookup. Find every `FirstSlotMd5Offset + slotIndex * SlotStride` and `FirstSlotDataOffset + slotIndex * SlotStride` reference and route it through the file-table walk.

- [ ] **Step 1: grep for `SlotStride` usages in `EldenRingSave.cs`**

After Tasks 3 + 4 there may be lingering references in pre-edit snapshot code, post-write verify, etc.

- [ ] **Step 2: Replace each with a per-slot `Bnd4Reader.GetByName(entries, $"USER_DATA{i:D3}").DataOffset` lookup**

Keep `SlotStride` as a public constant if `EldenRingFixture` still uses it (it does — it's the in-file stride between slot start and next slot start, used as the per-entry size in the file table). But `EldenRingSave` should not compute file offsets with it.

- [ ] **Step 3: Run tests — green**

```
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

---

## Task 6: Resilience test — fixture with an extra entry (TDD)

**Files:**
- Modify: `tests/ModManager.Tests/SaveEditor/FromSoft/EldenRingFixture.cs` — add `BuildSaveWithExtraEntry`
- Create: `tests/ModManager.Tests/SaveEditor/FromSoft/Bnd4WalkResilienceTests.cs`

The whole point of this work. A fixture that simulates a future ER patch — same character data, but the BND4 file table has a 12th entry (`USER_DATA012` — DLC-style addition). Parsing + reading still works.

- [ ] **Step 1: Add `BuildSaveWithExtraEntry(uint runes, ...)` to the fixture**

Same shape as `BuildSaveWithOneCharacter`, plus:
- File table has 12 entries instead of 11 (10 slots + USER_DATA011 + USER_DATA012)
- USER_DATA012's `data_offset` points at a small region inside the existing buffer (e.g. a zero-filled 0x100-byte region we add at the very end). It doesn't need to be a real save section — the test only cares that the walk doesn't fall over.
- Name table extends to include `USER_DATA012` (one extra entry).
- Header bookkeeping (`file_count = 12`, `file_header_size = 12 * 0x28`) updates.

- [ ] **Step 2: Write tests against the new fixture**

```csharp
[Fact]
public void Reader_handles_save_with_extra_dlc_entry()
{
    var bytes = EldenRingFixture.BuildSaveWithExtraEntry(runes: 12345, vig: 20, ...);
    var path = Path.GetTempFileName();
    File.WriteAllBytes(path, bytes);
    try
    {
        var chars = EldenRingSave.ReadCharacters(path);
        Assert.Single(chars);
        Assert.Equal(12345u, chars[0].Runes);
    }
    finally { File.Delete(path); }
}

[Fact]
public void Writer_handles_save_with_extra_dlc_entry()
{
    var bytes = EldenRingFixture.BuildSaveWithExtraEntry(runes: 1, vig: 10, ...);
    var path = Path.GetTempFileName();
    File.WriteAllBytes(path, bytes);
    try
    {
        EldenRingSave.WriteEdit(path, slotIndex: 0,
            new CharacterEdit { Name = "Patched", Runes = 999, Vig = 50, ... });

        var chars = EldenRingSave.ReadCharacters(path);
        Assert.Single(chars);
        Assert.Equal("Patched", chars[0].Name);
        Assert.Equal(999u, chars[0].Runes);
    }
    finally { File.Delete(path); }
}
```

- [ ] **Step 3: Add the negative test — missing SAVE_HEADER throws loud**

```csharp
[Fact]
public void Reader_fails_loud_when_save_header_entry_is_missing()
{
    // Build a fixture where USER_DATA011 has been renamed to USER_DATA099 (simulating a patch
    // that renamed the global slot). Reader must throw InvalidDataException listing the names
    // it DID find — never silently read the wrong region.
    var bytes = EldenRingFixture.BuildSaveWithRenamedSaveHeader(...); // helper to add
    var path = Path.GetTempFileName();
    File.WriteAllBytes(path, bytes);
    try
    {
        var ex = Assert.Throws<InvalidDataException>(() => EldenRingSave.ReadCharacters(path));
        Assert.Contains("USER_DATA011", ex.Message);
        Assert.Contains("USER_DATA099", ex.Message);
    }
    finally { File.Delete(path); }
}
```

`BuildSaveWithRenamedSaveHeader` is a small fixture variant — same as `BuildSaveWithOneCharacter` but with `USER_DATA099` in the name table for what was `USER_DATA011`.

- [ ] **Step 4: Run tests — all green**

```
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

---

## Task 7: Smoke + PR

**Files:**
- (no new files)

- [ ] **Step 1: Full test run**

```
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

All green.

- [ ] **Step 2: Build the App to catch any downstream consumers**

```
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

If the App layer references the old fixture constants (it shouldn't — they're test-only), surface those references and gate the constant changes behind a slimmer public API.

- [ ] **Step 3: Manual smoke against a real save (optional, recommended)**

If a real ER save is on the dev box (NOT committed to the repo), open the Saves dialog, load the file, observe the characters list populates. Don't edit. The read path is the most likely regression class; the write path is gated by the post-write verify.

- [ ] **Step 4: Open the PR off `master`**

Independent branch, NOT stacked on any other open PR (per CLAUDE.md — stacked PRs orphan into base-branch merges). After merge: `git show origin/master:src/ModManager.Core/SaveEditor/FromSoft/Bnd4Reader.cs` to verify the work reached `master`.

- [ ] **Step 5: Log the decision**

`mcp__626Labs__manage_decisions log` against project `McSbZWG3AkLLxNAd3RJt`:

> Replaced ER save editor's hardcoded section offsets with a runtime BND4 file-table walk.
> Reader/writer locate SAVE_HEADER + slot bodies by entry name (`USER_DATA011`, `USER_DATA000`..`USER_DATA009`).
> Fail-loud `InvalidDataException` with found-names list when the expected name is absent — future ER patches that rename entries surface immediately instead of corrupting saves.
> Pinned by a `BuildSaveWithExtraEntry` fixture that simulates the DLC's 12-entry layout.

---

## What's NOT changing (anti-scope-creep)

- The MVP's read/write semantics for slot data, GA-items walk, stats encoding, MD5 placement, atomic-write pipeline, post-write verify mask.
- Stats / runes / name validation rules.
- The Saves dialog UI.
- Snapshot-first behavior.
- AES handling (none exists; ER doesn't need it).
- DS3 / Sekiro / AC6 support (out of scope; same engine family but a separate hardening pass).

## Effort estimate

**Total: small.** Pure-core change. ~150 LoC of new reader + ~50 LoC of fixture extension + ~80 LoC of new tests. No UI work. No new dependencies. Two single-file refactors in `EldenRingSave.cs`. Roughly a one-sit task for an experienced runner; subagent-driven runs may parallelize Tasks 1 (`Bnd4Reader`) and Task 2 (fixture extension) since they don't depend on each other.
