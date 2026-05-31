using System.IO.Compression;
using ModManager.Core.Frameworks;

namespace ModManager.Tests.Frameworks;

// Issue #108: UE4SS installable via framework intake. Target is <gameRoot>/<projectSubfolder>/Binaries/Win64
// (e.g. R5/Binaries/Win64 for Windrose) — derived from the game's ue-pak mod-location paths, NOT gameRoot.
// The game exe (<Project>-Win64-Shipping.exe) sits in that same folder, so the install must never clobber it.
public class Ue4ssInstallerTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "ue4ss-install-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private static readonly string[] WindroseModLocs = { "R5/Content/Paks/~mods", "R5/Content/Paks/LogicMods" };

    private static KnownFramework Ue4ss() =>
        KnownFramework.Catalog.Single(f => f.FrameworkId == "ue4ss");

    private string MakeGameData()
    {
        var d = Path.Combine(_tmp, "GameData");
        Directory.CreateDirectory(d);
        return d;
    }

    // A gameRoot whose R5/Binaries/Win64 exists and holds a stub <Project>-Win64-Shipping.exe (what
    // the proxy DLL chain-loads next to). Returns (gameRoot, binWin64).
    private (string gameRoot, string binWin64) MakeUeGameRoot(string exeName = "Windrose-Win64-Shipping.exe")
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var binWin64 = Path.Combine(gameRoot, "R5", "Binaries", "Win64");
        Directory.CreateDirectory(binWin64);
        File.WriteAllBytes(Path.Combine(binWin64, exeName), new byte[] { 7, 7, 7 });
        return (gameRoot, binWin64);
    }

    private string BuildZip(params (string Path, byte[] Bytes)[] entries)
    {
        var zipPath = Path.Combine(_tmp, $"src-{Guid.NewGuid():n}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using var stream = File.Create(zipPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (path, bytes) in entries)
        {
            using var es = zip.CreateEntry(path).Open();
            es.Write(bytes, 0, bytes.Length);
        }
        return zipPath;
    }

    // A minimal UE4SS-shaped zip: proxy DLL + the UE4SS-unique signature pair under ue4ss/.
    private string Ue4ssZip(string proxy = "dwmapi.dll") => BuildZip(
        (proxy, new byte[] { 1 }),
        ("ue4ss/UE4SS.dll", new byte[] { 2 }),
        ("ue4ss/UE4SS-settings.ini", new byte[] { 3 }));

    // ── Catalog entry ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Catalog_ships_UE4SS_for_ue_pak_multi_game()
    {
        var u = Ue4ss();
        Assert.Equal("UE4SS", u.DisplayName);
        Assert.Equal("ue-pak", u.Engine);
        Assert.Null(u.SteamAppId); // engine-only scope — works across ue-pak games
        Assert.Equal("UeProjectBinariesWin64", u.InstallRoot);
        Assert.Contains("UE4SS.dll", u.ZipSignatureFiles);
        Assert.Contains("UE4SS-settings.ini", u.ZipSignatureFiles);
        Assert.DoesNotContain("dwmapi.dll", u.ZipSignatureFiles); // proxy is a HINT, not a signature
        Assert.Contains("*-Shipping.exe", u.ForbiddenPaths);
    }

    // ── Resolver ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ResolveInstallRoot_UeProject_returns_project_Binaries_Win64_absolute()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var resolved = FrameworkInstaller.ResolveInstallRoot("UeProjectBinariesWin64", gameRoot, WindroseModLocs);
        Assert.Equal(Path.GetFullPath(Path.Combine(gameRoot, "R5", "Binaries", "Win64")), resolved);
    }

    [Fact]
    public void ResolveInstallRoot_UeProject_returns_null_when_no_project_subfolder()
    {
        // A Content-rooted-only mod location yields no project subfolder.
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var resolved = FrameworkInstaller.ResolveInstallRoot(
            "UeProjectBinariesWin64", gameRoot, new[] { "Content/Paks/~mods" });
        Assert.Null(resolved);
    }

    // ── Install ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Install_lands_UE4SS_under_project_Binaries_Win64_leaving_the_exe_untouched()
    {
        var (gameRoot, binWin64) = MakeUeGameRoot();
        var data = MakeGameData();
        var exeBytesBefore = File.ReadAllBytes(Path.Combine(binWin64, "Windrose-Win64-Shipping.exe"));

        var r = FrameworkInstaller.Install(Ue4ssZip(), Ue4ss(), gameRoot, data, WindroseModLocs);

        Assert.True(File.Exists(Path.Combine(binWin64, "dwmapi.dll")));
        Assert.True(File.Exists(Path.Combine(binWin64, "ue4ss", "UE4SS.dll")));
        Assert.True(File.Exists(Path.Combine(binWin64, "ue4ss", "UE4SS-settings.ini")));
        Assert.Equal(exeBytesBefore, File.ReadAllBytes(Path.Combine(binWin64, "Windrose-Win64-Shipping.exe")));
        Assert.Equal(Path.GetFullPath(binWin64), r.InstallPath); // persisted absolute → uninstall reverses
    }

    [Fact]
    public void Install_refuses_a_zip_that_would_overwrite_the_game_exe()
    {
        var (gameRoot, _) = MakeUeGameRoot();
        var data = MakeGameData();
        var hostile = BuildZip(
            ("dwmapi.dll", new byte[] { 1 }),
            ("ue4ss/UE4SS.dll", new byte[] { 2 }),
            ("ue4ss/UE4SS-settings.ini", new byte[] { 3 }),
            ("Windrose-Win64-Shipping.exe", new byte[] { 66 })); // forbidden by *-Shipping.exe

        var ex = Assert.Throws<InvalidOperationException>(
            () => FrameworkInstaller.Install(hostile, Ue4ss(), gameRoot, data, WindroseModLocs));
        Assert.Contains("forbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        // Nothing extracted — exe still the stub bytes.
        Assert.Equal(new byte[] { 7, 7, 7 },
            File.ReadAllBytes(Path.Combine(gameRoot, "R5", "Binaries", "Win64", "Windrose-Win64-Shipping.exe")));
    }

    [Fact]
    public void Install_allows_a_same_suffix_file_nested_in_the_payload()
    {
        // A legit mod file deep in the payload sharing the -Shipping.exe suffix is NOT the game exe.
        var (gameRoot, binWin64) = MakeUeGameRoot();
        var data = MakeGameData();
        var zip = BuildZip(
            ("dwmapi.dll", new byte[] { 1 }),
            ("ue4ss/UE4SS.dll", new byte[] { 2 }),
            ("ue4ss/UE4SS-settings.ini", new byte[] { 3 }),
            ("ue4ss/Mods/Cool-Shipping.exe", new byte[] { 9 }));

        var r = FrameworkInstaller.Install(zip, Ue4ss(), gameRoot, data, WindroseModLocs);

        Assert.True(File.Exists(Path.Combine(binWin64, "ue4ss", "Mods", "Cool-Shipping.exe")));
        Assert.Equal(4, r.InstalledFiles.Count);
    }

    [Fact]
    public void Install_throws_when_no_project_subfolder_resolves()
    {
        var (gameRoot, _) = MakeUeGameRoot();
        var data = MakeGameData();
        var ex = Assert.Throws<InvalidOperationException>(
            () => FrameworkInstaller.Install(Ue4ssZip(), Ue4ss(), gameRoot, data, new[] { "Content/Paks/~mods" }));
        Assert.Contains("project subfolder", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Install_throws_when_resolved_folder_has_no_game_exe()
    {
        // R5/Binaries/Win64 exists but holds no *-Shipping.exe → the proxy would land where nothing loads it.
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        Directory.CreateDirectory(Path.Combine(gameRoot, "R5", "Binaries", "Win64")); // no exe
        var data = MakeGameData();
        var ex = Assert.Throws<InvalidOperationException>(
            () => FrameworkInstaller.Install(Ue4ssZip(), Ue4ss(), gameRoot, data, WindroseModLocs));
        Assert.Contains("executable", ex.Message, StringComparison.OrdinalIgnoreCase);
        // Nothing written.
        Assert.False(File.Exists(Path.Combine(gameRoot, "R5", "Binaries", "Win64", "dwmapi.dll")));
    }

    [Fact]
    public void Install_refuses_directory_traversal()
    {
        var (gameRoot, _) = MakeUeGameRoot();
        var data = MakeGameData();
        var zip = BuildZip(
            ("../../Windows/System32/evil.dll", new byte[] { 1 }),
            ("ue4ss/UE4SS.dll", new byte[] { 2 }),
            ("ue4ss/UE4SS-settings.ini", new byte[] { 3 }));
        Assert.Throws<InvalidOperationException>(
            () => FrameworkInstaller.Install(zip, Ue4ss(), gameRoot, data, WindroseModLocs));
    }

    [Fact]
    public void Install_then_Uninstall_removes_every_installed_file_and_leaves_the_exe()
    {
        // Uninstall's documented contract is files-only (it never prunes directories — see
        // FrameworkRegistryTests), so the invariant we pin is: every file the install added is gone,
        // and the game exe it was forbidden from touching is byte-for-byte intact. A leftover empty
        // ue4ss/ dir is harmless and consistent with the existing ELM uninstall behavior.
        var (gameRoot, binWin64) = MakeUeGameRoot();
        var data = MakeGameData();
        var exeBefore = File.ReadAllBytes(Path.Combine(binWin64, "Windrose-Win64-Shipping.exe"));

        FrameworkInstaller.Install(Ue4ssZip(), Ue4ss(), gameRoot, data, WindroseModLocs);
        Assert.True(File.Exists(Path.Combine(binWin64, "dwmapi.dll"))); // installed

        FrameworkRegistry.Uninstall(data, "ue4ss", gameRoot);

        Assert.False(File.Exists(Path.Combine(binWin64, "dwmapi.dll")));                  // removed
        Assert.False(File.Exists(Path.Combine(binWin64, "ue4ss", "UE4SS.dll")));          // removed
        Assert.False(File.Exists(Path.Combine(binWin64, "ue4ss", "UE4SS-settings.ini"))); // removed
        Assert.Equal(exeBefore, File.ReadAllBytes(Path.Combine(binWin64, "Windrose-Win64-Shipping.exe"))); // untouched
        Assert.False(Directory.Exists(Path.Combine(data, "frameworks", "ue4ss")));        // manifest torn down
    }

    // ── Classify: multi-game + proxy-is-hint ───────────────────────────────────────────────────────

    [Fact]
    public void Classify_matches_UE4SS_across_ue_pak_games_regardless_of_app_id()
    {
        var names = new[] { "dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/UE4SS-settings.ini" };
        Assert.Equal("ue4ss", KnownFramework.Classify(names, "ue-pak", "3041230").Match?.FrameworkId);  // Windrose
        Assert.Equal("ue4ss", KnownFramework.Classify(names, "ue-pak", "1623730").Match?.FrameworkId);  // Palworld
    }

    [Fact]
    public void Classify_matches_when_proxy_is_xinput_variant()
    {
        // UE4SS ships the proxy as dwmapi/xinput1_3/d3d11 depending on release — the signature is the
        // UE4SS-unique pair, so an xinput-variant zip still classifies.
        var names = new[] { "xinput1_3.dll", "ue4ss/UE4SS.dll", "ue4ss/UE4SS-settings.ini" };
        Assert.Equal("ue4ss", KnownFramework.Classify(names, "ue-pak", "3041230").Match?.FrameworkId);
    }

    [Fact]
    public void Classify_no_match_when_only_one_signature_file_present()
    {
        var names = new[] { "dwmapi.dll", "ue4ss/UE4SS.dll" }; // missing UE4SS-settings.ini
        Assert.Null(KnownFramework.Classify(names, "ue-pak", "3041230").Match);
    }
}
