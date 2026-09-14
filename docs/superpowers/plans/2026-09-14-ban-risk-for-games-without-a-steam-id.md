# Ban risk for games without a Steam id — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ban risk resolves from the whole game instead of its Steam id, and writing a changed save on a high-risk game asks first.

**Architecture:** `BanRiskCatalog.Effective(GameEntry)` takes the highest of three sources: the feed by Steam app id, the feed by manifest id, and a short compiled floor. Every caller switches to it, and a source-scan test stops the Steam-id-only call from coming back. The save-write gate is a pure Core rule plus its own acknowledgment file. The drop path stays testable through a `writeAllowed` flag, and the one dialog lives in a shared App helper.

**Tech Stack:** .NET 10, C#, xUnit, WinUI 3.

**Spec:** `docs/superpowers/specs/2026-09-13-ban-risk-for-games-without-a-steam-id-design.md` (approved 2026-09-14). Read it before starting any task.

## Global Constraints

- Every Core behaviour change starts with a failing xUnit test in `tests/ModManager.Tests/`.
- Never run bare `dotnet test` or `dotnet build` at the repo root. Use `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` and `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Version=0.22.0`.
- Close the running app before building the App project.
- Warnings are errors. Nullable is on.
- No WinUI or WinRT types in `src/ModManager.Core/` (`CorePurityTests`).
- Compiled floor, exactly: `ea-sports-college-football-27` / Steam `4032350` → High; `madden-nfl-27` / Steam `3940610` → High.
- Save acknowledgment file: `ban-risk-save-acks.json`. The enable acknowledgment file stays `ban-risk-acks.json`, unchanged.
- Gated save writes: edit character, save-mod drop install. **Never gated:** reset a save mod, remove a save mod, any snapshot restore, profile restore.
- The prompt asks on every gated write until *Don't ask again for this game's saves* is ticked, and the tick covers that one game only.
- Prompt copy, verbatim:
  - Title: `Write to a save on {game}?`
  - Body: `This game uses anti-cheat, and its publisher's rules can treat a modified save as a reason to ban an account. The launcher snapshots the save first, so you can put it back. It can't tell you whether an edited save is safe to take online, and it won't guess.`
  - Second line: `If your saves sync to the cloud, the edited one syncs too.`
  - Checkbox: `Don't ask again for this game's saves`
  - Primary button: `Write the save`. Close button: `Cancel`. Default button: Close.
- Automation ids on the prompt: checkbox `SaveWriteRiskDontAsk`, body text `SaveWriteRiskBody`.
- No copy may call a save write safe.
- Conventional commits ending with `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.

---

### Task 1: `BanRiskCatalog.Effective` with an id map and a compiled floor

**Files:**
- Modify: `src/ModManager.Core/BanRiskCatalog.cs`
- Test: `tests/ModManager.Tests/BanRiskCatalogTests.cs`

**Interfaces:**
- Consumes: `BanRiskRules.Max(GameBanRisk, GameBanRisk)`, `BanRiskRules.Parse(string?)`, `EffectiveManifest.Current`, `EffectiveManifest.Generation`, `EffectiveManifest.SetRemote(GameManifest?)`.
- Produces: `public static GameBanRisk BanRiskCatalog.Effective(GameEntry game)`. `ByAppId(string?)` stays public and keeps its behaviour.

- [ ] **Step 1: Write the failing tests.** Append these to the existing `BanRiskCatalogTests` class. It is already in `[Collection("ManifestState")]` and resets the remote in `Dispose`.

```csharp
    private static GameManifestEntry Entry(string id, string? steam, string? risk)
        => new() { Id = id, Name = id, Stores = new StoreIds { SteamAppId = steam }, BanRisk = risk };

    private static GameEntry Game(string id, string? steam)
        => new() { Id = id, GameName = id, SteamAppId = steam };

    [Fact]
    public void Effective_finds_a_flagged_game_that_has_no_Steam_id()
    {
        // The defect: a game registered from the EA app carries no Steam id, and ByAppId-only
        // resolution read None for it.
        EffectiveManifest.SetRemote(new GameManifest { Games = new[] { Entry("some-ea-game", "555", "high") } });

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(Game("some-ea-game", null)));
    }

    [Fact]
    public void Effective_matches_ByAppId_for_Steam_games()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[] { Entry("risky", "111", "high"), Entry("mid", "333", "medium"), Entry("safe", "222", null) },
        });

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(Game("my-own-id", "111")));
        Assert.Equal(GameBanRisk.Medium, BanRiskCatalog.Effective(Game("my-own-id", "333")));
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.Effective(Game("my-own-id", "222")));
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.Effective(Game("my-own-id", "999")));
    }

    [Fact]
    public void Effective_takes_the_highest_source_in_both_directions()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[] { Entry("by-steam-medium", "700", "medium"), Entry("by-id-high", "701", "high"),
                            Entry("by-steam-high", "702", "high"), Entry("by-id-low", "703", "low") },
        });

        // Steam id says medium, id says high.
        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(Game("by-id-high", "700")));
        // Steam id says high, id says low.
        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(Game("by-id-low", "702")));
    }

    [Theory]
    [InlineData("ea-sports-college-football-27", null)]
    [InlineData("madden-nfl-27", null)]
    [InlineData("some-legacy-id", "4032350")]
    [InlineData("some-legacy-id", "3940610")]
    public void The_compiled_floor_holds_with_no_remote_feed(string id, string? steam)
    {
        EffectiveManifest.SetRemote(null);

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(Game(id, steam)));
    }

    [Fact]
    public void A_feed_cannot_lower_the_compiled_floor()
    {
        EffectiveManifest.SetRemote(new GameManifest { Games = new[] { Entry("madden-nfl-27", "3940610", "low") } });

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(Game("madden-nfl-27", "3940610")));
    }

    [Fact]
    public void Id_matching_ignores_case_and_does_not_match_a_prefix()
    {
        EffectiveManifest.SetRemote(new GameManifest { Games = new[] { Entry("Risky-Game", null, "high") } });

        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(Game("risky-game", null)));
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.Effective(Game("risky-game-2", null)));
        EffectiveManifest.SetRemote(null);
        Assert.Equal(GameBanRisk.None, BanRiskCatalog.Effective(Game("madden-nfl-27-2", null)));
    }

    [Fact]
    public void Effective_sees_a_feed_change_after_a_first_resolve()
    {
        EffectiveManifest.SetRemote(new GameManifest { Games = new[] { Entry("changing", null, "high") } });
        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(Game("changing", null)));

        EffectiveManifest.SetRemote(new GameManifest { Games = new[] { Entry("changing", null, "medium") } });
        Assert.Equal(GameBanRisk.Medium, BanRiskCatalog.Effective(Game("changing", null)));
    }
