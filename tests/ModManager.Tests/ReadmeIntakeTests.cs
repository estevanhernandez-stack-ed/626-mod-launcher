using ModManager.Core;

namespace ModManager.Tests;

// Capturing a mod's readme at intake: when a dropped zip carries a README, it's cached under each
// mod key the zip adds, so the viewer can surface it later. Derived cache — best-effort, never
// breaks intake. ReadmePathFor resolves a mod's cached readme (or null).
public class ReadmeIntakeTests
{
    private static (string root, string modsDir, string src, GameContext c) Fixture()
    {
        var root = TestSupport.TempDir("readme-intake-");
        var gameRoot = Path.Combine(root, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(modsDir);
        Directory.CreateDirectory(src);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" },
        });
        return (root, modsDir, src, c);
    }

    [Fact]
    public async Task AddMods_caches_a_zips_readme_for_the_mod_key()
    {
        var (_, _, src, c) = Fixture();
        var zip = Path.Combine(src, "pack.zip");
        TestSupport.WriteZip(zip, ("cool.pak", "PAK"), ("README.md", "# Cool Mod\nInstall notes"));

        await Scanner.AddModsAsync(new[] { zip }, c);

        var readme = Scanner.ReadmePathFor("cool", c);
        Assert.NotNull(readme);
        Assert.Equal("# Cool Mod\nInstall notes", TestSupport.Read(readme!));
        Assert.EndsWith(".md", readme);
    }

    [Fact]
    public async Task ExecuteIntake_caches_a_zips_readme_for_each_added_mod()
    {
        var (_, _, src, c) = Fixture();
        var zip = Path.Combine(src, "pack.zip");
        TestSupport.WriteZip(zip, ("a.pak", "A"), ("b.pak", "B"), ("readme.txt", "shared notes"));

        var plan = Scanner.PlanIntake(new[] { zip }, c);
        Scanner.ExecuteIntake(plan, new HashSet<string>(), c);

        Assert.Equal("shared notes", TestSupport.Read(Scanner.ReadmePathFor("a", c)!));
        Assert.Equal("shared notes", TestSupport.Read(Scanner.ReadmePathFor("b", c)!));
    }

    [Fact]
    public async Task No_readme_in_zip_caches_nothing()
    {
        var (_, _, src, c) = Fixture();
        var zip = Path.Combine(src, "pack.zip");
        TestSupport.WriteZip(zip, ("cool.pak", "PAK"));

        await Scanner.AddModsAsync(new[] { zip }, c);

        Assert.Null(Scanner.ReadmePathFor("cool", c));
    }

    [Fact]
    public void ReadmePathFor_is_null_when_nothing_cached()
    {
        var (_, _, _, c) = Fixture();
        Assert.Null(Scanner.ReadmePathFor("anything", c));
    }

    [Fact]
    public async Task A_zip_slip_entry_name_cannot_escape_the_readme_cache_dir()
    {
        var (_, _, src, c) = Fixture();
        var zip = Path.Combine(src, "evil.zip");
        // A traversal-laden mod entry name + a readme: the key derives from the basename only, and
        // the cache write is guarded — nothing may land outside <dataDir>\readmes.
        TestSupport.WriteZip(zip, ("../../../../pwned.pak", "PAK"), ("README.md", "x"));

        await Scanner.AddModsAsync(new[] { zip }, c);

        var cached = Scanner.ReadmePathFor("pwned", c);
        Assert.NotNull(cached);
        var readmesDir = Path.GetFullPath(Path.Combine(c.DataDir, "readmes"));
        Assert.Equal(readmesDir, Path.GetFullPath(Path.GetDirectoryName(cached!)!)); // stayed in-bounds
        Assert.False(File.Exists(Path.GetFullPath(Path.Combine(c.DataDir, "..", "pwned.md"))));
    }
}
