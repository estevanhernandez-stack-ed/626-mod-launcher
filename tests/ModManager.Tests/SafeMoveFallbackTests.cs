using ModManager.Core;

namespace ModManager.Tests;

// The copy-verify-delete fallback is the path every move between the game's drive and 626's data
// drive takes, and a fixture on one volume never reaches it through Move: the rename just works. So
// these call SafeMove.MoveByCopy directly. Every failing case asserts the end state the reversibility
// law asks for: the source byte-identical to before, and nothing left at dest.
public class SafeMoveFallbackTests
{
    private static Dictionary<string, byte[]> Snapshot(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(root, f), File.ReadAllBytes);

    private static SortedSet<string> Dirs(string root) =>
        new(Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Select(d => Path.GetRelativePath(root, d)),
            StringComparer.Ordinal);

    private static void AssertSameTree(Dictionary<string, byte[]> before, SortedSet<string> dirsBefore, string root)
    {
        var after = Snapshot(root);
        Assert.Equal(before.Keys.OrderBy(k => k, StringComparer.Ordinal), after.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (rel, bytes) in before) Assert.Equal(bytes, after[rel]);
        Assert.Equal(dirsBefore, Dirs(root));
    }

    // Names sort so the file a test locks comes LAST: every other file is copied (or deleted) before
    // the failure, which is what makes "partway" real rather than a failure on the first file.
    private static string MakeTree(string root)
    {
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(Path.Combine(src, "b-inner", "deeper"));
        Directory.CreateDirectory(Path.Combine(src, "c-empty"));
        File.WriteAllBytes(Path.Combine(src, "a-first.bin"), new byte[] { 1, 2, 3 });
        File.WriteAllText(Path.Combine(src, "b-inner", "mid.txt"), "MID");
        File.WriteAllText(Path.Combine(src, "b-inner", "deeper", "deep.txt"), "DEEP");
        File.WriteAllBytes(Path.Combine(src, "z-last.bin"), new byte[] { 9, 8, 7, 6 });
        return src;
    }

    [Fact]
    public void Folder_fallback_that_succeeds_moves_everything()
    {
        var root = TestSupport.TempDir("safemove-fb-");
        var src = MakeTree(root);
        var before = Snapshot(src); var dirsBefore = Dirs(src);
        var dest = Path.Combine(root, "dest");

        SafeMove.MoveByCopy(src, dest);

        Assert.False(Directory.Exists(src));
        AssertSameTree(before, dirsBefore, dest);   // empty folders travel too
    }

