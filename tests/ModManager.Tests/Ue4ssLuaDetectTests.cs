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
}