```

- [ ] **Step 2: Run them and watch them fail.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~BanRiskCatalogTests"`
Expected: a build error. `BanRiskCatalog` does not contain a definition for `Effective`.

- [ ] **Step 3: Implement.** Replace the body of `src/ModManager.Core/BanRiskCatalog.cs` from the class declaration down with this. Keep the `using` and the namespace, and extend the class summary to say that it resolves by id too:

```csharp
public static class BanRiskCatalog
{
    private sealed record Maps(IReadOnlyDictionary<string, GameBanRisk> ByAppId,
                               IReadOnlyDictionary<string, GameBanRisk> ById);

    private static Maps? _maps;
    private static int _mapGen = -1;
    private static readonly object _gate = new();

    // The launcher's own write features ship with their protection. Keyed by manifest id AND Steam
    // app id, so a Steam copy registered under an older id still hits. Only games the launcher itself
    // writes files for belong here; every other game's risk stays data in the feed. Raise-only: a feed
    // can never lower these, and removing one is a code change and a release.
    private static readonly IReadOnlyDictionary<string, GameBanRisk> FloorById =
        new Dictionary<string, GameBanRisk>(StringComparer.OrdinalIgnoreCase)
        {
            ["ea-sports-college-football-27"] = GameBanRisk.High,
            ["madden-nfl-27"] = GameBanRisk.High,
        };

    private static readonly IReadOnlyDictionary<string, GameBanRisk> FloorByAppId =
        new Dictionary<string, GameBanRisk>(StringComparer.Ordinal)
        {
            ["4032350"] = GameBanRisk.High, // EA Sports College Football 27
            ["3940610"] = GameBanRisk.High, // Madden NFL 27
        };

    private static Maps Current
    {
        get
        {
            lock (_gate)
            {
                var gen = EffectiveManifest.Generation;
                if (_maps is null || _mapGen != gen)
                {
                    _maps = Build();
                    _mapGen = gen;
                }
                return _maps;
            }
        }
    }

    private static Maps Build()
    {
        var byAppId = new Dictionary<string, GameBanRisk>(StringComparer.Ordinal);
        var byId = new Dictionary<string, GameBanRisk>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in EffectiveManifest.Current.Games)
        {
            var level = BanRiskRules.Parse(g.BanRisk);
            if (level == GameBanRisk.None) continue;
            if (g.Stores.SteamAppId is { } appId) byAppId[appId] = level;
            if (!string.IsNullOrEmpty(g.Id)) byId[g.Id] = level;
        }
        return new Maps(byAppId, byId);
    }

    /// <summary>The ban-risk level for a Steam app id, or None when unflagged / unknown / id is null.
    /// Callers outside Core use <see cref="Effective"/>; a source-scan test enforces it.</summary>
    public static GameBanRisk ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Current.ByAppId.TryGetValue(steamAppId, out var r) ? r : GameBanRisk.None;

    /// <summary>The ban risk the launcher acts on for this game: the highest of what the feed says by
    /// Steam app id, what it says by manifest id, and the compiled floor. Highest wins, so no single
    /// source can lower another. A game with no Steam id (EA app, Xbox, a folder) still resolves.</summary>
    public static GameBanRisk Effective(GameEntry game)
    {
        var level = ByAppId(game.SteamAppId);
        if (!string.IsNullOrEmpty(game.Id))
        {
            if (Current.ById.TryGetValue(game.Id, out var byId)) level = BanRiskRules.Max(level, byId);
            if (FloorById.TryGetValue(game.Id, out var floorId)) level = BanRiskRules.Max(level, floorId);
        }
        if (!string.IsNullOrEmpty(game.SteamAppId) && FloorByAppId.TryGetValue(game.SteamAppId, out var floorApp))
            level = BanRiskRules.Max(level, floorApp);
        return level;
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~BanRiskCatalogTests"`
Expected: PASS, all 8 tests plus the 4 theory rows.

