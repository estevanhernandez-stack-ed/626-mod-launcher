using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.ViewModels;
using ModManager.Core.Frameworks;
using ModManager.Core.Tools;

namespace ModManager.App.Tools;

/// <summary>
/// Slim tools row that sits above the mod list. Renders three states from the active VM:
///   - <see cref="MainViewModel.Tools"/> as buttons (installed tools, click to launch in Task 9)
///   - <see cref="MainViewModel.MissingTools"/> as HyperlinkButton chips (catalog entries that
///     apply to this game but aren't installed — clicking opens the download page)
///   - An empty-state hint when both collections are empty
/// The + button is always visible so a user can install an ad-hoc tool zip (Task 9 wires the picker).
/// </summary>
public sealed partial class ToolsPanel : UserControl
{
    public ToolsPanel()
    {
        InitializeComponent();
    }

    /// <summary>The active main-window VM. Set by the host (MainWindow) right after the VM is built.
    /// Null until then — bindings on the XAML use <c>x:Bind … Mode=OneWay</c> so empty collections
    /// + collapsed visibility is the harmless default. Notifications for the visibility properties
    /// (HasTools / HasMissingTools / ToolsEmptyHintVisibility / ToolsRowVisible) flow from
    /// MainViewModel — they fire at every <c>ReloadModsAsync</c> tick.</summary>
    public MainViewModel? ViewModel { get; set; }

    // Snapshot-then-launch lives on the VM. The handler reads the tool entry off the button's Tag
    // (Task 8 convention — the slim row uses Tag rather than CommandParameter) and delegates.
    private async void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is ToolEntry entry && ViewModel is not null)
        {
            await ViewModel.LaunchToolAsync(entry);
        }
    }

    // Right-click on an installed tool opens the configure dialog (rename / change runnable /
    // toggle EditsSaves / uninstall). The dialog writes through ToolRegistry.Save itself; we just
    // kick a RefreshAsync after it closes so the slim row repaints with the new shape.
    private async void OnToolRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is ToolEntry entry && ViewModel is not null)
        {
            var dataDir = ViewModel.GameDataDirPublic();
            if (string.IsNullOrEmpty(dataDir)) return;

            var dialog = new ToolConfigureDialog(entry, dataDir) { XamlRoot = this.XamlRoot };
            await dialog.ShowAsync();
            // Refresh tools — the dialog wrote through ToolRegistry.Save (Save or Uninstall path).
            // RefreshAsync is the public wrapper around the VM's private ReloadModsAsync.
            await ViewModel.RefreshAsync();
        }
    }

    // Catalog "Get …" chip: open the Nexus / vendor page in the system browser. Some catalog entries
    // ship with a null/empty GetUrl (pin not landed yet) — guard so the click is a no-op there.
    private async void OnGetToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton btn && btn.Tag is KnownTool known
            && !string.IsNullOrEmpty(known.GetUrl))
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(known.GetUrl));
        }
    }

    // Open a file picker for a tool archive — the drop pipeline (ToolDetector.Classify → ToolIntake)
    // carves tools out of the mod intake, so we reuse AddModsAsync just like the drag-drop path.
    private async void OnAddToolClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            await ViewModel.PromptAddToolAsync();
        }
    }

    // Click an installed-framework button → pop a "how to use" toast read LIVE from the framework's
    // installed settings (real hot-reload key + console state + mods folder for UE4SS). The TeachingTip
    // anchors to the clicked button; light-dismiss closes it.
    private void OnFrameworkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not FrameworkInstallManifest m) return;

        var usage = MainViewModel.FrameworkUsageFor(m);
        UsageTip.Title = $"How to use {usage.DisplayName}";
        UsageTip.Subtitle = string.Join("\n", usage.Lines)
            + (usage.DocsUrl is not null ? $"\n\nDocs: {usage.DocsUrl}" : "");
        UsageTip.Target = el;
        UsageTip.IsOpen = true;
    }

    // Pencil next to a framework button → edit its settings INI in the existing editor (snapshot-first
    // save + restore-previous). Reuses IniEditorDialog as-is — the modId slot becomes the framework id,
    // used only to bucket INI-history backups. The pencil is collapsed when the framework installed no
    // .ini (ConfigVisibility), but we re-check here defensively. Multiple INIs → a quick picker.
    private async void OnEditFrameworkConfigClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not FrameworkInstallManifest m || ViewModel is null) return;

        var inis = FrameworkUsage.ConfigFiles(m);
        if (inis.Count == 0) return;

        string? iniPath;
        if (inis.Count == 1)
        {
            iniPath = inis[0];
        }
        else
        {
            var list = new ListView { ItemsSource = inis, SelectionMode = ListViewSelectionMode.Single };
            var picker = new ContentDialog
            {
                Title = $"Edit which settings file in {m.DisplayName}?",
                Content = list,
                PrimaryButtonText = "Open",
                CloseButtonText = "Cancel",
                IsPrimaryButtonEnabled = false,
                XamlRoot = this.XamlRoot,
            };
            list.SelectionChanged += (_, _) => picker.IsPrimaryButtonEnabled = list.SelectedItem is not null;
            iniPath = await picker.ShowAsync() == ContentDialogResult.Primary ? list.SelectedItem as string : null;
        }
        if (iniPath is null) return;

        var dataDir = ViewModel.GameDataDirPublic();
        if (string.IsNullOrEmpty(dataDir))
        {
            ViewModel.StatusText = "No game data dir available — can't snapshot INI history.";
            return;
        }

        var dialog = new IniEdit.IniEditorDialog(iniPath, dataDir, m.FrameworkId) { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();
        if (dialog.StatusMessage is not null) ViewModel.StatusText = dialog.StatusMessage;
    }
}
