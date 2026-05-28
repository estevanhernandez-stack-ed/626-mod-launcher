using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class RestoreMarkersTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "rp-mark-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    [Fact]
    public void RestoreAvailable_round_trips_camelCase()
    {
        Directory.CreateDirectory(_tmp);
        RestoreMarkers.WriteRestoreAvailable(_tmp, "20260528-141233");
        var json = File.ReadAllText(Path.Combine(_tmp, RestoreMarkers.RestoreAvailableFile));
        Assert.Contains("\"restorePoint\"", json);
        Assert.DoesNotContain("\"RestorePoint\"", json);
        Assert.Equal("20260528-141233", RestoreMarkers.ReadRestoreAvailable(_tmp));
    }

    [Fact]
    public void ReadRestoreAvailable_null_when_absent()
        => Assert.Null(RestoreMarkers.ReadRestoreAvailable(Path.Combine(_tmp, "nope")));

    [Fact]
    public void LastClear_round_trips_and_clears()
    {
        Directory.CreateDirectory(_tmp);
        RestoreMarkers.WriteLastClear(_tmp, "2026-05-28T14:12:33Z", "20260528-141233");
        var lc = RestoreMarkers.ReadLastClear(_tmp)!;
        Assert.Equal("20260528-141233", lc.RestorePoint);
        RestoreMarkers.ClearLastClear(_tmp);
        Assert.Null(RestoreMarkers.ReadLastClear(_tmp));
    }
}
