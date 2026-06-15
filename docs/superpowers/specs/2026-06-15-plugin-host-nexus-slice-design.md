# Plugin host + Nexus vertical slice — design

**Date:** 2026-06-15
**Status:** Design — approved in brainstorm, pending plan
**Branch:** `feat/plugin-host-nexus-slice`
**Project:** `DP1YCsh7iAN1yAiR8sAd`

## Why

The Microsoft Store feasibility ([`2026-06-15-msstore-feasibility.md`](../research/2026-06-15-msstore-feasibility.md)) found that the launcher's Store-unfriendly surfaces — the in-app third-party mod-source browsing (Nexus/CurseForge → Store policy 10.1.6 / 11.12 / 11.13) and the EAC anti-cheat toggle (reviewer-discretion risk) — are exactly the features a clean Store SKU shouldn't carry. Rather than strip them, **break them out as plugins from a separate signed source**, leaving a maximally clean core. A Store app also can't download + run code the Store didn't review — so the plugin *host itself* ships only in the GitHub build, and the Store SKU is a **sealed core with the host compiled out**. One stroke clears the storefront/UGC rules, the no-external-code rule, and the EAC risk. (This mirrors the dual-channel pattern Este ran on a prior project: clean core on the Store, add-on plugins from elsewhere.)

This spec is **sub-project 1** of that platform shift — the foundational vertical slice. Sequencing chosen: Nexus first (the hardest, most-woven integration forces an honest contract before others depend on it), expecting one contract churn.

## Decomposition (the platform, for context)

1. **Plugin host + contract + Nexus as the first plugin** — *this spec.*
2. CurseForge → second plugin (proves the contract generalizes).
3. EAC toggle → plugin (the file-op one; obeys the reversibility laws).
4. Separate signed plugin source + in-app install/trust (the "from elsewhere" half).
5. Store-clean build flavor hardening + MSIX packaging (ties back to the feasibility).

(Sub-projects 4–5 are partially seeded here — local signed loading + the build-flavor gate — but full remote distribution and MSIX are their own specs.)

## Scope of this slice

**In:** the slim Abstractions assembly; an App-side `PluginHost` that verifies + loads signed plugin assemblies from a local plugins dir; the build-flavor gate (`FULL` vs `STORE`) that compiles the host out of the Store SKU; the Nexus integration extracted into a signed plugin implementing the contract; the core's UI shells that plugins populate; the DTO↔`ModMeta` mapping; the host-owned credential store + update-debounce.

**Out (deferred):** remote plugin distribution / a plugin feed + in-app browse-install (sub-project 5); MSIX packaging of the Store flavor (sub-project 5/6); CurseForge (sub-project 2); the EAC toggle (sub-project 3); NXM protocol-link handling and bulk-endorsement sync polish (the *expected churn* — wired minimally here, hardened once Nexus teaches the contract its final shape).

## Architecture (Section 1)

Four parts:

- **`ModManager.Plugins.Abstractions`** (NEW, pure assembly) — the extension-point interfaces + DTOs a plugin implements. Pure .NET, no WinUI, no dependency on full Core. A plugin references *this slim surface only*, never all of Core. This is the stable contract.
- **`PluginHost`** (App-side, `FULL` flavor only) — discovers plugin assemblies in a plugins dir, **verifies a detached ECDSA P-256 signature against a pinned plugin-signing key** (the `ManifestSignature` pattern, applied to code), loads each in a collectible `AssemblyLoadContext`, instantiates the plugin entry type, and registers its contributions into the app's DI/registries. A bad/missing signature → not loaded, no crash.
- **Build-flavor gate** — `FULL` (GitHub) compiles the host in; `STORE` compiles it out entirely (no loader, no external code → Store-policy clean). The compiled line between the two SKUs.
- **The Nexus plugin** — `NexusClient` + its App-side orchestration, extracted into a signed assembly implementing the Abstractions contracts.

**Design stances (approved):**
1. **Plugins are logic + declarative contributions — no XAML in plugins.** Core owns all WinUI and exposes *shells* (per-row actions area, metadata-badge slot, settings-section host); plugins populate them via data + callbacks. Keeps plugins clean .NET, avoids runtime-loaded XAML, keeps the App the only WinUI layer. `CorePurityTests` discipline extends — Abstractions stays pure.
2. **Plugins get no law-bypass.** A plugin that touches files (EAC, later) goes through the same reversible primitives the core enforces — the host hands plugins *guarded* operations, never raw access. Reversibility, camelCase-on-disk, and the forbidden-paths gate hold across the boundary.

## The contract surface (Section 2)

Sized to exactly what Nexus needs — the slice contract, expected to churn once:

