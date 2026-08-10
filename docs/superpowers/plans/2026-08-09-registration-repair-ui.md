# Registration Repair (Surfaces) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give a user a way to see what their game registration claims, compare it against what the launcher actually found, and fix it — without ever orphaning the folder that holds their disabled mods.

**Architecture:** Three small Core additions first (a banner predicate, a second change list, a progress callback), each fully tested. Then a thin App-side `RegistrationRepairService` that owns the save flow, and one `ContentDialog` whose top half renders `GameShape` read-only and whose bottom half hides eight editable fields behind an expander. The dialogs are dumb renderers; everything that decides something lives in Core behind a test.

**Tech Stack:** .NET 10, C# (`<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`), WinUI 3 (`Microsoft.WindowsAppSDK`), xUnit, CommunityToolkit.Mvvm.

**Spec:** `docs/superpowers/specs/2026-08-09-registration-repair-ui-design.md` (commit `e1b5460`)

**Branch note:** this builds on `feat/registration-repair-core` (PR #265), which carries the spec-1 primitives and is not yet merged. Work on that branch or on a branch cut from it.

## Global Constraints

- **Test command is project-scoped, always:** `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. Never run bare `dotnet test` or `dotnet build` at the repo root — the WinUI App project hangs the build. To build the App: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`.
- **Pure core:** nothing under `src/ModManager.Core/` may reference WinUI, WinRT, `Microsoft.UI.*`, or `Windows.UI.*`. `CorePurityTests` fails the suite if it does.
- **camelCase JSON on disk**, always.
- **`<Nullable>enable</Nullable>` + `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`** — a warning is a build failure. No dead null-coalescing against non-nullable members (`ModLocation.Name`/`.Path`, `GameEntry.GroupingRule`, `GameEntry.GameName`, `GameEntry.GameRoot` are all non-nullable).
- **Reversibility:** the move runs before the registry write; a failed write after a successful move re-plans the move in reverse.
- **One `ContentDialog` at a time.** WinUI 3 throws on a nested one. The move-or-pin confirm uses the flag-then-`Hide` hand-off read by `MainWindow` after `ShowAsync` returns.
- **Voice for all user-facing strings:** builder-to-builder, second person, sentence case, period at the end. No emoji. No "seamlessly / robust / leverage / delightful".
- **Fixed wording** (from the spec, do not paraphrase): More menu item `Check setup…`; banner button `Check setup`; dialog eyebrow `GAME // SETUP`, title `Setup`; expander header `Edit setup…`.
- **Commits:** conventional. Areas: `discovery`, `registration`, `datadir`, `settings`, `dialog`, `viewmodel`.
- **After any XAML edit**, clean `obj/` and `bin/` before building — a stale codegen leaves the app crashing at `Connect()` with `InvalidCastException`. Make sure the app is not running when you build.

---

### Task 1: `GameShape.NeedsAttention` — the banner predicate

**Files:**

- Modify: `src/ModManager.Core/Discovery/GameShape.cs` (add a computed property to the `GameShape` record)
- Test: `tests/ModManager.Tests/Discovery/GameShapeTests.cs` (append to the existing class)

**Interfaces:**

- Consumes: the existing `GameShape` record (`ModCount`, `DeclaredLocations` with `Exists`).
- Produces: `bool GameShape.NeedsAttention`. Tasks 5 and 6 read it.

- [ ] **Step 1: Write the failing tests**

Append inside the existing `GameShapeTests` class in `tests/ModManager.Tests/Discovery/GameShapeTests.cs`:

```csharp
    // ---- the banner predicate ----

    // Drift is common and usually harmless: this is the live Elden Ring shape — a declared Mod Engine 2
    // folder that does not exist, while eleven mods load fine by direct-inject. Firing a banner here
    // would flag a working install and teach the user to dismiss the one case that matters.
    [Fact]
    public void A_healthy_drifted_game_does_not_need_attention()
    {
        var (game, root) = FromSoftGame();
        LoaderWithDllMods(root, "SkipTheIntro", "RemoveVignette");

        var shape = GameShape.Of(game);

        Assert.Equal(LocationAlignment.Drifted, shape.Alignment);
        Assert.True(shape.Managed);
        Assert.False(shape.NeedsAttention);
    }

    // The shape that actually hurt: a Cyberpunk registration whose 194 .archive mods showed as zero.
    // Nothing found AND the folder we were told to look in is not there.
    [Fact]
    public void No_mods_and_a_missing_declared_folder_needs_attention()
    {
        var (game, _) = FromSoftGame();   // declares "mod", which this fixture never creates

        var shape = GameShape.Of(game);

        Assert.Equal(0, shape.ModCount);
        Assert.Contains(shape.DeclaredLocations, d => !d.Exists);
        Assert.True(shape.NeedsAttention);
    }

    // An empty game whose folder IS there is just an empty game — nothing to report.
    [Fact]
    public void No_mods_but_a_folder_that_exists_does_not_need_attention()
    {
        var root = TestSupport.TempDir("shape-empty-ok-");
        Directory.CreateDirectory(Path.Combine(root, "mods"));

        var game = new GameEntry
        {
            Id = "empty-game", GameName = "Empty", GameRoot = root, Engine = "ue-pak",
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            DataDir = Path.Combine(root, "_data"),
        };

        Assert.False(GameShape.Of(game).NeedsAttention);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~GameShapeTests"`

Expected: FAIL to compile — `error CS1061: 'GameShape' does not contain a definition for 'NeedsAttention'`.

- [ ] **Step 3: Add the property**

In `src/ModManager.Core/Discovery/GameShape.cs`, add to the `GameShape` record immediately after the `Notes` property:

```csharp
    /// <summary>
    /// Whether this registration's drift is provably costing the user something.
    ///
    /// <para>Nothing found, AND at least one declared location is not on disk. That pair is the shape
    /// that actually hurt: a Cyberpunk registration looking for <c>pak</c> in a folder of
    /// <c>.archive</c> files reported 194 mods as zero.</para>
    ///
    /// <para>Deliberately NOT "is it drifted". Drift is common and usually harmless — Elden Ring is
    /// drifted and perfectly healthy, as is any loader-based install. A banner on every drift would
    /// flag working games and train the user to dismiss the one case worth reading.</para>
    /// </summary>
    public bool NeedsAttention => ModCount == 0 && DeclaredLocations.Any(d => !d.Exists);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~GameShapeTests"`

Expected: PASS, 14 tests (the original 11 plus 3).

- [ ] **Step 5: Run the full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

Expected: PASS. Baseline entering this plan is 1,819 passing / 2 skipped; expect 1,822 passing.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/Discovery/GameShape.cs tests/ModManager.Tests/Discovery/GameShapeTests.cs
git commit -m "feat(discovery): name the drift that is actually costing something

Nothing found, AND a declared location that is not on disk. That pair is the
shape that hurt — a registration looking for pak in a folder of .archive files
reported 194 mods as zero.

Deliberately not 'is it drifted'. Elden Ring is drifted and perfectly healthy, as
is any loader-based install; a banner on every drift would flag working games and
train the user to dismiss the one case worth reading."
```

---

### Task 2: `RegistrationChangePlan.OtherChanges` — changes that carry no pin

**Files:**

- Modify: `src/ModManager.Core/GameEntry.cs` (four new field-name constants)
- Modify: `src/ModManager.Core/RegistrationChange.cs` (the record + `Plan`)
- Test: `tests/ModManager.Tests/RegistrationChangeTests.cs` (append)

**Interfaces:**

- Consumes: `GameEntry.UserSet*` constants from spec 1.
- Produces: `GameEntry.FieldGameName` == `"gameName"`, `FieldEngine` == `"engine"`, `FieldSteamAppId` == `"steamAppId"`, `FieldRequiredLauncher` == `"requiredLauncher"`; and `IReadOnlyList<string> RegistrationChangePlan.OtherChanges`. Task 7 renders it.

- [ ] **Step 1: Write the failing tests**

Append inside the existing `RegistrationChangeTests` class:

```csharp
    // ---- changes that are real but carry no pin ----

    // FieldsChanged is deliberately the four PINNABLE fields. Without a second list, renaming a game
    // saves a real change while the consequences panel sits blank — the exact lie spec 1's final
    // review warned about.
    [Fact]
    public void A_rename_is_reported_as_an_other_change_and_pins_nothing()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.GameName = "Elden Ring (Reforged)";

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Contains(GameEntry.FieldGameName, plan.OtherChanges);
        Assert.Empty(plan.FieldsChanged);
        Assert.Empty(plan.FieldsToPin);
        Assert.True(plan.CanSave);
    }

    [Fact]
    public void Steam_id_and_required_launcher_are_other_changes()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.SteamAppId = "1245620";
        proposed.RequiredLauncher = "ersc_launcher.exe";

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Contains(GameEntry.FieldSteamAppId, plan.OtherChanges);
        Assert.Contains(GameEntry.FieldRequiredLauncher, plan.OtherChanges);
        Assert.Empty(plan.FieldsChanged);
    }

    // A pinnable field belongs in FieldsChanged and must NOT be duplicated into OtherChanges —
    // a UI rendering both lists would show it twice and imply two separate consequences.
    [Fact]
    public void A_pinnable_change_is_never_duplicated_into_other_changes()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.ModLocations = new[] { new ModLocation("mods", "mods", "Game/mod") };

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Contains(GameEntry.UserSetModLocations, plan.FieldsChanged);
        Assert.DoesNotContain(GameEntry.UserSetModLocations, plan.OtherChanges);
    }

    [Fact]
    public void An_engine_change_is_an_other_change_as_well_as_a_note()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));
        var proposed = Copy(stored);
        proposed.Engine = "ue-pak";

        var plan = RegistrationChange.Plan(stored, proposed);

        Assert.Contains(GameEntry.FieldEngine, plan.OtherChanges);
        Assert.NotEmpty(plan.Notes);
    }

    [Fact]
    public void An_unchanged_entry_reports_no_other_changes()
    {
        var stored = Stored(TestSupport.TempDir("rc-"));

        Assert.Empty(RegistrationChange.Plan(stored, Copy(stored)).OtherChanges);
    }
