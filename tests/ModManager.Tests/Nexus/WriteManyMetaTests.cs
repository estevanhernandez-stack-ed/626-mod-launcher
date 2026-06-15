using ModManager.Core;

namespace ModManager.Tests.Nexus;

// WriteManyMeta persists a batch of refreshed metas in one atomic pass (reusing LoadMetadata /
// SaveMetadata), leaving untouched entries intact and round-tripping NexusLatestVersion as
// camelCase on disk.
public class WriteManyMetaTests
{
    private static GameContext Fixture()
    {
        var root = TestSupport.TempDir("writemany-");
        var gameRoot = Path.Combine(root, "game");
        Directory.CreateDirectory(gameRoot);
        return Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" },
            NexusGameDomain = "eldenring",
        });
    }

    [Fact]
    public void WriteManyMeta_persists_batch_and_reloads_with_latest_version()
    {
        var c = Fixture();
        Scanner.WriteManyMeta(c, new (string, ModMeta)[]
        {
            ("modA", new ModMeta { Title = "A", NexusModId = 1, Version = "1.0", NexusLatestVersion = "2.1", EndorsementCount = 50 }),
            ("modB", new ModMeta { Title = "B", NexusModId = 2, Version = "3.0", NexusLatestVersion = "3.0" }),
        });

        var reloaded = Scanner.LoadMetadata(c);

        Assert.Equal(2, reloaded.Count);
        Assert.Equal("2.1", reloaded["modA"].NexusLatestVersion);
        Assert.Equal("1.0", reloaded["modA"].Version);
        Assert.Equal(50, reloaded["modA"].EndorsementCount);
        Assert.Equal("3.0", reloaded["modB"].NexusLatestVersion);
    }

    [Fact]
    public void WriteManyMeta_leaves_other_entries_untouched_and_overwrites_matching_keys()
    {
        var c = Fixture();
        // seed one entry the batch does not touch + one it will overwrite
        Scanner.SaveMetadata(c, new Dictionary<string, ModMeta>
        {
            ["keep"] = new ModMeta { Title = "Keep me", NexusModId = 9 },
            ["modA"] = new ModMeta { Title = "old A", NexusModId = 1, NexusLatestVersion = null },
        });

        Scanner.WriteManyMeta(c, new (string, ModMeta)[]
        {
            ("modA", new ModMeta { Title = "old A", NexusModId = 1, Version = "1.0", NexusLatestVersion = "5.0" }),
        });

        var reloaded = Scanner.LoadMetadata(c);

        Assert.Equal(2, reloaded.Count);
        Assert.Equal("Keep me", reloaded["keep"].Title);       // untouched
        Assert.Equal("5.0", reloaded["modA"].NexusLatestVersion); // overwritten
    }

    [Fact]
    public void WriteManyMeta_writes_camelCase_on_disk()
    {
        var c = Fixture();
        Scanner.WriteManyMeta(c, new (string, ModMeta)[]
        {
            ("modA", new ModMeta { NexusModId = 1, NexusLatestVersion = "2.1" }),
        });

        var json = File.ReadAllText(c.MetadataPath);
        Assert.Contains("\"nexusLatestVersion\"", json);
        Assert.DoesNotContain("\"NexusLatestVersion\"", json);
        Assert.Contains("\"nexusModId\"", json);
    }
}
