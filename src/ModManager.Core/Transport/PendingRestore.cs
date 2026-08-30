using System.IO.Compression;
using System.Text.Json;

namespace ModManager.Core.Transport;

/// <summary>One game waiting for its game to come back, described without opening anything.</summary>
/// <param name="GameId">The id the archive recorded. What a later registration is matched against.</param>
/// <param name="GameName">What to call it on screen. Falls back to the id when the archive had no name.</param>
/// <param name="Path">The one-game archive on disk.</param>
/// <param name="Bytes">Its size, so somebody can see what holding it costs.</param>
public sealed record HeldGame(
    string GameId,
    string GameName,
    string Path,
    long Bytes,
    bool SaveIncluded,
    int ModFileCount,
    int SaveFileCount,
    string? HeldUtc);

/// <summary>
/// Holding a game's contents until the game itself comes back.
///
/// <para>Step four, and the case the whole archive exists for. The normal state of a fresh Windows
/// install is that the backup holds twelve games and the machine has none of them; the report screen
/// can only say <i>waiting on the game</i>, because there is nowhere to put a game's files until it
/// is registered. The honest answer to that is to wait — not to guess a path, and not to make
/// somebody keep a backup file findable for a week while Steam downloads.</para>
///
/// <para><b>Nothing is resolved at hold time.</b> A game can come back on a different drive, in a
/// different Steam library, under a folder that did not exist when the backup was made. So what is
/// held is the CONTENT, never a path, and every destination is resolved at the moment the game is
/// registered — which is the same rule <see cref="ProfileRestore"/> already works by.</para>
///
/// <para><b>It is not a second format.</b> Holding writes a one-game profile archive, so putting it
/// back later is the ordinary restore reading the ordinary shape. There is no second reader to keep
/// in step with the first, and no second thing to get wrong.</para>
/// </summary>
public static class PendingRestore
{
    /// <summary>Written beside the manifest so a listing can say when something was held without
    /// depending on a file timestamp, which copying moves.</summary>
    public const string StampEntry = "held.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private sealed record Stamp(string HeldUtc);

    /// <summary>
    /// Take one game out of a backup and keep it until its game exists here.
    ///
    /// <para><b>This copies.</b> The premise of the feature is a machine being rebuilt, so the backup
    /// is on a USB stick or a share that is about to be unplugged. A pointer to it would work right up
    /// until the moment it mattered, which is the worst time to find out.</para>
    /// </summary>
    /// <returns>The path of the one-game archive that was written.</returns>
    public static string Hold(string archivePath, string gameId, string holdingDir)
    {
        var manifest = ProfileArchive.ReadManifest(archivePath)
            ?? throw new InvalidOperationException(
                "That file is not a 626 backup, or its description could not be read. Nothing was held.");

        var game = manifest.Games.FirstOrDefault(
            g => string.Equals(g.Game.Id, gameId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"This backup does not contain {gameId}, so there is nothing to hold.");

        Directory.CreateDirectory(holdingDir);
        var dest = PathFor(game.Game.Id, holdingDir);
        var tmp = dest + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);

        var prefix = ProfileArchive.GamesPrefix + game.Game.Id + "/";

        using (var source = ZipFile.OpenRead(archivePath))
        using (var held = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            // A manifest describing this game alone. Reusing the outer one would leave a file claiming
            // eleven games it does not carry, and every reader downstream would believe it.
            var one = manifest with { Games = new[] { game }, Excluded = ExclusionsFor(manifest, prefix) };
            using (var w = new StreamWriter(held.CreateEntry(ProfileArchive.ManifestEntry).Open()))
                w.Write(JsonSerializer.Serialize(one, Json));

            using (var w = new StreamWriter(held.CreateEntry(StampEntry).Open()))
                w.Write(JsonSerializer.Serialize(new Stamp(DateTime.UtcNow.ToString("O")), Json));

            foreach (var entry in source.Entries)
            {
                if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;

                using var from = entry.Open();
                using var to = held.CreateEntry(entry.FullName).Open();
                from.CopyTo(to);
            }
        }

        // Replace rather than pile up: opening the same backup twice must not leave two copies of a
        // 4 GB game with no way to tell which is current.
        if (File.Exists(dest)) File.Delete(dest);
        File.Move(tmp, dest);
        return dest;
    }