```

Note: the existing `Copy` helper in this test class does not copy `SteamAppId` or `RequiredLauncher`. Extend it so it does — add `SteamAppId = g.SteamAppId, RequiredLauncher = g.RequiredLauncher,` to its initializer. Without that, `Steam_id_and_required_launcher_are_other_changes` would pass for the wrong reason (both sides null → the test would be asserting against its own fixture gap rather than the planner).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~RegistrationChangeTests"`

Expected: FAIL to compile — `error CS0117: 'GameEntry' does not contain a definition for 'FieldGameName'`.

- [ ] **Step 3: Add the field-name constants**

In `src/ModManager.Core/GameEntry.cs`, immediately after the four existing `UserSet*` constants:

```csharp
    // Field names for changes that are real but carry no pin — see RegistrationChangePlan.OtherChanges.
    // The UserSet* constants above are field names too; they are the PINNABLE subset, and they carry
    // that prefix because they are also what gets written into UserSet.
    public const string FieldGameName = "gameName";
    public const string FieldEngine = "engine";
    public const string FieldSteamAppId = "steamAppId";
    public const string FieldRequiredLauncher = "requiredLauncher";
```

- [ ] **Step 4: Add the list to the plan record**

In `src/ModManager.Core/RegistrationChange.cs`, add to `RegistrationChangePlan` immediately after `FieldsChanged`:

```csharp
    /// <summary>
    /// Field names that changed but carry no pin and no data-dir move — they simply save.
    ///
    /// <para>Exists because <see cref="FieldsChanged"/> is deliberately the four PINNABLE fields, so a
    /// rename or a Steam-id correction would otherwise save a real change while a UI bound to
    /// <see cref="FieldsChanged"/> showed nothing. A field appears in one list or the other, never
    /// both.</para>
    /// </summary>
    public required IReadOnlyList<string> OtherChanges { get; init; }
```

- [ ] **Step 5: Populate it in `Plan`**

In `RegistrationChange.Plan`, add after the existing `rootChanged` block and before the engine note, a block collecting the non-pinnable changes:

```csharp
        // Real changes that carry no pin and no move. Kept separate from `changed` so the two lists
        // stay disjoint: a UI renders both, and a field appearing twice would imply two consequences.
        var other = new List<string>();
        if (!string.Equals(stored.GameName, proposed.GameName, StringComparison.Ordinal))
            other.Add(GameEntry.FieldGameName);
        if (!string.Equals(stored.Engine ?? "", proposed.Engine ?? "", StringComparison.OrdinalIgnoreCase))
            other.Add(GameEntry.FieldEngine);
        if (!string.Equals(stored.SteamAppId ?? "", proposed.SteamAppId ?? "", StringComparison.Ordinal))
            other.Add(GameEntry.FieldSteamAppId);
        if (!string.Equals(stored.RequiredLauncher ?? "", proposed.RequiredLauncher ?? "", StringComparison.OrdinalIgnoreCase))
            other.Add(GameEntry.FieldRequiredLauncher);
```

Then add `OtherChanges = other,` to the returned `RegistrationChangePlan` initializer.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~RegistrationChangeTests"`

Expected: PASS, 17 tests (the existing 12 plus 5).

- [ ] **Step 7: Run the full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

Expected: PASS, 1,827 passing / 2 skipped.

- [ ] **Step 8: Commit**

```bash
git add src/ModManager.Core/GameEntry.cs src/ModManager.Core/RegistrationChange.cs tests/ModManager.Tests/RegistrationChangeTests.cs
git commit -m "feat(registration): report the changes that carry no pin

FieldsChanged is deliberately the four PINNABLE fields, so a rename or a Steam-id
correction saved a real change while a UI bound to that list showed nothing —
the exact lie the last review warned about.

OtherChanges carries them. The two lists are disjoint by construction: a field
appears in one or the other, never both, because a UI renders both and a
duplicate would imply two separate consequences."
```

---

### Task 3: per-file progress on `DataDirMove.Execute`

**Files:**

- Modify: `src/ModManager.Core/DataDirMove.cs`
- Test: `tests/ModManager.Tests/DataDirMoveTests.cs` (append to `DataDirMoveExecuteTests`)

**Interfaces:**

- Consumes: `SafeMove.CopyFileVerified(string src, string dest)` (public, verifies size after copy).
- Produces: `DataDirMove.Execute(DataDirMovePlan plan, IProgress<(int Copied, int Total)>? progress = null)`. Task 4 passes a progress object.

- [ ] **Step 1: Write the failing tests**

Append inside `DataDirMoveExecuteTests`:

