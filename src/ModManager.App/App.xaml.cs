using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using ModManager.App.Services;
using ModManager.App.ViewModels;
using ModManager.Core;
using ModManager.Core.Plugins;

namespace ModManager.App;

public partial class App : Application
{
    // The deployed metadata proxy (holds the CurseForge key server-side; the URL is not secret).
    private const string MetadataProxy = "https://626-mod-metadata-proxy.626labs.workers.dev";

    public static IHost AppHost { get; private set; } = null!;

    /// <summary>The active main window — set once during <see cref="OnLaunched"/>. Exposed so non-window
    /// surfaces (e.g. the tools panel) can wire <c>InitializeWithWindow</c> on Win11 pickers without
    /// walking the visual tree.</summary>
    public static Window? MainWindow { get; private set; }

    private Window? _window;

    public App()
    {
        InitializeComponent();
        HookCrashLogging();
        AppHost = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<HttpClient>();
                services.AddSingleton<ICurseForgeClient>(sp =>
                    new CurseForgeClient(sp.GetRequiredService<HttpClient>(), new CurseForgeOptions { BaseUrl = MetadataProxy }));
                services.AddSingleton<LauncherService>();
                services.AddSingleton<ModEngineService>();
                services.AddSingleton<DirectInjectService>();
                services.AddSingleton<ThemeService>();
                services.AddSingleton<SteamService>();
                services.AddSingleton<IStoreLibrary>(sp => sp.GetRequiredService<SteamService>());
                services.AddSingleton<LudusaviService>();
                services.AddSingleton<GameDefinitionResolver>();
                services.AddSingleton<NexusService>();
                // The loopback PKCE OAuth flow. Config defaults to the baked public endpoints here; Task 10
                // overlays the signed remote client_id at startup. RefreshAsync is wired into NexusService
                // after Build() so the token store can refresh without knowing any App/HTTP types.
                services.AddSingleton<NexusOAuthService>(sp =>
                    new NexusOAuthService(
                        sp.GetRequiredService<HttpClient>(),
                        sp.GetRequiredService<NexusService>()));
                services.AddSingleton<SaveEditorService>();
                services.AddSingleton<AvatarService>();
                services.AddSingleton<AppSettingsService>();
                services.AddSingleton<UpdateChecker>();
                services.AddSingleton<NexusUpdatePoll>();
                services.AddSingleton<RemoteManifestSource>();
                services.AddSingleton<RestorePointService>();
                // Find-what's-already-there discovery: read-only sweep + the per-game Nexus name
                // index it identifies against.
                services.AddSingleton<Services.DiscoveryScanService>();
                services.AddSingleton<Services.ModNameIndexSource>();
                // Registration repair: previews what an edit would do and applies it, move-before-write.
                services.AddSingleton<Services.RegistrationRepairService>();
                // The contribution sink loaded plugins register their mod sources into. Empty when no
                // plugin loads (the Store SKU + the zero-plugins path) — every consumer tolerates empty.
                services.AddSingleton<ModSourceRegistry>();
#if FULL
                // The off-Store plugin feed: fetches, verifies, and hot-loads the Nexus plugin on connect.
                // Absent from the STORE build — the Store SKU has no plugin host and no feed URL.
                services.AddSingleton<PluginFeedSource>(sp =>
                {
                    var nexus = sp.GetRequiredService<NexusService>();
                    return new PluginFeedSource(
                        sp.GetRequiredService<HttpClient>(),
                        sp.GetRequiredService<ModSourceRegistry>(),
                        nexus,
                        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
                        () => nexus.IsConnected,
                        sp.GetRequiredService<AppSettingsService>());
                });
#endif
                services.AddTransient<MainViewModel>();
                services.AddTransient<LibraryViewModel>();
            })
            .Build();

        // Wire the OAuth flow into the token store: given a refresh token, NexusService can obtain a fresh
        // token set without knowing any App/HTTP types (it holds only this injected delegate). Harmless in
        // the Store SKU — the delegate is just parked on the store until a connect happens.
        {
            var nexus = AppHost.Services.GetRequiredService<NexusService>();
            var oauth = AppHost.Services.GetRequiredService<NexusOAuthService>();
            nexus.RefreshAsync = oauth.RefreshAsync;

            // Task 10: apply the cached signed client_id synchronously — instant, no network — so it's
            // already present the moment the user could click Connect. Independent of connect/plugin
            // consent (the client_id has to exist BEFORE connect can work at all); any failure falls
            // back to the baked config, which is always the floor.
            oauth.Config = new NexusOAuthConfigSource(AppHost.Services.GetRequiredService<HttpClient>())
                .LoadCachedEffective();
        }

