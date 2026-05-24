using ModManager.Core;

namespace ModManager.Tests;

// Ports scanner-intake.test.js + intake-folder.test.js — no-overwrite intake, zip extraction,
// and recursive folder import.
public class ScannerIntakeTests
{
    private static (string root, string modsDir, string src, GameContext c) Fixture(string[]? exts = null)
    {
        var root = TestSupport.TempDir("intake-");
        var gameRoot = Path.Combine(root, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(modsDir);
        Directory.CreateDirectory(src);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = exts ?? new[] { "pak" },
        });
        return (root, modsDir, src, c);
    }

    [Fact]
    public async Task AddMods_places_a_new_mod_file()
    {
        var (_, modsDir, src, c) = Fixture();
        var srcFile = Path.Combine(src, "cool.pak");
        File.WriteAllText(srcFile, "NEW");

        var r = await Scanner.AddModsAsync(new[] { srcFile }, c);

        Assert.Equal("NEW", TestSupport.Read(Path.Combine(modsDir, "cool.pak")));
        Assert.Contains("cool.pak", r.Added);
    }

    [Fact]
    public async Task AddMods_does_not_overwrite_existing_and_reports_skipped()
    {
        var (_, modsDir, src, c) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "ORIGINAL");
        var srcFile = Path.Combine(src, "cool.pak");
        File.WriteAllText(srcFile, "NEW");

        var r = await Scanner.AddModsAsync(new[] { srcFile }, c);

        Assert.Equal("ORIGINAL", TestSupport.Read(Path.Combine(modsDir, "cool.pak")));
        Assert.DoesNotContain("cool.pak", r.Added);
        Assert.Contains(r.Skipped, s => s.Name == "cool.pak");
    }

    [Fact]
    public async Task Zip_intake_extracts_new_mods_skips_existing()
    {
        var (_, modsDir, src, c) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "ORIGINAL");

        var zipPath = Path.Combine(src, "pack.zip");
        TestSupport.WriteZip(zipPath, ("cool.pak", "ZIPVERSION"), ("fresh.pak", "ZIPFRESH"));

        var r = await Scanner.AddModsAsync(new[] { zipPath }, c);

        Assert.Equal("ORIGINAL", TestSupport.Read(Path.Combine(modsDir, "cool.pak")));
        Assert.Equal("ZIPFRESH", TestSupport.Read(Path.Combine(modsDir, "fresh.pak")));
        Assert.Contains("fresh.pak", r.Added);
        Assert.Contains(r.Skipped, s => s.Name == "cool.pak");
    }

    [Fact]
    public async Task AddMods_imports_from_a_folder_recursively_ignoring_non_mods()
    {
        var (root, modsDir, _, c) = Fixture();
        var src = Path.Combine(root, "download");
        Directory.CreateDirectory(Path.Combine(src, "nested"));
        File.WriteAllText(Path.Combine(src, "a.pak"), "A");
        File.WriteAllText(Path.Combine(src, "readme.txt"), "hi");
        File.WriteAllText(Path.Combine(src, "nested", "b.pak"), "B");

        var r = await Scanner.AddModsAsync(new[] { src }, c);

        Assert.Equal("A", TestSupport.Read(Path.Combine(modsDir, "a.pak")));
        Assert.Equal("B", TestSupport.Read(Path.Combine(modsDir, "b.pak")));
        Assert.Contains("a.pak", r.Added);
        Assert.Contains("b.pak", r.Added);
        Assert.DoesNotContain(r.Skipped, s => s.Name == "readme.txt");
    }

    [Fact]
    public async Task AddMods_reports_directly_chosen_non_mod_as_skipped()
    {
        var (root, _, _, c) = Fixture();
        var txt = Path.Combine(root, "notes.txt");
        File.WriteAllText(txt, "x");
        var r = await Scanner.AddModsAsync(new[] { txt }, c);
        Assert.Contains(r.Skipped, s => s.Name == "notes.txt");
    }
}
