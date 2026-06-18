# Plugin slice — Sub-project 5c: plugin distribution (signed feed from GitHub) — design

**Date:** 2026-06-18
**Status:** Design — approved, pending spec review
**Branch:** `feat/plugin-host-nexus-slice` (the plugin arc) — producer work lands in the new `626-mod-plugins` repo
**Project:** `DP1YCsh7iAN1yAiR8sAd`

## Goal

Deliver the off-Store plugin to FULL installs. The plugin extraction (Phases A/B) and the signing keypair (5a) are done; `PluginHost` already verifies a detached signature and loads via a collectible `AssemblyLoadContext` (FULL-gated). What's missing is **delivery**: getting the signed plugin dll + its detached `.sig` *into* `%LOCALAPPDATA%\ModManagerBuilder\plugins\`, from a source that is **not** the Store-bound launcher release channel. This spec designs that delivery — a signed plugin feed on GitHub, fetched/verified/installed by FULL, with a contract-compatibility gate so a stale plugin can never load against a changed contract.

The STORE flavor is untouched: the loader compiles out under `#if FULL`, so none of this exists there.

## Decisions (resolved 2026-06-18)

| Fork | Decision |
|---|---|
| Where the plugin lives + is built/signed | **Separate public `626-mod-plugins` repo** with its own signing CI — keeps executable plugin code off the Store-bound launcher repo; plugins version independently. |
| Discovery + contract-compat gate | **Signed plugins index** (`plugins.json`, mirrors `games-manifest.json`): per-plugin `version`/`downloadUrl`/`sha256` + `minBinaryVersion`. Reuses the manifest trust model and carries the compat gate. |
| First-run UX | **Auto-fetch on Nexus connect** (the moment the user signals they want Nexus) + ~24h debounced re-check. Seamless; the plugin only arrives if Nexus is actually used. |
| Where the code-signing key lives when signing | **Plugins-repo CI** holds the private key as a secret (`PLUGIN_SIGNING_KEY`) and signs on release. Fully automated, mirrors the manifest model. Consequence: the plugin builds *in* that repo, so its source moves there and the contract ships as a NuGet. |

## Grounded current state (verified)

