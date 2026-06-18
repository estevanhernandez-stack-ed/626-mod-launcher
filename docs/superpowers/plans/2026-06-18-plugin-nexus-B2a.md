# Plugin slice — Phase B2a: grow the contract + rewire Scanner identify Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Grow the `IModSource` contract to carry everything Scanner's md5-identify needs, and rewire Scanner's three identify methods + `Ue4ssLuaInstaller` from `INexusClient` to the Abstractions `IModSource` — so scan-time identify routes through the plugin. (B2b later adds the bulk endorse-state/updated reads, rewires `NexusRefresh`, and *deletes* the Core Nexus cluster — Core stays not-yet-Nexus-free after B2a.)

**Architecture:** The contract's `SourceModMetadata` grows the identity/credit fields, and `IdentifyByHashAsync` returns a new `SourceIdentifyResult(ref, metadata)` (one md5 call → id + full metadata, matching today's `NexusMd5Match`). Core's `SourceMetadataMapper` maps the grown DTO → `ModMeta`; Scanner builds a Nexus-sourced `ModMeta` from it and `MergeMeta`s exactly as today. Scanner's identify methods take `IModSource?` (null → no-op, the zero-plugins/STORE path).

**Tech Stack:** .NET 10, C#, xUnit. Pure Core + Abstractions (`CorePurityTests` guards both). The plugin references only Abstractions. **Scanner is Core → its identify is unit-testable with a fake `IModSource`** (this is where B2a's test weight lives). The plugin + App call-sites are build-verified.

**Spec:** [`2026-06-18-plugin-nexus-B2-design.md`](../specs/2026-06-18-plugin-nexus-B2-design.md). **Branch:** `feat/plugin-host-nexus-slice`.

**Build/test (never bare `dotnet` at root; kill `ModManager.App` before App builds):**
- Core tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
- Plugin build: `dotnet build plugins/ModManager.Plugin.Nexus/ModManager.Plugin.Nexus.csproj`
- App FULL: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` · STORE: add `-p:Configuration=Store`

---

### Task 1: Grow the Abstractions contract

**Files:** `src/ModManager.Plugins.Abstractions/Contract.cs`; test `tests/ModManager.Tests/Plugins/AbstractionsContractTests.cs`.

- [ ] **Step 1: Update the contract** — grow `SourceModMetadata` (new fields optional/defaulted so existing call sites still compile), add `SourceIdentifyResult`, and change `IdentifyByHashAsync`'s return type:

```csharp
public sealed record SourceModMetadata(
    int? Endorsements, long? Downloads, string? LatestVersion, bool? Available, bool? Endorsed,
    // B2a — identity/credit fields md5-identify produces (what Scanner needs to build a ModMeta):
    string? Title = null, string? Description = null, string? Author = null, string? AuthorUrl = null,
    string? ImageUrl = null, string? ModUrl = null, string? Category = null,
    bool? ContainsAdultContent = null, int? NexusFileId = null);

/// <summary>An identify hit: the mod ref + the full metadata, both from the single md5 call.</summary>
public sealed record SourceIdentifyResult(SourceModRef Ref, SourceModMetadata Metadata);
```

In `IModSource`, change:
```csharp
    Task<SourceIdentifyResult?> IdentifyByHashAsync(string gameDomain, string md5);
