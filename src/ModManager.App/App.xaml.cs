using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using ModManager.App.Services;
using ModManager.App.ViewModels;
using ModManager.Core;

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
                services.AddSingleton<LudusaviService>();
                services.AddSingleton<GameProfileResolver>();
                services.AddSingleton<NexusService>();
                services.AddSingleton<SaveEditorService>();
                services.AddSingleton<AvatarService>();
                services.AddSingleton<AppSettingsService>();
                services.AddSingleton<UpdateChecker>();
                services.AddSingleton<RemoteManifestSource>();
                services.AddSingleton<RestorePointService>();
                services.AddTransient<MainViewModel>();
            })
            .Build();
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

        // Refresh the remote game-definition cache for the next launch (debounced, dark until a
        // feed URL is set). Fire-and-forget; failures are swallowed.
        _ = AppHost.Services.GetRequiredService<RemoteManifestSource>().RefreshAsync();
    }
}
