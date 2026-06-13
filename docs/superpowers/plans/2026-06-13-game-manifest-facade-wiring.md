# Game Manifest — Phase 1 (slice 2): wire the facades to EffectiveManifest

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route the three game-identity facades through `EffectiveManifest.Current` and give `EffectiveManifest` a `SetRemote` hook, so a verified remote manifest can override/extend the embedded one — with the read path proven unchanged when no remote is set.

**Architecture:** `EffectiveManifest` gains state: `SetRemote(GameManifest?)` (the App will call this once at startup in a later slice), a monotonic `Generation` counter, and `Current => Merge(embedded, remote)`. `KnownEngines`, `NexusDomains`, `PopularGames` stop reading `EmbeddedGameManifest.Current` directly and instead read `EffectiveManifest.Current` through a **generation-checked cache** (rebuild only when `SetRemote` bumps the generation). Tests that mutate the shared static live in a `DisableParallelization` collection and reset after each — so the existing parity tests stay byte-for-byte untouched and the zero-behavior-change proof holds. Pure Core; no network, no App changes, no key.

**Tech Stack:** .NET 10, C#, xUnit. No new package references.

**Spec:** `docs/superpowers/specs/2026-06-12-game-manifest-roadmap-design.md` §4 (facades become thin over the effective manifest), §5 (the effective manifest is embedded + verified-remote).

---

## Why this slice, and what it is NOT

This is the integration seam: the facades now flow through `EffectiveManifest`, and the `SetRemote` hook exists and is tested. The App-side `RemoteManifestSource` (HttpClient fetch → on-disk cache → `SetRemote` at startup) + the settings toggle is the **next** slice (slice 3) — it's the first production caller of `SetRemote`. Until then `SetRemote` has no production caller, exactly like the trust-core primitives were built before their caller. That's the disciplined order: seam first, proven inert, then plug in the source.

**Out of scope here:** no `HttpClient`/network, no App-project changes, no `Program.Main` edit, no settings UI, no pinned production key, no miner. Adding any of those is a scope violation.

**Zero behavior change** when no remote is set: `Merge(embedded, null)` returns the embedded manifest by reference, so `EffectiveManifest.Current == EmbeddedGameManifest.Current` and every facade answers identically. Proven by the existing parity tests passing **with their files unmodified**.

## Current shapes this builds on (on `master`)

- `EffectiveManifest.Merge(GameManifest embedded, GameManifest? remote)` — pure; null remote returns embedded unchanged. **No `Current`/`SetRemote`/`Generation` yet** — this slice adds them.
- `EmbeddedGameManifest.Current` — `Lazy<GameManifest>` property (the validated embedded snapshot).
- `KnownEngines` — `private static readonly IReadOnlyDictionary<string,string> Map = Build();` where `Build()` iterates `EmbeddedGameManifest.Current.Games` filtering `ManifestSources.KnownEngines`. Public: `ByAppId(string?)`, `AllMappedEngines`.
- `NexusDomains` — same eager-`Map` pattern filtering `ManifestSources.NexusDomains`. Public: `ByAppId(string?)`, `Effective(GameEntry)`.
- `PopularGames` — `public static IReadOnlyList<PopularGame> All { get; } = Build();` filtering `ManifestSources.PopularGames`, ordered by `Featured`. Public: `All`, `Find(string?)`, `PopularGame` record.
- Tests `KnownEnginesTests`, `NexusGameDomainTests`, `PopularGamesTests` are the parity proof — **must stay byte-for-byte unchanged** (verified by an empty `git diff` in Task 5).

---

## File Structure

- Modify: `src/ModManager.Core/Manifest/EffectiveManifest.cs` — add `Current`, `SetRemote`, `Generation` (keep `Merge`).
- Modify: `src/ModManager.Core/KnownEngines.cs` — generation-checked cache over `EffectiveManifest.Current`.
- Modify: `src/ModManager.Core/NexusDomains.cs` — same.
- Modify: `src/ModManager.Core/PopularGames.cs` — same.
- Create: `tests/ModManager.Tests/Manifest/ManifestStateCollection.cs` — `DisableParallelization` collection definition.
- Create: `tests/ModManager.Tests/Manifest/EffectiveManifestStateTests.cs`
- Create: `tests/ModManager.Tests/Manifest/FacadeRemoteWiringTests.cs`
- Untouched (parity proof): `tests/ModManager.Tests/{KnownEnginesTests,PopularGamesTests,NexusGameDomainTests}.cs`

