# Framework intake (Elden Mod Loader + future frameworks) — Design Spec

**Date:** 2026-05-27
**Status:** Drafted from in-chat brainstorm with Este — awaiting review
**Branch (proposed):** `feat/framework-intake`
**Sibling spec:** [`2026-05-27-unified-catalog-direct-inject-config-design.md`](2026-05-27-unified-catalog-direct-inject-config-design.md) — F3, the catalog-shape unification that's a Phase 1 prereq for direct-inject mods. F2 (this spec) stays on the existing `FrameworkDeps` schema; it composes with the unified catalog later.

## Why

Smoke of PR #56 against Elden Ring surfaced two compounding problems with the framework-dependency UX:

1. **Chip text is unparseable to non-modders.** Every ER mod row shows `NEEDS DLL PROXY (DINPUT8/VERSION/WINHTTP)`. The thing the user actually needs is **ELDEN MOD LOADER** (`https://www.nexusmods.com/eldenring/mods/117`). "DLL Proxy" is technically accurate; "Elden Mod Loader" is what they search for.

2. **Drop-the-zip-and-pray fails by design.** User dropped the ELDEN MOD LOADER zip into the launcher expecting the chip to clear. Intake correctly routed it to mod intake (zip contains a `.dll` + signature files); mod intake extracted to the mods folder; chip stayed. ELDEN MOD LOADER's `dinput8.dll` has to land at the **game root** next to `eldenring.exe`. Mod intake won't put files there — that'd violate the file-ops-stay-reversible invariant by design.

So the launcher saw the framework zip, didn't recognize it as a framework, and dropped it in the wrong place. From the user's perspective: "the launcher knew this was the thing the chip asked for, why didn't it just install it?"

## Operating principle (locked in via brainstorm)

> "If we know how something needs to be installed, that's when we add it to our app. So we can let users know that not every tool is currently supported. If mods are supported, they send feedback and we work to get them supported."

The launcher is **explicit about what it supports**. Recognized frameworks get a smart drop flow with a confirmation step ("do it for me"); unrecognized framework-looking zips get a feedback nudge before falling through to mod intake. No silent guessing about install location.

## Architecture

Four-piece change, all hanging off the existing PR #51 framework-dep infrastructure:

```
+----------------------------+      +-------------------------------+
| FrameworkDeps.Catalog      |      | KnownFramework.Catalog (NEW)  |
| (detection + chip names)   |      | (intake-installable frameworks)|
+----------------------------+      +-------------------------------+
            |                                       |
            v                                       v
+----------------------------+      +-------------------------------+
| Chip text + get-link       |      | Drop-zip classifier (PR #56)  |
| (rename DINPUT8/... ->     |      | adds Pre-check 4: framework   |
|  ELDEN MOD LOADER)         |      | (after save-mod, Lua, tool)   |
+----------------------------+      +-------------------------------+
                                                    |
                                                    v
                                    +-------------------------------+
                                    | FrameworkInstaller            |
                                    | (extract -> game root, track  |
                                    |  reversibility in              |
                                    |  _626mods/<game>/frameworks/) |
                                    +-------------------------------+
```

`FrameworkDeps.Catalog` (detection) and `KnownFramework.Catalog` (intake-installable subset) stay separate near-term: detection covers ALL frameworks (UE4SS, BepInEx, ME2, SMAPI, DLL proxy, Forge/Fabric), but intake-installable is a smaller list — only the ones we know exactly how to install. Detection without intake-install support stays in `FrameworkDeps` (e.g., UE4SS detection — install flow is a future expansion).

## Data shapes

### Rename: `FrameworkDeps.Catalog` entries get specific names

The existing record stays as is:

```csharp
public sealed record FrameworkDep(
    string Engine,            // "ue-pak", "fromsoft", etc.
    string Name,              // <-- This changes
    IReadOnlyList<string> DetectRelativePaths,
    string GetUrl,
    string? Note,
    string? SteamAppId = null);
```

