# Registration Repair (Core Primitives) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the three pure-Core pieces that make repairing a game registration safe — a `userSet` marker that records deliberate user choices, a data-dir mover that cannot lose files, and a planner that says what an edit will actually do.

**Architecture:** Everything lands in `src/ModManager.Core/` as pure functions and records. `RegistrationRefresh` gains one optional parameter and keeps taking primitives rather than a `GameEntry`, so it stays trivially testable. `DataDirMove` splits into `Plan` (inspects, decides, writes nothing) and `Execute` (the only thing that touches disk). `RegistrationChange` composes the two into a single answer for a future UI. No WinUI, no view-models, no dialogs — the UI is spec 2.

**Tech Stack:** .NET 10, C# (`<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`), xUnit, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-08-09-registration-repair-core-design.md` (commit `3d2a614`)

## Global Constraints

- **Test command is project-scoped, always:** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. Never run bare `dotnet test` or `dotnet build` at the repo root — the WinUI App project hangs the build.
- **Pure core:** nothing under `src/ModManager.Core/` may reference WinUI, WinRT, `Microsoft.UI.*`, or `Windows.UI.*`. `CorePurityTests` fails the suite if it does.
- **camelCase JSON on disk**, always. New persisted shapes ship a round-trip test containing a string-contains assertion on the camelCase key — a plain round-trip passes either way, because System.Text.Json deserializes case-insensitively.
- **Reversibility:** no delete before a verified copy; roll back on any mid-flight failure; a tidy-up failure never risks the surviving copy.
- **validate-then-extract:** `Plan` cannot write, `Execute` cannot decide.
- **Voice for user-facing strings:** builder-to-builder, second person, sentence case, period at the end. No emoji. No "seamlessly / robust / leverage".
- **Commits:** conventional — `feat(area)`, `fix(area)`, `docs(area)`. Area here is `registration`, `datadir`, or `scanner`.
- **`Id` is immutable across an edit.** It is half the data-dir key. Nothing in this plan may propose changing it.

---

### Task 1: The `userSet` field on `GameEntry`

**Files:**

- Modify: `src/ModManager.Core/GameEntry.cs:84` (append after `LastLaunchedUtc`)
- Test: `tests/ModManager.Tests/UserSetMarkerTests.cs` (create)

**Interfaces:**

- Consumes: nothing.
- Produces: `GameEntry.UserSet` (`IReadOnlyList<string>?`), and the four field-name constants
  `GameEntry.UserSetFileExtensions` = `"fileExtensions"`, `GameEntry.UserSetGroupingRule` = `"groupingRule"`,
  `GameEntry.UserSetModLocations` = `"modLocations"`, `GameEntry.UserSetGameRoot` = `"gameRoot"`.
  Tasks 2 and 5 use these.

- [ ] **Step 1: Write the failing test**

Create `tests/ModManager.Tests/UserSetMarkerTests.cs`:

```csharp
using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Persistence;

namespace ModManager.Tests;

// A registration's stored value is ambiguous: "pak" might be a choice the user made, or the engine
// preset's default frozen in on the day they clicked Add. RegistrationRefresh guesses between those
// with an untouched-default heuristic. This marker removes the guess for anything the user actually
// edits — and because only the edit path writes it, adding it needs no migration.
public class UserSetMarkerTests
{
    [Fact]
    public void UserSet_round_trips_as_camelCase()
    {
        var dir = TestSupport.TempDir("userset-");
        var reg = new GameRegistry
        {
            Version = 1,
            ActiveGameId = "cyberpunk-2077",
            Games = new List<GameEntry>
            {
                new()
                {
                    Id = "cyberpunk-2077",
                    GameName = "Cyberpunk 2077",
                    FileExtensions = new[] { "archive" },
                    UserSet = new[] { GameEntry.UserSetFileExtensions },
                },
            },
        };

        RegistryStore.Save(dir, reg);

        var json = File.ReadAllText(Path.Combine(dir, "games.json"));
        Assert.Contains("\"userSet\"", json);          // camelCase on disk (the launcher's convention)
        Assert.DoesNotContain("\"UserSet\"", json);

        var loaded = RegistryStore.Load(dir);
        Assert.Equal(new[] { "fileExtensions" }, loaded.Games[0].UserSet);
    }

    // The whole reason this was cheap to add now and expensive during A1: every registration written
    // before today simply has no key, and must behave exactly as it does today.
    [Fact]
    public void A_registration_written_before_the_marker_loads_with_no_marker()
    {
        var dir = TestSupport.TempDir("userset-");
        File.WriteAllText(Path.Combine(dir, "games.json"),
            """
            { "version": 1, "activeGameId": "elden-ring",
              "games": [ { "id": "elden-ring", "gameName": "ELDEN RING", "fileExtensions": [] } ] }
            """);

        var loaded = RegistryStore.Load(dir);

        Assert.Null(loaded.Games[0].UserSet);
    }

    // A null marker must not add noise to every existing registration on disk.
    [Fact]
    public void An_unset_marker_is_omitted_from_the_file_entirely()
    {
        var dir = TestSupport.TempDir("userset-");
        RegistryStore.Save(dir, new GameRegistry
        {
            Games = new List<GameEntry> { new() { Id = "witchfire", GameName = "Witchfire" } },
        });

        Assert.DoesNotContain("userSet", File.ReadAllText(Path.Combine(dir, "games.json")));
    }

    // The constants exist so a typo is a compile error rather than a marker nothing ever matches.
    [Fact]
    public void The_field_name_constants_are_the_camelCase_json_names()
    {
        Assert.Equal("fileExtensions", GameEntry.UserSetFileExtensions);
        Assert.Equal("groupingRule", GameEntry.UserSetGroupingRule);
        Assert.Equal("modLocations", GameEntry.UserSetModLocations);
        Assert.Equal("gameRoot", GameEntry.UserSetGameRoot);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~UserSetMarkerTests"`

Expected: FAIL to compile — `error CS0117: 'GameEntry' does not contain a definition for 'UserSet'`.

- [ ] **Step 3: Add the field and constants**

In `src/ModManager.Core/GameEntry.cs`, add `using System.Text.Json.Serialization;` at the top of the file if it is not already present, then append inside `GameEntry` immediately after `LastLaunchedUtc` (line 84):

