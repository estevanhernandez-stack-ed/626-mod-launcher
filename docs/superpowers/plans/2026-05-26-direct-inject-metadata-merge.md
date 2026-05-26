# Direct-inject + ME2 metadata-merge gap — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:subagent-driven-development`. Test command: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (NEVER bare `dotnet test`). Build command for App: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. Kill `ModManager.App.exe` first if the build complains about a locked Core DLL.

**Goal:** Close the gap from `docs/superpowers/specs/2026-05-26-direct-inject-metadata-merge-design.md`. The metadata.json identify path landed in PR #40 — this PR makes the rows actually USE it for direct-inject + ME2 games. One-line VM seam fix plus a Core test that pins the merge contract for catalog-keyed rows.

**Architecture:** Promote `Metadata.MergeMetadata` from "implicit inside `Scanner.ListWithClassAsync`" to "always applied in `MainViewModel.ReloadModsAsync` after the list-builder branch." `Metadata.MergeMetadata` is idempotent — the scanner branch's double-merge is safe.

**Tech Stack:** .NET 10, xUnit, WinUI 3. No new NuGets.

---

## Task 1: Core — pin the merge contract for catalog-keyed rows

**Files:**
- Create: `tests/ModManager.Tests/DirectInjectMetadataMergeTests.cs`

The merge logic is already correct (`Metadata.MergeMetadata` works fine on catalog-keyed rows because it falls back to `metaMap[m.Name]`). The test exists to pin the contract so a future refactor of `_direct.List`'s row shape doesn't quietly break direct-inject metadata rendering.

- [ ] **Step 1: Write the failing test**

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class DirectInjectMetadataMergeTests
{
    // Row shape produced by DirectInjectService.Row for catalog-recognized mods:
    //   Name = "Seamless Co-op", Base = "Seamless Co-op", Description = "Detected: ..."
    // The "Detected: ..." filler description must be replaced by the Nexus description when
    // metadata.json has an entry for the catalog name.
    [Fact]
    public void Catalog_named_row_picks_up_metadata_by_name_when_base_equals_name()
    {
        var rows = new List<Mod>
        {
            new() {
                Name = "Seamless Co-op",
                Base = "Seamless Co-op",
                Description = "Detected: seamlesscoop",
                Location = "direct-inject",
                Enabled = true,
            },
        };
        var meta = new Dictionary<string, ModMeta>(StringComparer.OrdinalIgnoreCase)
        {
            ["Seamless Co-op"] = new ModMeta
            {
                Title = "Seamless Co-op (Elden Ring)",
                Author = "Yui",
                AuthorUrl = "https://www.nexusmods.com/users/49594931",
                Url = "https://www.nexusmods.com/eldenring/mods/510",
                Image = "https://staticdelivery.nexusmods.com/mods/4333/images/510/test.png",
                Description = "Overhaul to the co-operative aspect of Elden Ring's multiplayer",
            },
        };

        var merged = Metadata.MergeMetadata(rows, meta);

        var row = Assert.Single(merged);
        Assert.Equal("Seamless Co-op (Elden Ring)", row.BaseTitle);
        Assert.Equal("Seamless Co-op (Elden Ring)", row.DisplayName);
        Assert.Equal("Yui", row.Author);
        Assert.Equal("https://www.nexusmods.com/users/49594931", row.AuthorUrl);
        Assert.Equal("https://www.nexusmods.com/eldenring/mods/510", row.ModUrl);
        Assert.Equal("https://staticdelivery.nexusmods.com/mods/4333/images/510/test.png", row.Image);
        Assert.Equal("Overhaul to the co-operative aspect of Elden Ring's multiplayer", row.Description);
        Assert.True(row.HasMeta);
    }

    // The same row, but without a matching metadata entry, must keep its bare display state —
    // no crash, no misattribution. Prettify falls back to the catalog name as-is (already pretty).
    [Fact]
    public void Catalog_named_row_without_metadata_keeps_bare_display()
    {
        var rows = new List<Mod>
        {
            new() { Name = "Seamless Co-op", Base = "Seamless Co-op", Description = "Detected: seamlesscoop" },
        };
        var merged = Metadata.MergeMetadata(rows, new Dictionary<string, ModMeta>());

        var row = Assert.Single(merged);
        Assert.Equal("Seamless Co Op", row.BaseTitle);   // Prettify treats "-" as a word break
        Assert.Null(row.Author);
        Assert.Null(row.Image);
        Assert.False(row.HasMeta);
    }
}
```

- [ ] **Step 2: Run tests to verify they pass (this is a contract pin — the underlying merge is correct)**

```
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~DirectInjectMetadataMergeTests"
```

Expected: 2/2 PASS. (No production code change yet — this test only pins what `Metadata.MergeMetadata` already does for catalog-keyed row shape.)

- [ ] **Step 3: Commit the test alone**

```
git add tests/ModManager.Tests/DirectInjectMetadataMergeTests.cs
git commit -m "test: pin Metadata.MergeMetadata contract for direct-inject catalog-keyed rows"
```

---

## Task 2: VM — apply MergeMetadata after the list-builder branch

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs`

