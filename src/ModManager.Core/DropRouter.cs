using System.IO.Compression;
using ModManager.Core.Frameworks;
using ModManager.Core.Tools;

namespace ModManager.Core;

/// <summary>Which branch of the drop a game takes once frameworks are handled.</summary>
public enum DropLane
{
    /// <summary>A Mod Engine 2 game: mods are registered in its config, and drop-to-install isn't wired.</summary>
    ModEngine2,
    /// <summary>A FromSoft game without ME2: loose files placed into the play folder.</summary>
    DirectInject,
    /// <summary>Every other game: save mods, then UE4SS Lua mods, then tools, then the mod folder.</summary>
    ModFolder,
}

/// <summary>Where one dropped path goes.</summary>
public enum DropRoute
{
    /// <summary>A catalog framework (installed into the game, with a backup snapshot).</summary>
    Framework,
    /// <summary>Not handled: the ME2 lane doesn't install drops yet.</summary>
    NotInstalled,
    /// <summary>A save/world mod (into the save folder).</summary>
    SaveMod,
    /// <summary>A UE4SS Lua mod (into ue4ss\Mods when 626 owns UE4SS).</summary>
    Ue4ssLua,
    /// <summary>A recognised tool (extracted under the data dir and registered).</summary>
    Tool,
    /// <summary>Regular mod intake for the lane (<see cref="DropPlan.Intake"/> says file by file).</summary>
    Mod,
}

/// <summary>One dropped path and its route. <see cref="LooksLikeFramework"/> marks an archive the app
/// stops to ask about first (cancel drops it; continuing sends it down <see cref="Route"/>). The other
/// fields are filled for the route that uses them.</summary>
public sealed record DropItem(
    string Path,
    DropRoute Route,
    bool LooksLikeFramework = false,
    KnownFramework? Framework = null,
    string? FrameworkInstallRoot = null,
    IReadOnlyList<string>? FrameworkFiles = null,
    IReadOnlyList<string>? FrameworkOverwrites = null,
    SaveModDropVerdict? SaveMod = null,
    string? LuaModFolder = null,
    KnownTool? Tool = null,
    string? Problem = null);

/// <summary>
/// Everything a drop would do, decided without writing. <see cref="Blocked"/> means nothing installs
/// (no mod folder at all). <see cref="NeedsBanRiskAcknowledgment"/> is the whole-batch gate: a high
/// ban-risk game the user has not acknowledged installs nothing until they do. <see cref="Intake"/> is
/// the lane's plan for the <see cref="DropRoute.Mod"/> paths: what is new, what would replace an
/// existing file, and what is refused. <see cref="IntakeProblem"/> is set when that planning failed (a
/// corrupt archive): the app's drop reports the same error when it reaches that step.
/// </summary>
public sealed record DropPlan(
    string? Blocked,
    bool NeedsBanRiskAcknowledgment,
    DropLane Lane,
    IReadOnlyList<DropItem> Items,
    IntakePlan Intake,
    string? Ue4ssModsDir,
    string? IntakeProblem = null)
{
    public IEnumerable<DropItem> Of(DropRoute route) => Items.Where(i => i.Route == route);
}

/// <summary>
/// The drop, decided in one place (E1, fourth slice). The app's drop (<c>MainViewModel.AddModsAsync</c>)
/// acts on this plan: it asks its questions (framework confirm, the looks-like-a-framework nudge,
/// replacements, the ban-risk and save-write prompts) and then installs each item down its route. The
/// agent's <c>dry_run_intake</c> returns the same plan, and <c>intake</c> carries it out under the agent
/// rules. So a dry run describes the drop the app would perform, not a guess at it.
///
/// <para>The order is the app's, and load-bearing: frameworks first in every lane (a loader going live
/// is what anti-cheat sees), then the lane. In the mod-folder lane each path is a save mod, else a UE4SS
/// Lua mod, else a tool, else a regular mod, the first that claims it.</para>
///
/// <para>Pure apart from reading the dropped archives and checking which files exist. Nothing is
/// written.</para>
/// </summary>
public static class DropRouter
{
    public static DropPlan Plan(GameContext ctx, IReadOnlyList<string> paths, IArchiveReader? archiveReader = null)
    {
        archiveReader ??= new SharpCompressArchiveReader();
        var game = ctx.Game;
        var blocked = ModListing.HasNoModLane(ctx) ? ModListEmptyState.NoModLane : null;
        var needsAck = BanRiskRules.ShouldGateEnable(BanRiskCatalog.Effective(game), BanRiskAckStore.IsAcked(ctx.DataDir, game.Id ?? ""));
        var lane = ModEngine2Listing.IsConfigBacked(game) ? DropLane.ModEngine2
            : DirectInjectListing.Applies(game) ? DropLane.DirectInject
            : DropLane.ModFolder;

        var ue4ssModsDir = FrameworkRegistry.List(ctx.DataDir)
            .FirstOrDefault(m => string.Equals(m.FrameworkId, "ue4ss", StringComparison.OrdinalIgnoreCase)) is { } owned
            ? Path.Combine(owned.InstallPath, "ue4ss", "Mods")
            : null;

        var saveRefusal = SaveWritePolicy.Refusal(game);
        var saveTypeExts = string.IsNullOrEmpty(ctx.SaveDir)
            ? null
            : GameSaveTypesCatalog.Resolve(game).SaveTypes.Select(t => t.Extension).ToList();

        var items = new List<DropItem>();
        foreach (var path in paths)
        {
            var (fw, looksLike) = FrameworkOf(path, game);
            if (fw is not null)
            {
                items.Add(FrameworkItem(path, fw, ctx));
                continue;
            }
            items.Add(LaneItem(path, lane, ctx, saveTypeExts, saveRefusal, ue4ssModsDir, archiveReader) with { LooksLikeFramework = looksLike });
        }

        var modPaths = items.Where(i => i.Route == DropRoute.Mod).Select(i => i.Path).ToList();
        // Planning never throws: the app plans before its drop's try block. A corrupt archive throws in
        // the lane's planner, and the app's drop meets that same error when it reaches the step.
        IntakePlan intake;
        string? intakeProblem = null;
        try
        {
            intake = blocked is not null ? EmptyIntake : lane switch
            {
                DropLane.DirectInject => DirectInjectListing.PlayFolder(ctx.GameRoot) is { } play
                    ? DirectInject.Plan(play, modPaths)
                    : EmptyIntake,
                DropLane.ModFolder => modPaths.Count == 0 ? EmptyIntake : Scanner.PlanIntake(modPaths, ctx),
                _ => EmptyIntake,
            };
        }
        catch (Exception e)
        {
            intake = EmptyIntake;
            intakeProblem = ErrorRemedy.Describe(e);
        }

        return new DropPlan(blocked, needsAck, lane, items, intake, ue4ssModsDir, intakeProblem);
    }

