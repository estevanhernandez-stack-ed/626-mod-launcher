# Nexus OAuth migration — design

**Date:** 2026-07-19
**Status:** Spec (approved in-conversation). Spans two repos (`626-mod-launcher` + `626-mod-plugins`). Driven by Nexus Mods' registration requirements. One coherent migration — the pieces ship together.

## The problem

Nexus Mods, in the registration thread, set hard requirements to register the app for OAuth:

1. **API keys removed completely** — "API keys cannot be used in public facing apps... experimentation and testing purposes only." OAuth2 becomes the only auth.
2. **Fix the credential-exposure architecture** — today the plugin calls `host.GetCredential("nexus")` and gets the raw key ([Contract.cs:16](../../src/ModManager.Plugins.Abstractions/Contract.cs), [NexusModSource.cs:467](../../../626-mod-plugins/src/ModManager.Plugin.Nexus/NexusModSource.cs)). Nexus calls this an exfiltration risk. The host must own all credentials; the plugin must never see a secret.
3. **Consent-gate the first plugin download** — today first connect silently force-installs the Nexus plugin ([PluginFeedSource.cs:191](../../src/ModManager.App/Services/PluginFeedSource.cs)). Nexus wants explicit manual accept.
4. **Provide:** app name, callback URL, scope-mapping capability list, and a build with the OAuth framework ready + keys removed — which they inspect *before* registering us and issuing the `client_id`.

The `client_id` gap is a chicken-and-egg: OAuth can't complete a real sign-in until Nexus registers us. **Decision (Este): cut over publicly now** — ship keys-removed + OAuth-framework in the public v0.11.0, and deliver the `client_id` remotely via the signed feed the moment Nexus registers us, so OAuth lights up with no second release.

## Approved decisions

- **Host-owned auth via a new optional interface** (`IAuthorizedSend`), following the `IModTextSearch` optional-capability precedent (Contract.cs:71-74) — zero ABI break for the shipped 0.10.0 plugin.
- **`GetCredential` deprecated, not removed** — kept for ABI (the 0.10.0 plugin calls it at load; removing = `MissingMethodException`), `[Obsolete]`, returns `null` under OAuth so a stale plugin fails inert.
- **Loopback PKCE** (RFC 8252 public client) — host runs the flow, owns tokens (DPAPI), auto-refreshes. The plugin never touches a token.
- **Keys removed completely** — DPAPI apikey store, the paste-key Settings UI, and `ConnectAsync(apiKey)` all deleted. Settings gets "Connect Nexus account."
- **`client_id` (+ scopes) feed-delivered** — public-by-design in PKCE, so signed-feed delivery is safe. OAuth endpoint URLs are config with baked defaults, confirmed against Nexus's OAuth guide at implementation time.
- **Cut over now** — v0.11.0 public removes keys immediately; user-scoped features show an honest "finalizing secure sign-in" state until the `client_id` lands; unauthenticated GraphQL search keeps working throughout.
- **Store SKU untouched** — no Nexus surface today; seal unchanged. (Store-Nexus is a separate future project — see *Future*.)

## Architecture

### 1. `IAuthorizedSend` — the host sends, the plugin never holds the secret

New interface in Abstractions **0.11.0**, added alongside the existing ones (never modifying `IModSource`/DTOs):

```csharp
public interface IAuthorizedSend
{
    // The plugin builds an UNAUTHENTICATED request; the host attaches the OAuth bearer
    // (or nothing if not connected) plus the mandatory Application-Name/Application-Version
    // headers, and sends on the shared HttpClient. The token never crosses into plugin code.
    Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpRequestMessage request, string credentialKey, CancellationToken ct = default);
}
```

- **Host side:** implemented on `HostServices` ([PluginHost.cs:102](../../src/ModManager.App/Services/PluginHost.cs)). It reads the token from `NexusService` (host-only), stamps `Authorization: Bearer <token>` + `Application-Name: 626-mod-launcher` + `Application-Version`, sends. On 401 it triggers a single silent refresh then retries once; on refresh failure it surfaces "reconnect needed" and returns the 401 to the plugin.
- **Plugin side:** `NexusModSource` takes the host reference (not a `Func<string?> getApiKey`); its `SendAsync` ([NexusModSource.cs:460](../../../626-mod-plugins/src/ModManager.Plugin.Nexus/NexusModSource.cs)) builds the `HttpRequestMessage` and, **if the host implements `IAuthorizedSend`**, calls `SendAuthorizedAsync("nexus", …)`. Fallback to the legacy `apikey`-stamp path only when the host lacks the interface (new-plugin/old-host compat). Under the new path the plugin never calls `GetCredential` and never stamps `apikey`.
- **`GetCredential`** stays on `IPluginHostServices`, `[Obsolete("Use IAuthorizedSend; the host owns credentials")]`, and the host returns `null` for it once OAuth is the auth mode.

