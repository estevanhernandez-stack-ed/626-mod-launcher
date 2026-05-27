using System;
using System.IO;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core.IniEdit;

namespace ModManager.App.IniEdit;

/// <summary>
/// Edit one .ini file inside a mod's folder. Save routes through <see cref="IniEditService.SaveWithBackup"/>
/// so the prior contents land in the per-mod history bucket before the new write touches disk
/// (honor the snapshot-first rule from the FromSoft save editor). "Restore previous" pulls the most
/// recent backup back into the text box WITHOUT auto-saving — the user has to hit Save to commit.
/// </summary>
public sealed partial class IniEditorDialog : ContentDialog
{
    private readonly string _iniPath;
    private readonly string _gameDataDir;
    private readonly string _modId;

    public IniEditorDialog(string iniPath, string gameDataDir, string modId)
    {
        InitializeComponent();
        _iniPath = iniPath;
        _gameDataDir = gameDataDir;
        _modId = modId;

        PathLabel.Text = iniPath;
        ContentsBox.Text = File.Exists(iniPath) ? File.ReadAllText(iniPath) : "";

        PrimaryButtonClick += OnSaveClick;
        SecondaryButtonClick += OnRestoreClick;
    }

    /// <summary>Surfaced to the caller (MainWindow code-behind) so it can land the result on the
    /// status bar. Null when nothing happened (user cancelled before saving / restoring).</summary>
    public string? StatusMessage { get; private set; }

    private void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try
        {
            IniEditService.SaveWithBackup(_iniPath, ContentsBox.Text, _gameDataDir, _modId);
            StatusMessage = $"Saved {Path.GetFileName(_iniPath)}. Previous version kept in INI history.";
        }
        catch (Exception ex)
        {
            // Don't dismiss the dialog on failure — the user keeps their typed-in edits.
            args.Cancel = true;
            StatusMessage = $"Couldn't save {Path.GetFileName(_iniPath)}: {ex.Message}";
        }
    }

    private void OnRestoreClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Don't dismiss the dialog — restore-previous loads the backup into the textbox so the
        // user can see/edit it before deciding to Save.
        args.Cancel = true;
        var previous = IniEditService.RestorePrevious(_iniPath, _gameDataDir, _modId);
        if (previous is null)
        {
            StatusMessage = $"No previous version of {Path.GetFileName(_iniPath)} to restore.";
        }
        else
        {
            ContentsBox.Text = previous;
            StatusMessage = $"Loaded previous version of {Path.GetFileName(_iniPath)} — hit Save to commit.";
        }
    }
}
