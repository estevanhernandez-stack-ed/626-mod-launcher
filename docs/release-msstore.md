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

> **⚠ `-p:Version` does NOT set the MSIX package version.** That lives hardcoded in
> `src/ModManager.App/Package.appxmanifest` (`<Identity Version="…">`) and must be edited by hand
> before a Store build. Found while cutting 0.18.1: a build passing `-p:Version=0.18.1.0` produced a
> **0.17.0.0** package, because the manifest still said 0.17.0.0 and nothing reconciles the two. It
> looked correct on 0.17.0 only because the two numbers happened to match. Bump the manifest first,
> then verify the version out of the built bundle — never off the manifest on disk.

- **Locally:** bump `Package.appxmanifest`'s `Identity Version`, then
  `dotnet build src/ModManager.App/ModManager.App.csproj -c Store -p:Platform=x64` → `src/ModManager.App/AppPackages/.../ModManager.App_<v>_x64_Store.msixbundle`. Then `pwsh scripts/check-store-seal.ps1`.
- **In CI:** run the **Build Store MSIX (manual)** workflow with the version → download the `store-msixbundle-<v>` artifact.
- **With Nexus (the 0.15.0.0 line onward):** add `-p:StoreNexus=true`. Plain `-c Store` still builds the
  sealed, Nexus-free package; the flag compiles the Nexus source in from the pinned `external/626-mod-plugins`
  submodule (so `git submodule update --init` first on a fresh clone). Either way the seal must pass — the
  plugin LOADER is compiled out in both.

### Decide, every plugin release, whether the Store SKU follows

The two SKUs share one registration path — `ModSourceHostServices` and the same
`IModManagerPlugin.Register` entry point — so their BEHAVIOUR cannot fork. Their VERSION can.

The GitHub SKU picks up a new `nexus-vX` the moment it lands on the feed. The Store SKU does not: it
compiles from the pinned `external/626-mod-plugins` submodule, so it stays on whatever commit that
pointer names until someone moves it.

**So when you cut a plugin release, make it an explicit call:** should the Store SKU follow? If yes,
bump the submodule pointer, rebuild with `-p:StoreNexus=true`, and re-run the seal. If no, that is
fine — just make it a decision rather than a drift. Check the pointer before every Store submission:

```bash
git -C external/626-mod-plugins describe --tags
```

### ⚠ Never let a test build share the submission's output folder

A side-load test needs a throwaway package identity (so it installs beside the real Store app instead of
colliding with it), but a rebuild writes to the SAME `AppPackages` folder and **silently overwrites the
submission bundle**. That happened on 0.15.0.0: the uploaded package carried
`626LabsLLC.626ModLauncherNexusTest` and Partner Center rejected it with *"Invalid package identity name."*

So:

- Build test packages to a separate directory: add `-p:AppxPackageDir=<some temp dir>\` .
- **Verify the identity by reading it OUT OF the bundle** before uploading — never from the manifest on disk,
  which may have been edited and reverted since the bundle was produced:

```powershell
# unzip the .msixbundle, then the .msix inside it, then read AppxManifest.xml
([xml](Get-Content "<extracted>\AppxManifest.xml")).Package.Identity | Select-Object Name,Version
# expect: 626LabsLLC.626ModLauncher / <the submission version>
```

- Wipe `src/ModManager.App/AppPackages` before producing the real submission build.

## Submit (human-gated)

1. Partner Center → the reserved app → Packages → upload the `.msixbundle` (unsigned; the Store signs).
2. Listing: 626 voice, **Utility** category (not Game), screenshots from the themed app, accurate metadata, dependency disclosure.
3. Privacy policy URL (required): <https://github.com/estevanhernandez-stack-ed/626-mod-launcher/blob/master/PRIVACY.md>
   (hosted in-repo so it can be updated without waiting on the site). **Do not** point at 626labs.dev — that
   page still carries a "626 Labs never proxies third-party data" line that the CurseForge metadata proxy
   contradicts; see `docs/store/privacy-policy-update-for-nexus.md`.
4. **Age rating: re-run the questionnaire, never carry it forward, for any build that ships Nexus.** The app
   displays third-party mod listings fetched at runtime, so Online Content = Yes and Violence = Yes (mod
   screenshots for M-rated titles can depict combat and blood; the adult filter does not make the remainder
   violence-free). Expect a higher rating than the sealed SKU — that is correct, not a failure.
5. First cert round-trip: expect a question about writing into other publishers' game dirs — answer: "load-order utility for the user's own files, never bundles third-party binaries (see NOTICE)."

## Open / before-launch

- **Sideload-smoke** the packaged app on a real machine (sign with a self-signed cert + trust it, or use the `AppPackages\..._Test\Add-AppDevPackage.ps1`) — confirm it launches packaged.
- Swap the generated `Assets\*Logo*.png` (resized from `icon.ico`) for branded art.
- Decide self-contained vs framework-dependent + whether to add an ARM64 bundle (currently x64 self-contained).
- Optional cleanup: gate the Velopack entry / `UpdateChecker` `#if FULL` (harmless no-op under MSIX today).
