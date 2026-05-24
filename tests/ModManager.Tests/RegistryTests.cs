using ModManager.Core;

namespace ModManager.Tests;

// Ports registry-core.test.js — pure registry operations (active-game selection, upsert).
public class RegistryTests
{
    private static GameEntry G(string id) => new()
    {
        Id = id,
        GameName = id,
        GameRoot = "X",
        ModLocations = Array.Empty<ModLocation>(),
        FileExtensions = new[] { "pak" },
        GroupingRule = "filename_no_ext",
    };

    [Fact]
    public void EmptyRegistry_shape()
    {
        var r = Registry.EmptyRegistry();
        Assert.Equal(1, r.Version);
        Assert.Null(r.ActiveGameId);
        Assert.Empty(r.Games);
    }

    [Fact]
    public void Upsert_adds_and_sets_active_when_first()
    {
        var r = Registry.UpsertGame(Registry.EmptyRegistry(), G("a"));
        Assert.Single(r.Games);
        Assert.Equal("a", r.ActiveGameId);
    }

    [Fact]
    public void Upsert_updates_existing_by_id()
    {
        var r = Registry.UpsertGame(Registry.EmptyRegistry(), G("a"));
        r = Registry.UpsertGame(r, new GameEntry { Id = "a", GameName = "Renamed" });
        Assert.Single(r.Games);
        Assert.Equal("Renamed", r.Games[0].GameName);
    }

    [Fact]
    public void GetActiveGame_falls_back_to_first()
    {
        var r = Registry.UpsertGame(Registry.EmptyRegistry(), G("a"));
        r = Registry.UpsertGame(r, G("b"));
        r.ActiveGameId = "missing";
        Assert.Equal("a", Registry.GetActiveGame(r)!.Id);
    }

    [Fact]
    public void SetActiveGame_ignores_unknown_id()
    {
        var r = Registry.UpsertGame(Registry.EmptyRegistry(), G("a"));
        Assert.Equal("a", Registry.SetActiveGame(r, "nope").ActiveGameId);
    }

    [Fact]
    public void SetActiveGame_switches_to_a_known_id()
    {
        var r = Registry.UpsertGame(Registry.EmptyRegistry(), G("a"));
        r = Registry.UpsertGame(r, G("b"));
        Assert.Equal("b", Registry.SetActiveGame(r, "b").ActiveGameId);
    }
}