- [ ] **Step 5: Run the whole suite.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS, with nothing newly failing.

- [ ] **Step 6: Commit.**

```bash
git add src/ModManager.Core/BanRiskCatalog.cs tests/ModManager.Tests/BanRiskCatalogTests.cs
git commit -m "fix(ban-risk): resolve risk from the whole game, not only its Steam id

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Every caller goes through `Effective`, and a guard keeps it that way

**Files:**
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (lines ~406, ~486, ~1309)
- Modify: `src/ModManager.App/ViewModels/LibraryViewModel.cs` (line ~465)
- Modify: `src/ModManager.Mcp/Tools/WriteTools.cs` (line ~64)
- Create: `tests/ModManager.Tests/BanRiskCallSiteTests.cs`

**Interfaces:**
- Consumes: `BanRiskCatalog.Effective(GameEntry)` from Task 1.
- Produces: nothing new.

- [ ] **Step 1: Write the failing guard test.**

```csharp
namespace ModManager.Tests;

/// <summary>
/// Every surface outside Core asks <c>BanRiskCatalog.Effective(game)</c>. <c>ByAppId</c> alone reads
/// None for any game without a Steam id (EA app, Xbox, a folder), which switched the ban-risk law off
/// for exactly those games. The next new surface copies whichever call it finds first, so this test
/// makes sure the only call it can find is the right one.
/// </summary>
public class BanRiskCallSiteTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "ModManager.App")))
            dir = dir.Parent!;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Theory]
    [InlineData("ModManager.App")]
    [InlineData("ModManager.Mcp")]
    public void No_surface_outside_Core_resolves_ban_risk_by_Steam_id_alone(string project)
    {
        var root = Path.Combine(RepoRoot(), "src", project);
        var sep = Path.DirectorySeparatorChar;
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .Where(f => File.ReadAllText(f).Contains("BanRiskCatalog.ByAppId("))
            .Select(f => Path.GetRelativePath(RepoRoot(), f))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Use BanRiskCatalog.Effective(game) instead of ByAppId in: " + string.Join(", ", offenders));
    }
}
```

- [ ] **Step 2: Run it and watch it fail.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~BanRiskCallSiteTests"`
Expected: FAIL. The App row names `MainViewModel.cs` and `LibraryViewModel.cs`, and the Mcp row names `WriteTools.cs`.

- [ ] **Step 3: Switch the call sites.**

`MainViewModel.cs`, the warning (~406):

```csharp
    public Visibility BanRiskWarningVisibility =>
        _ctx is not null && BanRiskCatalog.Effective(_ctx.Game) >= GameBanRisk.Medium ? Visibility.Visible : Visibility.Collapsed;
```

`MainViewModel.cs`, the state chip (~486):

```csharp
        BanRisk = _ctx is not null && BanRiskCatalog.Effective(_ctx.Game) >= GameBanRisk.Medium,
```

`MainViewModel.cs`, `GateBanRiskEnableAsync` (~1309). Also change its summary's "LIVE by Steam app id" to "LIVE from the whole game (Steam id, manifest id, compiled floor)":

```csharp
        var level = BanRiskCatalog.Effective(_ctx.Game);
```

`LibraryViewModel.cs`, `BanRiskFor` (~465):

```csharp
        var risk = BanRiskCatalog.Effective(g);
```

`WriteTools.cs` (~64):

```csharp
                BanRiskCatalog.Effective(game),
```

- [ ] **Step 4: Run the guard and the whole suite.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS, guard included. The test project builds `ModManager.Mcp`, so `WriteTools.cs` compiles as part of this run.

- [ ] **Step 5: Build the App.**

Close the app if it is running. Then run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Version=0.22.0`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Commit.**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs src/ModManager.App/ViewModels/LibraryViewModel.cs src/ModManager.Mcp/Tools/WriteTools.cs tests/ModManager.Tests/BanRiskCallSiteTests.cs
git commit -m "fix(ban-risk): route every chip, gate and agent check through Effective

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: The save-write rule and its own acknowledgment

**Files:**
- Modify: `src/ModManager.Core/GameBanRisk.cs`
- Modify: `src/ModManager.Core/BanRiskAckStore.cs`
- Test: `tests/ModManager.Tests/BanRiskRulesTests.cs`, `tests/ModManager.Tests/BanRiskAckStoreTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `public enum BanRiskAck { EnableMods, WriteSaves }` (in `GameBanRisk.cs`)
  - `public static bool BanRiskRules.ShouldGateSaveWrite(GameBanRisk level, bool saveWritesAcked)`
  - `BanRiskAckStore.Load(string dataDir, BanRiskAck kind = BanRiskAck.EnableMods)`
  - `BanRiskAckStore.IsAcked(string dataDir, string gameId, BanRiskAck kind = BanRiskAck.EnableMods)`
  - `BanRiskAckStore.Ack(string dataDir, string gameId, BanRiskAck kind = BanRiskAck.EnableMods)`

