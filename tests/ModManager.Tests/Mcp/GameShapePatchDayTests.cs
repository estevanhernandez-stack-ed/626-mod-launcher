using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Manifest;
using ModManager.Mcp;
using ModManager.Mcp.Tools;

namespace ModManager.Tests.Mcp;

/// <summary>get_game_shape's read-only patchDay block: null with no record, camelCase keys with one.</summary>
[Collection("ManifestState")]
public class GameShapePatchDayTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null);

    private static JsonElement Shape(string patchDayJson)
    {
        EffectiveManifest.SetRemote(null);
        var root = TestSupport.TempDir("mcp-pd-").Replace('\\', '/');
        var dir = TestSupport.TempDir("mcp-pd-data-");
        File.WriteAllText(Path.Combine(dir, "games.json"), $$"""
            { "version": 1, "activeGameId": "pd", "games": [
              { "id": "pd", "gameName": "PD", "engine": "custom", "gameRoot": "{{root}}",
                "fileExtensions": ["pak"]{{patchDayJson}},
                "modLocations": [ { "name": "mods", "label": "Mods", "path": "mods" } ] } ] }
            """);
        McpConfig.DataRoot = dir;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ModTools.GetGameShape("pd")));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void No_record_means_patchDay_is_null()
    {
        var s = Shape("");
        Assert.True(s.TryGetProperty("patchDay", out var p));
        Assert.Equal(JsonValueKind.Null, p.ValueKind);
    }

    [Fact]
    public void A_record_reports_state_build_choice_and_restore_point()
    {
        var s = Shape(", \"patchDay\": { \"buildId\": \"200\", \"detectedUtc\": \"2026-10-16T09:00:00Z\", "
            + "\"restorePointId\": \"rp-1\", \"choice\": \"modded\", \"snoozedUntilChange\": true }");
        var p = s.GetProperty("patchDay");
        Assert.Equal("patched", p.GetProperty("state").GetString());
        Assert.Equal("200", p.GetProperty("detectedBuild").GetString());
        Assert.Equal("modded", p.GetProperty("choice").GetString());
        Assert.True(p.GetProperty("snoozed").GetBoolean());
        Assert.Equal("rp-1", p.GetProperty("restorePoint").GetString());
        Assert.Equal(JsonValueKind.Null, p.GetProperty("restorePointError").ValueKind);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("loader").ValueKind);
        Assert.Equal(JsonValueKind.Null, p.GetProperty("latestRelease").ValueKind);
    }

    [Fact]
    public void State_names_are_camelCase()
    {
        Assert.Equal("playingVanilla", PatchDayShape.StateName(PatchDayState.PlayingVanilla));
        Assert.Equal("updateAvailable", PatchDayShape.StateName(PatchDayState.UpdateAvailable));
        Assert.Equal("loaderUpdated", PatchDayShape.StateName(PatchDayState.LoaderUpdated));
        Assert.Equal("quiet", PatchDayShape.StateName(PatchDayState.Quiet));
    }
}
