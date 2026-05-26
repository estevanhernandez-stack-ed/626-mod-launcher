# How to ship a release

> Internal doc — the audience is whoever's pushing the next tag. Not for the public README.

## TL;DR — the happy path

```bash
# from a clean master, tests green
git tag v0.2.0
git push origin v0.2.0
```

CI builds, packs Velopack, uploads a **DRAFT** GitHub Release with every artifact attached. Open the draft, review, write release notes in the body, click **Publish**. Auto-update rolls out to existing installs within 24 hours (debounced).

That's it.

## What CI does

The workflow (`.github/workflows/release.yml`) fires on any `v*.*.*` or `v*.*.*.*` tag push:

1. **Restore + test** the Core suite (`dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`). A red test fails the release.
2. **Fetch prior release** so `vpk` can compute a delta against the previous `*-full.nupkg`. First-ever release tolerates the 404.
3. **Build Velopack release** via `scripts/build-velopack-release.ps1`. Self-contained .NET 10 publish → `vpk pack` → installer + nupkgs + manifest under `dist/release/`.
4. **Upload artifacts** as a 30-day GitHub Actions artifact (audit trail).
5. **Push to GitHub Releases** as a DRAFT. You review the draft, write notes, click Publish.

To auto-publish without the draft step, use **Actions → Release → Run workflow** with `publish: true`. Always works from a tag.

## Versioning

- 3-part SemVer (`0.2.0`) or 4-part (`0.2.0.0`) — both accepted.
- The 4th segment becomes the `AssemblyVersion`. Velopack uses 3-part (`packVersion`) — the script strips the 4th automatically.
- Pre-releases: tag normally, then in the **Run workflow** UI tick `pre: true`. Marks the GitHub Release as Pre-release.

## Auto-update mechanics

- Every shipping build is installed via `626ModLauncher-win-Setup.exe`. Velopack scaffolds a per-user install under `%LOCALAPPDATA%\626ModLauncher\`.
- On launch, `UpdateChecker` (see `src/ModManager.App/Services/UpdateChecker.cs`) pings `releases.win.json` on the GitHub Releases page. Debounced to once per 24 hours via a stamp file at `%LOCALAPPDATA%\ModManagerBuilder\last-update-check.txt`.
- v1 cut: detected updates are recorded on `UpdateChecker.AvailableVersion` but not auto-downloaded. Wire the toolbar "Update available" indicator + download + apply-on-restart through this same service when the UI lands.
- Dev runs (`bin/Debug`) and portable zips don't auto-update — `UpdateManager.IsInstalled` is `false` and the check exits cleanly.

## SmartScreen

First-time installs trigger SmartScreen ("Windows protected your PC"). Click **More info → Run anyway**. This is the v1 trade for shipping without a code-sign cert ($250-700/yr). Reputation builds with downloads — after a few hundred installs the warning typically goes away.

For the Microsoft Store path (which signs through Microsoft + bypasses SmartScreen), see the agent-access design sketch's adjacency notes — that's a separate flow, not blocking on it.

## Smoke locally before tagging

Before the first tag of a new version, run the script by hand to make sure the Velopack pack succeeds with the current build:

```pwsh
# from repo root
dotnet tool install -g vpk  # one-time
pwsh scripts/build-velopack-release.ps1 -Version 0.2.0
```

Check `dist/release/` — you should see:

- `626ModLauncher-win-Setup.exe` — the installer
- `626ModLauncher-0.2.0-win-full.nupkg` — full payload
- `releases.win.json` — auto-update manifest

(Delta nupkgs only appear from release #2 onward — first release has no prior to diff against.)

Run the Setup.exe on a clean Windows install (or a VM, or another user account). It should land in **Start → 626 Mod Launcher**, write nothing outside `%LOCALAPPDATA%\626ModLauncher\`, and uninstall cleanly via the standard Add/Remove Programs.

## Things that can go wrong

- **`vpk pack failed`** — usually a publish output issue. Check `dist/publish/` exists and has `ModManager.App.exe`. The publish step bundles VC++ runtime + trims WinUI MUI + strips the AI runtime via csproj targets; those should run automatically.
- **File-copy MSB3027 errors** during local build — the running app holds the DLL. Kill it (`Stop-Process -Name ModManager.App -Force`) and re-run.
- **CI test step fails** — run `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` locally with the same arguments. Don't merge without green Core.
- **Velopack reports "not installed" in dev** — expected. The auto-updater only runs against installed builds.

## Channel split (future)

Today: one channel (`win-x64`), tag-driven, auto-update from public GitHub Releases. When the Store path lights up, the MSIX flow runs in parallel and the in-app updater knows to skip when running as MSIX (the Store handles updates).
