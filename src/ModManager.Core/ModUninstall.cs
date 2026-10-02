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
    /// The confirm dialog's sentences about held folders, or null when nothing is held. Readable folders with
    /// trees make one sentence naming the trees (a family's merged, each once). A readable folder whose files
    /// fit no declared tree gets its own sentence naming its path, never spliced into the tree list. Each
    /// unreadable folder adds a sentence naming its path. "its"/"it" for one mod, "their"/"them" for several.
    /// </summary>
    public string? HeldSentence()
    {
        var (its, it) = Mods.Count > 1 ? ("their", "them") : ("its", "it");
        var sentences = new List<string>();
        var trees = HeldFolders.Where(h => !h.Unreadable).SelectMany(h => h.Trees)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (trees.Count > 0)
            sentences.Add($"626 is also holding some of {its} files in {string.Join(", ", trees)}, and will delete those too.");
        foreach (var h in HeldFolders.Where(h => !h.Unreadable && h.Trees.Count == 0))
            sentences.Add($"626 is also holding files for {it} in {h.Path} and will delete those too.");
        foreach (var h in HeldFolders.Where(h => h.Unreadable))
            sentences.Add($"626 couldn't read {h.Path} to see what it's holding for {it}; anything there will be deleted too.");
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
                TreeHolding.BeforeReadForTests?.Invoke(dir);
                if (!HeldPresent(ctx, m.Name)) continue;
                // The same no-following rule as the delete: a link counts as something held, but nothing
                // behind it is read, so no tree is named from a link's target.
                if (!LinkSafeDelete.HoldsAnything(dir)) continue;
                var trees = LinkSafeDelete.IsLink(new DirectoryInfo(dir))
                    ? Array.Empty<string>()
                    : TreeHolding.Held(ctx, m.Name, ctx.ExtraModTrees)
                        .Where(e => !ThroughLink(dir, e.AbsPath))
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

    // True when the entry, or any folder between the held folder and it, is a link.
    private static bool ThroughLink(string heldDir, string path)
    {
        for (var p = path; p is not null && p.Length > heldDir.Length; p = Path.GetDirectoryName(p))
        {
            FileAttributes attrs;
            try { attrs = File.GetAttributes(p); }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { continue; }
            if (attrs.HasFlag(FileAttributes.ReparsePoint)) return true;
        }
        return false;
    }

    /// <summary>Delete the mod. See <see cref="RunAll"/>.</summary>
    public static IReadOnlyList<string> Run(GameContext ctx, Mod mod) => RunAll(ctx, new[] { mod });

    /// <summary>
    /// Delete several mods (a variant family) as one decision. Each mod loses its live files from every
    /// location and mirror, any held (disabled) copy, and the install records that claimed them; for a
    /// Mod Engine 2 game, its folder and its config entry. Then that mod's <c>disabled-trees/&lt;Mod&gt;</c>
    /// folder goes too: a turned-off mod's held extras, or a live mod's leftovers.
    ///
    /// <para>Every mod is checked first, so a refused member stops the whole family before anything is
    /// deleted. The checks are <see cref="Refusal(GameContext, Mod)"/> and containment (<see cref="HeldDir"/>):
    /// the name must name a folder directly inside the holding root, which must not be a link. The folder is
    /// only touched when the root lists an entry by that real name, so an 8.3 alias never reaches it.</para>
    ///
    /// <para>Each mod's held folder goes right after that mod's own uninstall succeeds, so a failure there
    /// leaves it intact and the mod still listed, and a later member's failure leaves no orphan. It is deleted
    /// file by file and then folder by folder, deepest first, never recursively; a junction or symlink is
    /// removed as the link, and its target is never touched. Afterwards it is checked. A held folder that
    /// can't be fully deleted doesn't stop the run: once every mod is done, one
    /// <see cref="HeldFolderLeftException"/> names every folder left, with the first cause inside. Errors
    /// in the main uninstall surface as before.</para>
    /// </summary>
    /// <returns>The held folders that existed and were deleted.</returns>
    public static IReadOnlyList<string> RunAll(GameContext ctx, IReadOnlyList<Mod> mods)
    {
        var lane = ModListing.MechanismFor(ctx.Game, ctx);
        foreach (var m in mods)
            if (Refusal(lane, m) is { } why) throw new InvalidOperationException(why.Message);
        // Containment before anything is deleted: a name that escapes stops the whole run here.
        var heldDirs = mods.Select(m => string.IsNullOrEmpty(m.Name) ? null : HeldDir(ctx, m.Name)).ToList();

        var deleted = new List<string>();
        var left = new List<(string Mod, string Path)>();
        Exception? firstFailure = null;
        for (var i = 0; i < mods.Count; i++)
        {
            var m = mods[i];
            if (lane == ListingMechanism.ModEngine2) ModEngine2Writer.RemoveMod(ctx.Game, m.Name);
            else Scanner.UninstallMod(m.Name, ctx);

            // This mod's held folder goes right after its own uninstall, so a later member that fails leaves
            // no orphan behind. A folder that can't be fully deleted is recorded and the run carries on: the
            // mod is already gone, and stopping would only leave more behind.
            if (heldDirs[i] is not { } dir) continue;
            try
            {
                if (!HeldPresent(ctx, m.Name)) continue;
                DeleteHeldFolder(dir);
                if (LinkSafeDelete.HoldsAnything(dir)) left.Add((m.Name, dir));
                else deleted.Add(dir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                firstFailure ??= e;
                left.Add((m.Name, dir));
            }
        }
        if (left.Count > 0) throw new HeldFolderLeftException(left, deleted, firstFailure);
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
    /// The mod's holding folder, refused (throws) unless the name names a folder directly inside
    /// <see cref="TreeHolding.Root"/>: the resolved path's parent is the root and its last segment is the name,
    /// unchanged. That rejects separators, <c>..</c>, <c>.</c> and trailing dots or spaces, which path
    /// resolution would otherwise quietly normalise onto another mod's folder. The root must sit strictly under
    /// the data folder and must not be a link. The folder itself may be a link: the delete removes it as one.
    /// </summary>
    internal static string HeldDir(GameContext ctx, string modName)
    {
        var dataDir = Path.GetFullPath(ctx.DataDir);
        var root = Path.GetFullPath(TreeHolding.Root(ctx)).TrimEnd(Path.DirectorySeparatorChar);
        // Built by joining, never by resolving: GetFullPath expands an existing folder's 8.3 alias, so a mod
        // named OTHERL~1 would come back as another mod's long-named folder. The name is checked as text
        // instead (the same rejections as comparing the resolved last segment, without the expansion), and
        // the parent is checked on the resolved form.
        var dir = Path.Combine(root, modName);
        if (!StrictlyUnder(root, dataDir)
            || !NamesOneFolder(modName)
            || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(dir)), root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"626 won't delete \"{dir}\" for \"{modName}\": that name doesn't name a folder directly inside "
                + $"626's holding folder {root}. Nothing was changed.");

        if (Directory.Exists(root) && LinkSafeDelete.IsLink(new DirectoryInfo(root)))
            throw new InvalidOperationException(
                $"626 won't delete \"{dir}\" for \"{modName}\": the holding folder {root} is a link, so it may lead "
                + "somewhere else. Nothing was changed.");
        return dir;
    }

    /// <summary>
    /// True when the holding root has an entry whose real name is the mod's name (case-insensitive, as
    /// Windows is). Listed without a search pattern, so an 8.3 short name never matches: a mod literally named
    /// <c>OTHERL~1</c> does not reach <c>Other Long Name Mod</c>, though opening that path would.
    /// </summary>
    private static bool HeldPresent(GameContext ctx, string modName)
    {
        var root = TreeHolding.Root(ctx);
        if (!Directory.Exists(root)) return false;
        return new DirectoryInfo(root).EnumerateFileSystemInfos()
            .Any(e => string.Equals(e.Name, modName, StringComparison.OrdinalIgnoreCase));
    }

    // One path segment that Windows keeps exactly as written: no separators or other invalid characters (':'
    // included, so no alternate stream), not "." or "..", and no trailing dot or space, which Windows strips.
    private static bool NamesOneFolder(string name)
        => name.Length > 0 && name is not ("." or "..")
           && !name.EndsWith('.') && !name.EndsWith(' ')
           && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static bool StrictlyUnder(string path, string parent)
    {
        var withSep = parent.EndsWith(Path.DirectorySeparatorChar) ? parent : parent + Path.DirectorySeparatorChar;
        return path.Length > withSep.Length && path.StartsWith(withSep, StringComparison.OrdinalIgnoreCase);
    }
}


/// <summary>
/// The mods were uninstalled, but some of what 626 was holding for them could not be deleted (a file in use,
/// a permission). Thrown once at the end of the run, naming every folder left; the first cause is the
/// <see cref="Exception.InnerException"/>.
/// </summary>
public sealed class HeldFolderLeftException : IOException
{
    public HeldFolderLeftException(IReadOnlyList<(string Mod, string Path)> left, IReadOnlyList<string> deleted, Exception? inner)
        : base(MessageFor(left), inner)
    {
        Left = left.Select(l => l.Path).ToArray();
        Deleted = deleted.ToArray();
    }

    /// <summary>The held folders still there.</summary>
    public IReadOnlyList<string> Left { get; }

    /// <summary>The held folders that were deleted.</summary>
    public IReadOnlyList<string> Deleted { get; }

    public static string MessageFor(IReadOnlyList<(string Mod, string Path)> left)
        => left.Count == 1
            ? $"{left[0].Mod} was uninstalled, but 626 couldn't delete everything it was holding for it in {left[0].Path}. "
              + "Close anything using those files and delete the folder, or try again."
            : $"{JoinAnd(left.Select(l => l.Mod))} were uninstalled, but 626 couldn't delete everything it was holding for them in "
              + $"{JoinAnd(left.Select(l => l.Path))}. Close anything using those files and delete the folders, or try again.";

    private static string JoinAnd(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1];
    }
}
