using System.Text.Json;
using ModManager.Core;

namespace ModManager.Tests;

public class GameEntryRecencyFieldsTests
{
    private static readonly JsonSerializerOptions Opts = new()
    { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    [Fact]
    public void StoreSource_and_LastLaunchedUtc_round_trip_as_camelCase()
    {
        var e = new GameEntry { Id = "g", GameName = "G", GameRoot = @"C:\g",
            StoreSource = "steam", LastLaunchedUtc = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc) };
        var json = JsonSerializer.Serialize(e, Opts);
        Assert.Contains("\"storeSource\"", json);
        Assert.Contains("\"lastLaunchedUtc\"", json);
        Assert.DoesNotContain("\"StoreSource\"", json);
        var back = JsonSerializer.Deserialize<GameEntry>(json, Opts)!;
        Assert.Equal("steam", back.StoreSource);
        Assert.Equal(e.LastLaunchedUtc, back.LastLaunchedUtc);
    }

    [Fact]
    public void Existing_json_without_the_new_fields_deserializes_with_nulls()
    {
        var back = JsonSerializer.Deserialize<GameEntry>(
            "{\"id\":\"g\",\"gameName\":\"G\",\"gameRoot\":\"C:\\\\g\"}", Opts)!;
        Assert.Null(back.StoreSource);
        Assert.Null(back.LastLaunchedUtc);
    }
}
