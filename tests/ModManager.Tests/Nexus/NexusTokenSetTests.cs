using System;
using System.Text.Json;
using ModManager.Core.Nexus;
using Xunit;

public class NexusTokenSetTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FromTokenResponse_sets_expiry_from_expires_in()
    {
        var t = NexusTokenSet.FromTokenResponse("a", "r", 3600, "public", T0);
        Assert.Equal(T0.AddSeconds(3600), t.ExpiresAtUtc);
    }

    [Fact]
    public void NeedsRefresh_true_within_skew_of_expiry()
    {
        var t = NexusTokenSet.FromTokenResponse("a", "r", 3600, "public", T0);
        Assert.False(t.NeedsRefresh(T0.AddSeconds(3000), TimeSpan.FromMinutes(5)));
        Assert.True(t.NeedsRefresh(T0.AddSeconds(3400), TimeSpan.FromMinutes(5))); // within 5m of 3600
        Assert.True(t.NeedsRefresh(T0.AddSeconds(4000), TimeSpan.FromMinutes(5))); // already expired
    }

    [Fact]
    public void RoundTrips_as_camelCase()
    {
        var t = NexusTokenSet.FromTokenResponse("acc", "ref", 3600, "public", T0);
        var json = JsonSerializer.Serialize(t, NexusTokenSet.JsonOpts);
        Assert.Contains("\"accessToken\"", json);
        Assert.Contains("\"refreshToken\"", json);
        Assert.Contains("\"expiresAtUtc\"", json);
        Assert.DoesNotContain("\"AccessToken\"", json);
        var back = JsonSerializer.Deserialize<NexusTokenSet>(json, NexusTokenSet.JsonOpts)!;
        Assert.Equal(t, back);
    }

    [Fact]
    public void ToString_redacts_secret_tokens()
    {
        var t = NexusTokenSet.FromTokenResponse("SUPER_SECRET_ACCESS", "SUPER_SECRET_REFRESH", 3600, "public", T0);
        var s = t.ToString();
        Assert.DoesNotContain("SUPER_SECRET_ACCESS", s);
        Assert.DoesNotContain("SUPER_SECRET_REFRESH", s);
        Assert.Contains("<redacted>", s);
        Assert.Contains("public", s);   // non-secret fields still shown
    }
}
