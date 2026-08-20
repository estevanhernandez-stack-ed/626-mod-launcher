using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.Services;
using ModManager.Core;
using ModManager.Core.Transport;
using Windows.Storage.Pickers;

namespace ModManager.App;

/// <summary>A snapshot row prepared for display (title + "time · size").</summary>
public sealed record SaveRow(SaveSnapshot Snap, string Title, string Detail);

/// <summary>One "clone to" choice for a save file: the target type's label + extension.</summary>
public sealed record SaveCloneTarget(string TypeLabel, string Ext);

/// <summary>One world in the saves panel. The folder name is a GUID, so the row leads with the name
/// the GAME has for this world - read straight out of its own save - and puts the identifying facts
/// underneath: when it was last played, how many files, and what it costs on disk. A world with no
/// readable name falls back to the user's label and then to an ordinal.
/// See docs/superpowers/specs/2026-08-19-the-world-name-is-readable-design.md.</summary>
/// <param name="NameBudgetBytes">How many BYTES a rename may occupy, 0 when the save has no name to
/// change. Not characters.</param>
public sealed record SaveWorldRow(string Id, string Title, string Kind, string Detail, string Size,
                                  int BackupCount, int NameBudgetBytes, bool HasOwnSave)
{
    public Visibility HasBackupsVisibility => BackupCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Whether a rename can reach the save, or only our own label.</summary>
    public bool CanRenameInGame => NameBudgetBytes > 0;

    /// <summary>
    /// Why a rename cannot reach the save - the two reasons need different sentences.
    ///
    /// <para><b>No save of its own:</b> a world somebody else hosts keeps only LocalData.sav. There has
    /// never been a name in it.</para>
    ///
    /// <para><b>A save we can no longer read the name out of:</b> Palworld re-saved the world and the
    /// codec compressed a padded name into a back-reference. The world still shows the right name
    /// in-game - we just cannot change it from here any more.</para>
    /// </summary>
    public string WhyNotInGame => HasOwnSave
        ? "Palworld has re-saved this world since it was last named, and its own copy of the name is no "
          + "longer in a form the launcher can change. It still shows correctly in-game. The name you "
          + "type here is the launcher's."
        : "This world has no save of its own - it is hosted on someone else's machine. The name you type "
          + "here is the launcher's, and Palworld will not see it.";

    // FROZEN IDENTITY, per .claude/rules/automation-ids.md. Bound off the world's folder id, which is
    // the one thing here that never changes - not the title (the user renames it at will) and not the
    // noun. "World" is PALWORLD's word; a game that keeps a folder per save calls it a save, and when
    // this panel serves more than one game that noun has to come from the game. A harness pinned to
    // the Name would go red the day that happens, for no behavioural reason at all.
    // No row-level id: the row template's outer element is a Grid, which is not a control-view
    // element and would never reach the tree an agent walks - the same trap as putting one on a
    // Border. The three action buttons carry per-row identity instead, and a walk confirms they
    // surface. Verified: 6 SaveUnit* ids for two worlds.
    public string RenameAutomationId    => $"SaveUnitRename.{Id}";
    public string DuplicateAutomationId => $"SaveUnitDuplicate.{Id}";
    public string BackupAutomationId    => $"SaveUnitBackup.{Id}";
    public string RestoreAutomationId   => $"SaveUnitRestore.{Id}";

    // And the accessibility labels, which are what a screen reader announces and ARE allowed to
    // follow the copy. Different job from the ids above - both belong.
    public string RenameAutomationName    => $"Rename world {Id}";
    public string DuplicateAutomationName => $"Duplicate world {Id}";
    public string BackupAutomationName    => $"Back up world {Id}";
    public string RestoreAutomationName   => $"Restore world {Id}";
}

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
    private readonly GameEntry _game;   // for the running-game gate on anything that writes a save

    /// <summary>The mods this game has installed, supplied by the caller rather than rescanned. A
    /// bundle carries them so the machine at the other end can say what it is missing - the part no
    /// general-purpose save tool can produce.</summary>
    private readonly IReadOnlyList<BundleMod> _mods;
    private readonly string _savesDir;
    private readonly string _dataDir;
    private readonly IReadOnlyList<SaveType> _saveTypes;
    private readonly string? _engine;
    private readonly string? _steamAppId;
    private readonly string? _saveModPath;
    private readonly IReadOnlyList<string>? _saveModForbidden;
    private string? _saveDir;
    private bool _loaded; // suppress persist during initial control setup

    public SavesDialog(GameContext ctx, LauncherService svc, IntPtr hwnd,
                      IReadOnlyList<BundleMod>? mods = null)
    {
        InitializeComponent();
        ModManager.App.Services.DialogTheming.Apply(this); // vibe-glow wave 1: popup-scope theme brushes
        ModManager.App.Services.A11y.WireLiveRegion(StatusText); // vibe-glow wave 5: announce status writes
        _svc = svc;
        _hwnd = hwnd;
        _gameId = ctx.Game.Id;
        _game = ctx.Game;
        _mods = mods ?? Array.Empty<BundleMod>();
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

        var labels = WorldLabels.Load(_dataDir);
        var rows = SaveManager.ListWorlds(_saveDir).Select((w, i) => new SaveWorldRow(
            w.Name,
            labels.Display(w.Name, i + 1, w.GameName),
            w.RoleLabel,
            $"Last played {w.LastWriteUtc.ToLocalTime():yyyy-MM-dd HH:mm}  ·  {w.FileCount} file{(w.FileCount == 1 ? "" : "s")}  ·  {w.Name}"
                + (w.RoleCaveat.Length > 0 ? $"{Environment.NewLine}{w.RoleCaveat}" : ""),
            Human(w.Bytes),
            SaveManager.ListWorldSnapshots(_savesDir, w.Name).Count,
            w.NameBudgetBytes,
            w.HasOwnSave)).ToList();

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

    /// <summary>
    /// Pack this save into one file that can move to another machine.
    ///
    /// <para>Reads only — the save is never modified, which is what makes this safe to sit beside the
    /// destructive operations in the same panel. Secrets are left out by construction: an artifact
    /// meant to leave the machine cannot carry an account token. See
    /// <see cref="ModManager.Core.Transport.CredentialScan"/>.</para>
    /// </summary>
    private async void OnExportBundle(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }
        try
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeChoices.Add("626 save bundle", new List<string> { SaveBundle.Extension });
            picker.SuggestedFileName = $"{_game.Id}-save-{DateTime.Now:yyyyMMdd-HHmm}";
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            StatusText.Text = "Packing…";
            var manifest = await Task.Run(() => SaveBundle.Create(
                _saveDir!, file.Path,
                new BundleGame(_game.Id, _game.SteamAppId, _game.GameName),
                DateTime.UtcNow, BundleScope.Portable, _mods));

            var left = manifest.Excluded.Count == 0
                ? ""
                : $" {manifest.Excluded.Count} sign-in file{(manifest.Excluded.Count == 1 ? " was" : "s were")} left out - "
                  + "those are account tokens, not save data.";
            StatusText.Text =
                $"Packed {manifest.FileCount} file{(manifest.FileCount == 1 ? "" : "s")} ({Human(manifest.Bytes)}) "
                + $"and {_mods.Count} mod{(_mods.Count == 1 ? "" : "s")}.{left}";
        }
        catch (Exception ex) { StatusText.Text = ModManager.Core.ErrorRemedy.Describe(ex); }
    }

    /// <summary>
    /// Bring in a save packed on another machine.
    ///
    /// <para>The manifest is read and shown BEFORE anything is touched — including which of its mods
    /// are missing here, which is the whole reason a mod manager is the right place for this. Nothing
    /// is installed automatically; the list is a statement, not an action.</para>
    /// </summary>
    private async void OnImportBundle(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeFilter.Add(SaveBundle.Extension);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            var manifest = SaveBundle.ReadManifest(file.Path);
            if (manifest is null)
            {
                StatusText.Text = "That file is not a save bundle. Nothing was changed.";
                return;
            }
            ShowImportConfirm(sender as FrameworkElement ?? ImportBundleButton, file.Path, manifest);
        }
        catch (Exception ex) { StatusText.Text = ModManager.Core.ErrorRemedy.Describe(ex); }
    }

    private void ShowImportConfirm(FrameworkElement anchor, string bundlePath, SaveBundleManifest manifest)
    {
        var res = Application.Current.Resources;
        var panel = new StackPanel { Spacing = 8, MaxWidth = 400 };

        panel.Children.Add(new TextBlock
        {
            Text = "Replace this game's saves?",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        DateTime.TryParse(manifest.CreatedUtc, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var made);
        panel.Children.Add(new TextBlock
        {
            Text = $"{manifest.FileCount} file{(manifest.FileCount == 1 ? "" : "s")} ({Human(manifest.Bytes)})"
                 + (made == default ? "" : $", packed {made.ToLocalTime():yyyy-MM-dd HH:mm}")
                 + ". Everything in your save folder is replaced, and your current save is snapshotted "
                 + "as 'before-restore' first.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)res["ThemeInkDim"],
        });

        // The reason a mod manager is the right place for this. A save built on mods you do not have
        // will not behave, and nothing else in the chain knows WHICH they are.
        AddMissingModsSection(panel, manifest, res);

        var go = new Button
        {
            Content = "Replace my saves",
            Background = (Microsoft.UI.Xaml.Media.Brush)res["ThemeDanger"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)res["ThemeBg"],
        };
        // Filled danger has to survive the visual states - see .claude/rules/vsm-danger-buttons.md.
        go.Resources["ButtonBackgroundPointerOver"] = res["ThemeDanger"];
        go.Resources["ButtonBackgroundPressed"] = res["ThemeDanger"];
        go.Resources["ButtonForegroundPointerOver"] = res["ThemeBg"];
        go.Resources["ButtonForegroundPressed"] = res["ThemeBg"];
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(go, "SaveBundleImportConfirmButton");
        panel.Children.Add(go);

        var flyout = new Flyout { Content = panel };
        go.Click += (_, _) =>
        {
            flyout.Hide();
            if (GameIsRunning())
            {
                StatusText.Text = $"Close {_game.GameName} first - it would overwrite this on exit.";
                return;
            }
            try
            {
                SaveBundle.Restore(bundlePath, _saveDir!, _savesDir, _game.Id);
                StatusText.Text = "Save brought in. Your previous save was snapshotted as 'before-restore' first.";
                Refresh();
                RefreshSaveFiles();
                RefreshWorlds();
            }
            catch (Exception ex) { StatusText.Text = ModManager.Core.ErrorRemedy.Describe(ex); }
        };
        flyout.ShowAt(anchor);
    }

    /// <summary>
    /// Say which of a bundle's mods are missing here, and offer somewhere to get them.
    ///
    /// <para><b>Naming them is not enough.</b> A list of names the user then has to go and search for
    /// one by one is the same dead end the NEEDS ___ chip used to be: it states a problem and offers
    /// nothing. Each mod the bundle recorded an id for becomes a link.</para>
    ///
    /// <para><b>The link is built here, never taken from the bundle.</b> A bundle arrives from another
    /// person and is untrusted input; a URL inside it is a destination somebody else chose, and a
    /// phishing page under a real mod's name - rendered by an app the user trusts, right next to that
    /// mod's name - is the obvious attack. <see cref="SaveBundle.NexusUrlFor"/> builds it from the
    /// numeric id and the domain WE resolve for this game, so a stranger can name a mod but can never
    /// choose where the user is sent.</para>
    ///
    /// <para>Nothing installs itself. This is a statement with a door beside it, not an action.</para>
    /// </summary>
    private void AddMissingModsSection(StackPanel panel, SaveBundleManifest manifest, ResourceDictionary res)
    {
        if (manifest.Mods.Count == 0) return;

        var missing = SaveBundle.MissingMods(manifest, _mods.Select(m => m.Name).ToList());
        var total = manifest.Mods.Count;

        if (missing.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Built with {total} mod{(total == 1 ? "" : "s")}, and you have all of them.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)res["ThemeInkDim"],
            });
            return;
        }

        var heading = new TextBlock
        {
            Text = $"Built with {total} mod{(total == 1 ? "" : "s")}. {missing.Count} "
                 + $"{(missing.Count == 1 ? "is" : "are")} not installed here - the save will still "
                 + "load, but parts of it may not behave until you add them.",
            TextWrapping = TextWrapping.Wrap,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(heading, "SaveBundleMissingMods");
        panel.Children.Add(heading);

        var domain = NexusDomains.Effective(_game);
        var list = new StackPanel { Spacing = 2, Margin = new Thickness(0, 2, 0, 0) };

        // Capped, because a bundle from a 194-mod Cyberpunk save would otherwise be a wall inside a
        // flyout. The count above is always the whole truth.
        const int Shown = 8;
        foreach (var mod in missing.Take(Shown))
        {
            var url = SaveBundle.NexusUrlFor(mod, domain);
            var label = mod.Name + (string.IsNullOrWhiteSpace(mod.Version) ? "" : $"  ({mod.Version})");

            if (url is not null && SafeUrl.IsHttpUrl(url))
            {
                var link = new HyperlinkButton
                {
                    Content = label,
                    NavigateUri = new Uri(url),
                    Padding = new Thickness(0),
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(link, $"MissingMod.{mod.Name}");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(link, $"Get {mod.Name} on Nexus");
                list.Children.Add(link);
            }
            else
            {
                // No id recorded, or this game has no Nexus domain. Name it without inventing a
                // destination - a link that goes somewhere plausible-but-wrong is worse than none.
                var tb = new TextBlock
                {
                    Text = label,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)res["ThemeInkSoft"],
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(tb, $"MissingMod.{mod.Name}");
                list.Children.Add(tb);
            }
        }

        if (missing.Count > Shown)
            list.Children.Add(new TextBlock
            {
                Text = $"and {missing.Count - Shown} more.",
                Foreground = (Microsoft.UI.Xaml.Media.Brush)res["ThemeInkDim"],
            });

        panel.Children.Add(list);
    }

    /// <summary>
    /// Refuse anything that writes a save while the game is running.
    ///
    /// <para>Found the hard way: a world folder deleted while Palworld was open <b>came back on
    /// exit</b> - the game holds a loaded world in memory and flushes it. An operation that silently
    /// undoes itself is worse than one that refuses, because the user reports "it didn't work" and
    /// there is nothing in the logs to see.</para>
    ///
    /// <para>Fails CLOSED: if the probe cannot enumerate processes it throws, and we treat that as
    /// running rather than assume the coast is clear.</para>
    /// </summary>
    private bool GameIsRunning()
    {
        try { return new GameProcessProbe().AnyRunning(_game); }
        catch { return true; }
    }

    /// <summary>Live byte counter shared by the rename and duplicate flyouts. Counts BYTES because
    /// that is what the save measures - a five-character accented name costs ten.</summary>
    private static void WireBudget(TextBox box, TextBlock counter, Button action, int budgetBytes)
    {
        var res = Application.Current.Resources;
        void Recount()
        {
            var typed = box.Text.Trim();
            if (budgetBytes <= 0) { counter.Text = ""; action.IsEnabled = typed.Length > 0; return; }
            var used = PalworldWorldName.ByteLength(typed);
            var over = used > budgetBytes;
            counter.Text = $"{used} of {budgetBytes} bytes" + (over ? " - too long for the save" : "");
            counter.Foreground = (Microsoft.UI.Xaml.Media.Brush)res[over ? "ThemeDanger" : "ThemeInkDim"];
            action.IsEnabled = !over && typed.Length > 0;
        }
        box.TextChanged += (_, _) => Recount();
        Recount();
    }

    /// <summary>
    /// Rename a world - the name Palworld itself shows, when the save has room for it.
    ///
    /// <para>A Flyout rather than a dialog for the same reason the confirms are: this panel IS a
    /// ContentDialog and cannot host another one.</para>
    ///
    /// <para>The budget is stated before anything is typed. Discovering a limit by being refused is
    /// the failure this app keeps designing away from.</para>
    /// </summary>
    private void OnRenameWorld(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not SaveWorldRow row) return;
        if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }

        var res = Application.Current.Resources;
        var box = new TextBox { PlaceholderText = "e.g. Ridgeline Base", Text = row.Title, MinWidth = 260 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "WorldNameBox");

        var counter = new TextBlock { FontSize = 12 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(counter, "WorldNameBudget");

        var save = new Button { Content = "Save" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(save, "WorldNameSaveButton");

        var panel = new StackPanel { Spacing = 8, MaxWidth = 320 };
        panel.Children.Add(new TextBlock { Text = "Name this world", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = row.CanRenameInGame
                ? "This is the name Palworld shows. The save keeps it in a fixed space, so a new one has "
                  + "to fit - and Palworld's own settings screen will not let you change it at all. A "
                  + "name that fills the space exactly is the one that stays changeable."
                : row.WhyNotInGame,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)res["ThemeInkDim"],
        });
        panel.Children.Add(box);
        panel.Children.Add(counter);
        panel.Children.Add(save);

        var flyout = new Flyout { Content = panel };
        save.Click += (_, _) =>
        {
            var name = box.Text.Trim();
            flyout.Hide();
            try
            {
                if (row.CanRenameInGame)
                {
                    if (GameIsRunning())
                    {
                        StatusText.Text = "Close Palworld first - it holds the world open and would undo this on exit.";
                        return;
                    }
                    PalworldWorldName.Write(System.IO.Path.Combine(_saveDir!, row.Id), name,
                                            SaveManager.WorldSnapshotsDir(_savesDir, row.Id));
                    // The label is written too, NOT cleared. A name that does not fill its budget can
                    // stop being readable the next time Palworld saves - the codec compresses the
                    // padding away - and the panel must not lose the name at that point. The label is
                    // the durable record; the bytes in the save are what Palworld reads.
                    WorldLabels.Save(_dataDir, WorldLabels.Load(_dataDir).With(row.Id, name));
                    StatusText.Text = $"Renamed to \"{name}\". Palworld shows this too.";
                }
                else
                {
                    WorldLabels.Save(_dataDir, WorldLabels.Load(_dataDir).With(row.Id, name));
                    StatusText.Text = $"Named it \"{name}\" here. Palworld will not see this one.";
                }
                RefreshWorlds();
            }
            catch (Exception ex) { StatusText.Text = ModManager.Core.ErrorRemedy.Describe(ex); }
        };
        WireBudget(box, counter, save, row.NameBudgetBytes);
        flyout.ShowAt(fe);
    }

    /// <summary>
    /// Copy a world so there is somewhere safe to experiment.
    ///
    /// <para>Not confirmed - it creates, and destroys nothing. It does insist on a name, because a
    /// copy that cannot be told apart from its original is the exact reason this feature was built
    /// once, tested on the real game, and thrown away.</para>
    /// </summary>
    private void OnDuplicateWorld(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not SaveWorldRow row) return;
        if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }

        var res = Application.Current.Resources;
        var suggested = row.CanRenameInGame
            ? TruncateToBytes($"Copy of {row.Title}", row.NameBudgetBytes)
            : $"Copy of {row.Title}";

        var box = new TextBox { Text = suggested, MinWidth = 260 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "DuplicateWorldNameBox");

        var counter = new TextBlock { FontSize = 12 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(counter, "DuplicateWorldBudget");

        var go = new Button { Content = "Duplicate" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(go, "DuplicateWorldConfirmButton");

        var panel = new StackPanel { Spacing = 8, MaxWidth = 340 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Duplicate {row.Title}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = row.CanRenameInGame
                ? "The copy is a whole separate world you can play or wreck without touching this one. "
                  + "Give it a different name now - Palworld shows both, and two worlds under one name "
                  + "is how people delete the wrong thing."
                : "The copy is a separate world. " + row.WhyNotInGame,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)res["ThemeInkDim"],
        });
        panel.Children.Add(box);
        panel.Children.Add(counter);
        panel.Children.Add(go);

        var flyout = new Flyout { Content = panel };
        go.Click += (_, _) =>
        {
            var name = box.Text.Trim();
            flyout.Hide();
            if (GameIsRunning())
            {
                StatusText.Text = "Close Palworld first - it would undo the new world on exit.";
                return;
            }
            try
            {
                var id = SaveManager.DuplicateWorld(_saveDir!, row.Id, row.CanRenameInGame ? name : null);
                // The label goes in either way, for the same reason a rename writes one: a name that
                // does not fill its budget can stop being readable the next time Palworld saves, and
                // the copy must not lose the name that is the entire point of making it.
                if (name.Length > 0)
                    WorldLabels.Save(_dataDir, WorldLabels.Load(_dataDir).With(id, name));
                StatusText.Text = $"Duplicated. \"{name}\" is a separate world now - {row.Title} is untouched.";
                RefreshWorlds();
            }
            catch (Exception ex) { StatusText.Text = ModManager.Core.ErrorRemedy.Describe(ex); }
        };
        WireBudget(box, counter, go, row.NameBudgetBytes);
        flyout.ShowAt(fe);
    }

    /// <summary>Trim a suggested name to a BYTE budget without splitting a character in half.</summary>
    private static string TruncateToBytes(string s, int budgetBytes)
    {
        while (s.Length > 0 && PalworldWorldName.ByteLength(s) > budgetBytes) s = s[..^1];
        return s.TrimEnd();
    }

    // Back up one world. Not confirmed: it creates a snapshot and destroys nothing, which is the same
    // reason Back up now is not confirmed.
    private void OnBackupWorld(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not SaveWorldRow row) return;
        if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }
        try
        {
            var snap = SaveManager.BackupWorld(_saveDir, row.Id, _savesDir, row.Title);
            StatusText.Text = $"Backed up {row.Title} — {Human(snap.SizeBytes)}.";
            RefreshWorlds();
        }
        catch (Exception ex) { StatusText.Text = ModManager.Core.ErrorRemedy.Describe(ex); }
    }

    /// <summary>
    /// Restore one world, from that world's own snapshots.
    ///
    /// <para>The flyout IS the confirmation. It states the consequence once at the top, then lists
    /// this world's snapshots as danger-filled buttons — picking a specific dated snapshot out of a
    /// labelled list is already a deliberate act, and a second confirm on top would be the
    /// click-through training the confirming spec warns about.</para>
    /// </summary>
    private void OnRestoreWorld(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not SaveWorldRow row) return;
        if (string.IsNullOrEmpty(_saveDir)) { StatusText.Text = "Set a save folder first."; return; }

        var snaps = SaveManager.ListWorldSnapshots(_savesDir, row.Id);
        var panel = new StackPanel { Spacing = 8, MaxWidth = 380 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Replace {row.Title}?",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Everything in this world is replaced. Your other worlds are not touched, and this "
                 + "world is snapshotted as 'before-restore' first.",
            TextWrapping = TextWrapping.Wrap,
        });

        var flyout = new Flyout { Content = panel };
        var res = Application.Current.Resources;
        foreach (var snap in snaps)
        {
            var b = new Button
            {
                Content = $"{snap.TakenUtc.ToLocalTime():yyyy-MM-dd HH:mm}"
                        + (snap.Label.Length > 0 ? $"  ·  {snap.Label}" : "")
                        + $"  ·  {Human(snap.SizeBytes)}",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = (Microsoft.UI.Xaml.Media.Brush)res["ThemeDanger"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)res["ThemeBg"],
            };
            // Filled danger has to survive the visual states - see .claude/rules/vsm-danger-buttons.md.
            b.Resources["ButtonBackgroundPointerOver"] = res["ThemeDanger"];
            b.Resources["ButtonBackgroundPressed"] = res["ThemeDanger"];
            b.Resources["ButtonForegroundPointerOver"] = res["ThemeBg"];
            b.Resources["ButtonForegroundPressed"] = res["ThemeBg"];
            var chosen = snap;
            b.Click += (_, _) => { flyout.Hide(); DoRestoreWorld(row, chosen); };
            panel.Children.Add(b);
        }
        flyout.ShowAt(fe);
    }

    private void DoRestoreWorld(SaveWorldRow row, ModManager.Core.SaveSnapshot snap)
    {
        try
        {
            SaveManager.RestoreWorld(snap.Path, _saveDir!, row.Id, _savesDir);
            StatusText.Text = $"Restored {row.Title}. Its previous state was snapshotted as 'before-restore' first.";
            RefreshWorlds();
        }
        catch (Exception ex) { StatusText.Text = ModManager.Core.ErrorRemedy.Describe(ex); }
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
