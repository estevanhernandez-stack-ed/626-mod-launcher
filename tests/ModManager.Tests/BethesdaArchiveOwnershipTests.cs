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
}
