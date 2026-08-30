using Microsoft.Extensions.DependencyInjection;
using ModManager.Core.Transport;
using Windows.Storage.Pickers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ModManager.App.Services;
using ModManager.App.ViewModels;
using ModManager.Core;
using ModManager.Core.Catalog;
using ModManager.Core.Frameworks;
using ModManager.Core.Plugins;
using ModManager.Core.Tools;
using Windows.Storage.Pickers;
using Windows.UI;

namespace ModManager.App;



/// <summary>One row in the Settings → Restore points list. Detail pre-formats the game names
/// and total size so the XAML template binds a plain string, no converter needed.</summary>
public sealed record RestorePointRow(string Timestamp, string Detail, string Id)
{
    public string RowAutomationId => $"RestorePoint.{Id}";
    public string RestoreAutomationName => "Restore the point from " + Timestamp;
    public string DeleteAutomationName => "Delete the restore point from " + Timestamp;
}


/// <summary>
/// The settings hub. Identity (avatar / derived theme / window transparency) and Nexus Mods
/// account in one place. The Apply button commits the avatar + derived theme changes (gated by
/// the checkboxes); the transparency dropdown applies immediately; the Nexus Connect/Disconnect
/// buttons are also inline so the toolbar dot updates the moment you act here.
/// </summary>
public sealed partial class SettingsDialog : ContentDialog
{
    private readonly IntPtr _hwnd;
    private readonly AvatarService _avatars;
    private readonly ThemeService _themes;
    private readonly AppSettingsService _appSettings;
    private readonly MainViewModel _vm;
    private readonly RestorePointService _rp;
    private bool _suppressBackdropChange = true; // ignore the initial SelectionChanged from seeding

    private string? _pickedSourcePath;
    private IReadOnlyList<PaletteColor> _palette = Array.Empty<PaletteColor>();

    /// <summary>True when the user applied a change that needs to flow back to the main shell
    /// (avatar swap → icon refresh, theme add → dropdown refresh). Nexus + backdrop changes flow
    /// through their own notification paths and don't need this flag.</summary>
    public bool Changed { get; private set; }

    /// <summary>True when the user clicked "Reset launcher…". MainWindow.OnSettings checks this
    /// after ShowAsync() returns and opens SafeClearDialog if set — avoids nesting two
    /// ContentDialogs simultaneously, which is fragile in WinUI 3.</summary>
    public bool OpenSafeClearRequested { get; private set; }

    /// <summary>True when a profile restore actually wrote something. Separate from
    /// <see cref="Changed"/> because that one refreshes the theme list and the title-bar icon, and a
    /// restore needs the MOD LIST re-read instead — the files under it just changed.</summary>
    public bool RestoreHappened { get; private set; }

    /// <summary>Set when the user clicks "Restore" on a restore-point row. MainWindow.OnSettings
    /// reads this after ShowAsync() returns and shows the confirm + performs the restore — same
    /// flag-then-hide pattern used by OpenSafeClearRequested, no nested ContentDialog.</summary>
    public string? RestoreRequestedTimestamp { get; private set; }

    /// <summary>Set when the user clicks "Delete" on a restore-point row. MainWindow.OnSettings
    /// reads this after ShowAsync() returns and shows the confirm + deletes — same pattern.</summary>
    public string? DeleteRequestedTimestamp { get; private set; }

    /// <summary>Set when the user clicks "Connect Nexus account". The OAuth flow opens a browser and (on a
    /// first-ever connect) shows the consent dialog — neither can be nested under this ContentDialog, so we
    /// hand off: MainWindow.OnSettings runs <c>ViewModel.ConnectNexusAsync()</c> after this dialog closes.</summary>
    public bool ConnectNexusRequested { get; private set; }

