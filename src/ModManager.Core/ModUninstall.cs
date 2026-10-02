namespace ModManager.Core;

/// <summary>Why a mod can't be uninstalled.</summary>
public enum UninstallBlock
{
    /// <summary>Loose files in the game folder (direct-inject, loose-root): 626 never deletes those.</summary>
    LooseFiles,
    /// <summary>A mod another tool manages: uninstall it there.</summary>
    ManagedByAnotherTool,
    /// <summary>A row the listing appends that is not an installed mod (a proxy loader DLL, a shared
    /// library): there is nothing of its own to delete by name. Turn it off instead.</summary>
    NotAnInstalledMod,
}

public sealed record UninstallRefusal(UninstallBlock Kind, string Message);

/// <summary>One mod an uninstall will delete: its name and its files, as the listing reports them.</summary>
public sealed record UninstallPreviewMod(string Name, IReadOnlyList<string> Files);

/// <summary>
/// A mod's <c>disabled-trees/&lt;Mod&gt;</c> folder (B4 stage two) that an uninstall will delete with it.
/// <paramref name="Trees"/> are the declared extra trees it holds entries under, in the manifest's spelling;
/// empty when its files fit no tree the game still declares. <paramref name="Unreadable"/> means the folder
/// could not be read, so what it holds is unknown, and it is listed rather than guessed about.
/// </summary>
public sealed record UninstallHeldFolder(string ModName, string Path, IReadOnlyList<string> Trees, bool Unreadable);

/// <summary>What an uninstall will delete, worked out before asking the user. Read-only.</summary>
public sealed record UninstallPreview(IReadOnlyList<UninstallPreviewMod> Mods, IReadOnlyList<UninstallHeldFolder> HeldFolders)
{
    /// <summary>True when any held folder couldn't be read.</summary>
    public bool HeldFolderUnreadable => HeldFolders.Any(h => h.Unreadable);

    /// <summary>
    /// The confirm dialog's sentence about held folders, or null when nothing is held. Readable folders make
    /// one sentence naming their trees (a family's, merged, each once), or the folder's path when its files
    /// fit no declared tree; each unreadable folder adds a sentence naming its path.
    /// </summary>
    public string? HeldSentence()
    {
        var sentences = new List<string>();
        var readable = HeldFolders.Where(h => !h.Unreadable).ToList();
        if (readable.Count > 0)
        {
            var places = readable.SelectMany(h => h.Trees).Distinct(StringComparer.OrdinalIgnoreCase)
                .Concat(readable.Where(h => h.Trees.Count == 0).Select(h => h.Path)).ToList();
            sentences.Add($"626 is also holding some of its files in {string.Join(", ", places)}, and will delete those too.");
        }
        foreach (var h in HeldFolders.Where(h => h.Unreadable))
            sentences.Add($"626 couldn't read {h.Path} to see what it's holding for it; anything there will be deleted too.");
        return sentences.Count == 0 ? null : string.Join(" ", sentences);
    }
}

/// <summary>
/// Uninstalling a mod: whether it may be, what it will delete, and doing it. The one destructive mod
/// operation. The app's row (whether it offers Uninstall), its uninstall and family uninstall, and the
/// agent's uninstall_mod all ask here, so the agent can never delete what the app would not (E1, fifth slice).
///
/// <para>B4 stage two holds a turned-off mod's extra-tree entries in <c>disabled-trees/&lt;Mod&gt;</c>, and a live
/// mod can have leftovers there. Uninstall used to refuse such a mod. Este, 2026-10-02: "let them know what
/// it's going to do and let them choose to cancel or to proceed." So <see cref="Preview(GameContext, IReadOnlyList{Mod})"/>
/// names the held folder before the confirm, and <see cref="RunAll"/> deletes it with the mod.</para>
/// </summary>
public static class ModUninstall
{
    /// <summary>Why this mod can't be uninstalled, or null when it can.</summary>
    public static UninstallRefusal? Refusal(GameContext ctx, Mod mod) => Refusal(ModListing.MechanismFor(ctx.Game, ctx), mod);

