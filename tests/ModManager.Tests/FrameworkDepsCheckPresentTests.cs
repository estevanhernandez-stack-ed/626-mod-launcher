using ModManager.Core;

namespace ModManager.Tests;

// Pins the FrameworkDeps.CheckPresent probe. For UE-pak games, UE4SS detection probes
// under each ModLocation's project subfolder (e.g. "R5/Content/Paks/~mods" -> probe under
// "R5/Binaries/Win64/ue4ss/UE4SS.dll"), with the bare game root as a fallback. For non-UE
// engines, detect paths resolve relative to the game root only.
public class FrameworkDepsCheckPresentTests
{
    private static GameContext Ctx(string root, string engine, string modPath)
    {
        var game = new GameEntry
        {
            Id = "g",
            GameName = "Test",
            Engine = engine,
            GameRoot = root,
            ModLocations = new[] { new ModLocation("mods", "mods", modPath) },
            GroupingRule = "filename_no_ext",
            FileExtensions = new[] { "pak" },
        };
        return Scanner.GameContext(game);
    }

    [Fact]
    public void Ue_pak_with_ue4ss_dll_under_project_subfolder_is_present()
    {
        var root = TestSupport.TempDir("fwdep-");
        var bin = Path.Combine(root, "R5", "Binaries", "Win64", "ue4ss");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "UE4SS.dll"), "x");
        var ctx = Ctx(root, "ue-pak", "R5/Content/Paks/~mods");

        var missing = FrameworkDeps.CheckPresent(ctx);

        Assert.DoesNotContain(missing, d => d.Name == "UE4SS");
    }

    [Fact]
    public void Ue_pak_without_ue4ss_returns_ue4ss_as_missing()
    {
        var root = TestSupport.TempDir("fwdep-");
        var ctx = Ctx(root, "ue-pak", "R5/Content/Paks/~mods");

        var missing = FrameworkDeps.CheckPresent(ctx);

        Assert.Contains(missing, d => d.Name == "UE4SS");
    }

    [Fact]
    public void Bepinex_present_via_winhttp_at_root()
    {
        var root = TestSupport.TempDir("fwdep-");
        File.WriteAllText(Path.Combine(root, "winhttp.dll"), "x");
        var ctx = Ctx(root, "bepinex", "BepInEx/plugins");

        var missing = FrameworkDeps.CheckPresent(ctx);

        Assert.DoesNotContain(missing, d => d.Name == "BepInEx");
    }

    [Fact]
    public void Bepinex_absent_returns_bepinex_as_missing()
    {
        var root = TestSupport.TempDir("fwdep-");
        var ctx = Ctx(root, "bepinex", "BepInEx/plugins");

        var missing = FrameworkDeps.CheckPresent(ctx);

        Assert.Contains(missing, d => d.Name == "BepInEx");
    }

    [Fact]
    public void Smapi_present_via_exe_at_root()
    {
        var root = TestSupport.TempDir("fwdep-");
        File.WriteAllText(Path.Combine(root, "StardewModdingAPI.exe"), "x");
        var ctx = Ctx(root, "smapi", "Mods");

        var missing = FrameworkDeps.CheckPresent(ctx);

        Assert.DoesNotContain(missing, d => d.Name == "SMAPI");
    }

    [Fact]
    public void Fromsoft_returns_both_me2_and_dll_proxy_when_neither_present()
    {
        var root = TestSupport.TempDir("fwdep-");
        var ctx = Ctx(root, "fromsoft", "mod");

        var missing = FrameworkDeps.CheckPresent(ctx);

        Assert.Contains(missing, d => d.Name == "Mod Engine 2");
        Assert.Contains(missing, d => d.Name == "Elden Mod Loader");
    }

    [Fact]
    public void Fromsoft_with_dinput8_satisfies_dll_proxy_only()
    {
        var root = TestSupport.TempDir("fwdep-");
        File.WriteAllText(Path.Combine(root, "dinput8.dll"), "x");
        var ctx = Ctx(root, "fromsoft", "mod");

        var missing = FrameworkDeps.CheckPresent(ctx);

        Assert.DoesNotContain(missing, d => d.Name == "Elden Mod Loader");
        Assert.Contains(missing, d => d.Name == "Mod Engine 2"); // still missing
    }

    [Fact]
    public void Fromsoft_with_dinput8_in_Game_subfolder_satisfies_Elden_Mod_Loader()
    {
        // ER's eldenring.exe lives under <gameRoot>/Game/. ELM's dinput8.dll proxy must sit
        // next to the exe to load, so the file ACTUALLY lands at <gameRoot>/Game/dinput8.dll
        // when installed correctly. The probe must check there, not just the ship root.
        var root = TestSupport.TempDir("fwdep-");
        Directory.CreateDirectory(Path.Combine(root, "Game"));
        File.WriteAllText(Path.Combine(root, "Game", "dinput8.dll"), "x");
        var ctx = Ctx(root, "fromsoft", "mod");

        var missing = FrameworkDeps.CheckPresent(ctx);

        Assert.DoesNotContain(missing, d => d.Name == "Elden Mod Loader");
    }

    [Fact]
    public void Unknown_engine_returns_empty()
    {
        var root = TestSupport.TempDir("fwdep-");
        var ctx = Ctx(root, "custom", "mods");

        Assert.Empty(FrameworkDeps.CheckPresent(ctx));
    }
}
