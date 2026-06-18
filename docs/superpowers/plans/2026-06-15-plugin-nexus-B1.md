# Plugin slice — Phase B1: Nexus as a plugin (App-facing, Core untouched) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Stand up the real Nexus plugin and prove the host loop end-to-end on the App-facing features (endorse, stats refresh, update-check) consumed through the `IModSource` contract — **without** touching Core's `Scanner`/identify path (that's B2).

**Why staged:** The Phase-A whole-branch grep found Nexus woven into Core's read-path heart — `Scanner.cs` ([:1255,1291,1346](../../../src/ModManager.Core/Scanner.cs#L1255)) and `Ue4ssLuaInstaller.cs` ([:125](../../../src/ModManager.Core/Ue4ssLuaInstaller.cs#L125)) call `NexusClient` directly, plus the md5-identify path + ~12 tests. A clean full extraction is a read-path refactor (B2). B1 proves the contract + host on the user-facing Nexus actions while Core's `NexusClient` stays exactly where it is.

**The B1/B2 line:**
- **B1 (this plan):** the Nexus *plugin* implements `IModSource` over the Nexus REST API (its own lean client, references **only** `ModManager.Plugins.Abstractions`); the App's endorse heart / stats refresh / update-check route through `ModSourceRegistry.ById("nexus")` instead of Core's `NexusClient`. Core's `NexusClient` is **untouched** (Scanner still uses it for scan-time identify). Temporary, deliberate: two Nexus code paths until B2.
- **B2 (deferred, own spec):** rewire `Scanner` + `Ue4ssLuaInstaller` + md5-identify to the contract, delete `NexusClient`/`NexusRequests`/etc. from Core, relocate the ~12 Nexus tests to the plugin's test assembly. Removes the duplication.

**Tech Stack:** .NET 10, C#, xUnit. Pure Core + thin App; `CorePurityTests` (now also guards Abstractions). The plugin references only Abstractions + `System.Net.Http`. Test project references Core only — the plugin + App rewiring are build- + smoke-verified.

**Spec:** [`2026-06-15-plugin-host-nexus-slice-design.md`](../specs/2026-06-15-plugin-host-nexus-slice-design.md) · **Foundation:** Phase A (committed on this branch).

**Build/test (never bare `dotnet` at root; kill `ModManager.App` before App builds):**
- Core tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
- Plugin build: `dotnet build plugins/ModManager.Plugin.Nexus/ModManager.Plugin.Nexus.csproj`
- App FULL: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
- App STORE: add `-p:Configuration=Store`

---

### Task B1-1: The Nexus plugin (`IModSource` over the Nexus REST API)

**Files:**
- Create: `plugins/ModManager.Plugin.Nexus/ModManager.Plugin.Nexus.csproj` (references `..\..\src\ModManager.Plugins.Abstractions\ModManager.Plugins.Abstractions.csproj` ONLY)
- Create: `plugins/ModManager.Plugin.Nexus/NexusPlugin.cs` (`IModManagerPlugin`, `Id="nexus"`)
- Create: `plugins/ModManager.Plugin.Nexus/NexusModSource.cs` (`IModSource`)

- [ ] **Step 1: Read the reference** — `src/ModManager.Core/NexusClient.cs` + `NexusRequests.cs` + `NexusEndorse.cs` for the exact endpoints, headers (the ToS app headers + `apikey`), the endorse POST body shape, the md5 route, and the rate-limit/429 + precondition-refusal handling. The plugin re-implements a **lean** client for just the four `IModSource` operations (identify / metadata / update / endorse) over those same endpoints, mapping responses to the Abstractions DTOs. It does **not** copy all of `NexusRequests`/`NexusRefresh` — only what those four operations need.

- [ ] **Step 2: Implement `NexusModSource : IModSource`** — referencing only `ModManager.Plugins.Abstractions` + `System.Net.Http` + `System.Text.Json`. The HttpClient + API key come from the `IPluginHostServices` the plugin was handed (`host.HttpClient`, `host.GetCredential("nexus")`); the key is read per call, never stored. Mapping rules:
  - `IdentifyByHashAsync(gameDomain, md5)` → the md5 route → `SourceModRef(SourceId:"nexus", gameDomain, modId, version)`; null on 404/not-found.
  - `FetchMetadataAsync(ref)` → the per-mod route → `SourceModMetadata(Endorsements, Downloads, LatestVersion, Available: <reported>, Endorsed: null)`. **`Endorsed` MUST be null** — the per-mod endpoint carries no user-endorse state; endorse state is bulk-endpoint-owned (this is exactly the heart-wipe guard the mapper enforces).
  - `IsUpdateAvailableAsync(ref, installedVersion)` → newer-version check (the updated-by-game window or per-mod version compare) → bool.
  - `SetEndorsedAsync(ref, endorsed)` → the endorse/abstain POST → `EndorseResult(Ok, Refused, Message, NowEndorsed)`, degrading 4xx preconditions to `Refused=true` (never throw), surfacing the friendly message.
  - `RequiresApiKey => true`.
