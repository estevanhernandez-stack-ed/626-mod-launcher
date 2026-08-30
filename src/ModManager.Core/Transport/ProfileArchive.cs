using System.IO.Compression;
using System.Text.Json;

namespace ModManager.Core.Transport;

/// <summary>Everything one game contributes to a profile archive. Assembled by the caller, because
/// finding mod files and data folders is App-side work and this stays pure.</summary>
/// <param name="Game">Identity, so a restore can find where this game lives on the new machine.</param>
/// <param name="SaveDir">Its save folder, or null to archive no saves for this game.</param>
/// <param name="ModFiles">The files the SCANNER identified as mods, each relative path led by the
/// NAME of the location it came from: <c>&lt;location&gt;/&lt;mod&gt;/&lt;rest&gt;</c>. Never a folder —
/// mods sit intermixed with game content, and copying the folder would haul the base game.</param>
/// <param name="Mods">What those files are, so the far end can name what is missing.</param>
/// <param name="DataDir">The launcher's own per-game folder: classification, metadata, profiles.</param>
public sealed record ProfileGameSource(
    BundleGame Game,
    string? SaveDir,
    IReadOnlyList<BundlePlanFile> ModFiles,
    IReadOnlyList<BundleMod> Mods,
    string? DataDir)
{
    /// <summary>This game's mod location NAMES on the machine being archived, primary first.
    ///
    /// <para>Recorded because a game can keep mods in more than one place — Windrose keeps two — and a
    /// flat list loses which is which. Two same-named mods in different locations then collide in the
    /// archive outright, and a restore has nothing to resolve against but a guess.</para></summary>
    public IReadOnlyList<string> ModLocations { get; init; } = Array.Empty<string>();
}

/// <summary>What one game turned out to hold, recorded in the archive manifest.</summary>
public sealed record ProfileGame
{
    public BundleGame Game { get; init; } = new("", null, null);
    public bool SaveIncluded { get; init; }
    public int SaveFileCount { get; init; }
    public long SaveBytes { get; init; }
    public int ModFileCount { get; init; }
    public long ModBytes { get; init; }
    public int DataFileCount { get; init; }
    public long DataBytes { get; init; }
    public IReadOnlyList<BundleMod> Mods { get; init; } = Array.Empty<BundleMod>();

    /// <summary>The mod location names this game's files are filed under, primary first. Empty in
    /// format 1, which wrote mods flat.</summary>
    public IReadOnlyList<string> ModLocations { get; init; } = Array.Empty<string>();
}

/// <summary>What a profile archive declares about itself, stored as <c>profile.json</c> at the root.</summary>
public sealed record ProfileArchiveManifest
{
    public int ArchiveVersion { get; init; } = ProfileArchive.CurrentVersion;
    public string CreatedUtc { get; init; } = "";
    public string LauncherVersion { get; init; } = "";
    public bool SnapshotHistoryIncluded { get; init; }
    public IReadOnlyList<ProfileGame> Games { get; init; } = Array.Empty<ProfileGame>();
    public IReadOnlyList<BundleExclusion> Excluded { get; init; } = Array.Empty<BundleExclusion>();
    public IReadOnlyList<BundleNotice> Notices { get; init; } = Array.Empty<BundleNotice>();

    public int TotalFiles => Games.Sum(g => g.SaveFileCount + g.ModFileCount + g.DataFileCount);
    public long TotalBytes => Games.Sum(g => g.SaveBytes + g.ModBytes + g.DataBytes);
}

/// <summary>
/// One file holding a whole modded setup — every game's mods, saves and settings — so a fresh Windows
/// install is cheap again.
///
/// <para><b>People stopped reinstalling</b> because putting everything back got quietly enormous, so a
/// degrading install is cheaper than a clean one and the problems just live there. The launcher knows
/// more about a modded setup than anything else on the machine; the archive is only how you carry
/// it.</para>
///
/// <para><b>A bundle per game, plus the registry.</b> Not a second format:
/// <see cref="SaveBundle.WriteInto"/> writes each game's saves under <c>games/&lt;id&gt;/</c>, using
/// the same planner, the same credential scan and the same manifest as a standalone <c>.626save</c>.
/// Mods and launcher data sit alongside.</para>
///
/// <para><b>This half only reads.</b> Nothing here writes to a game folder or a save. It is shippable
/// on its own as a backup, which keeps the half that can hurt you out of the first release.</para>
///
/// <para>Design: <c>docs/superpowers/specs/2026-08-20-profile-archive-design.md</c>.</para>
/// </summary>
public static class ProfileArchive
{
    /// <summary>2 files mods under the location they came from; 1 wrote them flat. A reader must know
    /// which, because in format 1 the first path segment is a MOD name and in format 2 it is a
    /// LOCATION name — the same string meaning two different things.</summary>
    public const int CurrentVersion = 2;
    public const string ManifestEntry = "profile.json";
    public const string GamesPrefix = "games/";
    public const string ModsFolder = "mods/";
    public const string DataFolder = "data/";
    public const string Extension = ".626profile";

