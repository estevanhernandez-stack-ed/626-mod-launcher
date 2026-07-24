#if FULL
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using ModManager.Core.Nexus;
using ModManager.Core.Plugins;
using ModManager.Plugins.Abstractions;

namespace ModManager.App.Services;

/// <summary>
/// App-side plugin loader (FULL flavor only — the Store SKU compiles the call site out via <c>#if FULL</c>).
/// Discovers <c>*.dll</c> + sibling <c>*.dll.sig</c> in <c>%LOCALAPPDATA%\ModManagerBuilder\plugins\</c>,
/// verifies each against the pinned <see cref="PluginSigningKey"/> via <see cref="PluginSignature.Verify"/>,
/// and only then loads the verified assembly in a collectible <see cref="AssemblyLoadContext"/>. The single
/// exported <see cref="IModManagerPlugin"/> type is instantiated and handed an <see cref="IPluginHostServices"/>
/// it uses to register contributions (mod sources land in the shared <see cref="ModSourceRegistry"/>).
///
/// Fail-closed: an unsigned, mis-signed, or tampered assembly is never loaded. Every plugin is wrapped in
/// try/catch so one bad plugin never crashes startup — the app simply runs with whatever loaded cleanly
/// (and an empty registry is the zero-plugins path, identical to the Store SKU).
///
/// The <see cref="HttpClient"/> + token store are App-owned and passed in: under OAuth the host never hands
/// a raw secret to plugin code (<c>GetCredential</c> returns null). A source authorizes by handing the host
/// an unauthenticated request via <see cref="IAuthorizedSend"/>; the host attaches the bearer server-side and
/// the token never lands anywhere the plugin controls (operating law #2).
/// </summary>
public static class PluginHost
{
    /// <summary>The on-disk plugins directory — sibling to the other runtime data under
    /// <c>%LOCALAPPDATA%\ModManagerBuilder\</c> (matches <c>RemoteManifestSource</c> / <c>AppDiagnostics</c>).</summary>
    public static string PluginsDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModManagerBuilder", "plugins");

    /// <summary>Load every plugin recorded in <c>installed-plugins.json</c>. Loads exactly the
    /// <c>&lt;id&gt;.dll</c> files the feed installer wrote — any other dll in the directory (e.g. a
    /// stale hand-dropped <c>ModManager.Plugin.Nexus.dll</c> from dev-testing) is silently skipped.
    /// This prevents a leftover verified-but-stale dll from loading first (alphabetical order) and
    /// shadowing the feed-installed plugin with an older build. The signature gate inside
    /// <see cref="LoadOne"/> still applies — an id-named but tampered dll is refused.
    /// No-op (and safe) when the plugins dir or the record is missing.</summary>
    public static void LoadAll(ModSourceRegistry registry, HttpClient httpClient, NexusService nexus, string appVersion)
    {
        if (!Directory.Exists(PluginsDir)) return;
        var recordPath = Path.Combine(PluginsDir, "installed-plugins.json");
        var recorded = InstalledPluginsStore.Read(recordPath);
        if (recorded.Count == 0) return;
        foreach (var id in recorded.Keys)
        {
            var dll = Path.Combine(PluginsDir, $"{id}.dll");
            if (!File.Exists(dll))
            {
                AppDiagnostics.Log("plugin-host", new FileNotFoundException($"Recorded plugin dll not found: {dll}"));
                continue;
            }
            LoadOne(dll, registry, httpClient, nexus, appVersion);
        }
    }