    [Fact]
    public void Locked_file_in_a_folder_fails_the_copy_and_leaves_the_source_as_it_was()
    {
        var root = TestSupport.TempDir("safemove-fb-");
        var src = MakeTree(root);
        var before = Snapshot(src); var dirsBefore = Dirs(src);
        var dest = Path.Combine(root, "dest");

        // No sharing at all: the copy cannot even read it. The game holding a file this way is the
        // case the caller turns into "close the game".
        using (new FileStream(Path.Combine(src, "z-last.bin"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var ex = Assert.ThrowsAny<IOException>(() => SafeMove.MoveByCopy(src, dest));
            Assert.Equal(SafeMove.HrSharingViolation, ex.HResult);
        }

        Assert.False(Directory.Exists(dest));
        AssertSameTree(before, dirsBefore, src);
    }

    [Fact]
    public void Locked_file_in_a_folder_fails_the_delete_partway_and_the_source_is_restored()
    {
        var root = TestSupport.TempDir("safemove-fb-");
        var src = MakeTree(root);
        var before = Snapshot(src); var dirsBefore = Dirs(src);
        var dest = Path.Combine(root, "dest");

        // Readable, so the copy succeeds; not deletable, so the source delete fails after every other
        // file is already gone. Without the undo, the source is left half-deleted.
        using (new FileStream(Path.Combine(src, "z-last.bin"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var ex = Assert.ThrowsAny<IOException>(() => SafeMove.MoveByCopy(src, dest));
            Assert.Equal(SafeMove.HrSharingViolation, ex.HResult);
        }

        Assert.False(Directory.Exists(dest));
        AssertSameTree(before, dirsBefore, src);
    }

    [Fact]
    public void Copy_failure_partway_leaves_no_dest_and_an_intact_source()
    {
        var root = TestSupport.TempDir("safemove-fb-");
        var src = MakeTree(root);
        var before = Snapshot(src); var dirsBefore = Dirs(src);
        // A parent that does not exist yet: the fallback creates it, so the undo removes it too.
        var dest = Path.Combine(root, "new-parent", "dest");

        var copied = 0;
        SafeMove.FallbackStepForTests = (step, _) =>
        {
            if (step == "copy" && ++copied == 3) throw new IOException("There is not enough space on the disk (test).");
        };
        try
        {
            var ex = Assert.ThrowsAny<IOException>(() => SafeMove.MoveByCopy(src, dest));
            Assert.Contains("not enough space", ex.Message);
        }
        finally { SafeMove.FallbackStepForTests = null; }

        Assert.False(Directory.Exists(dest));
        Assert.False(Directory.Exists(Path.Combine(root, "new-parent")));
        AssertSameTree(before, dirsBefore, src);
    }

    [Fact]
    public void Single_file_fallback_whose_delete_fails_leaves_the_source_and_no_dest()
    {
        var root = TestSupport.TempDir("safemove-fb-");
        var src = Path.Combine(root, "a.bin");
        File.WriteAllBytes(src, new byte[] { 4, 5, 6 });
        var dest = Path.Combine(root, "held", "a.bin");

        using (new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var ex = Assert.ThrowsAny<IOException>(() => SafeMove.MoveByCopy(src, dest));
            Assert.Equal(SafeMove.HrSharingViolation, ex.HResult);
        }

        Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(src));
        Assert.False(File.Exists(dest));
        Assert.False(Directory.Exists(Path.Combine(root, "held")));
    }

    [Fact]
    public void Existing_dest_makes_the_fallback_refuse_without_copying()
    {
        var root = TestSupport.TempDir("safemove-fb-");
        var src = MakeTree(root);
        var before = Snapshot(src); var dirsBefore = Dirs(src);
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "theirs.txt"), "NOT OURS");

        Assert.ThrowsAny<IOException>(() => SafeMove.MoveByCopy(src, dest));

        Assert.Equal(new[] { "theirs.txt" }, Directory.GetFiles(dest).Select(Path.GetFileName));
        Assert.Equal("NOT OURS", File.ReadAllText(Path.Combine(dest, "theirs.txt")));
        AssertSameTree(before, dirsBefore, src);
    }

    [Fact]
    public void Restore_that_fails_keeps_the_full_copy_at_dest_and_says_where_it_is()
    {
        var root = TestSupport.TempDir("safemove-fb-");
        var src = MakeTree(root);
        var before = Snapshot(src);
        var dest = Path.Combine(root, "dest");

        SafeMove.FallbackStepForTests = (step, _) =>
        {
            if (step == "restore") throw new IOException("restore blocked (test)");
        };
        try
        {
            using (new FileStream(Path.Combine(src, "z-last.bin"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var ex = Assert.ThrowsAny<IOException>(() => SafeMove.MoveByCopy(src, dest));
                Assert.Contains(dest, ex.Message);
            }
        }
        finally { SafeMove.FallbackStepForTests = null; }

        // The source is incomplete, so the copy at dest is the only full one, and it must survive.
        var kept = Snapshot(dest);
        Assert.Equal(before.Keys.OrderBy(k => k, StringComparer.Ordinal), kept.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (rel, bytes) in before) Assert.Equal(bytes, kept[rel]);
    }

    // A real junction, because the bug is in how enumeration treats one: AllDirectories walks through
    // it, and a file-by-file delete would then empty a folder that is not part of the move at all.
    // xunit 2.9 has no runtime skip, so a machine that cannot make one fails here with the reason
    // rather than passing without having tested anything.
    private static void MakeJunction(string link, string target)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0 && Directory.Exists(link),
            $"Could not create a junction for this test (mklink exit {p.ExitCode}): {err}");
    }

    [Fact]
    public void Folder_containing_a_junction_is_refused_and_nothing_is_changed()
    {
        var root = TestSupport.TempDir("safemove-fb-");
        var src = MakeTree(root);
        var target = Path.Combine(root, "elsewhere");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "t.txt"), "NOT PART OF THE MOVE");
        MakeJunction(Path.Combine(src, "b-inner", "link"), target);
        var before = Snapshot(src);
        var dest = Path.Combine(root, "dest");

