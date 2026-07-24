using System.IO;
using System.Net.Http;
using System.Text.Json;
using ModManager.Core;
using ModManager.Core.Manifest;
using ModManager.Core.Nexus;

namespace ModManager.App.Services;

/// <summary>
/// Signed startup delivery of the OAuth <c>client_id</c> — the SAME signed-remote rail as the
/// game-definition feed (<see cref="RemoteManifestSource"/>), NOT the connect-gated plugin feed.
/// Fetched from the <c>626-game-manifest</c> repo (raw <c>main</c>) and verified against the SAME
/// pinned key that verifies the games manifest (<see cref="ManifestSigningKey.PublicKeySpki"/>) — no
/// new key. The payload is public-by-design (a PKCE public client has no secret to protect); the
/// signature only guarantees provenance, not secrecy.
///
/// <see cref="LoadCachedEffective"/> is the synchronous startup path — instant, no network — mirrors
/// <see cref="RemoteManifestSource.ApplyCachedAtStartup"/>. <see cref="RefreshAsync"/> is the
/// background fetch-verify-cache; on a verified payload it also returns the freshly-effective config
/// so the CURRENT session (not just the next) can pick up a delivered client_id without a restart.
/// Every failure — offline, 404 (expected until the payload is published), bad signature, malformed
/// JSON — is swallowed and leaves the cache untouched; baked is always the floor.
/// </summary>
public sealed class NexusOAuthConfigSource(HttpClient http)
{
    // 626-game-manifest repo, raw main — same host/pattern as RemoteManifestSource.FeedUrl. Signed by
    // that repo's CI (MANIFEST_SIGNING_KEY) and verified below against the pinned ManifestSigningKey.
    // Do NOT point this at the plugin repo — the OAuth config is signed by the manifest CI, not it.
    private const string Url = "https://raw.githubusercontent.com/estevanhernandez-stack-ed/626-game-manifest/main/nexus-oauth.json";
    private const string SigUrl = Url + ".sig";

    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModManagerBuilder", "nexus-oauth-cache.json");

    /// <summary>
    /// Baked ⊕ cached, synchronously — no network. Call at/right after DI setup so a previously
    /// delivered client_id is already present the moment the user could click Connect. Any failure
    /// reading or parsing the cache falls back to baked alone. Never throws.
    /// </summary>
    public NexusOAuthConfig LoadCachedEffective()
    {
        try
        {
            var cached = File.Exists(CachePath)
                ? JsonSerializer.Deserialize<NexusOAuthConfig>(File.ReadAllText(CachePath), NexusOAuthConfig.JsonOpts)
                : null;
            return NexusOAuthConfig.Baked.Overlay(cached);
        }
        catch { return NexusOAuthConfig.Baked; }
    }

    /// <summary>
    /// Background fetch + verify + cache for the next launch. On a verified payload, also writes the
    /// cache atomically (camelCase) and returns baked ⊕ remote so the caller can hot-apply it to the
    /// live <c>NexusOAuthService.Config</c> without waiting for a restart. Returns null on ANY failure
    /// (offline, 404 until the payload is published, bad signature, malformed JSON) and leaves the
    /// cache untouched — the caller keeps whatever it already had. Never throws.
    /// </summary>
    public async Task<NexusOAuthConfig?> RefreshAsync()
    {
        try
        {
            var json = await http.GetByteArrayAsync(Url).ConfigureAwait(false);
            var sig = await http.GetByteArrayAsync(SigUrl).ConfigureAwait(false);
            if (!ManifestSignature.Verify(ManifestSigningKey.PublicKeySpki, json, sig))
                return null;

            var remote = JsonSerializer.Deserialize<NexusOAuthConfig>(json, NexusOAuthConfig.JsonOpts);
            if (remote is null) return null;

            AtomicJson.WriteJsonAtomic(CachePath, remote);
            return NexusOAuthConfig.Baked.Overlay(remote);
        }
        catch
        {
            // Offline / 404 (expected until the payload is published) / bad signature / malformed
            // JSON -> leave the cache untouched; baked (or whatever's already cached) stays the floor.
            return null;
        }
    }
}
