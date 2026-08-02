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
/// <see cref="NexusService"/> (DPAPI); no token is exposed to plugin code, logged, or shown to the browser
/// (only the authorize URL — which is public-by-design in a PKCE public client — reaches the browser).
/// The security-relevant token-body parse lives in Core (<see cref="NexusTokenResponse"/>), unit-tested.
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

            // The browser only ever receives the authorize URL — no secret (PKCE public client: the
            // challenge is a hash, the state is a CSRF nonce). The user's password never touches this app.
            Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

            var ctx = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(3), ct).ConfigureAwait(false);
            var query = ctx.Request.QueryString;
            RespondAndClose(ctx, "You can return to 626 Mod Launcher now.");

            // Validate CSRF state BEFORE the code is read or exchanged — a mismatch aborts without exchange.
            var returnedState = query["state"] ?? "";
            if (!NexusPkce.StateMatches(state, returnedState))
                return new(false, null, "Sign-in could not be verified (state mismatch). Please try again.");

            var code = query["code"];
            if (string.IsNullOrEmpty(code))
                return new(false, null, query["error"] ?? "Sign-in was cancelled.");

            var tokens = await ExchangeAsync(code!, redirectUri, verifier, ct).ConfigureAwait(false);
            if (tokens is null) return new(false, null, "Could not complete sign-in with Nexus.");

            var (user, premium) = NexusJwtClaims.ReadIdentity(tokens.AccessToken);
            nexus.SaveTokens(tokens, user, premium);
            return new(true, user, null);
        }
        catch (OperationCanceledException) { return new(false, null, "Sign-in timed out."); }
        catch (Exception ex) { return new(false, null, ex.Message); }
        finally { listener.Stop(); }
    }

    public async Task<NexusTokenSet?> RefreshAsync(string refreshToken)
    {
        if (!Config.IsConfigured) return null;
        // Harden: any network/parse failure returns null so ValidBearerAsync's null-path fires (drop the
        // connection, graceful reconnect) instead of throwing up through IAuthorizedSend.SendAuthorizedAsync.
        try
        {
            using var body = NexusTokenRequest.BuildRefreshBody(Config, refreshToken);
            using var resp = await http.PostAsync(Config.TokenUrl, body).ConfigureAwait(false);
            return await ParseTokenAsync(resp).ConfigureAwait(false);
        }
        catch { return null; }
    }

    private async Task<NexusTokenSet?> ExchangeAsync(string code, string redirectUri, string verifier, CancellationToken ct)
    {
        using var body = NexusTokenRequest.BuildExchangeBody(Config, code, redirectUri, verifier);
        using var resp = await http.PostAsync(Config.TokenUrl, body, ct).ConfigureAwait(false);
        return await ParseTokenAsync(resp).ConfigureAwait(false);
    }

    // The token-endpoint body parse is a pure Core function (NexusTokenResponse) so the fail-closed
    // field extraction is unit-tested. This wrapper only owns the HTTP success-gate + string read.
    private static async Task<NexusTokenSet?> ParseTokenAsync(HttpResponseMessage resp)
    {
        if (!resp.IsSuccessStatusCode) return null;
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return NexusTokenResponse.Parse(body, DateTimeOffset.UtcNow);
    }

    /// <summary>Re-read the connected account's identity (display name + premium) from the current OAuth
    /// token's JWT claims and push it into <see cref="NexusService"/>. Refreshing the bearer first means a
    /// renewed token carries fresh claims. Offline-safe: any failure leaves the last-known identity
    /// untouched. Used by the "refresh account" path (Settings open) — NOT the token refresh itself.</summary>
    public async Task RefreshIdentityAsync()
    {
        try
        {
            var bearer = await nexus.ValidBearerAsync().ConfigureAwait(false);
            if (bearer is null) return; // disconnected / refresh rejected — nothing to refresh
            var (user, premium) = NexusJwtClaims.ReadIdentity(bearer);
            if (user is null) return; // couldn't read claims — keep the last-known name/premium
            if (nexus.CurrentTokens is { } tokens) nexus.SaveTokens(tokens, user, premium);
        }
        catch { /* offline / transient — keep last-known identity */ }
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
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private static void RespondAndClose(HttpListenerContext ctx, string message)
    {
        var html = Encoding.UTF8.GetBytes($"<html><body style='font-family:sans-serif;background:#0f1f31;color:#fff;text-align:center;padding-top:80px'>{message}</body></html>");
        ctx.Response.ContentType = "text/html";
        ctx.Response.OutputStream.Write(html, 0, html.Length);
        ctx.Response.Close();
    }
}