    /// <summary>
    /// Write one file holding every game's mods, saves and settings.
    ///
    /// <para><b>Reads only.</b> Nothing on this machine is touched, which is what lets this ship
    /// before any restore path exists — and keeps the half that can hurt you out of the first
    /// release.</para>
    ///
    /// <para>Snapshot history is opt-in. On a real 12-game profile it was 446 MB of a 482 MB launcher
    /// data total: backups of backups, and rarely what anyone needs on a new machine.</para>
    /// </summary>
    private async void OnCreateProfileArchive(object sender, RoutedEventArgs e)
    {
        var builder = App.AppHost.Services.GetRequiredService<Services.ProfileArchiveBuilder>();
        try
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeChoices.Add("626 profile archive", new List<string> { ProfileArchive.Extension });
            picker.SuggestedFileName = $"626-profile-{DateTime.Now:yyyyMMdd}";
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            var withHistory = ArchiveIncludeHistoryCheck.IsChecked == true;
            ArchiveCreateButton.IsEnabled = false;
            ArchiveStatusText.Text = "Looking at what you have…";

            // Gathering walks every game's mods and the write copies gigabytes. Both belong off the
            // UI thread, and the per-game callback is what stops a four-minute operation looking hung.
            var progress = new Progress<string>(name => ArchiveStatusText.Text = $"Packing {name}…");
            var version = System.Reflection.Assembly.GetExecutingAssembly()
                                .GetName().Version?.ToString() ?? "0.0.0";

            var manifest = await Task.Run(() =>
            {
                var sources = builder.Gather(n => ((IProgress<string>)progress).Report(n));
                return ProfileArchive.Create(sources, file.Path, DateTime.UtcNow, version, withHistory);
            });

            var left = manifest.Excluded.Count == 0
                ? ""
                : $" {manifest.Excluded.Count} sign-in file{(manifest.Excluded.Count == 1 ? " was" : "s were")} left out.";
            var carries = ModManager.Core.Transport.PersonalDataScan.MessageFor(manifest.Notices);

            ArchiveStatusText.Text =
                $"Backed up {manifest.Games.Count} game{(manifest.Games.Count == 1 ? "" : "s")} - "
                + $"{manifest.TotalFiles:N0} files, {Human(manifest.TotalBytes)}."
                + (withHistory ? "" : " Snapshot history was left out.")
                + left
                + (carries.Length > 0 ? " " + carries : "");
        }
        catch (Exception ex) { ArchiveStatusText.Text = ModManager.Core.ErrorRemedy.Describe(ex); }
        finally { ArchiveCreateButton.IsEnabled = true; }
    }

    /// <summary>
    /// Open a backup, say what is in it, and — once you have read that — put chosen parts of it back.
    ///
    /// <para>The reading came first and shipped on its own, which is why the acting hangs off it here
    /// rather than sitting behind a button of its own. Nothing is written until parts are ticked and
    /// the confirm is pressed; the report is still the whole screen until then.</para>
    ///
    /// <para>A Flyout rather than a dialog for the reason the saves panel found: Settings IS a
    /// ContentDialog and WinUI allows only one at a time per XamlRoot.</para>
    /// </summary>
    private async void OnInspectProfileArchive(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Desktop };
            picker.FileTypeFilter.Add(ProfileArchive.Extension);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            ArchiveStatusText.Text = "Reading…";
            var manifest = await Task.Run(() => ProfileArchive.ReadManifest(file.Path));
            if (manifest is null)
            {
                ArchiveStatusText.Text = "That file is not a 626 backup, or its description could not be read.";
                return;
            }

            var svc = App.AppHost.Services.GetRequiredService<LauncherService>();
            var installed = await Task.Run(() => InstalledModsByGame(svc));
            var report = ProfileInspector.Inspect(manifest, installed);

            ArchiveStatusText.Text = report.Headline;
            ShowArchiveReport(sender as FrameworkElement ?? ArchiveInspectButton, report, file.Path);
        }
        catch (Exception ex) { ArchiveStatusText.Text = ModManager.Core.ErrorRemedy.Describe(ex); }
    }

    /// <summary>What this machine has, per game. A game ABSENT from the map is not registered here;
    /// present-with-nothing means registered with no mods. The report needs both.</summary>
    private static Dictionary<string, IReadOnlyCollection<string>> InstalledModsByGame(LauncherService svc)
    {
        var map = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in svc.LoadRegistry().Games)
        {
            var names = new List<string>();
            try { names.AddRange(ModManager.Core.Scanner.ListClassified(ModManager.Core.Scanner.GameContext(g)).Select(m => m.Name)); }
            catch { /* an unreadable game is still REGISTERED, which is the fact that matters here */ }
            map[g.Id] = names;
        }
        return map;
    }

    private void ShowArchiveReport(FrameworkElement anchor, ProfileReport report, string archivePath)
    {
        var res = Application.Current.Resources;
        var panel = new StackPanel { Spacing = 10, MaxWidth = 460 };

        panel.Children.Add(new TextBlock
        {
            Text = "What is in this backup",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        });
        var head = new TextBlock { Text = report.Headline, TextWrapping = TextWrapping.Wrap };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(head, "ArchiveReportHeadline");
        panel.Children.Add(head);

        // Ticked per game, per part. Only games this machine actually has get boxes - there is
        // nowhere to put a game's files until the game itself is registered here.
        var picks = new List<(string GameId, RestoreParts Part, CheckBox Box)>();

        void Section(string title, IReadOnlyList<ModManager.Core.Transport.ProfileGameReport> games, string id,
                     bool offerRestore = false)
        {
            if (games.Count == 0) return;
            var heading = new TextBlock
            {
                Text = title,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 0),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(heading, id);
            panel.Children.Add(heading);

            // The ids go on the TEXT, never on the panels holding it. A StackPanel is not a
            // control-view element and never reaches the tree an agent walks - the same trap as
            // putting an id on a Border, which .claude/rules/automation-ids.md names explicitly. The
            // first cut of this put them on the rows and a UIA walk found zero of them.
            var list = new StackPanel { Spacing = 4 };
            foreach (var g in games)
            {
                var row = new StackPanel { Spacing = 1 };

                var gameName = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(g.Game.Game.Name) ? g.Game.Game.Id : g.Game.Game.Name!,
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(gameName, $"ArchiveGame.{g.Game.Game.Id}");
                row.Children.Add(gameName);

                var detail = new TextBlock
                {
                    Text = ProfileReportText.DetailFor(g),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)res["ThemeInkDim"],
                    FontSize = 12,
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(detail, $"ArchiveGameDetail.{g.Game.Game.Id}");
                row.Children.Add(detail);

                if (offerRestore)
                {
                    var parts = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        Margin = new Thickness(0, 2, 0, 4),
                    };

                    // A part is only offered when the backup HOLDS it. A ticked box for something the
                    // file does not carry reads as a promise, and the run would quietly do nothing.
                    void Part(string label, RestoreParts part, bool present)
                    {
                        if (!present) return;
                        var box = new CheckBox { Content = label, IsChecked = true, MinWidth = 0 };
                        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(
                            box, $"ArchiveRestorePart.{g.Game.Game.Id}.{part.ToString().ToLowerInvariant()}");
                        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                            box, $"{label} for {g.Game.Game.Name ?? g.Game.Game.Id}");
                        parts.Children.Add(box);
                        picks.Add((g.Game.Game.Id, part, box));
                    }

                    Part("Saves", RestoreParts.Saves, g.Game.SaveIncluded);
                    Part("Mods", RestoreParts.Mods, g.Game.ModFileCount > 0);
                    Part("Settings", RestoreParts.Settings, g.Game.DataFileCount > 0);

                    if (parts.Children.Count > 0) row.Children.Add(parts);
                }

                list.Children.Add(row);
            }
            panel.Children.Add(list);
        }

        Section("Set up on this machine", report.Here, "ArchiveGamesHere", offerRestore: true);
        Section("Waiting on the game", report.NotHere, "ArchiveGamesNotHere");

        // What was deliberately left out, and what it carries about its owner. Both were decided when
        // the archive was written; this is where somebody finally reads them.
        var left = report.ExcludedByReason;
        if (left.Count > 0)
        {
            var bits = new List<string>();
            if (left.TryGetValue("credential", out var c))
                bits.Add($"{c} sign-in file{(c == 1 ? "" : "s")} (account tokens, not save data)");
            if (left.TryGetValue("character", out var ch))
                bits.Add($"{ch} character file{(ch == 1 ? "" : "s")}");
            if (left.TryGetValue("personal", out var p))
                bits.Add($"{p} identifying file{(p == 1 ? "" : "s")}");
            if (bits.Count > 0)
            {
                var tb = new TextBlock
                {
                    Text = "Left out on purpose: " + string.Join(", ", bits) + ".",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)res["ThemeInkDim"],
                    Margin = new Thickness(0, 6, 0, 0),
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(tb, "ArchiveReportExcluded");
                panel.Children.Add(tb);
            }
        }

        var carries = ModManager.Core.Transport.PersonalDataScan.MessageFor(report.Manifest.Notices);
        if (carries.Length > 0)
            panel.Children.Add(new TextBlock
            {
                Text = carries,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)res["ThemeInkDim"],
            });

        // ---- putting it back -------------------------------------------------------------------
        // Below everything the file says about itself, because reading comes before acting and the
        // order on the screen is the order of the decision.
        if (picks.Count > 0)
        {
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = (Microsoft.UI.Xaml.Media.Brush)res["ThemeBorder"],
                Margin = new Thickness(0, 10, 0, 4),
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Nothing here deletes. Saves are snapshotted before they are replaced, and mods "
                     + "are added over what is there rather than clearing it — a mod folder holds the "
                     + "game's own content too.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)res["ThemeInkDim"],
                FontSize = 12,
            });

            var status = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 2, 0, 0),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(status, "ArchiveRestoreStatus");

            // Outlined danger at the entry, per .claude/rules/vsm-danger-buttons.md - filled danger is
            // sanctioned only inside a confirm, and Settings IS a ContentDialog so a second one cannot
            // open over it. The confirm is therefore the button's own second press: it names the count
            // it is about to act on, so an accidental first click cannot become an overwrite.
            var restore = new Button
            {
                Content = "Put chosen parts back…",
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)res["ThemeDanger"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)res["ThemeDanger"],
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 6, 0, 0),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(restore, "ArchiveRestoreButton");

            var armed = false;
            restore.Click += async (_, _) =>
            {
                var chosen = picks.Where(p => p.Box.IsChecked == true).ToList();
                if (chosen.Count == 0)
                {
                    armed = false;
                    restore.Content = "Put chosen parts back…";
                    status.Visibility = Visibility.Visible;
                    status.Text = "Nothing is ticked, so there is nothing to put back.";
                    return;
                }

                if (!armed)
                {
                    armed = true;
                    var n = chosen.Select(c => c.GameId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                    restore.Content = $"Confirm — overwrite files for {n} game{(n == 1 ? "" : "s")}";
                    return;
                }

                armed = false;
                restore.Content = "Put chosen parts back…";
                await PutBackAsync(archivePath, chosen, status, restore);
            };

            // Changing what is ticked disarms it. Otherwise a confirm could act on a different set
            // than the one it named.
            foreach (var (_, _, box) in picks)
            {
                void Disarm(object _, RoutedEventArgs __)
                {
                    if (!armed) return;
                    armed = false;
                    restore.Content = "Put chosen parts back…";
                }
                box.Checked += Disarm;
                box.Unchecked += Disarm;
            }

            panel.Children.Add(restore);
            panel.Children.Add(status);
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Text = "None of these games are set up on this machine yet, so there is nowhere to put "
                     + "them back. Add a game, then open this backup again.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }

        new Flyout
        {
            Content = new ScrollViewer { Content = panel, MaxHeight = 420, Padding = new Thickness(0, 0, 12, 0) },
        }.ShowAt(anchor);
    }

    /// <summary>
    /// Run the restore for what was ticked.
    ///
    /// <para><b>Every path is resolved HERE, from the registry, now.</b> The archive records what a
    /// game HAD, never where it lived — a fresh install has a different Steam library, a different
    /// drive and different folders, and reusing the recorded path is how a restore writes into
    /// somebody else's machine layout.</para>
    ///
    /// <para>Mod locations are passed by NAME so each mod goes back to the folder it came from. A game
    /// can keep mods in more than one place, and the primary is not always the right answer.</para>
    /// </summary>
    private async Task PutBackAsync(
        string archivePath,
        IReadOnlyList<(string GameId, RestoreParts Part, CheckBox Box)> chosen,
        TextBlock status,
        Button button)
    {
        var wanted = new Dictionary<string, RestoreParts>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, part, _) in chosen)
            wanted[id] = wanted.TryGetValue(id, out var have) ? have | part : part;

        var svc = App.AppHost.Services.GetRequiredService<LauncherService>();
        var games = svc.LoadRegistry().Games.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);

        var requests = new List<RestoreRequest>();
        var unresolved = new List<string>();
        foreach (var (id, parts) in wanted)
        {
            if (!games.TryGetValue(id, out var game)) { unresolved.Add(id); continue; }

            ModManager.Core.GameContext ctx;
            try { ctx = ModManager.Core.Scanner.GameContext(game); }
            catch { unresolved.Add(id); continue; }

            requests.Add(new RestoreRequest(
                id,
                parts,
                SaveDir: string.IsNullOrEmpty(game.SaveDir) ? null : game.SaveDir,
                ModDir: ctx.Locations.Count > 0 ? ctx.Locations[0].Abs : null,
                DataDir: ctx.DataDir,
                SnapshotsDir: ctx.SavesDir)
            {
                ModDirsByLocation = ctx.Locations.ToDictionary(
                    l => l.Name, l => l.Abs, StringComparer.OrdinalIgnoreCase),
            });
        }

        button.IsEnabled = false;
        status.Visibility = Visibility.Visible;
        status.Text = "Putting things back…";
        try
        {
            var probe = new GameProcessProbe();

            // Fails CLOSED both ways: a game the registry no longer knows counts as running, and the
            // probe's own enumeration failure propagates for Core to catch as the same answer. A
            // folder changed under a live game is silently undone on exit.
            var result = await Task.Run(() => ProfileRestore.Restore(
                archivePath, requests,
                gameId => !games.TryGetValue(gameId, out var g) || probe.AnyRunning(g)));

            var text = result.Summary;
            if (unresolved.Count > 0)
                text += $" {unresolved.Count} could not be located on this machine: "
                      + string.Join(", ", unresolved) + ".";
            status.Text = text;

            if (result.TotalFiles > 0) RestoreHappened = true;
        }
        catch (Exception ex) { status.Text = ModManager.Core.ErrorRemedy.Describe(ex); }
        finally { button.IsEnabled = true; }
    }

    private static string Human(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {units[i]}";
    }

    public SettingsDialog(IntPtr hwnd, AvatarService avatars, ThemeService themes, AppSettingsService appSettings, MainViewModel vm)
    {
        InitializeComponent();
        ModManager.App.Services.DialogTheming.Apply(this); // vibe-glow wave 1: popup-scope theme brushes
        _hwnd = hwnd;
        _avatars = avatars;
        _themes = themes;
        _appSettings = appSettings;
        _vm = vm;
        _rp = App.AppHost.Services.GetRequiredService<RestorePointService>();

        DeriveThemeCheck.Checked   += (_, _) => ThemeNameBox.Visibility = Visibility.Visible;
        DeriveThemeCheck.Unchecked += (_, _) => ThemeNameBox.Visibility = Visibility.Collapsed;

        // Seed the preview. With an avatar set it shows theirs; without one it shows the launcher's
        // OWN icon, because an empty square next to "Pick image…" does not say what the control does.
        // MainViewModel.AppIconSource already answers "user's, else bundled" for the title bar - reuse
        // it rather than writing a second rule that can drift from it.
        if (_avatars.HasAvatar)
        {
            PreviewImage.Source = new BitmapImage(new Uri(_avatars.AvatarPngPath));
            FileLabel.Text = "Current avatar";
            RemoveButton.Visibility = Visibility.Visible;
        }
        else
        {
            try { PreviewImage.Source = new BitmapImage(new Uri(vm.AppIconSource)); }
            catch { /* a missing bundled icon leaves the box empty, exactly as before */ }
            FileLabel.Text = "The launcher's icon";
        }

        // Seed the backdrop dropdown to the currently-saved value. The flag suppresses the initial
        // SelectionChanged firing as a "user action" — we only apply on actual user changes.
        BackdropBox.SelectedIndex = _appSettings.Backdrop switch
        {
            WindowBackdropKind.Mica    => 1,
            WindowBackdropKind.Acrylic => 2,
            _                          => 0,
        };
        _suppressBackdropChange = false;

        // Seed the auto-check-for-mod-updates toggle from the saved setting (default on).
        AutoCheckModUpdatesCheck.IsChecked = _appSettings.AutoCheckModUpdates;

        // Seed the keep-plugins-updated toggle.
        KeepPluginsUpdatedCheck.IsChecked = _appSettings.KeepPluginsUpdated;
        RefreshPluginStatus();

        // Seed the Nexus section. Re-validate the stored key first (offline-safe) so the account
        // name + premium tag are current before we render the banner.
        _ = InitializeNexusSectionAsync();

        // Seed the About → Installed tools list. Pure file-read, fast — fine on the UI thread.
        RefreshRestorePoints();
    }



    private static string GetUrlForFramework(string frameworkId)
        => KnownFramework.Catalog.FirstOrDefault(f => f.FrameworkId == frameworkId)?.GetUrl ?? "";





    private void OnBackdropChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressBackdropChange) return;
        if (BackdropBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;
        var kind = tag switch
        {
            "mica"    => WindowBackdropKind.Mica,
            "acrylic" => WindowBackdropKind.Acrylic,
            _         => WindowBackdropKind.Solid,
        };
        _appSettings.SetBackdrop(kind);
        // The MainWindow listens to AppSettingsService.BackdropChanged and re-applies the backdrop.
    }

    private async Task InitializeNexusSectionAsync()
    {
        if (_vm.NexusConnected) await _vm.RefreshNexusAsync();
        RefreshNexusUi();
    }

    /// <summary>Render the Nexus section based on the current connection + configuration state. Called on
    /// dialog open and after every Connect/Disconnect action. Secure OAuth sign-in — no key to paste. In
    /// the dark window (client_id not delivered yet) the Connect button is disabled with a "finalizing" note.</summary>
    private void RefreshNexusUi()
    {
        if (_vm.NexusConnected)
        {
            NexusConnectedBanner.Visibility = Visibility.Visible;
            NexusConnectedText.Text = $"Connected as {_vm.NexusAccountLine}";
            NexusExplainer.Text = "Your Nexus account is signed in with secure OAuth — used for endorsements, " +
                                  "mod ID lookups, and update checks. Disconnect to remove the saved sign-in, " +
                                  "or connect again to switch accounts.";
            NexusConnectButton.Content = "Switch account";
            NexusDisconnectButton.Visibility = Visibility.Visible;
        }
        else
        {
            NexusConnectedBanner.Visibility = Visibility.Collapsed;
            NexusExplainer.Text = "Sign in to Nexus with secure OAuth — it opens in your browser, no API key to " +
                                  "paste. Your sign-in stays on this machine and is only sent to Nexus's own API.";
            NexusConnectButton.Content = "Connect Nexus account";
            NexusDisconnectButton.Visibility = Visibility.Collapsed;
        }

        // Dark window: the OAuth client_id hasn't been delivered yet — connecting isn't possible, so disable
        // the button and explain instead of letting the user hit a dead end.
        var configured = _vm.NexusSignInConfigured;
        NexusConnectButton.IsEnabled = configured;
        NexusConnectSublabel.Text = "Secure sign-in is being finalized with Nexus.";
        NexusConnectSublabel.Visibility = configured ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>"Connect Nexus account" — hands off to the shell. The OAuth flow opens a browser and (on a
    /// first-ever connect) shows the consent dialog; neither can nest under this ContentDialog, so we flag +
    /// close and MainWindow.OnSettings runs <c>ViewModel.ConnectNexusAsync()</c> once this dialog is closed.</summary>
    private void OnNexusConnect(object sender, RoutedEventArgs e)
    {
        ConnectNexusRequested = true;
        Hide();
    }

    private void OnNexusDisconnect(object sender, RoutedEventArgs e)
    {
        _vm.DisconnectNexus();
        StatusText.Text = "Disconnected from Nexus.";
        RefreshNexusUi();
    }

    /// <summary>Persist the auto-check-for-mod-updates preference immediately on toggle (no Apply
    /// needed — it mirrors the backdrop dropdown's apply-on-change behavior).</summary>
    private void OnAutoCheckModUpdatesToggled(object sender, RoutedEventArgs e)
        => _appSettings.SetAutoCheckModUpdates(AutoCheckModUpdatesCheck.IsChecked == true);

    /// <summary>Persist the keep-plugins-updated preference immediately on toggle.</summary>
    private void OnKeepPluginsUpdatedToggled(object sender, RoutedEventArgs e)
        => _appSettings.SetKeepPluginsUpdated(KeepPluginsUpdatedCheck.IsChecked == true);

    /// <summary>Manual "Install / refresh Nexus plugin" button. FULL only — awaits
    /// <see cref="PluginFeedSource.FetchAsync"/> with <c>force: true</c> and maps the outcome to a
    /// human-readable status line. The button is disabled while the fetch is in flight so rapid
    /// double-clicks don't race the installer. STORE: <see cref="PluginFeedSource"/> is not
    /// registered, so we guard with <see cref="Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService{T}"/> and
    /// show the desktop-only note instead.</summary>
    private async void OnRefreshPlugin(object sender, RoutedEventArgs e)
    {
#if FULL
        var feed = App.AppHost.Services.GetService<PluginFeedSource>();
        if (feed is null)
        {
            PluginStatusText.Text = "Plugins are a desktop-only feature.";
            return;
        }
        RefreshPluginButton.IsEnabled = false;
        PluginStatusText.Text = "Checking the plugin feed…";
        try
        {
            var result = await feed.FetchAsync(force: true);
            PluginStatusText.Text = result.Outcome switch
            {
                PluginFetchOutcome.Installed    => $"Nexus plugin v{result.Version} installed.",
                PluginFetchOutcome.UpToDate     => $"Nexus plugin is up to date (v{result.Version}).",
                PluginFetchOutcome.RequiresUpdate => $"This plugin needs launcher v{result.Version} — update the launcher.",
                PluginFetchOutcome.NotApplicable => "Connect Nexus first.",
                PluginFetchOutcome.Failed        => $"Couldn’t fetch the plugin: {result.Message}",
                _                               => result.Message ?? "Done.",
            };
        }
        finally { RefreshPluginButton.IsEnabled = true; }
#else
        PluginStatusText.Text = "Plugins are a desktop-only feature.";
        await System.Threading.Tasks.Task.CompletedTask;
#endif
    }

    /// <summary>Populate the plugin status line. FULL shows the installed version (or "not
    /// installed"); STORE shows a static note because plugin delivery is desktop-only.</summary>
    private void RefreshPluginStatus()
    {
#if FULL
        var recordPath = System.IO.Path.Combine(PluginHost.PluginsDir, "installed-plugins.json");
        var installed = InstalledPluginsStore.Read(recordPath);
        if (installed.TryGetValue("nexus", out var version))
            PluginStatusText.Text = $"Nexus plugin: v{version} installed";
        else
            PluginStatusText.Text = "Nexus plugin: not installed";
#else
        PluginStatusText.Text = "Plugins are a desktop-only feature.";
#endif
    }

    private async void OnPickImage(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".bmp");
        picker.FileTypeFilter.Add(".gif");
        picker.FileTypeFilter.Add(".webp");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        _pickedSourcePath = file.Path;
        FileLabel.Text = System.IO.Path.GetFileName(file.Path);

        try
        {
            // Decode + sample at 64×64 for palette extraction (fast, plenty of signal).
            var (_, rgba64) = await AvatarService.ResizeToSquareAsync(file.Path, 64);
            _palette = PaletteExtractor.Extract(rgba64, 64, 64, k: 5);
            PreviewImage.Source = new BitmapImage(new Uri(file.Path));
            RenderPaletteStrip();
            StatusText.Text = "";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Couldn't read that image: " + ex.Message;
        }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        _avatars.Delete();
        Changed = true;
        Hide();
    }

    private async void OnApply(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var hasPick    = !string.IsNullOrEmpty(_pickedSourcePath);
        var wantsIcon  = UseAsIconCheck.IsChecked == true;
        var wantsTheme = DeriveThemeCheck.IsChecked == true && _palette.Count > 0;

        // Nothing semantically requested: no new pick, no existing avatar to clear, no theme to save.
        if (!hasPick && !wantsTheme && (!_avatars.HasAvatar || wantsIcon)) return;

        var deferral = args.GetDeferral();
        try
        {
            // Icon flow. "Use this image as the launcher's icon" is the END STATE the user wants:
            //   - Checked + new image picked → set the avatar to the picked image.
            //   - Unchecked + avatar exists  → revert to the bundled icon (delete the saved avatar).
            //   - Unchecked + no avatar      → no-op.
            //   - Checked + no new pick      → keep whatever's already set (no-op).
            if (wantsIcon && hasPick)
                await _avatars.ImportAsync(_pickedSourcePath!);
            else if (!wantsIcon && _avatars.HasAvatar)
                _avatars.Delete();

            if (wantsTheme)
            {
                var name = string.IsNullOrWhiteSpace(ThemeNameBox.Text) ? "From avatar" : ThemeNameBox.Text.Trim();
                var raw = PaletteToTheme.Derive(_palette, name);
                var json = SerializeRawTheme(raw);
                _themes.ImportUserTheme(json);
            }

            Changed = true;
        }
        catch (Exception ex)
        {
            // Cause framing, not bare exception text (F-062) — the user needs to know WHAT failed.
            StatusText.Text = "Couldn't apply these changes — " + ex.Message;
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void RenderPaletteStrip()
    {
        PaletteStrip.Children.Clear();
        foreach (var p in _palette)
        {
            // Color-only information gets a VISIBLE text alternative (F-045): Rectangle has no
            // automation peer, so a UIA name on it announces nothing — the hex caption is the
            // honest fix (readable, keyboard-independent, and picked up by OCR-based AT too).
            var hex = $"#{p.R:X2}{p.G:X2}{p.B:X2}";
            var swatch = new Rectangle
            {
                Width = 48, Height = 32,
                RadiusX = 0, RadiusY = 0,
                Fill = new SolidColorBrush(Color.FromArgb(255, p.R, p.G, p.B)),
            };
            ToolTipService.SetToolTip(swatch, hex);
            var caption = new TextBlock
            {
                Text = hex,
                FontSize = 10,
                FontFamily = (FontFamily)Application.Current.Resources["MonoFontFamily"],
                Foreground = (SolidColorBrush)Application.Current.Resources["ThemeInkDim"],
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            PaletteStrip.Children.Add(new StackPanel { Spacing = 2, Children = { swatch, caption } });
        }
        PaletteEmpty.Visibility = _palette.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string SerializeRawTheme(RawTheme raw)
    {
        var pairs = raw.Tokens.Select(kv => $"\"{kv.Key}\":\"{kv.Value}\"");
        var bloom = raw.AccentBloom is null
            ? ""
            : $",\"accent_bloom\":{{\"blur\":{raw.AccentBloom.Blur},\"alpha\":{raw.AccentBloom.Alpha}}}";
        return "{" + string.Join(",", pairs) + bloom + "}";
    }

    /// <summary>Populate the Settings → Restore points list from the service.</summary>
    private void RefreshRestorePoints()
    {
        var pts = _rp.ListRestorePoints();
        RestorePointsList.ItemsSource = pts
            .Select(p => new RestorePointRow(
                Timestamp: p.Timestamp,
                Detail: $"{string.Join(", ", p.GameNames)} · {FormatSize(p.TotalBytes)}",
                Id: p.Timestamp))
            .ToList();
        NoRestorePointsText.Visibility = pts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string FormatSize(long bytes)
    {
        const long GiB = 1L << 30;
        const long MiB = 1L << 20;
        return bytes >= GiB
            ? $"{bytes / (double)GiB:F1} GB"
            : $"{bytes / (double)MiB:F0} MB";
    }

    /// <summary>Restore button. WinUI 3 forbids opening a second ContentDialog while one is already
    /// showing — the confirm would throw InvalidOperationException. Pattern mirrors OnResetLauncher:
    /// set the hand-off timestamp, Hide() this dialog, and let MainWindow.OnSettings show the confirm
    /// after ShowAsync() returns (SettingsDialog is fully closed by then, no nesting).</summary>
    private void OnRestorePoint(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string ts) return;
        RestoreRequestedTimestamp = ts;
        Hide();
    }

    /// <summary>Delete button. Same nested-ContentDialog constraint as OnRestorePoint — route the
    /// action out to MainWindow via the flag-then-hide hand-off.</summary>
    private void OnDeleteRestorePoint(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string ts) return;
        DeleteRequestedTimestamp = ts;
        Hide();
    }

    /// <summary>Reset launcher button. Sets the hand-off flag and closes this dialog — MainWindow
    /// opens SafeClearDialog after ShowAsync() returns so both ContentDialogs never overlap.</summary>
    private void OnResetLauncher(object sender, RoutedEventArgs e)
    {
        OpenSafeClearRequested = true;
        Hide();
    }

}