        try
        {
            var ex = Assert.ThrowsAny<IOException>(() => SafeMove.MoveByCopy(src, dest));
            Assert.Contains("link", ex.Message);

            Assert.False(Directory.Exists(dest));
            Assert.Equal("NOT PART OF THE MOVE", File.ReadAllText(Path.Combine(target, "t.txt")));
            var after = Snapshot(src);   // reads through the junction, so its file is in both
            Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
            foreach (var (rel, bytes) in before) Assert.Equal(bytes, after[rel]);
            Assert.True(new DirectoryInfo(Path.Combine(src, "b-inner", "link")).Attributes.HasFlag(FileAttributes.ReparsePoint));
        }
        finally
        {
            var link = Path.Combine(src, "b-inner", "link");
            if (Directory.Exists(link)) Directory.Delete(link);   // the link only, never its target
        }
    }

    [Fact]
    public void File_changed_after_its_copy_is_not_deleted_and_the_source_is_put_back()
    {
        var root = TestSupport.TempDir("safemove-fb-");
        var src = MakeTree(root);
        var dest = Path.Combine(root, "dest");
        var mid = Path.Combine(src, "b-inner", "mid.txt");

        // The game appends to a file between its copy and its delete. Deleting it would lose the tail.
        SafeMove.FallbackStepForTests = (step, path) =>
        {
            if (step == "delete" && path == mid) File.AppendAllText(mid, "+TAIL");
        };
        try { Assert.ThrowsAny<IOException>(() => SafeMove.MoveByCopy(src, dest)); }
        finally { SafeMove.FallbackStepForTests = null; }

        Assert.Equal("MID+TAIL", File.ReadAllText(mid));                          // never deleted
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(src, "a-first.bin")));   // put back
        Assert.Equal("DEEP", File.ReadAllText(Path.Combine(src, "b-inner", "deeper", "deep.txt")));   // put back
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, File.ReadAllBytes(Path.Combine(src, "z-last.bin")));
        Assert.False(Directory.Exists(dest));
    }

    [Fact]
    public void Source_file_that_differs_from_its_copy_during_put_back_keeps_dest()
    {
        var root = TestSupport.TempDir("safemove-fb-");
        var src = MakeTree(root);
        var before = Snapshot(src);
        var dest = Path.Combine(root, "dest");
        var mid = Path.Combine(src, "b-inner", "mid.txt");
        var last = Path.Combine(src, "z-last.bin");

        // mid.txt changing stops the delete; then, while the source is being put back, a file that was
        // never deleted changes too. The source can no longer be confirmed as it was, so dest stays.
        SafeMove.FallbackStepForTests = (step, path) =>
        {
            if (step == "delete" && path == mid) File.AppendAllText(mid, "+TAIL");
            if (step == "restore") File.AppendAllText(last, "X");
        };
        try
        {
            var ex = Assert.ThrowsAny<IOException>(() => SafeMove.MoveByCopy(src, dest));
            Assert.Contains(dest, ex.Message);
        }
        finally { SafeMove.FallbackStepForTests = null; }

        var kept = Snapshot(dest);
        foreach (var (rel, bytes) in before) Assert.Equal(bytes, kept[rel]);
    }

    [Fact]
    public void Fast_path_rename_is_unchanged_for_a_folder()
    {
        var root = TestSupport.TempDir("safemove-fb-");
        var src = MakeTree(root);
        var before = Snapshot(src); var dirsBefore = Dirs(src);
        var dest = Path.Combine(root, "dest");

        SafeMove.Move(src, dest);

        Assert.False(Directory.Exists(src));
        AssertSameTree(before, dirsBefore, dest);
    }
}
