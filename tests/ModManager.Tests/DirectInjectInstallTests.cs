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

    // Build a source folder under _root with the given relative files, return its absolute path.
    private string MakeDir(string folderName, params (string rel, string content)[] files)
    {
        var dir = Path.Combine(_root, folderName);
        foreach (var (rel, content) in files)
        {
            var dest = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, content);
        }
        return dir;
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

    // ---- dropped folder (A2: flatten the wrapper like a zip, don't nest under the folder name) ----

    [Fact]
    public void Installs_a_dropped_folder_flattened_not_nested_under_its_name()
    {
        var src = MakeDir("SomeMod v1.2",
            ("ersc.dll", "dll"),
            ("ersc_settings.ini", "cfg"));

        var r = DirectInject.Install(Play, new[] { src });

        // Contents land directly in the game folder — the dropped folder name is the stripped wrapper.
        Assert.True(File.Exists(Path.Combine(Play, "ersc.dll")));
        Assert.True(File.Exists(Path.Combine(Play, "ersc_settings.ini")));
        Assert.False(Directory.Exists(Path.Combine(Play, "SomeMod v1.2"))); // not nested under the folder name
        Assert.Equal(2, r.Added.Count);
        // And the flattened layout is now recognized as the mod it is.
        Assert.Contains(
            DirectInject.Detect(Directory.GetFiles(Play).Select(Path.GetFileName)!, Array.Empty<string>()),
            m => m.Name == "Seamless Co-op");
    }

    [Fact]
    public void Dropped_folder_preserves_inner_subfolders_not_wrongly_flattened()
    {
        // Only the dropped folder's own name is the wrapper; meaningful inner folders survive.
        var src = MakeDir("SomeMod",
            ("ersc.dll", "dll"),
            ("SeamlessCoop/config.ini", "cfg"));

        var r = DirectInject.Install(Play, new[] { src });

        Assert.True(File.Exists(Path.Combine(Play, "ersc.dll")));
        Assert.True(File.Exists(Path.Combine(Play, "SeamlessCoop", "config.ini"))); // inner folder kept
        Assert.False(File.Exists(Path.Combine(Play, "config.ini")));                 // not over-flattened
        Assert.Equal(2, r.Added.Count);
    }

    [Fact]
    public void Plan_for_a_dropped_folder_strips_the_wrapper_name()
    {
        var src = MakeDir("UltrawideFix v3",
            ("ultrawidescreenfix.dll", "dll"),
            ("config.ini", "cfg"));

        var plan = DirectInject.Plan(Play, new[] { src });

        // RelPaths are relative to the game folder, with the dropped folder name stripped.
        var rels = plan.ToAdd.Select(a => a.RelPath).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "config.ini", "ultrawidescreenfix.dll" }, rels);
        Assert.DoesNotContain(plan.ToAdd, a => a.RelPath.Contains("UltrawideFix"));
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
