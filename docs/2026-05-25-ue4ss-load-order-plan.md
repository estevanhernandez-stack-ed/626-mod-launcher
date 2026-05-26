# UE4SS Load-Order Drive — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]`.

**Goal:** Let the existing load-order mode set the order of UE4SS mods by writing that order into UE4SS's own manifest (`mods.json` / `mods.txt` array/line order = load order), instead of the pak-prefix scheme (which doesn't apply to folder mods).

**Architecture:** A new `Ue4ssManifest.SetOrder(modsDir, orderedNames)` rewrites the manifest(s) so the named mods come first in the given order — preserving each mod's enable flag, comments, and the pinned `Keybinds`-last rule — in lockstep across mods.txt + mods.json. `Scanner.ApplyLoadOrder` already receives the global order from the App; after the pak-prefix pass it calls `SetOrder` for each UE4SS folder location with that location's mods in their relative order. No App changes.

**Tech Stack:** .NET 10, C#, xUnit. Branch: `feat/ue4ss-load-order` off `master` (already checked out). Core-only.

**Test/build:** `dotnet test "C:\Users\estev\Projects\626-mod-launcher\tests\ModManager.Tests\ModManager.Tests.csproj" --nologo` (EXPLICIT project). PowerShell; `git -C "C:\Users\estev\Projects\626-mod-launcher" ...`. Trailer: `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>`.

Reference: `Ue4ssManifest.SetEnabled` for the existing manifest read/write patterns (WriteAtomic, transactional txt+json lockstep, the `; Built-in keybinds` pin, JsonNode field preservation, the `TxtLine` regex). REUSE those — SetOrder must keep the same fidelity guarantees.

---

## Task 1: `Ue4ssManifest.SetOrder` — reorder the manifest, preserve flags + Keybinds pin

**Files:** Modify `src/ModManager.Core/Ue4ssManifest.cs`; add tests to `tests/ModManager.Tests/Ue4ssManifestTests.cs`.

Semantics: `SetOrder(modsDir, orderedNames)` puts the named mods first, in `orderedNames` order; mods present in the manifest but NOT in `orderedNames` keep their existing relative order after them; **`Keybinds` is always pinned last** (UE4SS requires it). Enable flags (`:1`/`:0` / `mod_enabled`) are preserved per mod. mods.txt comments are preserved (header block kept on top; the `; Built-in keybinds, do not move up!` comment stays attached above Keybinds). mods.txt + mods.json kept in lockstep (transactional, like SetEnabled). No-op-safe when a manifest is absent.

- [ ] **Step 1: Failing tests** (append to `Ue4ssManifestTests.cs`):

```csharp
    [Fact]
    public void SetOrder_reorders_mods_txt_preserving_flags_and_keybinds_last()
    {
        var d = ModsDir(); Folder(d, "A"); Folder(d, "B"); Folder(d, "C");
        File.WriteAllText(Path.Combine(d, "mods.txt"),
            "A : 1\nB : 1\nC : 0\n\n; Built-in keybinds, do not move up!\nKeybinds : 1\n");

        Ue4ssManifest.SetOrder(d, new[] { "C", "A" });

        var lines = File.ReadAllLines(Path.Combine(d, "mods.txt"))
            .Where(l => l.Contains(" : ")).Select(l => l.Trim()).ToList();
        // C, A first (in the given order), then the remaining (B), then Keybinds pinned last.
        Assert.Equal(new[] { "C : 0", "A : 1", "B : 1", "Keybinds : 1" }, lines);
    }

    [Fact]
    public void SetOrder_reorders_mods_json_preserving_enable_and_unknown_fields()
    {
        var d = ModsDir(); Folder(d, "A"); Folder(d, "B");
        File.WriteAllText(Path.Combine(d, "mods.json"),
            "[{\"mod_name\":\"A\",\"mod_enabled\":true,\"note\":\"keep\"},{\"mod_name\":\"B\",\"mod_enabled\":false}]");

        Ue4ssManifest.SetOrder(d, new[] { "B", "A" });

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(d, "mods.json")));
        var arr = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal("B", arr[0].GetProperty("mod_name").GetString());
        Assert.Equal("A", arr[1].GetProperty("mod_name").GetString());
        Assert.False(arr[0].GetProperty("mod_enabled").GetBoolean());          // flag preserved
        Assert.Equal("keep", arr[1].GetProperty("note").GetString());          // unknown field preserved
    }

    [Fact]
    public void SetOrder_keeps_txt_and_json_in_lockstep()
    {
        var d = ModsDir(); Folder(d, "A"); Folder(d, "B");
        File.WriteAllText(Path.Combine(d, "mods.txt"), "A : 1\nB : 1\n");
        File.WriteAllText(Path.Combine(d, "mods.json"),
            "[{\"mod_name\":\"A\",\"mod_enabled\":true},{\"mod_name\":\"B\",\"mod_enabled\":true}]");

        Ue4ssManifest.SetOrder(d, new[] { "B", "A" });

        var txtFirst = File.ReadAllLines(Path.Combine(d, "mods.txt")).First(l => l.Contains(" : ")).Trim();
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(d, "mods.json")));
        Assert.Equal("B : 1", txtFirst);
        Assert.Equal("B", doc.RootElement.EnumerateArray().First().GetProperty("mod_name").GetString());
    }

    [Fact]
    public void SetOrder_no_manifest_is_a_safe_noop()
    {
        var d = ModsDir(); Folder(d, "A");
        Ue4ssManifest.SetOrder(d, new[] { "A" }); // no throw, nothing to write
        Assert.False(File.Exists(Path.Combine(d, "mods.txt")));
    }
```

