using System.Text.Json;
using ModManager.Core.Manifest;

namespace ModManager.Tests.Manifest;

// camelCase-on-disk rule: the launcher's JSON shares shape with the Electron predecessor.
// The string-contains assertion is what protects the convention — STJ reads case-insensitively,
// so a round-trip alone would pass even if keys serialized as PascalCase.
public class GameManifestJsonTests
{
    [Fact]
    public void Manifest_round_trips_as_camelCase()
    {
        var original = new GameManifest
        {
            SchemaVersion = 1,
            MinBinaryVersion = "0.6.0",
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "elden-ring",
                    Name = "Elden Ring",
                    Engine = "fromsoft",
                    Stores = new StoreIds { SteamAppId = "1245620" },
                    NexusDomain = "eldenring",
                    Provenance = new ManifestProvenance
                    {
                        Sources = new[] { ManifestSources.KnownEngines, ManifestSources.NexusDomains },
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(original, ManifestJson.Options);

        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"steamAppId\"", json);
        Assert.Contains("\"nexusDomain\"", json);
        Assert.DoesNotContain("\"SchemaVersion\"", json);
        Assert.DoesNotContain("\"SteamAppId\"", json);

        var back = JsonSerializer.Deserialize<GameManifest>(json, ManifestJson.Options);
        Assert.NotNull(back);
        Assert.Equal("elden-ring", back!.Games[0].Id);
        Assert.Equal("1245620", back.Games[0].Stores.SteamAppId);
        Assert.Equal("fromsoft", back.Games[0].Engine);
        Assert.Contains(ManifestSources.NexusDomains, back.Games[0].Provenance.Sources);
    }
}
