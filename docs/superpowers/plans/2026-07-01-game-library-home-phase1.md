# Game Library home — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Fresh subagent per task + two-stage review between tasks. Steps use `- [ ]`.

**Goal:** Ship the Library home as the launcher's landing surface — a hybrid recent-cover-strip + mod-state list of every registered game, with per-game Play (modded/vanilla) + Manage, a discovery add-lane, and correct recency from 626's own launches + Steam.

**Architecture:** Presentation over data that mostly already exists, plus a pure-Core recency ladder. New Core units (`GameLibraryRow`, `GameLibraryBuilder`, `ILastPlayedSource`, `RecencyLadder`, `LastPlayed`) are TDD'd; the recency readers (Steam ACF, own-launch log) are App-side adapters behind the Core interface (same pattern as `IStoreLibrary`). The WinUI view + view-model are App-side, verified by build + smoke.

**Tech Stack:** .NET 10 / C# (Core: pure + xUnit; App: WinUI 3). Both flavors.

## Global Constraints

- **Both STORE and FULL** — no `#if FULL` anywhere in this feature. Verify STORE seals (`pwsh scripts/check-store-seal.ps1`).
- **Core purity** — recency readers + store/registry/IO live App-side behind Core interfaces; `GameLibraryBuilder` + `RecencyLadder` are pure Core. `CorePurityTests` must stay green.
- **camelCase JSON on disk** — new `GameEntry` fields + the launch log use `JsonNamingPolicy.CamelCase` via `AtomicJson`; every new persisted shape ships a round-trip test with a string-contains assertion on the camelCase key.
- **Reversibility** — recency/launch-log writes are additive metadata (atomic temp+rename), never touch game files; Play reuses the existing launch path.
- **Never bare `dotnet` at repo root.** Core tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. App build: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (FULL) + `-p:Configuration=Store` (STORE); kill `ModManager.App` first (`powershell Get-Process ModManager.App -ErrorAction SilentlyContinue | Stop-Process -Force`).
- **Conventional commits** + trailer `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`. Branch `feat/game-library-home`.
- **Phase 1 recency sources only:** `OwnLaunchLastPlayedSource` + `SteamLastPlayedSource`. `ILastPlayedSource` + `RecencyLadder` are built to accept more sources (Phase 2 adds GOG + UserAssist) — design the seams, don't build the Phase 2 readers.

## File structure

- Create `src/ModManager.Core/Recency/LastPlayed.cs` — the recency value record + `ILastPlayedSource`.
- Create `src/ModManager.Core/Recency/RecencyLadder.cs` — best-available-wins merge.
- Create `src/ModManager.Core/Library/GameLibraryRow.cs` — per-game view-data record.
- Create `src/ModManager.Core/Library/GameLibraryBuilder.cs` — rows from games + sources.
- Modify `src/ModManager.Core/GameEntry.cs` — add `StoreSource` + `LastLaunchedUtc`.
- Create `src/ModManager.App/Services/SteamLastPlayedSource.cs` + `OwnLaunchLastPlayedSource.cs`.
- Create `src/ModManager.App/Services/LaunchLog.cs` — append-only own-launch log (camelCase, atomic).
- Modify the launch path (`LauncherService` / its caller) — stamp `LastLaunchedUtc` + append a launch-log entry.
- Create `src/ModManager.App/ViewModels/LibraryViewModel.cs` + `src/ModManager.App/LibraryView.xaml`(.cs).
- Modify `src/ModManager.App/MainWindow.xaml`(.cs) — land on Library; navigate Library ↔ game view.
- Modify `tests/ModManager.Tests/` — Core tests per task; `docs/smoke-tests/pending.md` — Phase 1 entries.

---

## Task 1: `GameEntry` gains `StoreSource` + `LastLaunchedUtc` (Core, TDD)

**Files:**
- Modify: `src/ModManager.Core/GameEntry.cs`
- Test: `tests/ModManager.Tests/GameEntryRecencyFieldsTests.cs`

