using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// A stored save folder that a guess stopped one level too high gives way to the curated one, and only
/// where nothing restores. Found reviewing the feed PR that curated EA football's save folders
/// (626-game-manifest#26): registrations made before it stored <c>Documents\&lt;title&gt;</c>, the parent
/// of <c>saves</c>, and a stored folder that exists is never detected again.
/// </summary>
public class SaveDirRefreshTests
{
    private const string Parent = @"C:\Users\p\Documents\EA SPORTS College Football 27";
    private const string Saves = @"C:\Users\p\Documents\EA SPORTS College Football 27\saves";

    private static GameEntry Ea(string? saveDir) => new()
    {
        Id = "ea-sports-college-football-27", GameName = "EA SPORTS College Football 27", Engine = "frostbite",
        EaContentId = "16425899", SaveDir = saveDir,
    };

    private static GameEntry Steam(string? saveDir) => new()
    {
        Id = "elden-ring", GameName = "Elden Ring", Engine = "fromsoft", SteamAppId = "1245620", SaveDir = saveDir,
    };

    private static bool Exists(string _) => true;

    [Fact]
    public void A_stored_parent_of_the_curated_folder_moves_to_it()
        => Assert.Equal(Saves, SaveDirRefresh.Narrowed(Ea(Parent), Saves, Exists));

    [Fact]
    public void Separators_case_and_a_trailing_slash_are_spelling_not_location()
        => Assert.Equal("c:/users/p/documents/ea sports college football 27/saves",
            SaveDirRefresh.Narrowed(Ea(Parent + @"\"), "c:/users/p/documents/ea sports college football 27/saves", Exists));

    [Fact]
    public void The_curated_folder_already_stored_is_kept()
        => Assert.Null(SaveDirRefresh.Narrowed(Ea(Saves), Saves, Exists));

    // Anywhere other than an ancestor is a choice (the user's, or Ludusavi's), not a guess to correct.
    [Fact]
    public void A_stored_folder_elsewhere_is_kept()
    {
        Assert.Null(SaveDirRefresh.Narrowed(Ea(@"D:\Backups\cfb"), Saves, Exists));
        Assert.Null(SaveDirRefresh.Narrowed(Ea(Saves + @"\sub"), Saves, Exists));            // a child, not a parent
        Assert.Null(SaveDirRefresh.Narrowed(Ea(Parent[..^1]), Saves, Exists));                // "...Football 2": a prefix, not a parent
    }

    [Fact]
    public void A_curated_folder_that_does_not_exist_moves_nothing()
        => Assert.Null(SaveDirRefresh.Narrowed(Ea(Parent), Saves, _ => false));

    [Fact]
    public void Nothing_stored_or_nothing_curated_moves_nothing()
    {
        Assert.Null(SaveDirRefresh.Narrowed(Ea(null), Saves, Exists));
        Assert.Null(SaveDirRefresh.Narrowed(Ea(Parent), null, Exists));
    }

    // A game that can restore keeps its stored folder: narrowing under it would put an old snapshot,
    // rooted at the parent, back one level down as saves\saves.
    [Fact]
    public void A_game_whose_saves_can_be_restored_is_never_narrowed()
        => Assert.Null(SaveDirRefresh.Narrowed(
            Steam(@"C:\Users\p\AppData\Roaming\EldenRing"), @"C:\Users\p\AppData\Roaming\EldenRing\76561198000000000", Exists));
}
