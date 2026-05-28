using ModManager.Core.Frameworks;

namespace ModManager.Core.RestorePoints;

/// <summary>Hydrated inputs the engine needs to capture one game. All derivable from Core; the App
/// supplies the GameEntry + a built GameContext and picks the end-state.</summary>
public sealed record GameCaptureInput(GameEntry Game, GameContext Context, string EndState);

/// <summary>
/// The headless Safe Clear / Restore file engine. Takes explicit archive paths — no %APPDATA%
/// knowledge, no UI. Composes Phase 0 primitives (SafeMove, PathGate) + existing Core. The App
/// orchestrator (Phase 1B) calls these in the Law-A order: capture-all -> seal -> mutate-all.
/// </summary>
public static partial class RestorePointEngine
{
    /// <summary>Copy the game's data dir + framework install state into the archive, and build its
    /// manifest entry. Non-destructive: the live data dir and game folder are untouched.</summary>
    public static GameArchive CaptureGame(GameCaptureInput input, string gameArchiveDir)
    {
        var c = input.Context;
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