**Test command (never bare root):** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

---

### Task 1: EffectiveManifest state — Current / SetRemote / Generation + the test collection

**Files:**
- Modify: `src/ModManager.Core/Manifest/EffectiveManifest.cs`
- Create: `tests/ModManager.Tests/Manifest/ManifestStateCollection.cs`
- Create: `tests/ModManager.Tests/Manifest/EffectiveManifestStateTests.cs`

- [ ] **Step 1: Write the collection definition**

This collection runs serially relative to the rest of the suite, so state-mutating tests never race the parity tests. Create `tests/ModManager.Tests/Manifest/ManifestStateCollection.cs`:

```csharp
using Xunit;

namespace ModManager.Tests.Manifest;

// Tests in this collection mutate the process-global EffectiveManifest static (SetRemote).
// DisableParallelization keeps them from running concurrently with any other collection —
// so the parity tests (KnownEnginesTests etc.) never observe a transient remote and stay
// byte-for-byte unmodified. Every test in here MUST reset EffectiveManifest.SetRemote(null).
[CollectionDefinition("ManifestState", DisableParallelization = true)]
public class ManifestStateCollection { }
```

- [ ] **Step 2: Write the failing state tests**

Create `tests/ModManager.Tests/Manifest/EffectiveManifestStateTests.cs`:

```csharp
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

[Collection("ManifestState")]
public class EffectiveManifestStateTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null); // never leak state to other tests

    private static GameManifest Remote(params GameManifestEntry[] games) => new() { Games = games };

    private static GameManifestEntry Entry(string id, string engine, string? appId = null)
        => new()
        {
            Id = id,
            Name = id,
            Engine = engine,
            Stores = new StoreIds { SteamAppId = appId },
            Provenance = new ManifestProvenance { Sources = new[] { "known-engines" } },
        };

    [Fact]
    public void Current_defaults_to_the_embedded_manifest()
    {
        EffectiveManifest.SetRemote(null);
        Assert.Same(EmbeddedGameManifest.Current, EffectiveManifest.Current);
    }

    [Fact]
    public void SetRemote_makes_Current_reflect_the_merge()
    {
        EffectiveManifest.SetRemote(Remote(Entry("brand-new-game", "bethesda", "55555")));

        Assert.Contains(EffectiveManifest.Current.Games, g => g.Id == "brand-new-game");
        // embedded games still present
        Assert.Contains(EffectiveManifest.Current.Games, g => g.Id == "elden-ring");
    }

    [Fact]
    public void SetRemote_bumps_the_generation()
    {
        var before = EffectiveManifest.Generation;
        EffectiveManifest.SetRemote(Remote(Entry("x", "bethesda")));
        var after = EffectiveManifest.Generation;
        Assert.True(after > before, $"generation did not advance: {before} -> {after}");
    }

    [Fact]
    public void SetRemote_null_reverts_to_embedded()
    {
        EffectiveManifest.SetRemote(Remote(Entry("temp", "bethesda")));
        EffectiveManifest.SetRemote(null);
        Assert.Same(EmbeddedGameManifest.Current, EffectiveManifest.Current);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~EffectiveManifestStateTests"`
Expected: FAIL — `EffectiveManifest.Current` / `SetRemote` / `Generation` do not exist.

- [ ] **Step 4: Add the state to EffectiveManifest**

Edit `src/ModManager.Core/Manifest/EffectiveManifest.cs` — keep the existing `Merge` method exactly; add the state members. The full file becomes:

