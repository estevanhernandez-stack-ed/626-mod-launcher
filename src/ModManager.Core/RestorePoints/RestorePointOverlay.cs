using System.Text.Json;
using System.Text.Json.Nodes;
using ModManager.Core.Frameworks;

namespace ModManager.Core.RestorePoints;

/// <summary>What an overlay restore will do, decided and validated BEFORE anything is written (and before the
/// pre-restore snapshot is taken): the framework files to put back, and notes for the player.</summary>
public sealed record OverlayPlan(IReadOnlyList<string> Notes, IReadOnlyList<(string Src, string Dest)> FrameworkWrites);

public static partial class RestorePointEngine
{
    /// <summary>Why <see cref="PlanOverlay"/> plans no framework file on a ban-risk game.</summary>
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
    //
    // The keyed files are MERGED, not replaced: each is a map keyed by mod (or mod id), so a mod installed or
    // classified since the snapshot would otherwise lose its source URL, confidence, install time and update
    // tracking. The snapshot's value wins for a key both have; keys only the live file has are kept. Depth is
    // how many levels of keys are merged (config-overrides.json nests its map one level down).
    private static readonly (string Rel, int Depth)[] OverlayKeyedFiles =
    {
        ("metadata.json", 1), ("classification.json", 1), ("direct-inject/config-overrides.json", 2),
    };
    private static readonly string[] OverlayWholeFiles = { "loadorder.json" };   // one ordered list: the snapshot's order
    private static readonly string[] OverlayDataDirs = { "profiles", "saves", "config-backups", ".ini-history" };

