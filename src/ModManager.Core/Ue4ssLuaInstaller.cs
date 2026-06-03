namespace ModManager.Core;

/// <summary>The result of installing a UE4SS Lua mod: the leaf folder name it landed under in
/// ue4ss\Mods (the value the manifest + the launcher row key on).</summary>
public sealed record Ue4ssLuaInstallResult(string ModName, IReadOnlyList<string> InstalledFiles);

/// <summary>
/// Installs a UE4SS Lua-mod archive into a launcher-owned <c>ue4ss\Mods</c> folder. Validate-then-extract:
/// classify the archive (must be a Lua mod), plan every destination path through <see cref="PathGate"/>,
/// extract to a staging dir, then atomically move the finished mod folder into place. Nothing is written
/// to the live Mods folder until the staged copy is complete, so a mid-extract failure leaves it untouched.
/// Reversible: <see cref="Uninstall"/> removes exactly the mod's own folder, never a sibling.
///
/// Reads through <see cref="IArchiveReader"/> so zip / 7z / rar all work — same seam as the rest of intake.
/// Pure Core — no Electron, no WinUI.
/// </summary>
public static class Ue4ssLuaInstaller
{
    // Fixed prefix (no modName in it) so the crash-orphan reaper has a clean glob. The leading dot keeps
    // it out of normal mod listings; the GUID suffix makes concurrent installs collision-free.
    private const string StagingPrefix = ".staging-";