    /// <summary>Everything waiting, newest description first read from the file itself. A stray file
    /// in the folder is ignored — it is a folder on somebody's disk, and something else will end up in
    /// it eventually.</summary>
    public static IReadOnlyList<HeldGame> List(string holdingDir)
    {
        if (!Directory.Exists(holdingDir)) return Array.Empty<HeldGame>();

        var held = new List<HeldGame>();
        foreach (var file in Directory.EnumerateFiles(holdingDir, "*" + ProfileArchive.Extension))
        {
            var one = Describe(file);
            if (one is not null) held.Add(one);
        }
        return held;
    }

    /// <summary>What is waiting for one game, or null. The question the add-a-game path asks, so it
    /// must be cheap and must never throw — it runs on every registration, including the first one on
    /// a machine that has never held anything.</summary>
    public static HeldGame? For(string gameId, string holdingDir)
    {
        if (string.IsNullOrEmpty(gameId) || !Directory.Exists(holdingDir)) return null;
        var path = PathFor(gameId, holdingDir);
        return File.Exists(path) ? Describe(path) : null;
    }

    /// <summary>Throw away what was held for a game. True when there was something to throw away.</summary>
    public static bool Discard(string gameId, string holdingDir)
    {
        if (string.IsNullOrEmpty(gameId) || !Directory.Exists(holdingDir)) return false;
        var path = PathFor(gameId, holdingDir);
        if (!File.Exists(path)) return false;
        try { File.Delete(path); return true; }
        catch { return false; }
    }

    /// <summary>One file per game id, so holding the same game twice replaces rather than accumulates
    /// and <see cref="For"/> is a single existence check rather than a scan.</summary>
    private static string PathFor(string gameId, string holdingDir)
        => Path.Combine(holdingDir, Safe(gameId) + ProfileArchive.Extension);

    /// <summary>A game id is a kebab-case key, but it reaches this from a file written elsewhere, so
    /// it is treated as untrusted input rather than assumed to be a safe file name.</summary>
    private static string Safe(string gameId)
    {
        var chars = gameId.ToLowerInvariant().Select(
            c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray();
        var s = new string(chars).Trim('-');
        return s.Length == 0 ? "game" : s;
    }

    private static HeldGame? Describe(string path)
    {
        var m = ProfileArchive.ReadManifest(path);
        var g = m?.Games.FirstOrDefault();
        if (g is null) return null;

        string? heldUtc = null;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var stamp = zip.GetEntry(StampEntry);
            if (stamp is not null)
            {
                using var r = new StreamReader(stamp.Open());
                heldUtc = JsonSerializer.Deserialize<Stamp>(r.ReadToEnd(), Json)?.HeldUtc;
            }
        }
        catch { /* a missing or unreadable stamp costs a date, not the entry */ }

        return new HeldGame(
            g.Game.Id,
            string.IsNullOrWhiteSpace(g.Game.Name) ? g.Game.Id : g.Game.Name!,
            path,
            new FileInfo(path).Length,
            g.SaveIncluded,
            g.ModFileCount,
            g.SaveFileCount,
            heldUtc);
    }

    /// <summary>The outer manifest records exclusions for every game by full path. Carrying the lot
    /// into a one-game file would have it report files it never held.</summary>
    private static IReadOnlyList<BundleExclusion> ExclusionsFor(
        ProfileArchiveManifest manifest, string prefix)
        => manifest.Excluded
            .Where(x => x.Path.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// What a held backup carries, as a fragment the state chip finishes into a sentence.
    ///
    /// <para>Counts, never sizes. "3.9 GB is waiting" tells somebody what it costs them; "12 mods and
    /// 79 save files" tells them what they get back, which is the thing they are deciding about.</para>
    /// </summary>
    public static string Describe(HeldGame held)
    {
        var bits = new List<string>();
        if (held.ModFileCount > 0)
            bits.Add($"{held.ModFileCount:N0} mod file{(held.ModFileCount == 1 ? "" : "s")}");
        if (held.SaveFileCount > 0)
            bits.Add($"{held.SaveFileCount:N0} save file{(held.SaveFileCount == 1 ? "" : "s")}");

        return bits.Count switch
        {
            0 => "Settings",                       // nothing but the launcher's own per-game data
            1 => bits[0],
            _ => string.Join(" and ", bits),
        };
    }
}
