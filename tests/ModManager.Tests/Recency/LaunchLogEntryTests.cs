using System.Text.Json;
using ModManager.Core.Recency;

namespace ModManager.Tests.Recency;

public class LaunchLogEntryTests
{
    // Mirrors AtomicJson's on-disk options: camelCase write.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    [Fact]
    public void LaunchLogEntry_round_trips_as_camelCase()
    {
        var original = new LaunchLogEntry(
            GameId: "eldenring",
            StartedUtc: new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
            EndedUtc: new DateTime(2026, 7, 1, 12, 30, 0, DateTimeKind.Utc));

        var json = JsonSerializer.Serialize(original, Json);
        Assert.Contains("\"gameId\"", json);
        Assert.DoesNotContain("\"GameId\"", json);
        Assert.Contains("\"startedUtc\"", json);
        Assert.DoesNotContain("\"StartedUtc\"", json);
        Assert.Contains("\"endedUtc\"", json);
        Assert.DoesNotContain("\"EndedUtc\"", json);

        var rt = JsonSerializer.Deserialize<LaunchLogEntry>(json, Json)!;
        Assert.Equal(original.GameId, rt.GameId);
        Assert.Equal(original.StartedUtc, rt.StartedUtc);
        Assert.Equal(original.EndedUtc, rt.EndedUtc);
    }

    [Fact]
    public void LaunchLogEntry_endedUtc_is_optional()
    {
        var original = new LaunchLogEntry("skyrim", new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc), null);

        var json = JsonSerializer.Serialize(original, Json);
        var rt = JsonSerializer.Deserialize<LaunchLogEntry>(json, Json)!;

        Assert.Null(rt.EndedUtc);
        Assert.Equal(original.StartedUtc, rt.StartedUtc);
    }
}
