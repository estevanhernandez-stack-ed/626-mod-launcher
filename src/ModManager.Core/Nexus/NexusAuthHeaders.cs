using System.Net.Http;
using System.Net.Http.Headers;

namespace ModManager.Core.Nexus;

/// <summary>Stamps the OAuth bearer + Nexus ToS identification headers onto an outbound request.</summary>
public static class NexusAuthHeaders
{
    public static void Apply(HttpRequestMessage req, string? bearerToken, string appName, string? appVersion)
    {
        if (!string.IsNullOrEmpty(bearerToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        // ToS-mandated identification — always present, even unauthenticated.
        req.Headers.Remove("Application-Name");
        req.Headers.TryAddWithoutValidation("Application-Name", appName);
        req.Headers.Remove("Application-Version");
        req.Headers.TryAddWithoutValidation("Application-Version", appVersion ?? "");
    }

    /// <summary>
    /// Builds a HOST-OWNED copy of <paramref name="original"/> with the bearer + ToS headers stamped on
    /// the COPY. The caller's request is never mutated — so a plugin that supplied `original` cannot read
    /// the token back off it. Copies method, uri, content (+content headers), and non-content request headers.
    /// </summary>
    public static async Task<HttpRequestMessage> CloneWithAuthAsync(
        HttpRequestMessage original, string? bearerToken, string appName, string? appVersion,
        CancellationToken ct = default)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        if (original.Content is not null)
        {
            var bytes = await original.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var h in original.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        foreach (var h in original.Headers)                       // preserve non-content headers (Accept, etc.)
            clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        Apply(clone, bearerToken, appName, appVersion);            // stamp the CLONE (Apply is Remove-then-Add, idempotent)
        return clone;
    }
}
