# Unified mod/tool/framework catalog — Phase 1: direct-inject mod config discovery — Design Spec

**Date:** 2026-05-27
**Status:** Drafted from in-chat brainstorm with Este — awaiting review
**Branch (proposed):** `feat/known-direct-inject-mod-catalog`
**Sibling spec:** [`2026-05-27-framework-intake-design.md`](2026-05-27-framework-intake-design.md) — F2, the Elden Mod Loader install flow. Composes with this spec's schema in later phases.

## Why

Smoke of PR #56 against Elden Ring + Seamless Co-op surfaced two truths:

1. **Seamless Co-op has an INI worth editing** — `seamlesscoopsettings.ini` holds the password + session settings. Users edit it before every co-op session.
2. **The pencil icon never appears on Seamless's row** because [`MainViewModel.ReloadModsAsync`](../../../src/ModManager.App/ViewModels/MainViewModel.cs#L315-L331) computes `folderAbs = ""` for direct-inject mods (their `Mod.IsFolder = false`) and then skips the `*.ini` glob entirely. Direct-inject mods are by definition loose files in the game folder — they have no "mod folder" to glob.

The narrow fix is "add a known-config-paths catalog for direct-inject mods." The broader product question: the launcher has THREE silo'd catalogs at this point — `ToolCatalog` (PR #56), `FrameworkDeps.Catalog` (PR #51), and `DirectInject.Catalog` (this surface) — each with overlapping concerns (detection, display name, get-link, author/honor-the-builders) and divergent shapes. Every new game's ecosystem support means touching all three.

The 626 product positioning is "smarter than Vortex about each game's ecosystem." Three siloed catalogs is the opposite. **Phase 1 of this spec ships a schema that the existing direct-inject mod catalog migrates to, and that Tools and Frameworks fold into when concrete pressure makes them painful.**

## Phased delivery (locked in via brainstorm)

> "Phased broader (recommended): design the unified catalog schema now (cheap), ship F3 as Phase 1 (first consumer using the new schema for direct-inject mods + config-paths). Tools and Frameworks stay on their existing schemas; we migrate them when there's a concrete pressure that makes the silos painful."

This spec covers **Phase 1 only**:

- Define the unified schema as `KnownDirectInjectMod` (name signals first-consumer scope; rename to `CatalogEntry` or split into kind-tagged record in a future phase when Tools/Frameworks migrate)
- Migrate `DirectInject.Catalog` to the new schema (existing `Signature` private record → `KnownDirectInjectMod`)
- Add `ConfigPaths` per entry, drive the pencil icon from them
- Add per-mod install-path override storage (for users whose Seamless is in an unusual location)
- Add forbidden-paths gate (refuse override paths that point at protected folders)

**Out of scope for Phase 1, by explicit agreement:**

- Migrating `ToolCatalog` or `FrameworkDeps.Catalog` to the unified schema. Those stay as-is and ship independently. F2 stays on `FrameworkDeps` near-term.
- Settings UX for managing direct-inject mod overrides beyond a basic "Override config path" picker.
- Auto-glob fallback (`**/seamlesscoopsettings.ini` under game root) — catalog + override is sufficient; revisit only if real-world Seamless installs land in unexpected spots.

## Architecture

The schema is shaped to support **kind tagging** from day one, so when Tools/Frameworks eventually migrate they slot in as `Kind: "tool"` or `Kind: "framework"` without re-shaping the contract:

```csharp
public sealed record KnownDirectInjectMod(
    // Identity
    string Kind,                              // "directInjectMod" — Phase 1 single value; future: "tool" | "framework" | ...
    string ModId,                             // "seamless-coop", "reshade", "erss2", ... (stable slug for storage keys)
    string DisplayName,                       // "Seamless Co-op"
    string ChipKind,                          // "co-op" / "graphics" / "upscaler" / ... (existing DirectInjectMod.Kind)
    string Author,                            // "Yui" (honor-the-builders; nullable when unknown)
    string Engine,                            // "fromsoft" — must match GameEntry.Engine
    string? SteamAppId,                       // optional scope: only applies to this game (e.g., 1245620 for ER)
    string? GetUrl,                           // Nexus / project page (for "Get it here" links)

    // Detection — how to recognize the entry's on-disk install (and in a dropped zip)
    IReadOnlyList<string> InstallSignatureFiles,    // exact basenames the catalog matches on (e.g., ["ersc.dll", "ersc_settings.ini"])
    IReadOnlyList<string> InstallSignatureDirs,     // dir-segment names (e.g., ["seamlesscoop"])
    IReadOnlyList<string> InstallSignatureContains, // basename-contains (for ultrawide-style varying filenames)

    // Install location
    string InstallRoot,                       // "PlayFolder" | "GameRoot" | "GameRoot/Game" — symbolic, resolved at runtime
    IReadOnlyList<string> ConfigPaths,        // RELATIVE to resolved install root, e.g., ["SeamlessCoop/seamlesscoopsettings.ini"]

    // Override + safety
    IReadOnlyList<string> ForbiddenOverridePaths); // patterns that a user-provided override path must NOT match
                                                    // (e.g., "${gameRoot}/${exeName}", "${gameData}/**")
```

