# The explicit slug join Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the user picks a game the launcher already knows, record WHICH game it is — so its curated engine, mod path, save layout and ban risk actually reach it.

**Architecture:** `GameInput.Id` already exists and `BuildGameEntry` already prefers it over the name. Nothing populates it. Two call sites learn to: the Steam quick-pick plan resolves the manifest id from the app id, and the Add Game dialog carries the id of a game picked from the catalogue. Manual typing is unchanged and still derives the id from the name, because there is nothing better to derive it from.

**Tech Stack:** .NET 10, C#, xUnit. `src/ModManager.Core` (pure, tested), `src/ModManager.App` (WinUI 3 shell).

**Spec:** `docs/superpowers/specs/2026-09-04-non-steam-games-in-the-manifest-design.md` — section **C5** only.

## Global Constraints

- **No WinUI / WinRT types in `src/ModManager.Core/`.** `CorePurityTests` fails the suite if they leak in.
- **Nullable enabled, warnings are errors** (`Directory.Build.props`).
- **NEVER run bare `dotnet test` or `dotnet build` at the repo root** — the WinUI project hangs it. Use `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj` and `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`.
- **Delete `obj/` and `bin/` after any XAML edit**, and close the running app before building. Stale generated code makes the app die at `Connect()` with an `InvalidCastException`.
- **Manual typing must keep working exactly as it does now** — an id derived from the name, with no manifest lookup and no new failure mode.
- **Conventional commits**, area `intake` or `manifest`.

## Why this is worth doing — measured, not argued

The dialog prefills a game's **display name**, and the registered id is that name slugified. Against the real manifest ids today:

| name the picker prefills | slug produced | manifest id | |
|---|---|---|---|
| `Minecraft: Java Edition` | `minecraft-java-edition` | `minecraft` | **misses** |
| `The Witcher 2: Assassins of Kings Enhanced Edition` | `…-enhanced-edition` | `the-witcher-2-assassins-of-kings` | **misses** |
| `EA SPORTS College Football 27` | matches | matches | ok |
| `Crime Simulator` | matches | matches | ok |
| `How to Fish` | matches | matches | ok |

Two of five lose **all** curation — engine, mod path, save layout, the player seam, the ban risk — and nothing reports it. One of them is Minecraft, curated hours ago specifically to prove non-Steam games work.

## What is already done, and needs no task

- **`GameInput.Id` exists** (`GameEntry.cs:153`) and `EnginePresets.BuildGameEntry` already does `Slugify(input.Id ?? input.Name)`. The plumbing is there; only the callers are silent.
- **The duplicate-add guard exists.** `LauncherService.AddGame` calls `Registry.FindRegistered(reg, input.GameRoot, input.SteamAppId)` and switches to the existing game. The spec's C5 asked for a decision on colliding adds; that decision ("refuse") is already implemented for the case that matters. `UniqueId`'s `-2` suffix now only fires for two genuinely different games sharing a slug, which is correct. `marvel-s-spider-man-2-2` on the rig is a fossil of the old behaviour, not a live bug.

## File Structure

| File | Responsibility |
|---|---|
| `src/ModManager.Core/ManifestIdLookup.cs` | **create** — pure: which manifest entry does a Steam app id name? |
| `src/ModManager.Core/SteamGameImport.cs` | modify — the plan carries the manifest id |
| `src/ModManager.App/AddGameDialog.xaml.cs` | modify — remember a picked game's id; carry it into `BuildInput` |
| `tests/ModManager.Tests/ManifestIdLookupTests.cs` | **create** |
| `tests/ModManager.Tests/SteamGameImportTests.cs` | modify — the plan's id |
| `docs/smoke-tests/pending.md` | modify — the case a harness cannot cover |

---

### Task 1: Which manifest entry does a Steam app id name?

**Files:**
- Create: `src/ModManager.Core/ManifestIdLookup.cs`
- Test: `tests/ModManager.Tests/ManifestIdLookupTests.cs`

**Interfaces:**
- Consumes: `ModManager.Core.Manifest.GameManifest`, `GameManifestEntry`, `StoreIds`.
- Produces: `static string? ManifestIdLookup.BySteamAppId(GameManifest? manifest, string? steamAppId)` — the entry's `Id`, or null when there is no manifest, no app id, or no match.

- [ ] **Step 1: Write the failing tests**

Create `tests/ModManager.Tests/ManifestIdLookupTests.cs`:

