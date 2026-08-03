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

        var services = new ModSourceHostServices(registry, httpClient, nexus, appVersion);
        plugin.Register(services);
    }
}
#endif // FULL — the entire loader (AssemblyLoadContext + external-code-from-stream) is absent from the STORE build, not just dormant.
