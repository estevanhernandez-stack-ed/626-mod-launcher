using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModManager.App.Services;
using ModManager.Core;
using ModManager.Core.Discovery;

namespace ModManager.App;

/// <summary>
/// What this game's registration claims, next to what the launcher actually found — and, behind an
/// expander, the fields to change it.
///
/// <para>Diagnosis first, on purpose. The common outcome is that NOTHING is wrong: a game whose mods
/// load by a route the registration does not describe is normal (Elden Ring's eleven mods load by
/// direct-inject while it declares a Mod Engine 2 folder that does not exist). A surface that opened
/// straight into editable fields would imply something needed editing.</para>
///
/// <para>One dialog rather than two because WinUI 3 permits one ContentDialog per XamlRoot; chaining
/// diagnose to edit to confirm would be two nested hand-offs. This leaves exactly one, for the
/// move-or-pin confirm.</para>
/// </summary>
public sealed partial class GameSetupDialog : ContentDialog
{
    private readonly GameEntry _game;
    private readonly RegistrationRepairService _repair;
    private readonly GameShape _shape;

    // Owner window handle — the folder picker needs it, exactly as AddGameDialog does.
    private readonly IntPtr _hwnd;

    public GameSetupDialog(IntPtr hwnd, GameEntry game, RegistrationRepairService repair)
    {
        InitializeComponent();
        _hwnd = hwnd;
        _game = game;
        _repair = repair;
        _shape = repair.Shape(game);
        DialogTheming.Apply(this);   // popup-scope theme brushes
        RenderDiagnosis();
        SeedFields();
    }

    private void RenderDiagnosis()
    {
        ModsFoundText.Text = _shape.ModCount switch
        {
            0 => "None.",
            1 => "1 mod.",
            _ => $"{_shape.ModCount} mods.",
        };

        // A loader explains why sibling mods load from a folder the registration never mentions —
        // without it named, the drift below reads as misconfiguration with no cause.
        var hasLoaders = _shape.Loaders.Count > 0;
        LoadedByLabel.Visibility = hasLoaders ? Visibility.Visible : Visibility.Collapsed;
        LoadedByText.Visibility = hasLoaders ? Visibility.Visible : Visibility.Collapsed;
        LoadedByText.Text = string.Join(", ", _shape.Loaders);

        var roots = _shape.ContentRoots
            .Select(r => string.IsNullOrEmpty(r.RelativePath) ? "the game folder" : r.RelativePath)
            .ToList();
        LivingInLabel.Visibility = roots.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LivingInText.Visibility = roots.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LivingInText.Text = string.Join(", ", roots);

        // Never collapsed, unlike the loader/content-root rows above: an empty declared-location
        // list is exactly the condition this dialog exists to surface — a registration with
        // nothing declared at all. Hiding the row would go silent on the case a caller reaching
        // for this dialog is more likely than average to be looking at. "None declared." states
        // the fact plainly; GameShape.Notes is the only place that gets to say whether it's a problem.
        DeclaredLabel.Visibility = Visibility.Visible;
        DeclaredText.Visibility = Visibility.Visible;
        DeclaredText.Text = _shape.DeclaredLocations.Count > 0
            ? string.Join(", ", _shape.DeclaredLocations
                .Select(d => d.Exists ? d.Path : d.Path + "  (this folder doesn't exist)"))
            : "None declared.";

        // Rendered verbatim: GameShape already states whether drift is a problem, and re-wording it
        // here would let the dialog and the MCP tool tell the user two different stories.
        VerdictText.Text = string.Join(" ", _shape.Notes);
    }

    /// <summary>The edited entry, or null when the user closed without saving. Read by MainWindow.</summary>
    public GameEntry? Proposed { get; private set; }

    /// <summary>Set when saving implies a data-dir move the user must decide about. MainWindow shows
    /// the move-or-pin confirm AFTER this dialog closes — WinUI 3 forbids a nested ContentDialog.
    ///
    /// <para>internal, not public, and it has to stay that way. The XAML type-info generator walks the
    /// PUBLIC properties of every x:Class type and emits a parameterless activator for each property's
    /// type; <see cref="DataDirMovePlan"/> is all-required-members by design, so a public property here
    /// fails the build in generated code (CS9035 in XamlTypeInfo.g.cs) with no mention of this file.
    /// MainWindow lives in this assembly, so nothing downstream notices the difference.</para></summary>
    internal DataDirMovePlan? MoveDataDirRequested { get; private set; }

    private bool _seeding;

    private void SeedFields()
    {
        _seeding = true;   // suppress the live preview while we populate
        NameBox.Text = _game.GameName;
        FolderBox.Text = _game.GameRoot;
        ModPathBox.Text = _game.ModLocations.Count > 0 ? _game.ModLocations[0].Path : "";
        ExtensionsBox.Text = string.Join(", ", _game.FileExtensions);
        GroupingBox.Text = _game.GroupingRule;
        SteamBox.Text = _game.SteamAppId ?? "";
        LauncherBox.Text = _game.RequiredLauncher ?? "";

        EngineBox.ItemsSource = EnginePresets.Presets
            .Select(p => new EngineOption(p.Key, p.Value.Label)).ToList();
        EngineBox.SelectedItem = ((List<EngineOption>)EngineBox.ItemsSource)
            .FirstOrDefault(o => string.Equals(o.Key, _game.Engine, StringComparison.OrdinalIgnoreCase));
        _seeding = false;
    }