**Interfaces:**
- Produces: `GameEntry.StoreSource` (string?, `"steam"|"gog"|"epic"|"xbox"|"manual"`), `GameEntry.LastLaunchedUtc` (DateTime?). Both optional; existing games deserialize with them null.

- [ ] **Step 1: Write the failing round-trip test**

```csharp
using System.Text.Json;
using ModManager.Core;

namespace ModManager.Tests;

public class GameEntryRecencyFieldsTests
{
    private static readonly JsonSerializerOptions Opts = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    [Fact]
    public void StoreSource_and_LastLaunchedUtc_round_trip_as_camelCase()
    {
        var e = new GameEntry { Id = "g", GameName = "G", GameRoot = @"C:\g",
            StoreSource = "steam", LastLaunchedUtc = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc) };
        var json = JsonSerializer.Serialize(e, Opts);
        Assert.Contains("\"storeSource\"", json);
        Assert.Contains("\"lastLaunchedUtc\"", json);
        Assert.DoesNotContain("\"StoreSource\"", json);
        var back = JsonSerializer.Deserialize<GameEntry>(json, Opts)!;
        Assert.Equal("steam", back.StoreSource);
        Assert.Equal(e.LastLaunchedUtc, back.LastLaunchedUtc);
    }

    [Fact]
    public void Existing_json_without_the_new_fields_deserializes_with_nulls()
    {
        var back = JsonSerializer.Deserialize<GameEntry>(
            "{\"id\":\"g\",\"gameName\":\"G\",\"gameRoot\":\"C:\\\\g\"}", Opts)!;
        Assert.Null(back.StoreSource);
        Assert.Null(back.LastLaunchedUtc);
    }
}
```

