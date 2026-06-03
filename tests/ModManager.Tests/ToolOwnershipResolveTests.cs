using ModManager.Core;

namespace ModManager.Tests;

public class ToolOwnershipResolveTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "own-resolve-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private string FolderWithMarker()
    {
        Directory.CreateDirectory(_tmp);
        File.WriteAllText(Path.Combine(_tmp, "vortex.deployment.x.json"), "{}");
        return _tmp;
    }

    private static HashSet<string> Set(params string[] s) => new(s, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Owned_when_marker_present_and_not_taken_over()
    {
        var f = FolderWithMarker();
        var r = ToolOwnership.Resolve(f, takenOver: Set());
        Assert.Equal(OwnershipState.Owned, r.State);
        Assert.Equal(OwnerTool.Vortex, r.Owner);
    }

    [Fact]
    public void NotOwned_when_taken_over_and_marker_already_archived()
    {
        Directory.CreateDirectory(_tmp);                  // taken over, NO marker on disk
        var r = ToolOwnership.Resolve(_tmp, takenOver: Set(_tmp));
        Assert.Equal(OwnershipState.NotOwned, r.State);
    }

    [Fact]
    public void ReDeployed_when_taken_over_and_a_fresh_marker_reappeared()
    {
        var f = FolderWithMarker();                          // marker present
        var r = ToolOwnership.Resolve(f, takenOver: Set(f)); // and recorded as taken over
        Assert.Equal(OwnershipState.ReDeployed, r.State);
    }

    [Fact]
    public void NotOwned_when_no_marker_and_not_taken_over()
    {
        Directory.CreateDirectory(_tmp);
        var r = ToolOwnership.Resolve(_tmp, takenOver: Set());
        Assert.Equal(OwnershipState.NotOwned, r.State);
    }
}
