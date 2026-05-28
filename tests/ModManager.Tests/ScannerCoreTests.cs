using ModManager.Core;

namespace ModManager.Tests;

// Ports scanner.test.js — UE5-style game: a mirrored mod (Cool) + a client-only mod (Audio).
public class ScannerCoreTests
{
    private static (string root, string primary, string mirror, GameContext c) Setup()
    {
        var root = TestSupport.TempDir("mmb-");
        var primary = Path.Combine(root, "Paks", "~mods");
        var mirror = Path.Combine(root, "Server", "~mods");
        Directory.CreateDirectory(primary);
        Directory.CreateDirectory(mirror);
        foreach (var f in new[] { "Cool_P.pak", "Cool_P.ucas", "Cool_P.utoc" }) File.WriteAllText(Path.Combine(primary, f), "x");
        File.WriteAllText(Path.Combine(mirror, "Cool_P.pak"), "x"); // mirrored -> onServer
        File.WriteAllText(Path.Combine(primary, "Audio_P.pak"), "x"); // client-only
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = root,
            DataDir = Path.Combine(root, "_626mods", "t"), // pin to unique root — avoids %TEMP%\_626mods\t collision under parallel test runs
            ModLocations = new[] { new ModLocation("mods", "~mods", "Paks/~mods") { Mirrors = new[] { "Server/~mods" } } },
            FileExtensions = new[] { "pak", "ucas", "utoc" }, GroupingRule = "strip_underscore_p_suffix",
        });
        return (root, primary, mirror, c);
    }

    [Fact]
    public async Task BuildModList_groups_by_P_suffix_and_detects_onServer()
    {
        var (_, _, _, c) = Setup();
        var mods = await Scanner.BuildModListAsync(c);
        var cool = mods.First(m => m.Name == "Cool");
        var audio = mods.First(m => m.Name == "Audio");
        Assert.Equal(3, cool.Files.Count);
        Assert.True(cool.OnServer);
        Assert.False(audio.OnServer);
    }

    [Fact]
    public async Task ListWithClass_auto_seeds_mirrored_both_clientonly_sp()
    {
        var (_, _, _, c) = Setup();
        var mods = await Scanner.ListWithClassAsync(c);
        Assert.Equal("both", mods.First(m => m.Name == "Cool").Class);
        Assert.Equal("sp", mods.First(m => m.Name == "Audio").Class);
        Assert.True(File.Exists(c.ClassificationPath));
    }

    [Fact]
    public async Task DisableEnable_round_trip_preserves_client_only()
    {
        var (_, primary, mirror, c) = Setup();
        await Scanner.DisableModAsync("Audio", c);
        Assert.False(File.Exists(Path.Combine(primary, "Audio_P.pak")));
        Assert.True(File.Exists(Path.Combine(c.DisabledRoot, "Audio", "Audio_P.pak")));
        await Scanner.EnableModAsync("Audio", c);
        Assert.True(File.Exists(Path.Combine(primary, "Audio_P.pak")));
        Assert.False(File.Exists(Path.Combine(mirror, "Audio_P.pak"))); // client-only stays client-only
        Assert.False(Directory.Exists(Path.Combine(c.DisabledRoot, "Audio")));
    }

    [Fact]
    public async Task DisableEnable_round_trip_restores_mirrored_to_both_sides()
    {
        var (_, primary, mirror, c) = Setup();
        await Scanner.DisableModAsync("Cool", c);
        Assert.False(File.Exists(Path.Combine(primary, "Cool_P.pak")));
        Assert.False(File.Exists(Path.Combine(mirror, "Cool_P.pak")));
        await Scanner.EnableModAsync("Cool", c);
        Assert.True(File.Exists(Path.Combine(primary, "Cool_P.pak")));
        Assert.True(File.Exists(Path.Combine(mirror, "Cool_P.pak"))); // restored to server
    }

    [Fact]
    public async Task AddMods_places_loose_file_into_primary_and_mirror()
    {
        var (root, primary, mirror, c) = Setup();
        var src = Path.Combine(root, "incoming_P.pak");
        File.WriteAllText(src, "x");
        var r = await Scanner.AddModsAsync(new[] { src }, c);
        Assert.Equal(new[] { "incoming_P.pak" }, r.Added.ToArray());
        Assert.True(File.Exists(Path.Combine(primary, "incoming_P.pak")));
        Assert.True(File.Exists(Path.Combine(mirror, "incoming_P.pak")));
    }

    [Fact]
    public async Task AddMods_extracts_mod_entries_from_zip_skips_non_mods()
    {
        var (root, primary, _, c) = Setup();
        var zpath = Path.Combine(root, "pack.zip");
        TestSupport.WriteZip(zpath, ("Pack_P.pak", "x"), ("readme.txt", "hi"));
        var r = await Scanner.AddModsAsync(new[] { zpath }, c);
        Assert.Contains("Pack_P.pak", r.Added);
        Assert.True(File.Exists(Path.Combine(primary, "Pack_P.pak")));
        Assert.False(File.Exists(Path.Combine(primary, "readme.txt")));
    }

    [Fact]
    public async Task Profiles_capture_and_reapply_toggle_state()
    {
        var (_, primary, _, c) = Setup();
        await Scanner.SaveProfileAsync("all-on", c);
        await Scanner.DisableModAsync("Audio", c);
        Assert.False(File.Exists(Path.Combine(primary, "Audio_P.pak")));
        await Scanner.LoadProfileAsync("all-on", c);
        Assert.True(File.Exists(Path.Combine(primary, "Audio_P.pak"))); // re-enabled by profile
    }
}
