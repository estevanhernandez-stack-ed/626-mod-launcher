# Identify-paths audit

> The metadata-identify pipeline (`Scanner.FingerprintIdentifyAsync` → `Md5IdentifyArchivesAsync` → `RefreshMetadataByNameAsync`) assumes **a mod's display name comes from a file with one of `GameContext.FileExtensions`**. That assumption holds for extension-based engines (`.esp`, `.pak`, `.dll`, `.jar`) and breaks silently for everything else.
>
> This doc names the breakage per engine so the next contributor (or future-Este) hits documentation, not silent failure. The companion change ships the fromsoft direct-inject fix (`feat/identify-direct-inject-and-manual-match`) — the manual-match dialog is the universal escape hatch for whatever this doc says doesn't work yet.

## Status table

| Engine | Mod naming convention | Identify status |
| --- | --- | --- |
| `bethesda` (`.esp` / `.esl` / `.esm` / `.bsa`) | extension-based | ✅ works |
| `ue-pak` (Windrose, Demonologist, Witchfire, R6, etc.) | extension-based (`.pak`) | ✅ works |
| `bepinex` (`.dll` plugins) | extension-based | ✅ works (Nexus md5 path) |
| `minecraft` (`.jar`) | extension-based | ✅ works |
| `smapi` (Stardew) | folder-named, `manifest.json` | ⚠️ would silently no-op (empty `FileExtensions`) |
| `fromsoft` + Mod Engine 2 | folder-named, registered in `config_*.toml` | ⚠️ drop is stubbed; metadata never fires |
| `fromsoft` + direct-inject (Elden Ring) | catalog-named (Seamless Co-op, ReShade, regulation.bin…) | ✅ fixed by this PR |
| `ue4ss-lua` | folder-named (e.g. `R5ModSettings/`) | ⚠️ relies on Vortex deployment manifest; raw drops outside Vortex never identify |
| save-world mods | install into the save tree, not `Content/Paks/` | ✅ doesn't need this pipeline |

## Per-engine detail

### `bethesda` — extension-based

Skyrim / Fallout 4 style. Mods are individual plugin / archive files at the engine's load root. Scanner uses `ZipModKeys` filtered by `c.FileExtensions = ["esp", "esl", "esm", "bsa"]` (or however the registry entry names them). Display name = filename minus extension. Fingerprint + md5 + name-search all line up because the file IS the mod.

**Status:** ✅ Works.

**Next move if it breaks:** Verify the registry entry's `FileExtensions` list still matches what mod authors actually ship. New file types (`.es*` variants) show up over time.

### `ue-pak` — extension-based

Unreal `.pak` mods — Windrose, Demonologist, Witchfire, Rainbow Six, etc. Same shape as bethesda but the only extension is `.pak`. `GameContext` normalizes an empty `FileExtensions` to `["pak"]` — a defensive default for legacy registry entries.

**Status:** ✅ Works.

**Watch-out:** The empty-extensions normalization at `GameContext` construction makes "empty `FileExtensions`" indistinguishable from "explicitly only `pak`" downstream. The fromsoft branch in `Md5IdentifyArchivesAsync` discriminates on `c.Game.FileExtensions.Count > 0` (the raw registry value), NOT `c.FileExtensions.Count` (the normalized one). Don't confuse the two.

### `bepinex` — extension-based

`.dll` plugin mods. The Nexus md5 path is the dominant identifier here — CF rarely indexes BepInEx plugins.

**Status:** ✅ Works.

**Next move if it breaks:** Probably a Nexus archive shape change. The md5 is computed over the whole archive on disk; an upload pipeline that re-packs (different compression / metadata) will silently invalidate prior md5 matches. Surface those rows in the "Backfill" status text instead of letting them go unidentified.

### `minecraft` — extension-based

`.jar` mods. Identical shape to the others.

**Status:** ✅ Works.

### `smapi` — folder-named, `manifest.json`

Stardew Valley's mod loader. A SMAPI mod is a *folder* under `Mods/`, identified by the `manifest.json` it contains (`Name`, `Author`, `UniqueID`, `Version`). Filename has no meaning — the JSON does.

**Status:** ⚠️ Would silently no-op. The launcher doesn't currently target SMAPI; if a registry entry added it with empty `FileExtensions`, `ZipModKeys` would return nothing and the identify chain would attach metadata to no rows. Same structural class as the (now-fixed) fromsoft case.

