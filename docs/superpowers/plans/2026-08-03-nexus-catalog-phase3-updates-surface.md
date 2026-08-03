# Catalog Phase 3 — the updates surface — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Answer "what do I need to update?" without opening every game. A per-game update badge on the Library home, plus a cross-game Updates view listing every mod with a newer version waiting.

**Architecture:** Pure Core reader over each game's already-persisted `metadata.json`, surfaced by the Library view-model and a new Updates view. **Zero network calls and zero disk scans** — it reports what previous Nexus refreshes already learned.

**Tech Stack:** .NET 10, C#, WinUI 3, xUnit.

## Rescope — read this before anything else

The original spec framed Phase 3 as "turn the catalog's `viewerUpdateAvailable` into a view." **That was wrong and is explicitly rejected.** The launcher already detects updates, and more accurately:

- `Mod.UpdateAvailable => NexusLatestVersion is { } v && v != Version` (`src/ModManager.Core/Mod.cs:64`) compares the **actually installed** version against the latest on Nexus, and already renders an UPDATE chip per row (`MainWindow.xaml:515`).
- The catalog's `viewerUpdateAvailable` reflects what **Nexus records the user downloading** — not what is really in the game folder. For "do I need to update this", it is strictly less correct. Do not use it here.

The real gap is reach, not detection:
- `RefreshNexusStatsAsync` (`MainViewModel.cs:1846`) only ever runs for the **active game** (`if (_ctx is null) return;`).
- The Library home surfaces **no** update signal, so finding out anything needs updating means opening each game and eyeballing rows.

Phase 3 closes that gap by **aggregating existing, already-persisted data**.

## Verified facts (read from this codebase, 2026-08-03)

- `ModMeta` (`src/ModManager.Core/Mod.cs:~95-107`) persists BOTH sides of the compare: `Version` (installed) and `NexusLatestVersion` (latest seen on Nexus). Both are nullable/additive.
- Per-game metadata lives at `Path.Combine(dataDir, "metadata.json")` (`Scanner.cs:80`), and **`Scanner.DataDirForGame(GameEntry)` is public** (`Scanner.cs:31`).
- Therefore an update count is computable from a single JSON read per game — **no `GameContext`, no scan, no network.**
- `Scanner.LoadMetadata(ctx)` exists but takes a full context; this plan does NOT use it (too heavy for a badge).
- Registry games are enumerated via the existing service used by `LibraryViewModel` (`reg.Games`, `LibraryViewModel.cs:240`).

## Global Constraints

- **Never a network call from the badge or the Updates view.** They report last-known state. Refreshing stays the existing, user-initiated per-game action. This keeps us inside Nexus AUP with no new traffic at all.
- **"Never checked" must never read as "up to date."** A game whose metadata has NO `NexusLatestVersion` on any mod has simply never been refreshed. It must show **no badge** (and be labelled unchecked in the view) — never "0 updates". This is the same null-vs-false honesty the catalog badges follow.
- **Core purity:** the reader lives in `src/ModManager.Core/` with no WinUI/WinRT (`CorePurityTests` enforces this). It must never throw — a missing, empty, or malformed `metadata.json` yields "unknown", not an exception.
- **camelCase JSON on disk** — this task only READS existing metadata; do not change its shape or write it.
- No `#if FULL`. The Updates surface is flavor-neutral (it reads local metadata, not Nexus), so it works on the STORE build too — where the data simply comes from whatever CurseForge/Nexus enrichment exists. STORE build + `scripts/check-store-seal.ps1` must stay green.
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<Nullable>enable</Nullable>` — zero warnings.
- **Never bare `dotnet build`/`dotnet test` at the repo root.** Tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. App: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (STORE adds `-p:Configuration=Store`).
- **After ANY XAML edit:** `rm -rf src/ModManager.App/obj/x64/Debug src/ModManager.App/bin/x64/Debug` before rebuilding — stale WinUI codegen crashes the app at `MainWindow.Connect` with an `InvalidCastException`. `MSB3021` "used by another process" = a running launcher, a file lock, not a compile error.
- Commits: conventional + `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Explicit paths in `git add`, never `-A`. Branch `feat/updates-surface`; never commit to `master`.
- Launcher-only. **No Abstractions change, no plugin release** — nothing here touches the plugin contract.

