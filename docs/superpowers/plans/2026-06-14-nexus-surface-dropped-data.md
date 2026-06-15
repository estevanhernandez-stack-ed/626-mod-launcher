# Nexus: surface the data we already fetch — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Stop dropping rich Nexus data we already fetch. Surface endorsements + the real download count on mod rows, flag mods removed from Nexus, and persist the Nexus mod id + installed file id/version — the groundwork for the later "updates available" + endorse features.

**Architecture:** Every Nexus mod-details response we map already carries `endorsement_count`, `mod_downloads`, `version`, `status`, `available`, `contains_adult_content`, `mod_id`; md5_search `file_details` carries `file_id`/`version`. `MapMod` keeps ~6 fields and drops the rest. This reads them into `ModMeta` (persisted, camelCase), carries them through `MergeMeta`, copies the displayed ones into the in-memory `Mod`, and surfaces them on the row. Zero net-new API calls; additive nullable fields; fully reversible.

**Tech Stack:** .NET 10, C#, WinUI 3 (App), xUnit (Core). Pure-Core / thin-App (`CorePurityTests`). camelCase JSON on disk.

**Research:** [docs/superpowers/research/2026-06-14-nexus-data-research.md](../research/2026-06-14-nexus-data-research.md). Field names verified against node-nexus-api types.ts.

