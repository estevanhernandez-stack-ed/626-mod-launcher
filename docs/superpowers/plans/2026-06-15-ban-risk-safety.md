# Ban-Risk Safety Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A game-level ban-risk flag that forces a warning and gates enabling (warn-and-acknowledge, never refuse) on anti-cheat/ban-risk games — plus the principle as a canonical operating law.

**Architecture:** A descriptive `banRisk` string-enum (`null`/`low`/`medium`/`high`) on the game manifest, resolved **live by Steam app id** through a `BanRiskCatalog` facade (twin of `NexusDomains`) — no runtime `GameEntry` field, no registry migration, so a feed update protects already-added games immediately. A pure `BanRiskRules.ShouldGateEnable` decision is consulted by *every* enable path (per-row, variant, bulk) so none can bypass it; a per-game `BanRiskAckStore` (twin of `MpCompatStore`) remembers the acknowledgment. Merge uses a never-downgrade MAX so a remote feed can raise but never silently lower a curated risk. The App adds a confirm dialog (mirroring `ConfirmOwnedToggleAsync`) and a persistent banner (mirroring `MpWarning`).

**Tech Stack:** .NET 10, C# (nullable-on, warnings-as-errors), xUnit. Pure Core + thin App shell; `CorePurityTests` bans WinUI/WinRT. Test project references **Core only** — App gate/banner are build- + smoke-verified; all decisions live in tested Core.

**Spec:** [`docs/superpowers/specs/2026-06-15-ban-risk-safety-design.md`](../specs/2026-06-15-ban-risk-safety-design.md)

**Build/test commands (never bare `dotnet` at root):**
- Core tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` (`--filter <Class>` to scope)
- App build: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64` (kill any running `ModManager.App` first — MSB3027 lock)

---

## Task 1: `GameManifestEntry.BanRisk` field + round-trip test

**Files:**
- Modify: `src/ModManager.Core/Manifest/GameManifest.cs:39` (after `Featured`)
- Test: `tests/ModManager.Tests/Manifest/GameManifestJsonTests.cs`

- [ ] **Step 1: Write the failing test** — append to `GameManifestJsonTests`:

```csharp
    [Fact]
    public void BanRisk_round_trips_as_camelCase()
    {
        var original = new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry { Id = "marvel-rivals", Name = "Marvel Rivals", BanRisk = "high" },
            },
        };

        var json = JsonSerializer.Serialize(original, ManifestJson.Options);
        Assert.Contains("\"banRisk\"", json);          // camelCase key on disk
        Assert.DoesNotContain("\"BanRisk\"", json);     // guards against PascalCase regression

        var back = JsonSerializer.Deserialize<GameManifest>(json, ManifestJson.Options);
        Assert.Equal("high", back!.Games[0].BanRisk);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter GameManifestJsonTests`
Expected: FAIL — `GameManifestEntry` has no `BanRisk` (compile error CS0117).

- [ ] **Step 3: Implement** — add the field to `GameManifestEntry` (after the `Featured` line, before `Provenance`):

```csharp
    public string? BanRisk { get; init; }             // null | "low" | "medium" | "high" — anti-cheat/ban exposure for online play (descriptive only)
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter GameManifestJsonTests`
Expected: PASS (both tests).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Manifest/GameManifest.cs tests/ModManager.Tests/Manifest/GameManifestJsonTests.cs
git commit -m "feat(manifest): descriptive banRisk field on GameManifestEntry

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: `GameBanRisk` enum + `BanRiskRules` (parse / max / gate decision)

The pure heart: the level enum and all the rules. No IO.

**Files:**
- Create: `src/ModManager.Core/GameBanRisk.cs`
- Test: `tests/ModManager.Tests/BanRiskRulesTests.cs`

