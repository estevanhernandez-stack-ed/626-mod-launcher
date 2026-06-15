using ModManager.Core;

namespace ModManager.Tests;

public class UeProjectScanTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "ueprojscan-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    private string Root() { var r = Path.Combine(_tmp, Guid.NewGuid().ToString("n")); Directory.CreateDirectory(r); return r; }
    private static void MakePaks(string root, params string[] relSegments)
        => Directory.CreateDirectory(Path.Combine(new[] { root }.Concat(relSegments).Concat(new[] { "Content", "Paks" }).ToArray()));

    [Fact]
    public void Single_wrapper_is_found_at_depth_1()
    {
        var root = Root(); MakePaks(root, "Pal");
        var cands = UeProjectScan.Enumerate(root);
        Assert.Single(cands);
        Assert.Equal("Pal", cands[0].RelativeProjectPath);
        Assert.Equal(1, cands[0].WrapperDepth);
        Assert.True(UeProjectScan.HasContentPaks(root));
    }

    [Fact]
    public void Two_wrapper_is_found_at_depth_2()  // Marvel Rivals: MarvelGame/Marvel/Content/Paks
    {
        var root = Root(); MakePaks(root, "MarvelGame", "Marvel");
        var cands = UeProjectScan.Enumerate(root);
        Assert.Contains(cands, c => c.RelativeProjectPath.Replace('\\', '/') == "MarvelGame/Marvel" && c.WrapperDepth == 2);
        Assert.True(UeProjectScan.HasContentPaks(root));
    }

    [Fact]
    public void Root_is_the_project_at_depth_0()  // STALKER 2: install root IS the project
    {
        var root = Root(); MakePaks(root); // root/Content/Paks
        var cands = UeProjectScan.Enumerate(root);
        Assert.Contains(cands, c => c.RelativeProjectPath == "" && c.WrapperDepth == 0);
    }

    [Fact]
    public void Engine_sibling_is_skipped()
    {
        var root = Root(); MakePaks(root, "Engine"); MakePaks(root, "Phoenix");
        var cands = UeProjectScan.Enumerate(root);
        Assert.Single(cands);
        Assert.Equal("Phoenix", cands[0].RelativeProjectPath);
    }

    [Fact]
    public void Shipping_pak_and_binaries_signals_are_recorded()
    {
        var root = Root(); MakePaks(root, "Marvel");
        File.WriteAllText(Path.Combine(root, "Marvel", "Content", "Paks", "pakchunk0-Windows.pak"), "x");
        Directory.CreateDirectory(Path.Combine(root, "Marvel", "Binaries"));
        var c = UeProjectScan.Enumerate(root).Single();
        Assert.True(c.HasShippingPak);
        Assert.True(c.HasBinariesSibling);
    }

    [Fact]
    public void No_content_paks_means_no_detection()
    {
        var root = Root(); Directory.CreateDirectory(Path.Combine(root, "Misc", "Stuff"));
        Assert.Empty(UeProjectScan.Enumerate(root));
        Assert.False(UeProjectScan.HasContentPaks(root));
    }

    [Fact]
    public void Walk_respects_the_directory_budget()
    {
        var root = Root();
        for (var i = 0; i < 50; i++) Directory.CreateDirectory(Path.Combine(root, "junk" + i));
        MakePaks(root, "zzz_last", "deep"); // a real one that a tiny budget won't reach
        var cands = UeProjectScan.Enumerate(root, new ScanBudget(MaxDirs: 5));
        Assert.True(cands.Count <= 1); // budget stops the walk before exhausting all junk dirs
    }
}