**THE THREE-PLACES RULE (do not skip):** a *displayed* Nexus field must be added in THREE places or it silently fails — `ModMeta` (Mod.cs, persisted) → `MergeMeta` (Scanner.cs, or it's wiped on rescan) → in-memory `Mod` (Mod.cs) + `Metadata.MergeMetadata` (Metadata.cs, or it never renders). Persist-only fields (NexusModId, NexusFileId) need only the first two.

**Field plan:**
| field | ModMeta | MergeMeta | Mod + MergeMetadata | displayed |
|---|---|---|---|---|
| `Downloads` (existing long?) | ✓ exists | ✓ exists | ✓ exists | yes (just populate it) |
| `EndorsementCount` int? | add | add | add | yes |
| `Available` bool? | add | add | add | yes ("removed" hint when false) |
| `Version` string? | add | add | — | no (persist for update-check baseline) |
| `ContainsAdultContent` bool? | add | add | — | no (capture for future gating) |
| `NexusModId` int? | add | add | — | no (persist; endorse/update-check key) |
| `NexusFileId` int? | add | add | — | no (persist; update-check file key) |

Donate stays null for Nexus (no such field in the API).

---

## Task 1: ModMeta fields + MergeMeta carry-through

**Files:**
- Modify: `src/ModManager.Core/Mod.cs` (ModMeta class, ~line 54-80), `src/ModManager.Core/Scanner.cs` (MergeMeta, ~1141-1166)
- Test: `tests/ModManager.Tests/ModMetaRoundTripTests.cs`, `tests/ModManager.Tests/ManualMatchMergeTests.cs`

- [ ] **Step 1: Failing tests.** In `ModMetaRoundTripTests.cs`, extend the round-trip (mirror its existing camelCase pattern) to set + assert the new fields:

```csharp
[Fact]
public void ModMeta_nexus_fields_round_trip_as_camelCase()
{
    var m = new ModMeta { EndorsementCount = 1234, Version = "2.3", Available = false, ContainsAdultContent = false, NexusModId = 510, NexusFileId = 99 };
    var json = JsonSerializer.Serialize(m, Json);   // Json = the test's camelCase options block
    Assert.Contains("\"endorsementCount\"", json);
    Assert.Contains("\"nexusModId\"", json);
    Assert.Contains("\"nexusFileId\"", json);
    Assert.DoesNotContain("\"EndorsementCount\"", json);
    var rt = JsonSerializer.Deserialize<ModMeta>(json, Json)!;
    Assert.Equal(1234, rt.EndorsementCount);
    Assert.Equal("2.3", rt.Version);
    Assert.False(rt.Available);
    Assert.Equal(510, rt.NexusModId);
    Assert.Equal(99, rt.NexusFileId);
}
```

In `ManualMatchMergeTests.cs`, add a carry-through test (mirror the `InstalledUtc` pair at ~51-73) proving the new fields survive `MergeMeta` when the freshly-fetched (`cf`) side has them and curated lacks them:

```csharp
[Fact]
public void MergeMeta_carries_nexus_fields_from_fetched_when_curated_lacks_them()
{
    var cf = new ModMeta { NexusModId = 510, EndorsementCount = 1234, Available = false, Version = "2.3", NexusFileId = 99 };
    var curated = new ModMeta { Title = "Hand title" };   // no nexus fields
    var merged = CallMergeMeta(cf, curated);              // existing reflection helper
    Assert.Equal(510, merged.NexusModId);
    Assert.Equal(1234, merged.EndorsementCount);
    Assert.False(merged.Available);
    Assert.Equal("2.3", merged.Version);
    Assert.Equal(99, merged.NexusFileId);
    Assert.Equal("Hand title", merged.Title);            // curated still wins where it has a value
}
```

- [ ] **Step 2: Run — expect FAIL** (fields don't exist):

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~nexus_fields|FullyQualifiedName~carries_nexus"`

- [ ] **Step 3: Add the fields.** In `Mod.cs` `ModMeta`, after the existing optional fields (e.g. after `SourceConfidence`):

```csharp
    // Nexus enrichment (read live from the API response; all optional/additive).
    public int? EndorsementCount { get; set; }
    public string? Version { get; set; }
    public bool? Available { get; set; }              // false = Nexus reports the mod removed/unavailable
    public bool? ContainsAdultContent { get; set; }
    public int? NexusModId { get; set; }              // stable handle for endorse / update-check
    public int? NexusFileId { get; set; }             // the installed file's id (update-check key)
```

In `Scanner.cs` `MergeMeta`, add to the constructed `new ModMeta { ... }` block (every field — the merge drops anything not listed):

```csharp
        EndorsementCount = curated.EndorsementCount ?? cf.EndorsementCount,
        Version = curated.Version ?? cf.Version,
        Available = curated.Available ?? cf.Available,
        ContainsAdultContent = curated.ContainsAdultContent ?? cf.ContainsAdultContent,
        NexusModId = curated.NexusModId ?? cf.NexusModId,
        NexusFileId = curated.NexusFileId ?? cf.NexusFileId,
```

- [ ] **Step 4: Run — expect PASS**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ModMeta|FullyQualifiedName~MergeMeta|FullyQualifiedName~ManualMatch"`

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Mod.cs src/ModManager.Core/Scanner.cs tests/ModManager.Tests/ModMetaRoundTripTests.cs tests/ModManager.Tests/ManualMatchMergeTests.cs
git commit -m "feat(nexus): ModMeta fields for endorsements/version/available/adult/modId/fileId + MergeMeta carry-through"
```

---

## Task 2: Map the dropped fields in NexusRequests

**Files:**
- Modify: `src/ModManager.Core/NexusRequests.cs`
- Test: `tests/ModManager.Tests/NexusRequestsTests.cs`

- [ ] **Step 1: Failing tests.** In `NexusRequestsTests.cs`, extend the MapMod fixture (mirror the existing style) to include the new fields and assert them; extend the md5 `file_details` fixture with `version` and assert `NexusFileId`/`Version`:

```csharp
[Fact]
public void MapMod_reads_endorsements_downloads_version_available()
{
    using var doc = JsonDocument.Parse("""
    { "mod_id": 510, "name": "X", "summary": "s", "author": "a",
      "endorsement_count": 1234, "mod_downloads": 56789, "version": "2.3",
      "available": false, "contains_adult_content": false }
    """);
    var m = NexusRequests.MapMod("windrose", doc.RootElement);
    Assert.Equal(1234, m.EndorsementCount);
    Assert.Equal(56789L, m.Downloads);
    Assert.Equal("2.3", m.Version);
    Assert.False(m.Available);
    Assert.False(m.ContainsAdultContent);
    Assert.Equal(510, m.NexusModId);
}

[Fact]
public void MapMd5Response_stamps_file_id_and_version_from_file_details()
{
    using var doc = JsonDocument.Parse("""
    [ { "mod": { "mod_id": 510, "name": "X" },
        "file_details": { "file_id": 99, "version": "2.3.1" } } ]
    """);
    var match = NexusRequests.MapMd5Response("windrose", doc.RootElement)!;
    Assert.Equal(510, match.ModId);
    Assert.Equal(99, match.Meta.NexusFileId);
    Assert.Equal("2.3.1", match.Meta.Version);   // file_details.version is the installed-file version
}
```

- [ ] **Step 2: Run — expect FAIL**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~MapMod_reads|FullyQualifiedName~MapMd5Response_stamps"`

- [ ] **Step 3: Implement.** In `NexusRequests.cs`, add two helpers next to `Int`/`Bool`:

```csharp
    private static long? Long(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt64() : null;

    private static bool? BoolN(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el)
            ? el.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => (bool?)null }
            : null;
```

In `MapMod`, add to the returned `ModMeta` initializer (replace the `Downloads = null,` line; `modId` is already parsed at the top):

```csharp
            Donate = null,
            Downloads = Long(modObject, "mod_downloads"),
            EndorsementCount = Int(modObject, "endorsement_count"),
            Version = Str(modObject, "version"),
            Available = BoolN(modObject, "available"),
            ContainsAdultContent = BoolN(modObject, "contains_adult_content"),
            NexusModId = modId,
            Category = category,
```

In `MapMd5Response`, after `var meta = MapMod(domain, mod, categories);` and before constructing the match, stamp the file-level fields (ModMeta is a mutable class):

```csharp
            var meta = MapMod(domain, mod, categories);
            if (el.TryGetProperty("file_details", out var fd) && fd.ValueKind == JsonValueKind.Object)
            {
                meta.NexusFileId = Int(fd, "file_id");
                var fileVersion = Str(fd, "version");
                if (fileVersion is not null) meta.Version = fileVersion;   // installed-file version beats mod-level
            }
            return new NexusMd5Match(Int(mod, "mod_id"), meta);
```

- [ ] **Step 4: Run — expect PASS** (new + existing NexusRequests tests):

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~NexusRequests"`
Expected: all pass. (The existing `MapMod` test that asserted `Downloads` is null may need updating — it now reads `mod_downloads`; if the existing fixture has no `mod_downloads`, Downloads stays null and that assert still holds.)

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/NexusRequests.cs tests/ModManager.Tests/NexusRequestsTests.cs
git commit -m "feat(nexus): map endorsements/downloads/version/available/adult/modId + file_details id/version"
```

---

## Task 3: Carry the displayed fields into the in-memory Mod

**Files:**
- Modify: `src/ModManager.Core/Mod.cs` (the in-memory `Mod` class, ~38-50), `src/ModManager.Core/Metadata.cs` (`MergeMetadata` copy block, ~56-65)

- [ ] **Step 1: Add the fields to `Mod`.** Read `Mod.cs` first. After the existing `Downloads`/`Category` row fields on the `Mod` class, add:

```csharp
    public int? EndorsementCount { get; set; }
    public bool? Available { get; set; }     // false = removed from Nexus (drives the row hint)
```

- [ ] **Step 2: Copy them in `MergeMetadata`.** In `Metadata.cs`, in the `ModMeta` → `Mod` copy block (alongside `m.Downloads = meta.Downloads;`), add:

```csharp
        m.EndorsementCount = meta.EndorsementCount;
        m.Available = meta.Available;
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/ModManager.Core/ModManager.Core.csproj`
Expected: Build succeeded (Core, warnings-as-errors).

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.Core/Mod.cs src/ModManager.Core/Metadata.cs
git commit -m "feat(nexus): carry endorsements + availability into the in-memory Mod row model"
```

---

## Task 4: Surface endorsements + the "removed" hint on the row

**Files:**
- Modify: `src/ModManager.App/ViewModels/ModRowViewModel.cs`, `src/ModManager.App/MainWindow.xaml` (credit StackPanel, ~408-419)

Read both first. `DownloadsText`/`DownloadsVisibility` (~116-117) already exist and will now show real counts — no change needed there. `HasAnyCredit` (~119-120) gates the credit strip's visibility.

- [ ] **Step 1: Add row VM members.** Mirror `DownloadsText`/`DownloadsVisibility`:

```csharp
    public string EndorsementsText => Mod.EndorsementCount is > 0 ? $"{Mod.EndorsementCount:N0} endorsements" : "";
    public Visibility EndorsementsVisibility => Mod.EndorsementCount is > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RemovedFromNexusVisibility => Mod.Available == false ? Visibility.Visible : Visibility.Collapsed;
```

Add the two new signals to `HasAnyCredit` (so the credit strip shows when one of these is the only signal) — e.g. OR in `Mod.EndorsementCount is > 0 || Mod.Available == false`.

- [ ] **Step 2: Add to the XAML credit strip.** In `MainWindow.xaml`, alongside the `DownloadsText` TextBlock (~417-418), add an endorsements TextBlock (same `Opacity="0.55" FontSize="12"`) and a removed-hint TextBlock (use `ThemeDanger`):

```xml
                            <TextBlock Text="{x:Bind ViewModel.EndorsementsText, Mode=OneWay}"
                                       Visibility="{x:Bind ViewModel.EndorsementsVisibility, Mode=OneWay}"
                                       Opacity="0.55" FontSize="12" VerticalAlignment="Center" />
                            <TextBlock Text="Removed from Nexus" Foreground="{StaticResource ThemeDanger}"
                                       Visibility="{x:Bind ViewModel.RemovedFromNexusVisibility, Mode=OneWay}"
                                       FontSize="12" VerticalAlignment="Center" />
```

(Match the exact binding style — `x:Bind` vs `Binding` — and brush resource the sibling row elements use; confirm against the real markup.)

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/ViewModels/ModRowViewModel.cs src/ModManager.App/MainWindow.xaml
git commit -m "feat(nexus): show endorsements + real download count + a removed-from-Nexus hint on mod rows"
```

---

## Task 5: Smoke checklist + full verification

**Files:**
- Modify: `docs/smoke-tests/pending.md`

- [ ] **Step 1: Append**

```markdown
## Nexus enrichment — surfaced fields
- A mod identified via Nexus (md5/metadata) shows its endorsement count and a real download count on the row (download count was always blank before).
- A mod whose Nexus page was removed/taken down (available=false) shows a "Removed from Nexus" hint instead of a dead link.
- metadata.json carries nexusModId / nexusFileId / version for identified mods (persisted, survives a rescan — the groundwork for the future updates check).
```

- [ ] **Step 2: Full gate**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (all pass, incl. CorePurityTests)
Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (0 errors)

- [ ] **Step 3: Commit**

```bash
git add docs/smoke-tests/pending.md
git commit -m "docs(smoke): Nexus enrichment checklist"
```

---

## Self-Review

- **Three-places rule honored:** displayed fields (EndorsementCount, Available) added to ModMeta (T1) + MergeMeta (T1) + Mod & MergeMetadata (T3) + row (T4); Downloads reuses all-existing plumbing (just populated in T2); persist-only fields (Version, ContainsAdultContent, NexusModId, NexusFileId) added to ModMeta + MergeMeta only.
- **Privacy/reversibility:** all additive nullable fields; old metadata.json round-trips unchanged; no new API calls (reads fields already on responses we make); read-only; no destructive ops. camelCase round-trip test included (T1).
- **Pure-Core:** parse/map/merge in Core (tested); only the row VM + XAML in App. `CorePurityTests` green.
- **No Donate for Nexus** (verified absent from the API).
- **Type consistency:** `mod_downloads` (int in API) read via `Long` into `Downloads` (long?); `Available`/`ContainsAdultContent` via `BoolN` (bool?, null=absent); `NexusModId` from the already-parsed `mod_id`; `NexusFileId`/`Version` from `file_details` in MapMd5Response. The displayed `Mod.EndorsementCount`/`Mod.Available` match the ModMeta types.
- **Placeholders:** none — Core code complete; App task carries concrete code + the grounded anchors (HasAnyCredit, the credit StackPanel).