- [ ] **Step 1: Write the failing tests** — create `tests/ModManager.Tests/BanRiskRulesTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class BanRiskRulesTests
{
    [Theory]
    [InlineData("high", GameBanRisk.High)]
    [InlineData("HIGH", GameBanRisk.High)]
    [InlineData("medium", GameBanRisk.Medium)]
    [InlineData("low", GameBanRisk.Low)]
    [InlineData(null, GameBanRisk.None)]
    [InlineData("", GameBanRisk.None)]
    [InlineData("garbage", GameBanRisk.None)]
    public void Parse_maps_strings_case_insensitively(string? s, GameBanRisk expected)
        => Assert.Equal(expected, BanRiskRules.Parse(s));

    [Fact]
    public void Canonical_round_trips_the_levels()
    {
        Assert.Equal("high", BanRiskRules.Canonical(GameBanRisk.High));
        Assert.Equal("medium", BanRiskRules.Canonical(GameBanRisk.Medium));
        Assert.Equal("low", BanRiskRules.Canonical(GameBanRisk.Low));
        Assert.Null(BanRiskRules.Canonical(GameBanRisk.None));
    }

    [Fact]
    public void MaxString_never_downgrades()
    {
        Assert.Equal("high", BanRiskRules.MaxString("high", null));   // remote null can't lower curated high
        Assert.Equal("high", BanRiskRules.MaxString("low", "high"));  // remote high raises
        Assert.Equal("high", BanRiskRules.MaxString("high", "low"));  // remote low can't lower
        Assert.Null(BanRiskRules.MaxString(null, null));
    }

    [Fact]
    public void ShouldGateEnable_only_gates_high_and_unacked()
    {
        Assert.True(BanRiskRules.ShouldGateEnable(GameBanRisk.High, alreadyAcked: false));
        Assert.False(BanRiskRules.ShouldGateEnable(GameBanRisk.High, alreadyAcked: true));
        Assert.False(BanRiskRules.ShouldGateEnable(GameBanRisk.Medium, alreadyAcked: false));
        Assert.False(BanRiskRules.ShouldGateEnable(GameBanRisk.None, alreadyAcked: false));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter BanRiskRulesTests`
Expected: FAIL — `GameBanRisk` / `BanRiskRules` do not exist.

- [ ] **Step 3: Implement** — create `src/ModManager.Core/GameBanRisk.cs`:

```csharp
namespace ModManager.Core;

/// <summary>A game's anti-cheat/ban exposure for online modding. Ordered so a numeric max works.</summary>
public enum GameBanRisk { None = 0, Low = 1, Medium = 2, High = 3 }

/// <summary>
/// Pure rules for the game-level ban-risk flag: parse the descriptive manifest string, the
/// never-downgrade merge, and the single enable-gate decision every enable path consults. No IO,
/// no UI. The manifest flag is descriptive ("this game is risky"); these rules are the compiled
/// policy that decides what to do about it.
/// </summary>
public static class BanRiskRules
{
    /// <summary>Map the manifest string to a level. null / unknown / garbage -> None, case-insensitive.</summary>
    public static GameBanRisk Parse(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "low" => GameBanRisk.Low,
        "medium" => GameBanRisk.Medium,
        "high" => GameBanRisk.High,
        _ => GameBanRisk.None,
    };

    /// <summary>The canonical manifest string for a level, or null for None.</summary>
    public static string? Canonical(GameBanRisk r) => r switch
    {
        GameBanRisk.Low => "low",
        GameBanRisk.Medium => "medium",
        GameBanRisk.High => "high",
        _ => null,
    };

    /// <summary>The higher of two levels.</summary>
    public static GameBanRisk Max(GameBanRisk a, GameBanRisk b) => (GameBanRisk)System.Math.Max((int)a, (int)b);

    /// <summary>Merge two manifest strings, NEVER downgrading: the higher level's canonical string wins.
    /// A safety field must not be silently lowered by a remote feed (mirrors the Provenance never-downgrade rule).</summary>
    public static string? MaxString(string? a, string? b) => Canonical(Max(Parse(a), Parse(b)));

    /// <summary>High risk gates an enable until the user has acknowledged it for this game.
    /// Medium/Low/None never gate (banner-only). Single source of truth for every enable path.</summary>
    public static bool ShouldGateEnable(GameBanRisk level, bool alreadyAcked)
        => level == GameBanRisk.High && !alreadyAcked;
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter BanRiskRulesTests`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/GameBanRisk.cs tests/ModManager.Tests/BanRiskRulesTests.cs
git commit -m "feat(manifest): GameBanRisk enum + BanRiskRules (parse, never-downgrade max, gate decision)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: `EffectiveManifest.MergeEntry` — never-downgrade BanRisk

