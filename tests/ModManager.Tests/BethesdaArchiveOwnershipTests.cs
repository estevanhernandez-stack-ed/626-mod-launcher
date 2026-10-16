using ModManager.Core;

namespace ModManager.Tests;

/// <summary>Archive ownership is built once per folder and read by lookup.</summary>
public class BethesdaArchiveOwnershipTests
{
    private const int Plugins = 600;

    [Fact]
    public void Ownership_is_settled_once_and_answers_by_lookup()
    {
        var files = new List<string>();
        for (var i = 0; i < Plugins; i++)
        {
            files.Add($"P {i}.esp"); files.Add($"P {i} - Main.ba2"); files.Add($"P {i} - Textures.ba2");
        }

        var own = PluginArchives.Ownership.Build(files);

        for (var i = 0; i < Plugins; i++)
            Assert.Equal(new[] { $"P {i} - Main.ba2", $"P {i} - Textures.ba2" }, own.OwnedBy($"P {i}.esp").OrderBy(f => f, StringComparer.Ordinal));
        Assert.True(own.IsClaimed("P 3 - Main.ba2"));
        Assert.Empty(own.OwnedBy("P 3 - Main.ba2")); // not a plugin
    }

    // Two " - " separators can overlap (" - - "). The lookup stepped back past the overlapping one and never
    // tried the shorter stem, so "Foo - - Main.ba2" lost its owner and stayed live when Foo was turned off.
    [Theory]
    [InlineData("Foo.esp", "Foo - - Main.ba2", "Foo")]
    [InlineData("A.esp", "A - - B.ba2", "A")]
    [InlineData("Foo -.esp", "Foo - - Main.ba2", "Foo -")]
    public void Overlapping_separators_still_find_the_owner(string plugin, string archive, string owner)
    {
        var own = PluginArchives.Ownership.Build(new[] { plugin, archive });

        Assert.Equal(owner, own.OwnerOf(archive));
        Assert.Equal(new[] { archive }, own.OwnedBy(plugin));
    }
}
