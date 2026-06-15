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

    [Fact]
    public void ModMeta_nexus_fields_round_trip_as_camelCase()
    {
        var m = new ModMeta { EndorsementCount = 1234, Version = "2.3", Available = false, ContainsAdultContent = false, NexusModId = 510, NexusFileId = 99 };
        var json = JsonSerializer.Serialize(m, Json);
        Assert.Contains("\"endorsementCount\"", json);
        Assert.Contains("\"nexusModId\"", json);
        Assert.Contains("\"nexusFileId\"", json);
        Assert.DoesNotContain("\"EndorsementCount\"", json);
        var rt = JsonSerializer.Deserialize<ModMeta>(json, Json)!;
        Assert.Equal(1234, rt.EndorsementCount);
        Assert.Equal("2.3", rt.Version);
        Assert.False(rt.Available);
        Assert.Equal(510, rt.NexusModId);
        Assert.Equal(99, rt.NexusFileId);
    }

    [Fact]
    public void ModMeta_nexusLatestVersion_round_trips_as_camelCase()
    {
        var m = new ModMeta { NexusLatestVersion = "2.1" };
        var json = JsonSerializer.Serialize(m, Json);
        Assert.Contains("\"nexusLatestVersion\"", json);          // camelCase key on disk
        Assert.DoesNotContain("\"NexusLatestVersion\"", json);
        var rt = JsonSerializer.Deserialize<ModMeta>(json, Json)!;
        Assert.Equal("2.1", rt.NexusLatestVersion);
    }

    [Fact]
    public void ModMeta_endorsed_round_trips_as_camelCase()
    {
        var m = new ModMeta { Endorsed = true };
        var json = JsonSerializer.Serialize(m, Json);
        Assert.Contains("\"endorsed\"", json);          // camelCase key on disk
        Assert.DoesNotContain("\"Endorsed\"", json);
        var rt = JsonSerializer.Deserialize<ModMeta>(json, Json)!;
        Assert.True(rt.Endorsed);
    }
}
