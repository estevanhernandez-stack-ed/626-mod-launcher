using System;
using System.Text.Json;

namespace ModManager.Core.Nexus;

/// <summary>OAuth token material. SECRET — persisted only under DPAPI, never logged, never fed.</summary>
public sealed record NexusTokenSet(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc, string Scope)
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static NexusTokenSet FromTokenResponse(
        string accessToken, string refreshToken, int expiresInSeconds, string scope, DateTimeOffset now) =>
        new(accessToken, refreshToken, now.AddSeconds(expiresInSeconds), scope);

    /// <summary>True if the access token is expired or within <paramref name="skew"/> of expiring.</summary>
    public bool NeedsRefresh(DateTimeOffset now, TimeSpan skew) => now + skew >= ExpiresAtUtc;

    public override string ToString() =>
        $"NexusTokenSet {{ AccessToken = <redacted>, RefreshToken = <redacted>, ExpiresAtUtc = {ExpiresAtUtc:o}, Scope = {Scope} }}";
}
