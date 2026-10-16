using ModManager.Core;

namespace ModManager.Tests;

// A Cyberpunk ArchiveXL `.xl` can stand on its own: plain `Foo.xl`, or `Foo.archive.xl` whose
// `Foo.archive` is absent. The game's declared extension is `archive`, so neither was ever listed, and a
// file the launcher cannot list is a file it cannot toggle. A `Foo.archive.xl` beside its `Foo.archive`
// is still a sidecar (A3) and still rides with it.
public class StandaloneXlTests
{
    private static (string modsDir, GameContext c) Fixture()
    {
        var root = TestSupport.TempDir("standalone-xl-");
        var gameRoot = Path.Combine(root, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot,
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "archive" }, GroupingRule = "filename_no_ext",
        });
        return (modsDir, c);
    }

    private static void Put(string dir, string name, string body) => File.WriteAllText(Path.Combine(dir, name), body);

    [Fact]
    public async Task A_plain_xl_is_its_own_row()
    {
        var (modsDir, c) = Fixture();
        Put(modsDir, "QuestTriggerFixes.xl", "XL");

        var one = Assert.Single(await Scanner.BuildModListAsync(c));

        Assert.Equal("QuestTriggerFixes", one.Name);
        Assert.True(one.Enabled);
        Assert.Equal(new[] { "QuestTriggerFixes.xl" }, one.Files);
    }

    [Fact]
    public async Task An_archive_xl_with_no_archive_is_its_own_row()
    {
        var (modsDir, c) = Fixture();
        Put(modsDir, "Cronos.archive.xl", "XL");

        var one = Assert.Single(await Scanner.BuildModListAsync(c));

        Assert.Equal("Cronos", one.Name);
        Assert.Equal(new[] { "Cronos.archive.xl" }, one.Files);
    }

    [Fact]
    public async Task A_paired_archive_xl_still_rides_with_its_archive_and_is_not_listed_separately()
    {
        var (modsDir, c) = Fixture();
        Put(modsDir, "CoolMod.archive", "ARCHIVE");
        Put(modsDir, "CoolMod.archive.xl", "XL");
        Put(modsDir, "Orphan.archive.xl", "XL2");

        var mods = (await Scanner.BuildModListAsync(c)).OrderBy(m => m.Name, StringComparer.Ordinal).ToList();

        Assert.Equal(new[] { "CoolMod", "Orphan" }, mods.Select(m => m.Name));
        Assert.Equal(new[] { "CoolMod.archive", "CoolMod.archive.xl" }, mods[0].Files.OrderBy(f => f, StringComparer.Ordinal));
        Assert.Equal(new[] { "Orphan.archive.xl" }, mods[1].Files);
    }

    [Fact]
    public async Task A_similarly_named_archive_does_not_claim_an_orphan_xl()
    {
        var (modsDir, c) = Fixture();
        Put(modsDir, "Cool.archive", "A");
        Put(modsDir, "CoolExtra.archive.xl", "XL");   // shares the "Cool" prefix, has no archive of its own

        var mods = (await Scanner.BuildModListAsync(c)).OrderBy(m => m.Name, StringComparer.Ordinal).ToList();

        Assert.Equal(new[] { "Cool", "CoolExtra" }, mods.Select(m => m.Name));
        Assert.Equal(new[] { "Cool.archive" }, mods[0].Files);
        Assert.Equal(new[] { "CoolExtra.archive.xl" }, mods[1].Files);
    }

    [Theory]
    [InlineData("Say_Something_Damn_It.xl")]
    [InlineData("Cronos.archive.xl")]
    public async Task Disable_moves_a_standalone_xl_to_holding_and_enable_restores_it_byte_identical(string file)
    {
        var (modsDir, c) = Fixture();
        var bytes = new byte[] { 0x2D, 0x2D, 0x2D, 0x0D, 0x0A, 0x00, 0xFF, 0x41 };
        File.WriteAllBytes(Path.Combine(modsDir, file), bytes);
        var name = Assert.Single(await Scanner.BuildModListAsync(c)).Name;

        await Scanner.DisableModAsync(name, c);

        Assert.False(File.Exists(Path.Combine(modsDir, file)));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(c.DisabledRoot, name, file)));
        var held = Assert.Single(await Scanner.BuildModListAsync(c));
        Assert.False(held.Enabled);
        Assert.Equal(name, held.Name);

        await Scanner.EnableModAsync(name, c);

        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(modsDir, file)));
        Assert.True(Assert.Single(await Scanner.BuildModListAsync(c)).Enabled);
    }

    [Fact]
    public async Task Uninstall_removes_a_standalone_xl()
    {
        var (modsDir, c) = Fixture();
        Put(modsDir, "dlc_liberation_protocol.xl", "XL");

        await Scanner.UninstallModAsync("dlc_liberation_protocol", c);

        Assert.False(File.Exists(Path.Combine(modsDir, "dlc_liberation_protocol.xl")));
    }

    [Fact]
    public async Task A_plain_xl_beside_a_same_named_archive_joins_that_row()
    {
        var (modsDir, c) = Fixture();
        Put(modsDir, "Foo.archive", "A");
        Put(modsDir, "Foo.xl", "XL");

        var one = Assert.Single(await Scanner.BuildModListAsync(c));

        Assert.Equal("Foo", one.Name);
        Assert.Equal(new[] { "Foo.archive", "Foo.xl" }, one.Files.OrderBy(f => f, StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_standalone_xl_follows_the_load_order_prefix_and_undo_finds_it_again()
    {
        var (modsDir, c) = Fixture();
        Put(modsDir, "Orphan.archive.xl", "XL");
        Put(modsDir, "Plain.xl", "XL2");

        await Scanner.ApplyLoadOrderAsync(c, new[] { "Plain", "Orphan" });
        var prefixed = Directory.GetFiles(modsDir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.All(prefixed, n => Assert.Contains("__", n));

        var rows = await Scanner.BuildModListAsync(c);
        Assert.Equal(new[] { "Orphan", "Plain" }, rows.Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal));

        var undo = await Scanner.ResetLoadOrderAsync(c);

        Assert.Equal(2, undo.Renamed);
        Assert.True(File.Exists(Path.Combine(modsDir, "Orphan.archive.xl")));
        Assert.True(File.Exists(Path.Combine(modsDir, "Plain.xl")));
    }

    // The archive is held in disabled/X/ and an orphan X.archive.xl is live. The live .xl must not take the
    // name "X" from the held row: it would read on, could not be turned off (the holding folder is taken),
    // and an uninstall of it would delete the held archive nobody was shown.
    [Fact]
    public async Task A_live_xl_does_not_hide_a_held_copy_with_the_same_name()
    {
        var (modsDir, c) = Fixture();
        var archive = new byte[] { 1, 2, 3, 0, 255 };
        var xl = new byte[] { 9, 8, 7, 0, 13, 10 };
        File.WriteAllBytes(Path.Combine(modsDir, "X.archive"), archive);
        await Scanner.DisableModAsync("X", c);
        File.WriteAllBytes(Path.Combine(modsDir, "X.archive.xl"), xl);

        var row = Assert.Single(await Scanner.BuildModListAsync(c));
        Assert.Equal("X", row.Name);
        Assert.False(row.Enabled);
        Assert.Equal(new[] { "X.archive" }, row.Files);

        await Scanner.EnableModAsync("X", c);

        Assert.Equal(archive, File.ReadAllBytes(Path.Combine(modsDir, "X.archive")));
        Assert.Equal(xl, File.ReadAllBytes(Path.Combine(modsDir, "X.archive.xl")));
        var after = Assert.Single(await Scanner.BuildModListAsync(c));
        Assert.True(after.Enabled);
        Assert.Equal(new[] { "X.archive", "X.archive.xl" }, after.Files.OrderBy(f => f, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Uninstalling_while_a_same_named_copy_is_held_deletes_only_what_the_row_shows()
    {
        var (modsDir, c) = Fixture();
        File.WriteAllText(Path.Combine(modsDir, "X.archive"), "A");
        await Scanner.DisableModAsync("X", c);
        File.WriteAllText(Path.Combine(modsDir, "X.archive.xl"), "XL");

        var row = Assert.Single(await Scanner.BuildModListAsync(c));
        Assert.False(row.Enabled);   // the row IS the held copy; the live .xl is not a second thing to delete

        // The live .xl is untouched by anything the row offers until the pair is reunited.
        Assert.True(File.Exists(Path.Combine(modsDir, "X.archive.xl")));
        Assert.True(File.Exists(Path.Combine(c.DisabledRoot, "X", "X.archive")));

        // Uninstalling that row removes the held copy it shows, and nothing it does not: the live .xl stays.
        await Scanner.UninstallModAsync("X", c);
        Assert.False(Directory.Exists(Path.Combine(c.DisabledRoot, "X")));
        Assert.True(File.Exists(Path.Combine(modsDir, "X.archive.xl")));
    }

    [Fact]
    public async Task A_game_that_does_not_list_archive_gets_no_xl_rows()
    {
        var root = TestSupport.TempDir("standalone-xl-pak-");
        var modsDir = Path.Combine(root, "game", "mods");
        Directory.CreateDirectory(modsDir);
        var c = Scanner.GameContext(new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = Path.Combine(root, "game"),
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        });
        Put(modsDir, "notes.xl", "XL");

        Assert.Empty(await Scanner.BuildModListAsync(c));
    }
}