**Files:**
- Modify: `src/ModManager.Core/Manifest/EffectiveManifest.cs:72-85` (the `embedded with { ... }` block)
- Test: `tests/ModManager.Tests/Manifest/EffectiveManifestTests.cs` (append; create if absent)

- [ ] **Step 1: Write the failing test** — append a test that a remote can raise but never lower the curated risk:

```csharp
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

public class BanRiskMergeTests
{
    private static GameManifest One(string id, string? banRisk) => new()
    {
        Games = new[] { new GameManifestEntry { Id = id, Name = id, BanRisk = banRisk } },
    };

    [Fact]
    public void Remote_null_cannot_blank_a_curated_high()
    {
        var merged = EffectiveManifest.Merge(One("g", "high"), One("g", null));
        Assert.Equal("high", merged.Games[0].BanRisk);
    }

    [Fact]
    public void Remote_can_raise_risk()
    {
        var merged = EffectiveManifest.Merge(One("g", "low"), One("g", "high"));
        Assert.Equal("high", merged.Games[0].BanRisk);
    }

    [Fact]
    public void Remote_lower_cannot_downgrade_curated_high()
    {
        var merged = EffectiveManifest.Merge(One("g", "high"), One("g", "low"));
        Assert.Equal("high", merged.Games[0].BanRisk);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter BanRiskMergeTests`