## File Structure

| File | Responsibility |
|---|---|
| `src/ModManager.Core/ModUpdateSummary.cs` (create) | Pure reader: game → update count + pending list, or "unknown". |
| `tests/ModManager.Tests/ModUpdateSummaryTests.cs` (create) | Its tests. |
| `src/ModManager.App/ViewModels/LibraryViewModel.cs` (modify) | Per-game badge count. |
| `src/ModManager.App/LibraryView.xaml` (modify) | Render the badge on the game row. |
| `src/ModManager.App/UpdatesView.xaml(.cs)` (create) | Cross-game Updates list. |
| `src/ModManager.App/MainWindow.xaml(.cs)` (modify) | Host + entry point for the Updates view. |
| `docs/smoke-tests/pending.md` (modify) | Smoke entries. |

---

### Task 1: Core — the update summary reader

**Files:** create `src/ModManager.Core/ModUpdateSummary.cs`, `tests/ModManager.Tests/ModUpdateSummaryTests.cs`.

**Produces** (consumed by Tasks 2 and 3):

```csharp
/// <summary>One mod with a newer version available.</summary>
public sealed record PendingUpdate(string GameId, string GameName, string ModKey, string ModName,
    string? InstalledVersion, string LatestVersion, int? NexusModId, string? NexusDomain);

/// <summary>What we know about one game's updates. Checked=false means the game has never had a Nexus
/// refresh, which is NOT the same as "up to date" and must not render as zero.</summary>
public sealed record GameUpdateSummary(string GameId, string GameName, bool Checked,
    IReadOnlyList<PendingUpdate> Pending)
{
    public int Count => Pending.Count;
}

public static class ModUpdateSummary
{
    public static GameUpdateSummary ForGame(GameEntry game);
    public static IReadOnlyList<GameUpdateSummary> ForGames(IEnumerable<GameEntry> games);
}
```

Implementation: resolve `Scanner.DataDirForGame(game)`, read `metadata.json`, deserialize the persisted key→`ModMeta` map with the SAME `JsonSerializerOptions` the rest of the metadata path uses (camelCase — read `Scanner`'s options and reuse, do not invent new ones). A mod is pending when `NexusLatestVersion` is non-blank AND differs from `Version` (mirror `Mod.UpdateAvailable`'s exact rule — read it and match, including its null handling). `Checked` is true when ANY entry has a non-blank `NexusLatestVersion`.

**Never throws:** missing file, empty file, malformed JSON, unreadable directory → `Checked = false`, empty list.

