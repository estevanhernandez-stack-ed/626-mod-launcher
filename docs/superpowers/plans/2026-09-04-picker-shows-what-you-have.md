# The picker shows what you have Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Add Game picker offer every curated game, with the ones installed on this machine at the top — so a curated game can actually be found and picked.

**Architecture:** `PopularGames` currently projects only entries carrying a legacy `popular-games` provenance tag: 18 of 156. Widen it to every entry the projection can actually represent (one with an engine and a mod path), and make its Steam app id nullable so a non-Steam game does not break it. The App then ranks the games it can see installed to the top, because "what is installed" is a machine fact the pure layer must not reach for.

**Tech Stack:** .NET 10, C#, xUnit. `src/ModManager.Core` (pure, tested), `src/ModManager.App` (WinUI 3).

**Spec:** `docs/superpowers/specs/2026-09-04-non-steam-games-in-the-manifest-design.md` — section **C4**.

## Global Constraints

- **No WinUI / WinRT types in `src/ModManager.Core/`.** `CorePurityTests` enforces it.
- **Nullable enabled, warnings are errors.**
- **NEVER run bare `dotnet test` or `dotnet build` at the repo root.** Use `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` and `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`.
- **Close any running `ModManager.App.exe` before building.** It holds `ModManager.Core.dll` and the build fails on a file lock.
- **No XAML edit is required by this plan.** If you find yourself editing a `.xaml` file, stop and report — the control stays a `ComboBox`; only its contents and their order change.
- **Conventional commits**, area `intake` or `manifest`.

## The decision this plan implements, and what it rejects

C4 as originally written said the picker shows every curated game **installed** on this machine. Applied literally that hides Minecraft — and Minecraft is the entry curated specifically to prove non-Steam games work.

To filter by installed you must detect installed. For Steam that is easy (read `appmanifest_*.acf`). For everything else **there is no signal**: the manifest has no field naming where a game installs. `saveDirHint` names where it *saves*, a different folder. Minecraft's game root is `%APPDATA%\.minecraft`, under no launcher's install root at all, so a folder probe of `Program Files\EA Games` and friends would not find it either.

Rejected alternatives:

- **Add an install hint to the manifest.** A new schema field, a curation burden on all 156 entries, and it only helps games somebody remembers to fill in. Keeps its value as a later option, not a prerequisite.
- **Filter to installed anyway.** Ships a picker that cannot show the game the previous phase existed to prove.

**Chosen: show everything, rank what we can see installed to the top.** The games you own surface where you expect them; the rest stay findable by typing. Ranking is a hint, so a game we fail to detect is merely lower in the list rather than absent — the failure direction that costs a scroll instead of a capability.

## File Structure

| File | Responsibility |
|---|---|
| `src/ModManager.Core/PopularGames.cs` | modify — widen the projection; `SteamAppId` becomes nullable |
| `src/ModManager.App/AddGameDialog.xaml.cs` | modify — rank installed first before binding |
| `tests/ModManager.Tests/PopularGamesTests.cs` | modify — the old tests encode the legacy list |
| `docs/superpowers/specs/2026-09-04-non-steam-games-in-the-manifest-design.md` | modify — record the decision under C4 |
| `docs/smoke-tests/pending.md` | modify — the case a harness cannot cover |

---

### Task 1: The catalogue offers every game it can represent

**Files:**
- Modify: `src/ModManager.Core/PopularGames.cs`
- Test: `tests/ModManager.Tests/PopularGamesTests.cs`

**Interfaces:**
- Consumes: `EffectiveManifest.Current`, `GameManifestEntry`.
- Produces: `PopularGame.SteamAppId` is now `string?` (was `string`). `PopularGames.All` returns every manifest entry that has both a non-null `Engine` and a non-null `ModPath`, ordered by `Featured` ascending with unfeatured entries after, then by `Name`. `PopularGames.Find(string?)` is unchanged.

- [ ] **Step 1: Write the failing tests**

Replace the body of `tests/ModManager.Tests/PopularGamesTests.cs` with this class, keeping the file's existing `using` lines and namespace:

