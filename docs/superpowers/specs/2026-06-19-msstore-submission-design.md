# Microsoft Store submission — design + plan (supersedes the staged runbook)

**Date:** 2026-06-19
**Status:** Spec. Approved direction: spec-first, then build. Supersedes `docs/release-msstore.md` (the old "Phase 3, staged" runbook — keep it only as history; this is canonical).
**Grounding:** `docs/superpowers/research/2026-06-15-msstore-feasibility.md` (the feasibility verdict + policy citations) + repo state as of v0.8.1.

## The bottom line

A sealed-core Store SKU is viable and most of the way there. The plugin extraction (v0.8.0/0.8.1) and the ban-risk gate (v0.7.0) already cleared the two "Major" blockers the feasibility flagged. The remaining work is: **strip the one true dealbreaker (the EAC-disable toggle) from the Store flavor**, **convert the app from unpackaged to MSIX**, and a short list of submission line-items. The strategic decisions (dual-channel, strip-EAC) are settled — we built the plugin platform precisely so the Store SKU could be a sealed core. This is execution, ~1-3 weeks calendar, mostly first-submission iteration, $0-99 one-time.

## What's already solved (do not redo)

- **In-app Nexus browsing → UGC/storefront obligations (was Major).** Solved by the plugin extraction. The STORE flavor (`Configuration=Store`, `FULL` undefined) compiles out `PluginHost` / `PluginFeedSource` / the Nexus surface entirely — binary-verified absent. The Store SKU is "manage your own installed game files," the exact clean framing a reviewer wants. No CurseForge/Nexus ToS exposure for Store (the browser isn't there).
- **Ban-risk gate (was Major: "designed not built").** Shipped v0.7.0 — `BanRiskCatalog` / `GameBanRisk` + the warn-and-acknowledge enable path. The "honest about what it does" story is live.
- **STORE flavor exists + builds clean** (`-p:Configuration=Store`), and CI already exercises both flavors.

## The SKU shape (decided)

**Store SKU = sealed core.** No plugin surface (done), **no EAC-disable toggle** (to do), no in-app mod browser (done — it was plugin-gated). Full feature set stays on the GitHub/Velopack FULL SKU. One codebase, one thin build-flavor gate (`#if FULL`), two artifacts per release.

## The dealbreaker: gate the EAC-disable toggle out of STORE

`src/ModManager.Core/AntiCheat.cs` is a reversible EAC bootstrapper-exe swap (`start_protected_game.exe` ↔ a copy of the real game exe, original parked as `.626off`). It lets a modded game launch with anti-cheat off. To a reviewer reading the source it's plainly an anti-cheat bypass — the feasibility's load-bearing review risk (high-risk at reviewer discretion; no black-letter ban, no precedent either way). The clean move is to make it **absent from the Store SKU**, surface and mechanism.

Two gating levels — the spec requires both for a defensible "stripped" state, mirroring how the plugin host is binary-verified absent:

1. **App surface (required).** Gate every EAC-disable affordance under `#if FULL`:
   - `MainWindow.xaml.cs` → `AddAntiCheatToggle` (the Launch Options card with the "Switch to offline mode (anti-cheat off)" button).
   - `MainViewModel` → `SetAntiCheat`, `AntiCheatStateOf`, and the `LaunchOptionKind.AntiCheatToggle` row that exposes it.
   - Any `LaunchOptions` entry of kind `AntiCheatToggle` must not be produced in STORE.
   - Result: the Store app has no anti-cheat-off affordance at all.
2. **Core mechanism (recommended — binary absence).** Define `FULL` in `ModManager.Core.csproj` for non-Store configs (mirror the App's `Condition="'$(Configuration)' != 'Store'"` → `DefineConstants;FULL`), and wrap `AntiCheat.cs` in `#if FULL`. Then a STORE Core build omits the bypass code entirely — it can't be decompiled out of the shipped binary because it isn't there. Tests build FULL (Debug/Release), so `AntiCheatTests` keep running. Verify `CorePurityTests` is unaffected (it guards WinUI/WinRT leaks, orthogonal to flavor).

**Seal check (required).** Add an automated STORE-seal assertion so the gate can't silently regress: a CI step (in `release-msstore.yml`, and ideally mirrored in `release.yml`) that builds the STORE binaries and greps the produced `ModManager.App.dll` + `ModManager.Core.dll` for forbidden symbols — `PluginHost`, `PluginFeedSource`, `AntiCheat`, `Disable`/`Enable` bootstrapper entry points — failing the build if any appear. This is the same binary-symbol-scan the v0.8.1 purity review ran by hand; make it a gate.

## MSIX packaging

The app is unpackaged today (`WindowsPackageType=None`, Velopack installer, custom `Program.cs` entry running `VelopackApp.Build().Run()` before WinUI). Store requires MSIX (do NOT submit the Velopack EXE via the Store's EXE path — Microsoft won't re-sign it, which defeats the whole point).

- **Single-project MSIX** on `ModManager.App` (`<EnableMsixTooling>true</EnableMsixTooling>` + a `Package.appxmanifest`), built for the Store flavor only. Prefer single-project over a separate `ModManager.Package` `.wapproj` unless the WinAppSDK tooling forces the latter.
- **Manifest capabilities: `runFullTrust` ONLY.** Do **not** declare `broadFileSystemAccess` — the feasibility corrected the old runbook on this: a full-trust mediumIL desktop MSIX writes to user-writable folders via ordinary Win32 I/O under NTFS; `broadFileSystemAccess` is a UWP/sandbox concept that, if declared, is a scrutinized capability inviting review friction the app doesn't need.
- **Identity/category:** Utility, not Game (dodges the gaming-specific policy rules). Publisher identity from Partner Center.
- **Architectures:** x64 + ARM64 `.msixbundle` (the App already targets `Platforms=x64;ARM64`).
- **Velopack reconciliation:** the Store flavor must NOT run the Velopack updater (Store manages updates). `UpdateChecker` already no-ops when not a Velopack install (`UpdateManager.IsInstalled` false), so it likely stands down cleanly under MSIX — **confirm** that, and if it doesn't, gate `UpdateChecker` (and any `VelopackApp.Build().Run()` update side effects) under `#if FULL`. The custom entry point must still launch WinUI correctly when packaged.

## Audit before packing (MSIX-compat)

The feasibility flagged these as unverified — audit during the build, not assumed:

- **Own-exe-path reads.** Anywhere the app reads its own EXE location / `AppContext.BaseDirectory` / single-file extraction path — MSIX install layout differs from a Velopack install. Grep and verify each survives packaging.
- **App-local copy targets** (VC++ runtime, `Assets/icon.ico`, resources) — confirm they land correctly in the MSIX layout.
- **The Velopack entry point** under MSIX — `VelopackApp.Build().Run()` should be a no-op when not Velopack-installed, but verify it doesn't throw or misbehave packaged.

## CI

- **`release-msstore.yml`** — manual-dispatch only (`workflow_dispatch`). Builds the STORE-flavor `.msixbundle` (x64 + ARM64), runs the STORE-seal symbol check, uploads the bundle as an artifact. **Submission stays human-gated** — no auto-publish to Partner Center in v1. (A later iteration can add the Store submission API / StoreBroker, still human-approved.)
- Keep the existing `release.yml` (Velopack/GitHub FULL SKU) exactly as-is — the two channels are independent.

## Este's external track (cannot be done from the repo)

1. **Partner Center registration.** Individual is free (as of Sept 2025); company is $99 one-time. **Pre-req to verify before assuming $0:** whether the free individual tier can publish an app declaring `runFullTrust`, or whether the restricted-capability tier requires the $99 company account. The feasibility left this unverified — confirm in Partner Center before committing to a tier.
2. **Privacy policy** (mandatory — the Nexus key is PII on the FULL SKU; the Store SKU has no Nexus, but a privacy policy URL is still required at submission). A short honest policy: no telemetry, key stays on-machine, no server-side collection.
3. **Listing** — copy in the 626 voice ("load-order utility for your own installed game files"), screenshots from the themed app, Utility category, accurate metadata, dependency disclosure.
4. **First cert round-trip** — budget calendar time; expect a reviewer question about writing into other publishers' game directories. Answer: "load-order utility for the user's own files, never bundles third-party binaries — see NOTICE."

## Open / unverified items (resolve before or during)

- Partner Center tier vs `runFullTrust` (Este — item 1 above).
- Own-exe-path / MSIX layout behavior (audit during Task: MSIX).
- Whether ARM64 is worth shipping v1 (x64 covers the vast majority of modded-PC gamers — could ship x64-only first, add ARM64 later). Decision for the build.
- Self-contained vs framework-dependent MSIX (decide before packing — self-contained is simpler for the user, larger download).

## Proposed build sequence (after this spec is approved)

1. **EAC gate** (App surface `#if FULL` + Core `AntiCheat.cs` `#if FULL` + Core csproj FULL constant) + the STORE-seal symbol check. Tests + both flavors green. *This is shippable on its own and hardens the FULL SKU's story too.*
2. **MSIX packaging** — `EnableMsixTooling`, `Package.appxmanifest` (`runFullTrust` only), Store-flavor bundle build; the own-exe-path + Velopack-entry audit; confirm `UpdateChecker` stands down.
3. **`release-msstore.yml`** (manual-dispatch, builds the bundle + runs the seal check).
4. **Supersede `release-msstore.md`** — point it at this spec, fix the `broadFileSystemAccess` error, mark what's done.
5. **Hand off to Este** for Partner Center + listing + submission (his track can start in parallel at step 1 — the account registration + tier verification gate nothing on my side).

## Decision log pointer

Log the SKU-shape + EAC-strip decision to the 626 dashboard when the build starts (project `DP1YCsh7iAN1yAiR8sAd`), tagged to this spec.
