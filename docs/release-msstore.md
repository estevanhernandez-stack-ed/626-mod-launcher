# Microsoft Store release flow

> **Canonical design:** [`docs/superpowers/specs/2026-06-19-msstore-submission-design.md`](superpowers/specs/2026-06-19-msstore-submission-design.md).
> This file is the maintainer quick-reference; the spec carries the rationale, the policy citations, and the full task breakdown. (The old "Phase 3 — staged, not wired" runbook + its `broadFileSystemAccess` guidance is superseded — that capability is **not** declared; a full-trust desktop MSIX writes via ordinary Win32 I/O.)

## What's wired (as of v0.8.1 line)

- **Sealed Store SKU.** `Configuration=Store` leaves `FULL` undefined: the off-Store plugin loader and the EAC-disable toggle (`AntiCheat.cs`) are compiled out and **binary-verified absent**. The in-app Nexus browser was already plugin-gated. So the Store SKU is "manage your own installed game files."
- **MSIX packaging.** The Store flavor packs as a single-project MSIX (`src/ModManager.App/Package.appxmanifest`) — unsigned `.msix` + `.msixbundle` (the Store re-signs). `runFullTrust` is the only declared capability.
- **Seal gate.** `scripts/check-store-seal.ps1` proves the strip (fails if `PluginHost` / `PluginFeedSource` / `WirePluginFeed` / `.626off` / `AntiCheatState` leak in). Run it before any submission.
- **CI.** `.github/workflows/release-msstore.yml` — manual-dispatch only: builds the bundle, runs the seal gate, uploads the `.msixbundle` artifact. Submission stays human-gated.

## Reserved product identity (Partner Center, publisher 626Labs LLC)

| Field | Value |
|---|---|
| Package Identity Name | `626LabsLLC.626ModLauncher` |
| Publisher | `CN=177BCE59-0966-4975-9962-10E36652141F` |
| Publisher display name | `626Labs LLC` |
| Package Family Name | `626LabsLLC.626ModLauncher_wz1chhb2h2v4a` |
| Store ID | `9N53V6RRJK95` |

These live in `Package.appxmanifest` (Name + Publisher + PublisherDisplayName). The PFN / Store ID are Partner-Center-side reference.

## Build a Store bundle

- **Locally:** `dotnet build src/ModManager.App/ModManager.App.csproj -c Store -p:Platform=x64 -p:Version=<v>` → `src/ModManager.App/AppPackages/.../ModManager.App_<v>_x64_Store.msixbundle`. Then `pwsh scripts/check-store-seal.ps1`.
- **In CI:** run the **Build Store MSIX (manual)** workflow with the version → download the `store-msixbundle-<v>` artifact.

## Submit (human-gated)

1. Partner Center → the reserved app → Packages → upload the `.msixbundle` (unsigned; the Store signs).
2. Listing: 626 voice, **Utility** category (not Game), screenshots from the themed app, accurate metadata, dependency disclosure.
3. Privacy policy URL (required): no telemetry, Nexus key stays on-machine, no server-side collection. (Store SKU has no Nexus surface anyway.)
4. First cert round-trip: expect a question about writing into other publishers' game dirs — answer: "load-order utility for the user's own files, never bundles third-party binaries (see NOTICE)."

## Open / before-launch

- **Sideload-smoke** the packaged app on a real machine (sign with a self-signed cert + trust it, or use the `AppPackages\..._Test\Add-AppDevPackage.ps1`) — confirm it launches packaged.
- Swap the generated `Assets\*Logo*.png` (resized from `icon.ico`) for branded art.
- Decide self-contained vs framework-dependent + whether to add an ARM64 bundle (currently x64 self-contained).
- Optional cleanup: gate the Velopack entry / `UpdateChecker` `#if FULL` (harmless no-op under MSIX today).
