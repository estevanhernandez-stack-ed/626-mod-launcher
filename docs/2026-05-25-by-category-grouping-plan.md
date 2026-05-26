# By-Category Grouping — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Steps use `- [ ]`.

**Goal:** Add **"By category"** to the group-by toggle so the list groups by the mod's content category (e.g. "Gameplay", "Ships", "UI") instead of by source or MP-safety. Captures the category during the CurseForge metadata fetch and stores it on the mod.

**Architecture:** `ModMeta` + `Mod` gain a `Category` string. `CurseForgeRequests.MapMod` captures the first category name from the CurseForge response (CF mods have a `categories` array). `Metadata.MergeMetadata` carries `Category` through onto the live `Mod`. `MainViewModel.GroupModes` adds `"By category"` and `SectionOf` groups by `m.Category ?? "UNCATEGORIZED"` in that mode.

**Tech Stack:** .NET 10, C#, xUnit. Branch: `feat/by-category` off `master` (already checked out).

**Scope honest note (Nexus deferred):** Nexus's API returns a numeric `category_id`; resolving id → name needs a separate `/games/{domain}/categories` call. Deferring Nexus category capture to a follow-on; CF-sourced mods get a real category, Nexus-only / built-ins fall into `UNCATEGORIZED` (perfectly reasonable for v1).

**Test/build:** `dotnet test "C:\Users\estev\Projects\626-mod-launcher\tests\ModManager.Tests\ModManager.Tests.csproj" --nologo` (EXPLICIT). App build: `dotnet build "...ModManager.App.csproj" -p:Platform=x64 --nologo`. PowerShell; `git -C "C:\Users\estev\Projects\626-mod-launcher" ...`. Trailer: `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>`.

Reference: `src/ModManager.Core/CurseForgeRequests.cs` (`MapMod` at line 106, the `CfMod` model — verify the `Categories` shape against the real DTO; CurseForge mods have an array of category objects with `name`). `src/ModManager.Core/Metadata.cs` (`MergeMetadata` is where to thread `Category`). `src/ModManager.App/ViewModels/MainViewModel.cs` (`GroupModes`, `SectionOf`, the `"By class"` branch — mirror its shape for `"By category"`).

---

## Task 1: `ModMeta.Category` + `Mod.Category` + capture from CurseForge

**Files:** Modify `src/ModManager.Core/Mod.cs` (ModMeta + Mod), `src/ModManager.Core/CurseForgeRequests.cs` (`MapMod`), `src/ModManager.Core/Metadata.cs` (`MergeMetadata`). Add tests to `tests/ModManager.Tests/RefreshMetadataTests.cs` (or a new `CategoryTests.cs`).

- [ ] **Step 1: Failing tests**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class CategoryTests
{
    [Fact]
    public void MergeMetadata_threads_category_from_meta_onto_mod()
    {
        var mod = new Mod { Name = "X", Base = "X" };
        var map = new Dictionary<string, ModMeta> { ["X"] = new ModMeta { Title = "X", Category = "Gameplay" } };
        var merged = Metadata.MergeMetadata(new[] { mod }, map).First();
        Assert.Equal("Gameplay", merged.Category);
    }

    [Fact]
    public void MergeMetadata_leaves_category_null_when_metadata_silent()
    {
        var merged = Metadata.MergeMetadata(new[] { new Mod { Name = "X", Base = "X" } }, null).First();
        Assert.Null(merged.Category);
    }

    [Fact]
    public void CurseForge_MapMod_captures_first_category_name_when_present()
    {
        // Read the actual CfMod model first. If CfMod.Categories is a list of objects with Name,
        // construct one with two categories ("Gameplay","UI") and assert MapMod -> Category=="Gameplay".
        // If CfMod doesn't yet have Categories, add a minimal field (List<CfCategory> Categories
        // with Name) to the model and assert. Either way, MapMod fills Category from the first entry.
        // Implementer: see the inline guidance.
    }
}
```

- [ ] **Step 2: Run red.**

- [ ] **Step 3: Implement (in order):**

3a. **`ModMeta.Category`** — in `src/ModManager.Core/Mod.cs`, after `CurseforgeId`:
```csharp
    public string? Category { get; set; }
```

3b. **`Mod.Category`** — in the same file, in the `Mod` class enrichment section (after `Downloads`):
```csharp
    public string? Category { get; set; }
```

3c. **`Metadata.MergeMetadata`** — after `m.Downloads = e?.Downloads;`, add:
```csharp
            m.Category = e?.Category;
