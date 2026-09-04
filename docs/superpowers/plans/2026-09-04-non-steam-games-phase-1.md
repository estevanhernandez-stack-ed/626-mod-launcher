# Non-Steam games, phase 1: slug-keyed overrides Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a curated override describe a game that is not on Steam, and fail the build when two overrides claim the same key.

**Architecture:** Two changes, both in the manifest miner, both pure. An override currently needs a Steam app id to survive loading *and* merging; after this it may key on its slug instead. Because the launcher already resolves a registered game to its manifest entry by slug (`Scanner.cs:63`), that alone makes a non-Steam game curateable end to end — no launcher change, no schema change, no app release. A validation gate lands with it, because slug-keying makes duplicate slugs a new failure mode and duplicate Steam ids are already a live one.

**Tech Stack:** .NET 10, C#, xUnit. `tools/ManifestMiner` (console, pure logic), tests in `tests/ModManager.Tests/Miner/` (which project-references the miner).

**Spec:** `docs/superpowers/specs/2026-09-04-non-steam-games-in-the-manifest-design.md` — sections C1 and C2. Its sibling `2026-09-04-launch-options-as-curated-data-design.md` is **not** in this plan.

## Global Constraints

- **camelCase JSON on disk.** Override files and the manifest are camelCase; use `ManifestJson.Options` for every serialize/deserialize. See `.claude/rules/camelcase-json-on-disk.md`.
- **Nullable enabled, warnings are errors** (`Directory.Build.props`). No `!` to silence a genuine null.
- **Never run bare `dotnet test` or `dotnet build` at the repo root** — the WinUI project hangs it. Always `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`.
- **Existing behaviour is preserved exactly.** All 149 current override files carry a Steam app id and must keep matching by it, in preference to the slug.
- **Conventional commits**, area `manifest` or `miner`.
- **The miner is tool-only** — never shipped in the launcher binary.

## Scope

**In:** C1 (overrides may key on slug) and C2 (duplicate-key gate).

**Out, and deliberately:** the EA store identifier (C3 — nothing keys on it, and the right key is an open question), the `PopularGames` picker change (C4), the explicit slug join in `GameInput` (C5), all non-Steam discovery, and both launch-option specs. Phase 1 needs none of the open questions answered.

## A correction to the spec, found while planning

The spec says the blocker is one line, in `OverridesMerge.Apply`. **It is two.** `OverridesLoader.Load` also drops the entry, before the merge ever sees it:

```csharp
if (entry is not null && !string.IsNullOrWhiteSpace(entry.SteamAppId))
    result.Add(entry);
```

Task 1 fixes the loader, Task 3 fixes the merge. Task 5 corrects the spec text so it stops claiming one.

`Load` also throws away the file path, so the duplicate gate could not name the offending files. Task 2 gives loaded entries their source path — which is why the gate comes after it.

## File Structure

| File | Responsibility |
|---|---|
| `tools/ManifestMiner/OverrideEntry.cs` | modify — `SteamAppId` becomes optional; add `SourcePath` |
| `tools/ManifestMiner/OverridesLoader.cs` | modify — load entries without a Steam id; record each file's path |
| `tools/ManifestMiner/OverridesValidate.cs` | **create** — pure duplicate-key check, returns problems |
| `tools/ManifestMiner/OverridesMerge.cs` | modify — resolve by Steam id, else by slug |
| `tools/ManifestMiner/Program.cs` | modify — run the gate, fail the run on a problem |
| `tests/ModManager.Tests/Miner/OverridesLoaderTests.cs` | modify — loader cases |
| `tests/ModManager.Tests/Miner/OverridesValidateTests.cs` | **create** — gate cases |
| `tests/ModManager.Tests/Miner/OverridesMergeTests.cs` | modify — merge cases |
| `626-game-manifest/overrides/README.md` | modify — document slug-keying |

---

### Task 1: An override may omit its Steam app id

