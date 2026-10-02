using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Manifest;
using ModManager.Mcp;
using ModManager.Mcp.Tools;

namespace ModManager.Tests.Mcp;

/// <summary>
/// get_game_shape says when a declared path was corrected by the game's definition (B1). Without it
/// an agent reads <c>path: "mods"</c> against a games.json that says <c>mod</c> and has no way to
/// reconcile the two.
///
/// <para>In the ManifestState collection, not McpDataRoot: it needs the process-global remote manifest
/// as well as McpConfig.DataRoot, and ManifestState disables parallelization outright, so nothing in
/// McpDataRoot can run alongside it.</para>
/// </summary>
[Collection("ManifestState")]
public class GameShapeToolTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null);

    private static JsonElement OnlyLocation(string gameId)
    {
        var json = JsonSerializer.Serialize(ModTools.GetGameShape(gameId));
        using var doc = JsonDocument.Parse(json);
        var locs = doc.RootElement.GetProperty("declaredLocations");
        Assert.Equal(1, locs.GetArrayLength());
        return locs[0].Clone();
    }

    private static void Seed(string gamesJson)
    {
        var dir = TestSupport.TempDir("mcp-shape-");
        File.WriteAllText(Path.Combine(dir, "games.json"), gamesJson);
        McpConfig.DataRoot = dir;
    }

    [Fact]
    public void A_corrected_path_reports_the_corrected_path_and_what_games_json_says()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "elden-ring", Name = "ELDEN RING", Engine = "fromsoft", ModPath = "mods",
                    Provenance = new ManifestProvenance { Sources = new[] { "known-engines" } },
                },
            },
        });
        var root = TestSupport.TempDir("mcp-shape-er-").Replace('\\', '/');
        Seed($$"""
            { "version": 1, "activeGameId": "elden-ring", "games": [
              { "id": "elden-ring", "gameName": "ELDEN RING", "engine": "fromsoft", "gameRoot": "{{root}}",
                "groupingRule": "by_folder",
                "modLocations": [ { "name": "mods", "label": "mods", "path": "mod" } ] } ] }
            """);

        var loc = OnlyLocation("elden-ring");

        Assert.Equal("mods", loc.GetProperty("path").GetString());
        Assert.True(loc.GetProperty("declared").GetBoolean());
        Assert.Equal("mod", loc.GetProperty("correctedFrom").GetString());
    }

    [Fact]
    public void Nothing_corrected_means_no_correctedFrom()
    {
        EffectiveManifest.SetRemote(null);
        var root = TestSupport.TempDir("mcp-shape-plain-").Replace('\\', '/');
        Seed($$"""
            { "version": 1, "activeGameId": "plain-shape", "games": [
              { "id": "plain-shape", "gameName": "Plain", "engine": "custom", "gameRoot": "{{root}}",
                "fileExtensions": ["pak"],
                "modLocations": [ { "name": "mods", "label": "Mods", "path": "mods" } ] } ] }
            """);

        var loc = OnlyLocation("plain-shape");

        Assert.Equal("mods", loc.GetProperty("path").GetString());
        Assert.True(!loc.TryGetProperty("correctedFrom", out var c) || c.ValueKind == JsonValueKind.Null);
    }
}
