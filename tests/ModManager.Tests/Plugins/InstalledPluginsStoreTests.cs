using System.IO;
using ModManager.Core.Plugins;

namespace ModManager.Tests.Plugins;

public class InstalledPluginsStoreTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "mm-installed-plugins-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Round_trips_versions_and_writes_camelCase()
    {
        var path = TempFile();
        try
        {
            InstalledPluginsStore.Write(path, new Dictionary<string, string> { ["nexus"] = "1.0.0" });

            var json = File.ReadAllText(path);
            Assert.Contains("\"versions\"", json);      // camelCase key on disk
            Assert.DoesNotContain("\"Versions\"", json);

            var read = InstalledPluginsStore.Read(path);
            Assert.Equal("1.0.0", read["nexus"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_of_a_missing_file_is_empty()
        => Assert.Empty(InstalledPluginsStore.Read(TempFile()));

    [Fact]
    public void Read_of_a_corrupt_file_is_empty()
    {
        var path = TempFile();
        try { File.WriteAllText(path, "{ not json"); Assert.Empty(InstalledPluginsStore.Read(path)); }
        finally { File.Delete(path); }
    }
}
