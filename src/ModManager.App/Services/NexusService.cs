using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>
/// Holds the user's Nexus connection: their own personal API key + the connection state.
/// The key is stored per-user at %APPDATA%\ModManagerBuilder\nexus.json (sibling to games.json) — it is
/// the USER's key, obtained at runtime, and is NEVER baked into the binary (operating law #2). At rest it
/// is **DPAPI-encrypted** (<see cref="DataProtectionScope.CurrentUser"/>), so the ciphertext is bound to
/// this Windows account — another user or machine can't read it. A pre-encryption plaintext file is
/// adopted once and immediately re-saved encrypted. When the SSO app slug is registered later, the SSO
/// handshake writes the same key sink; today the user pastes a personal key (account settings -> API
/// access). Headers are per-request, so sharing the singleton HttpClient with CurseForge is safe.
/// </summary>
public sealed class NexusService
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModManagerBuilder", "nexus.json");

    private static readonly JsonSerializerOptions Json =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private string? _key;

    /// <summary>The Nexus-ToS application identity sent on every validate request.</summary>
    private const string ApplicationName = "626-mod-launcher";

    /// <summary>The launcher version reported to Nexus via the ToS <c>Application-Version</c> header
    /// (mirrors <see cref="RemoteManifestSource"/>'s manifest-feed identity). Resolved once from the
    /// entry-assembly version, falling back to a fixed placeholder when the version is unavailable.</summary>
    private static readonly string AppVersionString =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    public NexusService(HttpClient http)
    {
        _http = http;
        Load();
    }

    public bool IsConnected => !string.IsNullOrEmpty(_key);
    public string? ConnectedUser { get; private set; }
    public bool ConnectedPremium { get; private set; }

    /// <summary>The host-owned credential lookup the <c>PluginHost</c> hands a plugin via
    /// <c>IPluginHostServices.GetCredential</c>. Returns the on-machine Nexus key (decrypted in memory,
    /// never persisted by the plugin) for the <c>"nexus"</c> key; null for anything else or when not
    /// connected. The plugin receives the value per call and gets no handle it could exfiltrate.</summary>
    public string? GetCredential(string key)
        => string.Equals(key, "nexus", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_key) ? _key : null;

    /// <summary>Validate a pasted personal key against Nexus; on success store it (+ the account name)
    /// and connect. Returns the account name, or null if the key was rejected (401).</summary>
    public async Task<string?> ConnectAsync(string apiKey)
    {
        apiKey = (apiKey ?? "").Trim();
        if (apiKey.Length == 0) return null;
        var user = await ValidateKeyAsync(apiKey); // null on bad key
        if (user is null) return null;
        _key = apiKey;
        ConnectedUser = user.Value.Name;
        ConnectedPremium = user.Value.IsPremium;
        Save();
        return user.Value.Name;
    }

    /// <summary>Re-validate the stored key to refresh the account name + premium flag (e.g. after an
    /// upgrade, or to populate premium for a connection stored before it was tracked). Offline-safe:
    /// a network/transient failure leaves the cached values intact.</summary>
    public async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(_key)) return;
        try
        {
            var user = await ValidateKeyAsync(_key);
            if (user is null) return; // key now rejected — leave it to the connect flow to handle
            ConnectedUser = user.Value.Name;
            ConnectedPremium = user.Value.IsPremium;
            Save();
        }
        catch { /* offline / transient — keep the cached name + premium */ }
    }

    /// <summary>
    /// Verify a Nexus personal key and read the account identity: <c>GET /v1/users/validate.json</c>
    /// over the shared <see cref="HttpClient"/>, stamped with the Nexus-ToS identity headers
    /// (<c>apikey</c> + <c>Application-Name</c> + <c>Application-Version</c>). Returns the account
    /// name + premium flag, or null on a rejected key (401). The validate response is snake_case
    /// (<c>name</c> / <c>is_premium</c>); any other non-2xx throws (the caller decides whether that
    /// is fatal). Inline here so Core carries no Nexus client code — the read/write surface lives in
    /// the Nexus plugin; this is the one App-side credential check the key store needs.
    /// </summary>
    private async Task<(string? Name, bool IsPremium)?> ValidateKeyAsync(string key)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/users/validate.json");
        msg.Headers.TryAddWithoutValidation("Accept", "application/json");
        msg.Headers.TryAddWithoutValidation("Application-Name", ApplicationName);
        msg.Headers.TryAddWithoutValidation("Application-Version", AppVersionString);
        msg.Headers.TryAddWithoutValidation("apikey", key);

        using var res = await _http.SendAsync(msg);
        if (res.StatusCode == HttpStatusCode.Unauthorized) return null; // bad / expired key
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Nexus request failed ({(int)res.StatusCode})");

        await using var stream = await res.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        var premium = root.TryGetProperty("is_premium", out var p) && p.ValueKind == JsonValueKind.True;
        return (name, premium);
    }

    /// <summary>Clear the stored key — Nexus features go inert; everything else is unaffected.</summary>
    public void Disconnect()
    {
        _key = null;
        ConnectedUser = null;
        ConnectedPremium = false;
        try { if (File.Exists(StorePath)) File.Delete(StorePath); } catch { /* best effort */ }
    }

    // ApiKey = legacy plaintext (read-only fallback, migrated on first save); ApiKeyProtected = the
    // DPAPI-encrypted key (base64) we write going forward. UserName is not secret.
    private sealed class Stored
    {
        public string? ApiKey { get; set; }
        public string? ApiKeyProtected { get; set; }
        public string? UserName { get; set; }
        public bool Premium { get; set; }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            var s = JsonSerializer.Deserialize<Stored>(File.ReadAllText(StorePath), Json);
            if (s is null) return;
            ConnectedUser = s.UserName;
            ConnectedPremium = s.Premium;

            if (!string.IsNullOrEmpty(s.ApiKeyProtected))
            {
                _key = Unprotect(s.ApiKeyProtected);          // null if it can't be decrypted (other user/machine, corrupt)
                if (_key is null) ConnectedUser = null;       // can't use it -> not connected
            }
            else if (!string.IsNullOrEmpty(s.ApiKey))
            {
                _key = s.ApiKey;                              // legacy plaintext from before encryption
                Save();                                       // migrate: re-write encrypted, drop the plaintext
            }
        }
        catch { _key = null; ConnectedUser = null; /* unreadable -> not connected */ }
    }

    // Writes only the encrypted key (the plaintext ApiKey field is never written again).
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        AtomicJson.WriteJsonAtomic(StorePath, new Stored
        {
            ApiKeyProtected = string.IsNullOrEmpty(_key) ? null : Protect(_key),
            UserName = ConnectedUser,
            Premium = ConnectedPremium,
        });
    }

    // DPAPI, CurrentUser scope — ciphertext is bound to this Windows account.
    private static string Protect(string plain)
        => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    private static string? Unprotect(string protectedBase64)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), null, DataProtectionScope.CurrentUser)); }
        catch { return null; } // tampered / different user / corrupt -> treat as not connected
    }
}
