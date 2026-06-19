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

    [Fact]
    public void MinimumBinaryToUnblock_reports_the_floor_when_the_binary_is_too_old()
    {
        var need = PluginGate.MinimumBinaryToUnblock(
            Index(1, Entry("nexus", "1.0.0", "0.9.0")), new Version(0, 7, 0), None);
        Assert.Equal(new Version(0, 9, 0), need);
    }

    [Fact]
    public void MinimumBinaryToUnblock_is_null_when_everything_is_installable()
    {
        var need = PluginGate.MinimumBinaryToUnblock(
            Index(1, Entry("nexus", "1.0.0", "0.7.0")), new Version(0, 7, 0), None);
        Assert.Null(need);
    }

    [Fact]
    public void MinimumBinaryToUnblock_picks_the_lowest_unblocking_version()
    {
        // Two gated plugins; the lowest floor is the smallest launcher bump that unblocks anything.
        var need = PluginGate.MinimumBinaryToUnblock(
            Index(1, Entry("a", "1.0.0", "1.2.0"), Entry("b", "1.0.0", "0.9.0")),
            new Version(0, 7, 0), None);
        Assert.Equal(new Version(0, 9, 0), need);
    }

    [Fact]
    public void MinimumBinaryToUnblock_ignores_an_already_current_entry()
    {
        // Already installed at the listed version → not something an update would "unblock".
        var need = PluginGate.MinimumBinaryToUnblock(
            Index(1, Entry("nexus", "1.0.0", "0.9.0")), new Version(0, 7, 0),
            new Dictionary<string, string> { ["nexus"] = "1.0.0" });
        Assert.Null(need);
    }

    [Fact]
    public void MinimumBinaryToUnblock_is_null_for_an_unknown_schema()
    {
        // A schema we refuse isn't a version problem — don't suggest a launcher update for it.
        var need = PluginGate.MinimumBinaryToUnblock(
            Index(99, Entry("nexus", "1.0.0", "9.9.9")), new Version(0, 7, 0), None);
        Assert.Null(need);
    }
}
