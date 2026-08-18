using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// A15: the mod folder is the field that decides whether the scan looks anywhere at all. Quick-add
/// took it from the engine preset and ignored the manifest's per-game override, and nothing repaired
/// it afterwards — so a game registered against a folder that does not exist reported "no mods found"
/// forever. These lock both halves: the add path honours the curated path, and an already-registered
/// game picks up a correction.
/// </summary>
public class RegistrationModPathTests
{
    // Embedded-manifest fixtures whose curated modPath differs from their engine preset.
    private const string CyberpunkAppId = "1091500";   // custom  -> archive/pc/mod   (preset: mods)
    private const string PalworldAppId  = "1623730";   // ue-pak  -> Pal/Content/Paks/~mods
    private const string EldenRingAppId = "1245620";   // fromsoft, no curated modPath in the snapshot

    // ---- the facade ------------------------------------------------------------------------

    [Fact]
    public void KnownModPaths_reads_the_curated_path_for_a_game_that_states_one()
        => Assert.Equal("archive/pc/mod", KnownModPaths.ByAppId(CyberpunkAppId));

    [Fact]
    public void KnownModPaths_is_null_for_an_unknown_app_id()
        => Assert.Null(KnownModPaths.ByAppId("0000000"));

    [Fact]
    public void KnownModPaths_is_null_for_a_blank_app_id()
        => Assert.Null(KnownModPaths.ByAppId(""));

    // ---- the add path ----------------------------------------------------------------------

    [Fact]
    public void Plan_prefers_the_curated_mod_path_over_the_engine_preset()
    {
        // The bug: "custom" presets to "mods", so Cyberpunk registered against a folder it does not
        // use. The engine comes from the folder scan here because Cyberpunk carries no known-engines
        // provenance — which makes the point sharper: the curated path must win no matter HOW the
        // engine was resolved, since the preset it selects is exactly what was wrong.
        var plan = SteamGameImport.Plan(
            new SteamImportCandidate(CyberpunkAppId, "Cyberpunk 2077", @"C:\games\Cyberpunk 2077"),
            folderDetectedEngine: "custom");

        Assert.True(plan.Addable);
        Assert.Equal("custom", plan.Engine);
        Assert.Equal("archive/pc/mod", plan.Input!.ModPath);
        Assert.NotEqual(EnginePresets.Presets["custom"].ModPath, plan.Input!.ModPath);
    }

    [Fact]
    public void Plan_prefers_the_curated_mod_path_for_an_unreal_game_with_a_project_prefix()
    {
        // Every UE game prefixes its project name; no preset can derive that.
        var plan = SteamGameImport.Plan(
            new SteamImportCandidate(PalworldAppId, "Palworld", @"C:\games\Palworld"),
            folderDetectedEngine: null);

        Assert.True(plan.Addable);
        Assert.Equal("Pal/Content/Paks/~mods", plan.Input!.ModPath);
    }

    [Fact]
    public void Plan_falls_back_to_the_preset_when_the_manifest_curates_no_path()
    {
        var plan = SteamGameImport.Plan(
            new SteamImportCandidate(EldenRingAppId, "ELDEN RING", @"C:\games\ELDEN RING"),
            folderDetectedEngine: null);

        Assert.True(plan.Addable);
        Assert.Equal(EnginePresets.Presets["fromsoft"].ModPath, plan.Input!.ModPath);
    }

    // ---- the refresh rule ------------------------------------------------------------------

    [Fact]
    public void ModPath_takes_the_manifest_when_the_stored_value_is_still_the_preset_default()
        => Assert.Equal("reframework/autorun",
            RegistrationRefresh.ModPath(stored: "mods", presetDefault: "mods", manifest: "reframework/autorun"));

    [Fact]
    public void ModPath_keeps_a_customised_value()
        => Assert.Equal("my/own/folder",
            RegistrationRefresh.ModPath(stored: "my/own/folder", presetDefault: "mods", manifest: "reframework/autorun"));

    [Fact]
    public void ModPath_keeps_the_stored_value_when_the_user_pinned_it()
        => Assert.Equal("mods",
            RegistrationRefresh.ModPath(stored: "mods", presetDefault: "mods", manifest: "reframework/autorun", userSet: true));

