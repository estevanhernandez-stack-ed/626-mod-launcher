using System;
using System.Text;
using ModManager.Core.Nexus;
using Xunit;

public class NexusJwtClaimsTests
{
    // Build a JWT (header.payload.signature) with a given payload JSON — only the payload segment matters.
    private static string Jwt(string payloadJson)
    {
        static string B64Url(string s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return B64Url("{\"alg\":\"RS256\"}") + "." + B64Url(payloadJson) + ".sig-not-verified";
    }

    [Fact]
    public void Reads_username_and_premium_from_membership_roles()
    {
        var jwt = Jwt("{\"user\":{\"id\":12345,\"username\":\"EstePremium\"," +
                      "\"membership_roles\":[\"member\",\"supporter\",\"premium\"]}}");
        var (user, premium) = NexusJwtClaims.ReadIdentity(jwt);
        Assert.Equal("EstePremium", user);
        Assert.True(premium);
    }

    [Fact]
    public void Lifetimepremium_counts_as_premium()
    {
        var jwt = Jwt("{\"user\":{\"username\":\"Lifer\",\"membership_roles\":[\"member\",\"lifetimepremium\"]}}");
        var (_, premium) = NexusJwtClaims.ReadIdentity(jwt);
        Assert.True(premium);
    }

    [Fact]
    public void Member_only_is_not_premium()
    {
        var jwt = Jwt("{\"user\":{\"username\":\"FreeUser\",\"membership_roles\":[\"member\"]}}");
        var (user, premium) = NexusJwtClaims.ReadIdentity(jwt);
        Assert.Equal("FreeUser", user);
        Assert.False(premium);
    }

    [Fact]
    public void Malformed_token_yields_null_identity_no_throw()
    {
        Assert.Equal((null, false), NexusJwtClaims.ReadIdentity("not-a-jwt"));
        Assert.Equal((null, false), NexusJwtClaims.ReadIdentity(""));
        Assert.Equal((null, false), NexusJwtClaims.ReadIdentity(null));
        Assert.Equal((null, false), NexusJwtClaims.ReadIdentity("aaa.!!!notbase64!!!.ccc"));
    }
}
