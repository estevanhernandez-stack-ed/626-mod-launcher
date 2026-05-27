using ModManager.Core;

namespace ModManager.Tests;

// Pins the static catalog of known framework dependencies. One entry per (engine, framework) pair.
// Spec: docs/superpowers/specs/2026-05-26-mod-dependency-detection-design.md.
public class FrameworkDepsCatalogTests
{
    [Fact]
    public void Catalog_has_six_entries_across_known_engines()
    {
        Assert.Equal(6, FrameworkDeps.Catalog.Count);
    }

    [Fact]
    public void Catalog_includes_ue4ss_for_ue_pak()
    {
        var ue4ss = FrameworkDeps.Catalog.Single(d => d.Name == "UE4SS");
        Assert.Equal("ue-pak", ue4ss.Engine);
        Assert.Contains("ue4ss", ue4ss.DetectRelativePaths[0], StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://", ue4ss.GetUrl);
    }

    [Fact]
    public void Catalog_includes_bepinex_smapi_me2_eml_forge_fabric()
    {
        var names = FrameworkDeps.Catalog.Select(d => d.Name).ToList();
        Assert.Contains("BepInEx", names);
        Assert.Contains("SMAPI", names);
        Assert.Contains("Mod Engine 2", names);
        Assert.Contains("DLL proxy (dinput8/version/winhttp)", names);
        Assert.Contains("Forge or Fabric", names);
    }

    [Fact]
    public void Every_entry_has_https_get_url_and_at_least_one_detect_path()
    {
        foreach (var dep in FrameworkDeps.Catalog)
        {
            Assert.StartsWith("https://", dep.GetUrl);
            Assert.NotEmpty(dep.DetectRelativePaths);
            Assert.False(string.IsNullOrWhiteSpace(dep.Note));
        }
    }
}
