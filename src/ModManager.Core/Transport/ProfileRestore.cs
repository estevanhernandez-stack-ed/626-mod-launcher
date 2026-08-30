using System.IO.Compression;

namespace ModManager.Core.Transport;

/// <summary>What one game's restore is allowed to touch. Chosen per game, per part.</summary>
[Flags]
public enum RestoreParts
{
    None = 0,
    Saves = 1,
    Mods = 2,
    Settings = 4,
}

/// <summary>
/// One game's restore, as asked for by the caller.
/// </summary>
/// <param name="GameId">Must match the archive's id for that game.</param>
/// <param name="Parts">What to put back. Nothing happens for <see cref="RestoreParts.None"/>.</param>
/// <param name="SaveDir">Where this machine keeps that game's saves, resolved NOW — never the path the
/// archive was made from. A fresh install has a different Steam library and different folders.</param>
/// <param name="ModDir">Where mods go on this machine, resolved now.</param>
/// <param name="DataDir">The launcher's own per-game folder on this machine.</param>
/// <param name="SnapshotsDir">Where the pre-restore snapshot is written.</param>
public sealed record RestoreRequest(
    string GameId,
    RestoreParts Parts,
    string? SaveDir = null,
    string? ModDir = null,
    string? DataDir = null,
    string? SnapshotsDir = null);

/// <summary>What actually happened to one game — including what did not, and why.</summary>
public sealed record RestoreOutcome(
    string GameId,
    RestoreParts Restored,
    int FileCount,
    long Bytes,
    bool SnapshotTaken,
    string? Skipped = null);

/// <summary>What the whole run did.</summary>
public sealed record RestoreResult
{
    public IReadOnlyList<RestoreOutcome> Games { get; init; } = Array.Empty<RestoreOutcome>();
    public int TotalFiles => Games.Sum(g => g.FileCount);
    public long TotalBytes => Games.Sum(g => g.Bytes);
    public IReadOnlyList<RestoreOutcome> Skipped => Games.Where(g => g.Skipped is not null).ToList();

    /// <summary>What to tell the user afterwards. Names what could not be done as plainly as what
    /// could — a restore that half-worked and said "done" is the failure this wording exists to
    /// prevent.</summary>
    public string Summary
    {
        get
        {
            var did = Games.Where(g => g.Skipped is null).ToList();
            if (did.Count == 0 && Skipped.Count == 0) return "Nothing was selected, so nothing changed.";

            var head = did.Count == 0
                ? "Nothing was restored."
                : $"Restored {did.Count} game{(did.Count == 1 ? "" : "s")} — "
                  + $"{TotalFiles:N0} file{(TotalFiles == 1 ? "" : "s")} ({ProfileReportText.Human(TotalBytes)}).";

            if (Skipped.Count == 0) return head;
            return head + $" {Skipped.Count} skipped: "
                 + string.Join("; ", Skipped.Select(s => $"{s.GameId} — {s.Skipped}")) + ".";
        }
    }
}

/// <summary>
/// Putting a profile archive back, per game and per part.
///
/// <para><b>The half that can hurt you</b>, and built last on purpose: the report screen came first so
/// the reading was known-good before anything acted on it.</para>
///
/// <para>Three guards, each learned rather than assumed:</para>
/// <list type="bullet">
/// <item><b>Snapshot first.</b> Anything about to be replaced is snapshotted, per the file-op laws.</item>
/// <item><b>Refuse while the game runs.</b> A folder changed under a running game is silently undone
/// on exit — proved on Palworld, where a deleted world came back.</item>
/// <item><b>Paths are resolved NOW.</b> The archive records what a game HAD, never where it lived. A
/// fresh install has a different library, a different drive, different folders.</item>
/// </list>
///
/// <para>A game the archive holds but this machine has not installed is <b>skipped with a reason</b>,
/// never failed — that is the normal case on the machine this feature exists for.</para>
/// </summary>
public static class ProfileRestore
{
    /// <summary>
    /// Restore the requested games and parts.
    /// </summary>
    /// <param name="isGameRunning">Asked once per game before anything is written for it. Must fail
    /// CLOSED — an unknown answer counts as running.</param>
    public static RestoreResult Restore(
        string archivePath,
        IReadOnlyList<RestoreRequest> requests,
        Func<string, bool> isGameRunning)
    {
        var manifest = ProfileArchive.ReadManifest(archivePath)
            ?? throw new InvalidOperationException(
                "That file is not a 626 backup, or its description could not be read. Nothing was changed.");

        if (manifest.ArchiveVersion > ProfileArchive.CurrentVersion)
            throw new InvalidOperationException(
                $"This backup was made by a newer version of the launcher (format {manifest.ArchiveVersion}). "
                + "Update, then try again — nothing was changed.");

        var byId = manifest.Games.ToDictionary(g => g.Game.Id, StringComparer.OrdinalIgnoreCase);
        var outcomes = new List<RestoreOutcome>();

        using var zip = ZipFile.OpenRead(archivePath);

        foreach (var req in requests)
        {
            if (req.Parts == RestoreParts.None) continue;

            if (!byId.ContainsKey(req.GameId))
            {
                outcomes.Add(new RestoreOutcome(req.GameId, RestoreParts.None, 0, 0, false,
                    "this backup does not contain that game"));
                continue;
            }

            // Asked per game and BEFORE anything is written for it. A folder changed under a running
            // game is silently undone on exit, which reports as "it didn't work" with nothing to see.
            bool running;
            try { running = isGameRunning(req.GameId); }
            catch { running = true; }   // fail closed: an unknown answer is not permission
            if (running)
            {
                outcomes.Add(new RestoreOutcome(req.GameId, RestoreParts.None, 0, 0, false,
                    "the game is running"));
                continue;
            }

            outcomes.Add(RestoreOne(zip, req));
        }

        return new RestoreResult { Games = outcomes };
    }

