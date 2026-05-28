using ModManager.Core.RestorePoints;

namespace ModManager.Tests.RestorePoints;

public class FileTallyTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "rp-tally-" + Guid.NewGuid().ToString("n"));
    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    [Fact]
    public void Sha256_is_stable_and_differs_by_content()
    {
        Directory.CreateDirectory(_tmp);
        var a = Path.Combine(_tmp, "a.bin"); var b = Path.Combine(_tmp, "b.bin");
        File.WriteAllBytes(a, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(b, new byte[] { 1, 2, 4 });
        Assert.Equal(FileTally.Sha256(a), FileTally.Sha256(a));
        Assert.NotEqual(FileTally.Sha256(a), FileTally.Sha256(b));
    }

    [Fact]
    public void ByteSize_and_FileCount_sum_a_tree()
    {
        Directory.CreateDirectory(Path.Combine(_tmp, "sub"));
        File.WriteAllBytes(Path.Combine(_tmp, "top.bin"), new byte[10]);
        File.WriteAllBytes(Path.Combine(_tmp, "sub", "deep.bin"), new byte[5]);
        Assert.Equal(15, FileTally.ByteSize(_tmp));
        Assert.Equal(2, FileTally.FileCount(_tmp));
    }

    [Fact]
    public void ByteSize_of_missing_dir_is_zero()
        => Assert.Equal(0, FileTally.ByteSize(Path.Combine(_tmp, "nope")));
}
