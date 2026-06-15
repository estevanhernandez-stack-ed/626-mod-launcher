using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.ViewModels;
using ModManager.Core;

namespace ModManager.App;

public sealed partial class ProfilesDialog : ContentDialog
{
    private readonly GameContext _ctx;
    private readonly MainViewModel _vm;

    /// <summary>True if a profile was loaded — the main mod list should refresh on close.</summary>
    public bool Changed { get; private set; }

    public ProfilesDialog(GameContext ctx, MainViewModel vm)
    {
        InitializeComponent();
        _ctx = ctx;
        _vm = vm;
        _ = Refresh();
    }

    private async Task Refresh()
    {
        var names = (await Scanner.ListProfilesAsync(_ctx)).ToList();
        ProfileList.ItemsSource = names;
        EmptyText.Visibility = names.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name)) { StatusText.Text = "Enter a name first."; return; }
        try
        {
            await Scanner.SaveProfileAsync(name, _ctx);
            NameBox.Text = "";
            StatusText.Text = $"Saved '{name}'.";
            await Refresh();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void OnLoad(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not string name) return;
        try
        {
            // Route through the VM so the ban-risk gate runs once before any mod gets enabled.
            // On cancel (un-acked high-risk game), the profile is not applied and nothing is enabled.
            if (!await _vm.LoadProfileAsync(name)) { StatusText.Text = "Load cancelled."; return; }
            Changed = true;
            StatusText.Text = $"Loaded '{name}'.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not string name) return;
        try
        {
            await Scanner.DeleteProfileAsync(name, _ctx);
            StatusText.Text = $"Deleted '{name}'.";
            await Refresh();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
}
