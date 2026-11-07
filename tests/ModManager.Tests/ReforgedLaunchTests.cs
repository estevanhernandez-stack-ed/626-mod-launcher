using ModManager.Core;

namespace ModManager.Tests;

/// <summary>
/// Reforged installs outside the game folder and starts through its own launcher (save-type offer, decision 3).
/// The fixture is laid out the way Este's D:\ERRv2.1.2.3 is (measured 2026-11-07): numbered .BAT files at the
/// root calling internals\launcher\ReforgedLauncher.exe, alt-profile .BATs two levels down, and
/// mod\altsaves.toml naming the save extension.
/// </summary>
public class ReforgedLaunchTests : IDisposable
{
    private readonly string _root = TestSupport.TempDir("mmb-reforged-");
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Exe => Path.Combine(_root, "internals", "launcher", "ReforgedLauncher.exe");
    private string MainBat => Path.Combine(_root, "2 - Launch ELDEN RING Reforged - Offline or Seamless (Windows).BAT");
    private string AltBat => Path.Combine(_root, "extras", "Alt Save Profiles - Windows", "Alt Save Profile 1 (Offline).BAT");

    private void Lay(string? toml)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Exe)!);
        File.WriteAllText(Exe, "x");
        File.WriteAllText(MainBat, "@ECHO OFF\r\nif exist \".\\internals\\launcher\\ReforgedLauncher.exe\" GOTO launch\r\n:launch\r\n.\\internals\\launcher\\ReforgedLauncher.exe --offline\r\n");
        Directory.CreateDirectory(Path.GetDirectoryName(AltBat)!);
        File.WriteAllText(AltBat, "..\\..\\internals\\launcher\\ReforgedLauncher.exe --alt-save-profile-1 --offline\r\n");
        if (toml is null) return;
        Directory.CreateDirectory(Path.Combine(_root, "mod"));
        File.WriteAllText(Path.Combine(_root, "mod", "altsaves.toml"), toml);
    }

    private const string Both = "# Extension you want to use for the save files\nextension = \".err\"\n\n# while playing with seamless coop enabled.\nseamless_extension = \".err\"\n";

    private static LaunchTarget Target(string path, string? args = null) => new("Reforged", "exe", path) { Args = args };

    [Fact]
    public void Exe_target_reads_the_toml_extension()
    {
        Lay(Both);
        var i = ReforgedLaunch.Inspect(Target(Exe))!;
        Assert.Equal(".err", i.SaveExtension);
        Assert.False(i.AltProfile);
        Assert.Equal(Path.GetFullPath(_root), Path.GetFullPath(i.Root));
    }

    [Fact]
    public void Bat_target_finds_its_launcher()
    {
        Lay(Both);
        Assert.Equal(".err", ReforgedLaunch.Inspect(Target(MainBat))!.SaveExtension);
    }

    [Fact]
    public void Alt_profile_bat_is_flagged_and_still_finds_the_root()
    {
        Lay(Both);
        var i = ReforgedLaunch.Inspect(Target(AltBat))!;
        Assert.True(i.AltProfile);
        Assert.Equal(Path.GetFullPath(_root), Path.GetFullPath(i.Root));
    }

    [Fact]
    public void Alt_profile_in_exe_args_is_flagged()
    {
        Lay(Both);
        Assert.True(ReforgedLaunch.Inspect(Target(Exe, "--alt-save-profile-2"))!.AltProfile);
    }

    [Fact]
    public void Differing_extensions_are_ambiguous()
    {
        Lay("extension = \".err\"\nseamless_extension = \".co2\"\n");
        Assert.Null(ReforgedLaunch.Inspect(Target(Exe))!.SaveExtension);
    }

    [Fact]
    public void Missing_toml_falls_back_to_err()
    {
        Lay(null);
        Assert.Equal(".err", ReforgedLaunch.Inspect(Target(Exe))!.SaveExtension);
    }

    [Fact]
    public void Custom_and_dotless_extensions_are_honored()
    {
        Lay("extension = \"rfg\"\nseamless_extension = \".rfg\"\n");
        Assert.Equal(".rfg", ReforgedLaunch.Inspect(Target(Exe))!.SaveExtension);
    }

    [Fact]
    public void Other_targets_are_not_reforged()
    {
        Assert.Null(ReforgedLaunch.Inspect(new LaunchTarget("S", "steam", "steam://rungameid/1245620")));
        Assert.Null(ReforgedLaunch.Inspect(Target(@"C:\g\Game\ersc_launcher.exe")));
        var otherBat = Path.Combine(_root, "other.bat");
        File.WriteAllText(otherBat, "start something.exe");
        Assert.Null(ReforgedLaunch.Inspect(Target(otherBat)));
    }

    [Fact]
    public void Reforged_launchers_count_as_mod_launchers_so_vanilla_never_picks_them()
    {
        Lay(Both);
        Assert.True(LaunchTargetPicker.IsModLauncher(Exe));
        Assert.True(LaunchTargetPicker.IsModLauncher(MainBat));
        Assert.False(LaunchTargetPicker.IsModLauncher(@"C:\g\Game\eldenring.exe"));
    }

    [Fact]
    public void Vanilla_never_resolves_to_a_reforged_bat()
    {
        Lay(Both);
        var game = new GameEntry
        {
            Id = "er", GameName = "ELDEN RING", Engine = "fromsoft",
            LaunchTargets = new[] { new LaunchTarget("Reforged", "exe", MainBat) { IsDefault = true } },
        };
        Assert.NotEqual(MainBat, LaunchTargetPicker.Pick(game, LaunchMode.Vanilla, seamlessFullyInstalled: false)?.Target);
    }
}
