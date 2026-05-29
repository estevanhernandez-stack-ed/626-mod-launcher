using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class EngineRuntimeProcessesTests
{
    [Fact]
    public void Fromsoft_includes_runtime_and_eac_exe_names()
    {
        var names = EngineRuntimeProcesses.For("fromsoft");
        Assert.Contains("eldenring", names);            // the game runtime exe
        Assert.Contains("start_protected_game", names); // the EAC bootstrapper it runs behind
    }

    [Fact]
    public void Unknown_or_null_engine_returns_empty()
    {
        Assert.Empty(EngineRuntimeProcesses.For("nope"));
        Assert.Empty(EngineRuntimeProcesses.For(null));
    }

    [Fact]
    public void Engine_lookup_is_case_insensitive()
    {
        Assert.Contains("eldenring", EngineRuntimeProcesses.For("FromSoft"));
    }
}
