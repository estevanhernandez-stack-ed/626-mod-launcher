using ModManager.Core;
using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class RestorePointEngineCaptureTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "rp-cap-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private (GameEntry game, GameContext c, string modsDir) MakeGame()
    {
        var gameRoot = Path.Combine(_tmp, "game");
        var modsDir = Path.Combine(gameRoot, "mods");
        Directory.CreateDirectory(modsDir);
        var game = new GameEntry
        {
            Id = "t", GameName = "T", GameRoot = gameRoot, DataDir = Path.Combine(_tmp, "_626mods", "t"),
            ModLocations = new[] { new ModLocation("mods", "mods", "mods") },
            FileExtensions = new[] { "pak" }, GroupingRule = "filename_no_ext",
            LaunchTargets = new[] { new LaunchTarget("Play", "exe", "game.exe") { IsDefault = true } },
        };
        return (game, Scanner.GameContext(game), modsDir);
    }

    [Fact]
    public async Task CaptureGame_copies_data_dir_and_builds_manifest_entry()
    {
        var (game, c, modsDir) = MakeGame();
        File.WriteAllText(Path.Combine(modsDir, "cool.pak"), "DATA");
        await Scanner.DisableModAsync("cool", c);
        Scanner.SaveMetadata(c, new Dictionary<string, ModMeta>
        {
            ["cool"] = new ModMeta { Url = "https://nexusmods.com/x", SourceConfidence = "fingerprint" }
        });

        var gameArchiveDir = Path.Combine(_tmp, "archive", "games", "t");
        var entry = RestorePointEngine.CaptureGame(new GameCaptureInput(game, c, EndState: "vanilla"), gameArchiveDir);

        Assert.Equal("DATA", File.ReadAllText(Path.Combine(gameArchiveDir, "data", "disabled", "cool", "cool.pak")));
        Assert.Equal("t", entry.Id);
        Assert.Equal("vanilla", entry.EndState);
        Assert.Single(entry.LaunchTargets);
        Assert.Contains(entry.Mods, m => m.Name == "cool" && m.SourceUrl == "https://nexusmods.com/x" && m.SourceConfidence == "fingerprint");
        Assert.True(File.Exists(Path.Combine(c.DisabledRoot, "cool", "cool.pak")));   // live data dir untouched
    }

    [Fact]
    public void CaptureGame_snapshots_installed_framework_files()
    {
        var (game, c, _) = MakeGame();
        // Framework installed at the game root with one file.
        var installPath = c.GameRoot;
        File.WriteAllBytes(Path.Combine(installPath, "dinput8.dll"), new byte[] { 1, 2, 3 });
        // Write the framework's install manifest into the data dir (frameworks/<id>/install.json).
        var fwDir = Path.Combine(c.DataDir, "frameworks", "elden-mod-loader");
        Directory.CreateDirectory(fwDir);
        var manifest = new ModManager.Core.Frameworks.FrameworkInstallManifest(
            "elden-mod-loader", "Elden Mod Loader", "TechieW",
            installPath, new[] { "dinput8.dll" }, DateTime.UtcNow, null);
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, WriteIndented = true };
        File.WriteAllText(Path.Combine(fwDir, "install.json"), System.Text.Json.JsonSerializer.Serialize(manifest, opts));

        var gameArchiveDir = Path.Combine(_tmp, "archive", "games", "t");
        var entry = RestorePointEngine.CaptureGame(new GameCaptureInput(game, c, "vanilla"), gameArchiveDir);

        // The framework's installed file was snapshotted into the archive.
        var fa = Assert.Single(entry.Frameworks);
        Assert.Equal("elden-mod-loader", fa.FrameworkId);
        Assert.NotNull(fa.CapturedStateRel);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(gameArchiveDir, fa.CapturedStateRel!, "dinput8.dll")));
        // The live framework file is untouched (capture is a copy).
        Assert.True(File.Exists(Path.Combine(installPath, "dinput8.dll")));
    }

    [Fact]
    public async Task CaptureGame_resolves_metadata_for_a_variant_mod_by_base()
    {
        var (game, c, modsDir) = MakeGame();
        // "MoreStamina_5x" → Variant.ParseVariant → Base="MoreStamina", Tag="5x"
        // Confirmed via VariantTests.ParseVariant_single_multiplier.
        var variantName = "MoreStamina_5x";
        var baseName = "MoreStamina";
        File.WriteAllText(Path.Combine(modsDir, variantName + ".pak"), "DATA");
        await Scanner.DisableModAsync(variantName, c);
        Scanner.SaveMetadata(c, new Dictionary<string, ModMeta>
        {
            [baseName] = new ModMeta { Url = "https://nexusmods.com/v", SourceConfidence = "fingerprint" }
        });

        var entry = RestorePointEngine.CaptureGame(new GameCaptureInput(game, c, "vanilla"),
            Path.Combine(_tmp, "archive", "games", "t"));

        var mod = Assert.Single(entry.Mods, m => m.Name == variantName);
        Assert.Equal("https://nexusmods.com/v", mod.SourceUrl);   // resolved via BASE key, not full name
        Assert.Equal("fingerprint", mod.SourceConfidence);
    }
}
