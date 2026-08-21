using ModManager.Core.Characters;

namespace ModManager.Tests;

/// <summary>
/// Listing the characters in a Cyberpunk 2077 save folder — the second implementation, and the one
/// that showed listing and editing are different jobs.
///
/// <para>Fixtures are shaped from the real thing: 93 save folders across two playthroughs, each
/// holding <c>metadata.9.json</c>, <c>sav.dat</c> and <c>screenshot.png</c> — except two, which have
/// no payload at all.</para>
/// </summary>
public class CyberpunkCharactersTests
{
    private static string Save(string root, string folder, string json, bool payload = true, bool shot = true)
    {
        var dir = Path.Combine(root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "metadata.9.json"), json);
        if (payload) File.WriteAllText(Path.Combine(dir, "sav.dat"), "not really lz4");
        if (shot) File.WriteAllText(Path.Combine(dir, "screenshot.png"), "png");
        return dir;
    }

    private static string Meta(string name, string lifePath, int level, int cred,
                              string stamp, string playthrough = "abc123", string modded = "true")
        => $$"""
        {
          "RootType": "saveMetadataContainer",
          "Data": {
            "metadata": {
              "name": "{{name}}", "lifePath": "{{lifePath}}",
              "level": {{level}}.0, "streetCred": {{cred}}.0,
              "timestampString": "{{stamp}}",
              "playthroughID": "{{playthrough}}",
              "isModded": {{modded}}
            }
          }
        }
        """;

    [Fact]
    public void A_save_reads_as_a_character_with_its_identity_and_progress()
    {
        var root = TestSupport.TempDir("cp-read-");
        Save(root, "ManualSave-12", Meta("ManualSave-12", "Nomad", 47, 39, "18:08:48, 21.07.2025"));

        var c = Assert.Single(CyberpunkCharacters.Read(root));

        Assert.Equal("ManualSave-12", c.Id);
        Assert.Equal("Nomad", c.Kind);
        Assert.Equal("Level 47  ·  Street Cred 39", c.Progress);
        Assert.Equal(new DateTime(2025, 7, 21, 18, 8, 48), c.LastPlayed);
        Assert.True(c.MadeWithMods);
        Assert.False(c.Editable);            // sav.dat is chunked LZ4 and we have no business writing it
        Assert.NotNull(c.ThumbnailPath);
    }

    [Fact]
    public void The_folder_name_is_the_name_because_that_is_what_the_game_shows()
    {
        // Renaming the folder renames the save in-game; `name` in the metadata is only ever a mirror,
        // written once and never corrected. So a renamed folder must win over a stale field.
        var root = TestSupport.TempDir("cp-name-");
        Save(root, "Before Point Of No Return", Meta("ManualSave-12", "Corpo", 40, 30, "10:00:00, 1.01.2025"));

        Assert.Equal("Before Point Of No Return", Assert.Single(CyberpunkCharacters.Read(root)).Name);
    }

    [Fact]
    public void The_timestamp_is_parsed_day_first_with_an_unpadded_day()
    {
        // The trap. "8.07.2025" is 8 July. DateTime.Parse on a US machine reads it as 7 August and
        // never fails - a whole month wrong, silently, on a field used for sorting.
        Assert.Equal(new DateTime(2025, 7, 8, 23, 36, 47),
            CyberpunkCharacters.ParseTimestamp("23:36:47, 8.07.2025"));
        Assert.Equal(new DateTime(2025, 7, 21, 18, 8, 48),
            CyberpunkCharacters.ParseTimestamp("18:08:48, 21.07.2025"));

        Assert.Null(CyberpunkCharacters.ParseTimestamp("not a timestamp"));
        Assert.Null(CyberpunkCharacters.ParseTimestamp(null));
    }

    [Fact]
    public void A_save_with_no_payload_is_listed_and_labelled_rather_than_hidden_or_thrown_on()
    {
        // Two of ninety-three real folders have metadata and a screenshot but no sav.dat. A scanner
        // that assumes the triple throws on a real user's real directory today.
        var root = TestSupport.TempDir("cp-orphan-");
        Save(root, "ManualSave-21", Meta("ManualSave-21", "StreetKid", 30, 20, "22:54:00, 27.10.2022"),
             payload: false);

        var c = Assert.Single(CyberpunkCharacters.Read(root));
        Assert.Contains("incomplete", c.Progress);
    }

    [Fact]
    public void An_absent_isModded_is_unknown_and_never_false()
    {
        // The flag only exists from patch 1.6 onward. Rendering "made without mods" for a 2020 save
        // would be inventing a fact about somebody's playthrough.
        var root = TestSupport.TempDir("cp-nomod-");
        Save(root, "AutoSave-0", """
        { "Data": { "metadata": { "name": "AutoSave-0", "lifePath": "Nomad", "level": 12.0,
          "streetCred": 3.0, "timestampString": "01:02:03, 4.05.2020" } } }
        """);

        Assert.Null(Assert.Single(CyberpunkCharacters.Read(root)).MadeWithMods);
    }

    [Fact]
    public void Saves_come_back_newest_first()
    {
        var root = TestSupport.TempDir("cp-order-");
        Save(root, "Old", Meta("Old", "Corpo", 5, 1, "10:00:00, 1.01.2021"));
        Save(root, "New", Meta("New", "Corpo", 50, 40, "10:00:00, 1.01.2025"));
        Save(root, "Middle", Meta("Middle", "Corpo", 25, 20, "10:00:00, 1.01.2023"));

        Assert.Equal(new[] { "New", "Middle", "Old" },
            CyberpunkCharacters.Read(root).Select(c => c.Id).ToArray());
    }

    [Fact]
    public void The_playthrough_id_is_what_groups_ninety_three_saves_into_two_characters()
    {
        var root = TestSupport.TempDir("cp-group-");
        var a = Save(root, "S1", Meta("S1", "Nomad", 10, 5, "10:00:00, 1.01.2024", playthrough: "aaa"));
        var b = Save(root, "S2", Meta("S2", "Nomad", 20, 9, "10:00:00, 2.01.2024", playthrough: "aaa"));
        var c = Save(root, "S3", Meta("S3", "Corpo", 30, 9, "10:00:00, 3.01.2024", playthrough: "bbb"));

        Assert.Equal("aaa", CyberpunkCharacters.PlaythroughIdOf(a));
        Assert.Equal("aaa", CyberpunkCharacters.PlaythroughIdOf(b));
        Assert.Equal("bbb", CyberpunkCharacters.PlaythroughIdOf(c));
    }

    [Fact]
    public void The_metadata_filename_is_globbed_because_the_number_is_a_format_version()
    {
        var root = TestSupport.TempDir("cp-glob-");
        var dir = Path.Combine(root, "Future");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "metadata.11.json"),
            Meta("Future", "Nomad", 60, 50, "10:00:00, 1.01.2027"));
        File.WriteAllText(Path.Combine(dir, "sav.dat"), "x");

        Assert.Single(CyberpunkCharacters.Read(root));
    }

    [Fact]
    public void One_unreadable_folder_does_not_cost_the_user_the_other_ninety_two()
    {
        var root = TestSupport.TempDir("cp-bad-");
        Save(root, "Good", Meta("Good", "Nomad", 10, 5, "10:00:00, 1.01.2024"));
        var bad = Path.Combine(root, "Broken");
        Directory.CreateDirectory(bad);
        File.WriteAllText(Path.Combine(bad, "metadata.9.json"), "{ not json");

        Assert.Equal("Good", Assert.Single(CyberpunkCharacters.Read(root)).Id);
    }

    [Fact]
    public void A_missing_or_empty_folder_is_no_characters_and_never_a_throw()
    {
        Assert.Empty(CyberpunkCharacters.Read(Path.Combine(TestSupport.TempDir("cp-none-"), "nope")));
        Assert.Empty(CyberpunkCharacters.Read(TestSupport.TempDir("cp-empty-")));
        Assert.Empty(CyberpunkCharacters.Read(""));
    }

    [Fact]
    public void The_display_lines_never_come_back_blank()
    {
        var bare = new SaveCharacter("id", "V", "", "", null);
        Assert.Equal("V", bare.Headline);
        Assert.Equal("", bare.Detail);

        var full = new SaveCharacter("id", "V", "Nomad", "Level 47", new DateTime(2025, 7, 21, 18, 8, 0));
        Assert.Equal("V  ·  Nomad", full.Headline);
        Assert.Equal("Level 47  ·  2025-07-21 18:08", full.Detail);
    }
}

