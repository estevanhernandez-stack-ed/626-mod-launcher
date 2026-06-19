// tests/ModManager.Tests/Plugins/PluginGateTests.cs
using ModManager.Core.Plugins;

namespace ModManager.Tests.Plugins;

public class PluginGateTests
{
    private static PluginIndexEntry Entry(string id, string version, string minBin) =>
        new(id, id, version, minBin, $"https://x/{id}.dll", $"https://x/{id}.dll.sig", "hash");

    private static PluginIndex Index(int schema, params PluginIndexEntry[] entries) => new(schema, entries);

    private static readonly Dictionary<string, string> None = new();

    [Fact]
    public void Eligible_entry_is_selected()
    {
        var got = PluginGate.SelectInstallable(
            Index(1, Entry("nexus", "1.0.0", "0.7.0")), new Version(0, 7, 0), None);
        Assert.Equal("nexus", Assert.Single(got).Id);
    }

    [Fact]
    public void Unknown_schema_rejects_the_whole_feed()
    {
        var got = PluginGate.SelectInstallable(
            Index(99, Entry("nexus", "1.0.0", "0.0.0")), new Version(9, 9, 9), None);
        Assert.Empty(got);
    }

    [Fact]
    public void Schema_zero_rejects_the_whole_feed()
    {
        // schemaVersion 0 is < Known but must NOT be treated as schema 1 — it's a malformed feed.
        var got = PluginGate.SelectInstallable(
            Index(0, Entry("nexus", "1.0.0", "0.0.0")), new Version(9, 9, 9), None);
        Assert.Empty(got);
    }

    [Fact]
    public void Entry_needing_a_newer_binary_is_skipped()
    {
        var got = PluginGate.SelectInstallable(
            Index(1, Entry("nexus", "2.0.0", "0.9.0")), new Version(0, 7, 0), None);
        Assert.Empty(got);
    }

    [Fact]
    public void Already_installed_at_same_version_is_skipped()
    {
        var got = PluginGate.SelectInstallable(
            Index(1, Entry("nexus", "1.0.0", "0.7.0")), new Version(0, 7, 0),
            new Dictionary<string, string> { ["nexus"] = "1.0.0" });
        Assert.Empty(got);
    }

    [Fact]
    public void Installed_at_older_version_is_selected_for_update()
    {
        var got = PluginGate.SelectInstallable(
            Index(1, Entry("nexus", "1.1.0", "0.7.0")), new Version(0, 7, 0),
            new Dictionary<string, string> { ["nexus"] = "1.0.0" });
        Assert.Equal("nexus", Assert.Single(got).Id);
    }

    [Fact]
    public void Unparseable_min_binary_version_skips_the_entry()
    {
        var got = PluginGate.SelectInstallable(
            Index(1, Entry("nexus", "1.0.0", "not-a-version")), new Version(9, 9, 9), None);
        Assert.Empty(got);
    }
}
