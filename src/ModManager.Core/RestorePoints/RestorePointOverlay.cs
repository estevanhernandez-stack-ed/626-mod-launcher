using ModManager.Core.Frameworks;

namespace ModManager.Core.RestorePoints;

public static partial class RestorePointEngine
{
    /// <summary>Why <see cref="ReplayOverlay"/> wrote no framework file on a ban-risk game.</summary>
    public const string FrameworksKeptForBanRisk =
        "this game carries a ban risk, so 626 left its mod loader files as they are. "
        + "Put them back from the launcher, which asks first.";

    // What an overlay restore puts back from a snapshot's data folder: an ALLOW-LIST, in the way Safe Clear's
    // turn-off scope is one (a list of exclusions is a list someone has to remember to extend). These are the
    // player's own descriptive data: they say what a mod is, what order it sits in, which loadouts exist and
    // which backups were made. Everything else in the data folder is deliberately NOT here, because it
    // describes or authorizes the game as it is NOW and putting an older copy back would be wrong:
    //   - ban-risk-acks.json / ban-risk-save-acks.json: consent. An old "yes" must never become a new one
    //     after the player revoked it; the live ack stays authoritative.
    //   - taken-over.json, vortex-takeover/: a folder handed back to Vortex stays handed back; a restore does
    //     not silently undo that.
    //   - disabled/, disabled-trees/, direct-disabled/, loose-disabled/: holding folders, i.e. enable state. A
    //     mod switched back on since would be held a second time.
    //   - frameworks/, installs/, replaced/, tools/, tools.json, vanilla-stash.json: what is installed and
    //     what 626 placed. Putting a record back for something gone claims ownership of files it no longer
    //     owns, and a framework registry entry back is a framework "installed" with no say-so.
    // Files are replaced, never deleted: a file the player has now that the snapshot lacks stays.
    private static readonly string[] OverlayDataFiles =
    {
        "metadata.json", "loadorder.json", "classification.json", "direct-inject/config-overrides.json",
    };
    private static readonly string[] OverlayDataDirs = { "profiles", "saves", "config-backups", ".ini-history" };

