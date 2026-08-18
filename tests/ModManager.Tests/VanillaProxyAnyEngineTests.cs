using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// A16. "Play vanilla" stepped aside nothing on a Monster Hunter Wilds install whose REFramework
/// proxy (dinput8.dll) was hijacking the process, because the App gated the proxy check on
/// engine == "fromsoft". The game kept loading REFramework and kept crashing — a seventeen-month-old
/// loader against a freshly patched build.
///
/// <para>The gate contradicted the rule its own neighbouring comment states: "The OS hijack is a fact
/// of the filesystem, not of how the launcher chooses to display rows." A proxy DLL at the play-folder
/// root hijacks every process regardless of which engine the game runs.</para>
///
/// <para>These lock the pure detector against a REAL RE Engine game root, because the risk of
/// un-gating is a false positive on a DLL the game legitimately ships — and that list is long and
/// modern (DLSS, FidelityFX, DirectStorage, Streamline).</para>
/// </summary>
public class VanillaProxyAnyEngineTests
{
    // Verbatim top-level DLL listing from G:\SteamLibrary\steamapps\common\MonsterHunterWilds
    // on 2026-08-18, an RE Engine game with REFramework installed.
    private static readonly string[] RealReEngineGameRoot =
    {
        "AkConvolutionReverb.dll", "AkSoundSeedAir.dll", "CrashHandler.dll", "CrashReportDll.dll",
        "GFSDK_Aftermath_Lib.x64.dll", "MasteringSuite.dll", "PartyWin.dll", "amd_ags_x64.dll",
        "amd_fidelityfx_framegeneration_dx12.dll", "amd_fidelityfx_loader_dx12.dll",
        "amd_fidelityfx_upscaler_dx12.dll", "dinput8.dll", "dstorage.dll", "dstoragecore.dll",
        "fmodex64.dll", "libxess.dll", "nvngx_dlss.dll", "nvngx_dlssd.dll", "nvngx_dlssg.dll",
        "sl.common.dll", "sl.dlss.dll", "sl.dlss_d.dll", "sl.dlss_g.dll", "sl.interposer.dll",
        "sl.pcl.dll", "sl.reflex.dll", "steam_api64.dll", "unrar.dll",
        "MonsterHunterWilds.exe", "Modmanager.exe",
    };

    [Fact]
    public void Finds_the_refr_proxy_in_a_real_re_engine_game_root()
    {
        var found = DirectInject.ProcessLoadProxiesIn(RealReEngineGameRoot);

        // Exactly one, and it is the loader that was still injecting after "mods off".
        Assert.Equal(new[] { "dinput8.dll" }, found);
    }

    [Fact]
    public void Does_not_mistake_a_modern_game_s_own_dlls_for_proxies()
    {
        // The un-gating risk is a false positive here: stepping aside DLSS or DirectStorage would
        // break the game while claiming to make it vanilla. None of these share a proxy name.
        var shipped = RealReEngineGameRoot.Where(f => f != "dinput8.dll").ToArray();

        Assert.Empty(DirectInject.ProcessLoadProxiesIn(shipped));
        Assert.False(DirectInject.AnyProcessLoadProxy(shipped));
    }

    [Fact]
    public void The_detector_is_engine_agnostic_by_construction()
    {
        // It takes filenames, never a GameEntry — so nothing about it can depend on the engine. The
        // bug was a caller-side gate; this pins that the pure layer never grows one.
        var reEngine = new[] { "dinput8.dll", "MonsterHunterWilds.exe" };
        var fromSoft = new[] { "dinput8.dll", "eldenring.exe" };
        var unreal   = new[] { "dinput8.dll", "Windrose-Win64-Shipping.exe" };

        Assert.Equal(DirectInject.ProcessLoadProxiesIn(fromSoft), DirectInject.ProcessLoadProxiesIn(reEngine));
        Assert.Equal(DirectInject.ProcessLoadProxiesIn(fromSoft), DirectInject.ProcessLoadProxiesIn(unreal));
    }

    [Fact]
    public void Finds_every_recognised_proxy_name_not_just_dinput8()
    {
        // ReShade ships as dxgi.dll, ASI loaders as version.dll, Seamless as ersc.dll. A vanilla
        // launch that only knew dinput8 would leave the others injecting.
        var root = new[] { "dxgi.dll", "version.dll", "winmm.dll", "ersc.dll", "game.exe" };

        Assert.Equal(4, DirectInject.ProcessLoadProxiesIn(root).Count);
    }
}
