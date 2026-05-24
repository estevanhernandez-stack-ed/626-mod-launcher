using ModManager.Core;

namespace ModManager.Tests;

// Ports engine-presets.test.js — engine presets + registry-entry assembly for Add Game.
public class EnginePresetsTests
{
    [Fact]
    public void Slugify()
    {
        Assert.Equal("windrose", EnginePresets.Slugify("Windrose"));
        Assert.Equal("skyrim-special-edition", EnginePresets.Slugify("Skyrim Special Edition!"));
        Assert.Equal("game", EnginePresets.Slugify(""));
    }

    [Fact]
    public void UniqueId_appends_suffix_on_collision()
    {
        Assert.Equal("windrose", EnginePresets.UniqueId("windrose", Array.Empty<string>()));
        Assert.Equal("windrose-2", EnginePresets.UniqueId("windrose", new[] { "windrose" }));
        Assert.Equal("windrose-3", EnginePresets.UniqueId("windrose", new[] { "windrose", "windrose-2" }));
    }

    [Fact]
    public void BuildGameEntry_applies_UE_preset_and_steam_fields()
    {
        var e = EnginePresets.BuildGameEntry(
            new GameInput { Name = "Windrose", Engine = "ue-pak", GameRoot = "C:/g/Windrose", SteamAppId = "3041230", ModPath = "R5/Content/Paks/~mods" },
            Array.Empty<string>());

        Assert.Equal("windrose", e.Id);
        Assert.Equal(new[] { "pak", "ucas", "utoc" }, e.FileExtensions.ToArray());
        Assert.Equal("strip_underscore_p_suffix", e.GroupingRule);
        Assert.Equal("R5/Content/Paks/~mods", e.ModLocations[0].Path);
        Assert.Equal("3041230", e.SteamAppId);
        Assert.Equal("steam://rungameid/3041230", e.LaunchUrl);
    }

    [Fact]
    public void BuildGameEntry_falls_back_to_preset_modpath_and_custom_default()
    {
        var e = EnginePresets.BuildGameEntry(
            new GameInput { Name = "My Game", Engine = "bethesda", GameRoot = "D:/g" }, Array.Empty<string>());
        Assert.Equal("Data", e.ModLocations[0].Path);
        Assert.Equal(new[] { "esp", "esl", "esm", "bsa" }, e.FileExtensions.ToArray());

        var c = EnginePresets.BuildGameEntry(
            new GameInput { Name = "Weird", Engine = "nope", GameRoot = "X" }, Array.Empty<string>());
        Assert.Equal("filename_no_ext", c.GroupingRule); // custom default
    }

    [Fact]
    public void BuildGameEntry_id_is_unique_against_existing()
    {
        var e = EnginePresets.BuildGameEntry(
            new GameInput { Name = "Windrose", Engine = "ue-pak", GameRoot = "X" }, new[] { "windrose" });
        Assert.Equal("windrose-2", e.Id);
    }

    [Fact]
    public void Every_preset_has_the_required_shape()
    {
        foreach (var (key, p) in EnginePresets.Presets)
        {
            Assert.False(string.IsNullOrEmpty(p.Label), key);
            Assert.NotNull(p.FileExtensions);
            Assert.False(string.IsNullOrEmpty(p.GroupingRule), key);
            Assert.False(string.IsNullOrEmpty(p.ModPath), key);
        }
    }
}
