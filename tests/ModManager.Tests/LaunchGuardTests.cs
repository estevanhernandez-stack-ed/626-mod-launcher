using ModManager.Core;

namespace ModManager.Tests;

// Launch enforcement: when a game declares a required launcher (e.g. Seamless Co-op) and mods are
// enabled, the launcher is the default Play and a vanilla launch must confirm first. Pure verdicts.
public class LaunchGuardTests
{
    private static GameEntry Game(string? requiredLauncher)
        => new() { Id = "g", GameName = "G", RequiredLauncher = requiredLauncher };

    private static LaunchTarget Steam() => new("Play vanilla (Steam)", "steam", "steam://rungameid/1");
    private static LaunchTarget Exe() => new("Play (Seamless Co-op)", "exe", @"C:\game\ersc_launcher.exe");

    [Fact]
    public void RequiresLauncher_true_when_set_and_a_mod_is_enabled()
        => Assert.True(LaunchGuard.RequiresLauncher(Game("ersc_launcher.exe"), anyModsEnabled: true));

    [Fact]
    public void RequiresLauncher_false_when_no_mods_enabled()
        => Assert.False(LaunchGuard.RequiresLauncher(Game("ersc_launcher.exe"), anyModsEnabled: false));

    [Fact]
    public void RequiresLauncher_false_when_no_required_launcher()
    {
        Assert.False(LaunchGuard.RequiresLauncher(Game(null), anyModsEnabled: true));
        Assert.False(LaunchGuard.RequiresLauncher(Game(""), anyModsEnabled: true));
    }

    [Fact]
    public void NeedsVanillaConfirm_true_for_a_steam_target_when_enforcement_active()
        => Assert.True(LaunchGuard.NeedsVanillaConfirm(Game("ersc_launcher.exe"), anyModsEnabled: true, Steam()));

    [Fact]
    public void NeedsVanillaConfirm_false_for_an_exe_launcher_target()
        => Assert.False(LaunchGuard.NeedsVanillaConfirm(Game("ersc_launcher.exe"), anyModsEnabled: true, Exe()));

    [Fact]
    public void NeedsVanillaConfirm_false_when_enforcement_inactive()
    {
        Assert.False(LaunchGuard.NeedsVanillaConfirm(Game(null), anyModsEnabled: true, Steam()));
        Assert.False(LaunchGuard.NeedsVanillaConfirm(Game("ersc_launcher.exe"), anyModsEnabled: false, Steam()));
    }

    // Direct-inject DLLs (dinput8/ersc/ReShade) load into ANY process started from the game folder,
    // so a vanilla/steam launch with them active crashes the game start ("unable to start correctly").
    // Distinct from RequiredLauncher: that's "mods won't load"; this is "the game won't start at all".

    [Fact]
    public void NeedsDirectInjectStepAside_true_for_a_steam_target_when_dlls_active()
        => Assert.True(LaunchGuard.NeedsDirectInjectStepAside(Steam(), anyDirectInjectDllsActive: true));

    [Fact]
    public void NeedsDirectInjectStepAside_false_for_an_exe_launcher_target()
        => Assert.False(LaunchGuard.NeedsDirectInjectStepAside(Exe(), anyDirectInjectDllsActive: true));

    [Fact]
    public void NeedsDirectInjectStepAside_false_when_no_dlls_active()
        => Assert.False(LaunchGuard.NeedsDirectInjectStepAside(Steam(), anyDirectInjectDllsActive: false));

    // An exe launcher (e.g. Seamless's ersc_launcher.exe) on a Steam-DRM game needs Steam running
    // first, or the DRM bootstrap never completes and the launch silently no-ops. steam:// targets
    // self-start Steam, and a non-Steam game doesn't depend on it.

    private static GameEntry SteamGame(string? appId)
        => new() { Id = "er", GameName = "ELDEN RING", SteamAppId = appId };

    [Fact]
    public void NeedsSteamRunning_true_for_exe_target_on_a_steam_game()
        => Assert.True(LaunchGuard.NeedsSteamRunning(SteamGame("1245620"), Exe()));

    [Fact]
    public void NeedsSteamRunning_false_for_steam_url_target()
        => Assert.False(LaunchGuard.NeedsSteamRunning(SteamGame("1245620"), Steam()));

    [Fact]
    public void NeedsSteamRunning_false_when_no_steam_app_id()
        => Assert.False(LaunchGuard.NeedsSteamRunning(SteamGame(null), Exe()));
}
