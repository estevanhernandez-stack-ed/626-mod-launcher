using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModManager.Core;

namespace ModManager.App.Services;

/// <summary>
/// Holds the user's Nexus connection: their own personal API key + the resolved <see cref="NexusClient"/>.
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

    public NexusService(HttpClient http)
    {
        _http = http;
        Load();
    }

    public bool IsConnected => !string.IsNullOrEmpty(_key);
    public string? ConnectedUser { get; private set; }

    /// <summary>A client bound to the stored key, or null when not connected.</summary>
    public INexusClient? Client => string.IsNullOrEmpty(_key) ? null : new NexusClient(_http, new NexusOptions { ApiKey = _key });

    /// <summary>Validate a pasted personal key against Nexus; on success store it (+ the account name)
    /// and connect. Returns the account name, or null if the key was rejected (401).</summary>
    public async Task<string?> ConnectAsync(string apiKey)
    {
        apiKey = (apiKey ?? "").Trim();
        if (apiKey.Length == 0) return null;
        var user = await new NexusClient(_http, new NexusOptions { ApiKey = apiKey }).ValidateAsync(); // null on bad key
        if (user is null) return null;
        _key = apiKey;
        ConnectedUser = user.Name;
        Save();
        return user.Name;
    }

    /// <summary>Clear the stored key — Nexus features go inert; everything else is unaffected.</summary>
    public void Disconnect()
    {
        _key = null;
        ConnectedUser = null;
        try { if (File.Exists(StorePath)) File.Delete(StorePath); } catch { /* best effort */ }
    }

    // ApiKey = legacy plaintext (read-only fallback, migrated on first save); ApiKeyProtected = the
    // DPAPI-encrypted key (base64) we write going forward. UserName is not secret.
    private sealed class Stored
    {
        public string? ApiKey { get; set; }
        public string? ApiKeyProtected { get; set; }
        public string? UserName { get; set; }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            var s = JsonSerializer.Deserialize<Stored>(File.ReadAllText(StorePath), Json);
            if (s is null) return;
            ConnectedUser = s.UserName;

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
