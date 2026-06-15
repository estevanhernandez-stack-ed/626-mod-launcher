# Plugin Host + Nexus Vertical Slice — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a signed-plugin host with a slim contract, prove it end-to-end by extracting Nexus into the first plugin, and gate it so the Store build is a sealed core with the host compiled out.

**Architecture:** A new pure `ModManager.Plugins.Abstractions` assembly defines the contract (`IModSource` + DTOs + the host-services + plugin-entry interfaces). Core gains a pure `ModSourceRegistry` + DTO→`ModMeta` mapping + a `PluginSignature` verify (reusing `ManifestSignature`). An App-side `PluginHost` (FULL flavor only) verifies + `AssemblyLoadContext`-loads signed plugins and registers their contributions. Nexus moves into `plugins/ModManager.Plugin.Nexus` implementing the contract.

**Tech Stack:** .NET 10, C# (nullable-on, warnings-as-errors), xUnit. Pure Core + thin App shell; `CorePurityTests` bans WinUI/WinRT. ECDSA P-256 detached-sig (System.Security.Cryptography). `System.Runtime.Loader.AssemblyLoadContext` for plugin loading. Test project references **Core only** — App host/shells + the Nexus extraction are build- + smoke-verified.

**Spec:** [`docs/superpowers/specs/2026-06-15-plugin-host-nexus-slice-design.md`](../specs/2026-06-15-plugin-host-nexus-slice-design.md)

**Build/test commands (never bare `dotnet` at root):**
- Core tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (`--filter <Class>` to scope)
- App build (FULL): `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (kill any running `ModManager.App` first — MSB3027 lock)
- App build (STORE flavor): `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Configuration=Store` (configuration added in Task 8)

**Phasing:** Phase A (Tasks 1–6) is the foundation — fully testable Core/host + the build-flavor gate, proven with a signed *fake* plugin. Phase B (Tasks 7–11) extracts the real Nexus integration against the now-proven contract; it is build- + smoke-heavy and is where the contract churns. **Re-derive Phase B's exact edits from the live code when you reach it** — the file paths + patterns are precise, but the App rewiring is discovered during extraction (the test project can't reference App, so these are build+smoke).

---

## Phase A — Foundation (host + contract + signing, TDD)

### Task 1: `ModManager.Plugins.Abstractions` — the contract

**Files:**
- Create: `src/ModManager.Plugins.Abstractions/ModManager.Plugins.Abstractions.csproj`
- Create: `src/ModManager.Plugins.Abstractions/Contract.cs`
- Test: `tests/ModManager.Tests/Plugins/AbstractionsContractTests.cs`

- [ ] **Step 1: Create the project** — `src/ModManager.Plugins.Abstractions/ModManager.Plugins.Abstractions.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

Add a project reference from the test project (`tests/ModManager.Tests/ModManager.Tests.csproj`, in the existing `<ItemGroup>` of project refs):

```xml
    <ProjectReference Include="..\..\src\ModManager.Plugins.Abstractions\ModManager.Plugins.Abstractions.csproj" />
```

- [ ] **Step 2: Write the failing test** — `tests/ModManager.Tests/Plugins/AbstractionsContractTests.cs` (a fake plugin proves the contract is implementable + the DTOs carry the Nexus shape):

