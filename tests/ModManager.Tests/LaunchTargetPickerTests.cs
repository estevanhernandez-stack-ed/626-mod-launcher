using ModManager.Core;

namespace ModManager.Tests;

// Which target Play runs. Found on the 2026-11-07 Patch Day smoke: with Seamless Co-op installed, "Play
// vanilla for now" stepped the mods aside and then launched ersc_launcher.exe, which skips Easy Anti-Cheat
// and injects Seamless itself. Vanilla must never resolve to a mod launcher.
public class LaunchTargetPickerTests
{
    private const string Root = @"C:\Steam\steamapps\common\ELDEN RING";

    private static LaunchTarget Seamless(bool isDefault = true)
        => new("Play (Seamless Co-op)", "exe", Root + @"\Game\ersc_launcher.exe") { IsDefault = isDefault };

    private static LaunchTarget SteamTarget(bool isDefault = false)
        => new("Play vanilla (Steam)", "steam", "steam://rungameid/1245620") { IsDefault = isDefault };

    private static GameEntry EldenRing(params LaunchTarget[] targets) => new()
    {
        Id = "elden-ring", GameName = "ELDEN RING", GameRoot = Root, SteamAppId = "1245620",
        LaunchTargets = targets.ToList(),
    };

    [Fact]
    public void Vanilla_with_Seamless_installed_picks_the_Steam_target_not_the_Seamless_launcher()
    {
        var game = EldenRing(Seamless(), SteamTarget());
        var pick = LaunchTargetPicker.Pick(game, LaunchMode.Vanilla, seamlessFullyInstalled: true);
        Assert.NotNull(pick);
        Assert.Equal("steam", pick!.Kind);
        Assert.Equal("steam://rungameid/1245620", pick.Target);
    }

    [Fact]
    public void Modded_with_Seamless_installed_still_picks_the_Seamless_launcher()
    {
        var game = EldenRing(SteamTarget(isDefault: true), Seamless(isDefault: false));
        var pick = LaunchTargetPicker.Pick(game, LaunchMode.Modded, seamlessFullyInstalled: true);
        Assert.Contains("ersc_launcher", pick!.Target);
    }

    [Fact]
    public void Modded_without_Seamless_falls_back_to_the_default_target()
    {
        var game = EldenRing(SteamTarget(), Seamless());
        var pick = LaunchTargetPicker.Pick(game, LaunchMode.Modded, seamlessFullyInstalled: false);
        Assert.Contains("ersc_launcher", pick!.Target); // the explicit default, as before
    }

    [Fact]
    public void Vanilla_with_only_a_mod_launcher_registered_builds_a_Steam_target_from_the_app_id()
    {
        var game = EldenRing(Seamless());
        var pick = LaunchTargetPicker.Pick(game, LaunchMode.Vanilla, seamlessFullyInstalled: true);
        Assert.Equal("steam", pick!.Kind);
        Assert.Equal("steam://rungameid/1245620", pick.Target);
    }

    [Fact]
    public void Vanilla_prefers_the_legacy_launch_url_over_a_synthesized_one()
    {
        var game = EldenRing(Seamless());
        game.LaunchUrl = "steam://rungameid/1245620//-custom";
        var pick = LaunchTargetPicker.Pick(game, LaunchMode.Vanilla, seamlessFullyInstalled: true);
        Assert.Equal("steam://rungameid/1245620//-custom", pick!.Target);
    }

    [Theory]
    [InlineData(@"Game\ersc_launcher.exe")]
    [InlineData(@"Game\launch_elden_ring_seamlesscoop.exe")]
    [InlineData(@"modengine2_launcher.exe")]
    [InlineData(@"ModEngine2\modengine2_launcher.exe")]
    public void Vanilla_never_returns_a_known_mod_launcher(string relative)
    {
        var game = new GameEntry
        {
            Id = "g", GameName = "G", GameRoot = Root,
            LaunchTargets = [new LaunchTarget("Play modded", "exe", relative) { IsDefault = true }],
        };
        Assert.Null(LaunchTargetPicker.Pick(game, LaunchMode.Vanilla, seamlessFullyInstalled: true));
    }

    [Fact]
    public void Vanilla_on_a_non_Steam_game_runs_its_own_exe_target()
    {
        var game = new GameEntry
        {
            Id = "g", GameName = "G", GameRoot = @"C:\Games\G",
            LaunchTargets =
            [
                new LaunchTarget("Play modded", "exe", "modengine2_launcher.exe") { IsDefault = true },
                new LaunchTarget("Play", "exe", @"C:\Games\G\game.exe"),
            ],
        };
        var pick = LaunchTargetPicker.Pick(game, LaunchMode.Vanilla, seamlessFullyInstalled: false);
        Assert.Equal(@"C:\Games\G\game.exe", pick!.Target);
    }

    [Fact]
    public void Vanilla_on_a_target_less_game_uses_its_launch_exe_unless_it_is_a_mod_launcher()
    {
        var plain = new GameEntry { Id = "g", GameName = "G", GameRoot = @"C:\Games\G", LaunchExe = "game.exe" };
        Assert.Equal("game.exe", LaunchTargetPicker.Pick(plain, LaunchMode.Vanilla, false)!.Target);

        var modded = new GameEntry { Id = "g", GameName = "G", GameRoot = @"C:\Games\G", LaunchExe = "ersc_launcher.exe" };
        Assert.Null(LaunchTargetPicker.Pick(modded, LaunchMode.Vanilla, false));
    }

    [Fact]
    public void Modded_on_a_target_less_game_returns_null_so_the_legacy_path_runs()
    {
        var game = new GameEntry { Id = "g", GameName = "G", SteamAppId = "1" };
        Assert.Null(LaunchTargetPicker.Pick(game, LaunchMode.Modded, false));
    }
}