**Next move:** When SMAPI lands, mirror the fromsoft fix — give Scanner a way to derive mod keys from `manifest.json` inside the archive (extract the top-level folder name + cross-check the manifest's `UniqueID`). Or just declare `manifest.json` the recognizer and treat the containing folder name as the mod key.

### `fromsoft` + Mod Engine 2 — folder-named, registered in TOML

Dark Souls 3 / Sekiro / Elden Ring / Armored Core VI with [ModEngine2](https://github.com/soulsmods/ModEngine2). A mod is a folder under the ME2 install — `<ME2>/mod/<ModName>/parts/...` etc. — and load order is the array order inside `config_*.toml`. Earlier entries in the `external_dlls` / `external_files` arrays win conflicts.

**Status:** ⚠️ Drop is stubbed. The intake path currently tells the user to place the folder + edit the TOML manually. Metadata identify never runs against an ME2 mod because nothing reaches Scanner with an archive path.

**Next move:** Implement the drop-to-install via the TOML edit (we already read/write it for load-order — `ModEngine2Config.cs`). For identify, derive the mod key from the *top-level folder name in the archive* and run the same Nexus md5 / CF fingerprint chain. The manual-match dialog is the user-facing escape hatch in the meantime.

### `fromsoft` + direct-inject — catalog-named (THIS PR)

Loose files dropped directly into the game folder. Elden Ring is heavy on this — Seamless Co-op, ReShade, modded `regulation.bin`, Ultrawide fixes, etc. There's no "container folder" — files land at the game root (`ersc.dll`, `reshade.ini`, `regulation.bin`) and the launcher recognizes the mod via `DirectInject.Catalog` signatures (Files / Dirs / FileContains rules).

**Status today (before this PR):** ❌ Broken end-to-end. The drop path called `DirectInject.Execute` which gave the mod a catalog name (e.g. "Seamless Co-op") and returned. The regular intake branch's three identifies (`FingerprintIdentifyAsync`, `Md5IdentifyArchivesAsync`, `RefreshMetadataByNameAsync`) were never wired into the direct-inject branch. Backfill from a downloads folder also failed because `Md5IdentifyArchivesAsync` used `ZipModKeys` with empty `FileExtensions` — Nexus md5 found the archive but there were no keys to attach metadata to.

**Status (this PR):** ✅ Fixed.

1. `DirectInject.MatchSignaturesInZip(IEnumerable<string> entryNames)` mirrors the on-disk catalog against an archive's entry list, returning the catalog-mod names a zip installs.
2. `Scanner.Md5IdentifyArchivesAsync` branches on `c.Game.FileExtensions.Count > 0` (raw registry, not normalized) — empty → use the catalog matcher, non-empty → keep `ZipModKeys`.
3. `MainViewModel.AddModsAsync` direct-inject branch now calls the same three identifies the regular branch does, best-effort, errors swallowed.

**Watch-out:** The `DirectInject.Catalog` is hand-curated. New direct-inject mods that ship under a name the catalog doesn't recognize fall through to the manual-match escape hatch (right-click row → "Match to a mod…"). Add catalog entries when you see new patterns.

### `ue4ss-lua` — folder-named, Vortex-managed

UE4SS Lua mods land at `<game>/Binaries/Win64/ue4ss/Mods/<ModName>/` (or similar — engine-specific subpath). Vortex manages most of these via its deployment manifest, which the launcher honors via the "managed" read-only badge.

**Status:** ⚠️ Mixed. Vortex-deployed Lua mods identify correctly because the manifest tells us the mod's name + provenance. **Raw drops outside Vortex never identify** — the launcher sees a folder name but doesn't know what to look it up as. This affects users who download Lua mods directly from Nexus and drop them without Vortex.

**Next move:** Two options, both viable.
1. Parse the standard `enabled.txt` / Lua mod's first-line comment (some authors include `-- ModName by Author` headers) for hints + cross-check Nexus.
2. Wire the manual-match dialog as the documented escape hatch and don't try to auto-identify raw Lua drops.

Option 2 is what we have today and it's fine — Lua mods are a power-user surface; manual match is a 10-second action.

### save-world mods — own lifecycle

Save-tree mods (Windrose / generic UE games with persistent world data) install into the save folder hierarchy, not `Content/Paks/`. They don't traverse the metadata-identify pipeline at all — they're recognized at drop time by their save-tree signature and stored with their own metadata.

**Status:** ✅ Doesn't need this pipeline. The save-mod drop path attaches the source URL + author at install time.

## When you add a new engine

A checklist for the next contributor. Run through these before declaring an engine "supported."

1. **Registry entry.** Add the engine to `EnginePresets.Presets`. Set `FileExtensions` if the engine names mods after a file extension; leave it empty if the engine names mods another way (folder name, catalog, manifest).

2. **`FileExtensions` semantics.** If empty, `Md5IdentifyArchivesAsync` will fall into the fromsoft branch and call `DirectInject.MatchSignaturesInZip` — that's fine if the engine IS direct-inject. If the engine names mods from a folder structure or manifest file, you need a NEW branch in `Md5IdentifyArchivesAsync` (mirror the `c.Game.FileExtensions.Count > 0` discriminator and add the new path).

3. **Drop / intake path.** `PlanIntake` + the matching `Execute` method need to know how this engine's mods are installed. Don't stub it and call it done — a stubbed install means metadata never identifies, which means the row stays a monogram forever.

4. **Identify chain.** After install, the VM's intake branch must call all three: `FingerprintIdentifyAsync`, `Md5IdentifyArchivesAsync`, `RefreshMetadataByNameAsync`. Best-effort with swallowed errors. If you skip any, that engine's mods will show up unidentified.

5. **Add this engine's row to the status table above** with its convention + status. Make the structural gap visible to the next person, even if you don't fix it the same day.

6. **Manual-match escape hatch.** The "Match to a mod…" right-click works for every engine because it operates on the `Mod.Name` key, not the engine. You don't need to wire anything new — it's already universal. Mention it in the user-facing copy when an engine is known-flaky.

## Related code

- `src/ModManager.Core/Scanner.cs::Md5IdentifyArchivesAsync` — the branch.
- `src/ModManager.Core/DirectInject.cs::Catalog` + `MatchSignaturesInZip` — the catalog + recognizer.
- `src/ModManager.Core/EnginePresets.cs` — registry of supported engines.
- `src/ModManager.Core/Mod.cs::ModMeta.IsManual` — the manual-match lock flag.
- `src/ModManager.App/ManualMatchDialog.xaml(.cs)` + `MainWindow.xaml.cs::OnManualMatch` — the universal escape hatch.
