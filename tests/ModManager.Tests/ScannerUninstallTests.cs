using ModManager.Core;

namespace ModManager.Tests;

// Gated uninstall — the one place the launcher permanently deletes mod files. The GATE is the
// UI confirm; the Core does the delete only when explicitly called. Removes live files from
// every location + mirror AND any disabled holding folder. Idempotent.
public class ScannerUninstallTests
{
    private static (string primary, string mirror, GameContext c) Setup()
    {
        var root = TestSupport.TempDir("uninstall-");
        var primary = Path.Combine(root, "mods");
        var mirror = Path.Combine(root, "server");
        Directory.CreateDirectory(primary);
        Directory.CreateDirectory(mirror);
        File.WriteAllText(Path.Combine(primary, "cool.pak"), "X");
        File.WriteAllText(Path.Combine(mirror, "cool.pak"), "X");
        var c = Scanner.GameContext(new GameEntry
        {
            // Pin DataDir under the unique temp root. Without this, DataDirForGame resolves to the
            // SHARED %TEMP%\_626mods\t (parent-of-gameRoot + Id), and parallel Scanner tests using
            // Id="t" race on disabled/cool — an intermittent flake. Mirrors the ScannerCoreTests fix.
            Id = "t", GameName = "T", GameRoot = root, DataDir = Path.Combine(root, "_626mods", "t"),
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") { Mirrors = new[] { "server" } } },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
        });
        return (primary, mirror, c);
    }

    [Fact]
    public async Task Uninstall_removes_live_mod_files_from_all_locations()
    {
        var (primary, mirror, c) = Setup();
        await Scanner.UninstallModAsync("cool", c);
        Assert.False(File.Exists(Path.Combine(primary, "cool.pak")));
        Assert.False(File.Exists(Path.Combine(mirror, "cool.pak")));
        Assert.DoesNotContain(await Scanner.BuildModListAsync(c), m => m.Name == "cool");
    }

    [Fact]
    public async Task Uninstall_removes_a_disabled_mods_holding_folder()
    {
        var (_, _, c) = Setup();
        await Scanner.DisableModAsync("cool", c);
        Assert.True(Directory.Exists(Path.Combine(c.DisabledRoot, "cool")));

        await Scanner.UninstallModAsync("cool", c);

        Assert.False(Directory.Exists(Path.Combine(c.DisabledRoot, "cool")));
        Assert.DoesNotContain(await Scanner.BuildModListAsync(c), m => m.Name == "cool");
    }

    [Fact]
    public async Task Uninstall_is_idempotent_for_unknown_and_leaves_others_intact()
    {
        var (primary, _, c) = Setup();
        await Scanner.UninstallModAsync("nope", c); // no throw
        Assert.True(File.Exists(Path.Combine(primary, "cool.pak")));
    }

    [Fact]
    public async Task Uninstall_throws_for_an_owned_mod_and_leaves_files_intact()
    {
        // Arrange: make the primary location owned by Vortex so the mod gets ReadOnly=true.
        var (primary, _, c) = Setup();
        File.WriteAllText(Path.Combine(primary, "__folder_managed_by_vortex"), "");

        // Act + Assert: must throw, not silently succeed.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Scanner.UninstallModAsync("cool", c));

        // The file must still exist — nothing was deleted.
        Assert.True(File.Exists(Path.Combine(primary, "cool.pak")));
    }

    // ---- B4U round 3: a name alias must never reach another mod's turned-off copy ----
    // Windows strips a trailing dot or space and expands an 8.3 alias when a path is opened, so joining a
    // mod's name onto disabled/ can land on a different mod's folder. The delete only happens when the
    // name is one folder as written AND disabled/ lists an entry by that real name.

    [Theory]
    [InlineData("cool.")]
    [InlineData("cool ")]
    public async Task A_name_ending_in_a_dot_or_space_never_deletes_the_turned_off_copy_it_normalises_onto(string name)
    {
        var (primary, _, c) = Setup();
        await Scanner.DisableModAsync("cool", c);
        var coolsCopy = Path.Combine(c.DisabledRoot, "cool", "cool.pak");
        Assert.True(File.Exists(coolsCopy)); // pre-condition: cool is off, its copy held
        var alias = Path.Combine(primary, name + ".pak");
        File.WriteAllText(alias, "ALIAS");
        Assert.Contains(await Scanner.BuildModListAsync(c), m => m.Name == name); // pre-condition: listed by that name

        await Scanner.UninstallModAsync(name, c);

        Assert.False(File.Exists(alias));
        Assert.Equal("X", File.ReadAllText(coolsCopy));
    }

