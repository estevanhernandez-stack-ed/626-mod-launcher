using ModManager.Core;

namespace ModManager.Tests;

public class OwnershipMarkersTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "own-markers-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private string Folder()
    {
        Directory.CreateDirectory(_tmp);
        return _tmp;
    }

    [Fact]
    public void Finds_vortex_deployment_manifest()
    {
        var f = Folder();
        File.WriteAllText(Path.Combine(f, "vortex.deployment.windrose-scripts.json"), "{}");
        var markers = OwnershipMarkers.MarkerFilesIn(f);
        Assert.Single(markers);
        Assert.Equal(OwnerTool.Vortex, markers[0].Owner);
        Assert.EndsWith("vortex.deployment.windrose-scripts.json", markers[0].Path);
    }

    [Fact]
    public void Finds_vortex_managed_flag_file()
    {
        var f = Folder();
        File.WriteAllText(Path.Combine(f, "__folder_managed_by_vortex"), "");
        var markers = OwnershipMarkers.MarkerFilesIn(f);
        Assert.Contains(markers, m => m.Owner == OwnerTool.Vortex && m.Path.EndsWith("__folder_managed_by_vortex"));
    }

    [Fact]
    public void Finds_mo2_meta_ini()
    {
        var f = Folder();
        File.WriteAllText(Path.Combine(f, "meta.ini"), "[General]");
        var markers = OwnershipMarkers.MarkerFilesIn(f);
        Assert.Contains(markers, m => m.Owner == OwnerTool.Mo2 && m.Path.EndsWith("meta.ini"));
    }

    [Fact]
    public void Empty_when_no_markers_or_missing_folder()
    {
        Assert.Empty(OwnershipMarkers.MarkerFilesIn(Folder()));
        Assert.Empty(OwnershipMarkers.MarkerFilesIn(Path.Combine(_tmp, "does-not-exist")));
    }

    [Fact]
    public void OwnerOf_returns_the_first_marker_owner_or_null()
    {
        var f = Folder();
        Assert.Null(OwnershipMarkers.OwnerOf(f));
        File.WriteAllText(Path.Combine(f, "vortex.deployment.x.json"), "{}");
        Assert.Equal(OwnerTool.Vortex, OwnershipMarkers.OwnerOf(f));
    }
}
