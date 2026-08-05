using ModManager.Core;

namespace ModManager.Tests;

// Scanner.ArchiveModKeysFor — the read-only seam onto the same archive-contents -> mod-key
// derivation Md5IdentifyArchivesAsync uses internally (ZipModKeys for extension engines,
// DirectInject.MatchSignaturesInZip for catalog engines), exposed so an App-layer caller can
// resolve the real write key WITHOUT hashing/identifying/saving — the discovery-adoption review
// path needs the keys before it knows whether the user approves the write.
public class ArchiveModKeysForTests
{
    [Fact]
    public void Extension_engine_derives_the_grouped_key_from_the_archives_pak_entry()
    {
        var root = TestSupport.TempDir("archkeys-ext-");
        var gameRoot = Path.Combine(root, "game");
        Directory.CreateDirectory(gameRoot);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" },
            GroupingRule = "strip_underscore_p_suffix", // UE-pak convention: Foo_P.pak -> "Foo"
        });
        var zipPath = Path.Combine(TestSupport.TempDir("archkeys-ext-zip-"), "Foo-1-0.zip");
        TestSupport.WriteZip(zipPath, ("Foo_P.pak", "PAKBYTES"), ("readme.txt", "hi"));

        var keys = Scanner.ArchiveModKeysFor(zipPath, c);

        Assert.Equal(new[] { "Foo" }, keys);
    }

    [Fact]
    public void Catalog_engine_derives_keys_via_directinject_signature_matching()
    {
        var root = TestSupport.TempDir("archkeys-cat-");
        var gameRoot = Path.Combine(root, "game");
        Directory.CreateDirectory(gameRoot);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "elden", GameName = "Elden Ring", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mod", "mod", "Game/mod") },
            FileExtensions = Array.Empty<string>(), // fromsoft signature: catalog-named, not extension-based
        });
        // A Seamless Co-op archive layout (matches DirectInject.Catalog files for "Seamless Co-op") —
        // same fixture Md5IdentifyFromsoftTests uses for the identical signature-match branch.
        var zipPath = Path.Combine(TestSupport.TempDir("archkeys-cat-zip-"), "SeamlessCoOp-1.zip");
        TestSupport.WriteZip(zipPath,
            ("ersc.dll", "fakedll"),
            ("ersc_settings.ini", "[settings]"),
            ("launch_elden_ring_seamlesscoop.exe", "fakeexe"));

        var keys = Scanner.ArchiveModKeysFor(zipPath, c);

        Assert.Contains("Seamless Co-op", keys);
    }

    [Fact]
    public void Missing_archive_returns_empty_never_throws()
    {
        var root = TestSupport.TempDir("archkeys-missing-");
        var gameRoot = Path.Combine(root, "game");
        Directory.CreateDirectory(gameRoot);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" },
        });

        var keys = Scanner.ArchiveModKeysFor(Path.Combine(root, "does-not-exist.zip"), c);

        Assert.Empty(keys);
    }

    [Fact]
    public void Corrupt_archive_returns_empty_never_throws()
    {
        var root = TestSupport.TempDir("archkeys-corrupt-");
        var gameRoot = Path.Combine(root, "game");
        Directory.CreateDirectory(gameRoot);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" },
        });
        var badZip = Path.Combine(root, "not-a-zip.zip");
        File.WriteAllText(badZip, "this is not a zip archive");

        var keys = Scanner.ArchiveModKeysFor(badZip, c);

        Assert.Empty(keys);
    }

    [Fact]
    public void Archive_whose_contents_match_nothing_known_returns_empty()
    {
        var root = TestSupport.TempDir("archkeys-noop-");
        var gameRoot = Path.Combine(root, "game");
        Directory.CreateDirectory(gameRoot);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "elden", GameName = "Elden Ring", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mod", "mod", "Game/mod") },
            FileExtensions = Array.Empty<string>(),
        });
        var zipPath = Path.Combine(TestSupport.TempDir("archkeys-noop-zip-"), "random.zip");
        TestSupport.WriteZip(zipPath, ("readme.txt", "hi"), ("screenshot.png", "x"));

        var keys = Scanner.ArchiveModKeysFor(zipPath, c);

        Assert.Empty(keys);
    }
}