    /// <summary>The lane's rules, for the row deciding whether to offer Uninstall without touching disk per
    /// row. Held extra-tree files are not a refusal: the preview names them and the uninstall deletes them.</summary>
    /// <param name="lane">The game's lane, worked out once by a caller asking about many mods.</param>
    public static UninstallRefusal? Refusal(ListingMechanism lane, Mod mod)
    {
        if (lane is ListingMechanism.DirectInject or ListingMechanism.LooseRoot)
            return new(UninstallBlock.LooseFiles,
                $"\"{mod.Name}\" is loose files in the game folder, which 626 never deletes. Turn it off instead "
                + "(its files move aside and can come back), or remove them by hand.");
        if (mod.ReadOnly)
            return new(UninstallBlock.ManagedByAnotherTool, $"\"{mod.Name}\" is managed by another tool. Uninstall it there.");
        // Appended by the listing on any lane, so the scanner's uninstall cannot find them by name and
        // would report success having deleted nothing (the trap ModToggle routes around for toggles).
        if (mod.Location == ProxyLoaderRows.LocationTag || mod.Class == "library")
            return new(UninstallBlock.NotAnInstalledMod,
                $"\"{mod.Name}\" is a loader or shared library 626 lists alongside your mods, not an installed mod. "
                + "Turn it off instead.");
        return null;
    }

    /// <summary>What uninstalling this mod will delete. See <see cref="Preview(GameContext, IReadOnlyList{Mod})"/>.</summary>
    public static UninstallPreview Preview(GameContext ctx, Mod mod) => Preview(ctx, new[] { mod });

    /// <summary>
    /// What uninstalling these mods will delete: each mod's files, and each <c>disabled-trees/&lt;Mod&gt;</c>
    /// folder that holds files, with the declared trees it holds them under. A folder whose files fit no
    /// declared tree is listed anyway, with no trees. A folder that can't be read is listed as
    /// <see cref="UninstallHeldFolder.Unreadable"/> rather than skipped: uninstall will still try to delete it.
    /// Read-only. Throws <see cref="InvalidOperationException"/> for a mod name that would resolve outside the
    /// holding root, as <see cref="RunAll"/> does.
    /// </summary>
    public static UninstallPreview Preview(GameContext ctx, IReadOnlyList<Mod> mods)
    {
        var listed = mods.Select(m => new UninstallPreviewMod(m.Name, m.Files.ToArray())).ToArray();
        var held = new List<UninstallHeldFolder>();
        foreach (var m in mods)
        {
            if (string.IsNullOrEmpty(m.Name)) continue;
            var dir = HeldDir(ctx, m.Name);
            try
            {
                if (!TreeHolding.HoldsFiles(ctx, m.Name)) continue;
                var trees = TreeHolding.Held(ctx, m.Name, ctx.ExtraModTrees)
                    .Select(e => e.Tree).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                held.Add(new UninstallHeldFolder(m.Name, dir, trees, Unreadable: false));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                held.Add(new UninstallHeldFolder(m.Name, dir, Array.Empty<string>(), Unreadable: true));
            }
        }
        return new UninstallPreview(listed, held.ToArray());
    }

    /// <summary>Delete the mod. See <see cref="RunAll"/>.</summary>
    public static IReadOnlyList<string> Run(GameContext ctx, Mod mod) => RunAll(ctx, new[] { mod });

