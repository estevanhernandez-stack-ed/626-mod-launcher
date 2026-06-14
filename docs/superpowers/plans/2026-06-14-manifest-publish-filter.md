# Game Manifest — publish transform (make the feed consumable + small)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix two go-live blockers found verifying the first published feed: (1) feed entries carry mining provenance (`ludusavi`/`mo2`/`curated`) but the launcher facades consume only the functional tags (`known-engines`/`nexus-domains`/`popular-games`), so the feed would update nothing; (2) the published manifest is 26 MB / 48,657 mostly-skeletal entries. One transform fixes both: tag each entry with the functional tag its fields earn, and drop entries that earn none.

**Architecture:** A pure `PublishManifest.ForPublish(GameManifest) → GameManifest` in the miner: per entry, add `known-engines` if `engine` is set, `nexus-domains` if `nexusDomain` is set, `popular-games` if `engine && modPath && featured`; keep only entries that earned ≥1 functional tag (mining tags like `ludusavi` are preserved for attribution). The CLI applies it in the `--sign` path so the published `games-manifest.json` is the tagged, filtered set. A launcher-side test proves a feed-shaped entry is actually consumed by the facades — the guard whose absence let the gap through.

**Tech Stack:** .NET 10, C#, xUnit. Miner change + one launcher test. No facade/Core logic change.