```csharp
    // A multi-gigabyte move behind a bare spinner is indistinguishable from a hang, and the one thing
    // the user must not do is kill the app mid-move.
    [Fact]
    public void A_copy_move_reports_progress_for_every_file()
    {
        var from = Src("a.txt", "sub/b.txt", "sub/deep/c.txt");
        var to = Path.Combine(TestSupport.TempDir("ddm-to-"), "moved");
        var forced = DataDirMove.Plan(from, to) with { Kind = DataDirMoveKind.CopyVerifyDelete };
        var seen = new List<(int Copied, int Total)>();

        var result = DataDirMove.Execute(forced, new Progress<(int, int)>(p => { lock (seen) seen.Add(p); }));

        Assert.True(result.Moved);
        lock (seen)
        {
            Assert.NotEmpty(seen);
            Assert.All(seen, p => Assert.Equal(3, p.Total));
            Assert.Equal(3, seen.Max(p => p.Copied));
        }
    }

    // A rename is instantaneous; reporting a fake tick would only invite a progress bar that lies.
    [Fact]
    public void A_rename_reports_no_progress()
    {
        var from = Src("a.txt");
        var to = Path.Combine(Path.GetDirectoryName(from)!, "renamed-" + Guid.NewGuid().ToString("N"));
        var seen = new List<(int, int)>();

        var result = DataDirMove.Execute(DataDirMove.Plan(from, to), new Progress<(int, int)>(p => { lock (seen) seen.Add(p); }));

        Assert.True(result.Moved);
        lock (seen) Assert.Empty(seen);
    }

    // The default keeps every existing call site and all current tests compiling unchanged.
    [Fact]
    public void A_null_progress_callback_changes_nothing()
    {
        var from = Src("a.txt", "sub/b.txt");
        var to = Path.Combine(TestSupport.TempDir("ddm-to-"), "moved");
        var forced = DataDirMove.Plan(from, to) with { Kind = DataDirMoveKind.CopyVerifyDelete };

        var result = DataDirMove.Execute(forced, progress: null);

        Assert.True(result.Moved);
        Assert.Equal(2, Directory.GetFiles(to, "*", SearchOption.AllDirectories).Length);
    }
```

`Progress<T>` marshals callbacks to a captured `SynchronizationContext` when one exists and to the thread pool otherwise, so the assertions lock the list. Do not replace `Progress<T>` with a hand-rolled `IProgress<T>` to avoid the lock — a hand-rolled one would not exercise what the App actually passes.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~DataDirMoveExecuteTests"`

Expected: FAIL to compile — `error CS1739: The best overload for 'Execute' does not have a parameter named 'progress'`.

- [ ] **Step 3: Add the parameter and the reporting copy**

In `src/ModManager.Core/DataDirMove.cs`, change the `Execute` signature to:

```csharp
    public static DataDirMoveResult Execute(DataDirMovePlan plan, IProgress<(int Copied, int Total)>? progress = null)
```

In the copy path, replace the single line `SafeMove.CopyDirVerified(plan.From, staging);` with:

```csharp
                CopyTreeReporting(plan.From, staging, plan.FileCount, progress);
```

And add this private helper next to `Verify`:

```csharp
    /// <summary>
    /// Copy the tree file by file, verifying each and reporting after each one.
    ///
    /// <para>Deliberately not <see cref="SafeMove.CopyDirVerified"/>, which is otherwise the same
    /// guarantee: it offers no seam to report from, and a multi-gigabyte move behind a bare spinner
    /// is indistinguishable from a hang — while killing the app mid-move is the one thing a user must
    /// not do. Empty directories are recreated first so a folder the user's tools rely on does not
    /// silently vanish; <see cref="Verify"/> only walks files and would not catch that.</para>
    /// </summary>
    private static void CopyTreeReporting(string from, string to, int total, IProgress<(int, int)>? progress)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));

        var copied = 0;
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            SafeMove.CopyFileVerified(file, Path.Combine(to, Path.GetRelativePath(from, file)));
            progress?.Report((++copied, total));
        }
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter "FullyQualifiedName~DataDirMove"`

Expected: PASS, 22 tests (the existing 19 plus 3).

- [ ] **Step 5: Run the full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

Expected: PASS, 1,830 passing / 2 skipped.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Core/DataDirMove.cs tests/ModManager.Tests/DataDirMoveTests.cs
git commit -m "feat(datadir): report progress while copying

A multi-gigabyte move behind a bare spinner is indistinguishable from a hang, and
killing the app mid-move is the one thing the user must not do.

Copies file by file through SafeMove.CopyFileVerified rather than CopyDirVerified
— same per-file guarantee, but with a seam to report from. Empty directories are
recreated first, because Verify only walks files and would not catch one
vanishing. A rename reports nothing: it is instantaneous, and a fake tick would
only invite a progress bar that lies.

Optional and defaulted, so every existing call site and all 19 current tests
compile and behave unchanged."
```

---

### Task 4: `RegistrationRepairService` — the save flow

**Files:**

- Create: `src/ModManager.App/Services/RegistrationRepairService.cs`
- Modify: `src/ModManager.App/App.xaml.cs` (register in the DI host, beside the other services)

**Interfaces:**

- Consumes: `GameShape.Of`, `RegistrationChange.Plan`, `DataDirMove.Plan` / `.Execute`, `Scanner.DataDirForGame`, `Registry.UpsertGame`, `LauncherService.LoadRegistry` / `.SaveRegistry` / `.NotifyRegistryChanged`.
- Produces:
  - `sealed record RepairSaveOutcome(bool Saved, string Message)`
  - `GameShape Shape(GameEntry game)`
  - `RegistrationChangePlan Preview(GameEntry stored, GameEntry proposed)`
  - `Task<RepairSaveOutcome> SaveAsync(GameEntry stored, GameEntry proposed, bool moveDataDir, IProgress<(int Copied, int Total)>? progress)`

  Tasks 6–8 consume all four.

- [ ] **Step 1: Write the service**

There is no test step here: this type takes `LauncherService`, which reads and writes the real
`%APPDATA%` registry and is App-layer. Every decision it makes is already tested in Core
(`RegistrationChange.Plan`, `DataDirMove.Plan`/`Execute`); what remains is orchestration, and it is
covered by the smoke entries in Task 9. Keep it that way — if you find yourself adding a *decision*
here, move it to Core with a test instead.

Create `src/ModManager.App/Services/RegistrationRepairService.cs`:

```csharp
using ModManager.Core;
using ModManager.Core.Discovery;

namespace ModManager.App.Services;

/// <summary>The outcome of a registration save, in words the status bar can show verbatim.</summary>
public sealed record RepairSaveOutcome(bool Saved, string Message);

/// <summary>
/// Owns the registration-repair flow: read the shape, preview an edit's consequences, and save.
///
/// <para>Deliberately NOT in MainViewModel, which has 14 concrete service dependencies and cannot be
/// constructed in a test. Three times in recent work a decision parked there accumulated defects
/// until it was extracted to Core. This type is orchestration only — every decision it acts on is
/// computed in Core behind a test.</para>
/// </summary>
public sealed class RegistrationRepairService
{
    private readonly LauncherService _svc;

    public RegistrationRepairService(LauncherService svc) => _svc = svc;

    public GameShape Shape(GameEntry game) => GameShape.Of(game);

    public RegistrationChangePlan Preview(GameEntry stored, GameEntry proposed)
        => RegistrationChange.Plan(stored, proposed);

