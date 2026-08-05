using System.Text.Json;
using ModManager.Core.Discovery;

namespace ModManager.Tests.Discovery;

// camelCase-on-disk law. The string-contains assertion is what protects it — STJ reads
// case-insensitively, so a round-trip alone passes even with PascalCase keys.
public class ModNameIndexJsonTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    [Fact]
    public void Index_round_trips_as_camelCase()
    {
        var original = ModNameIndex.Merge(
            ModNameIndex.Empty,
            new[] { new ModNameIndexEntry(510, "Seamless Co-op", "LukeYui", 42000) });

        var json = JsonSerializer.Serialize(original, JsonOpts);

        Assert.Contains("\"modId\"", json);
        Assert.Contains("\"endorsements\"", json);
        Assert.DoesNotContain("\"ModId\"", json);
        Assert.DoesNotContain("\"Endorsements\"", json);
        Assert.Contains("\"entries\"", json);
        Assert.DoesNotContain("\"Entries\"", json);
        Assert.DoesNotContain("\"Name\"", json);
        Assert.DoesNotContain("\"Author\"", json);

        var back = JsonSerializer.Deserialize<ModNameIndex>(json, JsonOpts);
        Assert.NotNull(back);
        var only = Assert.Single(back!.Entries);
        Assert.Equal(510, only.ModId);
        Assert.Equal("Seamless Co-op", only.Name);
        Assert.Equal("LukeYui", only.Author);
    }
}
