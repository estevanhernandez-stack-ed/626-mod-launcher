using System;
using System.Text.Json;

namespace ModManager.Core.Nexus;

/// <summary>
/// Pure, testable serialization for the Nexus OAuth token store. The App owns file IO + DPAPI; this type
/// owns the on-disk SHAPE and the legacy-key migration decision, with protect/unprotect INJECTED as
/// delegates so the security-critical logic is unit-testable (passthrough in tests, DPAPI in production).
/// No UI, no DPAPI type, no file IO lives here — Core stays pure, and the round-trip + legacy-discard +
/// fail-closed behavior is pinned by real xUnit tests.
///
/// On-disk shape (camelCase is LAW — shared with the launcher's other state files):
/// <code>{ "tokensProtected": "&lt;protected blob&gt;", "connectedUser": "Name" }</code>
/// The token material is serialized with <see cref="NexusTokenSet.JsonOpts"/> and handed to
/// <c>protect</c> before it touches disk — readable token values never appear in the envelope.
/// </summary>
public static class NexusTokenStore
{
    /// <summary>camelCase envelope options — the on-disk key convention every launcher state file follows.</summary>
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>The on-disk envelope. <c>tokensProtected</c> is the protected (DPAPI + base64 in production)
    /// token blob, null when disconnected. <c>connectedUser</c> is the display name — not secret.</summary>
    public sealed record StoreEnvelope(string? TokensProtected, string? ConnectedUser);

    /// <summary>Outcome of <see cref="Load"/>. <c>LegacyKeyDiscarded</c> is true when the raw file was the
    /// old api-key shape — the key is discarded (non-compliant under OAuth) and the caller should delete the
    /// file and prompt a fresh connect.</summary>
    public sealed record LoadResult(NexusTokenSet? Tokens, string? ConnectedUser, bool LegacyKeyDiscarded);

    /// <summary>Serialize the token store to camelCase envelope JSON. <paramref name="protect"/> encrypts the
    /// token blob (DPAPI + base64 in production, identity in tests); it is applied to the serialized
    /// <see cref="NexusTokenSet"/> before it is wrapped, so plaintext token values never reach disk.</summary>
    public static string Serialize(NexusTokenSet? tokens, string? user, Func<string, string> protect)
    {
        string? protectedBlob = tokens is null
            ? null
            : protect(JsonSerializer.Serialize(tokens, NexusTokenSet.JsonOpts));
        return JsonSerializer.Serialize(new StoreEnvelope(protectedBlob, user), JsonOpts);
    }

    /// <summary>Parse the token store from raw JSON, never throwing. Order matters:
    /// (1) legacy detection FIRST — a top-level <c>apiKey</c>/<c>apiKeyProtected</c> is the old shape, so
    ///     discard it (keys are non-compliant) and report <c>LegacyKeyDiscarded</c>;
    /// (2) else deserialize the envelope and <paramref name="unprotect"/> the token blob;
    /// (3) any parse failure, unprotect failure (null), or malformed token JSON → disconnected, no throw.</summary>
    public static LoadResult Load(string rawJson, Func<string, string?> unprotect)
    {
        try
        {
            using (var doc = JsonDocument.Parse(rawJson))
            {
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    (root.TryGetProperty("apiKey", out _) || root.TryGetProperty("apiKeyProtected", out _)))
                {
                    // Old api-key file — keys are non-compliant under OAuth. Discard, never migrate.
                    return new LoadResult(null, null, LegacyKeyDiscarded: true);
                }
            }

            var envelope = JsonSerializer.Deserialize<StoreEnvelope>(rawJson, JsonOpts);
            if (envelope is null) return new LoadResult(null, null, false);

            if (envelope.TokensProtected is { } protectedBlob)
            {
                var json = unprotect(protectedBlob);
                if (json is null) return new LoadResult(null, null, false); // undecryptable -> disconnected
                var tokens = JsonSerializer.Deserialize<NexusTokenSet>(json, NexusTokenSet.JsonOpts);
                return new LoadResult(tokens, envelope.ConnectedUser, false);
            }

            // Envelope with no token blob — a bare/disconnected file. Keep any stored user name.
            return new LoadResult(null, envelope.ConnectedUser, false);
        }
        catch
        {
            // Corrupt / unreadable JSON, or an unprotect that threw — treat as disconnected. Never throw.
            return new LoadResult(null, null, false);
        }
    }
}
