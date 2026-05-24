using ModManager.Core;

namespace ModManager.Tests;

// Ports refresh-metadata.test.js — search-by-name metadata refresh (curated wins, CF fills gaps).
public class RefreshMetadataTests
{
    private static (GameContext c, string modsDir) Fixture(params string[] files)
    {
        var root = TestSupport.TempDir("refreshmeta-");
        var gameRoot = Path.Combine(root, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        foreach (var f in files) File.WriteAllText(Path.Combine(modsDir, f), "x");
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "Windrose", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        });
        return (c, modsDir);
    }

    // Fixed gameId; returns canned hits when the query contains a known phrase.
    private static FakeCurseForgeClient FakeByQuery(Dictionary<string, CfMod[]> byQuery) => new()
    {
        OnResolveGameId = _ => Task.FromResult<int?>(99078),
        OnSearch = (_, query) =>
        {
            var k = byQuery.Keys.FirstOrDefault(key => query.ToLowerInvariant().Contains(key));
            return Task.FromResult<IReadOnlyList<CfMod>>(k is not null ? byQuery[k] : Array.Empty<CfMod>());
        },
    };

    [Fact]
    public async Task Writes_cf_metadata_for_confident_matches_skips_rest()
    {
        var (c, _) = Fixture("BlackMarketShipyard.pak", "TotallyLocalMod.pak");
        var client = FakeByQuery(new()
        {
            ["black market shipyard"] = new[]
            {
                new CfMod { Id = 1528245, Name = "Black Market Shipyard", Summary = "Buy/sell goods",
                    Authors = new() { new CfAuthor { Name = "an0nym0uz", Url = "https://cf/an0nym0uz" } },
                    Links = new CfLinks { WebsiteUrl = "https://cf/bms" } },
            },
        });

        var r = await Scanner.RefreshMetadataByNameAsync(c, client);

        Assert.Equal(99078, r.GameId);
        Assert.Equal(1, r.Matched);
        Assert.Equal(2, r.Total);
        var meta = Scanner.LoadMetadata(c);
        Assert.Equal("an0nym0uz", meta["BlackMarketShipyard"].Author);
        Assert.Equal("Black Market Shipyard", meta["BlackMarketShipyard"].Title);
        Assert.Equal(1528245, meta["BlackMarketShipyard"].CurseforgeId);
        Assert.False(meta.ContainsKey("TotallyLocalMod"));
    }

    [Fact]
    public async Task Curated_fields_win_and_only_fill_gaps()
    {
        var (c, _) = Fixture("BlackMarketShipyard.pak");
        Scanner.SaveMetadata(c, new Dictionary<string, ModMeta>
        {
            ["BlackMarketShipyard"] = new() { Title = "My Custom Title", Description = "curated desc" },
        });
        var client = FakeByQuery(new()
        {
            ["black market shipyard"] = new[]
            {
                new CfMod { Id = 1528245, Name = "Black Market Shipyard", Summary = "cf summary",
                    Authors = new() { new CfAuthor { Name = "an0nym0uz" } }, Links = new CfLinks() },
            },
        });

        await Scanner.RefreshMetadataByNameAsync(c, client);

        var e = Scanner.LoadMetadata(c)["BlackMarketShipyard"];
        Assert.Equal("My Custom Title", e.Title);
        Assert.Equal("curated desc", e.Description);
        Assert.Equal("an0nym0uz", e.Author);
        Assert.Equal(1528245, e.CurseforgeId);
    }

    [Fact]
    public async Task Resolves_gameid_from_game_name_when_not_preset()
    {
        var (c, _) = Fixture("x.pak");
        string? askedFor = null;
        var client = new FakeCurseForgeClient
        {
            OnResolveGameId = name => { askedFor = name; return Task.FromResult<int?>(99078); },
            OnSearch = (_, _) => Task.FromResult<IReadOnlyList<CfMod>>(Array.Empty<CfMod>()),
        };
        var r = await Scanner.RefreshMetadataByNameAsync(c, client);
        Assert.Equal("Windrose", askedFor);
        Assert.Equal(99078, r.GameId);
    }
}
