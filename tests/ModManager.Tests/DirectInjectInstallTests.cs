using System.IO.Compression;
using ModManager.Core;

namespace ModManager.Tests;

// Installing direct-inject mods by dropping a zip / files onto a FromSoft game: extract into the
// exe folder, flattening a single wrapper folder, refusing path-traversal, never overwriting.
public class DirectInjectInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mmb-dii-" + Guid.NewGuid().ToString("N"));
    private string Play => Path.Combine(_root, "Game");

    public DirectInjectInstallTests() => Directory.CreateDirectory(Play);
    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private string MakeZip(string name, params (string entry, string content)[] entries)
    {
        var path = Path.Combine(_root, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
            using (var w = new StreamWriter(zip.CreateEntry(entry).Open())) w.Write(content);
        return path;
    }

    // ---- pure helpers ----

    [Fact]
    public void WrapperPrefix_strips_a_single_wrapping_folder()
        => Assert.Equal("UltrawideFix", DirectInject.WrapperPrefix(new[] { "UltrawideFix/x.dll", "UltrawideFix/cfg.ini" }));

    [Fact]
    public void WrapperPrefix_is_null_when_files_sit_at_root()
    {
        Assert.Null(DirectInject.WrapperPrefix(new[] { "x.dll" }));                       // single root file
        Assert.Null(DirectInject.WrapperPrefix(new[] { "reshade-shaders/x.fx", "ReShade.ini" })); // mixed roots
    }

    [Fact]
    public void WrapperPrefix_never_treats_traversal_as_a_wrapper()
        => Assert.Null(DirectInject.WrapperPrefix(new[] { "../x.dll", "../y.dll" }));

    // ---- install ----

    [Fact]
    public void Installs_a_wrapped_zip_flattened_into_the_game_folder()
    {
        var zip = MakeZip("UltrawideFix.zip",
            ("UltrawideFix/ultrawidescreenfix.dll", "dll"),
            ("UltrawideFix/config.ini", "cfg"));

        var r = DirectInject.Install(Play, new[] { zip });

        Assert.True(File.Exists(Path.Combine(Play, "ultrawidescreenfix.dll")));
        Assert.True(File.Exists(Path.Combine(Play, "config.ini")));
        Assert.Equal(2, r.Added.Count);
        // And it's now recognized.
        Assert.Contains(DirectInject.Detect(Directory.GetFiles(Play).Select(Path.GetFileName)!, Array.Empty<string>()),
            m => m.Kind == "display");
    }

    [Fact]
    public void Refuses_zip_slip_entries_but_installs_safe_siblings()
    {
        var zip = MakeZip("evil.zip", ("good.dll", "ok"), ("../evil.dll", "pwn"));

        var r = DirectInject.Install(Play, new[] { zip });

        Assert.True(File.Exists(Path.Combine(Play, "good.dll")));
        Assert.False(File.Exists(Path.Combine(_root, "evil.dll"))); // never escaped the game folder
        Assert.Contains(r.Skipped, s => s.Reason.Contains("unsafe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Never_overwrites_an_existing_file()
    {
        File.WriteAllText(Path.Combine(Play, "config.ini"), "MINE");
        var zip = MakeZip("w.zip", ("config.ini", "theirs"));

        var r = DirectInject.Install(Play, new[] { zip });

        Assert.Equal("MINE", File.ReadAllText(Path.Combine(Play, "config.ini"))); // untouched
        Assert.Contains(r.Skipped, s => s.Reason.Contains("already", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Installs_a_loose_dropped_file()
    {
        var src = Path.Combine(_root, "EldenRing_Ultrawide.dll");
        File.WriteAllText(src, "dll");

        var r = DirectInject.Install(Play, new[] { src });

        Assert.True(File.Exists(Path.Combine(Play, "EldenRing_Ultrawide.dll")));
        Assert.Single(r.Added);
    }
}
