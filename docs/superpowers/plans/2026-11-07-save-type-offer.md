# Save-Type Offer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** At Play, when the save file the launch will read has no characters and another declared save type does, offer to copy one over (snapshot first), with warnings on copies into vanilla.

**Architecture:** Three pure Core pieces: manifest-declared save types with a `kind`, Reforged launch recognition, and `SaveOffer.Evaluate`. Plus one Core probe that reads save files (target first, so the common case reads one file). The App adds one dialog and one call in `MainViewModel.LaunchTargetExplicit`, which every real launch passes through.

**Tech Stack:** .NET 10, C#, xUnit, WinUI 3.

**Spec:** `docs/superpowers/specs/2026-11-07-save-type-offer-design.md` (decided 2026-11-07).

## Global Constraints

- Core stays pure: no WinUI/WinRT in `src/ModManager.Core/` (`CorePurityTests`).
- Every on-disk JSON shape is camelCase, with a round-trip test that asserts the key string. Add each new shape to `.claude/rules/camelcase-json-on-disk.md`.
- Reversible: the existing target is snapshotted (`SaveManager.Backup(..., "before-clone", auto: true)`) before `CloneToType(..., overwrite: true)`. The source is never written. No `File.Delete`.
- Never forced: nothing copies without a click; closing the dialog launches nothing.
- Every new interactive control ships with an `AutomationProperties.AutomationId` (`.claude/rules/automation-ids.md`); ids go on controls/TextBlocks, never on a `Border`/`Grid`.
- Never offer when the target has even one character (the copy replaces the whole file).
- An unreadable file is unknown: never a source; as target, no offer.
- Unknown manifest words degrade, never throw (an enum in the feed would drop the whole feed).
- Build/test only by explicit project: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`, `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`.
- UI copy: sentence case, second person, periods, no emoji.

## Review Focus

1. **Several base names in one save folder** (`ER0000.sl2` plus a stray `ER0000 - Copy.co2.bak`): `.bak` and non-matching files are never sources; only exact `<base><declared ext>` files count. Pinned in Task 3.
2. **Steam Cloud or another process holds the target open when the copy runs**: the copy throws, the dialog result says why, and nothing launches. The snapshot already exists. Pinned in Task 6 (the copy is in a try/catch that returns "don't launch").
3. **Reforged's `altsaves.toml` with differing `extension` and `seamless_extension`**: the save it reads is ambiguous, so no offer. Pinned in Task 2.
4. **A launch target with no save folder known** (`SaveDir` null or missing): no probe, no offer, the launch proceeds. Pinned in Task 4.
5. **Probe cost on every Play**: the target is read first, and a target with characters stops the probe. This is a 28 MB read on Elden Ring; it must run off the UI thread. Pinned in Task 4 (target-first short circuit test) and Task 6 (`Task.Run`).

---

### Task 1: Save types carry a kind, and come from the manifest

**Files:**
- Modify: `src/ModManager.Core/GameSaveTypes.cs` (SaveType gains `Kind`; `Resolve` prefers manifest types; engine fallback gets kinds)
- Modify: `src/ModManager.Core/Manifest/GameManifest.cs` (`GameManifestEntry.SaveTypes`, new `ManifestSaveType`)
- Modify: `src/ModManager.Core/Manifest/ManifestValidator.cs` (drop malformed save types)
- Modify: `src/ModManager.Core/Manifest/EffectiveManifest.cs` (`MergeEntry`: `SaveTypes = remote.SaveTypes ?? embedded.SaveTypes`)
- Modify: `src/ModManager.Core/Manifest/games-manifest.json` (Elden Ring entry gets four types)
- Test: `tests/ModManager.Tests/SaveTypesManifestTests.cs`

**Interfaces:**
- Produces: `record SaveType(string Extension, string Label, string? Kind = null)`; `SaveTypeKind` static class with `Vanilla = "vanilla"`, `Coop = "coop"`, `Overhaul = "overhaul"`; `record ManifestSaveType { string Extension; string Label; string? Kind }`; `GameManifestEntry.SaveTypes : IReadOnlyList<ManifestSaveType>?`; `GameSaveTypesCatalog.Resolve(GameEntry)` unchanged signature.

- [ ] **Step 1: Failing tests**

```csharp
public class SaveTypesManifestTests
{
    [Fact]
    public void SaveTypes_round_trip_as_camelCase()
    {
        var e = new GameManifestEntry { Id = "x", Name = "X", SaveTypes = new[] { new ManifestSaveType { Extension = ".co2", Label = "Seamless Co-op", Kind = "coop" } } };
        var json = JsonSerializer.Serialize(e, ManifestJson.Options);
        Assert.Contains("\"saveTypes\"", json);
        Assert.Contains("\"extension\"", json);
        Assert.Contains("\"kind\"", json);
        Assert.DoesNotContain("\"SaveTypes\"", json);
        var back = JsonSerializer.Deserialize<GameManifestEntry>(json, ManifestJson.Options)!;
        Assert.Equal("coop", back.SaveTypes![0].Kind);
    }

