using System.Text.Json;

namespace ModManager.Core.Nexus;

/// <summary>
/// OAuth client configuration. Endpoints are baked (public, stable); ClientId arrives via the
/// signed startup overlay (empty until Nexus registers us). All values are public-by-design in a
/// PKCE public client — safe to ship in git and in a signed feed.
/// BUILD-TIME: confirm AuthorizeUrl/TokenUrl/Scopes against Nexus's OAuth guide before release.
/// </summary>
public sealed record NexusOAuthConfig(string ClientId, string AuthorizeUrl, string TokenUrl, string Scopes)
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // CONFIRM these two URLs + the scope string against Nexus's OAuth guide at build time.
    public static readonly NexusOAuthConfig Baked = new(
        ClientId: "",
        AuthorizeUrl: "https://users.nexusmods.com/oauth/authorize",
        TokenUrl: "https://users.nexusmods.com/oauth/token",
        Scopes: "public");

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

    /// <summary>Overlay a remote config over the baked default: any non-blank remote field wins.</summary>
    public NexusOAuthConfig Overlay(NexusOAuthConfig? remote)
    {
        if (remote is null) return this;
        static string Pick(string remoteVal, string baseVal) =>
            string.IsNullOrWhiteSpace(remoteVal) ? baseVal : remoteVal;
        return new NexusOAuthConfig(
            Pick(remote.ClientId, ClientId),
            Pick(remote.AuthorizeUrl, AuthorizeUrl),
            Pick(remote.TokenUrl, TokenUrl),
            Pick(remote.Scopes, Scopes));
    }
}