#if FULL
        // Discover + verify + load signed plugins (FULL flavor only — the Store SKU compiles this out).
        // Each plugin's mod sources land in the shared registry; the credential lookup + shared HttpClient
        // are App-owned and passed in. Fail-closed + per-plugin try/catch live in PluginHost — a bad or
        // missing plugins dir is a clean no-op, leaving the app on the zero-plugins path.
        PluginHost.LoadAll(
            AppHost.Services.GetRequiredService<ModSourceRegistry>(),
            AppHost.Services.GetRequiredService<HttpClient>(),
            AppHost.Services.GetRequiredService<NexusService>(),
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0");
#endif

#if STORE_NEXUS
        // Packaged (Store) SKU with Nexus compiled in: register the compiled-in sources directly. Same
        // Register() entry point and same host services the off-Store loader uses — only the delivery
        // differs, so nothing is downloaded or executed that did not ship in the reviewed package.
        BuiltInModSources.RegisterAll(
            AppHost.Services.GetRequiredService<ModSourceRegistry>(),
            AppHost.Services.GetRequiredService<HttpClient>(),
            AppHost.Services.GetRequiredService<NexusService>());
#endif
    }

    // Wire app-wide exception logging as early as possible. WinUI can swallow exceptions thrown from
    // input-event handlers — leaving the UI dead with no trace. Log every escape hatch; for the
    // UI-thread one keep the app alive (a logged near-miss beats a silent dead dialog or a hard crash).
    // AppDomain / unobserved-Task escapes can only be logged, not recovered.
    private void HookCrashLogging()
    {
        UnhandledException += (_, e) => { AppDiagnostics.Log("ui", e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppDiagnostics.Log("appdomain", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown"));
        TaskScheduler.UnobservedTaskException += (_, e) => { AppDiagnostics.Log("task", e.Exception); e.SetObserved(); };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        MainWindow = _window;
        _window.Activate();

        // Fire-and-forget update check (debounced 24h, fails silently). Comfort, not load-bearing.
        // Only meaningful when the app was installed via the Velopack Setup.exe — UpdateChecker
        // detects "not installed" (dev runs, portable zip) and exits without touching the network.
        _ = AppHost.Services.GetRequiredService<UpdateChecker>().CheckForUpdatesAsync();

        // Refresh the remote game-definition cache for the next launch (debounced 24h). The feed
        // has been live since v0.6.0. Fire-and-forget; failures are swallowed.
        _ = AppHost.Services.GetRequiredService<RemoteManifestSource>().RefreshAsync();

        // Background: fetch + verify a freshly-delivered OAuth client_id (same signed manifest rail,
        // Task 10). On success, hot-apply it to the live NexusOAuthService.Config so THIS session can
        // pick it up without a restart — deliberately independent of connect/plugin consent, since the
        // client_id must exist BEFORE the user connects. Fire-and-forget; failures are swallowed.
        _ = RefreshNexusOAuthConfigAsync();
    }

    // Task 10 background refresh glue: NexusOAuthConfigSource.RefreshAsync doesn't know about
    // NexusOAuthService (App services stay decoupled); this is the one place that connects "freshly
    // verified config" to "the live service the rest of the app reads Config from."
    private static async Task RefreshNexusOAuthConfigAsync()
    {
        var source = new NexusOAuthConfigSource(AppHost.Services.GetRequiredService<HttpClient>());
        var fresh = await source.RefreshAsync().ConfigureAwait(false);
        if (fresh is not null)
            AppHost.Services.GetRequiredService<NexusOAuthService>().Config = fresh;
    }
}
