using ModManager.Core;

namespace ModManager.Tests;

// Pins the shape of FrameworkDep — the pure data record for known framework dependencies
// (UE4SS, BepInEx, SMAPI, ME2, DLL proxy, Forge/Fabric). Catalog + probe come in later tasks.
public class FrameworkDepsModelTests
{
    [Fact]
    public void FrameworkDep_carries_name_engine_detect_paths_and_url()
    {
        var dep = new FrameworkDep(
            Engine: "ue-pak",
            Name: "UE4SS",
            DetectRelativePaths: new[] { "Binaries/Win64/ue4ss/UE4SS.dll", "Binaries/Win64/dwmapi.dll" },
            GetUrl: "https://github.com/UE4SS-RE/RE-UE4SS/releases",
            Note: "Required for Lua mods and LogicMods paks.");
        Assert.Equal("ue-pak", dep.Engine);
        Assert.Equal("UE4SS", dep.Name);
        Assert.Equal(2, dep.DetectRelativePaths.Count);
        Assert.Equal("https://github.com/UE4SS-RE/RE-UE4SS/releases", dep.GetUrl);
        Assert.Equal("Required for Lua mods and LogicMods paks.", dep.Note);
    }
}
