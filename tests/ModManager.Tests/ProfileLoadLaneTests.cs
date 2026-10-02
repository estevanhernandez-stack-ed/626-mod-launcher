using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// A profile load goes through the one toggle router (E1 slice two). It used to move every mod through
/// the scanner's folder lane, so on a direct-inject game (FromSoft, no Mod Engine 2) loading a profile
/// found nothing to move and changed nothing — the failure #347 fixed for the agent's toggle, still
/// alive in the profile path the app and the agent both use.
/// </summary>
public class ProfileLoadLaneTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-profile-lane-");
    private string GameRoot => Path.Combine(_root, "ELDEN RING");
    private string Play => Path.Combine(GameRoot, "Game");
    private readonly GameEntry _game;

    public ProfileLoadLaneTests()
    {
        Directory.CreateDirectory(Path.Combine(Play, "reshade-shaders"));
        File.WriteAllText(Path.Combine(Play, "reshade-shaders", "shader.fx"), "fx");
        File.WriteAllText(Path.Combine(Play, "ReShadePreset.ini"), "preset");
        File.WriteAllText(Path.Combine(Play, "eldenring.exe"), "game");
        _game = new GameEntry
        {
            Id = "er-profile", GameName = "ER", Engine = "fromsoft", GameRoot = GameRoot,
            DataDir = Path.Combine(_root, "data"),
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private Mod ReShade() => ModListing.Resolve(_game).Single(m => m.Name == "ReShade");

    [Fact]
    public async Task Loading_a_profile_on_a_direct_inject_game_moves_the_mods_files()
    {
        var c = Scanner.GameContext(_game);
        await Scanner.SaveProfileAsync("with-reshade", c);          // ReShade on

        await ModToggle.SetEnabledAsync(c, ReShade(), enabled: false);
        Assert.False(File.Exists(Path.Combine(Play, "ReShadePreset.ini")));

        await Scanner.LoadProfileAsync("with-reshade", Scanner.GameContext(_game));

        Assert.True(ReShade().Enabled);
        Assert.Equal("preset", File.ReadAllText(Path.Combine(Play, "ReShadePreset.ini")));
        Assert.True(File.Exists(Path.Combine(Play, "eldenring.exe")));
    }

    [Fact]
    public async Task The_plan_names_what_would_change_and_changes_nothing()
    {
        var c = Scanner.GameContext(_game);
        await Scanner.SaveProfileAsync("with-reshade", c);
        await ModToggle.SetEnabledAsync(c, ReShade(), enabled: false);

        var plan = Scanner.ProfilePlan("with-reshade", Scanner.GameContext(_game));

        var (mod, enable) = Assert.Single(plan);
        Assert.Equal("ReShade", mod.Name);
        Assert.True(enable);
        Assert.False(ReShade().Enabled);   // planning wrote nothing
    }

    [Fact]
    public async Task A_profile_matching_the_current_state_plans_nothing()
    {
        var c = Scanner.GameContext(_game);
        await Scanner.SaveProfileAsync("as-is", c);
        Assert.Empty(Scanner.ProfilePlan("as-is", Scanner.GameContext(_game)));
    }

    // Review on #369: saves now snapshot every lane, loaders included, and a load would silently move
    // the DLL loader every other mod rides on, without the warning its own row toggle gives.
    [Fact]
    public async Task A_loader_is_never_in_a_profile_plan()
    {
        File.WriteAllText(Path.Combine(Play, "dinput8.dll"), "dll");
        var c = Scanner.GameContext(_game);
        var loader = ModListing.Resolve(_game).Single(m => m.IsLoader);
        await ModToggle.SetEnabledAsync(c, loader, enabled: false);
        await Scanner.SaveProfileAsync("loader-off", c);                 // records the loader as off
        await ModToggle.SetEnabledAsync(c, ModListing.Resolve(_game).Single(m => m.IsLoader), enabled: true);

        Assert.Empty(Scanner.ProfilePlan("loader-off", Scanner.GameContext(_game)));
    }

    [Theory]
    [InlineData("{\"mods\":null}")]
    [InlineData("{\"mods\":[null,{\"enabled\":true}]}")]
    public void A_hand_edited_profile_with_nothing_usable_plans_nothing_rather_than_throw(string json)
    {
        var c = Scanner.GameContext(_game);
        Directory.CreateDirectory(c.ProfilesDir);
        File.WriteAllText(Path.Combine(c.ProfilesDir, "odd.json"), json);

        Assert.Empty(Scanner.ProfilePlan("odd", c));
    }
}
