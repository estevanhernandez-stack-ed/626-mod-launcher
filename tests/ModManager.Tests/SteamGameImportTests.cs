using ModManager.Core;

namespace ModManager.Tests;

// "Add from Steam" auto-add: turn an installed Steam game into a ready-to-register GameInput.
// Engine priority is app-id map first (catches proprietary engines with no folder signature), then
// the folder scan; undetectable engine => not addable (route to manual, don't guess).
public class SteamGameImportTests
{
    private const string EldenRingAppId = "1245620";   // mapped to "fromsoft" in KnownEngines

    [Fact]
    public void Plan_uses_appid_engine_over_folder_scan()
    {
        // Elden Ring's app id maps to fromsoft; even if a folder scan said something else, the app-id
        // mapping wins (FromSoft games have no clean folder signature).
        var plan = SteamGameImport.Plan(
            new SteamImportCandidate(EldenRingAppId, "ELDEN RING", @"C:\games\ELDEN RING"),
            folderDetectedEngine: "ue4ss");

        Assert.True(plan.Addable);
        Assert.Equal("fromsoft", plan.Engine);
        Assert.NotNull(plan.Input);
        Assert.Equal("fromsoft", plan.Input!.Engine);
    }

    [Fact]
    public void Plan_falls_back_to_folder_engine_when_appid_unknown()
    {
        var plan = SteamGameImport.Plan(
            new SteamImportCandidate("999999999", "Some Unreal Game", @"C:\games\Unreal"),
            folderDetectedEngine: "ue4ss");

        Assert.True(plan.Addable);
        Assert.Equal("ue4ss", plan.Engine);
    }

    [Fact]
    public void Plan_needs_manual_when_engine_undetectable()
    {
        var plan = SteamGameImport.Plan(
            new SteamImportCandidate("999999999", "Mystery Game", @"C:\games\Mystery"),
            folderDetectedEngine: null);

        Assert.False(plan.Addable);
        Assert.Null(plan.Engine);
        Assert.Null(plan.Input);
    }

    [Fact]
    public void Plan_builds_input_with_steam_fields_and_preset_mod_path()
    {
        var plan = SteamGameImport.Plan(
            new SteamImportCandidate(EldenRingAppId, "ELDEN RING", @"C:\games\ELDEN RING"),
            folderDetectedEngine: null);

        var input = plan.Input!;
        Assert.Equal("ELDEN RING", input.Name);
        Assert.Equal(@"C:\games\ELDEN RING", input.GameRoot);
        Assert.Equal(EldenRingAppId, input.SteamAppId);
        // mod path comes from the engine preset (not hardcoded here, so the test tracks the preset).
        var expectedModPath = EnginePresets.Presets.TryGetValue("fromsoft", out var p) ? p.ModPath : null;
        Assert.Equal(expectedModPath, input.ModPath);
    }

    [Fact]
    public void The_plan_carries_the_manifest_id_so_curation_reaches_the_game()
    {
        // Without this the registered id is Slugify(the Steam display name), which for a game whose
        // manifest id differs - "Minecraft: Java Edition" vs "minecraft" - matches nothing and throws
        // away the engine, mod path, save layout and ban risk that were curated for it.
        var plan = SteamGameImport.Plan(
            new SteamImportCandidate(EldenRingAppId, "ELDEN RING", @"C:\games\ELDEN RING"),
            folderDetectedEngine: "ue4ss");

        Assert.True(plan.Addable);
        Assert.Equal("elden-ring", plan.Input!.Id);
    }

    [Fact]
    public void A_game_the_manifest_does_not_know_carries_no_id_and_falls_back_to_its_name()
    {
        // Not an error. BuildGameEntry does Slugify(Id ?? Name), so a null id is exactly today's
        // behaviour - the change only ever ADDS certainty, never removes the fallback.
        var plan = SteamGameImport.Plan(
            new SteamImportCandidate("999999", "Some Unmined Game", @"C:\games\Unmined"),
            folderDetectedEngine: "bepinex");

        Assert.Null(plan.Input!.Id);
    }
}
