using ModManager.Core;

namespace ModManager.Tests.Stores;

public class NoModLaneTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-nolane-");
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private GameEntry Frostbite()
    {
        var root = Path.Combine(_root, "Madden NFL 27");
        Directory.CreateDirectory(root);
        var e = EnginePresets.BuildGameEntry(new GameInput { Id = "madden-nfl-27", Name = "Madden NFL 27", Engine = "frostbite", GameRoot = root }, null);
        e.DataDir = Path.Combine(_root, "data");
        return e;
    }

    [Fact]
    public void A_game_with_no_mod_locations_has_no_mod_lane()
        => Assert.True(ModListing.HasNoModLane(Scanner.GameContext(Frostbite())));

    [Fact]
    public void The_same_game_with_a_location_behaves_like_any_other_game()
    {
        // Facts, not policy: nothing keys on the engine name or the store.
        var e = Frostbite();
        e.ModLocations = new[] { new ModLocation("mods", "mods", "mods") };
        Assert.False(ModListing.HasNoModLane(Scanner.GameContext(e)));
    }

    [Fact]
    public void Lanes_that_do_not_use_locations_are_not_mistaken_for_none()
    {
        var fromsoft = new GameEntry { Id = "er", Engine = "fromsoft", GameRoot = _root, DataDir = Path.Combine(_root, "d2") };
        Assert.False(ModListing.HasNoModLane(Scanner.GameContext(fromsoft)));
    }

    [Fact]
    public async Task A_toggle_on_a_game_with_no_mod_lane_is_refused_with_the_sentence()
    {
        var ctx = Scanner.GameContext(Frostbite());
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ModToggle.SetEnabledAsync(ctx, new Mod { Name = "anything", Location = "mods" }, false));
        Assert.Equal(ModListEmptyState.NoModLane, e.Message);
    }
}
