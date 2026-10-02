using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class OffBoardingSheetTests
{
    private static OffBoardingReport Report() => new(
        GameName: "ELDEN RING",
        RestorePointPath: @"C:\Users\you\AppData\Roaming\ModManagerBuilder\restore-points\20260528-141233",
        LaunchLines: new[] { "Seamless Co-op is still installed. Launch with:", @"  D:\ELDEN RING\Game\sc\launch.exe", "  Do NOT launch from Steam directly while Seamless Co-op is installed." },
        Frameworks: new[] { "Elden Mod Loader (by TechieW)" },
        Mods: new[]
        {
            new OffBoardingModLine("KnownMod", "https://nexusmods.com/x", "fingerprint", "2026-04-02"),
            new OffBoardingModLine("GuessMod", "https://nexusmods.com/y", "nameSearch", null),
            new OffBoardingModLine("SideloadMod", null, null, null),
        },
        OwnedMods: new[] { new OffBoardingOwnedMod("VortexA", "Vortex"), new OffBoardingOwnedMod("VortexB", "Vortex") });

    [Fact]
    public void Render_leads_with_preservation_and_lists_launch_and_sources()
    {
        var s = OffBoardingSheet.Render(Report());

        Assert.Contains("Your mods are preserved", s);
        Assert.Contains("20260528-141233", s);
        Assert.Contains("Launch with:", s);
        Assert.Contains("Elden Mod Loader (by TechieW)", s);
        Assert.Contains("source: https://nexusmods.com/x", s);
        Assert.Contains("likely source: https://nexusmods.com/y", s);
        Assert.Contains("source not recorded", s);
        Assert.Contains("Managed by Vortex", s);
        Assert.Contains("VortexA", s);
        Assert.Contains("installed 2026-04-02", s);
    }

    [Fact]
    public void Render_never_emits_a_nexus_account_or_key()
    {
        var s = OffBoardingSheet.Render(Report());
        Assert.DoesNotContain("apiKey", s, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", s);
    }

    [Fact]
    public void Render_touches_no_filesystem()
    {
        var before = Directory.GetCurrentDirectory();
        var s = OffBoardingSheet.Render(Report());
        Assert.False(string.IsNullOrWhiteSpace(s));
        Assert.Equal(before, Directory.GetCurrentDirectory());
    }

    [Fact]
    public void Render_names_the_correct_manager_for_each_owned_mod()
    {
        var r = Report() with { OwnedMods = new[] { new OffBoardingOwnedMod("VortA", "Vortex"), new OffBoardingOwnedMod("Mo2A", "Mo2") } };
        var s = OffBoardingSheet.Render(r);
        Assert.Contains("Managed by Vortex (1): VortA", s);
        Assert.Contains("Managed by Mo2 (1): Mo2A", s);
        Assert.DoesNotContain("Managed by Vortex (1): Mo2A", s);   // no cross-labeling
    }

    [Fact]
    public void Render_uses_plain_source_when_url_known_but_confidence_null()
    {
        var r = Report() with { Mods = new[] { new OffBoardingModLine("KnownNoConf", "https://nexusmods.com/z", null, null) } };
        var s = OffBoardingSheet.Render(r);
        Assert.Contains("source: https://nexusmods.com/z", s);
        Assert.DoesNotContain("likely source: https://nexusmods.com/z", s);
    }

    [Fact]
    public void Render_reassures_saves_are_untouched_and_names_the_location()
    {
        var r = Report() with { SaveLocation = @"C:\Users\you\AppData\Roaming\EldenRing\76561198", SaveBackupCount = 3 };
        var s = OffBoardingSheet.Render(r);
        Assert.Contains("YOUR SAVES", s);
        Assert.Contains("not touched", s);                                          // the load-bearing reassurance
        Assert.Contains(@"C:\Users\you\AppData\Roaming\EldenRing\76561198", s);      // names where they are
        Assert.Contains("3", s);                                                    // backup count surfaced
    }

    [Fact]
    public void Render_handles_unknown_save_location_without_a_path_or_zero_backups()
    {
        var r = Report() with { SaveLocation = null, SaveBackupCount = 0 };
        var s = OffBoardingSheet.Render(r);
        Assert.Contains("YOUR SAVES", s);
        Assert.Contains("not touched", s);   // still reassures even when the launcher tracked no save folder
    }

    [Fact]
    public void Render_says_how_many_mods_were_turned_off_where_they_are_held_and_that_restore_returns_them()
    {
        var r = Report() with
        {
            TurnedOffCount = 12,
            HeldInDataDir = @"D:\SteamLibrary\_626mods\elden-ring",
            TurnOffSkips = new[] { new ClearSkip("StubbornMod", "an earlier turned-off copy of it is already held") },
        };
        var s = OffBoardingSheet.Render(r);

        Assert.Contains("MODS TURNED OFF", s);
        Assert.Contains("turned off 12 mods", s);
        Assert.Contains(@"D:\SteamLibrary\_626mods\elden-ring", s);
        Assert.Contains("turns exactly those 12 back on", s);
        // Without a copy in the restore point the data folder is the only copy, and the sheet says to keep it.
        Assert.Contains("keep that folder", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StubbornMod", s);
        Assert.Contains("still active", s);
        Assert.Contains("an earlier turned-off copy of it is already held", s);
    }

    [Fact]
    public void Render_says_the_restore_point_holds_copies_when_it_does_and_never_says_dont_delete()
    {
        var r = Report() with
        {
            TurnedOffCount = 3,
            HeldInDataDir = @"D:\SteamLibrary\_626mods\elden-ring",
            TurnedOffModsCopied = true,
        };
        var s = OffBoardingSheet.Render(r);

        Assert.Contains("copies of them are saved in your restore point", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("turns exactly those 3 back on", s);
        Assert.DoesNotContain("don't delete", s, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("keep that folder", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_has_no_turned_off_section_without_the_record()
    {
        var s = OffBoardingSheet.Render(Report());
        Assert.DoesNotContain("MODS TURNED OFF", s);
    }
}