    // Files that carry a loader mod's on/off. A framework ships some of them (UE4SS ships Mods/mods.txt and
    // per-mod enabled.txt; BepInEx's doorstop_config.ini holds the switch that makes BepInEx inject at all);
    // putting the captured copy back would flip what the player has switched since.
    private static bool CarriesEnableState(string rel)
    {
        var name = Path.GetFileName(rel);
        return name.Equals("mods.txt", StringComparison.OrdinalIgnoreCase)
            || name.Equals("mods.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("enabled.txt", StringComparison.OrdinalIgnoreCase)
            || name.Equals("doorstop_config.ini", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Decide what restoring a KIND point's game as an overlay will do. Writes nothing; throws
    /// <see cref="InvalidOperationException"/> if the snapshot names a framework file outside its install
    /// root, so the caller can refuse before taking the pre-restore snapshot. The safe counterpart of
    /// <see cref="ReplayGame"/>, which assumes a Safe Clear emptied the game first: over a live, modded game
    /// it never runs ReplayGame's loader re-apply (step 4 switched off mods enabled since capture and deleted
    /// <c>enabled.txt</c>), its vanilla-moved / remainder / turn-on steps, or the sheet removal, and it
    /// never enables anything, so the ban-risk gate reduces to "do not write framework files".
    /// <para>A framework's files are planned only while ALL of these hold; otherwise it is left as it is,
    /// with a note: the game is not ban-gated and has no Play-vanilla stash; the framework is registered
    /// NOW, with the install stamp it had at capture, at the same install path; and none of its proxy files
    /// is in the holding folder (any one held means a step-aside, finished or interrupted). Of its captured
    /// files only those the LIVE record still lists are planned, minus loader on/off files, minus any file
    /// the catalog forbids a framework to touch (the game executable).</para>
    /// </summary>
    public static OverlayPlan PlanOverlay(GameArchive ga, string gameArchiveDir, GameContext liveCtx)
    {
        var notes = new List<string>();
        var writes = new List<(string Src, string Dest)>();
        if (ga.Frameworks.Count == 0) return new OverlayPlan(notes, writes);

        if (BanRiskRules.ShouldGateEnable(BanRiskCatalog.Effective(liveCtx.Game),
                BanRiskAckStore.IsAcked(liveCtx.DataDir, liveCtx.Game.Id ?? "")))
        { notes.Add(FrameworksKeptForBanRisk); return new OverlayPlan(notes, writes); }
        if (File.Exists(VanillaStashStore.PathFor(liveCtx.DataDir)))
        {
            notes.Add("Play vanilla is on for this game (626 stepped some mod loader files aside), so 626 left its mod loader files as they are.");
            return new OverlayPlan(notes, writes);
        }

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
            if (FrameworkRegistry.AnyProxyHeld(liveCtx.DataDir, now))
            { notes.Add($"{fw.DisplayName} is switched off, so 626 left its files as they are."); continue; }

            // The snapshot is untrusted input: every path in it is checked, whether or not it is written.
            var installFull = Path.GetFullPath(now.InstallPath);
            foreach (var rel in fw.InstalledFiles)
                if (IsRooted(rel) || !PathGate.IsContained(rel, installFull))
                    throw new InvalidOperationException($"Restore refused: framework file \"{rel}\" escapes the install root.");

            var listedNow = new HashSet<string>(now.InstalledFiles.Select(NormRel), StringComparer.OrdinalIgnoreCase);
            var forbidden = KnownFramework.Catalog
                .FirstOrDefault(k => string.Equals(k.FrameworkId, fw.FrameworkId, StringComparison.OrdinalIgnoreCase))?.ForbiddenPaths
                ?? Array.Empty<string>();
            foreach (var rel in fw.InstalledFiles)
            {
                if (!listedNow.Contains(NormRel(rel))) continue;
                if (CarriesEnableState(rel) || PathGate.IsForbidden(NormRel(rel), forbidden)) continue;
                var src = Path.Combine(capturedAbs, rel);
                if (File.Exists(src)) writes.Add((src, Path.Combine(now.InstallPath, rel)));
            }
        }
        return new OverlayPlan(notes, writes);
    }

    /// <summary>
    /// Carry out an <see cref="OverlayPlan"/> (the caller has already taken the pre-restore snapshot, so every
    /// write is undoable): the allow-listed launcher data first, then the planned framework files, hash-
    /// verified (this is code the game loads). Every write is temp + verify + atomic move, so each file is
    /// whole-old or whole-new; throws if one fails. Returns notes for the player: a keyed file that couldn't
    /// be merged and was replaced whole.
    /// </summary>
    public static IReadOnlyList<string> ApplyOverlay(OverlayPlan plan, string gameArchiveDir, GameContext liveCtx)
    {
        var notes = new List<string>();
        var archivedData = Path.Combine(gameArchiveDir, "data");
        if (Directory.Exists(archivedData))
        {
            foreach (var (rel, depth) in OverlayKeyedFiles)
            {
                var src = Path.Combine(archivedData, rel);
                if (!File.Exists(src)) continue;
                var dest = Path.Combine(liveCtx.DataDir, rel);
                if (File.Exists(dest) && TryMerge(src, dest, depth) is { } merged)
                    AtomicJson.WriteTextAtomic(dest, merged);
                else
                {
                    if (File.Exists(dest))
                        notes.Add($"{rel} couldn't be merged with the snapshot's copy, so 626 replaced it whole. "
                            + "Anything added to it since the snapshot is only in the copy saved before this restore.");
                    CopyFileAtomic(src, dest, verifyHash: false);
                }
            }
            foreach (var rel in OverlayWholeFiles)
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
        foreach (var (src, dest) in plan.FrameworkWrites) CopyFileAtomic(src, dest, verifyHash: true);
        return notes;
    }

    // The merged text of a keyed file, or null when either side isn't a JSON object (the caller then replaces whole).
    private static string? TryMerge(string snapshotFile, string liveFile, int depth)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(snapshotFile)) is not JsonObject snap
                || JsonNode.Parse(File.ReadAllText(liveFile)) is not JsonObject live) return null;
            return MergeKeyed(snap, live, depth).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException) { return null; }
    }

    // Live entries in their order, the snapshot's value where both have the key (names compare case-
    // insensitively, as the launcher reads them), then the snapshot's remaining keys.
    private static JsonObject MergeKeyed(JsonObject snap, JsonObject live, int depth)
    {
        var snapKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, _) in snap) snapKeys.TryAdd(k, k);
        var result = new JsonObject();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, liveValue) in live)
        {
            if (!snapKeys.TryGetValue(k, out var sk)) { result[k] = liveValue?.DeepClone(); continue; }
            taken.Add(k);
            var sv = snap[sk];
            result[sk] = depth > 1 && sv is JsonObject so && liveValue is JsonObject lo
                ? MergeKeyed(so, lo, depth - 1)
                : sv?.DeepClone();
        }
        foreach (var (k, sv) in snap)
            if (!taken.Contains(k)) result[k] = sv?.DeepClone();
        return result;
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