    [Fact]
    public void Elden_ring_declares_four_types_with_kinds()
    {
        var game = new GameEntry { Id = "elden-ring", GameName = "ELDEN RING", Engine = "fromsoft", SteamAppId = "1245620", ManifestId = "elden-ring" };
        var types = GameSaveTypesCatalog.Resolve(game).SaveTypes;
        Assert.Equal(new[] { ".sl2", ".co2", ".err", ".mod" }, types.Select(t => t.Extension));
        Assert.Equal(new[] { "vanilla", "coop", "overhaul", "overhaul" }, types.Select(t => t.Kind));
    }

    [Fact]
    public void Another_fromsoft_game_keeps_the_engine_fallback_with_kinds()
    {
        var game = new GameEntry { Id = "dsr-test", GameName = "Some FromSoft", Engine = "fromsoft" };
        var types = GameSaveTypesCatalog.Resolve(game).SaveTypes;
        Assert.Equal(new[] { ".sl2", ".co2", ".err" }, types.Select(t => t.Extension));
        Assert.Equal("vanilla", types[0].Kind);
    }

    [Theory]
    [InlineData("co2")]        // no dot
    [InlineData(".")]          // empty
    [InlineData("../x")]       // path
    [InlineData(".toolongext1")]
    public void Validator_drops_a_malformed_extension(string ext)
    {
        var entry = new GameManifestEntry { Id = "x", Name = "X", SaveTypes = new[] { new ManifestSaveType { Extension = ext, Label = "L" }, new ManifestSaveType { Extension = ".sl2", Label = "Vanilla", Kind = "vanilla" } } };
        Assert.Equal(new[] { ".sl2" }, ManifestValidator.SafeSaveTypes(entry.SaveTypes)!.Select(t => t.Extension));
    }
}
```

- [ ] **Step 2: Run, expect compile failure** (`SaveTypes`, `ManifestSaveType`, `SafeSaveTypes` missing).
- [ ] **Step 3: Implement.**
  - `SaveType(string Extension, string Label, string? Kind = null)`; `public static class SaveTypeKind { public const string Vanilla = "vanilla", Coop = "coop", Overhaul = "overhaul"; }`.
  - `ManifestSaveType` record with `init` props, in `GameManifest.cs` beside `LoaderManifestEntry`.
  - `ManifestValidator.SafeSaveTypes(IReadOnlyList<ManifestSaveType>?)`: keep entries whose extension matches `^\.[A-Za-z0-9]{1,8}$` and whose label is non-blank; null when none remain. Applied in the entry-gate loop the way `ExtraModTrees` is.
  - `MergeEntry`: `SaveTypes = remote.SaveTypes ?? embedded.SaveTypes,` (the `EveryOptionalFieldMerges` test checks this).
  - `GameSaveTypesCatalog.Resolve`: `ManifestIdLookup.ConfirmedEntryFor(game)?.SaveTypes` mapped to `SaveType` when non-empty, else the engine fallback (`.sl2` vanilla, `.co2` coop, `.err` overhaul).
  - `games-manifest.json` Elden Ring entry: `"saveTypes": [{".sl2","Vanilla","vanilla"}, {".co2","Seamless Co-op","coop"}, {".err","Reforged","overhaul"}, {".mod","Alt Saves","overhaul"}]` in object form.
- [ ] **Step 4: Run the Core suite; expect pass.** Existing `GameSaveTypes`/Saves tests must stay green (SaveType positional equality changes are why `Kind` is a defaulted third parameter).
- [ ] **Step 5: Add `GameManifestEntry.saveTypes` to the camelCase rule list. Commit** `feat(saves): save types carry a kind and come from the game manifest`.

### Task 2: Recognize Reforged's launcher and the save it reads

**Files:**
- Create: `src/ModManager.Core/ReforgedLaunch.cs`
- Modify: `src/ModManager.Core/LaunchTargetPicker.cs` (`IsModLauncher` recognizes Reforged, `.exe` and `.bat`)
- Test: `tests/ModManager.Tests/ReforgedLaunchTests.cs`

**Interfaces:**
- Produces: `record ReforgedLaunchInfo(string Root, bool AltProfile, string? SaveExtension)`; `static ReforgedLaunchInfo? ReforgedLaunch.Inspect(LaunchTarget target)`; `const string ReforgedLaunch.LauncherExe = "ReforgedLauncher.exe"`.

Rules: an exe target named `ReforgedLauncher.exe` has root two folders up (`internals\launcher\`). A `.bat`/`.cmd` target counts when its text mentions `ReforgedLauncher.exe`; its root is the nearest ancestor (the folder itself, then up to three levels) holding `internals\launcher\ReforgedLauncher.exe`. `AltProfile` is true when the args or the bat text contain `--alt-save-profile`. `SaveExtension` is read from `<root>\mod\altsaves.toml` (`extension = "..."` and `seamless_extension = "..."`). If both are present and they differ, it's null (ambiguous). If one is present, use it. If the file is missing or unreadable, use `.err`. Any exception means null info, never a throw.

- [ ] **Step 1: Failing tests** (temp dir fixture laid out as `D:\ERRv2.1.2.3` is):

```csharp
public class ReforgedLaunchTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-reforged-");
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
    private string Exe => Path.Combine(_root, "internals", "launcher", "ReforgedLauncher.exe");
    private void Lay(string? toml)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Exe)!);
        File.WriteAllText(Exe, "x");
        File.WriteAllText(Path.Combine(_root, "2 - Launch ELDEN RING Reforged - Offline or Seamless (Windows).BAT"),
            "@ECHO OFF\r\n.\\internals\\launcher\\ReforgedLauncher.exe --offline\r\n");
        Directory.CreateDirectory(Path.Combine(_root, "extras", "Alt Save Profiles - Windows"));
        File.WriteAllText(Path.Combine(_root, "extras", "Alt Save Profiles - Windows", "Alt Save Profile 1 (Offline).BAT"),
            "..\\..\\internals\\launcher\\ReforgedLauncher.exe --alt-save-profile-1 --offline\r\n");
        if (toml is not null) { Directory.CreateDirectory(Path.Combine(_root, "mod")); File.WriteAllText(Path.Combine(_root, "mod", "altsaves.toml"), toml); }
    }
    private const string Both = "# Extension\nextension = \".err\"\n\nseamless_extension = \".err\"\n";

    [Fact] public void Exe_target_reads_the_toml_extension()
    { Lay(Both); var i = ReforgedLaunch.Inspect(new LaunchTarget("R", "exe", Exe))!; Assert.Equal(".err", i.SaveExtension); Assert.False(i.AltProfile); Assert.Equal(_root, i.Root); }

    [Fact] public void Bat_target_finds_its_launcher()
    { Lay(Both); var bat = Directory.GetFiles(_root, "*.BAT").Single(); Assert.Equal(".err", ReforgedLaunch.Inspect(new LaunchTarget("R", "exe", bat))!.SaveExtension); }

    [Fact] public void Alt_profile_bat_is_flagged()
    { Lay(Both); var bat = Directory.GetFiles(Path.Combine(_root, "extras", "Alt Save Profiles - Windows")).Single(); Assert.True(ReforgedLaunch.Inspect(new LaunchTarget("R", "exe", bat))!.AltProfile); }

    [Fact] public void Differing_extensions_are_ambiguous()
    { Lay("extension = \".err\"\nseamless_extension = \".co2\"\n"); Assert.Null(ReforgedLaunch.Inspect(new LaunchTarget("R", "exe", Exe))!.SaveExtension); }

    [Fact] public void Missing_toml_falls_back_to_err()
    { Lay(null); Assert.Equal(".err", ReforgedLaunch.Inspect(new LaunchTarget("R", "exe", Exe))!.SaveExtension); }

    [Fact] public void Custom_extension_is_honored()
    { Lay("extension = \".rfg\"\nseamless_extension = \".rfg\"\n"); Assert.Equal(".rfg", ReforgedLaunch.Inspect(new LaunchTarget("R", "exe", Exe))!.SaveExtension); }

    [Fact] public void Other_targets_are_not_reforged()
    {
        Assert.Null(ReforgedLaunch.Inspect(new LaunchTarget("S", "steam", "steam://rungameid/1245620")));
        Assert.Null(ReforgedLaunch.Inspect(new LaunchTarget("E", "exe", @"C:\g\ersc_launcher.exe")));
    }

    [Fact] public void Reforged_launchers_count_as_mod_launchers_so_vanilla_never_picks_them()
    { Lay(Both); Assert.True(LaunchTargetPicker.IsModLauncher(Exe)); Assert.True(LaunchTargetPicker.IsModLauncher(Directory.GetFiles(_root, "*.BAT").Single())); }
}
```

- [ ] **Step 2: Run; expect compile failure.**
- [ ] **Step 3: Implement** `ReforgedLaunch` as above. A toml read uses a regex `^\s*(seamless_extension|extension)\s*=\s*"([^"]*)"` per line, with a dotless value normalized to `.x`. `IsModLauncher`: before the `.exe` check, `if (ReforgedLaunch.IsReforgedLauncherPath(path)) return true;`, where that helper is true for a `ReforgedLauncher.exe` name, or for a `.bat`/`.cmd` that exists and whose text mentions `ReforgedLauncher.exe` (wrapped in a try, false on error).
- [ ] **Step 4: Run Core suite; pass** (`LaunchTargetPickerTests` must stay green).
- [ ] **Step 5: Commit** `feat(launch): recognize Reforged's launcher and the save it reads`.