    /// <summary>
    /// Delete several mods (a variant family) as one decision. Each mod loses its live files from every
    /// location and mirror, any held (disabled) copy, and the install records that claimed them; for a
    /// Mod Engine 2 game, its folder and its config entry. Then each mod's <c>disabled-trees/&lt;Mod&gt;</c>
    /// folder goes too: a turned-off mod's held extras, or a live mod's leftovers.
    ///
    /// <para>Every mod is checked first, so a refused member stops the whole family before anything is
    /// deleted. The checks are <see cref="Refusal(GameContext, Mod)"/> and containment: each held folder must
    /// resolve strictly under the holding root inside the data folder, with no link above it.</para>
    ///
    /// <para>The held folders go only after the main uninstall has succeeded, so a failure there leaves them
    /// intact and the mod still listed. They are deleted file by file and then folder by folder, deepest
    /// first, never recursively; a junction or symlink is removed as the link, and its target is never
    /// touched. Afterwards each is checked, and one that still holds anything throws an
    /// <see cref="IOException"/> naming it. Locked-file errors surface.</para>
    /// </summary>
    /// <returns>The held folders that existed and were deleted.</returns>
    public static IReadOnlyList<string> RunAll(GameContext ctx, IReadOnlyList<Mod> mods)
    {
        var lane = ModListing.MechanismFor(ctx.Game, ctx);
        foreach (var m in mods)
            if (Refusal(lane, m) is { } why) throw new InvalidOperationException(why.Message);
        // Containment before anything is deleted: a name that escapes stops the whole run here.
        var heldDirs = mods.Where(m => !string.IsNullOrEmpty(m.Name)).Select(m => HeldDir(ctx, m.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var m in mods)
        {
            if (lane == ListingMechanism.ModEngine2) ModEngine2Writer.RemoveMod(ctx.Game, m.Name);
            else Scanner.UninstallMod(m.Name, ctx);
        }

        var deleted = new List<string>();
        foreach (var dir in heldDirs)
        {
            if (!Directory.Exists(dir) && !File.Exists(dir)) continue;
            DeleteHeldFolder(dir);
            if (LinkSafeDelete.HoldsAnything(dir))
                throw new IOException($"The mod was uninstalled, but files are still held in {dir}. Delete that folder by hand, or close what is using it and try again.");
            deleted.Add(dir);
        }
        return deleted;
    }

    /// <summary>Tests only: replaces the held-folder delete, so a test can prove the check afterwards
    /// catches a delete that did nothing. Thread-static, like the toggle's own test hooks.</summary>
    [ThreadStatic] internal static Action<string>? DeleteHeldForTests;

    private static void DeleteHeldFolder(string dir)
    {
        if (DeleteHeldForTests is { } hook) { hook(dir); return; }
        LinkSafeDelete.DeleteTree(dir);
    }

    /// <summary>
    /// The mod's holding folder, refused (throws) unless it sits strictly under <see cref="TreeHolding.Root"/>,
    /// which sits strictly under the data folder, with no link from the root down to it. The folder itself
    /// may be a link: the delete removes it as one.
    /// </summary>
    internal static string HeldDir(GameContext ctx, string modName)
    {
        var dataDir = Path.GetFullPath(ctx.DataDir);
        var root = Path.GetFullPath(TreeHolding.Root(ctx));
        var dir = Path.GetFullPath(TreeHolding.ModDir(ctx, modName));
        if (!StrictlyUnder(root, dataDir) || !StrictlyUnder(dir, root))
            throw new InvalidOperationException(
                $"626 won't delete \"{dir}\" for \"{modName}\": it is not inside 626's holding folder {root}. Nothing was changed.");

        for (var p = Path.GetDirectoryName(dir); p is not null && p.Length >= root.Length; p = Path.GetDirectoryName(p))
            if (Directory.Exists(p) && LinkSafeDelete.IsLink(new DirectoryInfo(p)))
                throw new InvalidOperationException(
                    $"626 won't delete \"{dir}\" for \"{modName}\": \"{p}\" is a link, so it may lead outside the holding folder. Nothing was changed.");
        return dir;
    }

    private static bool StrictlyUnder(string path, string parent)
    {
        var withSep = parent.EndsWith(Path.DirectorySeparatorChar) ? parent : parent + Path.DirectorySeparatorChar;
        return path.Length > withSep.Length && path.StartsWith(withSep, StringComparison.OrdinalIgnoreCase);
    }
}
