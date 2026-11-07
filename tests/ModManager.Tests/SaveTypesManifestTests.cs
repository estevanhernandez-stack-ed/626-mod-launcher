using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests;

/// <summary>
/// Save types come from the signed game manifest (save-type offer, decision 4: "if the game or its mods can do
/// it, we should too"), each with a kind that decides the warning on a copy into vanilla. The engine fallback
/// stays for FromSoft games the manifest doesn't describe.
/// </summary>
public class SaveTypesManifestTests
{
    [Fact]
    public void SaveTypes_round_trip_as_camelCase()
    {
        var e = new GameManifestEntry
        {
            Id = "x", Name = "X",
            SaveTypes = new[] { new ManifestSaveType { Extension = ".co2", Label = "Seamless Co-op", Kind = "coop" } },
        };
        var json = JsonSerializer.Serialize(e, ManifestJson.Options);
        Assert.Contains("\"saveTypes\"", json);
        Assert.Contains("\"extension\"", json);
        Assert.Contains("\"kind\"", json);
        Assert.DoesNotContain("\"SaveTypes\"", json);
        var back = JsonSerializer.Deserialize<GameManifestEntry>(json, ManifestJson.Options)!;
        Assert.Equal("coop", back.SaveTypes![0].Kind);
        Assert.Equal(".co2", back.SaveTypes[0].Extension);
    }

    [Fact]
    public void Elden_ring_declares_four_types_with_kinds()
    {
        var types = GameSaveTypesCatalog.Resolve(SaveTestGames.Steam("1245620", "fromsoft")).SaveTypes;
        Assert.Equal(new[] { ".sl2", ".co2", ".err", ".mod" }, types.Select(t => t.Extension));
        Assert.Equal(new[] { "vanilla", "coop", "overhaul", "overhaul" }, types.Select(t => t.Kind));
    }

    [Fact]
    public void Another_fromsoft_game_keeps_the_engine_fallback_with_kinds()
    {
        var types = GameSaveTypesCatalog.Resolve(SaveTestGames.Steam("0", "fromsoft")).SaveTypes;
        Assert.Equal(new[] { ".sl2", ".co2", ".err" }, types.Select(t => t.Extension));
        Assert.Equal(new[] { "vanilla", "coop", "overhaul" }, types.Select(t => t.Kind));
    }

    [Theory]
    [InlineData("co2")]
    [InlineData(".")]
    [InlineData("../x")]
    [InlineData(".toolongext1")]
    [InlineData("")]
    public void Validator_drops_a_malformed_extension(string ext)
    {
        var types = new[]
        {
            new ManifestSaveType { Extension = ext, Label = "L" },
            new ManifestSaveType { Extension = ".sl2", Label = "Vanilla", Kind = "vanilla" },
        };
        Assert.Equal(new[] { ".sl2" }, ManifestValidator.SafeSaveTypes(types)!.Select(t => t.Extension));
    }

    [Fact]
    public void Validator_returns_null_when_nothing_survives()
        => Assert.Null(ManifestValidator.SafeSaveTypes(new[] { new ManifestSaveType { Extension = ".sl2", Label = " " } }));

    [Fact]
    public void An_unknown_kind_is_kept_as_written_and_never_throws()
    {
        var json = "{\"id\":\"x\",\"name\":\"X\",\"saveTypes\":[{\"extension\":\".abc\",\"label\":\"New\",\"kind\":\"somethingNew\"}]}";
        var e = JsonSerializer.Deserialize<GameManifestEntry>(json, ManifestJson.Options)!;
        Assert.Equal("somethingNew", e.SaveTypes![0].Kind);
    }
}
