using System.Text;
using ModManager.Core;

namespace ModManager.Tests;

// Golden-vector tests for Md5Hash — the file-identity hash Nexus md5_search expects.
// Lowercase hex, no separators. Vectors are the canonical RFC 1321 / well-known MD5 outputs.
public class Md5HashTests
{
    [Fact]
    public void OfBytes_abc_matches_golden_vector()
    {
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", Md5Hash.OfBytes(Encoding.UTF8.GetBytes("abc")));
    }

    [Fact]
    public void OfBytes_empty_matches_golden_vector()
    {
        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", Md5Hash.OfBytes(Array.Empty<byte>()));
    }

    [Fact]
    public void OfFile_round_trips_a_temp_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"md5-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("abc"));
            Assert.Equal("900150983cd24fb0d6963f7d28e17f72", Md5Hash.OfFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