```

3d. **`CurseForgeRequests.MapMod`** — read `CfMod` (in this file or its requests counterpart) to find/add a `Categories` field. CurseForge's `Mod` API returns `categories: [{ id, name, ... }]`. Add (if missing) to `CfMod`:
```csharp
public sealed class CfCategory { public string? Name { get; set; } }
public sealed class CfMod { /* existing... */ public List<CfCategory>? Categories { get; set; } }
```
Then in `MapMod` (line 106), add `Category` to the returned `ModMeta`:
```csharp
Category = mod.Categories is { Count: > 0 } ? Z(mod.Categories[0].Name) : null,
```
(`Z` is the existing null/empty-trim helper.)

Make the failing CF test concrete — construct a `CfMod` with `Categories = new() { new CfCategory { Name = "Gameplay" } }`, call `MapMod`, assert `Category == "Gameplay"`.

- [ ] **Step 4: Run green** (filter `~Category`), then full suite. The CurseForge JSON deserializer should pick up the new optional `Categories` field automatically; if it doesn't deserialize, check the JsonSerializerOptions or the property-naming policy in `CurseForgeRequests` and adapt.

- [ ] **Step 5: Commit** `feat: capture mod category from CurseForge metadata (ModMeta.Category)`

---

## Task 2: `"By category"` group-by mode

**Files:** Modify `src/ModManager.App/ViewModels/MainViewModel.cs` (`GroupModes`, `SectionOf`). Build-verified.

- [ ] **Step 1: Add `"By category"` to `GroupModes`**. Currently the list is `{ "By source", "By class" }`. Append `"By category"`:
```csharp
public IReadOnlyList<string> GroupModes { get; } = new[] { "By source", "By class", "By category" };
```

- [ ] **Step 2: Extend `SectionOf`** to handle the new mode. Read the existing method (which branches on `GroupMode == "By class"` then falls through to source) and add a branch BEFORE the class branch:
```csharp
        if (GroupMode == "By category")
        {
            var c = string.IsNullOrWhiteSpace(m.Category) ? "UNCATEGORIZED" : m.Category!.Trim().ToUpperInvariant();
            // Rank by alphabetical category so the order is stable; uncategorized goes LAST.
            var rank = string.Equals(c, "UNCATEGORIZED", StringComparison.Ordinal) ? int.MaxValue : c.GetHashCode();
            return (rank, c);
        }
```

(Hash-based rank is fine for stable A-Z-ish ordering within a session; if you want true alphabetical, compute the rank from `string.Compare` — but stability per session is what `OrderBy` needs, and the labels render the actual names. Implementer's call.)

A cleaner alternative that's deterministic alphabetical with UNCATEGORIZED last:
```csharp
        if (GroupMode == "By category")
        {
            var c = string.IsNullOrWhiteSpace(m.Category) ? "UNCATEGORIZED" : m.Category!.Trim().ToUpperInvariant();
            // Use a per-call cache so OrderBy is stable and alphabetical with Uncategorized last.
            // (Done via an indexer mapped from the distinct categories at section-stamp time.)
            return (c == "UNCATEGORIZED" ? 9999 : 0, c);
        }
```
The "rank" field only matters for top-to-bottom block ORDER; within OrderBy's stable sort all items sharing a `Label` end up grouped regardless of rank, so as long as UNCATEGORIZED ranks last, alphabetical-within-rank-0 falls out from how the sections render (labels grouped by stable order means the first occurrence of each category fixes its position). Implementer: pick whichever produces a sensible top-down order; the user can refine.

- [ ] **Step 3: Build the App** (x64), 0 errors.

- [ ] **Step 4: Commit** `feat: By-category group-by option (groups mods by CF metadata category)`

---

## Task 3: Verify + push
- [ ] Full suite green (only the 2 known 7z/rar SKIPs). App builds x64.
- [ ] Push `feat/by-category`; open PR vs `master`.

## Deferred / follow-on
- **Nexus category capture** (needs id → name resolution via `/games/{domain}/categories` — a separate Nexus client method).
- A built-in category mapping for the UE4SS framework folders so they group as "UE4SS BUILT-IN" instead of "UNCATEGORIZED" (a one-line override in `SectionOf`).

## Self-Review
**Coverage:** `Category` in ModMeta + Mod + merge (T1), CF capture (T1), `"By category"` group-by (T2). Nexus category + built-in mapping deferred. ✅
**Risk:** Metadata-only read+display change (no file IO beyond the existing metadata persistence). The owned-folder and mod-content invariants are untouched. Untrusted strings (category names from CF) flow through the existing text-only metadata render path. ✅
**Type consistency:** `ModMeta.Category` / `Mod.Category` (string?), `CfMod.Categories` (`List<CfCategory>?`, each with `Name`), `MainViewModel.GroupModes` (3 entries), `SectionOf` new branch. ✅
