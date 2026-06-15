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
        public Task<IReadOnlyList<NexusUpdateEntry>> GetRecentlyUpdatedAsync(string d, string p) => throw new NotSupportedException();
        public Task<EndorseOutcome> EndorseAsync(string d, int id, string v, EndorseAction a) => throw new NotSupportedException();
        public NexusRateLimit? LastRateLimit => null;
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

    // Nexus indexes the md5 of the PUBLISHED ARCHIVE, not the extracted file — so identification
    // must hash the dropped zip and apply the match to every mod key that zip contributes.
    [Fact]
    public async Task Md5IdentifyArchives_hashes_the_dropped_zip_and_applies_to_its_mod_keys()
    {
        var (_, c) = Fixture("windrose");
        var zipPath = Path.Combine(TestSupport.TempDir("md5arch-"), "PirateDepot-1234.zip");
        TestSupport.WriteZip(zipPath, ("cool.pak", "PAKBYTES"), ("readme.txt", "hi"));
        var expectedArchiveMd5 = Md5Hash.OfFile(zipPath);

        string? seenMd5 = null;
        var fake = new FakeNexusClient((_, md5) =>
        {
            seenMd5 = md5;
            return Task.FromResult<NexusMd5Match?>(new NexusMd5Match(123, new ModMeta { Title = "Pirate Depot", Author = "IceBox" }));
        });

        var r = await Scanner.Md5IdentifyArchivesAsync(c, fake, new[] { zipPath });

        Assert.Equal(expectedArchiveMd5, seenMd5);   // hashed the ARCHIVE, not the .pak inside
        Assert.Equal(1, r.Matched);
        var meta = Scanner.LoadMetadata(c);
        Assert.Equal("Pirate Depot", meta[Variant.ParseVariant("cool").Base].Title);
    }

    // A Nexus archive-md5 match is exact provenance, so it overrides an existing CurseForge match
    // (the collision CF was winning) — Nexus identity wins, CF fills only what Nexus lacks.
    [Fact]
    public async Task Md5IdentifyArchives_overrides_an_existing_curseforge_match()
    {
        var (_, c) = Fixture("windrose");
        var zipPath = Path.Combine(TestSupport.TempDir("md5arch-"), "x.zip");
        TestSupport.WriteZip(zipPath, ("cool.pak", "PAKBYTES"));
        var key = Variant.ParseVariant("cool").Base;
        Scanner.SaveMetadata(c, new Dictionary<string, ModMeta>
        {
            [key] = new ModMeta { Title = "Cool (CF)", Url = "https://www.curseforge.com/windrose/mods/cool", Downloads = 999 },
        });
        var fake = new FakeNexusClient((_, _) => Task.FromResult<NexusMd5Match?>(
            new NexusMd5Match(285, new ModMeta { Title = "Cool", Url = "https://www.nexusmods.com/windrose/mods/285", Author = "Kingtology" })));

        await Scanner.Md5IdentifyArchivesAsync(c, fake, new[] { zipPath });

        var meta = Scanner.LoadMetadata(c);
        Assert.Equal("https://www.nexusmods.com/windrose/mods/285", meta[key].Url); // Nexus page wins
        Assert.Equal("Kingtology", meta[key].Author);                              // Nexus author wins
        Assert.Equal(999, meta[key].Downloads);                                    // CF fills what Nexus lacks
    }

    [Fact]
    public async Task Md5IdentifyArchives_returns_zero_without_a_nexus_domain()
    {
        var (_, c) = Fixture(null);
        var zipPath = Path.Combine(TestSupport.TempDir("md5arch-"), "x.zip");
        TestSupport.WriteZip(zipPath, ("cool.pak", "PAKBYTES"));
        var fake = new FakeNexusClient((_, _) =>
            Task.FromResult<NexusMd5Match?>(new NexusMd5Match(1, new ModMeta { Title = "X" })));

        var r = await Scanner.Md5IdentifyArchivesAsync(c, fake, new[] { zipPath });

        Assert.Equal(0, r.Matched);
        Assert.Empty(Scanner.LoadMetadata(c));
    }
}
