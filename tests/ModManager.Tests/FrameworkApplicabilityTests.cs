using ModManager.Core;

namespace ModManager.Tests;

public class FrameworkApplicabilityTests
{
    private static Mod Pak(string? loader = null) => new() { Name = "X", Loader = loader, IsFolder = false };
    private static Mod LuaFolder() => new() { Name = "X", Loader = "ue4ss", IsFolder = true };

    [Fact]
    public void Lua_mod_needs_ue4ss_regardless_of_location()
        => Assert.True(FrameworkApplicability.ModNeedsUe4ss(LuaFolder(), "R5/Content/Paks/~mods"));

    [Fact]
    public void LogicMods_pak_needs_ue4ss()
        => Assert.True(FrameworkApplicability.ModNeedsUe4ss(Pak(), "R5/Content/Paks/LogicMods"));

    [Fact]
    public void Plain_content_pak_in_mods_does_not_need_ue4ss()
        => Assert.False(FrameworkApplicability.ModNeedsUe4ss(Pak(), "R5/Content/Paks/~mods"));

    [Fact]
    public void Plain_content_pak_in_paks_root_does_not_need_ue4ss()
        => Assert.False(FrameworkApplicability.ModNeedsUe4ss(Pak(), "Witchfire/Content/Paks"));

    [Fact]
    public void LogicMods_match_is_case_and_separator_insensitive()
    {
        Assert.True(FrameworkApplicability.ModNeedsUe4ss(Pak(), @"R5\Content\Paks\logicmods"));
        Assert.True(FrameworkApplicability.ModNeedsUe4ss(Pak(), "R5/Content/Paks/LogicMods/"));
    }

    [Fact]
    public void Null_or_empty_location_with_plain_pak_does_not_need_ue4ss()
    {
        Assert.False(FrameworkApplicability.ModNeedsUe4ss(Pak(), ""));
        Assert.False(FrameworkApplicability.ModNeedsUe4ss(Pak(), null!));
    }
}
