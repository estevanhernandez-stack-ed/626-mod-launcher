using System;
using ModManager.Core.Nexus;
using Xunit;

public class NexusTokenResponseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parse_valid_full_response_builds_tokens_with_expiry_from_now()
    {
        const string json =
            "{ \"access_token\": \"acc\", \"refresh_token\": \"ref\", \"expires_in\": 1800, \"scope\": \"public\" }";

        var t = NexusTokenResponse.Parse(json, T0);

        Assert.NotNull(t);
        Assert.Equal("acc", t!.AccessToken);
        Assert.Equal("ref", t.RefreshToken);
        Assert.Equal("public", t.Scope);
        Assert.Equal(T0.AddSeconds(1800), t.ExpiresAtUtc);
    }

    [Fact]
    public void Parse_missing_access_token_returns_null()
    {
        const string json = "{ \"refresh_token\": \"ref\", \"expires_in\": 1800, \"scope\": \"public\" }";
        Assert.Null(NexusTokenResponse.Parse(json, T0));
    }

    [Fact]
    public void Parse_empty_access_token_returns_null()
    {
        const string json = "{ \"access_token\": \"\", \"refresh_token\": \"ref\" }";
        Assert.Null(NexusTokenResponse.Parse(json, T0));
    }

    [Fact]
    public void Parse_missing_optional_fields_uses_defaults()
    {
        const string json = "{ \"access_token\": \"acc\" }";

        var t = NexusTokenResponse.Parse(json, T0);

        Assert.NotNull(t);
        Assert.Equal("acc", t!.AccessToken);
        Assert.Equal("", t.RefreshToken);                   // refresh_token default
        Assert.Equal("", t.Scope);                          // scope default
        Assert.Equal(T0.AddSeconds(3600), t.ExpiresAtUtc);  // expires_in default 3600
    }

    [Fact]
    public void Parse_malformed_json_returns_null_without_throwing()
    {
        Assert.Null(NexusTokenResponse.Parse("not json {{{", T0));
        Assert.Null(NexusTokenResponse.Parse("", T0));
        Assert.Null(NexusTokenResponse.Parse("[1,2,3]", T0)); // valid JSON, wrong (non-object) shape
    }
}
