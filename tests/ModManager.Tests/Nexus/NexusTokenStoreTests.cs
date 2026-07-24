using System;
using ModManager.Core.Nexus;
using Xunit;

/// <summary>
/// Real tests for the pure Core token store. DPAPI protect/unprotect are INJECTED as delegates, so the
/// security-critical serialization + legacy-discard + fail-closed logic runs headless with a passthrough
/// protector (production wires DPAPI). Secret markers mirror <c>NexusTokenSetTests</c>.
/// </summary>
public class NexusTokenStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static NexusTokenSet SampleTokens() =>
        NexusTokenSet.FromTokenResponse("SUPER_SECRET_ACCESS", "SUPER_SECRET_REFRESH", 3600, "public", T0);

    [Fact]
    public void Serialize_then_Load_round_trips_tokens_and_user()
    {
        var tokens = SampleTokens();

        var json = NexusTokenStore.Serialize(tokens, "TestUser", s => s);   // passthrough protect
        var result = NexusTokenStore.Load(json, s => s);                    // passthrough unprotect

        Assert.False(result.LegacyKeyDiscarded);
        Assert.Equal("TestUser", result.ConnectedUser);
        Assert.NotNull(result.Tokens);
        Assert.Equal(tokens.AccessToken, result.Tokens!.AccessToken);
        Assert.Equal(tokens.RefreshToken, result.Tokens.RefreshToken);
        Assert.Equal(tokens.ExpiresAtUtc, result.Tokens.ExpiresAtUtc);
        Assert.Equal(tokens.Scope, result.Tokens.Scope);
    }

    [Fact]
    public void Serialize_writes_camelCase_envelope_keys()
    {
        var json = NexusTokenStore.Serialize(SampleTokens(), "TestUser", s => s);

        Assert.Contains("\"tokensProtected\"", json);
        Assert.Contains("\"connectedUser\"", json);
        Assert.DoesNotContain("\"TokensProtected\"", json);
        Assert.DoesNotContain("\"ConnectedUser\"", json);
    }

    [Fact]
    public void Serialize_with_no_tokens_writes_null_blob_and_loads_disconnected()
    {
        var json = NexusTokenStore.Serialize(null, null, s => s);
        var result = NexusTokenStore.Load(json, s => s);

        Assert.Null(result.Tokens);
        Assert.False(result.LegacyKeyDiscarded);
    }

    [Fact]
    public void Load_legacy_apiKey_file_is_discarded()
    {
        var result = NexusTokenStore.Load("{\"apiKey\":\"abc\"}", s => s);

        Assert.True(result.LegacyKeyDiscarded);
        Assert.Null(result.Tokens);
        Assert.Null(result.ConnectedUser);
    }

    [Fact]
    public void Load_legacy_apiKeyProtected_file_is_discarded()
    {
        var result = NexusTokenStore.Load(
            "{\"apiKeyProtected\":\"BASE64==\",\"userName\":\"Old\",\"premium\":true}", s => s);

        Assert.True(result.LegacyKeyDiscarded);
        Assert.Null(result.Tokens);
        Assert.Null(result.ConnectedUser);
    }

    [Fact]
    public void Load_corrupt_json_returns_disconnected_without_throwing()
    {
        var result = NexusTokenStore.Load("{ this is not valid json ", s => s);

        Assert.Null(result.Tokens);
        Assert.Null(result.ConnectedUser);
        Assert.False(result.LegacyKeyDiscarded);
    }

    [Fact]
    public void Load_unprotect_failure_returns_disconnected_without_throwing()
    {
        // A well-formed envelope whose blob can't be unprotected (delegate returns null) -> disconnected.
        var json = NexusTokenStore.Serialize(SampleTokens(), "TestUser", s => s);
        var result = NexusTokenStore.Load(json, _ => null);

        Assert.Null(result.Tokens);
        Assert.False(result.LegacyKeyDiscarded);
    }

    [Fact]
    public void Serialize_does_not_leak_plaintext_token_values_when_protected()
    {
        // With a real (opaque) protector, the readable token values must never appear in the envelope JSON.
        var json = NexusTokenStore.Serialize(SampleTokens(), "TestUser", _ => "OPAQUE_BLOB");

        Assert.Contains("OPAQUE_BLOB", json);
        Assert.DoesNotContain("SUPER_SECRET_ACCESS", json);
        Assert.DoesNotContain("SUPER_SECRET_REFRESH", json);
    }
}
