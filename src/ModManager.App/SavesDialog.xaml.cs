using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.Services;
using ModManager.Core;
using Windows.Storage.Pickers;

namespace ModManager.App;

/// <summary>A snapshot row prepared for display (title + "time · size").</summary>
public sealed record SaveRow(SaveSnapshot Snap, string Title, string Detail);

/// <summary>One "clone to" choice for a save file: the target type's label + extension.</summary>
public sealed record SaveCloneTarget(string TypeLabel, string Ext);

/// <summary>A save-file row: its name + type, and the other types it can be cloned to.</summary>
public sealed record SaveFileRow(string Name, string TypeLabel, IReadOnlyList<SaveCloneTarget> Targets);

/// <summary>One installed-save-mod row: friendly title + when/source detail.</summary>
public sealed record SaveModRow(SaveModEntry Entry, string Title, string Detail);

/// <summary>One character-row for the editor. Bridges the Core CharacterSlot to the
/// data-template's two-line display.</summary>
public sealed record CharacterRow(
    string SavePath, ModManager.Core.SaveEditor.FromSoft.CharacterSlot Slot,
    string Headline, string Detail);

public sealed partial class SavesDialog : ContentDialog
{
    private readonly LauncherService _svc;
    private readonly IntPtr _hwnd;
    private readonly string _gameId;
    private readonly string _savesDir;
    private readonly string _dataDir;
    private readonly IReadOnlyList<SaveType> _saveTypes;
    private readonly string? _saveModPath;
    private readonly IReadOnlyList<string>? _saveModForbidden;
    private string? _saveDir;
    private bool _loaded; // suppress persist during initial control setup