- [ ] **Step 1: Write the failing tests.** Append to `BanRiskRulesTests`:

```csharp
    [Theory]
    [InlineData(GameBanRisk.High, false, true)]
    [InlineData(GameBanRisk.High, true, false)]
    [InlineData(GameBanRisk.Medium, false, false)]
    [InlineData(GameBanRisk.Low, false, false)]
    [InlineData(GameBanRisk.None, false, false)]
    public void ShouldGateSaveWrite_gates_only_high_and_unacked(GameBanRisk level, bool acked, bool expected)
        => Assert.Equal(expected, BanRiskRules.ShouldGateSaveWrite(level, acked));

    [Fact]
    public void ShouldGateSaveWrite_asks_every_time_until_acked()
    {
        // No hidden "already shown" state: the prompt comes back on every write until the box is ticked.
        Assert.True(BanRiskRules.ShouldGateSaveWrite(GameBanRisk.High, saveWritesAcked: false));
        Assert.True(BanRiskRules.ShouldGateSaveWrite(GameBanRisk.High, saveWritesAcked: false));
    }
```

Append to `BanRiskAckStoreTests`:

```csharp
    [Fact]
    public void Ack_kinds_are_independent()
    {
        BanRiskAckStore.Ack(_tmp, "elden-ring");                                   // EnableMods by default
        Assert.True(BanRiskAckStore.IsAcked(_tmp, "elden-ring", BanRiskAck.EnableMods));
        Assert.False(BanRiskAckStore.IsAcked(_tmp, "elden-ring", BanRiskAck.WriteSaves));

        BanRiskAckStore.Ack(_tmp, "madden-nfl-27", BanRiskAck.WriteSaves);
        Assert.True(BanRiskAckStore.IsAcked(_tmp, "madden-nfl-27", BanRiskAck.WriteSaves));
        Assert.False(BanRiskAckStore.IsAcked(_tmp, "madden-nfl-27", BanRiskAck.EnableMods));
    }

    [Fact]
    public void Each_kind_writes_its_own_file_and_the_enable_file_keeps_its_name()
    {
        BanRiskAckStore.Ack(_tmp, "a", BanRiskAck.EnableMods);
        BanRiskAckStore.Ack(_tmp, "b", BanRiskAck.WriteSaves);

        Assert.True(File.Exists(Path.Combine(_tmp, "ban-risk-acks.json")));
        Assert.True(File.Exists(Path.Combine(_tmp, "ban-risk-save-acks.json")));
        Assert.DoesNotContain("\"b\"", File.ReadAllText(Path.Combine(_tmp, "ban-risk-acks.json")));
        Assert.DoesNotContain("\"a\"", File.ReadAllText(Path.Combine(_tmp, "ban-risk-save-acks.json")));
    }

    [Fact]
    public void An_enable_ack_file_from_before_the_change_is_still_honoured()
    {
        Directory.CreateDirectory(_tmp);
        File.WriteAllText(Path.Combine(_tmp, "ban-risk-acks.json"), "[\"monster-hunter-wilds\"]");

        Assert.True(BanRiskAckStore.IsAcked(_tmp, "monster-hunter-wilds"));
        Assert.False(BanRiskAckStore.IsAcked(_tmp, "monster-hunter-wilds", BanRiskAck.WriteSaves));
    }
```

- [ ] **Step 2: Run them and watch them fail.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~BanRiskRulesTests|FullyQualifiedName~BanRiskAckStoreTests"`
Expected: a build error. `ShouldGateSaveWrite` and `BanRiskAck` do not exist.

- [ ] **Step 3: Implement.** In `GameBanRisk.cs`, add below the `GameBanRisk` enum:

```csharp
/// <summary>What a ban-risk acknowledgment covers. Separate records, because agreeing to enable mods
/// on a game is not agreeing to have its saves edited.</summary>
public enum BanRiskAck { EnableMods, WriteSaves }
```

and add this inside `BanRiskRules`, after `ShouldGateEnable`:

```csharp
    /// <summary>A write that puts something new into a save on a high-risk game asks every time, until
    /// the user ticks "don't ask again" for this game's saves. Fixing a save or going back to what you had
    /// is never gated, so callers only consult this for new changes. Separate from ShouldGateEnable so the
    /// two can diverge without a rename.</summary>
    public static bool ShouldGateSaveWrite(GameBanRisk level, bool saveWritesAcked)
        => level == GameBanRisk.High && !saveWritesAcked;
```

In `BanRiskAckStore.cs`, replace the `FileName` constant and the three methods with the following. Also update the class summary so it names both files:

