using ModManager.Core;

namespace ModManager.Tests;

/// <summary>A20. Create the declared mod folder when the manifest named it — and only then.</summary>
public class ModFolderSeedTests
{
    // cyberpunk-2077 is in the embedded snapshot with modPath archive/pc/mod.
    private static GameEntry Cp(string root, string[]? userSet = null) => new()
    {
        Id = "cyberpunk-2077",
        Engine = "custom",
        GameRoot = root,
        ModLocations = new[] { new ModLocation("mods", "Mods", "archive/pc/mod") },
        UserSet = userSet,
    };

    [Fact]
    public void Creates_the_curated_folder_when_it_is_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "a20-" + Guid.NewGuid().ToString("N"));
        var path = ModFolderSeed.PathToCreate(Cp(root), exists: p => p == root);

        Assert.NotNull(path);
        Assert.EndsWith(Path.Combine("archive", "pc", "mod"), path);
    }

    [Fact]
    public void Creates_nothing_when_the_folder_is_already_there()
        => Assert.Null(ModFolderSeed.PathToCreate(Cp(@"C:\g\cp"), exists: _ => true));

    [Fact]
    public void Creates_nothing_when_the_manifest_names_no_path()
    {
        // A preset default or a hand-typed value pointing nowhere may be pointing nowhere because it
        // is WRONG. Creating it would replace a visible symptom with a silent one.
        var g = new GameEntry { Id = "not-in-any-manifest", Engine = "custom", GameRoot = @"C:\g\x" };

        Assert.Null(ModFolderSeed.PathToCreate(g, exists: _ => true));
    }

    [Fact]
    public void Creates_nothing_when_the_user_pinned_their_locations()
    {
        // Someone who pinned a location and left it empty meant to.
        var g = Cp(@"C:\g\cp", userSet: new[] { GameEntry.UserSetModLocations });

        Assert.Null(ModFolderSeed.PathToCreate(g, exists: p => p == @"C:\g\cp"));
    }

    [Fact]
    public void Creates_nothing_when_the_game_root_itself_is_gone()
    {
        // An absent game root means the install moved or vanished. Making a folder inside it would
        // resurrect a directory tree for a game that is not there.
        Assert.Null(ModFolderSeed.PathToCreate(Cp(@"C:\g\gone"), exists: _ => false));
    }

    [Fact]
    public void Creates_nothing_for_a_null_game()
        => Assert.Null(ModFolderSeed.PathToCreate(null));
}
