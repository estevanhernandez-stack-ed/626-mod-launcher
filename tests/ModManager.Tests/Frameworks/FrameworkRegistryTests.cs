using System.Text.Json;
using ModManager.Core.Frameworks;

namespace ModManager.Tests.Frameworks;

public class FrameworkRegistryTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "fw-reg-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    [Fact]
    public void List_returns_empty_when_no_installs()
    {
        var gameData = Path.Combine(_tmp, "GameData");
        Directory.CreateDirectory(gameData);

        var result = FrameworkRegistry.List(gameData);

        Assert.Empty(result);
    }

    [Fact]
    public void List_returns_one_manifest_per_framework()
    {
        var gameData = Path.Combine(_tmp, "GameData");
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        Directory.CreateDirectory(gameRoot);
        WriteFakeManifest(gameData, "elden-mod-loader", "Elden Mod Loader", "TechieW", gameRoot);
        WriteFakeManifest(gameData, "some-other", "Some Other", "alice", gameRoot);

        var result = FrameworkRegistry.List(gameData);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, m => m.FrameworkId == "elden-mod-loader");
        Assert.Contains(result, m => m.FrameworkId == "some-other");
    }

    [Fact]
    public void Uninstall_removes_installed_files_and_restores_backup()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var gameData = Path.Combine(_tmp, "GameData");
        Directory.CreateDirectory(gameRoot);

        // Pre-state: framework files were installed (bytes [1]), a backup was taken of the
        // original dinput8.dll the install replaced (original bytes [9, 9]).
        File.WriteAllBytes(Path.Combine(gameRoot, "dinput8.dll"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(gameRoot, "mod_loader_config.ini"), new byte[] { 2 });

        var fwDir = Path.Combine(gameData, "frameworks", "elden-mod-loader");
        var backupDir = Path.Combine(fwDir, "backup", "20260527000000");
        Directory.CreateDirectory(backupDir);
        File.WriteAllBytes(Path.Combine(backupDir, "dinput8.dll"), new byte[] { 9, 9 });

        WriteManifest(fwDir, new FrameworkInstallManifest(
            "elden-mod-loader", "Elden Mod Loader", "TechieW", gameRoot,
            new[] { "dinput8.dll", "mod_loader_config.ini" },
            DateTime.UtcNow, backupDir));

        FrameworkRegistry.Uninstall(gameData, "elden-mod-loader", gameRoot);

        // Backed-up file restored; non-backed-up file removed; framework dir + manifest gone.
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(Path.Combine(gameRoot, "dinput8.dll")));
        Assert.False(File.Exists(Path.Combine(gameRoot, "mod_loader_config.ini")));
        Assert.False(Directory.Exists(fwDir));
    }

    [Fact]
    public void Uninstall_with_no_backup_just_removes_installed_files()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var gameData = Path.Combine(_tmp, "GameData");
        Directory.CreateDirectory(gameRoot);

        File.WriteAllBytes(Path.Combine(gameRoot, "dinput8.dll"), new byte[] { 1 });

        var fwDir = Path.Combine(gameData, "frameworks", "elden-mod-loader");
        WriteManifest(fwDir, new FrameworkInstallManifest(
            "elden-mod-loader", "Elden Mod Loader", "TechieW", gameRoot,
            new[] { "dinput8.dll" }, DateTime.UtcNow, null));

        FrameworkRegistry.Uninstall(gameData, "elden-mod-loader", gameRoot);

        Assert.False(File.Exists(Path.Combine(gameRoot, "dinput8.dll")));
        Assert.False(Directory.Exists(fwDir));
    }

    [Fact]
    public void Uninstall_resolves_files_against_InstallPath_not_gameRoot()
    {
        // FromSoft case: framework installed under <gameRoot>\Game\ (PlayFolder). The manifest's
        // InstallPath points at Game\; the bug resolved against gameRoot and missed the files.
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var playFolder = Path.Combine(gameRoot, "Game");
        var gameData = Path.Combine(_tmp, "GameData");
        Directory.CreateDirectory(playFolder);
        File.WriteAllBytes(Path.Combine(playFolder, "dinput8.dll"), new byte[] { 1 });

        var fwDir = Path.Combine(gameData, "frameworks", "elden-mod-loader");
        WriteManifest(fwDir, new FrameworkInstallManifest(
            "elden-mod-loader", "Elden Mod Loader", "TechieW",
            playFolder,                                   // InstallPath = PlayFolder
            new[] { "dinput8.dll" }, DateTime.UtcNow, null));

        FrameworkRegistry.Uninstall(gameData, "elden-mod-loader", gameRoot);

        Assert.False(File.Exists(Path.Combine(playFolder, "dinput8.dll")));  // would have been left behind
        Assert.False(Directory.Exists(fwDir));
    }

    [Fact]
    public void Uninstall_falls_back_to_gameRoot_when_InstallPath_empty()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var gameData = Path.Combine(_tmp, "GameData");
        Directory.CreateDirectory(gameRoot);
        File.WriteAllBytes(Path.Combine(gameRoot, "dinput8.dll"), new byte[] { 1 });

        var fwDir = Path.Combine(gameData, "frameworks", "elden-mod-loader");
        WriteManifest(fwDir, new FrameworkInstallManifest(
            "elden-mod-loader", "Elden Mod Loader", "TechieW",
            "",                                               // legacy manifest — InstallPath absent/empty
            new[] { "dinput8.dll" }, DateTime.UtcNow, null));

        FrameworkRegistry.Uninstall(gameData, "elden-mod-loader", gameRoot);

        Assert.False(File.Exists(Path.Combine(gameRoot, "dinput8.dll")));
        Assert.False(Directory.Exists(fwDir));
    }

    [Fact]
    public void Uninstall_throws_when_manifest_missing()
    {
        var gameData = Path.Combine(_tmp, "GameData");
        Directory.CreateDirectory(gameData);

        Assert.Throws<FileNotFoundException>(
            () => FrameworkRegistry.Uninstall(gameData, "nonexistent", _tmp));
    }

    private static void WriteFakeManifest(string gameData, string id, string name, string author, string installRoot)
    {
        var fwDir = Path.Combine(gameData, "frameworks", id);
        WriteManifest(fwDir, new FrameworkInstallManifest(
            id, name, author, installRoot,
            Array.Empty<string>(), DateTime.UtcNow, null));
    }

    private static void WriteManifest(string fwDir, FrameworkInstallManifest m)
    {
        Directory.CreateDirectory(fwDir);
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        File.WriteAllText(Path.Combine(fwDir, "install.json"), JsonSerializer.Serialize(m, opts));
    }
}