```csharp
    private static string FileName(BanRiskAck kind) => kind switch
    {
        BanRiskAck.WriteSaves => "ban-risk-save-acks.json",
        _ => "ban-risk-acks.json", // EnableMods: the original file, unchanged so existing acks keep working
    };

    /// <summary>The acked game-id set for one kind. Missing or corrupt file -> empty.</summary>
    public static IReadOnlySet<string> Load(string dataDir, BanRiskAck kind = BanRiskAck.EnableMods)
    {
        var path = Path.Combine(dataDir, FileName(kind));
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

    public static bool IsAcked(string dataDir, string gameId, BanRiskAck kind = BanRiskAck.EnableMods)
        => !string.IsNullOrEmpty(gameId) && Load(dataDir, kind).Contains(gameId);

    /// <summary>Record an acknowledgment of one kind for a game and persist atomically. Idempotent.</summary>
    public static void Ack(string dataDir, string gameId, BanRiskAck kind = BanRiskAck.EnableMods)
    {
        if (string.IsNullOrEmpty(gameId)) return;
        var set = new HashSet<string>(Load(dataDir, kind), StringComparer.Ordinal) { gameId };
        Directory.CreateDirectory(dataDir);
        AtomicJson.WriteJsonAtomic(Path.Combine(dataDir, FileName(kind)), set.OrderBy(x => x, StringComparer.Ordinal).ToList());
    }
```

- [ ] **Step 4: Run the tests and watch them pass.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit.**

```bash
git add src/ModManager.Core/GameBanRisk.cs src/ModManager.Core/BanRiskAckStore.cs tests/ModManager.Tests/BanRiskRulesTests.cs tests/ModManager.Tests/BanRiskAckStoreTests.cs
git commit -m "feat(ban-risk): a save-write rule with its own acknowledgment

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: The save-mod drop path can refuse to write without asking a dialog

**Files:**
- Modify: `src/ModManager.Core/SaveModFlow.cs`
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (the `SaveModFlow.TryHandleDrops` call, ~3771), passing `writeAllowed: true` only so the build compiles. Task 5 replaces it.
- Test: `tests/ModManager.Tests/SaveModFlowTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `SaveModDropOutcome.NeedsAcknowledgment` (new enum member, appended last)
  - `SaveModFlow.TryHandleDrops(IEnumerable<string> paths, IReadOnlyList<string> saveTypeExtensions, string saveProfilesDir, string snapshotsDir, string dataDir, string? saveModPath, IReadOnlyList<string>? forbidden, bool writeAllowed)`. `writeAllowed` is **required**, with no default, so a new caller cannot forget it.

- [ ] **Step 1: Write the failing tests.** In `SaveModFlowTests.cs`, add `writeAllowed: true` to all four existing `TryHandleDrops` calls, then append:

```csharp
    [Fact]
    public void A_world_zip_with_writes_not_allowed_needs_acknowledgment_and_writes_nothing()
    {
        var guid = "0123456789abcdef0123456789abcdef";
        var zip = MakeZip("world.zip", new[] { ($"{guid}/data.json", "{}") });
        var profiles = NewDir("saves");
        var oneProfile = Path.Combine(profiles, "user1");
        Directory.CreateDirectory(Path.Combine(oneProfile, "RocksDB", "1.0"));
        var snaps = NewDir("snaps");
        var data = NewDir("data");
        var before = Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).OrderBy(x => x).ToList();

        var verdicts = SaveModFlow.TryHandleDrops(
            new[] { zip }, saveTypeExtensions: Array.Empty<string>(),
            saveProfilesDir: profiles, snapshotsDir: snaps,
            dataDir: data, saveModPath: null, forbidden: null, writeAllowed: false);

        Assert.Single(verdicts);
        Assert.Equal(SaveModDropOutcome.NeedsAcknowledgment, verdicts[0].Outcome);
        Assert.Equal(guid, verdicts[0].WorldGuid);
        var after = Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories).OrderBy(x => x).ToList();
        Assert.Equal(before, after);                 // no world, no snapshot, no store entry
        Assert.Empty(SaveModStore.Load(data));
    }

    [Fact]
    public void A_content_zip_is_unaffected_by_writeAllowed()
    {
        var zip = MakeZip("content.zip", new[] { ("AwesomeMod_P.pak", "x") });
        var verdicts = SaveModFlow.TryHandleDrops(
            new[] { zip }, saveTypeExtensions: Array.Empty<string>(),
            saveProfilesDir: NewDir("saves"), snapshotsDir: NewDir("snaps"),
            dataDir: NewDir("data"), saveModPath: null, forbidden: null, writeAllowed: false);
        Assert.Equal(SaveModDropOutcome.NotASaveMod, verdicts[0].Outcome);
    }
```

- [ ] **Step 2: Run them and watch them fail.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SaveModFlowTests"`
Expected: a build error. There is no parameter named `writeAllowed`, and no `NeedsAcknowledgment`.

- [ ] **Step 3: Implement.** In `SaveModFlow.cs`:

```csharp
/// <summary>Outcome of a single archive drop through the save-mod fast-path. NeedsAcknowledgment:
/// it is a save mod for a game whose save writes are gated, and nothing was written.</summary>
public enum SaveModDropOutcome { Installed, NotASaveMod, Failed, NeedsAcknowledgment }
```

