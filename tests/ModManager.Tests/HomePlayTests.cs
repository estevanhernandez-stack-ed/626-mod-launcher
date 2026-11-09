using ModManager.Core;

namespace ModManager.Tests;

// Play on the library home. Found sweeping for ACE COMBAT 8 ripples, 2026-11-09: the home ran the game's
// DEFAULT target whatever the mode, so an Elden Ring in vanilla mode started Mod Engine 2 or Seamless (no
// anti-cheat), and it skipped the save offer and the Steam start the game view's Play does. The home now
// launches in place only when the game view would do nothing more; anything else opens the game view.
public class HomePlayTests
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

    private static HomePlayVerdict Decide(GameEntry game, LaunchMode mode, bool steamRunning = true,
        bool seamless = false, Func<LaunchTarget, LaunchMode, bool>? offer = null)
        => HomePlay.Decide(game, mode, seamless, steamRunning, offer ?? ((_, _) => false));

    [Fact]
    public void Vanilla_never_runs_the_default_mod_launcher_from_the_home()
    {
        var v = Decide(EldenRing(Seamless(), SteamTarget()), LaunchMode.Vanilla, seamless: true);
        Assert.Equal(HomePlayKind.Launch, v.Kind);
        Assert.Equal("steam", v.Target!.Kind);
    }

    [Fact]
    public void Vanilla_with_no_safe_way_in_opens_the_game_view()
    {
        var game = new GameEntry { Id = "x", GameName = "X", GameRoot = Root, LaunchTargets = new List<LaunchTarget> { Seamless() } };
        var v = Decide(game, LaunchMode.Vanilla);
        Assert.Equal(HomePlayKind.OpenGameView, v.Kind);
    }

    [Fact]
    public void Modded_plain_case_launches_the_same_target_the_game_view_would()
    {
        var game = EldenRing(SteamTarget(isDefault: true), Seamless(isDefault: false));
        var v = Decide(game, LaunchMode.Modded, seamless: true);
        Assert.Equal(HomePlayKind.Launch, v.Kind);
        Assert.Contains("ersc_launcher", v.Target!.Target);
    }

    [Fact]
    public void A_required_launcher_opens_the_game_view()
    {
        var game = EldenRing(Seamless());
        game.RequiredLauncher = "ersc_launcher.exe";
        Assert.Equal(HomePlayKind.OpenGameView, Decide(game, LaunchMode.Modded).Kind);
    }

    [Fact]
    public void An_exe_target_on_a_Steam_game_with_Steam_closed_opens_the_game_view()
    {
        var v = Decide(EldenRing(Seamless()), LaunchMode.Modded, steamRunning: false);
        Assert.Equal(HomePlayKind.OpenGameView, v.Kind);
    }

    [Fact]
    public void A_steam_target_with_Steam_closed_still_launches_here()
    {
        var v = Decide(EldenRing(SteamTarget(isDefault: true)), LaunchMode.Modded, steamRunning: false);
        Assert.Equal(HomePlayKind.Launch, v.Kind);
    }

    [Fact]
    public void A_save_offer_opens_the_game_view_and_is_asked_with_the_launch_target_and_mode()
    {
        LaunchTarget? askedTarget = null;
        LaunchMode? askedMode = null;
        var v = Decide(EldenRing(Seamless(), SteamTarget()), LaunchMode.Vanilla,
            offer: (t, m) => { askedTarget = t; askedMode = m; return true; });
        Assert.Equal(HomePlayKind.OpenGameView, v.Kind);
        Assert.Equal("steam", askedTarget!.Kind);
        Assert.Equal(LaunchMode.Vanilla, askedMode);
    }

    [Fact]
    public void No_targets_in_modded_keeps_the_legacy_launch()
    {
        var game = new GameEntry { Id = "x", GameName = "X", GameRoot = Root, LaunchUrl = "steam://rungameid/1" };
        Assert.Equal(HomePlayKind.LaunchLegacy, Decide(game, LaunchMode.Modded).Kind);
    }

    [Fact]
    public void A_save_probe_that_throws_opens_the_game_view_rather_than_launching_unasked()
    {
        var v = Decide(EldenRing(SteamTarget(isDefault: true)), LaunchMode.Modded,
            offer: (_, _) => throw new IOException("locked"));
        Assert.Equal(HomePlayKind.OpenGameView, v.Kind);
    }
}
