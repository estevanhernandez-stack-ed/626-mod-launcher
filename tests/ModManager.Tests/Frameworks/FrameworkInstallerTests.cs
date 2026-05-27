using System.IO.Compression;
using ModManager.Core.Frameworks;

namespace ModManager.Tests.Frameworks;

public class FrameworkInstallerTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "fw-install-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private string MakeGameRoot()
    {
        var root = Path.Combine(_tmp, "GameRoot");
        Directory.CreateDirectory(root);
        return root;
    }

    private string MakeGameData()
    {
        var dir = Path.Combine(_tmp, "GameData");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string BuildZip(params (string Path, byte[] Bytes)[] entries)
    {
        var zipPath = Path.Combine(_tmp, $"src-{Guid.NewGuid():n}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using var stream = File.Create(zipPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (path, bytes) in entries)
        {
            var entry = zip.CreateEntry(path);
            using var es = entry.Open();
            es.Write(bytes, 0, bytes.Length);
        }
        return zipPath;
    }

    private static KnownFramework Elm() => new(
        FrameworkId: "elden-mod-loader",
        DisplayName: "Elden Mod Loader",
        Engine: "fromsoft",
        SteamAppId: "1245620",
        GetUrl: "https://www.nexusmods.com/eldenring/mods/117",
        Author: "TechieW",
        ZipFilenameHints: new[] { "elden", "mod", "loader" },
        ZipSignatureFiles: new[] { "dinput8.dll", "mod_loader_config.ini" },
        InstallRoot: "GameRoot",
        ForbiddenPaths: new[] { "eldenring.exe" });

    [Fact]
    public void Install_extracts_files_to_game_root()
    {
        var gameRoot = MakeGameRoot();
        var gameData = MakeGameData();
        var zip = BuildZip(
            ("dinput8.dll", new byte[] { 1, 2, 3 }),
            ("mod_loader_config.ini", new byte[] { 4, 5 }),
            ("ModLoader/some.dll", new byte[] { 6 }));

        var result = FrameworkInstaller.Install(zip, Elm(), gameRoot, gameData);

        Assert.True(File.Exists(Path.Combine(gameRoot, "dinput8.dll")));
        Assert.True(File.Exists(Path.Combine(gameRoot, "mod_loader_config.ini")));
        Assert.True(File.Exists(Path.Combine(gameRoot, "ModLoader", "some.dll")));
        Assert.Equal("elden-mod-loader", result.FrameworkId);
        Assert.Equal(3, result.InstalledFiles.Count);
    }

    [Fact]
    public void Install_writes_camelCase_manifest()
    {
        var gameRoot = MakeGameRoot();
        var gameData = MakeGameData();
        var zip = BuildZip(("dinput8.dll", new byte[] { 1 }), ("mod_loader_config.ini", new byte[] { 2 }));

        FrameworkInstaller.Install(zip, Elm(), gameRoot, gameData);

        var manifestPath = Path.Combine(gameData, "frameworks", "elden-mod-loader", "install.json");
        Assert.True(File.Exists(manifestPath));

        var json = File.ReadAllText(manifestPath);
        Assert.Contains("\"frameworkId\":", json);
        Assert.Contains("\"displayName\":", json);
        Assert.Contains("\"installedFiles\":", json);
        Assert.Contains("\"installedUtc\":", json);
    }

    [Fact]
    public void Install_backs_up_existing_file_before_overwriting()
    {
        var gameRoot = MakeGameRoot();
        var gameData = MakeGameData();
        File.WriteAllBytes(Path.Combine(gameRoot, "dinput8.dll"), new byte[] { 9, 9, 9 });
        var zip = BuildZip(("dinput8.dll", new byte[] { 1 }), ("mod_loader_config.ini", new byte[] { 2 }));

        var result = FrameworkInstaller.Install(zip, Elm(), gameRoot, gameData);

        // Existing file replaced with new bytes.
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(gameRoot, "dinput8.dll")));
        // Backup preserved under the framework's backup subfolder.
        var backupRoot = Path.Combine(gameData, "frameworks", "elden-mod-loader", "backup");
        Assert.True(Directory.Exists(backupRoot));
        var backedUp = Directory.EnumerateFiles(backupRoot, "dinput8.dll", SearchOption.AllDirectories).Single();
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(backedUp));
        Assert.NotNull(result.BackupSnapshotPath);
    }

    [Fact]
    public void Install_refuses_zip_that_contains_a_forbidden_path()
    {
        var gameRoot = MakeGameRoot();
        var gameData = MakeGameData();
        var zip = BuildZip(
            ("dinput8.dll", new byte[] { 1 }),
            ("mod_loader_config.ini", new byte[] { 2 }),
            ("eldenring.exe", new byte[] { 99 }));  // forbidden

        var ex = Assert.Throws<InvalidOperationException>(
            () => FrameworkInstaller.Install(zip, Elm(), gameRoot, gameData));
        Assert.Contains("forbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eldenring.exe", ex.Message);

        // None of the files should have been extracted.
        Assert.False(File.Exists(Path.Combine(gameRoot, "dinput8.dll")));
        Assert.False(File.Exists(Path.Combine(gameRoot, "mod_loader_config.ini")));
    }

    [Fact]
    public void Install_refuses_zip_with_path_escaping_install_root()
    {
        var gameRoot = MakeGameRoot();
        var gameData = MakeGameData();
        var zip = BuildZip(
            ("../escaped.dll", new byte[] { 1 }),
            ("dinput8.dll", new byte[] { 2 }),
            ("mod_loader_config.ini", new byte[] { 3 }));

        Assert.Throws<InvalidOperationException>(
            () => FrameworkInstaller.Install(zip, Elm(), gameRoot, gameData));
    }

    [Fact]
    public void Install_with_no_overwrites_leaves_backup_snapshot_path_null()
    {
        var gameRoot = MakeGameRoot();
        var gameData = MakeGameData();
        var zip = BuildZip(("dinput8.dll", new byte[] { 1 }), ("mod_loader_config.ini", new byte[] { 2 }));

        var result = FrameworkInstaller.Install(zip, Elm(), gameRoot, gameData);

        Assert.Null(result.BackupSnapshotPath);
    }
}
