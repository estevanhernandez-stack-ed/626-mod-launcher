using ModManager.Core.Frameworks;

namespace ModManager.Core.RestorePoints;

/// <summary>Hydrated inputs the engine needs to capture one game. All derivable from Core; the App
/// supplies the GameEntry + a built GameContext and picks the end-state.</summary>
public sealed record GameCaptureInput(GameEntry Game, GameContext Context, string EndState);

/// <summary>Result of <see cref="RestorePointEngine.ApplyEndState"/>. vanilla populates MovedFiles;
/// modsActive populates EnableOutcomes. The other list is always empty.</summary>
public sealed record EndStateResult(
    IReadOnlyList<MovedFile> MovedFiles,
    IReadOnlyList<Scanner.EnableOutcome> EnableOutcomes);

/// <summary>
/// The headless Safe Clear / Restore file engine. Takes explicit archive paths — no %APPDATA%
/// knowledge, no UI. Composes Phase 0 primitives (SafeMove, PathGate) + existing Core. The App
/// orchestrator (Phase 1B) calls these in the Law-A order: capture-all -> seal -> mutate-all.
/// </summary>
public static partial class RestorePointEngine
{
    /// <summary>Apply the chosen end-state to a game AFTER its capture is sealed.
    /// vanilla: move detected direct-inject game-folder files into the archive (recorded), uninstall
    /// frameworks (their files were captured), flip loader manifests off; owned mods untouched.
    /// modsActive: re-enable everything from holding, returning per-mod outcomes (skips surfaced).</summary>
    public static EndStateResult ApplyEndState(GameContext c, string endState, string gameArchiveDir)
    {
        if (string.Equals(endState, "modsActive", StringComparison.OrdinalIgnoreCase))
            return new EndStateResult(Array.Empty<MovedFile>(), ReEnableAll(c));
        if (!string.Equals(endState, "vanilla", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown end-state \"{endState}\" (expected \"vanilla\" or \"modsActive\").", nameof(endState));

        var moved = MoveDirectInjectToArchive(c, gameArchiveDir);
        UninstallFrameworks(c);
        FlipLoadersOff(c);
        return new EndStateResult(moved, Array.Empty<Scanner.EnableOutcome>());
    }

    private static IReadOnlyList<Scanner.EnableOutcome> ReEnableAll(GameContext c)
    {
        var outcomes = new List<Scanner.EnableOutcome>();
        foreach (var name in DirectoryNames(c.DisabledRoot))
            outcomes.Add(Scanner.EnableModWithOutcomeAsync(name, c).GetAwaiter().GetResult());
        return outcomes;
    }

    private static IReadOnlyList<MovedFile> MoveDirectInjectToArchive(GameContext c, string gameArchiveDir)
    {
        var playFolder = c.GameRoot;
        // DirectInject.Detect expects basenames (just file/dir names), not full paths.
        // Directory.GetFiles/GetDirectories return full paths; extract the filename component first.
        var fileNames = Directory.Exists(playFolder)
            ? Directory.GetFiles(playFolder).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList()
            : new List<string>();
        var dirNames = Directory.Exists(playFolder)
            ? Directory.GetDirectories(playFolder).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList()
            : new List<string>();

        var moved = new List<MovedFile>();
        Directory.CreateDirectory(Path.Combine(gameArchiveDir, "vanilla-moved"));
        // Catalog direct-inject only (Detect). The DLL-mod-loader's individual mods/*.dll sub-mods
        // (DirectInject.DetectLoaderMods) are NOT swept here — once the loader's proxy DLL is moved
        // out, the loader won't load and the game is vanilla-playable. Per the spec's return-to-vanilla
        // honesty caveat, those loose files may remain (the off-boarding sheet says so).
        foreach (var di in DirectInject.Detect(fileNames, dirNames))
        {
            // di.Entries are basenames relative to the play folder (same list that was passed in).
            foreach (var rel in di.Entries)
            {
                var srcAbs = Path.Combine(playFolder, rel);
                var destAbs = Path.Combine(gameArchiveDir, "vanilla-moved", rel);
                if (File.Exists(srcAbs))
                {
                    var size = new FileInfo(srcAbs).Length;
                    var sha = FileTally.Sha256(srcAbs);
                    SafeMove.Move(srcAbs, destAbs);
                    moved.Add(new MovedFile(rel, size, sha));
                }
                else if (Directory.Exists(srcAbs))
                {
                    var size = FileTally.ByteSize(srcAbs);
                    SafeMove.Move(srcAbs, destAbs);
                    moved.Add(new MovedFile(rel, size, null));
                }
            }
        }
        return moved;
    }

    private static void UninstallFrameworks(GameContext c)
    {
        foreach (var fw in FrameworkRegistry.List(c.DataDir))
            FrameworkRegistry.Uninstall(c.DataDir, fw.FrameworkId, c.GameRoot);
    }

    private static void FlipLoadersOff(GameContext c)
    {
        foreach (var m in Scanner.BuildModListAsync(c).GetAwaiter().GetResult())
        {
            if (!m.Enabled) continue;
            var abs = c.Locations.FirstOrDefault(l => l.Name == m.Location)?.Abs;
            if (abs is null) continue;
            try
            {
                if (m.Loader == "ue4ss") Ue4ssManifest.SetEnabled(abs, m.Name, enabled: false);
                else if (m.Loader == "bepinex") BepInExPlugins.SetEnabled(abs, m.Name, enable: false);
            }
            catch { /* best effort — loader manifest may be absent; vanilla is still safe */ }
        }
    }

    private static IEnumerable<string> DirectoryNames(string root)
        => Directory.Exists(root)
            ? Directory.GetDirectories(root).Select(d => Path.GetFileName(d)!)
            : Enumerable.Empty<string>();


    /// <summary>Copy the game's data dir + framework install state into the archive, and build its
    /// manifest entry. Non-destructive: the live data dir and game folder are untouched.
    /// <paramref name="gameArchiveDir"/> must be a fresh directory — if its <c>data</c>
    /// sub-directory already exists this method throws <see cref="InvalidOperationException"/>.</summary>
    public static GameArchive CaptureGame(GameCaptureInput input, string gameArchiveDir)
    {
        var c = input.Context;

        if (Directory.Exists(Path.Combine(gameArchiveDir, "data")))
            throw new InvalidOperationException(
                $"Restore-point archive dir already populated: \"{gameArchiveDir}\". Capture requires a fresh directory.");

        Directory.CreateDirectory(gameArchiveDir);

        if (Directory.Exists(c.DataDir))
            SafeMove.CopyDirVerified(c.DataDir, Path.Combine(gameArchiveDir, "data"));

        var frameworks = new List<FrameworkArchive>();
        foreach (var fw in FrameworkRegistry.List(c.DataDir))
        {
            var capturedRel = Path.Combine("frameworks-state", fw.FrameworkId);
            var capturedAbs = Path.Combine(gameArchiveDir, capturedRel);
            foreach (var rel in fw.InstalledFiles)
            {
                var srcAbs = Path.Combine(fw.InstallPath, rel);
                if (File.Exists(srcAbs)) SafeMove.CopyFileVerified(srcAbs, Path.Combine(capturedAbs, rel));
            }
            frameworks.Add(new FrameworkArchive(fw.FrameworkId, fw.DisplayName, fw.Author,
                fw.InstallPath, fw.InstalledFiles, capturedRel));
        }

        var modList = Scanner.BuildModListAsync(c).GetAwaiter().GetResult();
        var meta = Scanner.LoadMetadata(c);

        var mods = new List<ArchivedMod>();
        var loaderMods = new List<LoaderModState>();
        foreach (var m in modList)
        {
            // Mod.Base is only populated by ListWithClass (variant parsing); BuildModListAsync leaves
            // it empty. Derive the variant-stripped base ourselves — MergeMetadata keys by base, so
            // a mod named "MoreStamina_5x" must look up metadata under "MoreStamina", not the full name.
            var metaKey = Variant.ParseVariant(m.Name).Base;
            if (string.IsNullOrEmpty(metaKey)) metaKey = m.Name;
            meta.TryGetValue(metaKey, out var md);
            mods.Add(new ArchivedMod(m.Name, m.Enabled, md?.Url, md?.SourceConfidence, md?.InstalledUtc?.ToString("o")));
            if (m.Loader is "ue4ss" or "bepinex")
                loaderMods.Add(new LoaderModState(m.Name, m.Loader, m.Enabled));
        }

        var ownedMods = new List<OwnedModNote>();
        foreach (var loc in c.Locations)
        {
            var owner = ToolOwnership.Detect(loc.Abs);
            if (owner is null) continue;
            foreach (var m in modList)
                if (m.ReadOnly && m.Location == loc.Name)
                    ownedMods.Add(new OwnedModNote(m.Name, owner.ToString()!));
        }

        return new GameArchive(
            Id: input.Game.Id,
            GameName: input.Game.GameName,
            GameRoot: c.GameRoot,
            EndState: input.EndState,
            LaunchTargets: input.Game.LaunchTargets,
            RequiredLauncher: input.Game.RequiredLauncher,
            Frameworks: frameworks,
            LoaderMods: loaderMods,
            OwnedMods: ownedMods,
            MovedFiles: Array.Empty<MovedFile>(),
            Mods: mods,
            OffboardingSheetGameFolderPath: null);
    }
}
