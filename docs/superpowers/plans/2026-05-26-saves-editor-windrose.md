# Windrose Save Editor (MVP) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax. Test command: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (NEVER bare `dotnet test` — hangs building WinUI). Build (App): `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. Kill `ModManager.App.exe` first if the build complains about a locked Core DLL.

**Goal:** Ship a working Windrose save editor with name + level + XP + gold + attribute editing, embedded in the existing Saves dialog's Characters section, with mandatory pre-edit snapshots. The save format is RocksDB; reads/writes go through the official `RocksDB.NET` NuGet (Apache-2.0, native binaries bundled in self-contained publish).

**Architecture:** Pure-core format layer (`ModManager.Core.SaveEditor.Windrose.*`) — fully unit-testable against a temp-dir RocksDB fixture, no real save file in the repo. `SaveEditorService` becomes engine-aware: dispatches to FromSoft or Windrose based on `GameEntry.Engine` + `SteamAppId`. Neutral `CharacterRow` DTO at the service boundary so the Saves dialog stops branching on engine.

**Tech Stack:** .NET 10, ModManager.Core (pure), ModManager.App (WinUI 3), xUnit. New NuGet: `RocksDB.NET`.

**Spec:** `docs/superpowers/specs/2026-05-26-saves-editor-windrose-design.md`

---

## Task 0: Format research — pin the RocksDB key schema + payload offsets

**Files:**
- Create: `docs/superpowers/research/2026-05-26-windrose-save-format.md`

**This is the load-bearing task.** Every subsequent task depends on the key + offsets pinned here. If Task 0's findings are wrong, Tasks 3 / 4 / 6 surface it through round-trip tests — but the cheaper move is to verify carefully here.

**Reference sources (in order of authority):**

1. **Chris971991/WindroseCharacterEditor** on GitHub (MIT). C++ ImGui editor — the source code is the single best reference for the on-disk byte layout. Read: the key naming, the per-character struct, the offsets within the binary blob, the attribute order.
2. **WSE Project Save Editor GUI 1.3** — Python + wxPython source. Bundles `rocksdb.dll`. The Python is more readable than C++ for offset extraction; cross-check against Chris971991.
3. **WSE Project's standalone HTML Item ID Database** — 1.17 MB, 1,268 items × 20 categories × 5 rarities. Source the **item catalog** for Task 8.

**Deliverable — fill in `docs/superpowers/research/2026-05-26-windrose-save-format.md`:**

```markdown
# Windrose save format — 2026-05-26

## Save folder location

`%LOCALAPPDATA%\..\<R5 install root>\Saved\SaveProfiles\<steamid>\`
(Already resolved by Ludusavi + Steam-user-id wiring shipped in PR #45.)

## RocksDB store contents

Standard RocksDB files: `CURRENT`, `MANIFEST-*`, `*.sst`, `LOG`, `OPTIONS-*`, `LOCK`.

## Key schema

| Key pattern | Type | What it stores |
|---|---|---|
| `(fill in from Chris971991)` | binary | per-character payload |
| `(index/manifest key, if one)` | binary or string | list of character GUIDs |
| `(version stamp / sentinel keys)` | — | DO NOT MODIFY |

## Per-character payload byte layout

Fill in offsets, types, lengths. Use little-endian unless noted otherwise.

| Offset | Type | Field |
|---|---|---|
| 0x00 | (TBD) | Name length / name string |
| ... | ... | Level |
| ... | ... | XP |
| ... | ... | Gold |
| ... | ... | Attribute 0 |
| ... | ... | Attribute 1 |
| ... | ... | (...remaining attributes...) |

## Attribute order + count

From Chris971991's source. Locked here as the canonical list:

1. (e.g.) Strength
2. Dexterity
3. ...

## Value caps (for validation)

| Field | Min | Max | Source |
|---|---|---|---|
| Level | 1 | 100 | (verify) |
| XP | 0 | (cap) | (verify) |
| Gold | 0 | (cap) | (verify) |
| Each attribute | 1 | 99 | (verify) |
| Name length | 1 | (cap) chars | (verify) |

## Sentinel keys to leave alone

(Anything that's NOT a per-character key. Listed explicitly.)

## Sources cited

- Chris971991/WindroseCharacterEditor — (URL) — MIT
- WSE Project — (URL) — license TBD
- WSE Project Item DB HTML — (URL)
- facebook/rocksdb (the C++ engine RocksDB.NET wraps) — BSD-3-Clause

## Open questions / risks

(Anything the references conflict on, or that's covered by neither.)
```

- [ ] **Step 1:** Locate Chris971991's repo. Read the editor source's data-layout headers + the struct that describes one character. Capture offsets verbatim.
- [ ] **Step 2:** Locate WSE Project's repo. Cross-check the same offsets in its Python serialization code.
- [ ] **Step 3:** Download WSE Project's HTML item DB. Save raw HTML to `docs/superpowers/research/raw/windrose-items.html` (NOT under `src/` — research staging only). Confirm structure parses to JSON in Task 8.
- [ ] **Step 4:** Fill in the research doc with sources, key schema, offsets, value caps.
- [ ] **Step 5:** Commit the research doc.

```bash
git add docs/superpowers/research/2026-05-26-windrose-save-format.md
git commit -m "research: pin Windrose RocksDB key schema + character payload offsets"
```

The later tasks reference this doc by absolute offsets. If a task fails its round-trip test, the failure points back here — fix the offsets in the doc + the constants in `WindroseCharacterPayload.cs` together, never just the code.

---

## Task 1: Add `RocksDB.NET` NuGet ref + verify portable bundling

**Files:**
- Modify: `src/ModManager.Core/ModManager.Core.csproj`

Add the dependency. Confirm it bundles the native `librocksdb.dll` into the self-contained publish (zero user-installed prerequisites per the keystone deps rule).

- [ ] **Step 1: Add the package reference**

Edit `src/ModManager.Core/ModManager.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="SharpCompress" Version="0.48.1" />
    <PackageReference Include="RocksDB.NET" Version="9.4.1" />
  </ItemGroup>

</Project>
```

(Pin to the latest stable version that targets net8.0+ / has win-x64 native binaries. Confirm via `dotnet package search RocksDB.NET` before committing.)

- [ ] **Step 2: Restore + build the Core**

```bash
dotnet restore src/ModManager.Core/ModManager.Core.csproj
dotnet build src/ModManager.Core/ModManager.Core.csproj
```

Expected: build passes; warnings about the native binary location are OK.

- [ ] **Step 3: Smoke the bundling with a publish**

```bash
dotnet publish src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -c Release --self-contained true -r win-x64
```

Inspect the publish output. Expected: `runtimes/win-x64/native/librocksdb.dll` (or equivalent) is present in the published folder. If it's missing, RocksDB.NET's targets aren't flowing through the App project; add a transitive reference or `<RestoreAdditionalProjectSources>` per the package's README.

