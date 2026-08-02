using System;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ModManager.Core.Nexus;

/// <summary>
/// Reads the signed-in user's identity (display name + premium status) from a Nexus OAuth access token.
/// The Nexus access token is a JWT whose payload carries a <c>user</c> object with <c>username</c> and a
/// <c>membership_roles</c> array (e.g. "member", "supporter", "premium", "lifetimepremium"). We read our
/// OWN token's claims (obtained directly from the token endpoint over TLS) — no signature verification is
/// needed to display who's logged in, and no extra HTTP round-trip. Never throws: a malformed token yields
/// (null, false).
/// </summary>
public static class NexusJwtClaims
{
    public static (string? Username, bool Premium) ReadIdentity(string? accessToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(accessToken)) return (null, false);
            var parts = accessToken.Split('.');
            if (parts.Length < 2) return (null, false);   // not a JWT (header.payload.signature)

            var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("user", out var user) || user.ValueKind != JsonValueKind.Object)
                return (null, false);

            var username = user.TryGetProperty("username", out var u) ? u.GetString() : null;

            var premium = false;
            if (user.TryGetProperty("membership_roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
            {
                premium = roles.EnumerateArray().Any(r =>
                    r.ValueKind == JsonValueKind.String &&
                    (string.Equals(r.GetString(), "premium", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(r.GetString(), "lifetimepremium", StringComparison.OrdinalIgnoreCase)));
            }

            return (username, premium);
        }
        catch
        {
            return (null, false);   // malformed token — never break the connect flow over a display detail
        }
    }

    private static byte[] Base64UrlDecode(string segment)
    {
        var s = segment.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