    /// <summary>
    /// Apply an edit.
    ///
    /// <para>ORDER IS THE SAFETY. The data-dir move runs BEFORE the registry write, so a failed move
    /// leaves nothing written anywhere — registration untouched, data untouched. A failed write AFTER
    /// a successful move would orphan the user's only copy of their disabled mods, so that case
    /// re-plans the move in reverse and runs it. If the reverse also fails, both absolute paths go
    /// into the message: silence is the only unacceptable outcome.</para>
    /// </summary>
    public async Task<RepairSaveOutcome> SaveAsync(
        GameEntry stored, GameEntry proposed, bool moveDataDir, IProgress<(int Copied, int Total)>? progress)
    {
        var plan = Preview(stored, proposed);
        if (!plan.CanSave)
            return new RepairSaveOutcome(false, string.Join(" ", plan.Blockers));

        var movedTo = (string?)null;
        var movedFrom = (string?)null;

        if (plan.DataDir is { } move)
        {
            if (moveDataDir)
            {
                var result = await Task.Run(() => DataDirMove.Execute(move, progress));
                if (!result.Moved)
                    return new RepairSaveOutcome(false, result.Error ?? "The launcher data could not be moved.");
                movedFrom = move.From;
                movedTo = move.To;
            }
            else
            {
                // Pin: point the registration at where the data already is. Scanner.DataDirForGame
                // honours an explicit DataDir ahead of its derivation, so nothing moves at all.
                proposed.DataDir = Scanner.DataDirForGame(stored);
            }
        }

        if (plan.FieldsToPin.Count > 0) proposed.UserSet = plan.FieldsToPin;

        try
        {
            var reg = _svc.LoadRegistry();
            _svc.SaveRegistry(Registry.UpsertGame(reg, proposed));
        }
        catch (Exception e)
        {
            if (movedTo is null || movedFrom is null)
                return new RepairSaveOutcome(false, ErrorRemedy.Describe(e));

            // The data is at the new location and the registration still points at the old one — the
            // orphaning this whole feature exists to prevent. Put it back. A fresh plan, not a stored
            // inverse, so the reverse gets the same refusals and free-space check as the forward trip.
            var back = DataDirMove.Execute(DataDirMove.Plan(movedTo, movedFrom));
            return back.Moved
                ? new RepairSaveOutcome(false, "Nothing was changed. " + ErrorRemedy.Describe(e))
                : new RepairSaveOutcome(false,
                    $"Your settings could not be saved, and the launcher data could not be moved back. "
                    + $"It is at {movedTo}; this game still expects it at {movedFrom}.");
        }

        _svc.NotifyRegistryChanged();
        return new RepairSaveOutcome(true, "Saved.");
    }
}
```

- [ ] **Step 2: Register it in the DI host**

In `src/ModManager.App/App.xaml.cs`, beside the other `AddSingleton` registrations for App services, add:

```csharp
        services.AddSingleton<Services.RegistrationRepairService>();
```

- [ ] **Step 3: Build the App project**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`

Expected: `0 Error(s)`.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`

Expected: PASS, 1,830 passing / 2 skipped — unchanged, since this task adds no Core behavior.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/Services/RegistrationRepairService.cs src/ModManager.App/App.xaml.cs
git commit -m "feat(registration): own the repair flow outside the view-model

MainViewModel has 14 concrete service deps and cannot be constructed in a test;
three times recently a decision parked there accumulated defects until it was
extracted. This is orchestration only — every decision it acts on is computed in
Core behind a test.

The move runs before the registry write, so a failed move leaves nothing written
anywhere. A failed write after a successful move would orphan the user's only
copy of their disabled mods, so it re-plans the move in reverse and runs it; if
that also fails, both absolute paths go into the message."
```

---

### Task 5: the banner

**Files:**

- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (a property + set it in `ReloadModsAsync`)
- Modify: `src/ModManager.App/MainWindow.xaml` (a third border inside the existing `VortexBannerArea`)
- Modify: `src/ModManager.App/MainWindow.xaml.cs` (two handlers)

**Interfaces:**

- Consumes: `GameShape.NeedsAttention` (Task 1), `RegistrationRepairService.Shape` (Task 4).
- Produces: `MainViewModel.SetupBannerVisibility`; `MainWindow.OnCheckSetup` (also the More-menu handler, added in Task 8).

- [ ] **Step 1: Add the view-model property**

In `src/ModManager.App/ViewModels/MainViewModel.cs`, beside the existing `OwnedBannerVisibility` /
`ReDeployedBannerVisibility` properties:

```csharp
    // Drift that is provably costing something — nothing found AND a declared folder that is not
    // there. See GameShape.NeedsAttention for why this is not "is it drifted": a banner on every
    // drift would flag Elden Ring and every other loader-based install, all of them working fine.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SetupBannerVisibility))]
    private bool setupNeedsAttention;

    public Visibility SetupBannerVisibility =>
        SetupNeedsAttention ? Visibility.Visible : Visibility.Collapsed;
```

- [ ] **Step 2: Set it during a reload**

In `MainViewModel.ReloadModsAsync`, next to where `OwnedLocations` / `ReDeployedLocations` are
recomputed, add:

```csharp
        SetupNeedsAttention = _ctx is not null && GameShape.Of(_ctx.Game).NeedsAttention;
```

Add `using ModManager.Core.Discovery;` to the file's usings if it is not already present.

- [ ] **Step 3: Add the banner XAML**

In `src/ModManager.App/MainWindow.xaml`, inside `<StackPanel x:Name="VortexBannerArea" Grid.Row="0">`,
after the existing re-deployed border:

```xml
                <!-- Setup banner: shown only when drift is provably costing something (no mods found
                     AND a declared folder that isn't there). Same grammar as the Vortex banners —
                     statement of fact, one verb, a Dismiss. -->
                <Border x:Name="SetupBanner"
                        Visibility="{x:Bind ViewModel.SetupBannerVisibility, Mode=OneWay}"
                        Background="{StaticResource ThemeBarBg}" BorderBrush="{StaticResource ThemeBorder}"
                        BorderThickness="0,0,0,1" Padding="12,6">
                    <StackPanel Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                        <TextBlock Text="No mods found here, and the folder this game is set to look in doesn't exist."
                                   VerticalAlignment="Center" TextWrapping="Wrap" />
                        <Button Content="Check setup" Click="OnCheckSetup" />
                        <Button Content="Dismiss" Click="OnDismissSetupBanner" />
                    </StackPanel>
                </Border>
```

- [ ] **Step 4: Add the handlers**

In `src/ModManager.App/MainWindow.xaml.cs`, beside `OnDismissVortexBanner`:

```csharp
    // Session-level dismiss, matching the Vortex banner: a later rescan may re-show it, which is
    // acceptable — the alternative is a persisted "don't tell me" that outlives the problem.
    private void OnDismissSetupBanner(object sender, RoutedEventArgs e)
        => SetupBanner.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
```

Add a temporary stub for `OnCheckSetup` so this task builds on its own; Task 8 replaces its body:

```csharp
    private async void OnCheckSetup(object sender, RoutedEventArgs e)
    {
        // Body lands in Task 8, once GameSetupDialog exists.
        await Task.CompletedTask;
    }
```

- [ ] **Step 5: Clean and build**

Run:

```bash
rm -rf src/ModManager.App/obj src/ModManager.App/bin
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: `0 Error(s)`. The clean is required — a stale XAML codegen leaves the app crashing at
`Connect()` with `InvalidCastException`.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.App/ViewModels/MainViewModel.cs src/ModManager.App/MainWindow.xaml src/ModManager.App/MainWindow.xaml.cs
git commit -m "feat(viewmodel): surface a registration that is costing the user mods

A third banner in the existing banner area, on the same grammar as the Vortex
ones — statement of fact, one verb, a Dismiss, escalation by border colour only.

Shown only when GameShape.NeedsAttention: nothing found AND a declared folder
that is not there. Not on drift generally, which would flag Elden Ring and every
loader-based install while all of them work fine."
```

---

### Task 6: `GameSetupDialog` — the diagnosis half

**Files:**

- Create: `src/ModManager.App/GameSetupDialog.xaml`
- Create: `src/ModManager.App/GameSetupDialog.xaml.cs`

**Interfaces:**

