using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Persistence;

namespace ModManager.Tests;

// A registration's stored value is ambiguous: "pak" might be a choice the user made, or the engine
// preset's default frozen in on the day they clicked Add. RegistrationRefresh guesses between those
// with an untouched-default heuristic. This marker removes the guess for anything the user actually
// edits — and because only the edit path writes it, adding it needs no migration.
public class UserSetMarkerTests
{
    [Fact]
    public void UserSet_round_trips_as_camelCase()
    {
        var dir = TestSupport.TempDir("userset-");
        var reg = new GameRegistry
        {
            Version = 1,
            ActiveGameId = "cyberpunk-2077",
            Games = new List<GameEntry>
            {
                new()
                {
                    Id = "cyberpunk-2077",
                    GameName = "Cyberpunk 2077",
                    FileExtensions = new[] { "archive" },
                    UserSet = new[] { GameEntry.UserSetFileExtensions },
                },
            },
        };

        RegistryStore.Save(dir, reg);

        var json = File.ReadAllText(Path.Combine(dir, "games.json"));
        Assert.Contains("\"userSet\"", json);          // camelCase on disk (the launcher's convention)
        Assert.DoesNotContain("\"UserSet\"", json);

        var loaded = RegistryStore.Load(dir);
        Assert.Equal(new[] { "fileExtensions" }, loaded.Games[0].UserSet);
    }

    // The whole reason this was cheap to add now and expensive during A1: every registration written
    // before today simply has no key, and must behave exactly as it does today.
    [Fact]
    public void A_registration_written_before_the_marker_loads_with_no_marker()
    {
        var dir = TestSupport.TempDir("userset-");
        File.WriteAllText(Path.Combine(dir, "games.json"),
            """
            { "version": 1, "activeGameId": "elden-ring",
              "games": [ { "id": "elden-ring", "gameName": "ELDEN RING", "fileExtensions": [] } ] }
            """);

        var loaded = RegistryStore.Load(dir);

        Assert.Null(loaded.Games[0].UserSet);
    }

    // A null marker must not add noise to every existing registration on disk.
    [Fact]
    public void An_unset_marker_is_omitted_from_the_file_entirely()
    {
        var dir = TestSupport.TempDir("userset-");
        RegistryStore.Save(dir, new GameRegistry
        {
            Games = new List<GameEntry> { new() { Id = "witchfire", GameName = "Witchfire" } },
        });

        Assert.DoesNotContain("userSet", File.ReadAllText(Path.Combine(dir, "games.json")));
    }

    // The constants exist so a typo is a compile error rather than a marker nothing ever matches.
    [Fact]
    public void The_field_name_constants_are_the_camelCase_json_names()
    {
        Assert.Equal("fileExtensions", GameEntry.UserSetFileExtensions);
        Assert.Equal("groupingRule", GameEntry.UserSetGroupingRule);
        Assert.Equal("modLocations", GameEntry.UserSetModLocations);
        Assert.Equal("gameRoot", GameEntry.UserSetGameRoot);
    }
}
