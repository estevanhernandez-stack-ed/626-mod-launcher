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
                    SaveDirHint = "<home>/EldenRing",
                    BanRisk = "high",
                    SafeRoute = "offline",
                    SafeRouteHint = "Mod offline with EAC disabled; never carry modded files into vanilla online play.",
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
        // Ban-risk nuance (batch 4): the safe-route facet rides next to banRisk, camelCase like
        // everything else, and must survive the round-trip so the feed can publish it.
        Assert.Contains("\"safeRoute\"", json);
        Assert.Contains("\"safeRouteHint\"", json);
        Assert.DoesNotContain("\"SafeRoute\"", json);
        Assert.Contains("\"saveDirHint\"", json);
        Assert.DoesNotContain("\"SchemaVersion\"", json);
        Assert.DoesNotContain("\"SteamAppId\"", json);
        Assert.DoesNotContain("\"SaveDirHint\"", json);

        var back = JsonSerializer.Deserialize<GameManifest>(json, ManifestJson.Options);
        Assert.NotNull(back);
        Assert.Equal("elden-ring", back!.Games[0].Id);
        Assert.Equal("1245620", back.Games[0].Stores.SteamAppId);
        Assert.Equal("fromsoft", back.Games[0].Engine);
        Assert.Equal("offline", back.Games[0].SafeRoute);
        Assert.StartsWith("Mod offline", back.Games[0].SafeRouteHint);
        Assert.Contains(ManifestSources.NexusDomains, back.Games[0].Provenance.Sources);
    }

    [Fact]
    public void BanRisk_round_trips_as_camelCase()
    {
        var original = new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry { Id = "marvel-rivals", Name = "Marvel Rivals", BanRisk = "high" },
            },
        };

        var json = JsonSerializer.Serialize(original, ManifestJson.Options);
        Assert.Contains("\"banRisk\"", json);          // camelCase key on disk
        Assert.DoesNotContain("\"BanRisk\"", json);     // guards against PascalCase regression

        var back = JsonSerializer.Deserialize<GameManifest>(json, ManifestJson.Options);
        Assert.Equal("high", back!.Games[0].BanRisk);
    }
}

public class SaveLayoutManifestFieldTests
{
    [Fact]
    public void It_round_trips_as_camelCase_on_disk()
    {
        var entry = new GameManifestEntry { Id = "palworld", SaveLayout = "worlds" };

        var json = JsonSerializer.Serialize(entry, ManifestJson.Options);

        Assert.Contains("\"saveLayout\"", json);        // the assertion that actually protects it:
        Assert.DoesNotContain("\"SaveLayout\"", json);  // deserialization is case-insensitive
        Assert.Equal("worlds", JsonSerializer.Deserialize<GameManifestEntry>(json, ManifestJson.Options)!.SaveLayout);
    }

    [Fact]
    public void An_unknown_value_from_a_newer_feed_does_not_take_the_whole_manifest_down()
    {
        // Modelled as a string, not the SaveLayout enum, for exactly this. System.Text.Json throws a
        // JsonException on an unrecognised enum member, and ManifestLoader catches JsonException by
        // returning null - which drops the ENTIRE feed, all 150 games, over one unknown word.
        var json = """{"id":"g","saveLayout":"somethingWeHaveNeverHeardOf"}""";

        var entry = JsonSerializer.Deserialize<GameManifestEntry>(json, ManifestJson.Options);

        Assert.NotNull(entry);
        Assert.Equal("somethingWeHaveNeverHeardOf", entry!.SaveLayout);
    }

    [Fact]
    public void An_entry_without_it_is_silent_rather_than_claiming_a_default()
    {
        // "Nobody looked" and "we looked and it is flat" must stay distinguishable in the DATA. The
        // runtime collapses them to TypedFiles; the manifest must not.
        //
        // The key is present and null rather than absent - ManifestJson.Options does not set
        // DefaultIgnoreCondition, and every shipped entry already writes its unset fields this way.
        // Null is the absence of a claim either way; what matters is that it never round-trips into a
        // value.
        var json = JsonSerializer.Serialize(new GameManifestEntry { Id = "g" }, ManifestJson.Options);

        Assert.DoesNotContain("\"saveLayout\": \"", json);   // no value, in particular not a default
        Assert.Null(JsonSerializer.Deserialize<GameManifestEntry>(json, ManifestJson.Options)!.SaveLayout);
    }
}
