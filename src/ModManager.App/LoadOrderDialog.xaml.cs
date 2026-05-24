using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core;

namespace ModManager.App;

public sealed partial class LoadOrderDialog : ContentDialog
{
    private readonly GameContext _ctx;
    private readonly ObservableCollection<string> _order = new();

    public LoadOrderDialog(GameContext ctx)
    {
        InitializeComponent();
        _ctx = ctx;
        OrderList.ItemsSource = _order;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _order.Clear();
        foreach (var key in await Scanner.GetLoadOrderAsync(_ctx)) _order.Add(key);
    }

    private void OnUp(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => MoveSelected(-1);
    private void OnDown(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => MoveSelected(+1);

    private void MoveSelected(int delta)
    {
        var i = OrderList.SelectedIndex;
        if (i < 0) return;
        var j = Math.Clamp(i + delta, 0, _order.Count - 1);
        if (i == j) return;
        var item = _order[i];
        _order.RemoveAt(i);
        _order.Insert(j, item);
        OrderList.SelectedIndex = j;
    }

    private async void OnApply(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            await Scanner.ApplyLoadOrderAsync(_ctx, _order.ToList());
            StatusText.Text = "Applied. Use Reset to restore original names.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void OnReset(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            await Scanner.ResetLoadOrderAsync(_ctx);
            await LoadAsync();
            StatusText.Text = "Reset to original filenames.";
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
}