```csharp
using System.Threading;

namespace ModManager.Core.Manifest;

/// <summary>
/// Produces the effective game manifest: the embedded snapshot overlaid with a verified remote
/// manifest (when one has been set). The facades read <see cref="Current"/>. <see cref="SetRemote"/>
/// is called once at startup by the App layer after it loads + verifies a cached remote manifest;
/// until then the remote is null and Current == the embedded manifest, so behavior is unchanged.
/// </summary>
public static class EffectiveManifest
{
    // Reference assignment is atomic; volatile gives cross-thread visibility. SetRemote is a
    // startup-time, single-writer operation in production; reads are lock-free.
    private static volatile GameManifest? _remote;
    private static int _generation;

    /// <summary>Monotonic counter; advances on every <see cref="SetRemote"/>. Consumers cache by it.</summary>
    public static int Generation => Volatile.Read(ref _generation);

    /// <summary>The embedded manifest overlaid with the current remote (if any).</summary>
    public static GameManifest Current => Merge(EmbeddedGameManifest.Current, _remote);

    /// <summary>Set (or clear, with null) the verified remote manifest. Bumps <see cref="Generation"/>.</summary>
    public static void SetRemote(GameManifest? remote)
    {
        _remote = remote;
        Interlocked.Increment(ref _generation);
    }

    public static GameManifest Merge(GameManifest embedded, GameManifest? remote)
    {
        if (remote is null)
            return embedded;

        var byId = new Dictionary<string, GameManifestEntry>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var g in embedded.Games)
        {
            if (byId.TryAdd(g.Id, g)) order.Add(g.Id);
            else byId[g.Id] = g;
        }
        foreach (var g in remote.Games) // remote wins on id collision; new ids appended in remote order
        {
            if (byId.TryAdd(g.Id, g)) order.Add(g.Id);
            else byId[g.Id] = g;
        }

        return embedded with { Games = order.Select(id => byId[id]).ToList() };
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~EffectiveManifestStateTests"`
Expected: PASS (4 facts). The existing `EffectiveManifestTests` (Merge) must also still pass — run `--filter "FullyQualifiedName~EffectiveManifest"` to confirm both.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/Manifest/EffectiveManifest.cs tests/ModManager.Tests/Manifest/ManifestStateCollection.cs tests/ModManager.Tests/Manifest/EffectiveManifestStateTests.cs
git commit -m "feat(manifest): EffectiveManifest.Current/SetRemote/Generation state"
```

---

### Task 2: KnownEngines reads EffectiveManifest.Current (generation-checked)

**Files:**
- Modify: `src/ModManager.Core/KnownEngines.cs`
- Test: add to `tests/ModManager.Tests/Manifest/FacadeRemoteWiringTests.cs` (created here)
- Parity proof (must stay unchanged): `tests/ModManager.Tests/KnownEnginesTests.cs`

- [ ] **Step 1: Write the failing wiring test**

Create `tests/ModManager.Tests/Manifest/FacadeRemoteWiringTests.cs` (NexusDomains + PopularGames cases append here in Tasks 3–4):

```csharp
using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

