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
                services.AddTransient<MainViewModel>();
            })
            .Build();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
