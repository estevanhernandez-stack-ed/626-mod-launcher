using ModManager.Core;

namespace ModManager.Tests.ConfigMods;

public class ConfigModTests
{
    [Theory]
    [InlineData("Engine.ini", true)]
    [InlineData("Scalability.ini", true)]
    [InlineData("Input.ini", true)]
    [InlineData("GameUserSettings.ini", true)]
    [InlineData("Game.ini", true)]
    [InlineData("engine.INI", true)]                 // case-insensitive
    [InlineData("Config/Engine.ini", true)]          // subfolder basename
    [InlineData(@"some\dir\GameUserSettings.ini", true)]
    [InlineData("settings.ini", false)]              // random ini is NOT a config mod
    [InlineData("Engine.txt", false)]
    [InlineData("mod_P.pak", false)]
    [InlineData("", false)]
    public void IsConfigFile_matches_known_UE_config_basenames(string name, bool expected)
        => Assert.Equal(expected, ConfigMod.IsConfigFile(name));

    [Fact]
    public void Config_only_payload_is_config()
        => Assert.True(ConfigMod.IsConfigOnlyPayload(
            new[] { "Engine.ini" }, new[] { "pak", "ucas", "utoc" }));

    [Fact]
    public void Payload_with_paks_is_not_config_even_if_it_bundles_a_config()
        => Assert.False(ConfigMod.IsConfigOnlyPayload(
            new[] { "CoolMod_P.pak", "Engine.ini" }, new[] { "pak", "ucas", "utoc" }));

    [Fact]
    public void Payload_with_random_ini_only_is_not_config()
        => Assert.False(ConfigMod.IsConfigOnlyPayload(
            new[] { "settings.ini" }, new[] { "pak" }));

    [Fact]
    public void Junk_entries_are_ignored_when_judging_config_only()
        => Assert.True(ConfigMod.IsConfigOnlyPayload(
            new[] { "README.txt", "preview.jpg", "Engine.ini" }, new[] { "pak" }));

    [Fact]
    public void Empty_payload_is_not_config()
        => Assert.False(ConfigMod.IsConfigOnlyPayload(Array.Empty<string>(), new[] { "pak" }));

    [Fact]
    public void ClassifyDrop_returns_config_for_a_loose_known_config_file()
        => Assert.Equal("config", Intake.ClassifyDrop(@"C:\downloads\Engine.ini", new[] { "pak" }));

    [Fact]
    public void ClassifyDrop_still_skips_a_random_ini()
        => Assert.Equal("skip", Intake.ClassifyDrop(@"C:\downloads\settings.ini", new[] { "pak" }));

    [Fact]
    public void ClassifyDrop_zip_and_mod_verdicts_unchanged()
    {
        Assert.Equal("zip", Intake.ClassifyDrop("a.zip", new[] { "pak" }));
        Assert.Equal("mod", Intake.ClassifyDrop("a.pak", new[] { "pak" }));
        Assert.Equal("skip", Intake.ClassifyDrop("a.exe", new[] { "pak" }));
    }
}