- [ ] **Step 2: Run red** (`--filter "FullyQualifiedName~SetOrder"`).

- [ ] **Step 3: Implement `SetOrder`** in `Ue4ssManifest.cs`. Read the existing `SetEnabled` / `SetInModsTxt` / `SetInModsJson` / `WriteAtomic` helpers first and reuse their style (transactional json-first-then-txt with rollback; JsonNode for json; line-preserving for txt). Sketch:

```csharp
    /// <summary>
    /// Reorder the manifest so <paramref name="orderedNames"/> come first (in that order); other
    /// listed mods keep their relative order after; Keybinds stays pinned last. Enable flags,
    /// comments, and unknown json fields are preserved; mods.txt + mods.json stay in lockstep.
    /// </summary>
    public static void SetOrder(string modsDir, IReadOnlyList<string> orderedNames)
    {
        var txt = Path.Combine(modsDir, "mods.txt");
        var json = Path.Combine(modsDir, "mods.json");
        var txtPre = File.Exists(txt) ? File.ReadAllText(txt) : null;
        var jsonPre = File.Exists(json) ? File.ReadAllText(json) : null;
        if (txtPre is null && jsonPre is null) return;

        // json first (authoritative), then txt; restore json on a txt failure (lockstep).
        if (jsonPre is not null) WriteAtomic(json, OrderModsJson(jsonPre, orderedNames));
        if (txtPre is not null)
        {
            try { WriteAtomic(txt, OrderModsTxt(txtPre, orderedNames)); }
            catch { if (jsonPre is not null) File.WriteAllText(json, jsonPre); throw; }
        }
    }
```

Implement two pure helpers:
- `OrderModsTxt(content, orderedNames)`: split into lines; classify each as a comment/blank, a `Name : flag` entry (via `TxtLine`), or other. Keep leading comment/blank lines (before the first entry) as a header. Pull out entries. Reorder entries: first the ones in `orderedNames` (that exist), in that order; then the remaining entries in their original order. **Force `Keybinds` (case-insensitive) to the very end** regardless of `orderedNames`. Re-emit: header lines, then the reordered non-Keybinds entries, then the `; Built-in keybinds...` comment (if it existed) + the Keybinds entry last. Preserve each entry's exact `Name : flag` text (flag unchanged).
- `OrderModsJson(content, orderedNames)`: parse with `JsonNode` into a `JsonArray`; build a new array: matching `mod_name` objects in `orderedNames` order first (move the whole node, preserving all fields), then the rest in original order, **Keybinds last**. Serialize indented.

- [ ] **Step 4: Run green** (filter), then full suite.
- [ ] **Step 5: Commit** `feat: Ue4ssManifest.SetOrder — reorder the loader manifest (flags + Keybinds pin preserved)`

---

## Task 2: Drive UE4SS order from `Scanner.ApplyLoadOrder`

**Files:** Modify `src/ModManager.Core/Scanner.cs` (`ApplyLoadOrder`); create `tests/ModManager.Tests/Ue4ssLoadOrderTests.cs`.

