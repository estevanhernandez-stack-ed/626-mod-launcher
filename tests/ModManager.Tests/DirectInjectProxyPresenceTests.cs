using ModManager.Core;

namespace ModManager.Tests;

// A process-load proxy DLL (dinput8 / dxgi / version / winhttp / ersc ...) sitting at the TOP of the
// game's exe folder is auto-loaded by Windows into ANY process started from that folder — including a
// plain vanilla/Steam launch, which it then crashes (0xc000007b / 0xc0000142). The crash-guard must
// detect this from PHYSICAL presence at the play-folder top, independent of the UI's mod-row list
// (which drops the loader row when its mods\ folder has contents — the bug that blinded the guard).
public class DirectInjectProxyPresenceTests
{
    [Fact]
    public void Dinput8_at_top_level_is_a_process_load_proxy()
        => Assert.True(DirectInject.AnyProcessLoadProxy(new[] { "dinput8.dll", "eldenring.exe" }));

    [Fact]
    public void Other_known_proxies_are_detected()
    {
        Assert.True(DirectInject.AnyProcessLoadProxy(new[] { "dxgi.dll" }));
        Assert.True(DirectInject.AnyProcessLoadProxy(new[] { "version.dll" }));
        Assert.True(DirectInject.AnyProcessLoadProxy(new[] { "winhttp.dll" }));
        Assert.True(DirectInject.AnyProcessLoadProxy(new[] { "ersc.dll" }));
    }

    [Fact]
    public void Match_is_case_insensitive()
        => Assert.True(DirectInject.AnyProcessLoadProxy(new[] { "DINPUT8.DLL" }));

    [Fact]
    public void Vanilla_top_level_files_yield_no_proxy()
        => Assert.False(DirectInject.AnyProcessLoadProxy(
            new[] { "eldenring.exe", "start_protected_game.exe", "steam_api64.dll", "oo2core_6_win64.dll" }));

    [Fact]
    public void Empty_yields_false()
        => Assert.False(DirectInject.AnyProcessLoadProxy(Array.Empty<string>()));
}
