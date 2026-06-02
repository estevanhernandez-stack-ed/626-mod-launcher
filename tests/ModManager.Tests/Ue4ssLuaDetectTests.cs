using ModManager.Core;

namespace ModManager.Tests;

public class Ue4ssLuaDetectTests
{
    [Fact]
    public void Detects_a_classic_ue4ss_lua_mod_by_Scripts_lua()
    {
        var entries = new[]
        {
            "R5ModSettings/",
            "R5ModSettings/Scripts/",
            "R5ModSettings/Scripts/main.lua",
            "R5ModSettings/Scripts/R5ModSettings.lua",
            "R5ModSettings/enabled.txt",
            "R5ModSettings/dlls/main.dll",
        };
        var v = Ue4ssLuaDetect.Detect(entries);
        Assert.True(v.IsLuaMod);
        Assert.Equal("R5ModSettings", v.ModFolderName);
    }

    [Fact]
    public void Detects_a_lua_mod_with_only_enabled_txt_and_dlls()
    {
        var entries = new[] { "NativeMod/enabled.txt", "NativeMod/dlls/main.dll" };
        var v = Ue4ssLuaDetect.Detect(entries);
        Assert.True(v.IsLuaMod);
        Assert.Equal("NativeMod", v.ModFolderName);
    }

    [Fact]
    public void A_pak_anywhere_vetoes_lua_detection()
    {
        var entries = new[] { "R5ModSettings/Scripts/main.lua", "extra/AwesomeMod_P.pak" };
        var v = Ue4ssLuaDetect.Detect(entries);
        Assert.False(v.IsLuaMod);
    }

    [Fact]
    public void Bare_lua_files_without_a_folder_are_not_a_lua_mod()
    {
        var entries = new[] { "main.lua", "helper.lua" };
        Assert.False(Ue4ssLuaDetect.Detect(entries).IsLuaMod);
    }

    [Fact]
    public void Empty_entries_return_not_a_lua_mod()
    {
        Assert.False(Ue4ssLuaDetect.Detect(Array.Empty<string>()).IsLuaMod);
    }

    [Fact]
    public void Detects_a_lua_mod_nested_inside_a_version_wrapper_folder()
    {
        // The "Windrose Shanties Anywhere" archive from Nexus wraps the mod folder in a version
        // folder: <version>/<mod>/Scripts/main.lua. The mod that should land under ue4ss\Mods is the
        // INNER folder, not the version wrapper. (Real bug: this silently installed nothing.)
        var entries = new[]
        {
            "Windrose Shanties Anywhere v1/",
            "Windrose Shanties Anywhere v1/Windrose Shanties Anywhere/",
            "Windrose Shanties Anywhere v1/Windrose Shanties Anywhere/enabled.txt",
            "Windrose Shanties Anywhere v1/Windrose Shanties Anywhere/mod.txt",
            "Windrose Shanties Anywhere v1/Windrose Shanties Anywhere/Scripts/main.lua",
        };
        var v = Ue4ssLuaDetect.Detect(entries);
        Assert.True(v.IsLuaMod);
        Assert.Equal("Windrose Shanties Anywhere", v.ModFolderName);
    }

    [Fact]
    public void A_pak_anywhere_still_vetoes_even_when_a_nested_scripts_folder_exists()
    {
        // The veto must win regardless of nesting depth — a content mod that also ships a Scripts
        // folder is still a content mod, not a Lua mod.
        var entries = new[]
        {
            "Pack v2/InnerMod/Scripts/main.lua",
            "Pack v2/Content_P.pak",
        };
        Assert.False(Ue4ssLuaDetect.Detect(entries).IsLuaMod);
    }
}