    /// <summary>Verify + load a single plugin dll (the just-downloaded hot-load path and the per-file
    /// step of <see cref="LoadAll"/>). Returns true iff a plugin assembly was loaded and registered.
    /// Fail-closed + never throws: a missing/bad signature or a load error logs and returns false.</summary>
    public static bool LoadOne(string dllPath, ModSourceRegistry registry, HttpClient httpClient, NexusService nexus, string appVersion)
    {
        try
        {
            var sig = dllPath + ".sig";
            if (!File.Exists(sig)) return false;
            var assemblyBytes = File.ReadAllBytes(dllPath);
            var signatureBytes = File.ReadAllBytes(sig);
            if (!PluginSignature.Verify(assemblyBytes, signatureBytes)) return false;
            LoadVerified(assemblyBytes, registry, httpClient, nexus, appVersion);
            return true;
        }
        catch (Exception ex)
        {
            AppDiagnostics.Log("plugin-host", ex);
            return false;
        }
    }

    private static void LoadVerified(
        byte[] assemblyBytes, ModSourceRegistry registry, HttpClient httpClient, NexusService nexus, string appVersion)
    {
        // Collectible context so a future reload/unload path can drop the assembly cleanly.
        var alc = new AssemblyLoadContext(name: "ModManagerPlugin", isCollectible: true);
        using var stream = new MemoryStream(assemblyBytes);
        var assembly = alc.LoadFromStream(stream);

        var entryType = assembly.GetExportedTypes()
            .FirstOrDefault(t => typeof(IModManagerPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });
        if (entryType is null) return; // not a plugin assembly

        if (Activator.CreateInstance(entryType) is not IModManagerPlugin plugin) return;

        var services = new HostServices(registry, httpClient, nexus, appVersion);
        plugin.Register(services);
    }

    /// <summary>The App-side <see cref="IPluginHostServices"/> + <see cref="IAuthorizedSend"/> — owns the
    /// registry sink and the shared <see cref="HttpClient"/>, and is the ONLY place a bearer touches an
    /// outbound plugin request. Under OAuth the host no longer hands raw secrets to plugin code
    /// (<see cref="GetCredential"/> returns null); a plugin instead builds an UNAUTHENTICATED request and
    /// calls <see cref="SendAuthorizedAsync"/>, where the host attaches the bearer server-side. The token
    /// never reaches the plugin and is never logged.</summary>
    private sealed class HostServices(ModSourceRegistry registry, HttpClient httpClient, NexusService nexus, string appVersion)
        : IPluginHostServices, IAuthorizedSend
    {
        public void AddModSource(IModSource source) => registry.Add(source);
        public HttpClient HttpClient => httpClient;

        /// <summary>The launcher's own assembly version — handed to plugins for ToS-identity headers
        /// (e.g. the Nexus <c>Application-Version</c>). Passed in by the loader so the real shipped
        /// version flows through instead of the source's "0.0.0" fallback.</summary>
        public string AppVersion => appVersion;

        /// <summary>ABI-kept credential lookup. Under OAuth the host owns credentials and never hands a
        /// raw secret to plugin code — this always returns null. Existing call sites pass a key and
        /// tolerate null; the real auth path is <see cref="SendAuthorizedAsync"/>.</summary>
#pragma warning disable CS0618
        public string? GetCredential(string key) => null;
#pragma warning restore CS0618

        /// <summary>Send an authorized request on the plugin's behalf WITHOUT ever mutating the plugin's
        /// <paramref name="request"/>. For the "nexus" credential key the host resolves a currently-valid
        /// OAuth bearer and stamps it — plus the ToS identity headers — onto a HOST-OWNED clone
        /// (<see cref="NexusAuthHeaders.CloneWithAuthAsync"/>); any other key clones with identity headers
        /// only. The plugin's own request is only ever READ, so it never carries the bearer and the plugin
        /// can't read the token back off it. On a 401 with a bearer, force a token refresh (a server 401
        /// means the token is bad regardless of local expiry belief), re-clone from the untouched request,
        /// and retry once. Finally <c>resp.RequestMessage</c> is nulled so the returned response can't hand
        /// the plugin a handle to the bearer-stamped host request either.</summary>
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

            resp.RequestMessage = null;   // don't hand the plugin a handle to the bearer-stamped host request
            return resp;
        }
    }
}
#endif // FULL — the entire loader (AssemblyLoadContext + external-code-from-stream) is absent from the STORE build, not just dormant.
