using System;
using System.Collections.Generic;
using System.Net.Http;

namespace ModManager.Core.Nexus;

/// <summary>Builds the OAuth authorize URL and the token-endpoint POST bodies (PKCE, public client).</summary>
public static class NexusTokenRequest
{
    public static string BuildAuthorizeUrl(NexusOAuthConfig cfg, string redirectUri, string challenge, string state)
    {
        var q = new List<string>
        {
            "response_type=code",
            "client_id=" + Uri.EscapeDataString(cfg.ClientId),
            "redirect_uri=" + Uri.EscapeDataString(redirectUri),
            "scope=" + Uri.EscapeDataString(cfg.Scopes),
            "state=" + Uri.EscapeDataString(state),
            "code_challenge=" + Uri.EscapeDataString(challenge),
            "code_challenge_method=S256",
        };
        return cfg.AuthorizeUrl + "?" + string.Join("&", q);
    }

    public static FormUrlEncodedContent BuildExchangeBody(
        NexusOAuthConfig cfg, string code, string redirectUri, string verifier) =>
        new(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = cfg.ClientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = verifier,
        });

    public static FormUrlEncodedContent BuildRefreshBody(NexusOAuthConfig cfg, string refreshToken) =>
        new(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = cfg.ClientId,
            ["refresh_token"] = refreshToken,
        });
}
