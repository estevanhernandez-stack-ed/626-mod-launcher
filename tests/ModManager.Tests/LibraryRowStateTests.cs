using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// A library row is switchable only when we KNOW its dependents and none of them is on.
///
/// <para>Este's design: the launcher already reads which mods need a library, so it can also read
/// whether any of them is currently on — refusing the toggle only when something would actually
/// break, rather than always. His own caveat was the important half: "unless we know there's gonna be
/// more instances where it doesn't match up like that." There is one, so there are three states and
/// not two. "Nothing declared that it needs this" is not "nothing needs this", and only the second
/// makes a toggle safe.</para>
///
/// <para>Driven through <see cref="ModListing.Resolve"/> against a real folder on disk, because the
/// state depends on the mod folder, the holding folder and the listing together.</para>
/// </summary>
public class LibraryRowStateTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "librow-" + Guid.NewGuid().ToString("N"));
    private readonly string _gameRoot;
    private readonly string _dataDir;
    private readonly string _mods;
    private readonly string _held;

    public LibraryRowStateTests()
    {
        _gameRoot = Path.Combine(_sandbox, "game");
        _dataDir = Path.Combine(_sandbox, "data");
        _mods = Path.Combine(_gameRoot, "scripts");
        _held = Path.Combine(_dataDir, "disabled");
        Directory.CreateDirectory(_mods);
        Directory.CreateDirectory(_held);
    }

    public void Dispose() { try { Directory.Delete(_sandbox, true); } catch { } }

    private GameEntry Game() => new()
    {
        Id = "librow",
        Engine = "custom",
        GameRoot = _gameRoot,
        DataDir = _dataDir,
        FileExtensions = new[] { "lua" },
        ModLocations = new[] { new ModLocation("mods", "Mods", "scripts") },
    };

    private void Library(string name, string file = "init.lua")
    {
        Directory.CreateDirectory(Path.Combine(_mods, name));
        File.WriteAllText(Path.Combine(_mods, name, file), "-- library");
    }

    private void EnabledDependent(string name, string library)
        => File.WriteAllText(Path.Combine(_mods, name + ".lua"), $@"local c = require(""{library}"")");

    private void DisabledDependent(string name, string library)
    {
        var dir = Path.Combine(_held, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".lua"), $@"local c = require(""{library}"")");
    }

    private Mod LibraryRow(string name)
        => Assert.Single(ModListing.Resolve(Game()), m => m.Name == name && m.Class == "library");

    [Fact]
    public void In_use_when_a_dependent_is_ON_so_the_toggle_is_refused()
    {
        Library("_CatLib");
        EnabledDependent("overlay", "_CatLib");

        var row = LibraryRow("_CatLib");
        Assert.True(row.ReadOnly);                       // no toggle, no uninstall
        Assert.Contains("is on and needs it", row.Description);
        Assert.Contains("overlay", row.Description);
    }

    [Fact]
    public void Idle_when_its_only_dependent_is_OFF_so_the_toggle_is_allowed()
    {
        // The real Monster Hunter Wilds state: every mod disabled, so nothing needs the library now.
        Library("_CatLib");
        DisabledDependent("overlay", "_CatLib");

        var row = LibraryRow("_CatLib");
        Assert.False(row.ReadOnly);                      // switchable
        Assert.Contains("Nothing that needs it is on right now", row.Description);
        Assert.Contains("overlay", row.Description);     // still names who would need it back
    }

    [Fact]
    public void Unknown_is_NOT_permission_when_nothing_readable_declares_it()
    {
        // The case Este's caveat asked about. No dependent anywhere - which tells us we could not
        // find one, not that none exists. A mod may load it in a way we cannot see, so it stays on.
        Library("mystery");

        var row = LibraryRow("mystery");
        Assert.True(row.ReadOnly);
        Assert.Contains("not the same as nothing needing it", row.Description);
    }

    [Fact]
    public void One_dependent_ON_among_several_is_enough_to_refuse()
    {
        Library("_CatLib");
        DisabledDependent("overlay", "_CatLib");
        EnabledDependent("hud", "_CatLib");

        var row = LibraryRow("_CatLib");
        Assert.True(row.ReadOnly);
        Assert.Contains("hud", row.Description);
    }

    [Fact]
    public void A_library_nothing_requires_does_not_borrow_another_s_dependents()
    {
        Library("_CatLib");
        Library("unrelated");
        EnabledDependent("overlay", "_CatLib");

        Assert.True(LibraryRow("_CatLib").ReadOnly);      // in use
        Assert.True(LibraryRow("unrelated").ReadOnly);    // unknown, not idle
        Assert.Contains("not the same as nothing needing it", LibraryRow("unrelated").Description);
    }

    [Fact]
    public void A_library_never_counts_its_own_files_as_a_dependent()
    {
        // A library's own sources require itself constantly. Counting that would pin it In-use
        // forever and no library would ever be switchable.
        Directory.CreateDirectory(Path.Combine(_mods, "_CatLib"));
        File.WriteAllText(Path.Combine(_mods, "_CatLib", "draw.lua"), @"require(""_CatLib.const"")");
        DisabledDependent("overlay", "_CatLib");

        Assert.False(LibraryRow("_CatLib").ReadOnly);
    }
}