- [ ] **Step 2: Run, verify fail** — `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter GameEntryRecencyFields` → FAIL (members not defined). If `GameEntry` is a positional record, use object-initializer-friendly properties; match its existing construction style (read the file first).
- [ ] **Step 3: Add the two properties to `GameEntry`** (match the file's existing property style — `init` accessors, nullable, no attribute needed since camelCase is set at the serializer):

```csharp
public string? StoreSource { get; init; }
public DateTime? LastLaunchedUtc { get; init; }
```

- [ ] **Step 4: Run, verify pass** — same filter → PASS. Then full Core suite → green.
- [ ] **Step 5: Commit** — `feat(library): GameEntry gains StoreSource + LastLaunchedUtc`.

## Task 2: `LastPlayed` + `ILastPlayedSource` + `RecencyLadder` (Core, TDD)

**Files:**
- Create: `src/ModManager.Core/Recency/LastPlayed.cs`, `src/ModManager.Core/Recency/RecencyLadder.cs`
- Test: `tests/ModManager.Tests/Recency/RecencyLadderTests.cs`

**Interfaces:**
- Produces:
  - `record LastPlayed(DateTime? LastPlayedUtc, TimeSpan? Playtime, string Source)` (+ `static LastPlayed None`).
  - `record GameRecencyKey(string? SteamAppId, string? GameRoot, string? LaunchExe, string Id)` — what a source needs to look a game up.
  - `interface ILastPlayedSource { string Name { get; } LastPlayed? ForGame(GameRecencyKey key); }`
  - `static class RecencyLadder { LastPlayed Merge(GameRecencyKey key, IReadOnlyList<ILastPlayedSource> sources); }` — first source (in order) with a non-null `LastPlayedUtc` wins last-played; first with a non-null `Playtime` wins playtime (independently); returns `LastPlayed.None` if all miss.

- [ ] **Step 1: Write the failing tests**

```csharp
using ModManager.Core.Recency;

namespace ModManager.Tests.Recency;

public class RecencyLadderTests
{
    private sealed class Fake(string name, LastPlayed? r) : ILastPlayedSource
    { public string Name => name; public LastPlayed? ForGame(GameRecencyKey k) => r; }

    private static readonly GameRecencyKey Key = new(SteamAppId: "1", GameRoot: @"C:\g", LaunchExe: "g.exe", Id: "g");
    private static readonly DateTime T1 = new(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void First_source_with_last_played_wins()
    {
        var r = RecencyLadder.Merge(Key, new ILastPlayedSource[]
        {
            new Fake("own", new LastPlayed(T2, TimeSpan.FromHours(3), "own")),
            new Fake("steam", new LastPlayed(T1, null, "steam")),
        });
        Assert.Equal(T2, r.LastPlayedUtc);
        Assert.Equal(TimeSpan.FromHours(3), r.Playtime);
        Assert.Equal("own", r.Source);
    }

    [Fact]
    public void Playtime_falls_through_independently_of_last_played()
    {
        // own has last-played but no playtime; gog (later) has playtime -> take gog's playtime
        var r = RecencyLadder.Merge(Key, new ILastPlayedSource[]
        {
            new Fake("own", new LastPlayed(T2, null, "own")),
            new Fake("gog", new LastPlayed(T1, TimeSpan.FromHours(5), "gog")),
        });
        Assert.Equal(T2, r.LastPlayedUtc);           // own wins last-played
        Assert.Equal(TimeSpan.FromHours(5), r.Playtime); // gog supplies playtime
    }

    [Fact]
    public void All_miss_returns_none()
    {
        var r = RecencyLadder.Merge(Key, new ILastPlayedSource[] { new Fake("a", null), new Fake("b", LastPlayed.None) });
        Assert.Null(r.LastPlayedUtc);
        Assert.Null(r.Playtime);
    }

    [Fact]
    public void A_throwing_source_is_skipped_not_fatal()
    {
        var throwing = new ThrowingSource();
        var r = RecencyLadder.Merge(Key, new ILastPlayedSource[] { throwing, new Fake("steam", new LastPlayed(T1, null, "steam")) });
        Assert.Equal(T1, r.LastPlayedUtc);
    }
    private sealed class ThrowingSource : ILastPlayedSource
    { public string Name => "boom"; public LastPlayed? ForGame(GameRecencyKey k) => throw new InvalidOperationException(); }
}
```

- [ ] **Step 2: Run, verify fail** — `--filter RecencyLadder` → FAIL (types undefined).
- [ ] **Step 3: Implement `LastPlayed.cs`:**

```csharp
namespace ModManager.Core.Recency;

public sealed record LastPlayed(DateTime? LastPlayedUtc, TimeSpan? Playtime, string Source)
{
    public static readonly LastPlayed None = new(null, null, "none");
}

public sealed record GameRecencyKey(string? SteamAppId, string? GameRoot, string? LaunchExe, string Id);

public interface ILastPlayedSource
{
    string Name { get; }
    LastPlayed? ForGame(GameRecencyKey key);
}
```

- [ ] **Step 4: Implement `RecencyLadder.cs`:**

```csharp
namespace ModManager.Core.Recency;

public static class RecencyLadder
{
    public static LastPlayed Merge(GameRecencyKey key, IReadOnlyList<ILastPlayedSource> sources)
    {
        DateTime? lastPlayed = null; TimeSpan? playtime = null; string src = "none";
        foreach (var s in sources)
        {
            LastPlayed? r;
            try { r = s.ForGame(key); } catch { continue; } // a bad source never breaks the ladder
            if (r is null) continue;
            if (lastPlayed is null && r.LastPlayedUtc is not null) { lastPlayed = r.LastPlayedUtc; src = r.Source; }
            if (playtime is null && r.Playtime is not null) playtime = r.Playtime;
            if (lastPlayed is not null && playtime is not null) break;
        }
        return lastPlayed is null && playtime is null ? LastPlayed.None : new LastPlayed(lastPlayed, playtime, src);
    }
}
```

- [ ] **Step 5: Run, verify pass** — `--filter RecencyLadder` → PASS. Full Core suite → green.
- [ ] **Step 6: Commit** — `feat(recency): LastPlayed + ILastPlayedSource + best-available-wins ladder (Core)`.

## Task 3: `GameLibraryRow` + `GameLibraryBuilder` (Core, TDD)

**Files:**
- Create: `src/ModManager.Core/Library/GameLibraryRow.cs`, `src/ModManager.Core/Library/GameLibraryBuilder.cs`
- Test: `tests/ModManager.Tests/Library/GameLibraryBuilderTests.cs`

**Interfaces:**
- Consumes: `GameEntry`, `ILastPlayedSource`/`RecencyLadder` (Task 2), an injected mod-state lookup + tier/ban/loader lookups (as delegates, to keep Core pure and testable).
- Produces:
  - `enum EngineTier { EngineCurated, NexusOnly, Unknown }`
  - `record GameLibraryRow(string Id, string Name, string? StoreSource, string? CoverPath, LastPlayed Recency, int ModCount, int EnabledCount, string? ActiveProfile, EngineTier Tier, string? BanRisk, IReadOnlyList<string> DetectedLoaders, string? NexusDomain)`
  - `record GameModState(int ModCount, int EnabledCount, string? ActiveProfile)`
  - `static class GameLibraryBuilder { IReadOnlyList<GameLibraryRow> Build(IReadOnlyList<GameEntry> games, IReadOnlyList<ILastPlayedSource> sources, Func<GameEntry, GameModState> modState, Func<GameEntry, EngineTier> tier, Func<GameEntry, string?> banRisk, Func<GameEntry, IReadOnlyList<string>> loaders, Func<GameEntry, string?> cover); }` — rows ordered by `Recency.LastPlayedUtc` desc (nulls last, then by name).

- [ ] **Step 1: Write the failing tests** (ordering + rollup + nulls-last):

```csharp
using ModManager.Core;
using ModManager.Core.Library;
using ModManager.Core.Recency;

namespace ModManager.Tests.Library;

public class GameLibraryBuilderTests
{
    private sealed class Src(Dictionary<string, DateTime> byId) : ILastPlayedSource
    { public string Name => "t";
      public LastPlayed? ForGame(GameRecencyKey k) => byId.TryGetValue(k.Id, out var d) ? new LastPlayed(d, null, "t") : null; }

    [Fact]
    public void Rows_are_ordered_most_recent_first_nulls_last_then_name()
    {
        var games = new[]
        {
            new GameEntry { Id = "a", GameName = "Alpha", GameRoot = "x" },
            new GameEntry { Id = "b", GameName = "Bravo", GameRoot = "x" },
            new GameEntry { Id = "c", GameName = "Charlie", GameRoot = "x" }, // no recency
        };
        var src = new Src(new() {
            ["a"] = new DateTime(2026,7,1,10,0,0,DateTimeKind.Utc),
            ["b"] = new DateTime(2026,7,1,12,0,0,DateTimeKind.Utc),
        });
        var rows = GameLibraryBuilder.Build(games, new ILastPlayedSource[]{src},
            _ => new GameModState(0,0,null), _ => EngineTier.Unknown, _ => null,
            _ => Array.Empty<string>(), _ => null);
        Assert.Equal(new[]{"b","a","c"}, rows.Select(r => r.Id).ToArray()); // b (12:00), a (10:00), c (null)
    }

    [Fact]
    public void Mod_state_and_tier_roll_up_onto_the_row()
    {
        var games = new[] { new GameEntry { Id = "a", GameName = "Alpha", GameRoot = "x" } };
        var rows = GameLibraryBuilder.Build(games, Array.Empty<ILastPlayedSource>(),
            _ => new GameModState(12, 8, "Ironman"), _ => EngineTier.EngineCurated, _ => "high",
            _ => new[]{"Mod Engine 2"}, _ => @"C:\cover.jpg");
        var r = Assert.Single(rows);
        Assert.Equal(12, r.ModCount); Assert.Equal(8, r.EnabledCount);
        Assert.Equal("Ironman", r.ActiveProfile); Assert.Equal(EngineTier.EngineCurated, r.Tier);
        Assert.Equal("high", r.BanRisk); Assert.Contains("Mod Engine 2", r.DetectedLoaders);
    }
}
```

- [ ] **Step 2: Run, verify fail.** `--filter GameLibraryBuilder`.
- [ ] **Step 3: Implement `GameLibraryRow.cs`** (the records + enum above) and **`GameLibraryBuilder.cs`:**

```csharp
namespace ModManager.Core.Library;
using ModManager.Core;
using ModManager.Core.Recency;

public static class GameLibraryBuilder
{
    public static IReadOnlyList<GameLibraryRow> Build(
        IReadOnlyList<GameEntry> games, IReadOnlyList<ILastPlayedSource> sources,
        Func<GameEntry, GameModState> modState, Func<GameEntry, EngineTier> tier,
        Func<GameEntry, string?> banRisk, Func<GameEntry, IReadOnlyList<string>> loaders,
        Func<GameEntry, string?> cover)
    {
        var rows = new List<GameLibraryRow>(games.Count);
        foreach (var g in games)
        {
            var key = new GameRecencyKey(g.SteamAppId, g.GameRoot, g.LaunchExe, g.Id);
            var recency = RecencyLadder.Merge(key, sources);
            var ms = modState(g);
            rows.Add(new GameLibraryRow(g.Id, g.GameName, g.StoreSource, cover(g), recency,
                ms.ModCount, ms.EnabledCount, ms.ActiveProfile, tier(g), banRisk(g), loaders(g), g.NexusGameDomain));
        }
        return rows
            .OrderByDescending(r => r.Recency.LastPlayedUtc ?? DateTime.MinValue)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
```

- [ ] **Step 4: Run, verify pass.** Full Core suite green.
- [ ] **Step 5: Commit** — `feat(library): GameLibraryRow + builder (recency-ordered rows, Core)`.

## Task 4: Own-launch log + `OwnLaunchLastPlayedSource` + launch stamping (App)

**Files:**
- Create: `src/ModManager.App/Services/LaunchLog.cs`, `src/ModManager.App/Services/OwnLaunchLastPlayedSource.cs`
- Modify: the launch caller (find via `LauncherService.Launch` at `src/ModManager.App/Services/LauncherService.cs:115`) + registry save.
- Test: `tests/ModManager.Tests/` — a Core-side round-trip test for the launch-log record shape (put the record in Core if it needs a test; else App-tested by build+smoke).

**Interfaces:**
- Consumes: the existing launch path + `GameEntry`.
- Produces: `LaunchLog` (append `{ gameId, startedUtc, endedUtc? }` camelCase via `AtomicJson`); `OwnLaunchLastPlayedSource : ILastPlayedSource` (reads `LastLaunchedUtc` + summed session durations from the log → `LastPlayed`).

- [ ] **Step 1:** Put the persisted record in Core so it's round-trip-testable: `src/ModManager.Core/Recency/LaunchLogEntry.cs` → `record LaunchLogEntry(string GameId, DateTime StartedUtc, DateTime? EndedUtc)`. Write a camelCase round-trip test (`--filter LaunchLog`), assert `"gameId"`/`"startedUtc"` present, PascalCase absent. Implement to pass.
- [ ] **Step 2:** `LaunchLog.cs` (App) — append-only JSON at `%LOCALAPPDATA%\ModManagerBuilder\launch-log.json` via `AtomicJson` (camelCase). Methods: `Append(LaunchLogEntry)`, `ForGame(string id) : IReadOnlyList<LaunchLogEntry>`.
- [ ] **Step 3:** Stamp on launch — in the launch caller, after a successful `LauncherService.Launch`, set `GameEntry.LastLaunchedUtc = DateTime.UtcNow`, persist the registry (existing save path), and `LaunchLog.Append`. Wrap in try/catch — a log/stamp failure is non-fatal (recency degrades to Steam). **No change to the launch mechanism itself** (reversibility intact).
- [ ] **Step 4:** `OwnLaunchLastPlayedSource.cs` — `ForGame(key)` returns `new LastPlayed(entry.LastLaunchedUtc, summedPlaytimeFromLog, "626")` when we have a stamp; else null. (Playtime = sum of `EndedUtc - StartedUtc` where both present.)
- [ ] **Step 5: Build both flavors + seal.** FULL + STORE 0 errors; `check-store-seal.ps1` OK. Core suite green.
- [ ] **Step 6: Commit** — `feat(recency): own-launch log + source + launch-time stamping`.

## Task 5: `SteamLastPlayedSource` (App)

**Files:**
- Create: `src/ModManager.App/Services/SteamLastPlayedSource.cs`
- (Reuse the existing appmanifest parse — `SteamParse.ParseAppManifest` / `SteamService`.)

**Interfaces:**
- Consumes: the Steam library scan (`IStoreLibrary`/`SteamService`) which already reads `InstalledGame.LastPlayed` (Unix seconds).
- Produces: `SteamLastPlayedSource : ILastPlayedSource` — `ForGame(key)` matches `key.SteamAppId` to the scanned `InstalledGame`, returns `new LastPlayed(fromUnixSeconds(LastPlayed), null, "steam")` (playtime null — Steam local has none); null when no Steam appid / no match / no `LastPlayed` (pre-2020 ACF).

- [ ] **Step 1:** Implement the source over the existing Steam scan (cache the scan per load; don't re-read per game). Parse `LastPlayed` (string Unix seconds) → `DateTimeOffset.FromUnixTimeSeconds(...).UtcDateTime`; guard empty/unparseable → null.
- [ ] **Step 2: Build both flavors + seal.** 0 errors, seal OK.
- [ ] **Step 3: Commit** — `feat(recency): Steam appmanifest last-played source`.

## Task 6: `LibraryViewModel` (App)

**Files:**
- Create: `src/ModManager.App/ViewModels/LibraryViewModel.cs`
- (Reuse: `GameLibraryBuilder`, the two sources, `BanRiskCatalog`, `LoaderScan`, `EffectiveManifest`/`KnownEngines` for tier, `IStoreLibrary` for cover + discovery, the existing per-game mod-state read.)

**Interfaces:**
- Consumes: registered games (registry), the Core builder + sources, existing lookups.
- Produces: `LibraryViewModel` with `ObservableCollection<GameLibraryRow> Rows` (or a thin App row wrapper adding `ImageSource Cover` like `GameOption` does), `RecentRows` (top-N, N=6), `DiscoveryRows` (installed-unadded), `SearchText`, filter properties, and commands: `OpenGame(row)` (→ `SetActiveGame` + navigate), `PlayModded(row)` / `PlayVanilla(row)` (→ launch path), `AddDiscovered(row)` (→ `AddGameDialog` flow).

- [ ] **Step 1:** Implement `LoadAsync`: read games → build rows via `GameLibraryBuilder.Build(...)` wiring the delegates to the real lookups (mod-state from the existing per-game read; tier from `EffectiveManifest`/`KnownEngines`+nexus; ban from `BanRiskCatalog.ByAppId`; loaders from `LoaderScan.Detect`; cover from `IStoreLibrary.ResolveCoverArtPath` else null). Sources: `[OwnLaunchLastPlayedSource, SteamLastPlayedSource]` (order matters — own wins).
- [ ] **Step 2:** `RecentRows` = first 6 of `Rows`; `DiscoveryRows` = store scan minus registered app ids. Search filters `Rows` by name (case-insensitive); filter props narrow by source/tier/ban.
- [ ] **Step 3:** Commands delegate to existing services (`SetActiveGame`, the launch path, `AddGameDialog`). Cover fallback (placeholder) computed App-side (themed initial) when `CoverPath` is null.
- [ ] **Step 4: Build both flavors + seal.** 0 errors, seal OK.
- [ ] **Step 5: Commit** — `feat(library): LibraryViewModel (rows, recent, discovery, commands)`.

## Task 7: `LibraryView` + navigation (App)

**Files:**
- Create: `src/ModManager.App/LibraryView.xaml` + `.xaml.cs`
- Modify: `src/ModManager.App/MainWindow.xaml` + `.xaml.cs` (land on Library; Library ↔ game-view navigation; keep the title-bar switcher as a quick-switch alongside a Home button).

**Interfaces:**
- Consumes: `LibraryViewModel`.
- Produces: the hybrid view (recent cover strip via a horizontal `ItemsRepeater`/`ListView` of big cards; the all-games list via a vertical `ItemsRepeater` of mod-state rows; the collapsed discovery lane at the bottom). A row template shows cover + name + source badge + recency + mod-state line + tier/ban/loader chips + Play split-button + Manage.

- [ ] **Step 1:** Build `LibraryView.xaml` — recent strip (cover cards), all-games list (mod-state rows with the chips + Play/Manage), discovery `Expander`. Use existing theme tokens (accent, glass, text hierarchy). Bind to `LibraryViewModel`.
- [ ] **Step 2:** Wire navigation in `MainWindow` — app lands on `LibraryView`; `OpenGame` swaps to the existing game/mod view for the active game; a Home affordance returns to `LibraryView`. The existing ComboBox switcher stays as a quick in-context switch.
- [ ] **Step 3:** Cover placeholder — themed initial when no cover art (matches `GameOption.Cover` null-fallback behavior).
- [ ] **Step 4: Build both flavors + seal.** FULL + STORE 0 errors; `check-store-seal.ps1` OK. Launch the FULL build once to eyeball the view renders + navigation works.
- [ ] **Step 5: Smoke entry** — append to `docs/smoke-tests/pending.md`: open 626 → lands on Library home; recent strip shows most-recently-played cover cards; each row shows source + recency + mods N·M on + tier + ban/loader chips; Play modded/vanilla launches (and updates recency next load); Manage/body-click opens the game's mod view; Home returns; discovery lane lists installed-unadded games and adds one in a click; a game with no recency shows "unknown," never a fake time; STORE build renders identically.
- [ ] **Step 6: Commit** — `feat(library): Library home view + landing navigation`.

## Self-review

- **Spec coverage:** Library home landing (T7) · hybrid layout (T7) · recency ladder + Phase-1 sources (T2/T4/T5) · GameEntry fields + launch stamping (T1/T4) · row content incl. tier/ban/loaders (T3/T6/T7) · launch hub (T6/T7) · discovery lane (T6/T7) · cover + placeholder (T6/T7). Phase 2 (GOG, UserAssist) intentionally out — the `ILastPlayedSource` seam (T2) is where they slot in.
- **Placeholder scan:** none — Core tasks carry real TDD code; App/XAML tasks carry concrete interfaces + build/seal/smoke gates (WinUI views aren't unit-testable, matching the repo's established plan style).
- **Type consistency:** `LastPlayed`/`GameRecencyKey`/`ILastPlayedSource` (T2) are consumed unchanged in T3/T4/T5/T6; `GameLibraryRow`/`GameModState`/`EngineTier` (T3) consumed in T6/T7; `GameEntry.StoreSource`/`LastLaunchedUtc` (T1) consumed in T3/T4.
- **Core purity:** all readers (Steam ACF, launch log) are App-side; builder + ladder are pure Core. `CorePurityTests` in every gate.
- **Reversibility + camelCase:** launch-log + GameEntry writes are additive/atomic/camelCase with round-trip tests (T1, T4); launch mechanism untouched.