Add `bool writeAllowed` as the last parameter of `TryHandleDrops` and of the private `Handle`, and pass it through. Document it in `TryHandleDrops`'s summary: *"writeAllowed false: a detected save mod returns NeedsAcknowledgment and nothing is written. The caller decides it from BanRiskRules.ShouldGateSaveWrite, asks, and re-runs only those paths."* In `Handle`, insert this directly after the missing-world-GUID `Failed` return and before the `try { SaveModInstaller.InstallWorld(`:

```csharp
        if (!writeAllowed)
            return new SaveModDropVerdict(path, SaveModDropOutcome.NeedsAcknowledgment, verdict.WorldGuid, null);
```

In `MainViewModel.cs`, add `writeAllowed: true` to the existing `SaveModFlow.TryHandleDrops(` call after `forbidden: _ctx.Game.SaveModForbidden`, so behaviour is unchanged until Task 5.

- [ ] **Step 4: Run the suite and build the App.**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS.
Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Version=0.22.0`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 5: Commit.**

```bash
git add src/ModManager.Core/SaveModFlow.cs src/ModManager.App/ViewModels/MainViewModel.cs tests/ModManager.Tests/SaveModFlowTests.cs
git commit -m "feat(save-mods): a drop can stop short of writing and say it needs acknowledgment

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: The prompt, on the character editor and the save-mod drop

**Files:**
- Create: `src/ModManager.App/Services/SaveWriteRiskPrompt.cs`
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (new delegate beside `ConfirmBanRiskEnable` ~96, new `GateSaveWriteAsync` beside `GateBanRiskEnableAsync` ~1306, the save-mod pre-check ~3765-3785)
- Modify: `src/ModManager.App/MainWindow.xaml.cs` (wire the delegate beside `ViewModel.ConfirmBanRiskEnable = ...` ~85)
- Modify: `src/ModManager.App/SavesDialog.xaml.cs` (`OnEditCharacter` ~387)

App-layer only, which is headless-untestable. The policy it consults (`ShouldGateSaveWrite`, `Effective`, `BanRiskAck.WriteSaves`) is covered in Core by Tasks 1-4. Verification is the build plus the smoke in Task 6.

**Interfaces:**
- Consumes: `BanRiskCatalog.Effective(GameEntry)`, `BanRiskRules.ShouldGateSaveWrite(GameBanRisk, bool)`, `BanRiskAckStore.IsAcked/Ack(..., BanRiskAck.WriteSaves)`, `SaveModDropOutcome.NeedsAcknowledgment`, `TryHandleDrops(..., bool writeAllowed)`.
- Produces: `static Task<(bool proceed, bool dontAskAgain)> SaveWriteRiskPrompt.ShowAsync(XamlRoot root, string gameName)`, `MainViewModel.ConfirmSaveWrite` (`Func<string, Task<(bool proceed, bool dontAskAgain)>>?`).

- [ ] **Step 1: Create the shared prompt.** `src/ModManager.App/Services/SaveWriteRiskPrompt.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ModManager.App.Services;

/// <summary>
/// The one dialog for writing something new into a save on a high-risk game. The copy lives here and
/// nowhere else, so the character editor and the save-mod drop cannot drift apart. It only asks: the
/// caller decides whether to (BanRiskRules.ShouldGateSaveWrite) and records the tick
/// (BanRiskAckStore, BanRiskAck.WriteSaves). Not danger-filled: this is an informed choice about the
/// user's own file, and the snapshot is taken either way. It never calls a write safe.
/// </summary>
public static class SaveWriteRiskPrompt
{
    public static async Task<(bool proceed, bool dontAskAgain)> ShowAsync(XamlRoot root, string gameName)
    {
        var body = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "This game uses anti-cheat, and its publisher's rules can treat a modified save as a reason "
                 + "to ban an account. The launcher snapshots the save first, so you can put it back. It can't "
                 + "tell you whether an edited save is safe to take online, and it won't guess.",
        };
        AutomationProperties.SetAutomationId(body, "SaveWriteRiskBody");

        var cloud = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "If your saves sync to the cloud, the edited one syncs too.",
        };

        var dontAsk = new CheckBox { Content = "Don't ask again for this game's saves", Margin = new Thickness(0, 12, 0, 0) };
        AutomationProperties.SetAutomationId(dontAsk, "SaveWriteRiskDontAsk");

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(body);
        panel.Children.Add(cloud);
        panel.Children.Add(dontAsk);

        var dialog = new ContentDialog
        {
            Title = $"Write to a save on {gameName}?",
            Content = panel,
            PrimaryButtonText = "Write the save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close, // cancel is the safe default
            XamlRoot = root,
        };
        DialogTheming.Apply(dialog);
        var proceed = await dialog.ShowAsync() == ContentDialogResult.Primary;
        return (proceed, proceed && dontAsk.IsChecked == true);
    }
}
```

- [ ] **Step 2: Add the view-model delegate and gate.** In `MainViewModel.cs`, directly after the `ConfirmBanRiskEnable` property:

```csharp
    /// <summary>
    /// Shows the save-write prompt for a high-risk game and returns (proceed, dontAskAgain). The view wires
    /// it (dialog + XamlRoot live in code-behind). Unlike the enable gate, an UNWIRED delegate refuses:
    /// writing a changed save without the prompt is the one thing this gate exists to prevent.
    /// </summary>
    public Func<string, Task<(bool proceed, bool dontAskAgain)>>? ConfirmSaveWrite { get; set; }
```