The `Name` field flows directly into the chip text via `MissingFrameworks` (the `MainViewModel.MissingFrameworks` collection's `Name` property). For Elden Ring's DLL proxy entry at [`FrameworkDeps.cs:80`](../../../src/ModManager.Core/FrameworkDeps.cs#L80):

| Before | After |
|--------|-------|
| `Name: "DLL proxy (dinput8/version/winhttp)"` | `Name: "Elden Mod Loader"` |
| `GetUrl: <generic dll proxy doc>` | `GetUrl: "https://www.nexusmods.com/eldenring/mods/117"` |

`DetectRelativePaths` keeps the existing probe targets (`dinput8.dll`, `version.dll`, `winhttp.dll`) because those are still the file-system signal. The display layer is the only thing that changes.

Chip rendering flows through ModRowViewModel's existing `MissingFrameworks` binding. No XAML change.

### New: `KnownFramework.Catalog` (intake-installable frameworks)

Mirrors `ToolCatalog`'s shape ([`Tools/ToolCatalog.cs:17-26`](../../../src/ModManager.Core/Tools/ToolCatalog.cs#L17-L26)) but for frameworks. Each entry knows:

```csharp
public sealed record KnownFramework(
    string FrameworkId,                       // "eldenmodloader", "ue4ss-ue5", etc.
    string DisplayName,                       // "Elden Mod Loader"
    string Engine,                            // "fromsoft", "ue-pak", etc.
    string? SteamAppId,                       // optional, scope to a specific game
    string GetUrl,                            // Nexus / project page
    string Author,                            // honor-the-builders
    IReadOnlyList<string> ZipFilenameHints,   // ["eldenmodloader", "elden mod loader"]
    IReadOnlyList<string> ZipSignatureFiles,  // ["dinput8.dll", "ModLoader/"] — must all be present in zip
    string InstallRoot,                       // "GameRoot" | "Custom:..." (future)
    IReadOnlyList<string> ForbiddenPaths);    // paths inside install root that must NOT be overwritten
```

Day-one catalog entry: **Elden Mod Loader** for Elden Ring (`Engine=fromsoft`, `SteamAppId=1245620`, zip-sig `dinput8.dll` + `ModLoader/` folder, install at game root).

Future entries when we add support (each is an opt-in, not a heuristic): UE4SS for specific UE games, ME2 for FromSoft (if we ever ship the install-it-for-you flow — currently we just detect).

## The drop-zip flow (new Pre-check 4 in `AddModsAsync`)

After the existing Pre-checks 1-3 ([`MainViewModel.cs:1075-1147`](../../../src/ModManager.App/ViewModels/MainViewModel.cs#L1075-L1147)), add Pre-check 4 BEFORE the existing tool-intake check:

```
For each dropped path:
  1. Save-mod check (existing) — RocksDB/world content -> SaveModService.Install
  2. UE4SS Lua check (existing) -> route to UE4SS Lua intake
  3. Framework check (NEW):
     a. KnownFramework.Classify(zipPath, engine, steamAppId)
        - Iterate Catalog filtered to {Engine, SteamAppId}
        - For each, peek the zip's top-level entries
        - First catalog entry whose ZipFilenameHints OR ZipSignatureFiles match wins
     b. If matched: show confirmation dialog
        "{DisplayName} (by {Author}) — install at game root?
         Files: dinput8.dll, ModLoader/...
         Reversible from Settings → Installed frameworks."
        On confirm: FrameworkInstaller.Install(archive, knownFramework, gameContext)
        On cancel: do nothing — does NOT fall through to mod intake
     c. If unmatched BUT looks-like-a-framework (e.g., contains dinput8.dll or version.dll
        at zip root): show a one-time feedback nudge dialog
        "This looks like a game framework but isn't in our catalog yet.
         Drop us a note so we can support it: [Open feedback]
         You can still try installing it as a mod, but it probably won't work."
        Then fall through to existing mod intake.
  4. Tool check (existing PR #56)
  5. Mod intake (existing)
```

### `FrameworkInstaller.Install` (new pure-core)

```csharp
public sealed record FrameworkInstallResult(
    string FrameworkId,
    string InstallPath,
    IReadOnlyList<string> InstalledFiles,
    DateTime InstalledUtc);

public static class FrameworkInstaller
{
    // Pure-core. NO Electron, NO WinUI. System.IO + SharpCompress only.
    public static FrameworkInstallResult Install(
        string archivePath,
        KnownFramework framework,
        GameContext ctx);
}
```

Behavior:
1. Resolve install root (for `InstallRoot=GameRoot`: `ctx.GameRoot`).
2. Open archive via `SharpCompress.ArchiveFactory.OpenArchive`.
3. Read existing files at install root that the framework would overwrite; copy each to `<gameData>/frameworks/<frameworkId>/backup/<unix-ms>/<relative>` BEFORE extraction. Atomic.
4. Extract archive contents to install root.
5. Write manifest at `<gameData>/frameworks/<frameworkId>/install.json` (camelCase): `{ frameworkId, displayName, installPath, installedFiles[], installedUtc, backupSnapshotPath }`.
6. Return `FrameworkInstallResult`.
7. **Forbidden-paths gate** — if any path inside the archive resolves to a `ForbiddenPaths` entry (e.g., `eldenring.exe`), refuse the install with a clear error. No silent overwrite of the game executable.

Reversibility is via the pre-install snapshot at `<gameData>/frameworks/<frameworkId>/backup/...` — uninstall restores those files + deletes the framework's newly-added files (tracked in `installedFiles`).

### Settings → Installed frameworks

New section in [`SettingsDialog.xaml`](../../../src/ModManager.App/SettingsDialog.xaml) parallel to the existing "Installed tools" section. Per-game enumeration of `frameworks/<id>/install.json` files. Each row shows `DisplayName`, `Author`, `InstalledUtc`, `Uninstall` button (restores backup + removes installed files), `Get it here` link (opens `GetUrl`).

Honor-the-builders: NOTICE file gets a Frameworks attribution block (RimmyCode-style "metadata only, never bundled"). Tooltip on every framework row says `Catalog metadata only — never bundled. Install drops files only after explicit confirmation.`

## Detection of existing installs (no change needed)

[`FrameworkDeps.CheckPresent`](../../../src/ModManager.Core/FrameworkDeps.cs#L110-L124) already probes `DetectRelativePaths` under the game root. If a user installed Elden Mod Loader outside our launcher and `dinput8.dll` is present, the chip clears automatically. F2 adds NO new detection — only naming + intake.

## Out of scope (explicit)

- **UE4SS / ME2 / BepInEx intake flows** — F2 ships ONLY the Elden Mod Loader catalog entry. Adding more entries is a one-line catalog addition per framework, not a spec.
- **Migrating `FrameworkDeps` to the unified catalog schema** — F3's Phase 1 is direct-inject mods. Frameworks migrate in a later phase.
- **Detection of existing third-party installers** (Wabbajack-style mod packs that bundle frameworks) — we don't unbundle external mod packs.
- **Compressed-archive variants beyond zip** — `SharpCompress` handles 7z/rar transparently; same flow.

## Testing

Pure-core tests cover:

- `KnownFramework.Classify` matches an ELM-shaped zip via filename hint + signature files.
- `Classify` rejects an ELM zip when game engine is `ue-pak`.
- `Classify` returns null for a zip with NO known-framework signatures.
- `FrameworkInstaller.Install` backs up overwritten files before extraction.
- `Install` refuses if any extracted path lands inside `ForbiddenPaths`.
- `Install` writes a camelCase `install.json` manifest readable by Settings → Installed frameworks.
- `Install` is idempotent w.r.t. re-running over the same framework (or fails loud — TBD in plan).
- Forbidden-paths gate refuses to overwrite `eldenring.exe`.

UI smoke (manual):
- Drop the ELM zip → confirmation dialog → confirm → chip disappears, status: "Installed Elden Mod Loader at game root."
- Settings → Installed frameworks → ELM row visible.
- Uninstall → ELM files gone from game root; backup restored; chip returns.
- Drop a zip with `winhttp.dll` (looks-like-framework but unrecognized) → feedback nudge → falls through to mod intake.

## Open questions

None blocking — all locked in via brainstorm. Listed for plan-author reference:

- Future: how should ELM coexist with ME2 if a user has both installed? (Out of scope for F2 — both can be present; ME2 detection at `modengine2_launcher.exe` is independent.)
- Future: the "looks-like-a-framework" heuristic (dinput8/version/winhttp at zip root) could grow false positives. Revisit after smoke.