### 2. OAuth PKCE in the host

New `NexusOAuthService` (App-side; Core stays pure — any primitive it needs is abstracted):

- **Flow:** generate PKCE `code_verifier` + `S256` `code_challenge` + `state`; start a one-shot `HttpListener` on `http://127.0.0.1:<port>/callback`; open the system browser to the authorize URL (`client_id`, `redirect_uri`, `scope`, `state`, `code_challenge`); receive the redirect, validate `state`, exchange `code` + `code_verifier` at the token endpoint; store tokens.
- **Port:** attempt a fixed registered port; if occupied, fall back to an ephemeral loopback port (RFC 8252 §7.3 permits any loopback port). The callback URL we *register* with Nexus is the fixed form; the fallback is only used if that port is busy and Nexus's registration allows loopback-any-port (confirm with them).
- **Token store:** replaces the apikey in `nexus.json` — DPAPI `CurrentUser`, holding `access_token` + `refresh_token` + `expires_at` + `scope`. `NexusService` owns load/save/refresh; refresh is lazy (on 401 or near-expiry).
- **Config:** `client_id`, authorize/token endpoint URLs, scopes — a small `NexusOAuthConfig` record read from a baked default overlaid by a signed payload. **Fetched at startup**, parallel to the existing remote games-manifest fetch in `Program.Main` (the established signed-startup-fetch pattern) — **not** via the plugin feed, which is connect-gated: the `client_id` must already be present when the user clicks "Connect," and connect *is* the OAuth flow, so the config channel must resolve before any connect action and independent of it. Fetching a tiny JSON config (no executable code) needs no consent gate — Nexus's consent ask is scoped to the plugin *download*. `client_id` empty until the payload delivers it.

### 3. Keys removed

- Delete: the DPAPI apikey read/write in `NexusService` ([NexusService.cs:51-67,115-159](../../src/ModManager.App/Services/NexusService.cs)), `ConnectAsync(string apiKey)`, `NexusKeyValidator`'s role as the connect path, the Settings "Nexus API key" textbox + `ConnectNexusAsync(apiKey)` ([MainViewModel.cs:1917](../../src/ModManager.App/ViewModels/MainViewModel.cs)).
- Identity now comes from the OAuth userinfo (or `/v1/users/validate.json` called host-side *with the bearer* — confirm which the OAuth guide blesses).
- **Existing key users:** on first launch of v0.11.0, any stored apikey in `nexus.json` is discarded (non-compliant to keep), replaced by a one-time notice: "Nexus switched to secure sign-in — reconnect your account." The connect prompt appears next time a Nexus feature is used.

### 4. Consent-gate the first plugin download

- The first-ever install (no plugin present) requires an explicit "Install the Nexus plugin?" accept before any download. The two automatic triggers — `MaybeFetchOnConnectAsync` ([SettingsDialog.xaml.cs:398](../../src/ModManager.App/SettingsDialog.xaml.cs)) and the startup `FetchAsync(force:false)` ([MainWindow.xaml.cs:127](../../src/ModManager.App/MainWindow.xaml.cs)) — gate behind it.
- Because first-connect is *also* the OAuth handshake, the consent dialog explains both: "Connect your Nexus account and install the Nexus plugin (a small signed add-on, downloaded from 626-mod-plugins)." One accept covers the pair.
- Already-installed plugin **updates** don't re-prompt (the user consented once). The manual refresh button is already explicit.

### 5. The dark window (cut-over-now, honestly)

- Between v0.11.0 shipping and the `client_id` landing: user-scoped features (endorse, md5 identify, metadata refresh, update checks) render a quiet "secure sign-in is being finalized with Nexus — hang tight" state rather than failing.
- The **unauthenticated GraphQL search** (loose-identify) keeps working the whole time.
- `client_id` arrives via signed feed refresh → "Connect Nexus account" works → features light up. No release.

