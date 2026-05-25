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

    // Two-location fixture: one Vortex-owned location + one plain toggleable location.
    private static (string ownedDir, string normalDir, GameContext c) TwoLocationFixture()
    {
        var root = TestSupport.TempDir("profiles-owned-");
        var gameRoot = Path.Combine(root, "game");

        var ownedDir = Path.Combine(gameRoot, "ownedMods");
        Directory.CreateDirectory(ownedDir);
        File.WriteAllText(Path.Combine(ownedDir, "vortex.deployment.x.json"), "{}"); // ownership marker
        File.WriteAllText(Path.Combine(ownedDir, "vortex_mod.pak"), "OWNED");

        var normalDir = Path.Combine(gameRoot, "normalMods");
        Directory.CreateDirectory(normalDir);
        File.WriteAllText(Path.Combine(normalDir, "my_mod.pak"), "MINE");

        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[]
            {
                new ModLocation("owned", "Owned", "ownedMods"),
                new ModLocation("normal", "Normal", "normalMods"),
            },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        });
        return (ownedDir, normalDir, c);
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

    /// <summary>
    /// LoadProfile must NOT move files out of a Vortex-owned folder even when the saved profile
    /// records that mod as disabled. The owned mod's pak stays in loc.Abs; the disabled holding
    /// folder must not contain it.
    /// </summary>
    [Fact]
    public async Task LoadProfile_does_not_disable_an_owned_mod()
    {
        var (ownedDir, normalDir, c) = TwoLocationFixture();

        // Save a profile AFTER manually (unsafely, for test setup) writing a profile that records
        // the owned mod as disabled. We build the profile file directly so we don't depend on a
        // snapshot of enabled state — we want to simulate a profile recorded when the owned folder
        // wasn't yet claimed by Vortex.
        var profilePath = Path.Combine(c.ProfilesDir, "test.json");
        Directory.CreateDirectory(c.ProfilesDir);
        var profileJson = """
            {
              "savedAt": "2024-01-01T00:00:00Z",
              "game": "T",
              "mods": [
                { "name": "vortex_mod", "enabled": false },
                { "name": "my_mod",    "enabled": true  }
              ]
            }
            """;
        File.WriteAllText(profilePath, profileJson);

        // Load the profile — the owned mod must NOT be touched (its file must stay in ownedDir).
        await Scanner.LoadProfileAsync("test", c);

        // Owned mod's pak must still be in its original location.
        Assert.True(File.Exists(Path.Combine(ownedDir, "vortex_mod.pak")),
            "LoadProfile must not move a ReadOnly (Vortex-owned) mod's file out of its folder.");

        // The disabled holding folder must not contain the owned mod.
        var holdingFolder = Path.Combine(c.DisabledRoot, "vortex_mod");
        Assert.False(Directory.Exists(holdingFolder) &&
                     Directory.GetFileSystemEntries(holdingFolder).Any(e => Path.GetFileName(e) != "meta.json"),
            "LoadProfile must not create a holding folder for an owned mod.");

        // The non-owned mod should still be enabled (profile says enabled: true, it was already enabled).
        Assert.True(File.Exists(Path.Combine(normalDir, "my_mod.pak")),
            "LoadProfile must leave the non-owned mod in place when the profile says enabled.");
    }
}
