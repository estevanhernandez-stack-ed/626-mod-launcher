using System.IO.Compression;
using ModManager.Core;

namespace ModManager.Tests;

// Symptom 2 follow-up: now that the launcher OWNS the UE4SS install, a dropped Lua mod should land in
// ue4ss\Mods\<modName>\ — validate-then-extract, re-rooting a version-wrapped archive, reversible.
// The "Windrose Shanties Anywhere" archive is the real shape: <version>/<mod>/Scripts/main.lua.
public class Ue4ssLuaInstallerTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "ue4ss-lua-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    // The live UE4SS Mods folder the launcher owns (under <gameRoot>/R5/Binaries/Win64/ue4ss/Mods).
    private string MakeModsDir()
    {
        var d = Path.Combine(_tmp, "R5", "Binaries", "Win64", "ue4ss", "Mods");
        Directory.CreateDirectory(d);
        return d;
    }

    private string BuildZip(params (string Path, byte[] Bytes)[] entries)
    {
        var zipPath = Path.Combine(_tmp, $"src-{Guid.NewGuid():n}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using var stream = File.Create(zipPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (path, bytes) in entries)
        {
            using var es = zip.CreateEntry(path).Open();
            es.Write(bytes, 0, bytes.Length);
        }
        return zipPath;
    }

    private static byte[] B(params byte[] b) => b;

    // The real "shanties" shape: version wrapper -> mod folder -> Scripts/main.lua + enabled.txt + mod.txt.
    private string ShantiesZip() => BuildZip(
        ("Windrose Shanties Anywhere v1/Windrose Shanties Anywhere/enabled.txt", B(1)),
        ("Windrose Shanties Anywhere v1/Windrose Shanties Anywhere/mod.txt", B(2)),
        ("Windrose Shanties Anywhere v1/Windrose Shanties Anywhere/Scripts/main.lua", B(3)));

    [Fact]
    public void Install_reroots_a_version_wrapped_archive_into_Mods_modName()
    {
        var modsDir = MakeModsDir();

        var r = Ue4ssLuaInstaller.Install(ShantiesZip(), modsDir);

        var modRoot = Path.Combine(modsDir, "Windrose Shanties Anywhere");
        Assert.True(File.Exists(Path.Combine(modRoot, "Scripts", "main.lua")));  // re-rooted, wrapper stripped
        Assert.True(File.Exists(Path.Combine(modRoot, "enabled.txt")));
        Assert.True(File.Exists(Path.Combine(modRoot, "mod.txt")));
        Assert.False(Directory.Exists(Path.Combine(modsDir, "Windrose Shanties Anywhere v1"))); // wrapper not carried
        Assert.Equal("Windrose Shanties Anywhere", r.ModName);
    }

    [Fact]
    public void Install_is_reversible_leaving_other_mods_untouched()
    {
        var modsDir = MakeModsDir();
        // A pre-existing sibling mod must survive uninstall byte-for-byte.
        var sibling = Path.Combine(modsDir, "ConsoleEnablerMod", "Scripts");
        Directory.CreateDirectory(sibling);
        File.WriteAllBytes(Path.Combine(sibling, "main.lua"), B(9, 9));

        var r = Ue4ssLuaInstaller.Install(ShantiesZip(), modsDir);
        Assert.True(Directory.Exists(Path.Combine(modsDir, "Windrose Shanties Anywhere")));

        Ue4ssLuaInstaller.Uninstall(modsDir, r.ModName);

        Assert.False(Directory.Exists(Path.Combine(modsDir, "Windrose Shanties Anywhere"))); // gone
        Assert.Equal(B(9, 9), File.ReadAllBytes(Path.Combine(sibling, "main.lua")));         // sibling intact
    }

    [Fact]
    public void Install_refuses_to_overwrite_an_existing_mod_of_the_same_name()
    {
        var modsDir = MakeModsDir();
        // Same leaf name already present -> refuse rather than clobber (reversibility: no silent replace).
        Directory.CreateDirectory(Path.Combine(modsDir, "Windrose Shanties Anywhere"));
        File.WriteAllText(Path.Combine(modsDir, "Windrose Shanties Anywhere", "keep.txt"), "mine");

        var ex = Assert.Throws<InvalidOperationException>(() => Ue4ssLuaInstaller.Install(ShantiesZip(), modsDir));
        Assert.Contains("already", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(modsDir, "Windrose Shanties Anywhere", "keep.txt"))); // untouched
        Assert.False(File.Exists(Path.Combine(modsDir, "Windrose Shanties Anywhere", "Scripts", "main.lua")));
    }

    [Fact]
    public void Install_refuses_directory_traversal_without_writing_anything()
    {
        var modsDir = MakeModsDir();
        var hostile = BuildZip(
            ("Mod/Scripts/main.lua", B(1)),
            ("Mod/../../../../Windows/System32/evil.dll", B(66)));

        Assert.Throws<InvalidOperationException>(() => Ue4ssLuaInstaller.Install(hostile, modsDir));
        // Nothing landed — the mod folder wasn't created.
        Assert.False(Directory.Exists(Path.Combine(modsDir, "Mod")));
    }

    [Fact]
    public void Install_refuses_a_non_lua_archive()
    {
        var modsDir = MakeModsDir();
        // A content (pak) archive is not a Lua mod — the installer must refuse, not guess.
        var pak = BuildZip(("CoolMod_P.pak", B(1, 2, 3)));
        var ex = Assert.Throws<InvalidOperationException>(() => Ue4ssLuaInstaller.Install(pak, modsDir));
        Assert.Contains("lua", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_mid_extract_failure_rolls_back_to_a_clean_state()
    {
        // The heart of the reversibility claim: if extraction throws partway, the live Mods folder is
        // left clean — no mod folder, no leftover .staging-* debris. Inject a reader that throws on the
        // 2nd entry so the first file is staged before the failure.
        var modsDir = MakeModsDir();
        var zip = ShantiesZip();
        var throwingReader = new ThrowOnNthExtractReader(zip, throwOnExtractCall: 2);

        Assert.Throws<IOException>(() => Ue4ssLuaInstaller.Install(zip, modsDir, throwingReader));

        Assert.False(Directory.Exists(Path.Combine(modsDir, "Windrose Shanties Anywhere"))); // no partial mod
        Assert.Empty(Directory.GetDirectories(modsDir, ".*staging*"));                        // no orphan staging
    }

    [Fact]
    public void Install_reaps_a_stale_staging_dir_left_by_an_earlier_crash()
    {
        // A hard crash (power loss) mid-extract can orphan a .<mod>.staging-<guid> dir. Because the
        // scanner now surfaces ue4ss\Mods as a location, that debris could render as a phantom row —
        // so a fresh install reaps stale staging dirs first.
        var modsDir = MakeModsDir();
        var orphan = Path.Combine(modsDir, ".staging-deadbeef");
        Directory.CreateDirectory(orphan);
        File.WriteAllText(Path.Combine(orphan, "junk.lua"), "x");

        Ue4ssLuaInstaller.Install(ShantiesZip(), modsDir);

        Assert.False(Directory.Exists(orphan));                                             // reaped
        Assert.True(Directory.Exists(Path.Combine(modsDir, "Windrose Shanties Anywhere"))); // and the install still works
    }

    // A reader that proxies a real zip but throws IOException on the Nth Extract call (1-based).
    private sealed class ThrowOnNthExtractReader : IArchiveReader
    {
        private readonly string _zipPath;
        private readonly int _throwOn;
        public ThrowOnNthExtractReader(string zipPath, int throwOnExtractCall) { _zipPath = zipPath; _throwOn = throwOnExtractCall; }
        public IArchiveHandle Open(string archivePath) => new Handle(new SharpCompressArchiveReader().Open(_zipPath), _throwOn);

        private sealed class Handle : IArchiveHandle
        {
            private readonly IArchiveHandle _inner;
            private readonly int _throwOn;
            private int _calls;
            public Handle(IArchiveHandle inner, int throwOn) { _inner = inner; _throwOn = throwOn; }
            public IReadOnlyList<string> EntryNames => _inner.EntryNames;
            public void Extract(string entryName, string destAbs, bool overwrite)
            {
                if (++_calls == _throwOn) throw new IOException("simulated mid-extract failure");
                _inner.Extract(entryName, destAbs, overwrite);
            }
            public void Dispose() => _inner.Dispose();
        }
    }
}
