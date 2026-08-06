using ModManager.Core;

namespace ModManager.Tests;

// Ports scanner.test.js — UE5-style game: a mirrored mod (Cool) + a client-only mod (Audio).
public class ScannerCoreTests
{
    internal static (string root, string primary, string mirror, GameContext c) SetupPublic() => Setup();

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

    [Fact]
    public void ListClassified_sets_class_without_writing_classification()
    {
        var (_, _, _, c) = Setup();
        var mods = Scanner.ListClassified(c);
        Assert.Equal("both", mods.First(m => m.Name == "Cool").Class); // mirrored -> both
        Assert.Equal("sp", mods.First(m => m.Name == "Audio").Class);  // client-only -> sp
        Assert.False(File.Exists(c.ClassificationPath)); // read-only: no write
    }

    [Fact]
    public void PersistClassification_writes_the_seeded_map()
    {
        var (_, _, _, c) = Setup();
        Scanner.PersistClassification(c, Scanner.ListClassified(c));
        Assert.True(File.Exists(c.ClassificationPath));
        var written = Scanner.LoadClassification(c); // content is the seeded map, not just "a file"
        Assert.Equal("both", written["Cool"]);
        Assert.Equal("sp", written["Audio"]);
    }

    // ---- File extensions are normalised before they become a regex ----
    // Live: Marvel's Spider-Man 2 was registered from the Add Game dialog with the extensions typed
    // the way a human writes them — ".smpcmod", ".suit" — and those went straight into the file
    // regex. "\.(.smpcmod)$" needs a dot, then ANY character, then "smpcmod", so a real file
    // called Foo.smpcmod does not match and every mod for that game is invisible. Same silent
    // failure as a wrong extension list, from a leading dot the user had no reason to omit.

    private static GameContext WithExts(params string[] exts)
    {
        var root = TestSupport.TempDir("exts-");
        var mods = Path.Combine(root, "mods");
        Directory.CreateDirectory(mods);
        return Scanner.GameContext(new GameEntry
        {
            Id = "x", GameName = "X", GameRoot = root,
            DataDir = Path.Combine(root, "_626mods", "x"),
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = exts, GroupingRule = "filename_no_ext",
        });
    }

    [Theory]
    [InlineData(".smpcmod", "Hero.smpcmod")]
    [InlineData("smpcmod", "Hero.smpcmod")]      // already-clean form must keep working
    [InlineData(".pak", "Cool.pak")]
    public async Task A_leading_dot_on_an_extension_does_not_hide_every_mod(string ext, string fileName)
    {
        var c = WithExts(ext);
        File.WriteAllText(Path.Combine(c.GameRoot, "mods", fileName), "x");

        var mods = await Scanner.BuildModListAsync(c);

        Assert.Single(mods);
    }

    // The extension is interpolated into a regex, so any metacharacter in it must be inert data.
    // A "+" or "(" from a hand-typed entry would otherwise throw at context-build time and take the
    // whole game with it.
    [Fact]
    public async Task A_regex_metacharacter_in_an_extension_is_treated_as_text()
    {
        var c = WithExts("c++mod");
        File.WriteAllText(Path.Combine(c.GameRoot, "mods", "Thing.c++mod"), "x");

        var mods = await Scanner.BuildModListAsync(c);

        Assert.Single(mods);
    }

    // The wildcard was the actual mechanism of the live bug: prove the dot is literal now.
    [Fact]
    public async Task The_dot_in_an_extension_is_literal_not_a_wildcard()
    {
        var c = WithExts(".suit");
        File.WriteAllText(Path.Combine(c.GameRoot, "mods", "Real.suit"), "x");
        File.WriteAllText(Path.Combine(c.GameRoot, "mods", "Fake.Xsuit"), "x"); // matched by the buggy pattern

        var mods = await Scanner.BuildModListAsync(c);

        var one = Assert.Single(mods);
        Assert.Equal("Real", one.Name);
    }
}