### Task 3: The offer decision

**Files:**
- Create: `src/ModManager.Core/SaveOffer.cs`
- Test: `tests/ModManager.Tests/SaveOfferTests.cs`

**Interfaces:**
- Consumes: `SaveType` with `Kind` (Task 1), `ReforgedLaunchInfo` (Task 2), `LaunchMode`, `LaunchTargetPicker.IsModLauncher`.
- Produces:
  - `record SaveFileFacts(string FileName, bool Exists, int? Characters, DateTime LastWriteUtc, string? FirstCharacter = null, int? FirstLevel = null)` (`Characters` null = unreadable)
  - `enum SaveOfferWarning { None, Warn, Acknowledge }`
  - `record SaveOfferSource(SaveType Type, SaveFileFacts File, SaveOfferWarning Warning)`
  - `record SaveOfferPlan(SaveType Target, string TargetFileName, bool TargetExists, IReadOnlyList<SaveOfferSource> Sources)` (newest first; `[0]` default)
  - `static string? SaveOffer.TargetExtension(LaunchMode mode, LaunchTarget? target, ReforgedLaunchInfo? reforged, IReadOnlyList<SaveType> types)`
  - `static SaveOfferPlan? SaveOffer.Evaluate(IReadOnlyList<SaveType> types, SaveLayout layout, string? targetExt, Func<string, SaveFileFacts> facts, string baseName, IReadOnlyCollection<string> dismissed, bool writesRefused)`
  - `static SaveOfferWarning SaveOffer.WarningFor(SaveType source, SaveType target)`