Directly after `GateBanRiskEnableAsync`:

```csharp
    /// <summary>The save-write gate for new changes to a save (the save-mod drop). Asks every time on a
    /// high-risk game until the user ticks "don't ask again" for this game's saves. Fixes and undo never
    /// come through here. Returns true to write.</summary>
    private async Task<bool> GateSaveWriteAsync()
    {
        if (_ctx is null) return false;
        var level = BanRiskCatalog.Effective(_ctx.Game);
        var acked = BanRiskAckStore.IsAcked(_ctx.DataDir, _ctx.Game.Id, BanRiskAck.WriteSaves);
        if (!BanRiskRules.ShouldGateSaveWrite(level, acked)) return true;
        if (ConfirmSaveWrite is null) return false; // unwired -> nothing is written

        var (proceed, dontAsk) = await ConfirmSaveWrite(_ctx.Game.GameName);
        if (!proceed) return false;
        if (dontAsk) BanRiskAckStore.Ack(_ctx.DataDir, _ctx.Game.Id, BanRiskAck.WriteSaves);
        return true;
    }
```

- [ ] **Step 3: Gate the save-mod pre-check.** In the drop handler's `if (!string.IsNullOrEmpty(_ctx.SaveDir))` block (~3767), replace the single `TryHandleDrops` call and its `foreach` with the code below. The prompt is shown at most once per drop, and only when a drop actually is a save mod:

```csharp
                var saveRisk = BanRiskCatalog.Effective(_ctx.Game);
                var saveWritesAcked = BanRiskAckStore.IsAcked(_ctx.DataDir, _ctx.Game.Id, BanRiskAck.WriteSaves);
                IReadOnlyList<SaveModDropVerdict> verdicts = SaveModFlow.TryHandleDrops(
                    remaining, saveTypeExts,
                    saveProfilesDir: _ctx.SaveDir!,
                    snapshotsDir: _ctx.SavesDir,
                    dataDir: _ctx.DataDir,
                    saveModPath: _ctx.Game.SaveModPath,
                    forbidden: _ctx.Game.SaveModForbidden,
                    writeAllowed: !BanRiskRules.ShouldGateSaveWrite(saveRisk, saveWritesAcked));

                var needAck = verdicts.Where(v => v.Outcome == SaveModDropOutcome.NeedsAcknowledgment).ToList();
                if (needAck.Count > 0)
                {
                    if (await GateSaveWriteAsync())
                    {
                        var rerun = SaveModFlow.TryHandleDrops(
                            needAck.Select(v => v.SourcePath), saveTypeExts,
                            saveProfilesDir: _ctx.SaveDir!,
                            snapshotsDir: _ctx.SavesDir,
                            dataDir: _ctx.DataDir,
                            saveModPath: _ctx.Game.SaveModPath,
                            forbidden: _ctx.Game.SaveModForbidden,
                            writeAllowed: true);
                        verdicts = verdicts.Where(v => v.Outcome != SaveModDropOutcome.NeedsAcknowledgment).Concat(rerun).ToList();
                    }
                    else
                    {
                        // Still carved out of `remaining`: they ARE save mods, and regular intake must not try
                        // to classify their contents.
                        foreach (var v in needAck)
                        {
                            saveSkipReasons.Add($"{Path.GetFileName(v.SourcePath)}: not installed, nothing was written");
                            remaining.Remove(v.SourcePath);
                        }
                        verdicts = verdicts.Where(v => v.Outcome != SaveModDropOutcome.NeedsAcknowledgment).ToList();
                    }
                }

                foreach (var v in verdicts)
                {
                    if (v.Outcome == SaveModDropOutcome.Installed) { savedCount++; remaining.Remove(v.SourcePath); }
                    else if (v.Outcome == SaveModDropOutcome.Failed)
                    { saveSkipReasons.Add($"{Path.GetFileName(v.SourcePath)}: {v.Reason}"); remaining.Remove(v.SourcePath); }
                }
```

The `foreach` body is today's code, unchanged. If a local named `saveRisk` or `saveWritesAcked` already exists in the method, rename these rather than the existing ones.

**Ruling recorded here:** the enable gate proceeds when its delegate is unwired, so headless callers get no extra friction. The save-write gate **refuses** when unwired. The spec's promise is that a changed save is never written on a high-risk game without the prompt, and a missing delegate must not be how that promise breaks.

- [ ] **Step 4: Wire the delegate in the window.** In `MainWindow.xaml.cs`, directly after `ViewModel.ConfirmBanRiskEnable = ConfirmBanRiskEnableAsync;`:

```csharp
        // Save-write prompt for high-risk games. The VM decides whether to ask
        // (BanRiskRules.ShouldGateSaveWrite); the shared helper owns the copy.
        ViewModel.ConfirmSaveWrite = name => ModManager.App.Services.SaveWriteRiskPrompt.ShowAsync(Content.XamlRoot, name);
```

