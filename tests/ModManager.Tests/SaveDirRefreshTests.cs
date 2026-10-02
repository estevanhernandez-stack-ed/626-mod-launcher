using ModManager.Core;
using ModManager.Core.Manifest;

namespace ModManager.Tests;

/// <summary>
/// The save folder a game USES can be the curated one rather than a stored guess, at read time only.
/// Found reviewing the feed PR that curated EA football's save folders (626-game-manifest#26): games added
/// before it stored <c>Documents\&lt;title&gt;</c>, the parent of <c>saves</c>, and a stored folder that
/// exists is never detected again.
/// </summary>
[Collection("ManifestState")]
public class SaveDirRefreshTests : IDisposable
{
    private readonly string _docs = Path.Combine(Path.GetTempPath(), "sdr-" + Guid.NewGuid().ToString("N"));
    private string Parent => Path.Combine(_docs, "EA SPORTS College Football 27");
    private string Saves => Path.Combine(Parent, "saves");

    public void Dispose()
    {
        EffectiveManifest.SetRemote(null);
        try { Directory.Delete(_docs, recursive: true); } catch { }
    }

    private static GameEntry Ea(string? saveDir, IReadOnlyList<string>? userSet = null) => new()
    {
        Id = "ea-sports-college-football-27", GameName = "EA SPORTS College Football 27", Engine = "frostbite",
        EaContentId = "16425899", SaveDir = saveDir, UserSet = userSet,
    };

    private static bool Exists(string _) => true;

    [Fact]
    public void A_stored_direct_parent_of_the_curated_folder_gives_way_to_it()
        => Assert.Equal(Saves, SaveDirRefresh.Narrowed(Ea(Parent), Saves, Exists));

    [Fact]
    public void Dots_and_a_trailing_separator_are_spelling_not_location()
        => Assert.Equal(Saves, SaveDirRefresh.Narrowed(
            Ea(Path.Combine(_docs, ".", "EA SPORTS College Football 27") + Path.DirectorySeparatorChar), Saves, Exists));

    [Fact]
    public void The_curated_folder_already_stored_is_kept()
        => Assert.Null(SaveDirRefresh.Narrowed(Ea(Saves), Saves, Exists));

    // Only the exact guess (one level too high) is corrected. Anything else is a choice.
    [Fact]
    public void A_stored_folder_that_is_not_the_direct_parent_is_kept()
    {
        Assert.Null(SaveDirRefresh.Narrowed(Ea(_docs), Saves, Exists));                                  // two levels up
        Assert.Null(SaveDirRefresh.Narrowed(Ea(Path.Combine(Saves, "sub")), Saves, Exists));             // a child
        Assert.Null(SaveDirRefresh.Narrowed(Ea(Parent[..^1]), Saves, Exists));                           // "...Football 2"
        Assert.Null(SaveDirRefresh.Narrowed(Ea(Path.Combine(_docs, "elsewhere")), Saves, Exists));
    }

    // Review on #361: a folder the user picked is theirs, even the parent.
    [Fact]
    public void A_folder_the_user_picked_is_never_replaced()
        => Assert.Null(SaveDirRefresh.Narrowed(Ea(Parent, new[] { GameEntry.UserSetSaveDir }), Saves, Exists));

    [Fact]
    public void A_curated_folder_that_does_not_exist_is_not_used()
        => Assert.Null(SaveDirRefresh.Narrowed(Ea(Parent), Saves, _ => false));

    [Fact]
    public void Nothing_stored_or_nothing_curated_uses_what_is_stored()
    {
        Assert.Null(SaveDirRefresh.Narrowed(Ea(null), Saves, Exists));
        Assert.Null(SaveDirRefresh.Narrowed(Ea(Parent), null, Exists));
    }

    // A game that can restore keeps its stored folder: under it, an old snapshot rooted at the parent
    // would come back one level down as saves\saves.
    [Fact]
    public void A_game_whose_saves_can_be_restored_is_never_narrowed()
    {
        var eldenRing = new GameEntry { Id = "elden-ring", Engine = "fromsoft", SteamAppId = "1245620", SaveDir = Parent };
        Assert.Null(SaveDirRefresh.Narrowed(eldenRing, Saves, Exists));
    }

    // Review on #361: a hint naming a store account is one player's folder; on a shared PC, narrowing
    // to it would show another player's saves.
    [Fact]
    public void A_curated_folder_that_names_a_store_account_is_never_used()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "ea-sports-college-football-27", Name = "CFB", Engine = "frostbite",
                    Stores = new StoreIds { EaContentId = "16425899" },
                    SaveDirHint = "<winDocuments>/EA SPORTS College Football 27/<storeUserId>",
                },
            },
        });

        Assert.Null(SaveDirRefresh.Narrowed(Ea(Parent), Path.Combine(Parent, "1234"), Exists));
    }

    // Read time only: the context uses the effective folder, and the stored entry is untouched, so
    // nothing that writes the registry back can persist it.
    [Fact]
    public void The_context_uses_the_effective_folder_and_leaves_the_stored_entry_alone()
    {
        var game = Ea(Parent);

        var ctx = Scanner.GameContext(game, Saves);

        Assert.Equal(Saves, ctx.SaveDir);
        Assert.Equal(Parent, ctx.Game.SaveDir);
        Assert.Equal(Parent, Scanner.GameContext(game).SaveDir);   // no override: the stored folder
    }
}
