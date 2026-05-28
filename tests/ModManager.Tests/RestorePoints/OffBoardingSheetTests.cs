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
        OwnedMods: new[] { "VortexA", "VortexB" });

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
}
