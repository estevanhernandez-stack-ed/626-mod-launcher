using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.ViewModels;
using Windows.Storage.Pickers;

namespace ModManager.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private bool _loaded;

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = App.AppHost.Services.GetRequiredService<MainViewModel>();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Activated += OnFirstActivated;
    }

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        await ViewModel.LoadAsync();
    }

    // OneWay IsOn + this handler: ignore the programmatic set during reload (when the switch
    // already matches the committed state), act only on a real user flip.
    private async void OnModToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch sw || sw.DataContext is not ModRowViewModel row) return;
        if (sw.IsOn == row.Mod.Enabled) return;
        row.Enabled = sw.IsOn;
        await ViewModel.ToggleAsync(row);
    }

    private async void OnAddMods(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.FileTypeFilter.Add("*");
        var files = await picker.PickMultipleFilesAsync();
        if (files is { Count: > 0 })
            await ViewModel.AddModsAsync(files.Select(f => f.Path).ToList());
    }
}
