using System.Security.Cryptography;
using System.Text.Json;
using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Two mods whose names Windows reads as one folder. Under the UE-pak rule <c>Foo_P.pak</c> is <c>Foo</c> and
/// <c>Foo _P.pak</c> is <c>Foo </c>; Windows strips the trailing space, so holding <c>Foo </c> in
/// <c>disabled/Foo </c> used to put it in <c>Foo</c>'s folder. <c>CON_P.pak</c> is <c>CON</c>, a device.
/// Each now has its own holding folder (<see cref="HoldingName"/>), and every toggle is byte-identical.
/// </summary>
public class NameAliasToggleTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-alias-");
    private string GameRoot => Path.Combine(_root, "game");
    private string DataDir => Path.Combine(_root, "data");
    private string Mods => Path.Combine(GameRoot, "Content", "Paks", "~mods");
    private string Disabled => Path.Combine(DataDir, "disabled");

    public NameAliasToggleTests()
    {
        Directory.CreateDirectory(Mods);
        File.WriteAllText(Path.Combine(Mods, "Foo_P.pak"), "PLAIN FOO");
        File.WriteAllText(Path.Combine(Mods, "Foo _P.pak"), "SPACED FOO");
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    // The ue-pak preset, as the add-game flow builds it: strip_underscore_p_suffix.
    private GameEntry Game()
    {
        var g = EnginePresets.BuildGameEntry(
            new GameInput { Name = "Alias", Engine = "ue-pak", GameRoot = GameRoot, ModPath = "Content/Paks/~mods" },
            Array.Empty<string>());
        g.DataDir = DataDir;
        return g;
    }

    private GameContext Ctx() => Scanner.GameContext(Game());

    private Task<IReadOnlyList<Mod>> Rows() => Scanner.BuildModListAsync(Ctx());

    /// <summary>Every file under the game root, by relative path, with its SHA-256.</summary>
    private Dictionary<string, string> GameHashes()
        => Directory.GetFiles(GameRoot, "*", SearchOption.AllDirectories).ToDictionary(
            p => Path.GetRelativePath(GameRoot, p),
            p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))),
            StringComparer.Ordinal);

    private string Held(string modName) => Path.Combine(Disabled, HoldingName.Folder(modName)!);

    [Fact]
    public async Task Both_names_list_as_separate_rows()
    {
        var rows = await Rows();

        Assert.Single(rows, m => m.Name == "Foo" && m.Enabled);
        Assert.Single(rows, m => m.Name == "Foo " && m.Enabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Each_is_held_apart_and_both_come_back_byte_identical(bool spacedFirst)
    {
        var before = GameHashes();
        var order = spacedFirst ? new[] { "Foo ", "Foo" } : new[] { "Foo", "Foo " };

        await Scanner.DisableModAsync(order[0], Ctx());
        if (order[0] == "Foo ")
        {
            // Held in its own encoded folder; Foo is live and untouched.
            Assert.Equal("SPACED FOO", File.ReadAllText(Path.Combine(Disabled, "~626~466f6f20", "Foo _P.pak")));
            Assert.Equal("PLAIN FOO", File.ReadAllText(Path.Combine(Mods, "Foo_P.pak")));
            Assert.False(Directory.Exists(Path.Combine(Disabled, "Foo")));
        }
        else
        {
            Assert.Equal("PLAIN FOO", File.ReadAllText(Path.Combine(Disabled, "Foo", "Foo_P.pak")));
            Assert.Equal("SPACED FOO", File.ReadAllText(Path.Combine(Mods, "Foo _P.pak")));
            Assert.False(Directory.Exists(Path.Combine(Disabled, "~626~466f6f20")));
        }

        await Scanner.DisableModAsync(order[1], Ctx());

        Assert.Equal("PLAIN FOO", File.ReadAllText(Path.Combine(Disabled, "Foo", "Foo_P.pak")));
        Assert.Equal("SPACED FOO", File.ReadAllText(Path.Combine(Disabled, "~626~466f6f20", "Foo _P.pak")));
        Assert.False(File.Exists(Path.Combine(Disabled, "Foo", "Foo _P.pak")));
        Assert.Empty(Directory.GetFiles(Mods));

        var rows = await Rows();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, m => m.Name == "Foo" && !m.Enabled && m.Files.SequenceEqual(new[] { "Foo_P.pak" }));
        Assert.Single(rows, m => m.Name == "Foo " && !m.Enabled && m.Files.SequenceEqual(new[] { "Foo _P.pak" }));

        await Scanner.EnableModAsync(order[0], Ctx());
        await Scanner.EnableModAsync(order[1], Ctx());

        Assert.Equal(before, GameHashes());
        Assert.Empty(Directory.GetFileSystemEntries(Disabled));
        Assert.All(await Rows(), m => Assert.True(m.Enabled));
    }

    [Fact]
    public async Task A_device_name_turns_off_and_on_byte_identically_through_its_encoded_folder()
    {
        File.WriteAllText(Path.Combine(Mods, "CON_P.pak"), "CONSOLE MOD");
        var before = GameHashes();
        Assert.Single(await Rows(), m => m.Name == "CON");

        await Scanner.DisableModAsync("CON", Ctx());

        Assert.Equal("CONSOLE MOD", File.ReadAllText(Path.Combine(Disabled, "~626~434f4e", "CON_P.pak")));
        Assert.Equal(new[] { "~626~434f4e" }, Directory.GetDirectories(Disabled).Select(Path.GetFileName));
        Assert.Single(await Rows(), m => m.Name == "CON" && !m.Enabled);

        await Scanner.EnableModAsync("CON", Ctx());

        Assert.Equal(before, GameHashes());
        Assert.Empty(Directory.GetFileSystemEntries(Disabled));
    }

    [Theory]
    [InlineData("Foo ", false)]
    [InlineData("Foo ", true)]
    [InlineData("CON", false)]
    [InlineData("CON", true)]
    public async Task Uninstall_takes_only_its_own_files_and_leaves_Foo_alone(string name, bool turnedOff)
    {
        File.WriteAllText(Path.Combine(Mods, "CON_P.pak"), "CONSOLE MOD");
        // The bystander Foo, held off, so its folder is the one an aliasing join would reach.
        await Scanner.DisableModAsync("Foo", Ctx());
        if (turnedOff) await Scanner.DisableModAsync(name, Ctx());
        var row = ModListing.Resolve(Game()).Single(m => m.Name == name);

        ModUninstall.Run(Ctx(), row);

        Assert.DoesNotContain(await Rows(), m => m.Name == name);
        Assert.False(Directory.Exists(Held(name)));
        Assert.Equal("PLAIN FOO", File.ReadAllText(Path.Combine(Disabled, "Foo", "Foo_P.pak")));
        Assert.True(File.Exists(Path.Combine(Disabled, "Foo", "meta.json")));
        Assert.Single(await Rows(), m => m.Name == "Foo" && !m.Enabled);
    }

    // An ordinary name is never encoded, whatever its length, so a long one works exactly as before.
    [Fact]
    public async Task A_230_character_ordinary_name_turns_off_and_on_in_its_own_named_folder()
    {
        var name = new string('L', 230);
        File.WriteAllText(Path.Combine(Mods, name + "_P.pak"), "LONG MOD");
        var before = GameHashes();

        await Scanner.DisableModAsync(name, Ctx());

        Assert.Equal("LONG MOD", File.ReadAllText(Path.Combine(Disabled, name, name + "_P.pak")));
        Assert.Single(await Rows(), m => m.Name == name && !m.Enabled);

        await Scanner.EnableModAsync(name, Ctx());

        Assert.Equal(before, GameHashes());
        Assert.Empty(Directory.GetFileSystemEntries(Disabled));
    }

    // A RISKY name too long to encode in 255 characters has no holding folder: turning it off refuses,
    // before anything moves, and uninstalling it has nothing held to delete.
    [Fact]
    public async Task A_130_character_name_ending_in_a_dot_refuses_to_turn_off_and_moves_nothing()
    {
        var name = new string('D', 129) + ".";
        File.WriteAllText(Path.Combine(Mods, name + "_P.pak"), "DOTTED LONG");
        var before = GameHashes();
        Assert.Single(await Rows(), m => m.Name == name);

        var e = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Scanner.DisableModAsync(name, Ctx()));

        Assert.Equal($"626 can't turn \"{name}\" off: its name is too long to hold safely. Rename the file and try again. "
                     + "Nothing was moved.", e.Message);
        Assert.Equal(before, GameHashes());
        Assert.False(Directory.Exists(Disabled) && Directory.GetFileSystemEntries(Disabled).Length > 0);

        var row = ModListing.Resolve(Game()).Single(m => m.Name == name);
        Assert.Empty(ModUninstall.Preview(Ctx(), row).HeldFolders);
        ModUninstall.Run(Ctx(), row);
        Assert.False(File.Exists(Path.Combine(Mods, name + "_P.pak")));
        Assert.Equal("PLAIN FOO", File.ReadAllText(Path.Combine(Mods, "Foo_P.pak")));
    }

    // What an earlier build left: disabled/Bar with its record, an ordinary name, so the same folder.
    [Fact]
    public async Task A_legacy_held_folder_still_lists_and_turns_on()
    {
        var legacy = Path.Combine(Disabled, "Bar");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "Bar_P.pak"), "LEGACY BAR");
        File.WriteAllText(Path.Combine(legacy, "meta.json"), JsonSerializer.Serialize(new
        {
            location = "mods", hadOnServer = new Dictionary<string, bool> { ["Bar_P.pak"] = false },
            disabledAt = "2026-09-01T00:00:00.0000000Z", isFolder = false,
        }));

        Assert.Single(await Rows(), m => m.Name == "Bar" && !m.Enabled);

        await Scanner.EnableModAsync("Bar", Ctx());

        Assert.Equal("LEGACY BAR", File.ReadAllText(Path.Combine(Mods, "Bar_P.pak")));
        Assert.False(Directory.Exists(legacy));
    }

    // Held as an older build would have: a plain CreateDirectory named after the mod, with its record.
    private string? LegacyHold(string modName, string file, string content)
    {
        var dir = Path.Combine(Disabled, modName);
        try { Directory.CreateDirectory(dir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        if (!FolderNames.HasEntryNamed(Disabled, modName)) return null;   // the OS made something else of it
        File.WriteAllText(Path.Combine(dir, file), content);
        File.WriteAllText(Path.Combine(dir, "meta.json"), JsonSerializer.Serialize(new
        {
            location = "mods", hadOnServer = new Dictionary<string, bool> { [file] = false },
            disabledAt = "2026-09-01T00:00:00.0000000Z", isFolder = false,
        }));
        return dir;
    }

    // Windows 11 lets an older build create disabled/Aux, disabled/CON or disabled/Con.Fix. Those names now
    // encode, so the turn-on would look only in ~626~... and leave the row stuck off; the legacy fallback
    // reads the folder by its real name.
    [Theory]
    [InlineData("Aux")]
    [InlineData("CON")]
    [InlineData("Con.Fix")]
    public async Task A_legacy_device_name_hold_lists_and_turns_on_byte_identically(string name)
    {
        var file = name + "_P.pak";
        var dir = LegacyHold(name, file, "LEGACY " + name);
        if (dir is null)
        {
            // xUnit 2 has no runtime skip. This Windows refuses a folder by that name, so no older build can
            // have made one and there is nothing to read back. Passing vacuously.
            return;
        }
        Assert.NotEqual(name, HoldingName.Folder(name));   // pre-condition: the name now encodes

        Assert.Single(await Rows(), m => m.Name == name && !m.Enabled);

        var outcome = await Scanner.EnableModWithOutcomeAsync(name, Ctx());

        Assert.True(outcome.Enabled, outcome.Reason);
        Assert.Equal("LEGACY " + name, File.ReadAllText(Path.Combine(Mods, file)));
        Assert.False(FolderNames.HasEntryNamed(Disabled, name));
        Assert.Single(await Rows(), m => m.Name == name && m.Enabled);
    }

    [Fact]
    public async Task A_legacy_device_name_hold_uninstalls_through_its_real_folder()
    {
        if (LegacyHold("Aux", "Aux_P.pak", "LEGACY") is null) return;   // see above: passing vacuously
        await Scanner.DisableModAsync("Foo", Ctx());                      // a bystander held beside it
        var row = ModListing.Resolve(Game()).Single(m => m.Name == "Aux");

        ModUninstall.Run(Ctx(), row);

        Assert.False(FolderNames.HasEntryNamed(Disabled, "Aux"));
        Assert.DoesNotContain(await Rows(), m => m.Name == "Aux");
        Assert.Equal("PLAIN FOO", File.ReadAllText(Path.Combine(Disabled, "Foo", "Foo_P.pak")));
    }

    // A ~626~ folder that Folder would not write (hand-made, or a mod literally named so) is a raw name,
    // listed and turned on through the same fallback, never hidden.
    [Theory]
    [InlineData("~626~zz")]
    [InlineData("~626~466f6f")]
    public async Task A_non_canonical_prefixed_folder_lists_under_its_raw_name_and_turns_on(string name)
    {
        Assert.NotNull(LegacyHold(name, "Odd_P.pak", "ODD"));

        Assert.Single(await Rows(), m => m.Name == name && !m.Enabled);
        var outcome = await Scanner.EnableModWithOutcomeAsync(name, Ctx());

        Assert.True(outcome.Enabled, outcome.Reason);
        Assert.Equal("ODD", File.ReadAllText(Path.Combine(Mods, "Odd_P.pak")));
        Assert.False(Directory.Exists(Path.Combine(Disabled, name)));
    }
}

/// <summary>
/// The same alias on a Cyberpunk-shaped game with extra trees: <c>Foo.archive</c> is <c>Foo</c> and
/// <c>Foo..archive</c> is <c>Foo.</c>. A folder <c>r6/scripts/Foo.</c> can't be made on Windows, so the
/// extra-tree side has one <c>r6/scripts/Foo</c>, and its key rule (letters and digits only) reads both mods
/// as its claimant. Two claimants hold the entry back, so <c>Foo.</c>'s turn-off moves only its own main file.
/// </summary>
public class NameAliasTreeToggleTests : IDisposable
{
    private static readonly string[] Trees = { "r6/scripts", "r6/tweaks" };

    private readonly string _root = TestSupport.TempDir("mmb-alias-trees-");
    private string GameRoot => Path.Combine(_root, "game");
    private string DataDir => Path.Combine(_root, "data");

    public NameAliasTreeToggleTests()
    {
        Put("archive/pc/mod/Foo.archive", "PLAIN");
        Put("archive/pc/mod/Foo..archive", "DOTTED");
        Put("r6/scripts/Foo/main.reds", "FOO SCRIPTS");
    }

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private void Put(string rel, string content)
    {
        var p = Path.Combine(GameRoot, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
    }

    private GameContext Ctx() => Scanner.GameContext(new GameEntry
    {
        Id = "alias-trees", Engine = "custom", GameRoot = GameRoot, DataDir = DataDir,
        FileExtensions = new[] { "archive" },
        ModLocations = new[] { new ModLocation("mods", "Mods", "archive/pc/mod") },
    }, extraModTrees: Trees);

    [Fact]
    public async Task Turning_off_Foo_dot_holds_its_main_file_apart_and_moves_none_of_Foos_extras()
    {
        var rows = await Scanner.BuildModListAsync(Ctx());
        Assert.Single(rows, m => m.Name == "Foo");
        Assert.Single(rows, m => m.Name == "Foo.");

        // Two claimants for r6/scripts/Foo: the single-claimant rule holds it back for both.
        var moves = Scanner.ExtraTreeRowsFor(Ctx()).MovesFor(rows.Single(m => m.Name == "Foo."));
        Assert.Empty(moves.Movable);

        await Scanner.DisableModAsync("Foo.", Ctx());

        var held = Path.Combine(DataDir, "disabled", "~626~466f6f2e");
        Assert.Equal("DOTTED", File.ReadAllText(Path.Combine(held, "Foo..archive")));
        Assert.Equal("PLAIN", File.ReadAllText(Path.Combine(GameRoot, "archive", "pc", "mod", "Foo.archive")));
        Assert.Equal("FOO SCRIPTS", File.ReadAllText(Path.Combine(GameRoot, "r6", "scripts", "Foo", "main.reds")));
        Assert.False(Directory.Exists(Path.Combine(DataDir, "disabled", "Foo")));
        Assert.False(Directory.Exists(Path.Combine(DataDir, "disabled-trees")));

        var after = await Scanner.BuildModListAsync(Ctx());
        Assert.Single(after, m => m.Name == "Foo." && !m.Enabled);
        Assert.Single(after, m => m.Name == "Foo" && m.Enabled);

        await Scanner.EnableModAsync("Foo.", Ctx());

        Assert.Equal("DOTTED", File.ReadAllText(Path.Combine(GameRoot, "archive", "pc", "mod", "Foo..archive")));
        Assert.False(Directory.Exists(held));
    }

    // v0.23.0 held extra-tree entries under the raw name too: disabled-trees/Aux comes back with disabled/Aux.
    [Fact]
    public async Task A_legacy_device_name_hold_restores_its_extra_tree_entries_too()
    {
        var main = Path.Combine(DataDir, "disabled", "Aux");
        var extra = Path.Combine(DataDir, "disabled-trees", "Aux", "r6", "scripts", "Aux", "a.reds");
        try { Directory.CreateDirectory(main); Directory.CreateDirectory(Path.GetDirectoryName(extra)!); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return; }
        if (!FolderNames.HasEntryNamed(Path.Combine(DataDir, "disabled"), "Aux")) return;   // passing vacuously
        File.WriteAllText(Path.Combine(main, "Aux.archive"), "AUX MAIN");
        File.WriteAllText(Path.Combine(main, "meta.json"),
            "{\"location\":\"mods\",\"hadOnServer\":{\"Aux.archive\":false},\"isFolder\":false}");
        File.WriteAllText(extra, "AUX SCRIPTS");
        Assert.Single(await Scanner.BuildModListAsync(Ctx()), m => m.Name == "Aux" && !m.Enabled);

        var outcome = await Scanner.EnableModWithOutcomeAsync("Aux", Ctx());

        Assert.True(outcome.Enabled, outcome.Reason);
        Assert.Null(outcome.Reason);
        Assert.Equal("AUX MAIN", File.ReadAllText(Path.Combine(GameRoot, "archive", "pc", "mod", "Aux.archive")));
        Assert.Equal("AUX SCRIPTS", File.ReadAllText(Path.Combine(GameRoot, "r6", "scripts", "Aux", "a.reds")));
        Assert.False(Directory.Exists(main));
        Assert.False(Directory.Exists(Path.Combine(DataDir, "disabled-trees", "Aux")));
    }

    [Fact]
    public void A_held_extra_tree_folder_uses_the_encoded_name()
        => Assert.Equal(Path.Combine(DataDir, "disabled-trees", "~626~466f6f2e"), TreeHolding.ModDir(Ctx(), "Foo."));
}