If the publish fails or the native binary doesn't land, **STOP and resolve before continuing.** The deps rule is non-negotiable: a user can't be asked to install RocksDB themselves.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.Core/ModManager.Core.csproj
git commit -m "feat(save-editor): add RocksDB.NET (Apache-2.0) for Windrose save IO"
```

---

## Task 2: Core — `WindroseCharacterSlot` + `WindroseCharacterEdit` model types

**Files:**
- Create: `src/ModManager.Core/SaveEditor/Windrose/WindroseCharacterSlot.cs`
- Create: `src/ModManager.Core/SaveEditor/Windrose/WindroseCharacterEdit.cs`
- Create: `tests/ModManager.Tests/SaveEditor/Windrose/WindroseCharacterSlotTests.cs`

Pure data shapes. No I/O. Mirrors the FromSoft `CharacterSlot` / `CharacterEdit` pattern.

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core.SaveEditor.Windrose;

namespace ModManager.Tests.SaveEditor.Windrose;

public class WindroseCharacterSlotTests
{
    [Fact]
    public void WindroseCharacterSlot_carries_identity_level_currency_and_stats()
    {
        var slot = new WindroseCharacterSlot(
            CharacterId: "e3b0c442-98fc-1c14-9afb-f4c8996fb924",
            Name: "Yuka",
            Level: 42,
            Xp: 125_000,
            Gold: 18_500,
            Attributes: new[]
            {
                ("STR", 25), ("DEX", 22), ("CON", 18), ("INT", 14),
            });

        Assert.Equal("Yuka", slot.Name);
        Assert.Equal(42, slot.Level);
        Assert.Equal(125_000, slot.Xp);
        Assert.Equal(18_500, slot.Gold);
        Assert.Equal(4, slot.Attributes.Count);
        Assert.Equal(("STR", 25), slot.Attributes[0]);
    }

    [Fact]
    public void WindroseCharacterEdit_carries_changed_fields_only()
    {
        var edit = new WindroseCharacterEdit(
            Name: "Renamed",
            Level: 50,
            Xp: 200_000,
            Gold: 1_000_000,
            Attributes: new[]
            {
                ("STR", 30), ("DEX", 22), ("CON", 18), ("INT", 14),
            });

        Assert.Equal("Renamed", edit.Name);
        Assert.Equal(50, edit.Level);
        Assert.Equal(1_000_000, edit.Gold);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (compile-fail counts as red)**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~WindroseCharacterSlotTests"
```

Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement `WindroseCharacterSlot.cs`**

```csharp
namespace ModManager.Core.SaveEditor.Windrose;

/// <summary>
/// One Windrose character's read-only state. Returned from <see cref="WindroseSave.ReadCharacters"/>.
/// Attributes are name/value pairs to keep the model open to the actual attribute count Task 0
/// pins (Chris971991's editor exposes N attributes; we mirror that list, in order, as labels).
/// </summary>
public sealed record WindroseCharacterSlot(
    string CharacterId,                            // GUID — RocksDB key suffix
    string Name,
    int Level,
    int Xp,
    int Gold,
    IReadOnlyList<(string Label, int Value)> Attributes);
```

- [ ] **Step 4: Implement `WindroseCharacterEdit.cs`**

```csharp
namespace ModManager.Core.SaveEditor.Windrose;

/// <summary>
/// The set of fields one edit changes. All fields are required — the UI pre-fills with the
/// current values; "unchanged" is just the same value re-applied. Uniform write path = no
/// bug class where editing only one field zeros the rest.
/// </summary>
public sealed record WindroseCharacterEdit(
    string Name,
    int Level,
    int Xp,
    int Gold,
    IReadOnlyList<(string Label, int Value)> Attributes);
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~WindroseCharacterSlotTests"
```

Expected: 2/2 PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/SaveEditor/Windrose/WindroseCharacterSlot.cs \
        src/ModManager.Core/SaveEditor/Windrose/WindroseCharacterEdit.cs \
        tests/ModManager.Tests/SaveEditor/Windrose/WindroseCharacterSlotTests.cs
git commit -m "feat(save-editor): WindroseCharacterSlot + WindroseCharacterEdit model types"
```

---

## Task 3: Core — `WindroseKeys` + `WindroseCharacterPayload` (the load-bearing offsets)

**Files:**
- Create: `src/ModManager.Core/SaveEditor/Windrose/WindroseKeys.cs`
- Create: `src/ModManager.Core/SaveEditor/Windrose/WindroseCharacterPayload.cs`
- Create: `tests/ModManager.Tests/SaveEditor/Windrose/WindrosePayloadTests.cs`

The constants from Task 0 land here. **Every offset is documented with a comment citing the Task 0 research doc by section.** If a real Windrose save proves an offset wrong, fix THIS file plus the research doc — together, never apart.

- [ ] **Step 1: Write the failing test (payload round-trip)**

```csharp
using ModManager.Core.SaveEditor.Windrose;

namespace ModManager.Tests.SaveEditor.Windrose;

public class WindrosePayloadTests
{
    [Fact]
    public void Name_round_trips_through_payload()
    {
        var bytes = new byte[WindroseCharacterPayload.PayloadSize];
        WindroseCharacterPayload.WriteName(bytes, "Yuka");
        Assert.Equal("Yuka", WindroseCharacterPayload.ReadName(bytes));
    }

    [Fact]
    public void Level_xp_gold_round_trip_as_int32_little_endian()
    {
        var bytes = new byte[WindroseCharacterPayload.PayloadSize];
        WindroseCharacterPayload.WriteLevel(bytes, 42);
        WindroseCharacterPayload.WriteXp(bytes, 125_000);
        WindroseCharacterPayload.WriteGold(bytes, 18_500);

        Assert.Equal(42, WindroseCharacterPayload.ReadLevel(bytes));
        Assert.Equal(125_000, WindroseCharacterPayload.ReadXp(bytes));
        Assert.Equal(18_500, WindroseCharacterPayload.ReadGold(bytes));
    }

    [Fact]
    public void Attributes_round_trip_in_canonical_order()
    {
        var bytes = new byte[WindroseCharacterPayload.PayloadSize];
        var values = WindroseCharacterPayload.AttributeOrder
            .Select((label, i) => (label, i + 10))
            .ToArray();

        WindroseCharacterPayload.WriteAttributes(bytes, values);
        var read = WindroseCharacterPayload.ReadAttributes(bytes);

        Assert.Equal(values, read);
    }

