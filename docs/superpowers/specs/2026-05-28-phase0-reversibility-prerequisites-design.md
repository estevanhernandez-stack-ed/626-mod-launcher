# Phase 0 — Reversibility prerequisites for Safe Clear — Design Spec

**Date:** 2026-05-28
**Status:** Drafted — awaiting review
**Branch (proposed):** `fix/reversibility-prerequisites`
**Master:** [`2026-05-28-safe-clear-restore-onboarding-design.md`](2026-05-28-safe-clear-restore-onboarding-design.md)
**Next phase:** [`2026-05-28-phase1-safe-clear-restore-design.md`](2026-05-28-phase1-safe-clear-restore-design.md)

## Why

Safe Clear's "return to vanilla" and "leave mods active" paths reuse the existing enable/disable/uninstall primitives. Two of those primitives are broken or fragile in exactly the ways a mass, cross-volume, possibly-interrupted Safe Clear will hit. Fix them — test-first — before any Safe Clear code so the engine stands on solid ground. Every item here is independently shippable and independently valuable (they harden the *current* app too).

Each fix starts with a failing xUnit test in `tests/ModManager.Tests/` per the project's TDD law.

## P0.1 — `FrameworkRegistry.Uninstall` resolves against the wrong root

**Bug (verified).** `FrameworkInstaller.Install` extracts to `installRoot = ResolveInstallRoot(framework.InstallRoot, gameRoot)` — `<gameRoot>/Game/` for FromSoft `PlayFolder` frameworks ([`FrameworkInstaller.cs:55-71,82`](../../../src/ModManager.Core/Frameworks/FrameworkInstaller.cs#L55)) — records `InstalledFiles` relative to that root, and persists the absolute root in `manifest.InstallPath` (line 152). But `FrameworkRegistry.Uninstall` resolves deletes and backup-restores against `gameRoot` ([`FrameworkRegistry.cs:58,69`](../../../src/ModManager.Core/Frameworks/FrameworkRegistry.cs#L58)), ignoring `InstallPath`. For a `PlayFolder` framework (ELM's `dinput8.dll` chain-loader), uninstall targets `<gameRoot>\dinput8.dll` (absent), leaves `<gameRoot>\Game\dinput8.dll`, and restores backups to the wrong directory. Non-FromSoft works only by accident (`installRoot == gameRoot`).

**Fix.** Resolve both the delete loop and the backup-restore loop against `m.InstallPath` (already persisted), not `gameRoot`. The `gameRoot` parameter becomes unused for path resolution — keep it only if a guard needs it, else drop it from the signature and update the one caller.

```csharp
foreach (var rel in m.InstalledFiles)
{
    var abs = Path.Combine(m.InstallPath, rel);   // was: Path.Combine(gameRoot, rel)
    try { if (File.Exists(abs)) File.Delete(abs); } catch { /* leave for manual */ }
}
// backup restore:
var dst = Path.Combine(m.InstallPath, rel);       // was: Path.Combine(gameRoot, rel)
```

**Note for Phase 1.** `Uninstall` still uses `File.Delete` with no capture of deleted content. That's tolerable for an explicit user uninstall (the pre-install backup is the rollback), but Safe Clear must **snapshot the framework's current installed files into the restore point before** any uninstall runs (Law A). Phase 1 owns that ordering; Phase 0 only fixes the path.

**Tests.**
- Install a `PlayFolder`-rooted framework into a fake `<root>/Game/` tree, then `Uninstall` → game folder byte-for-byte identical to pre-install (this is the round-trip the validate-then-extract rule already mandates but clearly wasn't covering the PlayFolder case).
- Install a `GameRoot`-rooted framework → uninstall still works (regression guard).
- Uninstall restores a pre-install backup to `Game/`, not the game root.

## P0.2 — `EnableMod` has no rollback

**Bug (verified).** `DisableEntry` moves primary files with a `moved` list and rolls back on any failure ([`Scanner.cs:429-442`](../../../src/ModManager.Core/Scanner.cs#L429-L442)). Its inverse `EnableMod` restores from the holding folder in a loop ([`Scanner.cs:498-516`](../../../src/ModManager.Core/Scanner.cs#L498-L516)) with **no try/catch, no `moved` list**, and `File.Copy` (lines 511, 513) has no overwrite flag. A leftover target file throws mid-loop, leaving the mod split between holding and live with no rollback. Under "leave mods active" this happens *after* games.json is reset — a state no manifest describes.

**Fix.** Give `EnableMod` the same two-phase rollback shape as `DisableEntry`:
- Track each `(src, destLive, destMirrors[])` copied; on any failure, reverse the copies already made (delete the live/mirror copies that were created this run, leave the holding folder intact), then `throw` a clear `InvalidOperationException`.
- Decide the overwrite policy explicitly: a pre-existing target is a real conflict — surface it (don't silently overwrite a user's live file). Use `File.Copy(…, overwrite: false)` and let the rollback fire, with a message naming the colliding file.

**Surface the silent skips.** `EnableMod` returns silently on `ReadOnly` (line 470), unreadable `meta.json` (line 487), null meta (line 488), and tool-owned target (line 493). Under "leave mods active" a silent skip is invisible data-loss. Change the internal path to return a structured `EnableOutcome { Enabled, Skipped, Reason }` (or collect skips via an `out`/callback) so the Phase 1 orchestrator can reconcile the live enabled set against the pre-clear set and tell the user what didn't come back. The public `EnableModAsync` signature stays source-compatible (skips are still non-throwing) — only Safe Clear consumes the structured outcome.

**Tests.**
- Enable with a colliding live file → rolls back, holding folder intact, throws with the colliding path named.
- Enable a multi-file mod where the 2nd file fails → 1st file's live + mirror copies removed, holding folder complete, throws.
- Enable a `ReadOnly`/owned mod → returns a `Skipped` outcome with reason, no files touched.
- Happy-path enable still deletes the holding folder and restores mirrors per `hadOnServer`.

## P0.3 — `MoveAny`: scope the catch, verify cross-volume copies

**Fragility (verified).** `MoveAny` ([`Scanner.cs:300-312`](../../../src/ModManager.Core/Scanner.cs#L300-L312)) catches **everything** and falls back to copy+delete — so a locked file (game running, Explorer handle) masquerades as a cross-volume move and gets a doomed `File.Copy`, losing the original "file is locked" context. `CopyDir` (314-319) copies with no overwrite flag and **no post-copy verification**, then `DeleteDir` deletes the source — so a partial copy on a full disk deletes an unverified original. This is the byte-for-byte promise breaking on the exact two-drive config the user named (game on D:, `%APPDATA%` on C:).

**Fix.**
- Scope the catch to `IOException` (mirroring `DirectInject.MoveAny` at [`DirectInject.cs:435`](../../../src/ModManager.Core/DirectInject.cs#L435)). Within it, distinguish cross-volume (`HResult` for `ERROR_NOT_SAME_DEVICE` / `0x80070011`) from a sharing violation (`0x80070020`); a sharing violation re-throws as a clear "file is in use — close the game?" rather than attempting a copy that will also fail.
- In the cross-volume fallback: copy → **verify file count + per-file length match** (hash optional, gated by size for speed) → only then delete the source. On mismatch, abort and leave the source intact (never delete an unverified copy).

Keep the change surgical — `MoveAny`/`CopyDir` are shared by disable/enable/uninstall; the verification path is additive and must not change same-volume `Move` behavior (the common case stays a fast rename).

**Tests.** (Use a temp-dir harness simulating two roots; force the fallback by stubbing the move to throw `ERROR_NOT_SAME_DEVICE`.)
- Cross-volume move of a folder verifies count+size then deletes source.
- A simulated mid-copy failure leaves the source intact and throws (source not deleted).
- A sharing-violation HResult surfaces a "file in use" error, not a copy attempt.

## P0.4 — Extract `PathGate` (shared by install AND restore)

**Why.** Law B requires the restore replay to run the same containment + forbidden-path check the install path uses. That logic currently lives only inside `FrameworkInstaller.Install` ([`FrameworkInstaller.cs:98-119`](../../../src/ModManager.Core/Frameworks/FrameworkInstaller.cs#L98)) and as `DirectInject.SafeRelative` ([`DirectInject.cs:340-352`](../../../src/ModManager.Core/DirectInject.cs#L340)). Extract a single canonical helper so install and restore share one gate.

```csharp
namespace ModManager.Core;
public static class PathGate
{
    /// True iff relNorm resolves to a real file path strictly inside installRootFull
    /// (rejects "", ".", "..", drive-rooted, and any traversal outside the root).
    public static bool IsContained(string relNorm, string installRootFull);

    /// True iff relNorm hits a forbidden basename or relative path (case-insensitive).
    public static bool IsForbidden(string relNorm, IReadOnlyList<string> forbidden);
}
```

**Fix.** Move the logic verbatim into `PathGate`, refactor `FrameworkInstaller.Install` to call it (no behavior change — covered by existing framework tests), and have `DirectInject.SafeRelative` delegate to `PathGate.IsContained`. Phase 1's restore replay calls `PathGate` on every write.

**Tests.**
- `IsContained` rejects `..\..\Windows\System32\x.dll`, drive-rooted `C:\x`, `.`, `""`; accepts `Game\dinput8.dll`.
- `IsForbidden` matches basename and full-relative entries case-insensitively.
- Framework install still refuses a forbidden path / traversal after the refactor (regression).

## P0.5 — `SpaceCheck` pre-flight helper (Core, pure)

**Why.** Law E needs a free-space gate; there is no `DriveInfo` use anywhere today. `DriveInfo` is `System.IO` (Core-legal).

```csharp
namespace ModManager.Core;
public static class SpaceCheck
{
    public sealed record Result(bool Ok, long RequiredBytes, long AvailableBytes, string VolumeRoot);

    /// requiredBytes = payload; adds marginPct (default 0.10) and a floorBytes (default 1 GiB)
    /// headroom, then compares to DriveInfo(volumeRoot).AvailableFreeSpace.
    public static Result Require(string volumeRoot, long requiredBytes,
                                 double marginPct = 0.10, long floorBytes = 1L << 30);
}
```

App computes the payload size (sum of bytes to archive) and the restore-point volume root, calls `Require`, and refuses with a precise message ("need X GB free on C:, have Y") before any move. Pure and unit-testable with an injected available-bytes value (overload that takes `availableBytes` directly, so tests don't touch a real drive).

**Tests.** payload+margin+floor under available → Ok; equal-to-threshold → not Ok; floor dominates for tiny payloads.

## P0.6 — `ModMeta.installedUtc` + source-URL capture at intake + opt-in backfill

**Why.** The off-boarding sheet wants each mod's source URL and install date. Today URLs are captured only via post-install identification (Nexus md5 / CurseForge fingerprint / Vortex — [`NexusRequests.cs:98`](../../../src/ModManager.Core/NexusRequests.cs#L98), [`CurseForgeRequests.cs:124`](../../../src/ModManager.Core/CurseForgeRequests.cs#L124)); raw `.zip`/`.pak`/loose drops leave `url` null, and there's no install timestamp at all.

**Fix — three additive pieces, no new URL field.**
1. **`ModMeta.installedUtc`** (`DateTime?`, JSON `installedUtc`) added to [`Mod.cs:49-67`](../../../src/ModManager.Core/Mod.cs#L49). Set by the App at intake when a mod first lands. camelCase round-trip test.
2. **Reuse `ModMeta.Url` + `IsManual`** for user-supplied source. The App (`MainViewModel.AddModsAsync`) optionally prompts for a source URL at intake — **App-side only**, Core intake (`Scanner.AddMods`) stays prompt-free. A user-typed URL sets `IsManual = true` so auto-identify can't clobber it (the lock already exists — [`Scanner.cs:1007-1030`](../../../src/ModManager.Core/Scanner.cs#L1007) / [`Mod.cs:63-66`](../../../src/ModManager.Core/Mod.cs#L63)). **Do not add a third URL field** — `Url` + `Source` already exist.
3. **`sourceConfidence`** (`string?`: `manual` | `fingerprint` | `md5` | `nameSearch` | `null`) recorded alongside `Url` so the sheet can hedge a low-confidence name-search match ("likely source:") vs a high-confidence one ("source:").
4. **Opt-in backfill sweep** — a Settings action "try to identify my installed mods" runs the existing `Md5IdentifyArchivesAsync` / `FingerprintIdentifyAsync` chain over already-extracted mods to populate `url` retroactively for day-one users. Non-blocking, surfaced count of resolved vs unknown.

The prompt is optional and never gates intake. Day-one sheets will be honest-incomplete for pre-existing mods until the backfill runs — stated, not implied.

**Tests.**
- `ModMeta` round-trips `installedUtc` + `sourceConfidence` as camelCase (string-assert keys).
- A user-typed URL with `IsManual=true` survives a subsequent auto-identify pass unclobbered.
- Backfill populates `url` for a mod whose md5 matches a stub identifier and marks `sourceConfidence`.

## P0.7 — `DisableEntry` Phase-2 mirror snapshot-first ordering

**Why (verified).** Phase 2 of `DisableEntry` deletes mirror copies in a loop, accumulating `hadOnServer` in memory, and writes `meta.json` **only after** the loop ([`Scanner.cs:444-459`](../../../src/ModManager.Core/Scanner.cs#L444)). A crash between deleting a mirror and writing `meta.json` permanently loses the record that the mirror existed — enable then can't recreate it, and the mod silently fails to apply after a restore. A mass Safe Clear sweep hits this window repeatedly.

**Fix.** Snapshot-first, mirroring `IniEditService.SaveWithBackup` ordering ([`IniEditService.cs:27-38`](../../../src/ModManager.Core/IniEdit/IniEditService.cs#L27)): write `meta.json` with the full file list and provisional `hadOnServer` **before** clearing any mirror, then clear mirrors, then rewrite `meta.json` with confirmed values. A crash mid-sweep leaves a record that errs toward "had a mirror" (safe — enable recreates it) rather than losing it.

**Tests.** Simulate a crash (throw) after the first mirror delete → `meta.json` already on disk with the file recorded; enable recreates the mirror.

## Out of scope (Phase 0)

- Any `RestorePoint*` type, the Safe Clear dialog/orchestration, the off-boarding sheet, Restore replay — all Phase 1.
- The onboarding wizard — Phase 2.
- Changing the public async signatures of enable/disable beyond the additive structured-outcome consumed only by Safe Clear.

## Testing summary

All Core, headless, test-first. `CorePurityTests` continues to guard the namespace boundary. New round-trip tests for `ModMeta` additions follow the string-asserting camelCase pattern. The framework install→uninstall round-trip and the cross-volume move-verify harness are the highest-value additions — they're the guards that make Phase 1's "byte-for-byte" claim real.
