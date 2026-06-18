using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace ModManager.App.Services;

/// <summary>
/// The one App-side Nexus credential check: verify a personal key and read the account identity via
/// <c>GET /v1/users/validate.json</c>. Pulled out of <see cref="NexusService"/> as a small, pure,
/// WinUI-free helper so it is unit-testable over a mock <see cref="HttpMessageHandler"/> without
/// dragging the WinUI App (and its bare-<c>dotnet</c> build hang) into a test project — the same
/// transport-injection shape the relocated plugin tests use.
///
/// <para>This is deliberately the ONLY Nexus HTTP code left in the App: the read/write surface lives
/// in the Nexus plugin (<c>NexusModSource</c>), which has no validate path because <c>validate.json</c>
/// is purely a key-store concern, not an <c>IModSource</c> operation. Operating law #2 holds — the key
/// is supplied per call and never persisted by this helper.</para>
/// </summary>
internal static class NexusKeyValidator
{
    /// <summary>The Nexus-ToS application identity sent on the validate request (mirrors the plugin).</summary>
    internal const string ApplicationName = "626-mod-launcher";

    /// <summary>
    /// Verify a Nexus personal key and read the account identity over the shared <paramref name="http"/>,
    /// stamped with the Nexus-ToS identity headers (<c>apikey</c> + <c>Application-Name</c> +
    /// <c>Application-Version</c> + <c>Accept</c>). Returns the account name + premium flag, or null on a
    /// rejected key (401) or a non-object body. The validate response is snake_case (<c>name</c> /
    /// <c>is_premium</c>); any other non-2xx throws (the caller decides whether that is fatal).
    /// </summary>
    internal static async Task<(string? Name, bool IsPremium)?> ValidateAsync(
        HttpClient http, string key, string appVersion, CancellationToken ct = default)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/users/validate.json");
        msg.Headers.TryAddWithoutValidation("Accept", "application/json");
        msg.Headers.TryAddWithoutValidation("Application-Name", ApplicationName);
        msg.Headers.TryAddWithoutValidation("Application-Version", appVersion);
        msg.Headers.TryAddWithoutValidation("apikey", key);

        using var res = await http.SendAsync(msg, ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized) return null; // bad / expired key
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Nexus request failed ({(int)res.StatusCode})");

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        var premium = root.TryGetProperty("is_premium", out var p) && p.ValueKind == JsonValueKind.True;
        return (name, premium);
    }
}
