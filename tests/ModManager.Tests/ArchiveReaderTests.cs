using System.Text;
using ModManager.Core;

namespace ModManager.Tests;

// The archive seam (ArchiveReader) replaces the scattered raw System.IO.Compression.ZipFile
// mod-archive reading with one Core abstraction backed by SharpCompress, so 7z/rar work
// everywhere zip does. These tests pin the contract:
//   - zip regression: behaves exactly like the old ZipFile path (file entries listed, dirs
//     excluded, bytes extracted faithfully, forward-slash keys).
//   - multi-format: a non-zip archive (tar) reads/extracts through the SAME seam — proving the
//     abstraction isn't zip-only.
//   - 7z/rar: real read tests are present but Skip-gated because no 7z/rar writer is available
//     in this environment (no 7z/7za CLI, no py7zr). They run once a fixture exists.
public class ArchiveReaderTests
{
    private static readonly IArchiveReader Reader = new SharpCompressArchiveReader();

    // ---- zip regression (the old ZipFile.OpenRead path) ----

    [Fact]
    public void Open_lists_file_entries_only_excluding_directories()
    {
        var dir = TestSupport.TempDir("arc-zip-list-");
        var zip = Path.Combine(dir, "pack.zip");
        // A nested entry creates an implicit directory; the seam must list only the FILE.
        TestSupport.WriteZip(zip,
            ("root.pak", "A"),
            ("sub/inner.pak", "B"));

        using var h = Reader.Open(zip);
        var names = h.EntryNames.OrderBy(n => n).ToList();

        Assert.Contains("root.pak", names);
        Assert.Contains("sub/inner.pak", names);     // forward-slash separators, like ZipArchiveEntry.FullName
        Assert.DoesNotContain(names, n => n.EndsWith("/"));   // no directory entries
        Assert.Equal(2, names.Count);
    }

    [Fact]
    public void Extract_writes_the_right_bytes()
    {
        var dir = TestSupport.TempDir("arc-zip-extract-");
        var zip = Path.Combine(dir, "pack.zip");
        TestSupport.WriteZip(zip, ("mod.pak", "hello-bytes"));

        var dest = Path.Combine(dir, "out", "mod.pak");
        using var h = Reader.Open(zip);
        h.Extract("mod.pak", dest, overwrite: true);

        Assert.True(File.Exists(dest));
        Assert.Equal("hello-bytes", File.ReadAllText(dest));
    }

    [Fact]
    public void Extract_creates_missing_destination_directory()
    {
        var dir = TestSupport.TempDir("arc-zip-mkdir-");
        var zip = Path.Combine(dir, "pack.zip");
        TestSupport.WriteZip(zip, ("a/b/c.pak", "deep"));

        var dest = Path.Combine(dir, "x", "y", "z.pak"); // none of x/y exist yet
        using var h = Reader.Open(zip);
        h.Extract("a/b/c.pak", dest, overwrite: false);

        Assert.Equal("deep", File.ReadAllText(dest));
    }

    [Fact]
    public void Extract_overwrite_false_throws_when_dest_exists()
    {
        var dir = TestSupport.TempDir("arc-zip-noover-");
        var zip = Path.Combine(dir, "pack.zip");
        TestSupport.WriteZip(zip, ("mod.pak", "new"));
        var dest = Path.Combine(dir, "mod.pak");
        File.WriteAllText(dest, "existing");

        using var h = Reader.Open(zip);
        Assert.ThrowsAny<Exception>(() => h.Extract("mod.pak", dest, overwrite: false));
        Assert.Equal("existing", File.ReadAllText(dest)); // untouched
    }

    [Fact]
    public void Extract_overwrite_true_replaces_existing()
    {
        var dir = TestSupport.TempDir("arc-zip-over-");
        var zip = Path.Combine(dir, "pack.zip");
        TestSupport.WriteZip(zip, ("mod.pak", "new"));
        var dest = Path.Combine(dir, "mod.pak");
        File.WriteAllText(dest, "old");

        using var h = Reader.Open(zip);
        h.Extract("mod.pak", dest, overwrite: true);
        Assert.Equal("new", File.ReadAllText(dest));
    }

    [Fact]
    public void Extract_unknown_entry_throws()
    {
        var dir = TestSupport.TempDir("arc-zip-miss-");
        var zip = Path.Combine(dir, "pack.zip");
        TestSupport.WriteZip(zip, ("real.pak", "x"));

        using var h = Reader.Open(zip);
        Assert.ThrowsAny<Exception>(() => h.Extract("ghost.pak", Path.Combine(dir, "out.pak"), overwrite: true));
    }

    // ---- multi-format regression: same seam, non-zip container (tar via SharpCompress) ----

    [Fact]
    public void Open_and_extract_work_on_a_tar_archive()
    {
        var dir = TestSupport.TempDir("arc-tar-");
        var tar = Path.Combine(dir, "pack.tar");
        TestSupport.WriteTar(tar,
            ("root.pak", "tar-A"),
            ("nested/leaf.pak", "tar-B"));

        using var h = Reader.Open(tar);
        var names = h.EntryNames.OrderBy(n => n).ToList();
        Assert.Contains("root.pak", names);
        Assert.Contains("nested/leaf.pak", names);
        Assert.DoesNotContain(names, n => n.EndsWith("/"));

        var dest = Path.Combine(dir, "out", "leaf.pak");
        h.Extract("nested/leaf.pak", dest, overwrite: true);
        Assert.Equal("tar-B", File.ReadAllText(dest));
    }

    // ---- 7z / rar: real read tests, Skip-gated until a fixture exists ----
    // No 7z/7za/7zr CLI and no py7zr are available in this environment, so we can't generate a
    // .7z/.rar fixture programmatically. SharpCompress READS both formats via the same
    // ArchiveFactory.Open path the seam uses; these are verified by live smoke against a real
    // 7z/rar download. Remove Skip + drop a tiny fixture under tests/fixtures to activate.

    [Fact(Skip = "needs a 7z fixture / verified by live smoke")]
    public void Open_and_extract_work_on_a_7z_archive()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "pack.7z");
        using var h = Reader.Open(fixture);
        Assert.NotEmpty(h.EntryNames);
        var dest = Path.Combine(TestSupport.TempDir("arc-7z-"), "out.pak");
        h.Extract(h.EntryNames[0], dest, overwrite: true);
        Assert.True(File.Exists(dest));
    }

    [Fact(Skip = "needs a rar fixture / verified by live smoke")]
    public void Open_and_extract_work_on_a_rar_archive()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "pack.rar");
        using var h = Reader.Open(fixture);
        Assert.NotEmpty(h.EntryNames);
        var dest = Path.Combine(TestSupport.TempDir("arc-rar-"), "out.pak");
        h.Extract(h.EntryNames[0], dest, overwrite: true);
        Assert.True(File.Exists(dest));
    }
}