```csharp
using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests;

/// <summary>
/// Naming which curated game a Steam app id refers to.
///
/// <para>A registered game's id is what the launcher joins on to find its curated engine, mod path and
/// ban risk (<c>Scanner.cs</c>). Until now that id came from slugifying whatever display name the user
/// happened to have in the box — so "Minecraft: Java Edition" produced <c>minecraft-java-edition</c>
/// and matched the <c>minecraft</c> entry not at all, silently discarding every curated fact about it.
/// This is the lookup that lets the add path say WHICH game instead of guessing from a name.</para>
/// </summary>
public class ManifestIdLookupTests
{
    private static GameManifest M(params (string Id, string? Steam)[] games) => new()
    {
        Games = games.Select(g => new GameManifestEntry
        {
            Id = g.Id,
            Name = g.Id,
            Stores = new StoreIds { SteamAppId = g.Steam },
        }).ToList(),
    };

    [Fact]
    public void A_known_app_id_names_its_entry()
        => Assert.Equal("elden-ring", ManifestIdLookup.BySteamAppId(M(("elden-ring", "1245620")), "1245620"));

    [Fact]
    public void An_unknown_app_id_names_nothing()
        => Assert.Null(ManifestIdLookup.BySteamAppId(M(("elden-ring", "1245620")), "999999"));

    [Fact]
    public void An_entry_with_no_Steam_id_is_never_matched_by_one()
    {
        // Minecraft has no Steam id at all. Asking by app id must not reach it - the only honest
        // answer is "no match", which leaves the caller on the name-derived id.
        Assert.Null(ManifestIdLookup.BySteamAppId(M(("minecraft", null)), "1245620"));
    }

    [Fact]
    public void No_manifest_and_no_app_id_are_both_just_null()
    {
        // Runs on every Steam import, including on a machine whose manifest never loaded.
        Assert.Null(ManifestIdLookup.BySteamAppId(null, "1245620"));
        Assert.Null(ManifestIdLookup.BySteamAppId(M(("elden-ring", "1245620")), null));
        Assert.Null(ManifestIdLookup.BySteamAppId(M(("elden-ring", "1245620")), "   "));
    }

    [Fact]
    public void The_first_entry_wins_when_two_claim_one_app_id()
    {
        // The feed's build gate refuses a duplicate app id, so this should not reach a user. Pinned
        // anyway because "whatever the enumeration happened to reach first" is exactly the silent
        // behaviour that gate exists to stop, and a lookup should not reintroduce it by accident.
        Assert.Equal("first", ManifestIdLookup.BySteamAppId(M(("first", "111"), ("second", "111")), "111"));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManifestIdLookupTests"`

Expected: FAIL to COMPILE — `The name 'ManifestIdLookup' does not exist in the current context`.

- [ ] **Step 3: Write the lookup**

Create `src/ModManager.Core/ManifestIdLookup.cs`:

```csharp
using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// Naming which curated game a store identifier refers to.
///
/// <para>A registered game joins to its manifest entry by ID (<c>Scanner.GameContext</c>), and that id
/// used to come from slugifying whatever display name was in the wizard's box. When the two disagreed
/// — "Minecraft: Java Edition" against the <c>minecraft</c> entry — every curated fact about the game
/// was silently discarded, with nothing reported. This lets the add path state which game it is instead
/// of inferring it from a name somebody typed.</para>
///
/// <para>Returning null is a normal answer, not a failure: a game outside the manifest, a machine whose
/// feed never loaded, a game with no Steam id. The caller falls back to the name-derived id, which is
/// exactly today's behaviour.</para>
/// </summary>
public static class ManifestIdLookup
{
    public static string? BySteamAppId(GameManifest? manifest, string? steamAppId)
    {
        if (manifest is null || string.IsNullOrWhiteSpace(steamAppId)) return null;
        return manifest.Games
            .FirstOrDefault(g => string.Equals(g.Stores.SteamAppId, steamAppId, StringComparison.Ordinal))
            ?.Id;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~ManifestIdLookupTests"`

