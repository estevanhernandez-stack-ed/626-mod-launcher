using ModManager.Core;

namespace ModManager.Tests;

// Ownership detection from on-disk markers only — the runtime truth of who owns a folder.
public class ToolOwnershipTests
{
    private static string Dir() => TestSupport.TempDir("owner-");

    [Fact]
    public void Detect_vortex_marker_file_is_vortex()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "__folder_managed_by_vortex"), "");
        Assert.Equal(OwnerTool.Vortex, ToolOwnership.Detect(d));
    }

    [Fact]
    public void Detect_vortex_deployment_manifest_is_vortex()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "vortex.deployment.windrose-scripts.json"), "{}");
        Assert.Equal(OwnerTool.Vortex, ToolOwnership.Detect(d));
    }

    [Fact]
    public void Detect_mo2_meta_ini_is_mo2()
    {
        var d = Dir();
        File.WriteAllText(Path.Combine(d, "meta.ini"), "[General]");
        Assert.Equal(OwnerTool.Mo2, ToolOwnership.Detect(d));
    }

    [Fact]
    public void Detect_unowned_folder_is_null()
        => Assert.Null(ToolOwnership.Detect(Dir()));

    [Fact]
    public void Detect_missing_folder_is_null()
        => Assert.Null(ToolOwnership.Detect(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N"))));
}