```csharp
    /// <summary>
    /// Field names the user set DELIBERATELY, as their camelCase json names.
    ///
    /// <para>A stored value is ambiguous on its own: <c>["pak"]</c> might be a choice, or the engine
    /// preset's default frozen in on the day the game was added. <see cref="RegistrationRefresh"/>
    /// guesses between those with an untouched-preset-default heuristic, which is right for every
    /// registration measured so far but cannot distinguish a deliberate choice that HAPPENS to equal
    /// the default. This marker removes the guess for anything the user actually edits.</para>
    ///
    /// <para>Null means "not recorded", not "nothing is user-set" — every registration written before
    /// the edit surface existed has no key, and must keep behaving exactly as it does today. That
    /// back-compat is why this needed no migration, and why it was not worth adding during A1.</para>
    ///
    /// <para>Recorded for EVERY edited field; consulted today only for the two that self-heal. An
    /// entry no rule reads yet is inert — a fact waiting for a reader. Whoever adds the next
    /// self-healing field: check this before trusting the heuristic.</para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? UserSet { get; set; }

    // The json field names, as constants, so a marker is never a typo that silently matches nothing.
    public const string UserSetFileExtensions = "fileExtensions";
    public const string UserSetGroupingRule = "groupingRule";
    public const string UserSetModLocations = "modLocations";
    public const string UserSetGameRoot = "gameRoot";
```

`JsonIgnoreCondition.WhenWritingNull` is applied to this property alone rather than globally on
`AtomicJson` — a global setting would change how every other field serializes for all eleven
registrations on disk, which is a far larger blast radius than this field warrants.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~UserSetMarkerTests"`

Expected: PASS, 4 tests.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

Expected: PASS. Baseline before this plan is 1,775 passing / 2 skipped; expect 1,779 passing.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/GameEntry.cs tests/ModManager.Tests/UserSetMarkerTests.cs
git commit -m "feat(registration): record which fields the user set deliberately

