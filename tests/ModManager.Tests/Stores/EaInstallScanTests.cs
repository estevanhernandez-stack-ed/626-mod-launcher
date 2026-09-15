using ModManager.Core;
using ModManager.Core.Stores;

namespace ModManager.Tests.Stores;

public class EaInstallScanTests
{
    private static string Xml(string contentId, string title, string version = "1.0.0.1") =>
        EaInstallerDataTests.Sample
            .Replace("16425899", contentId)
            .Replace(">EA SPORTS College Football 27<", ">" + title + "<")
            .Replace("1.0.140.17622", version);

    private static Func<string, string?> Files(Dictionary<string, string> byPath)
        => p => byPath.TryGetValue(p, out var t) ? t : null;

    [Fact]
    public void An_install_with_readable_installerdata_becomes_an_installed_game()
    {
        var dir = Path.Combine("C:", "EA Games", "Madden NFL 27");
        var games = EaInstallScan.Scan(
            new[] { new EaRegistryInstall("Madden NFL 27", dir + Path.DirectorySeparatorChar) },
            Files(new() { [Path.Combine(dir, "__Installer", "installerdata.xml")] = Xml("16425895", "Madden NFL 27", "1.0.139.61898") }));

        var g = Assert.Single(games);
        Assert.Equal("ea", g.StoreKind);
        Assert.Equal("16425895", g.AppId);
        Assert.Equal("Madden NFL 27", g.Name);
        Assert.Equal(dir, g.InstallDir);                 // trailing separator trimmed
        Assert.Equal("1.0.139.61898", g.BuildId);
    }

    [Fact]
    public void A_staged_or_foreign_install_is_skipped()
    {
        var games = EaInstallScan.Scan(
            new[]
            {
                new EaRegistryInstall("No folder", null),
                new EaRegistryInstall("No installerdata", Path.Combine("C:", "EA Games", "Half")),
                new EaRegistryInstall("Garbage", Path.Combine("C:", "EA Games", "Bad")),
            },
            Files(new() { [Path.Combine("C:", "EA Games", "Bad", "__Installer", "installerdata.xml")] = "<nope/>" }));

        Assert.Empty(games);
    }

    [Fact]
    public void The_same_folder_seen_twice_is_listed_once()
    {
        // The 64- and 32-bit registry views can both carry the same game.
        var dir = Path.Combine("C:", "EA Games", "X");
        var entries = new[] { new EaRegistryInstall("X", dir), new EaRegistryInstall("X", dir.ToUpperInvariant()) };
        var games = EaInstallScan.Scan(entries,
            p => p.EndsWith("installerdata.xml", StringComparison.OrdinalIgnoreCase) ? Xml("1", "X") : null);

        Assert.Single(games);
    }

    [Fact]
    public void A_missing_title_falls_back_to_the_registry_display_name()
    {
        var dir = Path.Combine("C:", "EA Games", "Y");
        var noTitle = "<DiPManifest version=\"4.0\"><contentIDs><contentID>7</contentID></contentIDs></DiPManifest>";
        var g = Assert.Single(EaInstallScan.Scan(new[] { new EaRegistryInstall("Display Y", dir) }, _ => noTitle));
        Assert.Equal("Display Y", g.Name);
    }
}
