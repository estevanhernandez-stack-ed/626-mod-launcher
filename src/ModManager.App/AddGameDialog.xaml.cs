using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core;
using Windows.Storage.Pickers;

namespace ModManager.App;

public sealed partial class AddGameDialog : ContentDialog
{
    private readonly IntPtr _hwnd;

    public sealed record EngineOption(string Key, string Label);

    public AddGameDialog(IntPtr hwnd)
    {
        InitializeComponent();
        _hwnd = hwnd;
        EngineBox.ItemsSource = EnginePresets.Presets.Select(kv => new EngineOption(kv.Key, kv.Value.Label)).ToList();
        EngineBox.SelectedIndex = 0;
    }

    private void OnEngineChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EngineBox.SelectedItem is EngineOption opt && EnginePresets.Presets.TryGetValue(opt.Key, out var preset))
            ModPathBox.Text = preset.ModPath;
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        FolderBox.Text = folder.Path;
        if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = folder.Name;
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text) || string.IsNullOrWhiteSpace(FolderBox.Text))
        {
            ErrorText.Visibility = Visibility.Visible;
            args.Cancel = true; // keep the dialog open
        }
    }

    /// <summary>The assembled input — call only after a Primary result (validation has passed).</summary>
    public GameInput BuildInput() => new()
    {
        Name = NameBox.Text.Trim(),
        Engine = (EngineBox.SelectedItem as EngineOption)?.Key ?? "custom",
        GameRoot = FolderBox.Text.Trim(),
        ModPath = string.IsNullOrWhiteSpace(ModPathBox.Text) ? null : ModPathBox.Text.Trim(),
        SteamAppId = string.IsNullOrWhiteSpace(SteamBox.Text) ? null : SteamBox.Text.Trim(),
    };
}