- Consumes: `RegistrationRepairService.Shape` (Task 4), `GameShape` and its `Notes` / `ContentRoots` / `DeclaredLocations` / `Loaders` / `ModCount`.
- Produces: `GameSetupDialog(GameEntry game, RegistrationRepairService repair)`. Task 7 adds the edit half to these same two files; Task 8 shows it.

- [ ] **Step 1: Write the XAML**

Create `src/ModManager.App/GameSetupDialog.xaml`:

```xml
<ContentDialog
    x:Class="ModManager.App.GameSetupDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    AutomationProperties.Name="Setup"
    PrimaryButtonText="Save"
    CloseButtonText="Close"
    DefaultButton="Close"
    IsPrimaryButtonEnabled="False">

    <!-- Dialog shell (vibe-glow F-008): 3px accent rail + mono-caps stencil eyebrow + title. -->
    <ContentDialog.Title>
        <StackPanel Spacing="6">
            <Border Height="3" Background="{StaticResource ThemeAccent}" Margin="-24,0,-24,4"
                    AutomationProperties.AccessibilityView="Raw" />
            <TextBlock Text="GAME // SETUP" FontFamily="{StaticResource MonoFontFamily}"
                       AutomationProperties.AccessibilityView="Raw"
                       FontSize="{StaticResource TagFontSize}" CharacterSpacing="80"
                       Foreground="{StaticResource ThemeInkDim}" />
            <TextBlock Text="Setup" FontSize="{StaticResource ViewTitleFontSize}" FontWeight="SemiBold" />
        </StackPanel>
    </ContentDialog.Title>

    <!-- A ContentDialog does not scroll its own content; mirrors SettingsDialog. -->
    <ScrollViewer MaxHeight="640">
        <StackPanel Spacing="12" Width="440">

            <!-- The diagnosis, read-only, rendered straight from GameShape. -->
            <Grid ColumnSpacing="12" RowSpacing="4">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="120" />
                    <ColumnDefinition Width="*" />
                </Grid.ColumnDefinitions>
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" /><RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" /><RowDefinition Height="Auto" />
                </Grid.RowDefinitions>

                <TextBlock Grid.Row="0" Grid.Column="0" Text="Mods found" Foreground="{StaticResource ThemeInkDim}" />
                <TextBlock Grid.Row="0" Grid.Column="1" x:Name="ModsFoundText" TextWrapping="Wrap" />

                <TextBlock Grid.Row="1" Grid.Column="0" Text="Loaded by" Foreground="{StaticResource ThemeInkDim}"
                           x:Name="LoadedByLabel" />
                <TextBlock Grid.Row="1" Grid.Column="1" x:Name="LoadedByText" TextWrapping="Wrap" />

                <TextBlock Grid.Row="2" Grid.Column="0" Text="Living in" Foreground="{StaticResource ThemeInkDim}"
                           x:Name="LivingInLabel" />
                <TextBlock Grid.Row="2" Grid.Column="1" x:Name="LivingInText" TextWrapping="Wrap" />

                <TextBlock Grid.Row="3" Grid.Column="0" Text="Set to look in" Foreground="{StaticResource ThemeInkDim}" />
                <TextBlock Grid.Row="3" Grid.Column="1" x:Name="DeclaredText" TextWrapping="Wrap" />
            </Grid>

            <!-- The verdict, in the launcher's own words (GameShape.Notes). -->
            <TextBlock x:Name="VerdictText" TextWrapping="Wrap" Foreground="{StaticResource ThemeInkDim}" />

            <!-- Task 7 inserts the "Edit setup…" Expander here. -->

        </StackPanel>
    </ScrollViewer>
</ContentDialog>
```

- [ ] **Step 2: Write the code-behind**

Create `src/ModManager.App/GameSetupDialog.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.Services;
using ModManager.Core;
using ModManager.Core.Discovery;

namespace ModManager.App;

/// <summary>
/// What this game's registration claims, next to what the launcher actually found — and, behind an
/// expander, the fields to change it.
///
/// <para>Diagnosis first, on purpose. The common outcome is that NOTHING is wrong: a game whose mods
/// load by a route the registration does not describe is normal (Elden Ring's eleven mods load by
/// direct-inject while it declares a Mod Engine 2 folder that does not exist). A surface that opened
/// straight into editable fields would imply something needed editing.</para>
///
/// <para>One dialog rather than two because WinUI 3 permits one ContentDialog per XamlRoot; chaining
/// diagnose to edit to confirm would be two nested hand-offs. This leaves exactly one, for the
/// move-or-pin confirm.</para>
/// </summary>
public sealed partial class GameSetupDialog : ContentDialog
{
    private readonly GameEntry _game;
    private readonly RegistrationRepairService _repair;
    private readonly GameShape _shape;

    public GameSetupDialog(GameEntry game, RegistrationRepairService repair)
    {
        InitializeComponent();
        _game = game;
        _repair = repair;
        _shape = repair.Shape(game);
        DialogTheming.Apply(this);   // popup-scope theme brushes
        RenderDiagnosis();
    }

    private void RenderDiagnosis()
    {
        ModsFoundText.Text = _shape.ModCount switch
        {
            0 => "None.",
            1 => "1 mod.",
            _ => $"{_shape.ModCount} mods.",
        };

        // A loader explains why sibling mods load from a folder the registration never mentions —
        // without it named, the drift below reads as misconfiguration with no cause.
        var hasLoaders = _shape.Loaders.Count > 0;
        LoadedByLabel.Visibility = hasLoaders ? Visibility.Visible : Visibility.Collapsed;
        LoadedByText.Visibility = hasLoaders ? Visibility.Visible : Visibility.Collapsed;
        LoadedByText.Text = string.Join(", ", _shape.Loaders);

        var roots = _shape.ContentRoots
            .Select(r => string.IsNullOrEmpty(r.RelativePath) ? "the game folder" : r.RelativePath)
            .ToList();
        LivingInLabel.Visibility = roots.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LivingInText.Visibility = roots.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LivingInText.Text = string.Join(", ", roots);

        DeclaredText.Text = string.Join(", ", _shape.DeclaredLocations
            .Select(d => d.Exists ? d.Path : d.Path + "  (this folder doesn't exist)"));

        // Rendered verbatim: GameShape already states whether drift is a problem, and re-wording it
        // here would let the dialog and the MCP tool tell the user two different stories.
        VerdictText.Text = string.Join(" ", _shape.Notes);
    }
}
```

- [ ] **Step 3: Clean and build**

Run:

```bash
rm -rf src/ModManager.App/obj src/ModManager.App/bin
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: `0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/GameSetupDialog.xaml src/ModManager.App/GameSetupDialog.xaml.cs
git commit -m "feat(dialog): show what a registration claims next to what is there

Diagnosis first, on purpose: the common outcome is that nothing is wrong. A game
whose mods load by a route the registration does not describe is normal — Elden
Ring's eleven load by direct-inject while it declares a Mod Engine 2 folder that
does not exist. Opening straight into editable fields would imply otherwise.

