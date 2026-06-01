using ModManager.Core.Frameworks;

namespace ModManager.Tests.Frameworks;

// #110: surface "how to use" for an installed framework, read LIVE from the installed files (truthful
// to the user's actual config), not a static blurb. FrameworkUsage.Describe(frameworkId, installPath)
// reads the on-disk settings and returns a structured how-to the App renders in a toast.
public class FrameworkUsageTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "fw-usage-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    // Build a UE4SS install layout under installRoot/ue4ss with the given settings.ini body.
    private string MakeUe4ssInstall(string settingsIni)
    {
        var installRoot = Path.Combine(_tmp, "R5", "Binaries", "Win64");
        var ue = Path.Combine(installRoot, "ue4ss");
        Directory.CreateDirectory(Path.Combine(ue, "Mods"));
        File.WriteAllText(Path.Combine(ue, "UE4SS-settings.ini"), settingsIni);
        return installRoot;
    }

    private const string RealisticIni = """
        [General]
        EnableHotReloadSystem = 0
        HotReloadKey = R

        [Debug]
        ConsoleEnabled = 0
        GuiConsoleEnabled = 0
        """;

    [Fact]
    public void Describe_ue4ss_reads_the_real_hot_reload_key()
    {
        var installRoot = MakeUe4ssInstall(RealisticIni);

        var usage = FrameworkUsage.Describe("ue4ss", installRoot);

        Assert.Equal("UE4SS", usage.DisplayName);
        // Hot-reload is "Ctrl + <HotReloadKey>" — CTRL is always required per UE4SS.
        Assert.Contains(usage.Lines, l => l.Contains("Ctrl + R"));
    }

    [Fact]
    public void Describe_ue4ss_reports_console_off_when_both_console_flags_are_zero()
    {
        var installRoot = MakeUe4ssInstall(RealisticIni);

        var usage = FrameworkUsage.Describe("ue4ss", installRoot);

        // Console disabled in the user's settings -> tell them it's off + how to turn it on.
        Assert.Contains(usage.Lines, l => l.Contains("Console", StringComparison.OrdinalIgnoreCase)
                                          && l.Contains("off", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Describe_ue4ss_reports_console_on_when_a_console_flag_is_enabled()
    {
        var installRoot = MakeUe4ssInstall("""
            [General]
            HotReloadKey = F5

            [Debug]
            ConsoleEnabled = 1
            GuiConsoleEnabled = 0
            """);

        var usage = FrameworkUsage.Describe("ue4ss", installRoot);

        Assert.Contains(usage.Lines, l => l.Contains("Ctrl + F5"));
        Assert.Contains(usage.Lines, l => l.Contains("Console", StringComparison.OrdinalIgnoreCase)
                                          && l.Contains("on", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Describe_ue4ss_points_at_the_real_mods_folder()
    {
        var installRoot = MakeUe4ssInstall(RealisticIni);

        var usage = FrameworkUsage.Describe("ue4ss", installRoot);

        // The mods path is the truthful resolved one under the install root.
        var expectedMods = Path.Combine(installRoot, "ue4ss", "Mods");
        Assert.Contains(usage.Lines, l => l.Contains(expectedMods));
    }

    [Fact]
    public void Describe_ue4ss_includes_the_no_exe_note_and_docs_link()
    {
        var installRoot = MakeUe4ssInstall(RealisticIni);

        var usage = FrameworkUsage.Describe("ue4ss", installRoot);

        // UE4SS is a DLL proxy — loads with the game, no separate exe to run (the friend's question).
        Assert.Contains(usage.Lines, l => l.Contains("game", StringComparison.OrdinalIgnoreCase)
                                          && l.Contains("no", StringComparison.OrdinalIgnoreCase)
                                          && l.Contains("exe", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("https://docs.ue4ss.com", usage.DocsUrl);
    }

    [Fact]
    public void Describe_ue4ss_degrades_gracefully_when_settings_missing()
    {
        // Installed but settings.ini absent (or unreadable) -> still give the generic how-to + docs,
        // never throw.
        var installRoot = Path.Combine(_tmp, "R5", "Binaries", "Win64");
        Directory.CreateDirectory(Path.Combine(installRoot, "ue4ss", "Mods"));

        var usage = FrameworkUsage.Describe("ue4ss", installRoot);

        Assert.Equal("UE4SS", usage.DisplayName);
        Assert.NotEmpty(usage.Lines);                 // generic guidance still present
        Assert.Equal("https://docs.ue4ss.com", usage.DocsUrl);
    }

    [Fact]
    public void Describe_unknown_framework_returns_generic_installed_note()
    {
        var installRoot = Path.Combine(_tmp, "somewhere");
        Directory.CreateDirectory(installRoot);

        var usage = FrameworkUsage.Describe("some-other-framework", installRoot);

        // No framework-specific reader -> generic "installed at <path>" so the toast still says something.
        Assert.Contains(usage.Lines, l => l.Contains(installRoot));
        Assert.Null(usage.DocsUrl);                   // no docs link we can vouch for
    }
}