    public SavesDialog(GameContext ctx, LauncherService svc, IntPtr hwnd)
    {
        InitializeComponent();
        _svc = svc;
        _hwnd = hwnd;
        _gameId = ctx.Game.Id;
        _savesDir = ctx.SavesDir;
        _dataDir = ctx.DataDir;
        _saveDir = ctx.SaveDir; // detection (Ludusavi-first) is done by the caller before opening
        _saveTypes = GameProfiles.Resolve(ctx.Game.Engine, ctx.Game.SteamAppId).SaveTypes;
        _saveModPath = ctx.Game.SaveModPath;
        _saveModForbidden = ctx.Game.SaveModForbidden;
        AutoBackupCheck.IsChecked = ctx.Game.AutoBackupOnLaunch;
        KeepBox.Value = ctx.Game.SaveAutoKeep ?? 25;
        if (!string.IsNullOrEmpty(_saveDir)) StatusText.Text = "Save folder ready.";
        FolderBox.Text = _saveDir ?? "";
        Refresh();
        RefreshSaveFiles();
        RefreshSaveMods();
        RefreshCharacters();
        _loaded = true;
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
                          .Select(t => new SaveCloneTarget(t.Label, t.Extension)).ToList()))
            .ToList();
        SaveFileList.ItemsSource = rows;
        SaveFilesEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCloneMenuOpening(object sender, object e)
    {
        if (sender is not MenuFlyout menu || menu.Target?.DataContext is not SaveFileRow row) return;
        menu.Items.Clear();
        var baseName = System.IO.Path.GetFileNameWithoutExtension(row.Name);
        foreach (var t in row.Targets)
        {
            // If the target type already exists, the action becomes a gated "Replace" that snapshots
            // first — clearly labeled, reversible. Otherwise it's a plain clone.
            var exists = !string.IsNullOrEmpty(_saveDir) && File.Exists(System.IO.Path.Combine(_saveDir, baseName + t.Ext));
            var item = new MenuFlyoutItem
            {
                Text = exists ? $"Replace {t.TypeLabel} (snapshots first)" : $"Clone to {t.TypeLabel}",
                Tag = (row.Name, t.Ext, exists),
            };
            item.Click += OnCloneTo;
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuFlyoutItem { Text = "No other save types", IsEnabled = false });
    }

    private void OnCloneTo(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: ValueTuple<string, string, bool> t }) return;
        var (name, ext, replace) = t;
        if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }
        try
        {
            if (replace)
            {
                SaveManager.Backup(_saveDir, _savesDir, "before-clone", auto: true); // reversible
                var created = SaveManager.CloneToType(_saveDir, name, ext, overwrite: true);
                StatusText.Text = $"Snapshotted, then replaced → {created}.";
                Refresh();
            }
            else
            {
                var created = SaveManager.CloneToType(_saveDir, name, ext);
                StatusText.Text = $"Cloned {name} → {created}. Your original is untouched.";
            }
            RefreshSaveFiles();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void RefreshSaveMods()
    {
        var rows = SaveModStore.Load(_dataDir)
            .Select(e => new SaveModRow(e, e.Name,
                $"{e.InstalledUtc.ToLocalTime():g}  ·  world {Short(e.Guid)}  ·  {System.IO.Path.GetFileName(e.SourceZip)}"))
            .OrderByDescending(r => r.Entry.InstalledUtc)
            .ToList();
        SaveModList.ItemsSource = rows;
        SaveModEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshCharacters()
    {
        var rows = new List<CharacterRow>();
        if (!string.IsNullOrEmpty(_saveDir))
        {
            var svc = App.AppHost.Services
                .GetRequiredService<ModManager.App.Services.SaveEditorService>();
            // Scan every save-type extension this game declares. For ER that's .sl2 (Vanilla),
            // .co2 (Seamless Co-op), .err (Reforged). Same BND4 internal shape across all three;
            // SaveType.Label flows into the Character row so the user can tell at a glance which
            // file the character lives in (Seamless players write to .co2, never .sl2).
            foreach (var st in _saveTypes)
            {
                foreach (var savePath in System.IO.Directory.GetFiles(_saveDir, "*" + st.Extension))
                {
                    IReadOnlyList<ModManager.Core.SaveEditor.FromSoft.CharacterSlot> slots;
                    try { slots = svc.ReadCharacters(savePath); }
                    catch { continue; }   // any parse failure — skip the file, don't fail the dialog
                    foreach (var slot in slots)
                    {
                        rows.Add(new CharacterRow(
                            SavePath: savePath,
                            Slot: slot,
                            Headline: $"{slot.Name}  ·  {st.Label}",
                            Detail: $"Lv {slot.Level}  ·  {slot.Runes:N0} runes  ·  {(string.IsNullOrEmpty(slot.Class) ? "—" : slot.Class)}"));
                    }
                }
            }
        }
        CharacterList.ItemsSource = rows;
        CharactersEmpty.Visibility = rows.Count == 0
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
        EditorCredit.Text = "Save format support based on community reverse-engineering — see Settings → About for credits.";
    }

    private async void OnEditCharacter(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.FrameworkElement fe || fe.DataContext is not CharacterRow row) return;

        // WinUI 3 only allows one ContentDialog at a time per XamlRoot. SavesDialog is itself a
        // ContentDialog, so opening CharacterEditDialog directly on top throws
        // InvalidOperationException("Only one ContentDialog can be open at a time."). The pattern:
        // hide this dialog → open the editor → re-show this dialog with refreshed lists. Hide()
        // makes the outer ShowAsync return None; MainWindow.OnSaves doesn't act on the result.
        var xamlRoot = this.XamlRoot;
        var slot = row.Slot;
        var savePath = row.SavePath;
        this.Hide();

        var dialog = new CharacterEditDialog(slot) { XamlRoot = xamlRoot };
        Microsoft.UI.Xaml.Controls.ContentDialogResult result;
        string? statusAfter = null;
        try { result = await dialog.ShowAsync(); }
        catch (Exception ex)
        {
            // Surface what actually went wrong — name the type so we can chase the root cause
            // if it's something other than the nested-dialog rule.
            statusAfter = $"Couldn't open editor ({ex.GetType().Name}): {ex.Message}";
            result = Microsoft.UI.Xaml.Controls.ContentDialogResult.None;
        }

        if (statusAfter is null && result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
        {
            if (!dialog.IsValid())
            {
                statusAfter = "Name must be 1–16 characters. Edit was NOT applied.";
            }
            else
            {
                var edit = dialog.GetEdit();
                var svc = App.AppHost.Services.GetRequiredService<ModManager.App.Services.SaveEditorService>();
                try
                {
                    var snap = svc.EditCharacter(
                        saveDir: _saveDir!, snapshotsDir: _savesDir, savePath: savePath,
                        slotIndex: slot.SlotIndex, beforeEdit: slot, edit: edit);
                    statusAfter = $"Edited \"{slot.Name}\" → \"{edit.Name}\". Snapshot taken: {snap.Label}.";
                }
                catch (Exception ex) { statusAfter = ex.Message; }
            }
        }

        // Re-show SavesDialog with the new snapshot / character state and the status message.
        Refresh();
        RefreshCharacters();
        if (statusAfter is not null) StatusText.Text = statusAfter;
        try { await this.ShowAsync(); }
        catch { /* re-show race — the user can re-open Saves from the More menu */ }
    }

    private void OnSaveModReset(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SaveModRow row) return;
        if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }
        try
        {
            SaveModInstaller.ResetWorld(_saveDir, _savesDir, row.Entry.SourceZip,
                row.Entry.Guid, _saveModPath, _saveModForbidden);
            StatusText.Text = $"Reset {row.Entry.Name} — previous state snapshotted first.";
            Refresh();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void OnSaveModRemove(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SaveModRow row) return;
        if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }
        try
        {
            SaveModInstaller.RemoveWorld(_saveDir, _savesDir, row.Entry.Guid,
                _saveModPath, _saveModForbidden);
            SaveModStore.Remove(_dataDir, row.Entry.Guid);
            StatusText.Text = $"Removed {row.Entry.Name} — previous state snapshotted first.";
            Refresh();
            RefreshSaveMods();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private static string Short(string g) => g.Length <= 8 ? g : g[..8] + "…";

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

    private void OnAutoBackupChanged(object sender, RoutedEventArgs e) => PersistAutoBackup();
    private void OnKeepChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => PersistAutoBackup();

    private void PersistAutoBackup()
    {
        if (!_loaded) return;
        var keep = double.IsNaN(KeepBox.Value) ? 25 : (int)Math.Clamp(KeepBox.Value, 1, 999);
        _svc.SetAutoBackup(_gameId, AutoBackupCheck.IsChecked == true, keep);
    }

    // Open the save folder in Explorer. Quiet glyph next to Change… — Este asked for "go to save
    // folder right near where they link the save folder." Errors are swallowed: missing path /
    // shell failure isn't worth a toast (the user can re-set via Change…).
    private void OnOpenSaveFolder(object sender, RoutedEventArgs e)
    {
        var path = FolderBox.Text;
        if (string.IsNullOrEmpty(path)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* path gone / shell unavailable — silent */ }
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
