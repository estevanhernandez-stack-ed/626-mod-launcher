using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.Services;
using ModManager.Core;
using Windows.Storage.Pickers;

namespace ModManager.App;

/// <summary>A snapshot row prepared for display (title + "time · size").</summary>
public sealed record SaveRow(SaveSnapshot Snap, string Title, string Detail);

public sealed partial class SavesDialog : ContentDialog
{
    private readonly LauncherService _svc;
    private readonly IntPtr _hwnd;
    private readonly string _gameId;
    private readonly string _savesDir;
    private string? _saveDir;

    public SavesDialog(GameContext ctx, LauncherService svc, IntPtr hwnd)
    {
        InitializeComponent();
        _svc = svc;
        _hwnd = hwnd;
        _gameId = ctx.Game.Id;
        _savesDir = ctx.SavesDir;
        _saveDir = ctx.SaveDir; // detection (Ludusavi-first) is done by the caller before opening
        if (!string.IsNullOrEmpty(_saveDir)) StatusText.Text = "Save folder ready.";
        FolderBox.Text = _saveDir ?? "";
        Refresh();
    }

    private void Refresh()
    {
        var rows = SaveManager.ListSnapshots(_savesDir)
            .Select(s => new SaveRow(s,
                s.Label.Length > 0 ? s.Label : "(unlabeled)",
                $"{s.TakenUtc.ToLocalTime():g}  ·  {Human(s.SizeBytes)}"))
            .ToList();
        SnapshotList.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BackupButton.IsEnabled = !string.IsNullOrEmpty(_saveDir);
    }

    private async void OnChangeFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;
        _saveDir = folder.Path;
        FolderBox.Text = _saveDir;
        _svc.SetSaveDir(_gameId, _saveDir);
        Refresh();
    }

    private void OnBackup(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }
        try
        {
            var snap = SaveManager.Backup(_saveDir, _savesDir, LabelBox.Text);
            LabelBox.Text = "";
            StatusText.Text = $"Snapshot saved: {snap.FileName}";
            Refresh();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SaveRow row) return;
        if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }
        try
        {
            SaveManager.Restore(row.Snap.Path, _saveDir, _savesDir);
            StatusText.Text = "Restored. Your previous save was snapshotted as 'before-restore' first.";
            Refresh();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SaveRow row) return;
        SaveManager.Delete(row.Snap.Path);
        StatusText.Text = $"Deleted {row.Snap.FileName}.";
        Refresh();
    }

    private static string Human(long b)
        => b < 1024 ? $"{b} B" : b < 1024 * 1024 ? $"{b / 1024.0:0.#} KB" : $"{b / 1024.0 / 1024:0.#} MB";
}
