using System.Text.Json;
using ModManager.Core;

namespace ModManager.Tests;

public class ModMetaRoundTripTests
{
    // Mirrors Scanner's on-disk options: camelCase write, case-insensitive read.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    [Fact]
    public void ModMeta_round_trips_installedUtc_and_sourceConfidence_as_camelCase()
    {
        var original = new ModMeta
        {
            Url = "https://www.nexusmods.com/eldenring/mods/510",
            InstalledUtc = new DateTime(2026, 4, 2, 12, 0, 0, DateTimeKind.Utc),
            SourceConfidence = "fingerprint",
            IsManual = true,
        };

        var json = JsonSerializer.Serialize(original, Json);
        Assert.Contains("\"installedUtc\"", json);       // camelCase key on disk
        Assert.DoesNotContain("\"InstalledUtc\"", json);
        Assert.Contains("\"sourceConfidence\"", json);
        Assert.DoesNotContain("\"SourceConfidence\"", json);

        var rt = JsonSerializer.Deserialize<ModMeta>(json, Json)!;
        Assert.Equal(original.InstalledUtc, rt.InstalledUtc);
        Assert.Equal("fingerprint", rt.SourceConfidence);
        Assert.True(rt.IsManual);
        Assert.Equal(original.Url, rt.Url);
    }
}