Rules:
- `TargetExtension`. Vanilla mode: the type whose kind is `vanilla`. A target that is a Seamless launcher (`IsModLauncher` true, and the name has `ersc_launcher` or contains `seamless`): the `coop` type. Reforged info that's not an alt profile, with a declared extension: that extension. Anything else: null.
- `Evaluate`. Return null in any of these cases:
  - the layout isn't `TypedFiles`;
  - `targetExt` is null or not declared;
  - it's dismissed;
  - writes are refused;
  - the target facts are unreadable (`Exists && Characters is null`);
  - the target has one or more characters;
  - no source has `Characters > 0`.

  `facts` is called with `baseName + ext`, target first. Only declared types are sources, never the target itself.
- `WarningFor`. If the target isn't vanilla, `None`. If the source is vanilla, `None`. A coop source is `Warn`. Overhaul, or a missing or unknown kind, is `Acknowledge`.

- [ ] **Step 1: Failing tests** (all pure; `facts` from a dictionary):

```csharp
public class SaveOfferTests
{
    private static readonly SaveType Sl2 = new(".sl2", "Vanilla", "vanilla"), Co2 = new(".co2", "Seamless Co-op", "coop"),
        Err = new(".err", "Reforged", "overhaul"), Mod = new(".mod", "Alt Saves", "overhaul");
    private static readonly IReadOnlyList<SaveType> Types = new[] { Sl2, Co2, Err, Mod };
    private static readonly DateTime T0 = new(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc), T1 = new(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);

    private static Func<string, SaveFileFacts> F(params SaveFileFacts[] files)
        => name => files.FirstOrDefault(f => f.FileName == name) ?? new SaveFileFacts(name, false, 0, default);
    private static SaveOfferPlan? Eval(string? target, Func<string, SaveFileFacts> f, string[]? dismissed = null, bool refused = false, SaveLayout layout = SaveLayout.TypedFiles)
        => SaveOffer.Evaluate(Types, layout, target, f, "ER0000", dismissed ?? Array.Empty<string>(), refused);

    private static readonly SaveFileFacts EmptySl2 = new("ER0000.sl2", true, 0, T1);
    private static readonly SaveFileFacts Co2With3 = new("ER0000.co2", true, 3, T1, "Tarnished", 112);
    private static readonly SaveFileFacts ErrWith1 = new("ER0000.err", true, 1, T0, "Rogier", 80);

    [Fact] public void Offers_co2_into_an_empty_sl2()
    { var p = Eval(".sl2", F(EmptySl2, Co2With3))!; Assert.Equal(".sl2", p.Target.Extension); Assert.True(p.TargetExists); Assert.Equal(".co2", p.Sources.Single().Type.Extension); Assert.Equal(SaveOfferWarning.Warn, p.Sources[0].Warning); }

    [Fact] public void Offers_when_the_target_does_not_exist()
        => Assert.False(Eval(".sl2", F(Co2With3))!.TargetExists);

    [Fact] public void No_offer_when_the_target_has_a_character()
        => Assert.Null(Eval(".sl2", F(EmptySl2 with { Characters = 1 }, Co2With3)));

    [Fact] public void No_offer_when_no_source_has_characters()
        => Assert.Null(Eval(".sl2", F(EmptySl2, Co2With3 with { Characters = 0 })));

    [Fact] public void An_unreadable_target_means_no_offer()
        => Assert.Null(Eval(".sl2", F(EmptySl2 with { Characters = null }, Co2With3)));

    [Fact] public void An_unreadable_source_is_never_offered()
        => Assert.Equal(new[] { ".err" }, Eval(".sl2", F(EmptySl2, Co2With3 with { Characters = null }, ErrWith1))!.Sources.Select(s => s.Type.Extension));

    [Fact] public void Newest_source_is_first_and_overhaul_needs_acknowledgment()
    { var p = Eval(".sl2", F(EmptySl2, Co2With3, ErrWith1))!; Assert.Equal(new[] { ".co2", ".err" }, p.Sources.Select(s => s.Type.Extension)); Assert.Equal(SaveOfferWarning.Acknowledge, p.Sources[1].Warning); }

    [Fact] public void Into_a_modded_type_carries_no_warning()
    { var p = Eval(".err", F(new SaveFileFacts("ER0000.sl2", true, 2, T1), new SaveFileFacts("ER0000.err", true, 0, T0)))!; Assert.All(p.Sources, s => Assert.Equal(SaveOfferWarning.None, s.Warning)); }

    [Fact] public void Dismissed_refused_unknown_target_and_worlds_layout_mean_no_offer()
    {
        Assert.Null(Eval(".sl2", F(EmptySl2, Co2With3), dismissed: new[] { ".sl2" }));
        Assert.Null(Eval(".sl2", F(EmptySl2, Co2With3), refused: true));
        Assert.Null(Eval(null, F(EmptySl2, Co2With3)));
        Assert.Null(Eval(".xyz", F(EmptySl2, Co2With3)));
        Assert.Null(Eval(".sl2", F(EmptySl2, Co2With3), layout: SaveLayout.Worlds));
    }

    [Fact] public void Dismissal_is_per_target_type()
        => Assert.NotNull(Eval(".co2", F(new SaveFileFacts("ER0000.sl2", true, 2, T1)), dismissed: new[] { ".sl2" }));

    [Fact] public void Only_exact_base_plus_declared_extension_is_read()
    {
        var asked = new List<string>();
        Eval(".sl2", n => { asked.Add(n); return new SaveFileFacts(n, false, 0, default); });
        Assert.Equal(new[] { "ER0000.sl2", "ER0000.co2", "ER0000.err", "ER0000.mod" }, asked);
    }

    [Fact] public void Target_with_characters_stops_before_reading_sources()
    {
        var asked = new List<string>();
        Eval(".sl2", n => { asked.Add(n); return new SaveFileFacts(n, true, n.EndsWith(".sl2") ? 1 : 3, T1); });
        Assert.Equal(new[] { "ER0000.sl2" }, asked);
    }

    [Fact]
    public void Target_extension_by_launch()
    {
        Assert.Equal(".sl2", SaveOffer.TargetExtension(LaunchMode.Vanilla, new LaunchTarget("S", "steam", "steam://rungameid/1245620"), null, Types));
        Assert.Equal(".co2", SaveOffer.TargetExtension(LaunchMode.Modded, new LaunchTarget("C", "exe", @"C:\g\Game\ersc_launcher.exe"), null, Types));
        Assert.Equal(".err", SaveOffer.TargetExtension(LaunchMode.Modded, new LaunchTarget("R", "exe", @"D:\ERR\internals\launcher\ReforgedLauncher.exe"), new ReforgedLaunchInfo(@"D:\ERR", false, ".err"), Types));
        Assert.Null(SaveOffer.TargetExtension(LaunchMode.Modded, new LaunchTarget("R", "exe", "x.bat"), new ReforgedLaunchInfo(@"D:\ERR", true, ".err"), Types));
        Assert.Null(SaveOffer.TargetExtension(LaunchMode.Modded, new LaunchTarget("M", "exe", @"C:\g\modengine2_launcher.exe"), null, Types));
    }
}
```

