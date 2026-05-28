using ModManager.Core;

namespace ModManager.Tests;

public class SafeMoveTests
{
    [Fact]
    public void Move_same_volume_renames_file()
    {
        var root = TestSupport.TempDir("safemove-");
        var src = Path.Combine(root, "a.txt"); var dest = Path.Combine(root, "b.txt");
        File.WriteAllText(src, "DATA");
        SafeMove.Move(src, dest);
        Assert.False(File.Exists(src));
        Assert.Equal("DATA", File.ReadAllText(dest));
    }

    [Fact]
    public void CopyFileVerified_copies_bytes_and_leaves_source_intact()
    {
        var root = TestSupport.TempDir("safemove-");
        var src = Path.Combine(root, "a.bin"); var dest = Path.Combine(root, "sub", "a.bin");
        File.WriteAllBytes(src, new byte[] { 1, 2, 3, 4 });
        SafeMove.CopyFileVerified(src, dest);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(dest));
        Assert.True(File.Exists(src));   // a verified copy NEVER deletes the source
    }

    [Fact]
    public void CopyFileVerified_throws_and_preserves_a_preexisting_dest()
    {
        var root = TestSupport.TempDir("safemove-");
        var src = Path.Combine(root, "a.bin"); var dest = Path.Combine(root, "a.bin.copy");
        File.WriteAllBytes(src, new byte[] { 1 });
        File.WriteAllBytes(dest, new byte[] { 9 });
        Assert.ThrowsAny<IOException>(() => SafeMove.CopyFileVerified(src, dest));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(dest));  // pre-existing file untouched
    }

    [Fact]
    public void CopyDirVerified_reproduces_a_nested_tree()
    {
        var root = TestSupport.TempDir("safemove-");
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(Path.Combine(src, "inner"));
        File.WriteAllText(Path.Combine(src, "top.txt"), "T");
        File.WriteAllText(Path.Combine(src, "inner", "deep.txt"), "D");
        var dest = Path.Combine(root, "dest");
        SafeMove.CopyDirVerified(src, dest);
        Assert.Equal("T", File.ReadAllText(Path.Combine(dest, "top.txt")));
        Assert.Equal("D", File.ReadAllText(Path.Combine(dest, "inner", "deep.txt")));
    }

    [Fact]
    public void Move_surfaces_sharing_violation_instead_of_copying()
    {
        var root = TestSupport.TempDir("safemove-");
        var src = Path.Combine(root, "locked.bin");
        var dest = Path.Combine(root, "moved.bin");
        File.WriteAllBytes(src, new byte[] { 1, 2, 3 });

        // Hold an exclusive handle (no sharing) so a move of src raises a sharing violation —
        // the "game is running and has the file open" case.
        using var hold = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.None);

        // The sharing violation must SURFACE, not fall through to a (doomed) copy.
        Assert.ThrowsAny<IOException>(() => SafeMove.Move(src, dest));
        Assert.False(File.Exists(dest));   // no copy was attempted
        Assert.True(File.Exists(src));     // source preserved
    }
}
