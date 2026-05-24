using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core;

namespace ModManager.App;

public sealed partial class ProfilesDialog : ContentDialog
{
    private readonly GameContext _ctx;

    /// <summary>True if a profile was loaded — the main mod list should refresh on close.</summary>
    public bool Changed { get; private set; }

    public ProfilesDialog(GameContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
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
            await Scanner.LoadProfileAsync(name, _ctx);
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
