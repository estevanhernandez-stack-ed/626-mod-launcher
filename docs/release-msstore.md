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
- **Nexus needs no flag.** Every configuration compiles it in from the pinned `external/626-mod-plugins`
  submodule, so `git submodule update --init` first on a fresh clone. There is no longer a switch for a
  Nexus-free build: `-p:StoreNexus` is gone, because an unexercised build variant is exactly how this
  repo once shipped Nexus-free packages nobody intended. The seal must still pass — the plugin LOADER
  is compiled out of Store either way.

### The two SKUs can no longer drift on Nexus

They used to. Nexus reached the off-Store build as a signed plugin downloaded from our feed, and the
Store build compiled it in from the pinned submodule — so the two could sit on different Nexus
versions, and this file used to carry a ritual for deciding, every plugin release, whether the Store
SKU followed.

**That split was never Microsoft's rule — it was Nexus's.** Their integration could not ship until
they approved us as a partner, and the plugin kept that surface off a certified package meanwhile. The
approval landed, the Store SKU shipped Nexus compiled in from 0.15.0 and certified repeatedly, and the
split outlived its reason.

Both builds now compile Nexus in from `external/626-mod-plugins`. One pin, both SKUs, no decision to
forget. Check it before a submission the same as ever:

```bash
git -C external/626-mod-plugins describe --tags
```

The plugin **loader** is unchanged and still FULL-only. It is the lane for plugins we want on GitHub
before, or instead of, the Store — it simply has nothing of ours left to load. `PluginFeedSource` is
kept and deliberately wired to nothing: it only ever served the Nexus plugin, and fetching that now
would re-install a file the app ignores.

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
6. **Listing languages — expand these.** Decided after the submission that got approved: every listing
   from here adds markets rather than shipping English-only. See below for what that does and does
   not mean.

## Listing languages are not app languages

Three separate things get called "languages" and only one of them is expensive. Confusing them is how
a shopper ends up being sold an English app in French.

| | What it is | Cost |
|---|---|---|
| **Store listing languages** | Translated description, what's-new, screenshots, in Partner Center | Translation only, no code |
| **Package declared languages** | The `<Resources>` block in `Package.appxmanifest` | Free, and a lie unless the UI is translated |
| **The app's UI language** | Actual localization | A real feature, see below |

**Add the first. Never add the second without the third.**

The manifest currently reads `<Resource Language="x-generate" />`, which derives the declared list
from resources actually present in the package. There are none, so it declares one. Leave it that
way. Hardcoding a language list there tells the Store to offer the app to people it cannot serve.

**Machine translation is worse than English here.** The listing is written in a specific voice —
builder-to-builder, second person, no corporate speak — and that voice is the first thing a machine
translator destroys. A listing that reads like a template in German is a worse signal than an honest
English one. Budget for a person, or add fewer markets.

**Where to start, and why.** The modding audience skews heavily toward German, Russian and Simplified
Chinese, and the last two are large for exactly the titles this launcher curates. English, German,
Russian and Simplified Chinese is a defensible first four. Add Polish, French, Spanish and Brazilian
Portuguese when there is someone to check them.

### What UI localization would actually take

Not a config flag. Two blockers sit in front of it, both deliberate choices made for other reasons:

- `Directory.Build.props` sets `InvariantGlobalization` to true, which strips ICU. Removing it changes
  real behaviour — the game picker's `StringComparer.CurrentCultureIgnoreCase` currently sorts
  invariantly and would start sorting per locale.
- `ModManager.App.csproj` deletes the Windows App SDK `.mui` folders for ~85 languages to save package
  size, keeping only `en-us`. Any language you ship has to survive that trim.

Then the volume: 443 hardcoded string literals in XAML and zero `x:Uid`, which is the mechanism WinUI
localization runs on.

**Do not hand-sweep those 443.** `vibe-lingual` exists for exactly this job — scan user-facing strings
by kind, emit a readiness brief, then run a confidence-routed extract, wire, translate and guard loop
that mutates only with per-file backups and is safe to re-run. Sweeping by hand first means doing its
work manually and then having nothing left for it to do but translate.

The catch is substrate. It is deep on next-intl and the Next.js App Router: JSX text, `aria-label`,
`alt`, toasts, `Intl` date handling, RTL surfaces. This app is WinUI 3 and XAML, where localization
runs on `x:Uid`, `.resw` and `ResourceLoader`. None of that overlaps.

What does transfer is the shape rather than the code: the scan-by-kind, the readiness brief, the
confidence routing that extracts clean sites and leaves genuinely ambiguous ones inline, the catalog
parity guard, and the no-literals ratchet. It also ships an adapter seam with an honest
not-yet-implemented path, so pointing it at a WinUI app stands down cleanly instead of mangling it.
**A WinUI adapter is the path, not a fork.**

So the order is:

1. **Move the Core string boundary.** Design work, a human call, and no tool should make it.
2. **Clear the two build blockers** above, since a translated app that cannot load its own language
   resources is not translated.
3. **Write the WinUI adapter**, then let `vibe-lingual` do the extract, the wire and the translate.

Steps 1 and 2 are the ones that have to be done by hand. Step 3 is the one that should not be.

**Do the Core boundary first, before any `x:Uid` sweep.** Roughly a hundred user-facing strings are
produced inside `ModManager.Core`, which is pure and has no access to WinUI resources —
`GameStateChip` hands the app a `Label`, a `Detail` and an `ActionLabel`, all in English. The right
shape already exists in that same record for a different reason: it also carries a stable `Id`,
because automation identity had to survive copy changes. Localization wants exactly that. Core hands
over a key and the data to fill it; the App owns every word. Moving that boundary after the XAML
sweep means doing the sweep twice.

## Open / before-launch

- **Sideload-smoke** the packaged app on a real machine (sign with a self-signed cert + trust it, or use the `AppPackages\..._Test\Add-AppDevPackage.ps1`) — confirm it launches packaged.
- Swap the generated `Assets\*Logo*.png` (resized from `icon.ico`) for branded art.
- Decide self-contained vs framework-dependent + whether to add an ARM64 bundle (currently x64 self-contained).
- Optional cleanup: gate the Velopack entry / `UpdateChecker` `#if FULL` (harmless no-op under MSIX today).