    private static readonly IntakePlan EmptyIntake =
        new(Array.Empty<IntakeItem>(), Array.Empty<IntakeCollision>(), Array.Empty<SkippedItem>());

    /// <summary>The framework check the app runs first: a zip (and only a zip; 7z and rar are not
    /// peeked) whose entries match the catalog for this engine and store id, or that looks like an
    /// unrecognised framework.</summary>
    public static (KnownFramework? Match, bool LooksLike) FrameworkOf(string path, GameEntry game)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || !IsArchive(path)) return (null, false);
        IReadOnlyList<string> entries;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            entries = zip.Entries.Select(e => e.FullName).ToList();
        }
        catch { return (null, false); }   // can't peek: regular intake tries it
        var c = KnownFramework.Classify(entries, game.Engine ?? "", game.SteamAppId);
        return (c.Match, c.Match is null && c.LooksLikeFramework);
    }

    private static DropItem FrameworkItem(string path, KnownFramework fw, GameContext ctx)
    {
        var relPaths = ctx.Game.ModLocations.Select(l => l.Path).ToList();
        var root = FrameworkInstaller.ResolveInstallRoot(fw.InstallRoot, ctx.GameRoot ?? "", relPaths);
        if (root is null)
            return new DropItem(path, DropRoute.Framework, Framework: fw,
                Problem: $"Couldn't install {fw.DisplayName}: no project subfolder found in the game's mod locations. "
                         + "Re-scan the game's mod folders and try again.");

        IReadOnlyList<string> files;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            files = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).Where(e => !e.EndsWith('/')).ToList();
        }
        catch { files = Array.Empty<string>(); }
        // Checked where the installer will write, so the confirm shows the truth about what it replaces.
        var overwrites = files.Where(e => File.Exists(Path.Combine(root, e))).ToList();
        return new DropItem(path, DropRoute.Framework, Framework: fw, FrameworkInstallRoot: root,
            FrameworkFiles: files, FrameworkOverwrites: overwrites);
    }

    private static DropItem LaneItem(string path, DropLane lane, GameContext ctx, IReadOnlyList<string>? saveTypeExts,
        string? saveRefusal, string? ue4ssModsDir, IArchiveReader archiveReader)
    {
        if (lane == DropLane.ModEngine2)
            return new DropItem(path, DropRoute.NotInstalled, Problem: ModEngine2DropNotice);
        if (lane == DropLane.DirectInject) return new DropItem(path, DropRoute.Mod);

        // A save mod is only looked for when the game has a save folder, as in the app.
        if (saveTypeExts is not null)
        {
            var v = SaveModFlow.Classify(path, saveTypeExts, saveRefusal);
            if (v.Outcome != SaveModDropOutcome.NotASaveMod)
                return new DropItem(path, DropRoute.SaveMod, SaveMod: v,
                    Problem: v.Outcome == SaveModDropOutcome.Failed ? v.Reason : null);
        }

        if (File.Exists(path) && IsArchive(path))
        {
            try
            {
                using var arch = archiveReader.Open(path);
                var lua = Ue4ssLuaDetect.Detect(arch.EntryNames);
                if (lua.IsLuaMod)
                    return new DropItem(path, DropRoute.Ue4ssLua,
                        LuaModFolder: lua.ModFolderName ?? Path.GetFileNameWithoutExtension(path),
                        Problem: ue4ssModsDir is null
                            ? "A UE4SS Lua mod, but 626 doesn't manage this game's UE4SS, so it can't be installed here. Install it into UE4SS's Mods folder by hand."
                            : null);
            }
            catch { /* unreadable here: the next checks get it */ }

            try
            {
                var (cls, known) = ToolDetector.Classify(path, ctx.Game.Engine ?? "", ctx.Game.SteamAppId ?? "");
                if (cls == ToolClassification.Tool) return new DropItem(path, DropRoute.Tool, Tool: known);
            }
            catch (Exception e)
            {
                // The app carves a tool that fails here out of mod intake, so an exe-only zip is never
                // classified as a mod.
                return new DropItem(path, DropRoute.Tool, Problem: e.Message);
            }
        }

        return new DropItem(path, DropRoute.Mod);
    }

    /// <summary>The line the app shows for a Mod Engine 2 game's drop.</summary>
    public const string ModEngine2DropNotice =
        "For Mod Engine 2 games, place the mod's folder under the ME2 'mod' folder, then add it in the config. Auto-install is coming.";

    public static bool IsArchive(string path)
    {
        var lower = path.ToLowerInvariant();
        return Intake.ArchiveExtensions.Any(a => lower.EndsWith(a));
    }
}
