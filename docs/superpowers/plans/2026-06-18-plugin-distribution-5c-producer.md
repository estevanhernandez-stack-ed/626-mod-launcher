# 5c-producer — Sign + distribute the plugin from the 626-mod-plugins repo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development for the in-repo coding tasks. Steps use checkbox (`- [ ]`) syntax. NOTE: this plan spans TWO repos and has operational + outward-facing steps that are **not** subagent-automatable — they are marked **[Este]** (maintainer action) or **[outward-facing — confirm first]**.

**Goal:** Stand up the producer half of plugin distribution: publish `ModManager.Plugins.Abstractions` as a NuGet package, move the Nexus plugin into the public `626-mod-plugins` repo where it builds against that package, and a CI there that signs the dll + the `plugins.json` index with the `PLUGIN_SIGNING_KEY` secret and publishes them on a release — so the 5c-consumer's feed URL stops 404ing and the FULL launcher can fetch the real signed plugin.

**Architecture:** Launcher repo packs + publishes the Abstractions contract as a versioned NuGet. The `626-mod-plugins` repo carries the plugin source + tests, references the package, and its CI builds → tests → signs (ECDSA P-256, reusing the `ManifestSigner` pattern, key from the repo secret) → publishes a GitHub release with the dll, `dll.sig`, `plugins.json`, and `plugins.json.sig` on the `latest` release. The launcher repo then drops the moved source.

**Tech Stack:** .NET 10, `dotnet pack`/`dotnet nuget push`, GitHub Actions, ECDSA P-256 signing (`tools/ManifestMiner/ManifestSigner.cs` is the reference signer).

## Global Constraints