    [Fact]
    public void Long_name_truncates_to_max_no_overrun()
    {
        var bytes = new byte[WindroseCharacterPayload.PayloadSize];
        WindroseCharacterPayload.WriteName(bytes, new string('A', 1000));
        var read = WindroseCharacterPayload.ReadName(bytes);
        Assert.True(read.Length <= WindroseCharacterPayload.NameMaxLength);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~WindrosePayloadTests"
```

Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement `WindroseKeys.cs`**

```csharp
namespace ModManager.Core.SaveEditor.Windrose;

/// <summary>
/// RocksDB key schema for Windrose saves. Constants reflect the schema pinned in
/// <c>docs/superpowers/research/2026-05-26-windrose-save-format.md</c> §"Key schema".
/// </summary>
internal static class WindroseKeys
{
    // Prefix for per-character keys. Task 0 confirms the exact bytes; placeholder shape
    // below — replace with the pinned value. Common patterns observed in the references:
    //   "character/<guid>" or "Profile/<guid>" or a binary tag byte + GUID.
    public const string CharacterKeyPrefix = "character/";

    // Whitelist of sentinel keys we MUST NOT modify under any circumstance.
    public static readonly IReadOnlyList<string> SentinelKeys = new[]
    {
        // From Task 0 §"Sentinel keys to leave alone".
        "version",
        // ...
    };

    public static string BuildCharacterKey(string characterId)
        => CharacterKeyPrefix + characterId;

    public static bool IsCharacterKey(ReadOnlySpan<byte> key)
    {
        if (key.Length < CharacterKeyPrefix.Length) return false;
        var prefix = System.Text.Encoding.UTF8.GetBytes(CharacterKeyPrefix);
        return key[..prefix.Length].SequenceEqual(prefix);
    }

    public static string ExtractCharacterId(ReadOnlySpan<byte> key)
        => System.Text.Encoding.UTF8.GetString(key[CharacterKeyPrefix.Length..]);
}
```

- [ ] **Step 4: Implement `WindroseCharacterPayload.cs`**

```csharp
using System.Buffers.Binary;
using System.Text;

namespace ModManager.Core.SaveEditor.Windrose;

/// <summary>
/// Reads + writes the load-bearing fields within ONE character's RocksDB value blob.
///
/// All offsets are little-endian. Pinned from
/// <c>docs/superpowers/research/2026-05-26-windrose-save-format.md</c> §"Per-character payload
/// byte layout". If a real save proves these wrong, fix THIS constants table AND the research
/// doc together — never one without the other.
/// </summary>
internal static class WindroseCharacterPayload
{
    // Placeholder size — Task 0 pins the actual character record size. Likely a fixed-size
    // header + variable-size name. Implementer adjusts based on what Chris971991's editor
    // declares.
    public const int PayloadSize = 1024;

    // Offsets (PLACEHOLDERS — replace with Task 0 values).
    private const int OffsetNameLength = 0x00;        // uint16 little-endian, bytes of UTF-8 name
    private const int OffsetName       = 0x02;        // UTF-8 string
    public  const int NameMaxLength    = 32;          // chars (verify in Task 0)
    private const int OffsetLevel      = 0x40;        // int32 LE
    private const int OffsetXp         = 0x44;        // int32 LE
    private const int OffsetGold       = 0x48;        // int32 LE
    private const int OffsetAttrsBase  = 0x4C;        // contiguous int32 LE in AttributeOrder

    /// <summary>The canonical attribute order. Used as the contract between read+write
    /// paths and the UI's edit dialog. Task 0 §"Attribute order + count" pins this list.</summary>
    public static readonly IReadOnlyList<string> AttributeOrder = new[]
    {
        "Strength",
        "Dexterity",
        "Constitution",
        "Intelligence",
        // ...complete list from Task 0
    };

    public static string ReadName(ReadOnlySpan<byte> blob)
    {
        int len = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(OffsetNameLength, 2));
        len = Math.Min(len, NameMaxLength * 4); // UTF-8 worst case
        return Encoding.UTF8.GetString(blob.Slice(OffsetName, len));
    }

    public static void WriteName(Span<byte> blob, string name)
    {
        var encoded = Encoding.UTF8.GetBytes(name);
        var truncated = encoded.Length > NameMaxLength ? NameMaxLength : encoded.Length;

        // Clear the name region.
        blob.Slice(OffsetName, NameMaxLength * 4).Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(blob.Slice(OffsetNameLength, 2), (ushort)truncated);
        encoded.AsSpan(0, truncated).CopyTo(blob.Slice(OffsetName, truncated));
    }

    public static int ReadLevel(ReadOnlySpan<byte> blob)
        => BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(OffsetLevel, 4));

    public static void WriteLevel(Span<byte> blob, int level)
        => BinaryPrimitives.WriteInt32LittleEndian(blob.Slice(OffsetLevel, 4), level);