```csharp
/// <summary>
/// The Add Game quick-pick catalogue.
///
/// <para>It used to project only entries carrying the legacy <c>popular-games</c> provenance tag — 18
/// of 156 — which made a newly curated game invisible in the one surface built for finding a curated
/// game. For a Steam title that went unnoticed, because Steam detection finds it anyway. For a game
/// sold anywhere else the picker is the ONLY route, so "curated but unfindable" was the whole of the
/// user's experience of it.</para>
///
/// <para>It now offers every entry the projection can actually represent: one with an engine and a mod
/// path. An entry missing either cannot become a <see cref="PopularGame"/> without inventing values,
/// and inventing a mod path is how files land somewhere a loader never looks.</para>
/// </summary>
public class PopularGamesTests
{
    [Fact]
    public void Every_entry_with_an_engine_and_a_mod_path_is_offered()
    {
        var offered = PopularGames.All.Select(g => g.Id).ToHashSet();
        var representable = EffectiveManifest.Current.Games
            .Where(g => g.Engine is not null && g.ModPath is not null)
            .Select(g => g.Id);

        Assert.All(representable, id => Assert.Contains(id, offered));
    }

    [Fact]
    public void An_entry_with_no_engine_or_no_mod_path_is_left_out()
    {
        // Not a filter for tidiness: PopularGame's Engine and ModPath are non-nullable, so an entry
        // missing either could only be projected by inventing a value. Inventing a mod path is how
        // files land somewhere the loader never looks.
        var offered = PopularGames.All.Select(g => g.Id).ToHashSet();

        foreach (var g in EffectiveManifest.Current.Games.Where(g => g.Engine is null || g.ModPath is null))
            Assert.DoesNotContain(g.Id, offered);
    }

    [Fact]
    public void The_legacy_tag_no_longer_decides_membership()
    {
        // The tag reproduced a hand-written array's contents. Keeping it as the gate meant a curated
        // game was invisible until somebody remembered to tag it.
        var offered = PopularGames.All.Count;
        var tagged = EffectiveManifest.Current.Games
            .Count(g => g.Provenance.Sources.Contains(ManifestSources.PopularGames));

        Assert.True(offered > tagged, $"offered {offered} should exceed the {tagged} legacy-tagged");
    }

    [Fact]
    public void Featured_games_come_first_in_their_stated_order()
    {
        var featured = PopularGames.All
            .Select((g, i) => (g.Id, i))
            .Join(EffectiveManifest.Current.Games.Where(m => m.Featured is not null),
                  x => x.Id, m => m.Id, (x, m) => (x.i, rank: m.Featured!.Value))
            .OrderBy(x => x.rank)
            .ToList();

        // Their positions in the list must ascend with their featured rank, and all must precede
        // anything unfeatured.
        Assert.Equal(featured.OrderBy(x => x.i).Select(x => x.i), featured.Select(x => x.i));
        var firstUnfeatured = PopularGames.All
            .Select((g, i) => (g.Id, i))
            .Where(x => EffectiveManifest.Current.Games.First(m => m.Id == x.Id).Featured is null)
            .Select(x => x.i)
            .DefaultIfEmpty(int.MaxValue)
            .Min();
        Assert.All(featured, x => Assert.True(x.i < firstUnfeatured));
    }

    [Fact]
    public void A_game_with_no_Steam_id_is_offered_and_carries_a_null_id()
    {
        // Minecraft is the case this exists for: curated, moddable, and on no store the projection
        // used to be able to express. The old code forced g.Stores.SteamAppId! into a non-nullable
        // field, so the one game that proves non-Steam support was the one it could not represent.
        var offered = PopularGames.All.ToList();

        Assert.All(offered.Where(g => g.SteamAppId is not null),
                   g => Assert.False(string.IsNullOrWhiteSpace(g.SteamAppId)));
        // Any entry curated without a Steam id must still be offered, with a null id rather than "".
        foreach (var m in EffectiveManifest.Current.Games
                     .Where(m => m.Engine is not null && m.ModPath is not null && m.Stores.SteamAppId is null))
            Assert.Null(offered.Single(g => g.Id == m.Id).SteamAppId);
    }

    [Fact]
    public void Find_still_resolves_by_id_and_returns_null_for_an_unknown_one()
    {
        var any = PopularGames.All[0];

        Assert.Equal(any.Id, PopularGames.Find(any.Id)!.Id);
        Assert.Null(PopularGames.Find("not-a-real-game"));
        Assert.Null(PopularGames.Find(null));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~PopularGamesTests"`

