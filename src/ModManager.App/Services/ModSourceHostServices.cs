using System.Net.Http;
using ModManager.Core.Nexus;
using ModManager.Core.Plugins;
using ModManager.Plugins.Abstractions;

namespace ModManager.App.Services;

/// <summary>
/// The App-side <see cref="IPluginHostServices"/> + <see cref="IAuthorizedSend"/> — owns the registry sink
/// and the shared <see cref="HttpClient"/>, and is the ONLY place a bearer touches an outbound mod-source
/// request. Under OAuth the host never hands raw secrets to source code (<see cref="GetCredential"/>
/// returns null); a source builds an UNAUTHENTICATED request and calls
/// <see cref="SendAuthorizedAsync"/>, where the host attaches the bearer server-side. The token never
/// reaches the source and is never logged.
///
/// <para>Deliberately flavor-neutral and deliberately NOT named after the loader. Both SKUs need these
/// services, but they obtain their mod sources differently: the off-Store build loads a downloaded signed
/// assembly, while the Store build compiles the source in and registers it directly (nothing is downloaded
/// or executed that did not ship in the package). Keeping this shared means the two SKUs run byte-identical
/// auth and registration semantics instead of drifting apart — and keeping the loader's name out of it
/// means the Store binary stays clean for <c>scripts/check-store-seal.ps1</c>, which scans the shipped DLLs
/// for the loader's symbols.</para>
/// </summary>
internal sealed class ModSourceHostServices(
    ModSourceRegistry registry, HttpClient httpClient, NexusService nexus, string appVersion)
    : IPluginHostServices, IAuthorizedSend
{
    public void AddModSource(IModSource source) => registry.Add(source);
    public HttpClient HttpClient => httpClient;

    /// <summary>The launcher's own assembly version — handed to sources for ToS-identity headers
    /// (e.g. the Nexus <c>Application-Version</c>), so the real shipped version flows through instead of
    /// the source's "0.0.0" fallback.</summary>
    public string AppVersion => appVersion;

    /// <summary>ABI-kept credential lookup. Under OAuth the host owns credentials and never hands a raw
    /// secret to source code — this always returns null. Existing call sites pass a key and tolerate null;
    /// the real auth path is <see cref="SendAuthorizedAsync"/>.</summary>
#pragma warning disable CS0618
    public string? GetCredential(string key) => null;
#pragma warning restore CS0618

    /// <summary>Send an authorized request on the source's behalf WITHOUT ever mutating the source's
    /// <paramref name="request"/>. For the "nexus" credential key the host resolves a currently-valid OAuth
    /// bearer and stamps it — plus the ToS identity headers — onto a HOST-OWNED clone
    /// (<see cref="NexusAuthHeaders.CloneWithAuthAsync"/>); any other key clones with identity headers only.
    /// The source's own request is only ever READ, so it never carries the bearer and the source can't read
    /// the token back off it. On a 401 with a bearer, force a token refresh (a server 401 means the token is
    /// bad regardless of local expiry belief), re-clone from the untouched request, and retry once. Finally
    /// <c>resp.RequestMessage</c> is nulled so the returned response can't hand back a handle to the
    /// bearer-stamped host request either.</summary>
    public async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpRequestMessage request, string credentialKey, CancellationToken ct = default)
    {
        string? bearer = credentialKey.Equals("nexus", StringComparison.OrdinalIgnoreCase)
            ? await nexus.ValidBearerAsync().ConfigureAwait(false)
            : null;
        var send = await NexusAuthHeaders.CloneWithAuthAsync(request, bearer, "626-mod-launcher", appVersion, ct).ConfigureAwait(false);
        var resp = await httpClient.SendAsync(send, ct).ConfigureAwait(false);

        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && bearer is not null)
        {
            resp.Dispose();
            var fresh = await nexus.ValidBearerAsync(forceRefresh: true).ConfigureAwait(false);
            var retry = await NexusAuthHeaders.CloneWithAuthAsync(request, fresh, "626-mod-launcher", appVersion, ct).ConfigureAwait(false);
            resp = await httpClient.SendAsync(retry, ct).ConfigureAwait(false);
        }

        resp.RequestMessage = null;   // don't hand the source a handle to the bearer-stamped host request
        return resp;
    }
}