- **`IModSource`** — the plugin's primary type (`INexusClient` generalized to "a mod-source site"): identity (`id="nexus"`, display name), plus **identify** (file hash → source mod ref), **fetch metadata** (ref → endorsements / downloads / version / availability), **check update** (ref + installed version → is-newer), and the one write, **endorse/abstain** (ref + bool → result, with the graceful precondition refusals already built). The contract speaks in **Abstractions DTOs**, not Core's `ModMeta`.
- **Host-owned credential store** — the plugin *declares* "I need a per-user API key"; the host stores it on-machine and injects it at call time. The operating law survives the boundary — **the plugin never embeds or exfiltrates the key.**
- **Contributions into the core's shells** (data + callbacks, no plugin XAML):
  - a **row action** = the endorse heart (icon, tooltip, visibility = "this mod is identified on this source," async toggle, filled/empty state);
  - **metadata** flows into the core's *generic* mod-source fields on `ModMeta` (endorsements/downloads/version/availability — not Nexus-specific; CurseForge has them too, so they stay in core, populated by the active source plugin, persisted camelCase as today);
  - a **settings section** = connect/disconnect + key entry, dropped into the core's settings host;
  - an **update signal** feeding the existing UPDATE chip — **the host owns the debounce + 24h stamp**, the plugin only answers "is there a newer version," rate-limit/429-aware.

## What moves vs. stays + the gate (Section 3)

**Moves out** to the signed Nexus plugin assembly:
- `NexusClient` ([`src/ModManager.Core/NexusClient.cs`](../../../src/ModManager.Core/NexusClient.cs)) — HTTP client, rate-limit/429 handling, md5 identify, endorse POST, bulk endorsements.
- The App-side Nexus orchestration (`NexusService` + the Nexus-specific bits of `MainViewModel`/`ModRowViewModel`) — reworked to implement `IModSource` + register contributions.

**Stays** in core/App:
- `ModMeta` + its generic mod-source fields — the contract speaks Abstractions DTOs; **the core maps DTO → `ModMeta`** (this is what keeps the plugin off all-of-Core; purity holds — Abstractions is pure, the host is App-side, `CorePurityTests` stays green).
- The UI shells, the credential store, the update debounce/stamp, the camelCase persistence, and `NexusDomains` slug resolution (manifest-feed data — core owns it, hands the resolved domain to the plugin).

**Build-flavor gate:**
- `FULL` compiles `PluginHost` in; `STORE` compiles it out — no loader, no external code, Store-policy clean.
- The Nexus plugin loads from a **local signed plugins dir** for this slice (assembly + detached ECDSA sig, verified against the pinned plugin-signing key before load). Remote distribution is sub-project 5.

**The load-bearing invariant — the core runs fully with zero plugins.** Every contribution is additive: no plugin → empty row-actions area, no source badges, no connect setting, and every core feature (detect, intake, enable/disable, profiles, save/INI editors, ban-risk gate) works untouched. This is what makes the Store SKU a real product *and* proves the boundary is clean.

## Done-when

- `FULL` flavor loads the signed Nexus plugin and **endorse + enrich + update-check all work through the contract** (parity with today's in-core Nexus behavior).
- `STORE` flavor builds with the host compiled out and the app runs clean with **no Nexus surface at all** and no regression in any core feature.
- A tampered/unsigned plugin is refused by the host (no load, no crash).

## Testing

- **Pure-Core / host-testable (xUnit):** the Abstractions contract; the DTO↔`ModMeta` mapping; the signature-verify-before-load gate (mirrors the `ManifestSignature` tests — valid sig loads, tampered/missing sig refused); the "core with zero plugins" path (registries empty, features intact); the host-owned update-debounce/stamp.
- **App-side (build + smoke):** the actual `AssemblyLoadContext` load of the real Nexus plugin; the WinUI shell rendering of plugin contributions (endorse heart, badges, settings section); the `FULL` vs `STORE` flavor builds. Append to `docs/smoke-tests/pending.md`.
- **Laws:** `CorePurityTests` green (Abstractions pure, host App-side); reversibility/camelCase/forbidden-path unaffected (read-only Nexus surface this slice; the guarded-operation discipline is established here for the EAC sub-project).

## Surfaces touched

| Path | Change |
|---|---|
| `src/ModManager.Plugins.Abstractions/` | **NEW** — `IModSource` + extension-point contracts + DTOs (pure) |
| `src/ModManager.App/Services/PluginHost.cs` | **NEW** — discover + verify-signature + `AssemblyLoadContext` load + register contributions (`FULL` only) |
| `src/ModManager.Core/` (signature verify) | reuse/extract the `ManifestSignature` ECDSA verify for plugin assemblies; a pinned plugin-signing public key |
| `src/ModManager.App/` UI shells | per-row actions area, metadata-badge slot, settings-section host (core-owned WinUI populated by contributions) |
| `src/ModManager.App/` build flavors | `FULL` vs `STORE` configuration; host compiled out of `STORE` |
| `plugins/ModManager.Plugin.Nexus/` (NEW assembly) | `NexusClient` + Nexus orchestration → `IModSource` impl, signed |
| `src/ModManager.Core/ModMeta` + mapping | generic mod-source fields populated via DTO mapping (camelCase round-trip test) |
| `tests/ModManager.Tests/` | Abstractions contract, DTO↔ModMeta mapping, signature-verify gate, zero-plugins path, debounce |
| `docs/smoke-tests/pending.md` | FULL-loads-Nexus / STORE-sealed / tampered-plugin-refused smoke entries |