    [Fact]
    public async Task An_8_3_alias_never_deletes_the_long_named_turned_off_copy()
    {
        var (primary, _, c) = Setup();
        File.WriteAllText(Path.Combine(primary, "Other Long Name Mod.pak"), "LONG");
        await Scanner.DisableModAsync("Other Long Name Mod", c);
        var longDir = Path.Combine(c.DisabledRoot, "Other Long Name Mod");
        var longCopy = Path.Combine(longDir, "Other Long Name Mod.pak");
        Assert.True(File.Exists(longCopy)); // pre-condition
        var alias = ModUninstallTests.ShortNameOf(longDir);
        if (alias is null) return;   // xUnit 2 has no runtime skip: 8.3 names are off on this volume, nothing to alias
        Assert.True(Directory.Exists(Path.Combine(c.DisabledRoot, alias))); // pre-condition: the alias resolves
        File.WriteAllText(Path.Combine(primary, alias + ".pak"), "ALIAS");

        await Scanner.UninstallModAsync(alias, c);

        Assert.False(File.Exists(Path.Combine(primary, alias + ".pak")));
        Assert.Equal("LONG", File.ReadAllText(longCopy));
    }

    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("..\\..\\x")]
    public async Task A_name_that_would_lead_outside_the_disabled_root_reaches_only_its_own_encoded_folder(string name)
    {
        // Such a name is held in its own HoldingName folder (".." in ~626~2e2e), which can't leave disabled/.
        // Before the encoding this was refused outright, because joining ".." onto disabled/ is the data folder.
        var (primary, _, c) = Setup();
        await Scanner.DisableModAsync("cool", c);
        var coolsCopy = Path.Combine(c.DisabledRoot, "cool", "cool.pak");
        var own = Path.Combine(c.DisabledRoot, HoldingName.Folder(name));
        Directory.CreateDirectory(own);
        File.WriteAllText(Path.Combine(own, "held.pak"), "OWN");

        await Scanner.UninstallModAsync(name, c);

        Assert.False(Directory.Exists(own));
        Assert.Equal("X", File.ReadAllText(coolsCopy));
        Assert.True(Directory.Exists(c.DataDir));
    }

    // An empty key once deleted every turned-off mod (disabled/ + "" is disabled/ itself). Refused in words.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_name_is_refused_with_the_no_name_message_and_nothing_is_deleted(string empty)
    {
        var (_, _, c) = Setup();
        await Scanner.DisableModAsync("cool", c);
        var coolsCopy = Path.Combine(c.DisabledRoot, "cool", "cool.pak");

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Scanner.UninstallModAsync(empty, c));

        Assert.Equal("626 can't uninstall a mod with no name. Nothing was deleted.", e.Message);
        Assert.Equal("X", File.ReadAllText(coolsCopy));
    }

    // A turned-off copy whose REAL folder name ends in a dot (only a \\?\-aware tool makes one) is this mod's
    // own: delete it exactly, and still never the lookalike Foo beside it.
    [Fact]
    public async Task A_turned_off_copy_whose_real_name_ends_in_a_dot_is_deleted_exactly_and_its_lookalike_survives()
    {
        var (_, _, c) = Setup();
        var foo = Path.Combine(c.DisabledRoot, "Foo");
        Directory.CreateDirectory(foo);
        File.WriteAllText(Path.Combine(foo, "Foo.pak"), "FOO");
        var odd = @"\\?\" + Path.Combine(c.DisabledRoot, "Foo.");
        Directory.CreateDirectory(odd);
        File.WriteAllText(odd + @"\odd.pak", "ODD");
        Assert.Contains(await Scanner.BuildModListAsync(c), m => m.Name == "Foo."); // pre-condition: listed by its real name

        await Scanner.UninstallModAsync("Foo.", c);

        Assert.False(Directory.Exists(odd));
        Assert.Equal("FOO", File.ReadAllText(Path.Combine(foo, "Foo.pak")));
        Assert.DoesNotContain(await Scanner.BuildModListAsync(c), m => m.Name == "Foo.");
    }

    // The live-file loop deletes entries the scan enumerated, by their real names. One whose name ends in a dot
    // or space (only a \\?\-aware tool can make one) must be deleted exactly, never the entry Windows would
    // normalise the path onto.
    [Fact]
    public void An_entry_whose_name_ends_in_a_space_is_deleted_exactly_and_its_lookalike_survives()
    {
        var dir = TestSupport.TempDir("uninstall-exact-");
        var real = Path.Combine(dir, "Foo");
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "keep.pak"), "KEEP");
        var odd = @"\\?\" + Path.Combine(dir, "Foo ");
        Directory.CreateDirectory(odd);
        File.WriteAllText(odd + @"\gone.pak", "GONE");

        Scanner.DeleteEntryExactly(dir, "Foo ");

        Assert.False(Directory.Exists(odd));
        Assert.Equal("KEEP", File.ReadAllText(Path.Combine(real, "keep.pak")));
    }
}