- **The code-signing private key lives ONLY in the `626-mod-plugins` repo's `PLUGIN_SIGNING_KEY` secret** (already set, multi-line PKCS#8 PEM). It never enters the launcher repo or any other CI. The launcher binary pins only the public SPKI (`PluginSigningKey`, done in 5a).
- **Version coherence:** the Abstractions package version = the launcher release version (the release passes `-p:Version=<tag>` to the pack). The plugin's `minBinaryVersion` in `plugins.json` is set to the launcher version the plugin was built against, so the 5c-consumer gate (`minBinaryVersion ≤ running binary`) is coherent.
- **camelCase JSON:** the produced `plugins.json` uses camelCase keys (matches the 5c-consumer's `PluginIndex.TryParse`): `schemaVersion`, `plugins[]`, `id/displayName/version/minBinaryVersion/downloadUrl/sigUrl/sha256`.
- **Signature scheme:** ECDSA P-256 / SHA-256, `DSASignatureFormat.IeeeP1363FixedFieldConcatenation` — identical to `ManifestSignature`/`PluginSignature` (the consumer verifies with `PluginSignature.VerifyWithKey` against the pinned key). Sign the EXACT on-disk bytes of the dll and of `plugins.json`.
- **The launcher repo stays buildable at every step.** Nothing compile-references the plugin project (the App loads it via ALC at runtime); removing the source (Task 6) must keep Core + App (FULL + STORE) + the consumer fixture tests green.
- **Never bare `dotnet` at the launcher repo root.** Pack the explicit project. Conventional commits + trailer `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

## Task 1: Abstractions NuGet packaging (launcher repo) — ✅ DONE (2026-06-18)

**Files:** `src/ModManager.Plugins.Abstractions/ModManager.Plugins.Abstractions.csproj`

- [x] Configured packaging metadata: `IsPackable=true`, `PackageId=ModManager.Plugins.Abstractions`, MIT license, authors/description/repo URL, base `Version=0.6.0` (the release CI overrides with `-p:Version=<tag>`).
- [x] Verified `dotnet pack src/ModManager.Plugins.Abstractions/ModManager.Plugins.Abstractions.csproj -c Release` → `ModManager.Plugins.Abstractions.0.6.0.nupkg`, 0 warnings; `ModManager.Core` still builds against the (now packable) project reference, 0 errors.
- [x] Committed.

> If NuGet.org rejects the bare `ModManager.Plugins.Abstractions` id as taken/too-generic at publish time, prefix it (`Labs626.ModManager.Plugins.Abstractions`) and update the plugin's `PackageReference` to match. The consumer is unaffected (it never references the package — it ships the pinned key + fixture-tested code).

---

## Task 2: Choose the registry + provision publish auth — **[Este]** (operational gate)

The launcher needs somewhere to publish the package and the plugins repo needs to restore it. Two options:

- **NuGet.org (recommended for a public contract package).** Third-party plugin authors restore with no auth; the package is public + discoverable. Provision: create a NuGet.org account/org, mint an API key scoped to push `ModManager.Plugins.Abstractions`, add it as the launcher repo secret **`NUGET_API_KEY`**. Caveat: published versions are immutable (unlist, can't delete) — fine for a contract that only moves forward.
- **GitHub Packages (lower setup).** Publishes via the auto-provided `GITHUB_TOKEN` (no separate key), but **restore requires auth even for public packages** — every consumer (incl. third parties) needs a token + a `nuget.config` source. Friendlier to set up, less friendly to consume.

- [ ] **[Este]** Pick the registry and provision auth (the API key secret for NuGet.org, or confirm GitHub Packages). Tell the agent which — it changes only the publish step (Task 3) and the plugin's restore source (Task 4), not the package itself.

---

## Task 3: Publish the Abstractions package (launcher repo)

**Files:** `.github/workflows/release.yml` (add a pack+push step) OR a documented manual `scripts/publish-abstractions.ps1`.

- [ ] **Step 1:** Add a publish step gated on the chosen registry (Task 2). For NuGet.org, in the release job after the build:
  ```yaml
  - name: Pack + push Abstractions
    run: |
      dotnet pack src/ModManager.Plugins.Abstractions/ModManager.Plugins.Abstractions.csproj -c Release -p:Version=${{ github.ref_name }} -o ./nupkg
      dotnet nuget push ./nupkg/*.nupkg --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
  ```
  (`github.ref_name` is the tag, e.g. `v0.7.0` → strip the leading `v` if the pack rejects it; use a small step to normalize.) For GitHub Packages, push to `https://nuget.pkg.github.com/<owner>/index.json` with `GITHUB_TOKEN`.
- [ ] **Step 2: [Este / outward-facing]** Cut a launcher release (or run the manual script once) so `ModManager.Plugins.Abstractions <version>` exists on the registry. Verify it's restorable (`dotnet add package ModManager.Plugins.Abstractions --version <v>` in a scratch project). This unblocks Task 4.
- [ ] **Step 3:** Commit the workflow/script change (conventional + trailer).

---

## Task 4: Scaffold the plugins repo + move the plugin source (cross-repo) — **[outward-facing — confirm before push]**

**Repo:** `626-mod-plugins` (clone locally). **Moves OUT of the launcher repo:** `plugins/ModManager.Plugin.Nexus/`, `tests/ModManager.Plugin.Nexus.Tests/`.

- [ ] **Step 1:** Clone `626-mod-plugins`. Create the layout: `src/ModManager.Plugin.Nexus/` (the moved source) + `tests/ModManager.Plugin.Nexus.Tests/` (the moved tests) + a `nuget.config` if GitHub Packages was chosen.
- [ ] **Step 2:** Change the plugin csproj from the project reference to a package reference: `<PackageReference Include="ModManager.Plugins.Abstractions" Version="<launcher version>" />`. The test csproj keeps referencing the plugin + the package.
- [ ] **Step 3:** Build + test in the plugins repo: `dotnet test tests/ModManager.Plugin.Nexus.Tests/...` → green against the published package. Fix any reference drift (the source is unchanged; only the Abstractions reference shape changes).
- [ ] **Step 4: [outward-facing — confirm first]** Commit + push the plugins repo. (The launcher-side deletion is Task 6 — keep the source in both places until the plugins repo builds green, so nothing is ever homeless.)

---

## Task 5: Plugin signer + index + signing CI (plugins repo) — **[outward-facing — confirm before push]**

**Repo:** `626-mod-plugins`. **Reference:** `tools/ManifestMiner/ManifestSigner.cs` in the launcher repo (the exact ECDSA P-256 / `IeeeP1363FixedFieldConcatenation` signer to mirror).

- [ ] **Step 1: Signer.** A small C# tool/step that: signs the built `ModManager.Plugin.Nexus.dll` bytes → `ModManager.Plugin.Nexus.dll.sig`; computes its sha256 (lowercase hex); writes `plugins.json` (camelCase, the schema below) with `version` + `minBinaryVersion` = the launcher version the package targets + `downloadUrl`/`sigUrl` pointing at this release's assets; signs `plugins.json` → `plugins.json.sig`. The private key comes from the `PLUGIN_SIGNING_KEY` env (mapped from the secret), `ECDsa.ImportFromPem` (multi-line PEM survives via env), never interpolated into a shell line. Index shape:
  ```json
  { "schemaVersion": 1, "plugins": [ { "id": "nexus", "displayName": "Nexus Mods",
    "version": "<v>", "minBinaryVersion": "<v>",
    "downloadUrl": "https://github.com/estevanhernandez-stack-ed/626-mod-plugins/releases/download/nexus-v<v>/ModManager.Plugin.Nexus.dll",
    "sigUrl": ".../ModManager.Plugin.Nexus.dll.sig", "sha256": "<hex>" } ] }
  ```
- [ ] **Step 2: A signer round-trip test** (mirrors `ManifestSignerTests`): sign sample bytes, verify with the public SPKI → true; tamper → false. Lives in the plugins repo's test project.
- [ ] **Step 3: CI** (`.github/workflows/release.yml` in the plugins repo): on a `nexus-v*` tag → restore (the package) → build → test → run the signer → create a GitHub release with assets `ModManager.Plugin.Nexus.dll`, `.dll.sig`, AND publish `plugins.json` + `plugins.json.sig` to the repo's **`latest`** release (the stable consumer URL `releases/latest/download/plugins.json`). The `PLUGIN_SIGNING_KEY` secret is mapped to the signer's env.
- [ ] **Step 4: [outward-facing — confirm first]** Commit + push.

---

## Task 6: Remove the moved plugin source from the launcher repo

**Files:** delete `plugins/ModManager.Plugin.Nexus/`, `tests/ModManager.Plugin.Nexus.Tests/` from the launcher repo. **Keep:** `src/ModManager.Plugins.Abstractions/` (the package origin), the 5c-consumer code, the `tests/ModManager.App.NexusValidate.Tests/`.

- [ ] **Step 1:** Confirm nothing in the launcher repo compile-references the plugin project (grep for `ModManager.Plugin.Nexus` in `*.csproj` and `*.cs` outside the deleted dirs — expect only runtime/string references, none as a project reference).
- [ ] **Step 2:** Delete the two directories.
- [ ] **Step 3:** Verify the launcher repo is fully green: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` + `dotnet test tests/ModManager.App.NexusValidate.Tests/...` + FULL + STORE builds (kill the app first), all 0 errors/failures. The 5c-consumer fixture tests (hand-signed, test-key) do NOT depend on the real plugin source, so they stay green.
- [ ] **Step 4:** Commit (`refactor(plugins): plugin source now lives in 626-mod-plugins — drop it from the launcher repo`).

---

## Task 7: First release + live smoke — **[outward-facing — confirm first]** (the merge gate)

- [ ] **Step 1: [outward-facing]** Tag the plugins repo (`nexus-v<v>`) → CI signs + publishes the release + the `latest` `plugins.json`.
- [ ] **Step 2:** Verify the assets: `gh release view --repo estevanhernandez-stack-ed/626-mod-plugins` shows the dll + both sigs + index; the `latest` URL returns the signed `plugins.json`.
- [ ] **Step 3: [Este — live smoke]** On a FULL build with no plugin installed, connect a Nexus key → the consumer fetches → verifies (index sig + dll sig + sha against the pinned key) → installs → hot-loads → Nexus works. Confirm the heart/endorse/stats/identify loop end to end. (This is the smoke `docs/smoke-tests/pending.md` flagged as pending-producer.)
- [ ] **Step 4:** Once green end to end, the whole plugin arc is mergeable — proceed to finishing the branch (PR into master).

---

## Operational summary (the gates, in order)

1. ✅ Abstractions packs (Task 1, done).
2. **[Este]** pick registry + provision publish auth (Task 2).
3. publish the package (Task 3) — needs #2.
4. scaffold + move the plugin into the plugins repo against the package (Task 4) — needs #3; **confirm before push**.
5. signer + signing CI in the plugins repo (Task 5) — **confirm before push**.
6. drop the moved source from the launcher repo (Task 6) — in-repo, after #4 builds green.
7. **[outward-facing]** first release + live smoke (Task 7) — the merge gate.

## Self-review notes

- **Spec coverage:** the 5c-design's producer section — Abstractions NuGet (T1/T3), source move (T4/T6), plugins-repo CI signing the dll + index (T5), the stable `latest` feed URL (T5), version/`minBinaryVersion` coherence (Global Constraints + T5) — all mapped.
- **Why this isn't one autonomous build:** Tasks 2, 3-step-2, 4-step-4, 5-step-4, 7 are operational or outward-facing (publish auth, public repo pushes, a public release, a live smoke on the rig). They're sequenced + gated, not automated. The in-repo, in-my-control work (T1 done; T6 after the move) is subagent/agent-executable; the cross-repo scaffold I do via clone+push with confirmation.
- **Coherence guard:** nothing is ever homeless — the plugin source stays in the launcher repo until the plugins repo builds green (T4), and the launcher only drops it after (T6).
