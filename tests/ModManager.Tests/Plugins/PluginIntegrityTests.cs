// tests/ModManager.Tests/Plugins/PluginIntegrityTests.cs
using System.Text;
using ModManager.Core.Plugins;

namespace ModManager.Tests.Plugins;

public class PluginIntegrityTests
{
    // Known vector: SHA-256("abc") = ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad
    private static readonly byte[] Abc = Encoding.ASCII.GetBytes("abc");
    private const string AbcHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Fact]
    public void Sha256Hex_matches_the_known_vector_in_lowercase()
        => Assert.Equal(AbcHash, PluginIntegrity.Sha256Hex(Abc));

    [Fact]
    public void Sha256Matches_is_case_insensitive()
    {
        Assert.True(PluginIntegrity.Sha256Matches(Abc, AbcHash));
        Assert.True(PluginIntegrity.Sha256Matches(Abc, AbcHash.ToUpperInvariant()));
    }

    [Fact]
    public void Sha256Matches_rejects_a_wrong_hash()
        => Assert.False(PluginIntegrity.Sha256Matches(Abc, new string('0', 64)));
}