    // Files that carry a loader mod's on/off. A framework ships some of them (UE4SS ships Mods/mods.txt and
    // per-mod enabled.txt); putting the captured copy back would flip mods the player has switched since.
    private static bool CarriesEnableState(string rel)
    {
        var name = Path.GetFileName(rel);
        return name.Equals("mods.txt", StringComparison.OrdinalIgnoreCase)
            || name.Equals("mods.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("enabled.txt", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Restore a KIND point's game as an overlay on the live game. It is the safe counterpart of
    /// <see cref="ReplayGame"/>, which assumes a Safe Clear emptied the game first. Over a live, modded game
    /// it does only two things, and neither toggles a mod:
    /// <list type="number">
    /// <item>Puts back the allow-listed launcher data (see <c>OverlayDataFiles</c>), file by file, each swap atomic.</item>
    /// <item>Puts back the captured files of each framework that is installed NOW, unchanged in version since
    /// the capture, and not stepped aside, on a game that is not ban-gated. Loader on/off files are skipped.</item>
    /// </list>
    /// It never runs ReplayGame's loader re-apply (step 4: it switched off mods enabled since capture and
    /// deleted <c>enabled.txt</c>), its vanilla-moved / remainder / turn-on steps, or the sheet removal, and
    /// it never enables anything, so the ban-risk gate reduces to "do not write framework files". The caller
    /// has already taken a pre-restore snapshot, so every write here is undoable. Paths are validated for
    /// every framework BEFORE the first write. Returns notes for the player (what was left as it is, and why);
    /// throws if a write fails, with every file either whole-old or whole-new.
    /// </summary>
    public static IReadOnlyList<string> ReplayOverlay(GameArchive ga, string gameArchiveDir, GameContext liveCtx)
    {
        var notes = new List<string>();

        // Plan and validate first: the framework writes, for every framework, before anything is written.
        var banGated = BanRiskRules.ShouldGateEnable(BanRiskCatalog.Effective(liveCtx.Game),
            BanRiskAckStore.IsAcked(liveCtx.DataDir, liveCtx.Game.Id ?? ""));
        var writes = new List<(string Src, string Dest)>();
        if (ga.Frameworks.Count > 0 && banGated)
            notes.Add(FrameworksKeptForBanRisk);
        else
        {
            var live = FrameworkRegistry.List(liveCtx.DataDir);
            foreach (var fw in ga.Frameworks)
            {
                if (fw.CapturedStateRel is null) continue;
                var capturedAbs = Path.Combine(gameArchiveDir, fw.CapturedStateRel);
                if (!Directory.Exists(capturedAbs)) continue;
                var now = live.FirstOrDefault(l => string.Equals(l.FrameworkId, fw.FrameworkId, StringComparison.OrdinalIgnoreCase));
                if (now is null)
                { notes.Add($"{fw.DisplayName} isn't installed now, so 626 didn't put it back."); continue; }
                if (fw.InstalledUtc is null || now.InstalledUtc.ToUniversalTime() != fw.InstalledUtc.Value.ToUniversalTime())
                { notes.Add($"{fw.DisplayName} was installed or updated since the snapshot, so 626 left its files as they are."); continue; }
                if (!SameFolder(now.InstallPath, fw.InstallPath))
                { notes.Add($"{fw.DisplayName} now lives somewhere else, so 626 left its files as they are."); continue; }
                if (FrameworkRegistry.IsDisabled(liveCtx.DataDir, fw.FrameworkId))
                { notes.Add($"{fw.DisplayName} is switched off, so 626 left its files as they are."); continue; }

                var installFull = Path.GetFullPath(now.InstallPath);
                foreach (var rel in fw.InstalledFiles)
                {
                    if (IsRooted(rel) || !PathGate.IsContained(rel, installFull))
                        throw new InvalidOperationException($"Restore refused: framework file \"{rel}\" escapes the install root.");
                    if (CarriesEnableState(rel)) continue;
                    var src = Path.Combine(capturedAbs, rel);
                    if (File.Exists(src)) writes.Add((src, Path.Combine(now.InstallPath, rel)));
                }
            }
        }

        // 1. The launcher's own data, by allow-list.
        var archivedData = Path.Combine(gameArchiveDir, "data");
        if (Directory.Exists(archivedData))
        {
            foreach (var rel in OverlayDataFiles)
            {
                var src = Path.Combine(archivedData, rel);
                if (File.Exists(src)) CopyFileAtomic(src, Path.Combine(liveCtx.DataDir, rel), verifyHash: false);
            }
            foreach (var dir in OverlayDataDirs)
            {
                var src = Path.Combine(archivedData, dir);
                if (Directory.Exists(src)) CopyTreeAtomic(src, Path.Combine(liveCtx.DataDir, dir));
            }
        }

        // 2. Framework files, hash-verified (this is code the game loads).
        foreach (var (src, dest) in writes) CopyFileAtomic(src, dest, verifyHash: true);
        return notes;
    }

    private static bool SameFolder(string a, string b)
    {
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Replace <paramref name="dest"/> with a copy of <paramref name="src"/> so that it is never
    /// absent and never half-written: copy to a sibling temp, check it (size, and SHA-256 when asked), then
    /// move it over the destination in one step. On any failure the temp goes and the old file is untouched.</summary>
    internal static void CopyFileAtomic(string src, string dest, bool verifyHash)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var tmp = dest + ".rp-tmp";
        try
        {
            File.Copy(src, tmp, overwrite: true);
            if (new FileInfo(tmp).Length != new FileInfo(src).Length
                || (verifyHash && !string.Equals(FileTally.Sha256(tmp), FileTally.Sha256(src), StringComparison.OrdinalIgnoreCase)))
                throw new IOException($"Restore verify failed copying \"{Path.GetFileName(src)}\".");
            File.Move(tmp, dest, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* our own temp */ }
            throw;
        }
    }

    private static void CopyTreeAtomic(string src, string dest)
    {
        foreach (var f in Directory.GetFiles(src))
            CopyFileAtomic(f, Path.Combine(dest, Path.GetFileName(f)), verifyHash: false);
        foreach (var d in Directory.GetDirectories(src))
            CopyTreeAtomic(d, Path.Combine(dest, Path.GetFileName(d)));
    }
}
