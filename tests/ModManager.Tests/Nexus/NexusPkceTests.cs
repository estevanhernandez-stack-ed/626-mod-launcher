using System;
using System.Security.Cryptography;
using System.Text;
using ModManager.Core.Nexus;
using Xunit;

public class NexusPkceTests
{
    [Fact]
    public void Verifier_is_url_safe_and_in_length_range()
    {
        var v = NexusPkce.CreateVerifier();
        Assert.InRange(v.Length, 43, 128);                 // RFC 7636
        Assert.DoesNotContain('+', v);
        Assert.DoesNotContain('/', v);
        Assert.DoesNotContain('=', v);
    }

    [Fact]
    public void Challenge_is_base64url_sha256_of_verifier()
    {
        var v = "test_verifier_value_for_pkce_1234567890abcd";
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(v)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        Assert.Equal(expected, NexusPkce.Challenge(v));
    }

    [Fact]
    public void State_matches_only_itself_ordinal()
    {
        var s = NexusPkce.CreateState();
        Assert.True(NexusPkce.StateMatches(s, s));
        Assert.False(NexusPkce.StateMatches(s, s + "x"));
        Assert.False(NexusPkce.StateMatches(s, s.ToUpperInvariant()));
    }
}