- [ ] **Step 5: Gate the character editor before its form opens.** In `SavesDialog.xaml.cs` `OnEditCharacter`, directly after `this.Hide();` and before `var dialog = new CharacterEditDialog(slot)`:

```csharp
        // A character edit puts a new change into a save. On a high-risk game, ask BEFORE the form
        // opens: a prompt that refuses finished input teaches people to click through prompts.
        // Asks every time until "don't ask again" is ticked for this game's saves. The prompt opens
        // while this dialog is hidden (one ContentDialog per XamlRoot).
        var saveLevel = BanRiskCatalog.Effective(_game);
        if (BanRiskRules.ShouldGateSaveWrite(saveLevel, BanRiskAckStore.IsAcked(_dataDir, _game.Id, BanRiskAck.WriteSaves)))
        {
            var (proceed, dontAsk) = await ModManager.App.Services.SaveWriteRiskPrompt.ShowAsync(xamlRoot, _game.GameName);
            if (!proceed)
            {
                StatusText.Text = "Nothing was written.";
                try { await this.ShowAsync(); }
                catch { /* re-show race — the user can re-open Saves from the More menu */ }
                return;
            }
            if (dontAsk) BanRiskAckStore.Ack(_dataDir, _game.Id, BanRiskAck.WriteSaves);
        }
```

Add `using ModManager.Core;` at the top of `SavesDialog.xaml.cs` if it is not already there. Otherwise qualify the three Core types.

- [ ] **Step 6: Build the App and run the suite.**

Close the app if it is running. Then run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Version=0.22.0`
Expected: Build succeeded, 0 warnings.
Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS, `CorePurityTests` and `BanRiskCallSiteTests` included.

- [ ] **Step 7: Confirm the ungated paths stay ungated.** Run this search:

Run: `git grep -n "ShouldGateSaveWrite\|GateSaveWriteAsync" -- src`
Expected: matches only in `GameBanRisk.cs`, `MainViewModel.cs` (the gate and the save-mod pre-check) and `SavesDialog.xaml.cs` (`OnEditCharacter`). Nothing in `OnSaveModReset`, `OnSaveModRemove`, any restore handler, or either `ProfileRestore` caller.

- [ ] **Step 8: Commit.**

```bash
git add src/ModManager.App/Services/SaveWriteRiskPrompt.cs src/ModManager.App/ViewModels/MainViewModel.cs src/ModManager.App/MainWindow.xaml.cs src/ModManager.App/SavesDialog.xaml.cs
git commit -m "feat(saves): ask before writing a new change into a save on a high-risk game

Behaviour change: Elden Ring players see the prompt when they edit a
character, on every edit until they tick don't ask again for that game.
Reset, remove and every restore never ask.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: Smoke entry

**Files:**
- Modify: `docs/smoke-tests/pending.md` (append a section at the end)

**Interfaces:** none.

- [ ] **Step 1: Append the section** with the Edit tool, not a heredoc:

```markdown
## Ban risk follows the game, and a new change to a save asks first

Shipped: `BanRiskCatalog.Effective(game)` resolves risk from the Steam id, the manifest id and a compiled
floor for College Football 27 and Madden NFL 27. Save writes that put something new into a save on a
high-risk game ask first (spec `docs/superpowers/specs/2026-09-13-ban-risk-for-games-without-a-steam-id-design.md`).

1. **Steam games unchanged.** Open Monster Hunter Wilds and Elden Ring. `StateChip.ban-risk` is present
   on both, and enabling a mod still shows *Enable mods on {game}?* unless already acknowledged.
2. **A game with no Steam id.** Register a throwaway game by hand against an empty folder, with the id
   `madden-nfl-27` and no Steam id. `StateChip.ban-risk` shows. Remove the game afterwards.
3. **The editor asks before the form opens.** Point the Saves dialog at a **copy** of an Elden Ring save
   in a throwaway folder, never the real save directory. Press Edit on a character: *Write to a save on
   ELDEN RING?* appears before the editor. Cancel: the status line says *Nothing was written.* and the
   file hash is unchanged.
4. **Every time until ticked.** Edit again without ticking: it asks again. Tick *Don't ask again for this
   game's saves* and write: the next edit does not ask. `ban-risk-save-acks.json` holds the game id and
   `ban-risk-acks.json` is unchanged.
5. **Fixes never ask.** Reset and Remove on a save mod, and restoring a snapshot, never show the prompt
   on any game.

Why it matters: before this, a game added from anywhere but Steam read no ban risk at all, and the
character editor wrote changed saves on an anti-cheat game without saying so.
```

- [ ] **Step 2: Check for control bytes and that the suite still passes.**

Run: `python -c "b=open('docs/smoke-tests/pending.md','rb').read(); print(sum(1 for c in b if c<32 and c not in (9,10,13)))"`
Expected: `0`
Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SmokeCatalogueTests"`
Expected: PASS. This entry is prose only and adds no `smoke.json` case, so the catalogue and the harness stay in step.

- [ ] **Step 3: Commit.**

```bash
git add docs/smoke-tests/pending.md
git commit -m "docs(smoke): ban risk without a Steam id, and the save-write prompt

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```
