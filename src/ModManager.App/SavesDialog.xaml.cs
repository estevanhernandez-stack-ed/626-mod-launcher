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

/// <summary>One world in the saves panel. The folder name is a GUID, so the row leads with an ordinal
/// and puts the identifying facts underneath: when it was last played, how many files, and what it
/// costs on disk. Naming worlds properly is a separate decision - reading the name out of the save
/// format means taking on somebody else's reverse-engineering of a format that can change under us.
/// See docs/2026-08-19-saves-are-three-shapes.md.</summary>
public sealed record SaveWorldRow(string Title, string Kind, string Detail, string Size);

/// <summary>A save-file row: its name + type, and the other types it can be cloned to.</summary>
public sealed record SaveFileRow(string Name, string TypeLabel, IReadOnlyList<SaveCloneTarget> Targets)
{
    public string CloneAutomationName => $"Clone {Name} to another type"; // per-item UIA name (F-065)
}

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
    private readonly string? _engine;
    private readonly string? _steamAppId;
    private readonly string? _saveModPath;
    private readonly IReadOnlyList<string>? _saveModForbidden;
    private string? _saveDir;
    private bool _loaded; // suppress persist during initial control setup

    public SavesDialog(GameContext ctx, LauncherService svc, IntPtr hwnd)
    {
        InitializeComponent();
        ModManager.App.Services.DialogTheming.Apply(this); // vibe-glow wave 1: popup-scope theme brushes
        ModManager.App.Services.A11y.WireLiveRegion(StatusText); // vibe-glow wave 5: announce status writes
        _svc = svc;
        _hwnd = hwnd;
        _gameId = ctx.Game.Id;
        _savesDir = ctx.SavesDir;
        _dataDir = ctx.DataDir;
        _saveDir = ctx.SaveDir; // detection (Ludusavi-first) is done by the caller before opening
        _engine = ctx.Game.Engine;
        _steamAppId = ctx.Game.SteamAppId;
        _saveTypes = GameSaveTypesCatalog.Resolve(_engine, _steamAppId).SaveTypes;
        _saveModPath = ctx.Game.SaveModPath;
        _saveModForbidden = ctx.Game.SaveModForbidden;
        AutoBackupCheck.IsChecked = ctx.Game.AutoBackupOnLaunch;
        KeepBox.Value = ctx.Game.SaveAutoKeep ?? 25;
        if (!string.IsNullOrEmpty(_saveDir)) StatusText.Text = "Save folder ready.";
        FolderBox.Text = _saveDir ?? "";
        Refresh();
        RefreshSaveFiles();
        RefreshWorlds();
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
        // Which of the two empty states this is - the app not knowing the game's layout, or the
        // folder being wrong - decides what the user should do next, so the rule lives in Core.
        SaveFilesEmpty.Text = SaveListingEmptyState.MessageFor(
            folderSet: !string.IsNullOrEmpty(_saveDir), declaresTypes: _saveTypes.Count > 0);
        SaveFilesEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Worlds, for a game that keeps a folder per world. Hidden entirely otherwise - an empty "Worlds"
    // heading on Elden Ring would be a new way of saying nothing, which is what this panel was just
    // fixed for.
    private void RefreshWorlds()
    {
        var isWorlds = GameSaveTypesCatalog.Resolve(_engine, _steamAppId).Layout == SaveLayout.Worlds;
        if (!isWorlds || string.IsNullOrEmpty(_saveDir))
        {
            WorldsHeading.Visibility = Visibility.Collapsed;
            WorldList.Visibility = Visibility.Collapsed;
            SaveFilesHeading.Visibility = Visibility.Visible;
            SaveFileList.Visibility = Visibility.Visible;
            return;
        }

        var rows = SaveManager.ListWorlds(_saveDir).Select((w, i) => new SaveWorldRow(
            $"World {i + 1}",
            w.MultiplayerLabel,
            $"Last played {w.LastWriteUtc.ToLocalTime():yyyy-MM-dd HH:mm}  ·  {w.FileCount} file{(w.FileCount == 1 ? "" : "s")}  ·  {w.Name}",
            Human(w.Bytes))).ToList();

        WorldList.ItemsSource = rows;
        WorldsHeading.Visibility = Visibility.Visible;
        WorldList.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // The file list has nothing to say for this shape, so it does not get to say it - and neither
        // does its heading. Leaving "Save files" standing over nothing is the empty-heading problem
        // this panel was just fixed for, reintroduced one section up.
        SaveFilesEmpty.Visibility = Visibility.Collapsed;
        SaveFilesHeading.Visibility = Visibility.Collapsed;
        SaveFileList.Visibility = Visibility.Collapsed;
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
        catch (Exception ex) { StatusText.Text = ModManager.Core.ErrorRemedy.Describe(ex); }
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
        int filesScanned = 0;
        string? firstReadError = null;
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
                    filesScanned++;
                    IReadOnlyList<ModManager.Core.SaveEditor.FromSoft.CharacterSlot> slots;
                    try { slots = svc.ReadCharacters(savePath); }
                    catch (FileNotFoundException) { continue; } // file removed mid-scan — race, not an error worth surfacing
                    catch (Exception ex)
                    {
                        // Capture first failure so the empty-state surfaces a real reason instead of "no characters detected".
                        // Log every failure to Debug for dev-build visibility; keep iterating so partial reads still populate.
                        var fileName = System.IO.Path.GetFileName(savePath);
                        // Debug keeps the CLR type for root-causing; the user-facing copy doesn't (F-063).
                        System.Diagnostics.Debug.WriteLine($"[SavesDialog] ReadCharacters failed: {fileName}: {ex.GetType().Name} — {ex.Message}");
                        firstReadError ??= $"Couldn't read {fileName} — {ex.Message}";
                        continue;
                    }
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
        if (rows.Count == 0)
        {
            // Differentiate the three empty-state causes so the next smoke immediately shows the real failure:
            //  (a) no save folder picked → original message stands
            //  (b) folder picked, files found, every read threw → name the first exception
            //  (c) folder picked, files found, none threw but all slots skipped → "scanned N, no editable slots"
            if (firstReadError is not null)
            {
                CharactersEmpty.Text = $"Couldn't read any saves — first error: {firstReadError}";
            }
            else if (filesScanned > 0)
            {
                CharactersEmpty.Text = $"Scanned {filesScanned} save file(s) — no editable character slots found. "
                    + "Slots are skipped when the MD5 doesn't match or every stat is zero.";
            }
            else
            {
                CharactersEmpty.Text = "No editable characters here. If the folder is right, this game's save format isn't itemized yet.";
            }
            CharactersEmpty.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        else
        {
            CharactersEmpty.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
        // Mirror the first read error into StatusText too — the dialog's main status line is the spot the user reads.
        if (firstReadError is not null) StatusText.Text = $"Save read error: {firstReadError}";
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
            // Debug keeps the CLR type for chasing non-nested-dialog causes; user copy doesn't (F-063).
            System.Diagnostics.Debug.WriteLine($"[SavesDialog] editor open failed: {ex.GetType().Name}: {ex.Message}");
            statusAfter = $"Couldn't open the editor — {ex.Message}";
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
                catch (Exception ex) { statusAfter = ModManager.Core.ErrorRemedy.Describe(ex); }
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
        catch (Exception ex) { StatusText.Text = ModManager.Core.ErrorRemedy.Describe(ex); }
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
        catch (Exception ex) { StatusText.Text = ModManager.Core.ErrorRemedy.Describe(ex); }
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
            RefreshSaveFiles();
            RefreshWorlds();
        }
        catch (Exception ex) { StatusText.Text = ModManager.Core.ErrorRemedy.Describe(ex); }
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
        RefreshWorlds();
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
        catch (Exception ex) { StatusText.Text = ModManager.Core.ErrorRemedy.Describe(ex); }
    }

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SaveRow row) return;
        if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }

        // Reversible, but it replaces EVERYTHING - and the undo is only useful to someone who knows
        // before-restore exists. This is where they find that out.
        ShowConfirm(fe,
            "Replace your saves with this snapshot?",
            $"Everything in the save folder is replaced - {DescribeSaveFolder()}.\n\n"
            + "Your current saves are snapshotted as 'before-restore' first, so this is undoable.",
            "Replace", "ConfirmRestoreButton", () => DoRestore(row));
    }

    private void DoRestore(SaveRow row)
    {
        try
        {
            SaveManager.Restore(row.Snap.Path, _saveDir!, _savesDir);
            StatusText.Text = "Restored. Your previous save was snapshotted as 'before-restore' first.";
            Refresh();
            RefreshSaveFiles();
            RefreshWorlds();
        }
        catch (Exception ex) { StatusText.Text = ModManager.Core.ErrorRemedy.Describe(ex); }
    }

    /// <summary>
    /// A confirmation that works INSIDE this dialog.
    ///
    /// <para>SavesDialog is itself a ContentDialog, and one cannot be shown inside another — which is
    /// why Settings hands its confirms back to MainWindow to run after it closes. That is correct and
    /// heavy: it would close this panel and lose the reader's place in the list they are standing in.
    /// A Flyout is a popup rather than a dialog, so it composes here, and it opens at the pixel the
    /// user aimed at — which is also where the misclick happens.</para>
    /// </summary>
    private void ShowConfirm(FrameworkElement anchor, string title, string body, string confirmLabel,
                             string confirmId, Action act)
    {
        var panel = new StackPanel { Spacing = 10, MaxWidth = 340 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap });

        var confirm = new Button { Content = confirmLabel };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(confirm, confirmId);
        var cancel = new Button { Content = "Cancel" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(cancel, "ConfirmCancelButton");

        // Filled danger, and it has to survive the visual states. A Style that only sets Background
        // wins at rest and loses the moment the pointer arrives, because the stock template
        // re-resolves ButtonBackgroundPointerOver via ThemeResource - the button would read danger
        // until you reached for it, which is exactly backwards. Element-scope the state keys onto the
        // button using the SAME live brush instances ThemeService.Apply mutates, never new ones.
        // See .claude/rules/vsm-danger-buttons.md.
        var res = Application.Current.Resources;
        confirm.Background = (Microsoft.UI.Xaml.Media.Brush)res["ThemeDanger"];
        confirm.Foreground = (Microsoft.UI.Xaml.Media.Brush)res["ThemeBg"];
        confirm.Resources["ButtonBackgroundPointerOver"] = res["ThemeDanger"];
        confirm.Resources["ButtonBackgroundPressed"] = res["ThemeDanger"];
        confirm.Resources["ButtonForegroundPointerOver"] = res["ThemeBg"];
        confirm.Resources["ButtonForegroundPressed"] = res["ThemeBg"];

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(confirm);
        row.Children.Add(cancel);
        panel.Children.Add(row);

        var flyout = new Flyout { Content = panel };
        confirm.Click += (_, _) => { flyout.Hide(); act(); };
        cancel.Click += (_, _) => flyout.Hide();
        flyout.ShowAt(anchor);
    }

    /// <summary>What the save folder holds right now, for a confirm to say out loud. The counts are
    /// gathered here (I/O) and phrased by <see cref="SaveFolderSummary"/> (pure, tested) — a confirm
    /// that misreports what it is about to replace is worse than one that says nothing.</summary>
    private string DescribeSaveFolder()
    {
        if (string.IsNullOrEmpty(_saveDir) || !System.IO.Directory.Exists(_saveDir))
            return "an empty save folder";
        long bytes = 0;
        var files = 0;
        try
        {
            foreach (var f in System.IO.Directory.EnumerateFiles(_saveDir, "*", System.IO.SearchOption.AllDirectories))
            {
                bytes += new System.IO.FileInfo(f).Length;
                files++;
            }
        }
        catch { /* an unreadable folder still gets a sentence rather than a crash */ }
        return SaveFolderSummary.Describe(SaveManager.ListWorlds(_saveDir).Count, files, bytes);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SaveRow row) return;

        // The only irreversible action in this panel, and it destroys the thing that makes the others
        // safe. SaveManager.Delete is File.Delete - no recycle bin, no holding folder. It also sits on
        // the same row as Restore, so the misclick is "I meant to go back to my last good save and
        // instead permanently destroyed it".
        ShowConfirm(fe,
            "Delete this snapshot?",
            $"{row.Title}  -  {row.Detail}\n\n"
            + "This one is gone for good; snapshots are the only copy the launcher keeps.",
            "Delete it", "ConfirmDeleteButton", () => DoDelete(row));
    }

    private void DoDelete(SaveRow row)
    {
        SaveManager.Delete(row.Snap.Path);
        StatusText.Text = $"Deleted {row.Snap.FileName}.";
        Refresh();
    }

    // One formatter, in Core, shared with the confirm copy. This panel briefly had two and they
    // disagreed above a gigabyte.
    private static string Human(long b) => SaveFolderSummary.Human(b);
}
