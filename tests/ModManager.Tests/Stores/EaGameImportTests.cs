using ModManager.Core;
using ModManager.Core.Manifest;
using ModManager.Core.Stores;

namespace ModManager.Tests.Stores;

[Collection("ManifestState")]
public class EaGameImportTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null);

    private static readonly GameManifestEntry[] Manifest =
    {
        new() { Id = "ea-sports-college-football-27", Name = "EA SPORTS College Football 27", Stores = new StoreIds { SteamAppId = "4032350", EaContentId = "16425899" }, BanRisk = "high" },
        new() { Id = "madden-nfl-27", Name = "Madden NFL 27", Stores = new StoreIds { SteamAppId = "3940610", EaContentId = "16425895" }, BanRisk = "high" },
    };

    private static InstalledGame Ea(string id, string name = "Whatever") =>
        new("ea", id, name, Path.Combine("C:", "Program Files", "EA Games", name)) { BuildId = "1.0.0.1" };

    [Fact]
    public void A_matching_content_id_plans_the_manifest_game()
    {
        var input = EaGameImport.Plan(Ea("16425895", "Madden NFL 27"), Manifest, Path.Combine("L"))!;

        Assert.Equal("madden-nfl-27", input.Id);
        Assert.Equal("Madden NFL 27", input.Name);
        Assert.Equal("frostbite", input.Engine);
        Assert.Equal(Path.Combine("C:", "Program Files", "EA Games", "Madden NFL 27"), input.GameRoot);
        Assert.Equal("16425895", input.EaContentId);
        Assert.Equal("origin2://game/launch/?offerIds=16425895", input.LaunchUrl);
        Assert.Equal(Path.Combine("L", "626mods", "madden-nfl-27"), input.DataDir);
        Assert.Null(input.SteamAppId);
    }

    [Fact]
    public void No_content_id_match_means_no_plan_even_when_the_name_matches()
        => Assert.Null(EaGameImport.Plan(Ea("99999999", "Madden NFL 27"), Manifest, "L"));

    [Fact]
    public void A_steam_install_is_never_planned_as_an_ea_game()
        => Assert.Null(EaGameImport.Plan(new InstalledGame("steam", "16425895", "Madden NFL 27", "C:"), Manifest, "L"));

    [Theory]
    [InlineData("16425899")]
    [InlineData("16425895")]
    public void The_entry_an_ea_import_builds_resolves_high_ban_risk_with_or_without_a_feed(string contentId)
    {
        var entry = EnginePresets.BuildGameEntry(EaGameImport.Plan(Ea(contentId), Manifest, "L")!, null);

        EffectiveManifest.SetRemote(null);
        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(entry));
        EffectiveManifest.SetRemote(new GameManifest { Games = Manifest });
        Assert.Equal(GameBanRisk.High, BanRiskCatalog.Effective(entry));
    }
}