    /// <summary>Snapshot history lives here inside a game's data folder. Excluded by default: it is
    /// backups of backups, and on a real machine it was 446 MB of a 482 MB data total.</summary>
    public const string SnapshotSubfolder = "saves";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>
    /// Write the archive. Reads only.
    /// </summary>
    /// <param name="includeSnapshotHistory">Carry each game's restore points and snapshots. Off by
    /// default — an archive that silently hauls every snapshot you ever made is carrying a spare tyre
    /// for a spare tyre.</param>
    public static ProfileArchiveManifest Create(
        IReadOnlyList<ProfileGameSource> sources,
        string archivePath,
        DateTime createdUtc,
        string launcherVersion,
        bool includeSnapshotHistory = false)
    {
        var games = new List<ProfileGame>();
        var excluded = new List<BundleExclusion>();
        var notices = new List<BundleNotice>();

        ProfileArchiveManifest result;
        var tmp = archivePath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        if (File.Exists(tmp)) File.Delete(tmp);

        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var src in sources)
            {
                var prefix = GamesPrefix + src.Game.Id + "/";
                var entry = new ProfileGame { Game = src.Game, Mods = src.Mods };

                // ---- saves: the same bundle a standalone .626save would be ----------------------
                if (!string.IsNullOrEmpty(src.SaveDir) && Directory.Exists(src.SaveDir))
                {
                    var plan = SaveBundle.Plan(src.SaveDir!, src.Game, createdUtc,
                                               BundleScope.Portable, src.Mods);
                    SaveBundle.WriteInto(zip, plan, prefix);

                    entry = entry with
                    {
                        SaveIncluded = true,
                        SaveFileCount = plan.Manifest.FileCount,
                        SaveBytes = plan.Manifest.Bytes,
                    };
                    excluded.AddRange(plan.Manifest.Excluded.Select(
                        x => new BundleExclusion(prefix + SaveBundle.PayloadPrefix + x.Path, x.Reason)));
                    notices.AddRange(plan.Manifest.Notices.Select(
                        n => new BundleNotice(prefix + SaveBundle.PayloadPrefix + n.Path, n.Reason)));
                }

                // ---- mods: the scanner's file list, never a folder -------------------------------
                var (modFiles, modBytes) = WriteFiles(zip, src.ModFiles, prefix + ModsFolder, excluded);
                entry = entry with
                {
                    ModFileCount = modFiles,
                    ModBytes = modBytes,
                    ModLocations = src.ModLocations,
                };

                // ---- the launcher's own per-game data --------------------------------------------
                if (!string.IsNullOrEmpty(src.DataDir) && Directory.Exists(src.DataDir))
                {
                    var planned = new List<BundlePlanFile>();
                    foreach (var full in Directory.EnumerateFiles(src.DataDir!, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(src.DataDir!, full).Replace('\\', '/');

                        // Snapshot history is backups of backups. Skipped unless asked for.
                        if (!includeSnapshotHistory &&
                            rel.StartsWith(SnapshotSubfolder + "/", StringComparison.OrdinalIgnoreCase))
                            continue;

                        planned.Add(new BundlePlanFile(full, rel));
                    }
                    var (dataFiles, dataBytes) = WriteFiles(zip, planned, prefix + DataFolder, excluded);
                    entry = entry with { DataFileCount = dataFiles, DataBytes = dataBytes };
                }

                games.Add(entry);
            }

            result = new ProfileArchiveManifest
            {
                CreatedUtc = createdUtc.ToString("O"),
                LauncherVersion = launcherVersion,
                SnapshotHistoryIncluded = includeSnapshotHistory,
                Games = games,
                Excluded = excluded,
                Notices = notices,
            };

            using (var w = new StreamWriter(zip.CreateEntry(ManifestEntry).Open()))
                w.Write(JsonSerializer.Serialize(result, Json));
        }

        // Outside the using: the archive has to be CLOSED before it can be renamed, and doing the
        // move inside left the handle open and threw on every run.
        File.Move(tmp, archivePath, overwrite: true);
        return result;
    }

    /// <summary>Read what an archive says about itself, without extracting anything.</summary>
    public static ProfileArchiveManifest? ReadManifest(string archivePath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(archivePath);
            var entry = zip.GetEntry(ManifestEntry);
            if (entry is null) return null;
            using var r = new StreamReader(entry.Open());
            return JsonSerializer.Deserialize<ProfileArchiveManifest>(r.ReadToEnd(), Json);
        }
        catch { return null; }
    }

    /// <summary>Copy a planned set of files in, applying the same credential rule the bundles use — a
    /// mod folder or a launcher data folder can hold a token just as a save folder can.</summary>
    private static (int Files, long Bytes) WriteFiles(
        ZipArchive zip, IReadOnlyList<BundlePlanFile> files, string prefix, List<BundleExclusion> excluded)
    {
        var count = 0;
        long bytes = 0;
        foreach (var f in files)
        {
            try
            {
                if (CredentialScan.IsCredentialFile(f.Full))
                {
                    excluded.Add(new BundleExclusion(prefix + f.Relative, CredentialScan.Reason));
                    continue;
                }
                if (!File.Exists(f.Full)) continue;
                zip.CreateEntryFromFile(f.Full, prefix + f.Relative);
                bytes += new FileInfo(f.Full).Length;
                count++;
            }
            catch { /* one unreadable file must not cost the whole archive */ }
        }
        return (count, bytes);
    }
}
