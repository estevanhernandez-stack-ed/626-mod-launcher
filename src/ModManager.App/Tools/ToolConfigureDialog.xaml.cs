using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core.Tools;

namespace ModManager.App.Tools;

/// <summary>
/// Right-click target for an installed tool button. Re-scans <see cref="ToolEntry.InstallDir"/> so
/// the user can re-pick the runnable (heuristic install sometimes picks a launcher instead of the
/// real exe), rename the display label, toggle the EditsSaves snapshot opt-in, or uninstall the
/// tool (folder + registry entry both removed). All writes go through <see cref="ToolRegistry.Save"/>
/// so they're atomic; caller is expected to call <see cref="ViewModels.MainViewModel.RefreshAsync"/>
/// after the dialog closes to repaint the tools row.
/// </summary>
public sealed partial class ToolConfigureDialog : ContentDialog
{
    private readonly ToolEntry _original;
    private readonly string _gameDataDir;

    public ToolConfigureDialog(ToolEntry tool, string gameDataDir)
    {
        InitializeComponent();
        _original = tool;
        _gameDataDir = gameDataDir;

        DisplayNameBox.Text = tool.DisplayName;
        EditsSavesBox.IsChecked = tool.EditsSaves;

        // Scan the install dir for executables so the user can re-pick. Relative + forward-slashes
        // so the value lines up with how ToolIntake.Install records the Runnable on disk.
        if (Directory.Exists(tool.InstallDir))
        {
            var runnables = Directory.EnumerateFiles(tool.InstallDir, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext is ".exe" or ".bat" or ".ps1" or ".cmd";
                })
                .Select(f => Path.GetRelativePath(tool.InstallDir, f).Replace('\\', '/'))
                .ToList();
            RunnableBox.ItemsSource = runnables;
            RunnableBox.SelectedItem = tool.Runnable;
        }

        SecondaryButtonClick += OnUninstallClick;
        PrimaryButtonClick += OnSaveClick;
    }

    /// <summary>The new entry the user saved — null when the dialog was cancelled or uninstalled.</summary>
    public ToolEntry? UpdatedEntry { get; private set; }

    /// <summary>True when the user uninstalled — caller should reload tools (the entry is gone).</summary>
    public bool Uninstalled { get; private set; }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        // Shell-execute against the folder path — falls back to whatever explorer.exe does on a
        // missing path. We don't surface the failure: the user already sees the path in the chip
        // and the dialog stays open so they can retry or hit Cancel.
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _original.InstallDir,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Swallow — opening the folder is a convenience, not a contract. No status surface here.
        }
    }

    private void OnUninstallClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try
        {
            if (Directory.Exists(_original.InstallDir))
                Directory.Delete(_original.InstallDir, recursive: true);

            var registry = ToolRegistry.Load(_gameDataDir);
            var remaining = registry.Tools.Where(t => t.ToolId != _original.ToolId).ToList();
            ToolRegistry.Save(_gameDataDir, remaining);

            Uninstalled = true;
        }
        catch
        {
            // Keep the dialog open so the user can retry / open the folder and clear the lock. The
            // caller surfaces nothing from a cancelled close — silent is fine here because retry is
            // the next user action, not a status read.
            args.Cancel = true;
        }
    }

    private void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        try
        {
            UpdatedEntry = _original with
            {
                DisplayName = DisplayNameBox.Text,
                Runnable = RunnableBox.SelectedItem as string ?? _original.Runnable,
                EditsSaves = EditsSavesBox.IsChecked ?? false,
            };

            // Atomic write through ToolRegistry — replaces the old entry by ToolId, preserves any
            // other tools in the file. RefreshAsync on the VM picks the new shape up.
            var registry = ToolRegistry.Load(_gameDataDir);
            var updated = registry.Tools
                .Select(t => t.ToolId == _original.ToolId ? UpdatedEntry : t)
                .ToList();
            ToolRegistry.Save(_gameDataDir, updated);
        }
        catch
        {
            // Same retry stance as Uninstall — keep the dialog open if the save throws (e.g. the
            // file's been hand-edited into something unreadable). No status surface; the user can
            // hit Cancel or try again.
            args.Cancel = true;
        }
    }
}