/// <summary>
/// Ninety-three saves are two people. See <see cref="CyberpunkCharacters.ReadCharacters"/>.
/// </summary>
public class CyberpunkPlaythroughGroupingTests
{
    private static void Save(string root, string folder, string playthrough, int level, string stamp)
    {
        var dir = Path.Combine(root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "metadata.9.json"), $$"""
        { "Data": { "metadata": { "name": "{{folder}}", "lifePath": "Nomad", "level": {{level}}.0,
          "streetCred": 40.0, "timestampString": "{{stamp}}", "playthroughID": "{{playthrough}}" } } }
        """);
        File.WriteAllText(Path.Combine(dir, "sav.dat"), "x");
    }

    [Fact]
    public void Many_saves_of_one_playthrough_are_one_character()
    {
        var root = TestSupport.TempDir("cp-fold-");
        Save(root, "AutoSave-5", "aaa", 47, "10:00:00, 1.01.2025");
        Save(root, "AutoSave-6", "aaa", 48, "10:05:00, 1.01.2025");
        Save(root, "AutoSave-7", "aaa", 48, "10:10:00, 1.01.2025");
        Save(root, "ManualSave-1", "bbb", 12, "10:00:00, 1.01.2023");

        var chars = CyberpunkCharacters.ReadCharacters(root);

        Assert.Equal(2, chars.Count);
        var newest = chars[0];
        Assert.Equal("aaa", newest.Id);
        Assert.Equal("V", newest.Name);
        Assert.Contains("Level 48", newest.Progress);   // from that playthrough's NEWEST save
        Assert.Contains("3 saves", newest.Progress);    // and nothing is hidden
    }

    [Fact]
    public void A_save_with_no_playthrough_id_stands_alone_rather_than_joining_someone_else()
    {
        // Guessing that two unidentified saves are the same person is what makes a list untrustworthy.
        var root = TestSupport.TempDir("cp-lone-");
        Save(root, "Known", "aaa", 10, "10:00:00, 1.01.2025");
        var dir = Path.Combine(root, "Anonymous");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "metadata.9.json"),
            """
            { "Data": { "metadata": { "name": "Anonymous", "lifePath": "Corpo", "level": 5.0,
              "timestampString": "10:00:00, 1.01.2024" } } }
            """);
        File.WriteAllText(Path.Combine(dir, "sav.dat"), "x");

        Assert.Equal(2, CyberpunkCharacters.ReadCharacters(root).Count);
    }
}

