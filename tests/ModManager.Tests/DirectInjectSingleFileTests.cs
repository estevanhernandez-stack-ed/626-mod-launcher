using ModManager.Core;

namespace ModManager.Tests;

public class DirectInjectSingleFileTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "di-single-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    [Fact]
    public void DisableSingleFile_then_EnableSingleFile_round_trips()
    {
        var play = Path.Combine(_tmp, "play"); Directory.CreateDirectory(play);
        var holding = Path.Combine(_tmp, "hold");
        File.WriteAllText(Path.Combine(play, "dinput8.dll"), "proxy");

        DirectInject.DisableSingleFile(play, holding, "dinput8.dll");
        Assert.False(File.Exists(Path.Combine(play, "dinput8.dll")));   // stepped aside

        DirectInject.EnableSingleFile(play, holding, "dinput8.dll");
        Assert.Equal("proxy", File.ReadAllText(Path.Combine(play, "dinput8.dll"))); // restored byte-for-byte
    }

    [Fact]
    public void DisableSingleFile_is_a_safe_noop_when_the_file_is_absent()
    {
        var play = Path.Combine(_tmp, "play"); Directory.CreateDirectory(play);
        var holding = Path.Combine(_tmp, "hold");
        DirectInject.DisableSingleFile(play, holding, "dinput8.dll"); // not present -> no throw
        Assert.False(File.Exists(Path.Combine(holding, "dinput8.dll")));
    }

    [Fact]
    public void ProcessLoadProxiesIn_lists_only_recognized_proxy_names()
    {
        var names = DirectInject.ProcessLoadProxiesIn(new[]
        {
            @"C:\g\dinput8.dll", @"C:\g\game.exe", @"C:\g\version.dll"
        });
        Assert.Contains("dinput8.dll", names);
        Assert.Contains("version.dll", names);
        Assert.DoesNotContain(names, n => n == "game.exe");
    }
}
