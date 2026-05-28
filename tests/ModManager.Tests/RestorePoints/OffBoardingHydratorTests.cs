using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class OffBoardingHydratorTests
{
    [Fact]
    public void Hydrate_maps_archive_to_report()
    {
        var ga = new GameArchive("t", "T", @"D:\T", "vanilla",
            Array.Empty<LaunchTarget>(), null,
            new[] { new FrameworkArchive("elm", "Elden Mod Loader", "TechieW", @"D:\T", new[] { "dinput8.dll" }, "frameworks-state/elm") },
            Array.Empty<LoaderModState>(),
            new[] { new OwnedModNote("VortexMod", "Vortex") },
            Array.Empty<MovedFile>(),
            new[] { new ArchivedMod("CoolMod", true, "https://nexusmods.com/x", "fingerprint", "2026-04-02T00:00:00Z") },
            null);

        var report = OffBoardingHydrator.Hydrate(ga, @"C:\rp\20260528-141233", launchLines: new[] { "Launch with X" });

        Assert.Equal("T", report.GameName);
        Assert.Equal(@"C:\rp\20260528-141233", report.RestorePointPath);
        Assert.Contains("Launch with X", report.LaunchLines);
        Assert.Contains("Elden Mod Loader (by TechieW)", report.Frameworks);
        Assert.Contains(report.Mods, m => m.Name == "CoolMod" && m.SourceUrl == "https://nexusmods.com/x"
            && m.SourceConfidence == "fingerprint" && m.InstalledDate == "2026-04-02");
        Assert.Contains(report.OwnedMods, o => o.Name == "VortexMod" && o.ManagedBy == "Vortex");
    }

    [Fact]
    public void Hydrate_null_installedUtc_yields_null_date()
    {
        var ga = new GameArchive("t", "T", @"D:\T", "vanilla",
            Array.Empty<LaunchTarget>(), null, Array.Empty<FrameworkArchive>(), Array.Empty<LoaderModState>(),
            Array.Empty<OwnedModNote>(), Array.Empty<MovedFile>(),
            new[] { new ArchivedMod("Sideload", false, null, null, null) }, null);

        var report = OffBoardingHydrator.Hydrate(ga, @"C:\rp\x", Array.Empty<string>());
        var line = Assert.Single(report.Mods);
        Assert.Null(line.InstalledDate);
        Assert.Null(line.SourceUrl);
    }
}