The verdict is GameShape.Notes verbatim. Re-wording it here would let the dialog
and the get_game_shape MCP tool tell the user two different stories."
```

---

### Task 7: `GameSetupDialog` — the edit half

**Files:**

- Modify: `src/ModManager.App/GameSetupDialog.xaml` (the `Expander` and consequences panel)
- Modify: `src/ModManager.App/GameSetupDialog.xaml.cs` (seed fields, live preview, build the proposed entry)

**Interfaces:**

- Consumes: `RegistrationRepairService.Preview` (Task 4), `RegistrationChangePlan.OtherChanges` (Task 2), `EnginePresets.Presets`.
- Produces: `GameSetupDialog.Proposed` (`GameEntry?`), `GameSetupDialog.MoveDataDirRequested` (`DataDirMovePlan?`). Task 8 reads both.

- [ ] **Step 1: Add the expander XAML**

In `src/ModManager.App/GameSetupDialog.xaml`, replace the comment
`<!-- Task 7 inserts the "Edit setup…" Expander here. -->` with:

```xml
            <Expander x:Name="EditExpander" Header="Edit setup…" HorizontalAlignment="Stretch"
                      HorizontalContentAlignment="Stretch">
                <StackPanel Spacing="8">
                    <TextBlock Text="Game name" Foreground="{StaticResource ThemeInkDim}" />
                    <TextBox x:Name="NameBox" TextChanged="OnFieldChanged" />

                    <TextBlock Text="Game folder" Foreground="{StaticResource ThemeInkDim}" />
                    <Grid ColumnSpacing="8">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" /><ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <TextBox x:Name="FolderBox" Grid.Column="0" TextChanged="OnFieldChanged" />
                        <Button x:Name="BrowseButton" Grid.Column="1" Content="Browse…" Click="OnBrowse" />
                    </Grid>

                    <TextBlock Text="Engine" Foreground="{StaticResource ThemeInkDim}" />
                    <ComboBox x:Name="EngineBox" HorizontalAlignment="Stretch"
                              SelectionChanged="OnEngineChanged" DisplayMemberPath="Label" />

                    <TextBlock Text="Mod folder (relative to the game folder)" Foreground="{StaticResource ThemeInkDim}" />
                    <TextBox x:Name="ModPathBox" TextChanged="OnFieldChanged" />

                    <TextBlock Text="File extensions (comma separated)" Foreground="{StaticResource ThemeInkDim}" />
                    <TextBox x:Name="ExtensionsBox" TextChanged="OnFieldChanged" />

                    <TextBlock Text="Grouping rule" Foreground="{StaticResource ThemeInkDim}" />
                    <TextBox x:Name="GroupingBox" TextChanged="OnFieldChanged" />

                    <TextBlock Text="Steam App ID" Foreground="{StaticResource ThemeInkDim}" />
                    <TextBox x:Name="SteamBox" TextChanged="OnFieldChanged" />

                    <TextBlock Text="Required launcher (relative to the game folder)" Foreground="{StaticResource ThemeInkDim}" />
                    <TextBox x:Name="LauncherBox" TextChanged="OnFieldChanged" />

                    <!-- Consequences, live. FieldsChanged and OtherChanges answer different questions,
                         so they are rendered as separate lines rather than one merged list. -->
                    <Border x:Name="ConsequencesPanel" Visibility="Collapsed"
                            Background="{StaticResource ThemeBarBg}" BorderBrush="{StaticResource ThemeBorder}"
                            BorderThickness="1" Padding="10" CornerRadius="4">
                        <StackPanel Spacing="4">
                            <TextBlock Text="Saving will:" FontWeight="SemiBold" />
                            <TextBlock x:Name="ConsequencesText" TextWrapping="Wrap" />
                        </StackPanel>
                    </Border>

                    <TextBlock x:Name="BlockerText" TextWrapping="Wrap"
                               Foreground="{StaticResource ThemeDanger}" Visibility="Collapsed" />
                </StackPanel>
            </Expander>
```

- [ ] **Step 2: Seed the fields and wire the live preview**

In `src/ModManager.App/GameSetupDialog.xaml.cs`, add these members and methods to the class:

```csharp
    /// <summary>The edited entry, or null when the user closed without saving. Read by MainWindow.</summary>
    public GameEntry? Proposed { get; private set; }

    /// <summary>Set when saving implies a data-dir move the user must decide about. MainWindow shows
    /// the move-or-pin confirm AFTER this dialog closes — WinUI 3 forbids a nested ContentDialog.</summary>
    public DataDirMovePlan? MoveDataDirRequested { get; private set; }

    private bool _seeding;

    private void SeedFields()
    {
        _seeding = true;   // suppress the live preview while we populate
        NameBox.Text = _game.GameName;
        FolderBox.Text = _game.GameRoot;
        ModPathBox.Text = _game.ModLocations.Count > 0 ? _game.ModLocations[0].Path : "";
        ExtensionsBox.Text = string.Join(", ", _game.FileExtensions);
        GroupingBox.Text = _game.GroupingRule;
        SteamBox.Text = _game.SteamAppId ?? "";
        LauncherBox.Text = _game.RequiredLauncher ?? "";

        EngineBox.ItemsSource = EnginePresets.Presets
            .Select(p => new EngineOption(p.Key, p.Value.Label)).ToList();
        EngineBox.SelectedItem = ((List<EngineOption>)EngineBox.ItemsSource)
            .FirstOrDefault(o => string.Equals(o.Key, _game.Engine, StringComparison.OrdinalIgnoreCase));
        _seeding = false;
    }

    private sealed record EngineOption(string Key, string Label);

    private void OnFieldChanged(object sender, TextChangedEventArgs e) => Preview();

    // Unlike AddGameDialog, changing the engine here must NOT rewrite the mod-path box. Auto-filling a
    // field the user did not type makes RegistrationChange read it as their choice; the Core planner
    // drops preset-equal values on an engine change to defend against exactly that, and there is no
    // reason to hand it the problem in the first place.
    private void OnEngineChanged(object sender, SelectionChangedEventArgs e) => Preview();

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) { FolderBox.Text = folder.Path; Preview(); }
    }

    private GameEntry BuildProposed()
    {
        var exts = ExtensionsBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var loc = _game.ModLocations.Count > 0 ? _game.ModLocations[0] : new ModLocation("mods", "mods", "mods");

        return new GameEntry
        {
            // Id is IMMUTABLE across an edit: it is half the key the data-dir path derives from, so
            // re-slugging it on a rename would orphan every disabled mod, profile, and installed tool.
            Id = _game.Id,
            GameName = NameBox.Text.Trim(),
            Engine = (EngineBox.SelectedItem as EngineOption)?.Key ?? _game.Engine,
            WindowTitle = _game.WindowTitle,
            GameRoot = FolderBox.Text.Trim(),
            FileExtensions = exts,
            GroupingRule = GroupingBox.Text.Trim(),
            ModLocations = new[] { loc with { Path = ModPathBox.Text.Trim() } },
            SteamAppId = string.IsNullOrWhiteSpace(SteamBox.Text) ? null : SteamBox.Text.Trim(),
            LaunchUrl = _game.LaunchUrl,
            LaunchExe = _game.LaunchExe,
            LaunchTargets = _game.LaunchTargets,
            ModEngineConfig = _game.ModEngineConfig,
            DataDir = _game.DataDir,
            CurseforgeGameId = _game.CurseforgeGameId,
            ScanSubfolders = _game.ScanSubfolders,
            SaveDir = _game.SaveDir,
            RequiredLauncher = string.IsNullOrWhiteSpace(LauncherBox.Text) ? null : LauncherBox.Text.Trim(),
            SaveModPath = _game.SaveModPath,
            SaveModForbidden = _game.SaveModForbidden,
            NexusGameDomain = _game.NexusGameDomain,
            AutoBackupOnLaunch = _game.AutoBackupOnLaunch,
            SaveAutoKeep = _game.SaveAutoKeep,
            LastKnownSteamBuildId = _game.LastKnownSteamBuildId,
            StoreSource = _game.StoreSource,
            LastLaunchedUtc = _game.LastLaunchedUtc,
            UserSet = _game.UserSet,
        };
    }

    private void Preview()
    {
        if (_seeding) return;

        var plan = _repair.Preview(_game, BuildProposed());
        var lines = new List<string>();

        foreach (var f in plan.FieldsChanged)
            lines.Add($"lock in your {Human(f)}, so future definition updates leave it alone");
        foreach (var f in plan.OtherChanges)
            lines.Add($"update the {Human(f)}");
        if (plan.DataDir is { } move)
            lines.Add($"ask whether to move this game's launcher data ({move.FileCount} files) from {move.From}");
        foreach (var n in plan.Notes)
            lines.Add(n);

        ConsequencesText.Text = lines.Count > 0 ? "• " + string.Join("\n• ", lines) : "";
        ConsequencesPanel.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        BlockerText.Text = string.Join(" ", plan.Blockers);
        BlockerText.Visibility = plan.Blockers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Nothing to save is as good a reason to disable Save as a blocker is.
        IsPrimaryButtonEnabled = plan.CanSave
            && (plan.FieldsChanged.Count > 0 || plan.OtherChanges.Count > 0 || plan.DataDir is not null);
    }

    private static string Human(string field) => field switch
    {
        GameEntry.UserSetFileExtensions => "file extensions",
        GameEntry.UserSetGroupingRule => "grouping rule",
        GameEntry.UserSetModLocations => "mod folder",
        GameEntry.UserSetGameRoot => "game folder",
        GameEntry.FieldGameName => "game name",
        GameEntry.FieldEngine => "engine",
        GameEntry.FieldSteamAppId => "Steam App ID",
        GameEntry.FieldRequiredLauncher => "required launcher",
        _ => field,
    };