    private static RestoreOutcome RestoreOne(ZipArchive zip, RestoreRequest req)
    {
        var prefix = ProfileArchive.GamesPrefix + req.GameId + "/";
        var done = RestoreParts.None;
        var files = 0;
        long bytes = 0;
        var snapshotTaken = false;

        if (req.Parts.HasFlag(RestoreParts.Saves) && !string.IsNullOrEmpty(req.SaveDir))
        {
            snapshotTaken |= SnapshotFirst(req.SaveDir!, req.SnapshotsDir);
            var (n, b) = Extract(zip, prefix + SaveBundle.PayloadPrefix, req.SaveDir!);
            if (n > 0) { done |= RestoreParts.Saves; files += n; bytes += b; }
        }

        if (req.Parts.HasFlag(RestoreParts.Mods) && !string.IsNullOrEmpty(req.ModDir))
        {
            var (n, b) = Extract(zip, prefix + ProfileArchive.ModsFolder, req.ModDir!);
            if (n > 0) { done |= RestoreParts.Mods; files += n; bytes += b; }
        }

        if (req.Parts.HasFlag(RestoreParts.Settings) && !string.IsNullOrEmpty(req.DataDir))
        {
            var (n, b) = Extract(zip, prefix + ProfileArchive.DataFolder, req.DataDir!);
            if (n > 0) { done |= RestoreParts.Settings; files += n; bytes += b; }
        }

        return done == RestoreParts.None
            ? new RestoreOutcome(req.GameId, done, 0, 0, snapshotTaken, "this backup holds nothing for the parts you chose")
            : new RestoreOutcome(req.GameId, done, files, bytes, snapshotTaken);
    }

    /// <summary>Snapshot what is about to be replaced. Same guarantee and the same label the rest of
    /// the app uses, so a restore that goes wrong is undone the way everything else is.</summary>
    private static bool SnapshotFirst(string saveDir, string? snapshotsDir)
    {
        if (string.IsNullOrEmpty(snapshotsDir)) return false;
        try
        {
            if (!Directory.Exists(saveDir) || !Directory.EnumerateFileSystemEntries(saveDir).Any()) return false;
            SaveManager.Backup(saveDir, snapshotsDir!, "before-restore", auto: true);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Copy one section out, over the top of what is there.
    ///
    /// <para><b>Adds and overwrites; never clears the destination.</b> A save bundle's restore wipes
    /// first because it replaces ONE game's saves and the user asked for exactly that. A profile
    /// restore runs across everything at once, and a folder here may hold a game's own content — mods
    /// sit intermixed with it. Emptying that would take the game with it.</para>
    ///
    /// <para>Every entry is checked to resolve inside the destination: an archive is a file from
    /// another machine, and therefore untrusted.</para>
    /// </summary>
    private static (int Files, long Bytes) Extract(ZipArchive zip, string prefix, string destRoot)
    {
        var root = Path.GetFullPath(destRoot);
        Directory.CreateDirectory(root);

        var files = 0;
        long bytes = 0;
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;

            var rel = entry.FullName[prefix.Length..];

            // Containment, not a prefix match: a prefix accepts a SIBLING directory, so a root of
            // .../saves/pal would happily take .../saves/palworld-evil/x.sav.
            var dest = SafeExtractPath.ResolveOrThrow(root, rel);

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
            files++;
            bytes += entry.Length;
        }
        return (files, bytes);
    }
}
