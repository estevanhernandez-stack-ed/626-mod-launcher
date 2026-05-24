using System.Text;
using ModManager.Core;

namespace ModManager.Tests;

// Ports the hash half of fingerprint-core.test.js — MurmurHash2 (32-bit, seed 1) over
// whitespace-stripped bytes. The fingerprintsRequest tests live with CurseForge builders.
public class FingerprintTests
{
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Murmur2_is_deterministic()
    {
        Assert.Equal(Fingerprint.Murmur2(B("the quick brown fox"), 1),
                     Fingerprint.Murmur2(B("the quick brown fox"), 1));
    }

    [Fact]
    public void Murmur2_is_sensitive_to_input_and_seed()
    {
        Assert.NotEqual(Fingerprint.Murmur2(B("abc"), 1), Fingerprint.Murmur2(B("abd"), 1));
        Assert.NotEqual(Fingerprint.Murmur2(B("abc"), 1), Fingerprint.Murmur2(B("abc"), 2));
    }

    [Fact]
    public void StripWhitespace_removes_tab_nl_cr_space_keeps_rest()
    {
        Assert.Equal("abc", Encoding.UTF8.GetString(Fingerprint.StripWhitespace(B(" a\tb\r\nc "))));
    }

    [Fact]
    public void CurseForgeFingerprint_ignores_whitespace()
    {
        Assert.Equal(
            Fingerprint.CurseForgeFingerprint(B("name=cool\nversion=1")),
            Fingerprint.CurseForgeFingerprint(B("  name=cool\r\n\tversion=1  ")));
    }

    [Fact]
    public void CurseForgeFingerprint_differs_for_different_content()
    {
        Assert.NotEqual(
            Fingerprint.CurseForgeFingerprint(B("mod A bytes")),
            Fingerprint.CurseForgeFingerprint(B("mod B bytes")));
    }

    [Fact]
    public void CurseForgeFingerprint_pins_known_values()
    {
        Assert.Equal(1807539333u, Fingerprint.CurseForgeFingerprint(B("mod-data")));
        Assert.Equal(1540447798u, Fingerprint.CurseForgeFingerprint(B("")));
    }
}
