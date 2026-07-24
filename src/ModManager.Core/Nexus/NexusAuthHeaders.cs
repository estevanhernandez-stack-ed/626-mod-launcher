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
}
