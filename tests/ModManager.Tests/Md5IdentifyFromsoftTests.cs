using ModManager.Core;

namespace ModManager.Tests;

// Md5IdentifyArchives — fromsoft branch. c.Exts is empty for fromsoft so ZipModKeys returns
// nothing; the new branch uses DirectInject.MatchSignaturesInZip to recover catalog-named keys
// like "Seamless Co-op" or "ReShade". Pin that the branch fires + the right keys land.
public class Md5IdentifyFromsoftTests
{
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

    // A fromsoft GameContext mirrors the EnginePresets["fromsoft"] shape: Exts is empty (mods are
    // catalog-named, not extension-based) and NexusGameDomain is set so the md5 path runs.
    private static GameContext Fixture(string nexusDomain = "eldenring")
    {
        var root = TestSupport.TempDir("md5fs-");
        var gameRoot = Path.Combine(root, "game");
        Directory.CreateDirectory(gameRoot);
        return Scanner.GameContext(new GameEntry
        {
            Id = "elden", GameName = "Elden Ring", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mod", "mod", "Game/mod") },
            FileExtensions = Array.Empty<string>(),   // <-- fromsoft signature: empty Exts
            NexusGameDomain = nexusDomain,
        });
    }

    [Fact]
    public async Task Fromsoft_archive_matches_catalog_signature_and_attaches_metadata()
    {
        var c = Fixture();
        // A Seamless Co-op archive layout (matches DirectInject.Catalog files for "Seamless Co-op").
        var zipPath = Path.Combine(TestSupport.TempDir("md5fs-arch-"), "SeamlessCoOp-1.zip");
        TestSupport.WriteZip(zipPath,
            ("ersc.dll", "fakedll"),
            ("ersc_settings.ini", "[settings]"),
            ("launch_elden_ring_seamlesscoop.exe", "fakeexe"));

        var fake = new FakeNexusClient((_, _) =>
            Task.FromResult<NexusMd5Match?>(new NexusMd5Match(510, new ModMeta { Title = "Seamless Co-op (Nexus)", Author = "LukeYui" })));

        var r = await Scanner.Md5IdentifyArchivesAsync(c, fake, new[] { zipPath });

        Assert.Equal(1, r.Matched);
        var meta = Scanner.LoadMetadata(c);
        Assert.True(meta.ContainsKey("Seamless Co-op"),
            $"expected DI catalog key 'Seamless Co-op'; got: [{string.Join(", ", meta.Keys)}]");
        Assert.Equal("Seamless Co-op (Nexus)", meta["Seamless Co-op"].Title);
        Assert.Equal("LukeYui", meta["Seamless Co-op"].Author);
    }

    [Fact]
    public async Task Fromsoft_archive_with_no_catalog_match_returns_zero()
    {
        var c = Fixture();
        var zipPath = Path.Combine(TestSupport.TempDir("md5fs-noop-"), "random.zip");
        TestSupport.WriteZip(zipPath, ("readme.txt", "hi"), ("screenshot.png", "x"));

        var fake = new FakeNexusClient((_, _) =>
            Task.FromResult<NexusMd5Match?>(new NexusMd5Match(1, new ModMeta { Title = "Whatever" })));

        var r = await Scanner.Md5IdentifyArchivesAsync(c, fake, new[] { zipPath });

        Assert.Equal(0, r.Matched);
        Assert.Empty(Scanner.LoadMetadata(c));
    }

    [Fact]
    public async Task Fromsoft_archive_with_multiple_catalog_matches_attaches_to_each()
    {
        var c = Fixture();
        // An archive that bundles BOTH Seamless Co-op AND a Modded regulation.bin (rare but
        // possible: some mod packs do this). The Nexus md5 maps to one Nexus mod, but the archive
        // installs multiple catalog mods. The metadata gets attached to every catalog name.
        var zipPath = Path.Combine(TestSupport.TempDir("md5fs-multi-"), "combo.zip");
        TestSupport.WriteZip(zipPath,
            ("ersc.dll", "x"),
            ("ersc_settings.ini", "x"),
            ("regulation.bin", "x"));

        var fake = new FakeNexusClient((_, _) =>
            Task.FromResult<NexusMd5Match?>(new NexusMd5Match(999, new ModMeta { Title = "Combo Pack" })));

        var r = await Scanner.Md5IdentifyArchivesAsync(c, fake, new[] { zipPath });

        Assert.Equal(2, r.Matched); // 2 catalog keys matched
        var meta = Scanner.LoadMetadata(c);
        Assert.Contains("Seamless Co-op", meta.Keys);
        Assert.Contains("Modded regulation.bin", meta.Keys);
    }
}