```

Change the constructor to take the window handle (needed by the folder picker) and call `SeedFields`:

```csharp
    private readonly IntPtr _hwnd;

    public GameSetupDialog(IntPtr hwnd, GameEntry game, RegistrationRepairService repair)
    {
        InitializeComponent();
        _hwnd = hwnd;
        _game = game;
        _repair = repair;
        _shape = repair.Shape(game);
        DialogTheming.Apply(this);
        RenderDiagnosis();
        SeedFields();
    }
```

Add `using Microsoft.UI.Xaml.Controls;` and `using ModManager.Core;` if not already present.

- [ ] **Step 3: Handle Save**

Add to the class, and wire `PrimaryButtonClick="OnSave"` on the `ContentDialog` element in the XAML:

```csharp
    // Set the outputs and let the dialog close. The move-or-pin confirm and the save itself happen in
    // MainWindow AFTER this returns — a second ContentDialog cannot open while this one is up.
    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var proposed = BuildProposed();
        var plan = _repair.Preview(_game, proposed);
        if (!plan.CanSave) { args.Cancel = true; return; }   // keep the dialog open; typed edits survive
        Proposed = proposed;
        MoveDataDirRequested = plan.DataDir;
    }
```

- [ ] **Step 4: Clean and build**

Run:

```bash
rm -rf src/ModManager.App/obj src/ModManager.App/bin
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
```

Expected: `0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/GameSetupDialog.xaml src/ModManager.App/GameSetupDialog.xaml.cs
git commit -m "feat(dialog): edit the setup, with the consequences shown as you type

Eight fields behind a collapsed expander, under the diagnosis. FieldsChanged and
OtherChanges render as separate lines because they answer different questions —
what gets locked in against future definition updates, and what merely saves.

Changing the engine deliberately does NOT rewrite the mod-path box the way
AddGameDialog does. Auto-filling a field the user did not type makes the planner
read it as their choice; Core already defends against that on an engine change,
and there is no reason to hand it the problem.

Save is disabled when a blocker is present or nothing actually changed, and a
blocked save keeps the dialog open so typed edits survive."
```

---

### Task 8: wiring — the menu item, the confirm, and the save

**Files:**

- Modify: `src/ModManager.App/MainWindow.xaml` (a `Check setup…` menu item)
- Modify: `src/ModManager.App/MainWindow.xaml.cs` (`OnCheckSetup` body + the confirm)

**Interfaces:**

- Consumes: `GameSetupDialog` (Tasks 6–7), `RegistrationRepairService.SaveAsync` (Task 4).
- Produces: nothing further.

- [ ] **Step 1: Add the menu item**

In `src/ModManager.App/MainWindow.xaml`, in the More flyout, immediately before the
`Remove this game…` item:

```xml
                            <MenuFlyoutItem Text="Check setup…" Click="OnCheckSetup"
                                            ToolTipService.ToolTip="See what this game's setup says, next to what the launcher actually found.">
                                <MenuFlyoutItem.Icon>
                                    <FontIcon Glyph="&#xE713;" />
                                </MenuFlyoutItem.Icon>
                            </MenuFlyoutItem>
```

- [ ] **Step 2: Replace the `OnCheckSetup` stub**

In `src/ModManager.App/MainWindow.xaml.cs`, replace the Task 5 stub with:

```csharp
    private async void OnCheckSetup(object sender, RoutedEventArgs e)
    {
        var game = ViewModel.ActiveContextPublic?.Game;
        if (game is null) return;

        var repair = App.AppHost.Services.GetRequiredService<Services.RegistrationRepairService>();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dialog = new GameSetupDialog(hwnd, game, repair) { XamlRoot = Content.XamlRoot };
        await dialog.ShowAsync();

        if (dialog.Proposed is not { } proposed) return;

        // The move-or-pin decision, and the save, happen HERE rather than inside the dialog: WinUI 3
        // permits one ContentDialog per XamlRoot, so a confirm cannot open while the setup dialog is up.
        var move = true;
        if (dialog.MoveDataDirRequested is { } plan)
        {
            var confirm = new ContentDialog
            {
                Title = "Move this game's launcher data?",
                Content = $"You changed the game folder. This game's launcher data — disabled mods, "
                          + $"profiles, saves, installed tools — is {plan.FileCount} files at {plan.From}.\n\n"
                          + "Move it next to the new folder, or leave it where it is. Leaving it works "
                          + "fine; nothing is lost either way.",
                PrimaryButtonText = "Move it",
                SecondaryButtonText = "Leave it",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = Content.XamlRoot,
            };
            Services.DialogTheming.Apply(confirm);
            var answer = await confirm.ShowAsync();
            if (answer == ContentDialogResult.None) return;      // Cancel: change nothing
            move = answer == ContentDialogResult.Primary;
        }

        await ViewModel.SaveRegistrationAsync(repair, game, proposed, move);
    }
```

- [ ] **Step 3: Add the view-model entry point**

In `src/ModManager.App/ViewModels/MainViewModel.cs`, beside `RemoveActiveGameAsync`:

```csharp
    /// <summary>Apply a registration edit, then reload. The service owns the ordering that makes a
    /// failure recoverable; this method owns only the busy state and the status line.
    ///
    /// <para>The service arrives as a PARAMETER rather than a constructor dependency or a service-
    /// locator lookup. This constructor already takes 14 concrete services and is the reason nothing
    /// here can be tested; a fifteenth would make that worse, and a locator call would hide the
    /// dependency entirely. MainWindow has already resolved it — let it hand it over.</para></summary>
    public async Task SaveRegistrationAsync(
        Services.RegistrationRepairService repair, GameEntry stored, GameEntry proposed, bool moveDataDir)
    {
        IsBusy = true;
        try
        {
            // Per-file ticks, no Stop: a data-dir move must not be interruptible mid-flight, and the
            // staging-then-swap design is exactly what makes that safe.
            var progress = new Progress<(int Copied, int Total)>(p =>
                AmbientStatus($"Moving launcher data: {p.Copied} of {p.Total} files."));

            var outcome = await repair.SaveAsync(stored, proposed, moveDataDir, progress);
            AnswerStatus(outcome.Message);
            if (outcome.Saved) await LoadAsync();
        }
        catch (Exception e) { AnswerStatus(ErrorRemedy.Describe(e)); }
        finally { IsBusy = false; }
    }
