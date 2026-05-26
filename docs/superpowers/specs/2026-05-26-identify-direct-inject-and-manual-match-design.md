# Direct-inject identify + audit + manual match — Design Spec

**Date:** 2026-05-26
**Status:** Approved (Este, in-chat)
**Branch:** `feat/identify-direct-inject-and-manual-match`

## Why

Real-day report (Este, just installed v0.2.0): Elden Ring mods show monograms, never icons. Backfilling metadata from a downloads folder full of Nexus archives also produces nothing.

The investigation surfaced a structural gap that's bigger than one game:

The metadata-identify pipeline today assumes **mod name = file with a known extension**. `Scanner.ZipModKeys` filters zip entries by `c.Exts` (pak/ucas/dll/jar/…) and uses those file names as the keys to attach metadata to. When `c.Exts` is empty or doesn't apply, the pipeline silently no-ops.

That breaks Elden Ring direct-inject (Este's case), it breaks Mod Engine 2 (currently stubbed), it would break SMAPI mods if we ever ported them, and it makes raw UE4SS Lua drops outside Vortex's deployment manifest unidentifiable.

Today's per-engine state:

| Engine | Mod naming convention | Identify status |
| --- | --- | --- |
| **bethesda** (`.esp/.esl/.esm/.bsa`) | extension-based | ✅ works |
| **ue-pak** (Windrose, Demonologist, Witchfire, R6, etc.) | extension-based (`.pak`) | ✅ works |
| **bepinex** (`.dll` plugins) | extension-based | ✅ works (Nexus md5 path) |
| **minecraft** (`.jar`) | extension-based | ✅ works |
| **smapi** (Stardew) | folder-named, `manifest.json` | ⚠️ would silently no-op (empty Exts) |
| **fromsoft + ME2** | folder-named, registered in TOML | ⚠️ drop is stubbed; metadata never fires |
| **fromsoft + direct-inject** (Elden Ring) | catalog-named (Seamless Co-op, ReShade…) | ❌ **broken — this spec fixes** |
| **ue4ss lua** | folder-named (`R5ModSettings/`) | ⚠️ relies on Vortex deployment manifest; raw drops outside Vortex never identify |
| **save mods** | own lifecycle | ✅ doesn't need this pipeline |

The bug is one of **three structural classes**, not just one game.

## Goal

Three layers, shipped together:

1. **Fix Elden Ring direct-inject end-to-end.** Drop a mod archive → it gets identified by Nexus md5. Backfill a downloads folder → installed mods get matched.
2. **Make the structural gaps visible.** A docs/identify-paths-audit.md naming each engine's status so the next contributor (or future-Este) hits documentation, not silent failure.
3. **Universal escape hatch.** When auto-identify fails, the user can paste a Nexus or CurseForge URL and bind the row to that mod. Persists across rescans. Means the system never gets a user stuck — the worst case is "click here and paste a URL," not "no icon forever."

This combo is the right shape because it fixes the immediate bug while addressing the class. Layer 3 graceful-degrades every future engine we don't yet auto-recognize.

## Approach

### Layer 1 — Direct-inject identify path (Core + VM)

`DirectInject.Catalog` already names six known direct-inject mods by their on-disk signature (files / dirs / filename fragments). The fix turns that catalog into an **archive recognizer** too: given a zip's entry list, return which catalog mod(s) the archive installs.

**New pure helper:**

```csharp
// DirectInject.cs
public static IReadOnlyList<string> MatchSignaturesInZip(IEnumerable<string> zipEntryNames)
{
    // For each catalog Signature, check whether the archive's entries match:
    //   - Files: any entry's filename (basename, lower) equals a catalog file name
    //   - Dirs:  any entry's path contains a directory segment matching a catalog dir
    //   - FileContains: any entry's filename contains the fragment (case-insensitive)
    // Return the distinct Signature.Name values that matched.
}
```

**Scanner change:**

```csharp
// Md5IdentifyArchivesAsync — branch on c.Exts emptiness:
//   - Non-empty (pak/dll/jar): keep ZipModKeys (today's path)
//   - Empty (fromsoft):        use DirectInject.MatchSignaturesInZip(zip.EntryNames)
// Then proceed exactly as today: foreach key, meta[key] = MergeMeta(...).
```

This is one branch in one method. The matched key from the catalog (`"Seamless Co-op"`) lines up with the `Mod.Name` direct-inject already produces.

**VM change in `AddModsAsync` direct-inject branch:**

After `_direct.Execute(...)` + `Redetect` + `ReloadModsAsync`, call the same three identifies the regular branch does (`FingerprintIdentifyAsync`, `Md5IdentifyArchivesAsync`, `RefreshMetadataByNameAsync`), best-effort, errors swallowed. Five lines.

### Layer 2 — Audit doc

A new file `docs/identify-paths-audit.md` carrying the per-engine table above, plus a paragraph per engine that names:

- What the mod naming convention is on disk
- Which identify path applies (extension-based / catalog / Vortex manifest / manual)
- The status today
- What the "next move" would be to harden it

The audit doesn't auto-update itself — it's the architectural record the contributor reads when adding a new engine. The mod-safety-auditor agent or a future `vibe-test` audit could lint that engines listed in `EnginePresets.Presets` all appear in this doc.

### Layer 3 — Manual match dialog

**On the mod row, right-click → "Match to a mod…"** opens a small dialog:

- One TextBox: "Paste a Nexus or CurseForge mod URL."
- The launcher parses both URL forms:
  - Nexus: `nexusmods.com/<domain>/mods/<id>` → call `nexus.GetModAsync(domain, id)`
  - CF: `curseforge.com/<gameSlug>/mods/<modSlug>` → call CF's lookup-by-slug
- On success: write the resulting `ModMeta` to `metadata.json` against this `Mod.Name` key with a `Source = "manual"` field.
- Manual matches survive rescans (they're indexed by `Mod.Name`, persisted in the existing metadata store).

**Why it lives on the row:**

The user already knows WHICH mod row they're trying to identify (they're looking at "Seamless Co-op" with no icon). Putting the action on the row means zero ambiguity about which mod the URL is for. It also reuses the existing per-row right-click menu pattern.

**Why URL paste, not picker:**

Nexus and CF don't have a generic "search the world" API we'd want to surface in a dialog. The user is going to find the mod page in their browser anyway — pasting the URL is the natural handoff. URL parsing is bounded and testable; a search UI is a project of its own.

**Persistence:**

`Source = "manual"` on the metadata entry. The auto-identify paths (fingerprint / md5 / name-search) write `Source = "auto"`. When a future rescan re-runs auto-identify against a row that has a manual entry, `MergeMeta` already prefers existing values — manual wins. (Verify and explicitly test this; if the merge order goes the wrong way, the manual match would get clobbered.)

## What stays out of scope

- **ME2 auto-install.** Currently stubbed; the drop path tells the user to place the folder + edit the TOML. That's a separate feature.
- **UE4SS Lua identify path** without Vortex. The audit doc names it; the manual-match escape hatch already covers it for now.
- **SMAPI port.** Not in the launcher today.
- **Auto-update of the audit doc.** Manual upkeep is fine for now.

## File structure

| File | Role |
| --- | --- |
| `src/ModManager.Core/DirectInject.cs` | + `MatchSignaturesInZip(IEnumerable<string>)` static |
| `src/ModManager.Core/Scanner.cs` | `Md5IdentifyArchivesAsync` branches on `c.Exts` emptiness |
| `src/ModManager.Core/Metadata.cs` | + `Source` field on `ModMeta`; merge rule prefers manual |
| `src/ModManager.Core/SafeUrl.cs` (or new `ModSiteUrl.cs`) | + URL parsers for Nexus + CF mod-page URLs |
| `src/ModManager.App/ViewModels/MainViewModel.cs` | + `AddModsAsync` direct-inject branch calls identifies; + `ManualMatchAsync(row, url)` |
| `src/ModManager.App/MainWindow.xaml(.cs)` | + per-row right-click "Match to a mod…" menu item |
| `src/ModManager.App/ManualMatchDialog.xaml(.cs)` | URL paste dialog |
| `docs/identify-paths-audit.md` | the per-engine status table + paragraphs |
| `tests/ModManager.Tests/DirectInjectMatchSignaturesTests.cs` | new test class for the signature matcher |
| `tests/ModManager.Tests/Md5IdentifyArchivesTests.cs` (or extend existing) | fromsoft branch test |
| `tests/ModManager.Tests/ModSiteUrlTests.cs` | URL-parser tests (Nexus + CF) |
| `tests/ModManager.Tests/MetadataMergeTests.cs` (or extend existing) | manual-wins merge test |

## Tech stack

.NET 10, ModManager.Core (pure), ModManager.App (WinUI 3), xUnit. No new NuGets.

## Risk

Low-to-moderate. The Core changes are pure, deterministic, and testable; the worst case is "matches one signature it shouldn't" and we tighten the catalog test cases. The VM change in `AddModsAsync` mirrors the regular branch exactly. The XAML manual-match dialog is small and self-contained.

The one place to verify carefully: the `MergeMeta` direction. If auto-identify clobbers a manual match on rescan, the feature is worse than nothing. The tests must pin this.

## Approval

- [x] Layer 1 — `DirectInject.MatchSignaturesInZip` + `Scanner.Md5IdentifyArchivesAsync` fromsoft branch + `AddModsAsync` direct-inject identify wiring
- [x] Layer 2 — `docs/identify-paths-audit.md` with the per-engine table
- [x] Layer 3 — Per-row "Match to a mod…" right-click action + `ManualMatchDialog` + manual-wins merge rule