Expected: PASS, all five.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/ManifestIdLookup.cs tests/ModManager.Tests/ManifestIdLookupTests.cs
git commit -m "feat(manifest): name which curated game a Steam app id refers to"
```

---

### Task 2: The Steam quick-pick plan carries the manifest id

**Files:**
- Modify: `src/ModManager.Core/SteamGameImport.cs:39-46`
- Test: `tests/ModManager.Tests/SteamGameImportTests.cs`

**Interfaces:**
- Consumes: `ManifestIdLookup.BySteamAppId(GameManifest?, string?)` from Task 1.
- Produces: `SteamGameImport.Plan` returns a `SteamImportPlan` whose `Input.Id` is the manifest id when the app id is known, and null otherwise. The signature does not change.

- [ ] **Step 1: Write the failing tests**

Append to `tests/ModManager.Tests/SteamGameImportTests.cs`:

```csharp
    [Fact]
    public void The_plan_carries_the_manifest_id_so_curation_reaches_the_game()
    {
        // Without this the registered id is Slugify(the Steam display name), which for a game whose
        // manifest id differs - "Minecraft: Java Edition" vs "minecraft" - matches nothing and throws
        // away the engine, mod path, save layout and ban risk that were curated for it.
        var plan = SteamGameImport.Plan(
            new SteamImportCandidate("1245620", "ELDEN RING", @"C:\games\EldenRing"), "fromsoft");

        Assert.True(plan.Addable);
        Assert.Equal("elden-ring", plan.Input!.Id);
    }

    [Fact]
    public void A_game_the_manifest_does_not_know_carries_no_id_and_falls_back_to_its_name()
    {
        // Not an error. BuildGameEntry does Slugify(Id ?? Name), so a null id is exactly today's
        // behaviour - the change only ever ADDS certainty, never removes the fallback.
        var plan = SteamGameImport.Plan(
            new SteamImportCandidate("999999", "Some Unmined Game", @"C:\games\Unmined"), "bepinex");

        Assert.Null(plan.Input!.Id);
    }
```

`SteamGameImportTests` may already have a helper for building a candidate; if so use it rather than adding a second. If the existing tests call `Plan` with a different argument shape than shown, match theirs — the two arguments are the candidate and the detected engine.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SteamGameImportTests"`

Expected: FAIL — `Assert.Equal() Failure: Expected: elden-ring, Actual: (null)`.

If instead it fails because the embedded manifest has no `elden-ring` entry at app id `1245620`, check the real value with:

```bash
python -c "import json;m=json.load(open('src/ModManager.Core/Manifest/games-manifest.json',encoding='utf-8'));print([(g['id'],(g.get('stores') or {}).get('steamAppId')) for g in m['games'] if g['id']=='elden-ring'])"
```

and use whatever pairing that prints. Do not change the manifest to suit the test.

- [ ] **Step 3: Carry the id into the plan**

In `src/ModManager.Core/SteamGameImport.cs`, in the `new GameInput { … }` initializer, add `Id` as the first property:

```csharp
        var input = new GameInput
        {
            // WHICH curated game this is, rather than letting BuildGameEntry infer it from the display
            // name. Slugify(name) and the manifest id agree by luck, not by rule: "Minecraft: Java
            // Edition" produces minecraft-java-edition and matches the `minecraft` entry not at all,
            // which silently discards every curated fact about the game. Null when the manifest does
            // not know this app id, which leaves the name-derived fallback exactly as it was.
            Id = ManifestIdLookup.BySteamAppId(Manifest.EffectiveManifest.Current, game.AppId),
            Name = game.Name,
            Engine = engine,
            GameRoot = game.GameRoot,
            SteamAppId = game.AppId,
            ModPath = modPath,
        };
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SteamGameImportTests"`

Expected: PASS, including every case that existed before.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

Expected: PASS. `CorePurityTests` must stay green — `ManifestIdLookup` and `EffectiveManifest` are both Core, so nothing platform-specific has been introduced.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/SteamGameImport.cs tests/ModManager.Tests/SteamGameImportTests.cs
git commit -m "feat(intake): a Steam quick-add says which curated game it is"
```

---

### Task 3: The dialog remembers a picked game's id

**Files:**
- Modify: `src/ModManager.App/AddGameDialog.xaml.cs` — the `OnPopularSelected` handler, `BuildInput()` at ~463, and the Steam add row path
- Test: none — this is App-side WinUI and headless-untestable; Task 4 records the smoke case

**Interfaces:**
- Consumes: `PopularGame.Id` (already present on that record), and `GameInput.Id` from Task 2's usage.
- Produces: nothing later tasks build on.

- [ ] **Step 1: Add the field that remembers the pick**

In `src/ModManager.App/AddGameDialog.xaml.cs`, beside the existing `_appliedDraft` field (~line 27):

```csharp
    /// <summary>The manifest id of a game the user PICKED from the catalogue, as opposed to typed.
    ///
    /// <para>It is carried separately from the visible fields for the same reason <see
    /// cref="_appliedDraft"/> is: there is no control for it, and inferring it from the name box is
    /// precisely the bug this exists to fix. Null on a manual add, which leaves
    /// <c>BuildGameEntry</c> deriving the id from the name exactly as before.</para></summary>
    private string? _pickedManifestId;
```

- [ ] **Step 2: Set it when the user picks from the popular list**

In `OnPopularSelected`, immediately after the `if (PopularGamesBox.SelectedItem is not PopularGame g) return;` guard:

```csharp
        // Record WHICH game was picked before touching any text box. The name box is about to be
        // overwritten with the display name, and Slugify of that name is not reliably this game's id.
        _pickedManifestId = g.Id;
