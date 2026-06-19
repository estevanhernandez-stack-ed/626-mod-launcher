#if FULL
using System.IO;
using System.Net.Http;
using System.Reflection;
using ModManager.Core;
using ModManager.Core.Plugins;

namespace ModManager.App.Services;

/// <summary>
/// The outcome of a <see cref="PluginFeedSource.FetchAsync"/> call.
/// </summary>
public enum PluginFetchOutcome
{
    /// <summary>The fetch was skipped (not connected, debounce active, or concurrent call in progress).</summary>
    NotApplicable,
    /// <summary>The feed was reached and the installed plugin is already current.</summary>
    UpToDate,
    /// <summary>One or more plugins were downloaded, verified, and hot-loaded.</summary>
    Installed,
    /// <summary>The feed offers a plugin this launcher build is too old for — the user should update the
    /// launcher. <see cref="PluginFetchResult.Version"/> carries the required minimum binary version.</summary>
    RequiresUpdate,
    /// <summary>The fetch failed (offline, signature failure, installer error).</summary>
    Failed,
}

/// <summary>
/// Result returned by <see cref="PluginFeedSource.FetchAsync"/>.
/// </summary>
/// <param name="Outcome">High-level outcome code.</param>
/// <param name="Version">Version string of the installed/current plugin, when known.</param>
/// <param name="Message">Short human-readable reason — populated on <see cref="PluginFetchOutcome.Failed"/>
/// and optionally on other outcomes for UI display.</param>
public sealed record PluginFetchResult(PluginFetchOutcome Outcome, string? Version, string? Message);

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
    private readonly Func<bool> _isConnected;
    private readonly AppSettingsService _settings;

    /// <summary>In-flight guard: two rapid Nexus-connect events can both clear the debounce and race into
    /// the installer (identical temp-file paths collide, double LoadOne, last-writer race on the record).
    /// A 0/1 flag set via <see cref="Interlocked.Exchange(ref int, int)"/> makes a concurrent second
    /// connect a clean no-op.</summary>
    private int _running;

    /// <summary>Raised after a fetch hot-loads at least one plugin via <see cref="PluginHost.LoadOne"/>.
    /// The UI subscribes to re-evaluate Nexus-action availability (the hearts / "Refresh Nexus stats"
    /// surface) without waiting for the next rescan or game switch. Raised on a background thread —
    /// the handler marshals to the UI thread.</summary>
    public event EventHandler? PluginLoaded;

    public PluginFeedSource(HttpClient http, ModSourceRegistry registry,
        Func<string, string?> getCredential, Func<bool> isConnected, AppSettingsService settings)
    {
        _http = http; _registry = registry; _getCredential = getCredential;
        _isConnected = isConnected; _settings = settings;
    }

    /// <summary>
    /// Core fetch implementation used by both the connect-trigger and the manual button.
    /// <para>
    /// When <paramref name="force"/> is <see langword="true"/> the debounce and the
    /// <see cref="AppSettingsService.KeepPluginsUpdated"/> gate are bypassed — used for the first-install
    /// path and the manual "Install / refresh" button. When <see langword="false"/> the usual debounce +
    /// toggle logic applies.
    /// </para>
    /// <para>
    /// Never throws. Every failure is caught, logged via <see cref="AppDiagnostics"/>, and returned as
    /// <see cref="PluginFetchOutcome.Failed"/> with a short message.
    /// </para>
    /// </summary>
    public async Task<PluginFetchResult> FetchAsync(bool force)
    {
        // In-flight guard: a concurrent call no-ops cleanly instead of racing the installer.
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return new PluginFetchResult(PluginFetchOutcome.NotApplicable, null, "already running");

        try
        {
            // Connection guard: force bypasses debounce and the toggle, but not the connectivity
            // requirement. Without Nexus credentials the feed would hit the network and return a
            // vague network error. NotApplicable here lets Task 4 (the manual button) map this to
            // the "Connect Nexus first." UI string instead.
            if (!_isConnected())
                return new PluginFetchResult(PluginFetchOutcome.NotApplicable, null, "not connected");

            // Debounce / toggle guard (mirrors RemoteManifestSource.RefreshAsync): a debounced or
            // toggle-off re-check returns WITHOUT stamping. Stamping a skipped run would rewrite
            // last-plugin-check.txt to "now" on every call and starve the 24h re-check.
            // force bypasses both guards (first-install path + manual button).
            if (!force)
            {
                bool anyInstalled = InstalledPluginsStore.Read(RecordPath).Count > 0;
                if (anyInstalled)
                {
                    if (!_settings.KeepPluginsUpdated)
                        return new PluginFetchResult(PluginFetchOutcome.NotApplicable, null, "keep-plugins-updated is off");

                    var last = ReadStamp();
                    if (!NexusPollStamp.ShouldPoll(last, DateTime.UtcNow, DebounceWindow))
                        return new PluginFetchResult(PluginFetchOutcome.NotApplicable, null, "debounce active");
                }
                // else: first install — fetch now regardless of stamp/toggle.
            }

            try
            {
                var req = new PluginFeedRequest(FeedUrl, PluginSigningKey.PublicKeySpki.ToArray(),
                    AppVersion, PluginHost.PluginsDir, RecordPath);

                var run = await PluginFeedInstaller.RunAsync(req, Download).ConfigureAwait(false);

                var anyLoaded = false;
                string? version = null;
                foreach (var p in run.Installed)
                {
                    anyLoaded |= PluginHost.LoadOne(p.DllPath, _registry, _getCredential, _http);
                    version ??= p.Version;
                }

                // Tell the UI a plugin went live so Nexus actions (hearts / Refresh stats) light up
                // without a rescan. Only when something actually loaded — a no-op fetch must not nudge.
                if (anyLoaded) PluginLoaded?.Invoke(this, EventArgs.Empty);

                if (anyLoaded)
                    return new PluginFetchResult(PluginFetchOutcome.Installed, version, null);

                // Nothing loaded — say WHY honestly, never a blanket "up to date":
                // installed-to-disk-but-wouldn't-load is a real failure, not "current".
                if (run.Installed.Count > 0)
                    return new PluginFetchResult(PluginFetchOutcome.Failed, null, "plugin installed but failed to load");

                // Feed offline / unverifiable — not "up to date".
                if (!run.FeedReached)
                    return new PluginFetchResult(PluginFetchOutcome.Failed, null, "couldn't reach or verify the plugin feed");

                // Feed offers a plugin this build is too old for — point the user at a launcher update,
                // not a misleading "up to date". This is exactly what a sub-minBinaryVersion build hit.
                if (run.RequiresNewerBinary is { } need)
                    return new PluginFetchResult(PluginFetchOutcome.RequiresUpdate, need.ToString(),
                        $"this plugin needs launcher v{need} — update the launcher");

                // Feed reached, plugin present + current. Surface the current installed version.
                var existing = InstalledPluginsStore.Read(RecordPath);
                // Prefer the Nexus plugin's own version; fall back to any recorded entry (FirstOrDefault is
                // null-safe on an empty record). Guards against showing a non-Nexus version once a 2nd plugin exists.
                var currentVersion = existing.TryGetValue("nexus", out var nv) ? nv : existing.Values.FirstOrDefault();
                return new PluginFetchResult(PluginFetchOutcome.UpToDate, currentVersion, null);
            }
            catch (Exception ex)
            {
                AppDiagnostics.Log("plugin-feed", ex);
                return new PluginFetchResult(PluginFetchOutcome.Failed,
                    null, FriendlyMessage(ex));
            }
            finally { WriteStamp(); }  // only after an actual fetch attempt (debounce-skipped paths return before here)
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    /// <summary>Called after a successful Nexus connect. Installs the plugin immediately if none is
    /// installed yet (bypassing the debounce — the user wants Nexus now); otherwise it's a debounced
    /// update check gated on the "keep plugins updated" setting. Never throws (fire-and-forget).</summary>
    public Task MaybeFetchOnConnectAsync()
        => FetchAsync(force: InstalledPluginsStore.Read(RecordPath).Count == 0);

    private async Task<byte[]?> Download(string url, CancellationToken ct)
    {
        try { return await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false); }
        catch { return null; }  // offline / 404 (feed not published yet) → null, the installer treats as skip
    }

    private static string FriendlyMessage(Exception ex) => ex switch
    {
        HttpRequestException   => "couldn't reach the plugin feed",
        TaskCanceledException  => "plugin feed request timed out",
        _                      => ex.Message is { Length: > 0 } m ? m : "plugin fetch failed",
    };

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
