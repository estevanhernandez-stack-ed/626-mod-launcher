using System.IO;
using System.Security.Cryptography;
using System.Text;
using ModManager.Core;
using ModManager.Core.Nexus;

namespace ModManager.App.Services;

/// <summary>
/// Holds the user's Nexus OAuth connection — the token set + connection state. Tokens are stored per-user
/// at %APPDATA%\ModManagerBuilder\nexus.json, DPAPI-encrypted (<see cref="DataProtectionScope.CurrentUser"/>)
/// so the ciphertext is bound to this Windows account. No secret is ever baked into the binary (operating
/// law #2), and no token is handed to plugin code (<see cref="GetCredential"/> returns null under OAuth).
///
/// The pure on-disk shape + legacy-key migration live in <see cref="NexusTokenStore"/> (Core, unit-tested);
/// this App-side wrapper injects DPAPI protect/unprotect + file IO. A pre-OAuth api-key file found on load
/// is DISCARDED (keys are non-compliant) and its file deleted — the user reconnects via the OAuth flow
/// (wired in later tasks, which set <see cref="RefreshAsync"/> and call <see cref="SaveTokens"/>).
/// </summary>
public sealed class NexusService
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModManagerBuilder");
    private static readonly string StorePath = Path.Combine(Dir, "nexus.json");
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    private NexusTokenSet? _tokens;

    public NexusService() => Load();

    public NexusTokenSet? CurrentTokens => _tokens;
    public bool IsConnected => _tokens is not null;
    public string? ConnectedUser { get; private set; }

    /// <summary>Whether the connected account is Nexus Premium — surfaced in the account line's Premium/Free
    /// tag. Set from validate.json at connect + identity-refresh time (see <see cref="SaveTokens"/>). Not
    /// persisted in the token store: a cold start reads Free until the next identity refresh re-fetches it.</summary>
    public bool ConnectedPremium { get; private set; }

    /// <summary>True when load found and discarded a pre-OAuth api-key file — surfaced so the UI can nudge
    /// the user to reconnect via OAuth (the old key is gone; keys are non-compliant now).</summary>
    public bool LegacyKeyWasDiscarded { get; private set; }

    /// <summary>The OAuth service wires this in (later task): given a refresh token it returns a fresh token
    /// set, or null if the refresh was rejected. An injected delegate keeps Core + this store free of HTTP.</summary>
    public Func<string, Task<NexusTokenSet?>>? RefreshAsync { get; set; }

    /// <summary>The host-owned credential lookup a plugin receives via <c>IPluginHostServices.GetCredential</c>.
    /// Under OAuth the host no longer hands raw secrets to plugin code — this always returns null. Kept for
    /// ABI: existing call sites (PluginHost / the plugin feed) pass this method group and tolerate null.</summary>
#pragma warning disable CS0618
    public string? GetCredential(string key) => null;
#pragma warning restore CS0618

    /// <summary>Store a freshly-obtained token set (+ display name + premium flag) and persist it,
    /// DPAPI-encrypted. A null <paramref name="user"/> preserves the last-known display name (a transient
    /// identity-fetch miss shouldn't blank it); <paramref name="premium"/> is always applied.</summary>
    public void SaveTokens(NexusTokenSet tokens, string? user, bool premium = false)
    {
        _tokens = tokens;
        if (user is not null) ConnectedUser = user;
        ConnectedPremium = premium;
        LegacyKeyWasDiscarded = false;
        Save();
    }

    /// <summary>Returns a currently-valid access token, refreshing once if within the skew of expiry. Null
    /// when disconnected, when no refresh delegate is wired, or when the refresh is rejected — in which case
    /// the connection is dropped (the stale tokens are useless).</summary>
    public async Task<string?> ValidBearerAsync()
    {
        if (_tokens is null) return null;
        if (_tokens.NeedsRefresh(DateTimeOffset.UtcNow, RefreshSkew))
        {
            if (RefreshAsync is null) return null;
            var refreshed = await RefreshAsync(_tokens.RefreshToken).ConfigureAwait(false);
            if (refreshed is null) { Disconnect(); return null; }
            _tokens = refreshed;
            Save();
        }
        return _tokens.AccessToken;
    }

    /// <summary>Clear the stored tokens — Nexus features go inert; everything else is unaffected.</summary>
    public void Disconnect()
    {
        _tokens = null;
        ConnectedUser = null;
        ConnectedPremium = false;
        try { if (File.Exists(StorePath)) File.Delete(StorePath); } catch { /* best effort */ }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            var raw = File.ReadAllText(StorePath);
            var result = NexusTokenStore.Load(raw, Unprotect);

            if (result.LegacyKeyDiscarded)
            {
                LegacyKeyWasDiscarded = true;
                try { if (File.Exists(StorePath)) File.Delete(StorePath); } catch { /* best effort */ }
                return; // no tokens — the user must reconnect via OAuth
            }

            _tokens = result.Tokens;
            ConnectedUser = result.ConnectedUser;
        }
        catch { _tokens = null; ConnectedUser = null; /* unreadable -> not connected */ }
    }

    private void Save()
    {
        var json = NexusTokenStore.Serialize(_tokens, ConnectedUser, Protect);
        AtomicJson.WriteTextAtomic(StorePath, json); // creates the parent dir; atomic temp-then-rename
    }

    // DPAPI, CurrentUser scope — ciphertext is bound to this Windows account. base64 for JSON transport.
    private static string Protect(string plain)
        => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    private static string? Unprotect(string protectedBase64)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), null, DataProtectionScope.CurrentUser)); }
        catch { return null; } // tampered / different user / corrupt -> treat as not connected
    }
}