- [ ] **Step 1: Read `MainViewModel.cs` around line 238-245** to confirm the three-way branch shape:

```csharp
IReadOnlyList<Mod> list;
var directInject = DirectInjectBacked;
if (ConfigBacked) list = _me2.ListMods(_ctx.Game);
else if (directInject) list = _direct.List(_ctx.Game);
else list = await ReloadFromScannerAsync();
```

- [ ] **Step 2: Insert the merge call right after the branch**

```csharp
IReadOnlyList<Mod> list;
var directInject = DirectInjectBacked;
if (ConfigBacked) list = _me2.ListMods(_ctx.Game);
else if (directInject) list = _direct.List(_ctx.Game);
else list = await ReloadFromScannerAsync();

// Always merge metadata.json onto the row list. The scanner branch did this internally too —
// MergeMetadata is idempotent (same map → same fields), so the scanner branch's re-merge is a
// no-op and the direct-inject + ME2 branches now pick up Nexus / CF identifies the same way
// Windrose does. Without this, metadata.json entries written by Md5IdentifyArchivesAsync /
// RefreshMetadataByNameAsync for fromsoft games never reach the displayed rows.
list = Metadata.MergeMetadata(list, Scanner.LoadMetadata(_ctx));
```

- [ ] **Step 3: Build**

```
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Run the test suite**

```
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

Expected: same green count as master + 2 new Task 1 tests passing.

- [ ] **Step 5: Commit**

```
git add src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "fix: ReloadModsAsync merges metadata.json onto direct-inject + ME2 row lists"
```

---

## Final integration: smoke + PR

- [ ] **Smoke locally:**
  1. Publish: `dotnet publish src/ModManager.App/ModManager.App.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64`
  2. Run the published exe.
  3. Switch to Elden Ring. Confirm the four already-identified mods (Seamless Co-op, Ultrawide / Widescreen Fix, DLL mod loader, ERSS2 Frame Gen) now show Nexus thumbnails + titles + author credits.
  4. Drop another archive (or use Backfill) to confirm new identifies render.
  5. Regression: Windrose rows still render correctly (the scanner-branch double-merge must not regress).

- [ ] **PR description includes:**
  - Diagnosis: identify path landed in PR #40, but the row-render path skipped the merge for non-scanner engines.
  - Fix: one-line VM-seam call to `Metadata.MergeMetadata` after the list-builder branch.
  - Test count delta.

---

## Self-Review Notes

- **Spec coverage:** Task 1 pins the merge contract for catalog rows; Task 2 wires it. Both layers explicit.
- **Idempotency claim:** read Metadata.cs:42-77 — every assignment is `m.X = e?.X` or `Prettify(baseSource)`. Same input → same output. Safe to call twice on scanner-backed rows.
- **No new NuGets.**
- **Risk:** very low. The change is additive (no removed behavior for scanner-backed games) and unlocks an already-populated data source for direct-inject + ME2.