**Why this is correct:** the launcher folder-detects engines at runtime and already knows game names/Steam-IDs, so a skeletal feed entry (name+steamId only, no engine/nexus/modPath/featured) provides nothing the facades read — dropping it loses no launcher behavior. `saveDirHint`-only entries are also dropped (the launcher doesn't consume manifest `saveDirHint` today; revisit if it ever does).

---

## Scope

**In:** `PublishManifest.ForPublish` (miner, pure, tested), the `--sign` CLI applying it, and a launcher consumption-guard test. **Out:** any facade/Merge logic change (the facades stay tag-filtered; the miner now produces the right tags). The merge-dedup-by-steamId edge (a feed entry sharing a Steam id with an embedded entry under a different `id`) is noted but **deferred** — the current curated/MO2 set doesn't collide with the embedded 16; track it for when curation targets an embedded game.

---

## File Structure

- Create: `tools/ManifestMiner/PublishManifest.cs`
- Modify: `tools/ManifestMiner/Program.cs` — apply `ForPublish` in `--sign`.
- Create: `tests/ModManager.Tests/Miner/PublishManifestTests.cs`
- Create: `tests/ModManager.Tests/Manifest/FeedConsumptionTests.cs` (launcher-side guard)

**Test command:** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

---

### Task 1: PublishManifest.ForPublish — tag by field + drop skeletal

**Files:**
- Create: `tools/ManifestMiner/PublishManifest.cs`
- Test: `tests/ModManager.Tests/Miner/PublishManifestTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/ModManager.Tests/Miner/PublishManifestTests.cs`:

```csharp
using ManifestMiner;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Miner;

public class PublishManifestTests
{
    private static GameManifestEntry Entry(string id, string? engine = null, string? nexus = null,
        string? modPath = null, int? featured = null)
        => new()
        {
            Id = id, Name = id, Engine = engine, NexusDomain = nexus, ModPath = modPath, Featured = featured,
            Stores = new StoreIds { SteamAppId = id },
            Provenance = new ManifestProvenance { Sources = new[] { "ludusavi" }, Status = "auto" },
        };

    private static GameManifest Wrap(params GameManifestEntry[] g) => new() { Games = g };

    [Fact]
    public void Engine_entry_earns_known_engines_tag_and_is_kept()
    {
        var e = PublishManifest.ForPublish(Wrap(Entry("a", engine: "bethesda"))).Games.Single();
        Assert.Contains("known-engines", e.Provenance.Sources);
        Assert.Contains("ludusavi", e.Provenance.Sources); // mining tag preserved
    }

    [Fact]
    public void Nexus_entry_earns_nexus_domains_tag()
    {
        var e = PublishManifest.ForPublish(Wrap(Entry("a", nexus: "skyrim"))).Games.Single();
        Assert.Contains("nexus-domains", e.Provenance.Sources);
    }

    [Fact]
    public void Featured_with_engine_and_modpath_earns_popular_games_tag()
    {
        var e = PublishManifest.ForPublish(Wrap(Entry("a", engine: "bethesda", modPath: "Data", featured: 1))).Games.Single();
        Assert.Contains("popular-games", e.Provenance.Sources);
        Assert.Contains("known-engines", e.Provenance.Sources);
    }

    [Fact]
    public void Featured_without_engine_or_modpath_does_not_earn_popular_games()
    {
        var e = PublishManifest.ForPublish(Wrap(Entry("a", nexus: "x", featured: 1))).Games.Single();
        Assert.DoesNotContain("popular-games", e.Provenance.Sources); // PopularGame projection needs engine+modPath
    }

    [Fact]
    public void Skeletal_entry_is_dropped()
    {
        // name + steamId only — nothing the launcher's facades read.
        var result = PublishManifest.ForPublish(Wrap(Entry("skeletal")));
        Assert.Empty(result.Games);
    }

    [Fact]
    public void Mix_keeps_only_useful_entries()
    {
        var result = PublishManifest.ForPublish(Wrap(
            Entry("keep1", engine: "bethesda"),
            Entry("drop1"),
            Entry("keep2", nexus: "y"),
            Entry("drop2")));
        Assert.Equal(2, result.Games.Count);
        Assert.All(result.Games, g => Assert.True(
            g.Provenance.Sources.Contains("known-engines")
            || g.Provenance.Sources.Contains("nexus-domains")
            || g.Provenance.Sources.Contains("popular-games")));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~PublishManifestTests"`
Expected: FAIL — `PublishManifest` does not exist.

- [ ] **Step 3: Implement**

`tools/ManifestMiner/PublishManifest.cs`:

```csharp
using ModManager.Core.Manifest;

namespace ManifestMiner;

/// <summary>Shapes the mined manifest for PUBLISHING: tags each entry with the functional facade tag
/// its fields earn (so the launcher's KnownEngines/NexusDomains/PopularGames actually consume feed
/// entries), and drops entries that earn none (the skeletal backbone provides nothing the launcher
/// reads). Mining-source tags (ludusavi/mo2/curated) are preserved for attribution.</summary>
public static class PublishManifest
{
    public static GameManifest ForPublish(GameManifest manifest)
    {
        var kept = new List<GameManifestEntry>();
        foreach (var g in manifest.Games)
        {
            var tags = new List<string>(g.Provenance.Sources);
            void Add(string t) { if (!tags.Contains(t)) tags.Add(t); }

            if (g.Engine is not null) Add(ManifestSources.KnownEngines);
            if (g.NexusDomain is not null) Add(ManifestSources.NexusDomains);
            if (g.Engine is not null && g.ModPath is not null && g.Featured is not null) Add(ManifestSources.PopularGames);

            var earned = tags.Contains(ManifestSources.KnownEngines)
                         || tags.Contains(ManifestSources.NexusDomains)
                         || tags.Contains(ManifestSources.PopularGames);
            if (!earned) continue; // skeletal — nothing the facades read

            kept.Add(g with { Provenance = g.Provenance with { Sources = tags } });
        }
        return manifest with { Games = kept };
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~PublishManifestTests"`
Expected: PASS (6 facts).

- [ ] **Step 5: Commit**

```bash
git add tools/ManifestMiner/PublishManifest.cs tests/ModManager.Tests/Miner/PublishManifestTests.cs
git commit -m "feat(miner): publish transform — tag entries by field, drop skeletal"
```

---

### Task 2: Apply ForPublish in the --sign path

**Files:**
- Modify: `tools/ManifestMiner/Program.cs`

- [ ] **Step 1: Apply the transform before signing**

In the `--sign` block, run `PublishManifest.ForPublish` on the final manifest **before** serializing/signing, so the published `games-manifest.json` is the tagged, filtered set. Add the kept-count to the console line.

```csharp
if (args.Contains("--sign"))
{
    var publish = PublishManifest.ForPublish(/* the final validated manifest in scope */);
    var bytes = JsonSerializer.SerializeToUtf8Bytes(publish, ManifestJson.Options);
    var manifestOut = Path.Combine(outDir, "games-manifest.json");
    File.WriteAllBytes(manifestOut, bytes);
    // ... existing key read + ManifestSigner.Sign(bytes, keyPem) + write .sig ...
    Console.WriteLine($"Signed {publish.Games.Count} useful games -> games-manifest.json (+ .sig, {sig.Length} bytes)");
}
```

(Thread the in-scope final-manifest variable; the unsigned `manifest-draft.json` output stays the full set for review.)

- [ ] **Step 2: Offline smoke-run**

Run:
```bash
pwsh -Command "$e=[System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP256); [System.IO.File]::WriteAllText(\"$env:TEMP\sk.pem\", $e.ExportPkcs8PrivateKeyPem())"
printf 'Skyrim:\n  steam:\n    id: 72850\n' > "$TEMP/ludu-pub.yaml"
MANIFEST_SIGNING_KEY="$(cat "$TEMP/sk.pem")" dotnet run --project tools/ManifestMiner -- --file "$TEMP/ludu-pub.yaml" --with-overrides --sign
```
Expected: "Signed N useful games ..." where N is small (the curated/enriched entries, NOT the full backbone). Inspect `tools/ManifestMiner/out/games-manifest.json` — entries carry `known-engines` (Skyrim 72850 from the override) etc.; no skeletal-only entries.

- [ ] **Step 3: Commit**

```bash
git add tools/ManifestMiner/Program.cs
git commit -m "feat(miner): --sign publishes the tagged, filtered manifest (consumable + small)"
```

---

### Task 3: Launcher consumption guard

**Files:**
- Create: `tests/ModManager.Tests/Manifest/FeedConsumptionTests.cs`

The test the gap slipped past: a published-shaped feed entry must be consumed by the facades.

- [ ] **Step 1: Write the test**

`tests/ModManager.Tests/Manifest/FeedConsumptionTests.cs`:

```csharp
using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

// A feed entry, once tagged by the miner's publish transform, must actually reach the facades.
[Collection("ManifestState")]
public class FeedConsumptionTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null);

    [Fact]
    public void Facades_consume_a_published_feed_entry()
    {
        Assert.Null(KnownEngines.ByAppId("900001")); // baseline: unknown

        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "feed-game", Name = "Feed Game", Engine = "bethesda",
                    Stores = new StoreIds { SteamAppId = "900001" }, NexusDomain = "feedgame",
                    // the tags the miner's PublishManifest.ForPublish stamps:
                    Provenance = new ManifestProvenance
                    {
                        Sources = new[] { "ludusavi", "mo2", "known-engines", "nexus-domains" },
                        Status = "curated",
                    },
                },
            },
        });

        Assert.Equal("bethesda", KnownEngines.ByAppId("900001"));   // consumed via known-engines tag
        Assert.Equal("feedgame", NexusDomains.ByAppId("900001"));   // consumed via nexus-domains tag
    }

    [Fact]
    public void Untagged_feed_entry_is_not_consumed()
    {
        // Guards the failure mode we hit: mining-only tags must NOT reach the facades
        // (the publish transform is what adds the functional tags).
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "raw", Name = "Raw", Engine = "bethesda",
                    Stores = new StoreIds { SteamAppId = "900002" },
                    Provenance = new ManifestProvenance { Sources = new[] { "ludusavi", "mo2" } },
                },
            },
        });
        Assert.Null(KnownEngines.ByAppId("900002")); // no functional tag -> not consumed
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~FeedConsumptionTests"`
Expected: PASS (2 facts) — confirms the tag-driven consumption contract both directions.

- [ ] **Step 3: Commit**

```bash
git add tests/ModManager.Tests/Manifest/FeedConsumptionTests.cs
git commit -m "test(manifest): feed-entry consumption guard (tagged consumed, untagged not)"
```

---

### Task 4: Full suite + scope

**Files:** none (verification only).

- [ ] **Step 1: Full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`
Expected: PASS — existing + `PublishManifestTests` + `FeedConsumptionTests`. `CorePurityTests` green.

- [ ] **Step 2: Scope**

Run: `git diff --name-only master..HEAD -- src/`
Expected: EMPTY (miner + tests only; no facade/Core logic change — the facades already filter by tag; the miner now produces the tags). Miner output gitignored.

- [ ] **Step 3: Final commit (if needed)**

```bash
git add -A && git commit -m "chore(miner): publish-filter slice — full suite green"
```

(Skip if clean.)

---

## Self-Review

**Spec coverage:** §8 publishable manifest; the feed must be consumed by the facades + stay small → Tasks 1–3. ✓

**Placeholder scan:** none. ✓

**Type consistency:** `PublishManifest.ForPublish(GameManifest) → GameManifest`; references real `ManifestSources.{KnownEngines,NexusDomains,PopularGames}`. The consumption test uses the same tags the transform stamps. ✓

**Correctness:** `FeedConsumptionTests` is the regression guard for the exact gap found (mining-tagged feed entries weren't consumed). The transform drops only entries the facades read nothing from. `popular-games` is stamped only with `engine+modPath+featured` (the fields the `PopularGame` projection requires), so it can't produce a null-bearing quick-pick entry. ✓

**Deferred (noted):** merge-dedup-by-steamId (same game, different `id`, in both embedded + feed) — not triggered by the current curated/MO2 set; revisit when curation targets an embedded game's Steam id. `saveDirHint`-only entries are dropped (unused by the launcher today).