    public static Ue4ssLuaInstallResult Install(string archivePath, string ue4ssModsDir, IArchiveReader? reader = null)
    {
        if (string.IsNullOrEmpty(archivePath)) throw new ArgumentException("archivePath empty", nameof(archivePath));
        if (string.IsNullOrEmpty(ue4ssModsDir)) throw new ArgumentException("ue4ssModsDir empty", nameof(ue4ssModsDir));
        if (!File.Exists(archivePath)) throw new FileNotFoundException("Archive missing.", archivePath);

        reader ??= new SharpCompressArchiveReader();
        using var archive = reader.Open(archivePath);
        var entryNames = archive.EntryNames;

        // 1) Classify. Must be a Lua mod (gives us the leaf name + the wrapper prefix to strip).
        var verdict = Ue4ssLuaDetect.Detect(entryNames);
        if (!verdict.IsLuaMod || verdict.ModFolderName is null || verdict.ModFolderPath is null)
            throw new InvalidOperationException(
                "That archive doesn't look like a UE4SS Lua mod (no Scripts/*.lua or enabled.txt+dlls). Nothing was changed.");

        var modName = verdict.ModFolderName;
        var modRoot = Path.Combine(ue4ssModsDir, modName);

        // Reap any staging dirs orphaned by an earlier hard crash (power loss between extract + move).
        // The scanner surfaces ue4ss\Mods as a location now, so leftover debris could render as a
        // phantom row — sweep it before installing. Best-effort; a locked dir just stays till next time.
        ReapStagingDirs(ue4ssModsDir);

        // 2) Refuse to clobber an existing mod of the same name — no silent replace (reversibility).
        if (Directory.Exists(modRoot))
            throw new InvalidOperationException(
                $"A mod folder named \"{modName}\" already exists in ue4ss\\Mods — refusing to overwrite. " +
                "Remove or rename it first. Nothing was changed.");

        var modRootFull = Path.GetFullPath(modRoot);
        var prefix = verdict.ModFolderPath.TrimEnd('/') + "/";

        // 3) Plan every payload entry under the mod folder, stripping the archive's wrapper prefix so the
        //    files re-root to ue4ss\Mods\<modName>\... . PathGate refuses traversal / drive-rooted entries.
        var planned = new List<(string EntryName, string RelUnderMod)>();
        foreach (var name in entryNames)
        {
            var norm = name.Replace('\\', '/').TrimStart('/');
            var insideWrapper = norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

            var underMod = PathGate.SafeRelative(name, stripPrefix: verdict.ModFolderPath);
            if (underMod is null)
            {
                // SafeRelative returns null for a directory entry OR an unsafe (traversal/absolute) path.
                // An unsafe entry that IS inside the mod's wrapper must abort the whole install; an entry
                // outside the wrapper (a stray sibling) is simply not part of the mod, so skip it.
                if (insideWrapper && !norm.EndsWith("/"))
                    throw new InvalidOperationException(
                        $"Archive entry '{name}' is unsafe (path traversal or absolute) — refusing install. Nothing was changed.");
                continue;
            }

            var absTarget = Path.GetFullPath(Path.Combine(modRoot, underMod));
            if (!PathGate.IsContainedAbsolute(absTarget, modRootFull) &&
                !string.Equals(absTarget, modRootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Archive entry '{name}' resolves outside the mod folder — refusing install. Nothing was changed.");

            planned.Add((name, underMod));
        }

        if (planned.Count == 0)
            throw new InvalidOperationException(
                "The archive's Lua mod folder is empty after validation — nothing to install.");

        // 4) Extract to a staging dir SIBLING to the mod folder (same drive => atomic move). Only after the
        //    whole mod is staged do we move it into place, so the live Mods folder never sees a partial mod.
        var staging = Path.Combine(ue4ssModsDir, StagingPrefix + Guid.NewGuid().ToString("N"));
        var installed = new List<string>(planned.Count);
        try
        {
            foreach (var (entryName, relUnderMod) in planned)
            {
                var stageTarget = Path.Combine(staging, relUnderMod);
                archive.Extract(entryName, stageTarget, overwrite: false);
                installed.Add(relUnderMod);
            }

            // 5) Move-in: the staged folder becomes the mod folder in one Directory.Move.
            Directory.Move(staging, modRoot);
        }
        catch
        {
            // Roll back: drop the staging dir, and the half-moved mod folder if the move itself failed.
            TryDeleteDir(staging);
            if (Directory.Exists(modRoot)) TryDeleteDir(modRoot);
            throw;
        }

        return new Ue4ssLuaInstallResult(modName, installed);
    }

    /// <summary>
    /// Identify a just-installed Lua mod against Nexus by md5-hashing the dropped ARCHIVE (still in hand
    /// at install time — unlike the post-extract backfill path), then bind the returned metadata under the
    /// mod-FOLDER key (the same key the auto-located row uses). This is the fix for "Lua mod installs with
    /// no metadata": the generic md5-identify keys mods via ZipModKeys, which filters to pak/ucas/utoc and
    /// returns nothing for a Scripts-only Lua archive. Nexus identity is authoritative; a manual entry is
    /// never clobbered. Returns true iff a match was found and written. Best-effort — never throws.
    /// </summary>
    public static async Task<bool> IdentifyMetadataAsync(
        GameContext ctx, INexusClient nexus, string archivePath, string modName)
    {
        if (ctx is null || nexus is null || string.IsNullOrEmpty(modName)) return false;
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath)) return false;

        var domain = NexusDomains.Effective(ctx.Game);
        if (string.IsNullOrWhiteSpace(domain)) return false;

        try
        {
            var match = await nexus.GetByMd5Async(domain, Md5Hash.OfFile(archivePath));
            if (match?.Meta is null) return false;

            var existing = Scanner.LoadMetadata(ctx).GetValueOrDefault(modName);
            if (existing?.IsManual == true) return false; // user's hand-pick is locked — don't override

            var m = match.Meta;
            // Nexus md5 is exact provenance → authoritative; fill gaps from any existing entry.
            var merged = new ModMeta
            {
                Title = m.Title ?? existing?.Title,
                Description = m.Description ?? existing?.Description,
                Author = m.Author ?? existing?.Author,
                AuthorUrl = m.AuthorUrl ?? existing?.AuthorUrl,
                Url = m.Url ?? existing?.Url,
                Source = m.Source ?? existing?.Source,
                Donate = m.Donate ?? existing?.Donate,
                Image = m.Image ?? existing?.Image,
                Downloads = m.Downloads ?? existing?.Downloads,
                CurseforgeId = m.CurseforgeId ?? existing?.CurseforgeId,
                Category = m.Category ?? existing?.Category,
                InstalledUtc = existing?.InstalledUtc,
                SourceConfidence = "md5",
            };
            Scanner.WriteOneMeta(ctx, modName, merged);
            return true;
        }
        catch { return false; } // a miss / outage / unreadable archive never fails the install
    }

    /// <summary>Remove a Lua mod the launcher installed: delete exactly its own folder under ue4ss\Mods.
    /// Never touches a sibling. No-op-safe if the folder is already gone.</summary>
    public static void Uninstall(string ue4ssModsDir, string modName)
    {
        if (string.IsNullOrEmpty(ue4ssModsDir) || string.IsNullOrEmpty(modName)) return;
        var modRoot = Path.Combine(ue4ssModsDir, modName);
        // Containment guard: never delete outside the Mods folder even if modName is hostile.
        if (!PathGate.IsContainedAbsolute(Path.GetFullPath(modRoot), Path.GetFullPath(ue4ssModsDir))) return;
        if (Directory.Exists(modRoot)) TryDeleteDir(modRoot);
    }

    // Delete any leftover .staging-<guid> dirs (debris from a crash between extract + move). Each is
    // self-contained — never a real mod — so removing it is always safe. Best-effort per dir.
    private static void ReapStagingDirs(string ue4ssModsDir)
    {
        if (!Directory.Exists(ue4ssModsDir)) return;
        try
        {
            foreach (var d in Directory.EnumerateDirectories(ue4ssModsDir, StagingPrefix + "*"))
                TryDeleteDir(d);
        }
        catch { /* enumeration failure is non-fatal — install proceeds */ }
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best-effort rollback / cleanup */ }
    }
}
