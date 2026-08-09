using Microsoft.UI.Dispatching;
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

        // Built before SeedFields so a field handler can never reach a null timer.
        _previewTimer = DispatcherQueue.CreateTimer();
        _previewTimer.Interval = TimeSpan.FromMilliseconds(250);
        _previewTimer.IsRepeating = false;
        _previewTimer.Tick += (_, _) => RenderPreview();
        Closed += (_, _) => _previewTimer.Stop();   // never walk the disk for a dialog that is gone

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

        // One box, possibly several locations. ModLocator.Detect adds every candidate folder that
        // exists ("mods", "mods2", "mods3"…), and games really do carry three — Windrose declares
        // ~mods, LogicMods, and the UE4SS folder, all holding mods. The diagnosis above lists all of
        // them, so say plainly which one this box edits rather than let it read as covering the lot.
        if (_game.ModLocations.Count > 1)
            ModPathLabel.Text = $"Mod folder (the first of {_game.ModLocations.Count}; the others are unchanged)";
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
        // No explicit Preview() — assigning Text raises TextChanged, which is already wired to it.
        // Calling both ran the plan twice, and a plan is a directory walk (see _previewTimer).
        if (folder is not null) FolderBox.Text = folder.Path;
    }

    private GameEntry BuildProposed()
    {
        var exts = ExtensionsBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var loc = _game.ModLocations.Count > 0 ? _game.ModLocations[0] : new ModLocation("mods", "mods", "mods");
        var first = loc with { Path = ModPathBox.Text.Trim() };

        // EDIT THE FIRST LOCATION, CARRY THE REST. Rebuilding a single-element list would do two
        // separate kinds of damage to a game with more than one declared location — and multi-location
        // registrations are ordinary, not a corner case (ModLocator.Detect adds every folder that
        // exists, as "mods" / "mods2" / "mods3").
        //
        // 1. Saving would DELETE locations 2..n. Every mod in those folders drops out of the
        //    launcher's view and the per-location disable metadata keyed on "mods2" is orphaned.
        // 2. RegistrationChange.SameLocations compares Count first, so 3-vs-1 always reads as
        //    changed — landing modLocations in FieldsChanged, then FieldsToPin, then UserSet, and
        //    permanently opting that game out of every future manifest correction to its mod paths.
        //    Someone fixing a typo in the game name would trigger exactly the failure this feature
        //    exists to prevent.
        var locations = _game.ModLocations.Count > 1
            ? new[] { first }.Concat(_game.ModLocations.Skip(1)).ToArray()
            : new[] { first };

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
            ModLocations = locations,
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

    /// <summary>
    /// Coalesce keystrokes before re-planning.
    ///
    /// <para>A preview is not cheap. <c>RegistrationChange.Plan</c> plans the data-dir move whenever
    /// the game root has changed, and <c>DataDirMove.Plan</c> does a <c>Directory.GetFiles</c> over
    /// <c>AllDirectories</c> plus a <c>FileInfo.Length</c> per file — on the UI thread. Once the user
    /// corrects the folder, "the root has changed" stays true for the rest of the session, so an
    /// undebounced keystroke in ANY of the seven boxes re-walks <c>disabled\</c>,
    /// <c>direct-disabled\</c>, <c>frameworks\*\disabled-proxy\</c> and <c>tools\</c> — thousands of
    /// files and gigabytes for a well-used game.</para>
    ///
    /// <para>Coalescing here rather than caching the move plan because the walk happens INSIDE the
    /// Core planner, which the App cannot reach into: the same call produces the field diff, the
    /// blockers (a move refusal becomes one) and the move plan together. A dialog-side cache could
    /// only avoid it by re-deriving which blocker came from where, which would put consequence
    /// decisions back in the UI — the exact thing <c>RegistrationChange</c>'s doc forbids.</para>
    /// </summary>
    private readonly DispatcherQueueTimer _previewTimer;

    private void Preview()
    {
        if (_seeding) return;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void RenderPreview()
    {
        var plan = _repair.Preview(_game, BuildProposed());
        var lines = new List<string>();

        // THE LOCK-IN VERB BELONGS TO FieldsToPin, NOT FieldsChanged. The two answer different
        // questions and FieldsToPin can be SHORTER: on an engine change Core drops a changed field
        // whose value equals the newly-picked preset's own default, because that is the preset
        // speaking rather than the user. Binding the promise to FieldsChanged makes this panel state
        // the opposite of what saving does — repair a Skyrim registration added as "custom" by
        // picking bethesda and typing that preset's own Data / esp,esl,esm,bsa, and both fields are
        // reported as locked in while Core locks in neither.
        //
        // A field that was ALREADY pinned before this edit is not locked in BY this edit either; it
        // was locked in whenever the user last set it. It changed, and it saves.
        var pinning = new HashSet<string>(plan.FieldsToPin, StringComparer.OrdinalIgnoreCase);
        var alreadyPinned = new HashSet<string>(
            _game.UserSet ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        foreach (var f in plan.FieldsChanged)
            lines.Add(pinning.Contains(f) && !alreadyPinned.Contains(f)
                ? $"lock in your {Human(f)}, so future definition updates leave it alone"
                : $"update the {Human(f)}");

        // Kept a separate loop over a separate list on purpose: OtherChanges is what merely saves,
        // FieldsChanged is what can get locked in. Merging them would lose that distinction.
        foreach (var f in plan.OtherChanges)
            lines.Add($"update the {Human(f)}");

        if (plan.DataDir is { } move)
            lines.Add($"ask whether to move this game's launcher data ({move.FileCount} files) from {move.From}");

        var hasLines = lines.Count > 0;
        ConsequencesHeading.Visibility = hasLines ? Visibility.Visible : Visibility.Collapsed;
        ConsequencesText.Visibility = hasLines ? Visibility.Visible : Visibility.Collapsed;
        ConsequencesText.Text = hasLines ? "• " + string.Join("\n• ", lines) : "";

        // Notes are advisories about the edit, not consequences of saving, and they arrive as full
        // sentences — bulleted under "Saving will:" they produced "Saving will: • Changing the engine
        // from 'custom' to 'bethesda' changes which defaults…", which is not a sentence.
        var hasNotes = plan.Notes.Count > 0;
        NotesHeading.Visibility = hasNotes ? Visibility.Visible : Visibility.Collapsed;
        NotesText.Visibility = hasNotes ? Visibility.Visible : Visibility.Collapsed;
        NotesText.Text = string.Join(" ", plan.Notes);

        ConsequencesPanel.Visibility = hasLines || hasNotes ? Visibility.Visible : Visibility.Collapsed;

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