Expected: FAIL. `Every_entry_with_an_engine_and_a_mod_path_is_offered` fails because only tagged entries are offered.

- [ ] **Step 3: Widen the projection and make the Steam id nullable**

In `src/ModManager.Core/PopularGames.cs`, change the record's `SteamAppId` to nullable:

```csharp
public sealed record PopularGame(
    string Id,
    string Name,
    string Engine,
    string ModPath,
    string? SteamAppId)
```

and update the record's summary to say the Steam app id is null for a game not sold on Steam.

Then replace `Build()`:

```csharp
    private static IReadOnlyList<PopularGame> Build()
        // Every entry the projection can actually represent, not just the legacy-tagged 18. The tag
        // reproduced a hand-written array, so a newly curated game stayed invisible in the one surface
        // built for finding curated games — and for a game sold outside Steam, where no detection
        // exists, that was the whole of the user's experience of it.
        //
        // Engine and ModPath are the real gate, and not for tidiness: they are non-nullable on
        // PopularGame, so an entry missing either could only be projected by inventing a value.
        // Inventing a mod path is how files land somewhere a loader never looks.
        => EffectiveManifest.Current.Games
            .Where(g => g.Engine is not null && g.ModPath is not null)
            .OrderBy(g => g.Featured ?? int.MaxValue)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new PopularGame(g.Id, g.Name, g.Engine!, g.ModPath!, g.Stores.SteamAppId)
            {
                FileExtensions = g.FileExtensions,
            })
            .ToList();
```

Note `g.Stores.SteamAppId` no longer carries `!` — it is legitimately null for a game not on Steam.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~PopularGamesTests"`

Expected: PASS, all six.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

Expected: PASS. Other tests reference `PopularGames` — `FacadeRemoteWiringTests` and `PublishManifestTests`. If either fails, read it: a test asserting the legacy membership encodes the behaviour being deliberately changed and should be updated to the new rule; a test asserting remote-manifest wiring is checking something else and a failure there is a real regression. **Report which you found.**

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/PopularGames.cs tests/ModManager.Tests/PopularGamesTests.cs
git commit -m "feat(manifest): the quick-pick catalogue offers every curated game"
```

---

### Task 2: Installed games come first

**Files:**
- Modify: `src/ModManager.App/AddGameDialog.xaml.cs:79`
- Test: none — App-side WinUI, headless-untestable; Task 3 records the smoke case

**Interfaces:**
- Consumes: `PopularGames.All` from Task 1, and the dialog's existing installed-Steam-game list.
- Produces: nothing later tasks build on.

- [ ] **Step 1: Find what the dialog already knows about installed games**

Read `src/ModManager.App/AddGameDialog.xaml.cs` around the constructor. It builds Steam add rows and setup rows from a list of installed games, and `OnPopularSelected` already calls `InstalledGameMatch.ByAppId(_installedGames, …)`. Note the exact field name and element type — you need its Steam app ids. **Report what you found.**

- [ ] **Step 2: Rank installed first when binding**

Replace `PopularGamesBox.ItemsSource = PopularGames.All;` at line 79 with a ranked copy. Use the field name you found in Step 1 in place of `_installedGames`:

```csharp
        // Everything curated is offered, but the games on THIS machine come first — the list is 116
        // entries and a user is looking for one of the handful they own. Ranking rather than filtering
        // is deliberate: a game we cannot detect (anything not sold on Steam, which has no install
        // signal we can read) is merely lower down instead of absent, so a detection miss costs a
        // scroll rather than the capability.
        var installedAppIds = _installedGames.Select(g => g.AppId).ToHashSet(StringComparer.Ordinal);
        PopularGamesBox.ItemsSource = PopularGames.All
            .OrderByDescending(g => g.SteamAppId is not null && installedAppIds.Contains(g.SteamAppId))
            .ToList();
