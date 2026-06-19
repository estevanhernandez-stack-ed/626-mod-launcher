// tests/ModManager.Tests/Plugins/PluginIndexTests.cs
using System.Text;
using ModManager.Core.Plugins;

namespace ModManager.Tests.Plugins;

public class PluginIndexTests
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void TryParse_reads_a_camelCase_index()
    {
        var json = """
        { "schemaVersion": 1, "plugins": [
          { "id": "nexus", "displayName": "Nexus Mods", "version": "1.0.0",
            "minBinaryVersion": "0.7.0",
            "downloadUrl": "https://x/nexus.dll", "sigUrl": "https://x/nexus.dll.sig",
            "sha256": "abc123" } ] }
        """;
        Assert.True(PluginIndex.TryParse(Utf8(json), out var index));
        Assert.Equal(1, index!.SchemaVersion);
        var e = Assert.Single(index.Plugins);
        Assert.Equal("nexus", e.Id);
        Assert.Equal("Nexus Mods", e.DisplayName);
        Assert.Equal("1.0.0", e.Version);
        Assert.Equal("0.7.0", e.MinBinaryVersion);
        Assert.Equal("https://x/nexus.dll", e.DownloadUrl);
        Assert.Equal("https://x/nexus.dll.sig", e.SigUrl);
        Assert.Equal("abc123", e.Sha256);
    }

    [Fact]
    public void TryParse_returns_false_on_garbage()
    {
        Assert.False(PluginIndex.TryParse(Utf8("not json"), out var index));
        Assert.Null(index);
    }

    [Fact]
    public void TryParse_returns_false_on_empty_input()
    {
        Assert.False(PluginIndex.TryParse(Array.Empty<byte>(), out _));
    }
}
