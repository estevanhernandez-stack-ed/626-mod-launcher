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

        // Derived launch line — vanilla branch
        Assert.Single(report.LaunchLines);
        Assert.Contains("returned to vanilla", report.LaunchLines[0]);

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
}