    public static int ReadXp(ReadOnlySpan<byte> blob)
        => BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(OffsetXp, 4));

    public static void WriteXp(Span<byte> blob, int xp)
        => BinaryPrimitives.WriteInt32LittleEndian(blob.Slice(OffsetXp, 4), xp);

    public static int ReadGold(ReadOnlySpan<byte> blob)
        => BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(OffsetGold, 4));

    public static void WriteGold(Span<byte> blob, int gold)
        => BinaryPrimitives.WriteInt32LittleEndian(blob.Slice(OffsetGold, 4), gold);

    public static IReadOnlyList<(string Label, int Value)> ReadAttributes(ReadOnlySpan<byte> blob)
    {
        var result = new (string, int)[AttributeOrder.Count];
        for (int i = 0; i < AttributeOrder.Count; i++)
        {
            var v = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(OffsetAttrsBase + i * 4, 4));
            result[i] = (AttributeOrder[i], v);
        }
        return result;
    }

    public static void WriteAttributes(Span<byte> blob, IReadOnlyList<(string Label, int Value)> values)
    {
        // Caller is expected to pass the full canonical-order list; we write by index.
        for (int i = 0; i < AttributeOrder.Count && i < values.Count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(blob.Slice(OffsetAttrsBase + i * 4, 4), values[i].Value);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~WindrosePayloadTests"
```

Expected: 4/4 PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/SaveEditor/Windrose/WindroseKeys.cs \
        src/ModManager.Core/SaveEditor/Windrose/WindroseCharacterPayload.cs \
        tests/ModManager.Tests/SaveEditor/Windrose/WindrosePayloadTests.cs
git commit -m "feat(save-editor): WindroseKeys + WindroseCharacterPayload offset helpers"
```

---

## Task 4: Core — `WindroseSave` reader + writer (RocksDB IO + round-trip)

**Files:**
- Create: `src/ModManager.Core/SaveEditor/Windrose/WindroseSave.cs`
- Create: `tests/ModManager.Tests/SaveEditor/Windrose/WindroseFixture.cs`
- Create: `tests/ModManager.Tests/SaveEditor/Windrose/WindroseSaveTests.cs`

Public API. `ReadCharacters(savePath)` opens the RocksDB store read-only, walks character keys, deserializes. `WriteEdit(savePath, characterId, edit)` opens read-write, mutates one character's blob, closes — with post-write verification.

The fixture builds a temp-dir RocksDB store containing one known character. Tests run end-to-end (real RocksDB, no mocks); they're slower than pure-byte tests but exercise the actual write path.

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using ModManager.Core.SaveEditor.Windrose;

namespace ModManager.Tests.SaveEditor.Windrose;

public class WindroseSaveTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "windrose-save-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    [Fact]
    public void Read_returns_one_character_with_fixture_values()
    {
        Directory.CreateDirectory(_tmp);
        WindroseFixture.BuildOneCharacterStore(_tmp,
            characterId: "abc-123",
            name: "Yuka", level: 42, xp: 125_000, gold: 18_500,
            attributes: new[] { ("Strength", 25), ("Dexterity", 22), ("Constitution", 18), ("Intelligence", 14) });

        var chars = WindroseSave.ReadCharacters(_tmp);

        var slot = Assert.Single(chars);
        Assert.Equal("Yuka", slot.Name);
        Assert.Equal(42, slot.Level);
        Assert.Equal(18_500, slot.Gold);
        Assert.Equal(("Strength", 25), slot.Attributes[0]);
    }

    [Fact]
    public void WriteEdit_persists_new_values_and_round_trips()
    {
        Directory.CreateDirectory(_tmp);
        WindroseFixture.BuildOneCharacterStore(_tmp,
            characterId: "abc-123",
            name: "Yuka", level: 1, xp: 0, gold: 100,
            attributes: new[] { ("Strength", 10), ("Dexterity", 10), ("Constitution", 10), ("Intelligence", 10) });

        WindroseSave.WriteEdit(_tmp, characterId: "abc-123", new WindroseCharacterEdit(
            Name: "Renamed",
            Level: 50,
            Xp: 200_000,
            Gold: 1_000_000,
            Attributes: new[] { ("Strength", 30), ("Dexterity", 25), ("Constitution", 20), ("Intelligence", 15) }));

        var chars = WindroseSave.ReadCharacters(_tmp);
        var slot = Assert.Single(chars);
        Assert.Equal("Renamed", slot.Name);
        Assert.Equal(50, slot.Level);
        Assert.Equal(1_000_000, slot.Gold);
        Assert.Equal(("Strength", 30), slot.Attributes[0]);
    }

    [Fact]
    public void WriteEdit_with_unchanged_values_round_trips_to_byte_identical_payload()
    {
        Directory.CreateDirectory(_tmp);
        WindroseFixture.BuildOneCharacterStore(_tmp,
            characterId: "abc-123",
            name: "Yuka", level: 42, xp: 125_000, gold: 18_500,
            attributes: new[] { ("Strength", 25), ("Dexterity", 22), ("Constitution", 18), ("Intelligence", 14) });

        var before = WindroseSave.ReadCharacters(_tmp)[0];
        WindroseSave.WriteEdit(_tmp, characterId: "abc-123", new WindroseCharacterEdit(
            Name: before.Name, Level: before.Level, Xp: before.Xp, Gold: before.Gold,
            Attributes: before.Attributes));

        var after = WindroseSave.ReadCharacters(_tmp)[0];
        Assert.Equal(before, after);
    }

    [Fact]
    public void WriteEdit_throws_when_character_id_missing()
    {
        Directory.CreateDirectory(_tmp);
        WindroseFixture.BuildOneCharacterStore(_tmp,
            characterId: "abc-123",
            name: "x", level: 1, xp: 0, gold: 0,
            attributes: new[] { ("Strength", 10), ("Dexterity", 10), ("Constitution", 10), ("Intelligence", 10) });

        Assert.Throws<KeyNotFoundException>(() =>
            WindroseSave.WriteEdit(_tmp, characterId: "does-not-exist",
                new WindroseCharacterEdit("y", 1, 0, 0,
                    new[] { ("Strength", 10), ("Dexterity", 10), ("Constitution", 10), ("Intelligence", 10) })));
    }
}
```

- [ ] **Step 2: Implement `WindroseFixture.cs`**

```csharp
using System.IO;
using RocksDbSharp;
using ModManager.Core.SaveEditor.Windrose;

namespace ModManager.Tests.SaveEditor.Windrose;

/// <summary>Builds a minimal RocksDB store in the given temp dir containing ONE character keyed
/// by the given GUID. The fixture writes a single record using the same payload layout
/// <see cref="WindroseSave"/> reads from — that's the round-trip contract.</summary>
internal static class WindroseFixture
{
    public static void BuildOneCharacterStore(
        string storeDir,
        string characterId,
        string name, int level, int xp, int gold,
        IReadOnlyList<(string Label, int Value)> attributes)
    {
        Directory.CreateDirectory(storeDir);

        var options = new DbOptions().SetCreateIfMissing(true);
        using var db = RocksDb.Open(options, storeDir);

        var payload = new byte[WindroseCharacterPayload.PayloadSize];
        WindroseCharacterPayload.WriteName(payload, name);
        WindroseCharacterPayload.WriteLevel(payload, level);
        WindroseCharacterPayload.WriteXp(payload, xp);
        WindroseCharacterPayload.WriteGold(payload, gold);
        WindroseCharacterPayload.WriteAttributes(payload, attributes);

        db.Put(System.Text.Encoding.UTF8.GetBytes(WindroseKeys.BuildCharacterKey(characterId)), payload);
    }
}
```

- [ ] **Step 3: Implement `WindroseSave.cs`**

```csharp
using RocksDbSharp;

namespace ModManager.Core.SaveEditor.Windrose;

/// <summary>
/// Public API for reading and editing Windrose save files. The save is a RocksDB store at
/// <c>R5/Saved/SaveProfiles/&lt;steamid&gt;/</c>.
///
/// Read posture: <see cref="ReadCharacters"/> opens read-only (no LOCK file grabbed) so the
/// game can still launch right after.
///
/// Write posture: <see cref="WriteEdit"/> opens read-write; the game must be closed. A
/// lock-conflict surfaces as an <see cref="IOException"/> with a friendly message — the
/// caller (SaveEditorService) translates it to status text.
///
/// Format: per-character RocksDB keys (see <see cref="WindroseKeys"/>) point at fixed-size
/// payload blobs (see <see cref="WindroseCharacterPayload"/>). The references in
/// <c>docs/superpowers/research/2026-05-26-windrose-save-format.md</c> pin the byte layout.
/// </summary>
public static class WindroseSave
{
    /// <summary>Read every character in the store. Returned in key-iteration order
    /// (RocksDB sorts keys lexicographically; the per-character GUIDs are stable IDs, so this
    /// is deterministic — callers can render in this order).</summary>
    public static IReadOnlyList<WindroseCharacterSlot> ReadCharacters(string savePath)
    {
        var options = new DbOptions();
        using var db = RocksDb.OpenReadOnly(options, savePath, errIfWalFileExists: false);

        var slots = new List<WindroseCharacterSlot>();
        using var iter = db.NewIterator();
        for (iter.SeekToFirst(); iter.Valid(); iter.Next())
        {
            var key = iter.Key();
            if (!WindroseKeys.IsCharacterKey(key)) continue;

            var characterId = WindroseKeys.ExtractCharacterId(key);
            var payload = iter.Value();

            // Empty / corrupt records → skip rather than fail the whole read.
            if (payload.Length < WindroseCharacterPayload.PayloadSize) continue;
            var name = WindroseCharacterPayload.ReadName(payload);
            if (string.IsNullOrEmpty(name)) continue;

            slots.Add(new WindroseCharacterSlot(
                CharacterId: characterId,
                Name: name,
                Level: WindroseCharacterPayload.ReadLevel(payload),
                Xp: WindroseCharacterPayload.ReadXp(payload),
                Gold: WindroseCharacterPayload.ReadGold(payload),
                Attributes: WindroseCharacterPayload.ReadAttributes(payload)));
        }
        return slots;
    }

    /// <summary>Apply an edit to one character (identified by RocksDB key suffix). Opens the
    /// store read-write; mutates the one key's payload; re-reads the key after the write to
    /// verify the change landed. Throws on failure — the snapshot from
    /// <c>SaveEditorService.EditCharacter</c> is the user's recovery path.</summary>
    public static void WriteEdit(string savePath, string characterId, WindroseCharacterEdit edit)
    {
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(WindroseKeys.BuildCharacterKey(characterId));

        var options = new DbOptions().SetCreateIfMissing(false);
        using var db = RocksDb.Open(options, savePath);

        // Read the current payload (we preserve every byte except the fields we touch).
        var existing = db.Get(keyBytes)
            ?? throw new KeyNotFoundException($"Windrose character key not found: {characterId}");

        // The payload size must match what we expect — if not, the format changed and we refuse
        // to write (snapshot is still safe; user can restore).
        if (existing.Length != WindroseCharacterPayload.PayloadSize)
            throw new InvalidOperationException(
                $"Windrose payload size mismatch (expected {WindroseCharacterPayload.PayloadSize}, got {existing.Length}); refusing to write.");

        var mutated = (byte[])existing.Clone();
        WindroseCharacterPayload.WriteName(mutated, edit.Name);
        WindroseCharacterPayload.WriteLevel(mutated, edit.Level);
        WindroseCharacterPayload.WriteXp(mutated, edit.Xp);
        WindroseCharacterPayload.WriteGold(mutated, edit.Gold);
        WindroseCharacterPayload.WriteAttributes(mutated, edit.Attributes);

        db.Put(keyBytes, mutated);

        // Post-write verification: re-read the key, confirm the edit landed.
        var roundTripped = db.Get(keyBytes)
            ?? throw new InvalidOperationException("Post-write read returned null — store may be corrupt.");
        if (WindroseCharacterPayload.ReadName(roundTripped) != edit.Name
            || WindroseCharacterPayload.ReadLevel(roundTripped) != edit.Level
            || WindroseCharacterPayload.ReadGold(roundTripped) != edit.Gold)
        {
            throw new InvalidOperationException(
                "Post-write verification failed — the edit didn't persist. Snapshot is intact.");
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~WindroseSaveTests"
```

Expected: 4/4 PASS. If a test fails:
- "Key not found" on a read → the key prefix in `WindroseKeys.CharacterKeyPrefix` is wrong; fix per Task 0 research.
- "Payload size mismatch" → the `PayloadSize` constant doesn't match what Chris971991's editor expects; fix in `WindroseCharacterPayload.cs`.
- Wrong name/level/etc. after round-trip → offset constants are wrong; bisect using `WindrosePayloadTests` (Task 3).

- [ ] **Step 5: Run the FULL test suite to confirm no regressions**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

Expected: all green. New tests = ~10 added.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/SaveEditor/Windrose/WindroseSave.cs \
        tests/ModManager.Tests/SaveEditor/Windrose/WindroseSaveTests.cs \
        tests/ModManager.Tests/SaveEditor/Windrose/WindroseFixture.cs
git commit -m "feat(save-editor): WindroseSave reader+writer with RocksDB round-trip tests"
```

---

## Task 5: App — `SaveEditorService` engine routing + neutral DTOs

**Files:**
- Create: `src/ModManager.App/Services/SaveEditorAdapters/SaveEditorRouting.cs`
- Create: `src/ModManager.App/Services/SaveEditorAdapters/FromSoftAdapter.cs`
- Create: `src/ModManager.App/Services/SaveEditorAdapters/WindroseAdapter.cs`
- Modify: `src/ModManager.App/Services/SaveEditorService.cs`
- Create: `tests/ModManager.Tests/SaveEditor/SaveEditorRoutingTests.cs`

The service grows engine awareness and a neutral character DTO so the UI never branches on engine. Existing FromSoft callers must keep working (the spec's compatibility line: the Saves dialog goes through this layer; today's tests must stay green).

- [ ] **Step 1: Create the neutral DTO + routing enum**

`src/ModManager.App/Services/SaveEditorAdapters/SaveEditorRouting.cs`:

```csharp
namespace ModManager.App.Services.SaveEditorAdapters;

/// <summary>Which engine's adapter handles a given (engine, steamAppId) pair.</summary>
public enum SaveEditorAdapter { None, FromSoft, Windrose }

/// <summary>One character row, engine-neutral. The Saves dialog renders this; the adapter
/// for each engine produces it.</summary>
public sealed record CharacterRow(
    int SlotIndex,                                              // FromSoft slot index OR -1 for Windrose (id-keyed)
    string Identity,                                            // FromSoft Steam ID / Windrose character GUID
    string SavePath,                                            // file path (FromSoft) or store dir (Windrose)
    string Name,
    string ClassLabel,                                          // e.g. "Vagabond" / "—"
    int Level,
    long Currency,                                              // runes (FromSoft) / gold (Windrose)
    string CurrencyLabel,                                       // "runes" / "gold"
    IReadOnlyList<(string Label, int Value)> Attributes);

/// <summary>One edit, engine-neutral. The adapter unpacks this into the engine's
/// edit record (FromSoft <c>CharacterEdit</c> / Windrose <c>WindroseCharacterEdit</c>).</summary>
public sealed record CharacterEditInput(
    string Name,
    long Currency,
    IReadOnlyList<(string Label, int Value)> Attributes,
    int? Level = null,                                          // null means "computed" (FromSoft) / required (Windrose)
    int? Xp = null);                                            // Windrose only; null when not applicable

public static class SaveEditorRouting
{
    /// <summary>Dispatch on (engine, steamAppId). UE-pak alone is not enough — Steam App ID
    /// is the gate for Windrose (other UE-pak games have totally different save formats).</summary>
    public static SaveEditorAdapter Route(string? engine, string? steamAppId)
    {
        if (engine == "fromsoft") return SaveEditorAdapter.FromSoft;
        if (engine == "ue-pak" && steamAppId == "2399830") return SaveEditorAdapter.Windrose; // Windrose
        return SaveEditorAdapter.None;
    }
}
```

- [ ] **Step 2: Write the failing routing test**

`tests/ModManager.Tests/SaveEditor/SaveEditorRoutingTests.cs`:

```csharp
using ModManager.App.Services.SaveEditorAdapters;

namespace ModManager.Tests.SaveEditor;

public class SaveEditorRoutingTests
{
    [Theory]
    [InlineData("fromsoft", "1245620", SaveEditorAdapter.FromSoft)]   // Elden Ring
    [InlineData("fromsoft", "374320",  SaveEditorAdapter.FromSoft)]   // DS3
    [InlineData("ue-pak",   "2399830", SaveEditorAdapter.Windrose)]   // Windrose
    [InlineData("ue-pak",   "990080",  SaveEditorAdapter.None)]       // Hogwarts Legacy — UE-pak, no editor
    [InlineData("bethesda", "489830",  SaveEditorAdapter.None)]
    [InlineData(null,       null,      SaveEditorAdapter.None)]
    public void Routes_engine_and_appid_to_correct_adapter(string? engine, string? appId, SaveEditorAdapter expected)
        => Assert.Equal(expected, SaveEditorRouting.Route(engine, appId));
}
```

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SaveEditorRoutingTests"`. Expected: PASS after Step 1's file is in place.

- [ ] **Step 3: Implement the FromSoft adapter**

`src/ModManager.App/Services/SaveEditorAdapters/FromSoftAdapter.cs`:

```csharp
using ModManager.Core.SaveEditor.FromSoft;

namespace ModManager.App.Services.SaveEditorAdapters;

internal static class FromSoftAdapter
{
    public static IReadOnlyList<CharacterRow> Read(string savePath)
        => EldenRingSave.ReadCharacters(savePath)
            .Select(s => new CharacterRow(
                SlotIndex: s.SlotIndex,
                Identity: s.SteamId,
                SavePath: savePath,
                Name: s.Name,
                ClassLabel: s.Class,
                Level: s.Level,
                Currency: s.Runes,
                CurrencyLabel: "runes",
                Attributes: new[]
                {
                    ("VIG", (int)s.Vig), ("MND", (int)s.Mnd), ("END", (int)s.End), ("STR", (int)s.Str),
                    ("DEX", (int)s.Dex), ("INT", (int)s.Int), ("FAI", (int)s.Fai), ("ARC", (int)s.Arc),
                }))
            .ToList();

    public static void Write(string savePath, int slotIndex, CharacterEditInput input)
    {
        var a = input.Attributes;
        byte Get(string label) => (byte)(a.FirstOrDefault(x => x.Label == label).Value);

        var edit = new CharacterEdit(
            Name: input.Name,
            Runes: (uint)input.Currency,
            Vig: Get("VIG"), Mnd: Get("MND"), End: Get("END"), Str: Get("STR"),
            Dex: Get("DEX"), Int: Get("INT"), Fai: Get("FAI"), Arc: Get("ARC"));

        EldenRingSave.WriteEdit(savePath, slotIndex, edit);
    }
}
```

- [ ] **Step 4: Implement the Windrose adapter**

`src/ModManager.App/Services/SaveEditorAdapters/WindroseAdapter.cs`:

```csharp
using ModManager.Core.SaveEditor.Windrose;

namespace ModManager.App.Services.SaveEditorAdapters;

internal static class WindroseAdapter
{
    public static IReadOnlyList<CharacterRow> Read(string savePath)
        => WindroseSave.ReadCharacters(savePath)
            .Select(s => new CharacterRow(
                SlotIndex: -1,
                Identity: s.CharacterId,
                SavePath: savePath,
                Name: s.Name,
                ClassLabel: "—",                            // Windrose doesn't expose class as a simple label in MVP
                Level: s.Level,
                Currency: s.Gold,
                CurrencyLabel: "gold",
                Attributes: s.Attributes))
            .ToList();

    public static void Write(string savePath, string characterId, CharacterEditInput input)
    {
        var edit = new WindroseCharacterEdit(
            Name: input.Name,
            Level: input.Level ?? throw new InvalidOperationException("Windrose edit requires Level"),
            Xp: input.Xp ?? throw new InvalidOperationException("Windrose edit requires Xp"),
            Gold: (int)input.Currency,
            Attributes: input.Attributes);

        WindroseSave.WriteEdit(savePath, characterId, edit);
    }
}
```

- [ ] **Step 5: Refactor `SaveEditorService` to dispatch**

`src/ModManager.App/Services/SaveEditorService.cs`:

```csharp
using ModManager.App.Services.SaveEditorAdapters;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>
/// App-layer service that owns the "snapshot before every edit" safety law (per the spec) AND
/// dispatches reads/writes to the right engine adapter (FromSoft / Windrose). The Saves dialog
/// only talks to this service — never the Core save-editor types directly.
/// </summary>
public sealed class SaveEditorService
{
    /// <summary>Read characters from a save file via the engine's adapter. Read-only; no
    /// snapshot needed.</summary>
    public IReadOnlyList<CharacterRow> ReadCharacters(string savePath, string? engine, string? steamAppId)
        => SaveEditorRouting.Route(engine, steamAppId) switch
        {
            SaveEditorAdapter.FromSoft => FromSoftAdapter.Read(savePath),
            SaveEditorAdapter.Windrose => WindroseAdapter.Read(savePath),
            _                           => Array.Empty<CharacterRow>(),
        };

    /// <summary>Apply an edit. Snapshots first; if that fails, throws before any write.
    /// Returns the snapshot taken (so the UI can surface it).</summary>
    public SaveSnapshot EditCharacter(
        string saveDir, string snapshotsDir, string savePath,
        string? engine, string? steamAppId,
        CharacterRow beforeEdit, CharacterEditInput edit)
    {
        var label = $"before-edit: {beforeEdit.Name} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

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
            switch (SaveEditorRouting.Route(engine, steamAppId))
            {
                case SaveEditorAdapter.FromSoft:
                    FromSoftAdapter.Write(savePath, beforeEdit.SlotIndex, edit);
                    break;
                case SaveEditorAdapter.Windrose:
                    WindroseAdapter.Write(savePath, beforeEdit.Identity, edit);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"No save editor adapter for engine '{engine}' (Steam app id '{steamAppId}').");
            }
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"Edit failed ({e.Message}). Your save is still intact, and a pre-edit snapshot is in the Snapshots list.", e);
        }

        return snap;
    }
}
```

- [ ] **Step 6: Run tests + verify the App still compiles**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: tests green. App builds; the SavesDialog callsites are now broken (we'll fix them in Task 6). The compile-fail in `SavesDialog.xaml.cs` is the planned next step.

- [ ] **Step 7: Commit**

```bash
git add src/ModManager.App/Services/SaveEditorAdapters/ \
        src/ModManager.App/Services/SaveEditorService.cs \
        tests/ModManager.Tests/SaveEditor/SaveEditorRoutingTests.cs
git commit -m "feat(save-editor): engine-aware SaveEditorService with FromSoft + Windrose adapters"
```

---

## Task 6: App — SavesDialog + CharacterEditDialog wiring

**Files:**
- Modify: `src/ModManager.App/SavesDialog.xaml.cs`
- Modify: `src/ModManager.App/CharacterEditDialog.xaml`
- Modify: `src/ModManager.App/CharacterEditDialog.xaml.cs`

Re-thread the Saves dialog through the new neutral DTOs. The Characters section now displays both engines without branching; CharacterEditDialog accepts a `CharacterRow` and emits a `CharacterEditInput`.

- [ ] **Step 1: Update SavesDialog to pass engine + appId + use the neutral row**

In `SavesDialog.xaml.cs`:

1. Add fields for `_engine` and `_steamAppId`, populated from `ctx.Game.Engine` and `ctx.Game.SteamAppId`.
2. Change `CharacterRow` declaration (the local record at the top of the file) — either drop it and import `ModManager.App.Services.SaveEditorAdapters.CharacterRow`, or keep a thin local wrapper that adds the two-line display strings. Cleaner is to drop and use the neutral type directly.
3. Rework `RefreshCharacters()`:

```csharp
private void RefreshCharacters()
{
    var rows = new List<CharacterRow>();
    if (!string.IsNullOrEmpty(_saveDir))
    {
        var svc = App.AppHost.Services.GetRequiredService<SaveEditorService>();
        foreach (var st in _saveTypes)
        {
            foreach (var savePath in System.IO.Directory.GetFiles(_saveDir, "*" + st.Extension))
            {
                IReadOnlyList<CharacterRow> chars;
                try { chars = svc.ReadCharacters(savePath, _engine, _steamAppId); }
                catch { continue; }
                rows.AddRange(chars);
            }
        }
        // Windrose case: ctx.SaveDir is the RocksDB store, not a file. The _saveTypes loop above
        // is FromSoft-shaped (file extensions). Add a fallback: if the engine route is Windrose,
        // read the store directly.
        if (SaveEditorRouting.Route(_engine, _steamAppId) == SaveEditorAdapter.Windrose)
        {
            try { rows.AddRange(svc.ReadCharacters(_saveDir, _engine, _steamAppId)); }
            catch { /* surface to status text if needed */ }
        }
    }
    CharacterList.ItemsSource = rows;
    CharactersEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    EditorCredit.Text = ResolveCreditFor(_engine);
}

private static string ResolveCreditFor(string? engine) => engine switch
{
    "fromsoft" => "Save format support by BenGrn, alfizari, ClayAmore — see Settings → About.",
    "ue-pak"   => "Save format support by Chris971991 (MIT) and WSE Project — see Settings → About.",
    _          => "",
};
```

4. Rework `OnEditCharacter`:

```csharp
private async void OnEditCharacter(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement fe || fe.DataContext is not CharacterRow row) return;

    var xamlRoot = this.XamlRoot;
    this.Hide();

    var dialog = new CharacterEditDialog(row) { XamlRoot = xamlRoot };
    ContentDialogResult result;
    string? statusAfter = null;
    try { result = await dialog.ShowAsync(); }
    catch (Exception ex)
    {
        statusAfter = $"Couldn't open editor ({ex.GetType().Name}): {ex.Message}";
        result = ContentDialogResult.None;
    }

    if (statusAfter is null && result == ContentDialogResult.Primary)
    {
        if (!dialog.IsValid())
        {
            statusAfter = "Name must be 1–16 characters. Edit was NOT applied.";
        }
        else
        {
            var edit = dialog.GetEdit();
            var svc = App.AppHost.Services.GetRequiredService<SaveEditorService>();
            try
            {
                var snap = svc.EditCharacter(
                    saveDir: _saveDir!,
                    snapshotsDir: _savesDir,
                    savePath: row.SavePath,
                    engine: _engine,
                    steamAppId: _steamAppId,
                    beforeEdit: row,
                    edit: edit);
                statusAfter = $"Edited \"{row.Name}\" → \"{edit.Name}\". Snapshot taken: {snap.Label}.";
            }
            catch (Exception ex) { statusAfter = ex.Message; }
        }
    }

    Refresh();
    RefreshCharacters();
    if (statusAfter is not null) StatusText.Text = statusAfter;
    try { await this.ShowAsync(); } catch { /* re-open race */ }
}
```

- [ ] **Step 2: Update CharacterEditDialog to work from the neutral row**

`CharacterEditDialog.xaml.cs`: change the constructor to accept `CharacterRow` (replacing the FromSoft-specific `CharacterSlot` parameter). The dialog builds its attribute editors dynamically from `row.Attributes` (each is `(Label, Value)`), so both engines work with the same XAML.

The XAML moves to an `ItemsControl` over an `Attributes` collection on the view model, instead of hardcoded VIG/MND/END/... rows. Currency label binds to `row.CurrencyLabel`. Level is read-only when null (FromSoft computes it); editable when the engine provides one (Windrose).

`GetEdit()` returns `CharacterEditInput`:

```csharp
public CharacterEditInput GetEdit() => new(
    Name: NameBox.Text,
    Currency: long.Parse(CurrencyBox.Text),
    Attributes: _attributeEditors.Select(e => (e.Label, int.Parse(e.ValueBox.Text))).ToList(),
    Level: LevelBox.IsEnabled ? int.Parse(LevelBox.Text) : null,
    Xp: XpBox.IsEnabled ? int.Parse(XpBox.Text) : null);
```

- [ ] **Step 3: Build + run the app**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: build green. Manual smoke (DON'T merge yet):
1. Open the app with Elden Ring registered → Saves → Characters lists FromSoft chars. Edit one → snapshot appears, edit lands.
2. Switch to Windrose → Saves → Characters lists Windrose chars. Edit one → snapshot appears, edit lands. Re-open the save in the game (manual smoke, not test) → edit is reflected.

If Windrose characters don't show: bisect via the routing test + check `_engine` + `_steamAppId` are set correctly from `GameEntry`.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/SavesDialog.xaml.cs \
        src/ModManager.App/CharacterEditDialog.xaml \
        src/ModManager.App/CharacterEditDialog.xaml.cs
git commit -m "feat(save-editor): SavesDialog + CharacterEditDialog work for both engines via neutral DTOs"
```

---

## Task 7: Item catalog — extract WSE Project's HTML to JSON (ship; don't consume yet)

**Files:**
- Create: `src/ModManager.Core/SaveEditor/Windrose/ItemCatalog/windrose-items.json`
- Modify: `src/ModManager.Core/ModManager.Core.csproj` (embed JSON as a resource)

The catalog ships in this MVP so phase 2 (inventory editing) is a UI-only PR, not UI-plus-data. Nothing in this MVP reads the catalog — it's dead weight, intentionally.

- [ ] **Step 1: Extract the HTML to JSON**

The WSE Project HTML is a single 1.17 MB file. The data is embedded as a JS object literal or HTML table — Task 0's research note pins the structure. Write a one-off Node or Python script (`scripts/extract-windrose-items.js` — NOT committed) to parse + emit JSON of the shape:

```json
{
  "_credits": {
    "source": "WSE Project Save Editor GUI 1.3 Item ID Database",
    "url": "<URL from Task 0>",
    "license": "<as confirmed in Task 0>",
    "extracted_on": "2026-05-26"
  },
  "_attribution": "Item data extracted from the WSE Project Item ID Database. Used here under the same license, with full credit to its authors.",
  "categories": ["weapon", "armor", "trinket", "...20 total"],
  "rarities": ["common", "uncommon", "rare", "epic", "legendary"],
  "items": [
    {
      "id": 12345,
      "name": "Iron Sword",
      "category": "weapon",
      "rarity": "common",
      "icon": "iron_sword.png"
    }
    // ...1268 entries
  ]
}
```

Save to `src/ModManager.Core/SaveEditor/Windrose/ItemCatalog/windrose-items.json`.

- [ ] **Step 2: Embed the JSON as a resource**

In `ModManager.Core.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="SaveEditor\Windrose\ItemCatalog\windrose-items.json" />
</ItemGroup>
```

This bakes the JSON into the assembly. Phase 2 will load it via `Assembly.GetManifestResourceStream`. No runtime cost in this MVP; nothing reads it.

- [ ] **Step 3: Smoke-verify the JSON parses**

Write a one-off throwaway test (delete after running):

```csharp
[Fact(Skip = "smoke only — delete after first green run")]
public void Windrose_items_json_parses()
{
    var asm = typeof(WindroseCharacterPayload).Assembly;
    using var s = asm.GetManifestResourceStream("ModManager.Core.SaveEditor.Windrose.ItemCatalog.windrose-items.json")!;
    using var r = new StreamReader(s);
    var json = System.Text.Json.JsonDocument.Parse(r.ReadToEnd());
    Assert.True(json.RootElement.GetProperty("items").GetArrayLength() >= 1000);
}
```

Run once; confirm green; delete the test before commit.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.Core/SaveEditor/Windrose/ItemCatalog/windrose-items.json \
        src/ModManager.Core/ModManager.Core.csproj
git commit -m "feat(save-editor): ship Windrose item catalog (1268 items, WSE Project attributed)"
```

---

## Task 8: Honor-the-builders — NOTICE + Settings → About + smoke + PR

**Files:**
- Modify: `NOTICE`
- Modify: `src/ModManager.App/SettingsDialog.xaml(.cs)` (the About / credits section)

Three surfaces, per the spec.

- [ ] **Step 1: Extend NOTICE**

Append to the existing `NOTICE` file:

```
================================================================================
Windrose save editor — Format research and item catalog
================================================================================

The Windrose save editor in this product is built on prior work by:

- Chris971991, "Windrose Character Editor" (MIT License)
  Source: <URL from Task 0>
  License: MIT — see LICENSES/Chris971991-MIT.txt

- WSE Project, "Save Editor GUI" and "Item ID Database"
  Source: <URL from Task 0>
  License: <as confirmed in Task 0>
  The item catalog at src/ModManager.Core/SaveEditor/Windrose/ItemCatalog/windrose-items.json
  is derived from WSE Project's standalone HTML Item ID Database. Used with full credit.

================================================================================
RocksDB (used for Windrose save IO)
================================================================================

- RocksDB.NET — Apache License 2.0
  Source: https://github.com/curiosity-ai/rocksdb-sharp
  License: Apache-2.0 — see LICENSES/RocksDB.NET-Apache-2.0.txt

- facebook/rocksdb (the underlying C++ engine wrapped by RocksDB.NET) — BSD 3-Clause
  Source: https://github.com/facebook/rocksdb
  License: BSD-3-Clause — see LICENSES/facebook-rocksdb-BSD-3-Clause.txt
```

Copy the upstream license texts into `LICENSES/Chris971991-MIT.txt`, `LICENSES/RocksDB.NET-Apache-2.0.txt`, `LICENSES/facebook-rocksdb-BSD-3-Clause.txt` (and the WSE Project license, whatever Task 0 confirms).

- [ ] **Step 2: Extend Settings → About**

Add a Windrose credit block to the About section's credit list in `SettingsDialog.xaml`:

```xml
<TextBlock TextWrapping="Wrap" Margin="0,8,0,0">
  Windrose save format support by Chris971991 (MIT) and the WSE Project.
  The Windrose item catalog ships under WSE Project attribution.
  RocksDB IO via RocksDB.NET (Apache-2.0) wrapping facebook/rocksdb (BSD-3-Clause).
  See NOTICE for full license texts.
</TextBlock>
```

- [ ] **Step 3: Full-suite test pass**

```bash
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

Expected: all green. Then:

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: green.

- [ ] **Step 4: Smoke the running app (manual)**

1. Run the app with Elden Ring registered → Saves → Characters → edit → confirm snapshot lands + edit applies.
2. Run with Windrose registered → Saves → Characters → confirm characters list → edit one (e.g. bump gold) → confirm snapshot lands → relaunch the actual game → confirm edit is reflected.
3. Settings → About → confirm Windrose credit block is visible.

- [ ] **Step 5: Commit attribution + push the branch**

```bash
git add NOTICE LICENSES/ \
        src/ModManager.App/SettingsDialog.xaml \
        src/ModManager.App/SettingsDialog.xaml.cs
git commit -m "docs: attribute Chris971991 + WSE Project + RocksDB.NET for Windrose save editor"

# Push to master-rooted branch (per CLAUDE.md: independent PRs off master, no stacking).
git push -u origin feat/saves-editor-windrose-mvp
```

- [ ] **Step 6: Open the PR**

Use `gh pr create --base master`:

- Title: `feat(save-editor): Windrose save editor MVP (RocksDB read/write + engine routing)`
- Body summary points:
  - Adds Windrose save editor (name + level + XP + gold + attributes) via a new pure-core `WindroseSave` adapter and the official `RocksDB.NET` NuGet
  - Refactors `SaveEditorService` to be engine-aware (FromSoft + Windrose adapters; future games slot in via the routing table)
  - Ships the WSE Project item catalog (1,268 items) as an embedded JSON resource — used in phase 2 inventory editing, not consumed in this MVP
  - Snapshot-first safety law unchanged; extended to cover RocksDB stores (the existing whole-folder zip already captures every RocksDB file)
- Test plan: list the test additions (count) + the manual Windrose smoke
- Attribution: callout the Chris971991 + WSE Project + RocksDB.NET + facebook/rocksdb credits

---

## Done conditions

- [ ] All tests green: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
- [ ] Self-contained publish includes the `librocksdb.dll` native binary under `runtimes/win-x64/native/`
- [ ] App build green: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
- [ ] Manual smoke: Elden Ring characters still edit cleanly (FromSoft regression check)
- [ ] Manual smoke: Windrose character edits land, snapshot appears, game loads the edited save
- [ ] NOTICE updated with all four attributions (Chris971991, WSE Project, RocksDB.NET, facebook/rocksdb)
- [ ] Settings → About shows the new Windrose credit block
- [ ] PR opened against `master`, NOT stacked on any other branch
