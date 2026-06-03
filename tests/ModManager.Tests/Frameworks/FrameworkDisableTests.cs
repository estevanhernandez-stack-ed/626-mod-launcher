using ModManager.Core.Frameworks;

namespace ModManager.Tests.Frameworks;

public class FrameworkDisableTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "fw-disable-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private (string gameData, string binWin64) MakeInstall()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var binWin64 = Path.Combine(gameRoot, "R5", "Binaries", "Win64");
        var ue = Path.Combine(binWin64, "ue4ss");
        Directory.CreateDirectory(ue);
        File.WriteAllText(Path.Combine(binWin64, "dwmapi.dll"), "proxy bytes");
        File.WriteAllText(Path.Combine(ue, "UE4SS.dll"), "loader");
        File.WriteAllText(Path.Combine(ue, "UE4SS-settings.ini"), "[General]");

        var gameData = Path.Combine(_tmp, "data");
        var fwDir = Path.Combine(gameData, "frameworks", "ue4ss");
        Directory.CreateDirectory(fwDir);
        var manifest = new FrameworkInstallManifest(
            "ue4ss", "UE4SS", "RE-UE4SS team", binWin64,
            new[] { "dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/UE4SS-settings.ini" },
            DateTime.UtcNow, null);
        File.WriteAllText(Path.Combine(fwDir, "install.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions
            { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = true }));
        return (gameData, binWin64);
    }

    [Fact]
    public void Disable_steps_the_proxy_aside_leaving_ue4ss_in_place()
    {
        var (gameData, binWin64) = MakeInstall();
        FrameworkRegistry.Disable(gameData, "ue4ss");
        Assert.False(File.Exists(Path.Combine(binWin64, "dwmapi.dll")));
        Assert.True(File.Exists(Path.Combine(binWin64, "ue4ss", "UE4SS.dll")));
        Assert.True(File.Exists(Path.Combine(binWin64, "ue4ss", "UE4SS-settings.ini")));
        Assert.True(FrameworkRegistry.IsDisabled(gameData, "ue4ss"));
    }

    [Fact]
    public void Enable_restores_the_proxy_byte_for_byte()
    {
        var (gameData, binWin64) = MakeInstall();
        var before = File.ReadAllText(Path.Combine(binWin64, "dwmapi.dll"));
        FrameworkRegistry.Disable(gameData, "ue4ss");
        Assert.False(File.Exists(Path.Combine(binWin64, "dwmapi.dll")));
        FrameworkRegistry.Enable(gameData, "ue4ss");
        Assert.Equal(before, File.ReadAllText(Path.Combine(binWin64, "dwmapi.dll")));
        Assert.False(FrameworkRegistry.IsDisabled(gameData, "ue4ss"));
    }

    [Fact]
    public void IsDisabled_is_false_for_a_normally_installed_framework()
    {
        var (gameData, _) = MakeInstall();
        Assert.False(FrameworkRegistry.IsDisabled(gameData, "ue4ss"));
    }

    [Fact]
    public void Disable_is_a_safe_noop_when_already_disabled()
    {
        var (gameData, binWin64) = MakeInstall();
        FrameworkRegistry.Disable(gameData, "ue4ss");
        FrameworkRegistry.Disable(gameData, "ue4ss");
        Assert.True(FrameworkRegistry.IsDisabled(gameData, "ue4ss"));
        Assert.False(File.Exists(Path.Combine(binWin64, "dwmapi.dll")));
    }

    [Fact]
    public void Disable_throws_a_clear_error_when_no_manifest()
    {
        var gameData = Path.Combine(_tmp, "empty");
        Directory.CreateDirectory(gameData);
        Assert.Throws<FileNotFoundException>(() => FrameworkRegistry.Disable(gameData, "ue4ss"));
    }

    [Fact]
    public void Disable_is_a_safe_noop_for_a_legacy_manifest_with_empty_InstallPath()
    {
        // A pre-InstallPath manifest (empty InstallPath) can't resolve the proxy path — Disable must be a
        // safe no-op (not silently combine against "" and miss, not throw). IsDisabled stays false.
        var gameData = Path.Combine(_tmp, "legacy-data");
        var fwDir = Path.Combine(gameData, "frameworks", "ue4ss");
        Directory.CreateDirectory(fwDir);
        var manifest = new FrameworkInstallManifest(
            "ue4ss", "UE4SS", "RE-UE4SS team", "",   // empty InstallPath
            new[] { "dwmapi.dll" }, DateTime.UtcNow, null);
        File.WriteAllText(Path.Combine(fwDir, "install.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions
            { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = true }));

        FrameworkRegistry.Disable(gameData, "ue4ss");          // must not throw, must not create stray dirs
        Assert.False(FrameworkRegistry.IsDisabled(gameData, "ue4ss"));
    }
}