```

`OrderByDescending` on a bool puts `true` first, and LINQ's ordering is stable, so the featured-then-name order from Task 1 survives within each group.

- [ ] **Step 3: Build the app**

Close any running `ModManager.App.exe` first, then run:

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: `Build succeeded. 0 Error(s)`. No XAML changed, so no `obj/` deletion is needed.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/AddGameDialog.xaml.cs
git commit -m "feat(intake): the games you have come first in the picker"
```

---

### Task 3: Record the decision and the smoke case

**Files:**
- Modify: `docs/superpowers/specs/2026-09-04-non-steam-games-in-the-manifest-design.md`
- Modify: `docs/smoke-tests/pending.md`

**Interfaces:**
- Consumes: everything above. Produces: nothing.

- [ ] **Step 1: Record the decision in the spec**

In `docs/superpowers/specs/2026-09-04-non-steam-games-in-the-manifest-design.md`, find the C4 section (`### C4. The catalog stops assuming a Steam id`) and append this to it:

```markdown
**Amended 2026-09-04, after trying to build it.** C4 originally said the picker shows every curated game
*installed* on this machine. Applied literally that hides Minecraft — the entry curated specifically to
prove non-Steam games work.

Filtering by installed requires detecting installed. Steam is easy (`appmanifest_*.acf`). Everything
else has **no signal**: the manifest has no field naming where a game installs, and `saveDirHint` names
where it *saves*. Minecraft's game root is `%APPDATA%\.minecraft`, under no launcher's install root, so
a folder probe of the conventional roots would not find it either.

So: **every curated game is offered, and the ones we can see installed are ranked to the top.** Ranking
rather than filtering means a game we fail to detect is lower in the list rather than absent — a
detection miss costs a scroll instead of a capability. An install-hint field in the manifest stays a
later option rather than a prerequisite for this.
```

- [ ] **Step 2: Record the smoke case**

Append to `docs/smoke-tests/pending.md`:

```markdown

---

## The picker offers curated games, with yours first

The Add Game quick-pick used to list only entries carrying a legacy `popular-games` tag — 18 of 156.
A newly curated game was invisible in the one surface built for finding curated games.

1. **Open + Game and look at the picker.** It should list far more than the old 18, with the games
   installed on this machine at the top and the featured ones leading those.
2. **Minecraft must be in it.** It is curated, has no Steam app id, and was previously impossible for
   this list to represent — the old projection forced a non-null Steam id. Pick it and confirm the
   engine fills in as `minecraft` and the mod path as `mods`. The folder still has to be browsed to
   `%APPDATA%\.minecraft`, because nothing tells the launcher where Minecraft installs.
3. **A picked game keeps its curation.** After picking, add it and check `games.json`: the registered
   id must be the manifest id (`minecraft`), not a slug of the display name.
4. **A game we cannot detect is lower, never missing.** Ranking is a hint, not a filter — scroll or
   type and every curated game is still there.

*Known and deliberate: a game sold outside Steam is never ranked as installed, because there is no
install signal to read. It sits with the uninstalled ones and is found by typing.*
```

- [ ] **Step 3: Verify nothing asserts on these files**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SmokeCatalogue"`

Expected: PASS. Those tests guard `smoke.json`, not the prose.

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-09-04-non-steam-games-in-the-manifest-design.md docs/smoke-tests/pending.md
git commit -m "docs: the picker offers every curated game, ranked by what you have"
```

---

## Self-review

**Spec coverage.** C4's two halves — stop assuming a Steam id, and widen what the picker shows — are Task 1 and Task 2. The amendment in Task 3 records where the spec's original wording could not survive contact with Minecraft.

**Placeholders.** None. Tasks 1 and 3 carry their full content. Task 2's Steps 1 and 2 ask the implementer to read one field name out of existing code and use it — a real lookup, with the surrounding code given.

**Type consistency.** `PopularGame.SteamAppId` becomes `string?` in Task 1 and is null-checked in Task 2's ranking predicate. `PopularGames.All` returns `IReadOnlyList<PopularGame>` throughout.

**One risk carried deliberately.** Widening the list from 18 to ~116 in a plain `ComboBox` makes it long. Installed-first ordering plus the control's built-in type-ahead is judged sufficient; converting it to a filter box and list — the pattern the Steam picker already uses in the same dialog — is a follow-up if the list proves unwieldy in use, not part of this plan.
