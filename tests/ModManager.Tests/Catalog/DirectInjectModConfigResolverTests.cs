using ModManager.Core.Catalog;

namespace ModManager.Tests.Catalog;

public class DirectInjectModConfigResolverTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "di-resolve-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    [Fact]
    public void Resolve_returns_existing_default_path_for_Seamless()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var gameFolder = Path.Combine(gameRoot, "Game");
        var seamlessFolder = Path.Combine(gameFolder, "SeamlessCoop");
        Directory.CreateDirectory(seamlessFolder);
        var iniPath = Path.Combine(seamlessFolder, "seamlesscoopsettings.ini");
        File.WriteAllText(iniPath, "test");

        var result = DirectInjectModConfigResolver.Resolve(
            "Seamless Co-op", gameRoot, DirectInjectConfigOverrides.Empty);

        Assert.Contains(iniPath, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_uses_override_when_present()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var customDir = Path.Combine(_tmp, "Elsewhere");
        Directory.CreateDirectory(Path.Combine(gameRoot, "Game"));
        Directory.CreateDirectory(customDir);
        var customIni = Path.Combine(customDir, "custom-seamless.ini");
        File.WriteAllText(customIni, "test");

        var overrides = new DirectInjectConfigOverrides(new Dictionary<string, Dictionary<string, string>>
        {
            ["seamless-coop"] = new()
            {
                ["SeamlessCoop/seamlesscoopsettings.ini"] = customIni,
            },
        });

        var result = DirectInjectModConfigResolver.Resolve("Seamless Co-op", gameRoot, overrides);

        Assert.Contains(customIni, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_skips_paths_that_dont_exist_on_disk()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        Directory.CreateDirectory(Path.Combine(gameRoot, "Game"));
        // No SeamlessCoop folder — the default config path won't exist.

        var result = DirectInjectModConfigResolver.Resolve(
            "Seamless Co-op", gameRoot, DirectInjectConfigOverrides.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void Resolve_returns_empty_for_unknown_mod_name()
    {
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        Directory.CreateDirectory(gameRoot);

        var result = DirectInjectModConfigResolver.Resolve(
            "Definitely Not A Real Mod", gameRoot, DirectInjectConfigOverrides.Empty);

        Assert.Empty(result);
    }

    [Fact]
    public void Resolve_falls_back_to_gameRoot_when_no_Game_subfolder()
    {
        // Some FromSoft games (pre-DLC ER on older Steam manifests, or test fixtures) put
        // the exe right at the ship root. The resolver should still find INIs there.
        var gameRoot = Path.Combine(_tmp, "GameRoot");
        var seamlessFolder = Path.Combine(gameRoot, "SeamlessCoop");
        Directory.CreateDirectory(seamlessFolder);
        var iniPath = Path.Combine(seamlessFolder, "seamlesscoopsettings.ini");
        File.WriteAllText(iniPath, "test");

        var result = DirectInjectModConfigResolver.Resolve(
            "Seamless Co-op", gameRoot, DirectInjectConfigOverrides.Empty);

        Assert.Contains(iniPath, result, StringComparer.OrdinalIgnoreCase);
    }
}
