using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class OrchestratorContractsTests
{
    [Fact]
    public void SafeClearOptions_defaults_archive_on_and_keep_nexus_on()
    {
        var o = new SafeClearOptions();
        Assert.True(o.CreateRestorePoint);
        Assert.True(o.KeepNexus);
        Assert.Equal("vanilla", o.DefaultEndState);
        Assert.NotNull(o.PerGameEndState);
        Assert.Empty(o.PerGameEndState);
    }
}
