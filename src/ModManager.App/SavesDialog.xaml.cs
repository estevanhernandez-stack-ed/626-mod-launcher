using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.Services;
using ModManager.Core;
using Windows.Storage.Pickers;

namespace ModManager.App;

/// <summary>A snapshot row prepared for display (title + "time · size").</summary>
public sealed record SaveRow(SaveSnapshot Snap, string Title, string Detail);

/// <summary>One "clone to" choice for a save file: the menu label + the target extension.</summary>
public sealed record SaveCloneTarget(string Label, string Ext);

/// <summary>A save-file row: its name + type, and the other types it can be cloned to.</summary>
public sealed record SaveFileRow(string Name, string TypeLabel, IReadOnlyList<SaveCloneTarget> Targets);

public sealed partial class SavesDialog : ContentDialog
{
    private readonly LauncherService _svc;
    private readonly IntPtr _hwnd;
    private readonly string _gameId;
    private readonly string _savesDir;
    private readonly IReadOnlyList<SaveType> _saveTypes;
    private string? _saveDir;

    public SavesDialog(GameContext ctx, LauncherService svc, IntPtr hwnd)
    {
        InitializeComponent();
        _svc = svc;
        _hwnd = hwnd;
        _gameId = ctx.Game.Id;
        _savesDir = ctx.SavesDir;
        _saveDir = ctx.SaveDir; // detection (Ludusavi-first) is done by the caller before opening
        _saveTypes = GameProfiles.Resolve(ctx.Game.Engine, ctx.Game.SteamAppId).SaveTypes;
        AutoBackupCheck.IsChecked = ctx.Game.AutoBackupOnLaunch;
        if (!string.IsNullOrEmpty(_saveDir)) StatusText.Text = "Save folder ready.";
        FolderBox.Text = _saveDir ?? "";
        Refresh();
        RefreshSaveFiles();
    }

    private void Refresh()
    {
        var rows = SaveManager.ListSnapshots(_savesDir)
            .Select(s => new SaveRow(s,
                (s.IsAuto ? "auto · " : "") + (s.Label.Length > 0 ? s.Label : "(unlabeled)"),
                $"{s.TakenUtc.ToLocalTime():g}  ·  {Human(s.SizeBytes)}"))
            .ToList();
        SnapshotList.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BackupButton.IsEnabled = !string.IsNullOrEmpty(_saveDir);
    }

    // Save files in the folder, each with a "Clone to…" menu of the game's other declared save types.
    private void RefreshSaveFiles()
    {
        var rows = (string.IsNullOrEmpty(_saveDir) ? Array.Empty<SaveFile>() : SaveManager.ListSaveFiles(_saveDir, _saveTypes))
            .Select(f => new SaveFileRow(f.Name, f.TypeLabel,
                _saveTypes.Where(t => !string.Equals(t.Extension, f.Extension, StringComparison.OrdinalIgnoreCase))
                          .Select(t => new SaveCloneTarget("Clone to " + t.Label, t.Extension)).ToList()))
            .ToList();
        SaveFileList.ItemsSource = rows;
        SaveFilesEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCloneMenuOpening(object sender, object e)
    {
        if (sender is not MenuFlyout menu || menu.Target?.DataContext is not SaveFileRow row) return;
        menu.Items.Clear();
        foreach (var t in row.Targets)
        {
            var item = new MenuFlyoutItem { Text = t.Label, Tag = (row.Name, t.Ext) };
            item.Click += OnCloneTo;
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuFlyoutItem { Text = "No other save types", IsEnabled = false });
    }

    private void OnCloneTo(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: ValueTuple<string, string> pair }) return;
        if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }
        try
        {
            var created = SaveManager.CloneToType(_saveDir, pair.Item1, pair.Item2);
            StatusText.Text = $"Cloned {pair.Item1} → {created}. Your original is untouched.";
            RefreshSaveFiles();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; } // e.g. "a Reforged save already exists…"
    }

    private void OnRestoreTypeOpening(object sender, object e)
    {
        if (sender is not MenuFlyout menu || menu.Target?.DataContext is not SaveRow row) return;
        menu.Items.Clear();
        foreach (var t in SaveManager.TypesInSnapshot(row.Snap.Path, _saveTypes))
        {
            var item = new MenuFlyoutItem { Text = "Restore only " + t.Label, Tag = (row.Snap.Path, t.Extension) };
            item.Click += OnRestoreType;
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuFlyoutItem { Text = "No typed saves in this snapshot", IsEnabled = false });
    }

    private void OnRestoreType(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: ValueTuple<string, string> pair }) return;
        if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }
        try
        {
            SaveManager.RestoreType(pair.Item1, _saveDir, _savesDir, pair.Item2);
            StatusText.Text = "Restored that save type. Your previous state was snapshotted first.";
            Refresh();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void OnAutoBackupChanged(object sender, RoutedEventArgs e)
        => _svc.SetAutoBackup(_gameId, AutoBackupCheck.IsChecked == true, 25);

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
        RefreshSaveFiles();
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