- [ ] **Step 1: Failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class Ue4ssLoadOrderTests
{
    [Fact]
    public async Task ApplyLoadOrder_writes_ue4ss_manifest_order_for_folder_mods()
    {
        var root = TestSupport.TempDir("ue4ss-lo-");
        var mods = Path.Combine(root, "ue4ss", "Mods");
        Directory.CreateDirectory(Path.Combine(mods, "Aaa"));
        Directory.CreateDirectory(Path.Combine(mods, "Bbb"));
        File.WriteAllText(Path.Combine(mods, "mods.txt"), "Aaa : 1\nBbb : 1\n");
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
            FileExtensions = new[] { "pak" }, GroupingRule = "strip_underscore_p_suffix",
            ModLocations = new[] { new ModLocation("ue4ss", "UE4SS", "ue4ss/Mods") { Form = "folders" } },
        });

        await Scanner.ApplyLoadOrderAsync(c, new[] { "Bbb", "Aaa" });

        var first = File.ReadAllLines(Path.Combine(mods, "mods.txt")).First(l => l.Contains(" : ")).Trim();
        Assert.Equal("Bbb : 1", first);   // manifest reordered to match the requested order
    }

    [Fact]
    public async Task ApplyLoadOrder_ignores_ue4ss_mods_not_in_the_location()
    {
        // A pak mod + a UE4SS mod; the ue4ss SetOrder only sees the ue4ss folder's mods.
        var root = TestSupport.TempDir("ue4ss-lo2-");
        var paks = Path.Combine(root, "Paks", "~mods"); Directory.CreateDirectory(paks);
        File.WriteAllText(Path.Combine(paks, "Cool_P.pak"), "x");
        var mods = Path.Combine(root, "ue4ss", "Mods"); Directory.CreateDirectory(Path.Combine(mods, "Lua1"));
        File.WriteAllText(Path.Combine(mods, "mods.txt"), "Lua1 : 1\n");
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
            FileExtensions = new[] { "pak" }, GroupingRule = "strip_underscore_p_suffix",
            ModLocations = new[]
            {
                new ModLocation("mods", "~mods", "Paks/~mods"),
                new ModLocation("ue4ss", "UE4SS", "ue4ss/Mods") { Form = "folders" },
            },
        });
        // Should not throw mixing pak + ue4ss keys.
        await Scanner.ApplyLoadOrderAsync(c, new[] { "Cool", "Lua1" });
        Assert.Contains("Lua1 : 1", File.ReadAllText(Path.Combine(mods, "mods.txt")));
    }
}
```

- [ ] **Step 2: Run red.**

- [ ] **Step 3: Wire into `ApplyLoadOrder`.** In `src/ModManager.Core/Scanner.cs`, in `ApplyLoadOrder`, just BEFORE `SaveLoadOrder(c, orderedKeys);`, add:

```csharp
        // UE4SS folder locations don't use pak prefixes — persist their relative order into the
        // loader manifest instead (only the mods that live in that folder, in the requested order).
        foreach (var loc in c.Locations.Where(l => l.Form == "folders" && Ue4ssManifest.IsUe4ssFolder(l.Abs)))
        {
            var locNames = new HashSet<string>(ListSubfolders(loc.Abs), StringComparer.OrdinalIgnoreCase);
            var orderedForLoc = orderedKeys.Where(locNames.Contains).ToList();
            if (orderedForLoc.Count > 0) Ue4ssManifest.SetOrder(loc.Abs, orderedForLoc);
        }
```

(`ListSubfolders` is an existing private helper in Scanner. Confirm its signature; it returns the folder names in `loc.Abs`.)

- [ ] **Step 4: Run the full suite.** New tests pass; existing pak load-order tests unaffected (they have no UE4SS folder location).

- [ ] **Step 5: Commit** `feat: drive UE4SS manifest order from ApplyLoadOrder (folder mods)`

---

## Note
No App change: the load-order mode already feeds the global order to `Scanner.ApplyLoadOrderAsync`, which now also writes the UE4SS manifest order. BepInEx has no inherent load order (excluded). Owned (Vortex) UE4SS folders: `ApplyLoadOrder`'s pak loop already excludes `ReadOnly`; for the manifest `SetOrder`, reordering an owned folder's manifest is the same edit-with-warning class as enable/disable — but the load-order mode operates on the global enabled set; if this proves to touch an owned folder undesirably, gate the `SetOrder` loop with `ToolOwnership.Detect(loc.Abs) is null`. DECIDE in the safety review.

## Self-Review
**Coverage:** SetOrder (flags + Keybinds pin + json fields + lockstep) — T1; ApplyLoadOrder wiring (folder-mod order, mixed pak+ue4ss safe) — T2. ✅
**Safety:** reuses SetEnabled's atomic + transactional lockstep; preserves enable flags (order-only change); Keybinds pinned; owned-folder gating flagged for the review. ✅
**Type consistency:** `Ue4ssManifest.SetOrder(string, IReadOnlyList<string>)`; reuses `WriteAtomic`/`TxtLine`/JsonNode; `Scanner.ApplyLoadOrder` adds a loop using existing `ListSubfolders` + `Ue4ssManifest.IsUe4ssFolder`. ✅