## What we hand Nexus

- **App name:** 626 Mod Launcher (publisher: 626Labs LLC)
- **Callback URL:** `http://127.0.0.1:<port>/callback` (propose a fixed port; ask whether they permit loopback-any-port fallback per RFC 8252)
- **Scopes** — named as capabilities, mapped from the 8 real endpoints, for them to map to their vocabulary:
  - *identity* — who's signed in (`/v1/users/validate.json`)
  - *endorsements: read* — `/v1/user/endorsements.json`
  - *endorsements: write* — `POST …/{endorse|abstain}.json`
  - *mod metadata: read* — md5 identify + mod-by-id + updated lists (`/v1/games/{domain}/mods/…`)
  - (*search* — `/v2/graphql`, unauthenticated; no scope needed)
- **The build:** public v0.11.0 (keys removed + OAuth framework, `client_id` blank) *is* the review build we point them at.

## Repos, tasks, release

- **`626-mod-plugins`:** bump `PackageReference ModManager.Plugins.Abstractions` 0.10.0 → 0.11.0; `NexusModSource` ctor takes the host, `SendAsync` uses `IAuthorizedSend` with legacy fallback; `RequiresApiKey`/`getApiKey` semantics retired. Release `nexus-v0.11.0` after the launcher publishes 0.11.0.
- **`626-mod-launcher`:** Abstractions 0.11.0 (`IAuthorizedSend`, `[Obsolete]` on `GetCredential`); `NexusOAuthService` (PKCE loopback); `NexusService` token store + refresh; key-path removal; Settings "Connect Nexus account"; consent-gate dialog; `NexusOAuthConfig` baked+feed; the dark-window states. Release v0.11.0 (publishes Abstractions 0.11.0).
- **Order:** launcher v0.11.0 → plugin nexus-v0.11.0. Store SKU unaffected; STORE build + seal unchanged.

## Testing

- **Host/Core:** PKCE verifier/challenge (S256) correctness; `state` validation rejects mismatches; token store DPAPI round-trip (camelCase-on-disk); refresh-on-401 retries once then surfaces reconnect; `SendAuthorizedAsync` attaches bearer + preserves `Application-Name`/`Application-Version`; `GetCredential` returns null under OAuth.
- **Plugin:** `SendAsync` uses `IAuthorizedSend` when present and stamps **no** `apikey`; falls back cleanly when absent; every endpoint still routes through the one transport helper.
- **Consent gate:** first-download requires accept; already-installed update does not re-prompt.
- **App UI** (dialogs, Settings button, dark-window states): build + a `docs/smoke-tests/pending.md` entry (the loopback browser handshake only fully exercises on a real machine, and end-to-end needs the real `client_id`).
- Full Core suite + `CorePurity` green; STORE build + seal OK.

## Non-goals

- No key-based auth of any kind retained (Nexus-prohibited).
- No token or secret ever passed to plugin code (the whole point).
- No change to the games-manifest feed schema (the OAuth config is a separate small signed payload fetched at startup, not game data, and not carried on the connect-gated plugin feed).
- No Store Nexus surface in this migration (see *Future*).

## Future (seam kept open)

- **Nexus on Store** — its own spec. The blocker is *downloadable code* (Store §10.2.1), not auth: the Nexus source is delivered as a downloaded plugin DLL, which the Store forbids. The path is to **compile the Nexus source into the STORE flavor as first-party code** (it already depends only on `Abstractions` + BCL, so it's a delivery swap, not a rewrite) — and OAuth is exactly what makes that Store-viable (no embeddable secret). This migration must keep the Nexus source cleanly compile-able and must not couple it harder to the plugin loader. When pursued, the Store listing's what's-new + cert note discloses the Nexus network calls, same discipline as every prior submission.

## Success criteria

- The shipped 0.10.0 plugin still loads on the 0.11.0 host (ABI intact; `GetCredential` present though obsolete).
- Under OAuth, no code path outside `NexusService`/`NexusOAuthService` can read the token; the plugin provably never receives it.
- Keys are gone from disk and UI; a stored legacy key is discarded with a reconnect notice.
- First plugin download requires explicit consent; updates don't re-prompt.
- With a real `client_id` fed in, "Connect Nexus account" completes a PKCE sign-in and every user-scoped feature works — with no app update between key-removal and OAuth-live.
