using ModManager.Core;

namespace ModManager.Tests;

// Ports scanner-profiles.test.js — profile round trip + traversal-name rejection.
public class ScannerProfilesTests
{
    private static (string root, GameContext c) Fixture()
    {
        var root = TestSupport.TempDir("profiles-");
        var gameRoot = Path.Combine(root, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "a.pak"), "A");
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        });
        return (root, c);
    }

    private static async Task<bool> Enabled(GameContext c, string name)
        => (await Scanner.BuildModListAsync(c)).First(m => m.Name == name).Enabled;

    [Fact]
    public async Task Profile_round_trip_save_list_load_delete()
    {
        var (_, c) = Fixture();
        await Scanner.SaveProfileAsync("Hardcore", c);
        Assert.Equal(new[] { "Hardcore" }, (await Scanner.ListProfilesAsync(c)).ToArray());

        await Scanner.DisableModAsync("a", c);
        Assert.False(await Enabled(c, "a"));

        await Scanner.LoadProfileAsync("Hardcore", c);
        Assert.True(await Enabled(c, "a"));

        await Scanner.DeleteProfileAsync("Hardcore", c);
        Assert.Empty(await Scanner.ListProfilesAsync(c));
    }

    [Fact]
    public async Task SaveProfile_rejects_a_traversal_name()
    {
        var (root, c) = Fixture();
        var escapeTarget = Path.Combine(root, "pwned.json");
        await Assert.ThrowsAnyAsync<Exception>(() => Scanner.SaveProfileAsync("../../../pwned", c));
        Assert.False(File.Exists(escapeTarget));
    }

    [Fact]
    public async Task DeleteProfile_rejects_a_traversal_name()
    {
        var (root, c) = Fixture();
        var victim = Path.Combine(root, "keep.json");
        File.WriteAllText(victim, "IMPORTANT");
        await Assert.ThrowsAnyAsync<Exception>(() => Scanner.DeleteProfileAsync("../../../keep", c));
        Assert.Equal("IMPORTANT", TestSupport.Read(victim));
    }
}