`InstallSignatureFiles` / `InstallSignatureDirs` / `InstallSignatureContains` replace the existing `Signature.Files` / `Dirs` / `FileContains` fields one-to-one — so the migration is field-rename-only for detection.

`ConfigPaths` is the Phase 1 new field. For Seamless Co-op: `["SeamlessCoop/seamlesscoopsettings.ini"]`. For ReShade: `["reshade.ini", "reshadepreset.ini"]` (already-globbed, but explicit). For most others: empty (no editable config).

`InstallRoot` is symbolic — `PlayFolder` resolves to whatever [`DirectInjectService.PlayFolder(gameRoot)`](../../../src/ModManager.App/Services/DirectInjectService.cs#L116-L121) returns (FromSoft games nest under `Game/`). This keeps the schema portable across engine layouts.

## Migration of `DirectInject.Catalog`

The existing private `Signature` record (line 32 in [`DirectInject.cs`](../../../src/ModManager.Core/DirectInject.cs#L32)) is replaced by the public `KnownDirectInjectMod`. The static `Catalog` array is rewritten from `Signature[]` to `KnownDirectInjectMod[]`. Existing callers (`MatchSignaturesInZip`, `Detect`, `DetectLoaderMods`) update to read the new field names.

Migration table (Phase 1 entries):

| ModId | DisplayName | InstallSignatureFiles | InstallSignatureDirs | InstallSignatureContains | ConfigPaths | Author |
| --- | --- | --- | --- | --- | --- | --- |
| `reshade` | ReShade | `reshadepreset.ini`, `reshade.ini` | `reshade-shaders` | — | `reshade.ini`, `reshadepreset.ini` | crosire |
| `seamless-coop` | Seamless Co-op | `ersc.dll`, `ersc_settings.ini`, `launch_elden_ring_seamlesscoop.exe` | `seamlesscoop` | — | `SeamlessCoop/seamlesscoopsettings.ini`, `ersc_settings.ini` | Yui |
| `erss2-frame-gen` | ERSS2 Frame Gen | `erss-fg.dll`, `erss-fg.toml`, `erss2loader.log` | `erss2` | — | `erss-fg.toml` | praydog (unknown — to confirm) |
| `ultrawide-fix` | Ultrawide / Widescreen Fix | — | — | `ultrawide`, `widescreen` | — | community (unknown) |
| `modded-regulation` | Modded regulation.bin | `regulation.bin` | — | — | — | (varies — unknown at catalog time) |
| `dll-mod-loader` | DLL mod loader | `dinput8.dll` | — | — | — | community |

`ChipKind` values mirror the existing `Signature.Kind`: `graphics`, `co-op`, `upscaler`, `display`, `gameplay`, `dll`.

Authors are pulled where known from the existing [`metadata.json`](../../../docs/superpowers/specs/2026-05-26-direct-inject-metadata-merge-design.md) inventory; unknown stays empty (we don't speculate).

## The row-build hook

[`MainViewModel.ReloadModsAsync`](../../../src/ModManager.App/ViewModels/MainViewModel.cs#L308-L368) builds rows from `_direct.List(_ctx.Game)`. The current iniFiles enumeration at lines 320-331 only runs when `folderAbs` is non-empty (and that's empty for direct-inject mods).

The change: when the row's `Location == "direct-inject"`, populate `iniFiles` from the catalog's `ConfigPaths` instead.

```csharp
// Replace the current `IReadOnlyList<string> iniFiles = ...` block (~line 321-331) with:
IReadOnlyList<string> iniFiles = Array.Empty<string>();
if (rep.Location == "direct-inject")
{
    iniFiles = ResolveDirectInjectConfigPaths(rep, _ctx);  // catalog-driven, NOT glob
}
else if (!string.IsNullOrEmpty(folderAbs) && Directory.Exists(folderAbs))
{
    // Existing glob path for regular folder-tracked mods stays unchanged.
    try { iniFiles = Directory.EnumerateFiles(folderAbs, "*.ini", AllDirectories).Take(20).ToArray(); }
    catch { }
}
```

`ResolveDirectInjectConfigPaths` (new method, **pure-core** in `ModManager.Core` — no Electron/WinUI; App-layer just invokes it):

1. Look up the catalog entry by `rep.Name` (= `KnownDirectInjectMod.DisplayName`).
2. If none, return empty.
3. Resolve `InstallRoot` symbolic value → absolute path (`PlayFolder` for FromSoft, `GameRoot` otherwise).
4. Load the user-override (if any) from `<gameData>/direct-inject/<modId>/config.json` — schema: `{ "configPathOverrides": { "<relativePath>": "<absoluteOverride>" } }` (camelCase, atomic write).
5. For each `ConfigPaths` entry, compute the absolute candidate:
   - If user override exists: use the override (validated against `ForbiddenOverridePaths` before saving).
   - Else: `Path.Combine(resolvedInstallRoot, relativePath)`.
6. Return only paths that EXIST on disk.

The pencil icon at [`ModRowViewModel.cs:257-258`](../../../src/ModManager.App/ViewModels/ModRowViewModel.cs#L257-L258) lights up automatically — `HasIniFiles` already drives it from `IniFiles.Count > 0`.

## User-override storage

Per-game, per-mod override file at `<gameData>/direct-inject/<modId>/config.json`. Atomic JSON write through `FsAtomic.WriteJsonAtomic`. camelCase keys (matches the Electron-shared shape per [`shared-json-camelcase`](../../../) memory).

```json
{
  "configPathOverrides": {
    "SeamlessCoop/seamlesscoopsettings.ini": "D:/games/eldenring-seamless/seamlesscoopsettings.ini"
  },
  "installLocationOverride": null
}
```

`installLocationOverride` is reserved for Phase 1 — populated only when the entire mod install is outside the standard `InstallRoot`. The catalog-aware detector consults it for the on-disk scan too, so a Seamless install on a different drive still appears in the mod list.

## Forbidden-paths gate

Before persisting an override, validate the target path against `ForbiddenOverridePaths`. The patterns are interpolated against the game context:

- `${gameRoot}/${exeName}` — refuses overriding into the game executable itself
- `${gameData}/**` — refuses pointing INTO our reversibility storage (so user mistakes can't corrupt our snapshots)
- `${gameRoot}/EasyAntiCheat/**` — refuses pointing into anti-cheat folders

Validation throws `InvalidOperationException` with a clear message ("Can't override to a path inside the game's anti-cheat folder."). The user gets a toast; the override is not saved.

## Settings UX (Phase 1 minimum)

New "Direct-inject mods" section in [`SettingsDialog.xaml`](../../../src/ModManager.App/SettingsDialog.xaml), parallel to "Installed tools." Lists detected direct-inject mods with:

- Mod name + author
- Resolved install location (with "✓ standard" or "↗ custom" badge)
- Resolved config files (each with an "Override path…" button that opens a file picker)
- "Reset to default" button per-override

No "uninstall direct-inject mod" button here — the existing in-list enable/disable affordance covers that.

## Out of scope (explicit)

- **Migrating `ToolCatalog` to the unified schema** — separate phase, separate spec.
- **Migrating `FrameworkDeps.Catalog`** — separate phase. F2 stays narrow.
- **Glob fallback** for INI discovery when catalog + override miss — revisit if real-world data shows the need.
- **Auto-detection of non-standard install locations** (scanning the user's drives for Seamless installs they didn't tell us about) — explicit user pointer only, no scanning.
- **Catalog editor UI** for users to add their own entries — agentic profiles can fill catalogs offline; Phase 1 stays on the canonical catalog.

## Testing

Pure-core tests cover:

- `KnownDirectInjectMod`-shaped catalog migrates field-for-field; existing detection behavior preserved (parameterize the existing `DirectInjectTests` suite over both shapes during migration — kill the old shape's tests after).
- `ResolveDirectInjectConfigPaths` returns the Seamless INI when:
  - The standard install lands at `<playFolder>/SeamlessCoop/seamlesscoopsettings.ini`.
  - The user has set an override pointing elsewhere on disk.
- Returns empty when the catalog entry exists but no INI is on disk.
- Returns empty when the mod's row isn't in the catalog (unknown direct-inject mod — `regulation.bin` style).
- Forbidden-path gate refuses an override targeting `${gameRoot}/${exeName}`.
- Forbidden-path gate refuses an override into `${gameData}/**`.
- Round-trip: writing an override, then loading, returns the exact same path (camelCase JSON preserved).

UI smoke (manual):

- Open Saves dialog on ER with Seamless installed → Seamless Co-op row has a pencil icon → click → INI editor opens with the actual settings → edit + save → snapshot lands in `.ini-history/seamless-coop/`.
- Settings → Direct-inject mods → Seamless row shows install location + INI path → "Override path…" picker → pick a different file → next reload uses the override.
- Try to set an override pointing inside `_626mods/elden-ring/` → toast: "Can't override to a path inside our reversibility folder."

## Open questions

None blocking — all locked in via brainstorm. Listed for plan-author reference:

- The Phase 1 record name (`KnownDirectInjectMod`) signals direct-inject scope. When Tools/Frameworks migrate, the rename to a unified `CatalogEntry` (with kind tag) is mechanical. Decide naming convention at migration time, not now.
- ME2's config (`config_eldenring.toml`) is a separate surface — handled by `ModEngineService` already. Phase 1 doesn't touch it. If Phase 2 unifies, ME2 becomes a `Kind: "framework"` entry with its own `ConfigPaths`.
- Author attribution for entries where we don't know the author (e.g., `Modded regulation.bin` — varies per mod-pack) stays empty. The row's display says "by — / community" gracefully.