```csharp
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.Plugins;

public class AbstractionsContractTests
{
    // A minimal in-test plugin: proves the contract can actually be implemented.
    private sealed class FakePlugin : IModManagerPlugin
    {
        public string Id => "fake";
        public string DisplayName => "Fake";
        public void Register(IPluginHostServices host) => host.AddModSource(new FakeSource());
    }

    private sealed class FakeSource : IModSource
    {
        public string Id => "fake";
        public bool RequiresApiKey => true;
        public Task<SourceModRef?> IdentifyByHashAsync(string gameDomain, string md5)
            => Task.FromResult<SourceModRef?>(new SourceModRef("fake", gameDomain, 42, "1.0"));
        public Task<SourceModMetadata?> FetchMetadataAsync(SourceModRef r)
            => Task.FromResult<SourceModMetadata?>(new SourceModMetadata(10, 1000, "1.1", Available: true, Endorsed: false));
        public Task<bool> IsUpdateAvailableAsync(SourceModRef r, string installedVersion)
            => Task.FromResult(r.Version != installedVersion);
        public Task<EndorseResult> SetEndorsedAsync(SourceModRef r, bool endorsed)
            => Task.FromResult(new EndorseResult(Ok: true, Refused: false, Message: null, NowEndorsed: endorsed));
    }

    [Fact]
    public void Fake_plugin_registers_a_mod_source()
    {
        var registered = new List<IModSource>();
        var host = new TestHost(registered);
        new FakePlugin().Register(host);
        Assert.Single(registered);
        Assert.Equal("fake", registered[0].Id);
    }

    [Fact]
    public async Task Source_dtos_carry_the_nexus_shape()
    {
        var s = new FakeSource();
        var refr = await s.IdentifyByHashAsync("skyrimspecialedition", "abc");
        Assert.Equal(42, refr!.ModId);
        var meta = await s.FetchMetadataAsync(refr);
        Assert.Equal(10, meta!.Endorsements);
        Assert.True(await s.IsUpdateAvailableAsync(refr with { Version = "1.0" }, "0.9"));
        var endorse = await s.SetEndorsedAsync(refr, true);
        Assert.True(endorse.Ok && endorse.NowEndorsed == true);
    }

    private sealed class TestHost(List<IModSource> sink) : IPluginHostServices
    {
        public void AddModSource(IModSource source) => sink.Add(source);
        public string? GetCredential(string key) => null;
        public System.Net.Http.HttpClient HttpClient { get; } = new();
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter AbstractionsContractTests`
Expected: FAIL — the Abstractions types don't exist (compile error).

- [ ] **Step 4: Implement the contract** — `src/ModManager.Plugins.Abstractions/Contract.cs`:

```csharp
namespace ModManager.Plugins.Abstractions;

/// <summary>The entry type a plugin assembly exports. The host instantiates it and calls Register.</summary>
public interface IModManagerPlugin
{
    string Id { get; }            // stable, e.g. "nexus"
    string DisplayName { get; }   // "Nexus Mods"
    void Register(IPluginHostServices host);
}

/// <summary>What the host offers a plugin: register contributions, read the on-machine credential, shared HttpClient.
/// The plugin NEVER stores or exfiltrates the credential — it receives it per call from the host-owned store.</summary>
public interface IPluginHostServices
{
    void AddModSource(IModSource source);
    string? GetCredential(string key);                 // host-owned, on-machine per-user key store
    System.Net.Http.HttpClient HttpClient { get; }
}

/// <summary>A mod-source site (Nexus, CurseForge, ...). Speaks DTOs only — never Core types — so a plugin
/// references just this slim assembly. Generalizes INexusClient.</summary>
public interface IModSource
{
    string Id { get; }
    bool RequiresApiKey { get; }
    Task<SourceModRef?> IdentifyByHashAsync(string gameDomain, string md5);
    Task<SourceModMetadata?> FetchMetadataAsync(SourceModRef modRef);
    Task<bool> IsUpdateAvailableAsync(SourceModRef modRef, string installedVersion);
    Task<EndorseResult> SetEndorsedAsync(SourceModRef modRef, bool endorsed);
}

public sealed record SourceModRef(string SourceId, string GameDomain, int ModId, string Version);
public sealed record SourceModMetadata(int? Endorsements, long? Downloads, string? LatestVersion, bool Available, bool Endorsed);
public sealed record EndorseResult(bool Ok, bool Refused, string? Message, bool? NowEndorsed);
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter AbstractionsContractTests`
Expected: PASS (both tests).

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Plugins.Abstractions tests/ModManager.Tests/Plugins/AbstractionsContractTests.cs tests/ModManager.Tests/ModManager.Tests.csproj
git commit -m "feat(plugins): slim Abstractions contract (IModSource + DTOs + host services)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: `PluginSignature` + pinned plugin key — verify before load

Reuses `ManifestSignature.Verify` (ECDSA P-256 / SHA-256 / IeeeP1363) — same crypto the feed uses, applied to plugin assembly bytes.

