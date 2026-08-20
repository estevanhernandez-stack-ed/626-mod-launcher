using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// The sentence a Restore confirm shows before replacing everything in a save folder. In Core and
/// pure because <b>a confirmation that misreports what it will destroy is worse than one that says
/// nothing</b> — and built inline in a click handler it would be untestable and would drift the first
/// time the panel changed.
/// </summary>
public class SaveFolderSummaryTests
{
    [Fact]
    public void Worlds_are_what_a_player_of_a_worlds_game_is_deciding_about()
    {
        // A worlds-shaped folder also holds loose files - GlobalPalStorage.sav and friends - which are
        // real and are not what anyone weighs before pressing Replace. "74 save files" is technically
        // true and useless.
        Assert.Equal("2 worlds, 25.4 MB", SaveFolderSummary.Describe(worlds: 2, files: 74, bytes: 26_633_011));
    }

    [Fact]
    public void Files_are_the_answer_when_there_are_no_worlds()
        => Assert.Equal("3 save files, 12.5 KB", SaveFolderSummary.Describe(worlds: 0, files: 3, bytes: 12_800));

    [Theory]
    [InlineData(1, 0, "1 world")]
    [InlineData(2, 0, "2 worlds")]
    public void One_of_a_thing_is_not_pluralised(int worlds, int files, string expected)
        => Assert.StartsWith(expected, SaveFolderSummary.Describe(worlds, files, 1024));

    [Fact]
    public void One_save_file_is_not_pluralised_either()
        => Assert.StartsWith("1 save file,", SaveFolderSummary.Describe(0, 1, 1024));

    [Fact]
    public void An_empty_folder_reads_like_a_sentence_and_not_like_a_bug()
    {
        // Never "0 files, 0 B". It is also a hint that the folder may be the wrong one, which is the
        // other thing a person should be thinking at that moment.
        Assert.Equal("an empty save folder", SaveFolderSummary.Describe(0, 0, 0));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(26_633_011, "25.4 MB")]
    [InlineData(2_147_483_648, "2 GB")]
    public void Sizes_are_readable_at_every_scale(long bytes, string expected)
    {
        // A long-running Palworld world passes a gigabyte, and "2048 MB" is a number you have to stop
        // and convert. The saves panel briefly had two formatters and they disagreed exactly there.
        Assert.Equal(expected, SaveFolderSummary.Human(bytes));
    }

    [Fact]
    public void A_negative_size_cannot_happen_and_still_does_not_render_nonsense()
        => Assert.Equal("0 B", SaveFolderSummary.Human(-1));
}