// Proves the facades read the EFFECTIVE manifest (embedded + remote), not just the embedded one.
// In the DisableParallelization "ManifestState" collection so SetRemote never races other tests.
[Collection("ManifestState")]
public class FacadeRemoteWiringTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null);

    private static GameManifest Remote(params GameManifestEntry[] games) => new() { Games = games };

    [Fact]
    public void KnownEngines_reflects_a_remote_added_game()
    {
        // an app id not in the embedded snapshot
        Assert.Null(KnownEngines.ByAppId("70000001")); // embedded baseline: unknown

        EffectiveManifest.SetRemote(Remote(new GameManifestEntry
        {
            Id = "remote-bethesda-game",
            Name = "Remote Bethesda Game",
            Engine = "bethesda",
            Stores = new StoreIds { SteamAppId = "70000001" },
            Provenance = new ManifestProvenance { Sources = new[] { ManifestSources.KnownEngines } },
        }));

        Assert.Equal("bethesda", KnownEngines.ByAppId("70000001")); // now resolved via the remote
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FacadeRemoteWiringTests.KnownEngines_reflects_a_remote_added_game"`
Expected: FAIL — `KnownEngines` still reads the embedded manifest via an eager static `Map`, so the remote game isn't seen (returns null).

- [ ] **Step 3: Rewire KnownEngines to a generation-checked cache**

Edit `src/ModManager.Core/KnownEngines.cs`. Replace the eager `private static readonly ... Map = Build();` field with a generation-checked accessor; `Build()` reads `EffectiveManifest.Current` instead of `EmbeddedGameManifest.Current`. Keep `using ModManager.Core.Manifest;`, the namespace, and the public signatures (`ByAppId`, `AllMappedEngines`) identical. The class body becomes:

```csharp
    private static IReadOnlyDictionary<string, string>? _map;
    private static int _mapGen = -1;
    private static readonly object _gate = new();

    private static IReadOnlyDictionary<string, string> Map
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

    private static IReadOnlyDictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>();
        foreach (var g in EffectiveManifest.Current.Games)
        {
            if (g.Provenance.Sources.Contains(ManifestSources.KnownEngines)
                && g.Stores.SteamAppId is { } appId
                && g.Engine is { } engine)
            {
                map[appId] = engine;
            }
        }
        return map;
    }

    public static string? ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Map.TryGetValue(steamAppId, out var e) ? e : null;

    public static IEnumerable<string> AllMappedEngines => Map.Values.Distinct();
```

(Only the `EmbeddedGameManifest.Current` → `EffectiveManifest.Current` swap and the eager-field → generation-checked-`Map` change; the provenance-filter logic is identical.)

- [ ] **Step 4: Run the wiring test + the parity test**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FacadeRemoteWiringTests.KnownEngines_reflects_a_remote_added_game|FullyQualifiedName~KnownEnginesTests"`
Expected: PASS — the wiring test now sees the remote game, and `KnownEnginesTests` (unchanged) still passes (no remote set → `Current == embedded`).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/KnownEngines.cs tests/ModManager.Tests/Manifest/FacadeRemoteWiringTests.cs
git commit -m "refactor(manifest): KnownEngines reads EffectiveManifest.Current"
```

---

### Task 3: NexusDomains reads EffectiveManifest.Current (generation-checked)

**Files:**
- Modify: `src/ModManager.Core/NexusDomains.cs`
- Test: append to `tests/ModManager.Tests/Manifest/FacadeRemoteWiringTests.cs`
- Parity proof (unchanged): `tests/ModManager.Tests/NexusGameDomainTests.cs`

- [ ] **Step 1: Append the failing wiring test**

Add to the `FacadeRemoteWiringTests` class in `tests/ModManager.Tests/Manifest/FacadeRemoteWiringTests.cs`:

```csharp
    [Fact]
    public void NexusDomains_reflects_a_remote_added_slug()
    {
        Assert.Null(NexusDomains.ByAppId("70000002")); // embedded baseline: unknown

        EffectiveManifest.SetRemote(Remote(new GameManifestEntry
        {
            Id = "remote-nexus-game",
            Name = "Remote Nexus Game",
            Engine = "ue-pak",
            Stores = new StoreIds { SteamAppId = "70000002" },
            NexusDomain = "remotegame",
            Provenance = new ManifestProvenance { Sources = new[] { ManifestSources.NexusDomains } },
        }));

        Assert.Equal("remotegame", NexusDomains.ByAppId("70000002"));
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FacadeRemoteWiringTests.NexusDomains_reflects_a_remote_added_slug"`
Expected: FAIL — `NexusDomains` still reads the embedded manifest.

- [ ] **Step 3: Rewire NexusDomains**

Edit `src/ModManager.Core/NexusDomains.cs`. Same transform as KnownEngines: replace the eager `Map` field with the generation-checked accessor; `Build()` reads `EffectiveManifest.Current`, filters `ManifestSources.NexusDomains`, maps `SteamAppId → NexusDomain`. Keep `ByAppId` and `Effective(GameEntry)` signatures identical. The relevant members become:

```csharp
    private static IReadOnlyDictionary<string, string>? _map;
    private static int _mapGen = -1;
    private static readonly object _gate = new();

    private static IReadOnlyDictionary<string, string> Map
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

    private static IReadOnlyDictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>();
        foreach (var g in EffectiveManifest.Current.Games)
        {
            if (g.Provenance.Sources.Contains(ManifestSources.NexusDomains)
                && g.Stores.SteamAppId is { } appId
                && g.NexusDomain is { } domain)
            {
                map[appId] = domain;
            }
        }
        return map;
    }

    public static string? ByAppId(string? steamAppId)
        => !string.IsNullOrEmpty(steamAppId) && Map.TryGetValue(steamAppId, out var d) ? d : null;

    public static string? Effective(GameEntry game)
        => !string.IsNullOrWhiteSpace(game.NexusGameDomain) ? game.NexusGameDomain : ByAppId(game.SteamAppId);
```

Ensure `using ModManager.Core.Manifest;` is present.

- [ ] **Step 4: Run the wiring test + parity test**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FacadeRemoteWiringTests.NexusDomains_reflects_a_remote_added_slug|FullyQualifiedName~NexusGameDomainTests"`
Expected: PASS — wiring test sees the remote slug; `NexusGameDomainTests` (unchanged) still passes.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/NexusDomains.cs tests/ModManager.Tests/Manifest/FacadeRemoteWiringTests.cs
git commit -m "refactor(manifest): NexusDomains reads EffectiveManifest.Current"
```

---

### Task 4: PopularGames reads EffectiveManifest.Current (generation-checked)

**Files:**
- Modify: `src/ModManager.Core/PopularGames.cs`
- Test: append to `tests/ModManager.Tests/Manifest/FacadeRemoteWiringTests.cs`
- Parity proof (unchanged): `tests/ModManager.Tests/PopularGamesTests.cs`

`PopularGames.All` is currently an eager auto-property (`{ get; } = Build();`). It becomes a computed property with the same generation-checked cache.

- [ ] **Step 1: Append the failing wiring test**

Add to `FacadeRemoteWiringTests`:

```csharp
    [Fact]
    public void PopularGames_reflects_a_remote_featured_game()
    {
        Assert.DoesNotContain(PopularGames.All, g => g.Id == "remote-featured-game"); // baseline

        EffectiveManifest.SetRemote(Remote(new GameManifestEntry
        {
            Id = "remote-featured-game",
            Name = "Remote Featured Game",
            Engine = "bethesda",
            Stores = new StoreIds { SteamAppId = "70000003" },
            ModPath = "Data",
            Featured = 99,
            Provenance = new ManifestProvenance { Sources = new[] { ManifestSources.PopularGames } },
        }));

        var g = PopularGames.Find("remote-featured-game");
        Assert.NotNull(g);
        Assert.Equal("Remote Featured Game", g!.Name);
        Assert.Equal("bethesda", g.Engine);
        Assert.Equal("Data", g.ModPath);
        Assert.Equal("70000003", g.SteamAppId);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FacadeRemoteWiringTests.PopularGames_reflects_a_remote_featured_game"`
Expected: FAIL — `PopularGames.All` is an eager static built from the embedded manifest.

- [ ] **Step 3: Rewire PopularGames**

Edit `src/ModManager.Core/PopularGames.cs`. Keep the `PopularGame` record and `Find` exactly. Replace the eager `All` auto-property with a generation-checked computed property over `EffectiveManifest.Current`:

```csharp
    private static IReadOnlyList<PopularGame>? _all;
    private static int _allGen = -1;
    private static readonly object _gate = new();

    public static IReadOnlyList<PopularGame> All
    {
        get
        {
            lock (_gate)
            {
                var gen = EffectiveManifest.Generation;
                if (_all is null || _allGen != gen)
                {
                    _all = Build();
                    _allGen = gen;
                }
                return _all;
            }
        }
    }

    private static IReadOnlyList<PopularGame> Build()
        => EffectiveManifest.Current.Games
            .Where(g => g.Provenance.Sources.Contains(ManifestSources.PopularGames))
            .OrderBy(g => g.Featured ?? int.MaxValue)
            .Select(g => new PopularGame(g.Id, g.Name, g.Engine!, g.ModPath!, g.Stores.SteamAppId!)
            {
                FileExtensions = g.FileExtensions,
            })
            .ToList();

    /// <summary>Look up a game by id; null when unknown (or the id is null/empty).</summary>
    public static PopularGame? Find(string? id)
        => string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(g => g.Id == id);
```

Ensure `using ModManager.Core.Manifest;` is present.

- [ ] **Step 4: Run the wiring test + parity test**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FacadeRemoteWiringTests.PopularGames_reflects_a_remote_featured_game|FullyQualifiedName~PopularGamesTests"`
Expected: PASS — wiring test sees the remote featured game; `PopularGamesTests` (unchanged) still passes (order + Cyberpunk override intact, since no remote is set during its run).

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/PopularGames.cs tests/ModManager.Tests/Manifest/FacadeRemoteWiringTests.cs
git commit -m "refactor(manifest): PopularGames reads EffectiveManifest.Current"
```

---

### Task 5: Full suite + purity green, parity files provably untouched

**Files:** none (verification only).

- [ ] **Step 1: Run the complete Core suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — all tests, including the new `EffectiveManifestStateTests` + `FacadeRemoteWiringTests`, the unchanged parity tests, the Phase 0 manifest tests, and `CorePurityTests` (still pure — only `System.Threading` added, which is BCL). Note the total is ≥ the prior count plus the new tests; 0 failures.

- [ ] **Step 2: Prove the parity test files are byte-for-byte untouched**

Run: `git diff --stat master..HEAD -- tests/ModManager.Tests/KnownEnginesTests.cs tests/ModManager.Tests/PopularGamesTests.cs tests/ModManager.Tests/NexusGameDomainTests.cs`
Expected: empty output. The zero-behavior-change proof: the parity tests pass and their files were never edited.

- [ ] **Step 3: Confirm scope — no App/network leaked in**

Run: `git diff --name-only master..HEAD -- src/`
Expected: only `src/ModManager.Core/Manifest/EffectiveManifest.cs`, `src/ModManager.Core/KnownEngines.cs`, `src/ModManager.Core/NexusDomains.cs`, `src/ModManager.Core/PopularGames.cs`. No `src/ModManager.App/` changes, no `HttpClient`.

- [ ] **Step 4: Final commit (if any uncommitted fixups)**

```bash
git add -A
git commit -m "chore(manifest): facade wiring — full Core suite green"
```

(Skip if the working tree is clean.)

---

## Self-Review

**Spec coverage:** §4 facades thin over the effective manifest → Tasks 2–4. §5 effective = embedded + remote, the `SetRemote` hook → Task 1. ✓

**Placeholder scan:** No TBD/TODO; all code complete. ✓

**Type consistency:** `EffectiveManifest.Current` / `SetRemote(GameManifest?)` / `Generation` defined in Task 1, used identically in Tasks 2–4 and the tests. The generation-checked `Map`/`All` cache pattern is the same shape in all three facades. `ManifestSources.{KnownEngines,NexusDomains,PopularGames}` and `GameManifestEntry`/`StoreIds`/`ManifestProvenance` match the merged Phase 0 types. ✓

**Test-isolation correctness:** all `SetRemote`-mutating tests live in the `DisableParallelization` `ManifestState` collection and reset via `IDisposable.Dispose`, so they never run concurrently with — nor leak state into — the untouched parity tests. The generation counter guarantees a facade rebuilds after `SetRemote`, even though the parity tests (which never set a remote) see the embedded manifest. ✓

**One judgment flagged:** `SetRemote` has no production caller in this slice — it's the seam the App-side `RemoteManifestSource` will call at startup (slice 3). This mirrors the trust-core order (primitives before caller) and is proven inert. The generation-checked cache adds a per-access lock; contention is nil (startup-only writes), and the lock is the simplest correct guard against a torn rebuild.