**Files:**
- Create: `src/ModManager.Core/Plugins/PluginSignature.cs`
- Create: `src/ModManager.Core/Plugins/PluginSigningKey.cs`
- Test: `tests/ModManager.Tests/Plugins/PluginSignatureTests.cs`

- [ ] **Step 1: Write the failing test** — `tests/ModManager.Tests/Plugins/PluginSignatureTests.cs` (generates an ephemeral key, signs, verifies — mirrors the manifest signature tests):

```csharp
using System.Security.Cryptography;
using ModManager.Core.Manifest;
using ModManager.Core.Plugins;

namespace ModManager.Tests.Plugins;

public class PluginSignatureTests
{
    [Fact]
    public void Valid_signature_verifies_and_a_tampered_one_does_not()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki = ecdsa.ExportSubjectPublicKeyInfo();
        var payload = new byte[] { 1, 2, 3, 4, 5 };  // stand-in for assembly bytes
        var sig = ecdsa.SignData(payload, HashAlgorithmName.SHA256, ManifestSignature.Format);

        Assert.True(PluginSignature.VerifyWithKey(spki, payload, sig));               // valid
        var tampered = (byte[])payload.Clone(); tampered[0] ^= 0xFF;
        Assert.False(PluginSignature.VerifyWithKey(spki, tampered, sig));             // payload changed
        Assert.False(PluginSignature.VerifyWithKey(spki, payload, new byte[64]));     // bad sig
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter PluginSignatureTests`
Expected: FAIL — `PluginSignature` does not exist.

- [ ] **Step 3: Implement** — `src/ModManager.Core/Plugins/PluginSignature.cs`:

```csharp
using ModManager.Core.Manifest;

namespace ModManager.Core.Plugins;

/// <summary>Detached-signature verify for plugin assemblies — the same ECDSA P-256/SHA-256 scheme as the
/// manifest feed (<see cref="ManifestSignature"/>), applied to the plugin .dll bytes. The host verifies
/// a plugin against the pinned <see cref="PluginSigningKey"/> before loading it; a bad/missing signature
/// means the assembly is never loaded.</summary>
public static class PluginSignature
{
    /// <summary>Verify the assembly bytes against the pinned plugin-signing public key.</summary>
    public static bool Verify(ReadOnlySpan<byte> assemblyBytes, ReadOnlySpan<byte> signature)
        => VerifyWithKey(PluginSigningKey.PublicKeySpki, assemblyBytes, signature);

    /// <summary>Verify against an explicit key (test seam).</summary>
    public static bool VerifyWithKey(ReadOnlySpan<byte> spki, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
        => ManifestSignature.Verify(spki, data, signature);
}
```

And `src/ModManager.Core/Plugins/PluginSigningKey.cs` (the pinned public key — a placeholder zero-length array is fine until the real keypair is minted; the verify simply fails closed until then):

```csharp
namespace ModManager.Core.Plugins;

/// <summary>The pinned plugin-signing public key (SubjectPublicKeyInfo / DER). Mint the real ECDSA P-256
/// keypair when the plugin source goes live (sub-project 5) and paste the SPKI bytes here; until then an
/// empty key means every signature fails closed (no plugin loads), which is the safe default.</summary>
public static class PluginSigningKey
{
    public static ReadOnlySpan<byte> PublicKeySpki => Array.Empty<byte>();
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter PluginSignatureTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Plugins/ tests/ModManager.Tests/Plugins/PluginSignatureTests.cs
git commit -m "feat(plugins): PluginSignature verify (reuses ManifestSignature ECDSA) + pinned key slot

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: `ModSourceRegistry` — the contribution sink (zero-plugins invariant)

**Files:**
- Create: `src/ModManager.Core/Plugins/ModSourceRegistry.cs`
- Test: `tests/ModManager.Tests/Plugins/ModSourceRegistryTests.cs`

(Core references the Abstractions assembly — add `<ProjectReference Include="..\ModManager.Plugins.Abstractions\ModManager.Plugins.Abstractions.csproj" />` to `src/ModManager.Core/ModManager.Core.csproj`. Abstractions is pure, so `CorePurityTests` stays green.)

- [ ] **Step 1: Write the failing test** — `tests/ModManager.Tests/Plugins/ModSourceRegistryTests.cs`:

```csharp
using ModManager.Core.Plugins;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.Plugins;

