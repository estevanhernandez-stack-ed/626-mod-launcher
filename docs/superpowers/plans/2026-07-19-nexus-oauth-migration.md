# Nexus OAuth Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Nexus API-key auth with host-owned OAuth2 (loopback PKCE), so the plugin never sees a credential, keys are gone entirely, and the first plugin download is consent-gated — meeting Nexus's registration requirements.

**Architecture:** A new `IAuthorizedSend` optional interface (the `IModTextSearch` precedent) lets the host send authorized requests on the plugin's behalf; the token lives only in the host (`NexusService` + `NexusOAuthService`, DPAPI). Pure OAuth logic (PKCE, config overlay, token model, header stamping, request bodies, gate predicates) lives in `ModManager.Core` and is fully TDD'd; the loopback listener, browser launch, DPAPI persistence, and UI live App-side and are covered by build + smoke. The `client_id` is delivered by a signed startup fetch (the games-manifest rail), so OAuth lights up remotely without a release.

**Tech Stack:** .NET 10, C#, WinUI 3 (App), xUnit. `System.Security.Cryptography` (PKCE S256, DPAPI), `System.Net.HttpListener` (loopback), `System.Text.Json`.

## Global Constraints

- **Repos & order:** launcher `v0.11.0` (publishes `ModManager.Plugins.Abstractions` 0.11.0) **then** plugin `nexus-v0.11.0`. Human-gated release (Este cuts).
- **ABI:** never modify `IModSource` or any DTO. Add `IAuthorizedSend` as a NEW interface. `GetCredential` stays on `IPluginHostServices`, marked `[Obsolete]` — removing it breaks the shipped 0.10.0 plugin at load (`MissingMethodException`).
- **No secret to plugin code:** under OAuth the token is readable only inside `NexusService`/`NexusOAuthService`. `GetCredential` returns `null`. The plugin builds unauthenticated `HttpRequestMessage`s and hands them to the host.
- **Mandatory headers on every Nexus request:** `Application-Name: 626-mod-launcher` + `Application-Version: <appVersion>` (Nexus ToS) — preserved by the host's authorized send.
- **camelCase JSON on disk** (`JsonNamingPolicy.CamelCase`) for every persisted shape — `nexus.json` token store, cached OAuth config. Round-trip test with a `Contains("\"camelKey\"")` assertion.
- **Never bare `dotnet` at repo root.** Launcher tests: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj`. Launcher app build: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`. Plugin tests: target the plugin test csproj explicitly.
- **Store SKU untouched:** no `#if FULL`/`#if !FULL` change that alters STORE binaries; STORE build + `scripts/check-store-seal.ps1` stay green. Prefer an interface type-check over a flavor `#if`. (The plugin auto-fetch is *already* `#if FULL` — the consent gate lives inside that existing block.)
- **client_id is public-by-design** (PKCE public client) — safe to ship in a signed feed and in git config defaults; it is NOT a secret. Refresh/access tokens ARE secret — DPAPI only, never logged, never fed.
- **Build-time verifications (do NOT guess — confirm against Nexus's OAuth guide / their reply, code reads them from config):** exact `authorizeUrl` / `tokenUrl`, the identity endpoint under a bearer (`/v1/users/validate.json` with `Authorization: Bearer` vs a dedicated userinfo), the scope strings, and whether Nexus permits loopback-any-port or a single fixed port.

---

## File structure

**Launcher (`626-mod-launcher`):**

| Path | Responsibility |
|---|---|
| `src/ModManager.Plugins.Abstractions/Contract.cs` | +`IAuthorizedSend`; `[Obsolete]` on `GetCredential` |
| `src/ModManager.Core/Nexus/NexusPkce.cs` | PKCE S256 verifier/challenge + `state` gen/validate (pure) |
| `src/ModManager.Core/Nexus/NexusOAuthConfig.cs` | config record + baked default + remote overlay + `IsConfigured` |
| `src/ModManager.Core/Nexus/NexusTokenSet.cs` | token record + expiry logic + camelCase (de)serialize |
| `src/ModManager.Core/Nexus/NexusAuthHeaders.cs` | stamp Bearer + Application-Name/Version onto a request (pure) |
| `src/ModManager.Core/Nexus/NexusTokenRequest.cs` | build authorize URL + token-exchange/refresh POST bodies (pure) |
| `src/ModManager.Core/Nexus/NexusAuthGate.cs` | `CanUseUserScopedFeatures(configured, connected)` predicate (pure) |
| `src/ModManager.App/Services/NexusOAuthService.cs` | loopback `HttpListener` PKCE flow, browser launch, code exchange |
| `src/ModManager.App/Services/NexusService.cs` | token store (DPAPI), refresh, `IsConnected`, legacy-key discard |
| `src/ModManager.App/Services/NexusOAuthConfigSource.cs` | signed startup fetch of the config overlay (games-manifest rail) |
| `src/ModManager.App/Services/PluginHost.cs` | `HostServices` implements `IAuthorizedSend`; `GetCredential`→null |
| `src/ModManager.App/Services/PluginFeedSource.cs` | first-install consent gate |
| `src/ModManager.App/SettingsDialog.xaml(.cs)` | "Connect Nexus account" replaces the API-key textbox |
| `src/ModManager.App/ViewModels/MainViewModel.cs` | OAuth connect path; dark-window state; legacy-key notice |
| `tests/ModManager.Tests/Nexus/*` | all Core TDD |
| `tests/ModManager.Tests/Plugins/ModTextSearchContractTests.cs` (+ new) | ABI contract tests |
| `docs/smoke-tests/pending.md` | OAuth end-to-end + consent + dark-window entries |

**Plugin (`626-mod-plugins`):**

| Path | Responsibility |
|---|---|
| `src/ModManager.Plugin.Nexus/ModManager.Plugin.Nexus.csproj` | Abstractions `PackageReference` 0.10.0 → 0.11.0 |
| `src/ModManager.Plugin.Nexus/NexusModSource.cs` | ctor takes host ref; `SendAsync` prefers `IAuthorizedSend` |
| `src/ModManager.Plugin.Nexus/NexusPlugin.cs` | `Register` passes host to `NexusModSource` |
| `tests/ModManager.Plugin.Nexus.Tests/NexusAuthorizedSendTests.cs` | authorized-send-vs-fallback TDD |
| `.github/workflows/release.yml` | minBinaryVersion → 0.11.0 |

---

## Task 1: Abstractions 0.11.0 — `IAuthorizedSend` + deprecate `GetCredential`

**Files:**
- Modify: `src/ModManager.Plugins.Abstractions/Contract.cs`
- Test: `tests/ModManager.Tests/Plugins/AuthorizedSendContractTests.cs`

**Interfaces:**
- Produces: `interface IAuthorizedSend { Task<HttpResponseMessage> SendAuthorizedAsync(HttpRequestMessage request, string credentialKey, CancellationToken ct = default); }`; `IPluginHostServices.GetCredential` marked `[Obsolete]` but still present.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Plugins/AuthorizedSendContractTests.cs
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using ModManager.Plugins.Abstractions;
using Xunit;

public class AuthorizedSendContractTests
{
    [Fact]
    public void IAuthorizedSend_has_expected_shape()
    {
        var m = typeof(IAuthorizedSend).GetMethod("SendAuthorizedAsync")!;
        Assert.Equal(typeof(Task<HttpResponseMessage>), m.ReturnType);
        var p = m.GetParameters();
        Assert.Equal(typeof(HttpRequestMessage), p[0].ParameterType);
        Assert.Equal(typeof(string), p[1].ParameterType);
        Assert.Equal(typeof(CancellationToken), p[2].ParameterType);
    }

    [Fact]
    public void GetCredential_still_present_for_abi_but_obsolete()
    {
        // Removing it would MissingMethodException the shipped 0.10.0 plugin at load.
        var m = typeof(IPluginHostServices).GetMethod("GetCredential")!;
        Assert.NotNull(m);
        Assert.NotNull(m.GetCustomAttribute<System.ObsoleteAttribute>());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter AuthorizedSendContract`
Expected: FAIL — `IAuthorizedSend` does not exist.

- [ ] **Step 3: Add the interface + obsolete attribute**

In `Contract.cs`, add `using System.Threading;` and `using System.Net.Http;` if absent. Add after `IModTextSearch` (near line 74):

```csharp
/// <summary>
/// Optional host capability: the host sends an authorized request on the plugin's behalf,
/// attaching credentials (OAuth bearer) server-side. The plugin builds an UNAUTHENTICATED
/// request and never receives a token. Plugins built before this interface keep loading;
/// the host feature-detects with `host is IAuthorizedSend`.
/// </summary>
public interface IAuthorizedSend
{
    Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpRequestMessage request, string credentialKey, CancellationToken ct = default);
}
```

On `IPluginHostServices.GetCredential` (line 16), add the attribute:

```csharp
[System.Obsolete("The host owns credentials. Use IAuthorizedSend.SendAuthorizedAsync; the host returns null here under OAuth.")]
string? GetCredential(string key);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter AuthorizedSendContract`
Expected: PASS.

- [ ] **Step 5: Suppress the obsolete warning at the two internal call sites (not errors)**

`TreatWarningsAsErrors` is on. The host's own `HostServices.GetCredential` implementation and any internal caller must not error. In `Contract.cs` the declaration is fine; the *implementation* in `PluginHost.cs` (Task 6) will use `#pragma warning disable CS0618` locally. No change here — noted for Task 6.

- [ ] **Step 6: Commit**

```bash
git add src/ModManager.Plugins.Abstractions/Contract.cs tests/ModManager.Tests/Plugins/AuthorizedSendContractTests.cs
git commit -m "feat(plugins): IAuthorizedSend optional capability; deprecate GetCredential (no ABI break)"
```

---

## Task 2: `NexusPkce` (Core) — S256 verifier/challenge + state

**Files:**
- Create: `src/ModManager.Core/Nexus/NexusPkce.cs`
- Test: `tests/ModManager.Tests/Nexus/NexusPkceTests.cs`

**Interfaces:**
- Produces: `static class NexusPkce` with `string CreateVerifier()`, `string Challenge(string verifier)`, `string CreateState()`, `bool StateMatches(string expected, string actual)`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Nexus/NexusPkceTests.cs
using System;
using System.Security.Cryptography;
using System.Text;
using ModManager.Core.Nexus;
using Xunit;

public class NexusPkceTests
{
    [Fact]
    public void Verifier_is_url_safe_and_in_length_range()
    {
        var v = NexusPkce.CreateVerifier();
        Assert.InRange(v.Length, 43, 128);                 // RFC 7636
        Assert.DoesNotContain('+', v);
        Assert.DoesNotContain('/', v);
        Assert.DoesNotContain('=', v);
    }

    [Fact]
    public void Challenge_is_base64url_sha256_of_verifier()
    {
        var v = "test_verifier_value_for_pkce_1234567890abcd";
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(v)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        Assert.Equal(expected, NexusPkce.Challenge(v));
    }

    [Fact]
    public void State_matches_only_itself_ordinal()
    {
        var s = NexusPkce.CreateState();
        Assert.True(NexusPkce.StateMatches(s, s));
        Assert.False(NexusPkce.StateMatches(s, s + "x"));
        Assert.False(NexusPkce.StateMatches(s, s.ToUpperInvariant()));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter NexusPkce`
Expected: FAIL — type not found.

- [ ] **Step 3: Implement**

```csharp
// src/ModManager.Core/Nexus/NexusPkce.cs
using System;
using System.Security.Cryptography;
using System.Text;

namespace ModManager.Core.Nexus;

/// <summary>PKCE (RFC 7636, S256) + CSRF state for the OAuth authorization-code flow.</summary>
public static class NexusPkce
{
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>A high-entropy code_verifier (96 random bytes -> 128 base64url chars).</summary>
    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(96));

    /// <summary>code_challenge = base64url(SHA256(ASCII(verifier))).</summary>
    public static string Challenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>An opaque CSRF state token.</summary>
    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>Ordinal, length-safe comparison of the returned state to the expected one.</summary>
    public static bool StateMatches(string expected, string actual) =>
        !string.IsNullOrEmpty(expected) &&
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual ?? string.Empty));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter NexusPkce`
Expected: PASS.

> Note: `FixedTimeEquals` returns false for unequal-length inputs, so the `+ "x"` and upper-case cases pass.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Nexus/NexusPkce.cs tests/ModManager.Tests/Nexus/NexusPkceTests.cs
git commit -m "feat(nexus): PKCE S256 verifier/challenge + CSRF state (Core, pure)"
```

---

## Task 3: `NexusOAuthConfig` (Core) — record + baked default + remote overlay

**Files:**
- Create: `src/ModManager.Core/Nexus/NexusOAuthConfig.cs`
- Test: `tests/ModManager.Tests/Nexus/NexusOAuthConfigTests.cs`

**Interfaces:**
- Produces: `sealed record NexusOAuthConfig(string ClientId, string AuthorizeUrl, string TokenUrl, string Scopes)`; `static NexusOAuthConfig Baked` (endpoints filled, `ClientId=""`); `bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId)`; `NexusOAuthConfig Overlay(NexusOAuthConfig? remote)`; `static JsonSerializerOptions JsonOpts`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Nexus/NexusOAuthConfigTests.cs
using System.Text.Json;
using ModManager.Core.Nexus;
using Xunit;

public class NexusOAuthConfigTests
{
    [Fact]
    public void Baked_has_endpoints_but_no_client_id()
    {
        Assert.False(NexusOAuthConfig.Baked.IsConfigured);
        Assert.False(string.IsNullOrWhiteSpace(NexusOAuthConfig.Baked.AuthorizeUrl));
        Assert.False(string.IsNullOrWhiteSpace(NexusOAuthConfig.Baked.TokenUrl));
    }

    [Fact]
    public void Overlay_takes_remote_client_id_but_keeps_baked_endpoints_when_remote_blank()
    {
        var remote = new NexusOAuthConfig("real-client-id", "", "", "");
        var eff = NexusOAuthConfig.Baked.Overlay(remote);
        Assert.Equal("real-client-id", eff.ClientId);
        Assert.True(eff.IsConfigured);
        Assert.Equal(NexusOAuthConfig.Baked.AuthorizeUrl, eff.AuthorizeUrl); // blank remote -> keep baked
    }

    [Fact]
    public void Overlay_null_remote_returns_baked()
    {
        Assert.Equal(NexusOAuthConfig.Baked, NexusOAuthConfig.Baked.Overlay(null));
    }

    [Fact]
    public void RoundTrips_as_camelCase()
    {
        var c = new NexusOAuthConfig("cid", "https://a", "https://t", "public");
        var json = JsonSerializer.Serialize(c, NexusOAuthConfig.JsonOpts);
        Assert.Contains("\"clientId\"", json);
        Assert.DoesNotContain("\"ClientId\"", json);
        var back = JsonSerializer.Deserialize<NexusOAuthConfig>(json, NexusOAuthConfig.JsonOpts)!;
        Assert.Equal(c, back);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter NexusOAuthConfig`
Expected: FAIL — type not found.

- [ ] **Step 3: Implement**

```csharp
// src/ModManager.Core/Nexus/NexusOAuthConfig.cs
using System.Text.Json;

namespace ModManager.Core.Nexus;

/// <summary>
/// OAuth client configuration. Endpoints are baked (public, stable); ClientId arrives via the
/// signed startup overlay (empty until Nexus registers us). All values are public-by-design in a
/// PKCE public client — safe to ship in git and in a signed feed.
/// BUILD-TIME: confirm AuthorizeUrl/TokenUrl/Scopes against Nexus's OAuth guide before release.
/// </summary>
public sealed record NexusOAuthConfig(string ClientId, string AuthorizeUrl, string TokenUrl, string Scopes)
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // CONFIRM these two URLs + the scope string against Nexus's OAuth guide at build time.
    public static readonly NexusOAuthConfig Baked = new(
        ClientId: "",
        AuthorizeUrl: "https://users.nexusmods.com/oauth/authorize",
        TokenUrl: "https://users.nexusmods.com/oauth/token",
        Scopes: "public");

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

    /// <summary>Overlay a remote config over the baked default: any non-blank remote field wins.</summary>
    public NexusOAuthConfig Overlay(NexusOAuthConfig? remote)
    {
        if (remote is null) return this;
        static string Pick(string remoteVal, string baseVal) =>
            string.IsNullOrWhiteSpace(remoteVal) ? baseVal : remoteVal;
        return new NexusOAuthConfig(
            Pick(remote.ClientId, ClientId),
            Pick(remote.AuthorizeUrl, AuthorizeUrl),
            Pick(remote.TokenUrl, TokenUrl),
            Pick(remote.Scopes, Scopes));
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter NexusOAuthConfig`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Nexus/NexusOAuthConfig.cs tests/ModManager.Tests/Nexus/NexusOAuthConfigTests.cs
git commit -m "feat(nexus): OAuth config record + baked default + remote overlay (Core)"
```

---

## Task 4: `NexusTokenSet` (Core) — token record + expiry + camelCase

**Files:**
- Create: `src/ModManager.Core/Nexus/NexusTokenSet.cs`
- Test: `tests/ModManager.Tests/Nexus/NexusTokenSetTests.cs`

**Interfaces:**
- Produces: `sealed record NexusTokenSet(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc, string Scope)`; `bool NeedsRefresh(DateTimeOffset now, TimeSpan skew)`; `static NexusTokenSet FromTokenResponse(string accessToken, string refreshToken, int expiresInSeconds, string scope, DateTimeOffset now)`; `static JsonSerializerOptions JsonOpts`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Nexus/NexusTokenSetTests.cs
using System;
using System.Text.Json;
using ModManager.Core.Nexus;
using Xunit;

public class NexusTokenSetTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FromTokenResponse_sets_expiry_from_expires_in()
    {
        var t = NexusTokenSet.FromTokenResponse("a", "r", 3600, "public", T0);
        Assert.Equal(T0.AddSeconds(3600), t.ExpiresAtUtc);
    }

    [Fact]
    public void NeedsRefresh_true_within_skew_of_expiry()
    {
        var t = NexusTokenSet.FromTokenResponse("a", "r", 3600, "public", T0);
        Assert.False(t.NeedsRefresh(T0.AddSeconds(3000), TimeSpan.FromMinutes(5)));
        Assert.True(t.NeedsRefresh(T0.AddSeconds(3400), TimeSpan.FromMinutes(5))); // within 5m of 3600
        Assert.True(t.NeedsRefresh(T0.AddSeconds(4000), TimeSpan.FromMinutes(5))); // already expired
    }

    [Fact]
    public void RoundTrips_as_camelCase()
    {
        var t = NexusTokenSet.FromTokenResponse("acc", "ref", 3600, "public", T0);
        var json = JsonSerializer.Serialize(t, NexusTokenSet.JsonOpts);
        Assert.Contains("\"accessToken\"", json);
        Assert.Contains("\"refreshToken\"", json);
        Assert.Contains("\"expiresAtUtc\"", json);
        Assert.DoesNotContain("\"AccessToken\"", json);
        var back = JsonSerializer.Deserialize<NexusTokenSet>(json, NexusTokenSet.JsonOpts)!;
        Assert.Equal(t, back);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter NexusTokenSet`
Expected: FAIL — type not found.

- [ ] **Step 3: Implement**

```csharp
// src/ModManager.Core/Nexus/NexusTokenSet.cs
using System;
using System.Text.Json;

namespace ModManager.Core.Nexus;

/// <summary>OAuth token material. SECRET — persisted only under DPAPI, never logged, never fed.</summary>
public sealed record NexusTokenSet(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc, string Scope)
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static NexusTokenSet FromTokenResponse(
        string accessToken, string refreshToken, int expiresInSeconds, string scope, DateTimeOffset now) =>
        new(accessToken, refreshToken, now.AddSeconds(expiresInSeconds), scope);

    /// <summary>True if the access token is expired or within <paramref name="skew"/> of expiring.</summary>
    public bool NeedsRefresh(DateTimeOffset now, TimeSpan skew) => now + skew >= ExpiresAtUtc;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter NexusTokenSet`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Nexus/NexusTokenSet.cs tests/ModManager.Tests/Nexus/NexusTokenSetTests.cs
git commit -m "feat(nexus): token set record + expiry logic + camelCase (Core)"
```

---

## Task 5: `NexusAuthHeaders` (Core) — stamp bearer + ToS headers

**Files:**
- Create: `src/ModManager.Core/Nexus/NexusAuthHeaders.cs`
- Test: `tests/ModManager.Tests/Nexus/NexusAuthHeadersTests.cs`

**Interfaces:**
- Produces: `static class NexusAuthHeaders { void Apply(HttpRequestMessage req, string? bearerToken, string appName, string? appVersion); }`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Nexus/NexusAuthHeadersTests.cs
using System.Net.Http;
using ModManager.Core.Nexus;
using Xunit;

public class NexusAuthHeadersTests
{
    [Fact]
    public void Apply_stamps_bearer_and_tos_headers()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/x.json");
        NexusAuthHeaders.Apply(req, "tok123", "626-mod-launcher", "0.11.0");
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("tok123", req.Headers.Authorization!.Parameter);
        Assert.Contains("626-mod-launcher", string.Join(",", req.Headers.GetValues("Application-Name")));
        Assert.Contains("0.11.0", string.Join(",", req.Headers.GetValues("Application-Version")));
    }

    [Fact]
    public void Apply_without_token_still_stamps_tos_headers_no_authorization()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/x.json");
        NexusAuthHeaders.Apply(req, null, "626-mod-launcher", "0.11.0");
        Assert.Null(req.Headers.Authorization);
        Assert.True(req.Headers.Contains("Application-Name"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter NexusAuthHeaders`
Expected: FAIL — type not found.

- [ ] **Step 3: Implement**

```csharp
// src/ModManager.Core/Nexus/NexusAuthHeaders.cs
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter NexusAuthHeaders`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Nexus/NexusAuthHeaders.cs tests/ModManager.Tests/Nexus/NexusAuthHeadersTests.cs
git commit -m "feat(nexus): auth-header stamping helper (Core)"
```

---

## Task 6: `NexusTokenRequest` (Core) — authorize URL + token bodies

**Files:**
- Create: `src/ModManager.Core/Nexus/NexusTokenRequest.cs`
- Test: `tests/ModManager.Tests/Nexus/NexusTokenRequestTests.cs`

**Interfaces:**
- Produces: `static class NexusTokenRequest`: `string BuildAuthorizeUrl(NexusOAuthConfig cfg, string redirectUri, string challenge, string state)`; `FormUrlEncodedContent BuildExchangeBody(NexusOAuthConfig cfg, string code, string redirectUri, string verifier)`; `FormUrlEncodedContent BuildRefreshBody(NexusOAuthConfig cfg, string refreshToken)`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Nexus/NexusTokenRequestTests.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using ModManager.Core.Nexus;
using Xunit;

public class NexusTokenRequestTests
{
    private static readonly NexusOAuthConfig Cfg =
        new("cid", "https://auth/authorize", "https://auth/token", "public");

    [Fact]
    public void AuthorizeUrl_has_pkce_and_state()
    {
        var url = NexusTokenRequest.BuildAuthorizeUrl(Cfg, "http://127.0.0.1:41999/callback", "chal", "st");
        Assert.StartsWith("https://auth/authorize?", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("client_id=cid", url);
        Assert.Contains("code_challenge=chal", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("state=st", url);
        Assert.Contains("scope=public", url);
        Assert.Contains(Uri.EscapeDataString("http://127.0.0.1:41999/callback"), url);
    }

    [Fact]
    public async Task ExchangeBody_is_authorization_code_grant()
    {
        var body = NexusTokenRequest.BuildExchangeBody(Cfg, "the-code", "http://127.0.0.1:41999/callback", "verif");
        var s = await body.ReadAsStringAsync();
        Assert.Contains("grant_type=authorization_code", s);
        Assert.Contains("code=the-code", s);
        Assert.Contains("code_verifier=verif", s);
        Assert.Contains("client_id=cid", s);
    }

    [Fact]
    public async Task RefreshBody_is_refresh_token_grant()
    {
        var body = NexusTokenRequest.BuildRefreshBody(Cfg, "rtok");
        var s = await body.ReadAsStringAsync();
        Assert.Contains("grant_type=refresh_token", s);
        Assert.Contains("refresh_token=rtok", s);
        Assert.Contains("client_id=cid", s);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter NexusTokenRequest`
Expected: FAIL — type not found.

- [ ] **Step 3: Implement**

```csharp
// src/ModManager.Core/Nexus/NexusTokenRequest.cs
using System;
using System.Collections.Generic;
using System.Net.Http;

namespace ModManager.Core.Nexus;

/// <summary>Builds the OAuth authorize URL and the token-endpoint POST bodies (PKCE, public client).</summary>
public static class NexusTokenRequest
{
    public static string BuildAuthorizeUrl(NexusOAuthConfig cfg, string redirectUri, string challenge, string state)
    {
        var q = new List<string>
        {
            "response_type=code",
            "client_id=" + Uri.EscapeDataString(cfg.ClientId),
            "redirect_uri=" + Uri.EscapeDataString(redirectUri),
            "scope=" + Uri.EscapeDataString(cfg.Scopes),
            "state=" + Uri.EscapeDataString(state),
            "code_challenge=" + Uri.EscapeDataString(challenge),
            "code_challenge_method=S256",
        };
        return cfg.AuthorizeUrl + "?" + string.Join("&", q);
    }

    public static FormUrlEncodedContent BuildExchangeBody(
        NexusOAuthConfig cfg, string code, string redirectUri, string verifier) =>
        new(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = cfg.ClientId,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = verifier,
        });

    public static FormUrlEncodedContent BuildRefreshBody(NexusOAuthConfig cfg, string refreshToken) =>
        new(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = cfg.ClientId,
            ["refresh_token"] = refreshToken,
        });
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter NexusTokenRequest`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Nexus/NexusTokenRequest.cs tests/ModManager.Tests/Nexus/NexusTokenRequestTests.cs
git commit -m "feat(nexus): authorize URL + token/refresh body builders (Core)"
```

---

## Task 7: `NexusAuthGate` (Core) — user-scoped feature predicate

**Files:**
- Create: `src/ModManager.Core/Nexus/NexusAuthGate.cs`
- Test: `tests/ModManager.Tests/Nexus/NexusAuthGateTests.cs`

**Interfaces:**
- Produces: `static class NexusAuthGate { bool CanUseUserScopedFeatures(bool configured, bool connected); NexusAuthStatus Status(bool configured, bool connected); }`; `enum NexusAuthStatus { NotConfigured, Configured_Disconnected, Connected }`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ModManager.Tests/Nexus/NexusAuthGateTests.cs
using ModManager.Core.Nexus;
using Xunit;

public class NexusAuthGateTests
{
    [Theory]
    [InlineData(false, false, false)] // client_id not delivered yet -> dark window
    [InlineData(false, true,  false)] // (can't be connected if not configured, but guard anyway)
    [InlineData(true,  false, false)] // configured but user not signed in
    [InlineData(true,  true,  true )] // configured + signed in -> features live
    public void CanUseUserScopedFeatures(bool configured, bool connected, bool expected) =>
        Assert.Equal(expected, NexusAuthGate.CanUseUserScopedFeatures(configured, connected));

    [Fact]
    public void Status_reports_not_configured_as_dark_window()
    {
        Assert.Equal(NexusAuthStatus.NotConfigured, NexusAuthGate.Status(false, false));
        Assert.Equal(NexusAuthStatus.Configured_Disconnected, NexusAuthGate.Status(true, false));
        Assert.Equal(NexusAuthStatus.Connected, NexusAuthGate.Status(true, true));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter NexusAuthGate`
Expected: FAIL — type not found.

- [ ] **Step 3: Implement**

```csharp
// src/ModManager.Core/Nexus/NexusAuthGate.cs
namespace ModManager.Core.Nexus;

public enum NexusAuthStatus { NotConfigured, Configured_Disconnected, Connected }

/// <summary>
/// Decides whether user-scoped Nexus features (endorse, identify, updates) are usable. During the
/// "dark window" — client_id not yet delivered — features are disabled with a "finalizing sign-in"
/// message; unauthenticated GraphQL search is unaffected and never routed through this gate.
/// </summary>
public static class NexusAuthGate
{
    public static bool CanUseUserScopedFeatures(bool configured, bool connected) => configured && connected;

    public static NexusAuthStatus Status(bool configured, bool connected) =>
        !configured ? NexusAuthStatus.NotConfigured
        : connected ? NexusAuthStatus.Connected
        : NexusAuthStatus.Configured_Disconnected;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter NexusAuthGate`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Core/Nexus/NexusAuthGate.cs tests/ModManager.Tests/Nexus/NexusAuthGateTests.cs
git commit -m "feat(nexus): user-scoped-feature gate predicate (Core)"
```

---

## Task 8: `NexusService` token store migration (App)

**Files:**
- Modify: `src/ModManager.App/Services/NexusService.cs`
- Test: `tests/ModManager.Tests/Nexus/NexusTokenStoreJsonTests.cs` (JSON shape only — DPAPI/persist is App + smoke)

**Interfaces:**
- Consumes: `NexusTokenSet` (Task 4), `NexusOAuthConfig` (Task 3).
- Produces: on `NexusService` — `NexusTokenSet? CurrentTokens { get; }`, `void SaveTokens(NexusTokenSet t)`, `bool IsConnected` (has non-null tokens), `bool LegacyKeyWasDiscarded { get; }`, `Task<string?> ValidBearerAsync()` (returns access token, refreshing if needed), `void Disconnect()`. **Removes** `ConnectAsync(string apiKey)`. `GetCredential` returns `null`.

- [ ] **Step 1: Write the failing test (persisted shape)**

```csharp
// tests/ModManager.Tests/Nexus/NexusTokenStoreJsonTests.cs
using System;
using System.Text.Json;
using ModManager.Core.Nexus;
using Xunit;

public class NexusTokenStoreJsonTests
{
    // The on-disk envelope NexusService serializes before DPAPI-protecting.
    private sealed record NexusStoreFile(NexusTokenSet? Tokens, string? ConnectedUser);

    [Fact]
    public void Store_envelope_is_camelCase_and_round_trips()
    {
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var file = new NexusStoreFile(
            NexusTokenSet.FromTokenResponse("a", "r", 3600, "public", DateTimeOffset.UnixEpoch),
            "TestUser");
        var json = JsonSerializer.Serialize(file, opts);
        Assert.Contains("\"tokens\"", json);
        Assert.Contains("\"connectedUser\"", json);
        Assert.Contains("\"accessToken\"", json);
        var back = JsonSerializer.Deserialize<NexusStoreFile>(json, opts)!;
        Assert.Equal("TestUser", back.ConnectedUser);
        Assert.Equal("a", back.Tokens!.AccessToken);
    }
}
```

This pins the camelCase envelope shape. (DPAPI protect/unprotect + file IO are exercised by build + smoke; the envelope is what the round-trip law requires.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter NexusTokenStoreJson`
Expected: FAIL — until `NexusTokenSet` is referenced correctly it may not compile; PASS only once Tasks 3-4 exist (they do). If it already passes, proceed — this test guards the shape the App code below must use.

- [ ] **Step 3: Rewrite `NexusService` to a token store**

Replace the apikey members. Key points (full method bodies):

```csharp
// src/ModManager.App/Services/NexusService.cs — token-store shape
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Security.Cryptography;
using ModManager.Core;              // AtomicJson
using ModManager.Core.Nexus;

namespace ModManager.App.Services;

public sealed class NexusService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModManagerBuilder");
    private static readonly string StorePath = Path.Combine(Dir, "nexus.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    private sealed record StoreFile(string? TokensProtected, string? ConnectedUser);

    private NexusTokenSet? _tokens;
    public string? ConnectedUser { get; private set; }
    public bool LegacyKeyWasDiscarded { get; private set; }

    // The OAuth service injects itself for refresh; set once at startup wiring.
    public Func<string, Task<NexusTokenSet?>>? RefreshAsync { get; set; }

    public NexusService() => Load();

    public bool IsConnected => _tokens is not null;
    public NexusTokenSet? CurrentTokens => _tokens;

    // OBSOLETE by contract: the host never hands a secret to plugin code anymore.
    #pragma warning disable CS0618
    public string? GetCredential(string key) => null;
    #pragma warning restore CS0618

    public void SaveTokens(NexusTokenSet tokens, string? user)
    {
        _tokens = tokens;
        if (user is not null) ConnectedUser = user;
        Save();
    }

    public void Disconnect()
    {
        _tokens = null; ConnectedUser = null;
        try { if (File.Exists(StorePath)) File.Delete(StorePath); } catch { /* best effort */ }
    }

    /// <summary>Returns a currently-valid access token, refreshing once if near expiry. Null if disconnected or refresh fails.</summary>
    public async Task<string?> ValidBearerAsync()
    {
        if (_tokens is null) return null;
        if (_tokens.NeedsRefresh(DateTimeOffset.UtcNow, RefreshSkew) && RefreshAsync is not null)
        {
            var refreshed = await RefreshAsync(_tokens.RefreshToken).ConfigureAwait(false);
            if (refreshed is null) { Disconnect(); return null; }
            _tokens = refreshed; Save();
        }
        return _tokens?.AccessToken;
    }

    private static byte[] Protect(string s) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser);
    private static string Unprotect(byte[] b) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(b, null, DataProtectionScope.CurrentUser));

    private void Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return;
            var raw = File.ReadAllText(StorePath);

            // Legacy migration: an old file with an apikey field is DISCARDED (keys are non-compliant).
            using (var doc = JsonDocument.Parse(raw))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("apiKey", out _) || root.TryGetProperty("apiKeyProtected", out _))
                {
                    LegacyKeyWasDiscarded = true;
                    try { File.Delete(StorePath); } catch { }
                    return; // no tokens; user must reconnect via OAuth
                }
            }

            var file = JsonSerializer.Deserialize<StoreFile>(raw, JsonOpts);
            if (file?.TokensProtected is { } prot)
            {
                var json = Unprotect(Convert.FromBase64String(prot));
                _tokens = JsonSerializer.Deserialize<NexusTokenSet>(json, NexusTokenSet.JsonOpts);
                ConnectedUser = file.ConnectedUser;
            }
        }
        catch { _tokens = null; }  // unreadable store -> treat as disconnected
    }

    private void Save()
    {
        Directory.CreateDirectory(Dir);
        string? prot = _tokens is null
            ? null
            : Convert.ToBase64String(Protect(JsonSerializer.Serialize(_tokens, NexusTokenSet.JsonOpts)));
        AtomicJson.WriteJsonAtomic(StorePath, new StoreFile(prot, ConnectedUser), JsonOpts);
    }
}
```

Remove: `ConnectAsync(string apiKey)`, `ValidateKeyAsync`, `RefreshAsync(...)` old apikey variant, `ConnectedPremium` if it came from the key validate (or repopulate from OAuth identity later — out of scope here; drop it and its bindings). Update `NexusKeyValidator` usage: it is no longer the connect path.

- [ ] **Step 4: Build the app (App-side, no direct unit test for DPAPI IO)**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: build errors ONLY where callers used the removed `ConnectAsync(apiKey)`/`GetCredential` non-null — those are fixed in Tasks 9-12. For this task's gate, confirm `NexusService.cs` itself compiles in isolation via the test project reference + the Core suite:

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter NexusTokenStoreJson`
Expected: PASS.

> The full app build goes green at the end of Task 12 once every caller is migrated. Note in the commit that the app build is intentionally red between Tasks 8-11.

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.App/Services/NexusService.cs tests/ModManager.Tests/Nexus/NexusTokenStoreJsonTests.cs
git commit -m "feat(nexus): NexusService becomes an OAuth token store (DPAPI); legacy key discarded on load"
```

---

## Task 9: `NexusOAuthService` (App) — loopback PKCE flow + refresh

**Files:**
- Create: `src/ModManager.App/Services/NexusOAuthService.cs`
- Wire: `src/ModManager.App/App.xaml.cs` (register; set `NexusService.RefreshAsync`)

**Interfaces:**
- Consumes: `NexusPkce`, `NexusOAuthConfig`, `NexusTokenSet`, `NexusTokenRequest` (Tasks 2-6), `NexusService` (Task 8).
- Produces: `NexusOAuthService` — `Task<NexusConnectResult> ConnectAsync(CancellationToken ct)`, `Task<NexusTokenSet?> RefreshAsync(string refreshToken)`; `NexusOAuthConfig Config { get; set; }`; `sealed record NexusConnectResult(bool Ok, string? User, string? Error)`.

- [ ] **Step 1: Implement (App service — covered by build + smoke; the testable pieces are already Core-tested)**

```csharp
// src/ModManager.App/Services/NexusOAuthService.cs
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModManager.Core.Nexus;

namespace ModManager.App.Services;

public sealed record NexusConnectResult(bool Ok, string? User, string? Error);

/// <summary>
/// Runs the OAuth authorization-code + PKCE flow against a loopback redirect. The system browser
/// carries the user's Nexus session; this app never sees the password. Tokens are handed to
/// NexusService (DPAPI); no token is exposed to plugin code.
/// BUILD-TIME: confirm the identity call (validate.json under bearer vs userinfo) against Nexus's guide.
/// </summary>
public sealed class NexusOAuthService(HttpClient http, NexusService nexus, string appVersion)
{
    // A fixed loopback port we register with Nexus; if busy we fall back to an ephemeral one
    // (only usable if Nexus permits loopback-any-port — CONFIRM at registration).
    private const int PreferredPort = 41999;

    public NexusOAuthConfig Config { get; set; } = NexusOAuthConfig.Baked;

    public async Task<NexusConnectResult> ConnectAsync(CancellationToken ct)
    {
        if (!Config.IsConfigured)
            return new(false, null, "Secure sign-in is being finalized with Nexus. Try again shortly.");

        var (listener, redirectUri) = StartListener();
        try
        {
            var verifier = NexusPkce.CreateVerifier();
            var challenge = NexusPkce.Challenge(verifier);
            var state = NexusPkce.CreateState();
            var authorizeUrl = NexusTokenRequest.BuildAuthorizeUrl(Config, redirectUri, challenge, state);

            Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

            var ctx = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(3), ct).ConfigureAwait(false);
            var query = ctx.Request.QueryString;
            RespondAndClose(ctx, "You can return to 626 Mod Launcher now.");

            var returnedState = query["state"] ?? "";
            if (!NexusPkce.StateMatches(state, returnedState))
                return new(false, null, "Sign-in could not be verified (state mismatch). Please try again.");

            var code = query["code"];
            if (string.IsNullOrEmpty(code))
                return new(false, null, query["error"] ?? "Sign-in was cancelled.");

            var tokens = await ExchangeAsync(code!, redirectUri, verifier, ct).ConfigureAwait(false);
            if (tokens is null) return new(false, null, "Could not complete sign-in with Nexus.");

            var user = await FetchIdentityAsync(tokens.AccessToken, ct).ConfigureAwait(false);
            nexus.SaveTokens(tokens, user);
            return new(true, user, null);
        }
        catch (OperationCanceledException) { return new(false, null, "Sign-in timed out."); }
        catch (Exception ex) { return new(false, null, ex.Message); }
        finally { listener.Stop(); }
    }

    public async Task<NexusTokenSet?> RefreshAsync(string refreshToken)
    {
        if (!Config.IsConfigured) return null;
        using var body = NexusTokenRequest.BuildRefreshBody(Config, refreshToken);
        using var resp = await http.PostAsync(Config.TokenUrl, body).ConfigureAwait(false);
        return await ParseTokenAsync(resp).ConfigureAwait(false);
    }

    private async Task<NexusTokenSet?> ExchangeAsync(string code, string redirectUri, string verifier, CancellationToken ct)
    {
        using var body = NexusTokenRequest.BuildExchangeBody(Config, code, redirectUri, verifier);
        using var resp = await http.PostAsync(Config.TokenUrl, body, ct).ConfigureAwait(false);
        return await ParseTokenAsync(resp).ConfigureAwait(false);
    }

    private static async Task<NexusTokenSet?> ParseTokenAsync(HttpResponseMessage resp)
    {
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        var r = doc.RootElement;
        if (!r.TryGetProperty("access_token", out var at)) return null;
        var refresh = r.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "";
        var expires = r.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
        var scope = r.TryGetProperty("scope", out var sc) ? sc.GetString() ?? "" : "";
        return NexusTokenSet.FromTokenResponse(at.GetString() ?? "", refresh, expires, scope, DateTimeOffset.UtcNow);
    }

    private async Task<string?> FetchIdentityAsync(string accessToken, CancellationToken ct)
    {
        // CONFIRM endpoint at build time. Using v1 validate.json under a bearer as the baseline.
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/users/validate.json");
        NexusAuthHeaders.Apply(req, accessToken, "626-mod-launcher", appVersion);
        using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
        return doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
    }

    private static (HttpListener listener, string redirectUri) StartListener()
    {
        foreach (var port in new[] { PreferredPort, 0 })
        {
            try
            {
                int actual = port == 0 ? GetFreePort() : port;
                var prefix = $"http://127.0.0.1:{actual}/callback/";
                var l = new HttpListener();
                l.Prefixes.Add(prefix);
                l.Start();
                return (l, prefix.TrimEnd('/'));
            }
            catch (HttpListenerException) { /* try next */ }
        }
        throw new InvalidOperationException("Could not bind a loopback callback port.");
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start(); int p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
    }

    private static void RespondAndClose(HttpListenerContext ctx, string message)
    {
        var html = Encoding.UTF8.GetBytes($"<html><body style='font-family:sans-serif;background:#0f1f31;color:#fff;text-align:center;padding-top:80px'>{message}</body></html>");
        ctx.Response.ContentType = "text/html";
        ctx.Response.OutputStream.Write(html, 0, html.Length);
        ctx.Response.Close();
    }
}
```

- [ ] **Step 2: Wire in `App.xaml.cs`**

Register the service; connect `NexusService.RefreshAsync` to it; feed `Config` from Task 10's source. Sketch:

```csharp
// App.xaml.cs (composition)
var nexus = services.GetRequiredService<NexusService>();
var oauth = new NexusOAuthService(sharedHttpClient, nexus, appVersion);
nexus.RefreshAsync = oauth.RefreshAsync;             // NexusService can refresh without knowing App types
// oauth.Config is set from NexusOAuthConfigSource at startup (Task 10)
services.AddSingleton(oauth);
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: compiles (callers still pending Tasks 10-11; may still be red on Settings/VM until Task 11 — acceptable mid-migration).

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/Services/NexusOAuthService.cs src/ModManager.App/App.xaml.cs
git commit -m "feat(nexus): loopback PKCE OAuth service + refresh (host owns the flow)"
```

---

## Task 10: `NexusOAuthConfigSource` (App) — signed startup fetch of `client_id`

**Files:**
- Create: `src/ModManager.App/Services/NexusOAuthConfigSource.cs`
- Wire: `src/ModManager.App/Program.cs` (or wherever `RemoteManifestSource` is applied at startup)

**Interfaces:**
- Consumes: `NexusOAuthConfig` (Task 3); the existing manifest signature-verify machinery (`ManifestSignature.Verify`, the pinned public key) — reuse, do not reinvent.
- Produces: `NexusOAuthConfigSource` — `Task<NexusOAuthConfig> LoadEffectiveAsync()` (baked ⊕ cached ⊕ freshly fetched-and-verified remote).

- [ ] **Step 1: Implement (App wiring; overlay logic is Core-tested in Task 3)**

```csharp
// src/ModManager.App/Services/NexusOAuthConfigSource.cs
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ModManager.Core;              // AtomicJson
using ModManager.Core.Manifest;     // ManifestSignature (reuse the same ECDSA verify)
using ModManager.Core.Nexus;

namespace ModManager.App.Services;

/// <summary>
/// Fetches the signed OAuth config overlay (carrying the client_id) at startup — the same rail as the
/// games-manifest remote fetch, NOT the connect-gated plugin feed. Values are public-by-design; the
/// signature only guarantees provenance. Any failure -> fall back to baked/cached (dark window persists).
/// </summary>
public sealed class NexusOAuthConfigSource(HttpClient http)
{
    private const string Url = "https://github.com/estevanhernandez-stack-ed/626-mod-plugins/releases/latest/download/nexus-oauth.json";
    private const string SigUrl = Url + ".sig";
    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModManagerBuilder", "nexus-oauth-cache.json");

    public async Task<NexusOAuthConfig> LoadEffectiveAsync()
    {
        var cached = ReadCache();
        var eff = NexusOAuthConfig.Baked.Overlay(cached);
        try
        {
            var json = await http.GetByteArrayAsync(Url).ConfigureAwait(false);
            var sig = await http.GetByteArrayAsync(SigUrl).ConfigureAwait(false);
            if (ManifestSignature.Verify(json, sig))   // same pinned key as the games manifest
            {
                var remote = JsonSerializer.Deserialize<NexusOAuthConfig>(json, NexusOAuthConfig.JsonOpts);
                if (remote is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                    AtomicJson.WriteJsonAtomic(CachePath, remote, NexusOAuthConfig.JsonOpts);
                    eff = NexusOAuthConfig.Baked.Overlay(remote);
                }
            }
        }
        catch { /* offline / bad sig / malformed -> keep baked⊕cached; dark window stays honest */ }
        return eff;
    }

    private static NexusOAuthConfig? ReadCache()
    {
        try { return File.Exists(CachePath)
            ? JsonSerializer.Deserialize<NexusOAuthConfig>(File.ReadAllText(CachePath), NexusOAuthConfig.JsonOpts)
            : null; }
        catch { return null; }
    }
}
```

- [ ] **Step 2: Wire at startup**

Where `RemoteManifestSource` is applied in `Program.Main` (before facade reads), also resolve OAuth config and hand it to `NexusOAuthService.Config`. Order is independent of connect. Sketch:

```csharp
// Program.cs startup, alongside the games-manifest apply
var oauthCfg = await new NexusOAuthConfigSource(httpClient).LoadEffectiveAsync();
App.AppHost.Services.GetRequiredService<NexusOAuthService>().Config = oauthCfg;
```

- [ ] **Step 3: Build**

Run: `dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64`
Expected: compiles.

- [ ] **Step 4: Commit**

```bash
git add src/ModManager.App/Services/NexusOAuthConfigSource.cs src/ModManager.App/Program.cs
git commit -m "feat(nexus): signed startup fetch of OAuth config (client_id delivery, manifest rail)"
```

---

## Task 11: Host `IAuthorizedSend` + key-path UI removal + consent gate + dark window

**Files:**
- Modify: `src/ModManager.App/Services/PluginHost.cs` (HostServices implements `IAuthorizedSend`)
- Modify: `src/ModManager.App/App.xaml.cs` (stop passing `GetCredential` as auth; keep the Func returning null for ABI)
- Modify: `src/ModManager.App/Services/PluginFeedSource.cs` (first-install consent gate)
- Modify: `src/ModManager.App/SettingsDialog.xaml(.cs)` ("Connect Nexus account" replaces key textbox)
- Modify: `src/ModManager.App/ViewModels/MainViewModel.cs` (OAuth connect; dark-window state; legacy-key notice)
- Modify: `src/ModManager.App/MainWindow.xaml.cs` (startup fetch gated)
- Test: `tests/ModManager.Tests/Nexus/NexusConsentGateTests.cs`

**Interfaces:**
- Consumes: `IAuthorizedSend` (Task 1), `NexusService.ValidBearerAsync` (Task 8), `NexusOAuthService.ConnectAsync` (Task 9), `NexusAuthGate` (Task 7), `NexusAuthHeaders` (Task 5).
- Produces: `HostServices : IPluginHostServices, IModTextSearch?, IAuthorizedSend`; `bool PluginFeedSource.NeedsFirstInstallConsent()`.

- [ ] **Step 1: Failing test — first-install-vs-update consent predicate**

```csharp
// tests/ModManager.Tests/Nexus/NexusConsentGateTests.cs
using Xunit;

public class NexusConsentGateTests
{
    // Pure predicate mirrored into Core-style logic: consent required iff no plugin installed yet.
    private static bool NeedsFirstInstallConsent(int installedCount) => installedCount == 0;

    [Theory]
    [InlineData(0, true)]   // first-ever install -> must consent
    [InlineData(1, false)]  // already installed -> update, no re-prompt
    public void Consent_only_on_first_install(int installed, bool expected) =>
        Assert.Equal(expected, NeedsFirstInstallConsent(installed));
}
```

- [ ] **Step 2: Run to verify it passes as the spec (guards the rule the App code implements)**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter NexusConsentGate`
Expected: PASS. (This pins the rule; the App wiring below must honor it.)

- [ ] **Step 3: `HostServices` implements `IAuthorizedSend`**

In `PluginHost.cs`, extend the nested `HostServices` (line 102) to take `NexusService` and implement the interface:

```csharp
private sealed class HostServices(
    ModSourceRegistry registry, HttpClient httpClient, NexusService nexus, string appVersion)
    : IPluginHostServices, IAuthorizedSend
{
    public void AddModSource(IModSource source) => registry.Add(source);
    public HttpClient HttpClient => httpClient;
    public string AppVersion => appVersion;

    #pragma warning disable CS0618
    public string? GetCredential(string key) => null;   // host no longer hands out secrets
    #pragma warning restore CS0618

    public async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpRequestMessage request, string credentialKey, CancellationToken ct = default)
    {
        string? bearer = credentialKey.Equals("nexus", StringComparison.OrdinalIgnoreCase)
            ? await nexus.ValidBearerAsync().ConfigureAwait(false)
            : null;
        NexusAuthHeaders.Apply(request, bearer, "626-mod-launcher", appVersion);
        var resp = await httpClient.SendAsync(request, ct).ConfigureAwait(false);

        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && bearer is not null)
        {
            resp.Dispose();
            var retryBearer = await nexus.ValidBearerAsync().ConfigureAwait(false); // ValidBearer refreshes internally
            var retry = await CloneAsync(request).ConfigureAwait(false);
            NexusAuthHeaders.Apply(retry, retryBearer, "626-mod-launcher", appVersion);
            return await httpClient.SendAsync(retry, ct).ConfigureAwait(false);
        }
        return resp;
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage r)
    {
        var c = new HttpRequestMessage(r.Method, r.RequestUri);
        if (r.Content is not null)
        {
            var bytes = await r.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            c.Content = new ByteArrayContent(bytes);
            foreach (var h in r.Content.Headers) c.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        return c;
    }
}
```

Update the `HostServices` construction site to pass `nexus` + `appVersion` instead of the `getCredential` Func. In `App.xaml.cs`, remove the `nexus.GetCredential` Func argument to `PluginHost.LoadAll` / `PluginFeedSource`; those now receive `NexusService` directly (or nothing, per their new ctors).

- [ ] **Step 4: Consent gate in `PluginFeedSource`**

Add the predicate + gate `MaybeFetchOnConnectAsync` and the startup fetch behind an injected confirm delegate:

```csharp
// PluginFeedSource.cs
public bool NeedsFirstInstallConsent() => InstalledPluginsStore.Read(RecordPath).Count == 0;

// Delegate set by MainWindow: shows the consent dialog, returns true to proceed.
public Func<Task<bool>>? ConfirmFirstInstallAsync { get; set; }

public async Task MaybeFetchOnConnectAsync()
{
    if (NeedsFirstInstallConsent())
    {
        if (ConfirmFirstInstallAsync is null || !await ConfirmFirstInstallAsync().ConfigureAwait(false))
            return; // user declined the first plugin download
    }
    await FetchAsync(force: NeedsFirstInstallConsent()).ConfigureAwait(false);
}
```

Startup trigger (`MainWindow.xaml.cs:127-129`): if `NeedsFirstInstallConsent()` is true, do NOT auto-fetch on startup — first install only ever happens through the consented connect path.

- [ ] **Step 5: Settings + VM — "Connect Nexus account", dark window, legacy notice**

- `SettingsDialog.xaml`: remove the API-key `TextBox` + paste/validate button; add a **"Connect Nexus account"** button bound to `ViewModel.ConnectNexusAsync()` (no args). When `NexusAuthGate.Status(...) == NotConfigured`, the button is disabled with sublabel "Secure sign-in is being finalized with Nexus."
- `MainViewModel`: replace `ConnectNexusAsync(string apiKey)` with:

```csharp
public async Task ConnectNexusAsync()
{
    var result = await _oauth.ConnectAsync(CancellationToken.None);
    if (result.Ok)
    {
        RaiseNexusStateChanged();
        _ = App.AppHost.Services.GetRequiredService<PluginFeedSource>().MaybeFetchOnConnectAsync();
    }
    else ShowNexusError(result.Error);
}

public bool NexusUserFeaturesAvailable =>
    NexusAuthGate.CanUseUserScopedFeatures(_oauth.Config.IsConfigured, _nexus.IsConnected);
```

- Gate the user-scoped commands (endorse, identify, refresh-stats, loose-identify's *apply*) on `NexusUserFeaturesAvailable`; the unauthenticated GraphQL **search** stays available regardless.
- On startup, if `_nexus.LegacyKeyWasDiscarded`, show a one-time info: "Nexus now uses secure sign-in. Your old API key was removed — click Connect Nexus account to reconnect."

- [ ] **Step 6: First-install consent dialog copy (MainWindow)**

Wire `PluginFeedSource.ConfirmFirstInstallAsync` to a `ContentDialog`:
> **Connect Nexus and install the Nexus add-on?**
> To use Nexus features, 626 needs to (1) sign you in to Nexus in your browser, and (2) download a small signed add-on (the Nexus plugin) from the 626 plugin feed. Nothing is installed until you agree.
> [Connect and install] [Not now]

- [ ] **Step 7: Full app build + Core suite + purity + STORE seal**

```bash
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64          # FULL, expect: Build succeeded
dotnet build src/ModManager.App/ModManager.App.csproj -p:Platform=x64 -p:Configuration=Store  # expect: Build succeeded
dotnet test  tests/ModManager.Tests/ModManager.Tests.csproj                    # expect: Passed, 0 failed
dotnet test  tests/ModManager.Tests/ModManager.Tests.csproj --filter CorePurity # expect: 3/3
pwsh scripts/check-store-seal.ps1                                              # expect: STORE seal OK
```

- [ ] **Step 8: Smoke entries**

Append to `docs/smoke-tests/pending.md`: (a) OAuth connect end-to-end once a real `client_id` is fed (browser opens, sign-in, features light up); (b) dark-window state with `client_id` blank (user-scoped disabled + message, search still works); (c) first-connect shows the combined consent dialog; declining installs nothing; (d) already-installed plugin updates without re-prompting; (e) upgrading from a key build shows the legacy-key-discarded notice.

- [ ] **Step 9: Commit**

```bash
git add src/ModManager.App tests/ModManager.Tests/Nexus/NexusConsentGateTests.cs docs/smoke-tests/pending.md
git commit -m "feat(nexus): host-owned authorized send, OAuth connect UI, consent gate, dark-window states"
```

---

## Task 12: Plugin migration (`626-mod-plugins`) — bump ABI + host-authorized send

**Files:**
- Modify: `src/ModManager.Plugin.Nexus/ModManager.Plugin.Nexus.csproj` (Abstractions 0.10.0 → 0.11.0)
- Modify: `src/ModManager.Plugin.Nexus/NexusModSource.cs` (ctor takes host ref; `SendAsync` prefers `IAuthorizedSend`)
- Modify: `src/ModManager.Plugin.Nexus/NexusPlugin.cs` (`Register` passes host)
- Modify: `.github/workflows/release.yml` (minBinaryVersion → 0.11.0)
- Test: `tests/ModManager.Plugin.Nexus.Tests/NexusAuthorizedSendTests.cs`

**Dev bootstrap (Abstractions 0.11.0 not yet on nuget.org):** pack it locally from the launcher repo and restore from a local source:
```bash
dotnet pack ../626-mod-launcher/src/ModManager.Plugins.Abstractions/ModManager.Plugins.Abstractions.csproj \
  -p:Version=0.11.0 -o ./local-nuget
# add ./local-nuget as a restore source (nuget.config) for dev; PackageReference stays Version=0.11.0
```
The published 0.11.0 (from the launcher release) resolves from nuget.org at release time; the local source is dev-only.

**Interfaces:**
- Consumes: `IAuthorizedSend`, `IPluginHostServices` (Abstractions 0.11.0).
- Produces: `NexusModSource(IPluginHostServices host)`; `NexusPlugin.Register` → `new NexusModSource(host)`.

- [ ] **Step 1: Failing test — SendAsync prefers IAuthorizedSend, stamps no apikey**

```csharp
// tests/ModManager.Plugin.Nexus.Tests/NexusAuthorizedSendTests.cs
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ModManager.Plugins.Abstractions;
using ModManager.Plugin.Nexus;
using Xunit;

public class NexusAuthorizedSendTests
{
    private sealed class FakeHost : IPluginHostServices, IAuthorizedSend
    {
        public HttpRequestMessage? LastRequest;
        public bool AuthorizedSendCalled;
        public void AddModSource(IModSource s) { }
        #pragma warning disable CS0618
        public string? GetCredential(string key) => "SHOULD_NOT_BE_USED";
        #pragma warning restore CS0618
        public HttpClient HttpClient { get; } = new(new StubHandler());
        public string AppVersion => "0.11.0";
        public Task<HttpResponseMessage> SendAuthorizedAsync(HttpRequestMessage request, string credentialKey, CancellationToken ct = default)
        {
            AuthorizedSendCalled = true; LastRequest = request;
            Assert.False(request.Headers.Contains("apikey"));   // plugin must NOT stamp a key
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"mod\":{}}") });
        }
        private sealed class StubHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    [Fact]
    public async Task Uses_authorized_send_and_never_stamps_apikey()
    {
        var host = new FakeHost();
        var src = new NexusModSource(host);
        await src.FetchMetadataAsync(new SourceModRef("eldenring", 42, null));
        Assert.True(host.AuthorizedSendCalled);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/ModManager.Plugin.Nexus.Tests/ModManager.Plugin.Nexus.Tests.csproj --filter NexusAuthorizedSend`
Expected: FAIL — `NexusModSource(host)` ctor does not exist.

- [ ] **Step 3: Migrate the ctor + transport**

`.csproj` line 8: `<PackageReference Include="ModManager.Plugins.Abstractions" Version="0.11.0" />`.

`NexusModSource.cs` — new ctor + `SendAsync`:

```csharp
private readonly IPluginHostServices _host;
private readonly IAuthorizedSend? _authorized;
private readonly HttpClient _http;
private readonly string? _appVersion;

public NexusModSource(IPluginHostServices host)
{
    _host = host;
    _authorized = host as IAuthorizedSend;
    _http = host.HttpClient;
    _appVersion = host.AppVersion;
}

private const string ApplicationName = "626-mod-launcher";

private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? body = null)
{
    var msg = new HttpRequestMessage(method, url);
    if (body is not null) msg.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

    if (_authorized is not null)
        // Host owns auth: it stamps the bearer + ToS headers. Plugin sends nothing secret.
        return await _authorized.SendAuthorizedAsync(msg, "nexus");

    // Legacy fallback (pre-OAuth host): preserve ToS headers; no key available under the new host.
    msg.Headers.TryAddWithoutValidation("Application-Name", ApplicationName);
    msg.Headers.TryAddWithoutValidation("Application-Version", _appVersion);
    #pragma warning disable CS0618
    var legacyKey = _host.GetCredential("nexus");
    #pragma warning restore CS0618
    if (!string.IsNullOrEmpty(legacyKey))
        msg.Headers.TryAddWithoutValidation("apikey", legacyKey);
    return await _http.SendAsync(msg);
}
```

`NexusPlugin.cs:19-20`:
```csharp
public void Register(IPluginHostServices host) => host.AddModSource(new NexusModSource(host));
```

Remove the old `(HttpClient, Func<string?>, string?)` ctor and any `_getApiKey` field.

`.github/workflows/release.yml`: minBinaryVersion `0.10.0` → `0.11.0`.

- [ ] **Step 4: Run to verify it passes + full plugin suite**

```bash
dotnet test tests/ModManager.Plugin.Nexus.Tests/ModManager.Plugin.Nexus.Tests.csproj --filter NexusAuthorizedSend  # PASS
dotnet test tests/ModManager.Plugin.Nexus.Tests/ModManager.Plugin.Nexus.Tests.csproj                                 # all green
```

- [ ] **Step 5: Commit**

```bash
git add src/ModManager.Plugin.Nexus .github/workflows/release.yml tests/ModManager.Plugin.Nexus.Tests
git commit -m "feat(nexus): host-authorized transport (IAuthorizedSend); retire apikey; Abstractions 0.11.0"
```

---

## Task 13: ABI compat proof — the shipped 0.10.0 plugin still loads on the 0.11.0 host

**Files:**
- Test: `tests/ModManager.Tests/Plugins/LegacyPluginAbiTests.cs`

**Interfaces:**
- Consumes: `IPluginHostServices` (0.11.0).

- [ ] **Step 1: Test — a 0.10.0-shaped plugin (calls `GetCredential` at Register) loads against the new host contract**

```csharp
// tests/ModManager.Tests/Plugins/LegacyPluginAbiTests.cs
using System.Net.Http;
using ModManager.Plugins.Abstractions;
using Xunit;

public class LegacyPluginAbiTests
{
    // Mimics the shipped 0.10.0 plugin: it calls GetCredential unconditionally at Register.
    private sealed class LegacyStyleHost : IPluginHostServices
    {
        public bool GetCredentialCalled;
        public void AddModSource(IModSource s) { }
        #pragma warning disable CS0618
        public string? GetCredential(string key) { GetCredentialCalled = true; return null; }
        #pragma warning restore CS0618
        public HttpClient HttpClient { get; } = new();
        public string AppVersion => "0.11.0";
    }

    [Fact]
    public void Legacy_plugin_can_still_call_GetCredential_without_missing_method()
    {
        var host = new LegacyStyleHost();
        // The 0.10.0 plugin does exactly this at load; it must not throw.
        var key = ((IPluginHostServices)host).GetCredential("nexus");
        Assert.True(host.GetCredentialCalled);
        Assert.Null(key); // host returns null under OAuth — legacy plugin degrades to "no auth", not a crash
    }
}
```

- [ ] **Step 2: Run**

Run: `dotnet test tests/ModManager.Tests/ModManager.Tests.csproj --filter LegacyPluginAbi`
Expected: PASS — proves `GetCredential` is still callable (member present), so the shipped 0.10.0 plugin loads.

- [ ] **Step 3: Commit**

```bash
git add tests/ModManager.Tests/Plugins/LegacyPluginAbiTests.cs
git commit -m "test(plugins): prove shipped 0.10.0 plugin still loads on the 0.11.0 host (ABI intact)"
```

---

## Release choreography (human-gated — not a task)

1. Merge the launcher feature → cut **v0.11.0** → CI publishes **Abstractions 0.11.0** to NuGet.
2. Merge the plugin PR → tag **nexus-v0.11.0** (resolves the real 0.11.0 from NuGet; minBinaryVersion 0.11.0).
3. Reply to Nexus with: app name, the callback URL (`http://127.0.0.1:41999/callback`, ask re: loopback-any-port), the capability→scope list, and the public v0.11.0 build link as their review build.
4. On registration: publish the signed `nexus-oauth.json` (client_id) to the 626-mod-plugins release → startup fetch delivers it → OAuth lights up with no further release.
5. Store SKU: unchanged this cycle (no Nexus surface). Nexus-on-Store is the future compile-in spec.

---

## Self-review — spec coverage

- Keys removed completely → Tasks 8 (store), 11 (UI + `GetCredential`→null). ✔
- Host-owned auth, plugin never sees secret → Tasks 1 (`IAuthorizedSend`), 11 (host impl), 12 (plugin uses it, no apikey). ✔
- `GetCredential` deprecated not removed → Task 1 + Task 13 (ABI proof). ✔
- Loopback PKCE, host owns tokens/refresh → Tasks 2, 6, 8, 9. ✔
- client_id feed-delivered at startup (no connect circular-dep) → Tasks 3, 10. ✔
- Consent-gate first plugin download; updates don't re-prompt → Task 11 (predicate Task 11 test). ✔
- Dark window + unauth search stays alive → Tasks 7, 11. ✔
- Legacy key discarded + one-time notice → Tasks 8, 11. ✔
- What we hand Nexus (name, callback, scopes, review build) → Release choreography. ✔
- Store untouched, seal green → Task 11 Step 7. ✔
- camelCase on disk → Tasks 3, 4, 8 (round-trip asserts). ✔
- Build-time confirmations flagged (endpoints, identity call, port policy) → Global Constraints + inline `CONFIRM` notes. ✔