**Files:**
- Modify: `tools/ManifestMiner/OverrideEntry.cs`
- Modify: `tools/ManifestMiner/OverridesLoader.cs:19-23`
- Test: `tests/ModManager.Tests/Miner/OverridesLoaderTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `OverrideEntry.SteamAppId` is now `string?` (was `string` defaulting to `""`). `OverridesLoader.Load(string overridesDir)` keeps its signature and returns entries that may have a null `SteamAppId`.

- [ ] **Step 1: Write the failing test**

Append to `tests/ModManager.Tests/Miner/OverridesLoaderTests.cs`:

```csharp
    [Fact]
    public void Loads_an_override_that_has_no_Steam_app_id()
    {
        // A game bought from the EA app, Epic or GOG has no Steam id. Before this, the loader
        // dropped it here and the merge dropped it again - two silent gates, no report.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "some-ea-game.json"),
            "{ \"id\": \"some-ea-game\", \"name\": \"Some EA Game\", \"engine\": \"custom\" }");

        var loaded = OverridesLoader.Load(_dir);

        var entry = Assert.Single(loaded);
        Assert.Equal("some-ea-game", entry.Id);
        Assert.Null(entry.SteamAppId);
    }

    [Fact]
    public void An_override_with_neither_an_id_nor_a_Steam_id_is_still_dropped()
    {
        // There would be nothing to key it on. Task 3's gate reports this; the loader just
        // refuses to produce an entry that cannot be addressed.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "nameless.json"), "{ \"engine\": \"custom\" }");

        Assert.Empty(OverridesLoader.Load(_dir));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~OverridesLoaderTests"`

Expected: FAIL. `Loads_an_override_that_has_no_Steam_app_id` fails with `Assert.Single() Failure: The collection was empty`.

- [ ] **Step 3: Make `SteamAppId` optional**

In `tools/ManifestMiner/OverrideEntry.cs`, change the summary and the field:

```csharp
/// <summary>A hand-curated correction. Keyed by Steam app id when it has one, and otherwise by its
/// slug (<see cref="Id"/>) — a game bought from the EA app, Epic or GOG has no Steam id, and refusing
/// those made a whole category of game uncurateable. Any non-null field overrides the mined value on
/// the matched entry, or seeds a new entry when nothing matches. Curated data wins over everything the
/// miner produced.</summary>
public sealed record OverrideEntry
{
    /// <summary>The Steam app id, when the game is on Steam. Null is normal now, not an error.</summary>
    public string? SteamAppId { get; init; }
```

- [ ] **Step 4: Load an entry that has a usable key**

In `tools/ManifestMiner/OverridesLoader.cs`, replace the accept condition:

```csharp
                var entry = JsonSerializer.Deserialize<OverrideEntry>(File.ReadAllText(file), ManifestJson.Options);

                // Keyed by Steam id OR by slug. An entry with neither cannot be addressed at all, so
                // it is still refused here - OverridesValidate reports it as a build problem.
                if (entry is not null
                    && (!string.IsNullOrWhiteSpace(entry.SteamAppId) || !string.IsNullOrWhiteSpace(entry.Id)))
                    result.Add(entry);
```

Update the class summary's second sentence to say a file with neither key is skipped and reported.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~OverridesLoaderTests"`

Expected: PASS, all cases including the four that existed before.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

Expected: PASS. `SteamAppId` becoming nullable can surface warnings-as-errors elsewhere; if the build fails, fix the call site rather than restoring the non-null type.

- [ ] **Step 7: Commit**

```bash
git add tools/ManifestMiner/OverrideEntry.cs tools/ManifestMiner/OverridesLoader.cs tests/ModManager.Tests/Miner/OverridesLoaderTests.cs
git commit -m "feat(miner): an override may omit its Steam app id"
```

---

### Task 2: A loaded override remembers which file it came from

**Files:**
- Modify: `tools/ManifestMiner/OverrideEntry.cs`
- Modify: `tools/ManifestMiner/OverridesLoader.cs`
- Test: `tests/ModManager.Tests/Miner/OverridesLoaderTests.cs`

**Interfaces:**
- Consumes: `OverrideEntry` from Task 1.
- Produces: `OverrideEntry.SourcePath` — `string?`, the full path of the file the entry was read from, `null` for an entry constructed in a test. Task 3's gate names files with it.

- [ ] **Step 1: Write the failing test**

Append to `tests/ModManager.Tests/Miner/OverridesLoaderTests.cs`:

```csharp
    [Fact]
    public void A_loaded_override_remembers_its_file_so_a_problem_can_name_it()
    {
        // "Two overrides collide" is not actionable without both file names. The path is set by the
        // loader rather than parsed from JSON - it is not a curated field and must not be settable
        // from a file.
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "skyrim.json");
        File.WriteAllText(path, "{ \"steamAppId\": \"72850\", \"engine\": \"bethesda\" }");

        var entry = Assert.Single(OverridesLoader.Load(_dir));

        Assert.Equal(path, entry.SourcePath);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~A_loaded_override_remembers_its_file"`

Expected: FAIL to COMPILE — `'OverrideEntry' does not contain a definition for 'SourcePath'`.

- [ ] **Step 3: Add the field**

In `tools/ManifestMiner/OverrideEntry.cs`, after `SteamAppId`:

```csharp
    /// <summary>The file this entry was read from. Set by <see cref="OverridesLoader"/>, never by the
    /// JSON — it exists so a build problem can name the offending file, and a curated file must not be
    /// able to lie about where it lives.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? SourcePath { get; init; }
```

- [ ] **Step 4: Set it when loading**

In `tools/ManifestMiner/OverridesLoader.cs`, change the accept block to stamp the path:

```csharp
                if (entry is not null
                    && (!string.IsNullOrWhiteSpace(entry.SteamAppId) || !string.IsNullOrWhiteSpace(entry.Id)))
                    result.Add(entry with { SourcePath = file });
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~OverridesLoaderTests"`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add tools/ManifestMiner/OverrideEntry.cs tools/ManifestMiner/OverridesLoader.cs tests/ModManager.Tests/Miner/OverridesLoaderTests.cs
git commit -m "feat(miner): a loaded override remembers its file"
```

---

### Task 3: The duplicate-key gate

**Files:**
- Create: `tools/ManifestMiner/OverridesValidate.cs`
- Test: `tests/ModManager.Tests/Miner/OverridesValidateTests.cs`

**Interfaces:**
- Consumes: `OverrideEntry` with `SteamAppId`, `Id`, `Name`, `SourcePath` from Tasks 1 and 2.
- Produces:
  - `record OverrideProblem(string Message)`
  - `static IReadOnlyList<OverrideProblem> OverridesValidate.Check(IReadOnlyList<OverrideEntry> overrides)` — empty when everything is fine.
  - `static string OverridesValidate.KeyOf(OverrideEntry entry)` — the slug an entry will be addressed by: its `Id`, else `EnginePresets.Slugify(Name)`, else `""`.

This gate is not hypothetical. Two files in `626-game-manifest/overrides/` both claim Steam app id `20920` today (`the-witcher-2-assassins-of-kings.json` and `the-witcher-2-assassins-of-kings-enhanced-edition.json`). The richer one wins by iteration order alone.

- [ ] **Step 1: Write the failing tests**

Create `tests/ModManager.Tests/Miner/OverridesValidateTests.cs`:

```csharp
using ManifestMiner;

namespace ModManager.Tests.Miner;

/// <summary>
/// The gate that stops two curated files quietly fighting over one game.
///
/// <para>Before it, two overrides sharing a Steam app id both "worked": one won by iteration order and
/// nothing said so. Two files in the real overrides directory do exactly that
/// (the-witcher-2-assassins-of-kings and its enhanced edition, both claiming 20920). Today the richer
/// one happens to win; if that order ever flipped, the game would silently drop to nexus-only with no
/// engine and no mod path.</para>
/// </summary>
public class OverridesValidateTests
{
    private static OverrideEntry E(string? id = null, string? steam = null, string? name = null, string? path = null)
        => new() { Id = id, SteamAppId = steam, Name = name, SourcePath = path ?? (id ?? steam) + ".json" };

    [Fact]
    public void A_clean_set_has_no_problems()
        => Assert.Empty(OverridesValidate.Check(new[]
        {
            E(id: "skyrim", steam: "72850"),
            E(id: "palworld", steam: "1623730"),
            E(id: "some-ea-game"),
        }));

    [Fact]
    public void Two_overrides_sharing_a_Steam_id_are_a_problem_that_names_both_files()
    {
        var problems = OverridesValidate.Check(new[]
        {
            E(id: "the-witcher-2", steam: "20920", path: "witcher2.json"),
            E(id: "the-witcher-2-ee", steam: "20920", path: "witcher2-ee.json"),
        });

        var p = Assert.Single(problems);
        Assert.Contains("20920", p.Message);
        Assert.Contains("witcher2.json", p.Message);
        Assert.Contains("witcher2-ee.json", p.Message);
    }

    [Fact]
    public void Two_overrides_sharing_a_slug_are_a_problem_that_names_both_files()
    {
        // The failure mode slug-keying introduces. There is no second key to disambiguate on, so it
        // has to be fatal rather than resolved.
        var problems = OverridesValidate.Check(new[]
        {
            E(id: "big-ambitions", path: "big-ambitions.json"),
            E(id: "big-ambitions", path: "big-ambitions-copy.json"),
        });

        var p = Assert.Single(problems);
        Assert.Contains("big-ambitions", p.Message);
        Assert.Contains("big-ambitions-copy.json", p.Message);
    }

    [Fact]
    public void A_slug_derived_from_the_name_still_collides()
    {
        // An entry with no explicit id is addressed by Slugify(Name), so two of those collide just as
        // hard as two explicit ids - and it is less obvious from reading the files.
        var problems = OverridesValidate.Check(new[]
        {
            E(name: "Big Ambitions", path: "a.json"),
            E(name: "Big Ambitions", path: "b.json"),
        });

        Assert.Single(problems);
    }

    [Fact]
    public void An_override_with_no_usable_key_at_all_is_a_problem()
    {
        var problems = OverridesValidate.Check(new[] { E(path: "mystery.json") });

        var p = Assert.Single(problems);
        Assert.Contains("mystery.json", p.Message);
    }

    [Fact]
    public void Keys_are_compared_case_insensitively()
    {
        // "Palworld.json" and "palworld.json" are the same game to every consumer downstream.
        Assert.Single(OverridesValidate.Check(new[] { E(id: "Palworld"), E(id: "palworld") }));
    }

    [Fact]
    public void The_same_slug_and_the_same_Steam_id_reports_once_not_twice()
    {
        // One pair of files, one problem. Reporting it under both rules would read as two conflicts.
        var problems = OverridesValidate.Check(new[]
        {
            E(id: "dupe", steam: "111", path: "a.json"),
            E(id: "dupe", steam: "111", path: "b.json"),
        });

        Assert.Single(problems);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~OverridesValidateTests"`

Expected: FAIL to COMPILE — `The name 'OverridesValidate' does not exist`.

- [ ] **Step 3: Write the gate**

Create `tools/ManifestMiner/OverridesValidate.cs`:

```csharp
using ModManager.Core;

namespace ManifestMiner;

/// <summary>One reason the curated set cannot be merged safely.</summary>
public sealed record OverrideProblem(string Message);

/// <summary>
/// Pure check over the loaded overrides, run before any merging.
///
/// <para>Overrides are addressed by Steam app id when they have one and by slug otherwise, and either
/// key claimed twice means one file silently loses. That is not theoretical: two files in the real
/// directory both claim Steam id 20920, and today the richer one wins purely by iteration order — if
/// that flipped, the game would drop to nexus-only with no engine and no mod path, and nothing would
/// report it.</para>
///
/// <para>So a duplicate is a BUILD FAILURE rather than a resolved conflict. There is no second key to
/// disambiguate on, and picking a winner is what got us here.</para>
/// </summary>
public static class OverridesValidate
{
    /// <summary>The slug an entry will be addressed by: its explicit id, else one derived from its
    /// name, else empty — which is itself a problem.</summary>
    public static string KeyOf(OverrideEntry entry)
        => !string.IsNullOrWhiteSpace(entry.Id) ? entry.Id!
         : !string.IsNullOrWhiteSpace(entry.Name) ? EnginePresets.Slugify(entry.Name)
         : "";

    public static IReadOnlyList<OverrideProblem> Check(IReadOnlyList<OverrideEntry> overrides)
    {
        var problems = new List<OverrideProblem>();
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static string Where(OverrideEntry e) => e.SourcePath ?? "(unknown file)";

        foreach (var e in overrides.Where(e => KeyOf(e).Length == 0 && string.IsNullOrWhiteSpace(e.SteamAppId)))
            problems.Add(new OverrideProblem(
                $"{Where(e)} has neither an id nor a name, so nothing can address it."));

        void Duplicates(string label, Func<OverrideEntry, string?> keySelector)
        {
            foreach (var group in overrides
                         .Where(e => !string.IsNullOrWhiteSpace(keySelector(e)))
                         .GroupBy(e => keySelector(e)!, StringComparer.OrdinalIgnoreCase)
                         .Where(g => g.Count() > 1))
            {
                // One pair of files is ONE problem even when it collides on both keys - reporting it
                // twice would read as two separate conflicts.
                var files = group.Select(Where).OrderBy(f => f, StringComparer.Ordinal).ToList();
                if (!reported.Add(string.Join("|", files))) continue;

                problems.Add(new OverrideProblem(
                    $"{group.Count()} overrides share the same {label} '{group.Key}': {string.Join(", ", files)}. "
                    + "One would silently win; pick one file and delete the other."));
            }
        }

        Duplicates("Steam app id", e => e.SteamAppId);
        Duplicates("id", e => KeyOf(e) is { Length: > 0 } k ? k : null);

        return problems;
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~OverridesValidateTests"`

Expected: PASS, all seven.

- [ ] **Step 5: Commit**

```bash
git add tools/ManifestMiner/OverridesValidate.cs tests/ModManager.Tests/Miner/OverridesValidateTests.cs
git commit -m "feat(miner): fail the build when two overrides claim one game"
```

---

### Task 4: The merge resolves by Steam id, else by slug

**Files:**
- Modify: `tools/ManifestMiner/OverridesMerge.cs:22-42`
- Test: `tests/ModManager.Tests/Miner/OverridesMergeTests.cs`

**Interfaces:**
- Consumes: `OverrideEntry` (Tasks 1–2), `OverridesValidate.KeyOf` (Task 3).
- Produces: `OverridesMerge.Apply` unchanged in signature; an override with no Steam id now resolves by slug instead of being skipped.

- [ ] **Step 1: Write the failing tests**

Append to `tests/ModManager.Tests/Miner/OverridesMergeTests.cs`:

First widen the file's existing `Backbone` helper so it can express a game with no Steam id. Change
its tuple's `string steamId` to `string? steamId` — one word, and it keeps every existing call working:

```csharp
    private static GameManifest Backbone(params (string id, string? steamId, string? engine)[] games) => new()
```

Then append the tests:

```csharp
    [Fact]
    public void An_override_with_no_Steam_id_adds_an_entry_keyed_by_its_slug()
    {
        // The point of the whole change. The launcher resolves a registered game to its manifest entry
        // by slug (Scanner.cs), so an entry added this way is picked up with no launcher change at all.
        var backbone = Backbone(("skyrim", "72850", null));

        var merged = OverridesMerge.Apply(backbone, new[]
        {
            new OverrideEntry { Id = "some-ea-game", Name = "Some EA Game", Engine = "custom", ModPath = "Mods" },
        });

        var added = Assert.Single(merged.Games, g => g.Id == "some-ea-game");
        Assert.Equal("custom", added.Engine);
        Assert.Equal("Mods", added.ModPath);
        Assert.Null(added.Stores.SteamAppId);
        Assert.Contains("curated", added.Provenance.Sources);
    }

    [Fact]
    public void An_override_with_no_Steam_id_updates_an_existing_entry_with_the_same_slug()
    {
        var backbone = Backbone(("some-ea-game", null, null));

        var merged = OverridesMerge.Apply(backbone, new[]
        {
            new OverrideEntry { Id = "some-ea-game", Engine = "bepinex" },
        });

        Assert.Equal("bepinex", Assert.Single(merged.Games).Engine);   // updated, not duplicated
    }

    [Fact]
    public void The_Steam_id_still_wins_over_the_slug_when_both_could_match()
    {
        // Every one of the 149 existing override files has a Steam id and must keep matching by it.
        // Here the slug points at a DIFFERENT game than the Steam id does; the Steam id is correct.
        var backbone = Backbone(("skyrim", "72850", null), ("some-other-game", "999", null));

        var merged = OverridesMerge.Apply(backbone, new[]
        {
            new OverrideEntry { Id = "some-other-game", SteamAppId = "72850", Engine = "bethesda" },
        });

        Assert.Equal("bethesda", merged.Games.Single(g => g.Id == "skyrim").Engine);
        Assert.Null(merged.Games.Single(g => g.Id == "some-other-game").Engine);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~OverridesMergeTests"`

Expected: FAIL. The first two fail because the merge skips an override with no Steam id.

- [ ] **Step 3: Resolve by Steam id, else by slug**

In `tools/ManifestMiner/OverridesMerge.cs`, replace the loop body:

```csharp
        foreach (var ov in overrides)
        {
            // Steam id first, so all 149 existing files keep matching exactly as they did. Slug second,
            // which is the only key a game bought outside Steam has.
            string? existingId = null;
            if (!string.IsNullOrWhiteSpace(ov.SteamAppId) && idBySteam.TryGetValue(ov.SteamAppId!, out var bySteam))
                existingId = bySteam;
            else if (OverridesValidate.KeyOf(ov) is { Length: > 0 } slug && byId.ContainsKey(slug))
                existingId = slug;

            if (existingId is not null)
            {
                byId[existingId] = ApplyTo(byId[existingId], ov);
                continue;
            }

            var id = OverridesValidate.KeyOf(ov);
            if (id.Length == 0) continue;              // unaddressable; OverridesValidate reports it
            if (byId.ContainsKey(id) && !string.IsNullOrWhiteSpace(ov.SteamAppId))
                id = $"{id}-{ov.SteamAppId}";          // slug taken by a different game
            byId[id] = NewFrom(id, ov);
            order.Add(id);
            if (!string.IsNullOrWhiteSpace(ov.SteamAppId)) idBySteam[ov.SteamAppId!] = id;
        }
```

Update the class summary's first line to: *"Pure: apply curated overrides onto the (backbone + enriched) manifest, keyed by Steam id where there is one and by slug otherwise."*

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~OverridesMergeTests"`

Expected: PASS, including every case that existed before.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add tools/ManifestMiner/OverridesMerge.cs tests/ModManager.Tests/Miner/OverridesMergeTests.cs
git commit -m "feat(miner): resolve an override by slug when it has no Steam id"
```

---

### Task 5: The run fails on a problem, and the docs say why

**Files:**
- Modify: `tools/ManifestMiner/Program.cs:128-143`
- Modify: `docs/superpowers/specs/2026-09-04-non-steam-games-in-the-manifest-design.md`
- Modify (other repo): `626-game-manifest/overrides/README.md`

**Interfaces:**
- Consumes: `OverridesValidate.Check` (Task 3), `OverridesLoader.Load` (Tasks 1–2).
- Produces: nothing downstream; this is the wiring and the documentation.

- [ ] **Step 1: Wire the gate into the run**

In `tools/ManifestMiner/Program.cs`, inside the `--with-overrides` block, immediately after `var overrides = OverridesLoader.Load(overridesDir);`:

```csharp
    // Gate BEFORE merging. A duplicate key means one curated file silently loses, and the merge is
    // where that becomes invisible - so this runs first and stops the run rather than reporting after
    // the damage is baked into the draft.
    var problems = OverridesValidate.Check(overrides);
    if (problems.Count > 0)
    {
        Console.Error.WriteLine($"Overrides: {problems.Count} problem(s) - refusing to merge.");
        foreach (var p in problems) Console.Error.WriteLine($"  {p.Message}");
        return 1;
    }
```

`Program.cs` is a top-level-statements file that already returns an exit code in two places, so `return 1;` compiles as written.

- [ ] **Step 2: Verify the gate catches the live duplicate**

Run:

```bash
cd tools/ManifestMiner
dotnet run -- --with-overrides --overrides-dir ../../../626-game-manifest/overrides
```

Expected: exits non-zero, naming `the-witcher-2-assassins-of-kings.json` and `the-witcher-2-assassins-of-kings-enhanced-edition.json` as sharing Steam app id `20920`.

This is a real finding, not a test fixture. **Do not fix the data in this repo** — it lives in `626-game-manifest` and needs its own PR (Step 3).

- [ ] **Step 3: Fix the live duplicate in the manifest repo**

In `626-game-manifest`, on its own branch:

```bash
cd ../626-game-manifest
git checkout -b fix/duplicate-witcher-2-override
git rm overrides/the-witcher-2-assassins-of-kings-enhanced-edition.json
git commit -m "fix(overrides): one file per game - the Witcher 2 had two

Both claimed Steam app id 20920, which is the merge key, so one silently won by
iteration order. The richer one did, so the output was correct by luck: engine
custom, modPath CookedPC. The deleted file carried nothing the survivor lacks -
same name, same nexusDomain, no engine, no modPath - so had the order ever
flipped, the game would have dropped to nexus-only and nothing would have said so.

Found by the duplicate-key gate the miner now runs before merging."
```

Open a PR. The gate in Step 1 will fail CI until this merges — that is the gate working.

- [ ] **Step 4: Correct the spec's "one line" claim**

In `docs/superpowers/specs/2026-09-04-non-steam-games-in-the-manifest-design.md`, in *Where the constraint actually lives*, replace the heading and first sentence of item 1 with:

```markdown
**1. The miner drops the override — twice.** `OverridesLoader.Load` refuses to load an entry with no
Steam id, and `OverridesMerge.Apply` skips it again if it somehow arrived:

```csharp
// OverridesLoader.Load
if (entry is not null && !string.IsNullOrWhiteSpace(entry.SteamAppId))
    result.Add(entry);

// OverridesMerge.Apply
if (string.IsNullOrWhiteSpace(ov.SteamAppId)) continue;
```

Found while planning the build; the spec originally claimed one line. Everything downstream of both
checks is already keyed by slug.
```

- [ ] **Step 5: Document slug-keying for contributors**

In `626-game-manifest/overrides/README.md`, replace the first paragraph's key sentence and the format note:

```markdown
Hand-curated corrections that **win over** mined data. One `<game>.json` per game. The **Steam app id
is the key when the game has one**; a game sold outside Steam — the EA app, Epic, GOG — is keyed by its
`id` slug instead. Exactly one of the two is required.

The build **fails** if two files share an `id` or a Steam app id: one would silently win, and picking a
winner is not something a build should do quietly.
```

- [ ] **Step 6: Run the whole suite and the miner**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

Expected: PASS.

Then, once the Step 3 PR has merged:

```bash
cd tools/ManifestMiner
dotnet run -- --with-overrides --overrides-dir ../../../626-game-manifest/overrides
```

Expected: exit 0, and the `Overrides: N loaded` line reports the same curated count as before the change.

- [ ] **Step 7: Commit**

```bash
git add tools/ManifestMiner/Program.cs docs/superpowers/specs/2026-09-04-non-steam-games-in-the-manifest-design.md
git commit -m "feat(miner): refuse to merge a colliding override set"
```

---

### Task 6: Prove it end to end with a real non-Steam game

**Files:**
- Create (other repo): `626-game-manifest/overrides/ea-sports-college-football-27.json`
- Modify: `docs/smoke-tests/pending.md`

**Interfaces:**
- Consumes: everything above.
- Produces: the first curated non-Steam game, and a smoke record of the property.

- [ ] **Step 1: Write the override**

Create `626-game-manifest/overrides/ea-sports-college-football-27.json`:

```json
{
  "id": "ea-sports-college-football-27",
  "name": "EA SPORTS College Football 27",
  "nexusDomain": null,
  "banRisk": "high",
  "safeRoute": "offline",
  "safeRouteHint": "EA's user agreement prohibits modifying their games. Offline single-player modding is widely tolerated; online modes are not. This game ships EA's kernel-level anti-cheat and the launcher cannot turn it off."
}
```

**No `engine` and no `modPath`, deliberately.** The game is Frostbite — its mods are `.fbmod` files applied through FrostyModManager, which is not a file-drop layout the launcher speaks. Claiming an engine would make the launcher offer a mod folder that does nothing. The entry exists to describe the game honestly, not to pretend it is supported.

- [ ] **Step 2: Verify the miner accepts it**

```bash
cd tools/ManifestMiner
dotnet run -- --with-overrides --overrides-dir ../../../626-game-manifest/overrides
python -c "import json;m=json.load(open('out/manifest-draft.json'));g=[x for x in m['games'] if x['id']=='ea-sports-college-football-27'];print(g[0] if g else 'MISSING')"
```

Expected: the entry is present, with `banRisk` `high`, `safeRoute` `offline`, a null engine, and no `steamAppId` under `stores`.

- [ ] **Step 3: Record the smoke case**

Append to `docs/smoke-tests/pending.md`:

```markdown

---

## A game that is not on Steam can be curated

The first game in the feed with no Steam app id. Before this, `OverridesLoader` refused to load it and
`OverridesMerge` refused to merge it — so an EA-app, Epic or GOG game could not be described at all,
not even wrongly.

1. Add the game by hand in the launcher, naming it so its slug matches the manifest entry —
   for `ea-sports-college-football-27`, type **EA SPORTS College Football 27**.
2. Confirm the game picks up its curated facts: the ban-risk chip appears, and the safe-route sentence
   is the one from the manifest rather than a generic warning.
3. Confirm it offers **no** mod folder and **no** anti-cheat toggle. Both are correct: the entry
   deliberately declares no engine, and the launcher's toggle is for Easy Anti-Cheat's bootstrapper
   swap, which is not what EA's kernel-level anti-cheat is.

**The slug join is the fragile part of this case.** A registered game's id is `Slugify(whatever you
typed)`, so a typo means the curation silently does not apply and the game looks uncurated. Until the
add path sets the id explicitly (spec C5), check the id in `games.json` if the facts do not appear.
```

- [ ] **Step 4: Commit both repos**

```bash
cd ../626-game-manifest
git checkout -b data/ea-sports-college-football-27
git add overrides/ea-sports-college-football-27.json
git commit -m "data(games): EA SPORTS College Football 27, the first non-Steam entry

No engine and no modPath on purpose. Frostbite mods are .fbmod files applied
through FrostyModManager, not a file-drop layout the launcher speaks, so claiming
an engine would offer a mod folder that does nothing. The entry exists to describe
the game honestly - high ban risk, a documented offline route, and no toggle,
because EA's anti-cheat is a kernel service rather than a bootstrapper the game
launches through."

cd ../626-mod-launcher
git add docs/smoke-tests/pending.md
git commit -m "docs(smoke): a game that is not on Steam can be curated"
```

---

## Self-review

**Spec coverage.** C1 is Tasks 1, 2 and 4. C2 is Tasks 3 and 5. C3, C4 and C5 are explicitly out of phase 1 and named in *Scope*. Task 6 exists because a change nothing exercises is a change nobody can trust.

**Placeholders.** None. Every code step carries the code; every run step carries the command and the expected result.

**Type consistency.** `OverrideEntry.SteamAppId` is `string?` from Task 1 and used as nullable in Tasks 3 and 4. `SourcePath` is introduced in Task 2 and consumed in Task 3. `OverridesValidate.KeyOf` is defined in Task 3 and used in Task 4's merge. `OverrideProblem.Message` is the only field the tests and `Program.cs` read.

**One risk the plan carries deliberately.** Task 5 Step 2 makes the miner fail against the real overrides directory until the Step 3 PR merges in the other repo. That ordering is intentional — the gate proving itself against a live duplicate is worth more than a green run — but the two repos must land in that order.