    private sealed record EngineOption(string Key, string Label);

    private void OnFieldChanged(object sender, TextChangedEventArgs e) => Preview();

    // Unlike AddGameDialog, changing the engine here must NOT rewrite the mod-path box. Auto-filling a
    // field the user did not type makes RegistrationChange read it as their choice; the Core planner
    // drops preset-equal values on an engine change to defend against exactly that, and there is no
    // reason to hand it the problem in the first place.
    private void OnEngineChanged(object sender, SelectionChangedEventArgs e) => Preview();

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null) { FolderBox.Text = folder.Path; Preview(); }
    }

    private GameEntry BuildProposed()
    {
        var exts = ExtensionsBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var loc = _game.ModLocations.Count > 0 ? _game.ModLocations[0] : new ModLocation("mods", "mods", "mods");

        return new GameEntry
        {
            // Id is IMMUTABLE across an edit: it is half the key the data-dir path derives from, so
            // re-slugging it on a rename would orphan every disabled mod, profile, and installed tool.
            Id = _game.Id,
            GameName = NameBox.Text.Trim(),
            Engine = (EngineBox.SelectedItem as EngineOption)?.Key ?? _game.Engine,
            WindowTitle = _game.WindowTitle,
            GameRoot = FolderBox.Text.Trim(),
            FileExtensions = exts,
            GroupingRule = GroupingBox.Text.Trim(),
            ModLocations = new[] { loc with { Path = ModPathBox.Text.Trim() } },
            SteamAppId = string.IsNullOrWhiteSpace(SteamBox.Text) ? null : SteamBox.Text.Trim(),
            LaunchUrl = _game.LaunchUrl,
            LaunchExe = _game.LaunchExe,
            LaunchTargets = _game.LaunchTargets,
            ModEngineConfig = _game.ModEngineConfig,
            DataDir = _game.DataDir,
            CurseforgeGameId = _game.CurseforgeGameId,
            ScanSubfolders = _game.ScanSubfolders,
            SaveDir = _game.SaveDir,
            RequiredLauncher = string.IsNullOrWhiteSpace(LauncherBox.Text) ? null : LauncherBox.Text.Trim(),
            SaveModPath = _game.SaveModPath,
            SaveModForbidden = _game.SaveModForbidden,
            NexusGameDomain = _game.NexusGameDomain,
            AutoBackupOnLaunch = _game.AutoBackupOnLaunch,
            SaveAutoKeep = _game.SaveAutoKeep,
            LastKnownSteamBuildId = _game.LastKnownSteamBuildId,
            StoreSource = _game.StoreSource,
            LastLaunchedUtc = _game.LastLaunchedUtc,
            UserSet = _game.UserSet,
        };
    }

    private void Preview()
    {
        if (_seeding) return;

        var plan = _repair.Preview(_game, BuildProposed());
        var lines = new List<string>();

        foreach (var f in plan.FieldsChanged)
            lines.Add($"lock in your {Human(f)}, so future definition updates leave it alone");
        foreach (var f in plan.OtherChanges)
            lines.Add($"update the {Human(f)}");
        if (plan.DataDir is { } move)
            lines.Add($"ask whether to move this game's launcher data ({move.FileCount} files) from {move.From}");
        foreach (var n in plan.Notes)
            lines.Add(n);

        ConsequencesText.Text = lines.Count > 0 ? "• " + string.Join("\n• ", lines) : "";
        ConsequencesPanel.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        BlockerText.Text = string.Join(" ", plan.Blockers);
        BlockerText.Visibility = plan.Blockers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Nothing to save is as good a reason to disable Save as a blocker is.
        IsPrimaryButtonEnabled = plan.CanSave
            && (plan.FieldsChanged.Count > 0 || plan.OtherChanges.Count > 0 || plan.DataDir is not null);
    }

    private static string Human(string field) => field switch
    {
        GameEntry.UserSetFileExtensions => "file extensions",
        GameEntry.UserSetGroupingRule => "grouping rule",
        GameEntry.UserSetModLocations => "mod folder",
        GameEntry.UserSetGameRoot => "game folder",
        GameEntry.FieldGameName => "game name",
        GameEntry.FieldEngine => "engine",
        GameEntry.FieldSteamAppId => "Steam App ID",
        GameEntry.FieldRequiredLauncher => "required launcher",
        _ => field,
    };

    // Set the outputs and let the dialog close. The move-or-pin confirm and the save itself happen in
    // MainWindow AFTER this returns — a second ContentDialog cannot open while this one is up.
    //
    // BuildProposed() is called fresh here rather than reusing whatever Preview() last built: the save
    // path hands the instance to RegistrationRepairService, which assigns DataDir and UserSet ONTO it,
    // so a cached instance would preview differently on a second attempt after a failed save.
    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var proposed = BuildProposed();
        var plan = _repair.Preview(_game, proposed);
        if (!plan.CanSave) { args.Cancel = true; return; }   // keep the dialog open; typed edits survive
        Proposed = proposed;
        MoveDataDirRequested = plan.DataDir;
    }
}
