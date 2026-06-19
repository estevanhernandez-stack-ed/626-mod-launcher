#if FULL
using System.IO;
using System.Net.Http;
using System.Reflection;
using ModManager.Core;
using ModManager.Core.Plugins;

namespace ModManager.App.Services;

/// <summary>
/// Drives the off-Store plugin feed on the FULL flavor. On Nexus connect (or a 24h-debounced re-check)
/// it fetches the signed plugins.json from the 626-mod-plugins repo, verifies + gates + installs via the
/// headless <see cref="PluginFeedInstaller"/>, then hot-loads anything new through <see cref="PluginHost.LoadOne"/>
/// so Nexus works without a restart. Gated on the "keep plugins updated" setting (for re-checks); the
/// first install (no plugin yet) bypasses the debounce. Fail-silent: every failure is swallowed + logged;
/// a bad feed never disturbs an installed, working plugin. Absent entirely from the STORE build (#if FULL).
/// </summary>
public sealed class PluginFeedSource
{
    // Stable "latest release" asset URL on the plugins repo (see the 5c spec).
    private const string FeedUrl =
        "https://github.com/estevanhernandez-stack-ed/626-mod-plugins/releases/latest/download/plugins.json";

    private static readonly TimeSpan DebounceWindow = TimeSpan.FromHours(24);

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModManagerBuilder");
    private static readonly string StampPath = Path.Combine(DataDir, "last-plugin-check.txt");
    private static string RecordPath => Path.Combine(PluginHost.PluginsDir, "installed-plugins.json");

    private static Version AppVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private readonly HttpClient _http;
    private readonly ModSourceRegistry _registry;
    private readonly Func<string, string?> _getCredential;
    private readonly AppSettingsService _settings;

    public PluginFeedSource(HttpClient http, ModSourceRegistry registry,
        Func<string, string?> getCredential, AppSettingsService settings)
    {
        _http = http; _registry = registry; _getCredential = getCredential; _settings = settings;
    }

    /// <summary>Called after a successful Nexus connect. Installs the plugin immediately if none is
    /// installed yet (bypassing the debounce — the user wants Nexus now); otherwise it's a debounced
    /// update check gated on the "keep plugins updated" setting. Never throws.</summary>
    public async Task MaybeFetchOnConnectAsync()
    {
        // Debounce / toggle guard sits ABOVE the try/finally (mirrors RemoteManifestSource.RefreshAsync):
        // a debounced or toggle-off re-check returns WITHOUT stamping. Stamping a skipped run would rewrite
        // last-plugin-check.txt to "now" on every Nexus connect and starve the 24h re-check — it could
        // never become due. First install (anyInstalled == false) bypasses this and proceeds to fetch.
        bool anyInstalled = InstalledPluginsStore.Read(RecordPath).Count > 0;
        if (anyInstalled)
        {
            if (!_settings.KeepPluginsUpdated) return;                // re-checks are opt-out-able
            var last = ReadStamp();
            if (!NexusPollStamp.ShouldPoll(last, DateTime.UtcNow, DebounceWindow)) return;
        }
        // else: first install — fetch now regardless of stamp/toggle (they connected to use Nexus).

        try
        {
            var req = new PluginFeedRequest(FeedUrl, PluginSigningKey.PublicKeySpki.ToArray(),
                AppVersion, PluginHost.PluginsDir, RecordPath);

            var installed = await PluginFeedInstaller.RunAsync(req, Download).ConfigureAwait(false);

            foreach (var p in installed)
                PluginHost.LoadOne(p.DllPath, _registry, _getCredential, _http);  // hot-load — Nexus live now
        }
        catch (Exception ex) { AppDiagnostics.Log("plugin-feed", ex); }
        finally { WriteStamp(); }  // only after an actual fetch attempt
    }

    private async Task<byte[]?> Download(string url, CancellationToken ct)
    {
        try { return await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false); }
        catch { return null; }  // offline / 404 (feed not published yet) → null, the installer treats as skip
    }

    private static DateTime? ReadStamp()
    {
        try
        {
            if (!File.Exists(StampPath)) return null;
            return DateTime.TryParse(File.ReadAllText(StampPath).Trim(), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : null;
        }
        catch { return null; }
    }

    private static void WriteStamp()
    {
        try { Directory.CreateDirectory(DataDir); File.WriteAllText(StampPath, DateTime.UtcNow.ToString("O")); }
        catch { /* best-effort */ }
    }
}
#endif