```

- [ ] **Step 2: Update the contract test** — the fake in `AbstractionsContractTests.cs` now returns `SourceIdentifyResult`. Adjust `IdentifyByHashAsync` in the fake to `Task.FromResult<SourceIdentifyResult?>(new SourceIdentifyResult(new SourceModRef("fake", gameDomain, 42, "1.0"), new SourceModMetadata(10, 1000, "1.1", true, false, Title: "Fake Mod")))` and assert `refr.Ref.ModId == 42` + `refr.Metadata.Title == "Fake Mod"`.
- [ ] **Step 3:** Run `dotnet test ... --filter AbstractionsContractTests` → FAILs to compile first (old shape), passes after. Commit (`feat(plugins): grow IModSource contract — identity fields + SourceIdentifyResult`).

### Task 2: Grow `SourceMetadataMapper`

**Files:** `src/ModManager.Core/Plugins/SourceMetadataMapper.cs`; test `tests/ModManager.Tests/Plugins/SourceMetadataMapperTests.cs`.

- [ ] **Step 1: Failing test** — add cases asserting the new identity fields map onto `ModMeta` (reconcile to the real `ModMeta` names — `Image`/`Url`/`Category`/`ContainsAdultContent`/`NexusFileId`/`Author`/`AuthorUrl`/`Title`/`Description`), and a `FromIdentify` test asserting `NexusModId` comes from the **ref** (not the metadata):

```csharp
[Fact]
public void FromIdentify_sets_modId_from_ref_and_fields_from_metadata()
{
    var r = new SourceIdentifyResult(
        new SourceModRef("nexus", "skyrimspecialedition", 777, "2.0"),
        new SourceModMetadata(5, 100, "2.0", true, null, Title: "Cool", Author: "Mxyz", NexusFileId: 9));
    var meta = SourceMetadataMapper.FromIdentify(r);
    Assert.Equal(777, meta.NexusModId);
    Assert.Equal("Cool", meta.Title);
    Assert.Equal("Mxyz", meta.Author);
    Assert.Equal(9, meta.NexusFileId);
}
```

- [ ] **Step 2: Implement** — grow `Apply` to map every new field (nullable-never-clobber, preserving the heart-wipe guard) and add `FromIdentify`:

```csharp
public static ModMeta FromIdentify(SourceIdentifyResult r)
    => Apply(new ModMeta { NexusModId = r.Ref.ModId }, r.Metadata);

