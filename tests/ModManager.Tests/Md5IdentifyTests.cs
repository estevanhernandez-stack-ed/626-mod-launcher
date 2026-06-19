using ModManager.Core;
using ModManager.Plugins.Abstractions;

namespace ModManager.Tests;

// Md5IdentifyAsync — the md5 twin of FingerprintIdentifyAsync. Hash each just-dropped file,
// ask the mod-source plugin which mod it is by md5, merge that metadata (curated/CF wins, source fills gaps).
public class Md5IdentifyTests
{
    // A hand-rolled fake IModSource — only IdentifyByHashAsync is exercised here. The func returns the
    // SourceIdentifyResult (ref + full metadata) the grown contract carries.
    private sealed class FakeModSource : IModSource
    {
        private readonly Func<string, string, Task<SourceIdentifyResult?>> _byMd5;
        public FakeModSource(Func<string, string, Task<SourceIdentifyResult?>> byMd5) => _byMd5 = byMd5;
        public string Id => "nexus";
        public bool RequiresApiKey => true;
        public Task<SourceIdentifyResult?> IdentifyByHashAsync(string gameDomain, string md5) => _byMd5(gameDomain, md5);
        public Task<SourceModMetadata?> FetchMetadataAsync(SourceModRef modRef) => throw new NotSupportedException();
        public Task<bool> IsUpdateAvailableAsync(SourceModRef modRef, string installedVersion) => throw new NotSupportedException();
        public Task<EndorseResult> SetEndorsedAsync(SourceModRef modRef, bool endorsed) => throw new NotSupportedException();
        public Task<IReadOnlyList<SourceEndorsement>> GetUserEndorsementsAsync() => throw new NotSupportedException();
        public Task<IReadOnlyList<SourceUpdateEntry>> GetRecentlyUpdatedAsync(string gameDomain, string period) => throw new NotSupportedException();
    }

    // Convenience: build an identify hit from a modId + the identity fields the assertions check.
    private static SourceIdentifyResult Hit(string domain, int modId,
        string? title = null, string? author = null, string? url = null, long? downloads = null)
        => new(new SourceModRef("nexus", domain, modId, ""),
               new SourceModMetadata(null, downloads, null, null, null, Title: title, Author: author, ModUrl: url));

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
        var fake = new FakeModSource((d, _) =>
            Task.FromResult<SourceIdentifyResult?>(Hit(d, 123, title: "Pirate Depot", author: "someone")));

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
        var fake = new FakeModSource((d, _) =>
            Task.FromResult<SourceIdentifyResult?>(Hit(d, 123, title: "Pirate Depot")));

        var r = await Scanner.Md5IdentifyAsync(c, fake, new[] { "cool.pak" });

        Assert.Equal(0, r.Matched);
        Assert.Empty(Scanner.LoadMetadata(c));
    }

    [Fact]
    public async Task Md5Identify_returns_zero_when_no_match()
    {
        var (modsDir, c) = Fixture("windrose");
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "COOLBYTES");
        var fake = new FakeModSource((_, _) => Task.FromResult<SourceIdentifyResult?>(null));

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
        var fake = new FakeModSource((d, md5) =>
        {
            seenMd5 = md5;
            return Task.FromResult<SourceIdentifyResult?>(Hit(d, 123, title: "Pirate Depot", author: "IceBox"));
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
        var fake = new FakeModSource((d, _) => Task.FromResult<SourceIdentifyResult?>(
            Hit(d, 285, title: "Cool", url: "https://www.nexusmods.com/windrose/mods/285", author: "Kingtology")));

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
        var fake = new FakeModSource((d, _) =>
            Task.FromResult<SourceIdentifyResult?>(Hit(d, 1, title: "X")));

        var r = await Scanner.Md5IdentifyArchivesAsync(c, fake, new[] { zipPath });

        Assert.Equal(0, r.Matched);
        Assert.Empty(Scanner.LoadMetadata(c));
    }
}
