using ModManager.Core;

namespace ModManager.Tests;

// Md5IdentifyAsync — the md5 twin of FingerprintIdentifyAsync. Hash each just-dropped file,
// ask Nexus which mod it is by md5, merge that metadata (curated/CF wins, Nexus fills gaps).
public class Md5IdentifyTests
{
    // A hand-rolled fake INexusClient — only GetByMd5Async is exercised here.
    private sealed class FakeNexusClient : INexusClient
    {
        private readonly Func<string, string, Task<NexusMd5Match?>> _byMd5;
        public FakeNexusClient(Func<string, string, Task<NexusMd5Match?>> byMd5) => _byMd5 = byMd5;
        public Task<ModMeta?> GetModAsync(string gameDomain, int modId) => throw new NotSupportedException();
        public Task<NexusMd5Match?> GetByMd5Async(string gameDomain, string md5) => _byMd5(gameDomain, md5);
        public Task<NexusUser?> ValidateAsync() => throw new NotSupportedException();
    }

    private static (string modsDir, GameContext c) Fixture(string? nexusDomain)
    {
        var root = TestSupport.TempDir("md5id-");
        var gameRoot = Path.Combine(root, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" },
            NexusGameDomain = nexusDomain,
        });
        return (modsDir, c);
    }

    [Fact]
    public async Task Md5Identify_merges_nexus_metadata_for_a_matched_file()
    {
        var (modsDir, c) = Fixture("windrose");
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "COOLBYTES");
        var fake = new FakeNexusClient((_, _) =>
            Task.FromResult<NexusMd5Match?>(new NexusMd5Match(123, new ModMeta { Title = "Pirate Depot", Author = "someone" })));

        var r = await Scanner.Md5IdentifyAsync(c, fake, new[] { "cool.pak" });

        Assert.Equal(1, r.Matched);
        var meta = Scanner.LoadMetadata(c);
        var key = Variant.ParseVariant("cool").Base;
        Assert.True(meta.ContainsKey(key));
        Assert.Equal("Pirate Depot", meta[key].Title);
        Assert.Equal("someone", meta[key].Author);
    }

    [Fact]
    public async Task Md5Identify_returns_zero_when_no_nexus_domain()
    {
        var (modsDir, c) = Fixture(null);
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "COOLBYTES");
        var fake = new FakeNexusClient((_, _) =>
            Task.FromResult<NexusMd5Match?>(new NexusMd5Match(123, new ModMeta { Title = "Pirate Depot" })));

        var r = await Scanner.Md5IdentifyAsync(c, fake, new[] { "cool.pak" });

        Assert.Equal(0, r.Matched);
        Assert.Empty(Scanner.LoadMetadata(c));
    }

    [Fact]
    public async Task Md5Identify_returns_zero_when_no_match()
    {
        var (modsDir, c) = Fixture("windrose");
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "COOLBYTES");
        var fake = new FakeNexusClient((_, _) => Task.FromResult<NexusMd5Match?>(null));

        var r = await Scanner.Md5IdentifyAsync(c, fake, new[] { "cool.pak" });

        Assert.Equal(0, r.Matched);
        Assert.Empty(Scanner.LoadMetadata(c));
    }
}