public static ModMeta Apply(ModMeta meta, SourceModMetadata dto)
{
    meta.EndorsementCount = dto.Endorsements ?? meta.EndorsementCount;
    meta.Downloads = dto.Downloads ?? meta.Downloads;
    meta.NexusLatestVersion = dto.LatestVersion ?? meta.NexusLatestVersion;
    meta.Available = dto.Available ?? meta.Available;
    meta.Endorsed = dto.Endorsed ?? meta.Endorsed;
    meta.Title = dto.Title ?? meta.Title;
    meta.Description = dto.Description ?? meta.Description;
    meta.Author = dto.Author ?? meta.Author;
    meta.AuthorUrl = dto.AuthorUrl ?? meta.AuthorUrl;
    meta.Image = dto.ImageUrl ?? meta.Image;
    meta.Url = dto.ModUrl ?? meta.Url;
    meta.Category = dto.Category ?? meta.Category;
    meta.ContainsAdultContent = dto.ContainsAdultContent ?? meta.ContainsAdultContent;
    meta.NexusFileId = dto.NexusFileId ?? meta.NexusFileId;
    return meta;
}
```

(Reconcile the property names to the real `ModMeta` — `Version` vs `NexusLatestVersion`: the mapper writes the *upstream latest* to `NexusLatestVersion`; the installed `Version` is set by the identify path from the ref, NOT clobbered here. Confirm against `Mod.cs`.)

- [ ] **Step 3:** Run `--filter SourceMetadataMapperTests` → fails then passes. Commit (`feat(plugins): map grown source metadata + FromIdentify (modId from ref)`).

### Task 3: Update the plugin to the grown contract

**Files:** `plugins/ModManager.Plugin.Nexus/NexusModSource.cs`.

- [ ] **Step 1:** `IdentifyByHashAsync` now returns `SourceIdentifyResult?` — map the md5_search mod object to the **full** `SourceModMetadata` (Title/Author/Image/Url/Category/etc. from the Nexus fields — model on Core's `NexusRequests.MapMod`) plus the `SourceModRef`, and return both. `FetchMetadataAsync` returns the grown `SourceModMetadata` (fill the identity fields too; `Endorsed` stays null). Build the plugin → 0 errors, still references only Abstractions. Commit (`feat(plugins): Nexus plugin returns full identify metadata for the grown contract`).

### Task 4: Rewire Scanner + Ue4ssLua identify to `IModSource`

**Files:** `src/ModManager.Core/Scanner.cs` (`Md5IdentifyAsync`, `Md5IdentifyArchivesAsync`, `IdentifyVortexNexusAsync`), `src/ModManager.Core/Ue4ssLuaInstaller.cs` (`IdentifyMetadataAsync`); tests `tests/ModManager.Tests/Md5IdentifyTests.cs`, `Md5IdentifyFromsoftTests.cs`, `VortexNexusIdentifyTests.cs`, `Ue4ssLuaMetadataTests.cs`.

- [ ] **Step 1: Switch the test fakes** — these tests inject a fake `INexusClient` today; change them to inject a fake `IModSource` (whose `IdentifyByHashAsync` returns a `SourceIdentifyResult` and `FetchMetadataAsync` the grown DTO). Run them → they FAIL to compile (Scanner still takes `INexusClient`). This is the failing-test step.
- [ ] **Step 2: Rewire the methods** — change each signature from `INexusClient nexus` to `IModSource? source`. Body: keep `NexusDomains.Effective` for the domain; **if `source is null` return `IdentifyResult(0)`** (the no-op / zero-plugins path); else:
  - md5 paths: `var hit = await source.IdentifyByHashAsync(domain, md5); if (hit is null) continue; var nexusMeta = SourceMetadataMapper.FromIdentify(hit); meta[key] = MergeMeta(..., nexusMeta);` (same `MergeMeta` semantics as today — Nexus authoritative).
  - `IdentifyVortexNexusAsync`: build a `SourceModRef("nexus", domain, id, "")` from the Vortex-recorded modId → `await source.FetchMetadataAsync(ref)` → `SourceMetadataMapper.Apply(new ModMeta { NexusModId = id }, dto)` → `MergeMeta`.
  - `Ue4ssLuaInstaller.IdentifyMetadataAsync`: same md5 → `FromIdentify` → merge.
- [ ] **Step 3:** Run `dotnet test ... --filter "Md5Identify|VortexNexusIdentify|Ue4ssLuaMetadata"` → green. Then `--filter CorePurity` (Core still pure — `IModSource` is Abstractions). Commit (`feat(core): Scanner + Ue4ssLua identify route through IModSource (null = no-op)`).

### Task 5: App call sites pass the registry's `IModSource`

**Files:** wherever the App invokes `Scanner.Md5Identify*` / `IdentifyVortexNexusAsync` / `Ue4ssLuaInstaller.IdentifyMetadataAsync` (grep the App for these) — `MainViewModel` intake/reload + the Ue4ss install path.

- [ ] **Step 1:** Replace the `_nexus.Client` argument with `_sources.ById("nexus")` (the loaded `IModSource?`). With no plugin (STORE), the methods no-op (already handled in Core). **Do NOT touch the bulk endorse-state / `NexusRefresh` paths — those stay on `NexusClient` until B2b.** Kill `ModManager.App`; FULL build 0 errors; STORE build 0 errors. Commit (`feat(app): pass the Nexus IModSource to Scanner identify call sites`).

### Task 6: Verify

- [ ] **Step 1:** Full Core suite `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` → green incl. `CorePurity` + the rewired identify tests. Plugin build 0 errors. FULL + STORE build 0 errors; STORE dll still has no loader symbols.
- [ ] **Step 2:** Confirm the intermediate state is coherent — `grep -rn "INexusClient" src/ModManager.Core/Scanner.cs src/ModManager.Core/Ue4ssLuaInstaller.cs` returns nothing (identify is off `INexusClient`); Core's `NexusClient`/`INexusClient` still exist (used by the not-yet-migrated bulk/refresh paths — **B2b deletes them**). Note in `docs/smoke-tests/pending.md`: scan-time identify now flows through the dev-signed plugin in FULL; absent in STORE.
- [ ] **Step 3:** Commit any smoke-doc update.

---

## Notes
- **B2a does NOT make Core Nexus-free** — that's B2b (bulk endorse-state + updated-window reads added to the contract, `NexusRefresh` rewired, the Core Nexus cluster deleted, impl tests relocated). B2a leaves `NexusClient`/`INexusClient` in Core for the still-on-NexusClient bulk paths; this is the coherent intermediate state.
- **Laws:** Core + Abstractions stay pure (`IModSource` is Abstractions; the mapper is pure); the heart-wipe guard holds (`Endorsed` nullable, `?? meta.Endorsed`); no file-op/reversibility surface (read-only identify); plugin references only Abstractions.