- `PluginHost.LoadAll` ([src/ModManager.App/Services/PluginHost.cs](../../../src/ModManager.App/Services/PluginHost.cs)) enumerates `*.dll` + sibling `*.dll.sig` in `PluginHost.PluginsDir` (`%LOCALAPPDATA%\ModManagerBuilder\plugins\`), verifies each via `PluginSignature.Verify` against the pinned `PluginSigningKey`, and loads the verified bytes in a collectible ALC. Fail-closed; one bad plugin never crashes startup. The whole file is `#if FULL`.
- `PluginSigningKey.PublicKeySpki` ([src/ModManager.Core/Plugins/PluginSigningKey.cs](../../../src/ModManager.Core/Plugins/PluginSigningKey.cs)) is the real minted ECDSA P-256 SPKI (5a). `PluginSignature` verify reuses `ManifestSignature` (ECDSA P-256, `DSASignatureFormat.IeeeP1363FixedFieldConcatenation`).
- **No contract-version field exists today.** `IModManagerPlugin.Register(IPluginHostServices)` is the only seam. A plugin built against an older `Abstractions` whose required member is gone throws at instantiation → caught → skipped (ungraceful but safe). 5c replaces that with an explicit `minBinaryVersion` gate.
- **Precedent to mirror:** `RemoteManifestSource` ([src/ModManager.App/Services/RemoteManifestSource.cs](../../../src/ModManager.App/Services/RemoteManifestSource.cs)) — fetch a signed JSON from a separate public repo, verify the detached ECDSA sig against a pinned key, validate/gate, cache on disk, ~24h debounce, fail-silent to the embedded fallback. The plugin feed is the same pattern applied to code instead of data.

## The trust chain — two signatures, one key

The pinned `PluginSigningKey` verifies **both** artifacts:

1. **The index** (`plugins.json` + detached `plugins.json.sig`) — proves the catalog is authentic: no MITM can swap a `downloadUrl`, forge a `sha256`, or lie about `minBinaryVersion`. Verify reuses `ManifestSignature`.
2. **Each plugin dll** (`*.dll` + detached `*.dll.sig`) — the load-time gate `PluginHost` already enforces against the same pinned key.

Defense in depth: even a forged index can't get malicious code *loaded*, because the dll's own signature fails closed at load. The index signature defends the **catalog** (downgrade/tamper of versions + URLs), not code execution. One key signs both — no second key to mint or pin.

## The index schema (`plugins.json`)

camelCase on disk (the convention rule), signed with a sibling `plugins.json.sig`:

```json
{
  "schemaVersion": 1,
  "plugins": [
    {
      "id": "nexus",
      "displayName": "Nexus Mods",
      "version": "1.0.0",
      "minBinaryVersion": "0.7.0",
      "downloadUrl": "https://github.com/estevanhernandez-stack-ed/626-mod-plugins/releases/download/nexus-v1.0.0/ModManager.Plugin.Nexus.dll",
      "sigUrl": "https://github.com/estevanhernandez-stack-ed/626-mod-plugins/releases/download/nexus-v1.0.0/ModManager.Plugin.Nexus.dll.sig",
      "sha256": "<hex of the dll>"
    }
  ]
}
```

The index + its `.sig` are published as assets on the repo's **latest** release, so the consumer has one stable feed URL: `https://github.com/estevanhernandez-stack-ed/626-mod-plugins/releases/latest/download/plugins.json` (and `…/plugins.json.sig`). The consumer rejects an index whose `schemaVersion` exceeds what it knows (forward-compat: a newer feed never breaks an older binary), and skips any plugin whose `minBinaryVersion` exceeds this binary's version.

> **Producer prerequisite — done (2026-06-18):** the public `estevanhernandez-stack-ed/626-mod-plugins` repo exists and holds the `PLUGIN_SIGNING_KEY` Actions secret (the minted PKCS#8 PEM, multi-line). The repo is otherwise empty until 5c-producer lands the source + CI; the secret sits unused (verified on the first CI sign via a sign→verify round-trip).

## 5c-producer — the `626-mod-plugins` repo

1. **Publish `ModManager.Plugins.Abstractions` as a versioned NuGet package.** This is the contract anchor: the plugin pins `Abstractions vN`, and `minBinaryVersion` derives from the launcher version that ships that contract. (Registry — NuGet.org or org-scoped GitHub Packages — is a plan-level detail.)
2. **Move the Nexus plugin source + its tests** (`plugins/ModManager.Plugin.Nexus/`, `tests/ModManager.Plugin.Nexus.Tests/`) to `626-mod-plugins`, building against the Abstractions NuGet instead of the in-repo project reference. (This re-homes the test project B2b-2 created — the logical end-state of "plugin in its own repo.")
3. **Repo CI** (on release): build the plugin → sign the dll + the index with the `PLUGIN_SIGNING_KEY` secret (PKCS#8 PEM, same scheme as `ManifestSigner`) → publish a GitHub release with the dll + `.dll.sig` + the updated, signed `plugins.json` + `.sig`. The private key lives only in this repo's Actions secret — never in the launcher repo.

The launcher repo retains: the published Abstractions source (the package's origin), the App-side fetcher, `PluginHost`, and the App-side `NexusService`/`NexusKeyValidator` (the credential store + key validate stay App-side, unchanged).

## 5c-consumer — the launcher repo (all `#if FULL`)

1. **`PluginFeedSource`** (App service, mirrors `RemoteManifestSource`): fetch `plugins.json` + `.sig` over the shared `HttpClient` → verify the sig against the pinned `PluginSigningKey` → parse + gate (`schemaVersion` known, `minBinaryVersion ≤ this binary`) → for each eligible plugin not already installed at its `version`: download the dll + `.sig` → verify `sha256` and the dll signature → **atomic** install into `PluginsDir` (write to temp, verify, rename; a failed/partial download never clobbers a working plugin).
2. **Hot-load:** after a mid-session install, call `PluginHost.LoadVerified` on the new dll so its `IModSource` registers into the live `ModSourceRegistry` and Nexus works immediately — no restart. Startup `LoadAll` picks it up on subsequent launches.
3. **Trigger + cadence:** when the user connects their Nexus key (`NexusService.ConnectAsync` success) and **no compatible plugin is installed yet**, fetch immediately — bypassing the debounce, since they want Nexus *now*. Once a plugin is installed, every fetch is an *update check* on a ~24h debounce (stamp at `%LOCALAPPDATA%\ModManagerBuilder\last-plugin-check.txt`, mirroring the update-check/manifest stamps). An installed-version record (small JSON in `PluginsDir`, camelCase) drives the "already at version" skip and the "is a plugin installed" decision.
4. **Settings surface:** a status line (e.g. "Nexus plugin v1.0.0 installed" / "not installed") + a "keep plugins updated" toggle (default on), mirroring the "auto-update definitions" toggle. The toggle gates the debounced re-check, not the initial connect-time fetch.

## Contract-version gate

`minBinaryVersion` is the whole mechanism. When the `Abstractions` contract changes: the launcher ships a release with the new contract, and the plugins repo rebuilds the plugin against the new `Abstractions` NuGet with a bumped `minBinaryVersion`. An older launcher never pulls a too-new plugin (gate); a newer launcher pulls the matching one. This is the graceful replacement for today's "stale plugin throws at load." Contract changes therefore require a coordinated launcher + plugin release — the cost the gate makes safe.

## Error handling — fail-silent, never break a working install

Every failure path — offline, bad index sig, unknown `schemaVersion`, `minBinaryVersion` too high, `sha256` mismatch, bad dll sig, partial download — results in: nothing installed or loaded, the failure logged via `AppDiagnostics`, and the app continuing with whatever is already installed (or zero plugins = the STORE path). The atomic verify-before-replace guarantees a failed update never degrades a working plugin. A bad feed can never break a working install — the same operating law as the manifest feed.

## Testing

- **Core (pure, headless):** index parse + `schemaVersion`/`minBinaryVersion` gate + `sha256`/signature verify logic — modeled on `ManifestValidator`/`RemoteManifestCache` tests. The version-compare and gate are pure functions in Core.
- **App:** `PluginFeedSource` over a mock `HttpMessageHandler` (happy path, bad sig, sha mismatch, min-version-too-high, offline) — modeled on the `NexusValidate` tests. Atomic-install + hot-load asserted with a temp `PluginsDir`.
- **Producer:** the plugins-repo CI runs the plugin's own test project (relocated) + a signer round-trip test (sign → verify), mirroring `ManifestSignerTests`.

## Decomposition

5c is two largely independent halves and should be **two implementation plans**, not one:
- **5c-producer** — Abstractions NuGet + source/test move + plugins-repo CI signing/publishing. Mostly lands in the new repo.
- **5c-consumer** — `PluginFeedSource` + gate + atomic install + hot-load + trigger/cadence + Settings. Lands in the launcher repo.

They meet only at the index schema + the trust chain (both defined here), so they can proceed in parallel once this spec is approved. Consumer can be built + tested against a hand-signed fixture index before the producer repo exists.

## Done-when

- A FULL install with no plugin, on connecting a Nexus key, fetches → verifies → installs → hot-loads the signed Nexus plugin, and Nexus works with parity to a manually-dropped plugin.
- A tampered index, a tampered dll, a `sha256` mismatch, an unknown `schemaVersion`, and a too-high `minBinaryVersion` each result in no install and a clean continue (covered by tests).
- The plugin builds in `626-mod-plugins` against the Abstractions NuGet; its CI signs + publishes the dll + `.sig` + signed `plugins.json`; the private key exists only as that repo's Actions secret.
- STORE flavor unchanged: no fetcher, no loader, no plugin surface (the consumer code is `#if FULL`).
- Core + App test suites green; the relocated plugin tests green in the producer repo.

## Surfaces touched

| Path | Change |
|---|---|
| `626-mod-plugins` (new repo) | plugin source + tests + signing/publishing CI + the `PLUGIN_SIGNING_KEY` secret |
| `src/ModManager.Plugins.Abstractions/` | published as a versioned NuGet package (build/pack/publish wiring) |
| `src/ModManager.App/Services/PluginFeedSource.cs` | **new** — fetch + verify + gate + atomic install + hot-load (FULL-only) |
| `src/ModManager.App/Services/PluginHost.cs` | expose a `LoadOne(dllPath)` entry for hot-load (reuse `LoadVerified`) |
| `src/ModManager.Core/Plugins/` | pure index model + parse + `schemaVersion`/`minBinaryVersion` gate + verify helpers |
| `src/ModManager.App/` (Settings + connect) | trigger on Nexus connect; "keep plugins updated" toggle + status |
| `plugins/ModManager.Plugin.Nexus/`, `tests/ModManager.Plugin.Nexus.Tests/` | **removed** from this repo (moved to the plugins repo) |

## Out of scope / deferred

- **Multiple plugins.** The index is a list and the fetcher loops, but only the Nexus plugin exists today. CurseForge / the EAC toggle reuse this delivery layer unchanged when they're extracted.
- **The Nexus category-label parity gap** (FULL no longer resolves Nexus category names) is a separate, already-documented decision (`docs/smoke-tests/pending.md`) — independent of distribution.
- **Plugin uninstall / disable UX** beyond a basic remove — a later polish.
- **Key rotation** — same as the manifest model: mint a new keypair, re-pin, ship a release; no separate revocation channel.