public class ModSourceRegistryTests
{
    private sealed class Src(string id) : IModSource
    {
        public string Id => id;
        public bool RequiresApiKey => false;
        public Task<SourceModRef?> IdentifyByHashAsync(string g, string m) => Task.FromResult<SourceModRef?>(null);
        public Task<SourceModMetadata?> FetchMetadataAsync(SourceModRef r) => Task.FromResult<SourceModMetadata?>(null);
        public Task<bool> IsUpdateAvailableAsync(SourceModRef r, string v) => Task.FromResult(false);
        public Task<EndorseResult> SetEndorsedAsync(SourceModRef r, bool e) => Task.FromResult(new EndorseResult(true, false, null, e));
    }

    [Fact]
    public void Empty_registry_has_no_sources()  // the zero-plugins invariant (the Store SKU)
    {
        var reg = new ModSourceRegistry();
        Assert.Empty(reg.Sources);
        Assert.Null(reg.ById("nexus"));
    }

    [Fact]
    public void Registered_sources_resolve_by_id()
    {
        var reg = new ModSourceRegistry();
        reg.Add(new Src("nexus"));
        Assert.Single(reg.Sources);
        Assert.Equal("nexus", reg.ById("nexus")!.Id);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter ModSourceRegistryTests`
Expected: FAIL — `ModSourceRegistry` does not exist.

- [ ] **Step 3: Implement** — `src/ModManager.Core/Plugins/ModSourceRegistry.cs`:

```csharp
using ModManager.Plugins.Abstractions;

namespace ModManager.Core.Plugins;

/// <summary>Holds the mod sources contributed by loaded plugins. Empty when no plugin is loaded (the
/// Store SKU) — every consumer must tolerate an empty registry, which is what keeps the core a complete
/// product on its own.</summary>
public sealed class ModSourceRegistry
{
    private readonly List<IModSource> _sources = new();
    public IReadOnlyList<IModSource> Sources => _sources;
    public void Add(IModSource source) { if (ById(source.Id) is null) _sources.Add(source); }
    public IModSource? ById(string id) => _sources.FirstOrDefault(s => s.Id == id);
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter ModSourceRegistryTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Plugins/ModSourceRegistry.cs src/ModManager.Core/ModManager.Core.csproj tests/ModManager.Tests/Plugins/ModSourceRegistryTests.cs
git commit -m "feat(plugins): ModSourceRegistry contribution sink (zero-plugins invariant)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: `SourceMetadataMapper` — DTO → `ModMeta` (the boundary that keeps plugins off Core)

The plugin returns `SourceModMetadata` (Abstractions); the core maps it onto its existing `ModMeta` mod-source fields, so the plugin never references Core's `ModMeta`.

**Files:**
- Create: `src/ModManager.Core/Plugins/SourceMetadataMapper.cs`
- Test: `tests/ModManager.Tests/Plugins/SourceMetadataMapperTests.cs`

- [ ] **Step 1: Read `ModMeta`** — open `src/ModManager.Core/Mod.cs`, find the `ModMeta` record and note the exact mod-source field names (endorsement count, downloads, latest version, availability, endorsed). Use those exact names in the mapper + test below (the placeholders `Endorsements`/`Downloads`/`LatestVersion`/`Available`/`Endorsed` here MUST be reconciled to the real `ModMeta` property names).

- [ ] **Step 2: Write the failing test** — `tests/ModManager.Tests/Plugins/SourceMetadataMapperTests.cs`:

```csharp
using ModManager.Core;
using ModManager.Core.Plugins;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests.Plugins;

public class SourceMetadataMapperTests
{
    [Fact]
    public void Maps_source_metadata_onto_mod_meta_fields()
    {
        var dto = new SourceModMetadata(Endorsements: 12, Downloads: 3400, LatestVersion: "2.1", Available: true, Endorsed: true);
        var meta = new ModMeta();                              // existing core type
        var mapped = SourceMetadataMapper.Apply(meta, dto);    // returns the updated ModMeta
        // Reconcile these asserts to ModMeta's real field names (Step 1):
        Assert.Equal(12, mapped.EndorsementCount);
        Assert.Equal(3400, mapped.Downloads);
        Assert.Equal("2.1", mapped.LatestVersion);
        Assert.True(mapped.Endorsed);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter SourceMetadataMapperTests`
Expected: FAIL — `SourceMetadataMapper` does not exist (and/or field-name mismatch to reconcile).

- [ ] **Step 4: Implement** — `src/ModManager.Core/Plugins/SourceMetadataMapper.cs` (reconcile property names to the real `ModMeta`; `ModMeta` is a record → use `with`, or set mutable props if that's its shape):

```csharp
using ModManager.Plugins.Abstractions;

namespace ModManager.Core.Plugins;

/// <summary>Maps a plugin's source metadata DTO onto the core's ModMeta mod-source fields. The boundary
/// that lets a plugin speak DTOs and never reference Core's ModMeta. camelCase persistence is unchanged —
/// these are existing ModMeta fields populated from a new source.</summary>
public static class SourceMetadataMapper
{
    public static ModMeta Apply(ModMeta meta, SourceModMetadata dto) => meta with
    {
        EndorsementCount = dto.Endorsements ?? meta.EndorsementCount,
        Downloads = dto.Downloads ?? meta.Downloads,
        LatestVersion = dto.LatestVersion ?? meta.LatestVersion,
        Endorsed = dto.Endorsed,
        // map Available -> the existing availability field name
    };
}
```

- [ ] **Step 5: Run to verify it passes** (with field names reconciled)

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter SourceMetadataMapperTests`
Expected: PASS. Then run `--filter CorePurity` to confirm Core + Abstractions stay pure.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/Plugins/SourceMetadataMapper.cs tests/ModManager.Tests/Plugins/SourceMetadataMapperTests.cs
git commit -m "feat(plugins): SourceMetadata DTO -> ModMeta mapping boundary

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: `PluginHost` (App, FULL flavor) — discover, verify, load, register

App-side IO/reflection — **build-verified**, not unit-tested (test project doesn't reference App). Smoke-tested in Task 11 with a signed fake plugin.

**Files:**
- Create: `src/ModManager.App/Services/PluginHost.cs`
- Modify: `src/ModManager.App/` DI/startup (where services are composed — `App.xaml.cs` / `Program.cs` / the host builder)

- [ ] **Step 1: Implement `PluginHost`** — discover `*.dll` + sibling `*.dll.sig` in the plugins dir (`%LOCALAPPDATA%\ModManagerBuilder\plugins\` — match the existing data-dir convention), verify each with `PluginSignature.Verify(File.ReadAllBytes(dll), File.ReadAllBytes(sig))`, load verified assemblies in a collectible `AssemblyLoadContext`, find the `IModManagerPlugin` exported type, instantiate it, and call `Register` with an `IPluginHostServices` impl that adds to the shared `ModSourceRegistry`, reads the on-machine credential store, and hands over the shared `HttpClient`. Wrap each plugin in try/catch — one bad plugin never crashes startup. The `IPluginHostServices` impl is App-side (it owns the credential store + HttpClient).

- [ ] **Step 2: Register in DI/startup** — compose a single `ModSourceRegistry` into DI; after building services, call `PluginHost.LoadAll(registry, credentialStore, httpClient)`. **Guard the entire host call site with the FULL build flavor** (`#if FULL` — the constant is defined in Task 6) so the Store flavor never compiles the loader in.

- [ ] **Step 3: Build to verify** — kill any running `ModManager.App`, then `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. Expected: 0 errors. (No plugin loads yet — the plugins dir is empty; the app runs with an empty registry, which is the zero-plugins path.)

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/Services/PluginHost.cs src/ModManager.App/App.xaml.cs
git commit -m "feat(plugins): App-side PluginHost (verify + ALC load + register), FULL-gated

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Build flavors — `FULL` (GitHub) vs `STORE` (sealed core)

**Files:**
- Modify: `src/ModManager.App/ModManager.App.csproj`

- [ ] **Step 1: Add a `Store` configuration + the `FULL` symbol.** In `ModManager.App.csproj`, define `FULL` for every non-Store configuration and leave it undefined for `Store`:

```xml
  <PropertyGroup Condition="'$(Configuration)' != 'Store'">
    <DefineConstants>$(DefineConstants);FULL</DefineConstants>
  </PropertyGroup>
```

(So Debug/Release builds are FULL and compile the host in; a `-p:Configuration=Store` build leaves `FULL` undefined → the `#if FULL` host call site from Task 5 compiles out.)

- [ ] **Step 2: Build both flavors** — kill any running `ModManager.App`, then:
  - FULL: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` → 0 errors (host compiled in).
  - STORE: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Configuration=Store` → 0 errors (host compiled out; `PluginHost` not referenced).

- [ ] **Step 3: Commit**

```bash
git add src/ModManager.App/ModManager.App.csproj
git commit -m "build(plugins): FULL vs STORE flavor — host compiled out of the Store SKU

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Phase B — Nexus extraction (build + smoke; the contract churns here)

> **Phase B is integration, not unit-TDD** (the test project can't reference App or the plugin assembly). Re-derive the exact edits from the live code; the anchors below are precise, the rewiring is discovered. The Core/host foundation (Phase A) carries the test weight; Phase B is build- + smoke-verified.

### Task 7: `plugins/ModManager.Plugin.Nexus` — the assembly + `IModSource` impl

**Files:**
- Create: `plugins/ModManager.Plugin.Nexus/ModManager.Plugin.Nexus.csproj` (references `ModManager.Plugins.Abstractions` ONLY)
- Move: `NexusClient.cs` + `NexusRequests` + `NexusRefresh` + `NexusEndorse` + `NexusRateLimit` + `NexusOptions` + the Nexus DTOs (`NexusMd5Match`/`NexusUser`/`NexusUpdateEntry`/`NexusEndorsement`/`EndorseOutcome`/`EndorseAction`) from `src/ModManager.Core/` into the plugin
- Create: `plugins/ModManager.Plugin.Nexus/NexusPlugin.cs` (`IModManagerPlugin`) + `NexusModSource.cs` (`IModSource`)

- [ ] **Step 1:** Move the Nexus files into the plugin project. The pieces that returned/used Core's `ModMeta` (`NexusRequests.MapModResponse` → `ModMeta`) get reworked to return the Abstractions `SourceModMetadata` DTO instead — the DTO is the boundary. `NexusDomains` slug resolution stays in Core (manifest data); the host passes the resolved domain into the source via `SourceModRef.GameDomain`.
- [ ] **Step 2:** `NexusModSource : IModSource` adapts the existing methods to the contract: `IdentifyByHashAsync` → `GetByMd5Async`; `FetchMetadataAsync` → `GetModAsync` mapped to `SourceModMetadata`; `IsUpdateAvailableAsync` → `GetRecentlyUpdatedAsync`/version compare; `SetEndorsedAsync` → `EndorseAsync`. The API key comes from `IPluginHostServices.GetCredential("nexus")`, never embedded (operating law preserved).
- [ ] **Step 3:** `NexusPlugin : IModManagerPlugin` (`Id="nexus"`) `Register`s a `NexusModSource` built from `host.HttpClient` + `host.GetCredential`.
- [ ] **Step 4: Build** the plugin project: `dotnet build plugins/ModManager.Plugin.Nexus/ModManager.Plugin.Nexus.csproj`. Expected: 0 errors; it references **only** Abstractions (verify no Core reference crept in).
- [ ] **Step 5: Commit** (`feat(plugins): extract Nexus into ModManager.Plugin.Nexus implementing IModSource`).

### Task 8: Rewire the App to consume `IModSource` contributions through the shells

**Files:** `src/ModManager.App/ViewModels/MainViewModel.cs`, `ModRowViewModel.cs`, `src/ModManager.App/Services/NexusService.cs`, the settings dialog.

- [ ] Replace the hardcoded Nexus calls with calls through `ModSourceRegistry.ById("nexus")` (null when no plugin → the shells render nothing). The endorse heart, the metadata badges, the stats-refresh sweep, the connect/API-key settings section all read from the registered source via the shells (data + callbacks). Host owns the update debounce/stamp + credential store. Build (FULL): 0 errors. Commit.

### Task 9: Sign + locally deploy the Nexus plugin; FULL loads it

- [ ] Add a dev signing step (a script or `dotnet` target) that signs the built `ModManager.Plugin.Nexus.dll` with a **dev** ECDSA P-256 key and writes `ModManager.Plugin.Nexus.dll.sig`; paste that dev key's SPKI into `PluginSigningKey` for local testing (the production key is sub-project 5). Copy the dll + sig into the FULL build's plugins dir. Build + run FULL. Commit (`chore(plugins): dev-sign + deploy Nexus plugin locally`).

### Task 10: STORE flavor — no Nexus surface, core intact

- [ ] Build + run the STORE flavor; confirm no plugin loads (host compiled out), the registry is empty, and every core feature works with no Nexus UI present. Build-verify; smoke in Task 11.

### Task 11: Full verification + smoke checklist

- [ ] **Core suite:** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` → all green incl. `CorePurity` (Abstractions/Core pure), the new Plugins tests, no regression.
- [ ] **Both flavors build:** FULL + STORE, 0 errors (kill `ModManager.App` first).
- [ ] **Append to `docs/smoke-tests/pending.md`:**

```markdown
## Plugin host + Nexus slice (2026-06-15)

- [ ] **FULL loads the signed Nexus plugin.** With the dev-signed Nexus plugin in the plugins dir, launch the FULL build → endorse heart, endorsement/download/version badges, stats refresh, and update-check all work *through the plugin* (parity with the old in-core Nexus).
- [ ] **Tampered/unsigned plugin is refused.** Corrupt the plugin dll (or remove its .sig) → it does NOT load, the app starts clean, no crash, no Nexus surface.
- [ ] **STORE flavor is sealed.** Build/run `-p:Configuration=Store` → no plugin loads, no Nexus UI anywhere, and detect/intake/enable-disable/profiles/save+INI editors/ban-risk gate all work untouched.
- [ ] **API key never embedded.** Confirm the key is read from the on-machine store at call time and the plugin assembly contains no key.
```

- [ ] **Commit** the smoke checklist.

---

## Self-review notes (author checklist, run)

- **Spec coverage:** Abstractions contract = T1; signature gate = T2; registry/zero-plugins = T3; DTO↔ModMeta mapping = T4; PluginHost = T5; build flavors = T6; Nexus plugin = T7; App rewiring = T8; sign+deploy = T9; STORE sealed = T10; verify+smoke = T11. All spec "surfaces touched" rows covered.
- **Phasing honesty:** Phase A (T1–T6) is code-complete + TDD where unit-testable (Abstractions, signature, registry, mapping) and build-verified for the App host/flavors. Phase B (T7–T11) is build+smoke integration — the test project references Core only, and the extraction is the designed contract-churn point, so these tasks are precise-instruction (mirroring how App tasks were handled in the engine-detection + ban-risk plans), not fabricated line-complete code.
- **Reconcile-before-code flags:** T4 Step 1 (real `ModMeta` field names) and T7 (the exact Nexus files + their `ModMeta` usages) must be reconciled against live code when reached — called out explicitly, not left as silent placeholders.
- **Type consistency:** `IModSource`/`IModManagerPlugin`/`IPluginHostServices`, `SourceModRef`/`SourceModMetadata`/`EndorseResult`, `ModSourceRegistry`, `PluginSignature`/`PluginSigningKey`, `SourceMetadataMapper` — names consistent across tasks.
- **Laws:** Abstractions pure (`CorePurity` green); the host is App-side; the signature gate fails closed (empty pinned key until production); plugins get the credential per-call (no embed); no file-op laws touched this slice (read-only Nexus surface — the guarded-operation discipline is set up here for the EAC sub-project).