A stored value is ambiguous on its own — [\"pak\"] might be a choice, or the
engine preset's default frozen in the day the game was added. RegistrationRefresh
guesses between those with an untouched-default heuristic, which is right for
every registration measured so far but cannot tell a deliberate choice that
HAPPENS to equal the default.

Null means not-recorded, not nothing-is-set: registrations written before this
have no key and behave exactly as they do today. That back-compat is why the
marker needed no migration, and why it was not worth adding during A1.

Omitted from the file when null, via a property-scoped JsonIgnore rather than a
global option — a global setting would change how every other field serializes."
```

---

### Task 2: `RegistrationRefresh` honors the marker

**Files:**

- Modify: `src/ModManager.Core/RegistrationRefresh.cs:33-42`
- Modify: `src/ModManager.Core/Scanner.cs:56-62`
- Test: `tests/ModManager.Tests/RegistrationRefreshTests.cs` (append)

**Interfaces:**

- Consumes: `GameEntry.UserSet`, `GameEntry.UserSetFileExtensions`, `GameEntry.UserSetGroupingRule` from Task 1.
- Produces: `RegistrationRefresh.Extensions(stored, presetDefault, manifest, userSet = false)` and
  `RegistrationRefresh.Grouping(stored, presetDefault, manifest, userSet = false)`. Task 5 does not call these.

- [ ] **Step 1: Write the failing tests**

Append to `tests/ModManager.Tests/RegistrationRefreshTests.cs`, inside the existing class:

```csharp
    // ---- the explicit marker beats the inference ----

    // A1's one documented blind spot: a user who deliberately picks a value that happens to EQUAL the
    // preset default is indistinguishable from one who never touched it, so the manifest overrides a
    // real choice. The marker is the only signal in the system that is not an inference, so it wins.
    [Fact]
    public void A_marked_field_is_kept_even_when_it_equals_the_preset_default()
    {
        var effective = RegistrationRefresh.Extensions(
            stored: new[] { "pak" }, presetDefault: new[] { "pak" },
            manifest: new[] { "archive" }, userSet: true);

        Assert.Equal(new[] { "pak" }, effective);
    }

    [Fact]
    public void An_unmarked_field_still_self_heals()
    {
        var effective = RegistrationRefresh.Extensions(
            stored: new[] { "pak" }, presetDefault: new[] { "pak" },
            manifest: new[] { "archive" }, userSet: false);

        Assert.Equal(new[] { "archive" }, effective);
    }

    [Fact]
    public void A_marked_grouping_rule_is_kept_even_when_it_equals_the_preset_default()
    {
        Assert.Equal("filename_no_ext", RegistrationRefresh.Grouping(
            "filename_no_ext", "filename_no_ext", "extension", userSet: true));

        Assert.Equal("extension", RegistrationRefresh.Grouping(
            "filename_no_ext", "filename_no_ext", "extension", userSet: false));
    }

    // The default keeps every pre-existing call site and all ten original tests behaving identically.
    [Fact]
    public void The_marker_defaults_to_absent()
    {
        Assert.Equal(new[] { "archive" },
            RegistrationRefresh.Extensions(new[] { "pak" }, new[] { "pak" }, new[] { "archive" }));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~RegistrationRefreshTests"`

Expected: FAIL to compile — `error CS1739: The best overload for 'Extensions' does not have a parameter named 'userSet'`.

- [ ] **Step 3: Add the parameter**

In `src/ModManager.Core/RegistrationRefresh.cs`, replace the two public methods (lines 33-42) with:

```csharp
    /// <summary>The extensions to actually scan with. <paramref name="userSet"/> is checked FIRST and
    /// wins outright: it is the one signal here that is not an inference. The untouched-default test
    /// stays underneath as the fallback for registrations that predate the marker.</summary>
    public static IReadOnlyList<string> Extensions(
        IReadOnlyList<string> stored, IReadOnlyList<string> presetDefault,
        IReadOnlyList<string>? manifest, bool userSet = false)
        => userSet ? stored
         : manifest is { Count: > 0 } && IsUntouched(stored, presetDefault) ? manifest
         : stored;

    /// <summary>The grouping rule to actually group with. Same freeze, same rule, same precedence.</summary>
    public static string? Grouping(
        string? stored, string? presetDefault, string? manifest, bool userSet = false)
        => userSet ? stored
         : !string.IsNullOrWhiteSpace(manifest)
           && string.Equals(stored?.Trim() ?? "", presetDefault?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)
            ? manifest
            : stored;
```

- [ ] **Step 4: Wire it into the scanner**

In `src/ModManager.Core/Scanner.cs`, replace the `declaredExts` / `groupingRule` block (lines 56-62) with:

```csharp
        // A marked field is a choice the user stated outright; it outranks the untouched-default
        // inference below it. Null UserSet means "not recorded" — the pre-marker path, unchanged.
        var userSetExts = game.UserSet?.Contains(GameEntry.UserSetFileExtensions, StringComparer.OrdinalIgnoreCase) == true;
        var userSetGrouping = game.UserSet?.Contains(GameEntry.UserSetGroupingRule, StringComparer.OrdinalIgnoreCase) == true;
        var declaredExts = preset is null
            ? game.FileExtensions
            : RegistrationRefresh.Extensions(game.FileExtensions, preset.FileExtensions, manifestEntry?.FileExtensions, userSetExts);
        var groupingRule = preset is null
            ? game.GroupingRule
            : RegistrationRefresh.Grouping(game.GroupingRule, preset.GroupingRule, manifestEntry?.GroupingRule, userSetGrouping);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~RegistrationRefreshTests"`

Expected: PASS, 14 tests — the original ten unchanged plus the four new ones.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

Expected: PASS, 1,783 passing / 2 skipped. Scanner tests must be unchanged — an unmarked registration behaves exactly as before.

- [ ] **Step 7: Commit**

```bash
git add src/ModManager.Core/RegistrationRefresh.cs src/ModManager.Core/Scanner.cs tests/ModManager.Tests/RegistrationRefreshTests.cs
git commit -m "feat(registration): an explicit marker outranks the inference

RegistrationRefresh gains one optional parameter, checked before everything
else. A marked field is a choice the user stated outright; the untouched-preset-
default test stays underneath as the fallback for registrations that predate the
marker, and becomes less load-bearing over time rather than more.

Closes A1's one documented blind spot: a deliberate choice that happens to equal
the preset default was indistinguishable from a frozen default, so the manifest
silently overrode a real choice.

The parameter defaults to false, so every existing call site and all ten original
tests pass unchanged. Both methods still take primitives rather than a GameEntry,
so they stay trivially testable."
```

---

### Task 3: `DataDirMove.Plan` — inspect and refuse, write nothing

**Files:**

- Create: `src/ModManager.Core/DataDirMove.cs`
- Test: `tests/ModManager.Tests/DataDirMoveTests.cs` (create)

**Interfaces:**

- Consumes: nothing.
- Produces: `DataDirMoveKind` (`Nothing` / `Rename` / `CopyVerifyDelete`), `DataDirMovePlan`
  (`From`, `To`, `Kind`, `FileCount`, `TotalBytes`, `Refusal`, `CanProceed`), and
  `DataDirMove.Plan(string from, string to) → DataDirMovePlan`. Tasks 4 and 5 use these.

- [ ] **Step 1: Write the failing tests**

Create `tests/ModManager.Tests/DataDirMoveTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

// The data dir holds the ONLY copy of real user files — disabled mods, held framework proxies,
// archived Vortex takeovers, installed tool binaries. Moving it is the single most dangerous thing
// an edit can trigger, so it follows validate-then-extract: Plan cannot write, Execute cannot decide.
public class DataDirMovePlanTests
{
    private static string Src(params string[] names)
    {
        var d = TestSupport.TempDir("ddm-src-");
        foreach (var n in names) TestSupport.Write(Path.Combine(d, n), n);
        return d;
    }

    [Fact]
    public void A_plan_reports_the_real_file_count_and_size()
    {
        var from = Src("a.txt", "sub/b.txt", "sub/deep/c.txt");
        var to = Path.Combine(TestSupport.TempDir("ddm-to-"), "moved");

        var plan = DataDirMove.Plan(from, to);

        Assert.Equal(3, plan.FileCount);
        Assert.True(plan.TotalBytes > 0);
        Assert.True(plan.CanProceed);
    }

    // Never merge two data dirs — the same stance the legacy MigrateDataDir already takes. A merge
    // would interleave two games' disabled mods with no way to tell them apart afterwards.
    [Fact]
    public void A_non_empty_target_is_refused()
    {
        var from = Src("a.txt");
        var to = TestSupport.TempDir("ddm-to-");
        TestSupport.Write(Path.Combine(to, "occupied.txt"), "x");

        var plan = DataDirMove.Plan(from, to);

        Assert.False(plan.CanProceed);
        Assert.NotNull(plan.Refusal);
    }

    [Fact]
    public void An_empty_target_directory_is_not_a_refusal()
    {
        var from = Src("a.txt");
        var to = TestSupport.TempDir("ddm-to-");   // exists, but empty

        Assert.True(DataDirMove.Plan(from, to).CanProceed);
    }

    [Fact]
    public void A_missing_source_is_nothing_to_do_rather_than_an_error()
    {
        var from = Path.Combine(TestSupport.TempDir("ddm-"), "never-existed");
        var to = Path.Combine(TestSupport.TempDir("ddm-"), "moved");

        var plan = DataDirMove.Plan(from, to);

        Assert.Equal(DataDirMoveKind.Nothing, plan.Kind);
        Assert.True(plan.CanProceed);
        Assert.Equal(0, plan.FileCount);
    }

    [Fact]
    public void Moving_a_folder_onto_itself_is_nothing_to_do()
    {
        var from = Src("a.txt");

        Assert.Equal(DataDirMoveKind.Nothing, DataDirMove.Plan(from, from).Kind);
    }

    // Same volume with no target gets an atomic rename: instant, and there is no window in which the
    // data exists in neither place. That is strictly safer than copy-then-delete, so it is preferred.
    [Fact]
    public void Same_volume_with_an_absent_target_plans_a_rename()
    {
        var from = Src("a.txt");
        var to = Path.Combine(Path.GetDirectoryName(from)!, "renamed-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(DataDirMoveKind.Rename, DataDirMove.Plan(from, to).Kind);
    }

    // Plan is inspection only. If planning could write, a user clicking Cancel would already have
    // changed their install.
    [Fact]
    public void Planning_writes_nothing()
    {
        var from = Src("a.txt", "sub/b.txt");
        var to = Path.Combine(TestSupport.TempDir("ddm-to-"), "moved");
        var before = Directory.GetFileSystemEntries(from, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray();

        DataDirMove.Plan(from, to);

        Assert.Equal(before, Directory.GetFileSystemEntries(from, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray());
        Assert.False(Directory.Exists(to));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~DataDirMovePlanTests"`

Expected: FAIL to compile — `error CS0103: The name 'DataDirMove' does not exist in the current context`.

- [ ] **Step 3: Write the plan half**

Create `src/ModManager.Core/DataDirMove.cs`:

```csharp
namespace ModManager.Core;

/// <summary>How a data-dir move will be carried out.</summary>
public enum DataDirMoveKind
{
    /// <summary>Nothing to do — no source, or source and target are the same place.</summary>
    Nothing,
    /// <summary>Same volume, target absent: an atomic directory rename.</summary>
    Rename,
    /// <summary>Different volumes: copy to staging, verify, swap, then delete the source.</summary>
    CopyVerifyDelete,
}

/// <summary>What a move would do. Produced by <see cref="DataDirMove.Plan"/>; writes nothing.</summary>
public sealed record DataDirMovePlan
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required DataDirMoveKind Kind { get; init; }
    public required int FileCount { get; init; }
    public required long TotalBytes { get; init; }

    /// <summary>Why this move must not happen, in the user's words, or null when it may proceed.</summary>
    public string? Refusal { get; init; }

    public bool CanProceed => Refusal is null;
}

/// <summary>
/// Moves a game's launcher data folder, safely.
///
/// <para>The data dir holds the ONLY copy of real user files — <c>disabled\</c>,
/// <c>direct-disabled\</c>, <c>loose-disabled\</c>, <c>frameworks\*\disabled-proxy\</c>,
/// <c>vortex-takeover\</c>, <c>tools\</c>. Its path is a pure function of
/// <c>(Id, GameRoot)</c> (see <see cref="Scanner.DataDirForGame"/>), so correcting a game folder
/// moves it. Getting that wrong does not lose metadata; it loses mods.</para>
///
/// <para>Split per the repo's validate-then-extract law: <see cref="Plan"/> inspects and refuses and
/// cannot write; <see cref="Execute"/> writes and cannot decide. A UI can therefore show a real path
/// and a real size before the user commits to anything.</para>
/// </summary>
public static class DataDirMove
{
    public static DataDirMovePlan Plan(string from, string to)
    {
        var src = Norm(from);
        var dst = Norm(to);

        if (src.Length == 0 || dst.Length == 0 || !Directory.Exists(src))
            return Empty(src, dst);

        if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
            return Empty(src, dst);

        var files = Directory.GetFiles(src, "*", SearchOption.AllDirectories);
        var bytes = files.Sum(f => new FileInfo(f).Length);

        // Never merge two data dirs. Interleaving two games' disabled mods leaves no way to tell them
        // apart afterwards — the same stance the legacy MigrateDataDir already takes.
        if (Directory.Exists(dst) && Directory.EnumerateFileSystemEntries(dst).Any())
            return new DataDirMovePlan
            {
                From = src, To = dst, Kind = DataDirMoveKind.Nothing,
                FileCount = files.Length, TotalBytes = bytes,
                Refusal = "There is already launcher data in that location. Move or remove it first — "
                          + "merging two data folders would leave no way to tell the two games' files apart.",
            };

        var sameVolume = string.Equals(
            Path.GetPathRoot(src) ?? "", Path.GetPathRoot(dst) ?? "", StringComparison.OrdinalIgnoreCase);
        var kind = sameVolume && !Directory.Exists(dst) ? DataDirMoveKind.Rename : DataDirMoveKind.CopyVerifyDelete;

        // A rename needs no free space; a copy needs the whole thing on the far side before anything
        // is removed from this one. Checking here means we refuse before writing a single byte.
        if (kind == DataDirMoveKind.CopyVerifyDelete && !HasRoom(dst, bytes))
            return new DataDirMovePlan
            {
                From = src, To = dst, Kind = kind, FileCount = files.Length, TotalBytes = bytes,
                Refusal = $"There is not enough free space to move {Mb(bytes)} to that drive.",
            };

        return new DataDirMovePlan
        {
            From = src, To = dst, Kind = kind, FileCount = files.Length, TotalBytes = bytes,
        };
    }

    private static DataDirMovePlan Empty(string src, string dst) => new()
    {
        From = src, To = dst, Kind = DataDirMoveKind.Nothing, FileCount = 0, TotalBytes = 0,
    };

    private static bool HasRoom(string dst, long bytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(dst));
            return string.IsNullOrEmpty(root) || new DriveInfo(root).AvailableFreeSpace >= bytes;
        }
        catch { return true; }   // unknowable free space is not a reason to refuse; the copy will say
    }

    private static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0:N0} MB";

    internal static string Norm(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        try { return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return p.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~DataDirMovePlanTests"`

Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/DataDirMove.cs tests/ModManager.Tests/DataDirMoveTests.cs
git commit -m "feat(datadir): plan a data-dir move without touching disk

The data dir holds the only copy of real user files — disabled mods, held
framework proxies, archived Vortex takeovers, installed tool binaries — and its
path is a pure function of (Id, GameRoot), so correcting a game folder moves it.
Getting that wrong does not lose metadata; it loses mods.

Plan inspects and refuses; it cannot write. Refusals are decided before a single
byte moves: a non-empty target is never merged, and a copy that would not fit is
refused up front. Same-volume moves are planned as an atomic rename, which has no
window where the data exists in neither place."
```

---

### Task 4: `DataDirMove.Execute` — the only thing that writes

**Files:**

- Modify: `src/ModManager.Core/DataDirMove.cs` (append to the class)
- Test: `tests/ModManager.Tests/DataDirMoveTests.cs` (append a second class)

**Interfaces:**

- Consumes: `DataDirMovePlan`, `DataDirMoveKind` from Task 3.
- Produces: `DataDirMoveResult` (`Moved`, `SourceRemoved`, `Error`) and
  `DataDirMove.Execute(DataDirMovePlan plan) → DataDirMoveResult`. Task 5 does not call `Execute`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/ModManager.Tests/DataDirMoveTests.cs`:

```csharp
// Execute is the only thing here that writes. The ordering IS the safety: the source is never
// deleted until the target is verified in place, so any mid-flight failure leaves the user exactly
// where they started.
public class DataDirMoveExecuteTests
{
    private static string Src(params string[] names)
    {
        var d = TestSupport.TempDir("ddm-src-");
        foreach (var n in names) TestSupport.Write(Path.Combine(d, n), "content-of-" + n);
        return d;
    }

    [Fact]
    public void A_rename_moves_every_file_and_leaves_no_source()
    {
        var from = Src("a.txt", "sub/b.txt");
        var to = Path.Combine(Path.GetDirectoryName(from)!, "renamed-" + Guid.NewGuid().ToString("N"));

        var result = DataDirMove.Execute(DataDirMove.Plan(from, to));

        Assert.True(result.Moved);
        Assert.Null(result.Error);
        Assert.False(Directory.Exists(from));
        Assert.Equal("content-of-a.txt", File.ReadAllText(Path.Combine(to, "a.txt")));
        Assert.Equal("content-of-sub/b.txt", File.ReadAllText(Path.Combine(to, "sub", "b.txt")));
    }

    // The cross-volume path is exercised by constructing the plan directly, so the suite does not
    // need two volumes to run. The behaviour under test is the copy-verify-swap-delete sequence,
    // which is identical wherever the two paths happen to live.
    [Fact]
    public void A_copy_move_reproduces_the_whole_tree_and_removes_the_source()
    {
        var from = Src("a.txt", "sub/b.txt", "sub/deep/c.txt");
        var to = Path.Combine(TestSupport.TempDir("ddm-to-"), "moved");
        var planned = DataDirMove.Plan(from, to);
        var forced = planned with { Kind = DataDirMoveKind.CopyVerifyDelete };

        var result = DataDirMove.Execute(forced);

        Assert.True(result.Moved);
        Assert.True(result.SourceRemoved);
        Assert.False(Directory.Exists(from));
        Assert.Equal("content-of-sub/deep/c.txt", File.ReadAllText(Path.Combine(to, "sub", "deep", "c.txt")));
        Assert.Equal(3, Directory.GetFiles(to, "*", SearchOption.AllDirectories).Length);
    }

    // THE reversibility test, and the reason Execute is shaped the way it is. A file held open with
    // no sharing is a real failure mode (a running game, an antivirus scan), not a contrived one.
    [Fact]
    public void A_failure_mid_copy_leaves_the_source_intact_and_the_target_absent()
    {
        var from = Src("a.txt", "locked.txt", "c.txt");
        var to = Path.Combine(TestSupport.TempDir("ddm-to-"), "moved");
        var forced = DataDirMove.Plan(from, to) with { Kind = DataDirMoveKind.CopyVerifyDelete };

        DataDirMoveResult result;
        using (File.Open(Path.Combine(from, "locked.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = DataDirMove.Execute(forced);
        }

        Assert.False(result.Moved);
        Assert.NotNull(result.Error);
        Assert.False(Directory.Exists(to));                                   // no half-built target
        Assert.Equal(3, Directory.GetFiles(from, "*", SearchOption.AllDirectories).Length);
        Assert.Equal("content-of-a.txt", File.ReadAllText(Path.Combine(from, "a.txt")));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(to)!, "*.moving-*"));   // staging cleaned
    }

    [Fact]
    public void A_refused_plan_is_never_executed()
    {
        var from = Src("a.txt");
        var to = TestSupport.TempDir("ddm-to-");
        TestSupport.Write(Path.Combine(to, "occupied.txt"), "x");

        var result = DataDirMove.Execute(DataDirMove.Plan(from, to));

        Assert.False(result.Moved);
        Assert.NotNull(result.Error);
        Assert.True(File.Exists(Path.Combine(from, "a.txt")));
        Assert.Equal("x", File.ReadAllText(Path.Combine(to, "occupied.txt")));
    }

    [Fact]
    public void A_nothing_to_do_plan_succeeds_without_writing()
    {
        var from = Path.Combine(TestSupport.TempDir("ddm-"), "never-existed");
        var to = Path.Combine(TestSupport.TempDir("ddm-"), "moved");

        var result = DataDirMove.Execute(DataDirMove.Plan(from, to));

        Assert.True(result.Moved);
        Assert.Null(result.Error);
        Assert.False(Directory.Exists(to));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~DataDirMoveExecuteTests"`

Expected: FAIL to compile — `error CS0117: 'DataDirMove' does not contain a definition for 'Execute'`.

- [ ] **Step 3: Write the execute half**

Append inside the `DataDirMove` class in `src/ModManager.Core/DataDirMove.cs`, before the private
helpers:

```csharp
    /// <summary>
    /// Carry out a plan. The ONLY method here that writes.
    ///
    /// <para>THE ORDERING IS THE SAFETY. The source is never removed until the target is verified in
    /// place, so a failure at any point leaves the user exactly where they started. A failure to
    /// remove the source at the very end is deliberately non-fatal: a harmless duplicate is a far
    /// better outcome than risking the surviving copy in order to tidy up.</para>
    /// </summary>
    public static DataDirMoveResult Execute(DataDirMovePlan plan)
    {
        if (!plan.CanProceed)
            return new DataDirMoveResult { Moved = false, SourceRemoved = false, Error = plan.Refusal };

        if (plan.Kind == DataDirMoveKind.Nothing)
            return new DataDirMoveResult { Moved = true, SourceRemoved = false, Error = null };

        try
        {
            if (plan.Kind == DataDirMoveKind.Rename)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(plan.To)!);
                Directory.Move(plan.From, plan.To);
                return new DataDirMoveResult { Moved = true, SourceRemoved = true, Error = null };
            }

            // Stage beside the target so the swap into place is a rename, not a second long copy.
            var staging = plan.To + ".moving-" + Environment.ProcessId;
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                CopyTree(plan.From, staging);
                if (!Verify(plan.From, staging, out var mismatch))
                    throw new IOException("The copy did not match the original: " + mismatch);

                Directory.CreateDirectory(Path.GetDirectoryName(plan.To)!);
                Directory.Move(staging, plan.To);
            }
            catch
            {
                // Roll back to untouched. The source has not been read destructively, so removing the
                // staging tree puts the user exactly back where they started.
                try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
                catch { /* nothing further we can safely do */ }
                throw;
            }

            // Tidy-up only. The data is already safe at the target, so a failure here must not be
            // reported as a failed move — that would invite a caller to "retry" onto a populated target.
            var sourceRemoved = true;
            try { Directory.Delete(plan.From, recursive: true); }
            catch { sourceRemoved = false; }

            return new DataDirMoveResult { Moved = true, SourceRemoved = sourceRemoved, Error = null };
        }
        catch (Exception e)
        {
            return new DataDirMoveResult { Moved = false, SourceRemoved = false, Error = e.Message };
        }
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: false);
    }

    /// <summary>
    /// Same set of relative paths, same byte length for each.
    ///
    /// <para>That catches the failures that actually happen — a truncated copy, a file that did not
    /// make it, a disk that filled. It deliberately does NOT hash contents: hashing gigabytes of
    /// disabled mods would add minutes to every move to catch a class of silent corruption the
    /// rename path does not have at all. Stated here rather than implied away, so no caller reads
    /// "verify" as a guarantee this does not provide.</para>
    /// </summary>
    private static bool Verify(string from, string to, out string mismatch)
    {
        var a = Directory.GetFiles(from, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(from, f), f => new FileInfo(f).Length, StringComparer.OrdinalIgnoreCase);
        var b = Directory.GetFiles(to, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(to, f), f => new FileInfo(f).Length, StringComparer.OrdinalIgnoreCase);

        foreach (var (rel, len) in a)
        {
            if (!b.TryGetValue(rel, out var copied)) { mismatch = rel + " is missing."; return false; }
            if (copied != len) { mismatch = rel + " is a different size."; return false; }
        }
        if (b.Count != a.Count) { mismatch = "the copy has extra files."; return false; }

        mismatch = "";
        return true;
    }
```

And add the result record at the end of the file, after the `DataDirMove` class:

```csharp
/// <summary>The outcome of a <see cref="DataDirMove.Execute"/> call.</summary>
public sealed record DataDirMoveResult
{
    /// <summary>True when the data is at the target (or there was nothing to move).</summary>
    public required bool Moved { get; init; }

    /// <summary>False when the move succeeded but the old copy could not be deleted — a duplicate on
    /// disk, never a lost file.</summary>
    public required bool SourceRemoved { get; init; }

    /// <summary>Why the move did not happen, in the user's words, or null on success.</summary>
    public string? Error { get; init; }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~DataDirMoveExecuteTests"`

Expected: PASS, 5 tests.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

Expected: PASS, 1,795 passing / 2 skipped.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/DataDirMove.cs tests/ModManager.Tests/DataDirMoveTests.cs
git commit -m "feat(datadir): move the folder without ever risking it

The ordering is the safety: copy to staging beside the target, verify, swap by
rename, and only then delete the source. A failure at any point removes the
staging tree and leaves the user exactly where they started — covered by a test
that holds a source file open with no sharing, which is a real failure mode
(a running game, an antivirus scan) rather than a contrived one.

Failing to delete the source at the end is deliberately non-fatal. The data is
already safe at the target; reporting that as a failed move would invite a retry
onto a now-populated target, which Plan correctly refuses.

Verify is path-set plus byte length, and the doc comment says so — hashing
gigabytes of disabled mods would add minutes to catch a corruption class the
rename path does not have, and no caller should read 'verify' as more than it is."
```

---

### Task 5: `RegistrationChange.Plan` — what an edit will actually do

**Files:**

- Create: `src/ModManager.Core/RegistrationChange.cs`
- Test: `tests/ModManager.Tests/RegistrationChangeTests.cs` (create)

**Interfaces:**

- Consumes: `GameEntry.UserSet` and the four constants (Task 1); `DataDirMove.Plan`,
  `DataDirMovePlan` (Task 3); `Scanner.DataDirForGame`.
- Produces: `RegistrationChangePlan` (`FieldsChanged`, `FieldsToPin`, `DataDir`, `Blockers`, `Notes`,
  `CanSave`) and `RegistrationChange.Plan(GameEntry stored, GameEntry proposed) → RegistrationChangePlan`.
  Spec 2's UI consumes this.

- [ ] **Step 1: Write the failing tests**

Create `tests/ModManager.Tests/RegistrationChangeTests.cs`:

```csharp
using ModManager.Core;

namespace ModManager.Tests;

// The move-or-pin prompt has to name a real folder, a real size, and a real reason it might refuse.
// That is a decision, not a rendering detail — and decisions parked in MainViewModel (14 concrete
// service deps, unconstructible in tests) have repeatedly accumulated defects until someone
// extracted them. This extracts it before it is ever parked.
public class RegistrationChangeTests
{
    private static GameEntry Stored(string root) => new()
    {
        Id = "elden-ring",
        GameName = "ELDEN RING",
        Engine = "fromsoft",
        GameRoot = root,
        FileExtensions = Array.Empty<string>(),
        GroupingRule = "by_folder",
        ModLocations = new[] { new ModLocation("mods", "mods", "mod") },
    };

    private static GameEntry Copy(GameEntry g) => new()
    {
        Id = g.Id, GameName = g.GameName, Engine = g.Engine, GameRoot = g.GameRoot,
        FileExtensions = g.FileExtensions, GroupingRule = g.GroupingRule,
        ModLocations = g.ModLocations, UserSet = g.UserSet,
    };

    [Fact]
    public void An_unchanged_entry_changes_and_pins_nothing()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));

        var plan = RegistrationChange.Plan(stored, Copy(stored));

        Assert.Empty(plan.FieldsChanged);
        Assert.Empty(plan.FieldsToPin);
        Assert.Null(plan.DataDir);
        Assert.True(plan.CanSave);
    }

    [Fact]
    public void Correcting_the_mod_path_is_reported_and_pinned()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.ModLocations = new[] { new ModLocation("mods", "mods", "Game/mod") };

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Contains(GameEntry.UserSetModLocations, plan.FieldsChanged);
        Assert.Contains(GameEntry.UserSetModLocations, plan.FieldsToPin);
        Assert.Null(plan.DataDir);          // the mod path does not move the data dir
        Assert.True(plan.CanSave);
    }

    // THE identity rule. Id is half the data-dir key, so re-slugging it on a cosmetic rename would
    // silently orphan every disabled mod, profile, and installed tool.
    [Fact]
    public void Renaming_the_game_leaves_the_id_and_the_data_dir_alone()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.GameName = "Elden Ring (Reforged)";

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Equal("elden-ring", proposed.Id);
        Assert.Null(plan.DataDir);
        Assert.True(plan.CanSave);
    }

    [Fact]
    public void An_attempt_to_change_the_id_is_blocked_outright()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.Id = "elden-ring-2";

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.False(plan.CanSave);
        Assert.NotEmpty(plan.Blockers);
    }

    [Fact]
    public void Changing_the_game_folder_produces_a_move_plan_with_real_numbers()
    {
        var oldRoot = Path.Combine(TestSupport.TempDir("rc-old-"), "ELDEN RING");
        Directory.CreateDirectory(oldRoot);
        var stored = Stored(oldRoot);

        // Populate the data dir the stored entry actually resolves to.
        var dataDir = Scanner.DataDirForGame(stored);
        TestSupport.Write(Path.Combine(dataDir, "disabled", "SomeMod.dll"), "held file");

        var proposed = Copy(stored);
        proposed.GameRoot = Path.Combine(TestSupport.TempDir("rc-new-"), "ELDEN RING");
        Directory.CreateDirectory(proposed.GameRoot);

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.NotNull(plan.DataDir);
        Assert.Equal(1, plan.DataDir!.FileCount);
        Assert.True(plan.DataDir.TotalBytes > 0);
        Assert.Contains(GameEntry.UserSetGameRoot, plan.FieldsChanged);
        Assert.True(plan.CanSave);
    }

    // A refusal from the mover must surface, not be swallowed. Swallowing it would let a save proceed
    // into a merge that Plan already decided was unsafe.
    [Fact]
    public void A_refusal_from_the_mover_becomes_a_blocker()
    {
        var oldRoot = Path.Combine(TestSupport.TempDir("rc-old-"), "ELDEN RING");
        Directory.CreateDirectory(oldRoot);
        var stored = Stored(oldRoot);
        TestSupport.Write(Path.Combine(Scanner.DataDirForGame(stored), "disabled", "SomeMod.dll"), "held");

        var proposed = Copy(stored);
        proposed.GameRoot = Path.Combine(TestSupport.TempDir("rc-new-"), "ELDEN RING");
        Directory.CreateDirectory(proposed.GameRoot);
        // Occupy the destination data dir so the move is refused.
        TestSupport.Write(Path.Combine(Scanner.DataDirForGame(proposed), "occupied.txt"), "x");

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.False(plan.CanSave);
        Assert.NotEmpty(plan.Blockers);
    }

    // Changing the engine changes which preset defaults apply, so a field that reads as "untouched"
    // under one engine may read as customised under another — quietly altering whether future
    // manifest corrections reach this game. Report it; do not decide for the user.
    [Fact]
    public void Changing_the_engine_is_noted_because_it_shifts_the_preset_baseline()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.Engine = "ue-pak";

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.NotEmpty(plan.Notes);
        Assert.True(plan.CanSave);
    }

    [Fact]
    public void Fields_already_marked_stay_marked()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        stored.UserSet = new[] { GameEntry.UserSetFileExtensions };
        var proposed = Copy(stored);
        proposed.GroupingRule = "filename_no_ext";

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Contains(GameEntry.UserSetFileExtensions, plan.FieldsToPin);
        Assert.Contains(GameEntry.UserSetGroupingRule, plan.FieldsToPin);
    }

    [Fact]
    public void Planning_writes_nothing()
    {
        var root = TestSupport.TempDir("rc-");
        var stored = Stored(Path.Combine(root, "ELDEN RING"));
        Directory.CreateDirectory(stored.GameRoot);
        var before = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray();

        RegistrationChange.Plan(stored, Copy(stored));

        Assert.Equal(before, Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~RegistrationChangeTests"`

Expected: FAIL to compile — `error CS0103: The name 'RegistrationChange' does not exist in the current context`.

- [ ] **Step 3: Write the planner**

Create `src/ModManager.Core/RegistrationChange.cs`:

```csharp
namespace ModManager.Core;

/// <summary>What saving an edit would actually do. Produced by <see cref="RegistrationChange.Plan"/>.</summary>
public sealed record RegistrationChangePlan
{
    /// <summary>Field names (camelCase, matching the <c>GameEntry.UserSet*</c> constants) that differ.</summary>
    public required IReadOnlyList<string> FieldsChanged { get; init; }

    /// <summary>What the caller should write to <see cref="GameEntry.UserSet"/> on save — the fields
    /// changed here, plus everything already marked. Marks are never dropped by an unrelated edit.</summary>
    public required IReadOnlyList<string> FieldsToPin { get; init; }

    /// <summary>The data-dir move this edit implies, or null when it implies none.</summary>
    public DataDirMovePlan? DataDir { get; init; }

    /// <summary>Reasons this edit must not be saved as-is.</summary>
    public required IReadOnlyList<string> Blockers { get; init; }

    /// <summary>Consequences worth showing that are not blockers.</summary>
    public required IReadOnlyList<string> Notes { get; init; }

    public bool CanSave => Blockers.Count == 0;
}

/// <summary>
/// Works out what an edit to a game registration will actually do, before anything is saved.
///
/// <para>The UI renders this and never computes consequences itself. The move-or-pin prompt has to
/// name a real folder, a real byte count, and a real refusal reason — that is a decision, not a
/// rendering detail, and this repo has repeatedly learned that decisions parked in
/// <c>MainViewModel</c> (14 concrete service deps, unconstructible in tests) accumulate defects
/// until someone extracts them.</para>
///
/// <para>Pure, and does no IO of its own beyond delegating to <see cref="DataDirMove.Plan"/>.</para>
/// </summary>
public static class RegistrationChange
{
    public static RegistrationChangePlan Plan(GameEntry stored, GameEntry proposed)
    {
        var changed = new List<string>();
        var blockers = new List<string>();
        var notes = new List<string>();

        // THE IDENTITY RULE. Id is half the data-dir key (Scanner.DataDirForGame), so changing it
        // orphans every disabled mod, profile, and installed tool — silently, from what may have
        // looked like a cosmetic rename. An edit may never do this.
        if (!string.Equals(stored.Id, proposed.Id, StringComparison.Ordinal))
            blockers.Add("A game's id cannot change once it is registered — it is how the launcher "
                         + "finds this game's disabled mods, profiles, and installed tools. Rename the "
                         + "game instead; the id stays as it is.");

        if (!SameExtensions(stored.FileExtensions, proposed.FileExtensions))
            changed.Add(GameEntry.UserSetFileExtensions);

        // GroupingRule is a non-nullable string on GameEntry (defaults to ""), so no null guard here —
        // TreatWarningsAsErrors is on, and a dead ?? would not survive the build.
        if (!string.Equals(stored.GroupingRule.Trim(), proposed.GroupingRule.Trim(),
                StringComparison.OrdinalIgnoreCase))
            changed.Add(GameEntry.UserSetGroupingRule);

        if (!SameLocations(stored.ModLocations, proposed.ModLocations))
            changed.Add(GameEntry.UserSetModLocations);

        var rootChanged = !string.Equals(
            DataDirMove.Norm(stored.GameRoot), DataDirMove.Norm(proposed.GameRoot), StringComparison.OrdinalIgnoreCase);
        if (rootChanged) changed.Add(GameEntry.UserSetGameRoot);

        // Changing the engine changes which preset defaults apply, so a field that reads as
        // "untouched" under one engine may read as customised under another — quietly altering
        // whether future manifest corrections reach this game. Report it; the user decides.
        if (!string.Equals(stored.Engine ?? "", proposed.Engine ?? "", StringComparison.OrdinalIgnoreCase))
            notes.Add($"Changing the engine from '{stored.Engine}' to '{proposed.Engine}' changes which "
                      + "defaults this game is compared against, so it can change whether future "
                      + "definition updates reach it.");

        DataDirMovePlan? move = null;
        if (rootChanged && blockers.Count == 0)
        {
            move = DataDirMove.Plan(Scanner.DataDirForGame(stored), Scanner.DataDirForGame(proposed));
            if (move.Refusal is not null) blockers.Add(move.Refusal);
            if (move.Kind == DataDirMoveKind.Nothing && move.Refusal is null) move = null;
        }

        // Marks are additive. An edit to one field must never drop the mark on another — that would
        // silently re-expose a deliberate choice to being overwritten by a manifest correction.
        var pin = new List<string>(stored.UserSet ?? Array.Empty<string>());
        foreach (var f in changed)
            if (!pin.Contains(f, StringComparer.OrdinalIgnoreCase)) pin.Add(f);

        return new RegistrationChangePlan
        {
            FieldsChanged = changed,
            FieldsToPin = changed.Count == 0 ? (stored.UserSet ?? Array.Empty<string>()).ToList() : pin,
            DataDir = move,
            Blockers = blockers,
            Notes = notes,
        };
    }

    private static bool SameExtensions(IReadOnlyList<string> a, IReadOnlyList<string> b)
        => new HashSet<string>(a, StringComparer.OrdinalIgnoreCase)
            .SetEquals(new HashSet<string>(b, StringComparer.OrdinalIgnoreCase));

    // ModLocation is a positional record — ModLocation(string Name, string Label, string Path) — so
    // Name and Path are non-nullable. No ?? guards: TreatWarningsAsErrors is on and dead null-coalesce
    // on a non-nullable operand is exactly the kind of thing that breaks a build at the worst moment.
    // Label is deliberately not compared: it is display text, not part of where the mods are.
    private static bool SameLocations(IReadOnlyList<ModLocation> a, IReadOnlyList<ModLocation> b)
        => a.Count == b.Count
           && a.Zip(b).All(p =>
               string.Equals(p.First.Name, p.Second.Name, StringComparison.OrdinalIgnoreCase)
               && string.Equals(p.First.Path, p.Second.Path, StringComparison.OrdinalIgnoreCase));
}
```

Note on `FieldsToPin` when nothing changed: an unchanged entry returns exactly what was already
marked, so the "unchanged pins nothing" test holds for a game with no prior marks while a game that
already had marks does not lose them.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~RegistrationChangeTests"`

Expected: PASS, 9 tests.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

Expected: PASS, 1,804 passing / 2 skipped.

- [ ] **Step 6: Close A2's Core half in the backlog**

In `docs/2026-08-05-backlog.md`, under the `### A2.` heading, append:

```markdown
**Core primitives landed 2026-08-09** — `userSet` marker (`GameEntry.UserSet` + `RegistrationRefresh`
precedence), `DataDirMove` (Plan/Execute), `RegistrationChange` (the planner). Spec:
`docs/superpowers/specs/2026-08-09-registration-repair-core-design.md`. Plan:
`docs/superpowers/plans/2026-08-09-registration-repair-core.md`. The UI — repair surface plus the
full edit dialog behind it — is spec 2 and still open.
```

- [ ] **Step 7: Commit**

```bash
git add src/ModManager.Core/RegistrationChange.cs tests/ModManager.Tests/RegistrationChangeTests.cs docs/2026-08-05-backlog.md
git commit -m "feat(registration): say what an edit will actually do, before it happens

Given the stored entry and a proposed one, returns the consequences: which fields
changed, which become pinned, the data-dir move with a real path and a real byte
count, blockers, and notes. The UI renders this and never computes consequences
itself — the move-or-pin prompt has to be truthful, and that is a decision rather
than a rendering detail.

The identity rule is the load-bearing one: Id is half the data-dir key, so an
attempt to change it is blocked outright. Renaming a game leaves the id alone —
a re-slug on a cosmetic rename would silently orphan every disabled mod, profile,
and installed tool.

Marks are additive: editing one field never drops the mark on another, which
would quietly re-expose a deliberate choice to being overwritten. A refusal from
DataDirMove.Plan surfaces as a blocker rather than being swallowed, so a save can
never proceed into a merge the mover already refused."
```

---

## Self-review

**Spec coverage.** Every section of the spec maps to a task:

| spec section | task |
|---|---|
| 1. `userSet` — on disk, back-compat, constants | Task 1 |
| 1. `userSet` — in Core, precedence, Scanner wiring | Task 2 |
| 1. Recorded for every field, consulted for two | Task 1 (doc comment), Task 5 (`FieldsToPin`) |
| 2. Mover — Plan, all four refusals, kind selection | Task 3 |
| 2. Mover — two execution paths, ordering, verify | Task 4 |
| 3. Planner — the plan shape, identity rule, engine note | Task 5 |
| Testing — every listed case | Tasks 1–5 (25 tests total) |

**Placeholder scan.** No "TBD", no "add appropriate error handling", no "similar to Task N". Every
code step contains the actual code. Every test step contains the actual test.

**Type consistency.** `DataDirMovePlan` / `DataDirMoveKind` / `DataDirMoveResult` are defined in
Tasks 3–4 and used with those exact names in Task 5. `GameEntry.UserSetFileExtensions` and siblings
are defined in Task 1 and used verbatim in Tasks 2 and 5. `DataDirMove.Norm` is declared `internal`
in Task 3 because Task 5 calls it from the same assembly.

**One deliberate test-design note.** The cross-volume path is exercised by constructing a plan with
`Kind = CopyVerifyDelete` directly rather than requiring a second volume, so the suite runs anywhere.
The behaviour under test — copy, verify, swap, delete — is identical wherever the paths live.

**Not covered here, by design.** No UI, no view-model, no dialog, nothing writes `userSet`, and
nothing calls `Execute` outside tests. A user who never opens an edit dialog sees no behavior change.
That is spec 2.