- [ ] **Step 2: Run; compile failure.**
- [ ] **Step 3: Implement** `SaveOffer` per the rules. Sources are ordered by `LastWriteUtc` descending, then declared order.
- [ ] **Step 4: Run; pass.**
- [ ] **Step 5: Commit** `feat(saves): decide when a launch should offer a save from another mode`.

### Task 4: Read the facts off disk

**Files:**
- Create: `src/ModManager.Core/SaveOfferProbe.cs`
- Test: `tests/ModManager.Tests/SaveOfferProbeTests.cs`

**Interfaces:**
- Consumes: `EldenRingSave.ReadCharacters(path)`, `SaveOffer.Evaluate`, `GameSaveTypesCatalog`, `SaveLayoutCatalog`, `SaveWritePolicy.Refusal`, `GameEntry.SaveOfferDismissed` (Task 5 adds the property; this task adds it if Task 5 hasn't landed yet).
- Produces:
  - `static SaveFileFacts SaveOfferProbe.Read(string saveDir, string fileName, Func<string, IReadOnlyList<CharacterSlot>> reader)` (a missing file gives `Exists=false, Characters=0`; a reader throw gives `Characters=null`)
  - `static SaveOfferPlan? SaveOfferProbe.For(GameEntry game, string? saveDir, LaunchMode mode, LaunchTarget? target, Func<string, IReadOnlyList<CharacterSlot>>? reader = null)`, with the reader defaulting to `EldenRingSave.ReadCharacters`. Base name `ER0000` for `fromsoft`; null for other engines in v1.

- [ ] **Step 1: Failing tests**, using a fake reader keyed by file name over real temp files:

```csharp
[Fact] public void Missing_file_reads_as_absent_with_no_characters() { var f = SaveOfferProbe.Read(_dir, "ER0000.sl2", _ => throw new Exception()); Assert.False(f.Exists); Assert.Equal(0, f.Characters); }
[Fact] public void Reader_failure_reads_as_unknown() { File.WriteAllText(Path.Combine(_dir, "ER0000.sl2"), "x"); Assert.Null(SaveOfferProbe.Read(_dir, "ER0000.sl2", _ => throw new InvalidDataException()).Characters); }
[Fact] public void First_character_name_and_level_are_carried() { /* reader returns two slots; FirstCharacter/FirstLevel = slot 0's */ }
[Fact] public void No_save_folder_means_no_offer() { Assert.Null(SaveOfferProbe.For(EldenRing(), null, LaunchMode.Vanilla, Steam, FakeReader)); Assert.Null(SaveOfferProbe.For(EldenRing(), Path.Combine(_dir, "nope"), LaunchMode.Vanilla, Steam, FakeReader)); }
[Fact] public void End_to_end_empty_sl2_with_co2_characters_offers() { /* write both files; fake reader: .sl2 → [], .co2 → [slot]; assert plan Sources[0] is .co2 */ }
[Fact] public void Non_fromsoft_engine_never_offers() { /* Engine = "ue-pak" → null */ }
```

(Write the two commented bodies out in full when implementing. The `CharacterSlot` constructor takes `(slotIndex, name, class, level, runes, 8 stat bytes, steamId)`.)

- [ ] **Step 2–4:** run (fail), implement, run (pass).
- [ ] **Step 5: Commit** `feat(saves): read save facts for the offer, target first`.

### Task 5: Remember "don't ask for this game"

**Files:**
- Modify: `src/ModManager.Core/GameEntry.cs` (`public IReadOnlyList<string>? SaveOfferDismissed { get; set; }`, omitted when null)
- Modify: `src/ModManager.App/Services/LauncherService.cs` (`DismissSaveOffer(string gameId, string targetExt)` and `ClearSaveOfferDismissals(string gameId)` via `UpdateRegistry`)
- Test: `tests/ModManager.Tests/SaveOfferDismissalTests.cs`
- Modify: `.claude/rules/camelcase-json-on-disk.md`

- [ ] **Step 1: Failing test:** `GameEntry` with `SaveOfferDismissed = [".sl2"]` round-trips through the registry serializer options with `"saveOfferDismissed"` in the JSON and no `"SaveOfferDismissed"`. A null value is omitted (assert the key is absent). Follow `ManifestIdentityTests.ManifestId_round_trips_as_camelCase_and_is_omitted_when_null` for the options used.
- [ ] **Step 2–4:** run, implement, run.
- [ ] **Step 5: Commit** `feat(saves): remember a per-game, per-type dismissal of the save offer`.

### Task 6: The dialog and the Play hook

**Files:**
- Create: `src/ModManager.App/SaveOfferDialog.xaml`, `src/ModManager.App/SaveOfferDialog.xaml.cs`
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (`AskSaveOffer` delegate; `OfferSaveCopyAsync(target)` called at the top of `LaunchTargetExplicit`)
- Modify: `src/ModManager.App/MainWindow.xaml.cs` (wire the delegate next to `AskPatchDayAtPlay`)
- Modify: `.claude/rules/automation-ids.md` (list the new ids)

**Interfaces:**
- `enum SaveOfferChoice { CopyAndPlay, PlayWithout, DontAsk }`; `record SaveOfferAnswer(SaveOfferChoice Choice, SaveOfferSource? Source)`; dialog `Answer` is null when closed.
- `public Func<SaveOfferPlan, string, Task<SaveOfferAnswer?>>? AskSaveOffer { get; set; }` (the plan, plus the game name).

Behavior in `LaunchTargetExplicit(target)`, before the Steam check:

```csharp
if (!await OfferSaveCopyAsync(target)) return;   // closed, or a copy that failed: nothing launches
```

```csharp
private async Task<bool> OfferSaveCopyAsync(LaunchTarget target)
{
    if (_ctx is null || AskSaveOffer is not { } ask) return true;
    var game = _ctx.Game; var dir = _ctx.SaveDir; var mode = CurrentLaunchMode;
    SaveOfferPlan? plan;
    try { plan = await Task.Run(() => SaveOfferProbe.For(game, dir, mode, target)); }
    catch { return true; }   // a probe failure never blocks a launch
    if (plan is null) return true;
    var answer = await ask(plan, game.GameName);
    switch (answer?.Choice)
    {
        case null: StatusText = "Nothing launched."; return false;
        case SaveOfferChoice.PlayWithout: return true;
        case SaveOfferChoice.DontAsk:
            try { _svc.DismissSaveOffer(game.Id, plan.Target.Extension); } catch { }
            return true;
    }
    var src = answer.Source!;
    try
    {
        if (plan.TargetExists) SaveManager.Backup(dir!, _ctx.SavesDir, "before-clone", auto: true);
        SaveManager.CloneToType(dir!, src.File.FileName, plan.Target.Extension, overwrite: plan.TargetExists);
        StatusText = $"Copied your {src.Type.Label} save to {plan.Target.Label}. Your {src.Type.Label} save is untouched.";
        return true;
    }
    catch (Exception e) { StatusText = "Couldn't copy the save, so nothing launched. " + ErrorRemedy.Describe(e); return false; }
}
```

The dialog follows the `PatchDayDialog` shape: a title stack ("SAVES // PLAY" eyebrow, title "No {target label} characters yet"), content buttons with no built-in buttons, and Escape closing it.
- **Sentence** (`SaveOfferDialog` id on the TextBlock): "Your characters are in your {source label} save. {Game} in {target label} mode reads a different file, so you'd start with an empty title screen."
- **Source choice:**
  - With one source, a TextBlock: "{FirstCharacter}, level {FirstLevel}, and {n-1} more come with it. Your {source label} save stays as it is." Omit the "and N more" clause when n is 1, and the name and level clause when they're null.
  - With several sources, RadioButtons (`SaveOfferSource.{ext without dot}`, Name "{label}: {first}, last played {date local, MMM d}"), the newest checked.
- **Warning** (`SaveOfferWarning` TextBlock), visible when the chosen source's warning is not None: "This character goes online with anti-cheat on. If it picked up anything a mod added, that could flag your account."
- **Acknowledge** CheckBox (`SaveOfferAcknowledge`), visible when the warning is Acknowledge: "I understand this character may carry {source label} items into online play." Copy and play is enabled only while it's ticked; the binding re-evaluates on source change.
- **Buttons:** `SaveOfferCopyAndPlay` "Copy and play", `SaveOfferPlayWithout` "Play without copying", `SaveOfferDontAsk` "Don't ask for this game".

- [ ] **Step 1:** Write the dialog and VM change. Stop the app, delete `src/ModManager.App/obj` (stale-codegen rule), build the App x64: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. Expect 0 warnings and 0 errors.
- [ ] **Step 2:** Run the Core suite; pass (CorePurityTests included).
- [ ] **Step 3:** Add ids to `automation-ids.md` under "Added since". Commit `feat(saves): offer a save from another mode at Play`.

### Task 7: The miner carries saveTypes from overrides

**Files:**
- Modify: `tools/ManifestMiner/OverrideEntry.cs` (`IReadOnlyList<ManifestSaveType>? SaveTypes`)
- Modify: `tools/ManifestMiner/OverridesMerge.cs` (both merge sites, like `ExtraModTrees`)
- Modify: `tools/ManifestMiner/OverridesValidate.cs` (reuse `ManifestValidator.SafeSaveTypes`; a dropped entry is a validation error)
- Test: the miner's existing test project, if `ExtraModTrees` has tests there (`git grep -n ExtraModTrees tests`); mirror one.

- [ ] Steps: failing test that an override `saveTypes` lands in the mined entry → implement → pass → commit `feat(miner): carry saveTypes from overrides`.
- [ ] Follow-up, after merge: a data PR in `626-game-manifest` adding `saveTypes` to `overrides/elden-ring.json`, so the live feed agrees with the embedded snapshot.

### Task 8: Smoke entry and live check

- [ ] Append to `docs/smoke-tests/pending.md`. Steps:
  1. On Neb, with today's empty `ER0000.sl2`, press Play vanilla.
  2. The dialog names Seamless (newest) and Reforged.
  3. Select Reforged: the checkbox appears, and Copy is disabled until it's ticked.
  4. Select Seamless: Warn only.
  5. Copy and play. A character is on the title screen.
  6. Close the game. The Saves dialog shows `auto-before-clone`.
  7. Restore it. `ER0000.sl2` is byte-identical to the pre-copy file, compared by sha256.
  8. Watch for a Steam Cloud conflict prompt.

  The launch steps need Este present: this is an EAC title.
- [ ] Commit `docs(smoke): save-type offer`.

---

## Self-review

- **Spec coverage:** condition list (T3/T4), prompt + buttons (T6), launch table incl. Reforged + alt profile (T2/T3), manifest types + `.mod` (T1/T7), warnings matrix (T3 `WarningFor`, T6 UI), reversibility (T6 snapshot + clone), Steam Cloud risk (T8), whole-file risk (T3 "target has a character").
- **Types:** `SaveType(Extension, Label, Kind)`, `SaveFileFacts`, `SaveOfferPlan`, `SaveOfferSource`, `SaveOfferWarning`, `ReforgedLaunchInfo(Root, AltProfile, SaveExtension)` are used consistently across T2–T6.
- **Placeholders:** the T4 bodies marked "write out in full" are the only abbreviated tests; their assertions are specified inline.