Expected: FAIL — `BanRisk` not carried through `MergeEntry`, so the merged value is null (the `embedded with` doesn't set it... actually `embedded with` keeps the embedded value, so `Remote_can_raise_risk` fails: stays "low").

- [ ] **Step 3: Implement** — add one line to the `embedded with { ... }` return in `MergeEntry` (after the `Featured` line at [EffectiveManifest.cs:83]):

```csharp
            BanRisk = BanRiskRules.MaxString(embedded.BanRisk, remote.BanRisk),
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter BanRiskMergeTests`
Expected: PASS (all three).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Manifest/EffectiveManifest.cs tests/ModManager.Tests/Manifest/EffectiveManifestTests.cs
git commit -m "feat(manifest): never-downgrade banRisk in EffectiveManifest merge

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: `BanRiskCatalog` facade — live resolution by Steam app id

**Files:**
- Create: `src/ModManager.Core/BanRiskCatalog.cs`
- Test: `tests/ModManager.Tests/BanRiskCatalogTests.cs`

- [ ] **Step 1: Write the failing test** — create `tests/ModManager.Tests/BanRiskCatalogTests.cs`. (Resolution is off `EffectiveManifest.Current`; with no remote set, that's the embedded snapshot. The test sets a remote to control the data, then clears it — mirroring how manifest facades are tested.)

```csharp
using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests;

[Collection("EffectiveManifest")] // serialize: these mutate the shared remote
public class BanRiskCatalogTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null);

    [Fact]
    public void ByAppId_resolves_a_flagged_game_and_defaults_None()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry { Id = "risky", Name = "Risky", Stores = new StoreIds { SteamAppId = "111" }, BanRisk = "high" },
                new GameManifestEntry { Id = "safe", Name = "Safe", Stores = new StoreIds { SteamAppId = "222" } },
            },
        });

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.ByAppId("111"));
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.ByAppId("222"));  // present, no flag
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.ByAppId("999"));  // absent
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.ByAppId(null));
    }
}
```

If no `[CollectionDefinition("EffectiveManifest")]` exists in the test project, create one in this file:

```csharp
[CollectionDefinition("EffectiveManifest")]
public class EffectiveManifestCollection { }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter BanRiskCatalogTests`
Expected: FAIL — `BanRiskCatalog` does not exist.

- [ ] **Step 3: Implement** — create `src/ModManager.Core/BanRiskCatalog.cs` (mirrors `NexusDomains`):

```csharp
using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// Live Steam-app-id -> ban-risk level map, a facade over <see cref="EffectiveManifest"/> (twin of
/// <see cref="NexusDomains"/>). Resolving live — not off a persisted GameEntry field — means a feed
/// update that raises a game's risk protects players who already added it, with no migration. An
/// unflagged or unknown app id resolves to <see cref="GameBanRisk.None"/>.
/// </summary>
public static class BanRiskCatalog
{
    private static IReadOnlyDictionary<string, GameBanRisk>? _map;
    private static int _mapGen = -1;
    private static readonly object _gate = new();

    private static IReadOnlyDictionary<string, GameBanRisk> Map
    {
        get
        {
            lock (_gate)
            {
                var gen = EffectiveManifest.Generation;
                if (_map is null || _mapGen != gen)
                {
                    _map = Build();
                    _mapGen = gen;
                }
                return _map;
            }
        }
    }

    private static IReadOnlyDictionary<string, GameBanRisk> Build()
    {
        var map = new Dictionary<string, GameBanRisk>();
        foreach (var g in EffectiveManifest.Current.Games)
        {
            var level = BanRiskRules.Parse(g.BanRisk);
            if (level != GameBanRisk.None && g.Stores.SteamAppId is { } appId)
                map[appId] = level;
        }
        return map;
    }

    /// <summary>The ban-risk level for a Steam app id, or None when unflagged / unknown / id is null.</summary>
    public static GameBanRisk ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Map.TryGetValue(steamAppId, out var r) ? r : GameBanRisk.None;
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter BanRiskCatalogTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/BanRiskCatalog.cs tests/ModManager.Tests/BanRiskCatalogTests.cs
git commit -m "feat(manifest): BanRiskCatalog live ByAppId facade over EffectiveManifest

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: `BanRiskAckStore` — per-game acknowledgment persistence

**Files:**
- Create: `src/ModManager.Core/BanRiskAckStore.cs`
- Test: `tests/ModManager.Tests/BanRiskAckStoreTests.cs`

- [ ] **Step 1: Write the failing tests** — create `tests/ModManager.Tests/BanRiskAckStoreTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

public class BanRiskAckStoreTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "banack-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    [Fact]
    public void Ack_then_IsAcked_round_trips()
    {
        Assert.False(BanRiskAckStore.IsAcked(_tmp, "marvel-rivals"));
        BanRiskAckStore.Ack(_tmp, "marvel-rivals");
        Assert.True(BanRiskAckStore.IsAcked(_tmp, "marvel-rivals"));
        Assert.False(BanRiskAckStore.IsAcked(_tmp, "other-game"));
    }

    [Fact]
    public void Missing_or_corrupt_file_is_empty_not_an_error()
    {
        Assert.Empty(BanRiskAckStore.Load(_tmp));                       // missing dir
        Directory.CreateDirectory(_tmp);
        File.WriteAllText(Path.Combine(_tmp, "ban-risk-acks.json"), "{ not valid json");
        Assert.Empty(BanRiskAckStore.Load(_tmp));                       // corrupt -> empty, no throw
    }

    [Fact]
    public void Ack_is_idempotent()
    {
        BanRiskAckStore.Ack(_tmp, "g");
        BanRiskAckStore.Ack(_tmp, "g");
        Assert.Single(BanRiskAckStore.Load(_tmp));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter BanRiskAckStoreTests`
Expected: FAIL — `BanRiskAckStore` does not exist.

- [ ] **Step 3: Implement** — create `src/ModManager.Core/BanRiskAckStore.cs` (mirrors `MpCompatStore`'s tolerant + atomic shape; the payload is a string array, so no camelCase key concern):

```csharp
using System.Text.Json;

namespace ModManager.Core;

/// <summary>
/// Persists the set of game ids for which the user has acknowledged the ban-risk warning, at
/// &lt;dataDir&gt;\ban-risk-acks.json (a plain JSON array of game ids). Once a game is acked, the
/// enable gate stops prompting for it (the persistent banner still shows). Tolerant by design: a
/// missing or corrupt file yields an empty set, never throws. Writes go through AtomicJson.
/// </summary>
public static class BanRiskAckStore
{
    private const string FileName = "ban-risk-acks.json";

    /// <summary>The acked game-id set. Missing or corrupt file -> empty.</summary>
    public static IReadOnlySet<string> Load(string dataDir)
    {
        var path = Path.Combine(dataDir, FileName);
        if (!File.Exists(path)) return new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var ids = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path));
            return ids is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(ids, StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    public static bool IsAcked(string dataDir, string gameId)
        => !string.IsNullOrEmpty(gameId) && Load(dataDir).Contains(gameId);

    /// <summary>Record an acknowledgment for a game and persist atomically. Idempotent.</summary>
    public static void Ack(string dataDir, string gameId)
    {
        if (string.IsNullOrEmpty(gameId)) return;
        var set = new HashSet<string>(Load(dataDir), StringComparer.Ordinal) { gameId };
        Directory.CreateDirectory(dataDir);
        AtomicJson.WriteJsonAtomic(Path.Combine(dataDir, FileName), set.OrderBy(x => x, StringComparer.Ordinal).ToList());
    }
}
```

> **Note for the implementer:** verify `AtomicJson.WriteJsonAtomic`'s exact signature in `src/ModManager.Core/AtomicJson.cs` and match it (the same call `MpCompatStore.SetOverride` uses). If the method name/shape differs, follow `MpCompatStore`'s exact call.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter BanRiskAckStoreTests`
Expected: PASS (all three).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/BanRiskAckStore.cs tests/ModManager.Tests/BanRiskAckStoreTests.cs
git commit -m "feat(manifest): BanRiskAckStore per-game acknowledgment persistence

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: App — enable gate (dialog + every enable path)

App-side, not unit-testable here — **read the actual files and mirror the cited patterns**, then build-verify. The Core decision (`BanRiskRules.ShouldGateEnable`) carries the logic; this is wiring.

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` — `ToggleAsync` (~:707-732), `ToggleVariantAsync` (~:736-755), and the bulk enable paths (`SetAllAsync` ~:817, `SetMode`/`ApplyMode` ~:832)
- Modify: `src/ModManager.App/MainWindow.xaml.cs` — add a ban-risk confirm dialog mirroring `ConfirmOwnedToggleAsync` (~:176-200)

- [ ] **Step 1: Read the integration points**

Read `MainViewModel.cs` around the toggle/bulk handlers and how it gets the data dir (the same one `MpCompatStore` is called with) + the active game (`_ctx.Game` with `.SteamAppId` and `.Id`). Read `MainWindow.xaml.cs:176-200` (`ConfirmOwnedToggleAsync`) for the ContentDialog + "Don't warn me again" checkbox + bool-return pattern, and how the VM invokes a dialog on the App (the existing handler wiring, e.g. an `Action`/event the VM raises that `MainWindow` services).

- [ ] **Step 2: Add the confirm dialog (App)** in `MainWindow.xaml.cs`, mirroring `ConfirmOwnedToggleAsync`:

A `ConfirmBanRiskEnableAsync(string gameName)` returning a small result `(bool proceed, bool dontWarnAgain)`. Copy the `ConfirmOwnedToggleAsync` ContentDialog structure exactly; change the copy to be **ban-specific and distinct from the co-op-desync wording**:
- Title: `Enable mods on {gameName}?`
- Body: `This game uses anti-cheat. Enabling mods for online play can get your account banned. Disabling is always reversible.`
- Checkbox: `Don't warn me again for this game`
- PrimaryButton: `Enable anyway`  · CloseButton: `Cancel`  · DefaultButton: Close (cancel is the safe default)

- [ ] **Step 3: Gate every enable path (VM)** — before a disabled→enabled transition, in each path:

```
// resolve once at the top of the enable path
var level = BanRiskCatalog.ByAppId(_ctx.Game.SteamAppId);
var acked = BanRiskAckStore.IsAcked(dataDir, _ctx.Game.Id);
if (BanRiskRules.ShouldGateEnable(level, acked))
{
    var (proceed, dontWarn) = await <confirm-ban-risk-dialog>(_ctx.Game.GameName);
    if (!proceed) { /* revert visual exactly like the catch-block: row.Enabled = !row.Enabled; */ return; }
    if (dontWarn) BanRiskAckStore.Ack(dataDir, _ctx.Game.Id);
}
// ...proceed with the existing enable call
```

- `ToggleAsync`: gate only when the toggle is turning a row **on** (enabling), never when disabling. On cancel, revert `row.Enabled` exactly like the existing catch-block revert (~:728).
- `ToggleVariantAsync`: same gate before the enable.
- Bulk (`SetAllAsync` with on=true, `ApplyMode`/`SetMode` when the mode enables mods): gate **once** before the bulk enable; on cancel, abort the bulk enable (enable nothing). Disabling/clearing is never gated.

- [ ] **Step 4: Build to verify it compiles**

Kill any running `ModManager.App`, then run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs src/ModManager.App/MainWindow.xaml.cs
git commit -m "feat(settings): ban-risk enable gate on every enable path (warn + acknowledge)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: App — persistent ban-risk banner

App-side — mirror the `MpWarning` banner, build-verify.

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` — add `BanRiskWarningVisibility` + `BanRiskWarningText` parallel to `MpWarning` (~:168-175)
- Modify: `src/ModManager.App/MainWindow.xaml` — add a banner parallel to the `MpWarning` banner (~:235-242)

- [ ] **Step 1: Read the pattern**

Read `MainViewModel.cs:168-175` (`MpWarningText`/visibility computation + the `OnPropertyChanged` notify list) and `MainWindow.xaml:235-242` (the `MpWarning` banner element + bindings + `ThemeDanger`/icon).

- [ ] **Step 2: Implement (VM)** — add computed properties recomputed when the active game changes:

```
public bool BanRiskWarningVisible => BanRiskCatalog.ByAppId(_ctx.Game?.SteamAppId) >= GameBanRisk.Medium;
public string BanRiskWarningText => "This game uses anti-cheat — enabling mods for online play can get your account banned.";
```

(`>= GameBanRisk.Medium` so `high` and `medium` show the banner; `low`/`None` don't — per the spec's level table. Notify these alongside the existing `MpWarning` notifications when the game/context changes.)

- [ ] **Step 3: Implement (XAML)** — add a banner element mirroring the `MpWarning` one, bound to `BanRiskWarningVisible`/`BanRiskWarningText`, `ThemeDanger` styling, **distinct copy** from the co-op-desync banner (they may both be visible; they must read as two different warnings). Place it adjacent to the `MpWarning` banner.

- [ ] **Step 4: Build to verify it compiles**

Kill any running `ModManager.App`, then run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs src/ModManager.App/MainWindow.xaml
git commit -m "feat(settings): persistent ban-risk banner (distinct from co-op-desync)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: The operating law + miner parity + smoke

**Files:**
- Modify: `README.md` (Operating laws block), `c:\Users\estev\Projects\626-mod-launcher\CLAUDE.md` (What NOT to do)
- Modify: `tools/ManifestMiner/OverrideEntry.cs`
- Modify: `docs/smoke-tests/pending.md`

- [ ] **Step 1: README operating law** — add a fifth rule to the README *Operating laws* block, in the existing voice (builder-to-builder, sentence case, em-dashes ok, no emoji):

> **Never auto-force mods onto a ban-risk game.** Detecting a game's engine and mod path is fine. Enabling a mod on an anti-cheat/ban-risk title (GameGuard, online EAC, BattlEye) warns you and waits for an explicit acknowledgment — the launcher never enables behind your back, and never refuses your call (disable is always one click away).

- [ ] **Step 2: CLAUDE.md** — add a matching bullet to the *What NOT to do* section:

> - **Don't auto-force mods onto a ban-risk game.** Detection is fine; the enable path on an anti-cheat/ban-risk title must warn and require an explicit acknowledgment (`BanRiskRules.ShouldGateEnable`), and never enable without it. Reversibility stands — warn-and-ack, never hard-block.

- [ ] **Step 3: Miner parity** — add to `tools/ManifestMiner/OverrideEntry.cs` (so hand-curated ban-risk in `overrides/` survives a mine):

```csharp
    public string? BanRisk { get; init; }
```

(And, if the miner maps `OverrideEntry` onto `GameManifestEntry`, thread `BanRisk` through that mapping the same way `NexusDomain`/`Featured` are. Verify by reading the miner's override-apply path.)

- [ ] **Step 4: Smoke checklist** — append to `docs/smoke-tests/pending.md`:

```markdown
## Ban-risk safety (2026-06-15)

- [ ] **Gate fires on a high-risk game.** Flag a local game `banRisk: "high"` (manual registry/manifest edit). Enabling a mod prompts the ban-risk acknowledgment naming the game. Cancel -> nothing enables, the row reverts. Enable anyway with "Don't warn me again for this game" -> enables, and the next enable does NOT re-prompt.
- [ ] **Banner persists.** The ban-risk banner shows on that game and stays visible after the ack; its copy is ban-specific, not the co-op-desync wording.
- [ ] **Medium = banner only.** A `banRisk: "medium"` game shows the banner but never prompts; a null/None game shows neither.
- [ ] **Bulk gated once.** "Enable all" / applying a profile that enables mods on the un-acked high game prompts once (no per-row bypass).
- [ ] **Disable is never gated.** Disabling a mod on a high-risk game is always immediate (reversibility — getting safer needs no friction).
```

- [ ] **Step 5: Commit**

```bash
git add README.md CLAUDE.md tools/ManifestMiner/OverrideEntry.cs docs/smoke-tests/pending.md
git commit -m "docs(settings): ban-risk operating law + miner parity + smoke checklist

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: Full verification

- [ ] **Step 1: Full Core suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — all green incl. `CorePurityTests` (new `GameBanRisk`/`BanRiskCatalog`/`BanRiskAckStore`/`BanRiskRules` are pure Core), the new ban-risk tests, and no regression in the manifest/merge suites.

- [ ] **Step 2: App build**

Kill any running `ModManager.App`, then run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit** (only if the verification surfaced fixes; otherwise nothing to commit)

---

## Self-review notes (author checklist, run)

- **Spec coverage:** manifest field = T1; enum/rules/gate decision = T2; never-downgrade merge = T3; live catalog facade = T4; ack store = T5; App gate+dialog = T6; banner = T7; operating law + miner + smoke = T8; verify = T9. All "Surfaces touched" rows covered.
- **Architecture refinement vs synthesis:** ban-risk resolves **live** via `BanRiskCatalog.ByAppId` (no persisted `GameEntry` field, no registry migration) — better for a safety field (a feed raising risk protects already-added games). Recorded in the spec.
- **Laws:** Core additions are pure (no WinUI/WinRT) — `CorePurityTests` gate. No `File.Delete`/move in any new path (the ack store only writes its own json via `AtomicJson`); the gate never auto-enables and reverts on cancel. camelCase: manifest field asserted `"banRisk"` not `"BanRisk"`; the ack file is a string array (no keys to case). Never-downgrade merge is the one deliberate deviation, explicitly tested.
- **Type consistency:** `GameBanRisk {None,Low,Medium,High}`, `BanRiskRules.{Parse,Canonical,Max,MaxString,ShouldGateEnable}`, `BanRiskCatalog.ByAppId`, `BanRiskAckStore.{Load,IsAcked,Ack}` — identical across tasks.
- **No placeholders:** Core tasks (1-5, 9) have complete code + exact commands. App tasks (6-7) are build+smoke with precise integration anchors + the exact patterns to mirror, since the test project can't reference `ModManager.App`.