```

- [ ] **Step 3: Set it when the user picks a Steam quick-add row**

The Steam quick-pick path already receives a `GameInput` from `SteamGameImport.Plan` carrying the id (Task 2). Find where a `SteamAddRow`'s `Input` is used to add the game and confirm that `GameInput` reaches `LauncherService.AddGame` unmodified — if it is passed through directly, there is nothing to do here and no code change belongs in this step. If instead the dialog rebuilds a `GameInput` from the text boxes for that path, set `_pickedManifestId = plan.Input.Id;` at the point the row is selected, the same as Step 2.

Report in your task report which of the two it turned out to be.

- [ ] **Step 4: Carry it into the assembled input**

In `BuildInput()` (~line 463), add `Id` as the first property of the initializer:

```csharp
    public GameInput BuildInput() => new()
    {
        // Set only when the user PICKED a catalogued game; null when they typed one, which leaves
        // BuildGameEntry deriving the id from the name as it always has.
        Id = _pickedManifestId,
        Name = NameBox.Text.Trim(),
```

- [ ] **Step 5: Build the app**

Run:
```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: `Build succeeded. 0 Error(s)`. No XAML was edited, so no `obj/` deletion is needed.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/ModManager.App/AddGameDialog.xaml.cs
git commit -m "feat(intake): picking a catalogued game records which one it is"
```

---

### Task 4: Record the smoke case

**Files:**
- Modify: `docs/smoke-tests/pending.md`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

- [ ] **Step 1: Append the case**

Append to `docs/smoke-tests/pending.md`:

```markdown

---

## Picking a curated game actually gets you its curation

A registered game finds its curated engine, mod path, save layout and ban risk by **id**. That id used
to be `Slugify(whatever display name was in the box)`, and it matched the manifest only when the
curator happened to name the entry the same way. Measured against the real feed, **two of five sampled
entries missed** — including Minecraft, curated specifically to prove non-Steam games work.

1. **The case that used to fail.** Add Minecraft. Its manifest entry is `minecraft` while its display
   name is `Minecraft: Java Edition`, which slugifies to `minecraft-java-edition`. Check `games.json`:
   the registered id must be **`minecraft`**, and the game must arrive with engine `minecraft`, mod
   path `mods`, and its save layout — not as an uncurated `custom` game.
2. **Typing still works the old way.** Add a game by typing a name with no pick. Its id is still
   derived from the name; nothing looks it up and nothing fails. This path is unchanged on purpose —
   there is nothing better than the name to derive an id from.
3. **A game the manifest has never heard of.** Quick-add any installed Steam game with no manifest
   entry. It registers with a name-derived id and no curation, exactly as before. A null lookup is a
   normal answer, not an error.
4. **A repeat add still switches rather than duplicating.** Add a game already in the library. It must
   switch to the existing one — `Registry.FindRegistered` handles this by game root and Steam app id,
   and it is what stopped `windrose-2` happening. Confirm no second row appears.

**The fossil to expect on an existing machine.** A game added before this change keeps its old id, so
`marvel-s-spider-man-2-2` on the rig still matches no manifest entry and stays uncurated. Nothing
migrates it — re-adding is the fix, and that is a deliberate non-goal here rather than an oversight.
```

- [ ] **Step 2: Verify the checklist still parses**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~SmokeCatalogue"`

Expected: PASS. These tests guard `smoke.json` rather than the prose, so they should be unaffected — run them to be certain nothing else asserts on this file.

- [ ] **Step 3: Commit**

```bash
git add docs/smoke-tests/pending.md
git commit -m "docs(smoke): picking a curated game actually gets you its curation"
```

---

## Self-review

**Spec coverage.** C5's two halves: "GameInput gains an Id" is already true, so Tasks 1-3 cover the half that was missing — populating it from both catalogued paths. C5's collision question is already answered in shipped code by `Registry.FindRegistered`, recorded above under *What is already done*.

**Placeholders.** None. Every code step carries its code; every run step carries the command and the expected result. Task 3 Step 3 asks the implementer to determine which of two shapes the Steam path has and says exactly what to do in each case — that is a genuine branch in the existing code, not an unwritten instruction.

**Type consistency.** `ManifestIdLookup.BySteamAppId(GameManifest?, string?) -> string?` is defined in Task 1 and called in Task 2 with `EffectiveManifest.Current` and `game.AppId`. `_pickedManifestId` is `string?`, assigned from `PopularGame.Id` (a non-null `string`) and read into `GameInput.Id` (`string?`).

**One thing deliberately not done.** No migration for games already registered under a name-derived id. Re-adding fixes one, and a migration would have to guess which manifest entry an existing row meant — which is the same guess this plan exists to stop making.
