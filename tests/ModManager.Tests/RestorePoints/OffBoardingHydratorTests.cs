using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class OffBoardingHydratorTests
{
    // Shared helpers -------------------------------------------------------

    private static GameArchive VanillaArchive() =>
        new("t", "T", @"D:\T", "vanilla",
            Array.Empty<LaunchTarget>(), null,
            new[] { new FrameworkArchive("elm", "Elden Mod Loader", "TechieW", @"D:\T", new[] { "dinput8.dll" }, "frameworks-state/elm") },
            Array.Empty<LoaderModState>(),
            new[] { new OwnedModNote("VortexMod", "Vortex") },
            Array.Empty<MovedFile>(),
            new[] { new ArchivedMod("CoolMod", true, "https://nexusmods.com/x", "fingerprint", "2026-04-02T00:00:00Z") },
            null);

    private static GameArchive ModsActiveArchive() =>
        new("t", "T", @"D:\T", "modsActive",
            new[]
            {
                new LaunchTarget("Play (Seamless Co-op)", "exe", @"Game\sc\launch.exe") { IsDefault = true }
            },
            "seamlesscoop",
            Array.Empty<FrameworkArchive>(),
            Array.Empty<LoaderModState>(),
            Array.Empty<OwnedModNote>(),
            Array.Empty<MovedFile>(),
            Array.Empty<ArchivedMod>(),
            null);

    // Tests ----------------------------------------------------------------

    [Fact]
    public void Hydrate_vanilla_maps_archive_fields_and_derives_vanilla_launch_line()
    {
        var report = OffBoardingHydrator.Hydrate(VanillaArchive(), @"C:\rp\20260528-141233");

        Assert.Equal("T", report.GameName);
        Assert.Equal(@"C:\rp\20260528-141233", report.RestorePointPath);

        // Derived launch line — vanilla branch. An archive with no remainder sweep (and a Vortex mod left)
        // never claims a clean vanilla game: its mod folders were not cleared.
        Assert.Single(report.LaunchLines);
        Assert.DoesNotContain("returned to vanilla", report.LaunchLines[0]);
        Assert.Contains("may not be fully vanilla", report.LaunchLines[0]);
        var clean = OffBoardingHydrator.Hydrate(VanillaArchive() with
        {
            OwnedMods = Array.Empty<OwnedModNote>(),
            VanillaRemainder = Array.Empty<MovedFile>(),
            LeftInPlace = Array.Empty<InPlaceNote>(),
        }, "rp");
        Assert.Contains("returned to vanilla", Assert.Single(clean.LaunchLines));

        // Frameworks / mods / owned mods pass through unchanged
        Assert.Contains("Elden Mod Loader (by TechieW)", report.Frameworks);
        Assert.Contains(report.Mods, m => m.Name == "CoolMod" && m.SourceUrl == "https://nexusmods.com/x"
            && m.SourceConfidence == "fingerprint" && m.InstalledDate == "2026-04-02");
        Assert.Contains(report.OwnedMods, o => o.Name == "VortexMod" && o.ManagedBy == "Vortex");
    }

    [Fact]
    public void Hydrate_modsActive_derives_exe_launch_line_and_required_launcher_warning()
    {
        var report = OffBoardingHydrator.Hydrate(ModsActiveArchive(), @"C:\rp\20260528-141233");

        // Should have two lines: the exe target + the required-launcher warning
        Assert.Equal(2, report.LaunchLines.Count);
        Assert.Contains(report.LaunchLines, l => l.Contains("Launch with: Play (Seamless Co-op)"));
        Assert.Contains(report.LaunchLines, l => l.Contains("seamlesscoop") && l.Contains("mod launcher"));
    }

    [Fact]
    public void Hydrate_passes_save_location_and_backup_count_through()
    {
        var ga = VanillaArchive() with { SaveLocation = @"C:\Users\you\AppData\Roaming\EldenRing\765", SaveBackupCount = 4 };
        var report = OffBoardingHydrator.Hydrate(ga, @"C:\rp\x");
        Assert.Equal(@"C:\Users\you\AppData\Roaming\EldenRing\765", report.SaveLocation);
        Assert.Equal(4, report.SaveBackupCount);
    }

    [Fact]
    public void Hydrate_null_installedUtc_yields_null_date()
    {
        var ga = new GameArchive("t", "T", @"D:\T", "vanilla",
            Array.Empty<LaunchTarget>(), null, Array.Empty<FrameworkArchive>(), Array.Empty<LoaderModState>(),
            Array.Empty<OwnedModNote>(), Array.Empty<MovedFile>(),
            new[] { new ArchivedMod("Sideload", false, null, null, null) }, null);

        var report = OffBoardingHydrator.Hydrate(ga, @"C:\rp\x");
        var line = Assert.Single(report.Mods);
        Assert.Null(line.InstalledDate);
        Assert.Null(line.SourceUrl);
    }

    [Fact]
    public void Hydrate_counts_the_turned_off_set_minus_skips_and_carries_the_data_dir()
    {
        var ga = VanillaArchive() with
        {
            TurnedOffByClear = new[] { new ClearedMod("A", "mods"), new ClearedMod("B", "mods"), new ClearedMod("C", "mods") },
            TurnOffSkipped = new[] { new ClearSkip("B", "locked") },
            DataDir = @"D:\_626mods\t",
        };
        var report = OffBoardingHydrator.Hydrate(ga, @"C:\rp\x");

        Assert.Equal(2, report.TurnedOffCount);
        Assert.Equal(@"D:\_626mods\t", report.HeldInDataDir);
        Assert.Equal("B", Assert.Single(report.TurnOffSkips!).Name);
        Assert.Equal(0, report.CopiedToRestorePoint);   // no heldCopies record

        var copied = OffBoardingHydrator.Hydrate(ga with { HeldCopies = Array.Empty<HeldCopy>() }, "rp");
        Assert.Equal(0, copied.CopiedToRestorePoint);   // an empty record copies nothing
        Assert.Equal(2, copied.InDataFolder);
    }

    [Fact]
    public void Hydrate_leaves_the_turn_off_fields_empty_for_an_old_archive()
    {
        var report = OffBoardingHydrator.Hydrate(VanillaArchive(), @"C:\rp\x");
        Assert.Equal(0, report.TurnedOffCount);
        Assert.Null(report.TurnOffSkips);
    }
}
