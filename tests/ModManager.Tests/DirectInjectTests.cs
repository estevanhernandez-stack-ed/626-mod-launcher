using ModManager.Core;

namespace ModManager.Tests;

// Recognizing direct-inject FromSoft mods from what's in the game's exe folder. These mods don't
// use Mod Engine 2 — they're loose files dropped next to eldenring.exe (ReShade, Seamless Co-op,
// frame-gen loaders, a replaced regulation.bin). Detection is by known signature, case-insensitive.
public class DirectInjectTests
{
    private static IReadOnlyList<DirectInjectMod> Detect(string[] files, string[] dirs)
        => DirectInject.Detect(files, dirs);

    [Fact]
    public void Detects_reshade_from_its_shaders_folder()
    {
        var m = Detect(new[] { "d3d12.dll", "ReShadePreset.ini" }, new[] { "reshade-shaders" });
        Assert.Contains(m, x => x.Name == "ReShade" && x.Kind == "graphics");
    }

    [Fact]
    public void Detects_seamless_coop_from_ersc_dll()
        => Assert.Contains(Detect(new[] { "ersc.dll", "ersc_settings.ini" }, Array.Empty<string>()),
            x => x.Name == "Seamless Co-op" && x.Kind == "co-op");

    [Fact]
    public void Detects_erss2_framegen()
        => Assert.Contains(Detect(new[] { "ERSS-FG.dll", "ERSS-FG.toml" }, new[] { "ERSS2" }),
            x => x.Name.Contains("ERSS") && x.Kind == "upscaler");

    [Fact]
    public void A_loose_regulation_bin_is_a_gameplay_mod()
        => Assert.Contains(Detect(new[] { "regulation.bin", "eldenring.exe" }, Array.Empty<string>()),
            x => x.Kind == "gameplay");

    [Fact]
    public void Vanilla_files_yield_nothing()
    {
        var m = Detect(
            new[] { "eldenring.exe", "start_protected_game.exe", "Data0.bdt", "Data0.bhd", "steam_api64.dll", "oo2core_6_win64.dll" },
            new[] { "EasyAntiCheat", "movie" });
        Assert.Empty(m);
    }

    [Fact]
    public void Detection_is_case_insensitive_and_counts_each_mod_once()
    {
        // Folder + file both signal ReShade; it should appear exactly once.
        var m = Detect(new[] { "reshadepreset.ini" }, new[] { "ReShade-Shaders" });
        Assert.Single(m, x => x.Name == "ReShade");
    }
}