- [ ] **Step 3: Implement `NexusPlugin : IModManagerPlugin`** (`Id="nexus"`, `DisplayName="Nexus Mods"`) — `Register(host)` adds a `new NexusModSource(host.HttpClient, () => host.GetCredential("nexus"))`.
- [ ] **Step 4: Gate** — `dotnet build plugins/ModManager.Plugin.Nexus/ModManager.Plugin.Nexus.csproj` → 0 errors. **Verify it references ONLY Abstractions** (no ModManager.Core reference — grep the csproj + the assembly's referenced assemblies). Commit (`feat(plugins): Nexus plugin implementing IModSource over the Nexus REST API`).

### Task B1-2: Rewire the App's user-facing Nexus actions through the registry

**Files:** `src/ModManager.App/Services/NexusService.cs`, `src/ModManager.App/Services/NexusUpdatePoll.cs`, `src/ModManager.App/ViewModels/MainViewModel.cs`, `ModRowViewModel.cs`, `src/ModManager.App/MainWindow.xaml.cs`.

- [ ] **Step 1: Reconcile from live code** — find where the App calls Core's `NexusClient` for the three **user-facing** actions: the endorse heart toggle, the "Refresh Nexus stats" sweep, and the update-check/UPDATE chip. Those move to `ModSourceRegistry.ById("nexus")` (the loaded plugin's `IModSource`). **Leave Scanner's scan-time md5-identify on Core's `NexusClient`** — that's B2, out of scope here.
- [ ] **Step 2: Route through the registry** — the App resolves the active source via the shared `ModSourceRegistry` (the singleton from Phase A). When the source is null (STORE flavor / no plugin loaded), the endorse heart / stats / update surfaces are absent and the app is fully functional without them (the zero-plugins invariant). Map the plugin's `SourceModMetadata` onto `ModMeta` via `SourceMetadataMapper` (Phase A) so the heart is never wiped by a stats refresh.
- [ ] **Step 3: Gate** — kill `ModManager.App`; FULL build `-p:Platform=x64` → 0 errors; STORE build `-p:Configuration=Store` → 0 errors and **no user-facing Nexus surface** (registry empty). Commit (`feat(plugins): route App endorse/stats/update through the IModSource registry`).

### Task B1-3: Dev-sign + deploy the plugin; FULL loads it

**Files:** a dev-signing script/target; `src/ModManager.Core/Plugins/PluginSigningKey.cs` (dev SPKI for the branch).

- [ ] **Step 1:** Generate a **dev** ECDSA P-256 keypair (private key stays local/uncommitted). Sign the built `ModManager.Plugin.Nexus.dll` → `ModManager.Plugin.Nexus.dll.sig` (ECDSA P-256/SHA-256/IeeeP1363 — match `ManifestSignature.Format`). Paste the dev **public** SPKI bytes into `PluginSigningKey.PublicKeySpki` so the FULL build trusts the dev-signed plugin (production key is sub-project 5; this dev key is branch-local and replaced before any release). Copy the dll + sig into `%LOCALAPPDATA%\ModManagerBuilder\plugins\`.
- [ ] **Step 2: Gate** — FULL build 0 errors; the signed plugin is present in the plugins dir. (The functional "endorse works through the plugin" is the manual smoke in B1-4 — a GUI flow a build can't assert.) Commit (`chore(plugins): dev-sign + deploy the Nexus plugin for the FULL build`).

### Task B1-4: Verify + smoke

- [ ] **Step 1: Full Core suite** — `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` → all green incl. `CorePurity` (Core + Abstractions pure; the plugin is a separate assembly, not in the test project). No regression in the existing Nexus Core tests (they still test Core's untouched `NexusClient`).
- [ ] **Step 2: Both flavors** — FULL + STORE build 0 errors; re-confirm STORE dll has no `LoadFromStream`/`PluginHost`/`AssemblyLoadContext` (the Phase-A seal still holds).
- [ ] **Step 3: Smoke checklist** — append to `docs/smoke-tests/pending.md`:

```markdown
## Plugin slice B1 — Nexus as a plugin (2026-06-15)

- [ ] **FULL loads the dev-signed Nexus plugin** and the endorse heart, "Refresh Nexus stats", and the UPDATE chip all work *through the plugin* (parity with the old in-core App-facing Nexus). The plugin assembly contains no API key (read per-call from the on-machine store).
- [ ] **Stats refresh never wipes a heart** — endorse a mod, run a stats refresh; the filled heart survives (per-mod fetch returns Endorsed: null -> mapper preserves it).
- [ ] **Scan-time identify still works (B2 not done)** — Core's Scanner still enriches mods via its own NexusClient on add/rescan; md5-identify is unchanged.
- [ ] **STORE flavor** — no user-facing Nexus surface, no plugin loads, core fully functional; STORE dll has no loader symbols.
```

- [ ] **Step 4: Commit** the smoke checklist.

---

## Notes

- **Temporary duplication is intentional:** B1 has the plugin's lean Nexus client AND Core's `NexusClient` coexisting. B2 removes Core's copy when it rewires Scanner/identify to the contract. Don't try to unify them in B1 — that's the read-path refactor we deliberately deferred.
- **Laws:** plugin references only Abstractions (no Core); credential read per-call (operating law #2); `SourceMetadataMapper` prevents the heart-wipe; no file-op/reversibility surface (read-only Nexus actions); `CorePurity` green.