- [ ] **Step 1: Write the failing tests.** Cover: a metadata file with 2 of 5 mods having a newer `NexusLatestVersion` → Count 2, `Checked` true, and the pending entries carry the right names/versions; equal versions → not pending; `NexusLatestVersion` null or blank → not pending AND doesn't set `Checked`; a file where NO mod has a latest version → `Checked` **false**, Count 0 (the "never refreshed" case — assert `Checked` is false explicitly, this is the honesty rule); missing file → `Checked` false, no throw; malformed JSON → `Checked` false, no throw; `ForGames` aggregates and preserves per-game identity. Write real temp files (use the project's existing temp-dir test pattern — grep another Core test for how it makes a scratch dir).
- [ ] **Step 2: Run, confirm failure.** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter ModUpdateSummary`
- [ ] **Step 3: Implement** in Core, pure, no WinUI.
- [ ] **Step 4: Focused tests pass; run the FULL suite** (1480 pass today; all stay green, including `CorePurityTests`).
- [ ] **Step 5: Commit** `feat(updates): Core reader for per-game pending updates`.

---

### Task 2: Library home — the per-game badge

**Files:** modify `src/ModManager.App/ViewModels/LibraryViewModel.cs`, `src/ModManager.App/LibraryView.xaml`.

Each game row gains an update badge: the count when `Checked && Count > 0`, otherwise **collapsed**. Never render "0" and never render anything for an unchecked game — an absent badge means "nothing known", which is the truth.

- [ ] **Step 1: Read `LibraryViewModel.cs` and `LibraryView.xaml` first.** Find how a game row's other chips/labels are built and bound (there is an existing engine-tier chip; match its shape and theme resources — do not hardcode colors).
- [ ] **Step 2: Add the count** to the row view-model from `ModUpdateSummary.ForGame`, plus a `Visibility` for the badge. Compute it where the rows are built, off the UI thread if the existing code does file work off-thread; it is a small local file read per game, so keep it cheap and do NOT make it async-per-row if the surrounding code is synchronous.
- [ ] **Step 3: Render the badge** in the row template, visually consistent with the existing chips. Tooltip: "N mods have updates available. Open the game to review them." (sentence case, period).
- [ ] **Step 4: Clean + build FULL and STORE, run the seal, run the suite.**
- [ ] **Step 5: Commit** `feat(updates): per-game update badge on the library`.

---

### Task 3: The cross-game Updates view

**Files:** create `src/ModManager.App/UpdatesView.xaml(.cs)`; modify `src/ModManager.App/MainWindow.xaml(.cs)`.

- Read `src/ModManager.App/NexusCatalogView.xaml(.cs)` first — it is the established full-size UserControl-in-a-host-Grid pattern, and this view follows it (the storefront's `CatalogHost` swap is the model).
- Content: rows grouped by game — mod name, installed version → latest version, and a **button that opens that game** (so the user lands where the existing UPDATE chips and update flow already live). Do NOT build a new update-applying flow; this is a directory, not a new mechanism.
- Empty state when nothing is pending: distinguish **"no updates found"** (games have been checked) from **"no games have been checked yet"** (nothing has ever refreshed) — with a line telling the user that opening a game and using Refresh is what populates this.
- Entry point: a toolbar affordance on the Library home showing the total pending count, visible only when the total is > 0. Keep the Library home uncluttered otherwise.
- Never throws; the view builds entirely from `ModUpdateSummary.ForGames`.

- [ ] **Step 1: Build the view + host + entry point.**
- [ ] **Step 2: Clean (`rm -rf obj/x64/Debug bin/x64/Debug`), build FULL + STORE, run the seal.**
- [ ] **Step 3: Run the full suite.**
- [ ] **Step 4: Add smoke entries** to `docs/smoke-tests/pending.md`: badge appears on a game with known updates and matches the number of UPDATE chips inside that game; a never-refreshed game shows NO badge (not "0"); refreshing a game then returning to the Library updates the badge; the Updates view lists the same mods across games and its open-game button lands on the right game; both empty states read correctly; nothing in this surface makes a network call (it works with Nexus disconnected — verify by disconnecting and confirming the badge/list still render from stored data).
- [ ] **Step 5: Commit** `feat(updates): cross-game updates view`.

---

### Task 4: Verification

- [ ] Full launcher suite 0 failed (plugin suite untouched — no plugin change in this phase).
- [ ] FULL 0 errors; STORE 0 errors; `pwsh -File scripts/check-store-seal.ps1` → **STORE seal OK**.
- [ ] Audit: no network call anywhere in the new code (grep the new files for `Http`, `SendAsync`, `NexusSource` — there should be none); no WinUI/WinRT in Core; no `#if FULL`; no write to `metadata.json`; nothing shows "0 updates" for an unchecked game.
- [ ] Confirm the existing per-row UPDATE chip and `RefreshNexusStatsAsync` are unchanged — this phase adds a reader and surfaces, it does not modify detection.

## Release

Launcher-only → merge, tag **v0.15.0**, publish the draft. **No plugin release and no NuGet wait** — the plugin contract is untouched.