    [Fact]
    public void ModPath_keeps_the_stored_value_when_the_manifest_says_nothing()
    {
        Assert.Equal("mods", RegistrationRefresh.ModPath("mods", "mods", null));
        Assert.Equal("mods", RegistrationRefresh.ModPath("mods", "mods", "   "));
    }

    [Theory]
    // Separator, trailing slash and case are not choices anyone made — a registration spelled any of
    // these ways is still "untouched" and must stay eligible for the correction.
    [InlineData(@"Content\Paks\~mods", "Content/Paks/~mods")]
    [InlineData("Content/Paks/~mods/", "Content/Paks/~mods")]
    [InlineData("content/paks/~MODS", "Content/Paks/~mods")]
    public void ModPath_treats_cosmetic_path_spellings_as_untouched(string stored, string presetDefault)
        => Assert.Equal("Pal/Content/Paks/~mods",
            RegistrationRefresh.ModPath(stored, presetDefault, "Pal/Content/Paks/~mods"));

    // ---- the self-heal, end to end through Scanner.GameContext ------------------------------

    [Fact]
    public void GameContext_repairs_a_registration_frozen_on_the_preset_path()
    {
        // Exactly the shape quick-add used to write: engine preset's path, stored verbatim. Cyberpunk
        // curates archive/pc/mod, so the resolved location must move without the entry being rewritten.
        var game = new GameEntry
        {
            Id = "cyberpunk-2077",
            Engine = "custom",
            GameRoot = Path.Combine(Path.GetTempPath(), "a15-cp2077"),
            ModLocations = new[] { new ModLocation("mods", "Mods", EnginePresets.Presets["custom"].ModPath) },
        };

        var ctx = Scanner.GameContext(game);

        Assert.EndsWith(Path.Combine("archive", "pc", "mod"), ctx.Locations[0].Abs);
        // Read-only, per the A1 rule: the stored entry keeps what the user can see and edit.
        Assert.Equal("mods", game.ModLocations[0].Path);
    }

    [Fact]
    public void GameContext_leaves_a_customised_mod_path_alone()
    {
        var game = new GameEntry
        {
            Id = "cyberpunk-2077",
            Engine = "custom",
            GameRoot = Path.Combine(Path.GetTempPath(), "a15-cp2077-custom"),
            ModLocations = new[] { new ModLocation("mods", "Mods", "my/own/folder") },
        };

        // Verbatim, separators and all. Normalisation applies to a path ADOPTED from the manifest;
        // a value the user typed is theirs and gets echoed back as they wrote it.
        Assert.EndsWith("my/own/folder", Scanner.GameContext(game).Locations[0].Abs);
    }

    [Fact]
    public void GameContext_leaves_the_mod_path_alone_when_the_user_pinned_locations()
    {
        var game = new GameEntry
        {
            Id = "cyberpunk-2077",
            Engine = "custom",
            GameRoot = Path.Combine(Path.GetTempPath(), "a15-cp2077-pinned"),
            ModLocations = new[] { new ModLocation("mods", "Mods", EnginePresets.Presets["custom"].ModPath) },
            UserSet = new[] { GameEntry.UserSetModLocations },
        };

        Assert.EndsWith("mods", Scanner.GameContext(game).Locations[0].Abs);
    }

    [Fact]
    public void GameContext_keeps_another_tool_s_ownership_flag_when_it_repairs_the_path()
    {
        // The repair copies the record. Rebuilding it by hand would drop Managed, and 626 would start
        // writing into a folder Vortex owns — the ownership law broken for the sake of a path edit.
        var game = new GameEntry
        {
            Id = "cyberpunk-2077",
            Engine = "custom",
            GameRoot = Path.Combine(Path.GetTempPath(), "a15-cp2077-owned"),
            ModLocations = new[]
            {
                new ModLocation("mods", "Mods", EnginePresets.Presets["custom"].ModPath) { Managed = "vortex" },
            },
        };

        var loc = Scanner.GameContext(game).Locations[0];
        Assert.EndsWith(Path.Combine("archive", "pc", "mod"), loc.Abs);
        Assert.Equal("vortex", loc.Managed);
    }

    [Fact]
    public void ModPath_does_not_fire_when_the_stored_value_is_genuinely_a_different_folder()
        => Assert.Equal("Content/Paks",
            RegistrationRefresh.ModPath("Content/Paks", "Content/Paks/~mods", "Pal/Content/Paks/~mods"));
}
