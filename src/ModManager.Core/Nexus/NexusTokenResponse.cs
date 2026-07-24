using System;
using System.Text.Json;

namespace ModManager.Core.Nexus;

/// <summary>
/// Pure parse of an OAuth token-endpoint JSON body into a <see cref="NexusTokenSet"/>. Extracted from the
/// App OAuth service (mirroring the Task 8 Core seam) so the security-relevant field extraction is
/// unit-testable and fail-closed: a body without a usable <c>access_token</c>, or any malformed JSON, yields
/// null rather than a half-built token set. No HTTP, no UI, no HttpListener — Core stays pure.
/// </summary>
public static class NexusTokenResponse
{
    /// <summary>
    /// Parse a token-endpoint JSON body into a <see cref="NexusTokenSet"/>. Returns null when
    /// <c>access_token</c> is absent or empty, or when the body is not a valid JSON object — never throws.
    /// Reads <c>refresh_token</c> (default ""), <c>expires_in</c> (default 3600), <c>scope</c> (default "");
    /// the expiry instant is computed as <paramref name="now"/> + <c>expires_in</c> via
    /// <see cref="NexusTokenSet.FromTokenResponse"/>.
    /// </summary>
    public static NexusTokenSet? Parse(string json, DateTimeOffset now)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;

            var access = r.TryGetProperty("access_token", out var at) && at.ValueKind == JsonValueKind.String
                ? at.GetString()
                : null;
            if (string.IsNullOrEmpty(access)) return null;

            var refresh = r.TryGetProperty("refresh_token", out var rt) && rt.ValueKind == JsonValueKind.String
                ? rt.GetString() ?? ""
                : "";

            var scope = r.TryGetProperty("scope", out var sc) && sc.ValueKind == JsonValueKind.String
                ? sc.GetString() ?? ""
                : "";

            var expires = r.TryGetProperty("expires_in", out var ei)
                          && ei.ValueKind == JsonValueKind.Number
                          && ei.TryGetInt32(out var n)
                ? n
                : 3600;

            return NexusTokenSet.FromTokenResponse(access, refresh, expires, scope, now);
        }
        catch
        {
            // Malformed JSON or an unexpected element shape — fail closed (a token store never half-builds).
            return null;
        }
    }
}