```

- [ ] **Step 4: Clean, build, and run the suite**

Run:

```bash
rm -rf src/ModManager.App/obj src/ModManager.App/bin
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64
dotnet test tests/ModManager.Tests/ModManager.Tests.csproj
```

Expected: `0 Error(s)`; 1,830 passing / 2 skipped.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/MainWindow.xaml src/ModManager.App/MainWindow.xaml.cs src/ModManager.App/ViewModels/MainViewModel.cs
git commit -m "feat(settings): reach the setup dialog, and apply what it returns

Check setup... in the More menu, beside Remove this game. 'Check' rather than
'Edit' because the common outcome is that nothing is wrong, and an entry point
promising editing implies something needs editing.

The move-or-pin confirm opens from MainWindow after the setup dialog closes —
WinUI 3 permits one ContentDialog per XamlRoot, so it cannot nest. Leaving the
data where it is is the default button: it works fine and moves nothing.

The move reports per-file ticks and offers no Stop. Interrupting mid-flight is
what the staging-then-swap design exists to make unnecessary."
```

---

### Task 9: smoke entries and closing A2

**Files:**

- Modify: `docs/smoke-tests/pending.md`
- Modify: `docs/2026-08-05-backlog.md`

**Interfaces:** none — documentation only.

- [ ] **Step 1: Append the smoke section**

Add to `docs/smoke-tests/pending.md`, at the end of the file:

```markdown
## PR (spec 2) — registration repair surfaces

**Shipped:** `Check setup…` in the More menu and a conditional banner, both opening one
`GAME // SETUP` dialog that renders `GameShape` read-only with eight editable fields behind an
expander; a move-or-pin confirm for the data dir; `RegistrationRepairService` owning the save order.

**Core tests cover** the banner predicate, the two change lists, the progress callback, and every
failure path in `DataDirMove.Execute` including a source file held open. **What they can't cover:**
the App layer is headless-untestable. These steps check that the UI tells the truth about what Core
already decided.

**Smoke steps:**

1. **Elden Ring reads as healthy.** More → `Check setup…`. Expect: "11 mods", "Loaded by Elden Mod
   Loader", "Living in mods", "Set to look in mod (this folder doesn't exist)", and a verdict saying
   the mods load normally and this is drift, not damage. No banner anywhere. *Why it matters:* the
   dialog must not imply a repair on a working install — that is the failure mode the whole
   diagnose-first shape exists to avoid.
2. **The banner appears only when it should.** Temporarily point a test game's mod folder at a
   non-existent path with no mods present; expect the banner. `Dismiss` collapses it for the session.
   Switch away and back; a rescan may re-show it, which is acceptable.
3. **Save is disabled when it should be.** Open the expander and change nothing — Save stays
   disabled. Blank the game folder — Save stays disabled and the blocker text appears in danger
   colour. Restore it — Save enables.
4. **A rename shows a consequence.** Change only the game name. Expect "update the game name" under
   "Saving will:" and NO pin line. Save, and confirm the title bar and library row update. *Why it
   matters:* `FieldsChanged` excludes `gameName`, so a blank panel here would mean `OtherChanges` is
   not wired.
5. **A real data-dir move.** Change the game folder to a path on another drive. Expect the move-or-pin
   confirm naming the real file count and source path. Choose "Move it" and watch the status line tick
   per file. Afterwards: the mods still list, and the data dir exists at the new location and not the
   old.
6. **Pin moves nothing.** Same again, choosing "Leave it". Expect no progress ticks, the mods still
   listing, and `games.json` carrying an explicit `dataDir` pointing at the original location.
7. **Cancel is inert.** Open the dialog, type into several fields, press Close. Expect no change to
   `games.json` and no change on disk.
```

- [ ] **Step 2: Close A2 in the backlog**

In `docs/2026-08-05-backlog.md`, under the `### A2.` heading, replace the "Core primitives landed"
paragraph's final sentence (`The UI — repair surface plus the full edit dialog behind it — is spec 2
and still open.`) with:

```markdown
**UI landed 2026-08-09** — `Check setup…` plus a conditional banner, one `GAME // SETUP` dialog
(diagnosis from `GameShape`, eight fields behind an expander, live consequences), a move-or-pin
confirm, and `RegistrationRepairService`. Spec:
`docs/superpowers/specs/2026-08-09-registration-repair-ui-design.md`. Plan:
`docs/superpowers/plans/2026-08-09-registration-repair-ui.md`. Smoke steps in
`docs/smoke-tests/pending.md`. **A2 is closed.**
```

- [ ] **Step 3: Commit**

```bash
git add docs/smoke-tests/pending.md docs/2026-08-05-backlog.md
git commit -m "docs(smoke): registration repair steps, and close A2

Seven steps, each naming why it matters. The first is the one that would be
easiest to skip and hardest to notice: Elden Ring must read as healthy. A dialog
that implies a repair on a working install is the exact failure the
diagnose-first shape exists to avoid."
```

---

## Self-review

**Spec coverage.** Every section of the spec maps to a task:

| spec section | task |
|---|---|
| The banner, and its narrow predicate | 1 (predicate), 5 (surface) |
| Fixed wording across entry points | 5 (banner button), 8 (menu item), 6 (eyebrow/title), 7 (expander) |
| The dialog — diagnosis half, `GameShape.Notes` verbatim | 6 |
| The dialog — eight fields, consequences panel, Save gating | 7 |
| The confirm — flag-then-`Hide` hand-off | 7 (the two properties), 8 (the confirm itself) |
| Core addition 1 — `NeedsAttention` | 1 |
| Core addition 2 — `OtherChanges` | 2 |
| Core addition 3 — progress callback, no cancellation | 3 |
| The save path, move-before-write, reverse on failed write | 4 |
| Pin writes `DataDir`, moves nothing | 4 |
| Logic outside `MainViewModel` | 4 |
| Testing — Core tests | 1, 2, 3 |
| Testing — smoke entries | 9 |

**Placeholder scan.** No "TBD", no "add appropriate error handling", no "similar to Task N". Every
code step carries the actual code; every test step carries the actual test.

**Type consistency.** `GameShape.NeedsAttention` (Task 1) is read in Tasks 5 and 6.
`RegistrationChangePlan.OtherChanges` (Task 2) is read in Task 7's `Preview`.
`GameEntry.Field*` constants (Task 2) are used in Task 7's `Human`. `DataDirMove.Execute`'s
`IProgress<(int Copied, int Total)>` (Task 3) matches the `Progress<(int Copied, int Total)>`
constructed in Task 8. `RepairSaveOutcome`, `Shape`, `Preview`, `SaveAsync` (Task 4) are used in
Tasks 5–8. `GameSetupDialog(IntPtr, GameEntry, RegistrationRepairService)` settles in Task 7 and is
called with that signature in Task 8 — Task 6 creates the two-argument form and Task 7 replaces it,
which is called out explicitly in Task 7's step 2.

**Two things a reviewer should know are deliberate.**

- **Task 4 has no unit test.** `RegistrationRepairService` takes `LauncherService`, which reads and
  writes the real `%APPDATA%` registry. Every decision it acts on is already tested in Core; what is
  left is ordering, covered by smoke steps 5–7. The task text says so, and says what to do if a
  decision starts creeping in.
- **Task 5 ships a stub handler.** `OnCheckSetup` is empty until Task 8, so Task 5 builds and can be
  reviewed on its own rather than being merged into the dialog work.