/// <summary>A character's stats must come from a save that would actually load.</summary>
public class CyberpunkRepresentativeSaveTests
{
    private static void Save(string root, string folder, string stamp, int level, bool payload)
    {
        var dir = Path.Combine(root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "metadata.9.json"), $$"""
        { "Data": { "metadata": { "name": "{{folder}}", "lifePath": "StreetKid", "level": {{level}}.0,
          "streetCred": 50.0, "timestampString": "{{stamp}}", "playthroughID": "one" } } }
        """);
        if (payload) File.WriteAllText(Path.Combine(dir, "sav.dat"), "x");
    }

    [Fact]
    public void An_orphaned_newest_save_does_not_make_the_whole_character_read_as_broken()
    {
        // Straight from the real install: one playthrough's most recent save has no sav.dat, and
        // reading its stats made the character row say "incomplete" - describing a file while
        // appearing to describe a person. The stats come from the newest LOADABLE save instead, and
        // the broken ones are counted rather than hidden.
        var root = TestSupport.TempDir("cp-rep-");
        Save(root, "Working", "10:00:00, 1.01.2022", 39, payload: true);
        Save(root, "Orphan", "22:54:00, 27.10.2022", 99, payload: false);

        var c = Assert.Single(CyberpunkCharacters.ReadCharacters(root));

        Assert.Contains("Level 39", c.Progress);          // the loadable one, not the orphan's 99
        Assert.Contains("2 saves (1 incomplete)", c.Progress);
        Assert.DoesNotContain("·  incomplete", c.Progress);
    }

    [Fact]
    public void When_every_save_is_broken_the_character_still_appears()
    {
        // Falling back to nothing would hide a character the user definitely still has.
        var root = TestSupport.TempDir("cp-allbad-");
        Save(root, "A", "10:00:00, 1.01.2022", 12, payload: false);

        var c = Assert.Single(CyberpunkCharacters.ReadCharacters(root));
        Assert.Contains("1 save (1 incomplete)", c.Progress);
    }
}
