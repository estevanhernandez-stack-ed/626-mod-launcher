using ModManager.Core;

namespace ModManager.Tests;

public class PatchDayLibraryLineTests
{
    private static PatchDayRecord Rec(PatchDayChoice c) => new() { BuildId = "200", Choice = c };

    [Fact]
    public void No_record_is_quiet() =>
        Assert.Null(PatchDay.LibraryLine(null, LaunchMode.Modded));

    [Theory]
    [InlineData(PatchDayChoice.None, LaunchMode.Modded)]
    [InlineData(PatchDayChoice.None, LaunchMode.Vanilla)]
    [InlineData(PatchDayChoice.Modded, LaunchMode.Modded)]
    public void Undecided_or_modded_says_updated_since_last_modded_launch(PatchDayChoice c, LaunchMode m) =>
        Assert.Equal("Updated since your last modded launch", PatchDay.LibraryLine(Rec(c), m));

    [Fact]
    public void Vanilla_choice_in_vanilla_mode_says_playing_vanilla() =>
        Assert.Equal("Playing vanilla since the patch", PatchDay.LibraryLine(Rec(PatchDayChoice.Vanilla), LaunchMode.Vanilla));

    [Fact]
    public void Vanilla_choice_with_mods_back_in_place_is_quiet() =>
        Assert.Null(PatchDay.LibraryLine(Rec(PatchDayChoice.Vanilla), LaunchMode.Modded));
}
